using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.LoadHarness;

public sealed class LoadHarnessRunner : ILoadHarnessRunner
{
    private const string WaitStatsSql = @"
SELECT wait_type, wait_time_ms
FROM sys.dm_os_wait_stats
WHERE wait_type IN (
    'LCK_M_S', 'LCK_M_U', 'LCK_M_X', 'PAGEIOLATCH_SH', 'PAGEIOLATCH_EX', 'WRITELOG', 'CXPACKET', 'CXCONSUMER')";

    private const string LockSummarySql = @"
SELECT request_mode, resource_type, COUNT(*) AS lock_count
FROM sys.dm_tran_locks
WHERE resource_database_id = DB_ID()
GROUP BY request_mode, resource_type";

    private const string IndexFragmentationSql = @"
SELECT TOP (20)
    OBJECT_SCHEMA_NAME(s.object_id) AS schema_name,
    OBJECT_NAME(s.object_id) AS object_name,
    ISNULL(i.name, '') AS index_name,
    s.avg_fragmentation_in_percent,
    s.page_count
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'SAMPLED') AS s
JOIN sys.indexes AS i
    ON s.object_id = i.object_id
    AND s.index_id = i.index_id
WHERE s.page_count >= 1
ORDER BY s.avg_fragmentation_in_percent DESC";

    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LoadHarnessRunner> _logger;

    public LoadHarnessRunner(IFileSystem fileSystem, TimeProvider timeProvider, ILogger<LoadHarnessRunner>? logger = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<LoadHarnessRunner>.Instance;
    }

    public async Task<LoadHarnessReport> RunAsync(LoadHarnessOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var scripts = BuildScriptQueue(options);
        if (scripts.Count == 0)
        {
            return LoadHarnessReport.Empty();
        }

        var runStart = _timeProvider.GetUtcNow();
        var runTimestamp = _timeProvider.GetTimestamp();
        var results = ImmutableArray.CreateBuilder<ScriptReplayResult>(scripts.Count);

        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in scripts)
        {
            var result = await ExecuteScriptAsync(
                    connection,
                    item.Category,
                    item.Path,
                    options.CommandTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            results.Add(result);
        }

        var totalDuration = _timeProvider.GetElapsedTime(runTimestamp);
        return new LoadHarnessReport(runStart, _timeProvider.GetUtcNow(), results.ToImmutable(), totalDuration);
    }

    private async Task<ScriptReplayResult> ExecuteScriptAsync(
        SqlConnection connection,
        ScriptReplayCategory category,
        string path,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A script path is required.", nameof(path));
        }

        var warnings = ImmutableArray.CreateBuilder<string>();

        if (!_fileSystem.File.Exists(path))
        {
            warnings.Add($"Script '{path}' does not exist and was skipped.");
            return new ScriptReplayResult(
                category,
                path,
                BatchCount: 0,
                Duration: TimeSpan.Zero,
                BatchTimings: ImmutableArray<BatchTiming>.Empty,
                WaitStats: ImmutableArray<WaitStatDelta>.Empty,
                LockSummary: ImmutableArray<LockSummaryEntry>.Empty,
                IndexFragmentation: ImmutableArray<IndexFragmentationEntry>.Empty,
                Warnings: warnings.ToImmutable());
        }

        _logger.LogInformation("Running load harness script {Category} from {Path}.", category, path);

        var waitStatsBefore = await QueryWaitStatsAsync(connection, warnings, cancellationToken).ConfigureAwait(false);
        var batches = await LoadBatchesAsync(path, cancellationToken).ConfigureAwait(false);

        var batchTimings = ImmutableArray.CreateBuilder<BatchTiming>(batches.Count);
        var executedBatches = 0;
        var scriptTimestamp = _timeProvider.GetTimestamp();

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            executedBatches++;
            var stopwatch = Stopwatch.StartNew();
            await ExecuteBatchAsync(connection, batch, commandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            batchTimings.Add(new BatchTiming(executedBatches, stopwatch.Elapsed));
        }

        var scriptDuration = _timeProvider.GetElapsedTime(scriptTimestamp);
        var waitStatsAfter = await QueryWaitStatsAsync(connection, warnings, cancellationToken).ConfigureAwait(false);
        var waitStatsDelta = CalculateWaitStatsDelta(waitStatsBefore, waitStatsAfter);

        var lockSummary = await QueryLockSummaryAsync(connection, warnings, cancellationToken).ConfigureAwait(false);
        var indexFragmentation = await QueryIndexFragmentationAsync(connection, warnings, cancellationToken).ConfigureAwait(false);

        return new ScriptReplayResult(
            category,
            path,
            executedBatches,
            scriptDuration,
            batchTimings.ToImmutable(),
            waitStatsDelta,
            lockSummary,
            indexFragmentation,
            warnings.ToImmutable());
    }

    private static IReadOnlyList<(ScriptReplayCategory Category, string Path)> BuildScriptQueue(LoadHarnessOptions options)
    {
        var list = new List<(ScriptReplayCategory, string)>();

        if (!string.IsNullOrWhiteSpace(options.SafeScriptPath))
        {
            list.Add((ScriptReplayCategory.Safe, options.SafeScriptPath));
        }

        if (!string.IsNullOrWhiteSpace(options.RemediationScriptPath))
        {
            list.Add((ScriptReplayCategory.Remediation, options.RemediationScriptPath));
        }

        if (!options.StaticSeedScriptPaths.IsDefaultOrEmpty)
        {
            foreach (var seed in options.StaticSeedScriptPaths)
            {
                if (!string.IsNullOrWhiteSpace(seed))
                {
                    list.Add((ScriptReplayCategory.StaticSeed, seed));
                }
            }
        }

        if (!options.DynamicInsertScriptPaths.IsDefaultOrEmpty)
        {
            foreach (var dynamic in options.DynamicInsertScriptPaths)
            {
                if (!string.IsNullOrWhiteSpace(dynamic))
                {
                    list.Add((ScriptReplayCategory.Dynamic, dynamic));
                }
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<string>> LoadBatchesAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = _fileSystem.File.OpenRead(path);
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

    private static async Task ExecuteBatchAsync(
        SqlConnection connection,
        string batch,
        int? commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = batch;
        command.CommandType = CommandType.Text;

        if (commandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = commandTimeoutSeconds.Value;
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, long>> QueryWaitStatsAsync(
        SqlConnection connection,
        ImmutableArray<string>.Builder warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = WaitStatsSql;
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var waitType = reader.GetString(0);
                var waitTime = reader.GetInt64(1);
                result[waitType] = waitTime;
            }

            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Failed to query wait stats for load harness.");
            warnings.Add($"Failed to capture wait stats: {ex.Message}");
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static ImmutableArray<WaitStatDelta> CalculateWaitStatsDelta(
        Dictionary<string, long> before,
        Dictionary<string, long> after)
    {
        if (before.Count == 0 && after.Count == 0)
        {
            return ImmutableArray<WaitStatDelta>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<WaitStatDelta>();
        foreach (var pair in after)
        {
            before.TryGetValue(pair.Key, out var previous);
            var delta = pair.Value - previous;
            if (delta <= 0)
            {
                continue;
            }

            builder.Add(new WaitStatDelta(pair.Key, delta));
        }

        return builder.ToImmutable();
    }

    private async Task<ImmutableArray<LockSummaryEntry>> QueryLockSummaryAsync(
        SqlConnection connection,
        ImmutableArray<string>.Builder warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = LockSummarySql;
            var builder = ImmutableArray.CreateBuilder<LockSummaryEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var requestMode = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var resourceType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var count = reader.GetInt32(2);
                builder.Add(new LockSummaryEntry(requestMode, resourceType, count));
            }

            return builder.ToImmutable();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Failed to query lock summary for load harness.");
            warnings.Add($"Failed to capture lock summary: {ex.Message}");
            return ImmutableArray<LockSummaryEntry>.Empty;
        }
    }

    private async Task<ImmutableArray<IndexFragmentationEntry>> QueryIndexFragmentationAsync(
        SqlConnection connection,
        ImmutableArray<string>.Builder warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = IndexFragmentationSql;
            var builder = ImmutableArray.CreateBuilder<IndexFragmentationEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var schema = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var obj = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var index = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var fragmentation = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                var pages = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                builder.Add(new IndexFragmentationEntry(schema, obj, index, fragmentation, pages));
            }

            return builder.ToImmutable();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Failed to query index fragmentation for load harness.");
            warnings.Add($"Failed to capture index fragmentation: {ex.Message}");
            return ImmutableArray<IndexFragmentationEntry>.Empty;
        }
    }
}
