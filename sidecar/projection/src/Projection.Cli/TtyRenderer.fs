module Projection.Cli.TtyRenderer

open System
open Spectre.Console
open Projection.Core
open Projection.Pipeline

/// Tier-3 reporting (`REPORTING_HORIZON.md`; `docs/logging-format.md` §15.3) —
/// the Spectre.Console "channel 2" surface. Now a **consumer of the `View`
/// primitive**: each `build…View` produces a typed document; `View.write`
/// renders it (pretty on a colored console, plain on a `NoColors` one) and
/// `View.toJson` is the same document as structure. The renderers are derived
/// consumers of `LogSink` + the ledger, never a second emit surface.

/// True iff a pretty render is warranted: the operator asked for it AND
/// stderr is a real terminal (not a pipe / file). Per §15.1 — never draw
/// ANSI into a redirected stream.
let shouldRender (prettyRequested: bool) : bool =
    prettyRequested && not Console.IsErrorRedirected

// --- the verdict panel as a View -------------------------------------------

/// Build the terminal verdict panel `View` from the run's `LogSink` state +
/// the ledger. Pure data — `View.write` / `View.toJson` are the lenses.
let buildSummaryView (command: string) (code: int) : View.View =
    let registered, applied, declined = LogSink.transformCounts ()
    // The verdict line is voiced by `Voice`, keyed by the code the run earned:
    // the round-trip-verification proof (`canary.*`, §6) when a canary leg ran,
    // else the terminal outcome (`summary.runComplete`, §3). The copy is no longer
    // authored here — `TtyRenderer` looks it up by code (`THE_VOICE_INTEGRATION.md`
    // slice 1; the `code ⇔ copy` totality test holds it honest).
    let verdict =
        let codeForVerdict, payload : string * Voice.Payload =
            match LogSink.canaryVerdict () with
            | Some "green" -> "canary.diffEmpty", Map.empty
            | Some "red"   -> "canary.divergence", Map.empty
            | _ ->
                "summary.runComplete",
                Map.ofList [ "outcome", box (if code = 0 then "succeeded" else "failed") ]  // LINT-ALLOW: terminal TtyRenderer payload box into the Map<string,objnull> LogSink boundary
        match Voice.verdict codeForVerdict payload with
        | Some (st, t) -> View.PanelRow.Labeled("verdict", t, st)
        | None ->
            View.PanelRow.Labeled(
                "verdict",
                (if code = 0 then "The run completed without error." else "Stopped before completion."),
                (if code = 0 then View.Ok else View.Bad))
    let transforms =
        View.PanelRow.Labeled(
            "transforms",
            sprintf "%d registered %s %d applied %s %d declined" registered Theme.dot applied Theme.dot declined,
            View.Neutral)
    let edits = LogSink.suggestedConfigEdits ()
    let actionable =
        if edits = 0 then View.PanelRow.Labeled("actionable", "none", View.Ok)
        else
            // Impact-ranked — name the single biggest lever first.
            match LogSink.topSuggestion () with
            | Some (path, count) ->
                View.PanelRow.Labeled("actionable", sprintf "%d edit(s) %s top: %s (%d)" edits Theme.dot path count, View.Warn)
            | None -> View.PanelRow.Labeled("actionable", sprintf "%d edit(s) suggested" edits, View.Warn)
    // §6 — the Measure proof: the data norm (CDC capture count) made plain. A
    // CDC-silent leg is the green hush of an idempotent redeploy ("unchanged");
    // a captured count names exactly how many rows changed (rows changed = the
    // CDC count). Rendered only when the run had a CDC-measured leg
    // (`LogSink.cdcMeasure` is `Some`), so a measure-less run shows today's panel.
    let measure =
        match LogSink.cdcMeasure () with
        | Some 0 -> [ View.PanelRow.Labeled("data", "unchanged · CDC captured 0 rows", View.Ok) ]
        | Some n -> [ View.PanelRow.Labeled("data", sprintf "CDC captured %s rows" (Theme.humane n), View.Neutral) ]
        | None   -> []
    // Principle #5 — end with the next action.
    let nextAction = if edits > 0 then [ View.PanelRow.Next "projection suggest-config --apply" ] else []
    let cutover =
        match RunLedger.configuredDir () with
        | Some dir ->
            let r = RunLedger.read dir |> RunLedger.readiness
            let gate = if r.Eligible then "ELIGIBLE" else "not yet"
            [ View.PanelRow.Gauge(
                "cutover", r.ConsecutiveGreen, r.Threshold,
                sprintf "%d / %d green %s %s" r.ConsecutiveGreen r.Threshold Theme.arrow gate) ]
        | None -> []
    View.Panel(command, [ verdict; transforms ] @ measure @ [ actionable ] @ nextAction @ cutover)

let renderSummaryTo (console: IAnsiConsole) (command: string) (code: int) : unit =
    View.write console (buildSummaryView command code)

/// Render the verdict panel to stderr (channel 2 — the panel is a rendering
/// of events; stdout stays the narration surface).
let renderSummary (command: string) (code: int) : unit =
    let console = View.consoleTo Console.Error
    renderSummaryTo console command code

// --- the readiness board as a View -----------------------------------------

/// Build the cutover-readiness board `View` — hero answer first, then the R6
/// meter, the canary-history dots, the run totals, the ledger note.
let buildReadinessView (r: RunLedger.Readiness) (recent: string list) (series: int list) (ledgerPath: string) : View.View =
    let toGo = max 0 (r.Threshold - r.ConsecutiveGreen)
    let hero =
        if r.Eligible then
            View.Hero(View.Ok, sprintf "ELIGIBLE %s %d consecutive green canaries" Theme.dot r.ConsecutiveGreen)
        else
            View.Hero(View.Pending, sprintf "NOT YET %s %d green run(s) to cutover-ready" Theme.dot toGo)
    // The one lever (§8 / Appendix A.5: "One lever, named, with the next move" —
    // never a list of problems). Derived from the data already in the readiness
    // model: when the streak is broken (the most recent round-trip verification
    // diverged), THAT is the single blocking item — restoring a green check is the
    // honest next step; otherwise the lever is the remaining distance, the streak
    // read as distance to cutover. Rendered only while not yet eligible.
    let lever =
        if r.Eligible then []
        else
            match r.LastCanary with
            | Some "green" ->
                [ View.Note(sprintf "The lever %s %d more green round-trip verification(s) before cutover." Theme.dot toGo) ]
            | Some _ ->
                [ View.Note(sprintf "The lever %s the most recent round-trip verification diverged; a green check restores the streak." Theme.dot) ]
            | None ->
                [ View.Note(sprintf "The lever %s a round-trip verification has not yet run; the first green check opens the streak." Theme.dot) ]
    let history = if List.isEmpty recent then [] else [ View.Dots("history", recent) ]
    // #14 — the changeset trend beside the dots: how much the model is still moving
    // per run, as a sparkline. A settling model (fewer changes toward cutover) reads
    // as a falling line. Needs ≥ 2 points to be a trend; otherwise it stays silent.
    let trend = if List.length series >= 2 then [ View.Spark("changes / run", series) ] else []
    // The timeline read in words — the dots' shape said plainly (§8 / Appendix
    // A.5): how the recent checks landed, and which run is the present one.
    let timeline =
        if List.isEmpty recent then []
        else
            let n = List.length recent
            let diverged = recent |> List.filter (fun v -> v <> "green") |> List.length
            let shape =
                if diverged = 0 then sprintf "the last %d check(s) all passed" n
                else sprintf "the last %d check(s) %s %d passed %s %d diverged" n Theme.dot (n - diverged) Theme.dot diverged
            let here = if r.TotalRuns > 0 then sprintf " %s run %d, the present one" Theme.dot r.TotalRuns else ""
            [ View.Note(shape + here) ]
    let lastCanary = match r.LastCanary with Some c -> c | None -> "—"
    View.Doc(
        [ View.Blank
          hero
          View.Blank
          View.Meter("cutover", r.ConsecutiveGreen, r.Threshold, sprintf "%d / %d green" r.ConsecutiveGreen r.Threshold) ]
        @ lever
        @ history
        @ trend
        @ timeline
        @ [ View.Field(
              "runs",
              sprintf "%d total %s %d with a canary %s last %s" r.TotalRuns Theme.dot r.CanaryRuns Theme.dot lastCanary,
              View.Neutral)
            View.Blank
            View.Note(sprintf "ledger    %s" ledgerPath) ])

let renderReadinessBoardTo
    (console: IAnsiConsole)
    (r: RunLedger.Readiness)
    (recent: string list)
    (series: int list)
    (ledgerPath: string)
    : unit =
    View.write console (buildReadinessView r recent series ledgerPath)

// --- the Setup readback as a View (§14 / Appendix A.6) ---------------------

/// Build the arrival/setup readback `View` — a plain read of what is configured
/// and what is not, in the same calm voice (`THE_VOICE.md` §14: "a thing not
/// configured is a choice to make, not a failure"). Pure over the resolved
/// state so the env reads + the live probe stay at the boundary (`runSetup`); an
/// unset optional (the run ledger) earns a recommendation, never a scold.
/// `connection` is `(ref, reachable, grants)` when a target was probed, where
/// `grants` pairs each planned write action with its database-scope grant status
/// (D3 — the broader INSERT / CREATE TABLE / DELETE grants, not ALTER alone).
let buildSetupView
    (ledger: string option)
    (executeArmed: bool)
    (dwellMs: int64)
    (benchDir: string option)
    (connection: (string * bool * (Preflight.WriteAction * bool) list) option)
    : View.View =
    let history =
        match ledger with
        | Some dir -> View.Field("history", sprintf "retained %s %s" Theme.dot dir, View.Ok)
        | None     -> View.Field("history", "not retained", View.Neutral)
    let writes =
        if executeArmed then View.Field("live writes", "armed", View.Warn)
        else View.Field("live writes", "preview only", View.Ok)
    let board = View.Field("live board", sprintf "%d ms dwell" dwellMs, View.Neutral)
    let bench =
        match benchDir with
        | Some dir -> View.Field("bench output", dir, View.Neutral)
        | None     -> View.Field("bench output", "off", View.Neutral)
    // The live probe (only when a target was given) — reachability, then the
    // grant beneath it (the grant is unknowable until the target is reachable).
    let connectionBlock =
        match connection with
        | None -> []
        | Some (ref, reachable, grants) ->
            let connField =
                if reachable then View.Field("connection", sprintf "%s %s reachable" ref Theme.dot, View.Ok)
                else View.Field("connection", sprintf "%s %s unreachable" ref Theme.dot, View.Bad)
            // The ALTER line is preserved (the §14 / A.6 readback that already
            // shipped); the broader write grants (INSERT / CREATE TABLE / DELETE)
            // read on their own line — the granted set, or the missing set named
            // exactly (mirroring `buildSurveyView`'s "missing X, Y" phrasing).
            let alterGranted = grants |> List.exists (fun (a, g) -> a = Preflight.WriteAction.Alter && g)
            let grantField =
                if not reachable then []
                elif alterGranted then [ View.Field("grant", "ALTER granted", View.Ok) ]
                else [ View.Field("grant", "ALTER not granted", View.Warn) ]
            let dataActions =
                grants |> List.filter (fun (a, _) -> a <> Preflight.WriteAction.Alter)
            let writesField =
                if not reachable || List.isEmpty dataActions then []
                else
                    let missing = dataActions |> List.filter (fun (_, g) -> not g) |> List.map (fun (a, _) -> Preflight.permissionName a)
                    match missing with
                    | [] -> [ View.Field("writes", "INSERT, CREATE TABLE, DELETE granted", View.Ok) ]
                    | _  ->
                        // Terminal View-field operator copy at the setup-readback boundary:
                        // join the (≤3) missing permission-name strings into the "missing
                        // X, Y" phrasing, the same primitive the sibling `buildSurveyView`
                        // "missing" line uses at the same boundary. (1) lib: String.concat;
                        // (2) already in codebase; (3) cost: none (typed string list → one
                        // terminal string); (4) no typed-AST builder applies (leaf copy).
                        let missingList = missing |> String.concat ", "  // LINT-ALLOW: see four-question rationale above
                        [ View.Field("writes", sprintf "missing %s" missingList, View.Warn) ]
            (connField :: grantField) @ writesField
    let recommendation =
        match ledger with
        | Some _ -> []
        | None   ->
            [ View.Blank
              View.Note "Run history is not being retained. To keep a record of runs over time, set PROJECTION_LEDGER_DIR." ]
    View.Doc(
        [ View.Blank; View.Hero(View.Neutral, "Setup"); View.Blank
          history; writes; board; bench ]
        @ connectionBlock
        @ recommendation)

// --- the capability survey matrix (prototype) ------------------------------

/// Build the capability-survey `View` — the whole estate's declared-vs-actual
/// capability matrix (`HANDOFF_CAPABILITY_SURVEY_2026_06_09.md`). The verdict
/// leads (every place ready, or N need attention); each environment reads its
/// state plainly — covered / missing the named activities / unreachable / no
/// live gate. Pure over the probed reports.
let buildSurveyView (reports: CapabilitySurvey.EnvironmentReport list) : View.View =
    // G0b (P10) — the user-directory readability fragment appended to a
    // reachable place's line: the platform user table the golden/preview re-key
    // matches against, and whether it carries an email key. It reads plainly —
    // "users email-keyed" / "users (no email key)" / "no user directory".
    let userDirText (u: Projection.Adapters.Sql.ReadSide.UserDirectoryProbe) : string =
        match u.Found, u.EmailKeyed with
        | true, true  -> sprintf " %s users email-keyed" Theme.dot
        | true, false -> sprintf " %s users (no email key)" Theme.dot
        | false, _    -> sprintf " %s no user directory" Theme.dot
    let field (r: CapabilitySurvey.EnvironmentReport) =
        let value, status =
            if not r.Connected then "no live gate (file or ephemeral)", View.Neutral
            elif not r.Reachable then "unreachable", View.Bad
            elif not (List.isEmpty r.Missing) then
                sprintf "reachable %s missing %s" Theme.dot (r.Missing |> List.map CapabilitySurvey.Capability.text |> String.concat ", "), View.Warn  // LINT-ALLOW: terminal Spectre console text at the TtyRenderer boundary
            elif r.GrantUnreadable then
                // NM-55 — reachable but the grant could not be read: unverified,
                // not covered.
                sprintf "reachable %s grant unreadable (coverage unverified)" Theme.dot, View.Warn
            else
                // NM-54 — surface an unverified CDC axis rather than reading a
                // clean "no CDC": the probe could not be taken, so the verdict is
                // advisory (Warn), not Ok.
                let cdc =
                    if r.CdcProbeFailed then sprintf " %s CDC unverified" Theme.dot
                    elif r.CdcTracked then sprintf " %s CDC-tracked" Theme.dot
                    else ""
                let status = if r.CdcProbeFailed then View.Warn else View.Ok
                sprintf "reachable %s grant covered%s%s" Theme.dot cdc (userDirText r.UserDirectory), status
        View.Field(r.Name, value, status)
    let needAttention =
        reports
        |> List.filter CapabilitySurvey.blocked
        |> List.length
    let verdict =
        if needAttention = 0 then
            View.Hero(View.Ok, "Every connected environment can do what the pipeline asks of it.")
        else
            View.Hero(View.Warn, sprintf "%d environment(s) need attention before a live run." needAttention)
    View.Doc([ View.Blank; verdict; View.Blank ] @ (reports |> List.map field))

/// The perf bench table as a `View` (#13) — the perf surface joins the one lens:
/// color on a TTY, plain when piped, structured via `--format` / `--query`. Core's
/// `Bench.renderTable` stays (the perf-gate's plain dump reads it); this is the
/// Cli-side projection so the `-v` dump is a `View.Table`, not a separate text path
/// with no machine lens. Cells are `Neutral` — bench numbers are evidence, never a
/// verdict, so no status glyph colors them.
let benchView (stats: Bench.Stats list) : View.View =
    let headers = [ "label"; "count"; "total ms"; "min"; "mean"; "p50"; "p95"; "p99"; "max" ]
    let row (s: Bench.Stats) : (string * View.Status) list =
        [ s.Label,                 View.Neutral
          string s.Count,          View.Neutral
          string s.TotalMs,        View.Neutral
          string s.MinMs,          View.Neutral
          sprintf "%.1f" s.MeanMs, View.Neutral
          string s.P50Ms,          View.Neutral
          string s.P95Ms,          View.Neutral
          string s.P99Ms,          View.Neutral
          string s.MaxMs,          View.Neutral ]
    View.Doc [ View.Note "Bench (sorted by total time)"; View.Table(headers, stats |> List.map row) ]

/// Build the flow-menu `View` — the no-argument `projection` answer ("the
/// config IS the menu", THE_CLI.md §4.4), as one document the three lenses
/// share: a hero naming the daily act, then the flows as a table
/// (name / route / notes). 2026-07-02 — previously plain Console.Out lines,
/// invisible to `--json` / `--query` and unstyled under pretty.
let buildFlowMenuView (flows: (string * string * string * string) list) : View.View =
    let headers = [ "flow"; "from"; "to"; "notes" ]
    let row (name, source, target, extras) : (string * View.Status) list =
        [ name,   View.Neutral
          source, View.Neutral
          target, View.Neutral
          extras, View.Neutral ]
    View.Doc
        [ View.Note "Flows — the daily act is `projection <flow>` (preview by default; --go applies)."
          View.Table(headers, flows |> List.map row) ]

let renderReadinessBoard (r: RunLedger.Readiness) (recent: string list) (series: int list) (ledgerPath: string) : unit =
    // The board renders on every `readiness` (not just on a TTY). The factory
    // pins a width when piped (Spectre's auto-width collapses lines on a non-TTY)
    // and still strips color for the non-terminal sink.
    let console = View.consoleTo Console.Out
    renderReadinessBoardTo console r recent series ledgerPath

// --- the answer surface — render any View to stdout (INSTRUMENT slice 1) ----

/// The global `--query` selector (#17), set by `Program.main` from the `--query
/// <path>` flag. When present, the answer surface emits the JSONPath-subset slice
/// of `View.toJson` (`Query.render`) instead of the pretty or full-JSON lens — the
/// structured lens narrowed to what the operator asked for. `None` (the default)
/// leaves the answer untouched. It is a global — like `OperatorConsole.verboseMode`
/// / `prettyMode` — because it filters EVERY answer regardless of verb; it lives
/// here, not beside those, only because `renderAnswer` (its single reader) compiles
/// before `OperatorConsole`.
let queryPath : string option ref = ref None

/// The global `--open <path>` selector (#18) — the headless half of the dig. When
/// `Some path`, the answer surface force-reveals exactly that child-index branch
/// (`View.RenderOptions.OpenPath`) while every other branch stays at the ambient
/// `--depth` — a focus tool that composes with `--depth` and `--query`. `None` (the
/// default) leaves the render at the calm whole-tree-to-depth, byte-identical.
let openPath : int list option ref = ref None

/// Render any `View` to stdout — the "answer" surface (stdout carries the answer;
/// structured events stay on stderr). Pretty (color) on a TTY; plain when piped
/// (width pinned so lines don't collapse); the dig is revealed to `depth` levels.
/// `--format json` emits the same document as structure (`View.toJson`) — always
/// the full tree — so the human and machine lenses are the one value. `--query`
/// (`queryPath`) walks that SAME tree to a slice — the structured lens, narrowed.
let renderAnswer (asJson: bool) (depth: int) (v: View.View) : unit =
    match queryPath.Value with
    | Some path ->
        // The query walks the SAME `toJson` the json lens emits — one substrate,
        // narrowed. JSON text, so it stays on stdout (the answer channel).
        Console.Out.WriteLine(Query.render path (View.toJson v))
    | None ->
        if asJson then
            Console.Out.WriteLine((View.toJson v).ToJsonString())
        else
            let console = View.consoleTo Console.Out
            // #11/#18 — `depth` is the calm ambient; `--open` (openPath) force-reveals one
            // branch beyond it. With both at their defaults this is exactly `writeToDepth`.
            View.writeWith
                { View.defaultOptions with Depth = depth; Width = console.Profile.Width; OpenPath = openPath.Value }
                console v

/// Voice a refusal to STDERR (the §5 channel split — errors never on stdout).
/// The coded `ValidationError` becomes a register-correct `Surface` via
/// `Voice.errorSurface`, rendered through the same `View` engine that draws the
/// answer — so a refusal speaks in the operator register, not raw prose.
let renderVoicedError (error: Projection.Core.ValidationError) : unit =
    let view = Surface.render (Voice.errorSurface error)
    let console = View.consoleTo Console.Error
    View.writeToDepth console View.defaultDepth view

// --- the Gate surface — a refusal as a stop-and-confirm (INSTRUMENT slice 3) -

/// The Gate as a `Surface` (INSTRUMENT slice 3) — the stop-and-confirm a refusal
/// renders. Statement-first: a Hero names the danger in plain words (Bad for a
/// destructive change the operator must declare; Warn for a blocking pre-flight
/// refusal); the substantiation is a `Disclosure` carrying the formal proof —
/// the gate label, the specific detail, and the distinct exit code — open by
/// default but collapsible for the operator who already knows the drop; the next
/// action names how to proceed. Binds `Preflight.GateRefusal` — the structured
/// refusal that otherwise collapses to a single error string.
let buildGateSurface (command: string) (refusal: Preflight.GateRefusal) : Surface.Surface =
    // The §5 gate copy is voiced centrally by `Voice.gateSurface`, keyed by the
    // closed `Preflight.GateLabel` DU (the gate⇔copy totality) — `TtyRenderer`
    // no longer authors the refusal prose.
    Voice.gateSurface command refusal

/// The Gate `View`.
let buildGateView (command: string) (refusal: Preflight.GateRefusal) : View.View =
    Surface.render (buildGateSurface command refusal)

/// Render the Gate to stderr (a refusal is an event surface; stdout stays the
/// answer/narration surface).
let renderGate (command: string) (refusal: Preflight.GateRefusal) : unit =
    let console = View.consoleTo Console.Error
    View.write console (buildGateView command refusal)

// --- the error surface — refusals & errors as voice (slice 4) ---------------

/// Build the `View` for a `ValidationError list` — the §10/§14 frame voiced by
/// `Voice.errorsSurface` (statement-first; the located causes + codes beneath).
let buildErrorsView (errors: ValidationError list) : View.View =
    Surface.render (Voice.errorsSurface errors)

/// Render a `ValidationError list` to a chosen writer as the voiced §10/§14
/// surface — the operator reads a plain statement and the next move; the codes
/// ride in the substantiation, never on the statement line. The structured
/// NDJSON (`config.validationFailed` etc.) remains the machine channel, unchanged.
let renderErrorsTo (writer: System.IO.TextWriter) (errors: ValidationError list) : unit =
    if not (List.isEmpty errors) then
        // The factory pins a width when the sink is not a real terminal (piped /
        // file) so the grid cells don't collapse; color is stripped for the
        // non-terminal sink.
        let console = View.consoleTo writer
        View.write console (buildErrorsView errors)

/// Render a `ValidationError list` to stderr (the common case).
let renderErrors (errors: ValidationError list) : unit =
    renderErrorsTo Console.Error errors

// --- a voiced code's surface to a writer (slice 1 catalog, §3/§6 inline) -----

/// Render a voiced code's §-surface (statement over substantiation, ending on
/// the move) to a writer — the catalog copy (`Voice.surfaceOf`) projected
/// through the same `View` engine that draws the gate and the answer. The
/// structured NDJSON channel (`LogSink.emit`) is unchanged; this is the human
/// lens for a §6 proof / §3 verdict an executor narrates inline. NM-47: an
/// UNVOICED code no longer renders nothing (an invisible operator verdict) — it
/// falls back to a plain located narration of the raw code + payload
/// (`Voice.fallbackSurface`), so a typo'd or newly-added face code is loud, not
/// silent. The totality assertion pins every literal code passed here as voiced,
/// keeping the fallback unreached in production.
let renderVoicedTo (writer: System.IO.TextWriter) (code: string) (payload: Voice.Payload) : unit =
    let surface =
        match Voice.surfaceOf code payload with
        | Some surface -> surface
        | None         -> Voice.fallbackSurface code payload
    let console = View.consoleTo writer
    View.write console (Surface.render surface)
