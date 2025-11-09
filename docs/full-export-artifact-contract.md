# Full Export Artifact Contract

The full export pipeline now publishes two distinct artifact categories so downstream
loaders can differentiate between the bulk SSDT emission and the static seed set that
is replayed after the initial schema deployment.

## Directory Layout

* **Dynamic artifacts** are emitted beneath the SSDT output directory (the
  `build.outputDirectory` metadata field). This directory continues to contain the
  manifest, decision log, validation report, and opportunity scripts that describe the
  schema tightening results.
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

The CLI `SSDT Emission Summary` explicitly labels the seed artifacts and prints the
manifest semantics block so operators can validate the directory split without
inspecting the manifest JSON directly. 【F:src/Osm.Pipeline/Runtime/Verbs/FullExportVerb.cs†L196-L212】【F:src/Osm.Cli/Commands/CommandConsole.cs†L394-L429】

Downstream tooling can rely on these fields to stage seed scripts independently of the
SSDT output while still applying the full export bundle on first-run deployments.
