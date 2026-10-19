# Next — rewritten by the session that finishes something; never appended
Updated 2026-10-19 by #705, which brings v3's build (#704), the Facsimile (#706) and the vocabulary sweep (#707)
onto one branch, writes the Facsimile into the plan (`V3_MILESTONES.md` §3.1) and opens the parity ledger (`V3_PARITY.md`).

## The operator's actions
- Confirm or overturn the re-cut of M3 (`V3_MILESTONES.md` §9, §17 item 20): the existing-data tier planted by the
  Facsimile's planter (WP 3.8), the generator's port and the shape and scale tiers held until a trigger.
- Decide §17 items 23 (a code for the agent's next step; `8 needs-input` is recommended) and 24 (a column's
  value-free shape, read from an environment classified real). Send S5 to S8, and items 21 and 22, to the people below.
- At M0, install the SessionStart and SessionEnd hooks in `.claude/hooks/` as `CLAUDE.md` describes them; no agent may edit them.
- Answer round two of the M1 review (linked in #704's description); a card's option chosen there still takes effect.
- Move or delete the v2 worktrees under archive/v2/.claude/worktrees (3.6 GB), which Visual Studio and Copilot scan.
- Run the laptop check against Dev (M1 exit 1, spikes S1 and S3) with the tool folder built at
  `e0a4db94`, and report on the review page: the output, the build, `Script` and `LoadFromDatabase`
  times, `collation_name`, and whether `xp_instance_regread` is executable (`HAS_PERMS_BY_NAME`).

## In flight
- The pre-M2 pass, as #704 left it: the names and the register are merged; agents per adapter and for the kernel and
  the contract apply the audits' specifications; then the tests are refactored and every change is reviewed by the other model.

## Next, before M2
- The code the rulings of 2026-09-25 call for: the provenance record's per-claim constructors, `AggregateQuery.Of`
  internal with synonyms refused, a server host per environment, check drift reusing a built dacpac with one database
  read, and the Extended Events test extended to a drifted check drift and `read --from env:dev`.
- `V3_PARITY.md` §5 orders the work that needs no person: spikes S2, S4 and S9 to S11 in CI, WPs 3.1, 3.4 and
  3.5's first step, and WP 7.1. Each ledger row cites v2's proof; `ParityResolves` prints where they stand.
- `kernel/Environments.cs` LocalServerKind parses the unread key `localServer` as `docker` or `localdb`, and
  `tests/Golden/dbchange/environments.json` writes `docker`; WP 3.4 drops the key from both and from EnvironmentsTests and PublishProfilesTests.
- Then M2, from WP 2.1 and WP 2.4, once the operator allows it; WP 2.6 moves the Facsimile's case catalog to
  `tests/Cases/` (not held yet: WP 2.6). Before M5 and M6, correct the plan where it contradicts itself: WP 5.2's
  page cells, the vendoring verb's two names, R18's exit, who writes deployments.md, and the gate's wiki cells.

## Waiting on others
- S5, a lead who reads Dev's platform database: which `ossys_` tables and columns hold consumer references and publish times.
- S6, whoever manages the machines (laptops run LocalDB 16.x, the environments' network no Docker): open are the .NET 10 SDK
  on laptops and hosted agents, sudo there for `ci/sql.sh up`, the Octopus workers' .NET 10 runtime, and a package feed (§17 item 22).
- S7, whoever owns the deployment step: the publish profile, the DacFx version, and the Octopus login's language and DATEFORMAT.
- S8, the dev leads: Dev's, QA's and UAT's SQL Server version, compatibility level, RCSI and collation.
- The owners of the directory groups: a login holding only `VIEW DEFINITION` on Prod, and on QA and UAT for any
  unattended reader, asked for by that exact grant (§17 item 21).
