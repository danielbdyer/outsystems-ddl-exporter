module Projection.Tests.RichProfilingEndToEndTests

open System.Text.Json
open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.Distributions
open Projection.Targets.Json
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Session 9 milestone — end-to-end rich-profiling validation.
//
// Validates the full enriched-IR pipeline:
//
//   V1 JSON (profile-snapshot)             V2-only JSON (distributions)
//        │                                       │
//        ▼                                       ▼
//   ProfileSnapshot.attach           ProfileStatistics.attach
//        │                                       │
//        └────────────► Profile ◄────────────────┘
//                           │
//   Catalog ────────────────┴─────────────────► DistributionsEmitter.emit
//                                                       │
//                                                       ▼
//                                              JSON distribution report
//
// If this test passes, the rich-profiling vector has been empirically
// validated end-to-end — V1-derived evidence and V2-only evidence
// coexist in a single Profile, and a sibling Π consumes the
// enriched IR without losing either. The first capability admire
// without a V1 component (ADMIRE.md 2026-05-12) is operational.
// ---------------------------------------------------------------------------

let private mkKey (s: string) : SsKey = SsKey.original s |> Result.value
let private mkName (s: string) : Name = Name.create s |> Result.value

// ---------------------------------------------------------------------------
// V2 catalog — Parent + Child + Country, with the Country.NAME column the
// natural target for a categorical distribution (small vocabulary).
// ---------------------------------------------------------------------------

let private parentKindKey       = mkKey "OS_KIND_R9_Parent"
let private parentIdKey         = mkKey "OS_ATTR_R9_Parent_Id"
let private childKindKey        = mkKey "OS_KIND_R9_Child"
let private childIdKey          = mkKey "OS_ATTR_R9_Child_Id"
let private childParentFkKey    = mkKey "OS_ATTR_R9_Child_ParentId"
let private childToParentRefKey = mkKey "OS_REF_R9_Child_Parent"
let private countryKindKey      = mkKey "OS_KIND_R9_Country"
let private countryIdKey        = mkKey "OS_ATTR_R9_Country_Id"
let private countryNameKey      = mkKey "OS_ATTR_R9_Country_Name"

let private parent : Kind =
    { SsKey    = parentKindKey
      Name     = mkName "Parent"
      Origin   = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = "OSUSR_R9_PARENT" }
      Attributes = [
          { SsKey        = parentIdKey
            Name         = mkName "Id"
            Type         = Integer
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false } ]
      References = []; Indexes = [] }

let private child : Kind =
    { SsKey    = childKindKey
      Name     = mkName "Child"
      Origin   = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = "OSUSR_R9_CHILD" }
      Attributes = [
          { SsKey        = childIdKey
            Name         = mkName "Id"
            Type         = Integer
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false }
          { SsKey        = childParentFkKey
            Name         = mkName "ParentId"
            Type         = Integer
            Column       = { ColumnName = "PARENTID"; IsNullable = true }
            IsPrimaryKey = false; IsMandatory = false } ]
      References = [
          { SsKey           = childToParentRefKey
            Name            = mkName "Parent"
            SourceAttribute = childParentFkKey
            TargetKind      = parentKindKey
            OnDelete        = NoAction } ]
      Indexes = [] }

let private country : Kind =
    { SsKey    = countryKindKey
      Name     = mkName "Country"
      Origin   = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = "OSUSR_R9_COUNTRY" }
      Attributes = [
          { SsKey        = countryIdKey
            Name         = mkName "Id"
            Type         = Integer
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false }
          { SsKey        = countryNameKey
            Name         = mkName "Name"
            Type         = Text
            Column       = { ColumnName = "NAME"; IsNullable = false }
            IsPrimaryKey = false; IsMandatory = false } ]
      References = []; Indexes = [] }

let private endToEndCatalog : Catalog =
    { Modules = [
        { SsKey = mkKey "OS_MOD_R9"
          Name  = mkName "RichProfiling"
          Kinds = [ parent; child; country ] } ] }

// ---------------------------------------------------------------------------
// V1-shaped profile snapshot — null/duplicate/orphan evidence only,
// matching V1's ProfileSnapshot.cs shape exactly.
// ---------------------------------------------------------------------------

let private snapshotJson = """
{
  "columns": [
    { "Schema": "dbo", "Table": "OSUSR_R9_PARENT", "Column": "ID",
      "IsNullablePhysical": false, "IsComputed": false,
      "IsPrimaryKey": true, "IsUniqueKey": false, "DefaultDefinition": null,
      "RowCount": 500, "NullCount": 0,
      "NullCountStatus": { "CapturedAtUtc": "2026-05-12T00:00:00Z",
                           "SampleSize": 500, "Outcome": "Succeeded" } },
    { "Schema": "dbo", "Table": "OSUSR_R9_COUNTRY", "Column": "NAME",
      "IsNullablePhysical": false, "IsComputed": false,
      "IsPrimaryKey": false, "IsUniqueKey": false, "DefaultDefinition": null,
      "RowCount": 12, "NullCount": 0,
      "NullCountStatus": { "CapturedAtUtc": "2026-05-12T00:00:00Z",
                           "SampleSize": 12, "Outcome": "Succeeded" } }
  ],
  "uniqueCandidates": [],
  "compositeUniqueCandidates": [],
  "fkReality": []
}
"""

// ---------------------------------------------------------------------------
// V2-only distribution evidence — categorical value frequencies on
// Country.NAME. No V1 source; the JSON is V2-shaped.
// ---------------------------------------------------------------------------

let private distributionsJson = """
{
  "distributions": [
    { "Schema": "dbo", "Table": "OSUSR_R9_COUNTRY", "Column": "NAME",
      "Kind": "Categorical",
      "DistinctCount": 4,
      "IsTruncated": false,
      "Frequencies": [
        { "Value": "Canada", "Count": 3 },
        { "Value": "Mexico", "Count": 2 },
        { "Value": "Spain",  "Count": 4 },
        { "Value": "United States", "Count": 3 }
      ],
      "ProbeStatus": { "CapturedAtUtc": "2026-05-12T00:00:00Z",
                       "SampleSize": 12, "Outcome": "Succeeded" } }
  ]
}
"""

// ---------------------------------------------------------------------------
// The composed enrichment pipeline.
// ---------------------------------------------------------------------------

let private enrichedProfile () : Profile =
    ProfileSnapshot.attach endToEndCatalog snapshotJson
    |> Result.bind (ProfileStatistics.attach endToEndCatalog distributionsJson)
    |> Result.value

// ---------------------------------------------------------------------------
// MILESTONE: V1 evidence + V2-only evidence coexist in a single Profile.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MILESTONE: V1 column profiles and V2 distributions coexist in the enriched profile`` () =
    let profile = enrichedProfile ()
    Assert.Equal(2, profile.Columns.Length)
    Assert.Equal(1, profile.Distributions.Length)
    // The categorical distribution resolves to Country.NAME.
    let cat = Profile.tryFindCategorical countryNameKey profile
    Assert.True(cat.IsSome, "Expected categorical distribution on Country.NAME")
    Assert.Equal(4L, cat.Value.DistinctCount)

[<Fact>]
let ``MILESTONE: layering distributions does not lose V1 evidence`` () =
    let snapshotOnly =
        ProfileSnapshot.attach endToEndCatalog snapshotJson
        |> Result.value
    let layered =
        ProfileStatistics.attach endToEndCatalog distributionsJson snapshotOnly
        |> Result.value
    Assert.Equal(snapshotOnly.Columns.Length,          layered.Columns.Length)
    Assert.Equal(snapshotOnly.UniqueCandidates.Length, layered.UniqueCandidates.Length)
    Assert.Equal(snapshotOnly.ForeignKeys.Length,      layered.ForeignKeys.Length)
    // V2-only addition is the Distributions field.
    Assert.NotEmpty(layered.Distributions)
    Assert.Empty(snapshotOnly.Distributions)

[<Fact>]
let ``MILESTONE: adapter composition order does not matter`` () =
    // Run the adapters in both orders; assert the resulting profiles
    // have the same content (modulo distributions-list ordering, which
    // is empty in one path so the equivalence is exact here).
    let aThenB =
        ProfileSnapshot.attach endToEndCatalog snapshotJson
        |> Result.bind (ProfileStatistics.attach endToEndCatalog distributionsJson)
        |> Result.value
    // Reverse-order: distributions first onto Profile.empty, then
    // re-add snapshot data manually since ProfileStatistics doesn't
    // produce columns / unique / fk fields.
    let bThenA =
        ProfileStatistics.attach endToEndCatalog distributionsJson Profile.empty
        |> Result.value
    let bThenAEnriched =
        ProfileSnapshot.attach endToEndCatalog snapshotJson
        |> Result.value
    // Combine bThenA's distributions with bThenAEnriched's other fields
    // — the resulting Profile must equal aThenB's.
    let combined =
        { bThenAEnriched with Distributions = bThenA.Distributions }
    Assert.Equal<Profile>(aThenB, combined)

// ---------------------------------------------------------------------------
// MILESTONE: the Distributions sibling Π consumes the enriched IR and
// emits a structurally complete report.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MILESTONE: DistributionsEmitter emits the categorical evidence end-to-end`` () =
    let profile = enrichedProfile ()
    let output = DistributionsEmitter.emit endToEndCatalog profile
    use doc = JsonDocument.Parse(output)
    let root = doc.RootElement

    // Find Country.NAME and verify the distribution payload made it
    // through the entire stack.
    let countryNameRoot = SsKey.rootOriginal countryNameKey
    let mutable found = false
    for m in root.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                if a.GetProperty("ssKey").GetString() = countryNameRoot then
                    found <- true
                    let dist = a.GetProperty("distribution")
                    Assert.Equal(JsonValueKind.Object, dist.ValueKind)
                    Assert.Equal("Categorical", dist.GetProperty("kind").GetString())
                    Assert.Equal(4L, dist.GetProperty("distinctCount").GetInt64())
                    let freqs = dist.GetProperty("frequencies")
                    Assert.Equal(4, freqs.GetArrayLength())
    Assert.True(found, "Country.NAME not found in distributions output")

// ---------------------------------------------------------------------------
// T1 — end-to-end determinism. Same JSON inputs produce byte-identical
// emitter output across repeats.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: end-to-end pipeline is byte-deterministic`` () =
    let runOnce () =
        let profile = enrichedProfile ()
        DistributionsEmitter.emit endToEndCatalog profile
    let outputs = [ for _ in 1 .. 10 -> runOnce () ]
    let head = List.head outputs
    Assert.All(outputs, fun s -> Assert.Equal(head, s))

// ---------------------------------------------------------------------------
// T11 sibling-Π commutativity extended to three Π — every catalog kind's
// SsKey root appears in the SSDT, JSON, and Distributions outputs.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11 extended: every catalog kind's SsKey root appears in all three sibling Pi outputs`` () =
    let profile = enrichedProfile ()
    let ssdt   = RawTextEmitter.emit endToEndCatalog
    let json   = JsonEmitter.emit endToEndCatalog
    let distrs = DistributionsEmitter.emit endToEndCatalog profile
    for k in Catalog.allKinds endToEndCatalog do
        let root = SsKey.rootOriginal k.SsKey
        Assert.Contains(root, ssdt)
        Assert.Contains(root, json)
        Assert.Contains(root, distrs)

// ---------------------------------------------------------------------------
// Structural commitment — empty distributions JSON does not change the
// catalog metadata in the Distributions output (every kind still appears,
// every attribute still has a distribution field set to null).
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural commitment: empty distributions yields all-null distribution fields, full catalog structure preserved`` () =
    let snapshotOnly =
        ProfileSnapshot.attach endToEndCatalog snapshotJson
        |> Result.value
    let output = DistributionsEmitter.emit endToEndCatalog snapshotOnly
    use doc = JsonDocument.Parse(output)
    let root = doc.RootElement
    // Total catalog kinds preserved.
    let kindCount =
        seq { for m in root.GetProperty("modules").EnumerateArray() do
                yield m.GetProperty("kinds").GetArrayLength() }
        |> Seq.sum
    Assert.Equal((Catalog.allKinds endToEndCatalog).Length, kindCount)
    // Every distribution field is null.
    for m in root.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                Assert.Equal(JsonValueKind.Null, a.GetProperty("distribution").ValueKind)

// ---------------------------------------------------------------------------
// V2-only evidence is unreachable from V1's adapter — the V1 adapter
// leaves Distributions = []; the V2 adapter is the only population path.
// ---------------------------------------------------------------------------

[<Fact>]
let ``invariant: V1 ProfileSnapshot.attach does not populate Distributions`` () =
    let snapshotOnly =
        ProfileSnapshot.attach endToEndCatalog snapshotJson
        |> Result.value
    Assert.Empty(snapshotOnly.Distributions)
