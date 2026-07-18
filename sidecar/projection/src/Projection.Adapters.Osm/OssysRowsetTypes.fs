namespace Projection.Adapters.Osm

open Projection.Core

/// OSSYS rowset / JSON DTO record types — the wire-shape DTOs the
/// `osm_model.json` JSON path and the V1 metadata-rowset path both
/// translate from. Extracted verbatim from `CatalogReader` (2026-06-04
/// R1 decomposition) so the reader implementations and external
/// consumers (`MetadataSnapshotRunner`) share one types home. The
/// `SnapshotSource` parse-input DU stays in `CatalogReader` (its cases
/// are the public entry surface). No `[<RequireQualifiedAccess>]`: the
/// reader modules `open` this.
module OssysRowsetTypes =
    /// V1 rowset 1 — `#E` modules; chapter 3.2 slice 1. Hand-written
    /// F# transcription of V1's `OutsystemsModuleRow` DTO at
    /// `IOutsystemsMetadataReader.cs:71-87`, mapped to V2 algebraic
    /// vocabulary (`Module` / `Espace` aliasing per V1 OSSYS
    /// convention; the `Espace*` field names mirror V1's SQL surface).
    /// `EspaceSsKey` is the load-bearing addition over the JSON path
    /// (which drops it via `SnapshotJsonBuilder`'s field selection).
    ///
    /// **Slice 3 extension:** `EspaceKind` lifts V1's `ossys_Espace.EspaceKind`
    /// string (rowset 1; `OutsystemsModuleRow.EspaceKind`). Refines
    /// rule 17's `Origin` translation from the JSON-path two-way
    /// placeholder (`isExternal → ExternalIndirect`) to
    /// the three-way real (Native / ExternalIndirect /
    /// ExternalDirect). Empirical V1 values observed in fixtures:
    /// `"eSpace"` for normal modules (V1 test seed at
    /// tests/Fixtures/sql/model.edge-case.seed.sql:97-99). The IS-
    /// extension marker is conventionally `"Extension"` per
    /// `DECISIONS 2026-05-19 — rule 17` ("Extension" — or whatever
    /// the IS-marker turns out to be); this slice adopts `"Extension"`
    /// as the operative marker until a real V1 production sample
    /// surfaces a different string. Nullable (V1 column is nullable).
    type ModuleRow =
        {
            EspaceId       : int
            EspaceName     : string
            IsSystemModule : bool
            IsActive       : bool
            EspaceKind     : string option
            EspaceSsKey    : System.Guid option
        }

    /// V1 rowset 2 — `#Ent` entities; chapter 3.2 slice 1. Mapped to
    /// V2's `Kind` algebraic vocabulary (V2 uses `Kind` where V1 uses
    /// `Entity`). FK to `ModuleRow.EspaceId` (linkage flat across
    /// `RowsetBundle.Modules`/`RowsetBundle.Kinds`/`RowsetBundle.Attributes`,
    /// matching V1 SQL's normalized rowset shape; `parseRowsetBundle`
    /// joins on load). `EntitySsKey` + `PrimaryKeySsKey` are the
    /// load-bearing additions; `EspaceKind` (slice 3) on `ModuleRow`
    /// distinguishes Origin three-way.
    ///
    /// **Slice 4 extension:** `IsSystemEntity` lifts V1's
    /// `ossys_Entity.Is_System` column (rowset 2; previously dropped
    /// at the JSON projection layer). Lifts into a new V2 IR refinement
    /// — `ModalityMark.SystemOwned` — payload-free mark sibling to
    /// `TenantScoped` / `SoftDeletable`. Rationale for the IR
    /// refinement choice (boundary-discipline question per chapter
    /// 3.2 open):
    ///   - Flat `Kind.IsSystem: bool` rejected — V2 convention avoids
    ///     `Is*` booleans in the IR.
    ///   - `Origin` expansion (`OsNativeSystem`) rejected — system-
    ///     entity is orthogonal to native-vs-external; conflating
    ///     axes loses information.
    ///   - New `Kind.Stewardship: Stewardship` DU rejected — heavier
    ///     surface than evidence demands; defer until a second
    ///     stewardship axis surfaces (e.g., third-party-managed).
    ///   - `ModalityMark.SystemOwned` selected — matches existing
    ///     orthogonal-axes-list pattern; payload-free; consumers
    ///     walk `kind.Modality |> List.contains SystemOwned`.
    type KindRow =
        {
            EntityId          : int
            EspaceId          : int
            EntityName        : string
            PhysicalTableName : string
            DbSchema          : string
            IsStatic          : bool
            IsExternal        : bool
            IsSystemEntity    : bool
            IsActive          : bool
            EntitySsKey       : System.Guid option
            PrimaryKeySsKey   : System.Guid option
            /// Chapter A.0' slice α — Description lift. Carries V1's
            /// `ossys_Entity.Description` column. `None` when V1's
            /// source row is NULL.
            Description       : string option
        }

    /// V1 rowset 3 — `#Attr` attributes; chapter 3.2 slice 1. FK to
    /// `KindRow.EntityId`. `AttrSsKey` is the load-bearing addition
    /// over the JSON path. `IsActive` is carried into the V2 IR's
    /// `Attribute.IsActive` field per chapter A.0' slice β (the
    /// session-21 boundary filter was retired; this DTO field is now
    /// the rowset-path provenance for the IR field).
    type AttributeRow =
        {
            AttrId       : int
            EntityId     : int
            AttrName     : string
            PhysicalCol  : string
            DataType     : string
            /// Authored `ossys_Entity_Attr.Default_Value` — the logical
            /// Service-Studio default surface (`False` for BIT, etc.).
            /// `None` when the estate authored no default. Projects via
            /// `SqlLiteral.ofRaw` against the resolved `PrimitiveType`
            /// into `Attribute.DefaultValue` (same shape as the JSON
            /// path's `default` field).
            DefaultValue : string option
            IsMandatory  : bool
            IsIdentifier : bool
            IsAutoNumber : bool
            Length       : int option
            Precision    : int option
            Scale        : int option
            AttrSsKey    : System.Guid option
            IsActive     : bool
            /// Chapter A.0' slice α — Description lift. Carries V1's
            /// `ossys_EntityAttr.Description` column. `None` when V1's
            /// source row is NULL.
            Description  : string option
            /// Chapter 4.9 slice β — OriginalName lift (rowset path).
            /// V1's `ossys_EntityAttr.OriginalName` column. `None` when
            /// no rename history is recorded.
            OriginalName : string option
            /// Chapter 4.9 slice β — ExternalColumnType lift (rowset
            /// path). V1's `ossys_EntityAttr.ExternalColumnType` column.
            /// `None` for OS-native entities and when V1 omits the
            /// override.
            ExternalDatabaseType : string option
            /// Slice A.4.7'-prelude.row53-source-side — V1
            /// `#ColumnReality.IsComputed` (sys.columns.is_computed).
            /// `true` when the column is a SQL Server computed column.
            /// Pairs with `ComputedDefinition` to populate
            /// `Attribute.Computed : ComputedColumnConfig option`.
            IsComputed           : bool
            /// Slice A.4.7'-prelude.row53-source-side — V1
            /// `#ColumnReality.ComputedDefinition`
            /// (sys.computed_columns.definition). The expression text
            /// of a computed column (e.g., `([Base] * 2)`). `None`
            /// when the column is not computed. Combined with
            /// `IsComputed` to construct `ComputedColumnConfig`.
            ComputedDefinition   : string option
            /// Slice A.4.7'-prelude.row53-source-side — V1
            /// `#ColumnReality.DefaultConstraintName`
            /// (sys.default_constraints.name). V1's deployed-target
            /// DEFAULT constraint identifier (e.g.,
            /// `DF_Customer_CreatedAt`). `None` when no named DEFAULT
            /// constraint exists. Threads to `Attribute.DefaultName`
            /// for round-trip parity with V1 emission.
            DefaultConstraintName : string option
            /// WP8 / NM-72 — Service-Studio authored attribute order,
            /// carried from the real `ossys_Entity_Attr.Order_Num`
            /// column. `None` when the source estate lacks the column
            /// (the rowset SQL COALESCEs to the attribute's creation
            /// `Id` as a stable fallback, so this is rarely `None` on a
            /// live extraction; the JSON-fallback and hand-built models
            /// carry `None`). Threads to `Attribute.Order`, which the
            /// `CanonicalizeIdentity` pass consumes for emission column
            /// ordering `(PK first, then Order ascending, then SsKey)`.
            Order : int option
            /// F1 (audit 2026-06-17) — V1 `#ColumnReality.CollationName`
            /// (`sys.columns.collation_name`). The column's deployed SQL Server
            /// collation when non-default; `None` when the source carries no
            /// collation (database default) or the rowset path's source-side
            /// reflection didn't fire. Threads to `ColumnRealization.Collation`
            /// so a fresh deploy re-emits the team's chosen `COLLATE`.
            Collation : string option
            /// V1 `#ColumnReality.SqlType` + facets (`sys.columns` ⋈
            /// `sys.types`), parsed at the snapshot boundary into the same
            /// typed channel `external_dbType` resolves through. The
            /// DEPLOYED storage of the column — concrete evidence the type
            /// resolver may consult where the logical type is a
            /// convention rather than a declaration (reference-shaped
            /// `bt*` attributes whose deployed storage diverges from the
            /// BIGINT reference convention). `None` when the deployed
            /// reflection didn't fire or the deployed type is
            /// unrecognized.
            DeployedStorage : SqlStorageType option
            /// DECISIONS 2026-07-18 (decision 2; #669 M-3 / EF-18) — the
            /// DEPLOYED nullability (`#ColumnReality.IsNullable`,
            /// `sys.columns.is_nullable`). `Some false` (deployed NOT
            /// NULL) is PRESERVED at the lift: the emitted column stays
            /// NOT NULL even when the model marks the attribute optional
            /// — deployed-schema over model; an incremental apply can
            /// never drive a DBA-tightened column back to NULL. `Some
            /// true` and `None` leave the model's declaration in charge.
            DeployedIsNullable : bool option
            /// DECISIONS 2026-07-18 (#669 EF-21) — the deployed PERSISTED
            /// marking of a computed column (`sys.computed_columns
            /// .is_persisted`). Threads into `ComputedColumnConfig
            /// .IsPersisted`; the emission already renders the PERSISTED
            /// keyword, so carrying the fact closes the round-trip.
            IsPersisted : bool
        }

    /// V1 rowset 4 — `#RefResolved` resolved-reference rows; chapter
    /// 3.2 slice 2. One row per attribute that bears a foreign-key
    /// reference. FK to `AttributeRow.AttrId`. `RefEntityName`
    /// resolves the target kind by name (V1's `#RefResolved`
    /// aggregates the cross-module name resolution).
    /// `DeleteRuleCode` + `HasDbConstraint` come from V1's `#FkReality`
    /// (rowset 12); denormalized here so the V2 adapter sees
    /// per-reference completeness without joining a third rowset.
    /// The future C# loader pre-joins V1's `#RefResolved` ⊕ `#FkReality`
    /// → flat `ReferenceRow` records; in-memory test fixtures
    /// construct these literals directly. Same-module assumption
    /// (rule 16): `RefEntityName` is resolved within the source
    /// attribute's module. Cross-module FK references are a
    /// documented deferral (DECISIONS Active deferrals index —
    /// "Cross-module FK IR refinement").
    type ReferenceRow =
        {
            AttrId          : int
            RefEntityName   : string
            /// V1 entity ID of the reference target. When the target
            /// entity row carries its own `EntitySsKey` (V1 GUID-based
            /// identity), the rowset adapter must resolve the target
            /// SsKey via this ID rather than synthesizing the key from
            /// `(SourceModule, RefEntityName)` (which produces a
            /// different SsKey shape and breaks the danglingTarget
            /// invariant for cross-key-shape catalogs). Chapter 5.0
            /// slice γ — added to make GUID-bearing rowset bundles
            /// produce valid Catalogs end-to-end. `None` when the
            /// reference's target ID is unknown (defensive default;
            /// fallback to synthesized key).
            RefEntityId     : int option
            DeleteRuleCode  : string option
            HasDbConstraint : bool
            /// Slice 5.13.fk-features-emit cash-out (matrix row 58
            /// adapter-wiring residual). Optional ON UPDATE
            /// referential action carried from V1's
            /// `#FkReality.UpdateAction` per the
            /// `OssysReferenceRow → OssysFkColumnRow → OssysFkRealityRow`
            /// JOIN at `toBundle`. `None` when the rowset bundle
            /// doesn't surface a matching FK constraint (cross-
            /// catalog / JSON-path / non-OSSYS-source).
            OnUpdate        : string option
            /// WP-1b (DECISIONS 2026-07-16). Optional reflected ON DELETE
            /// referential action carried from V1's
            /// `#FkReality.DeleteAction` (SQL-Server vocabulary —
            /// `NO_ACTION` / `CASCADE` / `SET_NULL` / `SET_DEFAULT`, the same
            /// domain as `OnUpdate`, distinct from the OutSystems
            /// `DeleteRuleCode`) via the
            /// `OssysReferenceRow → OssysFkColumnRow → OssysFkRealityRow`
            /// JOIN at `toBundle`. `Some` iff the reference is physically
            /// backed; the rowset reader prefers it over `DeleteRuleCode`
            /// for the emitted action, and the runner names the divergence
            /// when they disagree. `None` for logical-only references and on
            /// the JSON / cross-catalog / non-OSSYS-source paths.
            ReflectedOnDelete : string option
            /// Slice 5.13.fk-features-emit cash-out (matrix row 59
            /// adapter-wiring residual). `false` when V1's
            /// `#FkReality.IsNoCheck = 1` flows through the same JOIN
            /// path; `true` (default) preserves V1's TRUSTED-by-default
            /// emission shape. Cross-catalog and JSON-path references
            /// default to `true`.
            IsConstraintTrusted : bool
        }

    /// V1 rowset bundle — the in-memory carrier
    /// `Projection.Adapters.OssysSql.MetadataSnapshotRunner.toBundle`
    /// produces (an F# `Microsoft.Data.SqlClient` loader, not the
    /// once-anticipated C# one; wired into production via
    /// `LiveModelRead.fs`); in-memory test fixtures construct directly. Per
    /// `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` §2-§3: hand-written F#
    /// records mirroring V1's first three rowsets (modules / entities /
    /// attributes); extends under empirical pressure as future
    /// lossiness members surface (`EspaceKind` at slice 3;
    /// `IsSystemEntity` at slice 4; per-table column structure / check
    /// constraints from rowsets 6+ at deferred slices). Flat-list shape
    /// matches V1's normalized rowset SQL output; `parseRowsetBundle`
    /// joins by FK ID columns at load time.
    ///
    /// **Slice 2 extension:** `References` lifts V1's `#RefResolved`
    /// (rowset 4) ⊕ `#FkReality` (rowset 12) into a flat-list join
    /// surface. Adding the field is a closed-DU-style extension on
    /// the record (existing literal sites must add `References = []`
    /// explicitly; the empirical-test discipline applies — the
    /// changed-callers walk catches surprises at compile time).
    /// V1 rowset `#AllIdx` — per-index physical reflection (chapter 5.13
    /// slice ossys-rowsets-cluster; matrix row 15). One row per index
    /// per Kind. JOINs by (EntityId, IndexName) with `IndexColumnRow`.
    /// Carries the V1 reflection fields V2's `Index` IR can consume;
    /// fields V2 IR doesn't yet model (IsDisabled, IgnoreDupKey,
    /// DataSpaceName/Type, PartitionColumnsJson, DataCompressionJson)
    /// remain typed on the snapshot but don't flow into IR — those are
    /// matrix rows 55 + 56 (deferred-with-trigger).
    type IndexRow =
        {
            EntityId         : int
            IndexName        : string
            IsUnique         : bool
            IsPrimary        : bool
            FilterDefinition : string option
            IsPadded         : bool
            FillFactor       : int
            AllowRowLocks    : bool
            AllowPageLocks   : bool
            NoRecompute      : bool
            /// Slice 5.13.fk-reality-join (paired with index-features
            /// adapter wiring) — `false` (V1 default) when the index
            /// is enabled; `true` when V1's `#AllIdx.IsDisabled = 1`.
            /// Threads to `Index.IsDisabled`; the SSDT emitter yields
            /// a post-CREATE-INDEX `ALTER INDEX … DISABLE` when set.
            IsDisabled       : bool
            /// Slice 5.13.fk-reality-join (paired) — `false` (V1
            /// default); `true` when V1's `#AllIdx.IgnoreDupKey = 1`.
            /// Threads to `Index.IgnoreDuplicateKey`; the SSDT emitter
            /// adds `IGNORE_DUP_KEY = ON` to the WITH clause.
            IgnoreDupKey     : bool
            /// Slice 5.13.fk-reality-join (paired) — single-value
            /// data compression level when uniform across partitions
            /// (`"NONE" | "ROW" | "PAGE"`). `None` when the index
            /// has no explicit compression option set OR carries
            /// heterogeneous per-partition compression (the partition
            /// axis is the row 56 residual). Threads to
            /// `Index.DataCompression`.
            DataCompression  : string option
            /// Slice A.4.7'-prelude.row56-dataspace (LR7 closure) —
            /// optional index dataspace placement carried from V1
            /// `#AllIdx.DataSpaceName` + `DataSpaceType` + (for
            /// partition schemes) `PartitionColumnsJson`. `None`
            /// when no DataSpace is set OR when V1's `type_desc`
            /// is something other than `ROWS_FILEGROUP` /
            /// `PARTITION_SCHEME` (defensive — V2 omits the `ON`
            /// clause rather than emitting an unrecognized
            /// dataspace shape).
            DataSpace        : DataSpace option
        }

    /// V1 rowset `#IdxColsMapped` — per-index column membership
    /// (chapter 5.13 slice ossys-rowsets-cluster; matrix row 16).
    /// One row per column per Index per Kind. `Direction` is V1's
    /// per-column sort direction (`"ASC"` / `"DESC"` / `null` →
    /// Ascending under SQL Server semantics). `HumanAttr` is V1's
    /// logical attribute name (preferred for SsKey resolution);
    /// `PhysicalColumn` is the fallback when the index references a
    /// column not in V1's logical attribute set.
    type IndexColumnRow =
        {
            EntityId       : int
            IndexName      : string
            Ordinal        : int
            HumanAttr      : string option
            PhysicalColumn : string option
            IsIncluded     : bool
            Direction      : string option
        }

    /// V1 rowset `#Triggers` — per-trigger physical reflection
    /// (chapter 5.13 slice ossys-rowsets-cluster; matrix row 23). One
    /// row per trigger per Kind. The trigger Definition is the full
    /// T-SQL `CREATE TRIGGER ...` body V1 reconstructs from
    /// `sys.sql_modules`; V2's `Trigger.Definition` IR field carries
    /// it through to emit. Triggers with empty/missing Definition lift
    /// to `Trigger.Definition = ""` — defensive shape per `Trigger.create`'s
    /// blank-rejection invariant; the adapter filters such rows out
    /// before constructing the IR.
    type TriggerRow =
        {
            EntityId    : int
            TriggerName : string
            IsDisabled  : bool
            Definition  : string option
        }

    /// Rowset 25 — one system-versioned entity's temporal configuration
    /// (DECISIONS 2026-07-18; #669 EF-23). Lifts into
    /// `ModalityMark.Temporal` so the estate board's temporal
    /// dealbreaker fires on rowset-sourced catalogs.
    type TemporalRow =
        {
            EntityId       : int
            HistorySchema  : string option
            HistoryTable   : string option
            PeriodStart    : string option
            PeriodEnd      : string option
            RetentionValue : int option
            RetentionUnit  : string option
        }

    /// Rowset 24 — one deployed `sys.sequences` row (DECISIONS 2026-07-18;
    /// #669 EF-22). The ten axes mirror `ReadSide`'s reflection so both
    /// lanes reconstruct the same `Sequence` values.
    type SequenceRow =
        {
            Schema       : string
            Name         : string
            DataType     : string
            StartValue   : decimal option
            Increment    : decimal option
            MinimumValue : decimal option
            MaximumValue : decimal option
            IsCycling    : bool
            IsCached     : bool
            CacheSize    : int option
        }

    /// V1 rowset `#ColumnCheckReality` — per-column CHECK constraint
    /// reflection (chapter 5.13 slice ossys-rowsets-cluster; matrix
    /// row 12). One row per CHECK constraint per attribute; V2's
    /// `Kind.ColumnChecks` IR is table-scoped (one list per Kind)
    /// because a CHECK can reference multiple columns. The adapter
    /// groups rows by the AttrId's owning Kind and produces a flat
    /// per-Kind `ColumnCheck list`.
    type ColumnCheckRow =
        {
            AttrId         : int
            ConstraintName : string
            /// `None` when the reading principal lacks VIEW DEFINITION —
            /// the managed-cloud grant (2026-07-06, the phase-2 mock-env
            /// program): `sys.check_constraints.definition` NULLs out and
            /// the constraint cannot be represented; the parse SKIPS it
            /// (named in the parser's docstring) instead of failing the
            /// whole extraction.
            Definition     : string option
            IsNotTrusted   : bool
        }

    type RowsetBundle =
        {
            Modules      : ModuleRow list
            Kinds        : KindRow list
            Attributes   : AttributeRow list
            References   : ReferenceRow list
            /// V1 rowset 9 (`#AllIdx`) — per-index physical reflection.
            /// Lifts into `Kind.Indexes` via JOIN with `IndexColumns`.
            /// Empty for fixtures / sources that don't carry index
            /// reality (the JSON path produces empty here; rowset
            /// path populates from `#AllIdx`).
            Indexes      : IndexRow list
            /// V1 rowset 10 (`#IdxColsMapped`) — per-index column
            /// membership. JOINs to `IndexRow` by (EntityId, IndexName).
            IndexColumns : IndexColumnRow list
            /// V1 rowset 17 (`#Triggers`) — per-trigger reflection.
            /// Lifts into `Kind.Triggers`.
            Triggers     : TriggerRow list
            /// V1 rowset 6 (`#ColumnCheckReality`) — per-column CHECK
            /// constraints. Lifts into `Kind.ColumnChecks` (grouped
            /// by the AttrId's owning Kind).
            ColumnChecks : ColumnCheckRow list
            /// Rowset 24 (DECISIONS 2026-07-18; #669 EF-22) — the deployed
            /// `sys.sequences` rows. Lifts into `Catalog.Sequences`; the
            /// emission already renders CREATE SEQUENCE, so carriage
            /// closes the round-trip. Empty for sources without the
            /// extended extraction (fixtures; the JSON path).
            Sequences : SequenceRow list
            /// Rowset 25 (DECISIONS 2026-07-18; #669 EF-23) — per-entity
            /// system-versioning configuration. Lifts into
            /// `ModalityMark.Temporal`; the estate board's temporal
            /// dealbreaker (and the publish refusal) fire from the mark.
            /// Empty for sources without the extended extraction.
            Temporal : TemporalRow list
        }

    /// Empty RowsetBundle helper. Test fixtures + JSON-path placeholders
    /// use `{ RowsetBundle.empty with Modules = ...; ... }` to override
    /// only the populated axes; rowset-extension fields default to `[]`
    /// per IR-grows-under-evidence (the per-axis-extension lift trigger
    /// is the slice that wires the consuming IR). Per slice
    /// 5.13.ossys-rowsets-cluster: lifting `Indexes`, `IndexColumns`,
    /// `Triggers`, `ColumnChecks` extended the record shape; this empty
    /// helper retires the literal-site-explosion that follows record
    /// extensions.
    [<RequireQualifiedAccess>]
    module RowsetBundle =
        let empty : RowsetBundle =
            { Modules      = []
              Kinds        = []
              Attributes   = []
              References   = []
              Indexes      = []
              IndexColumns = []
              Triggers     = []
              ColumnChecks = []
              Sequences    = []
              Temporal     = [] }
