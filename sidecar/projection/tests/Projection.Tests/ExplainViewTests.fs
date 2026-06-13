module Projection.Tests.ExplainViewTests

open System.Text.Json
open Xunit
open Projection.Core
open Projection.Cli

/// NM-37 — `runExplain` once hand-rolled its transform/findings output in raw
/// `printfn`, bypassing the `View` engine — so it had no JSON/`--query` lens and
/// escaped the code⇔copy + twelve-rule tests, while `View.Trail` (built for
/// exactly this surface) had ZERO producers. The face now builds a `View`
/// (`RunFaces.explainView`) routed through `TtyRenderer.renderAnswer`. These pin
/// that the explain story IS a `View.Trail` and carries the JSON lens — the human
/// and machine views are one value.

let private json (v: View.View) : JsonElement =
    JsonDocument.Parse((View.toJson v).ToJsonString()).RootElement.Clone()

let private trailEvent (passName: string) (key: SsKey) (kind: TransformKind) : LineageEvent =
    { PassName       = passName
      PassVersion    = 1
      SsKey          = key
      TransformKind  = kind
      Classification = Classification.DataIntent }

[<Fact>]
let ``NM-37: the explain story is a View.Trail (the previously zero-producer block)`` () =
    let key = SsKey.ossysOriginal (System.Guid.NewGuid())
    let trail = [ trailEvent "NullabilityPass" key TransformKind.Touched ]
    let view = RunFaces.explainView "OrderHeader" trail []
    // JSON lens — explain now has the structured projection it lacked. The block
    // the audit named (`View.Trail`) is present, with its named label.
    let blocks = (json view).GetProperty("blocks").EnumerateArray() |> Seq.toList
    let kindOf (b: JsonElement) = nonNull (b.GetProperty("kind").GetString())
    let trailBlock = blocks |> List.find (fun b -> kindOf b = "trail")
    Assert.Equal("transforms", nonNull (trailBlock.GetProperty("label").GetString()))
    let steps = trailBlock.GetProperty("steps").EnumerateArray() |> Seq.toList
    Assert.Equal(1, List.length steps)
    Assert.Contains("NullabilityPass", nonNull (steps.[0].GetProperty("step").GetString()))

[<Fact>]
let ``NM-37: a finding renders as a status field with its suggested fix (the JSON lens)`` () =
    let key = SsKey.ossysOriginal (System.Guid.NewGuid())
    let diag =
        { DiagnosticEntry.create "explain" DiagnosticSeverity.Warning "profile.nullBudget" "20% NULL" with
            SsKey = Some key
            SuggestedConfig = Some { Path = "$.policy.nullBudget"; Value = "0.5"; Note = None } }
    let view = RunFaces.explainView "CustomerId" [] [ diag ]
    let blocks = (json view).GetProperty("blocks").EnumerateArray() |> Seq.toList
    let kindOf (b: JsonElement) = nonNull (b.GetProperty("kind").GetString())
    let kinds = blocks |> List.map kindOf
    Assert.Contains("field", kinds)
    Assert.Contains("action", kinds)   // the suggested fix is the next-action line
    let field =
        blocks
        |> List.find (fun b ->
            kindOf b = "field"
            && nonNull (b.GetProperty("label").GetString()) = "profile.nullBudget")
    Assert.Equal("warn", nonNull (field.GetProperty("status").GetString()))

[<Fact>]
let ``NM-37: an empty match names the gap and the next move (never silence)`` () =
    let view = RunFaces.explainView "Nope" [] []
    let blocks = (json view).GetProperty("blocks").EnumerateArray() |> Seq.toList
    let kinds = blocks |> List.map (fun b -> nonNull (b.GetProperty("kind").GetString()))
    Assert.Contains("note", kinds)     // "no transforms or findings matched"
    Assert.Contains("action", kinds)   // "try a fuller name…"
    Assert.DoesNotContain("trail", kinds)
