# golden/ — exemplar output, produced by a live run

These files are not authored documentation; they are the **captured output of a real end-to-end
run** of this tree (make-mandatory two-release re-proved 2026-08-22, SQL Server 2022 in the warm
container, sqlpackage 170.4.83.3, isolated databases `pg_mm` and `pg_mm_naive`, torn down after). The
case: the make-mandatory spine — "Make the Email field on Customer required" against the default
populated seed (two NULL Emails). This estate cannot relax the data-loss guard, so the exemplar is the
**two-release**.

- `make-mandatory-pr.md` — the pull request body a reviewer reads. Every count, error text, and digest
  in it was observed on the disposable copy, including the row-presence guard verbatim, the naive
  single-release block (`Msg 50000`, `pg_mm_naive`), the two-release landing (Release 1 pre-deploy +
  Release 2 no-op, content digest `1818783869` identical across both, `pg_mm`), the
  seed-must-ride-with-the-change proof (`Msg 515`), and the enforcement + idempotency checks.
- `make-mandatory-conversation.md` — the developer-facing exchanges for the same change: the
  intake that scopes it and the verdict that explains it, per `THE_RECORD.md` §3.

Use them as the standard to imitate: a new surface (or a scored self-test run) should read like
these. If a future run of the same case produces materially different engine behavior (a changed
guard, a changed error), that is a finding about the tool version — re-prove and re-capture, and
stamp the new version, rather than editing these by hand.
