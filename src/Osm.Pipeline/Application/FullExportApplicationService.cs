using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Emission;
using Osm.Json;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.UatUsers;

namespace Osm.Pipeline.Application;

public sealed record FullExportApplicationInput(
    CliConfigurationContext ConfigurationContext,
    FullExportOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache,
    TighteningOverrides? TighteningOverrides = null,
    SchemaApplyOverrides? ApplyOverrides = null);

public sealed record FullExportApplicationResult(
    BuildSsdtApplicationResult Build,
    CaptureProfileApplicationResult Profile,
    ExtractModelApplicationResult Extraction,
    SchemaApplyResult Apply,
    SchemaApplyOptions ApplyOptions,
    UatUsersApplicationResult UatUsers);

public sealed class FullExportApplicationService : PipelineApplicationServiceBase, IApplicationService<FullExportApplicationInput, FullExportApplicationResult>
{
    private readonly IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> _profileService;
    private readonly IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> _extractService;
    private readonly IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> _buildService;
    private readonly SchemaApplyOrchestrator _schemaApplyOrchestrator;
    private readonly IModelJsonDeserializer _modelDeserializer;
    private readonly IUatUsersPipelineRunner _uatUsersRunner;
    private static readonly OutsystemsMetadataSnapshot PlaceholderMetadataSnapshot = new(
        Array.Empty<OutsystemsModuleRow>(),
        Array.Empty<OutsystemsEntityRow>(),
        Array.Empty<OutsystemsAttributeRow>(),
        Array.Empty<OutsystemsReferenceRow>(),
        Array.Empty<OutsystemsPhysicalTableRow>(),
        Array.Empty<OutsystemsColumnRealityRow>(),
        Array.Empty<OutsystemsColumnCheckRow>(),
        Array.Empty<OutsystemsColumnCheckJsonRow>(),
        Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
        Array.Empty<OutsystemsIndexRow>(),
        Array.Empty<OutsystemsIndexColumnRow>(),
        Array.Empty<OutsystemsForeignKeyRow>(),
        Array.Empty<OutsystemsForeignKeyColumnRow>(),
        Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
        Array.Empty<OutsystemsAttributeHasFkRow>(),
        Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
        Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
        Array.Empty<OutsystemsTriggerRow>(),
        Array.Empty<OutsystemsAttributeJsonRow>(),
        Array.Empty<OutsystemsRelationshipJsonRow>(),
        Array.Empty<OutsystemsIndexJsonRow>(),
        Array.Empty<OutsystemsTriggerJsonRow>(),
        Array.Empty<OutsystemsModuleJsonRow>(),
        "(reused)");

    public FullExportApplicationService(
        IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> profileService,
        IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> extractService,
        IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> buildService,
        SchemaApplyOrchestrator schemaApplyOrchestrator,
        IModelJsonDeserializer modelDeserializer,
        IUatUsersPipelineRunner uatUsersRunner)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _extractService = extractService ?? throw new ArgumentNullException(nameof(extractService));
        _buildService = buildService ?? throw new ArgumentNullException(nameof(buildService));
        _schemaApplyOrchestrator = schemaApplyOrchestrator ?? throw new ArgumentNullException(nameof(schemaApplyOrchestrator));
        _modelDeserializer = modelDeserializer ?? throw new ArgumentNullException(nameof(modelDeserializer));
        _uatUsersRunner = uatUsersRunner ?? throw new ArgumentNullException(nameof(uatUsersRunner));
    }

    public async Task<Result<FullExportApplicationResult>> RunAsync(
        FullExportApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        input = EnsureNotNull(input, nameof(input));
        var configurationContext = EnsureNotNull(input.ConfigurationContext, nameof(input.ConfigurationContext));

        var overrides = input.Overrides ?? FullExportOverrides.Empty;
        var buildOverrides = overrides.Build ?? FullExportOverrides.Empty.Build;
        var profileOverrides = overrides.Profile ?? FullExportOverrides.Empty.Profile;
        var extractOverrides = overrides.Extract ?? FullExportOverrides.Empty.Extract;
        var moduleFilter = input.ModuleFilter;
        var configuration = configurationContext.Configuration ?? CliConfiguration.Empty;

        if ((extractOverrides.Modules is null || extractOverrides.Modules.Count == 0) && moduleFilter.Modules.Count > 0)
        {
            extractOverrides = extractOverrides with { Modules = moduleFilter.Modules };
        }

        if (!extractOverrides.IncludeSystemModules.HasValue && moduleFilter.IncludeSystemModules.HasValue)
        {
            extractOverrides = extractOverrides with { IncludeSystemModules = moduleFilter.IncludeSystemModules };
        }

        if (!extractOverrides.OnlyActiveAttributes.HasValue && moduleFilter.IncludeInactiveModules.HasValue)
        {
            extractOverrides = extractOverrides with { OnlyActiveAttributes = !moduleFilter.IncludeInactiveModules.Value };
        }

        var includeSystemModules = moduleFilter.IncludeSystemModules
            ?? configuration.ModuleFilter.IncludeSystemModules
            ?? true;
        var includeInactiveModules = moduleFilter.IncludeInactiveModules
            ?? configuration.ModuleFilter.IncludeInactiveModules
            ?? true;

        Result<ExtractModelApplicationResult> extractResult;

        if (overrides.ReuseModelPath)
        {
            var reusePath = ResolveReuseModelPath(buildOverrides, profileOverrides, configuration);
            if (string.IsNullOrWhiteSpace(reusePath))
            {
                return ValidationError.Create(
                    "pipeline.fullExport.model.reuse.missing",
                    "Model reuse was requested but no model path was provided. Supply --model or configure model.path.");
            }

            extractResult = CreateReuseExtractionResult(reusePath!, includeSystemModules, includeInactiveModules);
        }
        else
        {
            var extractInput = new ExtractModelApplicationInput(configurationContext, extractOverrides, input.Sql);
            extractResult = await _extractService
                .RunAsync(extractInput, cancellationToken)
                .ConfigureAwait(false);
        }

        if (extractResult.IsFailure)
        {
            return Result<FullExportApplicationResult>.Failure(extractResult.Errors);
        }

        var extraction = extractResult.Value;
        var resolvedModelPath = ResolveModelPath(buildOverrides, profileOverrides, extraction);

        if (string.IsNullOrWhiteSpace(profileOverrides.ModelPath) && !string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            profileOverrides = profileOverrides with { ModelPath = resolvedModelPath };
        }

        var profileInput = new CaptureProfileApplicationInput(
            configurationContext,
            profileOverrides,
            moduleFilter,
            input.Sql,
            input.TighteningOverrides);

        var profileResult = await _profileService
            .RunAsync(profileInput, cancellationToken)
            .ConfigureAwait(false);

        if (profileResult.IsFailure)
        {
            return Result<FullExportApplicationResult>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;
        var profileSnapshotPath = profile.PipelineResult?.ProfilePath;

        if (string.IsNullOrWhiteSpace(buildOverrides.ModelPath) && !string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            buildOverrides = buildOverrides with { ModelPath = resolvedModelPath };
        }

        if (string.IsNullOrWhiteSpace(buildOverrides.ProfilePath) && !string.IsNullOrWhiteSpace(profileSnapshotPath))
        {
            buildOverrides = buildOverrides with { ProfilePath = profileSnapshotPath };
        }

        if (string.IsNullOrWhiteSpace(buildOverrides.ProfilerProvider) && !string.IsNullOrWhiteSpace(profile.ProfilerProvider))
        {
            buildOverrides = buildOverrides with { ProfilerProvider = profile.ProfilerProvider };
        }

        var buildInput = new BuildSsdtApplicationInput(
            configurationContext,
            buildOverrides,
            moduleFilter,
            input.Sql,
            input.Cache,
            input.TighteningOverrides,
            extraction.ExtractionResult.Dataset);

        var buildResult = await _buildService
            .RunAsync(buildInput, cancellationToken)
            .ConfigureAwait(false);

        if (buildResult.IsFailure)
        {
            return Result<FullExportApplicationResult>.Failure(buildResult.Errors);
        }

        var applyOverrides = input.ApplyOverrides ?? overrides.Apply ?? SchemaApplyOverrides.Empty;
        var applyOptions = ResolveSchemaApplyOptions(configurationContext, input.Sql, applyOverrides);

        var applyResult = await _schemaApplyOrchestrator
            .ExecuteAsync(buildResult.Value.PipelineResult, applyOptions, log: null, cancellationToken)
            .ConfigureAwait(false);

        if (applyResult.IsFailure)
        {
            return Result<FullExportApplicationResult>.Failure(applyResult.Errors);
        }

        var uatUsersOverrides = overrides.UatUsers ?? FullExportOverrides.Empty.UatUsers;
        var uatUsersOutcome = UatUsersApplicationResult.Disabled;

        if (uatUsersOverrides.Enabled)
        {
            if (string.IsNullOrWhiteSpace(buildResult.Value.OutputDirectory))
            {
                return Result<FullExportApplicationResult>.Failure(ValidationError.Create(
                    "pipeline.fullExport.uatUsers.outputDirectory.missing",
                    "Build output directory is required to emit uat-users artifacts."));
            }

            var uatUsersRequest = new UatUsersPipelineRequest(
                uatUsersOverrides,
                extraction.ExtractionResult,
                buildResult.Value.OutputDirectory);

            var uatUsersResult = await _uatUsersRunner
                .RunAsync(uatUsersRequest, cancellationToken)
                .ConfigureAwait(false);

            if (uatUsersResult.IsFailure)
            {
                return Result<FullExportApplicationResult>.Failure(uatUsersResult.Errors);
            }

            uatUsersOutcome = uatUsersResult.Value;
        }

        return new FullExportApplicationResult(
            buildResult.Value,
            profile,
            extraction,
            applyResult.Value,
            applyOptions,
            uatUsersOutcome);
    }

    private Result<ExtractModelApplicationResult> CreateReuseExtractionResult(
        string modelPath,
        bool includeSystemModules,
        bool includeInactiveModules)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path must be provided.", nameof(modelPath));
        }

        if (!File.Exists(modelPath))
        {
            return Result<ExtractModelApplicationResult>.Failure(ValidationError.Create(
                "pipeline.fullExport.model.reuse.notFound",
                $"Model path '{modelPath}' does not exist."));
        }

        try
        {
            using var stream = File.Open(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var warnings = new List<string>();
            var options = ModelJsonDeserializerOptions.Default
                .WithAllowDuplicateAttributeLogicalNames(true)
                .WithAllowDuplicateAttributeColumnNames(true);
            var modelResult = _modelDeserializer.Deserialize(stream, warnings, options);
            if (modelResult.IsFailure)
            {
                return Result<ExtractModelApplicationResult>.Failure(modelResult.Errors);
            }

            var model = modelResult.Value;
            AppendModuleWarnings(model, includeSystemModules, includeInactiveModules, warnings);

            var extraction = new ModelExtractionResult(
                model,
                ModelJsonPayload.FromFile(modelPath),
                NormalizeExportTimestamp(model.ExportedAtUtc),
                warnings,
                PlaceholderMetadataSnapshot,
                DynamicEntityDataset.Empty);

            return Result<ExtractModelApplicationResult>.Success(
                new ExtractModelApplicationResult(extraction, modelPath, ModelWasReused: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ExtractModelApplicationResult>.Failure(ValidationError.Create(
                "pipeline.fullExport.model.reuse.io",
                $"Failed to open model path '{modelPath}': {ex.Message}"));
        }
    }

    private static string? ResolveReuseModelPath(
        BuildSsdtOverrides buildOverrides,
        CaptureProfileOverrides profileOverrides,
        CliConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(buildOverrides?.ModelPath))
        {
            return buildOverrides.ModelPath;
        }

        if (!string.IsNullOrWhiteSpace(profileOverrides?.ModelPath))
        {
            return profileOverrides.ModelPath;
        }

        if (!string.IsNullOrWhiteSpace(configuration?.ModelPath))
        {
            return configuration.ModelPath;
        }

        return null;
    }

    private static void AppendModuleWarnings(
        OsmModel model,
        bool includeSystemModules,
        bool includeInactiveModules,
        ICollection<string> warnings)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        var seen = new HashSet<string>(warnings, StringComparer.Ordinal);

        foreach (var module in model.Modules)
        {
            if (!includeSystemModules && module.IsSystemModule)
            {
                continue;
            }

            var entityCount = includeInactiveModules
                ? module.Entities.Length
                : module.Entities.Count(static entity => entity.IsActive);

            if (entityCount > 0)
            {
                continue;
            }

            var message = $"Module '{module.Name.Value}' contains no entities and will be skipped.";
            if (seen.Add(message))
            {
                warnings.Add(message);
            }
        }
    }

    private static DateTimeOffset NormalizeExportTimestamp(DateTime exportedAtUtc)
    {
        if (exportedAtUtc.Kind == DateTimeKind.Local)
        {
            exportedAtUtc = exportedAtUtc.ToUniversalTime();
        }
        else if (exportedAtUtc.Kind == DateTimeKind.Unspecified)
        {
            exportedAtUtc = DateTime.SpecifyKind(exportedAtUtc, DateTimeKind.Utc);
        }

        return new DateTimeOffset(exportedAtUtc, TimeSpan.Zero);
    }

    private static string? ResolveModelPath(
        BuildSsdtOverrides buildOverrides,
        CaptureProfileOverrides profileOverrides,
        ExtractModelApplicationResult extraction)
    {
        if (!string.IsNullOrWhiteSpace(buildOverrides.ModelPath))
        {
            return buildOverrides.ModelPath;
        }

        if (!string.IsNullOrWhiteSpace(profileOverrides.ModelPath))
        {
            return profileOverrides.ModelPath;
        }

        return extraction.OutputPath;
    }

    private static SchemaApplyOptions ResolveSchemaApplyOptions(
        CliConfigurationContext configurationContext,
        SqlOptionsOverrides sqlOverrides,
        SchemaApplyOverrides applyOverrides)
    {
        if (configurationContext is null)
        {
            throw new ArgumentNullException(nameof(configurationContext));
        }

        if (sqlOverrides is null)
        {
            throw new ArgumentNullException(nameof(sqlOverrides));
        }

        applyOverrides ??= SchemaApplyOverrides.Empty;

        var configuration = configurationContext.Configuration ?? CliConfiguration.Empty;
        var sqlConfiguration = configuration.Sql ?? SqlConfiguration.Empty;

        var connectionString = applyOverrides.ConnectionString
            ?? sqlOverrides.ConnectionString
            ?? sqlConfiguration.ConnectionString;

        var commandTimeout = applyOverrides.CommandTimeoutSeconds
            ?? sqlOverrides.CommandTimeoutSeconds
            ?? sqlConfiguration.CommandTimeoutSeconds;

        var authenticationConfiguration = sqlConfiguration.Authentication ?? SqlAuthenticationConfiguration.Empty;
        var authenticationMethod = applyOverrides.AuthenticationMethod
            ?? sqlOverrides.AuthenticationMethod
            ?? authenticationConfiguration.Method;
        var trustServerCertificate = applyOverrides.TrustServerCertificate
            ?? sqlOverrides.TrustServerCertificate
            ?? authenticationConfiguration.TrustServerCertificate;
        var applicationName = applyOverrides.ApplicationName
            ?? sqlOverrides.ApplicationName
            ?? authenticationConfiguration.ApplicationName;
        var accessToken = applyOverrides.AccessToken
            ?? sqlOverrides.AccessToken
            ?? authenticationConfiguration.AccessToken;

        var enabled = applyOverrides.Enabled;
        enabled ??= !string.IsNullOrWhiteSpace(connectionString);

        if (!enabled.Value || string.IsNullOrWhiteSpace(connectionString))
        {
            return SchemaApplyOptions.Disabled;
        }

        var safeScript = applyOverrides.ApplySafeScript ?? true;
        var staticSeeds = applyOverrides.ApplyStaticSeeds ?? true;

        var authentication = new SqlAuthenticationSettings(
            authenticationMethod,
            trustServerCertificate,
            applicationName,
            accessToken);

        return new SchemaApplyOptions(
            Enabled: true,
            ConnectionString: connectionString,
            Authentication: authentication,
            CommandTimeoutSeconds: commandTimeout,
            ApplySafeScript: safeScript,
            ApplyStaticSeeds: staticSeeds,
            StaticSeedSynchronizationMode.NonDestructive);
    }
}
