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
    SchemaApplyOverrides? ApplyOverrides = null,
    UatUsersConfiguration? UatUsersConfiguration = null);

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
    private readonly FullExportCoordinator _coordinator;
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
        IUatUsersPipelineRunner uatUsersRunner,
        FullExportCoordinator coordinator)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _extractService = extractService ?? throw new ArgumentNullException(nameof(extractService));
        _buildService = buildService ?? throw new ArgumentNullException(nameof(buildService));
        _schemaApplyOrchestrator = schemaApplyOrchestrator ?? throw new ArgumentNullException(nameof(schemaApplyOrchestrator));
        _modelDeserializer = modelDeserializer ?? throw new ArgumentNullException(nameof(modelDeserializer));
        _uatUsersRunner = uatUsersRunner ?? throw new ArgumentNullException(nameof(uatUsersRunner));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
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
        var resolvedSqlConnectionString = ResolveString(input.Sql.ConnectionString, configuration.Sql?.ConnectionString);
        var uatUsersConfiguration = input.UatUsersConfiguration
            ?? configuration.UatUsers
            ?? UatUsersConfiguration.Empty;

        var moduleFilterOptionsResult = ModuleFilterResolver.Resolve(configuration, moduleFilter);
        if (moduleFilterOptionsResult.IsFailure)
        {
            return Result<FullExportApplicationResult>.Failure(moduleFilterOptionsResult.Errors);
        }

        var moduleFilterOptions = moduleFilterOptionsResult.Value;

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

        var includeSystemModules = moduleFilterOptions.IncludeSystemModules;
        var includeInactiveModules = moduleFilterOptions.IncludeInactiveModules;

        var reusePathCandidate = ResolveReuseModelPath(buildOverrides, profileOverrides, configuration);
        var shouldReuseModelPath = overrides.ReuseModelPath;

        if (!shouldReuseModelPath
            && !string.IsNullOrWhiteSpace(configuration.ModelPath)
            && string.Equals(reusePathCandidate, configuration.ModelPath, StringComparison.OrdinalIgnoreCase))
        {
            shouldReuseModelPath = true;
        }

        var extractInput = shouldReuseModelPath
            ? null
            : new ExtractModelApplicationInput(configurationContext, extractOverrides, input.Sql);

        Func<CancellationToken, Task<Result<ExtractModelApplicationResult>>> extractStage;
        if (shouldReuseModelPath)
        {
            if (string.IsNullOrWhiteSpace(reusePathCandidate))
            {
                return ValidationError.Create(
                    "pipeline.fullExport.model.reuse.missing",
                    "Model reuse was requested but no model path was provided. Supply --model or configure model.path.");
            }

            extractStage = _ => Task.FromResult(CreateReuseExtractionResult(
                reusePathCandidate!,
                includeSystemModules,
                includeInactiveModules,
                moduleFilterOptions.ValidationOverrides));
        }
        else
        {
            extractStage = cancellationToken1 => _extractService
                .RunAsync(extractInput!, cancellationToken1);
        }

        var profileOverridesLocal = profileOverrides;
        var buildOverridesLocal = buildOverrides;
        string? resolvedModelPath = null;
        string? profileSnapshotPath = null;

        var profileStage = new Func<ExtractModelApplicationResult, CancellationToken, Task<Result<CaptureProfileApplicationResult>>>(
            async (extraction, ct) =>
            {
                resolvedModelPath = ResolveModelPath(buildOverridesLocal, profileOverridesLocal, extraction);
                if (string.IsNullOrWhiteSpace(profileOverridesLocal.ModelPath) && !string.IsNullOrWhiteSpace(resolvedModelPath))
                {
                    profileOverridesLocal = profileOverridesLocal with { ModelPath = resolvedModelPath };
                }

                var profileInput = new CaptureProfileApplicationInput(
                    configurationContext,
                    profileOverridesLocal,
                    moduleFilter,
                    input.Sql,
                    input.TighteningOverrides);

                var profileResult = await _profileService
                    .RunAsync(profileInput, ct)
                    .ConfigureAwait(false);

                if (profileResult.IsSuccess)
                {
                    var profileValue = profileResult.Value;
                    profileSnapshotPath = profileValue.PipelineResult?.ProfilePath;

                    if (string.IsNullOrWhiteSpace(buildOverridesLocal.ModelPath) && !string.IsNullOrWhiteSpace(resolvedModelPath))
                    {
                        buildOverridesLocal = buildOverridesLocal with { ModelPath = resolvedModelPath };
                    }

                    if (string.IsNullOrWhiteSpace(buildOverridesLocal.ProfilePath) && !string.IsNullOrWhiteSpace(profileSnapshotPath))
                    {
                        buildOverridesLocal = buildOverridesLocal with { ProfilePath = profileSnapshotPath };
                    }

                    if (string.IsNullOrWhiteSpace(buildOverridesLocal.ProfilerProvider) && !string.IsNullOrWhiteSpace(profileValue.ProfilerProvider))
                    {
                        buildOverridesLocal = buildOverridesLocal with { ProfilerProvider = profileValue.ProfilerProvider };
                    }
                }

                return profileResult;
            });

        var buildStage = new Func<ExtractModelApplicationResult, CaptureProfileApplicationResult, CancellationToken, Task<Result<BuildSsdtApplicationResult>>>(
            (extraction, profile, ct) =>
            {
                var buildInput = new BuildSsdtApplicationInput(
                    configurationContext,
                    buildOverridesLocal,
                    moduleFilter,
                    input.Sql,
                    input.Cache,
                    input.TighteningOverrides,
                    extraction.ExtractionResult.Dataset,
                    EnableDynamicSqlExtraction: true);

                return _buildService.RunAsync(buildInput, ct);
            });

        var applyOverrides = input.ApplyOverrides ?? overrides.Apply ?? SchemaApplyOverrides.Empty;
        var applyOptions = ResolveSchemaApplyOptions(configurationContext, input.Sql, applyOverrides);

        var applyStage = new Func<BuildSsdtApplicationResult, CancellationToken, Task<Result<SchemaApplyResult>>>(
            (build, ct) => _schemaApplyOrchestrator.ExecuteAsync(build.PipelineResult, applyOptions, log: null, ct));

        var uatUsersOverrides = ResolveUatUsersOverrides(overrides.UatUsers, uatUsersConfiguration);
        Func<ExtractModelApplicationResult, BuildSsdtApplicationResult, ModelUserSchemaGraph, CancellationToken, Task<Result<UatUsersApplicationResult>>>? uatStage = null;
        if (uatUsersOverrides.Enabled)
        {
            uatStage = (extraction, build, schemaGraph, ct) =>
            {
                if (string.IsNullOrWhiteSpace(build.OutputDirectory))
                {
                    return Task.FromResult(Result<UatUsersApplicationResult>.Failure(ValidationError.Create(
                        "pipeline.fullExport.uatUsers.outputDirectory.missing",
                        "Build output directory is required to emit uat-users artifacts.")));
                }

                var uatUsersRequest = new UatUsersPipelineRequest(
                    uatUsersOverrides,
                    extraction.ExtractionResult,
                    build.OutputDirectory,
                    resolvedSqlConnectionString,
                    schemaGraph);

                return _uatUsersRunner.RunAsync(uatUsersRequest, ct);
            };
        }

        var coordinatorRequest = new FullExportCoordinatorRequest<ExtractModelApplicationResult, CaptureProfileApplicationResult, BuildSsdtApplicationResult>(
            extractStage,
            profileStage,
            buildStage,
            applyStage,
            applyOptions,
            extraction => extraction.ExtractionResult,
            uatStage);

        var coordinatorResult = await _coordinator
            .ExecuteAsync(coordinatorRequest, cancellationToken)
            .ConfigureAwait(false);

        if (coordinatorResult.IsFailure)
        {
            return Result<FullExportApplicationResult>.Failure(coordinatorResult.Errors);
        }

        var outcome = coordinatorResult.Value;

        return new FullExportApplicationResult(
            outcome.Build,
            outcome.Profile,
            outcome.Extraction,
            outcome.Apply,
            outcome.ApplyOptions,
            outcome.UatUsers);
    }

    private static UatUsersOverrides ResolveUatUsersOverrides(
        UatUsersOverrides? overrides,
        UatUsersConfiguration configuration)
    {
        configuration ??= UatUsersConfiguration.Empty;
        var cliEnabled = overrides?.Enabled == true;
        var configurationEnabled = ShouldEnableFromConfiguration(configuration);

        if (!cliEnabled && !configurationEnabled)
        {
            return UatUsersOverrides.Disabled;
        }

        var uatInventoryPath = ResolveString(overrides?.UatUserInventoryPath, configuration.UatUserInventoryPath);
        var qaInventoryPath = ResolveString(overrides?.QaUserInventoryPath, configuration.QaUserInventoryPath);

        if (string.IsNullOrWhiteSpace(uatInventoryPath)
            || string.IsNullOrWhiteSpace(qaInventoryPath))
        {
            return UatUsersOverrides.Disabled;
        }

        var includeColumns = ResolveIncludeColumns(overrides?.IncludeColumns, configuration.IncludeColumns);
        var userSchema = ResolveString(overrides?.UserSchema, configuration.UserSchema) ?? "dbo";
        var userTable = ResolveString(overrides?.UserTable, configuration.UserTable) ?? "User";
        var userIdColumn = ResolveString(overrides?.UserIdColumn, configuration.UserIdColumn) ?? "Id";
        var matchingStrategy = overrides?.MatchingStrategy ?? configuration.MatchingStrategy;
        var matchingAttribute = ResolveString(overrides?.MatchingAttribute, configuration.MatchingAttribute);
        var matchingRegex = ResolveString(overrides?.MatchingRegexPattern, configuration.MatchingRegexPattern);
        var fallbackAssignment = overrides?.FallbackAssignment ?? configuration.FallbackAssignment;
        var idempotentEmission = overrides?.IdempotentEmission
            ?? configuration.IdempotentEmission
            ?? false;
        var concurrency = overrides?.Concurrency ?? configuration.Concurrency;

        IReadOnlyList<UserIdentifier> fallbackTargets;
        if (overrides?.FallbackTargets is { Count: > 0 } overrideTargets)
        {
            fallbackTargets = overrideTargets.ToArray();
        }
        else
        {
            fallbackTargets = UserMatchingConfigurationHelper
                .NormalizeFallbackTargets(configuration.FallbackTargets)
                .ToArray();
        }

        return new UatUsersOverrides(
            Enabled: true,
            UserSchema: userSchema,
            UserTable: userTable,
            UserIdColumn: userIdColumn,
            IncludeColumns: includeColumns,
            UserMapPath: ResolveString(overrides?.UserMapPath, configuration.UserMapPath),
            UatUserInventoryPath: uatInventoryPath,
            QaUserInventoryPath: qaInventoryPath,
            SnapshotPath: ResolveString(overrides?.SnapshotPath, configuration.SnapshotPath),
            UserEntityIdentifier: ResolveString(overrides?.UserEntityIdentifier, configuration.UserEntityIdentifier),
            MatchingStrategy: matchingStrategy,
            MatchingAttribute: matchingAttribute,
            MatchingRegexPattern: matchingRegex,
            FallbackAssignment: fallbackAssignment,
            FallbackTargets: fallbackTargets,
            IdempotentEmission: idempotentEmission,
            Concurrency: concurrency);
    }

    private static IReadOnlyList<string> ResolveIncludeColumns(
        IReadOnlyList<string>? overrides,
        IReadOnlyList<string>? configuration)
    {
        if (overrides is { Count: > 0 })
        {
            return overrides;
        }

        if (configuration is { Count: > 0 })
        {
            return configuration.ToArray();
        }

        return Array.Empty<string>();
    }

    private static string? ResolveString(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private static bool ShouldEnableFromConfiguration(UatUsersConfiguration configuration)
    {
        if (configuration is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.QaUserInventoryPath))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(configuration.UatUserInventoryPath);
    }

    private Result<ExtractModelApplicationResult> CreateReuseExtractionResult(
        string modelPath,
        bool includeSystemModules,
        bool includeInactiveModules,
        ModuleValidationOverrides validationOverrides)
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
            var options = new ModelJsonDeserializerOptions(
                validationOverrides,
                missingSchemaFallback: null,
                allowDuplicateAttributeLogicalNames: true,
                allowDuplicateAttributeColumnNames: true);
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
        var staticSeedMode = applyOverrides.StaticSeedSynchronizationMode
            ?? configuration.Tightening.Emission.StaticSeeds.SynchronizationMode;

        var connectionString = string.IsNullOrWhiteSpace(applyOverrides.ConnectionString)
            ? null
            : applyOverrides.ConnectionString!.Trim();

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
            StaticSeedSynchronizationMode: staticSeedMode);
    }
}
