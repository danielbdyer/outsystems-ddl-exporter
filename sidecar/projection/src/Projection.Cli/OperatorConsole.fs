module Projection.Cli.OperatorConsole

// LINT-ALLOW-FILE: CLI operator-console substrate — terminal operator-facing prose
//   at the CLI boundary uses string composition; the structural surface is the typed
//   MovementSpec / Intent (Projection.Pipeline).

// The operator-console substrate every run face executes inside ("Program.fs
// owns argv parsing, exit codes, and console narration" — FullExportRun's
// contract): error/exit printing, the per-invocation presentation modes
// (verbose / pretty / watch, set once by the global-flag strip in `main`),
// the bench dump, the `withRun` LogSink run envelope, and the stage arcs the
// live Watch board pre-seeds. Extracted from Program.fs (2026-06-10
// decomposition, the B7/`Deploy.fs` precedent) so the faces (`Faces/*.fs`) and
// the dispatcher (`Program`) share one substrate.

open System
open System.Diagnostics
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT

/// Print each usage line directly to the writer via the BCL
/// `WriteLine` primitive. Per the data-structure-oriented
/// discipline: typed list flows in; per-line writes flow out; no
/// intermediate concatenation.
let printLines (writer: TextWriter) (lines: string list) : unit =
    for line in lines do writer.WriteLine line

let die (code: int) (message: string) : int =
    Console.Error.WriteLine message
    code

/// Render a `ValidationError list` to the writer as the **voiced** §10/§14
/// surface (`THE_VOICE.md`; `THE_CLI.md` §5) — a plain statement, the located
/// cause + code in the substantiation (never the code on the lead line), and the
/// next move. Delegates to `TtyRenderer.renderErrorsTo` (the `Voice.errorsSurface`
/// projection). The structured `config.validationFailed` / `transfer.*` NDJSON
/// stays the machine channel, unchanged — only the operator copy moves to the
/// register. (Replaces the prior `  [<code>] <message>` form, which led with the
/// code — a §10 violation.)
let printErrors (writer: TextWriter) (errors: ValidationError list) : unit =
    TtyRenderer.renderErrorsTo writer errors

/// Print the bench table to stdout AND persist a JSON snapshot.
/// Called at the tail of every successful subcommand so the perf
/// surface is in the operator's attention.
/// Polish — `-v` / `--verbose` surfaces the per-label bench table (and other
/// depth); set in `main`. Default is calm: the bench snapshot persists for the
/// perf gate + rides the `runComplete` aggregates, but the table is opt-in.
/// (An ALIAS since 2026-07-02 — the ref lives on the `Shell`, which compiles
/// first; every `open …OperatorConsole` site keeps reading the same cell.)
let verboseMode = Shell.verboseMode

let dumpBench (tag: string) : unit =
    let stats = Bench.snapshot ()
    if not (List.isEmpty stats) then
        // R1c — keyed by the run: the snapshot file and the captured Run
        // aggregate share the RunId address.
        let path = BenchSink.runPath (Directory.GetCurrentDirectory()) tag (LogSink.runId ())
        try BenchSink.persistJson path tag stats
        with ex -> eprintfn "  WARNING: failed to persist bench snapshot: %s" ex.Message
        // Calm by default (REPORTING_HORIZON polish) — the table is depth,
        // shown only under -v. The snapshot is always persisted.
        if verboseMode.Value then
            if Shell.prettyMode.Value then
                // Under pretty the boxed board + verdict panel own the console
                // (§13); a raw stdout table lands adjacent to the board's
                // teardown and spills past the frame. The depth stays in the
                // persisted snapshot; one line names where it lives.
                printfn ""
                printfn "  bench counters recorded: %s" path
            else
                // #13 — the `-v` bench dump joins the View lens (a `View.Table` via
                // `TtyRenderer.benchView`, so it gains color/json/`--query`); Core's
                // `Bench.renderTable` stays for the perf scenarios. The factory pins the
                // width and strips color for a piped sink.
                printfn ""
                View.write (View.consoleTo System.Console.Out) (TtyRenderer.benchView stats)
                printfn ""
                printfn "  bench snapshot: %s" path

/// Slice 4 (verb coverage) — bracket a verb body in the structured
/// LogSink run envelope so EVERY emitting verb (not just `full-export`)
/// produces a conforming NDJSON stream: a `config.runStart` first event
/// and the mandatory terminal `summary.runComplete` (§10), even when the
/// verb's middle is sparse. `full-export` is NOT wrapped here — it
/// self-brackets via `FullExportRun`. NDJSON goes to stderr (channel 1);
/// the verb's human narration stays on stdout (the §5 split).
/// Tier-3 — `--pretty` is the global channel-2 flag (stripped from argv in
/// `main`, recorded here). When set — explicitly, or AUTO when stderr is a real
/// TTY and NDJSON wasn't forced — channel 1 (NDJSON) is suppressed and the run
/// renders on the Spectre surface instead: the live stage board (`Watch`) DURING
/// the run, then the verdict panel as the resolved final frame. (`--watch` was
/// folded into this 2026-06-17 — the board is simply what pretty shows while a
/// run is in flight; the standalone flag is deprecated.)
/// (An ALIAS since 2026-07-02 — the ref lives on the `Shell`.)
let prettyMode = Shell.prettyMode

// The per-face stage arcs retired 2026-06-12 (card S2): the live Watch board
// pre-seeds from the declared `Spines` (`RunSpine` — one definition site per
// arc), no longer from hand-rolled string lists here.

let withRun (command: string) (body: unit -> int) : int =
    // Since 2026-07-02 this is a THIN alias over the operator shell — the one
    // door (`Shell.execute`): the run-envelope bracket (S4a, one owner),
    // channel-1 suppression under pretty (§15.1, now SCOPED so an outer
    // writer restores), the cross-run ledger append, and the verdict panel.
    // NM-34b — the generic verb wrapper is content-blind (it brackets an
    // arbitrary `body : unit -> int`); a digest-bearing verb runs through its
    // own face (e.g. full-export), not this generic bracket.
    Shell.execute
        (Shell.framed command)
        Shell.Bracket.Bracketed
        None
        body

let parseEnvironment (defaultLabel: string) (label: string option) : Projection.Core.Environment =
    match label with
    | None -> Projection.Core.Environment.Named defaultLabel
    | Some s ->
        match s.Trim().ToUpperInvariant() with
        | "DEV"  -> Projection.Core.Environment.Dev
        | "QA"   -> Projection.Core.Environment.Qa
        | "UAT"  -> Projection.Core.Environment.Uat
        | "PROD" -> Projection.Core.Environment.Prod
        | _      -> Projection.Core.Environment.Named (s.Trim())
