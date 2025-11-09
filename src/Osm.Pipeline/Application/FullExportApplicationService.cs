using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;

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
    SchemaApplyOptions ApplyOptions);

public sealed class FullExportApplicationService : PipelineApplicationServiceBase, IApplicationService<FullExportApplicationInput, FullExportApplicationResult>
{
    private readonly IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> _profileService;
    private readonly IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> _extractService;
    private readonly IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> _buildService;
    private readonly SchemaApplyOrchestrator _schemaApplyOrchestrator;

    public FullExportApplicationService(
        IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> profileService,
        IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> extractService,
        IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> buildService,
        SchemaApplyOrchestrator schemaApplyOrchestrator)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _extractService = extractService ?? throw new ArgumentNullException(nameof(extractService));
        _buildService = buildService ?? throw new ArgumentNullException(nameof(buildService));
        _schemaApplyOrchestrator = schemaApplyOrchestrator ?? throw new ArgumentNullException(nameof(schemaApplyOrchestrator));
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

        var extractInput = new ExtractModelApplicationInput(configurationContext, extractOverrides, input.Sql);
        var extractResult = await _extractService
            .RunAsync(extractInput, cancellationToken)
            .ConfigureAwait(false);

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

        return new FullExportApplicationResult(buildResult.Value, profile, extraction, applyResult.Value, applyOptions);
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
