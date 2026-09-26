using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Runtime;

namespace Osm.Pipeline.Runtime.Verbs;

public sealed record AnalyzeVerbOptions
{
    public string? ConfigurationPath { get; init; }
    public AnalyzeOverrides Overrides { get; init; } = new(null, null, null);
    public TighteningOverrides? Tightening { get; init; }
}

public sealed record AnalyzeVerbResult(
    CliConfigurationContext Configuration,
    AnalyzeApplicationResult ApplicationResult);

public sealed class AnalyzeVerb : PipelineVerb<AnalyzeVerbOptions, AnalyzeVerbResult>
{
    public const string VerbName = "analyze";

    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult> _applicationService;

    public AnalyzeVerb(
        ICliConfigurationService configurationService,
        IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult> applicationService,
        TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
    }

    public override string Name => VerbName;

    protected override async Task<Result<AnalyzeVerbResult>> ExecuteAsync(
        AnalyzeVerbOptions options,
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
            return Result<AnalyzeVerbResult>.Failure(configurationResult.Errors);
        }

        var overrides = options.Overrides ?? new AnalyzeOverrides(null, null, null);
        var input = new AnalyzeApplicationInput(configurationResult.Value, overrides, options.Tightening);

        var applicationResult = await _applicationService
            .RunAsync(input, cancellationToken)
            .ConfigureAwait(false);

        if (applicationResult.IsFailure)
        {
            return Result<AnalyzeVerbResult>.Failure(applicationResult.Errors);
        }

        return new AnalyzeVerbResult(configurationResult.Value, applicationResult.Value);
    }

    protected override IReadOnlyList<PipelineArtifact> DescribeArtifacts(AnalyzeVerbResult result)
    {
        var artifacts = new List<PipelineArtifact>();
        var pipelineResult = result.ApplicationResult.PipelineResult;

        if (!string.IsNullOrWhiteSpace(pipelineResult.SummaryPath))
        {
            artifacts.Add(new PipelineArtifact("tightening-summary", pipelineResult.SummaryPath, "text/plain"));
        }

        if (!string.IsNullOrWhiteSpace(pipelineResult.DecisionLogPath))
        {
            artifacts.Add(new PipelineArtifact("policy-decisions", pipelineResult.DecisionLogPath, "application/json"));
        }

        return artifacts;
    }

    protected override IReadOnlyDictionary<string, string?> BuildMetadata(
        AnalyzeVerbOptions options,
        Result<AnalyzeVerbResult> outcome)
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
        }

        return builder.ToImmutable();
    }
}
