using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Hosting;
using Osm.Pipeline.Hosting.Verbs;

namespace Osm.Cli.Commands;

internal sealed class BuildSsdtCommandFactory : ICommandFactory
{
    private readonly IPipelineVerb<BuildSsdtVerbOptions> _verb;
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
        IPipelineVerb<BuildSsdtVerbOptions> verb,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        CacheOptionBinder cacheOptionBinder,
        SqlOptionBinder sqlOptionBinder)
    {
        _verb = verb ?? throw new ArgumentNullException(nameof(verb));
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

        var verbOptions = new BuildSsdtVerbOptions(
            context.ParseResult.GetValueForOption(_globalOptions.ConfigPath),
            overrides,
            moduleFilter,
            sqlOverrides,
            cache,
            context.ParseResult.GetValueForOption(_openReportOption));

        var result = await _verb.RunAsync(verbOptions, cancellationToken).ConfigureAwait(false);
        context.ExitCode = result.ExitCode;
    }
}
