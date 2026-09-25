# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-25 by #704, during the pre-M2 pass the operator asked for.

## The operator's actions
- Send S5 to S8 to the people below.
- At M0, install `.claude/settings.json` and the SessionStart and SessionEnd hooks in `.claude/hooks/`
  (the permission check refuses an agent editing its own settings); the review page has the commands.
- Answer round two of the M1 review (its link is in #704's description). The pass applies each card's
  recommended option now; a different option chosen there still takes effect.
- Move or delete the five v2 worktrees under archive/v2/.claude/worktrees (3.6 GB): git ignores
  them, but Visual Studio, Copilot and repository searches still scan them.
- Run the laptop check against Dev (M1 exit 1, spikes S1 and S3) with the tool folder built at
  `e0a4db94`, and report on the review page: the output, the build, `Script` and `LoadFromDatabase`
  times, `collation_name`, and whether `xp_instance_regread` is executable (`HAS_PERMS_BY_NAME`).

## In flight
- The pre-M2 pass: the names and the register are merged; one agent per adapter (SQL Server, DacFx,
  git and files and programs) and one for the kernel and the contract apply the audits' specifications;
  then the tests are refactored and every change is reviewed by the other model.

## Next, before M2
- The code the rulings of 2026-09-25 call for: the provenance record's per-claim constructors,
  `AggregateQuery.Of` internal with synonyms refused, a server host per environment, check drift
  reusing a built dacpac with one database read, and the Extended Events test extended to a
  drifted check drift and `read --from env:dev`.
- Spike S2 (the data-loss checks both DacFx releases emit, under `tests/Golden/`) before WP 2.2;
  spike S4 (a synthetic copy's restore timed in the container and in LocalDB) before WP 3.4.
- Then M2, from WP 2.1 and WP 2.4, once the operator allows it.
- Before M5 and M6, correct the plan where it contradicts itself: WP 5.2's page cells, the
  vendoring verb's two names, R18's exit, who writes deployments.md, and the gate's wiki cells.

## Waiting on others
- S5, a lead with read access to Dev's platform database: which `ossys_` tables and columns hold
  consumer references and publish times.
- S6, IT: whether the team laptops and the hosted agents have the .NET 10 SDK and Docker, and
  whether the Octopus workers have the .NET 10 runtime.
- S7, release engineering: the publish profile, the DacFx version, and the login's language and
  DATEFORMAT that the Octopus step applies.
- S8, the dev leads: Dev's, QA's and UAT's SQL Server version, compatibility level, RCSI and collation.
