namespace Projection.Targets.Data

open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT  // LINT-ALLOW: cross-target dependency for ScriptDom MERGE + UPDATE typed-AST construction (`ScriptDomBuild.buildMergeStatement` slice α; `ScriptDomBuild.buildUpdateStatement` slice δ; `buildSqlLiteral`) per the Tier-1 #1 transition (RawTextEmitter retirement arc cash-out) and the chapter-4.1.B slice-δ extension; the typed AST flows through `ScriptDomGenerate.generateOne` for canonical SQL-text rendering; same architectural shape that SsdtDdlEmitter uses (chapter 4.1.A)

/// Π_StaticSeeds — chapter 4.1.B slice α emitter for static-modality
/// kinds. Consumes the `Catalog`'s `Modality.Static` populations and
/// produces idempotent MERGE statements per V1 trunk's `StaticSeed
/// SqlBuilder.cs:211-260` shape (V1 parity at slice α; the change-
/// detection predicate that closes CDC-noise lands at slice β; the
/// two-phase insertion / cycle-breaking pattern lands at slice δ per
/// V1's `PhasedDynamicEntityInsertGenerator.cs:88-148`).
///
/// **A18 amended.** The canonical entry `emitFromPlan` carries
/// `Catalog × Profile × DataLoadPlan`. The `DataLoadPlan` is the
/// post-substitution view — `DataLoadPlan.build` is the single
/// `OperatorIntent Insertion` site for the entire data-load family
/// (identity-substitution applied once), so this emitter classifies
/// entirely `DataIntent`. No `Policy` parameter — DataComposition
/// dispatch happens in the composer (slice η), not here.
///
/// **T11 sibling-Π commutativity.** The emitter produces an
/// `ArtifactByKind<DataInsertScript>` keyed by every catalog kind.
/// Kinds without `Modality.Static` produce a script with empty
/// `Phase1Merges` (no-op artifact) — per the strict-equality T11
/// invariant: every kind appears, no kind is silently absent.
[<RequireQualifiedAccess>]
module StaticSeedsEmitter =

    [<Literal>]
    let version : int = 1

    // The per-kind column vocabulary (columnTypeLookup / writableAttributes /
    // orderedColumnNames / pkColumnNames / updatableColumnNames) is shared with
    // `MigrationDependenciesEmitter` + `StagedMerge` and lives in
    // `Projection.Core.KindColumns` (extracted at the third consumer).

    // -------------------------------------------------------------------
    // Slice δ — cycle-membership detection + Phase-2 deferred-FK set.
    //
    // V1's empirical reference: `PhasedDynamicEntityInsertGenerator.cs:
    // 88-148` + `IdentifyNullableFKColumns` (line 150). Predicate: an
    // FK column is deferred IFF its target kind is in the same cycle
    // AND the source attribute's column is nullable. NOT-NULL FKs in
    // a cycle cannot be deferred (NULLing would violate the column
    // constraint); they surface as unresolved cycles in
    // `TopologicalOrder.Cycles` for the operator.
    //
    // The set of cycle members comes from the topological-order pass'
    // `Cycles : CycleDiagnostic list` field: every member of every SCC
    // (resolved or unresolved) participates in a cycle and may need
    // Phase-1 deferral on its outbound FKs to same-SCC peers.
    // -------------------------------------------------------------------

    // The raw-row → typed-`SqlLiteral` projection lives in
    // `Projection.Core.KindColumns.rowToTypedValues` (shared with the
    // migration lane).

    /// Project the typed-Values row into the `SqlLiteral list` form
    /// `MergeBuildArgs.Rows` expects. Iterates the kind's attributes
    /// in declared order. Slice δ: columns named in `deferred` are
    /// emitted as `SqlLiteral.NullLit` regardless of the row's
    /// typed value (Phase-1 cycle-break).
    // The deferred-aware VALUES-clause literal projection is shared:
    // `Projection.Core.KindColumns.typedValuesToSqlLiterals`.

    // -------------------------------------------------------------------
    // Staged-source form (the error-8623-safe MERGE for large kinds). The
    // inline `USING (VALUES …)` constructor exceeds the optimizer's
    // plan-complexity limit at ~30k rows (measured 2026-06-25; a MERGE's
    // limit is lower than a plain INSERT's because it adds join + match /
    // insert / delete arms). Above the threshold, rows stage through a
    // `#temp` in one atomic, GO-free batch; below, the inline form stands
    // (byte-identical — fixtures are ≤3 rows).
    // -------------------------------------------------------------------

    // The staging decision is the operator's `emission.dataStaging` posture
    // (`DataStagingPolicy.shouldStage`, default `auto` > 1000 rows) threaded from
    // the composer — see `renderMerge` / `kindToScript`. The staged-`#temp`
    // RENDERING (`#seed_` Phase-1 batch, `#fk_` set-based Phase-2) lives in the
    // shared `StagedMerge` module — `MigrationDependenciesEmitter` is its second
    // consumer (the verb extracted at the second consumer).


    // The Phase-1 MERGE / Phase-2 UPDATE rendering moved to the shared
    // `MergeRender` module (collapsed with MigrationDependenciesEmitter's
    // byte-identical copies; this lane passes `"emit.staticSeeds"` as the Bench
    // prefix). `MergeRender.renderMerge` / `MergeRender.renderUpdate`.

    /// Build one `DataInsertScript` for a kind. Empty-population kinds
    /// produce a no-op script (empty Phase1Merges, empty Rendered);
    /// per T11 strict-equality keyset, the script is still keyed in
    /// the artifact map. CDC-aware dispatch per slice β: the kind's
    /// `Profile.CdcAwareness.CdcEnabled` membership selects the
    /// change-detection-predicate variant. Slice δ adds cycle-aware
    /// dispatch: when the kind participates in a cycle and has
    /// nullable FKs to same-SCC peers, those FK columns are deferred
    /// (NULLed in the Phase-1 MERGE; populated in Phase-2 per-row
    /// UPDATEs). For non-cycle / no-deferred-FK kinds the slice-δ
    /// path is byte-identical to slice α/β output.
    ///
    /// **Per-kind `Rendered` scope.** The kind's MERGE is followed by
    /// its per-row Phase-2 UPDATEs in the rendered text. For self-
    /// referencing FK cases (one kind, FK to itself) this is fully
    /// deploy-correct: Phase-1 INSERTs all rows with FK = NULL,
    /// Phase-2 self-UPDATEs each row to its target PK. For multi-
    /// kind cycles correctness rests on the composer (slice η) to
    /// globally interleave Phase-1 across all kinds before any
    /// Phase-2 — the per-kind `Rendered` is correct only as a
    /// compositional input under that orchestration.
    /// The script value every no-rows path shares (also `emitFromPlan`'s
    /// value for plan-absent kinds).
    let private emptyScript : DataInsertScript =
        { Phase1Merges  = []
          Phase2Updates = []
          RenderedPhase1 = ""
          RenderedPhase2 = ""
          Rendered      = "" }

    /// The render core over ALREADY-TYPED rows — the one implementation
    /// both row carriers project into (A40: the row source is the single
    /// varying axis; the named-row and positional entries below cannot
    /// drift on MERGE semantics).
    let private scriptOfTyped
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (kind: Kind)
        (deferred: Set<Name>)
        (typedRows: (SsKey * Map<Name, SqlLiteral>) list)
        : DataInsertScript =
        let verification = opts.Verification
        let staging = opts.Staging
        let deleteScope = opts.DeleteScope
        if List.isEmpty typedRows then emptyScript
        else
            let cdcAware = CdcAwareness.isEnabled kind.SsKey cdc
            // AC-D7 — resolve the operator's scope against THIS kind: the
            // delete arm renders exactly when every term column is an
            // attribute here (`DeleteScopePolicy.resolveFor`); a kind
            // outside the scope keeps the upsert-only MERGE.
            let scopeForKind : DeleteScope option =
                deleteScope
                |> Option.bind (DeleteScopePolicy.resolveFor kind)
                |> Option.bind DeleteScope.create
            // NM-26 — bracket the Phase-1 MERGE with `SET IDENTITY_INSERT`
            // whenever the kind carries ANY IDENTITY column, via the
            // single-sourced `IdentityDisposition.needsIdentityInsert`
            // predicate shared with StaticPopulationEmitter +
            // MigrationDependenciesEmitter. Prior to NM-26 this gated on
            // `load.Disposition = AssignedBySink` (PK-IDENTITY only), which
            // failed to bracket a non-PK IDENTITY column whose explicit
            // value the MERGE's all-column INSERT still writes — a deploy
            // rejection of the same family as NM-25. The disposition still
            // drives the remap/MERGE strategy; it just no longer doubles as
            // the bracketing theory.
            let bracketIdentity =
                IdentityDisposition.needsIdentityInsert kind
            // PL-3 (S61/S20/S59) — the values-only projection and the row
            // count bind ONCE and serve both phases (each was re-derived by
            // the Phase-2 branch).
            let valueRows = typedRows |> List.map snd
            let rowCount = List.length typedRows
            // v7 slice 5 (DECISIONS 2026-07-18) — the EXACT repair set:
            // Phase-2 touches only rows carrying at least one non-NULL
            // deferred value. A row whose deferred columns are all NULL
            // was landed whole by Phase-1 — updating it re-set NULL over
            // NULL, inflating the load's norm (T15) for nothing. The
            // ledger becomes exact: ‖phase2‖ = |repairRows|.
            let repairRows =
                if Set.isEmpty deferred then []
                else
                    typedRows
                    |> List.filter (fun (_, vs) ->
                        deferred
                        |> Set.exists (fun c ->
                            match Map.tryFind c vs with
                            | Some SqlLiteral.NullLit | None -> false
                            | Some _ -> true))
            let renderedPhase1 =
                MergeRender.renderMerge "emit.staticSeeds" verification staging scopeForKind cdcAware deferred bracketIdentity kind valueRows
            let renderedPhase2 =
                if List.isEmpty repairRows then ""
                elif DataStagingPolicy.shouldStage staging rowCount then
                    // Set-based escalation (Step 4): above the SAME staging
                    // threshold that routes Phase-1 through a `#temp`, Phase-2's
                    // N per-row UPDATEs collapse to ONE `UPDATE … FROM target
                    // JOIN #fk` — a kind is treated coherently across both phases
                    // by one threshold (the ROUTE judges by Phase-1's row count;
                    // the staged rows are the repair set only). The narrow `#fk`
                    // temp carries the real deferred-FK values; Phase-1 already
                    // inserted the rows with those columns NULLed.
                    StagedMerge.renderStagedPhase2 "emit.staticSeeds" cdcAware (TableId.withoutCatalog kind.Physical) kind deferred (repairRows |> List.map snd)
                else
                    // PL-3 (S27/S57) — the per-kind constants prebind once;
                    // the row loop threads only the typed values.
                    let renderRow = MergeRender.renderUpdateForKind "emit.staticSeeds" cdcAware kind deferred
                    repairRows
                    |> Bench.iterMap "emit.staticSeeds.phase2Row" (fun (_, vs) -> renderRow vs)
                    |> System.String.Concat  // LINT-ALLOW: terminal Phase-2 cross-row UPDATE concatenation (chapter 4.1.B slice ι); each segment is the ScriptDom-rendered + GO-batched UPDATE for one row; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary; the typed `Statement` DU does not yet model UPDATE so `ScriptDomGenerate.toText` is not applicable
            let rendered =
                System.String.Concat(renderedPhase1, renderedPhase2)  // LINT-ALLOW: terminal per-kind concatenation of ScriptDom-rendered Phase-1 + Phase-2 strings (chapter 4.1.B slice κ; same architectural shape as slice δ's per-kind rendering); both segments are typed-AST outputs already terminated by `;\nGO\n`
            let mkRow (identifier: SsKey) (values: Map<Name, SqlLiteral>) : DataInsertRow =
                { KindKey       = kind.SsKey
                  Identifier    = identifier
                  Values        = values
                  DeferredFkSet = deferred }
            let phase1Rows = typedRows |> List.map (fun (id, vs) -> mkRow id vs)
            // v7 slice 5 — Phase-2's row list is the REPAIR set (rows with
            // a non-NULL deferred value), not Phase-1's whole list.
            let phase2Rows =
                repairRows |> List.map (fun (id, vs) -> mkRow id vs)
            { Phase1Merges   = phase1Rows
              Phase2Updates  = phase2Rows
              RenderedPhase1 = renderedPhase1
              RenderedPhase2 = renderedPhase2
              Rendered       = rendered }

    /// Render one plan load (the named-row carrier). The plan carries
    /// POST-substitution rows and the deferred-FK set; this entry
    /// type-lifts the rows once (slice κ pillar-1 lift) and delegates to
    /// the shared core. `ReconciledByRule` loads carry empty rows by
    /// plan-build and produce empty scripts; `PreservedFromSource` and
    /// `AssignedBySink` both render MERGE over the supplied rows with the
    /// IDENTITY PK kept (the MERGE's `ON` joins on it); NM-26 brackets
    /// IDENTITY-bearing kinds regardless of disposition (see the core).
    /// Apply the OutSystems single-space sentinel to one row's cells,
    /// staged per kind (the attribute lookup binds once, not per cell).
    /// Static-seed lane ONLY — see `KindColumns.outSystemsSpaceSentinel`.
    let private applySpaceSentinel (kind: Kind) : StaticRow -> StaticRow =
        let attrByName = kind.Attributes |> List.map (fun a -> a.Name, a) |> Map.ofList
        fun row ->
            { row with
                Values =
                    row.Values
                    |> Map.map (fun name cell ->
                        match Map.tryFind name attrByName with
                        | Some a -> KindColumns.outSystemsSpaceSentinel a cell
                        | None -> cell) }

    let private kindToScript
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (kind: Kind)
        (load: DataLoadKind)
        : DataInsertScript =
        if List.isEmpty load.Rows then emptyScript
        else
            let typeLookup = KindColumns.columnTypeLookup kind
            let sentinel = applySpaceSentinel kind
            let typedRows =
                load.Rows
                |> List.map (fun row ->
                    let row = sentinel row
                    row.Identifier,
                    KindColumns.rowToTypedValues typeLookup kind.Attributes row)
            scriptOfTyped opts cdc kind load.DeferredFkColumns typedRows

    /// Render one kind's script straight from the positional in-flight
    /// carrier (`RowQuantum`) — the slim-row sibling of `renderLoad`,
    /// skipping the IR rebuild (no per-row `Map<Name, string>` mint): the
    /// quantum's cells type-lift positionally
    /// (`KindColumns.quantumToTypedValues`), and each row's identity mints
    /// through the SAME `StaticRow.readsideIdentity` the IR-grain boundary
    /// uses, so the output equals `renderLoad` over materialized rows at
    /// FULL record grain, not just rendered text (pinned in the pure
    /// pool). `deferred` is the topology-derived cycle-break set
    /// (`TopologicalOrder.deferredFkColumns` — rows-independent, known
    /// before acquisition). Caller gate: quanta carry POST-substitution
    /// semantics only when no identity substitution applies (the empty
    /// remap) — a populated `SurrogateRemapContext` needs the named-row
    /// plan path.
    let renderQuanta
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (kind: Kind)
        (deferred: Set<Name>)
        (quanta: RowQuantum list)
        : DataInsertScript =
        if List.isEmpty quanta then emptyScript
        else
            let typeLookup = KindColumns.columnTypeLookup kind
            let schemaText = TableId.schemaText kind.Physical
            let tableText  = TableId.tableText kind.Physical
            // The single-space sentinel at the quantum grain: cells are
            // positional against the kind's attribute order.
            let attrArr = List.toArray kind.Attributes
            let sentinelQuantum (q: RowQuantum) : RowQuantum =
                { Cells =
                    q.Cells
                    |> Array.mapi (fun i c ->
                        if i < attrArr.Length then
                            KindColumns.outSystemsSpaceSentinel attrArr.[i] (ValueOption.toOption c)
                            |> Option.toValueOption
                        else c) }
            let typedRows =
                quanta
                |> List.mapi (fun idx q ->
                    StaticRow.readsideIdentity schemaText tableText idx,
                    KindColumns.quantumToTypedValues typeLookup kind.Attributes (sentinelQuantum q))
            scriptOfTyped opts cdc kind deferred typedRows

    /// Π_StaticSeeds emit (composer-facing). Per A18 amended
    /// (`Catalog × Profile`, never `Policy`) and T11 (every kind in
    /// the keyset). Per the chapter-4.1.B slice-η composer
    /// integration, the `topo : TopologicalOrder` argument is
    /// **hoisted** — the composer (`DataEmissionComposer.compose`)
    /// runs `TopologicalOrderPass` once per pipeline and threads
    /// the result to every emitter, so the O(N+E) Tarjan + Kahn cost
    /// is amortized across the data triumvirate rather than paid
    /// per-emitter.
    ///
    /// Slice β (CDC dispatch): the kind's `Profile.CdcAwareness.
    /// CdcEnabled` membership selects the change-detection-predicate
    /// MERGE variant. Slice δ (cycle-breaking): kinds in
    /// `topo.Cycles` defer their nullable same-SCC FK columns across
    /// the two-phase MERGE/UPDATE pattern.
    /// Π_StaticSeeds emit (canonical; plan-consuming, scope-bearing).
    /// Realizes the supplied `DataLoadPlan` as per-kind MERGE/UPDATE
    /// scripts: the plan carries POST-substitution rows + the
    /// deferred-FK set per kind; this entry just renders. Realization is
    /// `DataIntent` end-to-end — operator-supplied identity substitution
    /// landed once at `DataLoadPlan.build`. Kinds absent from the plan
    /// (no load) produce empty scripts per T11.
    ///
    /// `deleteScope` gates the `WHEN NOT MATCHED BY SOURCE … DELETE` arm
    /// (per `DeleteScopePolicy`): `None` (the `emitFromPlan` default)
    /// emits an upsert-only MERGE byte-identical to the pre-scope form;
    /// `Some policy` activates the convergent-delete arm on every kind
    /// the policy resolves against (`DeleteScopePolicy.resolveFor`). The
    /// emitter consumes the scope VALUE, never `Policy` (A18 amended);
    /// the composer threads it from `EmissionPolicy.DeleteScope`.
    /// Π_StaticSeeds emit (canonical; plan-consuming). Realizes the supplied
    /// `DataLoadPlan` as per-kind MERGE/UPDATE scripts under the operator's
    /// `DataEmitOptions` (delete scope / drift-guard / staging). The single
    /// plan-consuming entry — the prior `emitFromPlanWith` / `…WithVerification`
    /// / `…WithStaging` telescoping collapsed into the one options record.
    /// `DataEmitOptions.defaults` reproduces the pre-consolidation default
    /// (no delete arm, `Standard`, `auto` staging — byte-identical).
    /// Render ONE plan load — the per-kind unit `emitFromPlan` maps over
    /// the catalog keyset, exposed so an acquisition-overlapped realization
    /// can render a kind's script the moment its rows land (per-kind MERGE
    /// text depends only on the options, the kind's CDC membership, the
    /// kind itself, and its own load — never on another kind's rows;
    /// cross-kind order is the ASSEMBLY's concern and stays topological).
    /// Byte-identity with the batch path is BY CONSTRUCTION: `emitFromPlan`
    /// calls this same function per kind. An empty-`Rows` load renders the
    /// empty script (the same value `emitFromPlan` uses for plan-absent
    /// kinds).
    let renderLoad
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (kind: Kind)
        (load: DataLoadKind)
        : DataInsertScript =
        kindToScript opts cdc kind load

    let emitFromPlan
        (opts: DataEmitOptions)
        (catalog: Catalog)
        (profile: Profile)
        (plan: DataLoadPlan)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emitFromPlan"
        let cdc = profile.CdcAwareness
        let loadByKind = plan.Loads |> List.map (fun l -> l.Kind, l) |> Map.ofList
        ArtifactByKind.perKindBenched "emit.staticSeeds.kind" catalog (fun k ->
            match Map.tryFind k.SsKey loadByKind with
            | Some load -> kindToScript opts cdc k load
            | None      -> emptyScript)

    /// Π_StaticSeeds emit (composer-facing; hoisted topo). Builds the plan from
    /// `Kind.staticPopulations` per kind with the empty remap (the static-seeds
    /// row source is catalog-resident evidence; operators wanting identity
    /// substitution build the plan themselves via `DataLoadPlan.build` +
    /// `emitFromPlan`), then delegates to `emitFromPlan` under `opts`.
    let emitWithTopo
        (opts: DataEmitOptions)
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emitWithTopo"
        let rawRows =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey, Kind.staticPopulations k)
            |> Map.ofList
        let plan = DataLoadPlan.build catalog topo rawRows SurrogateRemapContext.empty
        emitFromPlan opts catalog profile plan

    /// Π_StaticSeeds emit (standalone). Convenience for callers that don't go
    /// through the `DataEmissionComposer` (canary tests, direct-Π integration
    /// tests). Computes the topological order internally and delegates to
    /// `emitWithTopo`. Pass `DataEmitOptions.defaults` for the default posture.
    let emit
        (opts: DataEmitOptions)
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo opts topo catalog profile

    /// Harvest-discipline classification per pillar 9 (chapter 5.13
    /// slice data-emission-registry). Three sites covering the
    /// emitter's surfaces; all `DataIntent` — the emitter consumes
    /// `Catalog × Profile` only (per A18 amended), and Profile's
    /// `CdcAwareness` field is evidence-shaped (not operator-supplied
    /// policy). The skeleton-purity property test asserts this
    /// emitter participates in `Project(catalog, Policy.empty,
    /// profile)` without emitting `OperatorIntent` lineage events.
    ///
    /// Per the canonical `RegisteredTransformMetadata` shape used by
    /// emitters (mirrors `CatalogReader.registeredMetadata` from the
    /// adapter precedent — the typed `RegisteredTransform<'In, 'Out>`
    /// shell doesn't cleanly fit emitter signatures with
    /// `ArtifactByKind<_>` outputs + `Result<_, EmitError>` envelopes;
    /// metadata-only registration captures the classification
    /// surface without forcing a Run-binding mismatch).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "staticSeedsEmitter" Data
            [ TransformSite.dataIntent "staticRowsProjection"
                "Emit MERGE statements for plan loads whose `Rows` are non-empty — pure projection of the supplied `DataLoadPlan`. Identity substitution landed once at `DataLoadPlan.build` (the OperatorIntent Insertion site); this realization consumes post-substitution rows and is DataIntent."
              TransformSite.dataIntent "cdcAwareChangeDetection"
                "Per-kind MERGE WHEN MATCHED predicate gates UPDATE on actual column-level differences when `Profile.CdcAwareness.CdcEnabled` carries the kind. Profile is *evidence* (A18 amended; pillar 9 — Profile-driven observations are DataIntent); the CDC predicate IS the data-intent shape, not an operator override. Slice β (chapter 4.1.B) cash-out."
              TransformSite.dataIntent "deferredFkPhase2"
                "Two-phase cycle-breaking — Phase-1 emits MERGEs with deferred FK columns NULLed; Phase-2 UPDATEs populate them once all Phase-1 inserts complete. Cycle membership is structural (from `DataLoadPlan.Loads[i].DeferredFkColumns`); the deferral is topology-derived, not operator-supplied. Slice δ (chapter 4.1.B) cash-out." ]
