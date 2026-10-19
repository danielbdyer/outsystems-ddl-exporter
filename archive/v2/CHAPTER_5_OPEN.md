# Chapter 5 open — Phase 8 pragmatic close (consumer-pressure-driven hygiene + governance)

**Sessions:** opens with this document (2026-05-11) after chapter 3.x close. **Posture:** Phase 8 of V2-driver KPI per `V2_DRIVER.md` §252 — consumer-pressure-driven hygiene + governance items shipping ad-hoc; **no single-chapter close** (the chapter stays open across slices until the operator declares the queue complete).

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame`. Companion close synthesis is **deferred** until the queue empties (or stabilizes per V1-sunset milestones).

---

## Why this chapter

The V2-driver KPI critical path (Phases 1–5 + 7) closed at chapter 4.3 close (2026-05-11); Phase 6 substantively shipped under reframe at chapter 3.x close (2026-05-11). What remains is **post-critical-path hygiene** — items that earn their place when a consumer surfaces them, not when a sequencing rule demands them.

Chapter 5 is the formal chapter name for that queue. Slices land as separate commits; the chapter open accumulates a slice list; the chapter close (if it ever fires) cashes out the queue's stable items.

---

## Strategic frame — axes named at chapter open

Per the chapter-4.x precedent, multi-session chapters name their load-bearing axes at chapter open before substantive slices begin.

1. **DDD — Coordinates Stage 2 names the schema-coordinate vocabulary at the type level.** Per pillar 8: `SchemaName` / `TableName` / `ColumnName` are concept-shaped value objects. The codebase's `TableId` (Stage 1) names the composite; Stage 2 names the components. The compiler refuses to confuse a schema with a table.

2. **FP — Smart constructors propagate `Result<'a>` at every value-object construction.** Per the structural-commitment-via-construction-validation principle: blank / over-length / SQL-injection-shaped inputs reject at construction. Downstream consumers trust the value.

3. **Hardcore (no string composition at the type-system boundary) — typed VOs replace `string` fields at the IR level.** The construction sites pay a `Result.value` cost; the read sites get domain types. Pillar 1 (data-structure-oriented) holds at the IR.

4. **Built-in obligation (FSharp.Analyzers.SDK for AST-level lint).** Per chapter 3.7 slice ν deferral: 27 grep-based lint rules cover the obvious surface; AST detection complements where syntactic detection misfires. The `Projection.Analyzers` project hosts custom analyzers consumed via the `fsharp-analyzers` runner.

5. **Streaming — analyzer is a side-channel; doesn't change the runtime surface.** Custom analyzers run in the editor and via CLI; they emit diagnostics, not artifacts. The runtime build path stays unaffected.

6. **Hexagonal — analyzers live in a sibling project to Core / Targets / Adapters.** `Projection.Analyzers` is the analyzer project; it depends on `FSharp.Analyzers.SDK` + `FSharp.Compiler.Service`. Not on Projection.Core (analyzers walk other projects' source; they don't consume their values).

7. **Aggregate-root smart constructor — the Stage-2 VOs are aggregate-root invariants for identifiers.** `SchemaName.create` is the single point that decides what's a valid schema name; downstream code never re-validates.

8. **Test-fidelity — analyzer tests assert structurally, not via golden output.** The analyzer's diagnostic emission is a function of the AST input; tests construct AST fragments and assert the analyzer flags them. Property-style.

---

## Slice scope — chapter ordering

Per V2_DRIVER §252 (deferred-with-trigger; consumer-pressure-driven):

| # | Slice | What |
|---|---|---|
| ν | F# Analyzers SDK custom analyzer (this chapter open ships this slice) | `Projection.Analyzers` project + one analyzer (`Projection001NoUnsafeTimeInCore` — detects `System.DateTime.Now` / `DateTime.UtcNow` / `Guid.NewGuid` calls inside `src/Projection.Core/`); `fsharp-analyzers` tool registered via `.config/dotnet-tools.json`; `scripts/run-analyzers.sh` runner |
| θ | Coordinates Stage 2 typed VOs (this chapter open ships this slice) | `SchemaName` / `TableName` / `ColumnName` smart constructors land in `Coordinates.fs`; SQL Server identifier-length validation (128-char limit); `Result<'a>`-returning surface; tests for rejection cases; **PhysicalRealization migration deferred** until adapter-ripple cost is empirically justified (the Stage-1 docstring's "real bug" trigger; per `Coordinates.fs:19-23`) |
| (future) | Hex port lifts (`IArtifactSink`, `IDeployHost`) | Under genuine consumer demand (no current consumer; `Render.toSsdtDirectory` writes files via `SsdtBundle` today) |
| (future) | Cutover-day operator runbook | Joint deliverable with solution architect; bridges V1's `ssdt-playbook/` and V2's algebraic guarantees |
| (future) | V1 sunset planning | After cutover+30 + one full schema-evolution cycle on V2 emissions |

---

## What this chapter does NOT do

Per pillar 4 + IR-grows-under-evidence:

- It does NOT migrate `PhysicalRealization.Schema/Table` (string fields) to typed `SchemaName`/`TableName` at this chapter open. **Slice θ ships the VOs; the field migration is deferred-with-trigger** until adapter-ripple cost is justified by an empirical safety win or a real bug.
- It does NOT auto-run analyzers in CI. The runner script is opt-in; CI integration is a future slice when the analyzer set grows beyond one rule.
- It does NOT add a comprehensive analyzer suite. Slice ν ships one analyzer as the infrastructure proof; additional analyzers earn their place when a real false-negative on the grep rules surfaces.

---

## Test baseline at chapter open

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Pre-slice-ν baseline:** 1060 non-canary tests passing + ~16 Docker-dependent canary tests; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. Lint clean across 27 rules.

Slices ν + θ add ~15 tests (analyzer logic; smart constructor rejection cases for the three VOs).
