# HANDOFF — operator intervention, the pretty TTY, and the faithfulness audit

> Forward-looking letter to the next agent. Branch: `claude/vector-wave-4-5`
> (pushed to origin). Written 2026-06-17 at a clean handoff point.

## Where you're standing

You're picking up a feature branch that turned three operator-facing asks — *interventional
tactics*, a richer `--pretty` TTY, and a *config wizard* — into shipped, tested code, and
ran a faithfulness audit alongside. The git log is your friend here; every commit message is
detailed. Read them top-down:

```
9ff4e437  #9 — hoist the tightening gate prompt before the live board
830568f5  tightening relax-always (A44-safe)
3e2bef1b  tightening-relaxation gate — relax-once (the steward override)
63747b06  --pretty drives the live board (+ injectability + timeline + CDC proof)
81e582a0  Intervene.fs — operator-intervention prompt seam (inert, tested)
ef87b649  AUDIT_2026_06_17 — faithfulness / no-silent-tightening sweep
c196164a  audit F5 — fix the inconsistent imposed decimal default
```

The tree is clean. ~280 pure tests were green across the session (`scripts/test.sh fast`, or
`dotnet test --filter` — note **`dotnet` is not on PATH**; it lives at
`$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe`).

## What's solid (don't re-derive it)

- **`Intervene.fs`** (`src/Projection.Cli/`) — the reusable prompt seam. `chooseOrDefault` /
  `promptValueOn`, gating on stderr **and** stdin being TTYs, degrading to a named fallback
  headless. Copy is resolved at the call site (the seam authors none). Tested.
- **`--pretty`** drives the live `Watch` board on every face (`--watch` deprecated); the board
  is console-injectable (`Watch.renderWatchOn`) for `TestConsole`; the timeline strip
  (`View.Timeline` + `Theme`) and the §6 CDC-measure proof in the verdict panel are in.
- **The tightening-relaxation gate** (`RunFaces.tighteningPreflight`) — the steward override
  you should understand before touching migrate: when the team's model narrows a column the
  live data can't satisfy, the operator gets **Halt / Relax-once / Relax-always**. The
  relaxation is a *named, tracked* event (`migrate.tighteningRelaxed`), never silent.
  Relax-always persists to `projection.json` via `RelaxationStore` (a **surgical** JSON merge
  — preserves every other key; does NOT touch `renderConfig`/A44). The gate prompt now fires
  *before* `renderWatch` (a Spectre `Live` region and a prompt can't share the terminal —
  that was #9). `Preflight.relaxTightening` flips only `Column.IsNullable`.

## What to do next (pick a thread; each stands alone)

1. **Finish the audit remediations** — the live work when this handed off. See
   `AUDIT_2026_06_17_FAITHFULNESS_SWEEP.md` → "Continuation status" for the per-finding triage
   and dispositions. F5 is done. The operator wanted "all of them"; do the **safe/quick** batch
   (F8/F11/F12/F14/F15) and **bounded** ones (F2+F13 register the unregistered mutators;
   F7-config-preserve) freely, but the **heavy four** (F1 collation/goldens, F3 totality/
   structural, F4 Docker round-trip, F10 IDENTITY/goldens) deserve **individual verified
   commits** — they move goldens, restructure the totality proof, or need Docker. Don't batch
   them. Run the tests and commit per logical unit.
2. **Wizard W0** (task #1) — the operator explicitly wanted this as a *fresh thread*. The full
   design is grounded in the earlier plan: refactor `runInit` to build a typed `ProjectionConfig`
   and emit via `ProjectionConfig.render` (the A44 inverse), then layer the interactive prompts
   on the `Intervene` seam. `renderConfig` only emits movement vocabulary — that's also F7's
   crux.
3. **Live Docker+TTY verification** — exercise `projection migrate --pretty` against a real
   CDC-tracked sink with a tightening violation, to confirm the gate prompt + board + the
   relax-always persistence end to end. The warm container is up (`scripts/test.sh status`);
   CDC tests use `IsolatedContainerFixture`.

## Gotchas worth knowing

- The relaxation/override discipline is the operator's load-bearing value: **every departure
  from the team's model must be a named, tracked exception, never silent.** That principle
  drove the whole gate. Hold it.
- The audit confirmed the faithful core is clean (nullability/structural passes/default policy
  are all `OperatorIntent`, Policy-gated, skeleton-excluded). The 15 findings are the
  *exceptions*, parked with dispositions.
- F-series numbering: "F7-config-preserve" (make `renderConfig` round-trip
  `tighteningRelaxations`) is distinct from audit "F7" (named-erasure A37 promotion). Both live
  in the audit doc.

Hold the spine. The substrate had a lot of goodness to leverage — it still does.
