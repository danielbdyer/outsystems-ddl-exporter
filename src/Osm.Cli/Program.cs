using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Collections.Immutable;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Osm.Cli;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Validation.Tightening;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOsmCli();

using var host = builder.Build();

var rootCommand = new RootCommand("OutSystems DDL Exporter CLI");

var buildCommand = BuildCommandFactory.Create();
var extractCommand = ExtractCommandFactory.Create();
var compareCommand = CreateCompareCommand();
var inspectCommand = CreateInspectCommand();

rootCommand.AddCommand(inspectCommand);
rootCommand.AddCommand(extractCommand);
rootCommand.AddCommand(buildCommand);
rootCommand.AddCommand(compareCommand);

if (host.Services.GetService<UatUsersCommand>() is not null)
{
    rootCommand.AddCommand(CreateUatUsersCommand());
}

var parser = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .AddMiddleware((context, next) =>
    {
        context.BindingContext.AddService(_ => host.Services);
        return next(context);
    })
    .Build();

return await parser.InvokeAsync(args);

Command CreateUatUsersCommand()
{
    var modelOption = new Option<string?>("--model", "Path to the UAT model JSON file.");
    var fromLiveOption = new Option<bool>("--from-live", "Discover catalog from live metadata.");
    var uatConnectionOption = new Option<string?>("--uat-conn", "ADO.NET connection string for the UAT database (required with --from-live).");
    var userSchemaOption = new Option<string?>("--user-schema", () => "dbo", "Schema that owns the user table.");
    var userTableOption = new Option<string?>("--user-table", () => "User", "User table name.");
    var userIdOption = new Option<string?>("--user-id-column", () => "Id", "Primary key column for the user table.");
    var includeColumnsOption = new Option<string[]>(
        name: "--include-columns",
        parseArgument: static result => result.Tokens
            .Select(token => token.Value)
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray(),
        description: "Restrict catalog to specific column names.")
    {
        AllowMultipleArgumentsPerToken = true
    };
    var outputOption = new Option<string?>("--out", () => "./_artifacts", "Root directory for artifacts.");
    var userMapOption = new Option<string?>("--user-map", "Path to a CSV containing SourceUserId,TargetUserId mappings.");
    var userDdlOption = new Option<string?>("--user-ddl", "SQL or CSV export of dbo.User containing allowed user identifiers.");
    var userIdsOption = new Option<string?>("--user-ids", "Optional CSV or text file containing one allowed user identifier per row.");
    var snapshotOption = new Option<string?>("--snapshot", "Optional path to cache foreign key scans as a snapshot.");
    var userEntityIdOption = new Option<string?>(
        "--user-entity-id",
        "Optional override identifier for the user entity (accepts bt*GUID*GUID, physical name, or numeric id).");

    var command = new Command("uat-users", "Emit user remapping artifacts for UAT.")
    {
        modelOption,
        fromLiveOption,
        uatConnectionOption,
        userSchemaOption,
        userTableOption,
        userIdOption,
        includeColumnsOption,
        outputOption,
        userMapOption,
        userDdlOption,
        userIdsOption,
        snapshotOption,
        userEntityIdOption
    };

    command.SetHandler(async context =>
    {
        var rootServices = CliCommandUtilities.GetServices(context);
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var handler = services.GetRequiredService<UatUsersCommand>();

        var parseResult = context.ParseResult;
        var userSchema = parseResult.GetValueForOption(userSchemaOption) ?? "dbo";
        var tableValue = parseResult.GetValueForOption(userTableOption) ?? "User";
        if (tableValue.Contains('.', StringComparison.Ordinal))
        {
            var parts = SplitTableIdentifier(tableValue);
            userSchema = parts.Schema;
            tableValue = parts.Table;
        }
        var allowedDdl = parseResult.GetValueForOption(userDdlOption);
        var allowedIds = parseResult.GetValueForOption(userIdsOption);
        if (string.IsNullOrWhiteSpace(allowedDdl) && string.IsNullOrWhiteSpace(allowedIds))
        {
            context.ExitCode = 1;
            CliCommandUtilities.WriteLine(context.Console, "Either --user-ddl or --user-ids must be supplied.");
            return;
        }

        var options = new UatUsersOptions(
            parseResult.GetValueForOption(modelOption),
            parseResult.GetValueForOption(uatConnectionOption),
            parseResult.GetValueForOption(fromLiveOption),
            userSchema,
            tableValue,
            parseResult.GetValueForOption(userIdOption) ?? "Id",
            parseResult.GetValueForOption(includeColumnsOption),
            parseResult.GetValueForOption(outputOption) ?? "./_artifacts",
            parseResult.GetValueForOption(userMapOption),
            allowedDdl,
            allowedIds,
            parseResult.GetValueForOption(snapshotOption),
            parseResult.GetValueForOption(userEntityIdOption));

        if (!options.FromLiveMetadata && string.IsNullOrWhiteSpace(options.ModelPath))
        {
            context.ExitCode = 1;
            CliCommandUtilities.WriteLine(context.Console, "--model is required when --from-live is not specified.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.UatConnectionString))
        {
            context.ExitCode = 1;
            CliCommandUtilities.WriteLine(context.Console, "--uat-conn is required.");
            return;
        }

        var exitCode = await handler.ExecuteAsync(options, context.GetCancellationToken()).ConfigureAwait(false);
        context.ExitCode = exitCode;
    });

    return command;

    static (string Schema, string Table) SplitTableIdentifier(string identifier)
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

Command CreateCompareCommand()
{
    var configOption = CliOptions.CreateConfigOption();
    var modulesOption = CliOptions.CreateModulesOption();
    var includeSystemModulesOption = CliOptions.CreateIncludeSystemModulesOption("Include system modules when filtering.");
    var excludeSystemModulesOption = CliOptions.CreateExcludeSystemModulesOption("Exclude system modules when filtering.");
    var includeInactiveModulesOption = CliOptions.CreateIncludeInactiveModulesOption("Include inactive modules when filtering.");
    var onlyActiveModulesOption = CliOptions.CreateOnlyActiveModulesOption("Restrict filtering to active modules only.");
    var cacheRootOption = CliOptions.CreateCacheRootOption();
    var refreshCacheOption = CliOptions.CreateRefreshCacheOption();
    var maxParallelOption = CliOptions.CreateMaxDegreeOfParallelismOption();
    var allowMissingPrimaryKeyOption = CliOptions.CreateOverrideOption("--allow-missing-primary-key", "Allow ingestion to include entities without primary keys. Use Module::Entity or Module::*.");
    var allowMissingSchemaOption = CliOptions.CreateOverrideOption("--allow-missing-schema", "Allow ingestion to include entities without schema names. Use Module::Entity or Module::*.");
    var sqlOptions = CliOptions.CreateSqlOptionSet();

    var modelOption = new Option<string?>("--model", "Path to the model JSON file.");
    var profileOption = new Option<string?>("--profile", "Path to the profiling snapshot.");
    var dmmOption = new Option<string?>("--dmm", "Path to the baseline DMM script.");
    var outputOption = new Option<string?>("--out", () => "out", "Output directory for comparison artifacts.");

    var command = new Command("dmm-compare", "Compare the emitted SSDT artifacts with a DMM baseline.")
    {
        modelOption,
        profileOption,
        dmmOption,
        outputOption
    };

    command.AddGlobalOption(configOption);
    command.AddOption(modulesOption);
    command.AddOption(includeSystemModulesOption);
    command.AddOption(excludeSystemModulesOption);
    command.AddOption(includeInactiveModulesOption);
    command.AddOption(onlyActiveModulesOption);
    command.AddOption(cacheRootOption);
    command.AddOption(refreshCacheOption);
    command.AddOption(maxParallelOption);
    command.AddOption(allowMissingPrimaryKeyOption);
    command.AddOption(allowMissingSchemaOption);
    CliCommandUtilities.AddSqlOptions(command, sqlOptions);

    command.SetHandler(async context =>
    {
        var rootServices = CliCommandUtilities.GetServices(context);
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var configurationService = services.GetRequiredService<ICliConfigurationService>();
        var application = services.GetRequiredService<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>>();

        var configPath = context.ParseResult.GetValueForOption(configOption);
        var configurationResult = await configurationService.LoadAsync(configPath, context.GetCancellationToken()).ConfigureAwait(false);
        if (configurationResult.IsFailure)
        {
            CliCommandUtilities.WriteErrors(context, configurationResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var modules = CliCommandUtilities.SplitModuleList(context.ParseResult.GetValueForOption(modulesOption));
        var allowMissingPrimaryKey = CliCommandUtilities.SplitOverrideList(context.ParseResult.GetValueForOption(allowMissingPrimaryKeyOption));
        var allowMissingSchema = CliCommandUtilities.SplitOverrideList(context.ParseResult.GetValueForOption(allowMissingSchemaOption));
        var moduleFilter = new ModuleFilterOverrides(
            modules,
            CliCommandUtilities.ResolveIncludeOverride(context, includeSystemModulesOption, excludeSystemModulesOption),
            CliCommandUtilities.ResolveInactiveOverride(context, includeInactiveModulesOption, onlyActiveModulesOption),
            allowMissingPrimaryKey,
            allowMissingSchema);

        var cache = new CacheOptionsOverrides(
            context.ParseResult.GetValueForOption(cacheRootOption),
            context.ParseResult.HasOption(refreshCacheOption) ? true : null);

        var overrides = new CompareWithDmmOverrides(
            context.ParseResult.GetValueForOption(modelOption),
            context.ParseResult.GetValueForOption(profileOption),
            context.ParseResult.GetValueForOption(dmmOption),
            context.ParseResult.GetValueForOption(outputOption),
            context.ParseResult.GetValueForOption(maxParallelOption));

        var input = new CompareWithDmmApplicationInput(
            configurationResult.Value,
            overrides,
            moduleFilter,
            CliCommandUtilities.CreateSqlOverrides(context.ParseResult, sqlOptions),
            cache);

        var result = await application.RunAsync(input, context.GetCancellationToken()).ConfigureAwait(false);
        if (result.IsFailure)
        {
            CliCommandUtilities.WriteErrors(context, result.Errors);
            context.ExitCode = 1;
            return;
        }

        var pipelineResult = result.Value.PipelineResult;
        CliCommandUtilities.EmitPipelineLog(context, pipelineResult.ExecutionLog);
        CliCommandUtilities.EmitPipelineWarnings(context, pipelineResult.Warnings);

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            CliCommandUtilities.WriteLine(context.Console, $"Cached inputs to {cacheResult.CacheDirectory} (key {cacheResult.Manifest.Key}).");
        }

        if (pipelineResult.Comparison.IsMatch)
        {
            CliCommandUtilities.WriteLine(context.Console, $"DMM parity confirmed. Diff artifact written to {result.Value.DiffOutputPath}.");
            context.ExitCode = 0;
            return;
        }

        if (pipelineResult.Comparison.ModelDifferences.Count > 0)
        {
            CliCommandUtilities.WriteErrorLine(context.Console, "Model requires additional SSDT coverage:");
            foreach (var difference in pipelineResult.Comparison.ModelDifferences)
            {
                CliCommandUtilities.WriteErrorLine(context.Console, $" - {CliCommandUtilities.FormatDifference(difference)}");
            }
        }

        if (pipelineResult.Comparison.SsdtDifferences.Count > 0)
        {
            CliCommandUtilities.WriteErrorLine(context.Console, "SSDT scripts drift from OutSystems model:");
            foreach (var difference in pipelineResult.Comparison.SsdtDifferences)
            {
                CliCommandUtilities.WriteErrorLine(context.Console, $" - {CliCommandUtilities.FormatDifference(difference)}");
            }
        }

        CliCommandUtilities.WriteErrorLine(context.Console, $"Diff artifact written to {result.Value.DiffOutputPath}.");
        context.ExitCode = 2;
    });

    return command;
}

Command CreateInspectCommand()
{
    var modelOption = new Option<string>("--model", "Path to the model JSON file.");
    modelOption.AddAlias("--in");
    modelOption.IsRequired = true;
    var command = new Command("inspect", "Inspect an OutSystems model JSON file.") { modelOption };

    command.SetHandler(async context =>
    {
        var rootServices = CliCommandUtilities.GetServices(context);
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var ingestion = services.GetRequiredService<IModelIngestionService>();
        var modelPath = context.ParseResult.GetValueForOption(modelOption);

        var warnings = new List<string>();
        var result = await ingestion
            .LoadFromFileAsync(modelPath!, warnings, context.GetCancellationToken())
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            CliCommandUtilities.WriteErrors(context, result.Errors);
            context.ExitCode = 1;
            return;
        }

        var model = result.Value;
        CliCommandUtilities.EmitPipelineWarnings(context, warnings.ToImmutableArray());
        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(static entity => entity.Attributes.Length));

        CliCommandUtilities.WriteLine(context.Console, $"Model exported at {model.ExportedAtUtc:O}");
        CliCommandUtilities.WriteLine(context.Console, $"Modules: {model.Modules.Length}");
        CliCommandUtilities.WriteLine(context.Console, $"Entities: {entityCount}");
        CliCommandUtilities.WriteLine(context.Console, $"Attributes: {attributeCount}");
        context.ExitCode = 0;
    });

    return command;
}

