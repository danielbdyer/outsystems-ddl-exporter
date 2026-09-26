using System;

namespace Osm.Validation.Tightening;

public sealed record PolicyAnalysisResult(PolicyDecisionSet Decisions, OpportunitiesReport Report)
{
    public static PolicyAnalysisResult Create(PolicyDecisionSet decisions, OpportunitiesReport report)
    {
        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return new PolicyAnalysisResult(decisions, report);
    }
}
