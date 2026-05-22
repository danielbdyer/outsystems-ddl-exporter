namespace Projection.Core.Passes

open Projection.Core

/// H-071 — Schema centrality metrics. Computes personalized PageRank
/// over the FK graph derived from a `TopologicalOrder`. The result
/// ranks kinds by structural centrality — highly central kinds are
/// FK targets for many other kinds and are operationally load-bearing.
///
/// Input: `TopologicalOrder` (carries the FK edge set + kind list
/// without requiring a full Catalog re-scan).
/// Output: `CentralityRanking` — per-kind decimal PageRank scores
/// sorted Score DESC, SsKey ASC for ties.
[<RequireQualifiedAccess>]
module CentralityPass =

    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "centrality"

    // PageRank constants — decimal per T1 byte-determinism discipline.
    // NOT [<Literal>] on decimals: CLR cctor limitation
    // (InvalidProgramException on decimal Literal in F#). Per
    // `DECISIONS 2026-05-09 — FP strict mode discipline`.
    let private dampingFactor   : decimal = 0.85m
    let private convergenceEps  : decimal = 0.000001m
    let private maxIterations   : int     = 100

    /// Run personalized PageRank on the FK graph encoded in
    /// `TopologicalOrder.Edges`. The FK graph is directed:
    /// an edge `(src, tgt)` means src has a FK pointing at tgt.
    /// In PageRank terms, a node with many incoming FK edges (many
    /// tables referencing it) is highly central.
    ///
    /// Convergence: max |Δrank| < ε across all nodes, or
    /// `maxIterations` iterations reached.
    let run (t: TopologicalOrder) : Lineage<Diagnostics<CentralityRanking>> =
        use _ = Bench.scope "pass.centrality"

        let nodes = t.Order
        let n = List.length nodes
        if n = 0 then
            let result = { Scores = []; Iterations = 0 }
            Lineage.ofValue (Diagnostics.ofValue result)
        else
            let nDecimal = decimal n

            // Build out-degree map and reversed adjacency (tgt → sources
            // that point at tgt) for the PageRank update.
            // Edge (src, tgt) in FK orientation: src cites tgt as a
            // dependency. In PageRank: src passes rank to tgt through
            // this citation edge. So the reversed adjacency is
            // tgt → [srcs that point at tgt].
            let outDegree : System.Collections.Generic.Dictionary<SsKey, int> =
                System.Collections.Generic.Dictionary()
            let revAdj : System.Collections.Generic.Dictionary<SsKey, SsKey list> =
                System.Collections.Generic.Dictionary()
            for node in nodes do
                if not (outDegree.ContainsKey node) then outDegree.[node] <- 0
                if not (revAdj.ContainsKey node) then revAdj.[node] <- []
            for (src, tgt) in t.Edges do
                outDegree.[src] <- (if outDegree.ContainsKey src then outDegree.[src] else 0) + 1
                let existing = if revAdj.ContainsKey tgt then revAdj.[tgt] else []
                revAdj.[tgt] <- src :: existing

            // Initialize rank vector uniformly.
            let mutable rank : Map<SsKey, decimal> =
                nodes |> List.map (fun k -> k, 1.0m / nDecimal) |> Map.ofList

            let mutable iterations = 0
            let mutable converged = false

            while not converged && iterations < maxIterations do
                // Dangling-mass: rank held by nodes with no out-links
                // would otherwise leak between iterations. Per the
                // standard PageRank formulation, redistribute uniformly.
                let danglingMass =
                    nodes
                    |> List.sumBy (fun u ->
                        let du = if outDegree.ContainsKey u then outDegree.[u] else 0
                        if du = 0 then
                            Map.tryFind u rank |> Option.defaultValue 0.0m
                        else 0.0m)
                let newRank =
                    nodes
                    |> List.map (fun v ->
                        let incomingSum =
                            (if revAdj.ContainsKey v then revAdj.[v] else [])
                            |> List.sumBy (fun u ->
                                let ru = Map.tryFind u rank |> Option.defaultValue 0.0m
                                let du = if outDegree.ContainsKey u then outDegree.[u] else 1
                                ru / decimal (max 1 du))
                        let score =
                            (1.0m - dampingFactor) / nDecimal
                            + dampingFactor * (incomingSum + danglingMass / nDecimal)
                        v, score)
                    |> Map.ofList
                let maxDelta =
                    nodes
                    |> List.map (fun k ->
                        let oldV = Map.tryFind k rank |> Option.defaultValue 0.0m
                        let newV = Map.tryFind k newRank |> Option.defaultValue 0.0m
                        abs (newV - oldV))
                    |> List.max
                rank <- newRank
                iterations <- iterations + 1
                if maxDelta < convergenceEps then converged <- true

            let scores =
                rank
                |> Map.toList
                |> List.map (fun (k, s) -> { SsKey = k; Score = s })
                |> List.sortWith (fun a b ->
                    let cmp = compare b.Score a.Score   // DESC by score
                    if cmp <> 0 then cmp else compare a.SsKey b.SsKey)  // ASC by key for ties

            let result = { Scores = scores; Iterations = iterations }

            let entry =
                DiagnosticEntry.create passName DiagnosticSeverity.Info
                    "centrality.computed"
                    (sprintf "centrality v%d: PageRank over %d nodes, %d edges; converged in %d iterations"
                        version n (List.length t.Edges) iterations)

            let events =
                nodes
                |> List.map (fun key ->
                    { PassName       = passName
                      PassVersion    = version
                      SsKey          = key
                      TransformKind  = Touched
                      Classification = DataIntent })

            Lineage.ofValueAndEvents events { Value = result; Entries = [entry] }

    /// Registered transform metadata. Input type is `TopologicalOrder`.
    let registered : RegisteredTransform<TopologicalOrder, CentralityRanking> =
        { Name         = passName
          Domain       = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName       = "pageRankCentrality"
                Classification = DataIntent
                Rationale      = "Personalized PageRank over the FK adjacency graph. Rank is determined by graph topology alone; no operator opinion in the computation." } ]
          Run    = run
          Status = Active }
