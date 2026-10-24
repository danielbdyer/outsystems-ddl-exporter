namespace Projection.Pipeline

// LINT-ALLOW-FILE: refusal / skip-reason strings at the spine's accounting
//   boundary compose via sprintf (the Catalog.create validation-message
//   precedent); the structural surface is the typed StageName / RunSpine /
//   StagedOutcome algebra.

open System.Diagnostics
open System.Threading.Tasks
open Projection.Core

/// R2 — the stage spine (`CONSTELLATION.md` §9.3; CONSTELLATION_BACKLOG
/// stage 2, cards S1/S2). Stage identity today is a string-prefix convention
/// over event codes (`<stage>.started` / `summary.stageCompleted{stage}`,
/// parsed by the Watch board) plus per-face display lists. The spine types
/// promote that convention to the type plane: a `StageName` is constructed
/// once, validly, or not at all; a `RunSpine` is the declared stage arc of
/// one run face — what the Watch pre-seeds from, and what the `staged { }`
/// CE holds the run accountable to (`declared ⇔ executed∪aborted`).
///
/// The smart ctor is the contract (the house derive-macro): `private` case
/// + `[<RequireQualifiedAccess>]` companion makes an invalid stage name
/// unrepresentable downstream.
type StageName = private StageName of string

[<RequireQualifiedAccess>]
module StageName =

    /// Non-blank, dot-free. Stage names key the wire codes
    /// (`<stage>.started`; the `stage` payload of `summary.stageCompleted`
    /// / `summary.stageProgress`) where `.` is the code-namespace
    /// separator — a dotted stage name would collide with the prefix
    /// convention the Watch board parses (`Watch.apply`).
    let create (name: string) : Result<StageName> =
        let blankErrors =
            Validation.nonBlank "stage.name.empty" "Stage name must be provided." name
        let dottedErrors =
            if not (System.String.IsNullOrWhiteSpace name) && name.Contains "." then
                [ ValidationError.create
                    "stage.name.dotted"
                    "Stage name must not contain '.' — the envelope-code namespace separator." ]
            else []
        match blankErrors @ dottedErrors with
        | [] -> Result.success (StageName name)
        | es -> Result.failure es

    /// The wire key — the `<stage>` of `<stage>.started` and the `stage`
    /// payload value of the summary events.
    let value (StageName n) : string = n

/// The declared stage arc of one run face — distinct, non-empty, in
/// execution order, with an optional umbrella **root** (the spine's root
/// scope — `FullExportRun`'s "pipeline" stage, which wraps the whole run
/// and is never a sub-stage the operator watches). Nesting is one level,
/// by declaration: the root brackets the run; the declared stages are the
/// arc inside it. The Watch board pre-seeds `Pending` lines from the
/// declared arc; the `staged { }` CE asserts `declared ⇔ executed∪aborted`
/// at run end (an open stage at run end becomes a named `Aborted`, never a
/// board hang).
type RunSpine = private { Root : StageName option; Declared : StageName list }

[<RequireQualifiedAccess>]
module RunSpine =

    let private stageErrors (stages: StageName list) : ValidationError list =
        let emptyErrors =
            Validation.nonEmpty
                "spine.stages.empty"
                "A run spine must declare at least one stage."
                stages
        let duplicateErrors =
            stages
            |> Validation.duplicateKeyErrors
                "spine.stages.duplicateKey"
                (sprintf "Run spine declares stage '%s' more than once; declared stages are distinct.")
                StageName.value
        emptyErrors @ duplicateErrors

    /// A rootless spine — the declared arc alone (migrate, deploy, …).
    let create (stages: StageName list) : Result<RunSpine> =
        match stageErrors stages with
        | [] -> Result.success { Root = None; Declared = stages }
        | es -> Result.failure es

    /// A spine with an umbrella root scope (full-export's "pipeline"). The
    /// root brackets the whole run — it is not a declared sub-stage, so it
    /// must not also appear in the arc.
    let createWithRoot (root: StageName) (stages: StageName list) : Result<RunSpine> =
        let rootErrors =
            if stages |> List.exists (fun s -> s = root) then
                [ ValidationError.create
                    "spine.root.declared"
                    "The umbrella root must not also be a declared sub-stage; nesting is one level, by declaration." ]
            else []
        match stageErrors stages @ rootErrors with
        | [] -> Result.success { Root = Some root; Declared = stages }
        | es -> Result.failure es

    /// The declared stages, in execution order (the root is not among them).
    let declared (spine: RunSpine) : StageName list = spine.Declared

    /// The declared arc as wire keys — what the Watch board pre-seeds from
    /// (the per-face string lists retire onto this projection).
    let keys (spine: RunSpine) : string list =
        spine.Declared |> List.map StageName.value

    /// The umbrella root scope, when declared.
    let root (spine: RunSpine) : StageName option = spine.Root

    /// The root's wire key — what the board elides (the umbrella is not a
    /// line the operator watches).
    let rootKey (spine: RunSpine) : string option =
        spine.Root |> Option.map StageName.value

/// The spine-level outcome of one declared stage. RI-2's correction is the
/// third arm: **aborted-at-stage is a real outcome** — a stage opened and
/// never closed because the run died inside it (e.g. `MigrationRun.execute`
/// opens "emit" and errors out before the close). The law admits it by
/// name rather than letting the board hang on an Active line. `Skipped`
/// is the declared-but-legitimately-not-run arm (a named reason — explicit
/// via `Staged.skip`, or downstream of a stop/abort) — distinct from
/// `Aborted`, which is always a failure of the run to reach the stage's
/// close. `Completed` is bracket-plane: the stage ran to its close — the
/// wire outcome (`succeeded` / `failed`) rides the envelope, not this arm.
type StagedOutcome =
    | Completed of durationMs: int64
    | Aborted of refusal: string
    | Skipped of reason: string

// ---------------------------------------------------------------------------
// The `staged { }` CE (card S2) — the writer-fidelity graduation, applied to
// time. Inside the CE, a stage crossing is structural: the Bind brackets
// `Bench.scope "stage.<name>"`, the `<stage>.started` envelope, and the
// `summary.stageCompleted` close (which the live Watch board folds) — and the
// builder's `Run` closes the books: `declared ⇔ executed∪aborted`, every
// declared stage accounted for by name, a missed or extra stage a named
// refusal at run end, never a render glitch.
// ---------------------------------------------------------------------------

/// How the staged run ended, on the value plane. `RunStopped` is a stage
/// body returning `Error` (the stage CLOSED — wire outcome `failed` — and
/// the downstream arc was skipped); `RunAborted` is an exception or a spine
/// refusal (undeclared / re-entered / unvisited stage), always named.
type StagedDisposition<'a, 'e> =
    | RunCompleted of value: 'a
    | RunStopped of error: 'e
    | RunAborted of refusal: string * cause: exn option

/// The closed books of one staged run: every declared stage's outcome, in
/// declared order, total by construction — plus the root bracket's outcome
/// when the spine declares an umbrella.
type StagedVerdict<'a, 'e> =
    { Root        : (StageName * StagedOutcome) option
      Outcomes    : (StageName * StagedOutcome) list
      Disposition : StagedDisposition<'a, 'e> }

[<RequireQualifiedAccess>]
module StagedVerdict =

    /// Project a closed verdict onto the value plane: `RunCompleted → Ok`,
    /// `RunStopped → Error`, and the two `RunAborted` cases preserve the engine's
    /// crash semantics — a captured exception re-throws with its ORIGINAL stack via
    /// `ExceptionDispatchInfo` (the trailing `Unchecked.defaultof` is unreachable;
    /// `Throw()` never returns), a bare spine refusal becomes `failwith`. The
    /// orchestrator tails (Pipeline / MigrationRun / TransferRun) that each
    /// hand-rolled this exact four-arm projection now share it, so "every spine
    /// abort re-raises identically" is structural rather than by convention.
    let toResult (verdict: StagedVerdict<'a, 'e>) : Result<'a, 'e> =
        match verdict.Disposition with
        | RunCompleted value -> Ok value
        | RunStopped error -> Error error
        | RunAborted (_, Some ex) ->
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
            Unchecked.defaultof<_>
        | RunAborted (refusal, None) -> failwith refusal

/// The per-step flow inside the CE — internal; faces see `StagedVerdict`.
type internal StagedStep<'a, 'e> =
    | Flowing of value: 'a
    | StoppedBy of error: 'e
    | AbortedBy of refusal: string * cause: exn option

/// The run-scoped accounting ledger. Mutable by design and confined to one
/// `Run` invocation (the run is synchronous + sequential — the LogSink
/// boundary precedent): a continuation that throws must not lose the books
/// already written.
type internal SpineLedger() =
    let visited = ResizeArray<StageName * StagedOutcome>()  // LINT-ALLOW: run-scoped accumulator; read out as an immutable list at close
    member _.Visit(name: StageName, outcome: StagedOutcome) : unit =
        visited.Add((name, outcome))
    member _.HasVisited(name: StageName) : bool =
        visited |> Seq.exists (fun (n, _) -> n = name)
    member _.Visited : (StageName * StagedOutcome) list =
        List.ofSeq visited

type internal SpineContext =
    { Spine : RunSpine
      Ledger : SpineLedger }

/// One staged computation — a program over the spine context. Constructed
/// only by `Staged.stage` / `Staged.skip` / the builder, so every stage
/// crossing is bracketed by construction.
type Staged<'a, 'e> = internal Staged of (SpineContext -> Task<StagedStep<'a, 'e>>)

[<RequireQualifiedAccess>]
module Staged =

    let internal run (Staged f) = f

    let private benchLabel (key: string) : string = "stage." + key

    let private abortRefusal (key: string) (ex: exn) : string =
        sprintf "spine.stage.aborted: '%s' threw %s: %s" key (ex.GetType().Name) ex.Message

    /// A declared stage: the bracket opens (`<stage>.started`, the Bench
    /// scope), the body runs, the bracket closes (`summary.stageCompleted`
    /// + the §10 stage-table entry — wire outcome `succeeded` on `Ok`,
    /// `failed` on `Error`, `aborted` on an exception, so the board's line
    /// always closes). An undeclared or re-entered stage refuses by name
    /// without opening a bracket (no phantom wire events).
    let stage (name: StageName) (body: unit -> Task<Result<'a, 'e>>) : Staged<'a, 'e> =
        Staged (fun ctx ->
            task {
                let key = StageName.value name
                if not (RunSpine.declared ctx.Spine |> List.contains name) then
                    return AbortedBy (sprintf "spine.stage.undeclared: '%s' ran outside the declared spine" key, None)
                elif ctx.Ledger.HasVisited name then
                    return AbortedBy (sprintf "spine.stage.reentered: '%s' ran twice in one run" key, None)
                else
                    LogSink.recordStageStart key
                    let sw = Stopwatch.StartNew()
                    use _ = Bench.scope (benchLabel key)
                    try
                        let! result = body ()
                        sw.Stop()
                        match result with
                        | Ok value ->
                            LogSink.recordStageEvent key sw.ElapsedMilliseconds LogSink.Succeeded
                            ctx.Ledger.Visit(name, Completed sw.ElapsedMilliseconds)
                            return Flowing value
                        | Error e ->
                            LogSink.recordStageEvent key sw.ElapsedMilliseconds LogSink.Failed
                            ctx.Ledger.Visit(name, Completed sw.ElapsedMilliseconds)
                            return StoppedBy e
                    with ex ->
                        sw.Stop()
                        // The Aborted arm is first-class: the bracket CLOSES
                        // (outcome `aborted` on the wire — the board line goes
                        // Halted, never hangs) and the refusal is named.
                        LogSink.recordStageEvent key sw.ElapsedMilliseconds LogSink.Aborted
                        ctx.Ledger.Visit(name, StagedOutcome.Aborted (abortRefusal key ex))
                        return AbortedBy (abortRefusal key ex, Some ex)
            })

    /// A declared stage explicitly not run this invocation, with its named
    /// reason. Ledger-only: no wire events (the stage never started; the
    /// board line honestly stays Pending), but the books stay total.
    let skip (name: StageName) (reason: string) : Staged<unit, 'e> =
        Staged (fun ctx ->
            task {
                let key = StageName.value name
                if not (RunSpine.declared ctx.Spine |> List.contains name) then
                    return AbortedBy (sprintf "spine.stage.undeclared: '%s' skipped outside the declared spine" key, None)
                elif ctx.Ledger.HasVisited name then
                    return AbortedBy (sprintf "spine.stage.reentered: '%s' visited twice in one run" key, None)
                else
                    ctx.Ledger.Visit(name, StagedOutcome.Skipped reason)
                    return Flowing ()
            })

/// Internal close — the books, balanced. Pure over the ledger snapshot.
module internal StagedClose =

    /// Total outcomes in DECLARED order: every declared stage appears
    /// exactly once — visited stages with their recorded outcome, unvisited
    /// stages as `Skipped` with the run-shaped reason.
    let private totalOutcomes
        (spine: RunSpine)
        (visited: (StageName * StagedOutcome) list)
        (skipReason: string)
        : (StageName * StagedOutcome) list =
        let byName = visited |> List.map (fun (n, o) -> StageName.value n, o) |> Map.ofList
        RunSpine.declared spine
        |> List.map (fun n ->
            match Map.tryFind (StageName.value n) byName with
            | Some o -> n, o
            | None   -> n, Skipped skipReason)

    let close
        (spine: RunSpine)
        (visited: (StageName * StagedOutcome) list)
        (step: StagedStep<'a, 'e>)
        : StagedVerdict<'a, 'e> =
        let lastVisitedKey =
            visited |> List.tryLast |> Option.map (fst >> StageName.value)
        match step with
        | Flowing value ->
            let unvisited =
                let byName = visited |> List.map (fst >> StageName.value) |> Set.ofList
                RunSpine.declared spine
                |> List.filter (fun n -> not (Set.contains (StageName.value n) byName))
            if List.isEmpty unvisited then
                { Root        = None
                  Outcomes    = totalOutcomes spine visited "unreachable"
                  Disposition = RunCompleted value }
            else
                // The law's teeth: a declared stage neither run nor skipped
                // is a named refusal at run end, not a silent pass.
                let names = unvisited |> List.map StageName.value |> String.concat ", "
                { Root        = None
                  Outcomes    = totalOutcomes spine visited "unvisited at run end — the close refused"
                  Disposition = RunAborted (sprintf "spine.stages.unvisited: %s" names, None) }
        | StoppedBy error ->
            let reason =
                match lastVisitedKey with
                | Some k -> sprintf "run stopped at '%s'" k
                | None   -> "run stopped before any stage"
            { Root        = None
              Outcomes    = totalOutcomes spine visited reason
              Disposition = RunStopped error }
        | AbortedBy (refusal, cause) ->
            { Root        = None
              Outcomes    = totalOutcomes spine visited (sprintf "run aborted: %s" refusal)
              Disposition = RunAborted (refusal, cause) }

    /// Guard the program run so a continuation that throws (between stage
    /// brackets) still closes the books by name.
    let guarded (m: Staged<'a, 'e>) (ctx: SpineContext) : Task<StagedStep<'a, 'e>> =
        task {
            try
                return! Staged.run m ctx
            with ex ->
                return AbortedBy (sprintf "spine.run.threw: %s: %s" (ex.GetType().Name) ex.Message, Some ex)
        }

/// The `staged spine { … }` builder. Member set mirrors the house CE
/// precedent (`LineageDiagnosticsBuilder`): Bind sequences stages (a
/// stopped or aborted flow short-circuits the rest — downstream declared
/// stages close as `Skipped`, by name); `Run` brackets the optional
/// umbrella root and closes the books. The CE expression evaluates to a
/// hot `Task<StagedVerdict<_,_>>`, like `task { }` itself.
type StagedBuilder(spine: RunSpine) =

    member _.Bind(m: Staged<'a, 'e>, f: 'a -> Staged<'b, 'e>) : Staged<'b, 'e> =
        Staged (fun ctx ->
            task {
                let! step = Staged.run m ctx
                match step with
                | Flowing a          -> return! Staged.run (f a) ctx
                | StoppedBy e        -> return StoppedBy e
                | AbortedBy (r, c)   -> return AbortedBy (r, c)
            })

    member _.Return(x: 'a) : Staged<'a, 'e> =
        Staged (fun _ -> Task.FromResult (Flowing x))

    member _.ReturnFrom(m: Staged<'a, 'e>) : Staged<'a, 'e> = m

    member _.Delay(f: unit -> Staged<'a, 'e>) : Staged<'a, 'e> =
        Staged (fun ctx -> Staged.run (f ()) ctx)

    member _.Run(m: Staged<'a, 'e>) : Task<StagedVerdict<'a, 'e>> =
        task {
            let ctx = { Spine = spine; Ledger = SpineLedger() }
            match RunSpine.root spine with
            | None ->
                let! step = StagedClose.guarded m ctx
                return StagedClose.close spine ctx.Ledger.Visited step
            | Some rootName ->
                // The umbrella root scope — one level of nesting, by
                // declaration. The root's bracket encloses the whole arc;
                // its wire outcome mirrors the disposition.
                let rootKey = StageName.value rootName
                LogSink.recordStageStart rootKey
                let sw = Stopwatch.StartNew()
                use _ = Bench.scope ("stage." + rootKey)
                let! step = StagedClose.guarded m ctx
                sw.Stop()
                let verdict = StagedClose.close spine ctx.Ledger.Visited step
                let wireOutcome, rootOutcome =
                    match verdict.Disposition with
                    | RunCompleted _      -> LogSink.Succeeded, Completed sw.ElapsedMilliseconds
                    | RunStopped _        -> LogSink.Failed,    Completed sw.ElapsedMilliseconds
                    | RunAborted (r, _)   -> LogSink.Aborted,   StagedOutcome.Aborted r
                LogSink.recordStageEvent rootKey sw.ElapsedMilliseconds wireOutcome
                return { verdict with Root = Some (rootName, rootOutcome) }
        }

[<AutoOpen>]
module StagedBuilderEntry =
    /// The `staged spine { … }` CE entry point — inside it, unmetered work
    /// between stages is syntactically impossible.
    let staged (spine: RunSpine) : StagedBuilder = StagedBuilder spine

/// The canonical stage names — the declare-once site at the stage-name
/// grain. The spines compose from these, and the faces reference the same
/// values at their `Staged.stage` calls, so a face cannot drift from its
/// declaration by retyping a string.
[<RequireQualifiedAccess>]
module Stages =

    let private name (s: string) : StageName =
        StageName.create s |> Result.value

    /// The full-export umbrella root (never a watched sub-stage).
    let pipeline  : StageName = name "pipeline"
    let extract   : StageName = name "extract"
    let profile   : StageName = name "profile"
    let emit      : StageName = name "emit"
    /// The migrate engine's safety gates (the 6.A.13 CDC gate + the G9
    /// tightening gate) — real SQL I/O, declared and metered (card S4b).
    let preflight : StageName = name "preflight"
    let deploy    : StageName = name "deploy"
    let canary    : StageName = name "canary"
    let load      : StageName = name "load"
    /// The publish store leg (diff-vs-prior + the episode record) — a declared
    /// post-root stage (2026-07-02) so the live board covers the WHOLE publish
    /// run: before this, the board hit its done-frame while the store leg was
    /// still working.
    let store     : StageName = name "store"
    /// The publish-and-load seed leg — a distinct key from the transfer
    /// `load` (its Voice gerund speaks the idempotent-seed act, not the
    /// transfer's row movement).
    let seedLoad  : StageName = name "seed-load"

/// The declared spines — one definition site per run face's arc (the
/// per-face display string lists retire onto these; the Watch pre-seeds
/// derive via `RunSpine.keys`).
[<RequireQualifiedAccess>]
module Spines =

    /// `full-export`: the "pipeline" umbrella over extract → profile → emit.
    let pipeline : RunSpine =
        RunSpine.createWithRoot Stages.pipeline [ Stages.extract; Stages.profile; Stages.emit ]
        |> Result.value

    /// The publish arcs — `pipeline` plus the store leg (when a lifecycle
    /// store is supplied) and the seed-load leg (publish-and-load). Chosen at
    /// DISPATCH, never as an optional seeded stage: the board's done-frame
    /// waits for every seeded stage to close, so a seeded-but-skipped stage
    /// would hold it back forever (2026-07-02).
    let publishWith (store: bool) (load: bool) : RunSpine =
        RunSpine.createWithRoot Stages.pipeline
            ([ Stages.extract; Stages.profile; Stages.emit ]
             @ (if store then [ Stages.store ] else [])
             @ (if load then [ Stages.seedLoad ] else []))
        |> Result.value

    /// The in-place migrate leg: build → safety gates → apply → verify.
    let migrate : RunSpine =
        RunSpine.create [ Stages.emit; Stages.preflight; Stages.deploy; Stages.canary ] |> Result.value

    /// The cross-substrate migrate: the schema leg, then the data load.
    let migrateData : RunSpine =
        RunSpine.create
            [ Stages.emit; Stages.preflight; Stages.deploy; Stages.canary; Stages.load ]
        |> Result.value

    /// The standalone deploy verb's single-stage arc.
    let deploy : RunSpine =
        RunSpine.create [ Stages.deploy ] |> Result.value

    /// The wide-canary verb's single-stage arc.
    let canary : RunSpine =
        RunSpine.create [ Stages.canary ] |> Result.value

    /// The data-transfer verbs' single-stage arc (the load leg streams its
    /// own per-table progress inside the one stage).
    let transfer : RunSpine =
        RunSpine.create [ Stages.load ] |> Result.value
