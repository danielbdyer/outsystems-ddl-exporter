using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.SqlExtraction;

public interface IOutsystemsMetadataReader
{
    Task<Result<OutsystemsMetadataSnapshot>> ReadAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default);
}

internal interface IMetadataSnapshotDiagnostics
{
    MetadataRowSnapshot? LastFailureRowSnapshot { get; }
}

public sealed record OutsystemsMetadataSnapshot(
    IReadOnlyList<OutsystemsModuleRow> Modules,
    IReadOnlyList<OutsystemsEntityRow> Entities,
    IReadOnlyList<OutsystemsAttributeRow> Attributes,
    IReadOnlyList<OutsystemsReferenceRow> References,
    IReadOnlyList<OutsystemsPhysicalTableRow> PhysicalTables,
    IReadOnlyList<OutsystemsColumnRealityRow> ColumnReality,
    IReadOnlyList<OutsystemsColumnCheckRow> ColumnChecks,
    IReadOnlyList<OutsystemsColumnCheckJsonRow> ColumnCheckJson,
    IReadOnlyList<OutsystemsPhysicalColumnPresenceRow> PhysicalColumnsPresent,
    IReadOnlyList<OutsystemsIndexRow> Indexes,
    IReadOnlyList<OutsystemsIndexColumnRow> IndexColumns,
    IReadOnlyList<OutsystemsForeignKeyRow> ForeignKeys,
    IReadOnlyList<OutsystemsForeignKeyColumnRow> ForeignKeyColumns,
    IReadOnlyList<OutsystemsForeignKeyAttrMapRow> ForeignKeyAttributeMap,
    IReadOnlyList<OutsystemsAttributeHasFkRow> AttributeForeignKeys,
    IReadOnlyList<OutsystemsForeignKeyColumnsJsonRow> ForeignKeyColumnsJson,
    IReadOnlyList<OutsystemsForeignKeyAttributeJsonRow> ForeignKeyAttributeJson,
    IReadOnlyList<OutsystemsTriggerRow> Triggers,
    IReadOnlyList<OutsystemsAttributeJsonRow> AttributeJson,
    IReadOnlyList<OutsystemsRelationshipJsonRow> RelationshipJson,
    IReadOnlyList<OutsystemsIndexJsonRow> IndexJson,
    IReadOnlyList<OutsystemsTriggerJsonRow> TriggerJson,
    IReadOnlyList<OutsystemsModuleJsonRow> ModuleJson,
    string DatabaseName)
{
    public IReadOnlyList<OutsystemsModuleRow> Modules { get; } = Modules ?? throw new ArgumentNullException(nameof(Modules));
    public IReadOnlyList<OutsystemsEntityRow> Entities { get; } = Entities ?? throw new ArgumentNullException(nameof(Entities));
    public IReadOnlyList<OutsystemsAttributeRow> Attributes { get; } = Attributes ?? throw new ArgumentNullException(nameof(Attributes));
    public IReadOnlyList<OutsystemsReferenceRow> References { get; } = References ?? throw new ArgumentNullException(nameof(References));
    public IReadOnlyList<OutsystemsPhysicalTableRow> PhysicalTables { get; } = PhysicalTables ?? throw new ArgumentNullException(nameof(PhysicalTables));
    public IReadOnlyList<OutsystemsColumnRealityRow> ColumnReality { get; } = ColumnReality ?? throw new ArgumentNullException(nameof(ColumnReality));
    public IReadOnlyList<OutsystemsColumnCheckRow> ColumnChecks { get; } = ColumnChecks ?? throw new ArgumentNullException(nameof(ColumnChecks));
    public IReadOnlyList<OutsystemsColumnCheckJsonRow> ColumnCheckJson { get; } = ColumnCheckJson ?? throw new ArgumentNullException(nameof(ColumnCheckJson));
    public IReadOnlyList<OutsystemsPhysicalColumnPresenceRow> PhysicalColumnsPresent { get; } = PhysicalColumnsPresent ?? throw new ArgumentNullException(nameof(PhysicalColumnsPresent));
    public IReadOnlyList<OutsystemsIndexRow> Indexes { get; } = Indexes ?? throw new ArgumentNullException(nameof(Indexes));
    public IReadOnlyList<OutsystemsIndexColumnRow> IndexColumns { get; } = IndexColumns ?? throw new ArgumentNullException(nameof(IndexColumns));
    public IReadOnlyList<OutsystemsForeignKeyRow> ForeignKeys { get; } = ForeignKeys ?? throw new ArgumentNullException(nameof(ForeignKeys));
    public IReadOnlyList<OutsystemsForeignKeyColumnRow> ForeignKeyColumns { get; } = ForeignKeyColumns ?? throw new ArgumentNullException(nameof(ForeignKeyColumns));
    public IReadOnlyList<OutsystemsForeignKeyAttrMapRow> ForeignKeyAttributeMap { get; } = ForeignKeyAttributeMap ?? throw new ArgumentNullException(nameof(ForeignKeyAttributeMap));
    public IReadOnlyList<OutsystemsAttributeHasFkRow> AttributeForeignKeys { get; } = AttributeForeignKeys ?? throw new ArgumentNullException(nameof(AttributeForeignKeys));
    public IReadOnlyList<OutsystemsForeignKeyColumnsJsonRow> ForeignKeyColumnsJson { get; } = ForeignKeyColumnsJson ?? throw new ArgumentNullException(nameof(ForeignKeyColumnsJson));
    public IReadOnlyList<OutsystemsForeignKeyAttributeJsonRow> ForeignKeyAttributeJson { get; } = ForeignKeyAttributeJson ?? throw new ArgumentNullException(nameof(ForeignKeyAttributeJson));
    public IReadOnlyList<OutsystemsTriggerRow> Triggers { get; } = Triggers ?? throw new ArgumentNullException(nameof(Triggers));
    public IReadOnlyList<OutsystemsAttributeJsonRow> AttributeJson { get; } = AttributeJson ?? throw new ArgumentNullException(nameof(AttributeJson));
    public IReadOnlyList<OutsystemsRelationshipJsonRow> RelationshipJson { get; } = RelationshipJson ?? throw new ArgumentNullException(nameof(RelationshipJson));
    public IReadOnlyList<OutsystemsIndexJsonRow> IndexJson { get; } = IndexJson ?? throw new ArgumentNullException(nameof(IndexJson));
    public IReadOnlyList<OutsystemsTriggerJsonRow> TriggerJson { get; } = TriggerJson ?? throw new ArgumentNullException(nameof(TriggerJson));
    public IReadOnlyList<OutsystemsModuleJsonRow> ModuleJson { get; } = ModuleJson ?? throw new ArgumentNullException(nameof(ModuleJson));
    public string DatabaseName { get; } = DatabaseName ?? throw new ArgumentNullException(nameof(DatabaseName));
}

public sealed record OutsystemsModuleRow(int EspaceId, string EspaceName, bool IsSystemModule, bool ModuleIsActive, string? EspaceKind, Guid? EspaceSsKey);

public sealed record OutsystemsEntityRow(
    int EntityId,
    string EntityName,
    string PhysicalTableName,
    int EspaceId,
    bool EntityIsActive,
    bool IsSystemEntity,
    bool IsExternalEntity,
    string? DataKind,
    Guid? PrimaryKeySsKey,
    Guid? EntitySsKey,
    string? EntityDescription);

public sealed record OutsystemsAttributeRow(
    int AttrId,
    int EntityId,
    string AttrName,
    Guid? AttrSsKey,
    string? DataType,
    int? Length,
    int? Precision,
    int? Scale,
    string? DefaultValue,
    bool IsMandatory,
    bool AttrIsActive,
    bool? IsAutoNumber,
    bool? IsIdentifier,
    int? RefEntityId,
    string? OriginalName,
    string? ExternalColumnType,
    string? DeleteRule,
    string? PhysicalColumnName,
    string? DatabaseColumnName,
    string? LegacyType,
    int? Decimals,
    string? OriginalType,
    string? AttrDescription);

public sealed record OutsystemsReferenceRow(int AttrId, int? RefEntityId, string? RefEntityName, string? RefPhysicalName);

public sealed record OutsystemsPhysicalTableRow(int EntityId, string SchemaName, string TableName, int ObjectId);

public sealed record OutsystemsColumnRealityRow(
    int AttrId,
    bool IsNullable,
    string SqlType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    string? CollationName,
    bool IsIdentity,
    bool IsComputed,
    string? ComputedDefinition,
    string? DefaultConstraintName,
    string? DefaultDefinition,
    string PhysicalColumn);

public sealed record OutsystemsColumnCheckRow(int AttrId, string ConstraintName, string Definition, bool IsNotTrusted);

public sealed record OutsystemsColumnCheckJsonRow(int AttrId, string CheckJson);

public sealed record OutsystemsPhysicalColumnPresenceRow(int AttrId);

public sealed record OutsystemsIndexRow(
    int EntityId,
    int ObjectId,
    int IndexId,
    string IndexName,
    bool IsUnique,
    bool IsPrimary,
    string Kind,
    string? FilterDefinition,
    bool IsDisabled,
    bool IsPadded,
    int? FillFactor,
    bool IgnoreDupKey,
    bool AllowRowLocks,
    bool AllowPageLocks,
    bool NoRecompute,
    string? DataSpaceName,
    string? DataSpaceType,
    string? PartitionColumnsJson,
    string? DataCompressionJson);

public sealed record OutsystemsIndexColumnRow(
    int EntityId,
    string IndexName,
    int Ordinal,
    string PhysicalColumn,
    bool IsIncluded,
    string? Direction,
    string? HumanAttr);

public sealed record OutsystemsForeignKeyRow(
    int EntityId,
    int FkObjectId,
    string FkName,
    string DeleteAction,
    string UpdateAction,
    int ReferencedObjectId,
    int? ReferencedEntityId,
    string ReferencedSchema,
    string ReferencedTable,
    bool IsNoCheck);

public sealed record OutsystemsForeignKeyColumnRow(
    int EntityId,
    int FkObjectId,
    int Ordinal,
    string ParentColumn,
    string ReferencedColumn,
    int? ParentAttrId,
    string? ParentAttrName,
    int? ReferencedAttrId,
    string? ReferencedAttrName);

public sealed record OutsystemsForeignKeyAttrMapRow(int AttrId, int FkObjectId);

public sealed record OutsystemsAttributeHasFkRow(int AttrId, bool HasFk);

public sealed record OutsystemsForeignKeyColumnsJsonRow(int FkObjectId, string ColumnsJson);

public sealed record OutsystemsForeignKeyAttributeJsonRow(int AttrId, string ConstraintJson);

public sealed record OutsystemsTriggerRow(int EntityId, string TriggerName, bool IsDisabled, string TriggerDefinition);

public sealed record OutsystemsAttributeJsonRow(int EntityId, string? AttributesJson);

public sealed record OutsystemsRelationshipJsonRow(int EntityId, string RelationshipsJson);

public sealed record OutsystemsIndexJsonRow(int EntityId, string IndexesJson);

public sealed record OutsystemsTriggerJsonRow(int EntityId, string TriggersJson);

public sealed record OutsystemsModuleJsonRow(string ModuleName, bool IsSystem, bool IsActive, string ModuleEntitiesJson);
