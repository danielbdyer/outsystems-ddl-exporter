# uat-users Verb

The `uat-users` verb discovers every foreign-key column that references `dbo.[User](Id)`, evaluates the live data set for out-of-scope user identifiers, and emits a deterministic remediation bundle. Operators receive a catalog, an orphan mapping template, a preview of pending row counts, and a guarded apply script that can be rerun safely in UAT.

## Key Behaviors

* **Deterministic discovery** – Metadata is sourced from the OutSystems model (or live metadata when `--from-live` is supplied) and flattened into a sorted, deduplicated catalog. Each catalog entry produces its own update block in the SQL script.
* **Attribute-level fallback** – When exported models omit explicit relationships to the Users table, provide `--user-entity-id` (accepts `btGUID*GUID`, numeric IDs, entity names, or physical table names). The command synthesizes catalog entries directly from attribute metadata so remediation can proceed without a fully coherent model.
* **Inventory-sourced allow-lists** – `--qa-user-inventory` and `--uat-user-inventory` ingest Service Center exports of the `ossys_User` table (schema: `Id,Username,EMail,Name,External_Id,Is_Active,Creation_Date,Last_Login`). The shared loader normalizes whitespace, deduplicates identifiers, enforces the schema declared in `config/supplemental/ossys-user.json`, and captures the entire roster for each environment so QA discoveries, the map template, and UAT targets share deterministic provenance.
* **Cross-environment map validation** – The `validate-user-map` stage cross-checks the populated map against the QA inventory, the discovered orphan set, and the allowed UAT user inventory from `--uat-user-inventory`. Missing mappings, duplicate `SourceUserId` values, or targets outside the approved UAT roster cause the command to fail before artifacts are emitted.
* **Live data analysis** – Using the supplied connection string, every catalogued column is scanned to collect distinct user identifiers and row counts. Results can be snapshotted to disk via `--snapshot` for repeatable dry runs.
* **Operator-controlled mappings** – `00_user_map.template.csv` lists every orphan. Populate the corresponding `00_user_map.csv` (or provide `--user-map`) with `SourceUserId,TargetUserId` pairs. Missing mappings are surfaced in both the preview file and the generated SQL comments.
* **Operator-tunable matching heuristics** – `--match-strategy` lets operators choose `case-insensitive-email`, `exact-attribute`, or `regex` heuristics. Pair it with `--match-attribute`, `--match-regex`, and `--match-fallback-*` options to pre-populate a subset of mappings and record the rationale for every automatic or manual decision.
* **Guarded apply script** – `02_apply_user_remap.sql` creates `#UserRemap` and `#Changes` temp tables, validates target existence, protects `NULL` values, and emits a summary. Updates only occur when `SourceUserId <> TargetUserId`, making the script idempotent and rerunnable.
* **SQL-friendly configuration parsing** – The CLI accepts `[schema].[table]` and quoted identifiers for `--user-table`, trimming duplicates from `--include-columns` so catalogs stay deterministic even when operators repeat column names.
* **Flexible identifier handling** – Allowed-user parsing and artifacts support numeric, `UNIQUEIDENTIFIER`, or free-form textual identifiers; the generated SQL automatically selects `INT`, `UNIQUEIDENTIFIER`, or `NVARCHAR` temporary table columns to match the observed inputs.

## CLI Contract

```
uat-users
  [--model <path>]                  # Required unless --from-live is supplied
  [--from-live]                     # Query metadata from the live UAT database
  --connection-string <connection string>    # Required; used for discovery and data analysis
  --uat-user-inventory <path>       # Required; CSV export of the UAT ossys_User table (Id,Username,EMail,Name,External_Id,Is_Active,Creation_Date,Last_Login)
  --qa-user-inventory <path>        # Required; CSV export of the QA ossys_User table (same schema)
  [--snapshot <path>]               # Optional JSON cache of FK analysis
  [--user-schema <schema>]          # Default: dbo
  [--user-table <table or schema.table>] # Default: User
  [--user-id-column <name>]         # Default: Id
  [--include-columns <list>]        # Optional allow-list of column names
  [--user-entity-id <identifier>]   # Optional override to synthesize FKs when the model lacks User relationships
  [--user-map <file>]               # Override path for SourceUserId,TargetUserId mappings
  [--match-strategy <strategy>]     # case-insensitive-email (default), exact-attribute, or regex
  [--match-attribute <attribute>]   # Username, Email, External_Id, etc. (used for exact/regex strategies)
  [--match-regex <pattern>]         # Regex pattern for regex strategy (captures 'target' or first capture group)
  [--match-fallback-mode <mode>]    # ignore (default), single, or round-robin
  [--match-fallback-target <id>]    # Approved fallback UserIds (repeat or comma-delimit values)
  [--out <dir>]                     # Default: ./_artifacts
```

## Required Inventories & CSV Schema

`uat-users` now requires two explicit inventories before it will emit artifacts:

1. **QA user inventory (`--qa-user-inventory`)** – Export every QA account from Service Center/`ossys_User` into a CSV containing the canonical columns (`Id, Username, EMail, Name, External_Id, Is_Active, Creation_Date, Last_Login`). The loader trims whitespace, normalizes timestamps to ISO-8601, and fails fast if the file is missing an `Id` column or if any identifiers are duplicated. This guarantees the `validate-user-map` stage can prove every `SourceUserId` originated in QA before it is mapped.
2. **UAT user inventory (`--uat-user-inventory`)** – Export the entire UAT `ossys_User` table into a CSV using the same schema. The loader populates the allowed-target set (`AllowedUserIds`) and enforces the same duplicate/missing safeguards so the validator can prove every `TargetUserId` is part of the approved UAT roster before SQL is emitted.

The QA and UAT CSVs share the same column expectations, so operators can reuse the Service Center export recipe for both environments. The deterministic schema is documented in `config/supplemental/ossys-user.json` for auditability.

## Matching Strategies & Fallbacks

The CLI now exposes a matching engine that can automatically propose a subset of mappings and record the rationale for every orphan:

* `case-insensitive-email` *(default)* compares QA and UAT `Email` values using ordinal-insensitive comparisons.
* `exact-attribute` compares the exact string captured by `--match-attribute` (`Username`, `External_Id`, `Name`, etc.).
* `regex` evaluates `--match-regex` against the attribute chosen by `--match-attribute`. Provide either a named `(?<target>...)` capture or rely on the first unnamed capture.

Fallback assignments activate when a match cannot be produced:

* `--match-fallback-mode ignore` *(default)* records the explanation but leaves the orphan unmapped.
* `--match-fallback-mode single` assigns every unmatched orphan to the first `--match-fallback-target` (after verifying it is in the approved UAT roster).
* `--match-fallback-mode round-robin` cycles through the provided fallback targets so manual mappers can triage evenly.

The same knobs can be expressed in `cli.json` via `matchStrategy`, `matchAttribute`, `matchRegex`, `fallbackMode`, and `fallbackTargets`.

Example:

```bash
dotnet run --project src/Osm.Cli -- uat-users \
  --model ./_artifacts/model.json \
  --uat-conn "Server=uat;Database=UAT;Trusted_Connection=True" \
  --uat-user-inventory ./extracts/uat_users.csv \
  --qa-user-inventory ./extracts/qa_users.csv \
  --match-strategy exact-attribute \
  --match-attribute External_Id \
  --match-fallback-mode round-robin \
  --match-fallback-target 400 --match-fallback-target 401
```

Every run produces `04_matching_report.csv` alongside the map template:

```
SourceUserId,TargetUserId,Strategy,Explanation,UsedFallback
999,200,CaseInsensitiveEmail,Matched email 'qa@example.com'.,False
111,,CaseInsensitiveEmail,QA inventory row does not include 'email'.,False
222,400,RoundRobin,Assigned round-robin fallback '400'.,True
```

Review the report to understand which mappings were auto-approved, which ones used fallback targets, and which orphans still need manual input. Invalid regex patterns will fail the pipeline up front, and fallback targets are validated against the parsed UAT roster to avoid accidentally assigning deleted accounts.

## Full Export Walkthrough (QA → UAT)

The `full-export` verb can emit the UAT remap bundle alongside SSDT artifacts when `--enable-uat-users` is supplied. This keeps the QA→UAT promotion in a single deterministic run:

```bash
dotnet run --project src/Osm.Cli \
  full-export \
  --mock-advanced-sql tests/Fixtures/extraction/advanced-sql.manifest.json \
  --profile-out ./out/profiles \
  --build-out ./out/full-export \
  --enable-uat-users \
  --uat-user-inventory ./extracts/uat_users.csv \
  --qa-user-inventory ./extracts/qa_users.csv \
  --user-map ./inputs/uat_user_map.csv
```

Key expectations:

* `full-export.manifest.json` now carries a `uat-users` stage (`Stages[].Name == "uat-users"`) with metadata such as `artifactRoot`, `allowedCount`, and `defaultUserMapPath` so automation can discover the bundle without scraping console output.
* The manifest’s `DynamicArtifacts` array lists the published files (`uat-users-preview`, `uat-users-script`, `uat-users-catalog`, `uat-users-map-template`, and both map variants) so CI/CD can archive the run.
* The metadata block exposes `uatUsers.*` keys (`enabled`, `artifactRoot`, `applyScriptPath`, `previewPath`, `catalogPath`, `uatUserInventoryPath`, `qaUserInventoryPath`, etc.) to record provenance for postmortems and guardrails.

Provide both inventories via `--qa-user-inventory` and `--uat-user-inventory`. If the primary map lives outside `<build-out>/uat-users`, include `--user-map` so the pipeline synchronizes the custom CSV into the canonical location.

## Primary Artifacts (written to `<out>/uat-users`)

| File | Description |
| --- | --- |
| `00_user_map.template.csv` | Generated orphan list with blank `TargetUserId` fields (and optional `Rationale`) ready for operator input. |
| `01_preview.csv` | Preview matrix of `TableName,ColumnName,OldUserId,NewUserId,RowCount` for every orphan with a mapped target. |
| `02_apply_user_remap.sql` | Idempotent SQL script containing the mapping, sanity checks, per-column updates, and a change summary. |
| `03_catalog.txt` | Ordered list of `<schema>.<table>.<column> -- <foreign key name>` entries comprising the catalog. |
| `04_matching_report.csv` | `SourceUserId,TargetUserId,Strategy,Explanation,UsedFallback` ledger explaining every automatic match, fallback assignment, or unresolved orphan. |

## Workflow

When operating through `full-export`, the steps below remain the same—the orchestrator simply runs them as part of the combined pipeline and records the artifact paths in the manifest:

1. **Discover and analyze**
   ```bash
   dotnet run --project src/Osm.Cli -- uat-users \
     --model ./_artifacts/model.json \
     --connection-string "Server=qa;Database=QA;Trusted_Connection=True;MultipleActiveResultSets=True" \
     --uat-user-inventory ./extracts/uat_users.csv \
     --qa-user-inventory ./extracts/qa_users.csv \
     --out ./_artifacts
   ```
   Inspect `03_catalog.txt` and the generated `00_user_map.template.csv`. Fill in the companion `00_user_map.csv` with desired `SourceUserId,TargetUserId` pairs.

2. **Review mappings and preview counts**
   Re-run the command after editing the map (or pointing `--user-map` to an updated CSV). Check `01_preview.csv` to confirm expected row counts per orphan/column combination. The validator will halt execution if any orphan lacks a mapping, if a `SourceUserId` is missing from the QA inventory, or if a `TargetUserId` is outside the parsed UAT allow-list so fixes can be applied before SQL is emitted.
   Consult `04_matching_report.csv` to see which rows were auto-resolved, which used fallback targets, and which still need manual entries along with the recorded explanation.

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
* The command fails fast when either inventory omits `Id` values or the UAT roster produces zero identifiers, preventing silent runs with empty allow-lists.
* The CLI requires `--qa-user-inventory` and rejects malformed QA exports (missing `Id`, duplicates, or empty files), guaranteeing the validator has a complete discovery set before proceeding.
* `validate-user-map` halts the run when mappings are missing, duplicated, or reference targets that are not part of the UAT allow-list, surfacing actionable errors in the console before artifacts are written.
