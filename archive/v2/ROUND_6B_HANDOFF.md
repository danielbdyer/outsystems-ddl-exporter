# Round 6b — Handoff to the next agent

You're picking up the fidelity-matrix build-down mid-Round-6b. The instrument is
`THE_USE_CASE_ONTOLOGY.obligations.md` — read its §0 (the criterion-anchored
**two-leg discernment**: judge each acceptance criterion independently against
(1) implemented reality and (2) a LIVE discriminating test; direction is always
criterion→{code,test}, never test→criterion), then §7 (the scorecard + HOLLOW
register) and §8 (the queue). This letter tells you what's done, what's next, and
the lessons that were paid for in blood this session.

## Where we are: HELD 49 / 57 (86%)

Pushed on `claude/masterwork-matrix-ontology-KFG25`. Tip: `d369129`.

- **Schema, Identity, Provenance planes: fully HELD.**
- **Round 6a** (`8c6ac5d`): D7+G4 (scoped MERGE delete arm), G9 (data-aware NOT-NULL
  tightening pre-flight), I7 (rename+reconcile compose) → HELD.
- **Round 6b Wave 1** (`d3067f2`): X2 (UAT re-key composed at the CLI call sites —
  the co-wrong `Map.empty` is gone) + G0 (mandatory `Preflight.allReporting` +
  `classify : code → (exit,label)`) → HELD. Plus two seams BUILT:
  - `ReadSide.cdcCaptureCount cnn (tracked: string list) : Task<int>` — the
    production CDC capture-count reader (exact-count Docker witness in
    `CdcMeasureTests.fs`: no-op redeploy = +0; INSERT = +1; UPDATE = +2).
  - Full-export diff-vs-prior store leg: `Compose.runWithConfigAndStore` /
    `FullExportRun.executeWithStore` (reads prior `LifecycleStore`, computes
    displacement, accumulates the P6 refactorlog, emits `ChangeManifest`, records
    the episode). Witnesses in `FullExportStoreTests.fs` (6 pure, green).
- **X3** (`d369129`): wired `full-export --lifecycle-store <path> [--env L]` to
  `executeWithStore` → the publication bundle. Witness in `FullExportCliTests.fs`
  (diff-vs-prior discriminator + reconstruction).

## Remaining: 5 HOLLOW (X1/X4/X5/X7/X8) + 3 NEITHER (G10/D10/X6)

### IMMEDIATE NEXT — X4 + X8 (the CDC-measure seam's CLI wiring). Both Docker.
The `cdcCaptureCount` reader exists; it just isn't called by any CLI verb. Both
criteria demand the count be **measured and surfaced**, not merely that no DDL ran.

- **X4 (redeploy, acceptance ~138):** "redeploy an unchanged model. Pass iff zero
  ALTERs AND zero CDC captures, **both measured**." Wire into the migrate verb
  (`Program.runMigrateExecute`, the in-place `MigrationRun.execute` arm, ~`Program.fs:1104`):
  after the (no-op) execute, if `ReadSide.cdcTrackedTables cnn` is non-empty, take a
  baseline before and a count after via `cdcCaptureCount`, surface the delta. Docker
  witness: deploy → enable CDC → redeploy unchanged → assert surfaced delta = 0; a
  real change → nonzero (exact-count, like `CdcMeasureTests`).
- **X8 (canary, acceptance ~142):** "canary … asserts CDC-silence on idempotent
  redeploy." Wire into `Program.runCanary` (~`Program.fs:341`) / `Deploy.runWideCanary`
  (`Deploy.fs:1361`): enable CDC on the deployed substrate, redeploy idempotently,
  assert `cdcCaptureCount` delta = 0. Docker witness.

Both touch `Program.fs` (the chokepoint) and are Docker → **do them yourself,
serially** (or one isolated-worktree agent doing both); do NOT fan them out as two
concurrent Docker tracks (host OOMs on concurrent SQL).

### Wave 2 (file-disjoint; ≤1 Docker per wave)
- **X5 (in-place migrate w/ data, ~139):** add `executeWithDataAndRecord` as a NEW
  function in `MigrationRun.fs` (DO NOT edit `execute` — the G9 gate lives there at
  ~`MigrationRun.fs:404`); measure via `cdcCaptureCount`, build a non-empty
  `DataObservation`, record the episode. Docker witness: recorded episode carries the
  measured capture count. Owns `MigrationRun.fs` (additive).
- **X7 (drift, ~141):** "diff deployed vs **the model**." Today `verify-data`
  (`Program.runVerifyData` ~`Program.fs:718`) compares two deployed substrates, not
  deployed-vs-model. Add a new `DriftRun.fs` (pure diff logic via
  `PhysicalSchema.diff (ofCatalog model) (ReadSide.read cnn)`) + a verb/flag. Docker
  read-back; pure diff.
- **X6 (eject, ~140; fork RESOLVED = append-forever):** new `EjectRun.fs` — read the
  timeline store, assemble the append-forever provenance package (every episode + full
  accumulated refactorlog), verify `EpisodicLifecycle.reconstructLatestSchema` from
  genesis = frozen state. Mostly pure. Owns a new file.
- **D10 type leg (~73; fork = explicit named `EmissionMode`):** new `EmissionMode.fs`
  with closed DU `Incremental | WipeAndLoad`; pure tests (default Incremental; distinct
  in the type system). Defer the live TRUNCATE wiring to Wave 3.

### Wave 3 (SERIALIZED — both edit `TransferRun.fs`'s write seam)
- **G10 (~124; fork = resumable/idempotent):** idempotent-upsert + phase-marker envelope
  around `writePlan` (`TransferRun.fs:141`) so a mid-load failure is recoverable on
  re-run (no duplicate rows). Docker witness: inject mid-load failure, re-run, assert no
  dupes + complete state. **`TransferRun.runCore`'s gate chain (~415–481) is recent
  hardened work — wrap, do not rewrite.**
- **D10 live leg:** wire `WipeAndLoad` → FK-ordered TRUNCATE + reload, CDC-gated. Rebase
  on G10's commit (same file).

## Lessons paid for this session — internalize before you dispatch anything

1. **Parallel subagents MUST use `isolation: worktree`.** Wave 1 ran four tracks in the
   SHARED main worktree by dispatch error → commit-tangle, agents reverting each other's
   `.fsproj`/test files, and concurrent-build corruption that produced PHANTOM gate
   failures. Recovery cost a clean `obj/bin` flush + rebuild + re-gate and a
   `git reset --soft` collapse of five tangled commits. Always isolate.
2. **Worktree bases drift far behind HEAD** (they branch from ~`40666466`, not HEAD).
   Brief every track to make MINIMAL, ADDITIVE changes and report `base SHA + commit SHA`;
   expect to re-apply `Program.fs` intent by hand at integration (it has diverged most).
3. **≤1 Docker track per parallel wave** — the 4-core/15GiB/no-swap host OOMs if two test
   processes hit the warm SQL container at once.
4. **Run the test, never trust build-only.** Wave 1's G0 witness was committed build-only
   and was 3-RED on running (hot-task gates recorded at list construction, not at await);
   another track caught + fixed it. A green build is not a green suite.
5. **Clean-rebuild before the authoritative gate** if anything ran concurrently: delete
   `obj/bin`, `dotnet build`, then `TEST_NO_BUILD=1 scripts/test.sh all`. TRX at
   `/tmp/projection-test-results/{fast,docker}.trx`; the docker pool is ~10min serial.
6. **Reuse existing primitives.** G9 reused `Preflight.tighteningOverlay` instead of a
   redundant diff-walk; X4/X8 reuse `cdcCaptureCount`; X3 reused `executeWithStore`. The
   recon agent's job is to find the seam, not rebuild it.
7. **Watch for cross-batch gate interactions.** R6a's G9 was shadowed by Track-E's G8
   narrowing refusal (the correct layering — G8 schema-blind declared-loss, G9 data-aware)
   so its witness needs `DeclareAll`. The full-suite gate is what catches these.

## How to verify a cell is genuinely HELD
A green test is necessary, not sufficient. For each cell, name the **adversarial input**
the discriminating test pins — the input a plausible-but-wrong implementation diverges on
(e.g. X2: a source user that collides-by-ID with one sink entity but matches-by-email a
third; the FK must follow the email match — `Map.empty` fails it). If you can't name the
divergence, the test is HOLLOW. Refresh §7/§8 of the matrix after each batch.
