using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

public sealed class SqlDataProfiler : IDataProfiler
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly OsmModel _model;
    private readonly SqlProfilerOptions _options;

    public SqlDataProfiler(IDbConnectionFactory connectionFactory, OsmModel model, SqlProfilerOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _options = options ?? SqlProfilerOptions.Default;
    }

    public async Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var tables = CollectTables();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            var metadata = await LoadColumnMetadataAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var rowCounts = await LoadRowCountsAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var plans = BuildProfilingPlans(metadata, rowCounts);
            var resultsLookup = new ConcurrentDictionary<(string Schema, string Table), TableProfilingResults>(TableKeyComparer.Instance);

            using var gate = new SemaphoreSlim(_options.MaxConcurrentTableProfiles);
            var tasks = new List<Task>(plans.Count);
            foreach (var plan in plans.Values)
            {
                tasks.Add(ProfileTableWithGateAsync(plan, resultsLookup, gate, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var columnProfiles = new List<ColumnProfile>();
            var uniqueProfiles = new List<UniqueCandidateProfile>();
            var compositeProfiles = new List<CompositeUniqueCandidateProfile>();
            var foreignKeys = new List<ForeignKeyReality>();

            foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
            {
                var schema = entity.Schema.Value;
                var table = entity.PhysicalName.Value;
                var tableKey = (schema, table);
                rowCounts.TryGetValue(tableKey, out var tableRowCount);
                var tableResults = resultsLookup.TryGetValue(tableKey, out var computed)
                    ? computed
                    : TableProfilingResults.Empty;

                foreach (var attribute in entity.Attributes)
                {
                    var columnName = attribute.ColumnName.Value;
                    var columnKey = (schema, table, columnName);
                    if (!metadata.TryGetValue(columnKey, out var meta))
                    {
                        continue;
                    }

                    var nullCount = tableResults.NullCounts.TryGetValue(columnName, out var value) ? value : 0L;

                    var columnProfileResult = ColumnProfile.Create(
                        SchemaName.Create(schema).Value,
                        TableName.Create(table).Value,
                        ColumnName.Create(columnName).Value,
                        meta.IsNullable,
                        meta.IsComputed,
                        meta.IsPrimaryKey,
                        IsSingleColumnUnique(entity, columnName),
                        meta.DefaultDefinition,
                        tableRowCount,
                        nullCount);

                    if (columnProfileResult.IsSuccess)
                    {
                        columnProfiles.Add(columnProfileResult.Value);
                    }
                }

                foreach (var index in entity.Indexes.Where(static idx => idx.IsUnique))
                {
                    var orderedColumns = index.Columns
                        .OrderBy(static column => column.Ordinal)
                        .Select(static column => column.Column.Value)
                        .ToArray();

                    if (orderedColumns.Length == 0)
                    {
                        continue;
                    }

                    var candidateKey = BuildUniqueKey(orderedColumns);
                    var hasDuplicates = tableResults.UniqueDuplicates.TryGetValue(candidateKey, out var duplicate) && duplicate;

                    if (orderedColumns.Length == 1)
                    {
                        var profileResult = UniqueCandidateProfile.Create(
                            SchemaName.Create(schema).Value,
                            TableName.Create(table).Value,
                            ColumnName.Create(orderedColumns[0]).Value,
                            hasDuplicates);

                        if (profileResult.IsSuccess)
                        {
                            uniqueProfiles.Add(profileResult.Value);
                        }
                    }
                    else
                    {
                        var columns = orderedColumns
                            .Select(name => ColumnName.Create(name).Value)
                            .ToImmutableArray();
                        var profileResult = CompositeUniqueCandidateProfile.Create(
                            SchemaName.Create(schema).Value,
                            TableName.Create(table).Value,
                            columns,
                            hasDuplicates);

                        if (profileResult.IsSuccess)
                        {
                            compositeProfiles.Add(profileResult.Value);
                        }
                    }
                }

                foreach (var attribute in entity.Attributes)
                {
                    if (!attribute.Reference.IsReference || attribute.Reference.TargetEntity is null)
                    {
                        continue;
                    }

                    var targetName = attribute.Reference.TargetEntity.Value;
                    if (!TryFindEntity(targetName, out var targetEntity))
                    {
                        continue;
                    }

                    var targetIdentifier = GetPreferredIdentifier(targetEntity);
                    if (targetIdentifier is null)
                    {
                        continue;
                    }

                    var foreignKeyKey = BuildForeignKeyKey(
                        attribute.ColumnName.Value,
                        targetEntity.Schema.Value,
                        targetEntity.PhysicalName.Value,
                        targetIdentifier.ColumnName.Value);

                    var hasOrphans = tableResults.ForeignKeys.TryGetValue(foreignKeyKey, out var orphaned) && orphaned;

                    var referenceResult = ForeignKeyReference.Create(
                        SchemaName.Create(schema).Value,
                        TableName.Create(table).Value,
                        ColumnName.Create(attribute.ColumnName.Value).Value,
                        SchemaName.Create(targetEntity.Schema.Value).Value,
                        TableName.Create(targetEntity.PhysicalName.Value).Value,
                        ColumnName.Create(targetIdentifier.ColumnName.Value).Value,
                        attribute.Reference.HasDatabaseConstraint);

                    if (referenceResult.IsFailure)
                    {
                        continue;
                    }

                    var realityResult = ForeignKeyReality.Create(referenceResult.Value, hasOrphans, isNoCheck: false);
                    if (realityResult.IsSuccess)
                    {
                        foreignKeys.Add(realityResult.Value);
                    }
                }
            }

            return ProfileSnapshot.Create(columnProfiles, uniqueProfiles, compositeProfiles, foreignKeys);
        }
        catch (DbException ex)
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create(
                "profile.sql.executionFailed",
                $"Failed to capture profiling snapshot: {ex.Message}"));
        }
    }

    private IReadOnlyCollection<(string Schema, string Table)> CollectTables()
    {
        var tables = new HashSet<(string Schema, string Table)>(TableKeyComparer.Instance);
        foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
        {
            tables.Add((entity.Schema.Value, entity.PhysicalName.Value));
        }

        return tables;
    }

    private async Task<Dictionary<(string Schema, string Table, string Column), ColumnMetadata>> LoadColumnMetadataAsync(
        DbConnection connection,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance);

        if (tables.Count == 0)
        {
            return metadata;
        }

        var command = connection.CreateCommand();
        var filterClause = BuildTableFilterClause(command, tables, "s.name", "t.name");
        command.CommandText = @$"SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    c.name AS ColumnName,
    c.is_nullable,
    c.is_computed,
    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
    dc.definition AS DefaultDefinition
FROM sys.columns AS c
JOIN sys.tables AS t ON c.object_id = t.object_id
JOIN sys.schemas AS s ON t.schema_id = s.schema_id
LEFT JOIN sys.default_constraints AS dc ON c.default_object_id = dc.object_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) AS pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
WHERE t.is_ms_shipped = 0 AND {filterClause};";

        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            if (!tables.Contains((schema, table)))
            {
                continue;
            }

            var column = reader.GetString(2);
            var isNullable = reader.GetBoolean(3);
            var isComputed = reader.GetBoolean(4);
            var isPrimaryKey = reader.GetInt32(5) == 1;
            var defaultDefinition = reader.IsDBNull(6) ? null : reader.GetString(6);

            metadata[(schema, table, column)] = new ColumnMetadata(isNullable, isComputed, isPrimaryKey, defaultDefinition);
        }

        return metadata;
    }

    private async Task<Dictionary<(string Schema, string Table), long>> LoadRowCountsAsync(
        DbConnection connection,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance);

        if (tables.Count == 0)
        {
            return counts;
        }

        var command = connection.CreateCommand();
        var filterClause = BuildTableFilterClause(command, tables, "s.name", "t.name");
        command.CommandText = @$"SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    SUM(p.rows) AS [RowCount]
FROM sys.tables AS t
JOIN sys.schemas AS s ON t.schema_id = s.schema_id
JOIN sys.dm_db_partition_stats AS p ON t.object_id = p.object_id
WHERE p.index_id IN (0,1) AND {filterClause}
GROUP BY s.name, t.name;";

        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            if (!tables.Contains((schema, table)))
            {
                continue;
            }

            var count = reader.GetInt64(2);
            counts[(schema, table)] = count;
        }

        return counts;
    }

    private async Task ProfileTableWithGateAsync(
        TableProfilingPlan plan,
        ConcurrentDictionary<(string Schema, string Table), TableProfilingResults> resultsLookup,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var results = await ProfileTableAsync(plan, cancellationToken).ConfigureAwait(false);
            resultsLookup[(plan.Schema, plan.Table)] = results;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TableProfilingResults> ProfileTableAsync(TableProfilingPlan plan, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        using var tableCancellation = CreateTableCancellationSource(cancellationToken);

        var nullCounts = await ExecuteWithTimeoutFallback(
            ct => ComputeNullCountsAsync(connection, plan, ct),
            BuildConservativeNullCounts(plan),
            tableCancellation,
            cancellationToken).ConfigureAwait(false);

        var duplicateFlags = await ExecuteWithTimeoutFallback(
            ct => ComputeDuplicateCandidatesAsync(connection, plan, ct),
            BuildConservativeUniqueResults(plan),
            tableCancellation,
            cancellationToken).ConfigureAwait(false);

        var orphanFlags = await ExecuteWithTimeoutFallback(
            ct => ComputeForeignKeyRealityAsync(connection, plan, ct),
            BuildConservativeForeignKeyResults(plan),
            tableCancellation,
            cancellationToken).ConfigureAwait(false);

        return new TableProfilingResults(nullCounts, duplicateFlags, orphanFlags);
    }

    private bool ShouldSample(long rowCount)
    {
        if (rowCount <= 0)
        {
            return false;
        }

        var threshold = _options.Sampling.RowCountSamplingThreshold;
        if (_options.Limits.MaxRowsPerTable.HasValue)
        {
            threshold = Math.Min(threshold, _options.Limits.MaxRowsPerTable.Value);
        }

        return rowCount > threshold;
    }

    private int GetSampleSize(long rowCount)
    {
        var sample = (long)_options.Sampling.SampleSize;
        if (_options.Limits.MaxRowsPerTable.HasValue)
        {
            sample = Math.Min(sample, _options.Limits.MaxRowsPerTable.Value);
        }

        if (rowCount > 0)
        {
            sample = Math.Min(sample, rowCount);
        }

        sample = Math.Clamp(sample, 1, (long)int.MaxValue);
        return (int)sample;
    }

    private async Task<T> ExecuteWithTimeoutFallback<T>(
        Func<CancellationToken, Task<T>> operation,
        T fallback,
        CancellationTokenSource? tableCancellation,
        CancellationToken originalToken)
    {
        try
        {
            var token = tableCancellation?.Token ?? originalToken;
            return await operation(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsTableTimeout(tableCancellation, originalToken))
        {
            return fallback;
        }
        catch (DbException ex) when (IsTimeoutException(ex))
        {
            return fallback;
        }
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

    private static bool IsTableTimeout(CancellationTokenSource? tableCancellation, CancellationToken originalToken)
    {
        return tableCancellation is not null && tableCancellation.IsCancellationRequested && !originalToken.IsCancellationRequested;
    }

    private static bool IsTimeoutException(DbException exception)
    {
        if (exception is SqlException sqlException)
        {
            foreach (SqlError error in sqlException.Errors)
            {
                if (error.Number == -2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<IReadOnlyDictionary<string, long>> ComputeNullCountsAsync(
        DbConnection connection,
        TableProfilingPlan plan,
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

        var useSampling = ShouldSample(plan.RowCount);
        var sampleSize = GetSampleSize(plan.RowCount);
        await using var command = connection.CreateCommand();
        var columnList = plan.Columns.Select(QuoteIdentifier).ToArray();
        var aliasNames = plan.Columns.Select((_, index) => $"NullCount{index}").ToArray();
        command.CommandText = BuildNullCountSql(plan.Schema, plan.Table, columnList, aliasNames, useSampling);

        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = sampleSize;
            command.Parameters.Add(parameter);
        }

        for (var i = 0; i < plan.Columns.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@column{i}";
            parameter.DbType = DbType.String;
            parameter.Value = plan.Columns[i];
            command.Parameters.Add(parameter);
        }

        var results = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var columnName = reader.GetString(0);
            var nullCount = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            var sampleCount = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);

            if (useSampling && sampleCount > 0 && sampleCount < plan.RowCount)
            {
                var estimated = (long)Math.Round(nullCount * (plan.RowCount / (double)sampleCount), MidpointRounding.AwayFromZero);
                nullCount = Math.Min(plan.RowCount, Math.Max(0, estimated));
            }
            else if (useSampling && sampleCount == 0)
            {
                nullCount = 0;
            }

            results[columnName] = nullCount;
        }

        foreach (var column in plan.Columns)
        {
            if (!results.ContainsKey(column))
            {
                results[column] = 0L;
            }
        }

        return results.ToImmutable();
    }

    private static string BuildNullCountSql(
        string schema,
        string table,
        IReadOnlyList<string> quotedColumns,
        IReadOnlyList<string> aliases,
        bool useSampling)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", quotedColumns));
        builder.AppendLine();
        builder.Append("    FROM ").Append(QualifyIdentifier(schema, table)).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }

        builder.AppendLine(")");
        builder.AppendLine("SELECT v.ColumnName, v.NullCount, a.SampleCount");
        builder.AppendLine("FROM (");
        builder.Append("    SELECT ");
        for (var i = 0; i < aliases.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append("SUM(CASE WHEN ").Append(quotedColumns[i]).Append(" IS NULL THEN 1 ELSE 0 END) AS ").Append(aliases[i]);
        }

        if (aliases.Count > 0)
        {
            builder.Append(", ");
        }

        builder.AppendLine("COUNT_BIG(*) AS SampleCount");
        builder.AppendLine("    FROM Source");
        builder.AppendLine(") AS a");
        builder.Append("CROSS APPLY (VALUES ");
        for (var i = 0; i < aliases.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append($"(@column{i}, a.{aliases[i]})");
        }

        builder.AppendLine(") AS v(ColumnName, NullCount);");
        return builder.ToString();
    }

    private async Task<IReadOnlyDictionary<string, bool>> ComputeDuplicateCandidatesAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.UniqueCandidates.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, bool>.Empty;
        }

        var candidates = plan.UniqueCandidates.Where(static candidate => !candidate.Columns.IsDefaultOrEmpty).ToImmutableArray();
        if (candidates.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, bool>.Empty;
        }

        var columnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            foreach (var column in candidate.Columns)
            {
                columnSet.Add(column);
            }
        }

        var useSampling = ShouldSample(plan.RowCount);
        await using var command = connection.CreateCommand();
        command.CommandText = BuildUniqueCandidatesSql(plan.Schema, plan.Table, columnSet, candidates, useSampling, command);

        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = GetSampleSize(plan.RowCount);
            command.Parameters.Add(parameter);
        }

        var results = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var hasDuplicates = !reader.IsDBNull(1) && reader.GetBoolean(1);
            results[key] = hasDuplicates;
        }

        foreach (var candidate in candidates)
        {
            if (!results.ContainsKey(candidate.Key))
            {
                results[candidate.Key] = false;
            }
        }

        return results.ToImmutable();
    }

    private static string BuildUniqueCandidatesSql(
        string schema,
        string table,
        IEnumerable<string> columnSet,
        ImmutableArray<UniqueCandidatePlan> candidates,
        bool useSampling,
        DbCommand command)
    {
        var builder = new StringBuilder();
        var projectedColumns = columnSet.Select(QuoteIdentifier).ToArray();
        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", projectedColumns));
        builder.AppendLine();
        builder.Append("    FROM ").Append(QualifyIdentifier(schema, table)).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }

        builder.AppendLine(")");
        builder.AppendLine("SELECT CandidateId, HasDuplicates");
        builder.AppendLine("FROM (");
        for (var i = 0; i < candidates.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine("    UNION ALL");
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@candidate{i}";
            parameter.DbType = DbType.String;
            parameter.Value = candidates[i].Key;
            command.Parameters.Add(parameter);

            builder.Append("    SELECT ");
            builder.Append(parameter.ParameterName);
            builder.Append(" AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source GROUP BY ");
            builder.Append(string.Join(", ", candidates[i].Columns.Select(QuoteIdentifier)));
            builder.Append(" HAVING COUNT(*) > 1) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDuplicates");
            builder.AppendLine();
        }

        builder.AppendLine(") AS results;");
        return builder.ToString();
    }

    private async Task<IReadOnlyDictionary<string, bool>> ComputeForeignKeyRealityAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.ForeignKeys.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, bool>.Empty;
        }

        var useSampling = ShouldSample(plan.RowCount);
        await using var command = connection.CreateCommand();
        var columnSet = plan.ForeignKeys.Select(static fk => fk.Column).Distinct(StringComparer.OrdinalIgnoreCase).Select(QuoteIdentifier).ToArray();
        command.CommandText = BuildForeignKeySql(plan.Schema, plan.Table, columnSet, plan.ForeignKeys, useSampling, command);

        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = GetSampleSize(plan.RowCount);
            command.Parameters.Add(parameter);
        }

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

        return results.ToImmutable();
    }

    private static string BuildForeignKeySql(
        string schema,
        string table,
        IReadOnlyCollection<string> sourceColumns,
        ImmutableArray<ForeignKeyPlan> candidates,
        bool useSampling,
        DbCommand command)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", sourceColumns));
        builder.AppendLine();
        builder.Append("    FROM ").Append(QualifyIdentifier(schema, table)).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }

        builder.AppendLine(")");
        builder.AppendLine("SELECT CandidateId, HasOrphans");
        builder.AppendLine("FROM (");
        for (var i = 0; i < candidates.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine("    UNION ALL");
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@fk{i}";
            parameter.DbType = DbType.String;
            parameter.Value = candidates[i].Key;
            command.Parameters.Add(parameter);

            builder.Append("    SELECT ");
            builder.Append(parameter.ParameterName);
            builder.Append(" AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source AS source LEFT JOIN ");
            builder.Append(QualifyIdentifier(candidates[i].TargetSchema, candidates[i].TargetTable));
            builder.Append(" AS target WITH (NOLOCK) ON source.");
            builder.Append(QuoteIdentifier(candidates[i].Column));
            builder.Append(" = target.");
            builder.Append(QuoteIdentifier(candidates[i].TargetColumn));
            builder.Append(" WHERE source.");
            builder.Append(QuoteIdentifier(candidates[i].Column));
            builder.Append(" IS NOT NULL AND target.");
            builder.Append(QuoteIdentifier(candidates[i].TargetColumn));
            builder.Append(" IS NULL) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasOrphans");
            builder.AppendLine();
        }

        builder.AppendLine(") AS results;");
        return builder.ToString();
    }

    private Dictionary<(string Schema, string Table), TableProfilingPlan> BuildProfilingPlans(
        IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
        IReadOnlyDictionary<(string Schema, string Table), long> rowCounts)
    {
        var builders = new Dictionary<(string Schema, string Table), TableProfilingPlanBuilder>(TableKeyComparer.Instance);

        foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
        {
            var schema = entity.Schema.Value;
            var table = entity.PhysicalName.Value;
            var key = (schema, table);

            if (!builders.TryGetValue(key, out var builder))
            {
                builder = new TableProfilingPlanBuilder(schema, table);
                builders[key] = builder;
            }

            foreach (var attribute in entity.Attributes)
            {
                var columnName = attribute.ColumnName.Value;
                if (metadata.ContainsKey((schema, table, columnName)))
                {
                    builder.AddColumn(columnName);
                }

                if (attribute.Reference.IsReference && attribute.Reference.TargetEntity is not null)
                {
                    var targetName = attribute.Reference.TargetEntity.Value;
                    if (TryFindEntity(targetName, out var targetEntity))
                    {
                        var targetIdentifier = GetPreferredIdentifier(targetEntity);
                        if (targetIdentifier is not null)
                        {
                            builder.AddForeignKey(
                                columnName,
                                targetEntity.Schema.Value,
                                targetEntity.PhysicalName.Value,
                                targetIdentifier.ColumnName.Value);
                        }
                    }
                }
            }

            foreach (var index in entity.Indexes.Where(static idx => idx.IsUnique))
            {
                var orderedColumns = index.Columns
                    .OrderBy(static column => column.Ordinal)
                    .Select(static column => column.Column.Value)
                    .ToArray();

                if (orderedColumns.Length == 0)
                {
                    continue;
                }

                builder.AddUniqueCandidate(orderedColumns);
            }
        }

        var plans = new Dictionary<(string Schema, string Table), TableProfilingPlan>(builders.Count, TableKeyComparer.Instance);
        foreach (var kvp in builders)
        {
            var key = kvp.Key;
            rowCounts.TryGetValue(key, out var rowCount);
            plans[key] = kvp.Value.Build(rowCount);
        }

        return plans;
    }

    private static string BuildUniqueKey(IEnumerable<string> columns)
    {
        return string.Join("|", columns.Select(static column => column.ToLowerInvariant()));
    }

    private static string BuildForeignKeyKey(string column, string targetSchema, string targetTable, string targetColumn)
    {
        return string.Join("|", new[] { column, targetSchema, targetTable, targetColumn }.Select(static value => value.ToLowerInvariant()));
    }

    private sealed record TableProfilingPlan(
        string Schema,
        string Table,
        long RowCount,
        ImmutableArray<string> Columns,
        ImmutableArray<UniqueCandidatePlan> UniqueCandidates,
        ImmutableArray<ForeignKeyPlan> ForeignKeys);

    private sealed record UniqueCandidatePlan(string Key, ImmutableArray<string> Columns);

    private sealed record ForeignKeyPlan(string Key, string Column, string TargetSchema, string TargetTable, string TargetColumn);

    private sealed record TableProfilingResults(
        IReadOnlyDictionary<string, long> NullCounts,
        IReadOnlyDictionary<string, bool> UniqueDuplicates,
        IReadOnlyDictionary<string, bool> ForeignKeys)
    {
        public static TableProfilingResults Empty { get; } = new(
            ImmutableDictionary<string, long>.Empty,
            ImmutableDictionary<string, bool>.Empty,
            ImmutableDictionary<string, bool>.Empty);
    }

    private sealed class TableProfilingPlanBuilder
    {
        private readonly HashSet<string> _columns = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _uniqueKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<UniqueCandidatePlan> _uniqueCandidates = new();
        private readonly HashSet<string> _foreignKeyKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ForeignKeyPlan> _foreignKeys = new();

        public TableProfilingPlanBuilder(string schema, string table)
        {
            Schema = schema;
            Table = table;
        }

        public string Schema { get; }

        public string Table { get; }

        public void AddColumn(string column)
        {
            if (!string.IsNullOrWhiteSpace(column))
            {
                _columns.Add(column);
            }
        }

        public void AddUniqueCandidate(IReadOnlyList<string> columns)
        {
            if (columns is null || columns.Count == 0)
            {
                return;
            }

            var normalized = columns.Select(static c => c).ToImmutableArray();
            var key = BuildUniqueKey(normalized);
            if (_uniqueKeys.Add(key))
            {
                _uniqueCandidates.Add(new UniqueCandidatePlan(key, normalized));
            }
        }

        public void AddForeignKey(string column, string targetSchema, string targetTable, string targetColumn)
        {
            if (string.IsNullOrWhiteSpace(column) ||
                string.IsNullOrWhiteSpace(targetSchema) ||
                string.IsNullOrWhiteSpace(targetTable) ||
                string.IsNullOrWhiteSpace(targetColumn))
            {
                return;
            }

            var key = BuildForeignKeyKey(column, targetSchema, targetTable, targetColumn);
            if (_foreignKeyKeys.Add(key))
            {
                _foreignKeys.Add(new ForeignKeyPlan(key, column, targetSchema, targetTable, targetColumn));
            }
        }

        public TableProfilingPlan Build(long rowCount)
        {
            var columns = _columns
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            var uniqueCandidates = _uniqueCandidates.ToImmutableArray();
            var foreignKeys = _foreignKeys.ToImmutableArray();
            return new TableProfilingPlan(Schema, Table, rowCount, columns, uniqueCandidates, foreignKeys);
        }
    }

    private bool TryFindEntity(EntityName logicalName, out EntityModel entity)
    {
        foreach (var module in _model.Modules)
        {
            foreach (var candidate in module.Entities)
            {
                if (candidate.LogicalName.Equals(logicalName))
                {
                    entity = candidate;
                    return true;
                }
            }
        }

        entity = null!;
        return false;
    }

    private static AttributeModel? GetPreferredIdentifier(EntityModel entity)
    {
        foreach (var attribute in entity.Attributes)
        {
            if (attribute.IsIdentifier)
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool IsSingleColumnUnique(EntityModel entity, string columnName)
    {
        foreach (var index in entity.Indexes)
        {
            if (!index.IsUnique)
            {
                continue;
            }

            if (index.Columns.Length == 1 && string.Equals(index.Columns[0].Column.Value, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return "[]";
        }

        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    internal static string BuildTableFilterClause(
        DbCommand command,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        string schemaColumn,
        string tableColumn)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (tables.Count == 0)
        {
            return "1 = 0";
        }

        var builder = new StringBuilder();
        builder.Append("EXISTS (SELECT 1 FROM (VALUES ");

        var index = 0;
        foreach (var (schema, table) in tables)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            var schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = $"@schema{index}";
            schemaParameter.DbType = DbType.String;
            schemaParameter.Value = schema;
            command.Parameters.Add(schemaParameter);

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = $"@table{index}";
            tableParameter.DbType = DbType.String;
            tableParameter.Value = table;
            command.Parameters.Add(tableParameter);

            builder.Append($"({schemaParameter.ParameterName}, {tableParameter.ParameterName})");
            index++;
        }

        builder.Append($") AS targets(SchemaName, TableName) WHERE targets.SchemaName = {schemaColumn} AND targets.TableName = {tableColumn})");
        return builder.ToString();
    }

    private static string QualifyIdentifier(string schema, string table)
    {
        return QuoteIdentifier(schema) + "." + QuoteIdentifier(table);
    }

    private sealed record ColumnMetadata(bool IsNullable, bool IsComputed, bool IsPrimaryKey, string? DefaultDefinition);

    private sealed class TableKeyComparer : IEqualityComparer<(string Schema, string Table)>
    {
        public static TableKeyComparer Instance { get; } = new();

        public bool Equals((string Schema, string Table) x, (string Schema, string Table) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Schema, string Table) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed class ColumnKeyComparer : IEqualityComparer<(string Schema, string Table, string Column)>
    {
        public static ColumnKeyComparer Instance { get; } = new();

        public bool Equals((string Schema, string Table, string Column) x, (string Schema, string Table, string Column) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Schema, string Table, string Column) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Column, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
