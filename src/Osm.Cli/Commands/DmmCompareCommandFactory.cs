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

internal sealed class DmmCompareCommandFactory : ICommandFactory
{
    private readonly IPipelineVerb<CompareWithDmmVerbOptions> _verb;
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheOptionBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot.");
    private readonly Option<string?> _dmmOption = new("--dmm", "Path to the baseline DMM script.");
    private readonly Option<string?> _outputOption = new("--out", () => "out", "Output directory for comparison artifacts.");

    public DmmCompareCommandFactory(
        IPipelineVerb<CompareWithDmmVerbOptions> verb,
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
        var command = new Command("dmm-compare", "Compare the emitted SSDT artifacts with a DMM baseline.")
        {
            _modelOption,
            _profileOption,
            _dmmOption,
            _outputOption,
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

        var overrides = new CompareWithDmmOverrides(
            context.ParseResult.GetValueForOption(_modelOption),
            context.ParseResult.GetValueForOption(_profileOption),
            context.ParseResult.GetValueForOption(_dmmOption),
            context.ParseResult.GetValueForOption(_outputOption),
            context.ParseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism));

        var verbOptions = new CompareWithDmmVerbOptions(
            context.ParseResult.GetValueForOption(_globalOptions.ConfigPath),
            overrides,
            moduleFilter,
            sqlOverrides,
            cache);

        var result = await _verb.RunAsync(verbOptions, cancellationToken).ConfigureAwait(false);
        context.ExitCode = result.ExitCode;
    }

}
