module Projection.Tests.RunHistoryTests

open Xunit
open Projection.Pipeline

/// The temporal base — the durable run timeline. Discriminating predicate:
/// trend / canaryHistory / readiness are all projections of one run sequence.

let private run (ts: string) (canary: string option) (declined: int) : Run.Run =
    { RunId = ts; Ts = ts; Command = "projection canary"; InputDigest = "d"
      Outcome = "succeeded"; Canary = canary; Registered = 42; Applied = 0; Declined = declined
      Events = []; Artifacts = Map.empty }

[<Fact>]
let ``RunHistory: ofRuns sorts chronologically (oldest first)`` () =
    let h = RunHistory.ofRuns [ run "2026-03" None 0; run "2026-01" None 0; run "2026-02" None 0 ]
    Assert.Equal<string list>([ "2026-01"; "2026-02"; "2026-03" ], h.Runs |> List.map (fun r -> r.Ts))

[<Fact>]
let ``RunHistory: trend maps a metric over the timeline`` () =
    let h = RunHistory.ofRuns [ run "2026-01" None 5; run "2026-02" None 2; run "2026-03" None 8 ]
    Assert.Equal<int list>([ 5; 2; 8 ], h |> RunHistory.trend (fun r -> r.Declined))

[<Fact>]
let ``RunHistory: canaryHistory is the green/red series, oldest first`` () =
    let h = RunHistory.ofRuns [ run "2026-01" (Some "green") 0; run "2026-02" None 0; run "2026-03" (Some "red") 0 ]
    Assert.Equal<string list>([ "green"; "red" ], RunHistory.canaryHistory h)   // None skipped

[<Fact>]
let ``RunHistory: readiness over the history reuses the R6 gauge (subsumes the ledger)`` () =
    let greens = [ for i in 1 .. RunLedger.R6Threshold -> run (sprintf "2026-%02d" i) (Some "green") 0 ]
    let r = RunHistory.ofRuns greens |> RunHistory.readiness
    Assert.Equal(RunLedger.R6Threshold, r.ConsecutiveGreen)
    Assert.True(r.Eligible)

[<Fact>]
let ``RunHistory: latest is the most recent run`` () =
    let h = RunHistory.ofRuns [ run "2026-01" None 0; run "2026-03" (Some "green") 0; run "2026-02" None 0 ]
    match RunHistory.latest h with
    | Some r -> Assert.Equal("2026-03", r.Ts)
    | None   -> Assert.Fail "expected a latest run"
