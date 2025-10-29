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

        var hasTableDescription = !string.IsNullOrWhiteSpace(table.Description);
        var hasColumnDescription = table.Columns.Any(static c => !string.IsNullOrWhiteSpace(c.Description));
        var hasIndexDescription = table.Indexes.Any(static i => !string.IsNullOrWhiteSpace(i.Description));

        if (!hasTableDescription && !hasColumnDescription && !hasIndexDescription)
        {
            return ImmutableArray<string>.Empty;
        }

        var scripts = ImmutableArray.CreateBuilder<string>();

        if (hasTableDescription)
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

        foreach (var index in table.Indexes)
        {
            if (string.IsNullOrWhiteSpace(index.Description))
            {
                continue;
            }

            var resolvedName = _formatter.ResolveConstraintName(index.Name, table.Name, table.LogicalName, effectiveTableName);
            scripts.Add(BuildIndexExtendedPropertyScript(
                table.Schema,
                effectiveTableName,
                resolvedName,
                index.Description!,
                index.IsPrimaryKey));
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

    private string BuildIndexExtendedPropertyScript(
        string schema,
        string table,
        string index,
        string description,
        bool isPrimaryKey)
    {
        var descriptionLiteral = EscapeSqlLiteral(description);
        var indexLiteral = EscapeSqlLiteral(index);
        var schemaLiteral = EscapeSqlLiteral(schema);
        var tableLiteral = EscapeSqlLiteral(table);
        var level2Type = isPrimaryKey ? "CONSTRAINT" : "INDEX";

        return $"""
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{descriptionLiteral}',
    @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
    @level1type=N'TABLE',@level1name=N'{tableLiteral}',
    @level2type=N'{level2Type}',@level2name=N'{indexLiteral}';
""".Trim();
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
