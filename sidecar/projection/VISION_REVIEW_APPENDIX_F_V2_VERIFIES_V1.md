# Appendix F — V2 Verifies V1 (Dogfood Plan)

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`; VISION_REVIEW.md
**Brief:** Make V2 immediately useful by having it *verify V1's outputs* before V2 ships any new emitter. V2 becomes V1's canary right now; value before chapter 3 closes.
**Synthesis location:** `VISION_REVIEW.md`, `VISION_REVISION_2.md`

---

V1 emits per-table `.sql` files into `<outputDirectory>/Modules/<Module>/<Schema>.<Table>.sql` (plus a manifest.json), is driven by `BuildSsdtPipeline`/`FullExportPipeline`, exposes a CLI verb `BuildSsdt`/`FullExport`, has SqlClient already wired (`SqlClientOutsystemsMetadataReader`), and already runs testcontainers in-tree (`tests/Osm.TestSupport/SqlServerFixture.cs`, mssql 2022-CU15). The OSSYS source it reads is the `osm_model.json` shape produced by `SnapshotJsonBuilder`. V2 already has `Projection.Adapters.Osm/CatalogReader.fs` consuming exactly that JSON shape. **That is the seam.**

---

# V2 as V1's canary, before V2 emits anything

## 1. Architecture

```
                     OSSYS DB (or fixture rowsets)
                         |
            +------------+-------------+
            |                          |
  V1 SnapshotJsonBuilder       (same JSON re-fed)
  (Osm.Pipeline.SqlExtraction)         |
            |                          v
            v               Projection.Adapters.Osm.CatalogReader
   V1 BuildSsdtPipeline  ->  Catalog_expected : Catalog
   (Osm.Pipeline.Orch)              (F#, shipped)
            |                          |
            v                          |
     <outDir>/Modules/                 |
       <Module>/<Schema>.<Tbl>.sql ----+
            |                          |
            v                          |
   *** Projection.Pipeline (NEW C#) ***|
   - spin up SqlServerFixture          |
   - apply each .sql in topo order  ---+
            |                          |
            v                          v
  Projection.Adapters.Sql.ReadSide (NEW F#)
       reads INFORMATION_SCHEMA + sys.* via SqlClient
            |
            v
   Catalog_observed : Catalog
            |
            v
   Comparator (F#)  : equalModulo Tolerance Catalog_expected Catalog_observed
            |
            v
   Diff -> Diagnostics, exit code, PR gate
```

Placement:
- **Projection.Pipeline** (C#, `sidecar/projection/src/Projection.Pipeline/`) — orchestrator. It is the *only* place that touches DacFx/testcontainers/file paths; the F# core stays pure. It depends on Osm.TestSupport's SqlServerFixture pattern (probably extract `MsSqlContainer` boot into `Projection.Pipeline.Ephemeral`).
- **Projection.Adapters.Sql.ReadSide** (F#, `Projection.Adapters.Sql.ReadSide/`) — `Task<Result<Catalog>> readCatalog(connStr, schemas)`. Returns the same `Catalog` the Core already consumes. Same shape as `CatalogReader.parse`; same return-type discipline (CLAUDE.md "Async/Task in adapters only").
- **Comparator** (F#, `Projection.Core/Verification/CatalogEquivalence.fs` or `Projection.Verification/`) — pure. No I/O. Reuses `SsKey` keying and structural equality.
- **Seam between V1 and V2**: V1's emitted `.sql` directory is the only artifact V2 cares about for the input side. For the oracle side, V1's `osm_model.json` (already on disk in V1's `--snapshot` output) feeds CatalogReader. Both already exist; nothing in V1 needs to change.

## 2. Smallest read-side adapter

**Yes, skip DacFx Extract for now.** DacFx Extract → TSqlModel forces taking on Microsoft.SqlServer.DacFx, a TSqlModel→Catalog reverse projection (which is half of `DacpacEmitter` anyway), and binary-determinism plumbing — exactly what chapter 3.3 is for. The canary doesn't need any of it.

Minimum viable adapter: SqlClient against `INFORMATION_SCHEMA.TABLES`, `INFORMATION_SCHEMA.COLUMNS`, `sys.indexes` + `sys.index_columns`, `sys.foreign_keys` + `sys.foreign_key_columns`, `sys.check_constraints`, `sys.extended_properties`. Nine queries, one transaction, mapped into the same `Module/Kind/Attribute/Reference` records the Core already exposes. Roughly 300–500 lines of F#. Argument:

- The Catalog IR is already source-agnostic — it has no DacFx-shaped fields. Mapping `INFORMATION_SCHEMA` rows is straightforward.
- Two consumers from day one: canary read-back, and drift detection (VISION_REVIEW recommendation 2026-05-08). DacpacEmitter doesn't need this adapter at all.
- It's the same posture as `Projection.Adapters.Osm.CatalogReader` — boundary returns `Task<Result<Catalog>>`; Core stays sync and pure.

When DacpacEmitter lands, an alternative `DacFxReadSide` may earn its place; the minimal adapter is not thrown away — it's the cross-check oracle (see §6).

## 3. V1 hooks

**Best case applies.** V1 already produces the two artifacts the canary needs:
- The emitted SSDT directory (`SsdtEmitter.EmitAsync` → `<outDir>/Modules/...`, manifest at `<outDir>/manifest.json`). Path is operator-known via `BuildSsdtVerbOptions`.
- `osm_model.json` from `SnapshotJsonBuilder` (already a `--snapshot` output of `extract-model` / `build-ssdt`).

**No V1 code changes.** The Azure DevOps pipeline gains one step after `build-ssdt`: invoke `Projection.Pipeline verify --emitted <outDir> --snapshot <osm_model.json>`. If snapshot wasn't persisted in a given pipeline lane, add `--persist-snapshot` to V1 — that's the *only* V1 hook, and even that is optional (re-extract from DB in the canary if needed, since `SqlClientOutsystemsMetadataReader` is library-callable).

## 4. Comparator

Tolerances V1's emission deliberately introduces and V2's expected Catalog will not carry verbatim:
- Index naming conventions (V1 prefixes/suffixes; observed names match V1's convention, expected names are SsKey-derived).
- CHECK constraints emitted by V1 templates that V2's Catalog doesn't model (Static-entity discriminators, system constraints).
- Extended-property metadata (descriptions, MS_Description) — V1 emits, V2 may not produce.
- Collation/ANSI defaults at column level when V2 models them implicitly.
- Computed columns and column ordinals (deploy order ≠ source order).
- Default-constraint *names* (V1 generates deterministic names; SQL Server may have its own).

Shape:

```fsharp
module CatalogEquivalence

type Tolerance = {
    IgnoreIndexNames        : bool        // compare by (columns, uniqueness, filter)
    IgnoreCheckConstraints  : Set<SsKey>  // V1-only checks scoped per-kind
    IgnoreExtendedProperties: bool
    IgnoreDefaultNames      : bool
    AttributeOrderInsensitive: bool
}

type Divergence =
    | KindMissing           of SsKey * Side
    | AttributeMismatch     of SsKey * AttributeDelta
    | ReferenceMissing      of SsKey * Side
    | IndexShapeMismatch    of SsKey * IndexDelta
    | UnexpectedExtra       of SsKey * Side * string

and Side = Expected | Observed

type Diff = { Divergences: Divergence list }   // empty = pass

val equalModulo : Tolerance -> Catalog -> Catalog -> Diff
```

The compare keys on `SsKey` root (T11 sibling-Π commutativity is the surface). Tolerances are *named* per V1 emission choice; the default `Tolerance` profile is calibrated empirically by running the canary against a real V1 output and pruning each false-positive class to a named tolerance with a citation. This converts "V1's quirks" into machine-readable record.

## 5. Value timeline

**Step 1 — Today, with what's shipped.** V2 verifies *content equivalence* of V1's `osm_model.json` round-trip: read it via `CatalogReader`, project through `JsonEmitter`, diff against V1's persisted snapshot. If V1's JSON is malformed or carries identifiers V2 cannot key, the canary fails. This is JSON-level parity — no read-side, no DB. Catches OSSYS-adapter regressions and JSON-projection drift. Worth wiring this week.

**Step 2 — Read-side adapter lands (chapter 3.x).** The full canary above. V1 emits → ephemeral DB → `INFORMATION_SCHEMA` extract → `equalModulo`. This delivers structural verification of V1's SSDT output without V2 ever generating SQL. **This is the moment V2 earns its keep**: every PR runs this; a real V1 emitter bug surfaces as a `Divergence` rather than as production CDC noise.

**Step 3 — DacpacEmitter (chapter 3.3).** Replace V1's SSDT with V2's DACPAC; re-run the same canary. Now V2 verifies V2. Add the redeploy-zero-ALTER assertion (R2). The cutover-window governance rule (R6) flips from "V2 verifies V1" to "V2 emits, canary verifies V2 against itself plus against a third oracle (the OSSYS source)."

Each step ships independently. Step 2 is the inflection point.

## 6. V1-bug attribution: triangulate

Single-oracle verification can't find V1 bugs. Use **three Catalogs**, two pairwise diffs:

- `C_ossys` — `Projection ∘ CatalogReader(osm_model.json)` — V2's pure expected.
- `C_v1` — `ReadSide(deploy(V1 SQL))` — what V1 actually built.
- `C_round` — `Projection ∘ ReadSide(deploy(V1 SQL))` (apply V2's passes to the observed catalog).

Three diffs, three different attributions:
- `C_ossys ≡ C_v1`: V1's output matches OSSYS truth. Pass.
- `C_ossys ≢ C_v1` and `C_round ≡ C_v1`: V1 emitted what it *intended* but V1's intent diverges from OSSYS. **V1 bug** (or V1 intentional divergence — surface it for ADMIRE classification).
- `C_ossys ≡ C_round` and `C_ossys ≢ C_v1`: round-trip through V2 reconciles, V1's emitted bytes drifted. **V1 emission bug** (formatting, ordering, encoding).
- All three disagree: read-side adapter or comparator tolerance is wrong. **V2 bug**, fix before promoting any verdict.

Frame the comparator output as `Divergence × AttributedSource`. The CLI surface in `Projection.Pipeline` should print the triangulation, not just a yes/no. This is the same shape ADMIRE.md uses; `extracted (with-divergence)` is its outcome.

## 7. Chapter-3 re-sequencing

**Yes, pull the read-side adapter to 3.1.** Original Appendix D ordering:
1. SnapshotRowsets, 2. Read-side, 3. DacpacEmitter, 4. Canary, 5. RefactorLogEmitter.

New ordering, justified by "V2-augmented immediately":
1. **Read-side adapter** (`Projection.Adapters.Sql.ReadSide/`) + minimal `Projection.Pipeline` shell + `CatalogEquivalence` comparator + triangulation. This is V2-augmented mode shipping today against V1.
2. SnapshotRowsets — still gates A1 for renames; still chapter 3.
3. DacpacEmitter — V2 now becomes its own input to the same canary, redeploy-zero-ALTER added.
4. RefactorLogEmitter.
5. Canary closure (mostly already built by step 1; this becomes the formal redeploy-assertion + multi-environment property test).

The reasoning: under the original ordering, the read-side existed only to close the loop on V2 emission, so it had to wait for an emitter. Under the V2-as-canary frame, the read-side has a consumer (V1 itself) the moment it ships. Cutover-fallback ladder also benefits — V2-augmented becomes a real, exercised path well before T-30, not a fallback drawn on paper.

DECISIONS entry to write: "Read-side adapter promoted to chapter 3.1 — V2-augmented mode is the immediate-value vehicle; supersedes Appendix D §5 sequencing." Re-open trigger: read-side adapter cannot be built without DacpacEmitter (it can — `INFORMATION_SCHEMA` is sufficient).

---

**Files cited:**
- `src/Osm.Emission/SsdtEmitter.cs` — V1 emits per-table `.sql` to `<outDir>/Modules/<Module>/<Schema>.<Tbl>.sql` plus `manifest.json`.
- `src/Osm.Emission/TableEmissionPlanner.cs` — emit layout.
- `src/Osm.Pipeline/Orchestration/BuildSsdtPipeline.cs` and `FullExportPipeline.cs` — V1 end-to-end.
- `src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs` — V1's `osm_model.json` producer.
- `src/Osm.Pipeline/SqlExtraction/SqlClientOutsystemsMetadataReader.cs` — already shows V1 is comfortable speaking SqlClient against live SQL Server; reuse pattern in `Projection.Adapters.Sql.ReadSide`.
- `tests/Osm.TestSupport/SqlServerFixture.cs` — testcontainers wiring (mssql 2022-CU15) is in-tree; `Projection.Pipeline` lifts it.
- `sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs` — shipped; consumes `osm_model.json`; oracle for `C_ossys`.
- `sidecar/projection/VISION_REVIEW_APPENDIX_D_SEQUENCING_PLAN.md` §6 — V2-augmented mode spec.
