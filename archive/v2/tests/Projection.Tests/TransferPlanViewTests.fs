module Projection.Tests.TransferPlanViewTests

// THE TRANSFER PLAN, rich lens (2026-07-08) — the `Rule`/`Tree` rendering of the
// pure plan. Renders to an in-memory writer (the redirected NoColors report),
// asserting the operator-facing structure survives.

open Xunit
open Projection.Pipeline
open Projection.Cli

let private render (p: TransferPlan.Plan) : string =
    use sw = new System.IO.StringWriter()
    TransferPlanView.write sw p
    sw.ToString()

let private plan () =
    TransferPlan.ofCurrent "golden" "cloud-qa" "cloud-uat"
        { Strategy = "replace"; Reconciles = true; HasSubset = true
          SupportingScope = 0; Streaming = false; Staging = "auto" }

[<Fact>]
let ``ofPlan: each decision is a titled Rule section over a Tree of alternatives`` () =
    let out = render (plan ())
    Assert.Contains("TRANSFER PLAN", out)              // the masthead
    Assert.Contains("write strategy", out)             // a Rule section title (the axis)
    Assert.Contains("identity", out)
    Assert.Contains("realization", out)
    Assert.True([ "├"; "└"; "│" ] |> List.exists out.Contains, "no tree connectors in the plan")

[<Fact>]
let ``ofPlan: the current branch is marked and every branch names its config edit`` () =
    let out = render (plan ())
    Assert.Contains("current: replace", out)           // the current write strategy
    Assert.Contains("● replace", out)                  // the chosen branch marker
    Assert.Contains("▸ merge", out)                    // an unchosen branch marker
    Assert.Contains("set \"strategy\": \"merge\"", out)  // the config edit that would switch it
    Assert.Contains("upsert-only", out)                // the merge branch names the guarantee

[<Fact>]
let ``ofPlan: the closing reminder frames the config as the menu`` () =
    Assert.Contains("projection.json", render (plan ()))   // every branch is a hand-reachable edit
