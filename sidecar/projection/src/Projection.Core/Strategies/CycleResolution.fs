namespace Projection.Core

// LINT-ALLOW-FILE: cycle-resolver diagnostic prose. The strategy
// emits human-readable status strings via `sprintf` for operator-
// facing lineage entries (e.g., "SCC of size %d; current resolver
// handles 2-cycles only"). Same allowed-exception class as
// `TopologicalOrderPass.fs` and `Bench.fs` per `DECISIONS
// 2026-05-09 — Built-in obligation`.

/// Domain rules for cycle resolution in topological-sort passes.
///
/// The pure graph algebra (Kahn's algorithm + Tarjan's SCC) lives in
/// `TopologicalOrderPass`. The rules in *this* module are V2's
/// codification of V1's RDBMS-flavored semantic choices about which
/// FK edges are "soft" enough to break, and how to choose among
/// candidates when more than one is breakable. They live here, named
/// explicitly as domain logic, rather than threaded into the algebra
/// they parameterize.
///
/// `EdgeStrength` and `classifyEdge` together form the **classifier** —
/// a function from `(Kind, Reference)` to a strength label. The
/// classifier reads two IR fields (source attribute's `IsNullable`,
/// reference's `OnDelete`) and applies the V1 rule:
///   - `Cascade`  : `OnDelete = Cascade`. Structural — never breakable.
///   - `Weak`     : nullable + `(NoAction | SetNull)`. Breakable
///                  without violating data semantics.
///   - `Other`    : not nullable + `(NoAction | SetNull | Restrict)`.
///                  Breaking would orphan rows; never broken.
///
/// `weakFeedbackStrategy` is the **resolver** — 2026-07-07's
/// generalization of V1's asymmetric-2-cycle heuristic (retired the
/// same day; its behavior is the 2-cycle row of the new case map).
/// It breaks a weak-edge feedback set in ANY SCC whose every cycle
/// carries at least one Weak edge, and refuses — naming the exact
/// cycle — when a cycle of non-deferrable edges exists.
///
/// The classifier is a V1 carryover from the `EntityDependencySorter`
/// admire (ADMIRE.md, 2026-05-07). Future strategies (manual cycle
/// overrides — deferred with a named trigger, DECISIONS 2026-07-07 —
/// deferred-junction handling) live alongside in this module — never
/// inside the pure algebra.
[<RequireQualifiedAccess>]
type EdgeStrength =
    | Weak
    | Cascade
    | Other


[<RequireQualifiedAccess>]
module CycleResolution =

    /// Classify an FK reference by source-attribute nullability and
    /// `OnDelete` action. The classifier reads only IR fields — it does
    /// not need any external context — but the *rule* it applies is
    /// V1's domain policy, not algebra. Replace the classifier wholesale
    /// when V2 admits a non-RDBMS catalog whose edges have a different
    /// breakability semantics.
    let classify (sourceKind: Kind) (r: Reference) : EdgeStrength =
        let sourceAttrIsNullable =
            sourceKind.Attributes
            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
            |> Option.map (fun a -> a.Column.IsNullable)
            |> Option.defaultValue false
        match r.OnDelete, sourceAttrIsNullable with
        | Cascade, _                     -> EdgeStrength.Cascade
        | (NoAction | SetNull), true     -> EdgeStrength.Weak
        | _                              -> EdgeStrength.Other

    /// A closed directed cycle every edge of which is non-Weak — the
    /// refusal's CERTIFICATE (v7; DECISIONS 2026-07-18). The private
    /// constructor makes I3's refusal half unforgeable at the diagnostic
    /// surface: a value of this type cannot exist unless the edge list
    /// (a) forms a closed cycle (each edge's target is the next edge's
    /// source, wrapping) and (b) carries zero Weak edges. The certificate
    /// is what the operator checks — and what the cheapest relaxation is
    /// computed against.
    type StrongCycleCertificate = private { CertEdges : ((SsKey * SsKey) * EdgeStrength) list }

    [<RequireQualifiedAccess>]
    module StrongCycleCertificate =

        let create (edges: ((SsKey * SsKey) * EdgeStrength) list) : Result<StrongCycleCertificate, string> =
            if List.isEmpty edges then Error "a certificate needs at least one edge"
            elif edges |> List.exists (fun (_, s) -> s = EdgeStrength.Weak) then
                Error "a strong-cycle certificate cannot carry a Weak edge"
            else
                let closed =
                    edges
                    |> List.mapi (fun i ((_, t), _) ->
                        let ((nextS, _), _) = edges.[(i + 1) % edges.Length]
                        t = nextS)
                    |> List.forall id
                if closed then Ok { CertEdges = edges }
                else Error "a certificate's edges must form a closed cycle"

        let edges (c: StrongCycleCertificate) : ((SsKey * SsKey) * EdgeStrength) list = c.CertEdges

        let members (c: StrongCycleCertificate) : SsKey list =
            c.CertEdges |> List.map (fst >> fst) |> List.distinct |> List.sort

    /// HOW a resolution's break set was chosen (v7) — the objective the
    /// solver ran, or the named downgrade when it could not.
    [<RequireQualifiedAccess>]
    type BreakObjective =
        /// The exact minimum of `(Σ cost, cardinality, lexicographic)`
        /// over feasible weak subsets. Carries the chosen set's total cost.
        | MinimalBreakSet of totalCost: int64
        /// The v5 greedy walk, called directly (its own callers remain).
        | GreedyWalk
        /// The greedy walk running because the SCC's weak-edge count
        /// exceeded the exact threshold — the NAMED downgrade.
        | GreedyAboveThreshold of weakEdges: int * threshold: int

    /// WHY a resolution step produced its break set (v7; cash-out of the
    /// 2026-07-07 deferral "`ResolutionStep.Reason` free-form string →
    /// structured DU", trigger = the second strategy's arrival). Display
    /// goes through `describe` / `CycleDiagnostic.reasonText` — sites
    /// emit the DU, the projection owns the copy.
    [<RequireQualifiedAccess>]
    type ResolutionReason =
        /// A weak feedback set was broken; the objective names how.
        | AutoResolved of BreakObjective
        /// An all-strong cycle exists — the certificate carries it, and
        /// `Relaxation` names the cheapest strong edges whose columns,
        /// made nullable, admit automatic resolution ([] when the
        /// relaxation solver's own threshold was exceeded).
        | Refused of certificate: StrongCycleCertificate * relaxation: (SsKey * SsKey) list
        /// The degenerate arm: no cycle found among a supposed SCC's
        /// internal edges (structurally unreachable off Tarjan; kept
        /// legible).
        | NoCycleFound
        /// The `neverResolve` strategy declined by configuration.
        | Disabled of sccSize: int

    /// The output of a resolver step on a single SCC. `EdgesToBreak`
    /// are FK-orientation edges (source → target) the resolver
    /// authorizes the algebra to remove; `Reason` is the TYPED rationale
    /// (v7) — render via `describe`, never parse.
    type ResolutionStep = {
        EdgesToBreak : (SsKey * SsKey) list
        Reason       : ResolutionReason
    }

    /// A resolver: given an SCC's members and the FK-orientation edges
    /// within the SCC (with their classifier-assigned strengths), return
    /// the edges to break and a reason for the diagnostic.
    type Resolver =
        SsKey list -> ((SsKey * SsKey) * EdgeStrength) list -> ResolutionStep

    /// Combine two strengths of PARALLEL references over the same
    /// (source, target) pair: the pair is breakable only if EVERY parallel
    /// reference is Weak — a Cascade or non-nullable sibling still binds
    /// the dependency. Shared by the pass's graph construction and the
    /// resolver's defensive dedupe (2026-07-06 adversarial MEDIUM #9
    /// lifted here so there is ONE combiner).
    let combineStrength (a: EdgeStrength) (b: EdgeStrength) : EdgeStrength =
        match a, b with
        | EdgeStrength.Cascade, _ | _, EdgeStrength.Cascade -> EdgeStrength.Cascade
        | EdgeStrength.Other, _ | _, EdgeStrength.Other -> EdgeStrength.Other
        | _ -> EdgeStrength.Weak

    /// Deterministic directed-cycle search over FK-orientation edges:
    /// DFS with SsKey-sorted roots and adjacency; returns the EDGE LIST of
    /// the first cycle found (including a self-edge as a 1-edge cycle),
    /// or None when the graph is acyclic. Deterministic by construction —
    /// the same edge set yields the same cycle regardless of input order.
    let private findCycle (edges: (SsKey * SsKey) list) : (SsKey * SsKey) list option =
        let adjacency =
            edges
            |> List.groupBy fst
            |> List.map (fun (s, es) -> s, es |> List.map snd |> List.distinct |> List.sort)
            |> Map.ofList
        let nodes = edges |> List.collect (fun (s, t) -> [ s; t ]) |> List.distinct |> List.sort
        let mutable found : (SsKey * SsKey) list option = None
        let visited = System.Collections.Generic.HashSet<SsKey>()
        let onStack = System.Collections.Generic.HashSet<SsKey>()
        let rec dfs (path: SsKey list) (v: SsKey) : unit =
            if Option.isNone found then
                visited.Add v |> ignore
                onStack.Add v |> ignore
                for w in (Map.tryFind v adjacency |> Option.defaultValue []) do
                    if Option.isNone found then
                        if onStack.Contains w then
                            // Back-edge (v, w) closes the cycle: the path
                            // suffix from w to v, plus (v, w).
                            let suffix =
                                (v :: path)
                                |> List.rev
                                |> List.skipWhile (fun n -> n <> w)
                            let cycleEdges = List.pairwise suffix @ [ (v, w) ]
                            found <- Some cycleEdges
                        elif not (visited.Contains w) then
                            dfs (v :: path) w
                onStack.Remove v |> ignore
        for n in nodes do
            if Option.isNone found && not (visited.Contains n) then dfs [] n
        found

    let private edgeText ((s, t): SsKey * SsKey) : string =
        sprintf "%s -> %s" (SsKey.rootOriginal s) (SsKey.rootOriginal t)

    /// The display projection of a resolution step (v7) — the ONE owner
    /// of the resolver's operator copy. Preserves the pre-DU phrases
    /// exactly (existing substring pins and operator familiarity), plus
    /// the named-downgrade suffix for the above-threshold arm.
    let describe (step: ResolutionStep) : string =
        match step.Reason with
        | ResolutionReason.AutoResolved objective ->
            let baseText =
                sprintf "auto-resolved: %d weak (nullable) FK edge(s) deferred to phase 2 (%s)"
                    step.EdgesToBreak.Length
                    (step.EdgesToBreak |> List.sort |> List.truncate 3 |> List.map edgeText |> String.concat "; ")
            match objective with
            | BreakObjective.MinimalBreakSet _ | BreakObjective.GreedyWalk -> baseText
            | BreakObjective.GreedyAboveThreshold (weakEdges, threshold) ->
                sprintf "%s [greedy above the exact threshold: %d weak edges > %d]"
                    baseText weakEdges threshold
        | ResolutionReason.Refused (certificate, _) ->
            sprintf "unresolvable: a cycle of non-deferrable FK edges among [%s] — every edge on it is non-nullable or cascade; make one of its FK columns nullable (it then defers to phase 2 automatically), or transfer without these kinds"
                (StrongCycleCertificate.members certificate |> List.map SsKey.rootOriginal |> String.concat ", ")
        | ResolutionReason.NoCycleFound ->
            "no cycle found among the SCC's internal edges; please report"
        | ResolutionReason.Disabled sccSize ->
            sprintf "SCC of size %d; resolver disabled (neverResolve)" sccSize

    /// The WEAK-FEEDBACK resolver (2026-07-07) — generalizes and retires
    /// V1's asymmetric-2-cycle heuristic. THE COMPLETE CASE MAP over an
    /// SCC's classified internal edges:
    ///
    ///   | SCC shape                                   | outcome |
    ///   |---------------------------------------------|---------|
    ///   | size 1, Weak self-edge                      | resolved — self-edge deferred (the 2026-07-06 rule, preserved) |
    ///   | size 1, non-Weak self-edge                  | refused — a mandatory/cascade self-FK cannot defer |
    ///   | size 2, exactly one Weak inter-edge         | resolved — that edge (V1's asymmetric arm, byte-compatible) |
    ///   | size 2, BOTH inter-edges Weak               | resolved — one edge, deterministically chosen |
    ///   | size 2, zero Weak                           | refused — names the non-deferrable cycle |
    ///   | size ≥3, every cycle carries ≥1 Weak edge   | resolved — a weak feedback set, one break per residual cycle |
    ///   | size ≥3, some all-strong cycle              | refused — names that cycle's members |
    ///   | Cascade edges (any nullability)             | never broken — V1's structural rule stands |
    ///
    /// **Algorithm** (greedy weak-feedback): find a cycle (deterministic
    /// DFS); if every edge on it is non-Weak, REFUSE naming exactly that
    /// cycle; otherwise break the smallest Weak edge ON that cycle and
    /// repeat until acyclic. Termination: every step removes one edge.
    ///
    /// **Invariants** (property-tested in `CycleResolutionTests`):
    ///   I1 soundness  — broken ⊆ Weak: every broken edge has a nullable
    ///                   source column, so the two-phase load defers it
    ///                   (phase-1 NULL, phase-2 re-point).
    ///   I2 acyclicity — on resolution, the SCC minus the broken edges is
    ///                   acyclic (the pass's post-resolve Kahn re-run is a
    ///                   defensive re-check, not the guarantee).
    ///   I3 refusal precision — refuses ⟺ the SCC contains a cycle whose
    ///                   edges are ALL non-Weak (only strong edges survive
    ///                   removal, so a found all-strong cycle exists in the
    ///                   original graph; conversely an all-strong cycle can
    ///                   never be broken and is eventually found).
    ///   I4 determinism — input-order-independent (sorted roots,
    ///                   adjacency, and edge choice).
    ///   I5 frugality  — breaks one edge per residual cycle, never a
    ///                   blanket sweep of every weak edge (a pure weak
    ///                   ring of any size breaks exactly one edge).
    let weakFeedbackStrategy : Resolver =
        fun members internalEdges ->
            let memberSet = Set.ofList members
            // Defensive dedupe + parallel-strength combination + input-order
            // independence (the pass already dedupes; a direct caller may not).
            let edges =
                internalEdges
                |> List.filter (fun ((s, t), _) -> Set.contains s memberSet && Set.contains t memberSet)
                |> List.groupBy fst
                |> List.map (fun (e, xs) -> e, xs |> List.map snd |> List.reduce combineStrength)
                |> List.sortBy fst
            let strengthOf = Map.ofList edges
            let rec resolve (broken: (SsKey * SsKey) list) (remaining: (SsKey * SsKey) list) : ResolutionStep =
                match findCycle remaining with
                | None ->
                    match broken with
                    | [] ->
                        // Unreachable for a genuine SCC (Tarjan only hands
                        // cycle-bearing components); degrade legibly.
                        { EdgesToBreak = []
                          Reason       = ResolutionReason.NoCycleFound }
                    | _ ->
                        { EdgesToBreak = List.sort broken
                          Reason       = ResolutionReason.AutoResolved BreakObjective.GreedyWalk }
                | Some cycleEdges ->
                    let weakOnCycle =
                        cycleEdges
                        |> List.filter (fun e ->
                            Map.tryFind e strengthOf = Some EdgeStrength.Weak)
                        |> List.sort
                    match weakOnCycle with
                    | [] ->
                        // The cycle found is all-strong AT THIS STEP —
                        // its edges' recorded strengths certify it (the
                        // ctor re-checks: closed + zero Weak).
                        let certified =
                            cycleEdges
                            |> List.map (fun e ->
                                e, Map.tryFind e strengthOf |> Option.defaultValue EdgeStrength.Other)
                        match StrongCycleCertificate.create certified with
                        | Ok cert ->
                            { EdgesToBreak = []
                              Reason       = ResolutionReason.Refused (cert, []) }
                        | Error _ ->
                            // Structurally unreachable (findCycle returns a
                            // closed cycle; zero-Weak just filtered).
                            { EdgesToBreak = []
                              Reason       = ResolutionReason.NoCycleFound }
                    | chosen :: _ ->
                        resolve (chosen :: broken) (remaining |> List.filter (fun e -> e <> chosen))
            resolve [] (edges |> List.map fst)

    /// The "never resolve" strategy — refuse to break any cycle.
    /// Useful for callers that prefer the alphabetical fallback over
    /// any heuristic edge-breaking.
    let neverResolve : Resolver =
        fun members _ ->
            { EdgesToBreak = []
              Reason       = ResolutionReason.Disabled members.Length }

    // -----------------------------------------------------------------------
    // v7 (DECISIONS 2026-07-18) — the MINIMAL weak feedback set. The break
    // choice becomes measured: one resolver family over one cost axis.
    // -----------------------------------------------------------------------

    /// Cost of breaking an FK edge — ‖repair(e)‖ (the Phase-2 UPDATE's row
    /// count, which is its CDC capture count — T15's norm) when evidence is
    /// present; 0 when absent. At zero cost everywhere the objective
    /// `(Σ cost, cardinality, lexicographic)` degenerates to
    /// `(cardinality, lexicographic)`: the schema-only exact strategy IS
    /// this family at the zero cost function (A40 — one parameterized
    /// algorithm, not two strategies).
    type EdgeCost = (SsKey * SsKey) -> int64

    /// The exact solver enumerates weak-edge subsets: 2^12 = 4096
    /// candidates at most, each checked by one O(V+E) cycle search over
    /// an SCC's internal edges — bounded and Bench-labeled. Above the
    /// threshold the greedy walk runs and the downgrade is NAMED in the
    /// reason (downgrades never silent).
    [<Literal>]
    let exactWeakEdgeThreshold : int = 12

    /// The shared exact-subset solver: enumerate subsets of `candidates`
    /// (ascending bitmask over the SORTED list — deterministic), keep the
    /// feasible minimum of the TOTAL objective `(Σ cost, cardinality,
    /// lexicographic edge list)`. Feasibility: `fixedEdges` plus the
    /// unchosen candidates form an acyclic graph. Two consumers (A40):
    /// the weak-feedback break choice (fixed = strong, candidates =
    /// weak) and the refusal's cheapest relaxation (fixed = [],
    /// candidates = the strong-only subgraph).
    let private minimalSubset
        (cost: (SsKey * SsKey) -> int64)
        (fixedEdges: (SsKey * SsKey) list)
        (candidates: (SsKey * SsKey) list)
        : ((SsKey * SsKey) list * int64) option =
        let arr = candidates |> List.sort |> List.toArray
        let n = arr.Length
        let mutable best : (int64 * int * (SsKey * SsKey) list) option = None
        for mask in 0 .. (1 <<< n) - 1 do
            let subset =
                [ for i in 0 .. n - 1 do
                    if mask &&& (1 <<< i) <> 0 then yield arr.[i] ]
            let remaining = fixedEdges @ (arr |> Array.toList |> List.except subset)
            if Option.isNone (findCycle remaining) then
                let candidate = (subset |> List.sumBy cost, List.length subset, List.sort subset)
                match best with
                | None -> best <- Some candidate
                | Some current -> if compare candidate current < 0 then best <- Some candidate
        best |> Option.map (fun (c, _, s) -> s, c)

    /// The cheapest relaxation of a refused component: the minimal set of
    /// STRONG edges whose columns, made nullable (Weak), admit automatic
    /// resolution — i.e. whose removal makes the strong-only subgraph
    /// acyclic. Cost prefers `Other` (a nullability change: cost 1) over
    /// `Cascade` (a delete-rule change: cost 1,000,000) — the objective's
    /// Σ-cost component only reaches for a cascade edge when no
    /// nullability-only relaxation exists. `[]` above the solver
    /// threshold — the display prose names the omission.
    let private relaxationOf
        (strongClassified: ((SsKey * SsKey) * EdgeStrength) list)
        : (SsKey * SsKey) list =
        if List.length strongClassified > exactWeakEdgeThreshold then []
        else
            let strengthOf = Map.ofList strongClassified
            let costOf (e: SsKey * SsKey) : int64 =
                match Map.tryFind e strengthOf with
                | Some EdgeStrength.Cascade -> 1_000_000L
                | _ -> 1L
            match minimalSubset costOf [] (strongClassified |> List.map fst) with
            | Some (relaxed, _) -> relaxed
            | None -> []

    /// The v7 resolver — the minimal weak feedback set (DECISIONS
    /// 2026-07-18; #669 the measured-minimality program).
    ///
    /// Per SCC:
    ///   1. The strong-only subgraph (every non-Weak internal edge) is
    ///      cycle-checked DIRECTLY: a cycle there IS an all-strong cycle
    ///      of the original — I3's refusal predicate computed in one
    ///      step rather than discovered by greedy descent. Refusal names
    ///      the cycle's members (same operator phrasing as v5).
    ///   2. Otherwise every cycle carries a Weak edge, so a weak feedback
    ///      set exists. With |Weak| ≤ `exactWeakEdgeThreshold` the solver
    ///      enumerates subsets in deterministic order (sorted edges,
    ///      ascending bitmask) and keeps the feasible subset minimizing
    ///      `(Σ cost, cardinality, lexicographic edge list)` — a TOTAL
    ///      order, so the choice is deterministic and permutation-
    ///      invariant. Feasibility = the SCC minus the subset is acyclic.
    ///   3. Above the threshold the v5 greedy walk resolves (it cannot
    ///      refuse here — step 1 already proved every cycle carries a
    ///      Weak edge) and the reason NAMES the downgrade.
    ///
    /// **Invariants** (property-tested in `CycleResolutionTests`):
    ///   I1 soundness  — broken ⊆ Weak (only weak subsets are enumerated;
    ///                   the greedy fallback preserves its own I1).
    ///   I2 acyclicity — feasibility is checked against the full internal
    ///                   edge set, exactly.
    ///   I3 refusal precision — refuses ⟺ the strong-only subgraph is
    ///                   cyclic ⟺ an all-strong cycle exists.
    ///   I4 determinism — sorted enumeration + a total objective.
    ///   I5′ minimality — on the exact path the break set has minimum
    ///                   cardinality (at zero cost) among ALL feasible
    ///                   weak subsets; ties resolve lexicographically.
    ///                   v5's I5 frugality (one break per residual cycle)
    ///                   survives as a corollary: the minimum is never
    ///                   larger than greedy's set.
    ///   I6 exact optimality — no feasible weak subset with a strictly
    ///                   smaller objective exists (test-side independent
    ///                   enumeration).
    ///   I7 exact ≤ greedy — |exact| ≤ |greedy| on the same graph.
    let minimalFeedbackStrategy (cost: EdgeCost) : Resolver =
        fun members internalEdges ->
            let memberSet = Set.ofList members
            // Same prelude as the greedy engine: dedupe, combine parallel
            // strengths, sort — input-order independence.
            let edges =
                internalEdges
                |> List.filter (fun ((s, t), _) -> Set.contains s memberSet && Set.contains t memberSet)
                |> List.groupBy fst
                |> List.map (fun (e, xs) -> e, xs |> List.map snd |> List.reduce combineStrength)
                |> List.sortBy fst
            let strongEdges =
                edges |> List.choose (fun (e, s) -> if s <> EdgeStrength.Weak then Some e else None)
            let weakEdges =
                edges |> List.choose (fun (e, s) -> if s = EdgeStrength.Weak then Some e else None)
            match findCycle strongEdges with
            | Some strongCycle ->
                // I3, computed directly: the strong-only subgraph carries a
                // cycle — no weak subset can break it. The found cycle IS
                // the certificate; the relaxation names the cheapest strong
                // edges whose columns, made nullable, admit resolution.
                let certified =
                    strongCycle
                    |> List.map (fun e ->
                        e,
                        edges
                        |> List.tryPick (fun (e', s) -> if e' = e then Some s else None)
                        |> Option.defaultValue EdgeStrength.Other)
                let strongClassified =
                    edges |> List.filter (fun (_, s) -> s <> EdgeStrength.Weak)
                match StrongCycleCertificate.create certified with
                | Ok cert ->
                    { EdgesToBreak = []
                      Reason       = ResolutionReason.Refused (cert, relaxationOf strongClassified) }
                | Error _ ->
                    // Structurally unreachable (findCycle's cycle is closed;
                    // every edge came from the strong-only subset).
                    { EdgesToBreak = []
                      Reason       = ResolutionReason.NoCycleFound }
            | None ->
                let n = List.length weakEdges
                if n = 0 then
                    // No weak edges and the strong subgraph is acyclic —
                    // the whole SCC is acyclic. Unreachable for a genuine
                    // Tarjan component; degrade legibly (v5's arm).
                    { EdgesToBreak = []
                      Reason       = ResolutionReason.NoCycleFound }
                elif n <= exactWeakEdgeThreshold then
                    let runExact () =
                        use _ = Bench.scope "pass.topologicalOrder.exactSolver"
                        minimalSubset cost strongEdges weakEdges
                    match runExact () with
                    | Some (chosen, totalCost) when not (List.isEmpty chosen) ->
                        { EdgesToBreak = chosen
                          Reason       = ResolutionReason.AutoResolved (BreakObjective.MinimalBreakSet totalCost) }
                    | _ ->
                        // The empty subset feasible would mean the SCC was
                        // acyclic (handled above); no feasible subset at all
                        // cannot happen (every cycle carries a Weak edge, so
                        // the full weak set is feasible). Degrade legibly.
                        { EdgesToBreak = []
                          Reason       = ResolutionReason.NoCycleFound }
                else
                    // Above the exact threshold: the greedy walk resolves
                    // (refusal is impossible here — step 1 proved every
                    // cycle carries a Weak edge) and the downgrade is NAMED.
                    let greedy = weakFeedbackStrategy members internalEdges
                    { greedy with
                        Reason =
                            ResolutionReason.AutoResolved
                                (BreakObjective.GreedyAboveThreshold (n, exactWeakEdgeThreshold)) }

    /// The v7 default: the exact minimal feedback set at ZERO cost —
    /// schema-only, minimum cardinality, lexicographic ties. The
    /// evidence-weighted member of the family is the same function at
    /// `repairCostOf` (the render-plane binding supplies it; A18 keeps
    /// the chain prefix schema-only).
    let defaultStrategy : Resolver =
        minimalFeedbackStrategy (fun _ -> 0L)
