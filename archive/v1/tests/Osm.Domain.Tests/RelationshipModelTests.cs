using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class RelationshipModelTests
{
    [Fact]
    public void Create_ShouldDefaultDeleteRuleToIgnore_WhenNull()
    {
        var via = AttributeName.Create("CustomerId").Value;
        var target = EntityName.Create("Customer").Value;
        var physical = TableName.Create("OSUSR_CUSTOMER").Value;

        var result = RelationshipModel.Create(via, target, physical, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ignore", result.Value.DeleteRuleCode);
        Assert.False(result.Value.HasDatabaseConstraint);
    }

    [Fact]
    public void Create_ShouldTrimDeleteRuleAndPreserveConstraint()
    {
        var via = AttributeName.Create("CityId").Value;
        var target = EntityName.Create("City").Value;
        var physical = TableName.Create("OSUSR_CITY").Value;

        var result = RelationshipModel.Create(via, target, physical, " Protect ", true);

        Assert.True(result.IsSuccess);
        Assert.Equal("Protect", result.Value.DeleteRuleCode);
        Assert.True(result.Value.HasDatabaseConstraint);
    }
}
