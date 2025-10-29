using System;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening;

internal sealed class OpportunityBuilder
{
    public Opportunity? TryCreate(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis, ColumnCoordinate column)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (!ShouldCreateUniqueOpportunity(analysis))
        {
            return null;
        }

        var summary = BuildUniqueSummary(analysis);
        var risk = ChangeRiskClassifier.ForUniqueIndex(analysis);

        return Opportunity.Create(
            OpportunityType.UniqueIndex,
            "UNIQUE",
            summary,
            risk,
            analysis.Rationales,
            column: column,
            index: analysis.Index,
            disposition: OpportunityDisposition.NeedsRemediation);
    }

    private static bool ShouldCreateUniqueOpportunity(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis)
        => !analysis.Decision.EnforceUnique || analysis.Decision.RequiresRemediation;

    private static string BuildUniqueSummary(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis)
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

