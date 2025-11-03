using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal sealed class BuildSsdtCommandFactory : PipelineCommandFactory<BuildSsdtVerbOptions, BuildSsdtVerbResult>
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
    private readonly Option<string?> _sqlMetadataOption = new("--sql-metadata-out", "Path to write SQL metadata diagnostics (JSON).");
    private readonly Option<bool> _extractModelOption = new("--extract-model", "Run extract-model before emission and use the inline payload.");

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

    protected override string VerbName => BuildSsdtVerb.VerbName;

    protected override Command CreateCommandCore()
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
            _globalOptions.MaxDegreeOfParallelism,
            _sqlMetadataOption,
            _extractModelOption
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddModuleFilterOptions(command, _moduleFilterBinder);
        CommandOptionBuilder.AddCacheOptions(command, _cacheOptionBinder);
        CommandOptionBuilder.AddSqlOptions(command, _sqlOptionBinder);
        return command;
    }

    protected override BuildSsdtVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

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
            parseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism),
            parseResult.GetValueForOption(_sqlMetadataOption),
            parseResult.GetValueForOption(_extractModelOption));

        return new BuildSsdtVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            ModuleFilter = moduleFilter,
            Sql = sqlOverrides,
            Cache = cache
        };
    }

    protected override async Task<int> OnRunSucceededAsync(InvocationContext context, BuildSsdtVerbResult payload)
    {
        await EmitResultsAsync(context, payload).ConfigureAwait(false);
        return 0;
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
        CommandConsole.EmitProfilingInsights(context.Console, pipelineResult.ProfilingInsights);

        foreach (var diagnostic in pipelineResult.DecisionReport.Diagnostics)
        {
            if (diagnostic.Severity == TighteningDiagnosticSeverity.Warning)
            {
                CommandConsole.WriteErrorLine(context.Console, $"[warning] {diagnostic.Message}");
            }
        }

        CommandConsole.EmitNamingOverrideTemplate(context.Console, pipelineResult.DecisionReport.Diagnostics);

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

        if (!pipelineResult.TelemetryPackagePaths.IsDefaultOrEmpty && pipelineResult.TelemetryPackagePaths.Length > 0)
        {
            foreach (var packagePath in pipelineResult.TelemetryPackagePaths)
            {
                CommandConsole.WriteLine(context.Console, $"Telemetry package written to {packagePath}");
            }
        }

        CommandConsole.WriteLine(context.Console, $"Emitted {pipelineResult.Manifest.Tables.Count} tables to {applicationResult.OutputDirectory}.");
        CommandConsole.WriteLine(context.Console, $"Manifest written to {Path.Combine(applicationResult.OutputDirectory, "manifest.json")}");
        CommandConsole.WriteLine(context.Console, $"Columns tightened: {pipelineResult.DecisionReport.TightenedColumnCount}/{pipelineResult.DecisionReport.ColumnCount}");
        CommandConsole.WriteLine(context.Console, $"Unique indexes enforced: {pipelineResult.DecisionReport.UniqueIndexesEnforcedCount}/{pipelineResult.DecisionReport.UniqueIndexCount}");
        CommandConsole.WriteLine(context.Console, $"Foreign keys created: {pipelineResult.DecisionReport.ForeignKeysCreatedCount}/{pipelineResult.DecisionReport.ForeignKeyCount}");

        EmitSqlValidationSummary(context, pipelineResult);

        CommandConsole.EmitModuleRollups(
            context.Console,
            pipelineResult.ModuleManifestRollups,
            pipelineResult.DecisionReport.ModuleRollups);

        CommandConsole.EmitTogglePrecedence(context.Console, pipelineResult.DecisionReport.TogglePrecedence);

        foreach (var summary in PolicyDecisionSummaryFormatter.FormatForConsole(pipelineResult.DecisionReport))
        {
            CommandConsole.WriteLine(context.Console, summary);
        }

        CommandConsole.EmitContradictionDetails(context.Console, pipelineResult.Opportunities);

        CommandConsole.WriteLine(context.Console, string.Empty);
        CommandConsole.WriteLine(context.Console, "Tightening Artifacts:");
        CommandConsole.WriteLine(context.Console, $"  Decision log: {pipelineResult.DecisionLogPath}");
        CommandConsole.WriteLine(context.Console, $"  Opportunities report: {pipelineResult.OpportunitiesPath}");

        if (pipelineResult.Opportunities.ContradictionCount > 0)
        {
            CommandConsole.WriteLine(context.Console, $"  ⚠️  Needs remediation ({pipelineResult.Opportunities.ContradictionCount} contradictions): {pipelineResult.RemediationScriptPath}");
        }
        else
        {
            CommandConsole.WriteLine(context.Console, $"  Needs remediation: {pipelineResult.RemediationScriptPath}");
        }

        CommandConsole.WriteLine(context.Console, $"  Safe to apply ({pipelineResult.Opportunities.RecommendationCount + pipelineResult.Opportunities.ValidationCount} opportunities): {pipelineResult.SafeScriptPath}");

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

    private static void EmitSqlValidationSummary(InvocationContext context, BuildSsdtPipelineResult pipelineResult)
    {
        var summary = pipelineResult.SqlValidation ?? SsdtSqlValidationSummary.Empty;
        CommandConsole.WriteLine(
            context.Console,
            $"SQL validation: validated {summary.TotalFiles} file(s); {summary.FilesWithErrors} with errors; {summary.ErrorCount} error(s).");

        if (summary.ErrorCount <= 0 || summary.Issues.IsDefaultOrEmpty || summary.Issues.Length == 0)
        {
            return;
        }

        const int MaxSamples = 5;
        CommandConsole.WriteErrorLine(context.Console, "SQL validation errors (sample):");

        var samples = summary.Issues
            .Where(static issue => issue is not null)
            .SelectMany(issue => issue.Errors.Select(error => (issue.Path, error)))
            .Take(MaxSamples)
            .ToArray();

        foreach (var sample in samples)
        {
            var error = sample.error;
            CommandConsole.WriteErrorLine(
                context.Console,
                $"  {sample.Path}:{error.Line}:{error.Column} (#{error.Number}, severity {error.Severity}) {error.Message}");
        }

        var remaining = summary.ErrorCount - samples.Length;
        if (remaining > 0)
        {
            CommandConsole.WriteErrorLine(context.Console, $"  ... {remaining} additional error(s) omitted.");
        }
    }

    private static bool IsSqlProfiler(string provider)
        => string.Equals(provider, "sql", StringComparison.OrdinalIgnoreCase);
}
