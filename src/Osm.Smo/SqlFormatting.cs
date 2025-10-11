using System;

namespace Osm.Smo;

internal static class SqlFormatting
{
    public static string QuoteIdentifier(string identifier, SmoFormatOptions format)
    {
        return format.IdentifierQuoteStrategy switch
        {
            IdentifierQuoteStrategy.DoubleQuote => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
            IdentifierQuoteStrategy.None => identifier,
            _ => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]",
        };
    }

    public static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
