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

/// Render at an explicit `RenderOptions` (the #4 carrier) — the same NoColors plain
/// lens, but through `writeWith` so a test can pin a custom breadth / depth / width.
let private plainWith (opts: View.RenderOptions) (v: View.View) : string =
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- 200
    View.writeWith opts console v
    sw.ToString()

/// Render the plain lens at a chosen console width — the #11 width budget flows
/// from `console.Profile.Width`, so pinning the terminal narrow exercises the width
/// cap (truncation) the default 200-wide helpers never trip.
let private plainAtWidth (width: int) (v: View.View) : string =
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- width
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
let ``View: Timeline renders the strip + R6 meter and carries the full series in json`` () =
    let v = View.Timeline("cutover", [ "green"; "green"; "red"; "green" ], 3, 10, Some 3)
    // pretty/plain lens — dots, present marker, R6 meter, ratio
    let p = plain v
    Assert.Contains("●", p)          // green dot glyph
    Assert.Contains("✕", p)          // red verdict glyph
    Assert.Contains("▸", p)          // present marker (collapsed glyph)
    Assert.Contains("▇", p)          // R6 meter
    Assert.Contains("3/10", p)
    // json lens — SAME value: the full series + present index survive
    let j = json v
    Assert.Equal("timeline", j.GetProperty("kind").GetString())
    let cells = j.GetProperty("cells").EnumerateArray() |> Seq.map (fun e -> nonNull (e.GetString())) |> Seq.toList
    Assert.Equal<string list>([ "green"; "green"; "red"; "green" ], cells)
    Assert.Equal(3, j.GetProperty("filled").GetInt32())
    Assert.Equal(10, j.GetProperty("total").GetInt32())
    Assert.Equal(3, j.GetProperty("present").GetInt32())

[<Fact>]
let ``View: Timeline without a present marker omits present from json`` () =
    let j = json (View.Timeline("cutover", [ "green"; "green" ], 2, 10, None))
    let found, _ = j.TryGetProperty("present")
    Assert.False(found)

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
    let v = TtyRenderer.buildReadinessView r [ "green"; "red"; "green" ] [] "/x/runs.jsonl"
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
    let emailKeyed : Projection.Adapters.Sql.ReadSide.UserDirectoryProbe =
        { Found = true; EmailKeyed = true; TableName = Some "dbo.OSSYS_USER" }
    let absentDir = Projection.Adapters.Sql.ReadSide.UserDirectoryProbe.absent
    let reports : CapabilitySurvey.EnvironmentReport list =
        [ { Name = "cloud-uat"; Grant = Some Grant.SchemaAndData; Required = Set.empty; Connected = true;  Reachable = true;  Missing = []; GrantUnreadable = false; CdcTracked = false; CdcProbeFailed = false; UserDirectory = emailKeyed; ArchetypeFindings = [] }
          { Name = "prod";      Grant = Some Grant.DataOnly;      Required = Set.empty; Connected = true;  Reachable = true;  Missing = [ CapabilitySurvey.Performs Preflight.Insert ]; GrantUnreadable = false; CdcTracked = false; CdcProbeFailed = false; UserDirectory = absentDir; ArchetypeFindings = [] }
          { Name = "stale";     Grant = Some Grant.DataOnly;      Required = Set.empty; Connected = true;  Reachable = false; Missing = []; GrantUnreadable = false; CdcTracked = false; CdcProbeFailed = false; UserDirectory = absentDir; ArchetypeFindings = [] }
          { Name = "onprem";    Grant = None;                     Required = Set.empty; Connected = false; Reachable = false; Missing = []; GrantUnreadable = false; CdcTracked = false; CdcProbeFailed = false; UserDirectory = absentDir; ArchetypeFindings = [] } ]
    let p = plain (TtyRenderer.buildSurveyView reports)
    Assert.Contains("grant covered", p)        // cloud-uat — covered
    Assert.Contains("users email-keyed", p)    // cloud-uat — P10 user-directory fragment
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
let ``View: a collapsed Lane hints its item count — the affordance matches a collapsed Disclosure (#16)`` () =
    // A collapsed Lane no longer leaves its items implied: it shows the same `▸ N
    // items` affordance a collapsed Disclosure shows (`▸ N more`), so the two node
    // kinds speak one collapsed-affordance vocabulary.
    let items = [ "OrderHeader → SalesOrder"; "OrderDetail → SalesOrderLine"; "Customer → Account" ]
    let shallow = plainToDepth 0 (View.Lane("⟲", "rename", View.Ok, items))
    Assert.DoesNotContain("OrderHeader → SalesOrder", shallow)   // the item CONTENT stays hidden
    Assert.Contains("3 items", shallow)                          // the count + affordance is hinted
    Assert.Contains("▸", shallow)                                // with the openable marker
    // singular reads correctly — "1 item", never "1 items"
    let single = plainToDepth 0 (View.Lane("−", "remove", View.Bad, [ "OnlyOne" ]))
    Assert.Contains("1 item", single)
    Assert.DoesNotContain("1 items", single)

[<Fact>]
let ``View: a Trail caps its steps and names the remainder, collapses at depth 0 — json keeps the full chain (#15)`` () =
    // The transform chain gains the Lane discipline: capped + depth-gated on the
    // pretty lens, full on the machine lens — a long chain is no longer a wall.
    let steps = [ for i in 1 .. 20 -> (sprintf "Pass%02d" i, None) ]
    let v = View.Trail("transforms", steps)
    // depth 1 — the chain reveals, capped at laneCap(12) with a named remainder
    let deep = plainToDepth 1 v
    Assert.Contains("Pass01", deep)
    Assert.DoesNotContain("Pass20", deep)
    Assert.Contains("and 8 more", deep)        // 20 − laneCap(12) = 8, named
    // depth 0 — the chain collapses to a hint, like a collapsed Lane
    let shallow = plainToDepth 0 v
    Assert.DoesNotContain("Pass01", shallow)
    Assert.Contains("20 steps", shallow)
    // json — the full 20-step chain rides regardless (the machine lens never caps)
    Assert.Equal(20, (json v).GetProperty("steps").EnumerateArray() |> Seq.length)

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

// --- the RenderOptions carrier (#4 — one threaded policy, not scattered consts) -
// Discriminating predicate: the pretty-lens knobs (depth, breadth, width) ride ONE
// `RenderOptions` value through `writeWith`, so a caller overrides the breadth cap
// or the depth without touching a module constant — and the machine lens (`toJson`)
// reads NONE of them, because a cap is a pretty-lens concern (the one-substrate law).

[<Fact>]
let ``RenderOptions: writeWith threads the breadth cap — a custom LaneCap overrides the default, json keeps the full list`` () =
    // 10 items render whole under the default cap (12); an explicit LaneCap of 3
    // proves the breadth knob is read from RenderOptions, not the old module const.
    let items = [ for i in 1 .. 10 -> sprintf "Item%02d" i ]
    let v = View.Lane("+", "add", View.Ok, items)
    let p = plainWith { View.defaultOptions with Depth = 1; LaneCap = 3 } v
    Assert.Contains("Item03", p)
    Assert.DoesNotContain("Item04", p)
    Assert.Contains("and 7 more", p)        // 10 − LaneCap(3) = 7, named
    // the machine lens ignores the cap — breadth is a pretty-lens concern only
    let kept = (json v).GetProperty("items").EnumerateArray() |> Seq.length
    Assert.Equal(10, kept)

[<Fact>]
let ``RenderOptions: writeWith threads the depth — a deeper Depth reveals what the default collapses`` () =
    let v =
        View.Disclosure(
            "Details", View.Neutral,
            [ View.Disclosure("Inner", View.Neutral, [ View.Field("leaf", "deep", View.Bad) ]) ])
    // the nested leaf sits two levels down: collapsed at Depth 0, revealed at Depth 2 —
    // the Depth field flows through writeWith's carrier, not a bare int parameter.
    Assert.DoesNotContain("deep", plainWith { View.defaultOptions with Depth = 0 } v)
    Assert.Contains("deep", plainWith { View.defaultOptions with Depth = 2 } v)

// --- responsive width (#11 — the width cap, dual of the §12 breadth cap) -----
// Discriminating predicate: a value wider than the console is TRUNCATED with `…`
// on the pretty lens (one line, no mid-cell wrap), while `toJson` keeps the full
// untruncated value — width is a pretty-lens concern only, the same law `laneCap`
// obeys. The budget flows from `console.Profile.Width`, so it bites on a narrow
// terminal and never on the wide (200-col) one the other tests pin.

[<Fact>]
let ``View: a long Field value truncates to the console width with an ellipsis and does not wrap — json keeps the full value (#11)`` () =
    let full = "dropping index IX_Order_Stale on dbo.OrderHeader and three dependent constraints"
    let v = View.Field("detail", full, View.Bad)
    let p = plainAtWidth 40 v
    let lines = p.TrimEnd().Split('\n') |> Array.map (fun s -> s.TrimEnd('\r'))
    Assert.Single(lines) |> ignore                  // did NOT wrap to a second line
    Assert.Contains("…", p)                         // the truncation tail is shown
    Assert.DoesNotContain("constraints", p)         // the dropped tail is gone from the pretty lens
    for ln in lines do
        Assert.True(ln.Length <= 40, sprintf "rendered line over the 40-col budget: %d cols (%s)" ln.Length ln)
    // json — the SAME value, full and untruncated (width is pretty-only)
    Assert.Equal(full, (json v).GetProperty("value").GetString())

[<Fact>]
let ``View: a long Lane label truncates but the humane count survives the width cut — json keeps the full label + items (#11)`` () =
    let longName = "rename OrderHeaderArchiveStagingTableFromTheLegacyEstate"
    let items = [ for i in 1 .. 7 -> sprintf "table-%d" i ]
    let v = View.Lane("⟲", longName, View.Ok, items)
    let head = (plainAtWidth 40 v).TrimEnd().Split('\n').[0].TrimEnd('\r')
    Assert.True(head.Length <= 40, sprintf "headline over the 40-col budget: %d cols (%s)" head.Length head)
    Assert.Contains("…", head)        // the label was cut to fit
    Assert.EndsWith("7", head)        // the load-bearing humane count survived the cut
    // json keeps the full label and every item — width is pretty-only
    let j = json v
    Assert.Equal(longName, j.GetProperty("label").GetString())
    Assert.Equal(7, j.GetProperty("items").EnumerateArray() |> Seq.length)

[<Fact>]
let ``View: at a wide console a Field value renders whole — the width cap bites only when it must (#11)`` () =
    let full = "dropping index IX_Order_Stale on dbo.OrderHeader and three dependent constraints"
    let p = plainAtWidth 200 (View.Field("detail", full, View.Bad))
    Assert.Contains(full, p)          // the whole value is present
    Assert.DoesNotContain("…", p)     // no truncation tail at 200 cols

// --- surgical reveal (#18 — ViewPath / OpenPath: open one branch, leave the rest) -
// Discriminating predicate: an OpenPath force-reveals EXACTLY the addressed child-index
// branch (each element a level deeper), while every sibling stays at the ambient Depth —
// the dig's "open just this node, deeply, leave the rest collapsed". With OpenPath = None
// (every existing caller) the render reduces to today's Depth gate, byte-identical (the
// whole existing suite is that net). toJson never sees it — a path is pretty-only, like depth.

[<Fact>]
let ``View: OpenPath force-reveals exactly the addressed branch while siblings stay calm (#18)`` () =
    let v =
        View.Doc [
            View.Disclosure("alpha", View.Neutral, [ View.Field("a", "ALPHA-DETAIL", View.Ok) ])
            View.Disclosure("beta",  View.Neutral, [ View.Field("b", "BETA-DETAIL", View.Bad) ]) ]
    // ambient depth 0, no open path → BOTH collapse (this IS today's behavior)
    let calm = plainWith { View.defaultOptions with Depth = 0 } v
    Assert.DoesNotContain("ALPHA-DETAIL", calm)
    Assert.DoesNotContain("BETA-DETAIL", calm)
    // OpenPath [1] at ambient depth 0 → block 1 (beta) opens, block 0 (alpha) stays collapsed
    let opened = plainWith { View.defaultOptions with Depth = 0; OpenPath = Some [ 1 ] } v
    Assert.DoesNotContain("ALPHA-DETAIL", opened)   // the sibling stayed calm — surgical
    Assert.Contains("BETA-DETAIL", opened)          // the addressed branch opened

[<Fact>]
let ``View: OpenPath threads through nesting — Some [i;j] opens a branch two levels down (#18)`` () =
    let v =
        View.Doc [
            View.Disclosure("outer", View.Neutral, [
                View.Disclosure("inner", View.Neutral, [ View.Field("leaf", "DEEP-LEAF", View.Bad) ]) ]) ]
    // the leaf needs two levels; ambient Depth 0 and even 1 leave it collapsed
    Assert.DoesNotContain("DEEP-LEAF", plainWith { View.defaultOptions with Depth = 0 } v)
    Assert.DoesNotContain("DEEP-LEAF", plainWith { View.defaultOptions with Depth = 1 } v)
    // OpenPath [0;0] force-reveals outer then its inner — the leaf shows with NO global --depth bump
    Assert.Contains("DEEP-LEAF", plainWith { View.defaultOptions with Depth = 0; OpenPath = Some [ 0; 0 ] } v)

[<Fact>]
let ``View: OpenPath opens an addressed leaf-container (a Lane) even at ambient depth 0 (#18)`` () =
    let v = View.Doc [ View.Lane("⟲", "rename", View.Ok, [ "ORDER-MOVE"; "CUSTOMER-MOVE" ]) ]
    Assert.DoesNotContain("ORDER-MOVE", plainWith { View.defaultOptions with Depth = 0 } v)
    Assert.Contains("ORDER-MOVE", plainWith { View.defaultOptions with Depth = 0; OpenPath = Some [ 0 ] } v)

[<Fact>]
let ``View: the OpenPath tip wears the cursor caret — even a leaf — and no calm render does (#23)`` () =
    // Two leaf Fields. OpenPath = Some [1] exhausts the path AT block 1, so beta is the
    // cursor TIP (its OpenPath becomes Some []) and wears the caret; alpha (off-path) does
    // not. The caret is visible even though beta is a leaf with no disclosure marker.
    let v = View.Doc [ View.Field ("alpha", "A", View.Ok); View.Field ("beta", "B", View.Bad) ]
    let cursored = plainWith { View.defaultOptions with Depth = 0; OpenPath = Some [ 1 ] } v
    Assert.Contains(Theme.cursor, cursored)          // the focused leaf is marked
    // Byte-identity guard: no open path → no caret anywhere (the calm render is untouched).
    Assert.DoesNotContain(Theme.cursor, plainWith { View.defaultOptions with Depth = 0 } v)

// --- the one-substrate law over the whole DU (the totality lock) -----------
// Discriminating predicate: the pretty lens and the JSON lens are each TOTAL
// over the `View` DU — a case that forgets its render arm or its `toJson` arm
// fails HERE, in the pure pool, not in production at the tail of a run. The
// `Panel` row test pins the specific drift the typed `PanelRow` ended: the prior
// `Panel of View list` let `writePanel` render three cases and `| _ -> ()` the
// rest, while `toJson` kept them — the two lenses diverging on one value.

[<Fact>]
let ``View: a Panel renders EVERY row to both lenses — no row silently dropped`` () =
    let v =
        View.Panel(
            "verdict",
            [ View.PanelRow.Labeled("outcome", "succeeded", View.Ok)
              View.PanelRow.Gauge("cutover", 7, 10, "7 / 10 green")
              View.PanelRow.Next("projection suggest-config --apply") ])
    // pretty/plain lens — all three rows are present (the dropped-row bug would
    // lose the gauge and the next-action here).
    let p = plain v
    Assert.Contains("outcome", p)
    Assert.Contains("succeeded", p)
    Assert.Contains("▇", p)                                   // the gauge
    Assert.Contains("projection suggest-config --apply", p)   // the next-action row
    // json lens — the SAME value; "fields" carries every row, by its historical
    // kind, in order. The typed PanelRow makes a fourth, droppable kind impossible
    // to construct in the first place.
    let j = json v
    Assert.Equal("panel", j.GetProperty("kind").GetString())
    let kinds =
        j.GetProperty("fields").EnumerateArray()
        |> Seq.map (fun e -> nonNull (e.GetProperty("kind").GetString()))
        |> Seq.toList
    Assert.Equal<string list>([ "field"; "meter"; "action" ], kinds)

[<Fact>]
let ``View: every case renders to plain AND json without throwing (DU totality)`` () =
    // One instance per `View` case — exhaustive by construction. If a case is
    // added without a `writeBlock` arm or a `toJson` arm, this list won't compile
    // (the author must add the case) and the loop will catch a throwing lens.
    let cases : (string * View.View) list =
        [ "doc",        View.Doc [ View.Note "child" ]
          "panel",      View.Panel("p", [ View.PanelRow.Labeled("k", "v", View.Ok) ])
          "hero",       View.Hero(View.Ok, "ELIGIBLE")
          "field",      View.Field("k", "v", View.Warn)
          "meter",      View.Meter("m", 3, 10, "3 / 10")
          "dots",       View.Dots("history", [ "green"; "red" ])
          "spark",      View.Spark("trend", [ 3; 5; 2; 8 ])
          "timeline",   View.Timeline("cutover", [ "green"; "red" ], 3, 10, Some 1)
          "table",      View.Table([ "env"; "grant" ], [ [ "uat", View.Ok; "covered", View.Ok ]; [ "prod", View.Warn; "missing", View.Warn ] ])
          "trail",      View.Trail("steps", [ "naming", Some "rename"; "fk", None ])
          "lane",       View.Lane("+", "add", View.Ok, [ "A"; "B" ])
          "disclosure", View.Disclosure("Details", View.Neutral, [ View.Field("exit", "9", View.Bad) ])
          "note",       View.Note "a footnote"
          "action",     View.Action "do the thing"
          "blank",      View.Blank ]
    for (expectedKind, v) in cases do
        // human lens — renders at shallow and deep without throwing
        let shallow = plainToDepth 0 v
        let deep = plainToDepth 3 v
        Assert.False(isNull (box shallow), sprintf "%s: shallow render was null" expectedKind)
        Assert.False(isNull (box deep), sprintf "%s: deep render was null" expectedKind)
        // machine lens — serializes, and its kind tag is what the consumer expects
        let j = json v
        Assert.Equal(expectedKind, nonNull (j.GetProperty("kind").GetString()))

// --- defensive render (#3 — a markup fault degrades to plain, never crashes) -
// Discriminating predicate: `MarkupLine` THROWS on malformed markup, and the
// verdict panel renders at the END of a good run — the worst place to crash.
// `safeMarkupLine` must CONTAIN the fault: degrade to plain text, never throw.

[<Fact>]
let ``View: a malformed markup line degrades to plain text — a render fault never fails the run (#3)`` () =
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(
                Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
                Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- 200
    // `[badstyle]…[/]` is invalid Spectre markup (unknown style) and the trailing
    // `[` is an unbalanced tag — `MarkupLine` would throw on either. safeMarkupLine
    // must contain the fault and still surface the text, plain.
    View.safeMarkupLine console "[badstyle]contained[/] and a stray [ bracket"
    let out = sw.ToString()
    Assert.Contains("contained", out)   // the styled text survived, plain
    Assert.Contains("bracket", out)     // including past the unbalanced '['
    // reaching here at all means no exception escaped — the run was not failed

[<Fact>]
let ``View: a value carrying markup metacharacters renders them literally — the data-escaping discipline (#2/#3 guard)`` () =
    // A table literally named `Order[Archive]`, or a value with a `[bold]`-looking
    // token, must render its brackets as TEXT, not as a Spectre style tag — the
    // discipline every writeBlock data site follows (Markup.Escape before colorize).
    // This is the guard a future #2 (the Markup newtype) keeps green: a DOUBLE-escape
    // would render `[[`, an UNDER-escape would throw the data into #3's plain
    // fallback (a different string) — either way this assertion catches it.
    let p = plain (View.Field("table", "Order[Archive] is [bold]gone", View.Bad))
    Assert.Contains("Order[Archive] is [bold]gone", p)   // brackets intact, ONCE — not `[[`, not stripped

// --- the Table primitive (#12 — aligned columns, one lens) -------------------
// Discriminating predicate: a Table renders aligned columns to the human and
// carries the full grid (headers + every cell + per-cell status) to the machine —
// one value, two lenses. A cell's status drives its glyph so the matrix reads on a
// NoColors console; `toJson` keeps the status tags a `--query` can filter on.

[<Fact>]
let ``View: a Table renders headers + cells (plain) and carries the grid with per-cell status (json) (#12)`` () =
    let v =
        View.Table(
            [ "environment"; "grant" ],
            [ [ "cloud-uat", View.Ok;   "covered", View.Ok ]
              [ "prod",      View.Warn; "missing INSERT", View.Warn ] ])
    // pretty/plain lens — the headers and every cell's text are present
    let p = plain v
    Assert.Contains("environment", p)        // a header
    Assert.Contains("cloud-uat", p)          // a cell
    Assert.Contains("missing INSERT", p)     // a cell carrying spaces
    Assert.Contains("✓", p)                  // the Ok cell's glyph — status reads without color
    // json lens — the SAME grid: headers + rows + per-cell status
    let j = json v
    Assert.Equal("table", j.GetProperty("kind").GetString())
    let headers =
        j.GetProperty("headers").EnumerateArray() |> Seq.map (fun e -> nonNull (e.GetString())) |> Seq.toList
    Assert.Equal<string list>([ "environment"; "grant" ], headers)
    let rows = j.GetProperty("rows").EnumerateArray() |> Seq.toList
    Assert.Equal(2, rows |> List.length)
    let prodGrant = (rows.[1].EnumerateArray() |> Seq.toList).[1]
    Assert.Equal("missing INSERT", prodGrant.GetProperty("text").GetString())
    Assert.Equal("warn", prodGrant.GetProperty("status").GetString())

[<Fact>]
let ``Bench surface: benchView is a Table carrying the per-label stats in both lenses (#13)`` () =
    let stats : Projection.Core.Bench.Stats list =
        [ { Label = "emit.statements"; Count = 3; TotalMs = 120L; MinMs = 30L; MaxMs = 50L
            MeanMs = 40.0; P50Ms = 40L; P95Ms = 50L; P99Ms = 50L } ]
    let v = TtyRenderer.benchView stats
    // structure — a Doc whose Table maps each Stats record to a row of cells
    match v with
    | View.Doc blocks ->
        match blocks |> List.tryPick (function View.Table(h, r) -> Some(h, r) | _ -> None) with
        | Some (headers, rows) ->
            Assert.Contains("label", headers)
            Assert.Contains("total ms", headers)
            Assert.Equal(1, List.length rows)
            Assert.Equal<string>("emit.statements", fst rows.[0].[0])
            Assert.Equal<string>("120", fst rows.[0].[2])   // the total-ms cell
        | None -> Assert.Fail "benchView has no Table block"
    | other -> Assert.Fail(sprintf "benchView is not a Doc: %A" other)
    // machine lens — the perf surface is --query-able now: the numbers ride toJson
    let j = json v
    let table =
        j.GetProperty("blocks").EnumerateArray()
        |> Seq.find (fun b -> nonNull (b.GetProperty("kind").GetString()) = "table")
    let firstCell = (table.GetProperty("rows").EnumerateArray() |> Seq.head).EnumerateArray() |> Seq.head
    Assert.Equal("emit.statements", nonNull (firstCell.GetProperty("text").GetString()))

// --- trend surfaces (#14 — Theme.sparkline gets its first producer) ----------
// Discriminating predicate: a Spark compresses a numeric series into one ▁▂▃▄▅▆▇█
// line for the human, while `toJson` keeps the raw numbers the glyph hid — one
// value, two lenses. The readiness board is its first consumer: the changeset trend
// joins the canary dots.

[<Fact>]
let ``View: a Spark renders the series as a sparkline (plain) and carries the raw numbers (json) (#14)`` () =
    let v = View.Spark("changes / run", [ 1; 4; 2; 8; 3 ])
    // pretty/plain lens — the label + at least one sparkline bar glyph
    let p = plain v
    Assert.Contains("changes / run", p)
    Assert.True(
        [ "▁"; "▂"; "▃"; "▄"; "▅"; "▆"; "▇"; "█" ] |> List.exists (fun g -> p.Contains g),
        sprintf "no sparkline glyph in %s" p)
    // json lens — the SAME value: the raw series the glyph compressed
    let j = json v
    Assert.Equal("spark", j.GetProperty("kind").GetString())
    let vals = j.GetProperty("values").EnumerateArray() |> Seq.map (fun e -> e.GetInt32()) |> Seq.toList
    Assert.Equal<int list>([ 1; 4; 2; 8; 3 ], vals)

[<Fact>]
let ``View: the readiness board renders the changeset sparkline beside the dots (#14 consumer)`` () =
    let r : RunLedger.Readiness =
        { TotalRuns = 12; CanaryRuns = 12; ConsecutiveGreen = 5
          LastCanary = Some "green"; Threshold = 10; Eligible = false }
    let v = TtyRenderer.buildReadinessView r [ "green"; "green" ] [ 40; 22; 9; 3 ] "/x/runs.jsonl"
    let kinds =
        (json v).GetProperty("blocks").EnumerateArray()
        |> Seq.map (fun b -> nonNull (b.GetProperty("kind").GetString())) |> Seq.toList
    Assert.Contains("spark", kinds)     // the changeset trend joined the board
    Assert.Contains("dots", kinds)      // beside the canary dots

// --- the widget-elevation cases: Rule + Tree (2026-07-08) -------------------

[<Fact>]
let ``View: a titled Rule renders a divider carrying its title, and carries {title,status} in json`` () =
    let v = View.Rule(Some "cutover readiness", View.Neutral)
    let p = plain v
    Assert.Contains("cutover readiness", p)   // the title rides the divider
    Assert.Contains("─", p)                    // the rule line (box-drawing survives NoColors)
    let j = json v
    Assert.Equal("rule", j.GetProperty("kind").GetString())
    Assert.Equal("cutover readiness", j.GetProperty("title").GetString())
    Assert.Equal("neutral", j.GetProperty("status").GetString())

[<Fact>]
let ``View: a Rule caps its width on a widened report sink (never a 100k-column line)`` () =
    // The go board widens a redirected profile to 100_000 so proof never wraps; a
    // full-width rule there would be a 100_000-char line. The render arm caps it.
    use sw = new StringWriter()
    let console =
        AnsiConsole.Create(
            AnsiConsoleSettings(Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Out = AnsiConsoleOutput(sw)))
    console.Profile.Width <- 100000
    View.writeWith { View.defaultOptions with Width = System.Int32.MaxValue } console (View.Rule(Some "verdict", View.Neutral))
    let longestLine = sw.ToString().Split('\n') |> Array.map (fun l -> l.TrimEnd().Length) |> Array.max
    Assert.True(longestLine <= 120, sprintf "rule ran to %d columns" longestLine)

[<Fact>]
let ``View: a Tree renders connector lines fully expanded and carries the nested nodes in json`` () =
    let v =
        View.Tree("supporting scope", View.Ok,
            [ { Label = "References (1)"; Status = View.Ok
                Children = [ { Label = "existing-reference City"; Status = View.Ok
                               Children = [ { Label = "guarantee: no reference row is written."; Status = View.Neutral; Children = [] } ] } ] }
              { Label = "Dependents (1)"; Status = View.Bad
                Children = [ { Label = "owned-child City"; Status = View.Bad; Children = [] } ] } ])
    // pretty/plain lens — connector lines + every node fully expanded (not depth-gated)
    let p = plain v
    Assert.Contains("supporting scope", p)
    Assert.Contains("References (1)", p)
    Assert.Contains("existing-reference City", p)
    Assert.Contains("guarantee: no reference row is written.", p)   // the deepest leaf shows
    Assert.Contains("Dependents (1)", p)
    Assert.True([ "├"; "└"; "│" ] |> List.exists p.Contains, sprintf "no tree connector in %s" p)
    // json lens — SAME value: the full nested hierarchy + per-node status
    let j = json v
    Assert.Equal("tree", j.GetProperty("kind").GetString())
    Assert.Equal("ok", j.GetProperty("status").GetString())
    let nodes = j.GetProperty("nodes").EnumerateArray() |> Seq.toList
    Assert.Equal(2, nodes.Length)
    let refs = nodes |> List.find (fun n -> nonNull (n.GetProperty("label").GetString()) = "References (1)")
    let child = refs.GetProperty("children").EnumerateArray() |> Seq.head
    Assert.Equal("existing-reference City", child.GetProperty("label").GetString())
    let deps = nodes |> List.find (fun n -> nonNull (n.GetProperty("label").GetString()) = "Dependents (1)")
    Assert.Equal("bad", deps.GetProperty("status").GetString())
