---
name: multi-phase
description: Cross-cutting KNOWLEDGE shared by split-table, merge-tables, move-attribute, extract-to-lookup, archive-entity, delete-attribute (4-phase), retype-explicit, and temporal-convert. Owns the additive -> cutover -> subtractive shape, the coexistence WHY (a running app cannot switch shapes atomically), and the totality/conservation proofs that MUST pass before any subtractive drop (BEFORE/AFTER hash equal, source+archive == original, total-mapping zero-unmapped, cardinality absorbed == distinct-parents). BlockOnPossibleDataLoss blocks the drop phase; it is the licensing gate on that drop. Per-op skills POINT here. The publish loop + hash check live in prove-on-dacpac / talk-to-local-sql.
---

# Multi-phase — old and new must coexist

> When a request *relocates data between shapes* — rather than the schema merely *gaining* shape —
> the change can no longer ship in one release. The phases exist because a running app cannot switch
> shapes atomically. Every op that moves data points here, so the shape and the proofs are the same
> every time.

You are helping an **OutSystems-native developer** whose change moves data (a split, a merge, a
field relocation, a text-to-lookup promotion, an archive, a column drop, an explicit retype, a
temporal conversion). None of these is a single `CREATE` edit; all of them are staged for the same
reason.

## The coexistence WHY (specialize per op; do not restate the whole thing there)

The running OutSystems app **cannot atomically stop reading the old shape and start reading the new
one** the instant a deploy lands. If you drop the old shape in the same release that creates the
new one, any app code still on the old shape breaks the moment it deploys — and if the copy script
had a bug, the data is already gone. So the two shapes must **coexist** until reads are repointed,
and only then is the old shape removed. That coexistence requirement (state-variable 3) is exactly
what makes these changes **multi-PR**.

## The three-phase shape (each phase its own PR)

- **Phase 1 — additive.** Create the new shape (table / column / lookup / archive destination).
  Post-deploy-copy the existing rows across; dual-write new rows so both shapes stay in agreement.
  The app still reads the old shape. **Strict publishes clean — it is purely additive.** It ships
  as an additive schema change plus a post-deployment copy; the approval weighs the dual-write
  the application gains.
- **Phase 2 — cutover.** Repoint app reads (and FKs, views) from the old shape to the new. No
  schema change to prove beyond confirming both shapes still agree (hash both). Leave a
  backward-compat **computed column** exposing the old shape if any external consumer still
  references it (see `../identity-and-refactorlog/SKILL.md` for the computed-column bridge).
- **Phase 3 — subtractive.** Drop the old shape. On a populated table `BlockOnPossibleDataLoss`
  blocks a declarative drop the **same data-blind way** it blocks every tightening — on row
  presence, not on whether the copy succeeded (`../tightening-class/SKILL.md`). The conservation
  proof (below) does **not** lift that gate; the gate never reads it. What the proof licenses is the
  **reviewer's decision** that the data is safe to remove — it is the evidence the drop is approved
  on, not a signal the engine acts on. The drop's own shipping shape under the locked gate is the
  per-op subtractive pattern (`../../op/<op>/SKILL.md`; delete-attribute's 4-phase deprecation is the
  worked case). A dev lead approves the drop; once the removed data is genuinely irrecoverable,
  the approval carries the strongest weigh-line on this estate — the removal cannot be undone,
  and the record names it explicitly.

The empty-source case: if the source is **empty**, there is no data to move — the whole staged
shape collapses to a clean additive create (plus a clean subtractive drop). Prove the source is
empty first.

## The application-side cutover (the OutSystems half of Phase 2)

The schema side of every phase is provable on a disposable copy; the application side is not — no
copy can republish an OutSystems module. It is still part of the change, in a fixed order, and the
pull request names it rather than assuming it:

1. **The schema release deploys first.** OutSystems cannot consume what does not exist yet.
2. **Integration Studio:** refresh the External Entity definitions so the extension sees the new
   shape, then publish the extension.
3. **Service Studio:** refresh the extension reference in each consuming module and republish —
   "Outdated References" clears as each module picks up the new shape. The fan-out is real: modules
   referencing a changed entity republish, and modules referencing *those* may follow.
4. **Smoke test** the screens and flows that read the changed entity before calling the cutover done.

Two sequencing rules the phases depend on:

- The app release that **starts reading the new shape** (and dual-writing it, per Phase 1) follows
  the schema release that created it — never the same deploy.
- The app release that **stops writing the old shape** must be live before Phase 3's subtractive
  drop ships; otherwise the drop breaks a writer that still exists.

None of this is provable on the disposable copy, so the pull request carries it under **Not
verified**: the republish scope (which modules), the owner who confirms it, and the smoke test that
closes it. A cutover whose application half is unnamed is not a plan; it is a hope.

## The conservation proof that licenses the subtractive drop

**No subtractive drop ships until its proof passes.** The proof is op-specific in *what* it counts,
but always a conservation/totality check:

- **split / move-attribute / merge (value leg)** → BEFORE/AFTER **content-hash equal**: the moving
  columns in the source hash-equal to the same columns in the new home. Every value arrived.
- **merge (cardinality leg)** → **absorbed rows == distinct parents.** A merge silently *assumes*
  1:1; on actual 1:many a naive copy keeps one row per parent and drops the rest — and a value-hash
  will **not** catch it, because it only compares the rows that survived. Prove the **row-count**
  first, before any copy. Unequal counts = STOP; this is a design decision, not a matter of how the
  change ships.
- **archive** → **source-count + archive-count == original total** (no row lost, none duplicated),
  each batch commits (transaction log stays bounded).
- **extract-to-lookup** → **total mapping, zero unmapped:**
  `SELECT DISTINCT <oldcol> FROM <t> WHERE <oldcol> NOT IN (SELECT Code FROM <lookup>)` returns
  **zero rows** before the old column drops. One unmapped value silently becomes NULL, or blocks
  the FK.
- **delete-attribute (4-phase)** → prove the column is genuinely dead: stop-writes phase +
  `sys.dm_sql_referencing_entities` shows nothing reads it, before the irreversible drop.

The `BlockOnPossibleDataLoss` block on the drop phase is **the gate being conservative and
data-blind** — SSDT refuses on row presence, and it cannot know (and never checks) that the copy
already succeeded. So the conservation proof does **not** clear the block, and the gate does not
read it. The proof clears the only thing evidence *can* clear — the **reviewer's doubt** that the
data is safe to lose. The gate itself is cleared by the drop's shipping shape (the per-op
subtractive pattern), never by the proof: keep the two separate, or an agent will wrongly promise
that a passing proof makes the drop deploy.

## The ops this governs (and their distinguishing proof)

| Op | Distinguishing proof it still owns |
|---|---|
| split-table | hash the moving columns source vs. new table = equal |
| merge-tables | **cardinality first** (absorbed == distinct parents), then value-hash |
| move-attribute | join is 1:1, then value-hash (a cross-table move is NOT a rename — see identity) |
| extract-to-lookup | total mapping, zero unmapped, before the drop |
| archive-entity | source+archive == original; batched, log bounded |
| delete-attribute | column provably dead (referencing-entities + stop-writes) before drop |
| retype-explicit | `TRY_CONVERT` over real data — count the non-convertible rows |
| temporal-convert | period-column backfill produces sane ROW START; rows themselves untouched |

## Prove it (pointer, not a re-scaffold)

For the publish loop that runs each phase and shows the Phase-3 drop being blocked, see
`../../prove-on-dacpac/SKILL.md`. For the order-independent content-hash check (the BEFORE/AFTER
equality that licenses the drop) and the row-count / mapping probes, see
`../../talk-to-local-sql/SKILL.md`.

## The in-flight register (the forgotten Phase 2 is a red gate, not a memory)

Every change this concern governs holds a row in **`../../../estate/in-flight.md`** from the
merge of its first phase until the final phase ships: phase N of M, what ships next, and the
date the current window closes. **While any phase's model lags its database — every
tightening's R1→R2 gap and every contract leg — hold other publishes to that environment:**
any publish carrying the lagging model regenerates the old shape on a green publish (captured
live: one lagging-model publish re-created a dropped column, backfilled from its default —
`../../../sample-prs/compound/extract-to-lookup-program.md`). The in-flight row is the
register that hold is checked against. Advancing a phase updates the row; the CI gate fails the
build on an expired window, so re-dating one is a conscious, reviewer-signed act; shipping
the final phase deletes the row. A multi-phase change without its register row is incomplete
— the same standing as a rename without its refactorlog entry.

## Handbook

Cite by **filename**: **11-Multi-Phase-Evolution.md** (the additive→cutover→subtractive contract)
and handbook **14** (= §17; the operation recipes — §17.7 Table-Merge's curriculum home is the
playbook's `Table-Merge.md`; the proven working contract is the per-op skill
`../../op/merge-tables/`).
