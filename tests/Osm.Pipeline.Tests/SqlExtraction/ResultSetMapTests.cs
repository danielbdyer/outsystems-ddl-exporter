using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.SqlExtraction;
using Xunit;

namespace Osm.Pipeline.Tests.SqlExtraction;

public class ResultSetMapTests
{
    [Fact]
    public void EntitiesDescriptor_ShouldExposeColumnMetadataInOrdinalOrder()
    {
        var descriptor = EntitiesResultSetProcessor.Descriptor;

        var expectedNames = new[]
        {
            "EntityId",
            "EntityName",
            "PhysicalTableName",
            "EspaceId",
            "EntityIsActive",
            "IsSystemEntity",
            "IsExternalEntity",
            "DataKind",
            "PrimaryKeySSKey",
            "EntitySSKey",
            "EntityDescription"
        };

        Assert.Equal(expectedNames, descriptor.Columns.Select(column => column.Name));
        Assert.Equal(Enumerable.Range(0, expectedNames.Length), descriptor.Columns.Select(column => column.Ordinal));
    }

    [Fact]
    public void AttributesDescriptor_ShouldDescribeNullability()
    {
        var descriptor = AttributesResultSetProcessor.Descriptor;
        var nullability = descriptor.Columns.ToDictionary(column => column.Name, column => column.AllowsNull, StringComparer.Ordinal);

        Assert.False(nullability["AttrId"]);
        Assert.True(nullability["AttrSSKey"]);
        Assert.True(nullability["DataType"]);
        Assert.False(nullability["IsMandatory"]);
        Assert.True(nullability["AttrDescription"]);
    }

    [Fact]
    public async Task EntitiesProcessor_ShouldPopulateAccumulator()
    {
        var processor = new EntitiesResultSetProcessor();
        var accumulator = new MetadataAccumulator();

        var entityId = 42;
        var primaryKey = Guid.Parse("00000000-0000-0000-0000-000000000042");
        var entityKey = Guid.Parse("00000000-0000-0000-0000-000000000099");

        var rows = new[]
        {
            new object?[]
            {
                entityId,
                "Customer",
                "OSUSR_ABC_CUSTOMER",
                99,
                true,
                false,
                false,
                "Static",
                primaryKey,
                entityKey,
                "CRM entity"
            }
        };

        var columnNames = new[]
        {
            "EntityId",
            "EntityName",
            "PhysicalTableName",
            "EspaceId",
            "EntityIsActive",
            "IsSystemEntity",
            "IsExternalEntity",
            "DataKind",
            "PrimaryKeySSKey",
            "EntitySSKey",
            "EntityDescription"
        };

        using var reader = new SingleResultSetDataReader(rows, columnNames);
        var context = new ResultSetProcessingContext(reader, accumulator);

        var count = await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Equal(1, count);
        var entity = Assert.Single(accumulator.Entities);
        Assert.Equal(entityId, entity.EntityId);
        Assert.Equal("Customer", entity.EntityName);
        Assert.Equal("Static", entity.DataKind);
        Assert.Equal("CRM entity", entity.EntityDescription);
        Assert.Equal(primaryKey, entity.PrimaryKeySsKey);
        Assert.Equal(entityKey, entity.EntitySsKey);
    }

    [Fact]
    public async Task AttributesProcessor_ShouldPreserveNullableFields()
    {
        var processor = new AttributesResultSetProcessor();
        var accumulator = new MetadataAccumulator();

        var rows = new[]
        {
            new object?[]
            {
                7,
                42,
                "CustomerName",
                Guid.Empty,
                null,
                null,
                10,
                null,
                null,
                true,
                true,
                true,
                null,
                null,
                "OriginalName",
                null,
                "Cascade",
                "PHYSICAL_COLUMN",
                "DB_COLUMN",
                null,
                null,
                "OriginalType",
                null
            }
        };

        var columnNames = new[]
        {
            "AttrId",
            "EntityId",
            "AttrName",
            "AttrSSKey",
            "DataType",
            "Length",
            "Precision",
            "Scale",
            "DefaultValue",
            "IsMandatory",
            "AttrIsActive",
            "IsAutoNumber",
            "IsIdentifier",
            "RefEntityId",
            "OriginalName",
            "ExternalColumnType",
            "DeleteRule",
            "PhysicalColumnName",
            "DatabaseColumnName",
            "LegacyType",
            "Decimals",
            "OriginalType",
            "AttrDescription"
        };

        using var reader = new SingleResultSetDataReader(rows, columnNames);
        var context = new ResultSetProcessingContext(reader, accumulator);

        var count = await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Equal(1, count);
        var attribute = Assert.Single(accumulator.Attributes);
        Assert.Null(attribute.DataType);
        Assert.Null(attribute.Length);
        Assert.Equal(10, attribute.Precision);
        Assert.Null(attribute.IsIdentifier);
        Assert.Null(attribute.RefEntityId);
        Assert.Equal("OriginalName", attribute.OriginalName);
        Assert.Equal("Cascade", attribute.DeleteRule);
        Assert.Equal("PHYSICAL_COLUMN", attribute.PhysicalColumnName);
        Assert.Equal("DB_COLUMN", attribute.DatabaseColumnName);
        Assert.Null(attribute.LegacyType);
        Assert.Null(attribute.Decimals);
        Assert.Equal("OriginalType", attribute.OriginalType);
        Assert.Null(attribute.AttrDescription);
    }
}
