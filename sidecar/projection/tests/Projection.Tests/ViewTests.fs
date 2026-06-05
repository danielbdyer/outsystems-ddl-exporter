module Projection.Tests.ViewTests

open System.IO
open System.Text.Json
open Xunit
open Spectre.Console
open Projection.Pipeline
open Projection.Cli

/// Masterful base #3 — the `View` document primitive. The discriminating
/// predicate: pretty/plain and json are projections of ONE value, so the
/// human and machine lenses cannot drift.

let private plain (v: View.View) : string =
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- 200
    View.write console v
    sw.ToString()

let private json (v: View.View) : JsonElement =
    JsonDocument.Parse((View.toJson v).ToJsonString()).RootElement.Clone()

[<Fact>]
let ``View: one document renders to plain AND json over the same value`` () =
    let v =
        View.Doc [
            View.Hero(View.Ok, "ELIGIBLE")
            View.Field("canary", "green", View.Ok)
            View.Meter("cutover", 7, 10, "7 / 10 green") ]
    // pretty/plain lens
    let p = plain v
    Assert.Contains("ELIGIBLE", p)
    Assert.Contains("green", p)
    Assert.Contains("▇", p)          // meter
    Assert.Contains("✓", p)          // Ok glyph (status drives it)
    // json lens — SAME value, structured
    let j = json v
    Assert.Equal("doc", j.GetProperty("kind").GetString())
    let blocks = j.GetProperty("blocks").EnumerateArray() |> Seq.toList
    let field = blocks |> List.find (fun b -> b.GetProperty("kind").GetString() = "field")
    Assert.Equal("green", field.GetProperty("value").GetString())
    Assert.Equal("ok", field.GetProperty("status").GetString())
    let meter = blocks |> List.find (fun b -> b.GetProperty("kind").GetString() = "meter")
    Assert.Equal(7, meter.GetProperty("filled").GetInt32())
    Assert.Equal(10, meter.GetProperty("total").GetInt32())

[<Fact>]
let ``View: Status drives the glyph — Bad shows the cross, no color needed`` () =
    let p = plain (View.Field("outcome", "FAILED", View.Bad))
    Assert.Contains("FAILED", p)
    Assert.Contains("✕", p)

[<Fact>]
let ``View: the board build carries its data into json (consumer round-trip)`` () =
    let r : RunLedger.Readiness =
        { TotalRuns = 10; CanaryRuns = 10; ConsecutiveGreen = 10
          LastCanary = Some "green"; Threshold = 10; Eligible = true }
    let v = TtyRenderer.buildReadinessView r [ "green"; "red"; "green" ] "/x/runs.jsonl"
    let blocks = (json v).GetProperty("blocks").EnumerateArray() |> Seq.toList
    let kinds = blocks |> List.map (fun b -> nonNull (b.GetProperty("kind").GetString()))
    Assert.Contains("hero", kinds)
    Assert.Contains("meter", kinds)
    Assert.Contains("dots", kinds)

[<Fact>]
let ``View: a Lane renders its items (plain) and carries glyph/status/items (json)`` () =
    let v = View.Lane("⟲", "rename", View.Ok, [ "OrderHeader → SalesOrder"; "OrderDetail → SalesOrderLine" ])
    // pretty/plain lens — the label + both items
    let p = plain v
    Assert.Contains("rename", p)
    Assert.Contains("OrderHeader → SalesOrder", p)
    Assert.Contains("OrderDetail → SalesOrderLine", p)
    // json lens — SAME value, structured
    let j = json v
    Assert.Equal("lane", j.GetProperty("kind").GetString())
    Assert.Equal("rename", j.GetProperty("label").GetString())
    Assert.Equal("ok", j.GetProperty("status").GetString())
    let items =
        j.GetProperty("items").EnumerateArray() |> Seq.map (fun e -> nonNull (e.GetString())) |> Seq.toList
    Assert.Equal<string list>([ "OrderHeader → SalesOrder"; "OrderDetail → SalesOrderLine" ], items)
