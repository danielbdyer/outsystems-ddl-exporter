---
name: change-delete-rule
description: Use when the developer changes the Delete Rule on a reference — "change the Delete Rule to Protect/Ignore/Delete", "turn on cascade delete", "deleting a Customer should delete its Orders". A DROP and re-add of the FK to set its ON DELETE action; the risk is behavioural, not in the publish — especially CASCADE.
---

# Change the delete rule / cascade (Protect / Ignore / Delete)

> **Default (provisional — prove before you classify).** One release, applied in place: the foreign
> key is dropped and re-added to set its ON DELETE action, and no existing row is written. A dev lead
> reviews it: toward CASCADE the change alters runtime behaviour so a single parent delete removes
> child rows in another table — the publish is clean, the risk is behavioural. Prove the delta and the
> cascade's reach on a copy before classifying.

> **SHIP terminal: ONE RELEASE, in place, trusted.** Proven live on this branch (DB `db_cdr2`):
> changing `FK_Order_Status` to `ON DELETE CASCADE`, the generated script is `DROP CONSTRAINT
> [FK_Order_Status]` then `WITH NOCHECK ADD CONSTRAINT [FK_Order_Status] … ON DELETE CASCADE` then
> `WITH CHECK CHECK CONSTRAINT [FK_Order_Status]`; the strict publish returns `Successfully published
> database.`, `delete_referential_action_desc = CASCADE`, `is_not_trusted = 0`. No row is written; the
> re-add re-scans the child rows. Cascade reach (DB `db_cascbehav`): deleting one Status removed its 2
> Orders and left their 4 OrderLines orphaned — CASCADE goes one level. `FINDINGS_AND_CHANGES.md` F9.
>
> **Proven precedent:** `../../../sample-prs/change-delete-rule.md` — the worked instance of the
> ten-section pull-request template (`../../author-pr/SKILL.md`) for this op, carrying the live messages.

## OutSystems phrasing
The **Delete Rule** on the reference — **Protect** ("can't delete a Customer with Orders"),
**Ignore** ("let the Customer go, leave the Orders"), **Delete** ("delete the Customer and its
Orders").

## SSDT meaning
The FK's `ON DELETE` action. Mapping: **Protect → `ON DELETE NO ACTION`**; **Ignore → no clean
single-DB equivalent** (either `NO ACTION` with the parent delete still blocked, **or** `ON DELETE SET
NULL` if the FK column is nullable — ask which the developer means, do not silently pick); **Delete →
`ON DELETE CASCADE`**. You cannot alter the action in place, so the generated script drops the key and
re-adds it (`WITH NOCHECK ADD` then `WITH CHECK CHECK`, ending trusted). Edit the CREATE; never write `ALTER`.

## The named trap
Turning on **CASCADE** silently changes runtime behaviour — a delete that previously *failed* now
*removes child rows*. And the cascade reaches exactly one level: it removes the direct children whose
key declares CASCADE and **stops**, leaving grandchildren orphaned unless their own key cascades too
(proven: deleting a Status removed its Orders but left the Orders' OrderLines behind). Nothing here
touches the *publish* — a DROP and re-add is not blocked on data, because the rows were already valid —
so the danger is entirely behavioural.

## How it flips (the specifics only)
- the re-add re-validates the child rows and ends trusted; the publish is not blocked, because the key
  already held → one release, in place.
- toward CASCADE → a parent delete now removes child rows in another table → a dev lead reviews it, and
  the cascade reach is traced (which children go, and where it stops) before shipping.
- if the change also tightens the key (a new column or a re-point that some rows fail) → the create-fk
  orphan rules apply and it can flip to a reconcile-then-add change (see `../create-fk-orphan/SKILL.md`).

## Prove it
Script the delta and confirm it is `DROP CONSTRAINT` then `WITH NOCHECK ADD CONSTRAINT … ON DELETE
<action>` then `WITH CHECK CHECK` (not a table rebuild). For CASCADE, prove the reach: on a copy,
delete one parent and record which child rows are removed and which tables are left untouched. See
`../../prove-on-dacpac/SKILL.md` + `../../talk-to-local-sql/SKILL.md`. Seed: the Order → Status
reference and the Order → OrderLine rows make the one-level reach visible — the Orders cascade, the
OrderLines are left orphaned. **Setup note:** the sample's Order → Status FK is undeclared in the
authored baseline (it is a create-fk proof surface), so to prove a *rule change* first declare it
`NO ACTION`, then change it to CASCADE — disclose that the `NO ACTION` declaration is your setup, not
the authored baseline.

## The verdict (to the developer)
You asked to change the Delete Rule to Delete, which SSDT expresses as `ON DELETE CASCADE`: from now
on, deleting a Status also deletes every Order with that Status. On a copy of Dev, deleting one Status
removed its Orders — and left those Orders' OrderLines behind, orphaned, because the cascade reaches
only the direct children. The schema change itself is clean: the foreign key is dropped and re-added,
re-validates the existing rows, and ends trusted; nothing in the data blocks it. The reason to be
careful is runtime, not the publish — a single delete now removes rows in a second table and stops
there, so decide whether the orphaned OrderLines are acceptable or the chain must cascade too. A dev
lead should review it.

## The reasoning (in conversation)
A clean publish is not the same as a safe change. Some edits are risky in what they *do* at runtime,
not in what they do to the existing rows — how smoothly this deploys tells you nothing about whether a
delete will now cascade. And CASCADE is not transitive on its own: it removes the direct children and
stops, so a partial cascade can leave orphaned grandchildren. Trace the reach on a copy before
shipping, and read "it deployed clean" as nothing more than that.

## On the record
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op — with the live messages — is `../../../sample-prs/change-delete-rule.md`. SHIP
terminal: **ONE RELEASE, in place, trusted.** The fragment this operation contributes:

**Review & release**
- A dev lead reviews this: toward CASCADE, deleting a parent row silently removes its direct child rows
  in another table, and stops there — leaving grandchildren orphaned unless their key cascades too.
- Ships as one release, applied in place: the foreign key is dropped and re-added to set its ON DELETE
  action, re-validates the child rows, and ends trusted. No row is written.

**Verification** — run in each environment after deployment
```sql
-- expect the intended action (e.g. CASCADE): the delete rule landed as specified
SELECT name, delete_referential_action_desc
FROM sys.foreign_keys
WHERE name = 'FK_<Child>_<Parent>';
```

**Rollback**
Drop and re-add the foreign key with its previous ON DELETE action — lossless at the schema level,
because the publish writes no data. Not auto-reversible: any child rows a CASCADE has already removed
in a live environment are gone, and restoring the previous rule does not bring them back.

**Not verified**
- Application impact — any code path that relied on the delete being blocked (Protect) now succeeds
  and removes child rows; the running application's delete behaviour is not confirmed here (app owner).
- Cascade reach — the copy proves the cascade reaches the direct children and stops (Orders go, their
  OrderLines are left orphaned); whether that is acceptable, or the chain should cascade further, is a
  data-model decision to settle before promotion.
- Production scale and timing — a large cascade may remove many rows and run long or block writes; the
  small copy does not show it.
