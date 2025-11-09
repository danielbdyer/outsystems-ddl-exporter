using System;
using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Orchestration;

public sealed record SchemaDataApplyRequest(
    string ConnectionString,
    SqlConnectionOptions ConnectionOptions,
    int? CommandTimeoutSeconds,
    ImmutableArray<string> ScriptPaths,
    ImmutableArray<string> SeedScriptPaths,
    StaticSeedSynchronizationMode StaticSeedSynchronizationMode = StaticSeedSynchronizationMode.NonDestructive);

public sealed record StaticSeedValidationSummary(bool Attempted, bool Failed, string? FailureReason)
{
    public static StaticSeedValidationSummary NotAttempted { get; } = new(false, false, null);

    public static StaticSeedValidationSummary Success { get; } = new(true, false, null);

    public static StaticSeedValidationSummary Failure(string? reason)
        => new(true, true, string.IsNullOrWhiteSpace(reason) ? null : reason);
}

public sealed record SchemaDataApplyOutcome(
    ImmutableArray<string> AppliedScripts,
    ImmutableArray<string> AppliedSeedScripts,
    int ExecutedBatchCount,
    TimeSpan Duration,
    long MaxBatchSizeBytes,
    bool StreamingEnabled,
    StaticSeedValidationSummary StaticSeedValidation);

public interface ISchemaDataApplier
{
    Task<Result<SchemaDataApplyOutcome>> ApplyAsync(
        SchemaDataApplyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SchemaDataApplier : ISchemaDataApplier
{
    private readonly Func<string, SqlConnectionOptions, IDbConnectionFactory> _connectionFactoryAccessor;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchemaDataApplier> _logger;

    public SchemaDataApplier(
        Func<string, SqlConnectionOptions, IDbConnectionFactory> connectionFactoryAccessor,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<SchemaDataApplier> logger)
    {
        _connectionFactoryAccessor = connectionFactoryAccessor ?? throw new ArgumentNullException(nameof(connectionFactoryAccessor));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SchemaDataApplyOutcome>> ApplyAsync(
        SchemaDataApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return ValidationError.Create(
                "pipeline.fullExport.apply.connectionString.missing",
                "A SQL connection string must be provided before schema scripts can be applied.");
        }

        var scriptPaths = NormalizePaths(request.ScriptPaths);
        var seedPaths = NormalizePaths(request.SeedScriptPaths);

        var startTimestamp = _timeProvider.GetTimestamp();
        var executedBatches = 0;
        var maxBatchBytes = 0L;

        try
        {
            await using var connection = await CreateConnectionAsync(
                    request.ConnectionString,
                    request.ConnectionOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            var validateStaticSeeds = request.StaticSeedSynchronizationMode
                is StaticSeedSynchronizationMode.ValidateThenApply
                or StaticSeedSynchronizationMode.Authoritative;

            var validationSummary = StaticSeedValidationSummary.NotAttempted;

            foreach (var path in scriptPaths)
            {
                var result = await ExecuteBatchesAsync(
                        connection,
                        LoadBatchesAsync(path, cancellationToken),
                        request.CommandTimeoutSeconds,
                        transaction: null,
                        cancellationToken)
                    .ConfigureAwait(false);

                executedBatches += result.ExecutedCount;
                if (result.MaxBatchBytes > maxBatchBytes)
                {
                    maxBatchBytes = result.MaxBatchBytes;
                }
            }

            if (validateStaticSeeds && !seedPaths.IsDefaultOrEmpty)
            {
                validationSummary = await ValidateStaticSeedsAsync(
                        connection,
                        seedPaths,
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (validationSummary.Failed)
                {
                    var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                    return new SchemaDataApplyOutcome(
                        scriptPaths,
                        ImmutableArray<string>.Empty,
                        executedBatches,
                        elapsed,
                        maxBatchBytes,
                        StreamingEnabled: true,
                        validationSummary);
                }
            }

            foreach (var path in seedPaths)
            {
                var result = await ExecuteBatchesAsync(
                        connection,
                        LoadBatchesAsync(path, cancellationToken),
                        request.CommandTimeoutSeconds,
                        transaction: null,
                        cancellationToken)
                    .ConfigureAwait(false);

                executedBatches += result.ExecutedCount;
                if (result.MaxBatchBytes > maxBatchBytes)
                {
                    maxBatchBytes = result.MaxBatchBytes;
                }
            }

            var duration = _timeProvider.GetElapsedTime(startTimestamp);
            return new SchemaDataApplyOutcome(
                scriptPaths,
                seedPaths,
                executedBatches,
                duration,
                maxBatchBytes,
                StreamingEnabled: true,
                validationSummary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqlException or InvalidOperationException)
        {
            _logger.LogError(ex, "Schema apply failed while executing generated scripts.");
            return ValidationError.Create(
                "pipeline.fullExport.apply.failed",
                $"Schema apply failed: {ex.Message}");
        }
    }

    private ImmutableArray<string> NormalizePaths(ImmutableArray<string> paths)
    {
        if (paths.IsDefaultOrEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(paths.Length);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            builder.Add(_fileSystem.Path.GetFullPath(path));
        }

        return builder.ToImmutable();
    }

    private async Task<DbConnection> CreateConnectionAsync(
        string connectionString,
        SqlConnectionOptions options,
        CancellationToken cancellationToken)
    {
        var factory = _connectionFactoryAccessor(connectionString, options);
        return await factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<string> LoadBatchesAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_fileSystem.File.Exists(path))
        {
            throw new FileNotFoundException($"SQL script '{path}' could not be found.", path);
        }

        await using var stream = _fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var builder = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.AsSpan().Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var batch = ConsumeBatch(builder);
                if (batch is not null)
                {
                    yield return batch;
                }
            }
            else
            {
                builder.AppendLine(line);
            }
        }

        var finalBatch = ConsumeBatch(builder);
        if (finalBatch is not null)
        {
            yield return finalBatch;
        }
    }

    private static string? ConsumeBatch(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return null;
        }

        var batch = builder.ToString();
        builder.Clear();

        return string.IsNullOrWhiteSpace(batch) ? null : batch;
    }

    private static async Task<BatchExecutionMetrics> ExecuteBatchesAsync(
        DbConnection connection,
        IAsyncEnumerable<string> batches,
        int? commandTimeoutSeconds,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var executed = 0;
        var maxBatchBytes = 0L;

        await foreach (var batch in batches.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            var batchBytes = Encoding.UTF8.GetByteCount(batch);
            if (batchBytes > maxBatchBytes)
            {
                maxBatchBytes = batchBytes;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.Transaction = transaction;

            if (commandTimeoutSeconds.HasValue)
            {
                command.CommandTimeout = commandTimeoutSeconds.Value;
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            executed++;
        }

        return new BatchExecutionMetrics(executed, maxBatchBytes);
    }

    private async Task<StaticSeedValidationSummary> ValidateStaticSeedsAsync(
        DbConnection connection,
        ImmutableArray<string> seedPaths,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            foreach (var path in seedPaths)
            {
                await ExecuteBatchesAsync(
                        connection,
                        LoadBatchesAsync(path, cancellationToken),
                        commandTimeoutSeconds,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StaticSeedValidationSummary.Success;
        }
        catch (SqlException ex) when (IsStaticSeedDrift(ex))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return StaticSeedValidationSummary.Failure(ex.Message);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsStaticSeedDrift(SqlException exception)
    {
        foreach (SqlError error in exception.Errors)
        {
            if (error.Number == 50000
                || error.Message.Contains("Static entity seed data drift detected", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct BatchExecutionMetrics(int ExecutedCount, long MaxBatchBytes);
}
