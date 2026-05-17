module Projection.Tests.ManifestCoverageTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — shim restoring the Lineage<Catalog> shape.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

let private enrich (c: Catalog) : Catalog = (ciRun c).Value

// ---------------------------------------------------------------------------
// Chapter 4.4 slice α — CoverageBreakdown smart constructor invariants.
//
// Mirrors V1's `Osm.Emission.CoverageBreakdown` (SsdtManifest.cs:68-90)
// including percentage-rounding contract:
//   Math.Round(value, 2, MidpointRounding.AwayFromZero)
// with edge cases (Total = 0 → 100m; Emitted = 0 → 0m).
// ---------------------------------------------------------------------------

[<Fact>]
let ``CoverageBreakdown.create rejects negative Emitted`` () =
    match CoverageBreakdown.create -1 10 with
    | Error errs ->
        Assert.Contains (errs, fun e -> e.Code = "coverage.emittedNegative")
    | Ok _ -> Assert.Fail "Expected emittedNegative failure"

[<Fact>]
let ``CoverageBreakdown.create rejects negative Total`` () =
    match CoverageBreakdown.create 0 -1 with
    | Error errs ->
        Assert.Contains (errs, fun e -> e.Code = "coverage.totalNegative")
    | Ok _ -> Assert.Fail "Expected totalNegative failure"

[<Fact>]
let ``CoverageBreakdown.create rejects Emitted greater than Total`` () =
    match CoverageBreakdown.create 11 10 with
    | Error errs ->
        Assert.Contains (errs, fun e -> e.Code = "coverage.emittedExceedsTotal")
    | Ok _ -> Assert.Fail "Expected emittedExceedsTotal failure"

[<Fact>]
let ``CoverageBreakdown.create: Total = 0 yields 100 percent (vacuous full coverage)`` () =
    let result = CoverageBreakdown.create 0 0 |> Result.value
    Assert.Equal (0, result.Emitted)
    Assert.Equal (0, result.Total)
    Assert.Equal (100m, result.Percentage)

[<Fact>]
let ``CoverageBreakdown.create: Emitted = 0 with Total greater than 0 yields 0 percent`` () =
    let result = CoverageBreakdown.create 0 10 |> Result.value
    Assert.Equal (0m, result.Percentage)

[<Fact>]
let ``CoverageBreakdown.create: full coverage yields 100 percent`` () =
    let result = CoverageBreakdown.create 10 10 |> Result.value
    Assert.Equal (100m, result.Percentage)

[<Fact>]
let ``CoverageBreakdown.create: partial coverage rounds to 2 decimals AwayFromZero`` () =
    // 1/3 = 33.333...% → rounds to 33.33%
    let r1 = CoverageBreakdown.create 1 3 |> Result.value
    Assert.Equal (33.33m, r1.Percentage)
    // 2/3 = 66.666...% → rounds to 66.67% (AwayFromZero)
    let r2 = CoverageBreakdown.create 2 3 |> Result.value
    Assert.Equal (66.67m, r2.Percentage)
    // 5/8 = 62.5% → stays 62.5
    let r3 = CoverageBreakdown.create 5 8 |> Result.value
    Assert.Equal (62.5m, r3.Percentage)

// ---------------------------------------------------------------------------
// CoverageSummary.createComplete shape: V2 emits every kind from the
// catalog (T11 keyset coverage holds structurally); the default
// summary has Emitted = Total per axis.
// ---------------------------------------------------------------------------

[<Fact>]
let ``CoverageSummary.createComplete: per-axis Emitted equals Total`` () =
    let summary = CoverageSummary.createComplete 5 30 12 |> Result.value
    Assert.Equal (5, summary.Tables.Emitted)
    Assert.Equal (5, summary.Tables.Total)
    Assert.Equal (30, summary.Columns.Emitted)
    Assert.Equal (30, summary.Columns.Total)
    Assert.Equal (12, summary.Constraints.Emitted)
    Assert.Equal (12, summary.Constraints.Total)

[<Fact>]
let ``CoverageSummary.createComplete: every axis reports 100 percent`` () =
    let summary = CoverageSummary.createComplete 5 30 12 |> Result.value
    Assert.Equal (100m, summary.Tables.Percentage)
    Assert.Equal (100m, summary.Columns.Percentage)
    Assert.Equal (100m, summary.Constraints.Percentage)

[<Fact>]
let ``CoverageSummary.createComplete: zero counts yield 100 percent per axis (vacuous)`` () =
    let summary = CoverageSummary.createComplete 0 0 0 |> Result.value
    Assert.Equal (100m, summary.Tables.Percentage)
    Assert.Equal (100m, summary.Columns.Percentage)
    Assert.Equal (100m, summary.Constraints.Percentage)

[<Fact>]
let ``CoverageSummary.createComplete: aggregates errors across axes`` () =
    match CoverageSummary.createComplete -1 -1 -1 with
    | Error errs ->
        // One emittedNegative per axis (also totalNegative since
        // emitted=total=-1, but smart-constructor short-circuits on
        // emittedNegative first per the check order).
        Assert.Equal (3, List.length errs)
    | Ok _ -> Assert.Fail "Expected aggregated failure"

// ---------------------------------------------------------------------------
// Coverage.compute: pure function of Catalog.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Coverage.compute: T1 byte-determinism (same Catalog yields same CoverageSummary)`` () =
    let enriched = enrich sampleCatalog
    let c1 = Coverage.compute enriched
    let c2 = Coverage.compute enriched
    Assert.Equal<CoverageSummary> (c1, c2)

[<Fact>]
let ``Coverage.compute on sampleCatalog: Emitted equals Total per axis (V2 emits everything)`` () =
    let enriched = enrich sampleCatalog
    let summary = Coverage.compute enriched
    Assert.Equal (summary.Tables.Emitted, summary.Tables.Total)
    Assert.Equal (summary.Columns.Emitted, summary.Columns.Total)
    Assert.Equal (summary.Constraints.Emitted, summary.Constraints.Total)
    // 100% per axis under T11 keyset coverage.
    Assert.Equal (100m, summary.Tables.Percentage)
    Assert.Equal (100m, summary.Columns.Percentage)
    Assert.Equal (100m, summary.Constraints.Percentage)

[<Fact>]
let ``Coverage.compute on sampleCatalog: Tables count matches Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let summary = Coverage.compute enriched
    let allKinds = Catalog.allKinds enriched
    Assert.Equal (List.length allKinds, summary.Tables.Total)

[<Fact>]
let ``Coverage.compute on sampleCatalog: Columns count matches sum of Attribute lists`` () =
    let enriched = enrich sampleCatalog
    let summary = Coverage.compute enriched
    let allKinds = Catalog.allKinds enriched
    let expected =
        allKinds |> List.sumBy (fun k -> List.length k.Attributes)
    Assert.Equal (expected, summary.Columns.Total)

[<Property(MaxTest = 200)>]
let ``CoverageBreakdown.create: percentage stays in [0, 100] for valid inputs`` (e: int) (t: int) =
    // Restrict to valid input domain (non-negative; emitted <= total).
    let emitted = abs e
    let total = abs t
    let validEmitted = if total = 0 then 0 else min emitted total
    match CoverageBreakdown.create validEmitted total with
    | Ok b ->
        b.Percentage >= 0m && b.Percentage <= 100m
    | Error _ -> false

[<Property(MaxTest = 200)>]
let ``CoverageBreakdown.create: deterministic`` (e: int) (t: int) =
    let emitted = abs e
    let total = abs t
    let validEmitted = if total = 0 then 0 else min emitted total
    let r1 = CoverageBreakdown.create validEmitted total
    let r2 = CoverageBreakdown.create validEmitted total
    r1 = r2
