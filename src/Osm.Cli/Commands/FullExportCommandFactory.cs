using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class FullExportCommandFactory : PipelineCommandFactory<FullExportVerbOptions, FullExportVerbResult>
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheOptionBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;
    private readonly TighteningOptionBinder _tighteningBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to an existing model JSON file to reuse.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot or fixture input.");
    private readonly Option<string?> _profilerProviderOption = new("--profiler-provider", "Profiler provider to use (fixture or sql).");
    private readonly Option<string?> _staticDataOption = new("--static-data", "Path to static data fixture.");
    private readonly Option<string?> _buildOutputOption = new("--build-out", () => "out", "Output directory for SSDT artifacts.");
    private readonly Option<string?> _renameOption = new("--rename-table", "Rename tables using source=Override syntax.");
    private readonly Option<bool> _openReportOption = new("--open-report", "Generate and open an HTML report for this run.");
    private readonly Option<string?> _buildSqlMetadataOption = new("--build-sql-metadata-out", "Path to write SQL metadata diagnostics for SSDT emission (JSON).");

    private readonly Option<string?> _profileOutputOption = new("--profile-out", () => "profiles", "Directory to write profiling artifacts.");
    private readonly Option<string?> _profileSqlMetadataOption = new("--profile-sql-metadata-out", "Path to write profiling SQL metadata diagnostics (JSON).");

    private readonly Option<string?> _extractOutputOption = new("--extract-out", () => "model.extracted.json", "Output path for extracted model JSON.");
    private readonly Option<bool> _onlyActiveAttributesOption = new("--only-active-attributes", "Extract only active attributes.");
    private readonly Option<bool> _includeInactiveAttributesOption = new("--include-inactive-attributes", "Include inactive attributes when extracting.");
    private readonly Option<string?> _mockSqlOption = new("--mock-advanced-sql", "Path to advanced SQL manifest fixture.");
    private readonly Option<string?> _extractSqlMetadataOption = new("--extract-sql-metadata-out", "Path to write extraction SQL metadata diagnostics (JSON).");

    public FullExportCommandFactory(
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

    protected override string VerbName => FullExportVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command(
            "full-export",
            "Extract the model, capture profiling, and emit SSDT artifacts in a single run.")
        {
            _modelOption,
            _profileOption,
            _profilerProviderOption,
            _staticDataOption,
            _buildOutputOption,
            _renameOption,
            _openReportOption,
            _buildSqlMetadataOption,
            _profileOutputOption,
            _profileSqlMetadataOption,
            _extractOutputOption,
            _onlyActiveAttributesOption,
            _includeInactiveAttributesOption,
            _mockSqlOption,
            _extractSqlMetadataOption,
            _globalOptions.MaxDegreeOfParallelism
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddModuleFilterOptions(command, _moduleFilterBinder);
        CommandOptionBuilder.AddCacheOptions(command, _cacheOptionBinder);
        CommandOptionBuilder.AddSqlOptions(command, _sqlOptionBinder);
        CommandOptionBuilder.AddTighteningOptions(command, _tighteningBinder);
        return command;
    }

    protected override FullExportVerbOptions BindOptions(InvocationContext context)
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

        var buildOverrides = new BuildSsdtOverrides(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_buildOutputOption),
            parseResult.GetValueForOption(_profilerProviderOption),
            parseResult.GetValueForOption(_staticDataOption),
            parseResult.GetValueForOption(_renameOption),
            parseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism),
            parseResult.GetValueForOption(_buildSqlMetadataOption));

        var profileOverrides = new CaptureProfileOverrides(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_profileOutputOption),
            parseResult.GetValueForOption(_profilerProviderOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_profileSqlMetadataOption));

        IReadOnlyList<string>? extractModules = moduleFilter.Modules.Count > 0 ? moduleFilter.Modules : null;
        var onlyActive = ResolveOnlyActiveOverride(parseResult, moduleFilter);

        var extractOverrides = new ExtractModelOverrides(
            extractModules,
            moduleFilter.IncludeSystemModules,
            onlyActive,
            parseResult.GetValueForOption(_extractOutputOption),
            parseResult.GetValueForOption(_mockSqlOption),
            parseResult.GetValueForOption(_extractSqlMetadataOption));

        var hasModelOverride = parseResult.HasOption(_modelOption);
        var hasProfileOverride = parseResult.HasOption(_profileOption);
        var hasSqlConnection = !string.IsNullOrWhiteSpace(sqlOverrides.ConnectionString);
        var hasExtractionFixture = !string.IsNullOrWhiteSpace(extractOverrides.MockAdvancedSqlManifest);
        var hasProfilingConnections = sqlOverrides.ProfilingConnectionStrings is { Count: > 0 };
        var usesFixtureProfiler = !string.IsNullOrWhiteSpace(profileOverrides.ProfilerProvider)
            && string.Equals(profileOverrides.ProfilerProvider, "fixture", StringComparison.OrdinalIgnoreCase);
        var hasFixtureProfileInput = usesFixtureProfiler && !string.IsNullOrWhiteSpace(profileOverrides.ProfilePath);

        var skipExtraction = hasModelOverride && !hasSqlConnection && !hasExtractionFixture;
        var skipProfiling = hasProfileOverride && !hasSqlConnection && !hasProfilingConnections && !hasFixtureProfileInput;

        return new FullExportVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = new FullExportOverrides(buildOverrides, profileOverrides, extractOverrides, skipExtraction, skipProfiling),
            ModuleFilter = moduleFilter,
            Sql = sqlOverrides,
            Cache = cache,
            Tightening = tightening
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

        var extractionOutput = extractResult.OutputPath ?? "model.extracted.json";
        var resolvedExtractionPath = Path.GetFullPath(extractionOutput);
        var openReport = context.ParseResult.GetValueForOption(_openReportOption);

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

        return 0;
    }

    private bool? ResolveOnlyActiveOverride(ParseResult parseResult, ModuleFilterOverrides moduleFilter)
    {
        if (parseResult.HasOption(_onlyActiveAttributesOption))
        {
            return true;
        }

        if (parseResult.HasOption(_includeInactiveAttributesOption))
        {
            return false;
        }

        if (moduleFilter.IncludeInactiveModules.HasValue)
        {
            return !moduleFilter.IncludeInactiveModules.Value;
        }

        return null;
    }
}
