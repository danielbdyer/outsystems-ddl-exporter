# THE_CLI_BACKLOG.md — building the operator surface

The slice backlog that carries `THE_CLI.md` from vision to shipped. The target
is four verbs (`project` / `check` / `explain` / `seal`) over one engine, with
the emission-family verbs collapsed into one `MovementSpec`. This backlog is the
path; `THE_CLI.md` is the destination.

Discipline: each slice lands compiling + tested (pure tests in `Projection.Tests`,
no CLI-exe dependency where possible, per the `TransferSpec` precedent). The
existing 16-verb `Program.fs` stays live until slice 7 swaps the dispatch — no
operator-facing break mid-build. Voice on every operator string per `THE_VOICE.md`.

---

## Status legend
`[x]` landed · `[~]` in flight · `[ ]` not started

---

## Slice 1 — the typed surface + target aliasing  `[x]`

The spec algebra and the `projection.json` resolver — the foundation everything
else projects onto, and the operator's headline want (aliasing).

- `src/Projection.Pipeline/MovementSpec.fs` — the axis DUs (`Destination`,
  `Scope`, `Strategy`, `DataOrigin`, `Baseline`, `Shape`), the `MovementSpec`
  record + `forDestination` defaults + `isLiveWrite`, and the four-verb `Intent`.
- `src/Projection.Pipeline/MovementSurface.fs` — `TargetConfig` (parse + fromFile,
  D9-guarded), `Surface.resolveTarget` (the `--to` resolution order), the project
  flag reader, `Surface.buildProject`, `Surface.parse` (argv → `Intent`).
- `tests/Projection.Tests/MovementSurfaceTests.fs` — 19 tests: config parse,
  D9 rejection, resolution order, defaults + precedence, argv → Intent.

**Witness:** `dotnet test --filter MovementSurfaceTests` → 19/19. Build clean
(0 warnings, `TreatWarningsAsErrors`).

---

## Slice 2 — the engine entry: `Movement.run`  `[ ]`

One function the `project` intent calls, dispatching on `MovementSpec` to the
existing machinery (no new emission logic — a router over what is already built).

- Signature: `Movement.run : MovementSpec -> Task<Result<MovementReport>>`.
- Routing (reuses today's runners):
  - `Folder` + `Shape.Bundle/Ssdt/Skeleton` → `Compose.runWithConfig` /
    `runSkeletonOnly` (emit family).
  - `Docker` → `Deploy.runFromV1Json`-equivalent over the resolved model.
  - `Live` + `Baseline.Auto` → read A via `ReadSide.read`, then `MigrationRun`
    (the auto-A principle: empty A ⇒ create; non-empty ⇒ differential).
  - `Data.FromTarget` → `TransferRun` ingest leg with `--rekey` → reconciliation.
- `MovementReport` unifies the per-runner reports behind one narration surface.
- **Witness:** a `Movement.run` table-test mapping each protein's `MovementSpec`
  to the runner it dispatches to (no SQL — assert the routing decision).

**Dep:** slice 1. **Risk:** the auto-A read for `Live` is the load-bearing
unification; gate it behind the preview (slice 4) before any `--go`.

---

## Slice 3 — the two-gate safety model  `[ ]`

- Preview-by-default for `Live` writes: `Movement.run` on a non-committed `Live`
  spec returns the plan (the `MigrationRun.preview` artifacts), never writes.
- `--go` (intent) × `PROJECTION_ALLOW_EXECUTE=1` (authorization): the live write
  needs both; the refusal names which is absent (exit 7).
- Declared-loss carry-through: `--allow-drops` / the loss-token refusal (exit 9),
  routed from `MigrationRun` / `TransferRun`.
- **Witness:** preview-returns-no-write test; gate-refusal exit-code tests.

**Dep:** slice 2.

---

## Slice 4 — narration in the voice  `[ ]`

The preview footer ("Preview only. Re-run with --go to apply."), the refusal
lines, the irrelevant-modifier note. All via the `THE_VOICE.md` register
(stative, agentless, imperative direction, no pronouns, no "your"); coded events
with harvested copy per the `code ⇔ copy` totality test.

- **Witness:** a `code ⇔ copy` totality test over the new surface's event codes;
  voice-lint clean.

**Dep:** slices 2–3. **Reads:** `THE_VOICE.md`, `THE_STORYBOARD.md`.

---

## Slice 5 — `check` / `explain` / `seal` typed sub-surfaces  `[ ]`

Replace the skeleton's tail-capture with typed sub-intents over the existing
runners:

- `check` → `fidelity` (canary) · `drift --to` (`DriftRun`) · `data --to`
  (`DataIntegrityChecker`) · `ready` (`RunLedger`).
- `explain` → `diff <A> <B>` · `policy <a> <b>` (`PolicyDiff`) · `suggest <config>`.
- `seal` → eject (`EjectRun`) · `approve <version>` (`ApprovalWorkflow`).
- **Witness:** argv → typed-sub-intent tests per verb.

**Dep:** slice 1.

---

## Slice 6 — global-flag + config-path plumbing  `[ ]`

- Strip `--pretty` / `--json` / `-v` / `--help` before dispatch (carry today's
  `main` behavior).
- `projection.json` discovery: repo root, `PROJECTION_CONFIG` override.
- `--to` env aliases resolve through `TargetConfig` (already built); wire the
  auto-record of an episode when a target carries a `store`.
- **Witness:** flag-strip tests; config-discovery test.

**Dep:** slices 1, 5.

---

## Slice 7 — the dispatch swap (the cutover)  `[ ]`

Wire `Surface.parse` + `Movement.run` into `Program.fs main`, retiring the
16-verb match. The per-verb conditional trees (documented in the 2026-06-07 verb
audit) collapse into the one parameterized pipeline — the activation of the
"latent" calculus the morphology named.

- Keep the exit-code table stable (THE_CLI.md §9).
- Delete the dead per-verb run functions once their behavior is covered by
  `Movement.run` + the typed sub-intents.
- **Witness:** the full existing CLI behavior suite re-expressed against the new
  surface; the operator-reality canary stays green through the swap.

**Dep:** slices 2–6. **Risk:** highest — this is the operator-facing break;
land it behind a full behavior-parity test pass.

---

## Slice 8 — naming + open decisions  `[ ]`

Resolve the three open decisions (THE_CLI.md §12): the hero verb's name
(`project` vs `ship`/`deliver`); `check ready` vs `seal history`; synthetic
volume control. Each is a one-line change once the surface is live; defer until
the surface is exercised so the choice is evidence-led.

---

## Critical path

`1 → 2 → 3 → 4 → 7` is the spine (project working end-to-end, safe, in voice,
swapped in). Slices 5 and 6 are parallelizable against 2–4. Slice 8 is post-swap
polish.

## What is deliberately NOT in scope

The engine internals are unchanged — this backlog re-faces, it does not re-emit.
`Compose` / `MigrationRun` / `TransferRun` / `Deploy` / `EjectRun` stay as the
machinery; the slices route to them. No new emission logic, no algebra change.
