using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using SequenceDocument = ModelJsonDeserializer.SequenceDocument;

internal sealed class SequenceDocumentMapper
{
    private readonly DocumentMapperContext _context;
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;

    public SequenceDocumentMapper(DocumentMapperContext context, ExtendedPropertyDocumentMapper extendedPropertyMapper)
    {
        _context = context;
        _extendedPropertyMapper = extendedPropertyMapper;
    }

    public Result<ImmutableArray<SequenceModel>> Map(SequenceDocument[]? docs, DocumentPathContext path)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<SequenceModel>>.Success(ImmutableArray<SequenceModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<SequenceModel>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc is null)
            {
                continue;
            }

            var sequencePath = path.Index(i);
            var schemaResult = SchemaName.Create(doc.Schema);
            if (schemaResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(
                    _context.WithPath(sequencePath.Property("schema"), schemaResult.Errors));
            }

            var nameResult = SequenceName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(
                    _context.WithPath(sequencePath.Property("name"), nameResult.Errors));
            }

            var propertiesResult = _extendedPropertyMapper.Map(
                doc.ExtendedProperties,
                sequencePath.Property("extendedProperties"));
            if (propertiesResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(propertiesResult.Errors);
            }

            var modelResult = SequenceModel.Create(
                schemaResult.Value,
                nameResult.Value,
                doc.DataType,
                doc.StartValue,
                doc.Increment,
                doc.MinValue,
                doc.MaxValue,
                doc.Cycle,
                ParseSequenceCacheMode(doc.CacheMode),
                doc.CacheSize,
                propertiesResult.Value);

            if (modelResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(
                    _context.WithPath(sequencePath, modelResult.Errors));
            }

            builder.Add(modelResult.Value);
        }

        return Result<ImmutableArray<SequenceModel>>.Success(builder.ToImmutable());
    }

    private static SequenceCacheMode ParseSequenceCacheMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SequenceCacheMode.Unspecified;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "cache" or "cached" => SequenceCacheMode.Cache,
            "nocache" or "no-cache" or "no_cache" => SequenceCacheMode.NoCache,
            _ => SequenceCacheMode.UnsupportedYet,
        };
    }
}
