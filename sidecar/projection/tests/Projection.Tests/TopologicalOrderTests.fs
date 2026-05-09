module Projection.Tests.TopologicalOrderTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Empty value
// ---------------------------------------------------------------------------

[<Fact>]
let ``empty is empty across all collections`` () =
    let t = TopologicalOrder.empty
    Assert.Equal(Topological, t.Mode)
    Assert.Empty(t.Order)
    Assert.Empty(t.Edges)
    Assert.Empty(t.MissingEdges)
    Assert.Empty(t.Cycles)
    Assert.Empty(t.Diagnostics)

[<Fact>]
let ``empty is acyclic and complete`` () =
    Assert.True(TopologicalOrder.isAcyclic TopologicalOrder.empty)
    Assert.True(TopologicalOrder.isComplete TopologicalOrder.empty)

// ---------------------------------------------------------------------------
// isAcyclic reflects Mode
// ---------------------------------------------------------------------------

[<Fact>]
let ``isAcyclic is true only for Mode = Topological`` () =
    let topo = { TopologicalOrder.empty with Mode = Topological }
    let alpha = { TopologicalOrder.empty with Mode = Alphabetical }
    let junc  = { TopologicalOrder.empty with Mode = JunctionDeferred }
    Assert.True (TopologicalOrder.isAcyclic topo)
    Assert.False(TopologicalOrder.isAcyclic alpha)
    Assert.False(TopologicalOrder.isAcyclic junc)

// ---------------------------------------------------------------------------
// containsKind / positionOf
// ---------------------------------------------------------------------------

let private threeKindOrder : TopologicalOrder =
    { TopologicalOrder.empty with Order = [ customerKey; orderKey; countryKey ] }

[<Fact>]
let ``containsKind returns true for present keys, false for absent`` () =
    Assert.True (TopologicalOrder.containsKind customerKey threeKindOrder)
    Assert.True (TopologicalOrder.containsKind orderKey    threeKindOrder)
    Assert.True (TopologicalOrder.containsKind countryKey  threeKindOrder)
    let unknown = kindKey ["Unknown"]
    Assert.False(TopologicalOrder.containsKind unknown threeKindOrder)

[<Fact>]
let ``positionOf returns 0-based index for present keys`` () =
    Assert.Equal(Some 0, TopologicalOrder.positionOf customerKey threeKindOrder)
    Assert.Equal(Some 1, TopologicalOrder.positionOf orderKey    threeKindOrder)
    Assert.Equal(Some 2, TopologicalOrder.positionOf countryKey  threeKindOrder)

[<Fact>]
let ``positionOf returns None for absent keys`` () =
    let unknown = kindKey ["Unknown"]
    Assert.Equal(None, TopologicalOrder.positionOf unknown threeKindOrder)

// ---------------------------------------------------------------------------
// precedes — the predicate emitters most often want.
// ---------------------------------------------------------------------------

[<Fact>]
let ``precedes is true when parent index < child index`` () =
    Assert.True (TopologicalOrder.precedes customerKey orderKey   threeKindOrder)
    Assert.True (TopologicalOrder.precedes customerKey countryKey threeKindOrder)
    Assert.True (TopologicalOrder.precedes orderKey    countryKey threeKindOrder)

[<Fact>]
let ``precedes is false when parent index >= child index`` () =
    Assert.False(TopologicalOrder.precedes orderKey    customerKey threeKindOrder)
    Assert.False(TopologicalOrder.precedes countryKey  orderKey    threeKindOrder)
    Assert.False(TopologicalOrder.precedes customerKey customerKey threeKindOrder)

[<Fact>]
let ``precedes is false when either kind is absent`` () =
    let unknown = kindKey ["Unknown"]
    Assert.False(TopologicalOrder.precedes unknown    customerKey threeKindOrder)
    Assert.False(TopologicalOrder.precedes customerKey unknown    threeKindOrder)

// ---------------------------------------------------------------------------
// isComplete — true iff no missing edges
// ---------------------------------------------------------------------------

[<Fact>]
let ``isComplete is true when MissingEdges is empty`` () =
    Assert.True(TopologicalOrder.isComplete TopologicalOrder.empty)

[<Fact>]
let ``isComplete is false when MissingEdges has any entry`` () =
    let t =
        { TopologicalOrder.empty with
            MissingEdges = [ orderKey, customerKey ] }
    Assert.False(TopologicalOrder.isComplete t)

// ---------------------------------------------------------------------------
// CycleDiagnostic + EdgeStrength + OrderingMode are values that round-trip
// through structural equality. (Sanity that DUs / records compare correctly
// for downstream emitters that build expected-vs-actual fixtures.)
// ---------------------------------------------------------------------------

[<Fact>]
let ``OrderingMode values round-trip through structural equality`` () =
    Assert.Equal<OrderingMode>(Topological,      Topological)
    Assert.Equal<OrderingMode>(Alphabetical,     Alphabetical)
    Assert.Equal<OrderingMode>(JunctionDeferred, JunctionDeferred)
    Assert.NotEqual<OrderingMode>(Topological, Alphabetical)

[<Fact>]
let ``EdgeStrength values round-trip through structural equality`` () =
    Assert.Equal<EdgeStrength>(EdgeStrength.Weak,    EdgeStrength.Weak)
    Assert.Equal<EdgeStrength>(EdgeStrength.Cascade, EdgeStrength.Cascade)
    Assert.Equal<EdgeStrength>(EdgeStrength.Other,   EdgeStrength.Other)
    Assert.NotEqual<EdgeStrength>(EdgeStrength.Weak, EdgeStrength.Cascade)

[<Fact>]
let ``CycleDiagnostic round-trips through structural equality`` () =
    let a =
        { Members        = [ customerKey; orderKey ]
          BreakableEdges = [ customerKey, orderKey ]
          Reason         = "test cycle" }
    let b =
        { Members        = [ customerKey; orderKey ]
          BreakableEdges = [ customerKey, orderKey ]
          Reason         = "test cycle" }
    Assert.Equal(a, b)

[<Fact>]
let ``TopologicalOrder round-trips through structural equality`` () =
    let make () : TopologicalOrder =
        { Mode         = Topological
          Order        = [ customerKey; orderKey ]
          Edges        = [ orderKey, customerKey ]
          MissingEdges = []
          Cycles       = []
          Diagnostics  = [ "ran" ] }
    Assert.Equal(make (), make ())

// ---------------------------------------------------------------------------
// A property: precedes is asymmetric on distinct keys (anti-symmetric).
// If precedes a b, then NOT precedes b a.
// ---------------------------------------------------------------------------

[<Fact>]
let ``precedes is anti-symmetric on the synthetic ordering`` () =
    for a in [ customerKey; orderKey; countryKey ] do
        for b in [ customerKey; orderKey; countryKey ] do
            if a <> b then
                let ab = TopologicalOrder.precedes a b threeKindOrder
                let ba = TopologicalOrder.precedes b a threeKindOrder
                Assert.False(ab && ba)
