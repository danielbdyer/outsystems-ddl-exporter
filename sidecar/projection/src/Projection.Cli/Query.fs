module Projection.Cli.Query

open System.Text.Json.Nodes

/// The bounded JSONPath-subset walker over the `View.toJson` tree — the structured
/// lens `--query` redeems (#17). `View.toJson` always carries the full, uncapped
/// document; this selects a slice of it for the operator who wants one field, not
/// the whole tree. It is deliberately NOT a full JSONPath engine (the named
/// scope-creep risk, `SPECTRE_REFINEMENTS` §17 / §5): the supported grammar is the
/// shape the answer surfaces actually need —
///   - object-key access:        `kind`, `blocks`, `blocks.0.value` (a leading `.` is optional)
///   - array index:              `blocks[0]`
///   - array wildcard:           `blocks[]`            (every element)
///   - one flat equality filter: `blocks[?status=warn]`(elements whose `status` member equals `warn`)
/// Segments chain left to right; a key after a wildcard/filter MAPS over the
/// surviving elements (`blocks[?status=warn].value` → each matching block's value).
/// Every operation is TOTAL: a key miss, an out-of-range index, or a type mismatch
/// yields no match (never an exception) — the walker can never crash the answer it
/// filters. It grows at the second real query, not before.

/// A bracket operation applied to the current node(s).
type private Bracket =
    | Index of int
    | Filter of field: string * value: string
    | Wildcard

/// Split a path into segments on `.`, but never inside a `[...]` group — so a
/// dotted filter value (`[?value=dbo.Order]`) stays one segment. Empty segments
/// (a leading or doubled `.`) are dropped, so `.kind` and `kind` walk alike.
let private segmentsOf (path: string) : string list =
    let segs = System.Collections.Generic.List<string>()
    let sb = System.Text.StringBuilder()
    let mutable depth = 0
    for ch in path do
        match ch with
        | '[' -> depth <- depth + 1; sb.Append ch |> ignore
        | ']' -> depth <- max 0 (depth - 1); sb.Append ch |> ignore
        | '.' when depth = 0 -> segs.Add(sb.ToString()); sb.Clear() |> ignore
        | _ -> sb.Append ch |> ignore
    segs.Add(sb.ToString())
    segs |> Seq.filter (fun s -> s <> "") |> List.ofSeq

/// Parse a raw segment into an optional object key and an optional bracket op:
/// `blocks[0]` → (Some "blocks", Some (Index 0)); `[?status=warn]` → (None, Some Filter…);
/// `kind` → (Some "kind", None). A malformed bracket degrades to key-only (total).
let private parseSegment (seg: string) : string option * Bracket option =
    let lb = seg.IndexOf '['
    if lb < 0 then
        (if seg = "" then None else Some seg), None
    else
        let key = seg.Substring(0, lb)
        let keyOpt = if key = "" then None else Some key
        let rb = seg.IndexOf(']', lb)
        let inner = if rb > lb then seg.Substring(lb + 1, rb - lb - 1) else seg.Substring(lb + 1)
        let br =
            if inner = "" then Some Wildcard
            elif inner.StartsWith "?" then
                let body = inner.Substring 1
                let eq = body.IndexOf '='
                if eq >= 0 then Some(Filter(body.Substring(0, eq), body.Substring(eq + 1))) else None
            else
                match System.Int32.TryParse inner with
                | true, n -> Some(Index n)
                | _ -> None
        keyOpt, br

/// The unquoted scalar text of a leaf node, for filter comparison (a JSON string
/// drops its quotes; a number renders plain). A non-scalar compares as empty.
let private scalarText (node: JsonNode) : string =
    match node with
    | :? JsonValue -> node.ToString()
    | _ -> ""

/// Apply a bracket op to one node, flattening to the surviving nodes. A bracket on
/// a non-array yields nothing (total).
let private applyBracket (br: Bracket) (node: JsonNode) : JsonNode list =
    match node with
    | :? JsonArray as arr ->
        match br with
        | Index n -> if n >= 0 && n < arr.Count then arr.[n] |> Option.ofObj |> Option.toList else []
        | Wildcard -> arr |> Seq.choose Option.ofObj |> List.ofSeq
        | Filter (field, value) ->
            arr
            |> Seq.choose Option.ofObj
            |> Seq.filter (fun el ->
                match el with
                | :? JsonObject as o ->
                    match o.[field] |> Option.ofObj with
                    | Some n -> scalarText n = value
                    | None -> false
                | _ -> false)
            |> List.ofSeq
    | _ -> []

/// Walk the query over the root, returning the matched nodes — possibly several
/// (after a wildcard/filter), or none.
let walk (path: string) (root: JsonNode) : JsonNode list =
    let applySegment (nodes: JsonNode list) (seg: string) : JsonNode list =
        let keyOpt, brOpt = parseSegment seg
        let afterKey =
            match keyOpt with
            | None -> nodes
            | Some key ->
                nodes
                |> List.collect (fun node ->
                    match node with
                    | :? JsonObject as o -> o.[key] |> Option.ofObj |> Option.toList
                    | _ -> [])
        match brOpt with
        | None -> afterKey
        | Some br -> afterKey |> List.collect (applyBracket br)
    segmentsOf path |> List.fold applySegment [ root ]

/// Render the query result as JSON text — a single match verbatim, several matches
/// as a JSON array, no match as `null`. Each emitted node is deep-cloned so it is
/// detached from the source tree (System.Text.Json forbids re-parenting a node).
let render (path: string) (root: JsonNode) : string =
    match walk path root with
    | [] -> "null"
    | [ single ] -> single.DeepClone().ToJsonString()
    | many ->
        let arr = JsonArray()
        for n in many do arr.Add(n.DeepClone())
        arr.ToJsonString()
