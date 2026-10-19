module Twin.Tests.TwinIdentityTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core

// ---------------------------------------------------------------------------
// THE_TWIN.md §4.4 — the identity ACL: coordinate ↔ kind/attribute binding
// is exact-name, case-insensitive, and total (law 2 — an unbound coordinate
// refuses by name, never silently skips).
// ---------------------------------------------------------------------------

let private ok (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected success, got: %A" (es |> List.map (fun e -> e.Code))

let private name (s: string) : Name = Name.create s |> Result.value

let private attr (key: SsKey) (logical: string) (column: string) : Attribute =
    { Attribute.create key (name logical) PrimitiveType.Integer with
        Column = ColumnRealization.create column false |> Result.value }

let private customer : Kind =
    { Kind.create (kindKey ["C"]) (name "Customer") (mkTableId "dbo" "Customer")
        [ { attr (attrKey ["C"; "Id"]) "Id" "Id" with IsPrimaryKey = true }
          attr (attrKey ["C"; "Email"]) "Email" "Email" ] with
        Modality = [] }

let private catalog : Catalog =
    mkCatalog [ mkModule (modKey "M") (name "M") [ customer ] ]

let private index = CatalogIndex.ofCatalog catalog

[<Fact>]
let ``law 2: a defined table coordinate binds to its kind`` () =
    let kind = ok (CatalogIndex.bindKind index (ok (TableCoordinate.parse "dbo.Customer")))
    Assert.Equal(kindKey ["C"], kind.SsKey)

[<Fact>]
let ``binding is case-insensitive, the collation's semantics`` () =
    let kind = ok (CatalogIndex.bindKind index (ok (TableCoordinate.parse "DBO.CUSTOMER")))
    Assert.Equal(kindKey ["C"], kind.SsKey)

[<Fact>]
let ``law 2: an unknown table coordinate refuses by name`` () =
    match CatalogIndex.bindKind index (ok (TableCoordinate.parse "dbo.Ghost")) with
    | Ok _ -> failwith "expected the refusal"
    | Error (e :: _) ->
        Assert.Equal("twin.coordinate.table.unknown", e.Code)
        Assert.Equal(Some "dbo.Ghost", e.Metadata |> Map.tryFind "coordinate" |> Option.flatten)
    | Error [] -> failwith "empty error list"

[<Fact>]
let ``law 2: a column coordinate binds to its kind and attribute`` () =
    let kind, attribute = ok (CatalogIndex.bindColumn index (ok (ColumnCoordinate.parse "dbo.Customer.Email")))
    Assert.Equal(kindKey ["C"], kind.SsKey)
    Assert.Equal(attrKey ["C"; "Email"], attribute.SsKey)

[<Fact>]
let ``law 2: an unknown column coordinate refuses by name`` () =
    match CatalogIndex.bindColumn index (ok (ColumnCoordinate.parse "dbo.Customer.Ghost")) with
    | Ok _ -> failwith "expected the refusal"
    | Error (e :: _) -> Assert.Equal("twin.coordinate.column.unknown", e.Code)
    | Error [] -> failwith "empty error list"

[<Fact>]
let ``the coordinate of a kind round-trips through binding`` () =
    let coordinate = TwinIdentity.coordinateOfKind customer
    Assert.Equal("dbo.Customer", TableCoordinate.text coordinate)
    let bound = ok (CatalogIndex.bindKind index coordinate)
    Assert.Equal(customer.SsKey, bound.SsKey)

[<Fact>]
let ``containsTable answers the closed-set membership probe`` () =
    Assert.True(CatalogIndex.containsTable index (ok (TableCoordinate.parse "dbo.Customer")))
    Assert.False(CatalogIndex.containsTable index (ok (TableCoordinate.parse "dbo.Ghost")))
