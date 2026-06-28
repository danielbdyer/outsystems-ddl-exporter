using System.Collections.Immutable;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;

namespace Osm.Json.Deserialization;

using ExtendedPropertyDocument = ModelJsonDeserializer.ExtendedPropertyDocument;

internal sealed class ExtendedPropertyDocumentMapper
{
    private readonly DocumentMapperContext _context;

    public ExtendedPropertyDocumentMapper(DocumentMapperContext context)
    {
        _context = context;
    }

    public Result<ImmutableArray<ExtendedProperty>> Map(ExtendedPropertyDocument[]? docs, DocumentPathContext path)
        => _context.MapArray<ExtendedPropertyDocument, ExtendedProperty>(docs, path, (doc, propertyPath) =>
        {
            var value = doc.Value.ValueKind switch
            {
                JsonValueKind.Undefined => null,
                JsonValueKind.Null => null,
                JsonValueKind.String => doc.Value.GetString(),
                _ => doc.Value.GetRawText(),
            };

            var propertyResult = ExtendedProperty.Create(doc.Name, value);
            return propertyResult.IsFailure
                ? Result<ExtendedProperty>.Failure(_context.WithPath(propertyPath, propertyResult.Errors))
                : propertyResult;
        });
}
