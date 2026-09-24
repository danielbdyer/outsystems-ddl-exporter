# Operations — Columns, additive & lossless (Attributes) — FAMILY INDEX

> **This file is an INDEX.** The op specifics live in the per-op skills under
> `../op/<slug>/SKILL.md`; the shared reasoning lives in `../_index/`. Nothing here restates a guard
> or how a change flips.

The additive and lossless attribute operations. The developer thinks in **Entity Attributes**; in
SQL these are `ADD COLUMN` and `ALTER COLUMN` destinations where **every existing row already
satisfies the new shape** — a new nullable column, a loosening, a widening, a widening retype, a
rename that keeps its data, audit stamps. Because no existing row can conflict, each lands as
**one release, applied in place** — the data-loss guard (`BlockOnPossibleDataLoss = true`) never
fires. The two edges that carry weight anyway: a **rename** loses the column's data unless a
refactorlog entry travels with it (then the difference is `sp_rename`, not `DROP COLUMN` + `ADD`),
and **audit columns** made `NOT NULL` need every existing row to receive a value — cured by an
explicit default that stamps them as the columns land, or by a pre-deploy backfill when there is no
default. **Proving is classifying:** how a change ships is stated only after it is confirmed on a
disposable copy of Dev (`../prove-on-dacpac/SKILL.md`). The `ALTER` is never authored by hand — edit
the CREATE to the destination, and SSDT computes the difference.

## The ops (table of contents)

Every line names the op's **SHIP terminal** — the deployment shape the S5 sub-machine decides
(`../../THE_DECISION_TREE.md`), proven live on this branch and written up in the op's worked pull
request (`../../sample-prs/<op>.md`, an instance of the ten-section template `../author-pr/SKILL.md`).

| Op | Per-op skill | SHIP terminal · what it is / how it flips |
|---|---|---|
| add-optional | `../op/add-optional/SKILL.md` | **ONE RELEASE, in place.** A new nullable column — the difference is one `ADD [Col] <type> NULL`; existing rows take NULL, which the column already permits, so nothing can conflict and the add is never refused. The safest change in the catalog; the lightest look on this estate. |
| make-optional | `../op/make-optional/SKILL.md` | **ONE RELEASE, in place.** `NOT NULL` → `NULL` — the difference is one `ALTER COLUMN [Col] <type> NULL`; a loosening removes a rule, so no existing row can violate it and the deploy is never refused. The risk is downstream: a consumer that assumed the column was always filled now meets a NULL. That changes what the lead weighs, not how it ships. |
| widen | `../op/widen/SKILL.md` | **ONE RELEASE, in place.** Enlarge length/precision — the difference is one `ALTER COLUMN` to the wider type; every value already fits, so no data is read or rewritten (the value digest is identical before and after). The one coupling is the index-key byte limit: a widened column inside a non-clustered index key must not push it past ~1700 bytes. |
| retype-implicit | `../op/retype-implicit/SKILL.md` | **ONE RELEASE, in place.** A widening type change (`INT` → `BIGINT`, `VARCHAR` → `NVARCHAR`) — the difference is one `ALTER COLUMN` to the wider type; every value converts, so nothing is reshaped. Confirm the direction is a genuine widening: a narrowing or value-reshaping cast is refused on a populated table and is retype-explicit, not this op. |
| rename-attribute | `../op/rename-attribute/SKILL.md` | **ONE RELEASE, in place — only with the refactorlog entry.** With a `Rename Refactor` entry the difference is `EXEC sp_rename ... 'COLUMN'` (data and `object_id` preserved); without it the difference is `DROP COLUMN` + `ADD`, refused on a populated table (`Msg 50000`) and — if the gate is ever relaxed — every value in the column is lost. Read the difference; it must be `sp_rename`. Every caller of the old name must move. |
| audit-columns | `../op/audit-columns/SKILL.md` | **ONE RELEASE, in place** (nullable columns, or `NOT NULL` with an explicit default that stamps every existing row as the columns land); **ONE RELEASE with a pre-deploy backfill** for `NOT NULL` with no default on a populated table. A fresh column's `NOT NULL` block is cured by supplying the value — the default stamps the rows already there — which the existing-column tightening class can never allow. |

## Shared concerns for this family (the `_index` layer)

- `../_index/identity-and-refactorlog/SKILL.md` — governs **rename-attribute**: identity is separate
  from name; the refactorlog carries the column's data, so the difference must read `sp_rename`, not
  `DROP COLUMN` + `ADD`.
- `../_index/tightening-class/SKILL.md` — the boundary the **audit-columns** `NOT NULL` branch must
  not be conflated with: a fresh column's value-needed block is cured by a default; the existing-column
  row-presence guard (make-mandatory / narrow) is not.
- `../op/add-default/SKILL.md` — the proven clean shape behind the **audit-columns** `NOT NULL`-with-
  default branch: an explicit default stamps existing rows as the column lands.

## Handbook citation reminder

Handbook files are cited by FILENAME with a **+3 offset**: `13` = §16 (Operation Reference),
`14` = §17 (patterns), `15` = §18, `16` = §19 (anti-patterns).
