module Twin.Tests.EvidenceDriftTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core

// ---------------------------------------------------------------------------
// The capture-side drift comparison (PROVING_SURFACE_DESIGN §5.2): each
// capture also answers whether the environment's schema still matches the
// trunk head. Coordinate-level, columns joined by logical name — one
// comparator for both renditions.
// ---------------------------------------------------------------------------

let private name (s: string) : Name = Name.create s |> Result.value

let private attrOf (key: SsKey) (column: string) (ptype: PrimitiveType) (nullable: bool) (length: int option) : Attribute =
    { Attribute.create key (name column) ptype with
        Column = ColumnRealization.create column nullable |> Result.value
        Length = length }

let private kindOf (tag: string) (attrs: Attribute list) : Kind =
    { Kind.create (kindKey [tag]) (name "Customer") (mkTableId "dbo" "Customer") attrs with
        Modality = [] }

let private trunkCustomer : Kind =
    kindOf "DT"
        [ attrOf (attrKey ["DT"; "Id"]) "Id" Integer false None
          attrOf (attrKey ["DT"; "Email"]) "Email" Text true (Some 250)
          attrOf (attrKey ["DT"; "Score"]) "Score" Integer true None ]

let private trunkIndex =
    CatalogIndex.ofCatalog
        (Catalog.create [ mkModule (modKey "DR") (name "DR") [ trunkCustomer ] ] [] |> Result.value)

let private sectionFor (sourceKind: Kind) : DriftSection =
    EvidenceDrift.compare trunkIndex "qa" [ "dbo.Customer", sourceKind ]

[<Fact>]
let ``a matching environment yields no drift`` () =
    let section = sectionFor trunkCustomer
    Assert.Equal("qa", section.Source)
    Assert.Empty section.Entries

[<Fact>]
let ``a column the trunk does not carry is drift, named to its column`` () =
    let source =
        kindOf "DS"
            [ attrOf (attrKey ["DS"; "Id"]) "Id" Integer false None
              attrOf (attrKey ["DS"; "Email"]) "Email" Text true (Some 250)
              attrOf (attrKey ["DS"; "Score"]) "Score" Integer true None
              attrOf (attrKey ["DS"; "LegacyCode"]) "LegacyCode" Text true (Some 50) ]
    let entry = List.exactlyOne (sectionFor source).Entries
    Assert.Equal("columnNotInTrunk", entry.Kind)
    Assert.Equal(Some "LegacyCode", entry.Column)
    Assert.Contains("(50)", entry.Detail)

[<Fact>]
let ``a trunk column the environment lacks is drift the other way`` () =
    let source =
        kindOf "DS"
            [ attrOf (attrKey ["DS"; "Id"]) "Id" Integer false None
              attrOf (attrKey ["DS"; "Email"]) "Email" Text true (Some 250) ]
    let entry = List.exactlyOne (sectionFor source).Entries
    Assert.Equal("columnMissingInSource", entry.Kind)
    Assert.Equal(Some "Score", entry.Column)

[<Fact>]
let ``nullability and declared-type differences are separate named entries`` () =
    let source =
        kindOf "DS"
            [ attrOf (attrKey ["DS"; "Id"]) "Id" Integer false None
              // NOT NULL where the trunk says NULL, and narrower.
              attrOf (attrKey ["DS"; "Email"]) "Email" Text false (Some 100)
              attrOf (attrKey ["DS"; "Score"]) "Score" Integer true None ]
    let kinds = (sectionFor source).Entries |> List.map (fun e -> e.Kind) |> List.sort
    Assert.Equal<string list>([ "nullabilityDiffers"; "typeDiffers" ], kinds)

[<Fact>]
let ``a table the trunk does not carry is drift at the table grain`` () =
    let section = EvidenceDrift.compare trunkIndex "uat" [ "dbo.Legacy", trunkCustomer ]
    let entry = List.exactlyOne section.Entries
    Assert.Equal("tableNotInTrunk", entry.Kind)
    Assert.Equal(None, entry.Column)

[<Fact>]
let ``the report serializes its kinds and counts`` () =
    let source =
        kindOf "DS"
            [ attrOf (attrKey ["DS"; "Id"]) "Id" Integer false None
              attrOf (attrKey ["DS"; "Email"]) "Email" Text true (Some 250)
              attrOf (attrKey ["DS"; "Score"]) "Score" Integer true None
              attrOf (attrKey ["DS"; "LegacyCode"]) "LegacyCode" Text true (Some 50) ]
    let report = { Trunk = "ok"; Sections = [ sectionFor source ] }
    Assert.Equal(1, EvidenceDrift.entryCount report)
    let json = EvidenceDrift.serializeReport report
    Assert.Contains("columnNotInTrunk", json)
    Assert.Contains("LegacyCode", json)
    Assert.Contains("\"trunk\": \"ok\"", json)
