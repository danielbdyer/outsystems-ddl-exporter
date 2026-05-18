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

---

## Status history amendments — row reclassifications discovered by later slices

The matrix is append-only at the row level — original rows are not
modified in place. When a later slice discovers that an earlier row's
classification was stale (e.g., V2 actually carries the capability;
the original audit missed it), append a dated amendment to this
section naming the prior status, the new status, and the discovery
slice.

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
