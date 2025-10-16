using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

internal sealed class ProfilingQueryExecutor : IProfilingQueryExecutor
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly SqlProfilerOptions _options;

    public ProfilingQueryExecutor(IDbConnectionFactory connectionFactory, SqlProfilerOptions options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

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

        var foreignKeyFallback = (
            Orphans: BuildConservativeForeignKeyResults(plan),
            IsNoCheck: BuildConservativeForeignKeyNoCheckResults(plan));

        var foreignKeyReality = await ExecuteWithTimeoutFallback(
            ct => ComputeForeignKeyRealityAsync(connection, plan, ct),
            foreignKeyFallback,
            tableCancellation,
            cancellationToken).ConfigureAwait(false);

        return new TableProfilingResults(nullCounts, duplicateFlags, foreignKeyReality.Orphans, foreignKeyReality.IsNoCheck);
    }

    internal static string BuildUniqueCandidatesSql(
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

    internal static string BuildForeignKeySql(
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

    internal static string BuildForeignKeyMetadataSql(string schema, string table, DbCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        var schemaParameter = command.CreateParameter();
        schemaParameter.ParameterName = "@SchemaName";
        schemaParameter.DbType = DbType.String;
        schemaParameter.Value = schema;
        command.Parameters.Add(schemaParameter);

        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@TableName";
        tableParameter.DbType = DbType.String;
        tableParameter.Value = table;
        command.Parameters.Add(tableParameter);

        return @"SELECT
    parentColumn.name AS ColumnName,
    targetSchema.name AS TargetSchema,
    targetTable.name AS TargetTable,
    targetColumn.name AS TargetColumn,
    fk.is_not_trusted AS IsNotTrusted,
    fk.is_disabled AS IsDisabled
FROM sys.foreign_keys AS fk
JOIN sys.tables AS parentTable ON fk.parent_object_id = parentTable.object_id
JOIN sys.schemas AS parentSchema ON parentTable.schema_id = parentSchema.schema_id
JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns AS parentColumn ON fkc.parent_object_id = parentColumn.object_id AND fkc.parent_column_id = parentColumn.column_id
JOIN sys.tables AS targetTable ON fk.referenced_object_id = targetTable.object_id
JOIN sys.schemas AS targetSchema ON targetTable.schema_id = targetSchema.schema_id
JOIN sys.columns AS targetColumn ON fkc.referenced_object_id = targetColumn.object_id AND fkc.referenced_column_id = targetColumn.column_id
WHERE parentSchema.name = @SchemaName AND parentTable.name = @TableName;";
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
        await using var command = connection.CreateCommand();
        command.CommandText = BuildNullCountSql(plan.Schema, plan.Table, plan.Columns, useSampling);

        ApplyCommandTimeout(command);

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = GetSampleSize(plan.RowCount);
            command.Parameters.Add(parameter);
        }

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

        return results.ToImmutable();
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

        var columnSet = plan.UniqueCandidates
            .SelectMany(static candidate => candidate.Columns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var useSampling = ShouldSample(plan.RowCount);
        await using var command = connection.CreateCommand();
        command.CommandText = BuildUniqueCandidatesSql(plan.Schema, plan.Table, columnSet, plan.UniqueCandidates, useSampling, command);

        ApplyCommandTimeout(command);

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

        foreach (var candidate in plan.UniqueCandidates)
        {
            if (!results.ContainsKey(candidate.Key))
            {
                results[candidate.Key] = false;
            }
        }

        return results.ToImmutable();
    }

    private async Task<(IReadOnlyDictionary<string, bool> Orphans, IReadOnlyDictionary<string, bool> IsNoCheck)> ComputeForeignKeyRealityAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.ForeignKeys.IsDefaultOrEmpty)
        {
            return (ImmutableDictionary<string, bool>.Empty, ImmutableDictionary<string, bool>.Empty);
        }

        var useSampling = ShouldSample(plan.RowCount);
        await using var command = connection.CreateCommand();
        var columnSet = plan.ForeignKeys.Select(static fk => fk.Column).Distinct(StringComparer.OrdinalIgnoreCase).Select(QuoteIdentifier).ToArray();
        command.CommandText = BuildForeignKeySql(plan.Schema, plan.Table, columnSet, plan.ForeignKeys, useSampling, command);

        ApplyCommandTimeout(command);

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

        var metadata = await LoadForeignKeyMetadataAsync(connection, plan, cancellationToken).ConfigureAwait(false);

        return (results.ToImmutable(), metadata);
    }

    private async Task<IReadOnlyDictionary<string, bool>> LoadForeignKeyMetadataAsync(
        DbConnection connection,
        TableProfilingPlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildForeignKeyMetadataSql(plan.Schema, plan.Table, command);

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

        return results.ToImmutable();
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

    private static string BuildNullCountSql(string schema, string table, ImmutableArray<string> columns, bool useSampling)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WITH Source AS (");
        builder.Append("    SELECT ");
        if (useSampling)
        {
            builder.Append("TOP (@SampleSize) ");
        }

        builder.Append(string.Join(", ", columns.Select(QuoteIdentifier)));
        builder.AppendLine();
        builder.Append("    FROM ").Append(QualifyIdentifier(schema, table)).Append(" WITH (NOLOCK)");
        if (useSampling)
        {
            builder.AppendLine();
            builder.AppendLine("    ORDER BY (SELECT NULL)");
        }

        builder.AppendLine(")");
        builder.AppendLine("SELECT ColumnName, NullCount");
        builder.AppendLine("FROM (");
        for (var i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine("    UNION ALL");
            }

            var column = QuoteIdentifier(columns[i]);
            builder.Append("    SELECT '");
            builder.Append(columns[i]);
            builder.Append("' AS ColumnName, SUM(CASE WHEN ");
            builder.Append(column);
            builder.Append(" IS NULL THEN 1 ELSE 0 END) AS NullCount");
            builder.AppendLine();
            builder.Append("    FROM Source");
            builder.AppendLine();
        }

        builder.AppendLine(") AS results;");
        return builder.ToString();
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return "[]";
        }

        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    private static string QualifyIdentifier(string schema, string table)
    {
        return QuoteIdentifier(schema) + "." + QuoteIdentifier(table);
    }
}
