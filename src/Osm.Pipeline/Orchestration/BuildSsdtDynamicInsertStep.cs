using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.DynamicData;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtDynamicInsertStep : IBuildSsdtStep<StaticSeedsGenerated, DynamicInsertsGenerated>
{
    public BuildSsdtDynamicInsertStep(
        DynamicEntityInsertGenerator generator,
        PhasedDynamicEntityInsertGenerator phasedGenerator)
    {
        _ = generator ?? throw new ArgumentNullException(nameof(generator));
        _ = phasedGenerator ?? throw new ArgumentNullException(nameof(phasedGenerator));
    }

    public Task<Result<DynamicInsertsGenerated>> ExecuteAsync(
        StaticSeedsGenerated state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        _ = cancellationToken;
        state.Log.Record(
            "dynamicData.deprecated",
            "DynamicData emission has been deprecated; skipping generation of dynamic insert scripts.",
            new PipelineLogMetadataBuilder()
                .WithValue("outputs.dynamicInsertPaths", string.Empty)
                .WithValue("outputs.dynamicInsertMode", state.Request.DynamicInsertOutputMode.ToString())
                .WithValue("ordering.mode", EntityDependencyOrderingMode.Alphabetical.ToMetadataValue())
                .Build());

        return Task.FromResult(Result<DynamicInsertsGenerated>.Success(new DynamicInsertsGenerated(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Validations,
            state.Insights,
            state.Manifest,
            state.DecisionLogPath,
            state.OpportunityArtifacts,
            state.SqlProjectPath,
            state.SqlValidation,
            state.StaticSeedScriptPaths,
            state.StaticSeedData,
            ImmutableArray<string>.Empty,
            state.Request.DynamicInsertOutputMode,
            state.StaticSeedTopologicalOrderApplied,
            state.StaticSeedOrderingMode,
            DynamicInsertTopologicalOrderApplied: false,
            DynamicInsertOrderingMode: EntityDependencyOrderingMode.Alphabetical)));
    }
}
