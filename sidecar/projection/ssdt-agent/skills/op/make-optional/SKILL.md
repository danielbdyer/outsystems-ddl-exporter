---
name: make-optional
description: Use when the developer says "make this attribute optional", "uncheck Mandatory", "let it be blank now", "it doesn't have to be filled anymore" — an existing column NOT NULL→NULL. A pure loosening; the risk is downstream, not at deploy.
---

# Make optional (NOT NULL → NULL)

> **Default (provisional — the data decides).** Ships as a single schema change, applied in
> place — no data is read or written. Any team member can review it when nothing downstream
> assumes the column is always populated; a dev lead or an experienced developer should review it
> when consumers must change to tolerate a NULL. A loosening never blocks the deployment.

## OutSystems phrasing
"make this attribute optional", "uncheck Mandatory", "let MiddleName be blank now".

## SSDT meaning
Change `NOT NULL` to `NULL`. SSDT emits `ALTER COLUMN [Col] <type> NULL`. A pure loosening — no
existing row can violate "allows NULL", so SQL Server never refuses it. Edit the CREATE; never
write `ALTER`.

## The named trap
No deploy-time trap. The risk is **downstream**: code, reports, and ETL that never expected a
NULL in this column may now break on one — an application/consumer concern, not something the
deployment blocks. None material at the deploy layer.

## How it flips (the specifics only)
- any table state → ships in place as a single schema change; a loosening is never refused
- downstream consumers assume the column is always populated → the running application must
  change to tolerate a NULL, so a dev lead or an experienced developer should review — flag the
  consumers (this changes who reviews, not how it ships)
- CDC-enabled → the nullability change still alters the capture-instance shape → added scrutiny:
  the capture instance is frozen to the table's current columns and must be recreated (see
  `../../_index/cdc/SKILL.md`)

## Prove it
Strict publishes clean; the delta is a single `ALTER COLUMN ... NULL`; nothing is refused. The
publish itself cannot fail, so the proof exists to surface the *downstream* NULL risk rather than
a deploy-time one. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## The verdict (to the developer)
You're loosening the rule, so this always publishes clean — no existing row can violate "allows
NULL", and SSDT never refuses a loosening. The thing to watch is downstream: any report, query,
or code path that assumed this column is always filled will now meet a NULL and can break on it,
outside the deploy where the engine cannot see it. So the one thing worth pinning down before
this ships: who relies on this column always being populated?

## The reasoning (in conversation)
A clean publish is not the same as no risk. This change applies in place, touches no data, and is
never refused — and it can still need a careful reviewer, because the danger lives where the
engine cannot see it: in the consumers. The clean publish proves the schema transition is safe;
it tells you nothing about the code that reads the column. That is why the question to ask is
"who *relies* on this being non-NULL?" The failure mode to avoid: assuming a loosening is free,
and breaking a report that quietly assumed the column was always filled.

## On the record
The fragment this operation contributes to the pull request (`../../author-pr/SKILL.md`), in the
record register:

**Review & release**
- Ships as a single schema change, applied in place. No data is read or written.
- Any team member can review this: the column is loosened and the running application is
  unaffected. If a downstream consumer assumes the column is always populated, a dev lead or an
  experienced developer should review this instead — the running application must change to
  tolerate a NULL.
- Added scrutiny, only when the table is CDC-tracked: this table feeds a change-data-capture
  stream, so the capture instance is frozen to the table's current columns and must be recreated
  (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect is_nullable = 1: the column now permits NULL
SELECT c.name, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.<table>') AND c.name = '<column>';
```

**Rollback**
The loosening writes no data, so there is nothing to restore on the data side. Reversing the
schema is a re-tightening to NOT NULL — a separate make-mandatory change, not an automatic
reversal of this one: it is guarded while the table holds rows and is not lossless once any NULL
has been written (see `../make-mandatory/SKILL.md`).

**Not verified**
- Application impact — any report, query, or code path that assumed this column is never NULL
  will now meet one; which consumers depend on it is not confirmed by the publish. @app-owner.
- Other environments — whether Test, UAT, or Prod already hold NULLs, or whether downstream jobs
  there tolerate them, is not known from a disposable copy.
- Reversibility — only the forward loosening is proven. Re-tightening is a make-mandatory change
  with its own row-presence guard and is not exercised here.
