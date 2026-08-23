# The developer conversation — make Email required

The two exchanges a make-mandatory change produces: the intake that scopes it, and the verdict
that explains it. Everything below is what the developer reads.

---

**Developer:**

> Make the Email field on Customer required — no customer should be missing an email.

---

**Intake:**

You asked to make the Email attribute on the Customer entity required. In SQL terms that is one
operation: the existing column dbo.Customer.Email changes from NULL to NOT NULL. The table
definition gets edited to that destination and SSDT works out the steps — no hand-written ALTER.

Three facts decide how this ships, and two of them are measurable, so I'll prove them on a
disposable copy of Dev rather than ask you to guess:

- Is Customer populated? Measured: 5 rows.
- Do existing rows break the new rule? Measured: 2 rows have no Email — Initech (Id 3) and
  Stark Industries (Id 5).
- Must old and new application code coexist through the release? That one is about your
  deployment, not the data — tell me if the app ships separately from the schema.

One question is genuinely yours, because it is a business decision and no database can answer it:
when Email becomes required, what should those two customers hold? A real address you can source,
or a placeholder until one exists?

*(No human answer was available for this run. The placeholder policy documented in the project's
pre-deployment script — `unknown+<Id>@example.invalid`, one distinct value per row — is adopted as
the assumed answer, and the pull request names it as unconfirmed. Replace it when the real answer
exists.)*

---

**The verdict, after proving the change on a disposable copy of Dev:**

You asked to make Email required. On a disposable copy of Dev, SSDT refused it. The guard it
generates checks whether Customer has any rows at all — not whether Email has blanks:

```sql
IF EXISTS (select top 1 1 from [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
```

placed before the ALTER. SSDT writes the whole deploy script up front, so it can't know the
backfill that runs at deploy time will have cleared the blanks — it blocks the moment the table
holds data. That isn't a bug; it's the engine assuming the worst about data it can't interpret.
And it held here: the backfill stamped both blank rows, the count read 0, and the publish was
still blocked with the column left nullable. On an empty table the same edit would have just
applied.

So with data in the table it can't ship as one publish — this pipeline (Azure DevOps → Octopus)
can't relax the data-loss guard. It ships as two releases. Release 1 keeps the model saying
optional and does the work in a pre-deployment script: it fills the two blank emails and runs the
`ALTER … NOT NULL` itself. Because the model still says optional, SSDT generates no data-loss step,
so the guard never fires — and the column comes out NOT NULL. Release 2 lets the model catch up to
NOT NULL; the database is already there, so SSDT generates nothing. It landed: the column is NOT
NULL, and an insert with a blank email now fails with error 515, which is what "required" means at
the database.

One catch you would have hit later: your project's own seed data still declared those two
customers with no email. Publishing the model change without fixing the seed failed at the very
next step with that same error 515 — so the seed rows were updated to the backfilled values too,
and they ship in Release 1. The rule of thumb: a data fix has to live at its source (the seed, the
pre-deployment script), or the next deploy undoes it or collides with it.

The mistake this avoided: trusting the clean blank-count as a green light. The count reaching
zero is necessary — the column can't tighten with blanks present — but it never clears SSDT's
block on a populated table, so "backfill it and the NOT NULL just lands" is a recipe that fails.
And there is no gate to relax on this estate; the honest shape is the two-release.

What ships: the pre-deployment backfill and ALTER and the corrected seed rows (Release 1), then
the edited Customer table definition (Release 2) — two releases. A dev lead must review it, because
existing data is modified (two rows backfilled). Two things still need you:

- Confirm the backfill value with whoever owns customer data — the placeholder is visible
  anywhere an email is shown or sent.
- Check the application never saves a customer without an email; from now on that write fails
  with error 515 instead of succeeding quietly.
