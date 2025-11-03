using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Profiling;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

internal sealed class ProfilingQueryExecutor : IProfilingQueryExecutor
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly SqlProfilerOptions _options;
    private readonly SqlMetadataLog? _metadataLog;

    private readonly NullCountQueryBuilder _nullCountQueryBuilder;
    private readonly NullRowSampleQueryBuilder _nullRowSampleQueryBuilder;
    private readonly UniqueCandidateQueryBuilder _uniqueCandidateQueryBuilder;
    private readonly ForeignKeyProbeQueryBuilder _foreignKeyProbeQueryBuilder;
    private readonly IProfilingProbePolicy _probePolicy;

    public ProfilingQueryExecutor(
        IDbConnectionFactory connectionFactory,
        SqlProfilerOptions options,
        SqlMetadataLog? metadataLog = null,
        NullCountQueryBuilder? nullCountQueryBuilder = null,
        UniqueCandidateQueryBuilder? uniqueCandidateQueryBuilder = null,
        ForeignKeyProbeQueryBuilder? foreignKeyProbeQueryBuilder = null,
        IProfilingProbePolicy? probePolicy = null,
        TimeProvider? timeProvider = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metadataLog = metadataLog;
        _nullCountQueryBuilder = nullCountQueryBuilder ?? new NullCountQueryBuilder();
        _nullRowSampleQueryBuilder = new NullRowSampleQueryBuilder();
        _uniqueCandidateQueryBuilder = uniqueCandidateQueryBuilder ?? new UniqueCandidateQueryBuilder();
        _foreignKeyProbeQueryBuilder = foreignKeyProbeQueryBuilder ?? new ForeignKeyProbeQueryBuilder();
        var provider = timeProvider ?? TimeProvider.System;
        _probePolicy = probePolicy ?? new ProfilingProbePolicy(provider);
    }

    public async Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var tableCancellation = CreateTableCancellationSource(cancellationToken);

        var shouldSample = TableSamplingPolicy.ShouldSample(plan.RowCount, _options);
        var samplingParameter = shouldSample ? TableSamplingPolicy.GetSampleSize(plan.RowCount, _options) : 0;
        var sampleSize = shouldSample ? (long)samplingParameter : Math.Max(0L, plan.RowCount);

        var nullCounts = await _probePolicy.ExecuteAsync(
            ct => ComputeNullCountsAsync(connection, plan, shouldSample, samplingParameter, ct),
            BuildConservativeNullCounts(plan),
            sampleSize,
            tableCancellation,
            cancellationToken).ConfigureAwait(false);

        var nullCountStatuses = BuildStatusDictionary(nullCounts.Value.Keys, nullCounts.Status);

        // Capture NULL row samples for columns with nulls
        var nullRowSamples = await ComputeNullRowSamplesAsync(connection, plan, nullCounts.Value, cancellationToken).ConfigureAwait(false);

        var duplicateFlags = await _probePolicy.ExecuteAsync(
            ct => ComputeDuplicateCandidatesAsync(connection, plan, shouldSample, samplingParameter, ct),
            BuildConservativeUniqueResults(plan),
            sampleSize,
            tableCancellation,
            cancellationToken).ConfigureAwait(false);

        var duplicateStatuses = BuildStatusDictionary(duplicateFlags.Value.Keys, duplicateFlags.Status);

        var foreignKeyFallback = (
            Orphans: BuildConservativeForeignKeyResults(plan),
            IsNoCheck: BuildConservativeForeignKeyNoCheckResults(plan));

        var foreignKeyReality = await _probePolicy.ExecuteAsync(
            ct => ComputeForeignKeyRealityAsync(connection, plan, shouldSample, samplingParameter, ct),
            foreignKeyFallback,
            sampleSize,
            tableCancellation,
            cancellationToken).ConfigureAwait(false);

        var foreignKeyStatuses = BuildStatusDictionary(foreignKeyReality.Value.Orphans.Keys, foreignKeyReality.Status);
        var foreignKeyNoCheckStatuses = BuildStatusDictionary(foreignKeyReality.Value.IsNoCheck.Keys, foreignKeyReality.Status);

        return new TableProfilingResults(
            nullCounts.Value,
            nullCountStatuses,
            duplicateFlags.Value,
            duplicateStatuses,
            foreignKeyReality.Value.Orphans,
            foreignKeyStatuses,
            foreignKeyReality.Value.IsNoCheck,
            foreignKeyNoCheckStatuses,
            nullRowSamples);
    }

    private async Task<IReadOnlyDictionary<string, long>> ComputeNullCountsAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        bool useSampling,
        int samplingParameter,
        CancellationToken cancellationToken)
    {
        if (plan.Columns.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, long>.Empty;
        }

        if (plan.RowCount <= 0)
        {
            var zeroBuilder = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in plan.Columns)
            {
                zeroBuilder[column] = 0L;
            }

            return zeroBuilder.ToImmutable();
        }

        await using var command = connection.CreateCommand();
        _nullCountQueryBuilder.Configure(command, plan, useSampling, samplingParameter);

        ApplyCommandTimeout(command);

        var results = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var column = reader.GetString(0);
            var nullCount = reader.GetInt64(1);
            results[column] = nullCount;
        }

        foreach (var column in plan.Columns)
        {
            if (!results.ContainsKey(column))
            {
                results[column] = 0L;
            }
        }

        var dictionary = results.ToImmutable();
        _metadataLog?.RecordRequest(
            "sql.nullCounts",
            new
            {
                schema = plan.Schema,
                table = plan.Table,
                rowCount = plan.RowCount,
                results = dictionary
                    .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new { column = entry.Key, nullCount = entry.Value })
                    .ToArray()
            });

        return dictionary;
    }

    private async Task<IReadOnlyDictionary<string, bool>> ComputeDuplicateCandidatesAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        bool useSampling,
        int samplingParameter,
        CancellationToken cancellationToken)
    {
        if (plan.UniqueCandidates.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, bool>.Empty;
        }

        await using var command = connection.CreateCommand();
        _uniqueCandidateQueryBuilder.Configure(command, plan, useSampling, samplingParameter);

        ApplyCommandTimeout(command);

        var results = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var hasDuplicates = !reader.IsDBNull(1) && reader.GetBoolean(1);
            results[key] = hasDuplicates;
        }

        foreach (var candidate in plan.UniqueCandidates)
        {
            if (!results.ContainsKey(candidate.Key))
            {
                results[candidate.Key] = false;
            }
        }

        var dictionary = results.ToImmutable();
        _metadataLog?.RecordRequest(
            "sql.uniqueCandidates",
            new
            {
                schema = plan.Schema,
                table = plan.Table,
                rowCount = plan.RowCount,
                useSampling,
                results = dictionary
                    .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new { candidate = entry.Key, hasDuplicates = entry.Value })
                    .ToArray()
            });

        return dictionary;
    }

    private async Task<(IReadOnlyDictionary<string, bool> Orphans, IReadOnlyDictionary<string, bool> IsNoCheck)> ComputeForeignKeyRealityAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        bool useSampling,
        int samplingParameter,
        CancellationToken cancellationToken)
    {
        if (plan.ForeignKeys.IsDefaultOrEmpty)
        {
            return (ImmutableDictionary<string, bool>.Empty, ImmutableDictionary<string, bool>.Empty);
        }

        await using var command = connection.CreateCommand();
        _foreignKeyProbeQueryBuilder.ConfigureRealityCommand(command, plan, useSampling, samplingParameter);

        ApplyCommandTimeout(command);

        var results = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var hasOrphans = !reader.IsDBNull(1) && reader.GetBoolean(1);
            results[key] = hasOrphans;
        }

        foreach (var candidate in plan.ForeignKeys)
        {
            if (!results.ContainsKey(candidate.Key))
            {
                results[candidate.Key] = false;
            }
        }

        var dictionary = results.ToImmutable();
        var metadata = await LoadForeignKeyMetadataAsync(connection, plan, cancellationToken).ConfigureAwait(false);

        _metadataLog?.RecordRequest(
            "sql.foreignKeyReality",
            new
            {
                schema = plan.Schema,
                table = plan.Table,
                rowCount = plan.RowCount,
                useSampling,
                orphans = dictionary
                    .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new { candidate = entry.Key, hasOrphans = entry.Value })
                    .ToArray()
            });

        return (dictionary, metadata);
    }

    private async Task<IReadOnlyDictionary<string, bool>> LoadForeignKeyMetadataAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        _foreignKeyProbeQueryBuilder.ConfigureMetadataCommand(command, plan.Schema, plan.Table);

        ApplyCommandTimeout(command);

        var results = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var column = reader.GetString(0);
            var targetSchema = reader.GetString(1);
            var targetTable = reader.GetString(2);
            var targetColumn = reader.GetString(3);
            var isNotTrusted = !reader.IsDBNull(4) && reader.GetBoolean(4);
            var isDisabled = !reader.IsDBNull(5) && reader.GetBoolean(5);
            var key = ProfilingPlanBuilder.BuildForeignKeyKey(column, targetSchema, targetTable, targetColumn);
            results[key] = isNotTrusted || isDisabled;
        }

        foreach (var candidate in plan.ForeignKeys)
        {
            if (!results.ContainsKey(candidate.Key))
            {
                results[candidate.Key] = false;
            }
        }

        var dictionary = results.ToImmutable();
        _metadataLog?.RecordRequest(
            "sql.foreignKeyMetadata",
            dictionary
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new { candidate = entry.Key, isNotTrustedOrDisabled = entry.Value })
                .ToArray());

        return dictionary;
    }

    private static IReadOnlyDictionary<string, ProfilingProbeStatus> BuildStatusDictionary(
        IEnumerable<string> keys,
        ProfilingProbeStatus status)
    {
        if (status is null)
        {
            return ImmutableDictionary<string, ProfilingProbeStatus>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, ProfilingProbeStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            builder[key] = status;
        }

        return builder.ToImmutable();
    }

    private CancellationTokenSource? CreateTableCancellationSource(CancellationToken cancellationToken)
    {
        if (!_options.Limits.TableTimeout.HasValue)
        {
            return null;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_options.Limits.TableTimeout.Value);
        return source;
    }

    private void ApplyCommandTimeout(DbCommand command)
    {
        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }
    }

    private static IReadOnlyDictionary<string, long> BuildConservativeNullCounts(TableProfilingPlan plan)
    {
        if (plan.Columns.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, long>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in plan.Columns)
        {
            builder[column] = plan.RowCount;
        }

        return builder.ToImmutable();
    }

    private static IReadOnlyDictionary<string, bool> BuildConservativeUniqueResults(TableProfilingPlan plan)
    {
        if (plan.UniqueCandidates.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, bool>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in plan.UniqueCandidates)
        {
            builder[candidate.Key] = true;
        }

        return builder.ToImmutable();
    }

    private static IReadOnlyDictionary<string, bool> BuildConservativeForeignKeyResults(TableProfilingPlan plan)
    {
        if (plan.ForeignKeys.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, bool>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in plan.ForeignKeys)
        {
            builder[candidate.Key] = true;
        }

        return builder.ToImmutable();
    }

    private static IReadOnlyDictionary<string, bool> BuildConservativeForeignKeyNoCheckResults(TableProfilingPlan plan)
    {
        if (plan.ForeignKeys.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, bool>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in plan.ForeignKeys)
        {
            builder[candidate.Key] = true;
        }

        return builder.ToImmutable();
    }

    private async Task<IReadOnlyDictionary<string, NullRowSample>> ComputeNullRowSamplesAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        IReadOnlyDictionary<string, long> nullCounts,
        CancellationToken cancellationToken)
    {
        var results = ImmutableDictionary.CreateBuilder<string, NullRowSample>(StringComparer.OrdinalIgnoreCase);

        // Only capture samples for columns with nulls
        foreach (var column in plan.Columns)
        {
            if (!nullCounts.TryGetValue(column, out var nullCount) || nullCount == 0)
            {
                continue;
            }

            // Skip if no primary key columns available
            if (plan.PrimaryKeyColumns.IsDefaultOrEmpty)
            {
                results[column] = NullRowSample.Empty;
                continue;
            }

            try
            {
                await using var command = connection.CreateCommand();
                _nullRowSampleQueryBuilder.Configure(command, plan.Schema, plan.Table, column, plan.PrimaryKeyColumns);
                ApplyCommandTimeout(command);

                var sampleRows = ImmutableArray.CreateBuilder<NullRowIdentifier>();

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var pkValues = ImmutableArray.CreateBuilder<object?>();
                    for (var i = 0; i < plan.PrimaryKeyColumns.Length; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        pkValues.Add(value);
                    }

                    sampleRows.Add(new NullRowIdentifier(pkValues.ToImmutable()));
                }

                results[column] = NullRowSample.Create(plan.PrimaryKeyColumns, sampleRows.ToImmutable(), nullCount);
            }
            catch (DbException)
            {
                // If we fail to capture NULL row samples, just use empty sample
                results[column] = NullRowSample.Empty;
            }
        }

        return results.ToImmutable();
    }

}
