using System;
using Osm.Domain.Sql;

namespace Osm.Pipeline.UatUsers;

internal static class SqlFormatting
{
    public static string QuoteIdentifier(string identifier) => SqlIdentifier.Quote(identifier);

    public static string SqlStringLiteral(string value)
    {
        value ??= string.Empty;
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
