module Projection.Tests.RegistryDigestRoundTripTests

open Xunit
open System.Text.Json.Nodes
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter A.4.7' slice ζ — the 5th bidirectional property test.
//
// Closes the bidirectional contract for L3-CC-Transform-Totality /
// A41 at chapter A.4.7' close. Four tests from chapter A.4.7 slice θ
// (skeleton-purity at filter-shape; overlay-exercise; totality
// coverage; harvest-classification cross-reference) plus this one
// (registry-digest round-trip) yield the 5/5 covered by the chapter
// open's exit gate.
//
// Property statement: emit canary, parse manifest, extract digest,
// re-emit; assert digest stable across emits. Perturb a Sites
// rationale, re-emit; assert digest changed. Stability covers the
// reproducibility half; perturbation covers the sensitivity half.
// ---------------------------------------------------------------------------

let private extractDigest (manifestJson: string) : string =
    let node = JsonNode.Parse(manifestJson)
    match node with
    | null -> failwith "expected manifest JSON to parse"
    | n ->
        let registry = n.["registry"]
        match registry with
        | null -> failwith "expected manifest to carry registry.digest field"
        | reg ->
            let digest = reg.["digest"]
            match digest with
            | null -> failwith "expected registry.digest to be populated"
            | d -> d.GetValue<string>()

// ---------------------------------------------------------------------------
// Reproducibility half: identical registry + Catalog → identical
// digest across emits.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A41 (digest round-trip): identical registry yields identical digest across two emits`` () =
    let manifest1 = ManifestEmitter.buildWith Profile.empty RegisteredTransforms.all sampleCatalog
    let manifest2 = ManifestEmitter.buildWith Profile.empty RegisteredTransforms.all sampleCatalog
    Assert.Equal(manifest1.RegistryDigest, manifest2.RegistryDigest)

[<Fact>]
let ``A41 (digest round-trip): digest survives JSON serialize → parse → extract`` () =
    let manifest = ManifestEmitter.buildWith Profile.empty RegisteredTransforms.all sampleCatalog
    let json = ManifestEmitter.toJson manifest
    let extracted = extractDigest json
    Assert.Equal(manifest.RegistryDigest, extracted)

// ---------------------------------------------------------------------------
// Sensitivity half: perturbing a Sites.Rationale changes the digest.
// ---------------------------------------------------------------------------

let private perturbFirstRationale
    (entries: RegisteredTransformMetadata list)
    : RegisteredTransformMetadata list =
    match entries with
    | [] -> entries
    | first :: rest ->
        let perturbedSites =
            match first.Sites with
            | [] -> first.Sites
            | site :: siteRest ->
                let bumped =
                    { site with
                        Rationale = System.String.Concat (site.Rationale, " [PERTURBED]") }
                bumped :: siteRest
        { first with Sites = perturbedSites } :: rest

[<Fact>]
let ``A41 (digest round-trip): perturbing a Sites.Rationale changes the digest`` () =
    let baseline = TransformRegistry.digest RegisteredTransforms.all
    let perturbed = TransformRegistry.digest (perturbFirstRationale RegisteredTransforms.all)
    Assert.NotEqual<string>(baseline, perturbed)

[<Fact>]
let ``A41 (digest round-trip): perturbed registry surfaces a different digest in the manifest JSON`` () =
    let baseline = ManifestEmitter.buildWith Profile.empty RegisteredTransforms.all sampleCatalog
    let perturbed =
        ManifestEmitter.buildWith
            Profile.empty
            (perturbFirstRationale RegisteredTransforms.all)
            sampleCatalog
    let baselineJson = ManifestEmitter.toJson baseline
    let perturbedJson = ManifestEmitter.toJson perturbed
    Assert.NotEqual<string>(extractDigest baselineJson, extractDigest perturbedJson)

// ---------------------------------------------------------------------------
// Permutation-invariance: digest sorted-by-Name → reorder input list →
// same digest. Per the chapter-open framing: reorderings do not
// change the digest; the SHA256 input is the sorted projection.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A41 (digest round-trip): digest is invariant under input-list reordering`` () =
    let asGiven = TransformRegistry.digest RegisteredTransforms.all
    let reversed = TransformRegistry.digest (List.rev RegisteredTransforms.all)
    Assert.Equal<string>(asGiven, reversed)

// ---------------------------------------------------------------------------
// NM-60 — tamper-evidence: a crafted free-text field can no longer forge a
// delimiter-injection collision. Two DISTINCT registries whose UNESCAPED
// concatenation would have been byte-identical (one site with a rationale that
// embeds the `}{siteName=...;rationale=...` structure of a SECOND site, vs the
// two genuine sites) now produce DIFFERENT digests, because every variable-
// length field is length-prefixed (injective encoding).
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-60: a delimiter-injection collision in a Sites.Rationale is defeated by length-prefixing`` () =
    // Registry A — ONE site whose Rationale embeds the exact delimiter structure
    // that would, under the OLD unescaped scheme, re-parse as a second site.
    let injected =
        "r}{siteName=s2;classification=DataIntent;rationale=r2"
    let registryA =
        [ RegisteredTransformMetadata.emitter "T" Domain.Schema
            [ TransformSite.dataIntent "s" injected ] ]
    // Registry B — TWO genuine sites carrying the same content split across the
    // real field boundaries. Under the OLD raw concatenation A and B serialized
    // to the IDENTICAL buffer
    //   ...sites=[{siteName=s;classification=DataIntent;rationale=r}
    //             {siteName=s2;classification=DataIntent;rationale=r2}]
    // — a forged collision. With length prefixes they are distinguishable.
    let registryB =
        [ RegisteredTransformMetadata.emitter "T" Domain.Schema
            [ TransformSite.dataIntent "s" "r"
              TransformSite.dataIntent "s2" "r2" ] ]
    let digestA = TransformRegistry.digest registryA
    let digestB = TransformRegistry.digest registryB
    Assert.NotEqual<string>(digestA, digestB)

[<Fact>]
let ``NM-60: shifting a delimiter-like substring across the Name boundary changes the digest`` () =
    // Two single-entry registries: one carries `|domain=` injected into its Name,
    // the other splits the same characters across the real Name/domain boundary.
    // Length-prefixing the Name makes the two non-colliding.
    let registryA =
        [ RegisteredTransformMetadata.emitter "a|domain=Schema" Domain.Schema
            [ TransformSite.dataIntent "s" "r" ] ]
    let registryB =
        [ RegisteredTransformMetadata.emitter "a" Domain.Schema
            [ TransformSite.dataIntent "s" "r" ] ]
    Assert.NotEqual<string>(TransformRegistry.digest registryA, TransformRegistry.digest registryB)
