---
name: synonym
description: Use when the developer says "point this entity at a table in another database/server", "alias a table that lives elsewhere", "we moved Customer to the shared DB but keep the local name", "reference a table in the linked database" — a runtime-resolved alias to an external object. SSDT destination = a declarative CREATE SYNONYM whose target is NOT validated at publish time.
---

# Synonym (unvalidated-target / invisible-dependency trap)

> **Default (provisional — the data decides).** Mechanism 1 Pure Declarative, single-phase, Tier 1 for the synonym itself — but name the runtime-resolution gap.

## OutSystems phrasing
"point this entity at a table in another database/server", "alias a table that lives elsewhere", "we moved Customer to the shared DB but keep the local name".

## SSDT meaning
`CREATE SYNONYM dbo.Customer FOR OtherDb.dbo.Customer;`. A synonym is a **runtime-resolved alias** — it holds no data and its target is resolved when queried, not when created. SSDT publishes it declaratively. Never write ALTER.

## The named trap
The synonym's target is **not validated at publish time** — SSDT will happily create a synonym pointing at a database, table, or server that does not exist (or that the deploy account cannot see); the failure surfaces only at runtime, on the first query. A synonym also silently bypasses the dacpac's dependency tracking: SSDT does not know what is on the other side, so it cannot warn when that side changes. This is a single-op coupling (below the lift bar) — the gap lives here, not in an index.

## How it flips (the specifics only)
- synonym to an **in-model** object → **M1**, Tier 1.
- synonym to an **external** DB/server (the usual case) → M1 mechanically but **Tier 3**: the target is a cross-system dependency SSDT cannot see, so changes on the other side are invisible to the proving ground.

## Prove it
Preview the delta — a clean CREATE SYNONYM. Then prove the **runtime-resolution gap**: the synonym publishes clean even if the target is absent, so demonstrate that a query through the synonym fails at runtime when the target is missing — that is the thing the dacpac will not catch, and the developer must know it. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, a synonym (VIE-03) needs no local table — it targets an external DB and is proven by the runtime-resolution gap.

## Verdict to the developer
"The synonym publishes clean, but SSDT can't see what's on the other side of it — it won't tell you if the target table moves or disappears. That's a runtime dependency to track outside the proving ground."

## Teach it (the graduation)
A clean deploy proves the local half of a cross-system dependency and nothing about the remote half — whenever a change reaches outside the model's horizon (synonym, linked server, external feed), the proof is necessarily partial and the runtime gap must be named, not assumed away; the fail mode avoided is reading a green synonym deploy as proof the link works.
