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
    ///
    /// `captureInputs` (NM-34b) is evaluated in the `finally` AFTER the body, so
    /// it can read what the body computed (the resolved config + source-model
    /// content for the `Run.inputDigest`; the touched `Run.LedgerRef`s — episode
    /// coordinates / journal digests — from the body's result). Returning
    /// `("", [])` is the honest empty for a content-less verb (e.g. `check
    /// ready`): the bracket itself is content-blind, so each verb supplies its
    /// own inputs or declares it has none. NEVER fabricate a digest — an empty
    /// string means "no stable input content was hashed", which `Run.diff`'s
    /// dedup treats as a non-match (correct: it cannot claim two unknowable runs
    /// are the same).
    let bracket
        (command: string)
        (configure: unit -> unit)
        (startPayload: Map<string, objnull>)
        (captureInputs: unit -> string * Run.LedgerRef list)
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
            // R1b — wired ONCE, at the single bracket owner (the card's
            // "after S4, to wire once" clause): under PROJECTION_LEDGER_DIR
            // the completed aggregate persists beside the ledger NDJSON —
            // run.json keyed by runId, events including the terminal §10
            // close, the bench snapshot on the value (R1a) — so no bracketed
            // run leaves an orphan RunId, crashed bodies included. Opt-in
            // (the env var), and a capture failure never masks the body's
            // outcome. NM-34b — the input digest + touched ledger refs are now
            // supplied by `captureInputs` (the per-verb side-channel the bracket
            // grain cannot see for itself): the full-export face threads the real
            // `Run.inputDigest` + the recorded episode's `LedgerRef`; a
            // content-less verb returns `("", [])`. Evaluated defensively — a
            // capture throw degrades to the empty digest, never masking outcome.
            match RunLedger.configuredDir () with
            | Some dir ->
                (try
                    let bench : Bench.Run =
                        { CapturedAtUtc = System.DateTime.UtcNow  // LINT-ALLOW: reified non-determinism boundary at the envelope sink (BenchSink's pattern)
                          Tag = command
                          Stats = Bench.snapshot () }
                    let code = match outcome with LogSink.Succeeded -> 0 | _ -> 1
                    let inputDigest, ledgers =
                        try captureInputs ()
                        with _ -> "", []
                    Run.save dir
                        { Run.capture command code inputDigest Map.empty with
                            Bench = Some bench
                            Ledgers = ledgers }
                 with ex ->
                    eprintfn "  WARNING: failed to persist the run aggregate: %s" ex.Message)
            | None -> ()
        value
