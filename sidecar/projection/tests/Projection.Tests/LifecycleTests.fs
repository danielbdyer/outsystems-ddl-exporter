module Projection.Tests.LifecycleTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// FSharp.Core's two-arity Result constructors collide with
// `Projection.Core.DiagnosticSeverity.Error` once `Projection.Core` is
// opened; the private alias mirrors `CatalogDiffTests.fs` /
// `RefactorLogEmitterTests.fs`.
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail(sprintf "%A" err)
        Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        Assert.Fail(sprintf "%A" es)
        Unchecked.defaultof<'a>

let private mustResultFail (r: Result<'a>) : ValidationError list =
    match r with
    | Error es -> es
    | Ok _ ->
        Assert.Fail("Expected a failed Result.")
        []

let private nameOf (s: string) : Name = Name.create s |> mustResultOk
let private ver (ordinal: int) (label: string) : Version = Version.create ordinal label |> mustResultOk
let private tl (name: string) : Timeline = Timeline.create name |> mustResultOk

// ---------------------------------------------------------------------------
// Rename scenario (mirrors RefactorLogEmitterTests): C₁ rewrites `customer`'s
// Name while preserving its SsKey (A1, identity-survives-rename). The diff
// C₀ → C₁ is exactly one table rename.
// ---------------------------------------------------------------------------

let private renamedCustomerKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedSalesModule : Module = { salesModule with Kinds = [ renamedCustomerKind; order; country ] }
let private targetCatalog : Catalog = IRBuilders.mkCatalog [ renamedSalesModule ]

let private c0 : CatalogSnapshot = { Version = ver 0 "1.0.0"; Catalog = sampleCatalog }
let private c1 : CatalogSnapshot = { Version = ver 1 "1.1.0"; Catalog = targetCatalog }

let private devGenesis : Lifecycle = Lifecycle.genesis (tl "dev") c0
let private devChain : Lifecycle = Lifecycle.append c1 devGenesis |> mustResultOk

// ===========================================================================
// L-α — Version / Timeline value objects
// ===========================================================================

[<Fact>]
let ``Version.create rejects a negative ordinal`` () =
    let es = Version.create -1 "1.0.0" |> mustResultFail
    Assert.Contains(es, fun e -> e.Code = "version.ordinal.negative")

[<Fact>]
let ``Version.create rejects a blank label`` () =
    let es = Version.create 0 "   " |> mustResultFail
    Assert.Contains(es, fun e -> e.Code = "version.label.empty")

[<Fact>]
let ``Version.create accepts a valid ordinal and label; accessors round-trip`` () =
    let v = ver 3 "2.1.0"
    Assert.Equal(3, Version.ordinal v)
    Assert.Equal("2.1.0", Version.label v)

[<Fact>]
let ``Timeline.create rejects a blank name and accepts a valid one`` () =
    Assert.Contains(Timeline.create "" |> mustResultFail, fun e -> e.Code = "timeline.name.empty")
    Assert.Equal("uat", Timeline.name (tl "uat"))

// ===========================================================================
// L-β — Lifecycle chain + monotonic append (L3-L2)
// ===========================================================================

[<Fact>]
let ``genesis: head and latest are the genesis snapshot`` () =
    Assert.Equal(c0, Lifecycle.head devGenesis)
    Assert.Equal(c0, Lifecycle.latest devGenesis)
    Assert.Equal("dev", Timeline.name (Lifecycle.timeline devGenesis))

[<Fact>]
let ``A-Lifecycle-2 (L3-L2): append rejects a non-monotonic ordinal`` () =
    // Same ordinal as genesis — not strictly increasing.
    let stale : CatalogSnapshot = { Version = ver 0 "0.9.0"; Catalog = targetCatalog }
    let es = Lifecycle.append stale devGenesis |> mustResultFail
    Assert.Contains(es, fun e -> e.Code = "lifecycle.append.nonMonotonic")

[<Fact>]
let ``A-Lifecycle-2 (L3-L2): append advances latest and never alters prior history`` () =
    Assert.Equal(c1, Lifecycle.latest devChain)
    Assert.Equal(c0, Lifecycle.head devChain)
    // Prior history is unaltered: the genesis lifecycle still has one snapshot.
    Assert.Equal(1, List.length (Lifecycle.snapshots devGenesis))
    Assert.Equal(2, List.length (Lifecycle.snapshots devChain))

// ===========================================================================
// L-γ — evolutionChain (fold CatalogDiff.between)
// ===========================================================================

[<Fact>]
let ``evolutionChain: a genesis-only lifecycle has no edges`` () =
    let diffs = Lifecycle.evolutionChain devGenesis |> mustOk
    Assert.Empty(diffs)

[<Fact>]
let ``evolutionChain: one diff per edge`` () =
    let diffs = Lifecycle.evolutionChain devChain |> mustOk
    Assert.Equal(1, List.length diffs)
    // Three snapshots → two edges.
    let c2 : CatalogSnapshot = { Version = ver 2 "1.2.0"; Catalog = sampleCatalog }
    let longer = Lifecycle.append c2 devChain |> mustResultOk
    Assert.Equal(2, List.length (Lifecycle.evolutionChain longer |> mustOk))

[<Fact>]
let ``evolutionChain: the C0 to C1 edge captures the customer rename`` () =
    let diff = Lifecycle.evolutionChain devChain |> mustOk |> List.head
    let renamed = CatalogDiff.renamed diff
    Assert.True(Map.containsKey customerKey renamed)
    Assert.Equal("Patron", Name.value (Map.find customerKey renamed).NewName)

// ===========================================================================
// L-δ — replayTo (L3-L1) + per-timeline independence (L3-L3)
// ===========================================================================

[<Fact>]
let ``A-Lifecycle-1 (L3-L1): replayTo recovers the snapshotted catalog`` () =
    Assert.Equal<Catalog>(sampleCatalog, Lifecycle.replayTo (ver 0 "1.0.0") devChain |> mustResultOk)
    Assert.Equal<Catalog>(targetCatalog, Lifecycle.replayTo (ver 1 "1.1.0") devChain |> mustResultOk)

[<Fact>]
let ``A-Lifecycle-1 (L3-L1): replayTo fails on an absent version`` () =
    let es = Lifecycle.replayTo (ver 9 "9.9.9") devChain |> mustResultFail
    Assert.Contains(es, fun e -> e.Code = "lifecycle.version.notFound")

// NORTH_STAR §1 Time-axis round-trip witness (matrix-status.sh keys the Time
// cell on the `replayTo genesis` substring). §5.3 earns it: the genesis
// catalog C₀ is recoverable by replaying to its Version.
[<Fact>]
let ``Time round-trip (replay): replayTo genesis recovers the genesis catalog`` () =
    Assert.Equal<Catalog>(sampleCatalog, Lifecycle.replayTo (ver 0 "1.0.0") devChain |> mustResultOk)

// 6.A.11 (H-007) — replayability as a real reconstruction (fold applyDiff),
// not a snapshot fetch. The chain-level round-trip law: reconstructLatest
// derives the latest catalog from the per-edge deltas and agrees with the
// stored snapshot modulo the diff's captured surface.
[<Fact>]
let ``A-Lifecycle (6.A.11 / H-007): reconstructLatest derives the latest snapshot via fold applyDiff`` () =
    let reconstructed = Lifecycle.reconstructLatest devChain |> mustOk
    let latest = (Lifecycle.latest devChain).Catalog
    // The reconstruction (fold applyDiff genesis) reproduces the stored latest
    // (the customer-rename evolution) over the captured surface.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between latest reconstructed |> mustOk))
    // And it is NOT genesis — the rename was actually applied.
    Assert.False(CatalogDiff.isEmpty (CatalogDiff.between sampleCatalog reconstructed |> mustOk))

[<Fact>]
let ``reconstructLatest: a genesis-only lifecycle reconstructs C0`` () =
    let reconstructed = Lifecycle.reconstructLatest devGenesis |> mustOk
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between sampleCatalog reconstructed |> mustOk))

[<Fact>]
let ``A-Lifecycle-3 (L3-L3): timelines are independent histories`` () =
    let uat = Lifecycle.genesis (tl "uat") c0
    // Appending on dev produces a new value; the uat history is untouched.
    Assert.Equal("uat", Timeline.name (Lifecycle.timeline uat))
    Assert.Equal("dev", Timeline.name (Lifecycle.timeline devChain))
    Assert.Equal(1, List.length (Lifecycle.snapshots uat))
    Assert.Equal(2, List.length (Lifecycle.snapshots devChain))

// ===========================================================================
// §V E4 acceptance — Lifecycle's first real consumer.
// A 2-version evolutionChain feeds RefactorLogEmitter end-to-end and the
// stored prior catalog (C₀) becomes the refactor-log diff baseline.
// ===========================================================================

[<Fact>]
let ``E4: a 2-version evolutionChain drives RefactorLogEmitter to a correct sp_rename`` () =
    let diff = Lifecycle.evolutionChain devChain |> mustOk |> List.head
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    let customerEntries = Map.find customerKey entries
    Assert.Equal(1, List.length customerEntries)
    let entry = List.head customerEntries
    Assert.Equal(RenameRefactor, entry.OperationKind)
    Assert.Equal(SqlTable, entry.ElementType)
    Assert.Equal("[dbo].[OSUSR_S1S_CUSTOMER]", entry.ElementName)
    Assert.Equal("Patron", entry.NewName)
