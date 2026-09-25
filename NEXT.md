# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-25 by #704, after the M1 hardening merged at `e0a4db94`.

## The operator's actions
- Send S5 to S8 to the people below.
- At M0, install `.claude/settings.json` and the SessionStart and SessionEnd hooks in `.claude/hooks/`
  (the permission check refuses an agent editing its own settings); the review page has the commands.
- Answer round two of the M1 review (its link is in #704's description): the refactoring and naming
  proposals, decisions 2.5, 2.8, 2.9 and 2.23 to 2.27, the findings, and whether M2 starts.
- Move or delete the five v2 worktrees under archive/v2/.claude/worktrees (3.6 GB): git ignores
  them, but Visual Studio, Copilot and repository searches still scan them.
- Run the laptop check against Dev (M1 exit 1, spikes S1 and S3) with the tool folder built at
  `e0a4db94`, and report on the review page: the output, the build, `Script` and `LoadFromDatabase`
  times, `collation_name`, and whether `xp_instance_regread` is executable (`HAS_PERMS_BY_NAME`).

## In flight
- Nothing runs until round two is answered.

## Next, before M2
- One pass that applies round two's rulings: the chosen names, the approved primitives (R1 to R9),
  the register in `AGENTS.md` across every document and code string (with the VALUES rows D3, X5,
  O2, O10 and L10, which cite a file where they need the check that holds the value), and the
  findings the operator marks fix.
- The code the rulings of 2026-09-25 call for: the receipt's per-claim constructors, `Probe.Of`
  internal with synonyms refused, a server host per environment, check drift reusing a built dacpac
  with one database read, "dropped" in diff, and the Extended Events test extended to a drifted
  check drift and `read --from env:dev`.
- Spike S2 (the guards both DacFx releases emit, under `tests/Golden/guards/`) before WP 2.2; spike
  S4 (a Twin restore timed in the container and in LocalDB) before WP 3.4. Then M2, from WP 2.1
  and WP 2.4, once the operator allows it.
- Before M5 and M6, correct the plan where it contradicts itself: WP 5.2's page cells, the
  vendoring verb's two names, R18's exit, who writes deployments.md, and the gate's wiki cells.

## Waiting on others
- S5, a lead with read access to Dev's platform database: which `ossys_` tables and columns hold
  consumer references and publish times.
- S6, IT: whether the team laptops and the hosted agents have the .NET 10 SDK and Docker, and
  whether the Octopus workers have the .NET 10 runtime.
- S7, release engineering: the publish profile and the DacFx version the Octopus step applies.
- S8, the dev leads: Dev's, QA's and UAT's SQL Server version, compatibility level, RCSI and collation.
