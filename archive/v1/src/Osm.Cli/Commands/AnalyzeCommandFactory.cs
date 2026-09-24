using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
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

internal sealed class AnalyzeCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliGlobalOptions _globalOptions;
    private readonly TighteningOptionBinder _tighteningBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot.");
    private readonly Option<string?> _outputOption = new("--out", () => "out", "Output directory for tightening analysis outputs.");

    public AnalyzeCommandFactory(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        TighteningOptionBinder tighteningBinder)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _tighteningBinder = tighteningBinder ?? throw new ArgumentNullException(nameof(tighteningBinder));

        _modelOption.AddAlias("--in");
    }

    public Command Create()
    {
        var command = new Command("analyze", "Run the tightening analyzer against a model and profile snapshot.")
        {
            _modelOption,
            _profileOption,
            _outputOption
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddTighteningOptions(command, _tighteningBinder);
        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IVerbRegistry>();
        var verb = registry.Get(AnalyzeVerb.VerbName);

        var parseResult = context.ParseResult;
        var overrides = new AnalyzeOverrides(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_outputOption));

        var tightening = _tighteningBinder.Bind(parseResult);

        var options = new AnalyzeVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            Tightening = tightening
        };

        var run = await verb.RunAsync(options, context.GetCancellationToken()).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            CommandConsole.WriteErrors(context.Console, run.Errors);
            context.ExitCode = 1;
            return;
        }

        if (run.Payload is not AnalyzeVerbResult payload)
        {
            CommandConsole.WriteErrorLine(context.Console, "[error] Unexpected result type for analyze verb.");
            context.ExitCode = 1;
            return;
        }

        await EmitResultsAsync(context, payload).ConfigureAwait(false);
        context.ExitCode = 0;
    }

    private Task EmitResultsAsync(InvocationContext context, AnalyzeVerbResult payload)
    {
        var application = payload.ApplicationResult;
        var pipelineResult = application.PipelineResult;
        var report = pipelineResult.Report;

        CommandConsole.WriteLine(context.Console, $"Model: {application.ModelPath}");
        CommandConsole.WriteLine(context.Console, $"Profile: {application.ProfilePath}");
        CommandConsole.WriteLine(context.Console, $"Output directory: {application.OutputDirectory}");

        var pipelineWarnings = pipelineResult.Warnings
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .ToImmutableArray();

        if (pipelineWarnings.Length > 0 && pipelineResult.ExecutionLog.Entries.Count > 0)
        {
            CommandConsole.EmitPipelineLog(context.Console, pipelineResult.ExecutionLog);
        }

        if (pipelineWarnings.Length > 0)
        {
            CommandConsole.EmitPipelineWarnings(context.Console, pipelineWarnings);
        }

        foreach (var diagnostic in report.Diagnostics)
        {
            if (diagnostic.Severity == TighteningDiagnosticSeverity.Warning)
            {
                CommandConsole.WriteErrorLine(context.Console, $"[warning] {diagnostic.Message}");
            }
        }

        CommandConsole.EmitNamingOverrideTemplate(context.Console, report.Diagnostics);

        CommandConsole.WriteLine(context.Console, $"Columns confirmed NOT NULL: {report.TightenedColumnCount}/{report.ColumnCount}");
        CommandConsole.WriteLine(context.Console, $"Unique indexes confirmed UNIQUE: {report.UniqueIndexesEnforcedCount}/{report.UniqueIndexCount}");
        CommandConsole.WriteLine(context.Console, $"Foreign keys safe to create: {report.ForeignKeysCreatedCount}/{report.ForeignKeyCount}");

        CommandConsole.EmitTighteningStatisticsDetails(context.Console, report);

        CommandConsole.EmitModuleRollups(context.Console, ImmutableDictionary<string, ModuleManifestRollup>.Empty, report.ModuleRollups);
        CommandConsole.EmitTogglePrecedence(context.Console, report.TogglePrecedence);

        foreach (var summary in pipelineResult.SummaryLines)
        {
            if (!string.IsNullOrWhiteSpace(summary))
            {
                CommandConsole.WriteLine(context.Console, summary);
            }
        }

        CommandConsole.WriteLine(context.Console, $"Summary written to {pipelineResult.SummaryPath}");
        CommandConsole.WriteLine(context.Console, $"Decision log written to {pipelineResult.DecisionLogPath}");

        return Task.CompletedTask;
    }
}
