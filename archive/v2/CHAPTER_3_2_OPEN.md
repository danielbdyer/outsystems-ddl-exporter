# Chapter 3.2 open — `SnapshotRowsets` variant of `SnapshotSource`

**Branch:** `claude/review-ddl-exporter-zB3LF`. **Predecessor:** chapter-2 OSSYS adapter (sessions 13-25; `CHAPTER_2_CLOSE.md`); chapter-2-close subagent #5's pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` (load-bearing).

This chapter closes the **JSON-projection-lossiness class** (per `DECISIONS 2026-05-19 — naming the two classes of resolution patterns explicitly`) by adding a third variant to `SnapshotSource` that consumes V1's pre-aggregation rowset surface directly. SsKey arrives natively (instead of name-synthesized via `OS_MOD_*` / `OS_KIND_*` / `OS_ATTR_*` conventions); A1's bound on identity-survives-rename through the JSON path resolves; future class members (`EspaceKind`, `IsSystemEntity`) gain a clean carriage path.

## Strategic-frame axes (per DECISIONS 2026-05-15 chapter-open shape)

1. **Resolves a load-bearing forced-bound, not adds a feature.** A1's "identity survives rename" bound is *unconditional* in V2's algebraic intent but *bounded by JSON-projection lossiness* in the operational reality. SnapshotRowsets unbounds it. This is the chapter's V2-driver KPI cash-out — Phase 7 per `V2_DRIVER.md` table — and the structural prerequisite for cross-version identity stability under V2-driver mode.

2. **Closed-DU expansion empirical-test discipline applies and predicts cleanly.** Adding `SnapshotRowsets of RowsetBundle` to the existing `SnapshotSource` DU should produce F# exhaustiveness errors only at the `parse` match site inside `Projection.Adapters.Osm.CatalogReader`. The two existing `parseJsonString` / `parseDocument` paths are untouched. `parseRowsetBundle` is the new sibling pure-translation function. The pre-scope at §4 names this prediction; this chapter validates it empirically.

3. **The carrier is a value type, not a connection / reader / I/O handle.** `SnapshotRowsets of RowsetBundle` carries an in-memory record bundle; the F# adapter sees data, not effects. The future C#-shell loader (`Projection.Adapters.Osm.SqlClient`; pre-scope §2-§3; out of slice-1 scope) is the I/O surface that pulls the rowsets and materializes them into the bundle. Hexagonal architecture: I/O above the adapter, pure translation below.

4. **DTO surface is hand-written F#, not type-provider-derived, not C#-shared.** Per pre-scope §3: the SqlClient type-provider's compile-time DB requirement is a worse CI fragility surface than `JsonProvider`'s runtime-document-shape; rowset-shape evolution is gated by V1 SQL evolution (slow cadence). C# DTO sharing is forbidden by the cherry-pick discipline (`HANDOFF.md` — boundary is data, not typed cross-references). Hand-written F# records mirroring V1's `IOutsystemsMetadataReader.cs` shape is the fastest, simplest, most testable surface. V2-vocabulary names: `KindRow` (not `EntityRow`); `ModuleRow`; `AttributeRow`.

5. **SsKey-shape divergence per source variant is accepted; option 1 from pre-scope §4.** JSON path emits `Synthesized ("OS_KIND", [moduleName; entityName])` SsKeys; rowset path emits `OssysOriginal guid` SsKeys via `SsKey.ossysOriginal` (Identity.fs:70 — already prepared for this chapter). Cross-source parity tests (slice 5 deferred) will compare structural shape (kinds count, attributes per kind, etc.) without comparing SsKey identity directly. The deeper canonicalization (option 2: a `V1Mapped` SsKey carrying both forms via UUIDv5 derivation) is reserved for chapter 4.2 User FK reflow's `SourceTag` refactor.

6. **`ICatalogReader` port trigger does NOT fire here.** Per pre-scope §8 risk #4: `SnapshotRowsets` is a second *variant* of the existing OSSYS source, not a *second source*. The `ICatalogReader` Position B → A trigger ("a second catalog source materializes — DACPAC, OData, in-memory test reader") does not fire. The port stays at Position B. Chapter-mid-audit will scan this if the trigger interpretation drifts.

7. **First slice is heavier than chapter-2's first slice.** Chapter 2 opened with empirical-pressure fixtures + accumulating rules (six-slice OSSYS arc); chapter 3.2 opens with the canonical resolution shape (DTO surface + bundle path + variant scaffolding) and applies it to (re-)solve already-known questions. The mechanical groundwork is bounded; subsequent slices (2-4) re-do already-traced fixtures lightly under the new path.

8. **Close ritual paired with chapter 3 close.** Chapter 3 has accumulated three sub-arcs (3.1 closed; 3.5 / 3.6 / 3.7 / 4.1.A / 4.1.B-α/β/γ / RawTextEmitter retirement / Tier 1/2/3 substantively shipped, ritual deferred). Per `HANDOFF.md`, this chapter (3.2) is the final pre-chapter-4 sub-arc within chapter 3; its close runs the joint chapter-3 close ritual covering all of 3.1 / 3.2 / 3.5 / 3.6 / 3.7 substantive deliverables. Eight items per `CLAUDE.md` operating-disciplines table.

## Slice plan (5-6 substantive slices)

Per pre-scope §5 + §7:

| Slice | Scope | Lossiness members resolved |
|-------|-------|----------------------------|
| **1** (this slice) | `SnapshotRowsets` variant + `RowsetBundle` carrier + `ModuleRow`/`KindRow`/`AttributeRow` records + `parseRowsetBundle` minimum + first fixture mirroring session-18 minimal | SsKey at all three levels |
| **2** | Re-do session-19 reference-bearing fixture under the rowset path; FK SsKey carriage | (Refines reference SsKey synthesis; rule 16's same-module assumption tested against actual SsKey carriage) |
| **3** | Re-do session-20 external-entity fixture; activate `EspaceKind` | Rule 17 refines from `ExternalViaIntegrationStudio` placeholder to three-way real |
| **4** | New system-entity fixture; activate `IsSystemEntity` | Likely a V2 IR refinement (`Modality.System`? `Kind.IsSystem: bool`?) — boundary-discipline question, decided under empirical pressure |
| **5** | Cross-source parity tests (JSON ↔ Rowset for the same fixture) | (No new lossiness; validates structural-shape equivalence modulo documented SsKey divergence) |
| **Deferred** | Per-table column structure (rowset 6); check constraints (rowset 7); future members | Each surfaces under fixture pressure |

**Chapter close ritual** runs after slice 5, jointly with the deferred 3.5/3.6/3.7/4.1.A close rituals.

## Out of scope

- **C# SqlClient loader project** (`Projection.Adapters.Osm.SqlClient`). The bundle is a value type; how it gets materialized is an I/O concern that lives above the F# adapter. Slice 5+ or a separate "when LiveOssysConnection is reopened" trigger.
- **`LiveOssysConnection` variant**. Reserved per session-17 OSSYS adapter parse signature; deferred until V2 needs to operate without V1's chain in the loop entirely.
- **Triggers** (V1 rowset 18). Documented as not-carried-forward.
- **IR refinements driven by IsSystemEntity** (slice 4 surfaces them; the *resolution* lands in slice 4 work, not chapter open).
- **DacpacEmitter** (chapter 3.x; tracked-deferral in DECISIONS Active deferrals index; conditional on deploy-path requirement).

## What success looks like

**End of slice 1 (this session):** `SnapshotRowsets` DU variant lands; `RowsetBundle` carrier defined; minimum DTO surface (rowsets 1-3 only); `parseRowsetBundle` translates a fixture-bundle into a `Catalog`; new tests verifying the path round-trips a minimal fixture (one module, one kind, one attribute) with `OssysOriginal` SsKeys carried natively. Existing JSON-path tests untouched (closed-DU expansion empirical-test discipline holds). 866+ tests, 0 skipped, lint clean.

**End of chapter 3.2 (5-6 sessions):** all five slices shipped; cross-source parity tests green modulo the documented SsKey-shape divergence; A1's JSON-projection-lossiness bound resolved structurally; chapter close ritual operated jointly with the deferred 3.x close rituals.
