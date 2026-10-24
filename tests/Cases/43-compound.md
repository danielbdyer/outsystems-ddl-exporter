# 43 · Compound cases: releases and programs

*Release-grain molecules, in `README.md`'s grammar: the unit of proof is the release delta (FINDINGS Part 7, `sample-prs/compound/`), and a program is proven release by release on one copy (INVARIANTS F8).*
*Unless `env` says otherwise, the environment proven is Dev (QA, UAT and Prod unread), the effective options are the pipeline's (BlockOnPossibleDataLoss=true, GenerateSmartDefaults=false, DropObjectsNotInSource=false, IncludeTransactionalScripts=true), and reference rows come from the `referenceData` lane as of base.*
*`notChecked` uses only INVARIANTS H4's closed tokens; the `leg1`/`leg2` and citation conventions are those of `42-ops-tables.md`; bare `F<n>` in `source` cites FINDINGS_AND_CHANGES.md, `laws` cite INVARIANTS. A program proven with `--program` reports each release and its replay in one record, with `silence` per release (INVARIANTS D6; an R1 replay is the F17 hazard).*

---

## Additive batches

### CMP-01 · an all-additive release ships as one delta
```case
kind:     corpus
env:      pg · Customer 5 rows · CustomerAddress 5 rows, every CustomerId a real Customer · Order 4 rows
          · OrderLine 8 rows
change:   head + ReturnReason (Id int PK explicit · Code nvarchar(30) · IsActive bit DF 1) and its guarded seed
          (Damaged, WrongItem, NoLongerNeeded) · + Return (Id IDENTITY PK · OrderLineId → OrderLine · ReturnReasonId →
          ReturnReason · Quantity int DF 1) · + FK_CustomerAddress_Customer_CustomerId over the populated
          CustomerAddress · Order + ReturnsAllowed bit NOT NULL DEFAULT (1)
expect:   answers    Orphan(CustomerAddress.(CustomerId)→Customer)=clean · SeedDrift(ReturnReason)=clean (answered from
                     the delta: a new table)
          questions  Orphan(CustomerAddress.(CustomerId)→Customer) · Authored(Data/Seed.sql) · SeedDrift(ReturnReason) ·
                     measures Rows(Order) (Scan: the defaulted NOT NULL add); the two Return keys check only Return,
                     which this delta creates, so they are answered from the delta (ARCH §4)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.ReturnReason:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(CustomerAddress) (CustomerId linked to a planted Customer ancestor) ·
                                presence(Order) · declared(none: the seed writes only ReturnReason)
                     outcome    verdict clean · exit 0
                     copy       ReturnReason 3 rows · every foreign key is_not_trusted=0
                     record     tier T3 · ships ONE release, one publish; DacFx orders the objects
                                · effects {adds ReturnReason, Return, FK_Return_OrderLine, FK_Return_ReturnReason,
                                FK_CustomerAddress_Customer_CustomerId, Order.ReturnsAllowed} · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, engine-version,
                                drift-since-measure, integration-studio}
laws:     F1 F4 A5 D6
source:   receipt · F14 (PG_mol_x1); F19 (every key trusted on 170.5.76); sample-prs/compound/additive-batch.md
note:     The release takes its strictest question's tier: one populated child makes it T2 and its seed T3, while no
          atom can block on row presence. Nothing is hand-ordered inside a release.
```

### CMP-02 · the Twin's additive batch (parity)
```case
kind:     corpus
env:      pg · Order 6 rows · OrderLine 8 rows
change:   head + ReturnReason + Return (keys to OrderLine and ReturnReason) + Order.ReturnsAllowed bit NOT NULL
          DEFAULT (1); no seed, no key over a populated child
expect:   questions  none (Scan: the defaulted NOT NULL add) · measures Rows(Order)
          witnesses  presence(Order)
          outcome    verdict clean · exit 0
          copy       Return and ReturnReason present · FK_Return_OrderLine and FK_Return_ReturnReason present
          record     tier T0 · ships ONE release · effects {adds ReturnReason, Return, FK_Return_OrderLine,
                     FK_Return_ReturnReason, Order.ReturnsAllowed} · silence ok · notChecked {unread:QA, unread:UAT,
                     unread:Prod, integration-studio}
laws:     F3 F4 D6 T2
source:   receipt · SamplePr "compound additive batch: two tables, two keys, and a defaulted NOT NULL column land in one
          publish"
note:     The defaulted NOT NULL column writes every Order row but can refuse none: a `Scan` (ARCH §4), which asks no
          question and measures Rows(Order) for the scale flag.
```

---

## The veto, and where it is not atomic

### CMP-03 · a rename and a populated tightening in one release: the atomic veto
```case
kind:     corpus
env:      pg · Customer 5 rows · ContactPhone populated ×5 · Email NULL ×2
change:   refactorlog Rename Refactor [dbo].[Customer].[ContactPhone] → [MobileNumber]
          · Customer.Email NULL → NOT NULL ·
          one release, extract style (no scripts)
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          questions  HasRows(Customer) · NotNull(Customer.Email); the sp_rename reads no row
          witnesses  presence(Customer) · null(Customer.Email)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · residue {}: ContactPhone present, MobileNumber absent, Email nullable; the innocent
                     rename rolled back with the veto · ships serialized: the rename alone, then the tightening as its
                     own two releases (CMP-15) · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure,
                     integration-studio}
laws:     H5 H6 F1
source:   receipt · F13 (PG_mol_x1); sample-prs/compound/rename-then-tighten.md
note:     Atomic because the guard runs inside the transaction IncludeTransactionalScripts=true opens. CMP-05 and CMP-06
          are where a failed release is not atomic.
```

### CMP-04 · the Twin's atomic veto (parity)
```case
kind:     corpus
env:      pg · Order 6 rows · Product 5 rows, Name populated ×5
change:   Order + Memo nvarchar(100) NULL · Product.Name NULL → NOT NULL · one release
expect:   answers    HasRows(Product)=yes · NotNull(Product.Name)=clean
          questions  HasRows(Product) · NotNull(Product.Name)
          witnesses  presence(Product)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     tier T2 · residue {}: Order.Memo absent · notChecked {unread:QA, unread:UAT, unread:Prod,
                     drift-since-measure, integration-studio}
laws:     H5 H6 F1 T2
source:   receipt · SamplePr "compound atomic veto: the blocking atom refuses the release and the innocent atom rolls back
          with it"
note:     The Twin tightened OrderLine.Note; here Product.Name holds no NULL at all. The guard is data-blind (FINDINGS F7),
          so the veto needs no violating row.
```

### CMP-05 · the correction: a 547 at WITH CHECK CHECK lands after the commit and leaves the rest applied
```case
kind:     corpus
env:      pg · Customer 5 rows · Status 3 rows (lane) · Order 4 rows, one with StatusId 9 (no Status 9)
change:   Customer + MiddleName nvarchar(50) NULL · Order + FK_Order_Status → Status · one release ·
          options IncludeTransactionalScripts=true
expect:   answers    Orphan(Order.(StatusId)→Status)=violating
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  presence(Order) (StatusId linked to a lane Status) · orphan(Order.(StatusId)→Status)
          outcome    verdict blocked constraint · Msg 547 · FK_Order_Status · exit 3
          record     tier T2 · residue {main: Customer.MiddleName added, FK_Order_Status added WITH NOCHECK, committed ·
                     validation: FK_Order_Status present, untrusted (is_not_trusted=1)} · ships one release with a
                     pre-deploy reconcile (create-fk-orphan) · notChecked {unread:QA, unread:UAT, unread:Prod,
                     engine-version, drift-since-measure, integration-studio}
laws:     H6 F1 H5
source:   receipt · red-team #4 ✓ (verified 2026-10-15, TWIN_SUCCESSION §2: "even with transactional scripts on, a Msg
          547 at WITH CHECK CHECK lands after the commit, leaving everything else applied and the constraint
          untrusted"); F9 (db_orphD: the failed publish leaves the key present but untrusted)
note:     FINDINGS F13's veto is atomic only for a block inside the transaction. Validation runs after the post-deploy,
          outside the transaction (H6's phase order), and this residue is what H6 requires the record to carry, and
          why a re-run must re-probe trust.
```

### CMP-06 · the correction, under default options: a 1505 leaves the index dropped and the column altered
```case
kind:     corpus
env:      pg + IX_Product_Code (nonclustered, not unique) on Product(Code) · Product 5 rows, Code 'A100' ×2 ·
          options IncludeTransactionalScripts=false (DacFx's default)
change:   Product.Code nvarchar(50) → nvarchar(60) · IX_Product_Code made UNIQUE · one release
expect:   answers    Fits(Product.Code→nvarchar(60))={} · Unique(Product,(Code))={exact}
          questions  Fits(Product.Code→nvarchar(60)) · Unique(Product,(Code))
          witnesses  presence(Product) · dup(Product,(Code), exact)
          outcome    verdict blocked unique · Msg 1505 · IX_Product_Code · exit 3
          record     tier T2 · residue {main: IX_Product_Code dropped; Product.Code altered to nvarchar(60)}
                     · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     H6 F6 F1
source:   receipt · red-team #4 ✓ (verified 2026-10-15 at IncludeTransactionalScripts' default, TWIN_SUCCESSION §2: "a
          blocked unique-index build left the index dropped and the column altered"); F10 (a duplicate blocks Msg 1505)
note:     What a failure leaves is set by the effective options (INVARIANTS F6): proven under a profile with
          IncludeTransactionalScripts=true, this release would report residue {} and be wrong about an upper
          environment.
```

---

## The seed binds the release

### CMP-07 · the seed reverts a pre-deploy repoint it contradicts, on a green publish
```case
kind:     corpus
env:      pg · Status 3 rows (lane: 1 Pending, 2 Shipped, 3 Cancelled) · Order 4 rows; Order 3 holds StatusText
          'On Hold' and StatusId 1
change:   release M, first shape: pre-deploy inserts Status (4, 'On Hold') if missing and repoints Order 3 to StatusId 4
          · Data/Seed.sql: Status + (4, N'On Hold', 1), while its Order block still declares Order 3 with StatusId 1
          · Order + FK_Order_Status_StatusId → Status
expect:   answers    Orphan(Order.(StatusId)→Status)=clean · SeedDrift(Status)=clean
          questions  Orphan(Order.(StatusId)→Status) · Authored(Script.PreDeployment.sql) · Authored(Data/Seed.sql) ·
                     SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Order.StatusText, dbo.Order.StatusId:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Order) (StatusId linked to a lane Status) · declared(Order 3: StatusText 'On
                                Hold', the seed's own Code; StatusId 1)
                     outcome    verdict clean · exit 0
                     copy       after the publish Order 3's StatusId is 1 again: the post-deploy seed undid the
                                pre-deploy's repoint · FK_Order_Status_StatusId is_not_trusted=0
                     record     tier T3 · ships one release (M)
                                · silence not-silent(every publish repoints Order 3 in the
                                pre-deploy and the seed reverts it: 2 rows affected, the hash unchanged) · notChecked
                                {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     F3 F1 A5 D6
source:   receipt · F15 (a) (PG_mol_x1)
note:     Green and wrong. The seed is the truth surface; the pre-deploy only makes live rows match it, so the fix is
          the seed carrying the repoint. The copy's content shows the revert, and the replay's rows affected show the
          tug of war (D6).
```

### CMP-08 · the seed names a column the pre-deploy dropped: Msg 207 on a half-applied release
```case
kind:     corpus
env:      pg · Order 4 rows · release M landed (Status 4 rows, FK_Order_Status_StatusId trusted, StatusId consistent)
change:   release C1, first shape: pre-deploy `IF COL_LENGTH('dbo.Order','StatusText') IS NOT NULL BEGIN ALTER TABLE
          dbo.[Order] DROP CONSTRAINT DF_Order_StatusText; ALTER TABLE dbo.[Order] DROP COLUMN StatusText; END` ·
          the model still declares both · the Order seed still names StatusText
expect:   questions  Authored(Script.PreDeployment.sql); the plan, computed before the pre-deploy, carries nothing for
                     StatusText
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Order.StatusText:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(presence(Order))
                     outcome    verdict blocked authored · Msg 207 · post-deploy · exit 3
                     record     tier T3 · residue {pre-deploy: DF_Order_StatusText and StatusText dropped ·
                                post-deploy: the seed failed on StatusText} · ships C1 with the corrected seed in its
                                change set
laws:     H6 H5 A5
source:   receipt · F15 (b) (PG_mol_x1); F6 (a pre-deploy's work survives a failed deploy)
```

### CMP-09 · the rename face: the refactorlog renames the column, not the seed's references to it
```case
kind:     corpus
env:      pg · Customer 5 rows · ContactPhone populated ×5 · project style; Data/Seed.sql names ContactPhone six times
change:   refactorlog Rename Refactor [dbo].[Customer].[ContactPhone] → [MobileNumber] · the seed unchanged
expect:   questions  none: the sp_rename reads no row, and the seed did not change, so it is not `Authored`
          witnesses  none
          outcome    verdict blocked authored · Msg 207 · post-deploy · exit 3
          record     tier T0 · residue {main: sp_rename applied, MobileNumber holding the values · post-deploy: the
                     seed failed on ContactPhone} · ships one release with the seed's references renamed in the same
                     change set
laws:     H6 H5 F4
source:   receipt · F15 ("the rename face failed identically until the seed's six column references were renamed with
          it"); sample-prs/compound/rename-then-tighten.md
note:     Tier T0 and a block at once: the tier reads what the delta asks, and an unchanged script naming the old column
          is no question. The publish on the copy is what catches it.
```

---

## Phases, replays and the lag window

### CMP-10 · a phase-bound pre-deploy block left in place breaks the next phase
```case
kind:     corpus
env:      pg · Order 4 rows · C1's first attempt (CMP-08) left StatusText and DF_Order_StatusText dropped; the model
          still declares them
change:   C1 again, its seed corrected · Script.PreDeployment.sql still opens with release M's reconcile block, which
          reads StatusText unguarded, ahead of C1's guarded drop
expect:   questions  Authored(Script.PreDeployment.sql); the lagging model's plan re-adds StatusText with its default
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Order.StatusText:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(presence(Order))
                     outcome    verdict blocked authored · Msg 207 · pre-deploy · exit 3
                     record     tier T3 · residue {pre-deploy: the first block failed before anything else ran}
laws:     H5 E1 A5
source:   receipt · F16 (PG_mol_x1: "Msg 207 before anything else ran"); F6 (a pre-deploy step must be safe to re-run
          over a partial state)
note:     Any error inside a pre-deploy batch is `blocked authored` (H5, ARCH §6). Retiring M's block is C1's
          change-set work.
```

### CMP-11 · replaying the contract release re-creates the dropped column, backfilled from its default
```case
kind:     corpus
env:      pg · Order 4 rows · release M landed
change:   program C1: pre-deploy guarded drop of DF_Order_StatusText and StatusText, the model lagging,
          the seed no longer
          naming StatusText, M's block retired · C2: the model drops StatusText and its default, C1's block retired
expect:   answers    HasRows(Order)=yes
          questions  from C2's target (F8): HasRows(Order) · Authored(Script.PreDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Order.StatusText:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Order) · `-- shapes: none (the script reads no value)`
                     outcome    verdict clean · exit 0
                     record     C1 tier T3 · C2 tier T0 · C1 clean (StatusText gone) · C2 clean, empty plan · silence C1
                                not-silent(replay: `Successfully published`, plan non-empty, ADD [StatusText] … DEFAULT
                                'Pending', every row re-created as 'Pending' and the original text destroyed: the F17
                                hazard) · C2 ok · flags {in-flight} from C1 to C2 · ships two releases,
                                C2 promptly after
                                C1 with other publishes to the environment held · notChecked {unread:QA, unread:UAT,
                                unread:Prod, drift-since-measure, integration-studio}
laws:     F8 F1 A5 D6
source:   receipt · F17 (PG_mol_x1); F4 (the second release of a two-release is a no-op)
note:     Harmless in this program only because release M had already moved the information into StatusId.
```

### CMP-12 · the in-flight stop holds another change to a table in an open window
```case
kind:     process
setup:    estate/in-flight.md holds `| CHG-0412 | extract Order.StatusText | dbo.Order | 2 | 3 | C2 | 2026-11-01 | #801 |`
          · answers.Dev.json current for the change below
action:   facsimile prove Dev, for a change adding Order.Memo nvarchar(100) NULL
expect:   outcome    stop in-flight · "dbo.Order is in an open two-release window (estate/in-flight.md). Hold this change,
                     or make it the window's next release." · exit 5
          stdout     one line in A1's grammar, naming the stop and its JSON file
laws:     H3 A1 A3
source:   expected · ARCH §2 row 27 (the lag-window hold, ported as the `in-flight` stop); F17's remedies ("the
          in-flight tables column + scripts/inflight-check.mjs")
note:     The same command for the window's own C2 proceeds to a verdict; a change touching no listed table is never held.
```

### CMP-13 · a narrow's R1 replayed widens the column back
```case
kind:     corpus
env:      pg · Product 5 rows · Code LEN max 9, no trailing-space value
change:   program R1: model Code nvarchar(50) unchanged · pre-deploy, guarded on the column's current length,
          shortens any
          value over 10 and runs `ALTER TABLE dbo.Product ALTER COLUMN Code nvarchar(10) NOT NULL` · R2: model
          nvarchar(10), the pre-deploy retired
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→nvarchar(10))={}
          questions  from R2's target (F8): HasRows(Product) · Fits(Product.Code→nvarchar(10)) ·
                     Authored(Script.PreDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Product.Code:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Product) · declared(none beyond presence: Dev holds nothing over 10 and no
                                trailing-space twin)
                     outcome    verdict clean · exit 0
                     record     tier T3 · R1 clean (Code nvarchar(10)) · R2 clean, empty plan · silence R1
                                not-silent(replay plan: ALTER COLUMN Code nvarchar(50), the column widens back: the F17
                                hazard, no data lost) · R2 ok · flags {in-flight} between R1 and R2
                                · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure,
                                integration-studio}
laws:     F8 F1 A5 D6
source:   receipt · F3 ("re-publishing (model still says 50) reverted the column to 50"); F4 (prove_2rel: R1 → 10, R2 a
          no-op, re-publish stable)
```

### CMP-14 · a two-release make-mandatory whose R1 model diff is empty still plants R2's witnesses
```case
kind:     corpus
env:      pg · Customer 6 rows · Email NULL ×2
change:   program R1: model Email NULL unchanged · pre-deploy, guarded by
          `COLUMNPROPERTY(OBJECT_ID('dbo.Customer'), 'Email', 'AllowsNull') = 1`, backfills NULL Emails and runs
          `ALTER COLUMN Email nvarchar(256) NOT NULL` · R2: model Email NOT NULL, the pre-deploy retired
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          questions  from R2's target (F8): HasRows(Customer) · NotNull(Customer.Email)
                     · Authored(Script.PreDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.Email:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Customer) · null(Customer.Email)
                                · declared('' if `shape Dev dbo.Customer.Email`
                                reports one)
                     outcome    verdict clean · exit 0
                     record     tier T3 · R1 clean (Email NOT NULL, 0 NULL) · R2 clean, empty plan · silence R1
                                not-silent(replay plan: ALTER COLUMN Email NULL,
                                the tightening undone: the F17 hazard) ·
                                R2 ok · flags {in-flight} between R1 and R2 · notChecked {unread:QA, unread:UAT,
                                unread:Prod, drift-since-measure, integration-studio}
laws:     F8 F1 A5 D6
source:   receipt · F7 (mm_2r: R1 lands NOT NULL, R2 a no-op, re-publish stable); the replay's loosening is F3's face on
          this operation
note:     R1's own model diff is empty: without INVARIANTS F8 nothing would be planted, and the backfill would run
          over an empty copy (red-team #2). The null witness needs two rows, which also serve as presence (M2).
```

### CMP-15 · rename, then tighten: the serialized program
```case
kind:     corpus
env:      pg · Customer 5 rows · ContactPhone populated ×5 · Email NULL ×2
change:   program R1: refactorlog rename ContactPhone → MobileNumber, the seed's six references renamed with it ·
          R2a: CMP-14's R1 · R2b: CMP-14's R2
expect:   questions  R1: Authored(Data/Seed.sql) · R2a and R2b: as CMP-14
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  as CMP-14, plus the seed's Customer rows declared
                     outcome    verdict clean · exit 0
                     record     R1 clean (MobileNumber holds 5 of 5) · R2a clean · R2b clean, empty plan
                                · silence R1 ok ·
                                R2a not-silent(replay: the F17 hazard) · R2b ok · ships three releases on one table, in
                                order: the rename in place, then the tightening's two · notChecked {unread:QA,
                                unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     F8 A5 D6
source:   expected · sample-prs/compound/rename-then-tighten.md (recorded live, sqlpackage 170.5.76: Release 1 is the
          single sp_rename plus the corrected seed); F7 for the tightening's two releases
note:     CMP-03 is why the atoms serialize; one program proof on one copy covers all three releases.
```

### CMP-16 · the tightened column is the one renamed in the same release
```case
kind:     corpus
env:      pg · Customer 5 rows · ContactPhone populated ×5, NULL ×0
change:   refactorlog rename ContactPhone → MobileNumber and MobileNumber nvarchar(40) NULL → NOT NULL, one release
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.ContactPhone)=clean
          questions  HasRows(Customer) · NotNull(Customer.MobileNumber), resolved to the environment's
                     Customer.ContactPhone through the delta's own sp_rename and probed under the old name
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · residue {} (the rename rolls back with the veto)
laws:     S3 F4 F1
source:   verify · whether DacFx emits the sp_rename before the guarded ALTER on the new name; the resolution itself is
          ARCH §4's coordinate rule 2 and INVARIANTS S3 (renamed earlier in the delta → probed under the old name)
note:     Without the resolution rule this release would stop `missing-object` and never reach CMP-03's veto.
```

### CMP-17 · the extract-to-lookup program, end to end
```case
kind:     corpus
env:      pg · Status 3 rows (lane) · Order 4 rows · StatusText {Pending ×2, Shipped ×1, On Hold ×1}; Order 3 holds
          'On Hold' with StatusId 1 (one unmapped value, one disagreement)
change:   program M: pre-deploy reconcile (insert Status 4 'On Hold' if missing, repoint Order 3 where it is not yet
          repointed), Status seed + (4, N'On Hold', 1), Order seed repointing row 3, + FK_Order_Status_StatusId · C1:
          pre-deploy guarded drop of DF_Order_StatusText and StatusText, the model lagging, the Order seed no longer
          naming StatusText, M's block retired · C2: the model drops StatusText and its default, C1's block retired
expect:   answers    HasRows(Order)=yes · Orphan(Order.(StatusId)→Status)=clean · SeedDrift(Status)=clean
          questions  from C2's target (F8): HasRows(Order) · Orphan(Order.(StatusId)→Status) · SeedDrift(Status) ·
                     Authored per release
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Order.StatusText, dbo.Order.StatusId:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Order) (StatusId linked to a lane Status) · declared(Order 3: StatusText 'On
                                Hold', the seed's own Code; StatusId 1)
                     outcome    verdict clean · exit 0
                     record     M tier T3 · C1 tier T3 · C2 tier T0 · M clean (FK trusted, 0 unmapped,
                                0 disagreements) ·
                                C1 clean (StatusText gone) · C2 clean, empty plan · silence M ok · C1 not-silent(replay
                                plan: StatusText re-created from its default, the F17 hazard) · C2 ok · ships three
                                releases, M, C1, then C2 promptly, publishes held between C1 and C2 · flags {in-flight}
                                from C1 to C2 · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                                drift-since-measure, integration-studio}
laws:     F8 F1 A5 H2 D6
source:   receipt · F15, F16, F17, F19 (PG_mol_x1, the program run release by release); F4 (the last release of a
          two-release is a no-op); sample-prs/compound/extract-to-lookup-program.md
note:     The phase cases are OP-extract-to-lookup-*; this case is the order they compose in, on one copy.
```

---

## Per-environment deltas

### CMP-18 · R1 and R2 both merged before QA deploys: QA receives FINDINGS F2's shape
```case
kind:     corpus
env:      Dev: pg · Customer 6 rows · Email NOT NULL already (R1 landed) · QA: pg · Customer 6 rows · Email nullable,
          NULL ×2 (R1 never deployed)
change:   head = R2 merged on top of R1: the model declares Email NOT NULL and R1's guarded pre-deploy (backfill, then
          ALTER … NOT NULL) is still present · proved per environment: leg1 `prove Dev`, leg2 `prove QA`
expect:   leg1       questions  none
                     outcome    verdict clean · exit 0 (plan empty, the pre-deploy's guard skips)
                     record     tier T0 · silence ok
          leg2       answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
                     questions  HasRows(Customer) · NotNull(Customer.Email); R1's pre-deploy is unchanged since R1
                                merged, so it is `Authored` only if "changed against the base" is read per environment
                                (QA never ran it)
                     witnesses  presence(Customer) · null(Customer.Email)
                     outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
                     record     tier T2 · residue {pre-deploy: NULL Emails backfilled, Email altered NOT NULL}: the
                                half-apply · notChecked {unread:UAT, unread:Prod, drift-since-measure,
                                integration-studio}
laws:     F7 H6 F1 D6
source:   receipt · F2 (prove_nvB: the model change and the pre-deploy ALTER in one release block AND half-apply);
          red-team #1
note:     Proven against main or Dev the release is clean; QA's own extracted schema carries FINDINGS F2's shape
          (TWIN_SUCCESSION §3 step 2). Two proofs, because the delta hashes differ (F7).
```

### CMP-19 · R2 alone reaches a QA that skipped R1
```case
kind:     corpus
env:      Dev: pg · Customer 6 rows · Email NOT NULL (R1 landed) · QA: pg · Customer 6 rows · Email nullable, NULL ×0
change:   head = R2: the model declares Email NOT NULL; R1's pre-deploy retired · proved per environment: leg1
          `prove Dev`, leg2 `prove QA`
expect:   leg1       questions  none
                     outcome    verdict clean · exit 0
                     record     tier T0 · silence ok
          leg2       answers    HasRows(Customer)=yes · NotNull(Customer.Email)=clean
                     questions  HasRows(Customer) · NotNull(Customer.Email)
                     witnesses  presence(Customer)
                     outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
                     record     tier T2 · residue {} · notChecked {unread:UAT, unread:Prod, drift-since-measure}
laws:     F7 F1 D6
source:   receipt · F7 (mm_ax: a populated make-mandatory blocks Msg 50000 whatever the NULL content); F1 (the guard is
          table-has-rows)
note:     "Confirm R1 landed in each environment before R2" is what this proof enforces, per environment.
```

### CMP-20 · the union of environments builds a state no environment has
```case
kind:     corpus
env:      Dev: pg · Customer 5 rows · Email holding a case twin pair · QA: pg · Customer 5 rows · Email unique under
          the collation
change:   head + UX_Customer_Email: CREATE UNIQUE INDEX ON Customer(Email) WHERE Email IS NOT NULL · proved per
          environment: leg1 `prove Dev`, leg2 `prove QA`
expect:   questions  Unique(Customer,(Email),Email IS NOT NULL), counted under head's target collation for Email
          leg1       answers    HasRows(Customer)=yes · Unique(Customer,(Email),Email IS NOT NULL)={case}
                     witnesses  presence(Customer) · dup(Customer,(Email), case)
                     outcome    verdict blocked unique · Msg 1505 · UX_Customer_Email · exit 3
          leg2       answers    HasRows(Customer)=yes · Unique(Customer,(Email),Email IS NOT NULL)={}
                     witnesses  presence(Customer)
                     outcome    verdict clean · exit 0
                     record     tier T2 · silence ok
laws:     F7 F1 F3 D6
source:   expected · red-team #5 (a union of environments builds a state no environment has); INVARIANTS F7
note:     Two proofs, because the answers hashes differ. A union would plant Dev's twin in QA's copy and report QA
          blocked, a state QA does not hold; the twin's values are generated, `w`-marked (P5).
```

### CMP-21 · identical environments share one proof
```case
kind:     corpus
env:      QA and UAT: the same schema · Customer 6 rows · Email NULL ×2 in each
change:   Customer.Email NULL → NOT NULL (one release) · proved as `prove QA UAT`
expect:   answers    QA and UAT: HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          files      one proof (equal delta hash and answers hash), cited by verdict.QA.json and verdict.UAT.json ·
                     verdicts.json indexes both
          stdout     one line, the most severe outcome, naming verdicts.json
laws:     F7 D1 A1
source:   expected · INVARIANTS F7 ("two environments may share one proof only when their delta hash and answers hash
          are both equal") · ARCH §6 (a multi-environment prove prints its most severe outcome and names verdicts.json)
```

---

## Mixed releases

### CMP-22 · a release mixing an authored script with declarative atoms is T3
```case
kind:     corpus
env:      pg · Customer 5 rows · Order 4 rows, Order 4 on CustomerId 999 (no such Customer)
change:   Customer + MiddleName nvarchar(50) NULL · Order + FK_Order_Customer → Customer · pre-deploy
          `UPDATE dbo.[Order] SET CustomerId = 1 WHERE CustomerId NOT IN (SELECT Id FROM dbo.Customer);` (the
          developer's answer: reassign to Customer 1)
expect:   answers    Orphan(Order.(CustomerId)→Customer)=violating
          questions  Orphan(Order.(CustomerId)→Customer) · Authored(Script.PreDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Order.CustomerId:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Order) (+ancestor Customer) · orphan(Order.(CustomerId)→Customer) ·
                                declared(Customer 1 present, the reassignment's target)
                     outcome    verdict clean · exit 0
                     copy       FK_Order_Customer is_not_trusted=0 · Customer.MiddleName present · no orphan left
                     record     tier T3: the add-optional atom alone is T0, and a release takes its strictest question's
                                tier · ships one release (the pre-deploy reconcile, then the declarative adds) · silence
                                ok · notChecked {unread:QA, unread:UAT, unread:Prod, engine-version,
                                drift-since-measure, integration-studio}
laws:     F4 F1 A5 H3 D6
source:   receipt · F19 ("one FK over a populated child reconciled in the same release's pre-deploy" ended
          is_not_trusted = 0); F9 (db_orphB)
note:     The orphan row comes from the planter; the row the script names comes from witness.sql. The reconcile selects
          by predicate, so it also fixes the synthetic orphan; its target, Customer 1, is a literal that must exist in
          every environment, which is why it is declared. The tier is assigned from the whole delta, never from an
          atom's name (ARCH §7).
```

### CMP-23 · a counterfeit guard in the release's authored script
```case
kind:     corpus
env:      pg · Customer 6 rows · Email NULL ×1 · '' ×1
change:   CMP-14's program R1, its pre-deploy opening with a self-check that raises
          `RAISERROR(N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)`
          when it finds an Email of '' (a blank it does not handle)
expect:   questions  as CMP-14
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.Email:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Customer) · null(Customer.Email) · declared(a Customer with Email '')
                     outcome    verdict blocked authored · Msg 50000 · pre-deploy · exit 3
laws:     H5
source:   expected · red-team #12 (Msg 50000 state 127 can be counterfeited); INVARIANTS H5
note:     Not `blocked guard`: the failing batch is the pre-deploy's, not a guard batch of the delta (R1's model diff is
          empty, so the delta holds none), and any error inside an authored batch is `blocked authored` (H5).
```

### CMP-24 · the additive batch against an unread Prod: both worlds
```case
kind:     process
setup:    CMP-01's release on a branch, its witness.sql declared · answers.Dev.json measured · facsimile.json declares
          Prod with "read": "none"
action:   facsimile prove Dev Prod --assume Prod=worst
expect:   outcome    verdict blocked constraint · Msg 547 · FK_CustomerAddress_Customer_CustomerId · exit 3 (Prod's
                     violating world, the most severe; Dev as CMP-01, clean; Prod's clean world clean)
          stdout     one line, the most severe outcome, naming verdicts.json
          files      verdict.Prod.json stamped `assumed`, with both worlds, the schema taken from Dev, the violating
                     world's residue {main: the rest of the batch applied · validation: the key untrusted}, and the
                     probe to run before promoting (the CustomerAddress orphan count) · verdicts.json indexes Dev and
                     Prod
          record     Prod flags {unread:Prod, assumed:Prod} · notChecked {assumed:Prod, drift-since-measure,
                     engine-version, integration-studio}
laws:     H4 H6 A1
source:   expected · ARCH §8 (`--assume <env>=worst`); TWIN_SUCCESSION §3 step 7 (Prod: both worlds, the Msg the
          violating world would hit, and the probe to run before promoting)
note:     The violating world's residue is CMP-05's: an orphan in Prod would leave the rest of this batch applied.
```
