module Projection.Tests.FkSelectivityDiagnosticsTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// H-025 — FkSelectivityDiagnostics.emit contract tests.
/// Three structural cases: empty profile (identity), below-threshold
/// selectivity (no entry), above-threshold selectivity (entry emitted).

let private mkSel
    (refKey: SsKey)
    (frequencies: (string * int64) list)
    (distinctCount: int64)
    (isTruncated: bool)
    : ForeignKeySelectivity =
    ForeignKeySelectivity.create
        refKey frequencies distinctCount isTruncated
        (ProbeStatus.observed distinctCount)
    |> Result.value

let private refKey (name: string) : SsKey =
    SsKey.synthesized "REF" name |> Result.value

[<Fact>]
let ``H-025: empty profile → empty diagnostics`` () =
    let result = FkSelectivityDiagnostics.emit Profile.empty
    Assert.Empty result

[<Fact>]
let ``H-025: low-selectivity FK (many rows per value) → no entry`` () =
    // 4 distinct values, 100 rows → meanMatchCount = 25.0 (well above threshold 2.0)
    let sel =
        mkSel (refKey "OrderStatusId")
            [ "1", 40L; "2", 30L; "3", 20L; "4", 10L ]
            4L false
    let profile = { Profile.empty with ForeignKeySelectivities = [ sel ] }
    let result = FkSelectivityDiagnostics.emit profile
    Assert.Empty result

[<Fact>]
let ``H-025: below minDistinctCount guard → no entry`` () =
    // Only 5 distinct values (< 10 guard), even with meanMatchCount < 2.0
    let sel =
        mkSel (refKey "TinyLookupId")
            [ "1", 1L; "2", 1L; "3", 1L; "4", 1L; "5", 1L ]
            5L false
    let profile = { Profile.empty with ForeignKeySelectivities = [ sel ] }
    let result = FkSelectivityDiagnostics.emit profile
    Assert.Empty result

[<Fact>]
let ``H-025: high-selectivity FK → Info entry emitted`` () =
    // 50 distinct values, 55 rows → meanMatchCount = 1.1 (below threshold 2.0)
    let frequencies = [ for i in 1..50 -> string i, 1L ] @ [ "1", 4L ] |> List.truncate 50
    let sel =
        mkSel (refKey "CustomerId")
            ([ for i in 1..50 -> string i, 1L ])
            50L false
    let profile = { Profile.empty with ForeignKeySelectivities = [ sel ] }
    let result = FkSelectivityDiagnostics.emit profile
    Assert.Single result |> ignore
    let entry = List.head result
    Assert.Equal(DiagnosticSeverity.Info, entry.Severity)
    Assert.Equal("profiling.fkSelectivity.highSelectivityCandidate", entry.Code)
    Assert.Equal(Some sel.ReferenceKey, entry.SsKey)
    Assert.True(entry.Metadata.ContainsKey "distinctCount")
    Assert.True(entry.Metadata.ContainsKey "meanMatchCount")

[<Fact>]
let ``H-025: exactly-at-threshold (meanMatchCount = 2.0) → no entry`` () =
    // 10 distinct values, 20 rows → meanMatchCount = 2.0 (at threshold, not below)
    let sel =
        mkSel (refKey "RegionId")
            [ for i in 1..10 -> string i, 2L ]
            10L false
    let profile = { Profile.empty with ForeignKeySelectivities = [ sel ] }
    let result = FkSelectivityDiagnostics.emit profile
    Assert.Empty result
