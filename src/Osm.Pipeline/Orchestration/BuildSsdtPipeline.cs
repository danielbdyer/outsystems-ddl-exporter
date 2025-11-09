using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Emission;
using Osm.Pipeline.Mediation;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPipeline : ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>
{
    private readonly TimeProvider _timeProvider;
    private readonly BuildSsdtBootstrapStep _bootstrapStep;
    private readonly BuildSsdtEvidenceCacheStep _evidenceCacheStep;
    private readonly BuildSsdtPolicyDecisionStep _policyStep;
    private readonly BuildSsdtEmissionStep _emissionStep;
    private readonly BuildSsdtSqlValidationStep _sqlValidationStep;
    private readonly BuildSsdtStaticSeedStep _staticSeedStep;
    private readonly BuildSsdtDynamicInsertStep _dynamicInsertStep;
    private readonly BuildSsdtTelemetryPackagingStep _telemetryPackagingStep;

    public BuildSsdtPipeline(
        TimeProvider timeProvider,
        BuildSsdtBootstrapStep bootstrapStep,
        BuildSsdtEvidenceCacheStep evidenceCacheStep,
        BuildSsdtPolicyDecisionStep policyStep,
        BuildSsdtEmissionStep emissionStep,
        BuildSsdtSqlValidationStep sqlValidationStep,
        BuildSsdtStaticSeedStep staticSeedStep,
        BuildSsdtDynamicInsertStep dynamicInsertStep,
        BuildSsdtTelemetryPackagingStep telemetryPackagingStep)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _bootstrapStep = bootstrapStep ?? throw new ArgumentNullException(nameof(bootstrapStep));
        _evidenceCacheStep = evidenceCacheStep ?? throw new ArgumentNullException(nameof(evidenceCacheStep));
        _policyStep = policyStep ?? throw new ArgumentNullException(nameof(policyStep));
        _emissionStep = emissionStep ?? throw new ArgumentNullException(nameof(emissionStep));
        _sqlValidationStep = sqlValidationStep ?? throw new ArgumentNullException(nameof(sqlValidationStep));
        _staticSeedStep = staticSeedStep ?? throw new ArgumentNullException(nameof(staticSeedStep));
        _dynamicInsertStep = dynamicInsertStep ?? throw new ArgumentNullException(nameof(dynamicInsertStep));
        _telemetryPackagingStep = telemetryPackagingStep ?? throw new ArgumentNullException(nameof(telemetryPackagingStep));
    }

    public async Task<Result<BuildSsdtPipelineResult>> HandleAsync(
        BuildSsdtPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Scope.ModelPath))
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
            .BindAsync((emission, token) => _sqlValidationStep.ExecuteAsync(emission, token), cancellationToken)
            .BindAsync((validation, token) => _staticSeedStep.ExecuteAsync(validation, token), cancellationToken)
            .BindAsync((seeds, token) => _dynamicInsertStep.ExecuteAsync(seeds, token), cancellationToken)
            .BindAsync((dynamicInserts, token) => _telemetryPackagingStep.ExecuteAsync(dynamicInserts, token), cancellationToken)
            .ConfigureAwait(false);

        if (finalStateResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(finalStateResult.Errors);
        }

        var finalState = finalStateResult.Value;

        finalState.Log.Record(
            "pipeline.completed",
            "Build-SSDT pipeline completed successfully.",
            new PipelineLogMetadataBuilder()
                .WithPath("manifest", Path.Combine(request.OutputDirectory, "manifest.json"))
                .WithPath("decisionLog", finalState.DecisionLogPath)
                .WithPath("opportunities", finalState.OpportunityArtifacts.ReportPath)
                .WithPath("validations", finalState.OpportunityArtifacts.ValidationsPath)
                .WithValue(
                    "outputs.seedScripts",
                    finalState.StaticSeedScriptPaths.IsDefaultOrEmpty
                        ? "<none>"
                        : string.Join(";", finalState.StaticSeedScriptPaths))
                .WithValue(
                    "outputs.dynamicInsertScripts",
                    finalState.DynamicInsertScriptPaths.IsDefaultOrEmpty
                        ? "<none>"
                        : string.Join(";", finalState.DynamicInsertScriptPaths))
                .WithValue(
                    "outputs.telemetryPackages",
                    finalState.TelemetryPackagePaths.IsDefaultOrEmpty
                        ? "<none>"
                        : string.Join(";", finalState.TelemetryPackagePaths))
                .WithPath("cache.directory", finalState.EvidenceCache?.CacheDirectory)
                .Build());

        var moduleManifestRollups = BuildModuleManifestRollups(finalState.Manifest);

        return new BuildSsdtPipelineResult(
            finalState.Bootstrap.Profile!,
            finalState.Bootstrap.Insights,
            finalState.Report,
            finalState.Opportunities,
            finalState.Validations,
            finalState.Manifest,
            moduleManifestRollups,
            finalState.Insights,
            finalState.DecisionLogPath,
            finalState.OpportunityArtifacts.ReportPath,
            finalState.OpportunityArtifacts.ValidationsPath,
            finalState.OpportunityArtifacts.SafeScriptPath,
            finalState.OpportunityArtifacts.SafeScript,
            finalState.OpportunityArtifacts.RemediationScriptPath,
            finalState.OpportunityArtifacts.RemediationScript,
            finalState.StaticSeedScriptPaths,
            finalState.DynamicInsertScriptPaths,
            finalState.TelemetryPackagePaths,
            finalState.SqlValidation,
            finalState.EvidenceCache,
            finalState.Log.Build(),
            finalState.StaticSeedTopologicalOrderApplied,
            finalState.DynamicInsertTopologicalOrderApplied,
            finalState.Bootstrap.Warnings,
            finalState.Bootstrap.MultiEnvironmentReport);
    }

    private static ImmutableDictionary<string, ModuleManifestRollup> BuildModuleManifestRollups(SsdtManifest manifest)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var groups = manifest.Tables
            .GroupBy(table => table.Module, StringComparer.OrdinalIgnoreCase)
            .Select(group => new KeyValuePair<string, ModuleManifestRollup>(
                group.Key,
                new ModuleManifestRollup(
                    TableCount: group.Count(),
                    IndexCount: group.Sum(entry => entry.Indexes?.Count ?? 0),
                    ForeignKeyCount: group.Sum(entry => entry.ForeignKeys?.Count ?? 0))))
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return groups.Count == 0
            ? ImmutableDictionary<string, ModuleManifestRollup>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
            : groups;
    }
}
