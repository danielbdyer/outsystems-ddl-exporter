module Projection.Tests.LifecycleStoreTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err -> Assert.Fail(sprintf "%A" err); Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private mustStoreOk (r: FsResult<'a, LifecycleStoreError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> Assert.Fail(sprintf "%A" e); Unchecked.defaultof<'a>

let private nameOf (s: string) : Name = Name.create s |> mustResultOk
let private ver (o: int) (l: string) : Version = Version.create o l |> mustResultOk
let private tl (name: string) : Timeline = Timeline.create name |> mustResultOk
let private at (iso: string) : DateTimeOffset = DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture)

let private renamedKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedModule : Module = { salesModule with Kinds = [ renamedKind; order; country ] }
let private targetCatalog : Catalog = IRBuilders.mkCatalog [ renamedModule ]

let private coord o lbl env day = EpisodeCoordinate.create (ver o lbl) env (at (sprintf "2026-06-%02dT09:00:00+00:00" day))

// A durable-faithful chain (every episode carries Profile.empty), promoted
// across environments (dev → qa → named "staging"), exercising the tagged
// Environment round-trip including the `Named` escape hatch.
let private e0 = Episode.ofSchema (coord 0 "1.0.0" Environment.Dev 1) sampleCatalog
let private e1 =
    Episode.create (coord 1 "1.1.0" Environment.Qa 8) targetCatalog Profile.empty (Some "reflog#1") (DataObservation.create 120 (Some "lsn:0x10"))
let private e2 =
    Episode.create (coord 2 "1.2.0" (Environment.Named "staging") 15) targetCatalog Profile.empty None (DataObservation.create 0 None)

let private chain : EpisodicLifecycle =
    EpisodicLifecycle.genesis (tl "dev") e0
    |> (fun lc -> EpisodicLifecycle.append e1 lc |> mustResultOk)
    |> (fun lc -> EpisodicLifecycle.append e2 lc |> mustResultOk)

let private withTempFile (f: string -> unit) : unit =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "lifecycle-%s.json" (Guid.NewGuid().ToString "N"))
    try f path
    finally if System.IO.File.Exists path then System.IO.File.Delete path

// ===========================================================================
// 6.H.2 — the LifecycleStore persists the chain; load reconstructs it
// ===========================================================================

[<Fact>]
let ``6.H.2: save then load round-trips a durable-faithful chain exactly`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        Assert.Equal<EpisodicLifecycle>(chain, loaded))

[<Fact>]
let ``6.H.2: reconstructLatestSchema over the persisted chain reproduces the stored latest schema (FTC, durable)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let reconstructed = EpisodicLifecycle.reconstructLatestSchema loaded |> mustOk
        // The FTC over the loaded chain reproduces the stored latest schema
        // (modulo the diff's captured surface).
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between targetCatalog reconstructed |> mustOk)))

[<Fact>]
let ``6.H.2: every plane but the Profile survives the round-trip (coordinate, schema, data, refactorlog)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedE1 = EpisodicLifecycle.episodes loaded |> List.item 1
        Assert.Equal("QA", Environment.name loadedE1.Coordinate.Environment)
        Assert.Equal(1, Version.ordinal loadedE1.Coordinate.Version)
        Assert.Equal(at "2026-06-08T09:00:00+00:00", loadedE1.Coordinate.At)
        Assert.Equal(Some "reflog#1", loadedE1.RefactorLogRef)
        Assert.Equal(120, loadedE1.Data.CdcCaptureCount)
        Assert.Equal(Some "lsn:0x10", loadedE1.Data.CdcHandle)
        Assert.Equal<Catalog>(targetCatalog, loadedE1.Schema))

[<Fact>]
let ``6.H.2: the Named environment escape hatch round-trips unambiguously`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedE2 = EpisodicLifecycle.episodes loaded |> List.item 2
        Assert.Equal("staging", Environment.name loadedE2.Coordinate.Environment))

[<Fact>]
let ``6.H.2: the Profile is not persisted — a loaded episode equals its durableProjection`` () =
    withTempFile (fun path ->
        // Save a chain whose genesis carries a non-empty in-memory Profile.
        let profiled =
            { e0 with Profile = { Profile.empty with CdcAwareness = CdcAwareness.create (Set.singleton order.SsKey) Map.empty } }
        let profiledChain =
            EpisodicLifecycle.genesis (tl "dev") profiled
            |> (fun lc -> EpisodicLifecycle.append e1 lc |> mustResultOk)
        LifecycleStore.save path profiledChain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedGenesis = EpisodicLifecycle.head loaded
        // §12.4: the statistical Profile is not durable provenance.
        Assert.Equal<Profile>(Profile.empty, loadedGenesis.Profile)
        // The loaded chain equals the original projected through durableProjection.
        let expected =
            EpisodicLifecycle.genesis (tl "dev") (Episode.durableProjection profiled)
            |> (fun lc -> EpisodicLifecycle.append (Episode.durableProjection e1) lc |> mustResultOk)
        Assert.Equal<EpisodicLifecycle>(expected, loaded))

[<Fact>]
let ``6.H.2: save is byte-deterministic (T1)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let first = System.IO.File.ReadAllBytes path
        LifecycleStore.save path chain |> mustStoreOk
        let second = System.IO.File.ReadAllBytes path
        Assert.Equal<byte[]>(first, second))

// ===========================================================================
// Failure paths — fail-closed, structured errors, never silent
// ===========================================================================

[<Fact>]
let ``6.H.2: loading a non-existent file is a ParseFailure (a lifecycle has no empty value)`` () =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "missing-%s.json" (Guid.NewGuid().ToString "N"))
    match LifecycleStore.load path with
    | FsResult.Error (LifecycleStoreError.ParseFailure _) -> ()
    | other -> Assert.Fail(sprintf "expected ParseFailure, got %A" other)

[<Fact>]
let ``6.H.2: a malformed file is a ParseFailure, never a silent partial load`` () =
    withTempFile (fun path ->
        System.IO.File.WriteAllText(path, "{ not a lifecycle")
        match LifecycleStore.load path with
        | FsResult.Error (LifecycleStoreError.ParseFailure _) -> ()
        | other -> Assert.Fail(sprintf "expected ParseFailure, got %A" other))

[<Fact>]
let ``6.H.2: a corrupt embedded schema fails the load (codec re-validation surfaces)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        // Corrupt the embedded catalog interior: rename each kind's "physical"
        // property (written only by the codec, not the store envelope), so the
        // codec's `field "physical"` fails inside CatalogCodec.deserialize. The
        // store surfaces that as a ParseFailure — codec re-validation reaches
        // through the persistence boundary.
        let text = System.IO.File.ReadAllText path
        let corrupted = text.Replace("\"physical\"", "\"physicalX\"")
        System.IO.File.WriteAllText(path, corrupted)
        match LifecycleStore.load path with
        | FsResult.Error (LifecycleStoreError.ParseFailure _) -> ()
        | other -> Assert.Fail(sprintf "expected ParseFailure from codec re-validation, got %A" other))
