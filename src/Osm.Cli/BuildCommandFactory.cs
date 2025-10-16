using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Validation.Tightening;

namespace Osm.Cli;

internal static class BuildCommandFactory
{
    public static Command Create()
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
            var application = services.GetRequiredService<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>();

            var configPath = context.ParseResult.GetValueForOption(configOption);
            var cancellationToken = context.GetCancellationToken();

            var configurationResult = await configurationService.LoadAsync(configPath, cancellationToken).ConfigureAwait(false);
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
                CliCommandUtilities.CreateSqlOverrides(context.ParseResult, sqlOptions),
                cache);

            var result = await application.RunAsync(input, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                CliCommandUtilities.WriteErrors(context, result.Errors);
                context.ExitCode = 1;
                return;
            }

            var applicationResult = result.Value;
            var pipelineResult = applicationResult.PipelineResult;

            if (!string.IsNullOrWhiteSpace(applicationResult.ModelPath))
            {
                var modelMessage = applicationResult.ModelWasExtracted
                    ? $"Extracted model to {applicationResult.ModelPath}."
                    : $"Using model at {applicationResult.ModelPath}.";
                CliCommandUtilities.WriteLine(context.Console, modelMessage);
            }

            if (!applicationResult.ModelExtractionWarnings.IsDefaultOrEmpty
                && applicationResult.ModelExtractionWarnings.Length > 0)
            {
                CliCommandUtilities.EmitPipelineWarnings(context, applicationResult.ModelExtractionWarnings);
            }

            if (CliCommandUtilities.IsSqlProfiler(applicationResult.ProfilerProvider))
            {
                CliCommandUtilities.EmitSqlProfilerSnapshot(context, pipelineResult.Profile);
            }

            CliCommandUtilities.EmitPipelineLog(context, pipelineResult.ExecutionLog);
            CliCommandUtilities.EmitPipelineWarnings(context, pipelineResult.Warnings);

            foreach (var diagnostic in pipelineResult.DecisionReport.Diagnostics)
            {
                if (diagnostic.Severity == TighteningDiagnosticSeverity.Warning)
                {
                    CliCommandUtilities.WriteErrorLine(context.Console, $"[warning] {diagnostic.Message}");
                }
            }

            if (pipelineResult.EvidenceCache is { } cacheResult)
            {
                CliCommandUtilities.WriteLine(context.Console, $"Cached inputs to {cacheResult.CacheDirectory} (key {cacheResult.Manifest.Key}).");
            }

            if (!pipelineResult.StaticSeedScriptPaths.IsDefaultOrEmpty && pipelineResult.StaticSeedScriptPaths.Length > 0)
            {
                foreach (var seedPath in pipelineResult.StaticSeedScriptPaths)
                {
                    CliCommandUtilities.WriteLine(context.Console, $"Static entity seed script written to {seedPath}");
                }
            }

            CliCommandUtilities.WriteLine(context.Console, $"Emitted {pipelineResult.Manifest.Tables.Count} tables to {applicationResult.OutputDirectory}.");
            CliCommandUtilities.WriteLine(context.Console, $"Manifest written to {Path.Combine(applicationResult.OutputDirectory, "manifest.json")}");
            CliCommandUtilities.WriteLine(context.Console, $"Columns tightened: {pipelineResult.DecisionReport.TightenedColumnCount}/{pipelineResult.DecisionReport.ColumnCount}");
            CliCommandUtilities.WriteLine(context.Console, $"Unique indexes enforced: {pipelineResult.DecisionReport.UniqueIndexesEnforcedCount}/{pipelineResult.DecisionReport.UniqueIndexCount}");
            CliCommandUtilities.WriteLine(context.Console, $"Foreign keys created: {pipelineResult.DecisionReport.ForeignKeysCreatedCount}/{pipelineResult.DecisionReport.ForeignKeyCount}");

            foreach (var summary in PolicyDecisionSummaryFormatter.FormatForConsole(pipelineResult.DecisionReport))
            {
                CliCommandUtilities.WriteLine(context.Console, summary);
            }

            CliCommandUtilities.WriteLine(context.Console, $"Decision log written to {pipelineResult.DecisionLogPath}");

            if (context.ParseResult.GetValueForOption(openReportOption))
            {
                try
                {
                    var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, cancellationToken).ConfigureAwait(false);
                    CliCommandUtilities.WriteLine(context.Console, $"Report written to {reportPath}");
                    PipelineReportLauncher.TryOpen(reportPath, context.Console);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    CliCommandUtilities.WriteErrorLine(context.Console, $"[warning] Failed to open report: {ex.Message}");
                }
            }

            context.ExitCode = 0;
        });

        return command;
    }
}
