# ACCELERANT PLAN — wiring the F# Projection engine behind the tree

> **Status: PLAN (nothing wired yet).** This is the detailed "how" for `CONNECTORS.md` §3 (engine
> generates the proving ground) plus a new **evidence tier** (Profile / CatalogDiff / canary). It is
> the staged, verify-first path to making the engine an **optional accelerant** — a fast-path the
> tree uses when present, falling back to generic SSDT/SQL when absent.

## Governing principle (do not break)

**Codebase-indifferent.** Consume the engine's **CLI output + JSON artifacts only** — never import
the F# assembly (`CONNECTORS.md` §3). The tree keeps working with the hand-authored `SampleCatalog`
+ raw SQL probes when the engine is absent. The engine is a *fast-path behind one probe*, never a
dependency.

## What the engine gives us — three artifact-only surfaces

### A. Real proving-ground schema (the core accelerant — CONNECTORS §3)
- **Generate:** `projection <flow>` with an environment `access: bundle` + `emission.sqlproj: true`,
  `emission.staticSeeds: true` in `projection.json`.
- **Emits:** a buildable SDK-style `.sqlproj` (`Microsoft.Build.Sql/2.2.0`), per-table
  `Modules/*.sql`, `Data/*.sql` seeds, `Script.PostDeployment.sql` (with `:r` includes), a `.dacpac`,
  a conditional `.refactorlog`, and `manifest.json`. **Structurally identical to the hand-authored
  `SampleCatalog`** — so `skills/prove-on-dacpac`'s loop (build → Script → Strict → Permissive) runs
  **unchanged**, against real schema.
- **Catalog source is flexible:** live OSSYS (`model.env`/`model.ossys`), a `model.path`
  `osm_model.json` file, or a rowset snapshot — **no live connection required** for a test model.
- **Emitters:** `SqlprojEmitter.emit`, `PostDeployEmitter.renderIncludes`, `DacpacEmitter.emit`
  (`src/Projection.Targets.SSDT/`); pipeline entry `Compose.runFromCatalogWith` (`Pipeline.fs`).

### B. The Data Oracle — predict the veto (`projection profile`)
- **Capture:** `projection profile <conn> --out profile.json`.
- **Exposes per column/FK:** `ColumnProfile.NullCount / RowCount / MaxObservedLength`;
  `AttributeReality.HasNulls / HasDuplicates / HasOrphans / IsNullableInDatabase`;
  `ForeignKeyReality.HasOrphan / OrphanCount / IsNoCheck`; distributions; and **`CdcAwareness`
  (CdcEnabled + CdcInstance)**.
- **Turns "prove the veto" into "predict then prove":** `NullCount>0` → tightening veto;
  `MaxObservedLength>declared` → narrow veto; `HasOrphan` → FK veto; `HasDuplicates` → unique veto;
  `CdcAwareness` makes the **+1 CDC tripwire a fact, not a guess**. The tree's `talk-to-local-sql`
  probes (COUNT NULL, MAX(LEN), orphan LEFT JOIN, dup GROUP BY) become *reads of `profile.json`*.
- **Source:** `Profile.fs`, `LiveProfiler.captureEvidenceCache` (Adapters.Sql), CLI
  `Faces/Synthetic.fs runCaptureProfile`.

### C. Blast-radius + corroborating proofs (the reviewer's tools)
- **Blast-radius:** `projection diff a b --format json` (or `explain diff`) → `Renamed/Added/Removed`
  + per-channel `Reshaped` **facets** (DataType/Nullability/Length/... , OnDelete, Uniqueness, ...) +
  **`synthesizedRenameWarnings`** (rename-as-drop+add detection) + `isEmpty` (idempotency). Feeds
  `skills/review/blast-radius`. `CatalogDiff.fs`, `Faces/Diff.fs`.
- **Independent proofs a reviewer can marshal alongside the sqlpackage verdict:**
  - `projection check [--cdc-silence]` — round-trip structural equivalence + CDC-silence (exit 5 on
    divergence).
  - `projection check data --before <a> --after <b>` — row-count + null-count deltas (exit 8 on drift).
  - `projection compare a b` → `compare.json` — schema delta + data dealbreaker advisory.

## The mapping — where each plugs in

| Tree piece (generic today) | Engine accelerant | What it buys |
|---|---|---|
| `proving-ground/SampleCatalog` (hand-authored) | `projection <flow>` bundle | prove against **real** schema |
| `talk-to-local-sql` data probes | `projection profile → profile.json` | exact real-data evidence, one artifact, + CDC awareness |
| `classify-mechanism` (must-prove) | `profile.json` predicts the veto class *before* publishing | fewer blind publishes; confirm not discover |
| `skills/review/blast-radius` | `projection diff --format json` | deterministic facet-granular blast map + naked-rename warning |
| `skills/review/adversary` + `prove-on-dacpac` | `projection check` / `check data` / `compare` | independent corroboration of the sqlpackage verdict |

## The stable seam: one `accelerator-probe`

A thin skill (or a section in `talk-to-local-sql`) that **detects** the `projection` CLI + a
`projection.json`. Present → route the Data-Oracle to `projection profile`, blast-radius to
`projection diff`, the proving ground to `projection <flow>` output. Absent → generic SQL + the
hand-authored sample. The rest of the tree never knows which ran.

## Staged, verify-first

- **Stage 0 — prove the seam (empirical, ~1 session).** Generate a bundle from a test
  `osm_model.json`; confirm `dotnet build → dacpac`; confirm the *existing* `prove-on-dacpac` loop
  (Strict/Permissive) runs on it unchanged. Check the two `CONNECTORS.md` §3 gates: the emitted
  `.sqlproj` reclassifies pre/post-deploy out of the `**/*.sql` glob identically to the sample, and
  the DSP is `Sql160` both sides. (Both already appear correct from research.)
- **Stage 1 — schema accelerant.** A `use-engine-bundle` skill + the config recipe; point
  `prove-on-dacpac` at the engine's `out/` when present. Keep `SampleCatalog` as the fallback + the
  self-test fixture (do NOT delete it — it is the deterministic test bed).
- **Stage 2 — evidence accelerant.** The `accelerator-probe`; wire the Data-Oracle to
  `projection profile` (parse `profile.json` → predicted veto classes) and `skills/review/blast-radius`
  to `projection diff`.
- **Stage 3 — corroborating proofs.** The reviewer marshals `projection check` / `check data` /
  `compare` as *independent* evidence beside the sqlpackage verdict.

## Guardrails

- **Do NOT** import the F# assembly from skills — consume CLI + JSON only (`CONNECTORS.md` §3).
- **Do NOT** fold sqlpackage-driving into the engine yet (`CONNECTORS.md` §4) — the two-profile
  discipline (Strict veto-detector + Permissive consequence-oracle) + the content-hash proof stay
  skill-owned.
- **Do NOT** require the engine — it is optional; the generic path (hand-authored sample + SQL
  probes) must keep passing the self-test.
- The optional `projection explain oracle` verb (voicing the three predicted vetoes) is a **code
  change — defer it**; `profile --out json` is sufficient. Only add if parsing `profile.json` proves
  unwieldy.

## Open questions / risks

- **Which DB does the Data-Oracle profile?** The **real source/target** (to *predict*), not the
  throwaway (where you *prove*). Make this explicit in the `accelerator-probe` skill.
- **Bundle generation needs a catalog source** — document both `osm_model.json` (test) and live OSSYS
  (real).
- **CLI runtime** — the engine CLI must be runnable in-env (pinned .NET 9 SDK). Document
  `dotnet run --project src/Projection.Cli -- <verb>` vs a published exe.

## Connector cross-refs
- `CONNECTORS.md` §3 (engine generates the proving ground), §4 (sqlpackage driving), §6 (warm-sql
  substrate). Research file refs: `Profile.fs`, `CatalogDiff.fs`, `SqlprojEmitter.fs`,
  `DacpacEmitter.fs`, `PostDeployEmitter.fs`, `LiveProfiler.fs`, `DataIntegrityChecker.fs`,
  `Cli/Program.fs`, `Cli/Faces/{Emit,Diff,Synthetic,Operational,Canary}.fs`.
