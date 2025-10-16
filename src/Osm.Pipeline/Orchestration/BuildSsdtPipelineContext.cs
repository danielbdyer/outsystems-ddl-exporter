using System;
using System.Collections.Immutable;
using System.IO;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPipelineContext
{
    public BuildSsdtPipelineContext(BuildSsdtPipelineRequest request, PipelineExecutionLogBuilder log)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Log = log ?? throw new ArgumentNullException(nameof(log));
        TelemetryDirectory = Path.Combine(Request.OutputDirectory, "telemetry");
        PipelineLogPath = Path.Combine(TelemetryDirectory, PipelineExecutionLogWriter.LogFileName);
        PipelineWarningsPath = Path.Combine(TelemetryDirectory, PipelineExecutionLogWriter.WarningsFileName);
    }

    public BuildSsdtPipelineRequest Request { get; }

    public PipelineExecutionLogBuilder Log { get; }

    public string TelemetryDirectory { get; }

    public string PipelineLogPath { get; }

    public string PipelineWarningsPath { get; }

    public PipelineBootstrapContext? BootstrapContext { get; private set; }

    public EvidenceCacheResult? EvidenceCache { get; private set; }

    public PolicyDecisionSet? Decisions { get; private set; }

    public PolicyDecisionReport? DecisionReport { get; private set; }

    public SsdtManifest? Manifest { get; private set; }

    public string? DecisionLogPath { get; private set; }

    public ImmutableArray<string> StaticSeedScriptPaths { get; private set; } = ImmutableArray<string>.Empty;

    public OsmModel? FilteredModel => BootstrapContext?.FilteredModel;

    public ImmutableArray<EntityModel> SupplementalEntities => BootstrapContext?.SupplementalEntities ?? ImmutableArray<EntityModel>.Empty;

    public ProfileSnapshot? Profile => BootstrapContext?.Profile;

    public ImmutableArray<string> PipelineWarnings => BootstrapContext?.Warnings ?? ImmutableArray<string>.Empty;

    public void SetBootstrapContext(PipelineBootstrapContext context)
    {
        BootstrapContext = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void SetEvidenceCache(EvidenceCacheResult? cache)
    {
        EvidenceCache = cache;
    }

    public void SetPolicyDecisions(PolicyDecisionSet decisions, PolicyDecisionReport report)
    {
        Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        DecisionReport = report ?? throw new ArgumentNullException(nameof(report));
    }

    public void SetEmissionArtifacts(SsdtManifest manifest, string decisionLogPath)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(decisionLogPath))
        {
            throw new ArgumentException("Decision log path must be provided.", nameof(decisionLogPath));
        }

        DecisionLogPath = decisionLogPath;
    }

    public void SetStaticSeedScriptPaths(ImmutableArray<string> paths)
    {
        StaticSeedScriptPaths = paths;
    }
}
