using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal sealed class AnalyzeCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliGlobalOptions _globalOptions;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot.");
    private readonly Option<string?> _outputOption = new("--out", () => "out", "Output directory for tightening analysis outputs.");

    public AnalyzeCommandFactory(IServiceScopeFactory scopeFactory, CliGlobalOptions globalOptions)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));

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

        var options = new AnalyzeVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides
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

        CommandConsole.EmitPipelineWarnings(context.Console, pipelineResult.Warnings);
        CommandConsole.EmitPipelineLog(context.Console, pipelineResult.ExecutionLog);

        foreach (var diagnostic in report.Diagnostics)
        {
            if (diagnostic.Severity == TighteningDiagnosticSeverity.Warning)
            {
                CommandConsole.WriteErrorLine(context.Console, $"[warning] {diagnostic.Message}");
            }
        }

        CommandConsole.WriteLine(context.Console, $"Columns tightened: {report.TightenedColumnCount}/{report.ColumnCount}");
        CommandConsole.WriteLine(context.Console, $"Unique indexes enforced: {report.UniqueIndexesEnforcedCount}/{report.UniqueIndexCount}");
        CommandConsole.WriteLine(context.Console, $"Foreign keys created: {report.ForeignKeysCreatedCount}/{report.ForeignKeyCount}");

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
