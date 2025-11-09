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
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Orchestration;

public sealed record SchemaDataApplyRequest(
    string ConnectionString,
    SqlConnectionOptions ConnectionOptions,
    int? CommandTimeoutSeconds,
    ImmutableArray<string> ScriptPaths,
    ImmutableArray<string> SeedScriptPaths);

public sealed record SchemaDataApplyOutcome(
    ImmutableArray<string> AppliedScripts,
    ImmutableArray<string> AppliedSeedScripts,
    int ExecutedBatchCount,
    TimeSpan Duration,
    long MaxBatchSizeBytes,
    bool StreamingEnabled);

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

            foreach (var path in scriptPaths)
            {
                var result = await ExecuteBatchesAsync(
                        connection,
                        LoadBatchesAsync(path, cancellationToken),
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                executedBatches += result.ExecutedCount;
                if (result.MaxBatchBytes > maxBatchBytes)
                {
                    maxBatchBytes = result.MaxBatchBytes;
                }
            }

            foreach (var path in seedPaths)
            {
                var result = await ExecuteBatchesAsync(
                        connection,
                        LoadBatchesAsync(path, cancellationToken),
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                executedBatches += result.ExecutedCount;
                if (result.MaxBatchBytes > maxBatchBytes)
                {
                    maxBatchBytes = result.MaxBatchBytes;
                }
            }
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

        var duration = _timeProvider.GetElapsedTime(startTimestamp);
        return new SchemaDataApplyOutcome(
            scriptPaths,
            seedPaths,
            executedBatches,
            duration,
            maxBatchBytes,
            StreamingEnabled: true);
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

            if (commandTimeoutSeconds.HasValue)
            {
                command.CommandTimeout = commandTimeoutSeconds.Value;
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            executed++;
        }

        return new BatchExecutionMetrics(executed, maxBatchBytes);
    }

    private readonly record struct BatchExecutionMetrics(int ExecutedCount, long MaxBatchBytes);
}
