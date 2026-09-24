# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-24 by #704, after M1's code merged and CI passed on both operating systems at `d6c1eb92`.

## In flight
- The build is paused for the operator's review of M1. The review page is built by `ci/review/build.js`
  and holds the decisions, the findings, the laptop check and the ruling on M2. Its answers are read
  back with ArtifactData before any wave starts.
- `Contract.Milestone` stays 0 until the operator installs the settings and hooks and rules on how M1
  closes (decision 2.5 on the review page).

## Next, before M2
- A register pass over every document, refusal and finding message, CLI output line, test name and
  pull-request text, against the register in `AGENTS.md` as amended on 2026-09-24: plain technical
  language; circumstances and background before the point; top-down order; a literal phrase instead of
  a metaphor; no announcements of what follows; no reference or test that only restates itself. Extend
  `ci/register.json` and `Register.Prose` to catch what a test can catch.
- In the same pass, re-point the `VALUES.md` rows D3, X5, O2, O10 and L10: each cites the file
  `.github/workflows/estate.yml`, which passes `ValuesResolve` by existing; each needs the job or test
  that checks its value.
- A hardening wave for the findings the operator marks fix, under the rulings on the decisions.
- `VALUES.md` rows S2 (no silent downgrade) and O11 (large output truncated) are `pending M1` and
  unbuilt; decision 2.5 decides whether they are built now or re-pointed.
- Spikes with no CI job yet: spike S2, the guards both DacFx engines emit, under `tests/Golden/guards/`
  before WP 2.2; spike S4, a Twin restore timed in the container and in LocalDB, before WP 3.4.
- Then M2, starting with WP 2.1 and WP 2.4.

## Waiting on a person
- The operator, at M0: install `.claude/settings.json` and the SessionStart and SessionEnd hooks in
  `.claude/hooks/` (the permission check refuses an agent editing its own settings), and rule on the
  review page.
- S1 (the laptop half), S3 and M1 exit 1: one run of `estate check drift` against Dev from a team
  laptop, as the developers' group, reported on the review page's laptop form.
- S5, a lead with read access to Dev's platform database: which `ossys_` tables and columns hold
  consumer references and publish times.
- S6, IT: whether the team laptops and the hosted agents have the .NET 10 SDK and Docker.
- S7, release engineering: the publish profile and the DacFx version the Octopus step applies.
- S8, the dev leads: the SQL Server version, compatibility level and READ COMMITTED SNAPSHOT setting of
  Dev, QA and UAT.
