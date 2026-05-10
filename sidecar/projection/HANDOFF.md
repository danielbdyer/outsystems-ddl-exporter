# Handoff letter — Chapter 3.1 → Chapter 3.2 / 3.5 / 3.6 / 3.7 / 4.x

To the next-chapter agent. Read this before anything else in the V2 sidecar. It is short on purpose.

The chapter-1 and chapter-2 handoff letters are preserved at `HANDOFF_CHAPTER_1.md` and `HANDOFF_CHAPTER_2.md` adjacent to this file. Read them after this one if you want the prior architects' framings.

## Chapter 3.7 prologue (added 2026-05-10; in flight, audit-cleanup hygiene)

**Branch:** `claude/review-ddl-exporter-ilV0k`. **Test baseline:** 790 passing, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (Rule 27 added this chapter; see below). **Perf-gate:** clean.

Chapter 3.7 is a **B&W audit-cleanup hygiene chapter** picking up Tier-1 / Tier-2 / Tier-3 audit findings still open at chapter-3.6 close. Substantive deliverables shipped (load-bearing):

- **Slice α — `Lineage.Trail [<CustomEquality>]` (A26 cash-out)**. Audit Tier-2 #12. `Lineage<'a>` projects equality through `Value` only; trails are metadata not in equality. `Lineage.byValue` / `Lineage.byValueAndTrail` helpers expose the explicit projections. Monad-laws property tests + operator tests strengthened to `byValueAndTrail`. Two new property/Fact tests cash out A26 directly. Pass / PassWithDiagnostics aliases inherit the `'output : equality` constraint. +30 LOC.
- **Slice β — `Projection.Core.SqlTypeCorrespondence` bounded context (Tier-1 #8 cash-out)**. The forward / inverse PrimitiveType ↔ SQL DDL vocabulary pair previously split across `Projection.Targets.SSDT.Render.columnSqlType` (forward) and `Projection.Adapters.Sql.ReadSide.mapSqlType` (inverse) is consolidated into one closed-DU dispatch surface in Core. Round-trip property + 25 InlineData theory + Fact + property test sweep the recognized SQL Server alias vocabulary. ReadSide.mapSqlType becomes a 1-line alias.
- **Slice β' — `Render.columnSqlType` through ScriptDom typed AST (pillar 7 cash-out)**. Slice-β shipped with four `String.Concat` LINT-ALLOWs in Render that named the boundary without naming the considered alternative — *performance-of-compliance* (the named failure mode; see below). Slice-β' lifted `ScriptDomBuild.dataTypeReference` from `private` to public, added `ScriptDomGenerate.generateDataType : DataTypeReference -> string`, made `Render.columnSqlType` delegate. Output byte-identical (790 tests still green); four LINT-ALLOWs retired; two private helpers retired (`sqlTypeWithLength`, `sqlDecimal`); one unused import retired (`open System.Globalization`). Per-column generator instantiation surfaces via bench label `scriptDom.generateDataType` (perf-gate clean).
- **Slice β'' — LINT-ALLOW substantive-rationale discipline codification**. `DECISIONS 2026-05-10` codifies the four-question analysis as the structural prerequisite for any per-line `LINT-ALLOW` marker on a string-composition / built-in-substitute site. Names the failure mode **performance-of-compliance** (a marker shaped like an audit trail without the substance). Updates: pillar 7 amendment in DECISIONS.md supreme operating discipline section; new operating-disciplines table row in CLAUDE.md; expanded LINT-ALLOW guidance in root AGENTS.md; new sub-bullet in KICKOFF.md supreme-discipline section; new decision tree "When you reach for a string-composition primitive" in PLAYBOOK.md; lint Rule 27 added (per-line concat-aversion LINT-ALLOW inventory + soft floor).

**Outstanding chapter-3.7 slice queue** (committed in todo list; in user-preferred order):
- **γ** — `traverseCatalog` natural-transformation primitive (audit Tier-3 #23; FP composition).
- **ε** — Json + Distributions Π typed per-kind value (audit Tier-1 #7; T11 fully structural; pillar 1).
- **ζ** — Three `attach` adapters take string JSON → SnapshotSource-shaped (audit Tier-1 #6).
- **η** — `result {}` CE adoption at `ReadSide.fs:540-690` (audit Tier-3 #24).
- **θ** — Coordinates Stage 2 typed `SchemaName` / `TableName` / `ColumnName` VOs (audit Tier-3 #20a).
- **ι** — Lineage / Diagnostics writer-monad codification refresh (audit Tier-2 #18 + #19).
- **κ** — `Lineage.tell` `m.Trail @ [event]` O(N²) audit (perf-class question).
- **λ** — `SsKey.rootOriginal` V1 prefix in emitter output (audit Tier-1 #11; needs DECISIONS amendment first).
- **μ** — `Restrict→NoActionSql` Diagnostics scaffolding (audit Tier-1 #10 + Tier-2 #15).
- **ν** — F# Analyzers SDK custom analyzer (KICKOFF deferral #1; complements 27 grep rules with AST detection).
- **ξ / ο / π** — Port lifts (`ICatalogReader` / `IArtifactSink` / `IDeployHost`); ξ likely lands as part of forward chapter 3.2.

Chapter close ritual still deferred.

---

## Chapter 3.6 prologue (added 2026-05-09; substantive close, ritual deferred)

**Branch:** `claude/review-ddl-exporter-EH1lh`. **Test baseline:** 757 passing, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 26 rules. **Perf-gate:** clean against `bench/baseline-canary.json` baseline.

Chapter 3.6 closed five of the six chapter-3.5-close-deferred items (KICKOFF.md table) plus a comprehensive brittleness audit + library-API audit + Result migration. **Substantive deliverables shipped (load-bearing):**
- **Pillar 6 codified** (no V2-internal back-compat paths). Cashed out `SsKey.original` parser-shim, `SsKey.derived` aliasing forwarder, `LEGACY` source marker, CLI bare-positional-args back-compat. OSSYS adapter flows through `Module.create` / `Catalog.create`.
- **Pillar 7 codified** (gold-standard library precedence + perf-clause). Every refactor SHALL cite perf implications; every hot-path function has `Bench.scope`; every loop flows through `Bench` iterators; every counter via `Bench.recordSample`.
- **`LineageEvent.Removed of RemovalReason` + `Annotated of AnnotationDetail`**: typed-payload widening across 5 producer pass drivers + SymmetricClosure.
- **`SsKey.Synthesized of basisParts: string list`**: typed segments through the DU; `String.concat "_"` survives only at terminal `rootOriginal`.
- **`RawValueCodec`**: V2's canonical raw-value format contract; consolidates Render + Bulk + ReadSide.
- **`ConnectionString.parse`**: typed `SqlConnectionStringBuilder` validation.
- **`BatchSplitter` strategy**: `TSql160Parser` gold-standard with line-fold loud-fallback for `Deploy.executeBatch`.
- **`EmissionPolicy.create`**: A39 invariant.
- **`CatalogTraversal.mapKinds` primitive**: extracted from VisibilityMask + NormalizeStaticPopulations.
- **`BenchSink` port**: `Bench.persistJson` extracted from Core (audit Tier-1 #1).
- **Statistical perf-gate** (`scripts/perf-gate.sh`) + **pre-commit hook** + **Stop hook** (`hookSpecificOutput.additionalContext`): per-label `μ + Kσ` outlier detection across rolling history (N=20); soft-skip on missing Docker/dotnet.
- **`Result<'a>` aliased to `Microsoft.FSharp.Core.Result<'a, ValidationError list>`**: custom DU + `result {}` builder retired. **FsToolkit.ErrorHandling 4.18.0** adopted; `result {}` / `taskResult {}` / `validation {}` CEs now native. `DiagnosticSeverity` qualified.
- **ScriptDom expansion** at boundary sites (`createDatabaseSql`, `readRowsStream`, `readRows.SELECT COUNT`): single source of truth for SQL identifier quoting via `Identifier.EncodeIdentifier`.
- **Bench coverage at every pass entry** (10 passes; iterator-logging-as-first-class-outcome discipline).

**Sole 3.6-deferral remaining:**
- **Slice χ — F# Analyzers SDK custom analyzer** (KICKOFF deferral #1; standalone). Independent of chapter-3.5 deferrals; re-open trigger: false-negative surfaces in CI.

**3.6 chapter-close ritual still pending** (eight items per CLAUDE.md operating disciplines table): pick this up before opening Chapter 3.7 / 4.x if appropriate.

**Forward signals into 3.7+ / 4.x** (recorded for future agents at `DECISIONS.md` 2026-05-09 chapter-3.6 audit-findings entry): DacFx adoption (chapter 3.x DacpacEmitter — primitives named: `TSqlModel`, `DacPackage`, `DacServices.GenerateDeployScript`, `SchemaComparison`, `DacDeployOptions`), SqlBatch (when SqlClient ≥ 5.5 + canary bottleneck), `SqlConnection.RetryLogicProvider` (canary CI flake), `AsyncSeq` (when streaming readside needs `bufferByCountAndTime`), `JsonObject` typed per-kind (when 2nd `ArtifactByKind<string>` consumer fires), Argu CLI (when CLI grows beyond 3 commands), Verify.XUnit (DacpacEmitter golden-rotation pressure), `Microsoft.Extensions.Logging` (CI structured-logs consumer demand), `Utf8JsonReader` (bench surfaces JSON parse time at scale), incremental `validation {}` adoption at `CatalogReader.parseAttribute / parseKind` for ~80 LoC reduction + better error aggregation, incremental `taskResult {}` adoption at `Deploy.runWideCanaryWithLoader` for ~40 LoC reduction.

**Pillar 7 perf-clause practice**: agents SHALL cite perf implications in every commit message and SHALL identify the perf class before committing (zero / O(1) / O(N) / O(N log N) / O(N²) — with the scaling axis).

---

## Where you are

You have inherited three closed chapters (1, 2, 3.1) of the V2 sidecar. The most recent close — chapter 3.1, the canary chapter — accumulated through sessions 27 → 36. Chapter 3.1's substantive synthesis is at `CHAPTER_3_1_CLOSE.md`; the chapter's epistemic capstone is `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` (the five-agent DDD/Hexagonal/FP audit).

The codebase builds; **all 713 tests pass (697 non-canary + 16 canary)**; the canary's round-trip property holds across the 300-table forcing-function fixture and at 500k rows on the bulk path (27s warm).

You are not starting from scratch. You are continuing a multi-chapter arc whose accumulated judgment is partly in the canonical documents (`AXIOMS.md`, `DECISIONS.md`, `ADMIRE.md`) and partly in `CHAPTER_1_CLOSE.md` / `CHAPTER_2_CLOSE.md` / `CHAPTER_3_1_CLOSE.md` / `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` next to this letter.

## What to read, in order

1. **`CLAUDE.md`** — the navigation surface. Indexes the canonical documents and lists the operating disciplines. Start here.
2. **`HANDOFF.md`** (this file) — the bridge between what chapters 1+2+3.1 know and what you need to know.
3. **`CHAPTER_3_1_CLOSE.md`** — chapter-3.1 close synthesis. Sections of immediate relevance:
   - The chapter-3.1 arc summary (M1–M3 milestone sequence; forcing-function scaling; data plane + at-scale + audit).
   - The four meta-codifications (bench-driven optimization, stream-realization pattern, five-agent epistemic-tier audit, harmonization-via-parameterization).
   - The forward signals into chapter 3.2 / 3.5 / 4.1 / 4.2.
4. **`AUDIT_2026_05_DDD_HEXAGONAL_FP.md`** — the chapter-3.1 close audit. Tier 1 / 2 / 3 / 4 backlog organized by epistemic level (B&W vs SUBJ) and leverage (H/M/L). ~30 findings; 10 acted on at session 36; ~20 routed to named sub-chapters.
5. **`CHAPTER_2_CLOSE.md`** — chapter-2 close synthesis. Read for OSSYS adapter context (25 translation rules, three-class typology, V1-input-envelope walk).
6. **`CHAPTER_1_CLOSE.md`** — historical context. Some priorities resolved by chapter 2 / 3.1; disciplines and load-bearing commitments persist.
7. **`AXIOMS.md`** — the algebra. A1–A40 with V2 amendments appended. **Note:** A35 / A36 cashed at session 34; A37–A40 candidates added at chapter 3.1 close (Coordinates VO, aggregate constructors, harmonization-via-parameterization, writer-fidelity).
8. **`DECISIONS.md`** — chronological operating discipline. Long. Read the most recent ten entries first; chapter-3.1-close entries cluster at the bottom.
9. **`ADMIRE.md`** — V1↔V2 bridge. `EntityDependencySorter` advanced to (advanced — consumed via `TopologicalOrderPass.runWith`); `EntitySeedDeterminizer` (advanced — `StaticRow.Values` raw IR contract closes the loop).
10. **The code.** `Projection.sln`. Strategies in `src/Projection.Core/Strategies/`; passes in `src/Projection.Core/Passes/`; sibling Π emitters in `src/Projection.Targets.{SSDT,Json,Distributions}/`; F# adapters in `src/Projection.Adapters.{Sql,Osm}/`; the canary in `src/Projection.Pipeline/`. Chapter 3.1's substantive deliverables: `Statement.fs` / `Render.fs` / `RawTextEmitter.fs` (typed Π output); `Bulk.fs` / `Deploy.fs` (bulk realization); `AsyncStream.fs` / `ReadSide.fs:readRowsStream` (streaming readside); `Coordinates.fs` (`TableId` value object); `PhysicalSchema.fs` (four-axis fidelity surface).

## What's load-bearing

These commitments are not negotiable without explicit DECISIONS entries amending them. If you find yourself wanting to break one, write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** `Projection.Core` has zero I/O. Audited clean (`CHAPTER_1_CLOSE.md §1.1`); confirmed across chapters 2, 3.1. Chapter 3.1 audit Agent 2 #1 flagged `Bench.persistJson` as an outstanding violation; ⏸ deferred (rolls forward as `BenchSink` port extraction).
- **A18 amended.** Π consumes whichever subset of `Catalog × Profile` it needs, but never `Policy`. Catalog and Profile are *evidence*; Policy is *intent*. If you reach for Policy from inside an emitter, you are in the wrong layer — the work belongs in a pass.
- **A35 (chapter-3.1 contribution).** Π's output is a deterministic *statement stream*, not a string. Realization layers (`Render.toText`, `Deploy.executeStream`) consume the stream and choose their emission form. Bulk-vs-incremental deploy is realization-layer policy invisible to Π.
- **A36 (chapter-3.1 contribution).** Bulk-vs-incremental is realization-layer policy. The algebra (A18, T1, T11) holds at the stream level; how a realization deploys is its own concern.
- **Strategy-layer codification (`DECISIONS 2026-05-11`).** Pure functions of IR fields; typed function-type seam (`StrategyEvaluator<'context, 'config, 'decision>`); structured rationale DUs; lineage events on actual decisions; module name advertises domain (`<Domain>Rules` suffix); total decisions with named skips.
- **`Composition.fanOut` for registered-intervention pass drivers.** All pass drivers delegate to it.
- **Closed-DU expansion empirical-test discipline.** Adding a DU variant should produce F# exhaustiveness errors only at match sites; no caller reshaping outside the variant's module.
- **Decimal as default for continuous statistical evidence.** T1 byte-determinism requires it.
- **Sibling-Π commutativity (T11).** Every Π's output should mention every catalog kind by SsKey root. **Note:** T11 is currently *aspirational* not structural — three Π's return `string`. Chapter 3.5's Π port realization makes T11 structural via the typed `ArtifactByKind<'element>` surface.
- **Pass return-type codification.** Passes return `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` when producing decisions plus observer-relevant findings. Chapter 3.1's writer-fidelity codification adds: pass drivers MUST use `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` (the canonical primitives); manual record-building is forbidden.
- **Three-class typology for V1↔V2 translation findings (chapter-2 contribution).** Lossiness / boundary-discipline / alternative-IR-surface. Chapter 3.2 is a translation chapter — operate the typology.
- **Five-agent epistemic-tier audit protocol (chapter-3.1 contribution).** Multi-agent parallel audit at chapter close; convergence map as primary synthesis surface. Tier 1/2/3/4 backlog organizes findings by epistemic level + leverage.
- **Harmonization-via-parameterization (chapter-3.1 contribution).** When two implementations diverge on a single axis, parameterize the algorithm on that axis. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass`.

## What's deferred but might fire under your work

The Active deferrals index at the top of `DECISIONS.md` is the canonical surface; chapter-mid-audits and chapter-close ritual scan it. If your work surfaces the cash-out trigger, log a DECISIONS entry — don't quietly resolve the deferral.

**Chapter-3.5-likely fires:**
- **Π port realization** — three emitters return `string`; `Emitter<'element>` declared but unrealized. Chapter 3.5 RefactorLog is the natural new consumer that earns the typed structured output. Cross-cutting blocker for T11 structural-type encoding.
- **`SsKey.rootOriginal` V1 prefix in emitter output** — needs a DECISIONS amendment first to supersede the chapter-3 pre-scope §3 commitment that the source-prefix form is the stable identifier.
- **`Restrict → NoActionSql` collapse explicit** — paired with a Diagnostics-emission scaffolding for `CatalogReader`. Chapter 3.5 RefactorLog needs this for round-trip-fidelity diff.

**Chapter-3.2-likely fires:**
- **`SnapshotRowsets` variant of `SnapshotSource`** — closes the JSON-projection-lossiness class. Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`.
- **`ICatalogReader` port** — Position B trigger has fired (two consumers: `Osm.CatalogReader.parse` + `Sql.ReadSide.read`). Chapter 3.2 lifts the surface.
- **Three `attach` adapters take `string` JSON** — should mirror `SnapshotSource` shape. Hidden ports.
- **Silent V1 drops without Diagnostics** — chapter 3.2 adds the Diagnostics-emission scaffolding to `CatalogReader`.

**Chapter-4.1-likely fires:**
- **Type-correspondence module** — owns the 5 inverse functions (`mapSqlType` ↔ `columnSqlType`; `formatRawValue` ↔ `formatSqlLiteral`; `parseRaw` + `clrType`). T1 byte-determinism currently rests on conventional inversion across 3 projects. Chapter 4.1's data triumvirate (StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter) is where this lift earns its place.
- **`Bulk` lives in `Pipeline`** but is structurally `Adapters.Sql` concern. Move at chapter 4.1 open.
- **`IDeployHost` port** — wraps Testcontainers + warm-conn + executeStream. Chapter 4.1's data emitters need this seam to swap between live SQL and ephemeral container.
- **Streaming digest cash-out** — `RowDigester` / `PhysicalRowDigest` shipped at chapter 3.1 as scaffolding; chapter 4.1's data triumvirate uses them at scale.

**Chapter-4.2-likely fires:**
- **Identity DU refactor** — `OssysOriginal` / `V1Mapped` parameterized on `SourceTag` value object. The `V1Mapped` variant is reserved for cross-source identity threading; today unreachable from production. Chapter 4.2's User FK reflow makes it reachable.

**Cross-cutting cleanup (any chapter):**
- **`Bench.persistJson` writes from Core** — `BenchSink` port; Bench out of Core.
- **`IArtifactSink` port** — `Compose.write` + `Bench.persistJson` reach `File.WriteAllText` directly.
- **`Lineage.Trail` `[<CustomEquality>]`** — A26 documented but not enforced (default `=` compares Trail).
- **Typed `SchemaName` / `TableName` / `ColumnName` VOs** — Stage 2 of Coordinates.
- **`traverseCatalog` natural-transformation primitive** — 4 consumers hand-rolling mutable `ResizeArray<LineageEvent>` traversals.
- **`result { ... }` computation expression** — `ReadSide.fs:540–690` chains 4–5 deep, beyond the codebase's "bearable three steps" mark.

**Lower-priority (watch for accidental fires):**
- **Composition primitives `fallback`, `accumulate`, `wrap`, `lift`** — zero current consumers each.
- **Strategy registry mechanism** — N=5 strategies; threshold N≥4–6 plus a real consumer demanding name-keyed lookup.
- **Three-channel Diagnostics split** — single channel sufficient at all chapter-2/3.1 consumers.
- **Faker emitter** — gates on third evidence type.

## What you should not do

The accumulated judgment from sessions 1–36 includes specific don'ts:

- **Don't strip "dead code" without checking docstrings.** `ForeignKeyRules.isIgnoreRule` always returns false; `ForeignKeyKeepReason.CrossCatalogBlocked` is reserved-unreachable; `Origin.ExternalDirect` is unreachable from OSSYS today (chapter 4 SnapshotRowsets / DACPAC may reach it). All intentional.
- **Don't delete the OSSYS adapter's deferred `SnapshotSource` variants.** `SnapshotRowsets` and `LiveOssysConnection` are reserved DU variants with explicit re-open triggers. They appear unused until chapter 3.2+; do not delete.
- **Don't treat `RawTextEmitter` as an SSDT replacement.** It is a debug/diff-oracle synthetic-milestone form. DacpacEmitter (chapter 3.x) is the additive sibling, not a replacement.
- **Don't extract speculative composition primitives.** Two-consumer threshold; refined by anticipation-vs-speculation (`DECISIONS 2026-05-13`); refined again by `opportunityEntry` shape-distinction analysis (`DECISIONS 2026-05-14`).
- **Don't reach for Policy from a Π.** A18 amended forbids it.
- **Don't bypass the writer's API.** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` are the canonical pass-driver primitives. Manual record-building is forbidden.
- **Don't cash out Active deferrals silently.** The DacFx trigger fired silently across sessions 18–22 and was caught only at session-23 chapter-mid-audit. The lesson is structural: **the index exists so it doesn't recur**.
- **Don't open new substantive slices without classifying the finding into the three-class typology first.** Trace-before-fixture; classify; resolution shape follows.
- **Don't build ports without consumer pressure.** The session-36 audit identified ports declared in Core (`Emitter`, `Compare`, `Render`) that are unrealized. The fix isn't to add more declarations — it's either to *realize* with a real consumer (chapter 3.5 Π port) or to *retire* the declaration (the `Adapter` alias retired at session 36 with no consumer). Closed-DU expansion empirical-test discipline applies to ports too.
- **Don't overwrite this file.** When chapter 3.2 / 3.5 / 4.x closes, this letter becomes `HANDOFF_CHAPTER_3_1.md` and you write the new outgoing letter as `HANDOFF.md`. Append-only documentation discipline.

## Disposition

The dispositions chapter 3.1 inherited from chapters 1–2 hold; chapter 3.1 added these:

- **Bench-driven optimization protocol.** Three-candidate / 2-refuted / 1-confirmed shape; refuted swaps documented with bench data so the same swap doesn't recur.
- **Stream-realization pattern.** Π's typed output stream + realization layers as sibling consumers. Same algebra; multiple realizations.
- **Five-agent epistemic-tier audit at chapter close.** Multi-agent parallel; convergence-map as primary surface; Tier 1/2/3/4 backlog discipline.
- **Harmonization-via-parameterization.** Single-axis-divergent implementations earn one parameterized algorithm.
- **Writer-fidelity discipline.** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` canonical; manual record-building forbidden.
- **Audits roll forward as named sub-chapters.** Chapter 3.1's audit (Tier-1 / Tier-2 / Tier-3) didn't dump 30 findings into the next chapter — it routed each finding to the natural sub-chapter (3.2 / 3.5 / 4.1 / 4.2) with explicit pre-scope alignment. The discipline: audit findings are routed, not piled.

## Where to start

Per `CHAPTER_3_1_CLOSE.md`, the chapter-3.2 / 3.5 / 4.1 priorities split based on what unlocks T-30 first (per `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`):

1. **Read this letter, CLAUDE.md, CHAPTER_3_1_CLOSE.md, AUDIT_2026_05_DDD_HEXAGONAL_FP.md, and the recent DECISIONS entries** — orient. ~45 minutes.

2. **Decide chapter sequencing.** The four plausible next chapters:
   - **Chapter 3.2 — `SnapshotRowsets` adapter.** Closes the JSON-projection-lossiness class. Smaller scope. Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`. Lifts `ICatalogReader` port (Position B trigger fired).
   - **Chapter 3.5 — Π port realization + RefactorLog / CatalogDiff.** Largest leverage. Realizes the declared `Emitter<'element>` shape; unblocks T11 structural-type encoding. Pre-scope at `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`. Pairs naturally with the audit-deferred Π-port-realization.
   - **Chapter 3.x — DacpacEmitter.** Re-deferred at chapter-2 close. Inherits chapter-3.5's structured-output pattern. Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Eight risks/open-questions still open.
   - **Chapter 4.1 — Data triumvirate.** Pre-scope at `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`. Inherits `Bulk` / `RowDigester` / `AsyncStream` from chapter 3.1.

   The pre-scope documents are still current. Subagent #4's (DacpacEmitter) and subagent #5's (SnapshotRowsets) recommendations from chapter-2 close hold; the chapter-3.1 audit sharpens them with the Π-port-realization framing.

3. **Open the chapter you choose** with a chapter-open document naming the strategic-frame axes (`DECISIONS 2026-05-15` shape; the OSSYS chapter is the worked example). Multi-session chapters earn this discipline at chapter open.

After the chapter-open scoping, the substantive work begins. Operate the chapter-mid-audit at every 3–5 substantive sessions; operate the chapter-close ritual at chapter close (eight items, including the V1-input-envelope walk for V1↔V2 translation chapters and the new five-agent audit for architectural-frame chapters).

## Closing

You inherit a codebase whose architectural disciplines hold under audit (the chapter-3.1 audit was the strongest one yet — five agents, ~30 findings, codified as `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`), whose codification is at multiple stability marks, whose audit disciplines have proven generative (each audit produces new disciplines), and whose canonical documents are honest about what's been done and what hasn't.

Chapter 3.1's distinctive intellectual artifact: **the canary as a load-bearing forcing function**. The 300-table fixture is the verification surface for the V1↔V2 round-trip property. Chapter 3.1's distinctive operational artifact: **typed Π output as a stream, with realization plurality**. Chapter 3.1's load-bearing structural innovation: **harmonization-via-parameterization** — single-axis-divergent implementations earn one parameterized algorithm.

The chapter you open is yours to shape. The disciplines above are not constraints; they are the load-bearing structure that lets the chapter ahead support more weight than the one behind. Hold the spine.

— The session 27–36 architect.
