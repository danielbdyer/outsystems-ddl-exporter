# AUDIT 2026-06-04 — Actionables Backlog

**Date:** 2026-06-04
**Purpose:** The complete, executable backlog distilled from the two audit waves
(`AUDIT_2026_06_04_BLINDSPOT_COMPRESSION.md`, `CRYSTALLINE_FORM.md`,
`AUDIT_2026_06_04_PERIPHERY_ADVERSARIAL.md`). Every item carries source, type,
risk, estimated reclaim, verification, dependencies, and status. Ordered into
tranches by the revised reachability path (cheapest-safest first).
**Reclaim numbers are the adversarially-corrected (smaller) ones**, not wave-1's
survey estimates. Where wave 2 falsified a wave-1 collapse, the item is in the
**DO-NOT** register at the bottom, not the backlog.

**Status legend:** ☐ todo · ◐ in progress · ☑ done · ⊘ blocked/gated

---

## Tranche 1 — Stale-doc-claim fixes (doc-only; correctness; unblocks archival)

Zero code risk; pure correctness edits; should land first because they remove
load-bearing falsehoods and unblock doc archival.

- **D1 ☑ — "zero production callers" stale claim.** [done 2026-06-04 — added a
  superseded-flag at `CLAUDE.md:98`; left the append-only `DECISIONS.md:20369`
  (already superseded by `:20464`), the dated `WAVE_6_MORPHOLOGY` snapshot, and
  the finer-grained `THE_USE_CASE_ONTOLOGY.*` per-symbol claims untouched.] The diff/`Lifecycle`
  machinery is now wired into production. Fix the false assertions at
  `CLAUDE.md:98` and `DECISIONS.md:20369`; point to the resolution at
  `DECISIONS.md:20049/20464` + `EXECUTION_PLAN.md:775`.
  **DO NOT edit `CRYSTALLINE_FORM.md:184`** — its "zero production callers"
  refers to `Prism`/`PassContext`/`LineageTree`/`DiagnosticLattice`, which *are*
  grep-verified def-only (true claim, different referent).
  *Verify:* grep the phrase; confirm only the two false sites changed.

- **D2 ☑ — axiom-count drift.** [done 2026-06-04 — fixed AXIOMS.md:24 self-count
  (A1–A43 / T1–T16, confirmed against the body's `## The Change Algebra` §1809),
  README.md:111+645, PRODUCT_AXIOMS.md:474, CLAUDE.md:287.] Source of truth `AXIOMS.md` = **A43 / T16**, but
  `AXIOMS.md:24` self-states "A1-A42" (fix it first), then propagate:
  `README.md:111/627/645` ("A40"), `PRODUCT_AXIOMS.md:474` ("A40"),
  `CLAUDE.md:283/287` ("A41"/"A40"). Cite the count, don't restate it.
  *Verify:* `grep -rn "A1[–-]A4[0-2]\b"` returns only intended historical contexts.

- **D3 ☑ — stale test counts.** [done 2026-06-04 — replaced the 4 KICKOFF.md
  "882" as-current claims with "see `scripts/test.sh`" pointers. Left the
  historical baselines frozen (CLAUDE.md:460 / STAGING.md:112 / PLAYBOOK.md:602 /
  VISION_REVIEW.md:230 / V2_PATTERNS_COMPENDIUM.md:392 are dated Stage-0 / past-
  slice context, not current-state claims). Actual pure pool at execution: 2,794
  passing / 208 skipped.] `882/790/631/588/1128` are frozen snapshots
  (actual ~2,729). Ban absolute counts from live nav docs: `KICKOFF.md` (×4),
  `CLAUDE.md:460`, `STAGING.md:112`, `PLAYBOOK.md:602`, `VISION_REVIEW.md:230`,
  `V2_PRODUCTION_CUTOVER.md:1128`, `V2_PATTERNS_COMPENDIUM.md:392`. Replace with
  "see test run." Leave dated CLOSE-doc historical deltas intact.

---

## Tranche 2 — Correctness bugs (code; verifiable against the suite)

- **C1 ☑ — `SchemaComplexityPass` computes FK metrics over an EMPTY topology.**
  [done 2026-06-04 — added `PassChainAdapter.liftCatalogTopologyPass`, exposed
  `SchemaComplexityPass.name`, rewired both chain sites, added a chain-level
  regression test. Core builds 0-warning; targeted 18/18 + full pure pool
  2,794/0-fail green.]
  `RegisteredTransforms.fs:124` (`allChainSteps`) and `:190` (`allChainStepsFor`)
  wire it as `liftDecisionPass (SchemaComplexityPass.registered None)`, baking in
  `TopologicalOrder.empty`. Fix: add `PassChainAdapter.liftCatalogTopologyPass`
  (reads `state.TopologicalOrder` at apply-time), expose
  `SchemaComplexityPass.name`, rewire both sites. Severity **High** (silent
  wrong-answer). *Verify:* new chain-level regression test asserting
  `final.SchemaComplexity.Value.CyclomaticComplexity > 0` on `sampleCatalog`
  (which carries an order→customer FK) — fails pre-fix, passes post-fix +
  `scripts/test.sh fast`.

- **C2 ☑ — `BoundedContextPass.pickLabel` tie-break is a self-comparison no-op.**
  [done 2026-06-04 — replaced the `maxBy` self-compare with
  `sortBy (fun (lbl,cnt) -> -cnt, rootOriginal lbl) |> List.head`. Pure pool green.]
  `BoundedContextPass.fs:80`:
  `-CompareOrdinal(rootOriginal lbl, rootOriginal lbl)` is always 0. Fix: replace
  the `List.maxBy` with `List.sortBy (fun (lbl,cnt) -> -cnt, rootOriginal lbl) |> List.head`
  (count DESC, label ASC, deterministic). Severity **Low** (output stayed
  deterministic by upstream-sort luck). *Verify:* `scripts/test.sh fast`
  (`BoundedContextPassTests`).

---

## Tranche 3 — Dead-algebra deletion ☑ DONE (2026-06-04)

[Executed 2026-06-04 — `DECISIONS 2026-06-04 — Retire the speculative
writer-trinity / optics-duo / Kleisli-product algebra`. Removed `LineageTree`+CE,
`Certificate`, `Prism`, `PassContext`, `Pass.product`/`&&&`/`first`/`second`,
`DiagnosticRelation`+`relations`/`isMinimal`/`minimal`, and the dead
`CatalogLenses.sequences`/`indexesOf` — keeping `DiagnosticLattice.subsumes`.
Updated CLAUDE.md (load-bearing writer-trinity block + F#-feature table + Kleisli
commitment), DECISIONS (amendment), AxiomTests (5 Facts removed, H-008 repointed
to subsumes, 4 Skip stubs de-claimed), and the Diagnostics/Optics doc-comments.
Rewrote the H-015 Lens-law tests from a `PassContext` carrier to a tuple
(preserving lens coverage since Optics.fs is otherwise untested). Core + tests
build 0-warning; pure pool green. ~716 Core LOC + ~2.5K test LOC removed.
**`SqlStorageType.to/ofPrimitiveType` excluded** — ambiguous (delete-as-dead vs
promote-as-canonical); deferred to the AR1/`ColumnType` slice.]

### (original plan, for the record)

The single largest defensible Core reclaim (~716 Core LOC + ~2,300-2,800 test
LOC). **Confirmed worthwhile by the trajectory steelman** (`PERIPHERY_ADVERSARIAL.md` §8):
the two surfaces whose consumers shipped both *declined* the prebuilt structure.
**Gated** because these surfaces are named in CLAUDE.md's load-bearing
"writer-monad trinity" + AXIOMS + HORIZON — the codebase's own discipline
("do not break a load-bearing commitment without writing the amendment first")
requires a DECISIONS amendment *before* deletion.

- **X0 ⊘ — DECISIONS amendment** retiring the writer-trinity/optics-duo/Kleisli-
  product speculative surfaces, citing the trajectory evidence (policy-diff and
  Catalog↔DDL both declined them). Amend CLAUDE.md load-bearing commitments +
  F#-feature-surface table + AXIOMS + HORIZON. **Must precede X1-X2.**
- **X1 ☐ — delete from Core** (after X0): `Prism` (`Diagnostics.fs`+`Optics.fs`,
  ~102), `PassContext` (~93), `Pass.product`/`&&&`/`first`/`second` (~35),
  `Certificate` (~109), `LineageTree`+`lineageTree` CE (~294),
  `DiagnosticLattice`+`DiagnosticRelation` (~113 — **keep `subsumes` ~30 LOC** as
  a documented design note for the future `diagnose` verb), dead lenses
  `CatalogLenses.sequences`/`indexesOf`, and `SqlStorageType.to/ofPrimitiveType`
  (separately dead, no amendment needed).
- **X2 ☐ — delete the law tests** that go with them: `AdjunctionLawTests` (247),
  the LineageTree/Certificate/Prism/Kleisli sections of `LineageTests.fs` (~115
  refs) and `DiagnosticsTests.fs` (~120 refs / ~113 lines), and any
  Prism/PassContext/lattice property tests. *Verify:* `scripts/test.sh fast`
  green; `dotnet build` clean.

---

## Tranche 4 — God-file decompositions (0-LOC relocation; navigability)

Pure structural splits; no semantics change. **Must NOT merge forked code.**

- **R1 ☑ — `CatalogReader.fs` decomposed (DONE 2026-06-04).** 2,518 → **168 LOC
  facade**, split into `OssysRowsetTypes.fs` (361, DTOs) → `OssysTranslation.fs`
  (534, shared leaf layer — verified by call-graph) → `OssysJsonReader.fs` (763,
  JSON path) → `OssysRowsetReader.fs` (749, rowset path) → `CatalogReader.fs`
  (168, `SnapshotSource` + `parse` + `registeredMetadata`). Shipped in 3 green
  commits (types / translation / readers+facade). `SnapshotSource` kept in the
  facade (its DU cases are the public entry, 89× refs); external `CatalogReader.*`
  record-type refs rerouted to `OssysRowsetTypes.*`. Confirmed the JSON/rowset
  paths have **zero** code cross-dependencies (all apparent cross-refs were
  doc-comments + `parseAttribute`⊂`parseAttributeRow` substring false positives) —
  so **NOT merged** (N1 honored). Pure relocation; solution builds 0-warning; pure
  pool 2732/0-fail (CatalogReader is pure parsing — pure pool is complete
  verification, no canary needed).
- **R2 ☐ — `Deploy.fs` 1470 → 5 modules**:
  `Deploy.{Container,Connection,Execution,Parallelism,Canary}`. `parallelismCache`
  stays private inside Parallelism (it's a leaf, not a shared spine). *Verify:*
  build + Docker pool (`scripts/test.sh docker`).

---

## Tranche 5 — True small extractions (verbatim-dup / proven-consumer only)

Each is a genuine shared-type-and-function dup or a 2+-consumer collapse —
*not* a false-symmetry family.

- **E1 ◐ — `optInt` verbatim dedup done; cross-project `ReaderColumns` deferred.**
  [done 2026-06-04 — extracted the byte-identical `optInt` (`ReadSide.fs:238` ≡
  `:1431`) to a module-level `ReadSide.optIntOf reader`; full solution builds
  0-warning. **Build-verified only** — ReadSide's runtime tests are Docker-gated
  (pool degraded this session). The larger cross-project `ReaderColumns` module
  (sharing `readString`/`readInt`/… with `MetadataSnapshotRunner` in a different
  project) is deferred: it needs a project-structure decision + Docker
  verification, and is anticipatory beyond the one verbatim dup.]
- **E2 ☑ — `CatalogResolution.tryKindByLogical`.** [done 2026-06-04 — new
  `src/Projection.Pipeline/CatalogResolution.fs` with the pure lookup (returns
  `SsKey option`; takes `string×string`, no `Config` dep); rewired both verbatim
  copies (`SpecialCircumstancesBinding`/`EmissionFoldersBinding`) to it, each
  keeping its own error wrapping. Left `kindByPhysicalTable`/`attributeRef` as
  siblings. Build 0-warning; pure pool green.]
- **E3 ⊘ — DEFERRED (would be speculative).** `DiagnosticDocument.buildArtifact`
  is *already* deduplicated (one function, 3 live consumers). Promoting it to a
  Core `Emitter.perKindFolding` combinator now is anticipatory — the other
  emitters (Json/Distributions/SSDT) have different arities and don't need it.
  Building the combinator family ahead of a 4th consumer is exactly the
  abstract-ahead-of-evidence the audit criticized. Defer per IR-grows-under-
  evidence; revisit when a genuine 4th per-kind consumer of the folding shape
  appears.
- **E4 ⊘ — DEFERRED (Docker-gated).** `readGrouped<'K,'T>` kernel for the 4
  `Dictionary<K,ResizeArray<_>>` loops (`:438/469/511/589`) is a behavior-changing
  loop restructure whose only runtime verification is ReadSide's Docker-pool tests
  (degraded this session). Defer until the Docker pool is confirmed healthy so the
  restructure can be test-verified, not just compile-verified.

---

## Tranche 6 — Doc archival + DECISIONS prune (~35% corpus; gated on D1-D3)

- **DA1 ☐ — archive Tier-1 sediment** (~255 KB): `git mv` the 12 zero-inbound-
  reference files (OPEN files whose CLOSE exists + 2 superseded PRESCOPEs +
  `CHAPTER_A_4_7_CLOSE`) → `archive/`. **Re-run the inbound-reference grep before
  every move** (live indexes are stale). Reversible via git.
- **DA2 ☐ — DECISIONS.md prune** (~280 KB of 1.47 MB): session-reflection blocks
  (`2187-2335`, `2687-2820`), forward-signal sub-blocks, test-baseline lines,
  "this session" passages. **DO NOT prune the `19,900-20,688` tail** (live Wave-6
  resolutions). Surgical, diff-reviewed, **last**.
- **DA3 ☐ — single entry point**: canonical = `CLAUDE.md → ONTOLOGY → on-demand`.
  Strip index-claim language from `KICKOFF.md`, `PRODUCT_AXIOMS.md:4`; reword the
  ontology's "this is the index" → "this is the target map; CLAUDE.md is the
  entry."
- **DA4 ⊘ — Tier-2 archive** (~1.39 MB: chapter CLOSEs, `HANDOFF_CHAPTER_*`,
  `WAVE_6_*`, audits, prescopes). **Gated on D1-D3** retargeting the stale
  citations first. Re-run inbound grep per move.

---

## Tranche 7 — Test autophagy

- **T1 ☐ — fixture centralization** (~750 LOC). Promote `mustOk` (66 files),
  `mkName` (47), `mkKey` (31) into the *already-existing* `Fixtures.fs` /
  `IRBuilders.fs`; delete the local copies. No deliberately-divergent variants
  exist. Mechanical, compiler-verified.
- **T2 ☐ — citation-stub thinning** (`AxiomTests.fs`, ~1,568 LOC of `citationOf`
  no-ops + 34 Skip). Lower priority; ideally replace with a generated coverage
  map rather than hand-maintained stubs. Track the Skip count as a CI ratchet
  (cannot increase).
- **T3 ⊘ — V1-parity suite retirement** (~3,696 LOC). **Gated on cutover** — these
  die when V2 becomes the driver, not before. Do NOT delete now.

---

## Tranche 8 — Deferred architectural (own slices; NOT autophagy)

Each is a design change needing its own plan + tests, not a cleanup sweep.

- **AR1 ☐ — `ColumnType` vocabulary VO.** Single-source the facet tuple
  `(Type,Length,Precision,Scale,IsIdentity,SqlStorage,ExternalDatabaseType,Default,Computed)`
  across `Attribute`/`changedFacets`/`applyFacet`/`renderColumn`. Delete the dead
  `SqlStorageType.to/ofPrimitiveType`. **Keep `PhysicalColumn` a distinct quotient**
  (N5) — single-source the *vocabulary*, never the *type*.
- **AR2 ☑ — `ChannelDiff<'facet>`** (CatalogDiff 4-channel collapse, ~250 net
  LOC, per A40). Unlocked by AR1. [UPDATE 2026-07-03 — cashed out: `ChannelDiff<'change>`
  ships at `src/Projection.Core/CatalogDiff.fs:63`, with `AttributeDiff`/`ReferenceDiff`/
  `IndexDiff`/`SequenceDiff` (`:78,116,141,166`) as instantiations. Checkbox was stale;
  also recorded at `CONFIRMED_BACKLOG_2026_06_09.md` row B5.]
- **AR3 ☐ — `Analytics.touchAll` combinator** for the 6-site analytics-pass
  epilogue (~75 LOC).
- **AR4 ☐ — `GraphView`** (forward/reverse/undirected adjacency computed once on
  `TopologicalOrder`, threaded to the 5 graph passes that each rebuild it).
- **AR5 ☑ — `Statement` DU `Merge`/`Update` variants** to bring the Data
  triumvirate onto the typed `seq<Statement>` stream (closes the A35 gap — the
  Data emitters are currently a string-rendered island). [UPDATE 2026-07-03 —
  cashed out: `Merge of MergeBuildArgs` / `Update of UpdateBuildArgs` ship at
  `src/Projection.Targets.SSDT/Statement.fs:308,311`. Checkbox was stale; see
  `DECISIONS.md` "Statement DU MERGE/UPDATE promotion" cash-out, 2026-06-25.]
- **AR6 ☐ — derived `Codec<'a>`** for CatalogCodec (~58/60 pairs) + `writeOnly`
  and `iso`/`legacy` primitives for the asymmetries; `Index.Uniqueness` is the
  irreducible seam. Close the 2 property-test coverage holes (SsKey variants; the
  legacy uniqueness arm).
- **AR7 ☐ — seal the writer leak.** Route the 3 misrouted `*.computed` Info
  traces (`CentralityPass:151`, `BoundedContextPass:170`, `SchemaComplexityPass:132`)
  to `Bench.recordSample`; let those passes drop the always-Info `Diagnostics`
  channel. Keep three sinks (N6).
- **AR8 ☐ — name the emitter-output taxonomy** (keying discipline
  `PerKind`/`Flat`/`Whole` × terminal carrier) as documentation/types — **not a
  closed 3-variant enum** (N4); T11 = PerKind only.

---

## DO-NOT register (adversarially-verified traps)

Each was proposed by a survey-level reading and falsified by a discriminating
input. Recorded so they are not re-proposed.

- **N1 — CatalogReader JSON↔rowset record-builder fusion.** Identity derivation
  forks (synthesize-from-name quotient vs native-GUID intent); fusing relocates
  the fork. Reclaim ~0.
- **N2 — ReadSide↔EvidenceCache unification.** Schema-leg (catalog readback) vs
  data-leg (statistical profiling); shared syntax, different function.
- **N3 — Fk/Joint `DiagnosticRule` registry.** Inverted duals (`total/distinct`
  below-threshold vs `distinct/total` above-threshold); a hardcoded comparator
  silently miscompiles one.
- **N4 — Closing the emitter-output taxonomy as a 3-variant enum.** It is
  open-ended (`byte[]`/`Manifest`/`DockerImageContext`); a closed enum would lie.
- **N5 — Collapsing `PhysicalColumn` into `Attribute`.** `PhysicalColumn.Type`'s
  coarseness *is* the canary's equivalence relation (BIGINT→INT invisible by
  design); fusion blinds or breaks the canary.
- **N6 — Fusing the three writer sinks into one `Witness`.** `Bench` is an impure
  lock-protected statistical sink with no value channel and a separate consumer.
- **N7 — Merging `MigrationRun.executeWithDataAndRecord`** into a generic
  `andRecord`. CDC pollutes the read-back, so it uses a *different* verification
  gate (not orthogonal).
- **N8 — "one Binding abstraction."** 6 binders, 3 config shapes, 2 unique
  behaviors (`InsertionPolicy` degenerate; `TransformGroups` opt-in default-flip).

---

## Execution order (the critical path)

1. **D1-D3** (stale-claim doc fixes) — unblocks DA4.
2. **C1-C2** (correctness bugs) — small, verifiable, high-value.
3. **R1-R2** (god-file decompositions) — 0-LOC, navigability, low risk.
4. **E1-E4** (true small extractions) — ~100 LOC, compiler-verified.
5. **X0→X1→X2** (dead-algebra deletion) — largest Core reclaim, governance-gated
   by the X0 amendment.
6. **DA1-DA3** then **DA4** (doc archival + prune).
7. **T1** (fixture centralization).
8. **AR1-AR8** (architectural) — each its own planned slice, on the Wave-6 cadence.

Every code item verifies against `scripts/test.sh fast` (pure pool) at minimum;
R2/decomposition + adapter items also against `scripts/test.sh docker`.
