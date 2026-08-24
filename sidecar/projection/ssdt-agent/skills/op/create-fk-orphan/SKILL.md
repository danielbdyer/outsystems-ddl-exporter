---
name: create-fk-orphan
description: Use when the developer says "add a reference to Customer" but the data is dirty — some child rows point at parents that do not exist (orphans). The orphan must be reconciled in a pre-deploy or the add blocks with Msg 547; the declarative add then lands trusted on its own.
---

# Create a foreign key with orphans (reconcile first, or the add blocks)

> **Default (provisional — prove before you classify).** One release, applied in place, with a
> pre-deploy step that reconciles the orphans first — the add re-validates every child row, so an
> orphan blocks it with `Msg 547` until it is reconciled. A dev lead reviews it: existing data is
> modified and a new cross-table relationship is added. If the orphans are still being created by the
> running application, it stages across releases instead, so the application keeps working while the
> change is in flight.

> **SHIP terminal: ONE RELEASE, trusted (pre-deploy reconcile).** Proven live on this branch
> (DBs `db_orphB`, `db_orphC`, `db_orphD`): adding `FK_Order_Customer_CustomerId` with an orphan
> present and no reconcile → **BLOCK `Msg 547`** ("the ALTER TABLE statement conflicted with the
> FOREIGN KEY constraint …"). With the orphan removed in a pre-deploy and the seed fixed → publish
> lands, 0 orphans, `is_not_trusted = 0`. DacFx's generated script is `WITH NOCHECK ADD` then
> `WITH CHECK CHECK`, so the key trusts itself — a manual post-deploy `WITH CHECK CHECK` is redundant
> (`db_orphC`, identical result). `FINDINGS_AND_CHANGES.md` F5 (overturned)/F9.
>
> **Proven precedent:** `../../../sample-prs/create-fk-orphan.md` — the worked instance of the
> ten-section pull-request template (`../../author-pr/SKILL.md`) for this op, carrying the live messages.

## OutSystems phrasing
Same as create-fk-clean ("add a reference to Customer", "Order belongs to a Customer"), but some child rows point at parents that do not exist.

## SSDT meaning
The declarative FK add re-validates every existing child row on publish. With an orphan present it
blocks (`Msg 547`). The path: reconcile the orphans in a **pre-deploy** step (delete them, repoint
them, or insert the missing parents), then the same declarative add lands and trusts itself. Edit the
CREATE to add the key; put the reconcile in the pre-deploy script; never hand-write `WITH NOCHECK`.

## The named trap
Reaching for `WITH NOCHECK` by hand to get past the `Msg 547` block. That leaves the key present but
**untrusted** (`is_not_trusted = 1`), protecting nothing and ignored by the optimizer — and it is
never needed: reconcile the orphan and the declarative add trusts the key on its own. The failure is
the untrusted key, not the block; the block is the constraint doing its job. See
`../../_index/constraint-is-a-claim/SKILL.md`.

## How it flips (the specifics only)
- orphans reconcilable in one release → one release, in place: a pre-deploy `DELETE`/repoint clears the
  orphans, then the declarative add lands trusted. A dev lead reviews it, because existing data is modified.
- orphan reconcile **deletes** child rows → data is removed and cannot be undone from the schema; a
  principal reviews it.
- reconcile must wait on an app change (orphans still being created) → stages across releases so the
  running application keeps working while the change is in flight; a coexistence concern (see
  `../../_index/multi-phase/SKILL.md`).
- >1M rows → added scrutiny: the re-validation scans the table and the pre-deploy `DELETE` runs over
  the rows — either may block writes or run long (schedule a window).
- **the new child column is not auto-indexed** → recommend a nonclustered index on it. SQL Server
  indexes the parent side of a foreign key, never the child, so the join scans until it is indexed
  (F11). See `../../_index/when-to-index/SKILL.md`.

## Prove it
First prove the add is blocked and by how much — the orphan count via `LEFT JOIN ... WHERE p.<pk> IS
NULL`; publish without reconciling → `Msg 547`, the conflicting constraint named. Then add the
pre-deploy reconcile (and fix any seed that plants the orphan) and publish again → it lands, the orphan
count is 0, and `is_not_trusted = 0` without any manual step. See `../../prove-on-dacpac/SKILL.md` +
`../../talk-to-local-sql/SKILL.md`. Seed: the `Order.CustomerId = 999` orphan drives the whole sequence.

## The verdict (to the developer)
You asked to add a reference from Order to Customer. On a copy of Dev, the add was refused: `Msg 547` —
some Orders point at Customers that do not exist, and the key will not validate while those orphans are
there. So this release adds a pre-deploy step that clears the orphans first, then the foreign key adds
and ends trusted in the same publish. A dev lead should review it, because it changes existing data. The
call that is yours: how the orphans are fixed — repoint them to a real Customer, add the missing
Customers, or delete the orphaned Orders. Separately, the CustomerId column is not indexed — a foreign
key does not index its child side — so a nonclustered index on it is worth adding, in this PR or as a
fast follow.

## The reasoning (in conversation)
The block is the constraint working, not a problem to dodge: `Msg 547` means a child has no parent, and
the fix is to reconcile the data, never to hand-write `WITH NOCHECK`. Once the orphans are gone the
declarative add trusts the key on its own (`WITH NOCHECK ADD` then `WITH CHECK CHECK` in one publish),
so there is no separate trust step to remember. Confirm `is_not_trusted = 0` after deploy. See
`../../_index/constraint-is-a-claim/SKILL.md`.

## On the record
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op — with the live messages — is `../../../sample-prs/create-fk-orphan.md`. SHIP
terminal: **ONE RELEASE, trusted (pre-deploy reconcile).** The fragment this operation contributes:

**Review & release**
- A dev lead reviews this: existing data is modified (the orphans are reconciled) and a new cross-table
  relationship is added. If the reconcile deletes child rows, a principal reviews it: data is removed
  and cannot be undone from the schema.
- Ships as one release, applied in place — a pre-deploy step reconciles the orphans, then the
  declarative add re-validates every child row and ends trusted. If orphans are still being created by
  the running application, it stages across releases instead.
- Added scrutiny: none for a small table; at >1M rows the re-validation scan and the pre-deploy DELETE
  may block writes or run long (schedule a window).

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: every child points at a parent that exists
SELECT c.<fk> FROM child c LEFT JOIN parent p ON c.<fk> = p.<pk> WHERE p.<pk> IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key is validated and honored by the optimizer
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_<child>_<parent>';
```

**Rollback**
The foreign key drops without data loss: `ALTER TABLE <child> DROP CONSTRAINT FK_<child>_<parent>;`. The
orphan reconcile is not auto-reversed — the original child values are recorded in the pre-deploy step's
output for a manual restore.

**Not verified**
- Application impact: once the key is trusted, an insert or update that points a child at a parent that
  does not exist is rejected with error 547; application-side validation is not confirmed here.
- Other environments: the orphan set was proven on a copy of Dev only; QA, UAT, and Prod may hold a
  different set — run the orphan query before promotion and confirm the pre-deploy DELETE matches it.
- Trust on a different build config: the key trusted itself on this project's build; a project whose
  DacFx settings suppress the `WITH CHECK CHECK` would leave it untrusted. Confirm `is_not_trusted = 0`;
  if it is 1, a post-deploy `WITH CHECK CHECK` re-trusts it.
- Production scale: the re-validation and the reconcile are exercised at seed scale only; blocking and
  duration at production row counts are not shown by the small copy.
