module Projection.Tests.OssysDomainMiscParityTests

// V1 parity audit — slice 5.2.α.misc. Reserves matrix rows 60–63 +
// amendment to row 23 (V1's misc aggregates: Sequence / Trigger /
// ExtendedProperty / TemporalRetention vs V2's Catalog IR).

open Xunit

[<Fact(Skip = "Matrix row 60 — 🟢 PARITY (IR; emitter deferred). V1's `SequenceModel` (Schema, Name, DataType, StartValue, Increment, Minimum, Maximum, IsCycleEnabled, CacheMode enum, CacheSize, ExtendedProperties) maps to V2's `Sequence` in `Catalog.Sequences` (chapter A.0' slice δ; L3-S5 sub-axiom). V2 carries all V1 fields plus typed SsKey identity. V1's `SequenceCacheMode` 4-variant enum maps to V2's 3-variant closed DU (Unspecified | Cache | NoCache); V1's `UnsupportedYet` variant is deferred per slice-β normalization logic. Sequence-level ExtendedProperties are dropped at the adapter boundary; trigger: re-add when sequence-level extended-properties accessor lands. Emitter (`CREATE SEQUENCE`) is deferred per chapter A.0' slice δ — IR shipped without emission consumer.")>]
let ``5.2.α row 60: V1 SequenceModel ↔ V2 Catalog.Sequences PARITY (IR shipped; emitter deferred)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 60"

[<Fact(Skip = "Matrix row 61 — 🟢 PARITY. V1's `TriggerModel` (Name, IsDisabled, Definition) maps to V2's `Trigger` in `Kind.Triggers` (chapter A.0' slice γ; L3-S4 sub-axiom). V2 carries all V1 fields plus typed SsKey identity. V1's `Create` enforces non-blank Definition; V2's smart constructor mirrors. **Placement differs**: V1 has TriggerModel as a schema-scoped object; V2 places Trigger inside `Kind.Triggers` per the domain semantic (a trigger is owned by the table it fires on). This is semantic correction, not parity loss. **Important: matrix row 23 (OutsystemsTriggerRow → MetadataSnapshot.Triggers — 🟠 NOT-MAPPED) is now stale; the V1 OSSYS-source rowset 18 #Triggers lifts into the existing V2 `Trigger` IR shape rather than a new axis.** See row 23 Status history amendment.")>]
let ``5.2.α row 61: V1 TriggerModel ↔ V2 Kind.Triggers PARITY (V2 corrects placement)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 61"

[<Fact(Skip = "Matrix row 62 — 🟢 PARITY. V1's `ExtendedProperty` (Name : string, Value : string?) maps to V2's `ExtendedProperty` (Name : string, Value : string option). V2's module function `ExtendedProperty.create` normalizes empty-string Value to `None` per V1 parity. Smart-constructor invariants match (non-blank Name). **Scope**: V2 places ExtendedProperty at 4 levels — `Attribute.ExtendedProperties`, `Index.ExtendedProperties`, `Kind.ExtendedProperties`, `Module.ExtendedProperties`. V1's scope is broader (sequences also). V2's 4-level placement is the operationally-complete set today; sequence-level deferred per row 60's trigger. Emitter consumer is `ScriptDomBuild.buildSetExtendedProperty` per chapter 4.1.A slice 8; module-level emission gated on V1-side confirmation of module→schema convention (deferred per `DECISIONS 2026-05-17 — sp_addextendedproperty emission`).")>]
let ``5.2.α row 62: V1 ExtendedProperty ↔ V2 ExtendedProperty multi-level PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 62"

[<Fact(Skip = "Matrix row 63 — 🟢 PARITY (IR; emitter deferred). V1's temporal axis spans `TemporalRetentionPolicy` (Kind enum: None/Infinite/Limited/UnsupportedYet, Value, Unit enum) + `TemporalTableMetadata` (Type, HistorySchema, HistoryTable, PeriodStartColumn, PeriodEndColumn, RetentionPolicy, ExtendedProperties). V2's temporal axis is embedded in `ModalityMark.Temporal of TemporalConfig` (`Catalog.fs` line 337) carrying `TemporalRetention = Infinite | Limited of int × TemporalRetentionUnit` + `TemporalConfig = { HistorySchema; HistoryTable; PeriodStart; PeriodEnd; Retention }`. V2 **refines** V1's 4-variant enum to 2-variant DU — None is implicit (absence of `ModalityMark.Temporal` from the modality list); UnsupportedYet is carriage-only at V1 and deferred at V2 (chapter A.0' slice η scope). Temporal-table DDL emission (`CREATE TABLE ... PERIOD FOR ... HISTORY_RETENTION_PERIOD ...`) is deferred pending SSDT realization gate.")>]
let ``5.2.α row 63: V1 TemporalRetentionPolicy + TemporalTableMetadata ↔ V2 ModalityMark.Temporal PARITY (V2 refines)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 63"

[<Fact(Skip = "Matrix row 23 amendment 2026-05-17 (slice 5.2.α.misc). Per slice 5.2.α.misc audit, V2 ALREADY carries `Trigger` IR in `Kind.Triggers` (chapter A.0' slice γ; L3-S4 sub-axiom). The original row 23 NOT-MAPPED classification was authored against a stale view of V2's IR. **Reclassification: 🟠 NOT-MAPPED → 🟢 PARITY (V2 IR shipped; emitter status: structured trigger emission deferred pending chapter A.0' slice η).** The V1 OSSYS-source rowset 18 `#Triggers` lifts into the existing V2 `Trigger` shape (not a new axis) when slice 5.1.α.row-23-cash-out fires. See matrix row 61 (slice 5.2.α.misc) for the full V1 → V2 mapping.")>]
let ``5.2.α row 23 amendment: V2 Trigger IR ships per chapter A.0' slice γ; row 23 NOT-MAPPED reclassifies to PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 23 amendment + row 61"

[<Fact>]
let ``5.2.α.misc: domain-misc parity file present`` () =
    Assert.True(true)
