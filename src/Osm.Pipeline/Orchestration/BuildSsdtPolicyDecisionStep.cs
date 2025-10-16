using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPolicyDecisionStep : IBuildSsdtStep<EvidencePrepared, DecisionsSynthesized>
{
    private readonly TighteningPolicy _tighteningPolicy;

    public BuildSsdtPolicyDecisionStep(TighteningPolicy tighteningPolicy)
    {
        _tighteningPolicy = tighteningPolicy ?? throw new ArgumentNullException(nameof(tighteningPolicy));
    }

    public Task<Result<DecisionsSynthesized>> ExecuteAsync(
        EvidencePrepared state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var model = state.Bootstrap.FilteredModel
            ?? throw new InvalidOperationException("Pipeline bootstrap step must execute before policy decisions.");
        var profile = state.Bootstrap.Profile
            ?? throw new InvalidOperationException("Profiling must complete before policy decisions.");

        var decisions = _tighteningPolicy.Decide(model, profile, state.Request.TighteningOptions);
        var report = PolicyDecisionReporter.Create(decisions);

        state.Log.Record(
            "policy.decisions.synthesized",
            "Synthesized tightening decisions.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["columns"] = report.ColumnCount.ToString(CultureInfo.InvariantCulture),
                ["tightenedColumns"] = report.TightenedColumnCount.ToString(CultureInfo.InvariantCulture),
                ["remediationColumns"] = report.RemediationColumnCount.ToString(CultureInfo.InvariantCulture),
                ["uniqueIndexes"] = report.UniqueIndexCount.ToString(CultureInfo.InvariantCulture),
                ["uniqueIndexesEnforced"] = report.UniqueIndexesEnforcedCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = report.ForeignKeyCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeysCreated"] = report.ForeignKeysCreatedCount.ToString(CultureInfo.InvariantCulture)
            });

        return Task.FromResult(Result<DecisionsSynthesized>.Success(new DecisionsSynthesized(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            decisions,
            report,
            ImmutableArray<PipelineInsight>.Empty)));
    }
}
