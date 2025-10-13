# uat-users Verb

The `uat-users` verb operates purely on exported artifacts and metadata to build a deterministic catalog of every foreign key column that references `dbo.User(Id)`. It emits a self-contained SQL script that remaps user references according to operator-supplied mappings, keeping discovery and planning offline until the script is executed in UAT.

## Key Behaviors

* **Offline discovery** – Defaults to scanning the model JSON produced by the exporter. Supply `--from-live` with `--uat-conn` to pull metadata directly from `sys.foreign_keys` without writing to the database.
* **Deterministic catalog** – Columns referencing `dbo.User(Id)` are deduplicated and ordered by schema, table, and column. The catalog drives every downstream artifact.
* **Operator-controlled mappings** – Runs generate a `00_user_map.template.csv` file. Populate `00_user_map.csv` (or use `--user-map`) with `SourceUserId,TargetUserId` pairs, then rerun to embed those mappings into the SQL script.
* **Single apply script** – The emitted `02_apply_user_remap.sql` script creates `#UserRemap` and `#Changes` temp tables, applies updates per catalog entry, and captures an audit trail using `SYSUTCDATETIME()`.

## CLI Contract

```
uat-users
  [--model <path>]                  # Required unless --from-live is supplied
  [--from-live]                     # Query metadata from the live UAT database
  [--uat-conn <connection string>]  # Required when --from-live is set
  [--user-table <schema.table>]     # Default: dbo.User
  [--user-id-column <name>]         # Default: Id
  [--include-columns <list>]        # Optional allow-list of column names
  [--user-map <file>]               # CSV containing SourceUserId,TargetUserId mappings
  [--out <dir>]                     # Default: ./_artifacts
```

## Primary Artifacts (written to `<out>/uat-users`)

| File | Description |
| --- | --- |
| `00_user_map.template.csv` | CSV header ready for operator-supplied mappings. |
| `01_preview.csv` | Matrix of catalog columns against supplied mappings (row counts left blank for offline mode). |
| `02_apply_user_remap.sql` | Self-contained SQL script that updates each catalogued column. |
| `03_catalog.txt` | Ordered list of `<schema>.<table>.<column> -- <foreign key name>` pairs discovered by the pipeline. |

## Workflow

1. **Initial discovery**
   ```bash
   dotnet run --project src/Osm.Cli -- uat-users \
     --model ./_artifacts/model.json \
     --out ./_artifacts
   ```
   Review `03_catalog.txt` and fill `./_artifacts/uat-users/00_user_map.csv` with the desired `SourceUserId,TargetUserId` mappings.

2. **Generate final script**
   ```bash
   dotnet run --project src/Osm.Cli -- uat-users \
     --model ./_artifacts/model.json \
     --user-map ./_artifacts/uat-users/00_user_map.csv \
     --out ./_artifacts
   ```
   Execute the resulting `02_apply_user_remap.sql` in UAT to reconcile all foreign keys to in-scope users.

3. **Live metadata mode** (optional)
   ```bash
   dotnet run --project src/Osm.Cli -- uat-users \
     --from-live \
     --uat-conn "Server=uat-sql;Database=UAT;Trusted_Connection=True" \
     --out ./_artifacts
   ```
   Generates the same artifacts but discovers the catalog by querying `sys.foreign_keys` and related catalog views.

## Acceptance Checklist

* `03_catalog.txt` lists all parent columns referencing `dbo.User(Id)` exactly once.
* Providing `--include-columns CreatedBy,UpdatedBy` filters the catalog to those column names.
* The number of update blocks in `02_apply_user_remap.sql` equals the catalog length.
* The script never issues writes during discovery; only the emitted SQL updates UAT.
