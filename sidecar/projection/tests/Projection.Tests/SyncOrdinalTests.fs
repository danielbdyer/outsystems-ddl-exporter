module Projection.Tests.SyncOrdinalTests

// align-III.1 (audit a5's S-VO charge) — the sink edition ordinal VO. The
// laws pin what the private constructor makes unrepresentable: a sync below
// 1 cannot be minted (stored wires re-validate through `create`, fail-closed),
// "nothing witnessed yet" is the option (never a magic 0), and the successor
// discipline (`next`) is genesis-from-nothing, predecessor-plus-one after.

open Xunit
open Projection.Core

[<Fact>]
let ``align-III.1: create is fail-closed below 1 — no zero or negative edition exists`` () =
    match SyncOrdinal.create 1 with
    | Ok o -> Assert.Equal(SyncOrdinal.genesis, o)
    | Error m -> Assert.Fail m
    match SyncOrdinal.create 7 with
    | Ok o ->
        Assert.Equal(7, SyncOrdinal.value o)
        Assert.Equal("7", SyncOrdinal.text o)
    | Error m -> Assert.Fail m
    for bad in [ 0; -1; -42 ] do
        match SyncOrdinal.create bad with
        | Ok o -> Assert.Fail (sprintf "%d minted an edition: %A" bad o)
        | Error m -> Assert.Contains("1-based", m)

[<Fact>]
let ``align-III.1: next mints genesis from nothing and the successor after — absence is the option, never 0`` () =
    Assert.Equal(SyncOrdinal.genesis, SyncOrdinal.next None)
    Assert.Equal(1, SyncOrdinal.value (SyncOrdinal.next None))
    let third = SyncOrdinal.next (Some (SyncOrdinal.next (Some SyncOrdinal.genesis)))
    Assert.Equal(3, SyncOrdinal.value third)

[<Fact>]
let ``align-III.1: ordinals order and equate structurally (the ledger's comparisons ride the VO)`` () =
    let two = SyncOrdinal.next (Some SyncOrdinal.genesis)
    Assert.True(SyncOrdinal.genesis < two)
    Assert.Equal(two, SyncOrdinal.next (Some SyncOrdinal.genesis))
    Assert.Equal(Some two, List.max [ Some SyncOrdinal.genesis; Some two; None ])

[<Fact>]
let ``align-III.1: SinkEdition renders the digest-at-ordinal pin form — the pair travels together`` () =
    let edition = { ConnDigest = "abcd1234abcd1234"; Ordinal = SyncOrdinal.next (Some SyncOrdinal.genesis) }
    Assert.Equal("abcd1234abcd1234@2", SinkEdition.text edition)
