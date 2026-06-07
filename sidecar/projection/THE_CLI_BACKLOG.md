# THE_CLI_BACKLOG.md — building the operator surface

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

## Follow-ups (small, evidence-gated)

- `--reconcile <table>:<col>` on `project` (today: `--rekey <csv>`).
- `--scope` / `--how` / `--from` engine plumbing (today: accepted + noted).
- A `DECISIONS.md` entry recording the four-verb re-envisioning.
