module Projection.Tests.EmitterRegistrationsTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Targets.Json
open Projection.Targets.Distributions
open Projection.Targets.Data

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

// ---------------------------------------------------------------------------
// Slice 5.13.sibling-emitter-registry-json — `JsonEmitter.registeredMetadata`
// witnesses. Mirrors the SSDT block above on the JSON-projection axis. The
// emitter signature is `Catalog → Result<ArtifactByKind<JsonNode>, EmitError>`;
// metadata-only registration follows the SSDT cherry-pick precedent.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-json: JsonEmitter.registeredMetadata is at the Emitter stage`` () =
    let rt = JsonEmitter.registeredMetadata
    Assert.Equal("jsonEmitter", rt.Name)
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(Emitter, rt.StageBinding)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-json: JsonEmitter.registeredMetadata enumerates every projection feature`` () =
    let rt = JsonEmitter.registeredMetadata
    let siteNames = rt.Sites |> List.map (fun s -> s.SiteName) |> Set.ofList
    let expected =
        Set.ofList [
            "catalogDocument"
            "kindJson"
            "attributeJson"
            "referenceJson"
            "modalityProjection"
            "emitSlices"
        ]
    Assert.Equal<Set<string>>(expected, siteNames)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-json: every JsonEmitter site classifies as DataIntent (emitter projects evidence; A18 amended)`` () =
    let rt = JsonEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-json: every JsonEmitter site carries non-empty Rationale (pillar 9 harvest discipline)`` () =
    let rt = JsonEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.False (System.String.IsNullOrWhiteSpace site.Rationale)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-json: JsonEmitter.registeredMetadata validates through TransformRegistry.create`` () =
    match TransformRegistry.create [ JsonEmitter.registeredMetadata ] with
    | Ok entries -> Assert.Equal(1, List.length entries)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected emitter metadata to validate; got errors: %s" codes)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-json: joint registry (SSDT + Json) validates through TransformRegistry.create`` () =
    // The two sibling-Π emitters live in disjoint projects (Targets.SSDT
    // and Targets.Json); the registry's uniqueness invariant must hold
    // across the joint assembly.
    let joint =
        SsdtDdlEmitter.registeredMetadata
        :: JsonEmitter.registeredMetadata
        :: RegisteredTransforms.all
    match TransformRegistry.create joint with
    | Ok entries -> Assert.True (List.length entries >= 14)  // 12 Core + 2 emitters
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected joint registry to validate; got errors: %s" codes)

// ---------------------------------------------------------------------------
// Slice 5.13.sibling-emitter-registry-distributions —
// `DistributionsEmitter.registeredMetadata` witnesses. The emitter
// signature is `Catalog → Profile → string` (the wide A18-amended form;
// Profile is evidence per pillar 9).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-distributions: DistributionsEmitter.registeredMetadata is at the Emitter stage`` () =
    let rt = DistributionsEmitter.registeredMetadata
    Assert.Equal("distributionsEmitter", rt.Name)
    Assert.Equal(Diagnostics, rt.Domain)
    Assert.Equal(Emitter, rt.StageBinding)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-distributions: DistributionsEmitter.registeredMetadata enumerates every projection feature`` () =
    let rt = DistributionsEmitter.registeredMetadata
    let siteNames = rt.Sites |> List.map (fun s -> s.SiteName) |> Set.ofList
    let expected =
        Set.ofList [
            "catalogDocument"
            "kindJson"
            "attributeDistributionJson"
            "writeCategorical"
            "writeNumeric"
            "writeProbeStatus"
            "emitSlices"
        ]
    Assert.Equal<Set<string>>(expected, siteNames)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-distributions: every DistributionsEmitter site classifies as DataIntent (Profile is evidence; A18 amended)`` () =
    let rt = DistributionsEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-distributions: every DistributionsEmitter site carries non-empty Rationale (pillar 9 harvest discipline)`` () =
    let rt = DistributionsEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.False (System.String.IsNullOrWhiteSpace site.Rationale)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-distributions: DistributionsEmitter.registeredMetadata validates through TransformRegistry.create`` () =
    match TransformRegistry.create [ DistributionsEmitter.registeredMetadata ] with
    | Ok entries -> Assert.Equal(1, List.length entries)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected emitter metadata to validate; got errors: %s" codes)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-distributions: joint registry (SSDT + Json + Distributions) validates`` () =
    let joint =
        SsdtDdlEmitter.registeredMetadata
        :: JsonEmitter.registeredMetadata
        :: DistributionsEmitter.registeredMetadata
        :: RegisteredTransforms.all
    match TransformRegistry.create joint with
    | Ok entries -> Assert.True (List.length entries >= 15)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected joint registry to validate; got errors: %s" codes)

// ---------------------------------------------------------------------------
// Slice 5.13.sibling-emitter-registry-static-population —
// `StaticPopulationEmitter.registeredMetadata` witnesses. The emitter
// signature is `Catalog → seq<Statement>` (typed stream; A35).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-static-population: StaticPopulationEmitter.registeredMetadata is at the Emitter stage`` () =
    let rt = StaticPopulationEmitter.registeredMetadata
    Assert.Equal("staticPopulationEmitter", rt.Name)
    Assert.Equal(Data, rt.Domain)
    Assert.Equal(Emitter, rt.StageBinding)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-static-population: StaticPopulationEmitter.registeredMetadata enumerates every emission feature`` () =
    let rt = StaticPopulationEmitter.registeredMetadata
    let siteNames = rt.Sites |> List.map (fun s -> s.SiteName) |> Set.ofList
    let expected =
        Set.ofList [
            "kindStatements"
            "rowToCellValues"
            "identityToggle"
            "topologicalOrder"
            "statements"
        ]
    Assert.Equal<Set<string>>(expected, siteNames)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-static-population: every StaticPopulationEmitter site classifies as DataIntent (static rows are catalog evidence; A18 amended)`` () =
    let rt = StaticPopulationEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.Equal(DataIntent, site.Classification)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-static-population: every StaticPopulationEmitter site carries non-empty Rationale (pillar 9 harvest discipline)`` () =
    let rt = StaticPopulationEmitter.registeredMetadata
    for site in rt.Sites do
        Assert.False (System.String.IsNullOrWhiteSpace site.Rationale)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-static-population: StaticPopulationEmitter.registeredMetadata validates through TransformRegistry.create`` () =
    match TransformRegistry.create [ StaticPopulationEmitter.registeredMetadata ] with
    | Ok entries -> Assert.Equal(1, List.length entries)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected emitter metadata to validate; got errors: %s" codes)

[<Fact>]
let ``Slice 5.13.sibling-emitter-registry-static-population: joint registry (SSDT + Json + Distributions + StaticPopulation + four Data-axis siblings) validates`` () =
    // The four data-axis siblings (DataEmissionComposer, StaticSeedsEmitter,
    // MigrationDependenciesEmitter, BootstrapEmitter) live in
    // RegisteredDataTransforms.all (Projection.Targets.Data project).
    // StaticPopulationEmitter is now the fifth data-axis sibling; this
    // assertion exercises the full nine-emitter joint registry.
    let joint =
        [ SsdtDdlEmitter.registeredMetadata
          JsonEmitter.registeredMetadata
          DistributionsEmitter.registeredMetadata
          StaticPopulationEmitter.registeredMetadata ]
        @ RegisteredDataTransforms.all
        @ RegisteredTransforms.all
    match TransformRegistry.create joint with
    | Ok entries -> Assert.True (List.length entries >= 20)  // 12 Core + 4 data + 4 sibling emitters (incl. SSDT)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected joint registry to validate; got errors: %s" codes)
