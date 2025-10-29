using System;
using System.Collections.Generic;
using System.Linq;

namespace Osm.Smo;

internal static class ExternalDatabaseTypeParser
{
    public static (string BaseType, IReadOnlyList<int> Parameters) Parse(string externalType)
    {
        if (string.IsNullOrWhiteSpace(externalType))
        {
            return (string.Empty, Array.Empty<int>());
        }

        var trimmed = externalType.Trim();
        var openParen = trimmed.IndexOf('(');
        if (openParen < 0)
        {
            return (trimmed, Array.Empty<int>());
        }

        var baseType = trimmed[..openParen].Trim();
        var closeParen = trimmed.IndexOf(')', openParen + 1);
        var argsSegment = closeParen > openParen
            ? trimmed[(openParen + 1)..closeParen]
            : trimmed[(openParen + 1)..];

        var parameters = argsSegment
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseParameter)
            .ToArray();

        return (baseType, parameters);
    }

    private static int ParseParameter(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return 0;
        }

        var trimmed = segment.Trim();
        if (trimmed.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return int.TryParse(trimmed, out var value) ? value : 0;
    }
}
