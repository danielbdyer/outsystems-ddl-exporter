module Projection.Cli.Faces.Inspect

// The stored-run inspect faces (buildInspectView / runInspectHistory /
// runInspect), extracted from the RunFaces wall (recon #3). Self-contained: they
// read the `Run` store, build a `View`, and render through `TtyRenderer` /
// `Navigator` — never a RunFaces-internal helper.

open System
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

/// R1d — `projection inspect <runId> [<runId>]`: the stored Run aggregate,
/// rendered (D5 — the store already resolves; this verb renders). One id
/// answers "what was this run?"; two ids answer "what moved between these
/// runs?" through `Run.diff` (the UoM delta surface; the harness's
/// before/after protocol is its restriction to a key-label set).
/// Read-only and envelope-free — stays outside the bracket per the R1b law.
/// NOTE: the card named this verb `diff <runA> <runB>`; that name is held
/// by the shipped catalog-refs diff, so the run-grain projection lands
/// under `inspect` — one noun per surface, the collision named here.
/// A stored run as a navigable `View` (#18/#11) — the inspect surface joins the
/// one substrate (it gained the json / `--query` lens the old printf face never
/// had — the one-substrate law). A `Doc` of the essence (a `Hero` verdict + the
/// counts) over diggable `Disclosure`s (transforms / artifacts / ledgers / bench),
/// which the Navigator's `→` opens one at a time. `toJson` carries the whole tree.
let buildInspectView (r: Run.Run) : View.View =
    let outcomeStatus =
        match r.Outcome.ToLowerInvariant() with
        | "ok" | "success" | "succeeded" | "green" -> View.Ok
        | "error" | "failed" | "failure" | "red" -> View.Bad
        | _ -> View.Neutral
    let header =
        [ View.Hero (outcomeStatus, sprintf "%s — %s" r.RunId r.Command)
          View.Field ("at", r.Ts, View.Neutral)
          View.Field (
              "outcome",
              r.Outcome + (match r.Canary with Some c -> sprintf "   ·   canary %s" c | None -> ""),
              outcomeStatus)
          View.Field ("events", string (List.length r.Events), View.Neutral) ]
    let transforms =
        View.Disclosure (
            sprintf "transforms   ·   %d registered, %d applied, %d declined" r.Registered r.Applied r.Declined,
            View.Neutral,
            [ View.Field ("registered", string r.Registered, View.Neutral)
              View.Field ("applied", string r.Applied, View.Neutral)
              View.Field ("declined", string r.Declined, View.Neutral) ])
    let artifacts =
        let arts = r.Artifacts |> Map.toList
        View.Disclosure (
            sprintf "artifacts   ·   %d" (List.length arts),
            View.Neutral,
            (if List.isEmpty arts then [ View.Note "none" ]
             else arts |> List.map (fun (k, _) -> View.Field (k, "", View.Neutral))))
    let ledgers =
        match r.Ledgers with
        | [] -> []
        | ls ->
            [ View.Disclosure (
                  sprintf "ledgers extended   ·   %d" (List.length ls),
                  View.Neutral,
                  (ls
                   |> List.map (fun l ->
                       match l with
                       | Run.JournalRef d -> View.Field ("journal", d, View.Neutral)
                       | Run.EpisodeRef (t, o) -> View.Field ("episode", sprintf "%s ordinal %d" t o, View.Neutral)))) ]
    let bench =
        match r.Bench with
        | None -> []
        | Some b ->
            let top = b.Stats |> List.sortByDescending (fun s -> s.TotalMs) |> List.truncate 8
            if List.isEmpty top then []
            else
                [ View.Disclosure (
                      sprintf "slowest labels   ·   top %d" (List.length top),
                      View.Neutral,
                      (top |> List.map (fun s -> View.Field (s.Label, sprintf "%d ms" s.TotalMs, View.Neutral)))) ]
    View.Doc (header @ [ transforms; artifacts ] @ ledgers @ bench)

/// `inspect` with NO id (#10 — the time axis) — open the LATEST run and walk the
/// ledger with `PgUp`/`PgDn`. The runs are sorted newest-first (ISO `Ts` sorts
/// chronologically as text); the interactive Navigator scrubs them, each frame
/// re-`buildInspectView`'d on demand (the I/O closure the Navigator stays free of).
/// Piped / `--json` / `--query` render the newest run's document one-shot — same
/// one-substrate fallback as `inspect <id>`.
let runInspectHistory (asJson: bool) : int =
    match Run.storeDir () with
    | None ->
        eprintfn "No run store is configured. Set PROJECTION_LEDGER_DIR to capture runs, then inspect."
        4
    | Some dir ->
        match Run.list dir |> List.sortByDescending (fun r -> r.Ts) with
        | [] ->
            eprintfn "No runs in the store at %s." dir
            1
        | (newest :: _) as runs ->
            let interactive =
                Intervene.isInteractive ()
                && not System.Console.IsOutputRedirected
                && not asJson
                && Option.isNone TtyRenderer.queryPath.Value
            if interactive then
                let arr = List.toArray runs
                Navigator.runHistory arr.Length 0 (fun i -> buildInspectView arr.[i])
            else
                TtyRenderer.renderAnswer asJson View.defaultDepth (buildInspectView newest)
                0

let runInspect (idA: string) (idB: string option) (asJson: bool) : int =
    match Run.storeDir () with
    | None ->
        eprintfn "No run store is configured. Set PROJECTION_LEDGER_DIR to capture runs, then inspect by run id."
        4
    | Some dir ->
        let load (id: string) : Run.Run option = Run.load dir id
        match idB with
        | None ->
            match load idA with
            | None ->
                eprintfn "Run %s is not in the store at %s." idA dir
                1
            | Some r ->
                // The dig-as-motion Navigator on a real terminal (#11/#18); piped / --json
                // / --query render the SAME document one-shot (L2 `present` owns the choice).
                Navigator.present asJson View.defaultDepth (buildInspectView r)
        | Some idB ->
            match load idA, load idB with
            | None, _ ->
                eprintfn "Run %s is not in the store at %s." idA dir
                1
            | _, None ->
                eprintfn "Run %s is not in the store at %s." idB dir
                1
            | Some a, Some b ->
                let d = Run.diff None a b
                printfn "Runs %s → %s" (fst d.RunIds) (snd d.RunIds)
                printfn "  commands: %s → %s" (fst d.Commands) (snd d.Commands)
                printfn "  outcomes: %s → %s%s" (fst d.Outcomes) (snd d.Outcomes)
                    (match d.Canaries with
                     | Some ca, Some cb -> sprintf " (canary %s → %s)" ca cb
                     | _ -> "")
                printfn "  transform deltas: %+d registered, %+d applied, %+d declined   events: %+d"
                    d.Registered d.Applied d.Declined d.Events
                let moved = d.BenchDeltas |> List.filter (fun bd -> bd.DeltaMs <> 0L<Run.ms>) |> List.truncate 10
                if List.isEmpty moved then
                    printfn "  wall times: no label moved."
                else
                    printfn "  wall-time movement (largest first):"
                    for bd in moved do
                        let fmt (v: int64<Run.ms> option) = match v with Some x -> sprintf "%d" (int64 x) | None -> "—"
                        printfn "    %-44s %s → %s ms (%+d)" bd.Label (fmt bd.BeforeMs) (fmt bd.AfterMs) (int64 bd.DeltaMs)
                0
