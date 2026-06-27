namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver lineage diagnostic prose. The bounded-context pass emits
//   human-readable status strings (community-candidate counts) via `sprintf`
//   and uses function-local mutables for the community-detection algorithm
//   (performance-sensitive graph traversal, per the style guide's carve-out).
//   Structural pass output is fully typed; only the diagnostic-string surface
//   uses sprintf, per `DECISIONS 2026-05-09 — Built-in obligation`.

open Projection.Core

/// H-072 — Subgraph extraction for bounded context discovery. Groups
/// the FK graph into bounded context candidates using a label-
/// propagation community detection approach with deterministic
/// tie-breaking by SsKey. Communities with high internal edge density
/// relative to external edges are candidate bounded context boundaries.
///
/// Input: `TopologicalOrder` (carries the FK edge set + kind ordering).
/// Output: `BoundedContextDiscovery` — candidate context boundaries
/// sorted by anchor SsKey.
[<RequireQualifiedAccess>]
module BoundedContextPass =

    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "boundedContext"

    // Label propagation convergence limit — NOT [<Literal>] (not a
    // CLR primitive annotation context; just a local constant).
    let private maxPropagationRounds : int = 50

    // The undirected FK adjacency (community detection treats FK relationships as
    // undirected structural coupling) is the shared canonical
    // `TopologicalOrder.undirectedAdjacency` — see its docstring for the divergence
    // it retired.

    /// Label propagation: each node adopts the most frequent label
    /// among its neighbors. Ties broken by choosing the smallest
    // Slice 9 (2026-06-02 audit): label-propagation body decomposed
    // into `initialLabels`, `pickLabel`, `propagateOnce` named helpers;
    // `labelPropagation` becomes the iteration driver.

    /// Initial labels: each node carries its own SsKey as a label.
    let private initialLabels (nodes: SsKey list) : Map<SsKey, SsKey> =
        nodes |> List.map (fun k -> k, k) |> Map.ofList

    /// Pick the most-frequent label among a node's neighbors;
    /// tie-break on label ASC (SsKey rootOriginal). The neighbors
    /// projection ignores entries missing from the label map (treated
    /// as "no contribution"). Falls back to the node's own current
    /// label when it has no neighbors.
    let private pickLabel
        (node: SsKey)
        (neighbors: SsKey list)
        (labels: Map<SsKey, SsKey>)
        : SsKey =
        if List.isEmpty neighbors then
            Map.tryFind node labels |> Option.defaultValue node
        else
            let freqs =
                neighbors
                |> List.choose (fun n -> Map.tryFind n labels)
                |> List.countBy id
            freqs
            // Primary: count DESC; secondary: label ASC (ordinal). The
            // sort key sorts ascending, so negate the count (highest count
            // first) and use the label text directly (smallest label wins
            // the tie). `List.head` is then the deterministic winner.
            |> List.sortBy (fun (lbl, cnt) -> -cnt, SsKey.rootOriginal lbl)
            |> List.head
            |> fst

    /// One propagation round. Returns the new label map and a flag
    /// indicating whether any node's label changed.
    let private propagateOnce
        (nodes: SsKey list)
        (adj: Map<SsKey, SsKey list>)
        (labels: Map<SsKey, SsKey>)
        : Map<SsKey, SsKey> * bool =
        let newLabels =
            nodes
            |> List.map (fun node ->
                let neighbors = Map.tryFind node adj |> Option.defaultValue []
                node, pickLabel node neighbors labels)
            |> Map.ofList
        let changed =
            nodes
            |> List.exists (fun node ->
                let old = Map.tryFind node labels |> Option.defaultValue node
                let nw  = Map.tryFind node newLabels |> Option.defaultValue node
                old <> nw)
        newLabels, changed

    /// Drive `propagateOnce` until labels stabilize or
    /// `maxPropagationRounds` fires, whichever comes first. Nodes
    /// with no neighbors retain their initial labels by construction.
    let private labelPropagation
        (nodes: SsKey list)
        (adj: Map<SsKey, SsKey list>)
        : Map<SsKey, SsKey> =
        let mutable labels = initialLabels nodes
        let mutable changed = true
        let mutable rounds = 0
        while changed && rounds < maxPropagationRounds do
            rounds <- rounds + 1
            let newLabels, didChange = propagateOnce nodes adj labels
            labels <- newLabels
            changed <- didChange
        labels

    let run (t: TopologicalOrder) : Lineage<Diagnostics<BoundedContextDiscovery>> =
        use _ = Bench.scope "pass.boundedContext"

        let nodes = t.Order
        let adj = TopologicalOrder.undirectedAdjacency t

        let labels = labelPropagation nodes adj

        // Group nodes by their final label.
        let groups =
            nodes
            |> List.groupBy (fun node ->
                Map.tryFind node labels |> Option.defaultValue node)
            |> List.sortBy fst

        let candidates =
            groups
            |> List.map (fun (_, members) ->
                let memberSet = Set.ofList members
                let internalCount =
                    t.Edges
                    |> List.filter (fun (src, tgt) ->
                        Set.contains src memberSet && Set.contains tgt memberSet)
                    |> List.length
                let externalCount =
                    t.Edges
                    |> List.filter (fun (src, tgt) ->
                        (Set.contains src memberSet) <> (Set.contains tgt memberSet))
                    |> List.length
                // Anchor: member with the highest total degree; SsKey ASC for ties.
                let degreeOf (m: SsKey) =
                    t.Edges
                    |> List.filter (fun (src, tgt) -> src = m || tgt = m)
                    |> List.length
                let anchorKey =
                    members
                    |> List.sortWith (fun a b ->
                        let cmp = compare (degreeOf b) (degreeOf a)  // higher degree first
                        if cmp <> 0 then cmp else compare a b)       // SsKey ASC for ties
                    |> List.head
                { AnchorKey         = anchorKey
                  Members           = List.sort members
                  InternalEdgeCount = internalCount
                  ExternalEdgeCount = externalCount })
            |> List.sortBy (fun c -> c.AnchorKey)

        let result = { Candidates = candidates }

        let entry =
            DiagnosticEntry.create passName DiagnosticSeverity.Info
                "boundedContext.computed"
                (sprintf "boundedContext v%d: %d community candidates from %d nodes"
                    version (List.length candidates) (List.length nodes))

        LineageDiagnostics.touchedEpilogue passName version nodes [ entry ] result

    let registered : RegisteredTransform<TopologicalOrder, BoundedContextDiscovery> =
        { Name         = passName
          Domain       = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName       = "labelPropagation"
                Classification = DataIntent
                Rationale      = "Label-propagation community detection over the undirected FK coupling graph. Community membership is graph-topology-derived; no operator opinion in the partition." } ]
          Run    = run
          Status = Active }
