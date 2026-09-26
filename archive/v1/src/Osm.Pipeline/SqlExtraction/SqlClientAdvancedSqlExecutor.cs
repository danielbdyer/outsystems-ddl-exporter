using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

public sealed record SqlExecutionOptions(int? CommandTimeoutSeconds, SqlSamplingOptions Sampling)
{
    public static SqlExecutionOptions Default { get; } = new(null, SqlSamplingOptions.Default);
}

public sealed class SqlClientAdvancedSqlExecutor : IAdvancedSqlExecutor
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAdvancedSqlScriptProvider _scriptProvider;
    private readonly SqlExecutionOptions _options;
    private readonly ILogger<SqlClientAdvancedSqlExecutor> _logger;

    public SqlClientAdvancedSqlExecutor(
        IDbConnectionFactory connectionFactory,
        IAdvancedSqlScriptProvider scriptProvider,
        SqlExecutionOptions? options = null,
        ILogger<SqlClientAdvancedSqlExecutor>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _scriptProvider = scriptProvider ?? throw new ArgumentNullException(nameof(scriptProvider));
        _options = options ?? SqlExecutionOptions.Default;
        _logger = logger ?? NullLogger<SqlClientAdvancedSqlExecutor>.Instance;
    }

    public async Task<Result<long>> ExecuteAsync(
        AdvancedSqlRequest request,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        }

        if (!destination.CanSeek)
        {
            throw new ArgumentException("Destination stream must support seeking.", nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Executing advanced SQL script via SQL client (timeoutSeconds: {TimeoutSeconds}, moduleCount: {ModuleCount}, includeSystem: {IncludeSystem}, includeInactive: {IncludeInactive}, onlyActive: {OnlyActive}).",
            _options.CommandTimeoutSeconds,
            request.ModuleNames.Length,
            request.IncludeSystemModules,
            request.IncludeInactiveModules,
            request.OnlyActiveAttributes);

        if (request.ModuleNames.Length > 0)
        {
            _logger.LogDebug(
                "Advanced SQL request modules: {Modules}.",
                string.Join(",", request.ModuleNames.Select(static module => module.Value)));
        }

        var script = _scriptProvider.GetScript();
        if (string.IsNullOrWhiteSpace(script))
        {
            _logger.LogError("Advanced SQL script provider returned an empty script.");
            return Result<long>.Failure(ValidationError.Create("extraction.sql.script.missing", "Advanced SQL script was empty."));
        }

        _logger.LogDebug("Advanced SQL script length: {ScriptLength} characters.", script.Length);

        var stopwatch = Stopwatch.StartNew();

        destination.SetLength(0);
        destination.Position = 0;

        try
        {
            _logger.LogDebug("Opening SQL connection.");
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("SQL connection opened successfully.");

            await using var command = CreateCommand(connection, script, request, _options);
            _logger.LogDebug("Executing advanced SQL command.");

            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult, cancellationToken)
                .ConfigureAwait(false);

            var chunkCount = 0;
            var hasContent = false;
            var hasNonWhitespace = false;

            using var writer = new StreamWriter(destination, Encoding.UTF8, bufferSize: 81920, leaveOpen: true);
            var buffer = new char[8192];

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                chunkCount++;

                using var textReader = reader.GetTextReader(0);
                int read;
                while ((read = await textReader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    await writer.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    hasContent = true;

                    if (!hasNonWhitespace)
                    {
                        for (var i = 0; i < read; i++)
                        {
                            if (!char.IsWhiteSpace(buffer[i]))
                            {
                                hasNonWhitespace = true;
                                break;
                            }
                        }
                    }
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInformation(
                "Advanced SQL command completed in {DurationMs} ms with {ChunkCount} chunk(s).",
                stopwatch.Elapsed.TotalMilliseconds,
                chunkCount);

            if (!hasContent)
            {
                _logger.LogError("Advanced SQL command returned no results.");
                destination.SetLength(0);
                destination.Position = 0;
                return Result<long>.Failure(ValidationError.Create(
                    "extraction.sql.emptyJson",
                    "Advanced SQL execution returned no JSON payload."));
            }

            var bytesWritten = destination.Position;
            destination.Position = 0;

            if (bytesWritten == 0 || !hasNonWhitespace)
            {
                _logger.LogError("Advanced SQL command returned an empty JSON payload after concatenation.");
                destination.SetLength(0);
                destination.Position = 0;
                return Result<long>.Failure(ValidationError.Create(
                    "extraction.sql.emptyJson",
                    "Advanced SQL execution returned an empty JSON payload."));
            }

            return Result<long>.Success(bytesWritten);
        }
        catch (DbException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Advanced SQL execution failed after {DurationMs} ms.", stopwatch.Elapsed.TotalMilliseconds);
            destination.SetLength(0);
            destination.Position = 0;
            return Result<long>.Failure(ValidationError.Create(
                "extraction.sql.executionFailed",
                $"Advanced SQL execution failed: {ex.Message}"));
        }
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string script,
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

        var modules = request.ModuleNames.Length > 0
            ? string.Join(',', request.ModuleNames.Select(static module => module.Value))
            : string.Empty;

        var moduleParam = command.CreateParameter();
        moduleParam.ParameterName = "@ModuleNamesCsv";
        moduleParam.DbType = DbType.String;
        moduleParam.Value = modules;
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

        return command;
    }
}
