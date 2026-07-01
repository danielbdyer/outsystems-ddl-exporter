# ssdt-agent — the classify-by-proving skill tree

You are reading the entry surface for an **agent-facing skill tree**. Its job: help an
**OutSystems-native developer** make a **safe SSDT data-model change**. They think in
entities, attributes, and the Mandatory checkbox. You think in `CREATE TABLE` destinations
and what SSDT's publish engine actually does to real data. You translate, you prove, you
report — in their words.

## The thesis: proving is classifying

The same operation lands in a different SSDT **mechanism** depending on the **data**. "Make
this attribute mandatory" is a one-line `NOT NULL` edit — but whether it is a harmless
single-phase change or a multi-release backfill depends entirely on whether rows are empty
right now. **You cannot classify from the `.sql` text alone.** You must publish the change to
a copy of real-shaped data and watch what SSDT's publish engine does. The veto *is* the
classification.

The line the developer should experience:

> "You said make Email mandatory. I published that to a copy of your data — SSDT vetoed it
> because 1,240 rows are empty, so this is really a Pre-Deploy+Declarative change. Here's the
> backfill that makes it pass, proven."

That is the whole product. Everything below is in service of delivering that line, grounded in
a real generated delta and a real veto, every time.

## The two orthogonal axes (always emit BOTH)

A change is described by two independent coordinates. Do not collapse them.

- **MECHANISM — the "how"** (the team's "Five Mechanisms"):
  1. **Pure Declarative** — edit the CREATE, no script. (release bucket: *single-phase*)
  2. **Declarative + Post-Deploy** — CREATE + an idempotent post-deploy. (*single-PR*)
  3. **Pre-Deploy + Declarative** — backfill first, then the CREATE lands clean. (*single-PR*)
  4. **Script-Only** — the change is not expressible in the declarative model (CDC, FK
     reconcile, IDENTITY swap). (*single-PR operational, or multi-PR if staged*)
  5. **Multi-Phase** — spread across releases so old and new app code coexist. (*multi-PR*)

- **TIER 1–4 — the danger / who reviews:** Tier 1 self-serve … Tier 4 needs the gatekeeper.
  Apply **+1 tier** for any of: **CDC-enabled**, **>1M rows**, **first-time operation**.
  **Danger is not release-count.** Dropping a populated table is mechanically a one-shot
  single-PR drop, but it is Tier 4 because data is lost irreversibly.

## The four state-variables that flip the bucket

A mechanism is provisional until you know these four facts, and three of the four can only be
learned by proving against the data:

1. **Is the table populated?**
2. **Does the existing data violate the new rule?** (orphans / NULLs / dupes / over-length)
3. **Is CDC enabled and is a no-gap capture required?**
4. **Must old + new application code coexist** during the change?

Each one crossing its threshold bumps the operation up a bucket. The proving ground exists to
answer #1, #2, and (partly) #3 with evidence rather than a guess.

## The tree map

```
ssdt-agent/
├── README.md ················ you are here — the model, the axes, the read order
├── CONNECTORS.md ··········· future wiring seams (.claude/skills, Copilot, F# engine, ADO)
├── agents/
│   ├── intake.md ··········· Persona-1 front door: confirm intent, name the op, gather the 4 vars
│   ├── change-author.md ···· the workhorse: edit the CREATE, classify, PROVE, deliver the verdict
│   └── reviewer.md ········· Persona-2 STUB (deferred) — the review-packet contract only
├── skills/
│   ├── confirm-intent/ ····· OutSystems phrasing → catalog operation + the implicit destination
│   ├── classify-mechanism/ · the decision cascade → a PROVISIONAL Mechanism + Tier
│   ├── prove-on-dacpac/ ····· the proving loop that CONFIRMS or FLIPS the classification
│   ├── talk-to-local-sql/ ··· the throwaway-DB substrate + the data-hash oracle
│   └── operations/ ········· the ~50-operation catalog, grouped by family
│       ├── tables.md  columns.md  keys-and-refs.md  indexes.md
│       └── constraints.md  static-data.md  structural.md  views-synonyms.md  audit-cdc.md
├── proving-ground/ ········· the hand-authored, self-contained sample project (the substrate)
│   ├── SampleCatalog.sqlproj   Modules/*.sql   Script.Pre/PostDeployment.sql   Data/Seed.sql
│   ├── profiles/ ··········· Strict (veto detector) + Permissive (consequence oracle)
│   └── README.md ··········· the end-to-end run procedure (the runbook for the loop)
└── self-test/
    ├── prompts.md ·········· human-shaped dev prompts, each tagged with expected Mechanism+Tier
    └── rubric.md ··········· how to score a run (the same-op × different-seed gate)
```

## Read order

1. **`agents/`** — pick your role. Persona 1 (the developer's request) enters at `intake.md`,
   which hands a structured change-order to `change-author.md`. `reviewer.md` is a deferred
   stub.
2. **`skills/`** — the four capability skills (`confirm-intent` → `classify-mechanism` →
   `prove-on-dacpac`, riding on `talk-to-local-sql`), then the per-operation knowledge in
   `skills/operations/`.
3. **`proving-ground/README.md`** — the runbook. Read it before you run a single command.

## Scope

**Persona 1 only.** This tree helps the OutSystems-native developer *author* a safe change
and proves the classification for them. The reviewer / gatekeeper (Persona 2) is **deferred**
— `agents/reviewer.md` ships shaped, not built, with only the handoff contract. The F#
Projection engine that generated this whole codebase is an **optional accelerant** (it can
emit the `.sqlproj`/dacpac from a real catalog instead of our hand-authored sample); the
seams are catalogued in `CONNECTORS.md` and deliberately not wired.

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
