using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.CommandLine.Parsing;
using Osm.Cli;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Osm.Pipeline.Mediation;

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Services.AddLogging(static builder => builder.AddSimpleConsole());
hostBuilder.Services.AddSingleton<ICliConfigurationService, CliConfigurationService>();
hostBuilder.Services.AddSingleton<IModelJsonDeserializer, ModelJsonDeserializer>();
hostBuilder.Services.AddSingleton<IModelIngestionService, ModelIngestionService>();
hostBuilder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
hostBuilder.Services.AddSingleton<ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>, BuildSsdtPipeline>();
hostBuilder.Services.AddSingleton<ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>, DmmComparePipeline>();
hostBuilder.Services.AddSingleton<ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>, ExtractModelPipeline>();
hostBuilder.Services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>, BuildSsdtApplicationService>();
hostBuilder.Services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>, CompareWithDmmApplicationService>();
hostBuilder.Services.AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>, ExtractModelApplicationService>();

using var host = hostBuilder.Build();

var configOption = new Option<string?>("--config", "Path to CLI configuration file.");

var sqlOptions = CreateSqlOptionSet();
var modulesOption = new Option<string?>("--modules", "Comma or semicolon separated list of modules.");
modulesOption.AddAlias("--module");
var includeSystemModulesOption = new Option<bool>("--include-system-modules", "Include system modules when filtering.");
var excludeSystemModulesOption = new Option<bool>("--exclude-system-modules", "Exclude system modules when filtering.");
var includeInactiveModulesOption = new Option<bool>("--include-inactive-modules", "Include inactive modules when filtering.");
var onlyActiveModulesOption = new Option<bool>("--only-active-modules", "Restrict filtering to active modules only.");
var cacheRootOption = new Option<string?>("--cache-root", "Root directory for evidence caching.");
var refreshCacheOption = new Option<bool>("--refresh-cache", "Force cache refresh for this execution.");
var maxParallelOption = new Option<int?>("--max-degree-of-parallelism", "Maximum number of modules processed in parallel.");

var buildCommand = CreateBuildCommand();
var extractCommand = CreateExtractCommand();
var compareCommand = CreateCompareCommand();
var inspectCommand = CreateInspectCommand();

buildCommand.AddGlobalOption(configOption);
buildCommand.AddOption(modulesOption);
buildCommand.AddOption(includeSystemModulesOption);
buildCommand.AddOption(excludeSystemModulesOption);
buildCommand.AddOption(includeInactiveModulesOption);
buildCommand.AddOption(onlyActiveModulesOption);
buildCommand.AddOption(cacheRootOption);
buildCommand.AddOption(refreshCacheOption);
buildCommand.AddOption(maxParallelOption);
AddSqlOptions(buildCommand, sqlOptions);

extractCommand.AddGlobalOption(configOption);
AddSqlOptions(extractCommand, sqlOptions);

compareCommand.AddGlobalOption(configOption);
AddSqlOptions(compareCommand, sqlOptions);
compareCommand.AddOption(modulesOption);
compareCommand.AddOption(includeSystemModulesOption);
compareCommand.AddOption(excludeSystemModulesOption);
compareCommand.AddOption(includeInactiveModulesOption);
compareCommand.AddOption(onlyActiveModulesOption);
compareCommand.AddOption(cacheRootOption);
compareCommand.AddOption(refreshCacheOption);
compareCommand.AddOption(maxParallelOption);

var rootCommand = new RootCommand("OutSystems DDL Exporter CLI")
{
    inspectCommand,
    extractCommand,
    buildCommand,
    compareCommand
};

var parser = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .AddMiddleware((context, next) =>
    {
        context.BindingContext.AddService(_ => host.Services);
        return next(context);
    })
    .Build();

return await parser.InvokeAsync(args);

Command CreateBuildCommand()
{
    var modelOption = new Option<string?>("--model", "Path to the model JSON file.");
    var profileOption = new Option<string?>("--profile", "Path to the profiling snapshot.");
    var profilerProviderOption = new Option<string?>("--profiler-provider", "Profiler provider to use.");
    var staticDataOption = new Option<string?>("--static-data", "Path to static data fixture.");
    var outputOption = new Option<string?>("--out", () => "out", "Output directory for SSDT artifacts.");
    var renameOption = new Option<string?>("--rename-table", "Rename tables using source=Override syntax.");
    var openReportOption = new Option<bool>("--open-report", "Generate and open an HTML report for this run.");

    var command = new Command("build-ssdt", "Emit SSDT artifacts from an OutSystems model.")
    {
        modelOption,
        profileOption,
        profilerProviderOption,
        staticDataOption,
        outputOption,
        renameOption,
        openReportOption
    };

    command.SetHandler(async context =>
    {
        var rootServices = GetServices(context);
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var configurationService = services.GetRequiredService<ICliConfigurationService>();
        var application = services.GetRequiredService<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>();

        var configPath = context.ParseResult.GetValueForOption(configOption);
        var cancellationToken = context.GetCancellationToken();

        var configurationResult = await configurationService.LoadAsync(configPath, cancellationToken);
        if (configurationResult.IsFailure)
        {
            WriteErrors(context, configurationResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var modules = SplitModuleList(context.ParseResult.GetValueForOption(modulesOption));
        var moduleFilter = new ModuleFilterOverrides(
            modules,
            ResolveIncludeOverride(context, includeSystemModulesOption, excludeSystemModulesOption),
            ResolveInactiveOverride(context, includeInactiveModulesOption, onlyActiveModulesOption));

        var cache = new CacheOptionsOverrides(
            context.ParseResult.GetValueForOption(cacheRootOption),
            context.ParseResult.HasOption(refreshCacheOption) ? true : null);

        var overrides = new BuildSsdtOverrides(
            context.ParseResult.GetValueForOption(modelOption),
            context.ParseResult.GetValueForOption(profileOption),
            context.ParseResult.GetValueForOption(outputOption),
            context.ParseResult.GetValueForOption(profilerProviderOption),
            context.ParseResult.GetValueForOption(staticDataOption),
            context.ParseResult.GetValueForOption(renameOption),
            context.ParseResult.GetValueForOption(maxParallelOption));

        var input = new BuildSsdtApplicationInput(
            configurationResult.Value,
            overrides,
            moduleFilter,
            CreateSqlOverrides(context.ParseResult, sqlOptions),
            cache);

        var result = await application.RunAsync(input, cancellationToken);
        if (result.IsFailure)
        {
            WriteErrors(context, result.Errors);
            context.ExitCode = 1;
            return;
        }

        var applicationResult = result.Value;
        var pipelineResult = applicationResult.PipelineResult;

        if (IsSqlProfiler(applicationResult.ProfilerProvider))
        {
            EmitSqlProfilerSnapshot(context, pipelineResult.Profile);
        }

        EmitPipelineLog(context, pipelineResult.ExecutionLog);

        foreach (var diagnostic in pipelineResult.DecisionReport.Diagnostics)
        {
            if (diagnostic.Severity == TighteningDiagnosticSeverity.Warning)
            {
                WriteErrorLine(context.Console, $"[warning] {diagnostic.Message}");
            }
        }

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            WriteLine(context.Console, $"Cached inputs to {cacheResult.CacheDirectory} (key {cacheResult.Manifest.Key}).");
        }

        if (!string.IsNullOrWhiteSpace(pipelineResult.StaticSeedScriptPath))
        {
            WriteLine(context.Console, $"Static entity seed script written to {pipelineResult.StaticSeedScriptPath}");
        }

        WriteLine(context.Console, $"Emitted {pipelineResult.Manifest.Tables.Count} tables to {applicationResult.OutputDirectory}.");
        WriteLine(context.Console, $"Manifest written to {Path.Combine(applicationResult.OutputDirectory, "manifest.json")}");
        WriteLine(context.Console, $"Columns tightened: {pipelineResult.DecisionReport.TightenedColumnCount}/{pipelineResult.DecisionReport.ColumnCount}");
        WriteLine(context.Console, $"Unique indexes enforced: {pipelineResult.DecisionReport.UniqueIndexesEnforcedCount}/{pipelineResult.DecisionReport.UniqueIndexCount}");
        WriteLine(context.Console, $"Foreign keys created: {pipelineResult.DecisionReport.ForeignKeysCreatedCount}/{pipelineResult.DecisionReport.ForeignKeyCount}");

        foreach (var summary in PolicyDecisionSummaryFormatter.FormatForConsole(pipelineResult.DecisionReport))
        {
            WriteLine(context.Console, summary);
        }

        WriteLine(context.Console, $"Decision log written to {pipelineResult.DecisionLogPath}");

        if (context.ParseResult.GetValueForOption(openReportOption))
        {
            try
            {
                var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, cancellationToken).ConfigureAwait(false);
                WriteLine(context.Console, $"Report written to {reportPath}");
                PipelineReportLauncher.TryOpen(reportPath, context.Console);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteErrorLine(context.Console, $"[warning] Failed to open report: {ex.Message}");
            }
        }

        context.ExitCode = 0;
    });

    return command;
}

Command CreateExtractCommand()
{
    var modulesOptionLocal = new Option<string?>("--modules", "Comma or semicolon separated list of modules.");
    modulesOptionLocal.AddAlias("--module");
    var includeSystemOption = new Option<bool>("--include-system-modules", "Include system modules during extraction.");
    var onlyActiveAttributesOption = new Option<bool>("--only-active-attributes", "Extract only active attributes.");
    var outputOption = new Option<string?>("--out", () => "model.extracted.json", "Output path for extracted model JSON.");
    var mockSqlOption = new Option<string?>("--mock-advanced-sql", "Path to advanced SQL manifest fixture.");

    var command = new Command("extract-model", "Extract the OutSystems model using Advanced SQL.")
    {
        modulesOptionLocal,
        includeSystemOption,
        onlyActiveAttributesOption,
        outputOption,
        mockSqlOption
    };

    command.SetHandler(async context =>
    {
        var rootServices = GetServices(context);
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var configurationService = services.GetRequiredService<ICliConfigurationService>();
        var application = services.GetRequiredService<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>>();

        var configPath = context.ParseResult.GetValueForOption(configOption);
        var configurationResult = await configurationService.LoadAsync(configPath, context.GetCancellationToken());
        if (configurationResult.IsFailure)
        {
            WriteErrors(context, configurationResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var modules = SplitModuleList(context.ParseResult.GetValueForOption(modulesOptionLocal));
        var overrides = new ExtractModelOverrides(
            modules,
            context.ParseResult.HasOption(includeSystemOption),
            context.ParseResult.HasOption(onlyActiveAttributesOption),
            context.ParseResult.GetValueForOption(outputOption),
            context.ParseResult.GetValueForOption(mockSqlOption));

        var input = new ExtractModelApplicationInput(
            configurationResult.Value,
            overrides,
            CreateSqlOverrides(context.ParseResult, sqlOptions));

        var result = await application.RunAsync(input, context.GetCancellationToken());
        if (result.IsFailure)
        {
            WriteErrors(context, result.Errors);
            context.ExitCode = 1;
            return;
        }

        var outputPath = result.Value.OutputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
        await File.WriteAllTextAsync(outputPath, result.Value.ExtractionResult.Json, context.GetCancellationToken());

        var model = result.Value.ExtractionResult.Model;
        var moduleCount = model.Modules.Length;
        var entityCount = model.Modules.Sum(static m => m.Entities.Length);
        var attributeCount = model.Modules.Sum(static m => m.Entities.Sum(static e => e.Attributes.Length));

        if (result.Value.ExtractionResult.Warnings.Count > 0)
        {
            foreach (var warning in result.Value.ExtractionResult.Warnings)
            {
                WriteErrorLine(context.Console, $"Warning: {warning}");
            }
        }

        WriteLine(context.Console, $"Extracted {moduleCount} modules spanning {entityCount} entities.");
        WriteLine(context.Console, $"Attributes: {attributeCount}");
        WriteLine(context.Console, $"Model written to {outputPath}.");
        WriteLine(context.Console, $"Extraction timestamp (UTC): {result.Value.ExtractionResult.ExtractedAtUtc:O}");
        context.ExitCode = 0;
    });

    return command;
}

Command CreateCompareCommand()
{
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

    command.SetHandler(async context =>
    {
        var rootServices = GetServices(context);
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var configurationService = services.GetRequiredService<ICliConfigurationService>();
        var application = services.GetRequiredService<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>>();

        var configPath = context.ParseResult.GetValueForOption(configOption);
        var configurationResult = await configurationService.LoadAsync(configPath, context.GetCancellationToken());
        if (configurationResult.IsFailure)
        {
            WriteErrors(context, configurationResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var modules = SplitModuleList(context.ParseResult.GetValueForOption(modulesOption));
        var moduleFilter = new ModuleFilterOverrides(
            modules,
            ResolveIncludeOverride(context, includeSystemModulesOption, excludeSystemModulesOption),
            ResolveInactiveOverride(context, includeInactiveModulesOption, onlyActiveModulesOption));

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
            CreateSqlOverrides(context.ParseResult, sqlOptions),
            cache);

        var result = await application.RunAsync(input, context.GetCancellationToken());
        if (result.IsFailure)
        {
            WriteErrors(context, result.Errors);
            context.ExitCode = 1;
            return;
        }

        var pipelineResult = result.Value.PipelineResult;
        EmitPipelineLog(context, pipelineResult.ExecutionLog);

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            WriteLine(context.Console, $"Cached inputs to {cacheResult.CacheDirectory} (key {cacheResult.Manifest.Key}).");
        }

        if (pipelineResult.Comparison.IsMatch)
        {
            WriteLine(context.Console, $"DMM parity confirmed. Diff artifact written to {result.Value.DiffOutputPath}.");
            context.ExitCode = 0;
            return;
        }

        if (pipelineResult.Comparison.ModelDifferences.Count > 0)
        {
            WriteErrorLine(context.Console, "Model requires additional SSDT coverage:");
            foreach (var difference in pipelineResult.Comparison.ModelDifferences)
            {
                WriteErrorLine(context.Console, $" - {difference}");
            }
        }

        if (pipelineResult.Comparison.SsdtDifferences.Count > 0)
        {
            WriteErrorLine(context.Console, "SSDT scripts drift from OutSystems model:");
            foreach (var difference in pipelineResult.Comparison.SsdtDifferences)
            {
                WriteErrorLine(context.Console, $" - {difference}");
            }
        }

        WriteErrorLine(context.Console, $"Diff artifact written to {result.Value.DiffOutputPath}.");
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
        var rootServices = GetServices(context);
        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var ingestion = services.GetRequiredService<IModelIngestionService>();
        var modelPath = context.ParseResult.GetValueForOption(modelOption);

        var result = await ingestion.LoadFromFileAsync(modelPath!, context.GetCancellationToken()).ConfigureAwait(false);
        if (result.IsFailure)
        {
            WriteErrors(context, result.Errors);
            context.ExitCode = 1;
            return;
        }

        var model = result.Value;
        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(static entity => entity.Attributes.Length));

        WriteLine(context.Console, $"Model exported at {model.ExportedAtUtc:O}");
        WriteLine(context.Console, $"Modules: {model.Modules.Length}");
        WriteLine(context.Console, $"Entities: {entityCount}");
        WriteLine(context.Console, $"Attributes: {attributeCount}");
        context.ExitCode = 0;
    });

    return command;
}

static IServiceProvider GetServices(InvocationContext context)
    => context.BindingContext.GetRequiredService<IServiceProvider>();

static void AddSqlOptions(Command command, SqlOptionSet optionSet)
{
    command.AddOption(optionSet.ConnectionString);
    command.AddOption(optionSet.CommandTimeout);
    command.AddOption(optionSet.SamplingThreshold);
    command.AddOption(optionSet.SamplingSize);
    command.AddOption(optionSet.AuthenticationMethod);
    command.AddOption(optionSet.TrustServerCertificate);
    command.AddOption(optionSet.ApplicationName);
    command.AddOption(optionSet.AccessToken);
    command.AddOption(optionSet.ProfilerMaxDegreeOfParallelism);
    command.AddOption(optionSet.ProfilerBatchSize);
    command.AddOption(optionSet.ProfilerRetryCount);
    command.AddOption(optionSet.ProfilerRetryBaseDelay);
    command.AddOption(optionSet.ProfilerRetryJitter);
}

static SqlOptionsOverrides CreateSqlOverrides(ParseResult parseResult, SqlOptionSet optionSet)
    => new(
        parseResult.GetValueForOption(optionSet.ConnectionString),
        parseResult.GetValueForOption(optionSet.CommandTimeout),
        parseResult.GetValueForOption(optionSet.SamplingThreshold),
        parseResult.GetValueForOption(optionSet.SamplingSize),
        parseResult.GetValueForOption(optionSet.AuthenticationMethod),
        parseResult.GetValueForOption(optionSet.TrustServerCertificate),
        parseResult.GetValueForOption(optionSet.ApplicationName),
        parseResult.GetValueForOption(optionSet.AccessToken),
        parseResult.GetValueForOption(optionSet.ProfilerMaxDegreeOfParallelism),
        parseResult.GetValueForOption(optionSet.ProfilerBatchSize),
        parseResult.GetValueForOption(optionSet.ProfilerRetryCount),
        parseResult.GetValueForOption(optionSet.ProfilerRetryBaseDelay),
        parseResult.GetValueForOption(optionSet.ProfilerRetryJitter));

static void WriteLine(IConsole console, string message)
    => console.Out.Write(message + Environment.NewLine);

static void WriteErrorLine(IConsole console, string message)
    => console.Error.Write(message + Environment.NewLine);

static IReadOnlyList<string> SplitModuleList(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return Array.Empty<string>();
    }

    var separators = new[] { ',', ';' };
    return value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static bool? ResolveIncludeOverride(InvocationContext context, Option<bool> includeOption, Option<bool> excludeOption)
{
    if (context.ParseResult.HasOption(includeOption))
    {
        return true;
    }

    if (context.ParseResult.HasOption(excludeOption))
    {
        return false;
    }

    return null;
}

static bool? ResolveInactiveOverride(InvocationContext context, Option<bool> includeInactiveOption, Option<bool> onlyActiveOption)
{
    if (context.ParseResult.HasOption(includeInactiveOption))
    {
        return true;
    }

    if (context.ParseResult.HasOption(onlyActiveOption))
    {
        return false;
    }

    return null;
}

static bool IsSqlProfiler(string provider)
    => string.Equals(provider, "sql", StringComparison.OrdinalIgnoreCase);

static void EmitSqlProfilerSnapshot(InvocationContext context, Osm.Domain.Profiling.ProfileSnapshot snapshot)
{
    WriteLine(context.Console, "SQL profiler snapshot:");
    WriteLine(context.Console, ProfileSnapshotDebugFormatter.ToJson(snapshot));
}

static void EmitPipelineLog(InvocationContext context, PipelineExecutionLog log)
{
    if (log is null || log.Entries.Count == 0)
    {
        return;
    }

    WriteLine(context.Console, "Pipeline execution log:");
    foreach (var entry in log.Entries)
    {
        var metadata = entry.Metadata.Count == 0
            ? string.Empty
            : " | " + string.Join(", ", entry.Metadata.Select(pair => $"{pair.Key}={FormatMetadataValue(pair.Value)}"));

        WriteLine(context.Console, $"[{entry.TimestampUtc:O}] {entry.Step}: {entry.Message}{metadata}");
    }
}

static string FormatMetadataValue(string? value)
    => value ?? "<null>";

static void WriteErrors(InvocationContext context, IEnumerable<ValidationError> errors)
{
    foreach (var error in errors)
    {
        WriteErrorLine(context.Console, $"{error.Code}: {error.Message}");
    }
}

static SqlOptionSet CreateSqlOptionSet()
{
    var connectionString = new Option<string?>("--connection-string", "SQL connection string override.");
    var commandTimeout = new Option<int?>("--command-timeout", "Command timeout in seconds.");
    var samplingThreshold = new Option<long?>("--sampling-threshold", "Row sampling threshold for SQL profiler.");
    var samplingSize = new Option<int?>("--sampling-size", "Sampling size for SQL profiler.");
    var authenticationMethod = new Option<SqlAuthenticationMethod?>("--sql-authentication", "SQL authentication method.");
    var trustServerCertificate = new Option<bool?>("--sql-trust-server-certificate", result =>
    {
        if (result.Tokens.Count == 0)
        {
            return true;
        }

        if (bool.TryParse(result.Tokens[0].Value, out var parsed))
        {
            return parsed;
        }

        result.ErrorMessage = "Invalid value for --sql-trust-server-certificate. Expected 'true' or 'false'.";
        return null;
    })
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "Trust server certificate when connecting to SQL Server."
    };
    var applicationName = new Option<string?>("--sql-application-name", "Application name for SQL connections.");
    var accessToken = new Option<string?>("--sql-access-token", "Access token for SQL authentication.");
    var profilerMaxDop = new Option<int?>("--profiler-max-dop", "Maximum degree of parallelism for SQL profiling.");
    var profilerBatchSize = new Option<int?>("--profiler-batch-size", "Number of tables to include in each profiling batch.");
    var profilerRetryCount = new Option<int?>("--profiler-retry-count", "Retry attempts for transient SQL profiling errors.");
    var profilerRetryBaseDelay = new Option<double?>("--profiler-retry-base-delay", "Base delay in seconds before retrying profiling queries.");
    var profilerRetryJitter = new Option<double?>("--profiler-retry-jitter", "Maximum jitter in seconds to randomize profiling retries.");

    return new SqlOptionSet(
        connectionString,
        commandTimeout,
        samplingThreshold,
        samplingSize,
        authenticationMethod,
        trustServerCertificate,
        applicationName,
        accessToken,
        profilerMaxDop,
        profilerBatchSize,
        profilerRetryCount,
        profilerRetryBaseDelay,
        profilerRetryJitter);
}

readonly record struct SqlOptionSet(
    Option<string?> ConnectionString,
    Option<int?> CommandTimeout,
    Option<long?> SamplingThreshold,
    Option<int?> SamplingSize,
    Option<SqlAuthenticationMethod?> AuthenticationMethod,
    Option<bool?> TrustServerCertificate,
    Option<string?> ApplicationName,
    Option<string?> AccessToken,
    Option<int?> ProfilerMaxDegreeOfParallelism,
    Option<int?> ProfilerBatchSize,
    Option<int?> ProfilerRetryCount,
    Option<double?> ProfilerRetryBaseDelay,
    Option<double?> ProfilerRetryJitter);
