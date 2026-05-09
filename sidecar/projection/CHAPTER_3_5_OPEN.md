# Chapter 3.5 open — Π port realization (runway to RefactorLog + CatalogDiff)

**Sessions:** 37 → (in flight). **Posture:** chapter open. **Agent:** kickoff-architecture-improvements pickup.

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter`. Multi-session chapters earn this discipline at chapter open; the OSSYS chapter is the worked precedent. The companion close synthesis lands at `CHAPTER_3_5_CLOSE.md` when this chapter ends.

---

## Why this chapter

Chapter 3.1 closed (`CHAPTER_3_1_CLOSE.md`) with the audit-deferred Tier-1 #7: **three Π emitters return `string` despite `Emitter<'element>` declared in Core**. The declared shape is at `src/Projection.Core/Types.fs:49–63`:

```fsharp
type Emitter<'element> =
    Catalog -> Result<ArtifactByKind<'element>, EmitError>

type EmitterWithProfile<'element> =
    Catalog -> Profile -> Result<ArtifactByKind<'element>, EmitError>

type EmitterOverDiff<'element> =
    CatalogDiff -> Result<ArtifactByKind<'element>, EmitError>
```

`ArtifactByKind` and the four-variant `SsKey` DU are already shipped (Stage 0 + chapter 3.1 close). What is **not** shipped: the three concrete sibling Π's still expose `string`-returning `emit`. T11 (sibling-Π commutativity) is therefore *aspirational* — enforced today by `Assert.Contains` substring discipline at `tests/Projection.Tests/JsonEmitterTests.fs:96–105` and `tests/Projection.Tests/RichProfilingEndToEndTests.fs:280`.

Chapter 3.5's substantive deliverable is **`RefactorLogEmitter` over `CatalogDiff`** (per `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`). The pre-scope's §1 names the pre-condition: `ArtifactByKind<'element>` + `Emitter<'element>` realized for the existing three siblings *before* the diff-typed fourth sibling lands. Otherwise the new emitter ships into a port whose shape was only declared, never inhabited; the asymmetry the chapter is meant to eliminate (T11 prose vs T11 type) survives the chapter that was supposed to retire it.

**This chapter therefore opens with the Π port realization as its first slice arc.** RefactorLog + CatalogDiff inherit the realized seam.

---

## Strategic frame — eight axes named at chapter open

1. **Hexagonal — port realization, not port declaration.** The audit's Tier-1 #7 names *unrealized declared ports* as a leak: closed-DU expansion empirical-test discipline applies to ports too. The fix is either *realize* or *retire*. Three Π's converge on the same shape; this chapter realizes. The `Adapter` alias retired at session 36 was the sister act (no consumer; retire); `Emitter<'element>` is the inverse case (three consumers; realize).
2. **DDD — T11 becomes a type theorem.** Sibling-Π commutativity stops being a substring-search property and becomes a structural consequence of construction. `ArtifactByKind.create catalog slices` enforces strict equality between the slice's keyset and `Catalog.allKinds`'s SsKey set — any two `ArtifactByKind` values built from the same Catalog have equal keysets by construction. The chapter retires the `Assert.Contains` discipline.
3. **FP — composition over the port.** `emit = emitSlices >> Result.map Render.flatten >> Result.map Render.toText` is the natural shape; per-emitter the realization layer composes the per-kind slices into the per-emitter output form. Render is the composition seam, not a kitchen sink.
4. **Streaming — A35 holds at the per-kind boundary.** Per-kind elements are typed (RawText: `Statement list`; Json/Distributions: `string` for first slice; richer per-kind types ladder up under consumer pressure per the two-consumer threshold). The whole-emission stream is reconstructed by `Render.flatten` from the per-kind slices + topological order. A35's deterministic-statement-stream property holds at the per-kind level (each kind's slice is deterministic) and at the catalog level (Render's topological composition is deterministic).
5. **Big-O — per-key composition is O(N log N).** `ArtifactByKind` is `Map<SsKey, _>`; lookup is O(log N), the smart constructor's set-difference is O(N log N) where N = catalog kinds. Per-kind composition through `Render.flatten` is O(N log N) when topological order is needed, O(N) once the order is computed (single pass through the topological sequence; per-key Map lookup amortizes). This is structurally the right shape for chapter-4.4's drift detection, where a pointwise `Map<SsKey, DriftKind>` diff replaces today's byte-string-diff (Appendix H §H.8). The current `string` shape's diff is effectively O(N²) in catalog size for the substring discipline.
6. **Primitives — `Emitter<'element>` is the canonical Π primitive.** Three sibling realizations through one shape. The shape is the structural commitment that makes T11 a type theorem and unblocks chapter 3.5's RefactorLogEmitter (which inhabits `EmitterOverDiff<RefactorLogEntry list>` — the diff-typed sibling) and chapter 4.1's data triumvirate (which inhabits `Emitter<seq<Statement>>` per the Bulk-aware realization).
7. **ACL — the V1↔V2 boundary is unaffected.** The Π port realization is V2-internal; OSSYS adapter, ReadSide, and DACPAC seam unchanged. The chapter's work lives entirely inside `Projection.Core` (no change), `Projection.Targets.{SSDT, Json, Distributions}` (additive `emitSlices` + composition shift in `emit` wrappers), and the test surface (T11 type-theorem property + retired substring tests). No V1↔V2 translation rule changes.
8. **Discoverability / two-consumer threshold — defer richer per-element types until consumer pressure forces.** First slice ships per-kind elements as `Statement list` (RawText), `string` (Json), `string` (Distributions). Richer per-element types (`JsonNode` for Json, `DistributionSlice` for Distributions, `seq<Statement>` for the bulk-aware realization in chapter 4.1) earn their place under consumer pressure — DacpacEmitter (chapter 3.x) and chapter 4.1's data triumvirate are the natural second consumers. Per `DECISIONS 2026-05-13 — Anticipation vs speculation`, Position B (structural alignment when shape is concrete) is what this chapter satisfies.

---

## Slice arc

Per the pre-scope's §5 sequence, refined under empirical pressure (chapter-3.1 close cashed A35 — RawText's natural per-kind element is `Statement list`, not `string`):

| # | Slice | Files | Acceptance |
|---|---|---|---|
| α | `RawTextEmitter.emitSlices : Emitter<Statement list>` + `Render.flattenSlices` | `RawTextEmitter.fs`, `Render.fs`, `RawTextEmitterTests.fs` | T11 by type for RawText; `emit` byte-equivalent to today's output |
| β | `JsonEmitter.emitSlices : Emitter<string>` (per-kind JSON object as text) | `JsonEmitter.fs`, `JsonEmitterTests.fs` | T11 by type for Json; `emit` byte-equivalent to today's output |
| γ | `DistributionsEmitter.emitSlices : EmitterWithProfile<string>` | `DistributionsEmitter.fs`, `DistributionsEmitterTests.fs` | T11 by type for Distributions; `emit` byte-equivalent to today's output |
| δ | T11 type-theorem property tests; retire substring T11 enforcement | `JsonEmitterTests.fs`, `RichProfilingEndToEndTests.fs`, new property module | Three `Assert.Equal (Catalog.allKinds-keys) (ArtifactByKind.keys)` properties green; substring tests deleted |
| ε | Codification | `AXIOMS.md` (T11 amendment cash-out), `DECISIONS.md` (chapter-open + per-slice entries), `HANDOFF.md` (chapter-3.1 → 3.5 letter), `ADMIRE.md` (no V1 hook) | All canonical surfaces aligned |

Sequencing: α → β → γ ship in independent commits (each strictly additive; old `emit` wrappers stay byte-identical). δ retires the substring enforcement once all three slices are green. ε rolls codification at chapter close (placeholder amendments scaffolded at slice α).

The rest of chapter 3.5 — `CatalogDiff.between` smart constructor + `RefactorLogEmitter : EmitterOverDiff<RefactorLogEntry list>` + `Render.toRefactorLogXml` golden — opens after this slice arc lands.

---

## What this chapter does **not** do

Bounded by the chapter's strategic frame:

- **No richer per-kind element types** for Json / Distributions until a second consumer earns them. Two-consumer threshold per `DECISIONS 2026-05-13`.
- **No new ports** (`ICatalogReader`, `IArtifactSink`, `IDeployHost`, `BenchSink`). Routed to chapters 3.2 / 4.x per `HANDOFF.md`'s deferred-but-might-fire list.
- **No type-correspondence module extraction.** Routed to chapter 4.1 prep per audit Tier-1 #8.
- **No identity-DU refactor.** `OssysOriginal`/`V1Mapped`/`Synthesized`/`DerivedFrom` is the shipped four-variant DU; the audit-deferred `SourceTag` parameterization (Tier 2 #13) is chapter 4.2 territory.
- **No CatalogDiff cash-out yet.** That's the chapter's *substantive* deliverable; this is its runway.

---

## Forward signals

After the Π port realization closes (slices α–δ green, ε codified):

- **Chapter 3.5 substantive work opens** — `CatalogDiff.between` + `RefactorLogEmitter` + `Render.toRefactorLogXml` per `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`. Inherits the realized seam.
- **Chapter 3.x DacpacEmitter** (per pre-scope) inherits the structured-output pattern. T11 trivializes for it; the eight risks/open-questions in the pre-scope still apply.
- **Chapter 4.1 data triumvirate** picks up `Emitter<seq<Statement>>` as the bulk-aware realization (per A35/A36).
- **Chapter 4.4 drift detection** picks up `ArtifactByKind.compareWith eq deployed target : Map<SsKey, DriftKind>` as the pointwise-per-key diff.

---

## Operating disciplines re-stated for this chapter

Per `CLAUDE.md` operating-disciplines table (no new disciplines added; chapter-3.1's contributions hold):

- **Closed-DU expansion empirical test** — adding the per-emitter `emitSlices` should produce zero compile errors at consumers (existing `emit` callers untouched).
- **Two-consumer threshold** — per-element types stay simple until a second consumer fires.
- **Writer-fidelity** — N/A here (no pass driver work in this slice arc).
- **Bench-driven optimization** — measure before claiming `Map<SsKey, _>` allocation pressure; the 300-table fixture is the forcing function.
- **AXIOMS amendments scaffolded at chapter open; bodies filled at chapter close** — T11 amendment placeholder added to AXIOMS.md amendment scaffolding.
- **Chapter-mid-audit at every 3–5 substantive sessions** — this chapter's slices are small enough that mid-audit may not fire; if the chapter spans ≥ 5 sessions, dispatch.
- **Chapter-close ritual** at chapter close (eight items + the five-agent epistemic-tier audit if architectural-frame; the Π port realization is borderline — small-enough scope that the five-agent audit may not warrant; the chapter-3.5 substantive arc is more likely the audit-warranting close).

---

## Hold the spine

Three Π's converge on one shape. The shape is the structural commitment that lets the fourth (RefactorLog) ship without ceremony. T11 stops being a discipline and becomes a type theorem. The chapter's first slice arc earns the substantive deliverable that follows.

— Chapter 3.5 open, session 37.
