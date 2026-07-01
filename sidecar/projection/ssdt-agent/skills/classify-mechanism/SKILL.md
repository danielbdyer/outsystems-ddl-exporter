---
name: classify-mechanism
description: Use after confirm-intent has named the operation and gathered the four state-variables, to assign a PROVISIONAL Mechanism (1-5) and Tier (1-4) before proving. Walks the handbook decision cascade (file 15 = §18.1, Q1-Q4), applies the +1 escalations (CDC / >1M rows / first-time op), enforces danger-is-not-release-count, and decides WHEN the change can be classified on sight versus when it MUST be proven on the proving ground. Emits a provisional verdict that prove-on-dacpac confirms or flips.
---

# Classify mechanism

> **Why this (and what it teaches).** The verdict you produce here is **provisional on purpose** —
> classification from text is the named failure mode, and the data holds the casting vote.
> Mechanism and Tier are kept as **two orthogonal axes** because *how a change ships* and *how
> dangerous it is* are genuinely independent: a single-PR `DROP TABLE` is Tier 4. What this teaches:
> never collapse danger into release-count, and never treat a from-text reading as final past the
> purely-additive corner — the discriminator is "would this same `.sql` edit get the same answer
> regardless of the rows? If not, I have guessed, not classified." Surface this to the developer:
> a developer who learns that the mechanism can *flip on data they didn't mention* stops trusting
> the schema diff alone — that is the graduation from reading SQL to proving it.

You are helping an **OutSystems-native developer** land a safe schema change. This skill is the
**spine**: it turns a named operation + its data-state into two orthogonal answers — **how** the
change ships (Mechanism) and **how dangerous** it is (Tier) — and, just as importantly, decides
whether you can answer on sight or must go prove it.

**Classification from the `.sql` text alone is a guess. The data decides the mechanism.** This
skill produces a *provisional* verdict; `prove-on-dacpac` is what makes it true. Never deliver a
provisional verdict to the developer as if it were proven.

## The two axes are orthogonal — emit BOTH, always

- **Mechanism** = the *how* (the release shape). Five of them.
- **Tier** = the *danger* and *who must review*. Four of them, with escalations.

They are not the same axis. **Danger is not release-count.** A `DROP TABLE` on populated data is
mechanically a single one-shot script (a single-PR mechanism) but **Tier 4** because the data is
gone irreversibly. Keep them separate or you will under-review a catastrophe.

## The Five Mechanisms (the team's own vocabulary)

| # | Mechanism | Release bucket | What it is |
|---|---|---|---|
| 1 | **Pure Declarative** | single-phase | Edit the CREATE; SSDT computes a safe in-place delta. No script, no data touched. |
| 2 | **Declarative + Post-Deploy** | single-PR | Declarative schema change **plus** an idempotent post-deploy script (seed/backfill that runs after the schema lands). |
| 3 | **Pre-Deploy + Declarative** | single-PR | A pre-deploy script **first** (backfill / dedupe / reconcile) so the declarative change can then pass its veto. |
| 4 | **Script-Only** | single-PR (operational) | The change is not expressible in the declarative model (CDC, `NOCHECK`->`WITH CHECK CHECK`, table-swap). Lives in a script. |
| 5 | **Multi-Phase** | multi-PR | The change spans **multiple releases** because old and new app code must coexist, or a no-gap CDC rollout forces staging. |

**Mechanism -> bucket map** (the team's release vocabulary):

- **SINGLE-PHASE** = Pure Declarative (1): no script, one publish.
- **SINGLE-PR** = Declarative+Post (2) and/or Pre-Deploy+Declarative (3), or operational Script-Only (4): one release, scripts included.
- **MULTI-PR** = Multi-Phase (5): staged across releases.

## The Tier decision cascade (handbook file 15 = §18.1, Q1-Q4)

Walk these in order. The **first** that fires sets the floor; later questions can only raise it.

- **Q1 — Will data be lost?** (drop table/column with data, narrowing truncation, naked rename)
  -> **Tier 4**. Stop here for the floor; this is the irreversible band.
- **Q2 — Will existing data change or move?** (backfill, retype, value migration)
  -> **Tier 3 minimum, and MULTI-PR** is on the table.
- **Q3 — Cross-table or external dependencies?** (FK, view, proc, ETL, reports, External Entities)
  -> **Tier 3**.
- **Q4 — Can the app keep working unchanged?**
  - **NO** (the running OutSystems app breaks without a code change) -> **Tier 2, SINGLE-PR**.
  - **YES** (additive, app oblivious) -> **Tier 1, SINGLE-PHASE**.

Then apply the escalations.

## The +1 escalations (apply after the cascade)

Each independently bumps the Tier up by one (they stack):

- **CDC-enabled table** (+1). CDC is the team's biggest tripwire — every subsequent schema change
  now needs capture-instance management (**CDC Surprise**).
- **> 1,000,000 rows** (+1). Build time, blocking, memory grants — scale turns a benign edit risky.
- **First-time operation** for this team/codebase (+1). No muscle memory, no prior proof.

A Tier never exceeds 4; once at 4, escalations only sharpen the warning (e.g. "Tier 4, and CDC
makes the rollback even harder").

> **Why this (and what it teaches).** The +1 escalations exist because some risks are *standing*,
> not visible in the single delta: a CDC-enabled table carries an obligation on **every future
> change** (the capture instance must be managed), scale turns a metadata edit into a blocking
> build, and a first-time op has no prior proof to lean on. What this teaches: the danger of a
> change is not only what *this* edit does to the data — it is what the table's *context* makes
> every edit cost. Surface this: a developer who hears "the base op is trivial, but this table is
> CDC-tracked so it's +1 forever" stops being ambushed months later when a new column silently
> vanishes from the feed — that foresight is the graduation.

## The four state-variable flips (the heart)

The **same operation** changes Mechanism as the data crosses a threshold. This is the
classify-by-proving core. For any operation, the flip table is:

| State of the data | Mechanism it flips to |
|---|---|
| table **empty** | 1 Pure Declarative (single-phase) |
| populated, **data does NOT violate** the new rule | still 1 (but **prove it** — see below) |
| populated, **data violates** the new rule (NULLs / orphans / over-length / dups) | 3 Pre-Deploy+Declarative (backfill/reconcile first), single-PR — or 2 if the fix is a post-deploy seed |
| + **CDC-enabled, no-gap required** | 5 Multi-Phase, multi-PR; **+1 Tier** |
| + **> 1M rows / first-time op** | **+1 Tier** (mechanism may also stage) |
| change **not expressible declaratively** (CDC enable, FK NOCHECK reconcile, IDENTITY swap) | 4 Script-Only |
| **old + new app code must coexist** | 5 Multi-Phase, multi-PR |

The per-operation `skills/operations/*.md` entry carries the **specific** flip table and which
seed scenario exercises it. Read it.

## WHEN to classify on sight vs WHEN you MUST prove

This is the judgment call this skill exists to make.

**You may classify on sight (no proving needed) only when ALL of these hold:**

- The operation is **purely additive** and the app is oblivious (Q4 = YES), AND
- The change touches **no existing data** (a new nullable column, a brand-new table, a new index
  on a new column), AND
- The table is **not CDC-enabled** and **not** above the scale/first-time thresholds.

That is the Tier 1 / Mechanism 1 corner, and only that corner. Examples: `add-attribute-optional`
on a non-CDC table; `create-entity`; a post-deploy `MERGE` that adds a genuinely new lookup value.

**You MUST prove (hand to `prove-on-dacpac`) whenever ANY of these is true** — which is
*everything past the additive corner*:

- The operation could touch, move, or refuse existing data: **make-mandatory, narrow, retype,
  create-FK, add-unique, add-check, define-PK, delete-anything, rename-anything.**
- **Any** of the four state-variables is `unknown` (and for the data-violation variable it is
  *almost always* unknown — the developer's recollection is not proof).
- A **named trap** is in play (Naked Rename, Optimistic NOT NULL, Ambitious Narrowing, Forgotten
  FK Check, CDC Surprise). The delta is the only place to catch these reliably.
- The table is **CDC-enabled** or above the **scale/first-time** thresholds.

The discriminator that makes this real: **make-mandatory on a table with NULLs vs without NULLs is
identical `.sql` text but a different Mechanism.** If you would answer both the same way from the
text, you have not classified — you have guessed. Prove it.

> **Why this (and what it teaches).** Note the *honest correction* the proving ground forces here:
> for `make-mandatory` on a **populated** table, even *without* NULLs the change does **not** pass
> the prod-strict gate cleanly — SSDT's guard is **table-has-rows, not column-has-NULLs** (see
> `prove-on-dacpac`). So the on-sight corner is genuinely only the purely-additive case; the
> moment rows exist *and* a constraint tightens, you must prove. What this teaches: the size of the
> "answer-on-sight" zone is much smaller than intuition suggests — when in doubt, the cost of one
> publish is far less than the cost of a wrong verdict. Surface this to the developer so they
> understand *why* you are proving something that "looks obvious."

## What you emit (the provisional verdict)

A structured handoff to `prove-on-dacpac`:

- **operation** (from the change-order).
- **provisional Mechanism** (1-5) + its release bucket.
- **provisional Tier** (1-4), with each +1 escalation named and why.
- **cascade trace**: which of Q1-Q4 fired and set the floor.
- **the suspected flip**: which state-variable, if it crosses, changes the answer — and the exact
  thing the proving ground must check (e.g. "COUNT of NULL Email; Strict must veto if > 0").
- **on-sight vs must-prove**: your decision and the reason. If on-sight, say why proving is
  unnecessary; if must-prove, name the proof to demand.
- **named trap, if suspected**, so the delta gets read for it.

Label the whole thing **PROVISIONAL**. It becomes a verdict only after `prove-on-dacpac` returns.

## Worked examples

- **"Make Customer.Email required."** Operation `make-mandatory`. Q4 = NO (app may write NULLs
  today) -> floor Tier 2. Data-violation variable unknown -> **must prove**. Provisional: Mechanism
  1 *if* truly empty, but on a **populated** table it does **not** land clean even with zero NULLs
  (the guard is table-has-rows) — so expect a Strict veto and a conscious gate decision
  (named gate-relaxation after a proven-zero-NULL backfill, or multi-phase). Proof to demand:
  `COUNT(*) WHERE Email IS NULL`; then prove the veto STILL fires after the backfill clears it.
- **"Add an optional Notes field to Customer"** (non-CDC, small). Purely additive, app oblivious,
  no existing data touched -> **classify on sight**: Mechanism 1 Pure Declarative, Tier 1. Note it
  still goes through `prove-on-dacpac` for a clean-publish confirmation, but no flip is possible.
  (If the same table were **CDC-enabled**, the +1 fires and the capture instance must be recreated
  — the trivial base op hides a non-trivial CDC obligation. Prove it.)
- **"Drop the AuditLog table"** (populated, maybe CDC). Q1 = YES (data lost) -> **Tier 4**, floor
  set immediately. CDC +1 stays at 4 but hardens the warning. Mechanism: Script-Only / single-PR
  mechanically — *danger is not release-count*. Must prove: the `BlockOnPossibleDataLoss` veto is
  the safety proof; sequencing is disable-CDC-first, drop-FKs-first.
- **"Add a FK from Order.CustomerId to Customer"** (orphans unknown). Q3 = YES (cross-table) ->
  Tier 3. Must prove. Provisional: Mechanism 1 *if* no orphans; flips to 4 Script-Only
  (`NOCHECK` -> reconcile -> `WITH CHECK CHECK`) *if* orphans. **Forgotten FK Check** suspected.

## Connector points

- The cascade and flip tables are the natural body of a future PR template / review checklist —
  `change-author`'s review packet carries this verdict to `reviewer` (deferred). See `CONNECTORS.md`.
- `.claude/skills/`-shaped and Copilot-mappable (format verify-first); see `CONNECTORS.md`.
