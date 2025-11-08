using System;

namespace Osm.Pipeline;

internal sealed class ForwardSlashPathCanonicalizer : IPathCanonicalizer
{
    public string Canonicalize(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        return NormalizeSeparators(trimmed);
    }

    public string? CanonicalizeOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path?.Trim();
        }

        return Canonicalize(path!);
    }

    private static string NormalizeSeparators(string value)
        => value.IndexOf('\', StringComparison.Ordinal) < 0 ? value : value.Replace('\\', '/');
}
