namespace Projection.Cli

open System
open Spectre.Console
open Spectre.Console.Rendering
open Projection.Pipeline

/// THE VOICE — the live run (Watch). The streaming surface of `THE_VOICE.md` §13
/// / `THE_STORYBOARD.md` Act 4: stages fill in, in plain words, as the run
/// happens. A consumer of the `LogSink` stage stream (`addSubscriber`), never a
/// second emit surface — the board is a *rendering* of the same envelopes channel
/// 1 writes, so the live surface and the NDJSON can never drift.
///
/// **Slice 2 (the streaming Watch over the already-emitted stage stream).** This
/// voices the LIVE stage events `full-export` already emits via
/// `Compose.runWithConfig` (`extract/profile/emit.started`, `summary.stageCompleted`)
/// — no new events. Unifying the other runs onto the envelope spine + intra-stage
/// progress + ETA is the rest of slice 2 (`THE_VOICE_BUILD_MAP.md` §4.2/§4.4).
///
/// **The minimum-dwell floor (operator concern, 2026-06-06).** In a synchronous
/// run the only live events are the discrete stage transitions; a fast stage would
/// flip `started → completed` faster than the eye can read. The floor guarantees
/// each frame stays on screen a minimum interval before the next — but only ever
/// adds the *remainder below the floor*, so a stage that already ran longer than
/// the floor is never delayed. Minimal correction, at stage granularity only
/// (`THE_VOICE.md` §13 — "the estimate degrades honestly … never a progress bar
/// that misstates"; the calm is not bought by slowing the run, only by not
/// flickering past it).
[<RequireQualifiedAccess>]
module Watch =

    /// Intra-stage progress — how far an active stage has come (`done of total`)
    /// and the time so far, the basis of the honest estimate (`THE_VOICE.md` §13
    /// — "~8s remaining"; when no rate can be computed, none is shown).
    type Progress = { Done: int; Total: int; ElapsedMs: int64 }

    /// A stage's visible state on the board. `Pending` is a stage in the run's
    /// plan that has not yet started (shown faint with `○`, so the whole arc is
    /// visible from the first frame — `THE_STORYBOARD.md` Act 4 / Appendix A.3);
    /// `Active` is the gerund-in-progress (rule 12 exception), carrying its
    /// intra-stage progress once the producer reports any; `Done` carries the
    /// stage's measured duration.
    type StageState =
        | Pending
        | Active of Progress option
        | Done of durationMs: int64 option

    /// One line of the board — a stage, keyed by its internal name (the prefix of
    /// the `<stage>.started` / `summary.stageCompleted{stage}` codes), with its
    /// current visible state. Ordered by first appearance.
    type StageLine = { Key: string; State: StageState }

    type Board = { Stages: StageLine list }

    let empty : Board = { Stages = [] }

    /// A board pre-seeded with the run's planned stages, each `Pending` — so the
    /// operator sees the whole arc before the first stage starts, the stages
    /// filling in as the run happens (Appendix A.3). The umbrella stage is never
    /// seeded (it is not a sub-stage the operator watches).
    let seeded (stageKeys: string list) : Board =
        { Stages =
            stageKeys
            |> List.filter (fun k -> not (k = "pipeline"))
            |> List.map (fun k -> { Key = k; State = Pending }) }

    /// The umbrella "pipeline" stage (`FullExportRun.recordStage "pipeline"`) wraps
    /// the whole run; it is not a sub-stage the operator watches, so the board
    /// elides it (the sub-stages extract / profile / emit are the live arc).
    let private isUmbrella (key: string) : bool = key = "pipeline"

    let private durationOf (payload: Map<string, objnull>) : int64 option =
        match Map.tryFind "durationMs" payload with
        | Some (:? int64 as l) -> Some l
        | Some (:? int as i)   -> Some(int64 i)
        | _                    -> None

    let private int64Of (key: string) (payload: Map<string, objnull>) : int64 =
        match Map.tryFind key payload with
        | Some (:? int64 as l) -> l
        | Some (:? int as i)   -> int64 i
        | _                    -> 0L

    let private intOf (key: string) (payload: Map<string, objnull>) : int =
        match Map.tryFind key payload with
        | Some (:? int as i)   -> i
        | Some (:? int64 as l) -> int l
        | _                    -> 0

    /// Fold one envelope into the board. Returns the updated board and whether it
    /// produced a *renderable* transition (so the shell sleeps + refreshes only on
    /// real changes, never on every envelope). The board reacts to exactly two
    /// event kinds: a `<stage>.started` (→ `Active`) and a `summary.stageCompleted`
    /// (→ `Done` with the measured duration). The redundant `<stage>.completed`
    /// markers are ignored — `summary.stageCompleted` carries the duration.
    let apply (board: Board) (code: string) (payload: Map<string, objnull>) : Board * bool =
        if code.EndsWith ".started" then
            let key = code.Substring(0, code.Length - ".started".Length)
            if isUmbrella key then board, false
            else
                // A pre-seeded `Pending` stage flips to `Active` in place (keeping
                // its planned position); an unseeded stage appends; an already
                // Active / Done stage is a no-op (no spurious re-render).
                match board.Stages |> List.tryFind (fun s -> s.Key = key) with
                | Some { State = Pending } ->
                    { board with
                        Stages =
                            board.Stages
                            |> List.map (fun s -> if s.Key = key then { s with State = Active None } else s) }, true
                | Some _ -> board, false
                | None -> { board with Stages = board.Stages @ [ { Key = key; State = Active None } ] }, true
        elif code = "summary.stageProgress" then
            // An active stage reports how far it has come. The board updates that
            // line's progress in place — a renderable change, but never a new
            // line (a progress event for a stage that has not started is ignored).
            match Map.tryFind "stage" payload with
            | Some s when not (isNull s) ->
                let key = string s
                let prog = { Done = intOf "done" payload; Total = intOf "total" payload; ElapsedMs = int64Of "elapsedMs" payload }
                match board.Stages |> List.tryFind (fun ln -> ln.Key = key) with
                | Some { State = Active _ } ->
                    { board with
                        Stages =
                            board.Stages
                            |> List.map (fun ln -> if ln.Key = key then { ln with State = Active(Some prog) } else ln) }, true
                | _ -> board, false
            | _ -> board, false
        elif code = "summary.stageCompleted" then
            match Map.tryFind "stage" payload with
            | Some s when not (isNull s) ->
                let key = string s
                if isUmbrella key then board, false
                else
                    let dur = durationOf payload
                    if board.Stages |> List.exists (fun ln -> ln.Key = key) then
                        { board with
                            Stages =
                                board.Stages
                                |> List.map (fun ln -> if ln.Key = key then { ln with State = Done dur } else ln) }, true
                    else
                        { board with Stages = board.Stages @ [ { Key = key; State = Done dur } ] }, true
            | _ -> board, false
        else board, false

    /// Fold an envelope into the board (the `LogSink.Envelope` form).
    let applyEnvelope (board: Board) (env: LogSink.Envelope) : Board * bool =
        apply board env.Code env.Payload

    // -- the dwell floor (pure; clock-injected) --------------------------------

    /// The minimum interval a frame stays on screen before the next is shown
    /// (`PROJECTION_WATCH_DWELL_MS` overrides at the boundary; modest by default —
    /// long enough to read a stage line, short enough not to drag the run).
    let defaultDwellMs : int64 = 120L

    /// The sleep to enforce before showing the next frame: the remainder of the
    /// floor not already covered by the time since the last frame. A stage that
    /// already ran longer than the floor yields `0` (never delayed); the result is
    /// never negative. This is the whole of the correction — minimal by
    /// construction.
    let dwellMs (floorMs: int64) (lastRenderAtMs: int64) (nowMs: int64) : int64 =
        max 0L (floorMs - (nowMs - lastRenderAtMs))

    // -- rendering (the board → operator copy, voiced) -------------------------

    let private secondsText (ms: int64) : string =
        sprintf "%.1fs" (float ms / 1000.0)

    /// The honest estimate from a stage's progress (`THE_VOICE.md` §13 — "the
    /// estimate degrades honestly: when none can be computed, none is shown").
    /// A rate needs at least one item done over a measured interval; at the last
    /// item (or before the first) there is nothing to project, so `None`.
    let etaText (p: Progress) : string option =
        if p.Done <= 0 || p.ElapsedMs <= 0L || p.Done >= p.Total then None
        else
            let remainingMs = float (p.Total - p.Done) * (float p.ElapsedMs / float p.Done)
            Some(sprintf "~%s remaining" (secondsText (int64 remainingMs)))

    /// The progress fragment for an active stage — `142 of 300 · ~8s remaining`
    /// (humane numerals, the estimate only when it can be computed honestly).
    let progressText (p: Progress) : string =
        match etaText p with
        | Some eta -> sprintf "%s of %s · %s" (Theme.humane p.Done) (Theme.humane p.Total) eta
        | None     -> sprintf "%s of %s" (Theme.humane p.Done) (Theme.humane p.Total)

    /// The voiced text of a `Voice` statement code, filled from the payload. The
    /// board reuses the §13 stage copy (`<stage>.started` gerund; the resultative
    /// from `summary.stageCompleted`) — one register, never authored here.
    let private statementText (code: string) (payload: Voice.Payload) : string =
        match Voice.lookup code with
        | Some c ->
            match c.Statement payload with
            | View.Note t       -> t
            | View.Hero(_, t)   -> t
            | _                 -> code
        | None -> code

    /// The operator line for a stage — the gerund while active, the resultative
    /// (with the measured duration) once complete. Pure + voiced, so it is
    /// testable against the twelve-rule banned list.
    let lineText (line: StageLine) : string =
        // The gerund names the stage (its identity on the board); the glyph
        // carries the state (faint `○` pending, `▸` active, `✓` done).
        match line.State with
        | Pending -> statementText (line.Key + ".started") Map.empty
        | Active prog ->
            let baseText = statementText (line.Key + ".started") Map.empty
            match prog with
            | Some p -> sprintf "%s · %s" baseText (progressText p)
            | None   -> baseText
        | Done dur ->
            let baseText = statementText "summary.stageCompleted" (Map.ofList [ "stage", box line.Key ])
            match dur with
            | Some ms -> sprintf "%s · %s" baseText (secondsText ms)
            | None    -> baseText

    let private rowMarkup (line: StageLine) : string =
        let text = Markup.Escape(lineText line)
        match line.State with
        | Pending  -> sprintf "%s  %s" (Theme.muted Theme.pending)   (Theme.muted text)
        | Active _ -> sprintf "%s  %s" (Theme.accent Theme.collapsed) (Theme.bold text)
        | Done _   -> sprintf "%s  %s" Theme.ok                       (Theme.green text)

    /// Project the board onto a Spectre renderable — the live target the
    /// `LiveDisplayContext` updates in place.
    let toRenderable (board: Board) : IRenderable =
        let rows = board.Stages |> List.map (fun s -> Markup(rowMarkup s) :> IRenderable)
        Rows(rows) :> IRenderable

    // -- the live shell --------------------------------------------------------

    /// True iff a live Watch is warranted: the operator asked for it AND stderr is
    /// a real terminal (never animate into a pipe / file — the §13 / §15.1 rule).
    let shouldWatch (watchRequested: bool) : bool =
        watchRequested && not Console.IsErrorRedirected

    /// The dwell floor for this process — the default, overridable via
    /// `PROJECTION_WATCH_DWELL_MS` for perceptual tuning without a rebuild. Read at
    /// the boundary (Watch is the live surface, not Core).
    let resolveDwellMs () : int64 =
        match Environment.GetEnvironmentVariable "PROJECTION_WATCH_DWELL_MS" with
        | null -> defaultDwellMs
        | s ->
            match Int64.TryParse s with
            | true, v when v >= 0L -> v
            | _ -> defaultDwellMs

    /// Run `body` under a live stage board on stderr, driven by the `LogSink` stage
    /// stream. The board updates in place as stages fill in; each visible
    /// transition is held ≥ the dwell floor before the next is shown. Channel 1
    /// (NDJSON to stderr) is suppressed for the duration so it does not interleave
    /// with the live region; the subscriber renders exactly what channel 1 would
    /// have written (one substrate, two lenses).
    ///
    /// **Scope assumption (synchronous run).** The subscriber enforces the dwell by
    /// sleeping the emitting thread, which holds the `LogSink` lock briefly — safe
    /// because the run is synchronous + single-threaded and channel 1 is nulled
    /// (no contention). A future concurrent / async emitter would move the dwell to
    /// a drain loop on a render thread (`THE_VOICE_BUILD_MAP.md` §4.3).
    let renderWatch (seedStages: string list) (floorMs: int64) (body: unit -> int) : int =
        let console =
            AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Error)))
        let board = ref (seeded seedStages)
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let mutable lastRenderAt = 0L
        let mutable code = 0
        console.Live(toRenderable board.Value).Start(fun ctx ->
            let subscriber (env: LogSink.Envelope) =
                let board', changed = applyEnvelope board.Value env
                if changed then
                    board.Value <- board'
                    let now = sw.ElapsedMilliseconds
                    let sleep = dwellMs floorMs lastRenderAt now
                    if sleep > 0L then System.Threading.Thread.Sleep(int sleep)
                    lastRenderAt <- sw.ElapsedMilliseconds
                    ctx.UpdateTarget(toRenderable board.Value)
                    ctx.Refresh()
            LogSink.addSubscriber subscriber
            LogSink.setWriter System.IO.TextWriter.Null
            try
                code <- body ()
            finally
                LogSink.clearSubscribers ()
                LogSink.setWriter Console.Error)
        code
