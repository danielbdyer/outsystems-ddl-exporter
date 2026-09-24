module Projection.Tests.CatalogRiskTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// recon #17 — the data-risk classification moved out of the CLI `Comparison`
// render module into pure `Core.CatalogRisk`, so the "does this transition touch
// data?" predicates are property-testable here (and reusable by the migrate
// apply-gate) rather than stranded private behind the renderer.

let private attr (nullable: bool) : Attribute =
    { Attribute.create (attrKey [ "Sales"; "Customer"; "X" ]) (mkName "X") Integer with
        Column = ColumnRealization.create "X" nullable |> Result.value }

[<Fact>]
let ``CatalogRisk: attributeRiskCategory maps each data-touching facet to its typed bucket`` () =
    Assert.Equal(RiskCategory.TypeChange,       CatalogRisk.attributeRiskCategory AttributeFacet.DataType)
    Assert.Equal(RiskCategory.Tightening,       CatalogRisk.attributeRiskCategory AttributeFacet.Nullability)
    Assert.Equal(RiskCategory.PrimaryKeyChange, CatalogRisk.attributeRiskCategory AttributeFacet.PrimaryKey)
    Assert.Equal(RiskCategory.IdentityChange,   CatalogRisk.attributeRiskCategory AttributeFacet.Identity)

[<Fact>]
let ``CatalogRisk: a null -> not-null tightening rewrites data; not-null -> null does not (the asymmetry)`` () =
    // Tightening rows that already hold null must fail or be backfilled — a data
    // risk; loosening is always safe. This asymmetry is the whole point of the
    // Nullability predicate (a plain `<>` would flag both directions).
    Assert.True (CatalogRisk.attributeRewritesData (attr true)  (attr false) AttributeFacet.Nullability)
    Assert.False(CatalogRisk.attributeRewritesData (attr false) (attr true)  AttributeFacet.Nullability)
    Assert.False(CatalogRisk.attributeRewritesData (attr false) (attr false) AttributeFacet.Nullability)

[<Fact>]
let ``CatalogRisk: a type / primary-key / identity change always risks data`` () =
    let a = attr false
    Assert.True(CatalogRisk.attributeRewritesData a a AttributeFacet.DataType)
    Assert.True(CatalogRisk.attributeRewritesData a a AttributeFacet.PrimaryKey)
    Assert.True(CatalogRisk.attributeRewritesData a a AttributeFacet.Identity)
