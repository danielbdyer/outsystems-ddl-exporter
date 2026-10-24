using System;

namespace Osm.Emission.Formatting;

public static class SqlIdentifierFormatter
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
        if (schema is null)
        {
            throw new ArgumentNullException(nameof(schema));
        }

        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        return Quote(schema) + "." + Quote(table);
    }
}
