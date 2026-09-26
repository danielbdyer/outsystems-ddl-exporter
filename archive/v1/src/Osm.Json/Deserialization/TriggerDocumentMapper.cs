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
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<TriggerModel>>.Success(ImmutableArray<TriggerModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<TriggerModel>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc is null)
            {
                continue;
            }

            var triggerPath = path.Index(i);
            var nameResult = TriggerName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<ImmutableArray<TriggerModel>>.Failure(
                    _context.WithPath(triggerPath.Property("name"), nameResult.Errors));
            }

            var triggerResult = TriggerModel.Create(nameResult.Value, doc.IsDisabled, doc.Definition);
            if (triggerResult.IsFailure)
            {
                return Result<ImmutableArray<TriggerModel>>.Failure(
                    _context.WithPath(triggerPath, triggerResult.Errors));
            }

            builder.Add(triggerResult.Value);
        }

        return Result<ImmutableArray<TriggerModel>>.Success(builder.ToImmutable());
    }
}
