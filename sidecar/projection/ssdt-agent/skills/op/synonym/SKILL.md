---
name: synonym
description: Use when the developer says "point this entity at a table in another database/server", "alias a table that lives elsewhere", "we moved Customer to the shared DB but keep the local name", "reference a table in the linked database" — a runtime-resolved alias to an external object. SSDT destination = a declarative CREATE SYNONYM whose target is NOT validated at publish time.
---

# Synonym (unvalidated-target / invisible-dependency trap)

> **Default (provisional — the data decides).** Ships as a single schema change, applied in place —
> a declarative `CREATE SYNONYM`, no data read or written. A synonym to an in-model object is
> additive and the running application is unaffected, so any team member can review it. A synonym to
> an external database or server — the usual case — introduces a cross-system dependency SSDT cannot
> see, so a dev lead should review it. Prove before you classify, and name the runtime-resolution gap
> either way.

## OutSystems phrasing
"point this entity at a table in another database/server", "alias a table that lives elsewhere", "we moved Customer to the shared DB but keep the local name".

## SSDT meaning
`CREATE SYNONYM dbo.Customer FOR OtherDb.dbo.Customer;`. A synonym is a **runtime-resolved alias** — it holds no data and its target is resolved when queried, not when created. SSDT publishes it declaratively. Never write ALTER.

## The named trap
The synonym's target is **not validated at publish time** — SSDT creates the synonym without error even when the target database, table, or server does not exist (or the deploy account cannot see it); the failure surfaces only at runtime, on the first query. A synonym also bypasses the dacpac's dependency tracking silently: SSDT does not know what is on the other side, so it cannot warn when that side changes. This is a single-object coupling, not lifted to an index — the gap lives here, not in an index.

## How it flips (the specifics only)
- synonym to an **in-model** object → ships as a single schema change, applied in place, no data read or written; any team member can review — additive, the running application unaffected.
- synonym to an **external** database or server (the usual case) → ships the same way, one declarative statement in place, but a dev lead should review: the target is a cross-system dependency SSDT cannot see, so changes on the other side stay invisible to the disposable copy.

## Prove it
Preview the delta — a clean CREATE SYNONYM. Then prove the **runtime-resolution gap**: the synonym publishes clean even if the target is absent, so demonstrate that a query through the synonym fails at runtime when the target is missing — that is the thing the dacpac will not catch, and it must be named for the developer. See `prove-on-dacpac` / `talk-to-local-sql`. On the sample, a synonym (VIE-03) needs no local table — it targets an external DB and is proven by the runtime-resolution gap.

## The verdict (to the developer)
The synonym publishes clean, but SSDT can't see what's on the other side of it — it won't tell you if the target table moves or disappears. That's a runtime dependency you'll need to track outside the disposable copy: a green deploy here proves the synonym was created, not that anything answers on the other end. One thing worth pinning down: is that target already in place in the environments this ships to? That's the check the disposable copy can't do for you.

## The reasoning (in conversation)
A clean deploy proves the local half of a cross-system dependency and nothing about the remote half. Whenever a change reaches past what the dacpac can model — a synonym, a linked server, an external feed — the proof you can get is necessarily partial, so the runtime gap has to be named rather than assumed away. The failure to avoid is reading a green synonym deploy as proof the link works: it only shows the synonym was created, not that the target resolves.

## On the record
The fragment this contributes to the pull request (feeds `../../author-pr/SKILL.md`):

**Review & release**
- A dev lead should review this: the synonym targets an object in another database or server that
  the dacpac cannot see. A synonym to an in-model object is additive and the application is
  unaffected — any team member can review that one.
- Ships as a single schema change, applied in place — a declarative `CREATE SYNONYM`. No data is read
  or written.
- Added scrutiny: the target is not validated at publish time and its changes are invisible to the
  dacpac's dependency tracking — a runtime dependency tracked outside the disposable copy.

**Verification** — run in each environment after deployment
```sql
-- expect 1 row: the synonym exists and points at the intended target
SELECT name, base_object_name FROM sys.synonyms WHERE name = 'Customer';

-- expect success (no error): the target actually resolves in THIS environment
SELECT TOP 1 1 FROM dbo.Customer;
```

**Rollback**
`DROP SYNONYM dbo.Customer;` — lossless; a synonym holds no data of its own. Backing it out removes
the alias only, and the target object in the other database or server is untouched.

**Not verified**
- The remote half. Whether the target object exists, is reachable, and matches the expected shape is
  not validated at publish time — SSDT creates the synonym regardless, and the target resolves or
  fails only at runtime.
- Other environments. The target may exist in Dev but be absent, renamed, or pointed at a different
  database in Test, UAT, or Prod; the disposable copy cannot know. Run the verification query in each
  environment before promotion.
- Application impact. Any query through the synonym fails at runtime — not at publish — if the target
  is missing or its schema has drifted; the owner of the target system confirms it stays in sync.
