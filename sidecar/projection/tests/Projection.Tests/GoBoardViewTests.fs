module Projection.Tests.GoBoardViewTests

// The go board's RICH lens (2026-07-08, the rendering-elevation program): the
// pure `GoBoard.Board` projected through the Spectre-backed `View` engine —
// the responsive forecast table and the fully-expanded References / Dependents
// guarantee tree. Pure: renders to an in-memory writer (the redirected =
// NoColors, width-100 lens), asserts the operator-facing substrings survive.
// The machine lens (`GoBoard.toJsonString`) stays the separate CI contract —
// its byte-identity under a Body-bearing item is pinned here too.

open Xunit
open Projection.Pipeline
open Projection.Cli

let private render (board: GoBoard.Board) : string =
    use sw = new System.IO.StringWriter()
    GoBoardView.write sw board
    sw.ToString()

let private forecastLine src tbl before adds matches deletes note : GoBoard.ForecastLine =
    { Source = src; Table = tbl; Before = before; Adds = adds; Matches = matches; Deletes = deletes; Note = note }

[<Fact>]
let ``ofBoard: the forecast renders as a table with the add cells and a TOTAL row`` () =
    let lines = [ forecastLine "dbo.SRC" "dbo.DST" (Some 10L) 5L None 0L "brought along by Order.CityId" ]
    let board : GoBoard.Board =
        { Flow = "f"; From = "a"; To = "b"
          Items = [ GoBoard.forecastItem "forecast" (GoBoard.Status.Green "dry run complete") lines [] (GoBoard.forecastTable lines) ] }
    let out = render board
    Assert.Contains("+add", out)          // a short header cell — no wrap risk at width 100
    Assert.Contains("TOTAL", out)         // the totals row closes the table
    Assert.Contains("dbo.SRC", out)       // the source physical name
    Assert.Contains("dbo.DST", out)       // the sink physical name

[<Fact>]
let ``ofBoard: a Confirmed scope claim renders its guarantee and join edge under the family tree`` () =
    let claim : GoBoard.ScopeClaim =
        { Family = "references"; Relationship = "static-lookup"; Table = "Ref.Currency"
          Status = GoBoard.Status.Green "held identical"; JoinEdges = [ "Order.CurrencyId" ]
          Reason = "currencies are identical across environments"
          Guarantee = "held to zero divergence — a verified no-op on the lookup." }
    let groups = [ { GoBoard.ScopeGroup.Family = "references"; GoBoard.ScopeGroup.Claims = [ claim ] } ]
    let board : GoBoard.Board =
        { Flow = "f"; From = "a"; To = "b"
          Items = [ GoBoard.scopeItem "supporting scope" (GoBoard.Status.Green "all borne out") groups [] [] ] }
    let out = render board
    Assert.Contains("References", out)          // the family headline
    Assert.Contains("static-lookup", out)       // the relationship label
    Assert.Contains("Ref.Currency", out)        // the supporting table
    Assert.Contains("guarantee:", out)          // the invariant is labeled
    Assert.Contains("no-op on the lookup", out) // ...and stated
    Assert.Contains("Order.CurrencyId", out)    // the normalized join edge

[<Fact>]
let ``ofBoard: a green verdict names the --go command; a red one names the re-run`` () =
    let green : GoBoard.Board =
        { Flow = "golden"; From = "a"; To = "b"; Items = [ GoBoard.item "routing" (GoBoard.Status.Green "ok") ] }
    Assert.Contains("--go", render green)
    let red : GoBoard.Board =
        { Flow = "golden"; From = "a"; To = "b"; Items = [ GoBoard.item "routing" (GoBoard.Status.Red ("blocked", "fix it")) ] }
    let redOut = render red
    Assert.Contains("check go", redOut)
    Assert.Contains("fix it", redOut)           // the red item's remedy is surfaced

[<Fact>]
let ``toJsonString: a Body-bearing item renders JSON identical to a plain item with the same detail`` () =
    // The Body carrier is invisible to the machine lens — JSON reads Axis +
    // Status + Detail only. A `scopeItem` and an `itemWith` with the same
    // Detail must produce byte-identical JSON (the CI contract is stable).
    let detail = [ "line one"; "line two" ]
    let groups = [ { GoBoard.ScopeGroup.Family = "references"; GoBoard.ScopeGroup.Claims = [] } ]
    let bodyBoard : GoBoard.Board =
        { Flow = "f"; From = "a"; To = "b"; Items = [ GoBoard.scopeItem "scope" (GoBoard.Status.Green "ok") groups [ "x" ] detail ] }
    let plainBoard : GoBoard.Board =
        { Flow = "f"; From = "a"; To = "b"; Items = [ GoBoard.itemWith "scope" (GoBoard.Status.Green "ok") detail ] }
    Assert.Equal(GoBoard.toJsonString plainBoard, GoBoard.toJsonString bodyBoard)
