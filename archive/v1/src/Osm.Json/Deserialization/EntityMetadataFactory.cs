using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Json.Deserialization;

internal sealed class EntityMetadataFactory : IEntityMetadataFactory
{
    public EntityDocumentMapper.HelperResult<EntityMetadata> Create(
        EntityDocumentMapper.MapContext mapContext,
        string? description,
        ImmutableArray<ExtendedProperty> extendedProperties,
        TemporalTableMetadata temporal)
    {
        var metadata = EntityMetadata.Create(description, extendedProperties, temporal);
        return EntityDocumentMapper.HelperResult<EntityMetadata>.Success(mapContext, metadata);
    }
}
