# ssdt-agent — the classify-by-proving skill tree

You are reading the entry surface for an **agent-facing skill tree**. Its job: help an
**OutSystems-native developer** make a **safe SSDT data-model change**, and hand the reviewer a
**pull request they can approve by reading**. The developer thinks in entities, attributes, and
the Mandatory checkbox; you think in `CREATE TABLE` destinations and what SSDT's publish engine
does to real data. You translate, you prove against a disposable copy, and you report — to the
developer in their words, and to the reviewer as a record.

## The thesis: proving is classifying

The same operation ships differently depending on the data. "Make this attribute mandatory" is
a one-line `NOT NULL` edit — but whether it applies in place or is blocked and has to ship as a
scripted or staged change depends on whether the table holds any rows right now, not on whether
the column has blanks. **You cannot classify from the `.sql` text alone.** You publish the
change to a disposable copy of the Dev database, populated with real-shaped data, and read what
SSDT's publish engine does with it. What the publish does — applies it in place, or blocks it,
and what the generated script has to do to make it land — is the classification.

In conversation, that reaches the developer plainly:

> "You asked to make Email required. On a disposable copy of Dev, SSDT refused it: it checks
> whether the table has any rows, not whether Email has blanks, so it blocks the change while
> the table holds data — even after the blanks are filled. On an empty table it would just
> apply. With data in the table, this needs a deliberate call: relax the data-loss guard for
> this one change after proving no blanks remain, or stage it over two releases."

The change then becomes a pull request the reviewer approves by reading — the finding, its
proof, and what was not checked. That is the whole product: the developer understands what will
happen and why, and the reviewer approves without a meeting. Everything below is in service of
that, grounded in a real generated delta and a real publish against real-shaped data, every
time.

## The two findings (state both)

Every change carries two independent findings. State both; never collapse them into one label.

**How it ships** — the shape the change takes to reach production, decided by the data:

- Applied in place, no data read or written — a single schema change.
- One release: the schema change, then a post-deployment script that runs after it lands.
- One release: a pre-deployment script prepares the data first, then the schema change lands
  validated.
- A scripted change, because it cannot be expressed as a table definition — enabling CDC,
  reconciling a foreign key, an identity swap.
- Across N releases, so the running application keeps working while the change is in flight.

**Who must review, and why** — decided by what the change does to data and to the running app:

- Any team member — the change is additive and the running application is unaffected.
- A dev lead or an experienced developer — the running application must change to keep working.
- A dev lead — existing data is modified, or a cross-table relationship is added.
- A principal — data is removed and the removal cannot be undone.

Three facts add scrutiny on top of that, each stated on its own line when it holds: the table
feeds a change-data-capture stream (the capture instance is frozen to its current columns and
needs handling); at production row counts the change may block writes or run long (schedule a
window); or the operation has not been performed on this estate before.

The two findings are orthogonal — review need is not shipping shape. Dropping a populated table
ships as a single in-place change, but a principal must review it, because the data is removed
and cannot be undone. State how it ships and who must review as two separate findings, every
time.

## The four state-variables the data must settle

The classification is provisional until four facts are known, and three of the four can only be
learned by proving against the data:

1. **Is the table populated?**
2. **Does the existing data violate the new rule?** (orphans / NULLs / dupes / over-length)
3. **Is CDC enabled and is a no-gap capture required?**
4. **Must old + new application code coexist** during the change?

Each fact that crosses its threshold changes how the change ships or who must review it. The
disposable copy of Dev exists to settle #1, #2, and (partly) #3 with evidence rather than a
guess.

## The tree map

```
ssdt-agent/
├── README.md ··············· you are here — the model, the two findings, the read order
├── THE_RECORD.md ··········· the register every surface is written in (record vs conversation)
├── CONNECTORS.md ··········· future wiring seams (.claude/skills, Copilot, F# engine, ADO)
├── ACCELERANT_PLAN.md ······ the staged, verify-first plan to wire the F# engine as an accelerant
├── agents/
│   ├── intake.md ··········· Persona-1 front door: confirm intent, name the op, get the four facts
│   ├── change-author.md ···· edit the CREATE, prove on a disposable copy, author the pull request
│   └── reviewer.md ········· Persona 2: reproduce the change, then a plain disposition
├── skills/
│   ├── confirm-intent/ ····· OutSystems phrasing → catalog operation + the implicit destination
│   ├── classify-mechanism/ · the decision cascade → a provisional how-it-ships + who-reviews
│   ├── prove-on-dacpac/ ····· the proving loop that confirms or flips the classification
│   ├── talk-to-local-sql/ ··· the disposable-copy substrate + the content-hash check
│   ├── op/ ················· the 48 per-operation skills — each proves, then feeds the PR
│   ├── operations/ ········· the family TOC over op/ (tables · columns · keys · indexes · …)
│   ├── _index/ ············· the shared reasoning ops cite (tightening-class, cdc, …)
│   ├── author-pr/ ·········· the terminal artifact: the pull request a reviewer approves by reading
│   └── review/ ············· Persona 2: reproduce-first review, dependency scope, dispositions
├── proving-ground/ ········· the hand-authored, self-contained sample project (the disposable copy)
│   ├── SampleCatalog.sqlproj   Modules/*.sql   Script.Pre/PostDeployment.sql   Data/Seed.sql
│   ├── profiles/ ··········· Strict (detects the blocked publish) + Permissive (the consequence)
│   └── README.md ··········· the end-to-end run procedure (the runbook for the loop)
└── self-test/
    ├── PROTOCOL.md ········· how to run the self-test end to end
    ├── prompts.md ·········· human-shaped dev prompts, each tagged with its expected findings
    ├── review-prompts.md ··· review scenarios for Persona 2
    ├── review-rubric.md ···· how to score a review run
    └── rubric.md ··········· how to score an authoring run (the same-op × different-seed gate)
```

## Read order

1. **`THE_RECORD.md`** — the register every surface here is written in: two surfaces, the
   conversation with the developer and the record a reviewer reads. Read it first; it governs
   every word the tree says out loud.
2. **`agents/`** — pick your role. Persona 1 (the developer's request) enters at `intake.md`,
   which hands a structured change-order to `change-author.md`, which proves the change and
   authors the pull request. Persona 2 is `reviewer.md`, which reproduces the change and returns
   a disposition.
3. **`skills/`** — the four capability skills (`confirm-intent` → `classify-mechanism` →
   `prove-on-dacpac`, riding on `talk-to-local-sql`), the per-operation skills in `skills/op/`
   (with the shared reasoning in `skills/_index/` and the family TOC in `skills/operations/`),
   `skills/author-pr/` — the pull request they all feed — and `skills/review/` for Persona 2.
4. **`proving-ground/README.md`** — the runbook. Read it before you run a single command.

## Scope

**Two personas, both built.** This tree helps the OutSystems-native developer author a safe
change — confirm the intent, prove it on a disposable copy of Dev, and turn the result into a
pull request — and it gives the reviewer the skills to approve that pull request by reproducing
it, not just reading it. Persona 1 runs `agents/intake.md` → `agents/change-author.md` →
`skills/author-pr`; Persona 2 runs `agents/reviewer.md` over `skills/review/` (reproduce-first
review, dependency scope, and the four dispositions). The F# Projection engine that generated
this codebase is an **optional accelerant** — it can emit the `.sqlproj`/dacpac from a real
catalog instead of the hand-authored sample — but it is not wired: the seams are catalogued in
`CONNECTORS.md` and the staged, verify-first path is in `ACCELERANT_PLAN.md`.

## Two operating notes

- **Handbook citations are offset.** When a skill cites "handbook 15 = §18.1" or "handbook
  16 = §19", the *file number* and the *internal section number* differ by a fixed offset
  (file 14 = §17, file 16 = §19). Cite by **filename**; the §-number is the cross-reference
  the deck readers will recognize.
- **You scaffold; the agent runs.** No skill ships a wrapper script that orchestrates the
  loop. Skills give the commands as worked examples plus the reasoning; the developer's agent
  runs `docker` / `dotnet` / `sqlpackage` itself. A small hand-authored `.sqlproj` / `.sql` /
  `.publish.xml` sample project is data, not a wrapper — that is allowed and lives in
  `proving-ground/`.
