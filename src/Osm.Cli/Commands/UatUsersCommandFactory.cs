using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.Configuration;

namespace Osm.Cli.Commands;

internal sealed class UatUsersCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliGlobalOptions _globalOptions;
    private readonly ICliConfigurationService _configurationService;

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
    private readonly Option<string?> _uatInventoryOption = new(
        "--uat-user-inventory",
        "CSV export of the UAT ossys_User table (Id, Username, EMail, Name, External_Id, Is_Active, Creation_Date, Last_Login).");
    private readonly Option<string?> _qaInventoryOption = new("--qa-user-inventory", "CSV export of the QA dbo.User table (Id, Username, EMail, Name, External_Id, Is_Active, Creation_Date, Last_Login).");
    private readonly Option<string?> _snapshotOption = new("--snapshot", "Optional path to cache foreign key scans as a snapshot.");
    private readonly Option<string?> _userEntityIdOption = new("--user-entity-id", "Optional override identifier for the user entity (accepts btGUID*GUID, physical name, or numeric id).");

    public UatUsersCommandFactory(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        ICliConfigurationService configurationService)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    public Command Create()
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
            _uatInventoryOption,
            _qaInventoryOption,
            _snapshotOption,
            _userEntityIdOption
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var parseResult = context.ParseResult;
        var cancellationToken = context.GetCancellationToken();
        var configurationResult = await _configurationService
            .LoadAsync(parseResult.GetValueForOption(_globalOptions.ConfigPath), cancellationToken)
            .ConfigureAwait(false);
        if (configurationResult.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, configurationResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var configuration = configurationResult.Value.Configuration.UatUsers;

        var (modelPath, modelFromConfig) = ResolveStringOption(parseResult, _modelOption, configuration.ModelPath, null);

        var fromLiveSpecified = parseResult.HasOption(_fromLiveOption);
        var fromLive = fromLiveSpecified
            ? parseResult.GetValueForOption(_fromLiveOption)
            : configuration.FromLiveMetadata ?? false;
        var fromLiveFromConfig = !fromLiveSpecified && configuration.FromLiveMetadata.HasValue;

        var (connectionString, connectionFromConfig) = ResolveStringOption(parseResult, _uatConnectionOption, configuration.ConnectionString, null);

        var (userSchemaInput, schemaFromConfig) = ResolveStringOption(parseResult, _userSchemaOption, configuration.UserSchema, "dbo");
        var (userTableInput, tableFromConfig) = ResolveStringOption(parseResult, _userTableOption, configuration.UserTable, "User");
        var (userIdColumn, idFromConfig) = ResolveStringOption(parseResult, _userIdOption, configuration.UserIdColumn, "Id");

        var includeColumnsSpecified = parseResult.HasOption(_includeColumnsOption);
        var includeColumns = includeColumnsSpecified
            ? parseResult.GetValueForOption(_includeColumnsOption) ?? Array.Empty<string>()
            : (configuration.IncludeColumns.Count > 0 ? configuration.IncludeColumns.ToArray() : Array.Empty<string>());
        var includeColumnsFromConfig = !includeColumnsSpecified && configuration.IncludeColumns.Count > 0;

        var (outputDirectory, outputFromConfig) = ResolveStringOption(parseResult, _outputOption, configuration.OutputRoot, "./_artifacts");
        var (userMapPath, userMapFromConfig) = ResolveStringOption(parseResult, _userMapOption, configuration.UserMapPath, null);
        var (uatInventoryPath, uatInventoryFromConfig) = ResolveStringOption(parseResult, _uatInventoryOption, configuration.UatUserInventoryPath, null);
        var (qaInventoryPath, qaInventoryFromConfig) = ResolveStringOption(parseResult, _qaInventoryOption, configuration.QaUserInventoryPath, null);
        var (snapshotPath, snapshotFromConfig) = ResolveStringOption(parseResult, _snapshotOption, configuration.SnapshotPath, null);
        var (userEntityId, entityIdFromConfig) = ResolveStringOption(parseResult, _userEntityIdOption, configuration.UserEntityIdentifier, null);

        var userSchema = userSchemaInput ?? "dbo";
        var tableValue = userTableInput ?? "User";
        if (tableValue.Contains('.', StringComparison.Ordinal))
        {
            var parts = SplitTableIdentifier(tableValue);
            userSchema = parts.Schema;
            tableValue = parts.Table;
            if (!parseResult.HasOption(_userTableOption) && !string.IsNullOrWhiteSpace(configuration.UserTable))
            {
                schemaFromConfig = true;
                tableFromConfig = true;
            }
        }

        if (string.IsNullOrWhiteSpace(uatInventoryPath))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "--uat-user-inventory is required.");
            return;
        }

        if (!fromLive && string.IsNullOrWhiteSpace(modelPath))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "--model is required when --from-live is not specified.");
            return;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "--uat-conn is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(qaInventoryPath))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "--qa-user-inventory is required.");
            return;
        }

        var options = new UatUsersOptions(
            modelPath,
            connectionString,
            fromLive,
            userSchema,
            tableValue,
            userIdColumn ?? "Id",
            includeColumns,
            outputDirectory ?? "./_artifacts",
            userMapPath,
            uatInventoryPath,
            qaInventoryPath,
            snapshotPath,
            userEntityId,
            new UatUsersOptionOrigins(
                ModelPathFromConfiguration: modelFromConfig,
                ConnectionStringFromConfiguration: connectionFromConfig,
                FromLiveMetadataFromConfiguration: fromLiveFromConfig,
                UserSchemaFromConfiguration: schemaFromConfig,
                UserTableFromConfiguration: tableFromConfig,
                UserIdColumnFromConfiguration: idFromConfig,
                IncludeColumnsFromConfiguration: includeColumnsFromConfig,
                OutputDirectoryFromConfiguration: outputFromConfig,
                UserMapPathFromConfiguration: userMapFromConfig,
                UatUserInventoryPathFromConfiguration: uatInventoryFromConfig,
                QaUserInventoryPathFromConfiguration: qaInventoryFromConfig,
                SnapshotPathFromConfiguration: snapshotFromConfig,
                UserEntityIdentifierFromConfiguration: entityIdFromConfig));

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var handler = services.GetRequiredService<IUatUsersCommand>();
        var exitCode = await handler.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        context.ExitCode = exitCode;
    }

    private static (string? Value, bool FromConfiguration) ResolveStringOption(
        ParseResult parseResult,
        Option<string?> option,
        string? configurationValue,
        string? defaultValue)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        if (option is null)
        {
            throw new ArgumentNullException(nameof(option));
        }

        if (parseResult.HasOption(option))
        {
            return (parseResult.GetValueForOption(option), false);
        }

        if (!string.IsNullOrWhiteSpace(configurationValue))
        {
            return (configurationValue, true);
        }

        return (defaultValue, false);
    }

    private static (string Schema, string Table) SplitTableIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return ("dbo", "User");
        }

        var parts = ParseIdentifierParts(identifier.Trim());
        if (parts.Count == 0)
        {
            return ("dbo", "User");
        }

        if (parts.Count == 1)
        {
            var tableOnly = parts[0];
            var singleTable = string.IsNullOrWhiteSpace(tableOnly) ? "User" : tableOnly;
            return ("dbo", singleTable);
        }

        var schemaPart = parts[^2];
        var tablePart = parts[^1];
        var resolvedSchema = string.IsNullOrWhiteSpace(schemaPart) ? "dbo" : schemaPart;
        var resolvedTable = string.IsNullOrWhiteSpace(tablePart) ? "User" : tablePart;
        return (resolvedSchema, resolvedTable);
    }

    private static List<string> ParseIdentifierParts(string identifier)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return parts;
        }

        var span = identifier.AsSpan();
        var builder = new StringBuilder();
        var bracketDepth = 0;
        var inQuotes = false;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            switch (ch)
            {
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    continue;
                case '"':
                    if (bracketDepth == 0)
                    {
                        inQuotes = !inQuotes;
                        continue;
                    }
                    break;
                case '.':
                    if (bracketDepth == 0 && !inQuotes)
                    {
                        parts.Add(builder.ToString().Trim());
                        builder.Clear();
                        continue;
                    }
                    break;
            }

            builder.Append(ch);
        }

        parts.Add(builder.ToString().Trim());

        return parts;
    }
}
