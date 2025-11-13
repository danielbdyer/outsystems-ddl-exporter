using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.CommandLine.Parsing;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands.Options;

internal sealed class VerbOptionRegistry
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheBinder;
    private readonly SqlOptionBinder _sqlBinder;
    private readonly TighteningOptionBinder _tighteningBinder;
    private readonly SchemaApplyOptionBinder _schemaApplyBinder;
    private readonly UatUsersOptionBinder _uatUsersBinder;
    private readonly IReadOnlyList<IVerbOptionExtension> _extensions;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot or fixture input.");
    private readonly Option<string?> _profilerProviderOption = new("--profiler-provider", "Profiler provider to use (fixture or sql).");
    private readonly Option<string?> _staticDataOption = new("--static-data", "Path to static data fixture.");
    private readonly Option<string?> _renameTableOption = new("--rename-table", "Rename tables using source=Override syntax.");
    private readonly Option<DynamicInsertOutputMode> _dynamicInsertModeOption = new(
        "--dynamic-insert-mode",
        () => DynamicInsertOutputMode.PerEntity,
        "Dynamic insert emission mode: per-entity files (default) or single-file.");
    private readonly Option<bool> _extractModelInlineOption = new("--extract-model", "Run extract-model before emission and use the inline payload.");
    private readonly Option<string?> _buildOutputOption = new("--out", () => "out", "Output directory for SSDT artifacts.");
    private readonly Option<string?> _buildSqlMetadataOption = new("--sql-metadata-out", "Path to write SQL metadata diagnostics (JSON).");
    private readonly Option<string?> _profileOutputOption = new("--out", () => "profiles", "Directory to write profiling artifacts.");
    private readonly Option<string?> _profileSqlMetadataOption = new("--sql-metadata-out", "Path to write SQL metadata diagnostics (JSON).");
    private readonly Option<string?> _fullExportBuildOutput = new("--build-out", () => "out", "Output directory for SSDT artifacts.");
    private readonly Option<string?> _fullExportBuildSqlMetadataOption = new("--build-sql-metadata-out", "Path to write SQL metadata diagnostics for SSDT emission (JSON).");
    private readonly Option<string?> _fullExportProfileOutOption = new("--profile-out", () => "profiles", "Directory to write profiling artifacts.");
    private readonly Option<string?> _fullExportProfileSqlMetadata = new("--profile-sql-metadata-out", "Path to write profiling SQL metadata diagnostics (JSON).");
    private readonly Option<string?> _fullExportExtractOutOption = new("--extract-out", () => "model.extracted.json", "Output path for extracted model JSON.");
    private readonly Option<string?> _extractOutputOption = new("--out", () => "model.extracted.json", "Output path for extracted model JSON.");
    private readonly Option<bool> _onlyActiveAttributesOption = new("--only-active-attributes", "Extract only active attributes.");
    private readonly Option<bool> _includeInactiveAttributesOption = new("--include-inactive-attributes", "Include inactive attributes when extracting.");
    private readonly Option<string?> _mockAdvancedSqlOption = new("--mock-advanced-sql", "Path to advanced SQL manifest fixture.");
    private readonly Option<string?> _extractSqlMetadataOption = new("--extract-sql-metadata-out", "Path to write extraction SQL metadata diagnostics (JSON).");
    private readonly Option<string?> _extractSqlMetadataStandardOption = new("--sql-metadata-out", "Path to write SQL metadata diagnostics (JSON).");
    private readonly Option<string?> _dmmOption = new("--dmm", "Path to the baseline DMM script.");
    private readonly Option<string?> _comparisonOutputOption = new("--out", () => "out", "Output directory for comparison artifacts.");

    public VerbOptionRegistry(
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        CacheOptionBinder cacheBinder,
        SqlOptionBinder sqlBinder,
        TighteningOptionBinder tighteningBinder,
        SchemaApplyOptionBinder schemaApplyBinder,
        UatUsersOptionBinder uatUsersBinder,
        IEnumerable<IVerbOptionExtension> extensions)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _moduleFilterBinder = moduleFilterBinder ?? throw new ArgumentNullException(nameof(moduleFilterBinder));
        _cacheBinder = cacheBinder ?? throw new ArgumentNullException(nameof(cacheBinder));
        _sqlBinder = sqlBinder ?? throw new ArgumentNullException(nameof(sqlBinder));
        _tighteningBinder = tighteningBinder ?? throw new ArgumentNullException(nameof(tighteningBinder));
        _schemaApplyBinder = schemaApplyBinder ?? throw new ArgumentNullException(nameof(schemaApplyBinder));
        _uatUsersBinder = uatUsersBinder ?? throw new ArgumentNullException(nameof(uatUsersBinder));
        _extensions = extensions?.ToArray() ?? Array.Empty<IVerbOptionExtension>();

        BuildSsdt = CreateBuildSsdtDeclaration();
        Profile = CreateProfileDeclaration();
        ExtractModel = CreateExtractModelDeclaration();
        FullExport = CreateFullExportDeclaration();
        DmmCompare = CreateDmmCompareDeclaration();
    }

    public VerbOptionDeclaration<BuildSsdtOverrides> BuildSsdt { get; }

    public VerbOptionDeclaration<CaptureProfileOverrides> Profile { get; }

    public VerbOptionDeclaration<ExtractModelOverrides> ExtractModel { get; }

    public VerbOptionDeclaration<FullExportOverrides> FullExport { get; }

    public VerbOptionDeclaration<CompareWithDmmOverrides> DmmCompare { get; }

    private VerbOptionDeclaration<BuildSsdtOverrides> CreateBuildSsdtDeclaration()
    {
        var builder = CreateBuilder<BuildSsdtOverrides>()
            .UseModuleFilter(_moduleFilterBinder)
            .UseCache(_cacheBinder)
            .UseSql(_sqlBinder)
            .UseTightening(_tighteningBinder)
            .AddOption(_modelOption)
            .AddOption(_profileOption)
            .AddOption(_profilerProviderOption)
            .AddOption(_staticDataOption)
            .AddOption(_buildOutputOption)
            .AddOption(_renameTableOption)
            .AddOption(_globalOptions.MaxDegreeOfParallelism)
            .AddOption(_buildSqlMetadataOption)
            .AddOption(_extractModelInlineOption)
            .AddOption(_dynamicInsertModeOption)
            .BindOverrides(context => CreateBuildOverrides(context));

        AttachExtensions(builder, BuildSsdtVerb.VerbName);
        return builder.Build(BuildSsdtVerb.VerbName);
    }

    private VerbOptionDeclaration<CaptureProfileOverrides> CreateProfileDeclaration()
    {
        var builder = CreateBuilder<CaptureProfileOverrides>()
            .UseModuleFilter(_moduleFilterBinder)
            .UseSql(_sqlBinder)
            .UseTightening(_tighteningBinder)
            .AddOption(_modelOption)
            .AddOption(_profileOption)
            .AddOption(_profilerProviderOption)
            .AddOption(_profileOutputOption)
            .AddOption(_profileSqlMetadataOption)
            .BindOverrides(context => new CaptureProfileOverrides(
                context.ParseResult.GetValueForOption(_modelOption),
                context.ParseResult.GetValueForOption(_profileOutputOption),
                context.ParseResult.GetValueForOption(_profilerProviderOption),
                context.ParseResult.GetValueForOption(_profileOption),
                context.ParseResult.GetValueForOption(_profileSqlMetadataOption)));

        AttachExtensions(builder, ProfileVerb.VerbName);
        return builder.Build(ProfileVerb.VerbName);
    }

    private VerbOptionDeclaration<ExtractModelOverrides> CreateExtractModelDeclaration()
    {
        var builder = CreateBuilder<ExtractModelOverrides>()
            .UseModuleFilter(_moduleFilterBinder)
            .UseSql(_sqlBinder)
            .AddOption(_onlyActiveAttributesOption)
            .AddOption(_includeInactiveAttributesOption)
            .AddOption(_extractOutputOption)
            .AddOption(_mockAdvancedSqlOption)
            .AddOption(_extractSqlMetadataStandardOption)
            .BindOverrides(context => CreateExtractOverrides(context, useModuleDefaults: false, _extractOutputOption, _extractSqlMetadataStandardOption));

        return builder.Build(ExtractModelVerb.VerbName);
    }

    private VerbOptionDeclaration<FullExportOverrides> CreateFullExportDeclaration()
    {
        var builder = CreateBuilder<FullExportOverrides>()
            .UseModuleFilter(_moduleFilterBinder)
            .UseCache(_cacheBinder)
            .UseSql(_sqlBinder)
            .UseTightening(_tighteningBinder)
            .UseSchemaApply(_schemaApplyBinder)
            .UseUatUsers(_uatUsersBinder)
            .AddOption(_modelOption)
            .AddOption(_profileOption)
            .AddOption(_profilerProviderOption)
            .AddOption(_staticDataOption)
            .AddOption(_fullExportBuildOutput)
            .AddOption(_renameTableOption)
            .AddOption(_globalOptions.MaxDegreeOfParallelism)
            .AddOption(_fullExportBuildSqlMetadataOption)
            .AddOption(_dynamicInsertModeOption)
            .AddOption(_fullExportProfileOutOption)
            .AddOption(_fullExportProfileSqlMetadata)
            .AddOption(_fullExportExtractOutOption)
            .AddOption(_onlyActiveAttributesOption)
            .AddOption(_includeInactiveAttributesOption)
            .AddOption(_mockAdvancedSqlOption)
            .AddOption(_extractSqlMetadataOption)
            .BindOverrides(context => CreateFullExportOverrides(context));

        AttachExtensions(builder, FullExportVerb.VerbName);
        return builder.Build(FullExportVerb.VerbName);
    }

    private VerbOptionDeclaration<CompareWithDmmOverrides> CreateDmmCompareDeclaration()
    {
        var builder = CreateBuilder<CompareWithDmmOverrides>()
            .UseModuleFilter(_moduleFilterBinder)
            .UseCache(_cacheBinder)
            .UseSql(_sqlBinder)
            .UseTightening(_tighteningBinder)
            .AddOption(_modelOption)
            .AddOption(_profileOption)
            .AddOption(_dmmOption)
            .AddOption(_comparisonOutputOption)
            .AddOption(_globalOptions.MaxDegreeOfParallelism)
            .BindOverrides(context => new CompareWithDmmOverrides(
                context.ParseResult.GetValueForOption(_modelOption),
                context.ParseResult.GetValueForOption(_profileOption),
                context.ParseResult.GetValueForOption(_dmmOption),
                context.ParseResult.GetValueForOption(_comparisonOutputOption),
                context.ParseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism)));

        AttachExtensions(builder, DmmCompareVerb.VerbName);
        return builder.Build(DmmCompareVerb.VerbName);
    }

    private VerbOptionsBuilder<TOverrides> CreateBuilder<TOverrides>()
        => new(_globalOptions);

    private void AttachExtensions<TOverrides>(VerbOptionsBuilder<TOverrides> builder, string verbName)
    {
        foreach (var extension in _extensions.Where(ext => string.Equals(ext.VerbName, verbName, StringComparison.Ordinal)))
        {
            builder.AddExtension(extension);
        }
    }

    private BuildSsdtOverrides CreateBuildOverrides(VerbOverrideBindingContext context)
    {
        var parseResult = context.ParseResult;
        DynamicInsertOutputMode? dynamicInsertMode = null;
        if (parseResult.HasOption(_dynamicInsertModeOption))
        {
            dynamicInsertMode = parseResult.GetValueForOption(_dynamicInsertModeOption);
        }

        return new BuildSsdtOverrides(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_buildOutputOption),
            parseResult.GetValueForOption(_profilerProviderOption),
            parseResult.GetValueForOption(_staticDataOption),
            parseResult.GetValueForOption(_renameTableOption),
            parseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism),
            parseResult.GetValueForOption(_buildSqlMetadataOption),
            parseResult.GetValueForOption(_extractModelInlineOption),
            dynamicInsertMode);
    }

    private ExtractModelOverrides CreateExtractOverrides(
        VerbOverrideBindingContext context,
        bool useModuleDefaults,
        Option<string?> outputOption,
        Option<string?> metadataOption)
    {
        var parseResult = context.ParseResult;
        IReadOnlyList<string>? moduleOverride = context.ModuleFilter?.Modules.Count > 0 ? context.ModuleFilter.Modules : null;
        var includeSystem = context.ModuleFilter?.IncludeSystemModules;
        var onlyActive = ResolveOnlyActiveOverride(parseResult, context.ModuleFilter, useModuleDefaults);

        return new ExtractModelOverrides(
            moduleOverride,
            includeSystem,
            onlyActive,
            parseResult.GetValueForOption(outputOption),
            parseResult.GetValueForOption(_mockAdvancedSqlOption),
            parseResult.GetValueForOption(metadataOption));
    }

    private FullExportOverrides CreateFullExportOverrides(VerbOverrideBindingContext context)
    {
        var parseResult = context.ParseResult;
        var modelPath = parseResult.GetValueForOption(_modelOption);
        DynamicInsertOutputMode? dynamicInsertMode = null;
        if (parseResult.HasOption(_dynamicInsertModeOption))
        {
            dynamicInsertMode = parseResult.GetValueForOption(_dynamicInsertModeOption);
        }

        var buildOverrides = new BuildSsdtOverrides(
            modelPath,
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_fullExportBuildOutput),
            parseResult.GetValueForOption(_profilerProviderOption),
            parseResult.GetValueForOption(_staticDataOption),
            parseResult.GetValueForOption(_renameTableOption),
            parseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism),
            parseResult.GetValueForOption(_fullExportBuildSqlMetadataOption),
            DynamicInsertMode: dynamicInsertMode);

        var profileOverrides = new CaptureProfileOverrides(
            modelPath,
            parseResult.GetValueForOption(_fullExportProfileOutOption),
            parseResult.GetValueForOption(_profilerProviderOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_fullExportProfileSqlMetadata));

        var extractOverrides = CreateExtractOverrides(context, useModuleDefaults: true, _fullExportExtractOutOption, _extractSqlMetadataOption);
        var reuseModelPath = !string.IsNullOrWhiteSpace(modelPath) && !HasConflictingExtractOverrides(parseResult);

        return new FullExportOverrides(
            buildOverrides,
            profileOverrides,
            extractOverrides,
            context.SchemaApply,
            reuseModelPath,
            context.UatUsers);
    }

    private bool? ResolveOnlyActiveOverride(ParseResult parseResult, ModuleFilterOverrides? moduleFilter, bool useModuleDefaults)
    {
        if (parseResult.HasOption(_onlyActiveAttributesOption))
        {
            return true;
        }

        if (parseResult.HasOption(_includeInactiveAttributesOption))
        {
            return false;
        }

        if (useModuleDefaults && moduleFilter is not null && moduleFilter.IncludeInactiveModules.HasValue)
        {
            return !moduleFilter.IncludeInactiveModules.Value;
        }

        return null;
    }

    private bool HasConflictingExtractOverrides(ParseResult parseResult)
        => parseResult.HasOption(_fullExportExtractOutOption)
            || parseResult.HasOption(_mockAdvancedSqlOption)
            || parseResult.HasOption(_extractSqlMetadataOption)
            || parseResult.HasOption(_onlyActiveAttributesOption)
            || parseResult.HasOption(_includeInactiveAttributesOption);
}
