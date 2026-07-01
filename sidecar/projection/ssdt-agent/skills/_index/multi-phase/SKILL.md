---
name: multi-phase
description: Cross-cutting KNOWLEDGE shared by split-table, merge-tables, move-attribute, extract-to-lookup, archive-entity, delete-attribute (4-phase), retype-explicit, and temporal-convert. Owns the additive -> cutover -> subtractive shape, the coexistence WHY (a running app cannot switch shapes atomically), and the totality/conservation proofs that MUST pass before any subtractive drop (BEFORE/AFTER hash equal, source+archive == original, total-mapping zero-unmapped, cardinality absorbed == distinct-parents). The BlockOnPossibleDataLoss veto is the licensing gate on the drop phase. Per-op skills POINT here. The publish loop + hash oracle live in prove-on-dacpac / talk-to-local-sql.
---

# Multi-phase — old and new must coexist

> The instant a request *relocates data between shapes* — rather than the schema merely *gaining*
> shape — you have left the single-release world. The phases exist because a running app cannot
> switch shapes atomically. Every op that moves data points here so the shape and the proofs are
> the same, every time.

You are helping an **OutSystems-native developer** whose change moves data (a split, a merge, a
field relocation, a text-to-lookup promotion, an archive, a column drop, an explicit retype, a
temporal conversion). None of these is a single `CREATE` edit; all of them are staged for the same
reason.

## The coexistence WHY (specialize per op; do not restate the whole thing there)

The running OutSystems app **cannot atomically stop reading the old shape and start reading the new
one** the instant a deploy lands. If you drop the old shape in the same release that creates the
new one, any app code still on the old shape breaks the moment it deploys — and if the copy script
had a bug, the data is already gone. So the two shapes must **coexist** until reads are repointed,
and only then is the old shape removed. That coexistence requirement (state-variable 4) is exactly
what makes these changes **multi-PR**.

## The three-phase shape (each phase its own PR)

- **Phase 1 — additive (Mechanism 1/2).** Create the new shape (table / column / lookup / archive
  destination). Post-deploy-copy the existing rows across; dual-write new rows so both shapes stay
  in agreement. The app still reads the old shape. **Strict publishes clean — it is purely
  additive.** Tier 2.
- **Phase 2 — cutover (Mechanism 2).** Repoint app reads (and FKs, views) from the old shape to the
  new. No schema change to prove beyond confirming both shapes still agree (hash both). Leave a
  backward-compat view named for the old shape if any external consumer still references it (see
  `../identity-and-refactorlog/SKILL.md` for the compat-view bridge).
- **Phase 3 — subtractive (Mechanism 3).** Drop the old shape from the project. **This is the
  `BlockOnPossibleDataLoss` veto moment** — Strict MUST veto until the conservation proof (below)
  licenses the drop. Tier 3 (Tier 4 once the removed data is genuinely irrecoverable).

The greenfield collapse: if the source is **empty**, there is no data to move — the whole thing
collapses to a clean **Mechanism 1** additive create (+ a clean subtractive drop). Prove the source
is empty first.

## The conservation proof that licenses the subtractive drop

**No subtractive drop ships until its proof passes.** The proof is op-specific in *what* it counts,
but always a conservation/totality check:

- **split / move-attribute / merge (value leg)** → BEFORE/AFTER **content-hash equal**: the moving
  columns in the source hash-equal to the same columns in the new home. Every value arrived.
- **merge (cardinality leg)** → **absorbed rows == distinct parents.** A merge silently *assumes*
  1:1; on actual 1:many a naive copy keeps one row per parent and drops the rest — and a value-hash
  will **not** catch it, because it only compares the rows that survived. Prove the **row-count**
  first, before any copy. Unequal counts = STOP; this is a design decision, not a mechanism flip.
- **archive** → **source-count + archive-count == original total** (no row lost, none duplicated),
  each batch commits (transaction log stays bounded).
- **extract-to-lookup** → **total mapping, zero unmapped:**
  `SELECT DISTINCT <oldcol> FROM <t> WHERE <oldcol> NOT IN (SELECT Code FROM <lookup>)` returns
  **zero rows** before the old column drops. One unmapped value silently becomes NULL or vetoes
  the FK.
- **delete-attribute (4-phase)** → prove the column is genuinely dead: stop-writes phase +
  `sys.dm_sql_referencing_entities` shows nothing reads it, before the irreversible drop.

The `BlockOnPossibleDataLoss` veto on the drop phase is **the gate being conservative** — it
refuses because SSDT cannot know your copy already succeeded. The veto is the *licensing gate*; the
conservation proof is what earns the license.

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

For the publish loop that runs each phase and shows the Phase-3 veto, see
`../../prove-on-dacpac/SKILL.md`. For the order-independent content-hash oracle (the BEFORE/AFTER
equality that licenses the drop) and the row-count / mapping probes, see
`../../talk-to-local-sql/SKILL.md`.

## Handbook

Cite by **filename**: **11-Multi-Phase-Evolution.md** (the additive→cutover→subtractive contract)
and handbook **14** (= §17; the operation recipes — note §17.7 merge-entities and the §17.8
compat-view companion have missing handbook bodies and are AUTHORED in the per-op skills).
