module Projection.Tests.JunctionDeferredTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// H-040 — JunctionDeferred ordering mode
// ---------------------------------------------------------------------------

// A junction kind has ≥2 FK references AND ≤2 non-PK attributes.
// We build a small catalog: Author, Book, and Author_Book (junction).
// Author_Book references both Author and Book, with only FK attributes
// (no payload beyond the FK columns) → it qualifies as a junction.

let private synthKey (ns: string) (key: string) : SsKey =
    SsKey.synthesized ns key |> Result.value

let private authorKey   = synthKey "Lib" "Author"
let private bookKey     = synthKey "Lib" "Book"
let private junctionKey = synthKey "Lib" "Author_Book"

let private physical (table: string) : PhysicalRealization =
    TableId.create "dbo" table |> Result.value

let private mkAttr (root: string) (name: string) (isNullable: bool) : Attribute =
    let key = synthKey root name
    { Attribute.create key (Name.create name |> Result.value) PrimitiveType.Integer with
        Column = ColumnRealization.create (name) (isNullable) |> Result.value }

let private mkRef (ownerKey: SsKey) (targetKey: SsKey) (name: string) : Reference =
    let attrKey = synthKey (SsKey.rootOriginal ownerKey) name
    let refKey  = synthKey (SsKey.rootOriginal ownerKey) (name + "_ref")
    { Reference.create refKey (Name.create name |> Result.value) attrKey targetKey with
        ConstraintState = ConstraintState.TrustedConstraint }

let private buildJunctionCatalog () : Catalog =
    let author =
        Kind.create
            authorKey
            (Name.create "Author" |> Result.value)
            (physical "Author")
            [ mkAttr "Lib" "Id" false |> fun a -> { a with IsPrimaryKey = true } ]
    let book =
        Kind.create
            bookKey
            (Name.create "Book" |> Result.value)
            (physical "Book")
            [ mkAttr "Lib" "Id" false |> fun a -> { a with IsPrimaryKey = true } ]
    // Author_Book: 2 FK refs, 2 non-PK attrs (AuthorId + BookId) — qualifies as junction.
    let junctionBase =
        Kind.create
            junctionKey
            (Name.create "Author_Book" |> Result.value)
            (physical "Author_Book")
            [ mkAttr "Lib" "AuthorId" false
              mkAttr "Lib" "BookId"   false ]
    let junction =
        { junctionBase with
            References =
                [ mkRef junctionKey authorKey "AuthorId"
                  mkRef junctionKey bookKey   "BookId" ] }
    mkCatalog
        [ mkModule (synthKey "Lib" "Lib") (Name.create "Lib" |> Result.value)
            [ author; book; junction ] ]

let private deferredConfig : OrderingConfig =
    { SelfLoops = TreatAsCycle; JunctionDeferral = DeferJunctionKinds; Resolution = SchemaMinimal }

[<Fact>]
let ``JunctionDeferred mode: junction kind appears at the end of the order`` () =
    let catalog = buildJunctionCatalog ()
    let result =
        TopologicalOrderPass.runWithConfig deferredConfig catalog
        |> fun l -> l.Value
    Assert.Equal(JunctionDeferred, result.Mode)
    let lastKey = result.Order |> List.last
    Assert.Equal(junctionKey, lastKey)

[<Fact>]
let ``JunctionDeferred mode: non-junction kinds precede the junction`` () =
    let catalog = buildJunctionCatalog ()
    let result =
        TopologicalOrderPass.runWithConfig deferredConfig catalog
        |> fun l -> l.Value
    let junctionIdx = result.Order |> List.findIndex (fun k -> k = junctionKey)
    let authorIdx   = result.Order |> List.findIndex (fun k -> k = authorKey)
    let bookIdx     = result.Order |> List.findIndex (fun k -> k = bookKey)
    Assert.True(authorIdx < junctionIdx, "Author must precede junction")
    Assert.True(bookIdx   < junctionIdx, "Book must precede junction")

[<Fact>]
let ``EmitInTopologicalOrder config produces Topological mode not JunctionDeferred`` () =
    let catalog = buildJunctionCatalog ()
    let config = { SelfLoops = TreatAsCycle; JunctionDeferral = EmitInTopologicalOrder; Resolution = SchemaMinimal }
    let result =
        TopologicalOrderPass.runWithConfig config catalog
        |> fun l -> l.Value
    Assert.Equal(Topological, result.Mode)

[<Fact>]
let ``JunctionDeferred order contains all kinds exactly once`` () =
    let catalog = buildJunctionCatalog ()
    let result =
        TopologicalOrderPass.runWithConfig deferredConfig catalog
        |> fun l -> l.Value
    let allKindKeys =
        Catalog.allKinds catalog |> List.map (fun k -> k.SsKey) |> Set.ofList
    Assert.Equal(allKindKeys.Count, result.Order.Length)
    Assert.Equal<Set<SsKey>>(allKindKeys, Set.ofList result.Order)
