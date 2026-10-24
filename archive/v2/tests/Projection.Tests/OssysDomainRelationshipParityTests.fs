module Projection.Tests.OssysDomainRelationshipParityTests

// V1 parity audit — slice 5.2.α.relationship. Reserves matrix rows
// 57–59 (V1's three-type relationship/FK split vs V2's single Reference).

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------
// Shared fixture for rows 58 + 59 below — a minimal two-kind catalog
// (A ← B, B.AId references A.Id) mirroring the
// `SsdtDdlEmitterTests.fs` "Slice 5.13.fk-features-emit" fixture shape
// (which exercises the same two axes exhaustively via property + example
// tests). Parametrized on `OnUpdate` + `ConstraintState` so each row
// builds the catalog carrying only the axis it promotes.
// ---------------------------------------------------------------------

let private relFeaturesAKey = kindKey ["RelA"; "AKind"]
let private relFeaturesBKey = kindKey ["RelB"; "BKind"]
let private relFeaturesAIdAttr = attrKey ["RelA"; "AKind"; "Id"]
let private relFeaturesBIdAttr = attrKey ["RelB"; "BKind"; "Id"]
let private relFeaturesBFkAttr = attrKey ["RelB"; "BKind"; "AId"]
let private relFeaturesRefKey = refKey ["RelB"; "BKind"; "AId"]

let private relFeaturesAKind : Kind =
    { Kind.create
        relFeaturesAKey
        (mkName "AKind")
        (mkTableId "dbo" "OSUSR_RA_AKIND")
        [ { Attribute.create relFeaturesAIdAttr (mkName "Id") Integer with
                Column = ColumnRealization.create "ID" false |> Result.value
                IsPrimaryKey = true
                IsMandatory  = true } ]
      with References = []; Indexes = [] }

let private relFeaturesBKind (onUpdate: ReferenceAction option) (constraintState: ConstraintState) : Kind =
    let ref =
        { Reference.create relFeaturesRefKey (mkName "FkToA") relFeaturesBFkAttr relFeaturesAKey with
            OnDelete        = Cascade
            OnUpdate        = onUpdate
            ConstraintState = constraintState }
    { Kind.create
        relFeaturesBKey
        (mkName "BKind")
        (mkTableId "dbo" "OSUSR_RB_BKIND")
        [ { Attribute.create relFeaturesBIdAttr (mkName "Id") Integer with
                Column = ColumnRealization.create "ID" false |> Result.value
                IsPrimaryKey = true
                IsMandatory  = true }
          { Attribute.create relFeaturesBFkAttr (mkName "AId") Integer with
                Column = ColumnRealization.create "A_ID" false |> Result.value
                IsMandatory = true } ]
      with References = [ ref ]; Indexes = [] }

let private relFeaturesCatalog (onUpdate: ReferenceAction option) (constraintState: ConstraintState) : Catalog =
    mkCatalog
        [ mkModule (modKey "RelA") (mkName "RelA") [ relFeaturesAKind ]
          mkModule (modKey "RelB") (mkName "RelB") [ relFeaturesBKind onUpdate constraintState ] ]

let private relFeaturesBody (catalog: Catalog) : string =
    SsdtDdlEmitter.statements catalog |> Render.toText

[<Fact(Skip = "Matrix row 57 — 🟡 DIVERGENCE. V1 separates the FK axis across 3 types: `RelationshipModel` (the **logical** edge — via-attribute-to-entity, DeleteRuleCode, HasDatabaseConstraint), `ForeignKeyModel` (the **physical** constraint — name, DeleteRule, UpdateRule), `RelationshipActualConstraint` (the **reconciliation** — bridging logical to physical with per-column mapping + per-action NOCHECK state). V2 **conflates** all three into a single `Reference` record (SourceAttribute: SsKey, TargetKind: SsKey, OnDelete: ReferenceAction, HasDbConstraint: bool, RefEntityId: int option). V2's conflation flows from the chapter 4.6 design that lifted `HasDbConstraint` directly onto Reference (closing the logical/physical distinction at the IR layer). See `DECISIONS 2026-05-18 (slice 5.2.α.relationship) — V1 three-type relationship/FK split conflates into V2 single Reference`. Re-open trigger: V2 needs to round-trip FK constraint **names** (not just shapes) — currently V2 generates FK names at emit time via convention; if a deployed target has operator-supplied FK names that V2 must preserve, the conflation breaks down.")>]
let ``5.2.α row 57: V1 logical Relationship + physical ForeignKey + RelationshipActualConstraint split conflates into V2 Reference`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 57 + DECISIONS 2026-05-18 (slice 5.2.α.relationship)"

[<Fact>]
let ``5.2.α row 58: V1 UpdateAction on ForeignKeyModel lifts to V2 Reference.OnUpdate and renders ON UPDATE (matrix row 58 cashed out)`` () : unit =
    // The axis fired: `Reference.OnUpdate : ReferenceAction option`
    // (Catalog.fs:855) carries the paired update action V1's
    // `ForeignKeyModel.UpdateRule` named as missing. Assert (a) the IR
    // carries a `Some` action distinct from the default `None`, and
    // (b) the SSDT emitter renders the explicit ON UPDATE clause
    // beside the ON DELETE clause.
    let catalog = relFeaturesCatalog (Some SetNull) ConstraintState.TrustedConstraint
    let ref' =
        (Catalog.allKinds catalog |> List.find (fun k -> k.SsKey = relFeaturesBKey)).References
        |> List.exactlyOne
    Assert.Equal(Some SetNull, ref'.OnUpdate)
    let body = relFeaturesBody catalog
    Assert.Contains("ON DELETE CASCADE", body)
    Assert.Contains("ON UPDATE SET NULL", body)

[<Fact>]
let ``5.2.α row 59: V1 per-constraint NOCHECK state lifts to V2 Reference.ConstraintState and renders WITH NOCHECK (matrix row 59 cashed out)`` () : unit =
    // The axis fired — stronger than the row's proposed
    // `IsConstraintTrusted : bool` cash-out shape: `Reference
    // .ConstraintState` (Catalog.fs:861-871) is a closed 3-case DU
    // (`NoDbConstraint | TrustedConstraint | UntrustedConstraint`)
    // that makes the "untrusted without a constraint" quadrant
    // unrepresentable, with `hasDbConstraint` / `isConstraintTrusted`
    // as the boolean-projection escape hatches V1 parity sites read.
    // Assert the untrusted variant (a) carries through the IR and
    // (b) fires the post-CREATE-TABLE `WITH NOCHECK` alter.
    let untrustedCatalog = relFeaturesCatalog None ConstraintState.UntrustedConstraint
    let untrustedRef =
        (Catalog.allKinds untrustedCatalog |> List.find (fun k -> k.SsKey = relFeaturesBKey)).References
        |> List.exactlyOne
    Assert.Equal(ConstraintState.UntrustedConstraint, untrustedRef.ConstraintState)
    Assert.True(ConstraintState.hasDbConstraint untrustedRef.ConstraintState)
    Assert.False(ConstraintState.isConstraintTrusted untrustedRef.ConstraintState)
    let untrustedBody = relFeaturesBody untrustedCatalog
    Assert.Contains("ALTER TABLE [dbo].[OSUSR_RB_BKIND] WITH NOCHECK CHECK CONSTRAINT", untrustedBody)

    // And the trusted variant (the V1 default) emits no such alter —
    // proving the axis actually distinguishes the two states rather
    // than always firing.
    let trustedCatalog = relFeaturesCatalog None ConstraintState.TrustedConstraint
    let trustedBody = relFeaturesBody trustedCatalog
    Assert.DoesNotContain("WITH NOCHECK", trustedBody)

[<Fact>]
let ``5.2.α.relationship: domain-relationship parity file present`` () =
    Assert.True(true)
