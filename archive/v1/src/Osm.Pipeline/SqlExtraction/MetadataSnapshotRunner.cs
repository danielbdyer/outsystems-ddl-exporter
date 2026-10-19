using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class MetadataSnapshotRunner : IMetadataSnapshotDiagnostics
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDbCommandExecutor _commandExecutor;
    private readonly IReadOnlyList<IResultSetProcessor> _processors;
    private readonly SqlExecutionOptions _options;
    private readonly ILogger<MetadataSnapshotRunner> _logger;
    private readonly ITaskProgressAccessor _progressAccessor;
    private MetadataRowSnapshot? _lastFailureRowSnapshot;

    public MetadataSnapshotRunner(
        IDbConnectionFactory connectionFactory,
        IDbCommandExecutor commandExecutor,
        IEnumerable<IResultSetProcessor> processors,
        SqlExecutionOptions options,
        ILogger<MetadataSnapshotRunner>? logger = null,
        ITaskProgressAccessor? progressAccessor = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<MetadataSnapshotRunner>.Instance;
        _progressAccessor = progressAccessor ?? new TaskProgressAccessor();

        if (processors is null)
        {
            throw new ArgumentNullException(nameof(processors));
        }

        _processors = processors
            .OrderBy(static processor => processor.Order)
            .ThenBy(static processor => processor.Name, StringComparer.Ordinal)
            .ToArray();

        if (_processors.Count == 0)
        {
            throw new ArgumentException("At least one result set processor must be provided.", nameof(processors));
        }
    }

    public async Task<Result<OutsystemsMetadataSnapshot>> ExecuteAsync(
        string script,
        AdvancedSqlRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new ArgumentException("Script must be provided.", nameof(script));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var moduleCsv = request.ModuleNames.Length > 0
            ? string.Join(',', request.ModuleNames.Select(static module => module.Value))
            : string.Empty;

        _logger.LogInformation(
            "Executing metadata snapshot script via SQL client (timeoutSeconds: {TimeoutSeconds}, moduleCount: {ModuleCount}, includeSystem: {IncludeSystem}, includeInactive: {IncludeInactive}, onlyActive: {OnlyActive}).",
            _options.CommandTimeoutSeconds,
            request.ModuleNames.Length,
            request.IncludeSystemModules,
            request.IncludeInactiveModules,
            request.OnlyActiveAttributes);

        var stopwatch = Stopwatch.StartNew();
        var accumulator = new MetadataAccumulator();

        try
        {
            _lastFailureRowSnapshot = null;
            await using var connection = await _connectionFactory
                .CreateOpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            var databaseName = connection.Database;

            await using var command = CreateCommand(connection, script, moduleCsv, request, _options);
            await using var reader = await _commandExecutor
                .ExecuteReaderAsync(command, CommandBehavior.SequentialAccess, cancellationToken)
                .ConfigureAwait(false);

            var context = new ResultSetProcessingContext(reader, accumulator);

            var progress = _progressAccessor.Progress;
            using var task = progress?.Start("Extracting Metadata", _processors.Count);

            for (var i = 0; i < _processors.Count; i++)
            {
                var processor = _processors[i];
                task?.Description($"Extracting Metadata: {processor.Name}");
                var rowCount = await processor.ProcessAsync(context, cancellationToken).ConfigureAwait(false);
                task?.Increment(1);

                if (i < _processors.Count - 1)
                {
                    var nextProcessor = _processors[i + 1];
                    await EnsureNextResultSetAsync(
                        reader,
                        cancellationToken,
                        processor.Name,
                        rowCount,
                        nextProcessor.Name,
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
            _lastFailureRowSnapshot = null;
            return Result<OutsystemsMetadataSnapshot>.Success(snapshot);
        }
        catch (MetadataRowMappingException ex)
        {
            stopwatch.Stop();

            _lastFailureRowSnapshot = ex.RowSnapshot;

            var highlightedValue = ex.HighlightedColumn is null
                ? "<unavailable>"
                : ex.HighlightedColumn.IsNull
                    ? "<NULL>"
                    : ex.HighlightedColumn.ValuePreview ?? "<unavailable>";

            var friendlyContext = BuildFriendlyContext(ex, accumulator);

            _logger.LogError(
                ex,
                "Failed to map row {RowIndex} in result set '{ResultSetName}'. Column: {ColumnName}, Ordinal: {ColumnOrdinal}, ExpectedClrType: {ExpectedClrType}, ProviderType: {ProviderType}. DurationMs: {DurationMs}. ColumnValuePreview: {ColumnValuePreview}. FriendlyContext: {FriendlyContext}. RowSnapshot: {RowSnapshotJson}",
                ex.RowIndex,
                ex.ResultSetName,
                ex.ColumnName ?? "unknown",
                ex.Ordinal ?? -1,
                ex.ExpectedClrType?.FullName ?? "unknown",
                ex.ProviderFieldType?.FullName ?? "unknown",
                stopwatch.Elapsed.TotalMilliseconds,
                highlightedValue,
                friendlyContext ?? "<unavailable>",
                ex.RowSnapshot?.ToJson() ?? "<unavailable>");

            var message = friendlyContext is null
                ? ex.Message
                : string.Concat(ex.Message, " Context: ", friendlyContext, ".");

            return Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.metadata.rowMapping",
                message));
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

        var includeInactiveParam = command.CreateParameter();
        includeInactiveParam.ParameterName = "@IncludeInactive";
        includeInactiveParam.DbType = DbType.Boolean;
        includeInactiveParam.Value = request.IncludeInactiveModules;
        command.Parameters.Add(includeInactiveParam);

        var activeParam = command.CreateParameter();
        activeParam.ParameterName = "@OnlyActiveAttributes";
        activeParam.DbType = DbType.Boolean;
        activeParam.Value = request.OnlyActiveAttributes;
        command.Parameters.Add(activeParam);

        // Serialize entity filters to JSON
        var entityFilterParam = command.CreateParameter();
        entityFilterParam.ParameterName = "@EntityFilterJson";
        entityFilterParam.DbType = DbType.String;
        if (request.EntityFilters.Count > 0)
        {
            entityFilterParam.Value = JsonSerializer.Serialize(request.EntityFilters);
        }
        else
        {
            entityFilterParam.Value = DBNull.Value;
        }
        command.Parameters.Add(entityFilterParam);

        return command;
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

    private static string? BuildFriendlyContext(MetadataRowMappingException exception, MetadataAccumulator accumulator)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        if (accumulator is null)
        {
            return null;
        }

        var snapshot = exception.RowSnapshot;
        if (snapshot is null)
        {
            return null;
        }

        var columns = snapshot.Columns;
        if (columns.Count == 0)
        {
            return null;
        }

        var context = new List<string>();
        var resolvedAttributes = new HashSet<int>();
        var resolvedEntities = new HashSet<int>();
        var resolvedModules = new HashSet<int>();

        if (TryGetIntValue(columns, "AttrId", out var attributeId))
        {
            AppendAttribute(attributeId);
        }

        if (TryGetIntValue(columns, "EntityId", out var entityId))
        {
            AppendEntity(entityId);
        }

        if (TryGetIntValue(columns, "EspaceId", out var moduleId))
        {
            AppendModule(moduleId);
        }

        return context.Count == 0 ? null : string.Join(", ", context);

        void AppendAttribute(int attrId)
        {
            if (!resolvedAttributes.Add(attrId))
            {
                return;
            }

            var attribute = accumulator.Attributes.FirstOrDefault(attr => attr.AttrId == attrId);
            if (attribute is not null)
            {
                context.Add($"AttrId={attrId} ({attribute.AttrName})");
                AppendEntity(attribute.EntityId);
            }
            else
            {
                context.Add($"AttrId={attrId} (unresolved)");
            }
        }

        void AppendEntity(int entityIdValue)
        {
            if (!resolvedEntities.Add(entityIdValue))
            {
                return;
            }

            var entity = accumulator.Entities.FirstOrDefault(entity => entity.EntityId == entityIdValue);
            if (entity is not null)
            {
                context.Add($"EntityId={entityIdValue} ({entity.EntityName})");
                AppendModule(entity.EspaceId);
            }
            else
            {
                context.Add($"EntityId={entityIdValue} (unresolved)");
            }
        }

        void AppendModule(int moduleIdValue)
        {
            if (!resolvedModules.Add(moduleIdValue))
            {
                return;
            }

            var module = accumulator.Modules.FirstOrDefault(module => module.EspaceId == moduleIdValue);
            if (module is not null)
            {
                context.Add($"ModuleId={moduleIdValue} ({module.EspaceName})");
            }
            else
            {
                context.Add($"ModuleId={moduleIdValue} (unresolved)");
            }
        }
    }

    private static bool TryGetIntValue(IReadOnlyList<MetadataColumnSnapshot> columns, string columnName, out int value)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name must be provided.", nameof(columnName));
        }

        value = default;
        var column = columns.FirstOrDefault(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
        if (column is null || column.IsNull)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(column.ValuePreview))
        {
            return false;
        }

        return int.TryParse(column.ValuePreview, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    MetadataRowSnapshot? IMetadataSnapshotDiagnostics.LastFailureRowSnapshot => Volatile.Read(ref _lastFailureRowSnapshot);
}
