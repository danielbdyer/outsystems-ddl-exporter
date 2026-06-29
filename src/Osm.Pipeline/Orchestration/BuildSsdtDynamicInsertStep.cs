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

public sealed class BuildSsdtDynamicInsertStep : IBuildSsdtStep<BuildSsdtState, BuildSsdtState>
{
    public BuildSsdtDynamicInsertStep(
        DynamicEntityInsertGenerator generator,
        PhasedDynamicEntityInsertGenerator phasedGenerator)
    {
        _ = generator ?? throw new ArgumentNullException(nameof(generator));
        _ = phasedGenerator ?? throw new ArgumentNullException(nameof(phasedGenerator));
    }

    public Task<Result<BuildSsdtState>> ExecuteAsync(
        BuildSsdtState state,
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

        return Task.FromResult(Result<BuildSsdtState>.Success(state with
        {
            DynamicInsertScriptPaths = ImmutableArray<string>.Empty,
            DynamicInsertOutputMode = state.Request.DynamicInsertOutputMode,
            DynamicInsertTopologicalOrderApplied = false,
            DynamicInsertOrderingMode = EntityDependencyOrderingMode.Alphabetical,
        }));
    }
}
