module Projection.Tests.IslandDetectionTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// H-037 — Schema island detection
// ---------------------------------------------------------------------------

let private keyA = SsKey.synthesized "A" "A" |> Result.value
let private keyB = SsKey.synthesized "A" "B" |> Result.value
let private keyC = SsKey.synthesized "B" "C" |> Result.value
let private keyD = SsKey.synthesized "B" "D" |> Result.value

[<Fact>]
let ``empty catalog reports no islands`` () =
    let t = TopologicalOrder.empty
    let result =
        TopologicalOrderPass.runIslandDetection [] t
        |> LineageDiagnostics.payload
    Assert.Empty(result.Islands)

[<Fact>]
let ``single connected catalog reports no islands`` () =
    // A → B, B → C, C → D — one connected component.
    let t =
        { TopologicalOrder.empty with
            Order = [keyA; keyB; keyC; keyD]
            Edges = [(keyA, keyB); (keyB, keyC); (keyC, keyD)] }
    let allKeys = [keyA; keyB; keyC; keyD]
    let result =
        TopologicalOrderPass.runIslandDetection allKeys t
        |> LineageDiagnostics.payload
    Assert.Empty(result.Islands)

[<Fact>]
let ``two disconnected pairs are detected as two islands`` () =
    // A → B and C → D — two separate components, each size 2.
    let t =
        { TopologicalOrder.empty with
            Order = [keyA; keyB; keyC; keyD]
            Edges = [(keyA, keyB); (keyC, keyD)] }
    let allKeys = [keyA; keyB; keyC; keyD]
    let result =
        TopologicalOrderPass.runIslandDetection allKeys t
        |> LineageDiagnostics.payload
    Assert.Equal(2, result.Islands.Length)

[<Fact>]
let ``island members are sorted by SsKey`` () =
    let t =
        { TopologicalOrder.empty with
            Order = [keyA; keyB; keyC; keyD]
            Edges = [(keyA, keyB); (keyC, keyD)] }
    let allKeys = [keyA; keyB; keyC; keyD]
    let result =
        TopologicalOrderPass.runIslandDetection allKeys t
        |> LineageDiagnostics.payload
    for island in result.Islands do
        Assert.Equal<SsKey list>(List.sort island, island)

[<Fact>]
let ``one Warning diagnostic emitted per island`` () =
    let t =
        { TopologicalOrder.empty with
            Order = [keyA; keyB; keyC; keyD]
            Edges = [(keyA, keyB); (keyC, keyD)] }
    let allKeys = [keyA; keyB; keyC; keyD]
    let diagnostics_ =
        TopologicalOrderPass.runIslandDetection allKeys t
        |> LineageDiagnostics.entries
    Assert.Equal(2, diagnostics_.Length)
    for d in diagnostics_ do
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
        Assert.Equal("topology.island", d.Code)

[<Fact>]
let ``single-node components are not reported as islands`` () =
    // A → B (one edge), plus solo C and D (no edges).
    let t =
        { TopologicalOrder.empty with
            Order = [keyA; keyB; keyC; keyD]
            Edges = [(keyA, keyB)] }
    let allKeys = [keyA; keyB; keyC; keyD]
    let result =
        TopologicalOrderPass.runIslandDetection allKeys t
        |> LineageDiagnostics.payload
    // The A-B component has 2 members → one island reported.
    // C and D are singles → not reported.
    Assert.Equal(1, result.Islands.Length)
