module Projection.Tests.RowFidelityTests

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The pure row-fidelity comparator (T17, wave B2 — `Core/RowFidelity.fs`).
// The laws under test:
//   - KEY PLAN TOTALITY: a single integer key plans the lockstep; text,
//     composite, and absent keys carry their NAMED reasons (a downgrade is
//     never silent).
//   - THE DRILL-DOWN GRAIN: identical streams name nothing; a flipped cell
//     names its key and its differing columns; missing and extra rows name
//     their keys and directions — across the two renditions' bases (the
//     physical stream re-based onto the logical names).
//   - CAP HONESTY: the sample cap bounds the NAMED list; the difference
//     total stays exact.
//   - THE REFERENCE LAW: `compareOrdered` equals the set-based reference
//     diff over ordered unique-keyed streams (property) — the streaming
//     lockstep in `FidelityCompareRun` must equal this same reference; the
//     docker witness pins that equality live.
// ---------------------------------------------------------------------------

// The two renditions' column names for one kind — the physical (OSUSR) shape
// on the source side, the model's logical names on the target side; the
// rename map re-bases the source stream header-only.
let private physicalNames = [ "OSUSR_X_ID"; "OSUSR_X_EMAIL"; "OSUSR_X_NAME" ]
let private logicalNames  = [ "Id"; "Email"; "Name" ]

let private renameMap : Map<Name, Name> =
    List.zip physicalNames logicalNames
    |> List.map (fun (p, l) -> mkName p, mkName l)
    |> Map.ofList

let private sourceBasis = RowBasis.rename renameMap (RowBasis.ofNames (physicalNames |> List.map mkName))
let private targetBasis = RowBasis.ofNames (logicalNames |> List.map mkName)

/// Fixture cells are all present (non-NULL); a NULL cell is authored
/// with an explicit `ValueNone` at the site that needs one (WP-3).
let private quantum (cells: string list) : RowQuantum =
    { Cells = cells |> List.toArray |> Array.map ValueSome }

let private row (id: int64) (email: string) (name: string) : int64 * RowQuantum =
    id, quantum [ string id; email; name ]

// -- key-plan totality ---------------------------------------------------------

[<Fact>]
let ``keyPlanOf: a single integer key plans the lockstep; text, composite, and absent keys carry their named reasons`` () =
    // The shared Customer fixture carries a single Integer PK ("Id").
    match RowFidelity.keyPlanOf customer with
    | RowFidelity.KeyPlan.Int64Key column -> Assert.Equal("Id", Name.value column)
    | RowFidelity.KeyPlan.Unnameable reason -> Assert.Fail(sprintf "expected Int64Key; got Unnameable: %s" reason)
    let withAttributes (attrs: Attribute list) : Kind =
        { customer with Attributes = attrs }
    let textPk =
        withAttributes
            [ { Attribute.create (attrKey [ "K"; "Code" ]) (mkName "Code") Text with IsPrimaryKey = true } ]
    match RowFidelity.keyPlanOf textPk with
    | RowFidelity.KeyPlan.Unnameable reason -> Assert.Contains("integer key", reason)
    | other -> Assert.Fail(sprintf "expected Unnameable; got %A" other)
    let compositePk =
        withAttributes
            [ { Attribute.create (attrKey [ "K"; "A" ]) (mkName "A") Integer with IsPrimaryKey = true }
              { Attribute.create (attrKey [ "K"; "B" ]) (mkName "B") Integer with IsPrimaryKey = true } ]
    match RowFidelity.keyPlanOf compositePk with
    | RowFidelity.KeyPlan.Unnameable reason -> Assert.Contains("composite", reason)
    | other -> Assert.Fail(sprintf "expected Unnameable; got %A" other)
    let noPk =
        withAttributes
            [ { Attribute.create (attrKey [ "K"; "V" ]) (mkName "V") Integer with IsPrimaryKey = false } ]
    match RowFidelity.keyPlanOf noPk with
    | RowFidelity.KeyPlan.Unnameable reason -> Assert.Contains("no primary key", reason)
    | other -> Assert.Fail(sprintf "expected Unnameable; got %A" other)

// -- the drill-down grain -------------------------------------------------------

[<Fact>]
let ``compareOrdered: identical streams across the two renditions name nothing`` () =
    let rows = [ row 1L "a@x.example" "alpha"; row 2L "b@x.example" "bravo" ]
    let diffs, total = RowFidelity.compareOrdered 20 sourceBasis targetBasis rows rows
    Assert.Empty diffs
    Assert.Equal(0L, total)

[<Fact>]
let ``compareOrdered: one flipped cell names its key and its differing column (T17's drill-down grain)`` () =
    let left  = [ row 1041L "a@x.example" "alpha"; row 1042L "b@x.example" "bravo" ]
    let right = [ row 1041L "a@x.example" "alpha"; row 1042L "FLIPPED"      "bravo" ]
    let diffs, total = RowFidelity.compareOrdered 20 sourceBasis targetBasis left right
    Assert.Equal(1L, total)
    match diffs with
    | [ RowDifference.CellsDiffer (key, columns) ] ->
        Assert.Equal("1042", key)
        Assert.Equal<string list>([ "Email" ], columns |> List.map Name.value)
    | other -> Assert.Fail(sprintf "expected one CellsDiffer; got %A" other)

[<Fact>]
let ``compareOrdered: a missing and an extra row name their keys and directions`` () =
    let left  = [ row 1L "a@x" "alpha"; row 2L "b@x" "bravo"; row 3L "c@x" "charlie" ]
    let right = [ row 1L "a@x" "alpha"; row 3L "c@x" "charlie"; row 4L "d@x" "delta" ]
    let diffs, total = RowFidelity.compareOrdered 20 sourceBasis targetBasis left right
    Assert.Equal(2L, total)
    Assert.Equal<RowDifference list>(
        [ RowDifference.MissingInTarget "2"; RowDifference.ExtraInTarget "4" ], diffs)

[<Fact>]
let ``compareOrdered: the sample cap bounds the named list while the total stays exact`` () =
    let left  = [ for i in 1L .. 6L -> row i "same@x" "same" ]
    let right = [ for i in 1L .. 6L -> row i (sprintf "diff%d@x" (int i)) "same" ]
    let diffs, total = RowFidelity.compareOrdered 2 sourceBasis targetBasis left right
    Assert.Equal(6L, total)
    Assert.Equal(2, List.length diffs)

[<Fact>]
let ``differingColumns: the names walk the shared name-sorted order and cap at the culprit count`` () =
    let left  = quantum [ "1"; "a@x"; "alpha" ]
    let right = quantum [ "1"; "b@x"; "bravo" ]
    // Email and Name differ; name-sorted order is Email < Id < Name.
    Assert.Equal<string list>(
        [ "Email"; "Name" ],
        RowFidelity.differingColumns 4 sourceBasis targetBasis left right |> List.map Name.value)
    Assert.Equal<string list>(
        [ "Email" ],
        RowFidelity.differingColumns 1 sourceBasis targetBasis left right |> List.map Name.value)

// -- the reference law -----------------------------------------------------------

[<Property(MaxTest = 80)>]
let ``law: compareOrdered equals the set-based reference diff over ordered unique-keyed streams`` (leftSeed: byte list) (rightSeed: byte list) =
    // Unique ascending keys per side; a shared key's payload flips exactly
    // when the key is divisible by five — so all three difference families
    // arise from the seeds.
    let keysOf (seed: byte list) : int64 list =
        seed |> List.map int64 |> List.distinct |> List.sort
    let leftKeys = keysOf leftSeed
    let rightKeys = keysOf rightSeed
    let leftRows = leftKeys |> List.map (fun k -> row k "same@x" "same")
    let rightRows =
        rightKeys
        |> List.map (fun k -> if k % 5L = 0L then row k "flipped@x" "same" else row k "same@x" "same")
    let diffs, total = RowFidelity.compareOrdered 1000 sourceBasis targetBasis leftRows rightRows
    let leftSet = Set.ofList leftKeys
    let rightSet = Set.ofList rightKeys
    let expectedMissing = Set.difference leftSet rightSet
    let expectedExtra = Set.difference rightSet leftSet
    let expectedDiffer = Set.intersect leftSet rightSet |> Set.filter (fun k -> k % 5L = 0L)
    let actualMissing =
        diffs |> List.choose (function RowDifference.MissingInTarget k -> Some (int64 k) | _ -> None) |> Set.ofList
    let actualExtra =
        diffs |> List.choose (function RowDifference.ExtraInTarget k -> Some (int64 k) | _ -> None) |> Set.ofList
    let actualDiffer =
        diffs |> List.choose (function RowDifference.CellsDiffer (k, _) -> Some (int64 k) | _ -> None) |> Set.ofList
    actualMissing = expectedMissing
    && actualExtra = expectedExtra
    && actualDiffer = expectedDiffer
    && total = int64 (Set.count expectedMissing + Set.count expectedExtra + Set.count expectedDiffer)

// -- the verb routing (`check data --rows`) --------------------------------------

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es

let private rowsJson = """
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN" }
  }
}
"""

[<Fact>]
let ``check data --rows: the full operand set rides the args record with the twenty-row default cap`` () =
    let cfg = ProjectionConfig.parse rowsJson |> mustOk
    match (Command.planCheck cfg [ "data"; "--rows"; "--before"; "cloud-dev"; "--after"; "cloud-qa"; "--model"; "model.json" ]).Action with
    | PlanAction.CheckDataRows args ->
        Assert.Equal("cloud-dev", args.BeforeLabel)
        Assert.Equal("cloud-qa", args.AfterLabel)
        Assert.Equal("model.json", args.ModelRef)
        Assert.Equal(20, args.SampleCap)
        Assert.False args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckDataRows; got %A" other)

[<Fact>]
let ``check data --rows: no model ⇒ named refusal that says WHY, exit 2`` () =
    let cfg = ProjectionConfig.parse rowsJson |> mustOk
    match (Command.planCheck cfg [ "data"; "--rows"; "--before"; "cloud-dev"; "--after"; "cloud-qa" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.dataRowsNoModel", e.Code)
        Assert.Contains("rename map", e.Message)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check data --rows: a non-numeric sample ⇒ named refusal, exit 2; a numeric one rides`` () =
    let cfg = ProjectionConfig.parse rowsJson |> mustOk
    match (Command.planCheck cfg [ "data"; "--rows"; "--before"; "cloud-dev"; "--after"; "cloud-qa"; "--model"; "m.json"; "--sample"; "many" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.dataRowsSample", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)
    match (Command.planCheck cfg [ "data"; "--rows"; "--before"; "cloud-dev"; "--after"; "cloud-qa"; "--model"; "m.json"; "--sample"; "5" ]).Action with
    | PlanAction.CheckDataRows args -> Assert.Equal(5, args.SampleCap)
    | other -> Assert.Fail(sprintf "expected CheckDataRows; got %A" other)

[<Fact>]
let ``check data without --rows keeps its aggregate-count path (the arm is additive)`` () =
    let cfg = ProjectionConfig.parse rowsJson |> mustOk
    match (Command.planCheck cfg [ "data"; "--before"; "cloud-dev"; "--after"; "cloud-qa" ]).Action with
    | PlanAction.CheckData _ -> ()
    | other -> Assert.Fail(sprintf "expected CheckData; got %A" other)

// -- the intervention replay + the canonical-form erasures (wave B4b) ---------

[<Fact>]
let ``canonicalizeDateTimeCells: sub-millisecond tick residue truncates to the millisecond form; the original quantum is never mutated`` () =
    let ordinals = [| 1 |]
    let q = quantum [ "7"; "2026-01-02 03:04:05.0033333"; "alpha" ]
    let canonical = RowFidelity.canonicalizeDateTimeCells ordinals q
    Assert.Equal(ValueSome "2026-01-02 03:04:05.003", canonical.Cells.[1])
    Assert.Equal(ValueSome "7", canonical.Cells.[0])
    Assert.Equal(ValueSome "2026-01-02 03:04:05.0033333", q.Cells.[1])
    // identity when nothing exceeds the millisecond form
    let short = quantum [ "7"; "2026-01-02 03:04:05.003"; "alpha" ]
    Assert.Equal<string voption[]>(short.Cells, (RowFidelity.canonicalizeDateTimeCells ordinals short).Cells)

[<Fact>]
let ``canonicalizeDateTimeCells: one instant stored at datetime and datetime2 precision reads equal after the erasure`` () =
    let ordinals = [| 0 |]
    let fromDateTime  = RowFidelity.canonicalizeDateTimeCells ordinals (quantum [ "2026-01-02 03:04:05.0030000" ])
    let fromDateTime2 = RowFidelity.canonicalizeDateTimeCells ordinals (quantum [ "2026-01-02 03:04:05.0033333" ])
    Assert.Equal<string voption[]>(fromDateTime.Cells, fromDateTime2.Cells)

[<Fact>]
let ``replayQuantum: the own key and the referencing cells rewrite through their maps; absent values ride unchanged (preserved keys are identity)`` () =
    let q = quantum [ "3"; "10"; "alpha" ]
    let keyMap = Map.ofList [ "3", "2001" ]
    let fkMap = Map.ofList [ "10", "907" ]
    let replayed = RowFidelity.replayQuantum (Some (0, keyMap)) [ 1, fkMap ] q
    Assert.Equal<string voption list>([ ValueSome "2001"; ValueSome "907"; ValueSome "alpha" ], replayed.Cells |> Array.toList)
    Assert.Equal(ValueSome "3", q.Cells.[0])
    let unmatched = RowFidelity.replayQuantum (Some (0, Map.ofList [ "99", "1" ])) [ 1, Map.ofList [ "99", "1" ] ] q
    Assert.Equal<string voption[]>(q.Cells, unmatched.Cells)

[<Fact>]
let ``loadReplayMaps: journaled pairs fold keep-first per source key in chunk order; the --journal directory resolves its single file (at-least-once dedupe)`` () =
    let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fid-journal-" + System.Guid.NewGuid().ToString "N")
    try
        let journal = CaptureJournal.create dir "b4b-replay-witness"
        let chunk (ix: int) (pairs: (string * string) list) : ChunkRecord =
            { Kind = "Customer"; ChunkIx = ix; FirstPk = "1"; LastPk = "9"
              RawCount = List.length pairs; WrittenCount = List.length pairs
              Pairs = pairs |> List.map (fun (s, a) -> [| s; a |]) |> List.toArray }
        CaptureJournal.append journal (chunk 0 [ "1", "901"; "2", "902" ])
        // the resumed run re-appends its chunk (at-least-once) — last write
        // per chunk index wins at load, so the fold sees it once
        CaptureJournal.append journal (chunk 0 [ "1", "901"; "2", "902" ])
        // a later chunk overlapping an earlier source key — keep-first wins
        CaptureJournal.append journal (chunk 1 [ "2", "888"; "3", "903" ])
        let maps =
            match FidelityCompareRun.loadReplayMaps (CaptureJournal.filePath journal) with
            | Ok m -> m
            | Error es -> failwithf "loadReplayMaps failed: %A" es
        let customer = maps.["Customer"]
        Assert.Equal("901", customer.["1"])
        Assert.Equal("902", customer.["2"])
        Assert.Equal("903", customer.["3"])
        Assert.Equal(3, customer.Count)
        let viaDirectory =
            match FidelityCompareRun.loadReplayMaps dir with
            | Ok m -> m
            | Error es -> failwithf "directory resolution failed: %A" es
        Assert.Equal("901", viaDirectory.["Customer"].["1"])
    finally
        if System.IO.Directory.Exists dir then System.IO.Directory.Delete(dir, true)

[<Fact>]
let ``loadReplayMaps: a missing path and an ambiguous directory refuse by name`` () =
    match FidelityCompareRun.loadReplayMaps (System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-journal.ndjson")) with
    | Error (e :: _) -> Assert.Equal("fidelity.rows.journalMissing", e.Code)
    | other -> Assert.Fail(sprintf "expected the missing refusal; got %A" other)
    let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fid-journal-ambig-" + System.Guid.NewGuid().ToString "N")
    try
        System.IO.Directory.CreateDirectory dir |> ignore
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "transfer-aaaa.ndjson"), "")
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "transfer-bbbb.ndjson"), "")
        match FidelityCompareRun.loadReplayMaps dir with
        | Error (e :: _) -> Assert.Equal("fidelity.rows.journalAmbiguous", e.Code)
        | other -> Assert.Fail(sprintf "expected the ambiguity refusal; got %A" other)
    finally
        if System.IO.Directory.Exists dir then System.IO.Directory.Delete(dir, true)

// ---------------------------------------------------------------------------
// Wave B4a — the `@runId` interventions operand: a stored run's recorded
// `JournalRef` (digest + path) resolves to its journal file; every miss is a
// NAMED refusal. The run store rides `PROJECTION_RUNS_DIR` (saved/restored
// around each fact — the RunTests R1b pattern; `PROJECTION_LEDGER_DIR` is
// never touched, so the concurrent bracket-capture facts stay undisturbed).
// ---------------------------------------------------------------------------

let private withRunStore (f: string -> unit) : unit =
    let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fid-runstore-" + System.Guid.NewGuid().ToString "N")
    let prior = System.Environment.GetEnvironmentVariable "PROJECTION_RUNS_DIR"
    try
        System.IO.Directory.CreateDirectory dir |> ignore
        System.Environment.SetEnvironmentVariable("PROJECTION_RUNS_DIR", dir)
        f dir
    finally
        System.Environment.SetEnvironmentVariable("PROJECTION_RUNS_DIR", prior)
        if System.IO.Directory.Exists dir then System.IO.Directory.Delete(dir, true)

let private storedRun (runId: string) (ledgers: Run.LedgerRef list) : Run.Run =
    { RunId = runId; Ts = "2026-07-17T08:00:00Z"; Command = "projection move"
      InputDigest = ""; Outcome = "succeeded"; Canary = None
      Registered = 0; Applied = 0; Declined = 0
      Events = []; Artifacts = Map.empty; Ledgers = ledgers; Bench = None }

[<Fact>]
let ``interventions run reference: the stored run's JournalRef resolves to its journal file and the pairs fold (B4a)`` () =
    withRunStore (fun store ->
        let journalDir = System.IO.Path.Combine(store, "journals")
        let journal = CaptureJournal.create journalDir "b4a-run-ref-witness"
        CaptureJournal.append journal
            { Kind = "Customer"; ChunkIx = 0; FirstPk = "1"; LastPk = "2"; RawCount = 2; WrittenCount = 2
              Pairs = [| [| "1"; "901" |]; [| "2"; "902" |] |] }
        let path = CaptureJournal.filePath journal
        let digest =
            match CaptureJournal.digestOfFile path with
            | Some d -> d
            | None -> failwith "the journal file name must carry its digest (RI-7)"
        Run.save store (storedRun "01B4ARUN" [ Run.JournalRef (digest, path) ])
        match FidelityCompareRun.loadReplayMaps "@01B4ARUN" with
        | Ok maps ->
            Assert.Equal("901", maps.["Customer"].["1"])
            Assert.Equal("902", maps.["Customer"].["2"])
        | Error es -> failwithf "expected the run ref to resolve: %A" es)

[<Fact>]
let ``interventions run reference: an unknown run, a run with no journal, a moved file, and a pathless pre-B4a record each refuse by name`` () =
    withRunStore (fun store ->
        // unknown run
        (match FidelityCompareRun.loadReplayMaps "@01NOSUCH" with
         | Error (e :: _) -> Assert.Equal("fidelity.rows.runNotFound", e.Code)
         | other -> Assert.Fail(sprintf "expected runNotFound; got %A" other))
        // a run that kept no ledger
        Run.save store (storedRun "01NOLEDGER" [])
        (match FidelityCompareRun.loadReplayMaps "@01NOLEDGER" with
         | Error (e :: _) -> Assert.Equal("fidelity.rows.runNoJournal", e.Code)
         | other -> Assert.Fail(sprintf "expected runNoJournal; got %A" other))
        // a recorded file that is no longer there
        Run.save store (storedRun "01MOVED" [ Run.JournalRef ("aaaa", System.IO.Path.Combine(store, "transfer-aaaa.ndjson")) ])
        (match FidelityCompareRun.loadReplayMaps "@01MOVED" with
         | Error (e :: _) -> Assert.Equal("fidelity.rows.journalMissing", e.Code)
         | other -> Assert.Fail(sprintf "expected journalMissing; got %A" other))
        // a pre-B4a record: digest without a path
        Run.save store (storedRun "01OLDREC" [ Run.JournalRef ("bbbb", "") ])
        (match FidelityCompareRun.loadReplayMaps "@01OLDREC" with
         | Error (e :: _) -> Assert.Equal("fidelity.rows.journalMissing", e.Code)
         | other -> Assert.Fail(sprintf "expected the pathless refusal; got %A" other)))

[<Fact>]
let ``journal addressing: digestOfFile reads the digest back out of the RI-7 file name; startFresh truncates to an honest empty`` () =
    Assert.Equal(Some "a1b2c3d4e5f60718", CaptureJournal.digestOfFile "/journals/transfer-a1b2c3d4e5f60718.ndjson")
    Assert.Equal(None, CaptureJournal.digestOfFile "/journals/notes.txt")
    Assert.Equal(None, CaptureJournal.digestOfFile "/journals/transfer-.ndjson")
    let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fid-fresh-" + System.Guid.NewGuid().ToString "N")
    try
        let journal = CaptureJournal.create dir "b4a-fresh-witness"
        CaptureJournal.append journal
            { Kind = "Customer"; ChunkIx = 0; FirstPk = "1"; LastPk = "1"; RawCount = 1; WrittenCount = 1
              Pairs = [| [| "1"; "901" |] |] }
        CaptureJournal.startFresh journal
        // the file EXISTS and is empty — "the ledger was kept, nothing was
        // minted" is a provable claim; a deleted file would only say no one
        // was keeping records.
        Assert.True(System.IO.File.Exists (CaptureJournal.filePath journal))
        match FidelityCompareRun.loadReplayMaps (CaptureJournal.filePath journal) with
        | Ok maps -> Assert.True(Map.isEmpty maps)
        | Error es -> failwithf "an empty journal must load as empty maps: %A" es
    finally
        if System.IO.Directory.Exists dir then System.IO.Directory.Delete(dir, true)

[<Fact>]
let ``check data --rows: --interventions rides the args record — the path form and the run-reference form both pass through (B4a lifted the surface refusal)`` () =
    let cfg = ProjectionConfig.parse rowsJson |> mustOk
    match (Command.planCheck cfg [ "data"; "--rows"; "--before"; "cloud-dev"; "--after"; "cloud-qa"; "--model"; "m.json"; "--interventions"; "./journal" ]).Action with
    | PlanAction.CheckDataRows args -> Assert.Equal(Some "./journal", args.Interventions)
    | other -> Assert.Fail(sprintf "expected CheckDataRows; got %A" other)
    // B4a — a stored run now carries its ledger refs, so the run-reference
    // form plans through; the ENGINE resolves it (or refuses by name:
    // runStoreMissing / runNotFound / runNoJournal — RowFidelity's own facts).
    match (Command.planCheck cfg [ "data"; "--rows"; "--before"; "cloud-dev"; "--after"; "cloud-qa"; "--model"; "m.json"; "--interventions"; "@01HXYZ" ]).Action with
    | PlanAction.CheckDataRows args -> Assert.Equal(Some "@01HXYZ", args.Interventions)
    | other -> Assert.Fail(sprintf "expected CheckDataRows with the run reference; got %A" other)

// ---------------------------------------------------------------------------
// Wave B5 — `check fidelity <flow>` routing (DECISIONS 2026-07-15 decision 4:
// one fidelity concept, file-source and estate-source; flow-map membership is
// tested BEFORE the `.sql`-path default arm, and every other reading refuses
// by name).
// ---------------------------------------------------------------------------

let private fidelityFlowJson = """
{
  "model": "model.json",
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN" }
  },
  "flows": {
    "load-qa": { "from": "cloud-dev", "to": "cloud-qa" },
    "publish": { "from": "model", "to": "cloud-qa" }
  }
}
"""

[<Fact>]
let ``check fidelity flow: a flow from a live environment plans the container proof with the model and the twenty-row default cap`` () =
    let cfg = ProjectionConfig.parse fidelityFlowJson |> mustOk
    match (Command.planCheck cfg [ "fidelity"; "load-qa" ]).Action with
    | PlanAction.CheckFidelityFlow (model, _, args) ->
        Assert.Equal(ModelSource.ModelFile "model.json", model)
        Assert.Equal("load-qa", args.Flow)
        Assert.Equal("cloud-dev", args.FromLabel)
        Assert.Equal(20, args.SampleCap)
        Assert.False args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckFidelityFlow; got %A" other)
    match (Command.planCheck cfg [ "fidelity"; "load-qa"; "--sample"; "5"; "--format"; "json" ]).Action with
    | PlanAction.CheckFidelityFlow (_, _, args) ->
        Assert.Equal(5, args.SampleCap)
        Assert.True args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckFidelityFlow; got %A" other)

[<Fact>]
let ``check fidelity flow: every other reading refuses by name — model-sourced flow, unknown token, bare form, bad sample; a sql path keeps the canary`` () =
    let cfg = ProjectionConfig.parse fidelityFlowJson |> mustOk
    let codeOf (action: PlanAction) =
        match action with
        | PlanAction.Refused (exit, e) -> Some (exit, e.Code)
        | _ -> None
    // a flow that does not draw from a live environment
    Assert.Equal(Some (2, "cli.check.fidelityFlowSource"), codeOf (Command.planCheck cfg [ "fidelity"; "publish" ]).Action)
    // neither a flow nor a .sql path
    Assert.Equal(Some (2, "cli.check.fidelityUnknownFlow"), codeOf (Command.planCheck cfg [ "fidelity"; "nope" ]).Action)
    // the bare form names both readings
    Assert.Equal(Some (2, "cli.check.fidelityArgs"), codeOf (Command.planCheck cfg [ "fidelity" ]).Action)
    // a malformed sample cap
    Assert.Equal(Some (2, "cli.check.fidelitySample"), codeOf (Command.planCheck cfg [ "fidelity"; "load-qa"; "--sample"; "many" ]).Action)
    // the DDL round-trip canary's historical spelling is byte-identical
    match (Command.planCheck cfg [ "fidelity"; "legacy.sql" ]).Action with
    | PlanAction.CheckCanary ("legacy.sql", false) -> ()
    | other -> Assert.Fail(sprintf "expected CheckCanary; got %A" other)
    // and so is the bare `check <source.sql>` default arm
    match (Command.planCheck cfg [ "source.sql" ]).Action with
    | PlanAction.CheckCanary ("source.sql", false) -> ()
    | other -> Assert.Fail(sprintf "expected CheckCanary; got %A" other)

[<Fact>]
let ``check fidelity flow: a config with no model refuses by name — the proof stands the model up`` () =
    let cfg = ProjectionConfig.parse """
{
  "environments": { "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" }, "cloud-qa": { "access": "direct", "conn": "env:CLOUD_QA_CONN" } },
  "flows": { "load-qa": { "from": "cloud-dev", "to": "cloud-qa" } }
}
"""
              |> mustOk
    match (Command.planCheck cfg [ "fidelity"; "load-qa" ]).Action with
    | PlanAction.Refused (2, e) -> Assert.Equal("cli.check.fidelityNoModel", e.Code)
    | other -> Assert.Fail(sprintf "expected the no-model refusal; got %A" other)

[<Fact>]
let ``the three canonical-form erasures are minted, named, and declared in force on the rows proof`` () =
    for t in FidelityCompareRun.tolerancesInForce do
        Assert.Contains(t, ToleratedDivergence.allKnown)
        Assert.Equal(Some t, ToleratedDivergence.tryParse (ToleratedDivergence.name t))
    Assert.Equal<string list>(
        [ "BooleanCanonicalizationTolerated"; "DateTimeTickPrecisionTolerated"; "IntegerWidthNormalized" ],
        FidelityCompareRun.tolerancesInForce |> List.map ToleratedDivergence.name)