---
name: create-fk-clean
description: Use when the developer says "add a reference to Customer", "draw the relationship from Order to Customer", "Order belongs to a Customer" AND the child data is clean (every child points at a real parent) — a FOREIGN KEY that SQL Server validates against every existing child row and lands trusted in one release.
---

# Create a foreign key, clean data (Forgotten FK Check)

> **Default (provisional — prove before you classify).** One release, applied in place — the key is
> added and validated against every existing child row in a single publish, and no data is modified.
> A dev lead reviews it: a new cross-table relationship changes what the database accepts. Run the
> orphan probe first; if any child has no parent, this op does not apply — route to
> `../create-fk-orphan/SKILL.md`.

> **SHIP terminal: ONE RELEASE, trusted.** Proven live on this branch (SQL Server 2022,
> `sqlpackage 170.4.83.3`, DB `db_fkc2`): adding `FK_Order_Status` (`Order.StatusId → Status.Id`) with
> every child row valid, the generated script is `ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CONSTRAINT
> [FK_Order_Status] …` then `ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status]`,
> the strict publish (`BlockOnPossibleDataLoss = true`) returns `Successfully published database.`, and
> the key lands `is_not_trusted = 0`. DacFx's own `WITH CHECK CHECK` trusts it — there is no manual
> trust step. `FINDINGS_AND_CHANGES.md` F8/F9.
>
> **Proven precedent:** `../../../sample-prs/create-fk-clean.md` — the worked instance of the
> ten-section pull-request template (`../../author-pr/SKILL.md`) for this op, carrying the live messages.

## OutSystems phrasing
"add a reference to Customer", "draw the relationship from Order to Customer", "Order belongs to a Customer".

## SSDT meaning
`CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status]([Id])` added to the
child table's CREATE. On publish SQL Server validates every existing child row against the parent. With
every `Order.StatusId` present in `Status`, it lands. Edit the CREATE; never write `ALTER`.

## The named trap
**Forgotten FK Check** (handbook file 16 = §19.3): adding the key without probing for **orphans** —
child rows whose parent key does not exist. The declarative add re-validates the child rows on publish,
so a single orphan blocks the deploy with `Msg 547`. Reaching for `WITH NOCHECK` by hand to dodge the
block leaves the key untrusted (`is_not_trusted = 1`) — the anti-pattern. This is the constraint-is-a-claim
family; the orphan-reconcile path lives in `../create-fk-orphan/SKILL.md`.

## How it flips (the specifics only)
- no orphan rows → one release, in place; the key adds and re-validates in one publish and ends trusted
  (`is_not_trusted = 0`). Inserts and updates are now validated against the parent.
- orphan rows present → the re-validation blocks the deploy with `Msg 547` → this becomes a
  reconcile-then-add change; route to `../create-fk-orphan/SKILL.md`.
- parent/child table large → the validation scans the rows → added scrutiny at >1M rows: the scan may
  block writes or run long, so schedule a window.
- **the new child column is not auto-indexed** → recommend a nonclustered index on it. SQL Server
  indexes the parent side of a foreign key, never the child, so the join scans until it is indexed
  (F11). See `../../_index/when-to-index/SKILL.md`.

## Prove it
Run the orphan probe FIRST: `SELECT COUNT(*) FROM child c LEFT JOIN parent p ON c.<fk> = p.<pk> WHERE
p.<pk> IS NULL`. Then publish under the strict gate: 0 orphans → the key lands trusted; any orphan →
the publish blocks with `Msg 547` and names the conflicting constraint, and the op changes. See
`../../prove-on-dacpac/SKILL.md` (publish loop) + `../../talk-to-local-sql/SKILL.md` (probe). Seed: the
clean Order→Status rows are the positive; a seeded orphan flips it to a blocked deploy and routes to
create-fk-orphan.

## The verdict (to the developer)
You asked to add the reference from Order to Status. On a copy of Dev, every Order already points at a
real Status — no orphans — so the foreign key adds and re-validates in one publish and ends trusted, in
a single release with nothing to reconcile first. One thing changes going forward: an insert or update
that points an Order at a Status that does not exist is now rejected. One recommendation: the StatusId
column is not indexed — SQL Server does not index the child side of a foreign key — so the Order → Status
join scans Order; a nonclustered index on StatusId is worth adding, in this PR or as a fast follow.

## The reasoning (in conversation)
A foreign key is the clearest case of the existing rows setting the shape, not the script: the same
`ADD CONSTRAINT` text lands in one release when every child has a parent, and blocks with `Msg 547` the
moment one orphan exists. So you read what will happen from the orphan count, never from the `.sql` —
which is why the probe runs before anything is classified. The declarative add always re-validates
(`WITH NOCHECK ADD` then `WITH CHECK CHECK`), so a clean add ends trusted on its own; an untrusted key
only comes from a hand-written `WITH NOCHECK` dodge. See `../../_index/constraint-is-a-claim/SKILL.md`.

## On the record
The pull request is an instance of the ten-section template in `../../author-pr/SKILL.md`; the worked
instance for this op — with the live messages — is `../../../sample-prs/create-fk-clean.md`. SHIP
terminal: **ONE RELEASE, trusted.** The fragment this operation contributes:

**Review & release**
- A dev lead reviews this: a new cross-table relationship changes what the database accepts, and the
  application is now rejected (error 547) when it writes a child with no parent.
- Ships as one release, applied in place — one declarative add that re-validates every existing child
  row in the same publish and ends trusted. No data is modified.
- Added scrutiny: none for a small table; at >1M child or parent rows the validation scan may block
  writes or run long — schedule a window.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows: every child points at a real parent
SELECT c.<fk> FROM child c LEFT JOIN parent p ON c.<fk> = p.<pk> WHERE p.<pk> IS NULL;

-- expect one row, is_not_trusted = 0: the foreign key landed trusted
SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_<child>_<parent>';
```

**Rollback**
Lossless: `ALTER TABLE <child> DROP CONSTRAINT FK_<child>_<parent>;`. No data was modified, so nothing
else is reversed.

**Not verified**
- Application impact: any insert or update that points a child at a parent that does not exist is now
  rejected with error 547; application-side validation is not confirmed here.
- Other environments: the orphan probe was proven on a copy of Dev only; QA, UAT, and Prod may hold
  orphans this copy cannot see — run the orphan query before promotion; if it finds rows, use
  create-fk-orphan.
- Production scale: on a large table the validation scan's duration and locking are not shown by the
  small copy.
