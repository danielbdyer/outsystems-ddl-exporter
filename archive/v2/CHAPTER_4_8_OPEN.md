# Chapter 4.8 open — IRBuilders continuation + on-disk Index metadata bundle + isPlatformAuto emitter toggle

**Sessions:** opens with this document. **Posture:** three orthogonal cash-outs in one chapter, leveraging chapter 4.7's IRBuilders sweep + sibling-wrapper discipline. **Predecessors:** chapter 4.7 (IRBuilders sweep on Index + Reference; sibling-wrapper discipline codified).

---

## Why this chapter

The chapter 4.7 close shortlist's per-item touch profile shifted significantly after the IRBuilders sweep + discipline codification. Re-evaluation surfaced three items where the post-4.7 cost dropped meaningfully OR the operational value justifies execution now:

1. **IRBuilders sweep continuation** — chapter 4.7 slice γ migrated Index + Reference literals; Attribute / Kind / Module / Catalog literals were out-of-scope. Same Python migration pattern scales to those types; unlocks cheap future work on `OriginalName` + `ExternalDatabaseType` (Attribute fields) + any future Kind / Module / Catalog field additions.
2. **On-disk rich Index metadata** — V1's `IndexOnDiskMetadata` carries `FillFactor` / `IsPadded` / `AllowRowLocks` / `AllowPageLocks` / `NoRecomputeStatistics`. Each is an additive Index field; post-chapter-4.7's IRBuilders sweep, each costs ~2 sites (mkIndex default + per-test override).
3. **isPlatformAuto emitter consumption** — IR field shipped at chapter 4.6 slice β with no emitter consumer. V1's pipeline gates platform-auto index inclusion via `SsdtManifestOptions.IncludePlatformAutoIndexes`. V2 lifts the toggle to `EmissionPolicy` + has the SSDT emitter consult it at index emission time.

---

## Strategic frame — eight axes named at chapter open

1. **DDD — three orthogonal cash-outs.** Slice α is purely test-fixture mechanical migration; slice β is additive IR record-extension; slice γ is a Policy-axis + emitter consumption pair. Each has its own type-system surface.

2. **FP — pure refactors; pure additions; pure pass.** No new I/O; no new side effects. Slice γ's emitter filter is a pure predicate on Index.

3. **Hardcore — no string-concatenation paths touched.** All slices operate at type-system level.

4. **Streaming — no new bench scopes needed; existing `emit.ssdt.indexStatements` covers slice γ's filtered emission.**

5. **Hexagonal — adapter pickup for slice β** (V1's JSON projection carries IndexOnDiskMetadata fields if present; today V1's `osm_model.json` doesn't surface them but the IR fields are positioned for when DACPAC adapter or rowset slice carries them).

6. **Built-in obligation — ScriptDom typed AST for slice β's IndexOptions emission.** `CreateIndexStatement.IndexOptions` is `IList<IndexOption>`; each option type is a typed DU. No string-concat.

7. **Aggregate-root + smart constructor — slice β's new IR fields are non-invariant-bearing primitives** (`bool` / `int option`). No new smart constructors needed.

8. **Test-fidelity — per-axis tests.** Slice α verified by full-baseline test run post-migration (semantics-preserving). Slice β tests cover each new field's adapter pickup + emission (each contributes a non-default value to a fixture; asserts the rendered SQL contains the corresponding option). Slice γ tests cover the EmissionPolicy toggle's effect on the rendered index set.

---

## Slice arc

| # | Slice | Goal | LOC budget |
|---|---|---|---|
| α | IRBuilders continuation sweep: Python pass extended to Attribute + Kind + Module + Catalog literals; ~40-60 literal-site migrations | Future Attribute / Kind / Module / Catalog IR-field additions touch ~2 sites instead of ~30 each | ~120 src + ~300 test-file touches via Python pass |
| β | On-disk Index metadata bundle: 5 additive Index fields (`FillFactor` + `IsPadded` + `AllowRowLocks` + `AllowPageLocks` + `NoRecomputeStatistics`) + adapter scaffolding + ScriptDom IndexOptions emission | V2 IR + emission gain V1-parity on the storage/perf knobs V1's `IndexOnDiskMetadata` carries | ~200 src + ~120 test |
| γ | `EmissionPolicy.IncludePlatformAutoIndexes : bool` + emitter filter consuming `Index.IsPlatformAuto` | V1's `SsdtManifestOptions.IncludePlatformAutoIndexes` toggle lifted to V2; operator can filter platform-auto indexes from the SSDT bundle | ~80 src + ~60 test |
| δ | V1 differential consolidation + chapter close ritual | 8-item ritual discharged | ~60 test + close ritual |

**Total: ~400 LOC src + ~500 test-file touches.** Estimated 4 slices at session cadence.

---

## What this chapter does **not** do

- **No new chapter-blocking work.** The three slices are independent; each can ship + revert without affecting the others.
- **No IndexColumnDirection migration.** Record-modification still painful even post-sweep; deferred per chapter 4.6 close.
- **No partition / data compression Index metadata.** V1 carries these as separate composite types (`IndexPartitionColumn` / `IndexPartitionCompression` lists); they're substantially more complex than the simple boolean/int-option knobs in slice β scope. Deferred.

---

## Companion documents

- **V1 reference shapes:** `src/Osm.Domain/Model/IndexOnDiskMetadata.cs:1-64` (FillFactor / IsPadded / AllowRowLocks / AllowPageLocks / NoRecomputeStatistics + others); `src/Osm.Emission/SsdtManifest.cs:25-29` (SsdtManifestOptions.IncludePlatformAutoIndexes).
- **V2 surface to extend:** `Catalog.fs` Index record (additive fields); `Policy.fs` EmissionPolicy record; `CatalogReader.fs` parseIndex; `ScriptDomBuild.buildCreateIndex` IndexOptions emission; `SsdtDdlEmitter.indexStatements` filter.
- **Strategic frame precedents:** `CHAPTER_4_7_OPEN.md` (sibling refactor chapter); `CHAPTER_4_5_OPEN.md` (Index IR fidelity precedent).

---

## Open questions resolved at chapter open

**Q1 — Default values for Index on-disk metadata.** V1's `IndexOnDiskMetadata.Empty` carries: `FillFactor = null`, `IsPadded = false`, `AllowRowLocks = true`, `AllowPageLocks = true`, `NoRecomputeStatistics = false`. Decision: V2 mirrors V1's defaults exactly. `mkIndex` updates accordingly.

**Q2 — Emission of default values.** SQL Server CREATE INDEX's `WITH (...)` clause is omitted entirely when all options are at defaults. Decision: V2 emits `WITH (...)` only when at least one option diverges from default. Per-option mapping: `FillFactor = Some n` → `FILLFACTOR = n`; `IsPadded = true` → `PAD_INDEX = ON`; etc.

**Q3 — `IncludePlatformAutoIndexes` semantics.** V1 defaults to `true` (include platform-auto indexes). V2 mirrors. The toggle's effect at emission: when `false`, `SsdtDdlEmitter.indexStatements` filters out `Index.IsPlatformAuto = true` indexes from the rendered output. Manifest's predicate coverage still includes them (the predicate is "what could be there"; the toggle is "what gets emitted").

**Q4 — IRBuilders continuation sweep granularity.** Slice α extends the Python pass to Attribute / Kind / Module / Catalog literals. Decision: migrate every literal site that matches the IRBuilders.mkX signature default exactly; literals with mid-comment quirks (like the `UserFkReflowIntegrationTests.fs` Reference) stay as-is.

---

## AXIOMS amendment scan at chapter open

No new axiom candidate. Chapter operates within existing axioms — slice α is semantics-preserving refactor (T1 byte-determinism unchanged); slice β additive record-extensions (A39 invariants preserved); slice γ is a Policy axis (A18 amended preserved — the toggle lives in EmissionPolicy, consumed by emitter; A18-amended structural type forbids Policy-in-emitter, satisfied by the emitter receiving the toggle's-effect-as-filter via composition).

---

## Closing

Chapter 4.8 is **leverage-realizing infrastructure work** — three orthogonal cash-outs that compound the chapter 4.7 IRBuilders sweep + sibling-wrapper discipline. Slice α extends the sweep coverage; slice β demonstrates the post-sweep ~2-site-per-field cost on 5 real fields; slice γ wires up an IR field that's been sitting unused since chapter 4.6.

Per V2_DRIVER's per-axis correctness stakes, this is **cross-cutting infrastructure + Schema-axis polish work**. Lower per-axis stakes individually; cumulative cutover-fidelity weight as V2's emit surface gains V1 parity on storage-tuning knobs.

Slice α opens.
