# Twin succession: witness, not twin

*A design decision record, written 2026-10-15, for handing the SSDT proving workflow to a team that keeps
skill files but not an F# codebase. It is the result of a nine-agent panel: four researchers (the
skill tree's Twin dependencies, the per-operation data requirements, the `v3-build` branch, and the
Visual Studio / Copilot / MCP / LocalDB platform facts), three architects (zero-code floor, small C#
tool, knowledge layer), and two adversaries (wrong verdicts, verified on SQL Server 2022 with
sqlpackage 170.5.96; and the successor team's two-year survival). It proposes; the operator decides.*

---

## 0. The answer

The Twin can be retired from the proof loop entirely, and nothing that decides a verdict goes with it.

A schema change is classified by what DacFx's publish does to the data a copy holds. That publish is
**existence-sensitive, not distribution-sensitive**: the `BlockOnPossibleDataLoss` guard is
`IF EXISTS (SELECT TOP 1 1 FROM <table>) RAISERROR(...,16,127)`, and constraint validation fails on
the first violating row. So the disposable copy needs **witnesses** (one row in each populated table
the change touches, and one row of each violation class that some environment actually holds), not a
synthetic estate. The witnesses are planted from **facts measured read-only** in the real
environments (counts turned into booleans; no value ever leaves an environment). v3 had already named
those facts `ExistingData`.

The minimum proof surface is therefore:

1. **A LocalDB version at or above the estate project's target platform.** The sample targets Sql160,
   which needs LocalDB 2022 or later. Visual Studio installs LocalDB 2019, and only as an optional
   component.
2. **Microsoft-signed binaries already in the toolchain**: sqlpackage, SqlLocalDB, MSBuild (via
   vswhere), and sqlcmd.
3. **One PowerShell script, `prove.ps1`.** It runs under ConstrainedLanguage mode and is about 600
   lines. It orchestrates the loop and writes one `verdict.json`.
4. **About 450 lines of T-SQL the team can read:**
   - `plant.sql` plants the witnesses;
   - `probes/*.sql` measures the facts.
5. **A slim knowledge bundle**, about 25 files and under 2,000 lines, in which every operation skill
   carries a `## Proof plan` (what to measure, what to plant, which signal to read).
6. **The pipeline's own effective publish options**, captured from the Octopus step and never
   hand-written.

There is no Twin, no F#, no Docker, no Node, no bacpac and no MCP. Section 4 explains why the
mechanism is a script and not a compiled tool.

---

## 1. Why the Twin can go: the evidence

| Claim | Receipt |
|---|---|
| The Twin decides no verdict. | `ssdt-agent/skills/talk-to-local-sql/SKILL.md:94-99`: "The Twin is the BEFORE state; sqlpackage is the proof." |
| The decisive rows are hand-planted, not minted. | `ssdt-agent/proving-ground/Data/Seed.sql:3-6`: "These rows are the WHOLE POINT." |
| The Twin mint skips seeded tables. | `Twin.Runtime/Mint.fs:110`; `Runs.fs:232-248` |
| The Twin cannot plant violations. | Its post-mint trust gate refuses them (`Runs.fs:252-258`). |
| The Twin cannot run on the corporate network. | It needs Docker: `Runs.up` always calls `TwinContainer.ensureRunning` (`docker inspect` / `docker run`), and the connection is hard-wired to `localhost,<port>` with `sa`. |
| The Twin rides about 114K lines of F# kernel. | — |
| The data-loss guard is data-blind. | `V3_ARCHITECTURE.md` §2.2; findings F1, F7 |
| Volume never flips a verdict; it only affects timing. | F20: volume only feeds the ">1M rows: schedule a window" scrutiny line. |
| The Twin's realism can lose the witnesses a verdict needs. | Its laws push toward clean data (zero orphans, a trust gate, values fitted to width, NULLs drawn by rate). A violator rarer than 1/volume vanishes. |
| A copy built without the Twin from schema plus seeds is wrong for the real estate. | Its business tables are empty, which gives false "clean" verdicts on every tightening op. Witnesses close exactly this gap. |

v3 reached the same conclusion independently. Its planned "existing-data tier" was "a row where the
environment has rows, a null where it has nulls, an orphan where it has orphans"
(`origin/claude/v3-build:V3_MILESTONES.md` §3, ruling N5).

## 2. What the local proof is for

Octopus publishes with `BlockOnPossibleDataLoss` on. **That publish is the final proof; the local one
is a rehearsal.** The rehearsal earns its keep three ways:

1. **It predicts how the change ships before release night.** Without it, the change ships as written
   and the block is discovered in Octopus.
2. **It catches green-but-harmful publishes the guard cannot see:**
   - the lag-window revert (F17);
   - a pre-deploy script's side effect surviving a failed deploy (F2, F6);
   - environment-only constraints and indexes silently dropped;
   - a table renamed without a refactorlog entry, which leaves a phantom and stranded data;
   - a narrowing that silently trims trailing spaces;
   - the seed re-asserting rows (F15);
   - CDC capture-instance drift.
3. **It tells the dev lead plainly what was not checked.**

It must also guard against one more thing. **A failed deploy is not atomic under DacFx defaults.**
Verified on 2026-10-15, with `IncludeTransactionalScripts` at its default (false):

- a blocked unique-index build left the index dropped and the column altered;
- even with transactional scripts on, a Msg 547 at `WITH CHECK CHECK` lands after the commit,
  leaving everything else applied and the constraint untrusted.

So a wrong local "clean" can become a half-applied schema in an upper environment. That is why the
effective Octopus options are the first thing to capture (§10).

## 3. The loop

Who can read which environment decides who runs the loop:

- **Developers can read Dev.** The author runs the loop against Dev.
- **Dev leads can read QA and UAT.** The lead re-runs steps 2–6 against QA and UAT during review (§8).
- **Nobody can read Prod yet.** Prod gets the both-worlds treatment in step 7.

1. **Build head.** Use MSBuild via vswhere for the classic `.sqlproj`. VS 2026 supports only the
   classic format. For a two-release program, also build the **final target (R2)**: R1's model diff
   is empty by construction, so the facts must come from where the program is going.
2. **Read the environment's real delta** (read-only):
   - `sqlpackage /Action:Extract` of the environment's schema gives `base_<env>.dacpac`;
   - `/Action:Script` of head against the environment gives `delta_<env>.sql`, the delta that
     environment will actually receive.

   This catches:
   - R1 and R2 both merged before QA deploys (F2 in disguise);
   - cutover drift in QA and UAT;
   - untrusted or disabled constraints;
   - environment-only objects the deploy will drop.

   If the environment can't be reached, fall back to the merge-base build and record
   "Not checked: <env> drift."
3. **Derive the preconditions from the delta's statements, not from a model diff.** The model diff
   misses data-reading DDL that has no guard. Each statement maps to a precondition:

   | Statement in the delta | Precondition |
   |---|---|
   | Every guard table | TableEmpty |
   | `ALTER COLUMN … NOT NULL` | NotNull |
   | `ALTER COLUMN` with a new type, length, precision or collation | Fits |
   | `WITH CHECK CHECK CONSTRAINT` | FK or Check |
   | `CREATE UNIQUE … INDEX` / `ADD … UNIQUE` / `PRIMARY KEY` | Unique |
   | a `PERSISTED` computed column | Convertible |
   | a `tmp_ms_xx` rebuild | every column moved |

   A statement that reads rows and maps to no precondition makes the verdict **indeterminate**,
   never clean. The delta is machine-generated and regular, so pattern matching it is sound under a
   pinned DacFx, with a corpus test on every bump.
4. **Measure the facts** (read-only):
   - **Queries:** templated aggregate probes, `COUNT_BIG` / `SUM(CASE…)` only. Identifiers come from
     the delta, are checked with `COL_LENGTH`, and are bracket-quoted. Use `TRY_CONVERT` only, never
     `CONVERT`/`CAST`, whose errors quote values. Answers are integers only; SQL Server error messages
     are never recorded. Connect with `ApplicationIntent=ReadOnly`.
   - **Metadata:** server, database and column collation; trust and disabled flags;
     `is_tracked_by_cdc`; the `sys.partitions` row tier; version and compatibility level.
   - **Value classes for Fits:** over-length by LEN; over by trailing spaces only; `''`;
     non-convertible; round-trip loss.
   - **Output:** `existing-data.json`, stamped with login, time and probe text.
   - **A missing object stops the run.** It is never read as zero.
5. **Make the copy and plant the witnesses:**
   - **Base:** publish `base_<env>.dacpac` to a fresh LocalDB database. The `.bak` is cached by base
     fingerprint, so later runs restore in seconds.
   - **Plant:** `plant.sql` plants one valid presence row per populated touched table, plus its
     foreign-key ancestors, then one witness per measured violation class. CHECK predicates,
     triggers, odd types, seed-Id collisions and merge cardinality go in a hand-written,
     **DML-only** `witness.sql`, guided by the op skill's Proof plan.
   - **Then two checks:**
     - re-measure the copy with the same probes and refuse if its states differ from the target
       facts;
     - script `base_<env>` against the copy and require an empty plan. This schema-identity check
       stops a witness file from "fixing" the schema.
   - **Never re-publish the base after planting.** That double-applies scripts the environment
     already ran, and it erases the witnesses (verified).
6. **Prove** the delta with the **effective Octopus options**:
   - **Classify by message number.** A Msg 50000 at state 127 counts as the guard only when the
     failing batch is a guard batch in the delta. The other numbers:
     - 547: FK or CHECK
     - 515: NULL into a NOT NULL column
     - 1505 / 1750: unique build
     - 2627: seed duplicate
     - 2628, 8152, 8115: truncation, overflow
     - 245: conversion
     - 207, 544, 8672: seed column, IDENTITY_INSERT, MERGE
     - 1946: index key length
     - 4928, 4711: CDC
     - SQL71516: refused at build
     - anything unknown: indeterminate
   - **After any block,** script head against the copy and record **what was left applied, by
     phase**.
   - **Optionally run Permissive:** flip only `BlockOnPossibleDataLoss` to observe the consequence.
   - **Take a content hash** before and after, order-independent and keeping NULL distinct from `''`.
   - **For two-release programs,** prove R1, then replay R1 (a non-empty second plan is the F17
     hazard, recorded as such), then R2.
7. **Record:**
   - one proof per distinct (delta, facts) pair, so identical environments share a proof;
   - `verdict.json`, `delta_<env>.sql`, `existing-data.json` and `witness.sql` go under
     `proofs/<slug>/`, written by the script and not curated by hand;
   - the author-pr skill maps `verdict.json` onto the ten-section record.
   - **Prod:** prove **both worlds** (populated and clean; populated and violating). Record the Msg
     the violating world would hit and the probe to run before promoting.

## 4. The mechanism: a script, not an exe, and not MCP

The panel split on this question. The successor-team adversary settled it.

| Option | Verdict |
|---|---|
| **Zero code** (skill files, plus the agent composing 10–15 commands per change) | Correct but nondeterministic. A VS 2022 agent re-assembles `(localdb)\` escaping and log greps every time, with 10–15 approvals per change and no single place to fix a bug. Kept only as `PROVING.md`, the manual page describing what `prove.ps1` runs. |
| **C# exe** (about 1,000 lines, DacFx in-process, self-contained, vendored) | Technically the strongest: structured DacFx messages, ScriptDom parsing, `DacProfile.Load`. Most likely to die on day 1 and in year 2. It is an unsigned, low-prevalence binary in `C:\src`, which AppLocker default rules and Defender ASR target. It is about 100 MB, so it needs Git LFS and a signing owner. Rebuilding it needs the .NET 10 SDK and NuGet through the proxy. It is opaque to Copilot. Keep the design and the donor parts (Appendix B) as the escalation path. |
| **MCP server** | The GitHub "MCP servers in Copilot" policy is **off by default** for Business and Enterprise. Tools are off per user, and the server needs re-trust on every change. It can never be the floor. Dropped; revisit only if the org enables the policy *and* the team asks. |
| **`prove.ps1`** (recommended) | Calls only Microsoft-signed binaries on allowed paths. Copilot can read and fix it in the same chat. The team can debug it. One mechanism, no fallback to rot. **Write it ConstrainedLanguage-safe from day one:** native exes, cmdlets, `[xml]`, `[regex]`, `ConvertTo-Json`, no SqlClient objects. CI runs it under ConstrainedLanguage as a test. |

**The trade, named honestly.** Matching on the generated delta is less robust than ScriptDom. Under
a pinned DacFx, with the corpus test on every bump, it is sound.

**The decision gate.** A one-day spike on a real team laptop checks:

- whether an unsigned exe from the repository runs;
- `$ExecutionContext.SessionState.LanguageMode`;
- `Get-ExecutionPolicy -List`;
- `sqllocaldb versions`;
- whether sqlpackage and sqlcmd are present;
- whether the laptop can reach QA and UAT.

If the laptops run FullLanguage PowerShell, `prove.ps1` may also load the Microsoft-signed ScriptDom
assembly that ships beside sqlpackage, and parse instead of pattern-match. If signed internal exes
turn out to be routine, the C# design becomes viable again.

**Note for future agents.** This repository's V2 pillars ("data-structure-oriented over
string-parsing", typed-AST-first) govern `sidecar/projection/`. They do not govern the successor's
tooling, which optimizes for **legibility to the team that keeps it**. Do not "correct" `prove.ps1`
by importing V2's discipline.

## 5. The witness alphabet (compact)

Full table: the panel's witness report. **E/P** means the table is empty / holds at least one row.

| Op family | Witnesses | Signal |
|---|---|---|
| make-mandatory, narrow, delete-attribute, add-mandatory | P. For R1: a NULL; over-length by LEN; over by trailing space only (silent trim); precision or scale overflow | Msg 50000 guard; 515 / 2628 / 8115 in R1 |
| add-check; create-fk (clean or orphan); junction | a row with the predicate FALSE (UNKNOWN passes); an orphan | 547 at `WITH CHECK CHECK` (non-atomic) |
| add-unique, define-pk, modify-index to unique | an equal pair; 2 NULLs (plain index); case, accent and trailing-space twins under the **environment's** collation | 1505 (+1750) |
| retype-explicit, computed PERSISTED | non-convertible; `''` (TRY_CONVERT gives 0 or 1900-01-01 for int and date, NULL for decimal and GUID); lossy round-trip | 245 / guard; a lossy result shows only in the hash |
| rename-*, move-schema, delete-entity | P (so a loss or phantom is visible) | sp_rename vs DROP+CREATE in the delta; a phantom table left behind |
| identity-swap | gapped ids, not starting at 1; child rows | the `tmp_ms_xx` rebuild; IDENT_CURRENT ≥ MAX |
| seed ops | an Id taken by another Code in some environment; a NULL and a `''` | 2627 / 8672; silence on redeploy |
| change-delete-rule, toggle-trust | a 3-level chain; an orphan under a legacy NOCHECK | the DROP → NOCHECK ADD → CHECK CHECK sequence; trust read back |
| split, merge, move-attribute, extract-to-lookup, archive | value classes; an absorbed row with no parent; more rows than one batch | hash conservation; absorbed count vs distinct parents |
| add-optional, make-optional, widen, defaults, drop-constraint ops, create-entity | P (the hash stays unchanged) | clean |

## 6. What it cannot prove (the record's "Not checked")

- **Prod:** it has no reader. Record the both-worlds result and the probe to run before promoting.
- **Volume:**
  - timing;
  - transaction-log size;
  - `CommandTimeout` (60 by default) at more than 1M rows;
  - index-build locks.

  Estimate these from the `sys.partitions` row tier, not from LocalDB.
- **CDC:** LocalDB/Express has none. A touched table with `is_tracked_by_cdc` is a principal-led
  change.
- **Platform differences:**
  - server or tempdb collation for `#temp` tables in scripts (lint for `COLLATE DATABASE_DEFAULT`);
  - edition features (ONLINE=ON);
  - compatibility level.
- **Workload state:** index usage (drop-index), fragmentation (rebuild-index).
- **Change over time:** data between measurement and promotion.
- **The engine version:** the Octopus DacFx version, which governs constraint trust (162 vs 170).
- **Outside the database:** the Integration Studio refresh (the second half of every change), and
  external consumers such as SSIS.

## 7. The knowledge layer

**What the team will read for one change:**
- the router, indirectly through Copilot;
- one op skill;
- that op's sample PR;
- the error table when something breaks.

So the bundle vendored into the SSDT repository is about **25 files and under 2,000 lines**:

| File | Purpose |
|---|---|
| `.github/copilot-instructions.md` (≤80 lines) | The rules and the route |
| `.github/prompts/schema-change.prompt.md` | The entry point on VS 2022 and VS 2026 |
| `.github/prompts/review-schema-pr.prompt.md` | The reviewer's entry point |
| `ssdt/PROVING.md` (≤150) | Commands, exit codes, and an error → fix table |
| `ssdt/RECORD.md` (≤120) | The ten-section PR record, plus the Azure DevOps PR template |
| `ssdt/FINDINGS.md` (≤150) | F1–F20, one line each with receipts linked to the archive tag, plus the witness amendments |
| `.github/skills/<op>/SKILL.md` × 12 (≤60 each) | The common ops, each with its Proof plan. VS 2026 discovers them; VS 2022 reaches them by path from the prompt. No duplication |
| `ssdt/LONG_TAIL.md` | Every other op: "principal-led", with a link to the archived skill |
| `ledgers/in-flight.md`, `ledgers/toolchain.md` | The only ledgers kept |

The 12 common ops: add-optional, add-mandatory, make-mandatory, make-optional, widen, narrow,
retype, create-entity, create-fk, add-unique, add-index, rename-attribute, delete-attribute, plus the
seed ops.

**Removed from the vendored bundle:**
- all Twin, Docker, warm-container, bash, Node and F# references;
- `self-test/` (replaced by a 10-prompt `SMOKE.md`);
- PORTABILITY, CONNECTORS, ACCELERANT_PLAN, ENABLEMENT_PROGRAM and the dated reviews;
- the Node package generator (replaced by a ≤20-line PowerShell check that fails on
  `twin|docker|warm-sql|11433|\.fs\b|\.mjs`);
- `bake.mjs` (no bacpac is made any more).

"First time an op has shipped" becomes an Azure DevOps PR-label query, and the reviewer roster moves
into branch policy.

**Corrections the slim tree must carry:**
1. `skills/deploy-scripts/SKILL.md:87` claims pre- and post-deploy scripts roll back with the deploy.
   That is false (F2/F6, and verified for 1505 and 547).
2. `_index/tightening-class:29-30` and `_index/constraint-is-a-claim:98` claim clean populated
   constraint adds are refused. The live proofs published green.
3. `PORTABILITY.md:57` claims the non-Twin bake "carries the same counts, nulls, duplicates,
   orphans". It does not.

## 8. Gates

- **The lead's review extends the proof.** Only leads can read QA and UAT, so reproducing a proof
  means extending it to the environments the author cannot see:
  - run `prove.ps1` against QA and UAT;
  - if their delta or facts differ from Dev's, re-prove there;
  - the record gains those environments' lines.

  This gives the review substance beyond reading.
- **PR pipeline** (pinned hosted Windows image, LocalDB present):
  1. Re-derive the preconditions from head against the merge-base.
  2. Refuse if the committed facts don't cover them, or if they are stale.
  3. Rebuild the copy from the committed facts and `witness.sql`, then re-prove.
  4. Fail on blocked or refused.
  5. Report, but don't fail on, a mismatch with the author's verdict.

  It can't reach the environments, so it is authority over the **proof**, not over the **facts**.
  **Break glass:** when the gate is red for tooling reasons (exit 4, 6, 7 or 9), a lead may merge with
  a "Not checked: tooling" record, and the override is logged. Without an owned break-glass path, the
  branch policy gets switched off "temporarily" and stays off.
- **Octopus pre-flight** (phase 2, highest value per line):
  - a read-only `prove.ps1 predict` step before "Deploy DACPAC", on the Octopus worker, which can
    reach the target;
  - it scripts the release against the target, runs the guard and constraint probes, and stops
    *before* a non-atomic failure can half-apply;
  - it is also where the lag-window lock lives: Octopus release ordering makes R2 the only allowed
    next release after R1.
- **The inverted-verdict template must not be instantiated as written:**
  `ssdt-agent/pipelines/ssdt-agent-pr-validation.yml` restores the substrate into `<base>_<fp>` (line
  54) and then proves into a new, empty `$(Build.BuildId)_pr` (line 57). Every populated tightening
  would pass.

## 9. What retires, what is archived, what is donated

| Item | Disposition |
|---|---|
| Twin (`sidecar/projection/src/Twin.*`) and the F# sidecar | Archive: tag the source repository, add an "archived, unmaintained" banner, never vendor. Its laws remain good engineering; they are not on the proof path |
| The Twin's broader promise ("a local database full of realistic data") | Consciously dropped. OutSystems apps run on the platform against real environments, so realistic local data is not on the proof path. If ever needed again, it is a separate project |
| `ssdt-agent/scripts/*.mjs` | `prove.mjs` is the donor for `prove.ps1` (build detection, delta signals, exit vocabulary). The rest goes |
| `origin/claude/v3-build` (`dbchange`) | Tag `v3-donor`, then close. It is a sound design the team could not keep. Appendix B lists the parts worth lifting if the C# path is ever taken |
| `ssdt-playbook/` | Move to the ADO wiki as human curriculum. Skills stop citing it |

## 10. The handoff

**Day 1 (no code; the IT calendar is the critical path, at 1–3 weeks):**
1. Capture the Octopus step's **effective** options: the profile XML *plus* `/p:` overrides *plus*
   per-environment SQLCMD values. Also capture its **DacFx version**, and write both into
   `ledgers/toolchain.md`.
2. File four tickets:
   - read-only logins for QA and UAT (VIEW DEFINITION, `xp_instance_regread` for Script, and aggregate
     probes), plus a network route from the laptops;
   - LocalDB 2022+ for the team (or `AllowIncompatiblePlatform=True` on local copies only, stamped in
     the record);
   - confirmation of the Copilot "Editor preview features" policy;
   - a named Prod-reader owner.
3. Run the language-mode and exe spike on one senior's laptop (§4).
4. Make sure the inverted pipeline template is not live.

**Week 1:**
- Port `prove.mjs` to `prove.ps1` (ConstrainedLanguage-safe).
- Write `plant.sql` (presence plus NULL, duplicate, orphan, over-length and CHECK) and the probes.
- Run the loop on the real estate.
- Build the PR pipeline with break-glass.
- Day 5: a senior drives "make Email required" end to end while Danny stays silent. Record it, and
  fix every stumble.

**Weeks 2–4** (if there's time):
- Facts on QA and UAT.
- Value classes and the remaining data-sensitive ops' Proof plans.
- Three recordings with the principal driving: authoring, reviewing, and maintaining (bump the
  sqlpackage pin, fix a red build).
- Each senior ships one change solo.
- Danny answers only in writing.
- Delete whatever nobody touched.
- Publish an ownership table (pipeline, pins, in-flight ledger, break-glass approver), each owner with
  a backup.

**If there is only one week:** do Day 1 and Week 1, and cut everything else. The make-mandatory
recording on the real estate is the one act that cannot happen after Danny leaves.

**The actual window (decided 2026-10-15): about one week, with the F# turnover on day 1.**

1. **Before the turnover:**
   - run the laptop check (Appendix C) on one senior's laptop, once in Visual Studio's Terminal
     window;
   - read the Octopus step's effective options and DacFx version.
2. **Turnover day:**
   - Present the F# sidecar and the Twin as **archived reference, not a handoff**. Nobody inherits
     them.
   - Present this document as the successor path.
   - Confirm that the two principals own `prove.ps1`, the pins and the in-flight ledger: the on-team
     primary, with the supporting-team backup.
   - File the tickets the laptop check calls for.
3. **Days 2–4:**
   - Write `prove.ps1`, `plant.sql` and the probes, and run them on the real estate against Dev.
   - Strip the Twin, Docker, F# and Node paths from the vendored tree.
   - Write Proof plans for five ops: make-mandatory, narrow, create-fk, add-unique, delete-attribute.
     Everything else routes to `LONG_TAIL.md`.
4. **Day 5:** a senior drives make-mandatory end to end while Danny stays silent, and it is
   recorded. A lead extends it to QA and UAT.
5. **Deferred to the principals, in writing:**
   - **The PR pipeline gate.** Don't leave a half-built gate with no owner: a red gate nobody can fix
     gets switched off.
   - The Octopus pre-flight.
   - The remaining Proof plans.

## 11. Decisions only the operator can make

1. **Script or compiled tool. Decided:** PowerShell. The laptop check (Appendix C) confirms the
   language mode.
2. **How long until the handoff. Decided:** about one week, with the F# turnover on day 1 (§10).
3. **Who can reach what. Decided:** developers read Dev; dev leads read QA and UAT. The lead's review
   extends the proof (§8).
4. **Prod policy. Open:** prove both worlds (recommended), assume the worst, or wait for a reader.
5. **Ownership. Decided:** two principals, a primary on the team and a backup on the supporting team.
   They need not be named here. What matters:
   - each has said yes to owning `prove.ps1`, the pins and the in-flight ledger;
   - break-glass approval sits with the on-team primary and a dev lead, not with the cross-team
     backup.

---

## Appendix A — Red-team findings and their disposition

| # | Finding (verified on SQL Server 2022 + sqlpackage 170.5.96 where marked ✓) | Disposition |
|---|---|---|
| 1 | Proving against main misses the delta each environment receives (R1+R2 merged, drift, a disabled FK ✓) | §3.2 Script against each environment; Octopus pre-flight |
| 2 | A two-release R1 has an empty model diff, so nothing is planted | §3.1 derive from the R2 target |
| 3 | Data-reading DDL with no guard: PERSISTED computed columns ✓, collation change on a unique column ✓, `tmp_ms_xx` rebuild ✓ | §3.3 derive from delta statements; unmapped → indeterminate |
| 4 | The profile XML is not the effective options; failed deploys are not atomic ✓ | Day 1 capture; §3.6 record what was left applied |
| 5 | A union of environments builds a state no environment has | §3: no union; one proof per distinct (delta, facts) |
| 6 | CDC: 4928 on sp_rename ✓, 4711 on TRUNCATE ✓, a silently stale capture instance ✓ | Facts carry `is_tracked_by_cdc`; principal-led |
| 7 | CI cannot measure the facts it trusts | Facts stamped; pre-flight at promotion; §8 wording |
| 8 | A missing object read as empty | Stop; never zero |
| 9 | Re-publishing the base after planting erases witnesses ✓ | Rejected |
| 10 | Facts too coarse: TRY_CONVERT('') varies by type ✓; a narrowing silently trims trailing spaces ✓ | Value classes for Fits |
| 11 | `witness.sql` can change the schema | Schema-identity check; DML-only |
| 12 | Msg 50000 state 127 can be counterfeited by a user RAISERROR | Guard only if the failing batch is a guard batch |
| 13 | The copy takes the dacpac's collation ✓; `#temp` joins hit Msg 468 against the server collation ✓ | Facts carry collations; lint; residual |
| 14 | Environment-only objects dropped silently ✓; a rename without a refactorlog entry leaves a phantom | Drops listed from the environment's delta; refactorlog doctrine |
| 15 | Seed Ids differ per environment, causing 2627 | A seed-op probe |
| 16 | Prod unmeasured; an R1 replay is the F17 hazard; Msg 1945 warns now, 1946 fails later ✓ | Both worlds; classify as F17; a record line |

## Appendix B — Donor parts, if the C# path is ever taken

From `origin/claude/v3-build`:

- **`io/LocalServer.cs`** (about 60 lines worth lifting): LocalDB create and drop with 180 s timeouts,
  `SINGLE_USER WITH ROLLBACK IMMEDIATE`, `ClearPool`, and an unpooled `master` connection.
- **`io/PublishProfiles.cs`** (about 70): load the pipeline profile, strip the target and password,
  flip one flag.
- **`io/DacFx.cs`** (about 150): publish and message capture.
- **`kernel/DacFxMessage.cs`** (64): finds "Msg N" inside SQL72014. A Strict block arrives with no
  SqlException inside.
- **`io/SqlServer.cs`** 765–997: an integer-only probe allowlist.
- **`io/Telemetry.cs`**.

Lessons any implementation keeps:

- LocalDB's first connection can time out, so retry.
- DacFx and SqlClient refuse InvariantGlobalization; run under InvariantCulture instead.
- Set `DACFX_TELEMETRY_OPTOUT` before DacFx loads.
- Load dacpacs from a stream.
- Never keep a hand-written Strict profile.
- The post-deploy MERGE can itself throw and mask the result.

## Appendix C — The laptop check

Paste this into a PowerShell window opened in the SSDT repository folder; do not save it as a file
first. Execution policy does not apply to pasted commands, so the check can report on a laptop where
script files are blocked. Run it once more in Visual Studio's Terminal window, where Copilot's
commands run.

It is read-only: it writes one temporary file in the current folder, runs it, and deletes it. It uses
only cmdlets and property reads, so it works under ConstrainedLanguage mode.

```powershell
"--- PowerShell"
"Version:              $($PSVersionTable.PSVersion)"
"Language (console):   $($ExecutionContext.SessionState.LanguageMode)"
Get-ExecutionPolicy -List | Format-Table -AutoSize | Out-String

# The test that matters: a script FILE in the repository folder, where prove.ps1 will live.
$probe = Join-Path (Get-Location) 'lm-probe.ps1'
Set-Content -Path $probe -Value '"Language (script):    $($ExecutionContext.SessionState.LanguageMode)"'
try   { & $probe }
catch { "Script file blocked:  $($_.Exception.Message)" }
Remove-Item $probe -ErrorAction SilentlyContinue

"--- Application control"
try {
    $dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop
    "WDAC user mode:       $($dg.UsermodeCodeIntegrityPolicyEnforcementStatus)  (0 off, 1 audit, 2 enforced)"
} catch { "WDAC user mode:       not readable" }
try {
    $al = Get-AppLockerPolicy -Effective -Xml -ErrorAction Stop
    "AppLocker scripts:    $($al -match 'Type="Script"[^>]*EnforcementMode="Enabled"')"
    "AppLocker exes:       $($al -match 'Type="Exe"[^>]*EnforcementMode="Enabled"')"
} catch { "AppLocker:            not readable" }

"--- Tools"
if (Get-Command sqllocaldb -ErrorAction SilentlyContinue) { "LocalDB versions:"; & sqllocaldb versions }
else { "LocalDB:              not installed" }
foreach ($tool in 'sqlpackage', 'sqlcmd', 'msbuild') {
    $found = Get-Command $tool -ErrorAction SilentlyContinue
    if ($found) { "${tool}: $($found.Source)" } else { "${tool}: not on PATH" }
}
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    foreach ($vs in (& $vswhere -all -property installationPath)) {
        "Visual Studio:        $vs"
        $dac = Join-Path $vs 'Common7\IDE\Extensions\Microsoft\SQLDB\DAC'
        Get-ChildItem -Path $dac -Recurse -Filter SqlPackage.exe -ErrorAction SilentlyContinue |
            ForEach-Object { "  bundled sqlpackage: $($_.FullName)" }
    }
}
```

How to read the output:

| You see | It means | What `prove.ps1` does about it |
|---|---|---|
| `Language (script): FullLanguage` | No application control on scripts in this folder | Nothing to design around; it may load the ScriptDom DLL that ships beside sqlpackage |
| `Language (script): ConstrainedLanguage` | AppLocker or WDAC script rules apply | Fine: it is written ConstrainedLanguage-safe |
| `Script file blocked: …running scripts is disabled…`, with `MachinePolicy` and `UserPolicy` both `Undefined` | The execution policy is only a local default | Run it as `powershell -ExecutionPolicy Bypass -File prove.ps1`, or set `CurrentUser` to `RemoteSigned` |
| `Script file blocked`, with `MachinePolicy` or `UserPolicy` set | Group Policy pins the execution policy | The script must be signed with the org certificate (a ticket) |
| No LocalDB version at 16.x or above (2019 is 15.x; 2022 is 16.x; 2025 is 17.x) | It cannot host a Sql160 project under the pipeline profile | Request the SqlLocalDB 2022 MSI (needs admin), or use `AllowIncompatiblePlatform=True` on local copies only, stamped in every record |
| No `sqlpackage` on PATH and no bundled copy | — | Request the pinned sqlpackage zip through an Azure DevOps Artifacts feed |
| `AppLocker exes: True` | An unsigned exe in the repository folder would be blocked | Confirms the decision not to ship an exe |
