using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class AttributeReferenceTests
{
    [Fact]
    public void Create_ShouldReturnNone_WhenIsReferenceFalse()
    {
        var result = AttributeReference.Create(
            isReference: false,
            targetEntityId: null,
            targetEntity: null,
            targetPhysicalName: null,
            deleteRuleCode: null,
            hasDatabaseConstraint: null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReference);
    }

    [Fact]
    public void Create_ShouldFail_WhenReferenceMissingTargetNames()
    {
        var entityName = EntityName.Create("Customer").Value;

        var missingPhysical = AttributeReference.Create(
            isReference: true,
            targetEntityId: 10,
            targetEntity: entityName,
            targetPhysicalName: null,
            deleteRuleCode: null,
            hasDatabaseConstraint: null);

        Assert.True(missingPhysical.IsFailure);
        Assert.Contains(missingPhysical.Errors, e => e.Code == "attribute.reference.physical.missing");

        var missingLogical = AttributeReference.Create(
            isReference: true,
            targetEntityId: 10,
            targetEntity: null,
            targetPhysicalName: TableName.Create("OSUSR_CUST").Value,
            deleteRuleCode: null,
            hasDatabaseConstraint: null);

        Assert.True(missingLogical.IsFailure);
        Assert.Contains(missingLogical.Errors, e => e.Code == "attribute.reference.target.missing");
    }

    [Fact]
    public void Create_ShouldNormalizeDeleteRuleAndConstraint()
    {
        var result = AttributeReference.Create(
            isReference: true,
            targetEntityId: null,
            targetEntity: EntityName.Create("City").Value,
            targetPhysicalName: TableName.Create("OSUSR_CITY").Value,
            deleteRuleCode: " Protect ",
            hasDatabaseConstraint: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Protect", result.Value.DeleteRuleCode);
        Assert.False(result.Value.HasDatabaseConstraint);
    }
}
