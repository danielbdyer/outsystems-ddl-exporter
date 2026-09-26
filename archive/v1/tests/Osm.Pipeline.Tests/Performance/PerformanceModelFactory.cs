using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;

namespace Osm.Pipeline.Tests.Performance;

internal static class PerformanceModelFactory
{
    public static PerformanceModelDefinition CreateWideTableModel(int columnCount, long rowCount)
    {
        if (columnCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount), "Wide table must contain at least two columns.");
        }

        var moduleName = ModuleName.Create("PerformanceWide").Value;
        var entityName = EntityName.Create("WideEntity").Value;
        var tableName = TableName.Create("OSUSR_PERF_WIDE").Value;
        var schema = SchemaName.Create("dbo").Value;

        var attributes = CreateAttributeSet(columnCount);
        var entity = EntityModel.Create(
            moduleName,
            entityName,
            tableName,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: attributes).Value;

        return CreateDefinition(
            new[] { entity },
            moduleName,
            static _ => true,
            _ => rowCount);
    }

    public static PerformanceModelDefinition CreateEntityGridModel(int entityCount, int attributeCount, long rowCountPerEntity)
    {
        if (entityCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityCount), "Entity count must be positive.");
        }

        if (attributeCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(attributeCount), "Each entity must expose at least two attributes.");
        }

        var moduleName = ModuleName.Create("PerformanceGrid").Value;
        var entities = new List<EntityModel>(entityCount);
        for (var index = 0; index < entityCount; index++)
        {
            var entity = BuildGridEntity(moduleName, attributeCount, index);
            entities.Add(entity);
        }

        return CreateDefinition(
            entities,
            moduleName,
            static _ => true,
            _ => rowCountPerEntity);
    }

    private static PerformanceModelDefinition CreateDefinition(
        IEnumerable<EntityModel> entities,
        ModuleName moduleName,
        Func<EntityModel, bool> includeEntity,
        Func<EntityModel, long> rowCountSelector)
    {
        var entityArray = entities.Where(includeEntity).ToImmutableArray();
        if (entityArray.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("At least one entity must be provided for the performance model.");
        }

        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, entityArray).Value;
        var model = OsmModel.Create(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), new[] { module }).Value;

        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance);
        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance);

        foreach (var entity in entityArray)
        {
            var schema = entity.Schema.Value;
            var table = entity.PhysicalName.Value;
            rowCounts[(schema, table)] = rowCountSelector(entity);

            foreach (var attribute in entity.Attributes)
            {
                metadata[(schema, table, attribute.ColumnName.Value)] = new ColumnMetadata(
                    IsNullable: !attribute.IsMandatory,
                    IsComputed: false,
                    IsPrimaryKey: attribute.IsIdentifier,
                    DefaultDefinition: attribute.DefaultValue);
            }
        }

        return new PerformanceModelDefinition(model, metadata, rowCounts);
    }

    private static ImmutableArray<AttributeModel> CreateAttributeSet(int columnCount)
    {
        var attributes = new List<AttributeModel>(columnCount);
        attributes.Add(CreateIdentifierAttribute());

        for (var index = 1; index < columnCount; index++)
        {
            attributes.Add(CreateRegularAttribute(index));
        }

        return attributes.ToImmutableArray();
    }

    private static EntityModel BuildGridEntity(ModuleName module, int attributeCount, int index)
    {
        var entityName = EntityName.Create($"GridEntity{index:D3}").Value;
        var tableName = TableName.Create($"OSUSR_GRID_{index:D3}").Value;
        var schema = SchemaName.Create("dbo").Value;

        var attributes = new List<AttributeModel>(attributeCount);
        attributes.Add(CreateIdentifierAttribute());

        for (var attributeIndex = 1; attributeIndex < attributeCount; attributeIndex++)
        {
            attributes.Add(CreateRegularAttribute(attributeIndex));
        }

        return EntityModel.Create(
            module,
            entityName,
            tableName,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: attributes).Value;
    }

    private static AttributeModel CreateIdentifierAttribute()
    {
        return AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "INT",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;
    }

    private static AttributeModel CreateRegularAttribute(int ordinal)
    {
        return AttributeModel.Create(
            AttributeName.Create($"Column{ordinal:D3}").Value,
            ColumnName.Create($"COLUMN_{ordinal:D3}").Value,
            dataType: "NVARCHAR",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true).Value;
    }
}

internal sealed record PerformanceModelDefinition(
    OsmModel Model,
    IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> Metadata,
    IReadOnlyDictionary<(string Schema, string Table), long> RowCounts);
