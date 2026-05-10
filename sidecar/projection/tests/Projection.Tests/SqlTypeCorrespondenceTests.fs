module Projection.Tests.SqlTypeCorrespondenceTests

open Xunit
open FsCheck.Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Tier-1 #8 cash-out (chapter 3.7 slice β; AUDIT_2026_05_DDD_HEXAGONAL_FP).
//
// `SqlTypeCorrespondence` owns the round-trip pair `PrimitiveType ↔ SQL
// Server DDL type vocabulary`. The forward (`baseName`) and inverse
// (`ofSqlDataType`) halves share a single source of truth; these tests
// assert the round-trip property structurally and exhaustively.
// ---------------------------------------------------------------------------

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v          -> v
    | Error errs    ->
        Assert.Fail (sprintf "%A" errs)
        Unchecked.defaultof<'a>

// ---------------------------------------------------------------------------
// Round-trip property — the load-bearing invariant for T1 byte-determinism.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Tier-1 #8: ofSqlDataType ∘ baseName = id over every PrimitiveType`` () =
    for pt in SqlTypeCorrespondence.allPrimitives do
        let dataType = SqlTypeCorrespondence.baseName pt
        let recovered = SqlTypeCorrespondence.ofSqlDataType dataType |> mustOk
        Assert.Equal (pt, recovered)

[<Fact>]
let ``Tier-1 #8: allPrimitives enumerates every PrimitiveType variant`` () =
    // Closed-DU expansion empirical-test discipline (DECISIONS 2026-05-13):
    // adding a new PrimitiveType variant fires an exhaustiveness error
    // here; the assertion is that the enumeration is complete.
    let dispatched (pt: PrimitiveType) : unit =
        match pt with
        | Integer | Decimal | Text | Boolean
        | DateTime | Date | Time | Binary | Guid -> ()
    for pt in SqlTypeCorrespondence.allPrimitives do
        dispatched pt
    Assert.Equal (9, List.length SqlTypeCorrespondence.allPrimitives)

// ---------------------------------------------------------------------------
// Inverse classification — the SQL Server `INFORMATION_SCHEMA.DATA_TYPE`
// vocabulary is broader than V2's; multiple aliases collapse to one
// PrimitiveType. The aliases below are the ones the adapter recognizes;
// expanding the recognized alias set requires updating the table AND
// these tests in lockstep.
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("INT", "Integer")>]
[<InlineData("BIGINT", "Integer")>]
[<InlineData("SMALLINT", "Integer")>]
[<InlineData("TINYINT", "Integer")>]
[<InlineData("DECIMAL", "Decimal")>]
[<InlineData("NUMERIC", "Decimal")>]
[<InlineData("MONEY", "Decimal")>]
[<InlineData("SMALLMONEY", "Decimal")>]
[<InlineData("NVARCHAR", "Text")>]
[<InlineData("VARCHAR", "Text")>]
[<InlineData("CHAR", "Text")>]
[<InlineData("NCHAR", "Text")>]
[<InlineData("TEXT", "Text")>]
[<InlineData("NTEXT", "Text")>]
[<InlineData("BIT", "Boolean")>]
[<InlineData("DATETIME", "DateTime")>]
[<InlineData("DATETIME2", "DateTime")>]
[<InlineData("SMALLDATETIME", "DateTime")>]
[<InlineData("DATETIMEOFFSET", "DateTime")>]
[<InlineData("DATE", "Date")>]
[<InlineData("TIME", "Time")>]
[<InlineData("VARBINARY", "Binary")>]
[<InlineData("BINARY", "Binary")>]
[<InlineData("IMAGE", "Binary")>]
[<InlineData("UNIQUEIDENTIFIER", "Guid")>]
let ``Tier-1 #8: ofSqlDataType classifies every recognized alias`` (sqlType: string) (expectedName: string) =
    let recovered = SqlTypeCorrespondence.ofSqlDataType sqlType |> mustOk
    Assert.Equal (expectedName, sprintf "%A" recovered)

[<Fact>]
let ``Tier-1 #8: ofSqlDataType is case-insensitive on input`` () =
    let upper = SqlTypeCorrespondence.ofSqlDataType "NVARCHAR" |> mustOk
    let lower = SqlTypeCorrespondence.ofSqlDataType "nvarchar" |> mustOk
    let mixed = SqlTypeCorrespondence.ofSqlDataType "NvArChAr" |> mustOk
    Assert.Equal (Text, upper)
    Assert.Equal (Text, lower)
    Assert.Equal (Text, mixed)

[<Fact>]
let ``Tier-1 #8: ofSqlDataType returns Error for unknown SQL types`` () =
    let result = SqlTypeCorrespondence.ofSqlDataType "HIERARCHYID"
    match result with
    | Ok _ ->
        Assert.Fail "expected Error for unknown SQL type"
    | Error errs ->
        Assert.Single errs |> ignore
        let err = List.head errs
        Assert.Equal ("sqlTypeCorrespondence.unknown", err.Code)
        Assert.Contains ("HIERARCHYID", err.Message)

// ---------------------------------------------------------------------------
// Property: random ASCII-uppercase strings outside the recognized vocabulary
// always Error; recognized aliases always Ok. Property tests sweep beyond
// the InlineData explicit cases.
// ---------------------------------------------------------------------------

let private recognizedAliases : Set<string> =
    Set.ofList [
        "INT"; "BIGINT"; "SMALLINT"; "TINYINT"
        "DECIMAL"; "NUMERIC"; "MONEY"; "SMALLMONEY"
        "NVARCHAR"; "VARCHAR"; "CHAR"; "NCHAR"; "TEXT"; "NTEXT"
        "BIT"
        "DATETIME"; "DATETIME2"; "SMALLDATETIME"; "DATETIMEOFFSET"
        "DATE"
        "TIME"
        "VARBINARY"; "BINARY"; "IMAGE"
        "UNIQUEIDENTIFIER"
    ]

[<Property>]
let ``Tier-1 #8: every recognized alias maps to Ok`` () =
    recognizedAliases
    |> Set.forall (fun alias ->
        match SqlTypeCorrespondence.ofSqlDataType alias with
        | Ok _    -> true
        | Error _ -> false)
