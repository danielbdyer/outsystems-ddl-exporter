# V1_PARITY_MATRIX — formal representational coverage of V1 capabilities in V2

Opened **2026-05-17** at chapter 5.0 close per principal-PO direction:

> "I'd like to start heavily auditing the v1 codebase, step by step, to ensure there is maximal parity — there's a ton of code paths in V1 and I want to make sure the representational coverage-state of the parity is expressible in a formal way in the next agent's discipline."

This document is the canonical record of V1 capabilities and their V2 representation status. **Append-only at the row level; status updates by amendment.** Each row names one V1 capability and tracks its V2-side parity status through one of six classifications.

The matrix is the discipline's substrate. **Each parity-audit slice opens a small bounded scope (typically one V1 file or one V1 capability cluster); the slice closes with a matrix update + a test that exercises the parity claim.** Rows accumulate; coverage compounds.

---

## Classifications (the six row statuses)

Every row carries exactly one classification. Status transitions are amendments — append a new dated entry naming the prior status + the new evidence.

| Status | Meaning | Acceptance criterion |
|---|---|---|
| **🟢 PARITY** | V2 produces equivalent output for the V1 capability against shared inputs. | A passing test (unit or canary) asserts equivalence on a representative input. |
| **🔵 V2-EXTENSION** | V2 carries the capability AND adds structural strength V1 lacks (type-safety, invariants, additional axis). | The extension is documented; the V1-equivalent shape still produces equivalent output. |
| **🟡 DIVERGENCE** | V2 deliberately diverges from V1 — V1 has a behavior V2 does not replicate (intentional, principled). | The divergence is documented at the call site + an entry in DECISIONS.md naming the rationale. |
| **🟠 NOT-MAPPED** | V2 does not yet carry the V1 capability. The audit identified the capability but no V2 path exists. | The trigger-to-cash-out is named (consumer demand / cutover-blocker / etc.). |
| **🔴 V1-BUG-CORRECTED** | V2 implements the capability AND fixes a V1 bug or unsafety. | The V1 bug is referenced; the V2 correction is justified; a test exercises the corrected behavior. |
| **⚫ V1-SUNSET** | The V1 capability is not carried forward by intent — V2 sunsets it. | Sunset rationale documented; downstream consumers either unaffected or migrated. |

---

## How a parity-audit slice works

Per chapter 5.1+ discipline:

1. **Scope.** Pick ONE small V1 capability (typically one C# file, one method, or one tightly-related cluster). The scope size is measured in "audit-able in one session arc" — typically 50–500 LOC of V1 code.
2. **V1 trace.** Read the V1 source. Understand what it does. Trace its inputs / outputs / invariants. Name any V1 bugs you encounter.
3. **V2 inventory.** Find V2's representation (or absence). Trace V2's equivalent code path. Identify the delta.
4. **Classification.** Assign one of the six statuses. Justify in 1–3 sentences.
5. **Coverage test.** Add or identify a test that exercises the parity claim. For 🟢 PARITY: equivalence test. For 🔵 V2-EXTENSION: V1-equivalent test + extension test. For 🟡 DIVERGENCE: V2-divergent behavior test + DECISIONS row. For 🟠 NOT-MAPPED: Skip-stubbed test reserving the contract name. For 🔴 V1-BUG-CORRECTED: regression test exercising the fix. For ⚫ V1-SUNSET: Skip stub naming the sunset rationale.
6. **Matrix update.** Append a row to the matrix table below. If updating an existing row, append a new dated entry under "Status history" (do NOT modify the row in place — append-only).
7. **Slice close.** Single commit per slice. Commit message names the V1 source path + the V2 representation + the classification + the test added.

**Cadence.** One slice per agent session arc (during the parity-audit wave). Don't bundle multiple slices into one commit — the matrix's value is its append-only narrative of independent coverage events.

---

## Parity claim taxonomy (the verifiable equivalence shapes)

Different V1 capabilities have different shapes of "equivalent output." Per slice, name the shape:

- **Byte-equivalence.** V2 and V1 produce byte-identical output (e.g., SQL text, JSON serialization). Verified via `Assert.Equal<byte[]>` or `Assert.Equal<string>`.
- **Structural equivalence.** V2 and V1 produce structurally equivalent values that may differ in representation (e.g., V2's typed DU vs V1's enum). Verified via a structural comparator.
- **Behavioral equivalence.** V2 and V1 produce equivalent observable effects (e.g., same SQL Server schema deployed). Verified via post-condition assertion (e.g., PhysicalSchema diff).
- **Diagnostic equivalence.** V2 and V1 surface equivalent diagnostic events (e.g., same warning codes for the same input). Verified via Diagnostics-trail assertion.
- **Closure equivalence.** V2 and V1, given the same input, produce outputs that round-trip through the same operations identically. Verified via property test (e.g., emit → deploy → readback ≈ original).

---

## Matrix

| Row | V1 source | V2 representation | Status | Coverage test | Notes |
|---|---|---|---|---|---|
| 1 | `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1184 LOC; 22 result sets) | `Resources/outsystems_metadata_rowsets.sql` (embedded resource) | 🟢 PARITY | `MetadataExtractionSqlTests` (byte-equivalence; gated parity test) | Chapter 5.0 slice α. SQL is carbon-copied verbatim; byte-equivalence enforced when V1 trunk is present. |
| 2 | `tests/Fixtures/sql/model.edge-case.seed.sql` (V1 OSSYS bootstrap) | `Resources/ossys-edge-case.seed.sql` | 🟡 DIVERGENCE | `MetadataExtractionSqlTests`; `OssysExtractionCanaryTests` | Chapter 5.0 slice β. V2 strips V1's self-managed DB + filegroup + partition function/scheme + `IGNORE_DUP_KEY = ON` on filtered index (modern SQL Server rejects). Divergences documented inline. |
| 3 | `src/Osm.Pipeline/SqlExtraction/MetadataSnapshotRunner.cs` (~400 LOC; orchestration) | `MetadataSnapshotRunner.fs` (F# rewrite) | 🟢 PARITY (for the 4 V2-consumed rowsets) | `OssysExtractionCanaryTests` (behavioral equivalence) | Chapter 5.0 slice γ. F# rewrite at copy-time per chapter open Q1. Walks all 22 result sets; parses first 5. |
| 4 | `src/Osm.Pipeline/SqlExtraction/ModulesResultSetProcessor.cs` (6-column DTO) | `MetadataSnapshotRunner.OssysModuleRow` + `mapModuleRow` | 🟢 PARITY | `OssysExtractionCanaryTests` | Column ordering inherited from V1's processor. |
| 5 | `src/Osm.Pipeline/SqlExtraction/EntitiesResultSetProcessor.cs` (11-column DTO) | `MetadataSnapshotRunner.OssysEntityRow` + `mapEntityRow` | 🟢 PARITY | `OssysExtractionCanaryTests` | |
| 6 | `src/Osm.Pipeline/SqlExtraction/AttributesResultSetProcessor.cs` (23-column DTO) | `MetadataSnapshotRunner.OssysAttributeRow` + `mapAttributeRow` | 🟢 PARITY (partial; consumes 17 of 23 columns) | `OssysExtractionCanaryTests` | V2 maps the columns its `RowsetBundle.AttributeRow` consumes; the unused (DefaultValue / DatabaseColumnName / LegacyType / Decimals / OriginalType) are accessible-but-skipped. Lift trigger: V2 IR consumer surfaces. |
| 7 | `src/Osm.Pipeline/SqlExtraction/ReferencesResultSetProcessor.cs` (4-column DTO; SQL emits 5) | `MetadataSnapshotRunner.OssysReferenceRow` + `mapReferenceRow` | 🟢 PARITY (consumes 4 of 5 columns) | `OssysExtractionCanaryTests` | The 5th column (`RefEntityIsActive`) is in the SQL but V1's processor also skips it. |
| 8 | `src/Osm.Pipeline/SqlExtraction/PhysicalTablesResultSetProcessor.cs` (4-column DTO) | `MetadataSnapshotRunner.OssysPhysicalTableRow` + `mapPhysicalTableRow` | 🟢 PARITY | `OssysExtractionCanaryTests` | |
| 9 | V1's `parseReferenceRowFor` analog uses `RefEntityName` to construct target key (always synthesized) | `parseReferenceRowFor` resolves via `RefEntityId` → global `Map<int, SsKey>` lookup | 🔴 V1-BUG-CORRECTED | `OssysExtractionCanaryTests` | Chapter 5.0 slice δ. V1's lookup-by-name shape silently misjoined across modules with same entity name; V2 resolves by ID. Latent bug pre-V2; fix shipped at chapter 5.0. |
| 10 | V1's full `parsePrimitiveType` (covers all OutSystems DataTypes) | `parsePrimitiveType` (covers Identifier / Integer / LongInteger / Text / Email / PhoneNumber / Boolean / DateTime / Date / Time / Decimal / Currency / BinaryData) | 🟠 NOT-MAPPED (partial) | `OriginalNameAndExternalDbTypeLiftTests`; `OssysExtractionCanaryTests` | V2 covers the common types. Trigger: parity audit identifies untested type via V1 source walk. |
| 11 | `OutsystemsColumnRealityRow` (rowset 6 `#ColumnReality`; sys.columns reflection on OSSYS-source: SQL type / nullability / identity / computed / default / collation) | (not lifted; runner walks + skips) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 11`` ` (Skip) | Slice 5.1.α. V2's `Projection.Adapters.Sql.PhysicalSchemaReader` reflects sys.columns against the **deployed target**, not OSSYS-source. Trigger: V2 tightening / remediation decision demands source-side column reflection independent of deployed state. |
| 12 | `OutsystemsColumnCheckRow` (rowset 7 `#ColumnCheckReality`; CHECK constraint reflection on OSSYS-source: name / predicate / `IsNotTrusted`) | (not lifted; runner walks + skips) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 12`` ` (Skip) | Slice 5.1.α. V2's IR carries no CHECK-constraint axis. Trigger: V2 IR refinement adds a CHECK-constraint field AND a downstream emitter (SSDT or DACPAC) demands it. |
| 13 | `OutsystemsColumnCheckJsonRow` (rowset 8 `#AttrCheckJson`; FOR JSON PATH aggregation of CHECK constraints per attribute) | (not lifted; V1-internal JSON aggregation) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 13`` ` (Skip) | Slice 5.1.α. Feeds V1's `osm_model.json` emission, which V2's structured Catalog → SSDT/Json path replaces. See `DECISIONS 2026-05-17 (slice 5.1.α)`. Underlying CHECK evidence tracked separately at row 12. |
| 14 | `OutsystemsPhysicalColumnPresenceRow` (rowset 9 `#PhysColsPresent`; distinct AttrIds present as physical columns) | (not lifted; runner walks + skips) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 14`` ` (Skip) | Slice 5.1.α. V2 reconstructs presence on the deployed-target side via `PhysicalSchema.PhysicalRows` membership. Trigger: V2 reports OSSYS-source orphan attributes (logical-attribute-without-physical-column). |
| 15 | `OutsystemsIndexRow` (rowset 10 `#AllIdx`; sys.indexes reflection on OSSYS-source: name / uniqueness / kind / filter / disabled / fillfactor / lock + partition + compression metadata) | (not lifted; V2 currently sources Indexes via V1's IndexJson through `osm_model.json` parsing) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 15`` ` (Skip) | Slice 5.1.α. V2's `Catalog.Indexes` IR is fed by V1's IndexJson rowset (row 26 / ⚫ V1-SUNSET). Trigger: V2 lifts structured rowset 10 to OssysSql to replace the JSON-dependent path when V1 emission decomissions. |
| 16 | `OutsystemsIndexColumnRow` (rowset 11 `#IdxColsMapped`; per-index column ordinal + IsIncluded + direction + human-attr name) | (not lifted; sourced via IndexJson today) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 16`` ` (Skip) | Slice 5.1.α. Paired with row 15. V2's `Index.Columns : IndexColumn list` (chapter 4.9 slice γ) consumes from IndexJson currently. |
| 17 | `OutsystemsForeignKeyRow` (rowset 12 `#FkReality`; sys.foreign_keys reflection on OSSYS-source: name / actions / referenced object + entity / IsNoCheck) | partial — `Reference.HasDbConstraint` lifted (chapter 4.6 slice α) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 17`` ` (Skip) | Slice 5.1.α. V2's `PhysicalSchema.ForeignKeys` reflects sys.foreign_keys on the **deployed target**. Trigger: V2 reports source-vs-target FK drift OR an OSSYS-source-side FK action (e.g., IsNoCheck on source) feeds a tightening decision. |
| 18 | `OutsystemsForeignKeyColumnRow` (rowset 13 `#FkColumns`; per-FK column pairs + attribute IDs) | (not lifted; deployed-target side via `PhysicalForeignKey.Columns`) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 18`` ` (Skip) | Slice 5.1.α. Paired with row 17. |
| 19 | `OutsystemsForeignKeyAttrMapRow` (rowset 14 `#FkAttrMap`; materialized `(AttrId, FkObjectId)` lookup table) | reconstructed on-demand via `Catalog.References` filter | 🟡 DIVERGENCE | `OssysRowsetParityInventoryTests.``5.1.α row 19`` ` (Skip) | Slice 5.1.α. V2 chooses algebraic-join reconstruction over materialized lookup at ≤300-table scale. See `DECISIONS 2026-05-17 (slice 5.1.α) — Algebraic-join reconstruction`. |
| 20 | `OutsystemsAttributeHasFkRow` (rowset 15 `#AttrHasFK`; per-attribute boolean "carries any FK") | reconstructed on-demand via `Catalog.References` set membership | 🟡 DIVERGENCE | `OssysRowsetParityInventoryTests.``5.1.α row 20`` ` (Skip) | Slice 5.1.α. Same rationale as row 19. |
| 21 | `OutsystemsForeignKeyColumnsJsonRow` (rowset 16 `#FkColumnsJson`; FOR JSON PATH per FkObjectId) | (not lifted; V1-internal JSON aggregation) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 21`` ` (Skip) | Slice 5.1.α. Feeds V1's `osm_model.json` FK columns. V2 reconstructs at emit time. See `DECISIONS 2026-05-17 (slice 5.1.α)`. |
| 22 | `OutsystemsForeignKeyAttributeJsonRow` (rowset 17 `#FkAttrJson`; FOR JSON PATH per attribute of FK constraints) | (not lifted; V1-internal JSON aggregation) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 22`` ` (Skip) | Slice 5.1.α. Feeds V1's `osm_model.json`. V2 reconstructs at emit time. |
| 23 | `OutsystemsTriggerRow` (rowset 18 `#Triggers`; DDL trigger reflection on OSSYS-source: name / IsDisabled / definition) | (not lifted; V2 has no trigger axis in `Catalog`) | 🟠 NOT-MAPPED | `OssysRowsetParityInventoryTests.``5.1.α row 23`` ` (Skip) | Slice 5.1.α. Trigger: V2 IR refinement adds `Catalog.Triggers` AND a downstream emitter demands trigger evidence; OR cutover-15 risk analysis identifies live OSSYS-managed triggers that V2's emission must preserve. |
| 24 | `OutsystemsAttributeJsonRow` (rowset 19 `#AttrJson`; FOR JSON PATH per entity of attributes) | (not lifted; V2 builds `Catalog.Modules.Attributes` from rowset 3) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 24`` ` (Skip) | Slice 5.1.α. Feeds V1's `osm_model.json`. V2's structured equivalent is the typed `AttributeRow` list. |
| 25 | `OutsystemsRelationshipJsonRow` (rowset 20 `#RelJson`; FOR JSON PATH per entity of FK relationships) | (not lifted; V2 builds `Catalog.References` from rowset 4) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 25`` ` (Skip) | Slice 5.1.α. Feeds V1's `osm_model.json` relationships array. |
| 26 | `OutsystemsIndexJsonRow` (rowset 21 `#IdxJson`; FOR JSON PATH per entity of indexes) | (consumed today via `osm_model.json` → `Catalog.Indexes`) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 26`` ` (Skip) | Slice 5.1.α. Today V2 consumes IndexJson indirectly via `osm_model.json`; sunset rationale is "V1's JSON-aggregation step retires post-V1-sunset; V2 lifts structured rowsets 10 + 11 (rows 15 + 16) to maintain index evidence." |
| 27 | `OutsystemsTriggerJsonRow` (rowset 22 `#TriggerJson`; FOR JSON PATH per entity of triggers) | (not lifted; V2 has no trigger axis) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 27`` ` (Skip) | Slice 5.1.α. Underlying trigger evidence tracked at row 23. |
| 28 | `OutsystemsModuleJsonRow` (rowset 23 `#ModuleJson`; root FOR JSON PATH envelope per module) | (not lifted; V2's `Catalog` IR is the structured equivalent) | ⚫ V1-SUNSET | `OssysRowsetParityInventoryTests.``5.1.α row 28`` ` (Skip) | Slice 5.1.α. The osm_model.json document root. V2 emits SSDT artifacts directly from `Catalog`. |
| 29 | `OutsystemsMetadataSnapshot.DatabaseName` (envelope field; populated from `SqlConnection.Database`) | (not carried; absent from V2's `MetadataSnapshot`) | 🟡 DIVERGENCE | `OssysRowsetParityInventoryTests.``5.1.α row 29`` ` (Skip) | Slice 5.1.α. V2 treats database identity as a realization-time concern (emission parameter). See `DECISIONS 2026-05-17 (slice 5.1.α) — Database identity is a realization-time concern`. |

---

## Parity-audit slice queue (the in-flight wave)

The first wave targets the V1 SqlExtraction layer (most relevant to chapter 5.0's pivot). Each row below is a candidate slice; the next-agent should pick the highest-leverage row matching session capacity.

| Slice | V1 source(s) | Expected scope | Audit-priority rationale |
|---|---|---|---|
| ~~5.1.α~~ | ~~`src/Osm.Pipeline/SqlExtraction/IOutsystemsMetadataReader.cs`~~ | ~~~100 LOC; matrix rows 11–28~~ | **Shipped 2026-05-17 → matrix rows 11–29 (8 NOT-MAPPED + 3 DIVERGENCE + 8 V1-SUNSET).** |
| **5.1.β** | `src/Osm.Pipeline/SqlExtraction/SnapshotValidator.cs` | ~200 LOC; 1 matrix row | Sanity-check semantics V2 may want for live-DB pickup. |
| **5.1.γ** | `src/Osm.Pipeline/SqlExtraction/SqlClientOutsystemsMetadataReader.cs` | ~300 LOC; 1–3 matrix rows | Connection lifecycle, retry, timeout, transient-error semantics for the production wiring. |
| **5.1.δ** | `src/Osm.Pipeline/SqlExtraction/FixtureAdvancedSqlExecutor.cs` + `FixtureOutsystemsMetadataReader.cs` | ~200 LOC; 1–2 matrix rows | V1's offline-test-fixture surface — useful precedent for V2's offline-test infrastructure. |
| **5.1.ε** | `src/Osm.Pipeline/SqlExtraction/SqlMetadataDiagnosticsWriter.cs` | ~150 LOC; 1 matrix row | V1's diagnostic-event surface during extraction. Matters for Diagnostics-axis parity. |
| **5.1.ζ** | `src/Osm.Pipeline/SqlExtraction/MetadataContractOverrides.cs` | ~100 LOC; 1 matrix row | V1's hook for tolerating contract drift across OutSystems versions. |
| **5.2.α** | `src/Osm.Domain/Model/*.cs` (the aggregate-root model) | ~2000 LOC; 10+ matrix rows | The biggest single audit cluster. Likely produces many 🔵 V2-EXTENSION + several 🟢 PARITY + 🟠 NOT-MAPPED rows. |
| **5.2.β** | `src/Osm.Json/Deserialization/*.cs` | ~1000 LOC | V1's JSON shape that V2 currently mirrors via `osm_model.json` parsing. |
| **5.3.α** | `src/Osm.Smo/PerTableEmission/*.cs` | ~1500 LOC | V1's SMO-based emission. Matters for Schema-axis cutover-fidelity. |
| **5.3.β** | `src/Osm.Smo/IndexScriptBuilder.cs` + `CreateTableStatementBuilder.cs` | Already partially audited (chapter 4.9 references) | Re-validates chapter 4.9's slice γ + ε against V1 byte-shape. |

**Trigger to add a slice to the queue:** any V1 audit slice that touches a new V1 file/cluster. Append a row above. The queue is unordered priority-wise; pick what matches the session's capacity.

---

## Anti-patterns (what this discipline is NOT)

- **NOT a full enumeration of V1 code.** The matrix tracks V1 capabilities V2 cares about. V1-only test fixtures, V1's internal scaffolding, V1's discontinued experimental code paths are out of scope.
- **NOT a static-analysis report.** The matrix is per-capability evidence-driven. Each row is a deliberate audit slice that produced an artifact (a test, a divergence document, a sunset rationale).
- **NOT a substitute for property tests.** The matrix tracks per-capability parity status; property tests assert algebraic invariants (T1 determinism, T11 sibling commutativity, A39 smart-constructor invariants). They're orthogonal — both are needed.
- **NOT an excuse to defer hard work.** A 🟠 NOT-MAPPED status MUST name the trigger; "we'll get to it" is unacceptable. If the V2 capability is blocked on cutover, the row says so structurally.

---

## Maintenance

- **Per slice:** the slice's commit appends a row (or amendment) to the matrix.
- **Per chapter close:** the chapter-close ritual gains a "matrix coverage walk" item (added to the ritual at chapter 5.1 close).
- **Per quarter:** matrix re-balance — re-classify rows whose status has drifted; identify clusters of 🟠 NOT-MAPPED candidates that should be lifted in a focused chapter.

---

## Closing

This matrix exists because V1 is large and V2's cutover risk scales with the surface area of un-audited V1 capabilities. The discipline trades depth for breadth: each parity claim is independently verifiable, accumulates over time, and produces an artifact that compounds. Slice-by-slice, the matrix becomes V2's structural answer to "how confident are we that V2 covers V1's surface?"
