# Chapter A.0' open — IR fidelity lifts (Campaign A.2 + B prerequisite)

**Branch:** `claude/review-handoff-docs-CF2v5`. **Predecessor:** PR #538 → merged at `8733d0c`; A.7.1 atomic emission promoted L3-Boundary-AtomicEmission D → A. **Plan-of-record spec:** `V2_PRODUCTION_CUTOVER.md` §6.0' + §3.3 (IR-fidelity gap table). **Audit reference:** `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part VI.

This chapter promotes the **Tier-1 unnamed L3 axioms in §3.3** from Bucket D → Bucket A. Each lift carries a V1 schema concept that `Catalog` does not yet hold into a typed home — OR routes it through a `Diagnostic.Severity=Error` at the OSSYS-adapter boundary. The completion criterion is **L3-Boundary-NoSilentDrop**: no V1 concept in §3.3 leaves the adapter as silent passthrough. Campaign A.2 (no-silent-drop) is the chapter's structural target; Campaign B (smart-constructor sweep) lands incidentally on the new fields that gain invariants.

## Strategic-frame axes (per DECISIONS 2026-05-15 chapter-open shape)

1. **Each lift is structural commitment, not a feature.** A.0' is *not* about emitting more — emission lands per-lift downstream when consumer pressure surfaces (chapter 4.1.A slice 8 for extended properties is the precedent). The chapter's invariant is *the IR carries the evidence*; what passes/emitters do with it is each slice's downstream concern. The campaign tag is A.2 because the no-silent-drop axiom binds at the IR-fidelity surface, not at the emission surface.

2. **Record-field extensions, not DU widening.** Per the closed-DU empirical-test discipline's record-extension generalization (chapter 3.2 close): adding a field to `Kind` / `Attribute` / `Module` produces F# field-missing errors at literal-construction sites only; semantic interpretation sites are untouched. The blast radius for `Kind.Description` / `Attribute.Description` is wide (~200 record literals across adapters / tests / fixtures) but mechanical; the compiler enumerates the worklist. New value types (`Trigger`, `Sequence`, `TemporalConfig`, `ComputedColumnConfig`, `ColumnCheck`, `ExtendedProperty`) ship with smart constructors per A39.

3. **Twin-path discipline holds.** The OSSYS adapter parses two paths — JSON (`parseKind` / `parseAttribute`) and rowset (`parseKindRow` / `parseAttributeRow`); both must populate every new field. Slice 5's cross-source parity tests from chapter 3.2 are the precedent. Per-slice property tests verify roundtrip preservation across BOTH paths.

4. **`IsActive` is a semantic shift, not additive.** Per session-21 amendment, the JSON path filters `isActive: false` records at the adapter boundary (silently drops them). The §6.0' lift retires that filter — carries the flag through to the IR; downstream emitters decide. This is the only slice in the chapter that requires a DECISIONS amendment superseding a prior decision (session-21 inactive-records filter). Sliced separately and gated on operator alignment.

5. **Tolerance retirements are forward signals, not slice scope.** `CommentMetadataUnreflected` (Tolerance.fs:58) names the operational deferral that Description + ExtendedProperties lifts EVENTUALLY retire — *but only once emitters consume the IR fields* (chapter 4.1.A slice 8 territory). The chapter-A.0' close-ritual L3 audit step names these signals; the actual retirement is deferred-with-trigger per slice.

6. **L3 axiom-promotion accounting.** Per the cutover-plan §6.0' table, the chapter-target Δ is:
   - L3-S4 (Triggers): D → A
   - L3-S5 (Sequences): D → A
   - L3-S6 (DEFAULT): D → A
   - L3-S7 (Computed): D → A
   - L3-S8 (CHECK): D → A
   - L3-S9 (ExtendedProperties + Descriptions): D → A
   - L3-S10 / L3-I10 (Catalog coordinate): D → A
   - L3-CC4 (IR fidelity for production): D → A
   - L3-Boundary-NoSilentDrop: D → A (the completion criterion; verified by property test in the final slice)

7. **Multi-session chapter; chapter-mid-audit at session 3–5.** Per `DECISIONS 2026-05-19`, sessions 3–5 trigger a cross-document audit subagent dispatch (CRITICAL / MINOR / OPEN classification; Active deferrals scan required). The chapter-close ritual operates at the end with eight items including the **L3 audit step** per the verifiability-triangle cadence (`DECISIONS 2026-05-12`).

8. **No port lifts in this chapter.** `ICatalogReader` stays at Position B (chapter 3.2 disposition). Adding fields to `KindRow` / `AttributeRow` is a record-extension, not a new source. The Position-A trigger ("a true second catalog source") remains chapter-3.x DacpacEmitter / OData-adapter territory.

## Slice plan (7-9 substantive slices)

Order chosen by **risk × leverage × prerequisite chain**.

**Status:** ✅ shipped | 🟡 next | ⚪ future | ⏸ deferred-with-trigger.
**Mode** (per `DECISIONS 2026-05-15 — Closed-DU empirical-test discipline refinement`):
  - *literal-site* — the new field carries semantic ambiguity; test fixtures stay explicit and the agent walks every site.
  - *builder-mediated* — additive with a sensible default; test fixtures go through `Fixtures.attribute / kind / module' / catalog` builders and future fields touch only the builder.

| Slice | Scope | L3 axioms promoted | Risk | Mode | Status | Commit |
|---|---|---|---|---|---|---|
| **α** | `Kind.Description` + `Attribute.Description` (purely additive) | L3-S9 (descriptions sub-axiom) | Low — pattern-establishing | additive (pre-refinement) | ✅ Shipped | `3c75d00` |
| **β** | `Module.IsActive` + `Attribute.IsActive` (carry-through; retire boundary filter) | L3-S9 (IsActive sub-axiom; supersedes session-21) | Medium — semantic shift; DECISIONS amendment required | literal-site audit | ✅ Shipped (pillar 9 first worked example) | `014d5d1` |
| **γ** | `Catalog.Triggers : Trigger list` + `Trigger` value type + `Fixtures` builders + adapter pickup | L3-S4 | Medium — new top-level Catalog field | builder-mediated (first worked example) | ✅ Shipped | `16ab57d` |
| **δ** | `Catalog.Sequences : Sequence list` + `Sequence` value type + `SequenceCacheMode` DU + adapter pickup | L3-S5 | Medium — sibling of γ | builder-mediated (second worked example) | ✅ Shipped | (this slice) |
| **ε** | `Attribute.DefaultValue : SqlLiteral option` + `Attribute.Computed : ComputedColumnConfig option` + `Kind.ColumnChecks : ColumnCheck list` (Attribute / Kind body expansions) | L3-S6, L3-S7, L3-S8 | Medium — three related additions; share adapter machinery | builder-mediated (additive) | 🟡 Next | — |
| **ζ** | `ExtendedProperties: ExtendedProperty list` on Module / Kind / Attribute / Index | L3-S9 | High — four-level extension; widest blast radius | builder-mediated (additive) | ⚪ Future | — |
| **η** | `ModalityMark.Temporal of TemporalConfig` (DU widening for temporal tables) | (covered by L3-S4 family; sub-axiom pending) | High — only DU-widening slice in chapter; closed-DU discipline applies | literal-site audit (DU widening) | ⚪ Future | — |
| **θ** | `TableId.Catalog : string option` extension | L3-S10 / L3-I10 | High — invasive; touches every `TableId` literal site | literal-site audit (every TableId site) | ⚪ Future | — |
| **ι** | IsExternal / Origin mapping audit + final L3-Boundary-NoSilentDrop property test | L3-CC4 + completion criterion | Low — property tests only; no IR change | property tests only | ⚪ Future (chapter close) | — |

**Deferred-out-of-A.0'** per `V2_PRODUCTION_CUTOVER.md` §11.5:
- `OriginalName` (prior attribute names) — renames handled at cutover, not embedded.
- `ExternalDatabaseType` — V2's `PrimitiveType` abstraction is intentional per A13.
- `IndexColumnDirection` (per-column asc/desc) — vestigial per 2026-05-10 convention.
- `IsPlatformAuto` index flag — presentation-only.

## Out of scope

- **Emitter consumption of new fields.** Each lift is IR-carriage only. Downstream emission (extended-properties DDL; trigger CREATE statements; sequence DDL; check-constraint DDL) lands per-consumer when chapter 4.1.A slice 8 / chapter 3.x DacpacEmitter / chapter 4.4 RemediationEmitter need it.
- **`Module.create` invariant strengthening for new fields.** The §6.4.5 / §6.4.6 cross-field invariants batch (Campaign B core) is a sibling chapter, not A.0'. A.0' adds per-field smart constructors where the new value type carries its own invariants; cross-field invariants stay deferred.
- **`SnapshotJsonBuilder` (V1 → V2 round-trip) extension.** V1's projection may or may not carry the new field today; the adapter reads defensively (`getOptionalString` / `getOptionalInt`); the SnapshotJsonBuilder catch-up is a downstream consumer's concern.
- **C# SqlClient loader project.** Still deferred per `LiveOssysConnection` reserved variant; A.0' extends the in-memory rowset DTO surface, not the live-connection path.

## What success looks like

**End of slice α (this session, opening):** `Kind.Description` and `Attribute.Description` land as `string option` fields; OSSYS adapter populates from JSON `description` field (defensive — None when absent) and from extended `KindRow.Description` / `AttributeRow.Description`; new property tests verify roundtrip preservation on both paths. ~1128 → ~1132 tests, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`, lint clean across 27 rules. The slice-pattern is established for slices β–θ to inherit.

**End of chapter A.0' (~3-4 weeks; 7-9 slices):** every concept in `V2_PRODUCTION_CUTOVER.md` §3.3 has either a typed Catalog field (with smart constructor where invariants demand) or a structured-error path at the OSSYS-adapter boundary; differential property tests verify roundtrip on operator's representative workload; L3-Boundary-NoSilentDrop verified by the slice-ι property test `NoSilentDropTests.``every-V1-concept-either-carried-or-errored```; chapter-close ritual operated with L3 audit step + Active deferrals scan + Tolerance forward-signal accounting.

**Phase-A exit gate unblocked:** Phase-A's exit invariant (every Tier-1 L3 axiom reaches Bucket A or B; zero Bucket-D Tier-1 axioms) requires A.0' to complete. Chapter close hands off to the differential-testing soak (A.6).
