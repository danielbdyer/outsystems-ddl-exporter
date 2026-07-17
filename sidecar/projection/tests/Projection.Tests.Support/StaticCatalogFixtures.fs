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

/// One static kind's spec for the multi-kind form (P2-gate measurement,
/// 2026-06-12): the same per-kind shape `staticCatalog` always built,
/// reified so a catalog of N independent static kinds has the SAME single
/// definition site (the fifth hand-rolled instance stays unwritten).
type StaticKindSpec =
    { KindKeyParts : string list
      KindName : string
      PhysicalTable : string
      Attrs : StaticAttrSpec list
      Rows : (string * string list) list }

/// Build a one-module catalog of N static kinds. Key shapes are
/// byte-identical to `staticCatalog`'s per kind (same synthesis recipe).
let staticCatalogOfKinds
    (keyPrefix: string)
    (moduleName: string)
    (kinds: StaticKindSpec list)
    : Catalog =
    let mkKey parts = SsKey.synthesizedComposite keyPrefix parts |> Result.value
    let nmx s = Name.create s |> Result.value
    let buildKind (spec: StaticKindSpec) =
        let attrNames = spec.Attrs |> List.map (fun a -> nmx a.Name)
        let row (tag: string, cells: string list) =
            { Identifier = mkKey (spec.KindKeyParts @ [ "Row"; tag ])
              Values = StaticRow.presentValues (List.zip attrNames cells) }
        let attribute (a: StaticAttrSpec) =
            { Attribute.create (mkKey (spec.KindKeyParts @ [ a.Name ])) (nmx a.Name) a.Type with
                Column = ColumnRealization.create a.Column false |> Result.value
                IsPrimaryKey = a.IsPrimaryKey
                IsMandatory = true }
        { SsKey = mkKey spec.KindKeyParts
          Name = nmx spec.KindName
          Origin = Native
          Modality = [ Static (spec.Rows |> List.map row) ]
          Physical = TableId.create "dbo" spec.PhysicalTable |> Result.value
          Attributes = spec.Attrs |> List.map attribute
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
            Kinds = kinds |> List.map buildKind
            IsActive = true
            ExtendedProperties = [] } ]
        []
    |> Result.value

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
    staticCatalogOfKinds keyPrefix moduleName
        [ { KindKeyParts = kindKeyParts
            KindName = kindName
            PhysicalTable = physicalTable
            Attrs = attrs
            Rows = rows } ]
