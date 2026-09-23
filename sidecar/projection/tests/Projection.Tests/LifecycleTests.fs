module Projection.Tests.LifecycleTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The temporal axis on its LIVING carrier (align-III.5): these laws were
// authored against the schema-only `Lifecycle`/`CatalogSnapshot` twin and are
// ported verbatim onto `EpisodicLifecycle` + `Episode.ofSchema` — the durable
// multi-plane grain that subsumed it. The axiom-cited test names (L3-L1 /
// L3-L2 / L3-L3, the Time round-trip witness) are unchanged; the refusal
// codes are the episodic surface's own.
// ---------------------------------------------------------------------------

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
// Rename scenario (mirrors RefactorLogEmitterTests): E₁ rewrites `customer`'s
// Name while preserving its SsKey (A1, identity-survives-rename). The diff
// E₀ → E₁ is exactly one table rename.
// ---------------------------------------------------------------------------

let private renamedCustomerKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedSalesModule : Module = { salesModule with Kinds = [ renamedCustomerKind; order; country ] }
let private targetCatalog : Catalog = IRBuilders.mkCatalog [ renamedSalesModule ]

let private at0 = System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

/// A schema-only episode at an ordinal — `Episode.ofSchema` is the exact
/// carrier the retired `CatalogSnapshot` collapsed into.
let private ep (ordinal: int) (label: string) (catalog: Catalog) : Episode =
    Episode.ofSchema (EpisodeCoordinate.create (ver ordinal label) Environment.Dev at0) catalog

let private e0 : Episode = ep 0 "1.0.0" sampleCatalog
let private e1 : Episode = ep 1 "1.1.0" targetCatalog

let private devGenesis : EpisodicLifecycle = EpisodicLifecycle.genesis (tl "dev") e0
let private devChain : EpisodicLifecycle = EpisodicLifecycle.append e1 devGenesis |> mustResultOk

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
// L-β — episodic chain + monotonic append (L3-L2)
// ===========================================================================

[<Fact>]
let ``genesis: head and latest are the genesis episode`` () =
    Assert.Equal(e0, EpisodicLifecycle.head devGenesis)
    Assert.Equal(e0, EpisodicLifecycle.latest devGenesis)
    Assert.Equal("dev", Timeline.name (EpisodicLifecycle.timeline devGenesis))

[<Fact>]
let ``A-Lifecycle-2 (L3-L2): append rejects a non-monotonic ordinal`` () =
    // Same ordinal as genesis — not strictly increasing.
    let stale : Episode = ep 0 "0.9.0" targetCatalog
    let es = EpisodicLifecycle.append stale devGenesis |> mustResultFail
    Assert.Contains(es, fun e -> e.Code = "episodicLifecycle.append.nonMonotonic")

[<Fact>]
let ``A-Lifecycle-2 (L3-L2): append advances latest and never alters prior history`` () =
    Assert.Equal(e1, EpisodicLifecycle.latest devChain)
    Assert.Equal(e0, EpisodicLifecycle.head devChain)
    // Prior history is unaltered: the genesis lifecycle still has one episode.
    Assert.Equal(1, List.length (EpisodicLifecycle.episodes devGenesis))
    Assert.Equal(2, List.length (EpisodicLifecycle.episodes devChain))

// ===========================================================================
// L-γ — schemaEvolutionChain (fold CatalogDiff.between over Episode.Schema)
// ===========================================================================

[<Fact>]
let ``evolutionChain: a genesis-only lifecycle has no edges`` () =
    let diffs = EpisodicLifecycle.schemaEvolutionChain devGenesis |> mustOk
    Assert.Empty(diffs)

[<Fact>]
let ``evolutionChain: one diff per edge`` () =
    let diffs = EpisodicLifecycle.schemaEvolutionChain devChain |> mustOk
    Assert.Equal(1, List.length diffs)
    // Three episodes → two edges.
    let e2 : Episode = ep 2 "1.2.0" sampleCatalog
    let longer = EpisodicLifecycle.append e2 devChain |> mustResultOk
    Assert.Equal(2, List.length (EpisodicLifecycle.schemaEvolutionChain longer |> mustOk))

[<Fact>]
let ``evolutionChain: the E0 to E1 edge captures the customer rename`` () =
    let diff = EpisodicLifecycle.schemaEvolutionChain devChain |> mustOk |> List.head
    let renamed = CatalogDiff.renamed diff
    Assert.True(Map.containsKey customerKey renamed)
    Assert.Equal("Patron", Name.value (Map.find customerKey renamed).NewName)

// ===========================================================================
// L-δ — replayTo (L3-L1) + per-timeline independence (L3-L3)
// ===========================================================================

[<Fact>]
let ``A-Lifecycle-1 (L3-L1): replayTo recovers the snapshotted catalog`` () =
    Assert.Equal<Catalog>(sampleCatalog, EpisodicLifecycle.replayTo (ver 0 "1.0.0") devChain |> mustResultOk)
    Assert.Equal<Catalog>(targetCatalog, EpisodicLifecycle.replayTo (ver 1 "1.1.0") devChain |> mustResultOk)

[<Fact>]
let ``A-Lifecycle-1 (L3-L1): replayTo fails on an absent version`` () =
    let es = EpisodicLifecycle.replayTo (ver 9 "9.9.9") devChain |> mustResultFail
    Assert.Contains(es, fun e -> e.Code = "episodicLifecycle.version.notFound")

// NORTH_STAR §1 Time-axis round-trip witness — self-declared to
// matrix-status.sh via the `@axis Time roundtrip` tag below (align-III.10).
// §5.3 earns it: the genesis catalog E₀.Schema is recoverable by replaying
// to its Version.
[<Fact>]
// @axis Time roundtrip
let ``Time round-trip (replay): replayTo genesis recovers the genesis catalog`` () =
    Assert.Equal<Catalog>(sampleCatalog, EpisodicLifecycle.replayTo (ver 0 "1.0.0") devChain |> mustResultOk)

// 6.A.11 (H-007) — replayability as a real reconstruction (fold applyDiff),
// not a snapshot fetch. The chain-level round-trip law: reconstructLatestSchema
// derives the latest schema from the per-edge deltas and agrees with the
// stored episode modulo the diff's captured surface.
[<Fact>]
let ``A-Lifecycle (6.A.11 / H-007): reconstructLatest derives the latest snapshot via fold applyDiff`` () =
    let reconstructed = EpisodicLifecycle.reconstructLatestSchema devChain |> mustOk
    let latest = Episode.schema (EpisodicLifecycle.latest devChain)
    // The reconstruction (fold applyDiff genesis) reproduces the stored latest
    // (the customer-rename evolution) over the captured surface.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between latest reconstructed))
    // And it is NOT genesis — the rename was actually applied.
    Assert.False(CatalogDiff.isEmpty (CatalogDiff.between sampleCatalog reconstructed))

[<Fact>]
let ``reconstructLatest: a genesis-only lifecycle reconstructs E0`` () =
    let reconstructed = EpisodicLifecycle.reconstructLatestSchema devGenesis |> mustOk
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between sampleCatalog reconstructed))

// 6.H.3 — netSchemaDiff (the integral ∫δ as a single delta) + its equality to
// fold-compose over the evolution chain (the FTC's companion). A 3-episode
// chain genuinely exercises CatalogDiff.compose in the fold.
[<Fact>]
let ``6.H.3: netDiff applied to genesis reproduces latest (the integral)`` () =
    let nd = EpisodicLifecycle.netSchemaDiff devChain |> mustOk
    let reconstructed = CatalogDiff.applyDiff sampleCatalog nd
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between targetCatalog reconstructed))

[<Fact>]
let ``6.H.3: netDiff equals fold compose over the evolution chain (3 snapshots)`` () =
    // genesis (sampleCatalog) → e1 (Customer renamed Patron) → e2 (back to sample).
    let e2 : Episode = ep 2 "1.2.0" sampleCatalog
    let chainLc = EpisodicLifecycle.append e2 devChain |> mustResultOk
    let edges = EpisodicLifecycle.schemaEvolutionChain chainLc |> mustOk
    Assert.Equal(2, List.length edges)  // two edges → the fold actually composes
    let folded =
        match edges with
        | d0 :: rest ->
            rest |> List.fold (fun acc d ->
                match CatalogDiff.compose acc d with
                | Some c -> c
                | None -> Assert.Fail "lifecycle edges must be composable"; Unchecked.defaultof<_>) d0
        | [] -> Assert.Fail "expected edges"; Unchecked.defaultof<_>
    let nd = EpisodicLifecycle.netSchemaDiff chainLc |> mustOk
    let viaFold = CatalogDiff.applyDiff sampleCatalog folded
    let viaNet = CatalogDiff.applyDiff sampleCatalog nd
    // Both reproduce the latest (sampleCatalog again, here) over the captured surface.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between viaFold viaNet))
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between sampleCatalog viaNet))

// P4 — CatalogDiff.compose's PRODUCTION caller on the temporal axis:
// EpisodicLifecycle.netSchemaDiff folds it over the evolution chain. This test
// asserts the production net-diff (the compose fold) equals the direct
// between(genesis, latest) over a ≥3-episode chain — the functor law exercised
// in production, not just the unit test.
[<Fact>]
let ``P4 (6.H.3): production netDiff (compose fold) equals direct between(genesis, latest) over a 3-snapshot chain`` () =
    // genesis (sampleCatalog) → e1 (Customer renamed Patron) → e2 (back to sample).
    // Three episodes, two edges → the netSchemaDiff fold genuinely composes.
    let e2 : Episode = ep 2 "1.2.0" sampleCatalog
    let chainLc = EpisodicLifecycle.append e2 devChain |> mustResultOk
    Assert.Equal(2, List.length (EpisodicLifecycle.schemaEvolutionChain chainLc |> mustOk))
    // The production netSchemaDiff routes through CatalogDiff.compose (P4 consumer).
    let viaCompose = EpisodicLifecycle.netSchemaDiff chainLc |> mustOk
    // The direct between(genesis, latest) — the diff the fold must equal by the
    // functor law.
    let genesis = Episode.schema (EpisodicLifecycle.head chainLc)
    let latest = Episode.schema (EpisodicLifecycle.latest chainLc)
    let direct = CatalogDiff.between genesis latest
    // The composed net-diff equals the direct diff on the captured surface.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between
                                        (CatalogDiff.applyDiff genesis viaCompose)
                                        (CatalogDiff.applyDiff genesis direct)))
    // And the channel norms agree (round-trip churn cancels; net is empty here).
    Assert.Equal(CatalogDiff.norm direct, CatalogDiff.norm viaCompose)

[<Fact>]
let ``P4 (6.H.3): production netDiff over a non-trivial-net 3-snapshot chain equals direct between`` () =
    // genesis (sampleCatalog) → e1 (Customer renamed Patron) → e2 (stays Patron).
    // Net displacement E0→E2 is a genuine rename (NOT empty) — discriminates a
    // compose fold that silently dropped the middle edge.
    let e2 : Episode = ep 2 "1.2.0" targetCatalog
    let chainLc = EpisodicLifecycle.append e2 devChain |> mustResultOk
    let viaCompose = EpisodicLifecycle.netSchemaDiff chainLc |> mustOk
    let genesis = Episode.schema (EpisodicLifecycle.head chainLc)
    let direct = CatalogDiff.between genesis (Episode.schema (EpisodicLifecycle.latest chainLc))
    // Both reconstruct the latest (Patron) from genesis.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between
                                        (CatalogDiff.applyDiff genesis viaCompose)
                                        targetCatalog))
    Assert.Equal(CatalogDiff.norm direct, CatalogDiff.norm viaCompose)
    // The net is genuinely non-empty (the rename survived the fold).
    Assert.False(CatalogDiff.isEmpty viaCompose)

[<Fact>]
let ``P4 (6.H.3): production netDiff on a genesis-only lifecycle is the empty delta`` () =
    let nd = EpisodicLifecycle.netSchemaDiff devGenesis |> mustOk
    Assert.True(CatalogDiff.isEmpty nd)

// ---------------------------------------------------------------------------
// NM-45 — netSchemaDiff's non-composable fold is a NAMED refusal, not a silent
// fallback. `CatalogDiff.compose` returns `None` (fail-loud) exactly when two
// diffs are not adjacent on the captured surface; the branch is unreachable
// for a well-formed monotone chain (EpisodicLifecycle.append enforces it), so
// we (1) prove the fail-loud precondition `compose` guards on directly, and
// (2) prove every well-formed multi-edge chain that reaches the fold returns Ok
// — the NonComposableLifecycleChain error never fires on a monotone chain.
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-45: CatalogDiff.compose returns None (fail-loud) on non-adjacent diffs`` () =
    // d1 : genesis → targetCatalog (Customer renamed Patron).
    // d2 : genesis → genesis (the empty self-diff). d1's TARGET (Patron) does
    // NOT meet d2's SOURCE (genesis/Customer) on the captured surface, so the
    // groupoid composition is undefined — `compose` returns None. This is the
    // exact fail-loud signal netSchemaDiff's None branch names rather than masks.
    let d1 = CatalogDiff.between sampleCatalog targetCatalog
    let d2 = CatalogDiff.between sampleCatalog sampleCatalog
    Assert.True(Option.isNone (CatalogDiff.compose d1 d2),
                "compose must fail loud (None) when d1.target does not meet d2.source")
    // ...and the ADJACENT pair composes (Some) — the precondition is genuine.
    let d2adj = CatalogDiff.between targetCatalog sampleCatalog
    Assert.True(Option.isSome (CatalogDiff.compose d1 d2adj),
                "compose must be defined (Some) on an adjacent pair")

[<Fact>]
let ``NM-45: netDiff over a well-formed monotone chain is always Ok (the named refusal never fires)`` () =
    // Several well-formed chains, each reaching the compose fold (>= 2 edges).
    // Every one must be Ok — NonComposableLifecycleChain is unreachable by
    // construction for a monotone episodic lifecycle.
    let e2a : Episode = ep 2 "1.2.0" sampleCatalog
    let e2b : Episode = ep 2 "1.2.0" targetCatalog
    let chainBackToSample = EpisodicLifecycle.append e2a devChain |> mustResultOk
    let chainStaysPatron  = EpisodicLifecycle.append e2b devChain |> mustResultOk
    for chain in [ chainBackToSample; chainStaysPatron ] do
        match EpisodicLifecycle.netSchemaDiff chain with
        | Ok _ -> ()
        | Error (NonComposableLifecycleChain reason) ->
            Assert.Fail(sprintf "monotone chain produced the named non-composable refusal: %s" reason)
        | Error e -> Assert.Fail(sprintf "unexpected EmitError: %A" e)

[<Fact>]
let ``A-Lifecycle-3 (L3-L3): timelines are independent histories`` () =
    let uat = EpisodicLifecycle.genesis (tl "uat") e0
    // Appending on dev produces a new value; the uat history is untouched.
    Assert.Equal("uat", Timeline.name (EpisodicLifecycle.timeline uat))
    Assert.Equal("dev", Timeline.name (EpisodicLifecycle.timeline devChain))
    Assert.Equal(1, List.length (EpisodicLifecycle.episodes uat))
    Assert.Equal(2, List.length (EpisodicLifecycle.episodes devChain))

// ===========================================================================
// §V E4 acceptance — the temporal chain's first real consumer.
// A 2-episode schemaEvolutionChain feeds RefactorLogEmitter end-to-end and
// the stored prior schema (E₀) becomes the refactor-log diff baseline.
// ===========================================================================

[<Fact>]
let ``E4: a 2-version evolutionChain drives RefactorLogEmitter to a correct sp_rename`` () =
    let diff = EpisodicLifecycle.schemaEvolutionChain devChain |> mustOk |> List.head
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    let customerEntries = Map.find customerKey entries
    Assert.Equal(1, List.length customerEntries)
    let entry = List.head customerEntries
    Assert.Equal(RenameRefactor, entry.OperationKind)
    Assert.Equal(SqlTable, entry.ElementType)
    Assert.Equal("[dbo].[OSUSR_S1S_CUSTOMER]", entry.ElementName)
    Assert.Equal("Patron", entry.NewName)
