module Projection.Cli.Faces.Explain
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Intent / Voice catalog, BCL primitives only at this terminal text edge.

// The explain faces (explainView / explain-node / suggest-config / policy-diff),
// extracted from the RunFaces wall (recon #3). Depends only on Pipeline + the
// shared CLI helpers + `Faces.Common` (the `nameOf` spine the node-explain trail
// shares with the transfer faces). Verbatim relocation — zero behavior change.

open System
open System.IO
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Common

/// NM-37 — the explain story as a `View` (the masterful base #3 substrate),
/// built PURELY from the filtered transform trail + findings so it is testable
/// without the projection I/O. The transform trail uses the `View.Trail` block
/// (built for exactly this surface, previously zero producers); the
/// empty/findings states use `View.Note`/`View.Field`/`View.Action`. The whole
/// document routes through `TtyRenderer.renderAnswer`, so explain gains the
/// pretty + plain + JSON (`--format json` / `--query`) lenses every other answer
/// surface carries — the human and machine views are one value, never a parallel
/// print path. The trail is rendered through the SAME
/// `EventProjection.transformKindRender` the event stream uses, so the two
/// trails cannot drift.
let explainView
    (ssKeyText: string)
    (trail: LineageEvent list)
    (diags: DiagnosticEntry list)
    : View.View =
    let header = View.Field ("explain", ssKeyText, View.Neutral)
    if List.isEmpty trail && List.isEmpty diags then
        View.Doc
            [ header
              View.Blank
              View.Note "no transforms or findings matched"
              View.Action "try a fuller name, or a model that exercises this node" ]
    else
        // The transform trail: one step per touching transform, the step label
        // carrying the pass name and the rendered kind tag, the optional detail
        // its decision/rationale.
        let trailBlock =
            if List.isEmpty trail then []
            else
                let steps =
                    trail
                    |> List.map (fun e ->
                        let tag, detail = EventProjection.transformKindRender e.TransformKind
                        let stepLabel = sprintf "%s %s %s" e.PassName Theme.dot tag
                        stepLabel, detail)
                [ View.Trail ("transforms", steps); View.Blank ]
        // The findings: each a status-glyphed field (severity → status), its
        // suggested fix the next-action line beneath it.
        let findingBlocks =
            if List.isEmpty diags then []
            else
                let rows =
                    diags
                    |> List.collect (fun d ->
                        let st =
                            match d.Severity with
                            | DiagnosticSeverity.Error   -> View.Bad
                            | DiagnosticSeverity.Warning -> View.Warn
                            | _                          -> View.Neutral
                        let field = View.Field (d.Code, d.Message, st)
                        match d.SuggestedConfig with
                        | Some c -> [ field; View.Action (sprintf "fix: %s = %s" c.Path c.Value) ]
                        | None   -> [ field ])
                View.Note "findings" :: rows @ [ View.Blank ]
        View.Doc ([ header; View.Blank ] @ trailBlock @ findingBlocks)

/// P3 (REPORTING_HORIZON polish) — `explain <config> <ssKey>`. The drill-down
/// doorway: run the projection, then tell the full story for ONE node — every
/// transform that touched it (with the decision + rationale, rendered through
/// the SAME `EventProjection.transformKindRender` the event stream uses) and
/// every finding (with its suggested fix). "Every number is a doorway."
/// `ssKey` matches by exact root or substring, so `CustomerId` finds
/// `OSUSR_FOO.OrderHeader.CustomerId`.
let runExplain (configPath: string) (ssKeyText: string) (asJson: bool) (depth: int) : int =
    match Config.fromFile configPath with
    | Error errs ->
        printErrors Console.Error errs
        2
    | Ok config ->
        match (Compose.runWithConfig config).GetAwaiter().GetResult() with
        | Error errs ->
            printErrors Console.Error errs
            2
        | Ok report ->
            let matchesKey (k: SsKey) =
                let s = SsKey.rootOriginal k
                s = ssKeyText || s.Contains(ssKeyText)
            let trail = report.Trail |> List.filter (fun e -> matchesKey e.SsKey)
            let diags =
                (report.Diagnostics @ report.PassDiagnostics)
                |> List.filter (fun d -> match d.SsKey with Some k -> matchesKey k | None -> false)
            // L2 — explain is a read surface too: dig the transform trail + findings live
            // on a terminal, one-shot when piped. `present` returns 0; the empty-match case
            // keeps its 1 exit (the "nothing found" signal) after the view is shown either way.
            let shown = Navigator.present asJson depth (explainView ssKeyText trail diags)
            if List.isEmpty trail && List.isEmpty diags then 1 else shown

/// P4 (REPORTING_HORIZON polish) — `suggest-config <config> [--apply <out>]`.
/// Run the projection, collect every actionable `SuggestedConfig` from the
/// diagnostic streams, merge by path (dedup), **rank by impact** (how many
/// nodes each edit touches), and present the to-do list highest-leverage
/// first. `--apply` writes the merged patch JSON. This is principle #5 made
/// concrete: don't just describe — recommend, ranked, and hand over the patch.
let runSuggestConfig (configPath: string) (applyTo: string option) : int =
    match Config.fromFile configPath with
    | Error errs ->
        printErrors Console.Error errs
        2
    | Ok config ->
        let task = Compose.runWithConfig config
        match task.GetAwaiter().GetResult() with
        | Error errs ->
            printErrors Console.Error errs
            2
        | Ok report ->
            // Name the touched nodes by `Name` (the projection's read catalog), not the
            // GUID `rootOriginal` — the same legibility the diff + verify-data carry.
            let names = Catalog.nameIndex report.ReadCatalog
            let merged =
                (report.Diagnostics @ report.PassDiagnostics)
                |> List.choose (fun e -> e.SuggestedConfig |> Option.map (fun c -> c, e.SsKey))
                |> List.groupBy (fun (c, _) -> c.Path)
                |> List.map (fun (path, items) ->
                    let c0 = fst (List.head items)
                    let ssKeys =
                        items
                        |> List.choose (fun (_, k) -> k |> Option.map (nameOf names))
                        |> List.distinct
                    {| Path = path; Value = c0.Value; Note = c0.Note
                       Count = List.length items; SsKeys = ssKeys |})
                |> List.sortByDescending (fun s -> s.Count)
            printfn ""
            if List.isEmpty merged then
                printfn "  %s no actionable config edits — nothing to apply" Theme.ok
                0
            else
                printfn "  %d config edit(s) suggested, by impact:" (List.length merged)
                printfn ""
                for s in merged do
                    printfn "  %s %s = %s   (%d node%s)"
                        Theme.arrow s.Path s.Value s.Count (if s.Count = 1 then "" else "s")
                    match s.Note with
                    | Some n -> printfn "      %s %s" Theme.dot n
                    | None   -> ()
                printfn ""
                match applyTo with
                | Some out ->
                    let patch = System.Text.Json.Nodes.JsonObject()
                    for s in merged do
                        patch.[s.Path] <- System.Text.Json.Nodes.JsonValue.Create(s.Value)
                    let json =
                        patch.ToJsonString(
                            System.Text.Json.JsonSerializerOptions(WriteIndented = true))
                    File.WriteAllText(out, json)
                    printfn "  %s merged patch (%d edits) written to %s" Theme.ok (List.length merged) out
                | None ->
                    printfn "  %s --apply <out.json> to write the merged patch" Theme.dot
                0

/// §5.6 — `policy-diff <config-a> <config-b>`. Diff what two configs would
/// project over the shared Catalog (read from config-a's Model.Path). Renders
/// the five-axis structural delta + the changed-kind set. Pure/structural —
/// no live DB (Profile.empty); the operator's "diff policy A vs B" question.
let runPolicyDiff (configAPath: string) (configBPath: string) : int =
    match Config.fromFile configAPath, Config.fromFile configBPath with
    | Error errors, _
    | _, Error errors ->
        printErrors Console.Error errors
        6
    | Ok cfgA, Ok cfgB ->
        let result = (PolicyDiff.diffConfigs cfgA cfgB).GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Error errors ->
                printErrors Console.Error errors
                2
            | Ok diff ->
                let s = diff.StructuralDiff
                printfn "%s"
                    (if s.AnyChanged then "The two policies differ." else "The two policies are identical.")
                let axis (name: string) (changed: bool) =
                    printfn "  %-13s %s" name (if changed then "changed" else "same")
                axis "selection"    s.Selection.Changed
                axis "emission"     s.Emission.Changed
                axis "insertion"    s.Insertion.Changed
                axis "tightening"   s.Tightening.Changed
                axis "userMatching" s.UserMatching.Changed
                printfn "  changed tables: %d" (List.length diff.ChangedKinds)
                for k in diff.ChangedKinds do
                    printfn "    - %s" (SsKey.rootOriginal k)
                0
        dumpBench "policy-diff"
        exitCode
