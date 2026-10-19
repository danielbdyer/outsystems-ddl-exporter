using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

public class IndexDocumentMapperTests
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
    public void Map_ShouldMaterializeOnDiskMetadata()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var mapper = new IndexDocumentMapper(context, extendedPropertyMapper);

        var indexDocument = new ModelJsonDeserializer.IndexDocument
        {
            Name = "PK_OSUSR_CUSTOMER",
            Kind = "PK",
            IsPrimary = true,
            IsUnique = true,
            Columns = new[]
            {
                new ModelJsonDeserializer.IndexColumnDocument
                {
                    Attribute = "Id",
                    PhysicalColumn = "ID",
                    Ordinal = 1,
                    Direction = "ASC"
                }
            },
            PartitionColumns = new[]
            {
                new ModelJsonDeserializer.IndexPartitionColumnDocument
                {
                    Name = "TenantId",
                    Ordinal = 1
                }
            },
            DataCompression = new[]
            {
                new ModelJsonDeserializer.IndexPartitionCompressionDocument
                {
                    Partition = 1,
                    Compression = "ROW"
                }
            },
            ExtendedProperties = Array.Empty<ModelJsonDeserializer.ExtendedPropertyDocument>()
        };

        var result = mapper.Map(
            new[] { indexDocument },
            DocumentPathContext.Root.Property("indexes"));

        Assert.True(result.IsSuccess);
        var index = Assert.Single(result.Value);
        Assert.True(index.IsPrimary);
        var column = Assert.Single(index.Columns);
        Assert.Equal(IndexColumnDirection.Ascending, column.Direction);
        var partition = Assert.Single(index.OnDisk.PartitionColumns);
        Assert.Equal("TenantId", partition.Column.Value);
        var compression = Assert.Single(index.OnDisk.DataCompression);
        Assert.Equal(1, compression.PartitionNumber);
    }

    [Fact]
    public void Map_ShouldFail_WhenColumnsMissing()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var mapper = new IndexDocumentMapper(context, extendedPropertyMapper);

        var indexDocument = new ModelJsonDeserializer.IndexDocument
        {
            Name = "IX_OSUSR_CUSTOMER_NAME",
            Columns = null
        };

        var result = mapper.Map(
            new[] { indexDocument },
            DocumentPathContext.Root.Property("indexes"));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("index.columns.missing", error.Code);
    }
}
