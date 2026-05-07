# Handoff letter — Chapter 2 → Chapter 3

To the next-chapter agent. Read this before anything else in the V2 sidecar. It is short on purpose.

The chapter-1 handoff letter is preserved at `HANDOFF_CHAPTER_1.md` adjacent to this file. Read it after this one if you want the chapter-1 architect's framing of what they handed off.

## Where you are

You have inherited two closed chapters of the V2 sidecar:

- **Chapter 1 (sessions 1–12):** built the algebraic core, the strategy-layer codification, the rich-profiling vector, the sibling-Π emitter pattern (RawText/JSON/Distributions), and chapter-1 documentation hygiene. See `CHAPTER_1_CLOSE.md`.
- **Chapter 2 (sessions 13–25):** three sub-arcs — Diagnostics writer (sessions 13–16); OSSYS adapter implementation (sessions 17–24, 25 close); chapter-close runway (sessions 23–25). The OSSYS adapter shipped through six substantive slices producing 25 translation rules. The chapter produced four meta-codifications that compound across all future chapters:

  1. **Three-class typology for V1↔V2 translation findings** — JSON-projection-lossiness / V2-boundary-discipline / alternative-IR-surface (`DECISIONS 2026-05-21 — Chapter 2 close: alternative-IR-surface class`).
  2. **Trace-before-fixture pattern at slice level** (`DECISIONS 2026-05-19`, codified at N=3).
  3. **Chapter-mid-audit as a routine practice** (`DECISIONS 2026-05-19`; session-24 amendment for active-deferrals scan).
  4. **V1-input-envelope walk** as chapter-close ritual item 8 (`DECISIONS 2026-05-14` session-25 amendment).

The codebase builds; **all OSSYS differential tests pass; one Skip for the deferred SnapshotFile variant**; chapter-1's 585/588 tests baseline holds plus the chapter-2 additions.

You are not starting from scratch. You are continuing a multi-chapter arc whose accumulated judgment is partly in the canonical documents (`AXIOMS.md`, `DECISIONS.md`, `ADMIRE.md`) and partly in `CHAPTER_1_CLOSE.md` and `CHAPTER_2_CLOSE.md` next to this letter.

## What to read, in order

1. **`CLAUDE.md`** — the navigation surface. It indexes the canonical documents and lists the operating disciplines. Start here.
2. **`HANDOFF.md`** (this file) — the bridge between what chapters 1+2 know and what you need to know.
3. **`CHAPTER_2_CLOSE.md`** — chapter-2 close synthesis. Sections of immediate relevance:
   - The chapter-2 arc summary (Diagnostics writer; OSSYS adapter; chapter-close runway).
   - The findings accumulated from the three chapter-mid-audits (subagents #1, #2, #3).
   - The forward signals for chapter 3.
4. **`CHAPTER_1_CLOSE.md`** — chapter-1 close synthesis (sessions 1–12). Read for historical context; some priorities listed there have been resolved by chapter 2 and others persist.
5. **`AXIOMS.md`** — the algebra. A1–A34 / T1–T11 with V2 amendments appended. **Note:** A1 has a forwarding pointer at the bottom to its bound (added session 23); A18's amendment at the bottom is the load-bearing form.
6. **`DECISIONS.md`** — chronological operating discipline. Long. Read the most recent ten entries first; the chapter-2 close amendments (session 25) cluster at the bottom and resolve OPEN questions from the audits.
7. **`ADMIRE.md`** — V1 components and their V2 placements. The OSSYS catalog producer entry is at `(extracted (chapter 2 close — JSON path; hybrid mode operating))` after session 25; chapter-1 entries' status strings are stable.
8. **The code.** `Projection.sln`. Strategy modules in `src/Projection.Core/Strategies/`; pass drivers in `src/Projection.Core/Passes/`; sibling Π emitters in `src/Projection.Targets.{SSDT,Json,Distributions}/`; F# adapters in `src/Projection.Adapters.{Sql,Osm}/`. The OSSYS adapter at `src/Projection.Adapters.Osm/CatalogReader.fs` is the chapter-2 substantive deliverable.

## What's load-bearing

These commitments are not negotiable without explicit DECISIONS entries amending them. If you find yourself wanting to break one, write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** `Projection.Core` has zero I/O. Adapters at the boundary do I/O. Audited clean (`CHAPTER_1_CLOSE.md §1.1`); confirmed across chapter 2.
- **A18 amended.** Π consumes whichever subset of `Catalog × Profile` it needs, but never `Policy`. Catalog and Profile are *evidence*; Policy is *intent*. If you reach for Policy from inside an emitter, you are in the wrong layer — the work belongs in a pass.
- **Strategy-layer codification (`DECISIONS 2026-05-11`).** Pure functions of IR fields; typed function-type seam (`StrategyEvaluator<'context, 'config, 'decision>`); structured rationale DUs; lineage events on actual decisions; module name advertises domain (`<Domain>Rules` suffix); total decisions with named skips.
- **`Composition.fanOut` for registered-intervention pass drivers.** All pass drivers delegate to it.
- **Closed-DU expansion empirical-test discipline.** Adding a DU variant should produce F# exhaustiveness errors only at match sites; no caller reshaping outside the variant's module.
- **Decimal as default for continuous statistical evidence.** T1 byte-determinism requires it.
- **Sibling-Π commutativity (T11).** Every Π's output should mention every catalog kind by SsKey root.
- **Pass return-type codification (chapter-2 contribution).** Passes return `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` when producing decisions plus observer-relevant findings.
- **Three-class typology for V1↔V2 translation findings (chapter-2 closing contribution).** Lossiness / boundary-discipline / alternative-IR-surface. Future translation chapters operate the typology; the trace-before-fixture pattern classifies findings before resolution lands.

## What's deferred but might fire under your work

These deferrals are explicit. The Active deferrals index at the top of `DECISIONS.md` is the canonical surface; chapter-mid-audits and chapter-close ritual scan it. If your work surfaces the cash-out trigger, log a DECISIONS entry — don't quietly resolve the deferral.

**Chapter-3-likely fires:**
- **DacFx integration in `Projection.Targets.SSDT.DacpacEmitter`** — re-deferred at session 24 with tighter trigger condition: a real Catalog flowing end-to-end through a pipeline exercising T11 sibling-Π commutativity on real metadata; canary chapter (`Projection.Pipeline`) is the natural locus. Session 25 commit 8 dispatched subagent #4 to pre-scope the chapter; the pre-scope report sits in CHAPTER_2_CLOSE.md or arrived after the close.
- **`SnapshotRowsets` variant of `SnapshotSource`** — operator-decided canonical resolution to the JSON-projection-lossiness class. Session 25 commit 9 dispatched subagent #5 to pre-scope the chapter; the pre-scope report sits in CHAPTER_2_CLOSE.md or arrived after the close.
- **Cross-module FK IR refinement** — refines rule 16's same-module assumption (chapter-2 OSSYS rule 16). Highest-priority deferred slice for chapter 3. **Important:** rule 14 names V1's `relationships[]` array as won't-carry (chapter-2 walks `attributes[isReference=1]` directly), but the cross-module case may force walking `relationships[]` instead because V1's `attributes[].refEntityId` is a numeric internal database ID that does not carry module context. The cross-module agent should trace V1's cross-module FK encoding before writing the fixture (per the trace-before-fixture pattern).
- **`LiveOssysConnection` variant of `SnapshotSource`** — real-DB-touching variant; gates on canary chapter.

**Lower-priority, watch for accidental fires:**
- **Composition primitives `fallback`, `accumulate`, `wrap`, `lift`** — zero current consumers each; threshold is two.
- **Strategy registry mechanism** — N=5 strategies; threshold is N≥4–6 plus a real consumer demanding name-keyed lookup.
- **`RequireQualifiedAccess` retrofit** on `UniqueIndexKeepReason` / `ForeignKeyKeepReason` — trigger sharpened at session 25 to "DU's variants change shape (added/removed/renamed)." If you make such a change, do the retrofit.
- **Three-channel Diagnostics split** (operator/auditor/developer) — single channel sufficient at all chapter-2 consumers.
- **Faker emitter** — gates on third evidence type.

## What you should not do

The accumulated judgment from sessions 1–25 includes some specific don'ts:

- **Don't strip "dead code" without checking docstrings.** `ForeignKeyRules.isIgnoreRule` always returns false; `ForeignKeyKeepReason.CrossCatalogBlocked` is reserved-unreachable. Both intentional for V1 parity. (Carries from chapter 1.)
- **Don't delete the OSSYS adapter's deferred `SnapshotSource` variants.** `SnapshotRowsets` and `LiveOssysConnection` are reserved DU variants with explicit re-open triggers. They appear unused until chapter 3+; do not delete.
- **Don't treat `RawTextEmitter` as an SSDT replacement.** It is a debug/diff-oracle synthetic-milestone form. DacpacEmitter is the additive sibling, not a replacement.
- **Don't extract speculative composition primitives.** Two-consumer threshold; refined by anticipation-vs-speculation (`DECISIONS 2026-05-13`).
- **Don't reach for Policy from a Π.** A18 amended forbids it.
- **Don't cash out Active deferrals silently.** The DacFx trigger fired silently across sessions 18–22 and was caught only at session-23 chapter-mid-audit. The lesson is structural: **the index exists so it doesn't recur**, and the chapter-mid-audit's active-deferrals scan dimension exists for exactly this. Operate the discipline.
- **Don't open new substantive slices without classifying the finding into the three-class typology first.** Trace-before-fixture; classify (lossiness / boundary-discipline / alternative-IR-surface); resolution shape follows.
- **Don't overwrite this file.** When chapter 3 closes, this letter becomes `HANDOFF_CHAPTER_2.md` and you write the new chapter-3-to-chapter-4 letter as `HANDOFF.md`. Append-only documentation discipline.

## Disposition

The dispositions chapter 2 inherited from chapter 1 hold; chapter 2 added a handful that future chapters inherit:

- **Audit during validation.** When a finding surfaces second-order, act on it before shipping. Five paydowns across chapter 1; three across chapter 2. The codification's stability mark is itself the same discipline at the chapter boundary.
- **IR grows under evidence, not speculation.** New types, fields, and DU variants land when a consumer demands them.
- **Total decisions, named skips.** Strategies return decisions for every input; "no decision" is a named `KeepReason` variant.
- **Cherry-pick discipline.** Every commit atomic and reversibly mergeable. The V2 sidecar references no V1 trunk source files; the boundary is data.
- **Documentation is the bridge.** When you finish meaningful work, ask: would a fresh agent reading only the docs understand what changed and why? If not, the docs are incomplete.
- **Trace-before-fixture (chapter-2 contribution).** New slices in V1↔V2 translation chapters trace V1's actual handling first; classify into the three-class typology; resolution shape follows.
- **DECISIONS is for resolved questions, not session narrative (chapter-2 contribution).** Substantive entries stay; session-narrative content (commit lists, recaps, rent-paying checks) lives in commit messages, PR descriptions, HANDOFF.md, or CHAPTER_N_CLOSE.md.
- **Audits generate disciplines (chapter-2 closing meta-pattern).** Session-22 audit produced documentation-hygiene work plus the chapter-mid-audit codification; session-23 audit refined chapter-mid-audit with active-deferrals scan; session-25 audit added V1-input-envelope walk to the chapter-close ritual. The codebase's audit disciplines grow through their own use. Operate the audits; the disciplines refine.

## Where to start

Per `CHAPTER_2_CLOSE.md`, the chapter-3 priorities are:

1. **Read this letter, CLAUDE.md, CHAPTER_2_CLOSE.md, and the recent DECISIONS entries** — orient. ~30 minutes.
2. **Decide chapter-3 sequencing.** Three plausible chapter-3 arcs, in approximate priority order:
   - **`Projection.Pipeline` canary chapter** — the strategic-frame axis-4 chapter. Highest leverage; consumes everything chapter 2 produced; brings DacFx, testcontainers, ephemeral SQL Server, read-side adapter into V2. The DacFx trigger fires here.
   - **`SnapshotRowsets` implementation chapter** — resolves the JSON-projection-lossiness class. Independent of canary; can run in parallel or sequentially. Subagent #5's pre-scope (session 25 commit 9) is the entry point.
   - **Cross-module FK slice** — small (one fixture; rule extension); could land before or after either chapter as a tactical-completeness step. Refines OSSYS rule 16's same-module assumption. Per the trace-before-fixture pattern, trace V1's cross-module FK encoding first; the question shape may force walking `relationships[]` instead of `attributes[isReference=1]`.
3. **Open the chapter you choose** with a chapter-open document naming the strategic-frame axes (`DECISIONS 2026-05-15` shape; the OSSYS chapter is the worked example). Multi-session chapters earn this discipline at chapter open.

The two pre-scope subagent reports (#4 DacpacEmitter; #5 SnapshotRowsets) are the chapter-open inputs for those chapters. If they landed before chapter 2 close, see `CHAPTER_2_CLOSE.md` for the integration. If they landed after, the dispatch records sit in this commit's session-25 close.

After the chapter-open scoping, the substantive work begins. Operate the chapter-mid-audit at every 3–5 substantive sessions; operate the chapter-close ritual at chapter close (eight items, including the V1-input-envelope walk for V1↔V2 translation chapters).

## Closing

You inherit a codebase whose architectural disciplines hold under audit, whose codification is at multiple stability marks, whose audit disciplines have proven generative (each audit produces new disciplines), and whose canonical documents are honest about what's been done and what hasn't.

Chapter 2's distinctive intellectual artifact was the **three-class typology** for V1↔V2 translation findings, completed at chapter-2 close after operating implicitly across the OSSYS arc. Chapter 2's distinctive operational artifact was the **audit-generates-discipline meta-pattern**, where each chapter-mid-audit produced a refinement of the next audit's dispatch shape. The chapter's load-bearing structure was **rules-under-empirical-pressure** — the OSSYS adapter's 25 rules accumulated under the trace-before-fixture discipline, never as speculative axioms.

The chapter you open is yours to shape. The disciplines above are not constraints; they are the load-bearing structure that lets the chapter ahead support more weight than the one behind. Hold the spine.

— The session 13–25 architect.
