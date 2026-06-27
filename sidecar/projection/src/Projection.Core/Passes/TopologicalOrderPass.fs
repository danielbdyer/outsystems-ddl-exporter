namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver lineage diagnostic prose. The pass
// emits human-readable status strings (e.g., "topologicalOrder vN:
// %d nodes, %d edges, %d missing") via `sprintf` for operator-
// facing lineage entries. The structural pass output (`Order`,
// `Cycles`) is fully typed; only the diagnostic-string surface
// falls under the discipline's allowed exception per
// `DECISIONS 2026-05-09 — Built-in obligation`.

open System.Collections.Generic
open Projection.Core

/// The topological-order pass — produces a `TopologicalOrder` value
/// (per A32: passes may produce emitter-consumable values, not just
/// transformed catalogs). The pass signature is `Catalog ->
/// Lineage<TopologicalOrder>`; the catalog itself is unchanged. Schema
/// emitters ignore the result per A33; data emitters consume it for
/// FK-safe insertion ordering.
///
/// This commit covers Kahn's algorithm on **acyclic** catalogs. Cycle
/// detection (Tarjan's SCC) lands in the next commit; cycle resolution
/// (asymmetric-2-cycle resolver) in the commit after. On a cyclic input
/// today, the pass produces `Mode = Alphabetical` with a generic
/// `CycleDiagnostic` whose `Reason` notes that SCC enumeration is
/// pending the next pass version.
///
/// **Permutation invariance is the V2 contract.** V1's correctness
/// silently depends on `Dictionary<K,V>` insertion-order iteration in
/// the CLR — a property nowhere asserted in V1 tests but load-bearing
/// for V1's reproducibility. V2 makes the invariant explicit: the
/// output `TopologicalOrder` is byte-identical for any permutation of
/// the input catalog's modules / kinds / references. Achieved by
/// sorting at every internal iteration boundary by `SsKey`.
[<RequireQualifiedAccess>]
module TopologicalOrderPass =

    /// Pass version. Bump when:
    /// - cycle detection / resolution semantics change
    /// - the alphabetical-fallback ordering rule changes
    /// - the Edges / MissingEdges definition changes
    ///
    /// v1 — Kahn's only; cycles produce a single generic CycleDiagnostic.
    /// v2 — Adds Tarjan's SCC enumeration; one CycleDiagnostic per SCC.
    /// v3 — Adds edge classification (Weak / Cascade / Other) and the
    ///       asymmetric-2-cycle resolver. 2-member SCCs with exactly
    ///       one Weak edge auto-resolve; the broken edge is recorded in
    ///       `CycleDiagnostic.BreakableEdges`. 2-member SCCs with 0 or
    ///       multiple Weak edges, and SCCs of size >= 3, remain
    ///       unresolved — Mode falls back to Alphabetical.
    /// v4 — Self-loop detection: 1-member SCCs whose sole member has a
    ///       self-edge in the adjacency are reported as cycles (per
    ///       chapter 4.1.B slice δ — the data-emission path needs the
    ///       full cycle inventory to determine deferred-FK columns).
    ///       Pre-v4 single-node SCCs were dropped by the post-filter
    ///       per "Self-loops would require explicit detection — adds
    ///       when a real fixture surfaces them"; slice δ is that
    ///       fixture. The asymmetric-2-cycle resolver continues to
    ///       refuse 1-cycles (its name names the shape it handles);
    ///       the diagnostic surfaces for emitters that consume cycle
    ///       membership directly.
    [<Literal>]
    let version : int = 4

    [<Literal>]
    let private passName : string = "topologicalOrder"

    // -----------------------------------------------------------------------
    // Graph construction.
    //
    // For every reference R: kind k --references--> targetKind, the
    // precedence graph has an edge (targetKind, k.SsKey) — "targetKind
    // must come before k." Indegree[k] = number of prerequisites k has.
    //
    // The Edges field on the output records the FK orientation directly
    // (source, target) — that's what consumers care about, not the
    // precedence orientation, which is internal.
    //
    // Stable iteration: we sort kinds by SsKey before scanning so that
    // the discovered Edges and MissingEdges lists are deterministic
    // regardless of input list order.
    // -----------------------------------------------------------------------

    type private Graph = {
        Nodes              : SsKey list                       // sorted by SsKey
        Adjacency          : Map<SsKey, SsKey list>           // (parent → children)
        Indegree           : Map<SsKey, int>
        Edges              : (SsKey * SsKey) list             // (source, target) FK edges
        MissingEdges       : (SsKey * SsKey) list             // (source, target) where target absent
        ClassifiedEdges    : Map<(SsKey * SsKey), EdgeStrength>
            // Keyed by (source, target) — the FK orientation, not the
            // precedence orientation. Strengths come from
            // `CycleResolution.classify` (a V1-flavored domain rule);
            // the algebra here is unchanged if a different classifier
            // is plugged in.
    }

    let private buildGraph (selfLoops: SelfLoopPolicy) (c: Catalog) : Graph =
        let allKinds = Catalog.allKinds c
        let presentKeys = allKinds |> List.map (fun k -> k.SsKey) |> Set.ofList

        // Sort the iteration source by SsKey — this is the
        // permutation-invariance commitment in code.
        let sortedKinds = allKinds |> List.sortBy (fun k -> k.SsKey)
        let nodes = sortedKinds |> List.map (fun k -> k.SsKey)

        // Initialize adjacency and indegree maps with every node.
        let initialAdjacency =
            nodes |> List.map (fun n -> n, []) |> Map.ofList
        let initialIndegree =
            nodes |> List.map (fun n -> n, 0) |> Map.ofList

        let folder (state: Graph) (k: Kind) : Graph =
            // Sort references by SsKey too — same invariance commitment
            // at the inner level.
            let refs = k.References |> List.sortBy (fun r -> r.SsKey)
            refs
            |> List.fold (fun (st: Graph) r ->
                let edge = (k.SsKey, r.TargetKind)
                let strength = CycleResolution.classify k r
                let isSelfEdge = (r.TargetKind = k.SsKey)
                let skipSelfEdge = isSelfEdge && selfLoops = SkipSelfEdges
                if skipSelfEdge then
                    // Self-edge dropped under SkipSelfEdges policy;
                    // no graph mutation, no MissingEdge entry (the
                    // target IS present — we just don't track the
                    // dependency).
                    st
                elif Set.contains r.TargetKind presentKeys then
                    let children =
                        Map.tryFind r.TargetKind st.Adjacency
                        |> Option.defaultValue []
                    let updatedAdjacency =
                        st.Adjacency |> Map.add r.TargetKind (k.SsKey :: children)
                    let currentIndegree =
                        Map.tryFind k.SsKey st.Indegree |> Option.defaultValue 0
                    let updatedIndegree =
                        st.Indegree |> Map.add k.SsKey (currentIndegree + 1)
                    { st with
                        Adjacency       = updatedAdjacency
                        Indegree        = updatedIndegree
                        Edges           = edge :: st.Edges
                        ClassifiedEdges = Map.add edge strength st.ClassifiedEdges }
                else
                    { st with MissingEdges = edge :: st.MissingEdges }) state

        let initial =
            { Nodes           = nodes
              Adjacency       = initialAdjacency
              Indegree        = initialIndegree
              Edges           = []
              MissingEdges    = []
              ClassifiedEdges = Map.empty }

        // Per-kind graph-construction distribution surfaces under
        // `pass.topologicalOrder.kind` — one Bench sample per kind
        // scanned for FK references (the inner per-reference fold
        // runs within each sample). Bench.iterMap-style decoration
        // around the fold body preserves the accumulator threading.
        let scanned =
            sortedKinds
            |> List.fold (fun st k ->
                use _ = Bench.scope "pass.topologicalOrder.kind"
                folder st k) initial

        // Sort the Edges / MissingEdges lists so the output is
        // independent of accumulation order.
        { scanned with
            Adjacency =
                scanned.Adjacency
                |> Map.map (fun _ children -> List.sort children)
            Edges        = scanned.Edges        |> List.sort
            MissingEdges = scanned.MissingEdges |> List.sort }

    // -----------------------------------------------------------------------
    // Kahn's algorithm.
    //
    // Maintain a sorted-by-SsKey "ready" list (indegree 0). Pop the
    // smallest, append to result, decrement neighbors. Repeat. If the
    // ready list empties before all nodes are visited, there's a cycle.
    // -----------------------------------------------------------------------

    let private kahnSort (graph: Graph) : SsKey list * SsKey list =
        // Returns (sorted, unprocessed). Unprocessed is non-empty iff a
        // cycle exists. Function-local mutable Dictionary for indegree
        // (per the operating-disciplines table — Tarjan/Kahn worked
        // example); O(1) per decrement vs F# Map's O(log n).
        let indegree : Dictionary<SsKey, int> = Dictionary()
        for KeyValue(k, v) in graph.Indegree do
            indegree.[k] <- v
        let mutable ready =
            graph.Nodes
            |> List.filter (fun n ->
                match indegree.TryGetValue n with
                | true, v  -> v = 0
                | false, _ -> true)
            |> List.sort
        let result = ResizeArray<SsKey>()
        while not (List.isEmpty ready) do
            let head = List.head ready
            ready <- List.tail ready
            result.Add(head)
            let children = Map.tryFind head graph.Adjacency |> Option.defaultValue []
            for child in children do
                let current =
                    match indegree.TryGetValue child with
                    | true, v  -> v
                    | false, _ -> 0
                let next = current - 1
                indegree.[child] <- next
                if next = 0 then
                    // Insert in sorted position so the ready list stays
                    // a sorted set.
                    ready <-
                        (child :: ready)
                        |> List.sort
        let sorted = result |> List.ofSeq
        let processedSet = HashSet<SsKey>(sorted)
        let unprocessed =
            graph.Nodes
            |> List.filter (fun n -> not (processedSet.Contains n))
            |> List.sort
        sorted, unprocessed

    // -----------------------------------------------------------------------
    // Tarjan's strongly-connected-components algorithm.
    //
    // Operates on the precedence graph restricted to a node subset (the
    // unprocessed residue from Kahn's). Returns SCCs of size >= 2 — single
    // nodes without self-loops are trivially their own SCC and not
    // reported as cycles.
    //
    // Output is deterministic: members within each SCC are sorted by
    // SsKey; SCCs are sorted by their smallest member's SsKey.
    // -----------------------------------------------------------------------

    let private tarjanScc (nodes: SsKey list) (adjacency: Map<SsKey, SsKey list>) : SsKey list list =
        // Function-local mutable Dictionary / HashSet per the operating-
        // disciplines table ("Mutable state only function-local for
        // performance-sensitive algorithms (Tarjan SCC, ResizeArray
        // accumulators)"). O(1) per insert/lookup vs Map/Set's O(log n);
        // the algorithmic hot path on 300-table catalogs sees the
        // compounding effect.
        let nodeSet = HashSet<SsKey>(nodes)
        let mutable index = 0
        let indices : Dictionary<SsKey, int> = Dictionary()
        let lowlinks : Dictionary<SsKey, int> = Dictionary()
        let onStack : HashSet<SsKey> = HashSet()
        let stack = ResizeArray<SsKey>()
        let components = ResizeArray<SsKey list>()

        // Children restricted to the subset under analysis. Sorted by
        // SsKey so DFS order is deterministic.
        let childrenOf (v: SsKey) : SsKey list =
            Map.tryFind v adjacency
            |> Option.defaultValue []
            |> List.filter (fun w -> nodeSet.Contains w)
            |> List.sort

        let rec strongConnect (v: SsKey) : unit =
            indices.[v]  <- index
            lowlinks.[v] <- index
            index        <- index + 1
            stack.Add(v)
            onStack.Add(v) |> ignore
            for w in childrenOf v do
                if not (indices.ContainsKey w) then
                    strongConnect w
                    lowlinks.[v] <- min lowlinks.[v] lowlinks.[w]
                elif onStack.Contains w then
                    lowlinks.[v] <- min lowlinks.[v] indices.[w]
            if lowlinks.[v] = indices.[v] then
                let comp = ResizeArray<SsKey>()
                let mutable popping = true
                while popping do
                    let w = stack.[stack.Count - 1]
                    stack.RemoveAt(stack.Count - 1)
                    onStack.Remove(w) |> ignore
                    comp.Add(w)
                    if w = v then popping <- false
                components.Add(comp |> List.ofSeq |> List.sort)

        for v in nodes do
            if not (indices.ContainsKey v) then
                strongConnect v

        // Filter to cycle-bearing SCCs:
        //   - size ≥ 2 (the multi-node SCC case; pre-v4 behavior), OR
        //   - size = 1 AND the sole member has a self-edge in the
        //     restricted-children adjacency (the v4 self-loop case;
        //     surfaced by chapter 4.1.B slice δ — the data emission
        //     path needs cycle membership for self-referencing kinds
        //     like `employee.manager_id → employee` to populate
        //     `DeferredFkSet` and emit two-phase MERGE/UPDATE).
        let hasSelfEdge (members: SsKey list) : bool =
            match members with
            | [ v ] -> childrenOf v |> List.contains v
            | _     -> false
        components
        |> Seq.filter (fun c -> List.length c >= 2 || hasSelfEdge c)
        |> Seq.toList
        |> List.sortBy (fun c -> c |> List.head)

    // -----------------------------------------------------------------------
    // Resolver integration.
    //
    // The algebra here is: enumerate SCCs (Tarjan); ask the
    // domain-supplied resolver which FK edges to break per SCC; remove
    // the precedence-graph edges; re-run Kahn. The choice of resolver
    // is V1-flavored domain policy and lives in `CycleResolution`. This
    // pass currently uses `CycleResolution.asymmetric2CycleStrategy`;
    // pluggable resolvers arrive when an admire pass surfaces a need
    // (e.g., manual cycle overrides for known fixtures).
    // -----------------------------------------------------------------------

    type private ResolverOutcome = {
        RemovedPrecedenceEdges : Set<SsKey * SsKey>  // (parent, child) precedence
        ResolvedDiagnostics    : CycleDiagnostic list
        UnresolvedDiagnostics  : CycleDiagnostic list
    }

    let private internalEdgesOf
        (members: SsKey list)
        (classified: Map<(SsKey * SsKey), EdgeStrength>)
        : ((SsKey * SsKey) * EdgeStrength) list =
        // All (source, target) pairs where both endpoints are in the
        // SCC. Sorted by edge tuple for deterministic output.
        [ for a in members do
            for b in members do
                if a <> b then
                    match Map.tryFind (a, b) classified with
                    | Some s -> yield (a, b), s
                    | None   -> () ]
        |> List.sortBy fst

    let private applyResolver
        (resolver: CycleResolution.Resolver)
        (graph: Graph)
        (sccs: SsKey list list)
        : ResolverOutcome =
        let mutable removed : Set<SsKey * SsKey> = Set.empty
        let resolved = ResizeArray<CycleDiagnostic>()
        let unresolved = ResizeArray<CycleDiagnostic>()

        // Per-SCC cycle-resolution distribution surfaces under
        // `pass.topologicalOrder.scc` — one Bench sample per SCC
        // resolver-invocation (internal-edge enumeration +
        // resolver dispatch + diagnostic accumulation). Tarjan's
        // per-node + per-edge cost runs upstream in `tarjanScc`;
        // this surfaces the resolver pass-back-through.
        sccs
        |> Bench.iterDo "pass.topologicalOrder.scc" (fun scc ->
            let internalEdges = internalEdgesOf scc graph.ClassifiedEdges
            let step = resolver scc internalEdges
            if List.isEmpty step.EdgesToBreak then
                unresolved.Add(
                    { Members        = scc
                      BreakableEdges = []
                      Reason         = step.Reason })
            else
                for (source, target) in step.EdgesToBreak do
                    // Translate FK orientation (source, target) to
                    // precedence orientation (parent=target, child=source).
                    removed <- Set.add (target, source) removed
                resolved.Add(
                    { Members        = scc
                      BreakableEdges = step.EdgesToBreak
                      Reason         = step.Reason }))

        { RemovedPrecedenceEdges = removed
          ResolvedDiagnostics    = resolved |> List.ofSeq
          UnresolvedDiagnostics  = unresolved |> List.ofSeq }

    /// Build a reduced graph by removing precedence-graph edges. Used to
    /// re-run Kahn after the resolver breaks weak edges.
    let private reduceGraph (graph: Graph) (toRemove: Set<SsKey * SsKey>) : Graph =
        if Set.isEmpty toRemove then graph
        else
            let reducedAdjacency =
                graph.Adjacency
                |> Map.map (fun parent children ->
                    children
                    |> List.filter (fun child -> not (Set.contains (parent, child) toRemove)))
            let reducedIndegree =
                toRemove
                |> Set.fold (fun indeg (_, child) ->
                    let current = Map.tryFind child indeg |> Option.defaultValue 0
                    Map.add child (max 0 (current - 1)) indeg)
                    graph.Indegree
            { graph with Adjacency = reducedAdjacency; Indegree = reducedIndegree }

    // -----------------------------------------------------------------------
    // The pass.
    // -----------------------------------------------------------------------

    /// Pillar 9 (chapter A.4.7 slice α): the events emitted by this
    /// pass are all `Touched` (one per graph node from Kahn ordering),
    /// classified `DataIntent` — the SortKahn site is topology-derived
    /// (no operator opinion in node visitation). The SelfLoopHandling
    /// site (an `OperatorIntent Ordering` site per the chapter A.4.7
    /// open's Q9-trigger-fires worked example) affects `buildGraph`
    /// but emits no per-event lineage at this slice. The Sites
    /// distinction lands at slice ε with the registry entry; per-event
    /// classification here is uniformly `DataIntent`.
    let private classification : Classification = DataIntent

    let private touchedEvent (key: SsKey) : LineageEvent =
        { PassName       = passName
          PassVersion    = version
          SsKey          = key
          TransformKind  = Touched
          Classification = classification }

    /// Parameterized form. Per session-36 — `selfLoops` selects how a
    /// kind's reference to itself is handled during graph construction.
    /// `TreatAsCycle` (default) preserves pre-session-36 semantics;
    /// `SkipSelfEdges` drops self-references during construction so
    /// emitters whose target syntax allows inline self-FK constraints
    /// see the kind in topological position. Same algorithm, two
    /// projections — replaces the `RawTextEmitter.emissionOrder`
    /// duplicate (Agent 4 #6).
    let runWith (selfLoops: SelfLoopPolicy) (c: Catalog) : Lineage<TopologicalOrder> =
        let graph = buildGraph selfLoops c
        let sorted, unprocessed = kahnSort graph

        let result =
            if List.isEmpty unprocessed then
                // Acyclic — the output order is the topological sort.
                { Mode         = Topological
                  Order        = sorted
                  Edges        = graph.Edges
                  MissingEdges = graph.MissingEdges
                  Cycles       = []
                  Diagnostics  = [
                      sprintf "topologicalOrder v%d: %d nodes, %d edges, %d missing"
                          version graph.Nodes.Length graph.Edges.Length graph.MissingEdges.Length
                  ] }
            else
                // Cycle present. Run Tarjan's SCC on the unprocessed
                // residue, then ask the asymmetric-2-cycle resolver
                // which Weak edges it's willing to break.
                let sccs = tarjanScc unprocessed graph.Adjacency
                let resolution =
                    applyResolver CycleResolution.asymmetric2CycleStrategy graph sccs

                if List.isEmpty resolution.UnresolvedDiagnostics then
                    // Every SCC resolved. Re-run Kahn on the reduced graph.
                    let reduced = reduceGraph graph resolution.RemovedPrecedenceEdges
                    let resorted, residue = kahnSort reduced
                    if List.isEmpty residue then
                        { Mode         = Topological
                          Order        = resorted
                          Edges        = graph.Edges
                          MissingEdges = graph.MissingEdges
                          // Resolved cycles stay in Cycles for audit —
                          // they record the SCCs found and the edges
                          // broken to resolve them.
                          Cycles       = resolution.ResolvedDiagnostics
                          Diagnostics  = [
                              sprintf "topologicalOrder v%d: %d cycle(s) auto-resolved via Weak-edge removal"
                                  version resolution.ResolvedDiagnostics.Length
                          ] }
                    else
                        // Defensive: removing the resolver's chosen edges
                        // should always make the graph acyclic, but if a
                        // bug or unforeseen graph shape leaves residue
                        // we degrade gracefully.
                        let leftover = tarjanScc residue reduced.Adjacency
                        let leftoverDiagnostics =
                            leftover
                            |> List.map (fun members ->
                                { Members        = members
                                  BreakableEdges = []
                                  Reason         = "residual SCC after resolver; please report" })
                        { Mode         = Alphabetical
                          Order        = graph.Nodes
                          Edges        = graph.Edges
                          MissingEdges = graph.MissingEdges
                          Cycles       = resolution.ResolvedDiagnostics @ leftoverDiagnostics
                          Diagnostics  = [
                              sprintf "topologicalOrder v%d: resolver left residue; alphabetical fallback"
                                  version
                          ] }
                else
                    // At least one SCC the resolver can't handle. Fall
                    // back to alphabetical; record both the resolved and
                    // unresolved diagnostics so callers can audit.
                    let alphabeticalAll = graph.Nodes
                    { Mode         = Alphabetical
                      Order        = alphabeticalAll
                      Edges        = graph.Edges
                      MissingEdges = graph.MissingEdges
                      Cycles       = resolution.ResolvedDiagnostics @ resolution.UnresolvedDiagnostics
                      Diagnostics  = [
                          sprintf "topologicalOrder v%d: %d resolved, %d unresolved; alphabetical fallback"
                              version
                              resolution.ResolvedDiagnostics.Length
                              resolution.UnresolvedDiagnostics.Length
                      ] }

        let events = graph.Nodes |> List.map touchedEvent
        Lineage.ofValueAndEvents events result

    /// Run the topological-order pass with the default self-loop
    /// policy. Returns a `Lineage<TopologicalOrder>`; the catalog
    /// itself is not modified. One `Touched` event per kind scanned
    /// (per A25). Equivalent to `runWith TreatAsCycle`.
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `TopologicalOrderPass.registered.Run`
    let private run (c: Catalog) : Lineage<TopologicalOrder> =
        use _ = Bench.scope "passes.topologicalOrder"
        runWith TreatAsCycle c

    /// Chapter A.4.7 slice γ. **The chapter A.4.7 open Q9-trigger-
    /// fires worked example**. Two sites — `sortKahn` (DataIntent;
    /// Kahn ordering is topology-derived) and `selfLoopHandling`
    /// (OperatorIntent Ordering; `SelfLoopPolicy` is the named
    /// real-evidence trigger for the fifth `OverlayAxis` variant per
    /// `DECISIONS 2026-05-16 (chapter A.4.7 slice β) — OverlayAxis
    /// gains fifth variant Ordering`). The default `registered` uses
    /// `TreatAsCycle`; `registeredWith` exposes the configurable
    /// variant for slice ε consumers.
    let registered : RegisteredTransform<Catalog, TopologicalOrder> =
        { Name = passName
          Domain = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName = "sortKahn"
                Classification = DataIntent
                Rationale = "Kahn topological sort over kinds' SsKey-keyed reference graph. Deterministic; depends only on graph topology; no operator opinion." }
              { SiteName = "selfLoopHandling"
                Classification = OperatorIntent Ordering
                Rationale = "SelfLoopPolicy (TreatAsCycle | SkipSelfEdges) controls how a kind's reference to itself is handled during graph construction. Operator-supplied (passed via `runWith`); the chapter A.4.7 open's Q9-trigger-fires fifth OverlayAxis worked example. The default `registered` captures TreatAsCycle; `registeredWith` exposes the configurable variant." } ]
          Run = fun c -> run c |> Lineage.map Diagnostics.ofValue
          Status = Active }

    /// Chapter A.4.7 slice γ — configurable variant. Lets slice ε
    /// (the OrderingPolicy-stage consumer) supply the `SelfLoopPolicy`
    /// explicitly. The Sites list is the same as the default
    /// `registered`; the Run closure differs.
    let registeredWith (selfLoops: SelfLoopPolicy) : RegisteredTransform<Catalog, TopologicalOrder> =
        { registered with
            Run = fun c -> runWith selfLoops c |> Lineage.map Diagnostics.ofValue }

    // -----------------------------------------------------------------------
    // H-040 — JunctionDeferred mode.
    //
    // A "junction kind" (bridge / join table) is a Kind with ≥2 FK
    // References AND ≤2 non-PK Attributes. When DeferJunctionKinds is
    // requested the pass partitions the topological order into non-junction
    // kinds (FK-safe topological sequence) and junction kinds (appended
    // alphabetically), producing Mode = JunctionDeferred.
    // -----------------------------------------------------------------------

    /// True iff `k` is a junction kind: ≥2 FK references and ≤2
    /// non-PK attributes. Non-PK attributes that serve as FK columns
    /// are included in the count — junctions typically have no
    /// payload columns beyond their FK pair.
    let internal isJunctionKind (k: Kind) : bool =
        let fkCount = List.length k.References
        let nonPkAttrCount =
            k.Attributes |> List.filter (fun a -> not a.IsPrimaryKey) |> List.length
        fkCount >= 2 && nonPkAttrCount <= 2

    /// Full-config variant of the pass. The two axes (`SelfLoops` and
    /// `JunctionDeferral`) are independent; `runWithConfig` threads
    /// both through a single call. `runWith selfLoops` is equivalent
    /// to `runWithConfig { SelfLoops = selfLoops; JunctionDeferral =
    /// EmitInTopologicalOrder }`.
    let runWithConfig (config: OrderingConfig) (c: Catalog) : Lineage<TopologicalOrder> =
        let base_ = runWith config.SelfLoops c
        match config.JunctionDeferral with
        | EmitInTopologicalOrder -> base_
        | DeferJunctionKinds ->
            // Apply junction deferral as a post-processing step on the
            // already-computed topological order. The partitioning is
            // pure on Order and the catalog's kind list.
            base_
            |> Lineage.map (fun t ->
                let allKindsByKey =
                    Catalog.allKinds c
                    |> List.map (fun k -> k.SsKey, k)
                    |> Map.ofList
                let junctions, nonJunctions =
                    t.Order
                    |> List.partition (fun key ->
                        Map.tryFind key allKindsByKey
                        |> Option.map isJunctionKind
                        |> Option.defaultValue false)
                let deferredOrder =
                    nonJunctions @ (List.sort junctions)
                { t with
                    Mode  = JunctionDeferred
                    Order = deferredOrder })

    /// Configurable registered variant — exposes both ordering axes.
    /// The Sites list mirrors `registered`; the Run closure captures
    /// the supplied config.
    let registeredWithConfig (config: OrderingConfig) : RegisteredTransform<Catalog, TopologicalOrder> =
        { registered with
            Run = fun c -> runWithConfig config c |> Lineage.map Diagnostics.ofValue }

    // -----------------------------------------------------------------------
    // H-037 — Schema island detection.
    //
    // An "island" is a maximal weakly-connected component of the FK
    // graph with no edges to any other component. Islands indicate
    // isolated sub-schemas that share no FK relationships with the
    // rest of the catalog — likely integration seams or orphaned legacy
    // tables. One Warning DiagnosticEntry is emitted per island of
    // size ≥ 2 (single-kind components are unremarkable).
    //
    // Algorithm: BFS over the undirected projection of t.Edges. Each
    // connected component is one island. Function-local mutable
    // HashSet for visited tracking (O(1) per lookup; same discipline
    // as Tarjan/Kahn above).
    // -----------------------------------------------------------------------

    /// Detect schema islands: maximal weakly-connected components of
    /// the undirected FK graph. Takes a pre-computed `TopologicalOrder`
    /// (carries the edge set) and the full list of catalog kind keys.
    ///
    /// One Warning `DiagnosticEntry` is emitted per island of ≥2
    /// members. DiagnosticCode: `"topology.island"`.
    let runIslandDetection
        (allKeys: SsKey list)
        (t: TopologicalOrder)
        : Lineage<Diagnostics<IslandReport>> =
        use _ = Bench.scope "pass.islandDetection"

        // The shared canonical undirected adjacency (deduped + sorted + self-skip;
        // all benign for this weakly-connected-component BFS — it visits each node
        // once via `visited`, so neighbor dups / order / self-loops never change
        // the components). See `TopologicalOrder.undirectedAdjacency`.
        let undirected = TopologicalOrder.undirectedAdjacency t

        let visited = HashSet<SsKey>()
        let components = ResizeArray<SsKey list>()

        for key in List.sort allKeys do
            if not (visited.Contains key) then
                // BFS from key.
                let queue = Queue<SsKey>()
                let nodeMembers = ResizeArray<SsKey>()
                queue.Enqueue(key)
                visited.Add(key) |> ignore
                while queue.Count > 0 do
                    let v = queue.Dequeue()
                    nodeMembers.Add(v)
                    let neighbors =
                        Map.tryFind v undirected |> Option.defaultValue []
                    for n in neighbors do
                        if not (visited.Contains n) then
                            visited.Add(n) |> ignore
                            queue.Enqueue(n)
                components.Add(nodeMembers |> List.ofSeq |> List.sort)

        // A schema "island" implies isolation from the rest. When the
        // catalog is one big component, there are no islands — the
        // catalog is fully connected. When the catalog splits into
        // multiple components, each non-singleton component is an
        // island. Singletons are excluded per the test contract.
        let nonSingletonComponents =
            components
            |> Seq.filter (fun c -> List.length c >= 2)
            |> Seq.toList
            |> List.sortBy List.head

        let islands =
            if List.length nonSingletonComponents <= 1 && Seq.length components <= 1 then []
            else nonSingletonComponents

        let report = { Islands = islands }

        let diagnostics =
            islands
            |> List.mapi (fun i members ->
                let memberStr =
                    members
                    |> List.map SsKey.rootOriginal
                    |> String.concat ", "
                DiagnosticEntry.create passName DiagnosticSeverity.Warning
                    "topology.island"
                    (sprintf "Schema island #%d: %d kinds with no FK path to the rest of the catalog: [%s]"
                        (i + 1) (List.length members) memberStr))

        let events = allKeys |> List.map touchedEvent
        lineageDiagnostics {
            do! LineageDiagnostics.writeLineages events
            do! LineageDiagnostics.writeDiagnostics diagnostics
            return report
        }

    // -----------------------------------------------------------------------
    // H-039 — Cascade shock zone detection.
    //
    // A "cascade shock zone" is a set of tables reachable by following
    // Cascade-tagged FK edges depth-first from a root kind. A zone with
    // ≥3 reachable kinds is a potential cascading-delete / cascading-update
    // risk. One Warning DiagnosticEntry is emitted per qualifying zone.
    //
    // Algorithm: for each kind k, DFS following only Cascade-strength
    // edges. The Cascade subset of t.Edges is derived by looking up each
    // (src, tgt) pair's `Reference` on the source kind via the catalog
    // and running `CycleResolution.classify`.
    // -----------------------------------------------------------------------

    /// NM-36 — the cascade-shock analytics pass is a SEPARATE registered
    /// chain step (it produces an operator-facing risk warning, not the
    /// topological order), so it carries its own pass name. Its diagnostics
    /// and lineage events are stamped with `cascadeShockPassName` rather
    /// than the topology pass's `passName`, keeping the registry Name, the
    /// `DiagnosticEntry.Pass`, and the `LineageEvent.PassName` consistent.
    let cascadeShockPassName : string = "cascadeShockZones"

    let private cascadeShockTouchedEvent (key: SsKey) : LineageEvent =
        { PassName       = cascadeShockPassName
          PassVersion    = version
          SsKey          = key
          TransformKind  = Touched
          Classification = classification }

    /// Detect cascade shock zones: sets of kinds reachable by chasing
    /// Cascade-strength FK edges from a root, with |Reachable| ≥ 3.
    ///
    /// Takes the `Catalog` (to classify each edge) and the pre-computed
    /// `TopologicalOrder` (carries the edge set to avoid re-running the
    /// graph build). Returns one Warning `DiagnosticEntry` per qualifying
    /// zone. DiagnosticCode: `"topology.cascadeShock"`.
    let runCascadeShockZones
        (catalog: Catalog)
        (t: TopologicalOrder)
        : Lineage<Diagnostics<CascadeShockZone list>> =
        use _ = Bench.scope "pass.cascadeShockZones"

        // Build a lookup from (source SsKey) → Kind for classify calls.
        let kindByKey =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey, k)
            |> Map.ofList

        // Derive cascade-only adjacency from t.Edges by classifying each edge.
        let cascadeAdj =
            t.Edges
            |> List.fold (fun (acc: Map<SsKey, SsKey list>) (src, tgt) ->
                match Map.tryFind src kindByKey with
                | None -> acc
                | Some srcKind ->
                    let isCascade =
                        srcKind.References
                        |> List.exists (fun r ->
                            r.TargetKind = tgt &&
                            CycleResolution.classify srcKind r = EdgeStrength.Cascade)
                    if isCascade then
                        let existing = Map.tryFind src acc |> Option.defaultValue []
                        Map.add src (tgt :: existing) acc
                    else acc)
                Map.empty
        // Normalize adjacency lists (sort for determinism).
        let cascadeAdj =
            cascadeAdj |> Map.map (fun _ children -> List.sort children)

        // DFS reachability from each root via cascade edges.
        let reachableFrom (root: SsKey) : SsKey list =
            let visited = System.Collections.Generic.HashSet<SsKey>()
            visited.Add(root) |> ignore
            let stack = System.Collections.Generic.Stack<SsKey>()
            stack.Push(root)
            while stack.Count > 0 do
                let v = stack.Pop()
                let children =
                    Map.tryFind v cascadeAdj |> Option.defaultValue []
                for c in children do
                    if not (visited.Contains c) then
                        visited.Add(c) |> ignore
                        stack.Push(c)
            visited
            |> Seq.filter (fun k -> k <> root)
            |> Seq.toList
            |> List.sort

        let allKeys =
            Catalog.allKinds catalog |> List.map (fun k -> k.SsKey) |> List.sort

        let zones =
            allKeys
            |> List.choose (fun root ->
                let reachable = reachableFrom root
                if List.length reachable >= 3 then
                    Some { Root = root; Reachable = reachable }
                else None)
            |> List.sortBy (fun z -> z.Root)

        let diagnostics =
            zones
            |> List.map (fun z ->
                let reachStr =
                    z.Reachable
                    |> List.map SsKey.rootOriginal
                    |> String.concat ", "
                { DiagnosticEntry.create cascadeShockPassName DiagnosticSeverity.Warning
                    "topology.cascadeShock"
                    (sprintf "Cascade shock zone rooted at %s: %d kinds reachable via CASCADE FK edges: [%s]"
                        (SsKey.rootOriginal z.Root) (List.length z.Reachable) reachStr)
                  with SsKey = Some z.Root })

        let events = allKeys |> List.map cascadeShockTouchedEvent
        lineageDiagnostics {
            do! LineageDiagnostics.writeLineages events
            do! LineageDiagnostics.writeDiagnostics diagnostics
            return zones
        }

    /// NM-36 — registry metadata for the cascade-shock analytics pass,
    /// mirroring `SchemaComplexityPass.registered`. The pass reads the
    /// `Catalog` and the pre-computed `TopologicalOrder` from ComposeState
    /// at apply-time; like the other advisory analytics passes its single
    /// site is `DataIntent` (the cascade-risk warning is graph-derived, not
    /// an operator opinion). The `Run` is a placeholder for the metadata
    /// projection — the chain step's real execution lift is
    /// `liftCatalogTopologyPass cascadeShockPassName runCascadeShockZones`,
    /// which reads the live topology from ComposeState (the metadata Run is
    /// never invoked in the chain, matching the `SchemaComplexityPass`
    /// precedent's `registered None`).
    let cascadeShockRegistered : RegisteredTransform<Catalog, CascadeShockZone list> =
        { Name         = cascadeShockPassName
          Domain       = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName       = "cascadeShockZones"
                Classification = DataIntent
                Rationale      = "Cascade-delete / cascade-update risk advisory: sets of ≥3 kinds reachable by chasing CASCADE-strength FK edges from a root. Graph-derived; no operator opinion. Emits one Warning DiagnosticEntry per qualifying zone (topology.cascadeShock)." } ]
          Run    = fun c -> runCascadeShockZones c TopologicalOrder.empty
          Status = Active }
