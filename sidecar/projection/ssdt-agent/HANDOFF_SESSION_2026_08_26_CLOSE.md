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
- **F1, string reality** (the F-program's first slice; its law is ZERO NEW CONFIGURATION —
  no twin.json keys, no runbook steps): every import runs the twin-side reality probe
  (`RealityProbe.fs`) over each evidenced text column — empty-string, trailing-space, and
  case-collision counts plus LEN p50/p90, counts only, never values — and the audit probes
  the minted copy the same way. The merge rescales empties and trailing by the null-rate
  policy and takes collisions and quantiles by max; three new witness classes plant the
  realities synthetically (empty floor from the top of the non-null space; length-safe
  trailing reshape; a seeded token pair differing only in final-letter case, capped under
  the observed max) with the same named-skip legality; the audit blocks on presence where a
  witness can plant and holds the counts as margins. The rehearsal seeds real dirt in all
  three environments and reads it back off the minted template.
- **F2, conditional nulls and fan-out skew** (same law): the probe also discovers, per table
  within fixed bounds (two partner columns × three targets, spread threshold 0.15), which
  partner VALUE a column's nulls concentrate under — rich-tier-only, since partner values are
  literals — and the kernel's per-reference cardinality capture (five-distinct-parents floor)
  carries fan-out skew through the pack. The merge takes the widest-spread environment's whole
  conditional vector and maxes the fan-out maximum; the witness plants partition deficit
  floors LAST OF ALL (a floor destroys non-null rows, so it ranks past every claim — the
  ordering that keeps orphan and hot-parent plants alive) and re-points child rows at one real
  parent for the hot parent (legal on enforced edges — valid rows); the audit blocks on
  `fanOutMax`, advises on the p95 and on conditional-structure survival, and probes fan-outs
  on edges the read-back catalog cannot see, the same asymmetry as orphans. The conditional
  check is the floor's own deterministic guarantee (realized nulls, offset-adjusted, reach the
  recorded count clamped to the partition) — a strict hi-versus-lo rate comparison flakes
  under σ's draws and was rebuilt before it shipped. The rehearsal discovers QA's
  Rating-by-Name joint and UAT's hot region parent unconfigured and reads both back live.
- **F3, the sector mint and the deep per-environment audit** (same law): the merge embeds its
  inputs whole as labeled `sectors` in the merged rich pack (rich-only; singleton merges keep
  provenance so idempotence holds), and the mint repaints σ's rows into contiguous
  per-environment slices through the same Realize seam Faker and pins ride — vocabularies
  re-drawn per sector by largest-remainder quota (`SectorPaint.fs`, pure), keys/references/
  unique columns/nullness/the `''` sentinel untouched, and the empties-recording sector
  painting LAST so the empty floor's tail plants land in its own sector. `twin evidence
  audit` now also round-trips every input alone — throwaway database, per-environment mint,
  that pack's witnesses, profile-back, audit — the "would this block at QA specifically"
  proof, in the same blocking gate and its own report. Two measurement truths from the build:
  the probe's `''` strip must decrement DistinctCount with it (a complete vocabulary stays
  complete — `toProfile` refuses otherwise), and the profiler's five-sample numeric floor
  goes silent on heavily-floored small populations even though the planted envelope is
  there — the audit probes exact MIN/MAX wherever π is silenced, joining the orphan and
  fan-out probes.
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
  nightly's `toolSource` parameter (runbook §4 step 6); the build side landed. Of B7, fan-out
  skew landed as F2 and the per-environment mints landed as F3's deep audit leg.

## What will bite you

- The pure and Docker test pools never share one `dotnet test` (the OOM rule). The Twin's pure
  pool is 155 facts; the kernel's 4758; the rehearsal runs focused in ~90 s warm (the F3
  deep legs added three per-environment mints).
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
