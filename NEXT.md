# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-25 by #704, after the operator's review of M1.

## The operator's actions
- At M0, install `.claude/settings.json` and the SessionStart and SessionEnd hooks in `.claude/hooks/`
  (the permission check refuses an agent editing its own settings).
- Run the laptop check against Dev (M1 exit 1, spikes S1 and S3) and report it on the review page's
  form: the output, the build, `Script` and `LoadFromDatabase` times, and whether
  `HAS_PERMS_BY_NAME('master.dbo.xp_instance_regread','OBJECT','EXECUTE')` returns 1.
- Answer the second review round once it is published: decisions 2.5, 2.8 and 2.9 with more
  background, the proposed names, the refactoring recommendations, the findings, and whether M2 starts.
- Send S5 to S8 to the people below.

## In flight
- A read-only investigation: the code's duplication and missing primitives, what M2 to M7 will need
  from them, a naming audit, and more background for decision 2.9.
- A hardening wave for the correctness and security defects the review found in the database read,
  the plan, the CLI's exit mapping, the publish profile's bytes and `file:` references.
- An explainer of the kernel and the contract for the operator, kept out of the repository.

## Next, before M2
- After the second round: one pass that applies the chosen names, the primitives and the register in
  `AGENTS.md` (amended 2026-09-24) to every document and code string, including the VALUES rows D3,
  X5, O2, O10 and L10, which cite a file where they need the check that holds the value.
- The code the rulings of 2026-09-25 call for: the receipt's per-claim constructors, `Probe.Of`
  internal with synonyms refused, a server host per environment, check drift reusing a built dacpac
  with one database read, "dropped" in diff, and the Extended Events test extended to a drifted
  check drift and `read --from env:dev`.
- VALUES rows S2 and O11 read `pending M1` and are unbuilt; the second round decides how M1 closes.
- Spike S2 (the guards both DacFx releases emit, under `tests/Golden/guards/`) before WP 2.2; spike
  S4 (a Twin restore timed in the container and in LocalDB) before WP 3.4.
- Then M2, from WP 2.1 and WP 2.4, once the operator allows it.

## Waiting on others
- S5, a lead with read access to Dev's platform database: which `ossys_` tables and columns hold
  consumer references and publish times.
- S6, IT: whether the team laptops and the hosted agents have the .NET 10 SDK and Docker.
- S7, release engineering: the publish profile and the DacFx version the Octopus step applies.
- S8, the dev leads: the SQL Server version, compatibility level and `is_read_committed_snapshot_on`
  of Dev, QA and UAT.
