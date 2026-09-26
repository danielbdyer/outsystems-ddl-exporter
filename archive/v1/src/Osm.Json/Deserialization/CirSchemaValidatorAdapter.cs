using System.Text.Json;
using Osm.Domain.Abstractions;

namespace Osm.Json.Deserialization;

internal sealed class CirSchemaValidatorAdapter : IModelDocumentSchemaValidator
{
    public Result<bool> Validate(JsonElement root)
        => CirSchemaValidator.Validate(root);
}
