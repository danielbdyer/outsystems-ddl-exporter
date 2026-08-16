namespace Projection.Tests

open System
open Projection.Adapters.OssysSql

/// Declarative builders for `MetadataSnapshotRunner.MetadataSnapshot` rows —
/// the data-sink chapter's pure-pool test substrate (S1; CHAPTER_SINK_OPEN.md).
/// Promotes the `moduleRowWith`/`kindRowWith` idiom from
/// `IsActiveCarryThroughTests` (bundle grain) to the runner's snapshot grain:
/// every builder returns a fully-constructed record with a minimal-valid
/// default per field, and call sites reshape with `{ … with … }` so the
/// declared test input shows exactly the axes it exercises.
///
/// `fullyPopulated` is the codec-totality substrate (S3): one row per rowset
/// with EVERY option `Some` and every scalar non-default, varied by an integer
/// seed — the executable guard against the survival-rule-7 field-drop trap
/// (a codec or reconstruction site that silently zeroes an omitted field
/// cannot round-trip it).
[<RequireQualifiedAccess>]
module OssysSnapshotBuilders =

    /// Deterministic RFC-4122-shaped Guid from two ints — no ambient
    /// randomness (test inputs are declared, never drawn).
    let guidOf (hi: int) (lo: int) : Guid =
        Guid.Parse(sprintf "%08x-0000-4000-8000-%012x" hi lo)

    let moduleRow (espaceId: int) (name: string) : MetadataSnapshotRunner.OssysModuleRow =
        { EspaceId       = espaceId
          EspaceName     = name
          IsSystemModule = false
          IsActive       = true
          EspaceKind     = Some "eSpace"
          EspaceSsKey    = Some (guidOf 0x4f espaceId) }

    let entityRow (entityId: int) (espaceId: int) (name: string) (physicalTable: string) : MetadataSnapshotRunner.OssysEntityRow =
        { EntityId          = entityId
          EntityName        = name
          PhysicalTableName = physicalTable
          EspaceId          = espaceId
          IsActive          = true
          IsSystemEntity    = false
          IsExternal        = false
          DataKind          = Some "entity"
          PrimaryKeySsKey   = Some (guidOf 0x5a entityId)
          EntitySsKey       = Some (guidOf 0x5e entityId)
          Description       = None }

    let attributeRow (attrId: int) (entityId: int) (name: string) : MetadataSnapshotRunner.OssysAttributeRow =
        { AttrId         = attrId
          EntityId       = entityId
          AttrName       = name
          AttrSsKey      = Some (guidOf 0x5c attrId)
          DataType       = Some "Text"
          Length         = Some 50
          Precision      = None
          Scale          = None
          DefaultValue   = None
          IsMandatory    = false
          IsActive       = true
          IsAutoNumber   = false
          IsIdentifier   = false
          RefEntityId    = None
          OriginalName   = None
          ExternalDbType = None
          DeleteRule     = None
          PhysicalCol    = name.ToUpperInvariant()
          Description    = None
          Order          = Some 10 }

    /// The identifier attribute every parseable kind needs (Is_Identifier +
    /// Is_AutoNumber, physical `ID`, authored order 1).
    let identifierRow (attrId: int) (entityId: int) : MetadataSnapshotRunner.OssysAttributeRow =
        { attributeRow attrId entityId "Id" with
            DataType     = Some "Identifier"
            Length       = None
            IsMandatory  = true
            IsAutoNumber = true
            IsIdentifier = true
            PhysicalCol  = "ID"
            Order        = Some 1 }

    let referenceRow (attrId: int) (refEntityId: int) (refEntityName: string) : MetadataSnapshotRunner.OssysReferenceRow =
        { AttrId          = attrId
          RefEntityId     = Some refEntityId
          RefEntityName   = Some refEntityName
          RefPhysicalName = None }

    let physicalTableRow (entityId: int) (schema: string) (table: string) : MetadataSnapshotRunner.OssysPhysicalTableRow =
        { EntityId   = entityId
          SchemaName = schema
          TableName  = table
          ObjectId   = 100000 + entityId }

    let columnRealityRow (attrId: int) (physicalColumn: string) : MetadataSnapshotRunner.OssysColumnRealityRow =
        { AttrId                = attrId
          IsNullable            = true
          SqlType               = Some "nvarchar"
          MaxLength             = Some 100
          Precision             = None
          Scale                 = None
          CollationName         = None
          IsIdentity            = false
          IsComputed            = false
          ComputedDefinition    = None
          DefaultConstraintName = None
          DefaultDefinition     = None
          PhysicalColumn        = Some physicalColumn
          IsPersisted           = false }

    let columnCheckRow (attrId: int) (constraintName: string) : MetadataSnapshotRunner.OssysColumnCheckRow =
        { AttrId         = attrId
          ConstraintName = constraintName
          Definition     = Some "([VALUE]>=(0))"
          IsNotTrusted   = false }

    let physColsPresentRow (attrId: int) : MetadataSnapshotRunner.OssysPhysColsPresentRow =
        { AttrId = attrId }

    let indexRow (entityId: int) (indexName: string) : MetadataSnapshotRunner.OssysAllIdxRow =
        { EntityId             = entityId
          ObjectId             = 100000 + entityId
          IndexId              = 2
          IndexName            = indexName
          IsUnique             = false
          IsPrimary            = false
          Kind                 = "INDEX"
          FilterDefinition     = None
          IsDisabled           = false
          IsPadded             = false
          FillFactor           = 0
          IgnoreDupKey         = false
          AllowRowLocks        = true
          AllowPageLocks       = true
          NoRecompute          = false
          DataSpaceName        = None
          DataSpaceType        = None
          PartitionColumnsJson = None
          DataCompressionJson  = None }

    let indexColumnRow (entityId: int) (indexName: string) (ordinal: int) (physicalColumn: string) : MetadataSnapshotRunner.OssysIdxColMappedRow =
        { EntityId       = entityId
          IndexName      = indexName
          Ordinal        = ordinal
          PhysicalColumn = Some physicalColumn
          IsIncluded     = false
          Direction      = Some "ASC"
          HumanAttr      = None }

    let fkRealityRow (entityId: int) (fkName: string) (referencedEntityId: int) : MetadataSnapshotRunner.OssysFkRealityRow =
        { EntityId           = entityId
          FkObjectId         = 200000 + entityId
          FkName             = fkName
          DeleteAction       = Some "NO_ACTION"
          UpdateAction       = Some "NO_ACTION"
          ReferencedObjectId = 100000 + referencedEntityId
          ReferencedEntityId = Some referencedEntityId
          ReferencedSchema   = Some "dbo"
          ReferencedTable    = None
          IsNoCheck          = false }

    let fkColumnRow (entityId: int) (fkObjectId: int) (parentColumn: string) (referencedColumn: string) : MetadataSnapshotRunner.OssysFkColumnRow =
        { EntityId           = entityId
          FkObjectId         = fkObjectId
          Ordinal            = 1
          ParentColumn       = parentColumn
          ReferencedColumn   = referencedColumn
          ParentAttrId       = None
          ParentAttrName     = None
          ReferencedAttrId   = None
          ReferencedAttrName = None }

    let triggerRow (entityId: int) (triggerName: string) : MetadataSnapshotRunner.OssysTriggerRow =
        { EntityId          = entityId
          TriggerName       = triggerName
          IsDisabled        = false
          TriggerDefinition = Some (sprintf "CREATE TRIGGER [dbo].[%s] ON [dbo].[T] AFTER INSERT AS BEGIN SET NOCOUNT ON; END" triggerName) }

    let sequenceRow (schema: string) (name: string) : MetadataSnapshotRunner.OssysSequenceRow =
        { Schema       = schema
          Name         = name
          DataType     = "bigint"
          StartValue   = Some 1M
          Increment    = Some 1M
          MinimumValue = Some 1M
          MaximumValue = Some 9999999M
          IsCycling    = false
          IsCached     = true
          CacheSize    = Some 50 }

    let temporalRow (entityId: int) : MetadataSnapshotRunner.OssysTemporalRow =
        { EntityId       = entityId
          HistorySchema  = Some "dbo"
          HistoryTable   = Some "T_HISTORY"
          PeriodStart    = Some "VALIDFROM"
          PeriodEnd      = Some "VALIDTO"
          RetentionValue = Some 6
          RetentionUnit  = Some "Months" }

    /// The everything-present capability vector (a modern estate).
    let capabilityRow : MetadataSnapshotRunner.OssysCapabilityRow =
        { HasDataType           = true
          HasType               = true
          HasPrecision          = true
          HasScale              = true
          HasDecimals           = true
          HasOriginalName       = true
          HasExternalColumnType = true
          HasPhysicalColumnName = true
          HasDatabaseName       = true
          HasIsIdentifier       = true
          HasRefEntityId        = true
          HasIsAutoNumber       = true
          HasDefaultValue       = true
          HasDeleteRule         = true
          HasOriginalType       = true
          HasAttrSsKey          = true
          HasLength             = true
          HasOrderNum           = true
          HasEntityDescription  = true }

    let emptySnapshot : MetadataSnapshotRunner.MetadataSnapshot =
        { Modules            = []
          Entities           = []
          Attributes         = []
          References         = []
          PhysicalTables     = []
          ColumnReality      = []
          ColumnChecks       = []
          PhysColsPresent    = []
          Indexes            = []
          IndexColumns       = []
          ForeignKeysReality = []
          ForeignKeyColumns  = []
          Triggers           = []
          Sequences          = []
          Temporal           = []
          Capabilities       = []
          Scope              = MetadataSnapshotRunner.AcquisitionScope.Total }

    /// The common trio — modules + entities + attributes, physical axes empty.
    let snapshotOf
        (modules: MetadataSnapshotRunner.OssysModuleRow list)
        (entities: MetadataSnapshotRunner.OssysEntityRow list)
        (attributes: MetadataSnapshotRunner.OssysAttributeRow list)
        : MetadataSnapshotRunner.MetadataSnapshot =
        { emptySnapshot with
            Modules    = modules
            Entities   = entities
            Attributes = attributes }

    /// One row per rowset, EVERY option `Some`, every scalar non-default,
    /// varied by `seed` — the codec-totality substrate. A field a codec (or
    /// any reconstruction site) drops cannot survive
    /// `deserialize (serialize (fullyPopulated seed))` against this value.
    let fullyPopulated (seed: int) : MetadataSnapshotRunner.MetadataSnapshot =
        // abs AFTER the modulo: `abs Int32.MinValue` throws, and FsCheck
        // generates the extremes by design.
        let s = abs (seed % 10000)
        let tag (label: string) = sprintf "%s_%d" label s
        { Modules =
            [ { EspaceId       = 800 + s
                EspaceName     = tag "Module"
                IsSystemModule = true
                IsActive       = false
                EspaceKind     = Some "Extension"
                EspaceSsKey    = Some (guidOf 0x4f s) } ]
          Entities =
            [ { EntityId          = 8000 + s
                EntityName        = tag "Entity"
                PhysicalTableName = tag "OSUSR_FUL_T"
                EspaceId          = 800 + s
                IsActive          = false
                IsSystemEntity    = true
                IsExternal        = true
                DataKind          = Some "staticEntity"
                PrimaryKeySsKey   = Some (guidOf 0x5a s)
                EntitySsKey       = Some (guidOf 0x5e s)
                Description       = Some (tag "described") } ]
          Attributes =
            [ { AttrId         = 80000 + s
                EntityId       = 8000 + s
                AttrName       = tag "Attr"
                AttrSsKey      = Some (guidOf 0x5c s)
                DataType       = Some "Decimal"
                Length         = Some (1 + s % 400)
                Precision      = Some 19
                Scale          = Some 4
                DefaultValue   = Some "0.0"
                IsMandatory    = true
                IsActive       = false
                IsAutoNumber   = true
                IsIdentifier   = true
                RefEntityId    = Some (9000 + s)
                OriginalName   = Some (tag "Original")
                ExternalDbType = Some "decimal(19,4)"
                DeleteRule     = Some "Protect"
                PhysicalCol    = tag "COL"
                Description    = Some (tag "attr-described")
                Order          = Some (2 + s % 90) } ]
          References =
            [ { AttrId          = 80000 + s
                RefEntityId     = Some (9000 + s)
                RefEntityName   = Some (tag "RefEntity")
                RefPhysicalName = Some (tag "OSUSR_REF_T") } ]
          PhysicalTables =
            [ { EntityId   = 8000 + s
                SchemaName = "billing"
                TableName  = tag "OSUSR_FUL_T"
                ObjectId   = 100000 + s } ]
          ColumnReality =
            [ { AttrId                = 80000 + s
                IsNullable            = false
                SqlType               = Some "decimal"
                MaxLength             = Some 17
                Precision             = Some 19
                Scale                 = Some 4
                CollationName         = Some "Latin1_General_CI_AI"
                IsIdentity            = true
                IsComputed            = true
                ComputedDefinition    = Some "([A]+[B])"
                DefaultConstraintName = Some (tag "DF_T")
                DefaultDefinition     = Some "((0))"
                PhysicalColumn        = Some (tag "COL")
                IsPersisted           = true } ]
          ColumnChecks =
            [ { AttrId         = 80000 + s
                ConstraintName = tag "CK_T"
                Definition     = Some "([COL]>=(0))"
                IsNotTrusted   = true } ]
          PhysColsPresent =
            [ { AttrId = 80000 + s } ]
          Indexes =
            [ { EntityId             = 8000 + s
                ObjectId             = 100000 + s
                IndexId              = 3 + s % 5
                IndexName            = tag "IDX_T"
                IsUnique             = true
                IsPrimary            = true
                Kind                 = "PRIMARY KEY"
                FilterDefinition     = Some "([COL] IS NOT NULL)"
                IsDisabled           = true
                IsPadded             = true
                FillFactor           = 85
                IgnoreDupKey         = true
                AllowRowLocks        = false
                AllowPageLocks       = false
                NoRecompute          = true
                DataSpaceName        = Some "PRIMARY"
                DataSpaceType        = Some "FG"
                PartitionColumnsJson = Some "[\"COL\"]"
                DataCompressionJson  = Some "[{\"p\":1,\"c\":\"PAGE\"}]" } ]
          IndexColumns =
            [ { EntityId       = 8000 + s
                IndexName      = tag "IDX_T"
                Ordinal        = 1 + s % 3
                PhysicalColumn = Some (tag "COL")
                IsIncluded     = true
                Direction      = Some "DESC"
                HumanAttr      = Some (tag "Attr") } ]
          ForeignKeysReality =
            [ { EntityId           = 8000 + s
                FkObjectId         = 200000 + s
                FkName             = tag "FK_T"
                DeleteAction       = Some "CASCADE"
                UpdateAction       = Some "SET_NULL"
                ReferencedObjectId = 300000 + s
                ReferencedEntityId = Some (9000 + s)
                ReferencedSchema   = Some "billing"
                ReferencedTable    = Some (tag "OSUSR_REF_T")
                IsNoCheck          = true } ]
          ForeignKeyColumns =
            [ { EntityId           = 8000 + s
                FkObjectId         = 200000 + s
                Ordinal            = 1 + s % 2
                ParentColumn       = tag "COL"
                ReferencedColumn   = "ID"
                ParentAttrId       = Some (80000 + s)
                ParentAttrName     = Some (tag "Attr")
                ReferencedAttrId   = Some (90000 + s)
                ReferencedAttrName = Some "Id" } ]
          Triggers =
            [ { EntityId          = 8000 + s
                TriggerName       = tag "TR_T"
                IsDisabled        = true
                TriggerDefinition = Some (tag "CREATE TRIGGER") } ]
          Sequences =
            [ { Schema       = "billing"
                Name         = tag "SEQ_T"
                DataType     = "decimal"
                StartValue   = Some (decimal (100 + s))
                Increment    = Some 5M
                MinimumValue = Some 10M
                MaximumValue = Some (decimal (100000 + s))
                IsCycling    = true
                IsCached     = false
                CacheSize    = Some (10 + s % 40) } ]
          Temporal =
            [ { EntityId       = 8000 + s
                HistorySchema  = Some "billing"
                HistoryTable   = Some (tag "T_HISTORY")
                PeriodStart    = Some "VALIDFROM"
                PeriodEnd      = Some "VALIDTO"
                RetentionValue = Some (1 + s % 12)
                RetentionUnit  = Some "Weeks" } ]
          Capabilities =
            // Every field written explicitly; two bits vary by seed
            // parity so a codec that hardcodes either polarity cannot
            // round-trip both.
            [ { HasDataType           = true
                HasType               = (s % 2 = 0)
                HasPrecision          = true
                HasScale              = true
                HasDecimals           = false
                HasOriginalName       = true
                HasExternalColumnType = true
                HasPhysicalColumnName = true
                HasDatabaseName       = false
                HasIsIdentifier       = true
                HasRefEntityId        = true
                HasIsAutoNumber       = true
                HasDefaultValue       = true
                HasDeleteRule         = true
                HasOriginalType       = false
                HasAttrSsKey          = true
                HasLength             = true
                HasOrderNum           = (s % 2 = 1)
                HasEntityDescription  = true } ]
          // align-II.9 — Total, deliberately: this substrate feeds the
          // DISPLACEMENT algebra's laws too, and that algebra's domain is
          // totality-gated snapshots (only Total is ever witnessed). The
          // codec's non-default scope coverage is the explicit scoped
          // round-trip law in MetadataSnapshotCodecTests (R12).
          Scope = MetadataSnapshotRunner.AcquisitionScope.Total }
