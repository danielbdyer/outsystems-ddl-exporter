module Projection.Tests.TransferPlanTests

// THE TRANSFER PLAN (2026-07-08, the guided-wizard program) — the pure decision
// model: every axis + its alternatives + the WHY + the config edit. Pure, so the
// choices and their reasons are asserted without a DB, and the machine lens
// (`toJsonString`) is a stable `--format json` twin.

open Xunit
open Projection.Pipeline

let private sample : TransferPlan.Current =
    { Strategy = "replace"; Reconciles = true; HasSubset = true
      SupportingScope = 2; Streaming = false; Staging = "auto" }

let private plan () = TransferPlan.ofCurrent "golden" "cloud-qa" "cloud-uat" sample

[<Fact>]
let ``ofCurrent: one decision per transfer axis, each with its alternatives`` () =
    let p = plan ()
    Assert.Equal<string list>(
        [ "write strategy"; "identity"; "scope"; "realization"; "staging" ],
        p.Decisions |> List.map (fun d -> d.Axis))
    // every decision offers at least two branches (it is a CHOICE, not a fact)
    for d in p.Decisions do Assert.True(List.length d.Options >= 2, sprintf "axis %s has no alternatives" d.Axis)

[<Fact>]
let ``ofCurrent: the flow's current choice is the marked branch, and it carries a config edit`` () =
    let ws = (plan ()).Decisions |> List.find (fun d -> d.Axis = "write strategy")
    Assert.Equal("replace", ws.Current)
    let chosen = ws.Options |> List.filter (fun o -> o.Chosen)
    Assert.Equal(1, chosen.Length)                               // exactly one current
    Assert.Equal("strategy.replace", chosen.Head.Code)
    // every write-strategy branch names the exact config edit that selects it
    for o in ws.Options do Assert.StartsWith("\"strategy\":", o.ConfigEdit)

[<Fact>]
let ``ofCurrent: every option carries a non-empty WHY (the tradeoff is always stated)`` () =
    for d in (plan ()).Decisions do
        Assert.False(System.String.IsNullOrWhiteSpace d.Rationale, sprintf "axis %s has no rationale" d.Axis)
        for o in d.Options do
            Assert.False(System.String.IsNullOrWhiteSpace o.Why, sprintf "%s option %s has no why" d.Axis o.Code)

[<Fact>]
let ``the plan copy stays in THE_VOICE register — no first/second-person pronouns`` () =
    // The reporting-boundary prose obeys the same register the go board's does:
    // stative, agentless. A leaked "you"/"we"/"our" would break the voice.
    let banned = [ " you "; " your "; " we "; " our "; " us "; " i "; "please" ]
    let prose =
        (plan ()).Decisions
        |> List.collect (fun d -> d.Rationale :: (d.Options |> List.collect (fun o -> [ o.Label; o.Why ])))
    for s in prose do
        let padded = (" " + s + " ").ToLowerInvariant()
        for b in banned do
            Assert.False(padded.Contains(b), sprintf "plan copy leaked '%s': %s" b s)

[<Fact>]
let ``reselectStrategy: re-marks the write-strategy branch, leaving other axes untouched`` () =
    let p = TransferPlan.reselectStrategy "merge" (plan ())
    let ws = p.Decisions |> List.find (fun d -> d.Axis = "write strategy")
    Assert.Equal("merge", ws.Current)
    Assert.Equal("strategy.merge", (ws.Options |> List.find (fun o -> o.Chosen)).Code)
    // identity axis is unchanged (still re-keyed by rule)
    let id = p.Decisions |> List.find (fun d -> d.Axis = "identity")
    Assert.Equal("re-keyed by rule", id.Current)

[<Fact>]
let ``toJsonString: the machine lens carries every axis, its current, and the chosen options`` () =
    let json = TransferPlan.toJsonString (plan ())
    let doc = System.Text.Json.JsonDocument.Parse(json).RootElement
    Assert.Equal("golden", doc.GetProperty("flow").GetString())
    let decisions = doc.GetProperty("decisions").EnumerateArray() |> Seq.toList
    Assert.Equal(5, decisions.Length)
    let ws = decisions |> List.find (fun d -> d.GetProperty("axis").GetString() = "write strategy")
    Assert.Equal("replace", ws.GetProperty("current").GetString())
    let chosen =
        ws.GetProperty("options").EnumerateArray()
        |> Seq.filter (fun o -> o.GetProperty("chosen").GetBoolean())
        |> Seq.map (fun o -> nonNull (o.GetProperty("code").GetString()))
        |> Seq.toList
    Assert.Equal<string list>([ "strategy.replace" ], chosen)
