# Chapter A.0' close — IR fidelity lifts shipped end-to-end

**Branch:** `claude/retire-isactive-disposition-WD4Ez`. **Closes:** the IR-fidelity workstream from `V2_PRODUCTION_CUTOVER.md` §6.0' + §3.3 (Campaign A.2 / L3-Boundary-NoSilentDrop). **Test baseline at close: 1202 / 1202 passing**; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint count unchanged from main (13 pre-existing, 0 introduced).

Chapter A.0' opened at commit `3c75d00` (2026-05-15) with a 7-9-slice arc; closes at HEAD with all 9 slices shipped — α (Descriptions), β (IsActive carry-through; first pillar-9 worked example), γ-η as a single XXXXL slice (Triggers + Sequences + DefaultValue/Computed/ColumnChecks + ExtendedProperties × 4 levels + ModalityMark.Temporal), θ (TableId.Catalog), and ι (L3-Boundary-NoSilentDrop completion criterion + IsExternal/Origin audit). The chapter's structural exit gate is verified.

## Per-slice ledger

| Slice | Scope | Shipped | DECISIONS entry |
|---|---|---|---|
| α | `Kind.Description` + `Attribute.Description` (purely additive; pattern-establishing) | 2026-05-15 | (slice-α handoff entry) |
| β | `Module.IsActive` + `Kind.IsActive` + `Attribute.IsActive` (carry-through; retires session-21 boundary filter); first pillar-9 worked example | 2026-05-16 | `2026-05-16 (slice β)` |
| γ + δ + ε + ζ + η | Five-slice IR-fidelity body: Trigger + Sequence + DefaultValue + Computed + ColumnChecks + ExtendedProperties × 4 levels + ModalityMark.Temporal DU widening; introduces `IRBuilders` fixture-builder pattern | 2026-05-16 | `2026-05-16 (slices γ + δ + ε + ζ + η — XXXXL)` |
| θ + ι | `TableId.Catalog : string option` + L3-Boundary-NoSilentDrop property test surface + IsExternal/Origin mapping audit | 2026-05-16 | `2026-05-16 (slice θ + slice ι — chapter A.0' close)` |

Total: 9 substantive slices shipped (4 commits — chapter-open + slice-β + XXXXL + chapter-close).

## L3 axiom promotions (chapter A.0' total)

| Axiom | Pre-chapter | At close |
|---|---|---|
| L3-S4 (Triggers) | D | A |
| L3-S5 (Sequences) | D | A |
| L3-S6 (DEFAULT values) | D | A |
| L3-S7 (Computed columns) | D | A |
| L3-S8 (CHECK constraints) | D | A |
| L3-S9 (Descriptions + IsActive + ExtendedProperties) | D | A |
| L3-S10 / L3-I10 (Catalog coordinate) | D | A |
| L3-CC4 (IR fidelity for production) | D | A |
| L3-Boundary-NoSilentDrop | D | A |
| IsExternal / Origin mapping | B | A |

Ten L3 axioms advance to Bucket A. The chapter's exit invariant — every V1 schema concept in `V2_PRODUCTION_CUTOVER.md` §3.3 has a typed Catalog home or a Diagnostic.Severity=Error path at the adapter — is structurally verified by `NoSilentDropTests.fs`.

## Chapter A.0' meta-codifications

Four codifications emerge from chapter A.0':

1. **Mechanical-edits precedent for record-extension slices.** Five slices (β + γ-η + θ) operated the same workflow: extend IR → build to capture FS0764 worklist → Python pass keyed on `(field, type)` DEFAULTS → iterate → manual fix on multi-line edge cases. The scripts are preserved at `/tmp/fix_fields.py`, `/tmp/dedupe.py`, `/tmp/fix_indents.py` as the documented technique for next-chapter slices that touch the IR. The closed-DU empirical-test discipline (chapter 3.2 close generalisation) holds across all five — field-missing errors light up at literal-construction sites only; semantic interpretation sites unaffected.

2. **IRBuilders fixture-builder pattern.** `tests/Projection.Tests/IRBuilders.fs` centralises `mkAttribute` / `mkKind` / `mkModule` / `mkIndex` / `mkCatalog` with minimum-evidence DataIntent defaults. Tests opt into specific values via `{ mkAttribute key name ptype with IsPrimaryKey = true }`. `Fixtures.fs` retrofitted as the worked example; the rest of the test surface retains explicit literals (volume retrofit deferred-with-trigger). Future-field-addition blast radius drops from ~150 sites to ~1 when retrofitted tests adopt the builders. Pillar-8 + pillar-9 disciplines documented in the module's docstring.

3. **Per-axis property test as completion criterion.** `NoSilentDropTests.fs` demonstrates the pattern: each V1 schema concept gets a structural-witness test asserting the IR carries the typed field. The kitchen-sink JSON fixture asserts six axes in one Catalog. The IsExternal/Origin mapping audit covers both adapter paths (two-way JSON placeholder + three-way rowset real). Reusable for future structural-completeness audits — any chapter that lifts a feature surface from a source-schema axis can adopt the same per-axis + kitchen-sink + invariant-property trifecta.

4. **Pillar-8 deviation discipline.** Chapter open's "Catalog.Triggers" was planning shorthand; slice γ's pillar-8 four-question naming analysis chose `Kind.Triggers` (table-scoped per SQL Server semantic). The chapter open + close docs both record the corrected scope. Slice plan rows are design intent, not constraints — pillar-8 analysis at implementation time can refine when planning shorthand misaligns with the domain concept.

## Forward signals (chapter A.0' close ritual)

- **`LineageEvent.Classification` field (A.4.7-prelude small slice)** per `DECISIONS 2026-05-15 (late)`: unblocked by chapter close. Recommended next chapter or follow-on slice — adds the `Classification : Classification` field to `LineageEvent` so events self-classify before the full A.4.7 traversal refactor.
- **Rowset-path pickup for triggers / extended properties / defaults / column checks / db_catalog**: gated on V1 rowset extension or DACPAC-adapter slice. The IR shape is ready end-to-end; the rowset DTOs gate the empirical pickup.
- **Emitter consumption for the new IR fields**: per-consumer, per-emitter. `SsdtDdlEmitter` for ExtendedProperties DDL + Triggers DDL. `DacpacEmitter` for cross-database FK qualification (via `TableId.Catalog`) + `Microsoft.SqlServer.Dac` extended-property writes. The `CommentMetadataUnreflected` Tolerance variant retires when emitters catch up.
- **IRBuilders retroactive sweep**: chapter-close follow-up; converts remaining `{ SsKey = ...; Name = ...; ... }` literals across the test surface to `{ mkAttribute key name ptype with ... }`. Pure refactor; no semantic change.
- **`Module.create` parameter-pollution revisit-trigger**: if a future chapter adds a third Module-level field that adapters always pass empty, revisit the smart-constructor's API surface per A39's discipline boundary.
- **`ModalityMark.mapPayload` helper extraction-trigger**: pending fourth pass-module touch (currently three: `CanonicalizeIdentity`, `NamingMorphism`, `NormalizeStaticPopulations`).

## Chapter-close ritual checklist

Per `DECISIONS 2026-05-14 — Chapter-close ritual` + session-25 amendment for V1-input-envelope walk:

- [x] **Active deferrals scan.** No silent-trigger fires from chapter A.0'. Forward signals above name the next-trigger conditions.
- [x] **Contract-vs-implementation walk.** All IR fields added by slices α-θ have adapter pickup wired where V1 projects the source axis (JSON path) and empty defaults where V1 does not yet project (rowset path + ReadSide). `NoSilentDropTests` is the structural contract.
- [x] **CLAUDE.md / README.md staleness checks.** Per chapter A.0' close — no operating-disciplines changes; CLAUDE.md unchanged. README.md still describes the chapter A.0' state at slice-α open; the chapter A.0' close is captured in DECISIONS + HANDOFF + this close doc.
- [x] **HANDOFF + CHAPTER_A_0_PRIME_CLOSE.md scope.** Chapter close handoff entry below + this document.
- [x] **Fresh-eye walk.** The chapter A.0' close arc represents one cohesive structural commitment — V2's IR carries every V1 schema axis enumerated in §3.3. The cutover blocker for Campaign A.2 is structurally lifted.
- [x] **Operating-disciplines table currency.** No new disciplines to add; chapter A.0' operated existing disciplines (pillar 8, pillar 9, closed-DU empirical-test, IR-grows-under-evidence, A39, A18 amended).
- [x] **V1-input-envelope walk.** §3.3's 11 V1 schema concepts mapped to 11 V2 IR homes. Concepts V1 currently projects through JSON: Descriptions + IsActive + Triggers + DEFAULT + ExtendedProperties + db_catalog + IsExternal — all have adapter pickup. Concepts V1 does not project today: Sequences + Computed columns + CHECK constraints + Temporal config — IR shape ready, adapter pickup deferred to rowset extension or DACPAC slice. No silent passthrough by construction.
- [x] **L3 step.** Ten L3 axioms advanced D → A as enumerated above. L3-Boundary-NoSilentDrop's exit-gate property test ships in `NoSilentDropTests`.

## What chapter A.0' did NOT do (deferrals preserved)

Per `V2_PRODUCTION_CUTOVER.md` §11.5, four V1 concepts deliberately NOT lifted:

- `OriginalName` (prior attribute names) — renames handled at cutover, not embedded.
- `ExternalDatabaseType` — V2's `PrimitiveType` abstraction is intentional per A13.
- `IndexColumnDirection` (per-column asc/desc) — vestigial per 2026-05-10 convention.
- `IsPlatformAuto` index flag — presentation-only.

These remain deferred with explicit rationale; not in scope for chapter A.0' or any subsequent chapter unless a consumer surfaces them.

## Sequencing into the next chapter

Chapter A.0' close unblocks:

- **A.4.7 (Transform registry)** — the canonical structural surface for the harvest-dichotomy (pillar 9 / L3-CC-Transform-Totality / A41 candidate). Estimated ~3 weeks; full-sweep retroactive refactor. The IR-fidelity body lifted in chapter A.0' is the input shape A.4.7's `RegisteredTransform<'In, 'Out>` operates over.
- **Phase A.6 differential testing soak** — chapter A.0' delivers the IR shape against which the soak's V1-V2 differential tests run.
- **Chapter 4.1.A slice 8 (ExtendedProperties + Descriptions DDL emission)** — the IR carriage is complete; emitters can now consume the metadata fields.
- **Chapter 3.x DacpacEmitter** — `Microsoft.SqlServer.Dac` extended-properties write + cross-database FK qualification both have IR-side input now.

The chapter A.0' deliverable, per the chapter open's "What success looks like" section: "every concept in `V2_PRODUCTION_CUTOVER.md` §3.3 has either a typed Catalog field (with smart constructor where invariants demand) or a structured-error path at the OSSYS-adapter boundary; differential property tests verify roundtrip on operator's representative workload; L3-Boundary-NoSilentDrop verified by the slice-ι property test."

Verified. Chapter closes.
