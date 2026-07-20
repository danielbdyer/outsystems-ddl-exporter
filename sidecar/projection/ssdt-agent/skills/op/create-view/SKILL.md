---
name: create-view
description: Use when the developer says "give me a view that joins Order and Customer", "an Advanced Query entity for active customers", "expose a read-only combined entity", "a saved query I can read like a table" — a new OutSystems view / Advanced Query. SSDT destination = a declarative CREATE VIEW with columns enumerated explicitly (never SELECT *).
---

# Create a view (SELECT * View trap)

> **⚠️ PRINCIPAL-ONLY — out of the developer catalog on purpose.** This team does not author views at
> the outset; if the org adopts views later, a **principal** authors them. At intake, name the
> request, say views are a principal's call, and route it up — `confirm-intent` routes "a view" to a
> principal, not to a developer. This skill stays as the principal's reference; the mechanics below
> are kept for when a principal picks it up.

> **Default (provisional — the data decides).** Ships as a single schema change, applied in place —
> no data is read or written. **A principal must review this: views are a principal-only concern for
> this team** (the technical change is otherwise additive and the running application is unaffected).
> Enumerate the columns explicitly rather than `SELECT *`.

## OutSystems phrasing
"give me a view that joins Order and Customer", "an Advanced Query entity for active customers", "expose a read-only combined entity".

## SSDT meaning
`CREATE VIEW dbo.SomeView AS SELECT … FROM …`. SSDT publishes it declaratively as CREATE/ALTER VIEW; a view holds no data of its own, so there is no `BlockOnPossibleDataLoss` concern. Never write ALTER.

## The named trap
**SELECT \* View** (handbook 16 = §19). `SELECT * FROM dbo.Order` does not freeze the column list; its resolved shape is **cached metadata** that only updates when the module is rebound. SSDT auto-rebinds dependents on publish (`sp_refreshsqlmodule`), so *through SSDT* a `SELECT *` view stays current — but a base-table change made **out of band** (a raw `ALTER` outside the dacpac) leaves the view bound to the OLD shape until someone runs `sp_refreshview`. So the view's contract lives *outside* its own .sql and can drift with **no reviewable diff**. Always enumerate columns explicitly; SSDT will not flag the `*`. This trap recurs only in create-view and compat-view — it is NOT lifted to an index; it lives here and in `../compat-view/SKILL.md`.

## How it flips (the specifics only)
- plain enumerated-column view → ships in place, no data read or written; any team member can review.
- view that downstream apps/reports/ETL depend on → ships the same way, but a dev lead should review: the dependency scope reaches other systems, so changing it later is a cross-system change.
- `SELECT *` view → ships the same way, but it carries a latent defect — fix the `*` before shipping, not a flip.
- indexed/materialized view → see `../indexed-view/SKILL.md`; the materialization changes how it ships.

## Prove it
Preview the Strict delta — a clean CREATE/ALTER VIEW, no rebuild, nothing blocked (a view holds no data). Proving the `SELECT *` trap needs care (verified on a disposable copy of Dev): a **pure SSDT publish auto-emits `EXECUTE sp_refreshsqlmodule`** for the dependent view, so a *through-model* base-column add makes the `SELECT *` view **stay correct** — it picks up the new column. That is not the trap; SSDT is keeping the view current. To prove the trap as a *silent defect*, add the base column via a **non-SSDT path** (a raw `ALTER TABLE` outside the dacpac — a hotfix by another team), then show the `SELECT *` view is still bound to the OLD column set until someone runs `sp_refreshview` manually. That out-of-band drift is where a `SELECT *` view goes stale. See `prove-on-dacpac`. On the sample, `dbo.vOrderSummary` ships an enumerated variant plus a documented `SELECT *` variant for the scratch-copy drift proof (VIE-01).

## The verdict (to the developer)
"Your view publishes clean — it holds no data, so there's nothing to lose on deploy. I enumerated the columns explicitly instead of `SELECT *`, so it won't silently change shape when the underlying entity changes. One thing worth checking: does anything outside the database read this view — a report, an ETL job, another app? If so, a later change to it becomes a cross-system change, and a dev lead should review."

## The reasoning (in conversation)
A view written as `SELECT *` has no shape of its own — it defers its contract to whatever the base table happens to hold, and a change to it can leave no visible diff. That is the hardest kind of change to catch in review. Enumerating the columns freezes the surface, so any drift shows up as a diff someone can actually read. The failure you're avoiding is a `SELECT *` view that quietly stops matching its base as the base evolves.

## On the record
The fragment this contributes to the pull request (feeds `../../author-pr/SKILL.md`):

**Review & release**
- A principal must review this: views are a principal-only concern for this team (out of the developer catalog on purpose — see the banner). The technical change is otherwise additive and the running application is unaffected; if downstream apps, reports, or ETL read the view, that dependency scope is a further reason it is a principal's call.
- Ships as a single schema change, applied in place. No data is read or written.
- Added scrutiny: none. A `SELECT *` definition is a latent defect to fix before shipping, not added scrutiny.

**Verification** — run in each environment after deployment
```sql
-- expect the intended enumerated columns, in order; a stale SELECT * binding would show the old set
SELECT name, column_id FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.vOrderSummary') ORDER BY column_id;
```

**Rollback**
The view holds no data. `DROP VIEW dbo.vOrderSummary` backs out a new view with no data loss; reverting an edited view restores the prior CREATE/ALTER VIEW definition, also lossless.

**Not verified**
- Downstream consumers. Apps, reports, and ETL that read the view are outside the dacpac; their behaviour against the enumerated column set is not verified here.
- Out-of-band drift. A raw `ALTER TABLE` on a base table outside SSDT can leave a `SELECT *` view bound to the old shape until `sp_refreshview` is run manually, and SSDT emits no diff for it — the enumerated column list is the guard.
