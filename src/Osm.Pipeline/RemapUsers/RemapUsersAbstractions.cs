using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers;

public interface IRemapUsersTelemetry
{
    IReadOnlyList<RemapUsersTelemetryEntry> Entries { get; }
    void StepStarted(string stepName);
    void StepCompleted(string stepName);
    void Info(string stepName, string message, IReadOnlyDictionary<string, string?>? metadata = null);
    void Warning(string stepName, string message, IReadOnlyDictionary<string, string?>? metadata = null);
    void Error(string stepName, string message, Exception exception, IReadOnlyDictionary<string, string?>? metadata = null);
}

public sealed record RemapUsersTelemetryEntry(
    DateTimeOffset Timestamp,
    string Step,
    string EventType,
    TimeSpan? Duration,
    string Message,
    IReadOnlyDictionary<string, string?>? Metadata,
    string? ExceptionType,
    string? ExceptionMessage);

public interface IRemapUsersArtifactWriter
{
    Task WriteJsonAsync(string relativePath, object payload, CancellationToken cancellationToken);
    Task WriteCsvAsync(string relativePath, IEnumerable<IReadOnlyList<string>> rows, CancellationToken cancellationToken);
    Task WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken);
}

public interface ISchemaGraph
{
    Task<IReadOnlyList<SchemaTable>> GetTablesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SchemaForeignKey>> GetForeignKeysAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SchemaTable>> GetTopologicallySortedTablesAsync(CancellationToken cancellationToken);
}

public sealed record SchemaTable(string Schema, string Name)
{
    public string QualifiedName => $"[{Schema}].[{Name}]";
}

public sealed record SchemaForeignKey(
    string Name,
    SchemaTable SourceTable,
    string SourceColumn,
    SchemaTable TargetTable,
    string TargetColumn);

public interface IBulkLoader
{
    Task LoadAsync(BulkLoadRequest request, CancellationToken cancellationToken);
}

public sealed record BulkLoadRequest(
    string SourceDirectory,
    string TableSchema,
    string TableName,
    string StagingSchema,
    int BatchSize,
    TimeSpan CommandTimeout,
    int Parallelism);

public interface ISqlRunner
{
    Task<int> ExecuteAsync(string commandText, IReadOnlyDictionary<string, object?> parameters, TimeSpan timeout, CancellationToken cancellationToken);
    Task<TResult?> ExecuteScalarAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, TimeSpan timeout, CancellationToken cancellationToken);
    Task<IReadOnlyList<TResult>> QueryAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, Func<IDataRecord, TResult> projector, TimeSpan timeout, CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(string transactionName, TimeSpan timeout, Func<ISqlTransactionalRunner, CancellationToken, Task> work, CancellationToken cancellationToken);
}

public interface ISqlTransactionalRunner
{
    Task<int> ExecuteAsync(string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken);
    Task<TResult?> ExecuteScalarAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken);
    Task<IReadOnlyList<TResult>> QueryAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, Func<IDataRecord, TResult> projector, CancellationToken cancellationToken);
}
