# V1 Full-Export Reconciliation Plan

> **Adopted 2026-06-12** (operator-directed; collision adjudications C1–C4 recorded verbatim in
> §6). This is the canonical record of the 2026-06-12 deep parity research between V1's
> `full-export` flow and V2's config-driven projection verb, and the program that reconciles
> the gaps. Charter: **transcend-and-include** — no V2 functionality is deprecated; missing V1
> capability is absorbed; where behaviors differ, V2 wins unless a true collision of intent is
> surfaced and adjudicated (all four found are adjudicated in §6). Companion surfaces:
> `V1_PARITY_MATRIX.md` (row-level status; this plan triggers amendments, it does not replace
> the matrix), `CONFIRMED_BACKLOG_2026_06_09.md` (the what's-left ledger), `DECISIONS.md`
> (each work package's law changes land there first, per CLAUDE.md §5).
>
> Provenance: produced from an operator end-to-end run in a managed OutSystems environment
> (the "event ledger" in §1) plus eight parallel research sweeps over both trunks, with every
> load-bearing claim spot-verified against code on this date. File:line citations are to the
> tree at commit `7ee8f9e`.

---

## 0 — Reading guide

- §1: the operator's event ledger — the problem series as encountered, in order.
- §2: code-truth findings — the complete research record, by area, with citations.
- §3: standing-law constraints any slice must respect.
- §4: the work packages (WP1–WP9) — design, file targets, test obligations.
- §5: sequencing and the slice plan (slice 1 contents named).
- §6: the four adjudicated collisions, operator decisions verbatim.
- §7: extrapolation obligations to the remainder of the projection verb.
- §8: books obligations (DECISIONS / matrix / ADMIRE / AXIOMS touchpoints).

---

## 1 — The operator's event ledger (2026-06-12 managed-environment run)

Each issue was encountered sequentially; the series is causally coherent and reproducible:

1. **Config**: the V2 example config lacks the generic first-run settings — `model.modules`
   in v2 shape, `includeSystemModules` / `includeInactiveModules`, entity-level narrowing
   inside a module entry (e.g. `User` inside `ServiceCenter`), override entries on the V2
   surface (e.g. `dbo.MyOldTableName` → `dbo.MyNewTableName`), and a bundle/output-store
   configuration that keeps the default path write-averse against target environments.
   Each data source that allows it (e.g. `cloud-dev`) should be its own model source.
   `model.ossys` / `model.path` should be collapsed/deprecated where possible — `model.path`
   only ever derives from OSSYS in the V1 extract anyway. Exception to think through: a
   multi-environment readiness check (dev/QA/UAT precisely schema-synchronized; data contains
   no resolvable dealbreakers — NULLs in NOT-NULL, orphaned FKs) with one environment hosting
   the primary model that the others diff against.
2. **Scoping**: the live metadata query reads *every* module visible on the source connection
   even when the config declares a scoped module set; the declared module/entity scope should
   apply at query time, not only after the full catalog is materialized.
3. **Topology**: once scoped, SQL validation fails — V2's synthetic inverse references are
   treated like storage-backed foreign keys, so the emitter scripts a second FK on target
   primary-key columns: duplicate `FK_*` names and `..._<Id>`-shaped type mismatches. Desired:
   inverse references remain available for logical navigation and ordering but are excluded
   from FK tightening and SSDT FK emission — a logical-only edge class.
4. **Decision layer**: several "do not introduce/enforce" outcomes collapse to one `DropFk`
   action, and the emitter reads `DropFk` as "remove from the export entirely" even when the
   reference is backed by a real source-side constraint. Desired: storage-backed FKs continue
   to emit; a drop decision suppresses only relationships whose `HasDbConstraint`-style state
   says they do not exist as deployable source constraints; diagnostics narrowed accordingly.
5. **Identity annotations**: should move to the typed SSDT emission boundary under policy
   control instead of unconditional hard-coded output.
6. **`BatchSeparator`**: V2 emits `GO` with a leading blank line but no trailing blank line;
   V1 leaves one before the next statement — every multi-statement table differs in spacing.
7. **Emission policy threading**: the config-driven publish path builds the full operator
   policy then invokes the compose/emission seam with `EmissionPolicy.empty`; thread
   `policy.Emission` through instead, and collapse any "v2"-style flags to legible domain
   terminology, assimilating vanilla behavior into the registry/core pipeline.
8. **Data lanes**: Bootstrap, MigrationData, and StaticSeed emission has classification only,
   no row population (`Static []`); there should be a read-only hydration step streaming rows
   from the live source connection, grafted onto the catalog before the data emitters run.
   Verify the public `runWithConfig` writes the three data outputs.
   `BootstrapEmitter.emitFromPlan` returns empty scripts for every kind — delegate its
   plan-consuming surface to the same `DataLoadPlan`-driven emitter the static seeds use.
9. **First-deploy snapshot**: the projection path lacks V1's row-source contract — a
   first-deploy snapshot composed from static rows, regular rows, and supplemental rows in
   one global dependency order.
10. **SSDT CREATE TABLE parity**: DEFAULTs inline; FK/IX/UIX named with logical names; visible
    indentation as V1 (spaces); ON UPDATE / ON DELETE under FKs; EXEC/EXECUTE resolved;
    MS_Description uses logical names.
11. **`Order_Num`**: `a.[Order_Num]` is missing from the rowsets `INSERT INTO #Attr (` column
    list — Service Studio attribute order must drive SSDT emission ordering (crucial business
    case).
12. **V1 seed deviances** to bring in: `WHEN NOT MATCHED BY SOURCE THEN DELETE` mode;
    `EXCEPT`-based validate-before-apply drift check; `IDENTITY_INSERT` handling; row
    batching for large tables. Leave behind the `BaselineSeeds` file-layout grouping contract.
13. **General**: the current V2 lane may preserve its stronger native commitments in place;
    all of this is in scope for consideration.

---

## 2 — Code-truth findings (the research record)

### 2.A — V1 full-export flow (root trunk, C#)

- **Verbs** (`src/Osm.Cli/Program.cs`; factories in `src/Osm.Cli/Commands/`): `build-ssdt`,
  `full-export`, `profile`, `extract-model`, `dmm-compare`, `inspect`, `analyze`,
  `policy explain`, `verify-data`, env-gated `uat-users`.
- **Full-export stages** (`FullExportCoordinator.ExecuteAsync`,
  `src/Osm.Pipeline/Orchestration/FullExportCoordinator.cs:41-153`): Extract model → Profile →
  Build SSDT → Schema apply (opt-in: enabled iff `--apply-connection-string` present) → UAT
  users (optional) → manifest + optional load-harness replay.
- **Build-SSDT internal step chain** (`BuildSsdtPipeline.cs:83-95`): bootstrap → evidenceCache
  → policyDecision (tightening) → emission → sqlProject → sqlValidation → **staticSeed** →
  dynamicInsert (now a deprecated no-op) → **bootstrapSnapshot** → postDeploymentTemplate →
  telemetryPackaging.
- **Model extraction**: embedded `outsystems_metadata_rowsets.sql` executed by
  `MetadataSnapshotRunner` (C#), ~24 result sets; **scope pushed down in SQL** via
  `@ModuleNamesCsv`, `@EntityFilterJson` (`{"Module": ["Entity"]}` shape), `@IncludeSystem`,
  `@IncludeInactive`, `@OnlyActiveAttributes` (binding at
  `SqlExtraction/MetadataSnapshotRunner.cs:217-253`). Scope is *also* re-applied in memory by
  `ModuleFilter.Apply` at every model load — double enforcement is V1 precedent.
- **Model JSON**: built in C# by `SnapshotJsonBuilder` from the rowset snapshot; the JSON is
  the canonical interchange artifact; live OSSYS is its only producer inside an un-reused
  full-export (fixture JSONs accepted for tests). `HydrateExtractionAsync`
  (`FullExportApplicationService.cs:585-643`) re-enriches relationship/FK constraint metadata
  from the live DB at ingestion — the origin of V1's source-backed-FK truth.
- **Config surface** (loader `CliConfigurationLoader.cs:49-172`): sections `tightening(Path)`,
  `model {path, modules[] (string | {name, entities, allowMissingPrimaryKey,
  allowMissingSchema}), includeSystemModules, includeInactiveModules}`, `profile`, `dmm`,
  `cache`, `profiler`, `typeMapping`, `supplementalModels {includeUsers, paths}`,
  `dynamicData`, `uatUsers {…}`, `sql {connectionString, commandTimeoutSeconds, sampling,
  authentication, metadataContract.optionalColumns, profilingConnectionStrings ("Name::conn"
  labels — multi-environment profiling only, never write targets), tableNameMappings}`.
  Naming overrides live under `emission.namingOverrides {rules[], tables[], entities[]}`
  (deserializer `TighteningOptionsDeserializer.cs:189-239`) plus CLI `--rename-table`.
  Multi-environment drift exists as `MultiEnvironmentProfileReport` (nullability/uniqueness/
  orphaned-FK findings + constraint consensus) — report-only.
- **V1 writes are opt-in** (`--apply`); emission to `out/` is the write-averse default.

### 2.B — V2 projection flow (sidecar/projection, F#)

- **CLI**: `Program.fs:323-394`; closed secondary-verb set
  (`MovementSurface.fs:1051`: check/explain/seal/report/profile/init/diff); any other first
  token is a **flow name**. No `full-export` verb — the equivalent is a flow whose model
  source resolves to `ModelSource.ConfigFile` → `PlanAction.PublishBundle` →
  `RunFaces.runFullExport` → `FullExportRun.execute` → `Compose.runWithConfig`
  (`Pipeline.fs:1292-1352`: extract → profile → emit staged arc).
- **`runWithConfigCore`** (`Pipeline.fs:1033-1099`): rename pins → `applyRenames` →
  `buildPolicyFromConfig` + bindings → `projectWithStateWithPins` → optional dacpac →
  diagnostics assembly → `write`.
- **THE `EmissionPolicy.empty` HANDOFF (verified)**: `Pipeline.fs:1055` and `:1064` pass the
  literal `EmissionPolicy.empty` into the compose seam / dacpac filter; sibling sites at
  `:1207`, `:1284`, `:1393` (`projectWithConfig`, `applyShapingToCatalog`, `projectSeedPlan`)
  and `:629/:843/:869`, `Deploy.fs:1359/:1386`. The seam parameter drives exactly one thing —
  `EmissionPolicy.filterPlatformAutoIndexes` (`Policy.fs:515-529`); `empty` has
  `IncludePlatformAutoIndexes = true` so the filter is identity, and **no config key exists**
  for it. The data axis (`EmitData`/`DataComposition`/`DeleteScope`) is honored only because
  it rides the *other* channel (`fullPolicy.Emission` inside `projectWithStateWithPins`,
  `Pipeline.fs:595-606`) — one type, two channels, one config-fed.
- **`EmissionPolicy`** (`Policy.fs:99-114`): `EmitSchema`/`EmitData`/`EmitDiagnostics` (the
  first and third have **zero consumers**), `DataComposition` (`AllRemaining |
  AllExceptStatic | AllData`), `IncludePlatformAutoIndexes` (default true), `DeleteScope`.
- **Dormant config keys (parse, no consumer)**: `emission.ssdt/json/distributions/
  decisionLog/opportunities/validations`; `policy.selection`; `policy.userMatching`;
  `model.onlyActiveAttributes`; `model.validationOverrides`. `policy.insertion` binds but
  nothing downstream reads it (`InsertionPolicyBinding.fs:14-21`).
- **No `v2Annotations` or any `v2*` config flag exists anywhere** (grep-verified). The "v2"
  vocabulary lives only in the emitted extended-property names (`V2.LogicalName`, `V2.SsKey`).
- **Module filter**: `ModuleFilterBinding.fromConfig` → `ModuleFilter.apply`
  (`ModuleFilter.fs:324-437`), applied post-read at `Pipeline.fs:1140-1145/:1166` and the CLI
  seam `Program.fs:100-112`. Include flags inert unless `model.modules` non-empty (A7
  polarity, `ModuleFilterBinding.fs:61-84`, with `moduleFilter.flagsInert` note).
- **TransformRegistry**: single definition site `RegisteredTransforms.chainStepsWithPins`
  (`RegisteredTransforms.fs:118-146`) — CanonicalizeIdentity, VisibilityMask, NamingMorphism,
  NormalizeStaticPopulations, **SymmetricClosure**, LogicalTable/ColumnEmission (hard-wired
  Enabled), TableRename, TopologicalOrderPass, Centrality/BoundedContext, ProfileAnomaly,
  SchemaComplexity, QueryHint, four tightening passes (Nullability, UniqueIndex,
  **ForeignKey**, CategoricalUniqueness), UserFkReflowPass. `registered ⇔ executed`
  property-tested (A41). Emit phase: `Compose.emitSteps` fold (`Pipeline.fs:413-459`).
- **Outputs of a config-driven run today**: `Modules/<Module>/<Schema>.<Table>.sql` +
  `manifest.json`; `projection.json`, `distributions.json`, `manifest.remediation.sql`,
  `manifest.summary.txt`, `suggest-config.json` (always); `projection.dacpac` (opt-in);
  `Data/seed.sql` only when data flags on **and non-whitespace** — in practice empty and not
  written (see 2.G).
- **Provenance-arm gap**: `resolveFlowSpec` requires `Shaping.Model.Path = Some` to classify
  `ConfigFile` (`MovementSurface.fs:917-923`) — an `ossys`-only config never fires
  `PublishBundle`/provenance even with a `store` configured. The sample's `publish` flow is
  affected.

### 2.C — Live metadata scoping + `Order_Num`

- V2's `Resources/outsystems_metadata_rowsets.sql` is **byte-identical** to V1's (diff exit
  0, 1184 lines each) — the pushdown parameters already exist and cascade through `#E`/`#Ent`
  into all 23 rowsets.
- `MetadataSnapshotRunner.runAsyncWithOptions` binds all five parameters faithfully
  (`MetadataSnapshotRunner.fs:656-671`), but the single production caller —
  `LiveModelRead.fromConnection`, `LiveModelRead.fs:39` (verified) — hardcodes
  `MetadataSnapshotRunner.defaultParameters` (`:72-79`: empty modules, include everything).
  **The configured scope never reaches `SnapshotParameters`**; narrowing happens only
  post-materialization in `ModuleFilter.apply`.
- Bench probes already wrap the read (`adapter.osm.extract`, per-rowset pairs,
  `moduleFilter.apply`) — the win is measurable for free.
- **`Order_Num` is absent from BOTH trunks** (repo-wide grep: zero hits on the OSSYS
  attribute-order column). The `INSERT INTO #Attr (` list (rowsets:223-227) carries 23
  columns; none is order. V1 orders attributes alphabetically per entity (rowsets:1006), with
  identifier-first for JSON export (rowsets:803-804). V2's `CanonicalizeIdentity` (chain step
  1) re-sorts `Attributes`/`References` by `SsKey` (`CanonicalizeIdentity.fs:50-54`) — emitted
  column order is effectively GUID order of `ossys_Entity_Attr.SS_Key`. Service-Studio-order
  fidelity is a **new capability**, not parity restoration.

### 2.D — Reference topology and the FK decision layer (both bugs verified in code)

**Bug (a) — inverse references emit as real FKs.**
- Created: `SymmetricClosure.buildInverse` (`SymmetricClosure.fs:69-97`, verified) — inverse
  is a first-class `Reference` with `SsKey = DerivedFrom(originalKey, "inverse")`,
  `SourceAttribute` = the **target's PK attribute**, inheriting `IsUserFk`,
  `HasDbConstraint`, `OnUpdate`, `IsConstraintTrusted` (inheritance pinned as intended by
  `ReferenceHasDbConstraintTests.fs:199-219`). Attached to the target kind's `References`
  (`attachInverses`, `:218-232`). Step 5 of `chainSteps`; carries **no TransformGroup tag**
  (`TransformGroupsBinding.fs:83-90` tags only the tightening passes + userFkReflow), so it
  always runs.
- Flows: `projectFromChainWithState` hands the **post-closure** catalog to the emit fold
  (`Pipeline.fs:472-491`); both the SSDT leg (`:418-419`) and the data leg (`:599`) consume
  it.
- Misinterpreted: `createTableStatement` filters references **only** by `overlay.DropFk`
  (`SsdtDdlEmitter.fs:304-307`, verified); `fkDef` (`:235-283`) resolves an inverse to an FK
  *on the target's PK column* named `FK_<Owner>_<Target>_<PkColumn>`. No `DerivedFrom`
  awareness exists anywhere in `Projection.Targets.SSDT`. Consequences: duplicate FK names
  whenever two forward refs share a target (the `CreatedBy`/`UpdatedBy → User` pattern —
  both inverses name by the same PK column), and PK-to-PK type-mismatch validation failures.
  Inverses also flow into `untrustedFkAlters` (`:349-362`) producing phantom NOCHECK ALTERs.
- Interaction: with an FK intervention registered, inverses are suppressed *by accident*
  (`EvidenceMissing` → DropFk — the profiler keys `ForeignKeyReality` by pre-chain SsKeys,
  `LiveProfiler.fs:779-827`) at the cost of spurious `tightening.foreignKey.evidenceMissing`
  + `decision.fkDropped` warnings per inverse. With none registered (the default), every
  inverse emits.
- The intended contract is already stated but unenforced: "symmetric closure is for surface
  navigation, not for FK-safe data emission" (`TopologicalOrderPassTests.fs:602-608`).
- CI gap: every deploy canary emits from hand-built **pre-chain** catalogs
  (`CanaryRoundTripTests.fs:109,147`; `MigrationCanaryTests.fs:149`); the one full-chain run
  (`ComprehensiveCanaryTests.fs:327-329`) only counts bundle files. No test inspects
  post-chain FK DDL.

**Bug (b) — `DropFk` strips source-backed FKs.**
- `Reference.HasDbConstraint` exists (`Catalog.fs:707-719`, sourced from V1's `HasFK` rowset
  column) and is populated by every adapter (`OssysJsonReader.fs:230`,
  `OssysRowsetReader.fs:197`, `MetadataSnapshotRunner.fs:1101`, `ReadSide.fs:1164`) — but is
  consumed only by `ManifestEmitter` coverage predicates and the G14 invariant. **The FK
  decision layer never reads it** (verified: `ForeignKeyRules.evaluate`,
  `ForeignKeyRules.fs:241-315`).
- `evaluate` gates in order: `EnableCreation` (for *every* reference) → target exists →
  probe consultation. V1's carve-out is approximated by
  `ProbeStatus.Outcome = TrustedConstraint → EnforceConstraint DatabaseConstraintPresent`
  (`:271-275`) — but **no producer ever creates `TrustedConstraint`**
  (`LiveProfiler.deriveForeignKeyRealitiesWith` produces only observed/ambiguous and skips
  static source kinds; only the codec round-trips the variant). Dead branch in production. A
  source-backed FK with absent/ambiguous probe → `DoNotEnforce EvidenceMissing`.
- All seven `DoNotEnforce` reasons collapse into the flat `DropFk : Set<SsKey>`
  (`DecisionOverlay.fs:69-99`, verified), whose documented semantics are "drop the inline FK
  constraint at emission". The emitter removes them from the export (`SsdtDdlEmitter.fs:306`,
  `:356`).
- `foreignKeyDecisionDropDiagnostics` (`SsdtDdlEmitter.fs:923-944`, verified) claims "The
  source enforced it; the emitted schema does not" for **every** DropFk key — false for
  logical-only and inverse references.
- V1's lost behavior (`ForeignKeyEvaluator.cs:124-145`): `hasConstraint ⇒ createConstraint =
  true` with rationale `DatabaseConstraintPresent`, checked **before and regardless of** every
  gate (cross-schema/catalog blocks and `EnableCreation` apply only `&& !hasConstraint`;
  orphans never override). A source-backed FK is structurally impossible to drop in V1. V1
  has no drop concept at all — non-creation means "not introduced". V1's emitter dedupes
  constraint names via a silent `processedConstraints` HashSet
  (`SmoForeignKeyBuilder.cs:23`); V1 references are attribute-anchored so inverse edges
  cannot exist there.

### 2.E — SSDT emission byte parity (V2 output verified by executing the shipped assemblies)

1. **GO**: V1 `StatementBatchFormatter.cs:44-58` — blank line before AND after `GO`, between
   statements only, no trailing GO (TrimEnd). V2 `Render.fs:88-93` (verified) — `"\nGO\n"`:
   leading blank only, **no trailing blank**, and `yieldWithSeparator`
   (`SsdtDdlEmitter.fs:786-822`) appends a separator after the final statement too.
   `Pipeline.aggregateSsdt` joins with bare `"\nGO\n"` (`Pipeline.fs:235-240`). Per-table
   `SsdtFile.Body` contains **no GO at all** — test-pinned
   (`SsdtSchemaFidelityPropertyTests.fs:293-303`).
2. **Identity annotations**: `V2.LogicalName` (table + per column) and `V2.SsKey` (table) are
   unconditional yields (`SsdtDdlEmitter.fs:561-595`). No gate of any kind; V2's own diff
   surfaces are hard-coded blind to them (`PhysicalSchema.fs:526-532`,
   `ReadSide.fs:570-573`); ReadSide *depends* on them for round-trip identity recovery
   (`ReadSide.fs:1024,1070,1426-1530`). `IDENTITY (1, 1)` itself is byte-parity (both trunks
   effectively hard-code 1,1; C1 in the confirmed backlog accepts this).
3. **DEFAULTs**: inline in both (`CreateTableStatementBuilder.cs:319-335`;
   `ScriptDomBuild.fs:388-397`). V2 has no `EmitBareTableOnly` equivalent. Known named
   tolerance: empty-string Text default renders `DEFAULT NULL` vs V1 `DEFAULT ('')`
   (`EmptyTextNormalizedToNull`, `Tolerance.fs:85-98`).
4. **Naming**: PK — V1 source-derived (`PK_Customer_Id`), V2 synthesized
   `PK_<Schema>_<Table>` (`SsdtDdlEmitter.fs:187-206`). FK — V1 uses provided constraint
   name when present, multi-column segments, 128-char cap with SHA256-12 truncation, and a
   `ConstraintNameNormalizer` that swaps physical for logical names; V2 always synthesizes
   `FK_<Owner>_<Target>_<SourceColumn>` (single-column, ignores `Reference.Name`, no cap;
   converges on simple cases because `LogicalTableEmission` substitutes logical names into
   `Physical` first). IX/UIX — V1 synthesizes `UIX_/IX_<LogicalTable>_<LogicalCols>`
   (`IndexNameGenerator.cs:30-58` + normalizer); **V2 passes the source physical index name
   through verbatim** (`SsdtDdlEmitter.fs:430`) — `OSIDX_*` names surface.
5. **Indentation**: both use ScriptDom 4-space aligned column blocks; V1 then applies a
   4/8/12 constraint ladder. V2 carbon-copied the ladder (`ConstraintFormatter.fs:148-171,
   260-266`) but it fires only on the `Render.toText` flat-stream path (`Render.fs:144`);
   **per-table `SsdtFile` bodies skip it** — one-line constraints. V1 inlines single-column
   FKs on the column (8/12 indent); V2 emits FKs table-level (4/8).
6. **ON DELETE/ON UPDATE**: V1 emits only `DeleteAction` and only when ≠ NoAction; formatter
   normalizes (fill the missing clause with NO ACTION when exactly one is present; drop both
   when both are NO ACTION). V2 sets DeleteAction unconditionally (explicit `ON DELETE NO
   ACTION` in per-table bodies, `ScriptDomBuild.fs:494-497`), supports real ON UPDATE (V1
   cannot), and applies the same normalization — again only on the flat-stream path.
7. **EXEC/EXECUTE**: V1 hand-builds `EXEC sys.sp_addextendedproperty @name=N'…'` (compact,
   `;`-terminated). V2 typed ScriptDom canonicalizes to
   `EXECUTE [sys].[sp_addextendedproperty] @name = N'…'` — a **documented accepted
   deviation** (`DECISIONS.md:11927`).
8. **MS_Description**: V1 uses the effective (logical/override) table name and emission
   column name. V2 reads `k.Physical`/column realization — logical in the default chain
   because `LogicalTable/ColumnEmission` substitute first; physical names can escape via the
   S6.3 physical-rename-pin exemption (the prime suspect for the managed-environment observation —
   needs a pinning property test).

### 2.F — Config surfaces (V2 view)

- Two views over one document (A44, unified 2026-06-10): the **shaping** view
  (`Config.parse`, `Config.fs:1422-1476` — `model`, `overrides {tableRenames,
  emissionFolders, allowMissingPrimaryKey, circularDependencies, migrationDependencies.path,
  staticData.path}`, `emission`, `policy`, `typeMapping`, `profiler`, `cache`, `output`,
  `profile`) and the **movement** view (`ProjectionConfig.parse`,
  `MovementSurface.fs:306-388` — `environments.<name> {access: bundle|direct|docker, out,
  conn (env:/file: refs only, D9), grant: schema+data|data, store, rendition:
  physical|logical}`, `flows.<name> {from, to, tables, rekey, reconcile, scope, shape,
  shaping (per-flow whole-section overlay)}`).
- `model.modules` already parses both string and `{name, entities}` shapes
  (`Config.fs:45-47, 632-664`) — the v2 shape exists; the **sample doesn't show it** and the
  scope never reaches the adapter (2.C).
- `examples/projection.sample.json` shows environments/flows but no
  include flags, no physical→physical rename example; the full shaping surface is documented
  inline in `CONFIG_REFERENCE.md` (the once-separate `model.config.sample.json` was folded in,
  2026-06-22). The samples don't demonstrate per-flow `shaping.model` (the existing
  per-source-model mechanism).
- V1's per-module `allowMissingPrimaryKey`/`allowMissingSchema` narrowing maps to V2's
  top-level `overrides.allowMissingPrimaryKey` + `model.validationOverrides.allowMissingSchema`
  (the latter currently consumer-less).

### 2.G — Data lanes

- **`Static []` markers without populations**: `OssysRowsetReader.fs:579-583` (live path,
  verified) and `OssysJsonReader.fs:587-588` (JSON path) set the classification only. The
  population join (`static-entities.*.json` importer) was **removed 2026-06-08**
  (`V1_INPUT_DEPRECATION.md:146-154`) with the rationale "static entity data reaches V2
  through the model read … and through ReadSide" — which the forward model read does not
  satisfy. Only ReadSide (canary/readback) attaches rows — and it marks **every**
  data-bearing table ≤100k rows `Static` (survival rule 8; `ReadSide.fs:1748-1756`).
- **Effect**: `Kind.staticPopulations` yields `[]` → `StaticSeedsEmitter` builds an empty
  plan → `kindToScript` short-circuits → `DataEmissionComposer.unionSiblings` sees ownership
  with no `Phase1Merges` → `Data/seed.sql` composes to whitespace → `runWithConfig` skips
  writing it (`Pipeline.fs:588-606`, `| Ok _ -> outputs`).
- **`BootstrapEmitter.emitFromPlan` is a stub** (verified, `BootstrapEmitter.fs:60-103`): it
  discards `_plan` and returns the empty script per kind; `emitWithTopo` builds its plan from
  `Map.empty`. Registry-honest (`Status = NotImplementedInV2 "Slice ζ MVP …"`). Signature is
  **identical** to `StaticSeedsEmitter.emitFromPlan`, whose renderer
  (`emitFromPlanWith` → `kindToScript` → `renderMerge`/`renderUpdate`,
  `StaticSeedsEmitter.fs:150-378`) realizes any plan's loads — delegation is one line; the
  real work is upstream (row source; `UserRemapContext → SurrogateRemapContext` conversion;
  the composer's `OverlappingEmitterCoverage` partition assertion).
- **`DataLoadPlan`** (`DataLoadPlan.fs`): `Loads` in FK-safe topological order with
  `IdentityDisposition` (`PreservedFromSource | AssignedBySink | ReconciledByRule`),
  `DeferredFkColumns` (Phase-2), `UnbreakableCycleFks` (named refusal),
  `SkippedReferences`. `build` takes `Catalog × TopologicalOrder × Map<SsKey, StaticRow
  list> × SurrogateRemapContext` — the **one** `OperatorIntent Insertion` site.
- **Global order already exists**: the composer hoists one `TopologicalOrderPass` and walks
  `topo.Order` across the **union** of all lanes — all Phase-1 MERGEs then all Phase-2
  UPDATEs in one global order (`DataEmissionComposer.fs:204, 297-331`); leveled variant for
  parallel deploy. V1's first-deploy contract (`BuildSsdtBootstrapSnapshotStep.cs:49-66` —
  static + regular + supplemental in one global `EntityDependencySorter` order, phased
  NULL-FK/UPDATE) is structurally matched; V2's lanes are simply hollow.
- **Row readers exist and are reusable**: `ReadSide.readRowsStream`
  (`ReadSide.fs:841-946`, `SELECT <cols> FROM <table> ORDER BY <pk>` streaming) and
  `Ingestion.collectInOrder` (`Ingestion.fs:44-71`) which produces exactly the
  `Map<SsKey, StaticRow list>` shape `DataLoadPlan.build` consumes. The missing wire is in
  `runWithConfig` between model read and data composition. V1's row extraction
  (`StaticEntityDataProviders.cs:309-330`, `SqlDynamicEntityDataProvider.cs:593-649`,
  supplemental via `QuerySupplementalDataAsync`) uses the same SELECT shape.
- **V1 seed deviances vs V2**:
  - `WHEN NOT MATCHED BY SOURCE THEN DELETE` (V1 `Authoritative` mode,
    `StaticSeedSqlBuilder.cs:261-268`) → **V2 has it, better**: per-kind `DeleteScope`
    predicate (AC-D7, `ScriptDomBuild.fs:707-720`, `DeleteScopePolicy.resolveFor`).
  - `EXCEPT` validate-before-apply (`ValidateThenApply`, `StaticSeedSqlBuilder.cs:102-138`,
    symmetric EXCEPT pair + `THROW 50000`) → **V2 missing**; V2's posture is the
    CDC-silence norm.
  - `SET IDENTITY_INSERT` bracket (`StaticSeedSqlBuilder.cs:140-145, 202-206`) → **V2
    split**: present in `StaticPopulationEmitter`/Bulk lanes
    (`StaticPopulationEmitter.fs:95-116`, `ScriptDomBuild.buildSetIdentityInsert`); **absent
    from the production MERGE lane** ("slice E" deferred,
    `StaticSeedsEmitter.fs:273-275`) — a deploy-time landmine the moment rows flow.
  - Row batching (1000/batch, `StaticSeedSqlBuilder.cs:14, 94-100, 166-188, 275-302`) →
    **V2 missing** on the text lane; governed by the armed staged-bulk deferral (≳100k
    rows/kind; "do NOT re-open as correctness").
  - `BaselineSeeds/<Module>/StaticEntities.seed.sql` layout
    (`BuildSsdtStaticSeedStep.cs:94-156`) → **left behind by operator decision**.

### 2.H — Doc-canon constraints discovered (selected; see §3)

- "v2Annotations" and "Order_Num" appear nowhere in the projection docs — both gap items
  needed the vocabulary mapping recorded here.
- The 2026-06-08 input-deprecation rationale for removing the static-populations importer is
  **not satisfied by the code** (2.G) — a latent regression hidden by a documented removal;
  per the latest-first rule, the code wins and the doc claim is amended by this plan.
- The matrix already reserves `projection compare` (row 41) with a specified cash-out shape
  (closed-DU `DiffSource`, T11 inverse property) — the multi-environment readiness check is
  its trigger firing.
- Multi-environment connection modeling is settled (subsumed into the Transfer epic's
  `Environment`/`Substrate`/`TransferConnections`); promotion-chain orchestration is
  explicitly framed **above** the movement isomorphism (G5) — a legitimately open lane.
- `model.path` is already demoted to optional fallback; retirement is R6-gated (N green
  differential runs + operator sign-off). This plan does not retire it; it makes the samples
  ossys-first and fixes the provenance arm so ossys-only configs are first-class.
- Armed/deferred items this plan lands on (triggers now fired by the managed-environment run):
  provenance-typed Static (`ReadbackPopulated`); static-seed parent-handling ("surfaces under
  concrete operator demand"); constraint-name round-trip (matrix row 57); staged-bulk MERGE
  (perf trigger, conditional).

---

## 3 — Standing law any slice must respect

1. **A44** — every new knob lands in the unified `projection.json`; `expressible ⇔ reachable`
   (the `MovementIsomorphismTests.fs` canary); byte-identical-default invariant when wiring a
   previously-inert flag (the dacpac precedent).
2. **A18 / A35 / A36 / A41 / T11 / A33 / T1** — no `Policy` into emitters (thread plain
   values at the composer, the `DeleteScope` precedent); typed deterministic statement
   streams; realization choices invisible to Π; every transformation registered; sibling
   keyset agreement; schema=deterministic, data=topological ordering; bit-identical output
   per `(catalog, policy, profile)` triple.
3. **Pure core** — `Projection.Core` has zero I/O; hydration is adapter/pipeline work.
4. **No silent anything** — refusals, downgrades, skips, and suppressions are named.
5. **DECISIONS-first** — law changes write their `DECISIONS.md` amendment in the same commit
   or before; AXIOMS changes carry their `AxiomTests.fs` entry in the same commit.
6. **R6 dual-track** — V2 emits-but-doesn't-ship; live writes need `--go` +
   `PROJECTION_ALLOW_EXECUTE`; V1 is editorial donor only (carbon-copy + header citation +
   ADMIRE row).
7. **Deferral discipline** — armed items reopen via their trigger plus a DECISIONS cash-out
   entry; the named DO-NOT-ATTEMPT traps stay closed.
8. **J5 preemption** — a writable UAT connection preempts all of this.

---

## 4 — The work packages

### WP1 — Logical-only inverse edges (event-ledger #3)

**Change.**
- New `Projection.Core` predicate — the single definition site for "deployable reference":
  an active pattern over the derivation reason (consumers ≥3 sanctions the house
  match-macro). Inverse-derived references (`DerivedFrom(_, "inverse")`) are **not**
  deployable; everything else is.
- `ForeignKeyPass` evaluates only deployable references — inverses never enter decisions
  (kills the spurious `evidenceMissing`/`fkDropped` noise at the root).
- `SsdtDdlEmitter`: `createTableStatement` FK list, `untrustedFkAlters`,
  `foreignKeyDropDiagnostics`, and `foreignKeyDecisionDropDiagnostics` all filter to
  deployable references.
- Inverses **stay in the catalog** — `TopologicalOrderPass`, centrality, bounded-context,
  navigation, and ordering consumers keep the full closure. `HasDbConstraint` inheritance on
  the inverse **stays** (the pinned semantics — the inverse view surfaces the forward edge's
  storage truth); exclusion is by derivation class, not by flag.
- Replace nothing silently: a loud uniqueness assertion on emitted FK constraint names
  (V1 deduped silently; V2 trips a named error — with inverses excluded this is a tripwire,
  not a behavior).

**Tests.** Property: post-chain SSDT emission contains exactly one FK per non-dropped
*forward* reference and zero inverse-sourced FKs; the `CreatedBy`/`UpdatedBy → User` shape
produces no duplicate names; FK pass mints no decisions for inverses; full-chain canary that
emits from a **post-chain** catalog (closing the 2.D CI gap). Re-state the pinned
inheritance test alongside a new exclusion-contract pin.

### WP2 — `HasDbConstraint` carve-out at the decision layer (event-ledger #4)

**Change.**
- `ForeignKeyRules.evaluate` gate order becomes: target exists → **`reference.
  HasDbConstraint` ⇒ `EnforceConstraint DatabaseConstraintPresent`** → `EnableCreation` →
  probe gates. Missing target stays a (now honestly-diagnosed) suppression even for
  source-backed refs — a scoped export cannot emit an FK to a table outside the export.
  Trust state (`IsConstraintTrusted`) continues to drive the NOCHECK two-step at the emitter
  — strictly richer than V1.
- Retire the dead `TrustedConstraint` approximation branch (no producer exists; the DU
  variant stays for codec compatibility) — dead-algebra precedent, DECISIONS-noted.
- `decision.fkDropped` narrows: message claims source enforcement only when the reference's
  `HasDbConstraint` is true (the missing-target case); logical-only non-introduction reports
  as Info-level "not introduced". The `DropFk : Set<SsKey>` → reason-carrying map refinement
  is deferred to its own slice (it churns the 6.A.8 read-back and lifecycle codec); the
  diagnostics consult the catalog's flag directly in the interim.

**Tests.** Property: ∀ reference with `HasDbConstraint = true` and target in catalog, the
emitted schema contains the FK regardless of `EnableCreation`, profile absence, or orphan
evidence. Diagnostic-message conditionality pinned. Orphans-never-override pinned (V1
semantics; physically-backed + orphans only arises under NOCHECK/untrusted, which the
emitter already realizes).

### WP3 — Scoped live metadata read (event-ledger #2; adjudicated C4)

**Change.**
- Thread `Config.ModelSection` → `SnapshotParameters` through
  `readConfigModel`/`LiveModelRead.fromConnSpec`/`fromConnection`: modules → `@ModuleNamesCsv`;
  entity narrowing → `@EntityFilterJson` (the script already documents the shape); the two
  include flags; `onlyActiveAttributes` → `@OnlyActiveAttributes` (the dormant key gains its
  consumer).
- Pushdown fires only when `model.modules` is non-empty (A7 polarity intact;
  `moduleFilter.flagsInert` unchanged); otherwise `defaultParameters` stand.
- **`ModuleFilter.apply` stays as the semantic seam** (double enforcement — V1's own
  precedent). DECISIONS amendment reframes the 2026-05-16 "filtering is an IR concern"
  stance: scope pushdown is adapter-side extraction-cost reduction for declared scopes; the
  IR filter remains the correctness owner. Trigger: the managed-environment estate-scale run.

**Tests.** Docker-gated equivalence property:
`scopedRead(scope) ≡ ModuleFilter.apply(scope) ∘ fullRead` over the fixture estate. Bench
deltas recorded via the existing `adapter.osm.extract*` probes.

### WP4 — Emission-policy threading + flag rationalization (event-ledger #7)

**Change.**
- Collapse the two `EmissionPolicy` channels: drop the separate seam parameter from
  `projectWithStateWithPins`/`projectFromChainWithState`; use `policy.Emission` inside. All
  `EmissionPolicy.empty` literals at config-driven call sites dissolve by construction
  (`Pipeline.fs:1055/:1064/:1207/:1284/:1393`; audit `:629/:843/:869` + `Deploy.fs` sites
  for the non-config paths — those construct their policy explicitly).
- Wire `emission.includePlatformAutoIndexes` (default `true` = current behavior; matrix
  note vs V1's shipped-config default `false`).
- A44 cleanup: every dormant key gains its consumer or leaves the parser —
  `emission.ssdt/json/distributions/decisionLog/opportunities/validations` gate their
  `emitSteps`/write entries (defaults preserve current output); `EmitSchema`/
  `EmitDiagnostics` become real or are removed from the type; `policy.selection`/
  `policy.userMatching` adjudicated (likely: remove until their consumer exists);
  `model.validationOverrides.allowMissingSchema` wired to the existing validation overrides
  surface or removed.
- No "v2"-named config flags exist; none are introduced.

### WP5 — Identity annotations: gate + rename (event-ledger #5; adjudicated C1)

**Change.**
- **Rename** the extended properties to domain terminology (C1: rename now, pre-cutover —
  "V2" is meta-vocabulary, not domain): `V2.LogicalName` → `Projection.LogicalName`,
  `V2.SsKey` → `Projection.SsKey` (final names confirmed at slice open; one DECISIONS entry
  names them). Writer: `SsdtDdlEmitter.extendedPropertyStatements`. Readers: ReadSide reads
  **both** names (new-first) for environments already touched; the diff-blindness lists
  (`PhysicalSchema.fs:526-532`, `ReadSide.fs:570-573`) carry both during the window;
  retirement of the legacy-name read is trigger-gated (no `V2.*`-bearing environment
  remains).
- **Gate** emission behind a new `EmissionPolicy` axis (`identityAnnotations: emit | omit`,
  config under `emission`), default **emit**. `omit` is a named downgrade: a diagnostic
  states identity recovery degrades to name-derived SsKeys for this bundle; round-trip
  canaries skip-with-reason or run annotation-on. Threading rides WP4 (plain values at the
  composer; A18 holds).

### WP6 — Data lanes: hydration, bootstrap realization, first-deploy snapshot, deviances (event-ledger #8, #9, #12; adjudicated C2)

Ordered by dependency:
1. **IDENTITY_INSERT in the MERGE lane first** (pull "slice E" forward):
   `StaticSeedsEmitter` brackets IDENTITY-PK kinds (`buildSetIdentityInsert` exists) or
   suppresses the PK column per `IdentityDisposition`. Landing hydration without this
   converts empty-output into deploy-failure.
2. **Hydration step**: read-only, adapter-side, registered; on the config-driven path
   between model read and compose. For kinds owned by the active `DataComposition`, stream
   rows via `Ingestion`/`ReadSide.readRowsStream` from the `model.ossys` connection (row
   source = model source; dedicated override key only under demand). Graft per-kind, scoped
   to owned kinds only (never ReadSide's mark-everything; survival rule 8); land on the
   armed `ReadbackPopulated`/provenance-typed-Static design so authored vs hydrated rows
   stay distinguishable (closes the 4.4 trap). File-sourced model + data flags on ⇒ named
   skip note, never silent emptiness.
3. **Bootstrap delegates**: `BootstrapEmitter.emitFromPlan` →
   `StaticSeedsEmitter.emitFromPlanWith` (signature-identical). Bootstrap's plan gains its
   row source (supplemental kinds — `ossys_User` et al. — plus remaining kinds per
   `AllData`/`AllRemaining`); `UserRemapContext → SurrogateRemapContext` at
   `DataLoadPlan.build` (the anticipated conversion); the composer's
   `OverlappingEmitterCoverage` assertion remains the lane-partition law. The in-code
   "slice η" bootstrap-specific-body note is superseded by delegation.
4. **First-deploy snapshot**: with all lanes populated, the composer's existing global
   Phase-1⨄Phase-2 interleave **is** V1's `AllEntitiesIncludingStatic` contract. No new
   ordering machinery; V2's `UnbreakableCycleFks` named refusal is retained (superior to V1).
5. **Outputs**: `runWithConfig` writes per-lane artifacts (`Data/StaticSeeds.sql`,
   `Data/Bootstrap.sql`, `Data/MigrationData.sql`, each internally topo-ordered from the
   pre-union sibling dispatch) **plus** the fused global `Data/seed.sql` (current contract
   unchanged). Integration test pins all four under the three flags.
6. **Deviances**: `DeleteScope` already transcends `NOT MATCHED BY SOURCE` (matrix row).
   **EXCEPT validate-before-apply** lands as an **opt-in** data-emission mode (C2:
   CDC-silence stays canonical; EXCEPT is the conservative fallback/override until J5
   proves the CDC path on a managed OutSystems environment — opt-in via config, e.g.
   `emission.dataVerification: "validateBeforeApply"`, typed-AST EXCEPT prelude + THROW).
   Row batching stays under its armed perf trigger, now reachable — adopt V1's batched-MERGE
   shape when it fires. `BaselineSeeds` layout stays retired.

### WP7 — SSDT byte-parity polish (event-ledger #6, #10)

- **GO**: `Render.fs` `BatchSeparator` → `"\nGO\n\n"`; align `aggregateSsdt`'s joiner;
  refresh golden fixtures. Keep V2's terminal GO (harmless; V2 wins; matrix note). Keep the
  per-table no-GO file contract (test-pinned V2 design).
- **Formatting**: route per-table `SsdtFile` bodies through the same `ConstraintFormatter`
  ladder + ON-clause normalization the flat stream uses — delivers visible indentation,
  two-line DEFAULTs, and drops the spurious `ON DELETE NO ACTION` from file bodies.
- **IX/UIX naming**: port V1's logical synthesis (`UIX_/IX_<LogicalTable>_<LogicalCols>`)
  as a registered pass sibling to LogicalTable/ColumnEmission (A40 one-parameterized-
  algorithm; ADMIRE row for `IndexNameGenerator` + `ConstraintNameNormalizer`).
- **FK naming**: honor `Reference.Name` when present; 128-char deterministic hash cap
  (matrix row 57's trigger has fired). PK naming stays V2's `PK_<Schema>_<Table>` (V2 wins;
  matrix note).
- **EXEC/EXECUTE**: keep `EXECUTE [sys].[…]` (documented decision; "resolved to your
  preference" = V2's typed canonical form).
- **MS_Description**: add an end-to-end property pinning logical names on the config-driven
  path **including renamed/pinned tables**; fix what it catches (prime suspect: the S6.3
  physical-rename-pin exemption).

### WP8 — `Order_Num` / Service Studio ordering (event-ledger #11; adjudicated C3)

- Add `a.[Order_Num]` to the `#Attr` INSERT/SELECT in **V2's** rowsets script (header-cited
  divergence from the V1 carbon copy; V1 trunk is sunset-bound, out of corpus).
- Thread `Order : int option` through `OssysAttributeRow` → `AttributeRow` → `Attribute`
  (IR-grows-under-evidence; the business case is the evidence). JSON-fallback models carry
  `None`.
- **Registered catalog pass** (C3) after `CanonicalizeIdentity`: sort attributes by
  `(Order_Num ?? ∞, SsKey)` — deterministic (T1 intact), registry-visible (A41), inherited
  uniformly by SSDT, dacpac, and data-lane column order.

### WP9 — Config surface + multi-environment readiness (event-ledger #1)

- **Rewrite `examples/projection.sample.json`** as the first-run-complete artifact:
  per-environment connections (cloud-dev/qa/uat…); `model.ossys` as the sole model source in
  the main sample (`model.path` omitted — demotion-to-fallback and R6-gated retirement are
  standing law); `model.modules` v2 shape with `{ "name": "ServiceCenter", "entities":
  ["User"] }`; both include flags; a physical→physical rename
  (`dbo.MyOldTableName` → `dbo.MyNewTableName`); bundle-access environments as the default
  posture (write-aversion is structural: direct writes need `--go` +
  `PROJECTION_ALLOW_EXECUTE`); a `store` on the publish target; a per-flow `shaping.model`
  example (the existing per-source-model mechanism).
- **Fix the provenance arm**: `resolveFlowSpec` must classify `ConfigFile` for `ossys`-only
  configs (store presence is the decided trigger; the `model.path` requirement at
  `MovementSurface.fs:917-919` is a wiring oversight).
- **Multi-environment readiness check**: cash out the reserved `projection compare` verb
  (matrix row 41 — its trigger has fired) as orchestration above the movement isomorphism
  (the G5 framing): one designated model-host environment; per-pair schema diff via
  `CatalogDiff`/`PhysicalSchema`; data dealbreakers (NULL-in-NOT-NULL-candidate, orphaned
  FKs) read from the existing profiler evidence; V1's `MultiEnvironmentProfileReport`
  consensus shape is the donor; `Preflight.all` + the settled `Environment`/`Substrate`
  connection apparatus are the native organs. Design slice first (closed-DU `DiffSource` per
  the matrix's specified shape).

---

## 5 — Sequencing and the slice plan

Dependency-ordered program: **WP1+WP2** (constraint-surface correctness — unblocks the
operator's SQL validation) → **WP4** (threading; WP5 and parts of WP6/WP7 ride it) → **WP3**
(scoping) → **WP6** (data lanes; step 1 strictly before step 2) → **WP7/WP8** (emission
polish, ordering) → **WP9** (samples + compare verb). Books obligations (§8) ride every
slice. J5 preempts at any point.

**Slice 1 (this branch, in flight): WP1 + WP2 whole.** DECISIONS amendment first; the
deployable-reference predicate; FK pass + emitter + diagnostics exclusions; the
`HasDbConstraint` carve-out; dead-branch retirement; narrowed diagnostics; the property
tests and the post-chain canary; matrix row amendments. Rationale: this is the cluster that
turns the operator's scoped managed-environment run from SQL-validation-failure to green, and every
other package's tests get more honest once the constraint surface is correct.

**Slice 2 (next): WP4 + WP7-GO** (threading + the two-line `BatchSeparator` fix with golden
refresh). **Slice 3: WP3.** **Slice 4: WP6 steps 1–5.** **Slice 5: WP5 (rename+gate).**
**Slice 6: WP7 remainder + WP8.** **Slice 7: WP9 + WP6 step 6 (EXCEPT opt-in).** Slice
boundaries may merge under evidence; each closes with its books.

---

## 6 — Adjudicated collisions (operator decisions, 2026-06-12, verbatim)

- **C1 — `V2.*` extended-property names**: *"Let's go ahead and just get the rename done. V2
  is only useful as terminology in meta as we discuss the codebase, but is not practical
  otherwise inside of the domain."* → WP5 renames (dual-read window; trigger-gated legacy
  retirement).
- **C2 — drift authority for data**: *"let's have both coexist. CDC canonical is
  preferential as a primary emission strategy … let's either 'reverse progressive enhance'
  it and fall back to it upon CDC-approach failure, or make the strategy opt-in/override to
  go to the EXCEPT route. Perhaps that's unnecessary but I want to be conservative until J5
  lands."* → WP6.6: CDC-silence canonical; EXCEPT validate-before-apply as opt-in/override;
  revisit auto-fallback after J5.
- **C3 — where Service Studio ordering lives**: *"I agree with your recommendation, let's
  keep it as a registered pass."* → WP8.
- **C4 — adapter-time scope pushdown vs the 2026-05-16 IR-filtering stance**: *"I do think
  filtering in general should live in a lower-level bucket, however I'm good to elevate the
  model filtering to be adapter-time because specifically in this case there is a subset of
  applications/eSpaces/modules/tables that we actually do wish to retrieve inside of a
  projection scope. It's not useful to gain *all* of the applications, eSpaces, and tables,
  precisely because in some environments this will make the model extraction way longer than
  expected, and we'll discard the data immediately anyway."* → WP3 as designed (pushdown for
  declared scopes; `ModuleFilter` stays the semantic owner).

---

## 7 — Extrapolation obligations (the remainder of the projection verb)

1. **Deployable-reference audit**: every consumer of `Kind.References` is audited against
   the WP1 predicate — `DacpacEmitter`, `ManifestEmitter` coverage, `CatalogDiff` /
   `PhysicalSchema` comparison (an inverse-bearing emitted catalog vs a physical readback
   must not diff phantom FK adds — the round-trip law is exposed exactly the way emission
   was), `UserFkReflowPass`, data-lane `deferredFkColumns`, `RefactorLogEmitter`,
   `SchemaMigrationEmitter`. Navigation/ordering consumers keep the full closure by design.
2. **Scope propagation**: the WP3 scope reaches the live profiler (`acquireProfile`) and the
   WP6 hydration reads — profile and hydrate only scoped kinds.
3. **Policy-channel hygiene**: after WP4, no compose entry point accepts a separate
   emission-policy parameter; new emission capabilities land as `EmissionPolicy` axes
   threaded as plain values (the `DeleteScope` precedent), config-reachable per A44.
4. **Hydration as the one row-source seam**: Transfer's `Ingestion` and the full-export
   hydration share the reader leg; future producers (synthetic σ, golden) populate the same
   `Map<SsKey, StaticRow list>` contract; no second row-attachment mechanism.
5. **Full-chain canary discipline**: post-chain emission canaries become the norm for any
   pass that rewrites the catalog (the WP1 CI-gap lesson generalizes: pre-chain fixtures
   cannot witness pass/emitter interactions).
6. **Annotation vocabulary**: after WP5, no domain artifact carries "V2"; meta-discussion
   stays in the books.

---

## 8 — Books obligations

- **DECISIONS.md** entries (one per slice, written first): slice-1 entry covering the
  logical-only edge law + the `HasDbConstraint` carve-out + dead-branch retirement +
  diagnostics narrowing; WP3's pushdown reframe of 2026-05-16; WP4's channel collapse +
  flag wiring; WP5's rename + gate (names recorded); WP6's hydration seam + importer-removal
  rationale amendment (the 2026-06-08 claim is corrected); WP7's GO + formatting + naming;
  WP8's ordering pass; WP9's provenance-arm fix + compare-verb design.
- **V1_PARITY_MATRIX.md** amendments: rows 57–59 (FK naming/features — row 57's trigger
  fired); the inverse-reference divergence row (new: 🔴 V1-BUG-CORRECTED is wrong-shaped —
  this is a 🟡→🟢 path via WP1/WP2); GO spacing row; EXEC/EXECUTE row (⚫/🟡 documented
  deviation, V2 wins); `Order_Num` row (🔵 V2-EXTENSION — neither trunk had it);
  IDENTITY_INSERT MERGE-lane row; EXCEPT-drift row (🟡 opt-in coexistence per C2); per-lane
  data outputs row.
- **ADMIRE.md** rows: V1 `ForeignKeyEvaluator` carve-out consumption (the evaluator was
  already admired; the carve-out's consumption is the new placement); `IndexNameGenerator` +
  `ConstraintNameNormalizer` (WP7); `StaticSeedSqlBuilder` EXCEPT/batching shapes (WP6);
  `MultiEnvironmentProfileReport` (WP9, design-time donor).
- **AXIOMS.md**: no axiom text changes anticipated in slice 1; WP4 may amend A18's
  worked-example notes; any change carries its `AxiomTests.fs` entry in the same commit.
- **CLAUDE.md / HANDOFF.md**: handoff letter at each slice close per the chapter rhythm;
  this plan is Tier-2 reading for anyone touching the full-export surface until absorbed.
