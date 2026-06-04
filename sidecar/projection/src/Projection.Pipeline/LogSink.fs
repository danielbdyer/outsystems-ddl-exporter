namespace Projection.Pipeline

// LINT-ALLOW-FILE: structured-logging substrate at the egress boundary. `DateTime.UtcNow` is
//   the wall-clock event-timestamp source for operator-facing log events (the
//   logging-sink sibling of Bench.fs's time boundary); the process-scoped
//   RunAccumulator uses function-local mutables behind a single lock (documented
//   in the module header) and `box` at the System.Text.Json value boundary.
//   Log events are boundary I/O, not deterministic artifacts.

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Projection.Core

/// V2's structured event-emission substrate per `docs/logging-format.md`
/// (chapter B.4 slice 1) + the §11 roll-up aggregator (chapter B.4
/// slice 6.5). Hand-rolled per §15.2 — no `Microsoft.Extensions.Logging`,
/// no `Serilog`, no third-party logger. `LogSink` IS the logger;
/// `System.Text.Json` is the serialization surface (already a V2
/// dependency via `BenchSink.persistJson`).
///
/// Channel 1 (default, always-on): NDJSON to stderr. One event per
/// line; UTF-8; no pretty-printing on the wire. Per §5: stderr is
/// reserved for structured events; stdout is for artifact data when
/// operators pipe.
///
/// Channel 2 (Spectre.Console TtyRenderer): out of scope for chapter
/// B.4 per §15.3 + `DECISIONS 2026-05-20 (logging-format
/// implementation gap audit)`. Opens as its own micro-chapter when
/// the operator-pull trigger surfaces.
///
/// **Per-run state.** A process-scoped `RunAccumulator` records (a) the
/// running envelope list for snapshot/test, (b) per-`(category, code,
/// ssKey)` `GroupAccumulator` aggregating count + chronological
/// samples, (c) per-level event counts, (d) stage timings, (e)
/// artifact entries, (f) the suggested-config edit count. Built ONCE
/// during emission per the §11 Big-O constraint — `runComplete` reads
/// the accumulator rather than re-scanning the event stream.
///
/// **Threading.** Mutable state lives behind a single `lockObj`
/// matching the Bench primitives' lock posture. `emit` is safe to
/// call from any thread; ordering within a thread is preserved.
[<RequireQualifiedAccess>]
module LogSink =

    // -----------------------------------------------------------------
    // §3 envelope — closed-DU type surface
    // -----------------------------------------------------------------

    /// §4 severity levels — closed five-way enum. `Trace` and `Debug`
    /// are NOT emitted by default; CLI surface gates them on
    /// `--verbose` / `--debug` (Chapter C axis 7a / slice C.6).
    type Level =
        | Trace
        | Debug
        | Info
        | Warn
        | Error

    /// Chapter C slice C.6 — operator-facing verbosity gate. Three-way
    /// closed enum keyed by which low-level severities surface:
    ///   - `Quiet` (default) — only Info / Warn / Error surface.
    ///   - `Verbose` — additionally Debug surfaces.
    ///   - `Debug` — additionally Trace surfaces (function-entry /
    ///     exit + iteration-level bench probes).
    ///
    /// CLI mapping per §4: `--verbose` → `Verbose`; `--debug` →
    /// `Debug`; absence of both → `Quiet`. Operator may supply both
    /// flags safely; the union maximum wins (i.e., `--verbose --debug`
    /// is equivalent to `--debug`).
    ///
    /// `[<RequireQualifiedAccess>]` because `Debug` collides with the
    /// `Level.Debug` case — consumers disambiguate as
    /// `Verbosity.Debug` vs `Debug` (the Level case; unqualified
    /// resolution remains for Level uses).
    [<RequireQualifiedAccess>]
    type Verbosity =
        | Quiet
        | Verbose
        | Debug

    /// §6 event categories — closed eight-way enum, top-prefix of
    /// every `code` matches its category.
    type Category =
        | Config
        | Extract
        | Profile
        | Transform
        | Emit
        | Deploy
        | Canary
        | Summary

    /// §3 phase correlation tags. Distinct from `Level.Error`: an
    /// `error`-phase event names the closing of a `start`/`stepId`
    /// pair via failure; an `error`-level event names severity. They
    /// often co-occur but are independent dimensions.
    type Phase =
        | Start
        | Progress
        | End
        | ErrorPhase

    /// §3 transform source — V1 `ToggleExportValue.Source`
    /// generalization. Mandatory on `transform.*` codes; optional
    /// elsewhere.
    type Source =
        | Operator
        | Configuration
        | Default
        | Derived

    /// §10 terminal-event outcome — closed three-way enum. `Succeeded`
    /// (all stages green); `Failed` (an `error`-level event fired);
    /// `Aborted` (operator interrupt before completion).
    type Outcome =
        | Succeeded
        | Failed
        | Aborted

    /// §3 envelope — every emitted line on stderr conforms to this
    /// shape. Per the contract: `Payload` is `Map<string, objnull>` so
    /// caller composition stays uncontrolled; serialization at egress
    /// resolves each value via `JsonSerializer.Serialize` (string /
    /// numeric / bool / nested list / nested map).
    type Envelope =
        {
            RunId    : string
            Ts       : DateTime
            Level    : Level
            Category : Category
            Code     : string
            Phase    : Phase
            Source   : Source option
            SsKey    : SsKey option
            StepId   : string option
            Payload  : Map<string, objnull>
        }

    /// §11 per-group accumulator — built during stream emission.
    /// `Samples` carries the first three envelopes of the group in
    /// chronological order (V1 verbatim per `CommandConsole.cs:2069-
    /// 2078`).
    type GroupAccumulator =
        {
            Category : Category
            Code     : string
            SsKey    : SsKey option
            Count    : int
            FirstTs  : DateTime
            LastTs   : DateTime
            Samples  : Envelope list
        }

    /// §10 per-stage timing — populated by `recordStage` between
    /// `summary.stageCompleted` event emissions.
    type StageTiming =
        {
            Stage      : string
            DurationMs : int64
            Outcome    : Outcome
        }

    /// §10 artifact-table entry — populated by `recordArtifact`.
    /// `SizeBytes` for single-file artifacts; `FileCount` for
    /// directory-shaped artifacts (e.g., the SSDT bundle).
    type Artifact =
        {
            Kind      : string
            Path      : string
            SizeBytes : int64 option
            FileCount : int option
        }

    // -----------------------------------------------------------------
    // §3 ULID generation — runId construction
    // -----------------------------------------------------------------

    /// Crockford base32 alphabet (no I, L, O, U). Per §3: ULID-form
    /// runIds are lexically sortable by emit time — `ls bench/<tag>/`
    /// and `grep runId` give the same chronological ordering.
    [<Literal>]
    let private CrockfordAlphabet : string = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

    /// Generate a ULID per §3: 48-bit ms timestamp + 80-bit random,
    /// encoded as 26 chars of Crockford base32 (130 bits encoded with
    /// 2 leading zero bits, constraining the first char to 0-7).
    let generateRunId () : string =
        let ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        let randomBytes = Array.zeroCreate<byte> 10
        RandomNumberGenerator.Fill(randomBytes.AsSpan())
        let buf = Array.zeroCreate<byte> 16
        buf.[0] <- byte (ms >>> 40)
        buf.[1] <- byte (ms >>> 32)
        buf.[2] <- byte (ms >>> 24)
        buf.[3] <- byte (ms >>> 16)
        buf.[4] <- byte (ms >>> 8)
        buf.[5] <- byte ms
        Array.blit randomBytes 0 buf 6 10
        let chars = Array.zeroCreate<char> 26
        for k = 0 to 25 do
            let mutable v = 0
            for j = 0 to 4 do
                let bp = 5 * k + j - 2
                v <- v <<< 1
                if bp >= 0 && bp < 128 then
                    let b = int buf.[bp / 8]
                    let bit = (b >>> (7 - bp % 8)) &&& 1
                    v <- v ||| bit
            chars.[k] <- CrockfordAlphabet.[v]
        System.String chars

    // -----------------------------------------------------------------
    // Closed-DU → wire string projections (per §3-§6 lowercase form)
    // -----------------------------------------------------------------

    let levelToString (level: Level) : string =
        match level with
        | Trace -> "trace"
        | Debug -> "debug"
        | Info  -> "info"
        | Warn  -> "warn"
        | Error -> "error"

    let categoryToString (cat: Category) : string =
        match cat with
        | Config    -> "config"
        | Extract   -> "extract"
        | Profile   -> "profile"
        | Transform -> "transform"
        | Emit      -> "emit"
        | Deploy    -> "deploy"
        | Canary    -> "canary"
        | Summary   -> "summary"

    let phaseToString (phase: Phase) : string =
        match phase with
        | Start      -> "start"
        | Progress   -> "progress"
        | End        -> "end"
        | ErrorPhase -> "error"

    let sourceToString (src: Source) : string =
        match src with
        | Operator      -> "operator"
        | Configuration -> "configuration"
        | Default       -> "default"
        | Derived       -> "derived"

    let outcomeToString (oc: Outcome) : string =
        match oc with
        | Succeeded -> "succeeded"
        | Failed    -> "failed"
        | Aborted   -> "aborted"

    /// §3 RFC 3339 wire format for `ts`: UTC, millisecond precision,
    /// trailing `Z`. Boundary-only — Core never sees `DateTime` for
    /// emission; the envelope wall-clock is captured at the file/
    /// stream sink layer per the same precedent as
    /// `BenchSink.persistJson:54`.
    [<Literal>]
    let private Rfc3339UtcMs : string = "yyyy-MM-ddTHH:mm:ss.fffZ"

    let private formatTs (ts: DateTime) : string =
        let utc =
            if ts.Kind = DateTimeKind.Utc then ts
            else ts.ToUniversalTime()
        utc.ToString(Rfc3339UtcMs, System.Globalization.CultureInfo.InvariantCulture)

    // -----------------------------------------------------------------
    // §3 envelope construction — boundary-only
    // -----------------------------------------------------------------

    /// Map every Level to its canonical Phase default — `Error` →
    /// `ErrorPhase`, everything else → `Progress`. Callers override
    /// via `envelopeWith` when phase ≠ default (e.g., `Start` for
    /// step-correlation begin events).
    let private defaultPhaseFor (level: Level) : Phase =
        match level with
        | Error -> ErrorPhase
        | _     -> Progress

    /// `Trace`/`Debug` levels MUST be hidden by default per §4. Slice
    /// 6.5 ships the suppression check at the egress boundary; CLI
    /// `--verbose`/`--debug` (slice 7 + Chapter C slice C.6) flips
    /// the gate.
    let private isHiddenByDefault (level: Level) : bool =
        match level with
        | Trace | Debug -> true
        | _             -> false

    /// Chapter C slice C.6 — true iff the given `Level` is surfaced
    /// under the named `Verbosity`. Quiet hides Trace + Debug;
    /// Verbose surfaces Debug but still hides Trace; Verbosity.Debug
    /// surfaces both Trace + Debug. Info / Warn / Error always
    /// surface regardless of verbosity.
    let private isLevelVisible (verbosity: Verbosity) (level: Level) : bool =
        match level, verbosity with
        | (Info | Warn | Error), _             -> true
        | Debug, Verbosity.Quiet               -> false
        | Debug, _                             -> true
        | Trace, Verbosity.Debug               -> true
        | Trace, _                             -> false

    // -----------------------------------------------------------------
    // §11 / §10 — RunAccumulator (per-process state)
    // -----------------------------------------------------------------

    let private lockObj : obj = obj ()

    [<NoEquality; NoComparison>]
    type private RunState =
        {
            mutable RunId               : string
            mutable StartedAt           : DateTime
            mutable Verbosity           : Verbosity
            mutable MutedCategories     : Set<Category>
            Envelopes                   : ResizeArray<Envelope>
            Groups                      : Dictionary<Category * string * SsKey option, GroupAccumulator>
            EventCounts                 : Dictionary<Level, int>
            Stages                      : ResizeArray<StageTiming>
            Artifacts                   : ResizeArray<Artifact>
            mutable SuggestedConfigEdits: int
            // §10 transformSummary counters — incremented BEFORE the
            // verbosity filter so the rollup reflects the run, not the
            // display (`transform.registered` is Debug, suppressed under
            // Quiet; the summary must still count it).
            mutable TransformRegistered : int
            mutable TransformApplied    : int
            mutable TransformDeclined   : int
        }

    let private freshState () : RunState =
        {
            RunId                = generateRunId ()
            StartedAt            = DateTime.UtcNow  // LINT-ALLOW: wall-clock event timestamp at the logging egress boundary — operator-facing log events are boundary I/O, not deterministic artifacts (LogSink is the logging-sink sibling of Bench.fs's time boundary)
            Verbosity            = Verbosity.Quiet
            MutedCategories      = Set.empty
            Envelopes            = ResizeArray()
            Groups               = Dictionary()
            EventCounts          = Dictionary()
            Stages               = ResizeArray()
            Artifacts            = ResizeArray()
            SuggestedConfigEdits = 0
            TransformRegistered  = 0
            TransformApplied     = 0
            TransformDeclined    = 0
        }

    let private state : RunState ref = ref (freshState ())

    /// Default writer is stderr — channel-1 per §5. Tests inject a
    /// `StringWriter` via `setWriter` to capture envelopes.
    let private writer : TextWriter ref = ref Console.Error

    // -----------------------------------------------------------------
    // Configuration / lifecycle
    // -----------------------------------------------------------------

    /// Start a new run with a freshly-generated runId. Resets
    /// accumulator state. Returns the runId for caller propagation.
    let beginRun () : string =
        lock lockObj (fun () ->
            state.Value <- freshState ()
            state.Value.RunId)

    /// Start a new run with a caller-supplied runId. Used by tests +
    /// future replay shells where the runId is known up front.
    let beginRunWith (runId: string) : unit =
        lock lockObj (fun () ->
            let s = freshState ()
            s.RunId <- runId
            state.Value <- s)

    /// Reset to a fresh run state. Equivalent to `beginRun` but
    /// discards the returned runId (the most common test-harness
    /// shape).
    let reset () : unit =
        beginRun () |> ignore

    /// Override the egress writer. Channel-1 default is `Console.Error`;
    /// tests pass a `StringWriter` to capture envelopes for
    /// inspection. Future channel-2 (Spectre TtyRenderer) installs a
    /// `TextWriter.Null` here to suppress NDJSON-to-stderr when
    /// channel 2 is active.
    let setWriter (w: TextWriter) : unit =
        lock lockObj (fun () -> writer.Value <- w)

    /// Run `action` with `w` installed as the writer; restore the
    /// prior writer on exit (even under exception). Used by tests +
    /// scoped capture utilities.
    let withWriter (w: TextWriter) (action: unit -> 'a) : 'a =
        let prior = lock lockObj (fun () -> writer.Value)
        setWriter w
        try
            action ()
        finally
            setWriter prior

    /// Enable Trace/Debug emission. Off by default per §4.
    ///
    /// **Back-compat shim** (Chapter C slice C.6): mapped to the
    /// richer `Verbosity` DU — `true` → `Verbosity.Debug` (preserves
    /// the prior all-on semantics); `false` → `Verbosity.Quiet`.
    /// New consumers should use `setVerbosity` directly.
    let setVerbose (v: bool) : unit =
        lock lockObj (fun () ->
            state.Value.Verbosity <- if v then Verbosity.Debug else Verbosity.Quiet)

    /// Chapter C slice C.6 — set the per-process verbosity gate.
    /// Replaces the binary `setVerbose` with a three-way enum;
    /// `setVerbose` lives on as a back-compat shim.
    let setVerbosity (v: Verbosity) : unit =
        lock lockObj (fun () -> state.Value.Verbosity <- v)

    /// Chapter C slice C.6 — mute the given categories from the
    /// emission stream. Muted envelopes are dropped at the egress
    /// boundary (do NOT contribute to the §11 rollup either).
    /// Empty set (default) → no per-category filtering.
    let setMutedCategories (cats: Set<Category>) : unit =
        lock lockObj (fun () -> state.Value.MutedCategories <- cats)

    /// Current runId; read-only accessor. Returns the empty string
    /// only when called before `beginRun` (which freshState always
    /// initializes).
    let runId () : string =
        lock lockObj (fun () -> state.Value.RunId)

    // -----------------------------------------------------------------
    // §3 envelope construction
    // -----------------------------------------------------------------

    /// Build an envelope with the current runId + a fresh `ts`. The
    /// caller supplies the closed-DU classification axes; mandatory
    /// fields default to safe values (`Phase = defaultPhaseFor level`;
    /// `Source = None`; `SsKey = None`; `StepId = None`). Overrides
    /// flow via record-update at the call site.
    let envelope
        (level: Level)
        (category: Category)
        (code: string)
        (payload: Map<string, objnull>)
        : Envelope =
        {
            RunId    = lock lockObj (fun () -> state.Value.RunId)
            Ts       = DateTime.UtcNow  // LINT-ALLOW: wall-clock event timestamp at the logging egress boundary — operator-facing log events are boundary I/O, not deterministic artifacts (LogSink is the logging-sink sibling of Bench.fs's time boundary)
            Level    = level
            Category = category
            Code     = code
            Phase    = defaultPhaseFor level
            Source   = None
            SsKey    = None
            StepId   = None
            Payload  = payload
        }

    // -----------------------------------------------------------------
    // §3 wire serialization — `System.Text.Json.Utf8JsonWriter`
    // -----------------------------------------------------------------

    /// Reused options instance — `JsonSerializerOptions` is expensive
    /// to construct so we hold the default null-handling shape once.
    let private payloadOptions : JsonSerializerOptions =
        let o = JsonSerializerOptions()
        o.WriteIndented <- false
        o

    /// Serialize one payload value into the writer. Strings, ints,
    /// bools, floats, decimals, options/nullables, lists, arrays,
    /// dicts, and records all flow through `JsonSerializer.Serialize`
    /// — the boundary is `obj` because the contract specifies
    /// `Map<string, objnull>` payloads at §14.
    let private writePayloadValue (jw: Utf8JsonWriter) (v: objnull) : unit =
        match v with
        | null -> jw.WriteNullValue()
        | :? string as s -> jw.WriteStringValue s
        | :? bool as b -> jw.WriteBooleanValue b
        | :? int as i -> jw.WriteNumberValue i
        | :? int64 as l -> jw.WriteNumberValue l
        | :? float as f -> jw.WriteNumberValue f
        | :? decimal as d -> jw.WriteNumberValue d
        | :? DateTime as dt ->
            jw.WriteStringValue(formatTs dt)
        | _ ->
            // Delegate to JsonSerializer for compound shapes (lists,
            // arrays, records, dicts, nested maps).
            JsonSerializer.Serialize(jw, v, payloadOptions)

    /// Render an SsKey for the envelope's `ssKey` field — uses the
    /// canonical `rootOriginal` form per `Identity.fs:135`. Matches
    /// the rendering existing emitters use across V2.
    let private renderSsKey (key: SsKey) : string = SsKey.rootOriginal key

    /// Serialize one envelope as NDJSON to the writer (single line, no
    /// trailing whitespace beyond the newline). Field order matches
    /// §3 for grep-friendliness.
    let serializeEnvelope (env: Envelope) : string =
        use ms = new MemoryStream()
        let jw = new Utf8JsonWriter(ms)
        jw.WriteStartObject()
        jw.WriteString("runId", env.RunId)
        jw.WriteString("ts", formatTs env.Ts)
        jw.WriteString("level", levelToString env.Level)
        jw.WriteString("category", categoryToString env.Category)
        jw.WriteString("code", env.Code)
        jw.WriteString("phase", phaseToString env.Phase)
        match env.Source with
        | Some s -> jw.WriteString("source", sourceToString s)
        | None -> ()
        match env.SsKey with
        | Some k -> jw.WriteString("ssKey", renderSsKey k)
        | None -> ()
        match env.StepId with
        | Some id -> jw.WriteString("stepId", id)
        | None -> ()
        jw.WritePropertyName "payload"
        jw.WriteStartObject()
        for KeyValue(k, v) in env.Payload do
            jw.WritePropertyName k
            writePayloadValue jw v
        jw.WriteEndObject()
        jw.WriteEndObject()
        jw.Flush()
        Encoding.UTF8.GetString(ms.ToArray())

    // -----------------------------------------------------------------
    // §11 — rollup accumulator update
    // -----------------------------------------------------------------

    let private updateAccumulator (env: Envelope) : unit =
        let s = state.Value
        s.Envelopes.Add env
        match s.EventCounts.TryGetValue env.Level with
        | true, n -> s.EventCounts.[env.Level] <- n + 1
        | false, _ -> s.EventCounts.[env.Level] <- 1
        if env.Payload.ContainsKey "suggestedConfig" then
            s.SuggestedConfigEdits <- s.SuggestedConfigEdits + 1
        let key = (env.Category, env.Code, env.SsKey)
        match s.Groups.TryGetValue key with
        | true, acc ->
            let samples' =
                if List.length acc.Samples < 3 then acc.Samples @ [ env ]
                else acc.Samples
            s.Groups.[key] <- {
                acc with
                    Count   = acc.Count + 1
                    LastTs  = env.Ts
                    Samples = samples'
            }
        | false, _ ->
            s.Groups.[key] <- {
                Category = env.Category
                Code     = env.Code
                SsKey    = env.SsKey
                Count    = 1
                FirstTs  = env.Ts
                LastTs   = env.Ts
                Samples  = [ env ]
            }

    // -----------------------------------------------------------------
    // §3 + §5 — emit
    // -----------------------------------------------------------------

    /// Emit one envelope to the configured writer + update the run
    /// accumulator. Per §5 the writer is channel-1 stderr by default.
    /// Per §4 Trace/Debug events are suppressed under
    /// `Verbosity.Quiet` (the default); `--verbose` raises to
    /// `Verbosity.Verbose` (surfaces Debug); `--debug` raises to
    /// `Verbosity.Debug` (additionally surfaces Trace). Per Chapter
    /// C slice C.6, envelopes whose Category appears in
    /// `MutedCategories` are dropped at this boundary.
    let emit (env: Envelope) : unit =
        lock lockObj (fun () ->
            let s = state.Value
            // §10 transformSummary — count before the verbosity/mute filter so
            // the rollup reflects what ran, not what was displayed.
            (match env.Code with
             | "transform.registered" -> s.TransformRegistered <- s.TransformRegistered + 1
             | "transform.applied"    -> s.TransformApplied    <- s.TransformApplied + 1
             | "transform.declined"   -> s.TransformDeclined   <- s.TransformDeclined + 1
             | _ -> ())
            if not (isLevelVisible s.Verbosity env.Level) then
                ()
            elif Set.contains env.Category s.MutedCategories then
                ()
            else
                updateAccumulator env
                writer.Value.WriteLine(serializeEnvelope env))

    // -----------------------------------------------------------------
    // §10 — stage timings + artifact registration
    // -----------------------------------------------------------------

    /// Append a stage timing entry. Slice 7 wires
    /// `Bench.scope "stage.<name>"` enclosure + `recordStage` at exit.
    let recordStage (stage: string) (durationMs: int64) (outcome: Outcome) : unit =
        lock lockObj (fun () ->
            state.Value.Stages.Add {
                Stage      = stage
                DurationMs = durationMs
                Outcome    = outcome
            })

    let recordArtifact (artifact: Artifact) : unit =
        lock lockObj (fun () ->
            state.Value.Artifacts.Add artifact)

    /// Record a stage timing AND emit its `summary.stageCompleted`
    /// envelope (§7.8 — info / `end` phase, `stepId = stage`). The
    /// shared primitive for the orchestration's per-stage instrumentation
    /// (extract / profile / emit / deploy / canary) so the §10
    /// `runComplete` stage table and the live `summary.stageCompleted`
    /// stream stay in lock-step from one call site.
    let recordStageEvent (stage: string) (durationMs: int64) (outcome: Outcome) : unit =
        recordStage stage durationMs outcome
        let payload : Map<string, objnull> =
            Map.ofList [
                "stage",      box stage
                "durationMs", box durationMs
                "outcome",    box (outcomeToString outcome)
            ]
        emit
            { envelope Info Summary "summary.stageCompleted" payload with
                Phase  = End
                StepId = Some stage }

    // -----------------------------------------------------------------
    // §11 — rollup snapshot (read-only; built ONCE during emission)
    // -----------------------------------------------------------------

    /// Snapshot the in-memory event list — copy semantics so concurrent
    /// `emit` calls after snapshot don't mutate the returned list.
    /// Primary consumer is tests; secondary is the runSummary builder.
    let snapshot () : Envelope list =
        lock lockObj (fun () ->
            state.Value.Envelopes |> List.ofSeq)

    /// Snapshot the accumulator groups, sorted descending by `Count`
    /// then ascending by `FirstTs` (the §11 output ordering). Pure
    /// read — does not rebuild the dictionary.
    let aggregates () : GroupAccumulator list =
        lock lockObj (fun () ->
            state.Value.Groups.Values
            |> Seq.toList
            |> List.sortWith (fun a b ->
                let c = compare b.Count a.Count
                if c <> 0 then c
                else compare a.FirstTs b.FirstTs))

    /// Snapshot per-level event counts.
    let eventCounts () : Map<Level, int> =
        lock lockObj (fun () ->
            state.Value.EventCounts
            |> Seq.map (fun kv -> kv.Key, kv.Value)
            |> Map.ofSeq)

    let stages () : StageTiming list =
        lock lockObj (fun () -> state.Value.Stages |> List.ofSeq)

    let artifacts () : Artifact list =
        lock lockObj (fun () -> state.Value.Artifacts |> List.ofSeq)

    let suggestedConfigEdits () : int =
        lock lockObj (fun () -> state.Value.SuggestedConfigEdits)

    // -----------------------------------------------------------------
    // §11 — bench-stat surfacing under category=summary, code=bench.label
    // -----------------------------------------------------------------

    /// Bridge `Bench.Stats` records into synthesized aggregate
    /// envelopes per §11 "Bench.Stats integration": one entry per
    /// label under `category=summary, code=bench.label`. Used by
    /// `runComplete` when assembling the aggregates array.
    let private benchAggregates (runId: string) (now: DateTime) (stats: Bench.Stats list)
        : GroupAccumulator list =
        stats
        |> List.map (fun s ->
            let payload : Map<string, objnull> =
                Map.ofList [
                    "label",   box s.Label
                    "count",   box s.Count
                    "totalMs", box s.TotalMs
                    "minMs",   box s.MinMs
                    "maxMs",   box s.MaxMs
                    "meanMs",  box s.MeanMs
                    "p50Ms",   box s.P50Ms
                    "p95Ms",   box s.P95Ms
                    "p99Ms",   box s.P99Ms
                ]
            let env = {
                RunId    = runId
                Ts       = now
                Level    = Info
                Category = Summary
                Code     = "bench.label"
                Phase    = End
                Source   = None
                SsKey    = None
                StepId   = None
                Payload  = payload
            }
            {
                Category = Summary
                Code     = "bench.label"
                SsKey    = None
                Count    = s.Count
                FirstTs  = now
                LastTs   = now
                Samples  = [ env ]
            })

    // -----------------------------------------------------------------
    // §10 — runComplete: terminal `summary.runComplete` envelope
    // -----------------------------------------------------------------

    /// Render the aggregator's groups as JSON-friendly payload values.
    /// Each entry mirrors the §11 shape: category / code / ssKey /
    /// count / firstTs / lastTs / samples. Samples carry their full
    /// envelope payload + classification (level / phase / source /
    /// stepId) so post-hoc consumers can replay context.
    let private renderAggregateEntry (acc: GroupAccumulator) : Map<string, objnull> =
        let sampleMaps =
            acc.Samples
            |> List.map (fun env ->
                let baseFields : (string * objnull) list = [
                    "ts",      box (formatTs env.Ts)
                    "level",   box (levelToString env.Level)
                    "phase",   box (phaseToString env.Phase)
                    "payload", box env.Payload
                ]
                let withSource =
                    match env.Source with
                    | Some s -> baseFields @ [ "source", box (sourceToString s) ]
                    | None -> baseFields
                let withSsKey =
                    match env.SsKey with
                    | Some k -> withSource @ [ "ssKey", box (renderSsKey k) ]
                    | None -> withSource
                let final =
                    match env.StepId with
                    | Some id -> withSsKey @ [ "stepId", box id ]
                    | None -> withSsKey
                Map.ofList final)
        let ssKeyVal : objnull =
            match acc.SsKey with
            | Some k -> box (renderSsKey k)
            | None -> null
        Map.ofList [
            "category", box (categoryToString acc.Category)
            "code",     box acc.Code
            "ssKey",    ssKeyVal
            "count",    box acc.Count
            "firstTs",  box (formatTs acc.FirstTs)
            "lastTs",   box (formatTs acc.LastTs)
            "samples",  box sampleMaps
        ]

    let private renderStage (st: StageTiming) : Map<string, objnull> =
        Map.ofList [
            "stage",      box st.Stage
            "durationMs", box st.DurationMs
            "outcome",    box (outcomeToString st.Outcome)
        ]

    let private renderArtifact (a: Artifact) : Map<string, objnull> =
        let baseFields : (string * objnull) list = [
            "kind", box a.Kind
            "path", box a.Path
        ]
        let withBytes =
            match a.SizeBytes with
            | Some n -> baseFields @ [ "bytes", box n ]
            | None -> baseFields
        let final =
            match a.FileCount with
            | Some n -> withBytes @ [ "files", box n ]
            | None -> withBytes
        Map.ofList final

    let private eventCountsMap (counts: Map<Level, int>) : Map<string, objnull> =
        // Emit a stable per-level table so downstream parsers can index
        // by canonical name. Levels with zero events appear as 0 (per
        // §10 example which carries `"debug": 0`).
        [ Trace; Debug; Info; Warn; Error ]
        |> List.map (fun lv ->
            let n =
                match Map.tryFind lv counts with
                | Some v -> v
                | None -> 0
            levelToString lv, box n)
        |> Map.ofList

    /// §10 `rationaleHistogram` — group every `transform.declined` event by
    /// its payload `rationale` (the §8.1 closed enum), carrying the count +
    /// the first three SsKey samples per rationale. The operator scans this
    /// to know which knob turns which non-tightened axis. Empty map when no
    /// declines fired. Derived from the accumulated Info-level envelopes.
    let private buildRationaleHistogram (envelopes: ResizeArray<Envelope>) : Map<string, objnull> =
        envelopes
        |> Seq.filter (fun e -> e.Code = "transform.declined")
        |> Seq.choose (fun e ->
            match Map.tryFind "rationale" e.Payload with
            | Some r when not (isNull r) -> Some (string r, e)
            | _ -> None)
        |> Seq.groupBy fst
        |> Seq.map (fun (rationale, group) ->
            let items = group |> Seq.map snd |> Seq.toList
            let samples =
                items
                |> List.truncate 3
                |> List.choose (fun e -> e.SsKey |> Option.map renderSsKey)
            let entry : Map<string, objnull> =
                Map.ofList [ "count", box (List.length items); "samples", box samples ]
            rationale, (box entry : objnull))
        |> Map.ofSeq

    /// §12 `suggestedConfigDigest` (Tier-2 reporting) — the actionable digest
    /// surfaced in the verdict. Every event carrying a `payload.suggestedConfig`
    /// (`{ path, value, note }`) is merged by `path` (dedup), collecting the
    /// occurrence count + up to five sample SsKeys per path. This is the §12
    /// "single merged config patch" computed at the run boundary, so the
    /// operator reads the to-do list in the terminal rollup without a separate
    /// pass. Empty when no suggestions fired.
    let private buildSuggestedConfigDigest (envelopes: ResizeArray<Envelope>) : Map<string, objnull> =
        envelopes
        |> Seq.choose (fun e ->
            match Map.tryFind "suggestedConfig" e.Payload with
            | Some v ->
                match v with
                | :? Map<string, objnull> as cfg ->
                    match Map.tryFind "path" cfg with
                    | Some p when not (isNull p) -> Some (string p, cfg, e)
                    | _ -> None
                | _ -> None
            | None -> None)
        |> Seq.groupBy (fun (path, _, _) -> path)
        |> Seq.map (fun (path, group) ->
            let items = group |> Seq.toList
            let (_, firstCfg, _) = List.head items
            let pick k : objnull = match Map.tryFind k firstCfg with | Some x -> x | None -> null
            let ssKeys =
                items
                |> List.truncate 5
                |> List.choose (fun (_, _, e) -> e.SsKey |> Option.map renderSsKey)
            let entry : Map<string, objnull> =
                Map.ofList [
                    "value",  pick "value"
                    "note",   pick "note"
                    "count",  box (List.length items)
                    "ssKeys", box ssKeys
                ]
            path, (box entry : objnull))
        |> Map.ofSeq

    /// Emit the terminal `summary.runComplete` event per §10. Pulls
    /// the current accumulator + an optional `Bench.Stats` snapshot
    /// (caller-supplied so the CLI controls when the bench surface
    /// is captured). Returns the emitted envelope for caller
    /// inspection (testing + correlation).
    ///
    /// **Mandatory** terminal event — emits even on `Outcome.Failed`
    /// or `Outcome.Aborted` per §10 ("Even on failure, runSummary
    /// emits.").
    let runComplete
        (outcome: Outcome)
        (command: string)
        (benchStats: Bench.Stats list)
        : Envelope =
        lock lockObj (fun () ->
            let s = state.Value
            let now = DateTime.UtcNow  // LINT-ALLOW: wall-clock event timestamp at the logging egress boundary — operator-facing log events are boundary I/O, not deterministic artifacts (LogSink is the logging-sink sibling of Bench.fs's time boundary)
            let durationMs =
                let span = now - s.StartedAt
                int64 span.TotalMilliseconds
            let stagesPayload =
                s.Stages
                |> Seq.map renderStage
                |> Seq.toList
            let artifactsPayload =
                s.Artifacts
                |> Seq.map renderArtifact
                |> Seq.toList
            let benchAcc = benchAggregates s.RunId now benchStats
            // Aggregate ordering per §11: descending Count, ascending FirstTs.
            let aggregatePayload =
                Seq.append (s.Groups.Values) benchAcc
                |> Seq.sortWith (fun a b ->
                    let c = compare b.Count a.Count
                    if c <> 0 then c
                    else compare a.FirstTs b.FirstTs)
                |> Seq.map renderAggregateEntry
                |> Seq.toList
            let transformSummary : Map<string, objnull> =
                Map.ofList [
                    "registered", box s.TransformRegistered
                    "applied",    box s.TransformApplied
                    "declined",   box s.TransformDeclined
                ]
            let rationaleHistogram = buildRationaleHistogram s.Envelopes
            let payload : Map<string, objnull> =
                Map.ofList [
                    "outcome",              box (outcomeToString outcome)
                    "command",              box command
                    "durationMs",           box durationMs
                    "stages",               box stagesPayload
                    "eventCounts",          box (eventCountsMap (
                                                s.EventCounts
                                                |> Seq.map (fun kv -> kv.Key, kv.Value)
                                                |> Map.ofSeq))
                    "transformSummary",     box transformSummary
                    "rationaleHistogram",   box rationaleHistogram
                    "suggestedConfigEdits", box s.SuggestedConfigEdits
                    "suggestedConfigDigest",box (buildSuggestedConfigDigest s.Envelopes)
                    "artifacts",            box artifactsPayload
                    "aggregates",           box aggregatePayload
                ]
            let env = {
                RunId    = s.RunId
                Ts       = now
                Level    = Info
                Category = Summary
                Code     = "summary.runComplete"
                Phase    = End
                Source   = None
                SsKey    = None
                StepId   = None
                Payload  = payload
            }
            // Update the accumulator + write — same path as `emit` but
            // we already hold the lock, so inline the work.
            s.Envelopes.Add env
            match s.EventCounts.TryGetValue Info with
            | true, n -> s.EventCounts.[Info] <- n + 1
            | false, _ -> s.EventCounts.[Info] <- 1
            writer.Value.WriteLine(serializeEnvelope env)
            env)
