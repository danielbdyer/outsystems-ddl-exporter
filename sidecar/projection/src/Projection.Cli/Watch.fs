namespace Projection.Cli
// LINT-ALLOW-FILE-MUTATION: the live-watch TUI render loop + NDJSON-tail state are sealed function-local imperative loops at the terminal/IO boundary; mutation never escapes the watch loop

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

    /// The rollup codes the board's notice strip folds (2026-07-02) — each is a
    /// producer-aggregated Warn envelope (one calm line per notice family, the
    /// §12 at-scale law), never a per-item stream. 2026-07-06 — the fidelity
    /// data-violation rollup joins the strip (the source-data-contradicts-model
    /// finding, with its remediation-script pointer).
    let noticeCodes : Set<string> =
        Set.ofList [ LiveModelRead.noticeRollupCode; ModelFidelity.dataViolationsCode ]

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
          Umbrella: string option
          /// The notice strip (2026-07-02) — the run's Warn ROLLUP envelopes,
          /// keyed by code, replace-by-code (a publish re-reads the same model
          /// for its store/load legs, so identical rollups fold to one row —
          /// the counts never double). Rendered muted between the stage arc
          /// and the done-frame; the copy is the Voice catalog's (one register).
          Notices: (string * Map<string, objnull>) list
          /// #6 echo-the-fix (2026-07-02) — how many envelopes so far carried a
          /// `suggestedConfig` payload (the operator's prioritized to-do). The
          /// board teases the count live; the verdict panel ranks the lever.
          SuggestedEdits: int }

    let empty : Board =
        { Stages = []; Title = None; RunIdentity = None; Umbrella = Some "pipeline"; Notices = []; SuggestedEdits = 0 }

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
          Umbrella = Some "pipeline"
          Notices = []
          SuggestedEdits = 0 }

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
          Umbrella = RunSpine.rootKey spine
          Notices = []
          SuggestedEdits = 0 }

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

    /// The visible weight of one envelope's fold into the board — what the drain
    /// loop renders and paces on (#20 rework). `Transitioned` is a stage-arc change
    /// (a line started or closed): the dwell floor paces these so each stays
    /// legible. `Progressed` is an in-place progress update on an active line:
    /// rendered promptly, never dwelled (dwelling per progress frame is how the
    /// backlog replay hang happened). `NoChange` is every other envelope.
    [<RequireQualifiedAccess>]
    type Fold =
        | NoChange
        | Progressed
        | Transitioned

    /// The stronger of two folds — a coalesced batch renders once, paced by the
    /// strongest change it contained.
    let strongestFold (a: Fold) (b: Fold) : Fold =
        match a, b with
        | Fold.Transitioned, _ | _, Fold.Transitioned -> Fold.Transitioned
        | Fold.Progressed, _ | _, Fold.Progressed     -> Fold.Progressed
        | _                                           -> Fold.NoChange

    /// Fold one envelope into the board. Returns the updated board and the fold's
    /// visible weight (so the shell dwells only on stage transitions and refreshes
    /// only on real changes, never on every envelope). The board reacts to exactly
    /// three event kinds: a `<stage>.started` (→ `Active`), a `summary.stageProgress`
    /// (→ progress in place) and a `summary.stageCompleted`
    /// (→ `Done` with the measured duration on a `succeeded` outcome; → `Halted`
    /// on `failed` / `aborted`, so a closed-unsuccessful stage reads `✕`, never a
    /// `✓` that misstates and never a hung Active line — the R2 Aborted arm). The
    /// redundant `<stage>.completed` markers are ignored — `summary.stageCompleted`
    /// carries the duration.
    let applyKind (board: Board) (code: string) (payload: Map<string, objnull>) : Board * Fold =
        if code.EndsWith ".started" then
            let key = code.Substring(0, code.Length - ".started".Length)
            if isUmbrella board key then board, Fold.NoChange
            else
                // A pre-seeded `Pending` stage flips to `Active` in place (keeping
                // its planned position); an unseeded stage appends; an already
                // Active / Done stage is a no-op (no spurious re-render).
                match board.Stages |> List.tryFind (fun s -> s.Key = key) with
                | Some { State = Pending } ->
                    { board with
                        Stages =
                            board.Stages
                            |> List.map (fun s -> if s.Key = key then { s with State = Active None } else s) }, Fold.Transitioned
                | Some _ -> board, Fold.NoChange
                | None -> { board with Stages = board.Stages @ [ { Key = key; State = Active None } ] }, Fold.Transitioned
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
                            |> List.map (fun ln -> if ln.Key = key then { ln with State = Active(Some prog) } else ln) }, Fold.Progressed
                | _ -> board, Fold.NoChange
            | _ -> board, Fold.NoChange
        elif code = "summary.stageCompleted" then
            match Map.tryFind "stage" payload with
            | Some s when not (isNull s) ->
                let key = string s
                if isUmbrella board key then board, Fold.NoChange
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
                                |> List.map (fun ln -> if ln.Key = key then { ln with State = closed } else ln) }, Fold.Transitioned
                    else
                        { board with Stages = board.Stages @ [ { Key = key; State = closed } ] }, Fold.Transitioned
            | _ -> board, Fold.NoChange
        elif Set.contains code noticeCodes then
            // A Warn ROLLUP envelope → the notice strip, replace-by-code (the
            // 2-3 identical model reads of one publish fold to ONE row; the
            // counts never double). A progressed fold: rendered promptly,
            // never dwelled — it is a strip update, not an arc transition.
            let replaced =
                if board.Notices |> List.exists (fun (c, _) -> c = code) then
                    board.Notices |> List.map (fun (c, p) -> if c = code then (c, payload) else (c, p))
                else
                    board.Notices @ [ (code, payload) ]
            { board with Notices = replaced }, Fold.Progressed
        elif Map.containsKey "suggestedConfig" payload then
            // #6 echo-the-fix — an envelope carrying a suggested config edit
            // ticks the live teaser (the same predicate the §11 rollup's
            // `suggestedConfigEdits` counter uses; the verdict panel ranks the
            // single biggest lever after the run).
            { board with SuggestedEdits = board.SuggestedEdits + 1 }, Fold.Progressed
        else board, Fold.NoChange

    /// Fold one envelope into the board as a plain did-it-change — the stored-run
    /// reconstruction (`boardOfStored`) and the pure board tests fold on this; the
    /// live drain loop folds on `applyKind` (it paces on the fold's weight).
    let apply (board: Board) (code: string) (payload: Map<string, objnull>) : Board * bool =
        let board', fold = applyKind board code payload
        board', (fold <> Fold.NoChange)

    /// Fold an envelope into the board (the `LogSink.Envelope` form).
    let applyEnvelope (board: Board) (env: LogSink.Envelope) : Board * bool =
        apply board env.Code env.Payload

    /// Fold an envelope into the board carrying the fold's weight (the live drain
    /// loop's form — dwell pacing keys off `Fold.Transitioned`).
    let applyEnvelopeKind (board: Board) (env: LogSink.Envelope) : Board * Fold =
        applyKind board env.Code env.Payload

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

    /// How long an ACTIVE stage may go without a new envelope before the line degrades
    /// its estimate to `processing` (2026-07-06, amends #20). A long SQL query or a
    /// CPU-bound emit is quiet-but-working, so the board never asserts `stalled` (a
    /// verdict the event stream cannot ground) and the spinner keeps breathing (the
    /// process is demonstrably alive). Wall-clock since the last dequeued envelope,
    /// measured by the drain loop.
    let stallThresholdMs : int64 = 5000L

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
    /// The between-reports word for an active stage that has gone quiet past the
    /// threshold (2026-07-06, amends #20's `stalled` — operator-chosen wording):
    /// the stage is still running (the body thread lives; the spinner keeps
    /// breathing), so the line says `processing` rather than a stall verdict the
    /// event stream cannot ground.
    let private processingText : string = "processing"

    /// The determinate progress BAR (2026-07-08, the widget-elevation program) — a
    /// fixed-width `▇▇▇▇▇░░░░░` gauge, shown ONLY when a real denominator is known.
    /// This is the honest reading of §13's "never a progress bar that misstates": a
    /// bar is drawn precisely when `Total > 0` (a genuine fraction), and an unknown
    /// total (`Total <= 0`, a lazily-streamed producer) draws NO bar — the plain
    /// count-up carries it instead. The R6 `Theme.meter` takes the TOTAL as its
    /// width (a 300-row stage would draw 300 blocks), so the fill is scaled to a
    /// constant `barCells` width here.
    let private barCells = 20
    let progressBar (p: Progress) : string =
        if p.Total <= 0 then ""
        else sprintf "%s " (Theme.meter (p.Done * barCells / p.Total) barCells)

    let progressTextQuiet (quietForMs: int64 option) (p: Progress) : string =
        let bar = progressBar p
        let count =
            if p.Total <= 0 then sprintf "%s applied" (Theme.humane p.Done)
            else sprintf "%s%s of %s" bar (Theme.humane p.Done) (Theme.humane p.Total)
        match quietForMs with
        | Some _ ->
            // The estimate degrades HONESTLY (#20 / §13): a stage that has gone quiet drops
            // the countdown (a frozen `~Ns remaining` keeps lying as the clock runs) and
            // reads `processing` — the work continues; nothing new has been reported.
            sprintf "%s · %s" count processingText
        | None ->
            if p.Total <= 0 then count
            else
                match etaText p with
                | Some eta -> sprintf "%s · %s" count eta
                | None     -> count

    /// The progress fragment with no quiet gap (the calm default — every existing caller).
    let progressText (p: Progress) : string = progressTextQuiet None p

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
    let lineTextWith (quietForMs: int64 option) (line: StageLine) : string =
        // The gerund names the stage (its identity on the board); the glyph
        // carries the state (faint `○` pending, `▸` active, `✓` done). A quiet gap
        // degrades only the ACTIVE line (the drain loop's wall-clock signal).
        match line.State with
        | Pending -> statementText (line.Key + ".started") Map.empty
        | Active prog ->
            let baseText = statementText (line.Key + ".started") Map.empty
            match prog with
            | Some p -> sprintf "%s · %s" baseText (progressTextQuiet quietForMs p)
            // A progress-less stage that has gone quiet says so in words, the same
            // register the progress fragment uses — a frozen dimmed spinner with no
            // explanation left the operator guessing (#20 rework; §13 honesty).
            | None   ->
                match quietForMs with
                | Some _ -> sprintf "%s · %s" baseText processingText
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

    /// The stage line with no quiet gap (the calm default — every existing caller / test).
    let lineText (line: StageLine) : string = lineTextWith None line

    /// The operator line for a notice-strip row — the Voice statement for the
    /// rollup code, with its action (the artifact pointer) appended when one
    /// rides. Pure + voiced, so the strip is testable like the stage lines.
    let noticeText (code: string) (payload: Map<string, objnull>) : string =
        let statement = statementText code payload
        match Voice.lookup code |> Option.bind (fun c -> c.Action payload) with
        | Some (View.Action a) -> sprintf "%s %s" statement a
        | _ -> statement

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

    // `phase` advances the ACTIVE line's breathing spinner (#20 follow-on); every other
    // state ignores it (its glyph is fixed). A static render passes phase 0.
    let private rowMarkup (phase: int) (quietForMs: int64 option) (line: StageLine) : string =
        let text = Markup.Escape(lineTextWith quietForMs line)
        match line.State with
        | Pending  -> sprintf "%s  %s" (Theme.muted Theme.pending)        (Theme.muted text)
        | Active _ ->
            // A live stage spins (accent + bold) — and KEEPS spinning through a quiet
            // gap (2026-07-06, amends #20's frozen frame): the drain loop repainting IS
            // evidence the process is alive, and a frozen glyph asserted a hang the
            // event stream could not ground. The quiet gap reads in the line's words.
            sprintf "%s  %s" (Theme.accent (Theme.spinner phase)) (Theme.bold text)
        | Done _   -> sprintf "%s  %s" Theme.ok                           (Theme.green text)
        | Halted _ -> sprintf "%s  %s" (Theme.red Theme.bad)              (Theme.red text)

    /// The board's rows — the optional run-title header above, the stage arc, and
    /// the done-frame (the §13 follow-on + recorded-run identity) once the run
    /// reaches its terminal stage.
    let private boardRows (phase: int) (quietForMs: int64 option) (board: Board) : IRenderable list =
        let titleRow =
            match titleText board with
            | Some t -> [ Markup(Theme.muted (Markup.Escape t)) :> IRenderable ]
            | None   -> []
        let stageRows = board.Stages |> List.map (fun s -> Markup(rowMarkup phase quietForMs s) :> IRenderable)
        // The notice strip (2026-07-02) — the rollup rows, muted with the warn
        // glyph, between the arc and the done-frame: present, calm, never a wall.
        let noticeRows =
            board.Notices
            |> List.map (fun (code, payload) ->
                Markup(sprintf "%s  %s" (Theme.yellow Theme.warn) (Theme.muted (Markup.Escape(noticeText code payload)))) :> IRenderable)
        // #6 echo-the-fix — the live teaser for accumulated config-edit
        // suggestions; the copy rides the catalog (`watch.suggestedEdits`).
        let suggestedRow =
            if board.SuggestedEdits = 0 then []
            else
                let text = statementText "watch.suggestedEdits" (Map.ofList [ "count", box board.SuggestedEdits ])
                [ Markup(sprintf "%s  %s" (Theme.yellow Theme.warn) (Theme.muted (Markup.Escape text))) :> IRenderable ]
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
        titleRow @ stageRows @ noticeRows @ suggestedRow @ doneRow

    /// Project the board onto a Spectre renderable — the live target the
    /// `LiveDisplayContext` updates in place. A STATIC render (stored board, tests)
    /// passes spinner phase 0; the live drain loop threads the advancing phase.
    let toRenderable (board: Board) : IRenderable =
        Rows(boardRows 0 None board) :> IRenderable

    /// True iff a stage is in progress — the live drain loop pulses the spinner only
    /// when there is an active line to breathe (no idle churn otherwise).
    let hasActiveStage (board: Board) : bool =
        board.Stages |> List.exists (fun s -> match s.State with Active _ -> true | _ -> false)

    /// Like `toRenderable`, but with leading header rows above the arc — the
    /// cutover timeline strip (`DYNAMIC_DISPLAY` §4: where this run sits on the
    /// path to the R6 gate). An empty header is the bare board (unchanged). `phase`
    /// advances the active line's breathing spinner (#20 follow-on).
    let toRenderableWith (header: IRenderable list) (phase: int) (quietForMs: int64 option) (board: Board) : IRenderable =
        Rows(header @ boardRows phase quietForMs board) :> IRenderable

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

    /// The stall threshold for this process — the default, overridable via
    /// `PROJECTION_WATCH_STALL_MS` (perceptual tuning + a deterministic seam for
    /// the stall-render tests, the same shape as the dwell override).
    let resolveStallThresholdMs () : int64 =
        match Environment.GetEnvironmentVariable "PROJECTION_WATCH_STALL_MS" with
        | null -> stallThresholdMs
        | s ->
            match Int64.TryParse s with
            | true, v when v >= 0L -> v
            | _ -> stallThresholdMs

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

    /// Run `body` under a live stage board on stderr, driven by the `LogSink` stage
    /// stream. The board updates in place as stages fill in; each visible transition is
    /// held ≥ the dwell floor before the next is shown. Channel 1 (NDJSON) is suppressed
    /// for the duration so it does not interleave with the live region; the board renders
    /// exactly what channel 1 would have written (one substrate, two lenses).
    ///
    /// **The dwell is OFF the emitting thread (#20).** The subscriber only ENQUEUES an
    /// envelope (fast, inside emit's lock) and returns, so `emit` NEVER sleeps under the
    /// `LogSink` lock — the board is safe for a future concurrent realization stream. The
    /// drain loop on the ctx-affine Live thread folds the board, sleeps the dwell remainder,
    /// and refreshes; `body` runs on a BACKGROUND thread (it is the producer). Everything
    /// but the queue stays drain-loop-local (`board` / `sw` / `lastRenderAt`), so there is
    /// no shared-state race — the queue (FIFO, thread-safe) carries the order `emit`'s single
    /// lock already serialized. Teardown: `body`'s `finally` completes the queue, the loop
    /// drains the remainder (the done-frame is never lost), `clearSubscribers` runs, then we
    /// JOIN `body` for its exit code — so the caller still sees one synchronous, deterministic
    /// call (`Live.Start` blocks here), which is why `WatchInjectionTests` can assert on the
    /// final board with the dwell pinned to 0.
    /// The contained instrument box (2026-07-02, the operator-shell charter) —
    /// the live target wraps in a rounded panel so every run presents as ONE
    /// fixed-viewport box on the terminal. No alternate screen: the Live
    /// region repaints in place and the final frame stays in scrollback (the
    /// operator keeps the record after exit).
    let private boxed (inner: IRenderable) : IRenderable =
        let panel = Panel(inner)
        panel.Border <- BoxBorder.Rounded
        panel :> IRenderable

    let renderWatchOn (console: IAnsiConsole) (seed: Board) (floorMs: int64) (body: unit -> int) : int =
        let board = ref seed
        let header = cutoverHeader ()
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let stallMs = resolveStallThresholdMs ()
        // Three clocks (#20 rework, 2026-07-02). Splitting them is the fix for the
        // hang family: one clock cannot honestly pace the dwell, the heartbeat AND
        // the stall verdict at once.
        //   lastTransitionAt — dwell pacing: only stage TRANSITIONS are floored, so
        //     each arc change stays legible without turning a progress flood into a
        //     backlog replay.
        //   lastPaintAt — heartbeat pacing: the spinner breathes on wall clock,
        //     regardless of whether the queue is chatty (the old loop only breathed
        //     on a TryTake TIMEOUT, so a continuous envelope stream froze it).
        //   lastEventAt — liveness: ANY dequeued envelope proves the pipeline is
        //     alive; `stalled` is now-lastEventAt, never now-lastRender (the old
        //     conflation called a quiet-but-working stage stalled at 3s).
        let mutable lastTransitionAt = 0L
        let mutable lastPaintAt = 0L
        let mutable lastEventAt = 0L
        let mutable code = 0
        // House-NEW concurrency primitive (grep: the first BlockingCollection in src) — its
        // re-open is gated by the live board going off-thread (CLAUDE.md §7; DECISIONS 2026-06-19).
        let queue = new System.Collections.Concurrent.BlockingCollection<LogSink.Envelope>()
        console.Live(boxed (toRenderableWith header 0 None board.Value)).Start(fun ctx ->
            // Enqueue-and-return: the subscriber holds emit's lock only for the `Add`. A late
            // emit after `CompleteAdding` throws — swallowed; no such emit exists in practice
            // (the body's `withWriter` returns before the queue is completed). FORWARD NOTE:
            // when the concurrent realization stream this off-thread move enables actually
            // lands, re-check this swallow — a second emitter racing the
            // [CompleteAdding .. clearSubscribers] window could drop a frame silently.
            LogSink.addSubscriber (fun env -> try queue.Add env with _ -> ())
            // `body` on a background thread — it is the producer; the drain loop below owns
            // THIS (ctx-affine) thread. `withWriter Null` nulls channel 1 for the body's span
            // (saved/restored — so the board composes under an outer `--pretty` null exactly
            // as before); the `finally` completes the queue to signal the drain to finish.
            let bodyTask =
                System.Threading.Tasks.Task.Run(fun () ->
                    try LogSink.withWriter System.IO.TextWriter.Null body
                    finally queue.CompleteAdding())
            try
                let mutable draining = true
                // The breathing spinner's render tick (#20 follow-on) — advanced on EVERY
                // render (a folded frame OR an idle wake), so the active stage visibly breathes.
                let mutable phase = 0
                // One paint path — every repaint advances the spinner and stamps the
                // heartbeat clock, whatever prompted it.
                let paint (quietForMs: int64 option) =
                    phase <- phase + 1
                    lastPaintAt <- sw.ElapsedMilliseconds
                    ctx.UpdateTarget(boxed (toRenderableWith header phase quietForMs board.Value))
                    ctx.Refresh()
                // The quiet gap the paint reacts to (2026-07-06, amends #20's stall
                // verdict): wall-clock since the last DEQUEUED envelope, surfaced only
                // past the threshold. The line degrades to `processing` (the stale ETA
                // drops) and the spinner keeps breathing — never a `stalled` verdict.
                let quietGap () =
                    let gap = sw.ElapsedMilliseconds - lastEventAt
                    if gap > stallMs then Some gap else None
                while draining do
                    let mutable env = Unchecked.defaultof<LogSink.Envelope>
                    if queue.TryTake(&env, 100) then
                        // COALESCE (#20 rework): fold everything already queued into ONE
                        // frame, stopping after the first stage TRANSITION — transitions
                        // are bounded by the spine and each deserves its dwelled frame;
                        // progress and foreign envelopes are the unbounded flood, and
                        // folding them one-frame-per-envelope (each dwelled 120ms) is how
                        // the board lagged minutes behind and replayed a backlog after the
                        // run had already finished.
                        let mutable batch = Fold.NoChange
                        let mutable take = true
                        while take do
                            lastEventAt <- sw.ElapsedMilliseconds
                            let board', fold = applyEnvelopeKind board.Value env
                            board.Value <- board'
                            batch <- strongestFold batch fold
                            if batch = Fold.Transitioned then take <- false
                            elif not (queue.TryTake(&env, 0)) then take <- false
                        match batch with
                        | Fold.Transitioned ->
                            // The dwell floor paces stage transitions ONLY — the previous
                            // arc frame stays legible ≥ the floor before this one replaces
                            // it. Progress frames render immediately below.
                            let sleep = dwellMs floorMs lastTransitionAt sw.ElapsedMilliseconds
                            if sleep > 0L then System.Threading.Thread.Sleep(int sleep)
                            lastTransitionAt <- sw.ElapsedMilliseconds
                            paint None
                        | Fold.Progressed ->
                            paint None
                        | Fold.NoChange ->
                            // A batch of foreign envelopes changed nothing visible — but
                            // the pipeline is chatty-alive, so keep the spinner breathing
                            // on the wall-clock heartbeat (the old loop never repainted
                            // here, which froze the board for the whole flood).
                            if hasActiveStage board.Value && sw.ElapsedMilliseconds - lastPaintAt >= 100L then
                                paint (quietGap ())
                    elif queue.IsCompleted then
                        draining <- false
                    elif hasActiveStage board.Value then
                        // Idle wake (no envelope in 100ms) with a stage in progress — advance
                        // the spinner and refresh so the active line breathes between events. No
                        // fold, no dwell sleep (the dwell is a floor on TRANSITIONS, not added
                        // here). If the pipeline has gone quiet past the threshold, the line
                        // reads `processing` — honest, not a countdown that keeps lying while
                        // nothing new is reported, and never a `stalled` verdict the event
                        // stream cannot ground. Liveness keys off lastEventAt: any envelope
                        // proves work, board-visible or not.
                        paint (quietGap ())
                // Join the producer for its exit code — `GetAwaiter().GetResult()` so a body
                // exception propagates as ITSELF, not wrapped in `AggregateException`.
                code <- bodyTask.GetAwaiter().GetResult()
            finally
                // Reap the producer on EVERY exit path. The happy path joined above; this also
                // covers a RENDER throw — `ctx.UpdateTarget`/`Refresh` can raise `IOException`
                // on a TTY whose pipe broke / terminal closed mid-run — which would otherwise
                // bypass the join and ORPHAN the body on a pool thread with the global writer
                // pinned to `Null`. Swallowed so the render exception stays primary; the body's
                // own bracket records its outcome regardless.
                (try bodyTask.GetAwaiter().GetResult() |> ignore with _ -> ())
                LogSink.clearSubscribers ())
        code

    /// Production wrapper — the live board on stderr (channel 2), mirroring
    /// `TtyRenderer`'s console creation. Takes the SEED board (2026-07-02 —
    /// the shell frames it with the run title before handing it over; a bare
    /// `seededOf spine` renders the unframed arc exactly as before). Tests
    /// drive `renderWatchOn` with a `Spectre.Console.Testing.TestConsole` to
    /// assert the board (and the channel-1 suppression / prior-writer
    /// restoration) without a real TTY.
    let renderWatch (seed: Board) (floorMs: int64) (body: unit -> int) : int =
        // The live board only runs on a real terminal (`shouldWatch` gates on a
        // non-redirected stderr), so the factory pins no width here; it does honor
        // `NO_COLOR` / `CLICOLOR_FORCE` so a no-color operator watching a run gets
        // the plain board, the same as the verdict panel.
        let console = View.consoleTo Console.Error
        renderWatchOn console seed floorMs body
