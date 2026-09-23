module Projection.Tests.EpisodeTests

open System
open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// The two-arity Result constructors collide with `Projection.Core` once opened
// (mirrors LifecycleTests / CatalogDiffTests).
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err -> Assert.Fail(sprintf "%A" err); Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private mustResultFail (r: Result<'a>) : ValidationError list =
    match r with
    | Error es -> es
    | Ok _ -> Assert.Fail("Expected a failed Result."); []

let private nameOf (s: string) : Name = Name.create s |> mustResultOk
let private ver (ordinal: int) (label: string) : Version = Version.create ordinal label |> mustResultOk
let private tl (name: string) : Timeline = Timeline.create name |> mustResultOk
let private at (iso: string) : DateTimeOffset = DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture)

// Genesis schema = the sample catalog; the next schema renames `customer` →
// `Patron` (preserving SsKey, A1) — exactly one table rename, mirroring
// LifecycleTests so the schema-plane FTC has a known displacement.
let private renamedCustomerKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedSalesModule : Module = { salesModule with Kinds = [ renamedCustomerKind; order; country ] }
let private targetCatalog : Catalog = IRBuilders.mkCatalog [ renamedSalesModule ]

let private coord0 = EpisodeCoordinate.create (ver 0 "1.0.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
let private coord1 = EpisodeCoordinate.create (ver 1 "1.1.0") Environment.Dev (at "2026-06-08T09:00:00+00:00")

let private e0 : Episode = Episode.ofSchema coord0 sampleCatalog
let private e1 : Episode =
    Episode.create coord1 targetCatalog Profile.empty (Some "refactorlog#1") (DataObservation.observed 42 (Some "lsn:0x0A"))

let private devGenesis : EpisodicLifecycle = EpisodicLifecycle.genesis (tl "dev") e0
let private devChain : EpisodicLifecycle = EpisodicLifecycle.append e1 devGenesis |> mustResultOk

// ===========================================================================
// 6.H.1 — Episode co-records the five concerns at one coordinate
// ===========================================================================

[<Fact>]
let ``6.H.1: episode co-records schema + profile + refactorlog + cdc-handle at one Version`` () =
    // Schema plane.
    Assert.Equal<Catalog>(targetCatalog, Episode.schema e1)
    // Data plane (the CDC observation — a real measurement, count + handle).
    Assert.Equal(DataObservation.observed 42 (Some "lsn:0x0A"), e1.Data)
    Assert.Equal(42, DataObservation.captureCount e1.Data)
    Assert.Equal(Some "lsn:0x0A", DataObservation.handle e1.Data)
    // Decision plane (the emitted refactorlog reference).
    Assert.Equal(Some "refactorlog#1", e1.RefactorLogRef)
    // Time plane (the (Environment × Version × At) coordinate).
    Assert.Equal(1, Version.ordinal (Episode.version e1))
    Assert.Equal("DEV", Environment.name (Episode.coordinate e1).Environment)
    Assert.Equal(at "2026-06-08T09:00:00+00:00", (Episode.coordinate e1).At)

[<Fact>]
let ``6.H.1: ofSchema is the minimal-evidence shape (empty profile, no data observation, no refactorlog)`` () =
    Assert.Equal<Profile>(Profile.empty, e0.Profile)
    // align-III.6: a schema-only episode is NOT OBSERVED — no CDC ruler ran;
    // this is not the same fact as a measured zero.
    Assert.Equal(DataObservation.NotObserved, e0.Data)
    Assert.False(DataObservation.ran e0.Data)
    Assert.Equal(None, e0.RefactorLogRef)

[<Fact>]
let ``6.H.1: durableProjection drops the in-memory Profile, preserving every other plane`` () =
    let bystander = (order.SsKey)
    let withProfile =
        { e1 with Profile = { Profile.empty with CdcAwareness = CdcAwareness.create (Set.singleton bystander) Map.empty } }
    Assert.NotEqual<Profile>(Profile.empty, withProfile.Profile)
    let durable = Episode.durableProjection withProfile
    Assert.Equal<Profile>(Profile.empty, durable.Profile)
    // Only the Profile changes; restoring it recovers the original.
    Assert.Equal<Episode>(withProfile, { durable with Profile = withProfile.Profile })

// ===========================================================================
// EpisodicLifecycle — monotone chain
// ===========================================================================

[<Fact>]
let ``align-III.2: the episode grain names its discipline — admitChain runs Monotone through the shared ledger substrate`` () =
    // The grain finally instantiates a LedgerSpec (Monotone over the
    // schema-plane ordinal); admitChain admits a strictly-increasing chain
    // and refuses a regression as the typed ChainRefusal — the same order
    // `append` holds edge by edge.
    match EpisodicLifecycle.admitChain [ e0; e1 ] with
    | Ok verified -> Assert.Equal(2, List.length verified)
    | Error r -> failwithf "a monotone episode chain must admit; refused %A" r
    match EpisodicLifecycle.admitChain [ e1; e0 ] with
    | Ok _ -> failwith "a regressing episode chain must never admit"
    | Error (ChainRefusal.OrdinalRegression (pos, ordinal, prior)) ->
        Assert.Equal(1, pos); Assert.Equal(0, ordinal); Assert.Equal(1, prior)
    | Error other -> failwithf "expected OrdinalRegression, got %A" other

[<Fact>]
let ``EpisodicLifecycle.append enforces monotonic history (L3-L2)`` () =
    let es = EpisodicLifecycle.append e0 devGenesis |> mustResultFail
    Assert.Contains(es, fun e -> e.Code = "episodicLifecycle.append.nonMonotonic")

[<Fact>]
let ``EpisodicLifecycle: head is genesis, latest is the last appended`` () =
    Assert.Equal(0, Version.ordinal (Episode.version (EpisodicLifecycle.head devChain)))
    Assert.Equal(1, Version.ordinal (Episode.version (EpisodicLifecycle.latest devChain)))
    Assert.Equal("dev", Timeline.name (EpisodicLifecycle.timeline devChain))

// ===========================================================================
// The schema-plane FTC — the same CatalogDiff algebra Lifecycle runs,
// projected onto Episode.Schema (∂κ/∂episode over the schema concern)
// ===========================================================================

[<Fact>]
let ``EpisodicLifecycle.reconstructLatestSchema derives the latest schema via fold applyDiff`` () =
    let reconstructed = EpisodicLifecycle.reconstructLatestSchema devChain |> mustOk
    // Agrees with the stored latest schema modulo the captured surface.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between targetCatalog reconstructed))
    // And is genuinely the evolved schema, not genesis.
    Assert.False(CatalogDiff.isEmpty (CatalogDiff.between sampleCatalog reconstructed))

[<Fact>]
let ``EpisodicLifecycle.reconstructLatestSchema on a genesis-only lifecycle is genesis`` () =
    let reconstructed = EpisodicLifecycle.reconstructLatestSchema devGenesis |> mustOk
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between sampleCatalog reconstructed))

[<Fact>]
let ``EpisodicLifecycle.netSchemaDiff is the net displacement genesis to latest (one rename)`` () =
    let nd = EpisodicLifecycle.netSchemaDiff devChain |> mustOk
    Assert.Equal(1, CatalogDiff.norm nd)
    let reconstructed = CatalogDiff.applyDiff sampleCatalog nd
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between targetCatalog reconstructed))

[<Fact>]
let ``EpisodicLifecycle.schemaEvolutionChain has one edge per consecutive pair`` () =
    let chain = EpisodicLifecycle.schemaEvolutionChain devChain |> mustOk
    Assert.Equal(1, List.length chain)
    Assert.Equal(1, CatalogDiff.norm (List.head chain))

[<Fact>]
let ``Episode.withProvenance carries a canary-resolved tolerance residual (closes the NM-32 placeholder)`` () =
    // A canary-coupled run resolves a non-strict residual (the empty-text → NULL
    // erasure fired and was accepted); withProvenance records it on the episode,
    // replacing the Tolerance.strict placeholder.
    let residual = Tolerance.ofSet (Set.ofList [ ToleratedDivergence.CharAnsiPaddingTolerated ])
    let withProv = Episode.withProvenance residual [] e0
    Assert.False(Tolerance.isStrict withProv.Tolerances)
    Assert.True(Tolerance.tolerates ToleratedDivergence.CharAnsiPaddingTolerated withProv.Tolerances)
    // The genesis episode itself stays strict (the honest no-canary base case).
    Assert.True(Tolerance.isStrict e0.Tolerances)

[<Fact>]
let ``align-III.6: measured-zero is not unmeasured — Observed 0 and NotObserved are distinct facts`` () =
    // CDC-silence (an idempotent redeploy: the ruler RAN and read zero) is
    // the strongest guarantee; "no one looked" claims nothing. The retired
    // record folded both onto { CdcCaptureCount = 0 }.
    let silence = DataObservation.observed 0 None
    Assert.NotEqual<DataObservation>(DataObservation.NotObserved, silence)
    Assert.True(DataObservation.ran silence)
    Assert.False(DataObservation.ran DataObservation.NotObserved)
    // Both project zero onto the norm — the DU, not the fold, carries the fact.
    Assert.Equal(0, DataObservation.captureCount silence)
    Assert.Equal(0, DataObservation.captureCount DataObservation.NotObserved)
    Assert.Equal(None, DataObservation.handle DataObservation.NotObserved)
