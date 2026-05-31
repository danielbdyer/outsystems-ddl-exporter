module Projection.Tests.AppliedTransformsTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// §5.5 — `ManifestEmitter.appliedTransforms` derives the per-artifact overlay
// enumeration from a composed pipeline's lineage trail. Each LineageEvent
// carries an SsKey + a Classification (DataIntent | OperatorIntent of
// OverlayAxis). These tests pin the derivation semantics + the manifest wiring.

let private ev (ssKey: SsKey) (classification: Classification) : LineageEvent =
    { PassName       = "test"
      PassVersion    = 1
      SsKey          = ssKey
      TransformKind  = Touched
      Classification = classification }

// ---------------------------------------------------------------------------
// Derivation semantics
// ---------------------------------------------------------------------------

[<Fact>]
let ``appliedTransforms: a DataIntent-only trail yields one None row per artifact (skeleton-purity witness)`` () =
    let trail = [ ev customerKey DataIntent; ev orderKey DataIntent ]
    let result = ManifestEmitter.appliedTransforms trail
    Assert.Equal<(SsKey * OverlayAxis option) list>(
        [ customerKey, None; orderKey, None ] |> List.sort,
        result)
    // Skeleton-purity: zero Some-axis rows.
    Assert.DoesNotContain(result, fun (_, axisOpt) -> Option.isSome axisOpt)

[<Fact>]
let ``appliedTransforms: an OperatorIntent overlay yields a Some-axis row (overlay-exercise witness)`` () =
    let trail = [ ev customerKey (OperatorIntent Tightening) ]
    Assert.Equal<(SsKey * OverlayAxis option) list>(
        [ customerKey, Some Tightening ],
        ManifestEmitter.appliedTransforms trail)

[<Fact>]
let ``appliedTransforms: DataIntent collapses to nothing when an overlay also touched the artifact`` () =
    // customerKey is touched by both a skeleton (DataIntent) pass AND a
    // Tightening overlay — only the Some-axis row survives (a None row would
    // mislead: the artifact DID receive an overlay).
    let trail = [ ev customerKey DataIntent; ev customerKey (OperatorIntent Tightening) ]
    Assert.Equal<(SsKey * OverlayAxis option) list>(
        [ customerKey, Some Tightening ],
        ManifestEmitter.appliedTransforms trail)

[<Fact>]
let ``appliedTransforms: multiple distinct axes on one artifact produce one row each, axis-sorted`` () =
    // Emission (tag 1) sorts before Tightening (tag 3) under OverlayAxis DU order.
    let trail = [ ev customerKey (OperatorIntent Tightening); ev customerKey (OperatorIntent Emission) ]
    Assert.Equal<(SsKey * OverlayAxis option) list>(
        [ customerKey, Some Emission; customerKey, Some Tightening ],
        ManifestEmitter.appliedTransforms trail)

[<Fact>]
let ``appliedTransforms: duplicate same-axis events dedupe to one row`` () =
    let trail = [ ev customerKey (OperatorIntent Tightening); ev customerKey (OperatorIntent Tightening) ]
    Assert.Equal<(SsKey * OverlayAxis option) list>(
        [ customerKey, Some Tightening ],
        ManifestEmitter.appliedTransforms trail)

[<Fact>]
let ``appliedTransforms: output is sorted by (SsKey, OverlayAxis option) regardless of trail order (T1)`` () =
    let forward =
        [ ev customerKey (OperatorIntent Tightening)
          ev orderKey DataIntent
          ev countryKey (OperatorIntent Emission) ]
    let shuffled =
        [ ev countryKey (OperatorIntent Emission)
          ev customerKey (OperatorIntent Tightening)
          ev orderKey DataIntent ]
    let a = ManifestEmitter.appliedTransforms forward
    let b = ManifestEmitter.appliedTransforms shuffled
    Assert.Equal<(SsKey * OverlayAxis option) list>(a, b)
    Assert.Equal<(SsKey * OverlayAxis option) list>(List.sort a, a)

// ---------------------------------------------------------------------------
// Manifest wiring + serialization
// ---------------------------------------------------------------------------

let private sampleTrail =
    [ ev customerKey (OperatorIntent Tightening)
      ev orderKey DataIntent ]

[<Fact>]
let ``buildFull threads the trail into Manifest.AppliedTransforms`` () =
    let manifest =
        ManifestEmitter.buildFull Profile.empty [] None None [] sampleTrail sampleCatalog
    Assert.Equal<(SsKey * OverlayAxis option) list>(
        ManifestEmitter.appliedTransforms sampleTrail,
        manifest.AppliedTransforms)

[<Fact>]
let ``build and buildWith leave AppliedTransforms empty (no pipeline trail)`` () =
    Assert.Empty((ManifestEmitter.build sampleCatalog).AppliedTransforms)
    Assert.Empty((ManifestEmitter.buildWith Profile.empty [] sampleCatalog).AppliedTransforms)

[<Fact>]
let ``toJson renders appliedTransforms with overlay names and JSON null; byte-deterministic (T1)`` () =
    let manifest =
        ManifestEmitter.buildFull Profile.empty [] None None [] sampleTrail sampleCatalog
    let json1 = ManifestEmitter.toJson manifest
    let json2 = ManifestEmitter.toJson manifest
    Assert.Equal<string>(json1, json2)
    Assert.Contains("appliedTransforms", json1)
    Assert.Contains("Tightening", json1)
    // The DataIntent-only artifact (orderKey) renders its overlay as JSON null.
    Assert.Contains("\"overlay\": null", json1)
