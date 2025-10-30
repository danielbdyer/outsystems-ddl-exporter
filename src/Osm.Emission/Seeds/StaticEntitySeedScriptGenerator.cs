using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public sealed class StaticEntitySeedScriptGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StaticEntitySeedTemplateService _templateService;
    private readonly StaticSeedSqlBuilder _sqlBuilder;

    public StaticEntitySeedScriptGenerator(
        StaticEntitySeedTemplateService templateService,
        StaticSeedSqlBuilder sqlBuilder)
    {
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _sqlBuilder = sqlBuilder ?? throw new ArgumentNullException(nameof(sqlBuilder));
    }

    public string Generate(
        IReadOnlyList<StaticEntityTableData> tables,
        StaticSeedSynchronizationMode synchronizationMode)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (tables.Count == 0)
        {
            return _templateService.ApplyBlocks("-- No static entities were discovered in the supplied model." + Environment.NewLine);
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

            builder.Append(_sqlBuilder.BuildBlock(ordered[i], synchronizationMode));
        }

        return _templateService.ApplyBlocks(builder.ToString());
    }

    public async Task WriteAsync(
        string path,
        IReadOnlyList<StaticEntityTableData> tables,
        StaticSeedSynchronizationMode synchronizationMode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Seed output path must be provided.", nameof(path));
        }

        var script = Generate(tables, synchronizationMode);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory());
        await File.WriteAllTextAsync(path, script, Utf8NoBom, cancellationToken).ConfigureAwait(false);
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
            .Select(attribute =>
            {
                var physicalName = attribute.ColumnName.Value;
                var emissionName = physicalName;

                return new StaticEntitySeedColumn(
                    attribute.LogicalName.Value,
                    physicalName,
                    emissionName,
                    attribute.DataType,
                    attribute.Length,
                    attribute.Precision,
                    attribute.Scale,
                    primaryColumns.Contains(physicalName),
                    attribute.OnDisk.IsIdentity ?? attribute.IsAutoNumber);
            })
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
    string EmissionName,
    string DataType,
    int? Length,
    int? Precision,
    int? Scale,
    bool IsPrimaryKey,
    bool IsIdentity)
{
    public string EffectiveColumnName => string.IsNullOrWhiteSpace(EmissionName)
        ? ColumnName
        : EmissionName;

    public string TargetColumnName => EffectiveColumnName;
}

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
