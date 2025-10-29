using System;

namespace Osm.Pipeline.Profiling;

internal static class SqlIdentifierFormatter
{
    public static string Quote(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return "[]";
        }

        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string Qualify(string schema, string table)
    {
        return Quote(schema) + "." + Quote(table);
    }
}
