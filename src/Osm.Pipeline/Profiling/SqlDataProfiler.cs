using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

            var columnProfiles = new List<ColumnProfile>();
            var uniqueProfiles = new List<UniqueCandidateProfile>();
            var compositeProfiles = new List<CompositeUniqueCandidateProfile>();
            var foreignKeys = new List<ForeignKeyReality>();

            foreach (var entity in _model.Modules.SelectMany(static module => module.Entities))
            {
                var schema = entity.Schema.Value;
                var table = entity.PhysicalName.Value;
                rowCounts.TryGetValue((schema, table), out var tableRowCount);

                foreach (var attribute in entity.Attributes)
                {
                    var columnKey = (schema, table, attribute.ColumnName.Value);
                    if (!metadata.TryGetValue(columnKey, out var meta))
                    {
                        continue;
                    }

                    var (nullCount, _) = await ComputeNullCountAsync(
                        connection,
                        schema,
                        table,
                        attribute.ColumnName.Value,
                        tableRowCount,
                        cancellationToken).ConfigureAwait(false);

                    var columnProfileResult = ColumnProfile.Create(
                        SchemaName.Create(schema).Value,
                        TableName.Create(table).Value,
                        ColumnName.Create(attribute.ColumnName.Value).Value,
                        meta.IsNullable,
                        meta.IsComputed,
                        meta.IsPrimaryKey,
                        IsSingleColumnUnique(entity, attribute.ColumnName.Value),
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

                    var hasDuplicates = await HasDuplicateValuesAsync(
                        connection,
                        schema,
                        table,
                        orderedColumns,
                        rowCounts.GetValueOrDefault((schema, table)),
                        cancellationToken).ConfigureAwait(false);

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

                    var hasOrphans = await HasOrphansAsync(
                        connection,
                        schema,
                        table,
                        attribute.ColumnName.Value,
                        targetEntity.Schema.Value,
                        targetEntity.PhysicalName.Value,
                        targetIdentifier.ColumnName.Value,
                        rowCounts.GetValueOrDefault((schema, table)),
                        cancellationToken).ConfigureAwait(false);

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

                    var realityResult = ForeignKeyReality.Create(referenceResult.Value, hasOrphans);
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

        var command = connection.CreateCommand();
        command.CommandText = @"SELECT
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
WHERE t.is_ms_shipped = 0;";

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

        var command = connection.CreateCommand();
        command.CommandText = @"SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    SUM(p.rows) AS RowCount
FROM sys.tables AS t
JOIN sys.schemas AS s ON t.schema_id = s.schema_id
JOIN sys.dm_db_partition_stats AS p ON t.object_id = p.object_id
WHERE p.index_id IN (0,1)
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

    private bool ShouldSample(long rowCount)
    {
        if (rowCount <= 0)
        {
            return false;
        }

        var sampling = _options.Sampling;
        return rowCount > sampling.RowCountSamplingThreshold;
    }

    private async Task<(long NullCount, bool UsedSampling)> ComputeNullCountAsync(
        DbConnection connection,
        string schema,
        string table,
        string column,
        long rowCount,
        CancellationToken cancellationToken)
    {
        var useSampling = ShouldSample(rowCount);
        var qualified = QualifyIdentifier(schema, table);
        var quotedColumn = QuoteIdentifier(column);
        var sql = useSampling
            ? $"SELECT CAST(COUNT_BIG(*) AS BIGINT) AS SampleCount, CAST(SUM(CASE WHEN {quotedColumn} IS NULL THEN 1 ELSE 0 END) AS BIGINT) AS NullCount FROM (SELECT TOP (@SampleSize) {quotedColumn} FROM {qualified} WITH (NOLOCK) ORDER BY (SELECT NULL)) AS sample;"
            : $"SELECT CAST(COUNT_BIG(*) AS BIGINT) AS SampleCount, CAST(SUM(CASE WHEN {quotedColumn} IS NULL THEN 1 ELSE 0 END) AS BIGINT) AS NullCount FROM {qualified} WITH (NOLOCK);";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = _options.Sampling.SampleSize;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sampleCount = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
            var nullCount = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            if (useSampling && sampleCount > 0)
            {
                var estimated = (long)Math.Round(nullCount * (rowCount / (double)sampleCount), MidpointRounding.AwayFromZero);
                var clamped = Math.Min(rowCount, Math.Max(0, estimated));
                return (clamped, true);
            }

            return (nullCount, useSampling);
        }

        return (0, useSampling);
    }

    private async Task<bool> HasDuplicateValuesAsync(
        DbConnection connection,
        string schema,
        string table,
        IReadOnlyList<string> columns,
        long rowCount,
        CancellationToken cancellationToken)
    {
        if (columns.Count == 0)
        {
            return false;
        }

        var qualified = QualifyIdentifier(schema, table);
        var columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        var useSampling = ShouldSample(rowCount);

        var sb = new StringBuilder();
        sb.Append("SELECT TOP 1 1 FROM ");
        if (useSampling)
        {
            sb.Append("(SELECT TOP (@SampleSize) ").Append(columnList).Append(" FROM ").Append(qualified).Append(" WITH (NOLOCK) ORDER BY (SELECT NULL)) AS sample ");
            sb.Append("GROUP BY ").Append(columnList).Append(" HAVING COUNT(*) > 1;");
        }
        else
        {
            sb.Append(qualified).Append(" WITH (NOLOCK) GROUP BY ").Append(columnList).Append(" HAVING COUNT(*) > 1;");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sb.ToString();
        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = _options.Sampling.SampleSize;
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private async Task<bool> HasOrphansAsync(
        DbConnection connection,
        string fromSchema,
        string fromTable,
        string fromColumn,
        string toSchema,
        string toTable,
        string toColumn,
        long rowCount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toColumn))
        {
            return false;
        }

        var fromQualified = QualifyIdentifier(fromSchema, fromTable);
        var toQualified = QualifyIdentifier(toSchema, toTable);
        var fromColumnQuoted = QuoteIdentifier(fromColumn);
        var toColumnQuoted = QuoteIdentifier(toColumn);
        var useSampling = ShouldSample(rowCount);

        var sql = useSampling
            ? $"SELECT TOP 1 1 FROM (SELECT TOP (@SampleSize) {fromColumnQuoted} FROM {fromQualified} WITH (NOLOCK) WHERE {fromColumnQuoted} IS NOT NULL ORDER BY (SELECT NULL)) AS sample LEFT JOIN {toQualified} AS target WITH (NOLOCK) ON sample.{fromColumnQuoted} = target.{toColumnQuoted} WHERE target.{toColumnQuoted} IS NULL;"
            : $"SELECT TOP 1 1 FROM {fromQualified} AS source WITH (NOLOCK) LEFT JOIN {toQualified} AS target WITH (NOLOCK) ON source.{fromColumnQuoted} = target.{toColumnQuoted} WHERE source.{fromColumnQuoted} IS NOT NULL AND target.{toColumnQuoted} IS NULL;";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        if (useSampling)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@SampleSize";
            parameter.DbType = DbType.Int32;
            parameter.Value = _options.Sampling.SampleSize;
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
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
