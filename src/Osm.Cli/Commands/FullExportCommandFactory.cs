using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Options;
using Osm.LoadHarness;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class FullExportCommandFactory : PipelineCommandFactory<FullExportVerbOptions, FullExportVerbResult>
{
    private readonly VerbOptionDeclaration<FullExportOverrides> _verbOptions;
    private readonly ILoadHarnessRunner _loadHarnessRunner;
    private readonly LoadHarnessReportWriter _loadHarnessReportWriter;

    public FullExportCommandFactory(
        IServiceScopeFactory scopeFactory,
        VerbOptionRegistry optionRegistry,
        ILoadHarnessRunner loadHarnessRunner,
        LoadHarnessReportWriter loadHarnessReportWriter)
        : base(scopeFactory)
    {
        if (optionRegistry is null)
        {
            throw new ArgumentNullException(nameof(optionRegistry));
        }

        _verbOptions = optionRegistry.FullExport;
        _loadHarnessRunner = loadHarnessRunner ?? throw new ArgumentNullException(nameof(loadHarnessRunner));
        _loadHarnessReportWriter = loadHarnessReportWriter ?? throw new ArgumentNullException(nameof(loadHarnessReportWriter));
    }

    protected override string VerbName => FullExportVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command(
            "full-export",
            "Extract the model, capture profiling, and emit SSDT artifacts in a single run.");
        _verbOptions.Configure(command);
        command.AddValidator(result =>
        {
            var uatBinder = _verbOptions.UatUsersBinder;
            if (uatBinder is null)
            {
                return;
            }

            if (!result.GetValueForOption(uatBinder.EnableOption))
            {
                return;
            }

            var connectionString = result.GetValueForOption(uatBinder.UatConnectionOption);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                result.ErrorMessage = "--uat-conn is required when --enable-uat-users is specified.";
                return;
            }

            var qaInventory = result.GetValueForOption(uatBinder.QaInventoryOption);
            if (string.IsNullOrWhiteSpace(qaInventory))
            {
                result.ErrorMessage = "--qa-user-inventory must be supplied when --enable-uat-users is specified.";
            }

            var uatInventory = result.GetValueForOption(uatBinder.UatInventoryOption);
            if (string.IsNullOrWhiteSpace(uatInventory))
            {
                result.ErrorMessage = "--uat-user-inventory must be supplied when --enable-uat-users is specified.";
            }
        });
        return command;
    }

    protected override FullExportVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var bound = _verbOptions.Bind(context.ParseResult);
        context.BindingContext.AddService(_ => bound);

        if (bound.ModuleFilter is null)
        {
            throw new InvalidOperationException("Module filter overrides missing.");
        }

        if (bound.Sql is null)
        {
            throw new InvalidOperationException("SQL overrides missing.");
        }

        return new FullExportVerbOptions
        {
            ConfigurationPath = bound.ConfigurationPath,
            Overrides = bound.Overrides,
            ModuleFilter = bound.ModuleFilter,
            Sql = bound.Sql,
            Cache = bound.Cache,
            Tightening = bound.Tightening
        };
    }

    protected override async Task<int> OnRunSucceededAsync(InvocationContext context, FullExportVerbResult payload)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var applicationResult = payload.ApplicationResult;
        var extractResult = applicationResult.Extraction;
        var profileResult = applicationResult.Profile;
        var buildResult = applicationResult.Build;
        var applyResult = applicationResult.Apply;

        var bound = context.BindingContext.GetService<VerbBoundOptions<FullExportOverrides>>();
        var openReport = bound?.GetExtension<OpenReportSettings>()?.OpenReport ?? false;
        var loadHarness = bound?.GetExtension<LoadHarnessCliOptions>() ?? LoadHarnessCliOptions.Disabled;
        var extractionOutput = extractResult.OutputPath ?? "model.extracted.json";
        var resolvedExtractionPath = Path.GetFullPath(extractionOutput);

        CommandConsole.WriteLine(context.Console, string.Empty);
        CommandConsole.WriteLine(context.Console, "Full export pipeline summary:");
        CommandConsole.WriteLine(context.Console, string.Empty);
        CommandConsole.WriteLine(context.Console, "Model extraction:");
        CommandConsole.EmitExtractModelSummary(context.Console, extractResult, resolvedExtractionPath);

        CommandConsole.WriteLine(context.Console, string.Empty);
        CommandConsole.WriteLine(context.Console, "Profiling:");
        CommandConsole.EmitProfileSummary(context.Console, profileResult);

        await CommandConsole.EmitBuildSsdtRunAsync(
                context.Console,
                buildResult,
                buildResult.PipelineResult,
                openReport,
                context.GetCancellationToken())
            .ConfigureAwait(false);

        CommandConsole.WriteLine(context.Console, string.Empty);
        CommandConsole.WriteLine(context.Console, "Schema apply:");
        CommandConsole.EmitSchemaApplySummary(context.Console, applyResult);

        if (loadHarness.RunHarness)
        {
            await RunLoadHarnessAsync(context, payload, loadHarness, cancellationToken: context.GetCancellationToken())
                .ConfigureAwait(false);
        }

        return 0;
    }

    private async Task RunLoadHarnessAsync(
        InvocationContext context,
        FullExportVerbResult payload,
        LoadHarnessCliOptions cliOptions,
        CancellationToken cancellationToken)
    {
        var applyOptions = payload.ApplicationResult.ApplyOptions;
        var cliConnectionString = cliOptions.ConnectionString;
        var hasCliConnection = !string.IsNullOrWhiteSpace(cliConnectionString);
        var connectionString = hasCliConnection
            ? cliConnectionString
            : applyOptions.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (!hasCliConnection && string.IsNullOrWhiteSpace(applyOptions.ConnectionString))
            {
                CommandConsole.WriteErrorLine(
                    context.Console,
                    "[warning] Load harness skipped (no connection string provided via CLI or configuration).");
            }
            return;
        }

        var pipeline = payload.ApplicationResult.Build.PipelineResult;
        var safeScriptPath = ResolveScriptPath(pipeline.SafeScriptPath);
        var remediationScriptPath = ResolveScriptPath(pipeline.RemediationScriptPath);
        var staticSeedPaths = ResolveScriptPaths(pipeline.StaticSeedScriptPaths);
        var dynamicInsertPaths = ResolveScriptPaths(pipeline.DynamicInsertScriptPaths);

        if (safeScriptPath is null
            && remediationScriptPath is null
            && staticSeedPaths.IsDefaultOrEmpty
            && dynamicInsertPaths.IsDefaultOrEmpty)
        {
            CommandConsole.WriteErrorLine(context.Console, "[warning] Load harness skipped (no scripts were generated).");
            return;
        }

        var reportOutput = cliOptions.ReportOutputPath;
        var commandTimeout = cliOptions.CommandTimeoutSeconds ?? applyOptions.CommandTimeoutSeconds;

        var options = LoadHarnessOptions.Create(
            connectionString,
            safeScriptPath,
            remediationScriptPath,
            staticSeedPaths,
            dynamicInsertPaths,
            reportOutput,
            commandTimeout);

        var report = await _loadHarnessRunner.RunAsync(options, cancellationToken).ConfigureAwait(false);
        var reportPath = Path.GetFullPath(options.ReportOutputPath);

        await _loadHarnessReportWriter
            .WriteAsync(report, reportPath, cancellationToken)
            .ConfigureAwait(false);

        CommandConsole.WriteLine(context.Console, string.Empty);
        CommandConsole.WriteLine(context.Console, "Load harness replay:");
        CommandConsole.WriteLine(context.Console, $"  Scripts replayed: {report.Scripts.Length}");
        CommandConsole.WriteLine(context.Console, $"  Total duration: {report.TotalDuration:g}");
        CommandConsole.WriteLine(context.Console, $"  Report: {reportPath}");

        foreach (var script in report.Scripts)
        {
            CommandConsole.WriteLine(context.Console, string.Empty);
            CommandConsole.WriteLine(context.Console, $"  {script.Category}: {script.ScriptPath}");
            CommandConsole.WriteLine(context.Console, $"    Duration: {script.Duration:g} ({script.BatchCount} batches)");

            if (!script.WaitStats.IsDefaultOrEmpty)
            {
                CommandConsole.WriteLine(context.Console, "    Wait stats delta (ms):");
                foreach (var wait in script.WaitStats)
                {
                    CommandConsole.WriteLine(context.Console, $"      {wait.WaitType}: {wait.DeltaMilliseconds:N0}");
                }
            }

            if (!script.LockSummary.IsDefaultOrEmpty)
            {
                CommandConsole.WriteLine(context.Console, "    Lock summary:");
                foreach (var entry in script.LockSummary)
                {
                    CommandConsole.WriteLine(context.Console, $"      {entry.ResourceType} ({entry.RequestMode}): {entry.Count}");
                }
            }

            if (!script.IndexFragmentation.IsDefaultOrEmpty)
            {
                CommandConsole.WriteLine(context.Console, "    Fragmented indexes:");
                foreach (var index in script.IndexFragmentation)
                {
                    CommandConsole.WriteLine(
                        context.Console,
                        $"      {index.SchemaName}.{index.ObjectName}.{index.IndexName}: {index.AverageFragmentationPercent:F1}% ({index.PageCount:N0} pages)");
                }
            }

            if (!script.Warnings.IsDefaultOrEmpty)
            {
                foreach (var warning in script.Warnings)
                {
                    CommandConsole.WriteErrorLine(context.Console, $"[warning] {warning}");
                }
            }
        }
    }

    private static string? ResolveScriptPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static ImmutableArray<string> ResolveScriptPaths(ImmutableArray<string> paths)
    {
        if (paths.IsDefaultOrEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(paths.Length);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            builder.Add(Path.GetFullPath(path));
        }

        return builder.ToImmutable();
    }

}
