namespace Osm.Json.Deserialization;

using System.Collections.Immutable;
using Osm.Domain.Model;

internal interface IDuplicateWarningEmitter
{
    EntityDocumentMapper.HelperResult<EntityDocumentMapper.DuplicateAllowance> EmitWarnings(
        EntityDocumentMapper.MapContext mapContext,
        ImmutableArray<AttributeModel> attributes);
}
