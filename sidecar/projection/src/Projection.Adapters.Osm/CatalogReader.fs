namespace Projection.Adapters.Osm

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Projection.Core

/// Boundary adapter — converts V1's `osm_model.json` snapshot shape
/// into V2's `Catalog` IR.
///
/// **V1↔V2 boundary.** V1's metadata extraction chain
/// (`outsystems_metadata_rowsets.sql` → `MetadataSnapshotRunner` →
/// `SnapshotJsonBuilder` → `osm_model.json`) is the source of truth
/// for OutSystems platform metadata. V2's adapter consumes the JSON
/// document V1 produces. The cherry-pick discipline (`HANDOFF.md`)
/// keeps the boundary as data, not typed cross-references — this
/// adapter does not depend on any V1 C# types.
///
/// **Position B for `ICatalogReader`.** Per `DECISIONS 2026-05-15 —
/// OSSYS adapter parse signature`, the entry-point shape is
/// `SnapshotSource -> Task<Result<Catalog>>`. A future
/// `ICatalogReader` interface (when a second catalog source
/// materializes) wraps this signature trivially via object expression;
/// no retrofit needed.
///
/// **Implementation discipline.** Only what the differential tests
/// demand. The OSSYS implementation chapter accumulates translation
/// rules under empirical pressure from
/// `OsmCatalogReaderDifferentialTests`; speculative DTO design that
/// mirrors V1's full ~22 rowset surface is deliberately avoided.
[<RequireQualifiedAccess>]
module CatalogReader =

    /// The input slot on the parse function. Closed DU.
    ///
    /// **Two variants today; one variant planned (canonical
    /// resolution); one variant reserved.**
    ///
    ///   - `SnapshotFile` and `SnapshotJson` are the current
    ///     consumers. Both feed V1's canonical `osm_model.json`
    ///     shape; SsKey is name-synthesized; the bound on A1 is
    ///     documented (`DECISIONS 2026-05-15 — OSSYS adapter
    ///     translation rules`).
    ///
    ///   - **Planned: `SnapshotRowsets`.** Per the operator
    ///     decision recorded in `DECISIONS 2026-05-15 — OSSYS
    ///     adapter translation rules`, session-20 amendment, the
    ///     canonical resolution to the lossy-SSKey question is to
    ///     consume V1's trailing rowsets directly. Rowsets carry
    ///     SSKey natively and preserve per-table column structure
    ///     the `FOR JSON PATH` aggregations collapse. The variant
    ///     itself lands when chapter 2's organic flow brings it —
    ///     likely after the current OSSYS adapter chapter
    ///     completes its translation work through `SnapshotJson`.
    ///     The operator decision is locked; not subject to
    ///     relitigation.
    ///
    ///   - **Reserved: `LiveOssysConnection`.** A future variant
    ///     for the case where V2 needs to operate without V1's
    ///     chain in the loop entirely. Per `DECISIONS 2026-05-15 —
    ///     OSSYS adapter parse signature`, deferred until its
    ///     specific demand surfaces.
    ///
    /// Adding `SnapshotRowsets` speculatively today would violate
    /// the closed-DU expansion discipline (one consumer needed;
    /// zero exist). The variant is named here so future readers of
    /// the code see the architectural commitment without having to
    /// read DECISIONS to discover it.
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
    /// placeholder (`isExternal → ExternalViaIntegrationStudio`) to
    /// the three-way real (OsNative / ExternalViaIntegrationStudio /
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
            /// Slice 5.13.fk-features-emit cash-out (matrix row 59
            /// adapter-wiring residual). `false` when V1's
            /// `#FkReality.IsNoCheck = 1` flows through the same JOIN
            /// path; `true` (default) preserves V1's TRUSTED-by-default
            /// emission shape. Cross-catalog and JSON-path references
            /// default to `true`.
            IsConstraintTrusted : bool
        }

    /// V1 rowset bundle — the in-memory carrier the future C# SqlClient
    /// loader produces; in-memory test fixtures construct directly. Per
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
            Definition     : string
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
              ColumnChecks = [] }

    type SnapshotSource =
        /// Path to a V1-produced `osm_model.json` file on disk.
        | SnapshotFile of path: string
        /// In-memory snapshot string. Useful for tests and for
        /// pipelines that produce the snapshot in memory rather than
        /// via disk.
        | SnapshotJson of json: string
        /// V1 pre-aggregation rowset bundle. Chapter 3.2 — closes the
        /// JSON-projection-lossiness class (`DECISIONS 2026-05-19 —
        /// naming the two classes`). The rowsets carry SsKey natively
        /// (via `OssysOriginal guid` per `Identity.fs:70`); A1's
        /// "identity survives rename" bound resolves through this
        /// path. Coexists permanently with `SnapshotJson` per
        /// `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` §6 — no
        /// deprecation trigger named.
        | SnapshotRowsets of bundle: RowsetBundle

    let private adapterError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "adapter.osm.%s" code) message

    /// Aggregate underlying errors from a list of `Result.errors`
    /// projections; fall back to a single `adapterError` if no
    /// underlying errors decompose out of the failure (theoretical
    /// case — the outer `match _ -> ...` arm fires on a combination
    /// the explicit branches didn't enumerate).
    ///
    /// Codified at the two-consumer threshold (CLAUDE.md operating-
    /// disciplines table — emergent primitives earn their place
    /// through multi-consumer demand). Four V1↔V2 build-failure
    /// collection sites share this exact shape: `parseKindRow` /
    /// `parseModuleRow` (rowset path; chapter 3.2 slices 1 + 2);
    /// `parseKind` / `parseModule` (JSON path; chapter 2 sessions
    /// 18–25, now corrected). Before extraction the rowset path
    /// carried the inline form and the JSON path swallowed the
    /// underlying error with a generic `kindBuild` / `moduleBuild`
    /// umbrella — losing the substantive cause (e.g.,
    /// `adapter.osm.unmappedDeleteRule` from `parseDeleteRule`).
    /// The helper makes the diagnostic surface honest across both
    /// translation paths uniformly.
    let private propagateOrFallback
        (errorLists: ValidationError list list)
        (fallback: unit -> ValidationError) : Result<'a> =
        let combined = List.concat errorLists
        if List.isEmpty combined then Result.failureOf (fallback ())
        else Error combined

    // -----------------------------------------------------------------------
    // SsKey synthesis — V1's `osm_model.json` does NOT carry SSKey
    // values. SnapshotJsonBuilder writes names and physical-names; the
    // rowsets have SSKeys but the assembled JSON discards them. The
    // V2 parser synthesizes SsKey deterministically from name fields.
    // The synthesis convention matches existing V2 fixture builders
    // and is stable across runs of identical input. Re-open trigger
    // (per session 18 commit 4 DECISIONS entry): a real V1 chain
    // change that emits SSKeys, OR an alternative input shape that
    // carries them.
    // -----------------------------------------------------------------------

    // The synthesis source / basis split (slice 5.5 / `CHAPTER_3_PRESCOPE_
    // ARTIFACTBYKIND_REFACTOR.md` §7) makes A1's JSON-projection-lossiness
    // bound type-visible: `Synthesized (source, basis)` carries the
    // bounded variant tag, which downstream consumers can pattern-match
    // on. Each call site below names the source convention (`OS_MOD`,
    // `OS_KIND`, etc.) explicitly; the basis is the dot-separated
    // identifier coordinate.

    // Per the no-string-concatenation discipline (`DECISIONS
    // 2026-05-09`) + chapter-3.6 slice-δ: synthesis-basis composition
    // flows through `SsKey.synthesizedComposite` over typed component
    // lists; the typed `string list` IS the structure inside the
    // `Synthesized` DU, no `String.concat "_"` at the build path.
    // Strings emerge only at the terminal `SsKey.rootOriginal`
    // display projection.

    let private moduleSsKey (moduleName: string) : Result<SsKey> =
        SsKey.synthesized "OS_MOD" moduleName

    let private kindSsKey (moduleName: string) (entityName: string) : Result<SsKey> =
        SsKey.synthesizedComposite "OS_KIND" [ moduleName; entityName ]

    let private attributeSsKey
        (moduleName: string) (entityName: string) (attrName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_ATTR" [ moduleName; entityName; attrName ]

    /// Reference SsKey synthesis (session 19). The reference identifies
    /// by its source coordinate — `<srcModule>_<srcEntity>_<viaAttr>`
    /// uniquely names an FK because each attribute carries at most one
    /// outgoing reference in V1's metadata.
    let private referenceSsKey
        (sourceModuleName: string) (sourceEntityName: string) (viaAttrName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_REF" [ sourceModuleName; sourceEntityName; viaAttrName ]

    /// Index SsKey synthesis (session 22). Indexes identify by their
    /// V1 IndexName, scoped to the entity. V1's `IndexName` is unique
    /// per entity per V1's SQL extraction (`#AllIdx` keyed by
    /// `EntityId, IndexName`).
    let private indexSsKey
        (moduleName: string) (entityName: string) (indexName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_IDX" [ moduleName; entityName; indexName ]

    /// Trigger SsKey synthesis (chapter A.0' slice γ). Triggers
    /// identify by their V1 name scoped to the entity they fire on
    /// (a trigger is owned by exactly one table per SQL Server
    /// semantics).
    let private triggerSsKey
        (moduleName: string) (entityName: string) (triggerName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_TRG" [ moduleName; entityName; triggerName ]

    /// Sequence SsKey synthesis (chapter A.0' slice δ). Sequences
    /// identify by schema + name (a sequence is a top-level
    /// schema-scoped object per SQL Server semantics; SsKey carries
    /// the full coordinate to disambiguate across modules / schemas).
    let private sequenceSsKey
        (schemaName: string) (sequenceName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_SEQ" [ schemaName; sequenceName ]

    /// ColumnCheck SsKey synthesis (chapter A.0' slice ε). CHECK
    /// constraints identify by their declared name scoped to the
    /// entity. V1's `AttributeOnDiskCheckConstraint` carries a
    /// nullable name; when absent, the check is unnamed and the
    /// SsKey is derived from the entity + a synthetic discriminator
    /// at call time.
    let private columnCheckSsKey
        (moduleName: string) (entityName: string) (checkName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_CHK" [ moduleName; entityName; checkName ]

    // -----------------------------------------------------------------------
    // JSON helpers — light wrappers over System.Text.Json.JsonElement.
    // These are private; they exist to keep the translation code
    // readable, not as a general-purpose JSON utility surface.
    // -----------------------------------------------------------------------

    let private getProperty (element: JsonElement) (name: string) : Result<JsonElement> =
        match element.TryGetProperty(name) with
        | true, value -> Result.success value
        | _ ->
            Result.failureOf (
                adapterError
                    "missingProperty"
                    (sprintf "Required property '%s' not found." name))

    let private getString (element: JsonElement) (name: string) : Result<string> =
        match getProperty element name with
        | Error errors -> Error errors
        | Ok value ->
            if value.ValueKind = JsonValueKind.String then
                match value.GetString() with
                | null ->
                    Result.failureOf (
                        adapterError
                            "nullProperty"
                            (sprintf "Property '%s' is null; expected a string." name))
                | raw ->
                    Result.success raw
            else
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a string." name))

    /// Read an `isActive` flag with V1's default-true semantics. V1's
    /// SQL coerces missing/null `Is_Active` columns to active=true
    /// (per `outsystems_metadata_rowsets.sql:94, 116, 239`). V2's
    /// adapter mirrors that semantically: a missing `isActive`
    /// property is treated as active=true; explicit `false` is
    /// inactive; explicit `true` is active.
    ///
    /// Chapter A.0' slice β — the session-21 inactive-records
    /// filter retired. This helper now populates the V2 IR's
    /// `Module.IsActive` / `Kind.IsActive` / `Attribute.IsActive`
    /// carriage fields instead of gating record inclusion. Per the
    /// pillar-9 harvest-dichotomy classification: filtering on a
    /// lifecycle flag is `OperatorIntent` (a Selection-axis choice),
    /// mis-placed at the adapter boundary, which is restricted to
    /// `DataIntent` carriage. Downstream emitters decide whether to
    /// suppress inactive records; no Selection-axis pass ships with
    /// slice β (deferred-with-trigger; IR-grows-under-evidence).
    let private isActiveOrDefault (element: JsonElement) : bool =
        match element.TryGetProperty("isActive") with
        | true, value when value.ValueKind = JsonValueKind.False -> false
        | _ -> true

    let private getBool (element: JsonElement) (name: string) : Result<bool> =
        match getProperty element name with
        | Error errors -> Error errors
        | Ok value ->
            match value.ValueKind with
            | JsonValueKind.True  -> Result.success true
            | JsonValueKind.False -> Result.success false
            | _ ->
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a boolean." name))

    /// V1 carries some boolean-shaped flags as numbers (0/1) rather
    /// than JSON booleans — `isReference`, `reference_hasDbConstraint`,
    /// `physical_isPresentButInactive`. This helper accepts either
    /// shape; non-zero is true.
    let private getIntFlag (element: JsonElement) (name: string) : Result<bool> =
        match getProperty element name with
        | Error errors -> Error errors
        | Ok value ->
            match value.ValueKind with
            | JsonValueKind.Number ->
                match value.TryGetInt32() with
                | true, n -> Result.success (n <> 0)
                | _ ->
                    Result.failureOf (
                        adapterError
                            "typeMismatch"
                            (sprintf "Property '%s' is not an integer flag." name))
            | JsonValueKind.True  -> Result.success true
            | JsonValueKind.False -> Result.success false
            | _ ->
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a numeric flag or boolean." name))

    /// Optional V1 int-flag with a caller-supplied default. Mirrors
    /// V1's `ISNULL(<col>, <default>)` SQL idiom (used at
    /// `outsystems_model_export.sql:730` for `hasDbConstraint`, etc.).
    /// Convenience over `match getIntFlag … with Ok v -> v | Error _ ->
    /// default` — chapter 4.7 slice α consolidation. Returns the
    /// flag value when present + parseable; returns the default for
    /// absent / unparseable values (the failure case is itself the
    /// V1-COALESCE-equivalent silent default).
    let private getOptionalIntFlag
        (element: JsonElement)
        (name: string)
        (defaultValue: bool)
        : bool =
        match getIntFlag element name with
        | Ok v -> v
        | Error _ -> defaultValue

    /// Optional bool property with caller-supplied default. Sibling
    /// of `getOptionalIntFlag` for fields V1 projects as JSON
    /// booleans (e.g., index `isPlatformAuto`).
    let private getOptionalBool
        (element: JsonElement)
        (name: string)
        (defaultValue: bool)
        : bool =
        match getBool element name with
        | Ok v -> v
        | Error _ -> defaultValue

    /// Optional string property. Returns `None` for missing or
    /// JSON-null values, `Some s` for non-null strings, `Error`
    /// when the property exists but is not a string.
    let private getOptionalString (element: JsonElement) (name: string) : Result<string option> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success None
        | true, value ->
            match value.ValueKind with
            | JsonValueKind.Null      -> Result.success None
            | JsonValueKind.Undefined -> Result.success None
            | JsonValueKind.String ->
                match value.GetString() with
                | null    -> Result.success None
                | raw     -> Result.success (Some raw)
            | _ ->
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a string when present." name))

    // -----------------------------------------------------------------------
    // Translation — DataType string → V2 PrimitiveType.
    // The mapping rule is documented in session 18 commit 4 DECISIONS
    // entry. The fixture-driven implementation discipline applies:
    // only data types the differential tests exercise are mapped.
    // Subsequent fixtures extend the table.
    // -----------------------------------------------------------------------

    let private parsePrimitiveType (dataType: string) : Result<PrimitiveType> =
        // V1's `OutsystemsAttribute.DataType` carries the OutSystems
        // domain-type name (not the SQL type). Chapter 5.0 slice γ
        // extends the mapping to cover the data types V1's
        // `model.edge-case.seed.sql` fixture uses (Identifier / Text /
        // Boolean / DateTime / Integer / Decimal). The mapping is
        // V1-derived per the OSSYS adapter translation rule index
        // (`DECISIONS 2026-05-15 — OSSYS adapter translation rules`).
        match dataType with
        | "Identifier"        -> Result.success Integer
        | "Integer"           -> Result.success Integer
        | "LongInteger"       -> Result.success Integer
        | "Text"              -> Result.success Text
        | "Email"             -> Result.success Text
        | "PhoneNumber"       -> Result.success Text
        | "Boolean"           -> Result.success Boolean
        | "DateTime"          -> Result.success DateTime
        | "Date"              -> Result.success Date
        | "Time"              -> Result.success Time
        | "Decimal"           -> Result.success Decimal
        | "Currency"          -> Result.success Decimal
        | "BinaryData"        -> Result.success Binary
        | other ->
            Result.failureOf (
                adapterError
                    "unmappedDataType"
                    (sprintf "DataType '%s' has no V2 PrimitiveType mapping yet." other))

    /// V1 reference_deleteRuleCode → V2 ReferenceAction. Mirrors the
    /// V1 mapping in `Osm.Smo/SmoEntityEmitter.cs`:
    ///   "Delete"  → Cascade
    ///   "Protect" → NoAction
    ///   "Ignore"  → NoAction
    ///   null      → NoAction (V1's TreatMissingDeleteRuleAsIgnore default)
    /// Other / unmapped values fail with adapter.osm.unmappedDeleteRule.
    let private parseDeleteRule (code: string option) : Result<ReferenceAction> =
        match code with
        | None              -> Result.success NoAction
        | Some "Delete"     -> Result.success Cascade
        | Some "Protect"    -> Result.success NoAction
        | Some "Ignore"     -> Result.success NoAction
        | Some "SetNull"    -> Result.success SetNull
        | Some other ->
            Result.failureOf (
                adapterError
                    "unmappedDeleteRule"
                    (sprintf "reference_deleteRuleCode '%s' has no V2 ReferenceAction mapping yet." other))

    /// Slice A.4.7'-prelude.row17-18-rowset-roundtrip — parse V1's
    /// `#FkReality.update_referential_action_desc` / `delete_referential
    /// _action_desc` (SQL Server vocabulary, distinct from
    /// `parseDeleteRule`'s OutSystems vocabulary). SQL Server's
    /// `sys.foreign_keys` columns emit `NO_ACTION` / `CASCADE` /
    /// `SET_NULL` / `SET_DEFAULT` (underscored uppercase per
    /// `*_referential_action_desc`). Returns `None` for unrecognized
    /// vocabulary (defensive — V2 omits the ON UPDATE clause rather
    /// than emitting an unsupported variant). `SET_DEFAULT` degrades
    /// to `None` because V2's `ReferenceAction` DU doesn't model it
    /// yet (lift trigger: a real-world FK with ON UPDATE SET DEFAULT
    /// surfaces in fixture data).
    let private parseSqlForeignKeyAction (code: string option) : ReferenceAction option =
        match code with
        | None                -> None
        | Some "NO_ACTION"    -> Some NoAction
        | Some "CASCADE"      -> Some Cascade
        | Some "SET_NULL"     -> Some SetNull
        | Some "SET_DEFAULT"  -> None
        | Some _              -> None

    // -----------------------------------------------------------------------
    // Translation — V1 attribute → V2 Attribute.
    // -----------------------------------------------------------------------

    /// Optional non-negative-integer property reader. Returns
    /// `None` when the property is missing OR explicitly null;
    /// `Some n` when the property is present and parseable.
    /// Per session-32: V1's `osm_model.json` carries length /
    /// precision / scale as nullable JSON numbers (or omitted
    /// when not applicable to the type), so the adapter must
    /// gracefully absorb absence.
    let private getOptionalInt (element: JsonElement) (name: string) : int option =
        match element.TryGetProperty(name) with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt32() with
            | true, n -> Some n
            | _ -> None
        | _ -> None

    let private parseAttribute
        (moduleName: string) (entityName: string) (attrJson: JsonElement)
        : Result<Attribute> =
        let nameResult     = getString  attrJson "name"
        let physicalResult = getString  attrJson "physicalName"
        let dataTypeStr    = getString  attrJson "dataType"
        let isMandatory    = getBool    attrJson "isMandatory"
        let isIdentifier   = getBool    attrJson "isIdentifier"
        let isAutoNumber   = getBool    attrJson "isAutoNumber"
        // Chapter A.0' slice α — Description lift. Defensive read
        // via `getOptionalString` returns Ok None when the source
        // omits the field; the JSON path consumes V1's `description`
        // JSON property which `SnapshotJsonBuilder.cs` writes when
        // V1's `ossys_EntityAttr.Description` is non-null.
        let descriptionResult = getOptionalString attrJson "description"
        // Chapter 4.9 slice β — OriginalName + ExternalDatabaseType lift.
        // V1's JSON projects via `originalName` (NULL when no rename
        // history) and `external_dbType` (NULL for OS-native entities
        // and for external entities lacking an override). Both fields
        // are defensive optional reads; the adapter carries `None` when
        // the source omits or null-projects either.
        let originalNameResult       = getOptionalString attrJson "originalName"
        let externalDbTypeResult     = getOptionalString attrJson "external_dbType"
        match nameResult, physicalResult, dataTypeStr, isMandatory, isIdentifier with
        | Ok rawName, Ok physicalName, Ok rawDataType,
          Ok mandatory, Ok identifier ->
            let nameDU       = Name.create rawName
            let key          = attributeSsKey moduleName entityName rawName
            let primitive    = parsePrimitiveType rawDataType
            let description  =
                match descriptionResult with
                | Ok d -> d
                | Error _ -> None
            let originalName : string option =
                match originalNameResult with
                | Ok n -> n
                | Error _ -> None
            let externalDatabaseType : string option =
                match externalDbTypeResult with
                | Ok t -> t
                | Error _ -> None
            // Per session-32 — V1 surfaces length / precision /
            // scale on attribute records when applicable. The
            // adapter pulls them through to the V2 IR so the
            // canary's round-trip sees byte-faithful column
            // declarations (NVARCHAR(N) instead of NVARCHAR(MAX),
            // DECIMAL(P, S) instead of DECIMAL(18, 4) default).
            let lengthOpt    = getOptionalInt attrJson "length"
            let precisionOpt = getOptionalInt attrJson "precision"
            let scaleOpt     = getOptionalInt attrJson "scale"
            // Identity = isAutoNumber per V1 convention (only
            // primary-key columns marked isAutoNumber=true map to
            // SQL Server IDENTITY).
            let isIdentity =
                match isAutoNumber with
                | Ok true -> true
                | _ -> false
            // Chapter A.0' slice ε — DefaultValue lift. V1's JSON
            // `default` field is typically `null` in current
            // projections; when present, the adapter projects via
            // `SqlLiteral.ofRaw` against the typed `PrimitiveType`.
            // Falls back to `None` if the type-resolution failed
            // upstream (the parent record error path handles the
            // primitive's Error case).
            let defaultValue : SqlLiteral option =
                match primitive with
                | Error _ -> None
                | Ok p ->
                    match attrJson.TryGetProperty("default") with
                    | true, value when value.ValueKind <> JsonValueKind.Null ->
                        let rawOpt =
                            match value.ValueKind with
                            | JsonValueKind.String ->
                                match value.GetString() with
                                | null -> None
                                | s    -> Some s
                            | JsonValueKind.Number -> Some (value.GetRawText())
                            | JsonValueKind.True  -> Some "true"
                            | JsonValueKind.False -> Some "false"
                            | _ -> None
                        rawOpt |> Option.map (SqlLiteral.ofRaw p)
                    | _ -> None
            match nameDU, key, primitive with
            | Ok n, Ok k, Ok p ->
                Result.success
                    { SsKey        = k
                      Name         = n
                      Type         = p
                      Column       = { ColumnName = physicalName; IsNullable = not mandatory }
                      IsPrimaryKey = identifier
                      IsMandatory  = mandatory
                      Length       = lengthOpt
                      Precision    = precisionOpt
                      Scale        = scaleOpt
                      IsIdentity   = isIdentity
                      Description  = description
                      IsActive     = isActiveOrDefault attrJson
                      DefaultValue = defaultValue
                      DefaultName  = None
                      // Chapter A.0' slice ε — Computed lift; V1's
                      // JSON projection does not surface computed-
                      // column metadata. Positioned for future use.
                      Computed     = None
                      // Chapter A.0' slice ζ — ExtendedProperties
                      // attribute-level lift; V1's JSON projection
                      // does not surface attribute-level extended
                      // properties at the boundary today.
                      ExtendedProperties = []
                      OriginalName       = originalName
                      ExternalDatabaseType = externalDatabaseType }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                // Substantive causes (e.g., `adapter.osm.unmappedDataType`
                // from `parsePrimitiveType`) survive the attribute-level
                // wrap.
                propagateOrFallback
                    [ Result.errors nameDU
                      Result.errors key
                      Result.errors primitive ]
                    (fun () ->
                        adapterError
                            "attributeBuild"
                            (sprintf
                                "Failed to build attribute '%s' on '%s.%s'."
                                rawName moduleName entityName))
        | _ ->
            Result.failureOf (
                adapterError
                    "attributeFields"
                    (sprintf "Required attribute fields missing on an entity in module '%s'." moduleName))

    // -----------------------------------------------------------------------
    // Translation — V1 entity → V2 Kind.
    // -----------------------------------------------------------------------

    /// V1 isExternal → V2 Origin three-way collapse rule (session 20).
    ///
    /// Through the JSON-snapshot path V2 consumes today, V2 sees only
    /// the boolean `isExternal` flag at entity level. V1's
    /// IS-vs-Direct distinction is encoded in `EspaceKind` (string
    /// column at the espace/rowset level) which `SnapshotJsonBuilder`
    /// does NOT write to `osm_model.json`. The full distinction is
    /// **bound by the input path**:
    ///
    ///   - `isExternal: false` → OsNative (clear)
    ///   - `isExternal: true`  → ExternalViaIntegrationStudio (placeholder)
    ///
    /// The `ExternalViaIntegrationStudio` placeholder reflects that
    /// IS extensions are the standard V1 mechanism for external
    /// entities; most isExternal=true cases are IS-imported. Direct
    /// external entities (no IS step) exist but are rarer. The
    /// bound resolves when the `SnapshotRowsets` variant lands —
    /// rowsets carry `EspaceKind` natively, enabling the full
    /// three-way distinction. See `DECISIONS 2026-05-15 — OSSYS
    /// adapter translation rules`, session-20 amendment for the
    /// bounded-A1-equivalent disposition.
    ///
    /// The session-18 placeholder for this branch was
    /// `ExternalDirect`; that choice was made before the empirical
    /// pressure of an external-entity fixture. Session 20's fixture
    /// surfaced the question; the placeholder updates under the
    /// pressure. Documented in the session-20 DECISIONS amendment.
    let private parseOrigin (isExternal: bool) : Origin =
        if isExternal then ExternalViaIntegrationStudio else OsNative

    /// Three-way `Origin` translation for the rowset path (chapter
    /// 3.2 slice 3). Refines `parseOrigin`'s two-way placeholder
    /// using V1's `EspaceKind` string (rowset 1; previously dropped
    /// at the JSON projection layer). Empirical evidence:
    ///   - `"eSpace"` (normal module; seen in V1 test seed
    ///     `tests/Fixtures/sql/model.edge-case.seed.sql:97-99`)
    ///   - `"Extension"` (conventional IS-extension marker per
    ///     `DECISIONS 2026-05-19 — rule 17`; not yet observed in
    ///     the V1 test corpus but documented as the operative
    ///     marker until a production sample surfaces otherwise)
    ///
    /// The translation:
    ///   - `isExternal: false`                          → OsNative
    ///   - `isExternal: true` AND EspaceKind = "Extension" → ExternalViaIntegrationStudio
    ///   - `isExternal: true` otherwise                  → ExternalDirect
    ///
    /// Case-insensitive comparison on the marker (V1's column is
    /// nvarchar(50); historical samples have varied in capitalization
    /// across V1 versions). Null EspaceKind on an external entity
    /// resolves to ExternalDirect — the absence of an IS marker
    /// witnesses the absence of an IS step.
    ///
    /// Rule 17 now refines from the session-20 placeholder
    /// (collapsing to ExternalViaIntegrationStudio for all external
    /// entities) to the empirically-rooted three-way real. The JSON
    /// path's `parseOrigin` is the still-bounded sibling — same
    /// signature, same JSON-projection-lossiness disposition.
    let private parseOriginFromRowset
        (isExternal: bool) (espaceKind: string option) : Origin =
        if not isExternal then OsNative
        else
            let isIsExtension =
                match espaceKind with
                | Some kind ->
                    System.String.Equals(kind, "Extension", System.StringComparison.OrdinalIgnoreCase)
                | None -> false
            if isIsExtension then ExternalViaIntegrationStudio
            else ExternalDirect

    /// Extract a Reference from a V1 attribute that carries
    /// `isReference: 1` plus its `refEntity_*` and
    /// `reference_deleteRuleCode` fields. Returns `None` for
    /// non-reference attributes; `Some Reference` for FK-bearing
    /// ones; `Error` when the attribute claims isReference=1 but
    /// the required fields are missing or malformed.
    ///
    /// V1's relationships[] array carries the same information in an
    /// aggregated form (viaAttributeName + toEntity_name +
    /// hasDbConstraint). The V2 adapter walks attributes[] directly
    /// because the attribute carries every field the Reference shape
    /// needs; the relationships[] array becomes a cross-check rather
    /// than the primary source.
    ///
    /// Same-module assumption: TargetKind is synthesized as
    /// OS_KIND_<sourceModule>_<refEntity_name>. Cross-module FK
    /// references would surface a richer rule when a fixture forces
    /// the question; the minimal session-19 fixture is same-module.
    let private parseReference
        (sourceModuleName: string) (sourceEntityName: string) (attrJson: JsonElement)
        : Result<Reference option> =
        match getIntFlag attrJson "isReference" with
        | Error errors -> Error errors
        | Ok false  -> Result.success None
        | Ok true ->
            let attrNameResult     = getString          attrJson "name"
            let refEntityNameResult = getOptionalString attrJson "refEntity_name"
            let deleteRuleResult   = getOptionalString attrJson "reference_deleteRuleCode"
            match attrNameResult, refEntityNameResult, deleteRuleResult with
            | Ok attrName, Ok (Some refEntityName), Ok deleteRuleCode ->
                let refKey      = referenceSsKey sourceModuleName sourceEntityName attrName
                let refName     = Name.create attrName
                let srcAttrKey  = attributeSsKey sourceModuleName sourceEntityName attrName
                let tgtKindKey  = kindSsKey sourceModuleName refEntityName
                let onDelete    = parseDeleteRule deleteRuleCode
                match refKey, refName, srcAttrKey, tgtKindKey, onDelete with
                | Ok rKey, Ok rName, Ok srcKey, Ok tgtKey, Ok rule ->
                    // Chapter 4.6 slice α — capture V1's
                    // `reference_hasDbConstraint` int-flag
                    // (COALESCE'd from outsystems_model_export.sql:730
                    // HasFK column; V1's JSON projection renames to
                    // `reference_hasDbConstraint` per SnapshotJsonBuilder).
                    // Defaults to false when V1 source omits the field
                    // (mirrors V1's ISNULL coalesce semantics).
                    // Chapter 4.7 slice α: getOptionalIntFlag retires the
                    // local `match … | Ok v -> v | Error _ -> default`
                    // pattern.
                    let hasDbConstraint =
                        getOptionalIntFlag attrJson "reference_hasDbConstraint" false
                    // Slice 5.13.fk-features-emit — smart-constructor
                    // migration. `Reference.create` carries the
                    // minimum-evidence defaults (OnDelete = NoAction;
                    // IsUserFk = false; HasDbConstraint = false;
                    // OnUpdate = None; IsConstraintTrusted = true);
                    // the JSON path overrides what the source carries.
                    // User-FK detection (chapter 4.2 deferral) stays at
                    // default `false`.
                    Result.success (Some
                        { Reference.create rKey rName srcKey tgtKey with
                            OnDelete        = rule
                            HasDbConstraint = hasDbConstraint })
                | _ ->
                    // Propagate underlying errors via
                    // `propagateOrFallback` — uniform with the four
                    // build sites in parseKind / parseModule /
                    // parseAttribute / parseIndex. Retires the
                    // partial-decompose `Error es` branch (which
                    // caught only the onDelete-error shape and dropped
                    // the other four error sources on the floor).
                    propagateOrFallback
                        [ Result.errors refKey
                          Result.errors refName
                          Result.errors srcAttrKey
                          Result.errors tgtKindKey
                          Result.errors onDelete ]
                        (fun () ->
                            adapterError
                                "referenceBuild"
                                (sprintf
                                    "Failed to build reference for attribute '%s' on '%s.%s'."
                                    attrName sourceModuleName sourceEntityName))
            | Ok attrName, Ok None, _ ->
                Result.failureOf (
                    adapterError
                        "referenceFields"
                        (sprintf
                            "Attribute '%s' on '%s.%s' has isReference=1 but no refEntity_name."
                            attrName sourceModuleName sourceEntityName))
            | _ ->
                Result.failureOf (
                    adapterError
                        "referenceFields"
                        (sprintf
                            "Required reference fields missing on an attribute in '%s.%s'."
                            sourceModuleName sourceEntityName))

    /// Extract one V2 Index from a V1 index JSON entry.
    ///
    /// V1's index JSON shape (per `outsystems_metadata_rowsets.sql`,
    /// `#IdxJson` aggregation at line 864+) carries: `name`,
    /// `isPrimary`, `kind`, `isUnique`, `isPlatformAuto`, plus
    /// storage/perf attributes (`isDisabled` / `isPadded` / etc.),
    /// structural fields (`filterDefinition`, `dataSpace`,
    /// `partitionColumns`, `dataCompression`), and `columns` array.
    /// V2's `Index` shape consumes only `name`, `isPrimary`,
    /// `isUnique`, plus the key columns from `columns[]`.
    ///
    /// **Included-columns drop.** Per the OSSYS ADMIRE entry's
    /// "what V2 will explicitly NOT carry forward" section, V1
    /// `columns[]` entries with `isIncluded: true` are dropped at
    /// the boundary; V2's `Columns` carries only key columns.
    /// Documented divergence; not a bug.
    ///
    /// **Column ordering.** V1 carries `columns[].ordinal` to
    /// preserve key-column order. V2's adapter sorts by ordinal
    /// before flattening to `Columns: SsKey list`.
    let private parseIndex
        (moduleName: string) (entityName: string) (indexJson: JsonElement)
        : Result<Index> =
        let nameResult      = getString indexJson "name"
        let isPrimaryResult = getBool   indexJson "isPrimary"
        let isUniqueResult  = getBool   indexJson "isUnique"
        match nameResult, isPrimaryResult, isUniqueResult with
        | Ok indexName, Ok isPrimary, Ok isUnique ->
            let indexKey  = indexSsKey moduleName entityName indexName
            let indexNm   = Name.create indexName
            // Walk columns[]; partition into key columns + included columns
            // (chapter 4.5 slice β — V2 now carries both axes; pre-slice-β
            // the adapter dropped isIncluded=true entries). Sort each
            // partition by ordinal; resolve attribute name to V2 SsKey.
            let columnsList =
                match indexJson.TryGetProperty("columns") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray() |> Seq.toList
                | _ -> []
            let extractCol (col: JsonElement) =
                // Best-effort ordinal extraction; missing ordinal sorts
                // as 0 (preserves first-in-array for malformed input).
                let ordinal =
                    match col.TryGetProperty("ordinal") with
                    | true, o when o.ValueKind = JsonValueKind.Number ->
                        match o.TryGetInt32() with
                        | true, n -> n
                        | _       -> 0
                    | _ -> 0
                let attrNameResult = getString col "attribute"
                ordinal, col, attrNameResult
            let isIncluded (col: JsonElement) : bool =
                match col.TryGetProperty("isIncluded") with
                | true, v when v.ValueKind = JsonValueKind.True -> true
                | _ -> false
            // Chapter 4.9 slice γ — parse per-column direction. V1's
            // JSON property is `direction` ("ASC" / "DESC" /
            // case-insensitive; absent / null / unknown → Ascending).
            // V1's `IndexColumnDirection.Unspecified` collapses to
            // Ascending under SQL Server semantics (the keyword is
            // omitted in CREATE INDEX, matching ScriptDom's
            // `SortOrder.NotSpecified`).
            let parseDirection (col: JsonElement) : IndexColumnDirection =
                match col.TryGetProperty("direction") with
                | true, v when v.ValueKind = JsonValueKind.String ->
                    match Option.ofObj (v.GetString()) with
                    | Some raw when System.String.Equals(raw.Trim(), "DESC", System.StringComparison.OrdinalIgnoreCase) ->
                        Descending
                    | _ -> Ascending
                | _ -> Ascending
            let keyColResults =
                columnsList
                |> List.filter (fun c -> not (isIncluded c))
                |> List.map extractCol
                |> List.sortBy (fun (o, _, _) -> o)
                |> List.map (fun (_, col, attrNameRes) ->
                    match attrNameRes with
                    | Error es -> Error es
                    | Ok an ->
                        match attributeSsKey moduleName entityName an with
                        | Error es -> Error es
                        | Ok key -> Result.success { Attribute = key; Direction = parseDirection col })
            let includedColResults =
                columnsList
                |> List.filter isIncluded
                |> List.map extractCol
                |> List.sortBy (fun (o, _, _) -> o)
                |> List.map (fun (_, _, attrNameRes) ->
                    match attrNameRes with
                    | Error es -> Error es
                    | Ok an -> attributeSsKey moduleName entityName an)
            // Per `Result.aggregate` (chapter-3.1 close audit): the
            // canonical accumulator for `Result<'a> seq` collapses to
            // `Result<'a list>` with errors aggregated (not short-
            // circuited). Retires the O(N²) `xs @ [x]` fold pattern
            // per `DECISIONS 2026-05-09` Big-O discipline.
            let foldedKeyCols = Result.aggregate keyColResults
            let foldedIncludedCols = Result.aggregate includedColResults
            match indexKey, indexNm, foldedKeyCols, foldedIncludedCols with
            | Ok k, Ok n, Ok cols, Ok includedCols ->
                // Chapter 4.5 slice α — capture V1's
                // `filterDefinition` if present (per V1 JSON shape;
                // raw string preserved through to emit time, parsed
                // by TSql160Parser at ScriptDomBuild.buildCreateIndex).
                let filter =
                    match indexJson.TryGetProperty("filterDefinition") with
                    | true, v when v.ValueKind = JsonValueKind.String ->
                        match Option.ofObj (v.GetString()) with
                        | Some raw when not (System.String.IsNullOrWhiteSpace raw) ->
                            Some raw
                        | _ -> None
                    | _ -> None
                // Chapter 4.6 slice β — capture V1's `isPlatformAuto`
                // flag (JSON projection of IndexModel.IsPlatformAuto).
                // Defaults to false when V1 source omits the field.
                // Chapter 4.7 slice α: getOptionalBool retires the
                // local `match … | Ok v -> v | Error _ -> default`
                // pattern.
                let isPlatformAuto = getOptionalBool indexJson "isPlatformAuto" false
                // Slice 5.13.smart-constructor-lift migration —
                // `Index.create` carries minimum-evidence defaults;
                // the JSON path overrides what the source surfaces.
                // V1's JSON projection does not yet carry on-disk
                // metadata or the slice 5.13.index-features-emit
                // axes (IgnoreDuplicateKey / IsDisabled /
                // DataCompression); those stay at smart-constructor
                // defaults pending a rowset wiring slice.
                Result.success
                    { Index.create k n cols with
                        IsUnique       = isUnique
                        IsPrimaryKey   = isPrimary
                        Filter         = filter
                        IncludedColumns = includedCols
                        IsPlatformAuto = isPlatformAuto }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                propagateOrFallback
                    [ Result.errors indexKey
                      Result.errors indexNm
                      Result.errors foldedKeyCols
                      Result.errors foldedIncludedCols ]
                    (fun () ->
                        adapterError
                            "indexBuild"
                            (sprintf
                                "Failed to build index '%s' on '%s.%s'."
                                indexName moduleName entityName))
        | _ ->
            Result.failureOf (
                adapterError
                    "indexFields"
                    (sprintf
                        "Required index fields missing on an entity in '%s.%s'."
                        moduleName entityName))

    /// Parse one V1 entity-level `triggers[]` element into a V2
    /// `Trigger`. Chapter A.0' slice γ — IR fidelity lift (L3-S4).
    /// V1 JSON shape: `{ name, isDisabled, definition }`. V1's
    /// `TriggerModel.Create` requires non-null definition; V2's
    /// `Trigger.create` mirrors that invariant via the smart
    /// constructor.
    let private parseTrigger
        (moduleName: string) (entityName: string) (triggerJson: JsonElement)
        : Result<Trigger> =
        let nameResult       = getString triggerJson "name"
        let definitionResult = getString triggerJson "definition"
        let isDisabled =
            match triggerJson.TryGetProperty("isDisabled") with
            | true, value when value.ValueKind = JsonValueKind.True -> true
            | _ -> false
        match nameResult, definitionResult with
        | Ok triggerName, Ok definition ->
            let key  = triggerSsKey moduleName entityName triggerName
            let name = Name.create triggerName
            match key, name with
            | Ok k, Ok n -> Trigger.create k n isDisabled definition
            | _ ->
                propagateOrFallback
                    [ Result.errors key
                      Result.errors name ]
                    (fun () ->
                        adapterError
                            "triggerBuild"
                            (sprintf
                                "Failed to build trigger '%s' on '%s.%s'."
                                triggerName moduleName entityName))
        | _ ->
            propagateOrFallback
                [ Result.errors nameResult
                  Result.errors definitionResult ]
                (fun () ->
                    adapterError
                        "triggerFields"
                        (sprintf
                            "Required trigger fields missing on '%s.%s'."
                            moduleName entityName))

    /// Parse one V1 entity-level `extendedProperties[]` element into a
    /// V2 `ExtendedProperty`. Chapter A.0' slice ζ — IR fidelity lift
    /// (L3-S9 extended-properties sub-axiom). V1 JSON shape:
    /// `{ name, value }` (value may be null).
    let private parseExtendedProperty
        (moduleName: string) (entityName: string) (epJson: JsonElement)
        : Result<ExtendedProperty> =
        let nameResult = getString epJson "name"
        let value =
            match epJson.TryGetProperty("value") with
            | true, v when v.ValueKind = JsonValueKind.String ->
                match v.GetString() with
                | null -> None
                | s    -> Some s
            | _ -> None
        match nameResult with
        | Ok epName -> ExtendedProperty.create epName value
        | _ ->
            propagateOrFallback
                [ Result.errors nameResult ]
                (fun () ->
                    adapterError
                        "extendedPropertyFields"
                        (sprintf
                            "Required extended-property fields missing on '%s.%s'."
                            moduleName entityName))

    let private parseKind
        (moduleName: string) (entityJson: JsonElement)
        : Result<Kind> =
        let nameResult       = getString entityJson "name"
        let physicalResult   = getString entityJson "physicalName"
        let schemaResult     = getString entityJson "db_schema"
        // Chapter A.0' slice θ — Catalog (database) coordinate lift
        // (L3-S10 / L3-I10). V1's JSON projects `db_catalog` (typically
        // `null`; explicit cross-database references land as a
        // non-blank string). Defensive read via `getOptionalString`.
        let catalogResult    = getOptionalString entityJson "db_catalog"
        let isStaticResult   = getBool   entityJson "isStatic"
        let isExternalResult = getBool   entityJson "isExternal"
        // Chapter A.0' slice α — Description lift. Same defensive
        // read shape as parseAttribute.
        let descriptionResult = getOptionalString entityJson "description"
        match nameResult, physicalResult, schemaResult, isStaticResult, isExternalResult with
        | Ok entityName, Ok physicalName, Ok schema,
          Ok isStatic, Ok isExternal ->
            let description =
                match descriptionResult with
                | Ok d -> d
                | Error _ -> None
            let kindKey   = kindSsKey moduleName entityName
            let kindName  = Name.create entityName
            // Chapter A.0' slice β — the session-21 inactive-records
            // filter retires. Inactive attributes are carried into
            // `Kind.Attributes` with `Attribute.IsActive=false`; the
            // adapter no longer drops them. Pillar-9 harvest analysis:
            // a Selection-axis filter is `OperatorIntent`, not
            // `DataIntent`; the adapter carries only `DataIntent`.
            let attrJsonList =
                match entityJson.TryGetProperty("attributes") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                | _ -> []
            let attrsResults =
                attrJsonList
                |> Bench.iterMap "adapter.osm.parse.attribute" (parseAttribute moduleName entityName)
            let refResults =
                attrJsonList
                |> Bench.iterMap "adapter.osm.parse.reference" (parseReference moduleName entityName)
            // Collect attribute results — `Result.aggregate` collapses
            // `Result<'a> seq` to `Result<'a list>` with errors
            // aggregated. Retires the O(N²) `xs @ [x]` fold pattern.
            let foldedAttrs = Result.aggregate attrsResults
            // Collect reference results — `Result.aggregate` then drop
            // `None` entries via `List.choose id`. Same Big-O profile
            // as the legacy fold (O(N) overall) without the per-step
            // append.
            let foldedRefs =
                refResults
                |> Result.aggregate
                |> Result.map (List.choose id)
            // Collect index results — session 22; iterate the
            // entity's `indexes[]` array. The inactive-records
            // filter (session 21) does NOT extend to indexes today;
            // V1 index records have no `isActive` field on the
            // index itself (storage-level objects in V1's metadata).
            let indexResults =
                match entityJson.TryGetProperty("indexes") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.index" (parseIndex moduleName entityName)
                | _ -> []
            let foldedIdx = Result.aggregate indexResults
            // Chapter A.0' slice γ — Triggers lift. V1's JSON projects
            // entity-level `triggers[]` (carrying name + isDisabled +
            // definition). Empty array when none.
            let triggerResults =
                match entityJson.TryGetProperty("triggers") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.trigger" (parseTrigger moduleName entityName)
                | _ -> []
            let foldedTriggers = Result.aggregate triggerResults
            // Chapter A.0' slice ζ — ExtendedProperties lift (kind
            // level). V1's JSON projects entity-level
            // `extendedProperties[]`.
            let epResults =
                match entityJson.TryGetProperty("extendedProperties") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.extendedProperty" (parseExtendedProperty moduleName entityName)
                | _ -> []
            let foldedEps = Result.aggregate epResults
            match kindKey, kindName, foldedAttrs, foldedRefs, foldedIdx, foldedTriggers, foldedEps with
            | Ok k, Ok n, Ok attrs, Ok refs, Ok idxs, Ok triggers, Ok eps ->
                let modality =
                    if isStatic then [ Static [] ] else []
                Result.success
                    { SsKey       = k
                      Name        = n
                      Origin      = parseOrigin isExternal
                      Modality    = modality
                      Physical    =
                        { Schema = schema
                          Table = physicalName
                          // Chapter A.0' slice θ — `db_catalog` carried
                          // through; `None` when V1 projects `null`
                          // (implicit-current-database scope).
                          Catalog =
                            match catalogResult with
                            | Ok value -> value
                            | Error _  -> None }
                      Attributes  = attrs
                      References  = refs
                      Indexes     = idxs
                      Description = description
                      IsActive    = isActiveOrDefault entityJson
                      Triggers    = triggers
                      // Chapter A.0' slice ε — ColumnChecks lift; V1's
                      // JSON projection does not surface table-level
                      // CHECK constraints today (V1 carries them on
                      // the on-disk metadata channel, not the JSON
                      // boundary). Empty default.
                      ColumnChecks = []
                      ExtendedProperties = eps }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`
                // (codified at two-consumer threshold; same surface as
                // parseKindRow on the rowset path). Substantive causes
                // — e.g., `adapter.osm.unmappedDeleteRule` from
                // `parseDeleteRule`, `adapter.osm.unmappedDataType`
                // from `parsePrimitiveType` — survive the kind-level
                // wrap. Prior shape swallowed them under a generic
                // `kindBuild` umbrella.
                propagateOrFallback
                    [ Result.errors kindKey
                      Result.errors kindName
                      Result.errors foldedAttrs
                      Result.errors foldedRefs
                      Result.errors foldedIdx
                      Result.errors foldedTriggers
                      Result.errors foldedEps ]
                    (fun () ->
                        adapterError
                            "kindBuild"
                            (sprintf
                                "Failed to build kind '%s' in module '%s'."
                                entityName moduleName))
        | _ ->
            Result.failureOf (
                adapterError
                    "kindFields"
                    (sprintf "Required entity fields missing in module '%s'." moduleName))

    // -----------------------------------------------------------------------
    // Translation — V1 module → V2 Module.
    // -----------------------------------------------------------------------

    let private parseModule (moduleJson: JsonElement) : Result<Module> =
        let nameResult = getString moduleJson "name"
        match nameResult with
        | Ok rawName ->
            let modKey  = moduleSsKey rawName
            let modName = Name.create rawName
            // Chapter A.0' slice β — the session-21 entity-level
            // filter retires. Inactive entities carry into
            // `Module.Kinds` with `Kind.IsActive=false`; downstream
            // emitters decide. Per Subagent #3's O2 finding the JSON
            // path's `parseModule` did not previously filter on
            // `module.isActive` (the filter only operated at entity
            // and attribute levels); slice β adds module-level
            // carriage via `isActiveOrDefault` so the IR's
            // `Module.IsActive` field has authoritative provenance
            // from both paths.
            let entitiesArr =
                match moduleJson.TryGetProperty("entities") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.kind" (parseKind rawName)
                | _ ->
                    []
            let foldedKinds = Result.aggregate entitiesArr
            match modKey, modName, foldedKinds with
            | Ok k, Ok n, Ok kinds ->
                // Per DECISIONS pillar 6 (chapter-3.6 sidebar):
                // boundary adapters flow through the aggregate-root
                // smart constructor, not record-literal — invariants
                // (kind-SsKey-disjoint within module) are checked
                // structurally at the boundary, not deferred.
                //
                // Chapter A.0' slice ζ — Module.ExtendedProperties
                // populated empty; V1's JSON projection does not
                // surface module-level extended properties.
                Module.create k n kinds (isActiveOrDefault moduleJson) []
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                // Substantive causes survive the module-level wrap.
                propagateOrFallback
                    [ Result.errors modKey
                      Result.errors modName
                      Result.errors foldedKinds ]
                    (fun () ->
                        adapterError
                            "moduleBuild"
                            (sprintf "Failed to build module '%s'." rawName))
        | _ ->
            Result.failureOf (
                adapterError
                    "moduleFields"
                    "Required module fields missing.")

    // -----------------------------------------------------------------------
    // Translation — V1 osm_model.json document → V2 Catalog.
    // -----------------------------------------------------------------------

    let private parseDocument (root: JsonElement) : Result<Catalog> =
        match root.TryGetProperty("modules") with
        | true, arr when arr.ValueKind = JsonValueKind.Array ->
            let modulesList =
                arr.EnumerateArray()
                |> Seq.toList
                |> Bench.iterMap "adapter.osm.parse.module" parseModule
            let folded = Result.aggregate modulesList
            match folded with
            | Ok modules ->
                // Per DECISIONS pillar 6: boundary adapter flows
                // through `Catalog.create` (aggregate-root invariant
                // check) rather than record-literal construction.
                //
                // Chapter A.0' slice δ — Catalog.Sequences populated
                // empty; V1's `osm_model.json` projection does not
                // surface sequences at the catalog boundary today.
                Catalog.create modules []
            | Error errors  -> Error errors
        | _ ->
            Result.failureOf (
                adapterError
                    "missingModules"
                    "Document is missing the 'modules' array.")

    let private parseJsonString (json: string) : Result<Catalog> =
        try
            use document = JsonDocument.Parse(json)
            parseDocument document.RootElement
        with
        | :? JsonException as ex ->
            Result.failureOf (
                adapterError
                    "jsonInvalid"
                    (sprintf "Failed to parse JSON: %s" ex.Message))

    // -----------------------------------------------------------------------
    // Translation — V1 RowsetBundle → V2 Catalog.
    //
    // Chapter 3.2 slice 1. Sibling translation path to parseDocument.
    // SsKey carriage flips from `Synthesized ("OS_KIND", [...])` (JSON
    // path) to `OssysOriginal guid` (rowset path) when the rowset DTO
    // carries the Guid; falls back to synthesized form when absent
    // (test convenience for partial fixtures). Per A1's chapter-3.2
    // bound resolution: this is the path where SsKey is no longer
    // JSON-projection-bounded.
    //
    // Per `DECISIONS 2026-05-22 — Stage 0 foundation phase` aggregate-
    // root smart constructor commitment + chapter-3.6 pillar 6
    // amendment: boundary translation flows through
    // `Catalog.create` / `Module.create` (referential-integrity
    // invariants) rather than record-literal construction.
    // -----------------------------------------------------------------------

    let private moduleSsKeyFromRow (row: ModuleRow) : Result<SsKey> =
        match row.EspaceSsKey with
        | Some g -> Result.success (SsKey.ossysOriginal g)
        | None   -> moduleSsKey row.EspaceName

    let private kindSsKeyFromRow
        (moduleName: string)
        (row: KindRow)
        : Result<SsKey> =
        match row.EntitySsKey with
        | Some g -> Result.success (SsKey.ossysOriginal g)
        | None   -> kindSsKey moduleName row.EntityName

    let private attributeSsKeyFromRow
        (moduleName: string)
        (entityName: string)
        (row: AttributeRow)
        : Result<SsKey> =
        match row.AttrSsKey with
        | Some g -> Result.success (SsKey.ossysOriginal g)
        | None   -> attributeSsKey moduleName entityName row.AttrName

    let private parseAttributeRow
        (moduleName: string)
        (entityName: string)
        (row: AttributeRow)
        : Result<Attribute> =
        let nameDU    = Name.create row.AttrName
        let key       = attributeSsKeyFromRow moduleName entityName row
        let primitive = parsePrimitiveType row.DataType
        match nameDU, key, primitive with
        | Ok n, Ok k, Ok p ->
            Result.success
                { SsKey        = k
                  Name         = n
                  Type         = p
                  Column       = { ColumnName = row.PhysicalCol
                                   IsNullable = not row.IsMandatory }
                  IsPrimaryKey = row.IsIdentifier
                  IsMandatory  = row.IsMandatory
                  Length       = row.Length
                  Precision    = row.Precision
                  Scale        = row.Scale
                  IsIdentity   = row.IsAutoNumber
                  Description  = row.Description
                  IsActive     = row.IsActive
                  // Slice A.4.7'-prelude.row53-source-side: V1's
                  // `#ColumnReality.DefaultDefinition` carries the
                  // expression text (e.g., `((0))`, `(getdate())`).
                  // Parens-stripping + literal-vs-expression
                  // disambiguation deferred per matrix row 53's named
                  // trigger ("expression-shaped defaults flow via
                  // raw-string pass-through at the realization
                  // boundary"). For now: rowset path leaves
                  // DefaultValue = None; the JSON path's
                  // `parseAttribute` populates from V1's `default`
                  // field which V1 emits as the literal value.
                  DefaultValue = None
                  // Slice A.4.7'-prelude.row53-source-side: V1
                  // `#ColumnReality.DefaultConstraintName` (sys
                  // .default_constraints.name) → V2 DefaultName for
                  // round-trip parity with V1's `DF_<table>_<column>`
                  // constraint identifier. `None` when no named
                  // DEFAULT constraint exists at the deployed target.
                  DefaultName  =
                      row.DefaultConstraintName
                      |> Option.bind (fun raw ->
                          Name.create raw |> Result.toOption)
                  // Slice A.4.7'-prelude.row53-source-side (LR4 cash-
                  // out completion): V1 `#ColumnReality.IsComputed` +
                  // `ComputedDefinition` (sys.computed_columns
                  // .definition) → V2 ComputedColumnConfig. The
                  // `IsPersisted` axis defaults to false because V1's
                  // SQL doesn't surface `sys.computed_columns
                  // .is_persisted`; persisted-detection is a follow-up
                  // rowset extension when V2 emission demands the
                  // PERSISTED keyword for round-trip.
                  Computed     =
                      if row.IsComputed then
                          row.ComputedDefinition
                          |> Option.bind (fun expr ->
                              ComputedColumnConfig.create expr false
                              |> Result.toOption)
                      else
                          None
                  ExtendedProperties = []
                  OriginalName = row.OriginalName
                  ExternalDatabaseType = row.ExternalDatabaseType }
        | _ ->
            // Propagate underlying errors via `propagateOrFallback`.
            propagateOrFallback
                [ Result.errors nameDU
                  Result.errors key
                  Result.errors primitive ]
                (fun () ->
                    adapterError
                        "attributeRowBuild"
                        (sprintf
                            "Failed to build attribute '%s' on '%s.%s' from rowset bundle."
                            row.AttrName moduleName entityName))

    /// Build one V2 `Reference` from a paired `(AttributeRow, ReferenceRow)`.
    /// Same structural shape as `parseReference` (JSON path,
    /// CatalogReader.fs:496) — both delegate to the shared
    /// `referenceSsKey` / `attributeSsKey` / `kindSsKey` synthesis
    /// helpers; both apply rule 16's same-module assumption (target
    /// kind name resolves within the source attribute's module).
    /// Cross-module FK lifts the same deferral.
    let private parseReferenceRowFor
        (kindKeysByEntityId: Map<int, SsKey>)
        (moduleName: string)
        (entityName: string)
        (attrRow: AttributeRow)
        (refRow: ReferenceRow)
        : Result<Reference> =
        let refKey     = referenceSsKey moduleName entityName attrRow.AttrName
        let refName    = Name.create attrRow.AttrName
        let srcAttrKey = attributeSsKeyFromRow moduleName entityName attrRow
        // Chapter 5.0 slice γ — when the bundle carries the target's
        // RefEntityId AND the target entity row has a resolved kind key
        // in the global lookup, use it directly. This handles GUID-based
        // EntitySsKey targets correctly (the fallback synthesized key
        // produces a different SsKey shape that breaks the
        // danglingTarget invariant). Falls back to the prior synthesized
        // shape when the target ID is absent or unresolvable.
        let tgtKindKey =
            match refRow.RefEntityId with
            | Some id ->
                match Map.tryFind id kindKeysByEntityId with
                | Some key -> Result.success key
                | None -> kindSsKey moduleName refRow.RefEntityName
            | None -> kindSsKey moduleName refRow.RefEntityName
        let onDelete   = parseDeleteRule refRow.DeleteRuleCode
        // Slice A.4.7'-prelude.row17-18-rowset-roundtrip — `OnUpdate`
        // carries SQL Server's `sys.foreign_keys.update_referential_action
        // _desc` vocabulary (NO_ACTION / CASCADE / SET_NULL / SET_DEFAULT),
        // not OutSystems' DeleteRuleCode vocabulary
        // (Delete / Protect / Ignore / SetNull). The prior
        // 5.13.fk-reality-join slice routed through `parseDeleteRule`
        // which silently dropped every valid SQL Server value into the
        // error branch (bug found 2026-05-19 via FkRealityRowsetRoundTripTests).
        // `parseSqlForeignKeyAction` is the SQL-Server-vocabulary parser;
        // unfamiliar values degrade to None per the rowset adapter's
        // defensive-parsing posture.
        let onUpdateRule = parseSqlForeignKeyAction refRow.OnUpdate
        match refKey, refName, srcAttrKey, tgtKindKey, onDelete with
        | Ok rKey, Ok rName, Ok srcKey, Ok tgtKey, Ok rule ->
            // Slice 5.13.fk-features-emit — smart-constructor migration.
            // Slice 5.13.fk-reality-join (2026-05-18) — `OnUpdate` +
            // `IsConstraintTrusted` thread through from the rowset
            // path's `#FkReality` JOIN at `toBundle`. Cross-catalog +
            // JSON-path references default to `(None, true)` per the
            // smart-constructor defaults.
            Result.success
                { Reference.create rKey rName srcKey tgtKey with
                    OnDelete            = rule
                    HasDbConstraint     = refRow.HasDbConstraint
                    OnUpdate            = onUpdateRule
                    IsConstraintTrusted = refRow.IsConstraintTrusted }
        | _ ->
            // Propagate underlying errors via `propagateOrFallback` —
            // uniform with parseReference on the JSON path.
            propagateOrFallback
                [ Result.errors refKey
                  Result.errors refName
                  Result.errors srcAttrKey
                  Result.errors tgtKindKey
                  Result.errors onDelete ]
                (fun () ->
                    adapterError
                        "referenceRowBuild"
                        (sprintf
                            "Failed to build reference for attribute '%s' on '%s.%s' from rowset bundle."
                            attrRow.AttrName moduleName entityName))

    /// Per-id-keyed groupings the rowset-bundle parser threads through
    /// `parseModuleRow` → `parseKindRow`. Slice 5.13.ossys-rowsets-cluster
    /// consolidates four existing Maps + four new index/trigger/check
    /// Maps into one record so future rowset lifts (matrix rows 58 +
    /// 59 cash-out, etc.) extend the context shape rather than the
    /// function signature.
    ///
    /// Sibling-wrapper-discipline-friendly: extending the record (an
    /// IR-grows-under-evidence move) is structurally cheap; expanding
    /// the parseKindRow signature with N more Maps is the anti-pattern
    /// the discipline names.
    type private RowsetParseContext =
        {
            /// EntityId → kind's resolved V2 SsKey (composite of GUID or
            /// synthesized identity per `kindSsKeyFromRow`). Used by
            /// `parseReferenceRowFor` for cross-module FK resolution.
            KindKeysByEntityId : Map<int, SsKey>
            /// EspaceId → kinds belonging to that module. Owned by
            /// `parseModuleRow`'s walk.
            KindsByEspace : Map<int, KindRow list>
            /// EntityId → attributes belonging to that kind. Used by
            /// `parseKindRow` for attribute construction.
            AttributesByEntity : Map<int, AttributeRow list>
            /// AttrId → references on that attribute. Used by
            /// `parseKindRow` for reference assembly.
            ReferencesByAttr : Map<int, ReferenceRow list>
            /// EntityId → indexes belonging to that kind. Slice
            /// 5.13.ossys-rowsets-cluster; matrix row 15.
            IndexesByEntity : Map<int, IndexRow list>
            /// (EntityId, IndexName) → index columns belonging to that
            /// index. Slice 5.13.ossys-rowsets-cluster; matrix row 16.
            IndexColumnsByIndex : Map<int * string, IndexColumnRow list>
            /// EntityId → triggers belonging to that kind. Slice
            /// 5.13.ossys-rowsets-cluster; matrix row 23.
            TriggersByEntity : Map<int, TriggerRow list>
            /// EntityId → CHECK constraints rolling up from this
            /// kind's attributes. Pre-grouped from `ColumnCheckRow`
            /// (per-AttrId in V1) into per-Kind list via AttrId→EntityId
            /// resolution at context construction. Slice
            /// 5.13.ossys-rowsets-cluster; matrix row 12.
            ColumnChecksByEntity : Map<int, ColumnCheckRow list>
        }

    /// Slice 5.13.ossys-rowsets-cluster — IndexColumn direction parser.
    /// V1's `#IdxColsMapped.Direction` carries `"ASC"` / `"DESC"`
    /// (case-insensitive); absent / null collapses to Ascending under
    /// SQL Server semantics (the keyword is omitted in CREATE INDEX,
    /// matching ScriptDom's `SortOrder.NotSpecified`). Sibling to the
    /// JSON-path's inline `parseDirection`.
    let private parseRowsetIndexDirection (raw: string option) : IndexColumnDirection =
        match raw with
        | Some d when
            System.String.Equals(d.Trim(), "DESC", System.StringComparison.OrdinalIgnoreCase) ->
            Descending
        | _ -> Ascending

    /// Slice 5.13.ossys-rowsets-cluster — IndexColumn attribute SsKey
    /// resolution. V1's `#IdxColsMapped.HumanAttr` is the COALESCE of
    /// `(PhysicalColumnName, DatabaseColumnName, AttrName)` — it
    /// carries the PHYSICAL name first when present (the typical
    /// case for OS-managed attributes), falling back to the logical
    /// name only when the physical name is empty. To bridge to V2's
    /// `attributeSsKey` (which keys on the **logical** AttrName), the
    /// resolver looks up the candidate string in the kind's
    /// `AttributeRow` list and uses the resolved `AttrName`.
    ///
    /// **Resolution order** (case-insensitive against the kind's
    /// attribute set):
    ///   1. `HumanAttr` matches an attribute's `AttrName` → use that
    ///      attribute's `AttrName`. Covers V1's COALESCE fallback to
    ///      `AttrName` when both physical-name columns are NULL.
    ///   2. `HumanAttr` matches an attribute's `PhysicalCol` → use
    ///      that attribute's `AttrName`. Covers V1's COALESCE
    ///      primary case where the PhysicalColumnName populates
    ///      HumanAttr.
    ///   3. `PhysicalColumn` matches an attribute's `PhysicalCol` →
    ///      use that attribute's `AttrName`. Fallback for rows where
    ///      HumanAttr is NULL.
    ///   4. None of the above → fail with `indexColumnUnresolved`.
    ///      The index references a column V2's attribute set doesn't
    ///      model — typically a system column (`OSPK`); the
    ///      diagnostic surfaces it so the operator can choose to
    ///      drop the index or extend V2's attribute model.
    let private resolveIndexColumnAttribute
        (moduleName: string)
        (entityName: string)
        (entityAttrs: AttributeRow list)
        (row: IndexColumnRow)
        : Result<SsKey> =
        let trimNonEmpty (s: string option) =
            s |> Option.map (fun v -> v.Trim())
              |> Option.filter (fun v -> not (System.String.IsNullOrEmpty v))
        let humanAttr   = trimNonEmpty row.HumanAttr
        let physColumn  = trimNonEmpty row.PhysicalColumn
        let findByAttrName (target: string) =
            entityAttrs
            |> List.tryFind (fun a ->
                System.String.Equals(a.AttrName, target, System.StringComparison.OrdinalIgnoreCase))
        let findByPhysicalCol (target: string) =
            entityAttrs
            |> List.tryFind (fun a ->
                System.String.Equals(a.PhysicalCol, target, System.StringComparison.OrdinalIgnoreCase))
        let firstHit (candidates: (unit -> AttributeRow option) list) : AttributeRow option =
            candidates
            |> List.tryPick (fun thunk -> thunk ())
        let resolved =
            firstHit
                [ (fun () -> humanAttr  |> Option.bind findByAttrName)
                  (fun () -> humanAttr  |> Option.bind findByPhysicalCol)
                  (fun () -> physColumn |> Option.bind findByPhysicalCol) ]
        match resolved with
        | Some attr ->
            // Per parseAttributeRow's shape: attribute SsKey is
            // `OssysOriginal GUID` when AttrSsKey is populated, else
            // synthesized from AttrName. Use the same helper to
            // produce a key that matches the attribute's actual SsKey.
            attributeSsKeyFromRow moduleName entityName attr
        | None ->
            Result.failureOf (
                adapterError
                    "indexColumnUnresolved"
                    (sprintf
                        "Index '%s' on kind '%s' references column '%s' (humanAttr='%s'); no matching attribute in V2's IR for this kind."
                        row.IndexName
                        entityName
                        (defaultArg physColumn "")
                        (defaultArg humanAttr "")))

    /// Slice 5.13.ossys-rowsets-cluster — per-Index assembly. Joins
    /// `IndexRow` with its `IndexColumnRow` list (lookup by EntityId
    /// + IndexName via `ctx.IndexColumnsByIndex`), partitions into
    /// key columns + included columns, sorts each partition by
    /// `Ordinal` for T1 byte-determinism, resolves attribute SsKeys
    /// per column, and lifts to V2's `Index` IR.
    let private parseIndexRowFor
        (ctx: RowsetParseContext)
        (moduleName: string)
        (entityName: string)
        (entityAttrs: AttributeRow list)
        (row: IndexRow)
        : Result<Index> =
        let indexKey  = indexSsKey moduleName entityName row.IndexName
        let indexName = Name.create row.IndexName
        let cols =
            Map.tryFind (row.EntityId, row.IndexName) ctx.IndexColumnsByIndex
            |> Option.defaultValue []
        let keyCols =
            cols
            |> List.filter (fun c -> not c.IsIncluded)
            |> List.sortBy (fun c -> c.Ordinal)
        let includedCols =
            cols
            |> List.filter (fun c -> c.IsIncluded)
            |> List.sortBy (fun c -> c.Ordinal)
        let keyColResults =
            keyCols
            |> List.map (fun c ->
                resolveIndexColumnAttribute moduleName entityName entityAttrs c
                |> Result.map (fun attrKey ->
                    { Attribute = attrKey
                      Direction = parseRowsetIndexDirection c.Direction } : IndexColumn))
        let includedColResults =
            includedCols
            |> List.map (resolveIndexColumnAttribute moduleName entityName entityAttrs)
        let foldedKeyCols      = Result.aggregate keyColResults
        let foldedIncludedCols = Result.aggregate includedColResults
        // FillFactor: SQL Server stores 0 as "server default" (unset);
        // V2 represents the default as None. Non-zero values pass
        // through; clamping to [1, 100] is V1's responsibility.
        let fillFactor =
            if row.FillFactor = 0 then None else Some row.FillFactor
        let filter =
            match row.FilterDefinition with
            | Some s when not (System.String.IsNullOrWhiteSpace s) -> Some s
            | _ -> None
        match indexKey, indexName, foldedKeyCols, foldedIncludedCols with
        | Ok k, Ok n, Ok keys, Ok included ->
            // Slice 5.13.smart-constructor-lift migration + slice
            // 5.13.fk-reality-join (2026-05-18) — rowset path
            // surfaces every #AllIdx axis V1 reflects: IsUnique /
            // IsPrimary, on-disk metadata, filter, included columns,
            // plus the slice-5.13.index-features-emit triple
            // (IsDisabled / IgnoreDuplicateKey / DataCompression).
            // IsPlatformAuto stays at default (rowset path doesn't
            // surface it; it lives on V1's logical IndexModel
            // projection, not on sys.indexes reality).
            let dataCompressionLevel =
                row.DataCompression
                |> Option.bind (fun s ->
                    match s.ToUpperInvariant() with
                    | "NONE" -> Some DataCompressionLevel.None
                    | "ROW"  -> Some DataCompressionLevel.Row
                    | "PAGE" -> Some DataCompressionLevel.Page
                    | _      -> None)
            Result.success
                { Index.create k n keys with
                    IsUnique              = row.IsUnique
                    IsPrimaryKey          = row.IsPrimary
                    Filter                = filter
                    IncludedColumns       = included
                    FillFactor            = fillFactor
                    IsPadded              = row.IsPadded
                    AllowRowLocks         = row.AllowRowLocks
                    AllowPageLocks        = row.AllowPageLocks
                    NoRecomputeStatistics = row.NoRecompute
                    IsDisabled            = row.IsDisabled
                    IgnoreDuplicateKey    = row.IgnoreDupKey
                    DataCompression       = dataCompressionLevel
                    // Slice A.4.7'-prelude.row56-dataspace (LR7
                    // closure): V1 #AllIdx.DataSpaceName/Type/
                    // PartitionColumnsJson → V2 Index.DataSpace.
                    // Carriage is direct; MetadataSnapshotRunner
                    // .toBundle does the JSON parse + DU shaping
                    // (the Adapter.Osm boundary trusts the typed
                    // DataSpace coming from the OssysSql adapter).
                    DataSpace             = row.DataSpace }
        | _ ->
            propagateOrFallback
                [ Result.errors indexKey
                  Result.errors indexName
                  Result.errors foldedKeyCols
                  Result.errors foldedIncludedCols ]
                (fun () ->
                    adapterError
                        "indexRowBuild"
                        (sprintf
                            "Failed to build index '%s' on kind '%s' from rowset bundle."
                            row.IndexName
                            entityName))

    /// Slice 5.13.ossys-rowsets-cluster — per-Trigger lift. Sibling
    /// to the JSON-path's `parseTrigger`. The caller pre-filters rows
    /// with blank Definition (Trigger.create rejects them); `def` is
    /// the unwrapped non-blank definition string.
    let private parseTriggerRowFor
        (moduleName: string)
        (entityName: string)
        (row: TriggerRow)
        (def: string)
        : Result<Trigger> =
        let trigKey  = triggerSsKey moduleName entityName row.TriggerName
        let trigName = Name.create row.TriggerName
        match trigKey, trigName with
        | Ok k, Ok n ->
            Trigger.create k n row.IsDisabled def
        | _ ->
            propagateOrFallback
                [ Result.errors trigKey
                  Result.errors trigName ]
                (fun () ->
                    adapterError
                        "triggerRowBuild"
                        (sprintf
                            "Failed to build trigger '%s' on kind '%s' from rowset bundle."
                            row.TriggerName
                            entityName))

    /// Slice 5.13.ossys-rowsets-cluster — per-ColumnCheck lift. V1's
    /// `#ColumnCheckReality` is per-column; V2's `Kind.ColumnChecks`
    /// is table-scoped (multi-column CHECKs collapse to one entry).
    /// The caller dedupes by ConstraintName before passing rows here.
    let private parseColumnCheckRowFor
        (moduleName: string)
        (entityName: string)
        (row: ColumnCheckRow)
        : Result<ColumnCheck> =
        let chkKey  = columnCheckSsKey moduleName entityName row.ConstraintName
        let chkName = Name.create row.ConstraintName
        match chkKey, chkName with
        | Ok k, Ok n ->
            ColumnCheck.create k (Some n) row.Definition row.IsNotTrusted
        | _ ->
            propagateOrFallback
                [ Result.errors chkKey
                  Result.errors chkName ]
                (fun () ->
                    adapterError
                        "columnCheckRowBuild"
                        (sprintf
                            "Failed to build CHECK constraint '%s' on kind '%s' from rowset bundle."
                            row.ConstraintName
                            entityName))

    let private parseKindRow
        (ctx: RowsetParseContext)
        (moduleName: string)
        (moduleEspaceKind: string option)
        (kindRow: KindRow)
        : Result<Kind> =
        let kindKey  = kindSsKeyFromRow moduleName kindRow
        let kindName = Name.create kindRow.EntityName
        // Chapter A.0' slice β — the session-21 attribute-level
        // filter retires on the rowset path (parity with the JSON
        // path retirement). Inactive attributes are carried with
        // `Attribute.IsActive=false`. References on inactive
        // attributes are carried through the join below (an
        // inactive attribute still has its reference rows; the
        // adapter's adapter-boundary discipline restricts to
        // `DataIntent` carriage).
        let attrRows =
            Map.tryFind kindRow.EntityId ctx.AttributesByEntity
            |> Option.defaultValue []
        let attrResults =
            attrRows
            |> Bench.iterMap "adapter.osm.parse.rowsetAttribute" (parseAttributeRow moduleName kindRow.EntityName)
        let foldedAttrs = Result.aggregate attrResults
        let refResults =
            attrRows
            |> List.collect (fun a ->
                Map.tryFind a.AttrId ctx.ReferencesByAttr
                |> Option.defaultValue []
                |> List.map (parseReferenceRowFor ctx.KindKeysByEntityId moduleName kindRow.EntityName a))
        let foldedRefs = Result.aggregate refResults
        // Slice 5.13.ossys-rowsets-cluster — per-Kind index assembly
        // from `IndexesByEntity` × `IndexColumnsByIndex`. The JOIN
        // resolves each IndexColumnRow's HumanAttr (preferred) or
        // PhysicalColumn (fallback) to V2's attribute SsKey via the
        // same `attributeSsKey` synthesizer the JSON path uses. Sort
        // by Ordinal within (key columns + included columns)
        // partitions for byte-determinism.
        let indexResults =
            Map.tryFind kindRow.EntityId ctx.IndexesByEntity
            |> Option.defaultValue []
            |> Bench.iterMap "adapter.osm.parse.rowsetIndex" (parseIndexRowFor ctx moduleName kindRow.EntityName attrRows)
        let foldedIndexes = Result.aggregate indexResults
        let triggerResults =
            Map.tryFind kindRow.EntityId ctx.TriggersByEntity
            |> Option.defaultValue []
            |> List.choose (fun row ->
                // `Trigger.create` rejects blank Definition; V1 rows
                // with NULL TriggerDefinition (rare; defensive)
                // filter out at the adapter boundary.
                match row.Definition with
                | None -> None
                | Some def when System.String.IsNullOrWhiteSpace def -> None
                | Some def -> Some (parseTriggerRowFor moduleName kindRow.EntityName row def))
        let foldedTriggers = Result.aggregate triggerResults
        let columnCheckResults =
            Map.tryFind kindRow.EntityId ctx.ColumnChecksByEntity
            |> Option.defaultValue []
            // Dedupe by ConstraintName — a multi-column CHECK
            // surfaces once per column in `#ColumnCheckReality`; V2's
            // `Kind.ColumnChecks` is table-scoped (one entry per
            // unique constraint).
            |> List.distinctBy (fun row -> row.ConstraintName)
            |> Bench.iterMap "adapter.osm.parse.rowsetColumnCheck" (parseColumnCheckRowFor moduleName kindRow.EntityName)
        let foldedColumnChecks = Result.aggregate columnCheckResults
        match kindKey, kindName, foldedAttrs, foldedRefs,
              foldedIndexes, foldedTriggers, foldedColumnChecks with
        | Ok k, Ok n, Ok attrs, Ok refs,
          Ok idx, Ok trigs, Ok checks ->
            let modality =
                [
                    if kindRow.IsStatic       then yield Static []
                    if kindRow.IsSystemEntity then yield SystemOwned
                ]
            Result.success
                { SsKey       = k
                  Name        = n
                  Origin      = parseOriginFromRowset kindRow.IsExternal moduleEspaceKind
                  Modality    = modality
                  Physical    = { Schema = kindRow.DbSchema
                                  Table  = kindRow.PhysicalTableName; Catalog = None }
                  Attributes  = attrs
                  References  = refs
                  Indexes     = idx
                  Description = kindRow.Description
                  IsActive    = kindRow.IsActive
                  Triggers    = trigs
                  ColumnChecks = checks
                  // Module-level ExtendedProperties not surfaced by
                  // V1's rowsets (chapter A.0' slice ζ deferral).
                  ExtendedProperties = [] }
        | _ ->
            propagateOrFallback
                [ Result.errors kindKey
                  Result.errors kindName
                  Result.errors foldedAttrs
                  Result.errors foldedRefs
                  Result.errors foldedIndexes
                  Result.errors foldedTriggers
                  Result.errors foldedColumnChecks ]
                (fun () ->
                    adapterError
                        "kindRowBuild"
                        (sprintf
                            "Failed to build kind '%s' in module '%s' from rowset bundle."
                            kindRow.EntityName moduleName))

    let private parseModuleRow
        (ctx: RowsetParseContext)
        (moduleRow: ModuleRow)
        : Result<Module> =
        let modKey  = moduleSsKeyFromRow moduleRow
        let modName = Name.create moduleRow.EspaceName
        let kindRows =
            Map.tryFind moduleRow.EspaceId ctx.KindsByEspace
            |> Option.defaultValue []
        let kindResults =
            kindRows
            |> Bench.iterMap "adapter.osm.parse.rowsetKind" (parseKindRow ctx moduleRow.EspaceName moduleRow.EspaceKind)
        let foldedKinds = Result.aggregate kindResults
        match modKey, modName, foldedKinds with
        | Ok k, Ok n, Ok kinds ->
            // Chapter A.0' slice ζ — Module.ExtendedProperties empty
            // on the rowset path; V1's rowsets do not surface
            // module-level extended properties.
            Module.create k n kinds moduleRow.IsActive []
        | _ ->
            propagateOrFallback
                [ Result.errors modKey
                  Result.errors modName
                  Result.errors foldedKinds ]
                (fun () ->
                    adapterError
                        "moduleRowBuild"
                        (sprintf
                            "Failed to build module '%s' from rowset bundle."
                            moduleRow.EspaceName))

    /// V1 rowset bundle → V2 Catalog. Sibling to `parseDocument` (JSON
    /// path). The flat-list bundle joins by FK ID columns at load time
    /// (`AttributeRow.EntityId` ↔ `KindRow.EntityId`; `KindRow.EspaceId`
    /// ↔ `ModuleRow.EspaceId`; `ReferenceRow.AttrId` ↔
    /// `AttributeRow.AttrId`); the resulting structure feeds the
    /// existing `Module.create` / `Catalog.create` aggregate-root
    /// smart constructors, so referential-integrity invariants are
    /// checked at the boundary identically to the JSON path.
    ///
    /// Big-O / pillar 7 perf clause: O(N + E + A + R) for the input
    /// bundle plus O(E + A) for the three Map.ofList constructions
    /// (one per ID-keyed projection). Per-module dispatch is O(E_m × A_e)
    /// with O(1) Map lookups; per-kind reference assembly is O(R_e)
    /// with O(1) Map lookups. Linear in the bundle's total size;
    /// matches `parseDocument`'s complexity class.
    let private parseRowsetBundle (bundle: RowsetBundle) : Result<Catalog> =
        let attributesByEntity =
            bundle.Attributes |> List.groupBy (fun a -> a.EntityId) |> Map.ofList
        let kindsByEspace =
            bundle.Kinds |> List.groupBy (fun k -> k.EspaceId) |> Map.ofList
        let referencesByAttr =
            bundle.References |> List.groupBy (fun r -> r.AttrId) |> Map.ofList
        // Slice 5.13.ossys-rowsets-cluster — per-id-keyed groupings
        // for the new index/trigger/check axes.
        let indexesByEntity =
            bundle.Indexes |> List.groupBy (fun i -> i.EntityId) |> Map.ofList
        let indexColumnsByIndex =
            bundle.IndexColumns
            |> List.groupBy (fun c -> c.EntityId, c.IndexName)
            |> Map.ofList
        let triggersByEntity =
            bundle.Triggers |> List.groupBy (fun t -> t.EntityId) |> Map.ofList
        // ColumnChecks are per-AttrId in V1's rowset; group up to
        // per-EntityId via the AttrId→EntityId resolution from the
        // attributes bundle. This pre-roll is O(C) rather than the
        // per-Kind alternative which would be O(C × E) (re-walk per
        // Kind), so the pre-built map keeps parseKindRow cheap.
        let entityByAttrId =
            bundle.Attributes
            |> List.map (fun a -> a.AttrId, a.EntityId)
            |> Map.ofList
        let columnChecksByEntity =
            bundle.ColumnChecks
            |> List.choose (fun row ->
                Map.tryFind row.AttrId entityByAttrId
                |> Option.map (fun eid -> eid, row))
            |> List.groupBy fst
            |> List.map (fun (eid, pairs) -> eid, pairs |> List.map snd)
            |> Map.ofList
        let moduleNameByEspaceId =
            bundle.Modules
            |> List.map (fun m -> m.EspaceId, m.EspaceName)
            |> Map.ofList
        let kindKeysByEntityId =
            bundle.Kinds
            |> List.choose (fun k ->
                match Map.tryFind k.EspaceId moduleNameByEspaceId with
                | None -> None
                | Some modName ->
                    let resolved =
                        match k.EntitySsKey with
                        | Some g -> Ok (SsKey.ossysOriginal g)
                        | None -> kindSsKey modName k.EntityName
                    match resolved with
                    | Ok key -> Some (k.EntityId, key)
                    | Error _ -> None)
            |> Map.ofList
        let ctx : RowsetParseContext =
            { KindKeysByEntityId   = kindKeysByEntityId
              KindsByEspace        = kindsByEspace
              AttributesByEntity   = attributesByEntity
              ReferencesByAttr     = referencesByAttr
              IndexesByEntity      = indexesByEntity
              IndexColumnsByIndex  = indexColumnsByIndex
              TriggersByEntity     = triggersByEntity
              ColumnChecksByEntity = columnChecksByEntity }
        let moduleResults =
            bundle.Modules |> Bench.iterMap "adapter.osm.parse.rowsetModule" (parseModuleRow ctx)
        match Result.aggregate moduleResults with
        | Ok modules ->
            Catalog.create modules []
        | Error errors -> Error errors

    /// Parse a V1 `osm_model.json` snapshot into a V2 `Catalog`.
    ///
    /// Async at the boundary even though the JSON-path implementation
    /// is synchronous; future async-by-nature variants
    /// (DACPAC unzip, eventual `LiveOssysConnection`) need the
    /// `Task<...>` shape. See `DECISIONS 2026-05-15 — OSSYS adapter
    /// parse signature` for the rationale.
    let parse (source: SnapshotSource) : Task<Result<Catalog>> =
        use _ = Bench.scope "adapter.osm.parse"
        match source with
        | SnapshotJson json ->
            Task.FromResult(parseJsonString json)
        | SnapshotRowsets bundle ->
            // Chapter 3.2 slice 1. Pure translation; no I/O. The
            // rowset-shaped V1 metadata flows through
            // `parseRowsetBundle` for FK-by-ID join + Module/Kind/
            // Attribute construction. Closed-DU expansion empirical-
            // test discipline (`DECISIONS 2026-05-13`): exhaustiveness
            // errors should light up only at this match site.
            Task.FromResult(parseRowsetBundle bundle)
        | SnapshotFile path ->
            // Read-then-parse is the natural shape; async file I/O
            // would benefit primarily for very large snapshots, which
            // the chapter-open scope hasn't reached yet. Keep the
            // synchronous read for now; the boundary is async.
            try
                let json = File.ReadAllText(path)
                Task.FromResult(parseJsonString json)
            with
            | :? IOException as ex ->
                Task.FromResult(
                    Result.failureOf (
                        adapterError
                            "fileReadFailed"
                            (sprintf "Failed to read snapshot file '%s': %s" path ex.Message)))
            | :? System.UnauthorizedAccessException as ex ->
                Task.FromResult(
                    Result.failureOf (
                        adapterError
                            "fileAccessDenied"
                            (sprintf "Access denied reading snapshot file '%s': %s" path ex.Message)))

    /// Chapter A.4.7 slice δ. The OSSYS adapter's `RegisteredTransform`
    /// surface — metadata-only, per the adapter's boundary-stage
    /// nature. The adapter's `parse : SnapshotSource -> Task<Result<
    /// Catalog>>` is not a pure `Catalog -> Lineage<Diagnostics<...>>`
    /// transformation (it does I/O for `SnapshotFile`, returns
    /// `Task<Result<...>>` for boundary-error reporting); the
    /// `RegisteredTransform<'In, 'Out>` typed shell doesn't fit cleanly.
    /// Slice δ ships `registeredMetadata : RegisteredTransformMetadata`
    /// — the metadata view of the adapter's harvest-discipline
    /// classification, suitable for the registry's totality-coverage
    /// scan (slice θ) and manifest emission (slice η).
    ///
    /// Per the chapter A.4.7 open: "every transformative rule (filters,
    /// remaps, derivations — not pass-through field-to-field mappings)
    /// gets a RegisteredTransform entry." Slice δ packages the rules
    /// as Sites within one registry entry (intra-adapter classification
    /// fidelity per pillar 9 + Q11); per-rule separate registration
    /// would require extracting each helper into a standalone
    /// transformation, which is a larger refactor deferred-with-trigger
    /// (real consumer pressure for per-rule audit granularity).
    ///
    /// All adapter rules classify as `DataIntent`. The adapter is a
    /// translation layer carrying V1 source-schema evidence forward
    /// into V2 typed evidence; no operator opinion enters at the
    /// adapter boundary (the operator-intent passes — IsActive
    /// filter retired at slice β, etc. — run downstream of the
    /// adapter). The skeleton-purity property test (slice θ) will
    /// witness that `Project(catalog, Policy.empty, profile)` traverses
    /// the adapter without emitting any `OperatorIntent` lineage event.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "ossysCatalogReader" Schema
            [ TransformSite.dataIntent "identitySynthesis"
                "Synthesize V2 SsKeys from V1 names: moduleSsKey / kindSsKey / attributeSsKey / referenceSsKey / indexSsKey / triggerSsKey / sequenceSsKey / columnCheckSsKey. Derivation is deterministic from source identifiers; no operator opinion enters."
              TransformSite.dataIntent "typeTranslation"
                "Map V1 type/code values to V2 typed DUs: parsePrimitiveType (V1 dataType string → V2 PrimitiveType per A13's typed surface); parseDeleteRule (V1 OutSystems-domain onDelete code 'Delete'/'Protect'/'Ignore'/'SetNull' → V2 ReferenceAction); parseSqlForeignKeyAction (V1 #FkReality SQL-Server-domain update_referential_action_desc 'NO_ACTION'/'CASCADE'/'SET_NULL'/'SET_DEFAULT' → V2 ReferenceAction option — distinct vocabulary from parseDeleteRule per slice A.4.7'-prelude.row17-18-rowset-roundtrip); parseOrigin / parseOriginFromRowset (isExternal flag → Origin DU). All translations are structural — V1's vocabulary maps deterministically into V2's typed system."
              TransformSite.dataIntent "jsonAggregateParsing"
                "Assemble JSON-path IR records: parseAttribute / parseReference / parseIndex / parseTrigger / parseExtendedProperty / parseKind / parseModule / parseDocument / parseJsonString. Each parser threads V1 evidence into V2's typed records; the parsing is field-by-field translation with no operator overlay."
              TransformSite.dataIntent "rowsetAggregateParsing"
                "Assemble rowset-path IR records: parseAttributeRow / parseReferenceRowFor / parseKindRow / parseModuleRow / parseRowsetBundle. Mirrors the JSON-path semantics for the rowset-source variant (chapter 3.2 slice 1 onward); same DataIntent translation discipline. Slice A.4.7'-prelude.row53-source-side extended the AttributeRow projection to surface V1 `#ColumnReality` reflection (IsComputed + ComputedDefinition + DefaultConstraintName) — V1 deployed-target evidence flows into `Attribute.Computed : ComputedColumnConfig option` + `Attribute.DefaultName : Name option` via `MetadataSnapshotRunner.toBundle`'s join."
              TransformSite.dataIntent "isActiveCarryThrough"
                "Chapter A.0' slice β retroactive site. IsActive is carried through at Module / Kind / Attribute levels (not filtered at the adapter boundary; the session-21 filter was retired as a mis-placed OperatorIntent of Selection per DECISIONS 2026-05-16 (slice β) — the first worked example of pillar 9). The carriage itself is DataIntent evidence; a downstream Selection-axis pass that re-applies an inactive-records drop is deferred-with-trigger per IR-grows-under-evidence."
              TransformSite.dataIntent "tableIdCatalogRead"
                "Chapter A.0' slice θ retroactive site. V1's db_catalog field is read into TableId.Catalog (string option); cross-database FK qualification carries through without silent degradation to implicit-current-database scope. DataIntent — source-schema evidence carried forward." ]
