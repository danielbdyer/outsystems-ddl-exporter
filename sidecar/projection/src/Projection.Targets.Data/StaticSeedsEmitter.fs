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
/// **A18 amended.** The signature carries `Catalog × Profile × sibling-
/// evidence input` (the sibling evidence is a `SurrogateRemapContext` —
/// operator-supplied identity remap, acquired by a Pass and consumed
/// here for FK re-pointing; same evidentiary shape as
/// `MigrationDependenciesEmitter`'s `UserRemapContext` parameter). No
/// `Policy` parameter — DataComposition dispatch happens in the
/// composer (slice η), not here.
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
        k.Attributes |> List.map (fun a -> a.Column.ColumnName)

    /// Primary-key column names in the kind's declared order. The
    /// MERGE's ON-clause joins on these; the WHEN-NOT-MATCHED INSERT
    /// includes them; the WHEN-MATCHED UPDATE excludes them (PK is
    /// stable per row identity). Phase-2 UPDATEs use the same set
    /// for the WHERE-clause row-scope.
    let private pkColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

    /// Non-PK column names (the MERGE's UPDATE-target columns).
    let private updatableColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> not a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

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

    /// Union of every SCC's member set across the topological pass's
    /// cycle diagnostics. A kind appears here IFF it participates in
    /// at least one cycle (resolved or unresolved). Concept-shaped
    /// per pillar 8: names *what is in cycles*, not the act of
    /// computing membership.
    let private cycleMembersOf (topo: TopologicalOrder) : Set<SsKey> =
        TopologicalOrder.cycleMembers topo

    /// The (attribute-name) columns on `k` that must be NULLed in
    /// Phase-1 and populated in Phase-2. A column is deferred iff:
    ///   - `k` participates in a cycle (`Set.contains k.SsKey
    ///     cycleMembers`), AND
    ///   - the FK's target is in the same cycle membership set, AND
    ///   - the source attribute's column is nullable (NULLing
    ///     a NOT-NULL FK would violate the constraint; V1 likewise
    ///     skips those — `IdentifyNullableFKColumns:184`).
    /// Returns `Set.empty` for non-cycle kinds and for kinds whose
    /// in-cycle FKs are all non-nullable.
    let private deferredColumns
        (cycleMembers: Set<SsKey>)
        (k: Kind)
        : Set<Name> =
        TopologicalOrder.deferredFkColumns cycleMembers k

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
            |> List.map (fun a -> a.Column.ColumnName)
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
    let private kindToScript
        (cdc: CdcAwareness)
        (cycleMembers: Set<SsKey>)
        (remapTargets: Set<SsKey>)
        (remap: SurrogateRemapContext)
        (k: Kind)
        : DataInsertScript =
        let populations = Kind.staticPopulations k
        if List.isEmpty populations then
            { Phase1Merges  = []
              Phase2Updates = []
              RenderedPhase1 = ""
              RenderedPhase2 = ""
              Rendered      = "" }
        else
            let cdcAware = CdcAwareness.isEnabled k.SsKey cdc
            let deferred = deferredColumns cycleMembers k
            let typeLookup = columnTypeLookup k
            // Apply the operator-supplied SurrogateRemapContext to FK
            // values targeting kinds in the remap set (OperatorIntent
            // Insertion — the remap inserts the assigned-side identity
            // where the source-side identity was). Empty remap → no-op
            // (no targets → empty fkTargets → identity over rows; the
            // skeleton path is preserved). Rows whose targeted FK has
            // no matched assigned surrogate are dropped — skip-and-
            // diagnose per the operator's supply discipline.
            let remappedPopulations =
                if Set.isEmpty remapTargets then populations
                else
                    let fkTargets = SurrogateRemap.fkColumnsTargeting remapTargets k
                    if Map.isEmpty fkTargets then populations
                    else (SurrogateRemap.remapRowFks fkTargets remap populations).Rows
            if List.isEmpty remappedPopulations then
                { Phase1Merges  = []
                  Phase2Updates = []
                  RenderedPhase1 = ""
                  RenderedPhase2 = ""
                  Rendered      = "" }
            else
            // Slice κ pillar 1 lift: project raw `Map<Name, string>`
            // populations into typed `Map<Name, SqlLiteral>` once at
            // construction time. Both Phase-1 MERGE rendering and
            // Phase-2 UPDATE rendering consume the typed shape.
            let typedRows =
                remappedPopulations
                |> List.map (fun row ->
                    row.Identifier,
                    staticRowToTypedValues typeLookup k.Attributes row)
            let renderedPhase1 =
                renderMerge cdcAware deferred k (typedRows |> List.map snd)
            let renderedPhase2 =
                if Set.isEmpty deferred then ""
                else
                    typedRows
                    |> Bench.iterMap "emit.staticSeeds.phase2Row" (fun (_, vs) -> renderUpdate cdcAware k deferred vs)
                    |> System.String.Concat  // LINT-ALLOW: terminal Phase-2 cross-row UPDATE concatenation (chapter 4.1.B slice ι); each segment is the ScriptDom-rendered + GO-batched UPDATE for one row; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary; the typed `Statement` DU does not yet model UPDATE so `ScriptDomGenerate.toText` is not applicable
            // Per-kind self-complete view: Phase-1 + Phase-2 in
            // textual order. Slice ι splits these for the composer's
            // global cross-kind ordering; per-kind `Rendered`
            // remains correct for self-FK cycles.
            let rendered =
                System.String.Concat(renderedPhase1, renderedPhase2)  // LINT-ALLOW: terminal per-kind concatenation of ScriptDom-rendered Phase-1 + Phase-2 strings (chapter 4.1.B slice κ; same architectural shape as slice δ's per-kind rendering); both segments are typed-AST outputs already terminated by `;\nGO\n`
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
    /// Π_StaticSeeds emit (composer-facing; full-explicit). Threads an
    /// operator-supplied `SurrogateRemapContext` through MERGE
    /// construction — every FK column whose target is a remap key is
    /// re-pointed from the Source surrogate to the assigned target
    /// surrogate. Empty remap → no-op (skeleton-purity preserved). Per
    /// the sibling-wrapper discipline, this is the full-explicit
    /// surface; `emitWithTopo` (no-remap default) and `emit` (no-topo +
    /// no-remap default) delegate here with `SurrogateRemapContext.empty`.
    let emitWithTopoAndRemap
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        (surrogateRemap: SurrogateRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emitWithTopoAndRemap"
        let cdc = profile.CdcAwareness
        let cycleMembers = cycleMembersOf topo
        let remapTargets =
            surrogateRemap.Assignments |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> Bench.iterMap "emit.staticSeeds.kind" (fun k ->
                k.SsKey, kindToScript cdc cycleMembers remapTargets surrogateRemap k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    let emitWithTopo
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        emitWithTopoAndRemap topo catalog profile SurrogateRemapContext.empty

    /// Π_StaticSeeds emit (standalone). Convenience for callers that
    /// don't go through the `DataEmissionComposer` (canary tests,
    /// direct-Π integration tests). Computes the topological order
    /// internally and delegates to `emitWithTopo` — same algebra, one
    /// extra `TopologicalOrderPass` invocation per call. The lineage
    /// trail of the topo pass is silently discarded; pipeline-level
    /// callers SHOULD route through the composer to preserve trail
    /// fidelity.
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
                "Emit MERGE statements for kinds whose `Modality` list contains `Static rows` — pure projection of Catalog-resident evidence (the Static rows are catalog data, not operator overlay). Per A18 amended, the emitter consumes Catalog × Profile only; no Policy enters this site."
              TransformSite.dataIntent "cdcAwareChangeDetection"
                "Per-kind MERGE WHEN MATCHED predicate gates UPDATE on actual column-level differences when `Profile.CdcAwareness.CdcEnabled` carries the kind. Profile is *evidence* (A18 amended; pillar 9 — Profile-driven observations are DataIntent); the CDC predicate IS the data-intent shape, not an operator override. Slice β (chapter 4.1.B) cash-out."
              TransformSite.dataIntent "deferredFkPhase2"
                "Two-phase cycle-breaking — Phase-1 emits MERGEs with deferred FK columns NULLed; Phase-2 UPDATEs populate them once all Phase-1 inserts complete. Cycle membership is structural (from `TopologicalOrder.Cycles`); the deferral is topology-derived, not operator-supplied. Slice δ (chapter 4.1.B) cash-out."
              TransformSite.operatorIntent "staticRowSurrogateRemap" Insertion
                "Apply an operator-supplied `SurrogateRemapContext` (acquired by `Reconciliation` / `UserFkReflowPass` / any future acquisition method) to FK column values in static rows before MERGE rendering. Every FK column whose target is in the remap is re-pointed from the Source surrogate to the assigned-side surrogate; rows whose targeted FK has no matched assigned counterpart are dropped (skip-and-diagnose at the operator's supply discipline). Operator intent — which Source identities reconcile to which target identities; OverlayAxis = Insertion (the remap inserts the assigned-side identity where the source-side identity was, mirroring the MigrationDependenciesEmitter precedent). Empty remap → no-op (skeleton-purity preserved)." ]
