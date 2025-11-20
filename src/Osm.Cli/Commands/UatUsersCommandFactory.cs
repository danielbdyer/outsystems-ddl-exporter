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
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.UatUsers;

namespace Osm.Cli.Commands;

internal sealed class UatUsersCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliGlobalOptions _globalOptions;
    private readonly ICliConfigurationService _configurationService;

    private readonly SqlOptionBinder _sqlBinder = new();
    private readonly Option<string?> _modelOption = new("--model", "Path to the UAT model JSON file.");
    private readonly Option<bool> _fromLiveOption = new("--from-live", "Discover catalog from live metadata.");
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
    private readonly Option<string?> _matchingStrategyOption = new("--match-strategy", "Matching strategy: case-insensitive-email, exact-attribute, or regex.");
    private readonly Option<string?> _matchingAttributeOption = new("--match-attribute", "Attribute to evaluate when using exact-attribute or regex strategies (Username, Email, External_Id, etc.).");
    private readonly Option<string?> _matchingRegexOption = new("--match-regex", "Regex pattern used when --match-strategy=regex (captures 'target' group or first capture).");
    private readonly Option<string?> _fallbackModeOption = new("--match-fallback-mode", "Fallback assignment mode: ignore, single, or round-robin.");
    private readonly Option<string[]> _fallbackTargetOption = new(
        name: "--match-fallback-target",
        description: "Approved UAT target user IDs to use when fallback mode assigns missing matches (accepts comma-delimited values).",
        parseArgument: static result => result.Tokens
            .Select(token => token.Value)
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray())
    {
        AllowMultipleArgumentsPerToken = true
    };
    private readonly Option<bool> _idempotentEmissionOption = new("--idempotent-emission", () => false, "Only rewrite artifacts when their contents change.");
    private readonly Option<bool> _verifyOption = new("--verify", () => false, "Run verification on generated artifacts and emit a verification report.");
    private readonly Option<string?> _verificationReportOption = new("--verification-report", "Path for the verification report JSON file (defaults to {artifacts-root}/uat-users/verification-report.json).");

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
            _userSchemaOption,
            _userTableOption,
            _userIdOption,
            _includeColumnsOption,
            _outputOption,
            _userMapOption,
            _uatInventoryOption,
            _qaInventoryOption,
            _snapshotOption,
            _userEntityIdOption,
            _matchingStrategyOption,
            _matchingAttributeOption,
            _matchingRegexOption,
            _fallbackModeOption,
            _fallbackTargetOption,
            _idempotentEmissionOption,
            _verifyOption,
            _verificationReportOption
        };

        _idempotentEmissionOption.AddAlias("--uat-users-idempotent-emission");

        foreach (var option in _sqlBinder.Options)
        {
            command.AddOption(option);
        }

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

        var cliConfiguration = configurationResult.Value.Configuration;
        var configuration = cliConfiguration.UatUsers;
        var sqlOverrides = _sqlBinder.Bind(parseResult);

        var (modelPath, modelFromConfig) = ResolveStringOption(parseResult, _modelOption, configuration.ModelPath, null);

        var fromLiveSpecified = parseResult.HasOption(_fromLiveOption);
        var fromLive = fromLiveSpecified
            ? parseResult.GetValueForOption(_fromLiveOption)
            : configuration.FromLiveMetadata ?? false;
        var fromLiveFromConfig = !fromLiveSpecified && configuration.FromLiveMetadata.HasValue;

        var connectionString = sqlOverrides.ConnectionString ?? cliConfiguration.Sql.ConnectionString;
        var connectionFromConfig = sqlOverrides.ConnectionString is null && !string.IsNullOrWhiteSpace(cliConfiguration.Sql.ConnectionString);

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
        var (matchingStrategyInput, matchingStrategyFromConfig) = ResolveStringOption(
            parseResult,
            _matchingStrategyOption,
            configuration.MatchingStrategy?.ToString(),
            null);
        var (matchingAttribute, matchingAttributeFromConfig) = ResolveStringOption(
            parseResult,
            _matchingAttributeOption,
            configuration.MatchingAttribute,
            null);
        var (matchingRegex, matchingRegexFromConfig) = ResolveStringOption(
            parseResult,
            _matchingRegexOption,
            configuration.MatchingRegexPattern,
            null);
        var (fallbackModeInput, fallbackModeFromConfig) = ResolveStringOption(
            parseResult,
            _fallbackModeOption,
            configuration.FallbackAssignment?.ToString(),
            null);
        var fallbackTargetsSpecified = parseResult.HasOption(_fallbackTargetOption);
        var fallbackTargets = fallbackTargetsSpecified
            ? parseResult.GetValueForOption(_fallbackTargetOption) ?? Array.Empty<string>()
            : (configuration.FallbackTargets.Count > 0 ? configuration.FallbackTargets.ToArray() : Array.Empty<string>());
        var fallbackTargetsFromConfig = !fallbackTargetsSpecified && configuration.FallbackTargets.Count > 0;

        var idempotentSpecified = parseResult.HasOption(_idempotentEmissionOption);
        var idempotentEmission = idempotentSpecified
            ? parseResult.GetValueForOption(_idempotentEmissionOption)
            : configuration.IdempotentEmission ?? false;
        var idempotentFromConfig = !idempotentSpecified && configuration.IdempotentEmission.HasValue;

        var verifySpecified = parseResult.HasOption(_verifyOption);
        var verifyArtifacts = verifySpecified
            ? parseResult.GetValueForOption(_verifyOption)
            : configuration.VerifyArtifacts ?? false;
        var verifyFromConfig = !verifySpecified && configuration.VerifyArtifacts.HasValue;

        var (verificationReportPath, verificationReportFromConfig) = ResolveStringOption(
            parseResult,
            _verificationReportOption,
            configuration.VerificationReportPath,
            null);

        UserMatchingStrategy matchingStrategy;
        UserFallbackAssignmentMode fallbackMode;
        try
        {
            matchingStrategy = matchingStrategyInput is { Length: > 0 }
                ? UserMatchingConfigurationHelper.ParseStrategy(matchingStrategyInput, UserMatchingStrategy.CaseInsensitiveEmail)
                : configuration.MatchingStrategy ?? UserMatchingStrategy.CaseInsensitiveEmail;
            fallbackMode = fallbackModeInput is { Length: > 0 }
                ? UserMatchingConfigurationHelper.ParseFallbackMode(fallbackModeInput, UserFallbackAssignmentMode.Ignore)
                : configuration.FallbackAssignment ?? UserFallbackAssignmentMode.Ignore;
        }
        catch (ArgumentException ex)
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, ex.Message);
            return;
        }

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
            CommandConsole.WriteErrorLine(context.Console, "--connection-string is required for uat-users.");
            return;
        }

        if (string.IsNullOrWhiteSpace(qaInventoryPath))
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, "--qa-user-inventory is required.");
            return;
        }

        UatUsersOptions options;
        try
        {
            options = new UatUsersOptions(
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
                matchingStrategy,
                matchingAttribute,
                matchingRegex,
                fallbackMode,
                fallbackTargets,
                idempotentEmission,
                verifyArtifacts,
                verificationReportPath,
                new UatUsersOptionOrigins(
                    ModelPathFromConfiguration: modelFromConfig,
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
                    UserEntityIdentifierFromConfiguration: entityIdFromConfig,
                    MatchingStrategyFromConfiguration: matchingStrategyFromConfig,
                    MatchingAttributeFromConfiguration: matchingAttributeFromConfig,
                    MatchingRegexFromConfiguration: matchingRegexFromConfig,
                    FallbackModeFromConfiguration: fallbackModeFromConfig,
                    FallbackTargetsFromConfiguration: fallbackTargetsFromConfig,
                    ConnectionStringFromConfiguration: connectionFromConfig,
                    IdempotentEmissionFromConfiguration: idempotentFromConfig,
                    VerifyArtifactsFromConfiguration: verifyFromConfig,
                    VerificationReportPathFromConfiguration: verificationReportFromConfig));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            context.ExitCode = 1;
            CommandConsole.WriteErrorLine(context.Console, ex.Message);
            return;
        }

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

        var optionResult = parseResult.FindResultFor(option);
        if (optionResult is not null && !optionResult.IsImplicit)
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
