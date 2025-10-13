# remap-users Verb

The `remap-users` verb stages DEV or QA snapshots into a UAT database, deterministically rewrites every foreign key that ultimately points to `ossys_User.Id`, and then (optionally) commits those changes back into the base schemas with constraints revalidated. The verb is gated by a dry-run manifest so that operators can review the planned rewrites and trust posture before any destructive load occurs.

## Key Guarantees

* **Determinism** – Each source `SourceEnv + SourceUserId` pair always maps to the same UAT identity unless the map is explicitly rebuilt.
* **Idempotence** – Once the pipeline converges, re-running with the same parameters produces zero data deltas.
* **Scoped blast radius** – All rewrites happen in `stg.*` tables; only catalogued tables have constraints toggled and re-CHECKed during load.
* **Trust restored** – The pipeline fails if any foreign key remains disabled or untrusted after load.
* **PII hygiene** – Artifacts redact user identifiers unless `--include-pii` is passed.
* **Proof first** – `--dry-run false` is rejected unless a matching dry-run manifest and dry-run hash (same parameters + inputs) from the last 24 hours exist.

## Required Schemas & Control Tables

The pipeline manages the following objects automatically:

| Schema | Object | Purpose |
| --- | --- | --- |
| `ctl` | `UserMap` | Persisted mapping of source user ids to UAT ids with match reason and timestamps. Primary key `(SourceEnv, SourceUserId)` ensures determinism across runs. |
| `ctl` | `UserFkCatalog` | Snapshot of every table/column that ultimately depends on `ossys_User.Id`. Used to drive rewrite/load ordering. |
| `ctl` | `UserKeyChanges` | Audit trail produced by `UPDATE ... OUTPUT` when staging tables are rewritten. |
| `stg` | `*` | Shadow copies of the base tables that participate in user remapping. Data is bulk-loaded here before any rewrite occurs. |

## Primary Artifacts

Every run emits artifacts into `--out` (default `./_artifacts/remap-users`):

| File | Description |
| --- | --- |
| `user-map.coverage.(json|csv)` | Match counts per rule plus a redacted sample of unresolved identifiers. |
| `user-fk-catalog.(json|txt)` | Catalog of every table/column touched (with alias path hints). |
| `fk-rewrites.delta.csv` | Remap, reassign, prune, and unmapped counts per table/column (policy included). |
| `unmapped.impact.csv` | Rows lacking a mapping prior to policy application. |
| `dry-run.summary.json` | Aggregate totals and the policy used. |
| `dry-run.summary.txt` | Human-readable guardrail summary (source env, coverage, totals). |
| `dry-run.hash` | Hash of parameters + snapshot fingerprints required for commit. |
| `postload.validation.json` | FK trust snapshot and validation errors. |
| `referential-probes.csv` | Post-load probe results demonstrating valid user references. |
| `load.order.txt` | Topological load order applied under the constraint window. |
| `session.log` | Parameters plus per-step timings and telemetry (no secrets/PII). |
| `run.manifest.json` | Dry-run manifest required to authorize a subsequent commit. |

## CLI Contract

```
remap-users
  --source-env <DEV|QA>
  --uat-conn <connection string>
  --snapshot-path <directory>
  --matching-rules <rule[,rule...]>
  [--fallback-user-id <id>]          # required if any rule is "fallback"
  [--policy <reassign|prune>]        # default: reassign
  [--dry-run <true|false>]           # default: true
  [--out <dir>]                      # default: ./_artifacts/remap-users
  [--batch-size <int>]               # default: 5000
  [--command-timeout-s <int>]        # default: 600
  [--parallelism <int>]              # default: 4
  [--log-level <info|debug|trace>]   # telemetry verbosity
  [--include-pii]                    # opt-in to expose identifiers in artifacts
  [--rebuild-map]                    # rebuild persisted ctl.UserMap for SourceEnv
  [--user-table <schema.table>]      # default: ossys_User
```

### Matching Rule Cascade

1. `email` – exact email match.
2. `normalize-email` – trimmed/lower-cased email match.
3. `username` – exact username.
4. `empno` – exact employee number.
5. `fallback` – explicit reassignment to `--fallback-user-id` (requires opt-in).

Rules execute in order, inserting only for users that are still unmapped. Ambiguous matches (e.g., multiple UAT rows sharing the same normalized email) are not auto-resolved; they are logged as unresolved and surface in the artifacts.

### Policy Handling

* `reassign` – Unmapped foreign keys are updated to `--fallback-user-id` after remapping.
* `prune` – Unmapped rows are deleted from staging (and therefore never loaded).

## Workflow

1. **Dry-run (required first):**

   ```bash
   dotnet run --project src/Osm.Cli -- remap-users \
     --source-env DEV \
     --uat-conn "Server=uat-sql;Database=UAT;Trusted_Connection=True" \
     --snapshot-path /snapshots/dev-2024-04-01 \
     --matching-rules email,normalize-email,username,empno,fallback \
     --fallback-user-id 12345 \
     --policy reassign \
     --out ./_artifacts/remap-users/dev \
     --dry-run true
   ```

   Review `fk-rewrites.delta.csv`, `unmapped.impact.csv`, `postload.validation.json`, and `session.log` to confirm the plan.

2. **Commit (within 24 hours, same parameters):**

   ```bash
   dotnet run --project src/Osm.Cli -- remap-users \
     --source-env DEV \
     --uat-conn "Server=uat-sql;Database=UAT;Trusted_Connection=True" \
     --snapshot-path /snapshots/dev-2024-04-01 \
     --matching-rules email,normalize-email,username,empno,fallback \
     --fallback-user-id 12345 \
     --policy reassign \
     --out ./_artifacts/remap-users/dev \
     --dry-run false
   ```

   The command checks the prior `run.manifest.json`, loads staging into base tables under a constraint window, and revalidates FK trust. A second commit run with the same inputs is idempotent and produces zero changes.

## Failure Modes & Mitigations

* **Missing dry-run manifest** – ensure a matching dry-run completed within the last 24 hours; regenerate if necessary.
* **Missing dry-run hash** – rerun the dry-run when inputs change so the hash matches the current parameters before committing.
* **Fallback user validation** – the pipeline fails fast if `--fallback-user-id` is missing or does not exist in UAT.
* **Residual untrusted FKs** – post-load validation blocks the run and the session log lists the offending constraints.
* **Ambiguous user matches** – recorded as unresolved; resolve manually or adjust matching rules before committing.
* **Implicit policy with unresolved users** – commit runs require an explicit `--policy` whenever the dry-run reports unresolved users.
* **Constraint pressure** – only tables in the FK catalog have constraints toggled; batching knobs (`--batch-size`, `--parallelism`) help throttle large updates.

Keep the control tables (`ctl.*`) in place across runs so the mapping remains deterministic. Pass `--rebuild-map` only when you explicitly need to recalculate the entire user map for a source environment.
