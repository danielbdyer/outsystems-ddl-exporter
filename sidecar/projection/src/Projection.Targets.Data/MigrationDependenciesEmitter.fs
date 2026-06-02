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
        k.Attributes |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    let private pkColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> a.IsPrimaryKey)
        |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    let private updatableColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> not a.IsPrimaryKey)
        |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    // Deferred-FK selection moved to `DataLoadPlan.build` — the plan's
    // `Loads[i].DeferredFkColumns` carries the result. The historical
    // private helper retired at the convergence.

    /// Project a `MigrationDependencyRow`'s raw `Map<Name, string>`
    /// into the typed `Map<Name, SqlLiteral>` shape
    /// `DataInsertRow.Values` expects (slice κ pillar 1 lift).
    /// Project a `StaticRow` (the plan's row carrier — `DataLoadPlan`
    /// converged both the migration and static row shapes into one)
    /// into typed `Map<Name, SqlLiteral>`. Mirror of
    /// `StaticSeedsEmitter.staticRowToTypedValues`.
    let private rowToTypedValues
        (typeLookup: Map<Name, PrimitiveType>)
        (attributes: Attribute list)
        (row: StaticRow)
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
        // Slice 5.13.cdc-silence-cross-emitter: mirror of
        // `StaticSeedsEmitter.renderMerge`. Deferred columns are
        // owned by Phase-2; including them in Phase-1's WHEN MATCHED
        // UPDATE branch causes idempotent redeploy to overwrite the
        // target column with the Phase-1 NULL, then Phase-2 sets it
        // back. Filtering deferred from UpdColumns makes Phase-1
        // structurally silent on the deferred-FK axis.
        let updColumns =
            k.Attributes
            |> List.filter (fun a -> not a.IsPrimaryKey)
            |> List.filter (fun a -> not (Set.contains a.Name deferred))
            |> List.map (fun a -> ColumnRealization.columnNameText a.Column)
        let args : ScriptDomBuild.MergeBuildArgs =
            {
                Target     = table
                AllColumns = orderedColumnNames k
                PkColumns  = pkColumnNames k
                UpdColumns = updColumns
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
        (cdcAware: bool)
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
            ColumnRealization.columnNameText a.Column, lit
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
              WhereCells = whereCells
              CdcAware   = cdcAware }
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

    /// Convert a `MigrationDependencyRow` to the `StaticRow` shape the
    /// converged `DataLoadPlan` carries. Drops `KindKey` (already
    /// indexed by the per-kind grouping) and preserves `Identifier` +
    /// `Values` verbatim — both row shapes share the same raw-value
    /// surface.
    let private toStaticRow (row: MigrationDependencyRow) : StaticRow =
        { Identifier = row.Identifier
          Values     = row.Values }

    /// Discover the user kind's `SsKey` from the catalog by scanning
    /// for any reference with `IsUserFk = true`. Per the
    /// `Reference.IsUserFk` docstring the flag is set iff `TargetKind`
    /// resolves to the platform user kind, so the first hit's target
    /// kind names the user kind. `None` when no such reference exists
    /// (the dominant case in V2 today, since the OSSYS adapter's
    /// IsUserFk-from-V1 detection is a chapter-4.2 deferral —
    /// `CatalogReader.fs:1164`).
    let private tryDiscoverUserKind (catalog: Catalog) : SsKey option =
        Catalog.allKinds catalog
        |> List.tryPick (fun k ->
            k.References
            |> List.tryPick (fun r ->
                if r.IsUserFk then Some r.TargetKind else None))

    /// Render one plan load. The plan carries POST-substitution rows
    /// (User-FK values rewritten at plan-build via the
    /// `UserRemap.toSurrogate`→`DataLoadPlan.build` route) and the
    /// deferred-FK set; this function just type-lifts and renders.
    /// Mirrors `StaticSeedsEmitter.kindToScript` exactly — both
    /// emitters realize the same algebra over the same plan shape.
    let private kindToScript
        (cdc: CdcAwareness)
        (kind: Kind)
        (load: DataLoadKind)
        : DataInsertScript =
        if List.isEmpty load.Rows then
            { Phase1Merges  = []
              Phase2Updates = []
              RenderedPhase1 = ""
              RenderedPhase2 = ""
              Rendered      = "" }
        else
            let cdcAware = CdcAwareness.isEnabled kind.SsKey cdc
            let deferred = load.DeferredFkColumns
            let typeLookup = columnTypeLookup kind
            let typedRows =
                load.Rows
                |> List.map (fun row ->
                    row.Identifier,
                    rowToTypedValues typeLookup kind.Attributes row)
            let renderedPhase1 =
                renderMerge cdcAware deferred kind (typedRows |> List.map snd)
            let renderedPhase2 =
                if Set.isEmpty deferred then ""
                else
                    typedRows
                    |> Bench.iterMap "emit.migrationDeps.phase2Row" (fun (_, vs) -> renderUpdate cdcAware kind deferred vs)
                    |> System.String.Concat  // LINT-ALLOW: terminal Phase-2 cross-row UPDATE concatenation (chapter 4.1.B slice ι; mirror of StaticSeedsEmitter); each segment is the ScriptDom-rendered + GO-batched UPDATE for one row; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary
            let rendered =
                System.String.Concat(renderedPhase1, renderedPhase2)  // LINT-ALLOW: terminal per-kind concatenation of ScriptDom-rendered Phase-1 + Phase-2 strings (chapter 4.1.B slice κ; mirror of StaticSeedsEmitter); both segments are typed-AST outputs already terminated by `;\nGO\n`
            let mkRow (identifier: SsKey) (values: Map<Name, SqlLiteral>) : DataInsertRow =
                { KindKey       = kind.SsKey
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

    /// Π_MigrationDependencies emit (canonical; plan-consuming).
    /// Realizes the supplied `DataLoadPlan` as per-kind MERGE/UPDATE
    /// scripts; the plan carries post-substitution rows (the User-FK
    /// remap was applied at `DataLoadPlan.build`). DataIntent
    /// end-to-end — operator opinion landed once at plan-build.
    let emitFromPlan
        (catalog: Catalog)
        (profile: Profile)
        (plan: DataLoadPlan)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emitFromPlan"
        let cdc = profile.CdcAwareness
        let loadByKind = plan.Loads |> List.map (fun l -> l.Kind, l) |> Map.ofList
        let emptyScript : DataInsertScript =
            { Phase1Merges = []; Phase2Updates = []; RenderedPhase1 = ""; RenderedPhase2 = ""; Rendered = "" }
        let slices =
            Catalog.allKinds catalog
            |> Bench.iterMap "emit.migrationDeps.kind" (fun k ->
                let script =
                    match Map.tryFind k.SsKey loadByKind with
                    | Some load -> kindToScript cdc k load
                    | None      -> emptyScript
                k.SsKey, script)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Build the `DataLoadPlan` from the supplied `MigrationDependency
    /// Context` (operator-supplied rows) and `UserRemapContext`
    /// (acquired evidence). Converts the per-kind rows to `StaticRow`
    /// and the `UserRemapContext` to a `SurrogateRemapContext` keyed
    /// under the discovered user kind; then routes through the
    /// single `DataLoadPlan.build` site (the one `OperatorIntent
    /// Insertion` altitude in the data-load family).
    let private buildPlan
        (catalog: Catalog)
        (topo: TopologicalOrder)
        (context: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : DataLoadPlan =
        let rawRows =
            MigrationDependencyContext.rowsByKind context
            |> Map.map (fun _ rows -> rows |> List.map toStaticRow)
        let remap =
            match tryDiscoverUserKind catalog with
            | Some userKindKey ->
                match UserRemapContext.toSurrogate userKindKey userRemap with
                | Ok r    -> r
                | Error _ -> SurrogateRemapContext.empty
            | None -> SurrogateRemapContext.empty
        DataLoadPlan.build catalog topo rawRows remap

    /// Π_MigrationDependencies emit (composer-facing; hoisted topo).
    /// Builds the plan from the migration row source + the
    /// `UserRemapContext`-derived `SurrogateRemapContext`, then
    /// delegates to `emitFromPlan`.
    let emitWithTopo
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emitWithTopo"
        let plan = buildPlan catalog topo context userRemap
        emitFromPlan catalog profile plan

    /// Π_MigrationDependencies emit (standalone). Convenience for
    /// callers that don't go through the `DataEmissionComposer`.
    /// Computes the topological order internally and delegates to
    /// `emitWithTopo` with `UserRemapContext.empty`.
    let emit
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo topo catalog profile context UserRemapContext.empty

    /// Harvest-discipline classification per pillar 9 (chapter 5.13
    /// slice data-emission-registry). Three sites — the structural
    /// emission is `DataIntent` (the per-kind MERGE construction is
    /// pure projection of `Catalog × Profile`), while the two
    /// operator-published inputs (`MigrationDependencyContext` rows;
    /// `UserRemapContext` mapping) are `OperatorIntent Insertion`.
    /// Pillar 9 → V2 splits the "what the operator publishes" axis
    /// from the "how it gets emitted" axis structurally; the
    /// composer threads the operator inputs but the emitter's
    /// emission shape is the same regardless.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "migrationDependenciesEmitter" Data
            [ TransformSite.operatorIntent "migrationRowEmission" Insertion
                "`MigrationDependencyContext.Rows` is operator-published legacy-domain row inventory (pre-scope §2.2: 'environment-specific evidence the migration team supplies'). Each row's `(KindKey, Identifier, Values)` is operator-supplied content that wouldn't be reachable from `Project(catalog, Policy.empty, profile)` — it lands via the operator-supplied context. OverlayAxis = Insertion (what content the catalog gains beyond source evidence). Note: identity-substitution within those rows landed at `DataLoadPlan.identitySubstitution` (the canonical site); this site classifies the *inclusion of the rows themselves*, not their value-rewriting."
              TransformSite.dataIntent "deferredFkPhase2"
                "Two-phase cycle-breaking parallel to `StaticSeedsEmitter` — Phase-1 emits MERGEs with deferred FK columns NULLed; Phase-2 UPDATEs populate them. Cycle membership is structural (topology-derived from `DataLoadPlan.Loads[i].DeferredFkColumns`); the deferral is the same algebra as the static-rows emitter. DataIntent because the cycle-resolution is structural, not operator-supplied." ]
