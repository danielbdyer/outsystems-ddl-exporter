using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class AttributeModelTests
{
    private static AttributeName Logical(string value) => AttributeName.Create(value).Value;
    private static ColumnName Column(string value) => ColumnName.Create(value).Value;

    [Fact]
    public void Create_ShouldFail_WhenDataTypeMissing()
    {
        var result = AttributeModel.Create(
            Logical("Id"),
            Column("ID"),
            dataType: "  ",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "attribute.dataType.invalid");
    }

    [Fact]
    public void Create_ShouldFail_WhenLengthNegative()
    {
        var result = AttributeModel.Create(
            Logical("Email"),
            Column("EMAIL"),
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: -1);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "attribute.length.invalid");
    }

    [Fact]
    public void Create_ShouldFail_WhenPrecisionNegative()
    {
        var result = AttributeModel.Create(
            Logical("Amount"),
            Column("AMOUNT"),
            dataType: "Decimal",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            precision: -5);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "attribute.precision.invalid");
    }

    [Fact]
    public void Create_ShouldFail_WhenScaleNegative()
    {
        var result = AttributeModel.Create(
            Logical("Amount"),
            Column("AMOUNT"),
            dataType: "Decimal",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            scale: -2);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "attribute.scale.invalid");
    }

    [Fact]
    public void Create_ShouldReturnReferenceNone_WhenNotReference()
    {
        var result = AttributeModel.Create(
            Logical("Name"),
            Column("NAME"),
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Reference.IsReference);
    }

    [Fact]
    public void Create_ShouldTrimOptionalFields()
    {
        var reference = AttributeReference.Create(
            isReference: true,
            targetEntityId: 10,
            targetEntity: EntityName.Create(" City ").Value,
            targetPhysicalName: TableName.Create(" OSUSR_CITY ").Value,
            deleteRuleCode: " Protect ",
            hasDatabaseConstraint: true).Value;

        var result = AttributeModel.Create(
            Logical("CityId"),
            Column("CITYID"),
            dataType: " Identifier ",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: reference,
            originalName: " CityId ",
            defaultValue: " 0 ",
            externalDatabaseType: "  uniqueidentifier  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Identifier", result.Value.DataType);
        Assert.Equal("CityId", result.Value.OriginalName);
        Assert.Equal(" 0 ", result.Value.DefaultValue); // intentional: default preserves whitespace
        Assert.Equal("uniqueidentifier", result.Value.ExternalDatabaseType);
        Assert.Equal("Protect", result.Value.Reference.DeleteRuleCode);
    }
}
