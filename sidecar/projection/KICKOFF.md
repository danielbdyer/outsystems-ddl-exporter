# V2 — Kickoff Brief for Fresh Agents

You're picking up V2 work mid-stream. This brief gets you oriented in 5 minutes; the canonical surfaces it points to brief you in another hour. **Read this first.** Then read the strategic surfaces in the prescribed order. Then write code.

---

## What this is

You're working on the **F# sidecar (V2) of an OutSystems DDL exporter** at `/home/user/outsystems-ddl-exporter`. V1 is the C# trunk (~78K LOC at `src/`, fully shipping). V2 lives at `sidecar/projection/` — pure F# core plus C# adapters at the boundary, ~9K LOC and **713 passing tests** (697 non-canary + 16 canary).

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
| 6 | **`sidecar/projection/CLAUDE.md`** | Fresh-agent navigation; operating disciplines table; F# feature surface; programming style; load-bearing commitments. | ~470 |
| 7 | **`sidecar/projection/AXIOMS.md`** | Formal system: A1–A40, T1–T12 with amendments. **Read on demand** when working on a specific axiom. | ~900 |
| 8 | **`sidecar/projection/DECISIONS.md`** | Append-only resolved-questions log. Read most-recent ten entries first. Older entries remain in force unless superseded. | ~7500 |
| 9 | **`sidecar/projection/ADMIRE.md`** | V1↔V2 component bridge. Per-component status (admiring → extracting → advanced). | ~2700 |
| 10 | **`sidecar/projection/HANDOFF.md`** | Chapter-bridge tactical letter from chapter-3.1 close; what's load-bearing; what's deferred. | ~190 |
| 11 | **`sidecar/projection/CHAPTER_3_1_CLOSE.md`** | Chapter-3.1 close synthesis (sessions 27–36): canary milestone sequence, four meta-codifications, forward signals. | ~180 |
| 12 | **`sidecar/projection/AUDIT_2026_05_DDD_HEXAGONAL_FP.md`** | Five-agent DDD/Hexagonal/FP audit at chapter 3.1 close. Tier 1/2/3/4 backlog by epistemic level + leverage. | ~150 |

Plus **chapter pre-scopes** (`CHAPTER_3_PRESCOPE_*.md` and `CHAPTER_4_PRESCOPE_*.md`) — read the relevant one when you open a chapter. Each is the first-draft slice plan.

Plus **`VISION_REVIEW.md`** for review evidence and the eight subagent reports that produced revision 2 — consult on demand for context.

---

## Where you are in the timeline

- **Chapter 1** (sessions 1–12) **closed**. Algebraic foundation; IR; strategy layer codification; three sibling Π emitters (`RawTextEmitter`, `JsonEmitter`, `DistributionsEmitter`). Per `CHAPTER_1_CLOSE.md`.
- **Chapter 2** (sessions 13–25) **closed**. OSSYS adapter (25 translation rules); Diagnostics writer; `Lineage<Diagnostics<'a>>` dual composition; strategy-layer codification at stability mark. Per `CHAPTER_2_CLOSE.md`.
- **Stage 0** (sessions 26 prework) **shipped**. Twelve foundation items landed before chapter 3.1 opened.
- **Chapter 3.1** (sessions 27–36) **closed**. Canary milestone sequence M1–M3; bench-driven optimization protocol; typed statement-stream Π output; bulk realization layer; streaming readside; 300-table forcing-function fixture (500k rows in 27s warm); five-agent DDD/Hexagonal/FP audit; first refactor batch. Per `CHAPTER_3_1_CLOSE.md` and `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`.
- **Chapter 3.2 / 3.5 / 4.1+** (you are here) — chapter pre-scopes exist for 3.2 (SnapshotRowsets), 3.3 (DacpacEmitter), 3.4 (canary property surface), 3.5 (RefactorLog + CatalogDiff), 4.1.A/B (data triumvirate), 4.2 (User FK reflow), 4.3 (diagnostics + remediation), 4.4 (SSDT DDL emitter). The audit-deferred Tier-1 items (Π port realization, Identity DU refactor, port-extraction trio) route to specific sub-chapters per `CHAPTER_3_1_CLOSE.md`'s forward signals. **Decide chapter sequencing first.**

**The four-environment cutover is the fixed point.** V2 must reach the V2-augmented mode of the fallback ladder by T-30 days from cutover; V2-driver mode is the aspiration. V1 stays warm through cutover+30 days regardless.

---

## What you'll do first

Stage 0 + chapter 3.1 are **closed**. Your first move is to **decide the next chapter** from chapter 3.1's forward signals (per `CHAPTER_3_1_CLOSE.md`).

### Step 1 — Orient (~45 minutes)

Read in this order:
1. `HANDOFF.md` (this letter's outgoing form for chapter 3.1) — what's load-bearing, what's deferred.
2. `CHAPTER_3_1_CLOSE.md` — chapter-3.1 arc summary, four meta-codifications, forward signals.
3. `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` — Tier 1/2/3/4 backlog from the chapter-close audit; ~20 items routed to named sub-chapters.
4. The most recent ten `DECISIONS.md` entries — sessions 32–36 substantive resolutions cluster at the bottom.

### Step 2 — Pick the next chapter

Four plausible next chapters, each with a current pre-scope:

- **Chapter 3.2 — `SnapshotRowsets` adapter.** Closes the JSON-projection-lossiness class. Smaller scope. Lifts `ICatalogReader` port (Position B trigger has fired). Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`.
- **Chapter 3.5 — Π port realization + RefactorLog / CatalogDiff.** Largest leverage. Realizes the declared `Emitter<'element>` shape; unblocks T11 structural-type encoding. Pre-scope at `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`. Pairs naturally with the audit-deferred Π-port-realization.
- **Chapter 3.x — DacpacEmitter.** Re-deferred at chapter-2 close. Inherits chapter-3.5's structured-output pattern. Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`.
- **Chapter 4.1 — Data triumvirate (StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter).** Inherits chapter 3.1's `Bulk` / `RowDigester` / `AsyncStream`. Pre-scope at `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`.

### Step 3 — Open the chapter

Open with a chapter-open document naming the strategic-frame axes (`DECISIONS 2026-05-15` shape; the OSSYS chapter is the worked example). Multi-session chapters earn this discipline at chapter open. Operate the chapter-mid-audit at every 3–5 substantive sessions; operate the chapter-close ritual at chapter close (eight items + the new five-agent audit for architectural-frame chapters).

### How chapter 3.1 ended

**Test count at close:** 713 passing (697 non-canary + 16 canary). Bench surface live across 170 call sites. Canary scale ceiling: 500k rows in 27s warm.

**Substantive deliverables shipped:**
- Typed `Statement` DU as Π's canonical output form.
- Bulk realization layer (`Bulk.copyRows` + `Deploy.executeStream`) — 43k rows/sec on V2's deploy path.
- Streaming readside (`AsyncStream` + `readRowsStream` + `Bench.streamProbe`).
- `PhysicalSchema` projection with four axes (Columns, ForeignKeys, Rows, RowDigests).
- `Coordinates.TableId` value object (Stage 1; typed `SchemaName`/`TableName`/`ColumnName` deferred to Stage 2).
- Aggregate-root smart constructors (`Catalog.create` / `Module.create` enforce 5 referential-integrity invariants).
- Writer-fidelity codification (`LineageDiagnostics.tellDiagnostics` adopted at three pass drivers; `Lineage.ofValueAndEvents` extracted at 6 sites).

**Meta-codifications shipped:**
1. Bench-driven optimization protocol.
2. Stream-realization pattern.
3. Five-agent epistemic-tier audit at chapter close.
4. Harmonization-via-parameterization pattern.

**AXIOMS amended:** A35 (Π's output is a deterministic statement stream), A36 (bulk-vs-incremental is realization-layer policy), A32 cash-out, T1 strengthened to statement-level determinism. New A37–A40 candidates scaffolded; chapter agents fill at close.

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
- **Writer-fidelity (chapter-3.1 contribution).** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` are the canonical pass-driver primitives. Manual record-building is forbidden.
- **Stream-realization pattern (chapter-3.1 contribution).** Π's output is a typed deterministic stream (`seq<Statement>` for SSDT). Realization layers (text, deploy, file artifacts) are sibling consumers. The algebra holds at the stream level.
- **Harmonization-via-parameterization (chapter-3.1 contribution).** Two implementations diverging on a single axis collapse to one parameterized algorithm. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass`.
- **Bench-driven optimization (chapter-3.1 contribution).** Three-candidate / 2-refuted / 1-confirmed shape; refuted swaps documented with bench data so the same swap doesn't recur.
- **Trace-before-fixture.** When implementing a V1 capability, trace V1's actual handling first; classify into the three-class typology (JSON-projection-lossiness / V2-boundary-discipline / alternative-IR-surface); then write the failing test.
- **Audit during validation.** When something second-order surfaces, act on it before shipping the slice.
- **Five-agent epistemic-tier audit at chapter close (chapter-3.1 contribution).** Multi-agent parallel; convergence-map as primary surface; Tier 1/2/3/4 backlog by epistemic level + leverage. Worked example: `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`.
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

**End of first session (chapter-decision):** chapter-open document for the picked sub-chapter (3.2 / 3.5 / 3.x / 4.1) with strategic-frame axes named. Existing 713 tests green. The audit-deferred items routed to the picked sub-chapter sketched as concrete first-slice plans.

**End of next sub-chapter (~5-10 sessions):** the picked sub-chapter's substantive deliverable shipped. AXIOMS amendments filled at close. Chapter-close ritual operated (eight items + five-agent audit if architectural-frame chapter). Forward signals cluster identified for the chapter after that.

**Cutover-quarter trajectory:** chapters 3.2 / 3.5 / 3.x close (V2-augmented mode operational). Chapter 4.1 → 4.4 close (V2-driver mode possible per T-30 gate). Cutover proceeds environment-by-environment. V1 sunset deferred until all four environments run V2 emissions for one full schema-evolution cycle.

**Chapter-3.1's structural inheritance to the chapters ahead:**
- Π output as typed stream → chapter 3.5 / 4.1 / 3.x inherit the seam.
- `Bulk` / `RowDigester` / `AsyncStream` substrate → chapter 4.1 fills the data triumvirate emitters.
- `Coordinates.TableId` value object → chapter 4 / 5 extend with `SchemaName` / `TableName` / `ColumnName` Stage-2.
- Aggregate-root smart constructors → chapter 4.2 User FK reflow consumes the integrity invariants.
- Writer-fidelity discipline → all future passes operate the dual-writer's API.
- Five-agent audit protocol → repeat at every chapter close that warrants architectural review.

---

## Hold the spine

V2 isn't aesthetic. The algebra isn't ceremonial. Every type theorem (T1, T11, A1) maps to a cutover-blocking property. The seven primitives compound. The chapter pre-scopes are tessellation instances, not arbitrary slice plans.

**V1 ships the cutover. V2 makes it verifiable.** Stage 0 is the moment the algebra becomes types. Land it cleanly; the rest compounds.

Three structural type theorems, one foundation phase, one property-test surface, one triangulation comparator, one fallback ladder, ten chapters. Hold the spine. The rest follows.

— Welcome aboard. Read the surfaces. Write the documentation. Open the first commit.
