using System;
using System.Collections.Generic;
using System.IO;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Mediation;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPipeline : ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>
{
    private readonly TimeProvider _timeProvider;
    private readonly IBuildSsdtStep<PipelineInitialized, BootstrapCompleted> _bootstrapStep;
    private readonly IBuildSsdtStep<BootstrapCompleted, EvidencePrepared> _evidenceCacheStep;
    private readonly IBuildSsdtStep<EvidencePrepared, DecisionsSynthesized> _policyStep;
    private readonly IBuildSsdtStep<DecisionsSynthesized, EmissionReady> _emissionStep;
    private readonly IBuildSsdtStep<EmissionReady, StaticSeedsGenerated> _staticSeedStep;

    public BuildSsdtPipeline(
        IBuildSsdtStep<PipelineInitialized, BootstrapCompleted> bootstrapStep,
        IBuildSsdtStep<BootstrapCompleted, EvidencePrepared> evidenceCacheStep,
        IBuildSsdtStep<EvidencePrepared, DecisionsSynthesized> policyStep,
        IBuildSsdtStep<DecisionsSynthesized, EmissionReady> emissionStep,
        IBuildSsdtStep<EmissionReady, StaticSeedsGenerated> staticSeedStep,
        TimeProvider timeProvider)
    {
        _bootstrapStep = bootstrapStep ?? throw new ArgumentNullException(nameof(bootstrapStep));
        _evidenceCacheStep = evidenceCacheStep ?? throw new ArgumentNullException(nameof(evidenceCacheStep));
        _policyStep = policyStep ?? throw new ArgumentNullException(nameof(policyStep));
        _emissionStep = emissionStep ?? throw new ArgumentNullException(nameof(emissionStep));
        _staticSeedStep = staticSeedStep ?? throw new ArgumentNullException(nameof(staticSeedStep));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<Result<BuildSsdtPipelineResult>> HandleAsync(
        BuildSsdtPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ModelPath))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.model.missing",
                "Model path must be provided for SSDT emission.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.output.missing",
                "Output directory must be provided for SSDT emission.");
        }

        var log = new PipelineExecutionLogBuilder(_timeProvider);
        var initialized = new PipelineInitialized(request, log);

        var bootstrapResult = await _bootstrapStep.ExecuteAsync(initialized, cancellationToken).ConfigureAwait(false);
        if (bootstrapResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(bootstrapResult.Errors);
        }

        var evidenceResult = await _evidenceCacheStep.ExecuteAsync(bootstrapResult.Value, cancellationToken).ConfigureAwait(false);
        if (evidenceResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(evidenceResult.Errors);
        }

        var decisionsResult = await _policyStep.ExecuteAsync(evidenceResult.Value, cancellationToken).ConfigureAwait(false);
        if (decisionsResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(decisionsResult.Errors);
        }

        var emissionResult = await _emissionStep.ExecuteAsync(decisionsResult.Value, cancellationToken).ConfigureAwait(false);
        if (emissionResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(emissionResult.Errors);
        }

        var seedsResult = await _staticSeedStep.ExecuteAsync(emissionResult.Value, cancellationToken).ConfigureAwait(false);
        if (seedsResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(seedsResult.Errors);
        }

        var finalState = seedsResult.Value;

        finalState.Log.Record(
            "pipeline.completed",
            "Build-SSDT pipeline completed successfully.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["manifestPath"] = Path.Combine(request.OutputDirectory, "manifest.json"),
                ["decisionLogPath"] = finalState.DecisionLogPath,
                ["seedScriptPaths"] = finalState.StaticSeedScriptPaths.IsDefaultOrEmpty ? "<none>" : string.Join(";", finalState.StaticSeedScriptPaths),
                ["cacheDirectory"] = finalState.EvidenceCache?.CacheDirectory
            });

        return new BuildSsdtPipelineResult(
            finalState.Bootstrap.Profile!,
            finalState.Report,
            finalState.Manifest,
            finalState.DecisionLogPath,
            finalState.StaticSeedScriptPaths,
            finalState.EvidenceCache,
            finalState.Log.Build(),
            finalState.Bootstrap.Warnings);
    }
}
