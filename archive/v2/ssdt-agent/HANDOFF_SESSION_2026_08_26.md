# Handoff — the ssdt-agent evaluation session

**For whoever picks this work up next, in any tool or session.** This file carries the context a
successor needs: what the work is, what this session changed, what is true about the estate, how to
work in this repository without breaking its disciplines, and what remains to be done.

---

## 1. Where the work lives

- **Repository:** `danielbdyer/outsystems-ddl-exporter`
- **Branch:** `claude/ssdt-agent-evaluation-gdg7m7`
- **Pull request:** #698, open, ten commits ahead of the merge-base
- **The subject of the work:** `sidecar/projection/ssdt-agent/` — the classify-by-proving skill tree
- **Working tree:** clean, everything committed and pushed

Start by reading, in this order: `sidecar/projection/ssdt-agent/README.md` (what the tree is),
`ASSESSMENT_2026_08_24.md` in the same directory (an outside evaluation of whether it is fit for
purpose), and `THE_RECORD.md` (the register everything in the tree is written in).

---

## 2. The situation — what is true about the estate

These facts came from the owner during the session and override anything the older documents in the
repository say. They drive most of the work.

- **The team.** A mixed-experience OutSystems team is taking ownership of its database schema in
  SSDT for the first time. Four named people review pull requests, and no one else does: three
  senior developers who know SQL and OutSystems well but have never used SSDT, and one principal
  who is fluent in SSDT and who is out of office during the initial cutover window. Principal-level
  reviews are deputized while they are away — two senior reviewers together, a proven backup, and
  the principal confirming on return. That rule is recorded in
  `sidecar/projection/ssdt-agent/estate/reviewers.md`.
- **The cutover is staged, and Dev goes last.** QA and UAT are already SSDT-managed, each set up by
  its own cutover publish. Dev's trunk switches over next weekend, and only then do changes promote
  through the pipeline, Dev to QA to UAT. Production has not been released to yet.
- **The first promotion into QA or UAT needs an extra check.** Those two environments were baselined
  outside this release train, so their starting schema may differ from what the trunk model expects.
  Script the full delta first and confirm it contains only the intended change; anything else is
  drift left over from the cutover.
- **The pipeline is Azure DevOps into Octopus, and its data-loss guard is always on.** No change can
  relax `BlockOnPossibleDataLoss` for a single deploy. That constraint forces every tightening or
  destructive change on populated data into a two-release shape, and it is the axiom the whole
  authoring machine is built around.
- **The team develops in GitHub Copilot inside Visual Studio**, on Windows, against the Azure DevOps
  repository. They do not use Claude Code. This matters more than anything else in the assessment.
- **The environments are named Dev, QA, UAT, and Prod.** There is no environment called "Test",
  though older documents used that name before this session corrected them.

---

## 3. What the tree is, in three sentences

The ssdt-agent tree is a working tool rather than documentation about a tool: a set of instructions
that a frontier model loads to help a developer change a database schema safely. It holds three
agent roles (intake, change-author, reviewer), a catalog of 41 schema operations with a shared
knowledge layer beneath them, and a small SQL Server project seeded with deliberately awkward data
that the agent publishes changes against. Its governing idea is that you cannot tell how a schema
change will behave by reading its SQL — the same edit ships one way against an empty table and
another way against a populated one — so the agent publishes the change to a disposable copy and
reads what the deployment engine actually did, and that result is the classification.

---

## 4. What this session changed — the ten commits

Grouped by what they accomplish, oldest first.

1. **`c6d7d8e` — sqlpackage provisioning and a toolchain pin ledger.** The proving loop's publish
   tool was not installed in the session container. The web SessionStart hook now installs it,
   verifies it, and exports the runtime shim; its version pin lives in a new ledger at
   `sidecar/projection/ssdt-agent/estate/toolchain.md`, which also holds the slot for the estate
   pipeline's DacFx version. A placeholder publish profile,
   `proving-ground/profiles/ProvingGround.Pipeline.publish.xml`, waits for the pipeline's real
   flags.
2. **`cd13e73` — recalibration to the real estate.** The staged cutover, the four-reviewer pool and
   the stand-in rule, the first-promotion drift check, and the renaming of the "Test" environment to
   QA across the tree and the playbook. This commit also fixed a self-contradiction in the review
   test suite and retired an obsolete pull-request template that lingered in three places.
3. **`a8be350` — the register standard, armed.** See section 6 below; this is the most important
   commit to understand before writing anything.
4. **`769b0a0` and `9464371` — the assessment.** An outside evaluation of the tree, written into
   `ASSESSMENT_2026_08_24.md`, then revised after two independent agent reviews reported.
5. **`b105ab6` — the GitHub Copilot packaging and the Windows proving-path runbook.** A second
   emission target that generates a self-contained `.github/` bundle for the team's Azure DevOps
   repository, plus `PROVING_PATH_WINDOWS.md`, the runbook for standing up a local SQL Server and
   sqlpackage on a Windows machine.
6. **`32987fe` — three small defects fixed.** The golden exemplar was re-proved live and re-captured
   in the current ten-section form rather than hand-edited; a ledger contradiction about
   `retype-explicit` was reconciled; and an operation that claimed no added scrutiny while the estate
   ledger implied otherwise now defers to the ledger.
7. **`f5c3629` and `d426743` — compound-change decomposition.** A new `decompose` skill that breaks
   one compound request into the fewest well-separated pull requests, then its hardening after an
   independent agent stress-tested the method and found real flaws.
8. **`fcf01e8` — the Phase 2 curriculum plan.** `PHASE_2_CURRICULUM.md`, the plan for moving the team
   from using the tool to not needing it, and for the manager leaving the review rotation.

---

## 5. The assessment's verdict, which a successor should not re-litigate

The tree's method is proven and its authoring judgment is strong. Delivering both to the team's
actual environment is not done, and that is prerequisite work rather than a detail.

The session reproduced the tree's central claim first-hand against a live SQL Server: making a
populated column required is refused by the deployment engine, and it is **still** refused after
every blank value is cleared, because the guard fires on whether the table holds rows at all. That
held on the real engine, with the same content digest the earlier run recorded.

Two independent agents then reviewed the tree without having made this session's edits, and both
converged on the finding that first-hand testing could not see: **the tree runs where it was tested,
not where the team works.** Its proving substrate assumes Docker and this monorepo, its packaging
assumed Claude Code, and it proves changes against a small sample database rather than the team's
real tables. A team that cannot prove falls back on the classifications the skills state in prose,
which is the guessing the tree itself calls unsafe.

Two correctness risks of the worst kind — a green deploy that did not do what was asked — are named
in the assessment and remain open:

- **Constraint trust is stated as settled but unverified on the team's engine.** Whether a new
  foreign key, check, or unique constraint re-validates the existing rows depends on the engine
  version. If the team's version behaves like the older one, a violating row would not block the
  deploy and the safety the skills claim would not hold.
- **A two-release tightening reverts if a second developer publishes anything to Dev during the
  window.** While the model deliberately lags, an unrelated publish carries the old shape and undoes
  the tightening, with every check still green.

---

## 6. The register — read this before writing a single line

The owner stopped the session mid-flight to correct the prose I was writing, and the correction is
now part of the tree's standard. It matters as much as any technical fact here.

**What was wrong.** My writing was compressed in a way that reads as precise and is actually hard to
follow. Four habits in particular: pairing a claim with its negated opposite ("a lookup, never a
recollection"), putting abstractions in the subject slot where a person or a named object belongs,
packing three or four ideas into one sentence, and folding a whole explanation into a two-word noun
the reader then has to unfold.

**What is wanted instead — Standard Technical English.** Every sentence gets a real subject that can
act. Spell the referent out rather than burying it in a label. Be expansive: assume the reader holds
about thirty percent less context than you think. Prefer two plain sentences to one packed one.

**Where the standard now lives.** `THE_RECORD.md` in the tree carries it: rule 6 asks for a real
subject in every sentence and names the personification to avoid; a principle after the nine rules
sets out "terse in manner, complete in reference"; the banned list names the `X, not Y` shape with a
test for it (delete the negated half — if nothing is lost, it was decoration); section 8 carries two
worked-wrong examples taken from my own first draft; and section 9 corrects the reader model to the
real review pool, so the standard is to write for a reviewer who knows SQL well and is new to SSDT.

A continuous-integration check lints the record surfaces for the banned constructions, but the lint
lags any new clever phrase. The standard is held by reading, not by the lint.

---

## 7. How to work in this repository

**Run the gates before every commit.** They check citations, the register, the pull-request template
mirror, both packaging targets, and the estate ledgers:

```
node sidecar/projection/scripts/ssdt-agent-gates.mjs all
```

**Regenerate both packages after editing any skill or agent frontmatter.** Editing a skill body needs
no regeneration; editing frontmatter or adding a skill does:

```
node sidecar/projection/scripts/ssdt-agent-package.mjs apply          # the .claude package
node sidecar/projection/scripts/ssdt-agent-package.mjs copilot-apply  # the Copilot bundle
```

**The proving loop, in this container.** A warm SQL Server runs in Docker as
`projection-mssql-warm`, started by the SessionStart hook. The host has no `sqlcmd`, so every query
goes through the container:

```
docker exec -i projection-mssql-warm /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Projection@Strong1' -C -d <database> -Q "<sql>"
```

Before running `sqlpackage`, export the shim: `DOTNET_ROOT=/root/.dotnet`,
`DOTNET_ROLL_FORWARD=Major`, and `$HOME/.dotnet/tools` on the path. Copy the proving ground to a
scratch directory and edit only the copy — never the tree's own
`proving-ground/Modules/*.sql`. Read a publish's verdict from its printed text rather than its exit
code: a block prints `Could not deploy package.` with a `Msg` line.

**Two disciplines that are easy to break.** Drop every database you create and remove your scratch
directory when you finish, because leaked databases degrade the shared container. And if a publish
hangs, the container has degraded rather than the code having regressed —
`bash sidecar/projection/scripts/warm-sql.sh restart` fixes it in about four seconds.

**Captured evidence is re-proved, never hand-edited.** The golden exemplar and the sample pull
requests carry real engine output. If one is wrong or stale, run the case again and capture the new
result; editing the numbers by hand destroys the only thing that makes them worth having.

---

## 8. What remains, in priority order

**The task the owner asked for next, which is not started.** A written system design document for
the ideal form factor of the local development experience — the proving surface itself. The question
is what stack and shape it should take (a Docker image that persists the latest synthetic schema and
data was the example given), derived from the invariant cases that stimulate its requirements. The
owner specifically noted that **a great deal of existing enablement lives in the F# codebase** and
asked for significant research and evaluation before writing. Relevant existing material includes
`sidecar/projection/THE_TWIN.md` (the post-eject synthetic-data sidecar and its charter),
`sidecar/projection/src/Twin.Core`, `Twin.Runtime`, and `Twin.Cli`, the synthetic-data design
documents in `sidecar/projection/`, `sidecar/projection/scripts/warm-sql.sh`, the proving ground
under the ssdt-agent tree, and `PROVING_PATH_WINDOWS.md`. This work should be researched thoroughly
before a line of the design is written.

**The prerequisites the assessment named, which belong to the owner's corporate machine.** Stand up
the Windows proving path against a restored copy of real Dev data; pin the pipeline's DacFx version
in the toolchain ledger and confirm the constraint-trust behavior once against a real publish; port
the Copilot packaging onto a real Visual Studio build and confirm which features that build actually
loads.

**Smaller open work in the tree.** Add an explicit instruction to the two-release operations to hold
other Dev publishes during the window. Fill the estate ledgers with real row counts at the Dev
cutover. Expand the catalog to cover the operations it currently lacks — dropping a constraint,
views, computed columns, collation changes — because a request for one of those currently finds no
skill and the agent improvises on a live database.

**Phase 2, when the cutover has stabilized.** `PHASE_2_CURRICULUM.md` holds the plan.

---

## 9. Cautions for a successor

- **The tree is unusually honest about itself, and that honesty is easy to mistake for a defect
  list.** Its findings ledger overturns its own past conclusions in public, with dates. Read a
  struck finding as evidence the method works rather than as rot.
- **Independent review earns its keep here.** Every substantive thing this session built was checked
  by an agent that had not built it, and each check found something real. Editing the tree makes you
  a poor judge of it within the hour.
- **The locked data-loss gate and a constraint's value-block are different mechanisms.** Confusing
  them produces wrong release counts. The knowledge layer under `skills/_index/` keeps them apart
  deliberately, and a skill should read an operation's release count from that operation rather than
  asserting one.
- **The estate ledgers are honest empty scaffolding.** They hold formats and no production history,
  because the estate has not cut over. Do not read an empty ledger as a broken one.
