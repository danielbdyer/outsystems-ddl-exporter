using System.Collections.Generic;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class ProfilingPlanBuilderTests
{
    [Fact]
    public void BuildPlans_ShapesPlanWithColumnsUniquesAndRowCounts()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [("dbo", "OSUSR_U_USER", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_U_USER", "EMAIL")] = new ColumnMetadata(true, false, false, null)
        };
        var coordinate = TableCoordinate.Create("dbo", "OSUSR_U_USER").Value;
        var rowCounts = new Dictionary<TableCoordinate, long>(TableCoordinate.OrdinalIgnoreCaseComparer)
        {
            [coordinate] = 100
        };

        var builder = new ProfilingPlanBuilder(model);
        var plans = builder.BuildPlans(metadata, rowCounts);

        Assert.True(plans.TryGetValue(coordinate, out var plan));
        Assert.Equal(100, plan.RowCount);
        Assert.Equal(new[] { "EMAIL", "ID" }, plan.Columns);
        Assert.Single(plan.UniqueCandidates);
        Assert.Equal("email", plan.UniqueCandidates[0].Key);
        Assert.Equal(new[] { "EMAIL" }, plan.UniqueCandidates[0].Columns);
        Assert.Empty(plan.ForeignKeys);
    }
}
