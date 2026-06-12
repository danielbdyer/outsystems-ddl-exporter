[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.StagedTests

// Card S2 (CONSTELLATION_BACKLOG stage 2) — the `staged { }` CE and the R2
// law: `declared ⇔ executed∪aborted`. The registry pattern's fifth instance
// (after registered⇔executed, code⇔copy, expressible⇔reachable, and the
// codec): the spine declares the arc; the CE brackets every crossing
// (`<stage>.started` / `summary.stageCompleted` / `Bench.scope "stage.<name>"`);
// the Run closure balances the books — every declared stage accounted for by
// name, a missed or extra stage a named refusal at run end, the Aborted arm
// first-class (an open stage at run end closes `aborted`, never a board hang).
//
// Lives in the Global-MutableState collection: the CE writes the LogSink +
// Bench process state, like every emitting surface.

open System.Threading.Tasks
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Cli

let private stage (n: string) : StageName =
    StageName.create n |> Result.value

let private spine3 : RunSpine =
    RunSpine.create [ stage "extract"; stage "deploy"; stage "canary" ] |> Result.value

let private ok (v: 'a) : unit -> Task<Result<'a, string>> =
    fun () -> Task.FromResult (Ok v)

let private err (e: string) : unit -> Task<Result<'a, string>> =
    fun () -> Task.FromResult (Error e : Result<'a, string>)

/// Run a staged program with the LogSink captured: returns the verdict plus
/// the envelopes channel 1 would have written, in stream order (via the
/// subscriber — the exact feed the live Watch board consumes).
let private runCaptured (program: unit -> Task<StagedVerdict<'a, 'e>>) : StagedVerdict<'a, 'e> * LogSink.Envelope list =
    let captured = ResizeArray<LogSink.Envelope>()
    LogSink.reset ()
    LogSink.addSubscriber captured.Add
    try
        let verdict =
            LogSink.withWriter (new System.IO.StringWriter()) (fun () ->
                (program ()).GetAwaiter().GetResult())
        verdict, List.ofSeq captured
    finally
        LogSink.clearSubscribers ()

let private codesOf (envs: LogSink.Envelope list) : string list =
    envs
    |> List.filter (fun e -> e.Code.EndsWith ".started" || e.Code = "summary.stageCompleted")
    |> List.map (fun e ->
        if e.Code = "summary.stageCompleted" then
            sprintf "completed:%O" (Map.find "stage" e.Payload)
        else e.Code)

let private outcomePayloadOf (stageKey: string) (envs: LogSink.Envelope list) : string =
    envs
    |> List.pick (fun e ->
        if e.Code = "summary.stageCompleted"
           && string (Map.find "stage" e.Payload) = stageKey
        then Some (string (Map.find "outcome" e.Payload))
        else None)

// ---------------------------------------------------------------------------
// the law — declared ⇔ executed∪aborted
// ---------------------------------------------------------------------------

[<Fact>]
let ``R2: declared ⇔ executed∪aborted — every declared stage accounted, in declared order`` () =
    let verdict, envs =
        runCaptured (fun () ->
            staged spine3 {
                let! a = Staged.stage (stage "extract") (ok 1)
                let! b = Staged.stage (stage "deploy") (ok (a + 1))
                let! c = Staged.stage (stage "canary") (ok (b + 1))
                return c
            })
    match verdict.Disposition with
    | RunCompleted 3 -> ()
    | other -> Assert.Fail(sprintf "expected RunCompleted 3, got %A" other)
    // The books: total over the declaration, in declared order, all bracket-closed.
    Assert.Equal<string list>(
        [ "extract"; "deploy"; "canary" ],
        verdict.Outcomes |> List.map (fst >> StageName.value))
    Assert.True(verdict.Outcomes |> List.forall (fun (_, o) -> match o with Completed _ -> true | _ -> false))
    // The wire: started + completed per stage, in execution order, all succeeded.
    Assert.Equal<string list>(
        [ "extract.started"; "completed:extract"
          "deploy.started";  "completed:deploy"
          "canary.started";  "completed:canary" ],
        codesOf envs)
    Assert.Equal("succeeded", outcomePayloadOf "deploy" envs)

[<Fact>]
let ``R2: a stage body's Error closes the stage failed and skips the downstream arc by name`` () =
    let verdict, envs =
        runCaptured (fun () ->
            staged spine3 {
                let! _ = Staged.stage (stage "extract") (ok 1)
                let! _ = Staged.stage (stage "deploy") (err "no grant")
                let! c = Staged.stage (stage "canary") (ok 3)
                return c
            })
    match verdict.Disposition with
    | RunStopped "no grant" -> ()
    | other -> Assert.Fail(sprintf "expected RunStopped, got %A" other)
    // deploy CLOSED (bracket plane) with the failed wire outcome; canary never opened.
    Assert.Equal("failed", outcomePayloadOf "deploy" envs)
    Assert.False(envs |> List.exists (fun e -> e.Code = "canary.started"))
    match verdict.Outcomes with
    | [ (_, Completed _); (_, Completed _); (_, Skipped reason) ] ->
        Assert.Equal("run stopped at 'deploy'", reason)
    | other -> Assert.Fail(sprintf "expected Completed/Completed/Skipped, got %A" other)

[<Fact>]
let ``R2: the Aborted arm — an exception closes the open stage aborted, never a board hang`` () =
    let verdict, envs =
        runCaptured (fun () ->
            staged spine3 {
                let! _ = Staged.stage (stage "extract") (ok 1)
                let! _ =
                    Staged.stage (stage "deploy") (fun () ->
                        task {
                            failwith "connection lost"
                            return (Ok 2 : Result<int, string>)
                        })
                let! c = Staged.stage (stage "canary") (ok 3)
                return c
            })
    match verdict.Disposition with
    | RunAborted (refusal, Some _) -> Assert.Contains("spine.stage.aborted: 'deploy'", refusal)
    | other -> Assert.Fail(sprintf "expected RunAborted with its cause, got %A" other)
    // The bracket CLOSED on the wire — outcome `aborted` — so the live board's
    // line goes Halted instead of hanging Active forever.
    Assert.Equal("aborted", outcomePayloadOf "deploy" envs)
    match verdict.Outcomes with
    | [ (_, Completed _); (_, StagedOutcome.Aborted r); (_, Skipped _) ] ->
        Assert.Contains("connection lost", r)
    | other -> Assert.Fail(sprintf "expected Completed/Aborted/Skipped, got %A" other)
    // The display half of the law: folding the same envelope stream the live
    // board consumes yields a Halted line — closed, honest, no hang.
    let board =
        envs |> List.fold (fun b e -> Watch.applyEnvelope b e |> fst) (Watch.seededOf spine3)
    match board.Stages |> List.tryFind (fun s -> s.Key = "deploy") with
    | Some { State = Watch.Halted _ } -> ()
    | other -> Assert.Fail(sprintf "expected the deploy line Halted, got %A" other)
    Assert.False(Watch.isTerminal board)

[<Fact>]
let ``R2: an undeclared stage refuses by name without opening a bracket`` () =
    let verdict, envs =
        runCaptured (fun () ->
            staged spine3 {
                let! _ = Staged.stage (stage "extract") (ok 1)
                let! x = Staged.stage (stage "verify") (ok 2)
                return x
            })
    match verdict.Disposition with
    | RunAborted (refusal, None) -> Assert.Contains("spine.stage.undeclared: 'verify'", refusal)
    | other -> Assert.Fail(sprintf "expected the undeclared refusal, got %A" other)
    // No phantom wire events for the undeclared stage.
    Assert.False(envs |> List.exists (fun e -> e.Code = "verify.started"))

[<Fact>]
let ``R2: a re-entered stage refuses by name`` () =
    let verdict, _ =
        runCaptured (fun () ->
            staged spine3 {
                let! _ = Staged.stage (stage "extract") (ok 1)
                let! x = Staged.stage (stage "extract") (ok 2)
                return x
            })
    match verdict.Disposition with
    | RunAborted (refusal, None) -> Assert.Contains("spine.stage.reentered: 'extract'", refusal)
    | other -> Assert.Fail(sprintf "expected the re-entry refusal, got %A" other)

[<Fact>]
let ``R2: an unvisited declared stage at completion is a named refusal, not a silent pass`` () =
    let verdict, _ =
        runCaptured (fun () ->
            staged spine3 {
                let! a = Staged.stage (stage "extract") (ok 1)
                return a
            })
    match verdict.Disposition with
    | RunAborted (refusal, None) ->
        Assert.Contains("spine.stages.unvisited", refusal)
        Assert.Contains("deploy", refusal)
        Assert.Contains("canary", refusal)
    | other -> Assert.Fail(sprintf "expected the unvisited refusal, got %A" other)

[<Fact>]
let ``R2: an explicit skip keeps the books total with its named reason`` () =
    let verdict, envs =
        runCaptured (fun () ->
            staged spine3 {
                let! a = Staged.stage (stage "extract") (ok 1)
                do! Staged.skip (stage "deploy") "no sink configured this run"
                let! c = Staged.stage (stage "canary") (ok (a + 2))
                return c
            })
    match verdict.Disposition with
    | RunCompleted 3 -> ()
    | other -> Assert.Fail(sprintf "expected RunCompleted, got %A" other)
    match verdict.Outcomes with
    | [ (_, Completed _); (_, Skipped reason); (_, Completed _) ] ->
        Assert.Equal("no sink configured this run", reason)
    | other -> Assert.Fail(sprintf "expected the skip in the books, got %A" other)
    // Ledger-only: a skipped stage never starts on the wire.
    Assert.False(envs |> List.exists (fun e -> e.Code = "deploy.started"))

[<Fact>]
let ``R2: the umbrella root brackets the run — one level of nesting, by declaration`` () =
    let rootSpine =
        RunSpine.createWithRoot (stage "pipeline") [ stage "extract"; stage "emit" ]
        |> Result.value
    let verdict, envs =
        runCaptured (fun () ->
            staged rootSpine {
                let! a = Staged.stage (stage "extract") (ok 1)
                let! b = Staged.stage (stage "emit") (ok (a + 1))
                return b
            })
    // The root's bracket encloses the whole arc on the wire.
    Assert.Equal<string list>(
        [ "pipeline.started"
          "extract.started"; "completed:extract"
          "emit.started";    "completed:emit"
          "completed:pipeline" ],
        codesOf envs)
    // The root rides the verdict beside the declared books, never among them.
    match verdict.Root with
    | Some (r, Completed _) -> Assert.Equal("pipeline", StageName.value r)
    | other -> Assert.Fail(sprintf "expected the root bracket completed, got %A" other)
    Assert.Equal<string list>(
        [ "extract"; "emit" ],
        verdict.Outcomes |> List.map (fst >> StageName.value))
    // The board elides the root: seeded from the spine, no root line appears.
    let board =
        envs |> List.fold (fun b e -> Watch.applyEnvelope b e |> fst) (Watch.seededOf rootSpine)
    Assert.Equal<string list>([ "extract"; "emit" ], board.Stages |> List.map (fun s -> s.Key))
    Assert.True(Watch.isTerminal board)

[<Fact>]
let ``R2: the stage bracket meters — Bench carries one stage-labelled sample per crossing`` () =
    // Safe in the Global-MutableState collection: every Bench.reset caller in
    // the pure pool serializes here.
    Bench.reset ()
    let _ =
        runCaptured (fun () ->
            staged spine3 {
                let! a = Staged.stage (stage "extract") (ok 1)
                let! _ = Staged.stage (stage "deploy") (ok 2)
                let! c = Staged.stage (stage "canary") (ok (a + 2))
                return c
            })
    let labels = Bench.snapshot () |> List.map (fun s -> s.Label)
    Assert.Contains("stage.extract", labels)
    Assert.Contains("stage.deploy", labels)
    Assert.Contains("stage.canary", labels)

// ---------------------------------------------------------------------------
// additivity (card S5) — T14 on the time plane, honestly
// ---------------------------------------------------------------------------

[<Fact>]
let ``R2: wall(root) − Σ wall(stage) ≤ ε — T14 on the time plane (the spine's nesting is tight)`` () =
    // The CE's own nesting: the root bracket encloses exactly the declared
    // children plus the builder's plumbing (binds, ledger appends, envelope
    // emission — no I/O between brackets by construction). The sleeps make
    // the children's wall dominate; ε is the plumbing, bounded generously
    // for CI scheduling noise. The +2ms slack absorbs independent stopwatch
    // rounding. (The RUN-level account — config, artifacts, face gates —
    // is the FullExportCliTests S5 witness; its residue is enumerated
    // there.)
    let rootSpine =
        RunSpine.createWithRoot (stage "pipeline") [ stage "extract"; stage "emit" ]
        |> Result.value
    let sleepStage (ms: int) : unit -> Task<Result<unit, string>> =
        fun () ->
            task {
                do! Task.Delay ms
                return Ok ()
            }
    let verdict, _ =
        runCaptured (fun () ->
            staged rootSpine {
                do! Staged.stage (stage "extract") (sleepStage 60)
                do! Staged.stage (stage "emit") (sleepStage 60)
                return ()
            })
    let childSum =
        verdict.Outcomes
        |> List.sumBy (fun (_, o) -> match o with Completed ms -> ms | _ -> 0L)
    match verdict.Root with
    | Some (_, Completed rootMs) ->
        Assert.True(rootMs + 2L >= childSum, sprintf "the root (%dms) must cover its children (%dms)" rootMs childSum)
        Assert.True(rootMs - childSum <= 250L, sprintf "nesting ε=%dms exceeds the named 250ms bound" (rootMs - childSum))
    | other -> Assert.Fail(sprintf "expected the root bracket completed, got %A" other)

// ---------------------------------------------------------------------------
// the run-envelope bracket (card S4a) — ONE owner, both callers
// ---------------------------------------------------------------------------

[<Fact>]
let ``S4a: RunEnvelope.bracket — runStart is the first event and runComplete the terminal one, with the verb's payload`` () =
    let captured = ResizeArray<LogSink.Envelope>()
    LogSink.reset ()
    LogSink.addSubscriber captured.Add
    try
        let value =
            LogSink.withWriter (new System.IO.StringWriter()) (fun () ->
                RunEnvelope.bracket "projection test-verb"
                    ignore
                    (Map.ofList [ "configPath", box "cfg.json" ])
                    (fun () -> 41 + 1, LogSink.Succeeded))
        Assert.Equal(42, value)
        let envs = List.ofSeq captured
        let first = List.head envs
        Assert.Equal("config.runStart", first.Code)
        Assert.Equal(box "projection test-verb", Map.find "command" first.Payload)
        Assert.Equal(box "cfg.json", Map.find "configPath" first.Payload)
        Assert.Equal("summary.runComplete", (List.last envs).Code)
    finally
        LogSink.clearSubscribers ()

[<Fact>]
let ``S4a: RunEnvelope.bracket — a crashed body still closes its stream with the §10 terminal event`` () =
    let captured = ResizeArray<LogSink.Envelope>()
    LogSink.reset ()
    LogSink.addSubscriber captured.Add
    try
        let thrown =
            try
                LogSink.withWriter (new System.IO.StringWriter()) (fun () ->
                    RunEnvelope.bracket "projection test-verb" ignore Map.empty
                        (fun () -> failwith "boom" : int * LogSink.Outcome))
                |> ignore
                false
            with _ -> true
        Assert.True(thrown, "the body's exception must propagate after the terminal event")
        let last = List.last (List.ofSeq captured)
        Assert.Equal("summary.runComplete", last.Code)
        Assert.Equal(box "failed", Map.find "outcome" last.Payload)
    finally
        LogSink.clearSubscribers ()

// ---------------------------------------------------------------------------
// the declared spines — the retired string lists, pinned at their one
// definition site
// ---------------------------------------------------------------------------

[<Fact>]
let ``S2: the declared spines carry the per-face arcs the string lists carried`` () =
    Assert.Equal<string list>([ "extract"; "profile"; "emit" ], RunSpine.keys Spines.pipeline)
    Assert.Equal(Some "pipeline", RunSpine.rootKey Spines.pipeline)
    Assert.Equal<string list>([ "emit"; "preflight"; "deploy"; "canary" ], RunSpine.keys Spines.migrate)
    Assert.Equal<string list>(
        [ "emit"; "preflight"; "deploy"; "canary"; "load" ],
        RunSpine.keys Spines.migrateData)
    Assert.Equal<string list>([ "deploy" ], RunSpine.keys Spines.deploy)
    Assert.Equal<string list>([ "canary" ], RunSpine.keys Spines.canary)
    Assert.Equal<string list>([ "load" ], RunSpine.keys Spines.transfer)

// ---------------------------------------------------------------------------
// R1e — the law: live view ≡ projection of the stored Run. The board
// reconstructed from the SERIALIZED envelope trail (the exact strings
// `Run.capture` persists as `Run.Events`) equals the board the live
// subscriber built. The S2 spine makes the reconstruction total: every
// crossing is on the wire, failed and aborted arms included.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R1: live view ≡ projection of the stored Run — the reconstructed board equals the live board`` () =
    // A mixed run: extract succeeds, deploy fails (→ Halted on the board),
    // canary is run-stopped (ledger-only Skip — Pending on both boards).
    let verdict, envs =
        runCaptured (fun () ->
            staged spine3 {
                let! a = Staged.stage (stage "extract") (ok 1)
                let! b = Staged.stage (stage "deploy") (err "refused")
                let! c = Staged.stage (stage "canary") (ok (b + 1))
                return c
            })
    (match verdict.Disposition with
     | RunStopped _ -> ()
     | other -> Assert.Fail(sprintf "expected RunStopped, got %A" other))
    let live =
        envs |> List.fold (fun b e -> Watch.applyEnvelope b e |> fst) (Watch.seededOf spine3)
    // The stored form: the same trail as Run.capture's Events field.
    let stored = LogSink.serializedEnvelopes ()
    let reconstructed = Watch.boardOfStored (Watch.seededOf spine3) stored
    Assert.Equal<Watch.Board>(live, reconstructed)
    // And the projection is honest about the mixed verdict: deploy Halted,
    // canary still Pending (its skip is ledger-only, by design).
    (match reconstructed.Stages |> List.tryFind (fun s -> s.Key = "deploy") with
     | Some { State = Watch.Halted _ } -> ()
     | other -> Assert.Fail(sprintf "expected deploy Halted on the reconstructed board, got %A" other))
    (match reconstructed.Stages |> List.tryFind (fun s -> s.Key = "canary") with
     | Some { State = Watch.Pending } -> ()
     | other -> Assert.Fail(sprintf "expected canary Pending on the reconstructed board, got %A" other))
