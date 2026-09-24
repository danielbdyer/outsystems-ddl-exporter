module Twin.Tests.CoordinateTests

open Xunit
open Projection.Core
open Twin.Core

// ---------------------------------------------------------------------------
// THE_TWIN.md §identity — coordinates: the dotted wire form parses strictly,
// matching is case-insensitive (SQL Server default-collation semantics), and
// unsupported shapes refuse by name.
// ---------------------------------------------------------------------------

let private ok (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected success, got: %A" (es |> List.map (fun e -> e.Code))

let private codeOf (r: Result<'a>) : string =
    match r with
    | Ok _ -> failwith "expected a refusal"
    | Error (e :: _) -> e.Code
    | Error [] -> failwith "empty error list"

[<Fact>]
let ``a table coordinate parses from schema.table`` () =
    let c = ok (TableCoordinate.parse "dbo.Customer")
    Assert.Equal("dbo.Customer", TableCoordinate.text c)

[<Fact>]
let ``a column coordinate parses from schema.table.column`` () =
    let c = ok (ColumnCoordinate.parse "dbo.Customer.Email")
    Assert.Equal("dbo.Customer.Email", ColumnCoordinate.text c)

[<Fact>]
let ``coordinate keys are case-insensitive`` () =
    let a = ok (TableCoordinate.parse "DBO.CUSTOMER")
    let b = ok (TableCoordinate.parse "dbo.Customer")
    Assert.Equal(TableCoordinate.key a, TableCoordinate.key b)
    let ca = ok (ColumnCoordinate.parse "DBO.Customer.EMAIL")
    let cb = ok (ColumnCoordinate.parse "dbo.CUSTOMER.email")
    Assert.Equal(ColumnCoordinate.key ca, ColumnCoordinate.key cb)

[<Fact>]
let ``a one-segment table coordinate refuses as malformed`` () =
    Assert.Equal("twin.coordinate.table.malformed", codeOf (TableCoordinate.parse "Customer"))

[<Fact>]
let ``a three-segment table coordinate refuses as malformed`` () =
    Assert.Equal("twin.coordinate.table.malformed", codeOf (TableCoordinate.parse "db.dbo.Customer"))

[<Fact>]
let ``a bracketed segment refuses as unsupported`` () =
    Assert.Equal("twin.coordinate.unsupported", codeOf (TableCoordinate.parse "[dbo].Customer"))

[<Fact>]
let ``a two-segment column coordinate refuses as malformed`` () =
    Assert.Equal("twin.coordinate.column.malformed", codeOf (ColumnCoordinate.parse "dbo.Customer"))

[<Fact>]
let ``coordinate text round-trips through parse`` () =
    let c = ok (TableCoordinate.parse "sales.OrderLine")
    Assert.Equal(c, ok (TableCoordinate.parse (TableCoordinate.text c)))
