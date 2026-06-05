module Projection.Tests.RunLedgerTests

open System
open System.IO
open Xunit
open Projection.Pipeline

/// Tier-4 reporting — the cross-run ledger + the R6 readiness gauge
/// (`REPORTING_HORIZON.md` §3; `DECISIONS 2026-05-22 — R6`).

let private record (canary: string option) : RunLedger.LedgerRecord =
    { RunId = "r"; Ts = "t"; Command = "projection canary"; Outcome = "succeeded"
      Canary = canary; Registered = 42; Applied = 0; Declined = 0 }

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
    Assert.Equal(Some "red", r.LastCanary)
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
        Assert.Equal(Some "green", records.[0].Canary)
        Assert.Equal(Some "red", records.[1].Canary)
        Assert.Equal(None, records.[2].Canary)
    finally
        try Directory.Delete(dir, true) with _ -> ()
