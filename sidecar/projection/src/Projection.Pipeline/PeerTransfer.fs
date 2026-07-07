namespace Projection.Pipeline

// LINT-ALLOW-FILE: the gate/proposal renderers compose operator-facing report
//   prose (THE_VOICE register) at a terminal reporting boundary; the detection
//   core is pure and carries no I/O — contract acquisition (the two OSSYS
//   reads) is the one Task-returning seam, mirroring `Readiness`/`Compare`'s
//   pure-core / I/O-one-layer-up split.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.OssysSql

/// The peer (A→A) transfer's contract + gate support: two cloud cells of ONE
/// model whose physical `OSUSR_*` names differ per espace (the QA→UAT partial
/// transfer, `PARTIAL_TRANSFER_READINESS_LOG.md` 2026-07-06).
///
/// The identity precondition the rename-aware engine needs (`TransferRun.fs`,
/// `runCore` with an ingestion pair) is SsKey ALIGNMENT between the two
/// contracts. `ReadSide` cannot supply it (SsKeys synthesized from physical
/// coordinates — two live reads never align); the ONE authored model rendered
/// twice (`CatalogRendition`) covers only the logical↔physical reverse leg.
/// The peer pair aligns the third way: EACH side's contract is read from its
/// own OSSYS metamodel (`Source.ofOssys` → `LiveModelRead`), whose `SsKey`s
/// are the native OutSystems GUIDs — stable across environments (LifeTime
/// preserves SS_KEY; the espace-invariance law, `CROSS_ENVIRONMENT_READINESS`
/// / `Readiness`). Same identity, two physical realizations — exactly the
/// precondition, without an authored model in the loop.
///
/// Two pre-transfer gates ride the acquired pair:
///   - the SHAPE gate — the SS_KEY-keyed schema-compatibility verdict
///     (insertability-blocking divergences refuse by name; real-but-benign
///     divergences surface as advisories, never silently), and
///   - the SUBSET-FK gate — every FK edge escaping a declared `tables` subset
///     is detected and a per-edge strategy is proposed (reconcile-by-key
///     against rows the sink already holds — the default recommendation;
///     widen the subset; or accept the drop-set with --allow-drops).
[<RequireQualifiedAccess>]
module PeerTransfer =

    // ------------------------------------------------------------------
    // Contract acquisition — the one I/O seam.
    // ------------------------------------------------------------------

    /// Read the two SsKey-aligned contracts from the two environments' OSSYS
    /// metamodels (D9 conn refs: `env:<var>` / `file:<path>` / raw). A failed
    /// read is the named `source.ossys.readFailed` refusal from the `Source`
    /// port, classified onto the schema-read axis (exit 6) by `Preflight`.
    ///
    /// Scope-bearing entry point (2026-07-07) — BOTH sides read under the
    /// same snapshot scope (the projection.json `model` binding via
    /// `SnapshotScopeBinding.fromModel`), so the transfer's contracts see
    /// the same modeled estate as full-export/publish. `acquireContracts`
    /// is the zero-default sibling.
    let acquireContractsWith
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        (sourceConn: string)
        (sinkConn: string)
        : Task<Result<Catalog * Catalog>> =
        task {
            match! Source.read (Source.ofOssysWith parameters sourceConn) with
            | Error es -> return Result.failure es
            | Ok sourceContract ->
                match! Source.read (Source.ofOssysWith parameters sinkConn) with
                | Error es -> return Result.failure es
                | Ok sinkContract -> return Result.success (sourceContract, sinkContract)
        }

    /// Zero-default sibling — the show-me-everything `defaultParameters`
    /// stance (the canary/mock-environment face; config-bound callers pass
    /// their model scope through `acquireContractsWith`).
    let acquireContracts (sourceConn: string) (sinkConn: string) : Task<Result<Catalog * Catalog>> =
        acquireContractsWith MetadataSnapshotRunner.defaultParameters sourceConn sinkConn

    // ------------------------------------------------------------------
    // The shape gate — SS_KEY-keyed schema compatibility, scoped to the
    // kinds the run will touch.
    // ------------------------------------------------------------------

    /// The shape verdict over the pair: `Blocking` divergences prevent correct
    /// row insertion (kind presence, attribute presence, column-shape facets);
    /// `Advisory` divergences are real but do not block a data load
    /// (constraint/index/kind-facet drift, logical renames, widenings) — they
    /// surface so nothing is silent.
    type ShapeVerdict =
        { Blocking : string list
          Advisory : string list }

    let private kindNameIn (c: Catalog) (key: SsKey) : string =
        match Catalog.tryFindKind key c with
        | Some k -> Name.value k.Name
        | None -> SsKey.rootOriginal key

    let private attrIn (k: Kind) (key: SsKey) : Attribute option =
        k.Attributes |> List.tryFind (fun a -> a.SsKey = key)

    let private attrNameIn (k: Kind option) (key: SsKey) : string =
        k
        |> Option.bind (fun k -> attrIn k key)
        |> Option.map (fun a -> Name.value a.Name)
        |> Option.defaultValue (SsKey.rootOriginal key)

    /// Compute the shape verdict for the pair, scoped: `Some keys` restricts
    /// the verdict to the kinds a partial run touches (the subset + its
    /// reconciled kinds); `None` judges the whole estate (a full transfer).
    /// Both catalogs are normalized to their espace-invariant logical shape
    /// first (`Readiness.toLogicalShape` — physical-realization artifacts
    /// stripped), so only REAL model divergence registers; the physical
    /// `OSUSR_*` naming difference this leg exists for never does.
    let shapeVerdict (scope: Set<SsKey> option) (sourceContract: Catalog) (sinkContract: Catalog) : ShapeVerdict =
        let src = Readiness.toLogicalShape sourceContract
        let snk = Readiness.toLogicalShape sinkContract
        let diff = CatalogDiff.between src snk
        let inScope (k: SsKey) =
            match scope with
            | None -> true
            | Some s -> Set.contains k s
        let blocking = ResizeArray<string>()
        let advisory = ResizeArray<string>()
        // Kind presence: a source kind absent from the sink model blocks — the
        // rows have no landing table. (Sink-only kinds never block a load OUT
        // of the source; they surface only on an unscoped, whole-estate check.)
        for key in CatalogDiff.removed diff |> Set.filter inScope do
            blocking.Add (sprintf "entity '%s' is not in the sink model — its rows have no landing table." (kindNameIn src key))
        if Option.isNone scope then
            for key in CatalogDiff.added diff do
                advisory.Add (sprintf "entity '%s' exists only in the sink model (no source rows will touch it)." (kindNameIn snk key))
        // Same-identity logical renames: identity (SS_KEY) holds, so the load
        // is unaffected; surfaced because the operator's table lists are
        // name-keyed.
        for KeyValue (key, r) in CatalogDiff.renamed diff do
            if inScope key then
                advisory.Add (sprintf "entity '%s' is named '%s' in the sink — same identity (SS_KEY); the load keys on identity." (Name.value r.OldName) (Name.value r.NewName))
        // Attribute grain, per in-scope kind present in both models.
        for KeyValue (kindKey, ad) in CatalogDiff.attributeDiffs diff do
            if inScope kindKey then
                let srcKind = Catalog.tryFindKind kindKey src
                let snkKind = Catalog.tryFindKind kindKey snk
                let kindLabel = kindNameIn src kindKey
                for attrKey in ad.Removed do
                    blocking.Add (sprintf "%s.%s exists only in the source model — its values have no landing column." kindLabel (attrNameIn srcKind attrKey))
                for attrKey in ad.Added do
                    match snkKind |> Option.bind (fun k -> attrIn k attrKey) with
                    | Some a when a.IsMandatory && not a.IsIdentity && Option.isNone a.DefaultValue ->
                        blocking.Add (sprintf "%s.%s is sink-only, mandatory, and carries no default — inserted rows cannot satisfy it." kindLabel (Name.value a.Name))
                    | Some a ->
                        advisory.Add (sprintf "%s.%s exists only in the sink model (inserted rows leave it to its default)." kindLabel (Name.value a.Name))
                    | None -> ()
                for KeyValue (attrKey, r) in ad.Renamed do
                    advisory.Add (sprintf "%s.%s is named '%s' in the sink — same identity; the rename map re-points it." kindLabel (Name.value r.OldName) (Name.value r.NewName))
                for change in ad.Reshaped do
                    let srcAttr = srcKind |> Option.bind (fun k -> attrIn k change.AttributeKey)
                    let snkAttr = snkKind |> Option.bind (fun k -> attrIn k change.AttributeKey)
                    let attrLabel = attrNameIn srcKind change.AttributeKey
                    for facet in change.Facets do
                        let line (verdict: ResizeArray<string>) (detail: string) =
                            verdict.Add (sprintf "%s.%s: %s." kindLabel attrLabel detail)
                        match facet with
                        | AttributeFacet.DataType ->
                            line blocking "the data type differs between source and sink"
                        | AttributeFacet.PrimaryKey ->
                            line blocking "the primary-key marking differs between source and sink"
                        | AttributeFacet.Identity ->
                            line blocking "the IDENTITY marking differs between source and sink"
                        | AttributeFacet.Computed ->
                            line blocking "the computed-column definition differs between source and sink"
                        | AttributeFacet.Nullability ->
                            // Judge on `ColumnRealization.IsNullable` — the
                            // SAME plane the facet fires on (adversarial LOW
                            // #12: `IsMandatory` can disagree with the column
                            // reality, letting a sink-NOT-NULL divergence pass
                            // as permissive).
                            match srcAttr, snkAttr with
                            | Some s, Some t when s.Column.IsNullable && not t.Column.IsNullable ->
                                line blocking "nullable in the source but NOT NULL in the sink — NULL values cannot land"
                            | _ ->
                                line advisory "the sink is more permissive on nullability (no value is refused)"
                        | AttributeFacet.Length ->
                            match srcAttr |> Option.bind (fun a -> a.Length), snkAttr |> Option.bind (fun a -> a.Length) with
                            | Some s, Some t when t < s ->
                                line blocking (sprintf "the sink length (%d) is narrower than the source (%d) — values can overflow" t s)
                            | None, Some t ->
                                // 2026-07-06 (adversarial HIGH #4): source
                                // open-ended (MAX) into a bounded sink was
                                // misread as "wider" — it truncates.
                                line blocking (sprintf "the source length is open-ended but the sink is bounded (%d) — values can overflow" t)
                            | _, None ->
                                line advisory "the sink length is open-ended (no value is refused)"
                            | _ ->
                                line advisory "the sink length is wider (no value is refused)"
                        | AttributeFacet.Precision | AttributeFacet.Scale ->
                            // Block only a NARROWING (adversarial LOW #11 —
                            // a sink-wider DECIMAL is loadable; refusing it
                            // was a false refusal).
                            let narrower (f: Attribute -> int option) =
                                match srcAttr |> Option.bind f, snkAttr |> Option.bind f with
                                | Some s, Some t when t < s -> true
                                | _ -> false
                            if narrower (fun a -> a.Precision) || narrower (fun a -> a.Scale) then
                                line blocking "the sink decimal precision/scale is narrower than the source — values can overflow"
                            else
                                line advisory "the sink decimal precision/scale is wider (no value is refused)"
                        | AttributeFacet.DefaultValue ->
                            line advisory "the default value differs (defaults never rewrite transferred values)"
        // Constraint / index / kind-own drift: real divergence, but none of it
        // refuses a row — advisory by design (the migrate verb owns schema).
        let advisoryCount (label: string) (keys: Set<SsKey>) =
            if not (Set.isEmpty keys) then
                let names = keys |> Set.toList |> List.map (kindNameIn src) |> String.concat ", "
                advisory.Add (sprintf "%s differ(s) on: %s (schema drift — does not block a data load)." label names)
        advisoryCount "foreign-key constraints" (CatalogDiff.referenceDiffs diff |> Map.filter (fun k _ -> inScope k) |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        advisoryCount "indexes" (CatalogDiff.indexDiffs diff |> Map.filter (fun k _ -> inScope k) |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        advisoryCount "entity-level facets (modality/triggers/checks/activation)" (CatalogDiff.kindFacetDiffs diff |> Map.filter (fun k _ -> inScope k) |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        { Blocking = List.ofSeq blocking; Advisory = List.ofSeq advisory }

    /// The gate form of the verdict: blocking divergence refuses by name
    /// (`transfer.peer.shapeDivergence` — the shape-divergence axis, exit 5,
    /// the same verdict class `check shape` reports). Advisory lines ride in
    /// the `Ok` so the caller surfaces them regardless.
    let shapeGate (scope: Set<SsKey> option) (sourceContract: Catalog) (sinkContract: Catalog) : Result<string list> =
        let verdict = shapeVerdict scope sourceContract sinkContract
        if List.isEmpty verdict.Blocking then Result.success verdict.Advisory
        else
            Result.failureOf
                (ValidationError.create "transfer.peer.shapeDivergence"
                    (sprintf "the source and sink models are not one shape over the transferred set (%d blocking divergence(s)): %s"
                        verdict.Blocking.Length
                        (String.concat " " verdict.Blocking)))

    // ------------------------------------------------------------------
    // The subset-FK gate — every relationship escaping the declared subset,
    // detected, with a strategy proposed per edge.
    // ------------------------------------------------------------------

    /// One FK edge from an in-subset kind to an out-of-subset, un-reconciled
    /// kind — the edge a partial transfer must decide a strategy for.
    type EscapingFk =
        { Kind       : SsKey
          KindName   : Name
          /// The FK attribute's logical name (espace-safe).
          Column     : Name
          /// The FK column is optional — rows carrying NULL pass untouched.
          Nullable   : bool
          Target     : SsKey
          TargetName : Name
          /// The target's owning MODULE name — the proposal narrates the
          /// resolvable `Module.Entity:Column` reconcile form (a bare
          /// logical name resolves by PHYSICAL table only, which differs
          /// per environment; adversarial MEDIUM #8).
          TargetModule : Name
          /// Candidate reconcile columns on the target: its single-column
          /// UNIQUE indexes over non-PK attributes (the business keys a
          /// `reconcile Module.Entity:Column` can match sink rows by).
          CandidateReconcileColumns : Name list }

    /// Detect every FK edge that escapes the declared subset: source kind in
    /// the load-set, target kind neither in the load-set nor reconciled.
    /// Deterministic (sorted by kind name, then column name). Empty load-set
    /// semantics ride the caller: pass the RESOLVED subset (a full transfer
    /// has no escaping edges by definition — pass `Set.empty` mapped over
    /// `None` upstream and skip the call).
    let escapingFks (contract: Catalog) (loadSet: Set<SsKey>) (reconciled: Set<SsKey>) : EscapingFk list =
        let candidateKeysOf (target: Kind) : Name list =
            target.Indexes
            |> List.choose (fun ix ->
                match ix.Uniqueness, ix.Columns with
                | IndexUniqueness.Unique, [ col ] ->
                    attrIn target col.Attribute
                    |> Option.filter (fun a -> not a.IsPrimaryKey)
                    |> Option.map (fun a -> a.Name)
                | _ -> None)
            |> List.sortBy Name.value
        let moduleOf : Map<SsKey, Name> =
            Catalog.allModulesKinds contract
            |> List.map (fun (m, k) -> k.SsKey, m.Name)
            |> Map.ofList
        // The ONE traversal (`TransferSubset.escapingEdges`, shared with the
        // engine backstop `Transfer.subsetEscapeGate`) — this detector only
        // ENRICHES each edge for the operator narration.
        TransferSubset.escapingEdges contract loadSet reconciled
        |> List.map (fun (kind, r, target) ->
            let sourceAttr = attrIn kind r.SourceAttribute
            { Kind       = kind.SsKey
              KindName   = kind.Name
              Column     = sourceAttr |> Option.map (fun a -> a.Name) |> Option.defaultValue r.Name
              Nullable   = sourceAttr |> Option.map (fun a -> not a.IsMandatory) |> Option.defaultValue false
              Target     = r.TargetKind
              TargetName = target.Name
              TargetModule = moduleOf |> Map.tryFind r.TargetKind |> Option.defaultValue target.Name
              CandidateReconcileColumns = candidateKeysOf target })
        |> List.sortBy (fun e -> Name.value e.KindName, Name.value e.Column)

    /// The per-edge strategy proposal lines (operator-facing; one line per
    /// escaping edge, the recommended move first, in the RESOLVABLE
    /// `Module.Entity:Column` reconcile form).
    let narrateEscapes (escapes: EscapingFk list) : string list =
        escapes
        |> List.map (fun e ->
            let targetRef = sprintf "%s.%s" (Name.value e.TargetModule) (Name.value e.TargetName)
            let candidates =
                match e.CandidateReconcileColumns with
                | [] -> sprintf "no unique non-key column detected — reconcile '%s:<a column you know is unique>' still works" targetRef
                | cs ->
                    cs
                    |> List.map (fun c -> sprintf "reconcile '%s:%s'" targetRef (Name.value c))
                    |> String.concat " or "
            let softening = if e.Nullable then " (optional — rows with no reference pass untouched)" else ""
            // Lead with the paste-able move (narrow terminals truncate the
            // tail; the remedy must survive).
            sprintf "%s.%s -> %s escapes the subset%s. %s; or add '%s' to tables."
                (Name.value e.KindName) (Name.value e.Column) (Name.value e.TargetName)
                softening
                candidates (Name.value e.TargetName))

    /// The gate form: a live Execute with un-strategized escaping edges
    /// refuses by name (`transfer.peer.subsetFkEscapes` — the drop-set axis,
    /// exit 9). A DryRun never refuses here — the preview narrates the
    /// proposals instead.
    ///
    /// 2026-07-06 (the phase-2 adversarial review, CRITICAL #2): the
    /// `--allow-drops` bypass is GONE. Nothing on the engine's write plane
    /// drops or NULLs an escaping FK — the rows would land carrying the
    /// SOURCE environment's surrogate values, silently cross-wired to
    /// whatever sink rows happen to own those keys (`--allow-drops` covers
    /// the REPORTED drop-set of reconciled/remapped kinds, which these
    /// columns never enter). Until the engine can genuinely drop/NULL
    /// escaping references, the honest gate refuses: reconcile the target
    /// or widen the subset.
    let subsetFkGate (execute: bool) (escapes: EscapingFk list) : Result<unit> =
        if execute && not (List.isEmpty escapes) then
            Result.failureOf
                (ValidationError.create "transfer.peer.subsetFkEscapes"
                    (sprintf "%d relationship(s) escape the declared table subset; each needs a strategy before a live run (their rows would keep SOURCE-environment references — --allow-drops does not cover this): %s"
                        escapes.Length
                        (String.concat " " (narrateEscapes escapes))))
        else Result.success ()

    // ------------------------------------------------------------------
    // Reconcile-candidate EVIDENCE (2026-07-07, the go-board forecast
    // program): each proposed reconcile column, validated against the
    // LIVE pair — is the column unique on the sink (the duplicate-key
    // tiebreaker never fires), and do the source's ACTUAL values find
    // sink rows? Read-only scalar counts plus a bounded sample; a probe
    // that cannot run degrades to a NAMED `Unprobed` verdict, never a
    // crash and never silence.
    // ------------------------------------------------------------------

    /// Distinct source values sampled per candidate column — bounded so
    /// the probe stays cheap on an estate-scale table, and far under the
    /// 2100-parameter cap the match query spends them against.
    [<Literal>]
    let private EvidenceSampleCap = 200

    [<RequireQualifiedAccess>]
    type EvidenceVerdict =
        /// The probe ran: sink uniqueness over non-null values + how many
        /// sampled distinct source values found a sink row.
        | Probed of sinkUnique: bool * sampleHits: int * sampleSize: int
        /// The probe could not run — the reason is named, never silent.
        | Unprobed of reason: string

    /// One candidate reconcile column's live evidence.
    type ReconcileEvidence =
        { Target      : SsKey
          /// `Module.Entity` — the resolvable, paste-able reconcile form.
          TargetRef   : string
          Column      : Name
          /// Nominated by a single-column unique sink index (the
          /// detector's candidates) vs proposed by name shape alone.
          IndexBacked : bool
          Verdict     : EvidenceVerdict }

    let private sampleSourceValues (source: SqlConnection) (schema: string) (table: string) (col: string) : obj list =
        use cmd = source.CreateCommand()
        cmd.CommandText <- sprintf "SELECT DISTINCT TOP (%d) [%s] FROM [%s].[%s] WHERE [%s] IS NOT NULL;" EvidenceSampleCap col schema table col  // LINT-ALLOW: terminal SQL-text boundary; identifiers come from validated TableId/ColumnRealization coordinates
        use r = cmd.ExecuteReader()
        let rec read (acc: obj list) = if r.Read() then read (r.GetValue 0 :: acc) else List.rev acc
        read []

    /// `(non-null count, distinct count)` of the candidate column on the
    /// sink — equal (and > 0) means the reconcile key never tiebreaks.
    let private sinkUniqueness (sink: SqlConnection) (schema: string) (table: string) (col: string) : int64 * int64 =
        use cmd = sink.CreateCommand()
        cmd.CommandText <- sprintf "SELECT COUNT_BIG([%s]), COUNT_BIG(DISTINCT [%s]) FROM [%s].[%s];" col col schema table  // LINT-ALLOW: terminal SQL-text boundary; identifiers come from validated coordinates
        use r = cmd.ExecuteReader()
        if r.Read() then r.GetInt64 0, r.GetInt64 1 else 0L, 0L

    /// How many of the sampled source values exist on the sink —
    /// parameterized VALUES row set, one round trip.
    let private sinkSampleHits (sink: SqlConnection) (schema: string) (table: string) (col: string) (values: obj list) : int =
        if List.isEmpty values then 0
        else
            use cmd = sink.CreateCommand()
            let rows = values |> List.mapi (fun i _ -> sprintf "(@p%d)" i) |> String.concat ","
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM (VALUES %s) AS v(x) WHERE EXISTS (SELECT 1 FROM [%s].[%s] WHERE [%s] = v.x);" rows schema table col  // LINT-ALLOW: terminal SQL-text boundary; values ride parameters, identifiers come from validated coordinates
            values |> List.iteri (fun i v -> cmd.Parameters.AddWithValue(sprintf "@p%d" i, v) |> ignore)
            System.Convert.ToInt32 (cmd.ExecuteScalar())

    /// The name shapes that make a non-PK TEXT attribute a plausible
    /// business key when no unique index nominates one. Heuristic
    /// candidates are always PROBED before proposed, and the narration
    /// marks them "no unique index" — the evidence, not the name, carries
    /// the recommendation.
    let private nameShapedCandidate (a: Attribute) : bool =
        not a.IsPrimaryKey
        && a.Type = PrimitiveType.Text
        && (let n = (Name.value a.Name).ToLowerInvariant()
            [ "name"; "code"; "email"; "username"; "login"; "key"; "label"; "abbreviation"; "identifier"; "number" ]
            |> List.exists (fun shape -> n = shape || n.EndsWith shape))

    /// Probe live evidence for each escaping target's candidate reconcile
    /// columns (index-backed first, then name-shaped; at most three per
    /// target). Synchronous scalar reads on the two open connections —
    /// the go board's short-lived probe pair.
    let probeReconcileEvidence
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (escapes: EscapingFk list)
        : ReconcileEvidence list =
        escapes
        |> List.distinctBy (fun e -> e.Target)
        |> List.collect (fun e ->
            match Catalog.tryFindKind e.Target sourceContract, Catalog.tryFindKind e.Target sinkContract with
            | Some srcKind, Some sinkKind ->
                let heuristic =
                    sinkKind.Attributes
                    |> List.filter nameShapedCandidate
                    |> List.map (fun a -> a.Name)
                    |> List.filter (fun n -> not (List.contains n e.CandidateReconcileColumns))
                    |> List.sortBy Name.value
                let targetRef = sprintf "%s.%s" (Name.value e.TargetModule) (Name.value e.TargetName)
                ((e.CandidateReconcileColumns |> List.map (fun c -> c, true))
                 @ (heuristic |> List.map (fun c -> c, false)))
                |> List.truncate 3
                |> List.map (fun (col, indexBacked) ->
                    let verdict =
                        match srcKind.Attributes |> List.tryFind (fun a -> a.Name = col),
                              sinkKind.Attributes |> List.tryFind (fun a -> a.Name = col) with
                        | Some srcAttr, Some sinkAttr ->
                            try
                                let sample =
                                    sampleSourceValues source
                                        (TableId.schemaText srcKind.Physical) (TableId.tableText srcKind.Physical)
                                        (ColumnRealization.columnNameText srcAttr.Column)
                                let kSchema, kTable = TableId.schemaText sinkKind.Physical, TableId.tableText sinkKind.Physical
                                let kCol = ColumnRealization.columnNameText sinkAttr.Column
                                let total, distinct = sinkUniqueness sink kSchema kTable kCol
                                let hits = sinkSampleHits sink kSchema kTable kCol sample
                                EvidenceVerdict.Probed (total = distinct && total > 0L, hits, sample.Length)
                            with ex -> EvidenceVerdict.Unprobed ex.Message
                        | _ -> EvidenceVerdict.Unprobed "the column is not present on both sides"
                    { Target = e.Target; TargetRef = targetRef; Column = col; IndexBacked = indexBacked; Verdict = verdict })
            | _ -> [])

    /// The per-candidate evidence lines (operator-facing; the paste-able
    /// move leads, the proof follows).
    let narrateEvidence (evidence: ReconcileEvidence list) : string list =
        evidence
        |> List.map (fun ev ->
            let paste = sprintf "reconcile '%s:%s'" ev.TargetRef (Name.value ev.Column)
            let basis = if ev.IndexBacked then "unique-indexed on the sink" else "name-shaped, no unique index"
            match ev.Verdict with
            | EvidenceVerdict.Probed (sinkUnique, hits, size) ->
                let uniq = if sinkUnique then "sink-unique" else "NOT sink-unique (duplicate values tiebreak to the oldest sink row)"
                let sample =
                    if size = 0 then "no non-null source values to sample"
                    else sprintf "%d/%d sampled source value(s) found in the sink" hits size
                let strength =
                    if size = 0 then "unproven"
                    elif hits < size then "PARTIAL — the unmatched source rows would halt a live reconcile"
                    elif sinkUnique then "a STRONG candidate"
                    else "a full match, but ambiguous"
                sprintf "%s (%s) — %s; %s: %s." paste basis uniq sample strength
            | EvidenceVerdict.Unprobed reason ->
                sprintf "%s (%s) — the evidence probe could not run: %s" paste basis reason)
