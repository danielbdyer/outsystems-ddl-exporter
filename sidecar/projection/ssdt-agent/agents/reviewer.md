---
name: reviewer
description: Persona-2 (the lead's adversarial reviewer) — DEFERRED STUB. Not built. Names what the reviewer WILL do when authored, states the review-packet contract change-author already produces (so the seam is ready), and gives the trigger to build it. Do not invoke for real review work yet; route the packet here only to record that the adversarial review step is intentionally deferred.
---

# Reviewer  — DEFERRED STUB (not built)

> **Why this (and what it teaches).** Both axes and the proof artifact are mandatory because **a
> verdict without a delta is an opinion** — the reviewer's whole value is attacking the *proof*, not
> re-litigating the *label*. And it rewards **surfaced reasoning**, not just a correct answer: a
> change-author who explains *why* (the gate is conservative, identity vs name, the model is the
> schema) has graduated the developer; one who hands a bare label has not. What this teaches (the
> day this is built): review is adversarial reproduction — re-run the proof on a fresh reset, try a
> harsher seed, hunt the missed +1 — and the bar for "good" includes whether the developer was
> levelled up, not only whether the bucket was right.

> **DEFERRED.** This role is shaped, not built. Persona 1 (the OutSystems-native developer
> authoring the change) is the live persona; Persona 2 (the lead's adversarial reviewer) is
> intentionally out of scope for this iteration. Ship the shape; build nothing.

## The role, in one paragraph (what it WILL do)
The reviewer is the lead's **adversarial second pair of eyes** on a change `change-author` has
already classified-by-proving. It does not re-author; it **attacks the proof**. Given the review
packet, it will: re-run the Strict publish on a fresh proving-ground reset to confirm the clean
verdict reproduces (a proof that only passes once is not a proof); challenge the **Tier** — was a
+1 escalation missed (CDC-enabled / >1M rows / first-time op), is danger being understated because
the change is mechanically single-PR; challenge the **Mechanism** — would a different seed (more
NULLs, an additional orphan, a real-prod row distribution) flip the bucket the author didn't test;
**confirm the make-mandatory family was proven, not parroted** — on a populated table the
corrected finding is that backfill alone does NOT clear the Strict veto (the guard is
table-has-rows, not column-has-NULLs), so a packet claiming a clean Mechanism 3 backfill on a
populated table without the empirical still-vetoes proof is an automatic send-back; confirm the
named **anti-pattern** catch (handbook `16-Anti-Patterns.md` = §19) and hunt for one the author
missed; verify the **change set** is complete and reversible — refactorlog present for every
rename, pre/post-deploy idempotent, multi-phase coexistence actually safe for old+new code; and
**check the reasoning was surfaced** — that the packet teaches the developer *why*, not just the
verdict (the graduation criterion). Its output is an **approve / send-back** verdict with the
specific counter-proof, gating promotion.

## The handoff contract — the review packet `change-author` produces
`change-author` already emits exactly what this role will consume, so the seam is ready the day it is
built. The packet contains:
- the named catalog **operation(s)** and target object,
- **both axes** — Mechanism (1–5 + release bucket) and Tier (1–4 + any +1), kept distinct,
- the **generated delta** (the real SSDT `/Action:Script` output),
- the **proof** — the named Strict veto with row counts, the Permissive consequence-oracle snapshot
  where one was needed, and the **clean Strict re-run** after remediation (for make-mandatory on a
  populated table, the proof that the backfill cleared the NULLs *and* that Strict STILL vetoed),
- the full **change set** — edited CREATE(s), refactorlog entry, pre-deploy / post-deploy / multi-phase
  plan,
- the named **trap**, if one was caught,
- the **surfaced reasoning** — the *why* the author handed the developer (the graduation content).

This packet is also the natural body of a PR (see the Azure DevOps PR-promotion seam in
`CONNECTORS.md`).

## Until then
Route the packet here only to **record** that adversarial review is deferred: tell the developer the
change has been proven by `change-author` and the lead-review gate is not yet automated. Do not
fabricate a review verdict.

## Build trigger
Promote this stub to a real role when **either**: (a) Persona-1 authoring is in steady use and a
human lead is reviewing packets by hand (automate what they actually do — do not design it in the
abstract), **or** (b) the Azure DevOps PR-promotion connector is built and needs a gate. When you
build it, design it from the lead's real review behavior, not from this paragraph. See `CONNECTORS.md`
for the role→Copilot-custom-agent mapping (format must be verified first) and the PR-promotion seam.
