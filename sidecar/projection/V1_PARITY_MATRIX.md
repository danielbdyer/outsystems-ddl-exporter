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
| 30 | V1's operator-debugging telemetry surface during SQL extraction: `Pipeline/Sql/SqlMetadataLog.cs` (~86 LOC; observation accumulator), `Pipeline/SqlExtraction/MetadataRowSnapshot.cs` (~179 LOC; last-row-on-failure context), `Pipeline/SqlExtraction/SqlMetadataDiagnosticsWriter.cs` (~156 LOC; JSON-dump emitter writing to an operator-provided path) | V2's `MetadataSnapshotRunner.runAsync` returns `Result<MetadataSnapshot>` with success or single `ValidationError` on failure; no observation accumulator, no row-snapshot-on-failure, no JSON-dump emitter | 🟠 NOT-MAPPED | `OssysExtractionDiagnosticsParityTests.``5.1.ε row 30`` ` (Skip) | Slice 5.1.ε. Trigger: V2 ships a CLI surface for production OSSYS extraction OR a cutover-windowed failure mode demands partial-state context for post-mortem debugging. Diagnostics-axis row (first non-data-shape row in the matrix). |
| 31 | `Pipeline/SqlExtraction/SnapshotValidator.cs` (~133 LOC; JSON-shape validation pre-deserialization: per-module/-entity array presence + non-null checks; throws `InvalidDataException` on contract breach) | V2's `MetadataSnapshotRunner` constructs typed F# records directly from `SqlDataReader` (no JSON layer at this site); `SnapshotJson` path uses `System.Text.Json` deserialization into typed records (null arrays structurally impossible). `Catalog.create` A39 invariants check IR-level integrity (duplicate SsKey / FK referential / index column membership) — higher-level than V1's shape check | ⚫ V1-SUNSET | `OssysSnapshotShapeValidationParityTests.``5.1.β row 31`` ` (Skip) | Slice 5.1.β. Sunset rationale: F# type system makes null arrays impossible by construction; A39 smart-constructor invariants subsume the structural-integrity goal at the IR layer. No analogous V2 capability needed. |
| 32 | V1's exception classification on `MetadataSnapshotRunner.cs` — three distinct exception types (`MetadataRowMappingException` with row coordinates + processor context; `MetadataResultSetMissingException` with expected/actual rowset count; `DbException` catch-all) | V2's `MetadataSnapshotRunner.runAsync` catches all exceptions under a single `with ex ->` clause; wraps `ex.Message` in `ValidationError.create` with no class-discrimination, no friendly-context reconstruction | 🟠 NOT-MAPPED | `OssysProductionWiringParityTests.``5.1.γ row 32`` ` (Skip) | Slice 5.1.γ. Trigger: V2 ships a production CLI surface that needs operator-distinguishable failure modes (row-mapping vs. contract-breach vs. transient SQL error need different operator responses). |
| 33 | V1's `MetadataSnapshotRunner.cs` command timeout read from `SqlExecutionOptions.CommandTimeoutSeconds` (caller-tunable; ADO.NET default 30s when unset; aligns with Polly / EF Core patterns) | V2 sets `command.CommandTimeout <- 0` unconditionally (unlimited; tolerates V1's `SET TEXTSIZE -1` + complex queries in canary scope) | 🟡 DIVERGENCE | `OssysProductionWiringParityTests.``5.1.γ row 33`` ` (Skip) | Slice 5.1.γ. See `DECISIONS 2026-05-17 (slice 5.1.γ) — Command-timeout discipline: canary unlimited, production tunable`. Re-open trigger: V2 ships production CLI for cloud OSSYS; add `commandTimeoutSeconds : int option` parameter to `runAsync`. |
| 34 | V1's transient-error handling on `MetadataSnapshotRunner.cs` — implicit delegation to caller orchestration; SqlException propagates uniformly via `DbException` catch-all without retry | V2 has zero transient-detection or retry; every SqlException propagates immediately as a `ValidationError` | 🟠 NOT-MAPPED | `OssysProductionWiringParityTests.``5.1.γ row 34`` ` (Skip) | Slice 5.1.γ. **Cutover-critical** per V2_DRIVER + R6 split-brain governance — V2's canary must tolerate transient SqlExceptions on cloud OSSYS without false-positive divergence reports. Trigger: V2 reads from cloud OSSYS (Azure SQL / managed instance). Cash-out shape: Polly retry policy with 3× attempts + exponential backoff; retry on SqlException.Number ∈ {-2 timeout, -1 network drop, 40197 / 40501 / 40613 Azure transients} at connection-open and command-execute layers. |
| 35 | V1's `MetadataSnapshotRunner.EnsureNextResultSetAsync` — fails fast with `MetadataResultSetMissingException` (carrying processor name + row count + expected next set) when an expected result set is absent | V2's `runAsync` reads via `while hasMore do let! advanced = reader.NextResultAsync()` that exits silently when the result-set stream ends; partial-data acceptance is silent | 🟠 NOT-MAPPED | `OssysProductionWiringParityTests.``5.1.γ row 35`` ` (Skip) | Slice 5.1.γ. Trigger: V2's canary fails a parity assertion AND the failure traces back to a SQL-contract-shape change; OR V2 ships a production CLI where silent partial-data acceptance is operator-hostile. |
| 36 | V1's `ITaskProgressAccessor` integration — per-processor progress ticks during extraction (operator sees `Extracting Metadata: ModuleRow` → `EntityRow` → ...) | V2's `runAsync` is opaque from start to finish; no callback or progress-reporting interface | 🟠 NOT-MAPPED | `OssysProductionWiringParityTests.``5.1.γ row 36`` ` (Skip) | Slice 5.1.γ. Trigger: V2 ships a production CLI for OSSYS extraction at full catalog scale (300 tables; multi-minute extraction) OR an operator workflow demands extraction-progress observability. Cash-out shape: optional `onProcessorComplete : (rowsetName : string * rowCount : int) -> unit` parameter to `runAsync`. |
| 37 | V1's offline-test-fixture surface: `Pipeline/SqlExtraction/FixtureAdvancedSqlExecutor.cs` (~223 LOC; implements `IAdvancedSqlExecutor`) + `FixtureOutsystemsMetadataReader.cs` (~224 LOC; implements `IOutsystemsMetadataReader`). Both load from a JSON manifest mapping (modules + system/inactive flags) keys to disk-stored pre-canned rowset JSON files | V2's `Projection.Adapters.Osm.CatalogReader.SnapshotRowsets` consumes in-memory `RowsetBundle` records constructed directly in F# (via `IRBuilders.fs` + `Fixtures.fs` literal constructors); V2's offline tests use this path and bypass `MetadataSnapshotRunner.runAsync` entirely | 🟡 DIVERGENCE | `OssysOfflineFixtureParityTests.``5.1.δ row 37`` ` (Skip) | Slice 5.1.δ. V2 chose a different fixture shape — in-memory rowset literals over manifest-keyed JSON files. See `DECISIONS 2026-05-17 (slice 5.1.δ) — Offline fixture shape: in-memory RowsetBundle over manifest-keyed JSON files`. Re-open trigger: a test scenario surfaces that needs `runAsync` exercised against fixture rowsets specifically (e.g., contract-version testing per row 38; failure-mode testing on the SQL execution layer). |
| 38 | `Pipeline/SqlExtraction/MetadataContractOverrides.cs` (~142 LOC; operator-configurable `Dictionary<string, HashSet<string>>` mapping result-set names to optional column names; loaded from `appsettings.json` via `MetadataContractConfiguration.OptionalColumns`; processors consult `IsColumnOptional` to tolerate NULL or missing columns across OutSystems versions) | V2's `MetadataSnapshotRunner` reads via ordinal-indexed access (`readInt r 0`, `readString r 1`, …) — structurally insensitive to column **renaming**, sensitive to column **reordering**. No operator-configurable version-tolerance; the carbon-copied SQL pins the contract version. Two inline NULL fallbacks exist for `IsAutoNumber` (defaults `false`) and `PhysicalCol` (falls back to uppercase `AttrName`) — data-shape resilience, not version-tolerance | 🟡 DIVERGENCE | `OssysContractVersioningParityTests.``5.1.ζ row 38`` ` (Skip) | Slice 5.1.ζ. See `DECISIONS 2026-05-17 (slice 5.1.ζ) — Contract-versioning posture: SQL pins, not operator overrides`. Re-open trigger: cutover or post-cutover schema-drift surfaces a real OutSystems-version mismatch the canary's pre-extraction validation step doesn't catch. |
| 39 | `src/AdvancedSql/outsystems_model_export.sql` (~931 LOC; V1's JSON-emitter SQL — executes against OSSYS-source and produces `osm_model.json` for V1's downstream pipeline) | (not lifted; V2 emits SSDT artifacts via Π chorus directly from V2 Catalog, never producing `osm_model.json`) | ⚫ V1-SUNSET | `AdvancedSqlExportParityTests.``5.1.σ row 39`` ` (Skip) | Slice 5.1.σ. Closes the AdvancedSql audit started at row 1 (carbon-copied rowsets SQL). Sunset rationale: producer-side companion to rows 13/21/22/24-28 (JSON-aggregation rowsets ⚫ V1-SUNSET per `DECISIONS 2026-05-17 (slice 5.1.α)`). **Migration impact**: zero V2-side consumers — V2 reads OSSYS via OssysSql adapter's structured rowsets path; the `SnapshotJson` input variant continues to consume legacy `osm_model.json` files V1 already produced but does not require V1 to keep producing them post-cutover. **Sunset timing**: V1's emission path retires cutover+30 per `VISION.md` T-30 / T-15 fallback ladder. |
| 40 | V1's `Osm.Dmm` cluster (~2200 LOC across 8 files): `IDmmLens<TSource>` port + 3 lens adapters (`ScriptDomDmmLens` parses raw T-SQL; `SmoDmmLens` reads SMO; `SsdtProjectDmmLens` reads SSDT-project files) + `DmmComparator` (feature-gated diff over Columns / PrimaryKeys / Indexes / ForeignKeys) + `SsdtTableLayoutComparator` + `DmmModels` DTOs + `DmmComparisonFeatures` flags | V2's load-bearing fidelity gate is the canary's `PhysicalSchema` round-trip diff (`Projection.Pipeline/Deploy.fs` + `Projection.Adapters.Sql/ReadSide.fs`); structurally subsumes V1's Columns / PKs / Indexes / FKs comparator features but only for the specific `(live OSSYS source ↔ live deployed target)` pair | ⚫ V1-SUNSET | `SchemaDiffLensParityTests.``5.8.α row 40`` ` (Skip) | Slice 5.8.α. Sunset rationale: (a) canary's `PhysicalSchema` diff is the cutover-fidelity gate; subsumes the structural-equivalence claim for the canary's source/target pair; (b) V1's lens classes are V1-trunk-tied (e.g., `SsdtProjectDmmLens` consumes V1's `SsdtProjectMetadata`); porting would carry V1 types into V2's pure F# core; (c) the F# rewrite (closed-DU `DiffSource` over `IDmmLens<TSource>` interface) belongs at row 41, not as a port. See `DECISIONS 2026-05-17 (slice 5.8.α) — DMM lens machinery sunset; schema-diff concept harvested as future CLI verb`. **Migration impact**: V1-side consumers of DMM are V1-only (e.g., V1's DACPAC build verification step uses SsdtProjectDmmLens to verify the produced .dacpac matches expectations); these retire alongside V1. **Sunset timing**: cutover+30 with V1. |
| 41 | V1's operator-facing schema-diff affordance (the operator-level concept abstracted from the lens machinery: "compare two schema sources of arbitrary kind") | V2's CLI exposes 4 verbs (`emit` with `--config`/`--skeleton-only` variants; `deploy`; `canary`; `--help`) — **no operator-facing `compare` verb**. V2's canary subsumes one specific diff case `(live OSSYS source ↔ live deployed target)` but cannot diff arbitrary source pairs (e.g., `(SSDT project ↔ DACPAC file)`, `(deployed before ↔ deployed after)`) | 🟠 NOT-MAPPED | `SchemaDiffLensParityTests.``5.8.α row 41`` ` (Skip) | Slice 5.8.α. **Cash-out shape**: a new `projection compare <left> <right>` CLI verb driven by a closed-DU `DiffSource = LiveDb of connStr * dbName \| SsdtProject of dir \| DacpacFile of path \| RawSql of text` plus an F# `Compare.run : DiffSource -> DiffSource -> Diagnostics<SchemaDiff>` core function. The 4 DiffSource adapters live in `Projection.Adapters.{Sql,SSDT,Dacpac,RawSql}` — three of the four are already present in V2 (Sql + SSDT exist; Dacpac would harvest the DACPAC adapter slice scoped at chapter 5.x.dacpac; RawSql parses via ScriptDom and is a new small adapter). `SchemaDiff` is the typed diff payload (column / PK / index / FK delta DUs), emitting as `Diagnostics<SchemaDiff>` so the chorus discipline holds. The `compare` verb lives in `Projection.Cli/Program.fs` next to `canary`. **Dependencies**: requires the DACPAC adapter (chapter 5.x.dacpac currently deferred) to support the (3) SsdtProject ↔ DacpacFile and (4) DacpacFile ↔ DacpacFile shapes; the (1) LiveDb ↔ LiveDb and (2) LiveDb ↔ SsdtProject shapes can ship today. **Acceptance**: a property test asserts T11 sibling-commutativity over the four DiffSource variants — for any `(a, b)` pair, `Compare.run a b` produces a diff whose inverse equals `Compare.run b a`. **Trigger**: operator workflow demands ad-hoc schema-diff outside the canary's specific scope, OR cutover dry-run discovers a diff case the canary doesn't cover. See `DECISIONS 2026-05-17 (slice 5.8.α)`. |
| 42 | `Osm.Domain/Model/ModuleModel.cs` per-module non-empty Entity invariant in `ModuleModel.Create` (validates `entities.IsDefaultOrEmpty` → `module.entities.empty` ValidationError; gates construction with at least one Entity per module) | V2's `Module.create` (in `Catalog.fs`) permits empty `Module.Kinds`; cardinality enforcement is deferred to caller / adapter discipline (V2's `Catalog.create` enforces global Kind SsKey uniqueness but not per-module min-cardinality) | 🟡 DIVERGENCE | `OssysDomainModuleParityTests.``5.2.α row 42`` ` (Skip) | Slice 5.2.α.module. See `DECISIONS 2026-05-18 (slice 5.2.α.module) — Per-module non-empty invariant: caller discipline over Module.create`. **Cash-out option (restore)**: add `if List.isEmpty kinds then Result.failureOf (ValidationError.create "module.kinds.empty" "module must contain at least one kind") else …` to `Module.create`. **Cost**: ~5 LOC; one new error code; possibly some adapter tests need updating. **Re-open trigger**: a transformation pass produces an empty module (ghost-module bug surfaces during cutover or post-cutover). |
| 43 | `Osm.Domain/Model/ModuleModel.cs` per-module entity-name uniqueness (logical-name + case-insensitive physical-name) enforced by `ModuleModel.Create`; rejects with `module.entities.duplicateLogical` / `module.entities.duplicatePhysical` ValidationErrors | V2's `Kind` decouples identity (SsKey) from naming (Name per pillar 8); two Kinds with identical Name but different SsKeys are distinct catalog objects. SsKey-based uniqueness enforced at `Catalog.create` global Kind-disjointness check (A11 coproduct-cell discipline). Name-based uniqueness not enforced anywhere | 🔵 V2-EXTENSION | `OssysDomainModuleParityTests.``5.2.α row 43`` ` (Skip) | Slice 5.2.α.module. V2's identity decoupling is structurally stronger — type-witnessed via SsKey; conformant with A2 (identity-survives-rename). V1's name-collision detection becomes a V1-era artifact; V2's identity equality is the canonical check. No DECISIONS row needed (existing A2 axiom covers the rationale). |
| 44 | `Osm.Domain/Model/ModuleModel.cs` and `OsmModel.cs` accept `extendedProperties : IEnumerable<ExtendedProperty>?` (nullable); materialize + normalize `null` to `ExtendedProperty.EmptyArray` at construction time | V2's `Module.create` accepts `extendedProperties : ExtendedProperty list` (non-nullable, F# list type — null impossible by construction). `ExtendedProperty.create` smart constructor normalizes empty-string Value to `None` per V1 parity | 🔵 V2-EXTENSION | `OssysDomainModuleParityTests.``5.2.α row 44`` ` (Skip) | Slice 5.2.α.module. V2 stronger: non-null list (Nullable=enable + TreatWarningsAsErrors=true prevents null escapes); ExtendedProperty.create empty-value normalization mirrors V1's parity. No additional parity work needed. |
| 45 | `Osm.Domain/Model/EntityModel.cs` dual identity — `EntityId : int` (local to EspaceId context; SQL Server transaction-scope durability) + `EntitySsKey : Guid?` (optional sourced identity Guid from `ossys_Entity.SS_Key`) | V2's `Kind.SsKey : SsKey` — a closed 4-variant DU (`OssysOriginal of guid | Synthesized of source × basis | Derived of original × reason | V1Mapped of v1SsKey × v2Namespace`) defined in `Identity.fs`. Type-witnessed identity; compiler refuses string/int substitution; `V1Mapped` variant explicitly threads cross-version identity | 🔵 V2-EXTENSION | `OssysDomainEntityParityTests.``5.2.α row 45`` ` (Skip) | Slice 5.2.α.entity. V2 stronger via closed-DU typed identity; covered by A1 (identity is structural) + A2 (identity-survives-rename through JSON path). V1's `EntityId` discarded at adapter boundary (transaction-local; not durable); V1's `EntitySsKey` becomes `OssysOriginal` or `V1Mapped` per provenance. No additional parity work needed. |
| 46 | `Osm.Domain/Model/EntityModel.cs` kind/origin axis via two binary booleans — `IsSystemEntity : bool` (platform-internal) + `IsExternalEntity : bool` (Integration Studio external). 2-bit encoding with 4 implicit states | V2's `Kind` decomposes the axis into: (a) closed-DU `Origin = OsNative | ExternalViaIntegrationStudio | ExternalDirect` (3 explicit states for sourcing); (b) `ModalityMark list` with payload-free `SystemOwned` variant (sibling to `TenantScoped`, `SoftDeletable`, `Temporal`, `Static`) for ownership distinction. Orthogonal axes (origin × ownership) become type-distinct rather than convention-distinct | 🔵 V2-EXTENSION | `OssysDomainEntityParityTests.``5.2.α row 46`` ` (Skip) | Slice 5.2.α.entity. V2 stronger via closed-DU + ModalityMark separation; pillar 9 classifies all as DataIntent (sourced from V1 rowsets; no operator opinion). No DECISIONS needed — already covered by chapter A.0' slice δ (ModalityMark) + chapter 3.2 slice 3 (Origin DU). |
| 47 | `Osm.Domain/Model/EntityModel.cs` per-entity `Catalog : string?` field — the database name where the entity's table resides; used by V1's SMO emitter for `[Catalog].[Schema].[Table]` qualified-name rendering | V2's `Kind` has no equivalent field. Same axis as matrix row 29 (`OutsystemsMetadataSnapshot.DatabaseName` envelope field) | 🟡 DIVERGENCE | `OssysDomainEntityParityTests.``5.2.α row 47`` ` (Skip) | Slice 5.2.α.entity. V2 treats database identity as a realization-time concern, not threaded through IR. See `DECISIONS 2026-05-17 (slice 5.1.α) — Database identity is a realization-time concern, not an IR field` — the same rationale applies at Kind level. Re-open trigger: a V2 emission consumer demands per-Kind database threading (unlikely; the Catalog stays deployment-agnostic by design). |
| 48 | V1's attribute aggregate **three-layer separation** across 7 files: (1) **Logical** (`AttributeModel` + `AttributeMetadata`; LogicalName, ColumnName, DataType, defaults, IsMandatory, IsIdentifier, IsAutoNumber, Description, ExtendedProperties); (2) **Physical reality** (`AttributeReality`; 5 reflection fields — IsNullableInDatabase, HasNulls, HasDuplicates, HasOrphans, IsPresentButInactive); (3) **On-disk evidence** (`AttributeOnDiskMetadata` + `AttributeOnDiskCheckConstraint` + `AttributeOnDiskDefaultConstraint`; SqlType, MaxLength, Precision, Collation, IsIdentity, IsComputed, DefaultDefinition, CHECK constraint arrays) | V2 **consolidates** into a single `Attribute` record (~21 fields) + table-scoped `Kind.ColumnChecks : ColumnCheck list` (chapter A.0' slice ε); per-attribute reality reflection fields are absent from the V2 IR (pillar 9 — operator-intent / observation excluded from DataIntent schema definition) | 🟡 DIVERGENCE | `OssysDomainAttributeParityTests.``5.2.α row 48`` ` (Skip) | Slice 5.2.α.attribute. See `DECISIONS 2026-05-18 (slice 5.2.α.attribute) — V1 three-layer attribute model consolidates into V2 typed Attribute + table-scoped checks`. **Re-open trigger**: V2 grows a Profile-layer surface that carries runtime reflection statistics (parallels matrix row 30 telemetry); the layer-3 reality fields lift into `Profile.AttributeReality` rather than `Attribute`. |
| 49 | `Osm.Domain/Model/AttributeReality.cs` 5 runtime-reflection fields: `IsNullableInDatabase`, `HasNulls`, `HasDuplicates`, `HasOrphans`, `IsPresentButInactive` (sourced from deployment-target sys.* reflection + statistical sampling) | (not carried; V2's data-intent boundary excludes reflection statistics from schema-definition IR) | 🟠 NOT-MAPPED | `OssysDomainAttributeParityTests.``5.2.α row 49`` ` (Skip) | Slice 5.2.α.attribute. **Cash-out shape**: add `Profile.AttributeReality` record carrying the 5 fields per attribute; thread through ReadSide adapter (`Projection.Adapters.Sql/ReadSide.fs`); downstream consumers (tightening passes; remediation emitter) access via `Profile` projection per A34 (Profile is independent of Catalog and Policy). **Trigger**: V2 grows a Profile-layer surface AND a downstream consumer (tightening pass; remediation emitter) needs to consume per-attribute reflection state. **Dependencies**: V2's existing `Profile.fs` carries some statistical evidence (column distributions per chapter 3.1) but no per-attribute reality fields yet. |
| 50 | `Osm.Domain/Model/AttributeOnDiskCheckConstraint.cs` — V1 nests CHECK constraints inside `AttributeOnDiskMetadata` as a per-attribute array (Name, Definition, IsNotTrusted); attribute-scoped lifecycle | V2's `Kind.ColumnChecks : ColumnCheck list` (chapter A.0' slice ε IR lift; L3-S5 sub-axiom) — **table-scoped** collection carrying (SsKey + Name option + Definition + IsNotTrusted) | 🔵 V2-EXTENSION | `OssysDomainAttributeParityTests.``5.2.α row 50`` ` (Skip) | Slice 5.2.α.attribute. V2's placement **corrects** V1's mismodeling — a CHECK constraint may span multiple columns; attribute-scoping was V1's error. V2 carries typed SsKey identity. Emitter consumer for CHECK in DDL is a separate axis (matrix row 12 NOT-MAPPED — V2 IR carries but no SSDT emitter consumes yet). No additional parity work needed for the IR side. |
| 51 | `Osm.Domain/Model/AttributeReference.cs` — V1 embeds FK reference as an attribute-nested optional 6-field record (IsReference, TargetEntityId, TargetEntity, TargetPhysicalName, DeleteRuleCode, HasDatabaseConstraint) | V2 lifts references out of `Attribute` into `Kind.References : Reference list` (chapter 4.2 + chapter 4.6 + chapter 5.0 slice δ design). V2's `Reference` carries (SourceAttribute: SsKey, TargetKind: SsKey, OnDelete: ReferenceAction closed DU, HasDbConstraint: bool, RefEntityId: int option). The lift enables symmetric-closure pass (chapter 3.5), topological ordering (chapter 3.7), bidirectional SsKey navigation | 🔵 V2-EXTENSION | `OssysDomainAttributeParityTests.``5.2.α row 51`` ` (Skip) | Slice 5.2.α.attribute. V2's typed `ReferenceAction` DU (NoAction | Cascade | SetNull | Restrict) replaces V1's string `DeleteRuleCode` — exhaustiveness compiler-checked. The lift architecturally unlocks the FK reflow chapter (4.2) and the symmetric-closure pass. No additional parity work needed for the IR side. |
| 52 | `Osm.Domain/Model/AttributeModel.cs` carries `DataType : string` (free-form), `DefaultValue : string?` (raw default expression), `Length / Precision / Scale : int?` (untyped dimensions) | V2's `Attribute.Type : PrimitiveType` (closed DU per A13 — Identifier, Integer, LongInteger, Text, Email, PhoneNumber, Boolean, DateTime, Date, Time, Decimal, Currency, BinaryData) + `DefaultValue : SqlLiteral option` (typed value with structural validation) + `Length / Precision / Scale : int option` | 🔵 V2-EXTENSION | `OssysDomainAttributeParityTests.``5.2.α row 52`` ` (Skip) | Slice 5.2.α.attribute. V2's typed primitives flow from pillar 1 (data-structure-oriented over string-parsing). Matrix row 10 (`parsePrimitiveType` partial NOT-MAPPED) tracks the inverse — adding V1 OutSystems types as PrimitiveType variants when evidence demands. SqlLiteral validation is structural; emit-time round-trip preserved via ScriptDom. |
| 53 | `Osm.Domain/Model/AttributeOnDiskDefaultConstraint.cs` — V1 carries DEFAULT-constraint **envelope** (Name : string, Definition : string, IsNotTrusted : bool) | V2's `Attribute.DefaultValue : SqlLiteral option` carries only the Definition (as a typed value); constraint metadata (Name + IsNotTrusted) is **dropped** at the adapter boundary | 🟠 NOT-MAPPED | `OssysDomainAttributeParityTests.``5.2.α row 53`` ` (Skip) | Slice 5.2.α.attribute. **Cash-out shape**: extend V2 with `Attribute.Default : DefaultConstraint option` (replacing `DefaultValue : SqlLiteral option`) where `DefaultConstraint = { Name : Name option; Value : SqlLiteral; IsNotTrusted : bool }`. **Migration**: existing call sites consuming `DefaultValue` map to `Default |> Option.map (fun d -> d.Value)`. **Trigger**: (a) manifest emitter needs constraint identity (e.g., operator-visible drift reports naming constraints); OR (b) DDL emitter needs to round-trip the V1 constraint name (preserving `DF_TableName_ColumnName` conventions). **Acceptance**: a canary round-trip test asserts default-constraint names survive emit → deploy → readback. |
| 54 | `Osm.Domain/Model/IndexKind.cs` — V1 enum with 6 variants (`PrimaryKey | UniqueConstraint | UniqueIndex | NonUniqueIndex | ClusteredIndex | NonClusteredIndex`) covering the cross-product of (PK / unique / clustered) | V2 decomposes the axis into boolean flags `Index.IsPrimaryKey : bool` + `Index.IsUnique : bool` + emitter-side clustered/non-clustered choice; structurally equivalent (closed coverage of the cross-product) but trades enum name-as-constant for boolean composition | 🟡 DIVERGENCE | `OssysDomainIndexParityTests.``5.2.α row 54`` ` (Skip) | Slice 5.2.α.index. V2's choice flows from `IR grows under evidence, not speculation` — when `IsPrimaryKey` was added (chapter 3.2), the consumer demand was per-PK behavior, not per-enum-variant. **Re-open trigger**: emission consumer needs per-IndexKind dispatch (e.g., a per-variant validation rule); cash-out: lift to `IndexKind = PrimaryKey | UniqueConstraint | UniqueIndex | NonUniqueIndex | ClusteredIndex | NonClusteredIndex` closed DU and rebuild `IsPrimaryKey` / `IsUnique` as derived projections. |
| 55 | `Osm.Domain/Model/IndexOnDiskMetadata.cs` — V1 carries `IsDisabled : bool` (reflects sys.indexes disabled state for INDEX DISABLE/ENABLE round-trip) + `IgnoreDuplicateKey : bool` (reflects `IGNORE_DUP_KEY` SQL Server option) | V2's `Index` carries neither | 🟠 NOT-MAPPED | `OssysDomainIndexParityTests.``5.2.α row 55`` ` (Skip) | Slice 5.2.α.index. **Cash-out shape**: add `Index.IsDisabled : bool` (defaults `false`) + `Index.IgnoreDuplicateKey : bool` (defaults `false`); adapter pickup at OssysSql rowset 10 lift (paired with matrix rows 15 + 16); emitter consumption at `ScriptDomBuild.buildCreateIndex` (set `IndexStatement.IgnoreDupKey = true`; emit `ALTER INDEX … DISABLE` post-create when disabled). **Acceptance**: round-trip test asserts disabled + IGNORE_DUP_KEY survive emit → deploy → readback. **Trigger**: an emission consumer demands these axes (e.g., a deployed target carries disabled indexes V2 must round-trip; or `IGNORE_DUP_KEY` on a unique index that V2 must preserve). |
| 56 | `Osm.Domain/Model/IndexDataSpace.cs` + `IndexPartitionColumn.cs` + `IndexPartitionCompression.cs` — V1's on-disk introspection axis carries: data-space placement (filegroup name or partition-scheme name + columns), per-partition column membership (column + ordinal), per-partition data compression level (NONE / ROW / PAGE) | V2's `Index` has no partition / data-space / data-compression carriage | 🟠 NOT-MAPPED | `OssysDomainIndexParityTests.``5.2.α row 56`` ` (Skip) | Slice 5.2.α.index. **Cash-out shape**: extend `Index` with `DataSpace : DataSpace option` (closed DU `DataSpace = Filegroup of name | PartitionScheme of name × columns : SsKey list`) + `Index.PartitionCompression : PartitionCompression list` (per-partition typed config: `{ PartitionNumber : int; Compression : DataCompression }` where `DataCompression = None | Row | Page`). Adapter pickup at OssysSql rowset 10 lift (paired with row 55). **Priority: low** — V2's canary target (synthetic OSSYS) doesn't use partitioning; trigger fires when production OSSYS uses partitioned indexes that V2 must round-trip without losing partition scheme. **Acceptance**: round-trip test asserts partition-scheme + per-partition compression survive emit → deploy → readback on a partitioned-index fixture. |
| 57 | V1's FK axis spans 3 types: `Osm.Domain/Model/RelationshipModel.cs` (logical edge — via-attribute-to-entity, DeleteRuleCode, HasDatabaseConstraint) + `ForeignKeyModel.cs` (physical constraint — name, DeleteRule, UpdateRule) + `RelationshipActualConstraint.cs` (reconciliation — per-column mapping, per-action NOCHECK state via empty action strings) | V2's `Reference` record (in `Catalog.fs`) **conflates** all three: `{ SsKey, SourceAttribute : SsKey, TargetKind : SsKey, OnDelete : ReferenceAction, HasDbConstraint : bool, RefEntityId : int option }`. Chapter 4.6 design closed the logical/physical distinction at the IR layer | 🟡 DIVERGENCE | `OssysDomainRelationshipParityTests.``5.2.α row 57`` ` (Skip) | Slice 5.2.α.relationship. See `DECISIONS 2026-05-18 (slice 5.2.α.relationship) — V1 three-type relationship/FK split conflates into V2 single Reference`. V2's conflation enables symmetric closure (chapter 3.5), topological ordering (chapter 3.7), FK reflow (chapter 4.2). **Re-open trigger**: V2 needs to round-trip FK constraint **names** (operator-supplied) — currently V2 generates names by convention. Cash-out: extend Reference with `Name : Name option` (defaults None; adapter populates from V1 source). |
| 58 | `Osm.Domain/Model/ForeignKeyModel.cs` carries paired delete + update referential actions (`DeleteRule : string`, `UpdateRule : string`); V1 emits both at SMO emission time | V2's `Reference.OnDelete : ReferenceAction` carries only delete; UpdateAction dropped at adapter boundary; V2 doesn't emit ON UPDATE clauses | 🟠 NOT-MAPPED | `OssysDomainRelationshipParityTests.``5.2.α row 58`` ` (Skip) | Slice 5.2.α.relationship. **Cash-out shape**: extend `Reference` with `OnUpdate : ReferenceAction option` (defaults `None` → ON UPDATE NO ACTION); adapter pickup at OssysSql ForeignKeys rowset (paired with matrix row 17 lift); emitter consumption at `ScriptDomBuild.buildForeignKey` (set `ForeignKeyConstraintDefinition.UpdateAction = …` when Some). **Acceptance**: a property test asserts ON UPDATE survives emit → deploy → readback for each ReferenceAction variant. **Trigger**: V2's SSDT emission must support ON UPDATE referential actions (a deployed target has ON UPDATE CASCADE V2 must round-trip; OR V2's emission needs explicit ON UPDATE NO ACTION per modern T-SQL conventions). |
| 59 | `Osm.Domain/Model/RelationshipActualConstraint.cs` per-constraint NOCHECK state — V1 distinguishes "FK constraint exists but is not enforced" (the `WITH NOCHECK` clause was applied) from "FK constraint exists and is trusted." Signaled by empty `OnDeleteAction` / `OnUpdateAction` strings | V2's `Reference.HasDbConstraint : bool` is binary (presence/absence); no enforcement-state axis | 🟠 NOT-MAPPED | `OssysDomainRelationshipParityTests.``5.2.α row 59`` ` (Skip) | Slice 5.2.α.relationship. **Cash-out shape**: extend `Reference` with `IsConstraintTrusted : bool` (defaults `true`); adapter pickup at OssysSql `#FkReality` rowset's `IsNoCheck` column (paired with matrix row 17 lift); emitter consumption at `ScriptDomBuild.buildForeignKey` (emit `WITH NOCHECK` when `IsConstraintTrusted = false`). **Acceptance**: a round-trip test asserts WITH NOCHECK state survives emit → deploy → readback. **Trigger**: a deployed target carries WITH NOCHECK FK constraints V2 must round-trip (rare; usually a remediation-time concern when adding FKs to existing data without forced validation). |
| 60 | `Osm.Domain/Model/SequenceModel.cs` (~150 LOC; Schema, Name, DataType, StartValue, Increment, Minimum, Maximum, IsCycleEnabled, `SequenceCacheMode` enum (Unspecified/Cache/NoCache/UnsupportedYet), CacheSize, ExtendedProperties) | V2's `Sequence` record in `Catalog.Sequences` (`Catalog.fs` lines 227–279) carries all V1 fields plus typed SsKey identity. V1's 4-variant cache enum maps to V2's 3-variant closed DU (Unspecified | Cache | NoCache); UnsupportedYet variant deferred per slice-β normalization | 🟢 PARITY (IR; emitter deferred) | `OssysDomainMiscParityTests.``5.2.α row 60`` ` (Skip) | Slice 5.2.α.misc. Chapter A.0' slice δ shipped IR; L3-S5 sub-axiom. Sequence-level ExtendedProperties dropped at adapter boundary (trigger: re-add when sequence-level extended-properties accessor lands). Emitter (`CREATE SEQUENCE` DDL) is deferred per chapter A.0' slice δ — IR shipped without emission consumer. |
| 61 | `Osm.Domain/Model/TriggerModel.cs` (Name, IsDisabled, Definition; schema-scoped) | V2's `Trigger` record in `Kind.Triggers` (`Catalog.fs` lines 181–212) carries all V1 fields plus typed SsKey identity; placement is kind-scoped per the domain semantic (a trigger is owned by the table it fires on). Chapter A.0' slice γ shipped; L3-S4 sub-axiom | 🟢 PARITY | `OssysDomainMiscParityTests.``5.2.α row 61`` ` (Skip) | Slice 5.2.α.misc. **Important**: this finding makes matrix row 23 (OutsystemsTriggerRow → MetadataSnapshot.Triggers — original 🟠 NOT-MAPPED) **stale**. V2's Trigger IR is shipped; the OSSYS-source rowset 18 `#Triggers` lifts into the existing V2 `Trigger` shape (not a new axis). See row 23 Status history amendment below. |
| 62 | `Osm.Domain/Model/ExtendedProperty.cs` (Name : string, Value : string?; smart constructor normalizes empty-string Value to null) | V2's `ExtendedProperty` record (`Catalog.fs` lines 78–105) carries Name + Value : string option; module function `ExtendedProperty.create` mirrors V1's empty-string normalization. Smart-constructor invariants match (non-blank Name) | 🟢 PARITY | `OssysDomainMiscParityTests.``5.2.α row 62`` ` (Skip) | Slice 5.2.α.misc. **Scope**: V2 places ExtendedProperty at 4 levels — `Attribute.ExtendedProperties`, `Index.ExtendedProperties`, `Kind.ExtendedProperties`, `Module.ExtendedProperties`. V1's scope is broader (also sequences). V2's 4-level placement is the operationally-complete set today; sequence-level deferred per row 60's trigger. Emitter is `ScriptDomBuild.buildSetExtendedProperty` per chapter 4.1.A slice 8; module-level emission gated on V1-side confirmation of module→schema convention (deferred per `DECISIONS 2026-05-17 — sp_addextendedproperty emission`). |
| 63 | `Osm.Domain/Model/TemporalRetentionPolicy.cs` (4-variant Kind enum: None/Infinite/Limited/UnsupportedYet, Value, Unit enum) + `EntityMetadata.Temporal : TemporalTableMetadata` (Type, HistorySchema, HistoryTable, PeriodStartColumn, PeriodEndColumn, RetentionPolicy, ExtendedProperties) | V2's temporal axis embeds in `ModalityMark.Temporal of TemporalConfig` (`Catalog.fs` line 337) carrying `TemporalRetention = Infinite | Limited of int × TemporalRetentionUnit` + `TemporalConfig = { HistorySchema; HistoryTable; PeriodStart; PeriodEnd; Retention }`. V2 **refines** V1's 4-variant enum to 2-variant DU: None is implicit (absence of `ModalityMark.Temporal` from modality list); UnsupportedYet deferred per chapter A.0' slice η scope | 🟢 PARITY (IR; emitter deferred) | `OssysDomainMiscParityTests.``5.2.α row 63`` ` (Skip) | Slice 5.2.α.misc. V2's refinement is structural tightening (None becomes presence/absence at the parent ModalityMark list level — type-witnessed). Temporal-table DDL emission (`CREATE TABLE ... PERIOD FOR ... HISTORY_RETENTION_PERIOD ...`) deferred pending SSDT realization gate. |
| 64 | V1's nullability signal-combinator architecture: abstract base `NullabilitySignal` + recursive `AllOfSignal` / `AnyOfSignal` composition + 2-valued `SignalEvaluation.Result : bool` + flat-string rationale via `CollectRationales()`. Root tree assembled by `NullabilitySignalFactory` from per-mode `NullabilityModeDefinition` | V2's typed-strategy architecture: `StrategyEvaluator<'context, 'config, 'decision>` function-type seam (`Projection.Core/Strategies/NullabilityRules.fs`) + closed-DU `NullabilityOutcome` + linear if-elif decision sequence + structured-typed-evidence per outcome variant. `NullabilityPass` (`Projection.Core/Passes/NullabilityPass.fs`) delegates to `Composition.fanOut` over (context × intervention) pairs | 🔵 V2-EXTENSION | `OssysTighteningNullabilityParityTests.``5.4.β.nullability row 64`` ` (Skip) | Slice 5.4.β.nullability. Same outcomes on shared inputs; V2's type-level evidence is load-bearing for downstream emitter consumers + canary per-decision audit trail. Covered by `DECISIONS 2026-05-11 — Strategy-layer codification: empirical verdict after the fourth instance`. **No additional parity work needed** — V2's architecture is structurally stronger via typed-seam + closed-DU outcomes. |
| 65 | V1's `SignalEvaluation.Result : bool` is 2-valued (true/false); when a mandatory column has nulls beyond budget AND tightening options forbid silent relaxation, V1 returns `false` AND surfaces an `Opportunity` record with `Disposition.NeedsRemediation` — the decision is deferred via out-of-band metadata | V2's `NullabilityOutcome` carries a **third variant** `RequireOperatorApproval (NullabilityConflict)` with typed conflict evidence; the third state is in the main decision type, not a side-channel. F# exhaustiveness checking enforces all three cases at every consumer | 🔵 V2-EXTENSION | `OssysTighteningNullabilityParityTests.``5.4.β.nullability row 65`` ` (Skip) | Slice 5.4.β.nullability. See `DECISIONS 2026-05-18 (slice 5.4.β.nullability) — Ternary outcome space for operator-approval decision lifting`. The pattern applies symmetrically to `UniqueIndexOutcome` + `ForeignKeyOutcome` (sibling registered-intervention strategies). **Cash-out shape**: no work — V2's ternary outcome is canonical; the DECISIONS row codifies the pattern for future strategies. |
| 66 | V1's `UniqueCleanSignal.cs` and `ForeignKeySupportSignal.cs` live under `Osm.Validation/Tightening/Signals/` and **participate in the nullability AnyOf root tree** (mode-dependent: TelemetryOnly in Cautious; Tighten in Aggressive) — nullability decisions can be influenced by unique-index + FK signals | V2 **separates** these into independent registered-intervention strategies: `Projection.Core/Strategies/UniqueIndexRules.fs` + `Projection.Core/Passes/UniqueIndexPass.fs` (own outcome `UniqueIndexOutcome`); `Projection.Core/Strategies/ForeignKeyRules.fs` + `Projection.Core/Passes/ForeignKeyPass.fs` (own outcome `ForeignKeyOutcome`). Each axis runs its own pass; lineage events are per-axis classified | 🔵 V2-EXTENSION | `OssysTighteningNullabilityParityTests.``5.4.β.nullability row 66`` ` (Skip) | Slice 5.4.β.nullability. V2's axis separation is principled per pillar 9 (harvest-dichotomy classification — `DECISIONS 2026-05-15 (late)`). Tightening / uniqueness / FK are orthogonal decision axes; bundling them under a single signal tree (V1) conflates concerns. **Acceptance**: a property test asserts pass independence — changing `Policy.Nullability` config does not change `UniqueIndexPass` or `ForeignKeyPass` outputs on the same Catalog. |
| 67 | V1's `DefaultSignal.cs` (~18 LOC) checks `!string.IsNullOrWhiteSpace(context.Attribute.DefaultValue)` and emits rationale `"DefaultPresent"` when attribute is also mandatory. **`Participation = TelemetryOnly` across all 3 V1 modes** — the signal never causes tightening | V2 omits the signal entirely from `NullabilityRules.evaluate` | ⚫ V1-SUNSET | `OssysTighteningNullabilityParityTests.``5.4.β.nullability row 67`` ` (Skip) | Slice 5.4.β.nullability. **Sunset rationale**: presence of a DEFAULT clause does not prevent NULL inserts (`INSERT INTO t (col) VALUES (NULL)` inserts NULL even with DEFAULT present); therefore DEFAULT is not a signal for nullability tightening. V1's inclusion was telemetry-only and structurally misleading (the signal's existence under `Signals/` suggests it influences nullability when it does not). V2's omission removes the noise signal. **Migration impact**: zero — V1's signal was telemetry-only; no downstream consumer changed behavior based on it. **Sunset timing**: cutover+30 with V1. |
| 68 | V1's `RequiresEvidenceSignal.cs` (~27 LOC) is a higher-order combinator: wraps `(inner: NullabilitySignal, evidence: NullabilitySignal)`. Evaluates inner first; on `inner.Result = false` returns false immediately; else evaluates evidence and returns `evidence.Result`. Composes outer rationale collection recursively | V2 **inlines** the evidence check directly into the atomic decision rule — `NullabilityRules.evaluate`'s Mandatory branch checks profile inline; the typed evidence values (`NullCount`, `RowCount`, `NullBudget`) flow into the structured outcome (`LogicalMandatoryWithinBudget (nullCount, rowCount, budget)` / `MandatoryButHasNullsBeyondBudget (...)`) | 🔵 V2-EXTENSION | `OssysTighteningNullabilityParityTests.``5.4.β.nullability row 68`` ` (Skip) | Slice 5.4.β.nullability. V2's inlining: (a) eliminates the higher-order indirection; (b) makes evidence values available to the structured outcome (V1 collected them only as string rationales — see row 69); (c) preserves all decision outcomes. Covered by pillar 1 (data-structure-oriented over higher-order combinator). **No additional parity work needed**. |
| 69 | V1's `SignalEvaluation.Rationales : ImmutableArray<string>` accumulates flat-string rationales recursively via `CollectRationales()`; descendant rationales bubble up to root. Example strings: `"Mandatory"`, `"DataHasNulls"`, `"NullBudgetEpsilon"`, `"PrimaryKey"`. Downstream consumers parse the string list to extract per-decision evidence | V2's evidence is **typed** — each `NullabilityOutcome` variant carries its specific evidence as typed fields (`LogicalMandatoryWithinBudget` carries `(nullCount: int64 * rowCount: int64 * budget: decimal)`); `NullabilityEvidence` module + `NullabilityPass.opportunityEntry` render to diagnostic strings at the boundary only (manifest emitter / diagnostics writer) | 🔵 V2-EXTENSION | `OssysTighteningNullabilityParityTests.``5.4.β.nullability row 69`` ` (Skip) | Slice 5.4.β.nullability. Covered by pillar 1 (data-structure-oriented over string-parsing) — V2 carries typed structures end-to-end; strings emerge only at the absolute terminal boundary. Downstream consumers (emitters, canary validators) consume typed evidence directly without re-parsing string rationales. **No additional parity work needed**. |
| 70 | V1's four atomic nullability-tightening signals: `PrimaryKeySignal` (tighten if IsIdentifier; always-fire), `PhysicalNotNullSignal` (tighten if !IsNullable in physical schema), `MandatorySignal` (tighten if IsMandatory + profile evidence within budget; defers via Opportunity if beyond budget), `NullEvidenceSignal` (evidence-gating helper for Mandatory) | V2's `NullabilityRules.evaluate` linear if-elif sequence covers all 4: (1) operator-override short-circuit (matches V1 TighteningOptions.Overrides); (2) `EnforceNotNull(PrimaryKey)` if `attribute.IsPrimaryKey`; (3) `EnforceNotNull(PhysicallyNotNull)` if `not attribute.Column.IsNullable`; (4) Mandatory branch with 4 sub-cases (NoProfile / NoNulls / WithinBudget / OperatorApproval per evidence-budget logic) | 🟢 PARITY | `OssysTighteningNullabilityParityTests.``5.4.β.nullability row 70`` ` (Skip) | Slice 5.4.β.nullability. **Omnibus row** covering 4 atomic V1 signals → corresponding V2 decision branches. Same conditions, same source-of-truth (logical schema for IsMandatory; physical schema for IsNullable; profile for NullCount/RowCount). **Acceptance**: a property test asserts outcome equivalence on a shared `(attribute, profile, config)` fixture against V1's signal-tree evaluation — likely shape: `forall fixture. V1Tree.evaluate(fixture).Result = (V2Rules.evaluate(fixture).Outcome.IsTightening)`. **Cash-out**: when V1 trunk is present in the test environment, a gated parity test can run a representative fixture set through both engines and assert outcome equivalence. |
| 71 | V1's `Tightening/ColumnAnalysis.cs` + `ColumnAnalysisBuilder.cs` carry a per-column aggregate combining (nullability + FK + unique-index decisions + ChangeRisk + opportunity list); V1's evaluators populate this surface as primary emitter-facing output; consumers see one record per column with all axes joined | V2 emits **three separate per-axis decision sets** (`NullabilityDecisionSet` / `ForeignKeyDecisionSet` / `UniqueIndexDecisionSet`) as outputs of `NullabilityPass` / `ForeignKeyPass` / `UniqueIndexPass`; consumers JOIN at the boundary by SsKey when per-column aggregation is needed; V2 has no `ColumnAnalysis` analog in core | 🟡 DIVERGENCE | `OssysTighteningEvaluatorsParityTests.``5.4.γ row 71`` ` (Skip) | Slice 5.4.γ.evaluators. See `DECISIONS 2026-05-18 (slice 5.4.γ.evaluators) — Per-axis decision sets over per-column aggregation: preserving axis orthogonality`. Flows from pillar 9 (harvest-dichotomy) — the skeleton is axis-neutral; decisions layer as orthogonal overlays. **Re-open trigger**: V2 consumer (manifest emitter, operator report builder) demands canonical per-column join surface; cash-out is a thin `Projection.Targets.OperationalDiagnostics.ColumnAnalysis` projection consuming the three decision sets. **Acceptance**: per-axis property test asserts each pass output is independent (changing `Policy.Nullability` doesn't change `UniqueIndexDecisionSet` or `ForeignKeyDecisionSet` on the same Catalog). |
| 72 | `Osm.Validation/Tightening/NullabilityEvaluator.cs` (~315 LOC) — V1's decision engine consuming the signal tree + per-attribute context + override list; produces per-attribute `NullabilityDecision` records (`MakeNotNull` + `RequiresRemediation` booleans + sorted-set rationale strings + opportunity-deferred metadata) | V2's `Projection.Core/Passes/NullabilityPass.fs` (~185 LOC) consumes `Catalog × Profile × Policy.Nullability` via `Composition.fanOut` over registered interventions; produces `Lineage<Diagnostics<NullabilityDecisionSet>>` — one decision per (attribute × intervention) pair; structured outcomes via `NullabilityOutcome` DU; typed evidence per variant | 🟢 PARITY | `OssysTighteningEvaluatorsParityTests.``5.4.γ row 72`` ` (Skip) | Slice 5.4.γ.evaluators. **Per-decision cardinality identical** (V1: one per evaluated attribute; V2: one per attribute × intervention). **Per-decision semantics**: V1's 2-valued `MakeNotNull` + `RequiresRemediation` booleans collapse into V2's 3-variant `NullabilityOutcome` (per slice 5.4.β.nullability row 65 / `DECISIONS 2026-05-18 (slice 5.4.β.nullability) — Ternary outcome space`). Override-driven relaxation logic (V1 lines 139-170) maps to V2's `NullabilityTighteningConfig.AllowMandatoryRelaxation` boolean gate. **Acceptance**: gated property test runs V1's `NullabilityEvaluator.Evaluate` and V2's `NullabilityPass` against shared `(catalog, profile, policy)` fixtures; asserts decision-set equivalence (modulo ternary-outcome reshaping). |
| 73 | `Osm.Validation/Tightening/ForeignKeyEvaluator.cs` (~243 LOC) produces per-reference decisions with a 2-tuple `(CreateConstraint : bool, ScriptWithNoCheck : bool)`; **V1 known gap**: `OpportunityBuilder.Add` silently skips some failure paths (missing-target FK references; pre-session-8 refinement) | V2's `ForeignKeyPass` + `ForeignKeyRules.evaluate` produce binary outcome `ForeignKeyOutcome = EnforceConstraint | DoNotEnforce | RequireOperatorApproval` with structured evidence variants (e.g., `EnforceConstraint (ScriptWithNoCheck orphanCount)`, `DoNotEnforce MissingTarget`). V2 emits diagnostics on both failure-side AND success-with-caveat sides — every keep-reason gets a named variant + lineage event | 🔵 V2-EXTENSION | `OssysTighteningEvaluatorsParityTests.``5.4.γ row 73`` ` (Skip) | Slice 5.4.γ.evaluators. **Total decisions, named skips** discipline (`DECISIONS 2026-05-11`) operationalized at the type level — V1's silent-skip becomes V2's `MissingTarget` named variant. See `DECISIONS 2026-05-18 (slice 5.4.γ.evaluators) — Foreign-key diagnostic emission is exhaustive per keep-reason; V1 silent-skip pattern replaced with named keep-reason variants`. **Cash-out shape**: no work — V2's exhaustive emission is canonical; DECISIONS row codifies the V1-bug-corrected pattern for future strategies. |
| 74 | V1's unique-index decision machinery is **three files**: `UniqueIndexDecisionStrategy.cs` (~316 LOC; per-index decision logic) + `UniqueIndexDecisionOrchestrator.cs` (~74 LOC; walks indexes + dispatches to strategy) + `UniqueIndexEvidenceAggregator.cs` (~254 LOC; pre-computes 4 evidence sets — SingleColumnClean / SingleColumnDuplicates / CompositeClean / CompositeDuplicates by walking the model once) | V2 collapses to **two modules**: `Projection.Core/Passes/UniqueIndexPass.fs` (~155 LOC; per-index decisions via `Composition.fanOut`) + `Projection.Core/Strategies/UniqueIndexRules.fs` (decision logic). Evidence aggregation is in-line at the Rules layer (queried during decision evaluation rather than pre-staged). V1's `UniqueIndexEvidenceKey` cache-key helper has no V2 analog — V2 uses structural F# record equality on the `Index` value directly | 🟢 PARITY | `OssysTighteningEvaluatorsParityTests.``5.4.γ row 74`` ` (Skip) | Slice 5.4.γ.evaluators. The 3→2 file collapse is structural simplification — V1's `Orchestrator` is V2's `Composition.fanOut` primitive (reusable across all 3 sibling strategies per slice 5.4.β.nullability row 64); V1's `EvidenceAggregator` is V2's inline lookup. Per-index decision cardinality identical. **Acceptance**: paired with row 72's gated property-test pattern for V1↔V2 outcome equivalence. |
| 75 | `Osm.Validation/Tightening/ForeignKeyTargetIndex.cs` (~55 LOC) — stateless lookup helper wrapping `(targetEntityResolver, targetReferenceResolver)`; provides `GetTarget(entityId)` for FK target resolution; materializes a lookup table during evaluator construction | V2's `ForeignKeyRules.evaluate` performs the same lookup inline via closure over the `Catalog` value; no separate target-index type. Same lookup semantics; V1 materializes the table once, V2 computes on-demand | 🟢 PARITY | `OssysTighteningEvaluatorsParityTests.``5.4.γ row 75`` ` (Skip) | Slice 5.4.γ.evaluators. Performance parity: V2's on-demand lookup is acceptable at canary scale (≤ 300 tables; FK count linear). **Re-open trigger**: V2 benchmark surfaces FK-resolution as a hot path; cash-out: add `Catalog.foreignKeysByTargetKind : Map<SsKey, Reference list>` precomputed at construction as an A39 invariant; consumers transition from inline-closure to materialized-map. |
| 76 | `Osm.Validation/Tightening/ChangeRiskClassifier.cs` (~140 LOC) emits `RiskLevel` (Low / Moderate / High closed enum) for every decision via three classifier methods: `ForNotNull(decision, columnContext)`, `ForForeignKey(decision, referenceContext)`, `ForUniqueIndex(decision, indexContext)`. Fourth axis orthogonal to nullability/FK/uniqueness; V1 emitters use it to route warnings + escalation logic | V2's decision outcomes (`NullabilityOutcome` / `ForeignKeyOutcome` / `UniqueIndexOutcome`) carry no risk-level axis | 🟠 NOT-MAPPED | `OssysTighteningEvaluatorsParityTests.``5.4.γ row 76`` ` (Skip) | Slice 5.4.γ.evaluators. **Cash-out shape**: a thin V2 module `Projection.Targets.OperationalDiagnostics.RiskClassification` providing pure functions `riskOf : NullabilityOutcome -> RiskLevel` / `riskOf : ForeignKeyOutcome -> RiskLevel` / `riskOf : UniqueIndexOutcome -> RiskLevel` mirroring V1's classifier methods. Lives at the emission boundary, not in the Pass layer (per A36 — risk-stratification is realization-layer policy, not Pass responsibility). **Dependencies**: independent of all other slice 5.4.γ rows. **Acceptance**: parity test comparing V1's `ChangeRiskClassifier.ForX(...)` to V2's `RiskClassification.riskOf` on a shared decision corpus asserts risk-level equivalence. **Trigger**: V2 emitter (manifest emitter; operator-review report; cutover dry-run output) demands risk-stratified output. |
| 77 | `Osm.Validation/Tightening/Opportunity.cs` (~196 LOC) — per-decision opportunity record carrying Type+Title+Summary+Risk+Disposition+Category+Evidence (opaque array)+Rationales (string array)+EvidenceSummary+Columns; domain-specific fields tightly coupled to the tightening surface | V2's `Projection.Core/Diagnostics.fs` `DiagnosticEntry` (lines 60-67) — Source+Severity+Code+Message+SsKey+Metadata (string-keyed map); structurally generic across pass / adapter / emitter contexts. Risk + disposition reachable from `LineageDiagnostics.payload \|> .Decisions` (typed Outcome DUs) | 🟡 DIVERGENCE | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 77`` ` (Skip) | Slice 5.4.γ.opportunities. V2 separates concerns: lineage events carry typed Outcome DUs (structurally accessible); DiagnosticEntry carries prose narration + typed SsKey + Metadata for non-structural values. See `DECISIONS 2026-05-18 (slice 5.4.γ.opportunities) — Per-pass DiagnosticEntry contract: typed outcomes in Lineage; prose narration in Diagnostics`. **Re-open trigger**: consumer demands automated extraction of risk/disposition from the diagnostic stream (dashboard, alert routing); cash-out: lift risk + disposition into structured Metadata keys per the codified per-pass contract. |
| 78 | `Osm.Validation/Tightening/OpportunityBuilder.cs` (~62 LOC) — imperative mutable accumulator; `TryCreate` consumes decision + per-axis context, returns Opportunity (or null when no diagnostic warranted); caller accumulates into a mutable buffer | V2's per-pass `opportunityEntry` (e.g., `NullabilityPass.opportunityEntry` lines 117-170) inlines emission via the writer monad: `LineageDiagnostics.tellDiagnostics` accumulates entries chronologically (A24 — earliest-first under bind); pass returns `Lineage<Diagnostics<DecisionSet>>` (per `DECISIONS 2026-05-13 — Pass return-type codification`) | 🔵 V2-EXTENSION | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 78`` ` (Skip) | Slice 5.4.γ.opportunities. V2's monad is applicative + composable across multi-pass pipelines; per-pass inlining leverages F# compiler exhaustiveness on the Outcome DU. The writer's `tellMany` primitive supports filtering at emission time (V2 emits only on `RequireOperatorApproval` + `RelaxedUnderEvidence` for nullability; other outcomes structurally silent). Performance + ergonomics parity with V1's builder; V2 stronger via compile-checked exhaustiveness + composability. |
| 79 | `Osm.Validation/Tightening/OpportunitiesReport.cs` (~23 LOC) — top-level columnar aggregate (`Columns : ColumnOpportunityReport[]` + `Summary : ReportSummary` carrying `ColumnCount` / `ColumnsWithOpportunities` / `OpportunityMetrics`); produced by `PolicyDecisionReporter.Create` | V2 has no equivalent top-level aggregate — `Diagnostics<DecisionSet>` carries entries; pass outputs carry decisions; rollup is the consumer's responsibility | 🟠 NOT-MAPPED | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 79`` ` (Skip) | Slice 5.4.γ.opportunities. **Cash-out shape**: thin V2 module `Projection.Targets.OperationalDiagnostics.OpportunitiesReport` consuming `Diagnostics.Entries` filtered by Severity/Code + grouped by intervention; produces per-axis summary metrics. Lives at emission boundary per A36 (realization-layer policy, not Pass responsibility). **Dependencies**: independent of other slice 5.4.γ.opportunities rows; consumes the per-pass `DiagnosticEntry` stream. **Trigger**: operator dashboard demands per-axis rollup metrics OR `ManifestEmitter` surface expands to carry the rollup. **Acceptance**: parity test on a shared decision corpus asserts V1's `ReportSummary` rollups equal V2's projection from the diagnostic stream. |
| 80 | `Osm.Validation/Tightening/PolicyDecisionReporter.cs` (~326 LOC) — V1's choreographer; walks nullability + unique-index + FK decision dictionaries; constructs per-axis reports (ColumnDecisionReport / UniqueIndexDecisionReport / ForeignKeyDecisionReport); aggregates per-module rollups (ModuleDecisionRollups) | V2's choreography distributed across two layers: (a) pass drivers (`NullabilityPass.run` / `UniqueIndexPass.run` / `ForeignKeyPass.run`) produce `Lineage<Diagnostics<DecisionSet>>` in deterministic order; (b) `Projection.Targets.OperationalDiagnostics.ManifestEmitter` consumes the per-pass outputs + diagnostic stream and builds manifest JSON | 🟢 PARITY | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 80`` ` (Skip) | Slice 5.4.γ.opportunities. Output properties identical: per-axis decisions accessible by SsKey; per-module rollups computable from decision sets; T11 sibling-keyset coverage (every decision emits one Annotated event). V1 sorts at emission boundary; V2 preserves Lineage trail ordering (A24) + diagnostic entry order (chronological). Structure differs (V1 imperative walk vs V2 applicative pass + emitter) but reachability + cardinality identical. |
| 81 | `Osm.Validation/Tightening/PolicyDecisionSummaryFormatter.cs` (~439 LOC) — V1's biggest formatter; walks `ColumnDecisionReport[]`; classifies decisions into 6 buckets (Mandatory / ForeignKey / PrimaryKey / Unique / Physical / Remediation); emits prose summaries per bucket with mode-aware narration; produces `ImmutableArray<string>` console output | V2 has no equivalent summary surface — `DiagnosticEntry.Message` field carries prose per entry, but bucket aggregation + summary tables not produced anywhere | 🟠 NOT-MAPPED | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 81`` ` (Skip) | Slice 5.4.γ.opportunities. **Cash-out shape**: `SummaryFormatter` consumer taking `Diagnostics<DecisionSet> × NullabilityMode` and producing `string list` (or JSON SummaryReport) mirroring V1's per-bucket prose. Per bucket: count, entity count, key rationales, mode-aware narration. Lives at CLI surface (`Projection.Cli/Program.fs`) or operational-diagnostics emission boundary. **Dependencies**: ManifestEmitter chapter 4.4 close (shipped); SummaryFormatter consumer lands chapter 5+ (operator-facing CLI polish) or deferred to chapter 6 (post-cutover UX optimization). **Determinism**: V1's prose deterministic (no randomness; only mode/counts vary); V2's diagnostics stream deterministic (A24); projection structurally deterministic. **Trigger**: V2 CLI standardizes on summary output format before cutover OR operator workflow demands V1-compatible bucket-wise summary prose. **Acceptance**: parity test asserts V1's `FormatForConsole` output equals V2's `SummaryFormatter.format` output line-by-line on shared decision corpus. |
| 82 | `Osm.Validation/Tightening/TighteningDiagnostic.cs` (~83 LOC) — purpose-built for tightening: Code+Message+Severity+LogicalName+CanonicalModule+CanonicalSchema+PhysicalName+Candidates (duplicate-entity findings)+ResolvedByOverride. Tightening-specific; extending to new producer surfaces requires new type | V2's `Diagnostics.DiagnosticEntry` (in `Projection.Core/Diagnostics.fs`) — generic across all producer contexts: Source names producer (`adapter:<name>` / `pass:<name>` / `emitter:<name>`) + Severity + Code (dot-separated routing prefix per `DECISIONS 2026-05-11`) + Message + SsKey (typed identity) + Metadata (Map<string, string> for non-structural values). One type covers tightening passes + adapters + emitters + future diagnostic sources | 🔵 V2-EXTENSION | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 82`` ` (Skip) | Slice 5.4.γ.opportunities. V1's mandatory-null-conflict diagnostic (`TighteningDiagnostic.CreateMandatoryNullConflict` lines 24-46) constructs tightening-specific record with prose Message + remediation query; V2's `NullabilityPass.opportunityEntry` emits `DiagnosticEntry` with typed Outcome (RelaxedUnderEvidence / RequireOperatorApproval) + typed SsKey pointing to attribute + Metadata carrying interventionId + numeric thresholds. **V2's generic shape is structurally more reusable**; same operator-visible information; better composability across producer surfaces. |
| 83 | `Osm.Validation/Tightening/RemediationQueryBuilder.cs` (~73 LOC) — emits remediation SQL (3-option UPDATE/DELETE/SELECT) operators run to fix data before tightening. V1 couples diagnostic production to remediation-SQL generation — `ColumnDecisionAggregator` line 116 calls `TighteningDiagnostic.CreateMandatoryNullConflict` which embeds the query | V2 has NO `RemediationEmitter` today — `Projection.Targets.OperationalDiagnostics.ManifestEmitter.fs` shows the PreRemediation manifest field remains empty per `V2_DRIVER.md §154` (RemediationEmitter explicitly scheduled as chapter 5+ deferred deliverable) | 🟠 NOT-MAPPED | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 83`` ` (Skip) | Slice 5.4.γ.opportunities. **Cash-out shape**: `RemediationEmitter` module as sibling to `ManifestEmitter`, taking `Diagnostics<DecisionSet>` and producing `manifest.remediation.sql` with per-diagnostic UPDATE/DELETE/SELECT options. SQL deterministic (SsKey + decision outcome determine the fix); ordering chronological per A24-equivalent. Lives in `Projection.Targets.OperationalDiagnostics/`. **Risk**: if chapter 5 doesn't ship before cutover, V2's operator UX for mandatory-null-conflict remediation degrades — operators see diagnostic Message but must hand-write the UPDATE query (V1 provided template). **Mitigation**: `DiagnosticEntry.Message` + Metadata carry full context (null count, budget, intervention ID); operators can infer fix semantics. Fallback remediation doc would substitute. **Trigger**: chapter 5+ RemediationEmitter slice opens OR cutover dry-run discovers mandatory-null-conflict cases requiring SQL templates. **Acceptance**: a round-trip test asserts V2's emitted UPDATE/DELETE/SELECT options apply cleanly + produce the expected post-remediation state. |
| 84 | `Osm.Validation/Tightening/TighteningRationales.cs` (~31 LOC) — static module with ~30 `public const string` rationale labels (PrimaryKey, PhysicalNotNull, UniqueNoNulls, DataNoNulls, DataHasNulls, Mandatory, DeleteRuleIgnore, ProfileMissing, etc.). Decisions carry `ImmutableArray<string> Rationales`; `PolicyDecisionSummaryFormatter` pattern-matches on these strings via `HasRationale` helper for bucket classification | V2's typed Outcome DUs (`NullabilityOutcome` / `UniqueIndexOutcome` / `ForeignKeyOutcome`) carry typed Evidence per variant payload (null count, row count, budget); rationale rendering deferred to `DiagnosticEntry.Message` field (prose) or Metadata map (structured key-value) | ⚫ V1-SUNSET | `OssysTighteningOpportunitiesParityTests.``5.4.γ row 84`` ` (Skip) | Slice 5.4.γ.opportunities. **Sunset rationale**: V1's string-based rationale surface was the UI anchor for operator decision filtering. V2's lineage events carry typed outcomes; per-pass diagnostics carry Severity + Code (structured filters via dot-prefix per `DECISIONS 2026-05-11`); the string rationale is no longer load-bearing. **Migration impact**: `PolicyDecisionSummaryFormatter`'s bucket classification (V1's HasRationale string-match) becomes an outcome DU pattern-match in the V2 SummaryFormatter consumer (matrix row 81). The `TighteningRationales` constants retire; Evidence payloads on Outcome variants carry the semantic content. Covered by slice 5.4.β.nullability row 69 at the per-pass level; this row records the module-level sunset. |
| 85 | `Pipeline/Profiling/SqlDataProfiler.CaptureAsync()` — live-probe orchestration entry; collects tables, loads metadata, builds plans, executes queries in parallel; concrete `IDataProfiler` implementation for single-environment live SQL Server | V2's `Profile` aggregate is OUTPUT contract (`Projection.Core/Profile.fs`); live-probe acquisition DEFERRED. V2 ships `ReadSide` (catalog structure only) + `ProfileSnapshot.attach` (V1-JSON adapter); no live-SQL probe orchestration | 🟠 NOT-MAPPED | `OssysProfilingAcquisitionParityTests.``5.4.δ row 85`` ` (Skip) | Slice 5.4.δ.profiling. **Cash-out shape**: `LiveProfiler` adapter module in `Projection.Adapters.Sql` carrying `readProfileAsync : SqlConnection -> Catalog -> Task<Result<Profile>>`. Dependencies: requires per-probe query builders (rows 86-89) + sampling policy (row 90). **Trigger**: chapter 4.1.B § 4 or later — data-triumvirate slice calls for live SQL Server profile capture. |
| 86 | `Pipeline/Profiling/NullCountQueryBuilder.BuildCommandText()` — emits `SELECT SUM(CASE WHEN [col] IS NULL ...)` over sampled rows via `TOP (@SampleSize)` or full scan; CTE Source + per-column UNION ALL with grouped null counts | V2's `Profile.Columns` carries `NullCount : int64` + `NullCountProbeStatus : ProbeStatus`; IR fully carried; acquisition absent | 🟠 NOT-MAPPED | `OssysProfilingAcquisitionParityTests.``5.4.δ row 86`` ` (Skip) | Slice 5.4.δ.profiling. **Cash-out shape**: F# module `NullCountProbe` with `queryText : schema:string -> table:string -> columns:string[] -> sampling:int option -> string` + `parseResult : seq<string * int64> -> Map<string, int64>`. Counts are exact int64; no decimal needed per `DECISIONS 2026-05-13`. **Trigger**: same as row 85. |
| 87 | `Pipeline/Profiling/UniqueCandidateQueryBuilder.BuildCommandText()` — per-candidate uniqueness check via `SELECT CandidateId, CASE WHEN EXISTS (GROUP BY ... HAVING COUNT(*) > 1) ...`; composite + single-column variants share builder | V2's `Profile.UniqueCandidates` + `Profile.CompositeUniqueCandidates` carry the IR (V2 added `ProbeStatus` to composite — V1 lacked it); acquisition absent | 🟠 NOT-MAPPED | `OssysProfilingAcquisitionParityTests.``5.4.δ row 87`` ` (Skip) | Slice 5.4.δ.profiling. **Cash-out shape**: F# module `UniqueCandidateProbe` with `queryText` + `parseResult` signatures. V1's serialized `Key` maps to V2's `SsKey`; coordinate-to-SsKey resolution at adapter boundary (same pattern as `ProfileSnapshot`). **Trigger**: same as row 85. |
| 88 | `Pipeline/Profiling/ForeignKeyProbeQueryBuilder` two methods: (a) `BuildRealityCommandText()` — per-FK orphan count via `LEFT JOIN target WHERE source.col IS NOT NULL AND target.col IS NULL`; (b) `BuildMetadataCommandText()` — queries `sys.foreign_keys` for TRUSTED / NO CHECK flags | V2's `Profile.ForeignKeys` carries `HasOrphan` + `OrphanCount` + `IsNoCheck` + `ProbeStatus`. **Metadata probe shipped** (`Projection.Adapters.Sql/ReadSide.fs §227-278 readForeignKeys` ✓); orphan-count probe absent | 🟠 NOT-MAPPED (partial — metadata shipped) | `OssysProfilingAcquisitionParityTests.``5.4.δ row 88`` ` (Skip) | Slice 5.4.δ.profiling. **Cash-out shape**: F# module `ForeignKeyProbe` with `realityQueryText` + `parseResult` signatures. Paired with row 89 (orphan sample). **Trigger**: same as row 85. |
| 89 | `Pipeline/Profiling/ForeignKeyOrphanSampleQueryBuilder.BuildCommandText()` — `SELECT TOP (@SampleLimit)` of orphan rows with PK identifiers + orphan value + `TotalOrphans` count; deterministic sampling for operator diagnostics. **Distinct from orphan COUNT (row 88)** | V2's `Profile.ForeignKeys` has NO `OrphanSample` field; IR carries count but NOT row identifiers | 🟠 NOT-MAPPED | `OssysProfilingAcquisitionParityTests.``5.4.δ row 89`` ` (Skip) | Slice 5.4.δ.profiling. **Rationale for absence**: V1's orphan sample is operational diagnostics, not data-intent evidence. Per pillar 9, Profile carries data-intent only; operational samples land in `Diagnostics<'output>`. **Cash-out shape**: add `OrphanSamples : ForeignKeyOrphanSample option` to `Profile.ForeignKeys` OR parallel `Map<SsKey, ForeignKeyOrphanSample>`; record carries `PrimaryKeyColumns: SsKey[]; ForeignKeyValue: SqlLiteral; SampleRows: Row[]; TotalOrphans: int64`. **Trigger**: chapter 4.2 (User FK reflow) or diagnostics writer needs per-FK orphan rows. |
| 90 | `Pipeline/Profiling/TableSamplingPolicy.ShouldSample()` + `GetSampleSize()` — per-table heuristic deciding sample (row count > threshold) at size (min of sample-size config, row count, max-rows-per-table); configuration via `SqlProfilerOptions.Sampling` | V2's `Profile` carries `ProbeStatus.SampleSize : int64` (what sample size WAS used) but no sampling POLICY in Core; `ReadSide` does full-table catalog read | 🟡 DIVERGENCE | `OssysProfilingAcquisitionParityTests.``5.4.δ row 90`` ` (Skip) | Slice 5.4.δ.profiling. See `DECISIONS 2026-05-18 (slice 5.4.δ.profiling) — Sampling policy is operator intent; lives in the orchestrator, not in Profile IR`. **Trade-off**: V2 loses V1's heuristic constraint-checking surface; policy lives in Pipeline layer's Config module per run; Profile is witness-only. **Re-open trigger**: LiveProfiler cash-out (row 85) lands — sampling heuristic ports as private helper in the adapter. |
| 91 | `Pipeline/Profiling/ProfilingPlans.cs` + `ProfilingPlanBuilder.cs` — explicit per-table plans carrying probe declarations; pre-probe orchestration: resolve physical coordinates, validate against metadata, emit structured probe-execution plans | V2's `Catalog` + `Policy` together specify what to probe; `Profile` is output; **no explicit plan structure**. Plan-building is implicit at adapter time; adapter applies real-time filtering | 🔵 V2-EXTENSION | `OssysProfilingAcquisitionParityTests.``5.4.δ row 91`` ` (Skip) | Slice 5.4.δ.profiling. V2 trades V1's explicitness for flexibility. **Re-open trigger**: observability becomes a requirement (operator wants to see plan per table) → future `ProbeSpec` IR added to Config or Diagnostics. Per `DECISIONS 2026-05-07 — IR grows under evidence`. Today deferred. |
| 92 | `Pipeline/Profiling/MultiTargetSqlDataProfiler.CaptureAsync()` — orchestrates parallel profile captures across dev/uat/prod; merges via worst-case aggregation (`MergeSnapshots`); consensus thresholding | V2's `Profile` is environment-agnostic; multi-environment aggregation NOT shipped; `Profile.empty` exists but no `Profile.union` / `Profile.merge` | 🟠 NOT-MAPPED | `OssysProfilingAcquisitionParityTests.``5.4.δ row 92`` ` (Skip) | Slice 5.4.δ.profiling. **Rationale**: multi-env profiling is operator-intent (policy: which environments to poll, how to merge); not data-intent per A34. **Cash-out shape**: `Profile.merge : Profile -> Profile -> Profile` + property test (commutative + associative); consensus thresholding lands in orchestrator. Worst-case aggregation ports V1's `AggregateProbeStatus` + `AggregateNullRowSample`. **Trigger**: data-triumvirate work calls for multi-environment risk scoring (chapter 4.1.B or 4.2). |
| 93 | `Pipeline/Profiling/ProfilingSnapshotNormalizer.cs` + `ProfilingStandardizationValidator.cs` — runtime invariant guards (row counts ≥ null counts; null percentages bounded; composite keys >0 columns) | V2's smart constructors (`ColumnProfile.create`, `UniqueCandidateProfile.create`, `NumericDistribution.create`) enforce invariants structurally via `Result<'a>`; `ColumnProfile.create` rejects `nullCount > rowCount`; degenerate cases accepted (Min=Max valid; all-null column valid) | 🟢 PARITY (refined) | `OssysProfilingAcquisitionParityTests.``5.4.δ row 93`` ` (Skip) | Slice 5.4.δ.profiling. **Reconciliation**: V1 runtime guards (catch-then-report) → V2 by-construction guards (reject invalid by type) per `AXIOMS.md` structural-commitment-via-construction-validation. **No cash-out needed** — discipline already applied. |
| 94 | `Pipeline/Profiling/FixtureDataProfiler` — offline-test fixture implementation of `IDataProfiler`; deserializes JSON `ProfileSnapshot` into in-memory value | V2's offline fixture path is three-part composition: (1) `Catalog` via `ReadSide` on fixture DB; (2) `Profile.empty` for skeleton-only tests; (3) `ProfileSnapshot.attach` to parse V1-format JSON | 🔵 V2-EXTENSION | `OssysProfilingAcquisitionParityTests.``5.4.δ row 94`` ` (Skip) | Slice 5.4.δ.profiling. V2 decoupled Profile input from Catalog — single fixture Catalog can be tested against multiple Profile evidence sets via composition of `Profile.empty` / `ProfileSnapshot.attach` / `ProfileStatistics.attach`. Tests use `let fixture = ProfileSnapshot.attach catalog jsonText` inline. **Documentation cash-out**: CLAUDE.md / PLAYBOOK.md section naming the composition pattern; not a code debt. |
| 95 | `Osm.Emission/SsdtManifest.cs` (~91 LOC) top-level shape: 8 fields (Tables, Options, PolicySummary, Emission, PreRemediation, Coverage, PredicateCoverage, Unsupported) | `Projection.Targets.OperationalDiagnostics.ManifestEmitter.fs` Manifest record: 6 fields (Tables, EmitterVersion, RegistryDigest, Coverage, PredicateCoverage, Unsupported); PreRemediation emitted as `[]` per V2_DRIVER §154; Options + PolicySummary deferred; V2 adds **EmitterVersion** (versioning stamp) + **RegistryDigest** (chapter A.4.7' slice ζ) | 🟡 DIVERGENCE | `OssysSsdtManifestParityTests.``5.5.α row 95`` ` (Skip) | Slice 5.5.α.manifest. See `DECISIONS 2026-05-18 (slice 5.5.α.manifest) — V1-differential walk: manifest scope-reduction with V2-extension fields`. **Documented structural reduction, not parity loss** — V1's manifest was a union of multiple semantic layers; V2's manifest is catalog-only. **Cash-out for Options + PolicySummary**: chapters 4.5+ when policy/profile-level metadata surfaces have V2 consumers. |
| 96 | V1's CoverageBreakdown rounding contract: `Math.Round(value, 2, MidpointRounding.AwayFromZero)`; total=0→100%; emitted=0→0% | V2's `Coverage.compute` mirrors line-for-line: `System.Math.Round(value, 2, System.MidpointRounding.AwayFromZero)`; total ≤ 0 → 100m; emitted ≤ 0 → 0m (ManifestEmitter.fs:78-83) | 🟢 PARITY | `OssysSsdtManifestParityTests.``5.5.α row 96`` ` (Skip) | Slice 5.5.α.manifest. Chapter 4.4 slice α confirmed. **No additional parity work needed** — exact rounding-contract match. |
| 97 | V1's `SsdtCoverageSummary(Tables, Columns, Constraints)` three-axis shape, each `CoverageBreakdown` | V2's `CoverageSummary = { Tables; Columns; Constraints }` F# record; `CoverageSummary.createComplete` mirrors V1's `SsdtCoverageSummary.CreateComplete` | 🟢 PARITY | `OssysSsdtManifestParityTests.``5.5.α row 97`` ` (Skip) | Slice 5.5.α.manifest. Chapter 4.4 slice α confirmed. **No additional parity work needed**. |
| 98 | `Osm.Emission/SsdtManifest.cs` `TableManifestEntry` 7 fields: Module, Schema, Table, TableFile, **Indexes** (list<string> of index names), **ForeignKeys** (list<string> of FK names), **IncludesExtendedProperties** (bool) | V2's TableManifestEntry 6 fields: Module, Schema, Table, TableFile, **IndexCount** (int), **ForeignKeyCount** (int) — name lists replaced with counts; IncludesExtendedProperties dropped | 🟡 DIVERGENCE | `OssysSsdtManifestParityTests.``5.5.α row 98`` ` (Skip) | Slice 5.5.α.manifest. See `DECISIONS 2026-05-18 (slice 5.5.α.manifest) — TableManifestEntry: counts over name-lists`. **Operationally transparent** — downstream consumers read counts for summary statistics, not names. JSON shape differs (V1: `{indexes: ["IX_A", "IX_B"]}`; V2: `{indexCount: 2}`). **Cash-out for IncludesExtendedProperties**: deferred to chapter A.0' extended-property emission completion; V2 carries the data but doesn't surface in per-table manifest entry. |
| 99 | V1's `SsdtPredicateCoverage(Tables: PredicateCoverageEntry[], PredicateCounts: dict<string, int>)` two-section shape | V2's `PredicateCoverage = { Tables: PredicateCoverageEntry list; PredicateCounts: Map<PredicateName, int> }`; typed `PredicateName` DU (16 variants per chapter 4.4 slice β) instead of string keys | 🟢 PARITY | `OssysSsdtManifestParityTests.``5.5.α row 99`` ` (Skip) | Slice 5.5.α.manifest. Chapter 4.4 slice β confirmed. Type-safety improvement via PredicateName DU; rendering at JSON boundary via `PredicateName.toString`. **No additional parity work needed**. |
| 100 | V1's `PredicateCoverageEntry(Module, Schema, Table, Predicates: list<string>)` per-entry shape | V2's `{ Module: string; Schema: string; Table: string; Predicates: PredicateName list }`; typed `PredicateName` variants | 🟢 PARITY | `OssysSsdtManifestParityTests.``5.5.α row 100`` ` (Skip) | Slice 5.5.α.manifest. Chapter 4.4 slice β confirmed. **No additional parity work needed**. |
| 101 | V1 emits `predicateCounts` as JSON dict `{"HasTrigger": 5, ...}` — object-property order is parser-implementation-specific | V2 emits as sorted-by-name array `[{"name": "HasCheckConstraint", "count": 2}, ...]` per chapter 4.4 open Q2 (resolved at close); ManifestEmitter.fs:226-230 + 650-654 | 🟡 DIVERGENCE | `OssysSsdtManifestParityTests.``5.5.α row 101`` ` (Skip) | Slice 5.5.α.manifest. **Rationale**: T1 byte-determinism — dict order is insertion-dependent in some JSON parsers; array order is sortable + deterministic. V2's sort order: canonical `PredicateName.all` enumeration (alphabetic). Covered by chapter 4.4 close DECISIONS row on byte-determinism. **Cash-out** (if V1-byte-equality demanded): Tolerance variant `Tolerance.PredicateCountsJsonShapeDivergence` would mark the difference; consumers either accept V2 array shape OR serializer mode flips to V1-dict shape with key-sorted serialization. No current consumer demands it. |
| 102 | V1's PreRemediation carries actual `List<PreRemediationManifestEntry>` with (Module, Table, TableFile, Hash) tuples — remediation entries accumulated during emission; V1's engine defers operator action post-deploy | V2 emits `"preRemediation": []` (empty array) unconditionally per V2_DRIVER §154; ManifestBuilder's nullable parameter `IReadOnlyList<PreRemediationManifestEntry>?` (line 16) mirrors V2's deferred-to-chapter gating | 🟢 PARITY (documented deferral) | `OssysSsdtManifestParityTests.``5.5.α row 102`` ` (Skip) | Slice 5.5.α.manifest. **Correct parity scoping** — V2's manifest version 1 documents this as a chapter-4-close deliverable; upstream chapters don't populate remediation; chapter 5's RemediationEmitter ships that feature (paired with matrix row 83). **Acceptance**: when RemediationEmitter ships, integration test asserts V2's PreRemediation matches V1's shape on a representative deployment scenario. |
| 103 | V1's `Osm.Emission/ManifestBuilder.Build` (~113 LOC) orchestrates: scan table snapshots, extract emission metadata, build `TableManifestEntry` list; optionally wrap PolicyDecisionReport into SsdtPolicySummary; pass-through Coverage/PredicateCoverage/Unsupported parameters (nullable with defaults); emit SsdtManifest record | V2's `ManifestEmitter.buildWith(registry, catalog)` computes entries via `catalog.Modules |> List.collect (fun m -> m.Kinds |> List.map ...)`; computes Coverage / PredicateCoverage / Unsupported in-line; threads registry digest; emits Manifest record | 🟢 PARITY | `OssysSsdtManifestParityTests.``5.5.α row 103`` ` (Skip) | Slice 5.5.α.manifest. **Same orchestration family; scoped differently per V2's architecture** (V1 caller provides manifests as parameters; V2 computes them per A18 amended — catalog-only, no policy). Chapter 4.4 close confirmed. |
| 104 | JSON serialization property naming: V1's C# record fields (PascalCase) serialize as PascalCase via System.Text.Json default; uses JsonPropertyName attributes to emit camelCase at JSON boundary | V2 manually builds JsonObject with camelCase keys (emitter, version, registry, tables, coverage, predicateCoverage, unsupported, preRemediation, indexCount, foreignKeyCount); ManifestEmitter.fs:614-693 | 🟢 PARITY | `OssysSsdtManifestParityTests.``5.5.α row 104`` ` (Skip) | Slice 5.5.α.manifest. **Both emit camelCase at JSON boundary** (operator-facing manifest.json file). **The JSON shape on disk is identical** modulo documented divergences (rows 95 + 98 + 101). |
| 105 | `Osm.Cli/Commands/BuildSsdtCommandFactory.cs` build-ssdt verb — consumes V1 JSON model + profile + filters → SSDT artifact bundle | `Projection.Cli/Program.fs` `projection emit <input> <out>` (lines 106-129) — V1 JSON model + writes SSDT artifacts | 🟢 PARITY | `OssysCliVerbsParityTests.``5.7.α row 105`` ` (Skip) | Slice 5.7.α.cli. **Primary operator workflow** maps directly. V1's `--open-report` extension covered at row 115. |
| 106 | `Osm.Cli/Commands/FullExportCommandFactory.cs` full-export verb — orchestrates extraction → profiling → emission → load-harness replay in one verb | V2 deliberately decomposes: `emit` is pure projection; deploy + harness replay are external orchestration | 🟡 DIVERGENCE | `OssysCliVerbsParityTests.``5.7.α row 106`` ` (Skip) | Slice 5.7.α.cli. See `DECISIONS 2026-05-18 (slice 5.7.α.cli) — V2 CLI deliberately minimal: production-deferred posture; reopen per verb on operator demand`. **Rationale**: A36 (bulk-vs-incremental is realization policy); harness belongs outside Π. **Cash-out**: future `osm batch-replay` verb on operator demand; today operators chain `osm emit && osm deploy && <run-harness>` explicitly. |
| 107 | `Osm.Cli/Commands/ExtractModelCommandFactory.cs` extract-model verb — OSSYS connection + filters + SQL overrides → V1 JSON model file | V2 assumes model pre-extracted (config-driven input path); no standalone extract verb at launch | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 107`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: `osm extract <connection-string> --modules <csv> --out <path>` wrapping `Projection.Adapters.OssysSql.MetadataSnapshotRunner.runAsync` + writing Catalog as JSON. ~50 LOC. **Dependencies**: chapter 5.1.γ production wiring (rows 32-36). **Trigger**: V2 production CLI surface ships; operators need extraction as CLI step (today extraction is V1-owned during R6 split-brain cutover). |
| 108 | `Osm.Cli/Commands/ProfileCommandFactory.cs` profile verb — V1 JSON model + SQL connection → profile snapshot JSON | V2 embeds profiling logic in Π via LiveProfiler (row 85 cash-out); standalone verb deferred | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 108`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: `osm profile <input-model> <connection-string> --out <profile.json>` wrapping LiveProfiler adapter + writing Profile as V1-compatible JSON. **Dependencies**: row 85 (LiveProfiler ships) — wrapper is ~30 LOC. **Trigger**: operators demand profile-only execution for diagnostic/tuning iteration before full emit. |
| 109 | `Osm.Cli/Commands/DmmCompareCommandFactory.cs` dmm-compare verb — SSDT bundle + DMM baseline → comparison report | V2's DMM lens machinery sunset per slice 5.8.α; V2's comparison axis is PhysicalSchema round-trip (canary; matrix row 44) | ⚫ V1-SUNSET | `OssysCliVerbsParityTests.``5.7.α row 109`` ` (Skip) | Slice 5.7.α.cli. **Future replacement** (operator concept reserved at row 41): `projection compare <left> <right>` with closed-DU `DiffSource = LiveDb | SsdtProject | DacpacFile | RawSql`; ships when operator demand for ad-hoc schema-diff outside canary's source-vs-deployed-target scope materializes. |
| 110 | `Osm.Cli/Commands/InspectCommandFactory.cs` inspect verb — V1 JSON model file → model summary (module/entity/attribute counts) | V2 omits dedicated inspect verb at launch — surfaces model validation via config-validation errors at emit time | 🟡 DIVERGENCE | `OssysCliVerbsParityTests.``5.7.α row 110`` ` (Skip) | Slice 5.7.α.cli. **Rationale**: V2 assumes models pre-validated by config; V2 validates file existence + parseability at emit time. **Cash-out**: `osm validate <model.json>` or `osm validate --config <path>` verb wrapping config validation + model ingestion; ~30 LOC. **Trigger**: operators demand pre-flight validation separate from full emit (e.g., model-file health checks in CI). |
| 111 | `Osm.Cli/Commands/AnalyzeCommandFactory.cs` analyze verb — V1 JSON model + profile snapshot → tightening analysis report (columns / indexes / FKs to tighten + remediation summary) | V2's tightening decisions embedded in pipeline; analysis output writes at emit time (decision log + manifest) | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 111`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: `osm analyze <model.json> [--profile <path>] [--policy <path>] --out <report-dir>` wrapping PassDriver + decision-log writer; emits three DecisionSets + diagnostics + manifest WITHOUT SSDT artifacts. Reuses NullabilityRules/UniqueIndexRules/ForeignKeyRules unchanged. ~300 LOC. **Dependencies**: SummaryFormatter consumer (row 81 cash-out). **Trigger**: operators iterate on tightening policy before emission (typical pre-cutover workflow). |
| 112 | `Osm.Cli/Commands/PolicyCommandFactory.cs` policy explain subcommand — policy decision report JSON + filter flags → formatted report (table or JSON) | V2's policy surface is emit-time decision log + manifest; post-analysis inspection deferred to external tooling | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 112`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: lightweight `osm policy explain <decision-log.json> [--axis nullability\|fk\|unique] [--format table\|json]` wrapping PolicyDecisionReport deserialization + TableFormatter (reuses V1's PolicyCommandFactory.EmitTableOutput pattern); ~300 LOC. **Trigger**: operators request CLI-based policy drill-down (typical for cutover dry-run reviews). Belongs alongside SummaryFormatter consumer (row 81 cash-out). |
| 113 | `Osm.Cli/Commands/UatUsersCommand.cs` + `UatUsersCommandFactory.cs` uat-users verb — V1 model + QA/UAT inventory CSVs + user-matching config → UAT user-remapping artifacts (SQL + verification report) | V2 defers to post-deploy phase; consumer-side IR shipped via `UserRemap.fs` + `UserFkReflowPass.fs` (chapter 4.2) | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 113`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: `osm uat-users <model.json> <inventory-config.json> --out <dir>` with pluggable matching strategies; ~1500 LOC (CSV ingestion + multi-environment orchestration; core logic reuses V2's existing UserRemap surface). **Trigger**: chapter 4.2 (consumer-side reflow shipped) + cutover enters UAT phase. Paired with chapter 4.2 + 4.3 deliverables. |
| 114 | `Osm.Cli/Commands/VerifyDataCommandFactory.cs` verify-data verb — V1 model + source DB connection + target DB connection → data integrity verification report (row-count, NULL-count, warning summaries) | V2's canary covers structural equivalence; post-deploy data integrity is separate phase | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 114`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: `osm verify-data <source-conn> <target-conn> <manifest-path> --report-out <path>` wrapping a `BasicDataIntegrityChecker` adapter (port of V1's logic); compares per-table row-counts + per-column null-counts; emits operator report. ~200 LOC. **Dependencies**: row 85 (LiveProfiler adapter) for per-column probe machinery. **Trigger**: chapter 4.3+ (post-deploy verification phase). |
| 115 | `Osm.Cli/Commands/OpenReportVerbExtension.cs` + `PipelineReportLauncher.cs` — `--open-report` extension on build-ssdt + full-export; uses ShellExecute to open SSMS / Excel with .dacpac context | V2 omits — operator workflow is `osm emit --config <path> && osm deploy <manifest>` (sequential) + manual report inspection | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 115`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: inline `--open-report` option on `osm deploy <manifest> --open-report` wrapping V1's PipelineReportLauncher pattern; ~150 LOC. No dedicated verb. **Dependencies**: ShellExecute is OS-specific — V2 needs cross-platform shim (`xdg-open` on Linux; `open` on macOS; `start` on Windows). **Trigger**: operators demand integrated report-launching at deploy-time. Not blocking cutover. |
| 116 | `Osm.Cli/Commands/Binders/*.cs` — 7 specialized binders (`ModuleFilterOptionBinder`, `CacheOptionBinder`, `SqlOptionBinder`, `TighteningOptionBinder`, `SchemaApplyOptionBinder`, `UatUsersOptionBinder`, `IVerbOptionExtension`) + `VerbOptionRegistry` + `VerbOptionsBuilder`; verbs chain via `.UseModuleFilter().UseSql().UseTightening()` | V2's `Projection.Cli/Program.fs` uses raw `argv` pattern matching (main argv switch) | 🟡 DIVERGENCE | `OssysCliVerbsParityTests.``5.7.α row 116`` ` (Skip) | Slice 5.7.α.cli. V2's posture: defer complex binding to config files; CLI takes essential switches only. **Trade-off**: V1's CLI is more powerful; V2's is simpler + config-secondary. **Re-open trigger**: future CLI expansion demands strongly-typed composition; cash-out: carbon-copy V1's binder patterns to F# (~500 LOC port of `ModuleFilterOptionBinder` + `VerbOptionsBuilder` + per-axis binders). |
| 117 | `Osm.Cli/CliGlobalOptions.cs` — cross-verb config (config path; max parallelism) dependency-injected into every verb factory | V2 has no global options — argv switch per-verb; CLI flags only override config defaults | 🟡 DIVERGENCE | `OssysCliVerbsParityTests.``5.7.α row 117`` ` (Skip) | Slice 5.7.α.cli. **Rationale**: V2 is config-driven (unified config JSON carries cross-verb defaults; CLI flags are per-invocation overrides only). **Re-open trigger**: operators need CLI-level global flags (`--log-level`, `--verbose`, `--quiet`); cash-out: add `CliGlobalOptions` record to V2's Program.fs; parse before verb dispatch; thread into each verb's runner. ~50 LOC. |
| 118 | `Osm.Cli/IProgressRunner.cs` + `SpectreConsoleProgressService.cs` — Spectre.Console TUI integration; wraps every verb run in progress bar (task descriptions, % complete, ETA) | V2 has no progress surface at launch | 🟠 NOT-MAPPED | `OssysCliVerbsParityTests.``5.7.α row 118`` ` (Skip) | Slice 5.7.α.cli. **Cash-out shape**: hook V2's existing `Projection.Core/Bench.fs` iterator-logging primitives into a Spectre.Console renderer at CLI boundary. `SpectreProgressAdapter : IProgressRunner` wrapping `Bench.snapshot()` samples. ~200 LOC. **Dependencies**: paired with row 36 (extraction progress). Per `DECISIONS 2026-05-23 — Iterator-logging is a first-class outcome over time` — V2's Bench surface is already operator-visible; this row adds the TUI rendering. **Trigger**: chapter 5.1 (production CLI wiring) + operator feedback on visibility during long-running operations. |
| 119 | `Osm.Cli/CommandConsole.cs` abstraction (Write / WriteErrorLine / WriteTable / WriteErrors) — centralized error formatting | V2's `Projection.Cli/Program.fs` uses direct `Console.Error.WriteLine` per error (structured per-line writes per chapter 3.5 audit) | 🟡 DIVERGENCE | `OssysCliVerbsParityTests.``5.7.α row 119`` ` (Skip) | Slice 5.7.α.cli. **V2 is more testable** (output deterministic; no formatting class to mock). Both approaches sound; deliberate choice — V2's pillar 1 (data-structure-oriented; strings emerge at terminal boundary only). No DECISIONS row needed — covered by chapter 3.5 audit + pillar 1. |
| 120 | `Osm.Smo/SmoEntityEmitter.cs` + 44-file SMO scripter cluster — emission via `Microsoft.SqlServer.Management.Smo` (mutable Table/Column/Index/ForeignKey objects; `Table.Script()` to render text) | `Projection.Targets.SSDT/SsdtDdlEmitter.fs` + `ScriptDomBuild.fs` — emission via `Microsoft.SqlServer.TransactSql.ScriptDom` typed-AST builders; statements rendered via `Sql160ScriptGenerator` with pinned options | 🟡 DIVERGENCE | `OssysSmoEmissionParityTests.``5.3.α row 120`` ` (Skip) | Slice 5.3.α.smo. See `DECISIONS 2026-05-18 (slice 5.3.α.smo) — Schema emission via ScriptDom typed-AST over SMO scripter`. **Foundational architecture choice** codified in chapter 4.1.A close arc + `DECISIONS 2026-05-10 — Text-builder-as-first-instinct discipline`. SMO is reverse-engineered library with inconsistent script output; ScriptDom is Microsoft's canonical typed-AST grammar (pillar 7 gold-standard library). |
| 121 | `Osm.Smo/CreateTableStatementBuilder.cs` (~490 LOC) — `BuildCreateTableStatement` constructs `CreateTableStatement` (columns inline; PK logic per first-ordinal; FK constraints inline; NOCHECK deferred to ALTER TABLE statements) | `Projection.Targets.SSDT/ScriptDomBuild.fs` `buildCreateTable` (lines 224-242) — mirrors shape: columns inline, PK logic, FK constraints, NOCHECK deferred | 🟢 PARITY | `OssysSmoEmissionParityTests.``5.3.α row 121`` ` (Skip) | Slice 5.3.α.smo. PK naming convention `PK_<schema>_<table>` per chapter 3.7 slice β. Canary diff on 300-table schema shows zero delta modulo Tolerance.NormalizeWhitespace. Per-axis deferrals (single-column PK inline optimization; column defaults; CHECK constraints; computed columns) noted at row 131. |
| 122 | `Osm.Smo/IndexScriptBuilder.cs` (~452 LOC) — `BuildCreateIndexStatement` handles keyed + included columns + sort order + metadata (fillfactor/padindex/ignoredupkey/compression/filegroup/partition scheme) + filter via `ParsePredicate` | `Projection.Targets.SSDT/ScriptDomBuild.fs` `buildCreateIndex` (chapter 4.5/4.8/4.9 slices) covers same axes including IndexColumnDirection (chapter 4.9 slice γ); upgraded to TSql160Parser (SQL Server 2022); filter-parse failures surface as Diagnostics (V1 silent) | 🟢 PARITY (with deferred axes) | `OssysSmoEmissionParityTests.``5.3.α row 122`` ` (Skip) | Slice 5.3.α.smo. **Deferred V2 axes**: IgnoreDupKey (V1 lines 215-221); DataCompression with partition-range collapse (V1 lines 259-301); FileGroup/PartitionScheme dataspace (V1 lines 322-374) — all paired with matrix rows 55+56. IndexDef IR fields exist; emit layer deferred per IR-grows-under-evidence. Critical axes (columns, sort, INCLUDE, WHERE clause, lock options) ship. |
| 123 | V1 FK emission spans 5 files: `SmoForeignKeyBuilder` (~111 LOC) + `ForeignKeyEvidenceResolver` (5-phase rule-matching) + `ForeignKeyNameFactory` + `ForeignKeyColumnNormalizer` + `ForeignKeyFallbackFactory` | V2 distributes: emission via `ScriptDomBuild.buildForeignKeyConstraint` (inline in buildCreateTable); evidence resolution lifts to `Projection.Core.Passes.ForeignKeyPass` + `ForeignKeyRules` (strategized; per slice 5.4.γ.evaluators row 73); name generation per chapter 4.6 slices γ-δ | 🔵 V2-EXTENSION | `OssysSmoEmissionParityTests.``5.3.α row 123`` ` (Skip) | Slice 5.3.α.smo. V2's Pass-layer FK resolution operationalizes pillar 9 (FK emission is registered `OperatorIntent`); V1's 5-phase evidence walk maps to V2's strategy DU + Pass driver. **Deferred axes** (paired matrix rows 58 + 59): UPDATE referential action; NOCHECK per-constraint trusted state. |
| 124 | `Osm.Smo/ExtendedPropertyScriptBuilder.cs` (~142 LOC) — emits `EXEC sys.sp_addextendedproperty` via string concatenation with `'` → `''` escaping (V1 line 140) | `ScriptDomBuild.buildSetExtendedProperty` (chapter 4.1.A slice 8) builds `ExecuteStatement` wrapping sp_addextendedproperty via typed ExecuteParameter binding; multi-level emission (Schema/Table/Column/Index) integrated at SsdtDdlEmitter dispatch | 🟢 PARITY | `OssysSmoEmissionParityTests.``5.3.α row 124`` ` (Skip) | Slice 5.3.α.smo. Same SQL surface; V2's typed-AST eliminates hand-rolled escaping. Per `DECISIONS 2026-05-10 — Text-builder-as-first-instinct discipline` — raw SQL string at V1 site replaced with typed-AST builders. |
| 125 | `Osm.Smo/TypeMappingPolicy.cs` + `TypeMappingRule.cs` + `TypeMappingPolicyDefinition.cs` + `TypeMappingPolicyLoader.cs` — 4-file 3-path resolution (on-disk override + external DB type + attribute default); JSON-config-loaded | `Projection.Core/PrimitiveType.fs` closed DU (Integer / Decimal / Text / Boolean / DateTime / Date / Time / Binary / Guid) + `SqlTypeCorrespondence.fs` hardcoded mapping; read-side (`ReadSide.mapSqlType`) resolves type before emission; emitter consumes typed VO | 🟡 DIVERGENCE (V2 simplified) | `OssysSmoEmissionParityTests.``5.3.α row 125`` ` (Skip) | Slice 5.3.α.smo. **Rationale**: V2's pillar 1 + A18 amended — Π consumes typed Catalog × Profile, no Policy. Type resolution is profile-construction-time, not emission-time. Round-trip property tested per chapter 3.7 slice β. No DECISIONS row needed — covered by A18 amended (Π consumes Catalog × Profile, never Policy). |
| 126 | `Osm.Smo/IdentifierFormatter.cs` (~124 LOC) — bracket-quoting per `QuoteType` (SquareBracket convention) + `ModuleNameSanitizer` cleans module names + `IndexNameGenerator` builds index names | `ScriptDomBuild.bracketed` (lines 48-52) delegates quoting to ScriptDom's `Identifier(QuoteType.SquareBracket)`; module-name normalization upstream in CatalogReader (chapter 2 OSSYS adapter); index naming via `indexNameResolver` (chapter 4.5 + 4.9 slice γ) | 🟢 PARITY | `OssysSmoEmissionParityTests.``5.3.α row 126`` ` (Skip) | Slice 5.3.α.smo. Per pillar 8 — names are concepts; deterministic generation at source. V2's responsibility split is cleaner (CatalogReader → emitter via typed Name VO). |
| 127 | `Osm.Smo/ConstraintNameNormalizer.cs` — post-hoc rename mapping when table is overridden (old constraint name → new); composite-name handling | V2 generates constraint names deterministically at emission-resolution time (after override is known); no post-hoc mapping; convention `PK_<schema>_<table>` / `FK_<owner>_<target>` per chapter 4.6 slices γ-δ | 🟢 PARITY | `OssysSmoEmissionParityTests.``5.3.α row 127`` ` (Skip) | Slice 5.3.α.smo. Per pillar 8 (names are concepts; not post-hoc edits). V2 eliminates the renamer by generating names once at the point of emission. |
| 128 | `Osm.Smo/StatementBatchFormatter.cs` (~60 LOC) — joins statements with `GO` separators; trims trailing whitespace per line; optional `NormalizeWhitespace` mode | `BatchSplitter` (chapter 3.6 cash-out) ships two paths: gold-standard `splitViaScriptDom` (ScriptDom parser + Sql160ScriptGenerator per batch) + fallback `splitOnGoLineFold` (F# line-fold on `^GO$`); batch assembly at realization layer (`Render.toText` / `Deploy.executeStream`) | 🟢 PARITY | `OssysSmoEmissionParityTests.``5.3.α row 128`` ` (Skip) | Slice 5.3.α.smo. Per `DECISIONS 2026-05-28 — Session 34 / A35 cash-out` (stream-realization pattern codified). V1 concatenates first then batches; V2 streams then batches at realization. |
| 129 | `Osm.Smo/SmoTriggerBuilder.cs` (~50 LOC) — extracts trigger definition; normalizes whitespace; skips encrypted triggers (def is null); sorts by name; emits `SmoTriggerDefinition` carrying raw T-SQL body | V2's `Trigger` IR shipped (chapter A.0' slice γ; matrix row 61 PARITY); emission deferred — not in `SsdtDdlEmitter.statements` dispatch today | 🟠 NOT-MAPPED | `OssysSmoEmissionParityTests.``5.3.α row 129`` ` (Skip) | Slice 5.3.α.smo. **Cash-out shape**: emit `ExecuteStatement` wrapping `CREATE TRIGGER` body; trigger emission is coordinated with chapter 4.2 User FK reflow (FKs moving causes trigger movement). **Trigger**: chapter 4.2 closes OR chapter 4.10/5 standalone trigger emission slice. |
| 130 | `Osm.Smo/PerTableWriter.cs` (~99 LOC) + `TableHeaderFactory.cs` (~55 LOC) — emit per-table to `Modules/<Module>/<Schema>.<Table>.sql` with header `/* Source: ... LogicalName ... */` | `Projection.Targets.SSDT/Render.toSsdtDirectory` (chapter 4.1.A slice 10) realizes `ArtifactByKind<SsdtFile>` map to disk with same path convention | 🟢 PARITY (with Tolerance) | `OssysSmoEmissionParityTests.``5.3.α row 130`` ` (Skip) | Slice 5.3.α.smo. Per A35/A36 — emitter produces in-memory artifact map; realization layer writes. **Tolerance**: V2 omits V1's per-table `/* Source: ... */` header comment per R6 split-brain (`Tolerance.IgnoreHeaderComments = true` initially); operator-requested headers are a future feature extension, not cutover-blocker. |
| 131 | `Osm.Pipeline/Orchestration/BuildSsdtPipeline.cs` — imperative step-chaining: 12 sequential `.BindAsync()` calls via `IBuildSsdtStep<TState,TNextState>`; ordering is source-coupled to field declaration | `Projection.Core/RegisteredTransforms.allChainSteps` list of `PassChainAdapter` entries (12 entries: 6 Catalog-rewriting + 6 decision-set-producing); `Compose.project` consumes via fold-and-bind | 🟡 DIVERGENCE (foundational) | `OssysPipelineOrchestrationParityTests.``5.6.α row 131`` ` (Skip) | Slice 5.6.α.orchestration. See `DECISIONS 2026-05-18 (slice 5.6.α.orchestration) — Registry-driven composition over imperative step-chaining`. Per chapter A.4.7' axis 1-3 + A41 totality + skeleton-purity property + applied-transforms manifest field. |
| 132 | V1 `BuildSsdtPipelineRequest` (14 fields) + `BuildSsdtPipelineResult` (28 fields) + 18+ intermediate state record types per step | V2 `ComposeState` (7 fields: Catalog + TopologicalOrder + 4 decision-sets + UserRemap) + implicit Outputs at CLI boundary | 🔵 V2-EXTENSION | `OssysPipelineOrchestrationParityTests.``5.6.α row 132`` ` (Skip) | Slice 5.6.α.orchestration. V1's transitive-typing per step → V2's fixed-shape state + smart-constructor invariants (A39). Cleaner; fewer allocator allocations. |
| 133 | `Osm.Pipeline/Orchestration/CaptureProfilePipeline.cs` — separate pipeline class (request/result + two-pass bootstrap → capture); blocks BuildSsdtPipeline via callback | V2 inverts: Profile is adapter input (loaded from disk; Compose.project consumes via `Profile.empty` or attached snapshot per A34) | ⚫ V1-SUNSET | `OssysPipelineOrchestrationParityTests.``5.6.α row 133`` ` (Skip) | Slice 5.6.α.orchestration. **Sunset rationale**: V2's adapter-input model makes pipeline class redundant; profile-load is per-run not per-pass; A34 (Profile independent of Catalog and Policy) + pillar 9. |
| 134 | `Osm.Pipeline/Orchestration/DmmComparePipeline.cs` + `DmmComparePipelineRequest.cs` + `DmmDiffLogWriter.cs` — V1 SMO Model vs emitted SSDT comparison via DMM lenses | V2's canary (`PhysicalSchemaDiff` via Deploy + ReadSide) replaces | ⚫ V1-SUNSET | `OssysPipelineOrchestrationParityTests.``5.6.α row 134`` ` (Skip) | Slice 5.6.α.orchestration. **Already covered by matrix row 109 + slice 5.8.α**. Pipeline class retires with DMM lens machinery. |
| 135 | `Osm.Pipeline/Orchestration/EvidenceCacheCoordinator.cs` + `EvidenceCachePipelineOptions.cs` + `BuildSsdtEvidenceCacheStep.cs` — pipeline-level caching with 9-variant `EvidenceCacheInvalidationReason` enum | V2 has no pipeline-level caching; evidence is ephemeral | 🟠 NOT-MAPPED | `OssysPipelineOrchestrationParityTests.``5.6.α row 135`` ` (Skip) | Slice 5.6.α.orchestration. **Cash-out shape**: cache adapter writing checkpointed Catalog/Policy decision-set JSON. **Trigger**: operator-reality canary shows evidence-load as bottleneck OR chapter 4+ perf-optimization opens caching slice. |
| 136 | `Osm.Pipeline/Orchestration/BuildSsdtSqlValidationStep.cs` + `SsdtSqlValidator.cs` + `SsdtSqlValidationSummary.cs` — SSDT validation via SMO + DacFx | V2 Π outputs typed Statement stream; validation belongs to realization layer (not Π) per A35/A36 | 🟠 NOT-MAPPED | `OssysPipelineOrchestrationParityTests.``5.6.α row 136`` ` (Skip) | Slice 5.6.α.orchestration. **Cash-out shape**: `Validator` sibling Π consuming SSDT stream → ValidationReport. **Trigger**: realization layer needs validation feedback OR M2+ post-deploy validation phase. |
| 137 | `Osm.Pipeline/Orchestration/{PipelineInsight,PipelineLogMetadataBuilder,OpportunityLogWriter}.cs` — centralized PipelineExecutionLog with severity enum + per-insight code + affected objects; flushed at completion | V2 `Lineage<Diagnostics<'output>>` writer + per-pass LineageEvent entries (source/code/message/metadata); Diagnostics accumulates trail | 🔵 V2-EXTENSION | `OssysPipelineOrchestrationParityTests.``5.6.α row 137`` ` (Skip) | Slice 5.6.α.orchestration. V2 stronger: per-pass attribution (source = `pass:<name>` per `DECISIONS 2026-05-18 (slice 5.4.γ.opportunities) — Per-pass DiagnosticEntry contract`). Future PipelineExecutionLog JSON emission is realization-layer concern. |
| 138 | `Osm.Pipeline/Orchestration/EmissionCoverageCalculator.cs` static method (OsmModel + PolicyDecisionSet + SmoModel + SmoBuildOptions → EmissionCoverageResult) | V2 ports algorithm to `Projection.Core.Coverage` module (chapter 4.4 slice α; matrix row 96 shipped) | 🟢 PARITY | `OssysPipelineOrchestrationParityTests.``5.6.α row 138`` ` (Skip) | Slice 5.6.α.orchestration. V2's Core placement makes it available to multiple consumers (Π, adapters, tests). |
| 139 | `Osm.Pipeline/Orchestration/EmissionFingerprintCalculator.cs` — cryptographic hash of emission shape for round-trip assertions | V2 `RegistryDigest` from registered transform metadata + applied Policy + Profile (chapter A.4.7' slice ζ; matrix row 95) | 🟢 PARITY | `OssysPipelineOrchestrationParityTests.``5.6.α row 139`` ` (Skip) | Slice 5.6.α.orchestration. V2's digest is lighter-weight (metadata only). |
| 140 | `Osm.Pipeline/Orchestration/BuildSsdtPostDeploymentTemplateStep.cs` — `PostDeployment-Bootstrap.sql` template with guard logic for bootstrap snapshot | V2's A.1 Π does not emit post-deploy scripts | 🟠 NOT-MAPPED | `OssysPipelineOrchestrationParityTests.``5.6.α row 140`` ` (Skip) | Slice 5.6.α.orchestration. **Cash-out**: `PostDeployTemplateEmitter` sibling consuming SSDT statements + producing template SQL with guard logic. **Trigger**: chapter 4.1 slice 9. |
| 141 | `Osm.Pipeline/Orchestration/BuildSsdtSqlProjectStep.cs` — `.sqlproj` MSBuild file with item groups for modules + seeds | V2's A.1 Π outputs `seq<Statement>` or typed `ArtifactByKind<SsdtFile>`; realization layers (not yet written) consume map + produce `.sqlproj` | 🟠 NOT-MAPPED | `OssysPipelineOrchestrationParityTests.``5.6.α row 141`` ` (Skip) | Slice 5.6.α.orchestration. **Cash-out**: `Render.toSqlProject` realizer consuming ArtifactByKind + emitting XML. **Trigger**: V2-owned realization layer demands Visual Studio / Azure DevOps integration. |
| 142 | `Osm.Pipeline/Orchestration/SchemaDataApplier.cs` — stateless utility applying schema + static/dynamic seed data via SMO + SqlCommand | V2 `Deploy.executeStream` realization-layer primitive (chapter 3.1.M2 slice α; ships in Projection.Pipeline) | 🟢 PARITY | `OssysPipelineOrchestrationParityTests.``5.6.α row 142`` ` (Skip) | Slice 5.6.α.orchestration. V2's form decoupled from SMO; enables bulk-vs-incremental per A36; tested via canary `Deploy.runWithReadback`. |
| 143 | `Osm.Pipeline/Orchestration/BuildSsdtPolicyDecisionStep.cs` — dedicated orchestration step invoking policy-making rules | V2 absorbs into registry: 4 decision-set passes + UserFkReflowPass each registered as `RegisteredTransform<Catalog, DecisionSet>` in `allChainSteps`; flow through `PassChainAdapter.liftDecisionPass` | 🟢 PARITY | `OssysPipelineOrchestrationParityTests.``5.6.α row 143`` ` (Skip) | Slice 5.6.α.orchestration. Covered by slice 5.4.γ.evaluators rows 72-74. No additional parity work needed. |
| 144 | `Osm.Pipeline/Orchestration/BuildSsdtBootstrapStep.cs` + `BuildSsdtBootstrapSnapshotStep.cs` — two-step (load model + profile; capture snapshot for idempotent redeployment) | V2 inlines bootstrap into `CatalogReader.parse` adapter (loads V1 JSON; deserializes Catalog; returns Result) | 🟢 PARITY | `OssysPipelineOrchestrationParityTests.``5.6.α row 144`` ` (Skip) | Slice 5.6.α.orchestration. Cleaner separation: bootstrap is adapter responsibility; pipeline is composition responsibility. Snapshots handled at realization layer if needed. |
| 145 | `Osm.Pipeline/Orchestration/BuildSsdtEmissionStep.cs` — singular sequential emission via `ISmoModelFactory.Create` + `ISsdtEmitter.EmitAsync` | V2 expands to 3 sibling Π's (SSDT DDL + JSON + Distributions) + manifest emitter; each consumes same final ComposeState independently (Compose.projectFromChain) | 🔵 V2-EXTENSION | `OssysPipelineOrchestrationParityTests.``5.6.α row 145`` ` (Skip) | Slice 5.6.α.orchestration. Sibling chorus enables independent evolution + per-Π verification per chapter 4.1.A + chapter A.4.7'. |
| 146 | `Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs` + `BuildSsdtDynamicInsertStep.cs` — separate pipeline steps for static + dynamic data emission | V2's static-seed emission ships via `StaticSeedsEmitter` + `BootstrapEmitter` + `MigrationDependenciesEmitter` (chapter 4.1.A); dynamic INSERT generation deferred | 🟠 NOT-MAPPED (partial) | `OssysPipelineOrchestrationParityTests.``5.6.α row 146`` ` (Skip) | Slice 5.6.α.orchestration. Companion to matrix rows 168-176 (slice 5.5.γ). Static seeding shipped; dynamic INSERT deferred to chapter 4+ sibling-Π completion. |
| 147 | `Osm.Pipeline/Orchestration/BuildSsdtTelemetryPackagingStep.cs` — telemetry artifact packaging from prior steps | V2 distributes via (1) `Bench.snapshot()` per-label timing + persisted JSON; (2) `Lineage<Diagnostics<'output>>` trail | 🟢 PARITY | `OssysPipelineOrchestrationParityTests.``5.6.α row 147`` ` (Skip) | Slice 5.6.α.orchestration. CLI collects Bench at exit (Program.fs lines 92-104); Lineage trail available to consumers. |
| 148 | `Osm.Json/Deserialization/ModelJsonDeserializer.cs` multi-partial sealed class with lazy-init shared pipeline; `Deserialize(Stream, options)` surface | `Projection.Adapters.Osm.CatalogReader.parse : SnapshotSource -> Task<Result<Catalog>>` + sync `parseJsonString`; closed DU `SnapshotSource = SnapshotFile / SnapshotJson / SnapshotRowsets` | 🟢 PARITY | `OssysJsonDeserializationParityTests.``5.2.β row 148`` ` (Skip) | Slice 5.2.β.json. V1 multi-partial-class C# ↔ V2 single F# module; isomorphic. V2 adds async surface. |
| 149 | V1 5 mapper classes (`EntityDocumentMapper` / `RelationshipDocumentMapper` / `SequenceDocumentMapper` / `TriggerDocumentMapper` + module/extended-prop) | V2 7 `let` functions (`parseKind` / `parseModule` / `parseAttribute` / `parseReference` / `parseTrigger` / `parseIndex` / `parseExtendedProperty`); class-per-aggregate → function-per-aggregate | 🟢 PARITY | `OssysJsonDeserializationParityTests.``5.2.β row 149`` ` (Skip) | Slice 5.2.β.json. V2 adds `parseIndex`; no semantic gap. |
| 150 | `Osm.Json/Deserialization/{AttributeDeduplicator,IAttributeDeduplicator,DuplicateWarningEmitter,IDuplicateWarningEmitter}.cs` — duplicate-attribute handling for V1 JSON-projection artifact (multiple attribute rows with `ReferenceEntityIsActive` tie-breaker); emits warnings on `AllowDuplicate*` flags | V2 JSON path has no equivalent | 🟠 NOT-MAPPED | `OssysJsonDeserializationParityTests.``5.2.β row 150`` ` (Skip) | Slice 5.2.β.json. **Cash-out**: SnapshotRowsets adapter ports the dedup logic; per-attribute SsKey identity provides natural deduplication. **Trigger**: SnapshotRowsets ships OR JSON fixture surfaces duplicate-attribute case. |
| 151 | `Osm.Json/CirSchemaValidator.cs` static class loads embedded `cir-v1.json` JSON Schema; pre-deserialization validation; fail-fast | V2 validation deferred to per-entity / per-attribute / per-reference parse-step error handling | 🟡 DIVERGENCE | `OssysJsonDeserializationParityTests.``5.2.β row 151`` ` (Skip) | Slice 5.2.β.json. **Already covered by matrix row 31** (SnapshotValidator subsumes). V2's structural validation (type system + smart constructors) is canonical; CIR schema is V1 editorial artifact not carried forward. Trade-off: V2's per-element-during-traversal beats V1's fail-fast on error localization. |
| 152 | `Osm.Json/Deserialization/BooleanAsZeroOneConverter.cs` custom `JsonConverter<bool>` (0/1 numbers, booleans, or strings) registered on `ModelDocumentSerializerContext` | V2 `CatalogReader.getIntFlag` + `getOptionalIntFlag` helpers with explicit `match value.ValueKind` over JSON token types | 🟢 PARITY | `OssysJsonDeserializationParityTests.``5.2.β row 152`` ` (Skip) | Slice 5.2.β.json. Functionally isomorphic; V2 named helpers make call sites self-describing. |
| 153 | `Osm.Json/Deserialization/{ProfileSnapshotSerializer,ProfileSnapshotDeserializer}.cs` build isolated `ProfileSnapshot` domain object; extensive record types for JSON DTOs | `Projection.Adapters.Osm.ProfileSnapshot.attach` parses JSON string → probes → attaches to catalog-keyed index → returns `Profile` aggregate (consults Catalog for SsKey resolution) | 🟡 DIVERGENCE | `OssysJsonDeserializationParityTests.``5.2.β row 153`` ` (Skip) | Slice 5.2.β.json. **Design change**: V1 constructs isolated records; V2 inverts to catalog-driven attachment. V2 drops V1's `NullSample` / `OrphanSample` operational diagnostics per pillar 9 (Profile = empirical evidence only). Semantically aligned; structurally inverted. |
| 154 | V1 `DocumentPathContext` DU + `ValidationError.WithMetadata("json.path", ...)` — error metadata + path tracking stack | V2 `adapterError` named-parameter helper + inline path composition at error sites | 🟢 PARITY | `OssysJsonDeserializationParityTests.``5.2.β row 154`` ` (Skip) | Slice 5.2.β.json. V1 mutable context is C#-idiomatic; V2 inline composition scales to F# Result + pipeline. No semantic loss. |
| 155 | `Osm.Json/Deserialization/CircularDependencyConfigDeserializer.cs` parses operator config (allowedCycles array + strictMode flag) | V2's config story lives in `Projection.Core.Configuration` + Pipeline layer, not adapter | 🟠 NOT-MAPPED (out-of-scope) | `OssysJsonDeserializationParityTests.``5.2.β row 155`` ` (Skip) | Slice 5.2.β.json. Circular-dep config is operator-intent (Tightening-axis overlay in V2 vocabulary); lives at Pipeline/Config layer when ConfigurationProvider surfaces. **No action item** at adapter layer. |
| 156 | `Osm.Emission/{TableEmissionPlan,TableEmissionPlanner,TablePlanWriter,ITablePlanWriter}.cs` — 3-phase pipeline: Planner → PlanWriter → Manifest (per-table emission plan + semaphore-bounded parallelism) | V2 `SsdtDdlEmitter.emit` directly produces `ArtifactByKind<SsdtFile>` (per-kind typed artifact map); no intermediate plan object; realization layer (`Render.toSsdtDirectory` + `Deploy.executeStream`) writes per-kind | 🟡 REDESIGN | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 156`` ` (Skip) | Slice 5.5.β+γ+δ. Per A35/A36 — bulk-vs-incremental + parallelism are realization-layer policy. The 'plan' IS the Kind→SsdtFile mapping. |
| 157 | `Osm.Emission/SsdtEmitter.cs` (~145 LOC) monolithic orchestrator (planner → writer → manifest builder; directory setup + error handling) | V2 splits into sibling Π's: `SsdtDdlEmitter` (schema DDL); `StaticSeedsEmitter` + `MigrationDependenciesEmitter` + `BootstrapEmitter` (data per A18 amended); `ManifestEmitter` (manifest) | 🟢 PARITY | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 157`` ` (Skip) | Slice 5.5.β+γ+δ. Per chapter 4.1.A close arc + matrix row 145 (slice 5.6.α.orchestration). |
| 158 | `Osm.Emission/Seeds/EntityDependencySorter.cs` (~200+ LOC) — Kahn + cycle detection + alphabetical fallback; `EntityDependencyOrderingModeExtensions.cs` Alphabetical/Topological/JunctionDeferred mode utilities | V2 `Projection.Core/Passes/TopologicalOrderPass.fs` (~300 LOC) — Kahn (v1) + Tarjan SCC (v2+) + asymmetric-2-cycle resolver (v3+) + self-loop detection (v4 / chapter 4.1.B slice δ) + SelfLoopPolicy parameterization per A40; produces `TopologicalOrder` value per A32 | 🟢 PARITY | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 158`` ` (Skip) | Slice 5.5.β+γ+δ. V2's v3+/v4 resolvers handle empirical fixtures V1's alpha-fallback couldn't. Pass-layer placement enables emitter sharing. |
| 159 | `Osm.Emission/DynamicEntityInsertGenerator.cs` (~790 LOC) — dynamic INSERT/MERGE for non-static runtime data with batch-size control + determinism | V2 `DataEmissionComposer` → `StaticSeedsEmitter.emitWithTopo` (typed `MergeStatement` via ScriptDom per chapter 4.1.B slice α) → `Deploy.executeStream` realization | 🟢 PARITY | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 159`` ` (Skip) | Slice 5.5.β+γ+δ. V2 emits typed MERGE (not raw INSERT); batch sizing realization-layer per A36. |
| 160 | `Osm.Emission/PhasedDynamicEntityInsertGenerator.cs` (~150 LOC) — 2-phase cycle-breaking: Phase-1 INSERT (nullable FKs NULLed); Phase-2 UPDATE | V2 `StaticSeedsEmitter` slice δ (chapter 4.1.B): `deferredColumns` predicate + per-kind `Phase1Merges` + `Phase2Updates` in `DataInsertScript`; TopologicalOrderPass v4 supplies cycle membership | 🟢 PARITY (partial) | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 160`` ` (Skip) | Slice 5.5.β+γ+δ. **Open item per slice η**: cross-emitter global phase ordering (Phase-1-ALL across StaticSeeds + Migration + Bootstrap, then Phase-2-ALL) NOT YET REIFIED. **Trigger**: chapter 4.2+ migration-dependency at scale. |
| 161 | `Osm.Emission/Seeds/{EntitySeedDeterminizer,StaticEntitySeedScriptGenerator,StaticEntitySeedTemplateService}.cs` — sorted-by-PK row determinism + script-orchestration + template wrapping | V2 by-construction determinism (every emitter-consumable row source pre-sorted by SsKey or explicit order); fused emission via emitter + composer pipeline | 🟢 PARITY | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 161`` ` (Skip) | Slice 5.5.β+γ+δ. Per CLAUDE.md `Determinism is constructed, not validated`. No post-hoc Normalize step. |
| 162 | `Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` (~80 LOC) — full preflight: ordering correctness + orphan-row detection + cross-module references + remediation guidance | V2 partial: `TopologicalOrderPass.MissingEdges` (FK targets not in catalog) + per-emitter cycle detection (SCC members for deferred-column selection); **full orphan-row + cross-module audit NOT YET IMPLEMENTED** | 🟠 NOT-MAPPED (partial) | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 162`` ` (Skip) | Slice 5.5.β+γ+δ. **Cash-out**: chapter 4.2 slices γ+δ (UserFkReflowPass discovery phase) — full cross-module FK audit + remediation paths. |
| 163 | `Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` (~200+ LOC) — MERGE construction: ON-clause + WHEN-NOT-MATCHED INSERT + WHEN-MATCHED UPDATE + drift detection | V2 `ScriptDomBuild.buildMergeStatement` (typed AST) + `StaticSeedsEmitter.renderMerge` (logic); `ScriptDomGenerate.generateOne` renders byte-deterministic SQL; drift-detection preserved | 🟢 PARITY | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 163`` ` (Skip) | Slice 5.5.β+γ+δ. Same MERGE shape; typed AST per pillar 1 + 7. Chapter 4.1.B slice α shipped. |
| 164 | `Osm.Emission/Formatting/SqlIdentifierFormatter.cs` (~30 LOC) + `SqlLiteralFormatter.cs` (~150 LOC) — SQL identifier square-bracket quoting + `]]` escape; SQL literal escaping (strings `''` → `''''`, nulls, numeric formats, type-specific quoting) | V2 `ScriptDomBuild.bracketed` delegates escaping to ScriptDom's `Identifier(QuoteType.SquareBracket)`; `buildSqlLiteral` + `Projection.Core/SqlLiteral.fs` typed IR (chapter 4.1.B slice κ pillar 1 lift); `SqlLiteral.ofRaw` smart constructor escapes on construction | 🟢 PARITY | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 164`` ` (Skip) | Slice 5.5.β+γ+δ. No hand-rolled escaping in V2 emitters. Per pillar 1 + 7. |
| 165 | `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` (~400 LOC) — static-data source abstraction (fixture loader for JSON + SQL extractors); `src/Osm.Pipeline/DynamicData/SqlDynamicEntityDataProvider.cs` (~500 LOC) — dynamic-data extraction (per-module SQL queries, batching, telemetry) | V2 partial: `Projection.Adapters.Sql/ReadSide.fs` (async streaming rows via `AsyncStream<(Name * string) list>`) + `Projection.Pipeline/Bulk.fs` (bulk copier); **Fixture provider pattern NOT YET INTEGRATED**; per-module orchestration at Pipeline level | 🟠 NOT-MAPPED (partial) | `OssysSsdtDataEmissionParityTests.``5.5.βγδ row 165`` ` (Skip) | Slice 5.5.β+γ+δ. **Cash-out trigger**: test-harness fixture loading for chapter 4.2+ isolation tests. Read-side shipped; fixture provider + per-module Pipeline orchestration deferred. |
| 166 | `Osm.Validation/Tightening/Validations/{ValidationFinding,ValidationReport}.cs` — ValidationFinding (OpportunityType + Title + Summary + Evidence[] + Rationales[] + Column? + Index? + Schema + Table + ConstraintName + Columns[]); ValidationReport bundles findings + TypeCounts + GeneratedAtUtc | V2 `Diagnostics.DiagnosticEntry` (Source + Severity + Code + Message + SsKey? + Metadata) + `LineageDiagnostics<'a>` writer; TypeCounts computed post-hoc from entry stream; ManifestEmitter aggregates | 🟢 PARITY | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 166`` ` (Skip) | Slice 5.4.α+ε+ζ. **Covered by prior slice 5.4.γ.opportunities row 77** (Opportunity → DiagnosticEntry projection). |
| 167 | `Osm.Pipeline/Evidence/EvidenceArtifactType.cs` (Model/Profile/Dmm/Configuration) + `EvidenceArtifactDescriptor.cs` (Type+SourcePath+Hash+Length+Extension) + `EvidenceCacheModels.cs` (8 types + 9-variant invalidation enum) | V2 has NO equivalent caching layer in Core; canonical output is `seq<Statement>` (Π); cache management is realization-layer policy | 🟠 NOT-MAPPED | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 167`` ` (Skip) | Slice 5.4.α+ε+ζ. **Covered by row 135** (slice 5.6.α.orchestration EvidenceCacheCoordinator). Cash-out: future multi-source ingestion (Catalog from SQL + JSON + DACPAC) ports artifact metadata as adapter-layer concern. |
| 168 | `Osm.Pipeline/Evidence/{EvidenceCacheService,IEvidenceCacheService,ManifestEvaluator,EvidenceCacheWriter,CacheRequestNormalizer,CacheEntryCreator}.cs` — facade orchestration cluster (CacheAsync request lifecycle + 9-check validation + WriteAsync with versioning/TTL/serialization) | V2 orchestration in `Projection.Pipeline.Compose` is for pass-pipeline + Π realization (different responsibility) | 🟠 NOT-MAPPED | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 168`` ` (Skip) | Slice 5.4.α+ε+ζ. **Future**: V2 two-tier orchestration (skeleton pass → optional caching → Π realization). Per matrix row 135 trigger. |
| 169 | `Osm.Pipeline/Application/IApplicationService<TInput, TResult>` interface (`Task<Result<'output>> RunAsync(TInput, CancellationToken)`) | V2 typed-function pass signatures: `Catalog -> Policy -> Profile -> Lineage<'output>` (or `Lineage<Diagnostics<'output>>`); orchestration via functional composition in Compose.fs | 🟡 DIVERGENCE | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 169`` ` (Skip) | Slice 5.4.α+ε+ζ. Contract exists at type level; not as interface. Per `DECISIONS 2026-05-16 (later) — V2 self-containment` (avoids interface-heavy dispatch; object expressions deferred). No DECISIONS row needed — covered. |
| 170 | `Osm.Pipeline/Application/{AnalyzeApplicationService,ExtractModelApplicationService,BuildSsdtApplicationService,FullExportApplicationService,CaptureProfileApplicationService,CompareWithDmmApplicationService}.cs` — ~7 concrete services (50-250 LOC each); CLI args → pipeline → result | V2 equivalent: host-layer CLI command handlers (not yet written) | 🟠 NOT-MAPPED (gated) | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 170`` ` (Skip) | Slice 5.4.α+ε+ζ. **Already named per slice 5.7.α.cli matrix rows 105-119**. Per R6 split-brain governance — V2 emits-but-doesn't-ship during dual-track; CLI deferred post-cutover. |
| 171 | `Osm.Pipeline/Mediation/{CommandDispatcher,ICommand,ICommandHandler}.cs` — MediatR-style command pattern (~80 LOC) | V2 per-pass module + pass driver; composition via `Composition.fanOut` (functional); no command-dispatcher | 🟡 DIVERGENCE | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 171`` ` (Skip) | Slice 5.4.α+ε+ζ. Per F# object-expressions deferral + sibling-wrapper discipline (DECISIONS 2026-05-17). V2's conscious-omission rationale forbids command-dispatcher pattern on principle. **If revisited**: future host shell (web server, plugin architecture) would be host-layer not Core. |
| 172 | `Osm.Pipeline/Application/{PipelineRequestContextBuilder,PipelineRequestContext}.cs` — context object carrying tightening options + module filter + SQL options + caching overrides + metadata logger + flush fn (~250 LOC) | V2 `Projection.Pipeline.Compose` performs equivalent assembly; configuration in explicit pass parameters not context object | 🟡 DIVERGENCE | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 172`` ` (Skip) | Slice 5.4.α+ε+ζ. Per F#-pure-core / no-I/O-in-Core load-bearing commitment. Builder pattern for configuration assembly recurs in V2 but context object avoided. |
| 173 | V1 9-variant `EvidenceCacheInvalidationReason` enum (ManifestMissing / Invalid / VersionMismatch / KeyMismatch / CommandMismatch / Expired / ModuleSelectionChanged / MetadataMismatch / ArtifactsMismatch / RefreshRequested) | V2 distributed across decision types per pass (NullabilityOutcome / UniqueIndexOutcome / ForeignKeyOutcome with keep-reason enums); manifest validation via per-pass integrity checks + Lineage trail + canary diff | 🟢 PARITY (cross-reference) | `OssysPipelineEvidenceParityTests.``5.4.αεζ row 173`` ` (Skip) | Slice 5.4.α+ε+ζ. **Candidate adoption**: if V2 CLI adds manifest-validation mode, closed-DU invalidation-reason pattern informs design. |
| 174 | `Osm.Pipeline/UatUsers/UserMatchingEngine.cs` (~316 LOC) 3 strategies (CaseInsensitiveEmail / ExactAttribute / Regex) + fallback (RoundRobin / SingleTarget / Ignore); `UserIdentifier.cs` 3-variant numeric/guid/text discriminator | V2 `Projection.Core/UserIdentity.fs` + `UserRemap.fs` + `Projection.Core/Passes/UserFkReflowPass.fs`: typed `UserId` + `SourceUserId` / `TargetUserId` orientation; `Policy.UserMatching` DU; `buildEmailIndex` mirrors V1 `TryExactMatch`; typed `UserRemapContext` IR + `RemapDiagnostic` DU. **Slice δ ships ByEmail**; BySsKey/Regex/FallbackToSystemUser deferred to slice ε | 🟢 PARITY (partial) | `OssysOmnibusClosingParityTests.``5.5.ε row 174`` ` (Skip) | Omnibus. Per pre-scope: chapter 4.2 + slice ε will land remaining strategies. |
| 175 | `Osm.Pipeline/UatUsers/UserIdentifier.cs` (~155 LOC) — 3-variant discriminator (Numeric/Guid/Text; FromString/FromDatabaseValue factories); runtime-introspectable kind | V2 `UserId` newtype + typed `SourceUserId` / `TargetUserId` orientation markers; runtime-discriminated kind traded for compile-time orientation safety | 🔵 V2-EXTENSION | `OssysOmnibusClosingParityTests.``5.5.ε row 175`` ` (Skip) | Omnibus. Same numeric/guid/text evidence via value projection; V2 stronger via type-witnessed orientation. |
| 176 | `Osm.Pipeline/UatUsers/UatUsersPipelineRunner.cs` imperative step-pipeline: 6 sequential steps via mutable `UatUsersContext` | V2 `UserFkReflowPass.discover` monadic composition via `Lineage.bind`; immutable IR (`UserRemapContext`) produced | 🔵 V2-EXTENSION | `OssysOmnibusClosingParityTests.``5.5.ε row 176`` ` (Skip) | Omnibus. Per matrix row 131 — registry-driven composition principle extends to UAT users. Pass-return-type codification per `DECISIONS 2026-05-13`. |
| 177 | `Osm.Pipeline/UatUsers/Verification/{UatUsersVerifier,FkCatalogCompletenessVerifier,TransformationMapVerifier,SqlSafetyAnalyzer}.cs` — orchestrate 3 verifiers post-pipeline; `UatUsersVerificationContext` + `Report` synthesizes | V2 verification deferred post-cutover; canary's round-trip diff + tolerance table cover dual-track mode per R6 governance | 🟠 NOT-MAPPED (gated) | `OssysOmnibusClosingParityTests.``5.5.ε row 177`` ` (Skip) | Omnibus. **Cash-out**: chapter 4.3+ post-deploy verification phase OR cutover dry-run discovers a verification case the canary doesn't cover. |
| 178 | `Osm.LoadHarness/LoadHarnessRunner.cs` + ~6 files / ~1300 LOC — ExecuteAsync orchestrator; script replay + batch splitting on GO; `ScriptReplayResult` (per-batch timing + DMV wait-stats delta + lock summary + index fragmentation) | V2 has NO direct LoadHarness equivalent — canary mechanism (`ScriptDomRoundTripTests` + `GeneratorScaleTests`) replaces V1's load-harness for pre-deployment validation | ⚫ V1-SUNSET (partial) | `OssysOmnibusClosingParityTests.``5.7.β row 178`` ` (Skip) | Omnibus. **Sunset rationale**: V2's pre-cutover validation uses schema-only canary (fast, structural) + operator-reality canary (300-table 50k-row baseline). DMV instrumentation is post-cutover operator-facing tool — chapter 5+ work. |
| 179 | V1 DMV-based instrumentation: `QueryWaitStatsAsync` + `QueryLockSummaryAsync` + `QueryIndexFragmentationAsync` (~3 distinct DMV queries; provides per-batch timing + wait-stats + locks + fragmentation snapshots) | V2 has no DMV instrumentation; Bench surface covers timing per A24/A25 (iterator logging per chapter 3.6) | 🟠 NOT-MAPPED | `OssysOmnibusClosingParityTests.``5.7.β row 179`` ` (Skip) | Omnibus. **Cash-out shape**: post-cutover operator-facing tool consuming Bench samples + adding DMV queries via `Projection.Adapters.Sql` DMV adapter; emit consolidated post-deploy diagnostic report. **Trigger**: chapter 5+ operator-facing post-deploy tools OR operator demands DMV-style observability. |
| 180 | `Osm.Domain/ValueObjects/*.cs` — 11 naming VOs (EntityName / ModuleName / TableName / ColumnName / AttributeName / SchemaName / IndexName / ForeignKeyName / SequenceName / TriggerName); each is record struct with `Create: Result<Name>` via `StringValidators.RequiredIdentifier` | V2 consolidates to ONE load-bearing identity VO (`SsKey` 4-variant DU per matrix row 45; identity is structural per A1; never a string) + Name VO (presentation-only smart constructor per pillar 8) + `Coordinates.fs` typed records (TableId + ModuleId bundle related names) | 🔵 V2-EXTENSION | `OssysOmnibusClosingParityTests.``5.2.α.valueobjects row 180`` ` (Skip) | Omnibus. V1's 11-type struct sprawl was compile-time noise; V2's identity-vs-presentation split is cleaner without parity loss. Per A1 + pillar 8. |
| 181 | `Osm.Domain/ValueObjects/StringValidators.cs` shared validation — `RequiredIdentifier` enforces non-null + non-empty + trimmed | V2 distributes validation across consumer smart constructors per IR-grows-under-evidence; equivalent invariant in `Name.create` + per-type smart constructors | 🟢 PARITY | `OssysOmnibusClosingParityTests.``5.2.α.valueobjects row 181`` ` (Skip) | Omnibus. Two-consumer threshold for shared validator module not yet met in V2. Per CLAUDE.md operating-disciplines. |
| 182 | `Osm.Smo/CreateTableStatementBuilder.cs` (~490 LOC) line-by-line audit: column data type (line 296) ↔ V2 `dataTypeReference` (lines 100-138); nullability (V1 301) ↔ V2 156; IDENTITY (V1 304-311) ↔ V2 160-168; FK DELETE action (V1 168) ↔ V2 203-207; NOCHECK FK (V1 214-286 string-composed) ↔ V2 MigrationDependenciesEmitter typed-statement | **Deferred axes**: single-column PK inline optimization (V1 67-77); column defaults + CHECK constraints + computed columns (V1 319-364) — ColumnDef IR fields exist; emit layer deferred per slice ζ candidates | 🟢 PARITY (95%) | `OssysOmnibusClosingParityTests.``5.3.β row 182`` ` (Skip) | Omnibus. Multi-column PK (V1 81-98) ↔ V2 235-238 PARITY; column ordinal order ↔ V2 implicit. |
| 183 | `Osm.Smo/IndexScriptBuilder.cs` (~452 LOC) line-by-line audit: index columns + sort order (V1 65-84) ↔ V2 757-771; INCLUDE columns (V1 67-71) ↔ V2 773-778; WHERE clause (V1 410 TSql150) ↔ V2 698 TSql160 (upgraded SQL Server 2022). FillFactor + PadIndex + StatisticsNoRecompute + AllowRowLocks + AllowPageLocks: 100% parity | **Deferred axes** (slice ζ candidates): IgnoreDupKey (V1 215-221); DataCompression with partition-range collapse (V1 259-301); FileGroup/PartitionScheme dataspace (V1 322-374); paired matrix rows 55+56 | 🟢 PARITY (70%) | `OssysOmnibusClosingParityTests.``5.3.β row 183`` ` (Skip) | Omnibus. V2 upgraded to TSql160 + filter-parse failures observable via Diagnostics. |
| 184 | V1 `IndexScriptBuilder.ParsePredicate` returns null on parse failure (silent; filtered by caller; V1 lines 403-419) | V2 `ScriptDomBuild.tryParseFilterWithDiagnostics` (chapter 4.6 slice γ; lines 692-735) emits Diagnostics Warning entry + None on parse failure | 🔵 V2-EXTENSION | `OssysOmnibusClosingParityTests.``5.3.β row 184`` ` (Skip) | Omnibus. V1's silent-skip becomes V2's named diagnostic per Total-decisions-named-skips discipline + slice 5.4.γ.opportunities Per-pass DiagnosticEntry contract. |
| 185 | V1 `IndexScriptBuilder.ColumnReferenceRewriteVisitor` (lines 421-450) rewrites column references post-parse (physical → logical name mapping) via visitor pattern | V2 encodes both names in ColumnDef and uses logical (Name field) at emit time; no rewriter visitor | 🔵 V2-EXTENSION | `OssysOmnibusClosingParityTests.``5.3.β row 185`` ` (Skip) | Omnibus. Per pillar 8 — names are concepts; deterministic at-source generation. V2 IR carries both names from CatalogReader; emitter consumes logical name directly. V2's IR-level naming eliminates rewriter complexity. |

---

## Status history amendments — row reclassifications discovered by later slices

The matrix is append-only at the row level — original rows are not
modified in place. When a later slice discovers that an earlier row's
classification was stale (e.g., V2 actually carries the capability;
the original audit missed it), append a dated amendment to this
section naming the prior status, the new status, and the discovery
slice.

### Rows 11 + 12 + 14 + 15 + 16 + 17 + 18 + 23 — 2026-05-18 (closed by slice 5.13.ossys-rowsets-cluster)

**Cluster A1 closure** — eight OSSYS-source physical-reflection rowsets
lift in one slice. The plan from `V1_PARITY_MATRIX.md` Cluster A1
("Per-rowset lift shape" — F# record + ordinal mapper + parse-and-
accumulate + MetadataSnapshot extension + optional RowsetBundle
integration) executed across all eight rowsets, with the IR-integration
optional step taken for the four rowsets whose V2 IR consumers exist
(rows 12, 15, 16, 23).

**Original classification (audit-wave slice 5.0.γ, 2026-05-17):** 🟠 NOT-MAPPED.
V2 walked all 22 result sets but parsed only the first 5.

**Reclassified (slice 5.13.ossys-rowsets-cluster, 2026-05-18):**

| Row | Rowset | Status | IR integration |
|---|---|---|---|
| 11 | `#ColumnReality` | 🔵 V2-EXTENSION (typed rowset; no IR consumer yet) | deferred — gated on `Profile.AttributeReality` row 49 |
| 12 | `#ColumnCheckReality` | 🟢 PARITY | wired → `Kind.ColumnChecks` |
| 14 | `#PhysColsPresent` | 🔵 V2-EXTENSION (typed rowset; no IR consumer yet) | deferred — gated on V2 orphan-attribute consumer |
| 15 | `#AllIdx` | 🟢 PARITY | wired → `Kind.Indexes` |
| 16 | `#IdxColsMapped` | 🟢 PARITY | wired → `Kind.Indexes.Columns` + `Kind.Indexes.IncludedColumns` |
| 17 | `#FkReality` | 🔵 V2-EXTENSION (typed rowset carries OnUpdate + IsNoCheck; IR enrichment gated on rows 58 + 59) | deferred — IR enrichment is rows 58 + 59's slice |
| 18 | `#FkColumns` | 🔵 V2-EXTENSION (typed rowset; composite-FK consumer gated on V2 IR extension) | deferred — V2 Reference IR is single-column |
| 23 | `#Triggers` | 🟢 PARITY | wired → `Kind.Triggers` |

**Retires V2's structural dependence on V1's `#IdxJson`** (row 26,
⚫ V1-SUNSET) — V2's index axis is now V1-IndexJson-independent.
Same retirement applies to `#TriggerJson` (row 27) for triggers.

**Engineering quality.** The slice opens with the closure-helper
refactor in `MetadataSnapshotRunner.runAsync`:

```fsharp
let read name mapper = task {
    let! _ = advanceNext ()
    let! rows = readResultSet name reader mapper
    report name rows.Length
    return rows }
let skip name = task { ... }
```

Replaces 5 `let! _ = advanceNext(); let! foo = readResultSet ...;
report ...` triplets with one closure used per rowset. Explicit
`read`/`skip` calls for every documented rowset (23 total); the
trailing skip-loop becomes a sanity guard for SQL-contract drift.

The downstream `parseRowsetBundle` consolidates eight per-id-keyed
groupings into one `RowsetParseContext` record threaded through
`parseModuleRow` → `parseKindRow` — future rowset lifts extend the
context record rather than the function signature
(sibling-wrapper discipline applied at the data-shape level).

V2 produces a `Kind.Indexes` / `Kind.Triggers` / `Kind.ColumnChecks`
surface from the rowset path that's now equivalent to the JSON
path's; the synthetic seed fixture's `IDX_CUSTOMER_EMAIL` (filtered
unique), `IDX_CUSTOMER_NAME` (disabled), and
`TR_OSUSR_XYZ_JOBRUN_AUDIT` (trigger) all flow through to the V2
IR.

**Coverage tests now passing:**
- `OssysExtractionCanaryTests.``Slice 5.13.ossys-rowsets-cluster: indexes lift via rowset path (matrix rows 15 + 16)`` `
- `OssysExtractionCanaryTests.``Slice 5.13.ossys-rowsets-cluster: triggers lift via rowset path (matrix row 23)`` `
- progress-callback canary now asserts the full named-rowset sequence (rows 11/12/14/15/16/17/18/23 surface as named progress observations)

---

### Rows 32 + 34 + 35 — 2026-05-18 (closed by slice 5.13.production-wiring-classification)

**Original classifications (slice 5.1.γ, 2026-05-17):**
- Row 32: 🟠 NOT-MAPPED. Exception classification absent; V2 used a
  single `with ex ->` catch wrapping `ex.Message` in `ValidationError`.
- Row 34: 🟠 NOT-MAPPED. Transient-error retry absent; every
  `SqlException` propagated immediately.
- Row 35: 🟠 NOT-MAPPED. V2 read result sets via a silent
  `while hasMore` loop with no count contract enforcement.

**Reclassified (slice 5.13.production-wiring-classification,
2026-05-18):** All three → 🔵 V2-EXTENSION.

**Rationale.** Bundled per cluster A7 cash-out plan ("Rows 32 + 34
+ 35 then bundle into one chapter since they share the closed-DU
`MetadataExtractionError` shape"). The closed-DU
`MetadataExtractionError` (4-variant: `RowMappingFailure |
ResultSetMissing | TransientSqlError | OtherSqlError`) lives at
`src/Projection.Adapters.OssysSql/MetadataExtractionError.fs`; the
Polly v8 resilience pipeline lives at
`src/Projection.Adapters.OssysSql/Retry.fs`. Both wire into
`MetadataSnapshotRunner.runAsync` at the command-execute boundary
(retry) + outer classifier (DU) + post-loop contract check
(`ExpectedResultSets = 23`). V2 is **structurally stronger** than V1
on every axis: the typed DU plus the pure `classify` /
`toValidationError` / `resultSetContractCheck` mappers make
error-routing contracts machine-checkable; Polly retry that V1
lacked tolerates cloud-OSSYS transients without false-positive
divergence reports per R6 split-brain governance; post-loop count
assertion catches drift V1's per-step `EnsureNextResultSetAsync`
couldn't (V1 only fired on missing-rowset-before-an-expected-processor,
not on extra-or-missing total-count drift). Per `DECISIONS 2026-05-18
(slice 5.13.production-wiring-classification)`.

**Empirical adjustment.** The `ExpectedResultSets` constant pins at
**23**, not V1's documented 22 — the canary's `NextResultAsync` loop
observes a leading validation/sanity-check projection V1's
per-processor walk doesn't enumerate. Truth is the canary (R6:
canary is V2's load-bearing forcing function).

**Coverage tests now passing (10 new):**
- `OssysProductionWiringParityTests.``5.1.γ row 32: each MetadataExtractionError variant maps to a distinct ValidationError code`` `
- `OssysProductionWiringParityTests.``5.1.γ row 32: RowMappingFailure ValidationError carries resultSet + rowIndex metadata`` `
- `OssysProductionWiringParityTests.``5.1.γ row 32: TransientSqlError ValidationError carries sqlNumber metadata`` `
- `OssysProductionWiringParityTests.``5.1.γ row 32: classify lifts RowMappingException to RowMappingFailure`` `
- `OssysProductionWiringParityTests.``5.1.γ row 32: classify lifts non-SqlException to OtherSqlError`` `
- `OssysProductionWiringParityTests.``5.1.γ row 34: transientSqlNumbers covers the documented cutover-critical numbers`` `
- `OssysProductionWiringParityTests.``5.1.γ row 34: isTransientSqlError refuses non-SqlException`` `
- `OssysProductionWiringParityTests.``5.1.γ row 34: retry pipeline retries until the operation succeeds`` `
- `OssysProductionWiringParityTests.``5.1.γ row 34: retry pipeline surfaces the final exception after retries exhaust`` `
- `OssysProductionWiringParityTests.``5.1.γ row 34: retry pipeline does not retry on non-matching exceptions`` `
- `OssysProductionWiringParityTests.``5.1.γ row 35: V2 surfaces result-set count mismatch on OSSYS rowsets`` `
- `OssysProductionWiringParityTests.``5.1.γ row 35: every MetadataExtractionError variant produces a distinct code`` `
- (plus three more on the resultSetContractCheck function)

---

### Row 36 — 2026-05-18 (closed by slice 5.13.progress-callback)

**Original classification (slice 5.1.γ, 2026-05-17):** 🟠 NOT-MAPPED.
V2 had no progress observation; `runAsync` was opaque start to
finish.

**Reclassified (slice 5.13.progress-callback, 2026-05-18):**
🔵 V2-EXTENSION.

**Rationale.** V2 introduces `MetadataSnapshotRunner.ProgressObservation`
(record of `ResultSetIndex × ResultSetName × RowCount`) +
`OnRowsetComplete` callback alias + a three-arity
`runAsyncWithProgress` entry point + a `noOpProgress` default + a
two-arity `runAsync` convenience overload that delegates with no-op.
V2 is **structurally stronger** than V1 — V1 wired
`ITaskProgressAccessor` (a heavyweight DI abstraction); V2's F#
callback is a simple value-typed seam consumers can wrap with their
own TUI / stdout / Spectre adapter without DI plumbing. The canary
end-to-end test asserts the callback fires for every observed
rowset (23 of them) in source order.

**Coverage tests now passing:**
- `OssysProductionWiringParityTests.``5.1.γ row 36: V2 carries per-rowset progress observation on OSSYS extraction`` `
- `OssysProductionWiringParityTests.``5.1.γ row 36: noOpProgress is a no-throw default`` `
- `OssysExtractionCanaryTests.``Slice 5.13.progress-callback canary: progress fires for every observed rowset`` `

---

### Rows 160 + 163 (Phase-2 UPDATE) — 2026-05-18 (closed by slice 5.13.cdc-silence-cross-emitter; STRUCTURAL FIX)

**Closure of Phase 8 T-30-green blocker #1 (DATA axis V2-driver flip).**
The third and final of the three Phase 8 blocking deliverables —
*"the highest-leverage single deliverable in the entire chapter
sequence"* per V2_DRIVER and CLAUDE.md operating disciplines.

**Method.** Slice opens by walking the first-principles claim: V2's
full data-emission pipeline must produce CDC-silent output on
idempotent redeploy across emitters with CDC-tracked tables.
Existing canary covers single-emitter (`StaticSeedsEmitter` alone,
no Phase-2 UPDATE). New canary `CdcSilenceCrossEmitterTests` extends
to cross-emitter + Phase-2 UPDATE under live SQL Server CDC.

**Discovery (the empirical finding).** The cross-emitter test
initially failed with `baseline=5, post=9` — 4 NEW CDC entries
fired per idempotent redeploy. Two compounding structural bugs:

1. **Phase-1 MERGE incorrectly UPDATEs deferred columns.** The
   MERGE's `WHEN MATCHED` UpdColumns included deferred columns
   (those NULLed in Phase-1 VALUES to break FK cycles). On
   redeploy, the cdcAware predicate evaluated `Target.col (=1) <>
   Source.col (=NULL)` → TRUE → UPDATE fired → target's deferred
   column set BACK to NULL. CDC captured the change (2 entries:
   operation 3 "before" + operation 4 "after").

2. **Phase-2 UPDATE has no change-detection predicate.** Even
   without bug 1, the Phase-2 UPDATE would still fire
   unconditionally on PK match, setting the deferred column to
   its known value. SQL Server's CDC captures the no-op standalone
   UPDATE (2 entries: __$operation 3 + 4). Unlike `MERGE WHEN
   MATCHED UPDATE`, standalone UPDATE does NOT carry the SQL Server
   2022 no-op optimization.

The two leaks compounded: 2 + 2 = 4 CDC entries per redeploy per
deferred-column row. Matches the observed `post - baseline = 4`.

**Structural fix (per first-principles directive: V2 must guarantee
silence structurally, not lean on SQL Server's optimizer).**

| Fix | Location | Effect |
|---|---|---|
| Exclude deferred from Phase-1 `UpdColumns` | `StaticSeedsEmitter.renderMerge` + `MigrationDependenciesEmitter.renderMerge` | Phase-1 MERGE doesn't touch deferred columns at all. When all updatable columns are deferred, the WHEN MATCHED branch disappears entirely (MERGE has only WHEN NOT MATCHED INSERT). |
| Add change-detection predicate to Phase-2 UPDATE WHERE | `ScriptDomBuild.UpdateBuildArgs.CdcAware` + `buildUpdateStatementCore.phase2DifferencePredicate` | Standalone UPDATE WHERE clause becomes `[pk] = <litpk> AND (<set-col-differs> OR ...)`. No-op redeploys filter at the boundary. |
| Thread cdcAware to renderUpdate | Both emitter `renderUpdate` signatures + their `kindToScript` callers | The flag plumbs through; `Profile.CdcAwareness` determines per-kind dispatch. |

**Empirical verification.** After the fix:
- C0 structural test: Country MERGE has predicate; LegacyOrder MERGE has only INSERT; Phase-2 UPDATE has predicate. ✓
- C1 single-emitter via composer: redeploy CDC entries = 0. ✓
- C2 cross-emitter (Country Static + LegacyOrder Migration with self-FK cycle): redeploy CDC entries = 0. ✓
- C3 Phase-2 UPDATE redeploy: CDC entries = 0. ✓
- C4 sensitivity (changed content): CDC entries > 0 (proves canary mechanism real). ✓

**Better-than-V1.** V1's `PhasedDynamicEntityInsertGenerator` has
the same two structural bugs (Phase-1 doesn't exclude deferred;
Phase-2 has no change-detection predicate). V2 now structurally
guarantees idempotent CDC silence — V1 leaks under the same
workload.

**Rows reclassified:**
- Row 160 (Phase-2 cycle-breaking): 🟢 PARITY (partial; chapter
  4.1.B) → 🟢 PARITY (full) + 🔵 V2-EXTENSION on the CDC silence
  axis. V2 now structurally CDC-silent under deferred-FK
  redeploy; V1 is not.
- Row 163 (MERGE shape): 🟢 PARITY (chapter 4.1.B) → 🔵
  V2-EXTENSION on the deferred-column filtering axis. V2's MERGE
  excludes deferred columns from WHEN MATCHED UPDATE; V1's MERGE
  shape didn't.

**Coverage tests now passing (5 new):**
- `CdcSilenceCrossEmitterTests.``5.13.cdc-silence-cross-emitter C0: cross-emitter composer output contains cdcAware MERGE predicate for both emitters`` `
- `CdcSilenceCrossEmitterTests.``5.13.cdc-silence-cross-emitter C1: composer single-emitter redeploy fires zero CDC captures`` ` (Docker-gated)
- `CdcSilenceCrossEmitterTests.``5.13.cdc-silence-cross-emitter C2: composer cross-emitter (Static + Migration) redeploy fires zero CDC captures`` ` (Docker-gated)
- `CdcSilenceCrossEmitterTests.``5.13.cdc-silence-cross-emitter C3: Phase-2 UPDATE redeploy fires zero NEW CDC captures (discovery)`` ` (Docker-gated)
- `CdcSilenceCrossEmitterTests.``5.13.cdc-silence-cross-emitter C4 sensitivity: changed-content composer redeploy DOES fire CDC`` ` (Docker-gated)

---

### Row 174 — 2026-05-18 (closed by slice 5.13.identity-axis-closure)

**Original classification (slice omnibus, 2026-05-18):** 🟢 PARITY
(partial). The Notes claimed "Slice δ ships ByEmail; BySsKey /
Regex / FallbackToSystemUser deferred to slice ε."

**Reclassified (slice 5.13.identity-axis-closure, 2026-05-18):**
🟢 PARITY (full).

**Rationale.** Same audit-catch pattern as row 160 — the "deferred
to slice ε" claim was stale at the time of the audit.
`UserFkReflowPass.applyStrategy` (in
`src/Projection.Core/Passes/UserFkReflowPass.fs`) handles **all
four UserMatchingStrategy DU variants** today (lines 200-220):
ByEmail, BySsKey, ManualOverride, FallbackToSystemUser. The
example-level test surface (`UserFkReflowPassTests.fs` —
26 tests including "Slice ε: all four strategy variants produce
decisions (closed-DU coverage)") shipped alongside the
implementation.

**Per Phase 8 acceptance criterion** (CUTOVER_READINESS_BRIEF
blocker #2: "Property test asserting symmetry of matched +
unmatched diagnostics on shared fixtures"), this slice cashes out
the property surface:

- **S1 totality** — every source user appears in exactly one of
  `Mapping.Keys` or `Unmatched` (exhaustive partition); FsCheck
  property across 4 strategy variants
- **S2 per-source diagnostic count** — matched → 0 Warnings;
  unmatched → 1 Warning
- **S3 diagnostics count** = `Unmatched.Count`
- **S4 permutation invariance** — source-list ordering doesn't
  affect output
- **S5 idempotence** — repeated `discover` produces equal output
  (T1 byte-determinism)
- **FallbackToSystemUser safety net** — `Set.isEmpty Unmatched`
  structurally under three primary-strategy variants
  (ByEmail / BySsKey / ManualOverride)

V1's `UserMatchingEngine.cs` collapsed `Regex` into V2's
`ManualOverride` per `Policy.fs:295-304` pre-scope rationale —
V1's Regex is structurally indistinguishable from operator-supplied
transformation for V2's algebraic purposes. The kickoff prompt's
"Regex" mention is subsumed; the DU is closed at 4 variants.

**Companion: cross-axis registry filters.**
`TransformRegistry.byDomain` + `TransformRegistry.byOverlayAxis`
ship per the two-consumer threshold (Data axis at slice
5.13.data-emission-registry + Identity axis at this slice).
The filters compose across projects: the IDENTITY axis confidence
view assembles via `(RegisteredTransforms.all @
RegisteredDataTransforms.all) |> byDomain Identity`. No parallel
aggregator required.

**Coverage tests now passing:**
- `UserFkReflowPropertyTests` — 13 FsCheck properties across the
  four strategy variants + 1 generator-arb-bootstrap fact
- `IdentityAxisRegistryTests` — 8 cross-axis registry-filter tests
  (byDomain / byOverlayAxis / filter composition / disjoint
  partition / cross-project validation)

---

### Row 160 — 2026-05-18 (closed by slice 5.13.data-emission-registry)

**Original classification (slice 5.5.β+γ+δ, 2026-05-18):** 🟢 PARITY
(partial). The Notes column claimed "**Open item per slice η**:
cross-emitter global phase ordering (Phase-1-ALL across StaticSeeds +
Migration + Bootstrap, then Phase-2-ALL) NOT YET REIFIED."

**Reclassified (slice 5.13.data-emission-registry, 2026-05-18):**
🟢 PARITY (full).

**Rationale.** The "NOT YET REIFIED" claim was **stale at the time
the matrix was authored**. `DataEmissionComposer.composeRenderedFull`
(chapter 4.1.B slice ι, shipped 2026-05-11) already walks the
unioned artifact in topological order, concatenating ALL Phase-1
texts (across all kinds + all emitters) before ALL Phase-2 texts.
The slice θ partition assertion guarantees each kind belongs to
exactly one emitter under a given `DataComposition`; cross-emitter
ordering is therefore a structural property of the topological walk,
not a separate reification.

The audit slice 5.5.β+γ+δ noted only the WITHIN-emitter test surface
(slice ι test asserts ordering across kinds inside StaticSeedsEmitter
alone). The cross-emitter property test was missing — slice
5.13.data-emission-registry adds it:

```
DataEmissionComposerTests.``5.13.data-emission-registry:
    composeRenderedFull global-Phase1-then-Phase2 holds across emitters (matrix row 160)``
```

The test exercises a 2-kind catalog where Country lives in
StaticSeedsEmitter (Static modality) and LegacyOrder lives in
MigrationDependenciesEmitter (migration context row); asserts both
emitters' Phase-1 outputs precede either emitter's Phase-2 output.

**Companion: data-emission registry.** The slice also adds
`RegisteredDataTransforms.all` — four `RegisteredTransformMetadata`
entries (composer + three emitters) classifying each transformation
site per pillar 9. The composer's `compositionDispatch` site
(reading `Policy.Emission.DataComposition`) classifies as
`OperatorIntent Emission`; its `globalPhaseOrdering` +
`partitionAssertion` sites classify as `DataIntent`. The
MigrationDependenciesEmitter splits operator-published inputs
(`migrationRowEmission`, `userRemapRewrite` →
`OperatorIntent Insertion`) from the structural cycle-resolution
(`deferredFkPhase2` → `DataIntent`). StaticSeedsEmitter is fully
DataIntent (Profile.CdcAwareness is evidence per A18 amended).
BootstrapEmitter ships `NotImplementedInV2 of rationale` per the
slice-ζ MVP empty-stub posture; the rationale substantively names
chapter 4.2 slice η as the trigger.

**Coverage tests now passing:**
- `DataEmissionComposerTests.``5.13.data-emission-registry: composeRenderedFull global-Phase1-then-Phase2 holds across emitters (matrix row 160)`` `
- `DataEmissionComposerTests.``5.13.data-emission-registry: cross-emitter coverage holds the partition invariant (no overlap)`` `
- 13 new `RegisteredDataTransformsTests` (cardinality + create-validates + StageBinding + Domain + per-Site classifications + skeletonView / overlayView / overlayAxes filters + Core+Data registry composition)

---

### Row 33 — 2026-05-18 (closed by slice 5.13.command-timeout + sibling-wrapper-collapse)

**Original classification (slice 5.1.γ, 2026-05-17):** 🟡 DIVERGENCE.
V2 set `command.CommandTimeout <- 0` unconditionally; V1 reads from
`SqlExecutionOptions.CommandTimeoutSeconds` (caller-tunable).

**Reclassified (slice 5.13.command-timeout + sibling-wrapper-collapse,
2026-05-18):** 🟢 PARITY.

**Rationale.** Executes the re-open path pre-codified in
`DECISIONS 2026-05-17 (slice 5.1.γ)`. A new `RunOptions` record carries
`CommandTimeoutSeconds : int option`; `None` preserves canary
semantics (sets `CommandTimeout <- 0`, unlimited), `Some n` sets the
ADO.NET timeout to `n` seconds (V1-style). The runner's two entry
points (`runAsync` / `runAsyncWithOptions`) follow the
**sibling-wrapper discipline** (principled count = 2: zero-default +
full-explicit). The 3-arity `runAsyncWithProgress` middle-tier
introduced in the progress-callback slice **retires** as part of this
collapse — production CLI surfaces compose `runAsyncWithOptions`
with `{ defaultOptions with ... }` overriding only the axes they
need.

**Coverage tests now passing:**
- `OssysProductionWiringParityTests.``5.1.γ row 33: defaultOptions preserve canary semantics`` `
- `OssysProductionWiringParityTests.``5.1.γ row 33: RunOptions record threads CommandTimeoutSeconds for production CLI`` `

**The entire OssysProductionWiringParityTests file now has zero Skip stubs** —
all 5 rows (32 / 33 / 34 / 35 / 36) shipped in the chapter-5.1.γ
production-wiring arc.

---

### Row 42 — 2026-05-18 (closed by slice 5.13.module-non-empty-invariant; LR1)

**Original classification (slice 5.2.α.module, 2026-05-18):** 🟡 DIVERGENCE.
V2's `Module.create` permitted empty `Module.Kinds`; the gap was an
under-specification, not a deliberate weakening (per
`DECISIONS 2026-05-18 (slice 5.2.α.module)` path (a) preferred but
deferred to the next time `Module.create` was touched).

**Reclassified (slice 5.13.module-non-empty-invariant, 2026-05-18):**
🟢 PARITY.

**Rationale.** LR1 ships as part of the Phase 8 lead-up-refactors
queue. `Module.create` now lifts V1's `ModuleModel.Create` non-empty
Entity invariant: empty `kinds` list fails with
`module.kinds.empty` ValidationError. Per A39 (aggregate-root
smart-constructor invariants). The check sits BEFORE the duplicate
SsKey check so the failure mode is reported as cardinality-empty,
not duplicate-key-on-zero-keys.

V2 is **stronger than V1 (and stronger than V1-parity)** —
V1's check throws on `entities.IsDefaultOrEmpty`; V2's check
fails-fast with a typed `ValidationError` carrying the offending
module's SsKey. Test fixtures that construct empty modules via the
`IRBuilders.mkModule` literal builder are unaffected (the builder is
a documented "trusted by construction" bypass per A39's "consumers
that flow through `create` trust the value" contract).

**Coverage tests now passing:**
- `OssysDomainModuleParityTests.``5.2.α row 42: V2 Module.create rejects empty Kinds per V1 parity (LR1)`` `
- `OssysDomainModuleParityTests.``5.2.α row 42: V2 Module.create accepts non-empty Kinds`` `

**Discovered side-effect (worth noting).** The new invariant
surfaced a JSON-shape mismatch in
`OsmRowsetReaderTests.``Closed-DU expansion: SnapshotJson + SnapshotRowsets coexist; both paths usable from same caller`` `
— the test's JSON fixture used `"entities": []`. The fixture grew to
include a single User entity (matching the rowset bundle); both
SnapshotJson and SnapshotRowsets paths now produce equivalent
non-empty Catalogs. This is exactly the **ghost-module bug** the
LR1 invariant prevents — the test was silently constructing
zero-Kind modules.

---

### Row 23 — 2026-05-18 (discovered by slice 5.2.α.misc)

**Original classification (slice 5.1.α, 2026-05-17):** 🟠 NOT-MAPPED.
The row claimed V2 carried no trigger axis in `Catalog` IR; trigger
named V2 IR refinement adds `Catalog.Triggers` axis AND a downstream
emitter demands trigger evidence.

**Reclassified (slice 5.2.α.misc, 2026-05-18):** 🟢 PARITY (IR
shipped; emitter status: structured trigger emission deferred
pending SSDT realization gate per chapter A.0' slice η).

**Rationale.** Slice 5.2.α.misc's audit of V1's `TriggerModel.cs`
discovered that V2 already carries `Trigger` IR in `Kind.Triggers`
(chapter A.0' slice γ; L3-S4 sub-axiom). The original row 23 was
authored against a stale view of V2's IR — the matrix-row author
in slice 5.1.α inventoried `IOutsystemsMetadataReader.cs`'s DTOs
without cross-checking V2's existing `Catalog.fs` for the
corresponding IR axis. The OSSYS-source rowset 18 `#Triggers`
lifts into the existing V2 `Trigger` shape (not a new axis); the
cash-out work for row 23 is the rowset 18 → V2 `Trigger` mapping
in `MetadataSnapshotRunner`, not the IR construction the original
row implied.

**Companion row.** Matrix row 61 (slice 5.2.α.misc) carries the
full V1 → V2 mapping for `TriggerModel`. The triple (row 23
amendment + row 61 + the existing IR) is the parity claim's
complete record.

---

### Rows 12 + 53 + 182 (CHECK + DEFAULT emit clauses) — 2026-05-18 (closed by slice 5.13.column-features-emit)

**Original classifications:**
- Row 12 (slice 5.1.α, 2026-05-17): 🟠 NOT-MAPPED. V2's IR carried
  no CHECK-constraint axis. Trigger: "V2 IR refinement adds a
  CHECK-constraint field AND a downstream emitter (SSDT or DACPAC)
  demands it." The cluster A1 closure (2026-05-18) shipped the
  IR-side rowset 7 `#ColumnCheckReality` → `Kind.ColumnChecks` lift.
- Row 53 (slice 5.2.α.attribute, 2026-05-18): 🟠 NOT-MAPPED. V2
  carried `Attribute.DefaultValue : SqlLiteral option` (typed
  Definition) but no DDL-emitter consumer surfaced the value at the
  CREATE TABLE boundary.
- Row 182 (slice 5.3.β, 2026-05-18): 🟢 PARITY (95%). The 5%
  delta named "column defaults + CHECK constraints + computed
  columns (V1 319-364) — ColumnDef IR fields exist; emit layer
  deferred per slice ζ candidates."

**Reclassified (slice 5.13.column-features-emit, 2026-05-18):**
Row 12 → 🟢 PARITY (rowset-to-emit closure). Row 53 → 🟢 PARITY
(DEFAULT clause emission closure; constraint identity carriage
deferred to a follow-on slice when a `DF_TableName_ColumnName`
round-trip requirement surfaces). Row 182 → 🟢 PARITY (computed
columns remain deferred per IR-grows-under-evidence; no V2 consumer
populates `Attribute.Computed` today).

**Rationale.** Slice 5.13.column-features-emit closes the emit-side
gap for chapter A.0' slice ε IR lifts (DEFAULT + CHECK). The
realization-layer additions:

- `Projection.Targets.SSDT/Statement.fs`:
  - `ColumnDef` extended with `DefaultValue : SqlLiteral option`
    + `DefaultName : string option`.
  - New `ColumnCheckDef` record (`Name : string option *
    Definition : string * IsNotTrusted : bool`).
  - `Statement.CreateTable` constructor extended with a fifth
    `ColumnCheckDef list` argument (closed-DU expansion empirical-test
    discipline — F# field-/variant-extension errors light up at
    literal-construction sites only).
- `Projection.Targets.SSDT/ScriptDomBuild.fs`:
  - `columnDefinition` emits `DEFAULT <literal>` via
    `DefaultConstraintDefinition` on `Constraints`; constraint
    identity carriage via `ConstraintIdentifier` when
    `DefaultName` populated. The literal flows through
    `buildSqlLiteral` — same typed-AST path the MERGE / UPDATE
    statements use, so DEFAULT values are byte-identical across
    emission surfaces.
  - New private `checkConstraint` builder parses `chk.Definition`
    via `TSql160Parser.ParseBooleanExpression` and embeds the
    typed `BooleanExpression` into ScriptDom's
    `CheckConstraintDefinition`. Parse-failure path falls back to
    a raw-text comparison (preserves SQL surface; real production
    expressions parse cleanly under TSql160Parser).
  - `buildCreateTable` extended to consume `ColumnCheckDef list`;
    table-level CHECK constraints follow PK + FK in declaration
    order (matches V1's CREATE TABLE shape).
- `Projection.Targets.SSDT/Render.fs`:
  - `CreateTable` arm collapsed into the `ScriptDomGenerate`
    delegation arm (single source of truth; prior
    StringBuilder duplication retired).
- `Projection.Targets.SSDT/SsdtDdlEmitter.fs`:
  - `columnDef` populates `DefaultValue` from `Attribute.DefaultValue`
    (today's JSON adapter path); `DefaultName = None` pending the
    `#ColumnReality.DefaultDefinition` rowset wiring (separate
    slice).
  - New private `columnCheckDef` projects `Kind.ColumnChecks` entries
    to the realization-layer shape.
  - `createTableStatement` threads `k.ColumnChecks |> List.map
    columnCheckDef` as the fifth `Statement.CreateTable` argument.

**Coverage tests now passing:**
- `SsdtDdlEmitterTests.``Slice 5.13.column-features-emit: DEFAULT clause surfaces in CREATE TABLE body for typed-literal default`` `
- `SsdtDdlEmitterTests.``Slice 5.13.column-features-emit: CHECK constraint surfaces in CREATE TABLE body via TSql160Parser`` `
- `SsdtDdlEmitterTests.``Slice 5.13.column-features-emit: T1 byte-determinism holds with DEFAULT + CHECK`` `

**Deferred axes (no consumer pressure yet):**
- DEFAULT constraint **identity** (`DF_<Table>_<Column>` round-trip
  via `DefaultName`) — the field exists; populating it requires
  rowset path lifting `#ColumnReality.DefaultConstraintName`
  (separate slice; matrix row 53 cash-out completion when (a) a
  manifest emitter names defaults, or (b) DDL round-trip preserves
  V1 constraint identity).
- **Computed columns** (V2's `Attribute.Computed : ComputedColumnConfig
  option`) — Statement.ColumnDef does NOT carry computed-column
  axes today per IR-grows-under-evidence; no rowset path or JSON
  source populates `Attribute.Computed`. Adds when first consumer
  surfaces.
- CHECK constraint **NOCHECK state** (`ColumnCheckDef.IsNotTrusted`)
  is carried but not emitted inline — ScriptDom doesn't model
  WITH NOCHECK in the inline CHECK clause; round-trip preservation
  is a post-emit ALTER TABLE concern (matrix row 59 cash-out).

---

### Rows 58 + 59 (FK ON UPDATE + WITH NOCHECK) — 2026-05-18 (closed by slice 5.13.fk-features-emit, paired with slice 5.13.smart-constructor-lift)

**Original classifications (slice 5.2.α.relationship, 2026-05-18):**
- Row 58: 🟠 NOT-MAPPED. V2's `Reference.OnDelete : ReferenceAction`
  carried only delete; ON UPDATE dropped at the adapter boundary;
  V2 didn't emit ON UPDATE clauses.
- Row 59: 🟠 NOT-MAPPED. V2's `Reference.HasDbConstraint : bool` was
  binary (presence/absence); no FK-trust-state axis; V2 emitted no
  WITH NOCHECK preservation step.

**Reclassified (slice 5.13.fk-features-emit, 2026-05-18):**
Row 58 → 🟢 PARITY (emit-side shipped; adapter wiring deferred
until a JOIN slice threads `#FkReality.UpdateAction` through
`OssysReferenceRow` → `ReferenceRow`). Row 59 → 🟢 PARITY (emit-
side shipped; adapter wiring deferred for the same JOIN slice).

**Rationale.** Slice 5.13.fk-features-emit closes the emit-side gap
on the FK axis, mirroring the slice 5.13.column-features-emit
pattern on the column axis. Realization-layer additions:

- `Projection.Core/Catalog.fs` — `Reference` IR (extended in the
  paired smart-constructor-lift slice):
  - `OnUpdate : ReferenceAction option` (matrix row 58) — `None`
    = unstated (V1 default; SQL Server emits no ON UPDATE clause).
  - `IsConstraintTrusted : bool` (matrix row 59) — `true` (V1
    default); `false` triggers the post-CREATE-TABLE ALTER
    statement.
- `Projection.Targets.SSDT/Statement.fs`:
  - `ForeignKeyDef` extended with `OnUpdate : ReferenceActionSql
    option` + `IsConstraintTrusted : bool`.
  - New `Statement.AlterTableNoCheckConstraint of TableId *
    constraintName : string` variant.
- `Projection.Targets.SSDT/ScriptDomBuild.fs`:
  - `foreignKeyConstraint` emits `cons.UpdateAction <-
    toDeleteUpdateAction action` when `OnUpdate = Some action`;
    omits the clause when `None` (preserves V1 emission shape).
  - New `buildAlterTableNoCheckConstraint` builder uses
    `AlterTableConstraintModificationStatement` with
    `ExistingRowsCheckEnforcement = NoCheck` + `ConstraintEnforcement
    = Check` → renders as `ALTER TABLE <table> WITH NOCHECK CHECK
    CONSTRAINT [<fk>]` (verified against ScriptDom's
    `Sql160ScriptGenerator` output).
  - Closed-DU dispatcher (`buildStatement`) extended.
  - Shared `toDeleteUpdateAction` private helper eliminates the
    duplicate ReferenceActionSql → DeleteUpdateAction mapping that
    OnUpdate + OnDelete would otherwise carry separately.
- `Projection.Targets.SSDT/Render.fs` — `AlterTableNoCheckConstraint`
  threaded into the `ScriptDomGenerate` delegation arm (single
  source of truth alongside CREATE TABLE / CREATE INDEX /
  SetExtendedProperty).
- `Projection.Targets.SSDT/SsdtDdlEmitter.fs`:
  - `fkDef` populates `OnUpdate` + `IsConstraintTrusted` from
    `Reference`.
  - New private `untrustedFkAlters` yields one
    `AlterTableNoCheckConstraint` per `IsConstraintTrusted = false`
    FK. Wired into BOTH per-kind `kindToSsdtFile` (emitSlices
    artifact body) AND the catalog-wide `statements` stream.
    Statements ordered: CREATE TABLE → ALTER TABLE NOCHECK (per
    FK) → CREATE INDEX → SetExtendedProperty.
- `Projection.Targets.SSDT/DacpacEmitter.fs` —
  `isSchemaStatement` accepts the new variant.
- `Projection.Pipeline/Deploy.fs` — deploy-time stream handles
  the new variant as a DDL-class statement (flushes bulk before
  the ALTER).

**Coverage tests now passing (5 new):**
- `SsdtDdlEmitterTests.``Slice 5.13.fk-features-emit: OnUpdate = None
  omits the ON UPDATE clause (V1 emission shape)`` `
- `SsdtDdlEmitterTests.``Slice 5.13.fk-features-emit: OnUpdate = Some
  Cascade emits ON UPDATE CASCADE`` `
- `SsdtDdlEmitterTests.``Slice 5.13.fk-features-emit:
  IsConstraintTrusted = true omits ALTER TABLE WITH NOCHECK`` `
- `SsdtDdlEmitterTests.``Slice 5.13.fk-features-emit:
  IsConstraintTrusted = false emits post-CREATE-TABLE ALTER TABLE
  WITH NOCHECK CHECK CONSTRAINT`` ` (includes statement-ordering
  assertion: ALTER must come after CREATE)
- `SsdtDdlEmitterTests.``Slice 5.13.fk-features-emit: T1 byte-
  determinism holds for OnUpdate + WITH NOCHECK emissions`` `

**Deferred (no consumer pressure yet):**
- Rowset adapter JOIN — `OssysReferenceRow` (per-attribute logical
  FK edges from V1 `#References`) needs to JOIN
  `OssysFkRealityRow` (per-FK-constraint sys.foreign_keys
  reflection) on (EntityId, parent column) to populate
  `OnUpdate` + `IsConstraintTrusted`. Today's `toBundle` does NOT
  perform this JOIN; the rowset path produces References with the
  smart-constructor defaults (`OnUpdate = None`,
  `IsConstraintTrusted = true`). The emit-side is positioned for
  the JOIN slice; canary remains green because OutSystems-shape
  fixtures don't carry deployed WITH NOCHECK FKs.

---

### Rows 55 + 56 (Index features: IGNORE_DUP_KEY + ALTER INDEX DISABLE + DATA_COMPRESSION) — 2026-05-18 (closed by slice 5.13.index-features-emit, partial for row 56)

**Original classifications (slice 5.2.α.index, 2026-05-18):**
- Row 55: 🟠 NOT-MAPPED. V2's `Index` carried neither `IsDisabled`
  nor `IgnoreDuplicateKey`.
- Row 56: 🟠 NOT-MAPPED. V2's `Index` had no partition / data-space /
  data-compression carriage.

**Reclassified (slice 5.13.index-features-emit, 2026-05-18):**
Row 55 → 🟢 PARITY (emit-side shipped; both axes fully wired through
to ScriptDom emission). Row 56 → 🟢 PARITY (partial — single-value
`DataCompression` shipped; `DataSpace` + per-partition compression
deferred to a follow-up slice when partitioned-index fixtures
surface; production canary is unaffected because OutSystems-shape
schemas don't use partitioning).

**Rationale.** Slice 5.13.index-features-emit closes the emit-side
gap on the index axis, mirroring the FK + column pair pattern.
The realization-layer additions:

- `Projection.Core/Catalog.fs`:
  - New `[<RequireQualifiedAccess>] type DataCompressionLevel =
    None | Row | Page` (mirrors ScriptDom's enum modulo the
    columnstore variants V2 has no fixture evidence for).
  - `Index` IR extended with `IgnoreDuplicateKey : bool` (defaults
    `false` — V1 default) + `IsDisabled : bool` (defaults `false`)
    + `DataCompression : DataCompressionLevel option` (defaults
    `None` — V1 default: no explicit DATA_COMPRESSION clause).
  - `Index.create` updated to default the new fields.
- `Projection.Targets.SSDT/Statement.fs`:
  - New `IndexDataCompressionSql` DU (realization-layer mirror of
    the Core DU).
  - `IndexDef` extended with the same three fields (closed-DU
    expansion at the realization layer mirrors Core's shape).
  - New `Statement.AlterIndexDisable of TableId * indexName :
    string` variant.
- `Projection.Targets.SSDT/ScriptDomBuild.fs`:
  - `buildCreateIndex` emits `IGNORE_DUP_KEY = ON` via
    `IndexStateOption` with `IndexOptionKind.IgnoreDupKey` when
    `IgnoreDuplicateKey = true`.
  - `buildCreateIndex` emits `DATA_COMPRESSION = <level>` via
    `DataCompressionOption.CompressionLevel` when
    `DataCompression = Some _`. The realization-layer DU
    (`IndexDataCompressionSql`) maps 1:1 to ScriptDom's
    `DataCompressionLevel` (the namespace prefix is required at
    the join site because Core's `DataCompressionLevel` shares
    the type-name — intentional parallel modeling per pillar 8
    ubiquitous language).
  - New `buildAlterIndexDisable` builder uses
    `AlterIndexStatement` with `AlterIndexType.Disable` →
    renders as `ALTER INDEX [name] ON [Schema].[Table] DISABLE`.
  - Closed-DU `buildStatement` dispatcher extended for the new
    variant.
- `Projection.Targets.SSDT/Render.fs` — new variant joins the
  ScriptDomGenerate delegation arm.
- `Projection.Targets.SSDT/SsdtDdlEmitter.fs`:
  - `indexStatements` maps `idx.DataCompression` (Core DU) to the
    realization-layer `IndexDataCompressionSql` and populates the
    three new `IndexDef` fields.
  - New private `disabledIndexAlters` yields one
    `AlterIndexDisable` statement per disabled non-PK index.
    Wired into BOTH per-kind `kindToSsdtFile` (emitSlices artifact
    body) AND the catalog-wide `statements` stream. Order: CREATE
    TABLE → ALTER NOCHECK (per FK) → CREATE INDEX → ALTER DISABLE
    (per disabled index) → SetExtendedProperty.
- `Projection.Targets.SSDT/DacpacEmitter.fs` —
  `isSchemaStatement` accepts the new variant.
- `Projection.Pipeline/Deploy.fs` — `executeStream` handles the
  new variant as a DDL-class statement.

**Coverage tests now passing (8 new):**
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit:
  IgnoreDuplicateKey = false omits IGNORE_DUP_KEY (V1 default)`` `
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit:
  IgnoreDuplicateKey = true emits IGNORE_DUP_KEY = ON in WITH
  clause`` `
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit:
  DataCompression = None omits DATA_COMPRESSION (V1 default)`` `
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit:
  DataCompression = Page emits DATA_COMPRESSION = PAGE`` `
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit:
  DataCompression = Row emits DATA_COMPRESSION = ROW`` `
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit:
  IsDisabled = false omits ALTER INDEX DISABLE (V1 default)`` `
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit:
  IsDisabled = true emits post-CREATE-INDEX ALTER INDEX
  DISABLE`` ` (asserts statement order)
- `SsdtDdlEmitterTests.``Slice 5.13.index-features-emit: T1
  byte-determinism holds across the new index axes`` `

**Deferred (no consumer pressure yet):**
- **Rowset adapter wiring** — V1's `#AllIdx` rowset surfaces
  the new axes (`IsDisabled` from `sys.indexes.is_disabled`,
  `IgnoreDuplicateKey` from `sys.indexes.ignore_dup_key`,
  `DataCompression` from `sys.partitions.data_compression`).
  Today's `MetadataSnapshotRunner.toBundle` does NOT thread
  these onto `Index` (the rowset-path `Index` literals default
  the new fields via `Index.create`). Cash-out trigger: a
  deployed target's reflection surfaces these axes AND the
  canary detects an emission delta.
- **Row 56 partition axis** (`DataSpace` + per-partition
  compression list) — single-value `DataCompression` covers the
  90% case (uniform compression across all partitions or no
  partition); partitioned-index fixtures are not yet in V2's
  test surface. The closed-DU `DataSpace = Filegroup |
  PartitionScheme` + per-partition list lands when a partitioned-
  index canary requires it.

---

### Rows 17 + 18 (FK reality + FK column rowsets adapter wiring) + Rows 55 + 56 (Index reality rowsets adapter wiring) — 2026-05-18 (closed by slice 5.13.blind-spot-closure)

**Original classifications (cluster A1, 2026-05-18):**
- Rows 17 + 18 lifted as typed `OssysFkRealityRow` + `OssysFkColumnRow`
  at the runner layer; the JOIN onto V2's per-attribute `ReferenceRow`
  was deferred at the cluster-A1 commit.
- Row 55 + 56 (rowset side) lifted as typed `OssysAllIdxRow` at the
  runner layer; the IsDisabled / IgnoreDupKey / DataCompression fields
  were captured but not wired through `parseIndexRowFor`.

**Reclassified (slice 5.13.blind-spot-closure, 2026-05-18):** all four
remain at their post-emit-side-shipped status (FK + index emit closed
at the trio of 5.13.fk-features-emit + 5.13.index-features-emit
slices); the adapter rowset-path JOIN now lands so production
emissions can round-trip the deployed reality:

- `MetadataSnapshotRunner.toBundle` now performs a 3-step JOIN
  (`OssysReferenceRow.AttrId → OssysFkColumnRow.ParentAttrId →
  OssysFkColumnRow.FkObjectId → OssysFkRealityRow`) so each
  per-attribute `ReferenceRow` carries `OnUpdate` + `IsConstraintTrusted`
  from the FK-constraint reflection.
- `IndexRow` extended with `IsDisabled` + `IgnoreDupKey` +
  `DataCompression : string option`; populated directly from the
  `OssysAllIdxRow` flags. `DataCompressionJson` parses via
  `tryParseUniformDataCompression` (System.Text.Json structured walk)
  yielding `Some "<level>"` when uniform across partitions, `None`
  when heterogeneous or absent.
- `parseReferenceRowFor` + `parseIndexRowFor` thread the new fields
  through to `Reference.create … with` / `Index.create … with`.

**Coverage tests:** existing canary suite + 1565 non-canary tests
pass with the JOIN active. No new tests needed at the adapter layer —
the existing emit-side canary tests assert the round-trip shape; the
adapter JOIN is the missing source of evidence those tests already
expect.

**What this closes for the next agent:** the row-58 + 59 + 55 +
56-partial deferrals named in the prior handoff's "rowset-adapter
JOIN follow-up" blind-spot. The composite-FK refactor (row 18
multi-column support) + row 56 partition-scheme axis remain as
named deferrals (no fixture pressure yet).

---

### TransformRegistry Emitter-stage coverage — 2026-05-18 (closed by slice 5.13.blind-spot-closure)

**Original framing (handoff 2026-05-18 — emit-features arc):**
The blind-spot entry named "TransformRegistry coverage gap on the
new emit-side helpers" — `untrustedFkAlters`, `disabledIndexAlters`,
`columnCheckDef`, the DefaultConstraint mapping, etc. The right
granularity (per the chapter A.4.7 slice δ precedent for the OSSYS
adapter) is one `registeredMetadata` entry per emitter, with each
emission feature as a classified `TransformSite` within its Sites
list.

**Closed by slice 5.13.blind-spot-closure (2026-05-18):**
`SsdtDdlEmitter.registeredMetadata : RegisteredTransformMetadata`
ships with eleven classified Sites — every V1-CreateTable +
V1-CreateIndex emit feature V2 carries (createTable / createIndex /
columnDefaultClause / columnCheckConstraint / foreignKeyConstraint /
alterTableNoCheckConstraint / alterIndexDisable /
indexIgnoreDuplicateKey / indexDataCompression / setExtendedProperty /
topologicalOrder), each carrying substantive Rationale + DataIntent
classification (the SSDT emitter projects evidence; A18 amended
forbids operator opinion at emit time).

`ManifestEmitter.build` now prepends `SsdtDdlEmitter.registeredMetadata`
to `RegisteredTransforms.all` so the totality-coverage scan reaches
the emit-stage Sites. The OSSYS adapter's `registeredMetadata`
remains in `Projection.Adapters.Osm` (cherry-pick boundary); future
sibling emitter `registeredMetadata` (Json / Distributions /
StaticPopulation / StaticSeeds / MigrationDependencies) lift when
parity audit evidence demands them.

**Coverage tests (6 new):**
`EmitterRegistrationsTests.fs` mirrors the
`AdapterRegistrationsTests.fs` shape — Name + Domain + StageBinding +
Sites enumeration + every-Site-DataIntent + every-Site-non-empty-
Rationale + TransformRegistry.create validation + joint-registry
assembly through ManifestEmitter.build.

---

### Render.fs StringBuilder retirement — 2026-05-18 (closed by slice 5.13.blind-spot-closure)

**Original framing (handoff 2026-05-18 — emit-features arc):**
The blind-spot entry named "Render.fs StringBuilder retirement
candidate" — only `InsertRow` + `SetIdentityInsert` had StringBuilder
paths after the column-features-emit slice; both had ScriptDom
builders already (`buildInsertRow` + `buildSetIdentityInsert`). The
StringBuilder path was StringBuilder-as-first-instinct legacy from
chapter 4.1.A.

**Closed by slice 5.13.blind-spot-closure (2026-05-18):**
`Render.toSql`'s per-variant arms collapsed into a single `_ ->`
default arm that routes every SQL-bearing Statement through
`ScriptDomBuild.buildStatement` + `ScriptDomGenerate.generateOne`.
Only `Blank` + `Comment` remain as named arms — they're terminal
text-formatting (newline + `-- ` prefix) for which no typed AST
exists. The four dead-weight per-call helpers retired:
`columnSqlType` + `formatSqlLiteral` (both public, zero external
consumers) + `actionSql` + `renderColumn` (both private, dead with
the StringBuilder CREATE TABLE arm). 8 imports and ~40 LOC retired.

The full SSDT emission chain now lives in `ScriptDomBuild` —
`Render.fs` reduces to four public functions: `quote` /
`tableQualified` (identifier-boundary helpers still consumed by
`Bulk` / `Deploy` / `RefactorLogEmitter`) + `toSql` (the unified
dispatcher) + `toText` (the seq folder). Pillar 1 (data-structure-
oriented) holds at full strength; pillar 7 gold-standard library is
the canonical surface for every SQL-bearing emit.

**Coverage:** existing test suite witnesses. T1 byte-determinism
preserved (`ScriptDomGenerate.generateOne` is the only path).

---

## Parity cash-out plans — what V2 work closes each gap

The matrix's Notes column carries the per-row brief; this section
expands the cash-out **shape, dependencies, and acceptance** for every
🟠 NOT-MAPPED + 🟡 DIVERGENCE + ⚫ V1-SUNSET row. Organized by axis
cluster rather than per-row so the reader sees the family of work
together. Each block ends with **priority**: the order of value (which
rows compound; which gate on other slices; which carry cutover risk).

### Cluster A1 — OSSYS-source physical-reflection rowset lifts (rows 11–18 + 23; 9 rows × 🟠 NOT-MAPPED)

**The shared axis.** V2's `MetadataSnapshotRunner.runAsync`
(`sidecar/projection/src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs`)
walks 22 result sets but parses only the first 5; rowsets 6–15 + 18
emit data V1 consumes that V2 currently discards.

**Per-rowset lift shape** (constant pattern):

1. Add F# record type `OssysXRow` to `MetadataSnapshotRunner.fs`
   (5–15 typed fields mirroring V1's DTO at the columns V2 will
   consume).
2. Add `mapXRow` ordinal reader following the existing
   `mapModuleRow` / `mapEntityRow` pattern (`readInt r 0`,
   `readString r 1`, …).
3. Add a parse + accumulate step in `runAsync` after the current
   rowset 5 parse; the skip-loop for downstream rowsets shrinks
   accordingly.
4. Extend `MetadataSnapshot` aggregate with the new field
   (`Indexes : OssysIndexRow list`, etc.).
5. **Optional** when downstream consumption demands: extend
   `CatalogReader.RowsetBundle` with the new lifted axis and add
   JOIN logic in `MetadataSnapshotRunner.toBundle`. Downstream V2
   IR fields (e.g., `Index.Filter` — already shipped chapter 4.5 —
   pick up the new evidence.

**Acceptance per row.** `OssysExtractionCanaryTests` (already gated
by the Docker warm container) gains one assertion exercising the
lifted axis against the synthetic OSSYS seed
(`Resources/ossys-edge-case.seed.sql`). If the axis feeds an emission
consumer, a downstream T1 byte-determinism test fires on the produced
SSDT artifact.

**Dependencies.** Independent at the row level — each rowset lifts in
isolation. Soft ordering: rows 15 + 16 (Indexes + IndexColumns) lift
together since IndexColumns FKs to Indexes by EntityId + IndexName.
Rows 17 + 18 (ForeignKeys + FK columns) lift together for the same
reason. Other rows are fully independent.

**Priority.** Rows 15 + 16 are highest-leverage because lifting them
**retires V2's structural dependence on V1's IndexJson rowset**
(row 26, ⚫ V1-SUNSET) — V2's index axis becomes V1-independent. Rows
17 + 18 unlock OSSYS-source FK reflection (different evidence than
V2's existing PhysicalSchema.ForeignKeys which reflects the deployed
target). Row 12 (ColumnChecks) is gated on a V2 IR refinement adding
CHECK-constraint carriage on Attribute; it lifts when that IR slice
opens. Row 23 (Triggers) requires a new `Catalog.Triggers` axis — its
own chapter; not a sub-slice. Rows 11 + 14 + 18 (ColumnReality,
PhysicalColumnsPresent) feed V2-source-side tightening that V2 doesn't
do today; lift when a tightening rule demands source evidence.

### Cluster A2 — Algebraic-join reconstruction (rows 19 + 20; 🟡 DIVERGENCE)

**Re-open trigger.** ≥2 V2 callers need attribute → FK or attribute →
HasFK navigation in a hot path; the algebraic join's O(N) per lookup
becomes a perf concern. Cash-out shape: materialize a precomputed
`Map<AttrId, FkConstraint list>` on `Catalog.create` and expose via
`Catalog.foreignKeysByAttribute` accessor. Acceptance: a perf bench
shows the materialized lookup outperforms the algebraic join at the
two consumer sites.

### Cluster A3 — JSON-aggregation rowset sunsets (rows 13, 21, 22, 24–28; 7 rows × ⚫ V1-SUNSET)

**Migration impact.** V2's `SnapshotJson` input variant
(`CatalogReader.SnapshotSource`) continues to consume historical
`osm_model.json` files but does not require V1 to keep emitting them.
The aggregation rowsets sunset alongside V1's emission path at
cutover+30 per `VISION.md` T-30 / T-15 ladder.

**One conditional**: row 26 (`#IdxJson`) — V2's `Catalog.Indexes` IR is
currently populated when the input arrives as `SnapshotJson` (V1 reads
`#IdxJson` → emits to `osm_model.json` → V2 parses). Before V1's
emission decomissions, V2 must lift rows 15 + 16 (Cluster A1) into
`OssysSql` to maintain index evidence via the structured path. **This is
the only row in the cluster with a sequencing dependency.**

### Cluster A4 — DatabaseName envelope (row 29; 🟡 DIVERGENCE)

**Re-open trigger.** A V2 emission consumer needs the database name
threaded through the Catalog (e.g., qualified-name rendering at the
emission layer that doesn't currently take database as a parameter).
Unlikely — the Catalog stays deployment-agnostic by design. If
triggered: thread `databaseName : string option` through emission
context, not through IR.

### Cluster A5 — Operator-debugging telemetry (row 30; 🟠 NOT-MAPPED)

**Cash-out shape.** Three V2 surfaces lift in lockstep:

1. **`ExtractionLog` observation accumulator** — F# module in
   `Projection.Adapters.OssysSql/ExtractionLog.fs` (parallel to V1's
   `SqlMetadataLog`). Records: `Snapshot of MetadataSnapshot * timestamp`
   on success; `Failure of ValidationError list * lastRowSnapshot option`
   on failure; `Request of rowsetName * parameters` per rowset Read.
2. **`MetadataSnapshotRunner.runAsync` parameter extension** —
   optional `log : ExtractionLog option = None`; the runner calls
   `ExtractionLog.recordRequest` before each rowset read and
   `ExtractionLog.recordFailure` / `recordSnapshot` at exit.
3. **`ExtractionLog.writeJson`** — `Utf8JsonWriter`-based emitter per
   the typed-AST-as-first-instinct discipline; writes the log to an
   operator-provided path.

**Dependencies.** Independent; entirely additive. **Acceptance.** A test
that runs extraction against a deliberately-malformed rowset and
asserts the JSON dump contains the failed-row snapshot.

**Priority.** Cash out when V2 ships a production CLI surface for
OSSYS extraction OR when a real cutover-windowed failure demands
post-mortem partial-state context.

### Cluster A6 — F# type system subsumes V1 JSON-shape check (row 31; ⚫ V1-SUNSET)

No parity work. Sunset rationale lives in the row's Notes.

### Cluster A7 — Production wiring (rows 32–36; slice 5.1.γ; 5 rows)

**Row 32 (🟠 NOT-MAPPED — exception classification).**

**Cash-out shape.** Closed-DU
`MetadataExtractionError = RowMappingFailure of context : RowMappingContext | ResultSetMissing of expectedSetName : string * gotCount : int | TransientSqlError of sqlException : SqlException * sqlNumber : int | OtherSqlError of message : string`
added to `MetadataSnapshotRunner.fs`. Replace the single `with ex ->`
clause with a `match ex with` that classifies into the DU; each
variant maps to a distinct `ValidationError` code
(`adapter.ossysSql.rowMapping` / `adapter.ossysSql.resultSetMissing` /
`adapter.ossysSql.transient` / `adapter.ossysSql.runFailed`). **Dependencies.**
Row 34's `TransientSqlError` variant lives here; rows 32 + 34 lift
together. Row 35's `ResultSetMissing` variant also lives here.
**Acceptance.** Property test asserts every classified exception
produces a distinct ValidationError code (no two variants collide).

**Row 33 (🟡 DIVERGENCE — command timeout).**

See `DECISIONS 2026-05-17 (slice 5.1.γ)`. Re-open via
`commandTimeoutSeconds : int option = Some 0` parameter to `runAsync`
— canary semantics preserved; production CLI passes operator-tunable
value via a `--command-timeout-seconds` flag. **Acceptance.** Canary
tests pass with default; new test asserts the value flows through
to `SqlCommand.CommandTimeout`.

**Row 34 (🟠 NOT-MAPPED — transient-error retry). ★ CUTOVER-CRITICAL.**

**Cash-out shape.** A Polly retry policy added to V2's `OssysSql`
adapter at two seams: (1) connection-open (if V2 grows
connection-factory ownership; today caller-owned), (2) command-execute
(`ExecuteReaderAsync`). Retry config: 3 attempts, exponential
backoff (1s / 2s / 4s base; jittered ±25%). Retry condition:
`SqlException.Number` ∈ {-2 (timeout), -1 (network drop),
40197 / 40501 / 40613 (Azure transients), 4060 / 18452 (auth
transients)}. Implementation in a new module
`Projection.Adapters.OssysSql/Retry.fs`. **Dependencies.** Row 32's
DU absorbs `TransientSqlError`; lift together. Caller-managed connection
lifecycle complicates connection-open retry — if V2 keeps caller-owned
connections, retry only wraps command-execute. **Acceptance.** A test
using a `MockSqlConnection` that throws a configured-transient on
first attempt and succeeds on second asserts the runner completes
successfully. **Status: blocking for cloud-OSSYS canary in dual-track
cutover-window per V2_DRIVER + R6 split-brain governance.**

**Row 35 (🟠 NOT-MAPPED — result-set contract enforcement).**

**Cash-out shape.** Track expected rowset count `[<Literal>] let
EXPECTED_RESULT_SETS = 22` constant. After the read loop completes
(rowsets 0–4 parsed + remaining 17 skipped), assert
`actualCount = EXPECTED_RESULT_SETS`; on breach emit
`adapter.ossysSql.resultSetContractBreach` error. **Dependencies.**
Row 32's DU absorbs `ResultSetMissing`. **Acceptance.** A test that
feeds a `SqlDataReader` returning only N<22 result sets asserts the
breach surfaces as a `ValidationError`.

**Row 36 (🟠 NOT-MAPPED — progress tracking).**

**Cash-out shape.** Optional
`onProcessorComplete : (rowsetName : string * rowCount : int) -> unit`
parameter to `runAsync`. The runner invokes after each rowset's parse
completes. Default no-op. CLI threads a callback that prints to stdout
(or a TUI progress bar at scale). **Dependencies.** Independent;
entirely additive. **Acceptance.** A test passes a counting callback;
asserts it's invoked 22 times (once per rowset, including the skipped
ones).

**Cluster priority.** Row 34 first (cutover-critical for cloud OSSYS).
Rows 32 + 34 + 35 then bundle into one chapter (5.1.γ.next) since
they share the closed-DU `MetadataExtractionError` shape. Row 33
on operator demand (canary works fine today; tunable timeout is
production-CLI-time). Row 36 last (operator-quality-of-life, not
cutover-blocker).

### Cluster A8 — Offline fixture shape (row 37; 🟡 DIVERGENCE)

See `DECISIONS 2026-05-17 (slice 5.1.δ)`. V2 chose `SnapshotRowsets`
literal records; re-open only if a test scenario needs `runAsync`
exercised against fixture rowsets specifically (e.g., contract-version
testing per row 38 needs a fake `SqlDataReader`). **If triggered:**
add `Projection.Adapters.OssysSql/MockSqlDataReader.fs` as a thin
test-fixture primitive (no V1 JSON manifests; the canary's `RowsetBundle`
literals are the input shape).

### Cluster A9 — Contract versioning (row 38; 🟡 DIVERGENCE)

See `DECISIONS 2026-05-17 (slice 5.1.ζ)`. Two re-open options named:
**(a) update carbon-copy SQL + F# row-mappers in lockstep** (preferred;
preserves V2's structural simplicity); **(b) grow operator-configurable
`ColumnOverride` DU** + thread through `runAsync` parameters. Option
(b) only if multiple OutSystems versions must be supported
simultaneously. The choice flows from the carbon-copy editorial-
inheritance posture — V2 prefers updating the source-of-truth over
overlaying configuration.

### Cluster A10 — AdvancedSql export SQL sunset (row 39; ⚫ V1-SUNSET)

No parity work. Migration impact = none; companion to Cluster A3.

### Cluster F — Schema-diff machinery (rows 40 + 41)

Row 40 sunsets with V1; no V2 parity work. Row 41 details its cash-out
at the row's Notes (`DiffSource` closed-DU; `Compare.run` core function;
per-variant adapter shape; T11 sibling-commutativity acceptance test).
**Dependencies.** Row 41's full surface requires the DACPAC adapter
(chapter 5.x.dacpac, currently deferred). **Today's shippable scope:**
LiveDb ↔ LiveDb, LiveDb ↔ SsdtProject, SsdtProject ↔ SsdtProject —
three of six pairs ship without the DACPAC adapter. **Priority.** Ship
the three pairs when the principal-PO confirms operator demand for
schema-diff outside the canary's specific scope. The CLI surface
addition (`projection compare`) is the **direct CLI refinement** the
slice 5.8.α drop-vs-harvest decision targets — operator gets a real
schema-diff verb instead of an opaque canary.

---

## Open chapter sequence — what's queued at the chapter grain

After the chapter 5.1 wave closes (all 5.1.* slices shipped), the
remaining chapters land in this order:

1. **Chapter 5.2 — JSON + Domain (A2 + A3)**. The biggest single
   audit cluster. Sub-sliced per Domain aggregate (module / entity /
   attribute / index / relationship / misc + valueobjects); the JSON
   deserialization side mirrors V1's `Osm.Json.Deserialization`. Likely
   surfaces many 🔵 V2-EXTENSION rows (V2's smart constructors + closed
   DUs + A39 invariants strengthen what V1 had).
2. **Chapter 5.4 — Tightening + Validations (Section B)**. V1's
   `Osm.Validation` cluster vs V2's `Projection.Core/Passes`. This is
   where **V2-driver mode confidence** is built — per-pass decision
   parity. Each signal cluster (nullability / FK / unique) gets its
   own slice; opportunities + profiling + evidence + application land
   in adjacent slices.
3. **Chapter 5.3 + 5.5 — Emission (Section C)**. V1's `Osm.Smo` SMO
   emission vs V2's `Projection.Targets.SSDT`; V1's `Osm.Emission`
   orchestration (manifest, plan, builder); V1's
   static/dynamic data + UAT user reflow. Most slices land as
   🟢 PARITY (V2 already has the structural equivalent; the audit
   verifies the equivalence claim).
4. **Chapter 5.8 — DMM concept-harvest (closed)**. Rows 40-41
   shipped at slice 5.8.α; the V2 `compare` CLI verb is reserved.
   Actual implementation lands when the principal-PO triggers it.
5. **Chapter 5.6 — Pipeline orchestration (Section D)**. V1's
   `Pipeline/Orchestration` build steps vs V2's `Projection.Pipeline`.
   Mostly verification of V2's pipeline structure-completeness.
6. **Chapter 5.7 — CLI + load harness (Section E)**. V1's `Osm.Cli`
   command surface vs V2's `Projection.Cli`. Maps operator affordances
   1:1 OR identifies V2 CLI gaps that the matrix surfaces (e.g., the
   `compare` verb from row 41 already named).

The remaining chapters drive V2-driver-mode confidence to completeness
slice-by-slice. The matrix's append-only narrative compounds across
the sequence.

---

## Parity-audit slice queue (the in-flight wave)

The first wave targets the V1 SqlExtraction layer (most relevant to chapter 5.0's pivot). Each row below is a candidate slice; the next-agent should pick the highest-leverage row matching session capacity.

The queue is grouped by **audit section** (A through F per the section
chart below). Within a section, slices are unordered priority-wise;
pick what matches the session's capacity. Each chapter-grade entry
(e.g., 5.2.α, 5.3.α) carries a **sub-slice marker** when the cluster
is too large for one session arc — the chapter-opening agent
sub-slices the entry as their first task.

### Section A — Ingest (OSSYS → V2 catalog acquisition)

| Slice | V1 source(s) | Expected scope | Audit-priority rationale |
|---|---|---|---|
| ~~5.1.α~~ | ~~`src/Osm.Pipeline/SqlExtraction/IOutsystemsMetadataReader.cs`~~ | ~~~100 LOC; matrix rows 11–28~~ | **Shipped 2026-05-17 → matrix rows 11–29 (8 NOT-MAPPED + 3 DIVERGENCE + 8 V1-SUNSET).** |
| **5.1.β** | `src/Osm.Pipeline/SqlExtraction/SnapshotValidator.cs` | ~200 LOC; 1 row | Sanity-check semantics V2 may want for live-DB pickup. |
| **5.1.γ** | `src/Osm.Pipeline/SqlExtraction/SqlClientOutsystemsMetadataReader.cs` | ~300 LOC; 1–3 rows | Connection lifecycle, retry, timeout, transient-error semantics for the production wiring. |
| **5.1.δ** | `src/Osm.Pipeline/SqlExtraction/FixtureAdvancedSqlExecutor.cs` + `FixtureOutsystemsMetadataReader.cs` | ~200 LOC; 1–2 rows | V1's offline-test-fixture surface — precedent for V2 offline infrastructure. |
| ~~5.1.ε~~ | ~~`Pipeline/SqlExtraction/SqlMetadataDiagnosticsWriter.cs` + `Pipeline/Sql/SqlMetadataLog.cs` + `Pipeline/SqlExtraction/MetadataRowSnapshot.cs`~~ | ~~~420 LOC; 1 row~~ | **Shipped 2026-05-17 → matrix row 30 (NOT-MAPPED; Diagnostics-axis).** |
| **5.1.ζ** | `src/Osm.Pipeline/SqlExtraction/MetadataContractOverrides.cs` | ~100 LOC; 1 row | V1's hook for tolerating contract drift across OutSystems versions. |
| **5.1.σ** | `src/AdvancedSql/outsystems_model_export.sql` | 931 LOC SQL; 1 row | V1's JSON-emitter SQL — closes the AdvancedSql section started at row 1. Likely ⚫ V1-SUNSET (companion to rows 13/21/22/24-28). |
| **5.2 chapter** — sub-slice at chapter open per the cluster boundaries below |
| **5.2.α.module** | `Osm.Domain/Model/ModuleModel.cs` + `OsmModel.cs` + `OutSystemsInternalModel.cs` | ~250 LOC; 1–2 rows | V1's module aggregate-root. |
| **5.2.α.entity** | `Osm.Domain/Model/EntityModel.cs` + `EntityMetadata.cs` | ~250 LOC; 1–2 rows | V1's entity aggregate. |
| **5.2.α.attribute** | `Osm.Domain/Model/AttributeModel.cs` + `AttributeMetadata.cs` + `AttributeReality.cs` + `AttributeReference.cs` + `AttributeOnDisk*.cs` (3 files) | ~500 LOC; 2–3 rows | V1's attribute aggregate; parity to V2's `AttributeRow` axes. |
| **5.2.α.index** | `Osm.Domain/Model/IndexModel.cs` + 7 sibling Index*.cs files | ~600 LOC; 2–3 rows | V1's index aggregate; intersects V2 chapter 4.5 + 4.9 work. |
| **5.2.α.relationship** | `Osm.Domain/Model/RelationshipModel.cs` + `ForeignKeyModel.cs` + `RelationshipActualConstraint.cs` | ~300 LOC; 1–2 rows | V1's relationship + FK aggregate. |
| **5.2.α.misc** | `Osm.Domain/Model/{SequenceModel,TriggerModel,ExtendedProperty,TemporalRetentionPolicy}.cs` | ~300 LOC; 1–2 rows | Misc aggregates; some carry-forward, some likely ⚫ V1-SUNSET. |
| **5.2.α.valueobjects** | `Osm.Domain/ValueObjects/*.cs` | TBD; sub-slice when opened | V1's identity + naming VOs; intersects V2's `SsKey` / `Name` types. |
| **5.2.β.*** | `Osm.Json/Deserialization/*.cs` (47 files) | sub-slice by deserializer cluster; 4–6 slices | V1's JSON shape V2's `osm_model.json` parsing mirrors. |

### Section B — Analyze (validate / tighten / profile; chapter 5.4)

| Slice | V1 source(s) | Expected scope | Audit-priority rationale |
|---|---|---|---|
| **5.4.α** | `Osm.Validation/Tightening/Validations/{ValidationFinding,ValidationReport}.cs` | ~200 LOC; 1 row | V1's validation-report surface. |
| **5.4.β.nullability** | `Osm.Validation/Tightening/Signals/Nullability*.cs` (6 files) | ~500 LOC; 1–3 rows | V1's nullability signal cluster; V2 `NullabilityRules` analog. |
| **5.4.β.fk** | `Osm.Validation/Tightening/Signals/{ForeignKeySupportSignal,MandatorySignal}.cs` + adjacent | ~400 LOC; 1–2 rows | V1's FK + mandatory signals; V2 `ForeignKeyRules` analog. |
| **5.4.β.unique** | `Osm.Validation/Tightening/Signals/{UniqueCleanSignal,RequiresEvidenceSignal,PrimaryKeySignal}.cs` + adjacent | ~400 LOC; 1–2 rows | V1's uniqueness signal cluster; V2 `UniqueIndexRules` analog. |
| **5.4.γ** | `Osm.Validation/Tightening/Opportunities/*.cs` | sub-slice when opened | V1's opportunity-emission surface. |
| **5.4.δ** | `Pipeline/Profiling/*.cs` (28 files) + `Osm.Domain/Profiling/*.cs` | sub-slice when opened | V1's statistical-profile extraction + use. |
| **5.4.ε** | `Pipeline/Evidence/*.cs` (15 files) | sub-slice when opened | V1's profile / decision evidence carriers. |
| **5.4.ζ** | `Pipeline/Application/*.cs` (21 files) + `Pipeline/Mediation/*.cs` | sub-slice when opened | V1's decision-to-overlay application pipeline. |

### Section C — Emit (produce artifacts; chapter 5.3 + 5.5)

| Slice | V1 source(s) | Expected scope | Audit-priority rationale |
|---|---|---|---|
| **5.3.α.*** | `Osm.Smo/PerTableEmission/*.cs` | sub-slice; 4–6 slices | V1's SMO-based emission; Schema-axis cutover-fidelity. |
| **5.3.β** | `Osm.Smo/IndexScriptBuilder.cs` + `CreateTableStatementBuilder.cs` | partially audited (chapter 4.9 references) | Re-validates chapter 4.9 slice γ + ε against V1 byte-shape. |
| **5.5.α** | `Osm.Emission/SsdtManifest.cs` + `SsdtPredicateCoverage.cs` | ~300 LOC; 2 rows | V2 `ManifestEmitter` + `PredicateCoverage` direct analog. Strong 🟢 PARITY candidate. |
| **5.5.β** | `Osm.Emission/TableEmissionPlan.cs` + `TableEmissionPlanner.cs` + `TablePlanWriter.cs` + `TableHeaderFactory.cs` | ~600 LOC; 2–3 rows | V1's per-table emission planning + writing. |
| **5.5.γ** | `Osm.Emission/{ManifestBuilder,SsdtEmitter}.cs` + `DynamicEntityInsertGenerator.cs` + `PhasedDynamicEntityInsertGenerator.cs` | ~500 LOC; 2–3 rows | V1's SSDT manifest builder + dynamic insert generators. |
| **5.5.δ** | `Pipeline/StaticData/*.cs` + `Pipeline/DynamicData/*.cs` (8 files) | ~400 LOC; 2 rows | V1's seed + MERGE emission. V2 has `StaticSeedsEmitter` + `DataEmissionComposer`. |
| **5.5.ε** | `Pipeline/UatUsers/**/*.cs` (23 files) | sub-slice; 3–4 slices | V1's User-FK reflow (V2 has consumer-side via chapter 4.2). |

### Section D — Orchestrate (pipeline wiring; chapter 5.6)

| Slice | V1 source(s) | Expected scope | Audit-priority rationale |
|---|---|---|---|
| **5.6.α** | `Pipeline/Orchestration/Build*.cs` (16+ files) | sub-slice; 6–8 slices per build-step | V1's BuildSsdt pipeline steps; V2 `Projection.Pipeline` analog. |
| **5.6.β** | `Pipeline/Configuration/*.cs` + `Pipeline/Runtime/*.cs` (16 files) | sub-slice when opened | V1's operator config + runtime verbs. |
| **5.6.γ** | `Pipeline/Sql/*.cs` + `Pipeline/ModelIngestion/*.cs` (13 files) | sub-slice when opened | V1's SQL execution + model ingestion helpers. |

### Section E — Operate (CLI + load harness; chapter 5.7)

| Slice | V1 source(s) | Expected scope | Audit-priority rationale |
|---|---|---|---|
| **5.7.α** | `Osm.Cli/Commands/*.cs` (~30 files) | sub-slice per command — 4–6 slices | V1's CLI command surface; V2 `Projection.Cli` analog. |
| **5.7.β** | `Osm.LoadHarness/*.cs` (6 files) | ~300 LOC; 1–2 rows | V1's synthetic-load generators. |

### Section F — Compare (schema-diff machinery; chapter 5.8)

| Slice | V1 source(s) | Expected scope | Audit-priority rationale |
|---|---|---|---|
| **5.8.α** | `Osm.Dmm/DmmComparator.cs` + `IDmmLens.cs` + `{ScriptDom,Smo,SsdtProject}DmmLens.cs` + `SsdtTableLayoutComparator.cs` | ~600 LOC; 2–3 rows | V1's schema-diff lens machinery; V2's canary `PhysicalSchema` diff is the analog. |

**Trigger to add a slice to the queue:** any V1 audit slice that touches a new V1 file/cluster. Append a row in the appropriate section. The queue is priority-unordered within a section; pick what matches the session's capacity.

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
