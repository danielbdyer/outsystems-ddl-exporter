using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class SqlDataProfilerOrchestrationTests
{
    [Fact]
    public async Task CaptureAsync_ComposesMetadataPlansAndQueryResults()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var expected = ProfileFixtures.LoadSnapshot("profiling/profile.micro-unique.json");

        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [("dbo", "OSUSR_U_USER", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_U_USER", "EMAIL")] = new ColumnMetadata(true, false, false, null)
        };
        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance)
        {
            [("dbo", "OSUSR_U_USER")] = 100
        };

        var plan = new TableProfilingPlan(
            "dbo",
            "OSUSR_U_USER",
            100,
            ImmutableArray.Create("EMAIL", "ID"),
            ImmutableArray.Create(new UniqueCandidatePlan("email", ImmutableArray.Create("EMAIL"))),
            ImmutableArray<ForeignKeyPlan>.Empty);

        var results = new TableProfilingResults(
            new Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = 0,
                ["EMAIL"] = 0
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = false
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase));

        var metadataLoader = new StubMetadataLoader(metadata, rowCounts);
        var planBuilder = new StubPlanBuilder(plan);
        var queryExecutor = new StubQueryExecutor(results);
        var profiler = new SqlDataProfiler(new NullConnectionFactory(), model, SqlProfilerOptions.Default, metadataLoader, planBuilder, queryExecutor);

        var snapshot = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(snapshot.IsSuccess, string.Join(", ", snapshot.Errors.Select(e => e.Code)));
        Assert.Equal(expected.Columns, snapshot.Value.Columns);
        Assert.Equal(expected.UniqueCandidates, snapshot.Value.UniqueCandidates);
        Assert.Equal(expected.CompositeUniqueCandidates, snapshot.Value.CompositeUniqueCandidates);
        Assert.Equal(expected.ForeignKeys, snapshot.Value.ForeignKeys);
    }

    private sealed class StubMetadataLoader : ITableMetadataLoader
    {
        private readonly Dictionary<(string Schema, string Table, string Column), ColumnMetadata> _metadata;
        private readonly Dictionary<(string Schema, string Table), long> _rowCounts;

        public StubMetadataLoader(
            Dictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
            Dictionary<(string Schema, string Table), long> rowCounts)
        {
            _metadata = metadata;
            _rowCounts = rowCounts;
        }

        public Task<Dictionary<(string Schema, string Table, string Column), ColumnMetadata>> LoadColumnMetadataAsync(DbConnection connection, IReadOnlyCollection<(string Schema, string Table)> tables, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(_metadata, ColumnKeyComparer.Instance));
        }

        public Task<Dictionary<(string Schema, string Table), long>> LoadRowCountsAsync(DbConnection connection, IReadOnlyCollection<(string Schema, string Table)> tables, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Dictionary<(string Schema, string Table), long>(_rowCounts, TableKeyComparer.Instance));
        }
    }

    private sealed class StubPlanBuilder : IProfilingPlanBuilder
    {
        private readonly TableProfilingPlan _plan;

        public StubPlanBuilder(TableProfilingPlan plan)
        {
            _plan = plan;
        }

        public Dictionary<(string Schema, string Table), TableProfilingPlan> BuildPlans(
            IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
            IReadOnlyDictionary<(string Schema, string Table), long> rowCounts)
        {
            return new Dictionary<(string Schema, string Table), TableProfilingPlan>(TableKeyComparer.Instance)
            {
                [("dbo", "OSUSR_U_USER")] = _plan
            };
        }
    }

    private sealed class StubQueryExecutor : IProfilingQueryExecutor
    {
        private readonly TableProfilingResults _results;

        public StubQueryExecutor(TableProfilingResults results)
        {
            _results = results;
        }

        public Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken)
        {
            return Task.FromResult(_results);
        }
    }

    private sealed class NullConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<DbConnection>(RecordingDbConnection.WithResultSets());
        }
    }
}
