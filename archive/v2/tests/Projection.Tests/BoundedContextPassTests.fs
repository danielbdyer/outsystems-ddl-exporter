module Projection.Tests.BoundedContextPassTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// H-072 — Bounded context community detection
// ---------------------------------------------------------------------------

let private k key = SsKey.synthesized "M" key |> Result.value

[<Fact>]
let ``empty topology produces empty discovery`` () =
    let result =
        BoundedContextPass.run TopologicalOrder.empty
        |> LineageDiagnostics.payload
    Assert.Empty(result.Candidates)

[<Fact>]
let ``single-node topology produces one candidate`` () =
    let t = { TopologicalOrder.empty with Order = [k "A"]; Edges = [] }
    let result =
        BoundedContextPass.run t
        |> LineageDiagnostics.payload
    Assert.Equal(1, result.Candidates.Length)

[<Fact>]
let ``all kinds appear in exactly one community`` () =
    let keys = ["A";"B";"C";"D";"E"] |> List.map k
    let edges = [(k "A", k "B"); (k "B", k "C"); (k "D", k "E")]
    let t = { TopologicalOrder.empty with Order = keys; Edges = edges }
    let result =
        BoundedContextPass.run t
        |> LineageDiagnostics.payload
    let allMembers =
        result.Candidates |> List.collect (fun c -> c.Members) |> List.sort
    Assert.Equal<SsKey list>(List.sort keys, allMembers)

[<Fact>]
let ``internal edge counts are non-negative`` () =
    let keys = ["A";"B";"C"] |> List.map k
    let edges = [(k "A", k "B"); (k "B", k "C")]
    let t = { TopologicalOrder.empty with Order = keys; Edges = edges }
    let result =
        BoundedContextPass.run t
        |> LineageDiagnostics.payload
    for c in result.Candidates do
        Assert.True(c.InternalEdgeCount >= 0)
        Assert.True(c.ExternalEdgeCount >= 0)

[<Fact>]
let ``members within each candidate are sorted`` () =
    let keys = ["C";"A";"B"] |> List.map k
    let edges = [(k "A", k "B"); (k "B", k "C")]
    let t = { TopologicalOrder.empty with Order = keys; Edges = edges }
    let result =
        BoundedContextPass.run t
        |> LineageDiagnostics.payload
    for c in result.Candidates do
        Assert.Equal<SsKey list>(List.sort c.Members, c.Members)
