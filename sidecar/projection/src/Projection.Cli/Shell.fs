namespace Projection.Cli

// LINT-ALLOW-FILE: CLI operator-shell substrate — terminal operator-facing prose
//   at the CLI boundary; the structural surface is the typed Frame / RunSpine.

open System
open Projection.Pipeline

/// THE OPERATOR SHELL (2026-07-02) — the ONE door every verb's run walks
/// through under the channel-2 surface. Before this, the TTY treatment was
/// four inconsistent paths (a direct `Watch.renderWatch` for publish, the
/// `withRun` verdict-panel-only bracket for the move verbs, the gated
/// `Face.watchInline` boards, and ~10 verbs with no bracket at all spraying
/// raw NDJSON under `--pretty`). The shell is the unification:
///
///   - **not pretty** → the run-envelope bracket + the body, byte-identical
///     NDJSON for pipes, plus the cross-run ledger append.
///   - **pretty + a spine** → the live stage board inside the contained
///     instrument box (the `Watch` Live region), then the verdict panel.
///   - **pretty + no spine** (answer / preview verbs) → a static framed open
///     (a preview names "nothing will be written"), the body with channel 1
///     nulled for the span, then the verdict panel — never raw NDJSON over a
///     TTY, never a dead Live region.
///
/// Prompts keep the 2026-06-17 rule: a gate/`Intervene` prompt hoists BEFORE
/// `Shell.execute` — a Spectre Live region and a blocking prompt cannot share
/// the terminal. `Navigator` (inspect) stays outside by the same law.
[<RequireQualifiedAccess>]
module Shell =

    /// The per-invocation presentation modes — moved here from
    /// `OperatorConsole` (2026-07-02; the shell owns the display globals and
    /// compiles first; `OperatorConsole` aliases them so every existing
    /// `open …OperatorConsole` site keeps reading the same refs).
    let prettyMode : bool ref = ref false
    let verboseMode : bool ref = ref false

    /// The run's register — what kind of act the operator is watching. A
    /// `Preview` writes nothing (the daily `projection <flow>` without
    /// `--go`); `Go` is a live write; `ReadOnly` is an answer verb (diff /
    /// explain / check).
    type Register =
        | Preview
        | Go
        | ReadOnly

    /// A named flow's route — threaded from the dispatch layer so the box is
    /// titled with the daily surface's own words ("publish: cloud-dev →
    /// on-prem-dev"), never just the engine verb.
    type FlowRoute = { Name: string; From: string; To: string }

    /// The frame every run presents inside: the command, the flow route when
    /// the run IS a named flow, and the register.
    type Frame =
        { Command  : string
          Flow     : FlowRoute option
          Register : Register }

    [<RequireQualifiedAccess>]
    module Frame =
        let ofCommand (command: string) : Frame =
            { Command = command; Flow = None; Register = Go }

    /// The ambient frame for this invocation — set by `Shell.execute` on
    /// entry (and pre-seeded by the dispatcher for flow runs), read by the
    /// inner face combinators (`Face.watchInline`) so per-face signatures
    /// stay unchanged. The same per-invocation-global pattern as
    /// `prettyMode`.
    let currentFrame : Frame ref = ref (Frame.ofCommand "projection")

    /// The frame for a verb arm: a FLOW-dispatched run keeps its flow frame —
    /// the daily surface's own words title the box, the verdict panel, and
    /// the ledger record ("projection publish", never the engine verb it
    /// planned to) — while a direct verb takes its command. The register is
    /// untouched (the dispatcher's preview/go posture holds).
    let framed (command: string) : Frame =
        let ambient = currentFrame.Value
        match ambient.Flow with
        | Some _ -> ambient
        | None   -> { ambient with Command = command }

    /// Whether the body self-brackets its run envelope (`FullExportRun`
    /// carries its own `RunEnvelope` orchestration) or the shell brackets it.
    type Bracket =
        | Bracketed
        | SelfBracketed

    /// The box title: the flow route with its register for a flow frame
    /// ("publish: cloud-dev → on-prem-dev — preview"), the bare command
    /// otherwise. Feeds `watch.runTitle` through the Board's title seam.
    let titleOf (frame: Frame) : string =
        let register =
            match frame.Register with
            | Preview  -> " — preview"
            | Go       -> ""
            | ReadOnly -> ""
        match frame.Flow with
        | Some f -> sprintf "%s: %s %s %s%s" f.Name f.From Theme.arrow f.To register
        | None   -> frame.Command + register

    let private bracketed (bracket: Bracket) (command: string) (body: unit -> int) : int =
        match bracket with
        | SelfBracketed -> body ()
        | Bracketed ->
            // R1b / RI-11 — a verb that mints envelopes runs bracketed: the
            // stream opens with `config.runStart` and closes with the §10
            // terminal, crashed bodies included (the S4a law).
            RunEnvelope.bracket command ignore Map.empty (fun () -> "", []) (fun () ->
                let code = body ()
                code, (if code = 0 then LogSink.Succeeded else LogSink.Failed))

    /// The ledger tail every run shares (from the old `withRun`): append the
    /// run to the cross-run ledger when one is configured (Tier-4 reporting —
    /// the readiness gauge reads the canary streak; opt-in via
    /// `PROJECTION_LEDGER_DIR`).
    let private appendLedger (frame: Frame) (code: int) : unit =
        match RunLedger.configuredDir () with
        | Some dir ->
            let registered, applied, declined = LogSink.transformCounts ()
            try
                RunLedger.append dir
                    { RunId      = LogSink.runId ()
                      Ts         = System.DateTime.UtcNow.ToString("o")  // LINT-ALLOW: wall-clock timestamp at the operator-shell IO boundary (ISO-8601 round-trip o format)
                      Command    = frame.Command
                      Outcome    = (if code = 0 then "succeeded" else "failed")
                      Canary     = LogSink.canaryVerdict ()
                      Registered = registered
                      Applied    = applied
                      Declined   = declined }
            with ex -> eprintfn "  WARNING: failed to append ledger record: %s" ex.Message
        | None -> ()

    /// The static open for a run with no live arc — under pretty, a PREVIEW
    /// frame says so up front ("nothing will be written"), so a gated dry-run
    /// reads as a deliberate preview rather than a dead board.
    let private renderStaticOpenOn (console: Spectre.Console.IAnsiConsole) (frame: Frame) : unit =
        match frame.Register with
        | Preview ->
            let payload : Voice.Payload = Map.ofList [ "title", box (titleOf frame) ]
            let surface =
                match Voice.surfaceOf "shell.previewFrame" payload with
                | Some s -> s
                | None   -> Voice.fallbackSurface "shell.previewFrame" payload
            View.write console (Surface.render surface)
        | Go | ReadOnly -> ()

    /// The console-injected core — the TestConsole seam (mirrors
    /// `Watch.renderWatchOn` / `TtyRenderer.renderSummaryTo`). `pretty` and
    /// `live` arrive RESOLVED (production resolves them from `prettyMode` +
    /// the real TTY; tests pin them), so the behavior matrix is assertable
    /// without a terminal.
    let executeOn
        (console: Spectre.Console.IAnsiConsole)
        (pretty: bool)
        (live: bool)
        (frame: Frame)
        (bracket: Bracket)
        (spine: RunSpine option)
        (body: unit -> int)
        : int =
        currentFrame.Value <- frame
        let run () = bracketed bracket frame.Command body
        let code =
            match spine with
            | Some spine when live ->
                let seed = { Watch.seededOf spine with Title = Some (titleOf frame) }
                Watch.renderWatchOn console seed (Watch.resolveDwellMs ()) run
            | _ when pretty ->
                renderStaticOpenOn console frame
                // Channel 1 is suppressed for the span (never NDJSON and a
                // panel on the same TTY — §15.1); scoped, so an outer writer
                // is restored (an improvement over the old unscoped null).
                LogSink.withWriter IO.TextWriter.Null run
            | _ -> run ()
        // A ReadOnly answer verb (diff / explain / the check queries) never
        // joins the run ledger — the gauge reads the canary streak, and a
        // query about the ledger must not write to it (the `check ready`
        // no-append contract, now structural for the whole register). Go and
        // Preview runs append exactly as `withRun` always did.
        (match frame.Register with
         | ReadOnly -> ()
         | Go | Preview -> appendLedger frame code)
        if pretty then TtyRenderer.renderSummaryTo console frame.Command code
        code

    /// THE one door. Runs `body` inside the frame: the live boxed board when
    /// a spine rides and the terminal is real; the static framed open + nulled
    /// channel 1 when pretty without a spine; the bare bracket for pipes.
    /// Every path closes with the ledger append + (under pretty) the verdict
    /// panel.
    let execute (frame: Frame) (bracket: Bracket) (spine: RunSpine option) (body: unit -> int) : int =
        executeOn
            (View.consoleTo Console.Error)
            (TtyRenderer.shouldRender prettyMode.Value)
            (Watch.shouldWatch prettyMode.Value)
            frame bracket spine body
