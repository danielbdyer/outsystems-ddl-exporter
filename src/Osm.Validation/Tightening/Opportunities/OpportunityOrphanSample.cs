using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening.Opportunities;

public sealed record OpportunityOrphanSample(
    ImmutableArray<string> PrimaryKeyColumns,
    string ForeignKeyColumn,
    ImmutableArray<OpportunityOrphanRow> Rows,
    long TotalOrphans,
    bool IsTruncated)
{
    public int DisplayedRowCount => Rows.Length;

    public static OpportunityOrphanSample FromDomain(ForeignKeyOrphanSample sample)
    {
        if (sample is null)
        {
            throw new ArgumentNullException(nameof(sample));
        }

        var rows = sample.SampleRows
            .Select(OpportunityOrphanRow.FromDomain)
            .ToImmutableArray();

        return new OpportunityOrphanSample(
            sample.PrimaryKeyColumns,
            sample.ForeignKeyColumn,
            rows,
            sample.TotalOrphans,
            sample.IsTruncated);
    }
}

public sealed record OpportunityOrphanRow(
    ImmutableArray<object?> PrimaryKeyValues,
    object? ForeignKeyValue,
    string Display)
{
    public static OpportunityOrphanRow FromDomain(ForeignKeyOrphanIdentifier identifier)
    {
        if (identifier is null)
        {
            throw new ArgumentNullException(nameof(identifier));
        }

        return new OpportunityOrphanRow(
            identifier.PrimaryKeyValues,
            identifier.ForeignKeyValue,
            identifier.ToString());
    }
}
