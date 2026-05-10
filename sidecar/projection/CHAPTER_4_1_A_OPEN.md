# Chapter 4.1.A open — `Projection.Targets.SSDT.SsdtDdlEmitter`

**Sessions:** opens with this document. **Posture:** Phase 2 of V2-driver KPI critical path (per `V2_DRIVER.md`). **Predecessors:** chapter 3.5 (Π port realization + RefactorLog + CatalogDiff substantively shipped); chapter 3.6 (LineageEvent typed payloads + Pillars 6+7 codified); chapter 3.7 (audit-cleanup hygiene + LINT-ALLOW substantive-rationale + domain-first naming + Json/Distributions Π typed elements + V2-driver destination KPI codified). All three prior chapters' close rituals roll into this chapter's mid-audit + close.

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter` (multi-session chapters earn this discipline at chapter open). The companion close synthesis lands at `CHAPTER_4_1_A_CLOSE.md` when this chapter ends. **Operational pre-scope:** `CHAPTER_4_PRESCOPE_SSDT_DDL_EMITTER.md` — the implementation-grade plan with §1 scope, §2 architecture, §3 impedance, §4 per-file structure, §5 refactor-log integration, §6 determinism, §7 EmissionPolicy interaction, §8 slice-by-slice breakdown, §9 test strategy, §10 V1 differential, §11 risks, §12 dependencies. This open document does not re-derive the pre-scope; it names the chapter-level strategic axes under V2-driver KPI.

---

## Why this chapter

Chapter 4.1.A delivers **the V2 SSDT DDL emitter** — a sibling Π whose output is per-table `.sql` files arranged exactly as V1 currently emits them at `<outDir>/Modules/<Module>/<Schema>.<Table>.sql`, plus a sibling `<outDir>/manifest.json`. The artifact is the **production-deployment surface** that V2 owns under V2-driver mode (per `V2_DRIVER.md` Phase 2): the bytes that promote into the operator's existing Azure DevOps pipeline.

Pre-V2-driver-KPI codification, this chapter was framed as "schema-as-driver: V2 emits SSDT DDL diffable against V1." Post-codification (`DECISIONS 2026-05-10 — V2-driver as destination KPI`), the framing sharpens: V2 emits production SSDT DDL bytes; the canary verifies them; per-environment-per-artifact-type R6 governance flips authority from V1 to V2 once N=10 consecutive green canary runs + operator sign-off support the flip. The chapter owns the schema axis end-to-end.

---

## Strategic frame — eight axes named at chapter open

Per the OSSYS chapter precedent (`DECISIONS 2026-05-15`), multi-session chapters name their load-bearing axes at chapter open before substantive slices begin.

1. **DDD — `SsdtFile` typed value at the Π port surface.** The per-kind value is a typed record (`{ RelativePath; Body }`) carrying both the cross-platform-deterministic relative path AND the rendered SQL body. Pillar 1 (data-structure-oriented) holds: typed values flow through; the string emerges only at the absolute terminal `Sql160ScriptGenerator` boundary. The per-kind seam is `ArtifactByKind<SsdtFile>` — concept-shaped (the file IS the artifact); not a generic `Map<string, string>`.
2. **FP — Π port realization complete on the production axis.** `emitSlices : Emitter<SsdtFile>` extends the chapter-3.5 + chapter-3.7 typed-Π-port lineage to the production deploy surface. T11 sibling-Π commutativity becomes empirically testable across **four** Π emitters (RawText `Statement list` + Json `JsonNode` + Distributions `JsonNode` + SSDT-DDL `SsdtFile`). Pillar 7: the SQL body flows through ScriptDom's typed AST (`Statement.CreateTable` → `ScriptDomBuild.buildStatement` → `ScriptDomGenerate.generateOne`); no `String.Concat` at the emission site.
3. **Hardcore (no-string-concatenation) — RelativePath is the only string-composition site.** The relative path uses cross-platform-deterministic forward slashes (Path.Combine considered + rejected because platform-specific separators violate T1 byte-determinism); the LINT-ALLOW marker at the path-construction site embodies the four-question analysis per the pillar-7 substantive-rationale amendment.
4. **Streaming — slice-by-slice scope; no canary-scale streaming concern.** Schema artifacts are bounded (~300 tables = ~300 SsdtFile values; small footprint). Bench observability via `Bench.scope "emit.ssdt.emitSlices"`; no per-row streaming primitive needed at this slice. The chapter-3.1 streaming substrate (AsyncStream, RowDigester) applies to chapter 4.1.B (data triumvirate), not 4.1.A (schema).
5. **Hexagonal — F# core never touches the file system.** The emitter produces an in-memory `Map<RelativePath, string>` via `Render.toSsdtDirectory` (slice 10 composition; chapter-3.5 precedent: Π's typed output stream + realization layers as sibling consumers). A C# / F# Pipeline host consumes the map and writes the files; that's a downstream realization, not Π's job. Per A35: Π's canonical output is a typed deterministic *map* of typed values; realization layers (file write, archive bundle, deploy invocation) consume the map.
6. **Built-in obligation — ScriptDom's `Sql160ScriptGenerator` IS the SQL DDL emitter.** Per pillar 7's gold-standard library precedence: `SsdtDdlEmitter` does not compose SQL via `String.Concat`; it builds the typed `Statement.CreateTable` AST (already exists in `Statement.fs` from chapter 3.5) and routes through `ScriptDomBuild.buildStatement` + `ScriptDomGenerate.generateOne`. The chapter-3.7 slice β' precedent (Render.columnSqlType through ScriptDom typed AST) extends here naturally: the WHOLE statement, not just the type expression, flows through ScriptDom.
7. **Aggregate-root + smart constructor — `ArtifactByKind<SsdtFile>` Π port surface.** The smart constructor enforces strict-equality keyset (T11 structural; chapter 3.5 cash-out). Per the V2-driver KPI: schema fidelity verification rests on T11 structural correctness; the SSDT DDL emitter is the fourth sibling that proves the property under real production-shape catalogs.
8. **Test-fidelity — golden file + T11 vs RawTextEmitter + T1 byte-determinism.** Per the per-axis correctness stakes table in `V2_DRIVER.md`: schema axis verification depth is "high; mostly shipped (chapter 3.1)." This chapter completes the schema axis by adding the production-output emitter; tests assert (a) byte-determinism (T1), (b) T11 keyset agreement with RawTextEmitter, (c) golden-file SQL body matches expected for the slice's fixture catalog.

---

## Slice arc

Per the chapter pre-scope §8, ten substantive slices ordered by IR-grows-under-evidence. Each slice ships independently with its own commit; each slice gets a goal, fixture, test, file footprint, LOC estimate, acceptance criterion (per `CHAPTER_4_PRESCOPE_SSDT_DDL_EMITTER.md` §8 for the full breakdown).

| # | Slice | Pre-scope reference | LOC budget |
|---|---|---|---|
| 1 | Single-table catalog: schema + table emission (CREATE TABLE only; no FKs, no indexes, no modality) | §8 slice 1 | ~120 src + ~80 test |
| 2 | Multi-attribute formatting + composite types (every PrimitiveType variant) | §8 slice 2 | ~30 src + ~20 test |
| 3 | Indexes (single-column, non-unique and unique) | §8 slice 3 | ~50 src + ~30 test |
| 4 | Composite primary keys | §8 slice 4 | ~30 src + ~30 test |
| 5 | Intra-module FKs (inline) | §8 slice 5 | ~60 src + ~50 test |
| 6 | Cross-module FKs (parity-divergence accepted; gated on chapter 3.2) | §8 slice 6 | ~20 src + ~50 test |
| 7 | Identity columns + default constraints (gated on chapter 3.2) | §8 slice 7 | ~40 src + ~40 test |
| 8 | Extended properties (gated on chapter 3.2) | §8 slice 8 | ~50 src + ~40 test |
| 9 | Manifest emitter (`manifest.json` per V1 schema) | §8 slice 9 | ~150 src + ~80 test |
| 10 | Refactor-log composition + post-deploy split (`Render.toSsdtDirectory`) | §8 slice 10 | ~80 src + ~60 test |

**Total: ~720 LOC source + ~480 LOC tests.** Per the V2_DRIVER.md Phase 2 budget (~1 week at session cadence).

This document opens with **slice 1** ready to ship next; subsequent slices proceed in sequence, with slices 6/7/8 gated on chapter 3.2 (SnapshotRowsets) per the pre-scope §8 / §12.

---

## What this chapter does **not** do

Bounded by the strategic frame and the V2-driver KPI sequencing:

- **No CDC-aware data inserts.** Chapter 4.1.B's data triumvirate (StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter) is the next phase. Chapter 4.1.A emits no `INSERT` statements; static populations route to the post-deploy script the data triumvirate emits.
- **No User FK reflow.** Chapter 4.2's `UserFkReflowPass` is Phase 4. Chapter 4.1.A emits FKs structurally per `Reference.TargetKind`; per-environment User remapping is downstream of the emit.
- **No DACPAC.** Chapter 3.x DacpacEmitter is Phase 6 (conditional on deploy path). T11 sibling-Π commutativity between SSDT and DACPAC becomes testable when both ship; until then, T11 across the four current Π emitters (RawText / Json / Distributions / SSDT-DDL) is the structural surface.
- **No SnapshotRowsets identity stabilization.** Chapter 3.2 is Phase 7. Slices 6/7/8 of this chapter are gated on it (cross-module FK resolution; identity columns; extended properties); they ship as `[<Skip>]` stubs until the gating dependency arrives.
- **No file-system writes.** F# core never touches the FS; chapter 4.1.A produces in-memory `Map<RelativePath, string>` via slice 10 composition. The downstream host (Projection.Pipeline or Projection.Cli) writes the files.
- **No EmissionPolicy decisions in the emitter.** Per A18 amended, Π consumes Catalog × Profile but not Policy. EmissionPolicy modes (`AllSchema` / `AllData` / `AllRemaining`) are consumed by an `EmissionPolicyPass` that produces the Catalog the emitter sees; the emitter walks whatever Catalog it receives.

---

## Forward signals

After chapter 4.1.A closes (slices 1-10 green; canary verifies V2 SSDT directory ≈ V1 SSDT directory modulo named tolerances):

- **Phase 3 (chapter 4.1.B) opens** — CDC-critical data triumvirate. The CDC-silence-on-idempotent-redeploy property test is the highest-leverage single deliverable in the entire V2-driver chapter sequence (per `V2_DRIVER.md`).
- **Tolerance taxonomy (M4) operationalized** — chapter 4.1.A's slice 10 includes per-environment quotient configuration; subsequent chapters consume the same Tolerance surface for their respective axes.
- **Multi-environment promotion property test landed** — chapter 4.1.A's slice 10 + integration test exercises four-pair flow on at least two environments.
- **R6 governance per-environment-per-artifact-type flip becomes operational** — the Tolerance taxonomy + canary integration test + N=10 consecutive green runs per environment together compose the gate; per-env per-artifact-type V2-driver authority flips become operator-decidable.
- **V2 SSDT DDL becomes a real diff source for V1↔V2 dogfooding** — per pre-scope §1 fourth bullet: "the seam V2 verifies V1 across." Once V2 emits the same shape, the canary can compare V2's emitted directory vs V1's emitted directory as a pure-text differential, no testcontainers needed.

— Chapter 4.1.A architect (sessions opening this date).
