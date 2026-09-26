# SSDT_REMEDIATION_HANDOFF.md — kickoff letter for the next agent

> **Written 2026-07-16.** You are picking up the **SSDT emission remediation campaign** — turning
> the reviewed, dev-lead-blessed decision register into landed code, one work package at a time.
> The research is done and the plan is agreed; your job is to **implement WPs and get a lot done**.
> Read this once, then work from the canonical surfaces it points to. Second person, forward-looking.

---

## 1 — What this is, in sixty seconds

The F# sidecar (`sidecar/projection`) projects an OutSystems estate into an SSDT bundle (logical
names) + data lanes, racing toward an **eject** after which there is no upstream. A three-round
review produced a decision register and a **remediation plan of record**: 17 work packages
(WP-1 … WP-17) that close conspicuous deviations from OutSystems source intent — foreign-key
reality, empty-string preservation, NVARCHAR fidelity, clustering capture, scalar carriage, and
more. The manager (Danny) and the SSDT dev leads have signed off on the plan; nothing here is
speculative — it's a build queue.

**The one governing idea:** every emission decision should either faithfully mirror database
reality or name its divergence — never deviate silently. Most WPs are "make the silent thing
faithful, or refuse loudly."

## 2 — Where things stand right now

- **Branch:** `claude/fsharp-sidecar-emission-review-4spub5` (open **PR #669**). Rebased on the
  latest `main` (`d7f4566`, the estate chapter).
- **Landed:** the decision register + two companion docs (see §3), and **WP-1a** — the first code
  fix: `MetadataSnapshotRunner.toBundle` no longer hardcodes `HasDbConstraint = true`, so the FK
  evidence gate can distinguish logical-only from source-backed references on a **live** extraction
  (commit `f066754`; DECISIONS 2026-07-16; pure test `ReferenceHasDbConstraintLivePathTests`).
- **Not started:** WP-1(b–e) and WP-2 … WP-17.

## 3 — Your source of truth (read these, in order)

1. **`SSDT_HANDOFF_REVIEW_PACKET.md`** — the decision register (rows A1…H7, C11) + **§10, the WP
   plan of record**. Each WP has scope + "done means." §10 is your backlog. §11 is the operational
   runbook (how schema+data actually reach the target DB). §6 is the strawman eject config.
2. **`SCALAR_REPRESENTATION_AUDIT.md`** — the per-scalar × per-hop V1/V2 carriage catalog behind
   WP-17 (and rows C4/C11). Its §7 hazards and §8 witness gaps ARE the WP-17 task list.
3. **`DECISIONS.md`** — append-only, newest at bottom (`## YYYY-MM-DD — Title`). Read the last ~10
   entries + the 2026-07-16 WP-1a entry. **Every code change that moves behavior lands with a
   DECISIONS entry in the same commit.**
4. **`THE_GOLDEN_EMISSION.md`** — the blessing protocol (§2) and the variance inventory (§4). Any
   emission change lands as a reviewed golden byte-diff.
5. **`CLAUDE.md` §4** — the survival rules (the will-bite-you-today list). Non-negotiable.

## 4 — The disciplines you MUST follow (this repo is strict)

- **Blessing protocol.** An emission change is a **golden byte-diff**. Regenerate with
  `GOLDEN_RECORD=1` on the golden test run, review the diff, and land it **with a DECISIONS note
  naming the intent**. An unexplained golden diff is a defect, full stop.
- **DECISIONS entry in the same commit** as any behavior change (house style: `## YYYY-MM-DD —
  Title`, `**Context.**`, `**The decision.**`, `**Witness.**`, scope note; append at EOF).
- **Every AXIOMS.md change carries its `AxiomTests.fs` entry** in the same commit.
- **Typed-AST-first.** No string-building for SQL/JSON/XML — ScriptDom / typed writers only. The
  lint guardrail (`scripts/lint-discipline.sh`, `scripts/run-analyzers.sh`) enforces it; LINT-ALLOW
  needs the four-question rationale.
- **Name every refusal; downgrades are never silent.** A "faithful-or-refuse" WP means the refusal
  path carries a named diagnostic code (`category.subject.problem`), not a silent drop.
- **No inert config.** A fail-closed config surface must not carry decorative switches (this is why
  WP-1 kills the placebo knobs).

## 5 — Build & test reality (learn from my scars)

- SDK is pinned `9.0.314` (`global.json`). `dotnet build Projection.sln -c Debug` works but F#
  builds are **slow** (first pass >2 min) — expect it, launch in the background.
- **Tests: use `scripts/test.sh`, never a raw solution-wide `dotnet test`** (it OOM-kills the host —
  survival rule #1). Modes: `fast` (pure), `docker`, `focus <FQN-substring>`, `all`, `status`.
  Launch **bare in the background**, poll `scripts/test.sh status` — never `| tail` (survival rule #3).
- **⚠ The wedge I hit, so you don't waste an hour:** some *pure-project* classes (e.g.
  `OssysComprehensiveFixtureTests`) spin up **Testcontainers** directly. In an environment without a
  healthy warm SQL container they **cold-start and wedge** (`scripts/test.sh status` shows "no output
  >120s"). This is **infra, not your logic.** Mitigations: run a **warm container** first
  (`scripts/warm-sql.sh restart`, then `scripts/test.sh` auto-detects `PROJECTION_MSSQL_CONN_STR`),
  OR validate your change with a **Docker-free `focus <pattern>`** on the specific pure classes your
  change touches (this is how WP-1a was verified: `focus HasDbConstraint` → 3s green). For a real
  regression sweep, get the warm container healthy and run `scripts/test.sh all`.
- **On any failure, re-run with the TRX logger and grep the TRX** — console interleaves and lies
  (survival rule #4). `scripts/test.sh` captures the TRX and surfaces failed names for you.
- FS3511 in Release: no `let rec` inside `task { }`, no tuple `let!` (survival rule #5). `[<Literal>]`
  only on CLR primitives (rule #6). `{ X.create … with … }` silently inherits defaults — count fields
  at reconstruction sites (rule #7).

## 6 — Recommended sequence (highest leverage first; each is one commit, code+test+golden+DECISIONS)

The plan's own note: **WP-1a precedes everything that reasons about the logical-vs-backed split** —
it's landed, so the split is now real on the live path. From here:

**Tier 1 — finish the headline (FK reality), the mission's core:**
- **WP-1(b): emit the reflected `#FkReality.DeleteAction`** for physically-backed FKs (it's
  extracted at `MetadataSnapshotRunner.fs` and consumed nowhere today); when the model's rule code
  disagrees with the deployed action, keep the reflected action + raise a named divergence
  diagnostic. Pure-testable at the `toBundle`/reference seam. *Highest fidelity value.*
- **WP-1(d): resolve the placebo knobs** — make `treatMissingDeleteRuleAsIgnore` real (missing →
  Ignore = no FK) or delete it; implement-or-remove `allowCrossCatalog`; delete dead `strictMode`.
  Small, pure, satisfying.
- **WP-1(c/e): the evidence-gated eject posture + the msg-1785 cascade-path pre-analyzer** — larger;
  the posture is config/emission-shaping, the 1785 analyzer is a new pre-emit diagnostic.

**Tier 2 — quick self-contained wins (build momentum; pure tests + one golden re-record each):**
- **WP-16** — table-name collision tripwire (mirror the existing FK-name tripwire in
  `SsdtDdlEmitter.fs`). Locked in; pure.
- **WP-8** — PK naming `PK_<LogicalTable>_<KeyCols>` (V1's convention; `SmoIndexBuilder.cs:42-55` is
  the reference). One golden re-record (every PK name changes).
- **WP-11** — identifier-budget closure for pass-through names (fit-or-refuse).

**Tier 3 — the fidelity cluster (the conspicuous deviations Danny cares most about):**
- **WP-3** — stop erasing `'' → NULL` (the universal sentinel in `SqlLiteral.ofRaw` / `Bulk.parseRaw`);
  preserve `N''` end-to-end (V1 parity), handle the single-space sentinel deliberately, retire the
  `EmptyTextNormalizedToNull` tolerance. Touches the codec + a tolerance retirement + goldens.
- **WP-4** — `email`/`phone` → `NVARCHAR(250)/(20)` + on-disk type precedence (`OssysTypeMapping.fs`).
- **WP-9** — synthesize `DF_`/`CK_` names + reproduce untrusted-CHECK state + refuse on CHECK parse
  failure. **WP-10** — DB-level UNIQUE constraints re-emit as constraints (not indexes).

**Tier 4 — the larger fidelity/machinery WPs** (scope from the estate inventory first, §9 of the
packet — several bite only on DBA/External-Entity columns): WP-2 (clustering capture), WP-5 (silent
object classes), WP-6 (ScriptDom literal-safe rewrites incl. trigger bodies), WP-17 (scalar
carriage — Float/Real/DateTimeOffset/Xml + explicit-CAST temporal literals), WP-7, WP-12, WP-13,
WP-14, WP-15. Plus the two eject-blockers outside the WP list but flagged in the packet: **G3**
(wire the refactorlog into the bundle/.sqlproj — highest-stakes gap) and **G6** (`CREATE SCHEMA`).

Do **one WP per commit.** Read the WP's "done means" in §10 before starting; when you finish, tick
its inline `⚑` in the packet and refresh the §8 gap list. If a WP turns out larger than a clean
commit (WP-1c, WP-3), split it and say so in the DECISIONS entry.

## 7 — A worked example of the loop (WP-1a, so you can copy the rhythm)

1. Located the exact site (`MetadataSnapshotRunner.fs`, the `references` map in `toBundle`).
2. Confirmed the fix is safe (default emitted output unchanged; only a *configured* intervention
   changes behavior) and found the test seam (`toBundle` is public; other callers are pure).
3. Made the one-line fix + tightened two doc comments to match.
4. Wrote a **pure** regression test at the exact seam (backed → true, logical-only → false, siblings
   intact), added it to the `.fsproj` (F# needs explicit compile order).
5. Verified Docker-free: `focus HasDbConstraint` green in 3s.
6. Appended the DECISIONS entry (context / decision / witness / scope).
7. Committed all four files together, pushed to PR #669.

## 8 — Guardrails for the campaign

- **Don't stack new commits on a merged PR.** #669 is open; keep pushing to its branch. If it merges
  before you're done, restart the branch from `origin/main` (same name) for follow-up WPs.
- **Keep the packet honest.** It restates for review; when it disagrees with code/DECISIONS, they win.
  If you change behavior a register row describes, update the row in the same commit.
- **Scope by estate reality.** Before WP-2/5/12/17, run the §9 pre-eject audits (sys-catalog sweeps
  for triggers, computed/persisted, temporal, sequences, composite-PK FK targets, float/real/xml/
  datetimeoffset columns, >128 names, cross-module dup entity names). Several WPs are no-ops on a
  pure-native estate and load-bearing only at the External-Entities boundary — size them first.
- **When you're unsure whether a change should mirror reality or refuse,** the answer is almost
  always: mirror when you faithfully can, refuse loudly (named code) when you can't, and never
  silently coerce. That single rule resolves most WP ambiguities.

Go land WPs. The plan is good; the disciplines are the load-bearing structure that lets each commit
carry weight. Hold the spine.
