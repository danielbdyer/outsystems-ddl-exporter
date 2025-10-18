using System;
using System.Collections.Generic;
using System.Linq;

namespace Osm.Validation.Tightening;

public sealed record ReportSummary(
    int ColumnCount,
    int ColumnsWithOpportunities,
    OpportunityMetrics Metrics)
{
    public static ReportSummary From(IEnumerable<ColumnAnalysis> analyses)
    {
        if (analyses is null)
        {
            return new ReportSummary(0, 0, new OpportunityMetrics(0, 0, 0, 0, 0));
        }

        var list = analyses.ToList();
        var metrics = OpportunityMetrics.From(list.SelectMany(static a => a.Opportunities));
        var withOpportunities = list.Count(static a => a.Opportunities.Length > 0);

        return new ReportSummary(list.Count, withOpportunities, metrics);
    }
}
