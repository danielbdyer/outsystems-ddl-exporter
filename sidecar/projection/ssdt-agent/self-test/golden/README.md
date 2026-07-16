# golden/ — exemplar output, produced by a live run

These files are not authored documentation; they are the **captured output of a real end-to-end
run** of this tree (2026-07-16, SQL Server 2022 in the warm container, sqlpackage 170.4.83.3,
isolated database `PG_GOLD_COL03_42f8c552`, torn down after). The case: the make-mandatory spine —
"Make the Email field on Customer required" against the default populated seed (two NULL Emails).

- `make-mandatory-pr.md` — the pull request body, per `skills/author-pr/SKILL.md`. What a reviewer
  reads. Every count, error text, and digest in it was observed on the disposable copy, including
  the row-presence guard verbatim, the backfill-is-not-sufficient proof, the non-atomic
  relaxed-publish failure in the post-deployment seed, and the enforcement + idempotency checks.
- `make-mandatory-conversation.md` — the developer-facing exchanges for the same change: the
  intake that scopes it and the verdict that explains it, per `THE_RECORD.md` §3.

Use them as the standard to imitate: a new surface (or a scored self-test run) should read like
these. If a future run of the same case produces materially different engine behavior (a changed
guard, a changed error), that is a finding about the tool version — re-prove and re-capture, and
stamp the new version, rather than editing these by hand.
