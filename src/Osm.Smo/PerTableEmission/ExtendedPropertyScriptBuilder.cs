using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Smo;

namespace Osm.Smo.PerTableEmission;

internal sealed class ExtendedPropertyScriptBuilder
{
    private readonly SqlScriptFormatter _formatter;

    public ExtendedPropertyScriptBuilder(SqlScriptFormatter formatter)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    public ImmutableArray<string> BuildExtendedPropertyScripts(
        SmoTableDefinition table,
        string effectiveTableName,
        SmoFormatOptions format)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (effectiveTableName is null)
        {
            throw new ArgumentNullException(nameof(effectiveTableName));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        if (string.IsNullOrWhiteSpace(table.Description) && table.Columns.All(c => string.IsNullOrWhiteSpace(c.Description)))
        {
            return ImmutableArray<string>.Empty;
        }

        var scripts = ImmutableArray.CreateBuilder<string>();

        if (!string.IsNullOrWhiteSpace(table.Description))
        {
            scripts.Add(BuildTableExtendedPropertyScript(table.Schema, effectiveTableName, table.Description!, format));
        }

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Description))
            {
                continue;
            }

            scripts.Add(BuildColumnExtendedPropertyScript(table.Schema, effectiveTableName, column.Name, column.Description!, format));
        }

        return scripts.ToImmutable();
    }

    private string BuildTableExtendedPropertyScript(
        string schema,
        string table,
        string description,
        SmoFormatOptions format)
    {
        var schemaIdentifier = _formatter.QuoteIdentifier(schema, format);
        var tableIdentifier = _formatter.QuoteIdentifier(table, format);
        var escapedDescription = EscapeSqlLiteral(description);
        var schemaLiteral = EscapeSqlLiteral(schema);
        var tableLiteral = EscapeSqlLiteral(table);

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

    private string BuildColumnExtendedPropertyScript(
        string schema,
        string table,
        string column,
        string description,
        SmoFormatOptions format)
    {
        var schemaIdentifier = _formatter.QuoteIdentifier(schema, format);
        var tableIdentifier = _formatter.QuoteIdentifier(table, format);
        var columnLiteral = EscapeSqlLiteral(column);
        var descriptionLiteral = EscapeSqlLiteral(description);
        var schemaLiteral = EscapeSqlLiteral(schema);
        var tableLiteral = EscapeSqlLiteral(table);

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

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
