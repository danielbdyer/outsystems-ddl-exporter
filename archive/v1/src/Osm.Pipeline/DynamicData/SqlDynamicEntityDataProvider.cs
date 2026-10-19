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
    PipelineExecutionLogBuilder? Log = null,
    StaticSeedParentHandlingMode ParentHandlingMode = StaticSeedParentHandlingMode.AutoLoad);

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

        var entityLookup = EntityLookup.Create(request.Model);
        var extractionQueue = new Queue<EntityExtractionContext>();
        var enqueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SeedInitialEntities(
            request.Model,
            moduleFilter,
            moduleAllowList,
            entityLookup,
            extractionQueue,
            enqueued);

        var tables = new List<StaticEntityTableData>();
        var telemetryEntries = ImmutableArray.CreateBuilder<DynamicEntityTableTelemetry>();
        var parentTracker = new ParentRequirementTracker();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalRows = 0;
        var extractedTables = 0;
        var startTimestamp = _timeProvider.GetTimestamp();
        var extractionStartedAt = _timeProvider.GetUtcNow();

        try
        {
            await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            while (extractionQueue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var context = extractionQueue.Dequeue();
                if (!processed.Add(context.Key))
                {
                    continue;
                }

                var snapshot = EntityEmissionSnapshot.Create(context.ModuleName, context.Entity);
                var definition = CreateDefinition(snapshot, namingOverrides);
                if (definition.Columns.Length == 0)
                {
                    continue;
                }

                var orderColumns = DetermineOrderColumns(definition, snapshot);
                var extraction = await ExtractTableAsync(
                        connection,
                        context.ModuleName,
                        definition,
                        orderColumns,
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (extraction.Table is null)
                {
                    continue;
                }

                tables.Add(extraction.Table);
                totalRows += extraction.RowCount;
                extractedTables++;

                telemetryEntries.Add(new DynamicEntityTableTelemetry(
                    context.ModuleName,
                    context.Entity.LogicalName.Value,
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
                        .WithValue("module", context.ModuleName)
                        .WithValue("table.schema", definition.Schema)
                        .WithValue("table.name", definition.PhysicalName)
                        .WithCount("rows", extraction.RowCount)
                        .WithCount("batches", extraction.BatchCount)
                        .WithValue("checksum.crc32", extraction.Checksum)
                        .Build());

                TrackParentRequirements(
                    snapshot,
                    namingOverrides,
                    entityLookup,
                    parentTracker,
                    request.ParentHandlingMode,
                    extractionQueue,
                    enqueued,
                    request.Log);
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

        var parentStatuses = parentTracker.ToStatuses();

        if (tables.Count == 0)
        {
            request.Log?.Record(
                "dynamicData.extract.skipped",
                "No dynamic entities were eligible for extraction based on the configured module filters.",
                new PipelineLogMetadataBuilder()
                    .WithMetric("duration.ms", elapsed.TotalMilliseconds)
                    .Build());

            return Result<DynamicEntityExtractionResult>.Success(
                new DynamicEntityExtractionResult(DynamicEntityDataset.Empty, telemetry, parentStatuses));
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
            new DynamicEntityExtractionResult(
                DynamicEntityDataset.Create(tables),
                telemetry,
                parentStatuses));
    }

    private static void SeedInitialEntities(
        OsmModel model,
        ModuleFilterOptions moduleFilter,
        ISet<string>? moduleAllowList,
        EntityLookup lookup,
        Queue<EntityExtractionContext> queue,
        ISet<string> enqueued)
    {
        foreach (var module in model.Modules)
        {
            if (!ShouldIncludeModule(module, moduleFilter, moduleAllowList))
            {
                continue;
            }

            moduleFilter.EntityFilters.TryGetValue(module.Name.Value, out var entityFilter);

            foreach (var entity in module.Entities)
            {
                if (!ShouldIncludeEntity(entity, moduleFilter, entityFilter))
                {
                    continue;
                }

                if (!lookup.TryGetBySchemaTable(entity.Schema.Value, entity.PhysicalName.Value, out var context))
                {
                    context = new EntityExtractionContext(module, entity);
                }

                TryEnqueue(context, queue, enqueued);
            }
        }
    }

    private static void TrackParentRequirements(
        EntityEmissionSnapshot snapshot,
        NamingOverrideOptions namingOverrides,
        EntityLookup lookup,
        ParentRequirementTracker tracker,
        StaticSeedParentHandlingMode handlingMode,
        Queue<EntityExtractionContext> queue,
        ISet<string> enqueued,
        PipelineExecutionLogBuilder? log)
    {
        if (snapshot.Entity.Relationships.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var relationship in snapshot.Entity.Relationships)
        {
            if (relationship is null)
            {
                continue;
            }

            var parentContext = lookup.ResolveRelationship(relationship);
            if (parentContext is null || !parentContext.Entity.IsStatic || !parentContext.Entity.IsActive)
            {
                continue;
            }

            var parentSnapshot = EntityEmissionSnapshot.Create(parentContext.ModuleName, parentContext.Entity);
            var definition = CreateDefinition(parentSnapshot, namingOverrides);
            if (definition.Columns.Length == 0)
            {
                continue;
            }

            var requirement = tracker.Register(
                parentContext.Key,
                definition,
                snapshot.ModuleName,
                snapshot.Entity.LogicalName.Value);

            if (handlingMode == StaticSeedParentHandlingMode.AutoLoad)
            {
                requirement.MarkAutoLoaded();
                if (TryEnqueue(parentContext, queue, enqueued))
                {
                    log?.Record(
                        "dynamicData.extract.parentQueued",
                        $"Queued parent static seed '{definition.Schema}.{definition.PhysicalName}' for extraction.",
                        new PipelineLogMetadataBuilder()
                            .WithValue("child.module", snapshot.ModuleName)
                            .WithValue("child.entity", snapshot.Entity.LogicalName.Value)
                            .WithValue("parent.schema", definition.Schema)
                            .WithValue("parent.name", definition.PhysicalName)
                            .Build());
                }
            }
            else
            {
                requirement.MarkPending();
                log?.Record(
                    "dynamicData.extract.parentPending",
                    $"Static seed parent '{definition.Schema}.{definition.PhysicalName}' requires verification.",
                    new PipelineLogMetadataBuilder()
                        .WithValue("child.module", snapshot.ModuleName)
                        .WithValue("child.entity", snapshot.Entity.LogicalName.Value)
                        .WithValue("parent.schema", definition.Schema)
                        .WithValue("parent.name", definition.PhysicalName)
                        .Build());
            }
        }
    }

    private static bool TryEnqueue(
        EntityExtractionContext context,
        Queue<EntityExtractionContext> queue,
        ISet<string> enqueued)
    {
        if (!enqueued.Add(context.Key))
        {
            return false;
        }

        queue.Enqueue(context);
        return true;
    }

    private sealed record EntityExtractionContext(ModuleModel Module, EntityModel Entity)
    {
        public string ModuleName { get; } = Module.Name.Value;

        public string Schema => Entity.Schema.Value;

        public string PhysicalName => Entity.PhysicalName.Value;

        public string LogicalName => Entity.LogicalName.Value;

        public string Key => BuildSchemaTableKey(Schema, PhysicalName);
    }

    private sealed class EntityLookup
    {
        private readonly Dictionary<string, EntityExtractionContext> _byLogicalName;
        private readonly Dictionary<string, EntityExtractionContext> _bySchemaTable;
        private readonly Dictionary<string, EntityExtractionContext> _byPhysicalName;

        private EntityLookup(
            Dictionary<string, EntityExtractionContext> byLogicalName,
            Dictionary<string, EntityExtractionContext> bySchemaTable,
            Dictionary<string, EntityExtractionContext> byPhysicalName)
        {
            _byLogicalName = byLogicalName;
            _bySchemaTable = bySchemaTable;
            _byPhysicalName = byPhysicalName;
        }

        public static EntityLookup Create(OsmModel model)
        {
            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var byLogical = new Dictionary<string, EntityExtractionContext>(StringComparer.OrdinalIgnoreCase);
            var bySchemaTable = new Dictionary<string, EntityExtractionContext>(StringComparer.OrdinalIgnoreCase);
            var byPhysical = new Dictionary<string, EntityExtractionContext>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in model.Modules)
            {
                foreach (var entity in module.Entities)
                {
                    var context = new EntityExtractionContext(module, entity);
                    byLogical.TryAdd(context.LogicalName, context);
                    byPhysical.TryAdd(context.PhysicalName, context);
                    bySchemaTable.TryAdd(context.Key, context);
                }
            }

            return new EntityLookup(byLogical, bySchemaTable, byPhysical);
        }

        public bool TryGetBySchemaTable(string schema, string table, out EntityExtractionContext context)
        {
            var key = BuildSchemaTableKey(schema, table);
            return _bySchemaTable.TryGetValue(key, out context!);
        }

        public EntityExtractionContext? ResolveRelationship(RelationshipModel relationship)
        {
            if (!string.IsNullOrWhiteSpace(relationship.TargetEntity.Value)
                && _byLogicalName.TryGetValue(relationship.TargetEntity.Value, out var byLogical))
            {
                return byLogical;
            }

            if (!relationship.ActualConstraints.IsDefaultOrEmpty)
            {
                foreach (var constraint in relationship.ActualConstraints)
                {
                    if (constraint is null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                        && !string.IsNullOrWhiteSpace(constraint.ReferencedTable))
                    {
                        var key = BuildSchemaTableKey(constraint.ReferencedSchema, constraint.ReferencedTable);
                        if (_bySchemaTable.TryGetValue(key, out var bySchema))
                        {
                            return bySchema;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(relationship.TargetPhysicalName.Value)
                && _byPhysicalName.TryGetValue(relationship.TargetPhysicalName.Value, out var byPhysical))
            {
                return byPhysical;
            }

            return null;
        }
    }

    private sealed class ParentRequirementTracker
    {
        private readonly Dictionary<string, ParentRequirement> _requirements = new(StringComparer.OrdinalIgnoreCase);

        public ParentRequirement Register(
            string key,
            StaticEntitySeedTableDefinition definition,
            string childModule,
            string childEntity)
        {
            if (!_requirements.TryGetValue(key, out var requirement))
            {
                requirement = new ParentRequirement(definition);
                _requirements[key] = requirement;
            }

            requirement.AddReference(childModule, childEntity);
            return requirement;
        }

        public ImmutableArray<StaticSeedParentStatus> ToStatuses()
        {
            if (_requirements.Count == 0)
            {
                return ImmutableArray<StaticSeedParentStatus>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<StaticSeedParentStatus>(_requirements.Count);
            foreach (var requirement in _requirements.Values)
            {
                builder.Add(requirement.ToStatus());
            }

            return builder.ToImmutable();
        }

        internal sealed class ParentRequirement
        {
            private readonly HashSet<StaticSeedParentReference> _references = new(new ReferenceComparer());

            public ParentRequirement(StaticEntitySeedTableDefinition definition)
            {
                Definition = definition;
            }

            public StaticEntitySeedTableDefinition Definition { get; }

            public StaticSeedParentSatisfaction Satisfaction { get; private set; }
                = StaticSeedParentSatisfaction.RequiresVerification;

            public void AddReference(string module, string entity)
            {
                if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(entity))
                {
                    return;
                }

                _references.Add(new StaticSeedParentReference(module, entity));
            }

            public void MarkAutoLoaded()
            {
                Satisfaction = StaticSeedParentSatisfaction.AutoLoaded;
            }

            public void MarkPending()
            {
                if (Satisfaction == StaticSeedParentSatisfaction.AutoLoaded)
                {
                    return;
                }

                Satisfaction = StaticSeedParentSatisfaction.RequiresVerification;
            }

            public StaticSeedParentStatus ToStatus()
            {
                var references = _references.Count == 0
                    ? ImmutableArray<StaticSeedParentReference>.Empty
                    : _references.ToImmutableArray();

                return StaticSeedParentStatus.Create(Definition, Satisfaction, references);
            }

            private sealed class ReferenceComparer : IEqualityComparer<StaticSeedParentReference>
            {
                public bool Equals(StaticSeedParentReference? x, StaticSeedParentReference? y)
                {
                    if (x is null || y is null)
                    {
                        return false;
                    }

                    return string.Equals(x.Module, y.Module, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Entity, y.Entity, StringComparison.OrdinalIgnoreCase);
                }

                public int GetHashCode(StaticSeedParentReference obj)
                {
                    var moduleHash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Module ?? string.Empty);
                    var entityHash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Entity ?? string.Empty);
                    return HashCode.Combine(moduleHash, entityHash);
                }
            }
        }
    }

    private static string BuildSchemaTableKey(string schema, string table)
        => FormattableString.Invariant($"{schema}.{table}");

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
