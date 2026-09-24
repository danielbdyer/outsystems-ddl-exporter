using System;
using System.IO;
using System.Text.Json;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.SqlExtraction;

public sealed class SnapshotValidator
{
    private static readonly string[] EntityArrayPropertyNames =
    {
        "attributes",
        "relationships",
        "indexes",
        "triggers",
    };

    public ValidationError? Validate(Stream jsonStream)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        if (!jsonStream.CanSeek)
        {
            throw new ArgumentException("JSON payload stream must support seeking.", nameof(jsonStream));
        }

        var originalPosition = jsonStream.Position;

        try
        {
            jsonStream.Position = 0;
            using var document = JsonDocument.Parse(jsonStream);

            if (!document.RootElement.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
            {
                return ValidationError.Create(
                    "extraction.sql.contract.modules",
                    "Advanced SQL payload must contain a 'modules' array.");
            }

            foreach (var moduleElement in modules.EnumerateArray())
            {
                if (moduleElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var moduleName = moduleElement.TryGetProperty("name", out var moduleNameElement)
                    ? moduleNameElement.GetString()
                    : null;

                if (!moduleElement.TryGetProperty("entities", out var entities))
                {
                    return ValidationError.Create(
                        "extraction.sql.contract.entities",
                        $"Module '{moduleName ?? "<unknown>"}' was missing the 'entities' array.");
                }

                if (entities.ValueKind == JsonValueKind.Null)
                {
                    return ValidationError.Create(
                        "extraction.sql.contract.entities",
                        $"Module '{moduleName ?? "<unknown>"}' returned null for 'entities'.");
                }

                if (entities.ValueKind != JsonValueKind.Array)
                {
                    return ValidationError.Create(
                        "extraction.sql.contract.entities",
                        $"Module '{moduleName ?? "<unknown>"}' returned 'entities' as {entities.ValueKind} instead of an array.");
                }

                foreach (var entityElement in entities.EnumerateArray())
                {
                    if (entityElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var entityName = entityElement.TryGetProperty("name", out var entityNameElement)
                        ? entityNameElement.GetString()
                        : null;

                    if (entityElement.TryGetProperty("attributes", out var attributesElement)
                        && attributesElement.ValueKind == JsonValueKind.Null)
                    {
                        throw new InvalidDataException(
                            $"Advanced SQL extractor emitted null attributes for entity '{entityName ?? "<unknown>"}' in module '{moduleName ?? "<unknown>"}'.");
                    }

                    foreach (var propertyName in EntityArrayPropertyNames)
                    {
                        if (!entityElement.TryGetProperty(propertyName, out var arrayElement))
                        {
                            return ValidationError.Create(
                                "extraction.sql.contract.entityArray",
                                $"Entity '{entityName ?? "<unknown>"}' in module '{moduleName ?? "<unknown>"}' was missing the '{propertyName}' array.");
                        }

                        if (arrayElement.ValueKind == JsonValueKind.Null)
                        {
                            return ValidationError.Create(
                                "extraction.sql.contract.entityArray",
                                $"Entity '{entityName ?? "<unknown>"}' in module '{moduleName ?? "<unknown>"}' returned null for '{propertyName}'.");
                        }

                        if (arrayElement.ValueKind != JsonValueKind.Array)
                        {
                            return ValidationError.Create(
                                "extraction.sql.contract.entityArray",
                                $"Entity '{entityName ?? "<unknown>"}' in module '{moduleName ?? "<unknown>"}' returned '{propertyName}' as {arrayElement.ValueKind} instead of an array.");
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            return ValidationError.Create(
                "extraction.sql.contract.invalidJson",
                $"Advanced SQL payload could not be parsed: {ex.Message}");
        }
        finally
        {
            jsonStream.Position = originalPosition;
        }

        return null;
    }
}
