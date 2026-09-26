namespace Projection.Cli
// LINT-ALLOW-FILE-MUTATION: the live Spectre `Progress` render loop + task mutation are sealed
//   function-local imperative state at the terminal/IO boundary; mutation never escapes the loop.

open System
open Spectre.Console
open Projection.Pipeline

/// THE PROGRESS WIDGET (2026-07-08, the row-118 cash-out): the animated Spectre
/// `Progress` display — a determinate bar per long leg with an ETA — as a SECOND
/// rendering of the SAME `LogSink.summary.stageProgress` feed the `Watch` board
/// consumes. It is a *consumer* of channel 2, never a second emit surface
/// (`REPORTING_HORIZON.md` §15), so the animated bars and the NDJSON can never
/// drift.
///
/// It is a **mutually-exclusive ALTERNATIVE to the Watch board**, not a sibling:
/// Spectre's `Progress().Start(ctx -> …)` owns the channel-2 (`Console.Error`)
/// live region with its own render loop, exactly as `Live().Start()` does — two
/// live contexts on one console corrupt each other's repaints. The
/// `Shell.executeOn` fork selects ONE renderer per run (`--progress` picks the
/// bars, the default picks the Watch board).
///
/// Determinacy is honest (`THE_VOICE.md` §13): a stage draws a real bar only when
/// a genuine denominator arrives (`total > 0` — `extract`'s fixed result-sets,
/// `deploy`'s write count, `load`'s KIND count); a stage that never reports a
/// total is an indeterminate breathing spinner, never a bar that misstates.
[<RequireQualifiedAccess>]
module ProgressRenderer =

    /// One leg's live tally — how far it has come against its (possibly unknown)
    /// denominator. `Total <= 0` is an unknown denominator (a lazily-streamed
    /// producer); it renders as a spinner, not a bar.
    type Cell = { Done: int; Total: int; Completed: bool }

    /// The pure progress model — the legs in first-seen order + their tallies. The
    /// live loop folds envelopes into this and mirrors it onto Spectre tasks, so
    /// the fold (the interesting logic) is testable without a terminal — the same
    /// pure-model / live-render split `Watch` uses.
    type Model = { Order: string list; Cells: Map<string, Cell> }

    let empty : Model = { Order = []; Cells = Map.empty }

    let private intOf (key: string) (payload: Map<string, objnull>) : int =
        match Map.tryFind key payload with
        | Some (:? int as i)   -> i
        | Some (:? int64 as l) -> int l
        | _                    -> 0

    /// Fold one envelope into the model — the same three codes the Watch board
    /// reads: `<stage>.started` seeds a leg, `summary.stageProgress` updates its
    /// tally (seeding the leg if progress arrives before the start marker, as some
    /// producers emit), `summary.stageCompleted` closes it. Every other envelope is
    /// ignored (this surface is the progress lens only).
    let apply (m: Model) (env: LogSink.Envelope) : Model =
        let code = env.Code
        let payload = env.Payload
        let seed (key: string) (cell: Cell) : Model =
            if Map.containsKey key m.Cells then { m with Cells = Map.add key cell m.Cells }
            else { Order = m.Order @ [ key ]; Cells = Map.add key cell m.Cells }
        if code.EndsWith ".started" then
            let key = code.Substring(0, code.Length - ".started".Length)
            if Map.containsKey key m.Cells then m
            else seed key { Done = 0; Total = 0; Completed = false }
        elif code = "summary.stageProgress" then
            match Map.tryFind "stage" payload with
            | Some s when not (isNull s) ->
                let key = string s
                let d = intOf "done" payload
                let t = intOf "total" payload
                match Map.tryFind key m.Cells with
                | Some c -> { m with Cells = Map.add key { c with Done = d; Total = t } m.Cells }
                | None   -> seed key { Done = d; Total = t; Completed = false }
            | _ -> m
        elif code = "summary.stageCompleted" then
            match Map.tryFind "stage" payload with
            | Some s when not (isNull s) ->
                let key = string s
                match Map.tryFind key m.Cells with
                | Some c -> { m with Cells = Map.add key { c with Completed = true } m.Cells }
                | None   -> m   // a stage that never started here (an umbrella) — ignore
            | _ -> m
        else m

    /// The leg's operator label — the voiced gerund the Watch board uses for the
    /// same stage key (one register, authored once in the Voice catalog).
    let private labelOf (key: string) : string =
        Watch.lineText { Key = key; State = Watch.Active None }

    /// Render the progress bars over a body, on an injected console — the testable
    /// core (drive it with `Spectre.Console.Testing.TestConsole`, exactly as
    /// `renderWatchOn`). Mirrors the Watch's off-thread discipline: the subscriber
    /// only ENQUEUES (never sleeps `emit`); `body` runs on a background task under
    /// `withWriter Null` (channel 1 suppressed for its span, prior writer restored);
    /// `finally` reaps the body on every exit path and `clearSubscribers`.
    let renderProgressOn (console: IAnsiConsole) (body: unit -> int) : int =
        let model = ref empty
        let mutable code = 0
        let queue = new System.Collections.Concurrent.BlockingCollection<LogSink.Envelope>()
        let tasks = System.Collections.Generic.Dictionary<string, ProgressTask>()
        let progress = console.Progress()
        progress.AutoRefresh <- false          // the drain loop drives the refresh (deterministic under test)
        progress.Columns(
            [| TaskDescriptionColumn() :> ProgressColumn
               ProgressBarColumn() :> ProgressColumn
               PercentageColumn() :> ProgressColumn
               SpinnerColumn() :> ProgressColumn |]) |> ignore
        code <-
            progress.Start(fun ctx ->
                LogSink.addSubscriber (fun env -> try queue.Add env with _ -> ())
                let bodyTask =
                    System.Threading.Tasks.Task.Run(fun () ->
                        try LogSink.withWriter System.IO.TextWriter.Null body
                        finally queue.CompleteAdding())
                // Mirror the pure model onto Spectre tasks — create a leg's task on
                // first sight (indeterminate until a total arrives), update its value,
                // and drive it to 100% + stop on completion. Idempotent (take-latest),
                // so a coalesced backlog never replays.
                let sync () =
                    for key in model.Value.Order do
                        let c = model.Value.Cells.[key]
                        let task =
                            match tasks.TryGetValue key with
                            | true, t -> t
                            | _ ->
                                let t = ctx.AddTask(Markup.Escape (labelOf key))
                                t.IsIndeterminate <- true
                                tasks.[key] <- t
                                t
                        if c.Total > 0 then
                            task.IsIndeterminate <- false
                            task.MaxValue <- float c.Total
                            task.Value <- float (min c.Done c.Total)
                        if c.Completed then
                            task.IsIndeterminate <- false
                            if task.MaxValue <= 0.0 then task.MaxValue <- 1.0
                            task.Value <- task.MaxValue
                            task.StopTask()
                try
                    let mutable draining = true
                    while draining do
                        let mutable env = Unchecked.defaultof<LogSink.Envelope>
                        if queue.TryTake(&env, 100) then
                            model.Value <- apply model.Value env
                            // Coalesce everything already queued into ONE sync — a
                            // high-cadence progress flood collapses to a single frame
                            // (the backlog-replay defect the Watch loop also guards).
                            let mutable more = true
                            while more do
                                if queue.TryTake(&env, 0) then model.Value <- apply model.Value env
                                else more <- false
                            sync ()
                            ctx.Refresh()
                        elif queue.IsCompleted then draining <- false
                        else ctx.Refresh()   // heartbeat: the spinner breathes during a quiet leg
                    sync ()
                    ctx.Refresh()
                    bodyTask.GetAwaiter().GetResult()
                finally
                    // Reap the producer on EVERY exit path (a render throw included) so a
                    // leaked subscriber can never outlive the render with the writer pinned.
                    (try bodyTask.GetAwaiter().GetResult() |> ignore with _ -> ())
                    LogSink.clearSubscribers ())
        code

    /// Production wrapper — the bars on stderr (channel 2), mirroring the Watch's
    /// console creation (`NO_COLOR` / `CLICOLOR_FORCE` honored; no width pin — the
    /// gate below guarantees a real terminal).
    let renderProgress (body: unit -> int) : int =
        renderProgressOn (View.consoleTo Console.Error) body

    /// The gate — the bars run only on a real terminal (a redirected stderr keeps
    /// clean NDJSON on channel 1), mirroring `Watch.shouldWatch`.
    let shouldProgress (requested: bool) : bool =
        requested && not Console.IsErrorRedirected
