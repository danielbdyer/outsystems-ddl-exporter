module Projection.Tests.WatchTests

open Xunit
open Projection.Cli

/// THE VOICE — the live run (Watch), slice 2. The board is a *rendering* of the
/// LogSink stage stream; these tests pin the pure core — the dwell floor, the
/// board transitions, and the voiced stage lines (under the twelve-rule banned
/// list) — without a TTY or a real sleep.

let private payload (pairs: (string * objnull) list) : Map<string, objnull> =
    Map.ofList pairs

// ---------------------------------------------------------------------------
// the dwell floor (the operator-named perceptual minimum)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Watch dwell: a sub-floor frame is held for the remainder`` () =
    // 50ms elapsed since the last frame, floor 120 → hold the remaining 70ms.
    Assert.Equal(70L, Watch.dwellMs 120L 0L 50L)

[<Fact>]
let ``Watch dwell: a frame already past the floor is never delayed`` () =
    Assert.Equal(0L, Watch.dwellMs 120L 0L 200L)

[<Fact>]
let ``Watch dwell: exactly at the floor yields no delay`` () =
    Assert.Equal(0L, Watch.dwellMs 120L 0L 120L)

[<Fact>]
let ``Watch dwell: the correction is never negative`` () =
    Assert.True(Watch.dwellMs 120L 100L 1000L >= 0L)

[<Fact>]
let ``Watch dwell: the floor is the bound on added latency per frame`` () =
    // The most a single frame can ever be delayed is the floor itself (when no
    // time has elapsed since the last frame) — the minimal correction.
    Assert.Equal(120L, Watch.dwellMs 120L 500L 500L)

// ---------------------------------------------------------------------------
// the board transitions (the stage stream → the board)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Watch board: a stage start opens an Active line`` () =
    let board, changed = Watch.apply Watch.empty "extract.started" Map.empty
    Assert.True changed
    match board.Stages with
    | [ { Key = "extract"; State = Watch.Active None } ] -> ()
    | other -> Assert.Fail(sprintf "expected one Active extract line, got %A" other)

[<Fact>]
let ``Watch board: a repeated start is not a renderable change`` () =
    let board, _ = Watch.apply Watch.empty "extract.started" Map.empty
    let board', changed = Watch.apply board "extract.started" Map.empty
    Assert.False changed
    Assert.Equal(1, List.length board'.Stages)

[<Fact>]
let ``Watch board: stageCompleted flips the line to Done with its duration`` () =
    let board, _ = Watch.apply Watch.empty "extract.started" Map.empty
    let board', changed =
        Watch.apply board "summary.stageCompleted" (payload [ "stage", box "extract"; "durationMs", box 1200L ])
    Assert.True changed
    match board'.Stages with
    | [ { Key = "extract"; State = Watch.Done(Some 1200L) } ] -> ()
    | other -> Assert.Fail(sprintf "expected extract Done 1200, got %A" other)

[<Fact>]
let ``Watch board: the pipeline umbrella stage is elided`` () =
    let board, changed =
        Watch.apply Watch.empty "summary.stageCompleted" (payload [ "stage", box "pipeline"; "durationMs", box 5L ])
    Assert.False changed
    Assert.Empty board.Stages

[<Fact>]
let ``Watch board: an unrelated envelope is not a renderable change`` () =
    let _, changed = Watch.apply Watch.empty "transform.lineage" Map.empty
    Assert.False changed

[<Fact>]
let ``Watch board: stages hold their first-seen order`` () =
    let b1, _ = Watch.apply Watch.empty "extract.started" Map.empty
    let b2, _ = Watch.apply b1 "summary.stageCompleted" (payload [ "stage", box "extract"; "durationMs", box 1L ])
    let b3, _ = Watch.apply b2 "profile.started" Map.empty
    let keys = b3.Stages |> List.map (fun s -> s.Key)
    Assert.Equal<string list>([ "extract"; "profile" ], keys)

// ---------------------------------------------------------------------------
// the voiced stage lines (§13 — gerund active, resultative done)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Watch line: an active stage reads the in-progress gerund`` () =
    let text = Watch.lineText { Key = "extract"; State = Watch.Active None }
    Assert.Contains("Reading the model", text)

[<Fact>]
let ``Watch line: a completed stage reads the resultative with its duration`` () =
    let text = Watch.lineText { Key = "extract"; State = Watch.Done(Some 1200L) }
    Assert.Contains("Model read complete", text)
    Assert.Contains("1.2s", text)

[<Fact>]
let ``Watch line: a completed stage with no duration omits the time`` () =
    let text = Watch.lineText { Key = "profile"; State = Watch.Done None }
    Assert.Contains("Data check complete", text)
    Assert.DoesNotContain("s ·", text)

[<Fact>]
let ``Watch line: every stage line clears the twelve-rule banned list`` () =
    let banned =
        [ "your"; "you "; " i "; " we "; "that's real"; ", not "
          "destroy"; "cleaned up"; "dig"; "oops"; "let's"; "refused" ]
    let lines =
        [ Watch.lineText { Key = "extract"; State = Watch.Pending }
          Watch.lineText { Key = "profile"; State = Watch.Pending }
          Watch.lineText { Key = "emit";    State = Watch.Pending }
          Watch.lineText { Key = "extract"; State = Watch.Active None }
          Watch.lineText { Key = "profile"; State = Watch.Active None }
          Watch.lineText { Key = "emit";    State = Watch.Active None }
          Watch.lineText { Key = "deploy";  State = Watch.Active(Some { Done = 142; Total = 300; ElapsedMs = 4000L }) }
          Watch.lineText { Key = "extract"; State = Watch.Done(Some 1200L) }
          Watch.lineText { Key = "profile"; State = Watch.Done None }
          Watch.lineText { Key = "emit";    State = Watch.Done(Some 800L) } ]
    for line in lines do
        let lowered = line.ToLowerInvariant()
        for b in banned do
            Assert.False(lowered.Contains b, sprintf "stage line '%s' breaks the banned list: contains '%s'" line b)

[<Fact>]
let ``Watch line: the board renders one line per stage`` () =
    let b1, _ = Watch.apply Watch.empty "extract.started" Map.empty
    let b2, _ = Watch.apply b1 "profile.started" Map.empty
    let renderable = Watch.toRenderable b2
    // The renderable is built without throwing; the board carries two stages.
    Assert.NotNull(box renderable)
    Assert.Equal(2, List.length b2.Stages)

// ---------------------------------------------------------------------------
// the seeded board (the whole arc visible from the first frame — Appendix A.3)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Watch board: seeded shows the whole planned arc as Pending`` () =
    let board = Watch.seeded [ "extract"; "profile"; "emit" ]
    Assert.Equal(3, List.length board.Stages)
    Assert.True(board.Stages |> List.forall (fun s -> s.State = Watch.Pending))

[<Fact>]
let ``Watch board: seeded omits the umbrella pipeline stage`` () =
    let board = Watch.seeded [ "pipeline"; "extract"; "emit" ]
    Assert.Equal<string list>([ "extract"; "emit" ], board.Stages |> List.map (fun s -> s.Key))

[<Fact>]
let ``Watch board: a started stage flips its seeded Pending line to Active in place`` () =
    let board = Watch.seeded [ "extract"; "profile"; "emit" ]
    let board', changed = Watch.apply board "profile.started" Map.empty
    Assert.True(changed)
    match board'.Stages with
    | [ { Key = "extract"; State = Watch.Pending }
        { Key = "profile"; State = Watch.Active None }
        { Key = "emit";    State = Watch.Pending } ] -> ()
    | other -> Assert.Fail(sprintf "expected profile Active in place, the others Pending, got %A" other)

[<Fact>]
let ``Watch board: completing a seeded stage flips it to Done with its duration`` () =
    let board = Watch.seeded [ "extract"; "profile" ]
    let started, _ = Watch.apply board "extract.started" Map.empty
    let done', changed =
        Watch.apply started "summary.stageCompleted" (payload [ "stage", box "extract"; "durationMs", box 900L ])
    Assert.True(changed)
    match done'.Stages |> List.tryFind (fun s -> s.Key = "extract") with
    | Some { State = Watch.Done(Some 900L) } -> ()
    | other -> Assert.Fail(sprintf "expected extract Done 900, got %A" other)

[<Fact>]
let ``Watch line: a pending stage reads the stage gerund (the board shows the whole arc)`` () =
    let text = Watch.lineText { Key = "emit"; State = Watch.Pending }
    Assert.Contains("Building the changes", text)

[<Fact>]
let ``Watch line: the migrate leg's stages read their voiced gerund + resultative`` () =
    // the live migrate board (build → apply → verify) — the executor streams
    // deploy.started / canary.started; the board voices them, never the code.
    Assert.Contains("Applying the changes", Watch.lineText { Key = "deploy"; State = Watch.Active None })
    Assert.Contains("Verifying the round-trip", Watch.lineText { Key = "canary"; State = Watch.Active None })
    Assert.Contains("Deploy complete", Watch.lineText { Key = "deploy"; State = Watch.Done None })
    Assert.Contains("Round-trip verification complete", Watch.lineText { Key = "canary"; State = Watch.Done None })
    // never the raw code
    Assert.DoesNotContain("deploy.started", Watch.lineText { Key = "deploy"; State = Watch.Active None })
    Assert.DoesNotContain("canary.started", Watch.lineText { Key = "canary"; State = Watch.Active None })

// ---------------------------------------------------------------------------
// intra-stage progress + the honest estimate (§13)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Watch progress: a stageProgress event updates the active stage's progress in place`` () =
    let board = Watch.seeded [ "deploy" ]
    let started, _ = Watch.apply board "deploy.started" Map.empty
    let progressed, changed =
        Watch.apply started "summary.stageProgress"
            (payload [ "stage", box "deploy"; "done", box 142; "total", box 300; "elapsedMs", box 4000L ])
    Assert.True(changed)
    match progressed.Stages with
    | [ { Key = "deploy"; State = Watch.Active(Some p) } ] ->
        Assert.Equal(142, p.Done)
        Assert.Equal(300, p.Total)
    | other -> Assert.Fail(sprintf "expected deploy Active with progress, got %A" other)

[<Fact>]
let ``Watch progress: a stageProgress for a not-yet-started stage is ignored`` () =
    let board = Watch.seeded [ "deploy" ]   // Pending, never started
    let _, changed =
        Watch.apply board "summary.stageProgress"
            (payload [ "stage", box "deploy"; "done", box 1; "total", box 10; "elapsedMs", box 100L ])
    Assert.False(changed)

[<Fact>]
let ``Watch progress: the active line shows N of M and the honest estimate`` () =
    let text =
        Watch.lineText { Key = "deploy"; State = Watch.Active(Some { Done = 142; Total = 300; ElapsedMs = 4000L }) }
    Assert.Contains("142 of 300", text)
    Assert.Contains("remaining", text)

[<Fact>]
let ``Watch progress: the estimate degrades honestly — none before the first item or at the last`` () =
    Assert.True((Watch.etaText { Done = 0;   Total = 300; ElapsedMs = 0L }).IsNone)    // nothing done yet
    Assert.True((Watch.etaText { Done = 300; Total = 300; ElapsedMs = 5000L }).IsNone) // complete
    Assert.True((Watch.etaText { Done = 150; Total = 300; ElapsedMs = 5000L }).IsSome) // halfway → an estimate

[<Fact>]
let ``Watch progress: the numerals are humane at scale`` () =
    let text = Watch.progressText { Done = 1420; Total = 3000; ElapsedMs = 1000L }
    Assert.Contains("1,420 of 3,000", text)
