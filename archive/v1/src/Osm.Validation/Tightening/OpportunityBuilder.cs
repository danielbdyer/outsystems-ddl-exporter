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
            return "Unique index was not enforced. Remediate data before enforcement can proceed.";
        }

        if (analysis.HasDuplicates)
        {
            return "Unique index was not enforced. Resolve duplicate values before enforcement can proceed.";
        }

        if (analysis.PolicyDisabled)
        {
            return "Unique index was not enforced. Enable policy support before enforcement can proceed.";
        }

        if (!analysis.HasEvidence)
        {
            return "Unique index was not enforced. Collect profiling evidence before enforcement can proceed.";
        }

        return "Unique index was not enforced. Review policy requirements before enforcement can proceed.";
    }
}

