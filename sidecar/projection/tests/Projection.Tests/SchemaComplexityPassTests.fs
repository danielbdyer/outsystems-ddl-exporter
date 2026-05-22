module Projection.Tests.SchemaComplexityPassTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// H-075 — Schema complexity scoring
// ---------------------------------------------------------------------------

let private synthKey (ns: string) (key: string) : SsKey =
    SsKey.synthesized ns key |> Result.value

let private physical (table: string) : PhysicalRealization =
    { Schema = "dbo"; Table = table; Catalog = None }

let private mkAttr (root: string) (name: string) (isNullable: bool) : Attribute =
    let key = synthKey root name
    { Attribute.create key (Name.create name |> Result.value) PrimitiveType.Integer with
        Column = { ColumnName = name; IsNullable = isNullable } }

let private mkRef (ownerKey: SsKey) (targetKey: SsKey) : Reference =
    let attrKey = synthKey (SsKey.rootOriginal ownerKey) "fk"
    let refKey  = synthKey (SsKey.rootOriginal ownerKey) "fk_ref"
    { Reference.create refKey (Name.create "fk" |> Result.value) attrKey targetKey with
        HasDbConstraint = true }

let private mkSimpleKind (key: SsKey) : Kind =
    Kind.create
        key
        (Name.create (SsKey.rootOriginal key) |> Result.value)
        (physical (SsKey.rootOriginal key))
        [ mkAttr (SsKey.rootOriginal key) "Id" false |> fun a -> { a with IsPrimaryKey = true } ]

[<Fact>]
let ``empty catalog produces zero-score result`` () =
    let result =
        SchemaComplexityPass.run (mkCatalog []) TopologicalOrder.empty
        |> LineageDiagnostics.payload
    Assert.Equal(0, result.CyclomaticComplexity)
    Assert.Equal(0.0m, result.CouplingIndex)
    Assert.Equal(0.0m, result.OverallScore)

[<Fact>]
let ``single-kind catalog with no FKs has zero cyclomatic complexity`` () =
    let kA = synthKey "M" "A"
    let catalog =
        mkCatalog
            [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value) [ mkSimpleKind kA ] ]
    let topo = { TopologicalOrder.empty with Order = [kA]; Edges = [] }
    let result =
        SchemaComplexityPass.run catalog topo
        |> LineageDiagnostics.payload
    Assert.Equal(0, result.CyclomaticComplexity)

[<Fact>]
let ``FK edge count equals cyclomatic complexity`` () =
    let kA = synthKey "M" "A"
    let kB = synthKey "M" "B"
    let kC = synthKey "M" "C"
    let kindA = mkSimpleKind kA
    let kindB =
        { mkSimpleKind kB with
            References = [ mkRef kB kA ] }
    let kindC =
        { mkSimpleKind kC with
            References = [ mkRef kC kB ] }
    let catalog =
        mkCatalog
            [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value) [kindA; kindB; kindC] ]
    let topo = { TopologicalOrder.empty with Order = [kA; kB; kC]; Edges = [(kB, kA); (kC, kB)] }
    let result =
        SchemaComplexityPass.run catalog topo
        |> LineageDiagnostics.payload
    Assert.Equal(2, result.CyclomaticComplexity)

[<Fact>]
let ``overall score is in [0, 1]`` () =
    let kA = synthKey "M" "A"
    let kB = synthKey "M" "B"
    let kindA = mkSimpleKind kA
    let kindB = { mkSimpleKind kB with References = [ mkRef kB kA ] }
    let catalog =
        mkCatalog
            [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value) [kindA; kindB] ]
    let topo = { TopologicalOrder.empty with Order = [kA; kB]; Edges = [(kB, kA)] }
    let result =
        SchemaComplexityPass.run catalog topo
        |> LineageDiagnostics.payload
    Assert.True(result.OverallScore >= 0.0m, "OverallScore must be ≥ 0")
    Assert.True(result.OverallScore <= 1.0m, "OverallScore must be ≤ 1")

[<Fact>]
let ``nullable ratio reflects proportion of nullable attributes`` () =
    let kT = synthKey "M" "T"
    let attrs =
        [ mkAttr "M" "Id"  false |> fun a -> { a with IsPrimaryKey = true }
          mkAttr "M" "Col1" true
          mkAttr "M" "Col2" true
          mkAttr "M" "Col3" false ]
    let k = { mkSimpleKind kT with Attributes = attrs }
    let catalog =
        mkCatalog
            [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value) [k] ]
    let topo = { TopologicalOrder.empty with Order = [kT]; Edges = [] }
    let result =
        SchemaComplexityPass.run catalog topo
        |> LineageDiagnostics.payload
    // 2 of 4 attributes are nullable → ratio = 0.5
    Assert.Equal(0.5m, result.NullabilityRatio)

[<Fact>]
let ``more FK edges produce higher coupling index`` () =
    let kA = synthKey "M" "A"
    let kB = synthKey "M" "B"
    // Zero-edge catalog
    let zeroEdgeCatalog =
        mkCatalog
            [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value)
                [ mkSimpleKind kA; mkSimpleKind kB ] ]
    let zeroTopo = { TopologicalOrder.empty with Order = [kA; kB]; Edges = [] }
    let zeroResult =
        SchemaComplexityPass.run zeroEdgeCatalog zeroTopo
        |> LineageDiagnostics.payload
    // One-edge catalog
    let kindBwithRef = { mkSimpleKind kB with References = [ mkRef kB kA ] }
    let oneEdgeCatalog =
        mkCatalog
            [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value)
                [ mkSimpleKind kA; kindBwithRef ] ]
    let oneTopo = { TopologicalOrder.empty with Order = [kA; kB]; Edges = [(kB, kA)] }
    let oneResult =
        SchemaComplexityPass.run oneEdgeCatalog oneTopo
        |> LineageDiagnostics.payload
    Assert.True(oneResult.CouplingIndex > zeroResult.CouplingIndex,
        "More FK edges → higher coupling index")
