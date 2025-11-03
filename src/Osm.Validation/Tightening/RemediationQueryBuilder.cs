using System.Collections.Immutable;
using System.Text;
using Osm.Emission.Formatting;

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
        var quotedTable = SqlIdentifierFormatter.Qualify(schema, table);
        var quotedColumn = SqlIdentifierFormatter.Quote(column);

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
            builder.Append(string.Join(", ", primaryKeyColumns.Select(SqlIdentifierFormatter.Quote)));
            builder.AppendLine(", *");
            builder.Append("FROM ").AppendLine(quotedTable);
            builder.Append("WHERE ").Append(quotedColumn).AppendLine(" IS NULL");
            builder.Append("ORDER BY ");
            builder.Append(string.Join(", ", primaryKeyColumns.Select(SqlIdentifierFormatter.Quote)));
            builder.AppendLine(";");
        }

        return builder.ToString();
    }
}
