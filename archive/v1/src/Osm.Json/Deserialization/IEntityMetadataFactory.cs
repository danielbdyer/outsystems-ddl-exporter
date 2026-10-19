namespace Osm.Json.Deserialization;

using System.Collections.Immutable;
using Osm.Domain.Model;

internal interface IEntityMetadataFactory
{
    EntityDocumentMapper.HelperResult<EntityMetadata> Create(
        EntityDocumentMapper.MapContext mapContext,
        string? description,
        ImmutableArray<ExtendedProperty> extendedProperties,
        TemporalTableMetadata temporal);
}
