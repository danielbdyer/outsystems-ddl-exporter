---
name: archive-entity
description: Use when the developer says "archive old orders", "move the historical rows out to an archive table", "move records we don't need live anymore" — a data movement between tables, not a shape change.
---

# Archive entity

> **Default (provisional — the data decides).** Ships across releases: the archive table is added,
> then a batched post-deployment script moves the rows and the counts are reconciled, so the
> running application keeps reading live data while the move is in flight. A dev lead must review
> this: existing rows are moved out of the live table — a principal if the move cannot be undone or
> the volume is large. Create destination → migrate (batched) → verify counts. Prove it on a
> disposable copy before classifying.

## OutSystems phrasing
"archive old orders", "move the historical rows out to an archive table".

## SSDT meaning
This is a **data movement**, which SSDT does not express declaratively. The archive destination is
created declaratively; the row move is scripted in a post-deployment step, typically a batched
`DELETE ... OUTPUT DELETED.* INTO archive.X` to spare the transaction log. SSDT describes *shapes*,
not *data motion*.

## The named trap
Unbatched moves bloat the transaction log; child rows with FKs must move (or their FKs be disabled)
**before** parents; cross-database archives lose FK enforcement. The coexistence obligation (live +
archive both readable during the move) is the multi-phase concern — see
`../../_index/multi-phase/SKILL.md`; do not re-derive the coexistence shape here.

## How it flips (the specifics only)
- new archive table + scripted move → ships across releases (additive archive table → batched
  migrate → verify counts; see `../../_index/multi-phase/SKILL.md`), reviewed by a dev lead because
  it relocates existing data.
- large volume (>1M rows) → batching is mandatory and a principal should review it: at production
  row counts the move may block writes or run long, so schedule a window.
- source is CDC-enabled → the batched deletes generate capture rows that must be coordinated; added
  scrutiny (see `../../_index/cdc/SKILL.md`).
- active queries must see only live data during the move → the coexistence obligation keeps this
  staged across releases.

## Prove it
Prove **source-count + archive-count == original total** after the batched move (no rows dropped,
none duplicated) and that each batch commits (the log stays bounded) — this is the conservation
proof the multi-phase index owns. A Permissive run snapshots the before/after row hashes to prove
the moved rows are byte-identical in the archive. For the publish loop, see
`../../prove-on-dacpac/SKILL.md`; probes, `../../talk-to-local-sql/SKILL.md`.

## The verdict (to the developer)
You asked to archive the old rows — move them out of the live table into an archive. SSDT has no
declarative "move," so this ships in stages across more than one release: the archive table is
added first, then a batched post-deployment script moves the rows across, then the counts are
reconciled. On a disposable copy of Dev I proved the counts reconcile exactly — every row ends up
either still live or in the archive, none dropped and none duplicated — and each batch commits, so
the transaction log stays bounded. Because it's over a million rows, the move needs a maintenance
window on the real table, and a dev lead should review it since it relocates existing data. One
thing to settle: once these rows are in the archive, does anything still need to read them as if
they were live — a report, a screen, an export?

## The reasoning (in conversation)
The moment a request is about *moving data between tables* rather than *changing a table's shape*,
you've left the declarative world, and the proof changes with it: it becomes a conservation check —
does every row still exist somewhere afterward — rather than a schema diff (see
`../../_index/multi-phase/SKILL.md`). The failure this avoids is an unbatched move that silently
loses or doubles rows and looks identical in the schema, because the schema never described the
rows in the first place. The question that catches it up front: shape change, or data move?

## On the record
Fragments for the pull request (`../../author-pr/SKILL.md`), record register.

**Review & release**
- A dev lead must review this: existing rows are moved out of the live table. A principal must
  review this instead when the move cannot be undone (a cross-database archive loses FK enforcement)
  or the volume is large.
- Ships across releases: the archive table is added, then a batched post-deployment script moves the
  rows (`DELETE ... OUTPUT DELETED.* INTO archive.X`), then the counts are reconciled — so the
  running application keeps reading live data while the move is in flight.
- Added scrutiny, when it applies: at large volume (> 1M rows) the move may block writes or run long
  and batching is mandatory (schedule a window); a CDC-tracked source turns each batched delete into
  capture rows that must be coordinated (see `../../_index/cdc/SKILL.md`).

**Verification** — run in each environment after deployment
```sql
-- expect live_rows + archived_rows to equal the recorded pre-move total: no row lost, none doubled
SELECT
  (SELECT COUNT(*) FROM dbo.<source>)     AS live_rows,
  (SELECT COUNT(*) FROM archive.<table>)  AS archived_rows;
```

**Rollback**
Reversal is a reverse batched move from the archive back to the source. The moved rows are preserved
byte-identical in the archive (proven by the before/after row-hash snapshot), so the data itself is
recoverable; the reverse move is a scripted operation, not automatic, and where the archive lives in
another database FK enforcement was already lost on the archived copy.

**Not verified**
- Application impact — any report, screen, or export that reads the archived rows from the live
  source will now miss them. Whether application and reporting code expects those rows in the live
  table is not confirmed here (@app-owner).
- Other environments — Test, UAT, and Prod hold different row counts the disposable copy of Dev
  cannot see. Run the verification query before promotion.
- Production scale and timing — at production row counts the batched move may run long or block
  writes; the small copy proves the batches commit and the log stays bounded in shape, not the
  duration at scale.
- Reversibility — a cross-database archive loses FK enforcement, and the reverse move is not
  exercised on the copy; the forward move is all that was proven.
