using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class ProfileCommandFactory : PipelineCommandFactory<ProfileVerbOptions, ProfileVerbResult>
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling fixture when using the fixture provider.");
    private readonly Option<string?> _profilerProviderOption = new("--profiler-provider", "Profiler provider to use (fixture or sql).");
    private readonly Option<string?> _outputOption = new("--out", () => "profiles", "Directory to write profiling artifacts.");
    private readonly Option<string?> _sqlMetadataOption = new("--sql-metadata-out", "Path to write SQL metadata diagnostics (JSON).");

    public ProfileCommandFactory(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        SqlOptionBinder sqlOptionBinder)
        : base(scopeFactory)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _moduleFilterBinder = moduleFilterBinder ?? throw new ArgumentNullException(nameof(moduleFilterBinder));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
    }

    protected override string VerbName => ProfileVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command("profile", "Capture and persist a profiling snapshot.")
        {
            _modelOption,
            _profileOption,
            _profilerProviderOption,
            _outputOption,
            _sqlMetadataOption
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddModuleFilterOptions(command, _moduleFilterBinder);
        CommandOptionBuilder.AddSqlOptions(command, _sqlOptionBinder);
        return command;
    }

    protected override ProfileVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var parseResult = context.ParseResult;
        var moduleFilter = _moduleFilterBinder.Bind(parseResult);
        var sqlOverrides = _sqlOptionBinder.Bind(parseResult);

        var overrides = new CaptureProfileOverrides(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_outputOption),
            parseResult.GetValueForOption(_profilerProviderOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_sqlMetadataOption));

        return new ProfileVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            ModuleFilter = moduleFilter,
            Sql = sqlOverrides
        };
    }

    protected override Task<int> OnRunSucceededAsync(InvocationContext context, ProfileVerbResult payload)
    {
        var applicationResult = payload.ApplicationResult;
        var pipelineResult = applicationResult.PipelineResult;

        if (!string.IsNullOrWhiteSpace(applicationResult.ModelPath))
        {
            CommandConsole.WriteLine(context.Console, $"Using model at {applicationResult.ModelPath}.");
        }

        CommandConsole.EmitPipelineLog(context.Console, pipelineResult.ExecutionLog);
        CommandConsole.EmitPipelineWarnings(context.Console, pipelineResult.Warnings);
        CommandConsole.EmitProfilingInsights(context.Console, pipelineResult.Insights);
        CommandConsole.EmitProfilingTelemetry(context.Console, pipelineResult.Telemetry.Summary);

        if (string.Equals(applicationResult.ProfilerProvider, "sql", StringComparison.OrdinalIgnoreCase))
        {
            CommandConsole.EmitSqlProfilerSnapshot(context.Console, pipelineResult.Profile);
        }

        CommandConsole.WriteLine(context.Console, $"Profile written to {pipelineResult.ProfilePath}");
        CommandConsole.WriteLine(context.Console, $"Manifest written to {pipelineResult.ManifestPath}");

        return Task.FromResult(0);
    }
}
