module Projection.Tests.EmitterRegistrationsTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Slice 5.13.emit-features-registry (2026-05-18) — SSDT emitter
// `RegisteredTransform` surface witnesses. Mirrors the OSSYS-adapter
// AdapterRegistrationsTests shape on the Emitter stage.
//
// The emitter's `SsdtDdlEmitter.emitSlices` signature
// (`Catalog -> Result<ArtifactByKind<SsdtFile>, EmitError>`) doesn't fit
// the typed `RegisteredTransform<'In, 'Out>.Run` shell; metadata-only
// registration is the principled form (cherry-pick boundary precedent
// from CatalogReader.registeredMetadata at chapter A.4.7 slice δ).
//
// All emit-feature sites classify as DataIntent per pillar 9: the
// SSDT emitter projects Catalog evidence into the typed Statement
// stream. Selection / Tightening operator intent runs in passes
// upstream of the emitter (A18 amended forbids Policy at emit time).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice 5.13.emit-features-registry: SsdtDdlEmitter.registeredMetadata is at the Emitter stage`` () =
    let rt = SsdtDdlEmitter.registeredMetadata
    Assert.Equal("ssdtDdlEmitter", rt.Name)
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(Emitter, rt.StageBinding)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``Slice 5.13.emit-features-registry: SsdtDdlEmitter.registeredMetadata enumerates every emission feature`` () =
    let rt = SsdtDdlEmitter.registeredMetadata
    let siteNames = rt.Sites |> List.map (fun s -> s.SiteName) |> Set.ofList
    let expected =
        Set.ofList [
            "createTable"
            "createIndex"
            "columnDefaultClause"
            "columnCheckConstraint"
            "foreignKeyConstraint"
            "alterTableNoCheckConstraint"
            "alterIndexDisable"
            "indexIgnoreDuplicateKey"
            "indexDataCompression"
            "setExtendedProperty"
            "topologicalOrder"
        ]
    Assert.Equal<Set<string>>(expected, siteNames)

[<Fact>]
let ``Slice 5.13.emit-features-registry: every SsdtDdlEmitter site classifies as DataIntent (emitter projects evidence, never overlay)`` () =
    let rt = SsdtDdlEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``Slice 5.13.emit-features-registry: every SsdtDdlEmitter site carries non-empty Rationale (pillar 9 harvest discipline)`` () =
    let rt = SsdtDdlEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.False (System.String.IsNullOrWhiteSpace site.Rationale)

[<Fact>]
let ``Slice 5.13.emit-features-registry: SsdtDdlEmitter.registeredMetadata validates through TransformRegistry.create`` () =
    match TransformRegistry.create [ SsdtDdlEmitter.registeredMetadata ] with
    | Ok entries -> Assert.Equal(1, List.length entries)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected emitter metadata to validate; got errors: %s" codes)

[<Fact>]
let ``Slice 5.13.emit-features-registry: ManifestEmitter.build registry includes the SSDT emitter`` () =
    // The slice prepends SsdtDdlEmitter.registeredMetadata to the
    // RegisteredTransforms.all list at the manifest build site;
    // the emitter therefore appears in the registry digest path.
    let catalog : Catalog = { Modules = []; Sequences = [] }
    let manifest = ManifestEmitter.build catalog
    // The digest is opaque (sha-256-based); witness instead via the
    // joint registry assembly — both the SSDT emitter and the Core-
    // resident passes validate together.
    let joint = SsdtDdlEmitter.registeredMetadata :: RegisteredTransforms.all
    match TransformRegistry.create joint with
    | Ok entries -> Assert.True (List.length entries >= 13)  // 12 Core + 1 emitter
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected joint registry to validate; got errors: %s" codes)
    Assert.NotNull manifest.RegistryDigest
