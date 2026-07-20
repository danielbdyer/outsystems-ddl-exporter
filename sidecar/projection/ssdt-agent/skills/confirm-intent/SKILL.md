---
name: confirm-intent
description: Use FIRST, before any classification or proving, whenever an OutSystems-native developer asks for a schema change in their own words ("make Email required", "rename this attribute", "add a foreign key", "drop that table"). Disambiguates the request into exactly one op-slug (a skills/op/<op-slug>/ directory), surfaces the implicit SSDT destination the developer didn't say out loud, gathers the four data-state variables that decide how the change ships and who must review it, and pre-flags the governing skills/_index/ concern. Hands a structured change-order naming the op-slug + its per-op skill downstream. Does NOT edit SQL or classify.
---

# Confirm intent

> **Why this (and what it teaches).** Intake comes first because **the wrong operation proven
> perfectly is still wrong** — intent is the one thing the data can never correct for you. The
> disposable copy of Dev can tell you whether a column has NULLs; it cannot tell you that the
> developer meant *widen* when they said "change the email field." What this teaches: every schema
> change begins by restating the request in the developer's own words *and* in the table's terms,
> so a misunderstanding surfaces before any data is touched — the discriminator is "can I say this
> back in both vocabularies without guessing?" Surface this reasoning to the developer: a developer
> who learns that intake is disambiguation, not paperwork, starts stating the *destination* they
> want rather than the *journey* they imagine — the move from describing migrations to describing
> destinations.

You are helping an **OutSystems-native developer** make a safe schema change. They think in
**entities, attributes, references, and static entities** — the Service Studio vocabulary. You
think in **CREATE-table destinations** and SSDT publish mechanics. Your first job is not to act;
it is to make sure you and the developer mean the same thing, and to learn the one thing the
`.sql` text can never tell you: **what the data looks like.** This file owns the
request-to-operation dispatch; the conversational vocabulary around it — nouns, gestures, the
anchored SSDT explanations, the listening notes — is owned by `../os-vocabulary/SKILL.md`.

Nothing downstream is trustworthy if intake is wrong. A misnamed operation classifies the wrong
way; a missing data-state variable hides a refusal you'll only meet at deploy. Slow down here.

## What you produce

A **change-order** — a small structured handoff for `classify-mechanism`:

- **operation**: exactly one **op-slug** matching a `skills/op/<op-slug>/` directory, e.g.
  `make-mandatory`. This is the precise entry the change-author will open — hand it downstream.
- **opSkill**: the path `skills/op/<op-slug>/SKILL.md` (the op-slug's per-op skill).
- **outSystemsPhrasing**: the developer's own words, verbatim — you'll echo these back later.
- **target**: the table + column/constraint the destination edit lands on.
- **destination**: the *implicit* SSDT meaning (what the CREATE will say after the edit).
- **stateVariables**: the four state-variables, each `known`/`unknown` (unknown ones get proven, not guessed).
- **sharedConcern**: the governing `skills/_index/<concern>/SKILL.md` you pre-flagged (a rename →
  `identity-and-refactorlog`; a tightening → `tightening-class`; anything on a CDC table → `cdc`;
  a constraint → `constraint-is-a-claim`; a coexistence restructure → `multi-phase`; a seed →
  `idempotent-seed`). Optional but valued — it lets the change-author open the right WHY first.
- **notes**: anything ambiguous you resolved, and how.

You do **not** edit any `.sql`, and you do **not** decide how the change ships or who must review
it — that is `classify-mechanism`'s job.

## Step 1 — name exactly one operation

The developer speaks intent; you name **exactly one op-slug** — the directory name of a
`skills/op/<op-slug>/SKILL.md`. Use the translation table below, then open the matching per-op
skill to confirm: **its frontmatter `description` is written in your developer's own words**, so
if their phrasing matches a description, that is your op. If the request maps to **more than one**
op ("split this entity", "merge these", "extract this to a lookup" are multi-move), name them all
and tell the developer you'll sequence them via the structural/static per-op skills — do not
silently pick one.

Two disambiguations the table now forces you to make (the op split lives here, not downstream):

- **create-FK splits on the orphan question** → `create-fk-clean` vs `create-fk-orphan`. If you
  cannot yet tell whether children without a parent exist, mark state-variable 2 `unknown` and
  hand `create-fk-orphan` if the developer *hints* at old/dirty data, else `create-fk-clean`; the
  prove step settles it.
- **retype splits on direction** → `retype-implicit` (widening, lossless: INT→BIGINT,
  VARCHAR→NVARCHAR) vs `retype-explicit` (value-reshaping, lossy: text→date, text→int).
- **default splits on presence** → `add-default` (new) vs `modify-default` (DROP-then-ADD).

### OutSystems -> SSDT translation table

| The developer says (OutSystems) | op-slug | the op skill | The implicit SSDT destination |
|---|---|---|---|
| "add an attribute" / "new optional field" | `add-optional` | `skills/op/add-optional/SKILL.md` | new **nullable** column on the CREATE |
| "add a required attribute" | `add-mandatory` | `skills/op/add-mandatory/SKILL.md` | new `NOT NULL` column **+ DEFAULT** (or SSDT refuses it) |
| "tick Mandatory" / "make it required" | `make-mandatory` | `skills/op/make-mandatory/SKILL.md` | existing column `NULL -> NOT NULL` |
| "untick Mandatory" / "make it optional" | `make-optional` | `skills/op/make-optional/SKILL.md` | existing column `NOT NULL -> NULL` |
| "make the field bigger" / "allow longer text" | `widen` | `skills/op/widen/SKILL.md` | `NVARCHAR(n) -> NVARCHAR(m)`, m>n |
| "shorten it" / "limit to N chars" | `narrow` | `skills/op/narrow/SKILL.md` | `NVARCHAR(n) -> NVARCHAR(m)`, m<n (**Ambitious Narrowing**) |
| "change to a bigger number" / "INT to BIGINT" (widening) | `retype-implicit` | `skills/op/retype-implicit/SKILL.md` | in-place `ALTER COLUMN` (lossless) |
| "store the text as a date/number now" (reshaping) | `retype-explicit` | `skills/op/retype-explicit/SKILL.md` | add-new -> `TRY_CONVERT` -> drop-old (lossy) |
| "rename the attribute" | `rename-attribute` | `skills/op/rename-attribute/SKILL.md` | `sp_rename` via refactorlog (without the entry, a data-losing DROP+CREATE) |
| "remove this attribute" / "delete the field" | `delete-attribute` | `skills/op/delete-attribute/SKILL.md` | drop column (4-phase deprecation) |
| "new entity" / "create a table" | `create-entity` | `skills/op/create-entity/SKILL.md` | new `CREATE TABLE` |
| "rename the entity" | `rename-entity` | `skills/op/rename-entity/SKILL.md` | `sp_rename` table via refactorlog (without the entry, a data-losing DROP+CREATE) |
| "delete the entity" / "drop the table" | `delete-entity` | `skills/op/delete-entity/SKILL.md` | `DROP TABLE` (blocked on data loss; disable CDC + drop FKs first) |
| "move it to another module/schema" | `move-schema` | `skills/op/move-schema/SKILL.md` | `ALTER SCHEMA TRANSFER` or refactorlog |
| "archive old rows out" | `archive-entity` | `skills/op/archive-entity/SKILL.md` | batched move to an archive table (multi-phase) |
| "make this many-to-many" / "a bridge entity" | `junction` | `skills/op/junction/SKILL.md` | bridge table, composite PK over two FKs |
| "add a reference", **clean data** | `create-fk-clean` | `skills/op/create-fk-clean/SKILL.md` | `FOREIGN KEY`, orphan probe = 0 |
| "add a reference", **some children orphaned** | `create-fk-orphan` | `skills/op/create-fk-orphan/SKILL.md` | `NOCHECK` → reconcile → `WITH CHECK CHECK` (**Forgotten FK Check**) |
| "change what happens on delete" (Protect/Ignore/Delete) | `change-delete-rule` | `skills/op/change-delete-rule/SKILL.md` | `NO ACTION` / `CASCADE`; DROP+ADD FK |
| "remove the reference" / "unhook these" | `drop-fk` | `skills/op/drop-fk/SKILL.md` | `DROP CONSTRAINT` (integrity + plan loss) |
| "make this the identifier" / "primary key" | `define-pk` | `skills/op/define-pk/SKILL.md` | `PRIMARY KEY` (dup values fail; clustered build cost) |
| "add an index" / "make it faster" | `add-index` | `skills/op/add-index/SKILL.md` | nonclustered index (ONLINE = Enterprise) |
| "cover these columns" / "make this index unique" | `modify-index` | `skills/op/modify-index/SKILL.md` | index DROP+CREATE; UNIQUE flips on dupes |
| "we don't need that index" | `drop-index` | `skills/op/drop-index/SKILL.md` | `DROP INDEX` (prove usage first) |
| "rebuild it, it's fragmented" | `rebuild-index` | `skills/op/rebuild-index/SKILL.md` | **OPERATIONAL** — no declarative destination; route out |
| "default to X" (new default) | `add-default` | `skills/op/add-default/SKILL.md` | `DEFAULT` constraint `DF_Table_Col` |
| "change the default" / "stop defaulting" | `modify-default` | `skills/op/modify-default/SKILL.md` | DROP-then-ADD the `DEFAULT` constraint |
| "fill in the existing rows too" / "backfill the blanks" / "re-stamp the old rows" | `backfill-rows` | `skills/op/backfill-rows/SKILL.md` | post-deploy guarded idempotent UPDATE of existing rows (data-plane, not a schema edit) |
| "no duplicates" / "must be unique" | `add-unique` | `skills/op/add-unique/SKILL.md` | `UNIQUE` index (dups fail; one NULL only) |
| "only allow these values" / "must be positive" | `add-check` | `skills/op/add-check/SKILL.md` | `CHECK` constraint (existing violations fail) |
| "trust the constraint now" / "flip it on" | `toggle-trust` | `skills/op/toggle-trust/SKILL.md` | **OPERATIONAL** — `WITH CHECK CHECK`, not a CREATE edit |
| "static entity" / "lookup" / "reference table" (new) | `create-static-seed` | `skills/op/create-static-seed/SKILL.md` | post-deploy idempotent `MERGE`, explicit ids |
| "add a value to the list" / "change a label" | `edit-seed` | `skills/op/edit-seed/SKILL.md` | extend the guarded `MERGE` |
| "turn this text column into a lookup" | `extract-to-lookup` | `skills/op/extract-to-lookup/SKILL.md` | multi-phase: seed + nullable FK + backfill + drop text |
| "delete/retire a lookup value" | `delete-seed-value` | `skills/op/delete-seed-value/SKILL.md` | deactivate (`IsActive=0`), never hard DELETE |
| "split this entity into two" | `split-table` | `skills/op/split-table/SKILL.md` | multi-phase: create + copy + dual-write + drop |
| "merge these two entities" | `merge-tables` | `skills/op/merge-tables/SKILL.md` | multi-phase: prove 1:1 cardinality, absorb, drop |
| "this field is on the wrong entity" | `move-attribute` | `skills/op/move-attribute/SKILL.md` | copy-then-drop across tables (NOT a rename) |
| "Auto Number" on/off | `identity-swap` | `skills/op/identity-swap/SKILL.md` | **cannot ALTER** — shadow rebuild + `IDENTITY_INSERT` + reseed |
| "a view" / "an Advanced Query joining …" | `create-view` | `skills/op/create-view/SKILL.md` | `CREATE VIEW`, enumerated columns (**SELECT * View** trap) |
| "keep the old name working after a rename" | `compat-view` | `skills/op/compat-view/SKILL.md` | view bearing the old name over the new (temporary) |
| "point at a table in another database" | `synonym` | `skills/op/synonym/SKILL.md` | `CREATE SYNONYM` (runtime-resolution gap) |
| "materialize / cache the joined view" | `indexed-view` | `skills/op/indexed-view/SKILL.md` | `WITH SCHEMABINDING` + `UNIQUE CLUSTERED` |
| "full history on a NEW entity" | `temporal-new` | `skills/op/temporal-new/SKILL.md` | `SYSTEM_VERSIONING=ON` from birth (declarative) |
| "add history to an EXISTING populated entity" | `temporal-convert` | `skills/op/temporal-convert/SKILL.md` | multi-phase: period cols + backfill + enable versioning |
| "CreatedBy/CreatedOn/ModifiedBy/ModifiedOn" | `audit-columns` | `skills/op/audit-columns/SKILL.md` | nullable = M1; NOT NULL on populated = backfill |
| "turn on Change Data Capture" / "change feed for ETL" | `enable-cdc` | `skills/op/enable-cdc/SKILL.md` | **Script-Only**, not declarative (**CDC Surprise** — adds review scrutiny) |
| "CDC isn't picking up my new column" | `recreate-capture-instance` | `skills/op/recreate-capture-instance/SKILL.md` | capture-instance recreate / dual-instance |
| "just tell me WHICH rows changed" (mobile sync) | `change-tracking` | `skills/op/change-tracking/SKILL.md` | change tracking (lighter than CDC, all editions) |

If the developer's words don't fit any row, say so and ask a clarifying question — do not stretch
a near-match. "External Entity" (an OutSystems entity backed by a table you don't own) means the
destination edit may be out of your control; flag it and stop.

**Out of the catalog on purpose (route, don't stretch).** Some SQL objects have no op here because
an OutSystems-native developer does not author them from Service Studio and the change is a DBA's
call: **triggers, sequences, filegroups / partitioning, and index compression** — name the request,
say it is outside this tree, and route it to a DBA or principal. Three others are **genuine gaps an
OutSystems SSDT estate does hit and this catalog does not yet cover** — **computed / persisted
columns** (a cutover dealbreaker class), a **column collation change**, and **stored procedures /
functions** — handle them the same way (flag and route) until an op is authored for them
(`../../CERTIFICATION_PLAN.md` F13). Do not force any of these into a near-match op; a wrong op
proven perfectly is still wrong.

**The op skill IS the change-author's entry.** The op-slug you name resolves to
`skills/op/<op-slug>/SKILL.md` — that per-op skill is the exact file the change-author will open
for its provisional default, the How-it-flips table, and the verdict; the `_index` skill(s) it points
to carry the shared WHY. Because each per-op skill's frontmatter `description` is written in the
developer's own words, matching the phrasing IS the dispatch: if their sentence reads like a
description trigger, hand that slug with confidence.

## Step 2 — surface the destination they didn't say

The developer almost never states the destination; they state an *intent*. "Make it mandatory"
is a sentence about a checkbox; the destination is `NULL -> NOT NULL` on a specific column.
**Say the destination back in both languages** so the developer can catch a misunderstanding
before any data is touched:

> "To confirm: you want `Customer.Email` to become **required** — in the table that means
> `Email NVARCHAR(256) NOT NULL`. I'll describe that destination on the CREATE; I won't write an
> ALTER. Right?"

This is the team's doctrine in action: **"Stop writing migrations. Start describing destinations."**
/ **"Edit the CREATE, never write ALTER."** You are confirming the destination, not the journey.

> **Why this (and what it teaches).** You say the destination back in *both* vocabularies because
> the model **is** the schema: you describe where the table should end up and SSDT computes how to
> get there. What this teaches: the moment you can name the destination in table terms
> (`NULL -> NOT NULL`, `NVARCHAR(10)`), you have separated *what the developer wants* from *how the
> engine will achieve it* — and only the first is yours to confirm. Surface this so the developer
> hears their checkbox restated as a column shape; that translation is what moves them from
> thinking in Service Studio gestures to thinking in durable destinations.

## Step 3 — gather the four state-variables (the part the text can't tell you)

The **same operation ships a different way depending on the data.** You must establish
these four before classification. Ask the developer what they know; mark the rest **unknown** —
`prove-on-dacpac` will determine the unknowns against real-shaped data. Never guess them, and
never take a remembered row count as proof.

1. **Is the table populated?** (empty tables make most edits single-phase.)
2. **Does the existing data violate the new rule?** — NULLs for a make-mandatory, orphans for a
   new FK, over-length values for a narrow, duplicates for a unique. This is the variable that
   flips a one-line edit into a backfill.
3. **Is the table CDC-enabled, and is a no-gap capture required?** (CDC is the team's biggest
   tripwire — it adds review scrutiny and can force a multi-phase rollout.)
4. **Must old and new application code coexist during rollout?** — i.e. is there a window where
   the running OutSystems app and the new schema have to both work? (Forces phasing.)

Ask these in the developer's terms: "Roughly how many customers are in this table today, and do
any of them have a blank email?" — then record the answer as `known` only if they actually know;
otherwise `unknown`, to be proven.

> **Why this (and what it teaches).** You gather these four — and honestly mark the unknowns —
> because **we prove, we don't advise**: the `.sql` text and the developer's recollection both
> *look* like answers, but only the data on a disposable copy of Dev settles it. What this
> teaches: whenever the same edit could behave differently depending on what's already in the
> table, the row state is a question to *prove*, never a fact to *assume* — `unknown` is the
> honest default, not a gap. Surface this to the developer: a developer who learns that "I think
> it's clean" is a hypothesis, not a proof, stops being surprised by deploy-time refusals — that
> is judgment forming.

## Step 4 — hand off

Emit the change-order — with the **op-slug**, its **opSkill** path (`skills/op/<op-slug>/SKILL.md`),
and the pre-flagged **sharedConcern** (`skills/_index/<concern>/SKILL.md`) — and pass control to
`classify-mechanism`. Echo the developer's own phrasing in the handoff so the eventual verdict can be
delivered back in their words, and so the change-author opens the right per-op skill (whose
`description` is written in exactly those words) and the right `_index` WHY first.

## Traps you prevent here

Intake is where these get caught early (full catalog: handbook file 16 = §19). Each trap is OWNED
by an `_index` concern — pre-flag the owner, don't re-explain the trap:

- **A rename with no refactorlog entry** becomes DROP+CREATE = silent data loss.
  Flag rename ops now (`_index/identity-and-refactorlog`) so `prove-on-dacpac` reads the delta for it.
- **Optimistic NOT NULL** — "make it required" with no thought to existing rows. The whole point
  of step 3.2; owned by `_index/tightening-class` (the guard is table-has-rows, not NULL-has-rows).
- **CDC Surprise** — a change on a CDC-enabled table (`_index/cdc`). Step 3.3 is how you don't miss it.
- **Forgotten FK Check** — an orphaned child under a new FK (`_index/constraint-is-a-claim`). This is
  why create-FK splits into `create-fk-clean` / `create-fk-orphan` at intake, not downstream.

## Shared knowledge index (pre-flag the governing concern)

Once you have the op-slug, pre-flag the `skills/_index/<concern>/SKILL.md` that governs its shared
reasoning, so the change-author opens the right WHY first. You are not explaining the concern here —
you are routing to its owner. The six concerns and their triggers:

| If the op is… | pre-flag the concern |
|---|---|
| `make-mandatory`, `narrow`, `delete-attribute`, the NOT-NULL face of `add-mandatory`/`audit-columns` | `skills/_index/tightening-class/SKILL.md` (table-has-rows, data-blind guard) |
| `rename-entity`, `rename-attribute`, `move-schema`, `move-attribute`, `compat-view` | `skills/_index/identity-and-refactorlog/SKILL.md` (identity ≠ name) |
| `split-table`, `merge-tables`, `move-attribute`, `extract-to-lookup`, `archive-entity`, `retype-explicit`, `temporal-convert`, `delete-attribute` (4-phase) | `skills/_index/multi-phase/SKILL.md` (old + new coexist; conservation proof) |
| `enable-cdc`, `recreate-capture-instance`, `change-tracking`, **any op on a CDC-enabled table** | `skills/_index/cdc/SKILL.md` (the added-scrutiny tripwire, frozen capture shape) |
| `define-pk`, `create-fk-clean`, `create-fk-orphan`, `add-unique`, `add-check`, `toggle-trust`, `modify-index`→unique | `skills/_index/constraint-is-a-claim/SKILL.md` (a constraint is a data claim) |
| `create-static-seed`, `edit-seed`, `delete-seed-value`, the seed leg of `extract-to-lookup`, `backfill-rows`, the no-op redeploy | `skills/_index/idempotent-seed/SKILL.md` (guarded MERGE / UPDATE, silence is the proof) |

A change on a **CDC-enabled table** always pre-flags `_index/cdc` *in addition to* the op's own
concern — the added scrutiny rides on top of the base op (that is the whole `TRAP-01N` lesson).
Put the flagged concern in the change-order's `sharedConcern` field.

## Connector points

- **`.claude/skills/` (Claude Code):** this file's frontmatter (`name` = kebab matching the
  directory, plus a triggering `description`) is already shaped for `.claude/skills/`. See
  `CONNECTORS.md` — adoption is a copy, not a rewrite.
- **GitHub Copilot custom agents:** the intake role maps to a Copilot custom agent, but the file
  format must be **verified first** (see `CONNECTORS.md`, flagged).
- **F# engine:** a real OutSystems catalog (the engine's `Render`/`SsdtBundle` output) can supply
  the operation's real column types and the current data shape, replacing the developer's
  recollection in step 3. Highlighted, not wired.
