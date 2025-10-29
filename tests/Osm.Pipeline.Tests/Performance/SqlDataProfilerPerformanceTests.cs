using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.Tests.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests.Performance;

public sealed class SqlDataProfilerPerformanceTests
{
    [Fact]
    public async Task CaptureAsync_ShouldRespectConcurrencyGate_ForLargeEntityGrid()
    {
        var definition = PerformanceModelFactory.CreateEntityGridModel(entityCount: 500, attributeCount: 10, rowCountPerEntity: 10_000);
        var options = SqlProfilerOptions.Default with { MaxConcurrentTableProfiles = 8 };
        var metadataLoader = new SyntheticMetadataLoader(definition.Metadata, definition.RowCounts);
        var planBuilder = new ProfilingPlanBuilder(definition.Model, options.NamingOverrides);
        var executor = new RecordingQueryExecutor(options, TimeSpan.FromMilliseconds(5));
        var profiler = new SqlDataProfiler(
            new NullConnectionFactory(),
            definition.Model,
            options,
            metadataLoader,
            planBuilder,
            executor);

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(definition.RowCounts.Count, executor.ProbeRecords.Count);

        var expectedGate = Math.Min(options.MaxConcurrentTableProfiles, definition.RowCounts.Count);
        Assert.True(executor.MaxConcurrency <= options.MaxConcurrentTableProfiles);
        Assert.True(executor.MaxConcurrency >= expectedGate);
        Assert.All(executor.ProbeRecords, record => Assert.False(record.Sampled));
    }

    [Fact]
    public async Task CaptureAsync_ShouldHonorSamplingThreshold_ForWideTable()
    {
        var definition = PerformanceModelFactory.CreateWideTableModel(columnCount: 512, rowCount: 1_000_000);
        var options = SqlProfilerOptions.Default with { MaxConcurrentTableProfiles = 2 };
        var metadataLoader = new SyntheticMetadataLoader(definition.Metadata, definition.RowCounts);
        var planBuilder = new ProfilingPlanBuilder(definition.Model, options.NamingOverrides);
        var executor = new RecordingQueryExecutor(options, TimeSpan.Zero);
        var profiler = new SqlDataProfiler(
            new NullConnectionFactory(),
            definition.Model,
            options,
            metadataLoader,
            planBuilder,
            executor);

        var result = await profiler.CaptureAsync();

        Assert.True(result.IsSuccess);

        var record = Assert.Single(executor.ProbeRecords);
        Assert.True(record.Sampled);
        var maxRows = options.Limits.MaxRowsPerTable ?? long.MaxValue;
        var expectedSample = Math.Min(options.Sampling.SampleSize, (int)Math.Min(maxRows, record.RowCount));
        Assert.Equal(expectedSample, record.SampleSize);
    }

    private sealed class SyntheticMetadataLoader : ITableMetadataLoader
    {
        private readonly IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> _metadata;
        private readonly IReadOnlyDictionary<(string Schema, string Table), long> _rowCounts;

        public SyntheticMetadataLoader(
            IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
            IReadOnlyDictionary<(string Schema, string Table), long> rowCounts)
        {
            _metadata = metadata;
            _rowCounts = rowCounts;
        }

        public Task<Dictionary<(string Schema, string Table, string Column), ColumnMetadata>> LoadColumnMetadataAsync(
            System.Data.Common.DbConnection connection,
            IReadOnlyCollection<(string Schema, string Table)> tables,
            CancellationToken cancellationToken)
        {
            var builder = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance);
            foreach (var table in tables)
            {
                foreach (var (key, value) in _metadata)
                {
                    if (TableKeyComparer.Instance.Equals((key.Schema, key.Table), table))
                    {
                        builder[key] = value;
                    }
                }
            }

            return Task.FromResult(builder);
        }

        public Task<Dictionary<(string Schema, string Table), long>> LoadRowCountsAsync(
            System.Data.Common.DbConnection connection,
            IReadOnlyCollection<(string Schema, string Table)> tables,
            CancellationToken cancellationToken)
        {
            var builder = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance);
            foreach (var table in tables)
            {
                if (_rowCounts.TryGetValue(table, out var value))
                {
                    builder[table] = value;
                }
            }

            return Task.FromResult(builder);
        }
    }

    private sealed class RecordingQueryExecutor : IProfilingQueryExecutor
    {
        private readonly SqlProfilerOptions _options;
        private readonly TimeSpan _delay;
        private readonly List<ProbeRecord> _records = new();
        private int _inFlight;
        private int _maxConcurrency;

        public RecordingQueryExecutor(SqlProfilerOptions options, TimeSpan delay)
        {
            _options = options;
            _delay = delay;
        }

        public IReadOnlyList<ProbeRecord> ProbeRecords
        {
            get
            {
                lock (_records)
                {
                    return _records.ToImmutableArray();
                }
            }
        }

        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

        public async Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken)
        {
            if (plan is null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var current = Interlocked.Increment(ref _inFlight);
            UpdateMaxConcurrency(current);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                stopwatch.Stop();
                var shouldSample = TableSamplingPolicy.ShouldSample(plan.RowCount, _options);
                var sampleSize = TableSamplingPolicy.DetermineSampleSize(plan.RowCount, _options);
                var record = new ProbeRecord(plan.Schema, plan.Table, plan.RowCount, shouldSample, (int)sampleSize, stopwatch.Elapsed);

                lock (_records)
                {
                    _records.Add(record);
                }

                Interlocked.Decrement(ref _inFlight);
            }

            return TableProfilingResults.Empty;
        }

        private void UpdateMaxConcurrency(int current)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrency);
                if (observed >= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrency, current, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class NullConnectionFactory : IDbConnectionFactory
    {
        public Task<System.Data.Common.DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<System.Data.Common.DbConnection>(RecordingDbConnection.WithResultSets());
        }
    }

    public sealed record ProbeRecord(
        string Schema,
        string Table,
        long RowCount,
        bool Sampled,
        int SampleSize,
        TimeSpan Duration);
}
