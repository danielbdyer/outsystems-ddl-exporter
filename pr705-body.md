# v3 on one branch: the build from M0, the parity ledger, and the waves since 2026-10-19

## Summary

This branch carries everything v3 has, so one review and one merge bring it to `main`: v3's build (#704, M0 and M1), the Facsimile (#706), the tool-neutral vocabulary (#707), the Facsimile written into the plan, and the parity ledger `V3_PARITY.md`, held by `Budgets.Tests: ParityResolves`. Since 2026-10-19 the build continues on this branch in waves, each a set of work packages implemented test first in worktrees, verified by independent reviewers by lens (the plan, safety, the budgets), fixed for at most two rounds, and merged here. This description is the build's checklist and is refreshed after every wave. CI is green on every check at `e65acd0`.

## The build, milestone by milestone

| Milestone | State on this branch | What waits, and on whom |
|---|---|---|
| M0 Foundation | built (#704) | the operator's `.claude/settings.json` and hooks |
| M1 Read | built (#704) | exit 1, the laptop check against Dev (`NEXT.md` has the commands) |
| M2 Predict | WPs 2.1's first slice, 2.2, 2.3 and 2.4 landed; 2.1's second slice and 2.6 are verified and merge in wave 7 | WP 2.5 (`predict`, `classify`, `check cdc`) and M2's five exits, in wave 8 |
| M3 Synthetic copy | WPs 3.1, 3.4 (with spike S4) and 3.8 landed; WP 3.5 (`synthetic-copy up`) finishes its last review findings in wave 7 | WPs 3.6 and 3.7 in wave 8; exit 1 on a laptop with LocalDB 16.x |
| M7 Agent instructions | WP 7.1 (the citation test's rules for what WP 7.2 moves, and value O3's test) finishes in wave 7 | WP 7.2's first slice in wave 8 |
| M4 to M6 | not started | M2 and M3 |

### Waves

- [x] **B1, the per-file budget** (`c1ce443..ef22af1`): kernel, io and cli reflowed to 140 characters a line; `io/SqlServer.cs` (997 lines) and `io/Ssdt.cs` (767) split by concept into partial files under 500 lines; `tests/Io.Tests` files over 800 lines split. Test counts unchanged on both lanes.
- [x] **Wave 2** (`..f66521f`): WP 2.1a; the local server without Docker (SQL Server 2022 Express installed natively on Linux by `ci/sql.sh up`, LocalDB on Windows, io starting no server); the per-file ceilings as tests; the operator's rulings of 2026-10-19 recorded.
- [x] **CI fix** (`d19daab`): `ci/sql.sh up` exited silently on a runner that never had mssql-server, because `dpkg-query`'s exit status reached `errexit` through `pipefail`.
- [x] **Wave 3** (`2fd68cf`, `7c5f151`): H3, SQL Server 2022's `master.dacpac` and `msdb.dacpac` in the tool folder (DF-5), `Coordinates` under the collation, and wave 2's review findings; WP 2.4, kernel `Classify` recognizing an operation from its `recognize:` block and refusing a rename made by typing.
- [x] **Wave 4** (`9391327..1f2a47e`): io refuses DacFx's `xp_msver` call before SqlClient sends it, so the read-only principal sends no `EXEC` (the operator's option A of 2026-10-20); WP 2.3, `io/AggregateQueries`, one ScriptDom builder per precondition kind; WP 3.1, kernel `Schema` and `Order`; spike S2 on DacFx 162.5.57 and 170.5.96; `io/DacFx.Scripted`, which gives DacFx its script paths per call, since two concurrent plans on one database name lost a script on Windows.
- [x] **Wave 5** (`a1a09ca`, `89085f6`): H5, the findings of waves 3 and 4; WP 2.2, `io/BlockOnPossibleDataLoss`, which reads each check from DacFx's own script and runs its predicate read-only.
- [x] **Wave 6** (`2a3dd5b..bb1360c`): WP 3.8, kernel `Witness`, the existing-data tier's plan (one valid row where the environment has rows, one violating row per precondition answered violating, ordinal literals, no `Random`, no clock); spikes S9 to S11 as receipts, with check drift's warning for a check written with `IN`; WP 3.4, `io/LocalServer`'s sweep, first-connect retry and copy cache; spike S4's decision, that a proof's fresh copy is restored from the cached base.
- [ ] **Wave 7**, integrating: WP 2.1's second slice (the kinds `KeyLength`, `Rebuild`, `Computed`, `Temporal`, `Scan`, `Authored` and `SeedDrift`, matched on the plan's statements by a vocabulary that is data); WP 2.6 (the corpus, and the Facsimile's case catalog moved to `tests/Cases/` with its lint as a C# test); WP 3.5 with a declared witness kept from reaching a linked server; WP 7.1 with v2's operations folder added to the citation forms.
- [ ] **Wave 8**: WP 2.5 and M2's exits; WPs 3.6 and 3.7; WP 7.2's first slice; the review nits of waves 5 and 6.
- [ ] Then: M3's exits; M4; M5; M6; the rest of M7.

### The parity ledger

`ParityResolves` after wave 6: **held 25 · partly held 27 · not held yet 66 · retired 6 · the cutover tools 3** (127 rows). Most rows that are not held yet wait on WP 4.4's corpus, whose expected verdicts are v2's recorded ones.

## The operator's rulings

Each is one line in `DECISIONS.md`. Of 2026-10-19: M2 starts; M3's re-cut is confirmed; exit 8 is `needs-input`, added to `cli/exits.frozen` by the change that first raises one; a column's folded signatures may be read from an environment classified real (WP 3.7 adds one allowlist form and a privacy case); a check written with IN leaves check drift strict and is named with its remedy; the synthetic copy's name is shortened (§4 row 13); the width ceiling applies to code only; package ceilings rise by one rule while the per-file ceilings are kept; no Docker in v3, so the local server is LocalDB on Windows and SQL Server 2022 Express natively on Linux, CDC is never proven in this repository and its checks are a principal's on a full SQL Server; io starts no server; the local server's major version need only be at least the package's platform; the laptop check is reworded. Of 2026-10-20: io blocks DacFx's `xp_msver` call, so R14 keeps its no-`EXEC` rule; `CLAUDE.md` carries the rule that a workflow's arguments are saved before it launches.

## Budgets

Package ceilings by the rule of 2026-10-19 (the package's lines + twice what Appendix F still plans for M2 to M7), unchanged since: kernel 9,700, io 13,500, cli 3,400, code 26,600, tests 43,100, golden 3,000. Per file: 500 lines and 140 characters a line in kernel, io and cli; 800 lines in tests. `Budgets.Tests` enforces both. Appendix F's plan moved twice (2026-10-20): io's M2 row to 2,636 lines and the kernel's `Witness` row to 1,589, each with its decision line; neither raised a ceiling. `CLAUDE.md`'s manifest budget rose from 30 to 33 lines.

## Renamed tests

Five test names changed in wave 2, each because its subject was removed: two in `tests/Kernel.Tests/ProvenanceTests.cs` that named the image digest (now the server's edition), and the Docker items of `tests/Io.Tests/DoctorTests.cs`, replaced by the local server's port probe. A rename in a later wave of a test that `V3_PARITY.md`, `VALUES.md` or `LAWS.md` cites fails `ParityResolves` or `Laws`, and both are green.

## Read in this order

1. `V3_PARITY.md` §0 and §5; 2. `DECISIONS.md`'s lines from 2026-10-19; 3. `NEXT.md`; 4. `V3_MILESTONES.md` §3.1, §4, §8, §9 and §17.

## Not checked

- Nothing has run inside the environments' network (Dev, QA, UAT, the laptops); those exits stay with the people `NEXT.md` names.
- CDC is not proven anywhere in this repository, since no local server runs SQL Server Agent; law 8's CDC half is a principal's check.
- No harness runs v2's proof lane and v3 on one change; WP 4.4 takes v2's recorded verdicts as its expectations.
- The Facsimile's `verify` cases have not run.
- Test renames in waves 3 to 6 that no ledger cites are not listed.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Eu2Ney9xrEgmL5SpE1Q3io
