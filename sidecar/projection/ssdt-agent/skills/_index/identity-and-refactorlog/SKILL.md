---
name: identity-and-refactorlog
description: Cross-cutting KNOWLEDGE shared by rename-entity, rename-attribute, move-schema, and the compat-view bridge. Owns the discipline that IDENTITY IS SEPARATE FROM NAME — the refactorlog records that an old object and a new name are the same identity, so SSDT emits sp_rename (data + object_id preserved) instead of the DROP+CREATE / DROP COLUMN+ADD that silently loses the data. Owns the read-the-delta discriminator and the Refactorlog Cleanup companion trap. Per-op skills POINT here. The publish loop that PROVES the delta is sp_rename lives in prove-on-dacpac.
---

# Identity is separate from name — the refactorlog carries the data

> The single most important read in the whole tree: **is the generated delta `sp_rename`, or is
> it `DROP`+`CREATE`?** One preserves every row; the other drops them silently. Every op that
> changes a *name* an object is addressed by points here so the read is done the same way, every
> time.

You are helping an **OutSystems-native developer** who renamed an entity or attribute in Service
Studio and expects the data to follow. It only follows if SSDT knows the old object and the new
name are the **same identity** — and that knowledge lives in the **refactorlog**, not in the
`CREATE` text.

## The core distinction

- **Name** is an *address* — `dbo.Customer`, `Customer.Email`, `schema.Table`. Changing an address
  is cosmetic to the data.
- **Identity** is the *thing that carries the rows* — the `object_id`, the physical column. A safe
  rename keeps identity constant while changing the address.

The refactorlog is the record that says "the old address and the new address are the same
identity." With it, SSDT emits:

```sql
EXEC sp_rename 'dbo.Customer.Email', 'EmailAddress', 'COLUMN';   -- data preserved, identity preserved
```

**Without** it, SSDT sees one address vanish and a new one appear, and emits a drop-and-recreate
that loses the data:

```sql
ALTER TABLE [dbo].[Customer] DROP COLUMN [Email];   -- every value in the column, gone
ALTER TABLE [dbo].[Customer] ADD [EmailAddress] NVARCHAR(256) NULL;
```

(For a whole entity it is `DROP TABLE` + `CREATE TABLE`; for a schema move it is the same shape on
the two-part name.)

## The named traps this concern owns

- **A rename with no refactorlog entry** (handbook **16** = §19.1) — editing the name text
  *without* the refactorlog entry. The build succeeds (SSDT cannot see your rows); the deploy
  silently drops and recreates the object.
- **Refactorlog Cleanup** (handbook **16** = §19.6) — the companion. **Never delete old refactorlog
  entries.** A fresh-environment deploy *replays* the whole refactorlog; a deleted entry re-becomes
  a drop+create on the clean environment. The refactorlog is append-only history, not scratch.

## The WHY (specialize per op; do not restate the whole thing there)

A rename is safe **only because identity is separate from name.** SSDT diffs *shapes*, not intent:
absent an identity mapping, a renamed column is indistinguishable from "old column deleted, new
column added," and SSDT does the literal thing: it drops the old column and adds a new, empty
one. The refactorlog supplies the missing
identity link so the diff resolves to `sp_rename`. This is why you **never trust that an edit
*named* like a rename *behaves* like one** — you read the generated delta and confirm `sp_rename`
before publishing, catching the loss in the script (where it is free) instead of in production
(where it is not).

## The read-the-delta discriminator (the single most important read)

Script the delta and look:

- **`sp_rename ... 'COLUMN'` / `... 'OBJECT'`** (or `ALTER SCHEMA ... TRANSFER` for a move) → safe,
  data preserved. Proceed.
- **`DROP COLUMN`+`ADD`** or **`DROP TABLE`+`CREATE TABLE`** on something you asked to RENAME →
  **the delta drops and recreates the object, and the data is lost. Stop.** The refactorlog entry
  is missing. Demand it, rebuild, re-preview, and confirm the delta is now a rename. This carries
  the danger of irreversible data loss, regardless of how simple the single statement looks.

Also confirm the `.refactorlog` file actually changed when the rename was authored — an
unrecorded rename is the failure mode.

## The ops this governs (and how each differs)

- **rename-attribute** — `sp_rename ... 'COLUMN'`; a dev lead or an experienced developer should
  review it, because every caller of the column name (views, procs, ORM mappings, reports, ETL)
  must change.
- **rename-entity** — `sp_rename ... 'OBJECT'`; a dev lead or an experienced developer should
  review it, because every reference to the table name breaks.
- **move-schema** — the two-part `schema.Table` name is *just an address*; the refactorlog (or an
  explicit `ALTER SCHEMA target TRANSFER source.Table`, which preserves `object_id`) tells SSDT the
  identity survived the move. Same DROP+CREATE trap without it.
- **compat-view** — the bridge that lets the *old* name keep resolving after a rename: identity
  survived the move, and the view re-exposes the old address for consumers not yet updated (see
  the per-op skill for the recipe).

## Cross-table MOVE is NOT a rename

A **cross-table move** (move-attribute, move a field from Customer to Account) has **no refactorlog
identity mapping** — the refactorlog records renames *in place*, not relocations between objects.
So a cross-table move **must be copy-then-drop**, never a rename; letting SSDT treat "move" as a
rename does a DROP+CREATE that loses the values. That op is multi-phase (see
`../multi-phase/SKILL.md`); this concern only tells you *why it cannot be a rename*.

## Prove it (pointer, not a re-scaffold)

For the publish loop that PROVES the delta is `sp_rename` (script the delta, read it, confirm) see
`../../prove-on-dacpac/SKILL.md`; for confirming row counts / `object_id` are intact after an
`ALTER SCHEMA TRANSFER` see `../../talk-to-local-sql/SKILL.md`.

## Handbook

Cite by **filename**: **09-The-Refactorlog-and-Rename-Discipline.md** (the discipline), and **16**
(= §19; specifically §19.1, a rename with no refactorlog entry, and §19.6 Refactorlog Cleanup).
