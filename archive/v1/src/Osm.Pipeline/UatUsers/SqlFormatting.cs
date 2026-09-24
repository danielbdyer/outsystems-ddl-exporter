using System;

namespace Osm.Pipeline.UatUsers;

internal static class SqlFormatting
{
    public static string QuoteIdentifier(string identifier)
    {
        identifier ??= string.Empty;
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public static string SqlStringLiteral(string value)
    {
        value ??= string.Empty;
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
