using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Osm.Smo;

namespace Osm.Smo.PerTableEmission;

internal sealed class ExtendedPropertyScriptBuilder
{
    private readonly IdentifierFormatter _identifierFormatter;

    public ExtendedPropertyScriptBuilder(IdentifierFormatter identifierFormatter)
    {
        _identifierFormatter = identifierFormatter ?? throw new ArgumentNullException(nameof(identifierFormatter));
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
            scripts.Add(BuildExtendedPropertyScript(table.Schema, effectiveTableName, table.Description!, level2: null));
        }

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Description))
            {
                continue;
            }

            scripts.Add(BuildExtendedPropertyScript(
                table.Schema,
                effectiveTableName,
                column.Description!,
                level2: ("COLUMN", column.Name)));
        }

        foreach (var index in table.Indexes)
        {
            if (string.IsNullOrWhiteSpace(index.Description))
            {
                continue;
            }

            var resolvedName = _identifierFormatter.ResolveConstraintName(index.Name, table.Name, table.LogicalName, effectiveTableName);
            scripts.Add(BuildExtendedPropertyScript(
                table.Schema,
                effectiveTableName,
                index.Description!,
                level2: (index.IsPrimaryKey ? "CONSTRAINT" : "INDEX", resolvedName)));
        }

        return scripts.ToImmutable();
    }

    // Single template for the table / column / index variants of
    // sp_addextendedproperty. The variants differ only by the optional level-2
    // object line (column or index/constraint); table-level descriptions omit it.
    private static string BuildExtendedPropertyScript(
        string schema,
        string table,
        string description,
        (string Type, string Name)? level2)
    {
        var builder = new StringBuilder();
        builder.Append("EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'");
        builder.Append(EscapeSqlLiteral(description));
        builder.Append("',\n    @level0type=N'SCHEMA',@level0name=N'");
        builder.Append(EscapeSqlLiteral(schema));
        builder.Append("',\n    @level1type=N'TABLE',@level1name=N'");
        builder.Append(EscapeSqlLiteral(table));
        builder.Append('\'');

        if (level2 is { } level)
        {
            builder.Append(",\n    @level2type=N'");
            builder.Append(level.Type);
            builder.Append("',@level2name=N'");
            builder.Append(EscapeSqlLiteral(level.Name));
            builder.Append('\'');
        }

        builder.Append(';');
        return builder.ToString();
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
