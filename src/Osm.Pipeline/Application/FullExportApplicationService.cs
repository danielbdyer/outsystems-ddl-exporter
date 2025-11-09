using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Application;

public sealed record FullExportApplicationInput(
    CliConfigurationContext ConfigurationContext,
    FullExportOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache,
    TighteningOverrides? TighteningOverrides = null);

public sealed record FullExportApplicationResult(
    BuildSsdtApplicationResult Build,
    CaptureProfileApplicationResult Profile,
    ExtractModelApplicationResult Extraction);

public sealed class FullExportApplicationService : PipelineApplicationServiceBase, IApplicationService<FullExportApplicationInput, FullExportApplicationResult>
{
    private readonly IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> _profileService;
    private readonly IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> _extractService;
    private readonly IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> _buildService;

    public FullExportApplicationService(
        IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> profileService,
        IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> extractService,
        IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> buildService)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _extractService = extractService ?? throw new ArgumentNullException(nameof(extractService));
        _buildService = buildService ?? throw new ArgumentNullException(nameof(buildService));
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
            input.TighteningOverrides);

        var buildResult = await _buildService
            .RunAsync(buildInput, cancellationToken)
            .ConfigureAwait(false);

        if (buildResult.IsFailure)
        {
            return Result<FullExportApplicationResult>.Failure(buildResult.Errors);
        }

        return new FullExportApplicationResult(buildResult.Value, profile, extraction);
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
}
