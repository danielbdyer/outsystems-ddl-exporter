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

public sealed record DmmCompareVerbOptions
{
    public string? ConfigurationPath { get; init; }
    public CompareWithDmmOverrides Overrides { get; init; } = new(null, null, null, null, null);
    public ModuleFilterOverrides ModuleFilter { get; init; } = new(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>());
    public SqlOptionsOverrides Sql { get; init; } = new(null, null, null, null, null, null, null, null, null);
    public CacheOptionsOverrides Cache { get; init; } = new(null, null);
}

public sealed record DmmCompareVerbResult(
    CliConfigurationContext Configuration,
    CompareWithDmmApplicationResult ApplicationResult);

public sealed class DmmCompareVerb : PipelineVerb<DmmCompareVerbOptions, DmmCompareVerbResult>
{
    public const string VerbName = "dmm-compare";

    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult> _applicationService;

    public DmmCompareVerb(
        ICliConfigurationService configurationService,
        IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult> applicationService,
        TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
    }

    public override string Name => VerbName;

    protected override async Task<Result<DmmCompareVerbResult>> ExecuteAsync(
        DmmCompareVerbOptions options,
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
            return Result<DmmCompareVerbResult>.Failure(configurationResult.Errors);
        }

        var input = new CompareWithDmmApplicationInput(
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
            return Result<DmmCompareVerbResult>.Failure(applicationResult.Errors);
        }

        return new DmmCompareVerbResult(configurationResult.Value, applicationResult.Value);
    }

    protected override IReadOnlyList<PipelineArtifact> DescribeArtifacts(DmmCompareVerbResult result)
    {
        var artifacts = new List<PipelineArtifact>();
        var outputPath = result.ApplicationResult.DiffOutputPath;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            artifacts.Add(new PipelineArtifact("dmm-diff", outputPath, "application/json"));
        }

        if (result.ApplicationResult.PipelineResult.EvidenceCache is { } cache)
        {
            artifacts.Add(new PipelineArtifact("evidence-cache", cache.CacheDirectory));
            artifacts.Add(new PipelineArtifact("evidence-manifest", Path.Combine(cache.CacheDirectory, "manifest.json"), "application/json"));
        }

        return artifacts;
    }

    protected override IReadOnlyDictionary<string, string?> BuildMetadata(
        DmmCompareVerbOptions options,
        Result<DmmCompareVerbResult> outcome)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.ConfigurationPath))
        {
            builder["configOverride"] = options.ConfigurationPath;
        }

        if (outcome.IsSuccess)
        {
            var result = outcome.Value;
            var configuration = result.Configuration.Configuration;
            builder["configPath"] = result.Configuration.ConfigPath;
            builder["diffPath"] = result.ApplicationResult.DiffOutputPath;
            builder["modelPath"] = configuration.ModelPath;
            builder["profilePath"] = configuration.ProfilePath ?? configuration.Profiler.ProfilePath;
            builder["dmmPath"] = configuration.DmmPath;
        }

        return builder.ToImmutable();
    }
}
