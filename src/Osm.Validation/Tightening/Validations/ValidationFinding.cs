using System;
using System.Collections.Immutable;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening.Validations;

public sealed record ValidationFinding(
    OpportunityType Type,
    string Title,
    string Summary,
    ImmutableArray<string> Evidence,
    ImmutableArray<string> Rationales,
    ColumnCoordinate? Column,
    IndexCoordinate? Index,
    string? Schema,
    string? Table,
    string? ConstraintName,
    ImmutableArray<OpportunityColumn> Columns)
{
    public static ValidationFinding FromOpportunity(Opportunity opportunity)
    {
        if (opportunity is null)
        {
            throw new ArgumentNullException(nameof(opportunity));
        }

        return new ValidationFinding(
            opportunity.Type,
            opportunity.Title,
            opportunity.Summary,
            opportunity.Evidence,
            opportunity.Rationales,
            opportunity.Column,
            opportunity.Index,
            opportunity.Schema,
            opportunity.Table,
            opportunity.ConstraintName,
            opportunity.Columns);
    }
}
