namespace Projection.Core.Passes

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

    /// Build the undirected FK adjacency from the edge set (for
    /// community detection we treat FK relationships as undirected
    /// structural coupling).
    let private addNeighbor (m: Map<SsKey, SsKey list>) (a: SsKey) (b: SsKey) =
        let existing = Map.tryFind a m |> Option.defaultValue []
        if List.contains b existing then m
        else Map.add a (b :: existing) m

    let private buildUndirectedAdj (edges: (SsKey * SsKey) list) : Map<SsKey, SsKey list> =
        edges
        |> List.fold (fun acc (src, tgt) ->
            if src = tgt then acc
            else addNeighbor (addNeighbor acc src tgt) tgt src)
            Map.empty
        |> Map.map (fun _ neighbors -> List.sort neighbors)

    /// Label propagation: each node adopts the most frequent label
    /// among its neighbors. Ties broken by choosing the smallest
    /// label (SsKey comparison). Nodes with no neighbors keep their
    /// own label.
    let private labelPropagation
        (nodes: SsKey list)
        (adj: Map<SsKey, SsKey list>)
        : Map<SsKey, SsKey> =
        // Initialize: each node is its own label.
        let mutable labels : Map<SsKey, SsKey> =
            nodes |> List.map (fun k -> k, k) |> Map.ofList

        let mutable changed = true
        let mutable rounds = 0

        while changed && rounds < maxPropagationRounds do
            changed <- false
            rounds <- rounds + 1
            // Process nodes in sorted order for determinism.
            let newLabels =
                nodes
                |> List.map (fun node ->
                    let neighbors = Map.tryFind node adj |> Option.defaultValue []
                    if List.isEmpty neighbors then
                        node, Map.tryFind node labels |> Option.defaultValue node
                    else
                        // Count label frequencies among neighbors.
                        let freqs =
                            neighbors
                            |> List.choose (fun n -> Map.tryFind n labels)
                            |> List.countBy id
                        // Pick the label with maximum frequency; tie-break smallest.
                        let best =
                            freqs
                            |> List.maxBy (fun (lbl, cnt) ->
                                // Primary: count DESC; secondary: label ASC (negate string comparison)
                                cnt, -System.String.CompareOrdinal(SsKey.rootOriginal lbl, SsKey.rootOriginal lbl))
                            |> fst
                        node, best)
                |> Map.ofList
            for node in nodes do
                let old = Map.tryFind node labels |> Option.defaultValue node
                let nw  = Map.tryFind node newLabels |> Option.defaultValue node
                if old <> nw then changed <- true
            labels <- newLabels

        labels

    let run (t: TopologicalOrder) : Lineage<Diagnostics<BoundedContextDiscovery>> =
        use _ = Bench.scope "pass.boundedContext"

        let nodes = t.Order
        let adj = buildUndirectedAdj t.Edges

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

        let events =
            nodes
            |> List.map (fun key ->
                { PassName       = passName
                  PassVersion    = version
                  SsKey          = key
                  TransformKind  = Touched
                  Classification = DataIntent })

        lineageDiagnostics {
            do! LineageDiagnostics.writeLineages events
            do! LineageDiagnostics.writeDiagnostic entry
            return result
        }

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
