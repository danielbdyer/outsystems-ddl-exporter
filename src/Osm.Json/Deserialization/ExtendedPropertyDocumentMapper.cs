using System.Collections.Immutable;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;

namespace Osm.Json.Deserialization;

using ExtendedPropertyDocument = ModelJsonDeserializer.ExtendedPropertyDocument;

internal sealed class ExtendedPropertyDocumentMapper
{
    public Result<ImmutableArray<ExtendedProperty>> Map(ExtendedPropertyDocument[]? docs)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<ExtendedProperty>>.Success(ExtendedProperty.EmptyArray);
        }

        var builder = ImmutableArray.CreateBuilder<ExtendedProperty>(docs.Length);
        foreach (var doc in docs)
        {
            if (doc is null)
            {
                continue;
            }

            var value = doc.Value.ValueKind switch
            {
                JsonValueKind.Undefined => null,
                JsonValueKind.Null => null,
                JsonValueKind.String => doc.Value.GetString(),
                _ => doc.Value.GetRawText(),
            };

            var propertyResult = ExtendedProperty.Create(doc.Name, value);
            if (propertyResult.IsFailure)
            {
                return Result<ImmutableArray<ExtendedProperty>>.Failure(propertyResult.Errors);
            }

            builder.Add(propertyResult.Value);
        }

        return Result<ImmutableArray<ExtendedProperty>>.Success(builder.ToImmutable());
    }
}
