using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public class CheckConstraintModelTests
{
    [Fact]
    public void Create_ShouldFail_WhenDefinitionMissing()
    {
        var name = ConstraintName.Create("CK_Table_Positive").Value;
        var result = CheckConstraintModel.Create(name, "  ", isActive: true);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "checkConstraint.definition.invalid");
    }

    [Fact]
    public void Create_ShouldTrimDefinition_WhenValid()
    {
        var name = ConstraintName.Create("CK_Table_Positive").Value;
        var result = CheckConstraintModel.Create(name, " Amount > 0 ", isActive: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("Amount > 0", result.Value.Definition);
        Assert.True(result.Value.IsActive);
    }
}
