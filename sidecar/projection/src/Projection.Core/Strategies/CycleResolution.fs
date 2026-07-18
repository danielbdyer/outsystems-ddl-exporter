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

    /// The output of a resolver step on a single SCC. `EdgesToBreak`
    /// are FK-orientation edges (source → target) the resolver
    /// authorizes the algebra to remove; `Reason` is a human-readable
    /// diagnostic recorded in `CycleDiagnostic.Reason`.
    type ResolutionStep = {
        EdgesToBreak : (SsKey * SsKey) list
        Reason       : string
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
                          Reason       = "no cycle found among the SCC's internal edges; please report" }
                    | _ ->
                        { EdgesToBreak = List.sort broken
                          Reason       =
                            sprintf "auto-resolved: %d weak (nullable) FK edge(s) deferred to phase 2 (%s)"
                                broken.Length
                                (broken |> List.sort |> List.truncate 3 |> List.map edgeText |> String.concat "; ") }
                | Some cycleEdges ->
                    let weakOnCycle =
                        cycleEdges
                        |> List.filter (fun e ->
                            Map.tryFind e strengthOf = Some EdgeStrength.Weak)
                        |> List.sort
                    match weakOnCycle with
                    | [] ->
                        let cycleMembers =
                            cycleEdges |> List.map fst |> List.distinct |> List.sort
                        { EdgesToBreak = []
                          Reason       =
                            sprintf "unresolvable: a cycle of non-deferrable FK edges among [%s] — every edge on it is non-nullable or cascade; make one of its FK columns nullable (it then defers to phase 2 automatically), or transfer without these kinds"
                                (cycleMembers |> List.map SsKey.rootOriginal |> String.concat ", ") }
                    | chosen :: _ ->
                        resolve (chosen :: broken) (remaining |> List.filter (fun e -> e <> chosen))
            resolve [] (edges |> List.map fst)

    /// The "never resolve" strategy — refuse to break any cycle.
    /// Useful for callers that prefer the alphabetical fallback over
    /// any heuristic edge-breaking.
    let neverResolve : Resolver =
        fun members _ ->
            { EdgesToBreak = []
              Reason       =
                sprintf "SCC of size %d; resolver disabled (neverResolve)"
                    members.Length }

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
                // cycle — no weak subset can break it. Same phrasing as v5's
                // refusal (operator copy + substring pins preserved).
                let cycleMembers =
                    strongCycle |> List.map fst |> List.distinct |> List.sort
                { EdgesToBreak = []
                  Reason       =
                    sprintf "unresolvable: a cycle of non-deferrable FK edges among [%s] — every edge on it is non-nullable or cascade; make one of its FK columns nullable (it then defers to phase 2 automatically), or transfer without these kinds"
                        (cycleMembers |> List.map SsKey.rootOriginal |> String.concat ", ") }
            | None ->
                let n = List.length weakEdges
                if n = 0 then
                    // No weak edges and the strong subgraph is acyclic —
                    // the whole SCC is acyclic. Unreachable for a genuine
                    // Tarjan component; degrade legibly (v5's arm).
                    { EdgesToBreak = []
                      Reason       = "no cycle found among the SCC's internal edges; please report" }
                elif n <= exactWeakEdgeThreshold then
                    use _ = Bench.scope "pass.topologicalOrder.exactSolver"
                    let weakArr = weakEdges |> List.sort |> List.toArray
                    // Enumerate subsets ascending-bitmask over the SORTED
                    // weak edges; keep the feasible minimum of the TOTAL
                    // objective (Σ cost, |S|, lexicographic edge list).
                    let mutable best : (int64 * int * (SsKey * SsKey) list) option = None
                    for mask in 0 .. (1 <<< n) - 1 do
                        let subset =
                            [ for i in 0 .. n - 1 do
                                if mask &&& (1 <<< i) <> 0 then yield weakArr.[i] ]
                        let remaining =
                            strongEdges @ (weakArr |> Array.toList |> List.except subset)
                        if Option.isNone (findCycle remaining) then
                            let candidate =
                                (subset |> List.sumBy cost, List.length subset, List.sort subset)
                            match best with
                            | None -> best <- Some candidate
                            | Some current -> if compare candidate current < 0 then best <- Some candidate
                    match best with
                    | Some (_, _, chosen) when not (List.isEmpty chosen) ->
                        { EdgesToBreak = chosen
                          Reason       =
                            sprintf "auto-resolved: %d weak (nullable) FK edge(s) deferred to phase 2 (%s)"
                                chosen.Length
                                (chosen |> List.truncate 3 |> List.map edgeText |> String.concat "; ") }
                    | _ ->
                        // The empty subset feasible would mean the SCC was
                        // acyclic (handled above); no feasible subset at all
                        // cannot happen (every cycle carries a Weak edge, so
                        // the full weak set is feasible). Degrade legibly.
                        { EdgesToBreak = []
                          Reason       = "no cycle found among the SCC's internal edges; please report" }
                else
                    // Above the exact threshold: the greedy walk resolves
                    // (refusal is impossible here — step 1 proved every
                    // cycle carries a Weak edge) and the downgrade is NAMED.
                    let greedy = weakFeedbackStrategy members internalEdges
                    { greedy with
                        Reason =
                            sprintf "%s [greedy above the exact threshold: %d weak edges > %d]"
                                greedy.Reason n exactWeakEdgeThreshold }

    /// The v7 default: the exact minimal feedback set at ZERO cost —
    /// schema-only, minimum cardinality, lexicographic ties. The
    /// evidence-weighted member of the family is the same function at
    /// `repairCostOf` (the render-plane binding supplies it; A18 keeps
    /// the chain prefix schema-only).
    let defaultStrategy : Resolver =
        minimalFeedbackStrategy (fun _ -> 0L)
