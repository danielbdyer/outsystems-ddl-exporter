module Projection.Tests.EjectRunTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// AC-X6 — the eject protein (append-forever). The fork is RESOLVED to
// append-forever: freezing a timeline preserves EVERY episode + the full
// accumulated refactorlog, and the package self-verifies that the FTC
// reconstruction from genesis reproduces the frozen state. The witness
// discriminates against the COLLAPSE impostor (which would keep only the latest
// snapshot, discarding intermediate provenance).

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private nameOf (s: string) : Name = Name.create s |> mustResultOk
let private ver (o: int) (lbl: string) : Version = Version.create o lbl |> mustResultOk
let private tl (name: string) : Timeline = Timeline.create name |> mustResultOk
let private at (iso: string) : DateTimeOffset = DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture)

// A genesis schema (the sample catalog) evolving through one table rename
// (customer → Patron, SsKey-preserving) — a known, non-trivial displacement so
// the FTC reconstruction folds a real diff.
let private renamedCustomerKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedSalesModule : Module = { salesModule with Kinds = [ renamedCustomerKind; order; country ] }
let private targetCatalog : Catalog = IRBuilders.mkCatalog [ renamedSalesModule ]

let private coord0 = EpisodeCoordinate.create (ver 0 "1.0.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
let private coord1 = EpisodeCoordinate.create (ver 1 "1.1.0") Environment.Dev (at "2026-06-08T09:00:00+00:00")
let private coord2 = EpisodeCoordinate.create (ver 2 "1.2.0") Environment.Dev (at "2026-06-15T09:00:00+00:00")

let private e0 : Episode = Episode.ofSchema coord0 sampleCatalog
let private e1 : Episode = Episode.create coord1 targetCatalog Profile.empty (Some "refactorlog#1") (DataObservation.create 10 None)
let private e2 : Episode = Episode.create coord2 targetCatalog Profile.empty (Some "refactorlog#2") (DataObservation.create 20 None)

let private threeEpisodeChain : EpisodicLifecycle =
    EpisodicLifecycle.genesis (tl "eject-dev") e0
    |> EpisodicLifecycle.append e1
    |> mustResultOk
    |> EpisodicLifecycle.append e2
    |> mustResultOk

[<Fact>]
let ``AC-X6: eject preserves EVERY episode (append-forever, not collapsed to latest)`` () =
    let pkg = EjectRun.fromChain threeEpisodeChain |> Result.mapError (sprintf "%A") |> function Ok p -> p | Error e -> Assert.Fail e; Unchecked.defaultof<EjectPackage>
    // THE APPEND-FOREVER DISCRIMINATOR: all three episodes survive the freeze. A
    // collapse-at-freeze impostor would carry exactly 1.
    Assert.Equal(3, List.length pkg.Episodes)
    // The full refactorlog reference chain is accumulated in timeline order
    // (genesis e0 carries none; e1/e2 each carry one).
    Assert.Equal<string list>([ "refactorlog#1"; "refactorlog#2" ], pkg.RefactorLogRefs)
    // Genesis is preserved so any PRIOR state is reconstructable.
    Assert.Equal<Catalog>(sampleCatalog, pkg.GenesisSchema)

[<Fact>]
let ``AC-X6: the ejected package self-verifies — FTC reconstruction reproduces the frozen state`` () =
    match EjectRun.fromChain threeEpisodeChain with
    | Error e -> Assert.Fail(sprintf "%A" e)
    | Ok pkg ->
        // The frozen state is the latest recorded schema…
        Assert.Equal<Catalog>(targetCatalog, pkg.FrozenSchema)
        // …and the genesis→latest FTC reconstruction reproduces it (the
        // chain-level round-trip law). A package that dropped intermediate
        // episodes could not fold back to the frozen state.
        Assert.True(EjectRun.isFaithful pkg, "FTC reconstruction must reproduce the frozen state")

[<Fact>]
let ``AC-X6: eject round-trips through the durable store`` () =
    let path =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            System.String.Concat("x6-", System.Guid.NewGuid().ToString("N"), ".lifecycle.json"))
    try
        match LifecycleStore.save path threeEpisodeChain with
        | Error e -> Assert.Fail(sprintf "store save failed: %A" e)
        | Ok () ->
            match EjectRun.fromStore path with
            | Error e -> Assert.Fail e
            | Ok pkg ->
                Assert.Equal(3, List.length pkg.Episodes)
                Assert.Equal<string list>([ "refactorlog#1"; "refactorlog#2" ], pkg.RefactorLogRefs)
                Assert.True(EjectRun.isFaithful pkg)
    finally
        if System.IO.File.Exists path then System.IO.File.Delete path
