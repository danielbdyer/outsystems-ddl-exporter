# OrderLine: enforce Quantity > 0 (a violating row blocks validation and leaves the check untrusted; conforming data lands trusted)

**In OutSystems** — You add a business rule that a data value must satisfy — "Quantity must be positive", "Total can't be negative" — the kind of rule you'd want enforced at the database, not just in a screen validation.
**In SSDT** — a `CONSTRAINT [CK_OrderLine_Quantity] CHECK ([Quantity] > 0)` is added to `Tables/dbo.OrderLine.sql`. The publish engine validates that rule against **every existing row** as it lands — and the data decides whether it can.

## Summary

You require `OrderLine.Quantity > 0`. A CHECK constraint is a **claim about the existing data**: SQL
Server validates it against every row when it is added. If a row already violates the rule, the
validation **fails** and the deployment is refused; if every row conforms, it lands clean and
**trusted**. Both faces were proven objectively against a Twin — a disposable SQL Server database
published from this estate and filled with real-shaped synthetic data — with a **production-faithful**
publish (`BlockOnPossibleDataLoss = true`, the deployment a real environment runs).

The important finding is *how* SSDT tries to add it, and what a failed attempt leaves behind. SSDT adds
the constraint **`WITH NOCHECK`** (skipping validation), then runs a separate **`WITH CHECK CHECK`** to
validate the existing rows. Over a violating row that second step fails (`Msg 547`) and the publish is
refused — but the constraint **lingers, untrusted** (`is_not_trusted = 1`): the optimizer ignores it
and the bad row is still there. So the honest path is **reconcile the data first, then let it validate
trusted** — never leave it at `WITH NOCHECK` (see the trust ladder in
`../skills/op/toggle-trust/SKILL.md` and `../skills/_index/constraint-is-a-claim/SKILL.md`). No work
item was provided with the request; attach one before merge.

## Review & release

- A dev lead or an experienced developer should review this: the running application must change to
  keep working — any write that violates the rule now fails on the check.
- **Conforming data** (every row already satisfies the rule) → ships as a single schema change, applied
  in place; the check validates the existing rows as it lands and stays **trusted**.
- **Violating rows present** → the deployment is blocked. It ships as **one release with a
  pre-deployment fix-up** that brings the violating rows into compliance *before* the `WITH CHECK CHECK`
  validation; then the check lands validated and trusted. A dev lead must review this, because existing
  data is modified.
- Do **not** ship it `WITH NOCHECK` to "get past" the block: an untrusted check protects nothing the
  optimizer will rely on, and the violating rows remain. `is_not_trusted = 0` is the end-state proof.
- Added scrutiny: first time this operation is proven on the Twin; at production row counts the
  validation may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.OrderLine.sql` | Adds `CONSTRAINT [CK_OrderLine_Quantity] CHECK ([Quantity] > 0)` to the table definition |

No renames (the refactorlog is unchanged). No index, view, or procedure changes.

## Data remediation

Probe first: `SELECT COUNT(*) FROM dbo.OrderLine WHERE NOT (Quantity > 0);`. If it returns more than 0,
the deployment will block at the validation step. The remedy is a pre-deployment fix-up that brings
those rows into compliance — correct them to a valid `Quantity`, or handle them another way — before
the check validates. If a prior attempt already left an **untrusted** constraint behind, drop it (or
re-run `WITH CHECK CHECK` after the fix-up) so the end state is trusted. The original values changed by
a fix-up must be recorded for a manual restore (named under Not verified).

On the proof substrate both legs were observed directly:
- With **1 of 25** rows set to `Quantity = 0`, the publish was **refused** at validation and the
  constraint was left **untrusted**.
- With **every** row conforming (`Quantity > 0`), the identical check **applied** clean and **trusted**.

## Deployment evidence — objective proof, production-faithful publish, 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, seeds each condition
in real-shaped data, and asserts the outcome under a **production-faithful** DacFx posture
(`BlockOnPossibleDataLoss = true`, no smart-defaults). DacFx is the same publish engine `sqlpackage`
wraps.

**Test:** `Twin.Tests.Integration.SamplePrTighteningTests+SamplePrTighteningTests.add-check: a violating row blocks the check, conforming data applies`

```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 m 51 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 (violating leg) — a violating row REFUSES the check at validation, and leaves it untrusted.**
`OrderLine` held **25 rows**, **1** of which violated `Quantity > 0`. The production-faithful publish
was refused. Verbatim from the run — the failing statement is the `WITH CHECK CHECK` re-validation, not
the `ADD`:

```
Could not deploy package.
Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 547, Level 16, State 0, Line 1 The ALTER TABLE statement conflicted with the CHECK constraint "CK_OrderLine_Quantity". The conflict occurred in database "twin", table "dbo.OrderLine", column 'Quantity'.
Error SQL72045: Script execution error.  The executed script:
ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [CK_OrderLine_Quantity];
```

The observed post-block state, captured from the live database:

```
after block: CK_OrderLine_Quantity exists=1, is_not_trusted=1 (added WITH NOCHECK, never validated -> untrusted)
```

The constraint was added `WITH NOCHECK` (so it exists) and the `WITH CHECK CHECK` validation then
failed on the violating row — leaving the check present but **untrusted** (`is_not_trusted = 1`). The
deployment did not achieve a trusted, enforced rule; a fix-up plus re-validation is required.

**Fact 2 (satisfying leg) — conforming data applies clean and trusted.** With every row satisfying
`Quantity > 0`, the identical check published successfully under the same production-faithful posture:

```
LEG B baseline: rows violating (Quantity > 0)=0 (all conform)
LEG B production publish (WITH CHECK over conforming data): APPLIED, CK_OrderLine_Quantity exists=1, is_not_trusted=0
```

`is_not_trusted = 0` — the check exists, is validated, and the optimizer honors it. Clean data lands in
place, trusted, in a single change.

## Verification — run in each environment after deployment

```sql
-- expect 0 rows: no row violates the predicate
SELECT COUNT(*) AS violations FROM dbo.OrderLine WHERE NOT (Quantity > 0);

-- expect is_not_trusted = 0: the check is trusted, so the optimizer honors it
SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'CK_OrderLine_Quantity';
```

## Rollback

The constraint drops without data loss:

```sql
ALTER TABLE dbo.OrderLine DROP CONSTRAINT CK_OrderLine_Quantity;
```

This is also the cleanup for an **untrusted** constraint left by a blocked attempt. A pre-deployment
fix-up UPDATE is not auto-reversed; the original values recorded under Data remediation are what a
manual restore uses. Backing the change out was not exercised.

## Not verified

- **Application impact.** Any code path that writes a value violating the rule now fails on the check
  ("conflicted with the CHECK constraint"); application-side validation is not confirmed here — the
  application owner owns it.
- **The fix-up values.** When violating rows exist, what they become is a data-owner decision, not made
  here.
- **Other environments.** Test, UAT, and Prod may hold violating rows the disposable copy cannot see.
  Run the violation probe in each before promotion.
- **Production scale and timing.** On a large table the `WITH CHECK CHECK` validation may block writes
  or run long; the small copy does not show it.
- **Reversibility.** Both legs are proven forward (the block-and-untrusted state, and the clean trusted
  apply); backing out a fix-up is not exercised here.
