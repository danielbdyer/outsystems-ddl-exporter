using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
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

        var moduleInsights = BuildModuleInsights(report);

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
                ["modules"] = report.ModuleCount.ToString(CultureInfo.InvariantCulture),
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
            moduleInsights)));
    }

    private static ImmutableArray<PipelineInsight> BuildModuleInsights(PolicyDecisionReport report)
    {
        if (report.ModuleRollups.IsDefaultOrEmpty)
        {
            return ImmutableArray<PipelineInsight>.Empty;
        }

        var insights = ImmutableArray.CreateBuilder<PipelineInsight>();
        foreach (var pair in report.ModuleRollups.OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            insights.Add(CreateModuleInsight(pair.Key, pair.Value));
        }

        return insights.MoveToImmutable();
    }

    private static PipelineInsight CreateModuleInsight(string module, ModuleDecisionRollup rollup)
    {
        var baseline = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1} columns tightened, {2}/{3} unique indexes enforced, {4}/{5} foreign keys created.",
            rollup.TightenedColumnCount,
            rollup.ColumnCount,
            rollup.UniqueIndexesEnforcedCount,
            rollup.UniqueIndexCount,
            rollup.ForeignKeysCreatedCount,
            rollup.ForeignKeyCount);

        var rationaleSummary = FormatRationales(rollup.RationaleCounts);
        var summary = string.IsNullOrWhiteSpace(rationaleSummary)
            ? baseline
            : string.Concat(baseline, " Top rationales: ", rationaleSummary, ".");

        return new PipelineInsight(
            code: $"policy.module.{module}",
            title: $"Tightening outcomes for module '{module}'",
            summary: summary,
            severity: PipelineInsightSeverity.Info,
            affectedObjects: ImmutableArray.Create(module),
            suggestedAction: "Review emitted manifest and remediation guidance for this module.");
    }

    private static string FormatRationales(ImmutableDictionary<string, int> rationales)
    {
        if (rationales.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var top = rationales
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(static pair => $"{pair.Key} ({pair.Value.ToString(CultureInfo.InvariantCulture)})");

        return string.Join(", ", top);
    }
}
