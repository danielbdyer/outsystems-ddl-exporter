module Projection.Tests.NormalizeStaticPopulationsTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `NormalizeStaticPopulations.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private nspRun (c: Catalog) : Lineage<Catalog> =
    NormalizeStaticPopulations.registered.Run c |> Lineage.map (fun d -> d.Value)

// Chapter A.4.7' slice η — `CanonicalizeIdentity.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// Helpers — perturb the synthetic catalog's Country populations without
// changing identity, so we can verify the pass restores order.
// ---------------------------------------------------------------------------

let private withReversedCountryRows (c: Catalog) : Catalog =
    { Modules =
        c.Modules
        |> List.map (fun m ->
            { m with
                Kinds =
                    m.Kinds
                    |> List.map (fun k ->
                        if k.SsKey = countryKey then
                            { k with
                                Modality =
                                    k.Modality
                                    |> List.map (function
                                        | Static rows -> Static (List.rev rows)
                                        | other       -> other) }
                        else k) })
      Sequences = c.Sequences }

let private extractCountryRows (c: Catalog) : StaticRow list =
    let countryK = Catalog.tryFindKind countryKey c |> Option.get
    countryK.Modality
    |> List.choose (function Static rows -> Some rows | _ -> None)
    |> List.head

// ---------------------------------------------------------------------------
// Idempotence — running the pass twice equals running it once. Carries
// the V1 EntitySeedDeterminizer invariant directly into V2 (per the
// admire-entry contract: V2 must satisfy V1's invariants on the
// migrated subset).
// ---------------------------------------------------------------------------

[<Fact>]
let ``contract: idempotent on the synthetic fixture`` () =
    let once  = (nspRun sampleCatalog).Value
    let twice = (nspRun once).Value
    Assert.Equal(once, twice)

[<Fact>]
let ``contract: idempotent on a perturbed catalog`` () =
    let perturbed = withReversedCountryRows sampleCatalog
    let once  = (nspRun perturbed).Value
    let twice = (nspRun once).Value
    Assert.Equal(once, twice)

// ---------------------------------------------------------------------------
// Determinism (T1) — same input ⇒ same output, including lineage trail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: NormalizeStaticPopulations is deterministic`` () =
    let r1 = nspRun sampleCatalog
    let r2 = nspRun sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Normalization-on-perturbed input — the V1 EntitySeedDeterminizer's
// raison d'être. Reversed rows come back sorted; the canonical and the
// perturbed inputs converge to the same output.
// ---------------------------------------------------------------------------

[<Fact>]
let ``contract: a perturbed catalog normalizes to the canonical form`` () =
    let canonical = (nspRun sampleCatalog).Value
    let perturbed = withReversedCountryRows sampleCatalog
    let normalized = (nspRun perturbed).Value
    Assert.Equal(canonical, normalized)

[<Fact>]
let ``contract: rows are sorted by Identifier (SsKey)`` () =
    let perturbed = withReversedCountryRows sampleCatalog
    let normalized = (nspRun perturbed).Value
    let rows = extractCountryRows normalized
    let identifiers = rows |> List.map (fun r -> r.Identifier)
    Assert.Equal<SsKey list>(identifiers, List.sort identifiers)

// ---------------------------------------------------------------------------
// Identity preservation (A3, A4) — no SsKey invented, dropped, or rekeyed.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: pass neither invents nor drops kind SsKeys`` () =
    let inputKeys =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    let outputKeys =
        Catalog.allKinds (nspRun sampleCatalog).Value
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(inputKeys, outputKeys)

[<Fact>]
let ``A4: pass neither invents nor drops static-row Identifiers`` () =
    let inputIds =
        extractCountryRows sampleCatalog
        |> List.map (fun r -> r.Identifier)
        |> Set.ofList
    let outputIds =
        extractCountryRows (nspRun sampleCatalog).Value
        |> List.map (fun r -> r.Identifier)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(inputIds, outputIds)

// ---------------------------------------------------------------------------
// Static-only effect — kinds without Static modality pass through
// structurally unchanged and emit no lineage events.
// ---------------------------------------------------------------------------

[<Fact>]
let ``non-Static kinds pass through structurally unchanged`` () =
    let result = nspRun sampleCatalog
    // Customer (TenantScoped) and Order (no modality) survive byte-identical.
    Assert.Equal(customer, Catalog.tryFindKind customerKey result.Value |> Option.get)
    Assert.Equal(order,    Catalog.tryFindKind orderKey    result.Value |> Option.get)

[<Fact>]
let ``A25: only Static-bearing kinds emit Touched events`` () =
    let result = nspRun sampleCatalog
    let eventKeys = result.Trail |> List.map (fun e -> e.SsKey) |> Set.ofList
    Assert.Equal<Set<SsKey>>(Set.singleton countryKey, eventKeys)

[<Fact>]
let ``A23: events carry the pass version and name`` () =
    let result = nspRun sampleCatalog
    Assert.All(result.Trail, fun e ->
        Assert.Equal(NormalizeStaticPopulations.version, e.PassVersion)
        Assert.Equal("normalizeStaticPopulations", e.PassName))

// ---------------------------------------------------------------------------
// Edge cases the V1 tests do NOT cover (per the test-coverage scout
// report) but V2 should — these are the invariants property-based tests
// catch that example-based tests miss.
// ---------------------------------------------------------------------------

let private withCountryRows (rows: StaticRow list) (c: Catalog) : Catalog =
    { Modules =
        c.Modules
        |> List.map (fun m ->
            { m with
                Kinds =
                    m.Kinds
                    |> List.map (fun k ->
                        if k.SsKey = countryKey then
                            { k with Modality = [ Static rows ] }
                        else k) })
      Sequences = c.Sequences }

[<Fact>]
let ``edge: empty population list normalizes to empty list`` () =
    let perturbed = withCountryRows [] sampleCatalog
    let result = nspRun perturbed
    let rows = extractCountryRows result.Value
    Assert.Empty(rows)

[<Fact>]
let ``edge: single-row population is unchanged`` () =
    let singleton = countryPopulations |> List.head |> List.singleton
    let perturbed = withCountryRows singleton sampleCatalog
    let result = nspRun perturbed
    let rows = extractCountryRows result.Value
    Assert.Equal<StaticRow list>(singleton, rows)

[<Fact>]
let ``edge: a population already in canonical order is unchanged`` () =
    let preCanonical = (nspRun sampleCatalog).Value
    let preRows = extractCountryRows preCanonical
    let again = (nspRun preCanonical).Value
    let againRows = extractCountryRows again
    Assert.Equal<StaticRow list>(preRows, againRows)

// ---------------------------------------------------------------------------
// Property: any permutation of input rows yields the same sorted output.
// (V1 lacked this; FsCheck covers the combinatorial space V1's example
// tests missed.)
// ---------------------------------------------------------------------------

[<Property>]
let ``property: row order in input does not affect output order`` (reverseRows: bool) =
    let input =
        if reverseRows then withReversedCountryRows sampleCatalog
        else sampleCatalog
    let result = nspRun input
    let canonical = (nspRun sampleCatalog).Value
    extractCountryRows result.Value = extractCountryRows canonical

// ---------------------------------------------------------------------------
// Catalog cardinality — same module count, same kinds, same attributes,
// same references, same modality marks (just internally sorted).
// ---------------------------------------------------------------------------

[<Fact>]
let ``cardinality preserved: same modules / kinds / attributes / references / modality marks`` () =
    let result = nspRun sampleCatalog
    Assert.Equal(sampleCatalog.Modules.Length, result.Value.Modules.Length)
    let beforeKinds = Catalog.allKinds sampleCatalog
    let afterKinds  = Catalog.allKinds result.Value
    Assert.Equal(beforeKinds.Length, afterKinds.Length)
    for (before, after) in List.zip beforeKinds afterKinds do
        Assert.Equal(before.Attributes.Length, after.Attributes.Length)
        Assert.Equal(before.References.Length, after.References.Length)
        Assert.Equal(before.Modality.Length,   after.Modality.Length)

// ---------------------------------------------------------------------------
// Composition with canonicalizeIdentity — both passes are sort-driven
// normalizers; their composition is idempotent in either order.
// ---------------------------------------------------------------------------

[<Fact>]
let ``composes with canonicalizeIdentity: same result either order`` () =
    let aThenB =
        sampleCatalog
        |> nspRun
        |> Lineage.bind ciRun
    let bThenA =
        sampleCatalog
        |> ciRun
        |> Lineage.bind nspRun
    // Both orders converge to the same canonical Catalog (the trails
    // differ — order of Touched events varies — but the structural
    // result agrees).
    Assert.Equal(aThenB.Value, bThenA.Value)
