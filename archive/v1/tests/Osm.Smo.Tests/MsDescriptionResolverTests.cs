using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class MsDescriptionResolverTests
{
    [Fact]
    public void ResolveEntityMetadata_prefers_ms_description_property()
    {
        var extendedProperty = ExtendedProperty.Create("MS_Description", "Entity override").Value;
        var metadata = EntityMetadata.Create("Entity meta", new[] { extendedProperty });

        var description = MsDescriptionResolver.Resolve(metadata);

        Assert.Equal("Entity override", description);
    }

    [Fact]
    public void ResolveAttributeMetadata_falls_back_to_description()
    {
        var metadata = AttributeMetadata.Create(" Attribute description \t\n", extendedProperties: null);

        var description = MsDescriptionResolver.Resolve(metadata);

        Assert.Equal("Attribute description", description);
    }

    [Fact]
    public void ResolveIndex_uses_extended_property_value()
    {
        var name = IndexName.Create("IX_Test").Value;
        var column = IndexColumnModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            ordinal: 1,
            isIncluded: false,
            direction: IndexColumnDirection.Ascending).Value;

        var extendedProperty = ExtendedProperty.Create("MS_Description", "Index description").Value;
        var index = IndexModel.Create(
            name,
            isUnique: false,
            isPrimary: false,
            isPlatformAuto: false,
            new[] { column },
            extendedProperties: new[] { extendedProperty }).Value;

        var description = MsDescriptionResolver.Resolve(index);

        Assert.Equal("Index description", description);
    }
}
