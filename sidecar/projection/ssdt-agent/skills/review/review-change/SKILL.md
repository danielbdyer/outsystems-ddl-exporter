---
name: review-change
description: The review CONDUCTOR — the single entry for "review this change". Use when the lead's adversarial reviewer (Persona 2) is handed a change-author review packet (both axes + generated delta + proof + change set + named trap + surfaced reasoning) and must AUDIT it rather than re-author it. Picks a PROTOCOL identity (unique PG_<id>_<rand> DB + scratch copy), REPRODUCES the author's claimed Strict/Permissive outcome on that isolated DB, then dispatches blast-radius -> adversary -> verdict in order and returns the four-level disposition. A claim that will not reproduce is an automatic HAND-BACK/REFUSE. Reuses PROTOCOL wholesale, re-runs prove-on-dacpac's existing loop, and only re-runs classify-mechanism when a claim fails to reproduce.
---

# Review a change (the conductor)

> **Why this (and what it teaches).** A verdict without a reproduced delta is an **opinion**. The
> author already proved the change once; your value is not to believe them — it is to **make the
> same proof pass for you** on a fresh, isolated DB, then attack the seams the author's seed was too
> friendly to expose. You audit the *proof*, never re-litigate the *label*. What this teaches the
> lead: a BLESS you can trust is one where the reviewer re-ran the veto, not one where it read the
> claim.

You are the conductor of **classify-by-proving in scrutineer posture**. The change-author already
ran the loop and handed a **review packet**. You do **not** re-author the change or re-derive its
axes from scratch. You **reproduce** the author's proof on your own throwaway DB, then dispatch three
review skills in a fixed order, and return one of four verdicts. This skill owns the *sequence* and
the *reproduce step*; the three siblings own scope, attack, and judgment.

## Your input — the review packet (the audit target)

The packet `change-author` emits (its **Handoff** section is the producer). Every field is a
**claim you must discharge**, not a fact you inherit:

| Packet field | The claim it makes | Becomes the proof obligation |
|---|---|---|
| operation(s) + target object | "this is op X on object Y" | the op-slug the adversary and blast-radius load |
| **both axes** — Mechanism (1–5 + bucket) + Tier (1–4 + any +1) | "the how and the danger are these" | reproduce must yield the same delta/veto; Tier +1 must survive blast-radius |
| **generated delta** (`/Action:Script`) | "SSDT would run exactly this" | re-run `/Action:Script` on your DB; the delta must match in kind (sp_rename vs DROP+CREATE, the guard placement) |
| **proof** — named Strict veto + row counts + clean Strict re-run after remedy | "Strict vetoed for N rows, and my remedy makes it pass clean" | **re-run both** — the veto AND the clean re-run — on your DB. A proof that passed once for the author must pass for you |
| full **change set** — CREATE(s) + refactorlog + pre/post-deploy + multi-phase plan | "the recipe is complete and reversible" | build it on your DB; missing refactorlog / unguarded MERGE / NOCHECK-left-untrusted are auto-findings |
| named **trap**, if caught | "the anti-pattern is X (or none)" | the adversary hunts the trap the author *missed*, not just re-confirms the one caught |
| **surfaced reasoning** | "the why is drawn from `_index/<concern>`" | graded by the rubric dimensions (see `skills/review/verdict`) — right label + wrong-source why is a downgrade signal, not a BLESS |

"**Count every crossing**": each row above is one obligation. You discharge it or you reject it. An
un-discharged obligation cannot ride inside a BLESS.

## The audit sequence (fixed order — do not reorder)

**REPRODUCE -> SCOPE -> ATTACK -> JUDGE.** Scope before attack (you cannot bless past a cascade you
never enumerated); attack before judge (a finding may downgrade the level).

### 1. Pick your isolated identity — PROTOCOL, wholesale

Do **not** restate the isolation mechanics — they are owned by `self-test/PROTOCOL.md` and you obey
them verbatim: a unique `PG_<reviewId>_<rand>` DB, a scratch copy of the proving ground, every
`sqlpackage` call carrying `/TargetDatabaseName`, and an **unconditional teardown** on exit (drop
DB + `rm -rf` scratch). Resolve `DB`/`SCRATCH` once, reuse the literal values. For a **CDC** packet,
serialize per PROTOCOL §8 — the review runs no faster than the author's did. Nothing about being a
*reviewer* relaxes the isolation invariants; you are one more executor in the fleet.

### 2. REPRODUCE the author's claimed outcome (the spine)

Re-run the author's proof on **your** DB using the **existing** `prove-on-dacpac` loop — you do not
re-scaffold it, you re-run it:

- Copy the packet's change set into `$SCRATCH` (the edited CREATE(s), the refactorlog, pre/post-deploy).
- Build the dacpac, `/Action:Script` -> read **your** `delta.sql`, compare its *kind* to the packet's.
- Publish **Strict** to `$DB`. If the packet claimed a **veto**, your Strict run must veto the same
  way (same guard, comparable row count). If the packet claimed **clean** (Mechanism 1), your Strict
  run must publish clean.
- If the packet claimed a **remedy that re-passes Strict clean**, apply the remedy on `$SCRATCH` and
  confirm your Strict re-run is clean too.

**Reproduce is a gate, not a formality:**

- **Reproduces as claimed** -> the obligation is discharged; proceed to scope + attack.
- **Does NOT reproduce** (packet said clean, your Strict vetoes; packet said sp_rename, your delta
  is DROP+CREATE; the claimed clean re-run still vetoes) -> the claim is **false as stated**. This is
  an automatic **HAND-BACK** (backstop) or **REFUSE** — you do not silently fix it and bless. Only
  *here*, when a claim fails to reproduce, do you re-run `classify-mechanism` to establish what the
  engine *actually* forces — that becomes the corrected finding you hand back.

> The one packet class where a **persisting veto is the correct reproduction, not a failure**:
> make-mandatory on a populated table (the guard is table-has-rows). Reproduce = backfill to 0 NULLs
> on your DB, re-run Strict, prove it **STILL** vetoes. Owned by `skills/_index/tightening-class`;
> the adversary drives this challenge (`skills/review/adversary`, challenge 2). A packet claiming a
> "clean M3 after backfill" on a populated table that you reproduce to a still-veto is a caught
> defect, not a reproduce miss.

### 3. SCOPE — dispatch `skills/review/blast-radius`

Enumerate the dependency closure (FKs in/out, views, procs, indexes, CDC capture instances,
external/ETL) and count affected rows on your DB. **No verdict may exceed this scope.** The blast
map it produces is what a REFUSE-ESCALATE carries to the lead.

### 4. ATTACK — dispatch `skills/review/adversary`

Wield the two named moves (`CONSEQUENCE ORACLE`, `VETO-INJECTION LEG`) against the author's claim on
your DB: generate the violating row the author's friendly seed lacked, run the challenge that fits
the op class, and capture the exact `Msg` + offending value. Do **not** manufacture a veto on an op
class that structurally cannot fire one — naming the absence is the honest result.

### 5. JUDGE — dispatch `skills/review/verdict`

Fold reproduce + scope + attack into one of four levels (below), log any named risk/refusal in the
ledger, and route the escalation. Grade the packet against the rubric dimensions (`self-test/rubric.md`)
as the audit checklist — the change earns BLESS only if it passes the same fitness bar the author was
scored on.

## The four-level disposition (owned by `skills/review/verdict`; one line each here)

- **BLESS** — every obligation discharged, no downgrading finding -> straight to the deploy gate, zero lead time.
- **BLESS-WITH-NAMED-RISK** — fine given a logged, accepted risk (an out-of-band consumer, an asserted-not-proven reversibility) -> one-line lead accept/override.
- **HAND-BACK** — a real defect the OS-dev can fix **without** the lead (missing refactorlog, skipped orphan probe, over-length narrow) -> routes to persona-1; the lead never sees it.
- **REFUSE-ESCALATE** — a genuine design fork or irreversible-step judgment -> reaches the human lead with the blast map + the single specific question, homework done.

## What this skill REUSES (does not rebuild)

- `self-test/PROTOCOL.md` — the isolation + teardown mechanics, **wholesale**. Not a second protocol.
- `skills/prove-on-dacpac` — the publish loop you **re-run** (Strict/Permissive, the runtime shim,
  the content-hash oracle, the two named moves). Not re-scaffolded.
- `skills/talk-to-local-sql` — the substrate + every probe/injection SQL.
- `skills/classify-mechanism` — invoked **only** when a claim fails to reproduce, to establish the
  corrected axes. Not run per-change from scratch.
- `agents/change-author.md` — the packet contract (its Handoff section is the producer).
- The three sibling review skills it dispatches: `blast-radius`, `adversary`, `verdict`.

## Hard rules

- Everything you touch lives under `ssdt-agent/`. Never edit the F# codebase, never edit the authored
  proving-ground tree — only your `$SCRATCH` copy.
- You **scaffold** the reproduce/attack commands; you do not ship a wrapper. You are one more
  PROTOCOL executor, isolated and self-reaping.
- You **audit, you do not re-author.** Re-running `classify-mechanism` is reserved for a claim that
  failed to reproduce — never the default path.
