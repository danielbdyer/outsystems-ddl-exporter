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

    /// Writable attributes for a kind — the columns the MERGE may
    /// INSERT into and SELECT from `Source`. Persisted/computed columns
    /// (gap N2; `Computed = Some _`) are SQL-Server-computed at write
    /// time and can never appear in an INSERT column list, an UPDATE SET,
    /// or a USING source; including one is a hard SQL error. Filtering
    /// here keeps `AllColumns` and the per-row VALUES projection aligned.
    let private writableAttributes (k: Kind) : Attribute list =
        k.Attributes |> List.filter (fun a -> a.Computed = None)

    /// Order columns deterministically (matches V1 + the SSDT emitter).
    /// Per A33 (deterministic-ordered schema emission), sort by the
    /// kind's declared attribute order — which is itself canonical
    /// after `CanonicalizeIdentity`. Computed columns are excluded (never
    /// written).
    let private orderedColumnNames (k: Kind) : string list =
        writableAttributes k |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

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
        (verification: DataVerification)
        (staging: DataStagingPolicy)
        (deleteScope: ScriptDomBuild.DeleteScope option)
        (cdcAware: bool)
        (deferred: Set<Name>)
        (bracketIdentity: bool)
        (k: Kind)
        (typedRows: Map<Name, SqlLiteral> list)
        : string =
        use _ = Bench.scope "emit.staticSeeds.renderMerge"
        // PERF_HARNESS §3.6 label 2: rows per rendered MERGE — makes rows/sec
        // derivable from the <label>/<label>.rows pair in harness diffs.
        Bench.recordSample "emit.staticSeeds.renderMerge.rows" (int64 typedRows.Length)
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
        // Gap N2: a persisted computed column (`Computed = Some _`) is
        // SQL-Server-computed, so it is never an UPDATE-target (UPDATE SET
        // <computed> = ... is a hard SQL error) and never enters the
        // change-detection predicate. Exclude it alongside PK + deferred.
        let updColumns =
            k.Attributes
            |> List.filter (fun a -> not a.IsPrimaryKey)
            |> List.filter (fun a -> not (Set.contains a.Name deferred))
            |> List.filter (fun a -> a.Computed = None)
            |> List.map (fun a -> ColumnRealization.columnNameText a.Column)
        let args : ScriptDomBuild.MergeBuildArgs =
            {
                Target     = table
                AllColumns = orderedColumnNames k
                PkColumns  = pkColumnNames k
                UpdColumns = updColumns
                Rows        = typedRows |> List.map (typedValuesToSqlLiterals deferred (writableAttributes k))
                CdcAware    = cdcAware
                DeleteScope = deleteScope
                StagedSource = None
            }
        // Above the threshold the inline `USING (VALUES …)` MERGE hits SQL Server
        // error 8623, so stage the rows through a `#temp`. Below, the inline form
        // stands — byte-identical to the pre-staging output. The staging decision
        // is the operator's `emission.dataStaging` posture (default: auto > 1000).
        // The staged-vs-inline counters are the auditable trace of the choice
        // (capability-descent doctrine: an operator pinning `inline` on a managed
        // env must be able to see a large kind STAYED inline) — Bench is always-on
        // (the measurement-in-production discipline), so the counts ride the run.
        if DataStagingPolicy.shouldStage staging (List.length typedRows) then
            Bench.recordSample "emit.staticSeeds.staged" 1L
            StagedMerge.renderStagedPhase1 "emit.staticSeeds" verification bracketIdentity
                (DataStagingPolicy.shouldIndex staging (List.length typedRows)) table k
                { args with StagedSource = Some (StagedMerge.stagedTempName k) }
        else
        Bench.recordSample "emit.staticSeeds.inline" 1L
        let mergeStmt = (ScriptDomBuild.buildMergeStatement args).Value
        // ScriptDomGenerate.generateOne emits the MERGE without a
        // trailing `;` (semicolons appear between statements in a
        // batch, not after a single-statement render). SQL Server
        // REQUIRES MERGE to terminate with `;` (SqlException: "A
        // MERGE statement must be terminated by a semi-colon (;)").
        // The terminal-text boundary appends `;` + `GO`.
        let mergeText =
            ScriptDomGenerate.generateOne (mergeStmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement)
        let mergeBatch =
          if not bracketIdentity then
            System.String.Concat(  // LINT-ALLOW: terminal MERGE statement-terminator + GO-batch suffix on the rendered MERGE; segments are typed (output of `ScriptDomGenerate.generateOne` from typed AST + SQL Server's required MERGE statement-terminator + V1 batch-separator literal); BCL `String.Concat` is the right primitive at this terminal-text boundary
                mergeText, ";\nGO\n")
          else
            // WP6 step 1 (DECISIONS 2026-06-13) — an IDENTITY-PK static
            // kind (`IdentityDisposition.AssignedBySink`) seeds explicit PK
            // values, so the MERGE's WHEN-NOT-MATCHED INSERT writes into the
            // IDENTITY column and SQL Server requires `SET IDENTITY_INSERT
            // [t] ON` to be active for it. The toggle is SESSION-scoped, and
            // the leveled load leg (`Deploy.executeBatchParallel`) opens a
            // FRESH connection per GO-segment — so the bracket MUST stay ONE
            // GO batch (no internal GO): a GO between the toggle and the
            // MERGE would land them on different connections and the toggle
            // would not apply. One batch is correct for both the fused
            // `Data/seed.sql` artifact (one sqlcmd session) and the leveled
            // deploy (one connection per segment), and it keeps the bracket
            // INSIDE `RenderedPhase1` so the fused-≡-leveled partition law
            // holds. `generateOne` does NOT terminate a `SET IDENTITY_INSERT`
            // statement (verified against the recorded bytes — the
            // statement-terminator behavior is statement-type-specific, not
            // governed by `IncludeSemicolons`), and MERGE requires its
            // IMMEDIATELY-preceding statement to be `;`-terminated, so every
            // segment gets an explicit `;` (the MERGE's manual `;` is the
            // same documented ScriptDom MERGE-terminator quirk).
            let setIdentityInsert (enabled: bool) : string =
                ScriptDomGenerate.generateOne
                    (ScriptDomBuild.buildSetIdentityInsert table enabled
                     :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement)
            System.String.Concat(  // LINT-ALLOW: terminal IDENTITY_INSERT-bracketed MERGE batch as a single GO segment (WP6 step 1); every segment is the typed ScriptDom render of `SET IDENTITY_INSERT` / the MERGE; the SQL Server statement terminators + the V1 `GO` batch separator are the terminal-text literals; BCL `String.Concat` is the right primitive at this terminal-text boundary
                setIdentityInsert true, ";\n",
                mergeText, ";\n",
                setIdentityInsert false, ";\nGO\n")
        // NM-73 — prepend the validate-before-apply drift guard as its OWN
        // GO batch before the MERGE (mirrors V1 `ValidateThenApply` ordering:
        // the guard THROWs before the MERGE batch can run). `Standard` is
        // byte-identical to the pre-NM-73 emission; the guard is the typed
        // parse-template render of V1's symmetric-`EXCEPT` THROW. The MERGE's
        // `args` already names the exact source rows we're about to write.
        match verification with
        | DataVerification.Standard -> mergeBatch
        | DataVerification.ValidateBeforeApply ->
            let guardText =
                ScriptDomGenerate.generateOne (ScriptDomBuild.buildValidateBeforeApplyGuard args)
            System.String.Concat(  // LINT-ALLOW: terminal guard-batch prefix (NM-73); the guard is the typed-AST parse-template render of V1's symmetric-EXCEPT THROW, framed as its own GO batch ahead of the MERGE; the V1 `GO` batch separator is the terminal-text literal; BCL `String.Concat` is the right primitive at this terminal-text boundary
                guardText, "\nGO\n", mergeBatch)

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
    /// over the supplied rows; the IDENTITY PK is KEPT for both (the
    /// MERGE's `ON` joins on it). For `AssignedBySink` the Phase-1 MERGE
    /// is bracketed with `SET IDENTITY_INSERT` (WP6 step 1) — the
    /// slice-E "suppress the PK" note is overturned (HANDOFF, WP6).
    let private kindToScript
        (opts: DataEmitOptions)
        (cdc: CdcAwareness)
        (kind: Kind)
        (load: DataLoadKind)
        : DataInsertScript =
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
            // AC-D7 — resolve the operator's scope against THIS kind: the
            // delete arm renders exactly when every term column is an
            // attribute here (`DeleteScopePolicy.resolveFor`); a kind
            // outside the scope keeps the upsert-only MERGE.
            let scopeForKind : ScriptDomBuild.DeleteScope option =
                deleteScope
                |> Option.bind (DeleteScopePolicy.resolveFor kind)
                |> Option.map (fun terms -> ({ Terms = terms } : ScriptDomBuild.DeleteScope))
            let deferred = load.DeferredFkColumns
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
                renderMerge verification staging scopeForKind cdcAware deferred bracketIdentity kind (typedRows |> List.map snd)
            let renderedPhase2 =
                if Set.isEmpty deferred then ""
                elif DataStagingPolicy.shouldStage staging (List.length typedRows) then
                    // Set-based escalation (Step 4): above the SAME staging
                    // threshold that routes Phase-1 through a `#temp`, Phase-2's
                    // N per-row UPDATEs collapse to ONE `UPDATE … FROM target
                    // JOIN #fk` — a kind is treated coherently across both phases
                    // by one threshold. The narrow `#fk` temp carries the real
                    // deferred-FK values; Phase-1 already inserted the rows with
                    // those columns NULLed.
                    let table : TableId =
                        { Schema = kind.Physical.Schema
                          Table  = kind.Physical.Table; Catalog = None }
                    StagedMerge.renderStagedPhase2 "emit.staticSeeds" cdcAware table kind deferred (typedRows |> List.map snd)
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
    let emitFromPlan
        (opts: DataEmitOptions)
        (catalog: Catalog)
        (profile: Profile)
        (plan: DataLoadPlan)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emitFromPlan"
        let cdc = profile.CdcAwareness
        let loadByKind = plan.Loads |> List.map (fun l -> l.Kind, l) |> Map.ofList
        let emptyScript : DataInsertScript =
            { Phase1Merges = []; Phase2Updates = []; RenderedPhase1 = ""; RenderedPhase2 = ""; Rendered = "" }
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
