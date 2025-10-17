using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal sealed class BuildSsdtCommandFactory : ICommandFactory
{
    private readonly PipelineVerbExecutor _verbExecutor;
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
        PipelineVerbExecutor verbExecutor,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        CacheOptionBinder cacheOptionBinder,
        SqlOptionBinder sqlOptionBinder)
    {
        _verbExecutor = verbExecutor ?? throw new ArgumentNullException(nameof(verbExecutor));
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
        var cancellationToken = context.GetCancellationToken();
        var moduleFilter = _moduleFilterBinder.Bind(context.ParseResult);
        var cache = _cacheOptionBinder.Bind(context.ParseResult);
        var sqlOverrides = _sqlOptionBinder.Bind(context.ParseResult);

        var overrides = new BuildSsdtOverrides(
            context.ParseResult.GetValueForOption(_modelOption),
            context.ParseResult.GetValueForOption(_profileOption),
            context.ParseResult.GetValueForOption(_outputOption),
            context.ParseResult.GetValueForOption(_profilerProviderOption),
            context.ParseResult.GetValueForOption(_staticDataOption),
            context.ParseResult.GetValueForOption(_renameOption),
            context.ParseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism));

        var options = new BuildSsdtVerbOptions
        {
            ConfigurationPath = context.ParseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            ModuleFilter = moduleFilter,
            Sql = sqlOverrides,
            Cache = cache
        };

        var execution = await _verbExecutor
            .ExecuteAsync<BuildSsdtVerbResult>(context, BuildSsdtVerb.VerbName, options, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || execution.Payload is not { } payload)
        {
            return;
        }

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
