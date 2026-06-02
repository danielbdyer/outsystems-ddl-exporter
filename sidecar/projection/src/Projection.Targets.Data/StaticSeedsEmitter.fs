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

    /// Type-resolution lookup for a kind's columns. Returns the
    /// (column-name, primitive-type) pair for each attribute, so the
    /// renderer can format raw IR values as SQL literals.
    let private columnTypeLookup (k: Kind) : Map<Name, PrimitiveType> =
        k.Attributes
        |> List.map (fun a -> a.Name, a.Type)
        |> Map.ofList

    /// Order columns deterministically (matches V1 + the SSDT emitter).
    /// Per A33 (deterministic-ordered schema emission), sort by the
    /// kind's declared attribute order — which is itself canonical
    /// after `CanonicalizeIdentity`.
    let private orderedColumnNames (k: Kind) : string list =
        k.Attributes |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    /// Primary-key column names in the kind's declared order. The
    /// MERGE's ON-clause joins on these; the WHEN-NOT-MATCHED INSERT
    /// includes them; the WHEN-MATCHED UPDATE excludes them (PK is
    /// stable per row identity). Phase-2 UPDATEs use the same set
    /// for the WHERE-clause row-scope.
    let private pkColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> a.IsPrimaryKey)
        |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    /// Non-PK column names (the MERGE's UPDATE-target columns).
    let private updatableColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> not a.IsPrimaryKey)
        |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

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

    /// Project a StaticRow's `Map<Name, string>` raw values into the
    /// typed `Map<Name, SqlLiteral>` form `DataInsertRow.Values`
    /// expects (slice κ pillar 1 lift). Resolves each attribute's
    /// `PrimitiveType` once at construction; missing values default
    /// to empty-raw (V2 IR's empty-raw sentinel per `RawValueCodec`,
    /// which `SqlLiteral.ofRaw` maps to `NullLit` for non-text
    /// types).
    let private staticRowToTypedValues
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
    /// `MergeBuildArgs.Rows` expects. Iterates the kind's attributes
    /// in declared order. Slice δ: columns named in `deferred` are
    /// emitted as `SqlLiteral.NullLit` regardless of the row's
    /// typed value (Phase-1 cycle-break).
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

    /// Render the MERGE statement for a kind with its static populations
    /// via ScriptDom's typed-AST + `Sql160ScriptGenerator` pipeline.
    /// Per Tier-1 #1 (RawTextEmitter retirement arc cash-out): the
    /// hand-rolled StringBuilder MERGE construction (with 6 LINT-ALLOWs)
    /// retires in favor of `ScriptDomBuild.buildMergeStatement` —
    /// every node typed, every literal flowing through `SqlLiteral`,
    /// no terminal text composition until the writer boundary.
    ///
    /// Mirrors V1's `StaticSeedSqlBuilder.AppendMergeStatement`
    /// (`StaticSeedSqlBuilder.cs:211-260`) modulo ScriptDom's canonical
    /// formatting (newlines / wrapping). The change-detection predicate
    /// per chapter 4.1.B slice β + pre-scope §6 lands as typed
    /// `BooleanBinaryExpression` / `BooleanIsNullExpression` /
    /// `BooleanComparisonExpression` AST nodes. Slice δ extends with
    /// `deferred : Set<Name>` — the columns NULLed in the MERGE's
    /// VALUES so cycle-participating rows can INSERT before their
    /// same-SCC FK targets exist.
    let private renderMerge
        (cdcAware: bool)
        (deferred: Set<Name>)
        (k: Kind)
        (typedRows: Map<Name, SqlLiteral> list)
        : string =
        use _ = Bench.scope "emit.staticSeeds.renderMerge"
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table; Catalog = None }
        // Slice 5.13.cdc-silence-cross-emitter: exclude deferred
        // columns from WHEN MATCHED UPDATE's UpdColumns. Deferred
        // columns are owned by Phase-2; including them in Phase-1's
        // UPDATE branch causes idempotent redeploy to set the
        // target column to NULL (because Source's value is the
        // Phase-1 NULL form) and Phase-2 then sets it back. The
        // round-trip leaks 4 CDC entries per row. Filtering
        // deferred from UpdColumns makes Phase-1 silent on the
        // deferred-FK axis; Phase-2 owns the cycle-resolution
        // emission alone.
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
        // ScriptDomGenerate.generateOne emits the MERGE without a
        // trailing `;` (semicolons appear between statements in a
        // batch, not after a single-statement render). SQL Server
        // REQUIRES MERGE to terminate with `;` (SqlException: "A
        // MERGE statement must be terminated by a semi-colon (;)").
        // The terminal-text boundary appends `;` + `GO`.
        System.String.Concat(  // LINT-ALLOW: terminal MERGE statement-terminator + GO-batch suffix on the rendered MERGE; segments are typed (output of `ScriptDomGenerate.generateOne` from typed AST + SQL Server's required MERGE statement-terminator + V1 batch-separator literal); BCL `String.Concat` is the right primitive at this terminal-text boundary
            ScriptDomGenerate.generateOne (mergeStmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
            ";\nGO\n")

    /// Render one Phase-2 UPDATE statement for a row whose Phase-1
    /// MERGE deferred its same-SCC FK columns to NULL. The UPDATE
    /// scopes by the row's PK (`whereCells`) and SETs each deferred
    /// column to its original value from `row.Values`. Per Tier-3
    /// hard-requirement (`DECISIONS 2026-05-10 — text-builder-as-
    /// first-instinct discipline`): the typed-AST library is the
    /// gold standard; `ScriptDomBuild.buildUpdateStatement` is the
    /// chapter-4.1.B slice-δ addition that lands the typed shape.
    /// Same `;\nGO\n` terminal framing as `renderMerge`.
    let private renderUpdate
        (cdcAware: bool)
        (k: Kind)
        (deferred: Set<Name>)
        (typedValues: Map<Name, SqlLiteral>)
        : string =
        use _ = Bench.scope "emit.staticSeeds.renderUpdate"
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
        System.String.Concat(  // LINT-ALLOW: terminal UPDATE statement-terminator + GO-batch suffix on the rendered Phase-2 UPDATE (chapter 4.1.B slice δ); segments are typed (output of `ScriptDomGenerate.generateOne` from `ScriptDomBuild.buildUpdateStatement` typed AST + SQL Server's statement-terminator + V1 batch-separator literal); same architectural shape as `renderMerge`'s terminal-text boundary
            ScriptDomGenerate.generateOne (updateStmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
            ";\nGO\n")

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
    /// Render one plan load. The plan carries POST-substitution rows
    /// and the deferred-FK set; this function just type-lifts and
    /// renders. `Disposition` selects realization semantics:
    /// `ReconciledByRule` loads carry empty rows by plan-build and
    /// produce empty scripts (target already holds the identities);
    /// `PreservedFromSource` and `AssignedBySink` both render MERGE
    /// over the supplied rows (slice E will refine `AssignedBySink` to
    /// suppress the IDENTITY PK column).
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
            // Slice κ pillar 1 lift: project raw `Map<Name, string>`
            // populations into typed `Map<Name, SqlLiteral>` once at
            // construction time. Both Phase-1 MERGE rendering and
            // Phase-2 UPDATE rendering consume the typed shape.
            let typedRows =
                load.Rows
                |> List.map (fun row ->
                    row.Identifier,
                    staticRowToTypedValues typeLookup kind.Attributes row)
            let renderedPhase1 =
                renderMerge cdcAware deferred kind (typedRows |> List.map snd)
            let renderedPhase2 =
                if Set.isEmpty deferred then ""
                else
                    typedRows
                    |> Bench.iterMap "emit.staticSeeds.phase2Row" (fun (_, vs) -> renderUpdate cdcAware kind deferred vs)
                    |> System.String.Concat  // LINT-ALLOW: terminal Phase-2 cross-row UPDATE concatenation (chapter 4.1.B slice ι); each segment is the ScriptDom-rendered + GO-batched UPDATE for one row; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary; the typed `Statement` DU does not yet model UPDATE so `ScriptDomGenerate.toText` is not applicable
            let rendered =
                System.String.Concat(renderedPhase1, renderedPhase2)  // LINT-ALLOW: terminal per-kind concatenation of ScriptDom-rendered Phase-1 + Phase-2 strings (chapter 4.1.B slice κ; same architectural shape as slice δ's per-kind rendering); both segments are typed-AST outputs already terminated by `;\nGO\n`
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
    /// Π_StaticSeeds emit (canonical; plan-consuming). Realizes the
    /// supplied `DataLoadPlan` as per-kind MERGE/UPDATE scripts: the
    /// plan carries POST-substitution rows + the deferred-FK set per
    /// kind; this entry just renders. Realization is `DataIntent`
    /// end-to-end — operator-supplied identity substitution landed once
    /// at `DataLoadPlan.build`. Kinds absent from the plan (no load)
    /// produce empty scripts per T11.
    let emitFromPlan
        (catalog: Catalog)
        (profile: Profile)
        (plan: DataLoadPlan)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emitFromPlan"
        let cdc = profile.CdcAwareness
        let loadByKind = plan.Loads |> List.map (fun l -> l.Kind, l) |> Map.ofList
        let emptyScript : DataInsertScript =
            { Phase1Merges = []; Phase2Updates = []; RenderedPhase1 = ""; RenderedPhase2 = ""; Rendered = "" }
        let slices =
            Catalog.allKinds catalog
            |> Bench.iterMap "emit.staticSeeds.kind" (fun k ->
                let script =
                    match Map.tryFind k.SsKey loadByKind with
                    | Some load -> kindToScript cdc k load
                    | None      -> emptyScript
                k.SsKey, script)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Π_StaticSeeds emit (composer-facing; hoisted topo). Builds the
    /// plan from `Kind.staticPopulations` per kind with the empty
    /// remap (the static-seeds row source is catalog-resident
    /// evidence; operators wanting identity substitution build the
    /// plan themselves via `DataLoadPlan.build` + `emitFromPlan`).
    let emitWithTopo
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
        emitFromPlan catalog profile plan

    /// Π_StaticSeeds emit (standalone). Convenience for callers that
    /// don't go through the `DataEmissionComposer` (canary tests,
    /// direct-Π integration tests). Computes the topological order
    /// internally and delegates to `emitWithTopo`.
    let emit
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emit"
        let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
        emitWithTopo topo catalog profile

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
