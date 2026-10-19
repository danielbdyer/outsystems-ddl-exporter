using System;
using System.IO;
using System.Text;
using Osm.Domain.Abstractions;
using Osm.Emission;
using Osm.Json;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.UatUsers;

public interface IModelUserSchemaGraphFactory
{
    Result<ModelSchemaGraph> Create(ModelExtractionResult extraction);
}

public sealed class ModelUserSchemaGraphFactory : IModelUserSchemaGraphFactory
{
    private static readonly ModelJsonDeserializerOptions DeserializerOptions = ModelJsonDeserializerOptions.Default
        .WithAllowDuplicateAttributeLogicalNames(true)
        .WithAllowDuplicateAttributeColumnNames(true);

    private readonly IModelJsonDeserializer _deserializer;

    public ModelUserSchemaGraphFactory(IModelJsonDeserializer? deserializer = null)
    {
        _deserializer = deserializer ?? new ModelJsonDeserializer();
    }

    public Result<ModelSchemaGraph> Create(ModelExtractionResult extraction)
    {
        if (extraction is null)
        {
            throw new ArgumentNullException(nameof(extraction));
        }

        var fromDataset = TryCreateFromDataset(extraction.Dataset);
        if (fromDataset is ModelSchemaGraph graph)
        {
            return Result<ModelSchemaGraph>.Success(graph);
        }

        return Result<ModelSchemaGraph>.Success(new ModelSchemaGraph(extraction.Model));
    }

    private ModelSchemaGraph? TryCreateFromDataset(DynamicEntityDataset dataset)
    {
        if (dataset is null || dataset.IsEmpty)
        {
            return null;
        }

        var tables = dataset.Tables;
        if (tables.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var table in tables)
        {
            if (table.Rows.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var row in table.Rows)
            {
                foreach (var value in row.Values)
                {
                    if (value is not string text)
                    {
                        continue;
                    }

                    var trimmed = text.Trim();
                    if (!LooksLikeJson(trimmed))
                    {
                        continue;
                    }

                    var modelResult = Deserialize(trimmed);
                    if (modelResult.IsSuccess)
                    {
                        return new ModelSchemaGraph(modelResult.Value);
                    }
                }
            }
        }

        return null;
    }

    private Result<Osm.Domain.Model.OsmModel> Deserialize(string json)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream(buffer, writable: false);
            return _deserializer.Deserialize(stream, warnings: null, DeserializerOptions);
        }
        catch (EncoderFallbackException ex)
        {
            return Result<Osm.Domain.Model.OsmModel>.Failure(ValidationError.Create(
                "uatUsers.schemaGraph.dataset.encoding",
                $"Failed to interpret dataset payload as UTF-8 JSON: {ex.Message}"));
        }
    }

    private static bool LooksLikeJson(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            return ch is '{' or '[';
        }

        return false;
    }
}
