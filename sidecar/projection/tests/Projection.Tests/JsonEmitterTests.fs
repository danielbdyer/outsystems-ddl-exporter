module Projection.Tests.JsonEmitterTests

open System.Text.RegularExpressions
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.Json
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

// ---------------------------------------------------------------------------
// T1: determinism. Repeat invocations on byte-identical input produce
// byte-identical output.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: JSON emit is byte-identical across repeat invocations`` () =
    let enriched = enrich sampleCatalog
    let outputs = [ for _ in 1 .. 20 -> JsonEmitter.emit enriched ]
    let head = List.head outputs
    Assert.All(outputs, fun s -> Assert.Equal(head, s))

// ---------------------------------------------------------------------------
// Structural well-formedness. The hand-rolled JSON should round-trip
// through System.Text.Json without surprises.
// ---------------------------------------------------------------------------

[<Fact>]
let ``output is valid JSON parseable by System.Text.Json`` () =
    let enriched = enrich sampleCatalog
    let output = JsonEmitter.emit enriched
    // If parsing throws, the test fails with a useful diagnostic.
    use _doc = System.Text.Json.JsonDocument.Parse output
    Assert.True(true)

[<Fact>]
let ``parsed JSON has the expected top-level shape`` () =
    let enriched = enrich sampleCatalog
    let output = JsonEmitter.emit enriched
    use doc = System.Text.Json.JsonDocument.Parse output
    let root = doc.RootElement
    Assert.Equal("Projection.Targets.Json", root.GetProperty("emitter").GetString())
    Assert.Equal(JsonEmitter.version, root.GetProperty("version").GetInt32())
    Assert.Equal(enriched.Modules.Length, root.GetProperty("modules").GetArrayLength())

[<Fact>]
let ``parsed JSON contains every kind keyed by SsKey`` () =
    let enriched = enrich sampleCatalog
    let output = JsonEmitter.emit enriched
    use doc = System.Text.Json.JsonDocument.Parse output
    let root = doc.RootElement
    let modules = root.GetProperty("modules")
    let kindCount =
        seq { for m in modules.EnumerateArray() do
                let kinds = m.GetProperty("kinds")
                yield kinds.GetArrayLength() }
        |> Seq.sum
    Assert.Equal((Catalog.allKinds enriched).Length, kindCount)

// ---------------------------------------------------------------------------
// T4 / T11 — Sibling functor commutativity. Same enriched catalog ->
// SSDT and JSON outputs agree on every SsKey root that appears. Identity
// correspondence is preserved across both surfaces.
// ---------------------------------------------------------------------------

let private extractSsKeyRoots (catalog: Catalog) : Set<string> =
    let roots = ResizeArray<string>()
    for k in Catalog.allKinds catalog do
        roots.Add(SsKey.rootOriginal k.SsKey)
        for a in k.Attributes do
            roots.Add(SsKey.rootOriginal a.SsKey)
        for r in k.References do
            roots.Add(SsKey.rootOriginal r.SsKey)
    for m in catalog.Modules do
        roots.Add(SsKey.rootOriginal m.SsKey)
    Set.ofSeq roots

let private occursIn (text: string) (root: string) : bool =
    text.Contains(root)

[<Fact>]
let ``T4: every catalog SsKey root appears in JSON output`` () =
    // Pre-RawTextEmitter-retirement (chapter 4.1.A close arc): this
    // test also asserted the SsKey roots appear in SSDT output via the
    // RawTextEmitter's `Provenance` trailing comments. SsdtDdlEmitter
    // (the production schema emitter, ScriptDom-rendered) does not
    // emit those comments — SsKey roots are V2-IR-internal identifiers
    // with no SSDT-DDL surface. The structural T11 keyset property
    // (every kind in every Π's keyset) lives at
    // `SiblingEmitterContractTests.fs`; this test now narrows to JSON's
    // self-describing surface, where SsKey roots ARE structural.
    let enriched = enrich sampleCatalog
    let json = JsonEmitter.emit enriched
    for root in extractSsKeyRoots enriched do
        Assert.True(occursIn json root, sprintf "JSON output missing SsKey root: %s" root)

// `T11: sibling Pi's surface the same kind set when running on the same
// enriched catalog` retired in chapter 3.5 slice δ — the substring
// `Assert.Contains` discipline is now structural by virtue of the
// `Emitter<'element>` port and `ArtifactByKind`'s strict-equality
// smart constructor. Replaced by the contract property tests at
// `SiblingEmitterContractTests.fs` (renamed from `T11TypeTheoremTests.fs`
// at chapter 3.7 slice ε per the pillar-8 domain-first naming
// codification).

[<Fact>]
let ``T11: sibling Pi's agree on physical realization for every kind`` () =
    let enriched = enrich sampleCatalog
    let ssdt = SsdtDdlEmitter.statements enriched |> Render.toText
    let json = JsonEmitter.emit enriched
    for k in Catalog.allKinds enriched do
        // Physical schema and table appear in both surfaces, though
        // formatted differently (SSDT uses [schema].[table]; JSON uses
        // structured fields). Substring-existence is enough to prove
        // correspondence at this layer.
        Assert.Contains(k.Physical.Schema, ssdt)
        Assert.Contains(k.Physical.Schema, json)
        Assert.Contains(k.Physical.Table, ssdt)
        Assert.Contains(k.Physical.Table, json)

// ---------------------------------------------------------------------------
// JSON-specific structural checks: nested arrays/objects render correctly,
// strings are escaped, modality is rendered as an array.
// ---------------------------------------------------------------------------

[<Fact>]
let ``modality is rendered as a JSON array`` () =
    let enriched = enrich sampleCatalog
    let output = JsonEmitter.emit enriched
    // Customer has TenantScoped; Country has Static(3).
    Assert.Matches(Regex(@"""modality"":\s*\[\s*""TenantScoped""\s*\]"), output)
    Assert.Matches(Regex(@"""modality"":\s*\[\s*""Static\(3\)""\s*\]"), output)

[<Fact>]
let ``empty modality renders as an empty array (not null)`` () =
    let enriched = enrich sampleCatalog
    let output = JsonEmitter.emit enriched
    // Order has no modality marks.
    Assert.Matches(Regex(@"""name"":\s*""Order"".*?""modality"":\s*\[\]", RegexOptions.Singleline), output)

[<Fact>]
let ``nullable=false on every fixture attribute`` () =
    let enriched = enrich sampleCatalog
    let output = JsonEmitter.emit enriched
    use doc = System.Text.Json.JsonDocument.Parse output
    for m in doc.RootElement.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                Assert.False(a.GetProperty("nullable").GetBoolean())

// ---------------------------------------------------------------------------
// String escaping (rare in synthetic fixture but required by RFC 8259).
// ---------------------------------------------------------------------------

[<Fact>]
let ``string escaping handles double quotes and backslashes`` () =
    // Build a tiny catalog with a name containing characters that must
    // be escaped. The emitter must produce parseable JSON.
    let troubleName = Name.create "with\"quote\\backslash" |> Result.value
    let troubleKind : Kind = {
        SsKey = kindKey ["Trouble"]
        Name  = troubleName
        Origin = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "T"; Catalog = None }
        Attributes = []
        References = []
        Indexes    = []; Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }
    let troubleModule : Module = {
        SsKey = modKey "Trouble"
        Name  = Name.create "M" |> Result.value
        Kinds = [ troubleKind ]
        IsActive = true
        ExtendedProperties = []
        }
    let trouble : Catalog = { Modules = [ troubleModule ]; Sequences = [] }
    let output = JsonEmitter.emit trouble
    use _doc = System.Text.Json.JsonDocument.Parse output
    Assert.True(true)

// ---------------------------------------------------------------------------
// A18: signature-level invariant — Π takes no policy parameter.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A18: JsonEmitter.emit takes no policy parameter`` () =
    let _ : Catalog -> string = JsonEmitter.emit
    Assert.True(true)
