using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Json.Deserialization;

internal interface IAttributeDeduplicator
{
    EntityDocumentMapper.HelperResult<ImmutableArray<AttributeModel>> Deduplicate(
        EntityDocumentMapper.MapContext mapContext,
        DocumentPathContext attributesPath,
        ImmutableArray<AttributeModel> attributes,
        ModelJsonDeserializer.AttributeDocument[] sourceDocuments);
}
