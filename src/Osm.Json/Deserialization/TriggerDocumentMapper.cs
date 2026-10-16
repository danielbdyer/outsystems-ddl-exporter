using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using TriggerDocument = ModelJsonDeserializer.TriggerDocument;

internal sealed class TriggerDocumentMapper
{
    private readonly DocumentMapperContext _context;

    public TriggerDocumentMapper(DocumentMapperContext context)
    {
        _context = context;
    }

    public Result<ImmutableArray<TriggerModel>> Map(TriggerDocument[]? docs, DocumentPathContext path)
        => _context.MapArray<TriggerDocument, TriggerModel>(docs, path, (doc, triggerPath) =>
        {
            var nameResult = TriggerName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<TriggerModel>.Failure(
                    _context.WithPath(triggerPath.Property("name"), nameResult.Errors));
            }

            var triggerResult = TriggerModel.Create(nameResult.Value, doc.IsDisabled, doc.Definition);
            return triggerResult.IsFailure
                ? Result<TriggerModel>.Failure(_context.WithPath(triggerPath, triggerResult.Errors))
                : triggerResult;
        });
}
