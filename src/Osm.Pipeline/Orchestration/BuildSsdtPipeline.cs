using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Osm.Domain.Abstractions;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPipeline : ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>
{
    private readonly TimeProvider _timeProvider;
    private readonly BuildSsdtBootstrapStep _bootstrapStep;
    private readonly BuildSsdtEvidenceCacheStep _evidenceCacheStep;
    private readonly BuildSsdtPolicyDecisionStep _policyStep;
    private readonly BuildSsdtEmissionStep _emissionStep;
    private readonly BuildSsdtStaticSeedStep _staticSeedStep;

    public BuildSsdtPipeline(
        IPipelineBootstrapper? bootstrapper = null,
        TighteningPolicy? tighteningPolicy = null,
        SmoModelFactory? smoModelFactory = null,
        SsdtEmitter? ssdtEmitter = null,
        PolicyDecisionLogWriter? decisionLogWriter = null,
        IEvidenceCacheService? evidenceCacheService = null,
        StaticEntitySeedScriptGenerator? seedGenerator = null,
        StaticEntitySeedTemplate? seedTemplate = null,
        ProfileSnapshotDeserializer? profileSnapshotDeserializer = null,
        EmissionFingerprintCalculator? fingerprintCalculator = null,
        TimeProvider? timeProvider = null,
        IDataProfilerFactory? dataProfilerFactory = null)
    {
        var resolvedBootstrapper = bootstrapper ?? new PipelineBootstrapper();
        var resolvedTighteningPolicy = tighteningPolicy ?? new TighteningPolicy();
        var resolvedSmoModelFactory = smoModelFactory ?? new SmoModelFactory();
        var resolvedEmitter = ssdtEmitter ?? new SsdtEmitter();
        var resolvedDecisionLogWriter = decisionLogWriter ?? new PolicyDecisionLogWriter();
        var resolvedCacheService = evidenceCacheService ?? new EvidenceCacheService();
        var resolvedSeedGenerator = seedGenerator ?? new StaticEntitySeedScriptGenerator();
        var resolvedSeedTemplate = seedTemplate ?? StaticEntitySeedTemplate.Load();
        var resolvedProfileDeserializer = profileSnapshotDeserializer ?? new ProfileSnapshotDeserializer();
        var resolvedFingerprintCalculator = fingerprintCalculator ?? new EmissionFingerprintCalculator();
        var resolvedProfilerFactory = dataProfilerFactory
            ?? new DataProfilerFactory(
                resolvedProfileDeserializer,
                static (connectionString, options) => new SqlConnectionFactory(connectionString, options));

        _bootstrapStep = new BuildSsdtBootstrapStep(resolvedBootstrapper, resolvedProfilerFactory);
        _evidenceCacheStep = new BuildSsdtEvidenceCacheStep(resolvedCacheService);
        _policyStep = new BuildSsdtPolicyDecisionStep(resolvedTighteningPolicy);
        _emissionStep = new BuildSsdtEmissionStep(resolvedSmoModelFactory, resolvedEmitter, resolvedDecisionLogWriter, resolvedFingerprintCalculator);
        _staticSeedStep = new BuildSsdtStaticSeedStep(resolvedSeedGenerator, resolvedSeedTemplate);
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        var finalStateResult = await _bootstrapStep
            .ExecuteAsync(initialized, cancellationToken)
            .BindAsync((bootstrap, token) => _evidenceCacheStep.ExecuteAsync(bootstrap, token), cancellationToken)
            .BindAsync((evidence, token) => _policyStep.ExecuteAsync(evidence, token), cancellationToken)
            .BindAsync((decisions, token) => _emissionStep.ExecuteAsync(decisions, token), cancellationToken)
            .BindAsync((emission, token) => _staticSeedStep.ExecuteAsync(emission, token), cancellationToken)
            .ConfigureAwait(false);

        if (finalStateResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(finalStateResult.Errors);
        }

        var finalState = finalStateResult.Value;

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
