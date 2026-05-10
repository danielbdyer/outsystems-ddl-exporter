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
        ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson validCategoricalJson) Profile.empty
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Ok profile ->
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
        ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson validCategoricalJson) preExisting
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Ok profile ->
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
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson unsortedJson) Profile.empty
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Ok profile ->
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
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson unknownColumnJson) Profile.empty
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected success (silent skip), got %A" errs)
    | Ok profile ->
        Assert.Empty(profile.Distributions)

// ---------------------------------------------------------------------------
// JSON-shape errors.
// ---------------------------------------------------------------------------

[<Fact>]
let ``attach: rejects non-object root`` () =
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson "[]") Profile.empty
    match result with
    | Ok _ -> Assert.Fail "Expected failure on array root"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.json.shape")

[<Fact>]
let ``attach: rejects unparseable JSON`` () =
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson "{not json") Profile.empty
    match result with
    | Ok _ -> Assert.Fail "Expected failure on bad JSON"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.json.parse")

[<Fact>]
let ``attach: rejects unknown distribution Kind`` () =
    // "Temporal" is the placeholder for an unknown Kind — legal Kinds
    // as of session 10 are "Categorical" and "Numeric"; Temporal
    // arrives in a future session.
    let unknownKindJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "Kind": "Temporal",
          "Min": 0, "Max": 0,
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-12T00:00:00Z", "SampleSize": 0, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson unknownKindJson) Profile.empty
    match result with
    | Ok _ -> Assert.Fail "Expected failure on unknown Kind"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.distribution.kind.unknown")

// ---------------------------------------------------------------------------
// Numeric distribution parsing — landed in session 10 commit 3.
// ---------------------------------------------------------------------------

let private validNumericJson = """
{
  "distributions": [
    {
      "Schema": "dbo",
      "Table": "OSUSR_S1S_COUNTRY",
      "Column": "CODE",
      "Kind": "Numeric",
      "Min": 0,
      "P25": 10,
      "P50": 25,
      "P75": 50,
      "P95": 90,
      "P99": 99,
      "Max": 100,
      "SampleSize": 100,
      "ProbeStatus": {
        "CapturedAtUtc": "2026-05-13T00:00:00Z",
        "SampleSize": 100,
        "Outcome": "Succeeded"
      }
    }
  ]
}
"""

[<Fact>]
let ``attach numeric: parses numeric distribution and resolves to AttributeKey`` () =
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson validNumericJson) Profile.empty
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Ok profile ->
        match profile.Distributions with
        | [ AttributeDistribution.Numeric num ] ->
            Assert.Equal(countryCodeKey, num.AttributeKey)
            Assert.Equal(0m,   num.Min)
            Assert.Equal(50m,  num.P75)
            Assert.Equal(100m, num.Max)
            Assert.Equal(100L, num.SampleSize)
        | other ->
            Assert.Fail(sprintf "Expected one Numeric, got %A" other)

[<Fact>]
let ``attach numeric: monotonicity violation surfaces as construction error`` () =
    // P50 (50) > P75 (30) — violates the monotonicity contract enforced
    // by NumericDistribution.create. The smart constructor's failure
    // propagates through the adapter via Result.bind.
    let nonMonotonicJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "Kind": "Numeric",
          "Min": 0, "P25": 10, "P50": 50, "P75": 30, "P95": 90, "P99": 99, "Max": 100,
          "SampleSize": 100,
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-13T00:00:00Z", "SampleSize": 100, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson nonMonotonicJson) Profile.empty
    match result with
    | Ok _ -> Assert.Fail "Expected failure on non-monotonic percentiles"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.percentiles.nonMonotonic")

[<Fact>]
let ``attach numeric: sample size below floor surfaces as construction error`` () =
    let belowFloorJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "Kind": "Numeric",
          "Min": 0, "P25": 1, "P50": 2, "P75": 3, "P95": 4, "P99": 5, "Max": 6,
          "SampleSize": 4,
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-13T00:00:00Z", "SampleSize": 4, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson belowFloorJson) Profile.empty
    match result with
    | Ok _ -> Assert.Fail "Expected failure on SampleSize < floor"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.sampleSize.belowFloor")

[<Fact>]
let ``attach numeric: silently skips coordinates absent from the catalog`` () =
    // Same catalog-is-the-contract discipline as Categorical and
    // ProfileSnapshot.attach.
    let unknownCoordJson = """
    {
      "distributions": [
        {
          "Schema": "dbo", "Table": "OSUSR_NONEXISTENT", "Column": "GHOST",
          "Kind": "Numeric",
          "Min": 0, "P25": 10, "P50": 20, "P75": 30, "P95": 40, "P99": 50, "Max": 60,
          "SampleSize": 100,
          "ProbeStatus": {
            "CapturedAtUtc": "2026-05-13T00:00:00Z", "SampleSize": 100, "Outcome": "Succeeded"
          }
        }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson unknownCoordJson) Profile.empty
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected silent skip, got %A" errs)
    | Ok profile ->
        Assert.Empty(profile.Distributions)

[<Fact>]
let ``attach numeric: coexists with categorical in a single attach call`` () =
    // The single-function dispatch handles both variants in one
    // `distributions` array.
    let mixedJson = """
    {
      "distributions": [
        { "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
          "Kind": "Categorical",
          "DistinctCount": 3, "IsTruncated": false,
          "Frequencies": [
            { "Value": "CA", "Count": 1 },
            { "Value": "MX", "Count": 1 },
            { "Value": "US", "Count": 1 }
          ],
          "ProbeStatus": { "CapturedAtUtc": "2026-05-12T00:00:00Z",
                           "SampleSize": 3, "Outcome": "Succeeded" } },
        { "Schema": "dbo", "Table": "OSUSR_S1S_CUSTOMER", "Column": "TENANT_ID",
          "Kind": "Numeric",
          "Min": 1, "P25": 2, "P50": 3, "P75": 4, "P95": 8, "P99": 9, "Max": 10,
          "SampleSize": 100,
          "ProbeStatus": { "CapturedAtUtc": "2026-05-13T00:00:00Z",
                           "SampleSize": 100, "Outcome": "Succeeded" } }
      ]
    }
    """
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson mixedJson) Profile.empty
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Ok profile ->
        Assert.Equal(2, profile.Distributions.Length)
        // Both lookups succeed, on their respective attributes.
        Assert.True (Profile.tryFindCategorical countryCodeKey profile |> Option.isSome)
        Assert.True (Profile.tryFindNumeric customerTenantKey profile |> Option.isSome)

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
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson badOutcomeJson) Profile.empty
    match result with
    | Ok _ -> Assert.Fail "Expected failure on unknown ProbeOutcome"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "profileStatisticsAdapter.probeOutcome.unknown")

// ---------------------------------------------------------------------------
// Missing-array behavior — empty distributions handled like other adapters.
// ---------------------------------------------------------------------------

[<Fact>]
let ``attach: missing distributions array is treated as empty`` () =
    let result = ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson "{}") Profile.empty
    match result with
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Ok profile ->
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
        ProfileSnapshot.attach sampleCatalog (ProfileSnapshot.ProfileSnapshotJson snapshotJson)
        |> Result.bind (ProfileStatistics.attach sampleCatalog (ProfileStatistics.DistributionsJson validCategoricalJson))
    match r with
    | Error errs -> Assert.Fail(sprintf "Expected success, got %A" errs)
    | Ok profile ->
        Assert.Equal(1, profile.Columns.Length)
        Assert.Equal(1, profile.Distributions.Length)
