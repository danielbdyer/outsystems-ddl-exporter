namespace Osm.Json.Deserialization;

using Osm.Domain.ValueObjects;

internal interface IEntitySchemaResolver
{
    EntityDocumentMapper.HelperResult<SchemaName> Resolve(EntityDocumentMapper.MapContext mapContext);
}
