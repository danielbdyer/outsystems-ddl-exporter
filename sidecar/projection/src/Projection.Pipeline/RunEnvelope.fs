namespace Projection.Pipeline

// LINT-ALLOW-FILE: run-envelope bracket at the logging boundary — function-local
//   mutables thread the body's value/outcome past the mandatory `finally`
//   (no module-level mutable state), and `box` feeds the LogSink payload
//   (`Map<string, objnull>` — the BCL JSON boundary, same shape as
//   FullExportRun.fs's marker). The structural surface is typed end to end.

open Projection.Core

/// Card S4 — the run-envelope bracket, reconciled to ONE owner. Before
/// this module there were two: `OperatorConsole.withRun` (the CLI's verb
/// wrapper) and `FullExportRun.executeCore`'s self-reset — the same
/// begin → `config.runStart` → §7.4 registry inventory → body → terminal
/// `summary.runComplete` pattern, hand-rolled twice and free to drift.
/// Both now delegate here; the spine owns the bracket.
[<RequireQualifiedAccess>]
module RunEnvelope =

    /// Bracket a run body in the structured envelope. Guarantees, in order:
    /// a fresh runId + Bench state; the caller's run-state configuration
    /// (verbosity / mutes — applied AFTER the reset, or it would be erased);
    /// `config.runStart` as the FIRST event of every run (§7.1 — including
    /// failed-config runs, which previously started with
    /// `config.validationFailed`); the §7.4 classified transform inventory;
    /// the body; and the mandatory terminal `summary.runComplete` (§10) —
    /// ALWAYS, even when the body throws (the exception rethrows after the
    /// terminal event, so a crashed run still closes its stream).
    ///
    /// `startPayload` carries the verb's §7.1 payload beyond `command`
    /// (full-export adds `configPath`); `body` returns its value plus the
    /// run's wire outcome.
    let bracket
        (command: string)
        (configure: unit -> unit)
        (startPayload: Map<string, objnull>)
        (body: unit -> 'a * LogSink.Outcome)
        : 'a =
        LogSink.beginRun () |> ignore
        Bench.reset ()
        configure ()
        LogSink.emit
            { LogSink.envelope LogSink.Info LogSink.Config "config.runStart"
                (Map.add "command" (box command : objnull) startPayload) with
                Phase = LogSink.Start }
        // §7.4 — every run publishes its classified transform inventory
        // (the registry that drives the pass chain). Debug level: hidden
        // unless --verbose / --debug.
        EventProjection.ofRegistry RegisteredAllTransforms.all |> List.iter LogSink.emit
        let mutable outcome = LogSink.Failed
        let mutable value = Unchecked.defaultof<'a>
        try
            let v, oc = body ()
            outcome <- oc
            value <- v
        finally
            LogSink.runComplete outcome command (Bench.snapshot ()) |> ignore
        value
