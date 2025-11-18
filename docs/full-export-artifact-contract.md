# Full Export Artifact Contract

The full export pipeline now publishes two distinct artifact categories so downstream
loaders can differentiate between the bulk SSDT emission and the static seed set that
is replayed after the initial schema deployment.

## Directory Layout

* **Dynamic artifacts** are emitted beneath the SSDT output directory (the
  `build.outputDirectory` metadata field). This directory continues to contain the
  manifest, decision log, validation report, and opportunity scripts that describe the
  schema tightening results. It also houses per-entity INSERT scripts that now include
  the full dataset for each entity, not just the rows absent from the static seed
  catalog.
* A synthesized SQL Server Database project (`OutSystemsModel.sqlproj`) is written to
  the same root. It includes the emitted `Modules/` hierarchy and references the
  `Seeds/` directory as post-deployment content so SSDT imports pick up both the DDL and
  seed scripts.
* **Static seed artifacts** are emitted beneath a dedicated seed root. The pipeline
  calculates this common directory and surfaces it through the
  `build.staticSeedRoot` metadata value so that orchestration tools can stage the seed
  scripts separately from the SSDT payload.

## Run Manifest Shape

`FullExportRunManifest` records the artifact split explicitly:

* `DynamicArtifacts` tracks every emitted artifact that participates in the dynamic
  export footprint.
* `StaticSeedArtifacts` contains only the static seed scripts and master seed
  aggregates.
* `StaticSeedArtifactsIncludedInDynamic` is set when the static seed artifacts are
  mirrored into the dynamic list. The current default is `true`, allowing the initial
  run bundle to include the seeds while still giving orchestrators a clearly scoped
  seed collection for subsequent runs.

The `Stages` collection mirrors those categories so automation can toggle or audit the
payloads independently:

* `build-ssdt` — aggregate SSDT emission status plus manifest, decision log, safe /
  remediation scripts, and telemetry references.
* `static-seed` — exposes seed emission metadata with `root`, `ordering`,
  `scriptCount`, and `scripts` entries that capture the resolved directory, ordering
  mode, total script count, and absolute paths.
* `dynamic-insert` — exposes the live data replay bundle with `root`, `mode`,
  `ordering`, `scriptCount`, and `scripts` entries so deployments can run or skip
  those payloads without touching the SSDT stage. 【F:src/Osm.Pipeline/Runtime/FullExportRunManifest.cs†L12-L420】

## Metadata and CLI Summary

`FullExportVerb` metadata now includes the following keys in addition to the existing
entries:

| Key | Description |
| --- | --- |
| `build.staticSeedRoot` | Absolute path to the seed directory calculated from the emitted seed scripts. |
| `build.staticSeedsInDynamicManifest` | Indicates whether seed artifacts are mirrored into the dynamic manifest list. |
| `build.dynamicInsertRoot` | Directory containing dynamic replay scripts (one per entity) generated from live data. |
| `build.dynamicInsertMode` | Emission mode used for dynamic inserts (`PerEntity` or `SingleFile`). |
| `build.sqlProjectPath` | Full path to the synthesized `.sqlproj` that references the emitted modules and seed scripts. |

### UAT-Users Integration (Pre-Transformed Data)

When `full-export` runs with `--enable-uat-users`, additional metadata tracks the transformation:

| Key | Description |
| --- | --- |
| `uatUsers.enabled` | Boolean indicating UAT-users pipeline ran. |
| `uatUsers.transformationMode` | Either `pre-transformed-inserts` (full-export integration) or `post-load-updates` (standalone). |
| `uatUsers.orphanCount` | Number of out-of-scope QA user IDs discovered. |
| `uatUsers.mappedCount` | Number of orphans successfully mapped to UAT targets. |
| `uatUsers.fkCatalogSize` | Count of FK columns referencing User table. |
| `uatUsers.qaInventoryPath` | Path to QA user inventory CSV. |
| `uatUsers.uatInventoryPath` | Path to UAT user inventory CSV. |
| `uatUsers.userMapPath` | Path to the user mapping CSV used for transformation. |
| `uatUsers.validationReportPath` | Path to the validation report proving mapping correctness. |

The `dynamic-insert` stage includes `transformationApplied: true` when UAT-users pre-transforms the INSERT scripts. In this mode:

* **Dynamic INSERT scripts contain UAT-ready data**: User FK values are already mapped to UAT targets during generation
* **No post-load transformation needed**: Load scripts directly to UAT database
* **Verification proves in-scope guarantee**: All user FK values in emitted scripts exist in UAT inventory
* **NULL preservation enforced**: NULL user IDs remain NULL (never transformed)

**Contrast with standalone uat-users mode**: When `uat-users` runs independently (not via `full-export`), it emits UPDATE scripts that transform data after loading. The full-export integration eliminates this post-load step by pre-transforming during generation.

> **See**: `docs/design-uat-users-transformation.md` for detailed architecture, verification requirements, and migration path guidance.

The CLI `SSDT Emission Summary` explicitly labels the seed artifacts and prints the
manifest semantics block so operators can validate the directory split without
inspecting the manifest JSON directly. 【F:src/Osm.Pipeline/Runtime/Verbs/FullExportVerb.cs†L196-L212】【F:src/Osm.Cli/Commands/CommandConsole.cs†L394-L429】

Downstream tooling can rely on these fields to stage seed scripts independently of the
SSDT output while still applying the full export bundle on first-run deployments.

---

## Data Integrity Verification (DMM Replacement)

The full-export pipeline supports **end-to-end data integrity verification** to provide unfailing confidence that the ETL process is correct. This capability enables replacing expensive third-party tools (like DMM) with a verifiable, auditable pipeline.

### Verification Artifacts

When verification is enabled, additional artifacts are generated:

| Artifact | Description |
| --- | --- |
| `source-data-fingerprint.json` | Per-table row counts, per-column checksums, NULL counts, and distinct value counts captured from source database (QA) |
| `data-integrity-verification.json` | Comprehensive verification report comparing source fingerprint against target data after loading INSERT scripts |
| `load-harness-full-verification.json` | Extended load harness report including source-to-target parity verification, transformation validation, and performance metrics |

### Verification Report Schema

### Modular Verification Architecture

Verification is **composable and layered**:

**Base Layer: Full-Export Verification** (always active)
- Row count verification (no data loss)
- Column checksum verification (1:1 data fidelity)
- NULL preservation verification
- Schema validation (DDL matches model)
- **Works standalone** - you can verify basic full-export without UAT-users

**Specialized Layer: UAT-Users Verification** (when `--enable-uat-users`)
- User FK transformation verification
- UAT inventory compliance
- Transformation map validation
- **Builds on base layer** - non-user-FK columns verified via base layer

**Key principle**: UAT-users doesn't replace base verification; it **adds specialized checks on top**.

The `data-integrity-verification.json` report proves:
1. **No data loss**: Row counts match source exactly (base layer)
2. **1:1 data fidelity**: Non-transformed columns have identical checksums (base layer)
3. **Correct transformations**: User FK values map correctly per transformation map (uat-users layer)
4. **NULL preservation**: NULL counts match per column (both layers)

**Report structure (modular)**:
```json
{
  "overallStatus": "PASS",
  "verificationTimestamp": "2025-11-17T10:30:00Z",
  "sourceFingerprint": "source-data-fingerprint.json",

  // Base Layer: Always Present
  "baseVerification": {
    "rowCountVerification": "PASS",
    "checksumVerification": "PASS",
    "nullPreservation": "PASS",
    "schemaValidation": "PASS"
  },

  // Specialized Layer: Present when --enable-uat-users
  "uatUsersVerification": {
    "enabled": true,
    "transformationMapValid": true,
    "orphanMappingComplete": true,
    "uatInventoryCompliance": "PASS",
    "transformedColumnCount": 23
  },

  "tables": [...],
  "discrepancies": [],
  "summary": {
    "tablesVerified": 50,
    "tablesPassed": 50,
    "tablesFailed": 0,
    "columnsVerified": 500,
    "columnsPassed": 500,
    "columnsFailed": 0,
    "baseLayerColumns": 477,        // Verified via base layer
    "transformedColumns": 23,       // Verified via uat-users layer
    "dataLossDetected": false
  }
}
```

### Command-Line Verification Workflow

**Step 1: Generate Full Export**
```bash
# With UAT-users transformation
dotnet run --project src/Osm.Cli -- full-export \
  --enable-uat-users \
  --build-out ./out/uat-export \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --user-map ./uat_user_map.csv

# OR without UAT-users (base verification only)
dotnet run --project src/Osm.Cli -- full-export \
  --build-out ./out/export
```

**Step 2: Verify Data Integrity**
```bash
dotnet run --project tools/FullExportLoadHarness \
  --source-connection "Server=qa;Database=QA;Trusted_Connection=True" \
  --target-connection "Server=uat-staging;Database=UAT;Trusted_Connection=True" \
  --manifest ./out/uat-export/full-export.manifest.json \
  --uat-user-inventory ./uat_users.csv \
  --verification-report-out ./verification-report.json
```

**Step 3: Review Results**
```bash
# Check overall status
jq -r '.overallStatus' ./verification-report.json
# Output: PASS or FAIL

# Review base verification (always present)
jq '.baseVerification' ./verification-report.json

# Review UAT-users verification (if --enable-uat-users was used)
jq '.uatUsersVerification' ./verification-report.json

# If FAIL, review discrepancies
jq '.discrepancies' ./verification-report.json
```

**Step 4: Deploy (if verification passed)**
```bash
# Deploy to production UAT
sqlcmd -S uat -d UAT -i ./out/uat-export/SafeScript.sql

# Load pre-transformed dynamic data
for file in ./out/uat-export/DynamicData/**/*.dynamic.sql; do
  sqlcmd -S uat -d UAT -i "$file"
done
```

### Benefits Over DMM

| Aspect | DMM | Full-Export with Verification |
|--------|-----|-------------------------------|
| **Data integrity proof** | Manual validation | Automated verification report |
| **Transformation validation** | Hope and pray | Provable correctness (checksum + map validation) |
| **Cost** | Expensive subscription | Open-source tooling |
| **Developer experience** | Nightmare (manual processes) | Deterministic, repeatable, verifiable |
| **NULL preservation** | Not guaranteed | Proven with NULL count comparison |
| **Audit trail** | Limited visibility | Complete fingerprint + verification report |

> **See**: `docs/design-uat-users-transformation.md` for detailed verification strategy, load harness integration, and incremental verification approaches for large datasets.

---

## SSDT Integration Playbook

### 1. Land static seed MERGE scripts in Post-Deployment

1. Open the emitted `OutSystemsModel.sqlproj` in SSDT (or copy its contents into an
   existing project). The project already references the generated `Modules/` tree and
   treats `Seeds/` as post-deployment content.
2. In your SSDT project, create a `Seeds/` folder under **Post-Deployment** and copy the
   generated seed scripts from `build.staticSeedRoot`. The directory contains
   module-level `*.seed.sql` files plus any master aggregates emitted by the pipeline.
3. Edit `Script.PostDeployment.sql` and reference each seed script with SQLCMD includes:

   ```sql
   :r .\Seeds\AppCore\StaticEntities.seed.sql
   :r .\Seeds\MasterSeed.aggregate.sql
   ```

4. Because the scripts are written with `MERGE` and idempotent guards, they may be
   executed during every publish. SSDT treats the includes as part of the post-deploy
   batch, guaranteeing the static catalog stays synchronized with the seed manifest.

The manifest records the full path so pipeline automation can mirror the files directly
into the SSDT repository:

```bash
jq -r '.Metadata["build.staticSeedRoot"]' out/full-export/full-export.manifest.json
```

### 2. Stage dynamic inserts for deployment pipelines

Dynamic insert scripts are generated beneath `build.dynamicInsertRoot`. The exporter
supports two emission modes:

* **PerEntity (default)** – one file per entity under
  `<build-out>/DynamicData/<Module>/<Entity>.dynamic.sql`. Use this when you want
  granular visibility or need to parallelize execution.
* **SingleFile** – a consolidated `DynamicData.all.dynamic.sql` in the `DynamicData/`
  root. The file concatenates the per-entity batches in topological order (preserving
  the original `PRINT`/`GO` batching) so operators can run a single replay script.
  Enable this via `--dynamic-insert-mode single-file` (CLI) or `dynamicData.insertMode`
  in configuration.

Regardless of the mode, the scripts replay the full entity dataset—including rows that
also appear in the static seed catalog—so lower environments can be hydrated quickly.
They are not imported into SSDT; instead, schedule them as a deployment pipeline step:

1. After the dacpac publish, execute the dynamic scripts via `sqlcmd`, `SqlPackage` post
   scripts, or a runbook. A simple example:

   ```bash
   for script in "$(jq -r '.Stages[] | select(.Name=="dynamic-insert").Artifacts.scripts' \
     out/full-export/full-export.manifest.json | tr ';' '\n')"; do
     sqlcmd -S "$SQLSERVER" -d "$DB" -i "$script"
   done
   ```

2. When using Azure DevOps or GitHub Actions, treat the dynamic folder as a published
   artifact so operations can rerun the inserts after refreshing lower environments.
3. The load harness (`tools/FullExportLoadHarness`) accepts the same list of scripts to
   validate timing and locking before the production rollout.

### 3. Consume manifest metadata from automation

The `full-export.manifest.json` file provides stable keys for orchestration:

* `Metadata["build.staticSeedRoot"]` → location for static seeds imported via
  `Script.PostDeployment.sql`.
* `Stages[] | select(.Name=="static-seed").Artifacts.root` → base directory for
  static seeds plus `ordering` / `scriptCount` / `scripts` details for selective
  deployment.
* `Stages[] | select(.Name=="dynamic-insert").Artifacts.root` → base directory for
  dynamic inserts plus the `mode` / `ordering` / `scriptCount` / `scripts` metadata
  that powers downstream scheduling.

Keep both directories in the deployment artifact so subsequent runs can diff contents
against previous releases or rerun seeds in disaster recovery scenarios.

## Fixture Validation Walkthrough

Use the repository’s edge-case fixtures to validate an SSDT project end-to-end:

1. Generate a fresh bundle (or reuse the curated output):

   ```bash
   dotnet run --project src/Osm.Cli \
     full-export \
     --mock-advanced-sql tests/Fixtures/extraction/advanced-sql.manifest.json \
     --profile tests/Fixtures/profiling/profile.edge-case.json \
     --build-out ./out/full-export.edge-case
   ```

   The emitted manifest will expose the static and dynamic roots discussed above.

2. Import `out/full-export.edge-case/Modules/...` into SSDT (or copy
   `tests/Fixtures/emission/edge-case/Modules/...` if you prefer the checked-in
   baseline). Add the `Seeds/` hierarchy from the run output to `Post-Deployment` and
   include it via `Script.PostDeployment.sql`.
3. Publish the SSDT project to a scratch database, then execute either the per-entity
   scripts or the consolidated `DynamicData.all.dynamic.sql` (depending on the mode).
   Each batch uses `WITH (TABLOCK)` and is written in foreign-key
   order, so you can run the scripts with constraints already enforced. If your
   deployment policy requires staging without constraints, publish the base tables,
   apply the dynamic data, and then re-run the publish with constraint emission
   enabled. Use the load harness to rehearse timings locally:

   ```bash
   dotnet run --project tools/FullExportLoadHarness \
     --safe out/full-export.edge-case/SafeScript.sql \
     --static-seed "$(jq -r '.Stages[] | select(.Name=="static-seed").Artifacts.scripts' \
       out/full-export.edge-case/full-export.manifest.json)" \
     --dynamic-insert-root "$(jq -r '.Stages[] | select(.Name=="dynamic-insert").Artifacts.root' \
       out/full-export.edge-case/full-export.manifest.json)"
   ```

Regardless of mode, the recommended flow is: publish the DACPAC (which runs static
seeds via post-deployment), execute the dynamic data replay, and only fall back to a
constraint-free publish if corporate policy requires it.

4. Compare the deployed schema against the fixture manifest (`tests/Fixtures/emission/edge-case/manifest.json`)
   to confirm the SSDT project and seed scripts align with the documented contract.

Running this drill ensures new contributors can verify the SSDT workflow without waiting
for a live database and gives operators a repeatable recipe for validating future
changes.
