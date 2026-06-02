module Projection.Tests.RichProfilingEndToEndTests

open System.Text.Json
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Targets.Distributions
open Projection.Targets.Json
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `ForeignKeyPass.run` is private; the
// canonical surface is `.registered.Run`. Shape-compatible — both
// return `Lineage<Diagnostics<ForeignKeyDecisionSet>>`; this shim is
// a pure rename so existing assertions keep reading.
let private fkRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<ForeignKeyDecisionSet>> =
    (ForeignKeyPass.registered policy profile).Run catalog

// Chapter A.4.7' slice η — `UniqueIndexPass.run` is private; the
// canonical surface is `.registered.Run`. Shape-compatible — both
// return `Lineage<Diagnostics<UniqueIndexDecisionSet>>`; this shim is
// a pure rename so existing assertions keep reading.
let private uiRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<UniqueIndexDecisionSet>> =
    (UniqueIndexPass.registered policy profile).Run catalog

// Chapter A.4.7' slice η — `NullabilityPass.run` is private; the
// canonical surface is `.registered.Run`. Shape-compatible — both
// return `Lineage<Diagnostics<NullabilityDecisionSet>>`; this shim is
// a pure rename so existing assertions keep reading.
let private nullRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<NullabilityDecisionSet>> =
    (NullabilityPass.registered policy profile).Run catalog

// Chapter A.4.7' slice η — `CategoricalUniquenessPass.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<CategoricalUniquenessDecisionSet>>`. This per-file
// shim restores the `Lineage<CategoricalUniquenessDecisionSet>` shape so
// existing assertions keep reading.
let private cuRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<CategoricalUniquenessDecisionSet> =
    (CategoricalUniquenessPass.registered policy profile).Run catalog |> Lineage.map (fun d -> d.Value)

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

let private mkKey (s: string) : SsKey = testKey s
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
      Origin   = Native
      Modality = []
      Physical = mkTableId "dbo" "OSUSR_R9_PARENT"
      Attributes = [
          { Attribute.create parentIdKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true } ]
      References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

let private child : Kind =
    { SsKey    = childKindKey
      Name     = mkName "Child"
      Origin   = Native
      Modality = []
      Physical = mkTableId "dbo" "OSUSR_R9_CHILD"
      Attributes = [
          { Attribute.create childIdKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true }
          { Attribute.create childParentFkKey (mkName "ParentId") Integer with Column = ColumnRealization.create ("PARENTID") (true) |> Result.value } ]
      References = [
          Reference.create childToParentRefKey (mkName "Parent") childParentFkKey parentKindKey ]
      Indexes = []
      Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

let private country : Kind =
    { SsKey    = countryKindKey
      Name     = mkName "Country"
      Origin   = Native
      Modality = []
      Physical = mkTableId "dbo" "OSUSR_R9_COUNTRY"
      Attributes = [
          { Attribute.create countryIdKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true }
          { Attribute.create countryNameKey (mkName "Name") Text with Column = ColumnRealization.create ("NAME") (false) |> Result.value } ]
      References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

let private endToEndCatalog : Catalog =
    { Modules = [
        { SsKey = mkKey "OS_MOD_R9"
          Name  = mkName "RichProfiling"
          Kinds = [ parent; child; country ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

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
    ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
    |> Result.bind (ProfileStatistics.attach endToEndCatalog (ProfileStatistics.DistributionsJson distributionsJson))
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
        ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
        |> Result.value
    let layered =
        ProfileStatistics.attach endToEndCatalog (ProfileStatistics.DistributionsJson distributionsJson) snapshotOnly
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
        ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
        |> Result.bind (ProfileStatistics.attach endToEndCatalog (ProfileStatistics.DistributionsJson distributionsJson))
        |> Result.value
    // Reverse-order: distributions first onto Profile.empty, then
    // re-add snapshot data manually since ProfileStatistics doesn't
    // produce columns / unique / fk fields.
    let bThenA =
        ProfileStatistics.attach endToEndCatalog (ProfileStatistics.DistributionsJson distributionsJson) Profile.empty
        |> Result.value
    let bThenAEnriched =
        ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
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
// T11 sibling-Π commutativity — substring discipline retired in
// chapter 3.5 slice δ. Per the `Emitter<'element>` port realization,
// each Π returns `Result<ArtifactByKind<'element>, EmitError>`, and
// `ArtifactByKind`'s smart constructor enforces strict-equality between
// the slice's keyset and `Catalog.allKinds`'s SsKey set. T11 is now a
// structural type theorem, exercised at `SiblingEmitterContractTests.fs`
// (renamed from `T11TypeTheoremTests.fs` at chapter 3.7 slice ε per the
// pillar-8 domain-first naming codification). The `endToEndCatalog`-
// flavoured worked example survives implicitly via the contract tests'
// coverage of `sampleCatalog`; the cross-emitter keyset agreement is
// in `SiblingEmitterContractTests.``T11 (sibling commutativity):
// RawText, Json, Distributions key-sets are pairwise equal```.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Structural commitment — empty distributions JSON does not change the
// catalog metadata in the Distributions output (every kind still appears,
// every attribute still has a distribution field set to null).
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural commitment: empty distributions yields all-null distribution fields, full catalog structure preserved`` () =
    let snapshotOnly =
        ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
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
        ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
        |> Result.value
    Assert.Empty(snapshotOnly.Distributions)

// ---------------------------------------------------------------------------
// Session 10 milestone — both distribution variants flowing through the
// end-to-end pipeline.
//
// V1 JSON (snapshot)         V2-only JSON (Categorical + Numeric)
//      |                                |
// ProfileSnapshot.attach    ProfileStatistics.attach
//      |                                |
//      +---------> Profile <------------+
//                     |
// Catalog ------------+--> DistributionsEmitter.emit
//
// If this passes: the closed-DU expansion held under the second
// variant; both variants coexist; the emitter renders both
// correctly; sibling commutativity preserves across all three Pi
// (SSDT, JSON, Distributions) on the now-larger Profile shape.
// ---------------------------------------------------------------------------

let private mixedDistributionsJson = """
{
  "distributions": [
    { "Schema": "dbo", "Table": "OSUSR_R9_COUNTRY", "Column": "NAME",
      "Kind": "Categorical",
      "DistinctCount": 4, "IsTruncated": false,
      "Frequencies": [
        { "Value": "Canada", "Count": 3 },
        { "Value": "Mexico", "Count": 2 },
        { "Value": "Spain",  "Count": 4 },
        { "Value": "United States", "Count": 3 }
      ],
      "ProbeStatus": { "CapturedAtUtc": "2026-05-12T00:00:00Z",
                       "SampleSize": 12, "Outcome": "Succeeded" } },
    { "Schema": "dbo", "Table": "OSUSR_R9_PARENT", "Column": "ID",
      "Kind": "Numeric",
      "Min": 1, "P25": 125, "P50": 250, "P75": 375, "P95": 475, "P99": 495, "Max": 500,
      "SampleSize": 500,
      "ProbeStatus": { "CapturedAtUtc": "2026-05-13T00:00:00Z",
                       "SampleSize": 500, "Outcome": "Succeeded" } }
  ]
}
"""

let private enrichedProfileBothVariants () : Profile =
    ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
    |> Result.bind (ProfileStatistics.attach endToEndCatalog (ProfileStatistics.DistributionsJson mixedDistributionsJson))
    |> Result.value

[<Fact>]
let ``MILESTONE 10: Categorical + Numeric coexist in a single enriched profile`` () =
    let profile = enrichedProfileBothVariants ()
    Assert.Equal(2, profile.Distributions.Length)
    // Per-attribute lookups by variant succeed where evidence is
    // registered, return None elsewhere.
    Assert.True(Profile.tryFindCategorical countryNameKey profile |> Option.isSome)
    Assert.True(Profile.tryFindNumeric parentIdKey profile |> Option.isSome)
    // Cross-variant lookups return None (the discipline holds).
    Assert.True(Profile.tryFindNumeric countryNameKey profile |> Option.isNone)
    Assert.True(Profile.tryFindCategorical parentIdKey profile |> Option.isNone)

[<Fact>]
let ``MILESTONE 10: V1 evidence is preserved when both variants are layered`` () =
    let profile = enrichedProfileBothVariants ()
    // V1-derived fields unchanged from session 9's milestone.
    Assert.Equal(2, profile.Columns.Length)
    Assert.Empty(profile.UniqueCandidates)
    Assert.Empty(profile.ForeignKeys)

[<Fact>]
let ``MILESTONE 10: DistributionsEmitter renders both variants end-to-end`` () =
    let profile = enrichedProfileBothVariants ()
    let output = DistributionsEmitter.emit endToEndCatalog profile
    use doc = JsonDocument.Parse output
    let root = doc.RootElement
    let countryNameRoot = SsKey.rootOriginal countryNameKey
    let parentIdRoot    = SsKey.rootOriginal parentIdKey
    let mutable foundCat = false
    let mutable foundNum = false
    for m in root.GetProperty("modules").EnumerateArray() do
        for k in m.GetProperty("kinds").EnumerateArray() do
            for a in k.GetProperty("attributes").EnumerateArray() do
                let ssKey = a.GetProperty("ssKey").GetString()
                if ssKey = countryNameRoot then
                    let dist = a.GetProperty("distribution")
                    Assert.Equal(JsonValueKind.Object, dist.ValueKind)
                    Assert.Equal("Categorical", dist.GetProperty("kind").GetString())
                    foundCat <- true
                if ssKey = parentIdRoot then
                    let dist = a.GetProperty("distribution")
                    Assert.Equal(JsonValueKind.Object, dist.ValueKind)
                    Assert.Equal("Numeric", dist.GetProperty("kind").GetString())
                    Assert.Equal(500m, dist.GetProperty("max").GetDecimal())
                    foundNum <- true
    Assert.True(foundCat, "Country.NAME categorical not rendered end-to-end")
    Assert.True(foundNum, "Parent.ID numeric not rendered end-to-end")

[<Fact>]
let ``MILESTONE 10: T1 byte-determinism holds across the now-larger Profile shape`` () =
    let runOnce () =
        let profile = enrichedProfileBothVariants ()
        DistributionsEmitter.emit endToEndCatalog profile
    let outputs = [ for _ in 1 .. 10 -> runOnce () ]
    Assert.All(outputs, fun s -> Assert.Equal(List.head outputs, s))

[<Fact>]
let ``MILESTONE 10: T11 sibling commutativity preserved across self-describing Pi (Json + Distributions)`` () =
    // Pre-RawTextEmitter-retirement: this test also checked SsKey
    // roots in the SSDT output via RawTextEmitter's `Provenance`
    // trailing comments. The production SsdtDdlEmitter (ScriptDom-
    // rendered) does not emit those comments — SsKey roots are V2-IR-
    // internal identifiers with no SSDT-DDL surface. The structural
    // T11 keyset property (every kind appears in every Π's keyset)
    // lives at `SiblingEmitterContractTests.fs` and is enforced by
    // `ArtifactByKind.create`'s smart constructor; here we narrow to
    // the self-describing surfaces (Json, Distributions) where SsKey
    // roots ARE structural.
    let profile = enrichedProfileBothVariants ()
    let json   = JsonEmitter.emit endToEndCatalog
    let distrs = DistributionsEmitter.emit endToEndCatalog profile
    for k in Catalog.allKinds endToEndCatalog do
        let root = SsKey.rootOriginal k.SsKey
        Assert.Contains(root, json)
        Assert.Contains(root, distrs)

[<Fact>]
let ``MILESTONE 10: monotonicity violation in fixture surfaces as adapter error`` () =
    // Verifies the structural-commitment-via-construction-validation
    // pattern's reach: a bad fixture (P50 > P75) does not silently
    // produce a degenerate Profile. The adapter rejects it; the
    // pipeline halts; the diagnostic carries the smart constructor's
    // error code.
    let badJson = """
    {
      "distributions": [
        { "Schema": "dbo", "Table": "OSUSR_R9_PARENT", "Column": "ID",
          "Kind": "Numeric",
          "Min": 1, "P25": 125, "P50": 400, "P75": 200, "P95": 475, "P99": 495, "Max": 500,
          "SampleSize": 500,
          "ProbeStatus": { "CapturedAtUtc": "2026-05-13T00:00:00Z",
                           "SampleSize": 500, "Outcome": "Succeeded" } }
      ]
    }
    """
    let result =
        ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
        |> Result.bind (ProfileStatistics.attach endToEndCatalog (ProfileStatistics.DistributionsJson badJson))
    match result with
    | Ok _ -> Assert.Fail "Expected failure on monotonicity violation"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.percentiles.nonMonotonic")

// ---------------------------------------------------------------------------
// Session 11 milestone — first distribution-aware strategy flowing through
// the end-to-end pipeline.
//
// V1 JSON (snapshot)         V2-only JSON (Categorical evidence)
//      |                                |
// ProfileSnapshot.attach    ProfileStatistics.attach
//      |                                |
//      +---------> Profile <------------+
//                     |
// Catalog -- Policy --+--> CategoricalUniquenessPass
//                                  |
//                                  v
//                       CategoricalUniquenessDecisionSet
//
// If this passes: the codification's third real test holds end-to-end.
// Distribution evidence drives a strategy decision through the full
// pipeline; the closed DU's fourth variant + the codified pattern
// + the fanOut primitive + the StrategyEvaluator alias all carry
// the load.
// ---------------------------------------------------------------------------

let private uniquenessProfileJson = """
{
  "distributions": [
    { "Schema": "dbo", "Table": "OSUSR_R9_COUNTRY", "Column": "NAME",
      "Kind": "Categorical",
      "DistinctCount": 4, "IsTruncated": false,
      "Frequencies": [
        { "Value": "Canada", "Count": 1 },
        { "Value": "Mexico", "Count": 1 },
        { "Value": "Spain",  "Count": 1 },
        { "Value": "United States", "Count": 1 }
      ],
      "ProbeStatus": { "CapturedAtUtc": "2026-05-13T00:00:00Z",
                       "SampleSize": 4, "Outcome": "Succeeded" } }
  ]
}
"""

let private profileForCategoricalUniqueness () : Profile =
    ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
    |> Result.bind (ProfileStatistics.attach endToEndCatalog (ProfileStatistics.DistributionsJson uniquenessProfileJson))
    |> Result.value

let private categoricalUniquenessConfig =
    CategoricalUniquenessConfig.create 2L |> Result.value

let private uniquenessPolicy =
    { Policy.empty with
        Tightening =
            { Interventions =
                [ CategoricalUniqueness ("v2-distrib", categoricalUniquenessConfig) ] } }

[<Fact>]
let ``MILESTONE 11: CategoricalUniquenessPass produces SuggestUnique end-to-end on rich-profiling fixture`` () =
    let profile = profileForCategoricalUniqueness ()
    let lineage =
        cuRun endToEndCatalog uniquenessPolicy profile
    // Country.NAME has 4 distinct values, all unique in the sample
    // ⇒ SuggestUnique(EveryValueDistinct (4, 4)).
    let countryNameDecision =
        lineage.Value.Decisions
        |> List.find (fun d -> d.AttributeKey = countryNameKey)
    Assert.Equal(
        CategoricalUniquenessOutcome.SuggestUnique
            (EveryValueDistinct (4L, 4L)),
        countryNameDecision.Outcome)

[<Fact>]
let ``MILESTONE 11: every attribute lacking Categorical evidence surfaces as DoNotSuggest(NoCategoricalEvidence)`` () =
    let profile = profileForCategoricalUniqueness ()
    let lineage =
        cuRun endToEndCatalog uniquenessPolicy profile
    // Every attribute except Country.NAME has no Categorical evidence
    // ⇒ DoNotSuggest(NoCategoricalEvidence).
    let withoutCountryName =
        lineage.Value.Decisions
        |> List.filter (fun d -> d.AttributeKey <> countryNameKey)
    Assert.All(withoutCountryName, fun d ->
        Assert.Equal(
            CategoricalUniquenessOutcome.DoNotSuggest
                CategoricalUniquenessKeepReason.NoCategoricalEvidence,
            d.Outcome))

[<Fact>]
let ``MILESTONE 11: lineage discipline carries through the fanOut primitive`` () =
    let profile = profileForCategoricalUniqueness ()
    let lineage =
        cuRun endToEndCatalog uniquenessPolicy profile
    // One Annotated event per decision; pass version + name carried.
    Assert.Equal(lineage.Value.Decisions.Length, lineage.Trail.Length)
    Assert.All(lineage.Trail, fun e ->
        Assert.Equal(CategoricalUniquenessPass.version, e.PassVersion)
        Assert.Equal("categoricalUniqueness", e.PassName)
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

[<Fact>]
let ``MILESTONE 11: five-strategy coexistence holds end-to-end on the rich-profiling fixture`` () =
    let profile = profileForCategoricalUniqueness ()
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let uniqCfg = UniqueIndexTighteningConfig.create true true
    let fkCfg =
        ForeignKeyTighteningConfig.create true true false false true
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ Nullability           ("null-1",  nullCfg)
                      UniqueIndex           ("uniq-1",  uniqCfg)
                      ForeignKey            ("fk-1",    fkCfg)
                      CategoricalUniqueness ("cu-1",    categoricalUniquenessConfig) ] } }
    // Each pass filters its own variant; produces decisions only
    // tagged with its registered intervention id.
    let nullLineage = nullRun endToEndCatalog policy profile
    let uniqLineage = uiRun endToEndCatalog policy profile
    let fkLineage   = fkRun   endToEndCatalog policy profile
    let cuLineage   = cuRun endToEndCatalog policy profile
    Assert.All((LineageDiagnostics.payload nullLineage).Decisions, fun d -> Assert.Equal("null-1", d.InterventionId))
    Assert.All((LineageDiagnostics.payload uniqLineage).Decisions, fun d -> Assert.Equal("uniq-1", d.InterventionId))
    Assert.All((LineageDiagnostics.payload fkLineage).Decisions,   fun d -> Assert.Equal("fk-1",   d.InterventionId))
    Assert.All(cuLineage.Value.Decisions,   fun d -> Assert.Equal("cu-1",   d.InterventionId))

[<Fact>]
let ``MILESTONE 11: T1 byte-determinism holds for the new strategy`` () =
    let profile = profileForCategoricalUniqueness ()
    let r1 = cuRun endToEndCatalog uniquenessPolicy profile
    let r2 = cuRun endToEndCatalog uniquenessPolicy profile
    Assert.Equal<CategoricalUniquenessDecisionSet>(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)
