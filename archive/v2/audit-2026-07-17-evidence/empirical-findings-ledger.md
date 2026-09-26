# Empirical findings ledger — v1 full-export vs v2 publish (tree ea21b13, 2026-07-17)

Shared input: database `ParityEstate` on localhost:11433, seeded from
`sidecar/projection/src/Projection.Adapters.OssysSql/Resources/ossys-edge-case.seed.sql`
(7 espaces, 15 entities, 2 static kinds) + deterministic rows (Country×3, Currency×3, City×2, Customer×1).
v1 run: `full-export --config cfg.v1.live.json --connection-string <ParityEstate> --profiler-provider sql --refresh-cache`
(cfg: tighteningPath=config/default-tightening.json, supplementalModels.includeUsers=false).
v2 run: `projection publish --json` with model.ossys=file-ref, profiler live, PROJECTION_MSSQL_CONN_STR set.
Both exit 0. Evidence dir: scratchpad/empirical/. Round-trip DBs: ParityV1DB / ParityV2DB (deploy.sh, topo-ordered per-table files, same algorithm both sides).

## Headline functional-output differences (proven at deployed-database level; roundtrip/catalog.diff)

EF-1  DELETE/UPDATE RULES: v1 emits NO ON DELETE/ON UPDATE clause on ANY FK → all NO_ACTION deployed.
      v2 maps model rules Delete→CASCADE, SetNull→SET NULL, Protect/Ignore→NO ACTION and preserves deployed
      ON UPDATE CASCADE. Source model rules: OrderLine.OrderId=Delete, SalesOrder.CustomerId=Delete,
      StockMovement.SupplierId=SetNull; deployed physical estate matches v2 (CASCADE/CASCADE/SET_NULL + one
      ON UPDATE CASCADE on StockMovement.StockItemId). → v1 loses referential semantics; v2 faithful. v2-better, major.

EF-2  AUTHORED DEFAULTS: ossys DEFAULT_VALUE `''` (OutSystems empty-string expr) → v1 `DEFAULT ('')` (matches deployed);
      v2 `DEFAULT N''''''` = literal two-apostrophe string (deployed as (N'''''')). DEFAULT_VALUE `getutcdate()` →
      v1 `DEFAULT getutcdate()` (function, works); v2 `DEFAULT CAST ('getutcdate()' AS DATETIME2 (7))` → deployed as
      CONVERT(datetime2(7),'getutcdate()') → INSERT fails: Msg 241 conversion error (PROVEN by insert test on ParityV2DB).
      → v2 mis-interprets the OutSystems default expression language. v2-worse, blocker-class on function defaults.

EF-3  NULLABILITY TIGHTENING: v1 deployed Category.ParentCategoryId and StockMovement.SupplierId as NOT NULL
      (model+deployed say nullable; profile saw 0 rows/0 nulls → evidence-gated tightening). v2 keeps nullable.
      Interlock: NOT NULL SupplierId forecloses SET NULL delete rule. v1 diverges from deployed reality by policy;
      v2 refuses coercion (C6). Neutral-by-design but a v1→v2 BEHAVIOR CHANGE the operator must bless. major.

EF-4  UNTRUSTED FK END-STATE: source FK_OSUSR_INV_MOVEMENT_SUPPLIER is_not_trusted=1, enabled.
      v1: `WITH NOCHECK ADD` + `NOCHECK CONSTRAINT` → deployed is_not_trusted=1 AND is_disabled=1 (NO enforcement of new rows).
      v2: NOCHECK + WITH NOCHECK CHECK two-step → is_not_trusted=1, is_disabled=0 (matches source exactly).
      v2-better, major (v1 leaves an enforcement hole).

EF-5  COLLATION: source Email COLLATE Latin1_General_CI_AI. v1 drops it (deployed = DB default SQL_Latin1_General_CP1_CI_AS);
      v2 emits COLLATE and deploys faithfully. v2-better, major for collation-divergent estates.

EF-6  TYPE FIDELITY: source OSUSR_INT_SYNCLOG.RAWXML is XML. v1 emits NVARCHAR(MAX) (model text mapping);
      v2 emits XML (deployed-storage evidence for external/bt types). v2-better, major.

EF-7  TRIGGERS (both sides broken, differently):
      v1: renames trigger + retargets ON clause to logical table; BUT emits CREATE TRIGGER and
      `ALTER TABLE ... DISABLE TRIGGER [stale PHYSICAL trigger name]` in ONE batch (no GO) → the ALTER is INSIDE the
      trigger module (sys.sql_modules proven) → source-disabled trigger deploys ENABLED and every INSERT fails
      Msg 4920 (PROVEN insert test ParityV1DB.JobRun). Silent at deploy, bombs at runtime.
      v2: preserves trigger name + ON target as PHYSICAL (unrewritten, packet H2) → dbo.JobRun.sql + dbo.StockMovement.sql
      FAIL AT DEPLOY (Msg 8197) → tables partially deployed (post-trigger batches incl. extended props skipped).
      v2 correctly pairs its DISABLE with its own (physical) trigger name and disables only source-disabled triggers.
      Source truth: TR_JOBRUN_AUDIT disabled, TR_MOVEMENT_INS enabled, TR_MOVEMENT_UPD disabled.
      both-worse, blocker-class either way; v2 fails loud, v1 fails silent+late.

EF-8  V2 BOOTSTRAP ORDERING: Data/Bootstrap.sql renders Customer MERGE BEFORE City (its FK target) →
      linear execution fails FK 547 (PROVEN). Refutes "dependency-ordered" doc claim for the rendered file.
      v2-worse, blocker for the file-contract path (internal --go loader may level differently — mechanism pinned in Phase 3).

EF-9  V1 BOOTSTRAP TARGETS PHYSICAL NAMES: Bootstrap/AllEntitiesIncludingStatic.bootstrap.sql MERGEs into
      [dbo].[OSUSR_*]; PostDeployment-Bootstrap.sql guard queries [dbo].[OSUSR_REF_COUNTRY]. Against v1's OWN
      logical-name schema both fail (Msg 1088 PROVEN). v1's bootstrap lane is unusable for its stated purpose;
      v2 targets logical names (correct). v1-worse (v2 fixed), blocker on v1 side.

EF-10 CREATE SCHEMA: v1 emits none (billing.BillingAccount.sql fails on fresh DB without hand-authored schema);
      v2 emits Schemas/billing.sql (#671). v2-better, major (was G6).

EF-11 PLATFORM-AUTO INDEX: OSIDX_JOBRUN_CREATEDON (DESC) — v1 bundle OMITS it; v2 keeps as IX_JobRun_CreatedOn DESC.
      Divergent defaults. neutral-divergence, minor-major depending on estate.

## Parity confirmed (identical or equivalent, empirically)

EP-1  File layout Modules/<Module>/<schema>.<Table>.sql identical (15 files, same names/paths) — v1 uses LOGICAL names too.
EP-2  CREATE TABLE body shape: column order, type spellings+facets, IDENTITY (1,1), bracket style, 4-space indent,
      inline constraint ladder, GO framing — near byte-identical (v2 renderConstraintsElegant reproduces v1).
EP-3  Constraint naming: PK names identical everywhere incl. composite (PK_Customer_Id, PK_OrderLine_OrderId_LineNo);
      DF/CK names pass through with PHYSICAL table names embedded on BOTH sides (DF_OSUSR_ABC_CUSTOMER_FIRSTNAME);
      6/8 FK names identical; identical index names except the 3 named deltas (FK_Category_Parent_* vs
      FK_Category_Category_*, FK_SalesOrderLine_SalesOrder_SalesOrderId vs FK_OrderLine_SalesOrder_OrderId, +IX_JobRun).
EP-4  Logical-only reference materialization (E2): BOTH create enforced FK_JobRun_User_TriggeredByUserId. Identical default posture.
EP-5  Index options fidelity: filtered UIX (WHERE Email IS NOT NULL), FILLFACTOR=85, STATISTICS_NORECOMPUTE=ON,
      ALTER INDEX DISABLE for disabled indexes — both sides identical.
EP-6  Static seeds: same rows, same MERGE upsert semantics, same IDENTITY_INSERT bracketing intent, N-literals.
      (Layout differs: v1 per-module Seeds/RefData/StaticEntities.seed.sql + banners; v2 single Data/StaticSeeds.sql bare.)
EP-7  Encodings: both ASCII (UTF-8 subset), LF-only, no BOM, trailing newline.
EP-8  Round-trip data end-state: identical row counts in both deployed DBs (statics 3+3+0... — both bootstraps failed).
EP-9  MS_Description extended properties: same count and content both sides (v2 adds vendor pairs on top, see EA-2).

## Emission/aesthetic differences

EA-1  Extended-property statement style: v1 `EXEC sys.sp_addextendedproperty @name=N'...',` compact + trailing `;`
      vs v2 `EXECUTE [sys].[sp_addextendedproperty] @name = N'...'` bracketed/spaced, no `;`. cosmetic.
EA-2  v2 adds Projection.LogicalName + Projection.SsKey per table AND per column (70+70 props in deployed catalog). additive.
EA-3  Composite PK: v1 `PRIMARY KEY CLUSTERED (...)` vs v2 `PRIMARY KEY (...)` — deploy-equivalent. cosmetic.
EA-4  v1 static seed file carries banner comments + SET NOCOUNT ON + per-entity headers + `;` on own line;
      v2 bare compact statements. cosmetic (HeaderCommentsOmitted).
EA-5  v1 Bootstrap: CTE (`WITH SourceRows AS`) insert-only MERGE + separate phase-2 UPDATE section + timestamp header.
      v2 Bootstrap: inline USING(VALUES) full upsert (WHEN MATCHED UPDATE). semantic nuance + cosmetic.
EA-6  v1 sqlproj = legacy msbuild-2003 format, explicit Build items, PostDeploy glob `Seeds\**\*.sql` (SSDT allows one
      post-deploy — latent multi-file hazard), no refactorlog; v2 = SDK Microsoft.Build.Sql/2.2.0, glob-implicit,
      one PostDeploy incl StaticSeeds only, Bootstrap None'd, RefactorLog item wired (#671). operational.

## Determinism

ED-1  v2 re-run: 0-byte diff across ENTIRE dist tree (incl. manifest.json — packet §4's stamp exclusion is stale/now fixed).
ED-2  v1 re-run: 10 files differ (both bootstrap SQL — timestamp headers; manifest.json + full-export.manifest.json;
      policy-decisions/report, opportunities, validations; needs-remediation.sql + safe-to-apply.sql).
      v1 Modules/*.sql + Seeds ARE deterministic. v2-better for golden/diff-based review workflows.

## Interop / input compat

EI-1  v2 REFUSES v1's tests/Fixtures/model.edge-case.json: adapter.osm.indexFields "Required index fields missing"
      (AppCore.City, ExtBilling.BillingAccount); v1 loads same file best-effort with warnings. exit 2.
EI-2  v2 REFUSES v1's freshly live-extracted model.extracted.json: catalog.index.danglingColumn on
      Index ("OS_IDX", SystemUsers.User PK_User_Id) — v1 extraction emits an index referencing a column v2 doesn't
      materialize. v2 file-mode is NOT a drop-in reader of v1 extraction output today. (Run had includeUsers=false.)
EI-3  Extraction SQL byte-identical (md5 05b49adb...) with SESSION_CONTEXT('OsmSkipJsonRowsets') gate; v2 sets it, v1 not;
      typed-rowset content identical both sides.

## Operational

EO-1  emission.dacpac=true on a trigger-bearing estate: DacFx refuses (SQL71001) and the ENTIRE publish exits 2 with
      NO artifacts written (all-or-nothing). sqlproj-only run succeeds.
EO-2  v1 --apply applies only safe-to-apply.sql + Seeds (needs separate --apply-connection-string); v2 bundle-only by
      default; live load is a different flow (--go + grant). Full apply-path comparison in Phase 2/3 agents.
EO-3  v1 config auto-discovery (root pipeline.json) can silently flip full-export into model-reuse mode; v2 config is
      explicit per-cwd projection.json / PROJECTION_CONFIG. v2-better operationally.

## Additional empirical confirmations (round 2)

EF-12 CDC-SILENT REDEPLOY PREDICATE LATENT ON PUBLISH: v2 Data/StaticSeeds.sql + Data/Bootstrap.sql emit
      unconditional `WHEN MATCHED THEN UPDATE SET <every col>` — NO null-asymmetric change-detection predicate
      (PROVEN by reading emitted MERGE). The flagship "CDC-silent on idempotent redeploy" property (packet F2,
      V2_DRIVER stakes row 1) only wires the predicate when the profile carries CdcAwareness; the default live
      publish does not, so an idempotent redeploy of a CDC-tracked table would touch every row. v2's headline
      guarantee is a latent capability on the publish path, not a shipped default. major, v2's-own-claim-vs-reality.

EF-13 MANIFEST FK-COUNT CONTRACT DEFECT: v2 manifest.json foreignKeyCount disagrees with emitted FKs on 7/15
      tables (Customer 2≠1, City 1≠0, User 1≠0, Category 3≠1, SalesOrder 3≠2, StockItem 1≠0, Supplier 1≠0).
      The count includes INBOUND references (City counts Customer→City) — contradicting its own field doc
      "Count of FK constraints emitted (inline) for this table". A consumer trusting the manifest miscounts. minor, v2-worse.
EF-14 V2 manifest.json carries a far richer analytics surface than v1 (emitter/version/registry/coverage/
      predicateCoverage/deploymentBatches/policy/centrality/boundedContexts/schemaComplexity/queryHints — 20 sections)
      but DROPS v1's per-run decision accounting (policy-decisions.json, opportunities.json, validations.json).
      Different question-answering surfaces; not a strict superset either way. neutral (see report §Diagnostics).

## Round 3 — compressed-index fixture (closes audit M-2; adds a v1 deploy bug)

EF-15 DATA_COMPRESSION (live lane): source IX_STOCKITEM_REORDER_PAGECOMP=PAGE, IX_SUPPLIER_NAME_ROWCOMP=ROW.
      v1 PRESERVES: `WITH (DATA_COMPRESSION = PAGE ON PARTITIONS (1))` / `ROW ON PARTITIONS (1)`.
      v2 DROPS: bare `CREATE INDEX` (no WITH clause). Both read the same extraction SQL (data_compression IS
      selected). Deployed proof: V2CompTest StockItem indexes all data_compression_desc=NONE (decompressed).
      → CONFIRMED v2-worse, major functional regression on the live lane (was flagged "not verified" in the
      audit; now empirically proven). Deploying the ejected v2 project silently decompresses every compressed index.

EF-16 V1 INDEX-NAME COLLISION (deploy failure): source has IDX_STOCKITEM_REORDER on (REORDERLEVEL,WAREHOUSEQTY)
      AND IX_STOCKITEM_REORDER_PAGECOMP on (REORDERLEVEL). v1 synthesizes BOTH as [IX_StockItem_ReorderLevel]
      (no collision dedup) → deploying v1's dbo.StockItem.sql FAILS Msg 1913 "index ... already exists" (PROVEN
      on V1CollisionTest). v2 dedups to IX_StockItem_ReorderLevel_1 / _2 (SsKey-ordinal) and deploys clean.
      → v1-worse (v2's ordinal dedup fixes it), major. Confirms the naming-index-synthesis static finding empirically.

Combined: on a single estate with a compressed index + a same-leading-column index (both common), v1 FAILS TO
DEPLOY (Msg 1913) while v2 DEPLOYS BUT DECOMPRESSES. Neither yields a correct database — the audit's core thesis.

## Round 4 — empirical fan-out (8 axes, each on its own DB; deployed-database-proven)

EF-17 COMPOSITE-PK FK TRUNCATION (blocker, v2-worse): source LineComment has a physical 2-leg FK to
      OrderLine's composite PK (OrderId,LineNo). v1 emits both legs (deploys; FK present with 2 legs).
      v2's Reference IR is single sourceAttribute→targetKind (no referenced-column list) → emits ONLY the
      first leg vs OrderLine's first PK column → Msg 1776 "no primary or candidate keys ... match" + Msg 1750,
      table not created. Silent (v2 publish exit 0, no truncation diagnostic — violates its own
      downgrades-never-silent law). NEW blocker; conditional on the estate having a composite-PK-targeted FK.
      (Sibling: a LOGICAL single-col ref to a composite-PK target → BOTH engines emit an invalid single-leg FK
      → shared defect, not a v2-only regression.)

EF-18 DEPLOYED NOT NULL LOOSENED (major, v2-worse; proves audit M-3 second half): column Is_Mandatory=0 in
      model but physically ALTERed to NOT NULL. v1 emits NOT NULL (policy-decisions.json rationale
      PHYSICAL_NOT_NULL) → deployed is_nullable=0. v2 emits NULL → deployed is_nullable=1 (drops the constraint).
      Root cause: v2 catalog Column.IsNullable comes from model Is_Mandatory, not physical schema; v2 owns a rule
      literally named PhysicallyNotNull but it's inert (no physical-nullability input). On incremental apply v2
      drives the column back to NULL, weakening a prod invariant, no diagnostic.

EF-19 COMPUTED-EXPRESSION IDENTIFIERS NOT REWRITTEN (major, v2-worse): both rename table columns to logical
      mixed-case, but only v1 rewrites the computed-column EXPRESSION identifiers. v2 emits
      `[TotalValue] AS ([WAREHOUSEQTY] * [AVGCOST])` (physical uppercase) vs v1 `([WarehouseQty] * [AvgCost])`.
      Latent on default CI collation; on a case-sensitive collation v2's table FAILS to deploy: Msg 207 "Invalid
      column name WAREHOUSEQTY", OBJECT_ID NULL after. v1 deploys clean on CS. blocker-on-CS-collation.

EF-20 TEMPORAL / SYSTEM-VERSIONING (major, parity — BOTH wrong): source OSUSR_DEF_CITY temporal_type=2 +
      history table + PERIOD. BOTH engines emit a plain table (temporal_type=0), dropping GENERATED ALWAYS
      SYSSTART/SYSEND, PERIOD, SYSTEM_VERSIONING, and the history table — no warning either side. SHARED gap
      (not a switchover blocker; neither engine does it — operator must hand-author regardless).

EF-21 PERSISTED COMPUTED COLUMNS (major, parity — BOTH wrong): source TotalValue/DisplayLabel is_persisted=1;
      BOTH emit non-persisted (deployed is_persisted=0). Shared root cause: outsystems_metadata_rowsets.sql
      (byte-identical both sides) never selects sys.computed_columns.is_persisted. SHARED gap.

EF-22 SEQUENCES (major, parity — BOTH wrong): source dbo.TestSeq nowhere in either output; BOTH model-sourced
      lanes omit CREATE SEQUENCE (v1 model.json has no sequences key; v2 catalog.snapshot sequences=[]).
      Confirms packet C10. SHARED gap. (When a sequence-backed DEFAULT is in the metamodel, both emit the
      default but neither emits the sequence → both non-deployable.)

EF-23 V1 >1000-ROW AUTHORITATIVE DATA LOSS (major, v2-better; 5th v1 bug): destructive (Authoritative) sync of
      a static entity >1000 rows. v1 partitions into 1000-row batches, EACH carrying WHEN NOT MATCHED BY SOURCE
      THEN DELETE → each batch deletes the prior batch's rows. 1500-row source → deployed COUNT=500 (only batch 2,
      Id 1001-1500; 67% silent loss). PROVEN: StaticSeedSqlBuilder.cs:95/169/261-263. v2 stages-all-then-one-MERGE
      → cannot exhibit it (v2 deployed 1500). Default mode is non-destructive so only opt-in Authoritative configs
      are exposed, but the loss is silent when they are.

EF-24 TEMPORAL SCALE FACET (major, v2-better): v1 drops the scale facet for datetime2/time/datetimeoffset at the
      DDL layer → deployed widened to scale 7. Source scale 3/2/4 → v1 deployed all scale 7; v2 round-trips 3/2/4.
      v1's model.json carried onDisk.scale correctly (extraction fine) yet emitted bare DATETIME2/TIME. Latent at
      the scale-7 platform default (both bare==7); bites any non-default temporal scale (storage + schema-compare drift).

EF-25 EMPTY-STRING / SINGLE-SPACE DATA PLANE (parity): '' and the OutSystems single-space (' ') sentinel handling,
      NULL preservation, NOT NULL-carrying-'' load behavior — all byte/behavior-identical across engines on this axis.

EF-26 SCALAR TYPE MAP (parity): identifier/autonumber/reference→BIGINT, currency→DECIMAL(37,8), legacy datetime→
      DATETIME, int/bigint/bit/decimal — all identical v1↔v2 (deployed facets match). XML + collation: v2-better
      (v1 downgrades XML→NVARCHAR(MAX), drops NVARCHAR collation) — corroborates EF-5/EF-6.
