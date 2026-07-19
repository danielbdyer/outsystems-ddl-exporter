# THE_TWIN.md — the synthetic data sidecar for the ejected SSDT estate

**Status: CHARTER BUILD COMPLETE (2026-07-18).** M0–M5 shipped and green: the schema
loop, the mint loop (K1/K1b/K1c), evidence tiers (import both renditions, derive, verify),
the scenario compiler (K2 date windows, weights, ratios, pins), and the proof surface
(`check`, `classify`, `bake`, the ejection dry-run). Witness pools at close: kernel fast
green · kernel synthetic Docker green · Twin pure 65 · Twin Docker 6. Provenance: the
`data-twin` deferral (DECISIONS 2026-05-19), cashed out and closed by the two
`DECISIONS 2026-07-18` entries.

---

## 1 — The mission

After the eject, the SSDT repository is the schema's only truth — no upstream to
re-derive from, every change a hand edit. The Twin stands beside that repository and
keeps one promise:

> **One command holds a local SQL Server current with the repository's definitions and
> fills it with deterministic, masked, distribution-faithful synthetic data.**

The engine this rides on is the projection kernel's synthesis surface — σ : Profile →
Data with its π ∘ σ ≈ id proof, the hybrid-by-cardinality privacy contract, the
corrections/Faker realization, the FK-aware dependency-ordered load. The Twin adds a
**context around** that engine, never features inside it: the post-eject identity model,
the estate ingestion, the container lifecycle, the evidence tiers, the scenarios.

The Twin is a different mission from the engine's (publication-and-provenance,
terminating at the eject). It is built inside this solution to reuse the kernel, and
shaped from day one to peel off as a standalone tool (§8).

## 2 — The language

| Term | Meaning |
|---|---|
| **the estate definition** | The SSDT repo's table scripts + schema scripts + static-data lanes — the sole schema truth |
| **the twin** | The persistent local Docker SQL Server the tool maintains to match the estate definition |
| **coordinate** | A logical name path — `schema.table` / `schema.table.column` — the Twin's identity |
| **evidence** | Captured distribution facts bound to coordinates (counts, null rates, cardinalities, frequencies, percentiles, fan-out) |
| **shape tier / rich tier** | Committed literal-free evidence vs full evidence kept out of the repo |
| **scenario** | A named declarative overlay rewriting evidence + volumes + pins for one purpose — never a second generator |
| **mint** | One deterministic generation run: (estate, evidence, scenario, corrections, seed) → rows |
| **pin** | An exact operator-authored row seeded beside the synthetic mass |
| **correction** | The kernel's reviewable PII/realism classification artifact, adopted as-is |
| **fingerprint** | The hash of everything a materialization depends on; equality ⇒ `up` has nothing to do |
| **source** | A database evidence imports from — `logical` rendition (on-prem names match directly) or `physical` (OutSystems cloud, mapped through the capture-side catalog) |

## 3 — The laws (each executable; the citing test is the law's witness)

1. **Convergence** — after a green `up`, `up` again applies nothing
   (`TwinSchemaLoopTests``law 1``; the fingerprint pair in `[twin].[__state]`).
2. **Coordinate totality** — every coordinate in config/artifacts resolves against the
   estate definition or refuses by name and location (`TwinIdentityTests``law 2``;
   `twin.coordinate.*.unknown`).
3. **Shape-tier literal-freedom** — the committed evidence pack carries no captured
   literal value (`EvidenceTests``law 3``; asserted again at the file grain in the
   evidence-loop E2E).
4. **Collision refusal** — a table claimed by two evidence sources is a parse-time
   refusal naming the table and both sources (`TwinConfigTests``law 4``).
5. **Twin self-description** — the `[twin]` schema (one single-row state table) is the
   tool's only write outside the estate's own objects, excluded from schema comparison.
6. **Inherited and re-asserted at the twin's grain** — T1 determinism of the mint
   (`TwinMintLoopTests``T1``; byte-identical re-mints by construction under K1c),
   `S-stable` stability across schema versions (σ content-addresses every value to
   `(master, kind, column, row)`, so a schema edit re-mints only the changed columns and
   holds the rest byte-identical — `SyntheticDataTests``S-stable``; DECISIONS 2026-07-19),
   zero FK orphans (`K1 + L1`), the privacy contract (the evidence-loop E2E's
   never-re-emitted source emails), and π ∘ σ ≈ id productized as `twin check`
   (model → publish → lanes → mint ×2 → zero orphans + identical digests +
   preserved-vocabulary re-profile).

## 4 — Identity: the anti-corruption layer

The ejected estate carries no SsKey bindings; the kernel keys everything on SsKey. The
bridge (`Twin.Core/TwinIdentity.fs`):

- The twin catalog is the **`ReadSide` read-back of the twin database**. Absent
  `Projection.SsKey` extended properties, ReadSide synthesizes deterministic name-based
  keys — a pure function of the names, stable run to run by construction.
- `CatalogIndex.bindKind`/`bindColumn` bind coordinates to kinds/attributes by exact
  case-insensitive name (the collation's semantics); `TwinIdentity.coordinateOf*` is the
  reverse projection every artifact is written in.
- **No Twin artifact contains an SsKey.** An SsKey lives one process run. This is the
  masking boundary, the no-SsKey answer, and the reason the artifact format survives
  ejection unchanged.

## 5 — Masking: three provable planes

1. **At rest** — the committed shape tier carries no captured literal (law 3).
2. **At mint** — the kernel's privacy contract: a real value from a high-cardinality
   column is never emitted (hybrid-by-cardinality; kernel law + test).
3. **At presentation** — classified PII renders through the kernel's deterministic Faker
   realization (seeded by row identity; locale-aware; provably never sourced from
   production values).

## 6 — Architecture

```
src/Twin.Core      pure: coordinates, estate definition, fingerprint, identity ACL,
                   twin.json IR + parser (closed schema, located refusals, D9)
src/Twin.Runtime   I/O: estate files (glob), container manager (docker CLI, persistent,
                   warm-sql.sh conventions), DacFx model build + publish, static lanes,
                   read-back, [twin].[__state], the mint orchestration
src/Twin.Cli       exe `twin`: verbs, VOICE-register rendering, the projection exit-code
                   vocabulary (0 done · 1 argv · 4 docker unavailable · 6 config · 9 refused)
tests/Twin.Tests               the pure pool (laws 1–4 witnesses, parser, fingerprint, ACL)
tests/Twin.Tests.Integration   the Docker pool (collection "Twin-Docker"; own containers
                               on ports 21533/21633 — never the warm projection container)
```

**The kernel manifest** (enforced by `Twin.Tests/BoundaryTests.fs`; a wider reference is
a boundary defect):

- `Twin.Core` → `Projection.Core` only.
- `Twin.Runtime` → `Twin.Core`, `Projection.Core`, `Projection.Adapters.Sql` (ReadSide,
  LiveProfiler), `Projection.Pipeline` (Deploy.executeBatch, TransferResume.wipeFkOrdered,
  Transfer.runSynthetic, FakerRealization, ConnectionSpec), `Projection.Targets.SSDT`
  (Render.quote/tableQualified, DacFx), `Projection.Targets.Json` (ProfileCodec,
  CorrectionCodec), `Projection.Targets.Data`.
- Nothing in `Projection.*` references `Twin.*`.

**The mint's data flow** (`Twin.Runtime/Runs.fs`):

```
fingerprints match? ── yes ─► "Nothing to apply."
        │ no
ensure container (named, port-pinned, persistent; wait-ready probe)
build estate model → dacpac (TSqlModel.AddObjects per authored file — a repo that
        does not model refuses with the file named) → DacServices.Deploy
        (drop-not-in-source; security objects excluded; data re-mintable by definition)
clean slate: nullable deferred-FK columns nulled → child-first wipe →
        identity counters reseeded to the declared seed (the last_value guard
        normalizes SQL Server's virgin-vs-deleted RESEED asymmetry)
apply the estate's static lanes (its own reference data, verbatim)
ReadSide read-back → twin catalog; row-carrying kinds ARE the lane-seeded set
        (by observation — no config names it) → K1 provided pools
σ mint (Transfer.runSynthetic; corrections realize at the boundary) → load
write fingerprints to [twin].[__state] → the VOICE report
```

## 7 — The operator surface

```
twin up      [--scenario <name>]   container present → schema current → data present; ~1s no-op when current
twin seed    [--scenario <name>]   force a fresh mint, reproducibly
twin status  [--scenario <name>]   what the twin holds against what the repo defines
twin down / twin reset             stop (state kept) / remove (data gone)
twin init                          write a starter twin.json
twin check                         (M5) the proof on a throwaway database
twin evidence import|derive|verify (M3) the evidence lifecycle
twin bake                          (M5) the distributable pre-seeded image (DockerImageEmitter cash-out)
```

`twin.json` (closed schema; every unknown key refused by JSON path; secrets only as
`env:`/`file:` refs; full example in the M4 config reference):

```jsonc
{
  "estate":    { "tables": "Modules/**/*.sql", "schemas": "Schemas/*.sql", "staticData": ["Data/StaticSeeds.sql"] },
  "container": { "name": "twin-mssql", "port": 21433, "image": "…mssql/server:2022-latest", "password": "env:TWIN_SQL_PASSWORD" },
  "evidence":  { "shape": "twin/evidence.shape.json", "rich": "file:../secure/evidence.rich.json",
                 "sources": [ { "name": "on-prem-uat", "rendition": "logical", "conn": "env:UAT_CONN",
                                "tables": ["dbo.Customer", "dbo.Order"], "sampleRows": 100000 } ] },
  "corrections": "twin/corrections.json",
  "seed": 7, "scale": 1.0, "defaultRows": 100, "volumes": "flat",
  "scenarios": { "default": {},
                 "quarter-end": { "extends": "default",
                                  "tables": { "dbo.Order": { "rows": 50000,
                                              "columns": { "Status": { "weights": { "Open": 7, "Closed": 3 } },
                                                           "CreatedOn": { "between": ["2026-01-01","2026-03-31"], "skew": "late" } } },
                                              "dbo.OrderLine": { "perParent": { "dbo.Order": { "mean": 3.5 } } } },
                                  "pins": [ { "table": "dbo.Customer", "rows": [ { "Id": 1, "Name": "Canonical Test Customer" } ] } ] } }
}
```

Scenario semantics: **a scenario only rewrites evidence, volumes, corrections, and pins —
it never generates** (`Twin.Core/ScenarioCompiler.fs`). Weights reshape a text column's
vocabulary and force it Preserve (displacing a Synthesize classification, explicitly);
`between` windows a date or numeric column with `early`/`late`/`uniform` percentile skews
(dates ride K2's tick encoding); `perParent` — declared on the CHILD naming the parent —
derives the child's volume from the parent's resolved rows (uniform draws land the mean
fan-out); pins render per attribute type, merge AFTER Faker realization (verbatim), and
their keys join the FK-draw pools (K1b). Every override binds or refuses with the
scenario, coordinate, and expected shape named.

## 8 — Ejection (the peel, designed now, exercised at M5)

- The peel = move `Twin.*` + its two test projects to a new repository; kernel project
  refs become package refs (`dotnet pack` of the manifest's six projects) or a subtree
  copy. The boundary test IS the manifest's honesty.
- Artifacts carry coordinates only — the format survives the peel byte-identical.
- `Twin.Tests` borrows four fixture builders from `Projection.Tests.Support`
  (`kindKey`/`attrKey`/`mkTableId`/`mkModule` + `mkCatalog`); at the peel they get
  duplicated into `Twin.Tests` (noted in the test fsproj).
- The M5 dry-run: assemble tool + packed kernel + a fixture estate in a scratch repo and
  run the M1/M2 acceptance there; record the outcome here.

## 9 — Named boundaries (honest, current)

- **K2 is closed** (2026-07-18): date columns carry tick-encoded distribution evidence
  and sample through `RawValueCodec` — scenario date windows are ordinary rewrites.
- **The cardinality threshold is the masking pivot for unclassified columns**: ≤ τ (50)
  distinct values preserve by design (reference vocabularies). A sub-τ personal-data
  column must be classified (`twin classify`, the corrections artifact) to synthesize —
  the E2E suite pins the above-τ auto-synthesize side.
- **A self-referencing FK draws from the unaugmented own pool** (K1b's split view — the
  pool length is the row range).
- **`perParent` lands the MEAN fan-out via volumes**; skewed fan-out is the evidence
  plane's (`ForeignKeySelectivity`, F5a).
- **v1 re-mints on every schema change** (data-preserving refresh is future work) — but
  the re-mint is **stable across the schema change** (`S-stable`, DECISIONS 2026-07-19):
  σ content-addresses every value to `(master, kind, column, row)`, so a schema edit
  perturbs only the columns it touches and every other column re-mints byte-identical. This
  is the axis the whole test bed hangs on — the re-mint being deterministic is not enough;
  it must hold the unchanged data FIXED so a v1↔v2 test difference is the schema, not the
  dice. The `--dry-run --explain` view of the per-column resolution chain is the adjacent,
  still-deferred inspectability slice (named in the DECISIONS entry).
- **Evidence-free realism needs a corrections artifact** — without one, columns mint as
  shaped tokens; `twin init`'s proposer scaffolds the classification (M5 wires it).
- **`--watch`** deferred behind the same reconcile loop (operator decision 2026-07-18).
- **Composite/natural PKs** inherit the kernel's named v1 boundary.
- **L3 joint synthesis** stays bounded by the kernel's F5b surface.
- The Twin assumes a reachable Docker daemon; absence is the calm exit-4 refusal.

## 10 — Reading order for this context

`THE_TWIN.md` (this file) → `DECISIONS 2026-07-18 — The Twin opens` →
`THE_SYNTHETIC_DATA_DESIGN.md` + `THE_SYNTHETIC_DATA_FUZZING.md` (the engine underneath)
→ `THE_VOICE.md` (every operator string) → the two test pools (the laws, executable).
