using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal sealed class BuildSsdtCommandFactory : PipelineVerbCommandFactory
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheOptionBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot.");
    private readonly Option<string?> _profilerProviderOption = new("--profiler-provider", "Profiler provider to use.");
    private readonly Option<string?> _staticDataOption = new("--static-data", "Path to static data fixture.");
    private readonly Option<string?> _outputOption = new("--out", () => "out", "Output directory for SSDT artifacts.");
    private readonly Option<string?> _renameOption = new("--rename-table", "Rename tables using source=Override syntax.");
    private readonly Option<bool> _openReportOption = new("--open-report", "Generate and open an HTML report for this run.");

    public BuildSsdtCommandFactory(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        CacheOptionBinder cacheOptionBinder,
        SqlOptionBinder sqlOptionBinder)
        : base(scopeFactory)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _moduleFilterBinder = moduleFilterBinder ?? throw new ArgumentNullException(nameof(moduleFilterBinder));
        _cacheOptionBinder = cacheOptionBinder ?? throw new ArgumentNullException(nameof(cacheOptionBinder));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
    }

    public Command Create()
    {
        var command = new Command("build-ssdt", "Emit SSDT artifacts from an OutSystems model.")
        {
            _modelOption,
            _profileOption,
            _profilerProviderOption,
            _staticDataOption,
            _outputOption,
            _renameOption,
            _openReportOption,
            _globalOptions.MaxDegreeOfParallelism
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddModuleFilterOptions(command, _moduleFilterBinder);
        CommandOptionBuilder.AddCacheOptions(command, _cacheOptionBinder);
        CommandOptionBuilder.AddSqlOptions(command, _sqlOptionBinder);

        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        await ExecuteVerbAsync<BuildSsdtVerbOptions, BuildSsdtVerbResult>(
                context,
                BuildSsdtVerb.VerbName,
                (_, invocationContext) => CreateOptions(invocationContext),
                (_, invocationContext, payload) => HandleSuccessAsync(invocationContext, payload),
                "[error] Unexpected result type for build-ssdt verb.")
            .ConfigureAwait(false);
    }

    private BuildSsdtVerbOptions CreateOptions(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var moduleFilter = _moduleFilterBinder.Bind(parseResult);
        var cache = _cacheOptionBinder.Bind(parseResult);
        var sqlOverrides = _sqlOptionBinder.Bind(parseResult);

        var overrides = new BuildSsdtOverrides(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_outputOption),
            parseResult.GetValueForOption(_profilerProviderOption),
            parseResult.GetValueForOption(_staticDataOption),
            parseResult.GetValueForOption(_renameOption),
            parseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism));

        return new BuildSsdtVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            ModuleFilter = moduleFilter,
            Sql = sqlOverrides,
            Cache = cache
        };
    }

    private async ValueTask HandleSuccessAsync(InvocationContext context, BuildSsdtVerbResult payload)
    {
        await EmitResultsAsync(context, payload).ConfigureAwait(false);
        context.ExitCode = 0;
    }

    private async Task EmitResultsAsync(InvocationContext context, BuildSsdtVerbResult payload)
    {
        var applicationResult = payload.ApplicationResult;
        var pipelineResult = applicationResult.PipelineResult;

        if (!string.IsNullOrWhiteSpace(applicationResult.ModelPath))
        {
            var modelMessage = applicationResult.ModelWasExtracted
                ? $"Extracted model to {applicationResult.ModelPath}."
                : $"Using model at {applicationResult.ModelPath}.";
            CommandConsole.WriteLine(context.Console, modelMessage);
        }

        if (!applicationResult.ModelExtractionWarnings.IsDefaultOrEmpty && applicationResult.ModelExtractionWarnings.Length > 0)
        {
            CommandConsole.EmitPipelineWarnings(context.Console, applicationResult.ModelExtractionWarnings);
        }

        if (IsSqlProfiler(applicationResult.ProfilerProvider))
        {
            CommandConsole.EmitSqlProfilerSnapshot(context.Console, pipelineResult.Profile);
        }

        CommandConsole.EmitPipelineLog(context.Console, pipelineResult.ExecutionLog);
        CommandConsole.EmitPipelineWarnings(context.Console, pipelineResult.Warnings);

        foreach (var diagnostic in pipelineResult.DecisionReport.Diagnostics)
        {
            if (diagnostic.Severity == TighteningDiagnosticSeverity.Warning)
            {
                CommandConsole.WriteErrorLine(context.Console, $"[warning] {diagnostic.Message}");
            }
        }

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            CommandConsole.WriteLine(context.Console, $"Cached inputs to {cacheResult.CacheDirectory} (key {cacheResult.Manifest.Key}).");
        }

        if (!pipelineResult.StaticSeedScriptPaths.IsDefaultOrEmpty && pipelineResult.StaticSeedScriptPaths.Length > 0)
        {
            foreach (var seedPath in pipelineResult.StaticSeedScriptPaths)
            {
                CommandConsole.WriteLine(context.Console, $"Static entity seed script written to {seedPath}");
            }
        }

        CommandConsole.WriteLine(context.Console, $"Emitted {pipelineResult.Manifest.Tables.Count} tables to {applicationResult.OutputDirectory}.");
        CommandConsole.WriteLine(context.Console, $"Manifest written to {Path.Combine(applicationResult.OutputDirectory, "manifest.json")}");
        CommandConsole.WriteLine(context.Console, $"Columns tightened: {pipelineResult.DecisionReport.TightenedColumnCount}/{pipelineResult.DecisionReport.ColumnCount}");
        CommandConsole.WriteLine(context.Console, $"Unique indexes enforced: {pipelineResult.DecisionReport.UniqueIndexesEnforcedCount}/{pipelineResult.DecisionReport.UniqueIndexCount}");
        CommandConsole.WriteLine(context.Console, $"Foreign keys created: {pipelineResult.DecisionReport.ForeignKeysCreatedCount}/{pipelineResult.DecisionReport.ForeignKeyCount}");

        foreach (var summary in PolicyDecisionSummaryFormatter.FormatForConsole(pipelineResult.DecisionReport))
        {
            CommandConsole.WriteLine(context.Console, summary);
        }

        CommandConsole.WriteLine(context.Console, $"Decision log written to {pipelineResult.DecisionLogPath}");

        if (!context.ParseResult.GetValueForOption(_openReportOption))
        {
            return;
        }

        try
        {
            var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, context.GetCancellationToken()).ConfigureAwait(false);
            CommandConsole.WriteLine(context.Console, $"Report written to {reportPath}");
            PipelineReportLauncher.TryOpen(reportPath, context.Console);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CommandConsole.WriteErrorLine(context.Console, $"[warning] Failed to open report: {ex.Message}");
        }
    }

    private static bool IsSqlProfiler(string provider)
        => string.Equals(provider, "sql", StringComparison.OrdinalIgnoreCase);
}
