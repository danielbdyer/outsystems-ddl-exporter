using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Smo;

internal static class MsDescriptionScriptBuilder
{
    public static ImmutableArray<string> Build(
        SmoTableDefinition table,
        string effectiveTableName,
        SmoFormatOptions format)
    {
        if (string.IsNullOrWhiteSpace(table.Description) && table.Columns.All(static column => string.IsNullOrWhiteSpace(column.Description)))
        {
            return ImmutableArray<string>.Empty;
        }

        var scripts = ImmutableArray.CreateBuilder<string>();
        var dedupe = new HashSet<DescriptionKey>(DescriptionKeyComparer.Instance);

        if (!string.IsNullOrWhiteSpace(table.Description) &&
            dedupe.Add(DescriptionKey.ForTable(table.Schema, effectiveTableName)))
        {
            scripts.Add(BuildTableScript(table.Schema, effectiveTableName, table.Description!, format));
        }

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Description))
            {
                continue;
            }

            if (!dedupe.Add(DescriptionKey.ForColumn(table.Schema, effectiveTableName, column.Name)))
            {
                continue;
            }

            scripts.Add(BuildColumnScript(table.Schema, effectiveTableName, column.Name, column.Description!, format));
        }

        return scripts.ToImmutable();
    }

    private static string BuildTableScript(string schema, string table, string description, SmoFormatOptions format)
    {
        var schemaIdentifier = SqlFormatting.QuoteIdentifier(schema, format);
        var tableIdentifier = SqlFormatting.QuoteIdentifier(table, format);
        var escapedDescription = SqlFormatting.EscapeSqlLiteral(description);
        var schemaLiteral = SqlFormatting.EscapeSqlLiteral(schema);
        var tableLiteral = SqlFormatting.EscapeSqlLiteral(table);

        return $"""
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'{schemaIdentifier}.{tableIdentifier}')
      AND minor_id = 0
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'{escapedDescription}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{escapedDescription}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}';
""".Trim();
    }

    private static string BuildColumnScript(string schema, string table, string column, string description, SmoFormatOptions format)
    {
        var schemaIdentifier = SqlFormatting.QuoteIdentifier(schema, format);
        var tableIdentifier = SqlFormatting.QuoteIdentifier(table, format);
        var columnLiteral = SqlFormatting.EscapeSqlLiteral(column);
        var descriptionLiteral = SqlFormatting.EscapeSqlLiteral(description);
        var schemaLiteral = SqlFormatting.EscapeSqlLiteral(schema);
        var tableLiteral = SqlFormatting.EscapeSqlLiteral(table);

        return $"""
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'{schemaIdentifier}.{tableIdentifier}')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'{schemaIdentifier}.{tableIdentifier}'), N'{columnLiteral}', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'{descriptionLiteral}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}',
        @level2type=N'COLUMN',@level2name=N'{columnLiteral}';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{descriptionLiteral}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}',
        @level2type=N'COLUMN',@level2name=N'{columnLiteral}';
""".Trim();
    }

    private readonly record struct DescriptionKey(string Schema, string Table, string? Column)
    {
        public static DescriptionKey ForTable(string schema, string table) => new(schema, table, null);

        public static DescriptionKey ForColumn(string schema, string table, string column)
            => new(schema, table, column);
    }

    private sealed class DescriptionKeyComparer : IEqualityComparer<DescriptionKey>
    {
        public static DescriptionKeyComparer Instance { get; } = new();

        public bool Equals(DescriptionKey x, DescriptionKey y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Column ?? string.Empty, y.Column ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(DescriptionKey obj)
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema ?? string.Empty);
            hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table ?? string.Empty);
            hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column ?? string.Empty);
            return hash;
        }
    }
}
