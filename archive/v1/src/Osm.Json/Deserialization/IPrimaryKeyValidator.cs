namespace Osm.Json.Deserialization;

using System.Collections.Immutable;
using Osm.Domain.Model;

internal interface IPrimaryKeyValidator
{
    EntityDocumentMapper.HelperResult<bool> Validate(
        EntityDocumentMapper.MapContext mapContext,
        ImmutableArray<AttributeModel> attributes);
}
