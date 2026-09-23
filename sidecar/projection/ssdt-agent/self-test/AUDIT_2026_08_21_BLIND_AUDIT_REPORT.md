# Blind audit — the 41 operations against the success rubric (2026-08-21)

Independent adversarial review of `sidecar/projection/ssdt-agent/` (the 41 op skills +
`sample-prs/`), run from `self-test/blind-audit-prompt.md` by an agent that did not build the
tree. Method: read the four standard docs as claims, deep-read 23 op pairs spanning every
family, swept all 41 samples mechanically (section order/presence, register, person,
trap-names, internal citations, link resolution), cross-checked every number against
`proving-ground/Data/Seed.sql` and `Modules/*.sql`, and reproduced four of the boldest "What
proving showed" claims live on the warm SQL Server 2022 container (disposable DB
`audit_probe`, dropped after).

---

## Overall verdict

The tree does not yet meet its own bar, and the gap has a precise shape: **the hand-converged
families are genuinely excellent, and the law they obey has not been propagated to the layers
beneath and beside them.** Where an op was converged against a real publish — the keys five,
the tightening pair (make-mandatory / narrow), the drops, rename/move-schema, the additive
columns — the samples are seed-traceable to the row (`Customer 3 (Initech)`,
`STANDARD-SKU-001`, `Order 4 → CustomerId 999` with its 2 lines, the 2-order/4-line cascade
reach), the messages are verbatim and real, and every live reproduction attempted held (Msg
1505 `(<NULL>)`; the filtered index builds `has_filter = 1`; a new `NOT NULL DEFAULT` column
stamps all 5 existing rows while a DEFAULT on an existing column backfills none). The record
register also held where it is easiest to fail: all 41 samples carry all ten sections, the
record is agentless throughout, zero trap-names leaked, and only one cross-reference dangles
tree-wide. But the locked-gate axiom lives only in the op headers — the composed knowledge
layer those same ops route through (`tightening-class`, `prove-on-dacpac`,
`ask-the-developer`, `classify-mechanism`, `deploy-scripts`, `THE_RECORD.md` §8, even
make-mandatory's own "Prove it" step 4) still teaches the gate relaxation the estate cannot
perform; the authoritative standards (`THE_DECISION_TREE.md`, `FINDINGS_AND_CHANGES.md`) still
teach the overturned FK manual-trust step and route the now-proven drops to REFUSED; and the
static-data and structural families' samples assert where the converged families prove —
missing the decisive object entirely, citing receipts that cannot have run on the substrate,
and in one case (delete-seed-value) making a safety claim a live run disproves. The tree's
strongest property — proving is classifying — is real where it was exercised and cosmetic
where it was not.

## Best and worst

**Best (pass the reviewer test outright):**
- `sample-prs/rename-attribute.md` — both legs with verbatim messages, the loss shown under a
  labeled diagnostic run, a before/after digest (`1312825711`), and the refactorlog-deletion
  trap in the rollback. Nothing left to ask.
- `sample-prs/change-delete-rule.md` — the exact three generated statements, end-state
  `sys.foreign_keys` facts, and the cascade-reach receipt (2 Orders removed, 4 OrderLines
  orphaned) that matches the seed row-for-row.
- `sample-prs/delete-entity.md` — the phantom-removal proof (object_id `1061578820` surviving a
  "successful" publish), the re-create drift with the new object_id, and the correct
  one-release scripted shape — the hardest op family, fully reasoned. (Two minor slips, rows
  17 below.)

**Worst (fail the reviewer test):**
- `sample-prs/identity-swap.md` — its receipts are fabricated: the "incoming foreign keys
  (from `Order` and `OrderLine`)" do not exist anywhere in the substrate, and its check query
  reads a column `Order` does not have.
- `sample-prs/delete-seed-value.md` — the retired value is never named (`<valueId>`
  placeholders), and its verdict's safety claim is disproven by a live run on the substrate's
  actual shape.
- `sample-prs/create-static-seed.md` — a reference-data PR that never lists the reference data:
  no rows, no ids, no second-deploy receipt for the silence claim it ships on.

## Ranked findings

Severity: **blocker** = would send an agent or reviewer to a wrong or impossible shipping
shape; **major** = fails a rubric criterion outright; **minor** = real defect, contained.
Every finding is CONFIRMED against quoted text, the substrate, or a live run, except where
marked PLAUSIBLE.

| # | file / op | criterion | severity | the defect | evidence | suggested fix |
|---|---|---|---|---|---|---|
| 1 | `skills/op/make-mandatory/SKILL.md` | 6, 8 | blocker | The op's own "Prove it" procedure ends by offering the gate relaxation its own SHIP terminal forbids three lines up. CONFIRMED | Step 4: "Deliver the corrected verdict: (a) a named gate relaxation after proven-zero-NULL, or (b) the multi-phase path" — vs. line 18: "The old 'relax the gate for one publish' remedy is not available on this estate — do not offer it." | Rewrite step 4 to deliver the two-release verdict (F7) only. |
| 2 | `skills/_index/tightening-class`, `skills/prove-on-dacpac`, `skills/ask-the-developer`, `skills/classify-mechanism`, `skills/deploy-scripts`, `THE_RECORD.md` §8 | 6 | blocker | The entire composed layer the tightening ops route through ("do not re-derive… see tightening-class") still teaches targeted gate-relaxation as remedy (a) with no estate caveat, and never states the proven F4 two-release shape. CONFIRMED | tightening-class: "**(a) Targeted gate-relaxation.** … deliberately disable `BlockOnPossibleDataLoss` for this one targeted change"; ask-the-developer: "Relax the guard once, or stage it?"; prove-on-dacpac step 4: "(a) named gate-relaxation after…"; THE_RECORD.md ✓-exemplar: "the data-loss guard is relaxed for this one column". | Propagate the Part-1 axiom: replace remedy (a) with the F4 two-release on every listed surface. |
| 3 | `THE_DECISION_TREE.md:187`; `proving-ground/Modules/Order.sql:13-14` | 6 | blocker | The authoritative state machine still teaches the overturned F5 manual trust step, and the substrate's own Order.sql header teaches the hand-written `WITH NOCHECK` anti-pattern — FINDINGS §6.4's "no surface still teaches the manual trust step" is false. CONFIRMED | "a post-deploy `WITH CHECK CHECK` trusts the key (F5)"; "The proven remedy is the script path: add WITH NOCHECK -> reconcile (delete/repoint the orphan) -> WITH CHECK CHECK to re-trust." | Update both to the F9 law (declarative add auto-trusts; reconcile in pre-deploy). |
| 4 | `skills/op/delete-attribute`, `delete-entity` vs `THE_DECISION_TREE.md` + `FINDINGS_AND_CHANGES.md` Part 4/5 | 6 | blocker | Same request, opposite terminals by entry surface: both drop ops ship full proven guidance ("Proven live 2026-08-21 — this advances Part 5"), while the standards still route drops to REFUSED and say "Do not write drop guidance until then" — and the tree's stated reason ("the two-release trick does not transfer to a drop") is disproven by delete-attribute's own proof. CONFIRMED | FINDINGS Part 5: "Drops (delete-attribute, delete-entity) — still TO PROVE… SHIP routes these to REFUSED"; DECISION_TREE: "routes to REFUSED until its own pattern is proven". | Fold the two proven drop shapes into FINDINGS (new F-rows) and the S5 flex list. |
| 5 | `sample-prs/split-table.md`, `merge-tables.md`, `extract-to-lookup.md`, `skills/_index/multi-phase` | 6, 8 | blocker | Phase 3 claims the gate lifts on proof and never states how the drop actually ships: the gate is data-blind and never lifts (the tree's own tightening-class law), the real Phase-3 shapes are delete-attribute's two-release / delete-entity's scripted drop (never pointed to), and merge-tables' claim contradicts delete-entity's phantom-removal law (under the prod posture no declarative `DROP TABLE` is even generated). An agent reaching Phase 3 is stranded at a blocked publish holding a "license" no tool accepts. CONFIRMED | split-table: "R3's column drop is blocked under Strict until that hash-equality is proven — SSDT refuses to drop the old columns while it cannot see the values already arrived"; merge-tables: "`BlockOnPossibleDataLoss` blocks the `DROP TABLE` until the copy is proven". | State each Phase 3's real shipping shape by pointing at the proven drop ops; fix the "until… proven" mechanism sentence everywhere. |
| 6 | `sample-prs/identity-swap.md` | 4, 3 | major | Fabricated receipts: names "incoming foreign keys (from `Order` and `OrderLine`)" — no FK exists anywhere in the substrate, `Order` has no `CategoryId` column, and the check query `SELECT o.Id FROM dbo.[Order] o LEFT JOIN dbo.Category p ON o.CategoryId = p.Id` cannot run — yet the proving section says "(confirmed — not a no-op)". CONFIRMED vs `Modules/*.sql` | "drop and recreate the incoming foreign keys (from `Order` and `OrderLine`) around the rebuild" | Re-prove on a real shape (e.g. Product.CategoryId + a declared FK) and rewrite with actual receipts. |
| 7 | `skills/op/identity-swap/SKILL.md` | 8 | major | SHIP terminal "ACROSS MULTIPLE RELEASES" names no releases, no per-release content, no receipt of a split, and no reason DacFx's single-publish rebuild must split — an agent cannot author "How it ships" from it. CONFIRMED | "On a populated table with incoming FKs it stages across releases" — nothing anywhere says what R1/R2 contain. | Either prove the one-publish rebuild (likely ONE RELEASE) or name the releases and why. |
| 8 | `sample-prs/delete-seed-value.md` | 4, 1 | major | The verdict's safety claim is false on the substrate and contradicts the PR's own proving: `Product.CategoryId` deliberately has no FK ("FK to Category(Id) intentionally NOT declared"), and a live run of the hard DELETE **succeeded silently and orphaned the row** — nothing refused it. The retired value is also never named (`<valueId>` placeholders). CONFIRMED (live, DB `audit_probe`) | Verdict: "a hard DELETE of a referenced value is refused" — vs. its own proving: "it orphans every Product row pointing at the id". | State the truth (no FK ⇒ silent orphaning is the risk), name the retired row, quote no refusal that cannot happen. |
| 9 | `sample-prs/split-table.md` | 4 | major | The claimed proof cannot have run as written and carries zero receipts: the hash query reads `Line1, City, PostalCode FROM dbo.Customer` — columns `Customer.sql` does not have — and the proving section has no message, no count, no hash value. CONFIRMED | "Published to a throwaway copy on this branch… the source and new-table hashes over the moving columns **match**" (no values shown). | Re-run on a copy whose Customer actually holds the moving columns; paste the real hashes and counts. |
| 10 | `sample-prs/create-static-seed.md` (also `edit-seed.md`) | 1 | major | A reference-data PR that never shows the reference data: the seeded rows and their explicit ids — the only facts that decide the review — appear nowhere; the after-deploy check has no expected values. Forced reviewer question: "what rows and ids am I approving?" CONFIRMED | "The Category rows carry explicit ids. No existing data is touched — the table is new." (the rows are never listed). | List the seeded rows `(id, code, active)` in "The data" and the expected values in the check. |
| 11 | `sample-prs/create-static-seed.md` | 4 | major | The silence classification ships without its receipt and on a prior run: the op skill demands "deploy a SECOND time… assert 0 rows + identical hash", but the proving section reports no second deploy and cites precedent instead — exactly what author-pr forbids ("never dress precedent as this change's proof"). CONFIRMED | "a no-op redeploy **must** touch 0 rows and keep an identical hash… (`skills/_index/idempotent-seed`, Twin-proven)". | Run the redeploy on this branch and report "second publish: 0 rows affected, hash unchanged (0x…)". |
| 12 | Rollback sections: `narrow.md`, `make-mandatory.md`, `create-fk-orphan.md`, `add-check.md`, `add-unique.md`, `extract-to-lookup.md` + the matching skills | 3, 4 | major | The manual-restore path points at a record nothing creates: "the pre-deploy step's output" — the described pre-deploys are plain `UPDATE`/`DELETE` (the proving-ground scripts only `PRINT`), no OUTPUT clause or capture step is ever listed under "What changes", so a reviewer needing an original value would find nothing. CONFIRMED vs `Script.PreDeployment.sql` | narrow.md: "the original `STANDARD-SKU-001` lives in the Release 1 pre-deploy output for a manual restore"; create-fk-orphan.md: "they are in the pre-deploy step's output for the run that removed them". | Either record originals in the PR body (rename-attribute's pattern) or add a real OUTPUT/capture step to the script — then point at that. |
| 13 | 7 records: `add-default.md`, `modify-default.md`, `add-index.md`, `toggle-trust.md`, `create-static-seed.md`, `edit-seed.md`, `delete-seed-value.md` | 3 | major | Internal machinery leaks into the reviewer-facing record: F-numbers, `skills/_index/…` paths, `FINDINGS_AND_CHANGES.md`, "Twin-proven" — pointers a reviewer in Pune cannot open, violating "state the fact, not a pointer to it". CONFIRMED | toggle-trust.md: "already re-validates and trusts itself (F9/F10)"; add-index.md: "(`FINDINGS_AND_CHANGES.md` F11)"; create-static-seed.md: "(`skills/_index/idempotent-seed`, Twin-proven)". | Replace each citation with the fact it names; keep tree citations in the skills. |
| 14 | `sample-prs/retype-explicit.md` + `skills/op/retype-explicit` verdict | 4, 3 | major | Two samples contradict each other about the same object: retype-explicit says `Product.Code` = `100/200/400/500/30X` while narrow.md, add-unique.md and the shipped seed say `A100/B200/STANDARD-SKU-001/DUPE/DUPE`; the skill's verdict template additionally hardcodes "12 rows" that match nothing. CONFIRMED | "4 codes are whole numbers (`100`, `200`, `400`, `500`); 1 is not: Product 3, `30X`" — vs. Seed.sql Product 3 = `STANDARD-SKU-001`. | Re-prove retype on the real seed (or a named scratch variant, declared as such); make the skill's count a placeholder. |
| 15 | `sample-prs/edit-seed.md` | 3, 1 | minor | The worked instance stages `Refunded` on the wrong lookup and omits the one decisive fact: the substrate's own seed says Refunded belongs to **Status** ("Adding a new value here (e.g. (4, N'Refunded', 1))") while Categories are Hardware/Software/Service, and the new row's explicit id — the whole point of the explicit-id law — is never stated. CONFIRMED | Title: "Category: add 'Refunded' to the lookup"; "a new `WHEN NOT MATCHED THEN INSERT` row for `Refunded`" (no id anywhere). | Stage it on Status with the id (4) named in "What changes" and the check. |
| 16 | `THE_DECISION_TREE.md` S6 vs `THE_RECORD_FORMS.md` + samples | 8 | minor | Open-fork behavior is contradictory across the standards: S6 says "The PR cannot be written past an open fork" (BLOCKED_AWAITING_HUMAN), THE_RECORD_FORMS says "While it is open, the PR says so", and make-mandatory.md ships a placeholder fill value that is "not settled here" — an agent cannot tell whether to halt or emit. CONFIRMED | S6: "Unanswered → BLOCKED_AWAITING_HUMAN (terminal)." vs make-mandatory.md: "The fill value is the data owner's call… not settled here." | Decide one rule (e.g. emit-with-open-fork-flagged) and state it in both docs. |
| 17 | `sample-prs/delete-entity.md` | 3 | minor | Two residues in an otherwise exemplary PR: a two-release template line survives in a one-release change ("Confirm Release 1 landed… before promoting to the next"), and the rollback attributes the row count to a message that carries none. CONFIRMED | "The row count in the block message (8) records how many rows the drop removes" — the quoted Msg 50000 text contains no count. | Delete the Release-1 bullet; attribute the 8 to the pre-drop `COUNT(*)`. |
| 18 | `sample-prs/add-mandatory.md` vs `skills/op/add-optional` | 6 | minor | Contradictory claims about the same estate setting: add-optional says `IgnoreColumnOrder=True` makes position "a non-issue", add-mandatory says that even "with `IgnoreColumnOrder` on… Inserting it in the middle of the column list makes DacFx rebuild the whole table" — with the setting on, no rebuild occurs. CONFIRMED | add-mandatory.md "How it ships" bullet 2 and "Not checked" bullet 5. | Correct add-mandatory to match add-optional (rebuild only with the setting off). |
| 19 | Developer verdicts in `add-index`, `junction`, `rename-attribute`, `retype-explicit`, `widen` | 8 | minor | First-person agent voice against the tree's own converged form (FINDINGS §6.5: "no first-person agent voice") — including `widen`, the named exemplar. CONFIRMED | add-index: "I published it to a disposable copy of your data… we'd either run it online"; widen: "The one thing I checked was structural". | Recast as "On a disposable copy… the publish showed" per make-mandatory's verdict. |
| 20 | `skills/op/delete-attribute` verdict | 3 | minor | Invented count in the verdict template: "two views read it" — the proving ground contains no views, and §6.5 requires placeholders where a real run supplies the number. CONFIRMED | "the column still holds values and two views read it". | "`<N>` objects still reference it" with the referencing-entities probe named. |
| 21 | `sample-prs/extract-to-lookup.md` | 1 | minor | The PR never says which release it is or how many there are — "What changes" lists every phase (create, seed, FK, backfill, drop) in one PR, so a reviewer cannot tell what they are approving now. CONFIRMED | "It stages across releases: create the lookup, seed…, then drop the old text column" (no "first of N"). | Adopt split-table's form: "the first of N releases; this release ships X". |
| 22 | `sample-prs/create-fk-orphan.md` | 3 | minor | The flagship op's sample is the only one of 41 that breaks the "fixed order" (The data before How it ships), omits the required work-item line its clean twin carries, and drops 2 of its skill's 4 standing Not-verified items (app-impact 547, trust-on-build-config). CONFIRMED (scripted sweep) | Section heads: Verdict · Intent · What changes · Before promoting · **The data** · **How it ships** · … | Reorder, add the work-item line, carry the standing items. |
| 23 | `skills/_index/tightening-class` (Optimistic-NOT-NULL note) | 4 | minor | Right law, wrong receipt: cites `sample-prs/add-default.md` (+ stale "DacFx 162.5.57") as proof that "an explicit DEFAULT stamps every existing row" — that law is add-**mandatory**'s, and add-default.md itself says so ("that is `add-mandatory`, a different op"). Substance verified live. CONFIRMED | "(proven: `../../../sample-prs/add-default.md`, DacFx 162.5.57)". | Point at `add-mandatory.md` and the current stack. |
| 24 | `THE_RECORD_FORMS.md:109` | 8 | minor | The only dangling reference tree-wide: `skills/_index/remediation-adds-no-schema` does not exist (the invariant lives unnamed in THE_DECISION_TREE and multi-phase). CONFIRMED (link sweep) | "(`skills/_index/remediation-adds-no-schema`; `skills/ask-the-developer`)". | Point at the surfaces that own it, or create the index file. |

## Live verification record

Run on the warm container (`projection-mssql-warm`, SQL Server 2022), disposable DB
`audit_probe`, dropped after:

- **Held:** plain unique index over two NULLs → `Msg 1505 … The duplicate key value is
  (<NULL>)` verbatim; filtered `WHERE Email IS NOT NULL` → builds, `is_unique = 1`,
  `has_filter = 1` (add-unique.md, exact).
- **Held:** `ADD Segment NVARCHAR(20) NOT NULL CONSTRAINT … DEFAULT (N'Standard')` → all 5
  existing rows stamped (add-mandatory.md); DEFAULT added to existing nullable `Email` → both
  NULLs untouched, fresh insert filled (add-default.md). Both laws exact.
- **Failed:** delete-seed-value.md's verdict — hard DELETE of a referenced Category on the
  substrate's shape (no FK) succeeded silently and orphaned the referencing row; only with a
  declared FK does `Msg 547` refuse it (finding 8).
