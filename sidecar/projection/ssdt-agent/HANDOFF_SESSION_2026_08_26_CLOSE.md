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

## What remains

- **The owner's steps** — everything in `CAPTURE_POINT_RUNBOOK.md`. Nothing in the monorepo
  gates them. This is the critical path to day one.
- **B4, the existing-server seam** (`TwinSubstrate = ManagedContainer | ExternalServer`): lets
  `twin up`/`seed` run against LocalDB with no Docker. Capture, merge, and audit already run
  engine-agnostically; the developers' path runs on templates, so this widens convenience, not
  capability. The plan's C8 slice describes the shape (~24 call sites in `TwinContainer`
  consumers).
- **B6, the peel** — `twin` as a distributable dotnet tool; the ADO pipeline's `toolSource`
  parameter is the swap seat. B7 (fan-out skew, per-environment mints) stays later.
- **C15, the image rendition** — optional `--image` on the bake script wrapping the `.bak`;
  explicitly droppable pre-cutover, and §7's Docker-Desktop question should be answered before
  effort goes there.

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
