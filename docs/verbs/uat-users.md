# uat-users Verb

The `uat-users` verb discovers every foreign-key column that references `dbo.[User](Id)`, evaluates the live data set for out-of-scope user identifiers, and emits a deterministic remediation bundle. Operators receive a catalog, an orphan mapping template, a preview of pending row counts, and a guarded apply script that can be rerun safely in UAT.

## Key Behaviors

* **Deterministic discovery** – Metadata is sourced from the OutSystems model (or live metadata when `--from-live` is supplied) and flattened into a sorted, deduplicated catalog. Each catalog entry produces its own update block in the SQL script.
* **Attribute-level fallback** – When exported models omit explicit relationships to the Users table, provide `--user-entity-id` (accepts `btGUID*GUID`, numeric IDs, entity names, or physical table names). The command synthesizes catalog entries directly from attribute metadata so remediation can proceed without a fully coherent model.
* **Allowed-user hydration** – Either a `dbo.User` seed script (`--user-ddl`) or a plain identifier list (`--user-ids`) is parsed to determine the set of in-scope users. Any FK values outside of that set are treated as orphans.
* **Live data analysis** – Using the supplied UAT connection, every catalogued column is scanned to collect distinct user identifiers and row counts. Results can be snapshotted to disk via `--snapshot` for repeatable dry runs.
* **Operator-controlled mappings** – `00_user_map.template.csv` lists every orphan. Populate the corresponding `00_user_map.csv` (or provide `--user-map`) with `SourceUserId,TargetUserId` pairs. Missing mappings are surfaced in both the preview file and the generated SQL comments.
* **Guarded apply script** – `02_apply_user_remap.sql` creates `#UserRemap` and `#Changes` temp tables, validates target existence, protects `NULL` values, and emits a summary. Updates only occur when `SourceUserId <> TargetUserId`, making the script idempotent and rerunnable.

## CLI Contract

```
uat-users
  [--model <path>]                  # Required unless --from-live is supplied
  [--from-live]                     # Query metadata from the live UAT database
  --uat-conn <connection string>    # Required; used for discovery and data analysis
  --user-ddl <path>                 # SQL or CSV export of dbo.User(Id, ...)
  [--user-ids <path>]               # Optional CSV/txt list of allowed user identifiers
  [--snapshot <path>]               # Optional JSON cache of FK analysis
  [--user-schema <schema>]          # Default: dbo
  [--user-table <table or schema.table>] # Default: User
  [--user-id-column <name>]         # Default: Id
  [--include-columns <list>]        # Optional allow-list of column names
  [--user-entity-id <identifier>]   # Optional override to synthesize FKs when the model lacks User relationships
  [--user-map <file>]               # Override path for SourceUserId,TargetUserId mappings
  [--out <dir>]                     # Default: ./_artifacts
```

## Primary Artifacts (written to `<out>/uat-users`)

| File | Description |
| --- | --- |
| `00_user_map.template.csv` | Generated orphan list with blank `TargetUserId` fields (and optional `Rationale`) ready for operator input. |
| `01_preview.csv` | Preview matrix of `TableName,ColumnName,OldUserId,NewUserId,RowCount` for every orphan with a mapped target. |
| `02_apply_user_remap.sql` | Idempotent SQL script containing the mapping, sanity checks, per-column updates, and a change summary. |
| `03_catalog.txt` | Ordered list of `<schema>.<table>.<column> -- <foreign key name>` entries comprising the catalog. |

## Workflow

1. **Discover and analyze**
   ```bash
   dotnet run --project src/Osm.Cli -- uat-users \
     --model ./_artifacts/model.json \
     --uat-conn "Server=uat;Database=UAT;Trusted_Connection=True;MultipleActiveResultSets=True" \
     --user-ddl ./extracts/dbo.User.sql \
     --out ./_artifacts
   ```
   Inspect `03_catalog.txt` and the generated `00_user_map.template.csv`. Fill in the companion `00_user_map.csv` with desired `SourceUserId,TargetUserId` pairs.

2. **Review mappings and preview counts**
   Re-run the command after editing the map (or pointing `--user-map` to an updated CSV). Check `01_preview.csv` to confirm expected row counts per orphan/column combination.

3. **Apply in UAT**
   Once satisfied, execute `02_apply_user_remap.sql` against UAT. The script materialises `#UserRemap`, validates target users, updates each catalogued column via guarded `;WITH delta` blocks, records every change in `#Changes`, and prints a per-column summary. Re-running the script produces zero updates thanks to the `<>` guard and `WHERE ... IS NOT NULL` predicate.

4. **Snapshot reuse (optional)**
   Provide `--snapshot ./cache/uat-users.snapshot.json` to persist the FK analysis. Subsequent runs will reuse the snapshot (when the fingerprint matches) without re-querying SQL Server.

## Acceptance Checklist

* `03_catalog.txt` lists every FK column referencing `dbo.[User](Id)` exactly once, honoring `--include-columns` filters.
* `00_user_map.template.csv` contains all orphan `SourceUserId` values with blank targets; rerunning after filling targets embeds the mapping inline.
* `01_preview.csv` reports accurate row counts per orphan/column and reflects the provided mappings.
* `02_apply_user_remap.sql` includes `WHERE t.[Column] IS NOT NULL` guards, a target sanity check, and no `IDENTITY_INSERT` statements.
* Re-running `02_apply_user_remap.sql` after a successful apply produces zero additional changes (idempotent behavior).
