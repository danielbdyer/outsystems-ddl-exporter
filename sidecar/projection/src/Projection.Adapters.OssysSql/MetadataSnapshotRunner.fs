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
/// canary's offline-extraction surface — it walks the carbon-copied SQL's
/// 22 result sets, parses the first 5 into typed F# records mirroring V1's
/// DTOs, and assembles a `OssysRowsetTypes.RowsetBundle` consumable by V2's
/// existing `CatalogReader.parse` JSON / rowset adapter.
///
/// **Chapter 5.0 slice γ.** The canary's bootstrap+extract flow:
///   1. Caller deploys `MetadataExtractionSql.readEdgeCaseSeed()` against
///      a clean SQL Server database (creates synthetic OSSYS schema +
///      edge-case data).
///   2. Caller invokes `MetadataSnapshotRunner.runAsync` with an open
///      `SqlConnection` to that database + the V1 parameters.
///   3. This module reads `MetadataExtractionSql.read()` (the rowsets SQL),
///      executes it via `SqlCommand`, and walks `DbDataReader.NextResultAsync`
///      to enumerate all 22 result sets.
///   4. The first 5 result sets (Modules / Entities / Attributes /
///      References / PhysicalTables) parse into typed F# records; the
///      remaining 17 are skipped (the SQL still emits them but V2's
///      current consumption surface is the narrow 4-rowset
///      `OssysRowsetTypes.RowsetBundle`).
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
          IsMandatory       : bool
          IsActive          : bool
          IsAutoNumber      : bool
          IsIdentifier      : bool
          RefEntityId       : int option
          OriginalName      : string option
          ExternalDbType    : string option
          DeleteRule        : string option
          PhysicalCol       : string
          Description       : string option }

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
    /// SQL. Currently no V2 consumer; lifts at the runner layer so a
    /// future tightening-rule that needs source-reflection evidence
    /// (e.g., Profile.AttributeReality per matrix row 49) can wire from
    /// here without re-walking the rowset.
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
          PhysicalColumn        : string option }

    /// `#ColumnCheckReality` rowset (matrix row 12). Per-column CHECK
    /// constraint reflection. V2's `Kind.ColumnChecks` IR exists per
    /// chapter A.0' slice ε (matrix row 50); the rowset path now wires
    /// to it.
    type OssysColumnCheckRow =
        { AttrId         : int
          ConstraintName : string
          Definition     : string
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
        }

    // -------------------------------------------------------------------
    // Internal helpers — typed SqlDataReader column readers. Pattern
    // mirrors V1's `Column.StringOrNull` etc. but uses ordinal-indexed
    // access for performance + F# idioms.
    // -------------------------------------------------------------------

    let private readString (reader: SqlDataReader) (ordinal: int) : string =
        if reader.IsDBNull(ordinal) then
            invalidOp (sprintf "MetadataSnapshotRunner: required column at ordinal %d was NULL" ordinal)
        else
            reader.GetString(ordinal)

    let private readStringOpt (reader: SqlDataReader) (ordinal: int) : string option =
        if reader.IsDBNull(ordinal) then None
        else Some (reader.GetString(ordinal))

    let private readInt (reader: SqlDataReader) (ordinal: int) : int =
        // V1 sometimes returns int via flexible widening (Int16 / Int64);
        // SqlDataReader.GetInt32 throws on type mismatch. Use Convert to
        // tolerate width variation.
        //
        // Defensive-fallback (slice A.4.7'-prelude.defensive-hardening,
        // 2026-05-19): mirror `readString`'s explicit DBNull guard.
        // `Convert.ToInt32 DBNull.Value` silently returns 0 — which is
        // the WORST failure shape (silent identity/FK corruption in the
        // produced Catalog). Raise on NULL so the caller's snapshot
        // contract is honored (any required-int column with NULL is a
        // V1-source data integrity issue, not a V2 adapter problem).
        if reader.IsDBNull(ordinal) then
            invalidOp (sprintf "MetadataSnapshotRunner: required int column at ordinal %d was NULL" ordinal)
        else
            System.Convert.ToInt32(reader.GetValue(ordinal))

    let private readIntOpt (reader: SqlDataReader) (ordinal: int) : int option =
        if reader.IsDBNull(ordinal) then None
        else Some (readInt reader ordinal)

    let private readBool (reader: SqlDataReader) (ordinal: int) : bool =
        let value = reader.GetValue(ordinal)
        match value with
        | :? bool as b -> b
        | :? byte as b -> b <> 0uy
        | :? int as i  -> i <> 0
        | _ -> System.Convert.ToBoolean(value)

    let private readBoolOpt (reader: SqlDataReader) (ordinal: int) : bool option =
        if reader.IsDBNull(ordinal) then None
        else Some (readBool reader ordinal)

    let private readGuidOpt (reader: SqlDataReader) (ordinal: int) : Guid option =
        if reader.IsDBNull(ordinal) then None
        else Some (reader.GetGuid(ordinal))

    /// Read all rows of the current result set via `mapper`; advance to
    /// the next result set when complete. Returns the rows in source
    /// order.
    ///
    /// Mapper failures (e.g., `InvalidCastException` from a widened SQL
    /// type or `InvalidOperationException` from a required-but-NULL
    /// column) re-raise as `RowMappingException` carrying the
    /// `resultSetName` + zero-based `rowIndex` for downstream
    /// classification (matrix row 32 cash-out — the typed
    /// `MetadataExtractionError.RowMappingFailure` variant).
    let private readResultSet<'T>
            (resultSetName: string)
            (reader: SqlDataReader)
            (mapper: SqlDataReader -> 'T)
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
                            mapper reader
                        with
                        | :? RowMappingException -> reraise ()
                        | ex -> raise (RowMappingException (resultSetName, rowIndex, ex))
                    acc.Add row
                    rowIndex <- rowIndex + 1
                else hasMore <- false
            return List.ofSeq acc
        }

    /// Skip the current result set without parsing any rows. Used for the
    /// 17 result sets V2 doesn't yet consume; `NextResultAsync` advances
    /// past them.
    let private skipResultSet (reader: SqlDataReader) : Task<unit> =
        task {
            let mutable hasMore = true
            while hasMore do
                let! advanced = reader.ReadAsync()
                if not advanced then hasMore <- false
            return ()
        }

    let private mapModuleRow (r: SqlDataReader) : OssysModuleRow =
        { EspaceId       = readInt r 0
          EspaceName     = readString r 1
          IsSystemModule = readBool r 2
          IsActive       = readBool r 3
          EspaceKind     = readStringOpt r 4
          EspaceSsKey    = readGuidOpt r 5 }

    let private mapEntityRow (r: SqlDataReader) : OssysEntityRow =
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

    let private mapAttributeRow (r: SqlDataReader) : OssysAttributeRow =
        { AttrId         = readInt r 0
          EntityId       = readInt r 1
          AttrName       = readString r 2
          AttrSsKey      = readGuidOpt r 3
          DataType       = readStringOpt r 4
          Length         = readIntOpt r 5
          Precision      = readIntOpt r 6
          Scale          = readIntOpt r 7
          // ordinal 8 is DefaultValue (string?) — not consumed by V2
          // RowsetBundle today; skipped.
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
              // Fall back to AttrName when V1 source omits.
              match readStringOpt r 17 with
              | Some n when not (System.String.IsNullOrWhiteSpace n) -> n
              | _ -> (readString r 2).ToUpperInvariant()
          Description    = readStringOpt r 22 }

    let private mapReferenceRow (r: SqlDataReader) : OssysReferenceRow =
        { AttrId          = readInt r 0
          RefEntityId     = readIntOpt r 1
          RefEntityName   = readStringOpt r 2
          RefPhysicalName = readStringOpt r 3 }

    let private mapPhysicalTableRow (r: SqlDataReader) : OssysPhysicalTableRow =
        { EntityId   = readInt r 0
          SchemaName = readString r 1
          TableName  = readString r 2
          ObjectId   = readInt r 3 }

    // Slice 5.13.ossys-rowsets-cluster: mappers for rowsets 5–18 (less
    // the V1-SUNSET JSON-aggregation helpers).  Ordinal layout mirrors
    // V1's SELECT-projection ordering in `outsystems_metadata_rowsets.sql`
    // (see line numbers cited in each mapper's docstring).

    let private mapColumnRealityRow (r: SqlDataReader) : OssysColumnRealityRow =
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
          PhysicalColumn        = readStringOpt  r 12 }

    let private mapColumnCheckRow (r: SqlDataReader) : OssysColumnCheckRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1042-1048
        { AttrId         = readInt    r 0
          ConstraintName = readString r 1
          Definition     = readString r 2
          IsNotTrusted   = readBool   r 3 }

    let private mapPhysColsPresentRow (r: SqlDataReader) : OssysPhysColsPresentRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1056-1059
        { AttrId = readInt r 0 }

    let private mapAllIdxRow (r: SqlDataReader) : OssysAllIdxRow =
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

    let private mapIdxColMappedRow (r: SqlDataReader) : OssysIdxColMappedRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1084-1092
        { EntityId       = readInt       r 0
          IndexName      = readString    r 1
          Ordinal        = readInt       r 2
          PhysicalColumn = readStringOpt r 3
          IsIncluded     = readBool      r 4
          Direction      = readStringOpt r 5
          HumanAttr      = readStringOpt r 6 }

    let private mapFkRealityRow (r: SqlDataReader) : OssysFkRealityRow =
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

    let private mapFkColumnRow (r: SqlDataReader) : OssysFkColumnRow =
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

    let private mapTriggerRow (r: SqlDataReader) : OssysTriggerRow =
        // V1 SELECT at outsystems_metadata_rowsets.sql:1146-1151
        { EntityId          = readInt       r 0
          TriggerName       = readString    r 1
          IsDisabled        = readBool      r 2
          TriggerDefinition = readStringOpt r 3 }

    /// Number of user-visible result sets the carbon-copied OSSYS rowsets
    /// script emits. V1's documentation describes 22 user-visible rowsets
    /// (rowsets 0..21); the canary's empirical walk observes **23** —
    /// the script includes a leading validation/sanity-check projection
    /// that V1's per-processor walk doesn't enumerate but V2's
    /// `NextResultAsync` loop does. **Truth is the canary** (R6 split-brain:
    /// the canary is V2's load-bearing forcing function); the constant
    /// pins what V2 actually observes against the carbon-copied SQL.
    /// The post-loop assertion in `runAsync` surfaces SQL-contract drift
    /// (e.g., a V1 trunk refactor drops a rowset) as `ResultSetMissing`
    /// instead of silently accepting partial data. Matrix row 35.
    [<Literal>]
    let ExpectedResultSets = 23

    /// Execute the carbon-copied rowsets SQL against `cnn` (already open)
    /// with the supplied parameters + options. Walks all
    /// `ExpectedResultSets` result sets; parses the first 5 into typed
    /// records and skips the remaining. Returns a `MetadataSnapshot`
    /// carrying the 5 V2-relevant rowsets.
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
                // `read name mapper` advances to the next rowset, parses
                // every row via the typed mapper, reports the rowcount
                // for progress observation. Symmetric `skip name`
                // advances + skips + reports rowcount=0 for the V1-SUNSET
                // JSON-aggregation tail (#AttrCheckJson, #FkAttrMap,
                // #AttrHasFK, #FkColumnsJson, #FkAttrJson, #AttrJson,
                // #RelJson, #IdxJson, #TriggerJson, #ModuleJson).
                let read (name: string) (mapper: SqlDataReader -> 'T) : Task<'T list> =
                    task {
                        use _ = Bench.scope "adapter.osm.extract.rowset"
                        use _ = Bench.scope (sprintf "adapter.osm.extract.rowset.%s" name)
                        let! _ = advanceNext ()
                        let! rows = readResultSet name reader mapper
                        report name rows.Length
                        return rows
                    }
                let skip (name: string) : Task<unit> =
                    task {
                        use _ = Bench.scope "adapter.osm.extract.rowset"
                        use _ = Bench.scope (sprintf "adapter.osm.extract.rowset.%s" name)
                        let! _ = advanceNext ()
                        do! skipResultSet reader
                        report name 0
                    }

                // Rowset 0 — modules (no advance; reader opens here).
                let! modules =
                    task {
                        use _ = Bench.scope "adapter.osm.extract.rowset"
                        use _ = Bench.scope "adapter.osm.extract.rowset.modules"
                        let! rows = readResultSet "modules" reader mapModuleRow
                        report "modules" rows.Length
                        return rows
                    }

                // Rowsets 1–4 — already-lifted V2-consumed surface.
                let! entities       = read "entities"       mapEntityRow
                let! attributes     = read "attributes"     mapAttributeRow
                let! references     = read "references"     mapReferenceRow
                let! physicalTables = read "physicalTables" mapPhysicalTableRow

                // Rowsets 5–18 — physical-reflection lifts (slice
                // 5.13.ossys-rowsets-cluster). #AttrCheckJson (rowset
                // 7), #FkAttrMap, #AttrHasFK, #FkColumnsJson,
                // #FkAttrJson (rowsets 13–16) are V1-SUNSET JSON
                // helpers V2 doesn't consume; skipped with the same
                // report-shape so progress count stays accurate.
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

                // Rowset 17 — triggers (matrix row 23).
                let! triggers = read "triggers" mapTriggerRow

                // Rowsets 18–22 — V1-SUNSET JSON aggregation tail.
                do! skip "attrJson"
                do! skip "relJson"
                do! skip "idxJson"
                do! skip "triggerJson"
                do! skip "moduleJson"

                // Drain any trailing rowsets the SQL might emit beyond
                // the documented 23. Per matrix row 35: a SQL-contract
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
    ///     `HasDbConstraint` defaults to `true` when an FK is reflected
    ///     in the references rowset.

    /// Parse V1's `#AllIdx.DataCompressionJson` into a single-value
    /// compression code when the JSON encodes uniform compression
    /// across every partition. The JSON shape per V1's SQL is
    /// `[{"P":1,"Code":"PAGE"}, …]` — one entry per partition.
    /// Returns `Some "<code>"` when every entry's Code matches;
    /// `None` when heterogeneous or unparseable (row 56 partition
    /// axis residual). Pillar 7 four-question analysis: the typed
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
                            if entry.TryGetProperty("Code", &codeEl) then
                                match Option.ofObj (codeEl.GetString()) with
                                | Some s -> yield s
                                | None   -> ()
                    }
                    |> Seq.toList
                match codes |> List.distinct with
                | [ single ] -> Some single
                | _          -> None
        with _ -> None

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
        with _ -> None

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
                Some (DataSpace.Filegroup n)
            | "PARTITION_SCHEME" ->
                let cols =
                    partitionColumnsJson
                    |> Option.bind tryParsePartitionColumns
                    |> Option.defaultValue []
                Some (DataSpace.PartitionScheme (n, cols))
            | _ -> None
        | _ -> None

    let toBundle (snapshot: MetadataSnapshot) : OssysRowsetTypes.RowsetBundle =
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
                let realityIsComputed, realityComputedDef, realityDefaultName =
                    match Map.tryFind a.AttrId columnRealityByAttrId with
                    | Some cr -> cr.IsComputed, cr.ComputedDefinition, cr.DefaultConstraintName
                    | None    -> false, None, None
                {
                    AttrId               = a.AttrId
                    EntityId             = a.EntityId
                    AttrName             = a.AttrName
                    PhysicalCol          = a.PhysicalCol
                    DataType             = match a.DataType with Some s -> s | None -> "Text"
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
        let fkRealityById =
            snapshot.ForeignKeysReality
            |> List.map (fun fk -> fk.FkObjectId, fk)
            |> Map.ofList
        let fkRealityByParentAttrId =
            snapshot.ForeignKeyColumns
            |> List.choose (fun c ->
                match c.ParentAttrId, Map.tryFind c.FkObjectId fkRealityById with
                | Some pid, Some fk -> Some (pid, fk)
                | _ -> None)
            |> Map.ofList

        let references =
            snapshot.References
            |> List.choose (fun r ->
                match r.RefEntityName, Map.tryFind r.AttrId attributeById with
                | Some refName, Some attr ->
                    // The JOIN: when this attribute's FK is reflected
                    // in #FkReality, propagate UpdateAction + the
                    // inverted IsNoCheck. Absent rows degrade to the
                    // ReferenceRow defaults (None / true).
                    let fkOpt = Map.tryFind r.AttrId fkRealityByParentAttrId
                    let onUpdate =
                        fkOpt |> Option.bind (fun fk -> fk.UpdateAction)
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
                            HasDbConstraint     = true
                            OnUpdate            = onUpdate
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

        {
            Modules      = modules
            Kinds        = kinds
            Attributes   = attributes
            References   = references
            Indexes      = indexes
            IndexColumns = indexColumns
            Triggers     = triggers
            ColumnChecks = columnChecks
        }
