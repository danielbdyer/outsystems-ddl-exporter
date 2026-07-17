namespace Projection.Pipeline

// THE EVIDENCE CACHE (2026-07-10, the manifest program, slice 2 —
// THE_TRANSFER_MANIFEST.md §4.2-§4.5): the row substrate behind the decision
// workbench, read ONCE per board build from the same connections the
// authoritative dry run uses, and the PURE derivation of every answer
// archetype's exact consequence over it.
//
// The load-bearing decision (§4.2): every archetype's `ForecastDelta` is
// computed once, authoritatively, at board build — matches run through the
// SAME `Reconciliation.reconcileKindWith` the engine's live run uses, over
// the FULL candidate rowsets (never the TOP-200 strength sample). A later
// toggle is a pure lookup; no IO enters any reducer, and there is no second
// forecast derivation to diverge from the committed one.
//
// The recompute unit is the weakly-connected FK COMPONENT (§4.3): a row that
// one edge's unresolved reference drops never lands, so its other references
// are moot — per-target counts are honest only when the component's edges are
// resolved together. Independent components genuinely do not share match
// state, so holding them fixed is honest.
//
// Discover-once / derive-pure (the house `EvidenceCache` pattern, named in
// CLAUDE.md §6). The one IO seam is `fill`; everything beneath it is pure.
//
// Memory bound: the cache holds FULL rows only for the escape-TARGET kinds
// (the reconcile candidates — reference-sized tables), and exactly TWO
// columns (pk, fk) per referencing in-scope kind. The payload tables' full
// rows are never cached here.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

[<RequireQualifiedAccess>]
module EvidenceCache =

    // -- the cache -------------------------------------------------------------

    type Cache =
        { /// Escape-target kinds' FULL source rows (Values keyed by logical
          /// attribute Name — the SsKey-aligned comparison plane).
          SourceRows : Map<SsKey, StaticRow list>
          /// Escape-target kinds' FULL sink rows (the reconcile candidates).
          SinkRows   : Map<SsKey, StaticRow list>
          /// Per referencing (kind, escaping column): the source rows'
          /// (pk value, fk value) pairs; a NULL fk reads as "".
          References : Map<SsKey * Name, (string * string) list>
          /// Per (target kind, candidate column): the sink's
          /// (total, distinct) non-null counts — the exact uniqueness fact.
          Uniqueness : Map<SsKey * Name, int64 * int64> }

    /// The four real answer archetypes an escaping reference resolves by —
    /// each maps onto a `SupportingRelationship` case with a pure desugar.
    /// `Drop` is deliberately NOT an answer: the engine cannot NULL or drop
    /// an escaping reference; accepting loss is a named refusal, never a
    /// gate-clearing selection (§2, §10-A).
    [<RequireQualifiedAccess>]
    type Answer =
        /// existing-reference: match the target's rows by this column;
        /// references re-key onto the sink's own identities.
        | Reconcile of column: Name
        /// shared-anchor: every reference re-keys onto one sink row. The
        /// anchor may be unchosen (`None`) — the delta is already exact
        /// (every non-blank reference re-keys; nothing drops), because the
        /// fallback catches every miss by construction.
        | Pin of anchor: AssignedKey option
        /// widen: the target joins `tables` and transfers too; the sink
        /// mints its keys and references re-point through the capture.
        | Widen
        /// static-lookup: the same match as Reconcile, PLUS the held-identical
        /// assertion the run refuses on when violated.
        | StaticLookup of column: Name

    /// The exact consequence of one answer, computed over the full cached
    /// rowsets — never a sample extrapolation.
    type ForecastDelta =
        { /// referencing rows whose value re-keys onto a sink identity.
          RowsRekeyed       : int
          /// referencing rows that would drop — this target's reference
          /// resolves to no sink row under the answer.
          RowsDropped       : int
          /// rows of the target itself that enter the transfer (Widen only).
          RowsEnteringScope : int
          /// tables the answer adds to the write scope (Widen: 1).
          TablesTouched     : int
          /// decision keys this answer OPENS — Widen's fixpoint cascade
          /// (§4.5): the newly-escaping targets the widened kind points at.
          SpawnedKeys       : SsKey list
          /// decision keys this answer CLOSES (the edge's own target).
          ResolvedKeys      : SsKey list }

    /// One answer's full evidence: the delta, the exact sink-uniqueness fact,
    /// and the fingerprint INPUTS slice 4 hashes (the matched business-key
    /// values with their resolved sink identities, and the unmatched values)
    /// — named typed products of the ONE authoritative pass (§4.4, §5.2).
    type AnswerEvidence =
        { Answer          : Answer
          Delta           : ForecastDelta
          /// Some (total = distinct && total > 0) for a match answer's
          /// column; None where uniqueness does not apply (Pin, Widen).
          SinkUnique      : bool option
          /// (business-key value, resolved sink identity) per matched source
          /// row of the TARGET kind — the effect-fingerprint input.
          MatchedPairs    : (string * string) list
          /// target-kind source business-key values with no sink match.
          UnmatchedValues : string list }

    // -- pure derivations --------------------------------------------------------

    let private pkNameOf (catalog: Catalog) (kind: SsKey) : Name option =
        Catalog.tryFindKind kind catalog
        |> Option.bind (fun k -> k.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey))
        |> Option.map (fun a -> a.Name)

    let private isBlank (v: string) = System.String.IsNullOrWhiteSpace v

    /// The resolver one answer induces for a target: does a referencing fk
    /// value land on a sink identity? Plus the match products. Pure — every
    /// match runs through the SAME Core `reconcileKindWith` the engine uses.
    let private resolverFor
        (cache: Cache)
        (catalog: Catalog)
        (target: SsKey)
        (answer: Answer)
        : (string -> bool) * (string * string) list * string list * bool option =
        let srcRows = cache.SourceRows |> Map.tryFind target |> Option.defaultValue []
        let sinkRows = cache.SinkRows |> Map.tryFind target |> Option.defaultValue []
        let matchBy (col: Name) =
            match pkNameOf catalog target with
            | None -> (fun _ -> false), [], [], None
            | Some pkName ->
                let outcome =
                    Reconciliation.reconcileKindWith Set.empty target pkName
                        (ReconciliationStrategy.MatchByColumn col) srcRows sinkRows
                let resolves (fkVal: string) =
                    SurrogateRemapContext.tryFindAssigned target (SourceKey.ofString fkVal) outcome.Remap
                    |> Option.isSome
                let matched =
                    srcRows
                    |> List.choose (fun r ->
                        match StaticRow.value pkName r with
                        | Some pk ->
                            SurrogateRemapContext.tryFindAssigned target (SourceKey.ofString pk) outcome.Remap
                            |> Option.map (fun assigned ->
                                // Blessing-stable flatten (NULL → "") — these
                                // pairs feed the consent fingerprints.
                                StaticRow.valueOrEmpty col r, AssignedKey.value assigned)
                        | None -> None)
                let unmatched =
                    outcome.UnmatchedRows
                    |> List.map (fun (_, r) -> StaticRow.valueOrEmpty col r)
                let unique =
                    cache.Uniqueness
                    |> Map.tryFind (target, col)
                    |> Option.map (fun (total, distinct) -> total = distinct && total > 0L)
                resolves, matched, unmatched, unique
        match answer with
        | Answer.Reconcile col
        | Answer.StaticLookup col -> matchBy col
        | Answer.Pin _ -> (fun _ -> true), [], [], None
        | Answer.Widen -> (fun _ -> true), [], [], None

    /// The match products one column answer yields for a target — the
    /// (business-key value, resolved sink identity) pairs, the unmatched
    /// values, and the target's exact (total, distinct) sink counts on the
    /// column. Published for the act-consent fingerprints (the second
    /// consumer, slice 4a — `ActEvidence.fingerprintsOf` hashes exactly
    /// these); `componentDeltas` consumes the same resolver internally, so
    /// the workbench's forecast and the blessing's fingerprint read ONE match.
    let matchProducts (cache: Cache) (catalog: Catalog) (target: SsKey) (column: Name)
        : (string * string) list * string list * (int64 * int64) option =
        let _, matched, unmatched, _ = resolverFor cache catalog target (Answer.Reconcile column)
        matched, unmatched, (cache.Uniqueness |> Map.tryFind (target, column))

    /// Widen's fixpoint keyset diff (§4.5): the escaping targets that APPEAR
    /// when `target` joins the load set, minus those already open.
    let private widenSpawns
        (catalog: Catalog)
        (loadSet: Set<SsKey>)
        (reconciled: Set<SsKey>)
        (openTargets: Set<SsKey>)
        (target: SsKey)
        : SsKey list =
        PeerTransfer.escapingFks catalog (Set.add target loadSet) reconciled
        |> List.map (fun e -> e.Target)
        |> List.distinct
        |> List.filter (fun t -> t <> target && not (Set.contains t openTargets))
        |> List.sortBy SsKey.rootOriginal

    /// Compute every target's evidence for the component under ONE selection
    /// map (target -> answer). The component's edges resolve TOGETHER: a
    /// referencing row survives only when EVERY non-blank escaping value it
    /// carries resolves, so one target's unresolved reference honestly
    /// shrinks its siblings' re-key counts (§4.3).
    let componentDeltas
        (cache: Cache)
        (catalog: Catalog)
        (loadSet: Set<SsKey>)
        (reconciled: Set<SsKey>)
        (edges: PeerTransfer.EscapingFk list)
        (selections: Map<SsKey, Answer>)
        : Map<SsKey, AnswerEvidence> =
        let openTargets = edges |> List.map (fun e -> e.Target) |> Set.ofList
        // one resolver (+ products) per target, under its selected answer
        let byTarget =
            openTargets
            |> Set.toList
            |> List.map (fun t ->
                let answer = selections |> Map.tryFind t |> Option.defaultValue (Answer.Pin None)
                t, (answer, resolverFor cache catalog t answer))
            |> Map.ofList
        // per referencing kind: its escaping (column, target) pairs
        let edgesByKind =
            edges |> List.groupBy (fun e -> e.Kind)
        // per target: (rekeyed, dropped) over SURVIVING rows
        let counts =
            [ for (kind, kindEdges) in edgesByKind do
                // the kind's reference pairs, one list per escaping column
                let columns =
                    kindEdges
                    |> List.map (fun e ->
                        e.Column, e.Target,
                        (cache.References |> Map.tryFind (kind, e.Column) |> Option.defaultValue []))
                // row-major view: pk -> [(target, fkVal)]
                let rows =
                    columns
                    |> List.collect (fun (_, target, pairs) -> pairs |> List.map (fun (pk, fk) -> pk, (target, fk)))
                    |> List.groupBy fst
                    |> List.map (fun (pk, entries) -> pk, entries |> List.map snd)
                for (_, refs) in rows do
                    let survives =
                        refs
                        |> List.forall (fun (target, fk) ->
                            isBlank fk
                            || (match byTarget |> Map.tryFind target with
                                | Some (_, (resolves, _, _, _)) -> resolves fk
                                | None -> true))
                    for (target, fk) in refs do
                        if not (isBlank fk) then
                            match byTarget |> Map.tryFind target with
                            | Some (_, (resolves, _, _, _)) ->
                                if not (resolves fk) then yield target, (0, 1)
                                elif survives then yield target, (1, 0)
                                // resolves but the row drops on a sibling edge:
                                // neither re-keyed nor dropped FOR this target.
                            | None -> () ]
            |> List.groupBy fst
            |> List.map (fun (t, xs) ->
                t, (xs |> List.sumBy (fun (_, (r, _)) -> r), xs |> List.sumBy (fun (_, (_, d)) -> d)))
            |> Map.ofList
        openTargets
        |> Set.toList
        |> List.map (fun t ->
            let answer, (_, matchedPairs, unmatched, unique) = byTarget.[t]
            let rekeyed, dropped = counts |> Map.tryFind t |> Option.defaultValue (0, 0)
            let delta =
                match answer with
                | Answer.Widen ->
                    { RowsRekeyed = rekeyed; RowsDropped = dropped
                      RowsEnteringScope = cache.SourceRows |> Map.tryFind t |> Option.defaultValue [] |> List.length
                      TablesTouched = 1
                      SpawnedKeys = widenSpawns catalog loadSet reconciled openTargets t
                      ResolvedKeys = [ t ] }
                | _ ->
                    { RowsRekeyed = rekeyed; RowsDropped = dropped
                      RowsEnteringScope = 0; TablesTouched = 0
                      SpawnedKeys = []; ResolvedKeys = [ t ] }
            t, { Answer = answer; Delta = delta; SinkUnique = unique
                 MatchedPairs = matchedPairs; UnmatchedValues = unmatched })
        |> Map.ofList

    /// The candidate answers a target offers: one Reconcile per candidate
    /// column, the unchosen Pin, Widen, and one StaticLookup per candidate.
    let candidateAnswers (edges: PeerTransfer.EscapingFk list) (target: SsKey) : Answer list =
        let candidates =
            edges
            |> List.filter (fun e -> e.Target = target)
            |> List.collect (fun e -> e.CandidateReconcileColumns)
            |> List.distinct
        [ for c in candidates -> Answer.Reconcile c ]
        @ [ Answer.Pin None; Answer.Widen ]
        @ [ for c in candidates -> Answer.StaticLookup c ]

    /// Every answer's evidence for every target of the component — each
    /// candidate evaluated with the component's OTHER targets held at their
    /// current selection, and the whole component recomputed as a unit, so a
    /// sibling's stale delta can never render (§4.3). Deterministic: keyed
    /// maps; list orders derive from the sorted target set.
    let perAnswerDeltas
        (cache: Cache)
        (catalog: Catalog)
        (loadSet: Set<SsKey>)
        (reconciled: Set<SsKey>)
        (edges: PeerTransfer.EscapingFk list)
        (selections: Map<SsKey, Answer>)
        : Map<SsKey, Map<Answer, AnswerEvidence>> =
        let targets = edges |> List.map (fun e -> e.Target) |> List.distinct |> List.sortBy SsKey.rootOriginal
        targets
        |> List.map (fun t ->
            let answers = candidateAnswers edges t
            let per =
                answers
                |> List.map (fun a ->
                    let evidence = componentDeltas cache catalog loadSet reconciled edges (Map.add t a selections)
                    a, evidence.[t])
                |> Map.ofList
            t, per)
        |> Map.ofList

    /// Group the escaping edges into their weakly-connected FK components —
    /// the recompute units. Reuses the impact model's segmentation over the
    /// load set plus every edge's endpoints, so the workbench and the impact
    /// artifact agree on what "one unit" is.
    let componentsOf (catalog: Catalog) (loadSet: Set<SsKey>) (escapes: PeerTransfer.EscapingFk list) : PeerTransfer.EscapingFk list list =
        let scope = escapes |> List.fold (fun s e -> s |> Set.add e.Kind |> Set.add e.Target) loadSet
        TransferImpact.segmentKinds catalog scope
        |> List.choose (fun members ->
            let ms = Set.ofList members
            match escapes |> List.filter (fun e -> Set.contains e.Kind ms) with
            | [] -> None
            | es -> Some es)

    // -- the one IO seam: fill -----------------------------------------------------

    let private rowsOf (cnn: SqlConnection) (kind: Kind) : Task<StaticRow list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT * FROM [%s].[%s];" (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)  // LINT-ALLOW: validated TableId coordinates at the read boundary
            use! r = cmd.ExecuteReaderAsync()
            let ord = [ for i in 0 .. r.FieldCount - 1 -> r.GetName i, i ] |> Map.ofList
            let acc = System.Collections.Generic.List<StaticRow>()
            let mutable go = true
            while go do
                let! more = r.ReadAsync()
                if more then
                    let values =
                        kind.Attributes
                        |> List.choose (fun a ->
                            match Map.tryFind (ColumnRealization.columnNameText a.Column) ord with
                            // WP-3 (F11): SQL NULL reads as `None`; a genuine
                            // empty string as `Some ""` — distinct end-to-end.
                            | Some i -> Some (a.Name, (if r.IsDBNull i then None else Some (string (r.GetValue i))))
                            | None -> None)
                        |> Map.ofList
                    acc.Add { Identifier = kind.SsKey; Values = values }
                else go <- false
            return List.ofSeq acc
        }

    /// Hoisted per survival rule 5: a bare conditional expression in the
    /// resumable body fails FS3511 reduction in Release; a helper call
    /// reduces.
    let private cellText (r: System.Data.Common.DbDataReader) (i: int) : string =
        if r.IsDBNull i then "" else string (r.GetValue i)

    let private pairsOf (cnn: SqlConnection) (kind: Kind) (pkCol: string) (fkCol: string) : Task<(string * string) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT [%s], [%s] FROM [%s].[%s];" pkCol fkCol (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)  // LINT-ALLOW: validated coordinates
            use! r = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<string[]>()
            let mutable go = true
            while go do
                let! more = r.ReadAsync()
                if more then acc.Add [| cellText r 0; cellText r 1 |]
                else go <- false
            return acc |> Seq.map (fun a -> a.[0], a.[1]) |> List.ofSeq
        }

    let private uniquenessOf (cnn: SqlConnection) (kind: Kind) (col: string) : Task<int64 * int64> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG([%s]), COUNT_BIG(DISTINCT [%s]) FROM [%s].[%s];" col col (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)  // LINT-ALLOW: validated coordinates
            use! r = cmd.ExecuteReaderAsync()
            let! hasRow = r.ReadAsync()
            ignore hasRow
            return (r.GetInt64 0, r.GetInt64 1)
        }

    /// One escape-target read, resolved pure before the task opens (the
    /// FS3511 discipline: no tuple pattern heads a `let!` inside `task { }`).
    type private TargetRead =
        { Key : SsKey; SourceKind : Kind; SinkKind : Kind }

    type private UniquenessRead =
        { Key : SsKey; Column : Name; SinkKind : Kind; ColumnText : string }

    type private ReferenceRead =
        { Kind : SsKey; Column : Name; SourceKind : Kind; PkText : string; FkText : string }

    /// Read the row substrate ONCE, from the SAME connections the
    /// authoritative dry run uses: the escape-target kinds' full rows on both
    /// sides, each referencing kind's (pk, fk) pairs on the source, and the
    /// exact sink uniqueness per candidate column. Everything downstream of
    /// this call is pure. The catalog resolution happens BEFORE the task
    /// opens; the task body is a flat single-value spine (survival rule 5).
    let fill
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (escapes: PeerTransfer.EscapingFk list)
        : Task<Cache> =
        let targetReads =
            escapes
            |> List.map (fun e -> e.Target)
            |> List.distinct
            |> List.choose (fun t ->
                match Catalog.tryFindKind t sourceContract, Catalog.tryFindKind t sinkContract with
                | Some srcKind, Some sinkKind -> Some { Key = t; SourceKind = srcKind; SinkKind = sinkKind }
                | _ -> None)
        let uniquenessReads =
            targetReads
            |> List.collect (fun tr ->
                escapes
                |> List.filter (fun e -> e.Target = tr.Key)
                |> List.collect (fun e -> e.CandidateReconcileColumns)
                |> List.distinct
                |> List.choose (fun c ->
                    tr.SinkKind.Attributes
                    |> List.tryFind (fun a -> a.Name = c)
                    |> Option.map (fun attr ->
                        { Key = tr.Key; Column = c; SinkKind = tr.SinkKind
                          ColumnText = ColumnRealization.columnNameText attr.Column })))
        let referenceReads =
            escapes
            |> List.choose (fun e ->
                Catalog.tryFindKind e.Kind sourceContract
                |> Option.bind (fun kind ->
                    let pkCol = kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey)
                    let fkCol = kind.Attributes |> List.tryFind (fun a -> a.Name = e.Column)
                    match pkCol, fkCol with
                    | Some pk, Some fk ->
                        Some { Kind = e.Kind; Column = e.Column; SourceKind = kind
                               PkText = ColumnRealization.columnNameText pk.Column
                               FkText = ColumnRealization.columnNameText fk.Column }
                    | _ -> None))
        task {
            let mutable sourceRows = Map.empty
            let mutable sinkRows = Map.empty
            let mutable uniqueness = Map.empty
            let mutable references = Map.empty
            for tr in targetReads do
                let! src = rowsOf source tr.SourceKind
                let! snk = rowsOf sink tr.SinkKind
                sourceRows <- Map.add tr.Key src sourceRows
                sinkRows <- Map.add tr.Key snk sinkRows
            for ur in uniquenessReads do
                let! u = uniquenessOf sink ur.SinkKind ur.ColumnText
                uniqueness <- Map.add (ur.Key, ur.Column) u uniqueness
            for rr in referenceReads do
                let! pairs = pairsOf source rr.SourceKind rr.PkText rr.FkText
                references <- Map.add (rr.Kind, rr.Column) pairs references
            return { SourceRows = sourceRows; SinkRows = sinkRows; References = references; Uniqueness = uniqueness }
        }
