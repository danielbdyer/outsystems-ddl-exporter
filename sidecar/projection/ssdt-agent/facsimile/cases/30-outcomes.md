# 30 · Outcomes, tier and record — `Get-FxOutcome`, `Get-FxTier`, `Get-FxRecord`

*Three pure functions (ARCH §3, §6, §7; skills/author-pr, THE_RECORD_FORMS.md). Input sub-fields: `output` / `build` (sqlpackage or build text), `delta` (the batch map Get-FxQuestions made: guard batches, regions), `questions`, `answers`, `config`, `inflight`, `platform`, `verdict`, `replan` (head scripted against the copy after a block), `stamps`. A `record` line asserts only the items it states.*
*"Scratch run" = an output layout seen once on 2026-10-18 (sqlpackage 170.5.96, SQL Server 2022, the proving ground): `verify` until the first implementation run records it. Msg numbers carry their own receipts. Phases are named as H6 names them (pre-deploy, main, post-deploy, validation); any error inside a pre- or post-deploy batch is `blocked authored · Msg <n> · <pre-deploy|post-deploy>` (H5). A permissive leg's output is classified like any other and recorded beside the Strict verdict (CLS-55).*

---

## CLS · Get-FxOutcome: what the publish said

### CLS-01 · a clean publish
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Publishing to database 'fx_add_middle_name_dev_k2p' on server '(localdb)\MSSQLLocalDB'.
                     Initializing deployment (Start)
                     Initializing deployment (Complete)
                     Analyzing deployment plan (Start)
                     Analyzing deployment plan (Complete)
                     Updating database (Start)
                     Altering Table [dbo].[Customer]...
                     Update complete.
                     Updating database (Complete)
                     Successfully published database.
                     Time elapsed 0:00:04.19
expect:   outcome    verdict clean · exit 0
laws:     H1 H2 A3
source:   receipt · F14 (proven through scripts/prove.mjs, whose classifier reads "Successfully published database")
note:     Get-FxOutcome decides the publish only. H2's other conditions (answers, acceptance, identity, no unknown
          statement) are earlier steps, each a stop before the publish runs.
```

### CLS-02 · a data-loss warning on a clean publish is attached, not a block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Initializing deployment (Start)
                     *** The column [dbo].[Product].[LegacyCode] is being dropped, data loss could occur.
                     Initializing deployment (Complete)
                     Updating database (Start)
                     Altering Table [dbo].[Product]...
                     Update complete.
                     Updating database (Complete)
                     Successfully published database.
expect:   outcome    verdict clean · exit 0
          record     warnings {"The column [dbo].[Product].[LegacyCode] is being dropped, data loss could occur."}
laws:     H1 H2
source:   verify · the `***` warning line during Initializing was seen in a scratch run (on a blocked publish); that a clean publish prints it the same way is unconfirmed
note:     An empty Product passes its guard, so the drop applies; the warning still reaches the record.
```

### CLS-03 · "Successfully generated script" is not a publish
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Generating publish script for database 'bd_probe'.
                     Successfully generated script to file C:\fx\proofs\make-email-required\delta.Dev.sql.
                     Time elapsed 0:00:03.61
expect:   outcome    stop unclassified · "The publish ended with 'Successfully generated script to file …' that no rule classifies. Add a rule with its case." · exit 9
laws:     H2 H5 A3
source:   receipt · TWIN_SUCCESSION §6b boundary experiment (the offline Script prints exactly these lines)
```

### CLS-04 · the data-loss guard, attributed by evidence
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Publishing to database 'fx_make_email_required_dev_7q2' on server '(localdb)\MSSQLLocalDB'.
                     Initializing deployment (Start)
                     *** The column Email on table [dbo].[Customer] must be changed from NULL to NOT NULL. If the table contains data, the ALTER script may not work. To avoid this issue, you must add values to this column for all rows or mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
                     Initializing deployment (Complete)
                     Analyzing deployment plan (Start)
                     Analyzing deployment plan (Complete)
                     Updating database (Start)
                     Pre-deploy complete.
                     An error occurred while the batch was being executed.
                     Updating database (Failed)
                     *** Could not deploy package.
                     Warning SQL72016: The column Email on table [dbo].[Customer] must be changed from NULL to NOT NULL. If the table contains data, the ALTER script may not work. To avoid this issue, you must add values to this column for all rows or mark it as allowing NULL values, or enable the generation of smart-defaults as a deployment option.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
          delta      guard batch dbo.Customer (a 3-line comment and a blank line, then the IF; RAISERROR on its line 6)
                     · region pre Script.PreDeployment.sql
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     phase main · warnings {SQL72016} · messages {50000}
laws:     H5 H1 A3
source:   verify · the layout, including SQL72045 re-formatting the batch, was seen in a scratch run; the block itself is receipt F7 / F13
note:     SQL72045 re-formats what it quotes (keywords upper-cased, lines re-broken, `;` added), so the match is token for
          token with keywords folded. SQL72014's `Line 6` must also be that batch's RAISERROR line. The line ending
          after "The executed script:" was a CR in the scratch run; it is whitespace.
```

### CLS-05 · the provider prefix is not part of any rule
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Framework Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
          delta      as CLS-04
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     H5
source:   verify · "Framework …" is how scripts/prove.mjs's fixture and sample-prs quote it; the scratch run printed "Core …"
```

### CLS-06 · a counterfeit guard in the pre-deploy is the authored script's refusal
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     An error occurred while the batch was being executed.
                     Updating database (Failed)
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 92 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
                     GO
                     PRINT 'Pre-deploy: no backfill active. Uncomment a worked example when proving a change.';
          delta      no guard batches (the change adds FK_Order_Customer)
                     · region pre Script.PreDeployment.sql (its first batch: an 89-line comment, then the IF; RAISERROR on line 92)
expect:   outcome    verdict blocked authored · Msg 50000/127 · pre-deploy · exit 3
          record     phase pre-deploy
laws:     H5 H1
source:   verify · red-team #12 (the counterfeit; not ✓); this output was seen in a scratch run
note:     SQL72045 quotes a pre- or post-deploy failure as the whole authored script from its first statement,
          re-formatted, so the quote runs on past the RAISERROR. No pre-deploy PRINT precedes the error. Nothing here
          matches a guard batch of this delta, and any error inside an authored batch is `blocked authored` (H5).
```

### CLS-07 · in a batch holding several guards, SQL72014's line picks the table
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 4 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Product])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
          delta      one guard batch: dbo.Customer (RAISERROR on its line 2), dbo.Product (RAISERROR on its line 4)
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
laws:     H5
source:   verify · whether DacFx puts several guards in one batch (QST-08), and that Line counts lines of the batch as the delta holds it
```

### CLS-08 · counterfeit text identical to a real guard: the line tells them apart
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 2 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
          delta      guard batch dbo.Customer (RAISERROR on its line 6)
                     · region pre Script.PreDeployment.sql, whose only statement is the same guard text (RAISERROR on its line 2)
expect:   outcome    verdict blocked authored · Msg 50000/127 · pre-deploy · exit 3
          record     phase pre-deploy
laws:     H5
source:   expected · red-team #12; the line rule is the one CLS-04 states
```

### CLS-09 · identical text and identical line: no attribution
```case
kind:     pure
action:   Get-FxOutcome
input:    output     as CLS-08, but SQL72014 reads `Line 2`
          delta      guard batch dbo.Customer (RAISERROR on its line 2) · region pre Script.PreDeployment.sql with the
                     same two lines as its only statement, and no PRINT in either region
expect:   outcome    stop unclassified · "The publish ended with Msg 50000/127 that no rule classifies. Add a rule with its case." · exit 9
laws:     H5 H2
source:   expected · H5: attribution only by evidence; two batches match, so neither is chosen
```

### CLS-10 · Msg 50000/127 with no SQL72045 cannot be attributed
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
          delta      as CLS-04
expect:   outcome    stop unclassified · "The publish ended with Msg 50000/127 that no rule classifies…" · exit 9
laws:     H5 H2
source:   expected · H5 (the guard is named only when the quoted batch is a guard batch of this delta)
```

### CLS-11 · a quoted guard for a table this delta does not guard
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Product])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
          delta      guard batch dbo.Customer only; no authored region holds this text
expect:   outcome    stop unclassified · "The publish ended with Msg 50000/127 that no rule classifies…" · exit 9
laws:     H5
source:   expected · the output does not belong to this delta (a stale delta or a foreign script)
```

### CLS-12 · a remedy's own THROW is the authored script's refusal
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     An error occurred while the batch was being executed.
                     Updating database (Failed)
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50001, Level 16, State 1, Line 7 Pre-deploy: an Email shape this script does not handle is present; nothing was written.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT 1
                                FROM   dbo.Customer
                                WHERE  Email = N'N/A')
                         THROW 50001, N'Pre-deploy: an Email shape this script does not handle is present; nothing was written.', 1;
          delta      region pre Script.PreDeployment.sql (its first statement is that IF EXISTS … THROW)
expect:   outcome    verdict blocked authored · Msg 50001 · pre-deploy · exit 3
          record     phase pre-deploy
laws:     H5 A3
source:   expected · TWIN_SUCCESSION §6b ("the pre-deploy script begins with its confirmation probe and THROWs if it finds a shape it does not handle")
note:     This is the self-check doing its job; the record states the refusal, and nothing was written (F6 still applies to
          any earlier statement).
```

### CLS-13 · a RAISERROR in the post-deploy is the authored script's refusal
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     Update complete.
                     An error occurred while the batch was being executed.
                     Updating database (Failed)
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 1, Line 4 Seed: Category 4 is held by another Code in this database.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (its first statement is SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 50000 · post-deploy · exit 3
          record     phase post-deploy
laws:     H5
source:   expected · H5 ("any error raised inside a pre- or post-deploy batch, whatever its number, is blocked authored")
```

### CLS-14 · Msg 547 on a foreign key at validation, after the post-deploy
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     Pre-deploy: no backfill active. Uncomment a worked example when proving a change.
                     Creating Foreign Key [dbo].[FK_Order_Customer]...
                     The transacted portion of the database update succeeded.
                     Seed applied. Flip scenarios: NULL emails (3,5), over-length+dupe Code (3,4,5), orphan order (4).
                     Post-deploy complete: seed MERGEs applied (idempotent).
                     Checking existing data against newly created constraints
                     An error occurred while the batch was being executed.
                     Updating database (Failed)
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 547, Level 16, State 0, Line 1 The ALTER TABLE statement conflicted with the FOREIGN KEY constraint "FK_Order_Customer". The conflict occurred in database "fx_create_fk_dev_3k1", table "dbo.Customer", column 'Id'.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
expect:   outcome    verdict blocked constraint · Msg 547 · FK_Order_Customer · exit 3
          record     phase validation
laws:     H5 H6 A3
source:   verify · the phase order (validation after the post-deploy, outside the transacted portion) was observed 2026-10-18 on sqlpackage 170.5.96 and is pinned per version by INV-H6-01; the 547 itself is receipt F9 create-fk-orphan (the key left present but untrusted)
note:     Validation runs after the post-deploy, so main and post-deploy work stay applied (REC-19), in the order H6
          lists: pre-deploy, main, post-deploy, validation.
```

### CLS-15 · Msg 547 on a CHECK names the check
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Checking existing data against newly created constraints
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 547, Level 16, State 0, Line 1 The ALTER TABLE statement conflicted with the CHECK constraint "CK_Order_Total". The conflict occurred in database "fx_add_check_dev_q8w", table "dbo.Order", column 'Total'.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [CK_Order_Total];
expect:   outcome    verdict blocked constraint · Msg 547 · CK_Order_Total · exit 3
          record     phase validation
laws:     H5
source:   receipt · F10 add-check (BLOCK Msg 547, "the ALTER TABLE statement conflicted with the CHECK constraint … column 'Total'")
```

### CLS-16 · Msg 547 from a post-deploy insert is the authored script's block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     The transacted portion of the database update succeeded.
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 547, Level 16, State 0, Line 2 The INSERT statement conflicted with the FOREIGN KEY constraint "FK_Product_Category". The conflict occurred in database "fx_edit_seed_dev_p0c", table "dbo.Category", column 'Id'.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 547 · post-deploy · exit 3
          record     phase post-deploy · messages {547: FK_Product_Category}
laws:     H5
source:   expected · H5 (any number inside an authored batch); the 547 wording for an INSERT; the quote shape is CLS-28's
note:     The same number in the validation phase is `blocked constraint` (CLS-14): the phase, not the number, decides.
```

### CLS-17 · Msg 515 from the post-deploy seed after a tightening is the authored script's block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     The transacted portion of the database update succeeded.
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 515, Level 16, State 2, Line 9 Cannot insert the value NULL into column 'Email', table 'fx_make_email_required_dev_7q2.dbo.Customer'; column does not allow nulls. UPDATE fails.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 515 · post-deploy · exit 3
          record     phase post-deploy · messages {515: dbo.Customer.Email}
laws:     H5
source:   expected · H5; sample-prs/make-mandatory.md quotes this Msg 515 from a live run (not a listed receipt)
```

### CLS-18 · Msg 1505 on CREATE UNIQUE INDEX: one message, no 1750
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     Pre-deploy: no backfill active. Uncomment a worked example when proving a change.
                     Creating Index [dbo].[Product].[UX_Product_Code]...
                     An error occurred while the batch was being executed.
                     Updating database (Failed)
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 1505, Level 16, State 1, Line 1 The CREATE UNIQUE INDEX statement terminated because a duplicate key was found for the object name 'dbo.Product' and the index name 'UX_Product_Code'. The duplicate key value is (wdup).
                     Error SQL72045: Script execution error.  The executed script:
                     CREATE UNIQUE NONCLUSTERED INDEX [UX_Product_Code]
                         ON [dbo].[Product]([Code] ASC);
expect:   outcome    verdict blocked unique · Msg 1505 · UX_Product_Code · exit 3
          record     phase main
laws:     H5 A3
source:   receipt · F10 (a duplicate → Msg 1505 naming the value) · SamplePr "add-unique: duplicate values block the unique index, a unique column applies"
note:     The value in the text is the copy's witness (w-prefixed, P5), never an environment's. The single SQL72014 line
          with no 1750 was seen in a scratch run.
```

### CLS-19 · Msg 1505 then 1750 on ADD CONSTRAINT PRIMARY KEY: the first decides, both are recorded
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 1505, Level 16, State 1, Line 1 The CREATE UNIQUE INDEX statement terminated because a duplicate key was found for the object name 'dbo.OrderLine' and the index name 'PK_OrderLine'. The duplicate key value is (1, 1).
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 1750, Level 16, State 0, Line 1 Could not create constraint or index. See previous errors.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[OrderLine]
                         ADD CONSTRAINT [PK_OrderLine] PRIMARY KEY CLUSTERED ([OrderId] ASC, [LineNumber] ASC);
expect:   outcome    verdict blocked unique · Msg 1505 · PK_OrderLine · exit 3
          record     messages {1505, 1750}
laws:     H5
source:   verify · whether sqlpackage prints 1750 as its own SQL72014 line (sample-prs/define-pk.md saw 1505 then 1750 from hand-run T-SQL)
```

### CLS-20 · Msg 2601 on a post-deploy insert is the authored script's block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 2601, Level 14, State 1, Line 3 Cannot insert duplicate key row in object 'dbo.Category' with unique index 'UX_Category_Code'. The duplicate key value is (HW).
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 2601 · post-deploy · exit 3
          record     phase post-deploy · messages {2601: UX_Category_Code}
laws:     H5
source:   expected · H5; ARCH §6 keeps 2601 and 2627 as `blocked unique` only outside an authored batch (CLS-64)
```

### CLS-21 · Msg 2627 from a seed whose Id the environment holds under another Code
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 2627, Level 14, State 1, Line 5 Violation of PRIMARY KEY constraint 'PK_Category_Id'. Cannot insert duplicate key in object 'dbo.Category'. The duplicate key value is (4).
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 2627 · post-deploy · exit 3
          record     phase post-deploy · messages {2627: PK_Category_Id}
laws:     H5
source:   expected · ARCH §6 (the seed's 2627 is listed under blocked authored); red-team #15 (seed Ids differ per environment, causing 2627; not ✓)
```

### CLS-22 · Msg 2628 in the post-deploy is the authored script's block, naming the table and column
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 2628, Level 16, State 1, Line 3 String or binary data would be truncated in table 'fx_narrow_code_dev_r4t.dbo.Product', column 'Code'. Truncated value: 'wSTANDARD-'.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 2628 · post-deploy · exit 3
          record     phase post-deploy · messages {2628: dbo.Product.Code}
laws:     H5
source:   expected · H5; sample-prs/narrow.md quotes Msg 2628 from a live run (the seed still writing the long value)
note:     The truncated value in the text is the copy's witness (P5); the record keeps the number and the coordinate,
          never the value.
```

### CLS-23 · Msg 8152 (no column named) takes the object from the quoted batch
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 8152, Level 16, State 30, Line 1 String or binary data would be truncated.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Product] ALTER COLUMN [Code] NVARCHAR (10) NOT NULL;
expect:   outcome    verdict blocked truncation · Msg 8152 · dbo.Product.Code · exit 3
laws:     H5 F6
source:   expected · 8152 is the pre-2019 / compatibility-level wording of 2628; the permissive leg differs from Strict only in BlockOnPossibleDataLoss (F6)
note:     Get-FxOutcome classifies a permissive leg's output like any other; the shell records the result as the
          consequence beside the Strict verdict, never in its place (CLS-55).
```

### CLS-24 · Msg 8115 is overflow
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 8115, Level 16, State 8, Line 1 Arithmetic overflow error converting numeric to data type numeric.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [Amount] DECIMAL (9, 2) NOT NULL;
expect:   outcome    verdict blocked overflow · Msg 8115 · dbo.OrderLine.Amount · exit 3
laws:     H5
source:   expected · ARCH §6
note:     A permissive leg's classification, recorded beside the Strict verdict (CLS-55).
```

### CLS-25 · Msg 245 from a PERSISTED computed column, on the Strict leg (no guard)
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Altering Table [dbo].[T]...
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 245, Level 16, State 1, Line 1 Conversion failed when converting the varchar value 'wABC' to data type int.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[T]
                         ADD [RefNum] AS (CONVERT (INT, [Ref])) PERSISTED;
expect:   outcome    verdict blocked conversion · Msg 245 · dbo.T.RefNum · exit 3
          record     phase main
laws:     H5
source:   expected · red-team #3 (the PERSISTED computed column reads data with no guard ✓); 245 is the documented conversion error
```

### CLS-26 · Msg 241 and Msg 8114 are conversion too
```case
kind:     pure
action:   Get-FxOutcome (permissive leg), run once per output
input:    output     (a) Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 241, Level 16, State 1, Line 1 Conversion failed when converting date and/or time from character string.
                         Error SQL72045: Script execution error.  The executed script:
                         ALTER TABLE [dbo].[Customer] ALTER COLUMN [BirthText] DATE NULL;
                     (b) Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 8114, Level 16, State 5, Line 1 Error converting data type nvarchar to numeric.
                         Error SQL72045: Script execution error.  The executed script:
                         ALTER TABLE [dbo].[T] ALTER COLUMN [Ref] DECIMAL (9, 2) NULL;
expect:   outcome    (a) verdict blocked conversion · Msg 241 · dbo.Customer.BirthText · exit 3
                     (b) verdict blocked conversion · Msg 8114 · dbo.T.Ref · exit 3
laws:     H5
source:   expected · ARCH §6
note:     Permissive legs' classifications, recorded beside the Strict verdict (CLS-55).
```

### CLS-27 · Msg 544 in the post-deploy after an identity change is the authored script's block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Starting rebuilding table [dbo].[Category]...
                     The transacted portion of the database update succeeded.
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 544, Level 16, State 1, Line 2 Cannot insert explicit value for identity column in table 'Category' when IDENTITY_INSERT is set to OFF.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 544 · post-deploy · exit 3
          record     phase post-deploy
laws:     H5 A3
source:   receipt · F12 identity-swap (the post-deploy seed's explicit-id insert fails Msg 544 until bracketed with SET IDENTITY_INSERT)
```

### CLS-28 · Msg 207 in the post-deploy: the quote is the script's beginning, not the failing statement
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     Pre-deploy: no backfill active. Uncomment a worked example when proving a change.
                     Seed applied. Flip scenarios: NULL emails (3,5), over-length+dupe Code (3,4,5), orphan order (4).
                     An error occurred while the batch was being executed.
                     Updating database (Failed)
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 207, Level 16, State 1, Line 2 Invalid column name 'LegacyPhone'.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
                     GO
                     MERGE INTO dbo.Status
                      AS target
                     USING (VALUES (1, N'Pending', 1), (2, N'Shipped', 1), (3, N'Cancelled', 1)) AS source(Id, Code, IsActive) ON target.Id = source.Id
                     WHEN MATCHED AND (target.Code <> source.Code
                                       OR target.IsActive <> source.IsActive) THEN UPDATE
                     SET target.Code     = source.Code,
                         target.IsActive = source.IsActive
                     WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code, IsActive) VALUES (source.Id, sou
          delta      region post Script.PostDeployment.sql (with `:r .\Data\Seed.sql`; first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 207 · post-deploy · exit 3
          record     phase post-deploy
laws:     H5
source:   receipt · F15 (b) (a seed still naming a dropped column failed the publish with Msg 207); the layout was seen in a scratch run
note:     SQL72045 quotes the whole post-deploy from its first statement, re-formatted and cut at about 1 KB, so the
          failing statement need not appear. The phase comes from the quote's start and the post-deploy's PRINT output.
```

### CLS-29 · Msg 207 in the pre-deploy is the authored script's block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 207, Level 16, State 1, Line 3 Invalid column name 'StatusText'.
                     Error SQL72045: Script execution error.  The executed script:
                     UPDATE o
                     SET    StatusId = s.Id
          delta      region pre Script.PreDeployment.sql (first statement UPDATE o …)
expect:   outcome    verdict blocked authored · Msg 207 · pre-deploy · exit 3
          record     phase pre-deploy
laws:     H5
source:   receipt · F16 (the phase-bound reconcile block read the column the contract release drops: Msg 207 before anything else ran)
```

### CLS-30 · Msg 8672 in a seed MERGE is the authored script's block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 8672, Level 16, State 1, Line 3 The MERGE statement attempted to UPDATE or DELETE the same row more than once. This happens when a target row matches more than one source row. A MERGE statement cannot UPDATE/DELETE the same row of the target table multiple times. Refine the ON clause to ensure a target row matches at most one source row, or use the GROUP BY clause to group the source rows.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 8672 · post-deploy · exit 3
          record     phase post-deploy
laws:     H5
source:   expected · ARCH §6 (the seed's 8672 is listed under blocked authored)
```

### CLS-31 · Msg 1946 is a key-length block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Creating Index [dbo].[N].[IX_N_Big]...
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 1946, Level 16, State 3, Line 1 Operation failed. The index entry of length 1800 bytes for the index 'IX_N_Big' exceeds the maximum length of 1700 bytes for nonclustered indexes.
                     Error SQL72045: Script execution error.  The executed script:
                     CREATE NONCLUSTERED INDEX [IX_N_Big]
                         ON [dbo].[N]([Big] ASC);
expect:   outcome    verdict blocked keylength · Msg 1946 · IX_N_Big · exit 3
          record     phase main
laws:     H5
source:   receipt · red-team #16 (1946 fails on a key over the limit ✓)
```

### CLS-32 · Msg 1945 alone is a warning on a clean publish
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     Creating Index [dbo].[N].[IX_N_Big]...
                     Warning! The maximum key length for a nonclustered index is 1700 bytes. The index 'IX_N_Big' has maximum length of 2000 bytes. For some combination of large values, the insert/update operation will fail.
                     Update complete.
                     Updating database (Complete)
                     Successfully published database.
expect:   outcome    verdict clean · exit 0
          record     warnings {1945: IX_N_Big maximum key 2000 bytes}
laws:     H1 H2
source:   receipt · red-team #16 (Msg 1945 warns now, 1946 fails later ✓); how sqlpackage prints the informational line is verify
note:     The record carries it as a latent block for a later insert (REC-35).
```

### CLS-33 · Msg 4928 on sp_rename of a CDC-tracked column
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Rename [dbo].[C].[Code] to CodeX
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 4928, Level 16, State 1, Procedure sp_rename, Line 1 Cannot alter column 'Code' because it is 'REPLICATED'.
                     Error SQL72045: Script execution error.  The executed script:
                     EXECUTE sp_rename @objname = N'[dbo].[C].[Code]', @newname = N'CodeX', @objtype = N'COLUMN';
expect:   outcome    verdict blocked cdc · Msg 4928 · dbo.C.Code · exit 3
          record     phase main
laws:     H5
source:   receipt · red-team #6 (4928 on sp_rename ✓); the message wording is verify
note:     Only on a server with CDC (a configured CI server, or the oracle arm). On LocalDB a tracked table stops `cdc`
          before any publish (TIER-17).
```

### CLS-34 · Msg 4711 on TRUNCATE in an authored script is the authored script's block
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 4711, Level 16, State 1, Line 1 Cannot truncate table 'dbo.C' because it is published for replication or enabled for Change Data Capture.
                     Error SQL72045: Script execution error.  The executed script:
                     TRUNCATE TABLE dbo.C;
          delta      region pre Script.PreDeployment.sql (first statement TRUNCATE TABLE dbo.C;)
expect:   outcome    verdict blocked authored · Msg 4711 · pre-deploy · exit 3
          record     phase pre-deploy · flags {cdc}
laws:     H5
source:   expected · H5 ("whatever its number"); the engine message is red-team #6 (4711 on TRUNCATE ✓)
note:     Only on a server with CDC (a CI server or the oracle arm); on LocalDB the tracked table stops `cdc` before any
          publish. ARCH §6 also lists 4711 under `blocked cdc`, which H5 leaves to a generated statement (reported).
```

### CLS-35 · Msg 468 from a #temp join in a pre-deploy: authored when the collations match, platform when they do not
```case
kind:     pure
action:   Get-FxOutcome, once per platform
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 468, Level 16, State 9, Line 3 Cannot resolve the collation conflict between "Latin1_General_CS_AS" and "SQL_Latin1_General_CP1_CI_AS" in the equal to operation.
                     Error SQL72045: Script execution error.  The executed script:
                     CREATE TABLE #map (
                         Code NVARCHAR (50));
          delta      region pre Script.PreDeployment.sql (first statement CREATE TABLE #map …)
          platform   (a) the copy's server collation SQL_Latin1_General_CP1_CI_AS equals UAT's
                     (b) the copy's SQL_Latin1_General_CP1_CI_AS against UAT's Latin1_General_CI_AS
expect:   outcome    (a) verdict blocked authored · Msg 468 · pre-deploy · exit 3
                     (b) stop platform · "tempdb collation SQL_Latin1_General_CP1_CI_AS differs from UAT's
                     Latin1_General_CI_AS and the script compares #temp columns. <remedy>." · exit 6
          record     (a) phase pre-deploy
laws:     H5 S4 H3
source:   expected · ARCH §6 (468 is a finding only when the copy's server collation equals the environment's; any
          error inside an authored batch is blocked authored); the engine message is red-team #13 (#temp joins hit Msg
          468 against the server collation ✓)
note:     In (b) the copy's tempdb is not UAT's, so the 468 says nothing about UAT. A main-phase 468 is
          `blocked collation` (CLS-61).
```

### CLS-36 · SQL71516 at build is refused-at-build
```case
kind:     pure
action:   Get-FxOutcome (build)
input:    build      C:\estate\Modules\OrderLine.sql(14,5,14,5): Build error SQL71516: The referenced table '[dbo].[Order]' contains no primary or candidate keys that match the referencing column list in the foreign key. If the referenced column is a computed column, it should be persisted. [C:\estate\Estate.sqlproj]
                     Build FAILED.
expect:   outcome    verdict refused-at-build · SQL71516 · Modules/OrderLine.sql · exit 3
laws:     H1 A3
source:   receipt · F18 (a referenced PK drop fails the build with SQL71516 naming the referencing file; no dacpac, no delta, no publish); the wording is verify
```

### CLS-37 · any other SQL7xxxx or parser error at build is build-failed
```case
kind:     pure
action:   Get-FxOutcome (build), run once per output
input:    build      (a) C:\estate\Modules\vCustomer.sql(3,8,3,8): Build error SQL71501: View: [dbo].[vCustomer] has an unresolved reference to object [dbo].[Customer].[Phone]. [C:\estate\Estate.sqlproj]
                     (b) C:\estate\Modules\Customer.sql(9,1,9,1): Build error SQL46010: Incorrect syntax near ,. [C:\estate\Estate.sqlproj]
                     each followed by: Build FAILED.
expect:   outcome    (a) stop build-failed · "The project does not build: SQL71501 …[dbo].[vCustomer]…" · exit 7
                     (b) stop build-failed · "The project does not build: SQL46010 …" · exit 7
laws:     H3 A3
source:   expected · ARCH §6 (build-failed names the first error)
```

### CLS-38 · build warnings with a successful build are not an outcome
```case
kind:     pure
action:   Get-FxOutcome (build)
input:    build      C:\estate\Modules\vReport.sql(5,10,5,10): Build warning SQL71502: View: [dbo].[vReport] has an unresolved reference to object [Reporting].[dbo].[Fact].
                     Build succeeded.
                         1 Warning(s)
                         0 Error(s)
expect:   record     warnings {SQL71502}
laws:     H1
source:   expected · H1 (no warning-only exit)
note:     Get-FxOutcome returns no outcome for a successful build; the run continues to the publish and the warning
          rides to the record.
```

### CLS-39 · the first engine error decides; every one is recorded
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 515, Level 16, State 2, Line 1 Cannot insert the value NULL into column 'Email', table 'fx_make_email_required_dev_7q2.dbo.Customer'; column does not allow nulls. UPDATE fails.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 3621, Level 0, State 0, Line 1 The statement has been terminated.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
expect:   outcome    verdict blocked null · Msg 515 · dbo.Customer.Email · exit 3
          record     messages {515, 3621}
laws:     H5
source:   verify · whether sqlpackage prints 3621 as a second SQL72014 line (the scratch run's 1505 printed one line only)
note:     3621 is not in ARCH §6 and decides nothing: only the first engine error is classified. A permissive leg's
          classification, recorded beside the Strict verdict (CLS-55).
```

### CLS-40 · Msg 8111 (a PRIMARY KEY on a nullable column) is null, whatever the data
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 8111, Level 16, State 1, Line 1 Cannot define PRIMARY KEY constraint on nullable column in table 'OrderLine'.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 1750, Level 16, State 0, Line 1 Could not create constraint or index. See previous errors.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[OrderLine]
                         ADD CONSTRAINT [PK_OrderLine] PRIMARY KEY CLUSTERED ([OrderId] ASC, [LineNumber] ASC);
expect:   outcome    verdict blocked null · Msg 8111 · PK_OrderLine · exit 3
          record     phase main · messages {8111, 1750}
laws:     H5 E1
source:   expected · ARCH §6 (8111: a PRIMARY KEY on a nullable column, data-independent); sample-prs/define-pk.md saw 8111 then 1750 from hand-run T-SQL
note:     The block holds on an empty table too, so no question or witness bears on it (QST-42).
```

### CLS-41 · Msg 4901 on the permissive leg of a new NOT NULL column is null
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 4901, Level 16, State 1, Line 1 ALTER TABLE only allows columns to be added that can contain nulls, or have a DEFAULT definition specified, or the column being added is an identity or timestamp column, or alternatively if none of the previous conditions are satisfied the table must be empty to allow addition of this column. Column 'Segment' cannot be added to non-empty table 'Customer' because it does not satisfy these conditions.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Customer]
                         ADD [Segment] NVARCHAR (20) NOT NULL;
expect:   outcome    verdict blocked null · Msg 4901 · dbo.Customer.Segment · exit 3
laws:     H5 E1
source:   expected · ARCH §6 (4901: a NOT NULL column added without a default to a table with rows); the Strict leg is the guard (QST-10)
note:     A permissive leg's classification, recorded beside the Strict verdict (CLS-55).
```

### CLS-42 · Msg 248 (a digit string too large for int) is overflow
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 248, Level 16, State 1, Line 1 The conversion of the nvarchar value 'w99999999999' overflowed an int column.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Product] ALTER COLUMN [Code] INT NOT NULL;
expect:   outcome    verdict blocked overflow · Msg 248 · dbo.Product.Code · exit 3
laws:     H5 E1
source:   verify · that ALTER COLUMN raises 248 for this class; the mapping is ARCH §6 (220, 248 → overflow)
note:     A permissive leg's classification, recorded beside the Strict verdict (CLS-55).
```

### CLS-43 · SQL72015 on a blocked publish is attached; the engine error decides
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Warning SQL72015: The column [dbo].[Product].[LegacyCode] is being dropped, data loss could occur.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Product])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
          delta      guard batch dbo.Product (RAISERROR on its line 6)
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     warnings {SQL72015}
laws:     H5
source:   receipt · SamplePr "add-mandatory: a new NOT NULL column with no default blocks a populated table, applies when empty" (its Strict refusal carries SQL72015 beside Msg 50000 "Rows were detected")
```

### CLS-44 · a command timeout on the LocalDB copy
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg -2, Level 11, State 0, Line 0 Execution Timeout Expired.  The timeout period elapsed prior to completion of the operation or the server is not responding.
expect:   outcome    stop toolchain · "LocalDB not responding (LocalDB gave no answer in 180 s after one retry). Restart
                     it (SqlLocalDB stop, then start) and re-run." · exit 6
laws:     H3 A3
source:   verify · the text of the timeout line; ARCH §6's toolchain stop covers a LocalDB that does not respond; a witness-scale copy never needs that long, so the machine is at fault, not the change
```

### CLS-45 · a connection failure to the copy is the machine, not the environment
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Unable to connect to target server '(localdb)\MSSQLLocalDB'. Please verify the connection information such as the server name, login credentials, and firewall rules for the target server.
                     A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible.
expect:   outcome    stop toolchain · "LocalDB not responding ((localdb)\MSSQLLocalDB gave no answer after one retry).
                     Run `facsimile doctor`." · exit 6
laws:     H3 A3
source:   verify · the wording (scripts/prove.mjs matches "Unable to connect"); `unreachable` (exit 4) is reserved for environments
```

### CLS-46 · a connection failure during `read` is unreachable
```case
kind:     pure
action:   Get-FxOutcome (read leg: Extract)
input:    output     Connecting to database 'Estate' on server 'qa-sql'.
                     *** Error extracting database:Could not connect to database server.
                     A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible.
expect:   outcome    stop unreachable · "Cannot reach qa-sql (network). Check the network, or ask someone with access to
                     run `facsimile read QA`." · exit 4
laws:     H3 A3
source:   verify · the Extract wording for a refused connection
```

### CLS-47 · Extract without VIEW DEFINITION is no-access
```case
kind:     pure
action:   Get-FxOutcome (read leg: Extract)
input:    output     Connecting to database 'bd_probe' on server 'localhost,11433'.
                     Extracting schema
                     Extracting schema from database
                     *** Error extracting database:Could not extract package from specified database.
                     The reverse engineering operation cannot continue because you do not have View Definition permission on the 'bd_probe' database.
                     Time elapsed 0:00:09.20
          config     the login is r_dr
expect:   outcome    stop no-access · "r_dr lacks VIEW DEFINITION on bd_probe. Ask a lead to run `facsimile read <env>`, or request the grant." · exit 4
laws:     H3 A3 P4
source:   receipt · TWIN_SUCCESSION §6b boundary experiment (data-reader without VIEW DEFINITION: "sqlpackage refuses loudly"; r_dr.extract.log)
```

### CLS-48 · a platform mismatch at publish
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Initializing deployment (Start)
                     *** A project which specifies SQL Server 2022 or Azure SQL Managed Instance as the target platform cannot be published to SQL Server 2019.
                     Initializing deployment (Failed)
                     *** Could not deploy package.
          platform   LocalDB 15.0 · project DSP Sql160
expect:   outcome    stop platform · "LocalDB 15 is older than the project's target Sql160. Install LocalDB 2022 or later, or set allowOlderLocalDb (stamped on every record)." · exit 6
laws:     S4 H3
source:   verify · the wording and the DacFx message number; `doctor` should stop this before any publish (S4)
```

### CLS-49 · the base does not publish to LocalDB
```case
kind:     pure
action:   Get-FxOutcome (base leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 7609, Level 17, State 5, Line 1 Full-Text Search is not installed, or a full-text component cannot be loaded.
                     Error SQL72045: Script execution error.  The executed script:
                     CREATE FULLTEXT CATALOG [ftCatalog]
                         AUTHORITY [dbo];
expect:   outcome    stop base-unpublishable · "UAT's schema does not publish to LocalDB (full-text). Record it as Not checked." · exit 9
laws:     H3 H4
source:   verify · whether LocalDB supports full-text, and the number it raises
```

### CLS-50 · success text and an error in one output
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 515, Level 16, State 2, Line 1 Cannot insert the value NULL into column 'Email', table 'fx_x.dbo.Customer'; column does not allow nulls. UPDATE fails.
                     Successfully published database.
expect:   outcome    stop unclassified · "The publish ended with 'Successfully published database' and Msg 515 that no rule classifies…" · exit 9
laws:     H1 H2
source:   expected · H2 (no silent clean): contradictory evidence is never resolved toward clean
```

### CLS-51 · empty output
```case
kind:     pure
action:   Get-FxOutcome
input:    output     (empty)
expect:   outcome    stop unclassified · "The publish ended with no output that any rule classifies…" · exit 9
laws:     H1 H2
source:   expected · H2
```

### CLS-52 · reordered lines classify the same
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Error SQL72045: Script execution error.  The executed script:
                     IF EXISTS (SELECT TOP 1 1
                                FROM   [dbo].[Customer])
                         RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)
                             WITH NOWAIT;
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
                     *** Could not deploy package.
          delta      as CLS-04
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     H5 D1
source:   verify · whether sqlpackage's order ever varies (prove.mjs's fixture and the scratch run differ in where "*** Could not deploy package." falls)
```

### CLS-53 · localized engine text classifies by number
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 547, Level 16, State 0, Line 1 Die ALTER TABLE-Anweisung steht in Konflikt mit der FOREIGN KEY-Einschränkung "FK_Order_Customer". Der Konflikt trat in der fx_create_fk_dev_3k1-Datenbank, Tabelle "dbo.Customer", column 'Id' auf.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
expect:   outcome    verdict blocked constraint · Msg 547 · FK_Order_Customer · exit 3
laws:     H5
source:   verify · the localized wording, and that the Msg/Level/State prefix stays English; the object comes from the quoted batch first, the message text second
```

### CLS-54 · a localized success line is never read as clean
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Die Datenbank wurde erfolgreich veröffentlicht.
expect:   outcome    stop unclassified · "The publish ended with 'Die Datenbank wurde erfolgreich veröffentlicht.' that no rule classifies…" · exit 9
laws:     H2 E1
source:   verify · whether sqlpackage localizes its own lines by the machine's UI culture; if it does, the localized line enters as a rule with its case
```

### CLS-55 · the permissive leg is recorded beside the Strict verdict, never in place of it
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     Updating database (Start)
                     Altering Table [dbo].[Customer]...
                     Update complete.
                     Updating database (Complete)
                     Successfully published database.
          verdict    Strict: blocked guard · Msg 50000/127 · dbo.Customer
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     permissive {published; the content-hash diff names the rows it changed}
laws:     H1 F6
source:   expected · ARCH §5 step 10; skills/prove-on-dacpac ("Strict proves the refusal; Permissive, only after, shows the consequence")
```

### CLS-56 · Msg 220 is overflow
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 220, Level 16, State 2, Line 1 Arithmetic overflow error for data type tinyint, value = 300.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [Quantity] TINYINT NOT NULL;
expect:   outcome    verdict blocked overflow · Msg 220 · dbo.OrderLine.Quantity · exit 3
laws:     H5 E1
source:   receipt · SamplePr "retype-explicit: a lossy narrowing type change blocks a populated column, a widening retype applies" (its relaxed INT→TINYINT publish asserts "Msg 220" and "Arithmetic overflow error for data type tinyint")
note:     A permissive leg's classification, recorded beside the Strict verdict (CLS-55); the layout of the SQL72014 line
          is CLS-04's.
```

### CLS-57 · Msg 8169 is conversion
```case
kind:     pure
action:   Get-FxOutcome (permissive leg)
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 8169, Level 16, State 2, Line 1 Conversion failed when converting from a character string to uniqueidentifier.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Account] ALTER COLUMN [ExternalKey] UNIQUEIDENTIFIER NULL;
expect:   outcome    verdict blocked conversion · Msg 8169 · dbo.Account.ExternalKey · exit 3
laws:     H5 E1
source:   expected · ARCH §6 (8169 → conversion); the Fits classes of QST-26
```

### CLS-58 · any number inside an authored batch is blocked authored, the counterfeit 16,127 included
```case
kind:     pure
action:   Get-FxOutcome, once per (number, region)
input:    output     generator: for each n in {50000 state 127 (a counterfeit guard), 50000 state 1, 50001 (THROW), 207, 208,
                     515, 544, 547, 2601, 2627, 2628, 2714, 3726, 4711, 8152, 8672, 13575, 245, 468 (collations equal)}:
                     `Error SQL72014: … Msg n …` then `Error SQL72045: … The executed script:` quoting the region's first
                     statement
          delta      region pre Script.PreDeployment.sql · region post Script.PostDeployment.sql
expect:   outcome    every pair: verdict blocked authored · Msg n · <pre-deploy|post-deploy> · exit 3; never guard, unique,
                     null, constraint, cdc or collation, and never stop unclassified
laws:     H5 M1
source:   expected · INVARIANTS H5 ("any error raised inside a pre- or post-deploy batch, whatever its number"); ARCH §6
note:     Removing the authored rule must fail this case for every number at once (M1). The quote's first statement is
          what places the error in a region (CLS-28).
```

### CLS-59 · Msg 208 in a pre-deploy is authored
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Updating database (Start)
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 208, Level 16, State 1, Line 4 Invalid object name 'dbo.CustomerArchive'.
                     Error SQL72045: Script execution error.  The executed script:
                     INSERT INTO dbo.CustomerArchive (Id, Name)
                     SELECT Id, Name FROM dbo.Customer WHERE Region IS NULL;
          delta      region pre Script.PreDeployment.sql (first statement INSERT INTO dbo.CustomerArchive …)
expect:   outcome    verdict blocked authored · Msg 208 · pre-deploy · exit 3
          record     phase pre-deploy
laws:     H5
source:   expected · ARCH §6 (a script's 208); the pre-deploy runs before the main creates the table it names
```

### CLS-60 · Msg 2714 in a post-deploy is authored
```case
kind:     pure
action:   Get-FxOutcome
input:    output     The transacted portion of the database update succeeded.
                     An error occurred while the batch was being executed.
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 2714, Level 16, State 6, Line 1 There is already an object named '#seed' in the database.
                     Error SQL72045: Script execution error.  The executed script:
                     SET NOCOUNT ON;
          delta      region post Script.PostDeployment.sql (first statement SET NOCOUNT ON;)
expect:   outcome    verdict blocked authored · Msg 2714 · post-deploy · exit 3
          record     phase post-deploy
laws:     H5
source:   expected · ARCH §6 (a script's 2714)
```

### CLS-61 · Msg 468 in the main phase: collation when the collations match, platform when they do not
```case
kind:     pure
action:   Get-FxOutcome, once per platform
input:    output     Refreshing View [dbo].[vCustomerCode]...
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 468, Level 16, State 9, Procedure vCustomerCode, Line 4 Cannot resolve the collation conflict between "Latin1_General_CS_AS" and "Latin1_General_CI_AS" in the equal to operation.
                     Error SQL72045: Script execution error.  The executed script:
                     EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomerCode]';
          delta      main: a collation change on Customer.Code, then the view refresh
          platform   (a) the copy's server collation equals QA's · (b) it differs from QA's
expect:   outcome    (a) verdict blocked collation · Msg 468 · dbo.vCustomerCode · exit 3
                     (b) stop platform · "tempdb collation SQL_Latin1_General_CP1_CI_AS differs from QA's
                     Latin1_General_CI_AS …. <remedy>." · exit 6
          record     (a) phase main
laws:     H5 S4 F9
source:   expected · ARCH §6 (`blocked collation`: 468 when the copy's server collation equals the environment's,
          otherwise `stop platform`)
note:     Get-FxOutcome needs the two server collations beside the output, which ARCH §3's signature (the output and the
          delta) leaves implicit.
```

### CLS-62 · SQL72045's quote matches a guard batch token by token, and only then
```case
kind:     pure
action:   Get-FxOutcome, once per quote
input:    output     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected. The schema update is terminating because data loss might occur.
                     Error SQL72045: Script execution error.  The executed script:
                     (a) if exists (SELECT top 1 1 FROM dbo.Customer) RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.',16,127) WITH NOWAIT;
                     (b) IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 1) WITH NOWAIT;
                     (c) IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customers]) RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127) WITH NOWAIT;
          delta      guard batch dbo.Customer (CLS-04's: brackets, line breaks, lower-case `select top 1 1`, RAISERROR on
                     its line 6)
expect:   outcome    (a) verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
                     (b) stop unclassified · "The publish ended with Msg 50000/127 that no rule classifies. Add a rule
                     with its case." · exit 9
                     (c) stop unclassified · "The publish ended with Msg 50000/127 that no rule classifies…" · exit 9
laws:     H5
source:   expected · INVARIANTS H5, ARCH §6 (batches compared token by token, whitespace- and bracket-insensitive);
          the reformatting itself is CLS-04's scratch run
note:     Case, whitespace, line breaks and brackets never decide; a token that differs (a state, a table name) always
          does, so (b) and (c) match no guard batch of this delta.
```

### CLS-63 · the phase is read in DacFx's order: pre-deploy, main, post-deploy, then validation
```case
kind:     pure
action:   Get-FxOutcome, once per output
input:    output     (a) "Pre-deploy complete." · "Altering Table [dbo].[Order]..." · then Msg 547 quoting `ALTER TABLE
                     [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];` with no post-deploy PRINT yet
                     (b) "Pre-deploy complete." · "The transacted portion of the database update succeeded." · the
                     post-deploy's PRINT · "Checking existing data against newly created constraints" · then the same 547
                     (c) "Pre-deploy complete." · "The transacted portion of the database update succeeded." · then Msg 547
                     quoting the post-deploy's first statement
          delta      pre Script.PreDeployment.sql · main: WITH NOCHECK ADD FK_Order_Customer · post
                     Script.PostDeployment.sql · validation: WITH CHECK CHECK FK_Order_Customer
expect:   outcome    (a) verdict blocked constraint · Msg 547 · FK_Order_Customer · exit 3
                     (b) verdict blocked constraint · Msg 547 · FK_Order_Customer · exit 3
                     (c) verdict blocked authored · Msg 547 · post-deploy · exit 3
          record     (a) phase validation · residue lists post-deploy as not run · (b) phase validation · residue lists
                     pre-deploy, main and post-deploy as applied · (c) phase post-deploy
laws:     H6 H5
source:   expected · INVARIANTS H6 (pre-deploy → main → post-deploy → validation); the order is pinned per engine
          version by INV-H6-01
note:     The quoted batch places the failure; the PRINT markers place what ran before it. (a) is the order H6 rules out
          on 170.5.96; were an engine to produce it, the residue would say so rather than assume.
```

### CLS-64 · Msg 2627 inside a generated rebuild is unique, not authored
```case
kind:     pure
action:   Get-FxOutcome
input:    output     Starting rebuilding table [dbo].[T]...
                     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 2627, Level 14, State 1, Line 12 Violation of PRIMARY KEY constraint 'tmp_ms_xx_constraint_PK_T1'. Cannot insert duplicate key in object 'dbo.tmp_ms_xx_T'. The duplicate key value is (1, 1).
                     Error SQL72045: Script execution error.  The executed script:
                     BEGIN TRANSACTION;
                     SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
          delta      main: QST-63's rebuild batch, its PK now on (Id, Line)
expect:   outcome    verdict blocked unique · Msg 2627 · PK_T · exit 3
          record     phase main
laws:     H5
source:   expected · ARCH §6 (`blocked unique`: on insert, 2601 or 2627); the rebuild shape is red-team #3's
note:     The object is the constraint's name after the rebuild's sp_rename (tmp_ms_xx_constraint_PK_T1 → PK_T). The same
          number in a seed is authored (CLS-21).
```

### CLS-65 · an unknown number in the main phase stops
```case
kind:     pure
action:   Get-FxOutcome
input:    output     *** Could not deploy package.
                     Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 5074, Level 16, State 1, Line 1 The object 'DF_Customer_Region' is dependent on column 'Region'.
                     Error SQL72045: Script execution error.  The executed script:
                     ALTER TABLE [dbo].[Customer] ALTER COLUMN [Region] NVARCHAR (30) NULL;
          delta      main: that ALTER COLUMN
expect:   outcome    stop unclassified · "The publish ended with Msg 5074 that no rule classifies. Add a rule with its
                     case." · exit 9
laws:     H5 H2 E1
source:   expected · INVARIANTS H5 ("an unknown message number … in the generated main phase is stop unclassified,
          never clean")
note:     8111, which this case's predecessor used, now has its rule (CLS-40).
```

### CLS-66 · Extract that cannot log in is no-access
```case
kind:     pure
action:   Get-FxOutcome (read leg: Extract)
input:    output     Connecting to database 'Estate' on server 'dev-sql'.
                     *** Error extracting database:Could not connect to database server.
                     Login failed for user 'CORP\dev1'.
          config     the login is CORP\dev1
expect:   outcome    stop no-access · "CORP\dev1 cannot log in to Estate. Ask a lead to run `facsimile read Dev`, or
                     request the grant." · exit 4
laws:     H3 A3
source:   verify · the Extract wording for a refused login; the template is ARCH §6's no-access ("cannot log in to")
note:     A server that answers but refuses the login is no-access; one that does not answer is unreachable (CLS-46).
```

## TIER · Get-FxTier: tier and flags

### TIER-01 · no questions is T0
```case
kind:     pure
action:   Get-FxTier
input:    questions  none · measures Rows(Customer)
          answers    Dev Rows(Customer)=5
expect:   record     tier T0 · flags {}
laws:     D1 M1
source:   expected · ARCH §7; the widen of QST-19
```

### TIER-02 · HasRows alone is T1
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Product)
          answers    Dev HasRows(Product)=yes
expect:   record     tier T1
laws:     D1 M1
source:   expected · ARCH §7; the column drop of QST-73
```

### TIER-03 · each validation kind lifts the tier to T2, alone or with HasRows
```case
kind:     pure
action:   Get-FxTier, once per question set
input:    questions  each alone, then each with HasRows(T): NotNull(Customer.Email) · Fits(Product.Code→nvarchar(10)) ·
                     Unique(Product,(Code)) · Orphan(Order.(CustomerId)→Customer) · CheckFalse(Order,CK_Order_Total) ·
                     KeyLength(N,IX_N_Big) · Rebuild(T) · Computed(T.RefNum) · Temporal(Customer)
expect:   record     tier T2 in all eighteen runs
laws:     D1 M1
source:   expected · ARCH §7 (removing any one rule fails this case: M1)
```

### TIER-04 · any Authored or SeedDrift question is T3, whatever else is asked
```case
kind:     pure
action:   Get-FxTier, once per question set
input:    questions  (a) Authored(post:Data/Seed.sql:1, {Status.Code})
                     (b) HasRows(Customer) · NotNull(Customer.Email) · Authored(pre:Script.PreDeployment.sql:4, {Customer.Email})
                     (c) SeedDrift(Status) key Id, code Code, from post:Data/Seed.sql:1
expect:   record     (a) tier T3 · (b) tier T3 · (c) tier T3
laws:     D1 M1
source:   expected · ARCH §7 (the tier is the highest that holds; T3 is any Authored or SeedDrift)
```

### TIER-05 · the tier ignores the answers
```case
kind:     pure
action:   Get-FxTier, once per environment
input:    questions  HasRows(Customer) · NotNull(Customer.Email)
          answers    Dev HasRows(Customer)=no · QA HasRows(Customer)=yes, NotNull(Customer.Email)=violating
expect:   record     Dev tier T2 · QA tier T2
laws:     D1 F7
source:   expected · ARCH §7 ("identical in every environment"); the verdicts differ, the tier does not
```

### TIER-06 · the tier follows the statements, not the operation's name
```case
kind:     pure
action:   Get-FxTier, once per delta's questions
input:    questions  (a) "rename ContactPhone to MobileNumber" without a refactorlog entry: HasRows(Customer) (QST-75)
                     (b) the same rename with its refactorlog entry: none (QST-71)
expect:   record     (a) tier T1 · (b) tier T0
laws:     D1
source:   expected · (a) SamplePr "rename-attribute: a bare column rename (no refactorlog) is a DROP+ADD blocked by the data-loss guard; EXEC sp_rename preserves every value" (the DROP+ADD leg); (b) rests on QST-71's refactorlog delta, which is verify (the test runs sp_rename by hand, not from a refactorlog)
```

### TIER-07 · a Scan alone never raises the tier
```case
kind:     pure
action:   Get-FxTier
input:    questions  none · measures Rows(Order) for Scan(Order)
          answers    Dev Rows(Order)=4
expect:   record     tier T0
laws:     D1
source:   expected · ARCH §7 (T0: no data-sensitive statements); a Scan is data-insensitive (ARCH §4, QST-44)
```

### TIER-08 · discharged questions never raise the tier
```case
kind:     pure
action:   Get-FxTier
input:    questions  none · discharged {Orphan(CustomerPreference.(CustomerId)→Customer)}
expect:   record     tier T0
laws:     D1 S3
source:   expected · QST-58 (create-entity)
```

### TIER-09 · scale at exactly 1,000,000 rows when the delta alters a column there
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Customer) · NotNull(Customer.Email) · measures Rows(Customer)
          answers    QA Rows(Customer)=1000000
expect:   record     tier T2 · flags {scale}
laws:     H4 D1
source:   expected · ARCH §7 ("1M rows or more"); THE_RECORD §5 (the large-table scrutiny line)
```

### TIER-10 · 999,999 rows is not scale
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Customer) · NotNull(Customer.Email) · measures Rows(Customer)
          answers    QA Rows(Customer)=999999
expect:   record     tier T2 · flags {}
laws:     H4 D1
source:   expected · ARCH §7
```

### TIER-11 · an index build alone sets scale, with no question asked
```case
kind:     pure
action:   Get-FxTier
input:    questions  none · measures Rows(Order) for Scan(Order) (CREATE NONCLUSTERED INDEX)
          answers    QA Rows(Order)=1200000
expect:   record     tier T0 · flags {scale}
laws:     H4
source:   receipt · F20 (the nonclustered index build is the first operation whose engine cost surfaces, about 11 s over floor at 1.05M rows)
note:     The Scan's `measures` are what let `read` count rows for a T0 delta (ARCH §3: Get-FxQuestions returns them).
```

### TIER-12 · a table rebuild at 1M rows sets scale
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Category) · Rebuild(Category) classes {gapped} · measures Rows(Category)
          answers    UAT Rows(Category)=1500000
expect:   record     tier T2 · flags {scale}
laws:     H4
source:   expected · ARCH §7 ("rebuilds a table")
```

### TIER-13 · validating a constraint on a 1M-row child sets scale
```case
kind:     pure
action:   Get-FxTier
input:    questions  Orphan(OrderLine.(OrderId)→Order) · measures Rows(OrderLine)
          answers    QA Rows(OrderLine)=3100000 · Rows(Order)=40000
expect:   record     tier T2 · flags {scale}
laws:     H4
source:   expected · ARCH §7 ("validates a constraint on it"); F20 found the re-validation scan still invisible at 1.18M on its substrate, so the flag is scrutiny, not a prediction
```

### TIER-14 · scale is per touched table: a large parent of a small validated child is not scale
```case
kind:     pure
action:   Get-FxTier
input:    questions  Orphan(Shipment.(OrderId)→Order) · measures Rows(Shipment)
          answers    QA Rows(Shipment)=10 · Rows(Order)=2000000
expect:   record     tier T2 · flags {}
laws:     H4 D1
source:   expected · ARCH §7 ("a touched table … on it"): the constraint is on Shipment
```

### TIER-15 · a guard-only drop on a large table is scale; a nullable add is not
```case
kind:     pure
action:   Get-FxTier, once per question set
input:    questions  (a) HasRows(Product) · measures Rows(Product) (a DROP COLUMN, QST-73) · (b) none (an ADD … NULL)
          answers    UAT Rows(Product)=5000000
expect:   record     (a) tier T1 · flags {scale} · (b) tier T0 · flags {}
laws:     H4
source:   expected · ARCH §7 (scale: a touched table at 1,000,000 rows or more whose statement is data-sensitive, Scan,
          Rebuild or authored DML; HasRows is data-sensitive); an ADD … NULL is non-reading
note:     The guard reads one row, but the drop it guards is scrutinized at volume all the same; whether a guard alone
          should set scale is a spine question this case pins to the spine's present text.
```

### TIER-16 · an environment with no count gets no scale flag, and is flagged unread and assumed
```case
kind:     pure
action:   Get-FxTier, for the Prod record under --assume Prod=worst
input:    questions  HasRows(Customer) · NotNull(Customer.Email) · measures Rows(Customer)
          answers    Prod none
          config     Prod read none
expect:   record     tier T2 · flags {unread:Prod, assumed:Prod}
laws:     H4 F7
source:   expected · ARCH §7 (unread:<env>, assumed:<env>); the volume residual carries the unknown size (REC-05)
```

### TIER-17 · cdc from a touched table's answer
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(C) · measures Rows(C)
          answers    QA Cdc(C)=yes
expect:   record     tier T1 · flags {cdc}
laws:     H4
source:   receipt · red-team #6 (CDC: 4928 on sp_rename, 4711 on TRUNCATE ✓; facts carry is_tracked_by_cdc)
note:     The flag is the tier's; on LocalDB the shell then stops `cdc` (exit 9), because LocalDB has no CDC.
```

### TIER-18 · CDC on a table the delta does not touch sets no flag
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Customer)
          answers    QA Cdc(AuditLog)=yes · Cdc(Customer)=no
expect:   record     tier T1 · flags {}
laws:     H4
source:   expected · ARCH §7
```

### TIER-19 · unread environments: read none, and no answers file
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Customer)
          answers    Dev present · QA present · UAT absent
          config     Dev, QA, UAT read · Prod read none
expect:   record     flags {unread:UAT, unread:Prod}
laws:     H4 F7
source:   expected · ARCH §7 ("an environment has no answers (such as Prod)"); ARCH §3 hands Get-FxTier the config
```

### TIER-20 · in-flight when a touched table is in an open window
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Customer) · NotNull(Customer.Email)
          inflight   | CHG-0412 | make Email required | dbo.Customer | 1 | 2 | ship R2 | 2026-11-02 | #812 |
expect:   record     flags {in-flight}
laws:     H4 A5
source:   expected · ARCH §6/§7; estate/in-flight.md's `tables` column
note:     The flag is Get-FxTier's; the shell stops `in-flight` unless this change is the window's next release.
```

### TIER-21 · older-localdb when the override is set
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Customer)
          platform   LocalDB 15.0 · project DSP Sql160 · allowOlderLocalDb true
expect:   record     flags {older-localdb}
laws:     S4
source:   expected · INVARIANTS S4
```

### TIER-22 · a defaulted NOT NULL column on a large table is T0 and scale
```case
kind:     pure
action:   Get-FxTier
input:    questions  none · measures Rows(Customer) for Scan(Customer) (QST-70)
          answers    QA Rows(Customer)=3000000
expect:   record     tier T0 · flags {scale}
laws:     H4
source:   expected · ARCH §4 (an ADD … NOT NULL DEFAULT backfill is a Scan), §7 (scale counts a Scan)
note:     The backfill rewrites every row on editions without the metadata-only path; the flag is scrutiny, not a
          prediction.
```

### TIER-23 · every flag at once, in canonical order
```case
kind:     pure
action:   Get-FxTier, for the Prod record under --assume Prod=worst
input:    questions  HasRows(Customer) · NotNull(Customer.Email) · measures Rows(Customer)
          answers    QA Rows(Customer)=2000000 · Cdc(Customer)=yes
          config     Prod read none
          inflight   dbo.Customer listed
          platform   allowOlderLocalDb true, LocalDB 15.0, DSP Sql160
          lag        QA receives one operation beyond this PR · QA's deploy would drop dbo.QaOnly
expect:   record     tier T2 · flags {scale, cdc, unread:Prod, assumed:Prod, lag:QA, drift:QA, in-flight, older-localdb}
laws:     D1 H4
source:   expected · ARCH §7's flag table order
```

### TIER-24 · the order of the questions never changes the result
```case
kind:     pure
action:   Get-FxTier on TIER-23's input with its questions shuffled, ten times
expect:   record     byte-identical tier and flags each time
laws:     D1
source:   expected · INVARIANTS D1
```

### TIER-25 · lag: the environment receives operations beyond this PR
```case
kind:     pure
action:   Get-FxTier
input:    questions  HasRows(Customer)
          lag        Compare-FxDeltas: delta.QA.sql holds `ALTER TABLE [dbo].[Product] ADD [Sku] NVARCHAR (20) NULL;`, which
                     delta.pr.sql lacks (QA is a release behind the merge-base) · Dev's delta adds nothing beyond the PR
expect:   record     flags {lag:QA} · lag:QA {adds Product.Sku}
laws:     H4 F7
source:   expected · ARCH §3 (Compare-FxDeltas), §7 (lag:<env>)
note:     Dev carries no lag flag; the flag is per environment, from that environment's own delta (F7).
```

### TIER-26 · drift: the deploy would drop environment-only objects
```case
kind:     pure
action:   Get-FxTier
input:    questions  none
          lag        Compare-FxDeltas: delta.UAT.sql drops dbo.IX_Customer_Hotfix and dbo.vUatReport, objects only UAT
                     holds; delta.pr.sql drops neither
expect:   record     tier T0 · flags {drift:UAT} · drift:UAT {removes IX_Customer_Hotfix, vUatReport}
laws:     H4 F7
source:   expected · ARCH §3 (Compare-FxDeltas: "the environment-only objects the deploy drops"), §7 (drift:<env>)
```

### TIER-27 · in-flight is keyed on each environment's delta tables, and a PR that edits the ledger warns
```case
kind:     pure
action:   Get-FxTier, once per row
input:    questions  Dev: HasRows(Customer) · QA: HasRows(Customer) · Fits(Product.Code→nvarchar(10))
          inflight   | CHG-0412 | narrow Product.Code | dbo.Product | 1 | 2 | ship R2 | 2026-11-02 | #812 |
                     rows: (a) the PR's changed files never mention Product · (b) as (a), and the PR also edits the
                     ledger
expect:   record     (a) Dev flags {} · QA flags {in-flight} · (b) QA flags {in-flight} · warnings {the PR edits the
                     in-flight ledger}
laws:     H4 F7
source:   expected · ARCH §2 row 27 (keyed on the tables of each environment's delta, not on changed files; a PR that
          also edits the ledger warns rather than stops), §7
note:     In (a) the shell then stops in-flight for QA alone; in (b) the warning replaces that stop. An unreadable ledger
          is stop config (CLI-16).
```

### TIER-28 · scale from authored DML on a large table
```case
kind:     pure
action:   Get-FxTier
input:    questions  Authored(pre:Script.PreDeployment.sql:4, {Customer.Email}) · measures Rows(Customer)
          answers    QA Rows(Customer)=1500000
expect:   record     tier T3 · flags {scale}
laws:     H4
source:   expected · ARCH §7 (scale counts authored DML on the touched table)
```

## REC · Get-FxRecord: the ten sections

### REC-01 · one blocked verdict onto the ten sections
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Customer (Dev)
          questions  HasRows(Customer) · NotNull(Customer.Email)
          answers    Dev HasRows(Customer)=yes · NotNull(Customer.Email)=violating, 2 rows · Rows(Customer)=6 · Cdc(Customer)=no
          delta      the guard, then ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
          config     Dev read · QA, UAT unread · Prod read none
expect:   record     Verdict: blocked guard on dbo.Customer · tier T2 · ask "Confirm Customer.Email holds no NULL in each environment before promoting"
                     Intent: none from the tool (the developer's words)
                     What changes: effects {modifies Customer.Email NULL→NOT NULL}
                     Before promoting: QA, UAT, Prod: run the NotNull probe, expect 0 rows
                     The data: Dev: Customer holds 6 rows; Email is NULL in 2
                     How it ships: ships two releases (F4, F7)
                     What proving showed: tried: Msg 50000/127 on dbo.Customer (phase main) · residue {pre-deploy: none ·
                     main: none}
                     After deploy — check: SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL; expect 0
                     How to roll this back: ALTER COLUMN Email back to NULL loses nothing · backing out was not exercised
                     Not checked: notChecked {unread:QA, unread:UAT, unread:Prod, volume, cdc, drift-since-measure,
                     engine-version, integration-studio}
laws:     H4 H6 H1
source:   receipt · F7 (populated NULL → NOT NULL blocks and ships as two releases) · README §1's example record
note:     Intent is the developer's; the tool leaves it empty rather than inventing it. Counts are integers (P1).
```

### REC-02 · a clean T0 verdict still names what it did not check
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          questions  none
          answers    Dev, QA, UAT, Prod read; Cdc(Customer)=no everywhere
          delta      ALTER TABLE [dbo].[Customer] ADD [MiddleName] NVARCHAR (100) NULL;
expect:   record     ships in place, no data read or written · notChecked {volume, drift-since-measure, engine-version,
                     integration-studio}
laws:     H4
source:   receipt · SamplePr "add-optional: a new nullable column applies clean on a populated table" (the clean leg); the residual rule is H4
```

### REC-03 · the smallest notChecked: an index-only change, every environment read, no CDC
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          questions  none · measures Rows(Order) for Scan(Order)
          answers    every environment read; Rows(Order) < 1000000 everywhere; Cdc(Order)=no everywhere
          delta      CREATE NONCLUSTERED INDEX [IX_Order_StatusId] ON [dbo].[Order]([StatusId] ASC);
expect:   record     notChecked {volume, drift-since-measure, engine-version}
laws:     H4
source:   expected · H4 (volume and drift-since-measure always apply; engine-version until the Octopus DacFx pin equals the stamp, estate/toolchain.md; an index changes no entity, so no Integration Studio refresh)
```

### REC-04 · notChecked is drawn only from the closed set
```case
kind:     pure
action:   Get-FxRecord over every REC case's input
expect:   record     every notChecked is non-empty and ⊆ {unread:<env>, assumed:<env>, volume, cdc, workload,
                     platform:<what>, drift-since-measure, engine-version, integration-studio, external-consumers,
                     app-behaviour, declared-not-measured, tooling}, in that order
laws:     H4 D1
source:   expected · INVARIANTS H4 (the closed token set)
```

### REC-05 · unread environments are named in configuration order
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Customer (Dev)
          answers    Dev present · QA present · UAT absent
          config     Dev, QA, UAT read · Prod read none
expect:   record     notChecked {unread:UAT, unread:Prod, volume, cdc, drift-since-measure, engine-version,
                     integration-studio}
laws:     H4 F7
source:   expected · H4; QA's own verdict covers QA (F7), so it is not a residual of this one
```

### REC-06 · the cdc residual is omitted only when every environment was read and none tracks a touched table
```case
kind:     pure
action:   Get-FxRecord, once per answers set
input:    verdict    clean
          delta      ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (400) NULL;
          answers    (a) all four read, Cdc(Customer)=no everywhere · (b) Prod unread
expect:   record     (a) cdc absent from notChecked · (b) cdc present
laws:     H4
source:   expected · TWIN_SUCCESSION §6 (LocalDB has no CDC; an unread environment's CDC state is unknown)
```

### REC-07 · the platform residual: an older LocalDB, or a #temp table in an authored script
```case
kind:     pure
action:   Get-FxRecord, once per input
input:    verdict    clean
          platform   (a) older-localdb flag set
          delta      (b) a changed pre-deploy creating #map (Code NVARCHAR(50)) with no COLLATE DATABASE_DEFAULT
expect:   record     (a) notChecked includes platform:older-localdb · (b) notChecked includes platform:tempdb-collation
laws:     H4 S4
source:   receipt · red-team #13 (#temp joins hit Msg 468 against the server collation ✓) · S4
```

### REC-08 · the workload residual when an index is removed
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      DROP INDEX [IX_Customer_Region] ON [dbo].[Customer];
expect:   record     effects {removes IX_Customer_Region} · notChecked includes workload
laws:     H4
source:   receipt · SamplePr "drop-index: removing an index publishes clean and loses no rows; the scripted DROP INDEX is lossless" · TWIN_SUCCESSION §6 (index usage is workload state)
```

### REC-09 · effects: adds
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      QST-58's delta (CustomerPreference, its PK, FK_CustomerPreference_Customer) and QST-69's CREATE TABLE [archive].[OrderArchive]
expect:   record     effects {adds archive.OrderArchive, CustomerPreference, FK_CustomerPreference_Customer}
laws:     D1
source:   expected · skills/author-pr (What changes: one line per real change)
note:     A table's own PK and inline columns are part of "adds <table>", not separate lines.
```

### REC-10 · effects: modifies, with from and to
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Product
          delta      the guard, then ALTER TABLE [dbo].[Product] ALTER COLUMN [Code] NVARCHAR (10) NOT NULL;
          base       Product.Code nvarchar(50) NOT NULL
expect:   record     effects {modifies Product.Code nvarchar(50)→nvarchar(10)}
laws:     D1
source:   receipt · F1 (the narrowing)
```

### REC-11 · effects: removes; a default dropped with its column is mechanics
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Product
          delta      QST-73's delta (DROP CONSTRAINT DF_Product_LegacyCode, DROP COLUMN LegacyCode)
expect:   record     effects {removes Product.LegacyCode}
laws:     D1
source:   receipt · SamplePr "delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the scripted DROP COLUMN is the irreversible step"
```

### REC-12 · effects: renames from sp_rename; the __RefactorLog bookkeeping is not an effect
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      QST-71's delta
expect:   record     effects {renames Customer.ContactPhone→MobileNumber}
laws:     D1
source:   verify · the delta shape (QST-71); the effect itself is the rename sample-prs/rename-attribute.md proved
```

### REC-13 · effects: DROP + ADD is a removal and an addition, never a rename
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Customer
          delta      QST-75's delta (DROP COLUMN ContactPhone, ADD MobileNumber)
expect:   record     effects {adds Customer.MobileNumber · removes Customer.ContactPhone} · weighs data is removed irreversibly
laws:     D1
source:   receipt · SamplePr "rename-attribute: a bare column rename (no refactorlog) is a DROP+ADD blocked by the data-loss guard; EXEC sp_rename preserves every value"
note:     The record states what the delta does; that the author meant a rename is for the author and reviewer to see.
```

### REC-14 · effects: a rebuild's DROP TABLE and sp_renames are the rebuild; re-created identical objects are not effects
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      QST-63's delta (FK_T_P dropped and re-added unchanged; UX_T_Code re-created unchanged; RefNum added)
expect:   record     effects {adds T.RefNum · modifies T (rebuilt through tmp_ms_xx_T)}
laws:     D1
source:   receipt · red-team #3 (the generated rebuild delta)
```

### REC-15 · a key added WITH NOCHECK and never validated is recorded as untrusted
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      QST-56's delta (WITH NOCHECK ADD FK_Order_Status, no CHECK CHECK)
expect:   record     effects {adds FK_Order_Status (lands untrusted, is_not_trusted = 1)}
laws:     D1 H4
source:   verify · whether DacFx writes the shape (QST-56); the untrusted outcome of a NOCHECK add without re-validation is F9's anti-pattern
```

### REC-16 · effects: one object dropped and re-added with a new definition is a modification
```case
kind:     pure
action:   Get-FxRecord, once per delta
input:    verdict    clean
          delta      (a) QST-52's delta (FK_Order_Status re-added ON DELETE CASCADE)
                     (b) ALTER TABLE [dbo].[Order] DROP CONSTRAINT [DF_Order_StatusText]; then ALTER TABLE [dbo].[Order] ADD CONSTRAINT [DF_Order_StatusText] DEFAULT (N'Shipped') FOR [StatusText];
                     (c) DROP INDEX [IX_Product_Code] ON [dbo].[Product]; then CREATE UNIQUE NONCLUSTERED INDEX [IX_Product_Code] ON [dbo].[Product]([Code] ASC);
expect:   record     (a) effects {modifies FK_Order_Status ON DELETE NO ACTION→CASCADE}
                     (b) effects {modifies DF_Order_StatusText 'Pending'→'Shipped'}
                     (c) effects {modifies IX_Product_Code non-unique→unique}
laws:     D1
source:   receipt · F9 change-delete-rule (a) · F10 modify-default is a DROP+ADD (b); (c) expected · sample-prs/modify-index.md
```

### REC-17 · effects: a schema transfer is a rename
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      ALTER SCHEMA [archive] TRANSFER [dbo].[Category];
expect:   record     effects {renames dbo.Category→archive.Category}
laws:     D1
source:   verify · the delta shape (QST-72)
```

### REC-18 · a view refresh and database options are not effects
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      QST-05's QUERY_STORE block · EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomer]';
expect:   record     effects {}  · What changes collapses to one honest line
laws:     D1
source:   receipt · TWIN_SUCCESSION §6b (both statements in the boundary delta); skills/author-pr ("a section with nothing real collapses to one honest line")
```

### REC-19 · residue after Msg 547 at validation: main and post-deploy applied, the key untrusted
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked constraint · Msg 547 · FK_Order_Customer (phase validation)
          delta      pre-deploy (one no-op UPDATE) · main: WITH NOCHECK ADD FK_Order_Customer · post-deploy: the seed · validation: WITH CHECK CHECK
          replan     ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
expect:   record     residue {pre-deploy: applied · main: FK_Order_Customer added WITH NOCHECK · post-deploy: applied ·
                     validation: FK_Order_Customer untrusted (is_not_trusted = 1)}, in that order
laws:     H6
source:   verify · the order (post-deploy before validation, outside the transacted portion) was observed 2026-10-18 on sqlpackage 170.5.96 and is pinned by INV-H6-01; the untrusted key is receipt F9 (the failed publish leaves the key present but untrusted)
note:     What the replan shows for an untrusted key is part of the same open question.
```

### REC-20 · residue after Msg 1505 when the scripts are not transactional
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked unique · Msg 1505 · UX_T_Code (phase main)
          options    IncludeTransactionalScripts=False
          delta      QST-33's delta
          replan     CREATE UNIQUE NONCLUSTERED INDEX [UX_T_Code] ON [dbo].[T]([Code] ASC); plus the CHECK pair
expect:   record     residue {main: UX_T_Code dropped · T.Code collation changed to Latin1_General_CI_AS · T.Note added}
laws:     H6 F6
source:   receipt · red-team #4 (failed deploys are not atomic ✓) · INVARIANTS H6 ("a 1505 unique build leaves the index dropped and the column altered")
note:     The red team's publish options are not recorded; IncludeTransactionalScripts=False is sqlpackage's default.
          Under True the main rolls back whole (F13), and the residue is REC-22's.
```

### REC-21 · residue after the guard when the main is transactional: the pre-deploy's work survives
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Product
          options    IncludeTransactionalScripts=True
          delta      pre-deploy: UPDATE … LEFT(Code, 10) and ALTER TABLE dbo.Product ALTER COLUMN Code NVARCHAR(10) NOT NULL · main: the guard, then the model's ALTER
          replan     empty for Product.Code
expect:   record     residue {pre-deploy: Product.Code shortened and narrowed to nvarchar(10) · main: none}
laws:     H6
source:   receipt · F2 (refused, yet the column was already 10 afterward) · F6 (a pre-deploy side effect is not rolled back)
```

### REC-22 · residue after a guard with nothing before it is stated, not omitted
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Customer
          options    IncludeTransactionalScripts=True
          delta      main: the guard, an sp_rename on another table, then the ALTER; no pre-deploy
          replan     identical to the delta's main
expect:   record     residue {none}
laws:     H6
source:   receipt · F13 (one publish with a rename and a populated tightening → refused, and the rename did not land) · F1 (the column stayed 50)
```

### REC-23 · residue after a post-deploy failure: the main has landed
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked authored · Msg 544 · post-deploy (phase post-deploy)
          delta      QST-64's rebuild · post-deploy: the Category MERGE with explicit ids
          replan     empty
expect:   record     residue {pre-deploy: none · main: Category rebuilt with IDENTITY · post-deploy: failed at the seed}
laws:     H6
source:   expected · F12 (the first attempt failed in the post-deploy seed); F15 (b) (a half-applied release after a post-deploy failure)
```

### REC-24 · a clean verdict carries no residue
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      ALTER TABLE [dbo].[Customer] ADD [MiddleName] NVARCHAR (100) NULL;
expect:   record     no residue field
laws:     H6 H1
source:   expected · INVARIANTS H6 ("after any block")
```

### REC-25 · every verdict carries every stamp
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          stamps     tool 1.0.0 · sqlpackage 170.5.96 · LocalDB 16.0.4135 Express · head 3f9c2e1 · merge-base 8a01d77 ·
                     answers read from dev-sql/Estate · hashes of options, schema.Dev.dacpac, answers.Dev.json's content,
                     witness.sql, delta.Dev.sql and delta.pr.sql · answers measured by CORP\dev1 at 2026-10-18T09:12:00Z
expect:   record     stamps {tool, sqlpackage, server version and edition, head, mergeBase, answersServer,
                     answersDatabase, options#, schema#, answers#, witness#, delta#, deltaPr#, measuredBy, measuredAt},
                     all present
laws:     H7 E2
source:   expected · INVARIANTS H7
```

### REC-26 · each stamped input changes the identity hash; the verdict's time and the answers' login and time do not
```case
kind:     pure
action:   Get-FxRecord on REC-25's input, changing one input at a time
input:    stamps     change (1) the options hash (2) the schema hash (3) the answers' content hash (4) the witness hash
                     (5) the delta hash (6) the delta.pr hash (7) the head commit (8) the merge-base commit (9) the
                     sqlpackage version (10) the LocalDB version (11) the tool version; then, one at a time, (12) only the
                     verdict's own timestamp (13) only the answers' measuredAt (14) only the answers' login
expect:   record     identity hash differs from REC-25's in (1)–(11); is equal in (12)–(14)
laws:     H7 D1 F7
source:   expected · INVARIANTS H7 ("the login and the time are stamps but not inputs"), D1
```

### REC-27 · a verdict without the sqlpackage version is never emitted
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          stamps     as REC-25 but sqlpackage absent
expect:   outcome    stop toolchain · "sqlpackage not responding (it reported no version). Reinstall it at the pin, then
                     re-run." · exit 6
laws:     E2 H7 H1
source:   expected · E2 ("a verdict without a version stamp is invalid")
```

### REC-28 · assumed Prod, a tightening: both worlds and the probes to run first
```case
kind:     pure
action:   Get-FxRecord (--assume Prod=worst)
input:    verdict    populated-and-clean world: blocked guard · Msg 50000/127 · dbo.Customer
                     populated-and-violating world: blocked guard · Msg 50000/127 · dbo.Customer
          questions  HasRows(Customer) · NotNull(Customer.Email)
          config     Prod read none · UAT read (nearest)
expect:   record     stamps {assumed, schema from UAT} · flags {unread:Prod, assumed:Prod}
                     What proving showed: both worlds blocked by the guard, Msg 50000/127
                     Before promoting: Prod: run the row-count probe; if Prod holds rows, this ships as two releases · run the
                     NotNull probe for the backfill, expect the count the release-one script handles
                     notChecked includes assumed:Prod
laws:     H4 F7
source:   expected · ARCH §8 (--assume <env>=worst) · TWIN_SUCCESSION §6 ("record the both-worlds result and the probe to run before promoting")
```

### REC-29 · assumed Prod, a foreign key: the violating world names the message
```case
kind:     pure
action:   Get-FxRecord (--assume Prod=worst)
input:    verdict    populated-and-clean world: clean
                     populated-and-violating world: blocked constraint · Msg 547 · FK_Order_Customer
          questions  Orphan(Order.(CustomerId)→Customer)
          config     Prod read none · UAT read
expect:   record     stamps {assumed, schema from UAT} · flags {unread:Prod, assumed:Prod}
                     What proving showed: clean when every order has a customer; Msg 547 on FK_Order_Customer when one does not
                     Before promoting: Prod: run the orphan probe, expect 0 rows
                     notChecked includes assumed:Prod
laws:     H4
source:   expected · ARCH §8; F9 (the violating world's message)
```

### REC-30 · assumed Prod, a unique index: one violating world per class, probed under Prod's collation
```case
kind:     pure
action:   Get-FxRecord (--assume Prod=worst)
input:    verdict    violating worlds: exact, case, trailing, nulls → each blocked unique · Msg 1505 · UX_Customer_Email
          questions  Unique(Customer,(Email)) classes {exact, case, trailing, nulls}
          config     Prod read none
expect:   record     Before promoting: Prod: run the duplicate-group probe under the collation the index will be built
                     with, for each class, expect 0 · notChecked includes assumed:Prod and platform:collation (Prod's
                     collation was not read; the schema is the nearest read environment's)
laws:     H4 F9
source:   expected · F10 (1505 for a duplicate and for a second NULL); red-team #13 (the copy takes the dacpac's collation)
```

### REC-31 · how it ships and what the lead weighs are two fields, and neither is the tier
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          questions  Orphan(Order.(StatusId)→Status)
          delta      QST-50's shape for FK_Order_Status
expect:   record     tier T2 · ships in place (one release) · weighs a cross-table relationship is added
laws:     H1 D1
source:   receipt · F8 (a clean FK lands trusted in one release) · THE_RECORD §5 (the two findings stand together and never collapse)
```

### REC-32 · a populated column drop: two releases, and the strongest weigh-line
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked guard · Msg 50000/127 · dbo.Product
          questions  HasRows(Product)
          delta      QST-73's delta
expect:   record     tier T1 · ships two releases (F4) · weighs data is removed irreversibly
laws:     H1
source:   receipt · SamplePr "delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the scripted DROP COLUMN is the irreversible step" · THE_RECORD §5
```

### REC-33 · the scale flag adds the scrutiny line, separately
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          questions  none · measures Rows(Order) for Scan(Order)
          answers    QA Rows(Order)=1200000
          delta      CREATE NONCLUSTERED INDEX [IX_Order_StatusId] ON [dbo].[Order]([StatusId] ASC);
expect:   record     tier T0 · flags {scale} · ships in place · weighs additive · scrutiny "at production row counts this
                     change may block writes or run long — schedule a window"
laws:     H4
source:   receipt · F20 (index builds surface above 1M rows) · THE_RECORD §5 (the large-table scrutiny line)
```

### REC-34 · a defaulted NOT NULL column: one in-place release that writes every row
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          questions  none · measures Rows(Customer) for Scan(Customer)
          delta      ALTER TABLE [dbo].[Customer] ADD [Segment] NVARCHAR (20) CONSTRAINT [DF_Customer_Segment] DEFAULT (N'Standard') NOT NULL;
expect:   record     tier T0 · effects {adds Customer.Segment} · ships in place · weighs existing data is modified (every row receives 'Standard')
laws:     H1
source:   receipt · F14 (the default stamped all existing rows) · SamplePr "add-default: a NOT NULL column with a default applies clean and backfills existing rows"
```

### REC-35 · a key-length warning is carried as a later block
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean · warnings {1945: IX_N_Big maximum key 2000 bytes}
          delta      CREATE NONCLUSTERED INDEX [IX_N_Big] ON [dbo].[N]([Big] ASC);
expect:   record     What proving showed: Msg 1945 on IX_N_Big (maximum key 2000 bytes) · warnings {1945}
                     · notChecked includes app-behaviour ("a later row with a key over 1700 bytes fails Msg 1946")
laws:     H4
source:   receipt · red-team #16 (1945 warns now, 1946 fails later ✓)
```

### REC-36 · two environments with equal inputs share one proof; different answers do not
```case
kind:     pure
action:   Get-FxRecord, for Dev and QA
input:    verdict    (a) Dev and QA with equal delta hash and equal answers hash · (b) equal delta hash, QA's answers differ
expect:   record     (a) one proof, cited by verdict.Dev.json and verdict.QA.json · (b) two proofs, two records
laws:     F7
source:   expected · INVARIANTS F7
```

### REC-37 · the after-deploy checks follow the questions
```case
kind:     pure
action:   Get-FxRecord
input:    questions  NotNull(Customer.Email) · Unique(Product,(Code)) · Orphan(Order.(CustomerId)→Customer) · Fits(Product.Code→nvarchar(10))
expect:   record     After deploy — check:
                     SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL; expect 0
                     SELECT Code FROM dbo.Product GROUP BY Code HAVING COUNT(*) > 1; expect no rows
                     SELECT COUNT(*) FROM dbo.[Order] o WHERE NOT EXISTS (SELECT 1 FROM dbo.Customer c WHERE c.Id = o.CustomerId); expect 0
                     SELECT is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Customer'; expect 0
                     SELECT COL_LENGTH('dbo.Product', 'Code'); expect 20
laws:     H1
source:   expected · skills/author-pr (After deploy — check: each query with its expected result)
```

### REC-38 · the rollback line follows the effects, and always says it was not exercised
```case
kind:     pure
action:   Get-FxRecord, once per effect
input:    effects    (a) adds Customer.MiddleName · (b) modifies Product.Code nvarchar(50)→nvarchar(10) · (c) removes Product.LegacyCode
                     · (d) renames Customer.ContactPhone→MobileNumber
expect:   record     (a) drop the column; loses any value written since
                     (b) widen it back; values shortened by the change are not restored
                     (c) not reversible from the schema; only a restore brings the values back
                     (d) sp_rename it back; loses nothing
                     each ends "Backing the change out was not exercised."
laws:     H4
source:   expected · skills/prove-on-dacpac ("the disposable copy proves the FORWARD publish only")
```

### REC-39 · an authored block is reported as the script that refused, never as the guard
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked authored · Msg 50000/127 · pre-deploy (phase pre-deploy)
          questions  Orphan(Order.(CustomerId)→Customer) · Authored(pre:Script.PreDeployment.sql:1, {Customer})
expect:   record     tier T3 · What proving showed: tried: Script.PreDeployment.sql raised Msg 50000/127 before the change ran
                     · How it ships: none decided (the authored script refused)
laws:     H5 H1
source:   expected · red-team #12; CLS-06
```

### REC-40 · a clean publish that replays silent records silence ok
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean
          delta      ALTER TABLE [dbo].[Customer] ADD [MiddleName] NVARCHAR (100) NULL;
          replay     plan empty · rows affected 0 · hashes of Customer and every reference table equal before and after
expect:   record     silence ok
laws:     D6
source:   expected · INVARIANTS D6; ARCH §5 step 11
```

### REC-41 · not-silent carries its evidence, signal by signal
```case
kind:     pure
action:   Get-FxRecord, once per replay
input:    verdict    clean
          replay     (a) plan empty · rows affected 3 (the Status MERGE) · hashes equal
                     (b) plan re-creates Product.LegacyCode · rows affected 5 · Product's hash differs
expect:   record     (a) silence not-silent(rows affected 3: post-deploy Status MERGE; hashes unchanged)
                     (b) silence not-silent(plan: adds Product.LegacyCode; rows affected 5; hash changed: Product) · the
                     verdict stays clean
laws:     D6 H1
source:   expected · INVARIANTS D6 ("otherwise the record carries not-silent with the evidence"); F12 (the unguarded MERGE
          touches 3 rows); F17 (the replay re-creates a dropped column)
```

### REC-42 · lag and drift reach the record as their own lines
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    clean (QA)
          lag        lag:QA {adds Product.Sku} · drift:QA {removes vQaReport}
expect:   record     flags {lag:QA, drift:QA} · lag:QA {adds Product.Sku} · drift:QA {removes vQaReport} · What proving
                     showed names both, apart from this PR's own effects
laws:     H4 F7
source:   expected · ARCH §3 (Compare-FxDeltas), §7; cases/README §2 (the record's lag and drift lines)
note:     The PR's effects come from delta.pr.sql; what QA receives beyond them is QA's own, and the reviewer must see it
          separately.
```

### REC-43 · a question answered only by a declared witness is declared-not-measured
```case
kind:     pure
action:   Get-FxRecord, once per question
input:    verdict    blocked conversion · Msg 245 · dbo.Product.CodeNum (a) · clean (b)
          questions  (a) Computed(Product.CodeNum) unmeasured, discharged by witness.sql line 3
                     (b) CheckFalse(T,CK_T_RefNum) unmeasured (its predicate holds CONVERT), discharged by witness.sql line 2
expect:   record     (a), (b) notChecked includes declared-not-measured · the declared rows listed by line, apart from the
                     generated plan
laws:     H4 H2 M2
source:   expected · INVARIANTS H2 ("unmeasured questions count as answered only when a declared witness covers them"),
          H4, M2
```

### REC-44 · residue names the four phases in DacFx's order
```case
kind:     pure
action:   Get-FxRecord
input:    verdict    blocked unique · Msg 1505 · UX_Product_Code (phase main)
          options    IncludeTransactionalScripts=False
          delta      pre-deploy: one backfill UPDATE · main: ALTER COLUMN Product.Code, then CREATE UNIQUE INDEX
                     UX_Product_Code · post-deploy: the seed · validation: none
          replan     CREATE UNIQUE NONCLUSTERED INDEX [UX_Product_Code] ON [dbo].[Product]([Code] ASC); plus the
                     post-deploy
expect:   record     residue {pre-deploy: applied · main: Product.Code altered, UX_Product_Code not built · post-deploy:
                     not run · validation: not run}, in that order
laws:     H6
source:   expected · INVARIANTS H6 (pre-deploy, main, post-deploy, validation); red-team #4 (failed deploys are not atomic ✓)
```

### REC-45 · engine warnings are recorded and never decide
```case
kind:     pure
action:   Get-FxRecord, once per verdict
input:    verdict    (a) clean with warnings {1945: IX_N_Big} · (b) blocked guard · Msg 50000/127 · dbo.Product with
                     warnings {SQL72015, SQL72016}
expect:   record     (a) verdict clean · warnings {1945} · (b) verdict blocked guard · warnings {SQL72015, SQL72016}
laws:     H5 H1
source:   expected · ARCH §6 ("engine warnings (Msg 1945; SQL72015, SQL72016) never decide a verdict; they are recorded
          under warnings")
```
