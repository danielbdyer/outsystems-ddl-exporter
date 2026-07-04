module Projection.Tests.OssysRowsetParityInventoryTests

// V1 parity audit — slice 5.1.α (inventory of V1's
// `Osm.Pipeline.SqlExtraction.IOutsystemsMetadataReader.cs` DTOs).
// Each Skip stub reserves the contract name for one row of
// `V1_PARITY_MATRIX.md` (rows 11–29). The Skip rationale carries the
// classification + the trigger / divergence / sunset reference.
// When a future slice cashes out a parity claim, the Skip flips to a
// real assertion and the matrix row gets a Status-history amendment.

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// =====================================================================
// Shared fixture for the two promoted rows below (12, 23) — reuses
// `Fixtures.annotationBearingCatalog` (Wave-1 slice 1.3's fixture,
// already exercised end-to-end against a live target in
// `CanaryRoundTripTests`'s "triggers / checks / sequences / extended
// properties are RECOVERED" test). Its `Widget` kind carries one
// `Trigger` + one `ColumnCheck` — exactly the two axes rows 12 + 23
// promote. Rendering through `SsdtDdlEmitter.statements` here proves
// the pure emission path (no Docker needed); the Integration-suite
// canary proves the full round-trip.
// =====================================================================

let private rowsetInventoryWidgetKind : Kind =
    Catalog.allKinds annotationBearingCatalog
    |> List.find (fun k -> k.Name = mkName "Widget")

let private rowsetInventoryWidgetBody : string =
    SsdtDdlEmitter.statements annotationBearingCatalog |> Render.toText

// =====================================================================
// 🟠 NOT-MAPPED — V2's OssysSql adapter walks but does not parse
// these OSSYS-source rowsets. Each row names a concrete cash-out
// trigger; "we'll get to it" is forbidden by the discipline.
// =====================================================================

[<Fact(Skip = "Matrix row 11 — 🟠 NOT-MAPPED. V1 rowset 6 #ColumnReality (sys.columns reflection against OSSYS-source: SQL type / nullability / identity / computed / default / collation). V2's `Projection.Adapters.Sql.PhysicalSchemaReader` reflects sys.columns against the deployed target, not OSSYS-source. Trigger: V2 tightening or remediation decision demands source-side column reflection independent of deployed state.")>]
let ``5.1.α row 11: OutsystemsColumnRealityRow lifts to MetadataSnapshot.ColumnReality`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 11"

[<Fact>]
let ``5.1.α row 12: OutsystemsColumnCheckRow lifts to Kind.ColumnChecks and renders as a table-scoped CHECK constraint (matrix row 12 cashed out)`` () : unit =
    // The CHECK-constraint axis fired: `ColumnCheck : ColumnCheck list`
    // lives at `Kind.ColumnChecks` (Catalog.fs:1185), carrying name +
    // predicate + IsNotTrusted (the exact V1 #ColumnCheckReality shape
    // this row named as missing). Assert (a) the IR carries it and
    // (b) `SsdtDdlEmitter` (SsdtDdlEmitter.fs:1265, "matrix row 12")
    // projects it into the CREATE TABLE body as a named CONSTRAINT.
    Assert.False(List.isEmpty rowsetInventoryWidgetKind.ColumnChecks)
    let chk = List.exactlyOne rowsetInventoryWidgetKind.ColumnChecks
    Assert.Equal(Some "CK_Widget_Qty", chk.Name |> Option.map Name.value)
    Assert.Contains("CONSTRAINT [CK_Widget_Qty] CHECK", rowsetInventoryWidgetBody)

[<Fact(Skip = "Matrix row 14 — 🟠 NOT-MAPPED. V1 rowset 9 #PhysColsPresent (binary presence flag — distinct AttrIds that exist as physical columns on the OSSYS-source). V2 reconstructs presence on the deployed-target side via `PhysicalSchema.PhysicalRows` set membership; no OSSYS-source presence carrier. Trigger: V2 needs orphan-attribute detection on the OSSYS source (logical-attribute-without-physical-column reporting).")>]
let ``5.1.α row 14: OutsystemsPhysicalColumnPresenceRow lifts to MetadataSnapshot.PhysicalColumnsPresent`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 14"

[<Fact(Skip = "Matrix row 15 — 🟠 NOT-MAPPED. V1 rowset 10 #AllIdx (sys.indexes reflection on OSSYS-source: name / uniqueness / kind / filter / disabled / fillfactor / lock-options / partition / compression). V2's `Catalog.Indexes` IR is populated from V1's IndexJson rowset 21 via `osm_model.json`, not from this structured rowset directly. Trigger: V2 lifts IndexJson consumption to OssysSql to maintain index evidence post-V1-sunset.")>]
let ``5.1.α row 15: OutsystemsIndexRow lifts to MetadataSnapshot.Indexes`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 15"

[<Fact(Skip = "Matrix row 16 — 🟠 NOT-MAPPED. V1 rowset 11 #IdxColsMapped (per-index column membership on OSSYS-source: ordinal / physical column / IsIncluded / direction / human-attr name). V2's `Index.Columns : IndexColumn list` (chapter 4.9 slice γ) is populated from V1's IndexJson, not from this rowset. Trigger: paired with row 15 — V2 lifts the structured rowset path for index reflection.")>]
let ``5.1.α row 16: OutsystemsIndexColumnRow lifts to MetadataSnapshot.IndexColumns`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 16"

[<Fact(Skip = "Matrix row 17 — 🟠 NOT-MAPPED. V1 rowset 12 #FkReality (sys.foreign_keys reflection on OSSYS-source: FK name / delete+update actions / referenced object+entity / IsNoCheck). V2's `Reference.HasDbConstraint` (chapter 4.6 slice α) lifts the HasFK flag but not the full FK shape; V2's `PhysicalSchema.ForeignKeys` reflects sys.foreign_keys on the deployed target, not OSSYS-source. Trigger: V2 reports source-vs-target FK drift OR an OSSYS-source-side FK action (e.g., IsNoCheck flag from source) feeds a tightening decision.")>]
let ``5.1.α row 17: OutsystemsForeignKeyRow lifts to MetadataSnapshot.ForeignKeys`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 17"

[<Fact(Skip = "Matrix row 18 — 🟠 NOT-MAPPED. V1 rowset 13 #FkColumns (per-FK column mapping on OSSYS-source: ordinal / parent + referenced columns + attribute IDs). V2 reconstructs FK column pairs from `PhysicalSchema.ForeignKeys` on the deployed target. Trigger: paired with row 17 — V2 lifts OSSYS-source FK reflection.")>]
let ``5.1.α row 18: OutsystemsForeignKeyColumnRow lifts to MetadataSnapshot.ForeignKeyColumns`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 18"

[<Fact>]
let ``5.1.α row 23: OutsystemsTriggerRow lifts to Kind.Triggers and renders as a CREATE TRIGGER statement (matrix row 23 cashed out)`` () : unit =
    // The trigger axis fired: `Trigger : Trigger list` lives at
    // `Kind.Triggers` (Catalog.fs:1176), carrying name + IsDisabled +
    // the full T-SQL body (the exact V1 #Triggers shape this row named
    // as missing). Assert (a) the IR carries it and (b)
    // `triggerStatements` (SsdtDdlEmitter.fs:561-580) projects it into
    // a `CREATE TRIGGER` statement (plus the metadata comment).
    Assert.False(List.isEmpty rowsetInventoryWidgetKind.Triggers)
    let trg = List.exactlyOne rowsetInventoryWidgetKind.Triggers
    Assert.Equal("TR_Widget_Audit", Name.value trg.Name)
    Assert.False(trg.IsDisabled)
    Assert.Contains("CREATE TRIGGER [dbo].[TR_Widget_Audit]", rowsetInventoryWidgetBody)
    Assert.Contains("Trigger: TR_Widget_Audit (disabled: false)", rowsetInventoryWidgetBody)

// =====================================================================
// 🟡 DIVERGENCE — V2 deliberately diverges from V1; each row references
// a DECISIONS.md entry naming the rationale.
// =====================================================================

[<Fact(Skip = "Matrix row 19 — 🟡 DIVERGENCE. V1 rowset 14 #FkAttrMap materializes `(AttrId, FkObjectId)` lookup pairs for V1's ForeignKeyPass + diagnostic surfaces. V2 reconstructs the same surface algebraically from `Catalog.References` + `PhysicalSchema.ForeignKeys` on-demand; the materialized lookup is not needed at V2's scale. See `DECISIONS 2026-05-17 (slice 5.1.α) — Algebraic-join reconstruction over materialized FK-attribute lookup rowsets`.")>]
let ``5.1.α row 19: OutsystemsForeignKeyAttrMapRow divergence (algebraic-join reconstruction)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 19 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 20 — 🟡 DIVERGENCE. V1 rowset 15 #AttrHasFK materializes the per-attribute boolean `attribute carries any FK` for diagnostic / tightening surfaces. V2 computes it on-demand via `Catalog.References` set membership against `AttrId`. See `DECISIONS 2026-05-17 (slice 5.1.α) — Algebraic-join reconstruction over materialized FK-attribute lookup rowsets`.")>]
let ``5.1.α row 20: OutsystemsAttributeHasFkRow divergence (set-membership reconstruction)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 20 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 29 — 🟡 DIVERGENCE. V1's `OutsystemsMetadataSnapshot` envelope carries `DatabaseName : string` populated from `SqlConnection.Database` at extract time; V1 consumers use it for qualified-name composition + audit trails. V2's `MetadataSnapshot` has no equivalent field — V2 treats database identity as a realization-time concern (emission parameter). See `DECISIONS 2026-05-17 (slice 5.1.α) — Database identity is a realization-time concern, not an IR field`.")>]
let ``5.1.α row 29: OutsystemsMetadataSnapshot.DatabaseName divergence (realization-time concern)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 29 + DECISIONS 2026-05-17 (slice 5.1.α)"

// =====================================================================
// ⚫ V1-SUNSET — V1's `osm_model.json` emission path is V1-internal and
// sunsets with V1 per cutover-30 ladder gates. The 8 JSON-aggregation
// rowsets exist to feed that emission; V2's emission path (Catalog →
// SSDT / Json / Distributions) replaces it. See `DECISIONS 2026-05-17
// (slice 5.1.α) — V1's JSON-aggregation rowsets sunset with V1's
// `osm_model.json` emission path`.
// =====================================================================

[<Fact(Skip = "Matrix row 13 — ⚫ V1-SUNSET. V1 rowset 8 #AttrCheckJson (FOR JSON PATH aggregation of #ColumnCheckReality per attribute). Feeds V1's osm_model.json attribute-level check arrays. V2 doesn't emit osm_model.json; the underlying CHECK evidence is tracked as a separate NOT-MAPPED row (12). See sunset rationale in DECISIONS.")>]
let ``5.1.α row 13: OutsystemsColumnCheckJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 13 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 21 — ⚫ V1-SUNSET. V1 rowset 16 #FkColumnsJson (FOR JSON PATH aggregation of #FkColumns per FkObjectId). Feeds V1's osm_model.json FK-column arrays. V2 reconstructs FK column shapes from `Catalog.References` + `PhysicalSchema.ForeignKeys` at emit time. Sunsets with V1.")>]
let ``5.1.α row 21: OutsystemsForeignKeyColumnsJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 21 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 22 — ⚫ V1-SUNSET. V1 rowset 17 #FkAttrJson (FOR JSON PATH aggregation of #FkReality per attribute). Feeds V1's osm_model.json per-attribute FK constraint arrays. V2 reconstructs at emit time. Sunsets with V1.")>]
let ``5.1.α row 22: OutsystemsForeignKeyAttributeJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 22 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 24 — ⚫ V1-SUNSET. V1 rowset 19 #AttrJson (FOR JSON PATH aggregation of #Attr ⊕ #ColumnReality ⊕ #AttrCheckJson per entity). Feeds V1's osm_model.json entity-level attribute arrays. V2's `Catalog.Modules.*.Attributes` is the structured equivalent built from rowsets 3 directly. Sunsets with V1.")>]
let ``5.1.α row 24: OutsystemsAttributeJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 24 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 25 — ⚫ V1-SUNSET. V1 rowset 20 #RelJson (FOR JSON PATH aggregation of #FkReality per entity). Feeds V1's osm_model.json entity-level relationships array. V2's `Catalog.References` is the structured equivalent. Sunsets with V1.")>]
let ``5.1.α row 25: OutsystemsRelationshipJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 25 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 26 — ⚫ V1-SUNSET. V1 rowset 21 #IdxJson (FOR JSON PATH aggregation of #AllIdx + #IdxColsMapped per entity). Feeds V1's osm_model.json entity-level indexes array. V2's `Catalog.Indexes` IR is the consumer-side equivalent; today's V2 reads IdxJson via `osm_model.json` parsing, so V2 has structural dependence on V1 producing the JSON. Trigger to lift: when V1's emission is decomissioned, V2 lifts row 15 + 16 (the structured rowsets) into OssysSql to replace the JSON path.")>]
let ``5.1.α row 26: OutsystemsIndexJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 26 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 27 — ⚫ V1-SUNSET. V1 rowset 22 #TriggerJson (FOR JSON PATH aggregation of #Triggers per entity). Feeds V1's osm_model.json trigger arrays. V2 has no trigger axis; the underlying trigger evidence is tracked as a separate NOT-MAPPED row (23). Sunsets with V1.")>]
let ``5.1.α row 27: OutsystemsTriggerJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 27 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact(Skip = "Matrix row 28 — ⚫ V1-SUNSET. V1 rowset 23 #ModuleJson (root FOR JSON PATH envelope nesting #AttrJson + #RelJson + #IdxJson + #TriggerJson per entity, grouped by module). Builds V1's osm_model.json document root. V2's `Catalog` IR is the semantic equivalent. Sunsets with V1.")>]
let ``5.1.α row 28: OutsystemsModuleJsonRow sunsets with V1 osm_model.json emission`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 28 + DECISIONS 2026-05-17 (slice 5.1.α)"

// =====================================================================
// Slice anchor — one always-passing test asserts the file's purpose so
// `dotnet test` discovery surfaces the inventory file at every run, not
// just when Skip stubs are flipped.
// =====================================================================

[<Fact>]
let ``5.1.α: inventory file present; 17 Skip stubs reserve matrix rows 11-29 (rows 12 + 23 promoted)`` () : unit =
    // The artifact-as-evidence claim is structural: the matrix doc
    // names rows 11–29 (19 rows); rows 12 + 23 cashed out (CHECK
    // constraints, triggers) and promoted to real assertions above,
    // leaving 17 Skip stubs in this file. The assertion below is
    // intentionally weak — its function is to keep the inventory file
    // visible in test discovery so future slices flipping a Skip stub
    // surface immediately in the run.
    Assert.True(true)
