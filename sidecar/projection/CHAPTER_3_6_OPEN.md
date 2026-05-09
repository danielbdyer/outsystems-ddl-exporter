# Chapter 3.6 open — LineageEvent typed-payload widening + Identity.Synthesized typed-segments refactor

**Sessions:** 38 → (in flight). **Posture:** chapter open. **Agent:** review-ddl-exporter pickup.

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter`. Multi-session chapters earn this discipline at chapter open; the OSSYS chapter is the worked precedent. The companion close synthesis lands at `CHAPTER_3_6_CLOSE.md` when this chapter ends.

The chapter-3.5 close ritual is **deferred** at chapter open per `KICKOFF.md` ("Chapter 3.5's chapter-close ritual is deferred — pick it up before opening Chapter 3.6 if appropriate"). It rolls into chapter 3.6's close ritual; the audit-routed deferrals from chapter 3.1 (`AUDIT_2026_05_DDD_HEXAGONAL_FP.md`) that this chapter cashes out are listed below.

---

## Why this chapter

Chapter 3.5 (`KICKOFF.md` named-deferred backlog) closed substantively but left **six string-concatenation residuals** at the producer side of `LineageEvent`:

| # | Site | Producer | Residual |
|---|---|---|---|
| 2 | `Identity.fs:98` | `SsKey.synthesizedComposite` | `String.concat "_"` over typed `string list` (build path) |
| 3 | `Identity.fs:225` | `rootOriginal` Synthesized projection | `String.concat "_"` over typed 2-element list (display path) |
| 4 | `VisibilityMask.fs:58, 76` | `hideOrigin` / `hideModality` | `String.concat ""` to build `Predicate.Name : string` payload for `LineageEvent.Removed of string` |
| 6 | 5 pass drivers (Nullability / UniqueIndex / ForeignKey / CategoricalUniqueness / SymmetricClosure) | `Annotated of string` payload | `String.concat ""` over `[interventionId; " -> "; outcomeLabel]` for `LineageEvent.Annotated of string` |

Items 4 + 6 are coupled: both are gated on **widening `LineageEvent.Annotated` and `LineageEvent.Removed` from `string` payload to typed payload**. Items 2 + 3 are coupled: both are gated on **refactoring `SsKey.Synthesized of source: string * basis: string` to carry typed segments through the DU**. The closed-DU expansion empirical-test discipline (`DECISIONS 2026-05-13`) bounds the blast radius — match sites in match-only modules; ~50 test fixtures touched.

The survey (this session): `LineageEvent.Removed` and `LineageEvent.Annotated` have **0 structural consumers in `src/`** (no pattern-match outside their producer pass drivers). All ~13 string-payload assertions live in `tests/`. The widening is therefore strictly additive at the producer side and structurally tractable at the test side.

---

## Strategic frame — eight axes named at chapter open

1. **DDD — `LineageEvent` payloads become typed value objects.** `Removed of string` → `Removed of RemovalReason`; `Annotated of string` → `Annotated of AnnotationDetail`. Provenance is preserved structurally, not by string formatting. The convention (filtering passes name the predicate that fired; intervention passes name the decision outcome) is reified as DU variants, not string conventions.
2. **FP — closed-DU exhaustiveness replaces ad-hoc string parsing.** Audit readers, future dashboards, tests pattern-match exhaustively. The closed-DU expansion empirical-test discipline applies: adding a new filtering pass / intervention introduces a new variant; F# exhaustiveness errors light up only at consumer match sites.
3. **Hardcore (no-string-concatenation) — every `String.concat`/`""` site at producer collapses.** `VisibilityMask.fs:58, 76` (deferral 4) → DU constructors. Pass drivers' `interventionId + " -> " + outcomeLabel` (deferral 6) → typed `AnnotationDetail` record. `Identity.fs:98, 225` (deferrals 2, 3) → typed `string list` carried through `Synthesized` DU; `String.concat "_"` survives ONLY at the terminal `Synthesized.toIdentifier` projection with `LINT-ALLOW: terminal text-emission boundary`. After the chapter, the only `String.concat` sites in production are at the StructuredString stopgap (deferral 5; deferred to a future architectural chapter that widens diagnostic projection to typed BCL writers).
4. **Streaming — same algebra; no perf regression.** Trail size unchanged; per-event payload widens by a typed DU header (no allocation pressure beyond what the displaced strings caused). Big-O stable.
5. **Hexagonal — Core owns the typed payload; renderers/diagnostics own string projection.** `RemovalReason.toDiagnosticString : RemovalReason -> string` and `AnnotationDetail.toDiagnosticString : AnnotationDetail -> string` live in Core for boundary-emission consumers; the typed payload is the structural form. No string emerges at the producer; strings emerge at the rendering boundary, on demand.
6. **Built-in obligation — typed DU IS the structure.** No `StructuredString` builder needed at the lineage producer site. `String.concat ""` over a typed 2-element list is structurally equivalent to a 2-tuple; the chapter replaces it with the tuple (or a record) directly.
7. **Aggregate-root — `SsKey.Synthesized` becomes a typed aggregate carrying its provenance fragments.** `Synthesized of source: string * basisParts: string list` (or richer typed fragments). The typed segments are the structural commitment; `String.concat "_"` survives only at the terminal `Synthesized.toIdentifier` display projection — and only because the legacy parser at `SsKey.original` operates the round-trip pair.
8. **Test-fidelity — assertions become pattern-match on typed payload.** ~13 assertion sites widen from `Assert.Equal "origin=External" reason` to `match reason with | OriginPredicate Origin.External -> () | _ -> Assert.Fail _`. Compiler-checked exhaustiveness; structural fidelity; same behaviors.

---

## Slice arc

| # | Slice | Files | Acceptance |
|---|---|---|---|
| α | `LineageEvent.Removed of RemovalReason` typed-payload widening | `Lineage.fs`, `Passes/VisibilityMask.fs`, `tests/Projection.Tests/VisibilityMaskTests.fs` | VisibilityMask producer eliminates 2× `String.concat ""` (deferral 4); 6 test assertions pattern-match typed; lint clean; tests green |
| β | `LineageEvent.Annotated of AnnotationDetail` typed-payload widening (4 intervention pass drivers) | `Lineage.fs`, `Passes/{Nullability,UniqueIndex,ForeignKey,CategoricalUniqueness}Pass.fs`, corresponding `*PassTests.fs` | 4 producer sites eliminate `String.concat ""`; per-pass tests pattern-match typed payload; deferral 6 (intervention path) closed |
| γ | `Annotated` widening for `SymmetricClosure` skip-reason | `Passes/SymmetricClosure.fs`, `tests/Projection.Tests/SymmetricClosureTests.fs` | 1 producer site widens to typed `SkipReason` variant under `AnnotationDetail`; deferral 6 (closure path) closed |
| δ | `SsKey.Synthesized of source × basisParts: string list` typed-segments refactor | `Identity.fs`, ~50 test fixtures | `String.concat "_"` survives only at `Synthesized.toIdentifier` (terminal display); deferrals 2, 3 closed |
| ε | T11 / A1 / A4 amendment cash-outs; chapter-3.5 chapter-close ritual operated retroactively; codification | `AXIOMS.md`, `DECISIONS.md`, `HANDOFF.md`, `CLAUDE.md`, `ADMIRE.md` | All canonical surfaces aligned; chapter-close ritual eight items checked |

Sequencing: α ships first (smallest blast radius — single producer pass + single test file). β + γ ship in independent commits (each strictly additive at the typed-DU level). δ ships when β + γ have settled the `LineageEvent` widening pattern (the typed-segment Identity refactor reuses the same discipline). ε rolls codification at chapter close.

This session ships **slice α only**. β + γ + δ + ε are independent follow-up sessions.

---

## What this chapter does **not** do

Bounded by the strategic frame:

- **No three-channel Diagnostics split.** Single channel still sufficient (deferred at chapter 2).
- **No `BenchSink` port extraction.** Audit Tier-1 #1 rolls forward; chapter-cross-cutting cleanup at any later chapter close.
- **No `IArtifactSink` port.** Routed to chapter 4.x.
- **No `traverseCatalog` natural-transformation primitive extraction.** Adoption-trigger candidate from CLAUDE.md F# feature surface; codify when consumer demand pressures (4 hand-rolled traversals today; the ergonomic-bar-vs-extraction trade-off doesn't yet warrant the new primitive — the existing consumers are short and locally clear).
- **No `result { … }` computation expression introduction.** Adoption-trigger candidate; ReadSide.fs:540–690 chains 4–5 deep, beyond the codebase's "bearable three steps" mark — but adoption is non-blocking for chapter 3.6's substantive work and earns its own slice when it happens.
- **No identity-DU `SourceTag` parameterization.** Routed to chapter 4.2 (User FK reflow); the `V1Mapped` variant becomes reachable there.

---

## Forward signals

After chapter 3.6 closes (slices α–ε green, codification rolled):

- **`StructuredString` deferral 5** can be cashed out: a structured-diagnostic-projection surface that terminates in `XmlWriter` / `Utf8JsonWriter` carrying typed payload (rather than the V2-internal `StructuredString` builder). Chapter 4.x candidate.
- **`LineageEvent` typed payload** unblocks any future audit / dashboard consumer that wants to pattern-match removal categories or intervention decisions structurally — the typed payload IS the structural surface.
- **`SsKey.Synthesized` typed segments** unblock the V1↔V2 identity mapping at chapter 4.2 (User FK reflow); `V1Mapped` and `Synthesized` share the typed-fragment shape.

— Chapter 3.6 architect (sessions 38+).
