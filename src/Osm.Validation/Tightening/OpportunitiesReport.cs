using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening;

public sealed record OpportunitiesReport(
    ImmutableArray<ColumnAnalysis> Columns,
    ReportSummary Summary)
{
    public static OpportunitiesReport Create(IEnumerable<ColumnAnalysis> analyses)
    {
        if (analyses is null)
        {
            return new OpportunitiesReport(ImmutableArray<ColumnAnalysis>.Empty, ReportSummary.From(Array.Empty<ColumnAnalysis>()));
        }

        var columnArray = analyses.ToImmutableArray();
        var summary = ReportSummary.From(columnArray);
        return new OpportunitiesReport(columnArray, summary);
    }
}
