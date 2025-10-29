using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening;

internal sealed class UniqueIndexDecisionService
{
    private readonly TighteningLookupContext _context;
    private readonly UniqueIndexDecisionStrategy _strategy;

    public UniqueIndexDecisionService(TighteningLookupContext context, UniqueIndexDecisionStrategy strategy)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    }

    public UniqueIndexDecisionResult Analyze(IReadOnlyDictionary<ColumnCoordinate, ColumnAnalysisBuilder> columnBuilders)
    {
        if (columnBuilders is null)
        {
            throw new ArgumentNullException(nameof(columnBuilders));
        }

        var decisionBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, UniqueIndexDecision>();
        var moduleBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, string>();

        foreach (var module in _context.Model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                foreach (var index in entity.Indexes)
                {
                    if (!index.IsUnique)
                    {
                        continue;
                    }

                    var analysis = _strategy.Evaluate(entity, index);
                    decisionBuilder[analysis.Index] = analysis.Decision;
                    moduleBuilder[analysis.Index] = entity.Module.Value;

                    foreach (var column in analysis.Columns)
                    {
                        if (!columnBuilders.TryGetValue(column, out var builder))
                        {
                            continue;
                        }

                        builder.AddUniqueDecision(analysis.Decision);

                        if (!ShouldCreateOpportunity(analysis))
                        {
                            continue;
                        }

                        var summary = BuildSummary(analysis);
                        var risk = ChangeRiskClassifier.ForUniqueIndex(analysis);
                        var opportunity = Opportunity.Create(
                            OpportunityType.UniqueIndex,
                            "UNIQUE",
                            summary,
                            risk,
                            analysis.Rationales,
                            column: column,
                            index: analysis.Index,
                            disposition: OpportunityDisposition.NeedsRemediation);

                        builder.AddOpportunity(opportunity);
                    }
                }
            }
        }

        return new UniqueIndexDecisionResult(decisionBuilder.ToImmutable(), moduleBuilder.ToImmutable());
    }

    private static bool ShouldCreateOpportunity(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis)
        => !analysis.Decision.EnforceUnique || analysis.Decision.RequiresRemediation;

    private static string BuildSummary(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis)
    {
        if (analysis.Decision.RequiresRemediation)
        {
            return "Remediate data before enforcing the unique index.";
        }

        if (analysis.HasDuplicates)
        {
            return "Resolve duplicate values before enforcing the unique index.";
        }

        if (analysis.PolicyDisabled)
        {
            return "Enable policy support before enforcing the unique index.";
        }

        if (!analysis.HasEvidence)
        {
            return "Collect profiling evidence before enforcing the unique index.";
        }

        return "Review unique index enforcement before applying.";
    }
}

internal sealed record UniqueIndexDecisionResult(
    ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> UniqueDecisions,
    ImmutableDictionary<IndexCoordinate, string> IndexModules);
