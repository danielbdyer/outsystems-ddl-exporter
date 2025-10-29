using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Runtime.Verbs;

public sealed record ProfileVerbOptions
{
    public string? ConfigurationPath { get; init; }
    public CaptureProfileOverrides Overrides { get; init; } = new(null, null, null, null, null);
    public ModuleFilterOverrides ModuleFilter { get; init; } = new(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>());
    public SqlOptionsOverrides Sql { get; init; } = new(null, null, null, null, null, null, null, null);
}

public sealed record ProfileVerbResult(
    CliConfigurationContext Configuration,
    CaptureProfileApplicationResult ApplicationResult);

public sealed class ProfileVerb : PipelineVerb<ProfileVerbOptions, ProfileVerbResult>
{
    public const string VerbName = "profile";

    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> _applicationService;

    public ProfileVerb(
        ICliConfigurationService configurationService,
        IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> applicationService,
        TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
    }

    public override string Name => VerbName;

    protected override async Task<Result<ProfileVerbResult>> ExecuteAsync(
        ProfileVerbOptions options,
        CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var configurationResult = await _configurationService
            .LoadAsync(options.ConfigurationPath, cancellationToken)
            .ConfigureAwait(false);

        if (configurationResult.IsFailure)
        {
            return Result<ProfileVerbResult>.Failure(configurationResult.Errors);
        }

        var input = new CaptureProfileApplicationInput(
            configurationResult.Value,
            options.Overrides,
            options.ModuleFilter,
            options.Sql);

        var applicationResult = await _applicationService
            .RunAsync(input, cancellationToken)
            .ConfigureAwait(false);

        if (applicationResult.IsFailure)
        {
            return Result<ProfileVerbResult>.Failure(applicationResult.Errors);
        }

        return new ProfileVerbResult(configurationResult.Value, applicationResult.Value);
    }

    protected override IReadOnlyList<PipelineArtifact> DescribeArtifacts(ProfileVerbResult result)
    {
        var artifacts = new List<PipelineArtifact>();
        var pipelineResult = result.ApplicationResult.PipelineResult;

        if (!string.IsNullOrWhiteSpace(pipelineResult.ProfilePath))
        {
            artifacts.Add(new PipelineArtifact("profile", pipelineResult.ProfilePath, "application/json"));
        }

        if (!string.IsNullOrWhiteSpace(pipelineResult.ManifestPath))
        {
            artifacts.Add(new PipelineArtifact("manifest", pipelineResult.ManifestPath, "application/json"));
        }

        return artifacts;
    }

    protected override IReadOnlyDictionary<string, string?> BuildMetadata(
        ProfileVerbOptions options,
        Result<ProfileVerbResult> outcome)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.ConfigurationPath))
        {
            builder["configOverride"] = options.ConfigurationPath;
        }

        if (outcome.IsSuccess)
        {
            var result = outcome.Value;
            builder["configPath"] = result.Configuration.ConfigPath;
            builder["outputDirectory"] = result.ApplicationResult.OutputDirectory;
            builder["modelPath"] = result.ApplicationResult.ModelPath;
            builder["profilerProvider"] = result.ApplicationResult.ProfilerProvider;
            builder["fixtureProfilePath"] = result.ApplicationResult.FixtureProfilePath;
        }

        return builder.ToImmutable();
    }
}
