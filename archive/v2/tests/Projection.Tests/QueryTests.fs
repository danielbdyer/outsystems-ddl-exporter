module Projection.Tests.QueryTests

open System.Text.Json
open Xunit
open Projection.Cli

/// #17 — the `--query` lens redeems the structured tree `View.toJson` already
/// pays for. Discriminating predicate: the walker selects a slice of the SAME
/// document the json lens emits (one substrate), over a BOUNDED JSONPath subset —
/// object keys, array index, the wildcard, and one flat equality filter — and is
/// TOTAL: a miss yields `null`, never a throw, so it can't crash the answer it filters.

let private sample : View.View =
    View.Doc [
        View.Field("canary", "green", View.Ok)
        View.Field("drift", "3 tables", View.Warn)
        View.Field("lag", "tail end", View.Warn) ]

let private root () = View.toJson sample

let private parse (s: string) : JsonElement = JsonDocument.Parse(s).RootElement.Clone()

[<Fact>]
let ``Query: an object key selects that member — kind walks to the doc tag (leading dot optional)`` () =
    Assert.Equal("doc", (parse (Query.render "kind" (root ()))).GetString())
    Assert.Equal("doc", (parse (Query.render ".kind" (root ()))).GetString())   // jq habit tolerated

[<Fact>]
let ``Query: array index then key — blocks[0].value reaches the first field's value`` () =
    Assert.Equal("green", (parse (Query.render "blocks[0].value" (root ()))).GetString())

[<Fact>]
let ``Query: a flat equality filter selects matching elements — [?status=warn] keeps the two warns`` () =
    let j = parse (Query.render "blocks[?status=warn]" (root ()))
    Assert.Equal(JsonValueKind.Array, j.ValueKind)
    let labels =
        j.EnumerateArray() |> Seq.map (fun e -> nonNull (e.GetProperty("label").GetString())) |> Seq.toList
    Assert.Equal<string list>([ "drift"; "lag" ], labels)

[<Fact>]
let ``Query: a key after a filter MAPS over the survivors — [?status=warn].value collects both values`` () =
    let j = parse (Query.render "blocks[?status=warn].value" (root ()))
    let vals = j.EnumerateArray() |> Seq.map (fun e -> nonNull (e.GetString())) |> Seq.toList
    Assert.Equal<string list>([ "3 tables"; "tail end" ], vals)

[<Fact>]
let ``Query: the wildcard expands an array — blocks[] yields every block`` () =
    let j = parse (Query.render "blocks[]" (root ()))
    Assert.Equal(JsonValueKind.Array, j.ValueKind)
    Assert.Equal(3, j.EnumerateArray() |> Seq.length)

[<Fact>]
let ``Query: a single match renders the node itself, not an array — blocks[1] is the drift field`` () =
    let j = parse (Query.render "blocks[1]" (root ()))
    Assert.Equal(JsonValueKind.Object, j.ValueKind)
    Assert.Equal("drift", j.GetProperty("label").GetString())
    Assert.Equal("warn", j.GetProperty("status").GetString())

[<Fact>]
let ``Query: every miss is TOTAL — absent key, out-of-range index, key-into-scalar each yield null`` () =
    Assert.Equal("null", Query.render "nonexistent" (root ()))
    Assert.Equal("null", Query.render "blocks[9].value" (root ()))   // out-of-range index
    Assert.Equal("null", Query.render "kind.deeper" (root ()))       // key into a scalar leaf

[<Fact>]
let ``Query: the filter value may carry spaces — [?value=3 tables] stays one segment`` () =
    // The bracket-aware split keeps a spaced/dotted filter value whole; the match
    // is on the SAME scalar `toJson` emitted for that field.
    let j = parse (Query.render "blocks[?value=3 tables].label" (root ()))
    Assert.Equal("drift", j.GetString())

[<Fact>]
let ``Query: the lens is one substrate — the query walks the same toJson the json lens emits`` () =
    // The walker reads `View.toJson sample`; the node it selects carries exactly what
    // the json lens shows for that block — one document, two ways in.
    let viaQuery = parse (Query.render "blocks[2]" (root ()))
    Assert.Equal("lag", viaQuery.GetProperty("label").GetString())
    Assert.Equal("tail end", viaQuery.GetProperty("value").GetString())
