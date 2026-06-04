# AUDIT 2026-06-04 (Wave 2) — Adversarial Periphery Review & Reclaim Correction

**Date:** 2026-06-04
**Status:** Findings only; no source modified.
**Relationship to prior docs:** This is the adversarial hardening of
`AUDIT_2026_06_04_BLINDSPOT_COMPRESSION.md` (wave 1, the survey) and
`CRYSTALLINE_FORM.md` (the vision). It **corrects both downward** on reclaim.
Where wave 1 sized periphery duplication as large, wave 2 handed each proposed
collapse to an adversarial agent tasked with *falsifying* it — and most
collapses shrank, inverted, or revealed themselves as false-symmetry traps.
**Method:** Six probes — five on the periphery seams (adapters, pipeline,
targets, tests, docs) and one steelman of the "dead" speculative algebra. Each
applied the governing principle established by wave 1's IR/writer probes:
**shared vocabulary ≠ shared type; find the member or input that resists the
collapse.**

---

## Contents

- [0. The headline correction](#0-the-headline-correction)
- [1. The meta-finding](#1-the-meta-finding-about-auditing-itself)
- [2. Corrected reclaim ledger](#2-corrected-reclaim-ledger)
- [3. Adapters periphery — verdicts](#3-adapters-periphery--verdicts)
- [4. Pipeline periphery — verdicts](#4-pipeline-periphery--verdicts)
- [5. Targets periphery — verdicts](#5-targets-periphery--verdicts)
- [6. Test suite — proof-migration thesis](#6-test-suite--proof-migration-thesis)
- [7. Documentation — executable plan](#7-documentation--executable-plan)
- [8. The dead algebra — trajectory steelman](#8-the-dead-algebra--trajectory-steelman)
- [9. Corrections to the prior two docs](#9-corrections-to-the-prior-two-docs)
- [10. Revised reachability path](#10-revised-reachability-path)

---

## 0. The headline correction

Wave 1 read the periphery as a large duplication mine (`*Binding`/`*Run`/
`*Diagnostics` families; 6-site emitter walks; 15 reader loops; 3×-Core test
bulk). Wave 2's adversarial depth shows that **the periphery is mostly false
symmetry too** — the same Intent-vs-Quotient cleavage that protected
`PhysicalColumn` from fusion in wave 1 recurs everywhere:

- The CatalogReader JSON↔rowset "parallel paths" diverge on **identity
  derivation** (synthesize-from-name quotient vs native-GUID intent) — the exact
  cleavage as `Attribute` vs `PhysicalColumn`. Fusing them relocates the fork.
- The `*Binding` family has **3 config shapes + 2 unique behaviors**; there is no
  "one Binding abstraction," only one verbatim-duplicated resolver.
- `FkSelectivity` + `JointDependency` are **inverted duals**, not twins; a shared
  registry hardcoding one comparator silently miscompiles the other.
- `Emitter.perKind` fits **4 of 6 sites across 3 different arities**; the 3-shape
  output taxonomy is actually **open-ended**.
- The "3× Core is convention-debt" test thesis is **only ~13% true**; the
  typed-AST migration it proposed is **largely already done**.
- The "dead algebra" was **not a worthwhile down-payment**: the two surfaces
  whose consumers shipped both **declined the prebuilt structure on arrival**.

The one place the reclaim is genuinely large and real is the **prose corpus**
(~35% compressible) — because prose redundancy is real redundancy, not principled
divergence — but even there, stale live indexes gate blind archival.

---

## 1. The meta-finding (about auditing itself)

> **Survey-level audits systematically overestimate compressibility, because
> shared vocabulary reads as shared semantics.** Across both waves, *every* bold
> reclaim number — the IR fusion, the writer fusion, the binder/codec/perKind
> collapses, the test-migration thesis, the dead-algebra down-payment — shrank,
> inverted, or revealed itself as a trap under discriminating-input testing. The
> codebase is far more crystalline than any survey can show; its engineers
> already did the divergence-vs-duplication work. Only adversarial depth — "find
> the input on which the collapse gives the wrong answer" — distinguishes true
> duplication from principled divergence.

This is itself the strongest argument for the codebase's own
**"right-by-function, not by name"** discipline (`WAVE_6_ONTOLOGY.md`): a
plausibly-named-but-wrong collapse is caught only by the discriminating predicate,
never by the shared surface. The wins that survive are: **god-file decomposition,
naming the distinctions, deleting genuinely-dead algebra + doc sediment + a few
test-debt clusters, and fixing the one real correctness bug + the stale claims.**

---

## 2. Corrected reclaim ledger

Net LOC, conservative, after adversarial bounding. "Trap" = do not attempt.

| Region | Move | Net reclaim | Note |
|---|---|---|---|
| **Core** | delete dead algebra (Certificate 109 + DiagnosticLattice 113 + Prism 102 + PassContext 93 + LineageTree 294 + product/`&&&` 35), keep `subsumes` ~30 | **~−716** | the single defensible large delete (grep-verified zero callers) |
| Core | `ChannelDiff<'facet>` (CatalogDiff 4-channel) | ~−250 | wave-1 "~400" overstated |
| Core | analytics-pass epilogue → combinator | ~−75 | real |
| Core | SchemaComplexity empty-topology fix | ~0 | **correctness, not LOC** |
| Adapters | `ReaderColumns` helper + dedup `optInt` | ~−25 to −35 | the only true verbatim dup |
| Adapters | `readGrouped<'K,'T>` for 4 dict-accum loops | ~−30 to −40 | ReadSide-internal |
| Adapters | `readResultSet` for ~5 flat loops | ~−25 to −35 | cross-project; needs shared module |
| Adapters | CatalogReader JSON↔rowset fusion | **TRAP (0)** | identity-derivation fork |
| Adapters | ReadSide↔EvidenceCache unify | **TRAP (0)** | schema-leg vs data-leg |
| Adapters | CatalogReader decompose 2518→5 files | 0 (relocation) | **navigability win** |
| Pipeline | `CatalogResolution.kindByLogical` dedup | ~−20 | the only verbatim dup |
| Pipeline | `execute` post-combinators (5 of 6) | ~−15 | `executeWithDataAndRecord` resists |
| Pipeline | Deploy.fs 5-module split | 0 (relocation) | **navigability win** |
| Pipeline | `DiagnosticRule` registry | **TRAP (~−10, risky)** | Fk/Joint are inverted duals |
| Targets | `Emitter.perKind` family (fits 4/6, 3 arities) | ~−10 to −12 | the win is type-honesty |
| Targets | `buildArtifact` consolidation (3 live consumers) | small | the one *proven* collapse |
| Targets | close the 3-shape taxonomy as an enum | **TRAP (0)** | taxonomy is open-ended |
| Tests | delete dead-algebra law tests | ~−2,300 to −2,800 | follows the Core delete |
| Tests | fixture centralization (`mustOk`×66 etc.) | ~−750 | built-but-bypassed `Fixtures.fs` |
| Tests | citation-stub thinning (`AxiomTests`) | ~−1,500 | documentation-as-test |
| Tests | V1-parity suite (dies at cutover) | ~−3,700 | transitional, not now |
| Docs | archive sediment (immediately safe) | ~−255 KB | 12 zero-ref files |
| Docs | DECISIONS narrative prune | ~−280 KB | leave live Wave-6 tail |
| Docs | full archival after citation fixes | ~−2.0 MB total (~35%) | gated on stale-index repair |

**Honest totals:** Core code ~−1,050 LOC (~5%, mostly the dead-algebra delete);
periphery code ~−150 to −250 LOC + two god-file decompositions (navigability, not
deletion); tests ~−8,500 LOC deletable-soon (~13%) of which ~−5,750 is dead-algebra
+ fixtures + citation stubs; docs ~35%. **The codebase is not a third smaller. The
genuine wins are concentrated, not pervasive.**

---

## 3. Adapters periphery — verdicts

**Claim — CatalogReader JSON↔rowset fuse to one record-builder: FALSE-SYMMETRY
TRAP.** The discriminating member is **identity derivation**: `attributeSsKey`
(JSON, `CatalogReader.fs:498`) *always synthesizes* SsKey from names
(`Synthesized("OS_ATTR", …)`, `:922`) — a rename-lossy quotient, because
`osm_model.json` carries no SsKey (`:464-473`). `attributeSsKeyFromRow` (rowset,
`:1718`) *prefers native GUID* (`match row.AttrSsKey with Some g -> SsKey.ossysOriginal g | None -> …`,
`:1723-1725`). Same fork at `moduleSsKeyFromRow`/`kindSsKeyFromRow`. Reinforcing:
the two paths populate *different field subsets* — JSON gets `DefaultValue` but
rowset always `None` (`:1769`); rowset gets `DefaultName`/`Computed`/`SystemOwned`
modality/three-way `Origin` that JSON cannot derive. The shared **leaf
translation** (`resolveAttributeType`, `parseDeleteRule`, the 8 `*SsKey`
synthesizers) is *already* extracted and shared; the **record assembly** must stay
forked. Fusing adds parameter-threading, reclaims ~0.

**Claim — ReadSide 15 loops → one `readResultSet<'T>` kernel: PARTIAL (~9 of
14).** The discriminating resister is `readSchemaCombined` (`ReadSide.fs:1357`): a
single reader walked through **6 result sets** via `NextResultAsync`, where RS5
does row-discriminated routing into **two heterogeneously-typed dictionaries** on
a NULL test (`:1531-1535`) and returns a 7-tuple. No `'T` produces that. Also
resisting: 4 grouped-accumulation loops (want a *different* `readGrouped<'K,'T>`
kernel), the filter-and-skip-with-diagnostic FK loop (`:613`, needs
`'T option` + reject channel), and `readRowsStream` (`:~860`, stateful streaming,
ordinal-coupled, dynamic-SQL, own lifecycle — the materialize-vs-stream
antithesis). Real: a `ReaderColumns` helper + a `readGrouped` kernel, ~80-110 LOC
across both.

**Claim — ReadSide + LiveProfiler duplicate scaffolding EvidenceCache should
unify: FALSE-SYMMETRY TRAP (Lineage-vs-Bench analog).** ReadSide reads the
**schema catalog** (`INFORMATION_SCHEMA`, `sys.*`) → reconstructs a `Catalog`;
LiveProfiler reads the **user data** (`SELECT *`, `COUNT_BIG`) with per-Kind
IR-built SQL → produces a `Profile`. These are T16's two projections (schema leg
vs data leg). `EvidenceCache` already unifies the part that should be unified —
and is LiveProfiler-only *by design* (it holds `CachedValue array` row-data;
ReadSide never reads row-data except `readRowsStream`, which is the
Transfer/PhysicalSchema path, correctly routed elsewhere). Forcing ReadSide's
`sys.*` reads through a data-value cache is the misclassification.

**Decomposition (safe, valuable):** CatalogReader 2518 → 5 files in compile order:
`OssysRowsetTypes.fs` (~344, pure DTOs) → `OssysTranslation.fs` (~257, the shared
leaf layer) → `OssysJsonReader.fs` (~791) → `OssysRowsetReader.fs` (~728) →
`CatalogReader.fs` (~80, public `parse`). Ship types + translation first (zero
behavioral risk); JSON and rowset readers are independent. **Must NOT merge the
two readers.**

**Single best move:** extract `ReaderColumns` (`readString`/`readInt`/`readBool`/
`readGuidOpt` + the twice-duplicated `optInt` at `ReadSide.fs:238` ≡ `:1431`) —
the only true shared-type-and-function dup in the region.

**Positive findings:** no SQL-injection surface (identifiers via
`Identifier.EncodeIdentifier`, values via `SqlParameter`); the three ingestion
paths DO converge correctly (OssysSql reuses CatalogReader's rowset core via
`parseRowsetBundle`, not a third IR builder).

---

## 4. Pipeline periphery — verdicts

**Claim — 6 `*Binding` → one Binding abstraction: FALSE (4-of-6 on resolution
only).** The binders split 3 ways: config shape (`list` / `option` / single
`string`), catalog dependency (3 use it, 3 don't), overlay axis. Genuine
resisters: **InsertionPolicyBinding** (`:40-61`, degenerate `string→DU`, no
catalog/list/aggregate) and **TransformGroupsBinding** (`:139-152`, unique opt-in
default-flip where `UserReflow` is off-by-default). The only verbatim duplication
is `resolveKindByLogical`, copied between `SpecialCircumstancesBinding.fs:59` and
`EmissionFoldersBinding.fs:146` (the latter's docstring admits the mirror). Extract
that one (~20 LOC, 2 consumers). `kindByPhysicalTable` and the 3-part
`attributeRef` are *different* functions — siblings, not one parameterized
resolver.

**Claim — 4 `*Diagnostics` → registry: 2-of-4, and the 2 are inverted duals.**
`FkSelectivity` (`mean = total/distinct`, fires **below** threshold, keyed by
`ReferenceKey`) and `JointDependency` (`ratio = distinct/total`, fires **above**,
keyed by `KindKey`) are *reciprocal mirror-images*, not identical. A registry
hardcoding one comparison direction silently miscompiles the other. `InactiveAttribute`
(boolean-flag filter, no threshold, Warning not Info) and `SpecialCircumstances`
(scans `ComposeState`/`Catalog`, carries acceptance-annotation overlay) both
resist. **Registry = ~10 LOC saved, real miscompile risk → skip.**

**Claim — MigrationRun 2^N `executeXxx`: 5-of-6 collapse (count is 6, not 8).**
4 are clean orthogonal combinators over `execute` (`measureCdc`/`record`/`fromLive`/
`withData`). The resister is `executeWithDataAndRecord` (`:607-646`): a CDC-tracked
sink puts `cdc.*` objects in the read-back, **confounding** the schema-leg
`Verified` round-trip (`:598-603`), so it deliberately gates on a *different*
predicate than `executeWithData` (`:574`). Two distinct verification predicates →
not factorable into a generic `andRecord`. Leave it hand-written with a comment.

**Claim — Deploy.fs god-object via shared `parallelismCache`: split is real, the
coupling claim is FALSE.** `parallelismCache` (`:459`) is `private`, keyed by
connection string, touched only by `resolveParallelism` — a memoization leaf
*inside* the Parallelism cluster, not a shared spine. The 5 clusters couple by
call-direction (a clean DAG), so the split into `Deploy.{Container,Connection,
Execution,Parallelism,Canary}` is mechanical, 0-LOC relocation.

**Single best move:** extract `CatalogResolution.kindByLogical`. **Most dangerous
trap:** the Fk/Joint "identical rules" registry.

---

## 5. Targets periphery — verdicts

**Claim — `Emitter.perKind` collapses 6 sites: REFUTED as one combinator (fits 4,
across 3 arities).** `Types.fs:50-64` already defines 3 aliases (`Emitter`,
`EmitterWithProfile`, `EmitterOverDiff`). Walk: JsonEmitter (`:182`) fits canonically;
DistributionsEmitter (`:213`) needs `perKindWithProfile`; **RefactorLogEmitter
(`:318`) keys over the diff *target* catalog, not source** — needs `perKindOverDiff`;
DecisionLogEmitter (`:122`) is *already* the shared `DiagnosticDocument.buildArtifact`
(3 consumers); SSDT (`:832`) + the 3 real Data emitters want a *lookup* variant
(`Kind -> 'e option` from a side Map). So perKind is a **3-member family keyed by
the Emitter alias**, not one function. Real reclaim ~10-12 LOC; the value is
type-honesty.

**Claim — 3-shape closed taxonomy: REFUTED, it is open-ended (6+).** Beyond
`ArtifactByKind<'e>` (per-kind, where T11 lives), `SchemaMigrationEmitter` returns
`Diagnostics<Statement list>`, `DacpacEmitter` `Result<byte[]>`, `DockerImageEmitter`
`Result<DockerImageContext>`, `ManifestEmitter` `Manifest`. The honest description
is **two orthogonal axes** — keying discipline (`PerKind`/`Flat`/`Whole`, where
T11 = PerKind only) × terminal carrier (8 distinct) — *not* a closed 3-variant
enum (which would force `byte[]`/`Manifest`/`DockerImageContext` into a bucket they
don't share).

**Claim — typed-AST gap is just StaticSeeds' UPDATE: SHARPENED, far deeper.** The
`Statement` DU (`Statement.fs:269-391`, 26 variants) has **no `Merge` and no
`Update`**. All three Data emitters produce `DataInsertScript` whose SQL is
materialized only as **strings** (`RenderedPhase1`/`Phase2` via ScriptDom-render +
`String.Concat`). So A35 ("Π's output is a typed `seq<Statement>`") **does not hold
for any data-movement output** — the whole Data triumvirate is a string-rendered
island bridged to the stream only by text concatenation. Closing it requires
adding `Merge of TableId * MergeSpec` + `Update of TableId * UpdateSpec` to the DU
(an architectural slice, not a quick fix).

**Single best move:** consolidate the already-proven `buildArtifact` triplet (3
live consumers) into a named `Emitter.perKindFolding` combinator; let the other
arity variants follow as evidence accrues. **Do NOT** close the taxonomy enum or
sweep perKind onto RefactorLog/SSDT/Data at once.

---

## 6. Test suite — proof-migration thesis

**Thesis ("3× Core is convention-debt; migrate proofs to types"): PARTLY TRUE,
and the mechanisms are largely already executed or mis-aimed.**

- "3×" uses the smallest denominator; tests are **1.19× all production** (53,083
  LOC), not 3× the whole engine.
- **~39% hard-irreducible:** I/O canary ~9,600 LOC (15% — byte-determinism /
  CDC-silence are theorems about a running DB), genuinely combinatorial ~14,000
  (22% — 164 FsCheck properties + decision-tables like `ForeignKeyRulesTests`
  where each case pins a distinct outcome variant), byte-exact wire golden ~1,000
  (2%).
- **The typed-AST migration is mostly already done:** the big emitter tests
  (`SsdtDdlEmitterTests` 1202 LOC, `StaticSeedsEmitterTests` 658) have **zero
  triple-quotes** — they already assert via `Assert.Contains`. Most remaining
  triple-quotes are **input fixtures**, not output golden (a *different* refactor:
  parse-mutate-reserialize).
- **The floor-proof (must-stay golden):** `SqlLiteralTests` asserts
  `N'O''Brien'` — the terminal AST→bytes boundary where the escaping *is* the
  T-SQL wire spec. A typed-AST comparison would be circular (`toString x =
  toString x`). Golden tests cannot go to zero.
- **Fixture centralization is built but bypassed:** `Fixtures.fs` (276 LOC)
  already exposes `testKey`/`kindKey`/`sampleCatalog`, yet `mustOk` is
  re-declared in **66 files**, `mkName` in 47, `mkKey` in 31 — ordinary
  copy-paste drift, ~750 LOC, ~1.2%. No deliberately-divergent variants found.
- **Deletable-tomorrow convention-debt ≈ 13%:** citation stubs (`AxiomTests.fs`,
  126 `citationOf` no-ops + 34 Skip, ~1,568 LOC), dead-algebra law tests
  (~2,300-2,800), V1-parity (3,696, dies at cutover), fixture dup (~750).

Verdict: the suite shrinks **13-25% realistically**, not "toward Core." The claim
"tests carry the proof the types don't" overstates it — the largest reducible
chunks are citation-as-test documentation, transitional parity, and copy-paste
fixtures: *ordinary debt, not deep type-vs-test asymmetry.*

---

## 7. Documentation — executable plan

The one region where reclaim is large and real (~35% of 5.7 MB), because prose
redundancy *is* redundancy. **Caveat:** the corpus's live indexes (`CLAUDE.md`,
`KICKOFF.md`) are **stale** (describe the chapter-3 frontier; real frontier is
Wave-6/chapter-D), so much "obvious sediment" is still *cited as load-bearing* —
blind `git mv` is unsafe.

**Artifact 1 — Archive manifest.** Immediately-safe (zero inbound refs): 12 files
/ 261,282 bytes (the OPEN files whose CLOSE exists + 2 superseded PRESCOPEs +
`CHAPTER_A_4_7_CLOSE`). Tier-2 (~1.39 MB: chapter CLOSEs, `HANDOFF_CHAPTER_*`,
`WAVE_6_*`, audits, prescopes) requires retargeting the citing doc first.
**Re-run the inbound-reference grep before every move.**

**Artifact 2 — DECISIONS.md prune (~280 KB of 1.47 MB).** 264 session headers, 37
"forward signals" blocks, 52 test-baseline lines, 17 "this session" passages.
Concrete prunable ranges include `2187-2335` ("Session 10 reflection") and
`2687-2820` ("Session 11 reflection") = ~26 KB alone. **Do not prune the
19,900-20,688 tail** (live Wave-6 Lifecycle/migrate resolutions). Surgical,
diff-reviewed, last.

**Artifact 3 — Stale-claim fixes (do FIRST; pure correctness; unblocks archival):**
- *"zero production callers"* for diff/`Lifecycle` — now FALSE. Fix `CLAUDE.md:98`
  and `DECISIONS.md:20369`; point to the resolution at `DECISIONS.md:20049/20464`
  and `EXECUTION_PLAN.md:775`. **Do NOT edit `CRYSTALLINE_FORM.md:184`** — its
  "zero production callers" refers to `Prism`/`PassContext`/`LineageTree`/
  `DiagnosticLattice`, which *are* grep-verified def-only (true claim; different
  referent).
- *Axiom count:* source of truth `AXIOMS.md` = **A43 / T16**, but `AXIOMS.md:24`
  self-states "A1-A42" (fix it first), then propagate to `README.md:111/627/645`
  ("A40"), `PRODUCT_AXIOMS.md:474` ("A40"), `CLAUDE.md:283/287` ("A41"/"A40").
- *Test counts:* `882/790/631/588/1128` are all stale snapshots; actual ~2,729
  `[<Fact>]`/`[<Theory>]`. Ban absolute counts from live nav docs (`KICKOFF`,
  `CLAUDE:460`, `STAGING:112`, `PLAYBOOK:602`, `VISION_REVIEW:230`,
  `V2_PRODUCTION_CUTOVER:1128`, `V2_PATTERNS_COMPENDIUM:392`); cite "see test run."

**Artifact 4 — Single entry point.** Three docs self-declare as "the index"
(`CLAUDE.md:20`, `THE_USE_CASE_ONTOLOGY.md:1156/1072/823`, `KICKOFF.md`); 30 of 105
files carry "read-first" language. Canonical = **CLAUDE.md → ONTOLOGY → on-demand**;
strip the index-claim from `KICKOFF.md`, `PRODUCT_AXIOMS.md:4`, and reword the
ontology's "this is the index" → "this is the target map; CLAUDE.md is the entry."

**Immediately-executable reclaim: ~535 KB (Artifact 1 Tier-1 + Artifact 2
conservative). Full program ~2.0 MB (~35%)** once Artifact 3 unlocks Tier-2.

---

## 8. The dead algebra — trajectory steelman

All seven surfaces date from one 2026-05-22 burst, each justified by a *symmetry
argument* ("completes the duo/trinity/Kleisli category"). The steelman asked:
where was each headed, and would the stated future consumer actually use it
as-built? **The decisive evidence: the two surfaces whose consumers shipped or
were named both refused the down-payment on arrival.**

| Surface | Stated consumer | Roadmap | Verdict |
|---|---|---|---|
| **`LineageTree`** (294 LOC, H-005) | policy-diff (H-033/H-035, Cluster C) | **SHIPPED** (`osm diff-policy`) | **DELETE.** `PolicyDiff.diffFullProjection` (`:187-207`) runs the chain **twice into two flat `Lineage` carriers + `SsKey` join** — walked past the branching writer, because it wants *independent* runs (different policy → different chain → no shared prefix) + a per-`SsKey` outer join the free monad doesn't provide. |
| **`Prism`** (102 LOC, H-010) | Catalog↔DDL round-trip (H-058/H-093) | **proposed; already served** | **DELETE.** The round-trip *runs in production* (canary deploy→readback) but over **`PhysicalSchema`** (lossy quotient) with a `Tolerance` taxonomy, not symmetric `Get`/`ReverseGet` on `Catalog`. `ReverseGet(Get a) ≠ a` structurally; the "Prism law" widens into the tolerance diff that already exists. Category-error rename. |
| **`Certificate`** (109 LOC, H-004) | multi-target fanout (H-009) | proposed | **DELETE.** Admitted structural iso with `Lineage<Diagnostics<'a>>`; pure nominal sugar; rebuild the 10-LOC record when H-009 lands. |
| **`DiagnosticLattice`** (113 LOC, H-008) | operator triage / `diagnose` verb | no verb exists | **DELETE-AND-REBUILD.** The **one surface with real domain content** — the ~30-LOC `subsumes` rule (dot-separated-`Code` subsumption) is worth keeping as a design note. The real consumer will likely want *rollup-with-counts*, not antichain-deletion; drop `Precedes`/`relations`/`minimal`. |
| **`PassContext`** (93 LOC, H-062) | deep-pass env threading | "TBD" | **DELETE.** Its own trigger admits "F# parameter-passing is already fine at the codebase's current depth"; positional params + registry policy-closure serve the need. |
| **`Pass.product`/`&&&`/`first`/`second`** (35 LOC) | parallel composition (H-006), fanout (H-009) | static shipped, dynamic deferred | **DELETE-AND-REBUILD `product`** (needs the deferred SsKey-disjointness check built *with* it; the built `product` is sequential anyway). **DELETE `first`/`second`** (pure arrow-notation completionism, no path to a consumer). |
| **dead lenses** `sequences`/`indexesOf` | none (self-flagged) | none | **DELETE.** ~5 LOC each, recreate on demand. |

**Overall judgment: net-negative as shipped, with the R&D dividend already banked
in prose.** The exercise mapped the design space (the writer-trinity / Kleisli /
optics articulation is now load-bearing *documentation*), but the *code* adds
~550-746 LOC carrying cost + ~2,300-2,800 test LOC + a standing false-symmetry
temptation (the 2026-06-04 audit's central thesis). A down-payment the consumer
declines on arrival is stranded inventory. **Keep only `DiagnosticLattice.subsumes`
(~30 LOC) as a documented design note; quarantine the rest behind the existing
defer-with-trigger for one chapter as a courtesy, then delete at the next close.**

---

## 9. Corrections to the prior two docs

- **`AUDIT_2026_06_04_BLINDSPOT_COMPRESSION.md`** — its Tier-2 periphery reclaim
  (binder/diagnostic/run families, ReadSide kernel, CatalogReader fusion) is
  **overstated**. The families mostly do not collapse (false symmetry); real
  periphery reclaim is ~150-250 LOC + two god-file decompositions. Its "CatalogDiff
  ~400 LOC" is ~250 net. Its framing and correctness findings (SchemaComplexity
  bug, dead algebra, stale "zero callers") stand.
- **`CRYSTALLINE_FORM.md`** — §5's "test suite is 3× Core because invariants are
  convention-enforced" is **overstated**: ~39% irreducible, the typed-AST migration
  largely already done, deletable convention-debt ~13%. Its Intent-vs-Quotient
  spine, its do/do-not collapse table, and its "delete dead algebra" recommendation
  are *strengthened* by this wave (the algebra trajectory confirms the deletion;
  the periphery confirms the false-symmetry principle). §5's headline should read:
  *Core is ~95% crystalline; periphery code reclaim is small; the real wins are
  decomposition, dead-algebra deletion, ~13% test-debt, and ~35% doc compression.*
- **One claim NOT to "fix":** `CRYSTALLINE_FORM.md:184`'s "zero production callers"
  is correct (refers to the def-only algebra surfaces). The stale claim is
  `CLAUDE.md:98`'s, about the diff/`Lifecycle` machinery (which IS wired in).

---

## 10. Revised reachability path

Ordered by defensibility × value, cheapest-safest first. Numbers are the corrected
(smaller) ones.

1. **Stale-doc-claim fixes** (Artifact 3) — pure correctness, reversible, unblocks
   archival. The "zero callers" / axiom-count / test-count drifts.
2. **Fix the SchemaComplexity empty-topology bug + `pickLabel` self-comparison** —
   correctness, self-contained, verifiable against the suite.
3. **Delete the dead algebra** (Core ~−716 LOC + tests ~−2,500) keeping only
   `subsumes` — the largest defensible delete; ends the false-symmetry temptation
   at its source.
4. **Two god-file decompositions** (CatalogReader 2518→5; Deploy.fs →5 modules) —
   0-LOC relocation, pure navigability, no semantic risk; **must not merge the
   forked readers**.
5. **The three true small extractions** — `ReaderColumns`, `CatalogResolution.kindByLogical`,
   `buildArtifact`→`Emitter.perKindFolding` — each a verbatim-dup or proven-consumer
   collapse (~100 LOC total).
6. **Doc archival** (Artifact 1 Tier-1, then Tier-2 after citation fixes) + DECISIONS
   prune (Artifact 2, surgical, last).
7. **Fixture centralization** (~−750 test LOC) — mechanical, adopt the existing
   `Fixtures.fs`.
8. **Deferred / architectural** (not autophagy): `ColumnType` vocabulary VO +
   `ChannelDiff`; the `Statement` DU `Merge`/`Update` variants to close the Data
   typed-AST island; the analytics-pass combinator + `GraphView` threading.

**Do NOT attempt:** CatalogReader JSON↔rowset fusion; ReadSide↔EvidenceCache
unify; the Fk/Joint diagnostic registry; closing the emitter-output taxonomy as a
3-variant enum; collapsing `PhysicalColumn` into `Attribute`; fusing the writer
sinks. Each is a verified false-symmetry trap.
