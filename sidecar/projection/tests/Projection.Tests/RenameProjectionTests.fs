module Projection.Tests.RenameProjectionTests

open Xunit
open Projection.Core
open Projection.Pipeline

// 6.B.2 — pure witnesses for the RefactorLog-aware Transfer re-point. A rename
// changes a column's physical coordinates; the source row carries the OLD name,
// the sink expects the NEW name. The A→B `CatalogDiff` attribute rename gives a
// source-Name → sink-Name re-key that moves each row's values onto the sink's
// names by IDENTITY (A1-stable SsKey), never by ordinal.

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private betweenOk (a: Catalog) (b: Catalog) : CatalogDiff =
    match CatalogDiff.between a b with
    | FsResult.Ok d -> d
    | FsResult.Error e -> failwithf "between: %A" e
let private nm (s: string) : Name = Name.create s |> mustOk
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_RP_KIND" [ s ] |> mustOk
let private aKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_RP_ATTR" [ s ] |> mustOk

let private attr (key: SsKey) (logical: string) (col: string) (isPk: bool) : Attribute =
    { Attribute.create key (nm logical) Text with
        Column = ColumnRealization.create (col) (not isPk) |> Result.value
        IsPrimaryKey = isPk
        IsMandatory = isPk }

/// Customer with a stable Id and an Email attribute whose logical name +
/// physical column are parameterized (so A uses Email/EMAIL and B uses
/// Contact/CONTACT under the SAME attribute SsKey — a rename).
let private custKind (emailName: string) (emailCol: string) : Kind =
    Kind.create (kKey "Customer") (nm "Customer")
        (TableId.create "dbo" "RP_CUSTOMER" |> Result.value)
        [ attr (aKey "Id") "Id" "ID" true
          attr (aKey "Email") emailName emailCol false ]

let private catOf (k: Kind) : Catalog =
    IRBuilders.mkCatalog [ IRBuilders.mkModule (kKey "Mod") (nm "M") [ k ] ]

let private rowOf (vals: (string * string) list) : StaticRow =
    { Identifier = aKey "row"; Values = vals |> List.map (fun (k, v) -> nm k, v) |> Map.ofList }

[<Fact>]
let ``6.B.2: renames extracts the column rename from the A->B diff`` () =
    let a = catOf (custKind "Email" "EMAIL")
    let b = catOf (custKind "Contact" "CONTACT")
    let diff = betweenOk a b
    match RenameProjection.renames diff with
    | [ r ] ->
        Assert.Equal<SsKey>(aKey "Email", r.Attribute)
        Assert.Equal<Name>(nm "Email", r.SourceName)
        Assert.Equal<Name>(nm "Contact", r.SinkName)
    | other -> Assert.Fail(sprintf "expected exactly one column rename, got %A" other)

[<Fact>]
let ``6.B.2: a renamed column is re-pointed by the rename map, not matched by ordinal`` () =
    // Source row has Email + Phone. The rename map (Email → Contact) moves
    // Email's value onto Contact by NAME; Phone is untouched. A positional
    // (ordinal) scheme could not distinguish this — the re-point is by identity.
    let map =
        RenameProjection.renameMap
            [ { Attribute = aKey "Email"; SourceName = nm "Email"; SinkName = nm "Contact" } ]
    let row = rowOf [ "Email", "alice@x"; "Phone", "555" ]
    let out = RenameProjection.repointRow map row
    Assert.Equal("alice@x", out.Values.[nm "Contact"])
    Assert.Equal("555", out.Values.[nm "Phone"])
    Assert.False(out.Values.ContainsKey(nm "Email"))

[<Fact>]
let ``6.B.2: the value follows the name regardless of insertion order (not ordinal)`` () =
    let map =
        RenameProjection.renameMap
            [ { Attribute = aKey "Email"; SourceName = nm "Email"; SinkName = nm "Contact" } ]
    // Same cells, reversed insertion order — the re-point result is identical,
    // because it keys on the name, not the position.
    let a = RenameProjection.repointRow map (rowOf [ "Email", "x"; "Phone", "y" ])
    let b = RenameProjection.repointRow map (rowOf [ "Phone", "y"; "Email", "x" ])
    Assert.Equal<Map<Name, string>>(a.Values, b.Values)
    Assert.Equal("x", a.Values.[nm "Contact"])

[<Fact>]
let ``6.B.2: an empty rename map is identity (a no-rename transfer is byte-identical)`` () =
    let row = rowOf [ "Email", "a"; "Phone", "b" ]
    Assert.Equal<StaticRow>(row, RenameProjection.repointRow Map.empty row)
    Assert.Equal<StaticRow list>([ row ], RenameProjection.repointRows Map.empty [ row ])

[<Fact>]
let ``6.B.2: end-to-end — diff-derived renames re-point a source row onto the sink names`` () =
    // The full first-slice path: derive the rename map from between(A, B) and
    // re-point a source-shaped row onto the sink's names.
    let a = catOf (custKind "Email" "EMAIL")
    let b = catOf (custKind "Contact" "CONTACT")
    let map = RenameProjection.renames (betweenOk a b) |> RenameProjection.renameMap
    let sourceRow = rowOf [ "Id", "1"; "Email", "bob@x" ]
    let sinkRow = RenameProjection.repointRow map sourceRow
    Assert.Equal("bob@x", sinkRow.Values.[nm "Contact"])
    Assert.Equal("1", sinkRow.Values.[nm "Id"])
