using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public sealed class StaticEntitySeedTemplate
{
    private const string Placeholder = "{{STATIC_ENTITY_BLOCKS}}";

    private StaticEntitySeedTemplate(string content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public string Content { get; }

    public static StaticEntitySeedTemplate Load()
    {
        var assembly = typeof(StaticEntitySeedTemplate).GetTypeInfo().Assembly;
        const string resourceName = "Osm.Emission.Templates.StaticEntitySeedTemplate.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded static entity seed template '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        var content = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(content) || !content.Contains(Placeholder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Static entity seed template is missing the expected placeholder block.");
        }

        return new StaticEntitySeedTemplate(content);
    }

    public string ApplyBlocks(string blocks)
    {
        if (blocks is null)
        {
            throw new ArgumentNullException(nameof(blocks));
        }

        return Content.Replace(Placeholder, blocks, StringComparison.Ordinal);
    }
}

public sealed class StaticEntitySeedScriptGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Generate(
        StaticEntitySeedTemplate template,
        IReadOnlyList<StaticEntityTableData> tables,
        StaticSeedSynchronizationMode synchronizationMode)
    {
        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (tables.Count == 0)
        {
            return template.ApplyBlocks("-- No static entities were discovered in the supplied model." + Environment.NewLine);
        }

        var ordered = tables
            .OrderBy(t => t.Definition.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Definition.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Definition.EffectiveName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var builder = new StringBuilder();
        for (var i = 0; i < ordered.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            AppendBlock(builder, ordered[i], synchronizationMode);
        }

        return template.ApplyBlocks(builder.ToString());
    }

    public async Task WriteAsync(
        string path,
        StaticEntitySeedTemplate template,
        IReadOnlyList<StaticEntityTableData> tables,
        StaticSeedSynchronizationMode synchronizationMode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Seed output path must be provided.", nameof(path));
        }

        var script = Generate(template, tables, synchronizationMode);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory());
        await File.WriteAllTextAsync(path, script, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static void AppendBlock(
        StringBuilder builder,
        StaticEntityTableData tableData,
        StaticSeedSynchronizationMode synchronizationMode)
    {
        var definition = tableData.Definition;
        var schema = definition.Schema;
        var targetName = definition.EffectiveName;
        var physicalName = definition.PhysicalName;
        var rows = tableData.Rows;

        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine($"-- Module: {definition.Module}");
        builder.AppendLine($"-- Entity: {definition.LogicalName} ({schema}.{physicalName})");
        if (!string.Equals(physicalName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"-- Target: {schema}.{targetName}");
        }
        builder.AppendLine("--------------------------------------------------------------------------------");

        if (rows.Length == 0)
        {
            builder.AppendLine("-- No data rows were returned for this static entity; MERGE statement omitted.");
            return;
        }

        var hasIdentity = definition.Columns.Any(static c => c.IsIdentity);
        var targetIdentifier = FormatTwoPartName(schema, targetName);

        if (hasIdentity)
        {
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} ON;");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        builder.AppendLine($"MERGE INTO {targetIdentifier} AS Target");
        builder.AppendLine("USING");
        builder.AppendLine("(");
        builder.AppendLine("    VALUES");

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            builder.Append("        (");
            for (var j = 0; j < definition.Columns.Length; j++)
            {
                if (j > 0)
                {
                    builder.Append(", ");
                }

                var column = definition.Columns[j];
                builder.Append(FormatValue(row.Values[j], column));
            }

            builder.Append(')');
            if (i < rows.Length - 1)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        builder.Append(") AS Source (");
        builder.Append(string.Join(", ", definition.Columns.Select(static c => FormatColumnName(c.ColumnName))));
        builder.AppendLine(")");

        var primaryColumns = definition.Columns.Where(static c => c.IsPrimaryKey).ToArray();
        if (primaryColumns.Length == 0)
        {
            throw new InvalidOperationException($"Static entity '{definition.Module}::{definition.LogicalName}' does not define a primary key.");
        }

        builder.Append("    ON ");
        builder.AppendLine(string.Join(
            " AND ",
            primaryColumns.Select(static c => $"Target.{FormatColumnName(c.ColumnName)} = Source.{FormatColumnName(c.ColumnName)}")));

        var updatableColumns = definition.Columns.Where(static c => !c.IsPrimaryKey).ToArray();
        if (updatableColumns.Length > 0)
        {
            builder.AppendLine("WHEN MATCHED THEN UPDATE SET");
            for (var i = 0; i < updatableColumns.Length; i++)
            {
                var column = updatableColumns[i];
                builder.Append("    Target.");
                builder.Append(FormatColumnName(column.ColumnName));
                builder.Append(" = Source.");
                builder.Append(FormatColumnName(column.ColumnName));
                if (i < updatableColumns.Length - 1)
                {
                    builder.Append(',');
                }

                builder.AppendLine();
            }
        }

        builder.Append("WHEN NOT MATCHED THEN INSERT (");
        builder.Append(string.Join(", ", definition.Columns.Select(static c => FormatColumnName(c.ColumnName))));
        builder.AppendLine(")");
        builder.Append("    VALUES (");
        builder.Append(string.Join(", ", definition.Columns.Select(static c => $"Source.{FormatColumnName(c.ColumnName)}")));
        builder.AppendLine(");");

        if (synchronizationMode == StaticSeedSynchronizationMode.Authoritative)
        {
            builder.AppendLine("WHEN NOT MATCHED BY SOURCE THEN DELETE;");
        }

        builder.AppendLine();
        builder.AppendLine("GO");

        if (hasIdentity)
        {
            builder.AppendLine();
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} OFF;");
            builder.AppendLine("GO");
        }
    }

    private static string FormatTwoPartName(string schema, string name)
        => $"[{EscapeIdentifier(schema)}].[{EscapeIdentifier(name)}]";

    private static string FormatColumnName(string name)
        => $"[{EscapeIdentifier(name)}]";

    private static string EscapeIdentifier(string identifier)
        => identifier.Replace("]", "]]", StringComparison.Ordinal);

    private static string FormatValue(object? value, StaticEntitySeedColumn column)
    {
        if (value is null)
        {
            return "NULL";
        }

        return value switch
        {
            string s => $"N'{s.Replace("'", "''", StringComparison.Ordinal)}'",
            char c => $"N'{c.ToString().Replace("'", "''", StringComparison.Ordinal)}'",
            bool b => b ? "1" : "0",
            byte bt => bt.ToString(CultureInfo.InvariantCulture),
            sbyte sb => sb.ToString(CultureInfo.InvariantCulture),
            short sh => sh.ToString(CultureInfo.InvariantCulture),
            ushort ush => ush.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint ui => ui.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G17", CultureInfo.InvariantCulture),
            float f => f.ToString("G9", CultureInfo.InvariantCulture),
            DateOnly date => $"'{date:yyyy-MM-dd}'",
            TimeOnly time => $"'{time:HH:mm:ss.fffffff}'",
            DateTime dt => $"'{dt:yyyy-MM-ddTHH:mm:ss.fffffff}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-ddTHH:mm:ss.fffffffK}'",
            TimeSpan ts => $"'{ts:c}'",
            Guid g => $"'{g:D}'",
            byte[] bytes => $"0x{BitConverter.ToString(bytes).Replace("-", string.Empty, StringComparison.Ordinal)}",
            _ => $"N'{value.ToString()?.Replace("'", "''", StringComparison.Ordinal) ?? string.Empty}'"
        };
    }
}

public static class StaticEntitySeedDefinitionBuilder
{
    public static ImmutableArray<StaticEntitySeedTableDefinition> Build(OsmModel model, NamingOverrideOptions namingOverrides)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        namingOverrides ??= NamingOverrideOptions.Empty;

        var tables = ImmutableArray.CreateBuilder<StaticEntitySeedTableDefinition>();

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities.Where(static e => e.IsStatic && e.IsActive))
            {
                var definition = CreateDefinition(module.Name.Value, entity, namingOverrides);
                if (definition.Columns.Length == 0)
                {
                    continue;
                }

                tables.Add(definition);
            }
        }

        return tables.ToImmutable();
    }

    private static StaticEntitySeedTableDefinition CreateDefinition(string moduleName, EntityModel entity, NamingOverrideOptions namingOverrides)
    {
        var filteredAttributes = entity.Attributes
            .Where(static attribute => attribute.IsActive && !(attribute.OnDisk.IsComputed ?? false) && !attribute.Reality.IsPresentButInactive)
            .ToImmutableArray();

        if (filteredAttributes.IsDefaultOrEmpty)
        {
            return StaticEntitySeedTableDefinition.Empty;
        }

        var primaryIndex = entity.Indexes.FirstOrDefault(static index => index.IsPrimary);
        var primaryColumns = primaryIndex is null
            ? filteredAttributes.Where(static attribute => attribute.IsIdentifier).Select(static attribute => attribute.ColumnName.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : primaryIndex.Columns
                .Where(static column => !column.IsIncluded)
                .Select(static column => column.Column.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (primaryColumns.Count == 0)
        {
            primaryColumns = filteredAttributes
                .Where(static attribute => attribute.IsIdentifier)
                .Select(static attribute => attribute.ColumnName.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var columnDefinitions = filteredAttributes
            .Select(attribute => new StaticEntitySeedColumn(
                attribute.LogicalName.Value,
                attribute.ColumnName.Value,
                attribute.DataType,
                attribute.Length,
                attribute.Precision,
                attribute.Scale,
                primaryColumns.Contains(attribute.ColumnName.Value),
                attribute.OnDisk.IsIdentity ?? attribute.IsAutoNumber))
            .ToImmutableArray();

        if (columnDefinitions.IsDefault)
        {
            columnDefinitions = ImmutableArray<StaticEntitySeedColumn>.Empty;
        }

        var effectiveName = namingOverrides.GetEffectiveTableName(entity.Schema.Value, entity.PhysicalName.Value, entity.LogicalName.Value, moduleName);

        return new StaticEntitySeedTableDefinition(
            moduleName,
            entity.LogicalName.Value,
            entity.Schema.Value,
            entity.PhysicalName.Value,
            effectiveName,
            columnDefinitions);
    }
}

public interface IStaticEntityDataProvider
{
    Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
        IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
        CancellationToken cancellationToken = default);
}

public sealed record StaticEntitySeedTableDefinition(
    string Module,
    string LogicalName,
    string Schema,
    string PhysicalName,
    string EffectiveName,
    ImmutableArray<StaticEntitySeedColumn> Columns)
{
    public static StaticEntitySeedTableDefinition Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, ImmutableArray<StaticEntitySeedColumn>.Empty);
}

public sealed record StaticEntitySeedColumn(
    string LogicalName,
    string ColumnName,
    string DataType,
    int? Length,
    int? Precision,
    int? Scale,
    bool IsPrimaryKey,
    bool IsIdentity);

public sealed record StaticEntityRow(ImmutableArray<object?> Values)
{
    public static StaticEntityRow Create(IEnumerable<object?> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var array = values.ToImmutableArray();
        if (array.IsDefault)
        {
            array = ImmutableArray<object?>.Empty;
        }

        return new StaticEntityRow(array);
    }
}

public sealed record StaticEntityTableData(StaticEntitySeedTableDefinition Definition, ImmutableArray<StaticEntityRow> Rows)
{
    public static StaticEntityTableData Create(StaticEntitySeedTableDefinition definition, IEnumerable<StaticEntityRow> rows)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        var materialized = rows.ToImmutableArray();
        if (materialized.IsDefault)
        {
            materialized = ImmutableArray<StaticEntityRow>.Empty;
        }

        return new StaticEntityTableData(definition, materialized);
    }
}
