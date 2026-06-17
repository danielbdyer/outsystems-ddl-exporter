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
    /// stage's measured duration. `Halted` is the R2 Aborted arm on the board:
    /// the stage's bracket CLOSED with a non-success wire outcome (`failed` /
    /// `aborted`) — the line closes honestly (`✕`, "<Stage> stopped"), never a
    /// hang and never a `✓` that misstates (§13).
    type StageState =
        | Pending
        | Active of Progress option
        | Done of durationMs: int64 option
        | Halted of durationMs: int64 option

    /// One line of the board — a stage, keyed by its internal name (the prefix of
    /// the `<stage>.started` / `summary.stageCompleted{stage}` codes), with its
    /// current visible state. Ordered by first appearance.
    type StageLine = { Key: string; State: StageState }

    /// The board — the stage lines, plus the optional run frame (`THE_VOICE.md`
    /// §13: "the instrument speaks about its own running"). `Title` is the
    /// run-in-flight header voiced above the arc; `RunIdentity` is the run's
    /// ordinal, voiced in the done-frame as "Recorded as run N" when present. Both
    /// default to absent — the board renders the bare arc when no frame is given.
    /// `Umbrella` is the run's root-scope key (the spine's declared root — R2):
    /// its events are elided, never a watched line. Boards built from flat string
    /// lists keep the legacy "pipeline" convention; boards seeded from a
    /// `RunSpine` derive it from the declaration.
    type Board =
        { Stages: StageLine list
          Title: string option
          RunIdentity: string option
          Umbrella: string option }

    let empty : Board =
        { Stages = []; Title = None; RunIdentity = None; Umbrella = Some "pipeline" }

    /// A board pre-seeded with the run's planned stages, each `Pending` — so the
    /// operator sees the whole arc before the first stage starts, the stages
    /// filling in as the run happens (Appendix A.3). The umbrella stage is never
    /// seeded (it is not a sub-stage the operator watches).
    let seeded (stageKeys: string list) : Board =
        { Stages =
            stageKeys
            |> List.filter (fun k -> not (k = "pipeline"))
            |> List.map (fun k -> { Key = k; State = Pending })
          Title = None
          RunIdentity = None
          Umbrella = Some "pipeline" }

    /// A seeded board carrying the run frame — the run-title header voiced above
    /// the arc (`THE_VOICE.md` §13) and, when known up front, the run's ordinal for
    /// the done-frame's "Recorded as run N". The boundary (`renderWatch`) supplies
    /// the command + any run identity it holds; the board renders the bare arc when
    /// neither is given (the `seeded` shape stays the unframed default).
    let seededWith (command: string option) (runIdentity: string option) (stageKeys: string list) : Board =
        { seeded stageKeys with Title = command; RunIdentity = runIdentity }

    /// A board pre-seeded from a declared `RunSpine` (R2 — the pre-seeds derive
    /// from the declaration; the per-face string lists retire). The declared arc
    /// never contains the root by construction, so no filtering; the umbrella key
    /// derives from the spine's declared root rather than the legacy convention.
    let seededOf (spine: RunSpine) : Board =
        { Stages = RunSpine.keys spine |> List.map (fun k -> { Key = k; State = Pending })
          Title = None
          RunIdentity = None
          Umbrella = RunSpine.rootKey spine }

    /// The umbrella root scope (e.g. full-export's "pipeline") wraps the whole
    /// run; it is not a sub-stage the operator watches, so the board elides its
    /// events (the declared sub-stages are the live arc).
    let private isUmbrella (board: Board) (key: string) : bool =
        board.Umbrella = Some key

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
    /// (→ `Done` with the measured duration on a `succeeded` outcome; → `Halted`
    /// on `failed` / `aborted`, so a closed-unsuccessful stage reads `✕`, never a
    /// `✓` that misstates and never a hung Active line — the R2 Aborted arm). The
    /// redundant `<stage>.completed` markers are ignored — `summary.stageCompleted`
    /// carries the duration.
    let apply (board: Board) (code: string) (payload: Map<string, objnull>) : Board * bool =
        if code.EndsWith ".started" then
            let key = code.Substring(0, code.Length - ".started".Length)
            if isUmbrella board key then board, false
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
                if isUmbrella board key then board, false
                else
                    let dur = durationOf payload
                    // The wire outcome decides the closed state: `succeeded`
                    // (or absent — the pre-outcome envelope shape) → Done;
                    // `failed` / `aborted` → Halted. The line always closes.
                    let closed =
                        match Map.tryFind "outcome" payload with
                        | Some o when not (isNull o) && string o <> "succeeded" -> Halted dur
                        | _ -> Done dur
                    if board.Stages |> List.exists (fun ln -> ln.Key = key) then
                        { board with
                            Stages =
                                board.Stages
                                |> List.map (fun ln -> if ln.Key = key then { ln with State = closed } else ln) }, true
                    else
                        { board with Stages = board.Stages @ [ { Key = key; State = closed } ] }, true
            | _ -> board, false
        else board, false

    /// Fold an envelope into the board (the `LogSink.Envelope` form).
    let applyEnvelope (board: Board) (env: LogSink.Envelope) : Board * bool =
        apply board env.Code env.Payload

    /// R1e — reconstruct the board from a STORED run's serialized envelopes
    /// (`Run.Events`, the NDJSON lines `Run.capture` persists): each line
    /// parses to its (code, payload) and folds through the SAME `apply` the
    /// live subscriber feeds. The R1 law is that this projection equals the
    /// board the live run built — the stored Run is a faithful source for
    /// the view, not a lossy echo. Unparseable lines fold as no-ops (the
    /// board reacts to the stage codes only; foreign lines carry none).
    let boardOfStored (seed: Board) (events: string list) : Board =
        let parseLine (line: string) : (string * Map<string, objnull>) option =
            try
                use doc = System.Text.Json.JsonDocument.Parse line
                let root = doc.RootElement
                match root.TryGetProperty "code" with
                | true, c when c.ValueKind = System.Text.Json.JsonValueKind.String ->
                    let code = match c.GetString() with null -> "" | s -> s
                    let payload =
                        match root.TryGetProperty "payload" with
                        | true, p when p.ValueKind = System.Text.Json.JsonValueKind.Object ->
                            [ for prop in p.EnumerateObject() ->
                                let v : objnull =
                                    match prop.Value.ValueKind with
                                    | System.Text.Json.JsonValueKind.Number ->
                                        (match prop.Value.TryGetInt64() with
                                         | true, l -> box l
                                         | _ -> box (prop.Value.GetDouble()))
                                    | System.Text.Json.JsonValueKind.String ->
                                        (match prop.Value.GetString() with null -> box "" | s -> box s)
                                    | System.Text.Json.JsonValueKind.True -> box true
                                    | System.Text.Json.JsonValueKind.False -> box false
                                    | _ -> box (prop.Value.GetRawText())
                                prop.Name, v ]
                            |> Map.ofList
                        | _ -> Map.empty
                    Some (code, payload)
                | _ -> None
            with _ -> None
        events
        |> List.fold
            (fun board line ->
                match parseLine line with
                | Some (code, payload) -> fst (apply board code payload)
                | None -> board)
            seed

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
    /// (humane numerals, the estimate only when it can be computed honestly). A
    /// non-positive `Total` is an unknown denominator (a lazily-streamed producer
    /// that cannot count ahead) — then it is a plain count-up, `142 applied`, no
    /// fraction, no estimate (§13 — show the count without a misstated bar).
    let progressText (p: Progress) : string =
        if p.Total <= 0 then sprintf "%s applied" (Theme.humane p.Done)
        else
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
        | Halted dur ->
            // The closed-unsuccessful line — voiced through `watch.stageHalted`
            // (the catalog owns the copy; the board never authors prose).
            let baseText = statementText "watch.stageHalted" (Map.ofList [ "stage", box line.Key ])
            match dur with
            | Some ms -> sprintf "%s · %s" baseText (secondsText ms)
            | None    -> baseText

    /// The run's terminal stage — the last line on the board (the arc's final
    /// stage). `None` for an empty board. The done-frame's follow-on is keyed off
    /// it (§13 — "a finished change build offers the verify").
    let terminalStageKey (board: Board) : string option =
        board.Stages |> List.tryLast |> Option.map (fun s -> s.Key)

    /// Whether the run has reached its terminal stage — every seeded stage has
    /// closed (`Done` or `Halted`). NM-46: a run that HALTS at its terminal stage
    /// IS terminal — the arc landed (on a ✕, not a ✓), so the done-frame must still
    /// render and name the next move; treating only `Done` as terminal left a halted
    /// terminal stage with NO done-frame, the board stopping on the red ✕ in silence
    /// (a §13 violation). The done-frame renders once the visible arc has closed,
    /// never mid-run (a `Pending` / `Active` stage still keeps the frame held back).
    let isTerminal (board: Board) : bool =
        not (List.isEmpty board.Stages)
        && board.Stages |> List.forall (fun s -> match s.State with Done _ | Halted _ -> true | _ -> false)

    /// Whether the run reached terminal by HALTING at its last stage (the ✕ close)
    /// rather than completing it (the ✓ close) — the done-frame branches on this so a
    /// halted terminal run closes with a remediation follow-on, a completed one with
    /// the §13 next-phase follow-on. Only the terminal (last) stage's close decides
    /// the frame's register; an earlier halted-then-recovered stage does not arise on
    /// the board (a halt closes the arc), but keying on the terminal stage keeps the
    /// branch honest regardless.
    let private haltedAtTerminal (board: Board) : bool =
        match board.Stages |> List.tryLast with
        | Some { State = Halted _ } -> true
        | _                         -> false

    /// The done-frame line — the §13 follow-on for the terminal stage, with the
    /// recorded-run identity beneath when the board carries one. Voiced through
    /// `watch.runDone` (the catalog holds the copy; the board never authors prose).
    /// NM-46: branches on whether the terminal stage completed or HALTED — a halted
    /// terminal run closes with `Voice.followOnHalted` (a remediation move that
    /// points at the error surface), never silence on the ✕. `None` until the run is
    /// terminal.
    let doneFrameText (board: Board) : string option =
        if not (isTerminal board) then None
        else
            let followOn =
                match terminalStageKey board with
                | Some key when haltedAtTerminal board -> Voice.followOnHalted key
                | Some key                             -> Voice.followOnAfter key
                | None                                 -> "The run is complete."
            let payload =
                [ "followOn", box followOn ]
                @ (match board.RunIdentity with Some n -> [ "runIdentity", box n ] | None -> [])
                |> Map.ofList
            Some(statementText "watch.runDone" payload)

    /// The run-title header line — voiced through `watch.runTitle` when the board
    /// carries a title. `None` for an unframed board.
    let titleText (board: Board) : string option =
        board.Title
        |> Option.map (fun cmd -> statementText "watch.runTitle" (Map.ofList [ "command", box cmd ]))

    let private rowMarkup (line: StageLine) : string =
        let text = Markup.Escape(lineText line)
        match line.State with
        | Pending  -> sprintf "%s  %s" (Theme.muted Theme.pending)   (Theme.muted text)
        | Active _ -> sprintf "%s  %s" (Theme.accent Theme.collapsed) (Theme.bold text)
        | Done _   -> sprintf "%s  %s" Theme.ok                       (Theme.green text)
        | Halted _ -> sprintf "%s  %s" (Theme.red Theme.bad)          (Theme.red text)

    /// The board's rows — the optional run-title header above, the stage arc, and
    /// the done-frame (the §13 follow-on + recorded-run identity) once the run
    /// reaches its terminal stage.
    let private boardRows (board: Board) : IRenderable list =
        let titleRow =
            match titleText board with
            | Some t -> [ Markup(Theme.muted (Markup.Escape t)) :> IRenderable ]
            | None   -> []
        let stageRows = board.Stages |> List.map (fun s -> Markup(rowMarkup s) :> IRenderable)
        let doneRow =
            match doneFrameText board with
            | Some t ->
                // NM-46: a halted terminal run closes with the ✕/red glyph — a
                // green ✓ on the remediation line would misstate the run's outcome.
                let glyph, paint =
                    if haltedAtTerminal board then Theme.red Theme.bad, Theme.red
                    else Theme.ok, Theme.green
                [ Markup(sprintf "%s  %s" glyph (paint (Markup.Escape t))) :> IRenderable ]
            | None   -> []
        titleRow @ stageRows @ doneRow

    /// Project the board onto a Spectre renderable — the live target the
    /// `LiveDisplayContext` updates in place.
    let toRenderable (board: Board) : IRenderable =
        Rows(boardRows board) :> IRenderable

    /// Like `toRenderable`, but with leading header rows above the arc — the
    /// cutover timeline strip (`DYNAMIC_DISPLAY` §4: where this run sits on the
    /// path to the R6 gate). An empty header is the bare board (unchanged).
    let toRenderableWith (header: IRenderable list) (board: Board) : IRenderable =
        Rows(header @ boardRows board) :> IRenderable

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
    /// The cutover timeline strip line (pretty markup) — the canary-history dots
    /// with the present marker, the R6 gate meter, and the ratio (`●●●●✕●●▸
    /// ▇▇▇▇▇▇▇░░░ 7/10`). Pure over (cells, filled, total) so the content is
    /// testable without a ledger or a console.
    let cutoverStripText (cells: string list) (filled: int) (total: int) : string =
        let present = if List.isEmpty cells then None else Some(List.length cells - 1)
        sprintf "%s   %s   %s"
            (Theme.timelineMarkup cells present)
            (Theme.meter filled total)
            (Theme.muted (sprintf "%d/%d" filled total))

    /// The timeline header rows for the live board — read once from the configured
    /// ledger (`PROJECTION_LEDGER_DIR`). Absent ledger / no canary history ⇒ no
    /// header (the bare arc). The history is the PRIOR runs; this run's verdict is
    /// appended after it ends (`OperatorConsole.withRun`).
    let private cutoverHeader () : IRenderable list =
        match RunLedger.configuredDir () with
        | Some dir ->
            let records = RunLedger.read dir
            let cells = records |> List.choose (fun r -> r.Canary)
            if List.isEmpty cells then []
            else
                let r = RunLedger.readiness records
                [ Markup(cutoverStripText cells r.ConsecutiveGreen r.Threshold) :> IRenderable ]
        | None -> []

    let renderWatchOn (console: IAnsiConsole) (spine: RunSpine) (floorMs: int64) (body: unit -> int) : int =
        let board = ref (seededOf spine)
        let header = cutoverHeader ()
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let mutable lastRenderAt = 0L
        let mutable code = 0
        console.Live(toRenderableWith header board.Value).Start(fun ctx ->
            let subscriber (env: LogSink.Envelope) =
                let board', changed = applyEnvelope board.Value env
                if changed then
                    board.Value <- board'
                    let now = sw.ElapsedMilliseconds
                    let sleep = dwellMs floorMs lastRenderAt now
                    if sleep > 0L then System.Threading.Thread.Sleep(int sleep)
                    lastRenderAt <- sw.ElapsedMilliseconds
                    ctx.UpdateTarget(toRenderableWith header board.Value)
                    ctx.Refresh()
            LogSink.addSubscriber subscriber
            // Suppress channel 1 (NDJSON) for the live region via the SCOPED
            // `withWriter` — it saves the PRIOR writer and restores it on exit,
            // rather than hardcoding `Console.Error`. This is what lets the board
            // compose UNDER an outer `--pretty` null (`OperatorConsole.withRun`
            // nulls channel 1 for the whole bracket): the prior is already Null,
            // so it stays Null and the bracket's terminal `summary.runComplete`
            // is suppressed too — no NDJSON leaks between the board and the
            // verdict panel. Standalone (publish faces, no outer null) the prior
            // is `Console.Error`, restored unchanged — the established behavior.
            try
                code <- LogSink.withWriter System.IO.TextWriter.Null body
            finally
                LogSink.clearSubscribers ())
        code

    /// Production wrapper — the live board on stderr (channel 2), mirroring
    /// `TtyRenderer`'s console creation. Tests drive `renderWatchOn` with a
    /// `Spectre.Console.Testing.TestConsole` to assert the board (and the
    /// channel-1 suppression / prior-writer restoration) without a real TTY.
    let renderWatch (spine: RunSpine) (floorMs: int64) (body: unit -> int) : int =
        let console =
            AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Error)))
        renderWatchOn console spine floorMs body
