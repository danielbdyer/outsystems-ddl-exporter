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

// Slice 10 (2026-06-02 audit): four "round-trip through structural
// equality" tests pruned. They asserted `Assert.Equal(x, x)` on
// F#-auto-derived structural equality for `OrderingMode`,
// `EdgeStrength`, `CycleDiagnostic`, `TopologicalOrder` — they tested
// the F# compiler, not a contract V2 owns. Behavior preservation by
// closed-DU + record structural equality is a language guarantee;
// V2's own structural-commitment axioms (A4, A39) carry the
// invariants the suite needs.

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

// ---------------------------------------------------------------------------
// levels — Kahn-style level assignment for parallel-safe emission groups.
// ---------------------------------------------------------------------------

[<Fact>]
let ``levels: empty order produces empty level list`` () =
    Assert.Empty(TopologicalOrder.levels TopologicalOrder.empty)

[<Fact>]
let ``levels: solitary kind with no edges lives at level 0`` () =
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Order = [ customerKey ]
            Edges = [] }
    Assert.Equal<SsKey list list>(
        [ [ customerKey ] ],
        TopologicalOrder.levels t |> List.map ParallelSafe.members)

[<Fact>]
let ``levels: chain customer<-order<-country produces three singleton levels`` () =
    // Edges are (child, parent) — country depends on order; order depends on customer.
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Order = [ customerKey; orderKey; countryKey ]
            Edges = [ orderKey, customerKey
                      countryKey, orderKey ] }
    Assert.Equal<SsKey list list>(
        [ [ customerKey ]; [ orderKey ]; [ countryKey ] ],
        TopologicalOrder.levels t |> List.map ParallelSafe.members)

[<Fact>]
let ``levels: two independent kinds collapse into one level-0 group`` () =
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Order = [ customerKey; countryKey ]
            Edges = [] }
    // Both at level 0; inner list sorted by SsKey.
    let result = TopologicalOrder.levels t |> List.map ParallelSafe.members
    Assert.Single(result) |> ignore
    let level0 = List.head result
    Assert.Equal(2, List.length level0)
    Assert.Contains(customerKey, level0)
    Assert.Contains(countryKey, level0)

[<Fact>]
let ``levels: diamond shape — two parents at level 0; shared dependent at level 1`` () =
    // customer <- order; country <- order. order depends on both.
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Order = [ customerKey; countryKey; orderKey ]
            Edges = [ orderKey, customerKey
                      orderKey, countryKey ] }
    let result = TopologicalOrder.levels t |> List.map ParallelSafe.members
    Assert.Equal(2, List.length result)
    let level0 = List.head result
    let level1 = List.item 1 result
    Assert.Equal(2, List.length level0)
    Assert.Contains(customerKey, level0)
    Assert.Contains(countryKey,  level0)
    Assert.Equal<SsKey list>([ orderKey ], level1)

[<Fact>]
let ``levels: parallel-safety invariant holds — no edge between kinds at the same level`` () =
    // Variegated graph: 4 kinds, mixed dependencies.
    let aKey = kindKey ["A"]
    let bKey = kindKey ["B"]
    let cKey = kindKey ["C"]
    let dKey = kindKey ["D"]
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Order = [ aKey; bKey; cKey; dKey ]
            Edges = [ cKey, aKey  // C depends on A
                      cKey, bKey  // C depends on B
                      dKey, cKey ] }  // D depends on C
    let levels = TopologicalOrder.levels t |> List.map ParallelSafe.members
    // For each level, check no two kinds at that level have an edge between them.
    let levelOf (k: SsKey) : int =
        levels
        |> List.findIndex (List.contains k)
    for (child, parent) in t.Edges do
        let childLvl = levelOf child
        let parentLvl = levelOf parent
        Assert.True(
            parentLvl < childLvl,
            sprintf "edge (%A → %A): parent level %d should be < child level %d" parent child parentLvl childLvl)

[<Fact>]
let ``P2: levels under Alphabetical mode mints only singleton groups — no parallelism on an order without parent-precedence proof`` () =
    // The P2-wire finding (2026-06-12): the level computation rests on
    // "parents precede children", which only Mode = Topological carries.
    // Under the Alphabetical fallback (any unresolved cycle in the
    // catalog) a child can sort before its parent; the "unknown parent
    // contributes 0" rule then collapsed this REAL FK chain into one
    // "level" — a ParallelSafe group with FK edges inside it. The mode
    // guard refuses: singleton groups in Order order, vacuously safe,
    // exactly the sequential deploy.
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Mode  = Alphabetical
            // Alphabetical order happens to put dependents first here —
            // the shape that previously flattened everything to level 0.
            Order = [ countryKey; orderKey; customerKey ]
            Edges = [ orderKey, customerKey     // order depends on customer
                      countryKey, orderKey ] }  // country depends on order
    Assert.Equal<SsKey list list>(
        [ [ countryKey ]; [ orderKey ]; [ customerKey ] ],
        TopologicalOrder.levels t |> List.map ParallelSafe.members)

[<Fact>]
let ``levels: cycle-broken kind receives finite level`` () =
    // A ⟶ B ⟶ A (self-cycle). Cycle-resolver picks A first; B follows.
    // Edge (A, B) means A depends on B — but B comes AFTER A in Order
    // (because cycle resolver broke that edge); so A's "parent B" is
    // treated as unknown → A goes to level 0.
    let aKey = kindKey ["A"]
    let bKey = kindKey ["B"]
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Mode  = JunctionDeferred
            Order = [ aKey; bKey ]
            Edges = [ aKey, bKey
                      bKey, aKey ] }
    let result = TopologicalOrder.levels t |> List.map ParallelSafe.members
    Assert.Equal(2, List.length result)
    Assert.Equal<SsKey list>([ aKey ], List.head result)
    Assert.Equal<SsKey list>([ bKey ], List.item 1 result)

// ---------------------------------------------------------------------------
// P1 / R5 — the ParallelSafe token: levels is the mint; map/choose are the
// structure-preserving carriers (the convention witness, Bucket B).
// ---------------------------------------------------------------------------

[<Fact>]
let ``P1: levels mints ParallelSafe — map and choose carry the group without merging or reordering`` () =
    let t : TopologicalOrder =
        { TopologicalOrder.empty with
            Order = [ customerKey; countryKey; orderKey ]
            Edges = [ orderKey, customerKey ] }
    let levels = TopologicalOrder.levels t
    Assert.Equal(2, List.length levels)
    // map: one image per member, same order — the rendering carrier.
    let rendered = levels |> List.map (ParallelSafe.map SsKey.rootOriginal)
    Assert.Equal<string list>(
        List.head levels |> ParallelSafe.members |> List.map SsKey.rootOriginal,
        List.head rendered |> ParallelSafe.members)
    // choose: dropping a member keeps the group's remaining members intact.
    let chosen =
        List.head levels
        |> ParallelSafe.choose (fun k -> if k = countryKey then None else Some k)
    Assert.Equal<SsKey list>(
        (List.head levels |> ParallelSafe.members |> List.filter (fun k -> k <> countryKey)),
        ParallelSafe.members chosen)
    // isEmpty: the empty group is detectable without unwrapping.
    Assert.True(ParallelSafe.choose (fun _ -> None) (List.head levels) |> ParallelSafe.isEmpty)
