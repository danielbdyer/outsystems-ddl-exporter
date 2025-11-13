using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
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

    public async Task<Result<DynamicEntityDataset>> ExtractAsync(
        SqlDynamicEntityExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return Result<DynamicEntityDataset>.Failure(ValidationError.Create(
                "pipeline.dynamicData.connectionString.missing",
                "Dynamic entity extraction requires a SQL connection string."));
        }

        if (request.Model is null)
        {
            return Result<DynamicEntityDataset>.Failure(ValidationError.Create(
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
        var totalRows = 0;
        var extractedTables = 0;
        var startTimestamp = _timeProvider.GetTimestamp();

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

                    request.Log?.Record(
                        "dynamicData.extract.table",
                        $"Extracted dynamic entity data for '{definition.Schema}.{definition.PhysicalName}'.",
                        new PipelineLogMetadataBuilder()
                            .WithValue("module", module.Name.Value)
                            .WithValue("table.schema", definition.Schema)
                            .WithValue("table.name", definition.PhysicalName)
                            .WithCount("rows", extraction.RowCount)
                            .WithCount("batches", extraction.BatchCount)
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

            return Result<DynamicEntityDataset>.Failure(ValidationError.Create(
                "pipeline.dynamicData.sql.failed",
                $"Failed to retrieve dynamic entity data: {ex.Message}"));
        }

        var elapsed = _timeProvider.GetElapsedTime(startTimestamp);

        if (tables.Count == 0)
        {
            request.Log?.Record(
                "dynamicData.extract.skipped",
                "No dynamic entities were eligible for extraction based on the configured module filters.",
                new PipelineLogMetadataBuilder()
                    .WithMetric("duration.ms", elapsed.TotalMilliseconds)
                    .Build());

            return Result<DynamicEntityDataset>.Success(DynamicEntityDataset.Empty);
        }

        request.Log?.Record(
            "dynamicData.extract.completed",
            "Completed dynamic entity extraction.",
            new PipelineLogMetadataBuilder()
                .WithMetric("duration.ms", elapsed.TotalMilliseconds)
                .WithCount("tables", extractedTables)
                .WithCount("rows", totalRows)
                .Build());

        return Result<DynamicEntityDataset>.Success(DynamicEntityDataset.Create(tables));
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

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new object?[definition.Columns.Length];
                for (var i = 0; i < definition.Columns.Length; i++)
                {
                    var column = definition.Columns[i];
                    var rawValue = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    values[i] = column.NormalizeValue(rawValue);
                }

                rows.Add(StaticEntityRow.Create(values));
                batchCount++;
            }

            if (batchCount == 0)
            {
                break;
            }

            offset += batchCount;
            batches++;

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
        return new TableExtractionResult(tableData, rows.Count, batches);
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
        int BatchCount)
    {
        public static TableExtractionResult Empty { get; } = new(null, 0, 0);
    }
}
