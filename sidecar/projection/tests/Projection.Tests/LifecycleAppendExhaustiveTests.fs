module Projection.Tests.LifecycleAppendExhaustiveTests

// R3-LC (FORMAL_METHODS.md §2/§3): rung-3 bounded-exhaustive cover of
// `Lifecycle.append`'s monotone-history law (L3-L2).
//
// The claim: from a genesis at ordinal 0, a chain of appends succeeds
// IFF the appended ordinals are strictly increasing (each strictly
// above the running latest), and every rejection is the NAMED refusal
// `lifecycle.append.nonMonotonic` — never a silent reorder, never an
// unnamed failure.
//
// The sweep: every append sequence over the ordinal universe {0..3}
// up to depth 4 — Σ 4^d for d = 0..4 = 341 traces — folded through
// the real `Lifecycle.append`, with the accept/reject verdict checked
// against the strictly-increasing predicate computed independently.
//
// Scope stated honestly (the rung-3 boundary): exhaustive over a
// 4-ordinal universe to depth 4. The Phase-2 TLA⁺ `Lifecycle.tla`
// model lifts the same law to an unbounded-ordinal temporal spec.

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The trace universe.
// ---------------------------------------------------------------------------

let private ordinalUniverse = [ 0; 1; 2; 3 ]
let private maxDepth = 4

let rec private sequencesOfLength (n: int) : int list list =
    if n = 0 then [ [] ]
    else
        [ for tail in sequencesOfLength (n - 1) do
            for o in ordinalUniverse do
                yield o :: tail ]

let private allTraces : int list list =
    [ for d in 0 .. maxDepth do yield! sequencesOfLength d ]

// ---------------------------------------------------------------------------
// Realization — snapshots share one catalog; only the ordinal varies.
// ---------------------------------------------------------------------------

let private snapshotAt (ordinal: int) : CatalogSnapshot =
    { Version = Version.create ordinal $"v%d{ordinal}" |> Result.value
      Catalog = sampleCatalog }

let private timeline = Timeline.create "r3-lc" |> Result.value

/// Fold the trace's appends over a genesis-at-0 lifecycle through the
/// real `append`, short-circuiting on the first refusal (the caller's
/// natural Result.bind chain).
let private appendAll (ordinals: int list) : Result<Lifecycle> =
    let genesis = Lifecycle.genesis timeline (snapshotAt 0)
    ordinals
    |> List.fold
        (fun acc o -> Result.bind (Lifecycle.append (snapshotAt o)) acc)
        (Result.success genesis)

/// The law's independent side: strictly increasing above the running
/// latest, starting from the genesis ordinal 0.
let private isStrictlyIncreasingFromGenesis (ordinals: int list) : bool =
    0 :: ordinals
    |> List.pairwise
    |> List.forall (fun (prev, next) -> next > prev)

// ---------------------------------------------------------------------------
// R3-LC Law 0 — the enumeration is the space it claims to be.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-LC cardinality: the trace enumeration covers exactly sum(4^d, d=0..4) = 341 sequences`` () =
    Assert.Equal(341, List.length allTraces)
    Assert.Equal(341, allTraces |> List.distinct |> List.length)

// ---------------------------------------------------------------------------
// R3-LC Law 1 — acceptance is exactly the strictly-increasing chains.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-LC monotone acceptance: append accepts exactly the strictly-increasing extensions across every trace`` () =
    allTraces
    |> List.iter (fun trace ->
        let expected = isStrictlyIncreasingFromGenesis trace
        match appendAll trace with
        | Ok _ ->
            Assert.True(expected, sprintf "Non-monotone trace %A was accepted" trace)
        | Error _ ->
            Assert.False(expected, sprintf "Monotone trace %A was refused" trace))

// ---------------------------------------------------------------------------
// R3-LC Law 2 — every accepted chain's history is exactly genesis
// followed by the appended ordinals, in order: prior history is never
// altered, reordered, or dropped.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-LC history fidelity: every accepted trace's snapshot ordinals are genesis :: trace, in order`` () =
    allTraces
    |> List.filter isStrictlyIncreasingFromGenesis
    |> List.iter (fun trace ->
        match appendAll trace with
        | Ok lifecycle ->
            let ordinals =
                Lifecycle.snapshots lifecycle
                |> List.map (fun s -> Version.ordinal s.Version)
            Assert.Equal<int list>(0 :: trace, ordinals)
        | Error es ->
            Assert.Fail(sprintf "Monotone trace %A was refused: %A" trace es))

// ---------------------------------------------------------------------------
// R3-LC Law 3 — every refusal is the named one. No unnamed failure,
// no silent downgrade, anywhere in the space.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-LC named refusal: every rejected trace fails with lifecycle-append-nonMonotonic and nothing else`` () =
    allTraces
    |> List.filter (isStrictlyIncreasingFromGenesis >> not)
    |> List.iter (fun trace ->
        match appendAll trace with
        | Ok _ -> Assert.Fail(sprintf "Non-monotone trace %A was accepted" trace)
        | Error errors ->
            Assert.Equal(1, List.length errors)
            Assert.Equal("lifecycle.append.nonMonotonic", errors.Head.Code))
