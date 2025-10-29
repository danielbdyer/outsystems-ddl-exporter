using System;
using System.Collections.Immutable;
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

        state.Log.Record(
            "policy.decisions.synthesized",
            "Synthesized tightening decisions.",
            new PipelineLogMetadataBuilder()
                .WithCount("columns.total", report.ColumnCount)
                .WithCount("columns.tightened", report.TightenedColumnCount)
                .WithCount("columns.remediation", report.RemediationColumnCount)
                .WithCount("indexes.unique", report.UniqueIndexCount)
                .WithCount("indexes.uniqueEnforced", report.UniqueIndexesEnforcedCount)
                .WithCount("foreignKeys.total", report.ForeignKeyCount)
                .WithCount("foreignKeys.created", report.ForeignKeysCreatedCount)
                .WithCount("opportunities.total", opportunities.TotalCount)
                .WithCount("modules", report.ModuleCount)
                .Build());

        var moduleInsights = BuildModuleInsights(report);

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
        if (report.ModuleRollups.IsEmpty)
        {
            return ImmutableArray<PipelineInsight>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<PipelineInsight>(report.ModuleRollups.Count);
        foreach (var pair in report.ModuleRollups.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var module = pair.Key;
            var rollup = pair.Value;
            var summaryParts = new List<string>();

            if (rollup.TightenedColumnCount > 0)
            {
                summaryParts.Add($"{rollup.TightenedColumnCount} tightened columns");
            }

            if (rollup.UniqueIndexesEnforcedCount > 0)
            {
                summaryParts.Add($"{rollup.UniqueIndexesEnforcedCount} unique indexes enforced");
            }

            if (rollup.ForeignKeysCreatedCount > 0)
            {
                summaryParts.Add($"{rollup.ForeignKeysCreatedCount} foreign keys created");
            }

            if (summaryParts.Count == 0)
            {
                summaryParts.Add("No tightening actions applied");
            }

            var summary = string.Join(", ", summaryParts);
            var rationaleSummary = BuildRationaleSummary(rollup);

            builder.Add(new PipelineInsight(
                code: $"policy.module.{module}",
                title: $"{module} tightening rollup",
                summary: summary,
                severity: PipelineInsightSeverity.Info,
                affectedObjects: rationaleSummary,
                suggestedAction: "Review module rationale breakdown in manifest for detailed audit."));
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<string> BuildRationaleSummary(ModuleDecisionRollup rollup)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        AppendTopRationales(builder, "Columns", rollup.ColumnRationales);
        AppendTopRationales(builder, "Unique", rollup.UniqueIndexRationales);
        AppendTopRationales(builder, "ForeignKeys", rollup.ForeignKeyRationales);
        return builder.ToImmutable();
    }

    private static void AppendTopRationales(
        ImmutableArray<string>.Builder builder,
        string category,
        ImmutableDictionary<string, int> rationales)
    {
        if (rationales.IsEmpty)
        {
            return;
        }

        var top = rationales
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(static pair => $"{pair.Key}={pair.Value}");

        builder.Add($"{category}: {string.Join(", ", top)}");
    }
}
