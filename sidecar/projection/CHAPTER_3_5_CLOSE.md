# Chapter 3.5 close — synthesis (joint with chapter-3 cross-cutting close)

This document is the chapter-3.5 close synthesis, **landed retroactively as part of the chapter-3 cross-cutting close arc** (2026-05-10). Chapter 3.5's substantive deliverables shipped across sessions 37–40+ and were *partially* absorbed into the bundled `CHAPTER_4_1_A_CLOSE.md` (which covered chapter 3.6 / 3.7 / 4.1.A / 4.1.B-α/β/γ / RawTextEmitter retirement / Tier 1/2/3 transitions). The chapter-3 cross-cutting close audit (2026-05-10) flagged that 3.5 was unaddressed; this stub closes the gap.

**Status:** **closed** (joint chapter-3 close arc). Companion files: `CHAPTER_3_5_OPEN.md` (the chapter-open scaffold; slice α-row's file targets retired with the RawTextEmitter retirement — see §"Retired references" below); `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md` (the pre-scope); `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md` (the substantive deliverable pre-scope).

## Chapter 3.5 arc — what shipped

Per the chapter-open document's slice arc (lines 49-55), refined under empirical pressure across sessions 37–40+:

| Slice | Original target | Status | Notes |
|-------|-----------------|--------|-------|
| **α** | `RawTextEmitter.emitSlices : Emitter<Statement list>` + `Render.flattenSlices` | **Shipped** — initial Π port realization, since superseded | RawTextEmitter retired in chapter 4.1.A close arc; SSDT DDL emission moved to `SsdtDdlEmitter.statements` |
| **β** | `JsonEmitter.emitSlices : Emitter<string>` (per-kind JSON text) | **Shipped + advanced at chapter 3.7 slice ε** to `Emitter<JsonNode>` | Typed per-kind value; chapter-3.7 sidebar Tier-1 #7 cash-out |
| **γ** | `DistributionsEmitter.emitSlices : EmitterWithProfile<string>` | **Shipped + advanced at chapter 3.7 slice ε** to `EmitterWithProfile<JsonNode>` | Same lift as Json sibling |
| **δ** | T11 type-theorem property tests; retire substring T11 enforcement | **Shipped** | T11 is now structural via `ArtifactByKind<'element>` |
| **ε** | Codification | **Shipped** — see "Cashed amendments" below |
| **θ–ι** | `CatalogDiff.between` + `RefactorLogEmitter` + `Render.toRefactorLogXml` | **Shipped** (chapter 3.5 substantive deliverable per pre-scope) | First diff-typed sibling Π |

## What chapter 3.5 cashed in canonical surfaces

**AXIOMS amendments** (per the chapter-open §"Operating disciplines re-stated"):

- **T11 amended (structural-type encoding)** — body at `AXIOMS.md:716`. Π port realization makes T11 structural via `ArtifactByKind<'element>`. Sibling-Π commutativity stops being a substring-search property; becomes a structural consequence of construction.
- **T11 amended again (diff-typed inputs)** — body at `AXIOMS.md:796`. Chapter 3.5's diff-typed fourth sibling (`RefactorLogEmitter` over `CatalogDiff`) extends T11 to operate over diffs. Same operational shape — diff-typed Π over exhaustively-partitioned diff inputs.
- **A38 — `CatalogDiff` exhaustiveness** — body at `AXIOMS.md:1001`. Promoted from candidate to A38 at chapter 3.5 close. The diff IR exhausts the variants the catalog admits.

These three amendments were correctly bodied at the chapter close (2026-05-09 per the bodied-section dates), but the "Still scheduled (TBD)" placeholder list at `AXIOMS.md:1192` was not updated to move them out of the scheduled list. **Fixed at chapter-3 cross-cutting close (2026-05-10)** — see commit accompanying this document.

**Strategic-frame axes 1–8** (chapter-open §"Strategic frame") all held; no axis surfaced a divergence requiring chapter-close amendment.

**Operating disciplines re-stated** (chapter-open):
- Closed-DU expansion empirical test — confirmed (T11's expansion to diff-typed inputs lit up zero compile errors at non-RefactorLog consumers).
- Two-consumer threshold — held (per-element types remained `string` for Json/Distributions until chapter-3.7 slice ε's `JsonNode` lift surfaced consumer pressure).
- AXIOMS amendments scaffolded at chapter open; bodies filled at chapter close — operated; bodies landed at 3.5 close; only the cross-doc currency was missed.

## Retired references (chapter-open §"Slice arc")

The chapter-open document's slice α-row (line 51) names `RawTextEmitter.fs` and `RawTextEmitterTests.fs` as file targets. **Both retired in chapter 4.1.A close arc** (commit `197b9e7` per the audit). The SSDT DDL surface migrated to:

- `Projection.Targets.SSDT.SsdtDdlEmitter.fs` (active emitter)
- `Projection.Targets.SSDT.SsdtDdlEmitter.statements` (typed-stream realization)

Chapter 3.5's slice α work landed-and-was-superseded within a single chapter-3 arc; the substantive contribution (Π port realization via `ArtifactByKind`) survives in `SsdtDdlEmitter` and the Json / Distributions emitters.

## What carries forward from chapter 3.5

- **`ArtifactByKind<'element>` as the canonical per-kind structured output** — keystone for chapter 4.4 drift detection (pointwise `Map<SsKey, DriftKind>` diff).
- **Diff-typed Π pattern** — `EmitterOverDiff<'element>` shape inhabited by `RefactorLogEmitter`; chapter-4.x deploy-script emitters inherit the seam.
- **T11 as a type theorem** — substring-discipline T11 enforcement retired across emitter tests; structural enforcement via `ArtifactByKind.create` is now the canonical pattern.

## Chapter-close ritual disposition

The eight-item ritual (`DECISIONS 2026-05-14`) for chapter 3.5 was **deferred at the chapter's substantive close** and **discharged jointly with chapters 3.6 / 3.7 / 4.1.A / 4.1.B-in-flight** at `CHAPTER_4_1_A_CLOSE.md`'s chapter-close-ritual execution. Three items not covered by that joint pass landed at the chapter-3 cross-cutting close (2026-05-10):

1. **AXIOMS scheduled-amendments list update** — T11×2 + A38 moved from "Still scheduled" to "Cashed at chapter 3.5 close". Three bodies were correctly written in-document; only the meta-list was stale.
2. **This close synthesis (`CHAPTER_3_5_CLOSE.md`)** — companion to the chapter-open document; closes the symmetry gap.
3. **Cross-document staleness sweep** — see the accompanying commit's full sweep across CLAUDE.md / AXIOMS.md / README.md / DECISIONS.md.

## Closing

Chapter 3.5 delivered the Π port realization (T11 as type theorem) and the diff-typed fourth sibling (`RefactorLogEmitter` over `CatalogDiff`). The substantive contributions were absorbed forward (slice α's RawTextEmitter survived in `SsdtDdlEmitter`; slices β/γ advanced under chapter-3.7 slice ε to typed JsonNode). The chapter's close ritual was largely operated by the joint chapter-3 close arc; this document closes the symmetry gap so future agents see a complete chapter-3 close-doc sequence.

— Chapter 3.5 close synthesis, joint chapter-3 cross-cutting close arc (2026-05-10).
