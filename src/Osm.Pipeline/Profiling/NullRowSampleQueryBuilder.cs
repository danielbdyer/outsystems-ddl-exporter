using System;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using Osm.Emission.Formatting;

namespace Osm.Pipeline.Profiling;

internal sealed class NullRowSampleQueryBuilder
{
    private const int DefaultSampleLimit = 10;

    public void Configure(
        DbCommand command,
        string schema,
        string table,
        string column,
        ImmutableArray<string> primaryKeyColumns,
        int sampleLimit = DefaultSampleLimit)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            throw new ArgumentException("Schema cannot be null or whitespace.", nameof(schema));
        }

        if (string.IsNullOrWhiteSpace(table))
        {
            throw new ArgumentException("Table cannot be null or whitespace.", nameof(table));
        }

        if (string.IsNullOrWhiteSpace(column))
        {
            throw new ArgumentException("Column cannot be null or whitespace.", nameof(column));
        }

        command.Parameters.Clear();
        command.CommandText = BuildCommandText(schema, table, column, primaryKeyColumns, sampleLimit);
    }

    internal static string BuildCommandText(
        string schema,
        string table,
        string column,
        ImmutableArray<string> primaryKeyColumns,
        int sampleLimit)
    {
        var builder = new StringBuilder();
        var quotedColumn = SqlIdentifierFormatter.Quote(column);
        var quotedTable = SqlIdentifierFormatter.Qualify(schema, table);

        // If no PK columns, we can't identify specific rows
        if (primaryKeyColumns.IsDefaultOrEmpty)
        {
            builder.AppendLine("-- No primary key columns available to identify NULL rows");
            builder.AppendLine("SELECT NULL AS [_no_pk_]");
            builder.AppendLine("WHERE 1 = 0;");
            return builder.ToString();
        }

        builder.Append("SELECT TOP (").Append(sampleLimit).AppendLine(")");
        builder.Append("    ");
        builder.AppendLine(string.Join(", ", primaryKeyColumns.Select(SqlIdentifierFormatter.Quote)));
        builder.Append("FROM ").Append(quotedTable).AppendLine(" WITH (NOLOCK)");
        builder.Append("WHERE ").Append(quotedColumn).AppendLine(" IS NULL");
        builder.Append("ORDER BY ");
        builder.Append(string.Join(", ", primaryKeyColumns.Select(SqlIdentifierFormatter.Quote)));
        builder.AppendLine(";");

        return builder.ToString();
    }
}
