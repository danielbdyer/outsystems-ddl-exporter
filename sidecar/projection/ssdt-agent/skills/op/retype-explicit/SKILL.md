---
name: retype-explicit
description: Use when the developer says "change the text field to a date", "store it as a number now", "make this Text into an Integer", "convert this column's type" where not every value converts — a value-reshaping, lossy cast.
---

# Retype explicit (value-reshaping conversion)

> **Default (provisional — the data decides).** Mechanism 5 Multi-Phase, multi-PR, Tier 3–4 — not all values convert, and old and new types must coexist while you migrate.

## OutSystems phrasing
"change the Text attribute to a Date", "make this an Integer", "store it as a number now".

## SSDT meaning
Change the column's data type in a value-reshaping direction (`VARCHAR`→`DATE`,
`INT`→`UNIQUEIDENTIFIER`, `DATETIME`→`DATE`, any cast where not all values convert). This cannot
be a safe single `ALTER COLUMN`. Edit the CREATE toward the destination, but you own the staging;
never write a bare `ALTER`.

## The named trap
SSDT may *attempt* a single-step `ALTER COLUMN` on an explicit conversion that then fails (or
truncates) mid-deploy — you must **own the multi-phase shape manually**; the engine will not.
Old and new types coexisting is the multi-phase concern — see `../../_index/multi-phase/SKILL.md`;
the add-new → convert → swap tail follows `../rename-attribute/SKILL.md` rules. Do not re-derive
coexistence here.

## How it flips (the specifics only)
- explicit/narrowing → **M5 Multi-Phase, multi-PR**: add a new column of the target type → `UPDATE ... SET new = TRY_CONVERT(<type>, old)` → app transitions → drop old, rename new in. Tier 3–4. (See `../../_index/multi-phase/SKILL.md`.)
- any value fails `TRY_CONVERT` (returns NULL) → those rows need explicit handling → stays multi-PR, escalate Tier
- direction is actually widening/lossless → **wrong op** → `../retype-implicit/SKILL.md`
- CDC-enabled / >1M rows → **+1 Tier** (see `../../_index/cdc/SKILL.md`)

## Prove it
Prove a `TRY_CONVERT` probe over the real data — count how many rows return NULL (would not
convert) — and prove the add-new → `TRY_CONVERT` → drop-old sequence on the throwaway DB. If you
ever see SSDT emit a bare `ALTER COLUMN` for an explicit conversion, stop and force the
multi-phase path. For the publish loop, see `../../prove-on-dacpac/SKILL.md`; probes,
`../../talk-to-local-sql/SKILL.md`.

## Verdict to the developer
"Text→Date is value-reshaping: not every value parses. I ran TRY_CONVERT over your data — 12 rows
don't convert. So it's Multi-Phase across releases: add the new column, convert what's
convertible, fix the 12, then swap. Tier 3."

## Teach it (the graduation)
An explicit conversion needs proof because not all values convert, and old and new types must
coexist while you migrate (see `../../_index/multi-phase/SKILL.md`). Fail mode avoided: letting
SSDT attempt a bare single-step ALTER that fails mid-deploy — count the non-convertible rows
before you promise anything.
