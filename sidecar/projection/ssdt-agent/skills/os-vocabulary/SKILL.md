---
name: os-vocabulary
description: The OutSystems <-> SQL Server vocabulary for the conversation surface. Use at intake and any time the developer's words and the SSDT world need translating in either direction — hearing "static entity", "delete rule", "bootstrap", "Integration Studio refresh" correctly, and explaining a refactorlog, a publish profile, a dacpac, or a trusted constraint in one anchored sentence. Owns the noun map, the gesture map (what used to be a click in Service Studio -> what changes now), the anchored explanations for SSDT-only artifacts, and the listening notes for OutSystems terms that carry scope. THE_RECORD.md §4 owns the internal->record direction; this skill owns the developer-facing one.
---

# Speak the developer's language

> **Why this exists.** The conversation runs in the developer's words (`../../THE_RECORD.md` §3); the
> record runs in the DBA's (§2). The developer in this estate is **SQL-experienced and SSDT-new**:
> tables, columns, and foreign keys need no explanation, but Service Studio gestures (a checkbox, a
> delete rule, 1-Click Publish) need mapping to table-definition edits, and SSDT's own artifacts need
> a one-sentence anchor the first time they come up. Translation is this tree's job, in both
> directions — the developer is never required to learn an SSDT word before their change can proceed.

## The noun map

Both words are correct in conversation; the record always carries the SQL name
(`entity` alone never appears in a pull request — `THE_RECORD.md` §4).

| The developer says | The schema meaning | Conversation note |
|---|---|---|
| entity | table | interchangeable in conversation |
| attribute | column | interchangeable in conversation |
| identifier | primary key (`INT IDENTITY` by default) | "your Id column" |
| AutoNumber | the `IDENTITY` property | toggling it is a table rebuild — see `../op/identity-swap/` |
| reference | foreign key | "the relationship from Order to Customer" |
| Mandatory (the checkbox) | `NOT NULL` | whether it lands depends on the rows — see `../op/make-mandatory/` |
| static entity | a lookup table plus seeded rows | the records are application constants: explicit ids, never `IDENTITY` — see `../_index/idempotent-seed/` |
| bootstrap | an initial data load | here: seed rows in the post-deployment script |
| delete rule | the foreign key's delete behavior | Protect blocks the parent delete (`NO ACTION`); Delete cascades (`ON DELETE CASCADE`); Ignore's mapping is owned by `../op/change-delete-rule/` — do not improvise it |
| External Entity | a table owned outside OutSystems | that is this team: SSDT owns the schema, OutSystems consumes it |
| extension / Integration Studio | the bridge OutSystems uses to see external tables | its refresh is an application-side step — see Listening, below |

## The gesture map (what used to be a click)

The developer's instincts are Service Studio gestures. Meet each one with what actually happens now
— and where the risk moved.

- **"I used to tick Mandatory and publish."** Now the column becomes `NOT NULL` by editing the table
  definition — and whether it applies is decided by the rows, not the edit. On a populated table the
  deployment is blocked even after the blanks are filled (see `../op/make-mandatory/`).
- **"I used to rename the attribute and publish."** Now a rename is the edited name **plus** a
  refactorlog entry. Without the entry, SQL sees one column vanish and another appear, and the data
  goes with it (see `../op/rename-attribute/`).
- **"I used to change the delete rule in the reference."** Now the foreign key is dropped and
  re-added with the new rule — and a cascade's reach across child tables is mapped before it ships
  (see `../op/change-delete-rule/`).
- **"I used to add a record to the static entity."** Now it is a guarded MERGE row in the seed, with
  an explicit id, and a re-deploy that touches zero rows is the proof it is right (see
  `../op/edit-seed/`).
- **"I used to just publish."** Now the change ships as a pull request, deploys through the
  pipeline, and *then* the entity is refreshed in Integration Studio — the application-side step the
  schema change cannot do for you.

## SSDT-only artifacts — the one-sentence anchors

Introduce each with its anchor the first time it comes up in conversation; after that, call it by
name. Do not explain SQL to this developer; explain SSDT.

- **refactorlog** — "the file that records a rename as *the same column keeping its data* — Service
  Studio tracked that identity for you invisibly; out here, this file is where it lives."
- **dacpac** — "the compiled snapshot of the whole schema — what 1-Click Publish built for the app,
  but for the database."
- **publish profile** — "the safety settings a deployment runs under; it is why a risky change is
  blocked instead of quietly applied."
- **pre-deployment / post-deployment script** — "the only two places procedural SQL runs: before the
  schema change, to fix data so the change can land; after it, to seed lookup rows."
- **trusted constraint** — "SQL Server distinguishes a rule it has verified against every existing
  row from one it merely stores; only the verified kind protects anything, and we always finish with
  that kind (`is_not_trusted = 0`)."
- **capture instance** — "the change feed's frozen copy of the table's shape; a new column is
  silently absent from the feed until the instance is recreated."
- **a disposable copy of Dev** — "a scratch database shaped like Dev that we publish against to see
  what SSDT will actually do, then throw away."

## Listening — OutSystems words that carry scope

What the developer says places the work. Some of it is not schema work; say so plainly rather than
absorbing it.

- **"Integration Studio refresh" / "republish" / "Outdated References"** — application-side steps
  that follow a schema change. They cannot be proven on a disposable copy; the pull request names
  them under **Not verified** with the owner who confirms them.
- **"Site property", "timer", "screen", "aggregate"** — application concerns. If the request lives
  there, it is not a schema change; route it back rather than forcing it into an operation.
- **"The publish failed."** Ambiguous: the OutSystems publish or the database deployment? Ask which,
  in those words.
- **"Nobody uses that column/table."** An assumption, not a fact — the schema side can check
  referencing objects, and the application side stays an open question for the owner (see
  `../op/delete-attribute/`).

## Rules

- Translate at the boundary, in both directions; the developer's phrasing is never the obstacle.
- Anchor once, then plain: one sentence the first time an SSDT artifact comes up, its name after.
- Never explain SQL to a SQL-experienced developer; never require SSDT vocabulary from them.
- Ambiguity is resolved in their words, one question at a time (`../confirm-intent/SKILL.md` owns
  the request-to-operation dispatch; this file owns the words around it).
- The record's own lexicon — internal names out, plain findings in — is `../../THE_RECORD.md` §4,
  not this file. This file exists so the *conversation* is fluent, both ways.
