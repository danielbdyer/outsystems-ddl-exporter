---
name: change-delete-rule
description: Use when the developer changes the Delete Rule on a reference — "change the Delete Rule to Protect/Ignore/Delete", "turn on cascade delete", "deleting a Customer should delete its Orders". A DROP+ADD of the FK to set its ON DELETE action; the risk is behavioural, not in the publish — especially CASCADE.
---

# Change the delete rule / cascade (Protect / Ignore / Delete)

> **Default (provisional — the data decides).** A dev lead must review this: changing the rule
> toward CASCADE alters runtime behaviour so a single parent delete silently removes child rows in
> another table — the publish is clean, the risk is entirely behavioural. Ships as a single schema
> change, applied in place: the foreign key is dropped and re-added to set its ON DELETE action, and
> no existing data is modified. Prove the delta and the cascade's dependency scope on a disposable
> copy before classifying.

## OutSystems phrasing
The **Delete Rule** on the reference — **Protect** ("can't delete a Customer with Orders"),
**Ignore** ("let the Customer go, leave the Orders"), **Delete** ("delete the Customer and its
Orders").

## SSDT meaning
The FK's `ON DELETE` action. Mapping: **Protect → `ON DELETE NO ACTION`**; **Ignore → no clean
single-DB equivalent** (either `NO ACTION` + app-tolerated dangling refs, which still *blocks* the
parent delete, **or** `ON DELETE SET NULL` if the FK column is nullable — ask which the developer
means, don't silently pick); **Delete → `ON DELETE CASCADE`**. Changing the rule is a **DROP + ADD**
of the FK (you cannot alter the action in place).

## The named trap
Turning on **CASCADE** silently changes runtime behaviour — a delete that previously *failed* now
*removes child rows*, possibly **chaining** through multiple tables; cascaded deletes may also bypass
expected CDC/audit capture. None material to the *publish* (a DROP+ADD is never blocked on data) —
the danger is entirely behavioural.

## How it flips (the specifics only)
- the DROP+ADD is schema-only, so the deployment is never blocked on data → it ships in place as a
  single schema change.
- toward CASCADE → silent multi-table deletes → a dev lead must review this, and the full cascade
  graph is mapped before shipping.
- if the change also tightens the FK (re-validate existing rows) → the create-fk orphan rules apply →
  it could flip to a scripted change (see `../create-fk-orphan/SKILL.md`).
- CDC-enabled → cascaded deletes may bypass the expected change-data-capture capture → added scrutiny
  (see `../../_index/cdc/SKILL.md`).

## Prove it
Script the delta and confirm it is `DROP CONSTRAINT` + `ADD CONSTRAINT ... ON DELETE <action>` (NOT
a table rebuild). For CASCADE, prove the dependency scope: on a disposable copy of Dev, delete one
parent and record which child rows are removed across the whole cascade chain. See
`../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: the Order → OrderLine
chain (KEY-01) makes the cascade visible across two levels. **Setup note:** the sample's
`OrderLine → Order` FK is intentionally *undeclared* (it is a create-fk proof surface), so to prove a
*rule change* first declare it `NO ACTION`, then change it to CASCADE — disclose that this NO_ACTION
declaration is your setup, not the authored baseline.

## The verdict (to the developer)
You asked to change the Delete Rule to Delete, which SSDT expresses as `ON DELETE CASCADE`: from now
on, deleting an Order also deletes its OrderLines. That behaviour held on a disposable copy of Dev —
one Order delete removed its child lines, then rolled back. The schema change itself is clean: the
foreign key is dropped and re-added, and nothing in the existing data blocks it. The reason to be
careful is runtime, not the publish — a single delete now silently removes rows in a second table,
and in a deeper reference graph it would chain further, so map the full cascade before this ships. A
dev lead should review it before it goes out.

## The reasoning (in conversation)
A clean publish is not the same as a safe change. Some edits are risky in what they *do* at runtime,
not in what they do to the existing rows — how smoothly this deploys tells you nothing about whether
a delete will now cascade through your tables. The trap to avoid is reading "it deployed clean" as
"it's safe," when the CASCADE you just turned on will silently delete across tables the first time
someone removes a parent row.

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead must review this: changing the delete rule toward CASCADE alters runtime behaviour so
  that deleting a parent row silently removes its child rows in another table.
- Ships as a single schema change, applied in place: the foreign key is dropped and re-added to set
  its ON DELETE action. No existing data is modified.
- Added scrutiny, when it applies: a CDC-tracked table, where cascaded deletes may bypass the expected
  change-data-capture capture (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect the intended action (e.g. CASCADE): the delete rule landed as specified
SELECT name, delete_referential_action_desc
FROM sys.foreign_keys
WHERE name = 'FK_<Child>_<Parent>';
```

**Rollback**
Drop and re-add the foreign key with its previous ON DELETE action — lossless at the schema level,
because the publish modifies no data. Not auto-reversible: any child rows a CASCADE has already
removed in a live environment are gone, and restoring the previous rule does not bring them back.

**Not verified**
- Application impact — any code path that relied on the delete being blocked (Protect) now succeeds
  and removes child rows; the running application's delete behaviour is not confirmed here
  (@app-owner).
- Cascade depth — the disposable copy proves the seeded two-level chain (Order → OrderLine); deeper
  chains in the real reference graph are not exercised until the full cascade is mapped.
- Capture — whether a change-data-capture stream on the affected tables records the cascaded deletes
  is not verified on the copy (see `../../_index/cdc/SKILL.md`).
- Production scale and timing — a large cascade may remove many rows and run long or block writes; the
  small copy does not show it.
