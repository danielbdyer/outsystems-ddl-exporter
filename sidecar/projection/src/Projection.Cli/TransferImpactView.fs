module Projection.Cli.TransferImpactView

// LINT-ALLOW-FILE: this module composes the transfer-impact ARTIFACT as HTML text
//   at a terminal reporting boundary (the operator opens the file in a browser),
//   the same allowed-exception class as the planned-SQL preview and `GoBoardView`'s
//   report prose. HTML is the delivery medium, not an internal IR — there is no
//   downstream consumer to keep typed. The machine contract is the JSON twin
//   (`toJson`), built through `System.Text.Json.Nodes` (a typed node tree, not
//   string concatenation). Values are HTML-escaped at the single `esc` seam.

open System.Text
open System.Text.Json.Nodes
open Projection.Core
open Projection.Pipeline

let private esc (s: string) : string =
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

let private nameOf (catalog: Catalog) (kind: SsKey) : string =
    match Catalog.tryFindKind kind catalog with
    | Some k -> Name.value k.Name
    | None   -> SsKey.rootOriginal kind

let private changeClass (c: TransferImpact.ChangeKind) : string =
    match c with
    | TransferImpact.ChangeKind.Added     -> "add"
    | TransferImpact.ChangeKind.Deleted   -> "del"
    | TransferImpact.ChangeKind.Changed   -> "chg"
    | TransferImpact.ChangeKind.Unchanged -> "keep"

let private changeLabel (c: TransferImpact.ChangeKind) : string =
    match c with
    | TransferImpact.ChangeKind.Added     -> "added"
    | TransferImpact.ChangeKind.Deleted   -> "deleted by wipe"
    | TransferImpact.ChangeKind.Changed   -> "changed"
    | TransferImpact.ChangeKind.Unchanged -> "unchanged"

// -- the JSON twin (the machine contract) ----------------------------------

let rec private nodeJson (catalog: Catalog) (n: TransferImpact.EntityNode) : JsonNode =
    let o = JsonObject()
    o.["kind"] <- JsonValue.Create (nameOf catalog n.Kind)
    o.["key"] <- JsonValue.Create n.KeyValue
    o.["change"] <- JsonValue.Create (changeLabel n.Change)
    let attrs = JsonObject()
    for (c, v) in n.Attributes do attrs.[Name.value c] <- JsonValue.Create v
    o.["attributes"] <- attrs
    if not (List.isEmpty n.Diffs) then
        let da = JsonArray()
        for d in n.Diffs do
            let dj = JsonObject()
            dj.["column"] <- JsonValue.Create (Name.value d.Column)
            dj.["before"] <- JsonValue.Create d.Before
            dj.["after"] <- JsonValue.Create d.After
            da.Add dj
        o.["diffs"] <- da
    if not (List.isEmpty n.Refs) then
        let ra = JsonArray()
        for (label, disp) in n.Refs do
            let rj = JsonObject()
            rj.["relationship"] <- JsonValue.Create label
            rj.["parent"] <- JsonValue.Create disp
            ra.Add rj
        o.["references"] <- ra
    if not (List.isEmpty n.Children) then
        let ca = JsonArray()
        for (label, kids) in n.Children do
            let cj = JsonObject()
            cj.["relationship"] <- JsonValue.Create label
            let ka = JsonArray()
            for k in kids do ka.Add (nodeJson catalog k)
            cj.["rows"] <- ka
            ca.Add cj
        o.["children"] <- ca
    o :> JsonNode

let toJson (catalog: Catalog) (impact: TransferImpact.Impact) : string =
    let root = JsonObject()
    root.["flow"] <- JsonValue.Create impact.Flow
    root.["strategy"] <- JsonValue.Create impact.Strategy
    let t = JsonObject()
    t.["added"] <- JsonValue.Create impact.Totals.Added
    t.["deleted"] <- JsonValue.Create impact.Totals.Deleted
    t.["changed"] <- JsonValue.Create impact.Totals.Changed
    t.["unchanged"] <- JsonValue.Create impact.Totals.Unchanged
    root.["totals"] <- t
    let segs = JsonArray()
    for s in impact.Segments do
        let sj = JsonObject()
        sj.["members"] <- (let a = JsonArray() in (for m in s.Members do a.Add (JsonValue.Create (nameOf catalog m))); a)
        sj.["roots"] <- (let a = JsonArray() in (for r in s.Roots do a.Add (JsonValue.Create (nameOf catalog r))); a)
        let ctx = JsonArray()
        for c in s.Context do
            let cj = JsonObject()
            cj.["table"] <- JsonValue.Create (nameOf catalog c.Kind)
            cj.["sinkBefore"] <- JsonValue.Create c.SinkBefore
            cj.["added"] <- JsonValue.Create c.Added
            cj.["deleted"] <- JsonValue.Create c.Deleted
            cj.["changed"] <- JsonValue.Create c.Changed
            cj.["unchanged"] <- JsonValue.Create c.Unchanged
            ctx.Add cj
        sj.["context"] <- ctx
        let docs = JsonArray()
        for d in s.Documents do docs.Add (nodeJson catalog d)
        sj.["documents"] <- docs
        segs.Add sj
    root.["segments"] <- segs
    root.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true))

// -- the HTML artifact ------------------------------------------------------

let private attrsText (n: TransferImpact.EntityNode) : string =
    n.Attributes |> List.map (fun (_, v) -> esc v) |> String.concat " · "

let rec private nodeHtml (catalog: Catalog) (sb: StringBuilder) (n: TransferImpact.EntityNode) : unit =
    let cls = changeClass n.Change
    sb.Append(sprintf "<div class=\"doc %s\">" cls) |> ignore
    sb.Append("<div class=\"doc-head\">") |> ignore
    sb.Append(sprintf "<span class=\"ent\"><span class=\"id\">%s #%s</span> <span class=\"attrs\">%s</span></span>"
                (esc (nameOf catalog n.Kind)) (esc n.KeyValue) (attrsText n)) |> ignore
    sb.Append(sprintf "<span class=\"badge %s\">%s</span>" cls (changeLabel n.Change)) |> ignore
    sb.Append("</div>") |> ignore
    let hasNested = not (List.isEmpty n.Diffs) || not (List.isEmpty n.Refs) || not (List.isEmpty n.Children)
    if hasNested then
        sb.Append("<div class=\"children\">") |> ignore
        for d in n.Diffs do
            sb.Append(sprintf "<div class=\"diff\">%s&nbsp; <span class=\"was\">%s</span> &rarr; <span class=\"now\">%s</span></div>"
                        (esc (Name.value d.Column)) (esc d.Before) (esc d.After)) |> ignore
        for (label, disp) in n.Refs do
            sb.Append(sprintf "<div class=\"rel-label\">references &rarr; %s</div>" (esc label)) |> ignore
            sb.Append(sprintf "<div class=\"ref\">%s</div>" (esc disp)) |> ignore
        for (label, kids) in n.Children do
            sb.Append(sprintf "<div class=\"rel-label\">owned children &rarr; %s</div>" (esc label)) |> ignore
            for k in kids do nodeHtml catalog sb k
        sb.Append("</div>") |> ignore
    sb.Append("</div>") |> ignore

let private segmentHtml (catalog: Catalog) (sb: StringBuilder) (openFirst: bool) (s: TransferImpact.Segment) : unit =
    let rootName = s.Roots |> List.map (nameOf catalog) |> String.concat ", "
    let path = s.Members |> List.map (nameOf catalog) |> String.concat " ▸ "
    let tallies =
        s.Context
        |> List.fold (fun (a, d, c) x -> a + x.Added, d + x.Deleted, c + x.Changed) (0, 0, 0)
    let (ta, td, tc) = tallies
    sb.Append(sprintf "<details class=\"segment\"%s>" (if openFirst then " open" else "")) |> ignore
    sb.Append("<summary>") |> ignore
    sb.Append("<svg class=\"caret\" width=\"12\" height=\"12\" viewBox=\"0 0 12 12\" aria-hidden=\"true\"><path d=\"M4 2l5 4-5 4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>") |> ignore
    sb.Append(sprintf "<span><span class=\"seg-title\">%s</span> <span class=\"seg-path\">%s</span></span>" (esc rootName) (esc path)) |> ignore
    sb.Append("<span class=\"seg-tally\">") |> ignore
    if ta > 0 then sb.Append(sprintf "<b class=\"t-add\">+%d</b>" ta) |> ignore
    if td > 0 then sb.Append(sprintf "<b class=\"t-del\">&minus;%d</b>" td) |> ignore
    if tc > 0 then sb.Append(sprintf "<b class=\"t-chg\">~%d</b>" tc) |> ignore
    if ta = 0 && td = 0 && tc = 0 then sb.Append("<b style=\"color:var(--accent)\">no change</b>") |> ignore
    sb.Append("</span></summary>") |> ignore
    sb.Append("<div class=\"seg-body\">") |> ignore
    sb.Append("<div class=\"ctx\">") |> ignore
    for c in s.Context do
        sb.Append(sprintf "<span><span class=\"k\">%s</span> — %d on sink · +%d &minus;%d ~%d · %d unchanged</span>"
                    (esc (nameOf catalog c.Kind)) c.SinkBefore c.Added c.Deleted c.Changed c.Unchanged) |> ignore
    sb.Append("</div>") |> ignore
    for d in s.Documents do nodeHtml catalog sb d
    // the unchanged remainder, counted (never listed)
    let unchanged = s.Context |> List.sumBy (fun c -> c.Unchanged)
    if unchanged > 0 then
        sb.Append(sprintf "<div class=\"more\">+ %d unchanged row(s) across this segment not listed.</div>" unchanged) |> ignore
    sb.Append("</div></details>") |> ignore

let private css = """
:root{--bg:#f4f6f8;--surface:#fff;--surface-2:#eef1f5;--inset:#f8fafc;--ink:#161b24;--ink-soft:#586173;--ink-faint:#8a93a3;--line:#dce1ea;--accent:#1f8f80;--add:#14804a;--add-bg:#e7f4ec;--add-line:#57c78a;--del:#b3243b;--del-bg:#fbe9ec;--del-line:#e78798;--chg:#8a5d0e;--chg-bg:#f8efd8;--chg-line:#d9b25e;--keep:#8a93a3;--keep-bg:#f1f3f6;--sans:ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;--mono:ui-monospace,"SF Mono","JetBrains Mono",Menlo,Consolas,monospace;--r:10px;--r-sm:7px}
@media(prefers-color-scheme:dark){:root{--bg:#0d1117;--surface:#161c25;--surface-2:#1d2530;--inset:#12171f;--ink:#e7ebf2;--ink-soft:#9aa4b3;--ink-faint:#697485;--line:#27303d;--accent:#36b3a3;--add:#54d089;--add-bg:#11301e;--add-line:#2f7a51;--del:#f5788a;--del-bg:#341219;--del-line:#8a3242;--chg:#e2b45f;--chg-bg:#33280f;--chg-line:#7a5f28;--keep:#6b7686;--keep-bg:#1a212b}}
:root[data-theme=dark]{--bg:#0d1117;--surface:#161c25;--surface-2:#1d2530;--inset:#12171f;--ink:#e7ebf2;--ink-soft:#9aa4b3;--ink-faint:#697485;--line:#27303d;--accent:#36b3a3;--add:#54d089;--add-bg:#11301e;--add-line:#2f7a51;--del:#f5788a;--del-bg:#341219;--del-line:#8a3242;--chg:#e2b45f;--chg-bg:#33280f;--chg-line:#7a5f28;--keep:#6b7686;--keep-bg:#1a212b}
:root[data-theme=light]{--bg:#f4f6f8;--surface:#fff;--surface-2:#eef1f5;--inset:#f8fafc;--ink:#161b24;--ink-soft:#586173;--ink-faint:#8a93a3;--line:#dce1ea;--accent:#1f8f80;--add:#14804a;--add-bg:#e7f4ec;--add-line:#57c78a;--del:#b3243b;--del-bg:#fbe9ec;--del-line:#e78798;--chg:#8a5d0e;--chg-bg:#f8efd8;--chg-line:#d9b25e;--keep:#8a93a3;--keep-bg:#f1f3f6}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--sans);font-size:15px;line-height:1.55}
.wrap{max-width:1080px;margin:0 auto;padding:32px 24px 80px}
.mono,.mono *{font-family:var(--mono);font-variant-numeric:tabular-nums}
.masthead{border:1px solid var(--line);background:var(--surface);border-radius:var(--r);padding:22px 24px;margin-bottom:22px}
.eyebrow{font-family:var(--mono);font-size:11.5px;letter-spacing:.14em;text-transform:uppercase;color:var(--accent);font-weight:600;margin-bottom:9px}
h1{font-size:26px;font-weight:680;letter-spacing:-.01em;margin:0}
.flow-path{font-family:var(--mono);font-size:14px;color:var(--ink-soft)}.flow-path b{color:var(--ink)}
.title-row{display:flex;flex-wrap:wrap;align-items:baseline;gap:12px}
.meta{display:flex;flex-wrap:wrap;gap:8px 10px;margin-top:14px}
.chip{font-size:12.5px;padding:3px 10px;border-radius:999px;border:1px solid var(--line);background:var(--surface-2);color:var(--ink-soft)}.chip b{color:var(--ink)}
.tiles{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:22px 0}
@media(max-width:640px){.tiles{grid-template-columns:repeat(2,1fr)}}
.tile{border:1px solid var(--line);border-radius:var(--r-sm);background:var(--surface);padding:14px 15px;border-left-width:3px}
.tile .n{font-family:var(--mono);font-size:25px;font-weight:650;letter-spacing:-.02em;line-height:1.1}.tile .l{font-size:12px;color:var(--ink-soft);margin-top:3px}
.tile.add{border-left-color:var(--add-line)}.tile.add .n{color:var(--add)}.tile.del{border-left-color:var(--del-line)}.tile.del .n{color:var(--del)}
.tile.chg{border-left-color:var(--chg-line)}.tile.chg .n{color:var(--chg)}.tile.keep .n{color:var(--ink-soft)}
.legend{display:flex;flex-wrap:wrap;gap:14px;margin:0 2px 20px;font-size:12.5px;color:var(--ink-soft)}
.legend span{display:inline-flex;align-items:center;gap:6px}.swatch{width:11px;height:11px;border-radius:3px}
.segment{border:1px solid var(--line);border-radius:var(--r);background:var(--surface);margin-bottom:16px;overflow:hidden}
.segment>summary{list-style:none;cursor:pointer;padding:15px 18px;display:flex;align-items:center;gap:12px}
.segment>summary::-webkit-details-marker{display:none}
.segment>summary:focus-visible{outline:2px solid var(--accent);outline-offset:-2px}
.caret{transition:transform .18s;color:var(--ink-faint)}.segment[open] .caret{transform:rotate(90deg)}
.seg-title{font-weight:640;font-size:15.5px}.seg-path{font-family:var(--mono);font-size:12.5px;color:var(--ink-faint)}
.seg-tally{margin-left:auto;display:flex;gap:8px;font-family:var(--mono);font-size:12.5px}
.t-add{color:var(--add)}.t-del{color:var(--del)}.t-chg{color:var(--chg)}
.seg-body{padding:4px 18px 20px;border-top:1px solid var(--line)}
.ctx{display:flex;flex-wrap:wrap;gap:6px 16px;margin:14px 2px 18px;font-size:12.5px;color:var(--ink-soft)}.ctx .k{font-family:var(--mono);color:var(--ink)}
.doc{border:1px solid var(--line);border-radius:var(--r-sm);margin:10px 0;border-left-width:3px;background:var(--inset)}
.doc.add{border-left-color:var(--add-line)}.doc.del{border-left-color:var(--del-line)}.doc.chg{border-left-color:var(--chg-line)}.doc.keep{border-left-color:var(--line)}
.doc-head{display:flex;flex-wrap:wrap;align-items:baseline;gap:9px;padding:11px 14px}
.ent{font-family:var(--mono);font-size:13.5px;color:var(--ink)}.ent .id{color:var(--accent);font-weight:600}.ent .attrs{color:var(--ink-soft)}
.badge{font-size:11px;font-weight:650;letter-spacing:.03em;text-transform:uppercase;padding:2px 8px;border-radius:999px}
.badge.add{color:var(--add);background:var(--add-bg)}.badge.del{color:var(--del);background:var(--del-bg)}.badge.chg{color:var(--chg);background:var(--chg-bg)}.badge.keep{color:var(--keep);background:var(--keep-bg)}
.children{margin:0 0 10px 14px;padding-left:16px;border-left:1.5px dashed var(--line)}
.rel-label{font-family:var(--mono);font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:var(--ink-faint);margin:8px 0 3px}
.ref{font-family:var(--mono);font-size:12px;color:var(--ink-soft)}
.diff{font-family:var(--mono);font-size:12px;margin:2px 0 6px}
.diff .was{color:var(--del);text-decoration:line-through}.diff .now{color:var(--add)}
.more{font-size:12.5px;color:var(--ink-faint);padding:6px 2px 2px;font-style:italic}
footer{margin-top:26px;font-size:12px;color:var(--ink-faint);font-family:var(--mono);display:flex;flex-wrap:wrap;gap:6px 14px}
"""

/// Render the impact model to the self-contained HTML artifact.
let toHtml (catalog: Catalog) (impact: TransferImpact.Impact) : string =
    let t = impact.Totals
    let sb = StringBuilder()
    sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">") |> ignore
    sb.Append(sprintf "<title>Transfer impact — %s</title>" (esc impact.Flow)) |> ignore
    sb.Append("<style>").Append(css).Append("</style></head><body><div class=\"wrap\">") |> ignore
    // masthead
    sb.Append("<header class=\"masthead\"><div class=\"eyebrow\">Transfer impact · dry run · zero writes</div>") |> ignore
    sb.Append(sprintf "<div class=\"title-row\"><h1>%s</h1></div>" (esc impact.Flow)) |> ignore
    sb.Append(sprintf "<div class=\"meta\"><span class=\"chip\">strategy <b>%s</b></span><span class=\"chip\">segments <b>%d</b></span></div></header>"
                (esc impact.Strategy) (List.length impact.Segments)) |> ignore
    // tiles
    sb.Append("<section class=\"tiles\">") |> ignore
    sb.Append(sprintf "<div class=\"tile add\"><div class=\"n\">+%d</div><div class=\"l\">rows added</div></div>" t.Added) |> ignore
    sb.Append(sprintf "<div class=\"tile del\"><div class=\"n\">&minus;%d</div><div class=\"l\">rows deleted</div></div>" t.Deleted) |> ignore
    sb.Append(sprintf "<div class=\"tile chg\"><div class=\"n\">~%d</div><div class=\"l\">rows changed</div></div>" t.Changed) |> ignore
    sb.Append(sprintf "<div class=\"tile keep\"><div class=\"n\">%d</div><div class=\"l\">unchanged</div></div></section>" t.Unchanged) |> ignore
    // legend
    sb.Append("<div class=\"legend\"><span><span class=\"swatch\" style=\"background:var(--add-line)\"></span> added</span><span><span class=\"swatch\" style=\"background:var(--del-line)\"></span> deleted by wipe</span><span><span class=\"swatch\" style=\"background:var(--chg-line)\"></span> changed</span><span><span class=\"swatch\" style=\"background:var(--line)\"></span> unchanged (counted, not listed)</span></div>") |> ignore
    // segments
    impact.Segments |> List.iteri (fun i s -> segmentHtml catalog sb (i = 0) s)
    sb.Append(sprintf "<footer><span>projection check go %s --impact</span><span>·</span><span>go-board/%s.impact.html</span></footer>" (esc impact.Flow) (esc impact.Flow)) |> ignore
    sb.Append("</div></body></html>") |> ignore
    sb.ToString()
