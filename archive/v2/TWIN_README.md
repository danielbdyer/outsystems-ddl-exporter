# The Twin — a synthetic-data sidecar for this repository

> This file is written for the SSDT repository the tool ships beside. Copy it
> (or link it) into that repository next to `twin.json`.

One command holds a local SQL Server current with this repository's table
definitions and fills it with deterministic, masked, distribution-faithful
synthetic data:

```
twin up
```

That is the whole daily loop. When nothing changed, it answers in about a
second: `Nothing to apply.`

## What the twin is

A persistent Docker SQL Server (`twin-mssql` by default) that mirrors the
repository: the table scripts publish to it through DacFx (the same engine
`sqlpackage` uses, so what deploys here deploys anywhere), the repository's
own static reference data applies verbatim, and every other table fills with
synthetic rows minted from declared distributions. The data is:

- **deterministic** — the same seed mints byte-identical rows, every time;
- **masked, three ways** — the committed evidence tier carries no captured
  literal values; a real value from a high-cardinality column is never
  emitted; classified personal-data columns render through seeded realistic
  generators (names, emails, phones) that are provably never sourced from a
  real database;
- **relationally sound** — every foreign key resolves; the repository's own
  seeded lookup rows anchor the references.

## The commands

```
twin up      [--scenario <name>]   converge container + schema + data; fast no-op when current
twin seed    [--scenario <name>]   re-mint the data, reproducibly
twin status  [--scenario <name>]   what the twin holds vs what the repository defines
twin check   [--scenario <name>]   the proof, on a throwaway database
twin evidence import               profile the configured sources into the rich pack (kept OUT of the repo)
twin evidence derive               project rich → shape: the committed, literal-free tier
twin evidence verify               bind both packs against the estate; per-table coverage
twin classify                      propose personal-data classifications from column names
twin bake                          a docker build context for a distributable schema image
twin down / twin reset             stop (state kept) / remove (data gone)
twin init                          write a starter twin.json
```

## twin.json in five minutes

```jsonc
{
  "estate": {
    "tables": "Modules/**/*.sql",            // where the table scripts live
    "schemas": "Schemas/*.sql",              // CREATE SCHEMA scripts, if any
    "staticData": ["Data/StaticSeeds.sql"]   // the repo's own reference data
  },
  "corrections": "twin/corrections.json",    // reviewable PII classifications (twin classify writes it)
  "evidence": {
    "shape": "twin/evidence.shape.json",     // committed: counts, null rates, cardinalities — no values
    "rich":  "file:../secure/evidence.rich.json",  // out-of-repo: full distributions
    "sources": [                             // where evidence imports from (a closed, collision-free set)
      { "name": "on-prem-uat", "rendition": "logical", "conn": "env:UAT_CONN",
        "tables": ["dbo.Customer", "dbo.Order"] }
    ]
  },
  "seed": 7, "defaultRows": 100,
  "scenarios": {
    "default": {},
    "quarter-end": {
      "extends": "default",
      "tables": {
        "dbo.Order": {
          "rows": 50000,
          "columns": {
            "Status":    { "weights": { "Open": 7, "Closed": 3 } },
            "CreatedOn": { "between": ["2026-01-01", "2026-03-31"], "skew": "late" } } },
        "dbo.OrderLine": { "perParent": { "dbo.Order": { "mean": 3.5 } } } },
      "pins": [
        { "table": "dbo.Customer",
          "rows": [ { "Id": 1000, "Name": "Canonical Test Customer", "Email": "canon@example.test" } ] } ]
    }
  }
}
```

Rules worth knowing:

- **The surface is closed.** An unknown key refuses, named by its JSON path.
  Secrets never live in the file — connections and passwords are `env:` or
  `file:` references.
- **A scenario only rewrites** volumes, distributions, and pins — weights
  reshape a text column's vocabulary (and pin it verbatim), `between`
  windows a date or numeric column (`skew`: `early` / `late` / `uniform`),
  `perParent` derives a child's volume from its parent's, and `pins` are
  exact rows seeded beside the synthetic mass, referenceable by it.
- **Evidence is tiered.** `twin evidence import` writes the full (rich) pack
  to an out-of-repo location; `twin evidence derive` projects the committed
  shape tier, which provably carries no captured value. The mint layers:
  scenario → rich → shape → type defaults.
- **Faker bindings** in the corrections artifact address columns as
  `Module/Entity/Attribute`; against a read-back estate the module segment
  is `Reconstructed`.

## First hour

```
twin init                      # scaffold twin.json; set estate.tables
twin up                        # container + schema + data, from nothing
twin classify                  # propose PII classifications; review + commit the artifact
twin evidence import && twin evidence derive   # optional: real distribution shapes
twin check                     # the proof on a throwaway database
```

## CI

The proof verb is CI-shaped: with a Docker daemon present,

```
twin check
```

builds the model, publishes to a throwaway database, applies the lanes,
mints twice, and verifies zero orphaned references and a byte-identical
re-mint. Exit codes: `0` done · `1` arguments · `4` Docker unreachable ·
`6` configuration · `9` refused.
