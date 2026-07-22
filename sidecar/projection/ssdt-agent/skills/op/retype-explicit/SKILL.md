---
name: retype-explicit
description: Use when the developer says "change the text field to a date", "store it as a number now", "make this Text into an Integer", "convert this column's type" where not every value converts — a value-reshaping, lossy cast.
---

# Retype explicit (value-reshaping conversion)

> **Default (provisional — the data decides; prove before you classify).** Ships across multiple
> releases (multiple pull requests): add a new column of the target type, convert the values that
> convert with `TRY_CONVERT`, handle the rows that do not, then drop the old column and rename the
> new one in — the old and new types coexist while the application migrates. A dev lead must review
> this: existing data is reshaped and the old column is dropped; if non-convertible rows are dropped
> rather than reconciled, a principal must review it, because data is removed and cannot be undone.
> Count the non-convertible rows before promising anything.

## OutSystems phrasing
"change the Text attribute to a Date", "make this an Integer", "store it as a number now".

## SSDT meaning
Change the column's data type in a value-reshaping direction (`VARCHAR`→`DATE`,
`INT`→`UNIQUEIDENTIFIER`, `DATETIME`→`DATE`, any cast where not all values convert). This cannot
be a safe single `ALTER COLUMN`. Edit the CREATE toward the destination; the staging is owned
manually — never write a bare `ALTER`.

> **The application-side cutover is part of this change.** The Integration Studio / Service Studio
> republish order, the two sequencing rules (the app reads the new shape only after the schema
> release; the old shape drops only after the app stops writing it), and what the pull request
> names under Not verified are owned by `../../_index/multi-phase/SKILL.md` — plan no phase
> without it.

## The named trap
SSDT may *attempt* a single-step `ALTER COLUMN` on an explicit conversion that then fails (or
truncates) mid-deploy — the multi-phase shape must be owned manually; the engine will not.
Old and new types coexisting is the multi-phase concern — see `../../_index/multi-phase/SKILL.md`;
the add-new → convert → swap tail follows `../rename-attribute/SKILL.md` rules. Do not re-derive
coexistence here.

## How it flips (the specifics only)
- explicit/narrowing → ships across multiple releases (multiple pull requests): add a new column of
  the target type → `UPDATE ... SET new = TRY_CONVERT(<type>, old)` → app transitions → drop old,
  rename new in — existing data is reshaped, so a dev lead must review it (see
  `../../_index/multi-phase/SKILL.md`).
- any value fails `TRY_CONVERT` (returns NULL) → those rows need explicit handling → stays multi-PR;
  drop the non-convertible rows rather than reconcile them and a principal must review it, because
  data is removed and cannot be undone.
- direction is actually widening/lossless → **wrong op** → `../retype-implicit/SKILL.md`
- >1M rows → added scrutiny: at production row counts the convert-and-swap may block writes or run
  long — schedule a window.

## Prove it
Run a `TRY_CONVERT` probe over the real data — count how many rows return NULL (would not
convert) — and prove the add-new → `TRY_CONVERT` → drop-old sequence on a disposable copy of Dev.
If you ever see SSDT emit a bare `ALTER COLUMN` for an explicit conversion, stop and force the
multi-phase path. For the publish loop, see `../../prove-on-dacpac/SKILL.md`; probes,
`../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to store this column as a Date instead of Text, and the catch is that not every value
parses as a date. On a disposable copy of Dev I ran `TRY_CONVERT` over your actual data: 12 rows
don't convert. So this can't be one clean change — it stages across more than one release so the
running app keeps working: add a new Date column, convert the values that convert, deal with the 12
that don't, then swap the new column in and drop the old. Those 12 are the real question for you —
should they be corrected to real dates before the cutover, or is it acceptable for them to land as
NULL? Correcting them keeps this a reshape a dev lead can sign off; letting them drop means data lost
for good, which a principal should review.

## The reasoning (in conversation)
An explicit conversion earns its staging for two reasons, and only the data shows you the first: not
every value converts, so the rows that fail `TRY_CONVERT` have to be found and decided on before
anything is promised. The second holds even when every value does convert — the old and new types
have to coexist while the application moves from one to the other, which is why it can't land in a
single release (`../../_index/multi-phase/SKILL.md`). The failure this avoids is letting SSDT attempt
a bare single-step `ALTER COLUMN` that fails or truncates mid-deploy; counting the non-convertible
rows first is what turns "change the type" into a plan instead of a gamble.

## On the record
The fragment this op contributes to the pull request (`../../author-pr/SKILL.md`).

**Review & release**
- A dev lead must review this: existing data is reshaped — values are converted into a new column of
  the target type and the old column is dropped.
- Ships across multiple releases (multiple pull requests): add a new column of the target type,
  convert the convertible values with `TRY_CONVERT`, handle the non-convertible rows, then drop the
  old column and rename the new one in — the old and new types coexist while the application
  migrates, and the conversion cannot be expressed as a table definition.
- When non-convertible rows are dropped rather than reconciled: a principal must review this — data
  is removed and the removal cannot be undone.
- Added scrutiny, when it applies: `Added scrutiny: at production row counts the convert-and-swap
  may block writes or run long — schedule a window.`

**Verification** — run in each environment after deployment:
```sql
-- expect 0 rows: every value in the source column converts to the target type
-- (a returned row would become NULL on convert — reconcile it before the cutover)
SELECT <key>, <col> FROM <t> WHERE <col> IS NOT NULL AND TRY_CONVERT(<type>, <col>) IS NULL;
```
Before each promotion the generated delta must read as the staged add-new / `TRY_CONVERT` /
drop-old sequence, never a bare `ALTER COLUMN` on the original column — a single-step ALTER on an
explicit conversion fails or truncates mid-deploy.

**Rollback** — before the old column is dropped, backing out is lossless: drop the new column and
keep reading the old one, which still holds its original values. Once the old column is dropped its
values live only in the new column as the converted form; any row reconciled to convert, and any
non-convertible row allowed to land as NULL, is not recoverable from the schema — the pre-conversion
values must be preserved (a backup, or the coexisting old column) until the drop is confirmed
durable. Not auto-reversed.

**Not verified**
- Application impact — every read and write path still using the old type breaks once the column is
  swapped; that every caller has moved to the new type is not confirmed here — @app-owner confirms
  it.
- Other environments — Test, UAT, and Prod may hold values that convert differently, or more
  non-convertible rows than this copy; run the `TRY_CONVERT` probe in each environment before the
  convert phase.
- Production scale / timing — the convert-and-swap is exercised at seed scale only; blocking and
  duration at >1M rows are not shown by the disposable copy.
- Reversibility — only the forward conversion is exercised; restoring the original type and values
  after the old column is dropped is not proven, and any non-convertible row that landed as NULL
  cannot be restored.
