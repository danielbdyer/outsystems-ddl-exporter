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

let private plainToDepth (depth: int) (v: View.View) : string =
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- 200
    View.writeToDepth console depth v
    sw.ToString()

let private plain (v: View.View) : string = plainToDepth View.defaultDepth v

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
let ``Setup readback: an unset ledger is named and recommended, never scolded (§14)`` () =
    let p = plain (TtyRenderer.buildSetupView None false 120L None None)
    Assert.Contains("Setup", p)
    Assert.Contains("not retained", p)
    Assert.Contains("preview only", p)            // live writes not armed
    Assert.Contains("PROJECTION_LEDGER_DIR", p)   // the §14 recommendation

[<Fact>]
let ``Setup readback: configured state shows plainly, with no recommendation`` () =
    let p = plain (TtyRenderer.buildSetupView (Some "./runs") true 200L (Some "./bench") None)
    Assert.Contains("retained", p)
    Assert.Contains("./runs", p)
    Assert.Contains("armed", p)                       // live writes armed
    Assert.Contains("200 ms", p)
    Assert.DoesNotContain("PROJECTION_LEDGER_DIR", p) // configured → no scold

let private allGranted : (Preflight.WriteAction * bool) list =
    Preflight.allWriteActions |> List.map (fun a -> a, true)

[<Fact>]
let ``Setup readback: a reachable target shows reachable + the ALTER grant`` () =
    let p = plain (TtyRenderer.buildSetupView (Some "./runs") false 120L None (Some ("UAT", true, allGranted)))
    Assert.Contains("UAT", p)
    Assert.Contains("reachable", p)
    Assert.Contains("ALTER granted", p)

[<Fact>]
let ``Setup readback: a reachable target names the broader write grants (D3)`` () =
    // D3 — INSERT / CREATE TABLE / DELETE surface, not ALTER alone.
    let p = plain (TtyRenderer.buildSetupView (Some "./runs") false 120L None (Some ("UAT", true, allGranted)))
    Assert.Contains("INSERT", p)
    Assert.Contains("CREATE TABLE", p)
    Assert.Contains("DELETE", p)

[<Fact>]
let ``Setup readback: missing write grants are named exactly (D3, survey-style phrasing)`` () =
    // Only ALTER granted; the data writes (INSERT / CREATE TABLE / DELETE) are
    // missing and read on their own line ("missing INSERT, CREATE TABLE, DELETE").
    let grants =
        Preflight.allWriteActions
        |> List.map (fun a -> a, (a = Preflight.WriteAction.Alter))
    let p = plain (TtyRenderer.buildSetupView (Some "./runs") false 120L None (Some ("UAT", true, grants)))
    Assert.Contains("ALTER granted", p)
    Assert.Contains("missing", p)
    Assert.Contains("INSERT", p)

[<Fact>]
let ``Setup readback: an unreachable target is named, with no grant claimed`` () =
    let p = plain (TtyRenderer.buildSetupView None false 120L None (Some ("UAT", false, [])))
    Assert.Contains("unreachable", p)
    Assert.DoesNotContain("ALTER", p)   // the grant is unknowable until reachable

[<Fact>]
let ``Capability survey: covered / missing / unreachable / no-gate each read plainly`` () =
    let reports : CapabilitySurvey.EnvironmentReport list =
        [ { Name = "cloud-uat"; Grant = Some Grant.SchemaAndData; Required = Set.empty; Connected = true;  Reachable = true;  Missing = []; CdcTracked = false }
          { Name = "prod";      Grant = Some Grant.DataOnly;      Required = Set.empty; Connected = true;  Reachable = true;  Missing = [ CapabilitySurvey.Performs Preflight.Insert ]; CdcTracked = false }
          { Name = "stale";     Grant = Some Grant.DataOnly;      Required = Set.empty; Connected = true;  Reachable = false; Missing = []; CdcTracked = false }
          { Name = "onprem";    Grant = None;                     Required = Set.empty; Connected = false; Reachable = false; Missing = []; CdcTracked = false } ]
    let p = plain (TtyRenderer.buildSurveyView reports)
    Assert.Contains("grant covered", p)        // cloud-uat — covered
    Assert.Contains("missing INSERT", p)       // prod — declared data-only, INSERT absent
    Assert.Contains("unreachable", p)          // stale
    Assert.Contains("no live gate", p)         // onprem — bundle, nothing to probe
    Assert.Contains("need attention", p)       // the verdict (2 of 4)

// --- progressive disclosure (the dig substrate) ----------------------------
// Discriminating predicate: a Disclosure HIDES its detail when collapsed and
// REVEALS it one level deeper when open — but `toJson` carries the full detail
// REGARDLESS of render depth, so the machine lens never loses the tree the human
// collapsed. A naive "json mirrors the screen" drops the collapsed detail and
// fails the last assertion; a naive "render everything" fails the collapse.

[<Fact>]
let ``View: a Disclosure collapses its detail shallow, reveals it deep — json carries it either way`` () =
    let v =
        View.Disclosure(
            "Details", View.Neutral,
            [ View.Field("exit", "9", View.Neutral)
              View.Field("detail", "dropping index IX_Order_Stale", View.Bad) ])
    // collapsed (depth 0): the headline shows, the detail is hidden, the
    // affordance hints at what's inside.
    let shallow = plainToDepth 0 v
    Assert.Contains("Details", shallow)
    Assert.DoesNotContain("dropping index IX_Order_Stale", shallow)
    Assert.Contains("2 more", shallow)
    // open (depth 1): the detail is revealed.
    let deep = plainToDepth 1 v
    Assert.Contains("dropping index IX_Order_Stale", deep)
    // json — the full detail rides the structure no matter the render depth.
    let j = json v
    Assert.Equal("disclosure", j.GetProperty("kind").GetString())
    Assert.Equal(2, j.GetProperty("count").GetInt32())
    Assert.Equal(2, j.GetProperty("detail").EnumerateArray() |> Seq.length)

[<Fact>]
let ``View: a Lane collapses its items shallow, shows them one level deep`` () =
    let v = View.Lane("⟲", "rename", View.Ok, [ "OrderHeader → SalesOrder" ])
    Assert.DoesNotContain("OrderHeader → SalesOrder", plainToDepth 0 v)
    Assert.Contains("OrderHeader → SalesOrder", plainToDepth 1 v)

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

[<Fact>]
let ``View: a Lane caps its rendered items and names the remainder (THE_VOICE §12)`` () =
    let items = [ for i in 1 .. 20 -> sprintf "Table%02d" i ]
    let v = View.Lane("−", "remove", View.Bad, items)
    let p = plainToDepth 1 v
    // the first items show; beyond the cap collapses to a named remainder
    Assert.Contains("Table01", p)
    Assert.DoesNotContain("Table20", p)
    Assert.Contains("and 8 more", p)        // 20 − laneCap(12) = 8, named
    // the machine lens keeps the full list — the cap is a rendering concern only
    let kept = (json v).GetProperty("items").EnumerateArray() |> Seq.length
    Assert.Equal(20, kept)

[<Fact>]
let ``View: a Lane humanizes its true count in the header (THE_VOICE §12)`` () =
    let items = [ for i in 1 .. 2140 -> sprintf "c%04d" i ]
    let v = View.Lane("+", "add", View.Ok, items)
    Assert.Contains("2,140", plainToDepth 0 v)   // the count scales; the sentence does not
