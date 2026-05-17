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

    /// Project a `MigrationDependencyRow`'s raw `Map<Name, string>`
    /// into the typed `Map<Name, SqlLiteral>` shape
    /// `DataInsertRow.Values` expects (slice κ pillar 1 lift).
    /// Mirror of `StaticSeedsEmitter.staticRowToTypedValues`.
    let private migrationRowToTypedValues
        (typeLookup: Map<Name, PrimitiveType>)
        (attributes: Attribute list)
        (row: MigrationDependencyRow)
        : Map<Name, SqlLiteral> =
        attributes
        |> List.map (fun a ->
            let raw =
                Map.tryFind a.Name row.Values
                |> Option.defaultValue ""
            let typ =
                Map.tryFind a.Name typeLookup
                |> Option.defaultValue PrimitiveType.Text
            a.Name, SqlLiteral.ofRaw typ raw)
        |> Map.ofList

    /// Project the typed-Values row into the `SqlLiteral list` form
    /// `MergeBuildArgs.Rows` expects. Slice δ deferred handling
    /// (NULLed columns) mirrors `StaticSeedsEmitter`.
    let private typedValuesToSqlLiterals
        (deferred: Set<Name>)
        (attributes: Attribute list)
        (values: Map<Name, SqlLiteral>)
        : SqlLiteral list =
        attributes
        |> List.map (fun a ->
            if Set.contains a.Name deferred then
                SqlLiteral.NullLit
            else
                Map.tryFind a.Name values
                |> Option.defaultValue SqlLiteral.NullLit)

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
        (typedRows: Map<Name, SqlLiteral> list)
        : string =
        use _ = Bench.scope "emit.migrationDeps.renderMerge"
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table; Catalog = None }
        let args : ScriptDomBuild.MergeBuildArgs =
            {
                Target     = table
                AllColumns = orderedColumnNames k
                PkColumns  = pkColumnNames k
                UpdColumns = updatableColumnNames k
                Rows       = typedRows |> List.map (typedValuesToSqlLiterals deferred k.Attributes)
                CdcAware   = cdcAware
            }
        let mergeStmt = (ScriptDomBuild.buildMergeStatement args).Value
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
        (typedValues: Map<Name, SqlLiteral>)
        : string =
        use _ = Bench.scope "emit.migrationDeps.renderUpdate"
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table; Catalog = None }
        let cellOf (a: Attribute) : string * SqlLiteral =
            let lit =
                Map.tryFind a.Name typedValues
                |> Option.defaultValue SqlLiteral.NullLit
            a.Column.ColumnName, lit
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
        let updateStmt = (ScriptDomBuild.buildUpdateStatement args).Value
        System.String.Concat(  // LINT-ALLOW: terminal UPDATE statement-terminator + GO-batch suffix on the rendered Phase-2 UPDATE (chapter 4.1.B slice ε); segments are typed (output of `ScriptDomGenerate.generateOne` from `ScriptDomBuild.buildUpdateStatement` typed AST + SQL Server's statement-terminator + V1 batch-separator literal); same architectural shape as StaticSeedsEmitter.renderUpdate
            ScriptDomGenerate.generateOne (updateStmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
            ";\nGO\n")

    // -------------------------------------------------------------------
    // User-FK rewrite (chapter 4.2 slice η).
    //
    // Per pre-scope §5: at emit time, when emitting a row for a kind
    // with one or more User-FK columns (Reference.IsUserFk = true),
    // the emitter looks up each `CreatedBy` / `UpdatedBy` value in
    // `UserRemapContext.Mapping` and rewrites the value. If the lookup
    // fails (the source user is in `Unmatched`), the emitter SKIPS
    // the row entirely (V1 reference `UserMatchingResult.cs` +
    // `EmitArtifactsStep.cs`: "diagnostic + skip"; the diagnostic was
    // already emitted by `UserFkReflowPass.discover`, so the emitter
    // silently drops the row).
    // -------------------------------------------------------------------

    /// Set of (attribute-name) columns on `k` that resolve to User-
    /// FK references — i.e., references with `IsUserFk = true`. Each
    /// reference's `SourceAttribute` SsKey is resolved to its
    /// `Attribute.Name` via `Kind.tryFindAttribute` (slice δ
    /// improvement #5 cash-out; second consumer after slice δ's
    /// deferredColumns). Empty set for kinds with no User-FKs.
    let private userFkColumnNames (k: Kind) : Set<Name> =
        k.References
        |> List.choose (fun r ->
            if r.IsUserFk then
                Kind.tryFindAttribute r.SourceAttribute k
                |> Option.map (fun a -> a.Name)
            else None)
        |> Set.ofList

    /// Apply the User-FK remap to one migration row's raw values.
    /// Returns `Some` row with target-side values substituted for
    /// each User-FK column whose source value matched in
    /// `UserRemapContext.Mapping`; returns `None` if any User-FK
    /// column's source value is unmatched (V1 "diagnostic + skip"
    /// parity). Non-integer User-FK values (NULL sentinel; raw
    /// empty string per `RawValueCodec`) pass through unrewritten.
    let private rewriteUserFkColumns
        (userRemap: UserRemapContext)
        (userFks: Set<Name>)
        (row: MigrationDependencyRow)
        : MigrationDependencyRow option =
        if Set.isEmpty userFks then Some row
        else
            let folder
                (acc: Map<Name, string> option)
                (colName: Name)
                : Map<Name, string> option =
                match acc with
                | None -> None
                | Some values ->
                    match Map.tryFind colName values with
                    | None -> Some values  // column absent from row
                    | Some raw ->
                        // Empty raw = NULL sentinel (per
                        // RawValueCodec); pass through.
                        if System.String.IsNullOrWhiteSpace raw then
                            Some values
                        else
                            match System.Int32.TryParse raw with
                            | true, n ->
                                let source = SourceUserId.ofInt n
                                match UserRemapContext.tryFindTarget source userRemap with
                                | Some target ->
                                    let targetRaw = sprintf "%d" (TargetUserId.value target)  // LINT-ALLOW: terminal integer-to-raw projection at the row-Values boundary; `sprintf "%d"` formats the typed `TargetUserId` integer into the raw IR-string slot that `migrationRowToTypedValues` consumes downstream; same architectural shape as `Render.formatSqlLiteral`'s typed-value-to-raw projection
                                    Some (Map.add colName targetRaw values)
                                | None ->
                                    // Source user unmatched →
                                    // skip the entire row. The
                                    // diagnostic was already
                                    // emitted by UserFkReflowPass.
                                    None
                            | false, _ ->
                                // Non-integer User-FK value
                                // (unexpected but defensive).
                                Some values
            userFks
            |> Set.fold folder (Some row.Values)
            |> Option.map (fun vs -> { row with Values = vs })

    /// Build one `DataInsertScript` for a kind's migration rows.
    /// Empty per-kind context produces a no-op script (T11
    /// preserved). Cycle-aware Phase-1/Phase-2 dispatch identical
    /// to `StaticSeedsEmitter.kindToScript` (the slice δ shape).
    /// Slice η: User-FK columns rewritten via `UserRemapContext`;
    /// rows with unmatched source users are filtered out (V1
    /// "diagnostic + skip" parity).
    let private kindToScript
        (cdc: CdcAwareness)
        (cycleMembers: Set<SsKey>)
        (userRemap: UserRemapContext)
        (rowsByKind: Map<SsKey, MigrationDependencyRow list>)
        (k: Kind)
        : DataInsertScript =
        let rawRows = Map.tryFind k.SsKey rowsByKind |> Option.defaultValue []
        let userFks = userFkColumnNames k
        // Slice η: apply User-FK rewrite per row; rows with
        // unmatched source users drop out.
        let rows = rawRows |> List.choose (rewriteUserFkColumns userRemap userFks)
        if List.isEmpty rows then
            { Phase1Merges  = []
              Phase2Updates = []
              RenderedPhase1 = ""
              RenderedPhase2 = ""
              Rendered      = "" }
        else
            let cdcAware = CdcAwareness.isEnabled k.SsKey cdc
            let deferred = deferredColumns cycleMembers k
            let typeLookup = columnTypeLookup k
            // Slice κ pillar 1 lift: project raw rows into typed
            // SqlLiteral form once at construction.
            let typedRows =
                rows
                |> List.map (fun row ->
                    row.Identifier,
                    migrationRowToTypedValues typeLookup k.Attributes row)
            let renderedPhase1 =
                renderMerge cdcAware deferred k (typedRows |> List.map snd)
            let renderedPhase2 =
                if Set.isEmpty deferred then ""
                else
                    typedRows
                    |> List.map (fun (_, vs) -> renderUpdate k deferred vs)
                    |> System.String.Concat  // LINT-ALLOW: terminal Phase-2 cross-row UPDATE concatenation (chapter 4.1.B slice ι; mirror of StaticSeedsEmitter); each segment is the ScriptDom-rendered + GO-batched UPDATE for one row; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary
            let rendered =
                System.String.Concat(renderedPhase1, renderedPhase2)  // LINT-ALLOW: terminal per-kind concatenation of ScriptDom-rendered Phase-1 + Phase-2 strings (chapter 4.1.B slice κ; mirror of StaticSeedsEmitter); both segments are typed-AST outputs already terminated by `;\nGO\n`
            let mkRow (identifier: SsKey) (values: Map<Name, SqlLiteral>) : DataInsertRow =
                { KindKey       = k.SsKey
                  Identifier    = identifier
                  Values        = values
                  DeferredFkSet = deferred }
            let phase1Rows = typedRows |> List.map (fun (id, vs) -> mkRow id vs)
            let phase2Rows =
                if Set.isEmpty deferred then []
                else typedRows |> List.map (fun (id, vs) -> mkRow id vs)
            { Phase1Merges   = phase1Rows
              Phase2Updates  = phase2Rows
              RenderedPhase1 = renderedPhase1
              RenderedPhase2 = renderedPhase2
              Rendered       = rendered }

    /// Π_MigrationDependencies emit (composer-facing; hoisted topo).
    /// Per A18 amended (`Catalog × Profile` × sibling-evidence input,
    /// never `Policy`) and T11 (every kind in the keyset).
    let emitWithTopo
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        (userRemap: UserRemapContext)
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
            |> List.map (fun k -> k.SsKey, kindToScript cdc cycleMembers userRemap rowsByKind k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Π_MigrationDependencies emit (standalone). Convenience for
    /// callers that don't go through the `DataEmissionComposer`.
    /// Computes the topological order internally and delegates to
    /// `emitWithTopo` — same algebra, one extra `TopologicalOrderPass`
    /// invocation per call. `userRemap` defaults to empty (slice η:
    /// emitter integration is structurally complete but most callers
    /// don't yet supply a populated remap context).
    let emit
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo topo catalog profile context UserRemapContext.empty

    // Chapter 4.7 cleanup: `emitWithUserRemap` retired as
    // overdifferentiated middle-tier. Callers run
    // `TopologicalOrderPass.runWith TreatAsCycle` explicitly and use
    // `emitWithTopo` directly (which takes the precomputed topo +
    // both contexts). The composer (`DataEmissionComposer.composeFull`)
    // is the canonical pipeline entry point; hoists the topo once.
