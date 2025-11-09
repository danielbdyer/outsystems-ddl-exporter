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

internal sealed class BuildSsdtCommandFactory : PipelineCommandFactory<BuildSsdtVerbOptions, BuildSsdtVerbResult>
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheOptionBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;
    private readonly TighteningOptionBinder _tighteningBinder;

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
        SqlOptionBinder sqlOptionBinder,
        TighteningOptionBinder tighteningOptionBinder)
        : base(scopeFactory)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _moduleFilterBinder = moduleFilterBinder ?? throw new ArgumentNullException(nameof(moduleFilterBinder));
        _cacheOptionBinder = cacheOptionBinder ?? throw new ArgumentNullException(nameof(cacheOptionBinder));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
        _tighteningBinder = tighteningOptionBinder ?? throw new ArgumentNullException(nameof(tighteningOptionBinder));
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
        CommandOptionBuilder.AddTighteningOptions(command, _tighteningBinder);
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
        var tightening = _tighteningBinder.Bind(parseResult);

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
            Cache = cache,
            Tightening = tightening
        };
    }

    protected override async Task<int> OnRunSucceededAsync(InvocationContext context, BuildSsdtVerbResult payload)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var openReport = context.ParseResult.GetValueForOption(_openReportOption);
        await CommandConsole.EmitBuildSsdtRunAsync(
                context.Console,
                payload.ApplicationResult,
                payload.ApplicationResult.PipelineResult,
                openReport,
                context.GetCancellationToken())
            .ConfigureAwait(false);
        return 0;
    }
}
