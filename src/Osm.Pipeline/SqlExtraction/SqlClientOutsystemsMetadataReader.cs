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

            var modules = await ReadModulesAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "Modules", modules.Count, "Entities", 1).ConfigureAwait(false);

            var entities = await ReadEntitiesAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "Entities", entities.Count, "Attributes", 2).ConfigureAwait(false);

            var attributes = await ReadAttributesAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "Attributes", attributes.Count, "References", 3).ConfigureAwait(false);

            var references = await ReadReferencesAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "References", references.Count, "PhysicalTables", 4).ConfigureAwait(false);

            var physicalTables = await ReadPhysicalTablesAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "PhysicalTables", physicalTables.Count, "ColumnReality", 5).ConfigureAwait(false);

            var columnReality = await ReadColumnRealityAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ColumnReality", columnReality.Count, "ColumnChecks", 6).ConfigureAwait(false);

            var columnChecks = await ReadColumnChecksAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ColumnChecks", columnChecks.Count, "ColumnCheckJson", 7).ConfigureAwait(false);

            var columnCheckJson = await ReadColumnCheckJsonAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ColumnCheckJson", columnCheckJson.Count, "PhysicalColumnsPresent", 8).ConfigureAwait(false);

            var physicalColumnsPresent = await ReadPhysicalColumnsPresentAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "PhysicalColumnsPresent", physicalColumnsPresent.Count, "Indexes", 9).ConfigureAwait(false);

            var indexes = await ReadIndexesAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "Indexes", indexes.Count, "IndexColumns", 10).ConfigureAwait(false);

            var indexColumns = await ReadIndexColumnsAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "IndexColumns", indexColumns.Count, "ForeignKeys", 11).ConfigureAwait(false);

            var foreignKeys = await ReadForeignKeysAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ForeignKeys", foreignKeys.Count, "ForeignKeyColumns", 12).ConfigureAwait(false);

            var foreignKeyColumns = await ReadForeignKeyColumnsAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ForeignKeyColumns", foreignKeyColumns.Count, "ForeignKeyAttrMap", 13).ConfigureAwait(false);

            var foreignKeyAttrMap = await ReadForeignKeyAttrMapAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ForeignKeyAttrMap", foreignKeyAttrMap.Count, "AttributeHasFk", 14).ConfigureAwait(false);

            var attributeHasFk = await ReadAttributeHasFkAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "AttributeHasFk", attributeHasFk.Count, "ForeignKeyColumnsJson", 15).ConfigureAwait(false);

            var fkColumnsJson = await ReadForeignKeyColumnsJsonAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ForeignKeyColumnsJson", fkColumnsJson.Count, "ForeignKeyAttributeJson", 16).ConfigureAwait(false);

            var fkAttrJson = await ReadForeignKeyAttributeJsonAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "ForeignKeyAttributeJson", fkAttrJson.Count, "Triggers", 17).ConfigureAwait(false);

            var triggers = await ReadTriggersAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "Triggers", triggers.Count, "AttributeJson", 18).ConfigureAwait(false);

            var attributeJson = await ReadAttributeJsonAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "AttributeJson", attributeJson.Count, "RelationshipJson", 19).ConfigureAwait(false);

            var relationshipJson = await ReadRelationshipJsonAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "RelationshipJson", relationshipJson.Count, "IndexJson", 20).ConfigureAwait(false);

            var indexJson = await ReadIndexJsonAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "IndexJson", indexJson.Count, "TriggerJson", 21).ConfigureAwait(false);

            var triggerJson = await ReadTriggerJsonAsync(reader, cancellationToken).ConfigureAwait(false);
            await EnsureNextResultSetAsync(reader, cancellationToken, "TriggerJson", triggerJson.Count, "ModuleJson", 22).ConfigureAwait(false);

            var moduleJson = await ReadModuleJsonAsync(reader, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInformation(
                "Metadata snapshot script returned {ModuleCount} module(s), {EntityCount} entity rows, and {AttributeCount} attribute rows in {DurationMs} ms.",
                moduleJson.Count,
                entities.Count,
                attributes.Count,
                stopwatch.Elapsed.TotalMilliseconds);

            var snapshot = new OutsystemsMetadataSnapshot(
                modules,
                entities,
                attributes,
                references,
                physicalTables,
                columnReality,
                columnChecks,
                columnCheckJson,
                physicalColumnsPresent,
                indexes,
                indexColumns,
                foreignKeys,
                foreignKeyColumns,
                foreignKeyAttrMap,
                attributeHasFk,
                fkColumnsJson,
                fkAttrJson,
                triggers,
                attributeJson,
                relationshipJson,
                indexJson,
                triggerJson,
                moduleJson,
                databaseName ?? string.Empty);

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

    private static async Task<List<OutsystemsModuleRow>> ReadModulesAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsModuleRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsModuleRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                GetStringOrNull(reader, 4),
                GetGuidOrNull(reader, 5)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsEntityRow>> ReadEntitiesAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsEntityRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsEntityRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                GetStringOrNull(reader, 7),
                GetGuidOrNull(reader, 8),
                GetGuidOrNull(reader, 9),
                GetStringOrNull(reader, 10)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsAttributeRow>> ReadAttributesAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsAttributeRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsAttributeRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                GetGuidOrNull(reader, 3),
                GetStringOrNull(reader, 4),
                GetInt32OrNull(reader, 5),
                GetInt32OrNull(reader, 6),
                GetInt32OrNull(reader, 7),
                GetStringOrNull(reader, 8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                GetBooleanOrNull(reader, 11),
                GetBooleanOrNull(reader, 12),
                GetInt32OrNull(reader, 13),
                GetStringOrNull(reader, 14),
                GetStringOrNull(reader, 15),
                GetStringOrNull(reader, 16),
                GetStringOrNull(reader, 17),
                GetStringOrNull(reader, 18),
                GetStringOrNull(reader, 19),
                GetInt32OrNull(reader, 20),
                GetStringOrNull(reader, 21),
                GetStringOrNull(reader, 22)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsReferenceRow>> ReadReferencesAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsReferenceRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsReferenceRow(
                reader.GetInt32(0),
                GetInt32OrNull(reader, 1),
                GetStringOrNull(reader, 2),
                GetStringOrNull(reader, 3)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsPhysicalTableRow>> ReadPhysicalTablesAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsPhysicalTableRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsPhysicalTableRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsColumnRealityRow>> ReadColumnRealityAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsColumnRealityRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsColumnRealityRow(
                reader.GetInt32(0),
                reader.GetBoolean(1),
                reader.GetString(2),
                GetInt32OrNull(reader, 3),
                GetInt32OrNull(reader, 4),
                GetInt32OrNull(reader, 5),
                GetStringOrNull(reader, 6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                GetStringOrNull(reader, 9),
                GetStringOrNull(reader, 10),
                GetStringOrNull(reader, 11),
                reader.GetString(12)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsColumnCheckRow>> ReadColumnChecksAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsColumnCheckRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsColumnCheckRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsColumnCheckJsonRow>> ReadColumnCheckJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsColumnCheckJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsColumnCheckJsonRow(
                reader.GetInt32(0),
                reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsPhysicalColumnPresenceRow>> ReadPhysicalColumnsPresentAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsPhysicalColumnPresenceRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsPhysicalColumnPresenceRow(reader.GetInt32(0)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsIndexRow>> ReadIndexesAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsIndexRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsIndexRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetString(6),
                GetStringOrNull(reader, 7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                GetInt32OrNull(reader, 10),
                reader.GetBoolean(11),
                reader.GetBoolean(12),
                reader.GetBoolean(13),
                reader.GetBoolean(14),
                GetStringOrNull(reader, 15),
                GetStringOrNull(reader, 16),
                GetStringOrNull(reader, 17),
                GetStringOrNull(reader, 18)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsIndexColumnRow>> ReadIndexColumnsAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsIndexColumnRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsIndexColumnRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                GetStringOrNull(reader, 5),
                GetStringOrNull(reader, 6)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsForeignKeyRow>> ReadForeignKeysAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsForeignKeyRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsForeignKeyRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                GetInt32OrNull(reader, 6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetBoolean(9)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsForeignKeyColumnRow>> ReadForeignKeyColumnsAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsForeignKeyColumnRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsForeignKeyColumnRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                GetInt32OrNull(reader, 5),
                GetStringOrNull(reader, 6),
                GetInt32OrNull(reader, 7),
                GetStringOrNull(reader, 8)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsForeignKeyAttrMapRow>> ReadForeignKeyAttrMapAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsForeignKeyAttrMapRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsForeignKeyAttrMapRow(reader.GetInt32(0), reader.GetInt32(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsAttributeHasFkRow>> ReadAttributeHasFkAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsAttributeHasFkRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsAttributeHasFkRow(reader.GetInt32(0), reader.GetBoolean(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsForeignKeyColumnsJsonRow>> ReadForeignKeyColumnsJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsForeignKeyColumnsJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsForeignKeyColumnsJsonRow(reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsForeignKeyAttributeJsonRow>> ReadForeignKeyAttributeJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsForeignKeyAttributeJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsForeignKeyAttributeJsonRow(reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsTriggerRow>> ReadTriggersAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsTriggerRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsTriggerRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsAttributeJsonRow>> ReadAttributeJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsAttributeJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsAttributeJsonRow(reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsRelationshipJsonRow>> ReadRelationshipJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsRelationshipJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsRelationshipJsonRow(reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsIndexJsonRow>> ReadIndexJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsIndexJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsIndexJsonRow(reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsTriggerJsonRow>> ReadTriggerJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsTriggerJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsTriggerJsonRow(reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<OutsystemsModuleJsonRow>> ReadModuleJsonAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<OutsystemsModuleJsonRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new OutsystemsModuleJsonRow(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetString(3)));
        }

        return rows;
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

    private static string? GetStringOrNull(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static Guid? GetGuidOrNull(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static int? GetInt32OrNull(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static bool? GetBooleanOrNull(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
}
