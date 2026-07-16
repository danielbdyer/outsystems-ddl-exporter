---
name: adversary
description: The edge-case / red-team generator for the lead's adversarial reviewer. Use when review-change has reproduced an author's proof and it needs to be stress-tested — generate the violating data the author's friendly seed lacked and run the "what about X" challenge that fits the op class. Wields prove-on-dacpac's two named proof moves against the author's claim: after Strict blocks a data-removing op, run Permissive on the copy to observe the irreversible act; for a constraint/tightening op, inject a dup/orphan/over-length/NULL to capture the exact Msg and offending value. Owns 8 concrete challenges on real ops. Obeys prove-on-dacpac's scope discipline — never manufactures a block on an op class that structurally cannot fire one. Every WHY points to its _index owner; restates none.
---

# Adversary (the red team)

> **Why this pass.** The author proved the change safe for the data they seeded. The seed they were
> too kind to write — the orphan they didn't plant, the row that makes the table non-empty, the CDC
> column the dacpac can't see — is where the risk hides, and SSDT's own engine passes the verdict on
> it. No new attack is invented here; the two moves the disposable-copy loop already owns are pointed
> at the author's blind spot. A clean proof on a friendly seed is a hypothesis; the adversarial seed
> is the test that settles it.

The input is a **reproduced** change (from `skills/review/review-change`) on an isolated DB. The
adversary attacks the author's claim, and restates no guard, no rule, no trap — every WHY below
points to its `_index` owner. It adds only the *posture*: turn the author's clean seed into a hostile
one and read the engine.

## The two moves this pass wields (owned by `prove-on-dacpac`, not redefined)

- **The consequence run** — for a data-removing op that a principal must review because data is
  removed irreversibly. After Strict blocks, run Permissive on the scratch copy to let the
  irreversible act happen, then snapshot the exact rows and values that would be lost (the
  content-hash check in `talk-to-local-sql`). Turns "this could lose data" into "40,132 rows would
  be lost, here they are."
- **The injection leg** — for a *constraint / tightening* op with a data-loss block. If the author's
  seed is clean, **inject one violating row** (a dup, an orphan, an over-length value, a NULL) into
  `$DB`, then publish — to capture the **exact `Msg` number and the offending value** the author
  never provoked. Turns "clean on this seed" into "Msg 547, FK Order_Customer, offending value 999."

The injection SQL is `talk-to-local-sql`'s; the moves are `prove-on-dacpac`'s. Point at the author's
blind spot; do not re-scaffold.

## Scope discipline (inherited from `prove-on-dacpac`, non-negotiable)

These moves apply **only** to op classes that HAVE a data consequence or block. On ops with **no**
data block — `edit-seed` (a MERGE), `enable-cdc` (a scripted change), `create-view` (no rows),
`add-optional` (a nullable add) — do **not** manufacture a block that structurally cannot fire.
**Naming the absence IS the honest result:** "add-optional on a populated table — no block is
possible, the column is nullable; the clean Strict publish reproduces, and the pass stops there. No
attack surface here." A fabricated block on an op that cannot block is a review defect, not diligence.

## The challenges (8 concrete, on real ops from the 12-table catalog)

Each names: the author's likely **claim**, the **move**, the **injection/probe**, the exact
**engine signal** to capture, and the **`_index` owner** of the WHY (cited, never restated).

1. **A rename with no refactorlog entry, mislabeled clean** — *op `rename-attribute`.* Claim: "a
   plain `sp_rename`, applied in place, nothing lost." Script the delta on the scratch DB. If the
   refactorlog entry is absent, the delta is **DROP COLUMN + ADD** (silent data loss), not
   `sp_rename`. Capture the `DROP`/`ADD` lines. Demand `sp_rename` via the refactorlog. WHY ->
   `_index/identity-and-refactorlog` (identity is separate from name). Finding routes to
   **Returned to the author**.

2. **Make-mandatory claimed clean on a POPULATED table** — *op `make-mandatory`, target e.g.
   `Customer.Email`.* Claim: "backfilled the NULLs, so it ships clean as a pre-deployment backfill
   then the schema change." **Reproduce and go further:** backfill to **0** NULLs on the scratch DB
   (probe `COUNT(*) WHERE Email IS NULL` = 0), re-run Strict, and prove it **STILL blocks** — the
   guard is **table-has-rows, not column-has-NULLs** (`IF EXISTS(SELECT TOP 1 1 FROM Customer)
   RAISERROR`). The clean claim is false on any populated table. WHY -> `_index/tightening-class`.
   This is the gating challenge; a reviewer fooled by the clean claim fails the whole review. Finding
   routes to **Escalated — one question** (gate-relaxation after zero NULLs vs. staging across
   releases is a design fork).

3. **Add-FK claimed clean, orphan probe skipped** — *op `create-fk-clean` / `create-fk-orphan`,
   target e.g. `Order.CustomerId -> Customer`.* Claim: "clean — ships in place, the foreign key
   trusts on apply." **The injection leg:** inject an orphan (`Order.CustomerId = 999` with no
   matching Customer) into `$DB`, publish. Capture **Msg 547** + the orphan count. Even without
   injection, if the seed already has orphans the author never ran the LEFT JOIN probe. Demand
   `NOCHECK -> reconcile -> WITH CHECK CHECK`, prove `is_not_trusted = 0`. WHY ->
   `_index/constraint-is-a-claim`. Finding routes to **Returned to the author**.

4. **Narrow claimed clean on populated data** — *op `narrow`, target e.g. `Product.SKU`.* Claim:
   "clean — ships as a single schema change, applied in place." Run the `MAX(LEN(SKU))` probe on the
   scratch DB; if it exceeds the new size, or inject an over-length value
   (`'STANDARD-SKU-000000001'`), publish. Capture the **truncation block** + the offending length. A
   populated table never collapses to a clean in-place change when a value exceeds target. WHY ->
   `_index/tightening-class` (Ambitious Narrowing). Finding routes to **Returned to the author**
   (pre-deploy fit-check + the conscious gate call).

5. **CDC silent-gap change approved on a clean Strict publish** — *op e.g. `add-optional` on
   `CdcCandidate` (CDC-tracked).* Claim: "clean, ships in place, any team member can review it, no
   added scrutiny." The Strict publish **is** clean — and that is the trap: **CDC is invisible to the
   dacpac.** Prove the gap: after the column adds, `EXEC sys.sp_cdc_get_captured_columns
   @capture_instance='dbo_CdcCandidate'` **lacks the new column** — the capture instance is frozen to
   the enable-time shape. Do NOT be fooled by the clean publish. Demand the **added-scrutiny finding**
   and a capture-instance-recreate plan. WHY -> `_index/cdc` (the feed is frozen to the old shape; the
   added scrutiny a CDC table carries). Finding routes to **Escalated — one question** (no-gap vs.
   tolerable-gap is a design fork).

6. **Cascade scope unmapped on a delete-rule change** — *op `change-delete-rule`, target e.g. the
   `OrderLine -> Order` FK.* Claim: "ships in place, narrow dependency scope." Before approving,
   enumerate the FK-graph depth: `Order -> OrderLine`, and any FK *into* Order, so a `CASCADE` fans
   out further than the one edge the author looked at. Count the rows a cascade would touch. An
   **Approved** that never enumerated the cascade is invalid (`dependency-scope` owns the scope; the
   cascade is fed to it to enumerate). WHY -> `_index/multi-phase` (coexistence + cascade closure).
   Finding routes to **Approved with a named risk** or **Escalated — one question**, by depth.

7. **Refactorlog-cleanup left stale** — *op `rename-attribute` / a prior rename.* Claim: "rename done,
   refactorlog present." Check that the refactorlog entry **matches this rename and no other** — a
   stale entry from a previous rename (the Refactorlog Cleanup anti-pattern) makes SSDT emit an
   `sp_rename` for the *wrong* column, or a no-op that leaves the real rename with no refactorlog
   entry. Read the `.refactorlog` against the delta. WHY -> `_index/identity-and-refactorlog`. Finding
   routes to **Returned to the author**.

8. **Idempotent-seed over-capture** — *op `edit-seed` / `create-static-seed`, target e.g. `Status`.*
   Claim: "seed is idempotent." This op has **no data block** (it is a MERGE) — so do NOT manufacture
   one. Instead prove the **silence**: run the seed twice on the scratch DB; an **unconditional `WHEN
   MATCHED`** captures rows on the *second* (no-op) run and the content-hash shifts — the seed is not
   idempotent. A guarded `WHEN MATCHED` captures **0** rows on redeploy (CDC-silence). WHY ->
   `_index/idempotent-seed` (silence is the proof). Finding routes to **Returned to the author**
   (guard the MERGE).

## Picking the challenge (by op class)

- **Tightening ops** (`make-mandatory`, `narrow`, `delete-attribute` drop-face, `retype-explicit`) ->
  challenges 2, 4 + the injection leg. The still-blocks-at-zero-NULL result is the sharpest.
- **Constraint ops** (`create-fk-*`, `add-unique`, `add-check`, `define-pk`, `modify-index` unique) ->
  challenge 3 + the injection leg (inject the dup/orphan/violation).
- **Identity ops** (`rename-attribute`, `rename-entity`) -> challenges 1, 7 (read the delta for
  DROP+CREATE; audit the refactorlog).
- **Data-removing ops** (`delete-attribute`, `delete-entity`, drop-table, `narrow` past data) ->
  the consequence run (snapshot the rows and values that would be lost), and the sparring
  counter-design in `agents/reviewer.md`.
- **CDC-touching ops** (any change on `CdcCandidate` or a CDC-tracked table) -> challenge 5 (the gap
  is invisible to Strict — never trust the clean publish).
- **Coexistence / cascade ops** (`change-delete-rule`, `merge-tables`, `extract-to-lookup`,
  `temporal-convert`) -> challenge 6 + the cardinality/mapping proof.
- **No-block ops** (`edit-seed`, `enable-cdc`, `create-view`, `add-optional`) -> challenge 8 for seeds;
  otherwise **name the absence and stop.**

## What this skill REUSES (does not rebuild)

- `prove-on-dacpac` — the two named proof moves + the scope discipline. Wielded, not redefined; no third move is invented.
- `talk-to-local-sql` — every injection/probe SQL (the orphan INSERT, the over-length value, the `MAX(LEN)` probe, `sp_cdc_get_captured_columns`).
- `_index/tightening-class` (challenges 2, 4) · `_index/constraint-is-a-claim` (challenge 3) ·
  `_index/cdc` (challenge 5) · `_index/identity-and-refactorlog` (challenges 1, 7) ·
  `_index/multi-phase` (challenge 6) · `_index/idempotent-seed` (challenge 8) — every WHY, by pointer.
- The per-op skills (`skills/op/<slug>`) for the op's specifics.

## What this skill deliberately does NOT build

- **No cdc-sentinel skill** — the CDC silent-gap challenge (5) references `_index/cdc`; the
  added-scrutiny tripwire and the frozen-capture-instance WHY live there.
- **No reversibility skill** — the data-removing-op counter-design references `_index/multi-phase` and
  `prove-on-dacpac`'s "proves the FORWARD publish only" edge; reversibility stays an asserted claim,
  not a new surface.
- **No third adversarial move** — exactly the two `prove-on-dacpac` owns, in scrutineer posture.
