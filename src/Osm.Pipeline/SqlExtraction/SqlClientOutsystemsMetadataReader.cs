using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

public sealed class SqlClientOutsystemsMetadataReader : IOutsystemsMetadataReader
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAdvancedSqlScriptProvider _scriptProvider;
    private readonly SqlExecutionOptions _options;
    private readonly ILogger<SqlClientOutsystemsMetadataReader> _logger;
    private readonly IDbCommandExecutor _commandExecutor;
    private readonly MetadataContractOverrides _contractOverrides;
    private readonly IReadOnlyList<IResultSetDefinition> _resultSets;

    public SqlClientOutsystemsMetadataReader(
        IDbConnectionFactory connectionFactory,
        IAdvancedSqlScriptProvider scriptProvider,
        SqlExecutionOptions? options = null,
        ILogger<SqlClientOutsystemsMetadataReader>? logger = null,
        IDbCommandExecutor? commandExecutor = null,
        MetadataContractOverrides? contractOverrides = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _scriptProvider = scriptProvider ?? throw new ArgumentNullException(nameof(scriptProvider));
        _options = options ?? SqlExecutionOptions.Default;
        _logger = logger ?? NullLogger<SqlClientOutsystemsMetadataReader>.Instance;
        _commandExecutor = commandExecutor ?? DbCommandExecutor.Instance;
        _contractOverrides = contractOverrides ?? MetadataContractOverrides.Strict;
        _resultSets = CreateResultSets();

        if (_contractOverrides.HasOverrides)
        {
            foreach (var pair in _contractOverrides.OptionalColumns)
            {
                var columnList = string.Join(", ", pair.Value);
                _logger.LogInformation(
                    "Metadata contract override active for result set {ResultSet}. Optional columns: {Columns}.",
                    pair.Key,
                    columnList);
            }
        }
    }

    private IReadOnlyList<IResultSetDefinition> CreateResultSets()
        => new IResultSetDefinition[]
        {
            ResultSetDefinitionFactory.Create(
                "Modules",
                ResultSetReader<OutsystemsModuleRow>.Create(MetadataSchemas.Modules.MapRow),
                static (accumulator, rows) => accumulator.SetModules(rows)),
            ResultSetDefinitionFactory.Create(
                "Entities",
                ResultSetReader<OutsystemsEntityRow>.Create(MetadataSchemas.Entities.MapRow),
                static (accumulator, rows) => accumulator.SetEntities(rows)),
            ResultSetDefinitionFactory.Create(
                "Attributes",
                ResultSetReader<OutsystemsAttributeRow>.Create(MetadataSchemas.Attributes.MapRow),
                static (accumulator, rows) => accumulator.SetAttributes(rows)),
            ResultSetDefinitionFactory.Create(
                "References",
                ResultSetReader<OutsystemsReferenceRow>.Create(MetadataSchemas.References.MapRow),
                static (accumulator, rows) => accumulator.SetReferences(rows)),
            ResultSetDefinitionFactory.Create(
                "PhysicalTables",
                ResultSetReader<OutsystemsPhysicalTableRow>.Create(MetadataSchemas.PhysicalTables.MapRow),
                static (accumulator, rows) => accumulator.SetPhysicalTables(rows)),
            ResultSetDefinitionFactory.Create(
                "ColumnReality",
                ResultSetReader<OutsystemsColumnRealityRow>.Create(MetadataSchemas.ColumnReality.MapRow),
                static (accumulator, rows) => accumulator.SetColumnReality(rows)),
            ResultSetDefinitionFactory.Create(
                "ColumnChecks",
                ResultSetReader<OutsystemsColumnCheckRow>.Create(MetadataSchemas.ColumnChecks.MapRow),
                static (accumulator, rows) => accumulator.SetColumnChecks(rows)),
            ResultSetDefinitionFactory.Create(
                "ColumnCheckJson",
                ResultSetReader<OutsystemsColumnCheckJsonRow>.Create(MetadataSchemas.ColumnCheckJson.MapRow),
                static (accumulator, rows) => accumulator.SetColumnCheckJson(rows)),
            ResultSetDefinitionFactory.Create(
                "PhysicalColumnsPresent",
                ResultSetReader<OutsystemsPhysicalColumnPresenceRow>.Create(MetadataSchemas.PhysicalColumnsPresent.MapRow),
                static (accumulator, rows) => accumulator.SetPhysicalColumnsPresent(rows)),
            ResultSetDefinitionFactory.Create(
                "Indexes",
                ResultSetReader<OutsystemsIndexRow>.Create(MetadataSchemas.Indexes.MapRow),
                static (accumulator, rows) => accumulator.SetIndexes(rows)),
            ResultSetDefinitionFactory.Create(
                "IndexColumns",
                ResultSetReader<OutsystemsIndexColumnRow>.Create(MetadataSchemas.IndexColumns.MapRow),
                static (accumulator, rows) => accumulator.SetIndexColumns(rows)),
            ResultSetDefinitionFactory.Create(
                "ForeignKeys",
                ResultSetReader<OutsystemsForeignKeyRow>.Create(MetadataSchemas.ForeignKeys.MapRow),
                static (accumulator, rows) => accumulator.SetForeignKeys(rows)),
            ResultSetDefinitionFactory.Create(
                "ForeignKeyColumns",
                ResultSetReader<OutsystemsForeignKeyColumnRow>.Create(MetadataSchemas.ForeignKeyColumns.MapRow),
                static (accumulator, rows) => accumulator.SetForeignKeyColumns(rows)),
            ResultSetDefinitionFactory.Create(
                "ForeignKeyAttrMap",
                ResultSetReader<OutsystemsForeignKeyAttrMapRow>.Create(MetadataSchemas.ForeignKeyAttributeMap.MapRow),
                static (accumulator, rows) => accumulator.SetForeignKeyAttributeMap(rows)),
            ResultSetDefinitionFactory.Create(
                "AttributeHasFk",
                ResultSetReader<OutsystemsAttributeHasFkRow>.Create(MetadataSchemas.AttributeHasForeignKey.MapRow),
                static (accumulator, rows) => accumulator.SetAttributeForeignKeys(rows)),
            ResultSetDefinitionFactory.Create(
                "ForeignKeyColumnsJson",
                ResultSetReader<OutsystemsForeignKeyColumnsJsonRow>.Create(MetadataSchemas.ForeignKeyColumnsJson.MapRow),
                static (accumulator, rows) => accumulator.SetForeignKeyColumnsJson(rows)),
            ResultSetDefinitionFactory.Create(
                "ForeignKeyAttributeJson",
                ResultSetReader<OutsystemsForeignKeyAttributeJsonRow>.Create(MetadataSchemas.ForeignKeyAttributeJson.MapRow),
                static (accumulator, rows) => accumulator.SetForeignKeyAttributeJson(rows)),
            ResultSetDefinitionFactory.Create(
                "Triggers",
                ResultSetReader<OutsystemsTriggerRow>.Create(MetadataSchemas.Triggers.MapRow),
                static (accumulator, rows) => accumulator.SetTriggers(rows)),
            ResultSetDefinitionFactory.Create(
                "AttributeJson",
                ResultSetReader<OutsystemsAttributeJsonRow>.Create(MapAttributeJson),
                static (accumulator, rows) => accumulator.SetAttributeJson(rows)),
            ResultSetDefinitionFactory.Create(
                "RelationshipJson",
                ResultSetReader<OutsystemsRelationshipJsonRow>.Create(MetadataSchemas.RelationshipJson.MapRow),
                static (accumulator, rows) => accumulator.SetRelationshipJson(rows)),
            ResultSetDefinitionFactory.Create(
                "IndexJson",
                ResultSetReader<OutsystemsIndexJsonRow>.Create(MetadataSchemas.IndexJson.MapRow),
                static (accumulator, rows) => accumulator.SetIndexJson(rows)),
            ResultSetDefinitionFactory.Create(
                "TriggerJson",
                ResultSetReader<OutsystemsTriggerJsonRow>.Create(MetadataSchemas.TriggerJson.MapRow),
                static (accumulator, rows) => accumulator.SetTriggerJson(rows)),
            ResultSetDefinitionFactory.Create(
                "ModuleJson",
                ResultSetReader<OutsystemsModuleJsonRow>.Create(MetadataSchemas.ModuleJson.MapRow),
                static (accumulator, rows) => accumulator.SetModuleJson(rows)),
        };

    private OutsystemsAttributeJsonRow MapAttributeJson(DbRow row)
    {
        var allowNull = _contractOverrides.IsColumnOptional("AttributeJson", "AttributesJson");
        var record = MetadataSchemas.AttributeJson.MapRow(row, allowNull);

        if (allowNull && record.AttributesJson is null)
        {
            _logger.LogDebug(
                "AttributeJson result set row {RowIndex} returned NULL for AttributesJson and was accepted due to contract overrides.",
                row.RowIndex);
        }

        return record;
    }

    public async Task<Result<OutsystemsMetadataSnapshot>> ReadAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var script = _scriptProvider.GetScript();
        if (string.IsNullOrWhiteSpace(script))
        {
            _logger.LogError("OutSystems metadata script provider returned an empty script.");
            return Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.metadata.script.missing",
                "Metadata extraction script was empty."));
        }

        var moduleCsv = request.ModuleNames.Length > 0
            ? string.Join(',', request.ModuleNames.Select(static module => module.Value))
            : string.Empty;

        _logger.LogInformation(
            "Executing metadata snapshot script via SQL client (timeoutSeconds: {TimeoutSeconds}, moduleCount: {ModuleCount}, includeSystem: {IncludeSystem}, onlyActive: {OnlyActive}).",
            _options.CommandTimeoutSeconds,
            request.ModuleNames.Length,
            request.IncludeSystemModules,
            request.OnlyActiveAttributes);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var databaseName = connection.Database;

            await using var command = CreateCommand(connection, script, moduleCsv, request, _options);
            await using var reader = await _commandExecutor
                .ExecuteReaderAsync(command, CommandBehavior.SequentialAccess, cancellationToken)
                .ConfigureAwait(false);

            var accumulator = new MetadataAccumulator();

            for (var i = 0; i < _resultSets.Count; i++)
            {
                var definition = _resultSets[i];
                var rowCount = await definition
                    .ReadAsync(reader, cancellationToken, accumulator)
                    .ConfigureAwait(false);

                if (i < _resultSets.Count - 1)
                {
                    var nextDefinition = _resultSets[i + 1];
                    await EnsureNextResultSetAsync(
                        reader,
                        cancellationToken,
                        definition.Name,
                        rowCount,
                        nextDefinition.Name,
                        i + 1).ConfigureAwait(false);
                }
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Metadata snapshot script returned {ModuleCount} module(s), {EntityCount} entity rows, and {AttributeCount} attribute rows in {DurationMs} ms.",
                accumulator.ModuleJson.Count,
                accumulator.Entities.Count,
                accumulator.Attributes.Count,
                stopwatch.Elapsed.TotalMilliseconds);

            var snapshot = accumulator.BuildSnapshot(databaseName ?? string.Empty);

            return Result<OutsystemsMetadataSnapshot>.Success(snapshot);
        }
        catch (MetadataRowMappingException ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to map row {RowIndex} in result set '{ResultSetName}'. Column: {ColumnName}, Ordinal: {ColumnOrdinal}, ExpectedClrType: {ExpectedClrType}, ProviderType: {ProviderType}. DurationMs: {DurationMs}",
                ex.RowIndex,
                ex.ResultSetName,
                ex.ColumnName ?? "unknown",
                ex.Ordinal ?? -1,
                ex.ExpectedClrType?.FullName ?? "unknown",
                ex.ProviderFieldType?.FullName ?? "unknown",
                stopwatch.Elapsed.TotalMilliseconds);

            return Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.metadata.rowMapping",
                ex.Message));
        }
        catch (MetadataResultSetMissingException ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Metadata snapshot ended before the '{ExpectedNextResultSet}' result set (index {ExpectedIndex}) became available. Last completed '{CompletedResultSet}' contained {CompletedRowCount} row(s) after {DurationMs} ms.",
                ex.ExpectedNextResultSetName,
                ex.ExpectedNextResultSetIndex,
                ex.CompletedResultSetName,
                ex.CompletedRowCount,
                stopwatch.Elapsed.TotalMilliseconds);

            return Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.metadata.resultSets.missing",
                ex.Message));
        }
        catch (DbException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Metadata snapshot execution failed after {DurationMs} ms.", stopwatch.Elapsed.TotalMilliseconds);
            return Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.metadata.executionFailed",
                $"Metadata snapshot execution failed: {ex.Message}"));
        }
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string script,
        string moduleCsv,
        AdvancedSqlRequest request,
        SqlExecutionOptions options)
    {
        var command = connection.CreateCommand();
        command.CommandText = script;
        command.CommandType = CommandType.Text;
        if (options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = options.CommandTimeoutSeconds.Value;
        }

        var moduleParam = command.CreateParameter();
        moduleParam.ParameterName = "@ModuleNamesCsv";
        moduleParam.DbType = DbType.String;
        moduleParam.Value = moduleCsv;
        command.Parameters.Add(moduleParam);

        var includeParam = command.CreateParameter();
        includeParam.ParameterName = "@IncludeSystem";
        includeParam.DbType = DbType.Boolean;
        includeParam.Value = request.IncludeSystemModules;
        command.Parameters.Add(includeParam);

        var activeParam = command.CreateParameter();
        activeParam.ParameterName = "@OnlyActiveAttributes";
        activeParam.DbType = DbType.Boolean;
        activeParam.Value = request.OnlyActiveAttributes;
        command.Parameters.Add(activeParam);

        return command;
    }

    private interface IResultSetDefinition
    {
        string Name { get; }

        Task<int> ReadAsync(DbDataReader reader, CancellationToken cancellationToken, MetadataAccumulator accumulator);
    }

    private sealed class ResultSetDefinition<T> : IResultSetDefinition
    {
        private readonly ResultSetReader<T> _reader;
        private readonly Action<MetadataAccumulator, List<T>> _assign;

        public ResultSetDefinition(string name, ResultSetReader<T> reader, Action<MetadataAccumulator, List<T>> assign)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _assign = assign ?? throw new ArgumentNullException(nameof(assign));
        }

        public string Name { get; }

        public async Task<int> ReadAsync(DbDataReader reader, CancellationToken cancellationToken, MetadataAccumulator accumulator)
        {
            if (reader is null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (accumulator is null)
            {
                throw new ArgumentNullException(nameof(accumulator));
            }

            var rows = await _reader.ReadAllAsync(reader, Name, cancellationToken).ConfigureAwait(false);
            _assign(accumulator, rows);
            return rows.Count;
        }
    }

    private static class MetadataSchemas
    {
        internal static class Modules
        {
            private static readonly ColumnDefinition<int> EspaceId = Column.Int32(0, "EspaceId");
            private static readonly ColumnDefinition<string> EspaceName = Column.String(1, "EspaceName");
            private static readonly ColumnDefinition<bool> IsSystemModule = Column.Boolean(2, "IsSystemModule");
            private static readonly ColumnDefinition<bool> ModuleIsActive = Column.Boolean(3, "ModuleIsActive");
            private static readonly ColumnDefinition<string?> EspaceKind = Column.StringOrNull(4, "EspaceKind");
            private static readonly ColumnDefinition<Guid?> EspaceSsKey = Column.GuidOrNull(5, "EspaceSSKey");

            public static OutsystemsModuleRow MapRow(DbRow row) => new(
                EspaceId.Read(row),
                EspaceName.Read(row),
                IsSystemModule.Read(row),
                ModuleIsActive.Read(row),
                EspaceKind.Read(row),
                EspaceSsKey.Read(row));
        }

        internal static class Entities
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> EntityName = Column.String(1, "EntityName");
            private static readonly ColumnDefinition<string> PhysicalTableName = Column.String(2, "PhysicalTableName");
            private static readonly ColumnDefinition<int> EspaceId = Column.Int32(3, "EspaceId");
            private static readonly ColumnDefinition<bool> EntityIsActive = Column.Boolean(4, "EntityIsActive");
            private static readonly ColumnDefinition<bool> IsSystemEntity = Column.Boolean(5, "IsSystemEntity");
            private static readonly ColumnDefinition<bool> IsExternalEntity = Column.Boolean(6, "IsExternalEntity");
            private static readonly ColumnDefinition<string?> DataKind = Column.StringOrNull(7, "DataKind");
            private static readonly ColumnDefinition<Guid?> PrimaryKeySsKey = Column.GuidOrNull(8, "PrimaryKeySSKey");
            private static readonly ColumnDefinition<Guid?> EntitySsKey = Column.GuidOrNull(9, "EntitySSKey");
            private static readonly ColumnDefinition<string?> EntityDescription = Column.StringOrNull(10, "EntityDescription");

            public static OutsystemsEntityRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                EntityName.Read(row),
                PhysicalTableName.Read(row),
                EspaceId.Read(row),
                EntityIsActive.Read(row),
                IsSystemEntity.Read(row),
                IsExternalEntity.Read(row),
                DataKind.Read(row),
                PrimaryKeySsKey.Read(row),
                EntitySsKey.Read(row),
                EntityDescription.Read(row));
        }

        internal static class Attributes
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(1, "EntityId");
            private static readonly ColumnDefinition<string> AttrName = Column.String(2, "AttrName");
            private static readonly ColumnDefinition<Guid?> AttrSsKey = Column.GuidOrNull(3, "AttrSSKey");
            private static readonly ColumnDefinition<string?> DataType = Column.StringOrNull(4, "DataType");
            private static readonly ColumnDefinition<int?> Length = Column.Int32OrNull(5, "Length");
            private static readonly ColumnDefinition<int?> Precision = Column.Int32OrNull(6, "Precision");
            private static readonly ColumnDefinition<int?> Scale = Column.Int32OrNull(7, "Scale");
            private static readonly ColumnDefinition<string?> DefaultValue = Column.StringOrNull(8, "DefaultValue");
            private static readonly ColumnDefinition<bool> IsMandatory = Column.Boolean(9, "IsMandatory");
            private static readonly ColumnDefinition<bool> AttrIsActive = Column.Boolean(10, "AttrIsActive");
            private static readonly ColumnDefinition<bool?> IsAutoNumber = Column.BooleanOrNull(11, "IsAutoNumber");
            private static readonly ColumnDefinition<bool?> IsIdentifier = Column.BooleanOrNull(12, "IsIdentifier");
            private static readonly ColumnDefinition<int?> RefEntityId = Column.Int32OrNull(13, "RefEntityId");
            private static readonly ColumnDefinition<string?> OriginalName = Column.StringOrNull(14, "OriginalName");
            private static readonly ColumnDefinition<string?> ExternalColumnType = Column.StringOrNull(15, "ExternalColumnType");
            private static readonly ColumnDefinition<string?> DeleteRule = Column.StringOrNull(16, "DeleteRule");
            private static readonly ColumnDefinition<string?> PhysicalColumnName = Column.StringOrNull(17, "PhysicalColumnName");
            private static readonly ColumnDefinition<string?> DatabaseColumnName = Column.StringOrNull(18, "DatabaseColumnName");
            private static readonly ColumnDefinition<string?> LegacyType = Column.StringOrNull(19, "LegacyType");
            private static readonly ColumnDefinition<int?> Decimals = Column.Int32OrNull(20, "Decimals");
            private static readonly ColumnDefinition<string?> OriginalType = Column.StringOrNull(21, "OriginalType");
            private static readonly ColumnDefinition<string?> AttrDescription = Column.StringOrNull(22, "AttrDescription");

            public static OutsystemsAttributeRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                EntityId.Read(row),
                AttrName.Read(row),
                AttrSsKey.Read(row),
                DataType.Read(row),
                Length.Read(row),
                Precision.Read(row),
                Scale.Read(row),
                DefaultValue.Read(row),
                IsMandatory.Read(row),
                AttrIsActive.Read(row),
                IsAutoNumber.Read(row),
                IsIdentifier.Read(row),
                RefEntityId.Read(row),
                OriginalName.Read(row),
                ExternalColumnType.Read(row),
                DeleteRule.Read(row),
                PhysicalColumnName.Read(row),
                DatabaseColumnName.Read(row),
                LegacyType.Read(row),
                Decimals.Read(row),
                OriginalType.Read(row),
                AttrDescription.Read(row));
        }

        internal static class References
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<int?> RefEntityId = Column.Int32OrNull(1, "RefEntityId");
            private static readonly ColumnDefinition<string?> RefEntityName = Column.StringOrNull(2, "RefEntityName");
            private static readonly ColumnDefinition<string?> RefPhysicalName = Column.StringOrNull(3, "RefPhysicalName");

            public static OutsystemsReferenceRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                RefEntityId.Read(row),
                RefEntityName.Read(row),
                RefPhysicalName.Read(row));
        }

        internal static class PhysicalTables
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> SchemaName = Column.String(1, "SchemaName");
            private static readonly ColumnDefinition<string> TableName = Column.String(2, "TableName");
            private static readonly ColumnDefinition<int> ObjectId = Column.Int32(3, "object_id");

            public static OutsystemsPhysicalTableRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                SchemaName.Read(row),
                TableName.Read(row),
                ObjectId.Read(row));
        }

        internal static class ColumnReality
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<bool> IsNullable = Column.Boolean(1, "IsNullable");
            private static readonly ColumnDefinition<string> SqlType = Column.String(2, "SqlType");
            private static readonly ColumnDefinition<int?> MaxLength = Column.Int32OrNull(3, "MaxLength");
            private static readonly ColumnDefinition<int?> Precision = Column.Int32OrNull(4, "Precision");
            private static readonly ColumnDefinition<int?> Scale = Column.Int32OrNull(5, "Scale");
            private static readonly ColumnDefinition<string?> CollationName = Column.StringOrNull(6, "CollationName");
            private static readonly ColumnDefinition<bool> IsIdentity = Column.Boolean(7, "IsIdentity");
            private static readonly ColumnDefinition<bool> IsComputed = Column.Boolean(8, "IsComputed");
            private static readonly ColumnDefinition<string?> ComputedDefinition = Column.StringOrNull(9, "ComputedDefinition");
            private static readonly ColumnDefinition<string?> DefaultConstraintName = Column.StringOrNull(10, "DefaultConstraintName");
            private static readonly ColumnDefinition<string?> DefaultDefinition = Column.StringOrNull(11, "DefaultDefinition");
            private static readonly ColumnDefinition<string> PhysicalColumn = Column.String(12, "PhysicalColumn");

            public static OutsystemsColumnRealityRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                IsNullable.Read(row),
                SqlType.Read(row),
                MaxLength.Read(row),
                Precision.Read(row),
                Scale.Read(row),
                CollationName.Read(row),
                IsIdentity.Read(row),
                IsComputed.Read(row),
                ComputedDefinition.Read(row),
                DefaultConstraintName.Read(row),
                DefaultDefinition.Read(row),
                PhysicalColumn.Read(row));
        }

        internal static class ColumnChecks
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<string> ConstraintName = Column.String(1, "ConstraintName");
            private static readonly ColumnDefinition<string> Definition = Column.String(2, "Definition");
            private static readonly ColumnDefinition<bool> IsNotTrusted = Column.Boolean(3, "IsNotTrusted");

            public static OutsystemsColumnCheckRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                ConstraintName.Read(row),
                Definition.Read(row),
                IsNotTrusted.Read(row));
        }

        internal static class ColumnCheckJson
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<string> CheckJson = Column.String(1, "CheckJson");

            public static OutsystemsColumnCheckJsonRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                CheckJson.Read(row));
        }

        internal static class PhysicalColumnsPresent
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");

            public static OutsystemsPhysicalColumnPresenceRow MapRow(DbRow row) => new(AttrId.Read(row));
        }

        internal static class Indexes
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<int> ObjectId = Column.Int32(1, "object_id");
            private static readonly ColumnDefinition<int> IndexId = Column.Int32(2, "index_id");
            private static readonly ColumnDefinition<string> IndexName = Column.String(3, "IndexName");
            private static readonly ColumnDefinition<bool> IsUnique = Column.Boolean(4, "IsUnique");
            private static readonly ColumnDefinition<bool> IsPrimary = Column.Boolean(5, "IsPrimary");
            private static readonly ColumnDefinition<string> Kind = Column.String(6, "Kind");
            private static readonly ColumnDefinition<string?> FilterDefinition = Column.StringOrNull(7, "FilterDefinition");
            private static readonly ColumnDefinition<bool> IsDisabled = Column.Boolean(8, "IsDisabled");
            private static readonly ColumnDefinition<bool> IsPadded = Column.Boolean(9, "IsPadded");
            private static readonly ColumnDefinition<int?> FillFactor = Column.Int32OrNull(10, "Fill_Factor");
            private static readonly ColumnDefinition<bool> IgnoreDupKey = Column.Boolean(11, "IgnoreDupKey");
            private static readonly ColumnDefinition<bool> AllowRowLocks = Column.Boolean(12, "AllowRowLocks");
            private static readonly ColumnDefinition<bool> AllowPageLocks = Column.Boolean(13, "AllowPageLocks");
            private static readonly ColumnDefinition<bool> NoRecompute = Column.Boolean(14, "NoRecompute");
            private static readonly ColumnDefinition<string?> DataSpaceName = Column.StringOrNull(15, "DataSpaceName");
            private static readonly ColumnDefinition<string?> DataSpaceType = Column.StringOrNull(16, "DataSpaceType");
            private static readonly ColumnDefinition<string?> PartitionColumnsJson = Column.StringOrNull(17, "PartitionColumnsJson");
            private static readonly ColumnDefinition<string?> DataCompressionJson = Column.StringOrNull(18, "DataCompressionJson");

            public static OutsystemsIndexRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                ObjectId.Read(row),
                IndexId.Read(row),
                IndexName.Read(row),
                IsUnique.Read(row),
                IsPrimary.Read(row),
                Kind.Read(row),
                FilterDefinition.Read(row),
                IsDisabled.Read(row),
                IsPadded.Read(row),
                FillFactor.Read(row),
                IgnoreDupKey.Read(row),
                AllowRowLocks.Read(row),
                AllowPageLocks.Read(row),
                NoRecompute.Read(row),
                DataSpaceName.Read(row),
                DataSpaceType.Read(row),
                PartitionColumnsJson.Read(row),
                DataCompressionJson.Read(row));
        }

        internal static class IndexColumns
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> IndexName = Column.String(1, "IndexName");
            private static readonly ColumnDefinition<int> Ordinal = Column.Int32(2, "Ordinal");
            private static readonly ColumnDefinition<string> PhysicalColumn = Column.String(3, "PhysicalColumn");
            private static readonly ColumnDefinition<bool> IsIncluded = Column.Boolean(4, "IsIncluded");
            private static readonly ColumnDefinition<string?> Direction = Column.StringOrNull(5, "Direction");
            private static readonly ColumnDefinition<string?> HumanAttr = Column.StringOrNull(6, "HumanAttr");

            public static OutsystemsIndexColumnRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                IndexName.Read(row),
                Ordinal.Read(row),
                PhysicalColumn.Read(row),
                IsIncluded.Read(row),
                Direction.Read(row),
                HumanAttr.Read(row));
        }

        internal static class ForeignKeys
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(1, "FkObjectId");
            private static readonly ColumnDefinition<string> FkName = Column.String(2, "FkName");
            private static readonly ColumnDefinition<string> DeleteAction = Column.String(3, "DeleteAction");
            private static readonly ColumnDefinition<string> UpdateAction = Column.String(4, "UpdateAction");
            private static readonly ColumnDefinition<int> ReferencedObjectId = Column.Int32(5, "ReferencedObjectId");
            private static readonly ColumnDefinition<int?> ReferencedEntityId = Column.Int32OrNull(6, "ReferencedEntityId");
            private static readonly ColumnDefinition<string> ReferencedSchema = Column.String(7, "ReferencedSchema");
            private static readonly ColumnDefinition<string> ReferencedTable = Column.String(8, "ReferencedTable");
            private static readonly ColumnDefinition<bool> IsNoCheck = Column.Boolean(9, "IsNoCheck");

            public static OutsystemsForeignKeyRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                FkObjectId.Read(row),
                FkName.Read(row),
                DeleteAction.Read(row),
                UpdateAction.Read(row),
                ReferencedObjectId.Read(row),
                ReferencedEntityId.Read(row),
                ReferencedSchema.Read(row),
                ReferencedTable.Read(row),
                IsNoCheck.Read(row));
        }

        internal static class ForeignKeyColumns
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(1, "FkObjectId");
            private static readonly ColumnDefinition<int> Ordinal = Column.Int32(2, "Ordinal");
            private static readonly ColumnDefinition<string> ParentColumn = Column.String(3, "ParentColumn");
            private static readonly ColumnDefinition<string> ReferencedColumn = Column.String(4, "ReferencedColumn");
            private static readonly ColumnDefinition<int?> ParentAttrId = Column.Int32OrNull(5, "ParentAttrId");
            private static readonly ColumnDefinition<string?> ParentAttrName = Column.StringOrNull(6, "ParentAttrName");
            private static readonly ColumnDefinition<int?> ReferencedAttrId = Column.Int32OrNull(7, "ReferencedAttrId");
            private static readonly ColumnDefinition<string?> ReferencedAttrName = Column.StringOrNull(8, "ReferencedAttrName");

            public static OutsystemsForeignKeyColumnRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                FkObjectId.Read(row),
                Ordinal.Read(row),
                ParentColumn.Read(row),
                ReferencedColumn.Read(row),
                ParentAttrId.Read(row),
                ParentAttrName.Read(row),
                ReferencedAttrId.Read(row),
                ReferencedAttrName.Read(row));
        }

        internal static class ForeignKeyAttributeMap
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(1, "FkObjectId");

            public static OutsystemsForeignKeyAttrMapRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                FkObjectId.Read(row));
        }

        internal static class AttributeHasForeignKey
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<bool> HasForeignKey = Column.Boolean(1, "HasFk");

            public static OutsystemsAttributeHasFkRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                HasForeignKey.Read(row));
        }

        internal static class ForeignKeyColumnsJson
        {
            private static readonly ColumnDefinition<int> FkObjectId = Column.Int32(0, "FkObjectId");
            private static readonly ColumnDefinition<string> ColumnsJson = Column.String(1, "ColumnsJson");

            public static OutsystemsForeignKeyColumnsJsonRow MapRow(DbRow row) => new(
                FkObjectId.Read(row),
                ColumnsJson.Read(row));
        }

        internal static class ForeignKeyAttributeJson
        {
            private static readonly ColumnDefinition<int> AttrId = Column.Int32(0, "AttrId");
            private static readonly ColumnDefinition<string> ConstraintJson = Column.String(1, "ConstraintJson");

            public static OutsystemsForeignKeyAttributeJsonRow MapRow(DbRow row) => new(
                AttrId.Read(row),
                ConstraintJson.Read(row));
        }

        internal static class Triggers
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> TriggerName = Column.String(1, "TriggerName");
            private static readonly ColumnDefinition<bool> IsDisabled = Column.Boolean(2, "IsDisabled");
            private static readonly ColumnDefinition<string> TriggerDefinition = Column.String(3, "TriggerDefinition");

            public static OutsystemsTriggerRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                TriggerName.Read(row),
                IsDisabled.Read(row),
                TriggerDefinition.Read(row));
        }

        internal static class AttributeJson
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> AttributesJsonRequired = Column.String(1, "AttributesJson");
            private static readonly ColumnDefinition<string?> AttributesJsonOptional = Column.StringOrNull(1, "AttributesJson");

            public static OutsystemsAttributeJsonRow MapRow(DbRow row, bool allowNull = false)
            {
                var entityId = EntityId.Read(row);
                var attributesJson = allowNull
                    ? AttributesJsonOptional.Read(row)
                    : AttributesJsonRequired.Read(row);

                return new OutsystemsAttributeJsonRow(
                    entityId,
                    attributesJson);
            }
        }

        internal static class RelationshipJson
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> RelationshipsJson = Column.String(1, "RelationshipsJson");

            public static OutsystemsRelationshipJsonRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                RelationshipsJson.Read(row));
        }

        internal static class IndexJson
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> IndexesJson = Column.String(1, "IndexesJson");

            public static OutsystemsIndexJsonRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                IndexesJson.Read(row));
        }

        internal static class TriggerJson
        {
            private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
            private static readonly ColumnDefinition<string> TriggersJson = Column.String(1, "TriggersJson");

            public static OutsystemsTriggerJsonRow MapRow(DbRow row) => new(
                EntityId.Read(row),
                TriggersJson.Read(row));
        }

        internal static class ModuleJson
        {
            private static readonly ColumnDefinition<string> ModuleName = Column.String(0, "module.name");
            private static readonly ColumnDefinition<bool> IsSystem = Column.Boolean(1, "module.isSystem");
            private static readonly ColumnDefinition<bool> IsActive = Column.Boolean(2, "module.isActive");
            private static readonly ColumnDefinition<string> ModuleEntities = Column.String(3, "module.entities");

            public static OutsystemsModuleJsonRow MapRow(DbRow row) => new(
                ModuleName.Read(row),
                IsSystem.Read(row),
                IsActive.Read(row),
                ModuleEntities.Read(row));
        }

        private static class Column
        {
            public static ColumnDefinition<int> Int32(int ordinal, string name)
                => new(ordinal, name, row => row.GetRequiredInt32Flexible(ordinal, name));

            public static ColumnDefinition<int?> Int32OrNull(int ordinal, string name)
                => new(ordinal, name, row => row.GetInt32FlexibleOrNull(ordinal));

            public static ColumnDefinition<string> String(int ordinal, string name)
                => new(ordinal, name, row => row.GetRequiredString(ordinal, name));

            public static ColumnDefinition<string?> StringOrNull(int ordinal, string name)
                => new(ordinal, name, row => row.GetStringOrNull(ordinal));

            public static ColumnDefinition<bool> Boolean(int ordinal, string name)
                => new(ordinal, name, row => row.GetRequiredBoolean(ordinal, name));

            public static ColumnDefinition<bool?> BooleanOrNull(int ordinal, string name)
                => new(ordinal, name, row => row.GetBooleanOrNull(ordinal));

            public static ColumnDefinition<Guid> Guid(int ordinal, string name)
                => new(ordinal, name, row => row.GetRequiredGuid(ordinal, name));

            public static ColumnDefinition<Guid?> GuidOrNull(int ordinal, string name)
                => new(ordinal, name, row => row.GetGuidOrNull(ordinal));
        }

        private sealed class ColumnDefinition<T>
        {
            private readonly Func<DbRow, T> _reader;

            public ColumnDefinition(int ordinal, string name, Func<DbRow, T> reader)
            {
                Ordinal = ordinal;
                Name = name ?? throw new ArgumentNullException(nameof(name));
                _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            }

            public int Ordinal { get; }

            public string Name { get; }

            public T Read(DbRow row)
            {
                try
                {
                    return _reader(row);
                }
                catch (ColumnReadException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Type? providerType = null;

                    try
                    {
                        providerType = row.GetFieldType(Ordinal);
                    }
                    catch
                    {
                        // Preserve the original exception when the provider type cannot be determined.
                    }

                    throw new ColumnReadException(Name, Ordinal, typeof(T), providerType, row.ResultSetName, row.RowIndex, ex);
                }
            }
        }
    }

    private static class ResultSetDefinitionFactory
    {
        public static IResultSetDefinition Create<T>(
            string name,
            ResultSetReader<T> reader,
            Action<MetadataAccumulator, List<T>> assign)
            => new ResultSetDefinition<T>(name, reader, assign);
    }

    private sealed class MetadataAccumulator
    {
        public List<OutsystemsModuleRow> Modules { get; private set; } = new();
        public List<OutsystemsEntityRow> Entities { get; private set; } = new();
        public List<OutsystemsAttributeRow> Attributes { get; private set; } = new();
        public List<OutsystemsReferenceRow> References { get; private set; } = new();
        public List<OutsystemsPhysicalTableRow> PhysicalTables { get; private set; } = new();
        public List<OutsystemsColumnRealityRow> ColumnReality { get; private set; } = new();
        public List<OutsystemsColumnCheckRow> ColumnChecks { get; private set; } = new();
        public List<OutsystemsColumnCheckJsonRow> ColumnCheckJson { get; private set; } = new();
        public List<OutsystemsPhysicalColumnPresenceRow> PhysicalColumnsPresent { get; private set; } = new();
        public List<OutsystemsIndexRow> Indexes { get; private set; } = new();
        public List<OutsystemsIndexColumnRow> IndexColumns { get; private set; } = new();
        public List<OutsystemsForeignKeyRow> ForeignKeys { get; private set; } = new();
        public List<OutsystemsForeignKeyColumnRow> ForeignKeyColumns { get; private set; } = new();
        public List<OutsystemsForeignKeyAttrMapRow> ForeignKeyAttributeMap { get; private set; } = new();
        public List<OutsystemsAttributeHasFkRow> AttributeForeignKeys { get; private set; } = new();
        public List<OutsystemsForeignKeyColumnsJsonRow> ForeignKeyColumnsJson { get; private set; } = new();
        public List<OutsystemsForeignKeyAttributeJsonRow> ForeignKeyAttributeJson { get; private set; } = new();
        public List<OutsystemsTriggerRow> Triggers { get; private set; } = new();
        public List<OutsystemsAttributeJsonRow> AttributeJson { get; private set; } = new();
        public List<OutsystemsRelationshipJsonRow> RelationshipJson { get; private set; } = new();
        public List<OutsystemsIndexJsonRow> IndexJson { get; private set; } = new();
        public List<OutsystemsTriggerJsonRow> TriggerJson { get; private set; } = new();
        public List<OutsystemsModuleJsonRow> ModuleJson { get; private set; } = new();

        public void SetModules(List<OutsystemsModuleRow> rows) => Modules = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetEntities(List<OutsystemsEntityRow> rows) => Entities = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetAttributes(List<OutsystemsAttributeRow> rows) => Attributes = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetReferences(List<OutsystemsReferenceRow> rows) => References = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetPhysicalTables(List<OutsystemsPhysicalTableRow> rows) => PhysicalTables = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetColumnReality(List<OutsystemsColumnRealityRow> rows) => ColumnReality = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetColumnChecks(List<OutsystemsColumnCheckRow> rows) => ColumnChecks = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetColumnCheckJson(List<OutsystemsColumnCheckJsonRow> rows) => ColumnCheckJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetPhysicalColumnsPresent(List<OutsystemsPhysicalColumnPresenceRow> rows) => PhysicalColumnsPresent = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetIndexes(List<OutsystemsIndexRow> rows) => Indexes = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetIndexColumns(List<OutsystemsIndexColumnRow> rows) => IndexColumns = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetForeignKeys(List<OutsystemsForeignKeyRow> rows) => ForeignKeys = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetForeignKeyColumns(List<OutsystemsForeignKeyColumnRow> rows) => ForeignKeyColumns = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetForeignKeyAttributeMap(List<OutsystemsForeignKeyAttrMapRow> rows) => ForeignKeyAttributeMap = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetAttributeForeignKeys(List<OutsystemsAttributeHasFkRow> rows) => AttributeForeignKeys = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetForeignKeyColumnsJson(List<OutsystemsForeignKeyColumnsJsonRow> rows) => ForeignKeyColumnsJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetForeignKeyAttributeJson(List<OutsystemsForeignKeyAttributeJsonRow> rows) => ForeignKeyAttributeJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetTriggers(List<OutsystemsTriggerRow> rows) => Triggers = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetAttributeJson(List<OutsystemsAttributeJsonRow> rows) => AttributeJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetRelationshipJson(List<OutsystemsRelationshipJsonRow> rows) => RelationshipJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetIndexJson(List<OutsystemsIndexJsonRow> rows) => IndexJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetTriggerJson(List<OutsystemsTriggerJsonRow> rows) => TriggerJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public void SetModuleJson(List<OutsystemsModuleJsonRow> rows) => ModuleJson = rows ?? throw new ArgumentNullException(nameof(rows));

        public OutsystemsMetadataSnapshot BuildSnapshot(string databaseName)
            => new(
                Modules,
                Entities,
                Attributes,
                References,
                PhysicalTables,
                ColumnReality,
                ColumnChecks,
                ColumnCheckJson,
                PhysicalColumnsPresent,
                Indexes,
                IndexColumns,
                ForeignKeys,
                ForeignKeyColumns,
                ForeignKeyAttributeMap,
                AttributeForeignKeys,
                ForeignKeyColumnsJson,
                ForeignKeyAttributeJson,
                Triggers,
                AttributeJson,
                RelationshipJson,
                IndexJson,
                TriggerJson,
                ModuleJson,
                databaseName);
    }

    private async Task EnsureNextResultSetAsync(
        DbDataReader reader,
        CancellationToken cancellationToken,
        string completedResultSetName,
        int completedRowCount,
        string expectedNextResultSetName,
        int expectedNextResultSetIndex)
    {
        if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new MetadataResultSetMissingException(
                completedResultSetName,
                completedRowCount,
                expectedNextResultSetName,
                expectedNextResultSetIndex);
        }
    }
}
