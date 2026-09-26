namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver lineage diagnostic prose. The PageRank centrality pass emits
//   human-readable convergence status via `sprintf` and uses function-local
//   mutables + a Dictionary for the iterative PageRank computation (performance-
//   sensitive numeric algorithm, per the style guide's Tarjan/ResizeArray
//   carve-out). Structural output is fully typed; only the diagnostic prose and
//   the local algorithm state fall under the allowed exceptions.

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
    // PageRank tuning — advisory (a default operator opinion, overridable),
    // single-sourced from `AdvisoryTuning.defaults.Centrality`. NOT [<Literal>]
    // on decimals: CLR cctor limitation (InvalidProgramException on decimal
    // Literal in F#). Per `DECISIONS 2026-05-09 — FP strict mode discipline`.
    let private dampingFactor   : decimal = AdvisoryTuning.defaults.Centrality.DampingFactor
    let private convergenceEps  : decimal = AdvisoryTuning.defaults.Centrality.ConvergenceEps
    let private maxIterations   : int     = AdvisoryTuning.defaults.Centrality.MaxIterations

    /// Run personalized PageRank on the FK graph encoded in
    /// `TopologicalOrder.Edges`. The FK graph is directed:
    /// an edge `(src, tgt)` means src has a FK pointing at tgt.
    /// In PageRank terms, a node with many incoming FK edges (many
    /// tables referencing it) is highly central.
    ///
    /// Convergence: max |Δrank| < ε across all nodes, or
    /// `maxIterations` iterations reached.
    // Slice 9 (2026-06-02 audit): the PageRank body decomposed into
    // four named helpers — `buildAdjacency`, `initialRank`,
    // `pageRankStep`, `runUntilConverged`, `sortScores`. Mutation
    // stays where it earned its place (the inner Dictionary build +
    // the iteration driver); the outer `run` reads as a pipeline.

    /// Build the (out-degree, reverse-adjacency) pair used by the
    /// PageRank update. Edge `(src, tgt)` in FK orientation: src cites
    /// tgt as a dependency; in PageRank, src passes rank to tgt
    /// through this citation edge, so the reversed adjacency is
    /// `tgt → [srcs that point at tgt]`. Dictionary allocations are
    /// function-local; the contract is pure (no observable state
    /// escapes).
    let private buildAdjacency (nodes: SsKey list) (edges: (SsKey * SsKey) list) =
        let outDegree = System.Collections.Generic.Dictionary<SsKey, int>()
        let revAdj = System.Collections.Generic.Dictionary<SsKey, SsKey list>()
        for node in nodes do
            if not (outDegree.ContainsKey node) then outDegree.[node] <- 0
            if not (revAdj.ContainsKey node) then revAdj.[node] <- []
        for (src, tgt) in edges do
            outDegree.[src] <- (if outDegree.ContainsKey src then outDegree.[src] else 0) + 1
            let existing = if revAdj.ContainsKey tgt then revAdj.[tgt] else []
            revAdj.[tgt] <- src :: existing
        outDegree, revAdj

    /// Initial uniform rank: 1/n for every node.
    let private initialRank (nodes: SsKey list) : Map<SsKey, decimal> =
        let nDecimal = decimal (List.length nodes)
        nodes |> List.map (fun k -> k, 1.0m / nDecimal) |> Map.ofList

    /// One PageRank iteration. Returns the new rank vector and the
    /// L∞ (max-delta) distance from the previous rank — the
    /// convergence-test input. Dangling mass redistribution is the
    /// standard PageRank formulation: rank held by nodes with no
    /// out-links would otherwise leak between iterations.
    /// PL-5 (S42) — the graph-CONSTANT facts (node count; the dangling
    /// node set) are hoisted parameters computed once before the fixpoint
    /// loop; only the rank-dependent dangling MASS re-derives per
    /// iteration (it must — it reads the evolving rank vector).
    let private pageRankStepWith (nodes: SsKey list) (nDecimal: decimal) (danglingNodes: SsKey list) (outDegree: System.Collections.Generic.Dictionary<SsKey, int>) (revAdj: System.Collections.Generic.Dictionary<SsKey, SsKey list>) (rank: Map<SsKey, decimal>) =
        let danglingMass =
            danglingNodes
            |> List.sumBy (fun u -> Map.tryFind u rank |> Option.defaultValue 0.0m)
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
        newRank, maxDelta

    /// Drive `pageRankStep` to convergence (max-delta < `convergenceEps`)
    /// or `maxIterations`, whichever fires first. Returns the converged
    /// rank vector + the number of iterations actually run.
    let private runUntilConverged (nodes: SsKey list) (outDegree: System.Collections.Generic.Dictionary<SsKey, int>) (revAdj: System.Collections.Generic.Dictionary<SsKey, SsKey list>) (initial: Map<SsKey, decimal>) =
        // recon #19 — the bounded fixed-point scheme, named once in `Fixpoint`.
        // The step drives `pageRankStepWith` and reports convergence when the
        // max-delta falls below `convergenceEps`. PL-5 (S42): node count and
        // the dangling-node set are graph constants — computed HERE, once,
        // not per iteration.
        let nDecimal = decimal (List.length nodes)
        let danglingNodes =
            nodes
            |> List.filter (fun u ->
                (if outDegree.ContainsKey u then outDegree.[u] else 0) = 0)
        initial
        |> Fixpoint.iterate maxIterations (fun rank ->
            let newRank, maxDelta = pageRankStepWith nodes nDecimal danglingNodes outDegree revAdj rank
            newRank, maxDelta < convergenceEps)

    /// Sort the converged rank vector into a deterministic score list:
    /// DESC by score; ASC by SsKey for ties.
    let private sortScores (rank: Map<SsKey, decimal>) : CentralityScore list =
        rank
        |> Map.toList
        |> List.map (fun (k, s) -> { SsKey = k; Score = s })
        |> List.sortWith (fun a b ->
            let cmp = compare b.Score a.Score
            if cmp <> 0 then cmp else compare a.SsKey b.SsKey)

    let run (t: TopologicalOrder) : Lineage<Diagnostics<CentralityRanking>> =
        use _ = Bench.scope "pass.centrality"
        let nodes = t.Order
        let n = List.length nodes
        if n = 0 then
            Lineage.ofValue (Diagnostics.ofValue { Scores = []; Iterations = 0 })
        else
            let outDegree, revAdj = buildAdjacency nodes t.Edges
            let initial = initialRank nodes
            let converged, iterations = runUntilConverged nodes outDegree revAdj initial
            let scores = sortScores converged
            let result = { Scores = scores; Iterations = iterations }

            let entry =
                DiagnosticEntry.create passName DiagnosticSeverity.Info
                    "centrality.computed"
                    (sprintf "centrality v%d: PageRank over %d nodes, %d edges; converged in %d iterations"
                        version n (List.length t.Edges) iterations)

            LineageDiagnostics.touchedEpilogue passName version nodes [ entry ] result

    /// Registered transform metadata. Input type is `TopologicalOrder`.
    let registered : RegisteredTransform<TopologicalOrder, CentralityRanking> =
        { Name         = passName
          Domain       = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName       = "pageRankCentrality"
                Classification = DataIntent
                Rationale      = "Personalized PageRank over the FK adjacency graph. Advisory only — the rank is determined by graph topology, but the PageRank tuning (damping, convergence ε, max iterations) is a DEFAULT OPERATOR OPINION (overridable, named in `AdvisoryTuning.defaults.Centrality`); the ranking never enters the faithful projection." } ]
          Run    = run
          Status = Active }
