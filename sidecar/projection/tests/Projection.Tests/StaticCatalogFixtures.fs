module Projection.Tests.StaticCatalogFixtures

open Projection.Core

// CONSTELLATION_BACKLOG card F6 (plane N7) — the ONE static-fixture
// catalog builder. The same hand-rolled one-module / one-kind /
// Static-population `Catalog.create` chain had shipped four times
// (`staticSeedCatalog` + `wideSeedCatalog` in PerfHarnessScenarios, the
// two AC-X1 builds in MigrationCanaryTests); this module is their single
// definition site, so the fifth instance never gets written and fixture
// determinism has one home. `meshModel` is a cousin (FK mesh, no static
// rows) and deliberately stays separate.
//
// Determinism contract: SsKeys synthesize as
//   module    = [ "Mod" ]
//   kind      = kindKeyParts
//   attribute = kindKeyParts @ [ attr.Name ]
//   row       = kindKeyParts @ [ "Row"; rowTag ]
// under the instance's keyPrefix — byte-identical to every absorbed
// instance (the move's witness is the existing suites staying green).

type StaticAttrSpec =
    { Name : string
      Column : string
      Type : PrimitiveType
      IsPrimaryKey : bool }

let attr (name: string) (column: string) (ty: PrimitiveType) : StaticAttrSpec =
    { Name = name; Column = column; Type = ty; IsPrimaryKey = false }

let pk (name: string) (column: string) (ty: PrimitiveType) : StaticAttrSpec =
    { attr name column ty with IsPrimaryKey = true }

/// Build the shared static-population catalog shape. `rows` are
/// `(rowTag, cells-in-attribute-order)`; every attribute is mandatory
/// (the shared shape of all absorbed instances — a non-mandatory or
/// FK-bearing fixture is `meshModel` territory, not this builder's).
let staticCatalog
    (keyPrefix: string)
    (moduleName: string)
    (kindKeyParts: string list)
    (kindName: string)
    (physicalTable: string)
    (attrs: StaticAttrSpec list)
    (rows: (string * string list) list)
    : Catalog =
    let mkKey parts = SsKey.synthesizedComposite keyPrefix parts |> Result.value
    let nmx s = Name.create s |> Result.value
    let attrNames = attrs |> List.map (fun a -> nmx a.Name)
    let row (tag: string, cells: string list) =
        { Identifier = mkKey (kindKeyParts @ [ "Row"; tag ])
          Values = List.zip attrNames cells |> Map.ofList }
    let attribute (a: StaticAttrSpec) =
        { Attribute.create (mkKey (kindKeyParts @ [ a.Name ])) (nmx a.Name) a.Type with
            Column = ColumnRealization.create a.Column false |> Result.value
            IsPrimaryKey = a.IsPrimaryKey
            IsMandatory = true }
    let kind =
        { SsKey = mkKey kindKeyParts
          Name = nmx kindName
          Origin = Native
          Modality = [ Static (rows |> List.map row) ]
          Physical = TableId.create "dbo" physicalTable |> Result.value
          Attributes = attrs |> List.map attribute
          References = []
          Indexes = []
          Description = None
          IsActive = true
          Triggers = []
          ColumnChecks = []
          ExtendedProperties = [] }
    Catalog.create
        [ { SsKey = mkKey [ "Mod" ]
            Name = nmx moduleName
            Kinds = [ kind ]
            IsActive = true
            ExtendedProperties = [] } ]
        []
    |> Result.value
