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
  seed collection for subsequent runs. 【F:src/Osm.Pipeline/Runtime/FullExportRunManifest.cs†L12-L205】

## Metadata and CLI Summary

`FullExportVerb` metadata now includes the following keys in addition to the existing
entries:

| Key | Description |
| --- | --- |
| `build.staticSeedRoot` | Absolute path to the seed directory calculated from the emitted seed scripts. |
| `build.staticSeedsInDynamicManifest` | Indicates whether seed artifacts are mirrored into the dynamic manifest list. |
| `build.dynamicInsertRoot` | Directory containing dynamic replay scripts (one per entity) generated from live data. |
| `build.sqlProjectPath` | Full path to the synthesized `.sqlproj` that references the emitted modules and seed scripts. |

The CLI `SSDT Emission Summary` explicitly labels the seed artifacts and prints the
manifest semantics block so operators can validate the directory split without
inspecting the manifest JSON directly. 【F:src/Osm.Pipeline/Runtime/Verbs/FullExportVerb.cs†L196-L212】【F:src/Osm.Cli/Commands/CommandConsole.cs†L394-L429】

Downstream tooling can rely on these fields to stage seed scripts independently of the
SSDT output while still applying the full export bundle on first-run deployments.

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

Dynamic insert scripts are generated beneath `build.dynamicInsertRoot`, defaulting to
`<build-out>/DynamicData/<Module>/<Entity>.dynamic.sql`. These files replay the full
entity dataset—including rows that also appear in the static seed catalog—so operators
can hydrate environments with a single set of scripts when desired. They are not
imported into SSDT; instead, schedule them as a deployment pipeline step:

1. After the dacpac publish, execute the dynamic scripts via `sqlcmd`, `SqlPackage` post
   scripts, or a runbook. A simple example:

   ```bash
   for script in "$(jq -r '.Stages[] | select(.Name=="build-ssdt").Artifacts.dynamicInsertScripts' \
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
* `Stages[].Artifacts.dynamicInsertRoot` → base directory for dynamic inserts.
* `Stages[].Artifacts.staticSeedOrdering` / `dynamicInsertOrdering` → whether a
  topological order was applied before writing the scripts.

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
3. Publish the SSDT project to a scratch database, then execute any dynamic insert
   scripts under `DynamicData/`. Use the load harness to replay them locally:

   ```bash
   dotnet run --project tools/FullExportLoadHarness \
     --safe out/full-export.edge-case/SafeScript.sql \
     --static-seed "$(jq -r '.Stages[] | select(.Name=="build-ssdt").Artifacts.staticSeedScripts' \
       out/full-export.edge-case/full-export.manifest.json)" \
     --dynamic-insert-root "$(jq -r '.Stages[] | select(.Name=="build-ssdt").Artifacts.dynamicInsertRoot' \
       out/full-export.edge-case/full-export.manifest.json)"
   ```

4. Compare the deployed schema against the fixture manifest (`tests/Fixtures/emission/edge-case/manifest.json`)
   to confirm the SSDT project and seed scripts align with the documented contract.

Running this drill ensures new contributors can verify the SSDT workflow without waiting
for a live database and gives operators a repeatable recipe for validating future
changes.
