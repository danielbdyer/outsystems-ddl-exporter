# V2 — Kickoff Brief for Fresh Agents

You're picking up V2 work mid-stream. This brief gets you oriented in 5 minutes; the canonical surfaces it points to brief you in another hour. **Read this first.** Then read the strategic surfaces in the prescribed order. Then write code.

---

## ⭐ Read this before you do anything

**The user codified a supreme operating discipline at chapter 3.5 (2026-05-09)** that **supersedes most other intents for each and every session that follows.** It lives at the top of `DECISIONS.md` as five pillars:

1. **Data-structure-oriented over string-parsing** — typed values flow through; strings emerge ONLY at the absolute terminal BCL writer boundary (`Sql160ScriptGenerator`, `XmlWriter`, `Utf8JsonWriter`).
2. **Avoid string concatenation aggressively** — `sprintf`, `+`, `String.Format`, `String.Concat`, `String.Join`, interpolated strings, and even the V2-internal `StructuredString` builder are all flagged. Every site requires deep per-site analysis: built-in BCL method first, vendor SDK second, typed data structure third, `StructuredString` last (and only with rationale).
3. **Built-in obligation** — when a BCL or vendor SDK emits the structure, agents are *obliged* to use it. `Sql160ScriptGenerator` for T-SQL (production target SQL Server 2022 = compat 160; **not** Sql170ScriptGenerator, which is vNext preview). `XmlWriter` for XML. `Utf8JsonWriter` for JSON. `UuidV5` for RFC 4122 §4.3 GUIDs.
4. **Promised land of FP** — ≥95% pure functions; ≤5% isolated and tested exhaustively (property tests + parse-roundtrip + byte-determinism). Mutation reified at file level via `LINT-ALLOW-FILE-MUTATION`.
5. **Coding-style commitments preserved across sessions** — deep DDD, point-free composition, hexagonal architecture, hardcore FP (closed DUs, smart constructors, no `null`, monadic composition), OOP only at boundary code where BCL forces it, deep separation of concerns, verifiable + observable to the nth degree.

**Default to explicit acknowledgement of deviance.** The lint guardrail (`scripts/lint-discipline.sh`, 26 rules) is the structural enforcement. Every legitimate exception carries a `LINT-ALLOW: <rationale>` (per-line) or `LINT-ALLOW-FILE` / `LINT-ALLOW-FILE-MUTATION` (top-of-file) marker. Pre-commit hook + CI workflow are defense-in-depth.

**Read `DECISIONS.md`'s top section in full before adopting any pattern.**

---

## What this is

You're working on the **F# sidecar (V2) of an OutSystems DDL exporter** at `/home/user/outsystems-ddl-exporter`. V1 is the C# trunk (~78K LOC at `src/`, fully shipping). V2 lives at `sidecar/projection/` — pure F# core plus C# adapters at the boundary, **757 passing tests, 0 skipped**, lint clean across 26 rules. **DECISIONS.md supreme operating discipline carries 7 pillars** (data-structure-oriented; no string-concat; built-in obligation; FP-promised-land; coding-style; no-V2-back-compat; gold-standard-library-precedence + perf-clause).

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
| 8 | **`sidecar/projection/DECISIONS.md`** | Append-only resolved-questions log. **Read the supreme operating discipline at the top FIRST** (codified 2026-05-09; supersedes most other intents). Then read most-recent ten entries. Older entries remain in force unless superseded. | ~7500 |
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
- **Chapter 3.5** (slices α–ω, branch `claude/kickoff-architecture-improvements-Hf6Uw`) **substantively shipped; chapter-close ritual deferred**. RefactorLog + CatalogDiff Π port; UuidV5 (RFC 4122 §4.3 + segment-incremental SHA-1); ScriptDom Sql160 typed-AST emission for SQL bread-and-butter; per-DU `toStructured` / `toDiagnosticString` typed renderers (`StructuredString`); supreme operating discipline codification at top of `DECISIONS.md`; lint discipline expanded to 26 rules; pre-commit hook + CI workflow live; F# 9 nullness-strict tests; ScriptDom parse-roundtrip + determinism property tests. **758 passing tests, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`.**
- **Chapter 3.6** (slices α–ω, branch `claude/review-ddl-exporter-EH1lh`) **substantively shipped; chapter-close ritual deferred**. **Test baseline: 757 passing, 0 skipped, 0 build warnings.** Major deliverables:
  - **`LineageEvent` typed-payload widening** (slices α–γ): `Removed of RemovalReason` typed DU + `Annotated of AnnotationDetail` (5 producer pass drivers + SymmetricClosure skip-reasons). KICKOFF deferrals 4 + 6 closed.
  - **`SsKey.Synthesized` typed segments** (slice δ): `basis: string` → `basisParts: string list`; `String.concat "_"` lives only at terminal `rootOriginal`. KICKOFF deferrals 2 + 3 closed.
  - **Pillar 6 codified**: no V2-internal back-compat paths — refactor fully at time of insight. Cashed out `SsKey.original` parser-shim, `SsKey.derived` aliasing forwarder, `LEGACY` source marker. OSSYS adapter flows through `Module.create` / `Catalog.create`.
  - **Pillar 7 codified**: gold-standard library precedence (use-case-specific lib → typed DU → StructuredString → documented LINT-ALLOW). Plus pillar-7-perf-clause: every refactor cites perf implications; every hot-path function has `Bench.scope`; every loop flows through `Bench` iterators; every counter via `Bench.recordSample`.
  - **`RawValueCodec`**: V2's canonical raw-value format contract; consolidates 3 consumers (Render emit / Bulk parse / ReadSide format).
  - **`ConnectionString.parse`**: typed `SqlConnectionStringBuilder` validation; malformed env vars surface as stderr warning + structured fallback.
  - **ScriptDom expansion**: `createDatabaseSql` + `readRowsStream` (4 sites) + `readRows.SELECT COUNT` flow through `Identifier.EncodeIdentifier`. Adapters.Sql gained ScriptDom dep.
  - **`BatchSplitter` strategy** (TSql160Parser gold-standard with line-fold loud-fallback): `Deploy.executeBatch` flows through `BatchSplitter.splitWithLoudFallback`. ScriptDom failures emit a stderr announcement + record `<label>.scriptDom.fallback.count`. KICKOFF Section-6-irreducible #5 retired.
  - **`EmissionPolicy.create`**: A39 invariant ("at least one artifact family enabled").
  - **`CatalogTraversal.mapKinds` primitive**: extracted from VisibilityMask + NormalizeStaticPopulations (audit Tier-1 #5 cash-out).
  - **`BenchSink` port**: `Bench.persistJson` + `defaultPath` extracted from Core to `Projection.Pipeline.BenchSink` (audit Tier-1 #1 cash-out — F#-pure-core / no-I/O-in-Core restored).
  - **Statistical perf-gate** (`scripts/perf-gate.sh`): rolling history at `bench/history-canary.jsonl` (last N=20); per-label `μ + Kσ` outlier detection (K=3 default); warm-up flat-tolerance fallback. Pre-commit hook auto-runs in ~2s warm; soft-skip on missing Docker/dotnet. Baseline at `bench/baseline-canary.json`.
  - **Stop hook** (`.claude/hooks/perf-gate-stop.sh`): runs perf-gate on every agent stop; surfaces summary via `hookSpecificOutput.additionalContext` so the agent narrates the perf result in its next response.
  - **Iterator-logging discipline codified**: `Bench.scope` added at every pass entry (10 passes); pillar-7-perf-clause makes Bench-everywhere structural, not aspirational.
  - **`Result<'a>` migrated to `Microsoft.FSharp.Core.Result<'a, ValidationError list>`** (alias). Custom DU + `result {}` builder retired. **FsToolkit.ErrorHandling 4.18.0 + .TaskResult adopted**: `result {}` / `taskResult {}` / `validation {}` CEs now native. `DiagnosticSeverity` qualified to `[<RequireQualifiedAccess>]` to clear name collisions with Result.Error. 35 files mass-migrated.
- **Chapter 3.6+ / 3.2 / 4.1+** (you are here) — pre-scopes exist for 3.2 (SnapshotRowsets), 3.3 (DacpacEmitter), 3.4 (canary property surface), 4.1.A/B (data triumvirate), 4.2 (User FK reflow), 4.3 (diagnostics + remediation), 4.4 (SSDT DDL emitter). **Decide chapter sequencing first**.

**The four-environment cutover is the fixed point.** V2 must reach the V2-augmented mode of the fallback ladder by T-30 days from cutover; V2-driver mode is the aspiration. V1 stays warm through cutover+30 days regardless.

---

## Where you are picking up — named deferred backlog (chapter 3.5 close + 3.6 status)

Chapter 3.5 named six deferrals at slice ω close; chapter 3.6 (slices α–ω, this branch) closed five of them. Status as of chapter-3.6 substantive close:

| # | Deferral | Status |
|---|---|---|
| 1 | **Slice χ — F# Analyzers SDK custom analyzer** (`sidecar/projection/Projection.Analyzers/`) | **DEFERRED** — independent standalone project; complements lint-discipline's 26 grep rules with AST-level detection. Re-open trigger: false-negative surfaces in CI. |
| 2 | **`Identity.fs:98` — `SsKey.synthesizedComposite` build path** | **CLOSED** chapter 3.6 slice δ. `Synthesized of source × basisParts: string list`; `String.concat "_"` survives only at terminal `rootOriginal` display projection. |
| 3 | **`Identity.fs:225` — `rootOriginal` Synthesized projection** | **CLOSED** with #2. |
| 4 | **`VisibilityMask.fs:58, 76` — predicate names** | **CLOSED** chapter 3.6 slice α. `LineageEvent.Removed of RemovalReason` typed DU; `Predicate.Name : string` collapsed into `Predicate.Reason : RemovalReason`. |
| 5 | **`StructuredString.fs:51, 89` — typed renderer's residual `String.concat ""`** | **DEFERRED** — stopgap acknowledged; eliminating requires a structured-diagnostic-projection surface (typed BCL writer like XmlWriter/Utf8JsonWriter carrying typed payload). Re-open trigger: typed-diagnostic-projection chapter opens (4.x candidate). |
| 6 | **Pass-driver `interventionId -> outcomeLabel`** | **CLOSED** chapter 3.6 slices β + γ. `LineageEvent.Annotated of AnnotationDetail` typed DU across 4 intervention pass drivers + SymmetricClosure skip-reasons. |

**Chapter 3.6 also addressed cross-cutting work** (per `DECISIONS.md` 2026-05-09 chapter-3.6 entries):
  - **`Bulk.parseRaw → Result<obj>`** (audit Top-10 #2): **SKIPPED with documented perf reasoning** — at 100k records × ~10 cells/row = 1M cell parses, Result wrapping adds ~16 MB GC pressure on the hot bulk path; existing exception-to-Report at deploy boundary already structurally surfaces failures. Pillar 7 perf-clause example.
  - **`splitOnGo` (audit Section 6 #5)**: **CLOSED** via `BatchSplitter` strategy — TSql160Parser gold-standard with line-fold loud-fallback. Section-6-irreducible → Section-1-tightened.

**Recorded for future agents** (full audit findings + library-API queue at `DECISIONS.md` 2026-05-09 chapter-3.6 audit-findings entry): DacFx (chapter 3.x DacpacEmitter), SqlBatch (when SqlClient ≥ 5.5 + canary bottleneck), `SqlConnection.RetryLogicProvider` (canary CI flake), AsyncSeq (when streaming readside needs `bufferByCountAndTime`), `JsonObject` typed per-kind (when 2nd consumer of `ArtifactByKind<string>` needs typed manipulation), Argu (when CLI grows beyond 3 commands), Verify.XUnit (when DacpacEmitter golden-file rotation pressure fires), `Microsoft.Extensions.Logging` (when CI consumer demands structured logs), `Utf8JsonReader` (when bench surfaces JSON parse time).

**Disposition:** items 2, 3, 4, 6 are coupled (all gated on widening `LineageEvent.Annotated of string` → typed payload + `Synthesized` DU shape refactor). They earn one chapter together — call it Chapter 3.6 (LineageEvent typed-payload widening + Identity.Synthesized typed-segments refactor). Items 1 and 5 are independent and can land standalone.

**The lint guardrail's `LINT-ALLOW: <rationale>` markers** at each of these sites carry structural acknowledgement. Grep `rg "LINT-ALLOW" sidecar/projection/src/` to find every reified deviation; the rationale strings are deliberately uniform (`round-trip pair`, `terminal diagnostic projection`) so the deferred-backlog scan is structural, not narrative.

---

## What you'll do first

Stage 0 + chapters 3.1 and 3.5 (substantive) are **closed**. Your first move is to **either pick up the named deferrals above** (Chapter 3.6 — LineageEvent typed-payload widening + Identity.Synthesized typed-segments refactor) **or decide the next forward chapter**.

### Step 1 — Orient (~45 minutes)

Read in this order:
1. **`DECISIONS.md` — supreme operating discipline at the top** (codified 2026-05-09; five pillars; supersedes most other intents). This is non-negotiable groundwork.
2. `HANDOFF.md` — what's load-bearing, what's deferred.
3. `CHAPTER_3_1_CLOSE.md` — chapter-3.1 arc summary, four meta-codifications, forward signals.
4. `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` — Tier 1/2/3/4 backlog from the chapter-close audit; ~20 items routed to named sub-chapters.
5. The most recent ~20 `DECISIONS.md` entries — chapter 3.5's slices α–ω cluster at the bottom (codified the supreme operating discipline; reified-primitive pattern; built-in obligation; extended lint discipline).
6. `scripts/lint-discipline.sh` — 26 grep-based rules; pre-commit hook runs `--ci` on staged V2 files; CI workflow at `.github/workflows/lint-projection.yml`. Run `bash sidecar/projection/scripts/install-hooks.sh` to install the pre-commit symlink.

### Step 2 — Pick the next chapter (or pick up the deferred backlog)

**Strongly recommended first slice: Chapter 3.6 — LineageEvent typed-payload widening + Identity.Synthesized typed-segments refactor.** This unblocks deferrals 2, 3, 4, 6 from the named backlog above; converts every `LineageEvent.Annotated of string` site to typed payload; eliminates the three remaining `String.concat "_"` Identity-rendering sites; touches A1/A4 axioms and ~50 test fixtures. Largest leverage on string-aversion alignment.

Other plausible next chapters, each with a current pre-scope:

- **Chapter 3.2 — `SnapshotRowsets` adapter.** Closes the JSON-projection-lossiness class. Smaller scope. Lifts `ICatalogReader` port (Position B trigger has fired). Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`.
- **Chapter 3.x — DacpacEmitter.** Re-deferred at chapter-2 close. Inherits chapter-3.5's typed `ArtifactByKind<'element>` pattern. Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`.
- **Chapter 4.1 — Data triumvirate (StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter).** Inherits chapter 3.1's `Bulk` / `RowDigester` / `AsyncStream` and chapter 3.5's typed-statement-stream emission via `Sql160ScriptGenerator`. Pre-scope at `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`.
- **Slice χ — F# Analyzers SDK custom analyzer** (independent; deferral #1). Standalone project; complements the 26 grep-based lint rules with AST-level structural detection.

### Step 3 — Open the chapter

Open with a chapter-open document naming the strategic-frame axes (`DECISIONS 2026-05-15` shape; the OSSYS chapter is the worked example). Multi-session chapters earn this discipline at chapter open. Operate the chapter-mid-audit at every 3–5 substantive sessions; operate the chapter-close ritual at chapter close (eight items + the new five-agent audit for architectural-frame chapters).

### How chapter 3.5 ended (substantive close; ritual deferred)

**Test count:** 758 passing, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true` everywhere (production AND tests). Lint clean across 26 rules.

**Substantive deliverables shipped (slices α–ω):**
- `CatalogDiff` exhaustive partition (Renamed/Added/Removed/Unchanged) with smart constructor (A38 cash-out).
- `UuidV5.create` (RFC 4122 §4.3) + `UuidV5.createFromSegments` (typed byte-segment incremental SHA-1).
- `RefactorLogEmitter` over `EmitterOverDiff<RefactorLogEntry list>` Π port.
- `RefactorLogRender` via `XmlWriter` with pinned `XmlSettings.indentedUtf8NoBom()` (built-in obligation).
- `Sql160ScriptGenerator` typed-AST emission (`ScriptDomBuild` + `ScriptDomGenerate`) for SQL bread-and-butter; production target SQL Server 2022 = compat 160.
- `ArtifactByKind<'element>` smart constructor with strict-equality keyset enforcement (T11 structural-type-encoding cash-out).
- Per-DU `toStructured` / `toDiagnosticString` typed renderers across every Outcome / KeepReason / Evidence / Conflict in `NullabilityRules` / `UniqueIndexRules` / `ForeignKeyRules` / `CategoricalUniquenessRules`.
- `LineageBuffer` reified opaque accumulator (typed-private DU wrapping `List<LineageEvent>`).
- `PinnedWriting.JsonOptions` / `XmlSettings` reified BCL option-builders.
- `DatabaseNameGenerator` reified non-determinism boundary (eliminates `Guid.NewGuid()` leaks).
- 7 Skip-stub tests retired across 5 test files; 9 + 8 + 8 + 9 + 15 + 4 new property/example tests added (CatalogDiff / UuidV5 / RefactorLogEmitter / RefactorLogRender XDocument structural / ScriptDomRoundTrip / T11TypeTheorem).

**Disciplines codified at chapter 3.5:**
1. **Supreme operating discipline (5 pillars)** at top of `DECISIONS.md` — supersedes most other intents per session.
2. **Built-in obligation** — when a BCL/vendor SDK emits the structure, agents are obliged to use it.
3. **Reified-primitive pattern** — opaque accumulators (`LineageBuffer`), reified BCL option-builders (`PinnedWriting`), reified non-determinism boundaries (`DatabaseNameGenerator`).
4. **Extended lint discipline** — 26 rules across string-aversion (6) / mutation (4) / determinism (4) / core purity (4) / FP strict (4) / Big-O (1) / hexagonal (3); `LINT-ALLOW: <rationale>` per-line and `LINT-ALLOW-FILE` / `LINT-ALLOW-FILE-MUTATION` top-of-file allowlists; pre-commit hook + CI workflow defense-in-depth.
5. **Deep per-site analysis** — every concatenation site requires explicit consideration of (a) BCL built-in, (b) vendor SDK, (c) typed data structure, BEFORE falling back to `String.concat` with `LINT-ALLOW`.

**AXIOMS cash-outs:** A38 (CatalogDiff exhaustiveness); T11 (structural-type encoding via `ArtifactByKind<'element>`).

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

- **Branch:** `claude/kickoff-architecture-improvements-Hf6Uw` (verify with `git status`; check `git log --oneline -10` for context — last commit `4ea171a` retired 7 Skip-stubs and added 3 string-aversion lint rules).
- **Commit individual slices.** Never batch unrelated changes.
- **Follow the chapter-close ritual** at every chapter close (eight items per `CLAUDE.md` operating-disciplines table; `DECISIONS 2026-05-14`). **Chapter 3.5's chapter-close ritual is deferred** — pick it up before opening Chapter 3.6 if appropriate.
- **Before pushing:** `dotnet test` should be green (758 tests, 0 skipped). `bash sidecar/projection/scripts/lint-discipline.sh --ci` must exit 0 (26 rules; the pre-commit hook also runs this on staged V2 files).
- **Perf-regression gate:** `bash sidecar/projection/scripts/perf-gate.sh` runs the canary against `fixtures/canary-gate.sql`, captures the bench JSON, and compares per-label `TotalMs` against the committed `bench/baseline-canary.json`. Any label exceeding the baseline by more than `BENCH_TOLERANCE` (default 1.5×) blocks the commit. The pre-commit hook runs this automatically (~2s warm); soft-skips when Docker / dotnet are unavailable. Re-record the baseline with `PERF_GATE_RECORD=1 bash sidecar/projection/scripts/perf-gate.sh` when the perf floor legitimately changes (pair with a DECISIONS amendment naming the new floor's rationale). Per the iterator-logging-as-first-class-outcome discipline (CLAUDE.md operating disciplines table; chapter 3.6 cash-out), the bench surface is V2's perf evidence; this gate makes regression detection structural rather than aspirational.
- **Bypassing the lint guardrail** via `git commit --no-verify` is the explicit-deviance escape hatch; CI catches bypasses on PR. Don't bypass without writing the DECISIONS amendment first.
- **Commit message format:** follow the existing pattern in `git log --oneline` (each commit is `sidecar/projection: chapter <N> <slice tag> — <action>`). Multi-paragraph body explaining the *why*, not just the *what*. Sign with the Claude Code session URL.

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

**End of first session (chapter-decision):** chapter-open document for the picked sub-chapter (3.2 / 3.6 / 3.x / 4.1 / slice χ) with strategic-frame axes named. Existing 758 tests green; 0 skipped; lint clean across 26 rules. The audit-deferred items routed to the picked sub-chapter sketched as concrete first-slice plans.

**End of next sub-chapter (~5-10 sessions):** the picked sub-chapter's substantive deliverable shipped. AXIOMS amendments filled at close. Chapter-close ritual operated (eight items + five-agent audit if architectural-frame chapter). Forward signals cluster identified for the chapter after that.

**Cutover-quarter trajectory:** chapters 3.2 / 3.6 / 3.x close (V2-augmented mode operational). Chapter 4.1 → 4.4 close (V2-driver mode possible per T-30 gate). Cutover proceeds environment-by-environment. V1 sunset deferred until all four environments run V2 emissions for one full schema-evolution cycle.

**Chapter-3.1's structural inheritance to the chapters ahead:**
- Π output as typed stream → chapters 3.5 (shipped) / 4.1 / 3.x inherit the seam.
- `Bulk` / `RowDigester` / `AsyncStream` substrate → chapter 4.1 fills the data triumvirate emitters.
- `Coordinates.TableId` value object → chapter 4 / 5 extend with `SchemaName` / `TableName` / `ColumnName` Stage-2.
- Aggregate-root smart constructors → chapter 4.2 User FK reflow consumes the integrity invariants.
- Writer-fidelity discipline → all future passes operate the dual-writer's API.
- Five-agent audit protocol → repeat at every chapter close that warrants architectural review.

**Chapter-3.5's structural inheritance to the chapters ahead:**
- Supreme operating discipline (5 pillars) → all sessions confirm intent against each pillar before adopting any pattern.
- `ArtifactByKind<'element>` smart constructor → all sibling Π's land their typed output through it (T11 structural).
- `Sql160ScriptGenerator` typed-AST emission → chapters 4.1 / 4.4 inherit the SQL-emission seam (built-in obligation).
- `XmlWriter` / `Utf8JsonWriter` pinned-settings reified primitives → all future XML/JSON emission paths inherit `PinnedWriting`.
- Lint discipline (26 rules + pre-commit + CI) → all future code lands lint-clean by default; deviations carry `LINT-ALLOW` markers.
- Per-DU `toStructured` / `toDiagnosticString` typed renderers → all future Outcome / KeepReason / Evidence DUs ship with the same surface.

---

## Hold the spine

V2 isn't aesthetic. The algebra isn't ceremonial. Every type theorem (T1, T11, A1) maps to a cutover-blocking property. The seven primitives compound. The chapter pre-scopes are tessellation instances, not arbitrary slice plans.

**V1 ships the cutover. V2 makes it verifiable.** Stage 0 is the moment the algebra becomes types. Land it cleanly; the rest compounds.

Three structural type theorems, one foundation phase, one property-test surface, one triangulation comparator, one fallback ladder, ten chapters. Hold the spine. The rest follows.

— Welcome aboard. Read the surfaces. Write the documentation. Open the first commit.
