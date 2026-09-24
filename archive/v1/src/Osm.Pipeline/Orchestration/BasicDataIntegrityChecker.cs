using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Model.Emission;
using Osm.Emission.Formatting;

namespace Osm.Pipeline.Orchestration;

public sealed record BasicIntegrityCheckRequest(
    string SourceConnectionString,
    string TargetConnectionString,
    OsmModel Model,
    NamingOverrideOptions? NamingOverrides = null,
    int? CommandTimeoutSeconds = null);

public sealed record BasicIntegrityCheckResult(
    bool Passed,
    ImmutableArray<IntegrityWarning> Warnings,
    int TablesChecked,
    int RowCountMatches,
    int NullCountMatches);

public sealed record IntegrityWarning(
    string TableName,
    string ColumnName,
    string WarningType,
    long ExpectedValue,
    long ActualValue,
    string Message);

public interface IDataIntegrityQueryExecutor
{
    Task<Result<long>> GetRowCountAsync(
        string connectionString,
        string schema,
        string table,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken);

    Task<Result<long>> GetNullCountAsync(
        string connectionString,
        string schema,
        string table,
        string column,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken);
}

public sealed class SqlDataIntegrityQueryExecutor : IDataIntegrityQueryExecutor
{
    public async Task<Result<long>> GetRowCountAsync(
        string connectionString,
        string schema,
        string table,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var qualifiedTable = SqlIdentifierFormatter.Qualify(schema, table);
        var commandText = $"SELECT COUNT_BIG(*) FROM {qualifiedTable}";

        return await ExecuteScalarAsync(connectionString, commandText, commandTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<long>> GetNullCountAsync(
        string connectionString,
        string schema,
        string table,
        string column,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var qualifiedTable = SqlIdentifierFormatter.Qualify(schema, table);
        var quotedColumn = SqlIdentifierFormatter.Quote(column);
        var commandText = $"SELECT COUNT_BIG(*) FROM {qualifiedTable} WHERE {quotedColumn} IS NULL";

        return await ExecuteScalarAsync(connectionString, commandText, commandTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Result<long>> ExecuteScalarAsync(
        string connectionString,
        string commandText,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandType = CommandType.Text;
        if (commandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = commandTimeoutSeconds.Value;
        }

        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Result<long>.Success(result is long value ? value : 0L);
        }
        catch (Exception ex)
        {
            return ValidationError.Create(
                "dataIntegrity.queryFailed",
                $"Failed to execute data integrity query: {ex.Message}");
        }
    }
}

public sealed class BasicDataIntegrityChecker
{
    private readonly IDataIntegrityQueryExecutor _queryExecutor;

    public BasicDataIntegrityChecker(IDataIntegrityQueryExecutor queryExecutor)
    {
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
    }

    public async Task<BasicIntegrityCheckResult> CheckAsync(
        BasicIntegrityCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
        {
            throw new ArgumentException("Source connection string must be provided.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TargetConnectionString))
        {
            throw new ArgumentException("Target connection string must be provided.", nameof(request));
        }

        if (request.Model is null)
        {
            throw new ArgumentNullException(nameof(request.Model));
        }

        var namingOverrides = request.NamingOverrides ?? NamingOverrideOptions.Empty;
        var warnings = ImmutableArray.CreateBuilder<IntegrityWarning>();
        var rowCountMatches = 0;
        var nullCountMatches = 0;
        var tablesChecked = 0;

        foreach (var module in request.Model.Modules)
        {
            if (module is null || !module.IsActive)
            {
                continue;
            }

            foreach (var entity in module.Entities)
            {
                if (entity is null || !entity.IsActive || entity.IsExternal)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                tablesChecked++;

                var snapshot = EntityEmissionSnapshot.Create(module.Name.Value, entity);
                var sourceTableName = entity.PhysicalName.Value;
                var targetTableName = namingOverrides.GetEffectiveTableName(
                    entity.Schema.Value,
                    entity.PhysicalName.Value,
                    entity.LogicalName.Value,
                    module.Name.Value);

                var sourceRowCount = await _queryExecutor
                    .GetRowCountAsync(
                        request.SourceConnectionString,
                        entity.Schema.Value,
                        sourceTableName,
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                var targetRowCount = await _queryExecutor
                    .GetRowCountAsync(
                        request.TargetConnectionString,
                        entity.Schema.Value,
                        targetTableName,
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (sourceRowCount.IsFailure)
                {
                    warnings.Add(CreateQueryWarning(entity, "RowCountQueryFailed", sourceRowCount.Errors));
                    continue;
                }

                if (targetRowCount.IsFailure)
                {
                    warnings.Add(CreateQueryWarning(entity, "RowCountQueryFailed", targetRowCount.Errors));
                    continue;
                }

                if (sourceRowCount.Value != targetRowCount.Value)
                {
                    warnings.Add(new IntegrityWarning(
                        TableName: entity.PhysicalName.Value,
                        ColumnName: "<row count>",
                        WarningType: "RowCountMismatch",
                        ExpectedValue: sourceRowCount.Value,
                        ActualValue: targetRowCount.Value,
                        Message: $"Row count mismatch for {entity.Schema.Value}.{sourceTableName}: expected {sourceRowCount.Value}, got {targetRowCount.Value} (target {targetTableName})."));
                }
                else
                {
                    rowCountMatches++;
                }

                foreach (var attribute in snapshot.EmittableAttributes)
                {
                    if (attribute is null || attribute.IsMandatory || attribute.OnDisk.IsComputed == true)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var sourceNullCount = await _queryExecutor
                        .GetNullCountAsync(
                            request.SourceConnectionString,
                            entity.Schema.Value,
                            sourceTableName,
                            attribute.ColumnName.Value,
                            request.CommandTimeoutSeconds,
                            cancellationToken)
                        .ConfigureAwait(false);

                    var targetColumnName = ResolveTargetColumnName(attribute);
                    var targetNullCount = await _queryExecutor
                        .GetNullCountAsync(
                            request.TargetConnectionString,
                            entity.Schema.Value,
                            targetTableName,
                            targetColumnName,
                            request.CommandTimeoutSeconds,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (sourceNullCount.IsFailure)
                    {
                        warnings.Add(CreateQueryWarning(entity, attribute, "NullCountQueryFailed", sourceNullCount.Errors));
                        continue;
                    }

                    if (targetNullCount.IsFailure)
                    {
                        warnings.Add(CreateQueryWarning(entity, attribute, "NullCountQueryFailed", targetNullCount.Errors));
                        continue;
                    }

                    if (sourceNullCount.Value != targetNullCount.Value)
                    {
                        warnings.Add(new IntegrityWarning(
                            TableName: entity.PhysicalName.Value,
                            ColumnName: attribute.ColumnName.Value,
                            WarningType: "NullCountMismatch",
                            ExpectedValue: sourceNullCount.Value,
                            ActualValue: targetNullCount.Value,
                            Message: $"NULL count mismatch for {entity.Schema.Value}.{sourceTableName}.{attribute.ColumnName.Value}: expected {sourceNullCount.Value}, got {targetNullCount.Value} (target column {targetColumnName})."));
                    }
                    else
                    {
                        nullCountMatches++;
                    }
                }
            }
        }

        var warningArray = warnings.ToImmutable();
        return new BasicIntegrityCheckResult(
            Passed: warningArray.Length == 0,
            Warnings: warningArray,
            TablesChecked: tablesChecked,
            RowCountMatches: rowCountMatches,
            NullCountMatches: nullCountMatches);
    }

    private static IntegrityWarning CreateQueryWarning(EntityModel entity, string warningType, ImmutableArray<ValidationError> errors)
    {
        var message = string.Join("; ", errors.Select(static error => error.Message));
        return new IntegrityWarning(
            TableName: entity.PhysicalName.Value,
            ColumnName: "<query>",
            WarningType: warningType,
            ExpectedValue: 0,
            ActualValue: 0,
            Message: string.IsNullOrWhiteSpace(message)
                ? $"Data integrity query failed for {entity.Schema.Value}.{entity.PhysicalName.Value}."
                : message);
    }

    private static IntegrityWarning CreateQueryWarning(
        EntityModel entity,
        AttributeModel attribute,
        string warningType,
        ImmutableArray<ValidationError> errors)
    {
        var message = string.Join("; ", errors.Select(static error => error.Message));
        return new IntegrityWarning(
            TableName: entity.PhysicalName.Value,
            ColumnName: attribute.ColumnName.Value,
            WarningType: warningType,
            ExpectedValue: 0,
            ActualValue: 0,
            Message: string.IsNullOrWhiteSpace(message)
                ? $"Data integrity query failed for {entity.Schema.Value}.{entity.PhysicalName.Value}.{attribute.ColumnName.Value}."
                : message);
    }

    private static string ResolveTargetColumnName(AttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.LogicalName.Value))
        {
            return attribute.LogicalName.Value;
        }

        return attribute.ColumnName.Value;
    }
}
