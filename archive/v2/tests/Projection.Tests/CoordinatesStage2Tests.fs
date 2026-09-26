module Projection.Tests.CoordinatesStage2Tests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Chapter 5 slice θ — Coordinates Stage 2 typed value objects.
//
// Per `CHAPTER_5_OPEN.md` slice θ: `SchemaName` / `TableName` /
// `ColumnName` smart constructors land in `Coordinates.fs`. The migration
// of `PhysicalRealization` / `Column.ColumnName` record fields stays
// deferred-with-trigger at this slice (typed surface is opt-in for new
// code; existing string-field readers keep compiling).
//
// The smart constructor's invariants per slice θ:
//   • Reject null / empty / whitespace.
//   • Reject identifiers longer than 128 characters (SQL Server
//     identifier limit per Microsoft Learn).
//   • Accept any otherwise-valid identifier string (bracket-quoted
//     identifiers may carry SQL-reserved characters; that's a render-
//     time concern, not a construction concern).
// ---------------------------------------------------------------------------

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error errs ->
        Assert.Fail (sprintf "expected Ok; got %A" errs)
        Unchecked.defaultof<'a>

let private mustFailWithCode (code: string) (r: Result<'a>) : unit =
    match r with
    | Ok _ ->
        Assert.Fail (sprintf "expected failure with code %s; got Ok" code)
    | Error errs ->
        let codes = errs |> List.map (fun e -> e.Code)
        Assert.Contains (code, codes)

// ---------------------------------------------------------------------------
// SchemaName acceptance / rejection.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SchemaName.create accepts a normal identifier and round-trips via value`` () =
    let s = SchemaName.create "dbo" |> mustOk
    Assert.Equal<string> ("dbo", SchemaName.value s)

[<Fact>]
let ``SchemaName.create rejects empty string with schemaName.empty`` () =
    SchemaName.create "" |> mustFailWithCode "schemaName.empty"

[<Fact>]
let ``SchemaName.create rejects whitespace-only string with schemaName.empty`` () =
    SchemaName.create "   " |> mustFailWithCode "schemaName.empty"

[<Fact>]
let ``SchemaName.create rejects identifier longer than 128 characters`` () =
    let tooLong = System.String('a', 129)
    SchemaName.create tooLong |> mustFailWithCode "schemaName.tooLong"

[<Fact>]
let ``SchemaName.create accepts identifier of exactly 128 characters`` () =
    let atLimit = System.String('a', 128)
    let s = SchemaName.create atLimit |> mustOk
    Assert.Equal<string> (atLimit, SchemaName.value s)

// ---------------------------------------------------------------------------
// TableName acceptance / rejection — mirrors SchemaName.
// ---------------------------------------------------------------------------

[<Fact>]
let ``TableName.create accepts a normal identifier and round-trips via value`` () =
    let t = TableName.create "OSUSR_S1S_CUSTOMER" |> mustOk
    Assert.Equal<string> ("OSUSR_S1S_CUSTOMER", TableName.value t)

[<Fact>]
let ``TableName.create rejects empty string with tableName.empty`` () =
    TableName.create "" |> mustFailWithCode "tableName.empty"

[<Fact>]
let ``TableName.create rejects identifier longer than 128 characters with tableName.tooLong`` () =
    let tooLong = System.String('t', 200)
    TableName.create tooLong |> mustFailWithCode "tableName.tooLong"

// ---------------------------------------------------------------------------
// ColumnName acceptance / rejection — mirrors SchemaName / TableName.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ColumnName.create accepts a normal identifier and round-trips via value`` () =
    let c = ColumnName.create "CUSTOMER_ID" |> mustOk
    Assert.Equal<string> ("CUSTOMER_ID", ColumnName.value c)

[<Fact>]
let ``ColumnName.create rejects empty string with columnName.empty`` () =
    ColumnName.create "" |> mustFailWithCode "columnName.empty"

[<Fact>]
let ``ColumnName.create rejects identifier longer than 128 characters with columnName.tooLong`` () =
    let tooLong = System.String('c', 256)
    ColumnName.create tooLong |> mustFailWithCode "columnName.tooLong"

// ---------------------------------------------------------------------------
// Type-distinctness witness — the compiler refuses to confuse a
// SchemaName with a TableName (or with a raw string). The "test" here
// is structural: the lines that would substitute the wrong type
// across the three VOs would fail to compile. We exercise the round-
// trip to demonstrate the three constructors return distinct types.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Stage 2: SchemaName / TableName / ColumnName are distinct types`` () =
    // Construct one of each.
    let s = SchemaName.create "dbo"  |> mustOk
    let t = TableName.create  "X"    |> mustOk
    let c = ColumnName.create "ID"   |> mustOk
    // Each projects to its underlying string via its own module's
    // accessor. Cross-module access would fail at compile time —
    // `SchemaName.value t` doesn't typecheck because `t : TableName`.
    Assert.Equal<string> ("dbo", SchemaName.value s)
    Assert.Equal<string> ("X",   TableName.value  t)
    Assert.Equal<string> ("ID",  ColumnName.value c)
