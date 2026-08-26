# Capture-point runbook — from three real databases to the first distributed template

This runbook is for the one machine and one person with read access to the real environments:
the capture point. Everything the proving surface distributes is synthetic; the real databases
are read here, once per capture cycle, and what leaves this machine is measurements. Developers
never hold credentials to Dev, QA, or UAT — that boundary is this runbook's product.

The path has four legs, in order: pin the toolchain and prove the machine; capture the three
environments and review the classifications; merge, bake, and validate the first template
against the captured reality; stand the Azure DevOps nightly up so the template refreshes
without this machine. Each leg ends with something filed — a ledger row, a committed artifact,
a report — so the next person can see where the path stands without asking.

## 0 — What this machine needs

- The tooling monorepo, cloned at the pinned ref the estate's bake pipeline names (its
  `toolingRef` parameter). The `twin` CLI runs from it: `dotnet run --project
  sidecar/projection/src/Twin.Cli --` from the monorepo's `sidecar/projection/` directory.
- The .NET SDK the monorepo's `global.json` names, and one local SQL Server the captures and
  the bake can use — a container engine (the monorepo's `warm-sql.sh` owns bring-up) or any
  local instance reachable by connection string.
- Read access to Dev, QA, and UAT — restored copies are the safest form (capture from a
  restored backup, not the live server); read-only credentials to the live databases are the
  alternative when restores are impractical. `twin evidence import` issues SELECT statements
  only and needs read permission alone.
- The estate repository cloned beside the monorepo, with its twin root (the directory carrying
  `twin.json`; `estate-kit/twin.starter.json` is the starting shape).

## 1 — Pin the toolchain; prove the machine (the B0 leg)

1. Read the estate pipeline's DacFx version from the Azure DevOps → Octopus publish task, and
   record it with the matching sqlpackage in `estate/toolchain.md` — the two UNPINNED rows.
   Every trust claim in the tree floats until these are pinned; the acceptance below re-stamps
   what this machine actually runs either way.
2. Run the per-machine acceptance: `estate-kit/setup-proving-machine.sh` (or `.ps1` on
   Windows), with `--skip-template` on this first pass — no template exists yet. Every check
   must pass; append the printed machine row to `estate/toolchain.md`.
3. Mirror the pipeline's publish flags into the proving profiles if they differ (the
   `FINDINGS_AND_CHANGES.md` Part 5 open item closes here: verify a real publish leaves an
   added reference trusted, `is_not_trusted = 0`).

## 2 — Capture the three environments (the B1a leg)

1. Restore (or reach) the three copies, and export one connection string per environment:

   ```bash
   export TWIN_DEV_CONN='Server=...;Initial Catalog=DevCopy;...'
   export TWIN_QA_CONN='Server=...;Initial Catalog=QaCopy;...'
   export TWIN_UAT_CONN='Server=...;Initial Catalog=UatCopy;...'
   ```

2. Author one capture configuration per environment in the estate's twin root — the
   collision-refusal law holds because each import sees exactly one source. Three files,
   `twin.capture-dev.json` / `twin.capture-qa.json` / `twin.capture-uat.json`, each shaped:

   ```json
   {
     "estate": { "tables": "<your-ssdt-project>/Tables/**/*.sql", "staticData": [] },
     "container": { "name": "twin-mssql", "port": 21433 },
     "seed": 7,
     "evidence": {
       "rich": "twin/dev.rich.json",
       "sources": [
         { "name": "dev", "rendition": "physical", "conn": "env:TWIN_DEV_CONN",
           "tables": ["dbo.Customer", "dbo.Order"] }
       ]
     },
     "scenarios": { "default": {} }
   }
   ```

   Per environment, change the `rich` path, the source `name`, and the `conn` variable. The
   `rendition` names how that environment realizes the model: `physical` for the OutSystems
   cloud metamodel's naming (the import keys evidence to the logical entity names over the
   physical realizations — the cross-environment join key), `logical` where the schema already
   carries the on-premises names. The `tables` list is a closed set — start with the tables the
   cutover's first changes touch and widen deliberately; every listed table must bind, and an
   ambiguous or missing one is a named refusal, never a silent skip. Add `"sampleRows"` to a
   source to cap the scan cost on very large tables (counts stay exact).

3. Import each environment (`TWIN_CONFIG` selects the file):

   ```bash
   TWIN_CONFIG=<twin-root>/twin.capture-dev.json dotnet run --project src/Twin.Cli -- evidence import
   TWIN_CONFIG=<twin-root>/twin.capture-qa.json  dotnet run --project src/Twin.Cli -- evidence import
   TWIN_CONFIG=<twin-root>/twin.capture-uat.json dotnet run --project src/Twin.Cli -- evidence import
   ```

   Three rich packs land under `twin/`. Rich packs hold captured values (vocabularies) and stay
   **out of the repository**; only the shape tier and the reports are committed.

4. **The human gate — review the classifications.** Run `twin classify` (against the merge
   configuration below, twin up first) and review the proposed personal-data classifications by
   hand: every column that holds personal data below the cardinality threshold must be marked
   so its values synthesize rather than re-emit. Bless the reviewed artifact as
   `twin/corrections.json` and add `"corrections": "twin/corrections.json"` to the merge
   configuration. This review is on the cutover's critical path; nothing distributes before it.

## 3 — Merge, bake, validate (the first template)

1. The merge configuration is the twin root's `twin.json` (from `estate-kit/twin.starter.json`):
   `evidence.rich` names the merged pack's landing path and `evidence.merge.inputs` the three
   captured packs. Run the crossover and read its report:

   ```bash
   TWIN_CONFIG=<twin-root>/twin.json dotnet run --project src/Twin.Cli -- evidence merge
   ```

   The report (`twin/evidence-merge.report.json`, committed — literal-free) names each winning
   extreme's environment; a configured environment whose pack is missing is a refusal, because
   merging without it would let an average replace an extreme. Then derive and commit the shape
   tier: `twin evidence derive`.

2. Bake: from the monorepo's `sidecar/projection/`,
   `bash scripts/twin-bake-template.sh <twin-root>`. The bake converges the twin at the estate
   head, mints from the merged pack, plants and asserts the witness pass, runs the fidelity
   audit as a hard gate, stamps the template identity, and freezes the `.bak` beside its
   manifest.

3. **The operator-reality validation.** The bake already ran `twin evidence audit`; read its
   report (`twin/evidence-audit.report.json`) and file it beside the manifest. The audit is the
   surface demonstrating its claim on the real captures, per environment: every blocking
   verdict — presence, null rate, maximum length, duplicates, orphans, numeric envelope — holds
   against each environment's own pack, with the witness pass's lawful skips exempted, and the
   advisory margins (vocabulary coverage, distinct counts) are the fidelity report card. A
   blocking failure fails the bake by design; re-merge and re-bake rather than distributing a
   template that under-blocks an environment.

4. Prove one machine end to end on the artifact: re-run `setup-proving-machine` without
   `--skip-template`, pointed at the baked template's directory. Restore, identity, reset, and
   the acceptance suite must pass. File the manifest's commit + data fingerprint into
   `estate/toolchain.md` (the proving-template row).

## 4 — Stand the nightly up (the Azure DevOps leg)

Once, in this order:

1. **The feed.** Create the Azure Artifacts feed the nightly publishes to (Universal Packages;
   the pipeline's `feed` parameter). Until it exists, a network share works — `get-template`
   takes `--from <dir>` as the day-one fallback — and the pipeline still attaches the per-run
   artifact.
2. **The service connection.** Create the GitHub service connection that lets the pipeline
   check out the tooling monorepo, and set the pipeline's `githubServiceConnection` parameter
   to its name. The `toolingRef` parameter pins the monorepo ref; bump it deliberately.
3. **The vendor pull request.** From the monorepo, run `scripts/publish-to-estate.sh
   <estate-clone>`; review the clone's diff, merge `ssdt-agent/copilot-package/.github/` into
   the repository's `.github/` (the one manual step), and raise the pull request in Azure
   DevOps. The drop's `VENDOR.json` names the source commit.
4. **The pipeline.** Import `ssdt-agent/estate-kit/azure-pipelines.bake.yml` as a pipeline and
   set its parameters (twin root, lane, feed, package name). Its header carries the same
   prerequisites this section does.
5. **The first nightly.** Watch it run; download its template and manifest; file the manifest's
   identity into `estate/toolchain.md` and the audit report beside it. From then on the
   template refreshes nightly at the estate head, and this machine is needed only for the next
   capture cycle.

## The refusals this path can meet

Every refusal is named and stops before damage; none is a dead end.

- `twin.evidence.sourceMissingTable` — a closed-set table is absent in that environment.
  Narrow the set for that environment, or fix the coordinate.
- `twin.evidence.ambiguousEntity` — two entities in a physical source share a logical name.
  Narrow the source's modules, or rename; the coordinate must bind uniquely.
- `twin.evidence.merge.inputMissing` — a configured environment has no captured pack. Capture
  it, or remove it from the inputs deliberately; never bake around a missing environment.
- `twin.evidence.crossover.tierMismatch` — a shape pack sits among rich inputs (or the
  reverse). Re-derive; the merge refuses mixed tiers.
- A witness assertion failure or a blocking audit verdict at bake time — the template does not
  yet carry an environment's reality. The bake's output names the exact witness or verdict;
  re-capture or re-merge, then re-bake. Distribution waits.

## Cadence

The nightly bake refreshes the template at the estate head with the standing evidence. Re-run
the capture leg (this runbook's §2–§3) when the data's shape moves enough to matter: after a
release that migrates data, when a fidelity-audit margin drifts, or on a steady cadence the
team picks once the loop is routine. Re-run `twin classify` review whenever the capture set
widens — a new column is a new classification decision, and the human gate does not amortize.
