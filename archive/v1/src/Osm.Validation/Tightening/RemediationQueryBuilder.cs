using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Osm.Validation.Tightening;

internal static class RemediationQueryBuilder
{
    public static string BuildUpdateNullsQuery(
        string schema,
        string table,
        string column,
        ImmutableArray<string> primaryKeyColumns,
        string? suggestedDefaultValue = null)
    {
        var builder = new StringBuilder();
        var quotedTable = QualifyIdentifier(schema, table);
        var quotedColumn = QuoteIdentifier(column);

        builder.AppendLine("-- Option 1: Set NULL values to a default");
        builder.Append("UPDATE ").AppendLine(quotedTable);
        builder.Append("SET ").Append(quotedColumn).Append(" = ");

        if (!string.IsNullOrWhiteSpace(suggestedDefaultValue))
        {
            builder.AppendLine(suggestedDefaultValue);
        }
        else
        {
            builder.AppendLine("'<default_value>' -- Replace with appropriate default");
        }

        builder.Append("WHERE ").Append(quotedColumn).AppendLine(" IS NULL;");
        builder.AppendLine();

        builder.AppendLine("-- Option 2: Delete rows with NULL values");
        builder.Append("DELETE FROM ").AppendLine(quotedTable);
        builder.Append("WHERE ").Append(quotedColumn).AppendLine(" IS NULL;");

        if (!primaryKeyColumns.IsDefaultOrEmpty)
        {
            builder.AppendLine();
            builder.AppendLine("-- Option 3: Review specific rows and decide on a case-by-case basis");
            builder.Append("SELECT ");
            builder.Append(string.Join(", ", primaryKeyColumns.Select(QuoteIdentifier)));
            builder.AppendLine(", *");
            builder.Append("FROM ").AppendLine(quotedTable);
            builder.Append("WHERE ").Append(quotedColumn).AppendLine(" IS NULL");
            builder.Append("ORDER BY ");
            builder.Append(string.Join(", ", primaryKeyColumns.Select(QuoteIdentifier)));
            builder.AppendLine(";");
        }

        return builder.ToString();
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return "[]";
        }

        // Escape any existing brackets and wrap in brackets
        var escaped = identifier.Replace("]", "]]");
        return $"[{escaped}]";
    }

    private static string QualifyIdentifier(string schema, string objectName)
    {
        return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(objectName)}";
    }
}
