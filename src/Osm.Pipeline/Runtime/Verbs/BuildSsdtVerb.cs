using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Runtime.Verbs;

public sealed record BuildSsdtVerbOptions
{
    public string? ConfigurationPath { get; init; }
    public BuildSsdtOverrides Overrides { get; init; } = new(null, null, null, null, null, null, null);
    public ModuleFilterOverrides ModuleFilter { get; init; } = new(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>());
    public SqlOptionsOverrides Sql { get; init; } = new(null, null, null, null, null, null, null, null, null);
    public CacheOptionsOverrides Cache { get; init; } = new(null, null);
}

public sealed record BuildSsdtVerbResult(
    CliConfigurationContext Configuration,
    BuildSsdtApplicationResult ApplicationResult);

public sealed class BuildSsdtVerb : PipelineVerb<BuildSsdtVerbOptions, BuildSsdtVerbResult>
{
    public const string VerbName = "build-ssdt";

    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> _applicationService;

    public BuildSsdtVerb(
        ICliConfigurationService configurationService,
        IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> applicationService,
        TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
    }

    public override string Name => VerbName;

    protected override async Task<Result<BuildSsdtVerbResult>> ExecuteAsync(
        BuildSsdtVerbOptions options,
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
            return Result<BuildSsdtVerbResult>.Failure(configurationResult.Errors);
        }

        var input = new BuildSsdtApplicationInput(
            configurationResult.Value,
            options.Overrides,
            options.ModuleFilter,
            options.Sql,
            options.Cache);

        var applicationResult = await _applicationService
            .RunAsync(input, cancellationToken)
            .ConfigureAwait(false);

        if (applicationResult.IsFailure)
        {
            return Result<BuildSsdtVerbResult>.Failure(applicationResult.Errors);
        }

        return new BuildSsdtVerbResult(configurationResult.Value, applicationResult.Value);
    }

    protected override IReadOnlyList<PipelineArtifact> DescribeArtifacts(BuildSsdtVerbResult result)
    {
        var artifacts = new List<PipelineArtifact>();
        var pipelineResult = result.ApplicationResult.PipelineResult;
        var outputDirectory = result.ApplicationResult.OutputDirectory;

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            artifacts.Add(new PipelineArtifact("manifest", Path.Combine(outputDirectory, "manifest.json"), "application/json"));
        }

        if (!string.IsNullOrWhiteSpace(pipelineResult.DecisionLogPath))
        {
            artifacts.Add(new PipelineArtifact("decision-log", pipelineResult.DecisionLogPath, "application/json"));
        }

        if (!pipelineResult.StaticSeedScriptPaths.IsDefaultOrEmpty)
        {
            foreach (var seedPath in pipelineResult.StaticSeedScriptPaths)
            {
                if (!string.IsNullOrWhiteSpace(seedPath))
                {
                    artifacts.Add(new PipelineArtifact("static-seed", seedPath, "application/sql"));
                }
            }
        }

        if (pipelineResult.EvidenceCache is { } cache)
        {
            artifacts.Add(new PipelineArtifact("evidence-cache", cache.CacheDirectory));
            artifacts.Add(new PipelineArtifact("evidence-manifest", Path.Combine(cache.CacheDirectory, "manifest.json"), "application/json"));
        }

        return artifacts;
    }

    protected override IReadOnlyDictionary<string, string?> BuildMetadata(
        BuildSsdtVerbOptions options,
        Result<BuildSsdtVerbResult> outcome)
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
            builder["profilePath"] = result.ApplicationResult.ProfilePath;
            builder["profilerProvider"] = result.ApplicationResult.ProfilerProvider;
        }

        return builder.ToImmutable();
    }
}
