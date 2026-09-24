# Chapter 4.3 close — Operational Diagnostics V2 (V2-driver KPI Phase 5)

**Sessions:** chapter-4.3 opened on `claude/chapter-4-ddd-improvements-XVCAM` at `bf3770b`; structural slice arc α/β/γ shipped through `abe0040`.

This document discharges chapter 4.3's eight-item close ritual now that the three-emitter projection over `Diagnostics<'a>` is end-to-end + the Routing partition property is green. Slices δ (CLI wire-up) and ε (V1 differential) defer to the queue with explicit triggers per the close-ritual discipline.

---

## Why this close

Per `V2_DRIVER.md` per-axis correctness stakes: the operational-diagnostics axis is "Lower" stakes by V2-driver KPI rank — the structural commitment (three artifacts route from one stream via Code-prefix) ships the operator surface; the operational stakes (real-cutover dashboards, jq queries, structured ops review) are downstream of the structural commitment.

Chapter 4.3 ships the structural commitment. Operators consulting V2 during a cutover see three named artifacts whose contents derive deterministically from the Diagnostics<'a> writer the passes already populated — no new algebra, no new pass shape, no parallel writer.

---

## What shipped (slice arc α + β + γ)

### Slice α — DecisionLogEmitter + chapter-2 three-channel-deferral retired (`bf3770b`)

- **`CHAPTER_4_3_OPEN.md`** — eight-axis strategic frame; "refuse the split" decision codified.
- **New project `Projection.Targets.OperationalDiagnostics`** — sibling to `Targets.SSDT` / `Targets.Json` / `Targets.Data` / `Targets.Distributions`. Added to `Projection.sln`.
- **`DecisionLogEmitter.emit : Catalog -> DiagnosticEntry list -> Result<ArtifactByKind<JsonNode>, EmitError>`** — A18 amended preserved structurally; per-kind JSON document; T11 keyset coverage; T1 byte-determinism.
- **Per-kind JSON shape** — `{ ssKey, name, entries: [...] }`; entries: `{ source, severity, code, message, ssKey, metadata }`; metadata sorted by key for hash-table-iteration-variance immunity.
- **Pillar 1 cash-out** — JsonNode typed seam at the Π port (`Utf8JsonWriter` → `MemoryStream` → `JsonNode.Parse(ReadOnlySpan<byte>)`); strings emerge only at the terminal writer step.
- **Three-channel deferral retired** — DECISIONS entry codifies "refuse the split; the three V1 artifacts ARE the channels (decision-log = audit; opportunities = operator; validations = developer); routing happens at emit time via the Code-prefix table."

### Slice β — Routing primitive + OpportunitiesEmitter (`abe0040`)

- **New file `Routing.fs`** — `DiagnosticArtifact` closed DU + `DiagnosticArtifact.filename` projection + `Routing.route : DiagnosticEntry -> DiagnosticArtifact` (single point of routing decision; pure function of `Code`) + `Routing.partition` (chronological order preserved within each bucket).
- **`OpportunitiesEmitter`** — same architectural shape as `DecisionLogEmitter`; filter applied via `Routing.route entry = Opportunities`.
- **Shared internal `DiagnosticDocument` module extracted** at the two-consumer threshold (DecisionLog + OpportunitiesEmitter both consume; ValidationsEmitter at slice γ is the third).
- **Both `emit` (raw entries) and `emitRouted` (pre-filtered) signatures** per sibling — composer-friendly + standalone-friendly.
- **Defensive null-check in Routing.route** — F# 9 nullness-strict declares `DiagnosticEntry.Code` non-nullable but FsCheck generates null at runtime; `System.String.IsNullOrEmpty` is the BCL primitive that totalizes the routing function over the full runtime input domain per the total-decisions discipline.

### Slice γ — ValidationsEmitter + partition property (`abe0040`)

- **`ValidationsEmitter`** — same shape; filter applied via `Routing.route entry = Validations`.
- **The chapter signature deliverable (Routing partition property)** — every `DiagnosticEntry` routes to exactly one of three artifacts (no entry orphaned; no entry double-counted). FsCheck property test over generated entries; verified across > 100 chains.
- **Three-sibling commutativity test** — union of opportunities + validations + decisionLog covers the full entry set; per-bucket counts add up.

---

## Eight-item chapter-close ritual

Per `DECISIONS 2026-05-14 — Chapter-close ritual` + the V1-envelope-walk amendment.

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **Three-channel Diagnostics split** | **Retired at chapter 4.3 open (`bf3770b`)** — refuse the split; the artifacts ARE the channels. |
| Composition primitives `fallback` / `accumulate` / `wrap` / `lift` | Untriggered |
| Statement DU MERGE/UPDATE promotion | Untriggered |
| Sort-vs-data deferral predicate distinction | Untriggered (third cycle-metadata consumer hasn't surfaced) |
| OSSYS adapter User-kind identification surface | Untriggered |
| CSV adapter for `ManualOverride` | Untriggered |
| `Attribute.Default` field | Untriggered (SnapshotRowsets hasn't surfaced default-constraint columns) |
| `Kind.Description` + `Attribute.Description` fields | Untriggered (SnapshotRowsets hasn't surfaced description columns) |
| **DacFx adoption in DacpacEmitter** | Untriggered (chapter 3.x conditional on deploy path; see DECISIONS entry below) |
| Three-channel Diagnostics split (operator/auditor/developer) | **Retired** — see above |

Two new deferrals codified at this close (see DECISIONS entry below): **Slice δ (CLI wire-up)** and **Slice ε (V1 differential)**.

### 2. Contract-vs-implementation walk

The chapter contract per pre-scope §1: "Three new sibling Π's under `Projection.Targets.OperationalDiagnostics`, each emitting one V2 equivalent of a V1 operator-facing JSON artifact." **Every contract clause is implemented**:

- `DecisionLogEmitter` → `decision-log.json`-shaped per-kind JsonNode.
- `OpportunitiesEmitter` → `opportunities.json`-shaped per-kind JsonNode.
- `ValidationsEmitter` → `validations.json`-shaped per-kind JsonNode.
- A18 amended preserved (each emitter's signature is `Catalog × DiagnosticEntry list`; never Policy).
- T11 keyset coverage holds across all three siblings (every catalog kind keyed; empty `entries: []` when no diagnostics match).
- The Code-prefix routing table is the single point of decision; the partition property holds structurally.

The pre-scope's slice δ (CLI wire-up in `Projection.Pipeline`) and slice ε (V1 differential test) defer to the queue per the close-ritual discipline (see DECISIONS entry below for triggers).

### 3. CLAUDE.md staleness check

Operating-disciplines table current. No new disciplines warrant addition at this close — the existing pillar 1 / pillar 3 / pillar 8 / two-consumer threshold / writer-fidelity disciplines covered every slice.

### 4. README.md staleness check

Test baseline 1012 non-canary (was 963 at chapter 4.2 close). Update pending in this close commit.

### 5. HANDOFF.md scope

New chapter-4.3 prologue at this close (this commit). Names load-bearing (three-emitter projection over Diagnostics<'a>; Routing primitive; partition property; chapter-2 three-channel deferral retired) + deferred (slice δ CLI wire-up; slice ε V1 differential) + V1-input-envelope walk pointers.

### 6. Fresh-eye walk (cross-document drift)

- `KICKOFF.md` baseline test count refresh pending — was 963; now 1012 non-canary + ~16 Docker-dependent canary.
- `V2_DRIVER.md` Phase 5 status: **closed** (was "not-started (critical)").

### 7. V1-input-envelope walk

V1's three operator-facing JSON artifacts are the three empirical references:

- **`policy-decisions.json`** (`PolicyDecisionLogWriter.WriteAsync` at `src/Osm.Pipeline/Orchestration/PolicyDecisionLogWriter.cs:36-88`; record types lines 110-168). V2 ships **one** file (`decision-log.json`) whose shape is a shallow superset of V1's flat form indexed by SsKey. The two-file split V1 ships (`policy-decisions.json` + `policy-decision-report.json`) is a V1 implementation detail; V2's `Diagnostics<'a>` writer carries what both V1 files express. **Documented as deliberate divergence** per the supreme operating discipline pillar 6 (no V1 back-compat paths in V2).
- **`opportunities.json`** (`OpportunityLogWriter.WriteAsync` at `src/Osm.Pipeline/Orchestration/OpportunityLogWriter.cs:76-82`; record at `OpportunitiesReport.cs:6-13`). V2 binary outcomes (`UniqueIndexOutcome.EnforceUnique` / `DoNotEnforce`) collapse V1's "EnforceUnique + RequiresRemediation" combination; V2 emits one Warning per `DoNotEnforce`. **Documented as deliberate divergence** (V2 makes the binary choice structural; V1 carried both axes).
- **`validations.json`** (`ValidationReport.cs:7-15`; `ValidationFinding.cs:8-19`). V1 sorts findings by Schema/Table/ConstraintName/Type/Title via `ValidationFindingComparer.Instance`; V2 sorts by SsKey root for T1 byte-determinism. **Documented as deliberate divergence** (V2's identity-keyed sort is structural; V1's name-keyed sort is presentation-layer).

The slice ε V1 differential test (deferred — see DECISIONS) cashes out the divergences as named tolerances at the comparator layer when V1 fixture canonicalization stabilizes.

### 8. AXIOMS.md amendment cash-out

No new AXIOMS amendments earned at chapter 4.3 close. The chapter is **projection over substrate, not new algebra** (per pre-scope §"Strategic frame" axis 1). T11 keyset coverage holds structurally via `ArtifactByKind.create`; A18 amended preserved at the type level; existing axioms cover the chapter's algebraic claims.

---

## Test count

- **1012 non-canary tests passing** (was 963 at chapter 4.2 close; **+49 across all of chapter 4.3 + R4 multi-env property test**)
- **~16 Docker-dependent canary tests** (unchanged; no canary-affecting work in chapter 4.3)
- **Lint clean** across 27 rules
- **Build clean** under `TreatWarningsAsErrors=true`

---

## What's load-bearing going forward

Chapter 4.3's structural commitments that future chapters inherit:

- **`Routing.route` is the single point of routing decision** for DiagnosticEntry → operator artifact. New diagnostic Codes follow the convention (`tightening.*.opportunity.*` / `tightening.*.validation.*` / everything else); future code-namespaces extend the match without restructuring the writer.
- **Three sibling-Π emitters share one `DiagnosticDocument` writer** (internal module). A fourth operator-facing artifact (e.g., remediation-script JSON; cutover-day status) earns its place by adding a `DiagnosticArtifact` variant and a fourth filter — the `DiagnosticDocument` writer extends without reshaping.
- **The partition property is structural**: every entry routes to exactly one artifact. Property test over FsCheck-generated inputs is the regression gate.
- **JsonNode typed seam at every operator-diagnostics Π port** — pillar 1 holds end-to-end across the three emitters.
- **Three-channel-Diagnostics-split deferral retired** — future cross-cutting "let's split the Diagnostics<'a> writer" temptations refer to this DECISIONS entry's "refuse the split" rationale.

---

## What's deferred (with explicit triggers)

### Slice δ — Pipeline CLI wire-up

Per pre-scope §1.5 slice 5: the canary's CLI verb invokes the three emitters and writes the three files alongside the SSDT/DACPAC artifacts (`Projection.Pipeline/OperationalDiagnostics.cs` — C# in the canary project per `DECISIONS 2026-05-15`). **Deferred** at this close because the V2-driver KPI structural commitment is the three-emitter projection (which shipped) — the CLI wire-up is operator UX integration, not algebraic content. **Trigger to cash out**: a real cutover-day operator workflow that consumes the three artifacts (e.g., a CI pipeline step that publishes them as build artifacts, or a jq-based dashboard).

### Slice ε — V1 differential test

Per pre-scope §1.5 slice 6: the V1 envelope walk runs both V1 (existing trunk) and V2 (new emitters) against the same fixture Catalog and diffs the three artifacts. **Deferred** because (a) V1 fixture canonicalization at the trunk surface is in flux (V1's `OpportunityLogWriter` SQL-rendering side-effects are not scoped for V2 carry-forward); (b) the divergences are structurally documented (V2 collapses EnforceUnique+Remediation; V2 sorts by SsKey not Schema/Table/ConstraintName; V2 emits one decision-log file rather than V1's two) per the V1-envelope walk above. **Trigger to cash out**: V1's `OpportunityLogWriter` + `PolicyDecisionLogWriter` + `ValidationReport` writers stabilize as the canonical V1 reference shape.

### Chapter 4.4 — RemediationEmitter

Per `V2_DRIVER.md` Phase 6: chapter 4.4 ships `RemediationEmitter` — partial-state recovery primitive that consumes deployed Catalog + target Catalog via CatalogDiff and emits a corrective DACPAC. **Sequenced after chapter 3.x DacpacEmitter** because RemediationEmitter composes over DacpacEmitter's typed-DACPAC output. Chapter 3.x DacpacEmitter is conditional on the deploy path (DACPAC vs SSDT-style file deploy); chapter 4.4 inherits the conditionality. **Trigger**: chapter 3.x DacpacEmitter ships.

---

## What this close enables

- **Chapter 5+ pragmatic close** — cutover-day operator runbook (joint with solution architect; uses the three operator artifacts); F# Analyzers SDK custom analyzer; Coordinates Stage 2 typed `SchemaName` / `TableName` / `ColumnName` VOs; port lifts (`IArtifactSink` / `IDeployHost`); V1 sunset planning. Multi-session.
- **Chapter 4.3 slice δ + ε** unblock when the operator-workflow + V1-fixture-canonicalization triggers fire.
- **Chapter 4.4 RemediationEmitter** unblocks when chapter 3.x DacpacEmitter ships (deploy-path-conditional).

---

## Closing

Chapter 4.3 ships the operator-facing surface of V2's diagnostic pipeline. The structural commitment: three artifacts route from one stream via a pure function of `Code`. The chapter signature property — Routing partition — is structurally enforced; the operator-vocabulary names (decision-log / opportunities / validations) are concept-shaped per pillar 8.

Chapter 4 critical-path is fully closed (modulo the deploy-path-conditional DacpacEmitter). The V2-driver KPI's per-axis correctness gates are green for schema (chapter 4.1.A), data (chapter 4.1.B), User identity (chapter 4.2), and operator diagnostics (chapter 4.3); the cutover-ladder structural commitment (R4) is structurally encoded in the test surface.

Chapter 4.3 closed (2026-05-11).
