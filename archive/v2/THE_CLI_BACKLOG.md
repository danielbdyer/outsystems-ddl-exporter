# THE_CLI_BACKLOG.md — building the operator surface

> **SUPERSEDED IN PART (2026-06-08).** `THE_CLI.md` was re-derived against the
> operator's real workflows: the surface collapses further to **`projection
> <flow> [--go] [--fresh] [--allow-drops]`** over a two-layer config
> (`environments` with `access`/`grant` + named `flows`), with the baseline *A*
> first-class and a `seal`→episode→`report` provenance pair. The four-verb work
> below (the `MovementSpec` engine seam, the pure totality-tested `Command.plan`,
> the two-gate safety model, the voiced refusals) is the **foundation the new
> surface builds on** — it is not discarded. A fresh slice plan for the
> environments/flows layer lands here when that build opens; until then this
> section records what shipped under the prior shape.

The slice backlog that carried `THE_CLI.md` from vision to shipped. The target
— four verbs (`project` / `check` / `explain` / `seal`) over one engine, with
the emission-family verbs collapsed into one `MovementSpec` — is **landed**.

**STATUS — COMPLETE (2026-06-07).** The four-verb surface is the primary (and
only) CLI dispatch; the legacy 16-verb surface and its Argu glue are removed.
The new verbs delegate to the existing proven engine faces (`Compose` /
`Deploy` / `MigrationRun` / `TransferRun` / `DriftRun` / `EjectRun` /
`DataIntegrityChecker` / `PolicyDiff` / `FullExportRun`), so exit codes and
behavior are preserved by construction. Verified: pure pool 2827 passed / 0
failed; `MovementSurfaceTests` 19/19; runtime smoke (help, routing, aliasing,
D9 refusal, exit codes).

---

## Status legend
`[x]` landed · `[~]` in flight · `[ ]` not started

---

## Slice 1 — the typed surface + target aliasing  `[x]`

`MovementSpec.fs` (axis DUs + `MovementSpec` + `forDestination` + `isLiveWrite`
+ `Intent`) and `MovementSurface.fs` (`TargetConfig` parse/fromFile, D9-guarded;
`Surface.resolveTarget`; the project flag reader; `Surface.parse`). 19 pure tests.

## Slice 2 — the engine entry  `[x]`

`executeProject` (Program.fs) routes a `MovementSpec` to the engine faces:
folder → `runEmit` / `runEmitSkeletonOnly` / `runFullExport` (config bundle);
docker → `runDeploy`; live → preview / `runMigrateExecute` / `runMigrateWithData`
/ `runFullExportLoad` (config + `--go` = publish + load). The auto-A read for
live destinations is `runProjectLivePreview` + the migrate runners (which read A
via `ReadSide.read`).

## Slice 3 — the two-gate safety model  `[x]`

Live writes preview by default (`runProjectLivePreview`; `runTransfer` DryRun for
a `--data` source); `--go` is intent, `PROJECTION_ALLOW_EXECUTE=1` is
authorization (R6, exit 7, enforced inside the migrate runners); declared loss
via `--allow-drops` (exit 9).

## Slice 4 — narration in the voice  `[x]`

New surface strings follow the register (stative refusals; `noteUnhonored`
emits a named note for an accepted-but-unhonored axis — no silent drop). The
delegated runners keep their existing narration. (The full `code ⇔ copy`
totality harness remains owned by `THE_VOICE_INTEGRATION.md`; not duplicated.)

## Slice 5 — check / explain / seal  `[x]`

`executeCheck` (fidelity canary [+`--cdc-silence`] / drift / data / ready),
`executeExplain` (diff / policy / node / suggest / migrate-preview),
`executeSeal` (eject / approve) — each parses its tail and delegates.

## Slice 6 — global-flag + config plumbing  `[x]`

Global `--pretty` / `--json` / `-v` / `--help` stripped before dispatch (kept);
`discoverConfig` reads `projection.json` (or `PROJECTION_CONFIG`); `--to`
aliases resolve through `TargetConfig`; a target's `store` flows into the spec
so a live `--go` records an episode automatically.

## Slice 7 — the dispatch swap  `[x]`

`main` rewritten to: strip globals → `discoverConfig` → `Surface.parse` →
match `Intent` → executor. The 16-verb match is gone. The per-verb conditional
trees collapse into the one parameterized path (the latent calculus, activated).

## Slice 8 — naming + open decisions  `[x]`

- **Hero verb = `project`** (the domain's own word; committed).
- **`readiness` → `check ready`** (gate-shaped).
- Synthetic volume control and `--scope`/`--how`/`--from` engine knobs are
  *accepted at the surface* but not yet honored by the engine — surfaced as a
  named note, not a silent drop (see THE_CLI.md §12).

---

## Consciously dropped (named, not silent)

- `full-export --mute-category` / `--debug` (niche observability flags; `-v`
  maps to Verbose).
- `emit --config` `[accepted]` console narration (the data rides the NDJSON
  stream).
- `transfer` `--source-env` / `--sink-env` and `--reconcile` on `project`
  (re-key flows through `--rekey` user-map; named `--reconcile` is a follow-up).

## Fidelity pass (2026-06-07) — alignment hardening

After the four-verb surface shipped, a fidelity pass tightened the
surface↔engine alignment:

- **#1 — pure, totality-tested executor `[x]`.** `Surface.planProject :
  TargetConfig -> MovementSpec -> ExecutionPlan` (a `PlanAction` DU) is the
  pure routing; `executeProject` is a thin runner over it. A totality test
  sweeps the full axis product (3×3×5×3×2) — no combination throws, every
  `Refused` is a named exit code; routing-table example tests pin each variant.
  The surface→engine map is *proven*, not trusted.
- **#2 — axes honored or named `[x]`.** `--scope data` routes to the DML-only
  transfer; `--scope schema` skips the data leg; `--reconcile` threads to the
  re-key. The still-unhonored axes (`--how` / `--from` / `--data synthetic|none`)
  are a pure, tested `ExecutionPlan.Notes` list — surfaced, never silently
  dropped. (Engine-wiring those knobs — TransferRun emission-mode, MigrationRun
  genesis-force, a Faker source — remains the deeper follow-up.)
- **#4 — docs honest `[x]`.** A proteins-parse test suite (THE_CLI.md §8
  one-liners → asserted `MovementSpec`) locks the documented surface to the
  parser.
- **#5 — self-description + first-run `[x]`.** `explain registry` names the
  engine's registered transforms; `projection init` scaffolds a `projection.json`
  (refuses to overwrite; D9-safe conn references).
- **#3 — route CLI copy through Voice `[x]`.** Slice A (2026-06-07). The four
  verbs now share one pure plan (`Command.plan : TargetConfig -> Intent ->
  ExecutionPlan`, the `PlanAction` DU spanning project/check/explain/seal) with
  **coded refusals** (`Refused of exit * ValidationError`); the single runner
  (`runPlan`) voices every refusal through `Voice.errorSurface` to **stderr**
  (channel split preserved via `TtyRenderer.renderVoicedError`). The cross-verb
  totality test sweeps it; the voice-clean test covers the new `cli.*` codes.
  The colliding `Projection.Pipeline.Surface` module was renamed to `Command`
  (the CLI's `Surface` is the voice surface). This is `THE_VOICE_INTEGRATION.md`
  slice 4 for the new surface; the legacy `printErrors` sites in the engine
  `run*` functions remain on the plain path (their voicing is the rest of slice 4).

## Engine-wiring follow-ups (evidence-gated)

- `--how replace|fresh` → **DONE (Slice B partial, 2026-06-07)** — threads the
  `EmissionMode` through `Transfer.runThroughConnectionsWithEmission` for the
  pure-transfer data load (`project --to <sink> --data <src> [--scope data]`).
  On a combined schema+data migrate it stays the migrate's incremental MERGE
  (noted). Extending it to the migrate-with-data leg is the remainder.
- `--from empty` → genesis-force in `MigrationRun`.
- `--data synthetic [--rows N]` → a Faker data source.

---

## Environments / flows build (2026-06-08) — the re-grounded surface  `[x]`

**STATUS — COMPLETE (2026-06-08).** The `THE_CLI.md` (2026-06-08) target
shipped: `projection <flow> [--go] [--fresh] [--allow-drops]` over a two-layer
config (`environments` + `flows`), with `report` wired to the durable episode
store. Full pure pool green throughout.

- **F1 — the config schema `[x]`.** `Access` (Bundle/Direct/Docker) × `Grant`
  (SchemaAndData/DataOnly), `Environment`, `FlowSource`
  (Env/Model/Synthetic/NoData), `Flow` (from/to/rekey/tables); parse +
  D9-guard. 14 pure tests.
- **F2 — flow resolution `[x]`.** `resolveFlowSpec` (`to` access → Destination,
  `grant` → Scope + the **grant refusal**, `from` → DataOrigin, `--fresh` →
  Strategy/Baseline) riding the totality-tested `planMovement`; `planFlow` the
  grant gate. 9 tests.
- **F3 — the dispatch swap `[x]`.** `projection <flow>` (verb implied; closed
  secondary verbs check/explain/seal/report/init); the no-arg flow menu; help
  rewritten. 6 tests + runtime smoke.
- **F4 — seal→episode→report `[x]`.** `Environment.store` (the durable
  timeline); `report <flow>` reads it and renders the recorded
  `ChangeManifest` series (per-edge `‖δ‖` surfaced); a live `--go` records an
  episode into the target store (spec carries `toEnv.Store`). `ReportRun`
  (fromChain/fromStore/render) composes the already-built `LifecycleStore` +
  `ChangeManifest`. A storeless target refuses (cli.report.noStore). Tests:
  `ReportRun` round-trip + the planReport routing.
- **F5 — deprecate `targets` / `project --to` `[x]`.** Removed the `targets`
  block, the `project` verb, `buildProject`, `resolveTarget`,
  Target/TargetAddress/ResolvedTarget, and the project-only flag readers;
  `planProject` → `planMovement` over `resolveLiveConn`; `TargetConfig` →
  `ProjectionConfig`. `MovementSurfaceTests` rewritten to the flow surface.

## §12 follow-ups (2026-06-08) — status

Worked the THE_CLI.md §12 list:

- **`explain <flow>` (live preview) `[x]`** — B vs the target's last sealed
  episode (the preview sibling to `report`'s history). `bbb9d60`.
- **pre-flight gates `[x]`** — already engine-built (CDC-tracked sink +
  data-compat NOT-NULL tightening, refuse exit 9); added `--allow-cdc` to the
  flow surface so the CDC gate is overridable. `e5ddcb1`.
- **`tables` subset `[x]`** — honored on the data-transfer leg (only listed
  kinds load; the rest of the sink untouched; unknown names refused). `ebf0baa`.
- **rename-aware migrate-with-data `[~]`** — the pure rename-aware transfer
  (`runWithRenames`) exists; the migrate-with-data combination has no current
  flow consumer, so it is documented as a precondition rather than built ahead
  of evidence.
- **`from: synthetic --profile` (Faker) `[ ]`** — the one net-new feature
  left. Slice plan: (S1) a pure generator `SyntheticData.generate : Profile ×
  Catalog × seed → Map<SsKey, StaticRow list>` (FK keys drawn in topo order;
  categorical/numeric distributions + null-rates honored; deterministic) +
  unit tests; (S2) a synthetic-load runner (generate → `DataLoadPlan.build` →
  write to sink — synthetic has no source DB, so it does NOT reuse
  `runThroughConnections`); (S3) profile capture via `LiveProfiler` against the
  `--profile` env + CLI wiring of `DataOrigin.Synthetic`. Reuses the evidence
  in `Profile` (`ColumnProfile`/`CategoricalDistribution`/`NumericDistribution`)
  and the FK-aware approach proven in the test `FixtureGenerator`.

Deferred (unchanged): `--fresh` genesis-force in `MigrationRun`.
