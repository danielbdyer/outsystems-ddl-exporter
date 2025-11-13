using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Model.Emission;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.DynamicData;

public sealed record SqlDynamicEntityExtractionRequest(
    string ConnectionString,
    SqlConnectionOptions ConnectionOptions,
    OsmModel Model,
    ModuleFilterOptions ModuleFilter,
    NamingOverrideOptions NamingOverrides,
    int? CommandTimeoutSeconds,
    PipelineExecutionLogBuilder? Log = null);

public sealed class SqlDynamicEntityDataProvider : IDynamicEntityDataProvider
{
    private const int DefaultBatchSize = 1000;

    private readonly TimeProvider _timeProvider;
    private readonly int _batchSize;
    private readonly Func<string, SqlConnectionOptions, IDbConnectionFactory> _connectionFactoryFactory;

    public SqlDynamicEntityDataProvider(
        TimeProvider timeProvider,
        Func<string, SqlConnectionOptions, IDbConnectionFactory> connectionFactoryFactory)
        : this(timeProvider, connectionFactoryFactory, DefaultBatchSize)
    {
    }

    internal SqlDynamicEntityDataProvider(
        TimeProvider timeProvider,
        Func<string, SqlConnectionOptions, IDbConnectionFactory> connectionFactoryFactory,
        int batchSize)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _connectionFactoryFactory = connectionFactoryFactory ?? throw new ArgumentNullException(nameof(connectionFactoryFactory));
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        _batchSize = batchSize;
    }

    public async Task<Result<DynamicEntityExtractionResult>> ExtractAsync(
        SqlDynamicEntityExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return Result<DynamicEntityExtractionResult>.Failure(ValidationError.Create(
                "pipeline.dynamicData.connectionString.missing",
                "Dynamic entity extraction requires a SQL connection string."));
        }

        if (request.Model is null)
        {
            return Result<DynamicEntityExtractionResult>.Failure(ValidationError.Create(
                "pipeline.dynamicData.model.missing",
                "Dynamic entity extraction requires a resolved model."));
        }

        var namingOverrides = request.NamingOverrides ?? NamingOverrideOptions.Empty;
        var moduleFilter = request.ModuleFilter ?? ModuleFilterOptions.IncludeAll;
        var moduleAllowList = moduleFilter.Modules.IsDefaultOrEmpty
            ? null
            : moduleFilter.Modules
                .Select(module => module.Value)
                .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var connectionOptions = request.ConnectionOptions ?? SqlConnectionOptions.Default;
        var connectionFactory = _connectionFactoryFactory(request.ConnectionString.Trim(), connectionOptions);

        var tables = new List<StaticEntityTableData>();
        var telemetryEntries = ImmutableArray.CreateBuilder<DynamicEntityTableTelemetry>();
        var totalRows = 0;
        var extractedTables = 0;
        var startTimestamp = _timeProvider.GetTimestamp();
        var extractionStartedAt = _timeProvider.GetUtcNow();

        try
        {
            await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var module in request.Model.Modules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!ShouldIncludeModule(module, moduleFilter, moduleAllowList))
                {
                    continue;
                }

                moduleFilter.EntityFilters.TryGetValue(module.Name.Value, out var entityFilter);

                foreach (var entity in module.Entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!ShouldIncludeEntity(entity, moduleFilter, entityFilter))
                    {
                        continue;
                    }

                    var snapshot = EntityEmissionSnapshot.Create(module.Name.Value, entity);
                    var definition = CreateDefinition(snapshot, namingOverrides);
                    if (definition.Columns.Length == 0)
                    {
                        continue;
                    }

                    var orderColumns = DetermineOrderColumns(definition, snapshot);
                    var extraction = await ExtractTableAsync(
                        connection,
                        module.Name.Value,
                        definition,
                        orderColumns,
                        request.CommandTimeoutSeconds,
                        cancellationToken).ConfigureAwait(false);

                    if (extraction.Table is null)
                    {
                        continue;
                    }

                    tables.Add(extraction.Table);
                    totalRows += extraction.RowCount;
                    extractedTables++;

                    telemetryEntries.Add(new DynamicEntityTableTelemetry(
                        module.Name.Value,
                        entity.LogicalName.Value,
                        definition.Schema,
                        definition.PhysicalName,
                        definition.EffectiveName,
                        extraction.RowCount,
                        extraction.BatchCount,
                        extraction.Duration,
                        extraction.Checksum,
                        extraction.Chunks));

                    request.Log?.Record(
                        "dynamicData.extract.table",
                        $"Extracted dynamic entity data for '{definition.Schema}.{definition.PhysicalName}'.",
                        new PipelineLogMetadataBuilder()
                            .WithValue("module", module.Name.Value)
                            .WithValue("table.schema", definition.Schema)
                            .WithValue("table.name", definition.PhysicalName)
                            .WithCount("rows", extraction.RowCount)
                            .WithCount("batches", extraction.BatchCount)
                            .WithValue("checksum.crc32", extraction.Checksum)
                            .Build());
                }
            }
        }
        catch (DbException ex)
        {
            request.Log?.Record(
                "dynamicData.extract.failed",
                "Failed to extract dynamic entity data from SQL.",
                new PipelineLogMetadataBuilder()
                    .WithValue("error.message", ex.Message)
                    .Build());

            return Result<DynamicEntityExtractionResult>.Failure(ValidationError.Create(
                "pipeline.dynamicData.sql.failed",
                $"Failed to retrieve dynamic entity data: {ex.Message}"));
        }

        var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
        var completedAtUtc = _timeProvider.GetUtcNow();
        var telemetry = new DynamicEntityExtractionTelemetry(
            extractionStartedAt,
            completedAtUtc,
            telemetryEntries.ToImmutable());

        if (tables.Count == 0)
        {
            request.Log?.Record(
                "dynamicData.extract.skipped",
                "No dynamic entities were eligible for extraction based on the configured module filters.",
                new PipelineLogMetadataBuilder()
                    .WithMetric("duration.ms", elapsed.TotalMilliseconds)
                    .Build());

            return Result<DynamicEntityExtractionResult>.Success(
                new DynamicEntityExtractionResult(DynamicEntityDataset.Empty, telemetry));
        }

        request.Log?.Record(
            "dynamicData.extract.completed",
            "Completed dynamic entity extraction.",
            new PipelineLogMetadataBuilder()
                .WithMetric("duration.ms", elapsed.TotalMilliseconds)
                .WithCount("tables", extractedTables)
                .WithCount("rows", totalRows)
                .Build());

        return Result<DynamicEntityExtractionResult>.Success(
            new DynamicEntityExtractionResult(DynamicEntityDataset.Create(tables), telemetry));
    }

    private async Task<TableExtractionResult> ExtractTableAsync(
        DbConnection connection,
        string moduleName,
        StaticEntitySeedTableDefinition definition,
        string[] orderColumns,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var rows = new List<StaticEntityRow>();
        var offset = 0;
        var batches = 0;
        var chunks = ImmutableArray.CreateBuilder<DynamicEntityChunkTelemetry>();
        var schemaQualifiedName = FormatTwoPartName(definition.Schema, definition.PhysicalName);
        var selectList = string.Join(", ", definition.Columns.Select(static column => FormatColumnName(column.ColumnName)));
        var orderClause = orderColumns.Length == 0
            ? FormatColumnName(definition.Columns[0].ColumnName)
            : string.Join(", ", orderColumns.Select(static column => FormatColumnName(column)));

        var commandText = FormattableString.Invariant($@"
SELECT {selectList}
FROM {schemaQualifiedName}
ORDER BY {orderClause}
OFFSET @offset ROWS FETCH NEXT @fetch ROWS ONLY;");

        var tableStart = _timeProvider.GetTimestamp();
        var checksum = new DynamicEntityChecksumCalculator();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            command.CommandType = CommandType.Text;
            if (commandTimeoutSeconds.HasValue)
            {
                command.CommandTimeout = commandTimeoutSeconds.Value;
            }

            var offsetParameter = command.CreateParameter();
            offsetParameter.ParameterName = "@offset";
            offsetParameter.DbType = DbType.Int32;
            offsetParameter.Value = offset;
            command.Parameters.Add(offsetParameter);

            var fetchParameter = command.CreateParameter();
            fetchParameter.ParameterName = "@fetch";
            fetchParameter.DbType = DbType.Int32;
            fetchParameter.Value = _batchSize;
            command.Parameters.Add(fetchParameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var batchCount = 0;
            var chunkStart = _timeProvider.GetTimestamp();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new object?[definition.Columns.Length];
                for (var i = 0; i < definition.Columns.Length; i++)
                {
                    var column = definition.Columns[i];
                    var rawValue = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    values[i] = column.NormalizeValue(rawValue);
                }

                var row = StaticEntityRow.Create(values);
                rows.Add(row);
                checksum.AppendRow(row);
                batchCount++;
            }

            if (batchCount == 0)
            {
                break;
            }

            offset += batchCount;
            batches++;

            var chunkDuration = _timeProvider.GetElapsedTime(chunkStart);
            chunks.Add(new DynamicEntityChunkTelemetry(batches, batchCount, chunkDuration));

            if (batchCount < _batchSize)
            {
                break;
            }
        }

        if (rows.Count == 0)
        {
            return TableExtractionResult.Empty;
        }

        var tableData = StaticEntityTableData.Create(definition, rows);
        var tableDuration = _timeProvider.GetElapsedTime(tableStart);
        return new TableExtractionResult(
            tableData,
            rows.Count,
            batches,
            checksum.GetChecksum(),
            chunks.ToImmutable(),
            tableDuration);
    }

    private static StaticEntitySeedTableDefinition CreateDefinition(
        EntityEmissionSnapshot snapshot,
        NamingOverrideOptions namingOverrides)
    {
        var filteredAttributes = snapshot.EmittableAttributes
            .Where(static attribute => !(attribute.OnDisk.IsComputed ?? false))
            .ToImmutableArray();

        if (filteredAttributes.IsDefaultOrEmpty)
        {
            return StaticEntitySeedTableDefinition.Empty;
        }

        var entity = snapshot.Entity;
        var primaryIndex = entity.Indexes.FirstOrDefault(static index => index.IsPrimary);
        var primaryColumns = primaryIndex is null
            ? snapshot.IdentifierAttributes
                .Where(static attribute => !(attribute.OnDisk.IsComputed ?? false))
                .Select(static attribute => attribute.ColumnName.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : primaryIndex.Columns
                .Where(static column => !column.IsIncluded)
                .Select(static column => column.Column.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (primaryColumns.Count == 0)
        {
            primaryColumns = filteredAttributes
                .Where(static attribute => attribute.IsIdentifier)
                .Select(static attribute => attribute.ColumnName.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var columnDefinitions = filteredAttributes
            .Select(attribute =>
            {
                var physicalName = attribute.ColumnName.Value;
                var emissionName = attribute.LogicalName.Value;

                if (string.IsNullOrWhiteSpace(emissionName))
                {
                    emissionName = physicalName;
                }

                return new StaticEntitySeedColumn(
                    attribute.LogicalName.Value,
                    physicalName,
                    emissionName,
                    attribute.DataType,
                    attribute.Length,
                    attribute.Precision,
                    attribute.Scale,
                    primaryColumns.Contains(physicalName),
                    attribute.OnDisk.IsIdentity ?? attribute.IsAutoNumber,
                    IsNullable: !attribute.IsMandatory);
            })
            .ToImmutableArray();

        if (columnDefinitions.IsDefault)
        {
            columnDefinitions = ImmutableArray<StaticEntitySeedColumn>.Empty;
        }

        var effectiveName = namingOverrides.GetEffectiveTableName(
            entity.Schema.Value,
            entity.PhysicalName.Value,
            entity.LogicalName.Value,
            snapshot.ModuleName);

        return new StaticEntitySeedTableDefinition(
            snapshot.ModuleName,
            entity.LogicalName.Value,
            entity.Schema.Value,
            entity.PhysicalName.Value,
            effectiveName,
            columnDefinitions);
    }

    private static string[] DetermineOrderColumns(
        StaticEntitySeedTableDefinition definition,
        EntityEmissionSnapshot snapshot)
    {
        var primaryColumns = definition.Columns
            .Where(static column => column.IsPrimaryKey)
            .Select(static column => column.ColumnName)
            .ToArray();

        if (primaryColumns.Length > 0)
        {
            return primaryColumns;
        }

        if (snapshot.PreferredIdentifier is not null)
        {
            return new[] { snapshot.PreferredIdentifier.ColumnName.Value };
        }

        if (definition.Columns.Length > 0)
        {
            return new[] { definition.Columns[0].ColumnName };
        }

        return Array.Empty<string>();
    }

    private static bool ShouldIncludeModule(
        ModuleModel module,
        ModuleFilterOptions filter,
        ISet<string>? moduleAllowList)
    {
        if (!filter.IncludeSystemModules && module.IsSystemModule)
        {
            return false;
        }

        if (!filter.IncludeInactiveModules && !module.IsActive)
        {
            return false;
        }

        if (moduleAllowList is null)
        {
            return true;
        }

        return moduleAllowList.Contains(module.Name.Value);
    }

    private static bool ShouldIncludeEntity(
        EntityModel entity,
        ModuleFilterOptions filter,
        ModuleEntityFilterOptions? entityFilter)
    {
        if (!filter.IncludeInactiveModules && !entity.IsActive)
        {
            return false;
        }

        if (entityFilter is null)
        {
            return true;
        }

        return entityFilter.Matches(entity);
    }

    private static string FormatTwoPartName(string schema, string name)
        => FormattableString.Invariant($"[{schema.Replace("]", "]]", StringComparison.Ordinal)}].[{name.Replace("]", "]]", StringComparison.Ordinal)}]");

    private static string FormatColumnName(string name)
        => FormattableString.Invariant($"[{name.Replace("]", "]]", StringComparison.Ordinal)}]");

    private readonly record struct TableExtractionResult(
        StaticEntityTableData? Table,
        int RowCount,
        int BatchCount,
        string Checksum,
        ImmutableArray<DynamicEntityChunkTelemetry> Chunks,
        TimeSpan Duration)
    {
        public static TableExtractionResult Empty { get; } = new(null, 0, 0, "00000000", ImmutableArray<DynamicEntityChunkTelemetry>.Empty, TimeSpan.Zero);
    }

    private sealed class DynamicEntityChecksumCalculator
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
        private static readonly byte[] RowSeparator = { (byte)'\n' };
        private static readonly byte[] ColumnSeparator = { (byte)'|' };
        private static readonly byte[] NullMarker = Utf8.GetBytes("<null>");
        private static readonly byte[] EmptyMarker = Utf8.GetBytes("<empty>");
        private readonly Crc32 _crc = new();

        public void AppendRow(StaticEntityRow row)
        {
            _crc.Append(RowSeparator);
            if (row.Values.IsDefaultOrEmpty)
            {
                return;
            }

            foreach (var value in row.Values)
            {
                _crc.Append(ColumnSeparator);
                AppendValue(value);
            }
        }

        public string GetChecksum()
        {
            Span<byte> buffer = stackalloc byte[4];
            _crc.GetCurrentHash(buffer);
            return Convert.ToHexString(buffer);
        }

        private void AppendValue(object? value)
        {
            if (value is null || value is DBNull)
            {
                _crc.Append(NullMarker);
                return;
            }

            switch (value)
            {
                case string text:
                    AppendString(text);
                    return;
                case bool boolean:
                    AppendString(boolean ? "true" : "false");
                    return;
                case DateTime dateTime:
                    AppendString(dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                    return;
                case DateTimeOffset offset:
                    AppendString(offset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                    return;
                case Guid guid:
                    AppendString(guid.ToString("D", CultureInfo.InvariantCulture));
                    return;
                case byte[] bytes:
                    AppendString(Convert.ToHexString(bytes));
                    return;
                case ReadOnlyMemory<byte> memory:
                    AppendString(Convert.ToHexString(memory.Span));
                    return;
            }

            if (value is IFormattable formattable)
            {
                AppendString(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            }

            AppendString(value.ToString() ?? string.Empty);
        }

        private void AppendString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                _crc.Append(EmptyMarker);
                return;
            }

            var byteCount = Utf8.GetByteCount(value);
            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var written = Utf8.GetBytes(value, 0, value.Length, buffer, 0);
                _crc.Append(buffer.AsSpan(0, written));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
