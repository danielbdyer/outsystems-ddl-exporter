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

/// One summary row's machine form — shared by the plain and triaged twins.
let private summaryRowJson (catalog: Catalog) (r: TransferImpact.SummaryRow) : JsonObject =
    let rj = JsonObject()
    rj.["table"] <- JsonValue.Create (nameOf catalog r.Kind)
    rj.["role"] <- JsonValue.Create r.Role.Variety
    rj.["reason"] <- JsonValue.Create r.Role.Reason
    if r.Role.Guarantee <> "" then rj.["guarantee"] <- JsonValue.Create r.Role.Guarantee
    r.Role.Key |> Option.iter (fun k -> rj.["matchedBy"] <- JsonValue.Create k)
    r.Role.Verdict |> Option.iter (fun v -> rj.["verdict"] <- JsonValue.Create v)
    rj.["sinkBefore"] <- JsonValue.Create r.Context.SinkBefore
    rj.["added"] <- JsonValue.Create r.Context.Added
    rj.["deleted"] <- JsonValue.Create r.Context.Deleted
    rj.["changed"] <- JsonValue.Create r.Context.Changed
    rj.["unchanged"] <- JsonValue.Create r.Context.Unchanged
    rj

/// One segment's machine form — shared by the plain and triaged twins.
let private segmentJson (catalog: Catalog) (s: TransferImpact.Segment) : JsonObject =
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
    sj

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
    let sum = JsonArray()
    for r in impact.Summary do
        sum.Add (summaryRowJson catalog r)
    root.["summary"] <- sum
    let segs = JsonArray()
    for s in impact.Segments do
        segs.Add (segmentJson catalog s)
    root.["segments"] <- segs
    root.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true))

/// The TRIAGED machine twin (2026-07-10, the manifest program, slice 1):
/// the same document as `toJson`, with the segments in RANK order and each
/// carrying its `triage` class token and `couplingWeight`. Uncapped — every
/// unit is present regardless of how the pretty artifact folds (the
/// one-substrate law: the machine lens never loses what the human collapsed).
let toJsonTriaged (catalog: Catalog) (units: TransferTriage.TransferUnit list) (impact: TransferImpact.Impact) : string =
    let root = JsonObject()
    root.["flow"] <- JsonValue.Create impact.Flow
    root.["strategy"] <- JsonValue.Create impact.Strategy
    let t = JsonObject()
    t.["added"] <- JsonValue.Create impact.Totals.Added
    t.["deleted"] <- JsonValue.Create impact.Totals.Deleted
    t.["changed"] <- JsonValue.Create impact.Totals.Changed
    t.["unchanged"] <- JsonValue.Create impact.Totals.Unchanged
    root.["totals"] <- t
    let sum = JsonArray()
    for r in impact.Summary do
        sum.Add (summaryRowJson catalog r)
    root.["summary"] <- sum
    let segs = JsonArray()
    for u in units do
        let sj = segmentJson catalog u.Segment
        sj.["triage"] <- JsonValue.Create (TransferTriage.token u.Triage)
        sj.["couplingWeight"] <- JsonValue.Create u.CouplingWeight
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

let private segmentHtml (catalog: Catalog) (sb: StringBuilder) (openFirst: bool) (badge: string option) (s: TransferImpact.Segment) : unit =
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
    // The triage badge (2026-07-10, the manifest program): the unit's class,
    // stated on the summary line so a folded unit reads its verdict at a glance.
    (match badge with
     | Some b -> sb.Append(sprintf "<span class=\"chip\">%s</span>" (esc b)) |> ignore
     | None -> ())
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
:root{--bg:#f4f6f8;--surface:#fff;--surface-2:#eef1f5;--inset:#f8fafc;--ink:#161b24;--ink-soft:#586173;--ink-faint:#8a93a3;--line:#dce1ea;--accent:#1f8f80;--accent-soft:#e6f3f1;--add:#14804a;--add-bg:#e7f4ec;--add-line:#57c78a;--del:#b3243b;--del-bg:#fbe9ec;--del-line:#e78798;--chg:#8a5d0e;--chg-bg:#f8efd8;--chg-line:#d9b25e;--keep:#8a93a3;--keep-bg:#f1f3f6;--sans:ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;--mono:ui-monospace,"SF Mono","JetBrains Mono",Menlo,Consolas,monospace;--r:10px;--r-sm:7px}
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
h2{font-size:15px;font-weight:660;letter-spacing:-.01em;margin:30px 0 4px;display:flex;align-items:baseline;gap:10px}
h2 .sub{font-weight:400;font-size:12.5px;color:var(--ink-faint);font-family:var(--mono)}
.hrule{height:1px;background:var(--line);margin:10px 0 16px}
.headline{display:flex;gap:12px;align-items:flex-start;margin-top:16px;padding:13px 15px;border-radius:var(--r-sm);background:var(--accent-soft);border:1px solid var(--accent)}
.headline .dot{width:10px;height:10px;border-radius:50%;background:var(--accent);margin-top:5px;flex:none}.headline p{margin:0;font-size:13.5px}.headline b{font-weight:650}
.matrix{border:1px solid var(--line);border-radius:var(--r);overflow:hidden;background:var(--surface)}
table{border-collapse:collapse;width:100%;font-size:13px}
thead th{text-align:left;font-weight:600;font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:var(--ink-faint);padding:9px 14px;background:var(--surface-2);border-bottom:1px solid var(--line)}
th.num,td.num{text-align:right;font-family:var(--mono);font-variant-numeric:tabular-nums}
tbody td{padding:8px 14px;border-bottom:1px solid var(--line);vertical-align:baseline}tbody tr:last-child td{border-bottom:none}
.grp td{background:var(--inset);font-weight:640;font-size:12px;padding:7px 14px;color:var(--ink)}.grp .cnt{color:var(--ink-faint);font-weight:400;font-family:var(--mono)}
.tname{font-family:var(--mono);font-size:12.5px;color:var(--ink)}.rsn{color:var(--ink-soft);font-size:12.5px}.key{font-family:var(--mono);font-size:12px;color:var(--ink-soft)}
.verdict{font-family:var(--mono);font-size:12px;font-weight:600;white-space:nowrap}
.v-ok{color:var(--accent)}.v-add{color:var(--add)}.v-del{color:var(--del)}.v-chg{color:var(--chg)}.v-drift{color:var(--del)}
.rchip{font-size:11px;padding:1.5px 8px;border-radius:999px;border:1px solid var(--line);background:var(--surface-2);color:var(--ink-soft);font-family:var(--mono)}
.intent-grid{display:grid;grid-template-columns:1fr 1fr;gap:12px}@media(max-width:720px){.intent-grid{grid-template-columns:1fr}}
.intent{border:1px solid var(--line);border-radius:var(--r-sm);background:var(--surface);padding:13px 15px;border-left-width:3px;border-left-color:var(--accent)}
.intent .v{font-family:var(--mono);font-size:12px;font-weight:650;color:var(--accent);text-transform:uppercase;letter-spacing:.04em}
.intent .tbls{font-family:var(--mono);font-size:11.5px;color:var(--ink-faint);margin:2px 0 8px}
.intent .g{font-size:12.5px;color:var(--ink);margin:0 0 7px}
.intent .why{font-size:12px;color:var(--ink-soft);border-top:1px dashed var(--line);padding-top:7px}.intent .why b{color:var(--ink);font-weight:600}
.confirm{border:1px solid var(--accent);border-radius:var(--r);background:var(--surface);overflow:hidden}
.confirm-head{display:flex;align-items:center;gap:11px;padding:13px 16px;background:var(--accent-soft)}.confirm-head .dot{width:9px;height:9px;border-radius:50%;background:var(--accent)}.confirm-head b{font-weight:650}.confirm-head span{color:var(--ink-soft);font-size:12.5px}
.confirm-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:1px;background:var(--line)}
.cf{background:var(--surface);padding:9px 13px;display:flex;justify-content:space-between;align-items:baseline;gap:8px}.cf .t{font-family:var(--mono);font-size:12.5px}.cf .r{font-family:var(--mono);font-size:11.5px;color:var(--accent);white-space:nowrap}.cf.drift .r{color:var(--del)}
"""

// -- the scale surfaces: summary matrix, relational intent, 1:1 confirmation --

/// The risk each relational check defends against — the "why the 1:1 mattered"
/// prose (reporting copy; the file's LINT-ALLOW covers it).
let private whyCheck (variety: string) : string =
    match variety with
    | "static-lookup" -> "The payload's foreign keys resolve against this table BY THE NATURAL KEY. A code present on one side only — or the same code carrying a different value — silently re-points a moved row to the wrong reference. The 1:1 proof is what makes the re-key safe."
    | "existing-reference" -> "These rows belong to the TARGET, not the source. Every payload foreign key must find its match here or it would dangle (a 547). The match count is the proof that every reference lands."
    | "reference-seed" -> "Only the rows the target LACKS insert. The check confirms the overlap is matched and only the gap is written — so the seed can neither duplicate nor overwrite governed data."
    | "shared-anchor" -> "Every payload reference collapses onto one designated target row. The check proves the anchor exists and no divergent reference survives the load."
    | "owned-child" -> "The child rides the parent's wipe. The cascade edge is verified so a replace deletes child-first and re-inserts the parent and its children as one unit."
    | "blocked-dependent" -> "A real dependent, deliberately NOT harvested — the exclusion is acknowledged so a replace-wipe does not refuse on its account, and no environment-specific row is copied."
    | _ -> ""

let private groupLabel (variety: string) : string =
    match variety with
    | "payload" -> "Payload — the tracked tables you're moving"
    | "static-lookup" -> "Static lookup — reference data the environments must hold IDENTICALLY"
    | "existing-reference" -> "Existing reference — matched to the target's OWN rows; never copied"
    | "reference-seed" -> "Reference seed — copy only the rows the target lacks"
    | "shared-anchor" -> "Shared anchor — every reference re-pointed to one target row"
    | "owned-child" -> "Owned child — copied with the parent, wiped with it under replace"
    | "blocked-dependent" -> "Blocked dependent — a real dependent, deliberately not harvested"
    | other -> other

let private verdictCell (r: TransferImpact.SummaryRow) : string =
    match r.Role.Verdict with
    | Some v ->
        let drift = [ "drift"; "diverge"; "extra"; "missing"; "⚠" ] |> List.exists v.Contains
        sprintf "<span class=\"verdict %s\">%s</span>" (if drift then "v-drift" else "v-ok") (esc v)
    | None ->
        let c = r.Context
        if c.Added = 0 && c.Deleted = 0 && c.Changed = 0 then "<span class=\"verdict\" style=\"color:var(--ink-faint)\">unchanged</span>"
        else
            let parts =
                [ if c.Added > 0 then yield sprintf "+%d" c.Added
                  if c.Deleted > 0 then yield sprintf "&minus;%d" c.Deleted
                  if c.Changed > 0 then yield sprintf "~%d" c.Changed ]
            sprintf "<span class=\"verdict v-add\">%s</span>" (String.concat " " parts)

/// The summary matrix: every table grouped by relational role.
let private summaryHtml (catalog: Catalog) (sb: StringBuilder) (summary: TransferImpact.SummaryRow list) : unit =
    sb.Append("<h2>Every table, by relational role <span class=\"sub\">tracked + supporting · grouped by why it's in scope</span></h2><div class=\"hrule\"></div>") |> ignore
    sb.Append("<div class=\"matrix\"><div style=\"overflow-x:auto\"><table><thead><tr><th>Table</th><th>Role</th><th class=\"num\">Rows (sink)</th><th>Verdict</th><th>Matched by</th><th>Why it's in scope</th></tr></thead><tbody>") |> ignore
    summary
    |> List.groupBy (fun r -> r.Role.Variety)
    |> List.iter (fun (variety, rows) ->
        sb.Append(sprintf "<tr class=\"grp\"><td colspan=\"6\">%s <span class=\"cnt\">· %d table(s)</span></td></tr>" (esc (groupLabel variety)) (List.length rows)) |> ignore
        for r in rows do
            let key = r.Role.Key |> Option.map esc |> Option.defaultValue "—"
            sb.Append(sprintf "<tr><td class=\"tname\">%s</td><td><span class=\"rchip\">%s</span></td><td class=\"num\">%d</td><td>%s</td><td class=\"key\">%s</td><td class=\"rsn\">%s</td></tr>"
                        (esc (nameOf catalog r.Kind)) (esc variety) r.Context.SinkBefore (verdictCell r) key (esc r.Role.Reason)) |> ignore)
    sb.Append("</tbody></table></div></div>") |> ignore

/// The relational-intent cards: one per non-payload variety present.
let private intentHtml (catalog: Catalog) (sb: StringBuilder) (summary: TransferImpact.SummaryRow list) : unit =
    let byVariety =
        summary
        |> List.filter (fun r -> r.Role.Variety <> "payload")
        |> List.groupBy (fun r -> r.Role.Variety)
    if not (List.isEmpty byVariety) then
        sb.Append("<h2>Relational intent <span class=\"sub\">what each role guarantees — and why the check was necessary</span></h2><div class=\"hrule\"></div><div class=\"intent-grid\">") |> ignore
        for (variety, rows) in byVariety do
            let guarantee = rows |> List.tryPick (fun r -> if r.Role.Guarantee <> "" then Some r.Role.Guarantee else None) |> Option.defaultValue ""
            let tables = rows |> List.map (fun r -> nameOf catalog r.Kind) |> List.truncate 6 |> String.concat " · "
            sb.Append("<div class=\"intent\">") |> ignore
            sb.Append(sprintf "<div class=\"v\">%s</div><div class=\"tbls\">%s%s</div>" (esc variety) (esc tables) (if List.length rows > 6 then sprintf " +%d" (List.length rows - 6) else "")) |> ignore
            if guarantee <> "" then sb.Append(sprintf "<p class=\"g\">%s</p>" (esc guarantee)) |> ignore
            sb.Append(sprintf "<div class=\"why\"><b>Why check?</b> %s</div>" (esc (whyCheck variety))) |> ignore
            sb.Append("</div>") |> ignore
        sb.Append("</div>") |> ignore

/// The 1:1 confirmation panel: the reference/lookup kinds with an identity verdict.
let private confirmHtml (catalog: Catalog) (sb: StringBuilder) (summary: TransferImpact.SummaryRow list) : unit =
    let verified = summary |> List.filter (fun r -> Option.isSome r.Role.Verdict)
    if not (List.isEmpty verified) then
        let drifted = verified |> List.filter (fun r -> [ "drift"; "diverge"; "extra"; "missing"; "⚠" ] |> List.exists (r.Role.Verdict |> Option.defaultValue "").Contains)
        let clean = List.length verified - List.length drifted
        sb.Append("<h2>Reference data — the 1:1 confirmation <span class=\"sub\">the matched/identical verdict, per table</span></h2><div class=\"hrule\"></div><div class=\"confirm\">") |> ignore
        sb.Append(sprintf "<div class=\"confirm-head\"><span class=\"dot\"></span><b>%d of %d reference table(s) verified</b><span>— matched by natural key; a static-lookup additionally holds every column identical, with no extra or missing rows.</span></div>" clean (List.length verified)) |> ignore
        sb.Append("<div class=\"confirm-grid\">") |> ignore
        for r in verified do
            let isDrift = [ "drift"; "diverge"; "extra"; "missing"; "⚠" ] |> List.exists (r.Role.Verdict |> Option.defaultValue "").Contains
            sb.Append(sprintf "<div class=\"cf%s\"><span class=\"t\">%s</span><span class=\"r\">%s</span></div>" (if isDrift then " drift" else "") (esc (nameOf catalog r.Kind)) (esc (Option.defaultValue "" r.Role.Verdict))) |> ignore
        sb.Append("</div></div>") |> ignore

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
    // SCALE SURFACES (2026-07-09): summary-first so the variety is legible before
    // the detail — the matrix, the relational intent, the 1:1 confirmation.
    summaryHtml catalog sb impact.Summary
    intentHtml catalog sb impact.Summary
    confirmHtml catalog sb impact.Summary
    // The denormalized detail — the changed segments, COLLAPSED (open the ones you
    // want; unchanged tables are counted in the matrix, never listed here).
    let changedSegments = impact.Segments |> List.filter (fun s -> not (List.isEmpty s.Documents))
    if not (List.isEmpty changedSegments) then
        sb.Append("<h2>Changed data, denormalized <span class=\"sub\">owned children conjoined under each root; unchanged rows counted, not listed</span></h2><div class=\"hrule\"></div>") |> ignore
        changedSegments |> List.iter (segmentHtml catalog sb false None)
    sb.Append(sprintf "<footer><span>projection check go %s --impact</span><span>·</span><span>go-board/%s.impact.html</span></footer>" (esc impact.Flow) (esc impact.Flow)) |> ignore
    sb.Append("</div></body></html>") |> ignore
    sb.ToString()

// -- the TRIAGED artifact (2026-07-10, the manifest program, slice 1) --------
// The same masthead/tiles/summary surfaces, but the detail section is triaged
// by coupling: open units first (the top-ranked one revealed), each settled
// unit ONE folded line with its proof beneath the fold, and a cap-and-named
// settled tail so a large estate never scrolls — every row still counted (the
// fold hides scroll, never tally; the machine twin carries every unit).

/// The one-line verdict a triage class states on its unit's summary line.
/// THE_VOICE: plain and exacting — the precise mechanism, never an
/// abstraction; the true verb (inserted / deleted / written), never a
/// softener or a figure.
let private triageBadge (t: TransferTriage.TriageClass) : string =
    match t with
    | TransferTriage.TriageClass.SettledStatic   -> "source and target hold the same rows, verified column by column — nothing is written"
    | TransferTriage.TriageClass.SettledClosed   -> "each source row pairs with a row the target already holds, matched by its business column — nothing is inserted or deleted"
    | TransferTriage.TriageClass.SettledNoop     -> "no rows are added, deleted, or changed"
    | TransferTriage.TriageClass.OpenEscaping    -> "a column points at a table outside the transfer — a decision is required"
    | TransferTriage.TriageClass.OpenDestructive -> "rows are inserted or deleted in the target"

/// How many settled units render as individual folded lines before the tail
/// folds to one counted line (the constant-size discipline: a 3-unit run and
/// a 3,000-unit run open on the same calm screen).
let private settledTailCap = 8

let toHtmlTriaged (catalog: Catalog) (units: TransferTriage.TransferUnit list) (impact: TransferImpact.Impact) : string =
    let t = impact.Totals
    let sb = StringBuilder()
    sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">") |> ignore
    sb.Append(sprintf "<title>Transfer impact — %s</title>" (esc impact.Flow)) |> ignore
    sb.Append("<style>").Append(css).Append("</style></head><body><div class=\"wrap\">") |> ignore
    // masthead
    let openCount = units |> List.filter (fun u -> not (TransferTriage.isSettled u.Triage)) |> List.length
    sb.Append("<header class=\"masthead\"><div class=\"eyebrow\">Transfer impact · dry run · zero writes</div>") |> ignore
    sb.Append(sprintf "<div class=\"title-row\"><h1>%s</h1></div>" (esc impact.Flow)) |> ignore
    sb.Append(sprintf "<div class=\"meta\"><span class=\"chip\">strategy <b>%s</b></span><span class=\"chip\">units <b>%d</b></span><span class=\"chip\">open <b>%d</b></span><span class=\"chip\">settled <b>%d</b></span></div></header>"
                (esc impact.Strategy) (List.length units) openCount (List.length units - openCount)) |> ignore
    // tiles
    sb.Append("<section class=\"tiles\">") |> ignore
    sb.Append(sprintf "<div class=\"tile add\"><div class=\"n\">+%d</div><div class=\"l\">rows added</div></div>" t.Added) |> ignore
    sb.Append(sprintf "<div class=\"tile del\"><div class=\"n\">&minus;%d</div><div class=\"l\">rows deleted</div></div>" t.Deleted) |> ignore
    sb.Append(sprintf "<div class=\"tile chg\"><div class=\"n\">~%d</div><div class=\"l\">rows changed</div></div>" t.Changed) |> ignore
    sb.Append(sprintf "<div class=\"tile keep\"><div class=\"n\">%d</div><div class=\"l\">unchanged</div></div></section>" t.Unchanged) |> ignore
    // legend
    sb.Append("<div class=\"legend\"><span><span class=\"swatch\" style=\"background:var(--add-line)\"></span> added</span><span><span class=\"swatch\" style=\"background:var(--del-line)\"></span> deleted by wipe</span><span><span class=\"swatch\" style=\"background:var(--chg-line)\"></span> changed</span><span><span class=\"swatch\" style=\"background:var(--line)\"></span> unchanged (counted, not listed)</span></div>") |> ignore
    // the summary surfaces (variety legible before detail), unchanged
    summaryHtml catalog sb impact.Summary
    intentHtml catalog sb impact.Summary
    confirmHtml catalog sb impact.Summary
    // THE TRIAGE: open units first, the top-ranked revealed; settled units one
    // folded line each up to the cap; the tail one counted line.
    let openUnits, settledUnits = units |> List.partition (fun u -> not (TransferTriage.isSettled u.Triage))
    if not (List.isEmpty openUnits) then
        sb.Append("<h2>Open units <span class=\"sub\">each carries a decision to make, or rows that will be inserted or deleted; the unit with the most affected rows is expanded first</span></h2><div class=\"hrule\"></div>") |> ignore
        openUnits |> List.iteri (fun i u ->
            segmentHtml catalog sb (i = 0) (Some (triageBadge u.Triage)) u.Segment)
    if not (List.isEmpty settledUnits) then
        sb.Append("<h2>Settled units <span class=\"sub\">verified — one line each; expand a line to read the substantiation</span></h2><div class=\"hrule\"></div>") |> ignore
        let shown, tail = settledUnits |> List.splitAt (min settledTailCap (List.length settledUnits))
        shown |> List.iter (fun u -> segmentHtml catalog sb false (Some (triageBadge u.Triage)) u.Segment)
        if not (List.isEmpty tail) then
            let tailRows =
                tail |> List.sumBy (fun u -> u.Segment.Context |> List.sumBy (fun c -> c.Added + c.Deleted + c.Changed + c.Unchanged))
            sb.Append(sprintf "<div class=\"more\">and %d more settled unit(s) — %d row(s), each counted in the summary above and present in full in %s.impact.json.</div>" (List.length tail) tailRows (esc impact.Flow)) |> ignore
    sb.Append(sprintf "<footer><span>projection check go %s --impact</span><span>·</span><span>go-board/%s.impact.html</span></footer>" (esc impact.Flow) (esc impact.Flow)) |> ignore
    sb.Append("</div></body></html>") |> ignore
    sb.ToString()
