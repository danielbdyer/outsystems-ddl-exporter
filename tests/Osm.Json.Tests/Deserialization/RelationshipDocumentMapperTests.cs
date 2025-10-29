using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

public class RelationshipDocumentMapperTests
{
    private static DocumentMapperContext CreateContext(List<string> warnings)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return new DocumentMapperContext(ModelJsonDeserializerOptions.Default, warnings, serializerOptions);
    }

    [Fact]
    public void Map_ShouldRespectHasConstraintFlag()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var mapper = new RelationshipDocumentMapper(context);

        var document = new ModelJsonDeserializer.RelationshipDocument
        {
            ViaAttributeName = "CustomerId",
            TargetEntityName = "Customer",
            TargetEntityPhysicalName = "OSUSR_CUSTOMER",
            HasDbConstraint = 0
        };

        var result = mapper.Map(
            new[] { document },
            DocumentPathContext.Root.Property("relationships"));

        Assert.True(result.IsSuccess);
        var relationship = Assert.Single(result.Value);
        Assert.False(relationship.HasDatabaseConstraint);
    }

    [Fact]
    public void Map_ShouldMaterializeActualConstraints()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var mapper = new RelationshipDocumentMapper(context);

        var document = new ModelJsonDeserializer.RelationshipDocument
        {
            ViaAttributeName = "CustomerId",
            TargetEntityName = "Customer",
            TargetEntityPhysicalName = "OSUSR_CUSTOMER",
            ActualConstraints = new[]
            {
                new ModelJsonDeserializer.RelationshipConstraintDocument
                {
                    Name = "FK_OSUSR_ORDER_CUSTOMER",
                    ReferencedSchema = "dbo",
                    ReferencedTable = "OSUSR_CUSTOMER",
                    Columns = new[]
                    {
                        new ModelJsonDeserializer.RelationshipConstraintColumnDocument
                        {
                            OwnerPhysical = "CUSTOMERID",
                            OwnerAttribute = "CustomerId",
                            ReferencedPhysical = "ID",
                            ReferencedAttribute = "Id",
                            Ordinal = 1
                        }
                    }
                }
            }
        };

        var result = mapper.Map(
            new[] { document },
            DocumentPathContext.Root.Property("relationships"));

        Assert.True(result.IsSuccess);
        var relationship = Assert.Single(result.Value);
        var constraint = Assert.Single(relationship.ActualConstraints);
        Assert.Equal("FK_OSUSR_ORDER_CUSTOMER", constraint.Name);
        var column = Assert.Single(constraint.Columns);
        Assert.Equal("CUSTOMERID", column.OwnerColumn);
        Assert.Equal("ID", column.ReferencedColumn);
    }
}
