module Projection.Tests.TransferCellShapingTests

// THE CELL SHAPING availability rule: the bulk column list derives from the
// UNION of the rows' key sets, never the first row's alone. An ingested
// kind's key set is uniform (the SELECT projects every column), so the union
// equals any one row's set — byte-identical. A σ-MINTED row omits the key
// for a cell drawn NULL, so under first-row semantics a nullable evidenced
// column whose first row drew NULL vanished from the whole kind's load and
// every later row's value was silently dropped — found live by the Twin's
// three-environment crossover rehearsal (2026-08-26). A column absent from
// EVERY row stays omitted: the sink's own DEFAULT applies (the 2026-07-06
// HIGH #3 contract).

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

let private name (s: string) : Name = Name.create s |> Result.value

let private attrOf (key: SsKey) (column: string) (ptype: PrimitiveType) (nullable: bool) (isPk: bool) : Attribute =
    { Attribute.create key (name column) ptype with
        Column       = ColumnRealization.create column nullable |> Result.value
        IsPrimaryKey = isPk }

let private customer : Kind =
    { Kind.create (kindKey ["CSH"]) (name "Customer") (mkTableId "dbo" "Customer")
        [ attrOf (attrKey ["CSH"; "Id"]) "Id" Integer false true
          attrOf (attrKey ["CSH"; "Email"]) "Email" Text true false
          attrOf (attrKey ["CSH"; "Score"]) "Score" Integer true false
          attrOf (attrKey ["CSH"; "SinkOnly"]) "SinkOnly" Text true false ] with
        Modality = [] }

let private row (i: int) (cells: (string * string) list) : StaticRow =
    { Identifier = kindKey [ "CSH"; "ROW"; string i ]
      Values = cells |> List.map (fun (n, v) -> name n, Some v) |> Map.ofList }

[<Fact>]
let ``a column the first row omits is still carried when any later row speaks`` () =
    // Row 1's Email drew NULL (the key is omitted — the σ convention);
    // row 2 carries a value. The column must stay in the load, with the
    // first row's cell an explicit NULL.
    let rows =
        [ row 1 [ "Id", "1"; "Score", "5" ]
          row 2 [ "Id", "2"; "Email", "a@example.test"; "Score", "6" ] ]
    let cells = TransferCellShaping.toCellRows customer Set.empty rows
    let first = List.item 0 cells
    let second = List.item 1 cells
    let emailOf (r: CellValue list) : CellValue option =
        r |> List.tryFind (fun c -> c.Column = "Email")
    match emailOf first, emailOf second with
    | Some e1, Some e2 ->
        Assert.Equal(None, e1.Raw)
        Assert.Equal(Some "a@example.test", e2.Raw)
    | _ -> failwith "the Email column was dropped from the load because the first row drew NULL"

[<Fact>]
let ``a column absent from every row stays omitted so the sink default applies`` () =
    let rows =
        [ row 1 [ "Id", "1"; "Score", "5" ]
          row 2 [ "Id", "2"; "Score", "6" ] ]
    let cells = TransferCellShaping.toCellRows customer Set.empty rows
    for r in cells do
        Assert.True(r |> List.forall (fun (c: CellValue) -> c.Column <> "SinkOnly"))
        Assert.True(r |> List.forall (fun (c: CellValue) -> c.Column <> "Email"))

[<Fact>]
let ``uniform key sets shape identically to the first-row basis`` () =
    // The ingested lane's contract: every row carries every key, so the
    // union changes nothing — one cell per attribute per row, in order.
    let rows =
        [ row 1 [ "Id", "1"; "Email", "a@example.test"; "Score", "5" ]
          row 2 [ "Id", "2"; "Email", "b@example.test"; "Score", "6" ] ]
    let cells = TransferCellShaping.toCellRows customer Set.empty rows
    for r in cells do
        Assert.Equal<string list>(
            [ "Id"; "Email"; "Score" ],
            r |> List.map (fun c -> c.Column))
