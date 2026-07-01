---
name: make-optional
description: Use when the developer says "make this attribute optional", "uncheck Mandatory", "let it be blank now", "it doesn't have to be filled anymore" — an existing column NOT NULL→NULL. A pure loosening; the risk is downstream, not at deploy.
---

# Make optional (NOT NULL → NULL)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1–2 (Tier 2 if consumers assume non-NULL). Loosening never vetoes.

## OutSystems phrasing
"make this attribute optional", "uncheck Mandatory", "let MiddleName be blank now".

## SSDT meaning
Change `NOT NULL` to `NULL`. SSDT emits `ALTER COLUMN [Col] <type> NULL`. A pure loosening — no
existing row can violate "allows NULL", so SQL Server never refuses it. Edit the CREATE; never
write `ALTER`.

## The named trap
No deploy-time trap. The risk is **downstream**: code, reports, and ETL that never expected a
NULL in this column may now break on one — an application/consumer concern, not an SSDT veto.
None material at the deploy layer.

## How it flips (the specifics only)
- any table state → **M1, single-phase** (loosening never vetoes)
- downstream consumers assume the column is always populated → consumer risk → **Tier 2**, flag the consumers (not a mechanism flip)
- CDC-enabled → nullability change still alters the capture-instance shape → **+1 Tier**, recreate (see `../../_index/cdc/SKILL.md`)

## Prove it
Strict publishes clean; delta is a single `ALTER COLUMN ... NULL`; no veto. The proof is really
about reminding the developer of the *downstream* NULL risk, since the publish itself cannot
fail. For the publish loop, see `../../prove-on-dacpac/SKILL.md`.

## Verdict to the developer
"Making it optional always publishes clean — loosening a rule never trips SSDT. Pure Declarative,
single-phase. The thing to watch is downstream: any report or code that assumed this is always
filled will now meet a NULL. Tier 2 for that reason."

## Teach it (the graduation)
The absence of a publish veto is not the absence of risk — a clean Mechanism 1 can still be Tier 2
because the danger lives where the engine cannot see it (the consumers). Ask "who *relies* on this
being non-NULL?" Fail mode avoided: assuming loosening is free and breaking a report that assumed
the column was always filled.
