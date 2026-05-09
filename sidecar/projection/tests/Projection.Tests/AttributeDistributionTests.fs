module Projection.Tests.AttributeDistributionTests

open System
open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private succeededProbe (sample: int64) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch sample Succeeded
    |> Result.value

// ---------------------------------------------------------------------------
// CategoricalDistribution.create — validation surface.
// ---------------------------------------------------------------------------

[<Fact>]
let ``create: valid full distribution succeeds`` () =
    let probe = succeededProbe 3L
    let result =
        CategoricalDistribution.create
            countryCodeKey
            [ "CA", 1L; "MX", 1L; "US", 1L ]
            3L
            false
            probe
    match result with
    | Ok cat ->
        Assert.Equal(countryCodeKey, cat.AttributeKey)
        Assert.Equal(3L, cat.DistinctCount)
        Assert.False(cat.IsTruncated)
    | Error errs ->
        Assert.Fail(sprintf "Expected success, got %A" errs)

[<Fact>]
let ``create: rejects negative DistinctCount`` () =
    let probe = succeededProbe 3L
    let result =
        CategoricalDistribution.create
            countryCodeKey
            [ "CA", 1L ]
            -1L
            true
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected failure for negative DistinctCount"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "categoricalDistribution.distinctCount.negative")

[<Fact>]
let ``create: rejects negative per-value count`` () =
    let probe = succeededProbe 3L
    let result =
        CategoricalDistribution.create
            countryCodeKey
            [ "CA", -1L ]
            1L
            false
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected failure for negative per-value count"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "categoricalDistribution.frequencyCount.negative")

[<Fact>]
let ``create: rejects truncation contradiction (not truncated but DistinctCount > Frequencies.Length)`` () =
    let probe = succeededProbe 100L
    let result =
        CategoricalDistribution.create
            countryCodeKey
            [ "CA", 1L; "MX", 1L ]   // 2 frequencies
            10L                        // claims 10 distinct
            false                      // but not truncated → contradiction
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected truncation contradiction failure"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "categoricalDistribution.truncation.contradiction")

[<Fact>]
let ``create: accepts truncated distribution where DistinctCount > Frequencies.Length`` () =
    let probe = succeededProbe 1000L
    let result =
        CategoricalDistribution.create
            countryCodeKey
            [ "CA", 100L; "MX", 100L; "US", 100L ]
            195L         // observed total distinct, capped at top 3
            true
            probe
    match result with
    | Ok cat ->
        Assert.Equal(195L, cat.DistinctCount)
        Assert.True(cat.IsTruncated)
        Assert.Equal(3, List.length cat.Frequencies)
    | Error errs ->
        Assert.Fail(sprintf "Expected success, got %A" errs)

[<Fact>]
let ``create: sorts Frequencies alphabetically by value for determinism`` () =
    let probe = succeededProbe 3L
    // Pass values in non-alphabetical order; expect alphabetical
    // ordering on the constructed value.
    let result =
        CategoricalDistribution.create
            countryCodeKey
            [ "US", 1L; "CA", 1L; "MX", 1L ]
            3L
            false
            probe
    match result with
    | Ok cat ->
        let values = cat.Frequencies |> List.map fst
        Assert.Equal<string list>([ "CA"; "MX"; "US" ], values)
    | Error errs ->
        Assert.Fail(sprintf "Expected success, got %A" errs)

// ---------------------------------------------------------------------------
// totalObservations / isComplete helpers.
// ---------------------------------------------------------------------------

[<Fact>]
let ``totalObservations: sums per-value counts`` () =
    let probe = succeededProbe 12L
    let cat =
        CategoricalDistribution.create
            countryCodeKey
            [ "CA", 3L; "MX", 4L; "US", 5L ]
            3L false probe
        |> Result.value
    Assert.Equal(12L, CategoricalDistribution.totalObservations cat)

[<Fact>]
let ``isComplete: false when truncated, true when not`` () =
    let probe = succeededProbe 100L
    let truncated =
        CategoricalDistribution.create
            countryCodeKey [ "A", 50L ] 5L true probe
        |> Result.value
    let full =
        CategoricalDistribution.create
            countryCodeKey [ "A", 50L ] 1L false probe
        |> Result.value
    Assert.False(CategoricalDistribution.isComplete truncated)
    Assert.True (CategoricalDistribution.isComplete full)

// ---------------------------------------------------------------------------
// Profile.tryFindCategorical — lookup by attribute identity.
// ---------------------------------------------------------------------------

[<Fact>]
let ``tryFindCategorical: returns the registered distribution`` () =
    let probe = succeededProbe 3L
    let cat =
        CategoricalDistribution.create
            countryCodeKey [ "CA", 1L; "MX", 1L; "US", 1L ]
            3L false probe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Categorical cat ] }
    let result = Profile.tryFindCategorical countryCodeKey profile
    Assert.Equal<CategoricalDistribution option>(Some cat, result)

[<Fact>]
let ``tryFindCategorical: returns None for unknown attribute`` () =
    let probe = succeededProbe 3L
    let cat =
        CategoricalDistribution.create
            countryCodeKey [ "CA", 1L ] 1L false probe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Categorical cat ] }
    let result = Profile.tryFindCategorical customerNameKey profile
    Assert.Equal<CategoricalDistribution option>(None, result)

[<Fact>]
let ``tryFindCategorical: returns None on Profile.empty`` () =
    let result = Profile.tryFindCategorical countryCodeKey Profile.empty
    Assert.Equal<CategoricalDistribution option>(None, result)

// ---------------------------------------------------------------------------
// Profile.empty / Profile.isEmpty — Distributions is part of the empty
// commitment.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Profile.empty has no Distributions`` () =
    Assert.Empty(Profile.empty.Distributions)

[<Fact>]
let ``Profile.isEmpty: false when only Distributions has content`` () =
    let probe = succeededProbe 3L
    let cat =
        CategoricalDistribution.create countryCodeKey [ "CA", 1L ] 1L false probe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Categorical cat ] }
    Assert.False(Profile.isEmpty profile)

[<Fact>]
let ``Profile.isEmpty: true on Profile.empty`` () =
    Assert.True(Profile.isEmpty Profile.empty)

// ---------------------------------------------------------------------------
// AttributeDistribution DU round-trip.
// ---------------------------------------------------------------------------

[<Fact>]
let ``AttributeDistribution.Categorical round-trips`` () =
    let probe = succeededProbe 3L
    let cat =
        CategoricalDistribution.create countryCodeKey [ "CA", 1L ] 1L false probe
        |> Result.value
    Assert.Equal<AttributeDistribution>(
        AttributeDistribution.Categorical cat,
        AttributeDistribution.Categorical cat)

// ---------------------------------------------------------------------------
// NumericDistribution.create — structural-commitment validation surface
// (AXIOMS.md 2026-05-12 — structural-commitment-via-construction-validation).
// ---------------------------------------------------------------------------

[<Fact>]
let ``numeric create: monotonic percentiles + adequate sample size succeeds`` () =
    let probe = succeededProbe 100L
    let result =
        NumericDistribution.create
            countryCodeKey
            0m   // Min
            10m  // P25
            25m  // P50
            50m  // P75
            90m  // P95
            99m  // P99
            100m // Max
            100L // SampleSize
            probe
    match result with
    | Ok dist ->
        Assert.Equal(countryCodeKey, dist.AttributeKey)
        Assert.Equal(0m, dist.Min)
        Assert.Equal(50m, dist.P75)
        Assert.Equal(100m, dist.Max)
        Assert.Equal(100L, dist.SampleSize)
    | Error errs ->
        Assert.Fail(sprintf "Expected success, got %A" errs)

[<Fact>]
let ``numeric create: degenerate Min=Max=all-percentiles succeeds`` () =
    // A column where every observation is the same value. The
    // monotonicity chain holds with equalities; the contract permits.
    let probe = succeededProbe 50L
    let result =
        NumericDistribution.create
            countryCodeKey
            42m 42m 42m 42m 42m 42m 42m
            50L
            probe
    match result with
    | Ok dist ->
        Assert.True(NumericDistribution.isDegenerate dist)
    | Error errs ->
        Assert.Fail(sprintf "Expected success, got %A" errs)

[<Fact>]
let ``numeric create: rejects sample size below floor of 5`` () =
    let probe = succeededProbe 4L
    let result =
        NumericDistribution.create
            countryCodeKey
            0m 1m 2m 3m 4m 5m 10m
            4L
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected failure for SampleSize < 5"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.sampleSize.belowFloor")

[<Fact>]
let ``numeric create: rejects negative sample size`` () =
    let probe = succeededProbe 5L
    let result =
        NumericDistribution.create
            countryCodeKey
            0m 1m 2m 3m 4m 5m 10m
            -1L
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected failure for negative SampleSize"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.sampleSize.negative")

[<Fact>]
let ``numeric create: rejects out-of-order percentiles (P50 > P75)`` () =
    let probe = succeededProbe 100L
    let result =
        NumericDistribution.create
            countryCodeKey
            0m 10m 50m 30m 90m 99m 100m  // P50=50 > P75=30 — violates monotonicity
            100L
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected failure for non-monotonic percentiles"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.percentiles.nonMonotonic")

[<Fact>]
let ``numeric create: rejects Min greater than P25`` () =
    let probe = succeededProbe 100L
    let result =
        NumericDistribution.create
            countryCodeKey
            10m 5m 25m 50m 90m 99m 100m  // Min=10 > P25=5
            100L
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected failure: Min > P25"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.percentiles.nonMonotonic")

[<Fact>]
let ``numeric create: rejects P99 greater than Max`` () =
    let probe = succeededProbe 100L
    let result =
        NumericDistribution.create
            countryCodeKey
            0m 10m 25m 50m 90m 99m 50m  // Max=50 < P99=99
            100L
            probe
    match result with
    | Ok _ -> Assert.Fail "Expected failure: P99 > Max"
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = "numericDistribution.percentiles.nonMonotonic")

// ---------------------------------------------------------------------------
// Helpers — interQuartileRange, observedRange, isDegenerate.
// ---------------------------------------------------------------------------

[<Fact>]
let ``numeric helpers: interQuartileRange returns P75 minus P25`` () =
    let probe = succeededProbe 100L
    let dist =
        NumericDistribution.create
            countryCodeKey 0m 10m 25m 50m 90m 99m 100m 100L probe
        |> Result.value
    Assert.Equal(40m, NumericDistribution.interQuartileRange dist)

[<Fact>]
let ``numeric helpers: observedRange returns Max minus Min`` () =
    let probe = succeededProbe 100L
    let dist =
        NumericDistribution.create
            countryCodeKey 0m 10m 25m 50m 90m 99m 100m 100L probe
        |> Result.value
    Assert.Equal(100m, NumericDistribution.observedRange dist)

[<Fact>]
let ``numeric helpers: isDegenerate is true when Min equals Max`` () =
    let probe = succeededProbe 50L
    let degenerate =
        NumericDistribution.create
            countryCodeKey 7m 7m 7m 7m 7m 7m 7m 50L probe
        |> Result.value
    let normal =
        NumericDistribution.create
            countryCodeKey 0m 10m 25m 50m 90m 99m 100m 100L probe
        |> Result.value
    Assert.True (NumericDistribution.isDegenerate degenerate)
    Assert.False(NumericDistribution.isDegenerate normal)

// ---------------------------------------------------------------------------
// sampleSizeFloor literal — surfaces the named constant for readers and
// future maintainers.
// ---------------------------------------------------------------------------

[<Fact>]
let ``numeric: sampleSizeFloor is 5`` () =
    Assert.Equal(5L, NumericDistribution.sampleSizeFloor)

// ---------------------------------------------------------------------------
// AttributeDistribution.Numeric variant — DU integration.
// ---------------------------------------------------------------------------

[<Fact>]
let ``AttributeDistribution.Numeric round-trips`` () =
    let probe = succeededProbe 100L
    let num =
        NumericDistribution.create
            countryCodeKey 0m 10m 25m 50m 90m 99m 100m 100L probe
        |> Result.value
    Assert.Equal<AttributeDistribution>(
        AttributeDistribution.Numeric num,
        AttributeDistribution.Numeric num)

// ---------------------------------------------------------------------------
// Profile.tryFindNumeric — lookup by attribute identity.
// ---------------------------------------------------------------------------

[<Fact>]
let ``tryFindNumeric: returns the registered distribution`` () =
    let probe = succeededProbe 100L
    let num =
        NumericDistribution.create
            countryCodeKey 0m 10m 25m 50m 90m 99m 100m 100L probe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Numeric num ] }
    Assert.Equal<NumericDistribution option>(
        Some num,
        Profile.tryFindNumeric countryCodeKey profile)

[<Fact>]
let ``tryFindNumeric: returns None for unknown attribute`` () =
    let probe = succeededProbe 100L
    let num =
        NumericDistribution.create
            countryCodeKey 0m 10m 25m 50m 90m 99m 100m 100L probe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Numeric num ] }
    Assert.Equal<NumericDistribution option>(
        None,
        Profile.tryFindNumeric customerNameKey profile)

[<Fact>]
let ``tryFindNumeric: returns None on Profile.empty`` () =
    Assert.Equal<NumericDistribution option>(
        None,
        Profile.tryFindNumeric countryCodeKey Profile.empty)

// ---------------------------------------------------------------------------
// Cross-variant lookup discipline — Categorical lookup ignores Numeric
// distributions (and vice versa). The variants share the
// AttributeKey-keying space without colliding.
// ---------------------------------------------------------------------------

[<Fact>]
let ``cross-variant: tryFindCategorical does not return Numeric distributions`` () =
    let probe = succeededProbe 100L
    let num =
        NumericDistribution.create
            countryCodeKey 0m 10m 25m 50m 90m 99m 100m 100L probe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Numeric num ] }
    Assert.Equal<CategoricalDistribution option>(
        None,
        Profile.tryFindCategorical countryCodeKey profile)

[<Fact>]
let ``cross-variant: tryFindNumeric does not return Categorical distributions`` () =
    let probe = succeededProbe 3L
    let cat =
        CategoricalDistribution.create
            countryCodeKey [ "CA", 1L; "MX", 1L; "US", 1L ] 3L false probe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [ AttributeDistribution.Categorical cat ] }
    Assert.Equal<NumericDistribution option>(
        None,
        Profile.tryFindNumeric countryCodeKey profile)

// ---------------------------------------------------------------------------
// Coexistence — Categorical + Numeric distributions on different
// attributes cleanly co-inhabit a single Profile.
// ---------------------------------------------------------------------------

[<Fact>]
let ``coexistence: Categorical and Numeric distributions on different attributes coexist in one Profile`` () =
    let catProbe = succeededProbe 3L
    let numProbe = succeededProbe 100L
    let cat =
        CategoricalDistribution.create
            countryCodeKey [ "CA", 1L; "MX", 1L; "US", 1L ] 3L false catProbe
        |> Result.value
    let num =
        NumericDistribution.create
            customerNameKey 0m 10m 25m 50m 90m 99m 100m 100L numProbe
        |> Result.value
    let profile =
        { Profile.empty with
            Distributions = [
                AttributeDistribution.Categorical cat
                AttributeDistribution.Numeric num
            ] }
    Assert.Equal<CategoricalDistribution option>(Some cat, Profile.tryFindCategorical countryCodeKey profile)
    Assert.Equal<NumericDistribution option>    (Some num, Profile.tryFindNumeric customerNameKey profile)
    // Cross-shape lookups still return None.
    Assert.Equal<CategoricalDistribution option>(None, Profile.tryFindCategorical customerNameKey profile)
    Assert.Equal<NumericDistribution option>    (None, Profile.tryFindNumeric countryCodeKey profile)
