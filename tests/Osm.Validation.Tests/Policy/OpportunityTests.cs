using System.Collections.Generic;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class OpportunityTests
{
    private static readonly ColumnCoordinate Column = new(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("Id"));

    [Fact]
    public void Create_SortsAndDeduplicatesEvidence()
    {
        var evidence = new List<string> { "B", "A", "B", "" };

        var opportunity = Opportunity.Create(
            OpportunityCategory.Nullability,
            "NOT NULL",
            "Collect more evidence before tightening.",
            ChangeRisk.Moderate("Missing profiling evidence."),
            evidence,
            column: Column);

        Assert.Equal(new[] { "A", "B" }, opportunity.Evidence);
    }
}
