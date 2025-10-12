using System;
using System.Text.Json;

namespace Osm.Json;

internal static class JsonErrorFormatter
{
    public static string BuildMessage(string context, JsonException exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var path = string.IsNullOrWhiteSpace(exception.Path) ? "$" : exception.Path!;
        var location = FormatLocation(exception);
        var sanitizedMessage = SanitizeMessage(exception.Message);

        return $"{context} at path '{path}'{location}: {sanitizedMessage}";
    }

    private static string FormatLocation(JsonException exception)
    {
        if (!exception.LineNumber.HasValue && !exception.BytePositionInLine.HasValue)
        {
            return string.Empty;
        }

        var line = exception.LineNumber ?? 0;
        var position = exception.BytePositionInLine.HasValue
            ? exception.BytePositionInLine.Value + 1
            : (long?)null;

        if (line <= 0 && position is null)
        {
            return string.Empty;
        }

        return position.HasValue
            ? $" (line {line}, position {position.Value})"
            : $" (line {line})";
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unexpected end of JSON content.";
        }

        var trimmed = message.Trim();

        var pathIndex = trimmed.IndexOf(" Path:", StringComparison.Ordinal);
        if (pathIndex >= 0)
        {
            trimmed = trimmed[..pathIndex].TrimEnd('.', ' ');
        }

        var lineIndex = trimmed.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (lineIndex >= 0)
        {
            trimmed = trimmed[..lineIndex].TrimEnd('.', ' ');
        }

        return trimmed;
    }
}
