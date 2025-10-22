using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Smo;

namespace Osm.Smo.PerTableEmission;

internal sealed class ExtendedPropertyScriptBuilder
{
    public ExtendedPropertyScriptBuilder(SqlScriptFormatter formatter)
    {
        _ = formatter ?? throw new ArgumentNullException(nameof(formatter));
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
            scripts.Add(BuildTableExtendedPropertyScript(table.Schema, effectiveTableName, table.Description!));
        }

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Description))
            {
                continue;
            }

            scripts.Add(BuildColumnExtendedPropertyScript(table.Schema, effectiveTableName, column.Name, column.Description!));
        }

        return scripts.ToImmutable();
    }

    private string BuildTableExtendedPropertyScript(
        string schema,
        string table,
        string description)
    {
        var descriptionLiteral = EscapeSqlLiteral(description);
        var schemaLiteral = EscapeSqlLiteral(schema);
        var tableLiteral = EscapeSqlLiteral(table);

        return $"""
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{descriptionLiteral}',
    @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
    @level1type=N'TABLE',@level1name=N'{tableLiteral}';
""".Trim();
    }

    private string BuildColumnExtendedPropertyScript(
        string schema,
        string table,
        string column,
        string description)
    {
        var columnLiteral = EscapeSqlLiteral(column);
        var descriptionLiteral = EscapeSqlLiteral(description);
        var schemaLiteral = EscapeSqlLiteral(schema);
        var tableLiteral = EscapeSqlLiteral(table);

        return $"""
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
