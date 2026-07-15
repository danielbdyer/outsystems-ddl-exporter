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

let private quantum (cells: string list) : RowQuantum = { Cells = List.toArray cells }

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