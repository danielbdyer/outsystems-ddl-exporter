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

    private static readonly IReadOnlyList<IResultSetDefinition> ResultSets = new IResultSetDefinition[]
    {
        ResultSetDefinitionFactory.Create(
            "Modules",
            ResultSetReader<OutsystemsModuleRow>.Create(static row => new OutsystemsModuleRow(
                row.GetInt32(0),
                row.GetString(1),
                row.GetBoolean(2),
                row.GetBoolean(3),
                row.GetStringOrNull(4),
                row.GetGuidOrNull(5))),
            static (accumulator, rows) => accumulator.SetModules(rows)),
        ResultSetDefinitionFactory.Create(
            "Entities",
            ResultSetReader<OutsystemsEntityRow>.Create(static row => new OutsystemsEntityRow(
                row.GetInt32(0),
                row.GetString(1),
                row.GetString(2),
                row.GetInt32(3),
                row.GetBoolean(4),
                row.GetBoolean(5),
                row.GetBoolean(6),
                row.GetStringOrNull(7),
                row.GetGuidOrNull(8),
                row.GetGuidOrNull(9),
                row.GetStringOrNull(10))),
            static (accumulator, rows) => accumulator.SetEntities(rows)),
        ResultSetDefinitionFactory.Create(
            "Attributes",
            ResultSetReader<OutsystemsAttributeRow>.Create(static row => new OutsystemsAttributeRow(
                row.GetInt32(0),
                row.GetInt32(1),
                row.GetString(2),
                row.GetGuidOrNull(3),
                row.GetStringOrNull(4),
                row.GetInt32OrNull(5),
                row.GetInt32OrNull(6),
                row.GetInt32OrNull(7),
                row.GetStringOrNull(8),
                row.GetBoolean(9),
                row.GetBoolean(10),
                row.GetBooleanOrNull(11),
                row.GetBooleanOrNull(12),
                row.GetInt32OrNull(13),
                row.GetStringOrNull(14),
                row.GetStringOrNull(15),
                row.GetStringOrNull(16),
                row.GetStringOrNull(17),
                row.GetStringOrNull(18),
                row.GetStringOrNull(19),
                row.GetInt32OrNull(20),
                row.GetStringOrNull(21),
                row.GetStringOrNull(22))),
            static (accumulator, rows) => accumulator.SetAttributes(rows)),
        ResultSetDefinitionFactory.Create(
            "References",
            ResultSetReader<OutsystemsReferenceRow>.Create(static row => new OutsystemsReferenceRow(
                row.GetInt32(0),
                row.GetInt32OrNull(1),
                row.GetStringOrNull(2),
                row.GetStringOrNull(3))),
            static (accumulator, rows) => accumulator.SetReferences(rows)),
        ResultSetDefinitionFactory.Create(
            "PhysicalTables",
            ResultSetReader<OutsystemsPhysicalTableRow>.Create(static row => new OutsystemsPhysicalTableRow(
                row.GetInt32(0),
                row.GetString(1),
                row.GetString(2),
                row.GetInt32(3))),
            static (accumulator, rows) => accumulator.SetPhysicalTables(rows)),
        ResultSetDefinitionFactory.Create(
            "ColumnReality",
            ResultSetReader<OutsystemsColumnRealityRow>.Create(static row => new OutsystemsColumnRealityRow(
                row.GetInt32(0),
                row.GetBoolean(1),
                row.GetString(2),
                row.GetInt32OrNull(3),
                row.GetInt32OrNull(4),
                row.GetInt32OrNull(5),
                row.GetStringOrNull(6),
                row.GetBoolean(7),
                row.GetBoolean(8),
                row.GetStringOrNull(9),
                row.GetStringOrNull(10),
                row.GetStringOrNull(11),
                row.GetString(12))),
            static (accumulator, rows) => accumulator.SetColumnReality(rows)),
        ResultSetDefinitionFactory.Create(
            "ColumnChecks",
            ResultSetReader<OutsystemsColumnCheckRow>.Create(static row => new OutsystemsColumnCheckRow(
                row.GetInt32(0),
                row.GetString(1),
                row.GetString(2),
                row.GetBoolean(3))),
            static (accumulator, rows) => accumulator.SetColumnChecks(rows)),
        ResultSetDefinitionFactory.Create(
            "ColumnCheckJson",
            ResultSetReader<OutsystemsColumnCheckJsonRow>.Create(static row => new OutsystemsColumnCheckJsonRow(
                row.GetInt32(0),
                row.GetString(1))),
            static (accumulator, rows) => accumulator.SetColumnCheckJson(rows)),
        ResultSetDefinitionFactory.Create(
            "PhysicalColumnsPresent",
            ResultSetReader<OutsystemsPhysicalColumnPresenceRow>.Create(static row => new OutsystemsPhysicalColumnPresenceRow(
                row.GetInt32(0))),
            static (accumulator, rows) => accumulator.SetPhysicalColumnsPresent(rows)),
        ResultSetDefinitionFactory.Create(
            "Indexes",
            ResultSetReader<OutsystemsIndexRow>.Create(static row => new OutsystemsIndexRow(
                row.GetInt32(0),
                row.GetInt32(1),
                row.GetInt32(2),
                row.GetString(3),
                row.GetBoolean(4),
                row.GetBoolean(5),
                row.GetString(6),
                row.GetStringOrNull(7),
                row.GetBoolean(8),
                row.GetBoolean(9),
                row.GetInt32OrNull(10),
                row.GetBoolean(11),
                row.GetBoolean(12),
                row.GetBoolean(13),
                row.GetBoolean(14),
                row.GetStringOrNull(15),
                row.GetStringOrNull(16),
                row.GetStringOrNull(17),
                row.GetStringOrNull(18))),
            static (accumulator, rows) => accumulator.SetIndexes(rows)),
        ResultSetDefinitionFactory.Create(
            "IndexColumns",
            ResultSetReader<OutsystemsIndexColumnRow>.Create(static row => new OutsystemsIndexColumnRow(
                row.GetInt32(0),
                row.GetString(1),
                row.GetInt32(2),
                row.GetString(3),
                row.GetBoolean(4),
                row.GetStringOrNull(5),
                row.GetStringOrNull(6))),
            static (accumulator, rows) => accumulator.SetIndexColumns(rows)),
        ResultSetDefinitionFactory.Create(
            "ForeignKeys",
            ResultSetReader<OutsystemsForeignKeyRow>.Create(static row => new OutsystemsForeignKeyRow(
                row.GetInt32(0),
                row.GetInt32(1),
                row.GetString(2),
                row.GetString(3),
                row.GetString(4),
                row.GetInt32(5),
                row.GetInt32OrNull(6),
                row.GetString(7),
                row.GetString(8),
                row.GetBoolean(9))),
            static (accumulator, rows) => accumulator.SetForeignKeys(rows)),
        ResultSetDefinitionFactory.Create(
            "ForeignKeyColumns",
            ResultSetReader<OutsystemsForeignKeyColumnRow>.Create(static row => new OutsystemsForeignKeyColumnRow(
                row.GetInt32(0),
                row.GetInt32(1),
                row.GetInt32(2),
                row.GetString(3),
                row.GetString(4),
                row.GetInt32OrNull(5),
                row.GetStringOrNull(6),
                row.GetInt32OrNull(7),
                row.GetStringOrNull(8))),
            static (accumulator, rows) => accumulator.SetForeignKeyColumns(rows)),
        ResultSetDefinitionFactory.Create(
            "ForeignKeyAttrMap",
            ResultSetReader<OutsystemsForeignKeyAttrMapRow>.Create(static row => new OutsystemsForeignKeyAttrMapRow(
                row.GetInt32(0),
                row.GetInt32(1))),
            static (accumulator, rows) => accumulator.SetForeignKeyAttributeMap(rows)),
        ResultSetDefinitionFactory.Create(
            "AttributeHasFk",
            ResultSetReader<OutsystemsAttributeHasFkRow>.Create(static row => new OutsystemsAttributeHasFkRow(
                row.GetInt32(0),
                row.GetBoolean(1))),
            static (accumulator, rows) => accumulator.SetAttributeForeignKeys(rows)),
        ResultSetDefinitionFactory.Create(
            "ForeignKeyColumnsJson",
            ResultSetReader<OutsystemsForeignKeyColumnsJsonRow>.Create(static row => new OutsystemsForeignKeyColumnsJsonRow(
                row.GetInt32(0),
                row.GetString(1))),
            static (accumulator, rows) => accumulator.SetForeignKeyColumnsJson(rows)),
        ResultSetDefinitionFactory.Create(
            "ForeignKeyAttributeJson",
            ResultSetReader<OutsystemsForeignKeyAttributeJsonRow>.Create(static row => new OutsystemsForeignKeyAttributeJsonRow(
                row.GetInt32(0),
                row.GetString(1))),
            static (accumulator, rows) => accumulator.SetForeignKeyAttributeJson(rows)),
        ResultSetDefinitionFactory.Create(
            "Triggers",
            ResultSetReader<OutsystemsTriggerRow>.Create(static row => new OutsystemsTriggerRow(
                row.GetInt32(0),
                row.GetString(1),
                row.GetBoolean(2),
                row.GetString(3))),
            static (accumulator, rows) => accumulator.SetTriggers(rows)),
        ResultSetDefinitionFactory.Create(
            "AttributeJson",
            ResultSetReader<OutsystemsAttributeJsonRow>.Create(static row => new OutsystemsAttributeJsonRow(
                row.GetInt32(0),
                row.GetString(1))),
            static (accumulator, rows) => accumulator.SetAttributeJson(rows)),
        ResultSetDefinitionFactory.Create(
            "RelationshipJson",
            ResultSetReader<OutsystemsRelationshipJsonRow>.Create(static row => new OutsystemsRelationshipJsonRow(
                row.GetInt32(0),
                row.GetString(1))),
            static (accumulator, rows) => accumulator.SetRelationshipJson(rows)),
        ResultSetDefinitionFactory.Create(
            "IndexJson",
            ResultSetReader<OutsystemsIndexJsonRow>.Create(static row => new OutsystemsIndexJsonRow(
                row.GetInt32(0),
                row.GetString(1))),
            static (accumulator, rows) => accumulator.SetIndexJson(rows)),
        ResultSetDefinitionFactory.Create(
            "TriggerJson",
            ResultSetReader<OutsystemsTriggerJsonRow>.Create(static row => new OutsystemsTriggerJsonRow(
                row.GetInt32(0),
                row.GetString(1))),
            static (accumulator, rows) => accumulator.SetTriggerJson(rows)),
        ResultSetDefinitionFactory.Create(
            "ModuleJson",
            ResultSetReader<OutsystemsModuleJsonRow>.Create(static row => new OutsystemsModuleJsonRow(
                row.GetString(0),
                row.GetBoolean(1),
                row.GetBoolean(2),
                row.GetString(3))),
            static (accumulator, rows) => accumulator.SetModuleJson(rows)),
    };

    public SqlClientOutsystemsMetadataReader(
        IDbConnectionFactory connectionFactory,
        IAdvancedSqlScriptProvider scriptProvider,
        SqlExecutionOptions? options = null,
        ILogger<SqlClientOutsystemsMetadataReader>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _scriptProvider = scriptProvider ?? throw new ArgumentNullException(nameof(scriptProvider));
        _options = options ?? SqlExecutionOptions.Default;
        _logger = logger ?? NullLogger<SqlClientOutsystemsMetadataReader>.Instance;
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
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult, cancellationToken)
                .ConfigureAwait(false);

            var accumulator = new MetadataAccumulator();

            for (var i = 0; i < ResultSets.Count; i++)
            {
                var definition = ResultSets[i];
                var rowCount = await definition
                    .ReadAsync(reader, cancellationToken, accumulator)
                    .ConfigureAwait(false);

                if (i < ResultSets.Count - 1)
                {
                    var nextDefinition = ResultSets[i + 1];
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

            var rows = await _reader.ReadAllAsync(reader, cancellationToken).ConfigureAwait(false);
            _assign(accumulator, rows);
            return rows.Count;
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
