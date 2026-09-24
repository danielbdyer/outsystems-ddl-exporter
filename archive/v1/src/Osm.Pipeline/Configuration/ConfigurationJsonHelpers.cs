using System;
using System.IO;
using System.Text.Json;

namespace Osm.Pipeline.Configuration;

internal static class ConfigurationJsonHelpers
{
    public static bool TryParseBoolean(JsonElement element, out bool value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.String:
                return bool.TryParse(element.GetString(), out value);
            default:
                value = default;
                return false;
        }
    }

    public static bool TryReadPathProperty(JsonElement container, string propertyName, string baseDirectory, out string path)
    {
        path = string.Empty;
        if (!container.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            path = ResolveRelativePath(baseDirectory, value);
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("path", out var pathElement)
            && pathElement.ValueKind == JsonValueKind.String)
        {
            var value = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            path = ResolveRelativePath(baseDirectory, value);
            return true;
        }

        return false;
    }

    public static string ResolveRelativePath(string baseDirectory, string value)
    {
        if (Path.IsPathRooted(value))
        {
            return Path.GetFullPath(value);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, value));
    }
}
