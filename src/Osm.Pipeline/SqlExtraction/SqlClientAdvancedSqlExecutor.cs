using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public SqlClientAdvancedSqlExecutor(
        IDbConnectionFactory connectionFactory,
        IAdvancedSqlScriptProvider scriptProvider,
        SqlExecutionOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _scriptProvider = scriptProvider ?? throw new ArgumentNullException(nameof(scriptProvider));
        _options = options ?? SqlExecutionOptions.Default;
    }

    public async Task<Result<string>> ExecuteAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var script = _scriptProvider.GetScript();
        if (string.IsNullOrWhiteSpace(script))
        {
            return Result<string>.Failure(ValidationError.Create("extraction.sql.script.missing", "Advanced SQL script was empty."));
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, script, request, _options);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null || result is DBNull)
            {
                return Result<string>.Failure(ValidationError.Create(
                    "extraction.sql.emptyJson",
                    "Advanced SQL execution returned no JSON payload."));
            }

            if (result is string text)
            {
                return string.IsNullOrWhiteSpace(text)
                    ? Result<string>.Failure(ValidationError.Create(
                        "extraction.sql.emptyJson",
                        "Advanced SQL execution returned an empty JSON payload."))
                    : Result<string>.Success(text);
            }

            return Result<string>.Success(Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty);
        }
        catch (DbException ex)
        {
            return Result<string>.Failure(ValidationError.Create(
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

        var activeParam = command.CreateParameter();
        activeParam.ParameterName = "@OnlyActiveAttributes";
        activeParam.DbType = DbType.Boolean;
        activeParam.Value = request.OnlyActiveAttributes;
        command.Parameters.Add(activeParam);

        return command;
    }
}
