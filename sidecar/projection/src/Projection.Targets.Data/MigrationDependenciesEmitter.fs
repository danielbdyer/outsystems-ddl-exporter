namespace Projection.Targets.Data

open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT  // LINT-ALLOW: cross-target dependency for ScriptDom MERGE typed-AST construction (`ScriptDomBuild.buildMergeStatement` + `buildSqlLiteral`) per the Tier-1 #1 transition (chapter-4.1.B slice α / δ precedent) and the Tier-3 hard-requirement Active deferral (chapter-4.1.B slice ε MUST adopt `ScriptDomBuild.buildMergeStatement`); the typed AST flows through `ScriptDomGenerate.generateOne` for canonical SQL-text rendering

// ---------------------------------------------------------------------------
// MigrationDependencyContext — operator-published legacy-domain rows.
//
// Per `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` §2.2: migration-dependency
// status is operator intent, not catalog-resident evidence. The context
// carries actual row data (not behavioral configuration), so it is
// **Profile-shaped sibling input** rather than `Policy`. Adapter at
// the boundary (NDJSON / CSV pickup directory; deferred until I/O
// consumer demand surfaces) reads into this typed shape; the emitter
// is pure F# in Core-adjacent.
//
// **Slice ε scope (chapter 4.1.B).** The typed context shape lands;
// the boundary adapter is deferred until a real ingestion path
// surfaces (production migration teams may publish via a different
// format or via an existing config surface — I/O choice deferred per
// IR-grows-under-evidence). MVP consumers construct the context
// programmatically; tests use `MigrationDependencyContext.empty` and
// hand-rolled fixtures.
// ---------------------------------------------------------------------------

/// One row of legacy-domain data the migration team is publishing
/// for V2 to insert. Identity-keyed at every level (per A4); values
/// are raw IR strings per the `RawValueCodec` contract (the renderer
/// looks up the column's `PrimitiveType` from the kind's `Attribute`
/// list and applies `SqlLiteral.ofRaw` at MERGE construction).
type MigrationDependencyRow =
    {
        /// The owning kind's stable identity (per A4). Matched
        /// against `Catalog.tryFindKind` at emission time so the
        /// emitter can resolve column types + names without
        /// requiring the context to mirror the catalog's structure.
        KindKey    : SsKey
        /// The row's stable identity (per A1 / A7 — every row a
        /// migration adapter publishes carries an SsKey so cross-
        /// version diffs and re-publication are trackable).
        Identifier : SsKey
        /// Column-name → raw-value-string. Same shape as
        /// `StaticRow.Values` so static-vs-migration provenance is
        /// the only structural distinction.
        Values     : Map<Name, string>
    }

/// Operator-published row inventory for the migration-dependency
/// channel. Per pre-scope §2.2: "the context is environment-specific
/// evidence the migration team supplies, structurally indistinguishable
/// from per-environment data the read-side might surface."
type MigrationDependencyContext =
    {
        Rows : MigrationDependencyRow list
    }

[<RequireQualifiedAccess>]
module MigrationDependencyContext =

    /// The empty context — no migration rows. The neutral input for
    /// callers that don't have a migration-dependency channel
    /// configured (the dominant case at chapter 4.1.B slice ε
    /// since the ingestion adapter is deferred). `MigrationDependencies
    /// Emitter` against this context produces empty `Phase1Merges` /
    /// `Phase2Updates` for every kind (T11 keyset preserved).
    let empty : MigrationDependencyContext = { Rows = [] }

    /// Group the context's rows by their owning `KindKey`. The
    /// emitter uses this to fold rows into per-kind MERGEs while
    /// preserving deterministic per-row order. Rows in the input
    /// order are preserved within each group — callers controlling
    /// row order get byte-deterministic output.
    let rowsByKind (ctx: MigrationDependencyContext) : Map<SsKey, MigrationDependencyRow list> =
        ctx.Rows
        |> List.groupBy (fun r -> r.KindKey)
        |> Map.ofList

/// Π_MigrationDependencies — chapter 4.1.B slice ε emitter for
/// operator-published legacy-domain rows. Consumes `Catalog × Profile`
/// + `MigrationDependencyContext`; per A18 amended, no `Policy`. The
/// composition layer (`DataEmissionComposer`) reads `Policy.Emission.
/// DataComposition` and chooses whether this emitter fires; the
/// emitter does not.
///
/// **T11 sibling-Π commutativity.** The emitter produces an
/// `ArtifactByKind<DataInsertScript>` keyed by every catalog kind.
/// Kinds without rows in the context produce a script with empty
/// `Phase1Merges` (no-op artifact) — per the strict-equality T11
/// invariant: every kind appears, no kind is silently absent.
///
/// **Cycle-breaking parity with StaticSeedsEmitter.** Migration rows
/// sit in the same FK graph as static rows; an FK target that lives
/// in a cycle (whether populated by Static or Migration or
/// Bootstrap) needs the same Phase-1-NULL / Phase-2-UPDATE deferral.
/// `deferredColumns` is the same predicate used by `StaticSeedsEmitter`
/// — in-cycle membership + nullable column. The composer (slice η)
/// passes the hoisted `TopologicalOrder` so cycle membership is one
/// source-of-truth across the triumvirate.
///
/// **Pillar 7 Tier-3 hard-requirement (per `DECISIONS 2026-05-10 —
/// text-builder-as-first-instinct`)** holds: the MERGE shape flows
/// through `ScriptDomBuild.buildMergeStatement`'s typed AST + the
/// Phase-2 UPDATE flows through `ScriptDomBuild.buildUpdateStatement`.
/// The slice α / δ precedent is the structural template; this
/// emitter is its sibling-Π consumer.
[<RequireQualifiedAccess>]
module MigrationDependenciesEmitter =

    [<Literal>]
    let version : int = 1

    /// Type-resolution lookup for a kind's columns (mirrors
    /// `StaticSeedsEmitter.columnTypeLookup`).
    let private columnTypeLookup (k: Kind) : Map<Name, PrimitiveType> =
        k.Attributes
        |> List.map (fun a -> a.Name, a.Type)
        |> Map.ofList

    let private orderedColumnNames (k: Kind) : string list =
        k.Attributes |> List.map (fun a -> a.Column.ColumnName)

    let private pkColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

    let private updatableColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> not a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

    /// Cycle-membership-aware deferred-FK predicate. Per slice δ /
    /// `StaticSeedsEmitter.deferredColumns`: in-cycle + nullable.
    /// V1 reference: `IdentifyNullableFKColumns:184`.
    let private deferredColumns
        (cycleMembers: Set<SsKey>)
        (k: Kind)
        : Set<Name> =
        if not (Set.contains k.SsKey cycleMembers) then Set.empty
        else
            k.References
            |> List.choose (fun r ->
                if Set.contains r.TargetKind cycleMembers then
                    Kind.tryFindAttribute r.SourceAttribute k
                    |> Option.bind (fun a ->
                        if a.Column.IsNullable then Some a.Name else None)
                else None)
            |> Set.ofList

    /// Project a `MigrationDependencyRow` into the typed
    /// `SqlLiteral list` form `ScriptDomBuild.MergeBuildArgs.Rows`
    /// expects. Iterates the kind's attributes in declared order;
    /// missing values default to NULL (V2 IR's empty-raw sentinel
    /// per `RawValueCodec`); deferred-FK columns get `SqlLiteral.
    /// NullLit` regardless of the row's raw value (Phase-1 cycle-
    /// break — same shape as `StaticSeedsEmitter.rowToSqlLiterals`).
    let private rowToSqlLiterals
        (typeLookup: Map<Name, PrimitiveType>)
        (deferred: Set<Name>)
        (attributes: Attribute list)
        (row: MigrationDependencyRow)
        : SqlLiteral list =
        attributes
        |> List.map (fun a ->
            if Set.contains a.Name deferred then
                SqlLiteral.NullLit
            else
                let raw =
                    Map.tryFind a.Name row.Values
                    |> Option.defaultValue ""
                let typ =
                    Map.tryFind a.Name typeLookup
                    |> Option.defaultValue PrimitiveType.Text
                SqlLiteral.ofRaw typ raw)

    /// Render the MERGE statement for a kind with its migration
    /// rows via ScriptDom's typed-AST + `Sql160ScriptGenerator`
    /// pipeline. Same architectural shape as `StaticSeedsEmitter.
    /// renderMerge` — the Tier-3 hard-requirement adoption is what
    /// makes this code so similar to the slice-α / δ precedent.
    /// CDC-aware predicate dispatch per `Profile.CdcAwareness` is
    /// preserved.
    let private renderMerge
        (cdcAware: bool)
        (deferred: Set<Name>)
        (k: Kind)
        (rows: MigrationDependencyRow list)
        : string =
        use _ = Bench.scope "emit.migrationDeps.renderMerge"
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table }
        let typeLookup = columnTypeLookup k
        let args : ScriptDomBuild.MergeBuildArgs =
            {
                Target     = table
                AllColumns = orderedColumnNames k
                PkColumns  = pkColumnNames k
                UpdColumns = updatableColumnNames k
                Rows       = rows |> List.map (rowToSqlLiterals typeLookup deferred k.Attributes)
                CdcAware   = cdcAware
            }
        let mergeStmt = ScriptDomBuild.buildMergeStatement args
        System.String.Concat(  // LINT-ALLOW: terminal MERGE statement-terminator + GO-batch suffix on the rendered MERGE (chapter 4.1.B slice ε); segments are typed (output of `ScriptDomGenerate.generateOne` from typed AST + SQL Server's required MERGE statement-terminator + V1 batch-separator literal); same architectural shape as StaticSeedsEmitter's terminal-text boundary
            ScriptDomGenerate.generateOne (mergeStmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
            ";\nGO\n")

    /// Render one Phase-2 UPDATE for a row with a deferred FK column.
    /// Same shape as `StaticSeedsEmitter.renderUpdate`. Per the
    /// Tier-3 cash-out: `ScriptDomBuild.buildUpdateStatement` is the
    /// typed-AST primitive; this emitter is its second consumer.
    let private renderUpdate
        (k: Kind)
        (deferred: Set<Name>)
        (row: MigrationDependencyRow)
        : string =
        use _ = Bench.scope "emit.migrationDeps.renderUpdate"
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table }
        let typeLookup = columnTypeLookup k
        let cellOf (a: Attribute) : string * SqlLiteral =
            let raw =
                Map.tryFind a.Name row.Values
                |> Option.defaultValue ""
            let typ =
                Map.tryFind a.Name typeLookup
                |> Option.defaultValue PrimitiveType.Text
            a.Column.ColumnName, SqlLiteral.ofRaw typ raw
        let setCells =
            k.Attributes
            |> List.filter (fun a -> Set.contains a.Name deferred)
            |> List.map cellOf
        let whereCells =
            k.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map cellOf
        let args : ScriptDomBuild.UpdateBuildArgs =
            { Target     = table
              SetCells   = setCells
              WhereCells = whereCells }
        let updateStmt = ScriptDomBuild.buildUpdateStatement args
        System.String.Concat(  // LINT-ALLOW: terminal UPDATE statement-terminator + GO-batch suffix on the rendered Phase-2 UPDATE (chapter 4.1.B slice ε); segments are typed (output of `ScriptDomGenerate.generateOne` from `ScriptDomBuild.buildUpdateStatement` typed AST + SQL Server's statement-terminator + V1 batch-separator literal); same architectural shape as StaticSeedsEmitter.renderUpdate
            ScriptDomGenerate.generateOne (updateStmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
            ";\nGO\n")

    /// Build one `DataInsertScript` for a kind's migration rows.
    /// Empty per-kind context produces a no-op script (T11
    /// preserved). Cycle-aware Phase-1/Phase-2 dispatch identical
    /// to `StaticSeedsEmitter.kindToScript` (the slice δ shape).
    let private kindToScript
        (cdc: CdcAwareness)
        (cycleMembers: Set<SsKey>)
        (rowsByKind: Map<SsKey, MigrationDependencyRow list>)
        (k: Kind)
        : DataInsertScript =
        let rows = Map.tryFind k.SsKey rowsByKind |> Option.defaultValue []
        if List.isEmpty rows then
            { Phase1Merges = []; Phase2Updates = []; Rendered = "" }
        else
            let cdcAware = CdcAwareness.isEnabled k.SsKey cdc
            let deferred = deferredColumns cycleMembers k
            let mergeText = renderMerge cdcAware deferred k rows
            let updateTexts =
                if Set.isEmpty deferred then []
                else rows |> List.map (renderUpdate k deferred)
            let rendered =
                mergeText :: updateTexts
                |> System.String.Concat  // LINT-ALLOW: terminal per-kind text concatenation of ScriptDom-rendered + GO-batched MERGE/UPDATE statement strings (chapter 4.1.B slice ε); each segment is the typed-AST output of `ScriptDomGenerate.generateOne` already terminated by `;\nGO\n`; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary; the typed `Statement` DU does not yet model MERGE/UPDATE so `ScriptDomGenerate.toText` is not applicable here
            let mkRow (row: MigrationDependencyRow) : DataInsertRow =
                { KindKey       = k.SsKey
                  Identifier    = row.Identifier
                  Values        = row.Values
                  DeferredFkSet = deferred }
            let phase1Rows = rows |> List.map mkRow
            let phase2Rows =
                if Set.isEmpty deferred then []
                else rows |> List.map mkRow
            { Phase1Merges  = phase1Rows
              Phase2Updates = phase2Rows
              Rendered      = rendered }

    /// Π_MigrationDependencies emit (composer-facing; hoisted topo).
    /// Per A18 amended (`Catalog × Profile` × sibling-evidence input,
    /// never `Policy`) and T11 (every kind in the keyset).
    let emitWithTopo
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emitWithTopo"
        let cdc = profile.CdcAwareness
        let cycleMembers =
            topo.Cycles
            |> List.collect (fun c -> c.Members)
            |> Set.ofList
        let rowsByKind = MigrationDependencyContext.rowsByKind context
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, kindToScript cdc cycleMembers rowsByKind k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Π_MigrationDependencies emit (standalone). Convenience for
    /// callers that don't go through the `DataEmissionComposer`.
    /// Computes the topological order internally and delegates to
    /// `emitWithTopo` — same algebra, one extra `TopologicalOrderPass`
    /// invocation per call.
    let emit
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo topo catalog profile context
