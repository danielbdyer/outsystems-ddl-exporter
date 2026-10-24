# Handoff — the Voice chapter, closed. The instrument speaks.

*A forward-looking letter to the agent who picks this up next. The chapter that
`HANDOFF_VOICE_2026_06_06.md` opened is done; this letter orients you on what
that means, what is still open, and how to carry it on.*

---

## To the next agent

You are inheriting a finished thing, which is rarer than it sounds. The brief you
would have read — `HANDOFF_VOICE_2026_06_06.md`, seven waves, "wire the register
into the rendered TTY" — is **complete**, and the dynamic frontier the three
survey agents mapped is substantially closed too. The instrument no longer shows
the operator the apparatus. Every operator-facing surface in the CLI speaks in
one register; the live run renders itself as it happens; the readiness board
reads its timeline in words; the setup reads itself back. The `%A` dumps, the
`REFUSED`/`canary RED` shouts, the `norm=` algebra, the leaked `SsKey` — gone,
and a mechanical sweep confirms it. Run `projection setup --conn env:…` against a
live database and watch it tell you, in plain words, that the connection is
reachable and ALTER is granted. That is the instrument disappearing.

So your job is **not** to finish the voice — it is finished. Your job, if you
touch this surface, is to **hold it**, and to extend it only where a real
operator boundary exists. Read `THE_VOICE.md` before you write a single string;
the register was settled one rule at a time and it is easy to relapse by
instinct (rule 12 stative-agentless and the legibility axiom — algebra out,
domain in — are the two that drift). The locked decisions are in
`HANDOFF_VOICE_2026_06_06.md` Appendix A. Do not re-open them without the operator.

## What shipped (so you trust it on sight)

- **The register, everywhere.** Waves 0–6: the verdicts, the §5 gates (through
  `renderGate` over the closed `Preflight.GateLabel` DU; the intent gate through
  a flat `gate.intent` code), the §6 proofs, the §4 moves, the §10 errors, the
  §13 lifecycle, the §14 config. All route through `Voice` → `Surface` → `View`,
  held honest by the `code ⇔ copy` + gate⇔copy totality tests.
- **The live Watch, wired and streaming.** `Watch.renderWatch` (pre-seeded
  `Pending` arc, the minimum-dwell floor) is on `project` (bundle + load),
  `migrate --execute`, `migrate-with-data`, `transfer --execute`, and
  `project --deploy`. The executors stream the existing stage codes at their real
  phase boundaries (`MigrationRun.execute` → emit/deploy/canary;
  `TransferRun.writePlan` → load), plus **intra-stage progress + an honest
  estimate** (`summary.stageProgress`, `Watch.etaText`/`progressText`,
  `LogSink.recordStageProgress`). Verified *live* against the warm SQL container —
  the migrate streamed `done 1→2→3 of 3`; the data load streamed per-table.
- **The readiness timeline in words** (§8): the verdict dots now carry a plain
  narration ("the last N check(s) · M passed · K diverged · run N, the present
  one"). The ladder ("X green runs to go") was already in the Hero.
- **At scale** (§12): `View.Lane` caps its rendered items with `and N more` (the
  full list survives on `toJson`); `Theme.humane` numerals everywhere.
- **The setup readback** (§14 / Appendix A.6): `projection setup [--conn]`, the
  last fully-latent surface, with the live reachability + ALTER-grant probe.

## What is still open (and what is honestly *not* worth doing)

- **Per-table deploy progress: do not build it.** `project --deploy` is one
  aggregated SQL batch by design (perf) — there is no honest per-table boundary,
  and a counter there would lie. This is settled (`DECISIONS 2026-06-09` #6). The
  honest per-unit progress is the data-load loop, and it is already shipped.
- **The Watch board footer + done-frame** (Appendix A.3's "→ verification
  follows" / "recorded as run 11"). The board renders the stages; it does not yet
  render the run title above or the next-move line below. Pure rendering, modest
  value, a clean small slice if you want it.
- **The ladder's "one lever"** (Appendix A.5's "one item remains: map 3
  accounts"). The survey flagged this as the open question: there is no single
  *source* for the one blocking item. I did **not** fabricate one — the board
  shows the honest "X green runs to go". If a real intervention-ranking surface
  lands, wire the lever then.
- **Setup live-probe breadth.** `--conn` probes reachability + ALTER; the other
  grants (`INSERT`/`CREATE TABLE`) are in the same `GrantEvidence` set if a
  consumer wants them.
- **Wave 6 ecosystem surfaces** (`THE_USE_CASE_ONTOLOGY.md`, the proteins) are a
  different chapter — the engine's target, not the voice's rendering.

## How to work here

- **Read order:** `THE_VOICE.md` (the law, keep it open) → `THE_VOICE_BACKLOG.md`
  Appendix A (the finished surfaces) → this letter → `DECISIONS 2026-06-09` (the
  resolved state). The machinery is `Voice.fs` / `Surface.fs` / `View.fs` /
  `Watch.fs` / `TtyRenderer.fs` / `Comparison.fs`, all in `Projection.Cli`.
- **Disciplines that bind:** codes never change, only copy (the NDJSON contract
  is DO-NOT-BREAK); derive, never invent (every string from a §4/§5/§6/§10/§14
  example); pure-Core holds (no prose in `Projection.Core`); IR grows under
  evidence (voice what an executor emits — and the stage stream now reaches the
  migrate/transfer/deploy executors, so the spine is wide).
- **Running tests — read `DECISIONS 2026-06-09` (Agent test-execution protocol)
  first.** Run focused tests *directly* (`scripts/test.sh focus "<FQN>"`); never
  gate a run behind a `pgrep`/`until` loop (it matches its own command line or
  the persistent MSBuild node-reuse workers and waits forever); don't watch a run
  through a buffering `| tail`. A dead `projection-mssql-warm`
  (`scripts/warm-sql.sh restart`) is the cause of a batch of `SqlException`
  failures while `PROJECTION_MSSQL_CONN_STR` is set — diagnose, don't chase.

The bar was never "the guard passes." It was: would a newcomer trust this on
sight, would a master read it as a glance, and does the instrument disappear
behind it. It does. Hold it there.

— *the outgoing agent, 2026-06-09*
