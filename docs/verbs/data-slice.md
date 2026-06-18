# data-slice Verb

`data-slice` lifts a **use-case-scoped, referentially-complete subset** of rows out
of one OutSystems-backed SQL Server environment and lands it in another — preserving
not just the chosen rows but everything they reference (parents, enums, lookups), and
remapping surrogate keys for **DML-only** on-prem targets that lack `IDENTITY_INSERT`.
The slice definition is versioned config; running it produces either an on-demand
transfer or a committed golden dataset.

> Design: [`design-data-portability-subsetting.md`](../design-data-portability-subsetting.md).
> Specs: [M5.0](../implementation-specs/M5.0-data-portability-closure-selector.md),
> [M5.1](../implementation-specs/M5.1-capture-and-remap-loader.md),
> [M5.2](../implementation-specs/M5.2-natural-key-resolution.md),
> [M5.3](../implementation-specs/M5.3-golden-transfer-verification.md).
> Glossary: [`data-portability-glossary.md`](../data-portability-glossary.md).
> Status: planned (specification stage).

---

## ✅ Recommended Approach: scoped transfer between live environments

Extract a slice from a source connection and emit a self-contained, DML-only T-SQL
load artifact (capture-and-remap) plus a closure report.

```bash
dotnet run --project src/Osm.Cli -- data-slice \
  --model ./out/model.json \
  --config ./config/data-portability.json \
  --slice order-fulfillment-golden \
  --source-connection-string "Server=qa;Database=App;..." \
  --out ./out/slices
```

**Outcome**: `order-fulfillment-golden/` containing the load artifact, the closure
report, and (if natural keys are incomplete) a natural-key proposal file.

**Benefits**:
- Referentially complete by construction (closure-completeness invariant).
- No `IDENTITY_INSERT` required on the target; surrogate keys remapped at load time.
- Reuses existing target rows by natural key (no duplicates on a populated target).

---

## Alternative Mode: golden baseline (commit & reset)

Materialize the slice as a deterministic, version-controlled golden dataset that an
environment can be reset to.

```bash
dotnet run --project src/Osm.Cli -- data-slice \
  --model ./out/model.json \
  --config ./config/data-portability.json \
  --slice order-fulfillment-golden \
  --source-connection-string "Server=qa;Database=App;..." \
  --golden ./golden \
  --out ./out/slices
```

Or promote an already-extracted transfer to golden without re-querying the source:

```bash
dotnet run --project src/Osm.Cli -- data-slice \
  --promote ./out/slices/order-fulfillment-golden \
  --golden ./golden
```

---

## Key Behaviors

* **Scoped closure** – BFS from the slice roots, following per-relationship
  directives (`up` / `down` / `stop`); pulls only referenced parent rows, not whole
  tables; terminates at a referential fixed point.
* **Capture-and-remap load** – inserts without the PK, captures the server-assigned
  key via `OUTPUT`, and resolves child FKs by join against per-entity `#Map_*` tables.
* **Natural-key reuse** – matches sliced rows to existing target rows by declared (or
  profile-inferred, operator-confirmed) natural keys.
* **Cycle-safe** – FK cycles handled by the existing two-phase insert-then-update.
* **Verified** – optional post-load verification proves row counts, zero new orphans,
  and natural-key uniqueness on the target.

---

## CLI Contract

```
data-slice
  --model <path>                     # extracted OsmModel JSON
  --config <path>                    # data-portability.json (slice definitions)
  --slice <name>                     # which slice to materialize
  --source-connection-string <conn>  # source environment (read-only)
  [--out <dir>]                      # artifact output (default ./out/slices)
  [--golden <dir>]                   # also write a committed golden dataset
  [--promote <slice-dir>]            # promote an existing transfer to golden (no source query)
  [--profile <path>]                 # profile snapshot, for natural-key proposals
  [--verify-connection-string <conn>]# target, for post-load verification
  [--sampling-threshold <rows>]      # inherits dynamic-extraction sampling knobs
```

---

## Required Inputs

1. **Model** – an extracted `OsmModel` JSON (`extract-model` output).
2. **Config** – `data-portability.json` declaring `slices[]` (roots + edges +
   natural keys). See M5.0 for the schema.
3. **Source connection** – read-only access to the source environment.
4. *(Optional)* **Profile snapshot** – enables natural-key proposals from existing
   `UniqueCandidateProfile` data.

---

## Primary Artifacts (written to `<out>/<slice-name>/`)

| File | Description |
| --- | --- |
| `load.capture-remap.sql` | Self-contained DML-only load artifact (`#Stage_*`/`#Map_*`, `OUTPUT` capture, FK resolution, two-phase cycles). No `IDENTITY_INSERT`. |
| `closure-report.json` | Per-entity row counts, pull provenance (root/up/down), and any dangling-FK findings. |
| `natural-key-proposals.json` | Profile-inferred candidate keys for entities lacking a declared key (operator confirms into config). |
| `manifest.json` | Slice name, source env id, generated-at, counts, closure summary. |
| `verification-report.json` | *(with `--verify-connection-string`)* expected-vs-actual counts, new-orphan findings, natural-key uniqueness. |

Golden mode additionally writes `golden/<slice-name>/` with `manifest.json` and
deterministic `data/<Entity>.jsonl`.

---

## Workflow

1. **Define the slice** in `data-portability.json` (roots, edges, natural keys).
2. **Extract + emit**
   ```bash
   dotnet run --project src/Osm.Cli -- data-slice --model ... --config ... --slice ... --source-connection-string ...
   ```
   Review `closure-report.json`; resolve any dangling-FK or natural-key gaps.
3. **Apply** `load.capture-remap.sql` to the target (operator, or via the load harness).
4. **Verify** (optional) with `--verify-connection-string`; inspect `verification-report.json`.
5. **Promote** to golden once satisfied, if this slice is a baseline.

---

## Acceptance Checklist

* Closure report shows zero dangling mandatory FKs.
* Load artifact contains no `SET IDENTITY_INSERT`.
* Re-applying the artifact is idempotent on row counts (natural-key reuse).
* Post-load verification passes (counts match, zero new orphans, natural keys unique).
* Golden dataset (if produced) re-materializes byte-identically from the same source.
