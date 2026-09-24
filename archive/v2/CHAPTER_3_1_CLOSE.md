# Chapter 3.1 close — the canary chapter

**Sessions:** 27 → 36 (10 substantive sessions).
**Thesis:** the canary as a forcing function — V2 emits, deploys to ephemeral SQL Server, reads back, and compares against source. The round-trip property holds at 300 tables / 500k rows / six-axis empty diff.
**Outcome:** chapter 3.1 closes at session 36. M1–M3 milestones hit; M4 (Tolerance taxonomy) and M5 (R6 governance flip) deferred to chapter 3.5 / 3.x by design. Audit dispatched and synthesized; first refactor batch landed; remainder rolls forward as named sub-chapters.

## Strategic frame

Chapter 3.1 was scoped at session 27 as the canary milestone sequence — five milestones (M1 through M5) that progressively closed the V2-internal closure (M1), deploy semantics (M2), wide-canary structural fidelity (M3), Tolerance-modulo equivalence (M4), and R6 governance flip (M5). M1–M3 land in chapter 3.1; M4–M5 carry forward.

The chapter inherited from chapter 2:
- The Diagnostics writer (chapter 2 deliverable; codification stable mark held).
- The OSSYS adapter producing V2 Catalog from V1 JSON (25 translation rules).
- The three-class typology for V1↔V2 translation findings.
- The chapter-mid-audit + trace-before-fixture + V1-input-envelope-walk disciplines.

The chapter produced these substantive surfaces:
- A typed `Statement` DU as Π's canonical output form (sessions 34, 36).
- A bulk realization layer (`Bulk.copyRows` + `Deploy.executeStream`) hitting 43k rows/sec on V2's deploy path (sessions 34, 35).
- A streaming readside (`AsyncStream` + `readRowsStream` + first-class stream observability via `Bench.streamProbe` / `AsyncStream.probe`) (session 34).
- A `PhysicalSchema` projection with four axes (Columns, ForeignKeys, Rows, RowDigests) for structural-fidelity comparison (sessions 33, 35).
- A procedural `FixtureGenerator` with the 300-table forcing-function fixture and bulk-source loader (sessions 31, 33, 35).
- An integrated bench observability surface (`Bench.scope` / `Bench.iterDo` / `Bench.iterMap` / `Bench.streamProbe`) instrumented across 170 call sites (session 30 + session-34 stream extension).
- Coordinates value-object context (`TableId` lifted to Core; `[schema].[table]` rendering centralized) (session 36).
- Aggregate-root smart constructors (`Catalog.create` / `Module.create`) enforcing referential integrity in one pass, errors aggregated (session 36).
- Writer-fidelity codification (`LineageDiagnostics.tellDiagnostics` adopted at three pass drivers; `Lineage.ofValueAndEvents` extracted at 6 sites) (session 36).

## Sessions arc

The chapter ran in three sub-arcs.

### Arc 1: M1–M3 milestone sequence (sessions 27–28)
Built the canary milestones in sequence:
- **M1:** V2-internal closure — programmatic `Catalog → emit → deploy → read-back → compare`.
- **M2:** Deploy semantics — ephemeral SQL Server via Testcontainers; warm-container fast lane via `PROJECTION_MSSQL_CONN_STR`.
- **M3:** Wide canary — source DDL → readback → V2 emit → deploy → readback → compare. PhysicalSchema diff is the comparison primitive (per `DECISIONS 2026-05-23 — Source SQL Server with OutSystems semantics is the canary's primary wide integration surface`).

### Arc 2: forcing-function scaling (sessions 29–32)
Pushed canary against realistic shape and scale:
- **Session 29:** Bench observability layer (RAII `scope` + iterator combinators); bench summary at SessionEnd hook so cross-session regression is structural.
- **Session 30:** Phase-1 instrumentation across emitters / readside / PhysicalSchema (170 call sites); bench-driven optimization protocol — three optimization candidates tried, two refuted, one confirmed (countUserTables eliminated; sys.* + MARS reverted per bench data).
- **Session 31:** Procedural fixture generator (small/medium/realistic/enterprise specs); inline FK emission + topological emission order replacing two-pass FK pattern.
- **Session 32:** Full type fidelity — IR carries `Length` / `Precision` / `Scale` / `IsIdentity`; round-trip closes the NVARCHAR(N) / DECIMAL(P,S) / IDENTITY axes.

### Arc 3: data plane + at-scale + audit (sessions 33–36)
The chapter's substantive deliverable arc:
- **Session 33:** Data plane — `PhysicalSchema.Rows` axis (SHA256-hashed); `StaticRow.Values` raw IR contract; ReadSide reads row data; Π emits INSERT statements.
- **Session 34:** Typed statement-stream Π output (cash A35); bulk realization layer (cash A36); streaming readside (`AsyncStream` + `readRowsStream`); first-class stream observability.
- **Session 35:** Enterprise-scale polish — 500k rows in 27s warm (was 610s); bulk fixture loader; streaming digest scaffolding (`RowDigester` + `PhysicalRowDigest`); Big-O wins (ResizeArray accumulators, `Result.aggregate`, HashSet diff, SHA256.HashData, parallel hashing, lifted FK Maps).
- **Session 36:** Five-agent DDD/Hexagonal/FP audit; first refactor batch (B&W subset) landed: writer fidelity, `Coordinates.TableId` consolidation, `SelfLoopPolicy` parameterization (topological-sort harmonization), `Adapter` alias retired from Core, `Catalog.create` aggregate constructor, `ColumnProfile.create` invariant, Docker JIT bring-up.

## Meta-codifications

Chapter 3.1 produced four meta-codifications worth carrying forward:

### 1. Bench-driven optimization protocol (session 30)
Performance optimizations are no longer guesses. The protocol:
1. Three candidates tried with full bench data.
2. Each candidate either confirmed (faster + tested) or refuted (data-driven decision to revert).
3. Refuted candidates documented with bench data so the same swap doesn't recur.

Codified at `DECISIONS 2026-05-24 — Bench-driven optimization discipline`. Worked examples in chapter 3.1: countUserTables elimination (confirmed); sys.* readside join (refuted); MARS + parallel readside (refuted).

### 2. Stream-realization pattern (session 34)
Π's output is a deterministic typed *stream*, not a string. Realization layers (text rendering, deploy execution, file artifacts) are sibling consumers of the same stream. The algebra (A18, T1, T11) holds at the stream level; bulk-vs-incremental deploy is realization-layer policy invisible to Π.

Codified as **A35** (Π's output is a deterministic statement stream) and **A36** (bulk-vs-incremental is realization-layer policy). The pattern unblocks chapter 3.5's RefactorLog (different realization of the same stream) and chapter 4.1's data triumvirate (BCP/TVP realization of the seed-data stream).

### 3. Five-agent epistemic-tier audit protocol (session 36)
Multi-agent parallel audit dispatched at chapter close. Each agent:
- Carries a tightly orthogonal lens (UL / Hex / VO / FP / ACL).
- Tags findings B&W (objective leak) vs SUBJ (judgment call).
- Ranks findings H / M / L for refactor leverage.
- Synthesis tracks multi-axis confirmation as confidence signal.

Convergence-map (multi-agent overlap) is the synthesis primary surface — not a footnote. Tier 1/2/3/4 backlog organizes the findings by epistemic level + leverage so the operator knows which require their judgment before action.

Worked example: the session-36 audit (preserved at `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`) produced ~30 findings; 10 acted on at session 36 (B&W ship-without-ceremony subset); ~20 rolled forward to chapter 3.5 / 4.1 / 4.2 with explicit pre-scope alignment.

### 4. Harmonization-via-parameterization pattern (session 36)
When two implementations of an algorithm diverge on a single axis (e.g., `TopologicalOrderPass` treats self-loops as cycles; `RawTextEmitter.emissionOrder` skips them), the resolution is to *parameterize the algorithm* on the divergence axis (`SelfLoopPolicy = TreatAsCycle | SkipSelfEdges`), produce both projections from a single algorithm, and let consumers choose. Same algorithm, multiple projections.

This is the FP-flavored answer to the OOP "two specializations of one base class" pattern. It earns its place when the two implementations were structurally identical except for the one axis — which is the codification stability mark for *which* divergences deserve parameterization vs distinct algorithms.

## Forward signals into chapter 3.5 / 4.x

The chapter-mid audits and the chapter-close audit (session 36) surfaced these forward signals:

### Chapter 3.2 — SnapshotRowsets adapter (per `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`)
Closes the JSON-projection-lossiness class. SsKey at every level via the OSSYS rowset shape. Reserved DU variant (`SnapshotSource.SnapshotRowsets`) ready to extend. Pre-scope still current.

### Chapter 3.5 — Π port realization + RefactorLog / CatalogDiff (per `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`)
**Π port realization** is the chapter-3.1 audit's largest deferred item. Three emitters return `string`; the declared `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>` shape is unrealized. Aligning the three emitters to typed structured output (e.g., `seq<Statement>` for SSDT, `JsonNode` AST for Json, `DistributionRow seq` for Distributions) with `Render` as the per-emitter realization step makes T11 sibling-Π commutativity *structural* rather than substring-search-tested. The RefactorLog emitter is the natural new consumer that earns the realization. Chapter 3.5 opens here.

### Chapter 3.x — DacpacEmitter (per `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`)
The DacFx integration. Re-deferred at chapter-2 close; chapter 3.5's Π-port realization establishes the structured-output pattern that DacpacEmitter inherits. Pre-scope still current; subagent #4's eight risks/open-questions remain open.

### Chapter 4.1 — Data triumvirate (per `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`)
StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter. Chapter 3.1's `RowDigester` + `Bulk.copyRows` + `AsyncStream.batchesOf` are the substrate; chapter 4.1 fills in the emitters. The `Deploy.executeStream` realization is already bulk-aware.

### Chapter 4.2 — User FK reflow (per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`)
Cross-source identity threading. The `SsKey.V1Mapped` variant is reserved for this; today it's unreachable from production input (chapter 3.1 audit Agent 5 F14). Chapter 4.2 makes it reachable. The session-36 audit's Identity-DU refactor (replace V1-named variants with parameterized `SourceTag` value object) is the natural prep slice; lands at chapter 4.2 open.

### Cross-cutting forward signals (audit Tier-1 / Tier-2 deferrals)
Per `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`, the deferred audit items split across the chapters above:
- Π port realization → chapter 3.5
- Identity DU refactor → chapter 4.2
- Type-correspondence module → chapter 4.1 prep
- IArtifactSink / BenchSink / IDeployHost ports → chapter 4 cross-cutting (not chapter-specific; ship when consumer pressure forces)
- `SsKey.rootOriginal` V1 prefix in emitter output → needs DECISIONS amendment first
- Lineage.Trail `[<CustomEquality>]` → small fix; chapter 4 hygiene
- Typed `SchemaName` / `TableName` / `ColumnName` VOs → chapter 4 / 5 (Stage 2 of Coordinates)
- `ICatalogReader` port — Position B trigger has fired; chapter 3.2 lifts the surface
- Three `attach` adapters take string JSON → chapter 4 hygiene
- Restrict-collapse Diagnostic emission → paired with item below
- Silent V1 drops without Diagnostics → chapter 3.2 territory (paired with adapter Diagnostics scaffolding)

## Test count + canary numbers at close

| Surface | Count |
|---|---|
| Tests passing | 713 (697 non-canary + 16 canary, bulk1k/10k/100k inclusive) |
| Skipped (deliberate V2 divergence / reserved) | 7 |
| Canary scale ceiling validated | 500k rows in 27s warm (5 tables × 100k rows) |
| Forcing-function fixture | 300 tables / 200 entities / 100 static / 2000 seed rows / FK chains across 8 modules |
| Bench scopes instrumented | ~170 call sites across Core / Adapters / Targets / Pipeline |

## Disposition / load-bearing commitments inherited or refined

Chapter 3.1 holds the chapter-1 + chapter-2 commitments and adds:
- **A35 / A36 cash-out (sessions 34, 36).** Π's output is a deterministic statement stream; bulk-vs-incremental is realization-layer policy. T1 strengthens to statement-level determinism; T11 commutativity holds at the stream level.
- **Coordinates context Stage 1 (session 36).** `TableId` lifted to Core with smart constructor. `PhysicalRealization = TableId`. `[schema].[table]` rendering is one canonical function.
- **Aggregate-root smart constructors (session 36).** `Catalog.create` / `Module.create` enforce 5 referential-integrity invariants at construction; back-compat preserved.
- **Writer-fidelity (session 36).** `LineageDiagnostics.tellDiagnostics` is the canonical pass-driver constructor; manual record-building forbidden. `Lineage.ofValueAndEvents` is the canonical "value + trail" primitive.
- **Bench-driven optimization protocol (session 30).** Three-candidate / 2-refuted / 1-confirmed shape; refuted swaps documented with bench data.
- **Stream-realization pattern (session 34).** Π's typed output stream + realization layers as sibling consumers.
- **Five-agent epistemic-tier audit protocol (session 36).** Multi-agent parallel audit at chapter close; convergence map as primary synthesis surface.
- **Harmonization-via-parameterization (session 36).** Single-axis-divergent implementations earn one parameterized algorithm.

## Closing

Chapter 3.1's distinctive intellectual artifact: **the canary as a load-bearing forcing function**. The 300-table fixture is not a benchmark — it's the verification surface that closes the V1↔V2 round-trip property. The bench surface tracks scaling so regressions surface structurally; the audit-driven refactor protocol turns chapter close into a discipline-generating event.

The chapter's distinctive operational artifact: **Π's output is a typed deterministic stream, with realization-layer plurality**. RefactorLog (chapter 3.5), data triumvirate (chapter 4.1), DacpacEmitter (chapter 3.x) all inherit the seam. The pattern is the chapter's load-bearing inheritance to the chapters ahead.

The chapter's load-bearing structural innovation: **harmonization-via-parameterization**. Two algorithms with single-axis divergence collapse to one parameterized algorithm. The codebase produced this pattern from session-36's audit-driven refactor; the worked example is `SelfLoopPolicy` in `TopologicalOrderPass`. The pattern is named here so future chapters reach for it before duplicating.

The audit (preserved at `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`) is the chapter's epistemic capstone. The codebase was self-consistent against its own framework. The Tier-1 findings were real leaks against stated commitments, not cosmetic. ~30 findings; 10 shipped; ~20 routed to named sub-chapters with pre-scope alignment.

Hold the spine.

— The session 27–36 architect.
