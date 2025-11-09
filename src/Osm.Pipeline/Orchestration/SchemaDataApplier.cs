using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.IO.Abstractions;
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
    TimeSpan Duration);

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

        try
        {
            await using var connection = await CreateConnectionAsync(
                    request.ConnectionString,
                    request.ConnectionOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var path in scriptPaths)
            {
                var batches = await LoadBatchesAsync(path, cancellationToken).ConfigureAwait(false);
                executedBatches += await ExecuteBatchesAsync(
                        connection,
                        batches,
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var path in seedPaths)
            {
                var batches = await LoadBatchesAsync(path, cancellationToken).ConfigureAwait(false);
                executedBatches += await ExecuteBatchesAsync(
                        connection,
                        batches,
                        request.CommandTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
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
        return new SchemaDataApplyOutcome(scriptPaths, seedPaths, executedBatches, duration);
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

    private async Task<IReadOnlyList<string>> LoadBatchesAsync(string path, CancellationToken cancellationToken)
    {
        if (!_fileSystem.File.Exists(path))
        {
            throw new FileNotFoundException($"SQL script '{path}' could not be found.", path);
        }

        await using var stream = _fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return SplitBatches(content);
    }

    private static IReadOnlyList<string> SplitBatches(string script)
    {
        var batches = new List<string>();
        if (string.IsNullOrWhiteSpace(script))
        {
            return batches;
        }

        using var reader = new StringReader(script);
        var builder = new StringBuilder();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                AppendBatch(builder, batches);
            }
            else
            {
                builder.AppendLine(line);
            }
        }

        AppendBatch(builder, batches);
        return batches;
    }

    private static void AppendBatch(StringBuilder builder, ICollection<string> batches)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var batch = builder.ToString().Trim();
        builder.Clear();

        if (!string.IsNullOrWhiteSpace(batch))
        {
            batches.Add(batch);
        }
    }

    private static async Task<int> ExecuteBatchesAsync(
        DbConnection connection,
        IReadOnlyList<string> batches,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (batches.Count == 0)
        {
            return 0;
        }

        var executed = 0;
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
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

        return executed;
    }
}
