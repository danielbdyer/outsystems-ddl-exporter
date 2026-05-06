module Projection.Tests.DistributionsEmitterTests

open System
open System.Text.Json
open Xunit
open Projection.Core
open Projection.Targets.Distributions
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers — synthesize a profile carrying categorical evidence on the
// Country.Code attribute (the natural target in the synthetic catalog).
// ---------------------------------------------------------------------------

let private succeededProbe (sample: int64) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch sample Succeeded
    |> Result.value

let private countryCodeCategorical : CategoricalDistribution =
    CategoricalDistribution.create
        countryCodeKey
        [ "CA", 1L; "MX", 1L; "US", 1L ]
        3L
        false
        (succeededProbe 3L)
    |> Result.value

let private profileWithCountryCode : Profile =
    { Profile.empty with
        Distributions = [ AttributeDistribution.Categorical countryCodeCategorical ] }

// ---------------------------------------------------------------------------
// T1 determinism — repeat invocations on the same enriched IR produce
// byte-identical output. The same discipline applied to the existing
// emitters (SSDT, JSON).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: emit is byte-identical across repeat invocations`` () =
    let outputs =
        [ for _ in 1 .. 20 ->
            DistributionsEmitter.emit sampleCatalog profileWithCountryCode ]
    let head = List.head outputs
    Assert.All(outputs, fun s -> Assert.Equal(head, s))

// ---------------------------------------------------------------------------
// Structural well-formedness — output is valid JSON.
// ---------------------------------------------------------------------------

[<Fact>]
let ``output is valid JSON parseable by System.Text.Json`` () =
    let output = DistributionsEmitter.emit sampleCatalog profileWithCountryCode
    use _doc = JsonDocument.Parse(output)
    Assert.True(true)

[<Fact>]
let ``parsed JSON has the expected top-level shape`` () =
    let output = DistributionsEmitter.emit sampleCatalog profileWithCountryCode
    use doc = JsonDocument.Parse(output)
    let root = doc.RootElement
    Assert.Equal("Projection.Targets.Distributions", root.GetProperty("emitter").GetString())
    Assert.Equal(DistributionsEmitter.version, root.GetProperty("version").GetInt32())
    Assert.Equal(sampleCatalog.Modules.Length, root.GetProperty("modules").GetArrayLength())

// ---------------------------------------------------------------------------
// Profile evidence flows through to the output — Country.Code carries
// categorical, all other attributes carry null.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Country.Code attribute renders the categorical distribution`` () =
    let output = DistributionsEmitter.emit sampleCatalog profileWithCountryCode
    use doc = JsonDocument.Parse(output)
    let root = doc.RootElement
    let mutable found = false
    for m in root.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                let ssKey = a.GetProperty("ssKey").GetString()
                if ssKey = SsKey.rootOriginal countryCodeKey then
                    found <- true
                    let dist = a.GetProperty("distribution")
                    Assert.Equal(JsonValueKind.Object, dist.ValueKind)
                    Assert.Equal("Categorical", dist.GetProperty("kind").GetString())
                    Assert.Equal(3L, dist.GetProperty("distinctCount").GetInt64())
                    Assert.False(dist.GetProperty("isTruncated").GetBoolean())
                    Assert.Equal(3, dist.GetProperty("frequencies").GetArrayLength())
    Assert.True(found, "Country.Code attribute not found in output")

[<Fact>]
let ``attributes without distribution evidence render as null`` () =
    let output = DistributionsEmitter.emit sampleCatalog profileWithCountryCode
    use doc = JsonDocument.Parse(output)
    let root = doc.RootElement
    let countryCodeRoot = SsKey.rootOriginal countryCodeKey
    let mutable inspected = 0
    for m in root.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                let ssKey = a.GetProperty("ssKey").GetString()
                if ssKey <> countryCodeRoot then
                    let dist = a.GetProperty("distribution")
                    Assert.Equal(JsonValueKind.Null, dist.ValueKind)
                    inspected <- inspected + 1
    Assert.True(inspected > 0, "No null-distribution attributes inspected")

// ---------------------------------------------------------------------------
// Frequencies render in alphabetical order (the IR's determinism
// commitment flows through to the output).
// ---------------------------------------------------------------------------

[<Fact>]
let ``frequencies render in alphabetical-by-value order`` () =
    let output = DistributionsEmitter.emit sampleCatalog profileWithCountryCode
    use doc = JsonDocument.Parse(output)
    let root = doc.RootElement
    let countryCodeRoot = SsKey.rootOriginal countryCodeKey
    for m in root.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                if a.GetProperty("ssKey").GetString() = countryCodeRoot then
                    let freqs = a.GetProperty("distribution").GetProperty("frequencies")
                    let values =
                        [ for f in freqs.EnumerateArray() ->
                            f.GetProperty("value").GetString() ]
                    Assert.Equal<string list>([ "CA"; "MX"; "US" ], values)

// ---------------------------------------------------------------------------
// Empty-profile case — every attribute carries null distribution.
// ---------------------------------------------------------------------------

[<Fact>]
let ``empty profile yields all-null distributions but full catalog structure`` () =
    let output = DistributionsEmitter.emit sampleCatalog Profile.empty
    use doc = JsonDocument.Parse(output)
    let root = doc.RootElement
    let kindCount =
        seq { for m in root.GetProperty("modules").EnumerateArray() do
                yield m.GetProperty("kinds").GetArrayLength() }
        |> Seq.sum
    Assert.Equal((Catalog.allKinds sampleCatalog).Length, kindCount)
    // Every distribution is null on Profile.empty.
    for m in root.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                Assert.Equal(JsonValueKind.Null, a.GetProperty("distribution").ValueKind)

// ---------------------------------------------------------------------------
// T11 sibling-Π commutativity — Distributions emits the same SsKey
// roots that JSON / SSDT do (every kind appears in every Π).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11: every catalog kind's SsKey root appears in the Distributions output`` () =
    let output = DistributionsEmitter.emit sampleCatalog Profile.empty
    for k in Catalog.allKinds sampleCatalog do
        let root = SsKey.rootOriginal k.SsKey
        Assert.Contains(root, output)

// ---------------------------------------------------------------------------
// emitFromInput convenience.
// ---------------------------------------------------------------------------

[<Fact>]
let ``emitFromInput produces the same output as emit`` () =
    let input =
        { Catalog = sampleCatalog
          Policy  = Policy.empty
          Profile = profileWithCountryCode }
    let viaInput = DistributionsEmitter.emitFromInput input
    let direct   = DistributionsEmitter.emit sampleCatalog profileWithCountryCode
    Assert.Equal(direct, viaInput)

// ---------------------------------------------------------------------------
// A18 amended — the Distributions emitter takes (Catalog, Profile),
// no Policy. Signature-level invariant.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A18 amended: DistributionsEmitter.emit takes no policy parameter`` () =
    let _ : Catalog -> Profile -> string = DistributionsEmitter.emit
    Assert.True(true)

// ---------------------------------------------------------------------------
// Cross-platform newline determinism — output must use \n only,
// regardless of host OS line-ending convention. The same discipline
// JsonEmitter applied (DECISIONS 2026-05-06).
// ---------------------------------------------------------------------------

[<Fact>]
let ``output uses LF line endings (no CR)`` () =
    let output = DistributionsEmitter.emit sampleCatalog profileWithCountryCode
    Assert.DoesNotContain("\r", output)
