module Projection.Tests.ProfileStatisticsAdapterTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The fixture catalog: countryKey + countryCodeKey on
// physical (dbo, OSUSR_S1S_COUNTRY).CODE — the natural test target
// for categorical evidence (Country.Code is a small vocabulary).
// ---------------------------------------------------------------------------

let private validCategoricalJson = """
{
  "distributions": [
    {
      "Schema": "dbo",
      "Table": "OSUSR_S1S_COUNTRY",
      "Column": "CODE",
      "Kind": "Categorical",
      "DistinctCount": 3,
      "IsTruncated": false,
      "Frequencies": [
        { "Value": "CA", "Count": 1 },
        { "Value": "MX", "Count": 1 },
        { "Value": "US", "Count": 1 }
      ],
      "ProbeStatus": {
        "CapturedAtUtc": "2026-05-12T00:00:00Z",
        "SampleSize": 3,
        "Outcome": "Succeeded"
      }
    }
  ]
}
"""

// ---------------------------------------------------------------------------
// Happy path — JSON → Profile.Distributions, attribute resolved.
// ---------------------------------------------------------------------------

[<Fact>]
let ``attach: parses categorical distribution and resolves to AttributeKey`` () =
    let result =
        ProfileStatistics.attach sampleCatalog validCategoricalJson Profile.empty
    match result with
    | Failure errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Success profile ->
        match profile.Distributions with
        | [ AttributeDistribution.Categorical cat ] ->
            Assert.Equal(countryCodeKey, cat.AttributeKey)
            Assert.Equal(3L, cat.DistinctCount)
            Assert.False(cat.IsTruncated)
            Assert.Equal(3, cat.Frequencies.Length)
        | other ->
            Assert.Fail(sprintf "Expected one Categorical, got %A" other)

[<Fact>]
let ``attach: appends distributions rather than replacing`` () =
    // Pre-seed a different distribution; verify both end up present.
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch 1L Succeeded
        |> Result.value
    let preExistingCat =
        CategoricalDistribution.create customerNameKey [ "X", 1L ] 1L false probe
        |> Result.value
    let preExisting =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Categorical preExistingCat ] }
    let result =
        ProfileStatistics.attach sampleCatalog validCategoricalJson preExisting
    match result with
    | Failure errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Success profile ->
        Assert.Equal(2, profile.Distributions.Length)

[<Fact>]
let ``attach: frequencies are sorted alphabetically by value (determinism)`` () =
    // Submit out-of-order; expect alphabetical at the IR level.
    let unsortedJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "Kind": "Categorical",
          "DistinctCount": 3, "IsTruncated": false,
          "Frequencies": [
            { "Value": "US", "Count": 1 },
            { "Value": "CA", "Count": 1 },
            { "Value": "MX", "Count": 1 }
          ],
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-12T00:00:00Z", "SampleSize": 3, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog unsortedJson Profile.empty
    match result with
    | Failure errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Success profile ->
        match profile.Distributions with
        | [ AttributeDistribution.Categorical cat ] ->
            let values = cat.Frequencies |> List.map fst
            Assert.Equal<string list>([ "CA"; "MX"; "US" ], values)
        | _ -> Assert.Fail "Expected one Categorical distribution"

// ---------------------------------------------------------------------------
// Unresolvable coordinates — silently skipped (catalog is the contract).
// ---------------------------------------------------------------------------

[<Fact>]
let ``attach: silently skips coordinates absent from the catalog`` () =
    let unknownColumnJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_NONEXISTENT", "Column": "GHOST",
          "Kind": "Categorical",
          "DistinctCount": 0, "IsTruncated": false,
          "Frequencies": [],
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-12T00:00:00Z", "SampleSize": 0, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog unknownColumnJson Profile.empty
    match result with
    | Failure errs -> Assert.Fail(sprintf "Expected success (silent skip), got %A" errs)
    | Success profile ->
        Assert.Empty(profile.Distributions)

// ---------------------------------------------------------------------------
// JSON-shape errors.
// ---------------------------------------------------------------------------

[<Fact>]
let ``attach: rejects non-object root`` () =
    let result = ProfileStatistics.attach sampleCatalog "[]" Profile.empty
    match result with
    | Success _ -> Assert.Fail "Expected failure on array root"
    | Failure errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.json.shape")

[<Fact>]
let ``attach: rejects unparseable JSON`` () =
    let result = ProfileStatistics.attach sampleCatalog "{not json" Profile.empty
    match result with
    | Success _ -> Assert.Fail "Expected failure on bad JSON"
    | Failure errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.json.parse")

[<Fact>]
let ``attach: rejects unknown distribution Kind`` () =
    let unknownKindJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "Kind": "Numeric",
          "DistinctCount": 0, "IsTruncated": false,
          "Frequencies": [],
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-12T00:00:00Z", "SampleSize": 0, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog unknownKindJson Profile.empty
    match result with
    | Success _ -> Assert.Fail "Expected failure on unknown Kind"
    | Failure errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.distribution.kind.unknown")

[<Fact>]
let ``attach: rejects probe outcome the V2 DU doesn't support`` () =
    let badOutcomeJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "Kind": "Categorical",
          "DistinctCount": 0, "IsTruncated": false,
          "Frequencies": [],
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-12T00:00:00Z", "SampleSize": 0, "Outcome": "Mystery"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog badOutcomeJson Profile.empty
    match result with
    | Success _ -> Assert.Fail "Expected failure on unknown ProbeOutcome"
    | Failure errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.probeOutcome.unknown")

// ---------------------------------------------------------------------------
// Missing-array behavior — empty distributions handled like other adapters.
// ---------------------------------------------------------------------------

[<Fact>]
let ``attach: missing distributions array is treated as empty`` () =
    let result = ProfileStatistics.attach sampleCatalog "{}" Profile.empty
    match result with
    | Failure errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Success profile ->
        Assert.Empty(profile.Distributions)

// ---------------------------------------------------------------------------
// Composability with ProfileSnapshot.attach — both adapters can populate
// the same Profile in any order.
// ---------------------------------------------------------------------------

[<Fact>]
let ``attach composes with ProfileSnapshot.attach: distribution + snapshot evidence coexist`` () =
    let snapshotJson = """
    {
      "columns": [
        {
          "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "IsNullablePhysical": false, "IsComputed": false,
          "IsPrimaryKey": false, "IsUniqueKey": true,
          "DefaultDefinition": null,
          "RowCount": 3, "NullCount": 0,
          "NullCountStatus": {
            "CapturedAtUtc": "2026-05-12T00:00:00Z", "SampleSize": 3, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let r =
        ProfileSnapshot.attach sampleCatalog snapshotJson
        |> Result.bind (ProfileStatistics.attach sampleCatalog validCategoricalJson)
    match r with
    | Failure errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Success profile ->
        Assert.Equal(1, profile.Columns.Length)
        Assert.Equal(1, profile.Distributions.Length)
