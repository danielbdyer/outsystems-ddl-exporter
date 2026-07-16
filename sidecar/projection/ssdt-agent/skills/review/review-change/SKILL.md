---
name: review-change
description: The review conductor — the single entry for "review this change". Use when the lead's adversarial reviewer (Persona 2) is handed a change-author review packet (how it ships and who must review, the generated delta, the proof, the change set, the named trap, the surfaced reasoning) and must audit it rather than re-author it. Picks a PROTOCOL identity (unique PG_<id>_<rand> DB + scratch copy), reproduces the author's claimed Strict/Permissive outcome on that isolated DB, then dispatches dependency-scope -> adversary -> verdict in order and returns one of the four dispositions. A claim that will not reproduce is returned to the author or escalated. Reuses PROTOCOL wholesale, re-runs prove-on-dacpac's existing loop, and only re-runs classify-mechanism when a claim fails to reproduce.
---

# Review a change (the conductor)

> **The discipline: reproduce, don't read.** A disposition without a reproduced delta is an
> **opinion**. The author already proved the change once; the reviewer's value is not to believe the
> packet — it is to **make the same proof pass again** on a fresh, isolated DB, then attack the seams
> the author's seed was too friendly to expose. The audit is of the *proof*, never a re-litigation of
> the *label*. An approval a lead can trust is one where the reviewer re-ran the blocked publish, not
> one where it read the claim.

You are the conductor of **classify-by-proving in scrutineer posture**. The change-author already
ran the loop and handed a **review packet**. You do **not** re-author the change or re-classify it
from scratch. You **reproduce** the author's proof on your own throwaway DB, then dispatch three
review skills in a fixed order, and return one of the four dispositions. This skill owns the
*sequence* and the *reproduce step*; the three siblings own scope, attack, and judgment.

## Your input — the review packet (the audit target)

The packet `change-author` emits (its **Handoff** section is the producer). Every field is a
**claim you must discharge**, not a fact you inherit:

| Packet field | The claim it makes | Becomes the proof obligation |
|---|---|---|
| operation(s) + target object | "this is op X on object Y" | the op-slug the adversary and dependency-scope load |
| **how it ships and who must review** — the two findings (`THE_RECORD.md` §5) | "the shipping shape and the review need are these" | reproduce must yield the same delta/block; any added-scrutiny finding must survive dependency-scope |
| **generated delta** (`/Action:Script`) | "SSDT would run exactly this" | re-run `/Action:Script` on your DB; the delta must match in kind (sp_rename vs DROP+CREATE, the guard placement) |
| **proof** — named Strict block + row counts + clean Strict re-run after remedy | "Strict blocked for N rows, and my remedy makes it pass clean" | **re-run both** — the blocked publish AND the clean re-run — on your DB. A proof that passed once for the author must pass for you |
| full **change set** — CREATE(s) + refactorlog + pre/post-deploy + multi-phase plan | "the recipe is complete and reversible" | build it on your DB; missing refactorlog / unguarded MERGE / NOCHECK-left-untrusted are auto-findings |
| named **trap**, if caught | "the anti-pattern is X (or none)" | the adversary hunts the trap the author *missed*, not just re-confirms the one caught |
| **surfaced reasoning** | "the why is drawn from `_index/<concern>`" | graded by the rubric dimensions (see `skills/review/verdict`) — right label + wrong-source why is a downgrade signal, not an approval |

"**Count every crossing**": each row above is one obligation. You discharge it or you reject it. An
un-discharged obligation cannot ride inside an approval.

## The audit sequence (fixed order — do not reorder)

**REPRODUCE -> SCOPE -> ATTACK -> JUDGE.** Scope before attack (you cannot approve past a cascade you
never enumerated); attack before judge (a finding may downgrade the disposition).

### 1. Pick your isolated identity — PROTOCOL, wholesale

Do **not** restate the isolation mechanics — they are owned by `self-test/PROTOCOL.md` and you obey
them verbatim: a unique `PG_<reviewId>_<rand>` DB, a scratch copy of the proving ground, every
`sqlpackage` call carrying `/TargetDatabaseName`, and an **unconditional teardown** on exit (drop
DB + `rm -rf` scratch). Resolve `DB`/`SCRATCH` once, reuse the literal values. For a **CDC** packet,
serialize per PROTOCOL §8 — the review runs no faster than the author's did. Nothing about being a
*reviewer* relaxes the isolation invariants; you are one more executor in the fleet.

### 2. REPRODUCE the author's claimed outcome

Re-run the author's proof on **your** DB using the **existing** `prove-on-dacpac` loop — you do not
re-scaffold it, you re-run it:

- Copy the packet's change set into `$SCRATCH` (the edited CREATE(s), the refactorlog, pre/post-deploy).
- Build the dacpac, `/Action:Script` -> read **your** `delta.sql`, compare its *kind* to the packet's.
- Publish **Strict** to `$DB`. If the packet claimed the publish was **blocked**, your Strict run
  must be blocked the same way (same guard, comparable row count). If the packet claimed **clean**
  (an in-place change that touches no data), your Strict run must publish clean.
- If the packet claimed a **remedy that re-passes Strict clean**, apply the remedy on `$SCRATCH` and
  confirm your Strict re-run is clean too.

**Reproduce is a gate, not a formality:**

- **Reproduces as claimed** -> the obligation is discharged; proceed to scope + attack.
- **Does NOT reproduce** (packet said clean, your Strict blocks; packet said sp_rename, your delta
  is DROP+CREATE; the claimed clean re-run still blocks) -> the claim is **false as stated**. This is
  an automatic **return to the author** (backstop) or **escalation** — you do not silently fix it and
  approve. Only *here*, when a claim fails to reproduce, do you re-run `classify-mechanism` to
  establish what the engine *actually* forces — that becomes the corrected finding returned to the
  author.

> The one packet class where a **persisting block is the correct reproduction, not a failure**:
> make-mandatory on a populated table (the guard is table-has-rows). Reproduce = backfill to 0 NULLs
> on your DB, re-run Strict, prove it **STILL** blocks. Owned by `skills/_index/tightening-class`;
> the adversary drives this challenge (`skills/review/adversary`, challenge 2). A packet claiming a
> clean publish after a backfill on a populated table, which you reproduce to a still-block, is a
> caught defect, not a reproduce miss.

### 3. SCOPE — dispatch `skills/review/dependency-scope`

Enumerate the dependency closure (FKs in/out, views, procs, indexes, CDC capture instances,
external/ETL) and count affected rows on your DB. **No disposition may exceed this scope.** The
dependency map it produces is what an escalation carries to the lead.

### 4. ATTACK — dispatch `skills/review/adversary`

Wield the two named moves (`consequence check`, `violating-row probe`) against the author's claim on
your DB: generate the violating row the author's friendly seed lacked, run the challenge that fits
the op class, and capture the exact `Msg` + offending value. Do **not** manufacture a block on an op
class that structurally cannot fire one — naming the absence is the honest result.

### 5. JUDGE — dispatch `skills/review/verdict`

Fold reproduce + scope + attack into one of the four dispositions (below), log any named risk or
refusal in the ledger, and route the escalation. Grade the packet against the rubric dimensions
(`self-test/rubric.md`) as the audit checklist — the change is approved only if it passes the same
fitness bar the author was scored on.

## The four dispositions (owned by `skills/review/verdict`; one line each here)

- **Approved.** Every obligation discharged, no downgrading finding -> straight to the deploy gate, zero lead time.
- **Approved with a named risk.** Sound given a logged, accepted risk (an out-of-band consumer, an unproven reversibility) -> one-line lead accept or override.
- **Returned to the author.** A real defect the developer can fix **without** the lead (missing refactorlog, skipped orphan probe, over-length narrow) -> routes to persona-1; the lead never sees it.
- **Escalated — one question for the lead.** A genuine design fork or irreversible-step judgment -> reaches the human lead with the dependency map + the single specific question, homework done.

## What this skill REUSES (does not rebuild)

- `self-test/PROTOCOL.md` — the isolation + teardown mechanics, **wholesale**. Not a second protocol.
- `skills/prove-on-dacpac` — the publish loop you **re-run** (Strict/Permissive, the runtime shim,
  the content-hash check, the two named moves). Not re-scaffolded.
- `skills/talk-to-local-sql` — the substrate + every probe/injection SQL.
- `skills/classify-mechanism` — invoked **only** when a claim fails to reproduce, to establish the
  corrected findings. Not run per-change from scratch.
- `agents/change-author.md` — the packet contract (its Handoff section is the producer).
- The three sibling review skills it dispatches: `dependency-scope`, `adversary`, `verdict`.

## Hard rules

- Everything you touch lives under `ssdt-agent/`. Never edit the F# codebase, never edit the authored
  proving-ground tree — only your `$SCRATCH` copy.
- You **scaffold** the reproduce/attack commands; you do not ship a wrapper. You are one more
  PROTOCOL executor, isolated and self-reaping.
- You **audit, you do not re-author.** Re-running `classify-mechanism` is reserved for a claim that
  failed to reproduce — never the default path.
