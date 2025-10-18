using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPolicyDecisionStep : IBuildSsdtStep<EvidencePrepared, DecisionsSynthesized>
{
    private readonly TighteningPolicy _tighteningPolicy;
    private readonly ITighteningAnalyzer _analyzer;

    public BuildSsdtPolicyDecisionStep(TighteningPolicy tighteningPolicy, ITighteningAnalyzer analyzer)
    {
        _tighteningPolicy = tighteningPolicy ?? throw new ArgumentNullException(nameof(tighteningPolicy));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
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
        var opportunities = _analyzer.Analyze(model, profile, decisions);

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
                ["foreignKeysCreated"] = report.ForeignKeysCreatedCount.ToString(CultureInfo.InvariantCulture),
                ["opportunities"] = opportunities.TotalCount.ToString(CultureInfo.InvariantCulture)
            });

        return Task.FromResult(Result<DecisionsSynthesized>.Success(new DecisionsSynthesized(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            decisions,
            report,
            opportunities,
            ImmutableArray<PipelineInsight>.Empty)));
    }
}
