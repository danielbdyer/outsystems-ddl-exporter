using System;
using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class MetadataAccumulator
{
    public List<OutsystemsModuleRow> Modules { get; private set; } = new();
    public List<OutsystemsEntityRow> Entities { get; private set; } = new();
    public List<OutsystemsAttributeRow> Attributes { get; private set; } = new();
    public List<OutsystemsReferenceRow> References { get; private set; } = new();
    public List<OutsystemsPhysicalTableRow> PhysicalTables { get; private set; } = new();
    public List<OutsystemsColumnRealityRow> ColumnReality { get; private set; } = new();
    public List<OutsystemsColumnCheckRow> ColumnChecks { get; private set; } = new();
    public List<OutsystemsColumnCheckJsonRow> ColumnCheckJson { get; private set; } = new();
    public List<OutsystemsPhysicalColumnPresenceRow> PhysicalColumnsPresent { get; private set; } = new();
    public List<OutsystemsIndexRow> Indexes { get; private set; } = new();
    public List<OutsystemsIndexColumnRow> IndexColumns { get; private set; } = new();
    public List<OutsystemsForeignKeyRow> ForeignKeys { get; private set; } = new();
    public List<OutsystemsForeignKeyColumnRow> ForeignKeyColumns { get; private set; } = new();
    public List<OutsystemsForeignKeyAttrMapRow> ForeignKeyAttributeMap { get; private set; } = new();
    public List<OutsystemsAttributeHasFkRow> AttributeForeignKeys { get; private set; } = new();
    public List<OutsystemsForeignKeyColumnsJsonRow> ForeignKeyColumnsJson { get; private set; } = new();
    public List<OutsystemsForeignKeyAttributeJsonRow> ForeignKeyAttributeJson { get; private set; } = new();
    public List<OutsystemsTriggerRow> Triggers { get; private set; } = new();
    public List<OutsystemsAttributeJsonRow> AttributeJson { get; private set; } = new();
    public List<OutsystemsRelationshipJsonRow> RelationshipJson { get; private set; } = new();
    public List<OutsystemsIndexJsonRow> IndexJson { get; private set; } = new();
    public List<OutsystemsTriggerJsonRow> TriggerJson { get; private set; } = new();
    public List<OutsystemsModuleJsonRow> ModuleJson { get; private set; } = new();

    public void SetModules(List<OutsystemsModuleRow> rows) => Modules = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetEntities(List<OutsystemsEntityRow> rows) => Entities = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetAttributes(List<OutsystemsAttributeRow> rows) => Attributes = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetReferences(List<OutsystemsReferenceRow> rows) => References = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetPhysicalTables(List<OutsystemsPhysicalTableRow> rows) => PhysicalTables = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetColumnReality(List<OutsystemsColumnRealityRow> rows) => ColumnReality = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetColumnChecks(List<OutsystemsColumnCheckRow> rows) => ColumnChecks = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetColumnCheckJson(List<OutsystemsColumnCheckJsonRow> rows) => ColumnCheckJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetPhysicalColumnsPresent(List<OutsystemsPhysicalColumnPresenceRow> rows) => PhysicalColumnsPresent = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetIndexes(List<OutsystemsIndexRow> rows) => Indexes = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetIndexColumns(List<OutsystemsIndexColumnRow> rows) => IndexColumns = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetForeignKeys(List<OutsystemsForeignKeyRow> rows) => ForeignKeys = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetForeignKeyColumns(List<OutsystemsForeignKeyColumnRow> rows) => ForeignKeyColumns = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetForeignKeyAttributeMap(List<OutsystemsForeignKeyAttrMapRow> rows) => ForeignKeyAttributeMap = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetAttributeForeignKeys(List<OutsystemsAttributeHasFkRow> rows) => AttributeForeignKeys = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetForeignKeyColumnsJson(List<OutsystemsForeignKeyColumnsJsonRow> rows) => ForeignKeyColumnsJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetForeignKeyAttributeJson(List<OutsystemsForeignKeyAttributeJsonRow> rows) => ForeignKeyAttributeJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetTriggers(List<OutsystemsTriggerRow> rows) => Triggers = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetAttributeJson(List<OutsystemsAttributeJsonRow> rows) => AttributeJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetRelationshipJson(List<OutsystemsRelationshipJsonRow> rows) => RelationshipJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetIndexJson(List<OutsystemsIndexJsonRow> rows) => IndexJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetTriggerJson(List<OutsystemsTriggerJsonRow> rows) => TriggerJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public void SetModuleJson(List<OutsystemsModuleJsonRow> rows) => ModuleJson = rows ?? throw new ArgumentNullException(nameof(rows));

    public OutsystemsMetadataSnapshot BuildSnapshot(string databaseName)
        => new(
            Modules,
            Entities,
            Attributes,
            References,
            PhysicalTables,
            ColumnReality,
            ColumnChecks,
            ColumnCheckJson,
            PhysicalColumnsPresent,
            Indexes,
            IndexColumns,
            ForeignKeys,
            ForeignKeyColumns,
            ForeignKeyAttributeMap,
            AttributeForeignKeys,
            ForeignKeyColumnsJson,
            ForeignKeyAttributeJson,
            Triggers,
            AttributeJson,
            RelationshipJson,
            IndexJson,
            TriggerJson,
            ModuleJson,
            databaseName);
}
