module Projection.Tests.RawTextEmitterTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Π = Π_SSDT.RawTextEmitter.emit. The catalog flowing in must already be
// canonicalized (an E-pass invariant); these tests pre-run E so the
// emitter sees stable input.
// ---------------------------------------------------------------------------

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

// ---------------------------------------------------------------------------
// T1: determinism. Same enriched catalog → byte-identical output across
// repeated invocations. This is the core demonstration that the algebra
// holds end-to-end on the synthetic fixture.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: emit is byte-identical across repeated invocations`` () =
    let enriched = enrich sampleCatalog
    let outputs = [ for _ in 1 .. 20 -> RawTextEmitter.emit enriched ]
    let head = List.head outputs
    Assert.All(outputs, fun s -> Assert.Equal(head, s))

[<Fact>]
let ``T1: emit on canonicalize-then-perturb-then-canonicalize matches direct`` () =
    // Perturbing the catalog and re-canonicalizing must produce the same
    // output as canonicalizing the original directly. Demonstrates that
    // Project = Π ∘ E is order-insensitive on the input collections.
    let direct = RawTextEmitter.emit (enrich sampleCatalog)
    let perturbed =
        { sampleCatalog with Modules = List.rev sampleCatalog.Modules }
    let viaPerturbation = RawTextEmitter.emit (enrich perturbed)
    Assert.Equal(direct, viaPerturbation)

// ---------------------------------------------------------------------------
// Coverage: the output contains every kind, attribute, and reference's
// SsKey root (since SsKeys appear inline as comments). This is a coarse
// "no information lost" property; finer-grained correctness is the
// per-element rendering tests below.
// ---------------------------------------------------------------------------

[<Fact>]
let ``output contains every kind's SsKey root`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    for k in Catalog.allKinds enriched do
        Assert.Contains(SsKey.rootOriginal k.SsKey, output)

[<Fact>]
let ``output contains every attribute's column name`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    for k in Catalog.allKinds enriched do
        for a in k.Attributes do
            Assert.Contains(a.Column.ColumnName, output)

[<Fact>]
let ``output contains every reference's source attribute column`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    for k in Catalog.allKinds enriched do
        for r in k.References do
            let sourceColumn =
                k.Attributes
                |> List.find (fun a -> a.SsKey = r.SourceAttribute)
                |> fun a -> a.Column.ColumnName
            Assert.Contains(sourceColumn, output)

// ---------------------------------------------------------------------------
// CREATE TABLE + ALTER TABLE structural checks.
// ---------------------------------------------------------------------------

[<Fact>]
let ``output emits one CREATE TABLE per kind`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    let createTableCount =
        output.Split([| '\n' |])
        |> Array.filter (fun line -> line.StartsWith("CREATE TABLE "))
        |> Array.length
    Assert.Equal((Catalog.allKinds enriched).Length, createTableCount)

[<Fact>]
let ``output emits ALTER TABLE FK for the Order -> Customer reference`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    Assert.Contains("ALTER TABLE [dbo].[OSUSR_S1S_ORDER]", output)
    Assert.Contains("REFERENCES [dbo].[OSUSR_S1S_CUSTOMER]", output)

[<Fact>]
let ``output records the Static populations on Country`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    Assert.Contains("-- Static populations: 3 rows", output)
    Assert.Contains("Code=US", output)
    Assert.Contains("Code=CA", output)
    Assert.Contains("Code=MX", output)

[<Fact>]
let ``output records each kind's modality marks`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    // Customer is TenantScoped; Country is Static(3); Order has no marks.
    Assert.Contains("modality=[TenantScoped]", output)
    Assert.Contains("modality=[Static(3)]", output)

[<Fact>]
let ``output renders NOT NULL on every fixture attribute`` () =
    // The synthetic fixture has IsNullable=false on every attribute.
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    let attrCount =
        Catalog.allKinds enriched
        |> List.sumBy (fun k -> k.Attributes.Length)
    let notNullCount =
        output.Split([| '\n' |])
        |> Array.filter (fun line -> line.Contains("NOT NULL"))
        |> Array.length
    Assert.Equal(attrCount, notNullCount)

// ---------------------------------------------------------------------------
// Integration with visibility: emitting a masked catalog produces fewer
// CREATE TABLEs (one per surviving kind) and skips dangling references.
// ---------------------------------------------------------------------------

[<Fact>]
let ``emit on a visibility-masked catalog drops removed kinds`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ countryKey ] ] }
    let masked = (VisibilityMask.run mask sampleCatalog).Value
    let enriched = enrich masked
    let output = RawTextEmitter.emit enriched
    // Country is gone.
    Assert.DoesNotContain("OSUSR_S1S_COUNTRY", output)
    // Customer and Order remain.
    Assert.Contains("OSUSR_S1S_CUSTOMER", output)
    Assert.Contains("OSUSR_S1S_ORDER", output)

[<Fact>]
let ``emit warns when a reference points at a removed kind`` () =
    // Hide Customer; Order's reference now dangles. The emitter should
    // emit a warning comment rather than silently drop the FK.
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ customerKey ] ] }
    let masked = (VisibilityMask.run mask sampleCatalog).Value
    let enriched = enrich masked
    let output = RawTextEmitter.emit enriched
    Assert.Contains("WARNING: target kind not present in catalog", output)

// ---------------------------------------------------------------------------
// A18: Π is mechanical. There is no policy parameter to RawTextEmitter.emit.
// The signature itself is the test — if the emitter needed configuration,
// we'd have to add a parameter, and that parameter would belong in E.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A18: emit takes no policy parameter (signature-level invariant)`` () =
    // Compile-time test: emit's signature is Catalog -> string. Nothing
    // about policy enters Π. The test body is a no-op; the compiler is
    // the test.
    let _ : Catalog -> string = RawTextEmitter.emit
    Assert.True(true)

// ---------------------------------------------------------------------------
// Property: the output is non-empty and starts with the version banner.
// (Catches an emitter regression where the banner is dropped or moved.)
// ---------------------------------------------------------------------------

[<Property>]
let ``output starts with the version banner`` () =
    let enriched = enrich sampleCatalog
    let output = RawTextEmitter.emit enriched
    output.StartsWith("-- Generated by Projection.Targets.SSDT.RawTextEmitter v")
