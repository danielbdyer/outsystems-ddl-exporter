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
let verboseMode = ref false

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
let prettyMode = ref false

// The per-face stage arcs retired 2026-06-12 (card S2): the live Watch board
// pre-seeds from the declared `Spines` (`RunSpine` — one definition site per
// arc), no longer from hand-rolled string lists here.

let withRun (command: string) (body: unit -> int) : int =
    // Tier-3 channel 2 (§15.1) — when --pretty + a real TTY, the panel
    // REPLACES the NDJSON on stderr (never both on the same TTY); route
    // channel 1 to the null sink for this run. (The writer is not run
    // state, so it is set before the bracket's reset.)
    let pretty = TtyRenderer.shouldRender prettyMode.Value
    if pretty then LogSink.setWriter System.IO.TextWriter.Null
    // Card S4a — the run envelope bracket has ONE owner (`RunEnvelope`,
    // shared with `FullExportRun.executeCore`): runStart first, the §7.4
    // registry inventory, the terminal runComplete always — now including
    // crashed bodies, which previously escaped without their §10 close.
    let code =
        // NM-34b — the generic verb wrapper is content-blind (it brackets an
        // arbitrary `body : unit -> int`), so it declares no hashable inputs.
        // A digest-bearing verb runs through its own face (e.g. full-export),
        // not this generic console bracket.
        RunEnvelope.bracket command ignore Map.empty (fun () -> "", []) (fun () ->
            let code = body ()
            code, (if code = 0 then LogSink.Succeeded else LogSink.Failed))
    // Tier-4 reporting — append this run to the cross-run ledger when one is
    // configured (`PROJECTION_LEDGER_DIR`), so the readiness gauge can read
    // the canary streak. Opt-in: default runs don't accumulate.
    (match RunLedger.configuredDir () with
     | Some dir ->
         let registered, applied, declined = LogSink.transformCounts ()
         try
             RunLedger.append dir
                 { RunId      = LogSink.runId ()
                   Ts         = System.DateTime.UtcNow.ToString("o")  // LINT-ALLOW: wall-clock timestamp at the operator-console IO boundary (ISO-8601 round-trip o format)
                   Command    = command
                   Outcome    = (if code = 0 then "succeeded" else "failed")
                   Canary     = LogSink.canaryVerdict ()
                   Registered = registered
                   Applied    = applied
                   Declined   = declined }
         with ex -> eprintfn "  WARNING: failed to append ledger record: %s" ex.Message
     | None -> ())
    if pretty then TtyRenderer.renderSummary command code
    code

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
