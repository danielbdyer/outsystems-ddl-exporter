module Projection.Tests.OssysDomainRelationshipParityTests

// V1 parity audit — slice 5.2.α.relationship. Reserves matrix rows
// 57–59 (V1's three-type relationship/FK split vs V2's single Reference).

open Xunit

[<Fact(Skip = "Matrix row 57 — 🟡 DIVERGENCE. V1 separates the FK axis across 3 types: `RelationshipModel` (the **logical** edge — via-attribute-to-entity, DeleteRuleCode, HasDatabaseConstraint), `ForeignKeyModel` (the **physical** constraint — name, DeleteRule, UpdateRule), `RelationshipActualConstraint` (the **reconciliation** — bridging logical to physical with per-column mapping + per-action NOCHECK state). V2 **conflates** all three into a single `Reference` record (SourceAttribute: SsKey, TargetKind: SsKey, OnDelete: ReferenceAction, HasDbConstraint: bool, RefEntityId: int option). V2's conflation flows from the chapter 4.6 design that lifted `HasDbConstraint` directly onto Reference (closing the logical/physical distinction at the IR layer). See `DECISIONS 2026-05-18 (slice 5.2.α.relationship) — V1 three-type relationship/FK split conflates into V2 single Reference`. Re-open trigger: V2 needs to round-trip FK constraint **names** (not just shapes) — currently V2 generates FK names at emit time via convention; if a deployed target has operator-supplied FK names that V2 must preserve, the conflation breaks down.")>]
let ``5.2.α row 57: V1 logical Relationship + physical ForeignKey + RelationshipActualConstraint split conflates into V2 Reference`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 57 + DECISIONS 2026-05-18 (slice 5.2.α.relationship)"

[<Fact(Skip = "Matrix row 58 — 🟠 NOT-MAPPED. V1's `ForeignKeyModel` carries paired delete + update actions (`DeleteRule : string`, `UpdateRule : string`). V2's `Reference.OnDelete : ReferenceAction` carries only the delete action; UpdateAction is dropped at the adapter boundary. V2 doesn't emit ON UPDATE clauses today. Trigger: V2's SSDT emission must support ON UPDATE referential actions (e.g., a deployed target has ON UPDATE CASCADE that V2 must round-trip; or V2's emission needs to set ON UPDATE NO ACTION explicitly per modern T-SQL conventions). Cash-out shape: extend `Reference` with `OnUpdate : ReferenceAction option` (defaults `None` → ON UPDATE NO ACTION); adapter pickup at OssysSql ForeignKeys rowset (paired with matrix row 17); emitter consumption at `ScriptDomBuild.buildForeignKey` (set `ForeignKeyConstraintDefinition.UpdateAction = ...`).")>]
let ``5.2.α row 58: V1 UpdateAction on ForeignKeyModel lifts to V2 Reference.OnUpdate`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 58"

[<Fact(Skip = "Matrix row 59 — 🟠 NOT-MAPPED. V1's `RelationshipActualConstraint` distinguishes per-constraint NOCHECK state — empty `OnDeleteAction` / `OnUpdateAction` strings signal that the FK constraint exists but is not enforced (the WITH NOCHECK clause was applied at constraint creation). V2's `Reference.HasDbConstraint : bool` is **binary** — captures presence/absence of the FK constraint but not its enforcement state. Trigger: a deployed target carries WITH NOCHECK FK constraints that V2 must round-trip (rare; usually a remediation-time concern when adding FKs to existing data without forced validation). Cash-out shape: extend `Reference` with `IsConstraintTrusted : bool` (defaults `true`); adapter pickup at OssysSql `#FkReality` rowset's `IsNoCheck` column (paired with matrix row 17); emitter consumption at `ScriptDomBuild.buildForeignKey` (emit WITH NOCHECK when `IsConstraintTrusted = false`).")>]
let ``5.2.α row 59: V1 NOCHECK per-constraint state vs V2 binary HasDbConstraint`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 59"

[<Fact>]
let ``5.2.α.relationship: domain-relationship parity file present`` () =
    Assert.True(true)
