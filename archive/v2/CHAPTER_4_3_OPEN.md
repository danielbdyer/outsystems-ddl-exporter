# Chapter 4.3 open — Operational Diagnostics V2 (three-artifact projection over Diagnostics<'a>)

**Sessions:** opens with this document (2026-05-11). **Posture:** Phase 5 of V2-driver KPI critical path (per `V2_DRIVER.md`; the operator-facing diagnostics axis is the first V2 surface humans consult during a deploy). **Predecessors:** chapter 4.1.B (CDC-aware data triumvirate); chapter 4.2 (User FK reflow; A32 cashed out — the pass-produces-emitter-consumable-value pattern is a wired template).

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame`. Companion close synthesis lands at `CHAPTER_4_3_CLOSE.md` when this chapter ends. **Operational pre-scope:** `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` (covers chapters 4.3 + 4.4 jointly; this chapter is Part 1 only — chapter 4.4 RemediationEmitter is sequenced after chapter 3.x DacpacEmitter lands).

---

## Why this chapter

Per pre-scope §1: "Up through chapter 4.2 the only humans reading V2's output have been agents and test fixtures. Operational diagnostics and remediation are the first surfaces real cutover operators consult during a deploy." The discipline shifts — every JSON shape needs to be greppable with `jq`, every error needs to name a remedy, and three operator-facing artifacts route the existing `Diagnostics<'a>` writer's entries into:

- `decision-log.json` — every decision the system made (full audit).
- `opportunities.json` — actionable suggestions for operator review.
- `validations.json` — pass-witnessed invariant confirmations.

**The work is projection, not new algebra.** The passes already emit `DiagnosticEntry` records (`Source` / `Severity` / `Code` / `Message` / `SsKey` / `Metadata`); chapter 4.3 routes those entries to three named files via a `Code`-prefix table. No new IR, no new pass shape, no new writer.

---

## Strategic frame — eight axes named at chapter open

Per the chapter-4.1.A / chapter-4.1.B / chapter-4.2 precedent, multi-session chapters name their load-bearing axes at chapter open before substantive slices begin.

1. **DDD — `decision-log` / `opportunities` / `validations` are the operator-vocabulary names of three sibling projections.** Per pillar 8 (domain-first naming): the artifact names ARE the channels. The bounded context is "operational diagnostics" — what operators consult during a cutover, named in their vocabulary. `DecisionLogEmitter` / `OpportunitiesEmitter` / `ValidationsEmitter` are concept-shaped (name what they emit, not what they do).

2. **FP — three emitter modules share one routing primitive.** Per pre-scope §1.3 + the two-consumer threshold: `OperationalDiagnostics.Routing` is the shared module the three siblings consume. The routing function decides which artifact each `DiagnosticEntry` lands in via `Code`-prefix dispatch (`tightening.*.opportunity.*` → opportunities; `tightening.*.validation.*` → validations; everything else → decision-log). Pure function of `DiagnosticEntry`.

3. **Hardcore (no-string-concatenation) — JSON emission flows through `Utf8JsonWriter` + `JsonNode`.** Per pillar 3 (built-in obligation) + chapter 3.7 slice ε precedent (`JsonEmitter.emitSlices` returns `JsonNode`): the three emitters return `Result<ArtifactByKind<JsonNode>, EmitError>`. Pillar 1 holds at the Π port boundary; strings emerge only at the terminal Utf8JsonWriter step.

4. **Streaming — bench observability per emitter.** Each emitter's `Bench.scope` records its `Catalog × entries -> ArtifactByKind<JsonNode>` traversal; per-entry routing decisions surface via `Bench.iterDo` at canary scale.

5. **Hexagonal — diagnostics writer stays in Core; emitters in Targets/.** The three new emitters live under new project `Projection.Targets.OperationalDiagnostics`. `Diagnostics<'a>` writer (Core) unchanged.

6. **Built-in obligation — `Utf8JsonWriter` for the per-kind JSON shape; `JsonNode` typed seam at the Π port.** Mirrors `JsonEmitter` (chapter 1 + chapter 3.7 slice ε pillar-1 cash-out). No string composition at the per-entry boundary.

7. **Aggregate-root + smart constructor — per-kind `JsonNode` via `ArtifactByKind`.** T11 keyset coverage holds structurally: every catalog kind gets a JSON document (possibly empty when no entries match the kind's SsKey).

8. **Test-fidelity — three property tests at chapter signature.**
   - **T1**: same `(Catalog, entries)` → byte-identical JSON across repeat invocations.
   - **T11**: every `Catalog.allKinds` SsKey appears as a top-level key in each ArtifactByKind.
   - **Routing partition**: every `DiagnosticEntry` lands in exactly one of the three artifacts (no entry orphaned; no entry double-counted). Property test over generated entries with random Codes plus the Code-prefix table.

---

## Slice arc

Per pre-scope §1.5 + the strategic frame above:

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | `DecisionLogEmitter` minimal | Per-kind JSON document containing the entries that mention that kind's SsKey; T1 + T11 properties hold; entries without SsKey route to a catalog-level "unscoped" bucket | ~150 src + ~120 test |
| β | `OperationalDiagnostics.Routing` (shared module) + `OpportunitiesEmitter` | Routing primitive extracts at the second consumer; `tightening.*.opportunity.*`-prefixed entries land in opportunities.json | ~80 src + ~80 test |
| γ | `ValidationsEmitter` + routing partition property test | `tightening.*.validation.*`-prefixed entries land in validations.json; partition property holds across all three siblings | ~50 src + ~60 test |
| δ | (deferred per chapter close) CLI wire-up in `Projection.Pipeline` | Pipeline integration; canary writes three diagnostic files alongside SSDT/DACPAC artifacts | ~120 src |
| ε | (deferred per chapter close) V1 differential test | V1 envelope walk per chapter-close ritual item 8; account for every divergence per the chapter-2 three-class typology | ~250 LOC test fixture |

**Slice α + β + γ are the chapter signature** (the three operator artifacts ship structurally). Slice δ + ε are V1-parity polish; deferred per V2-driver KPI sequencing (the V2-driver mode doesn't require V1 envelope equality; it requires the artifacts BE shipped with operator-facing structure).

---

## Retiring the three-channel Diagnostics split deferral

Per pre-scope §1.4 + `HANDOFF.md` ("Three-channel Diagnostics split (operator/auditor/developer) — single channel sufficient at all chapter-2 consumers"): the chapter-2 deferral is **retired at chapter 4.3 open** with the decision **refuse the split; the artifacts ARE the channels, route by Code prefix at emit time**.

The three V1 artifacts (decision-log / opportunities / validations) are descriptive of *what is being emitted*, not of *who consumes it*:
- `decision-log` → audit channel (every decision the system made)
- `opportunities` → operator channel (actionable suggestions)
- `validations` → developer channel (pass-witnessed invariants)

Adopting this framing means **the existing `Diagnostics<'a>` writer remains single-channel**; routing happens at emit time via the Code-prefix table. No `DiagnosticChannel` DU; no parallel writer. Three artifacts route from one stream. The DECISIONS entry codifies this at chapter 4.3 open.

---

## Inheritance from prior chapters

- **Chapter 3.7 slice ε `JsonNode` typed Π port** — the three emitters return `Result<ArtifactByKind<JsonNode>, EmitError>` per the chapter-3.7 slice-ε pillar-1 cash-out.
- **Chapter 4.2 A32 wired template** — the chapter-4.2 `UserFkReflowPass.discover` produces `Diagnostics<UserRemapContext>`; chapter 4.3's three emitters consume the `entries` from any such pass-output (the `DiagnosticEntry list` shape is the common seam).
- **Pillar 3 + Pillar 1 (chapter 3.5 supreme operating discipline)** — `Utf8JsonWriter` is the gold-standard library for JSON emission; `JsonNode` is the typed seam.
- **Pillar 7 routing-table primitive** — pre-scope §1.3 names `OperationalDiagnostics.Routing` as the shared module the three siblings consume. Per the two-consumer threshold: routing primitive lands at slice β.

---

## What this chapter does NOT do

- **No `DiagnosticChannel` DU.** Per §1.4 + the deferral retirement: the artifacts ARE the channels.
- **No SQL remediation script rendering.** V1's `OpportunityLogWriter` embedded SQL; V2's surface is JSON only. A future emitter can produce remediation SQL if a real consumer demands it.
- **No new diagnostic codes.** The passes own those.
- **No RemediationEmitter.** Chapter 4.4 (RemediationEmitter — schema-level partial-state recovery) is sequenced after chapter 3.x DacpacEmitter (conditional on deploy path).

---

## Companion documents

- **Pre-scope (operational plan):** `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` Part 1.
- **V2-driver KPI:** `V2_DRIVER.md` (per-axis correctness stakes table — operator diagnostics is "Lower" stakes; chapter 4.3 ships the structural shape, not the operational stakes).
- **Strategic frame precedents:** `CHAPTER_4_1_A_OPEN.md` + `CHAPTER_4_1_B_OPEN.md` + `CHAPTER_4_2_OPEN.md` (sibling chapters; same eight-axis discipline).

---

## Closing

Chapter 4.3 is the first V2 surface humans consult during a cutover. The structural commitment: three artifacts route from one stream; the routing table is the single point of decision; `Utf8JsonWriter` + `JsonNode` carry the typed shape through the Π port; T11 keyset holds across all three siblings.

Each slice ships with its own commit; the close ritual operates at chapter close (eight items per CLAUDE.md operating-disciplines table).
