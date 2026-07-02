# PAY_ONCE_PLAN — the executable refactor plan from the single-receipt audit

> Companion to `PAY_ONCE_AUDIT.md` (the recommendation document: law, method,
> adjudicated ledger). This document is the EXECUTION side: every plan item is
> written to be picked up cold — receipts, the named carrier, the threading
> shape, the identity gate that must hold before any timing counts, the
> measurement leg, pinning tests, law surfaces touched, and ordering. Item ids
> `PL-n`; finding ids `Snn` refer to the audit's survivor ledger.
>
> **Standing rules for every item** (the house gates — restated once, not per
> item): (1) value identity before timing — the incumbent path's output pinned
> byte/value-identical before the leg's number enters any ledger; (2) fast pool
> + docker pool green before commit; (3) goldens re-record ONLY with a
> DECISIONS entry (none of these items should move a golden — a moved golden
> is a defect signal); (4) threading over memoization everywhere except the
> `ConditionalWeakTable`-keyed-by-the-immutable-value shape (`Catalog.kindIndex`
> precedent); (5) FS3511 posture in any touched `task { }`.

---

## Tier 1 — publish-critical

### PL-1 · One estate acquisition per combined verb (the audit's biggest receipt)

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry of the same date, "PL-1
> executed"). Carrier landed as `Compose.EstateAcquisition` +
> `runWithConfigAcquiring`; `projectSeedPlanUsing` / `storeLegFromAcquisition`
> / `loadStoreChain`-threaded `runStoreLeg` / `composePrefixState` (S52) /
> `Transfer.runWithRenamesUsing` + `runReconcilingWithRenamesUsing` (S13).
> Identity gates: `PayOnceCombinedVerbTests` (pure) +
> `PayOnceCombinedVerbDockerTests` (docker; wire receipt: ONE
> `adapter.osm.extract` per combined store verb). No goldens moved.

**Findings:** S00, S45, S50 (load leg), S05 (store leg), S51 (lifecycle ×4
loads), S53 (diff chain ×2 + FTC re-derive), S52 (Outputs built and discarded
by the seed-plan re-projection), S13 (migrate-with-data re-diff).

**The fact paid twice.** `runWithConfigAndLoad` runs the FULL publish
(`runWithConfig` → `readAndHydrateConfigModel` at `Pipeline.fs:1827`: the
23-result-set OSSYS snapshot + every static and bootstrap kind's row stream),
then `loadFromConfig → emittedSeedPlan` re-runs `readAndHydrateConfigModel`
(`Pipeline.fs:1944`) AND the whole pass chain AND every seed-lane render
(`projectSeedPlan`, `Pipeline.fs:1914`) against the same live source, only to
re-derive the seed plan the publish just had in its hands. The store leg is
the same shape: `runWithConfigAndStore` re-extracts via `emittedSchema →
readConfigModel` (`Pipeline.fs:2147`), then `LifecycleStore.load` parses the
same lifecycle file four times (`Pipeline.fs:1957,1977,2006,2029`) and the
per-edge diff chain is computed twice (`:1960` vs `:1980`).

**Named carriers.**
- `loadFromConfigUsing (catalog: Catalog) (bootstrapRows) (migration)
  (finalState: ComposeState)` — the load leg consumes the publish's extract
  triple + composed state; `loadFromConfig cfg` remains the standalone entry
  (compute-then-delegate, the `hydrateCatalogUsing` pattern).
- `storeLegFromCatalogUsing (emitted: Catalog) (finalState)` — same for the
  store leg.
- `runStoreLegUsing (chain: EpisodicLifecycle)` — ONE `LifecycleStore.load`
  at the verb boundary; `nextCoordinate`/`record`/`priorSchema`/`refactorLog`
  consume the loaded chain (S12's `recordVerified` gets the same treatment in
  PL-10).
- `edgeDiffs : CatalogDiff list` threaded once through the store leg
  (`schemaEvolutionChainUsing`); prior schema read as
  `(List.last episodes).Schema` instead of the FTC re-fold.
- Migrate-with-data (S13): a `*Using` form of `runWithRenamesWith` taking the
  schema leg's already-computed `CatalogDiff`.

**Why S52 dissolves here.** The seed-plan re-projection
(`projectSeedPlan` running `projectWithState` and discarding `Outputs`) stops
existing once the load leg receives the publish's `finalState`. The NM-02
registered⇔executed invariant (which killed the RemediationEmitter finding,
K23) is untouched: the artifact-producing publish still runs every emit step;
it is the REDUNDANT second projection that disappears, not any step within
the real one.

**Identity gate.** The load leg's seed plan and the store leg's episode must
be VALUE-IDENTICAL between the `cfg` form and the `*Using` form on the same
estate (a docker-pool test: run both forms against one seeded database,
assert plan/episode equality). The combined verbs' end artifacts byte-stable.

**Measurement leg.** The corpus gains a combined-verb leg: time
`runWithConfigAndLoad`-shaped work before/after (expected: the load leg's
marginal cost collapses from ≈ a second publish to ≈ zero). Wire receipts:
one OSSYS snapshot + one hydration per combined verb, measurable by counting
`adapter.osm.extract` / `ingestion.rowDrain` Bench samples (should halve).

**Law surfaces.** None moved. Watch: the load leg must consume the catalog
value the publish EMITTED (post-chain), not the pre-chain one — thread
`finalState.Catalog`/`EmittedCatalog` deliberately and pin which with a test.

**Effort/risk.** Medium-large (Pipeline.fs verb plumbing; the legs are
`task { }` — FS3511 posture). Highest value in the audit.

### PL-2 · AllData publishes stream static kinds once (S04)

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry "PL-2 executed"). The
> graft-from-bootstrapRows arm, on BOTH schedules: two-phase
> `hydrateAllDataArm` + `hydrateCatalogFromBootstrapRowsUsing`; pipelined
> drain-worker row retention (`retainRows`). Named schedule note
> `data.hydration.staticGraftRidesBootstrapDrain`. Gate:
> `PayOnceCombinedVerbDockerTests` AllData fact — drain count 1/publish,
> bundles byte-identical across schedules. K26 respected: the skip arm was
> rejected because it would move the catalog-plane bytes.

**The fact paid twice.** Under `DataComposition.AllData`, the static lane is
dispatched EMPTY (`DataEmissionComposer.fs:122-124`), yet
`hydrateCatalogUsing` still streams and grafts every static kind
(`Hydration.fs:104`) — and the bootstrap arm then streams the SAME static
kinds again (`Hydration.fs:239-242`, AllData ⇒ eligible includes static).

**Named carrier.** Composition-gated hydration: `hydrateCatalog*` learns the
`DataComposition` (already config-derivable via `Config.dataCompositionOf`)
and under `AllData` skips the static-graft stream entirely — or grafts from
the `bootstrapRows` map already drained (zero extra wire either way). The
named-skip discipline: the skip is composition-derived, logged in the
hydration diagnostics, never silent.

**Identity gate.** AllData publish artifacts byte-identical before/after
(the grafted populations feed goldens/canaries on OTHER compositions — pin
an AllData-composition bundle test asserting `Data/Bootstrap.sql` equality
and static-lane emptiness both ways).

**Measurement.** Corpus AllData leg: hydration wire volume halves for
static-heavy estates (count `ingestion.rowDrain.rows` samples).

**Effort/risk.** Small-medium. One subtlety: non-data consumers of grafted
populations (fidelity's residual collector reads the HYDRATED catalog — K26
established these are different catalog values; verify the AllData skip
doesn't starve `emittedResidualCollector`'s tolerance witnesses; if it does,
the graft-from-bootstrapRows variant is the correct arm, not the skip).

### PL-3 · Render-constant hoisting through the data lane

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry "PL-3 executed").
> `renderMerge` top-bindings (rowCount/writable/matchNames);
> `renderUpdateForKind` per-kind closure; `valueRows`/`phase2Rows = phase1Rows`
> in both emitters; `TableId.withoutCatalog`; `renderStagedPhase1` takes the
> threaded writable set. Gates: factorization laws + corpus byte-identity +
> docker pool unchanged.

**Findings:** S18 (Phase1Merges/Phase2Updates identical list built twice),
S19/S38 (`matchColumnNames` ×2, `writableAttributes` ×3 per renderMerge),
S20/S59 (`List.length typedRows` + staging verdict up to ×4 per kind), S23
(`TableId` rebuilt ×4), S27/S57 (renderUpdate recomputes per-kind constants
per ROW: Bench label, `matchAttributes`, deferred filter, setCells/whereCells
projections), S61 (`typedRows |> List.map snd` ×2 per staged cyclic kind),
plus the unadjudicated per-row `String.Concat(bench, ".renderUpdate")` label
(same slice, same fix — hand-ruled IN: the receipt is textually verifiable).

**Named carriers.** One slice through `scriptOfTyped`/`renderMerge`/
`renderUpdate`/`StagedMerge`:
- a per-kind **render vocabulary** binding set at `renderMerge` top:
  `writable`, `matchNames`, `orderedColumns` computed once and threaded into
  `MergeBuildArgs` + `StagedMerge` (S19/S38);
- `rowCount : int` computed once in `scriptOfTyped`, threaded (S20/S59);
- `valueRows` (`List.map snd`) bound once (S61);
- `phase2Rows = if Set.isEmpty deferred then [] else phase1Rows` (S18);
- `deployTarget : TableId` bound once in `scriptOfTyped` (S23; or a
  `TableId.withoutCatalog` helper if a second consumer appears);
- `renderUpdateForKindUsing` — a per-kind prebound closure carrying the lane
  label, `setAttrs`, `whereAttrs`; the row loop threads only `typedValues`
  (S27/S57 + the label item). NOTE the K13 kill correctly bounds the per-row
  lane at ≤1000 rows (staging threshold) — the win is real but small; this
  rides along because the closure form is also the CLEANER shape, and S57's
  receipts show `StagedMerge` already models it (`renderStagedPhase2`
  hoists the same projections).

**Identity gate.** The existing pure factorization laws
(`renderLoad ≡ emitFromPlan`, `renderQuanta ≡ renderLoad`) plus the corpus
byte-identity legs (pipelined + quanta passes assert `Rendered` equality)
already pin this end-to-end — they MUST pass unchanged, no re-record.

**Measurement.** Corpus emit leg + pipelined pass (expect single-digit-%
emit-CPU reduction; the honest expectation is small — this item is as much
altitude hygiene as wall-clock).

**Effort/risk.** Small-medium; all inside two files + KindColumns call
shape. No law surfaces.

### PL-4 · SSDT emission lookups: derive once, thread everywhere

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry "PL-4 executed").
> `FkEmissionLookups` + `fkResolutionsUsing` + `resolvedFksOf` +
> `foreignKeyDefOfUsing` + `firstKindBySchemaOf` + `Catalog.kindKeySet`
> (CWT) + `ArtifactByKind.mapValues`; one lookups/resolutions/overlay
> binding in the diagnostics assembly. Gates: DDL goldens + docker pool
> unchanged; pure pool green.

**Findings:** S46 (`buildLookups` ×3 per publish + a 4th walk), S47 (`fkDef`
resolves every reference ×4 per publish), S48 (`foreignKeyDefOf` pays a
whole-catalog `buildLookups` PER FK in SchemaMigrationEmitter), S37/S49
(`DecisionOverlay.ofComposeState` ×3 per publish), S28/S54
(first-kind-of-schema re-filter+re-sort per kind), S56
(`ArtifactByKind.create` rebuilds the catalog keyset Set per construction).

**Named carriers.**
- `FkEmissionLookups` — the `buildLookups` triple as a named value; `*Using`
  siblings for `foreignKeyDropDiagnostics` / `foreignKeyDecisionDrop…` /
  `foreignKeyNameCollision…` threaded from the ONE emit-step computation
  (`Pipeline.fs:1499-1514` call sites; the E3 `emittedNamesForKind` pattern).
- `resolvedFkByReference : Map<SsKey, ForeignKeyDef>` per kind — computed
  once in `kindToSsdtFile`/flat-stream (sibling of `emittedNamesForKind`),
  consumed by CREATE TABLE inline FKs, NOCHECK alters, and both diagnostics
  (S47); `foreignKeyDefOfUsing lookups` for SchemaMigrationEmitter (S48).
- one `decisionOverlay` binding in `runWithConfigCore` (or an
  `EmitContext.DecisionOverlay` field) shared by the emit step + both
  diagnostics (S37/S49).
- `firstKindBySchema : Map<SchemaName, SsKey>` per module, threaded into
  `kindToSsdtFile` (S28/S54).
- `Catalog.kindKeySet` — CWT-cached keyset beside `kindIndex` (S56), plus an
  `ArtifactByKind.mapValues` that preserves the proven keyset across a
  key-preserving rewrite (`Pipeline.fs:343`).

**Identity gate.** DDL goldens + docker pool unchanged (all pure threading of
identical values). The diagnostics' CONTENT byte-identical (they consume the
same lookups, earlier).

**Measurement.** Publish-side Bench: `emit.ssdt.*` totals on the canary; at
estate scale S47 alone is 4→1 reference resolutions × every FK.

**Effort/risk.** Small-medium; mechanical `*Using` threading with the E3
precedent already in the file.

### PL-5 · Profile evidence indexes (the tightening passes' O(n·m) floor)

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry "PL-5 executed").
> `Profile.tryFind*` bodies consult CWT-cached first-wins indexes;
> `Catalog.sortedKinds` (CWT) under `kindContexts`; the FK target index
> widened to per-Reference entries carrying the resolved PK attribute;
> one cardinality resolution per FK decision; one-fold cohesion; PageRank
> graph-constants hoisted (`pageRankStepWith`). Gates: pure + docker
> pools green, no pass added/removed, no goldens.

**Findings:** S35 (`Profile.tryFindColumn`/`tryFind*` are linear list scans
paid per attribute × per pass), S36 (FK derivations re-resolve target kind +
single-PK attribute per reference at 2+ sites), S39 (Nullability and
CategoricalUniqueness each re-derive the identical sorted (Kind × Attribute)
context list), S40 (cardinality scan ×2 per decision), S41 (cohesion re-walks
the edge list per module ×2), S42 (PageRank re-derives node count + dangling
set per iteration).

**Named carriers.**
- `Profile.columnIndex` / `foreignKeyIndex` / `cardinalityIndex` —
  `ConditionalWeakTable<Profile, Map<SsKey, _>>` keyed by the Profile VALUE
  (the `Catalog.kindIndex` precedent — the one sanctioned cache shape; the
  `tryFind*` signatures stay put, their bodies consult the index) (S35, S40).
- widen `ForeignKeyTargetIndex` entries to carry the resolved `tgtPkAttr`
  and key by `Reference.SsKey` so consumers resolve once (S36).
- a shared `attributeContexts` computed once per chain run and threaded to
  both passes — or CWT `Catalog.sortedKinds` consumed by `kindContexts`
  (S39).
- one-fold `(intra, total)` edge classification per module (S41);
  `pageRankStepWith nodeCount danglingNodes` hoisted bindings (S42).

**Identity gate.** Pure pool (ProfileTests, pass tests, parity tests)
unchanged; AxiomTests (`registered ⇔ executed` untouched — no pass
added/removed).

**Measurement.** A pass-chain Bench comparison on the canary catalog
(`pass.nullability`/`pass.categoricalUniqueness` labels); at estate scale
this is the difference between O(A·P) and O(A + P·log A) for the tightening
tier.

**Effort/risk.** Small-medium. CWT-on-Profile requires Profile to be
reference-stable through a pass run (it is — passes thread one value).

### PL-6 · The text plane pays per byte once

**Findings:** S25 (whole SSDT artifact built, then `ConstraintFormatter`
split-and-rejoins it — two more full copies), S26 (`TrimStart`/keyword index
per line ×2-3 across dispatch), S33 (`executeStreamWith` renders each
statement to a throwaway StringBuilder then copies into `pendingDdl`), S24
(`executeBatchParallel` re-parses V2's OWN rendered scripts with
`TSql160Parser` to recover GO boundaries V2 itself inserted), S31
(`stream.ToArray()` copies the whole serialized artifact before decode —
3 sites), S30 (`LifecycleStore` serializes the catalog to a UTF-16 string so
`WriteRawValue` can re-scan it back to UTF-8), S29 (codec sorts then rebuilds
a Map just to iterate), S32 (SsKey serialized twice per element), S14 (the
verb-side sibling of S24: statements joined into text, `executeBatch`
re-parses for boundaries).

**Named carriers.**
- `ConstraintFormatter.formatIntoUsing` — per-statement formatting into THE
  StringBuilder (a `RenderedLines` seq carrier replaces text→lines→text)
  (S25); a `TrimmedLine (indent, trimmed)` pair threaded from `tryFormatLine`
  into the shape formatters (S26).
- `Render.toSql pendingDdl s` — direct write; the buffer IS the carrier
  (S33).
- `ParallelSafe<SegmentedScript>` / `Deploy.executeSegments : SqlConnection
  -> string list -> Task<unit>` — segments carried as the list the renderer
  already had; the parser re-split becomes the fallback for text of unknown
  provenance only (S24, S14). NOTE K16/K17 bound this correctly: the
  formatter's text-shape parsing and pinNewlines are LOAD-BEARING — this
  item moves only the boundary RE-DISCOVERY, never the formatting.
- `CatalogCodec.writeTo : Utf8JsonWriter -> Catalog -> unit` (expose the
  private `wCatalog`) so the episode writer nests directly (S30);
  `stream.TryGetBuffer()` span decode in `PinnedWriting` (S31);
  `sortedValues` consumed directly (S29); `serializedKeys` bound once (S32).

**Identity gate.** BYTE-identical artifacts everywhere: goldens, the corpus
data lanes, LifecycleStore round-trip tests, manifest hashes. This item has
the strictest gate and the most mechanical verification.

**Measurement.** Corpus emit leg + a store-leg timing; expected win is
memory-copy reduction (~2-3 full artifact copies per publish) more than CPU.

**Effort/risk.** Medium: many small sites, each trivially checkable, each
individually committable. Execute file-by-file.

---

## Tier 2 — verb-scale (transfer / migrate / capture / preflight)

### PL-7 · Schema-only reads where rows were never the point

**Findings:** S01 (transfer contract via `ReadSide.read` materializes ≤100k
rows/table into `Modality.Static` the plan never consumes, then
`collectInOrder` streams the SAME rows for the load), S09 (SliceApply reads
the target with the row-lifting read; consumes schema only), S02
(profile-capture lifts rows, STRIPS them — the 4.4 trap — then LiveProfiler
re-streams the same tables), S10 (preflight builds a FULL EvidenceCache for
a gate consuming only the tightened columns' null counts).

**Named carriers.**
- `ReadSide.readSchema` — the rows-off contract read (the schema projection
  `read` already computes, minus the per-table `readRows` drain). Transfer
  and SliceApply consume it (S01, S09).
- Profile-capture: EITHER `readSchema` + live attach (pays rows once) OR
  `LiveProfiler.attachDerived` over the populations `read` already lifted
  (pays rows once, derives evidence purely — the P1 machinery, reused; live
  fallback for >100k tables which `read` skipped). The second is the
  pay-once-truer arm and the plan's recommendation (S02).
- `LiveProfiler.nullCountsFor (overlay)` — a scoped aggregate probe for the
  preflight gate (S10); the full cache remains for callers that need it.

**Identity gate.** Transfer/SliceApply/capture verb outputs value-identical
on a docker-pool fixture both ways; the preflight verdict identical on a
violation fixture.

**Measurement.** Transfer verb wall-clock on a seeded fixture: the contract
read drops from (COUNT + full scan)/table to metadata-only.

**Effort/risk.** Medium. `readSchema` must preserve `read`'s marking
semantics minus populations (survival rule 8's trap is ABOUT this seam —
the new reader must not silently change Static marking for downstream
consumers; pin with a test).

### PL-8 · Live discovery becomes single-scan (S03, S06)

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry "PL-8 executed").
> `EvidenceCache.cachedKindOfColumns` over the one drain (aggregate only
> for sampled kinds, via `aggregateCountsFor`, run pre-stream — no MARS);
> `readRowsStreamCapped` + a maxRows+1 drain replaces the COUNT probe.
> Gates: corpus live≡derived cache equality (P1 law), pure + docker pools.

**The fact paid twice.** `discoverKind` full-scans each unsampled table TWICE
(COUNT_BIG aggregate, then the full row stream); `readRows` COUNT(*)-probes a
table it immediately streams in full.

**Named carriers.** `EvidenceCache.cachedKindOfQuanta` over the ONE stream
(the P1/P3 machinery pointed at the live path's own interior): stream rows,
derive exact RowCount/NullCounts from the drained quanta; the aggregate query
survives ONLY for sampled kinds (capped stream can't yield exact counts —
the P4 exactness contract). `readRowsCapped` drains with maxRows+1 early stop
(overflow ⇒ None) so the gate and the rows ride one scan (S06).

**Identity gate.** The corpus live-scan leg asserts cache identity vs the
incumbent two-query discovery BEFORE the timing counts (the P1 law, again).
Sampled-kind behavior pinned unchanged (tiered corpus leg).

**Measurement.** Corpus `profile live-scan c4` leg — expect roughly the
aggregate scan's share to vanish (the leg's server-side work halves for
unsampled kinds).

**Effort/risk.** Small-medium; LiveProfiler-local.

### PL-9 · Streaming transfer: per-kind constants staged once (the lane the docstrings already promise)

**Findings:** S15 (staging DDL / capture MERGE / keymap SQL re-rendered per
50k chunk), S16 (cell getters + FK ordinals re-staged per chunk against the
docstring's "once per kind"), S17 (buffered Phase-2 recomputes PK/deferred
sets per ROW; the staged quantum sibling exists), S22 (renamed RowBasis
rebuilt in Phase 2), S11 (Phase 2 re-streams FULL row width when only PK +
deferred FK columns feed the UPDATE), S14 (executeSegments — shared with
PL-6).

**Named carriers.** `CaptureKindSql` (per-kind rendered texts + insertCols,
staged beside the sticky lane); `kindWriteLane` (per-kind closures staged
next to `pkOf` before the chunk loop); `phase2UpdateSqlStaged` (the
StaticRow twin of the quantum sibling); `basisByKind : Map<SsKey, RowBasis>`
threaded to both phases; `ReadSide.readRowsProjectedStream (columns)` — the
column-projected Phase-2 stream (S11: the wire pays PK + deferred columns,
not the full width).

**Identity gate.** Transfer round-trip fixtures (docker pool) byte/value
identical; the reverse-leg DML proof tests unchanged.

**Measurement.** The reverse-leg scale tests' wall-clock; S11's wire cut is
proportional to (row width − pk − deferred)/width on cycle kinds.

**Effort/risk.** Medium; TransferRun/TransferCellShaping/SurrogateCapture are
`task { }`-heavy (FS3511 posture) and the chunk recursion shapes must keep
their prefetch overlap.

### PL-10 · Small verb-state threadings (batchable, each trivial)

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry "PL-10 executed").
> S12 loadChain/nextCoordinateOfChain/recordOnChain; S08 scoped ingest;
> S43/S44 Map-membership; S58 one-pass folds; S34 rowSeedBasis (streaming
> FNV — seeds unchanged); S21/S60 qualifiedParts hoists.

**Findings + carriers, one line each:**
- S12: `recordVerified` loads the lifecycle twice → thread one loaded chain
  (`nextCoordinateOfChain`/`recordOnChain`).
- S08: `runCore` with a LoadSet ingests EVERYTHING then filters → use the
  existing `Ingestion.collectInOrderFor (loadSet ∪ reconciledKinds)`.
- S43/S44: Closure rebuilds closed-key Sets per reference/per step → one
  `closedKeySets` map per report; direct `Map.containsKey` in `nextFetches`.
- S58: `foldByKind`'s O(N²) `@`-append → `List.collect` (the
  `Result.aggregate` accumulator precedent).
- S34: Faker re-serializes the row SsKey per fresh-draw cell → `rowSeedBasis`
  bound once per row (deterministic-draw law pinned by existing synthetic
  tests — verify seeds unchanged, else it's a synthetic-data-breaking change
  and must NOT land silently).
- S21/S60: `qualifiedParts` hoisted above the per-attribute lambda (the
  `attachAnnotations` form already in the file).

**Identity gate.** Each has an existing test surface (lifecycle round-trip,
transfer fixtures, closure oracle tests, migration emitter tests, synthetic
determinism tests). No goldens.

### PL-11 · Delete the dead standalone readers (S07)

> **STATUS: EXECUTED 2026-07-02** (DECISIONS entry "PL-11 executed").
> All four deleted; the session-30 PK bench note relocated onto
> `readSchemaCombined`. Grep-proof + pools green.

Four standalone `ReadSide` readers duplicate `readSchemaCombined`'s SQL
verbatim with ZERO callers — the reflection SQL is maintained twice. The
dead-algebra precedent (DECISIONS 2026-06-04) says delete them; the combined
batch becomes the single definition site. Gate: grep-proof of zero callers +
pure pool. (If any is test-referenced, the test moves to the combined read.)

---

## Tier 3 — gap leads (UNVERIFIED — the critic's receipts, no skeptic pass yet)

Run these through the same verify → plan cycle before execution; receipts
are concrete but unadjudicated:

- **G1 (acquisition):** `projection.json` parsed up to FOUR times in one
  migrate leg (`Program.fs:341-346,477` + the tightening gate's re-read + …).
  Carrier shape: parse once at dispatch, thread the typed config.
- **G2 (representation):** `SliceFlowRun` chains extract→apply through a TEMP
  FILE serialization of the in-memory `GoldenDataset` it already holds
  (`SliceFlowRun.fs:25-36`). Carrier: an in-process `applyGoldenUsing`.
- **G3 (derivation):** `LogSink` serializes every envelope twice (once to the
  NDJSON writer, once retained typed and re-serialized at run save;
  `LogSink.fs:635` + Run store). Carrier: serialize once, share the string.
- **G4 (materialization):** `Run.list` fully parses every `run.json`
  INCLUDING the events array + artifact blobs to render an index
  (`Run.fs:227-234,146-149`). Carrier: an index projection / lazy artifact
  fields.
- **G5 (representation):** `SummaryFormatter.format` builds the summary,
  `sb.ToString()`, then splits per line with `\r`-strips. Carrier: emit
  lines.
- **G6 (tower):** `LogSink.serializeEnvelope` allocates MemoryStream +
  writer + `ToArray()` + UTF-16 decode PER envelope (`LogSink.fs:544-545`).
  Carrier: a pooled/reused writer or `PinnedWriting`'s span decode (PL-6's
  S31 fix reuses here).

---

## Ordering and dependencies

1. **PL-1** first — largest receipt, and it SUBSUMES S52 (don't do S52
   separately) and simplifies PL-2's test surface (fewer legs re-hydrating).
2. **PL-2** (independent, small) any time.
3. **PL-4 → PL-3** next on the publish path (PL-4's lookups thread through
   the same files PL-3 touches; doing PL-4 first avoids rebasing PL-3).
4. **PL-5** independent (Core-only).
5. **PL-6** file-by-file, anytime; its S24/S14 carrier is shared with PL-9 —
   land `executeSegments` once, consume twice.
6. **PL-8** before **PL-7**'s profile-capture arm (attachDerived reuse reads
   cleaner once the live path itself is single-scan).
7. **PL-9, PL-10, PL-11** as verb-time permits; **Tier 3** only after its own
   verify pass.

## What this plan deliberately does NOT contain

The 38 killed findings (see the audit §6) — the skeptics' refutations are
standing law for this plan: the raw-string quantum IR is the codec's law
(K11); the per-call ScriptDom generator is a recorded correctness decision
(K34); NM-02 keeps every emit step running on artifact-producing runs (K23);
`pinNewlines`' two Replaces are two different transforms (K17); the
extended-properties triple-scan is a disjoint partition (K01); CDC re-reads
are deliberate before/after measures (K03/K08). Re-proposing any of these
requires refuting the refutation, not re-finding the receipt.
