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
/// **A18 amended.** The signature carries `Catalog × Profile`; Profile
/// is reserved for the slice-β `CdcAwareness` field consumption. No
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
        topo.Cycles
        |> List.collect (fun c -> c.Members)
        |> Set.ofList

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

    /// Project one StaticRow into the typed `SqlLiteral list` form
    /// that `MergeBuildArgs.Rows` expects. Iterates the kind's
    /// attributes in declared order; missing values default to NULL
    /// (V2 IR's empty-raw sentinel per `RawValueCodec`). Slice δ:
    /// columns named in `deferred` are emitted as `SqlLiteral.NullLit`
    /// regardless of the row's raw value (Phase-1 cycle-break).
    let private rowToSqlLiterals
        (typeLookup: Map<Name, PrimitiveType>)
        (deferred: Set<Name>)
        (attributes: Attribute list)
        (row: StaticRow)
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
        (rows: StaticRow list)
        : string =
        use _ = Bench.scope "emit.staticSeeds.renderMerge"
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
        (k: Kind)
        (deferred: Set<Name>)
        (row: StaticRow)
        : string =
        use _ = Bench.scope "emit.staticSeeds.renderUpdate"
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
        (k: Kind)
        : DataInsertScript =
        let populations = Kind.staticPopulations k
        if List.isEmpty populations then
            { Phase1Merges = []; Phase2Updates = []; Rendered = "" }
        else
            let cdcAware = CdcAwareness.isEnabled k.SsKey cdc
            let deferred = deferredColumns cycleMembers k
            let mergeText = renderMerge cdcAware deferred k populations
            let updateTexts =
                if Set.isEmpty deferred then []
                else populations |> List.map (renderUpdate k deferred)
            // Phase-1 MERGE then per-row Phase-2 UPDATEs concatenated
            // (one terminal-text boundary; segments are each already
            // ScriptDom-rendered + GO-batched).
            let rendered =
                mergeText :: updateTexts
                |> System.String.Concat  // LINT-ALLOW: terminal per-kind text concatenation of ScriptDom-rendered + GO-batched MERGE/UPDATE statement strings (chapter 4.1.B slice δ); each segment is the typed-AST output of `ScriptDomGenerate.generateOne` already terminated by `;\nGO\n`; BCL `String.Concat(IEnumerable<string>)` is the right primitive at this terminal-text boundary (no intermediate parsing / re-stringification); the typed `Statement` DU does not yet model MERGE/UPDATE so `ScriptDomGenerate.toText` is not applicable here
            let mkRow (row: StaticRow) : DataInsertRow =
                { KindKey       = k.SsKey
                  Identifier    = row.Identifier
                  Values        = row.Values
                  DeferredFkSet = deferred }
            let phase1Rows = populations |> List.map mkRow
            let phase2Rows =
                if Set.isEmpty deferred then []
                else populations |> List.map mkRow
            { Phase1Merges  = phase1Rows
              Phase2Updates = phase2Rows
              Rendered      = rendered }

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
    let emitWithTopo
        (topo: TopologicalOrder)
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emitWithTopo"
        let cdc = profile.CdcAwareness
        let cycleMembers = cycleMembersOf topo
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, kindToScript cdc cycleMembers k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

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
