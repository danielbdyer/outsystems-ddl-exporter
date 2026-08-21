# Operations — Columns (tightening) — FAMILY INDEX

> **This file is an INDEX.** The op specifics live in the per-op skills under `../op/<slug>/SKILL.md`;
> the shared reasoning lives in `../_index/`. The PR template every op fills is `../author-pr/SKILL.md`;
> each op's worked instance is `../../sample-prs/<slug>.md`. The shipping shape is decided by the
> **S5 SHIP sub-machine** in `../../THE_DECISION_TREE.md`, not chosen — the proofs are in
> `../../FINDINGS_AND_CHANGES.md`.

**Family framing.** These are the attribute changes that **remove capacity** from a populated column —
require a value, shrink it, retype it, or drop it. In SSDT each is one `CREATE` edit whose outcome the
data decides, and on this pipeline (Azure DevOps → Octopus) the publish always runs with the data-loss
guard `BlockOnPossibleDataLoss` on and **cannot** relax it for one deploy. So the governing question is
never the `.sql` text — it is *is the table empty?* On an empty table the guard is inert and the change
lands in one release; on a populated table the same edit is refused by a data-blind guard
(`IF EXISTS (SELECT TOP 1 1 FROM <t>) RAISERROR(…)`, which checks row presence, not the column's values)
and must be shaped so DacFx generates no data-loss step — the **two-release** pattern (R1 makes the
physical change in a pre-deploy with the model unchanged; R2 the model catches up as a no-op). **Proving
is classifying** (`../prove-on-dacpac/SKILL.md`); the `ALTER` is never authored by hand.

## The ops (table of contents)

| Op | Per-op skill · worked PR | SHIP terminal | What it is / how it flips |
|---|---|---|---|
| add-mandatory | `../op/add-mandatory/SKILL.md` · `../../sample-prs/add-mandatory.md` | **ONE-RELEASE** with a default (TWO-RELEASE fallback if no default is acceptable) | A new `NOT NULL` column. With a `DEFAULT`, DacFx stamps every existing row in one clean `ALTER … ADD` — no data-loss step. With no default on a populated table the publish is refused (no value for the existing rows). |
| make-mandatory | `../op/make-mandatory/SKILL.md` · `../../sample-prs/make-mandatory.md` | **TWO-RELEASE** on a populated table (ONE-RELEASE when empty) | An existing column `NULL → NOT NULL`. The row-presence guard blocks it even with zero NULLs; R1 backfills + tightens in a pre-deploy with the model lagging, R2 the model catches up. The seed that feeds the column stops writing NULL in the same change set (else `Msg 515`). Proven F7. |
| narrow | `../op/narrow/SKILL.md` · `../../sample-prs/narrow.md` | **TWO-RELEASE** on a populated table (ONE-RELEASE when empty) | Shrink length/precision. The row-presence guard blocks it even when every value fits; R1 reconciles the over-length values + narrows in a pre-deploy with the model lagging, R2 the model catches up. The seed stops writing over-length values in the same change set (else `Msg 2628`). Proven F1–F4. |
| retype-explicit | `../op/retype-explicit/SKILL.md` · `../../sample-prs/retype-explicit.md` | **MULTI-PHASE** (several releases); the drop-old-column leg a TWO-RELEASE | A lossy/value-reshaping cast. A bare single-step type change is refused. Add a new column of the target type (additive, one release), convert with `TRY_CONVERT`, settle the non-convertible rows, move the app, then drop the old column — that drop leg is `delete-attribute`'s two-release. |
| delete-attribute | `../op/delete-attribute/SKILL.md` · `../../sample-prs/delete-attribute.md` | **TWO-RELEASE** on a populated column (ONE-RELEASE when empty/unused) | Drop a column. The row-presence guard blocks the drop of a populated column. After the app has stopped reading it, R1 drops the column's default constraint then the column in a pre-deploy with the model lagging (published once — a re-publish re-adds it), R2 the model drops it as a no-op. The seed stops writing the column in the same change set (else `Msg 207`). Advances FINDINGS Part 5. |

## The shared truth (why the whole family flips the same way)

The block is **row-presence, not value-shape**. `BlockOnPossibleDataLoss` guards the tightening `ALTER`
(or `DROP COLUMN`) with `IF EXISTS (SELECT TOP 1 1 FROM <t>) RAISERROR(…)`, above the statement, so a
populated table is refused even when every value already fits, holds zero NULLs, or the column is fully
populated. DacFx computes the whole deploy script once, up front, so a same-release pre-deploy backfill
cannot satisfy it — the tightening and the model change must be split across two releases (F2: combining
them blocks **and** half-applies). A pre-deploy side effect survives a failed deploy (F6), so every
pre-deploy step is idempotent, and R1 is published **once** — a re-publish of R1, with the model still
declaring the old shape, drifts the database back (F3). The post-deploy seed that feeds the tightened
column is part of the change set — it fails after the tightening lands if it still writes the old value.

- `../_index/tightening-class/SKILL.md` — the data-blind row-presence guard; the empty-vs-populated
  ladder; why classifying from the `.sql` text or a clean value probe reads green while the change is
  still blocked. Governs **add-mandatory, make-mandatory, narrow, delete-attribute**.
- `../_index/multi-phase/SKILL.md` — additive → cutover → subtractive coexistence and the totality
  proof that licenses a drop. Governs **retype-explicit, delete-attribute**.

## Handbook citation reminder

Handbook files are cited by FILENAME with a **+3 offset**: `13` = §16 (Operation Reference),
`14` = §17 (patterns), `15` = §18, `16` = §19 (anti-patterns).
