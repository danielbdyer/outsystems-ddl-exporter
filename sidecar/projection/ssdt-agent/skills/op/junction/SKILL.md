---
name: junction
description: Use when the developer says "make this a many-to-many", "a Student can have many Courses and a Course many Students", "add a bridge entity", "add a join/link table" — an M:N bridge table with a composite PK over two FKs.
---

# Junction (M:N bridge table)

> **Default (provisional — the data decides).** Ships as a single schema change, applied in
> place: a new `CREATE TABLE` whose composite primary key spans two foreign key columns; no
> existing data is read or written. A dev lead must review this: it adds two cross-table
> relationships. Prove both sides carry no orphan pairs before classifying — if the bridge is
> seeded with pairs referencing missing parents, the publish is blocked and it routes to
> `../create-fk-orphan/SKILL.md`.

## OutSystems phrasing
"make this a many-to-many", "a Student can have many Courses and a Course many Students", "add a bridge entity".

## SSDT meaning
A new `CREATE TABLE` whose PK is the **composite of two FK columns**, each FK referencing one
parent's PK. It is a `create-entity` with two inbound dependencies and one composite key. Never
write `ALTER`.

## The named trap
Declaring the FKs when the parent rows the bridge will reference do not yet exist (orphans on
seed) — the **Forgotten FK Check** in disguise. That is the constraint-is-a-claim concern — see
`../../_index/constraint-is-a-claim/SKILL.md`. Forgetting the composite PK lets duplicate pairs
in. Do not re-derive the orphan/claim mechanics here.

## How it flips (the specifics only)
- brand-new empty bridge, both parents present → ships as a single schema change, applied in
  place; a dev lead reviews it because two cross-table relationships are added.
- bridge seeded with pairs referencing missing parents → the foreign-key validation blocks the
  publish → this becomes the orphan-reconcile path: route to `../create-fk-orphan/SKILL.md` (and
  `../../_index/constraint-is-a-claim/SKILL.md`), where it ships as a scripted change and a dev
  lead must review it because existing data is modified.
- either parent table is large → the foreign-key validation scans both parents → added scrutiny
  at >1M rows: the scan may block writes or run long, so schedule a window.

## Prove it
A Strict publish creates the bridge clean and is not blocked — proving every seeded pair has both
parents present. Author one orphan pair and watch SSDT block the publish, demonstrating the
failure mode the composite-PK + two-FK shape guards against. Probe: `LEFT JOIN` each foreign-key
column to its parent looking for NULL parents. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You asked to make this a many-to-many — a Student can have many Courses, a Course many Students.
In SQL that's a bridge table: a small table whose primary key is the two foreign keys together,
one pointing at each side. On a disposable copy of your data, SSDT just creates it — there's
nothing to transition, so nothing can conflict or be lost. I checked both sides and found no
orphan pairs, so the two foreign keys validate and the table lands clean. A dev lead should
review it, because it now ties two tables together with two relationships. The one thing worth
deciding is whether you're seeding any initial pairs — if so, every pair needs a real row on both
sides, or the create is blocked.

## The reasoning (in conversation)
The shape carries the whole guarantee: the composite primary key over the two foreign keys is what
makes this a real many-to-many. The primary key spanning both columns stops the same pair being
recorded twice, and the two foreign keys stop a pair from pointing at a Student or Course that
doesn't exist — the same orphan-check discipline as any single foreign key (see
`../../_index/constraint-is-a-claim/SKILL.md`). The mistake to avoid is treating "many-to-many" as
just two loose columns and seeding pairs before both parents exist — then the create is blocked at
deploy.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: two cross-table relationships are added.
- Ships as a single schema change, applied in place — one `CREATE TABLE` whose composite primary
  key spans two foreign key columns; no existing data is read or written.
- Added scrutiny: none for small parents; at >1M rows in either parent the foreign-key validation
  scans both and may block writes or run long — schedule a window.

**Verification** — run in each environment after deployment
```sql
-- expect 0 rows from each: every bridge pair points at a real parent on both sides
SELECT b.<fkA> FROM dbo.<bridge> b
LEFT JOIN dbo.<parentA> a ON a.<pk> = b.<fkA> WHERE a.<pk> IS NULL;
SELECT b.<fkB> FROM dbo.<bridge> b
LEFT JOIN dbo.<parentB> c ON c.<pk> = b.<fkB> WHERE c.<pk> IS NULL;

-- expect 0 rows: no duplicate pair exists — the composite primary key forbids it
SELECT b.<fkA>, b.<fkB>, COUNT(*) FROM dbo.<bridge> b
GROUP BY b.<fkA>, b.<fkB> HAVING COUNT(*) > 1;
```

**Rollback**
Remove the bridge's `CREATE TABLE` from the project and republish; SSDT emits `DROP TABLE
[dbo].[<bridge>];`. Lossless only while the bridge is unwritten — it is created empty; once the
application writes pairs into it, dropping the table discards them, and any seed pairs go with it.

**Not verified**
- Application impact — a brand-new bridge nothing yet reads or writes does not change existing
  behaviour; any application code that writes pairs is not exercised here, and once the table is
  live an inserted pair pointing at a missing parent is rejected (error 547), a duplicate pair by
  the composite primary key (@app-owner).
- Other environments — the orphan probe was proven on a disposable copy of Dev only; if the bridge
  ships with seed pairs, Test, UAT, and Prod may hold parent rows this copy cannot see — run the
  verification queries before promotion.
- Production scale — at >1M rows in either parent the foreign-key validation's duration and
  locking are not shown by the small copy.
- Reversibility — the forward create is proven; once pairs are written, dropping the bridge is
  lossy (see Rollback).
