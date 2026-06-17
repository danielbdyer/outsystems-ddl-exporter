# Handoff addendum — 2026-06-16 (latest), F0c-I/O + F-Faker LANDED — the blessed-correction loop is operator-usable END-TO-END (durable write + `synth-correct` propose verb + `correction: file:<path>` flow wiring threading `Profile ⊕ Correction` into σ AND the Faker realization), and the Faker boundary is now COORDINATE-ADDRESSED and tunable (mask a column / permanent overrides / a wide generator catalog, bound by module-entity-attribute). The honest frontier from here is F3 (coverage) → F6 (distribution fitting) → F4 (rotation)

To the next agent.

**Read `THE_SYNTHETIC_DATA_FUZZING.md` first** (the §7 slice table is the live status; §3 coverage / §6 fitting / §4 rotation own the next moves). This session continued the advanced synthetic-data program with **two slices**, both gated and committed:

**F0c-I/O — the operator surface (commit `99be701f`).** The whole blessed loop is now real end-to-end. (1) The `synth-correct --out <path>` propose verb (`CorrectionProposeRun`, the Pipeline sibling of `ProfileCaptureRun`) resolves the configured model's CATALOG — the proposer types PII by attribute NAME, which the `Profile` does NOT carry — runs `CorrectionProposer.propose` → `CorrectionCodec.serialize` → durable write. (2) The `correction: file:<path>` flow wiring is the A44 compiler-guided cascade: `FlowSource.Synthetic` gains `correction` (its sibling of `profile`; parse∘render); `MovementSpec`/`FlowRunOpts`/`LoadOpts` gain `Correction` (the `--correction` per-run override WINS, else the flow's declared correction); `optsOf` + `resolveFlowSpec` thread it. (3) `SyntheticLoadRun.run` resolves+decodes the artifact (`resolveCorrection`, A39 re-validation), folds `Profile ⊕ Correction` onto the config, AND injects `FakerRealization.realize` between σ and the load. The injection point matters (see the scars).

**F-Faker — coordinate-addressed, tunable Faker (commit `a853f030`).** The operator's "couple Faker in configuration to specific module-entity-attribute locations" ask. An OSSYS attribute `SsKey` is an opaque GUID, so a durable blessed Faker binding addresses by **`AttributeCoordinate (module/entity/attribute)`** — hand-authorable, reviewable — resolved against the catalog at load (a not-found / ambiguous coordinate, and an unresolved coordinate at run start, are NAMED refusals). The wide `FakerGenerator` catalog (person / address / company / internet / lorem / guid / int+decimal ranges / dates) + `MaskRule` (redact / keepLast / keepFirst / hash — format-preserving masking of σ's PRESERVED value) + `Constant` override, locale-tunable. `applyToConfig` routes a fresh-fake generator ⇒ Synthesize (the privacy substrate) and a `Mask` ⇒ Preserve. `FakerRealization.realize` = the F2 PiiKind pass THEN the F-Faker pass (the more-specific Faker wins); person-based generators are referentially consistent per row, fresh-draw generators order-independent. **The operator chose this full design (coordinate-only addressing, the wide catalog, masking) via the opening question — do not narrow it without asking.**

**Your next move — the honest frontier (operator-named order).** **F3** (coverage corrections — `CoverageFloor`: exhaustive permutation / variety injection / distinct-floor — + the **L2-cov** canary; the "ensure all important values are included even where the source is patchy" quality gate; §3). Then **F6** (professional distribution fitting — a `ShapeHint` evidence axis at π: histograms / fitted families / multimodality — + a richer `sampleNumeric` at σ; the largest; §6). Then **F4** (boundary anonymizing rotation — corpus-row permutation that breaks subject↔row linkage; **needs a named threat model first**; §4). Each ships with its faithfulness-ladder witness in the warm Docker pool beside `SyntheticCanaryTests`; every σ change keeps the `π∘σ≈id` canary green (it is byte-identical when its evidence is absent).

**Scars that BIT this session (heed them — the first cost real time):**
- **⚠️ THE WORKTREE-PATH TRAP.** The Bash tool's cwd resets to the worktree ROOT (`...\.claude\worktrees\<name>`), where `sidecar/projection` (relative) is YOUR worktree's projection. But an ABSOLUTE `cd /c/Users/.../outsystems-ddl-exporter/sidecar/projection` (WITHOUT the `.claude/worktrees/<name>` segment) silently lands in the **MAIN repo** — a different checkout at a different commit. I spent time chasing "the fsproj is missing the slice files / the build is green but slice-less" before realizing my greps + a baseline build had run against the main repo (HEAD `99f6ff7a`), not the worktree (HEAD `d6f0d4f0`). **ALWAYS `git rev-parse HEAD` to confirm you're in the worktree; use the FULL worktree path** (`/c/Users/danny/code/outsystems-ddl-exporter/sidecar/projection/.claude/worktrees/<name>/sidecar/projection`). The Read tool with full worktree paths was always correct; only the Bash `cd` drifted.
- **Bogus global-seed footgun.** `Bogus.Randomizer.Seed` is a STATIC global; a `Bogus.Faker()` with no explicit seed draws from it, so constructing a SECOND faker RESETS the stream for the first (its `Person` would materialize under the wrong seed). The fix in `FakerRealization.realizeRow`: materialize a row's `Bogus.Person` (read its fields) for a (row, locale) group BEFORE constructing any other faker; for fresh-draw generators, construct→draw→discard within one `freshOrValue` call. Dates use a FIXED reference anchor (`refDate`), not `DateTime.Now` (Bogus's default), so the realization is reproducible.
- **The realize-injection compile order.** `FakerRealization.fs` (compile index 64) is AFTER `TransferRun.fs` (57), so `Transfer.runSynthetic` CANNOT reference `FakerRealization` — the realization is injected as a pure `rows → rows` parameter from `SyntheticLoadRun.fs` (65). Keep it that way (it also keeps Core σ and TransferRun Faker-agnostic). Default callers pass `id` (byte-identical; the canary's contract).
- **Core `core-sprintf` (sprintf AND String.concat banned).** The coordinate conflict-key uses a `ConflictKey` DU (no concat); the coordinate not-found/ambiguous refusals' `sprintf` carry a per-line `LINT-ALLOW`. Pipeline / Targets / Cli have no such rule (the boundary realization's string work is unmarked, as `FakerRealization` already carries `LINT-ALLOW-FILE`).
- **A large `Edit` can introduce a stray NUL byte** into a string literal (a `seedOfCell` separator I meant as a space landed as `\0`; it compiled and the tests passed, but `file FakerRealization.fs` reported `data` not `text` and `git diff --stat` flagged it **Bin**). The tell is git treating a source file as binary; the fix is `tr '\000' ' ' < f > f.tmp`. After a big edit to a file with multibyte chars, a quick `git diff --numstat` (a `- -` row = binary) catches it.
- `dotnet` not on PATH (`export PATH="$PATH:/c/Users/danny/AppData/Local/Microsoft/dotnet"`); build from the worktree projection; **never the pure + Docker pools as one `dotnet test`** (OOM — `scripts/test.sh fast` then `… focus`/`docker`, sequential); Docker tests soft-skip silently (confirm REAL per-test durations in seconds, never the green count).

**State.** Branch `claude/quirky-leakey-4da6cf` — a worktree reset onto `d6f0d4f0` (the 7-slice base, branch `claude/thirsty-fermat-0612fd` / PR #625, off `main` `48af1895`). Three commits atop: `99be701f` (F0c-I/O) + `a853f030` (F-Faker) + `d95f32e8` (the NUL-strip fix) — **committed, NOT pushed** (the operator drives push / PR). Debug+Release 0/0; pure pool PASS (3426 + the F0c-I/O + F-Faker witnesses); Docker focus `Synthetic` 66/66 incl. `canary: pi of sigma approximates id` (real 3s) + `F-Faker … realizes a column through the actual load` (0.97s); **full Docker pool re-run GREEN (769s, no regression)**. Hold the spine: name every refusal, count every crossing, leave the books balanced.

---

# Handoff addendum — 2026-06-16 (earlier), THE ADVANCED SYNTHETIC-DATA PROGRAM is under way — follow-on D shipped, then the operator named a high-fidelity "fuzzing" program and SEVEN slices landed (F0a/F0b/F1/F0c-propose/F5a/F5b/F2, all gated, PR #625). The next move is F0c-I/O — the operator surface that ties the blessed loop together end-to-end

To the next agent.

**Read `THE_SYNTHETIC_DATA_FUZZING.md` first** (the §7 slice table is the live status). This session did two things: shipped **follow-on D** (the streaming reverse-leg compensating-undo — see the letter below; the migrate envelope is now complete), then the operator pivoted to **"what remains"**, where the headline finding was that **the σ synthetic-data emitter is already BUILT** (`THE_SYNTHETIC_DATA_DESIGN.md`, 2026-06-08) — the Faker deferral row + `HOLDOUT_INVENTORY` #10 are STALE. From that we co-designed an advanced program and built seven slices.

**The spine you're standing on.** Synthesis is now `σ : Profile × Correction ⟶ Data` such that `π∘σ ≈ id MODULO the blessed Correction` — the engine's own core-adjunction shape ("identity, modulo named, closed erasures") applied to the synthesis section. Two planes, kept apart: **π / boundary** (heavy inference + Faker — may use float/RNG) and **pure-Core σ** (T1: no float/RNG/clock; Faker is seeded from σ's deterministic tokens at the boundary so determinism survives). The **blessed correction artifact** is the durable, operator-blessed override/intent hinge (the synthesis sibling of the RefactorLog + the Tolerance registry).

**What landed (PR #625; each independently gated — Debug+Release 0/0, pure pool 0-failed, Docker `π∘σ≈id` canary green throughout):**
- **F0a** `Correction` Core carrier + smart-ctor conflict refusal + `Profile ⊕ Correction` fold (`SyntheticCorrection.fs`).
- **F0b** durable `CorrectionCodec` (round-trip law, A39 decode-refusal) — the artifact persists.
- **F1** per-kind `VolumeTarget` (Absolute/Multiplier) — arbitrary scale, decoupled from the corpus.
- **F0c-propose** pure heuristic `CorrectionProposer` — first-draft PII typing.
- **F5a** σ ← captured `ForeignKeySelectivity` (rank-mapped FK skew).
- **F5b** σ ← captured `JointDistribution` (correlated FK-tuple synthesis, L3).
- **F2** `FakerRealization.realizePii` (Bogus, on Pipeline) — coherent fake person per row, seeded from row identity (referential consistency + determinism); Bogus stays OUTSIDE Core.

**Your next move: F0c-I/O — the operator surface.** Everything above is reachable only programmatically / in tests. F0c-I/O makes the blessed loop real end-to-end: (1) durable WRITE (`CorrectionCodec.serialize` → file); (2) the `synth-correct <profile> --out` propose verb (`CorrectionProposer` → codec → file); (3) `correction: file:<path>` flow wiring threading the blessed `Correction` through the synthetic flow AND calling `FakerRealization.realizePii` between σ and the load. It is the **A44 compiler-guided cascade** — adding a field to `FlowSource`/`FlowRunOpts`/`MovementSpec`/`LoadOpts` ripples through the parse∘render isomorphism + ~16 literal sites; the build is your guide. Witness via `MovementSurfaceTests` (the A44 expressible⇔reachable proof). After F0c-I/O: **F3** (coverage corrections + L2-cov canary — the operator's "ensure all important values are included" ask), **F6** (distribution fitting — a `ShapeHint` π axis + richer `sampleNumeric`; the largest), **F4** (boundary rotation — needs a named threat model).

**Scars that bit this session (heed them):**
- **`dotnet` is NOT on PATH** (bash or PowerShell). Bash: `export PATH="$PATH:/c/Users/danny/AppData/Local/Microsoft/dotnet"`. PowerShell: call `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe` by full path. Build/test from the worktree root (`...\sidecar\projection`) — the `cwd` drifts; use absolute project paths in PowerShell.
- **`lint-discipline.sh --ci` reports a long-standing ~209-site whole-tree baseline** (Run.fs/ReportRun.fs/EventProjection.fs etc. — pre-existing, none from this work). It is NOT the operative gate (that's the pre-commit scoped scan, which isn't installed in this worktree — run `scripts/install-hooks.sh` if you want it). Do **not** chase the baseline; just ensure your NEW files carry markers. **Core has a `core-sprintf` rule** (Targets don't) — a dynamic `ValidationError` message in Core needs a per-line `LINT-ALLOW: <substantive, ≥30-char, "terminal"/"boundary"-token>` (sprintf AND String.concat are both banned in Core). And **`lint | tail; echo $?` captures tail's exit, not lint's** — verify lint via a direct exit capture.
- **Bogus determinism**: `Bogus.Person`'s ctor takes a locale string (not a Randomizer). Seed via the global `Bogus.Randomizer.Seed <- System.Random(seed)` immediately before `Bogus.Faker()`, per row (single-threaded realization → deterministic). `f.Person` is one cached coherent individual.
- **Every σ change must keep the Docker `π∘σ≈id` canary green** — it asserts counts / zero-orphans / categorical-sets (NOT fan-out skew or FK values), and each σ addition (F1/F5a/F5b) is byte-identical when its evidence is absent. Run `scripts/test.sh focus 'Synthetic'` (warm container) after any σ touch.
- **F# backtick test names cannot contain `@`** (FS1104 — "email has @" → "email has an at-sign").

**State.** Branch `claude/thirsty-fermat-0612fd` → PR #625 (D + the 7 synthetic slices). Latest commit `fa15126a` (F2). Debug+Release 0/0; pure pool 3418 passed / 0 failed / 210 standing skips; Docker Synthetic* 55/55 incl. the canary + the new Faker tests. F2's realization is built but **not yet wired into the load** (F0c-I/O). Hold the spine: name every refusal, count every crossing, leave the books balanced — and don't chase the lint baseline.

---

# Handoff addendum — 2026-06-16 (later still), FOLLOW-ON D LANDED — the streaming reverse-leg's compensating-undo (M23's arm on `writePlanStreaming`, the estate-scale path) is built WITH its gate canary; the migrate "we can't go wrong" envelope (M21/M22/M23/M24 + D) is now COMPLETE. The frontier from here is the named moat only — do not build it without a fired trigger

To the next agent.

**D is done, and it taught the instruction set a lesson — carry the lesson forward.** `FOLLOWON_STREAMING_REVERT.md`
told you to hang the compensating-undo off the streaming call site's `Error es` arm, claiming the streaming path
"returns a `Result.failure`, not a throw." **It throws.** Per the `staged` CE (`RunSpine.fs`): a stage body that THROWS
→ `RunAborted (_, Some ex)` → `writePlanStreaming` re-raises (its `ExceptionDispatchInfo.Throw`, ~line 1790); one that
RETURNS `Error e` → `RunStopped e` → the `Result.failure` you see at the call site — and that only ever comes from the
resume **source-drift** refusal. The canary's own crash (a dropped sink column → `SqlBulkCopy` throws) is an exception,
so an `Error`-arm-only revert would have compensated NOTHING. Writing the canary first is what surfaced this. The seam
that shipped: a `try/with` around the `writePlanStreaming` call in `runStreamingReconcilingWithRenames` — a crash
reverts-then-re-raises; a named `Error es` returns WITHOUT reverting (a drift refusal wrote nothing new this run, so a
DELETE-by-captured-key would destroy PRIOR-run committed rows — never do that). If you ever revisit the streaming faces,
**do not "simplify" the revert back onto the `Error` arm.**

**What D is.** `replayJournalToRemap` + `runRevertFromJournal` (new `let private`s beside `buildRevertScript`/`runRevert`
in `TransferRun.fs`) reconstruct the M23 remap from the off-box `CaptureJournal` (the streaming path's durable
sink-minted-key ledger) and run the SAME `buildRevertScript` + `runRevert` the materialized arm runs. `autoRevert`/`revertDir`
thread through `runStreamingReconcilingWithRenames` + `runStreamingReverseLegThroughConnections` (the straight-load
`runStreamingWithRenames` passes inert `false None`); the RunFaces `Streaming` branch now passes the `revertAuto`/`revertOut`
already derived via `RevertPolicy.toEngine`. Both levers inert → byte-identical to pre-D. The gate is two
"streaming data canary (D)" witnesses in `TransferCanaryTests` (a pre-existing sink row proves the revert targets ONLY
captured minted keys).

**What remains is the MOAT, and it is named-not-unfinished.** Read `HOLDOUT_INVENTORY_2026_06_16.md` — the per-feature
trigger-gated study. The only items the study flags as actionable now are **(#10) the Faker / synthetic-data emitter**
(its trigger FIRED ~4 weeks ago — "bring to principal-PO"; the highest-confidence un-cashed trigger), **(#9)
policy-version plane → Episode** (half-fired — M5 landed Wave 4; only the consumer-need remains), and **(#3) the
CDC / OPEN-3 estate survey** (now runnable — J5 lifted the blocker). Everything else is honestly far / infra-gated
(P7b, computed-column S7) / consumer-gated (`Tolerance` config). **Naming a trigger-gated absence IS its completion
here — do not build moat without a fired trigger.** Surface the options to the operator; let them choose.

**Build-discipline scars (unchanged, still bite).** `dotnet` is NOT on the bash/PowerShell PATH on this box (it lives
at `C:\Users\danny\AppData\Local\Microsoft\dotnet`; `export PATH="$PATH:/c/Users/danny/AppData/Local/Microsoft/dotnet"`
before `scripts/test.sh`). Never run the pure + Docker pools as one `dotnet test` (OOM). The warm container had been up
11h this session — a batch of connection / pre-login failures means it died or its memory pool degraded
(`scripts/warm-sql.sh restart`), NOT a regression (two `TransferCanaryTests` showed stale FAILED from a prior session
and were 27/27 green on a fresh focused run). `static let` cannot follow members (FS0960). The streaming `try/with` is
FS3511-safe only because it binds single values (no tuple `let!`, no `let rec` in `task`).

**State.** Branch `claude/thirsty-fermat-0612fd` (a worktree off `main` `48af1895`, the merged PR #624 baseline). D +
its canary + the docs (this letter, the DECISIONS "Follow-on D BUILT" entry, the `FOLLOWON_STREAMING_REVERT.md` status)
sit as WORKING-TREE changes — the operator drives commits (the D code + its canary MUST commit together per the §0 gate).
Debug + **Release** 0/0; pure pool 0 failed (3387 passed / 210 standing skips); **`TransferCanaryTests` 27/27**; lint
clean (27 rules); `matrix-status.sh` gate=PASS, rungs 5/4/5, tolerances 10/3-open UNCHANGED. Hold the spine: name every
refusal, count every crossing, leave the books balanced — and never ship a destructive op untested.

---

# Handoff addendum — 2026-06-16 (later still), M24 LANDED — the migrate dispositions are now CONFIG-BASED (sensible defaults, not command clutter): atomic derives ON for direct+FullRights; revert is a per-environment policy. Follow-on C done (atomic on migrate-with-data). Follow-on D (streaming compensating-undo) DEFERRED behind a canary gate

To the next agent.

**The flags became config dispositions (operator review: "sensible defaults, not clutter, config-based").** Commit
`9ebb0989` + this follow-on re-homed M22/M23's bare flags into the A44 control plane:
- **`--atomic` is retired as an opt-in.** Atomic now DERIVES ON for a `direct`+`FullRights` sink (the common local
  `migrate` needs no flag), inert otherwise. Opt out via env `"atomicDeploy": false` or per-run `--no-atomic`. Derived
  in `resolveFlowSpec` from access + archetype.
- **`revert` is a per-environment policy** (`"revert": script|auto|off`; default `script` = emit the revert .sql, never
  auto-delete). `--auto-revert` forces `auto`; `--revert-dir` overrides the dir. `RevertPolicy.toEngine` collapses it to
  the engine's `(autoRevert, revertArtifactDir)` at the face.
- New `Environment` fields `atomicDeploy`/`revert` round-trip through the A44 parse∘render isomorphism (the env
  `MovementIsomorphismTests` covers them). Witnesses: three new `MovementSurfaceTests`.
- **Follow-on C — DONE.** `migrate --with-data`'s schema leg honors the derived atomic (`executeWithData*` gained an
  `atomic` param). The with-data DATA-leg revert is a small further follow-on (it routes through
  `Transfer.runWithRenamesWith`, not the ThroughConnections faces).

**Follow-on D — DEFERRED behind a HARD canary gate (do not skip it).** The streaming reverse-leg's M23 arm
(`writePlanStreaming`, the estate-scale hundreds-of-millions-row path) is mechanically feasible: at the streaming
`Error` branch (`TransferRun.fs` ~1968) replay the `CaptureJournal` into a `PackedSurrogateRemap` (the journal's `spec`
fold; map `record.Kind` root → SsKey via the catalog), call the existing `buildRevertScript` + `runRevert`, and add
`autoRevert`/`revertDir` params to `runStreamingReverseLegThroughConnections` + its delegators + the RunFaces streaming
call. **It MUST ship with a deterministic streaming-failure canary** — a wrong DELETE-by-captured-key at estate scale is
unrecoverable, so the engine's "every correctness claim a property test" discipline forbids shipping it untested. I
declined to ship it untested at the tail of this session. Streaming retains its journal-resume safety meanwhile; the
materialized transfer + both schema legs carry full M22/M23/M24 compensation. **The pickup-cold instruction set is
`FOLLOWON_STREAMING_REVERT.md`** — exact seams, the journal-replay helper, and the canary the gate requires.

**State.** Branch `claude/vector-wave-4-5`. Config redesign committed (`9ebb0989`); follow-on C is working-tree
(uncommitted — about to commit). Debug+Release 0/0; pure pool PASS; matrix unaffected. Hold the spine.

---

# Handoff addendum — 2026-06-16 (later still), M22 + M23 + the CLI flag-surfacing LANDED — the migrate "we can't go wrong" envelope, scoped to the real topology: the Atomic `BEGIN TRAN` (`--atomic`) is a LOCAL full-access lever (production schema is ADO/Octopus/SSDT, not direct-connect); the data leg got M21's twin (`--auto-revert`, else emit the precise revert script). Both opt-in, both default-off, both canary-green, and both now wired through the A44 control plane (operator-usable + witness-tested)

To the next agent.

**Two builds landed this session (engines tested; one mechanical step remains).** The operator drove `migrate A B`
toward promise 8 across both legs, and a mid-session topology correction re-scoped the Atomic work — heed it.

**M22 — the Atomic `BEGIN TRAN` envelope (opt-in `--atomic`, LOCAL full-access only).** `MigrationRun.executeWith
(atomic) …` wraps the schema deploy in `SET XACT_ABORT ON; BEGIN TRAN … COMMIT/ROLLBACK`; on failure it rolls back the
whole deploy and reuses M21's `compensateToSource` to verify A and supply the verdict (M21 is the fallback). The §10
capability conjunct was validated by the operator's probe (`ATOMIC_ENVELOPE_VALIDATION.md` §4: `ALTER ANY SCHEMA`,
transactional DDL, ROLLBACK reverts). **TOPOLOGY (do not lose this):** production on-prem schema ships as SSDT material
via **ADO branches → pipelines → Octopus** — the engine does NOT direct-connect to deploy production schema, so the
giant-TRAN is **not** a production path. `--atomic` is a **local full-access** lever (dev/test; the `FullRights`
archetype; the warm container is exactly this). For production the atomicity concern is DATA-only. Witness: the
`MigrationCanaryTests` M22 canaries — the part-way ALTER that M21 could only NAME as a residual now rolls back CLEANLY
under `--atomic`. `execute = executeWith false`, so non-atomic is byte-identical.

**M23 — the data-leg compensating-undo (opt-in `--auto-revert`, else emit the revert script).** `TransferRun.writePlan`'s
failure path now builds a child-first `DELETE`-by-captured-key revert (`buildRevertScript` over the new
`PackedSurrogateRemap.assignedKeysByKind`). `WriteOptions.AutoRevert = true` executes it; `false` (default) writes the
precise revert SQL to `RevertArtifactDir/transfer-revert.sql` for the operator — the operator's exact spec. Only
sink-minted keys are targeted (pre-existing rows untouched); the original failure still propagates. This is the
J5-evidence channel for the managed cloud + production data. Witness: two `TransferCanaryTests` Build A canaries
(auto-revert OFF → script written, rows kept; ON → rows deleted). Both `WriteOptions` fields default-off, so the whole
transfer subsystem is byte-identical for existing callers (TransferCanaryTests 25/25).

**The CLI flag-surfacing — LANDED this session (the A44 control-plane wiring).** `--atomic`, `--auto-revert`, and
`--revert-dir` are now operator-usable end-to-end. The fields thread through the full control plane: parsed at
`MovementSurface.fs` `Command.parse` (`List.contains "--atomic"` etc.) → `FlowRunOpts` → `resolveFlowSpec` →
`MovementSpec` → `optsOf` → `LoadOpts` → the run faces (`runMigrateExecute` gained an `atomic` param;
`runTransfer`/`runReverseLegTransfer` gained `autoRevert`/`revertDir`) → the engine (`MigrationRun.executeWith`;
`Transfer.run*ThroughConnectionsWith` → `WriteOptions.AutoRevert`/`RevertArtifactDir`). Witnessed by three new
`MovementSurfaceTests` (`--atomic` threads onto the migrate `LoadOpts`; default-off; `--auto-revert`/`--revert-dir`
thread onto the transfer `LoadOpts`) — the A44 expressible⇔reachable proof. All defaults are off (byte-identical).
The `Program.fs` usage help lists them. **Remaining (one named follow-on):** the STREAMING reverse-leg
(`writePlanStreaming`) does not yet carry M23's compensating arm (it resumes via journal); `--auto-revert` is wired to
the materialized transfer + reverse-leg paths (where `writePlan` lives), and the streaming entry is intentionally
untouched. Also un-wired by choice: `--atomic` on `migrate --with-data` (the composed local schema+data case — the
schema leg there can take `--atomic` as a small follow-on).

**Build-discipline scars (this session).** `dotnet` not on bash PATH (`export PATH=…/dotnet`). **`static let` cannot
follow members** (FS0960) — the new canary catalogs are local `let`s inside their members. Adding a `MigrationError`
case cascades to `Voice.migrationStopDetail` + `RunFaces.reportMigrationError` + the Voice totality sample. Threading a
field through `WriteOptions` cascades to `writePlan`/`writePlanResumable`/`runCore`/`runSynthetic` — the single-assembly
warnings-as-errors build is the backstop. Server-side `BEGIN TRAN` via `executeBatch` works for the schema leg; a
client `SqlTransaction` would be needed to span schema+data in one transaction (deliberately NOT taken — see M22).

**State.** Branch `claude/vector-wave-4-5` (M21+M22+M23 are working-tree atop Wave-4/5 — operator drives commits).
Debug + **Release** 0/0 (FS3511-safe); pure pool PASS; **Docker `MigrationCanaryTests` (M21+M22) + `TransferCanaryTests`
(M23) all green** on `projection-mssql-warm`; `matrix-status.sh` gate=PASS, **rungs 5/4/5 + tolerances 10/3-open
UNCHANGED** (M21/M22/M23 strengthened the T-VI footer (a), no ladder cell moved). `ATOMIC_ENVELOPE_VALIDATION.md` holds
the giant-TRAN trigger's resolution protocol (capability conjunct PASS; P7b + cost probes pending). Hold the spine.

---

# Handoff addendum — 2026-06-16 (later), M21 LANDED — the `migrate A B` failure path now rides M12's groupoid inverse: a mid-deploy crash rolls back to A or names the residual, never a silent partial ("refuses without damage" made executable). The Atomic giant-`BEGIN TRAN` and the permissions-content axis stay §10 deferred — their triggers have not fired

To the next agent.

**What landed (M21 — operator-authorized, disciplined-composition path).** The operator asked to take `migrate A B`
to the covenant's promise 8 ("atomic-or-resumable, refusing rather than corrupting") and chose — explicitly, via the
opening question — the **disciplined composition** over firing the §10 moat triggers. So the one genuinely-missing arm
of the migrate envelope is now built: the **compensating-undo rollback**. Before M21, `MigrationRun.execute`'s deploy
stage caught a mid-deploy exception and returned a bare `ExecutionFailed`, leaving partial DDL applied. Now
`compensateToSource` reads the live partial state `B''`, realizes the **rename channel** of M12's inverse
(`CatalogDiff.between B'' A` — data-preserving, metadata-only, always-invertible), and re-verifies against A: clean →
`ExecutionRolledBack`; a non-rename residual it must not auto-invert → `PartialWriteUnrecovered (residual)`, **named**
for the operator. Two new `MigrationError` cases; explicit loud CLI arms (exit 9, no silent downgrade); the Voice +
the totality test carry both. Read the 2026-06-16 (later) "M21" DECISIONS entry — it is the substance; this points.

**What this is NOT (heed the boundary — do not erase it).** M21 is **not** the Atomic giant `BEGIN TRAN` envelope.
That wrapper — which would make the failed-deploy state unreachable by wrapping the whole `deploy` in one transaction —
stays **§10 deferred**, trigger un-fired: it is gated on the managed-login long-transaction grant survey + the open P7b
throughput (a giant transaction over the estate holds schema-mod locks for the whole window and is CDC-hostile). The J5
managed-env evidence (ROLLBACK clean, but DML-only + AssignedBySink + cleanup-by-captured-key) points to the
**compensating channel M21 builds**, not the giant transaction. If you ever build the Atomic envelope, M21 becomes its
fallback for the managed-login-denied case — do not delete it. Likewise the **permissions-content axis** stays moat
(fires only at the eject — a flow that publishes grants). Building either without its trigger violates "IR grows under
evidence".

**If you have appetite.** The honest next reaches are still the named moat (`THE_VECTOR_EXECUTION_KICKOFF.md` §10) —
and only when a trigger fires. The nearest non-moat deepening M21 opens: if you want the `PartialWriteUnrecovered`
arm's *deterministic* witness (today the second canary is robust-to-batch-semantics, asserting only the invariant), a
focused test that forces a guaranteed-persistent non-rename residual would pin which arm SQL Server's batch-abort takes
— a small, real strengthening, not a new dimension.

**Build-discipline scars (unchanged, still bite).** `dotnet` is NOT on the bash PATH (`export
PATH="$PATH:/c/Users/danny/AppData/Local/Microsoft/dotnet"` before `scripts/test.sh`). F# 9 nullness on. **`static let`
cannot follow members in a type** — the M21 canary catalogs are local `let`s inside their test members for this reason
(FS0960). Adding a `MigrationError` DU case cascades to `Voice.migrationStopDetail` (exhaustive) + the CLI report arm +
the Voice totality sample list — the single-assembly warnings-as-errors build is the backstop.

**State.** Branch `claude/vector-wave-4-5` (M21 sits as working-tree changes atop the Wave-4/5 changes — the operator
drives commits). Debug + **Release** 0/0; pure pool `AxiomTests` + `VoiceTotalityTests` green (M16 citation gate green
on the two new T13 citations); **Docker `MigrationCanaryTests`/`SchemaMigrationCanaryTests` 23/23** (2 new M21 canaries
+ 21 unregressed) on `projection-mssql-warm`; `matrix-status.sh` gate=PASS, **rungs 5/4/5 + tolerances 10/3-open
UNCHANGED** (M21 strengthened the T-VI footer (a) line, no ladder cell moved). Hold the spine: name every refusal,
count every crossing, leave the books balanced.

---

# Handoff addendum — 2026-06-16, THE VECTOR WAVE 5 LANDED — **THE VECTOR IS COMPLETE** (the last open fidelity adjudication closed: authored-attribute identity now round-trips at the attribute grain; transactionality + permissions honestly named against the J5 evidence; the totality synthesis written); the only remaining frontier is the moat, which stays cut with named triggers

To the next agent.

**THE VECTOR is complete.** The defined four-wave plan (Waves 0–4) plus the operator-authorized fifth wave (the
residual OPEN fidelity adjudications) are all cashed. What remains is the **moat** — and the moat is *named, not
unfinished*: each item is cut with a re-open trigger (`THE_VECTOR_EXECUTION_KICKOFF.md` §10). Per the engine's
own discipline, naming a trigger-gated absence IS its completion. Read `THE_VECTOR_SYNTHESIS.md` for what the whole
program accomplished and why it matters *per se*, and the 2026-06-16 "THE VECTOR Wave 5 BUILT" DECISIONS entry for
the substance.

**What Wave 5 closed.**
1. **The ReadSide authored-*attribute* round-trip** (T-V/T-I — the treatise's single explicitly-open fidelity
   question). Adjudicated (it failed — attributes were SYNTHESIZED `READSIDE_ATTR` from physical coordinates, so an
   authored column rename landed in `Removed + Added`), then **fixed per the treatise's prescription, not the
   echo-chamber's**: per-column `Projection.SsKey` emission (the attribute-grain sibling of the table-level kind
   SsKey) + `recoverAttributeSsKey` threaded through `buildAttribute` AND `buildReference`. Goldens re-blessed
   (+700 additive lines, all column `Projection.SsKey`). An authored column rename now round-trips as `Renamed`.
2. **Transactionality** (T-VI) — the deferral trigger fired and points AWAY from a giant `BEGIN TRAN`: the J5
   managed-env evidence (DML-only, AssignedBySink, rollback = DELETE-by-captured-key) favors the compensating-undo
   channel (M12's `inverse`); the giant-transaction-over-estate is gated on the still-open P7b throughput. Named in
   the matrix footer; **no speculative build** (the correct outcome).
3. **Permissions** (T-VI) — named honestly in the **matrix footer**, NOT as a round-trip `ToleratedDivergence` (a
   category error — permissions is gated by the A2 pre-flight but not a projected/diffed axis). The full axis fires
   only at the eject (publish grants) — moat.

**If you have appetite, the honest frontier is the MOAT** — and only when a trigger fires: a second `Ingest`
source (DACPAC reader), a branching pass (`LineageTree` consumer), a flow that publishes grants (full permissions
axis), a container pool (Docker N≥20 real-wire sweep), or a real divergence between the four move enumerations
(`SchemaMove` unification). Building any of these *without* its trigger violates "IR grows under evidence" and
dilutes the closed set of falsifiable cells the whole program strengthened. Don't.

**Build-discipline scars (heed them).** `dotnet` is NOT on the bash PATH (`export
PATH="$PATH:/c/Users/danny/AppData/Local/Microsoft/dotnet"` before `scripts/test.sh`). F# 9 nullness is on. A
`bool×bool`→DU collapse keeps the wire byte-identical by projecting to the legacy pair at the codec boundary (the
`IndexUniqueness` precedent — M4). Adding an identity extended property re-blesses the goldens (`GOLDEN_RECORD=1` +
a DECISIONS note); confirm the `git diff` is additive-only before blessing.

**State.** Branch `claude/vector-wave-4-5` (off `main` `6c28130f`). Waves 4 + 5 sit as working-tree changes (not
yet committed — the operator drives commits). Debug + **Release** 0/0; **pure pool 3631/0**; `AxiomTests` 79/0;
`matrix-status.sh` gate=PASS (5/4/5, 10 tolerances, 3 open — footer extended with the two T-VI dimension names, no
ladder cell moved); `verifiability-gate.sh` exit 0; **Docker pool 247/247** (246 + the new attribute-recovery canary, 0 skipped;
no canary regressed from the new column-level emit). Hold the spine: name every refusal, count every crossing,
leave the books balanced.

---

# Handoff addendum — 2026-06-16, THE VECTOR WAVE 4 LANDED (the provenance ledger has its machine lens, the digest is determinism-by-construction, the constraint-trust quadrant is a type theorem — THE VECTOR's defined four-wave plan is fully cashed); your job is WAVE 5 — the residual OPEN fidelity adjudications (operator-authorized, past the defined plan)

To the next agent.

**Where you are.** THE VECTOR's defined plan (`THE_VECTOR.md` §7, Waves 0–4) is **fully cashed** modulo the
honestly-deferred M14. Wave 4 landed M18 (`ChangeManifest.toJson` — the CDC-norm queryable), M5 (the policy digest
is determinism-by-construction, no `sprintf "%A"`), and M4 (the `ConstraintState` DU — the illegal trust quadrant
is now *unrepresentable*, a type theorem; wire byte-identical via the `IndexUniqueness` projection precedent, so no
store migration). The persisted-state-evolution discipline is codified (NM-34 = the store-codec contract; the
serialized-form gating checklist). Read the 2026-06-16 "THE VECTOR Wave 4 BUILT" DECISIONS entry — it is the
substance; this letter points.

**Your mission (Wave 5 — operator-authorized, past the defined plan).** The operator chose to continue past the
four-wave plan to **close the residual OPEN fidelity adjudications** the treatise left dangling — the items that
genuinely close an L-rung toward the full fidelity algebra, as distinct from the moat (which STAYS CUT with named
triggers — do not build it). Three buildable items:
1. **The ReadSide authored-schema *attribute* round-trip adjudication** (T-V/T-I — the treatise's single explicitly
   -open fidelity question; §3.3 / §5.1 / Appendix B). `recoverKindSsKey` recovers authored *kind* identity via the
   `V2.SsKey` extended property, but **attributes** are not covered — so on a V2-authored source an attribute rename
   may land in `Removed + Added` rather than `Renamed`. RESOLVE IT: build a witness that determines whether authored
   -attribute round-trip holds today; **if it fails**, the fix is per-attribute `V2.SsKey` extended-property emission
   + recovery (`recoverAttributeSsKey`) — **NOT** changing the flat-synthesis fallback (that was the echo-chamber
   regression the adversarial layer killed). If it already holds, name it and close the adjudication.
2. **Transactionality completion** (T-VI). Wave 2 landed M20 (the `GateLabel.MidWriteNotProtected` naming) + M12
   (the groupoid `inverse` — the compensating-undo prerequisite). The live atomic `BEGIN TRAN` wrapper was deferred
   on "the managed-login grant survey resolves" — **check whether the J5 managed-env ledger fired that trigger**
   (`J5_MANAGED_ENV_CAPABILITY_PLAYBOOK.md` + the j5 memory). If fired, build the atomic-or-compensating arm on
   M12's inverse; if not, name it honestly-deferred with the precondition.
3. **Permissions interim honesty** (T-VI). Add the `PermissionsGatedNotProjected` tolerance (the sixth matrix row at
   L2-partial) — the honest-naming half the treatise endorses (§5.1), so the A2 pre-flight gate's existence stops
   making a whole dimension *look* closed. NOT the full permissions axis (that needs the eject — moat).

Then the operator wants **the totality synthesis**: what the whole Vector backlog accomplishes and why it matters
per se. Write it as the capstone.

**The moat stays cut (do NOT build).** DACPAC reader (needs a 2nd catalog source), `LineageTree` consumer (needs a
branching pass), full Permissions axis (needs the eject), Docker N≥20 real-wire sweep (needs a container pool), the
full `SchemaMove` unification (no fired divergence). Building these violates the stated intent; naming them IS the
completion. See `THE_VECTOR_EXECUTION_KICKOFF.md` §10.

**Build-discipline scars (heed them — they bit this session).**
1. **`dotnet` is NOT on the bash PATH** — `scripts/test.sh` fails its build step unless you `export
   PATH="$PATH:/c/Users/danny/AppData/Local/Microsoft/dotnet"` first (or build via PowerShell with the full path
   `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe`).
2. **F# 9 NULLNESS is on** — `JsonNode` indexers yield `JsonNode | null`; narrow with a match before `.GetValue`,
   and assign JSON null per-branch to the nullable setter (don't unify a `null` into a non-null `JsonNode` value).
3. **Closed-record field removal cascades widely** — M4's `(HasDbConstraint, IsConstraintTrusted)` → `ConstraintState`
   touched ~22 test files. The single-assembly build (FS-warnings-as-errors) is the completeness backstop: build,
   fix every flagged site, rebuild. Watch for **DU case-name collisions** (M4's `TrustedConstraint` collided with
   `ProbeOutcome.TrustedConstraint` → `[<RequireQualifiedAccess>]` resolved it).
4. **A `bool×bool`→DU collapse keeps the wire byte-identical** by projecting to the legacy boolean pair at the codec
   boundary (the `IndexUniqueness` precedent) — that is why M4 needed no store migration and the goldens stayed green.

**State.** Branch `claude/vector-wave-4-5` (off `main` `6c28130f` = the merged Wave-3 PR #622). Wave 4 sits as
working-tree changes (not yet committed — the operator drives commits). Debug + **Release** 0/0; **pure pool
3629/0**; `AxiomTests` 79/0; `matrix-status.sh` gate=PASS byte-identical (5/4/5, 10 tolerances, 3 open);
`verifiability-gate.sh` exit 0; **Docker pool 246/246**. Hold the spine: name every refusal, count every crossing,
leave the books balanced.

---

# Handoff addendum — 2026-06-15, THE VECTOR WAVE 3 LANDED (the engine is measurably smaller — the descriptor is data, the JSON dance has one home, the binding algebra is a CE, the totality proof is one functor); your job is WAVE 4 — the corollary cashes & the gated deepenings (the operator payoff)

To the next agent.

**Your mission.** Build **Wave 4** of `THE_VECTOR.md` — the last wave: **corollary cashes & gated deepenings**
(§6 corollaries + Kind II/IV; §7 Wave 4). This wave is DIFFERENT from 2–3: it is smaller, and several moves are
**gated behind a prerequisite or a compile-order fix**, so the order matters more than the parallelism. Read
`THE_VECTOR.md` §6 (M18/M14/M4/M5 + "the cross-cutting prerequisite") + §7 Wave 4 first.

**The moves, in dependency order (NOT all parallel).**
1. **The cross-cutting prerequisite FIRST — the persisted-state-evolution discipline** (§6 "the cross-cutting
   prerequisite"). Before M4/M5 touch any serialized form, write it down: name **NM-34** (a missing field reads as
   `None`) as the de-facto store-codec contract; check whether the journal/episode formats carry a version stamp;
   make "this change touches a serialized form" a gating checklist item. This is a DECISIONS-codification move, not
   code — but it GATES M4 and M5. Do it first so they can proceed honestly.
2. **M18 — `ChangeManifest.toJson`** (§5.2, "the lowest-risk corollary on the board"). `ReportRun` has `render` (human
   prose) and no `toJson`; the CDC-capture-count data norm (T15) is a real measured value flowing to `ReportRun.render`
   as prose only. The typed total `ChangeManifest` already exists and is sorted-for-T1; only the second lens (machine)
   is missing. Add `toJson` so the SSIS consumer can diff the change-manifest sprint-over-sprint. Own surface
   (`ChangeManifest` / `ReportRun`). **Independent — do this one regardless; it needs no prerequisite.**
3. **M5 — retire `VersionedPolicy.digestOf`'s `sprintf "%A"`** (§6 Kind II). Replace the F# structural-printer digest
   (determinism-by-luck) with an explicit length-prefixed token projection (the `TransformRegistry.digest` discipline
   next door is the precedent). **Becomes a PREREQUISITE the moment the digest is persisted into episodes** — so
   sequence it under the persisted-state discipline (step 1). Own surface (`VersionedPolicy`).
4. **M4 — `ConstraintState` DU** (§6 Kind II, **top-scored, tied with M1**). Collapse the `(HasDbConstraint,
   IsConstraintTrusted)` `bool × bool` quadrant into a 3-variant `ConstraintState` DU
   (`NoConstraint | Trusted | Untrusted`), eliminating the illegal `(false, true)` state. **It CHANGES THE CATALOG
   CODEC**, so it sequences BEHIND the store-migration story (step 1) — the `CapabilityProfile.of` archetype work
   (closed DU → one total expansion site → derived projection → round-trip law → reconciliation finding) is the
   in-repo precedent to copy. The largest move of the wave; treat it like the M1 keystone (its own care).
5. **M14 — the `Traversal` optic — GATED, do NOT schedule until the compile-order split is resolved** (§4.2/§6
   Kind IV). It unifies the single-focus `Lens` with the hand-rolled `Catalog.mapKinds`/`CatalogTraversal.mapKinds`
   bulk-map, but the duplication's named cause is a compile-order split; fix that first or leave M14 deferred (record
   the deferral honestly — "not yet, and here is the trigger" is the discipline).

**How to drive it.** M18 + the persisted-state discipline + M5 are a clean small workflow (or just inline — they're
modest). M4 deserves its own careful pass (codec change + the round-trip law + the reconciliation finding), like a
mini-keystone. M14 stays deferred behind its gate. **If you Workflow it, heed the orchestration lesson below.**

**⚠️ ORCHESTRATION LESSON (cost real integration time in Wave 3 — heed it).** The Workflow's worktree-isolated
implementers branch off the **session-start commit**, NOT your advancing branch HEAD. In a multi-wave session this
means **later waves' worktrees lack earlier waves' commits.** Consequences: (1) `git cherry-pick` still works (it
applies each move's OWN delta, valid against its base), so integration is fine — but where a move touches a file an
earlier wave modified, expect a 3-way merge to resolve (combine both); (2) the **adversarial verifiers' base is
wrong** if you pass them your current HEAD — they will diff against a non-ancestor and raise FALSE blockers that are
really the earlier wave's ABSENCE from the implementer's base (Wave 3's M7 verifier "rejected" the move for
"deleting `inverse`/M11/M12" that were simply not in M7's `dda83a8f` base). **Mitigations:** pass the verifier the
implementers' REAL base (the session-start commit, e.g. `git rev-parse <branch>^`), or have it self-detect via
`git merge-base`; and always confirm a "rejection" against the move's REAL delta (`git diff <realBase>..<branch>`)
before believing it. Cherry-pick + the full build/pools are the true safety net.

**What is DONE — do not redo (Waves 0–3, all in this PR).** Wave 0 (honesty). Wave 1 (the keystone M1 — Decision is
EARNED ✅ L3). Wave 2 (the reversible algebra: M13 total `between` · M11 metric · M12 groupoid inverse · M3 swept
T16 · M20 transactionality naming · the M11/M12 axiom witnesses · the option-C re-trust capability-descent fix).
Wave 3 (compression: M7 `ChannelSpec`+`Changed→Reshaped` · M8 `JsonWriting` seam · M9 binding `validation` CE · M17
totality functor · M6 `[<Struct>]` keys · M19 `[<Measure>] row`). Read the 2026-06-15 "THE VECTOR Wave 3 BUILT"
DECISIONS entry — it is the substance; this letter points.

**Matrix on this PR.** gate=PASS · rungs L1/L2/L3 = 5/4/5 · tolerances 10 (3 open Schema OpenGaps + the option-C
`FkTrustNotRestoredOnBulkLoad` AcceptedFaithful). Waves 2–3 moved no tolerance and renamed no witness, so the matrix
is byte-identical to its post-Wave-1 state — the under-claim holds. **M4 is the first Wave-4 move that COULD move a
cell** (it touches the trust quadrant the Decision axis depends on); regenerate the matrix carefully after it.

**Build-discipline scars (unchanged — heed them).**
1. **⚠️ Docker tests SILENTLY NO-OP** unless `$env:PROJECTION_MSSQL_CONN_STR=
   "Server=localhost,11433;User Id=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False"`
   (the warm container). Confirm a **real per-test duration in seconds** via the TRX, never the green count.
2. **Run bash scripts from the WORKTREE** (the Bash tool resets cwd to the worktree root). `dotnet` is at
   `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe` (9.0.314; not on bash PATH). Build **Release** too
   (FS3511). Never the pure + Docker pools in one `dotnet test` (OOM) — `test.sh fast` / `docker`.
3. **Run the FULL Docker pool (246/246 is the bar), not just `~Canary`** — a comparison-primitive change ripples
   through every canary (it caught the pre-existing option-C reverse-leg bug in Wave 2). On any `Failed: N>0`, read
   the TRX, and decide regression-vs-pre-existing by running the one test on the base.
4. **Closed-DU/closed-record edits cascade — grep first, fix all sites, build once** (the single-assembly build is
   your completeness backstop). M4's `ConstraintState` is exactly such a cascade (and a CODEC change — the goldens +
   the store round-trip tests are the guard).

**State.** Branch `claude/romantic-bohr-e270dd`; Waves 0–3 sit on this branch as fourteen commits atop `main`
`dda83a8f`. Debug + **Release** 0/0; pure pool green; **Docker 246/246**; `AxiomTests` 79/0; `matrix-status.sh`
gate=PASS (byte-identical); `verifiability-gate.sh` exit 0. After Wave 4, the §7 vector is fully cashed (modulo the
honestly-deferred M14 + the moat). Chapter-close and extend the same PR (or open the Wave-4 PR atop it). Hold the
spine: name every refusal, count every crossing, leave the books balanced.

---

# Handoff addendum — 2026-06-15, THE VECTOR WAVE 2 LANDED (the reversible algebra is complete, the change-algebra round-trip is property-witnessed, the destructive failure mode is named); your job is WAVE 3 — the widest fan-out, again as a WORKFLOW

To the next agent.

**Your mission.** Build **Wave 3** of `THE_VECTOR.md` — the **compression & idiom** wave (§6 Kind III/V + the
corollaries; §7 Wave 3). Seven moves, mostly file-disjoint — the **widest fan-out** of the program. Drive it
as a `Workflow` exactly as Waves 2 did (worktree-isolated implementers · one default-skeptical adversarial
verifier per move · one integrator who alone owns the build / matrix / gates). Read `THE_VECTOR.md` §6 (the
move catalog) + §7 (Wave 3) first; the moves below are the partition.

**The seven moves and how they partition (the workflow's shape).**
1. **M7 — the diff `ChannelSpec`** + **the `Changed → Reshaped` rename — ONE serialized implementer** (both live
   in `CatalogDiff.fs` / the `ChannelDiff` channel structure; they conflict if split, like Wave 2's trio). M7
   reifies the kind-scoped diff channel as a value `ChannelSpec<'entity,'facet> = { entitiesOf; keyOf; nameOf;
   changedFacets; mkChange; applyFacet }` and collapses the four `*Diff` builders + four `apply*Diff` patchers
   into one fold per direction (the source comment "mirrors `attributeDiff` EXACTLY" is the fired trigger). The
   rename is the zero-risk `ChannelDiff.Changed → Reshaped` field rename so the diff and migration layers speak
   one word — a closed-record/field cascade: `grep -rn "\.Changed" src tests` near `ChannelDiff`/`*Diff` first.
   **Heads-up: `CatalogDiff.fs` was heavily rewritten by Wave 2 (M11/M12/M13) — `between` is now total, and
   `inverse`/the M11/M12 tests are new. Read its current shape before you touch it.**
2. **M8 — the `JsonDocumentWriter` seam.** One pair of helpers (`writeToNode : (Utf8JsonWriter -> unit) -> JsonNode`
   + a node→indented-string renderer) retires the `Utf8JsonWriter → MemoryStream → byte[] → JsonNode.Parse`
   dance duplicated across `JsonEmitter` / `DistributionsEmitter` / `CatalogCodec` / `ProfileCodec` (≥4 sites).
   Own surface. Parallel.
3. **M9 — the binding algebra.** FsToolkit `validation { }` at the nine `*Binding` sites; collapse the three
   bind-convergences (the 4-Result tuple in `runWithConfigCore`, the 3-Result tuples in the two shaped-catalog
   runners) into one `bind-all`, structurally closing the model-read-vs-live `applyModuleFilter` divergence.
   Own surface (`Projection.Pipeline` bindings). Parallel. **The riskiest seam — read `THE_CONFIG_CONTROL_PLANE.md`
   §6 first.**
4. **M17 — the totality-test functor.** The four structurally-identical totality tests
   (`CapabilitySurvey`/`Voice`/`Transform`/`ManifestPredicate`) → one parameterized module + four ~10-line
   instantiations. Own surface (test project). Parallel.
5. **M6 — `[<Struct>]` (+ optional smart ctor) on `SourceKey`/`AssignedKey`.** One-word copy over the string ref
   (identical to `RowQuantum`'s rationale); removes per-row DU-wrapper allocation on the FK re-point loop.
   **measure-then-promote** per the `CONSTELLATION §9.7` ritual — ship the bench rationale, not just the
   attribute. Own surface. Parallel.
6. **M19 — `[<Measure>] row` on the data-norm deltas** (`cdcDelta`/`RowCountDeltas`/`NullCountDeltas`), the
   natural sibling of the shipped `[<Measure>] ms`. Own surface. Parallel.

So: **2 implementers for the M7+rename pair and M9 (the two with cascades), the rest highly parallel** → one
adversarial verifier each → one integrator. Have implementers **commit to their worktree branch** and integrate
via **`git cherry-pick`** — NOT text-diff-apply (the Windows trailing-newline trap). Wave 2's integrator
cherry-picked four branches cleanly; the only reconcile was a one-line helper (M3 funnelled `between` through a
single helper so the M13 signature change was a one-line fix). Ask your implementers to do the same wherever a
move couples to another's surface.

**What is DONE — do not redo (Waves 0 + 1 + 2, all in this PR).** Wave 0 (honesty: M1′+M2+M16+M15). Wave 1
(the keystone M1 — Decision is an EARNED ✅ L3). Wave 2 (this letter's predecessor): **M13** (`CatalogDiff.between`
is now total, the `Result` dropped — 13 src + ~120 test sites adapted); **M11** (the triangle inequality → the
norm is a proven *metric*); **M12** (the groupoid inverse + the rollback witness + the groupoid law); **M3**
(`genCatalogPair` + the swept T16/no-cheat/norm properties — round-trip is now property-witnessed); **M20**
(`GateLabel.MidWriteNotProtected` + the pure `transactionalityViolations` gate — the live atomic wrapper stays
deferred). M11/M12 earned their `AxiomTests` T13/T15 witnesses. Read the 2026-06-15 "THE VECTOR Wave 2 BUILT"
DECISIONS entry — it is the substance; this letter points.

**One bug Wave 2's full-pool run surfaced and FIXED (do not be surprised).** The trio change to `CatalogDiff`
is a comparison primitive, so the integrator ran the **full** Docker pool (246), not just `~Canary` — and it
caught a **pre-existing option-C bug** (confirmed on the base commit): `restoreFkTrust` ran an unconditional
`ALTER … WITH CHECK CHECK CONSTRAINT` on the materialized reverse-leg path, which fails on a **no-ALTER
`ManagedDml` cloud sink** (SQL 1088). Fixed by **capability descent** — the re-trust now descends on the
ALTER-permission family (1088/4902/229) to the named `FkTrustNotRestoredOnBulkLoad` tolerance, propagating
constraint conflicts (547). **The materialized path is now capability-gated; the option-C Wave-2 follow-on
(wire re-trust into `writePlanStreaming` / `runSynthetic` + the CLI flag) still stands** and is a clean Wave-3
pick-up if it fits your slice.

**Matrix on `main`+this PR.** gate=PASS · rungs L1/L2/L3 = 5/4/5 · tolerances 10 (3 open — the Schema OpenGaps
`IndexOptionsUnreflected` / `CompositePkFkUnreflected` / `TriggerBodyUnparsedDropped`; the 10th is the option-C
`FkTrustNotRestoredOnBulkLoad`, AcceptedFaithful). **Wave 2 added/retired no tolerance and renamed no witness,
so the matrix regenerated byte-identically** — Wave 3's compression moves should do the same (they're
idiom/compression, not new erasures); if `matrix-status.sh` shows a diff after a Wave-3 move, something
unintended changed — investigate before committing.

**Build-discipline scars (heed them — they all bit this session).**
1. **⚠️ Docker tests SILENTLY NO-OP on this Windows box** unless `$env:PROJECTION_MSSQL_CONN_STR=
   "Server=localhost,11433;User Id=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False"`
   (the warm container, up). Confirm a **real per-test duration in seconds** via the TRX, never the green count.
2. **⚠️ Run bash scripts from the WORKTREE, not the main checkout** (the Bash tool resets cwd to the worktree
   root each call, so a bare `scripts/…` from there is fine; an absolute path into the worktree is safest).
3. `dotnet` is at `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe` (9.0.314; not on bash PATH). Build
   **Release too** before claiming done (only it catches FS3511). **Never the pure + Docker pools in one
   `dotnet test`** (OOM) — `test.sh fast` / `docker`.
4. **Run the FULL Docker pool (246/246 is the bar), not just `~Canary`** — a change to a comparison primitive
   (`CatalogDiff`, `PhysicalSchema`) ripples through every canary, and the *only* failure Wave 2 found was a
   non-canary reverse-leg test the option-C author had never run. **On any `Failed: N>0`, read the TRX** (the
   console interleaves and lies) and decide regression-vs-pre-existing by running the one test on the base.
5. **Closed-DU / closed-record edits cascade — grep first, fix all sites, build once** (the build is your
   completeness backstop: the test project is one assembly, so a missed site fails compilation). M7's rename and
   M9's binding collapse are both cascades.

**State.** Branch `claude/romantic-bohr-e270dd`; Waves 0+1 (from PR #620, merged) + Wave 2 sit on this branch as
five commits (the trio, M20, M3, the option-C fix, the M11/M12 axiom witnesses) atop `main` `dda83a8f`. Debug +
**Release** 0/0; pure pool **3372/0**; **Docker 246/246**; `AxiomTests` 79/0; `matrix-status.sh` gate=PASS
(byte-identical); `verifiability-gate.sh` exit 0. After Wave 3, chapter-close and extend the same PR (or open the
Wave-3 PR atop it per the operator's call). Hold the spine: name every refusal, count every crossing, leave the
books balanced — and let the workflow carry the fan-out.

---

# Handoff addendum — 2026-06-15, THE VECTOR WAVE 1 LANDED (the keystone M1 — the Decision axis is now an EARNED green); your job is WAVE 2 — the fan-out wave, and this time you ORCHESTRATE IT WITH A WORKFLOW

To the next agent.

**Your mission.** Build **Wave 2** of `THE_VECTOR.md` — five moves, cleanly partitionable, and the **first
fan-out wave**. Wave 0 (honesty) and Wave 1 (the keystone) were single coupled moves done inline; Wave 2 is
where worktree isolation finally pays. **Drive it as a `Workflow`** (the operator asked for this explicitly):
worktree-isolated implementers, **one adversarial verifier per move**, and **one integrator** who alone owns
the build / matrix / gates. Read `THE_VECTOR.md` §6 (the move catalog) + §7 (the wave roadmap) first; the moves
below are the partition.

**The five moves and how they partition (this is the workflow's shape).**
1. **The `CatalogDiff.fs` trio — M11 → M13 → M12 — ONE serialized implementer** (shared file; they conflict if
   parallel). M11 = the triangle-inequality law on `between`; M13 = drop the `Result` on `between` (it is total);
   M12 = the groupoid inverse (`between a b` ∘ `between b a` = id). Order matters: M11, then M13, then M12.
2. **M3 — `genCatalogPair` + the swept property** (its own surface — a generator + a property test). Independent;
   runs parallel to the trio.
3. **M20 — `GateLabel.MidWriteNotProtected` + the pure gate** (`Preflight.fs`). Independent; parallel.
   So: **3 worktree implementers** (trio / M3 / M20) → **3 adversarial verifiers** (one each) → **1 integrator**
   who cherry-picks/integrates, runs the one authoritative build, regenerates the matrix, and runs the gates.
   Have implementers **commit to their worktree branch** and integrate via cherry-pick — NOT text-diff-apply
   (Windows diffs lack the trailing newline `git apply` wants; that bit Wave 0).

**What is DONE — do not redo (Wave 0 + Wave 1, both in this PR).** Wave 0: M1′ + M2 + M16 + M15 + count
corrections (the honesty wave). Wave 1 (the 2026-06-15 "THE VECTOR Wave 1 BUILT" DECISIONS entry is the
substance — read it): `PhysicalForeignKey.IsTrusted`; overlay-aware `PhysicalSchema.ofCatalogWith` (with
`ofCatalog = ofCatalogWith empty`, byte-identical); the Docker decision-readback canary (agreement +
falsifiability, ~3 s real); **M1′ retired** (both Decision tolerances, per-axis, proven); the matrix flipped
**Decision → `✅ L3`, L2 4/5, tolerances 9 (3 open)**. The honest machine's fifth column is now true.

**One open question Wave 1 surfaced and DEFERRED to the operator (do not silently decide it).** M1's new FK
`IsTrusted` field revealed that `Transfer.Execute` bulk-loads via `SqlBulkCopy` WITHOUT `CHECK_CONSTRAINTS`,
leaving the sink's FKs **untrusted** (`is_not_trusted = 1`) while the source is trusted. The transfer canary now
normalizes the trust bit by name (`TransferCanaryFixtures.trustNormalizedFks` — a precise exclusion; structure
still compared) and FK-trust round-trip is witnessed on the schema surface. **The question for the operator:**
should the transfer re-validate FKs (`WITH CHECK CHECK CONSTRAINT`) after load to re-trust the sink, trading
throughput for trust fidelity at hundreds-of-millions-of-rows scale? It's flagged in DECISIONS; surface it.

**Build-discipline scars (heed them — they all bit this session).**
1. **⚠️ Docker tests SILENTLY NO-OP on this Windows box** unless `$env:PROJECTION_MSSQL_CONN_STR=
   "Server=localhost,11433;User Id=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False"`
   (the warm container, up). Confirm a **real per-test duration in seconds** via the TRX, never the green count.
2. **⚠️ Run bash scripts from the WORKTREE, not the main checkout.** `cd`-ing to
   `C:/Users/danny/code/outsystems-ddl-exporter/sidecar/projection` lands in the MAIN checkout (a *different*
   branch); `matrix-status.sh` then reads the wrong `Tolerance.fs` and overwrites the main checkout's matrix.
   Run scripts by absolute path into the worktree: `bash "<worktree>/sidecar/projection/scripts/<x>.sh"`. (The
   Bash tool resets cwd to the worktree root each call, so a bare `scripts/...` from there is also fine.)
3. `dotnet` lives at `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe` (9.0.314; not on bash PATH).
   Build/test via PowerShell, or `export PATH="/c/Users/danny/AppData/Local/Microsoft/dotnet:$PATH"` for
   `scripts/test.sh`. **Never the pure + Docker pools in one `dotnet test`** (OOM) — use `test.sh fast` /
   `docker`. Build **Release** too before claiming done (only it catches FS3511).
4. **Run the FULL pure pool, not a focused subset** — Wave 0 left `MatrixLadderTests."exactly two tolerances are
   open"` red (it never updated the count when it added OpenGaps), and only the full pool surfaces it. A change
   to a comparison primitive (`PhysicalSchema`, `CatalogDiff`) ripples through every canary — run the **full
   Docker pool** after it (244/244 is the bar), not just `~Canary`.
5. **Closed-DU edits cascade — grep first, fix all sites, build once.** `M13` (drop `Result` on `between`)
   changes every `CatalogDiff.between` call site; `grep -rn "CatalogDiff.between" src tests` before you touch it.

**State.** Branch `claude/wizardly-wing-f7fb26`, Wave 0 + Wave 1 committed (this branch fast-forwarded onto
Wave 0's commit `a71bdc34`, which was committed but **never merged to main** — the PR carries both). After
Wave 2, chapter-close and extend the same PR (or open the Wave-2 PR atop it per the operator's call). Hold the
spine: name every refusal, count every crossing, leave the books balanced — and this time, let the workflow
carry the fan-out.

---

# Handoff addendum — 2026-06-15, THE VECTOR WAVE 0 LANDED (honesty & fitness); your job is WAVE 1 — the keystone M1, which turns the honest tolerance back into an earned green

To the next agent.

**Your mission.** Build **Wave 1 — M1, the Decision-readback adjunction** (the single highest-leverage
move in `THE_VECTOR.md`; §6 M1, §7 Wave 1; mechanics in `THE_VECTOR_UNABRIDGED.md` Part III). Wave 0
just made the engine honest about the Decision axis; M1 makes it *true*. When M1 lands, **delete the
two `@ladder … Decision OpenGap` tags** (`FkTrustUnreflected`, `UniquePromotionUnreflected`) — the
generator auto-flips Decision back to faithful. Honesty first (done), then the theorem (you).

**The move, precisely (the surfaces are scouted and the join keys confirmed).**
1. Add `IsTrusted : bool` to `PhysicalForeignKey` (`src/Projection.Core/PhysicalSchema.fs`). A normally-
   enforced FK is `true`; a `WITH NOCHECK` FK is `false`. The widened set-difference in
   `PhysicalSchema.diff` picks the field up with **no comparator change**.
2. Make `ofCatalog` overlay-aware **the way the SSDT emitter already is** — copy the
   `statements`/`statementsWith` precedent (`SsdtDdlEmitter.fs:880-886`): add
   `ofCatalogWith : DecisionOverlay -> Catalog -> PhysicalSchema` (the overlay-aware core) and keep
   `ofCatalog c = ofCatalogWith DecisionOverlay.empty c`. This preserves **byte-identity at `empty`** and
   every one of the ~30 existing `ofCatalog c` call sites — guard with the T1 goldens
   (`GoldenEmissionTests`) and `AdjunctionLawTests`. Do NOT change `ofCatalog`'s signature.
3. In `toPhysicalForeignKeys`, set `IsTrusted = r.IsConstraintTrusted && not (Set.contains r.SsKey overlay.NoCheckFk)`
   (the source half reads the overlay; the readback half is overlay-`empty` and `r.IsConstraintTrusted`
   is already recovered from `sys.foreign_keys.is_not_trusted` at **`ReadSide.fs:1171`** — the read leg is
   free). In `toPhysicalIndexes`, set `IsUnique = IndexUniqueness.isUnique idx.Uniqueness || Set.contains idx.SsKey overlay.EnforceUnique`.
   (`Reference.SsKey` matches `overlay.NoCheckFk`; `Index.SsKey` matches `overlay.EnforceUnique` — both confirmed.)
4. Add the **decision-readback property**: emit a `WITH NOCHECK` FK via the overlay → deploy → ReadSide →
   `ofCatalog` on the readback yields `IsTrusted = false`, and `ofCatalogWith overlay` on the source agrees
   — the diff is empty; a *trusted* FK round-trips `IsTrusted = true`; the no-cheat case (emitter didn't
   actually emit NOCHECK) shows a non-empty diff. This routes the recovered decision through the **general
   comparator** instead of the bespoke nullability-only test (`CanaryRoundTripTests.fs` ~886). It is
   **Docker-gated** — see the scar below; confirm a real duration, never the green count.

**Exit criterion (Wave 1).** The live canary witnesses `NoCheckFk`/`EnforceUnique` survival through
emit → deploy → read-back; the matrix's Decision cell is *showable*, not asserted, and flips back to
`✅` when you delete the two Decision tolerances.

**State.** Branch `claude/sleepy-lumiere-43d5c9`. Wave 0 is committed and green: Debug + **Release**
clean; pure pool 3357/0; `matrix-status.sh` regenerated (L1/L2/L3 = 5/3/5; Decision `◑ L2-partial`;
tolerances 11, 5 open); `verifiability-gate.sh` clean; the analyzer gate green and now CI-promoted. The
2026-06-15 `DECISIONS` entry "THE VECTOR Wave 0 BUILT" is the substance; this letter points. After Wave 1,
chapter-close and the PR carries Wave 0 + Wave 1 together.

**Build-discipline scars (heed them — M1's witness is Docker-gated).**
1. **⚠️ Docker tests SILENTLY NO-OP on this Windows box** unless `$env:PROJECTION_MSSQL_CONN_STR=
   "Server=localhost,11433;User Id=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False"`
   (the warm container, currently up). They pass as ~0.4 ms `()` no-ops otherwise — **confirm via per-test
   duration in seconds**, never the green count (survival-rule #12). M1's readback property is exactly such
   a witness.
2. `dotnet` is at `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe` (9.0.314; not on bash PATH) —
   build/test via PowerShell; **never the pure + Docker pools in one `dotnet test`** (OOM). Build in
   **Release too** before claiming done (Wave 0 found a pre-existing FS3511 only Release surfaces; CI runs
   bash gates, not a Release compile).
3. Default-empty overlay args **must preserve byte-identity at `empty`** — the goldens are the guard. Every
   `AXIOMS.md` change carries its `AxiomTests.fs` witness in the same commit. Name every refusal; the
   matrix under-claims by construction — keep it that way.

---

# Handoff addendum — 2026-06-15, BUILD THE ARCHETYPE SLICES — the model is locked, the inputs are confirmed, the plan is written; your job is to build Slice A → C → S → B per REVERSE_LEG_WORK_PLAN.md

To the next agent.

**Your mission.** Build the four sequenced slices in **`REVERSE_LEG_WORK_PLAN.md`** — there is nothing
left to discover; the operator's real-estate facts are all in, the model is locked, and the plan names
each slice's mechanism, gate, and exit-test witness. Read `REVERSE_LEG_WORK_PLAN.md` first, then the
2026-06-15 `DECISIONS.md` entries (they are the substance; the plan points). Then build, warm-witness,
and write one DECISIONS entry per slice.

**The locked model (what every slice assumes).** One engine, **two flows, two sink archetypes** — the
real estate proved the archetype-as-config-disposition design:
- **Flow P — populate the on-prem** (the engine writes in two ways: direct-connect `migrate` + emit
  SSDT/data artifacts). Sink = on-prem = **`FullRights`-minus-DMV** (verified: CREATE TABLE, ALTER,
  IDENTITY_INSERT, sink-resident progress; no `VIEW DATABASE PERFORMANCE STATE`). ⇒ `PreservedFromSource`
  (no keymap) + sink-resident resume.
- **Flow R — the reverse leg** (on-prem → the *empty* cloud it fills). Sink = cloud = **`ManagedDml`**
  (J5). ⇒ `AssignedBySink` + keymap + the `#`-temp spill + client journal.
- **Sizing:** estate-data on-prem = 2.0 M key-map-shaped rows (75 MB) / 3.5 M all-`dbo` (134 MB); host
  = 64 GB. The resident map **fits even at ~200 M (~4–8 GB ≪ 64 GB)** — so **Slice S (the spill) is a
  completeness/headroom build, not a current necessity**: ship it **armed-but-inert** (configurable
  threshold, default off, byte-identical today). Do not overstate its need; the operator chose it for
  scale-safety, recorded as a build-ahead-of-the-wake.

**The slices (build order A → C → S → B; the graph is A → {C, S, B}).**
1. **A — the `Archetype` config type** + `CapabilityProfile.of` + `Environment.Archetype` (default
   inferred from `Grant`, byte-identical). Pure round-trip witness. The foundation; nothing branches yet.
2. **C — the FullRights populate forks** (highest value): `PreservedFromSource` (write source keys
   directly, *no* capture/remap) + sink-resident resume, gated on a `FullRights` sink; `ManagedDml`
   keeps AssignedBySink + journal byte-identical. Docker witness: keys preserved on FullRights, old path
   on ManagedDml.
3. **S — the reverse-leg `#`-temp keymap spill** (armed scale-safety): a session `#`-temp keymap (temp
   tables ARE permitted under DML — J5 P5) + server-side `UPDATE…JOIN` for phase-2, above a configurable
   threshold. Equivalence canary: spill-on vs resident → **byte-identical** sink state.
4. **B — the survey verifies the archetype** (A44; least urgent — the verdicts are known): route
   `CapabilitySurvey` through `archetype.Grant` + declared-vs-probed reconciliation, so a
   `FullRights`-minus-DMV surfaces as a named split.

**What is SHIPPED — do not redo.** Phases 2–4 + NM-58 (reconcile ∘ streaming + the validate-user-map
halt; force-journal + journal-address-drift refusals; the DryRun row-count preview; the reconcile-key
robustness — blank-key exclusion + duplicate-target tiebreaker). 62 Docker + 184 pure green warm. The
operator package (`REVERSE_LEG_OPERATOR_PROBE_SHEET.md`, `PHASE_1_REAL_WIRE_HARNESS.md`,
`NEXT_BUILD_INPUTS.sql`) and the design (`DATABASE_ARCHETYPES.md`).

**Build-discipline scars (heed them).**
1. **⚠️ Docker tests SILENTLY NO-OP on this Windows box** unless `$env:PROJECTION_MSSQL_CONN_STR=
   "Server=localhost,11433;User Id=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False"`
   (the warm container). They pass as 0.4 ms `()` no-ops otherwise — **confirm via per-test TRX
   durations (seconds, not ms)**, never the green count (survival-rule #12).
2. `dotnet` is at `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe` (not on bash PATH) — build/
   test via PowerShell; never the pure + Docker pools in one `dotnet test` (CLAUDE.md §4.1).
3. **The archetype is total-over-the-engine** (closed DU, one `CapabilityProfile.of` site — the
   `ArtifactByKind` discipline); `Grant` becomes a *derived* projection. Named refusals + a witness +
   a DECISIONS entry per slice; promote the reserved Skip-stubs where they fit.

**State.** Branch `claude/reverse-leg-execution-phases-2-5`, **PR #614** (open against `main`). Worktree
at `sidecar/projection/.claude/worktrees/unruffled-diffie-801d9b`. Read the work plan, build the slices,
keep the books balanced.

---

## Handoff addendum — 2026-06-15, REVERSE-LEG EXECUTION (Phases 2–5 landed) + THE OPERATOR PROBE PACKAGE — your job is to DECODE the operator's probe results (attached with this handoff) and drive each staged item to completion

To the next agent.

**Your mission in one sentence.** The reverse-leg data-load engine is built and witnessed at Docker
scale through Phase 4; what remained was everything that needs the operator's real databases. The
operator has now **run the probes and is bringing you the results** (attached beneath this handoff —
the filled-in ledgers from `REVERSE_LEG_OPERATOR_PROBE_SHEET.md`, the `PHASE_1_REAL_WIRE_HARNESS.md`
throughput bench, and the Part E archetype verdict). **Take those results and turn each "staged /
gated" item into a built, witnessed one.** This letter is the decoder: result → what it unlocks →
the slice to build.

**Where you are standing.** Branch `claude/unruffled-diffie-801d9b`, 7 commits on top of the charter
base (`74e9b597`). Read `CHARTER_REVERSE_LEG_EXECUTION.md` first (Part VII = code state; Part IX =
the phase ladder with the ✅/⏳ status I left). Phases 2–4 are **built + warm-witnessed** (reconcile ∘
streaming with the validate-user-map halt; force-journal + address-drift guard; the movement dry-run
row-count preview). Phases 1/5 are operator/real-wire/CDC-gated — the harness + probe sheet are how
they unblock. The three operator-facing companions are `REVERSE_LEG_OPERATOR_PROBE_SHEET.md`,
`PHASE_1_REAL_WIRE_HARNESS.md`, `DATABASE_ARCHETYPES.md`.

**The decoder — consume the attached results like this:**

| Operator result (probe) | What it unlocks → your slice |
|---|---|
| **Part E archetype verdict** (E1 CREATE TABLE, E3 IDENTITY_INSERT) | **The biggest fork.** *FullRights* (both OK) ⇒ build `DATABASE_ARCHETYPES.md` Slice A (the `Archetype` type + `CapabilityProfile.of`, derived from `Grant` — byte-identical) **with its first consumer**: Slice C (sink-resident progress table — retires the Phase-3 journal hazards on-prem) and/or Slice D (PreservedFromSource — removes the whole capture/remap/FK-repoint path when keys can be preserved). *ManagedDml* (both denied) ⇒ the engine already ships this; declare it + move on. *Split* ⇒ treat each capability independently. **Do not build the archetype type with no consumer** (zero-consumer rule); build it the moment a fork needs it. |
| **D1 throughput (P7b)** rows/sec vs floor | ≥ floor ⇒ record in DECISIONS (amend the ~271/~27k/~35.5k ladder) + proceed to the cutover gates. Materially < 20k ⇒ trigger an escape hatch *with a plan*: the 50k-chunk sweep, the **parallel wavefronts port** (reuse `Deploy.executeLeveledSeed`/`ParallelSafe` on the reverse leg — port, not build), or the sink-resident spill (sized by B2). |
| **B1 row counts + B2 FK-target rows / `approx_keymap_MB`** | vs the transfer-host memory budget ⇒ resident remap (fast) **or** build the sink-resident keymap / server-side `UPDATE…JOIN` **spill** (DESIGNED-only today — this result is its wake). Also: if a real resume will exceed ~10M captured pairs, that is the wake for **journal compaction** (Phase 3 staged). |
| **B3 orphan FK rows** | the expected drop count ⇒ set `--allow-drops` expectations / decide clean-vs-accept; confirm the exit-9 + `SkippedReferences` report matches. |
| **B4 reconcile-key quality** (dup / null / casing of email) | dups ⇒ build a tiebreaker; nulls ⇒ those users hit the validate-user-map halt (expected); **casing mismatch ⇒ the reconcile matches raw strings (ordinal Map) — if source/sink email casing differs, build a normalization step** or the match silently misses. |
| **A1–A5 schema shape** | the disposition mix + the refusal list. Composite-PK (A2) / no-PK (A1) / non-nullable cycle FK (A3) ⇒ each refuses by name — plan handling. A5 non-insertable columns ⇒ confirm the engine excludes them for those tables. |
| **C1 object-scope DENY** | if any planned-write table is missing a permission ⇒ **promote the reserved Skip-stub** `ReverseLegBoundaryTests."object-scope DENY refused by name before any write"` (descend `Preflight.captureGrantEvidence` to table scope). |
| **C3 triggers** | the capture-ladder descent map ⇒ confirm those descents appear in the run report (expected, not a regression); scrutinize any INSTEAD OF. |
| **C4 CDC enabled?** | the verdict that **arms NM-73 auto-fallback** (Phase 5): CDC live ⇒ wire CDC-silence → EXCEPT automatic fallback (the manual override already ships). |
| **C5 memory semaphore** | the chunk-size ceiling ⇒ pre-size `CaptureChunkSize` / drop-indexes-during-load to avoid the RESOURCE_SEMAPHORE stall. |
| **C6 real-table write-probe** | if it errored ⇒ the exact constraint/trigger/permission to handle before a real run (the single most important pre-run fact). |
| **E4 schema parity / shape drift** | if the rendered contract and live shape diverge ⇒ promote the reserved Skip-stub `ReverseLegBoundaryTests."B-drift refused by name"` (a named `transfer.sourceShapeDrift` preflight). |

**Build discipline (the scars from this session — heed them).**
1. **⚠️ Docker tests SILENTLY NO-OP on the Windows box.** `DockerDaemon.ensureRunning()` checks a
   *Linux* socket, so every `EphemeralContainerFixture` test passes as a 0.4ms `()` no-op unless
   `PROJECTION_MSSQL_CONN_STR` is set. Run them with
   `$env:PROJECTION_MSSQL_CONN_STR="Server=localhost,11433;User Id=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False"`
   (the warm container; `scripts/warm-sql.sh conn` prints it) and **confirm via per-test TRX durations
   (seconds, not ms)** — never trust the green count. This is survival-rule #12 in operational form.
2. **`dotnet` is not on the bash PATH;** it lives at `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe`.
   Use PowerShell to build/test. Pure pools and Docker pools separately (CLAUDE.md §4.1).
3. **Every refusal named; nothing silent.** Each new gate ships as a named `ValidationError` + a pure
   witness; promote the reserved Skip-stubs rather than writing from zero; DECISIONS entry per slice.
4. **The archetype is zero-consumer until its fork** — build the type *with* Slice C/D, not before
   (the dead-algebra retirement precedent).

**What NOT to redo (already built + witnessed, warm).** Reconcile ∘ streaming + the streaming
validate-user-map halt (Phase 2); the force-journal + address-drift refusals (Phase 3); the streaming
DryRun row-count preview (Phase 4). 62 reverse-leg + forward-reconcile Docker tests + 184 pure tests
green for real against the warm container. The DECISIONS 2026-06-15 entries (Phases 2–5 + the
archetype direction) are the substance; this letter only points.

Hold the spine — name every refusal, count every crossing, and turn each probe result into a settled
disposition.

---

# Handoff addendum — 2026-06-15, MIGRATION-CONTEXT WIRING — the data triumvirate is whole: the operator-curated Migration lane now has a real row source (JSON file → MigrationDependencyContext), threaded the same seam as Bootstrap, partition-disjoint, Docker-witnessed three lanes live

To the next agent.

You are inheriting a **clean, single-slice branch paused for review.** Branch
`claude/migration-context-wiring`, one commit on top of `main` (`02bba071`, the
merged live-source/Bootstrap chapter). The operator asked to commit this slice and
stop for review before the next one. Everything below it is the prior chapter's
letter history.

**What shipped (don't redo) — handoff task 1, "migration-context wiring", complete
and verified.** The production compose path used to thread
`MigrationDependencyContext.empty`; the Migration lane had no row source. It does now:

1. **`MigrationDependenciesBinding.fs`** (new, `Projection.Pipeline`) reads the
   operator-curated file at `overrides.migrationDependencies.path` into a
   `MigrationDependencyContext`. **Format = JSON, logical-keyed (operator decision):**
   `{ "kinds": [ { "module", "entity", "rows": [ { "id", "values": {col:val} } ] } ] }`.
   Resolves `(module, entity)` → `SsKey` via `CatalogResolution.tryKindByLogical`;
   synthesizes row ids via `SsKey.synthesizedComposite "migration" […]`; values are
   raw strings (`""` = NULL; number/bool coerce). No path ⇒ empty context (no-op).
   Malformed / unresolved / unreadable ⇒ loud `pipeline.migrationDependencies.*`. This
   cashes out the deferred slice-ε ingestion adapter.
2. **Threaded the SAME seam Bootstrap rides** (parity): `readAndHydrateConfigModel`
   returns `(Catalog * bootstrapRows * migration)`; publish
   (`projectWithStateWithPinsAndBootstrap` → `composeRenderedBundleWithBootstrap`) and
   store-leg (`projectSeedPlan` → `composeRenderedLeveledWithBootstrap`) both carry it.
   Non-config callers delegate `…empty` (byte-identical).
3. **Partition stays disjoint**: `hydrateBootstrapRowsExcluding migrationKinds` drops
   (Static ∪ **Migration**) from the bootstrap complement under
   `AllRemaining`/`AllExceptStatic`, so `OverlappingEmitterCoverage` can't trip. Under
   `AllData` the Migration lane is skipped, so no exclusion needed.

**Verified.** `MigrationDependenciesBindingTests` 7/7 (pure); `LiveSourceDockerTests`
4/4 against the warm container incl. the new **"data triumvirate"** witness (Static +
Migration + Bootstrap all populate live, disjoint — TRX-confirmed it ran, not
skipped). 109/0 across the touched pure classes. DECISIONS 2026-06-15 entry written;
`MigrationDependenciesEmitter` docstring updated.

**What you should pick up next (operator's task list; both independent, larger
slices).**
- **Task 2 — the AllData double-stream cleanup (perf-only).** Under `AllData`,
  `hydrateCatalog` still grafts static rows (unused — StaticSeeds is skipped under
  AllData) AND `hydrateBootstrapRows` re-streams every kind incl. static. One-pass
  unification (skip the static graft under AllData, or share one stream). Bench
  before/after — correctness-first was the operator's call, so this is pure
  optimization.
- **Task 3 — reconciliation WP7–WP9** (`V1_FULL_EXPORT_RECONCILIATION_PLAN.md` §4–5):
  WP7 SSDT byte-parity (GO → `"\nGO\n\n"`, per-table constraint formatting, IX/UIX
  logical-name synthesis, FK 128-char cap, MS_Description pinning); WP8 `Order_Num`
  Service-Studio ordering pass (C3); WP9 first-run-complete `projection.sample.json`
  (which should now showcase the `migrationDependencies` file too), ossys-only
  provenance-arm fix, `compare`-verb design slice. Independent — sequence per §5.

**Watch-fors (this session's scars).** `dotnet` isn't on the bash PATH — prefix
`export PATH="/c/Users/danny/AppData/Local/Microsoft/dotnet:$PATH"`. The Docker gate
(`Deploy.Docker.ensureRunning`) probes a **Unix socket** that doesn't exist on
Windows, so it soft-skips unless `PROJECTION_MSSQL_CONN_STR` is set — point it at the
warm container (`Server=localhost,11433;User Id=sa;Password=Projection@Strong1;
TrustServerCertificate=True;Encrypt=False`) and a 6 s / 4-test run is real; a 33 ms /
4-test "pass" is a soft-skip (survival rule 12 — check the TRX, not the green count).
Never run pure+Docker as one `dotnet test`. The warm container was "Up 48 min" — if a
connection batch starts failing, restart it, don't assume a regression.

---

# Handoff addendum — 2026-06-14 (night), THE LIVE-SOURCE CHAPTER — both owed items CLOSED + VERIFIED: live-path Docker witnesses (+ two adapter fixes) and Bootstrap-always (live-hydrated, AllData via config); 3 pre-existing Docker breakages also fixed

To the next agent.

You are inheriting a **closed chapter**. The two items the predecessor letter
(below) left owed are both **shipped and Docker-verified**, the design question
behind Bootstrap was **resolved with the operator**, and three pre-existing
Docker-pool breakages surfaced along the way are fixed. Everything is on branch
`claude/projection-invariant-audit-b6iknj` as **4 commits on top of `main`**
(`739e2505`); a fresh PR was opened (the predecessor's #607/#608 are merged). The
plan that drove this — `PLAN_2026_06_14_LIVE_SOURCE_AND_BOOTSTRAP.md` — is now
fully executed.

**What shipped (don't redo).**

1. **Live-path Docker witnesses + two adapter fixes** (commit `063f4e37`).
   `tests/.../LiveSourceDockerTests.fs` proves `live:`-ref catalog readback and
   `compare` live-profiling end-to-end. Writing them exposed two latent defects in
   the build-only-verified adapter: (a) **the 4.4 trap** — `Source.ofLive`'s
   profile path profiled a ReadSide-derived (all-`Static`) catalog, which
   `LiveProfiler` skips, so the dealbreaker section was silently always empty;
   fixed with `Catalog.stripStaticPopulations` (the `Preflight`/`DataIntegrity
   Checker` precedent). (b) **connection exceptions escaped** the `Source` port's
   `Task<Result<_>>` boundary; now wrapped as the named `source.live.connection
   Failed` / `.profileFailed` refusals. The stale `RefTests` "live ref fails loud
   (adapter pending)" (red since the adapter shipped) now asserts the real contract.

2. **Bootstrap-always** (B1 `dbcf0ef4` composer; B2 `c081fe6e` pipeline). The
   operator's model (verbatim, recorded in DECISIONS): *Bootstrap = all the data,
   with a flag to make it the non-intersecting complement.* That maps onto the
   existing `DataComposition` DU — `AllData` (everything) vs `AllRemaining`
   (complement of Static ∪ Migration, the default). **DynamicData is a deprecated
   V1 term** — V2's Bootstrap is V1's `AllEntitiesIncludingStatic` snapshot. The
   `BootstrapEmitter` now renders a real row source; `Hydration.hydrateBootstrap
   Rows` streams the bootstrap-eligible kinds live (scoped per composition,
   disjoint from Static so the partition law holds); `emission.bootstrapAllData`
   makes `AllData` reachable; NM-73's drift guard rides Bootstrap too (operator
   decision). `Data/Bootstrap.sql` golden re-blessed (DECISIONS-noted) + a Docker
   hydration witness. A latent `AllData`-dispatch bug (Static fired under AllData,
   would trip `OverlappingEmitterCoverage` once Bootstrap is populated) was fixed.

3. **3 pre-existing Docker breakages** (commit `ae3e54b6`, NOT regressions from
   this work — the full Docker pool just hadn't been run): two `MigrationCanary
   Tests` AC-X1 looked up the retired fused `Data/seed.sql` (the per-lane
   retirement missed them — `FullExportDataBundleTests` was updated, these
   Docker-only tests weren't) → now `Data/StaticSeeds.sql`; `CanaryRoundTripTests`
   6.A.6 built a `Reference` in the illegal constraint-state quadrant → added
   `HasDbConstraint = true`.

**Verified:** pure pool **3314/0** (211 skip); the live-path Docker class **3/3**;
the 3 fixed tests green focused; the full Docker pool re-run was the green gate.

**What you might pick up next (all deferred, none blocking).**
- **Supplemental bootstrap kinds** — V1's snapshot injects `ossys_User` et al.
  beyond the catalog's own entities; deferred per operator. The `UserRemapContext`
  plumbing through Bootstrap already exists.
- **Migration-context wiring** — the production compose path still threads
  `MigrationDependencyContext.empty`; `hydrateBootstrapRows` already excludes
  migration kinds from the bootstrap complement, so confirm the partition
  end-to-end when migration is wired.
- **The `AllData` double-stream** — under `AllData`, `hydrateCatalog` still grafts
  static rows (unused, StaticSeeds skipped) AND `hydrateBootstrapRows` re-streams
  them; a one-pass unification is a perf-only cleanup (correctness-first was the
  operator's call).
- **The wider reconciliation program** — `V1_FULL_EXPORT_RECONCILIATION_PLAN.md`
  WP7 (SSDT byte-parity), WP8 (`Order_Num`), WP9 (config samples + the `compare`
  verb's design slice) remain.

Hold the spine — read the plan, run the tests warm, and note that the live path now
has its Docker witness and Bootstrap is no longer a hollow lane.

---

# Handoff addendum — 2026-06-14 (evening), THE LIVE-SOURCE CHAPTER — campaign owed-work CLOSED + operator data-artifact direction; live-source adapter + compare-profiling SHIPPED build-verified; OWED: Docker smoke tests for the live path + Bootstrap-always (has a real open question)

To the next agent.

You are inheriting an **operator-directed chapter on top of the (now-closed)
invariant near-miss campaign**. Everything lives on branch
`claude/projection-invariant-audit-b6iknj`, **PR #607** (8 commits on top of
`4c1426a0` = main). Your executable plan is **`PLAN_2026_06_14_LIVE_SOURCE_AND_
BOOTSTRAP.md`** — read it after this letter; it carries the file:line seams.

**What is DONE (context, so you don't redo it).** The campaign's owed slices all
landed (NM-73 on BOTH data lanes; NM-17 the heavy `KindFacet` diff channel + the
four NM-16 tolerances retired; NM-62 was already-satisfied). Then the operator
redirected to data artifacts: the fused `Data/seed.sql` FILE is **retired** — the
per-lane files (`Data/StaticSeeds.sql` / `Data/MigrationData.sql` /
`Data/Bootstrap.sql`) are the operator-facing artifacts, each emitted when its lane
has content (the ≥2-lane gate is gone; `bundle.Fused` stays in-memory for the
leveled deploy's cross-lane ordering). Per-lane data goldens are blessed under
`Golden/data-lanes/`. The **live-source adapter** (`Source.ofLive`; `Ref` `live:`
refs resolve) and **`compare` live-profiling** are wired and **build-verified** —
but they compose Docker-tested adapters WITHOUT their own end-to-end Docker test yet.

**What you OWE (two things; the plan has the recipes):**
1. **Docker smoke tests for the live path.** Prove `live:`-ref catalog readback
   (`ReadSide.read`) and `compare` live-profiling (dealbreakers populated) work
   end-to-end. The fixture surface is ready: `EphemeralContainerFixture`'s
   `MasterConnectionString` + `WithEphemeralDatabase` + `Deploy.ConnectionString
   .buildPerDb` give you a `live:`-able connection string. Plan §2.
2. **Bootstrap-always.** The operator wants `Data/Bootstrap.sql` created basically
   always, Docker + live. Step 1 already makes the FILE emit the instant the lane
   has content — but **nothing feeds the Bootstrap lane**: `BootstrapEmitter
   .emitWithTopo` uses `Map.empty`, and hydration only grafts STATIC-marked kinds.
   **There is a real design question first** (do NOT guess): *what is a
   bootstrap-eligible kind and where do its rows come from?* — the complement of
   (Static ∪ Migration): system users / default policies. Resolve the row-source
   with the operator (live-hydrate the complement? an explicit context? a modality
   mark?), THEN wire it through `dispatchSiblings` → `BootstrapEmitter` and add the
   golden (non-Docker via `DataLaneGoldenTests` + a Docker hydrated scenario). Plan §3.

**WILL-BITE-YOU watch-fors (this session's scars):**
- The **warm SQL container is up** (`scripts/warm-sql.sh status`) — but it dies
  under accumulated load (survival rule #2: `Could not open a connection` ⇒
  `warm-sql.sh restart`, re-run focused; NOT a regression).
- Commits show **"Unverified"** — the env's SSH signing key is empty (environmental).
- **PR #607's description is stale** (lists only the first 3 commits) — `gh pr edit`
  needs `gh auth refresh -s read:project`; the full 8-commit summary is below in
  this letter's predecessor and in the plan §4.
- The campaign's whole ethos: **don't ship inert/half-built code, and bless a golden
  only with a DECISIONS note.** Land the live Docker tests before claiming the live
  path works; resolve the Bootstrap row-source question before building it.

Hold the spine — read the plan, run the tests warm, finish the live path with a
Docker witness, and name the Bootstrap row-source before you wire it.

---

# Handoff addendum — 2026-06-14, THE INVARIANT NEAR-MISS CAMPAIGN (the bug hunt) — ~60 of 74 findings shipped + two features (Model Fidelity Report · `compare` verb); THREE slices owed (NM-73 EXCEPT guard · NM-17 KindFacet channel · NM-62 trivial)

To the next agent.

You are inheriting the close of a **near-miss invariant hunt**, not a chapter in
the usual program. An operator-directed audit (a 12-agent sweep) catalogued **74
near-miss findings** — places where the codebase *almost* obeys a rule it would
want named/enforced/tested — in `AUDIT_2026_06_13_INVARIANT_NEAR_MISS_HUNT.md`.
That doc is the campaign's index; its new **§ Disposition** (top) is the live
ledger of what shipped vs what's owed. Branch `claude/projection-invariant-audit-b6iknj`,
HEAD `fc35d517`, ~70 commits, full pure pool **green (3298/0)** on a healthy
container. Read the audit doc's Disposition first, then this letter's *owed* list.

**What is DONE (don't re-hunt — the audit doc's Disposition has the per-finding
map).** The whole no-brainer / next-ten / further-ten / gate-cluster / Pile-2
sweep landed (~50 fixes), each green-gated. Two corrections worth carrying: **NM-06
(`rekey`) is LIVE** (the sole user-map-path wiring — the audit's "dead" verdict was
stale, so it STAYED), and **NM-01 is shelved** (operator unsure of intent — drop-
all-and-reinsert-via-stream, not yet built). Plus two real features shipped: the
**Model Fidelity Report** (`fidelity.json`/`.txt` on every full-export/migrate run,
surfaced via `report` — a count-first roll-up of data-violations + accepted-
divergences + uniqueness candidates; closed NM-35 by *building* the uniqueness
section) and the **`projection compare <A> <B>`** verb (read-only multi-env
readiness — schema delta + the fidelity engine's data-dealbreakers).

**What you OWE (three slices, in priority order):**

1. **NM-73 — EXCEPT validate-before-apply.** *Consciously deferred, not forgotten.*
   It's a **safety-critical drift guard** (a typed-AST `IF EXISTS (SELECT keys FROM
   target EXCEPT SELECT keys FROM <the MERGE source>) THROW`), so build it carefully
   in daylight — a subtly-wrong one THROWs spuriously or misses real drift. The plan
   is settled: add `EmissionPolicy.DataVerification = Standard | ValidateBeforeApply`
   (default `Standard`, BYTE-IDENTICAL); the `emission.dataVerification` config key
   (mirror NM-02 `EmitSchema`/NM-70 identity-annotations); thread to the data emitters'
   `renderMerge` (the IDENTITY_INSERT-bracket precedent at `StaticSeedsEmitter.renderMerge`,
   one GO batch); build the typed guard via the existing `TSql160Parser` template path
   (the same parse-string-into-typed-AST the CHECK/computed-column builders use —
   `ScriptDomBuild.fs:410`), NOT a hand-built AST and NOT a text-builder. Pin the
   "expected pre-state" = the target's managed rows match the source we're about to
   write (first apply over an empty target passes; a re-apply over a drifted target
   THROWs). **An agent started ONLY the `Policy` axis overnight; I reverted it** so no
   inert field is left behind — start clean.

2. **NM-17 — the real `KindFacet` diff channel (the capstone).** NM-16 took the
   *light* route (named the kind-level erasures — triggers/CHECKs/modality/IsActive —
   as `ToleratedDivergence`s so `CatalogDiff.norm=0` is witnessed, not silent). The
   *heavy* route closes it: a `KindFacet` DU mirroring `AttributeFacet` in
   `CatalogDiff.between`, with `applyDiff` patches + the round-trip + fixtures, then
   retire those tolerances. It touches the change algebra and regenerates goldens —
   its own focused slice.

3. **NM-62 — trivial.** Two advisory thresholds (`QueryHintPass`/`ProfileAnomalyPass`)
   are hardcoded; lift to named module constants (no config knob warranted).

   Plus a **small follow-on**: `compare` v1 resolves operands to catalogs only, so the
   data-dealbreaker section is advisory-silent — live-profile the *source* operand
   (reuse the profile-capture path) to populate it.

**WILL-BITE-YOU watch-fors (this session's scars):**
- **The warm SQL container degrades under accumulated load and dies MID-POOL** — a
  full ~3500-test pool exhausts it and the OSSYS-extraction classes
  (`BtReferenceFkFlow`/`OssysComprehensiveFixture`/`OssysExtractionCanary`) fail with
  connection errors. This is survival rule #2, NOT a regression. `scripts/warm-sql.sh
  restart`, then a *focused* run of the failed OSSYS class proves health; the full
  pool is green on a fresh container. Don't chase these as code bugs.
- **Subagents died silently mid-task several times** (committed nothing, or left
  uncommitted WIP). When delegating, check `git -C <worktree> status` if a completion
  is slow; the WIP is often salvageable (NM-71's 365-line `Compare.fs` was recovered
  that way). The worktree-branch ref also drifts onto the main checkout — re-`checkout`
  the feature branch before integrating.
- **Commits show "Unverified" on GitHub** — the env's SSH signing key
  (`/home/claude/.ssh/commit_signing_key.pub`) is **empty (0 bytes)**, so signing is
  impossible here. Every commit this session is unsigned; it's the environment, not a
  discipline lapse.
- **The perf-gate's stop-hook false-trips** on `emit.staticPopulation.statements.stream`
  (the canonical rule-#13 label) — it runs CONCURRENTLY with the test pool and inflates
  the CPU-bound labels uniformly. **Never `PERF_GATE_RECORD=1`** off these; the gate
  changes touch zero emit/ScriptDom files, so the label cannot be a real regression.

The hunt is essentially won — the codebase's "name every refusal, witness every
erasure, no silent downgrade" discipline now holds at far more sites than it did
72 hours ago. Finish NM-73 with care (it's the one safety surface), land NM-17 as
the algebra capstone, and the campaign closes clean.

Hold the spine — and name the gate.

---

# Handoff addendum — 2026-06-13, WP6 COMPLETE (data lanes filled): IDENTITY_INSERT bracket · Bootstrap Active · per-lane outputs (self-minimizing) · hydration · goldens reconciled — TWO caveats owed before merge (live OSSYS witness; Docker pool)

To the next agent.

Slice 5 (WP6, the data lanes) is **done, steps 1–5**, on branch
`claude/dreamy-pasteur-akjbx3` (PR #604). Read this, then the mid-slice and
pre-scope letters below for the seam-level detail. The program queue resumes
at WP5 (the C1 `V2.*` rename + gate), then WP7-remainder/WP8, then WP9; WP6
step 6 (EXCEPT validate-before-apply, C2) is its own later slice per the
plan's §5.

**What shipped (commit · what · witness):**
- `68b3506` — **step 1** IDENTITY_INSERT bracket on `AssignedBySink` MERGEs,
  ONE GO batch (the leveled deploy opens a connection per GO-segment).
- `9be312c` — **step 2** `BootstrapEmitter` delegates to the static-seeds
  renderer + goes `Active` (the last `NotImplementedInV2` in the codebase).
- `a796991` — **step 3** per-lane data outputs via `composeRenderedBundle`
  (one dispatch → fused + 3 lanes), emitted **only when ≥2 lanes carry
  content** (self-minimizing: a single active lane equals the fused seed, so
  no redundant per-lane file). Composer-witnessed.
- `196ac53` — **step 4** hydration: `Hydration.graftStaticPopulations`
  (pure) + `hydrateCatalog` (async — OSSYS-sourced opens a SECOND connection
  and streams the static-marked kinds via `Ingestion.collectInOrderFor`,
  never `ReadSide.read`). One seam `readAndHydrateConfigModel` feeds BOTH the
  publish extract stage and the store leg `emittedSeedPlan` (parity). Named
  skip `data.hydration.skippedFileSourced` for file-sourced models.
- **step 5** per-lane goldens reconciled (no new artifacts — see below).
- Plus the golden corpus reshaped to **one maximal `master/` + small
  standalone one-offs** (`2ca4374` delta layout → `f1ca2a3` take-2),
  operator-directed minimize-surface.

**The golden corpus, as it stands.** `master/` is the one full standalone
emission (all catalog variants incl. the delete-scope arm folded in);
`pruned-platform-auto/` is a 2-file one-off over a tiny catalog
(`GoldenCatalog.prunePlatformAutoCatalog`). The data lane in `master` is the
fused `Data/seed.sql` only (single static lane → the ≥2 rule omits per-lane
files); it already pins the static MERGEs, the `Tier` IDENTITY bracket, and
the `ScopedLookup` delete arm. **Do not add per-lane `Data/*.sql` goldens** —
they'd byte-duplicate `seed.sql`; the per-lane split is witnessed in
`DataEmissionComposerTests` (a 2-lane catalog), which is the right altitude
(the golden path supplies no migration/bootstrap context).

**Caveats — both prior ones are now RESOLVED (Docker was auto-repaired
mid-session):**
1. **Docker pool: GREEN (231/231).** The warm container came back, so the
   pool ran — the WP5 ReadSide dual-read round-trip (deploy `Projection.*` →
   read back → recover) and the WP6 leveled deploy are now Docker-witnessed,
   plus CDC silence. Fast pool with the warm container is fully green
   (**3161/0/211** — the three formerly-"env-gated" extraction classes now
   execute and pass).
2. **The live OSSYS hydration stream: WITNESSED.** A Docker-gated test
   (`IngestionIntegrationTests` — `hydrateCatalog … via BOTH env: and file:
   ossys refs`) seeds a static table and runs `hydrateCatalog` against the
   container; the `Static []` marker fills with the seeded rows. `model.ossys`
   accepts BOTH `env:` and `file:` refs (the `file:` form is the operator's
   predominant one and is NOT deprecated); the skip diagnostic
   (`data.hydration.skippedNoLiveSource`) fires only for `model.path` (the
   JSON fallback, no live source) — never for a `model.ossys` `file:` ref.
   The ONE residual: the dual-window MIGRATE edge (rebinding the renamed
   identity property on a legacy `V2.*` deployed schema) — trigger-gated on
   legacy-name retirement.

**The named follow-up:** hydration uses the **marker approach** (rows graft
into `Modality.Static`, indistinguishable from authored). The armed
`ReadbackPopulated` provenance-typed-Static closed-DU change (keeps authored
vs hydrated distinguishable) is the immediate follow-up — a codec-totality
blast across every round-trip surface; its DECISIONS amendment comes first.
Hydration only FILLS existing `Static` markers (never mints new ones), so it
does not reintroduce the 4.4 trap.

**Rhythm that held:** DECISIONS entry before each step's code; golden diff in
the same commit as the emission change; fail-then-bless-then-INSPECT (a
missing `;` in the IDENTITY bracket was caught only by reading the bytes);
both books amended per step; fast pool green (delta = only the env-gated
classes) after every step. Hold the spine.

---

# Handoff addendum — 2026-06-13, WP6 mid-slice: steps 1+2 CLOSED (the MERGE lane brackets IDENTITY_INSERT; Bootstrap delegates and goes Active — the last `NotImplementedInV2` is gone); steps 3–5 remain; the Docker pool is OWED

To the next agent.

You are inside slice 5 (WP6, the data lanes). The pre-scope letter
immediately below is still your seam map for what remains — trust it,
re-grep the line numbers. Two of its five steps are now SHIPPED on this
branch (`claude/dreamy-pasteur-akjbx3`); read this letter first, then drop
to the pre-scope for steps 3–5.

**Step 1 — IDENTITY_INSERT bracket — CLOSED (`68b3506`).**
`StaticSeedsEmitter.kindToScript` dispatches on `load.Disposition`: an
`IdentityDisposition.AssignedBySink` kind's Phase-1 MERGE is wrapped
`SET IDENTITY_INSERT [t] ON; <merge>; SET IDENTITY_INSERT [t] OFF; GO` as
**one GO segment**. The single-batch shape is load-bearing, not cosmetic:
`Deploy.executeBatchParallel` opens a *fresh connection per GO-segment*
(Deploy.fs:485) and the toggle is session-scoped — split the bracket across
GO and the toggle lands on a different connection than the MERGE. The PK is
KEPT (the MERGE's ON joins on it); the slice-E "suppress the PK" note is
overturned. The bracket lives inside `RenderedPhase1`, so the fused-≡-leveled
partition law holds untouched. Trap I hit, now documented in the code:
`ScriptDomGenerate.generateOne` does NOT terminate a `SET IDENTITY_INSERT`
(verified against recorded bytes — terminator behavior is statement-type
specific, not `IncludeSemicolons`), so each segment is `;`-terminated
explicitly. `GoldenCatalog` gained `Tier` (the first IDENTITY-PK static);
goldens re-recorded; `THE_GOLDEN_EMISSION.md` §4 row flipped to
COVERED+BLESSED. **Discipline that saved me: fail → re-record → INSPECT the
bytes → re-verify. The first re-record had `SET … ON` with no `;` and the
golden negative-invariants did not catch it — only reading the seed bytes
did.**

**Step 2 — Bootstrap delegation — CLOSED (`9be312c`).**
`BootstrapEmitter.emitFromPlan` now delegates to
`StaticSeedsEmitter.emitFromPlanWith None` (A40 — same algebra over the same
`DataLoadPlan`); the slice-ζ empty stub + its `emptyScript` are gone.
`emitWithTopo` wires the `UserRemapContext → SurrogateRemapContext`
conversion (`UserRemapContext.toSurrogate` via the discovered user kind,
mirroring `MigrationDependenciesEmitter.buildPlan`). Registry `Status`
flipped `NotImplementedInV2 → Active` via the `emitter` helper, adding a
DataIntent `bootstrapRowsProjection` site beside the OperatorIntent
`userRemapBootstrap`. **This was the last `NotImplementedInV2` in the
codebase — `grep -rn NotImplementedInV2 src` now finds only the DU
definition, its validator, and the render/digest projections; every
registered transform is `Status = Active`.** The step is BYTE-STABLE: the
bootstrap row source is still `Map.empty` (the per-kind graft is step 4), so
the lane emits empty and the golden corpus did not move. **A LAW for step
4, named so you don't rediscover it:** Bootstrap's hydrated row source MUST
be the complement of (Static-populated ∪ Migration-context) kinds — feed it
a kind another lane also populates and the composer's
`OverlappingEmitterCoverage` assertion escalates to a production `invalidOp`
(`Pipeline.fs:604-607`).

**What remains: steps 3, 4, 5 (see the pre-scope below for the seams).**
Step 3 (per-lane `Data/StaticSeeds.sql` / `Bootstrap.sql` / `MigrationData.sql`
out of `SiblingArtifacts` pre-union) and step 5 (the per-lane goldens) are
PURE-witnessable through the golden corpus and need no live connection — they
can land next without Docker. **Note the golden corpus was reshaped (DECISIONS
2026-06-13 take 2 + `THE_GOLDEN_EMISSION.md §3`):** it is now one maximal
`master/` (the full Platonic catalog under a kitchen-sink config — delete-scope
is FOLDED IN, since it resolves per kind) plus small standalone one-offs for
genuinely-global flags (today just `pruned-platform-auto/`, a tiny catalog).
So step 3's per-lane data files (`Data/StaticSeeds.sql` etc.) land in
`master/Data/` only — there is no second data scenario to keep in sync. Step 4 (hydration in `runWithConfig`) is the
big one and the only step that wants a live OSSYS connection; the marker /
`ReadbackPopulated` choice and the `projectSeedPlan` parity duty are in the
pre-scope. The IDENTITY_INSERT bracket already rides Bootstrap's delegation,
so step-3 lane outputs inherit it for free.

**THE DOCKER POOL IS OWED — read this.** The sandbox's Docker daemon was
reachable at session open (a `projection-mssql-warm` container was up) then
went away mid-session (SQL port closed; `warm-sql.sh restart` itself needs
Docker). Consequence you will also see: the three Docker-gated extraction
classes that live in the FAST pool — `BtReferenceFkFlowTests`,
`OssysComprehensiveFixtureTests`, `OssysExtractionCanaryTests` — *self-skip*
when Docker is absent (fast pool green) but *error* with "Could not open a
connection" when Docker is intermittently present and `ensureRunning()`
memoizes true before SQL is reachable (the flaky 27–28 connection failures;
survival-rule-2 family). They are environmental, orthogonal to WP6, and
unchanged by steps 1+2. **Steps 1+2 are witnessed by the PURE pool only**
(GoldenEmission + the data-lane composer/registry tests — the
operator-blessing surface for an emission change). `scripts/test.sh docker`
could NOT be run this session; run it before merge.

**Rhythm that held:** DECISIONS entry before the code (both steps); golden
diff in the same commit as the emission change (step 1); fail-then-bless-
then-INSPECT; both books amended with the slice. Hold the spine.

---

# Handoff addendum — 2026-06-13, SESSION CLOSE: the reconciliation program is four slices deep, every pool green, and slice 5 (WP6, the data lanes) is pre-scoped to the line

To the next agent.

You are inheriting the V1 full-export reconciliation program at full
stride. Read `V1_FULL_EXPORT_RECONCILIATION_PLAN.md` FIRST (the
research record, the nine work packages, the four operator
adjudications C1–C4), then `THE_GOLDEN_EMISSION.md` (the blessing
protocol you will live inside). Four slices shipped this session —
`cd11d93` (logical-only inverse edges + the HasDbConstraint carve-out),
`ab5df9d` (EmissionPolicy channel collapse + the golden corpus
founding), `2749ee4`/`8371a31` (operator blessings #1 and #2: V1
per-table form, inline FK/CHECK ladders, the constraint stack, the
128-char identifier budget), `e16aab9` (scope pushdown + the
equivalence law + the dangling-reference fix). Every slice closed with
fast AND docker pools green (latest: 3150/0 and 231/231). The
operating rhythm that made that true: DECISIONS entry FIRST, golden
diff in the SAME commit as the emission change, matrix/charter
amendments with the slice, both pools before push.

**Your slice is WP6 — the data lanes.** The operator's directive is
standing in `THE_GOLDEN_EMISSION.md` §4: per-lane golden artifacts
(`Data/StaticSeeds.sql` / `Data/Bootstrap.sql` /
`Data/MigrationData.sql`) land in the SAME commit as the lanes fill.
The full seam map below is current at `e16aab9` — verify line numbers
with a grep, then execute.

**Step 1 — IDENTITY_INSERT bracket (strictly first; ~half day).**
`StaticSeedsEmitter.kindToScript` (StaticSeedsEmitter.fs:276–332)
never reads `load.Disposition` — dispatch on
`IdentityDisposition.AssignedBySink` (minted structurally at
DataLoadPlan.build:106 via `IdentityDisposition.ofKind`,
SurrogateRemap.fs:78–83) and bracket `RenderedPhase1` with
`ScriptDomBuild.buildSetIdentityInsert` (ScriptDomBuild.fs:1187–1195,
raw node → `ScriptDomGenerate.generateOne` → the `;\nGO\n` framing the
partition law depends on). Bracket INSIDE the per-kind rendered string
— a bracket outside it breaks the P2 leveled-partition property
(DataEmissionComposerTests.fs:594–721). PK-suppression is WRONG for
the MERGE lane (the ON clause joins on the PK). Precedent:
StaticPopulationEmitter.fs:95–116. Add an IDENTITY-PK static kind to
GoldenCatalog (the statics are all `pkAttr … false` today, so no diff
shows until you do) + re-record + DECISIONS note.

**Step 2 — Bootstrap delegation (~1 day).** `BootstrapEmitter
.emitFromPlan` (BootstrapEmitter.fs:76–82) discards its plan — body
becomes a delegation to `StaticSeedsEmitter.emitFromPlanWith`
(StaticSeedsEmitter.fs:364–378; signature-identical modulo the scope).
The UserRemap conversion already exists: `UserRemapContext.toSurrogate`
(UserRemap.fs:183–200), worked example at
MigrationDependenciesEmitter.buildPlan:412–428. Flip
`registeredMetadata.Status` from `NotImplementedInV2` to `Active`
(BootstrapEmitter.fs:141–143) or the totality property tests bite.
Bootstrap's "remaining kinds" set MUST be the complement of
(Static-populated ∪ Migration-context) kinds — an overlap is
`OverlappingEmitterCoverage` which Pipeline.fs:604–607 escalates to a
production `invalidOp`. Rewrite the empty-no-op pins
(BootstrapEmitterTests.fs:83–100).

**Step 3 — Per-lane outputs (~1 day).** The per-lane rendered strings
already exist BEFORE the union: `SiblingArtifacts`
(DataEmissionComposer.fs:60–65) out of `dispatchSiblings` (:98–128).
Render each sibling in `topo.Order` exactly as `composeRenderedFull`
does (:318–331) — render ONCE from one dispatch, never twice. The
decoration (Pipeline.fs:596–607) adds the three keys beside
`Data/seed.sql`; `writeAllToStaging` iterates `DataBundle`
generically (zero writer changes). Keep `Map.isEmpty` when the flags
are off (FullExportDataBundleTests.fs:67–86 pins it).

**Step 4 — Hydration (the big one; 2–3 days).** The graft point:
`runWithConfig` is a staged task (Pipeline.fs:1318–1378); add a staged
step between extract and emit on the `acquireProfile` template
(:1015–1032) — `runWithConfigCore` is deliberately sync (FS3511; its
docstring :1034–1038), so hydration lives in the async caller. Row
source: open a SECOND connection from `cfg.Model.Ossys` via the
`LiveModelRead.fromConnSpecWith` Substrate pattern
(LiveModelRead.fs:83–100; the model-read connection is use-disposed —
not reusable). Stream per owned kind via `Ingestion.streamKindRows` /
`collectInOrder` (Ingestion.fs:19–71 — already FS3511-safe; scope it
to owned kinds, NEVER `ReadSide.read` — survival rule 8 marks
everything Static). Replace the `Static []` marks
(OssysRowsetReader.fs:581) with hydrated rows;
`NormalizeStaticPopulations` sorts them deterministically for free if
you graft pre-chain (graft by SsKey — rename-invariant, A1).
**Parity duty:** hydration must also reach `projectSeedPlan` /
`emittedSeedPlan` (Pipeline.fs:1407–1446) or the deployed seed drifts
from the published one. File-sourced model + data flags on ⇒ a NAMED
skip diagnostic, never silent emptiness. The `ReadbackPopulated`
provenance-typed-Static closed-DU change is the structural close
(armed at CONSTELLATION_BACKLOG.md:796–799; the plan declares its
trigger fired) — codec-totality blast radius; its DECISIONS amendment
comes FIRST and it is acceptable to land hydration with the marker
approach + the DU change as the immediate follow-up if the blast
proves large mid-slice.

**Step 5 — Per-lane goldens (same commit train as 3/4).** GoldenCatalog
gains the lane variances (a bootstrap-owned kind, a migration-row
kind, the IDENTITY-PK static from step 1); `GOLDEN_RECORD=1` +
DECISIONS note per the blessing protocol.

**Traps, beyond the survival list:** (1) the Platonic catalog's
self-FK (`Engagement.ParentId`) puts the data composer's
`TreatAsCycle` topo into Alphabetical mode — the gen-7 residual
("fused-path alphabetical MERGE order can violate non-cycle FK chains
among seeded kinds — loud, not silent") becomes LIVE once real rows
flow; watch RegionA/B Phase-2 ordering in the golden diffs. (2)
Compose data over the POST-chain catalog only (gen-7 trap (a)). (3)
The golden comparator fails on artifact-set drift — three new files
per scenario are EXPECTED failures until re-recorded with the note.

**After WP6, the queue:** WP5 (the C1 `V2.*` → domain-name rename +
gate — its golden diff is the worked example of the blessing
protocol; dual-read window in ReadSide), WP7-remainder (logical
IX/UIX synthesis; trigger-definition rewrite; >128 PK golden example),
WP8 (Order_Num — registered pass per C3; the rowsets-SQL divergence
gets a header citation), WP9 (sample-config rewrite; the
`resolveFlowSpec` ossys-only provenance-arm fix at
MovementSurface.fs:917–923; the reserved `projection compare` verb —
matrix row 41's shape, trigger fired). The EXCEPT validate-before-apply
mode (C2: opt-in) rides WP6's lane work or its own slice. J5 — a
writable UAT connection — still trumps everything.

Run `scripts/test.sh fast` early and often; docker pool before every
push (the warm container is your friend; survival rules 1–4 for its
failure signatures). Hold the spine.

---

# Handoff addendum — 2026-06-13, reconciliation slice 4 CLOSE (the scope pushes down to the OSSYS read; the equivalence law; the dangling-reference fix)

To the next agent.

**Slice 4 (WP3; adjudication C4) shipped** (`DECISIONS 2026-06-13`
slice-4 entry — it AMENDS the 2026-05-16 "filtering is an IR concern"
stance for the scope axis): `SnapshotScopeBinding.fromModel` binds
`model.modules` + entity narrowing + the include flags into the
adapter's `SnapshotParameters` (A7 opt-in gate mirrored verbatim from
`ModuleFilterBinding`; `OnlyActiveAttributes` deliberately NOT pushed);
`LiveModelRead.fromConnSpecWith`/`fromConnectionWith` are the
scope-bearing faces; `readConfigModel` binds them on the full-export
path; `ModuleFilter.apply` REMAINS the semantic seam (double
enforcement — V1's own precedent). The LAW:
`scopedRead(scope) ≡ ModuleFilter.apply(scope) ∘ fullRead`,
Docker-witnessed against the 3-module edge-case seed.

**The law's first run exposed a latent integrity gap**: `apply` did
list surgery without restoring the aggregate invariant — kept kinds
referencing excluded modules carried DANGLING references
(`Catalog.create`-unconstructible values). Fixed at BOTH legs with one
defined semantic: a declared scope excludes its cross-scope edges
exactly as it excludes the kinds they point at (`apply` step-5 prune;
bundle-grain prune under a pushed scope; unknown-`RefEntityId` rows
kept so corrupt sources still fail loudly). Named consequence: the
missing-target diagnostics are structurally unreachable through the
scoping path now; the per-edge `moduleFilter.referencePruned` witness
lands when the filter seam gains a diagnostics channel.

**Operator directive (2026-06-13), now standing in the charter**: when
WP6 lands, the golden corpus grows PER-LANE data artifacts
(`Data/StaticSeeds.sql` / `Data/Bootstrap.sql` /
`Data/MigrationData.sql` + the fused global seed) in the same commit —
`THE_GOLDEN_EMISSION.md` §4's data-lane expansion section is the
reminder; the Platonic catalog gains the lane variances then.

Queue: slice 5 = WP6 (data lanes — IDENTITY_INSERT strictly BEFORE
hydration; bootstrap delegates to the static-seeds renderer; per-lane
outputs + the per-lane goldens above). Then WP5 (C1 rename + gate),
WP7-remainder/WP8, WP9. J5 still trumps everything.

---

# Handoff addendum — 2026-06-13, reconciliation slice 3b CLOSE (operator blessing #2: the column-constraint stack; inline CHECKs; composite indexes; the identifier budget)

To the next agent.

**Slice 3b shipped on the operator's second blessing pass**
(`DECISIONS 2026-06-13` slice-3b entry; commit `8371a31`; fast pool
3146/0, docker pool 231/231): single-column CHECKs attach beneath
their attribute (`attachInlineCheck`, structurally anchored — exactly
one referenced column ⇒ inline, else table-level); the
ConstraintFormatter's per-kind splitters are REPLACED by the
column-constraint STACK segmenter (top-level paren/bracket/quote-aware
scan; any DEFAULT/CHECK/PK/FK combination on one column renders as one
statement, every segment laddered, comma on the last);
`IdentifierBudget.fit` (Core) caps generated FK/PK names at 128
(115-char head + `_` + 12-hex SHA-256 of the full name; matrix row 57
length-cap cashed out); the Platonic catalog gained composite-PK
`Assignment`, composite + mixed-direction indexes on `Engagement`, the
DEFAULT+FK and DEFAULT+CHECK stacks, a multi-column table-level CHECK,
and the long-name `Ledger → EcrmSnapshot` pair whose hashed 128-char
FK name is golden-visible.

Watch for: (1) the formatter's old single-constraint splitters are
GONE — anything resembling them in stale branches will conflict; (2) a
>128 generated PK name has the budget applied but no catalog example
yet (needs a >120-char table name; inventory TODO); (3) trigger
DEFINITIONS still carry physical table/column names (inventory TODO,
own slice).

Queue unchanged: slice 4 = WP3 (scope pushdown into the OSSYS read).
Then WP6 (data lanes — IDENTITY_INSERT strictly before hydration), WP5
(C1 rename + gate), WP7-remainder/WP8, WP9. J5 still trumps everything.

---

# Handoff addendum — 2026-06-13, reconciliation slice 3 CLOSE (operator blessing #1: per-table V1 form; inline FK ladder; CHECK/filter logical rewrite; the catalog consolidates)

To the next agent.

**Slice 3 was the corpus's first yield** (`DECISIONS 2026-06-13`): the
operator's blessing pass over the goldens redirected the queue (WP3
scope pushdown moves to the next slice — operator blessing outranks).
Four changes: (1) per-table `SsdtFile` bodies render through
`Render.toText` — framed GO BETWEEN statements (never trailing), the
constraint ladder, the wrapped EXEC, newline-terminated; the per-kind
no-GO pin is OVERTURNED by operator decision and rewritten. (2)
Single-column FKs attach inline beneath their source column
(`attachInlineForeignKey`, the LR3 sibling) and the formatter gained
the column-suffix FK ladder (+4/+8/+12) with V1's NO ACTION fill/drop
normalization. (3) `LogicalColumnEmission` v2 follows the substitution
into CHECK definitions and index FILTER predicates (trigger bodies
still carry physical names — own slice; inventory TODO). (4) The
Platonic catalog consolidated: master `ScalarGallery` (every scalar ×
its DEFAULT literal + checks + trigger + the index gallery), master
`Engagement` (every reference variance including the SELF-referencing
FK), pure targets, `Heap`, Statics unchanged. Goldens re-recorded
under the DECISIONS note.

Watch for: four 5.13-era pins were updated to the rendered-body
contract (the OnUpdate fill convention + the CHECK two-line ladder) —
any other test asserting RAW per-table body shapes will collide with
the formatter; pin against the laddered form.

Queue: slice 4 = WP3 (scope pushdown into the OSSYS read; C4
adjudicated). Then WP6 (data lanes — IDENTITY_INSERT strictly before
hydration), WP5 (C1 rename + gate), WP7-remainder/WP8, WP9. J5 still
trumps everything.

---

# Handoff addendum — 2026-06-12, reconciliation slice 2 CLOSE + THE GOLDEN EMISSION adopted

To the next agent.

**Slice 2 (WP4 + WP7-GO) shipped** (`DECISIONS 2026-06-12 — Slice 2`):
the `projectWith*` family lost its second `EmissionPolicy` channel
(`fullPolicy.Emission` is the one channel; every config-driven
`EmissionPolicy.empty` literal dissolved);
`emission.includePlatformAutoIndexes` is config-reachable (default
true; CONFIG_REFERENCE updated); `BatchSeparator` renders `\nGO\n\n`
(V1's blank-both-sides framing; `aggregateSsdt` joiner aligned). Fast
pool green; Docker pool 231/231 green on the warm container.

**THE GOLDEN EMISSION is live** (`THE_GOLDEN_EMISSION.md`; DECISIONS
entry of the same date): the Platonic catalog (`GoldenCatalog.fs`,
every expressible emission variance), three scenario configs, the
byte comparator + `GOLDEN_RECORD=1` blessing protocol, and the
committed corpus at `tests/Projection.Tests/Golden/`. The first
recording already found two real things: delete-scope terms resolve
POST-chain (logical column names under the default rendition — doc
mismatch recorded), and per-table bodies lack trailing newlines. From
now on: **a slice that changes emission lands with its golden diff in
the same commit** — the diff is the operator-blessing surface. The
known-unblessed inventory rows in §4 are the reconciliation plan's
worklist intersected with the corpus.

Queue unchanged otherwise: slice 3 = WP3 (scope pushdown), then WP6
(data lanes — IDENTITY_INSERT strictly before hydration), WP5 (C1
rename + gate — its golden diff will be the worked example of the
blessing protocol), WP7/WP8, WP9. J5 still trumps everything.

---

# Handoff addendum — 2026-06-12, reconciliation slice 1 CLOSE (inverse references are logical-only edges; the FK decision layer reads `HasDbConstraint`)

To the next agent.

**A new program opened today and its first slice is DONE.** The operator
ran the full-export flow end-to-end in a managed OutSystems environment and mapped the
problem series; the resulting research record and nine-package program
live in `V1_FULL_EXPORT_RECONCILIATION_PLAN.md` — read it BEFORE touching
anything on the full-export surface; it captures the code-truth findings
(file:line), the standing-law constraints, and four operator-adjudicated
collisions (C1: rename the `V2.*` extended properties to domain
terminology; C2: CDC-silence canonical, EXCEPT validate-before-apply as
opt-in fallback; C3: Service-Studio ordering as a registered pass; C4:
adapter-time scope pushdown for declared scopes, `ModuleFilter` stays the
semantic owner).

**Slice 1 (WP1+WP2) shipped** (`DECISIONS 2026-06-12 — Slice 1 of the
full-export reconciliation`; matrix rows 191–192 + row-57 amendment):
`Reference.isDeployable`/`isInverse` are the single definition site;
`ForeignKeyPass` (v3) and every `SsdtDdlEmitter` constraint surface
exclude the inverse class (navigation keeps the closure; flag
inheritance retained); `ForeignKeyRules.evaluate` gained the V1
carve-out (MissingTarget → HasDbConstraint ⇒ Enforce → PolicyDisabled →
profile) and lost the producer-less `TrustedConstraint` branch; the
decision-drop audit splits `decision.fkDropped` (Warning, source-backed)
from `decision.fkNotIntroduced` (Info, logical-only); a schema-scoped
FK-name collision tripwire (Error) rides the config-driven diagnostics.
Witnesses in `DeployableReferenceTests.fs` (post-closure AND full-chain —
the first canaries that emit from a post-chain catalog; keep that
discipline for any pass that rewrites the catalog). Fast pool green
(3,132 passed / 0 failed).

**The queue is the plan's §5**: slice 2 = WP4 (collapse the
`EmissionPolicy` two-channel seam; wire the dormant emission flags) +
the `BatchSeparator` trailing-blank fix with golden refresh; then WP3
(scope pushdown), WP6 (data lanes — IDENTITY_INSERT strictly BEFORE
hydration), WP5 (the C1 rename + gate), WP7/WP8, WP9. Books obligations
per slice are in the plan's §8. J5 still trumps everything.

---

# Handoff addendum — 2026-06-12, generation 7 CLOSE (P2 is WIRED: the load leg deploys the seed leveled-parallel; the mint gained its mode guard)

To the next agent.

**P2 is DONE — the wire landed on the gate generation 6 met.** The
production data face was FOUND, not assumed: the card's "fused
schema+seeds" premise about `runDeploy → runEphemeral` was the probe's
error — `aggregateSsdt` joins `SsdtBundle` (schema files only;
`Data/seed.sql` lives in `DataBundle` and never rode the deploy face),
so face (a) is REFUSED on the card (nothing data-shaped to level;
schema leveling stays P3, trigger-held). The real face is the
full-export load leg, whose old shape (`executeBatch sink <fused
seed>`) was exactly the measured gate's losing leg. Now:
`runFullExportLoad` → `Compose.runWithConfigAndLoad` (executor seam
re-threaded: inject `Deploy.executeLeveledSeed <connection string>` —
partial application carries the string for per-segment pooled opens;
`sink` stays the CDC measure's connection) →
`Compose.loadLeveledSeedAndRecord` → `Deploy.executeLeveledSeed`, the
ONE owner of the leveled order (Phase-1 levels then Phase-2, levels
sequential, within-level concurrency by the token, parallelism via
`resolveParallelism`). Faithfulness is a LAW: the leveled plan
PARTITIONS the published seed (GO-batch segment-multiset equality,
property-witnessed in both ordering modes); the fused
`loadSeedAndRecord` stays as the published-artifact contract witness.

**The wire surfaced a mint defect — fixed at the mint.** Under
`Mode = Alphabetical` (any unresolved cycle; ONE self-FK kind anywhere
suffices), `TopologicalOrder.levels`' "unknown parent contributes 0"
rule collapsed real FK chains into ONE ParallelSafe group — the
token's no-edge-within-group law was violated at its only mint, and
the wire would have promoted that to CONCURRENT execution. `levels`
now licenses multi-member groups only under `Topological`; degraded
modes yield singleton groups in order (≡ the sequential deploy,
exactly). A named residual rides the DECISIONS entry: the FUSED path's
alphabetical MERGE order under that mode is a PRE-EXISTING hazard for
non-cycle FK chains among seeded kinds (loud FK error, not silent);
cure trigger = a real catalog hitting it (cycle-resolution reach, not
deploy-order surgery).

**Witness state at close (this host runs ~20% slower than gen 6's —
calibrate before comparing):** inherited fast pool replicated EXACTLY
at open (3116/0/211); with the wire, fast 3121/0/211 at 137s (+5 pure:
partition law, level precedence, singleton degrade, isEmpty parity,
the mint-mode witness) and docker 231/0 at 374s (+2 over the inherited
229 = gen 6's gated leveled-deploy scenario fact + the new live leg —
confirmed EXERCISED, not soft-skipped, per §4 rule 12); comprehensive
canary gate-open 1/1 at 4m02s, empty PhysicalSchema diff, leveled
target-deploy at parallelism 4 with 3 real Phase-1 levels (the guard
did not degrade the acyclic path); `perf-harness.sh run leveled-deploy`
replicated 2.78× post-wire (1932 ms → 696 ms, parallelism 4 — inside
the gate's 2.59–2.85× band); readside-rowstream 1042 ms / 206 ms at
100k×12 (above the 869/165 band by the same ~20% this host runs
everything; elements exact at 100000); perf-gate CLEAN solo (132
labels; baseline NOT re-recorded — no floor moved); lint surface
byte-identical to the clean tree.

**Traps from this session:** (a) the pass chain rewrites `Physical` to
the LOGICAL name (the D.1.a move) — a fixture whose KindName and
PhysicalTable differ deploys under the KIND name; the existing X1
tests only worked because theirs coincide; compose any test plan over
the POST-CHAIN catalog (`finalState.Catalog`), as `projectSeedPlan`
does. (b) §4 rule 12 bit live: my first leveled-leg run "passed" while
soft-skipping on a swallowed CDC-enable error — grep the live log for
the SKIP marker before counting a docker witness. (c) NEW §4 rule 13:
a perf-gate verdict taken while anything else runs on the host is VOID
— the CPU-bound tier false-tripped 3.3× over baseline during a
concurrent build; the solo re-run was clean on the same tree.

**Your queue, by the backlog's graph:** the Voice lane's named
remainder (migrate success verdicts the natural small face; transfer
narration wants TransferReport → Surface; explain/suggest want
View/Surface documents — reasons in the lane's commits), P3 stays
trigger-held, the §6 armed items keep their wake conditions (F1-hex
unchanged — `digestOf` untouched). J5 — a writable UAT connection —
still trumps everything.

Hold the spine; balance the books; keep the patient breathing; re-run
the witnesses you inherit before you stand on them — mine are one
command each: `scripts/test.sh fast`, `scripts/test.sh docker`,
`perf-harness.sh run leveled-deploy`, `perf-harness.sh run
readside-rowstream`, `scripts/perf-gate.sh` (solo, per rule 13).

— Generation 7, the wire-layer, 2026-06-12

# Handoff addendum — 2026-06-12, generation 6 SECOND POSTSCRIPT (P2's gate is MET: 2.6–2.9× at the operator envelope; the wiring slice is yours)

The half-met gate in the postscript below is now FULLY MET. The
declared `leveled-deploy-150x42` scenario (H7-disciplined: in `all`,
the registry, the gated fact; `StaticCatalogFixtures` gained the
N-kind form rather than a fifth instance) ran the paired comparison at
the operator envelope through the REAL path — `composeRenderedLeveled`
→ `ParallelSafe` → `executeBatchParallel` under `resolveParallelism` —
and replicated **2.85× / 2.59× / 2.65×** (sequential ~2.0–2.1s →
parallel ~0.74–0.79s, parallelism 4). One command:
`perf-harness.sh run leveled-deploy`.

**What remains of P2 is the WIRE, deliberately left whole for a fresh
session** — the design constraints are on the card so you don't
re-derive them: (a) `runDeploy → runEphemeral` deploys `aggregateSsdt`'s
FUSED schema+seeds — a schema-vs-data split there must stay faithful to
the published bundle, never a re-composition that can diverge; (b) the
full-export load leg injects a `SqlConnection -> string -> Task`
executor while `executeBatchParallel` needs the connection STRING for
per-segment opens — re-thread the seam. Witnesses as carded:
operator-reality canary + perf-gate (baseline re-record only if a
floor legitimately moves, with its DECISIONS amendment).

— Generation 6, the contract-keeper (second postscript), 2026-06-12

# Handoff addendum — 2026-06-12, generation 6 POSTSCRIPT (P1 is CUT: ParallelSafe is minted by levels; P2's gate is half-met, named)

A continuation after the letter below — read it as that letter's coda.

**P1 is DONE.** `ParallelSafe<'a>` lives in Core beside its one mint
(`TopologicalOrder.levels`); `map`/`choose` carry the proof through
the composer (the cross-kind `String.Concat` + LINT-ALLOW retired);
`Deploy.executeBatchParallel` DEMANDS the token — miswiring is now a
compile error; the stale "canary continues using sequential" status
note is retired (RI-5). Segment bytes, bench labels, and the
manifest's wire shape are all unchanged by construction. The leveled ≡
sequential equivalence ran GATE-OPEN
(`PROJECTION_RUN_COMPREHENSIVE_CANARY=1`, 1/1 at 4m17s — remember §4
rule 12: that canary soft-skips in the docker pool; open the gate when
its verdict matters). Pure pool 3116/0 (31s) at the commit.

**P2 is NOT cut, deliberately.** Its gate demands an operator-scale
measurement through the production face; what exists is segment-level
evidence (the 20-table microbench: 782ms → 411ms, 1.90× at
parallelism 4 — printed by the ExecuteBatchParallelTests bench, now in
the docker pool's standing output). Run the operator-envelope
before/after (6.25k×150 through the CLI deploy path) BEFORE wiring;
the backlog card carries this gate state. P3 stays trigger-held.

The queue from here: P2 behind its measurement, the Voice lane's named
remainder, the §6 armed items. J5 still trumps everything.

— Generation 6, the contract-keeper (postscript), 2026-06-12

# Handoff addendum — 2026-06-12, generation 6 CLOSE (the contract leg is CLOSED: L1→L4 + R1a→R1e; the Voice lane advanced; the pools pay for work)

To the next agent.

The books balance. What you inherit, in order of weight:

**1. The ledger contract is LIVE (L1→L4) on the RI-3 admission split.**
`LedgerSpec` + the `Verified<_>` proof token in Core (`Ledger.fs`);
`writeAdmit` (external witness — B′≡B at `recordVerified`; the
journal's commit-point position) vs `resumeAdmit` (recomputed-vs-stored
fingerprint; drift is typed, mapped onto the unchanged
`transfer.resume.sourceDrift`). The journal instance adapts the
effectful remap fold (Genesis = the shared in-flight accumulator); the
episode instance names load-time verification as chain structure,
honestly; G10 is the degenerate single-quantum instance, retired as a
second mechanism. Two card corrections are named in the backlog (L2's
resumePoint→resumeAdmit-per-chunk; the positional WriteAdmit). F1-hex
stays ARMED — `digestOf` was never touched.

**2. The Run is COMPLETE and WIRED (R1a→R1e).** Capture lives at the
ONE bracket owner (`RunEnvelope.bracket`, the "wire once" clause
realized literally): under `PROJECTION_LEDGER_DIR`, every bracketed
run persists its aggregate — crashed bodies included, no orphan
RunIds. Transfer/reverse-leg/migrate/migrate-with-data/synth-load
moved under `withRun`; `runReadiness`'s orphan beginRun is retired
(face-bracketed, no ledger append per its contract); bench keys by
RunId; `projection inspect <runId> [<runId>]` renders the store and
the `Run.diff` delta surface — where the §9.7 UoM promotion FIRED
(`[<Measure>] ms`, scoped exactly as gated). R1e's law is witnessed:
stored-trail board ≡ live board (mixed run), readiness over RunHistory
≡ the ledger projection. The card's `diff <a> <b>` name collided with
the shipped refs-diff verb — landed under `inspect`, named.

**3. The pools pay for work, not ceremony — and §4 rule 2's third
signature has a ROOT CAUSE.** The flat ~3.4s ephemeral-test floor was
the DROP killing the idle pooled per-DB session (3051ms measured; 51ms
evicted) — `ClearPool` lands before every drop. The warm container was
dying of LEAKED databases (`withBootstrappedDatabase` never dropped;
209 counted) — it reaps now; a full fast run adds zero. The pool split
follows the Collection ATTRIBUTE, not the filename — the substring
trap had broken both ways (six Docker tests in the parallel fast pool;
six pure AxiomTests in the serial docker pool; set-diff proved the
6↔6 swap, nothing dropped). Build skips when the test DLL is newer
than every input. **fast: 58–71s → 31s; docker: 925s → 383s (229/0);
every passing pool prints its five slowest.** `runEphemeral` is
deliberately unchanged (deploy-to-docker leaves an inspectable DB by
design). The remaining docker wall is ~71s of 100k sustained-envelope
MEASUREMENT — witnesses, not overhead.

**4. The Voice lane advanced in parallel (a worktree subagent,
reviewed against the register before merging).** Seven faces voiced
(full-export --load, deploy's SSDT-rejection worked example, the
canary pair, drift, the migrate stop channel with
`Voice.migrationStopDetail` as a typed projection, eject,
verify-data); 21 codes, catalog 20→41, both totality registries per
commit, exits byte-identical, NDJSON untouched. RunFaces raw-print
census 148 → 80; the remaining faces (transfer narration — wants a
TransferReport→Surface design, not flat templates; explain/suggest —
View/Surface documents; migrate success verdicts) are deferred with
named reasons in the lane's commits.

**Witness state at close:** pure pool 31s green at every commit
(3115/0 at the head under the corrected split — six axiom tests came
HOME to the fast pool, six Docker tests went home to docker); full
Docker pool 229/0 (383s) at the head; the inherited witnesses were
re-run at open per the law (pure 3078/0/211 exact; Docker 229/0 at
925s pre-fix; readside-rowstream 869ms/165ms — inside generation 4's
band, four hosts deep). One infra flake en route, named and §4-filed:
a 27-failure pre-login-handshake batch (rule 2's fourth signature;
restart → focused re-run green → full green).

**Traps from this session:** (a) `Assert`-era F# files can shadow
`System.DateTime` — qualify it in new test literals (two FS0003s cost
minutes). (b) The paren-`use` eviction form trips FS0792 inside a task
CE's `finally` — use let-bind + explicit Dispose. (c) Editing test.sh
while a pool RUNS risks corrupting the in-flight bash — stage to /tmp,
swap after the verdict.

**Your queue, by the backlog's graph:** P1→P2 (licensed parallelism —
`ParallelSafe` minted by `levels`; P3 stays trigger-held on harness
evidence), the Voice lane's named remainder, and the §6 armed items
with their wake conditions (F1-hex still waits on a third
persistence-coupled digest or a digestOf touch). J5 — a writable UAT
connection — still trumps everything.

Hold the spine; balance the books; keep the patient breathing; re-run
the witnesses you inherit before you stand on them — mine are one
command each: `scripts/test.sh fast` (31s), `scripts/test.sh docker`
(383s, 229/0), `perf-harness.sh run readside-rowstream` (the Q-arc's
band).

— Generation 6, the contract-keeper, 2026-06-12

# Handoff addendum — 2026-06-12, generation 5 CLOSE (the S-track is CLOSED: the spine is held, S1→S5)

To the next agent.

The structural leg is done. Seven commits (S1, S2, S3, S4a, S4b, S4c,
S5), pure pool green at every one (3056 → 3078; the +22 are named
witnesses), full Docker pool green at S1, S3, and the S5 HEAD. What you
inherit, in order of weight:

**1. The R2 law is LIVE: `declared ⇔ executed∪aborted`.** The
`staged spine { }` CE (`RunSpine.fs`) owns every stage crossing on the
migrated faces — `Bench.scope "stage.<name>"` (a NEW additive meter
surface), the `<stage>.started` envelope, and the
`summary.stageCompleted` close, with the books balanced at run end
(missed/extra/re-entered stages are named refusals; `Completed` is
bracket-plane; the Aborted arm closes the wire `aborted` so the board
goes `Halted`, never hangs — the RI-2(a) defect class is structurally
impossible on migrated faces). Read `DECISIONS 2026-06-12 — The stage
spine lands` and the S4 entry before touching any run face.

**2. The engines own their spines; the bracket has ONE owner.**
`Compose.runWithConfig` = `staged Spines.pipeline` (umbrella root +
extract/profile/emit); `MigrationRun.execute` = `staged Spines.migrate`
with the CDC+tightening gates as the declared `preflight` stage (Voice:
"Safety checks"); both transfer load paths = `staged Spines.transfer`.
`RunEnvelope.bracket` is the single run-envelope owner — `withRun` and
`FullExportRun.executeCore` both delegate; runStart is now FIRST on
every run (failed-config included) and the §10 terminal fires even on
crash. The pinned slice-7 trio held WITHOUT amendment; the named shape
changes are in the commit messages and the S4 DECISIONS entry.

**3. S5's additivity holds with NAMED residue.** Two witnesses (CE
tightness ≤250 ms; the real publish run: umbrella−children ≤500 ms,
run−umbrella ≤3000 ms). The named UNBOUNDED residue: the migrate FACE's
grant pre-flights (`migratePreflights`) — real SQL before the engine's
spine opens. Bounding them = lifting the face gates into a declared
arc — R1b-adjacent; do not bound them dishonestly.

**4. Meter.pass landed (§9.8.5 fired, RI-6 resolved):**
`PassChainAdapter.compose` is literally `Pass.composeAll` over
decorated arrows; the label bytes (`compose.passChain.<Name>`) were
kept against the thesis sketch's `s.Name` form — the refusal is noted
in §9.8.5's amendment.

**Witness state at close:** pure 3078/0/211 (60 s warm); Docker 229/0
at the close re-run. Two infra flakes were named, diagnosed via TRX,
and re-run green — a SQL 1205 deadlock inside `sp_cdc_enable_db` setup,
and `insufficient system memory in resource pool 'default'` on the
100k scale tests after the warm container's third full pool run. The
second is now CLAUDE.md §4 rule 2's third signature: restart the
container and re-run the class BEFORE suspecting code. The inherited
witnesses were re-run at open per the lineage's law: pure pool
replicated exactly (3056/0/211) and the Q-arc's AFTER replicated on
this host (811 ms end-to-end / 167 ms materialize at 100k×12 — inside
generation 4's band). The arc stands on rock; extend me the same
courtesy — my AFTER is one command too.

**Two traps from this session, so they don't bite you:** (a) F#
closures cannot capture `let mutable` locals — moving loop bodies into
a stage thunk means ref cells (`writePlan`'s accumulators; the compiler
error is clear but the fix touches every usage). (b) If you work two
cards in one tree, the commit-separation dance (temp-file swaps to
commit each card at its verified content) costs real care — prefer
committing each card BEFORE cutting the next; I learned this twice.

**Your queue, by the backlog's graph:** the L-track (L1→L4 — F4's
pinned journal surface awaits; fold F1-hex into L2 if you touch
`digestOf`, per §6 item 13), then the R1 stage — note R1b's sequencing
clause is now SATISFIED (the card says "do R1b after S4 to wire once";
S4 is done), and R1e's S2 dependency is met. The armed items (§6) keep
their named wake conditions. J5 — a writable UAT connection — still
trumps everything.

Hold the spine — it holds; balance the books; keep the patient
breathing; re-run the witnesses you inherit before you stand on them.

— Generation 5, the spine-setter, 2026-06-12

# Handoff addendum — 2026-06-12, generation 4 CLOSE (the Q-arc is CUT and measured; the substrate is COMPLETE; F6/F7 closed)

To the next agent.

The arc-cutter's session is done. What you inherit, in order of weight:

**1. The Q-track critical path is CLOSED (Q1→Q2→Q3→Q4, all DONE).** The
in-flight row carrier is positional: `readRowsStream` emits `RowQuantum`
against `Kind.rowBasis`; the Map + `READSIDE_ROW` mint lives ONLY at the
IR-grain boundary (`ReadSide.materializeStream`, its own `materializeIr`
label); the streaming realization consumes quanta end-to-end — renames
are basis-header operations (`RowBasis.rename`, the per-row walk is
DELETED on that path), PK/FK/identity access is by precomputed ordinal,
the capture ladder is carrier-generic (staged `getterOf`, A40). **The
measured win: end-to-end 985→757 ms mean (−23%, ~7.6 µs/row, up to 133k
rows/sec); carrier build 4.20→1.56 µs/row (−63%).** Byte-stability held
everywhere it had to: canary row hashes (Q1's permutation), journal
fingerprints (existing journals resume), IR-grain rows
(`R4: ofQuantum ∘ toQuantum = id`). RI-13 records where the cards
changed shape under the knife: Q4's deletion landed at Q2;
`streamsInOrder` was deleted (consumer-less), not re-typed; Q2+Q3
shipped as one verified commit per the coupling finding. Read
`DECISIONS 2026-06-12 — The Q-arc lands` before touching the read path.

**2. The measurement substrate is COMPLETE (H0–H7 all closed).** Twelve
declared scenarios, every one exercised gate-open this session; numbers
in PERF_HARNESS §5. The new verdicts: the 5000 bulk default VINDICATED
(31.3k rows/sec ≈ 10k's 31.4k; 1k pays +27%); pure static-population
emit is 3.1 µs/row (the in-canary number was ~90% consumer time — the
3a caveat, quantified); profiler discovery ≈ 4 ms/table (the B.3 prior
CONFIRMED — leave it); ossys-parse ~170 ms/1000 entities (cheap; A3/A4
stay unfired); physical-schema-verify decomposes the ~14 s bulk100k
verify prior (rows.hash 3.5 µs/row is its biggest part — an UNMEASURED-
until-now candidate if a future gate demands it). One trap with teeth,
new in PERF_HARNESS §5: an indefinitely-suspended bulk load on the warm
container is a MEMORY-GRANT stall (`sys.dm_exec_query_memory_grants` +
`_resource_semaphores`, grant ~535 MB batch-size-INDEPENDENT) — the
day-old container's semaphore shrinks below it; `warm-sql.sh restart`
is the remedy. Do not blame the batch knob; I did, for half an hour.

**3. F6 and F7 closed** (the fixture builder absorbed FOUR instances,
not the card's three; the dead digest fold is deleted, recipes kept).
Stage 0 and Stage 1 of the backlog are now ENTIRELY done.

**Your queue, by the backlog's own graph:** the S-track (S1→S5, the
spine — the structural critical path to R1e, untouched and ready in its
parallel lane); then L1–L4 (F4's pinned journal surface awaits), then
the R1 stage. The armed items (§6) still have named wake conditions —
including F1-hex (item 13) and staged-bulk (item 9). The perf substrate
is no longer the work; it is the instrument — capture before/after per
card, per the protocol. J5 — a writable UAT connection — still trumps
everything.

Witness state at close: pure pool 3056/0/211 green at every commit;
canary suite 103/0/4; full Docker pool 222/0 at the arc commit and
229/0 (876 s) at the close re-run (`test.sh all`: fast exit 0, docker
exit 0 — the gated perf facts soft-skip there by design and were each
exercised gate-open this session). Six commits, every one green, every
number in its commit message.

Hold the spine; balance the books; keep the patient breathing; re-run
the witnesses you inherit before you stand on them.

# Handoff addendum — 2026-06-11, builder session 2 CLOSE (H7 + the Q-arc boundary map + plane N10; the arc is yours)

To the next agent.

Three things landed after the letter below was written; read them as its
continuation, then take the directive at the end.

**1. The Q2→Q3→Q4 arc now has its boundary map** (the Q2 card's second
blockquote in `CONSTELLATION_BACKLOG.md` §3 stage 5). Verified at HEAD:
`readRowsStream` has exactly three consumers (buffered `readRows`,
`Ingestion.streamKind`, the harness drain); `Ingestion.collectInOrder`
is the materialized path's single conversion point (quantum→StaticRow,
Map + Identifier minted at that boundary, preview scale) while
`streamsInOrder` is the surface Q3 re-types for the 288M streaming
realization; renames collapse to basis-level (the per-row rename walk is
DELETED, not ported); and the attribution trap is named — the
`materialize` label must ride the carrier build wherever it lives, or
your H3 re-run is void. Start the arc from that map, not from a survey.

**2. Plane N10 / card F7:** the streaming-digest apparatus
(`RowDigester.empty/add/finalize` + `PhysicalSchema.withDigests` + the
always-empty `RowDigests` axis) is consumer-less at HEAD — its "used by
the canary" docstring was false and is fixed. Disposition carded:
delete per the dead-algebra precedent (~13 sites, one file), with the
wire-it alternative named and bounded. The hash recipes stay — they are
live. Your arc does NOT need the fold; `hashQuantumBytes` covers the
hash plane.

**3. Card H7 is DONE:** `PerfHarnessScenarios.all` is the declare-once
catalog (Make-thunked; gate-closed = 20 ms skip), the facts index into
it, the registry⇔catalog totality test pins it pure-pool, and
`perf-harness.sh list` reports full scale alternations. H4–H6 now land
declared. En route, one new trap with teeth in the risk register:
`test.sh` splits pools by SUBSTRING over the FQN — never embed a
Docker-collection module name in a test display name.

**Directive, in order:** the Q2→Q3→Q4 arc as ONE focused session from
the boundary map (the witness chain: byte-identical canary at every
commit, reverse-leg Streaming+Canary suites green, H3 re-run last with
the end-to-end `.all` number as the win's witness — not the relocated
label); then H4/H5/H6 against the declared catalog; F6 and F7 as
interleave. J5 preempts everything. The pure pool ran green at every
one of this session's eleven commits (54–68 s; 3052/0/211 at close);
the warm container idiom held throughout.

Hold the spine; balance the books; keep the patient breathing.

# Handoff addendum — 2026-06-11, builder session 2 (re-imaging 2 + the whole F-leg + Q1 landed; the critical path is open and stocked)

To the next agent.

You inherit a re-imaged plan and seven green commits past it. This
session re-verified `CONSTELLATION_BACKLOG.md` at HEAD `9ab0e4b` and then
executed against it. Two things to internalize before you cut.

**First, the plan corrected itself again.** The builder's `bench/perf`
witnesses were gitignored (root `.gitignore:32`), so I re-ran both
verdicts on a second host and they REPLICATED — the MERGE cliff stays
refuted, the Q-gate stays open (materialize 4.83 µs/row = 40.1% of
stream wall vs the builder's 4.77/42%). Six new corrections landed
(RI-7..RI-12). The sharpest: the backlog's own F1 casing claim was
FALSE (CaptureJournal/TransformRegistry are lowercase; the journal
digest IS the filename, so any "fix the casing once" would orphan every
journal at resume), and the thesis's §9.8.11 declare-once prediction
PARTIALLY FAILED at H0 by its own test (scattered Facts + a comment
registry, no single list, no totality test). Trust the backlog at HEAD
over its first edition; trust the code over both.

**Second, the entire Stage-0 F-leg and the Q-foundation are DONE, each
green, each witnessed, each its own commit:**
- **F2** — one `Catalog.stripStaticPopulations`; the Preflight
  `Modality = []` over-erasure (it wiped authored TenantScoped/etc.)
  closed.
- **F3** — the case-sensitive `CatalogResolution` physical-lookup bug
  closed; the SQL-default-collation policy named once
  (`TableId.tableTextEquals` &c). I REFUSED the card's "delegate"
  recommendation as false symmetry (the two sides return different
  types) — re-imaged to naming the comparison policy instead.
- **F4** — seven pure-pool `CaptureJournal` witnesses; the resume
  surface pinned (incl. the finding that a corrupt non-JSON line THROWS
  — not silently lossy — which L2 inherits).
- **F1** — the byte-identical row-hash twins collapsed
  (`RowDigester.hashRowBytes` canonical). I SPLIT the 10-site hex
  scatter off as armed F1-hex (§6 item 13): different plane, persistence
  risk, nil reward; fold it into L2 when it touches `digestOf`.
- **F5** — `LiveProfiler`'s TWO hand-rolled drains → one local
  `drainReader`; the cross-file kernel stays refused (compile order,
  RI-9).
- **Q1** — `RowBasis` + `RowQuantum` (`[<Struct>]`, the gate-fired
  promotion) + `RowDigester.hashQuantumBytes`, byte-identical to
  `hashRowBytes`. An FsCheck property over 100 random column orderings
  is the witness and the type's second consumer.

**Your move: the Q2→Q3→Q4 arc, as ONE focused unit.** I proved (and
carded, see Q2's sequencing finding) that Q2 is NOT independently
win-bearing — `readRowsStream`'s streaming consumers read by-key, so
flipping its return type breaks them until Q3, and converting back to
`StaticRow` at the boundary only ADDS per-row cost until Q4 deletes the
SsKey synthesis. So do Q2→Q3→Q4 together, green at every commit, with
the byte-identical canary (Docker) as the standing witness and an H3
re-run at the end showing the `materialize` label drop. The number is
Q4's, not Q2's. Q1's `hashQuantumBytes` byte-identity is the footing the
whole arc stands on — it is already proven.

The lighter interleave cards remain open and independent: **F6** (one
static-fixture catalog builder — N7's quadruplets) and **H7** (reify
`Scenarios.all` + the registry⇔list totality test — the RI-8 repair;
note its hidden cost, the `// PERF-SCENARIO:` comment's `|` does
double duty as scale-separator and field-separator, so the shell `list`
under-reports today). The S-track (spine) is still open in its own lane.

Unchanged and binding: **J5 preempts everything**; the survival rules
are `CLAUDE.md` §4 (the warm container is up on :11433; pure pool ran
54-59s green at every commit this session); per-win bench discipline;
the perf-gate baseline moves only with its DECISIONS amendment. The
armed items (§6) have named wake conditions — recognize them, don't
pre-build them.

Hold the spine; balance the books; keep the patient breathing; re-run
the witnesses you inherit before you stand on them.

You inherit a running instrument and two answers. Builder session 1
closed three cards off `CONSTELLATION_BACKLOG.md`:

**H0** — the harness spine is LIVE (`PerfHarnessScenarios.fs` +
`scripts/perf-harness.sh list|run|capture|diff`; gate
`PROJECTION_RUN_PERF_HARNESS=1`; artifacts at `bench/perf/<name>/`;
slice-0 acceptance green: zero-Δ double-capture, counts byte-identical,
fleet absent from `test.sh docker`).

**H1** — **the MERGE cliff is REFUTED.** The emitted
`MERGE … USING (VALUES …)` executes at 1k/2.5k/10k rows/kind on SQL
Server 2022 (COUNT(*)-verified `.ok` samples; `renderMerge.rows`=10000
at Count=1 proves the single-statement form; the 1000-row TVC cap binds
INSERT…VALUES only). Slope ~2.5k rows/sec @10k. Card H2 is CLOSED as
no-correctness-defect; staged-bulk demoted to armed-perf. Do NOT
re-open the cliff; the witness is in `bench/perf/seed-merge-execute-*`
and PERF_HARNESS §5.

**H3** — the ReadSide drain + the §3.6 `materialize` label (ONE
aggregated sample per stream, inside `readRowsStream`'s pull —
`ReadSide.fs`, boundary documented at the accumulator). In-harness at
100k×12: **11.40 µs/row end-to-end; materialize 4.77 µs/row = 42% of
stream wall**. The R4 premise is confirmed: **the Q-track gate is
OPEN** (backlog stage 5, cards Q1–Q4 — RowBasis with the name-sorted
hash permutation first; the canary-hash byte-identity witness is the
acceptance).

Your queue, in the order I'd take it: the F-cards as warm-up (F1
digest twins, F2 Static-strip + the Preflight over-erasure fix, F3
case-policy fork, F4 journal unit tests, F5 profiler drain — all S,
all independent), then **Q1** under the open gate, then H4/H5/H6 to
finish the measurement substrate. The S-track (spine) remains open in
parallel. J5 still preempts everything.

Operational notes: the warm container is up on :11433
(`warm-sql.sh restart` if conn failures batch); `perf-harness.sh
capture before <filter>` is your before-ritual for ANY perf-touching
card; the pure pool was green at every commit this session (66-77s
warm). The harness's first two runs each falsified a documented
prediction — keep letting it.

Hold the spine; balance the books; keep the patient breathing.

# Handoff addendum — 2026-06-11, the Lapidary close (the backlog exists; you are generation 3, the builder)

To the next agent.

You build. The plan is **`CONSTELLATION_BACKLOG.md`** — thirteen verified
cleavage planes, thirty-six cards across six stages, a named critical
path (H0 → H1 → H2: the harness spine, then the MERGE-cliff BEFORE
witness, then the cliff fix as a pure Statement-stream rewrite), and
five corrections to the thesis it realizes. Read its §1 first: the
re-imaging found `CONSTELLATION.md` wrong in five places, most notably
that **the Run aggregate already exists** (`Run.fs`, shipped 2026-06-05,
test-only wiring — R1 is completion-and-wiring, not creation) and that
the two ledgers are **duals, not twins** (the journal stores quanta, the
episode store stores snapshots; the corrected `LedgerSpec` splits
WriteAdmit from ResumeAdmit). Trust the backlog over the thesis where
they differ; trust the code over both.

Your first three moves are Stage 0's: H0 (the harness spine,
`PERF_HARNESS.md` §4 slice 0, design RESOLVED — build it, don't
re-litigate it), then H1 (the cliff witness — capture the failure BEFORE
any fix), then F1–F5 as interleave cards (the digest twins at
`PhysicalSchema.fs:333/593` are byte-identical and five sites scatter
four hex idioms; the Static-strip triplication includes a latent
over-erasure at `Preflight.fs:126`; the TransferSpec/CatalogResolution
case divergence is a real semantic fork). Every card carries its
witness, size, deps, and rollback; no card may leave the canary red at
a commit boundary; behavior changes are witness-first, always.

Unchanged and binding: **J5 preempts everything** — the cards are sized
so preemption strands nothing. The survival rules are `CLAUDE.md` §4.
The armed items (§6 of the backlog: journal compaction with its ~9-10GB
numbers, the provenance-typed Static, envelope spill, wavefronts) have
named wake conditions — recognize them; do not pre-build them. Per win:
before/after numbers in the commit message, the bench protocol's
three-candidate shape where a perf claim is made, and the perf-gate
baseline re-recorded only with its DECISIONS amendment.

Hold the spine; balance the books; keep the patient breathing.

# Handoff addendum — 2026-06-11, latest (the program is redirected: the Lapidary backlog precedes the harness build)

To the next agent.

One correction to the letter beneath this one, by operator direction:
your program is NOT to build the perf harness directly. A planning
generation has been interposed. Your mission arrives from the operator
as your opening message — the **Lapidary prompt**, preserved verbatim at
`LAPIDARY.md` — and its single deliverable is **`CONSTELLATION_BACKLOG.md`**:
the surgical slice plan that realizes `CONSTELLATION.md`'s R1–R5, its §10
staged path, and the *fired* items of its §9.8 pattern corpus. You write
no production code this session. You re-image the thesis's claims at
current HEAD (its §12 epistemic ledger tells you which were verified
versus testimony versus conjecture — re-prove before betting a slice),
survey by recommendation rather than by module, and produce the slice
catalog: per-slice cleavage plane with file:line, signature-grade
incision, acceptance witness, blast radius, size class, dependency
edges, trigger status, rollback story — plus the dependency graph, the
named critical path, the refusals with wake conditions, the risk
register, and your own epistemic ledger. Where the thesis is wrong or
has aged, say so and route around it; a backlog that never disagrees
with its thesis has not re-imaged hard enough.

The bottleneck-sweep program itself still stands — it becomes
generation 3's execution, sequenced by your backlog with the harness as
its stage 0. `PERF_HARNESS.md`'s design remains RESOLVED: sequence it,
never re-litigate it.

Unchanged from below: **J5 (a writable UAT connection) trumps
everything, including this** — your sequencing must survive that
preemption. The survival rules are `CLAUDE.md` §4. The >1000-row MERGE
cliff (`ScriptDomBuild.fs:857`, no chunking, no test) belongs in your
catalog as the witness-first canonical case — in your catalog, not in
your diff.

Hold the spine; balance the books.

# Handoff addendum — 2026-06-11, later (CONSTELLATION.md landed — the architectural thesis; your program is unchanged)

To the next agent.

One new canonical surface sits at the projection root: **`CONSTELLATION.md`**
(2026-06-11; amended the same day with §§4–6 — the holonic map, the
calculus, the conceptual thermodynamics — and §9, the reification of
R1–R5 as signature-grade F#, whose §9.8 pattern corpus walks eleven
grains with each entry trigger-statused; later sections renumbered;
pointers below follow the amended numbering) — the architectural thesis for
where this codebase is headed, derived from the code at HEAD by an
eight-sector reconnaissance plus source spot-checks. Read it AFTER
`PERF_HARNESS.md`, not instead of it: its §10 migration path deliberately
anchors on the harness backlog, so **your program is unchanged — build the
perf harness first** (`PERF_HARNESS.md` §4, slice 0 onward). The thesis names the system's organizing principle (the
Conservation Ledger: torsor partial sums, fingerprinted, append-only — at
episode grain `LifecycleStore`, at chunk grain `CaptureJournal`, the same
shape built twice independently; zero cross-references, verified),
adjudicates the streaming question (confirmed as the dominant realization
carrier; refuted as a core data model — do not rebuild the retired
`AsyncStream` combinator surface), and commits to five recommendations
R1–R5, each with a counterexample condition.

The one correctness-adjacent finding to hold while you build the harness:
`buildMergeStatementCore` adds every row to a single `InlineDerivedTable`
with no chunking (`ScriptDomBuild.fs:857`) and NO test covers the
>1000-row VALUES boundary — harness slice 1 answers it with a BEFORE
witness before any fix lands.

What not to re-litigate from the thesis without a DECISIONS amendment: the
stream-wrapper refusal (its §11 item 1), the Torsor-typeclass refusal
(§11 item 2, reaffirming `WAVE_6_ALGEBRA.md` §12.3), and runtime-adaptive
realization selectors (§11 item 4 — selectors stay pure over committed
priors). R1 (the Run
as a value — the one aggregate with no section/retraction pair) is the
largest new structural commitment; it is staged AFTER the harness and the
stage spine, and it subsumes D5/D6 and the `REPORTING_HORIZON.md`
run-ledger — check the thesis §8/§9/§10 before opening any of those
backlog items independently.

And the navigation file itself was rebuilt this session: **`CLAUDE.md`
is new** (operator-adopted; `DECISIONS 2026-06-11 — CLAUDE.md rebuilt
from scratch`). Point-don't-restate; a twelve-item survival list is the
only sanctioned restatement (re-verified at chapter close); the F#
surface is trigger-governed instead of a hand-maintained table. The
predecessor is archived at `CLAUDE_ARCHIVE_2026_06_11.md` — provenance
only, never current state. If you find the new index wrong anywhere,
fixing it in the same commit as the discovery is now standing law (its
§8).

Hold the spine; balance the books.

# Handoff addendum — 2026-06-11 (the realization selector closed the reverse-leg arc; your program is the before/after bottleneck sweep)

To the next agent.

You're picking up at a clean seam. The reverse-leg/288M arc is COMPLETE
through the realization selector: `ReverseLegRealization.choose` (pure,
in `TransferRun.fs`) auto-selects the streaming realization whenever the
request admits it, the capability-descent ladder and per-kind lane choice
handle the genuinely dynamic layers beneath it, and the whole stack is
proven at ~35.5–40.8k rows/sec sustained on loopback (288M ≈ 2.0–2.3h).
Read, in order: `DECISIONS 2026-06-11 — the realization selector`,
`DECISIONS 2026-06-10 — The streaming realization + chunk-resume journal`,
`— 6.A.2 LIFTED…`, `— Capability-descent is the house pattern…`, and the
two addenda at the foot of `AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md`
(~30 minutes). The test surfaces are `ReverseLeg{Canary,Property,Scale,
Streaming,Boundary}Tests.fs` — the fixture idioms there are your
templates.

**Your program — the operator's words: "find other places where
performance might bottleneck and fix them … the profiling, the data
extraction, the SSDT file emission, the Bootstrap emission itself, how
the generated INSERT and/or MERGE scripts for the Bootstrap will run.
Let's before-and-after optimize those too."** Hold the bench-driven
optimization protocol strictly (three-candidate / 2-refuted / 1-confirmed
with bench data; refuted swaps documented). The measurement
infrastructure already exists — do NOT guess at hot spots:

1. **Capture the BEFORE profile first.** The operator-reality canary
   (`scripts/perf-gate.sh`, ~6s warm) and `GeneratorScaleTests`
   (`bulk1k/10k/100k`) emit per-label Bench rollups (Count/Mean/P50/P95/
   P99, sorted by TotalMs — the expensive labels surface at the top).
   Run bulk100k + operator-reality, keep the rollups as the baseline
   artifact, and let the top labels NAME your targets.
2. **The candidate areas, with my priors (verify against the rollup):**
   `LiveProfiler` / `EvidenceCache` (already discovery-then-derive
   optimized — measure before touching); `ReadSide.readRowsStream`
   (per-row `Map<Name,string>` construction — allocation-heavy at scale;
   a column-array carrier would be an IR-adjacent change, so measure
   FIRST and weigh against the StaticRow contract); `SsdtDdlEmitter` /
   `Render.toText` (statement-stream rendering — streamProbe labels
   exist); `StaticSeedsEmitter.renderMerge` + how the emitted Bootstrap
   MERGE scripts EXECUTE (batch sizing of the rendered VALUES blocks —
   note the 1000-row table-value-constructor cap we hit on the transfer
   side; the same parse-bound ceiling likely applies to emitted scripts,
   and the staged-bulk pattern from `SurrogateCapture` is the proven
   alternative shape); `Deploy.executeStream`'s InsertRow-run folding
   into SqlBulkCopy (already bulk — measure batch boundaries).
3. **Per win:** before/after numbers in the commit message, a Bench
   label if the path lacks one, and the perf-gate baseline re-recorded
   (`PERF_GATE_RECORD=1`) ONLY when a floor legitimately improves, with
   the DECISIONS amendment the gate discipline requires.

Also still open from the reverse-leg queue (lower priority than the
sweep): the real-wire bench; reconcile ∘ streaming; WipeAndLoad ∘
journal; journal compaction; the G1/G2 reserved preflights. The FS3511
Release traps (let rec inside task; tuple let!; tuple-pattern for) and
the ISNULL-vs-CASE identity-propagation trap are documented in the
2026-06-10 DECISIONS entries — read them before touching the write path.

# Handoff addendum — 2026-06-10, night (streaming + packed remap + chunk resume + the capture ladder landed)

To the next agent.

The 288M-row program's four engine slices are in: read `DECISIONS
2026-06-10 — The streaming realization + chunk-resume journal` and
`— 6.A.2 LIFTED…`, plus Addendum 2 at the foot of
`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md`, before touching the write
path. In brief: `SurrogateCapture` is the capture-lane ladder (three
rungs, one semantics; descent only on SQL error 334; every descent named
on the report; sticky per kind) — extend it by adding a rung function and
a ladder entry, never by branching inside a rung. `PackedSurrogateRemap`
is the realization-layer remap (int64-packed, string fallback; consumed
via `SurrogateRemap.remapRowFksWith` — A40). `Transfer.
runStreamingWithRenames` is the bounded-memory straight load (structure-
only plan; per-kind 50k chunks; phase 2 re-streams); `CaptureJournal` is
the client-side chunk ledger (fingerprint-guarded skip; `transfer.resume.
sourceDrift`; a completed run re-runs as a full skip — G3 closed under a
journal). Watch two FS3511 shapes in Release: a `let rec` INSIDE a task
block, and tuple `let!` / tuple-pattern `for` — hoist helpers out of the
task and bind single values.

One finding to carry forward: the G10 resumable envelope's progress table
needs CREATE TABLE — the real cloud sink's `grant: data` forbids it, so
G10 cannot run there; the journal is the DML-legal replacement on the
streaming path.

Your queue: wire `runStreamingWithRenames` + a `--journal <dir>` flag
onto the reverse-leg CLI face (the engine entry is proven; the face is
unwired); the real-wire bench before trusting the ~27k rows/sec / 3h
figure; reconcile ∘ streaming and WipeAndLoad ∘ journal (both refused-by-
scope today, named in DECISIONS); journal compaction if the estate's
FK-target pair count makes the NDJSON unwieldy; parallel per-table
wavefronts only if the wire bench misses 20k rows/sec.

# Handoff addendum — 2026-06-10, late (the 288M-row program: set-based capture + the 6.A.2 lift landed)

To the next agent.

The operator sharpened the reverse-leg premise to ~288M rows in a ≤4h
window, with the huge tables FK-referenced, the F1 lift authorized, and
chunk-level resume requested. Two engine slices landed the same evening —
read `DECISIONS 2026-06-10 — 6.A.2 LIFTED…` and the addendum at the foot
of `AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md` before touching the write
path. In brief: (1) the AssignedBySink capture lane is now set-based —
bulk-staged into a session temp table cloned from the sink, one
MERGE…OUTPUT per 50k chunk; measured ~27k rows/sec sustained (288M ≈ 3h,
inside the window) versus ~271 rows/sec for the retired per-row loop, and
the unreferenced kinds skip capture entirely via Bulk.copyRowsSinkMinted.
Watch the ISNULL-vs-CASE identity-propagation trap documented at the
staging SELECT INTO — a CASE wrapper constant-folds and the staging mints
its own keys (the keystone canary catches it). (2) The cyclic
AssignedBySink refusal is LIFTED: phase 1 re-points excluding deferred
columns, phase 2 keys its UPDATE on the ASSIGNED PK through the completed
remap, and an orphaned deferred reference is a named phase-2 erasure.

Your queue, in priority order (the report addendum carries the detail):
streaming ingestion with bounded memory (collectInOrder materializes whole
tables — the binding constraint at 288M), the remap representation for
huge FK-referenced tables (packed int64 or sink-resident keymap — gate it
on the estate row-count/FK-fan-in survey), chunk-level resume (operator-
requested), parallel per-table wavefronts only if the real-wire bench
misses 20k rows/sec, and the survey items (platform triggers → OUTPUT
INTO; P7 ceilings). Re-bench over the real network before trusting the 3h
figure.

# Handoff addendum — 2026-06-10, evening (LE-3: the reverse leg proven at full implicature)

To the next agent.

The reverse leg is no longer L1-on-one-table. This session drove the proof
into the previously untested intersection — rendered cross-rendition
contracts × sink-minted identity (`AssignedBySink` everywhere) × a
multi-kind FK graph (depth-4 chain + diamond) × the DML-only principal —
and the whole stack is green in both pools. **Read
`AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md` first**: it states, per
constraint in the operator's pre-cutover premise, what is proven, what is
refused by name, and what is open, with the two findings that need the
operator's eyes (F1: the cyclic-AssignedBySink refusal is probably on the
critical path — a self-FK IDENTITY kind refuses the whole load; the lift
is tractable but is the operator's call. F2: the per-row INSERT…OUTPUT
capture envelope measured at ~271 rows/sec — the MERGE…OUTPUT set-based
follow-on's measured-bottleneck trigger is now satisfied).

The new surfaces, all test-side (no engine change was made):
`ReverseLegCanaryTests.fs` (Tier 1 keystone + apparatus + the DML-only
principal trio + the DENY/drift pins), `ReverseLegPropertyTests.fs` (the
eight pure laws — order soundness, disposition totality, remap algebra,
refusal totality over generated unsatisfiable shapes, rendition
invariance), `ReverseLegScaleTests.fs` (the capture envelope + the CDC
isometry norm — ‖δ‖ = capture count = row count, exactly), and
`ReverseLegBoundaryTests.fs` (the CLI reconcile/user-map refusal live +
four reserved Skip-stub contracts carrying their promotion triggers).

What this collapses: the J5 managed-environment spike is now a re-run of a proven
suite against a real connection — P1/P2/P3/P6 are answered on mock
infrastructure. What genuinely remains for J5: the actual OutSystems grant
envelope (and the G1 object-scope-DENY gap, pinned), platform triggers on
OSUSR tables (they would force the OUTPUT INTO form, survey P5), and P7
batch ceilings over a real wire. The decision asks are ranked in the
report §6; act on none of them without the operator.

# Handoff addendum — 2026-06-10, latest (J3 closed; A7 resolved)

To the next agent.

Two updates to the addendum beneath this one. **The reverse-leg runner arm
is no longer unforced — J3 is CLOSED.** The contract source the wiring
waited on exists: `CatalogRendition.logical`/`.physical`
(`src/Projection.Pipeline/CatalogRendition.fs`) render the ONE authored
model at both renditions — the same two emission-axis passes the down-leg
publish applies, SsKeys untouched (A1), so the pair aligns by construction
and the B→A rename map is the identity (`Name` is rendition-invariant; the
rendition difference rides each contract's physical coordinates at its own
SQL boundary). `PlanAction.RunReverseLeg` carries the model and a
model-less legacy flow refuses at PLAN time; the CLI arm runs
`Transfer.runReverseLegThroughConnections` through the apparatus;
reconcile/rekey on the reverse leg is a NAMED refusal (the reconcile +
rename combination stays the follow-on). Witnesses: the `M3/LE-1 …
RENDERED … (CatalogRendition)` Docker canary + the J3 pure tests in
`MovementSurfaceTests`. Read `DECISIONS 2026-06-10 — J3 residual CLOSED`
before touching any of it. The deliberately-NOT-built alternative is live
attribute-scope `V2.SsKey` recovery in `ReadSide.buildAttribute`; its
re-open trigger is a reverse leg over an estate with no authored model.

**A7 polarity is RESOLVED** (operator): the module-filter include flags
stay opt-in; the inert combination carries a named note +
`moduleFilter.flagsInert` diagnostic. Do not re-open.

The remaining queue is now: **J5** (managed-environment execute — operator-gated on a
writable connection; the one true critical-path item), **B9** (IRBuilders
α′ Python pass), D5–D7 instrument slices, §C modeling decisions, §E/§F
speculative items. Small operational note: `scripts/test.sh`'s failed-name
extraction was fixed this session (it could blame the textually-preceding
PASSED test — testName and outcome share one TRX line; no `-B1`).

# Handoff addendum — 2026-06-10, later (the CLI decomposition landed; the queue is operator-gated)

To the next agent.

One update to the letter beneath this one: item 3 (the `Program.fs`
decomposition) is DONE — `Program.fs` (2,151) split into
`OperatorConsole.fs` (the console substrate: modes / `withRun` envelope /
error-exit printing / stage arcs), `RunFaces.fs` (the proven `run*` faces,
one per `PlanAction`), and a 379-line `Program.fs` that is exactly usage +
`runPlan` + the dispatch-local helpers + `main`. Behavior-preserving:
solution builds clean, CLI smoke verified live, pure pool green. If you
split `RunFaces` further into family files, it is now a self-contained move.

So the remaining queue is operator-gated on both ends: **J5** (managed-environment
execute — needs a writable connection) and the **A7 polarity decision**
(asked of the operator at this session's close; check the conversation /
their next instruction before assuming). The reverse-leg runner arm stays
deliberately unforced pending a contract source. B8 closed; the
DacFx/ScriptDom question is closed and witnessed
(`DacpacPublishEquivalenceTests`) — do not re-open it.

One operational reminder beyond the letter below: the Docker daemon on this
host has died twice in one session, taking `projection-mssql-warm` with it.
A batch of `Could not open a connection` failures — including in the PURE
pool, which opportunistically uses the warm container while
`PROJECTION_MSSQL_CONN_STR` is exported — means `scripts/warm-sql.sh
restart`, not a regression. `scripts/test.sh status` first, always.

Hold the spine.

---

# Handoff letter — 2026-06-10 (the wiring shelf is empty; what's left is yours to choose)

To the next agent.

You're picking up after the remaining-shelf sweep on branch
`claude/fsharp-projection-review-vrbtpr` (pushed; no PR yet — the operator
hasn't asked for one). Read `CONFIRMED_BACKLOG_2026_06_09.md` §⓪′ first —
it is the code-verified ledger of what this sweep closed (A1 fully, A3
fully, D8, J2, the dacpac wire, `shape: manifest`, B8) — then the
2026-06-10 `DECISIONS.md` entry for the resolved questions you must not
re-litigate: the A3 faithful-omission rule, the inert-default-flips-off
rule for the dacpac gate, and the DacFx/ScriptDom question (CLOSED — a
documented boundary, now gated by `DacpacPublishEquivalenceTests`; do not
re-open the "unify the ALTER surface" framing).

What's actually left, in the order I'd take it:

1. **J5 (managed-environment execute, OPEN-2)** is the cutover-critical path and is
   blocked on the OPERATOR — a writable connection for the ops spike +
   `--execute` under R6 with `--preview-row-cap`. If the operator shows up
   with an environment, drop everything else.
2. **The A7 polarity decision** is also the operator's: should
   `model.includeSystemModules/includeInactiveModules: false` act globally
   with no `modules` named, or stay effective only alongside a non-empty
   `modules` list (today's opt-in, byte-identical default)? Five minutes of
   their time; ask before you guess.
3. **`Program.fs` decomposition** is the largest honest code-side item
   (~2,150 lines: console substrate at the top, ~35 `run*` functions in
   family clusters, `runPlan` + `main` at the bottom). Scope it as the
   B7/`Deploy.fs` precedent: move the shared console substrate (the mode
   refs + `withRun`/`printErrors`/`dumpBench`) into its own module first,
   then the runner families (emission / proof / transfer / migrate /
   explain), keeping `Program.fs` as parse + dispatch. Do it as its own
   fresh session — it is mechanical but wide, and every `run*` touches the
   substrate.
4. **The reverse-leg runner arm (J3 residual)** stays deliberately
   unforced: it needs the two SsKey-aligned contracts (a shared authored
   model rendered in both renditions, or attribute-scope `V2.SsKey`
   recovery in `ReadSide.buildAttribute`). The classifier + engine face are
   landed; don't wire the arm until the contract source exists.

Operational notes you'll want: `scripts/test.sh status` is new — every pool
run leaves a live log + one-line status file, so "is it stuck?" is one
command (the 2026-06-09 protocol's tail-buffering trap bit this session
too; launch pools bare in the background, never through a pipe). The
Docker daemon died once mid-session and took `projection-mssql-warm` with
it — a batch of `Could not open a connection` failures means
`scripts/warm-sql.sh restart`, not a real regression. Full Docker pool was
194/194 green on this branch before push.

Hold the spine.

---

# Handoff letter — 2026-06-02 (the isomorphism-climb debrief is canonical; read it before you open a slice)

To the next agent.

You're picking up the Wave-6 climb with one new load-bearing surface in front of
you: **`DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md`**. Read it first.
It exists because the planning docs had drifted from the code — most of them
still describe the 2026-05-31 five-axis red-team state, and the codebase has
climbed well past it (6.A.* faithfulness, 6.B.1/6.B.2 orthogonality pre-flights,
6.D.1 `migrate A B` live, and 6.H.1/6.H.2/6.H.4 durable provenance all landed,
plus the F#-practices audit slices 0–12). The debrief reconciles the North-Star
matrix against HEAD cell by cell, then gives you a 20-row fidelity ledger
(G1–G20, each with file:line + ladder level + the named refusal or silent
erasure it is) and a 10-cluster slice backlog (A–J) with scope, signatures,
acceptance witnesses, dependencies, and survey-gating. `EXECUTION_PLAN.md` Wave 6
and `BACKLOG.md` are now reconciled to match it.

**Where the open work actually is.** The data plane refuses loudly (exit-9 on
drops; the execute-gate refuses cyclic/composite `AssignedBySink`). The deep
L2 holes are on the **schema/diff plane** and in **T-VI spanning**:

- **C1 — widen the `CatalogDiff` captured surface** (`CatalogDiff.fs:380-388`).
  This is the centerpiece: `between`/`applyDiff` — and therefore `migrate A B` —
  capture only kind + attribute column-shape. References, indexes, and sequences
  ride through unchanged, so an A→B that adds an FK or changes a sequence is
  *silently no-op'd*. The round-trip law holds only on the captured surface.
- **A — the T-VI pre-flight suite** (extend `Preflight.fs`): connection,
  permission, transactional/resumable. The only place a target can still be
  *silently corrupted* (write-denied → 0 rows; mid-load crash → half-populated).
- **B1 — wire `migrate --source-conn --execute`** (today the CLI verb is
  plan-only; the live square is test-driven). Gate it behind A and C1.
- **D — the generated L2/L3 matrix** (the old 6.E.1). `matrix-status.sh` reports
  L1 presence only; it could not see that "6.A un-hollowed ReadSide" still left
  `Indexes = []` at reconstruction (`ReadSide.fs:877`). Build the generator that
  reports each cell's ladder level from the proof — the keystone that makes the
  climb self-verifying.

**The discipline that will bite hardest** is the smart-constructor
default-substitution bomb (`{ Reference.create … with … }` silently inheriting
`IsConstraintTrusted = true`; same for `Index.create`'s lock flags). C1, E1, and
E3 all touch reconstruction sites — set every field the diff touches explicitly.
The debrief §6 has the full list. Always `bash scripts/test.sh fast` before
"done"; never run the pure + Docker pools as one `dotnet test`.

**Reading order (~15 min):** the debrief §0→§2 (orientation + the fidelity
ledger), then the §3 cluster you're about to build, then §4 (critical path) and
§5 (what's survey-gated vs buildable now). When the climb advances, **update the
debrief first**, then propagate to the other surfaces — it is now the canonical
state-and-backlog document; the ontology/algebra/morphology remain canonical for
their framing.

Hold the spine. Complete the matrix.

— The debrief author (2026-06-02).

---

# Handoff letter — 2026-05-31 (EXECUTION_PLAN Wave 4 CLOSED — 4.2–4.6 shipped)

To the next agent.

**Wave 4 is closed.** You're inheriting a green tree with all five remaining Wave-4 slices
shipped and verified (the prior agent had closed 4.1; this session closed 4.2–4.6). Build is
clean (`dotnet build Projection.sln`, 0/0); pure pool **2480 passed / 0 / 207 skipped**; the
Docker-gated additions (verify-data ×3, the apparatus-driven reconcile canary, the full transfer
canary ×8) are green against the warm container. Commit arc: `23e80de` (4.3) → `fcd8f52` (4.6) →
`76ecb34` (4.5) → `6a3126c` (4.4) → `ded475b` (4.2). What each shipped, in one line:

**One pre-existing Docker failure you'll inherit (NOT from Wave 4):** the full Docker pool reports
`CanaryRoundTripTests.A42 (2.4 canary): a DoNotEnforce FK decision keeps the FK out of the deployed
schema` failing at line 531 — the empty-overlay *baseline* deploys an FK but the readback
reconstructs 0 (expected 1). I reproduced it **identically at the pre-session base commit `deb5853`**
via a worktree, so it predates this session's work (the A42 fixture is all `Catalog = None`, which
my 4.3 emission change leaves byte-identical). It looks like an FK-readback gap in this specific SQL
container/version — worth a look, but it is not a Wave-4 regression. (`GeneratorScaleTests`
deterministic-seed also flaked once under the full serial-Docker resource pressure but passes in
isolation.)

- **4.3** cross-DB FK — `schemaObjectFromTableId` emits a three-part `[db].[schema].[table]` when
  `TableId.Catalog = Some`; `toTableId` carries `Physical.Catalog`. Additive (None ⇒ byte-identical).
- **4.6** `Origin` rename — `OsNative→Native`, `ExternalViaIntegrationStudio→ExternalIndirect`
  (variants AND rendered strings; V1 emits `isExternal`, so the regold was V2-only).
- **4.5** DACPAC joins T11 — keyset agreement is *verified* (round-trip read of the DacFx model,
  SsKey recovered via the Catalog's physical-coordinate bijection), not structural — it's
  `Result<byte[]>`, not an `ArtifactByKind`. That verified-vs-structural distinction is the honest
  framing; don't try to force the binary sibling into the ArtifactByKind shell.
- **4.4** `osm verify-data` — `DataIntegrityChecker.compare` profiles two deployments via
  `LiveProfiler.captureEvidenceCache` (×2) and diffs in pure F#. **The trap that cost me an hour:**
  `LiveProfiler` skips `Modality.Static` kinds, and `ReadSide.read` marks *every* reconstructed table
  that carries rows as `Static` (a canary per-row-reconstruction artifact) — so profiling a ReadSide
  catalog yields an empty cache. `compare` clears the `Static` marking first. If you profile a
  ReadSide-derived catalog anywhere else, you'll hit the same empty-cache surprise.
- **4.2** Transfer C′-wire — `ConnectionResolver.openSubstrate` + `Transfer.runThroughConnections`
  drive a run through the `TransferConnections` apparatus; `TransferSpec.parseUserMapCsv` /
  `resolveUserMap` / `resolveAllReconciliation` land the `ManualOverride` CSV. The CLI's
  `runTransfer` now opens nothing itself — it builds the apparatus and hands it to
  `runThroughConnections`.

## What you should NOT do next

**Do not build Wave 5 speculatively.** This is the load-bearing call and it's deliberate (IR grows
under evidence, not speculation). Wave 5 is either *blocked* or *defer-with-trigger*:
- **5.1** (Transfer D-exec, managed OutSystems environment load) is blocked on **OPEN-2** — does a managed OutSystems environment
  expose a writable SQL connection to entity tables, or is it platform-API-only? That's an ops
  spike, not an engineering task. The one buildable crumb is `--preview-row-cap` on `TransferArgs`.
- **5.2–5.9** each carry a named trigger (5.2 AssignedBySink: a real sink-minted-key load; 5.3
  Lifecycle: a stored-prior-catalog consumer; 5.5 applied-transforms manifest: an R6 audit reader;
  5.6 policy-intelligence: a UAT dry-run diff; etc.). Build the slice when its trigger fires — not
  before. `EXECUTION_PLAN.md` §III Wave 5 has the first-slice + trigger for each; the *Active
  deferrals* index at the top of `DECISIONS.md` is where you check whether a trigger has fired.

If you have appetite and no Wave-5 trigger has fired, the honest next frontier is the **endgame
backlog** (`EXECUTION_PLAN.md` §V — E1 verifiability CI gate, E2 generated readiness map, E3
decision-layer adjunction, E4 lifecycle operationalization, E5 registry-as-documentation). E1
(wire `scripts/verifiability-gate.sh` into CI so a phantom-Bucket-A axiom can't merge) is the
lowest-risk, highest-leverage of those and has no external gate.

## Disciplines that bit this session (internalize before you write code)

- **Solution-build-before-commit + `bash scripts/test.sh fast` before "done."** Still the only ground
  truth. The pure/Docker pools must never run as one `dotnet test` (OOM on this host) — use
  `scripts/test.sh`.
- **Closed-DU rename = let the compiler drive the blast radius.** 4.6 touched ~36 files; I changed
  the 6 source sites, built, and the exhaustiveness/undefined-name errors named every remaining
  test/fixture. Don't hand-hunt; build and read the errors.
- **ReadSide marks data tables `Static`** (the 4.4 trap above). Remember it.
- **The lint inventory (`scripts/lint-discipline.sh`) exits 1 as a standing soft-floor** — it's an
  inventory of pre-existing LINT-ALLOW sites, not a gate you broke. Confirm your files aren't in the
  new output; don't chase the legacy entries.

## Reading order (≈10 min)

1. `EXECUTION_PLAN.md` §III Wave 4 (now all DONE with shipped-notes) + §III Wave 5 (triggers) — ~5 min.
2. `git log --oneline ded475b~6..ded475b` — the Wave-4 close arc — ~2 min.
3. `DECISIONS.md` *Active deferrals* index (top) — confirm no Wave-5 trigger has silently fired — ~3 min.

Hold the spine. Wave 4 gave you a bidirectional frontier that's wired end-to-end (the apparatus
drives; verify-data gates data fidelity; the DACPAC sibling is under the T11 contract). Wave 5 waits
for evidence.

— The Wave-4-close architect.

---

# Handoff letter — 2026-05-30 (EXECUTION_PLAN Wave 4.1 CLOSED + branch-red repair)

To the next agent.

You're picking up the EXECUTION_PLAN wave work mid-Wave-4. Waves 0–3 are done (git log `4fdeb1d`..`b80383c`; an audit this session confirmed ~26/31 slices BUILT, the rest org-gated or trivially-deferred). Wave 3.3 (`d20bebc`) collapsed the planned standalone `uat-users` verb into opt-in behavior of `transfer` + `full-export` — there is no `osm uat-users`, and that's deliberate (`DECISIONS 2026-05-30`). **Wave 4.1 (`V2.SsKey` persistence) is now CLOSED and verified end-to-end.** Your job is to keep going through Wave 4: **4.2** (connection apparatus + CSV loader / `LiveOssysConnection`), **4.3** (cross-DB FK three-part name), **4.4** (`osm verify-data` verb), **4.5** (DacpacEmitter joins the T11 sibling contract), **4.6** (`Origin` variant rename). Wave 5 is genuinely blocked (5.1 needs a writable UAT SQL connection — OPEN-2) or defer-with-trigger (5.2–5.9, no current consumer) — do NOT build Wave 5 speculatively; it violates "IR grows under evidence."

## The most important thing to internalize before you write code

**Run `dotnet build Projection.sln` (the whole solution) before every single commit, and `bash scripts/test.sh fast` before you call a slice done.** This session opened on a branch that had been pushed **red**: commit `aa7aa9a` (Wave 4.1 part-2b) didn't compile — a `readSchemaCombined` return-type annotation was 6-wide against a 7-tuple body, and the 4.1 acceptance test referenced three helpers that never existed (`CanaryTestGuard.runWhenEnabled`, `CanaryHarness.deployAndReadback`, `sampleSourceCatalog`). Both were mechanical, both repaired in `8dbcdfd`, but they slipped in because a prior commit verified only its own project, not the solution. The compiler and `scripts/test.sh` are the only ground truth here — trust them over any summary (including this letter).

## What you inherit, working and verified

`SsKey.serialize` / `deserialize` (`Identity.fs`) — total, round-trippable, tag+length-prefixed so nesting is unambiguous. `SsdtDdlEmitter` persists `V2.SsKey` as a table-level extended property (sibling to the existing `V2.LogicalName`). `ReadSide.buildKind` hydrates from it (`deserialize` → fall back to `kindSsKey` synthesis). The Docker-gated round-trip `` ``4.1: V2.SsKey persistence — ReadSide recovers OssysOriginal identities`` `` passes in ~8s (deploy `OssysOriginal`-keyed catalog → read back → recovered key IS the original GUID). Pure pool: **2462 passed / 0 / 207 skipped**.

## How to do Wave 4.2 (your next slice)

The `TransferConnections` apparatus already exists in `Transfer.fs` (`Substrate`, `Environment`, `ConnectionRef`, `TransferConnections.create` with role-mismatch validation). `ConnectionResolver.resolve` resolves an env-var/file ref to a connection string (D9: never hold a secret in `Config`). The deltas: thread `TransferConnections` through `TransferRun.runCore`; wire `Program.fs` `runTransfer` + `TransferArgs.fs` for `--environment` / named-substrate resolution; add a `--user-map` CSV loader for `ManualOverride`; add `openSubstrate : Substrate -> Task<Result<SqlConnection, _>>`. Acceptance: the reconcile canary stays green driven through `TransferConnections`; a `ManualOverride` CSV round-trips. Reuse the existing profiler — don't add per-table probes (see the EvidenceCache discipline).

## Reading order (≈12 min)

1. `EXECUTION_PLAN.md` §III Wave 4 (4.1 now marked DONE with the cautionary note; 4.2–4.6 specs intact) — ~6 min.
2. The three `DECISIONS 2026-05-30` entries (uat-users collapse; and read the most-recent ten) — ~4 min.
3. `git log --oneline d20bebc..HEAD` — the Wave 3.3 + 4.1 commit arc — ~2 min.

Disciplines that bite in this wave: **solution-build-before-commit** (above); **`skipIfNoDocker` for Docker-gated tests** (the canary collection pattern — never `CanaryTestGuard`, which doesn't exist); **IR grows under evidence** (Wave 5 stays deferred); **D9** (connection strings via `ConnectionRef`, never `Config`).

Hold the spine.

---

# Handoff letter — 2026-05-23 (slice D.2.c + D.2.d + D.3.b XXXXXL combined slice CLOSED)

To the next agent.

You're picking up V2 mid-Chapter D with the emission-aesthetics arc significantly advanced and the architectural-totality gap that opened during the prior session now closed. Three sub-slices landed as one XXXXXL combined slice this session: D.3.b registered `ConstraintFormatter` as `OperatorIntent Emission` metadata; D.2.c added `Statement.BatchSeparator` (typed GO emission); D.2.d added `Statement.AlterTableDisableTrigger` + per-trigger metadata comments. Test suite 2370/0/207 green throughout. The realization-layer-overlay registration discipline is now the canonical shape for every future emission-aesthetic transformation.

## Where you are in the spine of the work

Read `SLICE_D_2_C_D_2_D_D_3_B.md` first (~7 min) — combined slice doc covering all three sub-slices + the realization-layer-boundary discipline they share. Then the DECISIONS entry (~4 min) for the canonical decisions including the metadata-only registration pattern that resolves the pillar-9 totality question for `string -> string` transformations.

## Architectural posture you inherit

Pillar 9 totality holds at the realization layer via the **metadata-only registration pattern**: realization-layer overlays (text post-processors operating on rendered SQL) register as `RegisteredTransformMetadata` only (no `RegisteredTransform.Run`); their per-invocation execution happens at the realization-layer call site (e.g., `Render.toText`); the registry's totality-coverage scan + the canary manifest's `applied-transforms` field see them. This preserves the classification contract WITHOUT forcing every text-level transformation through the writer-monad shell.

Mode parameter precedent established at slice-D.1.a (`LogicalTableEmission.Mode = Enabled | Disabled`) now extends to realization-layer overlays uniformly: `ConstraintFormatter.Mode = Enabled | Disabled`. Production wiring captures `Enabled`; `Disabled` is the diagnostic / V1-parity-bisect surface. Every future emission-aesthetic transformation lands with the same Mode + registeredMetadata shape — that's the architectural surface you inherit.

## What's still in D.2.b's deferred queue

From the operator-PO subagent harvest at session open:

1. **D.2.e — ALTER WITH NOCHECK ADD CONSTRAINT semantic rework** (Large; HIGH visibility). V2 currently emits untrusted FKs as `FK inside CREATE TABLE` + `post-ALTER TABLE WITH NOCHECK CHECK CONSTRAINT` (semantically equivalent to V1's deployed state). V1 emits `FK as standalone ALTER WITH NOCHECK ADD CONSTRAINT` (different textual shape; same end-state). Deferred unless an operator surfaces preference for the textual divergence; V2's structurally correct, just textually different. Rework would touch emission-order rework + a new `Statement.AlterTableNoCheckAddForeignKey` variant.

2. **Lineage events on formatter sites** (Medium; LOW visibility). Per-invocation `LineageEvent` emission would surface in the operator's lineage trail when the formatter reshapes a CONSTRAINT line. Requires either a writer-monad refactor of `Render.toText` (currently `string → string`) or a side-channel. Pillar 9's classification gap is closed (the formatter has registered metadata + sites); the per-invocation event-emission gap is a separate concern named for a future slice when a consumer demands the lineage detail.

3. **Extended properties beyond MS_Description / V2.LogicalName** — confirmed by subagent that V1 only emits MS_Description in production; the V2.LogicalName slot D.1.b added is V2-growth. Closed.

4. **Header / footer banners** — explicitly out of scope per the `IgnoreHeaderComments` tolerance.

## What you might open next

The chapter D arc has clean closure surfaces:

- **D.4.a — Chapter-mid audit of pillar 9 totality across all sibling emitters**. Now that ConstraintFormatter ships registered, dispatch the parallel walk: every emitter / formatter / overlay that V2 ships should appear in `RegisteredAllTransforms.all`. The audit produces a coverage map (registered vs not) and surfaces any remaining drift. ~2-3 hours; dispatchable to a subagent.
- **D.5 — AdjunctionLawTests' H-050 widening for the new Statement variants**. `BatchSeparator` + `AlterTableDisableTrigger` should preserve the adjunction `PhysicalSchema.ofCatalog c = ofStatementStream (SsdtDdlEmitter.statements c)` on the new variants. Likely already does (the variants don't affect column / FK / extended-property projections); add explicit property-test coverage to make the property structural. ~1 hour.
- **D.6 — Perf-gate baseline re-record**. Chapter D added ~2-3k extra statements per canary (GO separators + V2.LogicalName extended properties + trigger comments). No observed regression in unit tests; production-scale operator-reality canary may drift. Run `PERF_GATE_RECORD=1 ./scripts/perf-gate.sh` + commit new baseline + DECISIONS amendment. ~30 minutes.
- **D.2.e if the operator-PO surfaces the preference** (per #1 above).

## What's load-bearing

Carried-forward, still load-bearing:
- **Metadata-only registration pattern for realization-layer transformations**. New emission-aesthetic overlays follow `ConstraintFormatter`'s shape exactly: Mode parameter + `registeredMetadata` per `RegisteredTransformMetadata.emitter` + append to `RegisteredAllTransforms.all`. Don't try to force `string -> string` transformations through the typed `RegisteredTransform<'In, 'Out>` shell.
- **Closed-DU expansion empirical-test discipline (extended at N=N+1)**. Adding `BatchSeparator` + `AlterTableDisableTrigger` to `Statement` produced exactly TWO exhaustiveness errors (both at `Deploy.executeStream`'s match site). The pattern holds — exhaustiveness errors light up only at match sites that genuinely care.
- **Mode parameter mirrors `LogicalTableEmission.Mode` precedent across catalog + realization layers**. Every operator-toggleable overlay uses `Enabled | Disabled`; production captures `Enabled`; `Disabled` is the diagnostic / V1-parity-bisect surface.

New from this slice:
- **The realization-layer-overlay registration discipline**. Per the DECISIONS entry — realization-layer transformations carry pillar-9 classification via metadata-only registration; the per-invocation typed-Run is preserved for catalog-level transformations where the writer-monad makes sense.

## Reading order (~20 min)

1. **`SLICE_D_2_C_D_2_D_D_3_B.md`** — combined slice doc; covers all three sub-slices + the metadata-only registration pattern. ~7 min.
2. **`DECISIONS 2026-05-23 (slice D.2.c + D.2.d + D.3.b + D.3.c codification)`** — canonical decisions; the realization-layer-boundary discipline codified. ~4 min.
3. **`src/Projection.Targets.SSDT/ConstraintFormatter.fs:55-107`** — Mode + registeredMetadata; the canonical realization-layer-overlay shape. ~3 min.
4. **`src/Projection.Targets.SSDT/Statement.fs:262-321`** — the new closed-DU variants for `BatchSeparator` + `AlterTableDisableTrigger`. ~3 min.
5. **`src/Projection.Pipeline/RegisteredAllTransforms.fs:53-59`** — where `ConstraintFormatter.registeredMetadata` lands in the totality surface. ~2 min.

## Pitfalls this slice hit that you can avoid

- **`TransformSite.operatorIntent` argument order**: signature is `(name: string) (axis: OverlayAxis) (rationale: string)`, NOT `(axis) (name) (rationale)`. F# positional inference helps but is occasionally misleading — caught at compile time by the type error "expected string, got OverlayAxis" but worth knowing.
- **Adding Statement variants requires Deploy.executeStream dispatch**. The `executeStream` function's match against `Statement` is the one place exhaustiveness fires; treat the no-op + DDL-flush dispatch branches as the canonical extension shape.
- **Don't over-engineer realization-layer registration**. The temptation is to wrap `ConstraintFormatter.format` as a `RegisteredTransform<string, string>` with `Run : string -> Lineage<Diagnostics<string>>`. Resist. The metadata-only registration pattern is the canonical fit; the typed-Run shell is for catalog-level transformations where the writer-monad makes sense.

Hold the spine. The chapter-D emission-aesthetics arc has structural-totality on the architectural axis AND operator-visibility-near-parity on the V1-parity axis. Whatever opens next inherits a substrate where every emission-aesthetic transformation is registered + classified by construction.

— The slice D.2.c + D.2.d + D.3.b architect.

---

# Handoff letter — 2026-05-23 (slice D.2.a CLOSED; chapter D's emission-aesthetics arc opens)

To the next agent.

You're picking up V2 with **chapter D's first arc closed (D.1.a/b/c — logical-name emission end-to-end) and the second arc opened (D.2.a — elegant constraint formatting just shipped)**. The operator-PO flagged the emission-layout gap immediately after the logical-name arc landed: V1's C# pipeline produces multi-level-tab elegance for PK / FK / DEFAULT; V2's ScriptDom-default emission packs constraints onto column lines. D.2.a carbon-copies V1's `ConstraintFormatter` as F# and wires it into `Render.toText` as a terminal post-processor. Test suite 2370/0/207 green.

## Where you are in the spine of the work

D.2.a is the first slice of chapter D's emission-aesthetics arc. The arc's framing: V2 now has structurally-correct emission (logical names; verified roundtrip; canary triangle). The remaining axis is **operator-visible layout** — each CREATE TABLE's shape, the indentation conventions, the deferred extended-property formatting, anonymous-default handling. The arc closes when V2's emission matches V1's elegance across every operator-visible shape in the realistic-fixture's output.

Read `SLICE_D_2_A.md` first (~6 min) for the constraint-formatter carbon-copy mechanism + the three patterns recognised. Then `ADMIRE.md`'s newly-appended entry (~3 min) for the V1 → V2 input-shape adaptation. The DECISIONS entry (~3 min) carries the resolved questions including the deferred extended-property + anonymous-default branches.

## D.2.b candidates — pick what the operator surfaces

D.2.a opens the arc; D.2.b's specifics depend on what aesthetic gaps the operator notices next. Most likely-surfaced gaps:

1. **`EXECUTE sys.sp_addextendedproperty` multi-line formatting.** V1 emits each EXEC with `@name=N'...', @value=N'...',` on the head line and `@level0type=N'SCHEMA',@level0name=N'dbo',` / `@level1type=N'TABLE',@level1name=N'X',` on indented continuation lines. V2 currently emits the entire EXEC as a single long line. Most visible operator-aesthetic gap remaining; high signal-to-noise ratio for the slice (one new pattern in `ConstraintFormatter.tryFormatLine`).

2. **Anonymous DEFAULT (no constraint name).** V2's IR has `Attribute.DefaultName : Name option`; the None case lands as ScriptDom-emitted `[col] type NULL DEFAULT (value)` without the CONSTRAINT prefix. The current formatter scans for `" CONSTRAINT ["` so anonymous defaults pass through unchanged. V1's fixture shows multi-line anonymous default — extend `tryFormatLine` with a `" DEFAULT ("`-keyword detection branch. ~30 min.

3. **CHECK constraint formatting.** V2 emits column-inline `CHECK (expr)` today; V1's fixture pattern for CHECK isn't visible in the canonical edge-case fixture but probably follows the same multi-line shape. Detect when an operator surfaces it.

4. **Composite PK / multi-column constraint.** V2's emitter uses ScriptDom's `UniqueConstraintDefinition`; for multi-column PK, the constraint emits at the table level (not column-inline) with column list. The current formatter handles single-column PK (column-inline detection) but not multi-column (the table-level `CONSTRAINT [PK] PRIMARY KEY ([col1], [col2])` line). Extend the table-level FK detection to also catch PRIMARY KEY at the line start. ~30 min.

## What's load-bearing from D.2.a

Carried-forward, still load-bearing:
- **Carbon-copy V1 with citation + ADMIRE row.** The slice's mechanism is the V1-self-containment + editorial-inheritance discipline operating at slice scope. Future emission-aesthetic slices that draw on V1 logic follow the same shape: file-header citation comment + ADMIRE.md entry + refactor freely from carbon-copy state.
- **Text post-processing at `Render.toText` terminal boundary is the canonical fit when ScriptDom's formatter can't express the desired shape.** Don't try to subclass `Sql160ScriptGenerator`; don't reach into reflection. The LINT-ALLOW substantive-rationale discipline names this boundary as the allowed exception, with substantive rationale.
- **V1's indentation conventions (4 / 8 / 12 spaces) are the unifying axis.** Every multi-line emission across the SSDT bundle (constraint formatting, future extended-property formatting, future CHECK formatting) follows the same 4-space-step hierarchy. The constraint formatter's `bodyIndent = indent + "    "` / `clauseIndent = indent + "        "` pattern is the precedent.

## Pitfalls D.2.a hit that you can avoid

- **The flat-stream emission gap (slice D.1.c finding) STILL applies if you add new statement types.** `SsdtDdlEmitter.statements` and `kindToSsdtFile` are two emission paths; per-kind statement types must yield from BOTH or the adjunction H-050 breaks. If you add new emission shapes for D.2.b (extended-property reformatting, etc.), confirm the new statements appear in both paths.
- **The 4 / 8 / 12 space conventions are CARBON-COPIED, not invented.** D.2.a's initial off-by-4 (16 spaces for ON DELETE/ON UPDATE instead of 12) was caught by sample emission inspection; the fix used V1's `ownerIndent + 4` formula directly. When extending the formatter, anchor against V1's source (`src/Osm.Smo/PerTableEmission/ConstraintFormatter.cs`) for indentation, not against your aesthetic intuition.
- **The formatter operates AFTER `Render.toText` accumulates ScriptDom-rendered text.** It's a STRING TRANSFORMATION; no access to ScriptDom AST state. Input is whatever ScriptDom produces; output is whatever V1's shape requires. When ScriptDom's emission changes (e.g., a future ScriptDom version produces different column-inline formatting), the formatter's input detection patterns need to update.

## Reading order (~15 min)

1. **`SLICE_D_2_A.md`** — slice doc; three patterns recognised; deferred items. ~6 min.
2. **`src/Projection.Targets.SSDT/ConstraintFormatter.fs`** — the F# port; file-header LINT-ALLOW + carbon-copy citation; three pattern handlers. ~5 min.
3. **`ADMIRE.md`** newly-appended entry — V1 source location + V2-growth delta documented. ~2 min.
4. **`tests/Fixtures/emission/edge-case/Modules/AppCore/dbo.Customer.sql`** (V1 reference fixture) — the elegant V1 output shape the F# port targets. ~2 min.

Hold the spine. Chapter D's emission-aesthetics arc is operator-visible polish on top of the structurally-correct emission D.1's three sub-slices delivered. The arc closes when V2's output matches V1's elegance across the operator's full set of canonical fixtures.

— The slice D.2.a architect.

---

# Handoff letter — 2026-05-23 (slice D.1.c CLOSED; chapter D's first arc complete)

To the next agent.

You're picking up V2 with **chapter D's logical-name-emission arc closed** — all three sub-slices shipped green this session, 2369 tests passing, 0 failures. D.1.a closed the substitution mechanism; D.1.b closed the extended-property recovery; D.1.c closed the canary triangle assertion. The operator-reality canary now exercises and verifies the full property — source DDL with logical-name extended properties → V2 substitutes + emits → deploys → reads back → triangle predicate fires green. V2's "operator-meaningful identifiers in deployed SSDT" claim is a structural artifact now, not aspirational.

The natural next question is **what chapter opens after chapter D's first arc**. The principal-PO has the call. This letter sets up the strategic decision and names the substantive follow-on candidates surfaced during the arc.

## Where you are in the spine of the work

Read `SLICE_D_1_C.md` first (~6 min) for the closing slice's mechanism + the triangle property's structural form. Then `SLICE_D_1_B.md` (~6 min) for the extended-property recovery path and the chain-order correction that landed mid-arc. Then `SLICE_D_1_A.md` (~5 min) for the substitution-vs-rename naming framing that's now structurally codified. The three DECISIONS entries (2026-05-23, sequential) carry the canonical decisions.

## What the chapter D arc shipped

**Three sub-slices, end-to-end:**
- **D.1.a**: `LogicalTableEmission` + `LogicalColumnEmission` (default-on Core passes, classified `OperatorIntent Emission`). V2 emits `[dbo].[Customer]([Email])` instead of `[dbo].[OSUSR_ABC_CUSTOMER]([EMAIL])`.
- **D.1.b**: `V2.LogicalName` extended-property emission per CREATE TABLE + per column. ReadSide's `readSchemaCombined` extended with a 5th batch joining `sys.extended_properties`; `buildKind` / `buildAttribute` hydrate `Kind.Name` / `Attribute.Name` from the property. Backward-compat fallback to deployed-name when absent.
- **D.1.c**: `PhysicalSchema.LogicalNameBindings` axis carries the logical-name binding through the diff surface. Canary fixtures (`canary-gate.sql` + `SourceSchema.realistic`) gain V2.LogicalName extended-property calls. New Docker-bound triangle canary asserts the property end-to-end.

**Plus mid-arc structural fixes:**
- **Chain order corrected** (D.1.b): both logical-emission passes now run BEFORE `TableRename` so operator pins dominate. D.1.a's stated-but-not-implemented contract.
- **Statement-stream emission gap closed** (D.1.c): the flat `statements` function was missing `extendedPropertyStatements` (only per-kind file emission included them). V2.LogicalName extended properties consequently never landed in `runWithReadback` / `Render.toText` deploys until D.1.c. Surfaced when the new `LogicalNameBindings` axis joined `isEqual` and the M3 V2-internal closure test failed immediately. The discipline: the adjunction `PhysicalSchema.ofCatalog c = ofStatementStream (SsdtDdlEmitter.statements c)` is structural; widening a PhysicalSchema axis requires extending both `ofCatalog` AND `ofStatementStream` AND the statement-stream emitter in lockstep.

## What's load-bearing for whatever opens next

Carried-forward, still load-bearing:
- **Substitution-vs-rename naming distinction.** Modules that AUTHOR new names share `*Rename` suffix; modules that SUBSTITUTE pre-existing catalog axes share `*Emission` suffix. Applies to any future pass that "expresses one catalog axis through another."
- **Default-on is operator intent.** Production chain wires the substitution passes Enabled by default; `Disabled` mode preserves diagnostic / V1-parity fallback. Both classified `OperatorIntent Emission`. Apply the framing to any future operator-overlay axis where the production default IS the operator's intent.
- **V2-namespace prefix on V2-internal extended properties.** `V2.LogicalName` is the first; future V2-internal annotations (`V2.<axis>`) follow the same naming convention. SQL Server reserves `MS_*` for system properties; the `V2.` namespace is safely distinct from operator-supplied properties.
- **Statement-stream emission must mirror per-kind file emission.** The `statements` / `emitSlices` divergence is a recurring failure shape. Adjunction is structural; the AdjunctionLawTests' H-050 surface should grow per-axis coverage as PhysicalSchema widens.

New from chapter D's arc (read the corresponding DECISIONS entries):
- **Read-the-substrate-before-committing (N=3 codification).** Slice docstrings that assert structural properties (chain order, dominance, precedence, layer-locality, adjunction) must be VERIFIED against the substrate before the slice closes. D.1.b caught the chain-order contradiction; D.1.c caught the statement-stream emission gap. Apply pre-emptively when claiming a structural property.
- **Triangle predicate scope = "as separate as the diff comparator allows."** The diff comparator computes the difference; the canary applies the property predicate. Don't conflate. Generalizes: when adding a new comparison axis to PhysicalSchema, the property assertions live in the canary tests, not in `PhysicalSchema.isEqual`.

## Candidate next-chapter opens (read at chapter-open)

These are the substantive forward signals surfaced during the chapter D arc. The principal-PO call is which to open first.

1. **Operator-overlay surface for the substitution toggle.** Today's logical-emission passes are wired Enabled at module-init in `RegisteredTransforms.allChainSteps`. Operators who want physical-name emission (diagnostic / V1-parity / specific-table fallback) need a config knob. Shape: `Config.PolicySection.LogicalNameEmission` taking values `Enabled | Disabled | PerKind of Map<SsKey, Mode>`. ~3-5 days; mirrors chapter C's binder-then-wire pattern.

2. **AdjunctionLawTests' H-050 widening.** Chapter D's slice D.1.c surfaced the adjunction's per-axis fragility. Today H-050 covers Columns + ForeignKeys axes; widening to include Rows, RowDigests, AND LogicalNameBindings would make the structural-property test catch future axis-emission gaps before they reach the canary. ~2-3 days; pure F# (no Docker dependency); high-leverage for forward-stability.

3. **Perf-gate baseline re-record.** Chapter D added ~17 extended-property statements per canary deploy (one per table + per column). No observable perf regression in the green test run, but the operator-reality 150-table canary at production scale adds ~2.5k extra EXEC statements per deploy. Run `PERF_GATE_RECORD=1 ./scripts/perf-gate.sh` to re-baseline; pair with a DECISIONS amendment naming the new floor. ~30 minutes; mechanical.

4. **CDC-silence verification for V2.LogicalName emissions.** Per chapter 4.1.B's CDC-silence-on-idempotent-redeploy property: V2 emits unchanged SSDT → no CDC captures fire. The V2.LogicalName extended-property emissions should be IDEMPOTENT (re-deploying the same V2.LogicalName value shouldn't fire CDC), but the test surface doesn't yet cover this axis. ~2-3 days; adds property test to existing `CdcSilenceCrossEmitterTests`.

5. **Tolerance taxonomy extension for non-V2-emitted schemas.** Pre-D.1.b deployed schemas (and non-V2-emitted schemas) have no V2.LogicalName extended properties; ReadSide's fallback derives `Kind.Name = deployed_name`. Canary comparisons against such schemas would show source's logical-name bindings (recovered from D.1.b extended-property hydration on the V2-emitted side) differing from target's fallback-derived bindings. A `Tolerance.LogicalNameRecoveryAbsent` variant + canary-side tolerance acceptance would let the canary compare V2-shape against legacy-shape gracefully. ~1 week; touches the chapter-4 Tolerance taxonomy.

## Reading order before chapter-D-arc-close conversation with the PO (~30 min)

1. **`SLICE_D_1_C.md`** — closing slice; triangle property structural form. ~6 min.
2. **`SLICE_D_1_B.md`** — recovery mechanism; chain-order correction. ~6 min.
3. **`SLICE_D_1_A.md`** — substitution mechanism; naming framing. ~5 min.
4. **The three DECISIONS entries** (2026-05-23, slice D.1.a / D.1.b / D.1.c) — canonical decisions. ~5 min.
5. **`tests/Projection.Tests/LogicalNameTriangleCanaryTests.fs`** — the triangle assertion shape; lives below the existing canary's M3 facts. ~3 min.
6. **`src/Projection.Core/PhysicalSchema.fs:128-180,231-271,395-420,448-456`** — the structural extension landed by D.1.c. ~5 min.

## Pitfalls D.1.c hit that you can avoid

- **F# namespace resolution doesn't auto-import child modules from the parent namespace.** D.1.c's `LogicalNameTriangleCanaryTests.fs` initially used `namespace Projection.Tests` + tried `SourceFixtures.SourceSchema.realistic` — compile failed because `SourceFixtures` is a namespace, not a module the namespace declaration brought in. The fix: use `module Projection.Tests.LogicalNameTriangleCanaryTests` at file level (mirrors `CanaryRoundTripTests.fs`), then `open Projection.Tests.SourceFixtures` (which makes the SourceSchema module visible). Generalizes: test files that consume `SourceFixtures.SourceSchema.*` should be module-shaped, not namespace-shaped.
- **fsproj compile order matters.** `LogicalNameTriangleCanaryTests` was initially placed at line 44 of the .fsproj — alphabetical placement put it before `Fixtures/SourceSchema.fs` (line 214). F# compile order is fsproj-order, not file-system-order. Tests that depend on shared fixtures must be listed AFTER the fixture files. Place new canary tests near `CanaryRoundTripTests.fs` (around line 216-218 in the current fsproj).
- **Triangle identity projection must drop the substitution-mutated axis.** Initial projection used `(Schema, Column, LogicalName)` — failed immediately because source's `Column = Some "ID"` differs from target's `Column = Some "Id"` under substitution. The correct projection is `(Schema, TableLogicalName, ColumnLogicalName option)` — drop the OSSYS-shape Column, use the table-level binding's LogicalName for table-identity, use the column-level binding's own LogicalName for column-identity.
- **The fixture augmentation surfaces operator-reality cleanly; the test surface needs guards.** D.1.c shipped two guard tests (source-side divergence; target-side substitution worked) alongside the main triangle test. Without the guards, the main test could pass trivially if the fixture-augmentation got reverted (no divergence to verify) or if the pipeline-emit silently fell through to raw-emit (no substitution to verify).

## Closing posture

Chapter D's first arc is the cleanest 3-sub-slice closure the project has shipped: each sub-slice ships green, builds on the prior, and the closing slice produces a structural artifact (the triangle predicate) that turns the product claim into a continuous verification surface. Hold the spine — the next chapter inherits a substrate where logical-name emission is verifiably correct on every canary run, and the operator-reality canary has a new axis of bite. Whatever opens next, this arc's discipline + structural patterns are now load-bearing for the chapters to come.

— The chapter-D-arc-close architect.

---

# Handoff letter — 2026-05-23 (slice D.1.b CLOSED; only D.1.c remaining in chapter D's first arc)

To the next agent.

You're picking up V2 with **two of three sub-slices in chapter D's logical-name-emission arc shipped green**. D.1.a (substitution mechanism) and D.1.b (V2.LogicalName extended-property roundtrip) are landed; you own D.1.c — the canary triangle assertion that closes the arc and makes the operator-reality canary verifiably bite on logical-name semantics. The substrate D.1.b leaves you is **clean** (2365 pass, 0 fail, 207 skip) and the end-to-end mechanism is **verified** (the 3 Docker-bound roundtrip tests at `LogicalNameRoundtripTests.fs` exercise source → emit → deploy → ReadSide read with full divergence recovery).

## Where you are in the spine of the work

Read `SLICE_D_1_B.md` first (~6 min). It documents the V2.LogicalName extended-property mechanism and the chain-order correction landed mid-D.1.b. Then read `SLICE_D_1_A.md` (~5 min) for the substitution-vs-rename framing carried into both slices. The full chapter D opening conversation that led here is in the `HANDOFF.md` letter below this one (the D.1.a closing letter); read that for the original three-sub-slice carve-out.

## D.1.c — your next slice

**Scope.** The operator-reality canary today uses a pure-physical fixture (`fixtures/canary-gate.sql` + `Projection.Tests.SourceFixtures.SourceSchema.realistic`). After D.1.a + D.1.b's mechanism is in place, the canary STILL passes trivially on that fixture because the substitution is a no-op when logical = physical from the source. D.1.c augments the canary to actually verify the logical-name emission triangle property.

**Three ends of the change.**

1. **Canary fixture augmentation (`fixtures/canary-gate.sql` + the realistic-generator source).** Add `EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'<logical>'` calls for every table + every column in the source DDL. The fixture's source catalog (after ReadSide reads it through D.1.b's recovery path) now has `Kind.Name = "Customer"` distinct from `Kind.Physical.Table = "OSUSR_S1S_CUSTOMER"`. This makes the fixture exercise the divergent case the substitution is designed to address. Same change to `GenerateSpec.operatorReality` (or wherever the 150-table fixture is generated) so the live perf-gate canary has the divergence too.

2. **`PhysicalSchema` widening (`src/Projection.Core/PhysicalSchema.fs`).** Add a fifth field: `LogicalNameBindings : Set<{ PhysicalTable: string; LogicalName: string }>` (or per-column equivalent — pick at slice open based on what the triangle assertion needs). The `ofCatalog` projection populates from `Kind.Name` + `Kind.Physical.Table`; the `ofPhysicalSchema` projection from the deployed schema reads the V2.LogicalName extended property via D.1.b's hydration path. Diff comparator extends set-difference to the fifth field.

3. **Triangle assertion in the canary test (`tests/Projection.Tests/CanaryRoundTripTests.fs` wide-canary path).** The current canary asserts `PhysicalSchema.isEqual source target`. Extend to assert the triangle on the LogicalNameBindings axis: for every binding in the source, the target has a binding with the same logical name; for every binding in the target, the physical-table value equals the logical-name value (this is what V2 substitution produces in the deployed schema). The comparator computes the diff; the canary applies the property predicate over the diff output.

**What to verify before committing the slice.** The triangle assertion must FAIL on a deliberately-broken catalog (mutate `Kind.Name` to something not equal to anything in the deployed schema before re-emitting) to confirm the property has bite. Then revert and confirm it passes on the genuine roundtrip.

**Pitfall the slice will surface.** The perf-gate baseline needs re-recording. D.1.b's extended-property emission adds 1 + N statements per kind; D.1.c's fixture augmentation adds the same on the source side; the live canary's deploy + read cycle gets longer. Run `PERF_GATE_RECORD=1 ./scripts/perf-gate.sh` after the fixture lands; commit the new `bench/baseline-canary.json`; pair the re-record with a DECISIONS amendment naming the new floor's rationale per the existing perf-gate protocol.

**Pitfall to avoid.** Don't try to extend `PhysicalSchema` to carry the full Kind catalog (`Kind.Name` + `Kind.Physical` as a structured pair). The existing shape (`Columns / ForeignKeys / Rows / RowDigests` as flat sets) is the pattern; mirror it (`LogicalNameBindings` as a flat set of `{ PhysicalTable; LogicalName }` records). The diff comparator stays as set-difference per field; the triangle property is applied AT THE CANARY (read the diff, apply the predicate). Keep concerns separated.

## What's load-bearing from D.1.a + D.1.b

Carried across both sub-slices, still load-bearing:
- **Substitution-vs-rename naming distinction.** Modules that AUTHOR new names share `*Rename` suffix (`TableRename`); modules that SUBSTITUTE pre-existing axes share `*Emission` suffix (`LogicalTableEmission` / `LogicalColumnEmission`). D.1.c might add `LogicalNameBindings` or `LogicalNameRecovery` — concept-shaped, sibling-friendly.
- **Default-on is operator intent in 2026.** Production chain wires `Enabled` for both logical-emission passes; `Disabled` mode preserves diagnostic / V1-parity emission. Both classified `OperatorIntent Emission`. Apply the same framing to any D.1.c operator-toggle: the production default IS the operator's intent; toggle is for narrow non-production scenarios.
- **Chain order matters for operator-pin dominance.** D.1.b corrected D.1.a's wiring. `LogicalTableEmission` + `LogicalColumnEmission` run BEFORE `TableRename` in the chain; the substitution lands first, the operator-supplied override writes last and dominates. D.1.c shouldn't touch this order; if it adds new passes, slot them after the existing logical-emission block but before TableRename (or after TableRename if the new pass shouldn't be overridden by operator pins).
- **`V2.` namespace prefix for V2-internal extended properties.** D.1.c's fixture augmentation uses the same `V2.LogicalName` property name. If D.1.c needs to add a NEW V2-internal property type (e.g., `V2.LogicalSchema` for schema-level recovery, currently out of scope), follow the same `V2.<axis>` convention.

New from D.1.b:
- **Read-the-substrate-before-committing (N=3 codification).** Chapter C codified this at N=2 (the "verify the architect's named layer against the substrate" discipline). D.1.b extends to N=3 — slice docstrings that assert structural properties (chain order, dominance, precedence, layer-locality) must be VERIFIED against the substrate before the slice closes. D.1.c will likely assert "the triangle property holds end-to-end through the canary"; walk the canary's full source → emit → deploy → read → diff pipeline to confirm before claiming the property.
- **`readSchemaCombined`'s single-round-trip envelope.** D.1.b extended the existing 4-batch combined command to 5 batches. If D.1.c needs additional schema-side queries (extended-property reads for the new fifth `PhysicalSchema` field, etc.), prefer adding a 6th batch over a separate `SqlCommand` — the round-trip cost is the constraint, not the batch count.

## Reading order (~25 min before you cut code)

1. **`SLICE_D_1_B.md`** — D.1.b's mechanism + the chain-order correction + what's deferred to D.1.c. ~6 min.
2. **`SLICE_D_1_A.md`** — substitution mechanism + the substitution-vs-rename framing. ~5 min.
3. **`DECISIONS 2026-05-23 (slice D.1.b — V2.LogicalName extended-property roundtrip)`** — canonical decisions. ~3 min.
4. **`src/Projection.Core/PhysicalSchema.fs:134-410`** — the type shape + diff comparator + Tolerance mechanism. ~5 min.
5. **`tests/Projection.Tests/CanaryRoundTripTests.fs`** — the wide-canary path; where the triangle assertion lands. ~3 min.
6. **`fixtures/canary-gate.sql` + `tests/Projection.Tests/SourceFixtures.fs` (`SourceSchema.realistic`)** — what to augment with `V2.LogicalName` calls. ~3 min.

## Pitfalls D.1.b hit that you can avoid

- **Be careful with bulk-sed on test fixtures.** D.1.b touched 4 failing tests; one of them required separating the fixture's `physicalName` field (preserved as OSSYS-shape) from the assertion strings (updated to logical names). D.1.b's sed-broadness pitfall recurred during the EmissionFoldersOverlayTests fix; the recovery was straightforward but the discipline is: when fixture-vs-assertion distinction matters, use per-file Edits with surrounding context.
- **Don't assume `Assert.DoesNotContain("sp_addextendedproperty", body)` survives D.1.b.** Every CREATE TABLE now carries `V2.LogicalName` statements. Two existing tests asserted absence of the call as proxy for "no extended properties emitted." Narrow such assertions to the specific property name you actually mean (e.g., `Assert.DoesNotContain("MS_Description", body)`).
- **`@level0type = N'SCHEMA'` count assertions need to isolate per-axis contribution.** D.1.b's table-level extended properties all carry SCHEMA segments. Tests that counted `@level0type = N'SCHEMA'` occurrences as proxy for "module-level properties" needed to count the module-property's distinctive VALUE instead. D.1.c's `PhysicalSchema` widening might surface similar count-assertion fragility in other test classes; prefer counting the distinctive value.

Hold the spine. Slice D.1.c is the closing arc — it's where logical-name emission becomes a verifiable property of every canary run, not just a unit-tested mechanism. The triangle assertion is the structural artifact that turns the slice's product claim ("V2 emits logical names through the operator-visible roundtrip") into a forcing function that fires on every commit.

— The slice D.1.b architect.

---

# Handoff letter — 2026-05-23 (slice D.1.a CLOSED; sub-slices D.1.b + D.1.c open)

To the next agent.

You're picking up V2 mid-Chapter D. Chapter D's framing is **operator-visible emission shape**: the SSDT artifacts V2 produces should carry operator-meaningful identifiers (ubiquitous-language names like `Customer.Email`) instead of the OSSYS storage shape (`OSUSR_ABC_CUSTOMER.EMAIL`) that V2 had been emitting through chapter C. Slice D.1 is the structural slice that closes that gap; the principal-PO carved it into three sub-slices at slice open and **only the first (D.1.a) has shipped**. Your job is D.1.b — and D.1.c after that — and you're inheriting a green tree (2359 pass, 0 fail, 207 skip) plus an architecturally-clean substitution mechanism that just needs end-to-end roundtrip recovery + canary teeth.

## Where you are in the spine of the work

Read `SLICE_D_1_A.md` first (~5 min). It carves the full slice into the three sub-slices and explains why D.1.a alone doesn't deliver the end-to-end product: the substitution works (V2 emits `[dbo].[Customer]` now) but **ReadSide can't recover the original logical-vs-physical divergence** because `ReadSide.fs:640` derives `Kind.Name = Name.create table` directly from the deployed physical name. Roundtrip: deploy `[dbo].[Customer]` → ReadSide reads → `Kind.Name = "Customer"`, `Kind.Physical.Table = "Customer"`. No record survives that the original source's `Kind.Physical.Table` was `OSUSR_ABC_CUSTOMER` while its `Kind.Name` was `Customer`. This means **the operator-reality canary cannot today verify logical-name emission** — its source fixture is pure-physical, the substitution is a no-op on that fixture, and the canary passes trivially. The bite arrives at D.1.c.

## D.1.b — your next slice

**Scope.** V2 emits a `V2.LogicalName` extended property on every deployed CREATE TABLE / column carrying the pre-substitution logical name. ReadSide queries the property and hydrates `Kind.Name` / `Attribute.Name` from it (backward-compat fallback to `Name.create table` when the property is absent). End-to-end roundtrip recovery: deploy-and-read recovers the original logical-vs-physical divergence.

**Two ends of the change.**

1. **Emitter side** (`Projection.Targets.SSDT`). The SSDT emitter already invokes `sp_addextendedproperty` for kind-level / column-level / index-level metadata at `ScriptDomBuild.fs:1241-1255`. Add a new extended-property entry whose name is `V2.LogicalName` (or whatever short canonical name you prefer — choose at slice open) and whose value is the PRE-substitution logical name. This means the `LogicalTableEmission` / `LogicalColumnEmission` passes need to either (a) record what they substituted in a side channel that emission reads, OR (b) the emitter reads `Kind.Name` / `Attribute.Name` directly at emission time (the logical name is still in the catalog after substitution — only `Physical.Table` / `Column.ColumnName` got rewritten). Option (b) is cleaner and likely the right answer: emission carries `Kind.Name` into the extended property without any side-channel needed.

2. **Reader side** (`Projection.Adapters.Sql/ReadSide.fs`). Today `ReadSide.fs:640` calls `Name.create table` unconditionally. Lift to: query `sys.extended_properties` for the `V2.LogicalName` property on every read table; when present, hydrate `Kind.Name` from the property value; when absent, fall back to the existing `Name.create table` behavior (backward-compat for pre-D.1.b deployed schemas). Same lift for column-level: query for `V2.LogicalName` on every column, hydrate `Attribute.Name` when present.

**What to verify before committing the slice.** Property roundtrip: a catalog with divergent logical/physical names → V2 emit → deploy → ReadSide read → catalog whose `Kind.Name` / `Attribute.Name` match the original (NOT derived from physical). Add a new test file `LogicalNameRoundtripTests.fs` covering the property; lives in `tests/Projection.Tests/`.

**Pitfall to avoid.** The existing `Kind.Description` / `Attribute.Description` extended-property emission landed at chapter A.0' slice α as carriage-only — `IR fidelity lift (L3-S9 descriptions sub-axiom)`. Don't accidentally entangle the new `V2.LogicalName` property with the existing description channel; they're different concerns. Use a distinct extended-property name; ReadSide reads them separately; emission writes them separately.

**Pitfall the slice will surface.** `Compose.aggregateSsdt` and the manifest emission both compose paths from `Kind.Physical.*` — after D.1.a these are logical; after D.1.b they're STILL logical (D.1.b doesn't change the substitution; it adds metadata for recovery). Manifests stay logical-named; this is correct. ReadSide-recovered catalogs after D.1.b will produce identical structural emission to the pre-deploy catalog (the roundtrip becomes symmetric on `Name` and `Physical` both).

## D.1.c — the slice after D.1.b

**Scope.** Canary fixture augmented with logical-name extended properties; `PhysicalSchema` gains a `LogicalNameBinding` set; diff comparator amended to assert the triangle (`source.Kind.Name = target.Kind.Name = target.Kind.Physical.Table`) on top of existing set-differences. Perf-gate baseline re-recorded.

**Architectural sketch.** Today `PhysicalSchema` carries `Columns / ForeignKeys / Rows / RowDigests` (`PhysicalSchema.fs:134-155`). Add a fifth field: `LogicalNameBindings : Set<{ PhysicalTable: string; LogicalName: string }>`. Diff comparator extends set-difference to the fifth field. Triangle assertion lives in the canary test (`CanaryRoundTripTests.fs`'s wide-canary path), not in the comparator itself — the comparator computes the diff; the canary asserts the triangle property holds against the diff output.

**Canary fixture augmentation.** `canary-gate.sql` / `SourceSchema.realistic` add `sp_addextendedproperty` invocations carrying `V2.LogicalName` for every table/column. D.1.b's ReadSide extension queries these on readback; the canary's source catalog now has `Kind.Name = "Customer"` (from the property) and `Kind.Physical.Table = "OSUSR_ABC_CUSTOMER"` (from the deployed name) — distinct values. After V2's pipeline runs and re-emits, the target catalog has `Kind.Name = "Customer"` and `Kind.Physical.Table = "Customer"`. The triangle holds.

**Perf-gate baseline re-record.** Per `scripts/perf-gate.sh` — `PERF_GATE_RECORD=1 ./perf-gate.sh` captures N warm runs after the fixture change; commit the new `bench/baseline-canary.json`. Expected delta: small bump from the extra extended-property SQL emission (~5-10ms warm). Per `DECISIONS 2026-05-10 — Perf-gate μ+σ statistical baseline` pair the re-record with a DECISIONS amendment naming the new floor's rationale.

## Reading order (~20 min before you cut code)

1. **`SLICE_D_1_A.md`** — what shipped, what didn't, the sub-slice carve-out and why. ~5 min.
2. **`DECISIONS 2026-05-23 (slice D.1.a — logical-name emission as default)`** — the canonical decisions: substitution is operator intent; module names follow operator-visible effect not mechanism; closed-DU expansion absorbed cleanly. ~3 min.
3. **`src/Projection.Core/Passes/LogicalTableEmission.fs` + `LogicalColumnEmission.fs`** — the substitution mechanism. Both modules' docstrings explicitly call out the substitution-vs-rename distinction. Sister-passes; learn one, you know both. ~5 min.
4. **`src/Projection.Adapters.Sql/ReadSide.fs:640` and surrounding ~50 lines** — the load-bearing site for D.1.b. The current `Name.create table` IS the gap D.1.b closes. ~3 min.
5. **`src/Projection.Targets.SSDT/ScriptDomBuild.fs:1241-1255` and the call sites** — the existing extended-property emission seam. D.1.b extends here with a new property entry. ~5 min.

## Disciplines you'll need

Carried-forward from the broader codebase (still load-bearing):
- HANDOFF.md is append-only within a chapter; prepend new letters; never overwrite with Write. You're benefiting from this discipline right now (this letter prepends; the C.4-C.6 close letter survives below).
- "Handoff message" = forward-looking letter, second-person, problem-oriented. This letter addresses YOU directly with "what you need to know to do D.1.b"; the structure is forward-looking, not a backward-looking status report on D.1.a.
- Test-failure capture protocol — TRX-first when `dotnet test` reports `Failed: N`. Slice D.1.a used it once and the 10 failing tests came back classified in seconds.
- Closed-DU expansion empirical-test discipline — F# exhaustiveness errors light up only at match sites that genuinely care. Slice D.1.a widened `TransformKind` with `ColumnPhysicallyRenamed` and zero match sites needed updating (all had `_` wildcards). Apply the same discipline if D.1.b widens any closed DU.
- AxiomTests entry alignment — every new behavioral property gets an AxiomTests citation entry alongside the test file. D.1.a added `L3-Emission-Logical (slice D.1.a)`; D.1.b adds something like `L3-Emission-LogicalRoundtrip (slice D.1.b)`.

New from D.1.a (read the corresponding DECISIONS entry for full prose):
- **Substitution vs rename naming distinction.** Passes that AUTHOR new names share the `*Rename` suffix (operator supplies new target via `RenameSpec`); passes that SUBSTITUTE pre-existing catalog axes share the `*Emission` suffix. D.1.b might add `LogicalNameRecovery` (ReadSide-side recovery of the logical name from extended properties) — concept-shaped, operator-visible-effect-named, sibling-friendly with `LogicalTableEmission` / `LogicalColumnEmission`.
- **Mode parameter as toggle seam over runtime config injection.** `Enabled | Disabled` captured at registration time. D.1.b's ReadSide extension might want a similar mode for the property-lookup-vs-fallback behavior; consider the same `Mode` shape if a toggle surfaces.
- **Default-on is operator intent in 2026.** Production chain wires `Enabled`; `Disabled` is the diagnostic / V1-parity fallback. Both classified `OperatorIntent Emission`. Apply the same framing to D.1.b: ReadSide property-lookup IS the production default; falling back to `Name.create table` is backward-compat for pre-D.1.b deployed schemas, not a configuration knob.

## Pitfalls D.1.a hit that you can avoid

- **Don't carry over naming patterns from existing modules without re-applying the four-question domain-naming analysis.** D.1.a originally named the new passes `TableRenameToLogical` / `ColumnRenameToLogical` because that was the closest sibling pattern (`TableRename`'s shape). Principal-PO flagged the misnomer mid-implementation. The fix: when adding a sibling pass, articulate what the sibling REPRESENTS in the domain before adopting the existing module name's pattern. The failure mode is **misnomer-by-inheritance**.
- **Don't broaden a fixture sed when you only want to change assertions.** D.1.a's `sed -i 's/OSUSR_APPCORE_USER/User/g'` on test files initially rewrote both the fixture JSON's `physicalName` field AND the assertion strings. The fixture's purpose was to test logical/physical divergence; rewriting the fixture eliminated the divergence. Caught quickly via re-read; the fix was to restore `OSUSR_*` in fixture JSON while keeping logical names in assertions. **Use narrow sed boundaries or per-file edits when fixture-vs-assertion distinction matters.**
- **`PrimitiveType` doesn't have `String` — it has `Text`.** Tripped up a test fixture mid-D.1.a. The compiler caught it instantly via `TreatWarningsAsErrors=true`; just naming so the next agent doesn't waste cycles on the same wrong-guess.

Hold the spine. Slice D.1.a closes the substitution mechanism; D.1.b closes the recovery mechanism; D.1.c closes the verification mechanism. Each is independently shippable and each builds on the prior. The product outcome (V2 emits logical names AND the canary verifies the roundtrip) lands at D.1.c.

— The slice D.1.a architect.

---

# Handoff letter — 2026-05-20 (Chapter C CLOSED; phase A1 operator-config wiring sweep complete)

To the next agent.

You're picking up V2 with **Chapter C closed**. All six operator-overlay axes named at chapter B.4's mid-chapter strategic exploration are wired through `Compose.runWithConfig`: tightening (C.1), special-circumstances allowlists (C.2), emission-folders (C.3), tag-groups (C.4), insertion semantics (C.5), and verbosity + per-category mute (C.6). The chapter-close ritual ran in full: `CHAPTER_C_CLOSE.md` synthesis published, prior chapter letters archived at `HANDOFF_CHAPTER_C.md`, Active deferrals scanned clean, no CLAUDE.md / README.md drift. **Test baseline: 1871/1871 non-Docker passing; 0 warnings under `TreatWarningsAsErrors=true`.**

The natural next question is **what chapter opens next.** Five candidates surfaced at chapter close (see `CHAPTER_C_CLOSE.md` §"Open questions for the next chapter's opening"). The principal-PO has the call. This letter sets you up to bring the chapter-open conversation to the principal-PO with the substantive context already in hand.

## What's load-bearing across chapter C's contribution

**The Pipeline-as-overlay-realization-layer architectural pattern.** Across the six slices a single architectural seam recurred:
- Operator-supplied textual config (`Config.fs` section)
- → Dedicated binder in Pipeline (`<Axis>Binding.fromConfig`) resolving textual refs against the loaded catalog + validating shape-level invariants
- → Typed runtime overlay (`<Axis>` record / DU in Pipeline)
- → Applied at the Pipeline-layer realization boundary (chain filter / bundle rewrite / policy aggregation)

The Core (`Projection.Core`) carries the DataIntent kernel; the Pipeline carries the OperatorIntent realization. Future slices touching operator-overlay axes — insertion-consumer wiring, per-emitter group filtering, additional `TransformGroup` variants — should mirror this seam.

**The "verify the architect's named consumer layer against the substrate" discipline.** Codified at N=2 (C.3 + C.4 both found the architect's HANDOFF recommendation wrong-by-one-layer). The recipe is **read the substrate end-to-end before committing.** When a future architect's letter names a consumer-shape, validate it by walking the substrate from the relevant boundary IN both directions before committing to the recipe. The recommended layer might be one layer up (C.4: Pipeline tag map vs Core registry field) or one layer down (C.3: typed `ArtifactByKind<SsdtFile>` vs post-compose `Map<string, string>`).

**The annotate-don't-suppress discipline operationalized.** Every operator-overlay axis preserves the underlying structural value AND annotates the operator intent via metadata. C.2 stamps `Metadata.acceptedVia` on allowlisted diagnostics; C.3 preserves basename + smart-constructor invariant on rewrite; C.4's `passTags` map is a side annotation, not a structural mutation. Carries forward to any operator-overlay slice where source data could be occluded.

**The "wiring-without-downstream-consumer is a valid slice shape" discipline.** C.5 ships the `Policy.Insertion` binder + threading through the `Policy` record but no pass/emitter consumes it yet — the operator-facing surface lands so hand-edited configs produce no surprises; consumer wiring follows under concrete operator-pull pressure. Pairs with IR-grows-under-evidence; counters the "wait for the consumer" alternative that would delay operator-facing surfaces.

## Five candidate chapter-open conversations (read these before bringing options to the PO)

(1) **C.5 downstream consumer wiring as chapter D scope.** `Policy.Insertion` is wired but no pass/emitter reads it. The natural consumer is `DataEmissionComposer.dispatch` (chapter 4.1.B slice η). The composer today reads `EmissionPolicy.DataComposition` (`AllRemaining | AllExceptStatic | AllData`) which is structurally distinct from `InsertionPolicy` (`SchemaOnly | InsertNew | Merge | TruncateAndInsert`). The wiring shape: a per-emitter execution-mode resolver consults `Policy.Insertion` to choose between INSERT vs MERGE vs TRUNCATE+INSERT at MigrationDependencies + Bootstrap + StaticSeeds emission time. Estimate: ~1 week focused slice. **Most concrete; lowest risk; ships the chapter-C surface end-to-end.**

(2) **Faker emitter promotion (trigger structurally met since chapter B.3).** Three new evidence types shipped at slice B.3.8 + slice B.3.5's StatisticalMoments lift; the deferred-trigger fires structurally. Decision pending operator-pull at chapter B.4 / chapter C close — **deferred through both.** Principal-PO call on whether to open Chapter D as Faker promotion or hold longer. Estimate: 2-3 weeks (synthetic-data Π carbon-copy from V1 + tests + canary integration).

(3) **L3 axiom promotion cycle (verifiability-triangle audit refresh).** Two candidates from chapter C's pattern repetition: **L3-CC-AcceptanceAnnotation** (every operator-visible structural finding with an addressable acceptance path carries the annotation; the finding remains visible) and **L3-CC-ApplyLayerLocality** (operator overlays apply at the layer carrying the typed identity the override is keyed by). Promoting requires a verifiability-triangle audit dispatch (5 agents per `DECISIONS 2026-05-12 — Verifiability-triangle audit methodology`). Estimate: ~3-5 days for the audit + ~1 week for codification + per-axiom property tests.

(4) **`LiveOssysConnection` cluster.** Blocked on operator managed-environment access. When access opens, the cluster (live OSSYS path + multi-env + UAT-users + user-reflow strategy + extraction-time knobs) lands as a follow-up chapter and closes Phase B's functional-equivalence arm (per `CHAPTER_B_4_CLOSE.md` §"Phase B exit gate status"). Estimate: 4-6 weeks; not chapter-D candidate unless operator surfaces access pre-cutover.

(5) **TransformGroup DU expansion + per-emitter filtering.** Today's C.4 chain-filter only excludes PASSES whose tags intersect disabled groups. If operator-pull surfaces for "toggle MigrationDependencies emission off without using `EmissionPolicy.EmitData = false`," that's a new `TransformGroup` variant + new emitter-side filtering mechanism. Estimate: ~3-5 days focused slice (the structural seam already exists; the work is the closed-DU expansion + the emitter-side filter at the chain boundary).

## Reading order (~45 min)

1. **This letter** — 5-min context for the chapter-D opening conversation.
2. **`CHAPTER_C_CLOSE.md` (~150 lines)** — full close synthesis: substantive contributions, disciplines codified, the chapter-close ritual's 8 items, test baseline, what's deferred, the five candidate open conversations.
3. **`HANDOFF_CHAPTER_C.md` (~410 lines)** — the chapter's per-slice architect letters preserved in chronological order (C.4-C.5-C.6 at top; C.3; C.2; C.1; chapter-B.4 close letter at bottom). Read for the per-slice architect narratives — these are where the substrate-verification discipline got codified slice-by-slice + where each architectural decision was named.
4. **The four key files chapter C added at the Pipeline layer:**
   - `src/Projection.Pipeline/SpecialCircumstancesBinding.fs` (~165 LOC) — the binder template
   - `src/Projection.Pipeline/SpecialCircumstancesDiagnostics.fs` (~140 LOC) — the post-chain-scan pattern
   - `src/Projection.Pipeline/EmissionFoldersBinding.fs` (~190 LOC) — the structured folder-validation pattern + binder
   - `src/Projection.Pipeline/TransformGroupsBinding.fs` (~125 LOC) — the Pipeline-layer tag-map pattern + closed-DU binder
5. **`DECISIONS 2026-05-20 (slices C.4 + C.5 + C.6)` consolidated entry + the per-slice entries above it** — the chapter's substantive resolved questions + discipline codifications.

## Disciplines internalized by the time you finish chapter C

Carried-forward from prior chapters (still load-bearing):
- HANDOFF.md is append-only within a chapter; rotates at close (you're the beneficiary of this discipline — `HANDOFF_CHAPTER_C.md` carries the chapter's letter history).
- "Handoff message" = forward-looking letter, not backward-looking status report (this letter addresses you in the second person; the structure is "what you need to know to do your work," not "what we did").
- Test-failure capture protocol — TRX-first when `dotnet test` reports `Failed: N` (used in this chapter when C.4's initial passTags map had stale names; the TRX surfaced the actual mismatched names in seconds).
- Closed-DU expansion empirical-test discipline — applied to C.4's `TransformGroup` DU (2 variants reflect today's evidence; no speculative pre-population).
- IR-grows-under-evidence — applied to C.5's wiring-without-consumer scope; applied to C.4's minimal DU seed.

New from chapter C (read the relevant DECISIONS entries):
- **Pillar 9 separation at the Pipeline boundary**: operator-overlay axes' typed runtime values live in Pipeline; Core stays DataIntent-pure.
- **Verify the architect's named layer against the substrate (N=2)**: codified at slice-C.3 + slice-C.4-C.6 DECISIONS entries.
- **Structured-error sub-codes for taxonomy**: dot-suffix codes when one validation step has multiple distinct rule violations.
- **Wiring-without-downstream-consumer as a valid slice shape**: codified at slice-C.4-C.6 DECISIONS entry.
- **Mute-before-accumulator-update ordering invariant**: codified at slice-C.4-C.6 DECISIONS entry.

## Pitfalls chapter C hit that you can avoid

- **Don't assume `setVerbose true` semantics survive a refactor.** The existing `LogSinkTests` test "trace and debug surface when setVerbose true" expects all-on behavior. C.6's `Verbosity` DU split could have broken this (Verbose was conceptually below Debug); keeping the back-compat shim `setVerbose true → Verbosity.Debug` preserved the test. Any future LogSink refactor that touches the `setVerbose`-equivalent API needs the same back-compat check.
- **`Map.singleton` doesn't exist in F#.** Use `Map.ofList [ k, v ]`. C.4 tests hit this once; the build error pointed it out, but a fresh agent might assume parity with `Set.singleton`. Defensive pattern: when reaching for a `Map`-construction primitive, double-check `Map.<primitive>` exists before committing.
- **Closed-DU case names can collide across DUs in the same module.** `Tightening` collides between `OverlayAxis` and `TransformGroup`; `Debug` collides between `Level` and `Verbosity`. Solution: `[<RequireQualifiedAccess>]` on the secondary DU. Worth checking when adding new DUs alongside existing ones.
- **Don't write tests that depend on Bench/LogSink mutable state without `[<Xunit.Collection("Global-MutableState")>]`.** The collection serializes the tests; without it, parallel xUnit execution can intermix two tests' LogSink writes. C.6's `LogSinkVerbosityTests` ships with the collection attribute; mirror for any future LogSink test.

Hold the spine. Phase A1 (operator-config wiring) closes with chapter C; phase A2's shape is the next-chapter conversation. Bring the five candidate open conversations to the principal-PO with the substantive context already in hand.

— The chapter C architect (chapter close).
