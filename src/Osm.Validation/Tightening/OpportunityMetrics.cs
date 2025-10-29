using System;
using System.Collections.Generic;

namespace Osm.Validation.Tightening.Opportunities;

public sealed record OpportunityMetrics(
    int Total,
    int LowRisk,
    int ModerateRisk,
    int HighRisk,
    int UnknownRisk)
{
    public static OpportunityMetrics From(IEnumerable<Opportunity> opportunities)
    {
        if (opportunities is null)
        {
            return new OpportunityMetrics(0, 0, 0, 0, 0);
        }

        var low = 0;
        var moderate = 0;
        var high = 0;
        var unknown = 0;

        foreach (var opportunity in opportunities)
        {
            switch (opportunity.Risk.Level)
            {
                case RiskLevel.Low:
                    low++;
                    break;
                case RiskLevel.Moderate:
                    moderate++;
                    break;
                case RiskLevel.High:
                    high++;
                    break;
                default:
                    unknown++;
                    break;
            }
        }

        var total = low + moderate + high + unknown;
        return new OpportunityMetrics(total, low, moderate, high, unknown);
    }
}
