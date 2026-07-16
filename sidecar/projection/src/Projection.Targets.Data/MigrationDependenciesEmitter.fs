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
// **Profile-shaped sibling input** rather than `Policy`. The emitter is
// pure F# in Core-adjacent; the boundary adapter lives at the pipeline
// layer.
//
// **Ingestion adapter (cashed out 2026-06-15).** The slice-ε boundary-
// adapter deferral fired: `Projection.Pipeline.MigrationDependenciesBinding
// .fromConfig` reads the operator-curated file at
// `overrides.migrationDependencies.path` (JSON, logical-keyed — see that
// module) into this typed shape, resolving logical `(Module, Entity)` to
// `SsKey` against the catalog. The config-driven full-export run threads
// the resolved context through the composer (publish + store-leg). Direct
// consumers (canary / golden tests) still construct the context
// programmatically or pass `MigrationDependencyContext.empty`.
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
        /// Column-name → raw cell. Same shape as `StaticRow.Values`
        /// so static-vs-migration provenance is the only structural
        /// distinction. WP-3 (F11): `None` is SQL NULL; the config
        /// file's documented `"" = NULL` convention maps to `None`
        /// at the binding parse.
        Values     : Map<Name, string option>
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
    /// configured — the ingestion adapter (`MigrationDependenciesBinding
    /// .fromConfig`, cashed out 2026-06-15 per the header above) is
    /// opt-in via `overrides.migrationDependencies.path`; direct
    /// consumers (canary / golden tests) still pass this when they
    /// don't need the channel. `MigrationDependenciesEmitter` against
    /// this context produces empty `Phase1Merges` / `Phase2Updates`
    /// for every kind (T11 keyset preserved).
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

    // The per-kind column vocabulary (columnTypeLookup / writableAttributes /
    // orderedColumnNames / pkColumnNames / updatableColumnNames) is shared with
    // `StaticSeedsEmitter` + `StagedMerge` and lives in
    // `Projection.Core.KindColumns` (extracted at the third consumer).

    // Deferred-FK selection moved to `DataLoadPlan.build` — the plan's
    // `Loads[i].DeferredFkColumns` carries the result. The historical
    // private helper retired at the convergence.

    // The raw-row → typed-`SqlLiteral` projection lives in
    // `Projection.Core.KindColumns.rowToTypedValues` (shared with the
    // static-seed lane; `DataLoadPlan` converged both row shapes into `StaticRow`).

    // The deferred-aware VALUES-clause literal projection is shared:
    // `Projection.Core.KindColumns.typedValuesToSqlLiterals`.

    // The Phase-1 MERGE / Phase-2 UPDATE rendering moved to the shared
    // `MergeRender` module (this lane's copies were byte-identical to
    // StaticSeedsEmitter's modulo the Bench label — and had drifted to a swapped
    // `deferred`/`bracketIdentity` arg order, now removed). This lane passes
    // `"emit.migrationDeps"` as the Bench prefix.

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
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (kind: Kind)
        (load: DataLoadKind)
        : DataInsertScript =
        // The migration lane honors all three optional axes: verification,
        // delete-scope, AND staging (via the shared `StagedMerge` rendering —
        // 2026-06-25, closing the lane's 8623 scale wall).
        let verification = opts.Verification
        let staging = opts.Staging
        let deleteScope = opts.DeleteScope
        if List.isEmpty load.Rows then
            { Phase1Merges  = []
              Phase2Updates = []
              RenderedPhase1 = ""
              RenderedPhase2 = ""
              Rendered      = "" }
        else
            let cdcAware = CdcAwareness.isEnabled kind.SsKey cdc
            // AC-D7 — per-kind scope resolution; mirrors
            // `StaticSeedsEmitter.kindToScript`.
            let scopeForKind : DeleteScope option =
                deleteScope
                |> Option.bind (DeleteScopePolicy.resolveFor kind)
                |> Option.bind DeleteScope.create
            let deferred = load.DeferredFkColumns
            let typeLookup = KindColumns.columnTypeLookup kind
            let typedRows =
                load.Rows
                |> List.map (fun row ->
                    row.Identifier,
                    KindColumns.rowToTypedValues typeLookup kind.Attributes row)
            // NM-25 / NM-26 — bracket the Phase-1 MERGE with SET IDENTITY_INSERT
            // whenever the kind carries ANY IDENTITY column, via the
            // single-sourced `IdentityDisposition.needsIdentityInsert` predicate
            // shared with StaticSeedsEmitter + StaticPopulationEmitter. Gating on
            // `AssignedBySink` (PK-IDENTITY only) missed a non-PK IDENTITY column
            // whose explicit value the all-column INSERT still writes — a deploy
            // rejection of the same family NM-25 closed for the PK case.
            let bracketIdentity =
                IdentityDisposition.needsIdentityInsert kind
            // PL-3 (S61/S20/S59) — mirror of StaticSeedsEmitter: the
            // values-only projection and the row count bind ONCE for both
            // phases.
            let valueRows = typedRows |> List.map snd
            let rowCount = List.length typedRows
            let renderedPhase1 =
                MergeRender.renderMerge "emit.migrationDeps" verification staging scopeForKind cdcAware deferred bracketIdentity kind valueRows
            let renderedPhase2 =
                if Set.isEmpty deferred then ""
                elif DataStagingPolicy.shouldStage staging rowCount then
                    // Set-based escalation above the SAME staging threshold (mirror
                    // of StaticSeedsEmitter): the N per-row UPDATEs collapse to ONE
                    // `UPDATE … FROM target JOIN #fk` via the shared StagedMerge.
                    StagedMerge.renderStagedPhase2 "emit.migrationDeps" cdcAware (TableId.withoutCatalog kind.Physical) kind deferred valueRows
                else
                    // PL-3 (S27/S57) — per-kind constants prebound once.
                    let renderRow = MergeRender.renderUpdateForKind "emit.migrationDeps" cdcAware kind deferred
                    typedRows
                    |> Bench.iterMap "emit.migrationDeps.phase2Row" (fun (_, vs) -> renderRow vs)
                    |> System.String.Concat  // LINT-ALLOW: terminal Phase-2 cross-row UPDATE concatenation (chapter 4.1.B slice ι; mirror of StaticSeedsEmitter); each segment is the ScriptDom-rendered + GO-batched UPDATE for one row; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary
            let rendered =
                System.String.Concat(renderedPhase1, renderedPhase2)  // LINT-ALLOW: terminal per-kind concatenation of ScriptDom-rendered Phase-1 + Phase-2 strings (chapter 4.1.B slice κ; mirror of StaticSeedsEmitter); both segments are typed-AST outputs already terminated by `;\nGO\n`
            let mkRow (identifier: SsKey) (values: Map<Name, SqlLiteral>) : DataInsertRow =
                { KindKey       = kind.SsKey
                  Identifier    = identifier
                  Values        = values
                  DeferredFkSet = deferred }
            let phase1Rows = typedRows |> List.map (fun (id, vs) -> mkRow id vs)
            // PL-3 (S18) — Phase-2's row list IS Phase-1's (mirror).
            let phase2Rows =
                if Set.isEmpty deferred then []
                else phase1Rows
            { Phase1Merges   = phase1Rows
              Phase2Updates  = phase2Rows
              RenderedPhase1 = renderedPhase1
              RenderedPhase2 = renderedPhase2
              Rendered       = rendered }

    /// Π_MigrationDependencies emit (canonical; plan-consuming,
    /// scope-bearing). Realizes the supplied `DataLoadPlan` as per-kind
    /// MERGE/UPDATE scripts; the plan carries post-substitution rows (the
    /// User-FK remap was applied at `DataLoadPlan.build`). DataIntent
    /// end-to-end — operator opinion landed once at plan-build.
    ///
    /// `deleteScope` gates the `WHEN NOT MATCHED BY SOURCE … DELETE` arm
    /// (per `DeleteScopePolicy`): `None` (the `emitFromPlan` default)
    /// emits an upsert-only MERGE byte-identical to the pre-scope form;
    /// `Some policy` activates the convergent-delete arm on every kind
    /// the policy resolves against (`DeleteScopePolicy.resolveFor`). The
    /// emitter consumes the scope VALUE, never `Policy` (A18 amended);
    /// the composer threads it from `EmissionPolicy.DeleteScope`.
    let emitFromPlan
        (opts: DataEmitOptions)
        (catalog: Catalog)
        (profile: Profile)
        (plan: DataLoadPlan)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emitFromPlan"
        let cdc = profile.CdcAwareness
        let loadByKind = plan.Loads |> List.map (fun l -> l.Kind, l) |> Map.ofList
        let emptyScript : DataInsertScript =
            { Phase1Merges = []; Phase2Updates = []; RenderedPhase1 = ""; RenderedPhase2 = ""; Rendered = "" }
        ArtifactByKind.perKindBenched "emit.migrationDeps.kind" catalog (fun k ->
            match Map.tryFind k.SsKey loadByKind with
            | Some load -> kindToScript opts cdc k load
            | None      -> emptyScript)

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
        (opts: DataEmitOptions)
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emitWithTopo"
        let plan = buildPlan catalog topo context userRemap
        emitFromPlan opts catalog profile plan

    /// Π_MigrationDependencies emit (standalone). Convenience for callers that
    /// don't go through the `DataEmissionComposer`. Computes the topological
    /// order internally and delegates to `emitWithTopo` with
    /// `UserRemapContext.empty`. Pass `DataEmitOptions.defaults` for defaults.
    let emit
        (opts: DataEmitOptions)
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.migrationDeps.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo opts topo catalog profile context UserRemapContext.empty

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
