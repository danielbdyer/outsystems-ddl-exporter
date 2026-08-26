# Handoff — the proving-surface build program, closed

You are picking up after the session that executed the proving-surface plan end to end on
`claude/ssdt-agent-evaluation-handoff-m03r0i` (PR #699, stacked on #698). Read
`PROVING_SURFACE_DESIGN.md` first — its §6 status paragraph and §7 are current as of this
letter — then this file for what the documents cannot tell you: where the seams are, what bit,
and what to do next.

## Where the program stands

Every critical-path slice of the approved plan landed, each proven live before its commit:

- **The evidence spine** (`494ee59`, `3f94b52`, `997016c`, `00e2ef8`, `2424975`): the pack
  carries orphan/duplicate/selectivity/joint reality; `Crossover.merge` keeps every extreme
  with per-winner attribution (null rate = the max-rate environment's pair rescaled with
  ceiling — the named divergence from `Profile.merge`); `twin evidence merge` clamps to the
  trunk and emits the witness pair; `twin evidence audit` is the per-environment ≥-blocking
  gate with witness skips as exemptions.
- **The witness hardening** (`781b3a3`): the null-rate floor (the mint draws NULLs per-row, so
  a realized count can land under the recorded one — the floor guarantees it), null-preserving
  `IS NOT NULL` ranking in every value witness, and disjoint per-column row windows with the
  non-null budget as legality.
- **The acceptance gate** (`314c7b3`): `TwinCrossoverRehearsalTests` — three fabricated
  environments on one trunk, capture ×3, merge with attribution, mint, witness pair at zero
  failures, audit at zero blocking, then Msg 547 and Msg 1505 live under the
  production-faithful publish. 42 seconds on the warm loop; `scripts/twin-crossover-rehearsal.sh`
  drives it; the proof lane carries it in CI. Its first executions exposed and fixed three
  latent defects (the design document's §5.2 evidence block names them; the sharpest — the
  synthetic load lane derived its bulk column list from the FIRST minted row's key set, so a
  nullable evidenced column whose first row drew NULL vanished from the whole load;
  `TransferCellShaping` now takes the union, with a kernel-pool regression suite).
- **Identity and the bake** (`34a3fe5`, `001e17b`): line-ending-blind fingerprints before any
  template existed; `scripts/twin-bake-template.sh` — converge, merge-when-configured, mint,
  witness, audit-as-hard-gate, identity stamp into `[twin].[__state]`, `BACKUP WITH
  COMPRESSION`, the manifest, prune. Proven: bake → 0.5 s restore → the copy answers its own
  commit.
- **The distribution ring** (`ab0fe98`, `70071d5`, `2a256e8`, `d65972e`, `c248e14`): the ADO
  nightly (pinned monorepo checkout, `toolSource` swap parameter, Universal Packages with a
  share-path fallback) plus the GitHub bake-mechanic check; the estate kit (mirrored bash and
  PowerShell lanes; the acceptance suite went 13-for-13 live on the containerized lane, and the
  PowerShell lane parses clean under 7.4 awaiting its first Windows run); the packager's
  `estate-kit` and `vendor` targets with the citation-closure gate (258+ files, closed); the
  skills' template-identity stamps; the per-environment ledgers and `CAPTURE_POINT_RUNBOOK.md`.
- **The drift leg** (`2ac266e`): every `twin evidence import` also compares the environment's
  bound schema against the trunk head into `twin/evidence-drift.report.json`; trunk-acquisition
  failure is a named skip, never a capture block.
- **The peel** (B6): `twin` packs as the `Twin.Tool` dotnet tool (PackAsTool on Twin.Cli;
  the Version pairs with `Runs.ToolVersion`). The bake script carries the peel's seams —
  `TWIN_BIN` (an installed tool instead of `dotnet run`), `TWIN_TOOL_VERSION`, and
  `TWIN_TOOLCHAIN_MD`, with the monorepo reads guarded — and ships in the estate kit, so
  the nightly's `toolSource: dotnetTool` mode installs the pinned package and runs with no
  monorepo checkout. Proven: the ejection dry-run holds packed-tool included, and the
  cross-shape bake identity (monorepo `dotnet run` versus the installed tool driven from a
  script copy outside the repository) is byte-equal on fingerprints and image tag — the
  GitHub bake check re-proves it on every run. The two FS3511 sites Release compilation
  surfaced (EvidenceMerge's nested trunk await; EvidenceAudit's tuple-element `for` with an
  await) are hoisted per the survival rule. Owner-side remainder: push the package to a
  NuGet feed and flip the parameter (runbook §4 step 6); the charter's full repository move
  stays deliberate.
- **The image rendition** (C15 — revived after the owner confirmed Docker Desktop on the
  team's machines): `twin-bake-template.sh --image` wraps the freshly baked `.bak` in the
  bake engine's own image behind a restore-on-first-start entrypoint (generated at bake
  time; the chown-for-mssql lesson carried into the Dockerfile), tags it
  `twin-template:<lane>-<commit8>-<dataFp8>`, records the tag in the manifest's
  `imageRendition`, and prunes this lane's tags alongside the `.bak` prune. The GitHub bake
  check runs `--image` and proves the run: the restored copy answers its identity from
  `[twin].[__state]`, holds estate rows, and a restart skips the restore. Registry push
  stays out of the bake until a registry is named.
- **The existing-server seam** (B4): `twin.json` names either the managed container or an
  existing server (`server.conn` env:/file: reference; `server.database` the knob, default
  `twin`; both sections explicit refuses). Every verb resolves through
  `Twin.Runtime/TwinSubstrate.fs`: `up` requires the server reachable and never provisions
  it, `down` is the named no-op, `reset` drops only the twin database. Proven live:
  seed → status (`Managed=false`) → down (server left) → reset (database-only drop) against
  an external instance, plus the unreachable refusal on the write path and the honest report
  on the read path.

## What remains

- **The owner's steps** — everything in `CAPTURE_POINT_RUNBOOK.md`. Nothing in the monorepo
  gates them. This is the critical path to day one.
- **B6's operational tail** — push `Twin.Tool` to an Azure Artifacts NuGet feed and flip the
  nightly's `toolSource` parameter (runbook §4 step 6); the build side landed. B7 (fan-out
  skew, per-environment mints) stays later.

## What will bite you

- The pure and Docker test pools never share one `dotnet test` (the OOM rule). The Twin's pure
  pool is 110 facts; the kernel's 4758; the rehearsal runs focused in ~45 s warm.
- A background shell does not inherit the conversation's working directory — every background
  command needs its own absolute `cd`, or the run fails on relative paths.
- The rehearsal test preserves its artifacts and prints the minted landscape plus the bound
  profile ON FAILURE — read that message before re-running anything; it was built to diagnose
  itself, and it found all three latent defects that way.
- A `docker cp` into the SQL container lands root-owned; the engine reads nothing until the
  chown (the kit's restore helper carries it — do not remove it).
- `sampleNumeric` clamps to the envelope and `sampleCategorical` never emits the NULL
  sentinel — if a column ever mints all-NULL again, look at the LOAD, not σ; the fsx probe
  pattern under the scratchpad (bind the pack, run `SyntheticData.generateWithDiagnostics`,
  then `DataLoadPlan.buildWith`, and count non-null per stage) isolates the failing stage in
  one run.

## Housekeeping

PR #699's checks were green through `c248e14` with the head's runs in flight at close; the
session was subscribed to the PR, so CI failures wake it. The hourly `send_later` check-in
could not be re-armed this session (the tool call required an approval the environment did not
grant); if you can schedule, re-arm it — and either way, check the PR's checks before building
further on the branch.
