# V2 — Kickoff Brief for Fresh Agents

You're picking up V2 work mid-stream. This brief gets you oriented in 5 minutes; the canonical surfaces it points to brief you in another hour. **Read this first.** Then read the strategic surfaces in the prescribed order. Then write code.

---

## What this is

You're working on the **F# sidecar (V2) of an OutSystems DDL exporter** at `/home/user/outsystems-ddl-exporter`. V1 is the C# trunk (~78K LOC at `src/`, fully shipping). V2 lives at `sidecar/projection/` — pure F# core plus C# adapters at the boundary, ~7K LOC and 631 passing tests.

V2's purpose: make a high-stakes database cutover **verifiable, reversible, and repeatable** through a sibling chorus of synchronized projections (SSDT DDL, CDC-aware data inserts, DACPAC, refactor log, distributions, diagnostics) emitted from a single algebraic core. **V1 ships the cutover; V2 makes it trustworthy.**

---

## The forcing function

A 300-table OutSystems 11 system facing an External Entities cutover. Every Entity swaps 1:1 to external on-prem SQL Server. Four environments (dev / qa / UAT / prod), Azure DevOps PR promotion. **CDC running in production with features depending on it; spurious change records would disrupt those features.** User FKs (CreatedBy / UpdatedBy) need environment-specific remapping. RefactorLog records must survive across schema versions. Repeatable cadence — schema and data evolve continuously.

If V2's emission is wrong: production data integrity corrupted; CDC-dependent features broken silently; partial cutover leaves hybrid state structurally hard to recover from. **This is what V2 must survive.** The algebra isn't aesthetic; it's the structural condition for the cutover being trustworthy.

---

## Strategic surfaces — read in this order

Don't skip. The first reading pass takes ~1 hour and gives you the full picture.

| # | Document | What it gives you | Lines |
|---|---|---|---|
| 1 | **`sidecar/projection/VISION.md`** | Strategic frame; cutover as forcing function; sibling chorus + verification posture; acceptance criteria; cutover fallback ladder; deeper structure overview | ~410 |
| 2 | **`sidecar/projection/SPINE.md`** | The system IS a category. Seven patterns tessellate (Π / Adapter / Pass / Render / Compare / Property / Diff). Seven primitives recur. Six structural inferences (sheaf / adjunction / Hom-set / quotient / continuation / tessellation instance). Ten leverage points. **Read carefully — it's the multiplier.** | ~660 |
| 3 | **`sidecar/projection/PLAYBOOK.md`** | Technical guidance bridging vision to implementation. Recurring patterns with code skeletons. F#/C# boundary contract. Five decision trees. Twelve anti-patterns. Per-chapter strategic notes. | ~740 |
| 4 | **`sidecar/projection/STAGING.md`** | Stage 0 foundation phase. Twelve dependencies to ship before chapter 3.1 opens; ~3,000 LOC budget; ~12-15 sessions. **This is what you're doing first.** | ~580 |
| 5 | **`sidecar/projection/BACKLOG.md`** | ~375 items inventoried by chapter / status / disposition. Includes Stage 0 + free corollaries. | ~900 |
| 6 | **`sidecar/projection/CLAUDE.md`** | Fresh-agent navigation; operating disciplines table; F# feature surface; programming style; load-bearing commitments. | ~450 |
| 7 | **`sidecar/projection/AXIOMS.md`** | Formal system: A1–A35, T1–T12 with amendments. **Read on demand** when working on a specific axiom. | ~600 |
| 8 | **`sidecar/projection/DECISIONS.md`** | Append-only resolved-questions log. Read most-recent ten entries first. Older entries remain in force unless superseded. | ~4500 |
| 9 | **`sidecar/projection/ADMIRE.md`** | V1↔V2 component bridge. Per-component status (admiring → extracting → extracted). | ~300 |
| 10 | **`sidecar/projection/HANDOFF.md`** | Chapter-bridge tactical letter from chapter-2 close; what's load-bearing; what's deferred. | ~150 |

Plus **chapter pre-scopes** (`CHAPTER_3_PRESCOPE_*.md` and `CHAPTER_4_PRESCOPE_*.md`) — read the relevant one when you open a chapter. Each is the first-draft slice plan.

Plus **`VISION_REVIEW.md`** for review evidence and the eight subagent reports that produced revision 2 — consult on demand for context.

---

## Where you are in the timeline

- **Chapter 1** (sessions 1–12) **closed**. Algebraic foundation; IR; strategy layer codification; three sibling Π emitters (`RawTextEmitter`, `JsonEmitter`, `DistributionsEmitter`). Per `CHAPTER_1_CLOSE.md`.
- **Chapter 2** (sessions 13–25) **closed**. OSSYS adapter (25 translation rules); Diagnostics writer; `Lineage<Diagnostics<'a>>` dual composition; strategy-layer codification at stability mark. Per `CHAPTER_2_CLOSE.md`.
- **Stage 0** (you are here) — foundation phase before chapter 3.1. Twelve items per `STAGING.md`; **not yet shipped**.
- **Chapter 3.1+** — chapter pre-scopes exist for 3.1, 3.2, 3.3, 3.4, 3.5, 4.1.A, 4.1.B, 4.2, 4.3, 4.4. Each is a first-draft slice plan refined under empirical pressure once the chapter opens.

**The four-environment cutover is the fixed point.** V2 must reach the V2-augmented mode of the fallback ladder by T-30 days from cutover; V2-driver mode is the aspiration. V1 stays warm through cutover+30 days regardless.

---

## What you'll do first

Stage 0 ships in four tiers (per `STAGING.md`). Start with **Tier 1 — documentation hygiene + governance burst**:

### S0.G — Five `DECISIONS.md` governance entries

1. **R6 cutover-window split-brain governance rule.** During dual-track, V2 emits-but-doesn't-ship; canary verifies V1 ≈ V2; disagreement blocks PR; transition gated on N=10 consecutive green canary runs.
2. **Chapter 3 sequencing decision.** Read-side adapter promoted to chapter 3.1 (was 3.2). The dogfood reframing (V2 verifies V1 starting now) earns it.
3. **CLAUDE.md reading-order update.** `VISION.md` added to the canonical surface list for fresh agents.
4. **T-30 / T-15 fallback ladder gates.** V2-driver requires (a) chapter 3 closed with green canary; (b) chapter 4.1 shipping; (c) chapter 4.2 shipping; (d) ≥1 full UAT dry-run. T-30 yellow → V2-augmented; T-15 unstable → V1-only. V1 stays warm through cutover+30.
5. **Stage 0 commitment.** The foundation phase ships as one unit before chapter 3.1 opens. Names the twelve Stage 0 items.

### S0.F — `AXIOMS.md` amendment scaffolding

Append placeholder headers (with TBD bodies) for each pending amendment:
- T1 amended (binary normal-form composition) — TBD chapter 3.3 close
- T11 amended (structural type encoding) — TBD chapter 3 cross-cutting close
- T11 amended again (diff-typed inputs) — TBD chapter 3.5 close
- A1 amended (four-variant `SsKey`) — TBD chapter 3 cross-cutting close
- A35 candidate (Π-erased axes) — TBD chapter 3.4 close
- A36 candidate (`CatalogDiff` exhaustiveness) — TBD chapter 3.5 close
- A32 cash-out — TBD chapter 4.2 close

### S0.J — Currency checks

Walk `ADMIRE.md` / `AXIOMS.md` / `CLAUDE.md` / `HANDOFF.md`; confirm:
- ADMIRE entries reflect actual V2 state (no drift).
- AXIOMS has A32, A34, T1-amended current.
- CLAUDE operating-disciplines links current; F# feature surface table reflects new candidates from SPINE.
- HANDOFF active-deferrals list has no silent-trigger fires.

### S0.L — Verify cross-references

Confirm `VISION.md` and `BACKLOG.md` reference SPINE / PLAYBOOK / STAGING in their documentation maps. (They do, as of `630e32c`.)

**First-session goal:** complete S0.G + S0.F + S0.J + S0.L. All documentation-only. ~700 lines of new documentation across DECISIONS / AXIOMS / ADMIRE / CLAUDE. If you do this cleanly, you've unblocked Stage 0's Tier 2.

**Subsequent sessions** (in order):
- **Tier 2 (keystone):** S0.A — type primitives in `Projection.Core/Types.fs` (~50 LOC).
- **Tier 3 (structural commitment):** S0.B — `ArtifactByKind` + `SsKey` four-variant + `CatalogDiff`. Per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`. Six slices, ~700 LOC source + ~520 LOC test.
- **Tier 4 (parallel support):** S0.C–S0.K. Render skeletons; property combinators; Tolerance taxonomy; configuration port; test support consolidation; multi-environment generator skeleton.

**After Stage 0:** chapter 3.1 opens.

---

## The disciplines you operate

Codified in `CLAUDE.md` operating-disciplines table. Short list with what makes them load-bearing:

- **F# pure core / no I/O in Core.** Audited clean. Don't break.
- **A18 amended.** Π consumes `Catalog × Profile`, never `Policy`. If you reach for Policy from inside an emitter, the work belongs in a pass.
- **Smart constructors return `Result<'a>`.** Every value-typed invariant rides on the value.
- **Closed DUs with `[<RequireQualifiedAccess>]`** for collision-prone case names.
- **Closed-DU expansion empirical test.** Adding a variant should produce F# exhaustiveness errors only at match sites within the variant's module. If callers outside the module need reshaping, the seam is wrong.
- **Two-consumer threshold for primitive extraction.** One consumer doesn't earn an abstraction. Per `DECISIONS 2026-05-13` (anticipation vs speculation).
- **IR grows under evidence, not speculation.** Wait for the second consumer.
- **Decimal as default for continuous statistical evidence.** T1 byte-determinism requires it.
- **Pass return-type codification.** `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` when decisions plus observer-relevant findings.
- **Trace-before-fixture.** When implementing a V1 capability, trace V1's actual handling first; classify into the three-class typology (JSON-projection-lossiness / V2-boundary-discipline / alternative-IR-surface); then write the failing test.
- **Audit during validation.** When something second-order surfaces, act on it before shipping the slice.
- **Active deferrals re-checked at chapter close.** Silent-trigger fires get caught by table-scan, not chronological re-read.

---

## F# / C# boundary rules (per PLAYBOOK)

- **F# default.** `Projection.Core`, all emitters (even Profile-consuming), boundary adapters reading via SqlClient + JSON, the comparator, all test projects.
- **C# only when foreign-API mutation-heavy.** `Projection.Pipeline` (testcontainers boot, file-system writes, CLI host); `Projection.Targets.SSDT.Dacpac` (DacFx wrapping with `IDisposable` lifetimes).
- **Seam: value-typed.** `byte[]` for DACPAC bytes; `Result<T, E>` for errors; `Map<RelativePath, string>` for directory writes; `Catalog` as opaque to C#.
- **Never:** F# code calling DacFx directly. F# code touching `IDisposable` chains. C# code mutating `Catalog`. C# code reading `Policy`.

---

## Test discipline (per CLAUDE.md)

- **Test names cite the axiom or theorem they enforce.** F# backtick-quoted: `` ``A1: rename preserves OssysOriginal SsKey`` ``, `` ``T11: emitSlices key-set equals Catalog.allKinds`` ``.
- **`Skip = "..."` for deliberate V2 divergences from V1.** The rationale lives in the Skip string. The test stays in test discovery so the divergence is structurally visible.
- **Property tests for combinatorial spaces; example tests for specific contracts.** FsCheck.Xunit covers permutation invariance, idempotence, deterministic-output-under-shuffling.
- **Three-tier canary** (per `CHAPTER_3_PRESCOPE_CANARY_PROPERTY_SURFACE.md`): tier-1 pure (no Docker, sub-second); tier-2 container-pooled (~150ms per case); tier-3 nightly integration.

---

## Git / workflow

- **Branch:** `claude/evaluate-fsharp-sidecar-6QTrt` (verify with `git status`; check `git log --oneline -5` for context).
- **Commit individual slices.** Never batch unrelated changes.
- **Follow the chapter-close ritual** at every chapter close (eight items per `CLAUDE.md` operating-disciplines table; `DECISIONS 2026-05-14`).
- **Before pushing:** `dotnet test` should be green. Existing 631 tests stay green at every Stage 0 step.
- **Commit message format:** follow the existing pattern in `git log --oneline`. Multi-paragraph body explaining the *why*, not just the *what*. Sign with the Claude Code session URL.

---

## When you're stuck

Return to the algebra. Per SPINE, the system IS a category. Identify which pattern instantiates here:

- **Π (Emitter)** — `Catalog -> Result<ArtifactByKind<'element>, EmitError>` (or Profile-consuming, or diff-consuming).
- **Adapter** — `External -> Task<Result<'internal, _>>`.
- **Pass** — `Catalog -> Policy -> Profile -> Lineage<'output>` (or with Diagnostics).
- **Render** — `SsKey list -> ArtifactByKind<'element> -> 'output`.
- **Compare** — `Tolerance -> Catalog -> Catalog -> Diff`.
- **Property** — `Catalog -> bool` (universally quantified canary).
- **Diff** — `Catalog -> Catalog -> Result<CatalogDiff, _>`.

Name the type variable. Identify which primitives compose. The pattern's universal properties tell you what tests must pass. The chapter writes itself.

When tempted to shortcut: trace V1 before writing tests; wait for the second consumer before extracting; add the AXIOMS amendment at chapter close. Each shortcut is a debug session deferred.

---

## What success looks like

**End of first session (documentation-only Tier 1):** S0.G + S0.F + S0.J + S0.L done. ~700 lines of new docs across DECISIONS / AXIOMS / ADMIRE / CLAUDE. All existing tests green. Chapter-2-close artifacts current.

**End of Stage 0 (~12-15 sessions):** all twelve items shipped. `Projection.Core/Types.fs` carries the seven pattern signatures. `ArtifactByKind` + `SsKey` four-variant + `CatalogDiff` are types, not disciplines. Existing emitters migrated; substring T11 tests retired. Render skeletons reserved. Property combinators ready. Tolerance taxonomy named. Configuration ported. Test support consolidated. Multi-environment generator stubbed. **All AXIOMS amendments scheduled with TBD bodies; chapter agents fill at close.**

**Chapter 3.1 opens** with the foundation in place. The dogfood frame ships immediately (V2 verifies V1's `osm_model.json` round-trip via existing `JsonEmitter`). The read-side adapter ships across slices 2-3. The comparator + Projection.Pipeline ship at slices 4-6.

**Cutover-quarter trajectory:** chapters 3.1 → 3.5 close (V2-augmented mode operational). Chapter 4.1.A → 4.4 close (V2-driver mode possible per T-30 gate). Cutover proceeds environment-by-environment. V1 sunset deferred until all four environments run V2 emissions for one full schema-evolution cycle.

---

## Hold the spine

V2 isn't aesthetic. The algebra isn't ceremonial. Every type theorem (T1, T11, A1) maps to a cutover-blocking property. The seven primitives compound. The chapter pre-scopes are tessellation instances, not arbitrary slice plans.

**V1 ships the cutover. V2 makes it verifiable.** Stage 0 is the moment the algebra becomes types. Land it cleanly; the rest compounds.

Three structural type theorems, one foundation phase, one property-test surface, one triangulation comparator, one fallback ladder, ten chapters. Hold the spine. The rest follows.

— Welcome aboard. Read the surfaces. Write the documentation. Open the first commit.
