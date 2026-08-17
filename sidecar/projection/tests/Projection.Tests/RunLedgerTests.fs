module Projection.Tests.RunLedgerTests

open System
open System.IO
open Xunit
open Projection.Pipeline

/// Tier-4 reporting — the cross-run ledger + the R6 readiness gauge
/// (`REPORTING_HORIZON.md` §3; `DECISIONS 2026-05-22 — R6`).

let private record (canary: string option) : RunLedger.LedgerRecord =
    { RunId = "r"; Ts = System.DateTimeOffset(2026, 6, 5, 0, 0, 0, System.TimeSpan.Zero); Command = "projection canary"; Outcome = "succeeded"
      Canary = Projection.Core.CanaryVerdict.ofTokenOpt canary; Registered = 42; Applied = 0; Declined = 0 }

[<Fact>]
let ``Tier-4 R6: eligible after Threshold consecutive green canaries`` () =
    let r = List.replicate RunLedger.R6Threshold (record (Some "green")) |> RunLedger.readiness
    Assert.Equal(RunLedger.R6Threshold, r.ConsecutiveGreen)
    Assert.True(r.Eligible)

[<Fact>]
let ``Tier-4 R6: one short of Threshold is not eligible`` () =
    let r = List.replicate (RunLedger.R6Threshold - 1) (record (Some "green")) |> RunLedger.readiness
    Assert.Equal(RunLedger.R6Threshold - 1, r.ConsecutiveGreen)
    Assert.False(r.Eligible)

[<Fact>]
let ``Tier-4 R6: a red canary resets the streak and eligibility`` () =
    let r =
        (List.replicate 12 (record (Some "green")) @ [ record (Some "red") ])
        |> RunLedger.readiness
    Assert.Equal(0, r.ConsecutiveGreen)
    Assert.Equal(Some Projection.Core.CanaryVerdict.Red, r.LastCanary)
    Assert.False(r.Eligible)

[<Fact>]
let ``Tier-4 R6: the streak counts only from the most recent run backward`` () =
    // A historical red before a fresh green streak does not reduce the
    // current streak — the gate measures the current run of greens.
    let r =
        ([ record (Some "red") ] @ List.replicate RunLedger.R6Threshold (record (Some "green")))
        |> RunLedger.readiness
    Assert.Equal(RunLedger.R6Threshold, r.ConsecutiveGreen)
    Assert.True(r.Eligible)

[<Fact>]
let ``Tier-4: runs with no canary leg are skipped, not streak-breaking`` () =
    let r =
        (List.replicate RunLedger.R6Threshold (record (Some "green")) @ [ record None; record None ])
        |> RunLedger.readiness
    Assert.Equal(RunLedger.R6Threshold, r.ConsecutiveGreen)
    Assert.Equal(RunLedger.R6Threshold, r.CanaryRuns)   // the two None runs don't count as canary runs
    Assert.True(r.Eligible)

[<Fact>]
let ``Tier-4: append then read round-trips records in order`` () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    try
        RunLedger.append dir (record (Some "green"))
        RunLedger.append dir (record (Some "red"))
        RunLedger.append dir (record None)
        let records = RunLedger.read dir
        Assert.Equal(3, List.length records)
        Assert.Equal(Projection.Core.CanaryVerdict.Green, records.[0].Canary)
        Assert.Equal(Projection.Core.CanaryVerdict.Red, records.[1].Canary)
        Assert.Equal(Projection.Core.CanaryVerdict.NotRun, records.[2].Canary)
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``align-III.1: a stored line with a malformed ts drops through the lenient posture — never a fabricated instant`` () =
    // The reader's standing posture skips malformed lines (align-III.3 owns
    // naming the skips); typing the instant folds a torn `ts` into that
    // posture instead of loading it as an empty string.
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    try
        RunLedger.append dir (record (Some "green"))
        File.AppendAllText(
            RunLedger.ledgerPath dir,
            """{"runId":"torn","ts":"not-an-instant","command":"c","outcome":"failed","canary":null,"registered":0,"applied":0,"declined":0}""" + "\n")
        let records = RunLedger.read dir
        Assert.Equal(1, List.length records)
        Assert.Equal("r", records.[0].RunId)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``align-III.3: a trailing torn line is tolerated silently; an interior corrupt line is counted on SkippedLines`` () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    try
        // Two good records, then an INTERIOR corrupt line, then a good record,
        // then a TRAILING torn line (a crash mid-append).
        RunLedger.append dir (record (Some "green"))
        RunLedger.append dir (record (Some "green"))
        File.AppendAllText(RunLedger.ledgerPath dir, "{ not json at all" + "\n")   // interior — counted
        RunLedger.append dir (record (Some "green"))
        File.AppendAllText(RunLedger.ledgerPath dir, """{"runId":"torn","ts":"2026""")   // trailing torn — tolerated (no newline)
        let reading = RunLedger.readReading dir
        Assert.Equal(3, List.length reading.Records)   // the three good records
        Assert.Equal(1, reading.SkippedLines)          // the ONE interior corrupt line
        // The gauge surfaces the skip count; `read` stays the records-only view.
        let r = RunLedger.readinessOf reading
        Assert.Equal(1, r.SkippedLines)
        Assert.Equal(3, List.length (RunLedger.read dir))
    finally
        try Directory.Delete(dir, true) with _ -> ()
