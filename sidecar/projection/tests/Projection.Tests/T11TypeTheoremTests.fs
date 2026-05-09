module Projection.Tests.T11TypeTheoremTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Targets.Json
open Projection.Targets.Distributions
open Projection.Tests.Fixtures
open Projection.Tests.ProfileFixtures

// ---------------------------------------------------------------------------
// T11 — sibling-Π commutativity, encoded as a structural type theorem.
//
// The discipline retired by these tests: the substring
// `Assert.Contains(SsKey.rootOriginal k.SsKey, output)` enforcement at
// `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280-289`.
// That discipline tested a downstream consequence (every catalog kind's
// SsKey root *appears in the rendered text*) by string-search; the
// assertion held but the *coverage property* the assertion was a proxy
// for held only by convention — emitters could in principle stop
// surfacing a kind without breaking the substring test if rendering
// happened to mention the SsKey root for unrelated reasons.
//
// Per chapter 3.5 slice α/β/γ: every Π returns
// `Result<ArtifactByKind<'element>, EmitError>`, and `ArtifactByKind`'s
// smart constructor enforces strict equality between the slice's keyset
// and `Catalog.allKinds`'s SsKey set. T11 is now a *structural*
// consequence of construction:
//
//   - Any two `ArtifactByKind` values built from the same Catalog have
//     equal keysets by construction.
//   - The substring discipline becomes redundant.
//
// These tests demonstrate the structural path executes (the smart
// constructor accepts the slice and returns `Ok`) and confirm the
// keyset is what `Catalog.allKinds` advertises. The *theorem* is
// type-encoded; these are the worked examples.
//
// Companion: `ArtifactByKindTests.fs` covers the smart constructor's
// rejection of missing / extra keys; together those + these worked
// examples close the T11 verification surface.
// ---------------------------------------------------------------------------

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

let private expectedKeyset (c: Catalog) : Set<SsKey> =
    Catalog.allKinds c |> List.map (fun k -> k.SsKey) |> Set.ofList

[<Fact>]
let ``T11 (type theorem): RawTextEmitter.emitSlices key-set equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected = expectedKeyset enriched
    match RawTextEmitter.emitSlices enriched with
    | Result.Ok artifact ->
        Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)
    | Result.Error err ->
        Assert.Fail(sprintf "RawTextEmitter.emitSlices returned %A" err)

[<Fact>]
let ``T11 (type theorem): JsonEmitter.emitSlices key-set equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected = expectedKeyset enriched
    match JsonEmitter.emitSlices enriched with
    | Result.Ok artifact ->
        Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)
    | Result.Error err ->
        Assert.Fail(sprintf "JsonEmitter.emitSlices returned %A" err)

[<Fact>]
let ``T11 (type theorem): DistributionsEmitter.emitSlices key-set equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected = expectedKeyset enriched
    match DistributionsEmitter.emitSlices enriched sampleProfile with
    | Result.Ok artifact ->
        Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)
    | Result.Error err ->
        Assert.Fail(sprintf "DistributionsEmitter.emitSlices returned %A" err)

// ---------------------------------------------------------------------------
// T11 sibling commutativity — three Π's converge on the same keyset
// because the ArtifactByKind smart constructor binds each to the
// Catalog's keyset by construction. This is the property the substring
// discipline approximated.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11 (sibling commutativity): RawText, Json, Distributions key-sets are pairwise equal`` () =
    let enriched = enrich sampleCatalog
    let rawTextKeys =
        match RawTextEmitter.emitSlices enriched with
        | Result.Ok a -> ArtifactByKind.keys a
        | Result.Error err -> Assert.Fail(sprintf "RawText: %A" err); Set.empty
    let jsonKeys =
        match JsonEmitter.emitSlices enriched with
        | Result.Ok a -> ArtifactByKind.keys a
        | Result.Error err -> Assert.Fail(sprintf "Json: %A" err); Set.empty
    let distKeys =
        match DistributionsEmitter.emitSlices enriched sampleProfile with
        | Result.Ok a -> ArtifactByKind.keys a
        | Result.Error err -> Assert.Fail(sprintf "Distributions: %A" err); Set.empty
    Assert.Equal<Set<SsKey>>(rawTextKeys, jsonKeys)
    Assert.Equal<Set<SsKey>>(jsonKeys, distKeys)
