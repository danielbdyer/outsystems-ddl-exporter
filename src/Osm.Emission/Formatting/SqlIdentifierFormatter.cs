using System;
using Osm.Domain.Sql;

namespace Osm.Emission.Formatting;

public static class SqlIdentifierFormatter
{
    public static string Quote(string identifier) => SqlIdentifier.Quote(identifier);

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

        return SqlIdentifier.Qualify(schema, table);
    }
}
