using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Osm.Cli.Commands;

internal sealed class UatUsersCommandModule : ICommandModule
{
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly Option<string?> _modelOption = new("--model", "Path to the UAT model JSON file.");
    private readonly Option<bool> _fromLiveOption = new("--from-live", "Discover catalog from live metadata.");
    private readonly Option<string?> _uatConnectionOption = new("--uat-conn", "ADO.NET connection string for the UAT database (required).");
    private readonly Option<string?> _userSchemaOption = new("--user-schema", () => "dbo", "Schema that owns the user table.");
    private readonly Option<string?> _userTableOption = new("--user-table", () => "User", "User table name.");
    private readonly Option<string?> _userIdOption = new("--user-id-column", () => "Id", "Primary key column for the user table.");
    private readonly Option<string[]> _includeColumnsOption = new(
        name: "--include-columns",
        description: "Restrict catalog to specific column names.",
        parseArgument: static result => result.Tokens
            .Select(token => token.Value)
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray())
    {
        AllowMultipleArgumentsPerToken = true
    };
    private readonly Option<string?> _outputOption = new("--out", () => "./_artifacts", "Root directory for artifacts.");
    private readonly Option<string?> _userMapOption = new("--user-map", "Path to a CSV containing SourceUserId,TargetUserId mappings.");
    private readonly Option<string?> _userDdlOption = new("--user-ddl", "SQL or CSV export of dbo.User containing allowed user identifiers.");
    private readonly Option<string?> _userIdsOption = new("--user-ids", "Optional CSV or text file containing one allowed user identifier per row.");
    private readonly Option<string?> _snapshotOption = new("--snapshot", "Optional path to cache foreign key scans as a snapshot.");
    private readonly Option<string?> _userEntityIdOption = new("--user-entity-id", "Optional override identifier for the user entity (accepts bt*GUID*GUID, physical name, or numeric id).");

    public UatUsersCommandModule(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public Command BuildCommand()
    {
        var command = new Command("uat-users", "Emit user remapping artifacts for UAT.")
        {
            _modelOption,
            _fromLiveOption,
            _uatConnectionOption,
            _userSchemaOption,
            _userTableOption,
            _userIdOption,
            _includeColumnsOption,
            _outputOption,
            _userMapOption,
            _userDdlOption,
            _userIdsOption,
            _snapshotOption,
            _userEntityIdOption
        };

        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var userSchema = parseResult.GetValueForOption(_userSchemaOption) ?? "dbo";
        var tableValue = parseResult.GetValueForOption(_userTableOption) ?? "User";
        if (tableValue.Contains('.', StringComparison.Ordinal))
        {
            var parts = SplitTableIdentifier(tableValue);
            userSchema = parts.Schema;
            tableValue = parts.Table;
        }

        var allowedDdl = parseResult.GetValueForOption(_userDdlOption);
        var allowedIds = parseResult.GetValueForOption(_userIdsOption);
        if (string.IsNullOrWhiteSpace(allowedDdl) && string.IsNullOrWhiteSpace(allowedIds))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "Either --user-ddl or --user-ids must be supplied.");
            return;
        }

        var options = new UatUsersOptions(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_uatConnectionOption),
            parseResult.GetValueForOption(_fromLiveOption),
            userSchema,
            tableValue,
            parseResult.GetValueForOption(_userIdOption) ?? "Id",
            parseResult.GetValueForOption(_includeColumnsOption),
            parseResult.GetValueForOption(_outputOption) ?? "./_artifacts",
            parseResult.GetValueForOption(_userMapOption),
            allowedDdl,
            allowedIds,
            parseResult.GetValueForOption(_snapshotOption),
            parseResult.GetValueForOption(_userEntityIdOption));

        if (!options.FromLiveMetadata && string.IsNullOrWhiteSpace(options.ModelPath))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "--model is required when --from-live is not specified.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.UatConnectionString))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "--uat-conn is required.");
            return;
        }

        var cancellationToken = context.GetCancellationToken();
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var handler = services.GetRequiredService<IUatUsersCommand>();
        var exitCode = await handler.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        context.ExitCode = exitCode;
    }

    private static (string Schema, string Table) SplitTableIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return ("dbo", "User");
        }

        var trimmed = identifier.Trim();
        var separator = trimmed.IndexOf('.');
        if (separator < 0)
        {
            return ("dbo", trimmed);
        }

        var schema = separator == 0 ? "dbo" : trimmed[..separator];
        var table = separator >= trimmed.Length - 1 ? "User" : trimmed[(separator + 1)..];
        return (schema, table);
    }
}
