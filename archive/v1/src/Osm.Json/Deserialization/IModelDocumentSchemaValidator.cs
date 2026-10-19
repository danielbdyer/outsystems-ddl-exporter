using System.Text.Json;
using Osm.Domain.Abstractions;

namespace Osm.Json.Deserialization;

internal interface IModelDocumentSchemaValidator
{
    Result<bool> Validate(JsonElement root);
}
