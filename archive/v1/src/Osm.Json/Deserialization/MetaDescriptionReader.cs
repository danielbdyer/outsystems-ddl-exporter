using System;
using System.Text.Json;

namespace Osm.Json.Deserialization;

internal static class MetaDescriptionReader
{
    public static string? Read(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Extract(document.RootElement);
    }

    public static string? Extract(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            JsonValueKind.Object => ExtractFromObject(element),
            JsonValueKind.Array => ExtractFromArray(element),
            _ => null
        };
    }

    public static string? Normalize(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description;

    private static string? ExtractFromObject(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "description", StringComparison.OrdinalIgnoreCase))
            {
                var fromDescription = Extract(property.Value);
                if (fromDescription is not null)
                {
                    return fromDescription;
                }
            }
        }

        if (TryGetDescriptionFromNameValue(element, "name", out var namedValue) ||
            TryGetDescriptionFromNameValue(element, "key", out namedValue) ||
            TryGetDescriptionFromNameValue(element, "property", out namedValue))
        {
            return namedValue;
        }

        if (element.TryGetProperty("value", out var valueElement))
        {
            var fromValue = Extract(valueElement);
            if (fromValue is not null)
            {
                return fromValue;
            }
        }

        if (element.TryGetProperty("text", out var textElement))
        {
            var fromText = Extract(textElement);
            if (fromText is not null)
            {
                return fromText;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                var nested = Extract(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ExtractFromArray(JsonElement element)
    {
        foreach (var item in element.EnumerateArray())
        {
            var fromArray = Extract(item);
            if (fromArray is not null)
            {
                return fromArray;
            }
        }

        return null;
    }

    private static bool TryGetDescriptionFromNameValue(JsonElement element, string nameProperty, out string? description)
    {
        description = null;
        if (!element.TryGetProperty(nameProperty, out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(candidate) ||
            !string.Equals(candidate, "description", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (element.TryGetProperty("value", out var valueElement))
        {
            description = Extract(valueElement);
            return description is not null;
        }

        if (element.TryGetProperty("text", out var textElement))
        {
            description = Extract(textElement);
            return description is not null;
        }

        return false;
    }
}
