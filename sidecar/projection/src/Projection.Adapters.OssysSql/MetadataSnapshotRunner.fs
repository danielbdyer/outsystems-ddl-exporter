namespace Projection.Adapters.OssysSql

// LINT-ALLOW-FILE: OSSYS metadata-extraction adapter at the boundary — terminal text over typed
//   segments, function-local result-set accumulators, and `box`/`unbox` at the
//   DbDataReader value boundary (BCL reader returns `obj` columns). The
//   intentional `open Projection.Adapters.Osm` composition carries its own
//   per-line marker; this file marker covers the boundary mutation + text.

open System
open System.Data
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Osm  // LINT-ALLOW: intentional adapter composition — the OssysSql extraction adapter assembles a `OssysRowsetTypes.RowsetBundle` (Osm's integration contract) for `CatalogReader.parse`; the SQL-extraction adapter feeds the projection adapter, a documented one-way dependency per the chapter-5.0 slice-γ bootstrap+extract flow

/// V2's metadata-snapshot runner. Carbon-copies V1's `MetadataSnapshotRunner`
/// (`Osm.Pipeline.SqlExtraction.MetadataSnapshotRunner`) at a much smaller
/// surface: V1 layers `IDbConnectionFactory` + `IDbCommandExecutor` +
/// per-processor abstractions over a generic V1-domain pipeline; V2 ships
/// a direct `SqlConnection`-receiving function because V2's runner is the
/// canary's offline-extraction surface — it walks the script's
/// `ExpectedResultSets` result sets (the count lives on that constant
/// alone), parses the V2-consumed rowsets into typed F# records mirroring
/// V1's DTOs, and assembles a `OssysRowsetTypes.RowsetBundle` consumable
/// by V2's existing `CatalogReader.parse` JSON / rowset adapter.
///
/// **Chapter 5.0 slice γ.** The canary's bootstrap+extract flow:
///   1. Caller deploys `MetadataExtractionSql.readEdgeCaseSeed()` against
///      a clean SQL Server database (creates synthetic OSSYS schema +
///      edge-case data).
///   2. Caller invokes `MetadataSnapshotRunner.runAsync` with an open
///      `SqlConnection` to that database + the V1 parameters.
///   3. This module reads `MetadataExtractionSql.read()` (the rowsets SQL),
///      executes it via `SqlCommand`, and walks `DbDataReader.NextResultAsync`
///      to enumerate all `ExpectedResultSets` result sets.
///   4. The V2-consumed result sets (logical metadata, physical
///      reflection, sequences/temporal, the capability vector) parse into
///      typed F# records; the V1-SUNSET JSON-aggregation tail is skipped
///      (the SQL still emits those sets in order).
///   5. Slice δ composes the typed records into the `RowsetBundle` via
///      JOIN logic (PhysicalTables → KindRow.DbSchema; ForeignKey reality
///      → ReferenceRow.DeleteRuleCode / HasDbConstraint).
///
/// The runner does not own the connection lifecycle; the caller opens +
/// disposes. T1 byte-determinism preserved: the SQL is deterministic
/// (no `NEWID()` or `GETDATE()` in user-visible projections per V1's
/// script); result-set ordering is fixed by the script.
[<RequireQualifiedAccess>]
module MetadataSnapshotRunner =

    /// Parameters for V1's rowsets SQL script. Mirrors the 5 declared
    /// parameters at the head of `outsystems_metadata_rowsets.sql`:
    ///   - `ModuleNames`: CSV of module (eSpace) names to include.
    ///     Empty = all modules.
    ///   - `IncludeSystem`: include system modules (e.g., `SystemUsers`).
    ///   - `IncludeInactive`: include inactive entities.
    ///   - `OnlyActiveAttributes`: skip attributes with `Is_Active = 0`.
    ///   - `EntityFilterJson`: per-module entity allow-list as JSON.
    ///     Null = no per-module filter.
    type SnapshotParameters =
        {
            ModuleNames          : string list
            IncludeSystem        : bool
            IncludeInactive      : bool
            OnlyActiveAttributes : bool
            EntityFilterJson     : string option
        }

    /// Default parameter shape: all modules, all entities, all attributes,
    /// include system + inactive. The "show me everything" stance — useful
    /// for the canary and for first-extract baselines.
    let defaultParameters : SnapshotParameters =
        {
            ModuleNames          = []
            IncludeSystem        = true
            IncludeInactive      = true
            OnlyActiveAttributes = false
            EntityFilterJson     = None
        }

    /// align-II.9 (E2; audit a1) — one axis a scoped acquisition narrowed
    /// on. Each axis names the `SnapshotParameters` knob that fired it;
    /// the ATTRIBUTE axis is A49's named residual — the pure narrowing
    /// seam cannot express it, so it always pays the wire.
    [<RequireQualifiedAccess>]
    type ScopeAxis =
        /// `ModuleNames` non-empty — narrowed to named modules.
        | Modules
        /// `IncludeSystem = false` — system modules excluded.
        | System
        /// `IncludeInactive = false` — inactive entities excluded.
        | Lifecycle
        /// `OnlyActiveAttributes = true` — inactive attributes excluded
        /// (A49's named residual: never sink-servable).
        | AttributeActivity
        /// `EntityFilterJson` present — narrowed to named entities.
        | EntityFilter

    /// align-II.9 — HOW an acquisition was scoped, as a value: total (the
    /// show-me-everything shape) or narrowed on the named axes. Stamped on
    /// every `MetadataSnapshot`; persisted by the codec as an OPTIONAL
    /// field defaulting `Total` — honest by the totality gate's own
    /// invariant (only total acquisitions were ever witnessed), so old
    /// stored snapshots read back truthfully with no version bump.
    [<RequireQualifiedAccess>]
    type AcquisitionScope =
        | Total
        | Scoped of axes: ScopeAxis list

    [<RequireQualifiedAccess>]
    module ScopeAxis =

        /// Every axis, in canonical (declaration) order — the order
        /// `AcquisitionScope.ofParameters` emits and the codec persists.
        let all : ScopeAxis list =
            [ ScopeAxis.Modules; ScopeAxis.System; ScopeAxis.Lifecycle
              ScopeAxis.AttributeActivity; ScopeAxis.EntityFilter ]

        /// Wire token (the codec's closed vocabulary).
        let token (a: ScopeAxis) : string =
            match a with
            | ScopeAxis.Modules           -> "modules"
            | ScopeAxis.System            -> "system"
            | ScopeAxis.Lifecycle         -> "lifecycle"
            | ScopeAxis.AttributeActivity -> "attributeActivity"
            | ScopeAxis.EntityFilter      -> "entityFilter"

        let tryParse (t: string) : ScopeAxis option =
            all |> List.tryFind (fun a -> token a = t)

    [<RequireQualifiedAccess>]
    module AcquisitionScope =

        /// The ONE classifier: the scope an acquisition ran under, read
        /// from its parameters. Emits axes in canonical order; the empty
        /// axis list IS `Total` (never `Scoped []` — the constructor
        /// discipline below keeps the two states distinct).
        let ofParameters (p: SnapshotParameters) : AcquisitionScope =
            let axes =
                [ if not (List.isEmpty p.ModuleNames) then ScopeAxis.Modules
                  if not p.IncludeSystem then ScopeAxis.System
                  if not p.IncludeInactive then ScopeAxis.Lifecycle
                  if p.OnlyActiveAttributes then ScopeAxis.AttributeActivity
                  if Option.isSome p.EntityFilterJson then ScopeAxis.EntityFilter ]
            match axes with
            | [] -> AcquisitionScope.Total
            | axes -> AcquisitionScope.Scoped axes

        /// Whether a state witnessed under `held` can serve a read
        /// requested under `requested` through the pure narrowing seam
        /// (A49's three-way law): a TOTAL held state serves any request
        /// narrowed on the module/system/lifecycle/entity axes (narrowing
        /// commutes across the wire); the ATTRIBUTE axis is the law's
        /// named residual — it always pays the wire. A scoped held state
        /// serves only its identical scope (nothing witnesses scoped
        /// today — the totality gate — so the arm is the honest
        /// future-proof, never a guess).
        let serves (held: AcquisitionScope) (requested: AcquisitionScope) : bool =
            match held, requested with
            | AcquisitionScope.Total, AcquisitionScope.Total -> true
            | AcquisitionScope.Total, AcquisitionScope.Scoped axes ->
                not (List.contains ScopeAxis.AttributeActivity axes)
            | AcquisitionScope.Scoped h, r -> AcquisitionScope.Scoped h = r

        /// Wire token: `total`, or `scoped:` + the axis tokens joined by
        /// `+` in canonical order.
        let token (s: AcquisitionScope) : string =
            match s with
            | AcquisitionScope.Total -> "total"
            | AcquisitionScope.Scoped axes ->
                "scoped:" + (axes |> List.map ScopeAxis.token |> String.concat "+")  // LINT-ALLOW: closed-vocabulary wire token minted at the codec boundary — a fixed discriminator prefix + validated axis tokens; the inverse tryParse round-trips it

        /// The token's inverse — fail-closed: an unknown discriminator or
        /// axis token, or an empty axis list, is `None` (a malformed
        /// stored scope never silently reads as any particular scope).
        let tryParse (raw: string) : AcquisitionScope option =
            if raw = "total" then Some AcquisitionScope.Total
            elif raw.StartsWith "scoped:" then
                let parts = raw.Substring("scoped:".Length).Split '+' |> Array.toList
                let axes = parts |> List.map ScopeAxis.tryParse
                if List.isEmpty parts || axes |> List.exists Option.isNone then None
                else Some (AcquisitionScope.Scoped (axes |> List.choose id))
            else None

    /// Per-rowset progress observation. Invoked by `runAsync` after each
    /// rowset's parse completes (or skip completes, for the 18 V2-skipped
    /// rowsets). `ResultSetIndex` is the zero-based position in the
    /// emitted result-set stream; `ResultSetName` is the V2-side label
    /// (`"modules"`, `"entities"`, `"attributes"`, `"references"`,
    /// `"physicalTables"`, or `"skipped-N"` for the 18 V2-skipped sets);
    /// `RowCount` is the number of rows parsed (always 0 for skipped
    /// sets — we don't materialize the unread rows).
    ///
    /// Matrix row 36 cash-out. V1's `ITaskProgressAccessor` is the
    /// V1-shape for the same axis; V2 lifts to a simpler F# callback
    /// that adapter consumers (CLI / TUI / Spectre) wire as they need.
    type ProgressObservation =
        {
            ResultSetIndex : int
            ResultSetName  : string
            RowCount       : int
        }

    /// Callback type for per-rowset progress observation. Default is
    /// no-op (see `noOpProgress`); CLI surfaces wire stdout / TUI
    /// adapters by passing their own callback.
    type OnRowsetComplete = ProgressObservation -> unit

    /// No-op progress callback. Used as the default when callers don't
    /// supply one — extraction proceeds with zero observation overhead.
    let noOpProgress : OnRowsetComplete = fun _ -> ()

    /// Optional axes for `runAsyncWithOptions`. Per the **sibling-wrapper
    /// discipline** (`CLAUDE.md` operating disciplines / chapter 4.7
    /// cleanup) — `MetadataSnapshotRunner` has two genuinely-orthogonal
    /// consumption axes (progress observation; command timeout). The
    /// principled count is **2 entry points** (`runAsync` zero-default;
    /// `runAsyncWithOptions` full-explicit) plus a typed record carrying
    /// the axes; new axes extend `RunOptions` rather than spawning new
    /// wrapper functions.
    ///
    /// - `CommandTimeoutSeconds = None` preserves V2's canary-time
    ///   behavior (sets `command.CommandTimeout <- 0`, unlimited;
    ///   tolerates V1's `SET TEXTSIZE -1` + complex queries). `Some n`
    ///   sets the ADO.NET timeout to `n` seconds (V1-style;
    ///   operator-tunable when V2 ships production CLI for cloud OSSYS).
    ///   Matrix row 33.
    /// - `OnRowsetComplete = noOpProgress` keeps extraction
    ///   observation-free; CLI surfaces wire their own callback. Matrix
    ///   row 36.
    type RunOptions =
        {
            CommandTimeoutSeconds : int option
            OnRowsetComplete      : OnRowsetComplete
        }

    /// Default options — canary-preserving (unlimited timeout, no
    /// progress observation). `runAsync` delegates with these.
    let defaultOptions : RunOptions =
        {
            CommandTimeoutSeconds = None
            OnRowsetComplete      = noOpProgress
        }

    /// V1-shaped typed rowsets parsed from the first 5 result sets.
    /// These mirror V1's `Outsystems*Row` DTOs at the columns V2's
    /// `OssysRowsetTypes.RowsetBundle` consumes (with the JOIN composition
    /// happening in slice δ).
    type OssysModuleRow =
        { EspaceId       : int
          EspaceName     : string
          IsSystemModule : bool
          IsActive       : bool
          EspaceKind     : string option
          EspaceSsKey    : Guid option }

    type OssysEntityRow =
        { EntityId          : int
          EntityName        : string
          PhysicalTableName : string
          EspaceId          : int
          IsActive          : bool
          IsSystemEntity    : bool
          IsExternal        : bool
          DataKind          : string option
          PrimaryKeySsKey   : Guid option
          EntitySsKey       : Guid option
          Description       : string option }

    type OssysAttributeRow =
        { AttrId            : int
          EntityId          : int
          AttrName          : string
          AttrSsKey         : Guid option
          DataType          : string option
          Length            : int option
          Precision         : int option
          Scale             : int option
          /// Authored `ossys_Entity_Attr.Default_Value` — the LOGICAL
          /// Service-Studio default (e.g. `False` for a BIT). Distinct
          /// from `#ColumnReality.DefaultDefinition` (the reflected SQL
          /// Server constraint expression): the authored surface says the
          /// team CONFIGURED a default; the reflected one may merely
          /// restate normal SQL behavior.
          DefaultValue      : string option
          IsMandatory       : bool
          IsActive          : bool
          IsAutoNumber      : bool
          IsIdentifier      : bool
          RefEntityId       : int option
          OriginalName      : string option
          ExternalDbType    : string option
          DeleteRule        : string option
          PhysicalCol       : string
          Description       : string option
          // WP8 / NM-72 — Service-Studio authored order from the real
          // `ossys_Entity_Attr.Order_Num` column (the rowset SQL
          // COALESCEs to the attribute's creation `Id` when the source
          // estate lacks the column, so this is `Some` on a live
          // extraction). Threads through `toBundle` into the
          // `AttributeRow.Order` the rowset reader consumes.
          Order             : int option }

    type OssysReferenceRow =
        { AttrId          : int
          RefEntityId     : int option
          RefEntityName   : string option
          RefPhysicalName : string option }

    type OssysPhysicalTableRow =
        { EntityId   : int
          SchemaName : string
          TableName  : string
          ObjectId   : int }

    /// `#ColumnReality` rowset (matrix row 11). One row per V1 attribute
    /// reflecting the deployed-target `sys.columns` projection — typed
    /// per-column reality the source-side adapter sees through V1's
    /// SQL. Consumed within this file: `toBundle` lifts collation /
    /// computed / default-constraint facets (~line 1180), and
    /// `columnRealityDivergences` (~line 1016, wired at
    /// `LiveModelRead.fs:139`) diffs it against the logical facets for
    /// identity/nullability divergence diagnostics. Not yet compared
    /// against `Profile.AttributeReality` (matrix row 49) — that
    /// remains a future tightening-rule trigger.
    type OssysColumnRealityRow =
        { AttrId                : int
          IsNullable            : bool
          SqlType               : string option
          MaxLength             : int option
          Precision             : int option
          Scale                 : int option
          CollationName         : string option
          IsIdentity            : bool
          IsComputed            : bool
          ComputedDefinition    : string option
          DefaultConstraintName : string option
          DefaultDefinition     : string option
          PhysicalColumn        : string option
          /// DECISIONS 2026-07-18 (#669 EF-21) — `sys.computed_columns
          /// .is_persisted`. Appended LAST per the append-only column
          /// contract; threads to `ComputedColumnConfig.IsPersisted`,
          /// whose emission already renders the PERSISTED keyword.
          IsPersisted           : bool }

    /// `#ColumnCheckReality` rowset (matrix row 12). Per-column CHECK
    /// constraint reflection. V2's `Kind.ColumnChecks` IR exists per
    /// chapter A.0' slice ε (matrix row 50); the rowset path now wires
    /// to it.
    type OssysColumnCheckRow =
        { AttrId         : int
          ConstraintName : string
          /// `None` when the principal lacks VIEW DEFINITION (the
          /// managed-cloud grant) — `sys.check_constraints.definition`
          /// NULLs out; the read must not fail on it (2026-07-06).
          Definition     : string option
          IsNotTrusted   : bool }

    /// `#PhysColsPresent` rowset (matrix row 14). Set of `AttrId` whose
    /// physical column actually exists in `sys.columns` — V1's
    /// orphan-attribute detection signal. No V2 consumer yet; lifts at
    /// the runner layer for future Profile-evidence consumers.
    type OssysPhysColsPresentRow =
        { AttrId : int }

    /// `#AllIdx` rowset (matrix row 15). Per-index physical reflection
    /// (UQ + IX + PK; columns flagged by `Kind = 'UNIQUE' | 'INDEX' |
    /// 'PRIMARY KEY'`). Retires V2's structural dependence on V1's
    /// `IndexJson` JSON-aggregation rowset (row 26) — V2's index axis
    /// becomes V1-IndexJson-independent. JOIN target for
    /// `Kind.Indexes` IR.
    type OssysAllIdxRow =
        { EntityId            : int
          ObjectId            : int
          IndexId             : int
          IndexName           : string
          IsUnique            : bool
          IsPrimary           : bool
          Kind                : string
          FilterDefinition    : string option
          IsDisabled          : bool
          IsPadded            : bool
          FillFactor          : int
          IgnoreDupKey        : bool
          AllowRowLocks       : bool
          AllowPageLocks      : bool
          NoRecompute         : bool
          DataSpaceName       : string option
          DataSpaceType       : string option
          PartitionColumnsJson: string option
          DataCompressionJson : string option }

    /// `#IdxColsMapped` rowset (matrix row 16). Per-index column
    /// membership — key columns + included columns + sort direction +
    /// human attribute name. JOINs by (EntityId, IndexName) against
    /// `OssysAllIdxRow`; the (EntityId, AttrId) lookup against
    /// `OssysAttributeRow` resolves human → AttrId for V2's SsKey
    /// derivation.
    type OssysIdxColMappedRow =
        { EntityId       : int
          IndexName      : string
          Ordinal        : int
          PhysicalColumn : string option
          IsIncluded     : bool
          Direction      : string option
          HumanAttr      : string option }

    /// `#FkReality` rowset (matrix row 17). Per-FK constraint
    /// reflection from V1 source-side `sys.foreign_keys`. Carries
    /// `OnUpdate` + `IsNoCheck` which V2's existing Reference IR does
    /// not yet model (matrix rows 58 + 59 cash-out); the typed rowset
    /// lifts at the runner layer so a future Reference IR extension
    /// wires from this evidence.
    type OssysFkRealityRow =
        { EntityId           : int
          FkObjectId         : int
          FkName             : string
          DeleteAction       : string option
          UpdateAction       : string option
          ReferencedObjectId : int
          ReferencedEntityId : int option
          ReferencedSchema   : string option
          ReferencedTable    : string option
          IsNoCheck          : bool }

    /// `#FkColumns` rowset (matrix row 18). Per-FK column membership
    /// (composite FK support). V2's existing Reference IR is
    /// single-column per chapter 5.0; multi-column FKs (composite keys)
    /// would consume this in a future IR refinement. Lifts at runner
    /// layer.
    type OssysFkColumnRow =
        { EntityId           : int
          FkObjectId         : int
          Ordinal            : int
          ParentColumn       : string
          ReferencedColumn   : string
          ParentAttrId       : int option
          ParentAttrName     : string option
          ReferencedAttrId   : int option
          ReferencedAttrName : string option }

    /// `#Triggers` rowset (matrix row 23). Per-trigger reflection from
    /// V1 source-side `sys.triggers`. V2's `Kind.Triggers` IR exists
    /// (chapter A.0' slice γ); the rowset path now wires to it. The
    /// trigger Definition is the full T-SQL `CREATE TRIGGER ...` body
    /// V1 reconstructs from `sys.sql_modules`; V2's Trigger record
    /// carries it through to emit.
    type OssysTriggerRow =
        { EntityId          : int
          TriggerName       : string
          IsDisabled        : bool
          TriggerDefinition : string option }

    /// Rowset 24 — deployed `sys.sequences` reflection (DECISIONS
    /// 2026-07-18; #669 EF-22). Appended at the script's end; the ten
    /// axes mirror `ReadSide`'s `SequenceRow` so both lanes reconstruct
    /// the same `Sequence` values.
    type OssysSequenceRow =
        { Schema       : string
          Name         : string
          DataType     : string
          StartValue   : decimal option
          Increment    : decimal option
          MinimumValue : decimal option
          MaximumValue : decimal option
          IsCycling    : bool
          IsCached     : bool
          CacheSize    : int option }

    /// Rowset 25 — per-entity system-versioning reflection (DECISIONS
    /// 2026-07-18; #669 EF-23). Lifts into `ModalityMark.Temporal` so
    /// the estate board's temporal dealbreaker fires from the mark.
    type OssysTemporalRow =
        { EntityId       : int
          HistorySchema  : string option
          HistoryTable   : string option
          PeriodStart    : string option
          PeriodEnd      : string option
          RetentionValue : int option
          RetentionUnit  : string option }

    /// Rowset 26 — the capability vector (the data-sink chapter, S2
    /// 2026-08-15). The script's @Has* COL_LENGTH probes, finally
    /// returned: which optional `ossys_Entity_Attr` columns THIS estate's
    /// platform version exposes, plus which entity-description column the
    /// extraction chose. One row per run; the sink persists it with every
    /// witnessed snapshot so ossys-schema drift is data (the journal's
    /// ShapeChanged transition), never surprise. `toBundle` deliberately
    /// ignores it — the vector informs the sink, never the Catalog.
    type OssysCapabilityRow =
        { HasDataType           : bool
          HasType               : bool
          HasPrecision          : bool
          HasScale              : bool
          HasDecimals           : bool
          HasOriginalName       : bool
          HasExternalColumnType : bool
          HasPhysicalColumnName : bool
          HasDatabaseName       : bool
          HasIsIdentifier       : bool
          HasRefEntityId        : bool
          HasIsAutoNumber       : bool
          HasDefaultValue       : bool
          HasDeleteRule         : bool
          HasOriginalType       : bool
          HasAttrSsKey          : bool
          HasLength             : bool
          HasOrderNum           : bool
          HasEntityDescription  : bool }

    /// Aggregate snapshot — the 5 originally-lifted rowsets plus the 8
    /// new physical-reflection rowsets (slice 5.13.ossys-rowsets-cluster).
    /// `toBundle` projects this into V2's `OssysRowsetTypes.RowsetBundle`,
    /// applying JOIN logic for the index / trigger / column-check axes
    /// that have V2 IR consumers ready.
    type MetadataSnapshot =
        {
            Modules            : OssysModuleRow list
            Entities           : OssysEntityRow list
            Attributes         : OssysAttributeRow list
            References         : OssysReferenceRow list
            PhysicalTables     : OssysPhysicalTableRow list
            ColumnReality      : OssysColumnRealityRow list
            ColumnChecks       : OssysColumnCheckRow list
            PhysColsPresent    : OssysPhysColsPresentRow list
            Indexes            : OssysAllIdxRow list
            IndexColumns       : OssysIdxColMappedRow list
            ForeignKeysReality : OssysFkRealityRow list
            ForeignKeyColumns  : OssysFkColumnRow list
            Triggers           : OssysTriggerRow list
            Sequences          : OssysSequenceRow list
            Temporal           : OssysTemporalRow list
            /// Rowset 26 — one row in practice (a list for uniformity
            /// with every other rowset field). Sink-plane evidence only;
            /// `toBundle` ignores it by design (capability-invariance is
            /// property-tested).
            Capabilities       : OssysCapabilityRow list
            /// align-II.9 (E2) — HOW this acquisition was scoped, stamped
            /// by `runAsync` from its own parameters (the one classifier).
            /// The codec persists it as an OPTIONAL field defaulting
            /// `Total` — old stored snapshots read back truthfully by the
            /// totality gate's own invariant (only total acquisitions were
            /// ever witnessed).
            Scope              : AcquisitionScope
        }

    // -------------------------------------------------------------------
    // Internal helpers — capture-then-map row access.
    //
    // The runner executes with `CommandBehavior.SequentialAccess` (cells
    // stream instead of row-buffering; the drained JSON-aggregate rowsets
    // skip without a single cell access). Under SequentialAccess the LIVE
    // reader permits each ordinal to be visited at most once, strictly
    // ascending — an access contract a typed mapper's field order either
    // honors or violates only at run time (`Invalid attempt to read from
    // column ordinal …`). 2026-07-07: `mapAttributeRow`'s PhysicalCol
    // fallback re-read ordinal 2 after ordinal 17 and killed BOTH
    // contract reads of a partial transfer on an estate whose
    // `PhysicalColumnName` resolves NULL. `captureRow` is therefore the
    // ONLY cell-access site against the live reader — one ascending
    // single-visit sweep materializing the row at rest — and every
    // mapper consumes the captured `RowAtRest`, where ordinal access
    // carries no order or visit-count obligation. Accessor surface
    // mirrors V1's `Column.StringOrNull` etc.
    // -------------------------------------------------------------------

    /// One result-set row at rest: every cell materialized exactly once,
    /// in ascending ordinal order. NULL cells carry `DBNull.Value`
    /// (exactly what `SqlDataReader.GetValue` returns), so the accessor
    /// NULL-guards below stay faithful to the prior live `IsDBNull` reads.
    [<Struct>]
    type private RowAtRest = RowAtRest of cells: obj array

    /// The single live-reader cell-access site (see the block comment
    /// above): one strictly-ascending, single-visit sweep — exactly the
    /// access contract `CommandBehavior.SequentialAccess` requires.
    let private captureRow (reader: SqlDataReader) : RowAtRest =
        let cells = Array.zeroCreate<obj> reader.FieldCount
        for ordinal in 0 .. cells.Length - 1 do
            cells.[ordinal] <- reader.GetValue(ordinal)
        RowAtRest cells

    let private cellAt (RowAtRest cells) (ordinal: int) : obj =
        cells.[ordinal]

    let private isDbNull (row: RowAtRest) (ordinal: int) : bool =
        match cellAt row ordinal with
        | :? DBNull -> true
        | _ -> false

    let private readString (row: RowAtRest) (ordinal: int) : string =
        if isDbNull row ordinal then
            invalidOp (sprintf "MetadataSnapshotRunner: required column at ordinal %d was NULL" ordinal)
        else
            unbox<string> (cellAt row ordinal)

    let private readStringOpt (row: RowAtRest) (ordinal: int) : string option =
        if isDbNull row ordinal then None
        else Some (unbox<string> (cellAt row ordinal))

    let private readInt (row: RowAtRest) (ordinal: int) : int =
        // V1 sometimes returns int via flexible widening (Int16 / Int64);
        // a direct Int32 unbox throws on type mismatch. Use Convert to
        // tolerate width variation.
        //
        // Defensive-fallback (slice A.4.7'-prelude.defensive-hardening,
        // 2026-05-19): mirror `readString`'s explicit DBNull guard.
        // `Convert.ToInt32 DBNull.Value` silently returns 0 — which is
        // the WORST failure shape (silent identity/FK corruption in the
        // produced Catalog). Raise on NULL so the caller's snapshot
        // contract is honored (any required-int column with NULL is a
        // V1-source data integrity issue, not a V2 adapter problem).
        if isDbNull row ordinal then
            invalidOp (sprintf "MetadataSnapshotRunner: required int column at ordinal %d was NULL" ordinal)
        else
            System.Convert.ToInt32(cellAt row ordinal)

    let private readIntOpt (row: RowAtRest) (ordinal: int) : int option =
        if isDbNull row ordinal then None
        else Some (readInt row ordinal)

    let private readBool (row: RowAtRest) (ordinal: int) : bool =
        let value = cellAt row ordinal
        match value with
        | :? bool as b -> b
        | :? byte as b -> b <> 0uy
        | :? int as i  -> i <> 0
        | _ -> System.Convert.ToBoolean(value)

    let private readBoolOpt (row: RowAtRest) (ordinal: int) : bool option =
        if isDbNull row ordinal then None
        else Some (readBool row ordinal)

    let private readGuidOpt (row: RowAtRest) (ordinal: int) : Guid option =
        if isDbNull row ordinal then None
        else Some (unbox<Guid> (cellAt row ordinal))

    let private readDecimalOpt (row: RowAtRest) (ordinal: int) : decimal option =
        // sys.sequences ranges surface as decimal(38,0) via the rowset's
        // explicit CAST; Convert tolerates provider width variation the
        // same way `readInt` does.
        if isDbNull row ordinal then None
        else Some (System.Convert.ToDecimal(cellAt row ordinal))

    /// Read all rows of the current result set — each row captured at
    /// rest via `captureRow`, then handed to `mapper`; advance to the
    /// next result set when complete. Returns the rows in source order.
    ///
    /// Capture + mapper failures (e.g., `InvalidCastException` from a
    /// widened SQL type or `InvalidOperationException` from a
    /// required-but-NULL column) re-raise as `RowMappingException`
    /// carrying the `resultSetName` + zero-based `rowIndex` for
    /// downstream classification (matrix row 32 cash-out — the typed
    /// `MetadataExtractionError.RowMappingFailure` variant).
    let private readResultSet<'T>
            (resultSetName: string)
            (reader: SqlDataReader)
            (mapper: RowAtRest -> 'T)
            : Task<'T list> =
        task {
            let acc = ResizeArray<'T>()
            let mutable rowIndex = 0
            let mutable hasMore = true
            while hasMore do
                let! advanced = reader.ReadAsync()
                if advanced then
                    let row =
                        try
                            mapper (captureRow reader)
                        with
                        | :? RowMappingException -> reraise ()
                        | ex -> raise (RowMappingException (resultSetName, rowIndex, ex))
                    acc.Add row
                    rowIndex <- rowIndex + 1
                else hasMore <- false
            return List.ofSeq acc
        }

    /// Skip the current result set without parsing any rows. Used for the
    /// Drained rowsets (`RowsetContract` — the V1-SUNSET JSON aggregation
    /// tail); `NextResultAsync` advances past them.
    let private skipResultSet (reader: SqlDataReader) : Task<unit> =
        task {
            let mutable hasMore = true
            while hasMore do
                let! advanced = reader.ReadAsync()
                if not advanced then hasMore <- false
            return ()
        }

    let private mapModuleRow (r: RowAtRest) : OssysModuleRow =
        { EspaceId       = readInt r 0
          EspaceName     = readString r 1
          IsSystemModule = readBool r 2
          IsActive       = readBool r 3
          EspaceKind     = readStringOpt r 4
          EspaceSsKey    = readGuidOpt r 5 }

    let private mapEntityRow (r: RowAtRest) : OssysEntityRow =
        { EntityId          = readInt r 0
          EntityName        = readString r 1
          PhysicalTableName = readString r 2
          EspaceId          = readInt r 3
          IsActive          = readBool r 4
          IsSystemEntity    = readBool r 5
          IsExternal        = readBool r 6
          DataKind          = readStringOpt r 7
          PrimaryKeySsKey   = readGuidOpt r 8
          EntitySsKey       = readGuidOpt r 9
          Description       = readStringOpt r 10 }

    let private mapAttributeRow (r: RowAtRest) : OssysAttributeRow =
        { AttrId         = readInt r 0
          EntityId       = readInt r 1
          AttrName       = readString r 2
          AttrSsKey      = readGuidOpt r 3
          DataType       = readStringOpt r 4
          Length         = readIntOpt r 5
          Precision      = readIntOpt r 6
          Scale          = readIntOpt r 7
          // ordinal 8 — authored Default_Value; carried so the rowset
          // path emits authored defaults (e.g. BIT False -> DEFAULT 0).
          DefaultValue   = readStringOpt r 8
          IsMandatory    = readBool r 9
          IsActive       = readBool r 10
          IsAutoNumber   = match readBoolOpt r 11 with Some b -> b | None -> false
          IsIdentifier   = match readBoolOpt r 12 with Some b -> b | None -> false
          RefEntityId    = readIntOpt r 13
          OriginalName   = readStringOpt r 14
          ExternalDbType = readStringOpt r 15
          DeleteRule     = readStringOpt r 16
          PhysicalCol    =
              // ordinal 17 = PhysicalColumnName; V1 reads it as
              // nullable but V2 requires non-null for `KindRow.PhysicalCol`.
              // Fall back to AttrName when V1 source omits. The fallback's
              // ordinal-2 re-read is free on the captured row — against
              // the LIVE SequentialAccess reader it was the 2026-07-07
              // partial-transfer killer (see the capture-then-map block).
              match readStringOpt r 17 with
              | Some n when not (System.String.IsNullOrWhiteSpace n) -> n
              | _ -> (readString r 2).ToUpperInvariant()
          Description    = readStringOpt r 22
          // ordinal 23 = Order_Num (WP8 / NM-72). Appended at the END of
          // the `attributes` SELECT so the existing ordinals (0-22) are
          // unshifted. The rowset SQL COALESCEs to the attribute's
          // creation `Id` when the source estate lacks the column, so
          // this is non-null on a live extraction.
          Order          = readIntOpt r 23 }

    let private mapReferenceRow (r: RowAtRest) : OssysReferenceRow =
        { AttrId          = readInt r 0
          RefEntityId     = readIntOpt r 1
          RefEntityName   = readStringOpt r 2
          RefPhysicalName = readStringOpt r 3 }

    let private mapPhysicalTableRow (r: RowAtRest) : OssysPhysicalTableRow =
        { EntityId   = readInt r 0
          SchemaName = readString r 1
          TableName  = readString r 2
          ObjectId   = readInt r 3 }

    // Slice 5.13.ossys-rowsets-cluster: mappers for rowsets 5–18 (less
    // the V1-SUNSET JSON-aggregation helpers).  Ordinal layout mirrors
    // V1's SELECT-projection ordering in `outsystems_metadata_rowsets.sql`
    // (see line numbers cited in each mapper's docstring).

    let private mapColumnRealityRow (r: RowAtRest) : OssysColumnRealityRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1025-1040
        { AttrId                = readInt        r 0
          IsNullable            = readBool       r 1
          SqlType               = readStringOpt  r 2
          MaxLength             = readIntOpt     r 3
          Precision             = readIntOpt     r 4
          Scale                 = readIntOpt     r 5
          CollationName         = readStringOpt  r 6
          IsIdentity            = readBool       r 7
          IsComputed            = readBool       r 8
          ComputedDefinition    = readStringOpt  r 9
          DefaultConstraintName = readStringOpt  r 10
          DefaultDefinition     = readStringOpt  r 11
          PhysicalColumn        = readStringOpt  r 12
          // DECISIONS 2026-07-18 (#669 EF-21) — appended column 13. The
          // SQL and this reader ship together (one resource, one
          // assembly); the result-set contract check guards drift.
          IsPersisted           = readBool       r 13 }

    let private mapColumnCheckRow (r: RowAtRest) : OssysColumnCheckRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1042-1048
        { AttrId         = readInt       r 0
          ConstraintName = readString    r 1
          Definition     = readStringOpt r 2
          IsNotTrusted   = readBool      r 3 }

    let private mapPhysColsPresentRow (r: RowAtRest) : OssysPhysColsPresentRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1056-1059
        { AttrId = readInt r 0 }

    let private mapAllIdxRow (r: RowAtRest) : OssysAllIdxRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1061-1082
        { EntityId             = readInt        r 0
          ObjectId             = readInt        r 1
          IndexId              = readInt        r 2
          IndexName            = readString     r 3
          IsUnique             = readBool       r 4
          IsPrimary            = readBool       r 5
          Kind                 = readString     r 6
          FilterDefinition     = readStringOpt  r 7
          IsDisabled           = readBool       r 8
          IsPadded             = readBool       r 9
          FillFactor           = readInt        r 10
          IgnoreDupKey         = readBool       r 11
          AllowRowLocks        = readBool       r 12
          AllowPageLocks       = readBool       r 13
          NoRecompute          = readBool       r 14
          DataSpaceName        = readStringOpt  r 15
          DataSpaceType        = readStringOpt  r 16
          PartitionColumnsJson = readStringOpt  r 17
          DataCompressionJson  = readStringOpt  r 18 }

    let private mapIdxColMappedRow (r: RowAtRest) : OssysIdxColMappedRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1084-1092
        { EntityId       = readInt       r 0
          IndexName      = readString    r 1
          Ordinal        = readInt       r 2
          PhysicalColumn = readStringOpt r 3
          IsIncluded     = readBool      r 4
          Direction      = readStringOpt r 5
          HumanAttr      = readStringOpt r 6 }

    let private mapFkRealityRow (r: RowAtRest) : OssysFkRealityRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1095-1106
        { EntityId           = readInt        r 0
          FkObjectId         = readInt        r 1
          FkName             = readString     r 2
          DeleteAction       = readStringOpt  r 3
          UpdateAction       = readStringOpt  r 4
          ReferencedObjectId = readInt        r 5
          ReferencedEntityId = readIntOpt     r 6
          ReferencedSchema   = readStringOpt  r 7
          ReferencedTable    = readStringOpt  r 8
          IsNoCheck          = readBool       r 9 }

    let private mapFkColumnRow (r: RowAtRest) : OssysFkColumnRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1109-1119
        { EntityId           = readInt        r 0
          FkObjectId         = readInt        r 1
          Ordinal            = readInt        r 2
          ParentColumn       = readString     r 3
          ReferencedColumn   = readString     r 4
          ParentAttrId       = readIntOpt     r 5
          ParentAttrName     = readStringOpt  r 6
          ReferencedAttrId   = readIntOpt     r 7
          ReferencedAttrName = readStringOpt  r 8 }

    let private mapTriggerRow (r: RowAtRest) : OssysTriggerRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1146-1151
        { EntityId          = readInt       r 0
          TriggerName       = readString    r 1
          IsDisabled        = readBool      r 2
          TriggerDefinition = readStringOpt r 3 }

    /// Rowset 24 (`sys.sequences`) — ordinals mirror the SELECT:
    /// SchemaName, SequenceName, DataType, StartValue, Increment,
    /// MinimumValue, MaximumValue, IsCycling, IsCached, CacheSize.
    let private mapSequenceRow (r: RowAtRest) : OssysSequenceRow =
        { Schema       = readString     r 0
          Name         = readString     r 1
          DataType     = readString     r 2
          StartValue   = readDecimalOpt r 3
          Increment    = readDecimalOpt r 4
          MinimumValue = readDecimalOpt r 5
          MaximumValue = readDecimalOpt r 6
          IsCycling    = readBool       r 7
          IsCached     = readBool       r 8
          CacheSize    = readIntOpt     r 9 }

    /// Rowset 25 (system-versioning) — ordinals mirror the SELECT:
    /// EntityId, HistorySchema, HistoryTable, PeriodStartColumn,
    /// PeriodEndColumn, RetentionValue, RetentionUnit.
    let private mapTemporalRow (r: RowAtRest) : OssysTemporalRow =
        { EntityId       = readInt       r 0
          HistorySchema  = readStringOpt r 1
          HistoryTable   = readStringOpt r 2
          PeriodStart    = readStringOpt r 3
          PeriodEnd      = readStringOpt r 4
          RetentionValue = readIntOpt    r 5
          RetentionUnit  = readStringOpt r 6 }

    let private mapCapabilityRow (r: RowAtRest) : OssysCapabilityRow =
        { HasDataType           = readBool r 0
          HasType               = readBool r 1
          HasPrecision          = readBool r 2
          HasScale              = readBool r 3
          HasDecimals           = readBool r 4
          HasOriginalName       = readBool r 5
          HasExternalColumnType = readBool r 6
          HasPhysicalColumnName = readBool r 7
          HasDatabaseName       = readBool r 8
          HasIsIdentifier       = readBool r 9
          HasRefEntityId        = readBool r 10
          HasIsAutoNumber       = readBool r 11
          HasDefaultValue       = readBool r 12
          HasDeleteRule         = readBool r 13
          HasOriginalType       = readBool r 14
          HasAttrSsKey          = readBool r 15
          HasLength             = readBool r 16
          HasOrderNum           = readBool r 17
          HasEntityDescription  = readBool r 18 }

    /// align-II.7 (E5; audit a1) — what happens to one wire rowset: V2
    /// parses it into a named `MetadataSnapshot` field, or drains it
    /// unread for a named reason. The parsed/drained split is a VALUE,
    /// never a comment.
    [<RequireQualifiedAccess>]
    type RowsetDisposition =
        /// Parsed into the named `MetadataSnapshot` field.
        | Parsed of target: string
        /// Advanced past unread, for the named reason.
        | Drained of reason: string

    /// One wire rowset the carbon-copied script emits: its zero-based
    /// wire ordinal, the walk/progress name, the leading column its
    /// SELECT projects (the cheap drift tripwire — a reordered or
    /// reshaped SELECT trips it BEFORE a mapper misreads shifted
    /// ordinals into wrong-but-parseable values), and its disposition.
    type RowsetContractEntry =
        { Ordinal       : int
          Name          : string
          LeadingColumn : string
          Disposition   : RowsetDisposition }

    /// align-II.7 — THE ROWSET CONTRACT: every result set the carbon-copied
    /// OSSYS rowsets script emits, in wire order. The walk derives from
    /// this table (`ExpectedResultSets` is its length; the progress labels
    /// are its names; each read/skip asserts its ordinal + leading
    /// column), so the count, the order, the names, and the parsed/drained
    /// split live in ONE place — the count-comments this table replaced
    /// had drifted twice (a stale "17 skipped", a stale "rowset 26" for a
    /// zero-based 25). Truth is the canary (R6 split-brain): the live
    /// walk verifies every row of this table on each extraction, and the
    /// append-only evolution law (new rowsets join at the END) is the
    /// same law the progress canary pins.
    [<RequireQualifiedAccess>]
    module RowsetContract =

        /// The V1-SUNSET drain reason — the ten JSON-aggregation helpers
        /// V2 never consumes (the typed rowsets carry the same facts;
        /// the session-context flag opts out of building them server-side).
        let private v1SunsetJson = "V1-SUNSET JSON aggregation; the typed rowsets carry the same facts"

        let all : RowsetContractEntry list =
            [ { Ordinal = 0;  Name = "modules";         LeadingColumn = "EspaceId";    Disposition = RowsetDisposition.Parsed "Modules" }
              { Ordinal = 1;  Name = "entities";        LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "Entities" }
              { Ordinal = 2;  Name = "attributes";      LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Parsed "Attributes" }
              { Ordinal = 3;  Name = "references";      LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Parsed "References" }
              { Ordinal = 4;  Name = "physicalTables";  LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "PhysicalTables" }
              { Ordinal = 5;  Name = "columnReality";   LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Parsed "ColumnReality" }
              { Ordinal = 6;  Name = "columnChecks";    LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Parsed "ColumnChecks" }
              { Ordinal = 7;  Name = "attrCheckJson";   LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 8;  Name = "physColsPresent"; LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Parsed "PhysColsPresent" }
              { Ordinal = 9;  Name = "allIdx";          LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "Indexes" }
              { Ordinal = 10; Name = "idxColsMapped";   LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "IndexColumns" }
              { Ordinal = 11; Name = "fkReality";       LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "ForeignKeysReality" }
              { Ordinal = 12; Name = "fkColumns";       LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "ForeignKeyColumns" }
              { Ordinal = 13; Name = "fkAttrMap";       LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 14; Name = "attrHasFK";       LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 15; Name = "fkColumnsJson";   LeadingColumn = "FkObjectId";  Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 16; Name = "fkAttrJson";      LeadingColumn = "AttrId";      Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 17; Name = "triggers";        LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "Triggers" }
              { Ordinal = 18; Name = "attrJson";        LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 19; Name = "relJson";         LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 20; Name = "idxJson";         LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 21; Name = "triggerJson";     LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 22; Name = "moduleJson";      LeadingColumn = "module.name"; Disposition = RowsetDisposition.Drained v1SunsetJson }
              { Ordinal = 23; Name = "sequences";       LeadingColumn = "SchemaName";  Disposition = RowsetDisposition.Parsed "Sequences" }
              { Ordinal = 24; Name = "temporal";        LeadingColumn = "EntityId";    Disposition = RowsetDisposition.Parsed "Temporal" }
              { Ordinal = 25; Name = "capabilities";    LeadingColumn = "HasDataType"; Disposition = RowsetDisposition.Parsed "Capabilities" } ]

        /// The contract row for a walk name — the walk cites entries by
        /// name; an unknown name is an impossible-state guard (the walk
        /// and the table ship in one file, and the live canary exercises
        /// every citation).
        let byName (name: string) : RowsetContractEntry =
            match all |> List.tryFind (fun e -> e.Name = name) with
            | Some e -> e
            | None -> invalidArg (nameof name) (sprintf "'%s' is not a rowset the contract names" name)

    /// The number of result sets the script emits — the length of the
    /// rowset contract (align-II.7: the count lives on the table alone;
    /// the post-loop assertion surfaces a dropped rowset as
    /// `ResultSetMissing`, and each read/skip asserts its own position +
    /// leading column as `RowsetContractDrift`). Matrix row 35.
    let ExpectedResultSets = List.length RowsetContract.all

    /// Execute the carbon-copied rowsets SQL against `cnn` (already open)
    /// with the supplied parameters + options. Walks all
    /// `ExpectedResultSets` result sets; parses every V2-consumed rowset
    /// into typed records and skips the V1-SUNSET JSON-aggregation tail.
    /// Returns the full `MetadataSnapshot`.
    ///
    /// **Determinism.** The SQL script is deterministic by construction
    /// (V1's pillar 1 / T1 commitment); parameter inputs + database state
    /// fully determine the output. Caller is responsible for fixing the
    /// database state (e.g., applying `readEdgeCaseSeed()` first) when
    /// determinism across runs matters.
    ///
    /// Full-explicit entry point — takes a typed `RunOptions` record
    /// carrying both progress observation (matrix row 36) and command
    /// timeout (matrix row 33). The `runAsync` convenience uses
    /// `defaultOptions`; new axes extend `RunOptions` rather than
    /// spawning new wrapper functions per the sibling-wrapper
    /// discipline.
    let runAsyncWithOptions
            (cnn: SqlConnection)
            (parameters: SnapshotParameters)
            (options: RunOptions)
            : Task<Result<MetadataSnapshot>> =
        task {
            use _ = Bench.scope "adapter.osm.extract"
            try
                // This runner ingests the TYPED rowsets and drains the
                // JSON-aggregation rowsets unread (`RowsetContract` — the
                // Drained entries) — opt out of BUILDING them server-side.
                // The flag rides SESSION context (not a command parameter)
                // so the script stays byte-identical with the V1 donor and
                // a context-less caller gets the historical full build;
                // every rowset is still returned in order with its columns
                // (the drained ones empty), so `ExpectedResultSets` and the
                // reader walk are untouched. Pool-reset clears session
                // context, so the flag never leaks to another logical
                // connection.
                use flagCommand = new SqlCommand("EXEC sp_set_session_context @key = N'OsmSkipJsonRowsets', @value = 1;", cnn)
                let! _ = flagCommand.ExecuteNonQueryAsync()
                let script = MetadataExtractionSql.read()
                use command = new SqlCommand(script, cnn)
                command.CommandType <- CommandType.Text
                // Matrix row 33: caller-tunable command timeout. None
                // preserves the canary's unlimited-timeout semantics
                // (V1's SET TEXTSIZE -1 + complex queries can run long);
                // Some n sets ADO.NET's per-command timeout to n seconds
                // (V1-style; operator-tunable via production CLI's
                // `--command-timeout-seconds` flag when that ships).
                command.CommandTimeout <-
                    match options.CommandTimeoutSeconds with
                    | Some n -> n
                    | None   -> 0
                let moduleCsv =
                    parameters.ModuleNames |> String.concat ","
                command.Parameters.AddWithValue("@ModuleNamesCsv", box moduleCsv)
                |> ignore
                command.Parameters.AddWithValue("@IncludeSystem", box parameters.IncludeSystem)
                |> ignore
                command.Parameters.AddWithValue("@IncludeInactive", box parameters.IncludeInactive)
                |> ignore
                command.Parameters.AddWithValue("@OnlyActiveAttributes", box parameters.OnlyActiveAttributes)
                |> ignore
                let entityFilterParam = SqlParameter("@EntityFilterJson", SqlDbType.NVarChar, -1)
                entityFilterParam.Value <-
                    match parameters.EntityFilterJson with
                    | Some j -> j :> obj
                    | None -> System.DBNull.Value :> obj
                command.Parameters.Add(entityFilterParam) |> ignore

                // Polly retry wraps `ExecuteReaderAsync` at the
                // command-execute boundary — the dominant transient
                // surface (matrix row 34 cash-out; cutover-critical per
                // V2_DRIVER + R6 split-brain governance). V1 had no
                // retry; V2 owns the policy structurally inside the
                // adapter so dual-track canary tolerates transient
                // cloud-OSSYS failures without false-positive divergence.
                use! reader =
                    Retry.runOnPipeline Retry.defaultPipeline (fun _ct ->
                        command.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                // The V1 script also emits diagnostic prints; result sets
                // are emitted in fixed order starting with the validation
                // sanity-check first. We rely on the script's documented
                // contract: the 22 user-visible result sets begin at the
                // SELECT statements at the script's tail (rowsets 0..21).
                //
                // Note: V1's `SqlClientOutsystemsMetadataReader` skips
                // PRINT messages by enumerating all readers regardless of
                // the row shape; we mirror that by reading every result
                // set sequentially.
                // Track observed result-set count for the post-loop
                // contract check (matrix row 35). The reader opens on
                // result set 0 already-positioned; every successful
                // NextResultAsync advances + increments.
                let mutable observedResultSets = 1
                let advanceNext () =
                    task {
                        let! advanced = reader.NextResultAsync()
                        if advanced then
                            observedResultSets <- observedResultSets + 1
                        return advanced
                    }
                let report (name: string) (rowCount: int) : unit =
                    // ResultSetIndex is the zero-based position at the
                    // time of reporting (observedResultSets - 1 because
                    // the counter has already incremented past this set).
                    options.OnRowsetComplete {
                        ResultSetIndex = observedResultSets - 1
                        ResultSetName  = name
                        RowCount       = rowCount
                    }
                // Reader opens already-positioned at rowset 0; subsequent
                // reads advance via `read`. Slice 5.13.ossys-rowsets-cluster
                // lifts the per-rowset advance + read + report triplet
                // into a closure helper that keeps the per-rowset
                // surface to ~1 line each.
                //
                // align-II.7 — the walk verifies THE ROWSET CONTRACT per
                // rowset: `read`/`skip` cite their contract row by name,
                // and `expect` asserts the wire position and the leading
                // column before any row is parsed — drift trips HERE,
                // located, never as a mapper misreading shifted ordinals.
                let expect (entry: RowsetContractEntry) : unit =
                    let position = observedResultSets - 1
                    if position <> entry.Ordinal then
                        raise (RowsetContractException (entry.Name,
                                sprintf "expected at wire ordinal %d, observed at %d" entry.Ordinal position))
                    if reader.FieldCount > 0 then
                        let observed = reader.GetName 0
                        if observed <> entry.LeadingColumn then
                            raise (RowsetContractException (entry.Name,
                                    sprintf "leads with column '%s'; the contract pins '%s'" observed entry.LeadingColumn))
                let read (name: string) (mapper: RowAtRest -> 'T) : Task<'T list> =
                    task {
                        use _ = Bench.scope "adapter.osm.extract.rowset"
                        use _ = Bench.scope (sprintf "adapter.osm.extract.rowset.%s" name)
                        let entry = RowsetContract.byName name
                        let! _ = advanceNext ()
                        expect entry
                        let! rows = readResultSet name reader mapper
                        report name rows.Length
                        return rows
                    }
                let skip (name: string) : Task<unit> =
                    task {
                        use _ = Bench.scope "adapter.osm.extract.rowset"
                        use _ = Bench.scope (sprintf "adapter.osm.extract.rowset.%s" name)
                        let entry = RowsetContract.byName name
                        let! _ = advanceNext ()
                        expect entry
                        do! skipResultSet reader
                        report name 0
                    }

                // The contract's first row — modules (no advance; the
                // reader opens already-positioned on it).
                let! modules =
                    task {
                        use _ = Bench.scope "adapter.osm.extract.rowset"
                        use _ = Bench.scope "adapter.osm.extract.rowset.modules"
                        expect (RowsetContract.byName "modules")
                        let! rows = readResultSet "modules" reader mapModuleRow
                        report "modules" rows.Length
                        return rows
                    }

                // The identity spine — the first parsed run of the contract.
                let! entities       = read "entities"       mapEntityRow
                let! attributes     = read "attributes"     mapAttributeRow
                let! references     = read "references"     mapReferenceRow
                let! physicalTables = read "physicalTables" mapPhysicalTableRow

                // The physical-reflection run (slice
                // 5.13.ossys-rowsets-cluster), Drained entries skipped
                // with the same report-shape so the progress count stays
                // accurate — which is which lives on `RowsetContract`.
                let! columnReality   = read "columnReality"   mapColumnRealityRow
                let! columnChecks    = read "columnChecks"    mapColumnCheckRow
                do! skip "attrCheckJson"
                let! physColsPresent = read "physColsPresent" mapPhysColsPresentRow
                let! indexes         = read "allIdx"          mapAllIdxRow
                let! indexColumns    = read "idxColsMapped"   mapIdxColMappedRow
                let! fkReality       = read "fkReality"       mapFkRealityRow
                let! fkColumns       = read "fkColumns"       mapFkColumnRow
                do! skip "fkAttrMap"
                do! skip "attrHasFK"
                do! skip "fkColumnsJson"
                do! skip "fkAttrJson"

                // Triggers (matrix row 23).
                let! triggers = read "triggers" mapTriggerRow

                // The V1-SUNSET JSON aggregation tail — all Drained.
                do! skip "attrJson"
                do! skip "relJson"
                do! skip "idxJson"
                do! skip "triggerJson"
                do! skip "moduleJson"

                // Sequences + temporal configuration (the extraction
                // fork; DECISIONS 2026-07-18; #669 EF-22 + EF-23).
                // Appended at the script's end.
                let! sequences = read "sequences" mapSequenceRow
                let! temporal  = read "temporal"  mapTemporalRow

                // The capability vector, the contract's last row (the
                // data-sink chapter, S2 2026-08-15): the @Has* COL_LENGTH
                // probes, finally returned. Sink-plane evidence;
                // `toBundle` ignores it by design.
                let! capabilities = read "capabilities" mapCapabilityRow

                // Drain any trailing rowsets the SQL might emit beyond
                // the contract. Per matrix row 35: a SQL-contract
                // drift adds rowsets here; the contract check below
                // surfaces the structural drift before the IR-build
                // path silently absorbs it. Today this loop should be
                // a no-op (advanceNext returns false on its first call).
                let mutable hasMore = true
                let mutable trailingIdx = 0
                while hasMore do
                    let! advanced = advanceNext ()
                    if not advanced then hasMore <- false
                    else
                        do! skipResultSet reader
                        report (sprintf "trailing-%d" trailingIdx) 0
                        trailingIdx <- trailingIdx + 1

                // Contract check (matrix row 35): assert observed
                // rowset count matches `ExpectedResultSets`. The
                // explicit-read shape above already enumerates every
                // documented rowset; this check is the structural
                // forcing function that catches future drift.
                let contractCheck =
                    MetadataExtractionError.resultSetContractCheck
                        ExpectedResultSets
                        observedResultSets
                match contractCheck with
                | Error errors -> return Result.failure errors
                | Ok () ->
                    return Result.success {
                        Modules            = modules
                        Entities           = entities
                        Attributes         = attributes
                        References         = references
                        PhysicalTables     = physicalTables
                        ColumnReality      = columnReality
                        ColumnChecks       = columnChecks
                        PhysColsPresent    = physColsPresent
                        Indexes            = indexes
                        IndexColumns       = indexColumns
                        ForeignKeysReality = fkReality
                        ForeignKeyColumns  = fkColumns
                        Triggers           = triggers
                        Sequences          = sequences
                        Temporal           = temporal
                        Capabilities       = capabilities
                        Scope              = AcquisitionScope.ofParameters parameters
                    }
            with
            | ex ->
                // Matrix row 32 — typed exception classification at the
                // adapter boundary. The closed-DU `MetadataExtractionError`
                // distinguishes operator-actionable failure modes
                // (row-mapping vs transient SQL vs other) so consumers
                // can route by `ValidationError.Code` without parsing
                // message text. V1's three-exception-class catch lifts to
                // the same typed surface — structurally stronger.
                let classified =
                    MetadataExtractionError.classify Retry.isTransientSqlError ex
                return Result.failureOf (
                    MetadataExtractionError.toValidationError classified)
        }

    /// Zero-default entry point — uses `defaultOptions` (no progress
    /// observation, unlimited command timeout). Canonical canary-
    /// extraction surface; production CLI surfaces compose
    /// `runAsyncWithOptions` directly with `{ defaultOptions with ... }`
    /// overriding axes as needed.
    let runAsync (cnn: SqlConnection) (parameters: SnapshotParameters)
            : Task<Result<MetadataSnapshot>> =
        runAsyncWithOptions cnn parameters defaultOptions

    /// Compose the typed snapshot into V2's `OssysRowsetTypes.RowsetBundle`.
    /// JOIN logic:
    ///   - Each `OssysEntityRow` produces one `KindRow` joined against the
    ///     `OssysPhysicalTableRow` by EntityId for the `DbSchema` value.
    ///     When the physical-table row is absent, defaults to `"dbo"`.
    ///   - Each `OssysAttributeRow` produces one `AttributeRow`.
    ///   - Each `OssysReferenceRow` produces one `ReferenceRow` with the
    ///     DeleteRule lifted from the joined Attribute (V1 carries it on
    ///     the attribute; V2's `ReferenceRow` carries it on the reference).
    ///     `HasDbConstraint` is `true` exactly when the attribute's FK is
    ///     reflected in `#FkReality` (the `sys.foreign_keys` projection);
    ///     a reference with no reflected FK is logical-only and carries
    ///     `false` (JSON-path `ISNULL(HasFK, 0)` parity — WP-1a).

    /// Parse `#AllIdx.DataCompressionJson` into a single-value
    /// compression code when the JSON encodes uniform compression
    /// across every partition. The JSON shape per the extraction
    /// script's rowset-9 projection is
    /// `[{"partition":1,"compression":"PAGE"}, …]` — one entry per
    /// partition (`data_compression_desc AS [compression]`). The
    /// original wiring read a property named `Code` that the script
    /// never emitted, so every parse returned `None` and the whole
    /// compression chain (bundle → reader → emitter) sat dark —
    /// the audit observed exactly that as EF-25 (DATA_COMPRESSION
    /// dropped). `Code` stays as a fallback for vintage captures.
    /// Returns `Some "<code>"` when every entry matches; `None`
    /// when heterogeneous or unparseable (row 56 partition axis
    /// residual). Pillar 7 four-question analysis: the typed
    /// AST library is `System.Text.Json` (JsonDocument is the BCL
    /// canonical parser); no LINT-ALLOW needed because the parsing
    /// is a structured walk, not string composition.
    let private tryParseUniformDataCompression (json: string) : string option =
        try
            use doc = System.Text.Json.JsonDocument.Parse(json)
            if doc.RootElement.ValueKind <> System.Text.Json.JsonValueKind.Array then
                None
            else
                let codes =
                    seq {
                        for entry in doc.RootElement.EnumerateArray() do
                            let mutable codeEl = Unchecked.defaultof<System.Text.Json.JsonElement>
                            if entry.TryGetProperty("compression", &codeEl)
                               || entry.TryGetProperty("Code", &codeEl) then
                                match Option.ofObj (codeEl.GetString()) with
                                | Some s -> yield s
                                | None   -> ()
                    }
                    |> Seq.toList
                match codes |> List.distinct with
                | [ single ] -> Some single
                | _          -> None
        with :? System.Text.Json.JsonException -> None   // malformed JSON → None; a fatal propagates

    /// Slice A.4.7'-prelude.row56-dataspace (LR7 closure): parse
    /// V1's `#AllIdx.PartitionColumnsJson` into a `string list`.
    /// JSON shape per V1's SQL is `[{"ordinal":1,"name":"PartitionKey"}, …]`
    /// — one entry per partition column. Returns the column names
    /// in ordinal order (V1's SQL already sorts by `partition_ordinal`).
    /// `None` when the JSON is malformed; `Some []` when the JSON
    /// is an empty array (legal for filegroup-backed indexes).
    let private tryParsePartitionColumns (json: string) : string list option =
        try
            use doc = System.Text.Json.JsonDocument.Parse(json)
            if doc.RootElement.ValueKind <> System.Text.Json.JsonValueKind.Array then
                None
            else
                let names =
                    seq {
                        for entry in doc.RootElement.EnumerateArray() do
                            let mutable nameEl = Unchecked.defaultof<System.Text.Json.JsonElement>
                            if entry.TryGetProperty("name", &nameEl) then
                                match Option.ofObj (nameEl.GetString()) with
                                | Some s when not (System.String.IsNullOrWhiteSpace s) -> yield s
                                | _ -> ()
                    }
                    |> Seq.toList
                Some names
        with :? System.Text.Json.JsonException -> None   // malformed JSON → None; a fatal propagates

    /// Slice A.4.7'-prelude.row56-dataspace (LR7 closure): project
    /// V1's `#AllIdx.DataSpaceName` + `DataSpaceType` (+
    /// `PartitionColumnsJson` for partition schemes) into V2's
    /// closed-DU `DataSpace option`. Returns `None` for the default
    /// case (no dataspace name OR unrecognized type) — V2 omits
    /// the `ON` clause in that case rather than emitting an
    /// unsupported shape. V1's `type_desc` values are
    /// `'ROWS_FILEGROUP'` (mapping to Filegroup) and
    /// `'PARTITION_SCHEME'` (mapping to PartitionScheme).
    let private tryProjectDataSpace
        (name: string option)
        (typeDesc: string option)
        (partitionColumnsJson: string option)
        : DataSpace option =
        match name, typeDesc with
        | Some n, Some t when not (System.String.IsNullOrWhiteSpace n) ->
            match t.ToUpperInvariant() with
            | "ROWS_FILEGROUP" ->
                // Default-filegroup suppression: `[PRIMARY]` placement is
                // SQL Server's default, not an intentional choice — lifting
                // it would restate `ON [PRIMARY]` in emitted DDL. Only a
                // NON-primary filegroup is intentional physical
                // configuration; partition schemes always carry.
                if System.String.Equals(n, "PRIMARY", System.StringComparison.OrdinalIgnoreCase)
                then None
                else Some (DataSpace.Filegroup n)
            | "PARTITION_SCHEME" ->
                let cols =
                    partitionColumnsJson
                    |> Option.bind tryParsePartitionColumns
                    |> Option.defaultValue []
                Some (DataSpace.PartitionScheme (n, cols))
            | _ -> None
        | _ -> None

    /// The `#FkReality` row reflected for each reference attribute, keyed by
    /// the reference's source `AttrId`. Encapsulates the JOIN chain
    /// `OssysReferenceRow.AttrId ↔ OssysFkColumnRow.ParentAttrId ↔
    /// OssysFkColumnRow.FkObjectId ↔ OssysFkRealityRow.FkObjectId` shared by
    /// `toBundle` (presence ⇒ `HasDbConstraint`, plus `UpdateAction` /
    /// `IsNoCheck` / `DeleteAction`) and `deleteRuleDivergences` (compares the
    /// reflected `DeleteAction` against the model rule). One definition site so
    /// the two consumers cannot drift.
    let private fkRealityByParentAttrIdMap (snapshot: MetadataSnapshot) : Map<int, OssysFkRealityRow> =
        let fkRealityById =
            snapshot.ForeignKeysReality
            |> List.map (fun fk -> fk.FkObjectId, fk)
            |> Map.ofList
        snapshot.ForeignKeyColumns
        |> List.choose (fun c ->
            match c.ParentAttrId, Map.tryFind c.FkObjectId fkRealityById with
            | Some pid, Some fk -> Some (pid, fk)
            | _ -> None)
        |> Map.ofList

    /// WP-1b (DECISIONS 2026-07-16) — NAME every disagreement between the
    /// OutSystems model's delete-rule code and the deployed FK's reflected
    /// `#FkReality.DeleteAction`, for physically-backed FKs. Database reality
    /// wins the emitted ON DELETE action (the rowset reader prefers the
    /// reflected action via `OssysTranslation.chooseOnDeleteAction`); this pure
    /// pass tells the operator when the model disagreed, so the override is a
    /// named observation, never silent. Only physically-backed FKs whose
    /// reflected action is representable and differs from the model's are
    /// reported. Deterministic — ordered by attribute id.
    let deleteRuleDivergences (snapshot: MetadataSnapshot) : DiagnosticEntry list =
        let referenceActionText (a: ReferenceAction) =
            match a with
            | ReferenceAction.NoAction -> "NO ACTION"
            | ReferenceAction.Cascade  -> "CASCADE"
            | ReferenceAction.SetNull  -> "SET NULL"
            | ReferenceAction.Restrict -> "RESTRICT"
        let attributeById =
            snapshot.Attributes |> List.map (fun a -> a.AttrId, a) |> Map.ofList
        let fkReality = fkRealityByParentAttrIdMap snapshot
        snapshot.References
        |> List.sortBy (fun r -> r.AttrId)
        |> List.choose (fun r ->
            match Map.tryFind r.AttrId attributeById, Map.tryFind r.AttrId fkReality with
            | Some attr, Some fk ->
                match OssysTranslation.deleteActionDivergence attr.DeleteRule fk.DeleteAction with
                | Some (modelAction, reflectedAction) ->
                    Some
                        { DiagnosticEntry.create
                            "adapter:OSSYS" DiagnosticSeverity.Warning
                            "adapter.ossys.fkReality.deleteActionDivergence"
                            (sprintf "Reference on column %s (attr %d): the OutSystems model's delete rule maps to ON DELETE %s but the deployed FK reflects ON DELETE %s. The engine emits the DEPLOYED (reflected) action; remediate the model or confirm which is authoritative."
                                attr.PhysicalCol r.AttrId
                                (referenceActionText modelAction) (referenceActionText reflectedAction))
                          with Metadata =
                                Map.ofList
                                    [ "attrId", string r.AttrId
                                      "physicalColumn", attr.PhysicalCol
                                      "modelDeleteRule", (attr.DeleteRule |> Option.defaultValue "<none>")
                                      "modelAction", referenceActionText modelAction
                                      "reflectedDeleteAction", (fk.DeleteAction |> Option.defaultValue "<none>")
                                      "reflectedAction", referenceActionText reflectedAction ] }
                | None -> None
            | _ -> None)

    /// F9 (audit 2026-06-17) — NAME the logical-vs-deployed divergences instead
    /// of discarding them. The adapter carries the LOGICAL Service-Studio facets
    /// (`IsMandatory` → nullability, `IsAutoNumber` → identity); the SAME snapshot
    /// fetched the DEPLOYED `#ColumnReality` (`cr.IsNullable`, `cr.IsIdentity`),
    /// which `toBundle` reads for collation/computed/default but never compares
    /// for nullability/identity. The carried value is UNCHANGED (the audit's
    /// "operator call": the operator decides which source is authoritative, the
    /// engine does not auto-resolve). Deterministic — ordered by attribute id.
    ///
    /// Two grains, deliberately different:
    ///   - **identity divergences stay per-column `Warning`s** — each names a
    ///     concrete column whose IDENTITY facet disagrees, individually
    ///     actionable;
    ///   - **nullability divergences aggregate into ONE informational
    ///     summary** (counts per direction + a small sample). Estates commonly
    ///     use logical mandatory semantics over physically nullable columns at
    ///     scale — a per-column warning flood buries the actionable signals
    ///     without adding information. This is a schema-reality observation
    ///     ("does the deployed column allow NULL?"); the separate question
    ///     "does current data contain NULL where the logical model forbids
    ///     it?" is a cutover blocker and stays itemized in the data-fidelity
    ///     diagnostics.
    let columnRealityDivergences (snapshot: MetadataSnapshot) : DiagnosticEntry list =
        let realityByAttrId =
            snapshot.ColumnReality |> List.map (fun cr -> cr.AttrId, cr) |> Map.ofList
        let paired =
            snapshot.Attributes
            |> List.sortBy (fun a -> a.AttrId)
            |> List.choose (fun a ->
                Map.tryFind a.AttrId realityByAttrId |> Option.map (fun cr -> a, cr))
        let identityDivergences =
            paired
            |> List.choose (fun (a, cr) ->
                if a.IsAutoNumber <> cr.IsIdentity then
                    Some
                        { DiagnosticEntry.create
                            "adapter:OSSYS" DiagnosticSeverity.Warning
                            "adapter.ossys.columnReality.identityDivergence"
                            (sprintf "Column %s (attr %d): the logical OSSYS model declares identity=%b but the deployed schema has identity=%b. The engine carries the LOGICAL value."
                                a.PhysicalCol a.AttrId a.IsAutoNumber cr.IsIdentity)
                          with Metadata =
                                Map.ofList
                                    [ "attrId", string a.AttrId
                                      "physicalColumn", a.PhysicalCol
                                      "logicalIdentity", string a.IsAutoNumber
                                      "deployedIdentity", string cr.IsIdentity ] }
                else None)
        let nullabilityDiverged =
            paired
            |> List.filter (fun (a, cr) -> (not a.IsMandatory) <> cr.IsNullable)
        let nullabilitySummary =
            match nullabilityDiverged with
            | [] -> []
            | diverged ->
                let mandatoryButNullable =
                    diverged |> List.filter (fun (a, cr) -> a.IsMandatory && cr.IsNullable)
                let nullableButNotNull =
                    diverged |> List.filter (fun (a, cr) -> not a.IsMandatory && not cr.IsNullable)
                let sample =
                    diverged
                    |> List.truncate 5
                    |> List.map (fun (a, _) -> a.PhysicalCol)
                let sampleMeta =
                    sample
                    |> List.mapi (fun i col -> sprintf "sample.%d" i, col)
                // The recommendation layer (2026-07-18): the divergence is
                // grounded in the platform's own contract — a mandatory
                // attribute is validated at run time only; database
                // constraints are created for primary keys and reference
                // attributes (OutSystems 11, entity-attribute properties /
                // database constraints) — so mandatory-over-nullable is the
                // platform's deployment shape on every estate, not drift.
                // The emitted NOT NULL is therefore a TIGHTENING, and its
                // data evidence + per-column recommendation live in
                // fidelity.json (backfill at or below the repair band,
                // keepNullable above it).
                let recommendation =
                    "Publish the tightenings with their fidelity evidence: backfill at or below the repair band, keep the column nullable (keepNullable) above it."
                [ { DiagnosticEntry.create
                      "adapter:OSSYS" DiagnosticSeverity.Info
                      "adapter.ossys.columnReality.nullabilityDivergence"
                      (sprintf "%d column(s) diverge on nullability between the logical OSSYS model and the deployed schema (%d logical-mandatory over deployed-nullable, %d logical-nullable over deployed NOT NULL; e.g. %s). A mandatory attribute is validated by the platform at run time only (database constraints are created for primary keys and references), so logical-mandatory over deployed-nullable is the platform's own deployment shape. A deployed NOT NULL is preserved in the emitted schema (decision 2); a logical-mandatory declaration over a deployed-nullable column emits NOT NULL from the model — a tightening — and the rows that contradict it are itemized in fidelity.json with per-column recommendations (the backfill block, or a keepNullable entry past the repair band)."
                          (List.length diverged)
                          (List.length mandatoryButNullable)
                          (List.length nullableButNotNull)
                          (String.concat ", " sample))
                    with Metadata =
                          Map.ofList
                              ([ "count", string (List.length diverged)
                                 "logicalMandatoryDeployedNullable", string (List.length mandatoryButNullable)
                                 "logicalNullableDeployedNotNull", string (List.length nullableButNotNull)
                                 "recommendation", recommendation ]
                               @ sampleMeta) } ]
        identityDivergences @ nullabilitySummary

    /// WP-4b (DECISIONS 2026-07-16; register C1/C2) — NAME every disagreement
    /// between an ordinary scalar's logical OSSYS type mapping and the deployed
    /// `#ColumnReality` storage. The engine's posture is stated per column: a
    /// SAME-category deployed refinement wins the emitted storage (V1's on-disk
    /// precedence, `resolveAttributeType`); the forced-runtime-mapping family
    /// (`identifier`/`autonumber`/`longinteger`) keeps its deliberate `BIGINT`
    /// (C2 — the INT-vs-BIGINT call is an estate decision); a cross-category
    /// deployed type keeps the logical value (no silent reclassification).
    /// Reference-shaped `bt*` attributes are excluded — their deployed-storage
    /// precedence is a separate, always-on channel. Deterministic — ordered by
    /// attribute id.
    let columnStorageDivergences (snapshot: MetadataSnapshot) : DiagnosticEntry list =
        let realityByAttrId =
            snapshot.ColumnReality |> List.map (fun cr -> cr.AttrId, cr) |> Map.ofList
        snapshot.Attributes
        |> List.sortBy (fun a -> a.AttrId)
        |> List.choose (fun a ->
            match a.DataType, Map.tryFind a.AttrId realityByAttrId with
            | Some rawType, Some cr ->
                let deployedOpt =
                    cr.SqlType
                    |> Option.bind (fun t -> SqlStorageType.ofSqlType t cr.MaxLength cr.Precision cr.Scale)
                let normalized = OssysTranslation.normalizeAttributeType rawType
                let isBt =
                    normalized.StartsWith("bt", System.StringComparison.Ordinal)
                    && normalized.Contains "*"
                match deployedOpt with
                | Some deployed when not isBt ->
                    match OssysTypeMapping.tryParse normalized a.Length a.Precision a.Scale with
                    | Some (logicalPt, logicalStorage) when logicalStorage <> deployed ->
                        let isForced =
                            match normalized with
                            | "identifier" | "autonumber" | "longinteger" -> true
                            | _ -> false
                        let sameCategory = SqlStorageType.toPrimitiveType deployed = logicalPt
                        let emitsDeployed = (not isForced) && sameCategory
                        // The recommendation layer (2026-07-18): the posture
                        // is stated WITH its reason and the move that would
                        // change it, so the model-vs-deployed authority
                        // choice is explicit, never implicit in a bare
                        // "emits X". Deployed-authoritative is the standing
                        // default before cutover: live data already occupies
                        // the deployed shape, and a narrowing publish is
                        // blocked at deploy while data exceeds the declared
                        // shape (the data-loss gate) — so model-authority is
                        // a tightening RULING taken with profile evidence,
                        // not a drift repair.
                        let recommendation =
                            if emitsDeployed then
                                "Live data already occupies the deployed shape, and a narrowing publish is blocked while the data exceeds the declared shape. Treat the model as authoritative only as a tightening ruling — schedule it with profile evidence — or leave the deployed shape standing."
                            elif isForced then
                                "The BIGINT identifier family is a deliberate estate decision; align the deployed column with it, or amend the decision before publishing."
                            else
                                "A deployed type outside the declared category is never adopted silently; correct the declared type in the model, or align the deployed column — the divergence stands until one side moves."
                        Some
                            { DiagnosticEntry.create
                                "adapter:OSSYS" DiagnosticSeverity.Warning
                                "adapter.ossys.columnReality.storageDivergence"
                                (sprintf "Column %s (attr %d): the logical OSSYS model maps type '%s' to %A but the deployed schema has %A. The engine emits the %s value. %s"
                                    a.PhysicalCol a.AttrId rawType logicalStorage deployed
                                    (if emitsDeployed then "DEPLOYED (on-disk precedence)" else "LOGICAL")
                                    recommendation)
                              with Metadata =
                                    Map.ofList
                                        [ "attrId", string a.AttrId
                                          "physicalColumn", a.PhysicalCol
                                          "logicalType", rawType
                                          "logicalStorage", sprintf "%A" logicalStorage
                                          "deployedStorage", sprintf "%A" deployed
                                          "emits", (if emitsDeployed then "deployed" else "logical")
                                          "recommendation", recommendation ] }
                    | _ -> None
                | _ -> None
            | _ -> None)

    /// NAME the attribute-flag-vs-entity-key primary-key divergences. OSSYS
    /// carries PK identity twice: per-attribute (`Is_Identifier`) and
    /// per-entity (`ossys_Entity.PrimaryKey_SS_Key`). The rowset reader
    /// treats the entity key as a RECOVERY source only (it fires when no
    /// attribute of the entity carries the explicit flag), so a
    /// disagreement — both sources present, naming different attributes —
    /// is never resolved by the engine: the explicit flag wins the carried
    /// value and this pure pass surfaces the contradiction as an
    /// operator-facing `Warning`. Deterministic — ordered by entity id.
    let primaryKeyDivergences (snapshot: MetadataSnapshot) : DiagnosticEntry list =
        let attrsByEntity =
            snapshot.Attributes |> List.groupBy (fun a -> a.EntityId) |> Map.ofList
        snapshot.Entities
        |> List.sortBy (fun e -> e.EntityId)
        |> List.collect (fun e ->
            match e.PrimaryKeySsKey with
            | None -> []
            | Some pkKey ->
                let attrs = Map.tryFind e.EntityId attrsByEntity |> Option.defaultValue []
                let flagged = attrs |> List.filter (fun a -> a.IsIdentifier)
                let flagMatchesEntityKey =
                    flagged |> List.exists (fun a -> a.AttrSsKey = Some pkKey)
                if List.isEmpty flagged || flagMatchesEntityKey then []
                else
                    [ { DiagnosticEntry.create
                          "adapter:OSSYS" DiagnosticSeverity.Warning
                          "adapter.ossys.primaryKey.divergence"
                          (sprintf "Entity %s (id %d): Is_Identifier marks attribute(s) %s but ossys_Entity.PrimaryKey_SS_Key names %O. The engine carries the explicit attribute flag; remediate the source or confirm which is authoritative."
                              e.EntityName e.EntityId
                              (flagged |> List.map (fun a -> a.AttrName) |> String.concat ", ")
                              pkKey)
                        with Metadata =
                              Map.ofList
                                  [ "entityId", string e.EntityId
                                    "entityName", e.EntityName
                                    "flaggedAttributes", (flagged |> List.map (fun a -> a.AttrName) |> String.concat ",")
                                    "primaryKeySsKey", string pkKey ] } ])

    /// align-II.8 (E1; audit a1; A54) — one NAMED erasure of the bundle
    /// projection: what `toBundle` folds, assumes, or drops, as a value.
    /// The adjunction's modulus at this seam is enumerable — nothing is
    /// lost in silence. Closed; a new fold or assumption in `toBundle`
    /// lands WITH its variant (the A54 constant-modulus law pins the set).
    [<RequireQualifiedAccess>]
    type BundleErasure =
        /// A parsed rowset's axes fold into a narrower bundle carrier (or
        /// none): the rowset (its `RowsetContract` name) and what the fold
        /// keeps or loses. Constant — these folds are the projection's
        /// design, present on every bundle.
        | FoldedRowset of rowset: string * detail: string
        /// A reference row dropped at the join — its target entity name or
        /// its owning attribute did not resolve, so no `ReferenceRow`
        /// carries it (data-dependent; the dropped row's attribute id
        /// locates it).
        | UnjoinedReference of attrId: int
        /// An entity with no physical-table row — its schema is assumed
        /// `dbo` (data-dependent).
        | AssumedSchema of entityId: int * entityName: string
        /// An attribute with no declared data type — assumed `Text`
        /// (data-dependent).
        | AssumedDataType of attrId: int * attrName: string
        /// The capability vector informs the sink plane only — the bundle
        /// is capability-invariant BY DESIGN (the data-sink chapter, S2),
        /// stated as a value rather than a comment.
        | CapabilityInvariant

    [<RequireQualifiedAccess>]
    module BundleErasure =

        /// Stable routing code per case (`<domain>.<subject>.<problem>`).
        let code (e: BundleErasure) : string =
            match e with
            | BundleErasure.FoldedRowset _       -> "adapter.ossys.bundleErasure.foldedRowset"
            | BundleErasure.UnjoinedReference _  -> "adapter.ossys.bundleErasure.unjoinedReference"
            | BundleErasure.AssumedSchema _      -> "adapter.ossys.bundleErasure.assumedSchema"
            | BundleErasure.AssumedDataType _    -> "adapter.ossys.bundleErasure.assumedDataType"
            | BundleErasure.CapabilityInvariant  -> "adapter.ossys.bundleErasure.capabilityInvariant"

        /// The located sentence (one mint; the notice rollup and the tests
        /// read the same copy).
        let describe (e: BundleErasure) : string =
            match e with
            | BundleErasure.FoldedRowset (rowset, detail) ->
                sprintf "the '%s' rowset folds at the bundle projection: %s." rowset detail
            | BundleErasure.UnjoinedReference attrId ->
                sprintf "the reference on attribute id %d did not join (its target entity name or owning attribute is unresolved) — no reference row carries it." attrId
            | BundleErasure.AssumedSchema (entityId, entityName) ->
                sprintf "entity %s (id %d) has no physical-table row — its schema is assumed 'dbo'." entityName entityId
            | BundleErasure.AssumedDataType (attrId, attrName) ->
                sprintf "attribute %s (id %d) declares no data type — assumed 'Text'." attrName attrId
            | BundleErasure.CapabilityInvariant ->
                "the capability vector informs the sink plane only — the bundle is capability-invariant by design."

        /// The notice-rollup projection: the constant by-design folds ride
        /// as Info (recorded on the detail artifact, never a warning wall);
        /// the data-dependent assumptions and drops warn (a shape surprise
        /// the operator should see).
        let toDiagnostic (e: BundleErasure) : DiagnosticEntry =
            let severity =
                match e with
                | BundleErasure.FoldedRowset _
                | BundleErasure.CapabilityInvariant -> DiagnosticSeverity.Info
                | BundleErasure.UnjoinedReference _
                | BundleErasure.AssumedSchema _
                | BundleErasure.AssumedDataType _ -> DiagnosticSeverity.Warning
            DiagnosticEntry.create "adapter:OSSYS" severity (code e) (describe e)

    /// The constant modulus — the folds every bundle projection performs
    /// by design, independent of the data (A54's enumerable half; the
    /// data-dependent erasures join per shape).
    let private constantErasures : BundleErasure list =
        [ BundleErasure.FoldedRowset ("physColsPresent",
            "the presence set is witnessed and persisted raw at the sink; no bundle field carries it")
          BundleErasure.FoldedRowset ("fkReality",
            "per-FK reflection folds to four per-reference scalars (HasDbConstraint, OnUpdate, ReflectedOnDelete, IsConstraintTrusted)")
          BundleErasure.FoldedRowset ("columnReality",
            "deployed reflection folds onto per-attribute facets (computed, default name, collation, storage, nullability, persisted)")
          BundleErasure.FoldedRowset ("entities",
            "Data_Kind's string domain folds to IsStatic (staticEntity alone reads true)")
          BundleErasure.CapabilityInvariant ]

    let toBundle (snapshot: MetadataSnapshot) : OssysRowsetTypes.RowsetBundle * BundleErasure list =
        use _ = Bench.scope "adapter.osm.extract.toBundle"
        let physicalByEntity =
            snapshot.PhysicalTables
            |> List.map (fun pt -> pt.EntityId, pt)
            |> Map.ofList
        let attributeById =
            snapshot.Attributes
            |> List.map (fun a -> a.AttrId, a)
            |> Map.ofList

        let modules =
            snapshot.Modules
            |> List.map (fun m ->
                {
                    EspaceId       = m.EspaceId
                    EspaceName     = m.EspaceName
                    IsSystemModule = m.IsSystemModule
                    IsActive       = m.IsActive
                    EspaceKind     = m.EspaceKind
                    EspaceSsKey    = m.EspaceSsKey
                } : OssysRowsetTypes.ModuleRow)

        let kinds =
            snapshot.Entities
            |> List.map (fun e ->
                let dbSchema =
                    match Map.tryFind e.EntityId physicalByEntity with
                    | Some pt -> pt.SchemaName
                    | None -> "dbo"
                {
                    EntityId          = e.EntityId
                    EspaceId          = e.EspaceId
                    EntityName        = e.EntityName
                    PhysicalTableName = e.PhysicalTableName
                    DbSchema          = dbSchema
                    // V1 classifies static (lookup) entities via
                    // `ossys_Entity.Data_Kind = 'staticEntity'`; derive
                    // `IsStatic` from it so the rowset path marks
                    // `Modality.Static` (matching the JSON path's
                    // `isStatic` projection). Previously hardcoded false,
                    // which silently dropped static-entity classification.
                    IsStatic          =
                        match e.DataKind with
                        | Some dk ->
                            System.String.Equals(
                                dk, "staticEntity",
                                System.StringComparison.OrdinalIgnoreCase)
                        | None -> false
                    IsExternal        = e.IsExternal
                    IsSystemEntity    = e.IsSystemEntity
                    IsActive          = e.IsActive
                    EntitySsKey       = e.EntitySsKey
                    PrimaryKeySsKey   = e.PrimaryKeySsKey
                    Description       = e.Description
                } : OssysRowsetTypes.KindRow)

        // Slice A.4.7'-prelude.row53-source-side — join
        // `OssysColumnRealityRow` by AttrId so each AttributeRow
        // surfaces V1's deployed-target reflection (IsComputed +
        // ComputedDefinition + DefaultConstraintName). The 3-step
        // JOIN pattern mirrors slice 5.13.fk-reality-join — Maps
        // once at toBundle entry; walk per-attribute; defaults to
        // empty fields when no ColumnReality row exists (attribute
        // never reflected against deployed schema; rowset path's
        // source-side reflection didn't fire).
        let columnRealityByAttrId =
            snapshot.ColumnReality
            |> List.map (fun cr -> cr.AttrId, cr)
            |> Map.ofList

        let attributes =
            snapshot.Attributes
            |> List.map (fun a ->
                let realityIsComputed, realityComputedDef, realityDefaultName, realityCollation =
                    match Map.tryFind a.AttrId columnRealityByAttrId with
                    | Some cr -> cr.IsComputed, cr.ComputedDefinition, cr.DefaultConstraintName, cr.CollationName
                    | None    -> false, None, None, None
                // Deployed storage evidence: `#ColumnReality.SqlType` +
                // facets parsed into the typed channel the resolver
                // consults for reference-shaped `bt*` attributes.
                // `MaxLength` is already character-normalized by the
                // rowsets SQL (nvarchar halved; -1 = MAX).
                let realityStorage =
                    match Map.tryFind a.AttrId columnRealityByAttrId with
                    | Some cr ->
                        cr.SqlType
                        |> Option.bind (fun t ->
                            SqlStorageType.ofSqlType t cr.MaxLength cr.Precision cr.Scale)
                    | None -> None
                {
                    AttrId               = a.AttrId
                    EntityId             = a.EntityId
                    AttrName             = a.AttrName
                    PhysicalCol          = a.PhysicalCol
                    DataType             = match a.DataType with Some s -> s | None -> "Text"
                    DefaultValue         = a.DefaultValue
                    IsMandatory          = a.IsMandatory
                    IsIdentifier         = a.IsIdentifier
                    IsAutoNumber         = a.IsAutoNumber
                    Length               = a.Length
                    Precision            = a.Precision
                    Scale                = a.Scale
                    AttrSsKey            = a.AttrSsKey
                    IsActive             = a.IsActive
                    Description          = a.Description
                    OriginalName         = a.OriginalName
                    ExternalDatabaseType = a.ExternalDbType
                    IsComputed            = realityIsComputed
                    ComputedDefinition    = realityComputedDef
                    DefaultConstraintName = realityDefaultName
                    // WP8 / NM-72 — carry the authored Service-Studio
                    // order through to the rowset reader.
                    Order                 = a.Order
                    // F1 (audit 2026-06-17) — carry the deployed collation.
                    Collation             = realityCollation
                    DeployedStorage       = realityStorage
                    // Decision 2 (DECISIONS 2026-07-18; #669 M-3 / EF-18) —
                    // carry the deployed nullability; the reader preserves
                    // a deployed NOT NULL over a model-optional declaration.
                    DeployedIsNullable    =
                        Map.tryFind a.AttrId columnRealityByAttrId
                        |> Option.map (fun cr -> cr.IsNullable)
                    // #669 EF-21 — carry the PERSISTED marking; the reader
                    // threads it into the computed-column configuration.
                    IsPersisted           =
                        Map.tryFind a.AttrId columnRealityByAttrId
                        |> Option.map (fun cr -> cr.IsPersisted)
                        |> Option.defaultValue false
                } : OssysRowsetTypes.AttributeRow)

        // Slice 5.13.fk-reality-join — JOIN OssysReferenceRow with
        // OssysFkRealityRow via OssysFkColumnRow's parent-attribute
        // pivot. The JOIN key chain is:
        //   OssysReferenceRow.AttrId
        //     ↔ OssysFkColumnRow.ParentAttrId
        //     ↔ OssysFkColumnRow.FkObjectId
        //     ↔ OssysFkRealityRow.FkObjectId
        // For composite FKs, multiple OssysReferenceRow entries map
        // to the same FkObjectId via different ParentAttrIds — each
        // V2 Reference (single-column today) sees the same
        // FkReality metadata as a result. Per-FK-constraint axes
        // (UpdateAction + IsNoCheck) are constant across columns;
        // V2's per-attribute Reference IR is the natural carrier.
        let fkRealityByParentAttrId = fkRealityByParentAttrIdMap snapshot

        let references =
            snapshot.References
            |> List.choose (fun r ->
                match r.RefEntityName, Map.tryFind r.AttrId attributeById with
                | Some refName, Some attr ->
                    // The JOIN: this attribute's FK is reflected in
                    // #FkReality iff `fkOpt` is Some. That presence IS
                    // `HasDbConstraint` — a reflected FK propagates
                    // UpdateAction + the inverted IsNoCheck; a reference
                    // with NO reflected FK is logical-only, so
                    // HasDbConstraint = false (mirroring the JSON path's
                    // ISNULL(HasFK, 0) parity), OnUpdate None, trusted by
                    // default.
                    //
                    // WP-1a (DECISIONS 2026-07-16) — this field was
                    // hardcoded `true`, so every live-extracted reference
                    // presented as source-backed and the FK evidence gate
                    // (which lets source-backed FKs bypass orphan checks)
                    // could never see a logical-only reference. The
                    // hardcode contradicted this function's own doc
                    // ("HasDbConstraint defaults to true when an FK is
                    // reflected") and diverged from the JSON path, which
                    // already defaults absent → false.
                    let fkOpt = Map.tryFind r.AttrId fkRealityByParentAttrId
                    let onUpdate =
                        fkOpt |> Option.bind (fun fk -> fk.UpdateAction)
                    // WP-1b (DECISIONS 2026-07-16) — carry the reflected
                    // ON DELETE action alongside OnUpdate; the rowset reader
                    // prefers it over the model rule for physically-backed
                    // FKs and `deleteRuleDivergences` names the disagreement.
                    let onDelete =
                        fkOpt |> Option.bind (fun fk -> fk.DeleteAction)
                    let isTrusted =
                        match fkOpt with
                        | Some fk -> not fk.IsNoCheck
                        | None    -> true
                    Some
                        ({
                            AttrId              = r.AttrId
                            RefEntityName       = refName
                            RefEntityId         = r.RefEntityId
                            DeleteRuleCode      = attr.DeleteRule
                            HasDbConstraint     = Option.isSome fkOpt
                            OnUpdate            = onUpdate
                            ReflectedOnDelete   = onDelete
                            IsConstraintTrusted = isTrusted
                        } : OssysRowsetTypes.ReferenceRow)
                | _ -> None)

        // Slice 5.13.ossys-rowsets-cluster — JOIN logic for the new
        // index/trigger/check axes. The mappings are largely shape-
        // preserving (V1 source columns → V2 RowsetBundle row fields);
        // CatalogReader.parseRowsetBundle does the EntityId-keyed
        // group-and-resolve work via RowsetParseContext.
        let indexes =
            snapshot.Indexes
            |> List.map (fun i ->
                // Slice 5.13.fk-reality-join (paired with index-
                // features adapter wiring) — parse `#AllIdx
                // .DataCompressionJson` into a single-value level
                // when the JSON encodes uniform compression across
                // all partitions. The V1 SQL emits the compression
                // map as JSON like `[{"P":1,"Code":"PAGE"}, …]`;
                // when every partition's `Code` matches, the single-
                // value form is faithful. Heterogeneous compression
                // surfaces as `None` here (row 56 partition residual).
                let dataCompression =
                    i.DataCompressionJson
                    |> Option.bind tryParseUniformDataCompression
                // Slice A.4.7'-prelude.row56-dataspace (LR7 closure)
                // — project V1's three dataspace fields into the
                // closed-DU; unknown type_desc values silently degrade
                // to None (V2 omits the ON clause for unrecognized
                // shapes rather than emitting a guess).
                let dataSpace =
                    tryProjectDataSpace
                        i.DataSpaceName
                        i.DataSpaceType
                        i.PartitionColumnsJson
                {
                    EntityId         = i.EntityId
                    IndexName        = i.IndexName
                    IsUnique         = i.IsUnique
                    IsPrimary        = i.IsPrimary
                    FilterDefinition = i.FilterDefinition
                    IsPadded         = i.IsPadded
                    FillFactor       = i.FillFactor
                    AllowRowLocks    = i.AllowRowLocks
                    AllowPageLocks   = i.AllowPageLocks
                    NoRecompute      = i.NoRecompute
                    IsDisabled       = i.IsDisabled
                    IgnoreDupKey     = i.IgnoreDupKey
                    DataCompression  = dataCompression
                    DataSpace        = dataSpace
                } : OssysRowsetTypes.IndexRow)

        let indexColumns =
            snapshot.IndexColumns
            |> List.map (fun c ->
                {
                    EntityId       = c.EntityId
                    IndexName      = c.IndexName
                    Ordinal        = c.Ordinal
                    HumanAttr      = c.HumanAttr
                    PhysicalColumn = c.PhysicalColumn
                    IsIncluded     = c.IsIncluded
                    Direction      = c.Direction
                } : OssysRowsetTypes.IndexColumnRow)

        let triggers =
            snapshot.Triggers
            |> List.map (fun t ->
                {
                    EntityId    = t.EntityId
                    TriggerName = t.TriggerName
                    IsDisabled  = t.IsDisabled
                    Definition  = t.TriggerDefinition
                } : OssysRowsetTypes.TriggerRow)

        let columnChecks =
            snapshot.ColumnChecks
            |> List.map (fun c ->
                {
                    AttrId         = c.AttrId
                    ConstraintName = c.ConstraintName
                    Definition     = c.Definition
                    IsNotTrusted   = c.IsNotTrusted
                } : OssysRowsetTypes.ColumnCheckRow)

        let sequences =
            snapshot.Sequences
            |> List.map (fun s ->
                ({
                    Schema       = s.Schema
                    Name         = s.Name
                    DataType     = s.DataType
                    StartValue   = s.StartValue
                    Increment    = s.Increment
                    MinimumValue = s.MinimumValue
                    MaximumValue = s.MaximumValue
                    IsCycling    = s.IsCycling
                    IsCached     = s.IsCached
                    CacheSize    = s.CacheSize
                } : OssysRowsetTypes.SequenceRow))

        let temporal =
            snapshot.Temporal
            |> List.map (fun t ->
                ({
                    EntityId       = t.EntityId
                    HistorySchema  = t.HistorySchema
                    HistoryTable   = t.HistoryTable
                    PeriodStart    = t.PeriodStart
                    PeriodEnd      = t.PeriodEnd
                    RetentionValue = t.RetentionValue
                    RetentionUnit  = t.RetentionUnit
                } : OssysRowsetTypes.TemporalRow))

        // align-II.8 (A54) — the data-dependent erasures, collected on the
        // SAME predicates the projections above decide with (an assumption
        // or a drop fires exactly when its shape occurs; deterministic
        // order — constants first, then entity/attribute/reference order).
        let assumedSchemas =
            snapshot.Entities
            |> List.choose (fun e ->
                if Map.containsKey e.EntityId physicalByEntity then None
                else Some (BundleErasure.AssumedSchema (e.EntityId, e.EntityName)))
        let assumedDataTypes =
            snapshot.Attributes
            |> List.choose (fun a ->
                match a.DataType with
                | Some _ -> None
                | None -> Some (BundleErasure.AssumedDataType (a.AttrId, a.AttrName)))
        let unjoinedReferences =
            snapshot.References
            |> List.choose (fun r ->
                match r.RefEntityName, Map.tryFind r.AttrId attributeById with
                | Some _, Some _ -> None
                | _ -> Some (BundleErasure.UnjoinedReference r.AttrId))
        let erasures =
            constantErasures @ assumedSchemas @ assumedDataTypes @ unjoinedReferences
        {
            Modules      = modules
            Kinds        = kinds
            Attributes   = attributes
            References   = references
            Indexes      = indexes
            IndexColumns = indexColumns
            Triggers     = triggers
            ColumnChecks = columnChecks
            Sequences    = sequences
            Temporal     = temporal
        },
        erasures
