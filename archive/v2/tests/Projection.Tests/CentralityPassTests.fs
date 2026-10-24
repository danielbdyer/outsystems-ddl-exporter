module Projection.Tests.CentralityPassTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// H-071 — Schema centrality metrics (PageRank)
// ---------------------------------------------------------------------------

let private kA = SsKey.synthesized "M" "A" |> Result.value
let private kB = SsKey.synthesized "M" "B" |> Result.value
let private kC = SsKey.synthesized "M" "C" |> Result.value

[<Fact>]
let ``empty topology produces empty ranking`` () =
    let result =
        CentralityPass.run TopologicalOrder.empty
        |> LineageDiagnostics.payload
    Assert.Empty(result.Scores)
    Assert.Equal(0, result.Iterations)

[<Fact>]
let ``single-node topology produces one score`` () =
    let t = { TopologicalOrder.empty with Order = [kA]; Edges = [] }
    let result =
        CentralityPass.run t
        |> LineageDiagnostics.payload
    Assert.Equal(1, result.Scores.Length)
    Assert.Equal(kA, result.Scores.[0].SsKey)

[<Fact>]
let ``central hub has higher score than leaf`` () =
    // A → C and B → C: C is the hub (two inbound FK edges).
    let t =
        { TopologicalOrder.empty with
            Order = [kA; kB; kC]
            Edges = [(kA, kC); (kB, kC)] }
    let result =
        CentralityPass.run t
        |> LineageDiagnostics.payload
    let scoreOf key =
        result.Scores
        |> List.find (fun s -> s.SsKey = key)
        |> fun s -> s.Score
    // C has two inbound edges; A and B have none → C should rank highest.
    Assert.True(scoreOf kC > scoreOf kA, "C should have higher centrality than A")
    Assert.True(scoreOf kC > scoreOf kB, "C should have higher centrality than B")

[<Fact>]
let ``ranking is sorted score DESC`` () =
    let t =
        { TopologicalOrder.empty with
            Order = [kA; kB; kC]
            Edges = [(kA, kC); (kB, kC)] }
    let result =
        CentralityPass.run t
        |> LineageDiagnostics.payload
    let scores = result.Scores |> List.map (fun s -> s.Score)
    let sorted = scores |> List.sortDescending
    Assert.True((scores = sorted), "Scores should be in descending order")

[<Fact>]
let ``all scores are positive`` () =
    let t =
        { TopologicalOrder.empty with
            Order = [kA; kB; kC]
            Edges = [(kA, kB); (kB, kC)] }
    let result =
        CentralityPass.run t
        |> LineageDiagnostics.payload
    for s in result.Scores do
        Assert.True(s.Score > 0.0m, sprintf "Score for %A should be positive" s.SsKey)

[<Fact>]
let ``scores sum approximately to 1`` () =
    let t =
        { TopologicalOrder.empty with
            Order = [kA; kB; kC]
            Edges = [(kA, kB); (kB, kC)] }
    let result =
        CentralityPass.run t
        |> LineageDiagnostics.payload
    let total = result.Scores |> List.sumBy (fun s -> s.Score)
    // PageRank scores should sum close to 1 (within convergence tolerance).
    Assert.True(abs (total - 1.0m) < 0.01m,
        sprintf "Scores should sum ≈ 1.0, got %M" total)
