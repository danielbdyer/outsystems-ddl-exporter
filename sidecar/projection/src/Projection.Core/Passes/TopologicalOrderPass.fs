namespace Projection.Core.Passes

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
    [<Literal>]
    let version : int = 2

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
        Nodes        : SsKey list                       // sorted by SsKey
        Adjacency    : Map<SsKey, SsKey list>           // (parent → children)
        Indegree     : Map<SsKey, int>
        Edges        : (SsKey * SsKey) list             // (source, target) FK edges
        MissingEdges : (SsKey * SsKey) list             // (source, target) where target absent
    }

    let private buildGraph (c: Catalog) : Graph =
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
                if Set.contains r.TargetKind presentKeys then
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
                        Adjacency = updatedAdjacency
                        Indegree  = updatedIndegree
                        Edges     = edge :: st.Edges }
                else
                    { st with MissingEdges = edge :: st.MissingEdges }) state

        let initial =
            { Nodes        = nodes
              Adjacency    = initialAdjacency
              Indegree     = initialIndegree
              Edges        = []
              MissingEdges = [] }

        let scanned = sortedKinds |> List.fold folder initial

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
        // cycle exists.
        let mutable indegree = graph.Indegree
        let mutable ready =
            graph.Nodes
            |> List.filter (fun n ->
                Map.tryFind n indegree |> Option.defaultValue 0 = 0)
            |> List.sort
        let result = ResizeArray<SsKey>()
        while not (List.isEmpty ready) do
            let head = List.head ready
            ready <- List.tail ready
            result.Add(head)
            let children = Map.tryFind head graph.Adjacency |> Option.defaultValue []
            for child in children do
                let current = Map.tryFind child indegree |> Option.defaultValue 0
                let next = current - 1
                indegree <- Map.add child next indegree
                if next = 0 then
                    // Insert in sorted position so the ready list stays
                    // a sorted set.
                    ready <-
                        (child :: ready)
                        |> List.sort
        let sorted = result |> List.ofSeq
        let unprocessed =
            graph.Nodes
            |> List.filter (fun n -> not (List.contains n sorted))
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
        let nodeSet = Set.ofList nodes
        let mutable index = 0
        let mutable indices : Map<SsKey, int> = Map.empty
        let mutable lowlinks : Map<SsKey, int> = Map.empty
        let mutable onStack : Set<SsKey> = Set.empty
        let stack = ResizeArray<SsKey>()
        let components = ResizeArray<SsKey list>()

        // Children restricted to the subset under analysis. Sorted by
        // SsKey so DFS order is deterministic.
        let childrenOf (v: SsKey) : SsKey list =
            Map.tryFind v adjacency
            |> Option.defaultValue []
            |> List.filter (fun w -> Set.contains w nodeSet)
            |> List.sort

        let rec strongConnect (v: SsKey) : unit =
            indices  <- Map.add v index indices
            lowlinks <- Map.add v index lowlinks
            index    <- index + 1
            stack.Add(v)
            onStack <- Set.add v onStack
            for w in childrenOf v do
                if not (Map.containsKey w indices) then
                    strongConnect w
                    let lw = Map.find w lowlinks
                    let lv = Map.find v lowlinks
                    lowlinks <- Map.add v (min lv lw) lowlinks
                elif Set.contains w onStack then
                    let iw = Map.find w indices
                    let lv = Map.find v lowlinks
                    lowlinks <- Map.add v (min lv iw) lowlinks
            if Map.find v lowlinks = Map.find v indices then
                let comp = ResizeArray<SsKey>()
                let mutable popping = true
                while popping do
                    let w = stack.[stack.Count - 1]
                    stack.RemoveAt(stack.Count - 1)
                    onStack <- Set.remove w onStack
                    comp.Add(w)
                    if w = v then popping <- false
                components.Add(comp |> List.ofSeq |> List.sort)

        for v in nodes do
            if not (Map.containsKey v indices) then
                strongConnect v

        // Filter to non-trivial SCCs (size >= 2). Single-node SCCs
        // without self-loops are not cycles. Self-loops would require
        // explicit detection — not present in the synthetic milestone;
        // adds when a real fixture surfaces them.
        components
        |> Seq.filter (fun c -> List.length c >= 2)
        |> Seq.toList
        |> List.sortBy (fun c -> c |> List.head)

    // -----------------------------------------------------------------------
    // The pass.
    // -----------------------------------------------------------------------

    let private touchedEvent (key: SsKey) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = key
          TransformKind = Touched }

    /// Run the topological-order pass. Returns a `Lineage<TopologicalOrder>`;
    /// the catalog itself is not modified. One `Touched` event per kind
    /// scanned (per A25).
    let run (c: Catalog) : Lineage<TopologicalOrder> =
        let graph = buildGraph c
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
                // residue to enumerate the strongly connected components;
                // each non-trivial SCC becomes a CycleDiagnostic. Cycle
                // resolution (asymmetric-2-cycle resolver) lands in the
                // next pass version.
                let sccs = tarjanScc unprocessed graph.Adjacency
                let cycles =
                    sccs
                    |> List.map (fun members ->
                        { Members        = members
                          BreakableEdges = []
                          Reason         = "SCC detected; resolver pending next pass version" })
                let alphabeticalAll = graph.Nodes  // already sorted
                { Mode         = Alphabetical
                  Order        = alphabeticalAll
                  Edges        = graph.Edges
                  MissingEdges = graph.MissingEdges
                  Cycles       = cycles
                  Diagnostics  = [
                      sprintf "topologicalOrder v%d: %d SCC(s) detected, %d nodes unprocessed; alphabetical fallback"
                          version cycles.Length unprocessed.Length
                  ] }

        let events = graph.Nodes |> List.map touchedEvent
        Lineage.tellMany events (Lineage.ofValue result)
