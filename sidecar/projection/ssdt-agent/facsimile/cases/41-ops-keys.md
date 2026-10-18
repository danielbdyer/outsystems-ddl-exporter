# Operation cases: constraints, keys and indexes

*Family `41-ops-keys` (`OP-<slug>-nn`): the fifteen constraint, key and index operations, in the grammar of `README.md`. Unless `env` names others, the effective options
are the proving-ground Pipeline profile (IncludeTransactionalScripts=True, DropObjectsNotInSource=False; DacFx's granular DropIndexes/DropConstraintsNotInSource defaults on); `pg` has no FKs, so `env` or `change` adds them; the stand-in environment is Dev.*

## add-check

### OP-add-check-01 · empty table: the check is added and re-validates nothing
```case
kind:     corpus
tags:     empty
env:      pg · Order 0 rows
change:   head adds CK_Order_Total CHECK (Total > 0) to Order · scripts unchanged
expect:   answers    HasRows(Order)=no · CheckFalse(Order,CK_Order_Total)=clean
          questions  CheckFalse(Order,CK_Order_Total)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted · effects {adds CK_Order_Total}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 F3 F4
source:   expected · skills/op/add-check · F10 (the add is WITH NOCHECK ADD + WITH CHECK CHECK whatever the data)
note:     the tier is T2 on an empty table too: it follows the delta's statements, never the rows (ARCH §7).
```

### OP-add-check-02 · populated, every row satisfies the rule: lands trusted in one release
```case
kind:     corpus
tags:     clean
env:      pg · Order 4 rows · Total all > 0
change:   head adds CK_Order_Total CHECK (Total > 0) to Order · scripts unchanged
expect:   answers    HasRows(Order)=yes · CheckFalse(Order,CK_Order_Total)=clean
          questions  CheckFalse(Order,CK_Order_Total)
          witnesses  presence(Order)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusts itself (is_not_trusted=0)
                     effects {adds CK_Order_Total} · notChecked {QA, UAT, Prod, volume, engine version (trust)}
laws:     F1 F3 H4 T2
source:   receipt · F10 (DB db_chk: clean → LAND, is_not_trusted = 0) · F19 · SamplePr "add-check: a violating row blocks the check, conforming data applies"
note:     the presence row must itself satisfy the new rule; OP-add-check-07 is the case where the planter's literal does not.
```

### OP-add-check-03 · populated, only NULLs where the rule looks: UNKNOWN is not FALSE, so it passes
```case
kind:     corpus
tags:     clean
env:      pg · Customer 4 rows · Region NULL×2 · 'North'×2
change:   head adds CK_Customer_Region CHECK (Region <> N'') to Customer · scripts unchanged
expect:   answers    HasRows(Customer)=yes · CheckFalse(Customer,CK_Customer_Region)=clean
          questions  CheckFalse(Customer,CK_Customer_Region)
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted · effects {adds CK_Customer_Region}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 F3
source:   expected · ARCH §4 CheckFalse row ("FALSE (not UNKNOWN)") · skills/_index/constraint-is-a-claim
note:     the probe is WHERE NOT (predicate), which a NULL never satisfies. A probe that counted NULL rows would answer
          violating and stop for a witness the environment does not need; the oracle's clean publish exposes it (F3).
```

### OP-add-check-04 · populated, one row FALSE: the agent writes the witness, then 547
```case
kind:     corpus
tags:     violating:false
env:      pg · Order 4 rows · Total -5.00×1
change:   head adds CK_Order_Total CHECK (Total > 0) to Order · scripts unchanged
          run 2 adds witness.sql: one Order row, Total -5.00
expect:   answers    HasRows(Order)=yes · CheckFalse(Order,CK_Order_Total)=violating
          questions  CheckFalse(Order,CK_Order_Total)
          witnesses  run 1 needs declared(CK_Order_Total FALSE)
                     run 2 presence(Order) · declared(Order Total -5.00)
          outcome    run 1 stop check-witness · "write one row into witness.sql that makes CK_Order_Total FALSE,
                     then re-run" · exit 5
                     run 2 verdict blocked constraint · Msg 547 · CK_Order_Total · exit 3
          record     run 2 tier T2 · ships only after a pre-deploy reconciles the row (OP-add-check-05)
                     residue {validation: CK_Order_Total present, untrusted (is_not_trusted=1)}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 F3 H2 H3 H6 A5 T2
source:   receipt · F10 (DB db_chkv: Total = -5 → BLOCK Msg 547, "conflicted with the CHECK constraint") · SamplePr "add-check: a violating row blocks the check, conforming data applies" (the block leaves the check untrusted)
note:     predicates are free-form, so the planter never invents a FALSE row; a CheckFalse "yes" always needs the agent's row.
```

### OP-add-check-05 · remedy: a pre-deploy reconciles the FALSE rows, then the check trusts itself
```case
kind:     corpus
tags:     remedy
env:      pg · Order 4 rows · Total -5.00×1
change:   head adds CK_Order_Total CHECK (Total > 0) to Order
          pre-deploy: UPDATE [Order] SET Total = 0.01 WHERE Total <= 0
          run 2 adds witness.sql: one Order row, Total -5.00 (the shape the script handles)
expect:   answers    HasRows(Order)=yes · CheckFalse(Order,CK_Order_Total)=violating
          questions  Authored(pre-deploy) · CheckFalse(Order,CK_Order_Total)
          witnesses  run 2 presence(Order) · declared(Order Total -5.00)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.Order.Total; declare a witness for
                     every shape the script handles" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T3 · ships one release (pre-deploy reconcile), trusts itself, no manual trust step
                     effects {adds CK_Order_Total} · notChecked {QA, UAT, Prod, change over time, engine version (trust)}
laws:     F2 F3 H2 H3 A5
source:   expected · skills/op/add-check ("a pre-deploy step brings them into compliance first") · F10 (reconcile in a pre-deploy, no manual trust step)
note:     one declared row discharges both needs: the authored-script look and the check-witness FALSE row.
```

### OP-add-check-06 · a predicate that converts: never probed (P3); the agent looks, declares, then 245
```case
kind:     corpus
tags:     edge
env:      pg · CustomerAddress 10 rows · PostalCode five-digit×4 · NULL×1 · 'SW1A 1AA'-shaped×5
change:   head adds CK_CustomerAddress_PostalCode CHECK (CONVERT(int, PostalCode) BETWEEN 1000 AND 99999)
          run 2 adds witness.sql: one CustomerAddress row, PostalCode 'wA1 1AA' (the AA9A_9AA shape facsimile
          shape reported)
expect:   answers    HasRows(CustomerAddress)=yes
                     CheckFalse(CustomerAddress,CK_CustomerAddress_PostalCode)=unmeasured (measured:false)
          questions  CheckFalse(CustomerAddress,CK_CustomerAddress_PostalCode)
          witnesses  run 2 presence(CustomerAddress) · declared(PostalCode 'wA1 1AA')
          outcome    run 1 stop check-witness · "CK_CustomerAddress_PostalCode converts, so Dev was not probed (P3).
                     Run facsimile shape Dev dbo.CustomerAddress.PostalCode, then write one row into witness.sql
                     that makes the check FALSE or fail, or declare that none exists" · exit 5
                     run 2 verdict blocked conversion · Msg 245 · CK_CustomerAddress_PostalCode · exit 3
          record     run 2 tier T2 · residue {validation: CK_CustomerAddress_PostalCode present, untrusted}
                     notChecked {QA, UAT, Prod, Dev's answer declared from a look, not measured (P3)}
laws:     P3 P5 H2 H3 A5 F3
source:   expected · INVARIANTS P3 (probes never CONVERT/CAST; their errors quote the value) · ARCH §4 CheckFalse row · ARCH §0 (authored meaning is looked at, then declared)
note:     decided: a predicate containing CONVERT, CAST or PARSE is never sent to an environment, and rewriting it to TRY_
          would turn a failing row into UNKNOWN and predict clean. The answer stays measured:false, so H2 forbids clean and the
          run stops. The env holds five of the shape because shape suppresses signatures seen fewer than k = 5 times (P6).
```

### OP-add-check-07 · the planter's presence literal breaks the new rule: acceptance stops it
```case
kind:     corpus
tags:     edge
env:      pg · Order 3 rows · Total all > 100
change:   head adds CK_Order_Total_Min CHECK (Total > 100) to Order
          run 2 adds witness.sql: one valid Order row, Total 150.00, under -- plants: Order
expect:   answers    HasRows(Order)=yes · CheckFalse(Order,CK_Order_Total_Min)=clean
          questions  CheckFalse(Order,CK_Order_Total_Min)
          witnesses  run 1 presence(Order) (the planter's ordinal literal for Total, at or below 100)
                     run 2 declared(plants: Order, Total 150.00) in place of the planter's row
          outcome    run 1 stop acceptance · "the copy answers CheckFalse(Order,CK_Order_Total_Min) yes, but Dev
                     answered no. Fix witness.sql" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T2 · ships one release, in place, trusted · notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 H2 H3 A5
source:   verify · ARCH §11.7: whether the ordinal literal for a decimal(18,2) column falls at or below 100 is what the first run decides; the case pins that acceptance, not the publish, catches a presence row the new rule refuses
note:     the new rule is not in the copy when the planter runs, so nothing stops it planting a row the rule would refuse.
```

## drop-check

### OP-drop-check-01 · empty table: one DROP CONSTRAINT
```case
kind:     corpus
tags:     empty
env:      pg + CK_Order_Total_NonNegative CHECK (Total >= 0), trusted · Order 0 rows
change:   head removes CK_Order_Total_NonNegative from Order
expect:   answers    HasRows(Order)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes CK_Order_Total_NonNegative}
                     notChecked {QA, UAT, Prod}
laws:     F3 F4
source:   expected · skills/op/drop-check
```

### OP-drop-check-02 · populated: nothing validated, nothing blocks, the rule's return is deferred
```case
kind:     corpus
tags:     clean
env:      pg + CK_Order_Total_NonNegative CHECK (Total >= 0), trusted · Order 4 rows · Total all >= 0
change:   head removes CK_Order_Total_NonNegative from Order
expect:   answers    HasRows(Order)=yes
          questions  none
          witnesses  presence(Order)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes CK_Order_Total_NonNegative}
                     notChecked {QA, UAT, Prod, change over time: rows written while the rule is gone are
                     re-validated if it returns (add-check)}
laws:     F3 F4 H4 T2
source:   receipt · SamplePr "drop-check: the drop is clean; a violating row written in the gap refuses the re-add" · sample-prs/drop-check.md (PG_inv_x1, sqlpackage 170.5.76)
note:     violating and remedy cannot occur: a drop validates nothing.
```

### OP-drop-check-03 · the re-add, later: a FALSE row written in the gap blocks the rule's return
```case
kind:     corpus
tags:     edge
env:      pg (CK_Order_Total_NonNegative dropped by an earlier release) · Order 5 rows · Total -5.00×1 written in the gap
change:   head re-adds CK_Order_Total_NonNegative CHECK (Total >= 0) to Order
          run 2 adds witness.sql: one Order row, Total -5.00
expect:   answers    HasRows(Order)=yes · CheckFalse(Order,CK_Order_Total_NonNegative)=violating
          questions  CheckFalse(Order,CK_Order_Total_NonNegative)
          witnesses  run 2 presence(Order) · declared(Order Total -5.00)
          outcome    run 1 stop check-witness · "write one row into witness.sql that makes CK_Order_Total_NonNegative
                     FALSE, then re-run" · exit 5
                     run 2 verdict blocked constraint · Msg 547 · CK_Order_Total_NonNegative · exit 3
          record     run 2 tier T2 · residue {validation: CK_Order_Total_NonNegative present, untrusted}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F3 H6 A5 T2
source:   receipt · SamplePr "drop-check: the drop is clean; a violating row written in the gap refuses the re-add"
```

### OP-drop-check-04 · an environment-only check is dropped by an unrelated release
```case
kind:     corpus
tags:     edge
env:      pg + CK_Product_Code_NotBlank CHECK (Code <> N'') on Product, environment-only (not in the project)
          · Customer 6 rows · Product 5 rows
change:   head adds Customer.MiddleName nvarchar(50) NULL (add-optional) · nothing touches Product
expect:   answers    HasRows(Customer)=yes · HasRows(Product)=yes
          questions  none
          witnesses  presence(Customer) · presence(Product)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place
                     effects {adds Customer.MiddleName · removes CK_Product_Code_NotBlank (environment-only)}
                     notChecked {QA, UAT, Prod}
laws:     F3 F7 H4
source:   receipt · red-team #14 (environment-only objects dropped silently ✓)
note:     DacFx's DropConstraintsNotInSource default emits the drop; only the delta read from this environment carries it
          (F7), so the record must list a removal the developer never asked for.
```

## add-unique

### OP-add-unique-01 · empty table: the unique index builds over nothing
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=no · Unique(Customer,(Email))={}
          questions  Unique(Customer,(Email))
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · effects {adds UIX_Customer_Email}
                     notChecked {QA, UAT, Prod}
laws:     F1 F3
source:   expected · skills/op/add-unique
```

### OP-add-unique-02 · populated, distinct values and one NULL: builds
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · Email distinct×4 · NULL×1
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))={}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place (build-or-block, no trust state) · effects {adds UIX_Customer_Email}
                     notChecked {QA, UAT, Prod, volume}
laws:     F1 F3 T2
source:   receipt · F10 ("clean values (including one NULL) build") · SamplePr "add-unique: duplicate values block the unique index, a unique column applies"
```

### OP-add-unique-03 · violating:exact: an equal pair blocks the build
```case
kind:     corpus
tags:     violating:exact
env:      pg · Customer 5 rows · Email equal pair×1 · distinct×3
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))={exact}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer) · dup(Customer,(Email),exact)
          outcome    verdict blocked unique · Msg 1505 (+1750) · UIX_Customer_Email · exit 3
          record     tier T2 · ships only after a pre-deploy de-dupe (OP-add-unique-08) · residue {none}
                     notChecked {QA, UAT, Prod}
laws:     F1 F3 H6 M2 T2
source:   receipt · F10 (a duplicate → Msg 1505, "duplicate key value is (A)") · SamplePr "add-unique: duplicate values block the unique index, a unique column applies"
note:     M2: one dup pair for the one class present, plus presence; the planted pair carries synthetic w-prefixed values (P5).
```

### OP-add-unique-04 · violating:case: a case twin collides under the default case-insensitive collation
```case
kind:     corpus
tags:     violating:case
env:      pg (SQL_Latin1_General_CP1_CI_AS) · Customer 5 rows · Email case twins 'Ann@x.test' / 'ann@x.test' · distinct×3
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))={case}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer) · dup(Customer,(Email),case)
          outcome    verdict blocked unique · Msg 1505 (+1750) · UIX_Customer_Email · exit 3
          record     tier T2 · residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9 P5
source:   expected · ARCH §4 Unique row · TWIN_SUCCESSION §6b (a case twin, 4 distinct as collated vs 5 binary, "would block a unique index with Msg 1505")
note:     the class is the gap between COUNT(DISTINCT c) and COUNT(DISTINCT c COLLATE Latin1_General_BIN2); P1 keeps the values home.
```

### OP-add-unique-05 · violating:accent under a database collation the copy must reproduce
```case
kind:     corpus
tags:     violating:accent
env:      pg · database collation Latin1_General_100_CI_AI · Customer 5 rows · Email accent twins 'rené@x.test' / 'rene@x.test'
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Collation(Customer.Email)=Latin1_General_100_CI_AI · Unique(Customer,(Email))={accent}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer) · dup(Customer,(Email),accent)
          outcome    verdict blocked unique · Msg 1505 (+1750) · UIX_Customer_Email · exit 3
          record     tier T2 · residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9 P5
source:   expected · red-team #13 (the copy takes the dacpac's collation ✓) · INVARIANTS F9 (column collation)
note:     LocalDB's default collation is accent-sensitive. A copy built with it would not collide the planted twin, and acceptance
          (F1) would stop rather than report clean; building from schema.<env>.dacpac is what makes the twin collide.
```

### OP-add-unique-06 · violating:trailing: a trailing-space twin is equal under every collation
```case
kind:     corpus
tags:     violating:trailing
env:      pg · Customer 5 rows · Email 'ann@x.test' / 'ann@x.test ' (one trailing space) · distinct×3
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))={trailing}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer) · dup(Customer,(Email),trailing)
          outcome    verdict blocked unique · Msg 1505 (+1750) · UIX_Customer_Email · exit 3
          record     tier T2 · residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 P5
source:   expected · ARCH §4 Unique row (trailing class) · skills/op/add-unique
note:     comparison pads trailing spaces whatever the collation, so the pair is one key; DISTINCT under binary still sees two.
```

### OP-add-unique-07 · violating:nulls: two NULLs block a plain unique index
```case
kind:     corpus
tags:     violating:nulls
env:      pg · Customer 5 rows · Email NULL×2 · distinct×3
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))={nulls}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer) · dup(Customer,(Email),nulls)
          outcome    verdict blocked unique · Msg 1505 (+1750) · UIX_Customer_Email · exit 3
          record     tier T2 · ships as a filtered index instead (OP-add-unique-09) or after make-mandatory
                     residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 M2
source:   receipt · F10 (DB db_uq: a second NULL → Msg 1505, "duplicate key value is (<NULL>)") · sample-prs/add-unique.md
note:     the planter fills nullable columns with non-NULL values for presence (ARCH §5.2); the nulls witness plants the pair
          of NULLs on purpose.
```

### OP-add-unique-08 · remedy: a pre-deploy de-dupe, then a filtered index
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 7 rows · Email equal pair×1 · case twins 'Bo@x.test' / 'bo@x.test' · distinct×3
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email) WHERE Email IS NOT NULL
          pre-deploy: for each Email held by more than one Customer (grouped under the column's collation), keep the
          lowest Id and set the others' Email to NULL
          run 2 adds witness.sql: the two shapes facsimile shape reported, an equal pair and a case pair
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email),Email IS NOT NULL)={exact, case}
          questions  Authored(pre-deploy) · Unique(Customer,(Email),Email IS NOT NULL)
          witnesses  run 2 presence(Customer) · dup(Customer,(Email),exact) · dup(Customer,(Email),case)
                     · declared(an equal pair) · declared(a case pair)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.Customer.Email; declare a witness for
                     every shape the script handles" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T3 · ships one release (pre-deploy de-dupe, then a filtered unique index)
                     effects {adds UIX_Customer_Email (filtered)} · notChecked {QA, UAT, Prod, change over time}
laws:     F1 F2 F3 H2 A5
source:   expected · skills/op/add-unique (de-dupe first; filtered index for repeated blanks) · TWIN_SUCCESSION §6b (add-unique remedies cross into zone 2: which duplicate wins, case and trailing twins)
note:     the shape summary's distinct-as-collated vs distinct-binary is what reveals the case pair. A de-dupe that compared with
          a binary collation would leave it, and the filtered build would block 1505.
```

### OP-add-unique-09 · filtered on IS NOT NULL: repeated NULLs pass
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows · Email NULL×2 · distinct×3
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email) WHERE Email IS NOT NULL
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email),Email IS NOT NULL)={}
          questions  Unique(Customer,(Email),Email IS NOT NULL)
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, filtered (has_filter=1) · effects {adds UIX_Customer_Email (filtered)}
                     notChecked {QA, UAT, Prod}
laws:     F1 F3
source:   receipt · F10 (DB db_uqf: the filtered index builds, is_unique = 1, has_filter = 1) · sample-prs/add-unique.md
note:     the probe applies the index's filter, so the nulls class is out of scope here and nothing beyond presence is planted.
```

### OP-add-unique-10 · twins that are distinct under the column's own collation are not duplicates
```case
kind:     corpus
tags:     edge
env:      pg · Customer.Email COLLATE Latin1_General_CS_AS (in head too) · Customer 5 rows · Email case twins 'Ann@x.test' / 'ann@x.test'
change:   head adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Collation(Customer.Email)=Latin1_General_CS_AS · Unique(Customer,(Email))={}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · effects {adds UIX_Customer_Email} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9
source:   expected · ARCH §4 Unique row ("any duplicate, under T's collation") · INVARIANTS F9 (column collation)
note:     a probe that grouped under the server default (CI) would answer {case}; the planted twin would not collide on the CS
          copy and acceptance would stop. The class is always measured under the column's collation.
```

## drop-unique

### OP-drop-unique-01 · empty table: one DROP INDEX
```case
kind:     corpus
tags:     empty
env:      pg + UIX_Product_Code UNIQUE INDEX on Product (Code) · Product 0 rows
change:   head removes UIX_Product_Code
expect:   answers    HasRows(Product)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes UIX_Product_Code} · notChecked {QA, UAT, Prod}
laws:     F3 F4
source:   expected · skills/op/drop-unique
```

### OP-drop-unique-02 · populated: the drop validates nothing; duplicates become writable
```case
kind:     corpus
tags:     clean
env:      pg (Status carries UIX_Status_Code) · Status 3 rows (referenceData)
change:   head removes CREATE UNIQUE INDEX UIX_Status_Code from Status
expect:   answers    HasRows(Status)=yes
          questions  none
          witnesses  presence(Status) (the referenceData rows)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes UIX_Status_Code}
                     notChecked {QA, UAT, Prod, workload: reads that used the index, and any seed or upsert keyed on Code
                     (Msg 8672 once duplicates exist), change over time: duplicates written after the drop block a
                     re-add (Msg 1505)}
laws:     F3 F4 H4 T2
source:   receipt · SamplePr "drop-unique: the drop is clean; a duplicate written in the gap refuses the re-add" · sample-prs/drop-unique.md (PG_inv_x1)
note:     violating and remedy cannot occur: a drop validates nothing.
```

### OP-drop-unique-03 · the re-add, later: a duplicate written in the gap blocks it
```case
kind:     corpus
tags:     edge
env:      pg (UIX_Customer_Email dropped by an earlier release) · Customer 6 rows · Email equal pair×1 written in the gap
change:   head re-adds CREATE UNIQUE INDEX UIX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))={exact}
          questions  Unique(Customer,(Email))
          witnesses  presence(Customer) · dup(Customer,(Email),exact)
          outcome    verdict blocked unique · Msg 1505 (+1750) · UIX_Customer_Email · exit 3
          record     tier T2 · residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 T2
source:   receipt · SamplePr "drop-unique: the drop is clean; a duplicate written in the gap refuses the re-add"
```

### OP-drop-unique-04 · with DropIndexesNotInSource off, removing the file drops nothing
```case
kind:     corpus
tags:     edge
env:      pg (Status carries UIX_Status_Code) · Status 3 rows (referenceData) · options {DropIndexesNotInSource=False}
change:   head removes CREATE UNIQUE INDEX UIX_Status_Code from Status
expect:   answers    HasRows(Status)=yes
          questions  none
          witnesses  presence(Status) (the referenceData rows)
          outcome    verdict clean · exit 0
          record     tier T0 · ships nothing: the delta is empty and UIX_Status_Code stays · effects {}
                     notChecked {QA, UAT, Prod}
laws:     F3 F4 F6
source:   expected · SamplePr "drop-unique: the drop is clean; a duplicate written in the gap refuses the re-add" (the drop happens under the granular DropIndexesNotInSource default) · INVARIANTS F6
note:     the record states what the delta does, not what the edit meant: a phantom drop must not be reported as removed.
```

## define-pk

### OP-define-pk-01 · empty table: the key builds over nothing
```case
kind:     corpus
tags:     empty
env:      pg, OrderLine without PK_OrderLine_Id (a heap) · OrderLine 0 rows
change:   head adds CONSTRAINT PK_OrderLine PRIMARY KEY CLUSTERED (OrderId, LineNumber) to OrderLine
expect:   answers    HasRows(OrderLine)=no · Unique(OrderLine,(OrderId,LineNumber))={}
          questions  Unique(OrderLine,(OrderId,LineNumber))
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · effects {adds PK_OrderLine} · notChecked {QA, UAT, Prod, IS refresh}
laws:     F1 F3 F4
source:   expected · skills/op/define-pk
```

### OP-define-pk-02 · populated composite key, unique and non-NULL: the clustered build lands
```case
kind:     corpus
tags:     clean
env:      pg, OrderLine without PK_OrderLine_Id (a heap) · OrderLine 8 rows · (OrderId, LineNumber) distinct
change:   head adds CONSTRAINT PK_OrderLine PRIMARY KEY CLUSTERED (OrderId, LineNumber) to OrderLine
expect:   answers    HasRows(OrderLine)=yes · Unique(OrderLine,(OrderId,LineNumber))={}
          questions  Unique(OrderLine,(OrderId,LineNumber))
          witnesses  presence(OrderLine)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place (the clustered build reorders every row) · effects {adds PK_OrderLine}
                     notChecked {QA, UAT, Prod, volume, IS refresh (the entity's Identifier)}
laws:     F1 F3 T2
source:   receipt · FINDINGS_AND_CHANGES Part 6.3 (define-pk: a clean composite builds) · SamplePr "define-pk: adding a primary key over unique non-NULL keys builds the clustered index clean"
```

### OP-define-pk-03 · violating:exact: a duplicate pair fails the build with 1505 and 1750
```case
kind:     corpus
tags:     violating:exact
env:      pg, OrderLine without PK_OrderLine_Id (a heap) · OrderLine 8 rows · (OrderId, LineNumber) (1,1)×2
change:   head adds CONSTRAINT PK_OrderLine PRIMARY KEY CLUSTERED (OrderId, LineNumber) to OrderLine
expect:   answers    HasRows(OrderLine)=yes · Unique(OrderLine,(OrderId,LineNumber))={exact}
          questions  Unique(OrderLine,(OrderId,LineNumber))
          witnesses  presence(OrderLine) · dup(OrderLine,(OrderId,LineNumber),exact)
          outcome    verdict blocked unique · Msg 1505 (+1750) · PK_OrderLine · exit 3
          record     tier T2 · ships only after a pre-deploy de-dupe (OP-define-pk-08) · residue {none}
                     notChecked {QA, UAT, Prod}
laws:     F1 F3 H6 M2
source:   receipt · FINDINGS_AND_CHANGES Part 6.3 (define-pk: dup → Msg 1505 + 1750)
note:     the outcome is one verdict with both numbers: 1750 ("could not create constraint") follows 1505 and is not a second block.
```

### OP-define-pk-04 · violating:case: a natural string key with case twins
```case
kind:     corpus
tags:     violating:case
env:      pg + Region (Code nvarchar(10) NOT NULL, Name nvarchar(50) NULL), a heap, in head too · Region 4 rows · Code 'NE' / 'ne'
change:   head adds CONSTRAINT PK_Region PRIMARY KEY CLUSTERED (Code) to Region
expect:   answers    HasRows(Region)=yes · Unique(Region,(Code))={case}
          questions  Unique(Region,(Code))
          witnesses  presence(Region) · dup(Region,(Code),case)
          outcome    verdict blocked unique · Msg 1505 (+1750) · PK_Region · exit 3
          record     tier T2 · residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9 P5
source:   expected · ARCH §4 Unique row (PRIMARY KEY) · skills/op/define-pk
```

### OP-define-pk-05 · violating:accent: accent twins under an accent-insensitive column
```case
kind:     corpus
tags:     violating:accent
env:      pg + Region (Code nvarchar(10) COLLATE Latin1_General_CI_AI NOT NULL, Name nvarchar(50) NULL), a heap, in head too
          · Region 4 rows · Code 'Né' / 'Ne'
change:   head adds CONSTRAINT PK_Region PRIMARY KEY CLUSTERED (Code) to Region
expect:   answers    HasRows(Region)=yes · Collation(Region.Code)=Latin1_General_CI_AI · Unique(Region,(Code))={accent}
          questions  Unique(Region,(Code))
          witnesses  presence(Region) · dup(Region,(Code),accent)
          outcome    verdict blocked unique · Msg 1505 (+1750) · PK_Region · exit 3
          record     tier T2 · residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9 P5
source:   expected · ARCH §4 Unique row · INVARIANTS F9 (column collation)
```

### OP-define-pk-06 · violating:trailing: 'NE' and 'NE ' are one key
```case
kind:     corpus
tags:     violating:trailing
env:      pg + Region (Code nvarchar(10) NOT NULL, Name nvarchar(50) NULL), a heap, in head too · Region 4 rows · Code 'NE' / 'NE '
change:   head adds CONSTRAINT PK_Region PRIMARY KEY CLUSTERED (Code) to Region
expect:   answers    HasRows(Region)=yes · Unique(Region,(Code))={trailing}
          questions  Unique(Region,(Code))
          witnesses  presence(Region) · dup(Region,(Code),trailing)
          outcome    verdict blocked unique · Msg 1505 (+1750) · PK_Region · exit 3
          record     tier T2 · residue {none} · notChecked {QA, UAT, Prod}
laws:     F1 F3 P5
source:   expected · ARCH §4 Unique row (trailing class)
```

### OP-define-pk-07 · violating:nulls: a key column that is nullable in the environment
```case
kind:     corpus
tags:     violating:nulls
env:      pg + Region (Code nvarchar(10) NULL, Name nvarchar(50) NULL), a heap · Region 4 rows · Code NULL×1
change:   head declares Region.Code NOT NULL and adds CONSTRAINT PK_Region PRIMARY KEY CLUSTERED (Code)
expect:   answers    HasRows(Region)=yes · NotNull(Region.Code)=violating · Unique(Region,(Code))={}
          questions  HasRows(Region) · NotNull(Region.Code) · Unique(Region,(Code))
          witnesses  presence(Region) · null(Region.Code)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Region · exit 3
          record     tier T2 · ships as the tightening class (two releases) before the key · residue {none}
                     notChecked {QA, UAT, Prod}
laws:     F3 F4 H5
source:   verify · skills/op/define-pk (nullable key → Msg 8111 + 1750, proven by a direct ALTER, not through a DacFx delta) · F7 (a populated NOT NULL alter is guarded): whether DacFx emits a guarded ALTER COLUMN ahead of ADD PRIMARY KEY, or rebuilds Region through tmp_ms_xx, is what the first run decides
note:     a head that keeps Code NULL with the key is expected to fail the build (stop build-failed · exit 7); the case pins the
          shape a developer actually reaches, the column made NOT NULL with the key.
```

### OP-define-pk-08 · remedy: a pre-deploy renumbers the duplicate lines, then the key lands
```case
kind:     corpus
tags:     remedy
env:      pg, OrderLine without PK_OrderLine_Id (a heap) · OrderLine 8 rows · (OrderId, LineNumber) (1,1)×2
change:   head adds CONSTRAINT PK_OrderLine PRIMARY KEY CLUSTERED (OrderId, LineNumber) to OrderLine
          pre-deploy: renumber LineNumber with ROW_NUMBER() OVER (PARTITION BY OrderId ORDER BY LineNumber, Id) where a pair repeats
          run 2 adds witness.sql: one more duplicate pair (the shape the script handles)
expect:   answers    HasRows(OrderLine)=yes · Unique(OrderLine,(OrderId,LineNumber))={exact}
          questions  Authored(pre-deploy) · Unique(OrderLine,(OrderId,LineNumber))
          witnesses  run 2 presence(OrderLine) · dup(OrderLine,(OrderId,LineNumber),exact) · declared(a duplicate pair)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.OrderLine.LineNumber; declare a witness
                     for every shape the script handles" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T3 · ships one release (pre-deploy de-dupe, then the key) · effects {adds PK_OrderLine}
                     notChecked {QA, UAT, Prod, change over time, IS refresh}
laws:     F2 F3 H2 A5
source:   expected · skills/op/define-pk ("one release with a pre-deployment script that dedupes or assigns keys")
```

### OP-define-pk-09 · a new table with its key inline: additive, no question
```case
kind:     corpus
tags:     edge
env:      pg
change:   head adds a new table Region (Code nvarchar(10) NOT NULL, Name nvarchar(50) NULL,
          CONSTRAINT PK_Region PRIMARY KEY CLUSTERED (Code))
expect:   answers    none
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {adds Region, PK_Region} · notChecked {QA, UAT, Prod, IS refresh}
laws:     F3 F4
source:   expected · skills/op/define-pk ("new table: the key is in the CREATE, additive")
note:     an inline key in CREATE TABLE is not an ADD CONSTRAINT and reads no row; the Unique row must not match it (F4).
```

## drop-pk

### OP-drop-pk-01 · empty, unreferenced: one DROP CONSTRAINT
```case
kind:     corpus
tags:     empty
env:      pg · Product 0 rows
change:   head removes PK_Product_Id from Product
expect:   answers    HasRows(Product)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes PK_Product_Id; Product becomes a heap}
                     notChecked {QA, UAT, Prod, IS refresh}
laws:     F3 F4
source:   expected · skills/op/drop-pk
```

### OP-drop-pk-02 · populated, unreferenced: publishes clean and leaves a heap
```case
kind:     corpus
tags:     clean
env:      pg · Category 3 rows (referenceData) · nothing references Category
change:   head removes PK_Category_Id from Category
expect:   answers    HasRows(Category)=yes
          questions  none
          witnesses  presence(Category) (the referenceData rows)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes PK_Category_Id; Category becomes a heap}
                     notChecked {QA, UAT, Prod, change over time: duplicate Ids are writable until a key returns,
                     and a re-add re-validates them, workload: lookups scan the heap, IS refresh (the Identifier)}
laws:     F3 F4 H4 T2
source:   receipt · F18 (unreferenced: one DROP CONSTRAINT, a heap after, rows intact) · SamplePr "drop-pk: referenced refuses at the model build; unreferenced drops clean and leaves a heap"
note:     violating and remedy cannot occur: a drop validates nothing.
```

### OP-drop-pk-03 · referenced by a foreign key in the project: refused at build
```case
kind:     corpus
tags:     refuse
env:      pg + FK_OrderLine_Order_OrderId (OrderLine.OrderId → Order.Id, trusted; in the project too) · Order 4 rows · OrderLine 8 rows
change:   head removes PK_Order_Id from Order · FK_OrderLine_Order_OrderId stays
expect:   answers    none (the build refuses before a delta exists)
          questions  none
          witnesses  none
          outcome    verdict refused-at-build · SQL71516 · dbo.Order · exit 3
          record     ships nothing until each referencing key is removed as its own change (drop-fk)
                     notChecked {QA, UAT, Prod: not read, the refusal is the same in every environment}
laws:     F3 H1 T2
source:   receipt · F18 (SQL71516 naming the referencing file; no dacpac, no delta, no publish) · SamplePr "drop-pk: referenced refuses at the model build; unreferenced drops clean and leaves a heap"
```

### OP-drop-pk-04 · a drifted environment holds a key the project does not know references it
```case
kind:     corpus
tags:     edge
env:      pg + FK_Product_Category (Product.CategoryId → Category.Id), environment-only · Category 3 rows · Product 5 rows
change:   head removes PK_Category_Id from Category
expect:   answers    HasRows(Category)=yes · HasRows(Product)=yes
          questions  none
          witnesses  presence(Category) · presence(Product)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {removes FK_Product_Category (environment-only); removes PK_Category_Id; Category becomes a heap}
                     notChecked {QA, UAT, Prod}
laws:     F3 F6 F7 H5
source:   verify · sample-prs/drop-pk.md says a drifted reference "fails this publish there"; red-team #14 says environment-only constraints are dropped silently: with DropConstraintsNotInSource on, the provisional expectation above holds; with it off, DROP CONSTRAINT meets Msg 3725, which no rule classifies (stop unclassified · exit 9). The first run under the captured options decides
note:     the project builds (it has no FK onto Category), so only the environment's own delta can show either outcome (F7).
```

### OP-drop-pk-05 · scale: dropping a clustered key on 1M rows or more
```case
kind:     pure
tags:     edge
input:    delta    ALTER TABLE [dbo].[Customer] DROP CONSTRAINT [PK_Customer_Id];   (clustered)
          answers  HasRows(Customer)=yes · Rows(Customer)=1500000
action:   Get-FxTier, then Get-FxRecord
expect:   record     tier T0 · flags {scale} · notChecked {volume: dropping the clustered index rewrites the table as a heap
                     and rebuilds its nonclustered indexes}
laws:     H4
source:   expected · skills/op/drop-pk ("dropping the clustered index on a large table can run long")
note:     ARCH §7's scale list (builds an index, rebuilds a table, alters a column, validates a constraint) does not name a
          clustered-key drop; this case adds it.
```

## create-fk-clean

### OP-create-fk-clean-01 · empty child table: the key validates nothing and lands trusted
```case
kind:     corpus
tags:     empty
env:      pg · Status 3 rows (referenceData) · Order 0 rows
change:   head adds CONSTRAINT FK_Order_Status FOREIGN KEY (StatusId) REFERENCES Status (Id) to Order
expect:   answers    HasRows(Order)=no · Orphan(Order.(StatusId)→Status)=clean
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted · effects {adds FK_Order_Status}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 F3 F4
source:   expected · skills/op/create-fk-clean · F9 (every declarative FK add is WITH NOCHECK ADD + WITH CHECK CHECK)
```

### OP-create-fk-clean-02 · populated, every child points at a lookup row the referenceData lane seeds
```case
kind:     corpus
tags:     clean
env:      pg · Status 3 rows (referenceData: 1 Pending, 2 Shipped, 3 Cancelled) · Order 4 rows · StatusId ∈ {1, 2, 3}
change:   head adds CONSTRAINT FK_Order_Status FOREIGN KEY (StatusId) REFERENCES Status (Id) to Order
expect:   answers    HasRows(Order)=yes · Orphan(Order.(StatusId)→Status)=clean
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  presence(Order) (+ancestors Status: a referenceData row, reused)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted (is_not_trusted=0), no manual trust step
                     effects {adds FK_Order_Status} · notChecked {QA, UAT, Prod, volume, engine version (trust)}
laws:     F1 F3 M2 T2
source:   receipt · F8 · F9 (DB db_fkc2: WITH NOCHECK ADD then WITH CHECK CHECK → LAND, is_not_trusted = 0) · F19 (re-confirmed on 170.5.76) · SamplePr "create-fk-clean: an FK over clean child data applies in place and lands trusted"
note:     the key is not in the copy yet, so the plan, not the schema, must point the presence row at a seeded Status; an
          arbitrary literal would plant an orphan and acceptance would stop. Violating data is create-fk-orphan.
```

### OP-create-fk-clean-03 · MATCH SIMPLE: a NULL in a nullable FK column is not an orphan
```case
kind:     corpus
tags:     clean
env:      pg · Account 3 rows · Customer 5 rows · AccountId NULL×2 · 3 pointing at Accounts
change:   head adds CONSTRAINT FK_Customer_Account FOREIGN KEY (AccountId) REFERENCES Account (Id) to Customer
expect:   answers    HasRows(Customer)=yes · Orphan(Customer.(AccountId)→Account)=clean
          questions  Orphan(Customer.(AccountId)→Account)
          witnesses  presence(Customer) (AccountId NULL: a nullable FK column is planted NULL)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted · effects {adds FK_Customer_Account}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 F3
source:   expected · ARCH §4 Orphan row (MATCH SIMPLE) · ARCH §5.2 (nullable FK columns get NULL)
note:     a probe counting the NULL rows as orphans would answer violating and plant an orphan; the oracle's clean publish
          would disagree (F3).
```

### OP-create-fk-clean-04 · composite key: a partly-NULL child passes even when its non-NULL part has no parent
```case
kind:     corpus
tags:     clean
env:      pg + UQ_OrderLine_Order_Line UNIQUE (OrderId, LineNumber) on OrderLine + Shipment (Id int IDENTITY PK,
          OrderId int NULL, LineNumber int NULL) · OrderLine 3 rows (1,1) (1,2) (2,1)
          · Shipment 3 rows (1,1) (9,NULL) (NULL,NULL)
change:   head adds CONSTRAINT FK_Shipment_OrderLine FOREIGN KEY (OrderId, LineNumber)
          REFERENCES OrderLine (OrderId, LineNumber) to Shipment
expect:   answers    HasRows(Shipment)=yes · Orphan(Shipment.(OrderId,LineNumber)→OrderLine)=clean
          questions  Orphan(Shipment.(OrderId,LineNumber)→OrderLine)
          witnesses  presence(Shipment) (both FK columns NULL)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted · effects {adds FK_Shipment_OrderLine}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 F3 F4
source:   expected · ARCH §4 Orphan row ("a NULL in any FK column passes")
note:     (9,NULL) has no parent OrderId 9, yet passes; the probe joins to the parent only when every FK column is non-NULL.
```

### OP-create-fk-clean-05 · scale: the validation scan on 1M rows or more raises the flag
```case
kind:     pure
tags:     edge
input:    delta    ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId])
                   REFERENCES [dbo].[Status] ([Id]);
                   ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];
          answers  HasRows(Order)=yes · Rows(Order)=1200000 · Orphan(Order.(StatusId)→Status)=clean
action:   Get-FxTier, then Get-FxRecord
expect:   record     tier T2 · flags {scale} · notChecked {volume: the validation scan's duration and locking}
laws:     H4
source:   expected · ARCH §7 (scale: "validates a constraint on it") · F20 (FK and CHECK re-validation scans were still invisible at 1.05M rows: the flag is a scrutiny line, not a predicted cost)
```

## create-fk-orphan

### OP-create-fk-orphan-01 · violating:orphan: 547 at WITH CHECK CHECK, and the key is left untrusted
```case
kind:     corpus
tags:     violating:orphan
env:      pg · Customer 3 rows · Order 4 rows · CustomerId 999×1 (no Customer 999)
change:   head adds CONSTRAINT FK_Order_Customer_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customer (Id) to Order
expect:   answers    HasRows(Order)=yes · Orphan(Order.(CustomerId)→Customer)=violating
          questions  Orphan(Order.(CustomerId)→Customer)
          witnesses  presence(Order) (+ancestors Customer) · orphan(Order.(CustomerId)→Customer)
          outcome    verdict blocked constraint · Msg 547 · FK_Order_Customer_CustomerId · exit 3
          record     tier T2 · ships only with a pre-deploy reconcile (OP-create-fk-orphan-04)
                     residue {validation: FK_Order_Customer_CustomerId present, untrusted (is_not_trusted=1)}
                     notChecked {QA, UAT, Prod, engine version (trust)}
laws:     F1 F3 H6 M2 T2
source:   receipt · F9 (DB db_orphD: BLOCK Msg 547; the failed publish leaves the key present-but-untrusted) · SamplePr "create-fk-orphan: an orphan blocks the FK at WITH CHECK CHECK and leaves it untrusted; reconcile then re-validate to trusted"
note:     an empty child table cannot hold an orphan; the empty case is OP-create-fk-clean-01.
```

### OP-create-fk-orphan-02 · violating:orphan in a nullable column: only NULL passes
```case
kind:     corpus
tags:     violating:orphan
env:      pg · Account 3 rows · Customer 5 rows · AccountId NULL×3 · valid×1 · 77×1 (no Account 77)
change:   head adds CONSTRAINT FK_Customer_Account FOREIGN KEY (AccountId) REFERENCES Account (Id) to Customer
expect:   answers    HasRows(Customer)=yes · Orphan(Customer.(AccountId)→Account)=violating
          questions  Orphan(Customer.(AccountId)→Account)
          witnesses  presence(Customer) (AccountId NULL) · orphan(Customer.(AccountId)→Account) (a non-NULL dangling AccountId)
          outcome    verdict blocked constraint · Msg 547 · FK_Customer_Account · exit 3
          record     tier T2 · residue {validation: FK_Customer_Account present, untrusted} · notChecked {QA, UAT, Prod}
laws:     F1 F3 H6
source:   expected · ARCH §4 Orphan row (MATCH SIMPLE) · F9 (the re-validation blocks Msg 547 over an orphan)
```

### OP-create-fk-orphan-03 · violating:orphan, composite: each part matches some parent, the pair matches none
```case
kind:     corpus
tags:     violating:orphan
env:      pg + UQ_OrderLine_Order_Line UNIQUE (OrderId, LineNumber) on OrderLine + Shipment (Id int IDENTITY PK,
          OrderId int NULL, LineNumber int NULL) · OrderLine 3 rows (1,1) (1,2) (2,1)
          · Shipment 3 rows (1,1) (2,2) (9,NULL)
change:   head adds CONSTRAINT FK_Shipment_OrderLine FOREIGN KEY (OrderId, LineNumber)
          REFERENCES OrderLine (OrderId, LineNumber) to Shipment
expect:   answers    HasRows(Shipment)=yes · Orphan(Shipment.(OrderId,LineNumber)→OrderLine)=violating
          questions  Orphan(Shipment.(OrderId,LineNumber)→OrderLine)
          witnesses  presence(Shipment) · orphan(Shipment.(OrderId,LineNumber)→OrderLine) (both columns non-NULL, no parent pair)
          outcome    verdict blocked constraint · Msg 547 · FK_Shipment_OrderLine · exit 3
          record     tier T2 · residue {validation: FK_Shipment_OrderLine present, untrusted} · notChecked {QA, UAT, Prod}
laws:     F1 F3 H6
source:   expected · ARCH §4 Orphan row · F9 (the re-validation blocks Msg 547 over an orphan)
note:     (2,2) is the orphan; (9,NULL) is not one (MATCH SIMPLE), so the witness must be a fully non-NULL pair with no parent.
```

### OP-create-fk-orphan-04 · remedy: a pre-deploy removes the orphans, then the key trusts itself
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 3 rows · Order 4 rows · CustomerId 999×1 (no Customer 999)
change:   head adds CONSTRAINT FK_Order_Customer_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customer (Id) to Order
          pre-deploy: DELETE the OrderLine rows of Orders with no Customer, then DELETE Order WHERE NOT EXISTS
          (a Customer with that Id); idempotent
          run 2 adds witness.sql: one Order row, CustomerId 998 (a dangling id, the shape the script handles)
expect:   answers    HasRows(Order)=yes · Orphan(Order.(CustomerId)→Customer)=violating
          questions  Authored(pre-deploy) · Orphan(Order.(CustomerId)→Customer)
          witnesses  run 2 presence(Order) (+ancestors Customer) · orphan(Order.(CustomerId)→Customer)
                     · declared(Order CustomerId 998)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.Order.CustomerId; declare a witness for
                     every shape the script handles" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T3 · ships one release (pre-deploy reconcile), trusted, no manual trust step
                     effects {adds FK_Order_Customer_CustomerId}
                     notChecked {QA, UAT, Prod, change over time: orphans written before promotion, engine version (trust)}
laws:     F2 F3 H2 A5
source:   receipt · F9 (DB db_orphB: reconciled in a pre-deploy → LAND, is_not_trusted = 0, no manual trust step; db_orphC: a manual WITH CHECK CHECK is redundant)
note:     the reconcile is written against "no parent", not against the value 999, so it also removes the planted and declared
          orphans; a reconcile naming one literal key would leave the synthetic orphan and block where Dev would not.
```

### OP-create-fk-orphan-05 · remedy meets a shape it does not handle: orphan Orders that have lines
```case
kind:     corpus
tags:     remedy
env:      pg + FK_OrderLine_Order (OrderLine.OrderId → Order.Id, trusted) · Customer 3 rows · Order 4 rows
          · CustomerId 999×1 · OrderLine 2 rows on that Order
change:   head adds CONSTRAINT FK_Order_Customer_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customer (Id) to Order
          pre-deploy: DELETE Order WHERE NOT EXISTS (a Customer with that Id), with no OrderLine step
          run 2 adds witness.sql: one Order row with CustomerId 998 and two OrderLine rows on it
expect:   answers    HasRows(Order)=yes · Orphan(Order.(CustomerId)→Customer)=violating
          questions  Authored(pre-deploy) · Orphan(Order.(CustomerId)→Customer)
          witnesses  run 2 presence(Order) (+ancestors Customer) · orphan(Order.(CustomerId)→Customer)
                     · declared(Order CustomerId 998 with 2 OrderLines)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.Order.CustomerId; declare a witness for
                     every shape the script handles" · exit 5
                     run 2 verdict blocked constraint · Msg 547 · FK_OrderLine_Order · exit 3
          record     run 2 tier T3 · residue {pre-deploy: the DELETE failed as one statement; nothing applied}
                     notChecked {QA, UAT, Prod}
laws:     F3 H5 H6 A5
source:   expected · F6 (pre-deploy work is not rolled back when the deploy fails) · TWIN_SUCCESSION §6b (a remedy must handle every shape; the script checks itself before writing)
note:     OrderLine is not touched by the delta, so no answer asks about its rows; only the declaration puts lines on the copy.
          The 547 names the existing FK_OrderLine_Order and is attributed to the pre-deploy (H5), not to the key being added.
```

### OP-create-fk-orphan-06 · the block is not atomic: the rest of the release has already committed
```case
kind:     corpus
tags:     edge
env:      pg · Customer 3 rows · Order 4 rows · CustomerId 999×1 · options {IncludeTransactionalScripts=True}
change:   head adds FK_Order_Customer_CustomerId (CustomerId → Customer.Id) and Order.Note nvarchar(200) NULL in one release
expect:   answers    HasRows(Order)=yes · Orphan(Order.(CustomerId)→Customer)=violating
          questions  Orphan(Order.(CustomerId)→Customer)
          witnesses  presence(Order) (+ancestors Customer) · orphan(Order.(CustomerId)→Customer)
          outcome    verdict blocked constraint · Msg 547 · FK_Order_Customer_CustomerId · exit 3
          record     tier T2 · residue {main: Order.Note added, committed · validation: FK_Order_Customer_CustomerId
                     present, untrusted (is_not_trusted=1)} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F6 H6
source:   receipt · red-team #4 (failed deploys are not atomic ✓) · TWIN_SUCCESSION §2 ("even with transactional scripts on, a Msg 547 at WITH CHECK CHECK lands after the commit, leaving everything else applied and the constraint untrusted")
note:     the WITH CHECK CHECK batch runs after the transacted portion commits; a guard block, by contrast, rolls the whole
          release back (F13). The residue is read by scripting head against the copy after the block (ARCH §5.8).
```

### OP-create-fk-orphan-07 · the lookup is empty in the environment but seeded on the copy
```case
kind:     corpus
tags:     edge
env:      pg · Status 0 rows (the environment never ran the lane) · Order 3 rows · StatusId 1×2 · 2×1
change:   head adds CONSTRAINT FK_Order_Status FOREIGN KEY (StatusId) REFERENCES Status (Id) to Order
expect:   answers    HasRows(Order)=yes · Orphan(Order.(StatusId)→Status)=violating
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  presence(Order) (+ancestors Status: a referenceData row) · orphan(Order.(StatusId)→Status)
                     (a StatusId absent from the referenceData rows)
          outcome    verdict blocked constraint · Msg 547 · FK_Order_Status · exit 3
          record     tier T2 · residue {validation: FK_Order_Status present, untrusted} · notChecked {QA, UAT, Prod}
laws:     F1 F3 M2
source:   expected · ARCH §5 steps 1–3 (the referenceData lanes run before planting; violations are planted per answered question)
note:     on the copy the lane has seeded 1–3, so Dev's own orphan values would not be orphans there; the witness is chosen
          against the copy's parent set (and P1 forbids carrying Dev's values anyway).
```

### OP-create-fk-orphan-08 · ScriptNewConstraintValidation off: no validation, no block, the key lands untrusted
```case
kind:     corpus
tags:     edge
env:      pg · Customer 3 rows · Order 4 rows · CustomerId 999×1 · options {ScriptNewConstraintValidation=False}
change:   head adds CONSTRAINT FK_Order_Customer_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customer (Id) to Order
expect:   answers    HasRows(Order)=yes
          questions  none (the delta holds WITH NOCHECK ADD and no WITH CHECK CHECK)
          witnesses  presence(Order)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place; lands untrusted (is_not_trusted=1) and the orphan stays
                     effects {adds FK_Order_Customer_CustomerId (untrusted)} · notChecked {QA, UAT, Prod, engine version}
laws:     F3 F4 F6 H4
source:   expected · V3_MILESTONES.md §1 fact 11 (measured 2026-09-23 on DacFx 162.5.57 and 170.5.96: is_not_trusted = 0 with default options, 1 with ScriptNewConstraintValidation=false)
note:     the profile, not the engine, decides trust. F6 refuses only a profile that disables BlockOnPossibleDataLoss, so this
          one proves, and Get-FxRecord must read "untrusted" from the delta's shape (an ADD with no CHECK CHECK).
```

## drop-fk

### OP-drop-fk-01 · empty child table: one DROP CONSTRAINT
```case
kind:     corpus
tags:     empty
env:      pg + FK_Order_Status (Order.StatusId → Status.Id, trusted) · Order 0 rows
change:   head removes FK_Order_Status from Order
expect:   answers    HasRows(Order)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes FK_Order_Status} · notChecked {QA, UAT, Prod}
laws:     F3 F4
source:   expected · skills/op/drop-fk
```

### OP-drop-fk-02 · populated: clean, no row touched, an orphan becomes writable
```case
kind:     corpus
tags:     clean
env:      pg + FK_Order_Status (trusted) · Status 3 rows (referenceData) · Order 4 rows
change:   head removes FK_Order_Status from Order
expect:   answers    HasRows(Order)=yes
          questions  none
          witnesses  presence(Order) (+ancestors Status)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes FK_Order_Status}
                     notChecked {QA, UAT, Prod, workload: plans that relied on the trusted key, change over time: orphans
                     written after the drop block a re-add (Msg 547)}
laws:     F3 F4 H4 T2
source:   receipt · F9 (DB db_dropfk: one DROP CONSTRAINT → LAND, no data touched) · SamplePr "drop-fk: removing a foreign key publishes clean and loses no rows, but an orphan can now be written"
note:     violating and remedy cannot occur: a drop validates nothing.
```

### OP-drop-fk-03 · the re-add, later: an orphan written in the gap blocks it
```case
kind:     corpus
tags:     edge
env:      pg (FK_Order_Status dropped by an earlier release) · Status 3 rows (referenceData) · Order 5 rows · StatusId 7×1 written in the gap
change:   head re-adds CONSTRAINT FK_Order_Status FOREIGN KEY (StatusId) REFERENCES Status (Id) to Order
expect:   answers    HasRows(Order)=yes · Orphan(Order.(StatusId)→Status)=violating
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  presence(Order) (+ancestors Status) · orphan(Order.(StatusId)→Status)
          outcome    verdict blocked constraint · Msg 547 · FK_Order_Status · exit 3
          record     tier T2 · residue {validation: FK_Order_Status present, untrusted} · notChecked {QA, UAT, Prod}
laws:     F1 F3 H6 T2
source:   receipt · F9 (a re-add is WITH NOCHECK ADD + WITH CHECK CHECK and blocks Msg 547 over an orphan, DB db_orphD) · SamplePr "drop-fk: removing a foreign key publishes clean and loses no rows, but an orphan can now be written"
```

### OP-drop-fk-04 · an environment-only key is dropped by an unrelated release
```case
kind:     corpus
tags:     edge
env:      pg + FK_Customer_Account (Customer.AccountId → Account.Id), environment-only · Account 3 rows · Customer 5 rows
change:   head adds Customer.MiddleName nvarchar(50) NULL (add-optional)
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place
                     effects {adds Customer.MiddleName · removes FK_Customer_Account (environment-only)}
                     notChecked {QA, UAT, Prod, workload: plans that relied on the trusted key}
laws:     F3 F7 H4
source:   receipt · red-team #14 (environment-only objects dropped silently ✓)
```

### OP-drop-fk-05 · with DropConstraintsNotInSource off, removing the key drops nothing
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Status (trusted) · Order 4 rows · options {DropConstraintsNotInSource=False}
change:   head removes FK_Order_Status from Order
expect:   answers    HasRows(Order)=yes
          questions  none
          witnesses  presence(Order) (+ancestors Status)
          outcome    verdict clean · exit 0
          record     tier T0 · ships nothing: the delta is empty and FK_Order_Status stays · effects {}
                     notChecked {QA, UAT, Prod}
laws:     F3 F4 F6
source:   expected · SamplePr "drop-fk: removing a foreign key publishes clean and loses no rows, but an orphan can now be written" (the drop happens under the granular DropConstraintsNotInSource default) · INVARIANTS F6
```

## toggle-trust

### OP-toggle-trust-01 · remedy: an authored WITH CHECK CHECK re-trusts a legacy untrusted key over clean data
```case
kind:     corpus
tags:     remedy
env:      pg + FK_Order_Customer_CustomerId added WITH NOCHECK (enabled, is_not_trusted=1; a plain FK in the project)
          · Customer 3 rows · Order 4 rows · no orphan
change:   post-deploy: ALTER TABLE [Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer_CustomerId · head otherwise unchanged
          run 2 adds witness.sql: a declaration that the look found no CustomerId without a parent (no row)
expect:   answers    HasRows(Order)=yes · Trust(Order,FK_Order_Customer_CustomerId)=untrusted
          questions  Authored(post-deploy)
          witnesses  run 2 presence(Order) (+ancestors Customer) · trust(Order, FK_Order_Customer_CustomerId, untrusted)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.Order.CustomerId; declare a witness for
                     every shape the script handles" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T3 · ships a scripted step, not a schema change; the key ends trusted (is_not_trusted 1 → 0)
                     effects {} · notChecked {QA, UAT, Prod, engine version}
laws:     F2 F3 F9 H2 A5 T2
source:   receipt · SamplePr "toggle-trust: WITH CHECK CHECK makes an untrusted FK trusted over clean data; a violating row keeps it untrusted"
note:     the operation has no declarative form, so its route is this authored path (the refuse-and-route of the skill). The
          trust witness reproduces Dev's untrusted state on the copy (F9) and must leave the identity plan empty (F2).
```

### OP-toggle-trust-02 · violating:orphan: re-trust over a legacy orphan fails 547 and the key stays untrusted
```case
kind:     corpus
tags:     violating:orphan
env:      pg + FK_Order_Customer_CustomerId added WITH NOCHECK over a legacy orphan (enabled, is_not_trusted=1)
          · Customer 3 rows · Order 4 rows · CustomerId 999×1
change:   post-deploy: ALTER TABLE [Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer_CustomerId · head otherwise unchanged
          run 2 adds witness.sql: one Order row, CustomerId 998 (the dangling shape)
expect:   answers    HasRows(Order)=yes · Trust(Order,FK_Order_Customer_CustomerId)=untrusted
          questions  Authored(post-deploy)
          witnesses  run 2 presence(Order) (+ancestors Customer) · trust(Order, FK_Order_Customer_CustomerId, untrusted)
                     · declared(Order CustomerId 998)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.Order.CustomerId; declare a witness for
                     every shape the script handles" · exit 5
                     run 2 verdict blocked constraint · Msg 547 · FK_Order_Customer_CustomerId · exit 3
          record     run 2 tier T3 · ships only with the orphan reconciled first, in the same script
                     residue {post-deploy: FK_Order_Customer_CustomerId still untrusted (is_not_trusted=1)}
                     notChecked {QA, UAT, Prod}
laws:     F2 F9 H5 H6 A5 T2
source:   receipt · SamplePr "toggle-trust: WITH CHECK CHECK makes an untrusted FK trusted over clean data; a violating row keeps it untrusted" · SamplePr "create-fk-orphan: an orphan blocks the FK at WITH CHECK CHECK and leaves it untrusted; reconcile then re-validate to trusted"
note:     the key is enabled on the copy, so the declared orphan can be inserted only with enforcement suspended: the planter
          applies it inside the trust witness and re-enables WITH NOCHECK, leaving the key as Dev holds it. The 547 is raised
          by the authored post-deploy and recorded against it (H5).
```

### OP-toggle-trust-03 · does the environment's delta re-trust an untrusted key on its own?
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Customer_CustomerId added WITH NOCHECK (enabled, is_not_trusted=1; a plain FK in the project)
          · Customer 3 rows · Order 4 rows · no orphan
change:   head adds Customer.MiddleName nvarchar(50) NULL (add-optional) · nothing touches Order
expect:   answers    HasRows(Customer)=yes · HasRows(Order)=yes · Trust(Order,FK_Order_Customer_CustomerId)=untrusted
          questions  Orphan(Order.(CustomerId)→Customer) if the delta re-validates the key; none otherwise
          witnesses  presence(Customer) · presence(Order) (+ancestors Customer) · trust(Order, FK_Order_Customer_CustomerId, untrusted)
          outcome    verdict clean · exit 0
          record     effects {adds Customer.MiddleName} plus {re-trusts FK_Order_Customer_CustomerId} if the delta re-validates
                     notChecked {QA, UAT, Prod}
laws:     F2 F3 F5 F9
source:   verify · ARCH §11.1 (does Extract preserve an enabled-but-untrusted constraint?) and DacFx's IgnoreWithNocheckOnForeignKeys (default False): whether the offline delta re-validates the key is what the first run decides
note:     if Extract drops the flag, the copy builds the key trusted and only the trust witness keeps F9, and the offline delta
          may then differ from a live Script (F5); either way the copy must hold the key untrusted before the publish.
```

### OP-toggle-trust-04 · a disabled key in the environment: an unrelated release re-enables it and fails 547
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Customer_CustomerId disabled (NOCHECK CONSTRAINT; a plain FK in the project)
          · Customer 3 rows · Order 4 rows · CustomerId 999×1 (written while the key was off)
change:   head adds Customer.MiddleName nvarchar(50) NULL (add-optional) · nothing touches Order
expect:   answers    HasRows(Customer)=yes · HasRows(Order)=yes · Trust(Order,FK_Order_Customer_CustomerId)=disabled
                     Orphan(Order.(CustomerId)→Customer)=violating
          questions  Orphan(Order.(CustomerId)→Customer)
          witnesses  presence(Customer) · presence(Order) (+ancestors Customer) · orphan(Order.(CustomerId)→Customer)
                     · trust(Order, FK_Order_Customer_CustomerId, disabled)
          outcome    verdict blocked constraint · Msg 547 · FK_Order_Customer_CustomerId · exit 3
          record     tier T2 · residue {main: Customer.MiddleName added; the key dropped and re-added WITH NOCHECK
                     · validation: FK_Order_Customer_CustomerId present, untrusted} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F7 F9 H6
source:   receipt · red-team #1 (a disabled FK changes the delta the environment receives ✓) · F9 (the re-add is WITH NOCHECK ADD + WITH CHECK CHECK and blocks Msg 547 over an orphan)
note:     the re-enable reads DROP · WITH NOCHECK ADD · WITH CHECK CHECK (the TWIN_SUCCESSION §5 signal), so an add-optional
          proven against main reads T0 while this environment blocks. The offline form rests on ARCH §11.1 (OP-toggle-trust-03).
```

### OP-toggle-trust-05 · a disabled key over clean data: an unrelated release silently turns it back on
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Customer_CustomerId disabled (NOCHECK CONSTRAINT; a plain FK in the project)
          · Customer 3 rows · Order 4 rows · no orphan
change:   head adds Customer.MiddleName nvarchar(50) NULL (add-optional) · nothing touches Order
expect:   answers    HasRows(Order)=yes · Trust(Order,FK_Order_Customer_CustomerId)=disabled · Orphan(Order.(CustomerId)→Customer)=clean
          questions  Orphan(Order.(CustomerId)→Customer)
          witnesses  presence(Customer) · presence(Order) (+ancestors Customer) · trust(Order, FK_Order_Customer_CustomerId, disabled)
          outcome    verdict clean · exit 0
          record     tier T2 · effects {adds Customer.MiddleName · re-enables FK_Order_Customer_CustomerId (was disabled), trusted}
                     notChecked {QA, UAT, Prod}
laws:     F3 F7 F9 H4
source:   verify · red-team #1 (the delta differs ✓): the exact re-enable statements on DacFx 162.x and 170.x (ARCH §11.3) and their offline form (ARCH §11.1) are what the first run decides
note:     if the re-enable is a bare WITH CHECK CHECK with no WITH NOCHECK ADD before it, the Orphan row ("an FK added WITH
          NOCHECK") does not match and the run must stop unknown-statement, never read it as clean (F4).
```

### OP-toggle-trust-06 · a disabled CHECK with a FALSE row: the re-enable needs the agent's witness
```case
kind:     corpus
tags:     edge
env:      pg + CK_Order_Total CHECK (Total > 0) disabled (NOCHECK; in the project) · Order 4 rows · Total -5.00×1
change:   head adds Customer.MiddleName nvarchar(50) NULL (add-optional)
          run 2 adds witness.sql: one Order row, Total -5.00 (applied with the check suspended)
expect:   answers    HasRows(Order)=yes · Trust(Order,CK_Order_Total)=disabled · CheckFalse(Order,CK_Order_Total)=violating
          questions  CheckFalse(Order,CK_Order_Total)
          witnesses  run 2 presence(Order) · trust(Order, CK_Order_Total, disabled) · declared(Order Total -5.00)
          outcome    run 1 stop check-witness · "write one row into witness.sql that makes CK_Order_Total FALSE, then
                     re-run" · exit 5
                     run 2 verdict blocked constraint · Msg 547 · CK_Order_Total · exit 3
          record     run 2 tier T2 · residue {main: Customer.MiddleName added · validation: CK_Order_Total present, untrusted}
                     notChecked {QA, UAT, Prod}
laws:     F3 F7 F9 A5 H6
source:   verify · red-team #1 records the FK face; whether a disabled CHECK is re-enabled the same way, and survives Extract (ARCH §11.1), is what the first run decides
```

### OP-toggle-trust-07 · a post-deploy that disables the key on every publish ("so the load can run")
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Customer_CustomerId (trusted) · Customer 3 rows · Order 4 rows
change:   post-deploy: ALTER TABLE [Order] NOCHECK CONSTRAINT FK_Order_Customer_CustomerId · head otherwise unchanged
          run 2 adds witness.sql: a declaration that the script reads no values (no row)
expect:   answers    HasRows(Order)=yes · Trust(Order,FK_Order_Customer_CustomerId)=trusted
          questions  Authored(post-deploy)
          witnesses  run 2 presence(Order) (+ancestors Customer)
          outcome    run 1 stop authored-script · "the change runs authored SQL over dbo.Order (FK_Order_Customer_CustomerId);
                     declare a witness for every shape the script handles" · exit 5
                     run 2 verdict clean · exit 0, and the silence check finds a second plan that re-enables the key
          record     run 2 tier T3 · ships not as a standing script: route to a runbook that disables, loads, and re-enables
                     WITH CHECK CHECK · effects {disables FK_Order_Customer_CustomerId}
                     notChecked {QA, UAT, Prod, change over time: rows loaded while it is off are re-validated by the
                     next publish's re-enable}
laws:     F3 H2 A5
source:   verify · ARCH §5 step 9 names the silence check but ARCH §6 gives no outcome for a non-empty second plan; whether the re-enable appears at all depends on the disabled flag reaching Script (ARCH §11.1)
```

## change-delete-rule

### OP-change-delete-rule-01 · empty child table: DROP and re-add, nothing to validate
```case
kind:     corpus
tags:     empty
env:      pg + FK_Order_Status (ON DELETE NO ACTION, trusted) · Status 3 rows (referenceData) · Order 0 rows
change:   head sets FK_Order_Status ON DELETE CASCADE
expect:   answers    HasRows(Order)=no · Orphan(Order.(StatusId)→Status)=clean
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted · effects {modifies FK_Order_Status: NO ACTION → CASCADE}
                     notChecked {QA, UAT, Prod, workload: cascade reach}
laws:     F1 F3 F4
source:   expected · skills/op/change-delete-rule
```

### OP-change-delete-rule-02 · populated, NO ACTION → CASCADE: a clean metadata DROP and re-add; the risk is behavioural
```case
kind:     corpus
tags:     clean
env:      pg + FK_Order_Status (ON DELETE NO ACTION, trusted) · Status 3 rows (referenceData) · Order 4 rows · OrderLine 8 rows on those Orders
change:   head sets FK_Order_Status ON DELETE CASCADE
expect:   answers    HasRows(Order)=yes · Orphan(Order.(StatusId)→Status)=clean
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  presence(Order) (+ancestors Status: a referenceData row, reused)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted
                     effects {modifies FK_Order_Status: NO ACTION → CASCADE (DROP · WITH NOCHECK ADD · WITH CHECK CHECK)}
                     notChecked {QA, UAT, Prod, workload: cascade reach, a Status delete removes its Orders and stops,
                     leaving their OrderLines orphaned, volume, engine version (trust)}
laws:     F1 F3 H4 T2
source:   receipt · F9 (DB db_cdr2: DROP · WITH NOCHECK ADD … ON DELETE CASCADE · WITH CHECK CHECK → LAND, CASCADE, is_not_trusted = 0; DB db_cascbehav: one-level reach) · SamplePr "change-delete-rule: an FK ON DELETE NO ACTION -> CASCADE is a clean metadata DROP+ADD"
note:     no publish exercises a delete, so the fan-out is a named residual, never a verdict.
```

### OP-change-delete-rule-03 · a legacy untrusted key with an orphan: the re-add fails 547 after the new rule committed
```case
kind:     corpus
tags:     violating:orphan
env:      pg + FK_Order_Status added WITH NOCHECK over a legacy orphan (ON DELETE NO ACTION, enabled, untrusted)
          · Status 3 rows (referenceData) · Order 4 rows · StatusId 9×1 (no Status 9)
change:   head sets FK_Order_Status ON DELETE CASCADE
expect:   answers    HasRows(Order)=yes · Trust(Order,FK_Order_Status)=untrusted · Orphan(Order.(StatusId)→Status)=violating
          questions  Orphan(Order.(StatusId)→Status)
          witnesses  presence(Order) (+ancestors Status) · trust(Order, FK_Order_Status, untrusted)
                     · orphan(Order.(StatusId)→Status) (planted inside the trust witness)
          outcome    verdict blocked constraint · Msg 547 · FK_Order_Status · exit 3
          record     tier T2 · residue {main: FK_Order_Status dropped and re-added ON DELETE CASCADE, committed
                     · validation: FK_Order_Status present, untrusted} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9 H6
source:   verify · ARCH §11.1: the verdict follows F9's re-add law; whether the copy reproduces the legacy untrusted key with its orphan (through Extract or the trust witness) is what the first run decides
note:     the rule change lands although the publish fails: the residue is a cascading, untrusted key. No remedy of this
          operation's own applies; the reconcile is create-fk-orphan's, in the same release.
```

### OP-change-delete-rule-04 · Ignore expressed as SET NULL on a nullable reference
```case
kind:     corpus
tags:     edge
env:      pg + FK_Customer_Account (ON DELETE NO ACTION, trusted) · Account 3 rows · Customer 5 rows · AccountId NULL×2
change:   head sets FK_Customer_Account ON DELETE SET NULL
expect:   answers    HasRows(Customer)=yes · Orphan(Customer.(AccountId)→Account)=clean
          questions  Orphan(Customer.(AccountId)→Account)
          witnesses  presence(Customer) (AccountId NULL)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place, trusted · effects {modifies FK_Customer_Account: NO ACTION → SET NULL}
                     notChecked {QA, UAT, Prod, workload: deleting an Account now NULLs its Customers' AccountId}
laws:     F1 F3 H4
source:   expected · skills/op/change-delete-rule (Ignore → ON DELETE SET NULL when the FK column is nullable)
```

### OP-change-delete-rule-05 · SET NULL on a NOT NULL reference: never clean, the run decides how it fails
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Status (ON DELETE NO ACTION, trusted) · Status 3 rows (referenceData) · Order 4 rows
change:   head sets FK_Order_Status ON DELETE SET NULL (Order.StatusId is NOT NULL)
expect:   outcome    stop build-failed · "the project does not build: <first error>" · exit 7
                     or, if the build passes, stop unclassified · "the publish ended with Msg 1761 that no rule classifies"
                     · exit 9
laws:     F3 H2 H5
source:   verify · skills/op/change-delete-rule ("ON DELETE SET NULL if the FK column is nullable"): whether SSDT refuses the model or SQL Server refuses the ALTER (Msg 1761) is what the first run decides; either way the outcome is a stop, never clean
```

## add-index

### OP-add-index-01 · empty table: CREATE INDEX over nothing
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head adds CREATE NONCLUSTERED INDEX IX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place (additive) · effects {adds IX_Customer_Email} · notChecked {QA, UAT, Prod}
laws:     F3 F4
source:   expected · skills/op/add-index
```

### OP-add-index-02 · populated: a non-unique index builds clean; its cost is a residual, not a question
```case
kind:     corpus
tags:     clean
env:      pg · Customer 6 rows
change:   head adds CREATE NONCLUSTERED INDEX IX_Customer_Email ON Customer (Email)
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place (additive) · effects {adds IX_Customer_Email}
                     notChecked {QA, UAT, Prod, volume: build time and the write-blocking lock, platform: edition (ONLINE)}
laws:     F3 F4 H4 T2
source:   receipt · SamplePr "add-index: a non-unique index builds clean over a populated table" · F11 (CREATE INDEX adds a real nonclustered index)
note:     a non-unique build reads every row but cannot fail on a value while its key fits the length limit, so it matches no
          vocabulary row and must not stop unknown-statement (F4); the scale flag carries its cost.
```

### OP-add-index-03 · violating:longkey: an existing value over 1,700 bytes fails the build with 1946
```case
kind:     corpus
tags:     violating:longkey
env:      pg + Product.Description nvarchar(1000) NULL (in head too) · Product 3 rows · Description of 900 characters ×1 (1,800 bytes)
change:   head adds CREATE NONCLUSTERED INDEX IX_Product_Description ON Product (Description)
expect:   answers    HasRows(Product)=yes · KeyLength(Product,IX_Product_Description)=violating
          questions  KeyLength(Product,IX_Product_Description)
          witnesses  presence(Product) · longkey(Product, IX_Product_Description)
          outcome    verdict blocked keylength · Msg 1946 · IX_Product_Description · exit 3
          record     tier T2 · ships not as written: a narrower key, or the column as an INCLUDE · residue {none}
                     notChecked {QA, UAT, Prod}
laws:     F1 F3 M2 P5
source:   expected · ARCH §4 KeyLength row · red-team #16 (Msg 1945 warns now, 1946 fails later ✓)
note:     the long witness value is synthetic and w-prefixed (P5). No authored remedy applies; the fix is in head.
```

### OP-add-index-04 · a key that can exceed 1,700 bytes but no row does: 1945 warns, the publish lands
```case
kind:     corpus
tags:     edge
env:      pg + Product.Description nvarchar(1000) NULL (in head too) · Product 3 rows · Description ≤ 100 characters
change:   head adds CREATE NONCLUSTERED INDEX IX_Product_Description ON Product (Description)
expect:   answers    HasRows(Product)=yes · KeyLength(Product,IX_Product_Description)=clean
          questions  KeyLength(Product,IX_Product_Description)
          witnesses  presence(Product)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · effects {adds IX_Product_Description}
                     notChecked {QA, UAT, Prod, change over time: an insert or update whose key exceeds 1,700 bytes
                     fails with Msg 1946}
laws:     F3 H4 H5
source:   receipt · red-team #16 (Msg 1945 warns now, 1946 fails later ✓)
note:     Msg 1945 is a warning in a successful publish: the classifier must know it as one, neither a block nor unclassified (H5).
```

### OP-add-index-05 · scale: an index build on a table of 1M rows or more
```case
kind:     pure
tags:     edge
input:    delta    CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer]([Email]);
          answers  HasRows(Customer)=yes · Rows(Customer)=1200000
action:   Get-FxTier, then Get-FxRecord
expect:   record     tier T0 · flags {scale} · notChecked {volume: build time and the write-blocking lock; schedule a window}
laws:     H4
source:   expected · ARCH §7 (scale: "builds an index") · F20 (the nonclustered index build is the first operation whose engine cost surfaces, ~11 s over floor at 1.05M rows)
```

### OP-add-index-06 · ONLINE = ON: an edition feature LocalDB does not have
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   head adds CREATE NONCLUSTERED INDEX IX_Customer_Email ON Customer (Email) WITH (ONLINE = ON)
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds IX_Customer_Email}
                     notChecked {QA, UAT, Prod, platform: edition, ONLINE = ON needs Enterprise or Developer; on Standard
                     the build blocks writes}
laws:     F3 H4 H5
source:   verify · whether DacFx carries ONLINE = ON into the delta at all, and if it does, whether LocalDB (Express) fails it with Msg 1712 (then the expectation becomes a named stop, not clean), is what the first run decides · TWIN_SUCCESSION §6 (edition features are a platform residual)
```

## modify-index

### OP-modify-index-01 · empty table: DROP + CREATE over nothing
```case
kind:     corpus
tags:     empty
env:      pg + IX_Customer_Email (Email) · Customer 0 rows
change:   head changes IX_Customer_Email to (Email, Region) INCLUDE (Name)
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {modifies IX_Customer_Email (DROP + CREATE)}
                     notChecked {QA, UAT, Prod}
laws:     F3 F4
source:   expected · skills/op/modify-index
```

### OP-modify-index-02 · populated, a key and an INCLUDE added: a clean DROP + CREATE rebuild
```case
kind:     corpus
tags:     clean
env:      pg + IX_Customer_Email (Email) · Customer 6 rows
change:   head changes IX_Customer_Email to (Email, Region) INCLUDE (Name)
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {modifies IX_Customer_Email (DROP + CREATE)}
                     notChecked {QA, UAT, Prod, volume: the rebuild's write-blocking lock}
laws:     F3 F4 T2
source:   receipt · SamplePr "modify-index: an existing index gains a key column and an INCLUDE via a clean DROP+CREATE"
```

### OP-modify-index-03 · non-unique → unique over distinct values: builds
```case
kind:     corpus
tags:     clean
env:      pg + IX_Product_Code (Code) · Product 5 rows · Code distinct
change:   head makes IX_Product_Code UNIQUE
expect:   answers    HasRows(Product)=yes · Unique(Product,(Code))={}
          questions  Unique(Product,(Code))
          witnesses  presence(Product)
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place (DROP INDEX + CREATE UNIQUE INDEX)
                     effects {modifies IX_Product_Code: unique} · notChecked {QA, UAT, Prod, volume}
laws:     F1 F3
source:   expected · sample-prs/modify-index.md · F10 (the duplicate law is add-unique's)
```

### OP-modify-index-04 · violating:exact without transactional scripts: the old index is already gone
```case
kind:     corpus
tags:     violating:exact
env:      pg + IX_Product_Code (Code) · Product 5 rows · Code equal pair×1 · options {IncludeTransactionalScripts=False}
change:   head makes IX_Product_Code UNIQUE
expect:   answers    HasRows(Product)=yes · Unique(Product,(Code))={exact}
          questions  Unique(Product,(Code))
          witnesses  presence(Product) · dup(Product,(Code),exact)
          outcome    verdict blocked unique · Msg 1505 (+1750) · IX_Product_Code · exit 3
          record     tier T2 · residue {main: the non-unique IX_Product_Code dropped, committed; the unique index not built}
                     notChecked {QA, UAT, Prod}
laws:     F1 F3 F6 H6
source:   receipt · red-team #4 (with IncludeTransactionalScripts at its default false, a blocked unique-index build left the index dropped ✓) · TWIN_SUCCESSION §2
note:     the environment is left with no index on Code at all, worse than before the change; the residue says so.
```

### OP-modify-index-05 · violating:case: a case twin blocks the unique rebuild
```case
kind:     corpus
tags:     violating:case
env:      pg + IX_Product_Code (Code) · Product 5 rows · Code 'Ab1' / 'ab1'
change:   head makes IX_Product_Code UNIQUE
expect:   answers    HasRows(Product)=yes · Unique(Product,(Code))={case}
          questions  Unique(Product,(Code))
          witnesses  presence(Product) · dup(Product,(Code),case)
          outcome    verdict blocked unique · Msg 1505 (+1750) · IX_Product_Code · exit 3
          record     tier T2 · residue {none: the transacted DROP + CREATE rolled back} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9 P5
source:   expected · ARCH §4 Unique row · skills/op/modify-index
```

### OP-modify-index-06 · violating:accent under an accent-insensitive column
```case
kind:     corpus
tags:     violating:accent
env:      pg + IX_Product_Code (Code) · Product.Code COLLATE Latin1_General_CI_AI (in head too) · Product 5 rows · Code 'Café' / 'Cafe'
change:   head makes IX_Product_Code UNIQUE
expect:   answers    HasRows(Product)=yes · Collation(Product.Code)=Latin1_General_CI_AI · Unique(Product,(Code))={accent}
          questions  Unique(Product,(Code))
          witnesses  presence(Product) · dup(Product,(Code),accent)
          outcome    verdict blocked unique · Msg 1505 (+1750) · IX_Product_Code · exit 3
          record     tier T2 · residue {none: the transacted DROP + CREATE rolled back} · notChecked {QA, UAT, Prod}
laws:     F1 F3 F9 P5
source:   expected · ARCH §4 Unique row · INVARIANTS F9 (column collation)
```

### OP-modify-index-07 · violating:trailing: 'AB1' and 'AB1 ' collide
```case
kind:     corpus
tags:     violating:trailing
env:      pg + IX_Product_Code (Code) · Product 5 rows · Code 'AB1' / 'AB1 '
change:   head makes IX_Product_Code UNIQUE
expect:   answers    HasRows(Product)=yes · Unique(Product,(Code))={trailing}
          questions  Unique(Product,(Code))
          witnesses  presence(Product) · dup(Product,(Code),trailing)
          outcome    verdict blocked unique · Msg 1505 (+1750) · IX_Product_Code · exit 3
          record     tier T2 · residue {none: the transacted DROP + CREATE rolled back} · notChecked {QA, UAT, Prod}
laws:     F1 F3 P5
source:   expected · ARCH §4 Unique row (trailing class)
```

### OP-modify-index-08 · violating:nulls: a unique rebuild on a nullable column with two NULLs
```case
kind:     corpus
tags:     violating:nulls
env:      pg + IX_Customer_Region (Region) · Customer 6 rows · Region NULL×2 · distinct×4
change:   head makes IX_Customer_Region UNIQUE
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Region))={nulls}
          questions  Unique(Customer,(Region))
          witnesses  presence(Customer) · dup(Customer,(Region),nulls)
          outcome    verdict blocked unique · Msg 1505 (+1750) · IX_Customer_Region · exit 3
          record     tier T2 · residue {none: the transacted DROP + CREATE rolled back} · notChecked {QA, UAT, Prod}
laws:     F1 F3
source:   expected · F10 (a unique index permits exactly one NULL; a second → Msg 1505)
note:     the filtered form (WHERE Region IS NOT NULL) passes, as OP-add-unique-09.
```

### OP-modify-index-09 · remedy: a pre-deploy de-dupe, then the unique rebuild
```case
kind:     corpus
tags:     remedy
env:      pg + IX_Product_Code (Code) · Product 5 rows · Code equal pair×1
change:   head makes IX_Product_Code UNIQUE
          pre-deploy: for each Code held by more than one Product, suffix the later rows' Code with their Id
          run 2 adds witness.sql: one more equal pair (the shape the script handles)
expect:   answers    HasRows(Product)=yes · Unique(Product,(Code))={exact}
          questions  Authored(pre-deploy) · Unique(Product,(Code))
          witnesses  run 2 presence(Product) · dup(Product,(Code),exact) · declared(an equal pair)
          outcome    run 1 stop authored-script · "run facsimile shape Dev dbo.Product.Code; declare a witness for every
                     shape the script handles" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T3 · ships one release (pre-deploy de-dupe, then DROP INDEX + CREATE UNIQUE INDEX)
                     effects {modifies IX_Product_Code: unique} · notChecked {QA, UAT, Prod, change over time}
laws:     F2 F3 H2 A5
source:   expected · skills/op/modify-index ("a pre-deployment de-dupe plus the declarative change, in one PR")
```

### OP-modify-index-10 · violating:exact with transactional scripts: the release rolls back whole
```case
kind:     corpus
tags:     edge
env:      pg + IX_Product_Code (Code) · Product 5 rows · Code equal pair×1 · options {IncludeTransactionalScripts=True}
change:   head makes IX_Product_Code UNIQUE
expect:   answers    HasRows(Product)=yes · Unique(Product,(Code))={exact}
          questions  Unique(Product,(Code))
          witnesses  presence(Product) · dup(Product,(Code),exact)
          outcome    verdict blocked unique · Msg 1505 (+1750) · IX_Product_Code · exit 3
          record     tier T2 · residue {none: the transacted DROP + CREATE rolled back; the non-unique index stands}
                     notChecked {QA, UAT, Prod}
laws:     F3 F6 H6
source:   expected · F13 (under IncludeTransactionalScripts = true a blocked release rolled back whole) · INVARIANTS H6
note:     the same fixture as OP-modify-index-04 with one option flipped; the residue, not the verdict, differs (F6).
```

## drop-index

### OP-drop-index-01 · empty table: one DROP INDEX
```case
kind:     corpus
tags:     empty
env:      pg + IX_Customer_Region (Region) · Customer 0 rows
change:   head removes IX_Customer_Region
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes IX_Customer_Region} · notChecked {QA, UAT, Prod}
laws:     F3 F4
source:   expected · skills/op/drop-index
```

### OP-drop-index-02 · populated: clean and lossless; "unused" is a workload claim the proof cannot check
```case
kind:     corpus
tags:     clean
env:      pg + IX_Customer_Region (Region) · Customer 6 rows
change:   head removes IX_Customer_Region
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes IX_Customer_Region}
                     notChecked {QA, UAT, Prod, workload: index usage (sys.dm_db_index_usage_stats) is not read, and a
                     dropped FK-column index brings the join scan back (F11)}
laws:     F3 F4 H4 T2
source:   receipt · SamplePr "drop-index: removing an index publishes clean and loses no rows; the scripted DROP INDEX is lossless"
note:     violating and remedy cannot occur: a drop validates nothing.
```

### OP-drop-index-03 · an environment-only index is dropped by an unrelated release
```case
kind:     corpus
tags:     edge
env:      pg + IX_Customer_Email_Legacy (Email), environment-only · Customer 6 rows
change:   head adds Customer.MiddleName nvarchar(50) NULL (add-optional)
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place
                     effects {adds Customer.MiddleName · removes IX_Customer_Email_Legacy (environment-only)}
                     notChecked {QA, UAT, Prod, workload: queries that used the dropped index}
laws:     F3 F7 H4
source:   receipt · red-team #14 (environment-only objects dropped silently ✓)
```

### OP-drop-index-04 · with DropIndexesNotInSource off, removing the index drops nothing
```case
kind:     corpus
tags:     edge
env:      pg + IX_Customer_Region (Region) · Customer 6 rows · options {DropIndexesNotInSource=False}
change:   head removes IX_Customer_Region
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships nothing: the delta is empty and IX_Customer_Region stays · effects {}
                     notChecked {QA, UAT, Prod}
laws:     F3 F4 F6
source:   expected · SamplePr "drop-index: removing an index publishes clean and loses no rows; the scripted DROP INDEX is lossless" (the drop happens under the granular DropIndexesNotInSource default) · INVARIANTS F6
```

## rebuild-index

### OP-rebuild-index-01 · "rebuild the fragmented index": no declarative delta, routed to maintenance
```case
kind:     corpus
tags:     refuse
env:      pg + IX_Customer_Email (Email) · Customer 6 rows
change:   none to head (the request is maintenance: ALTER INDEX IX_Customer_Email REBUILD)
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships nothing: no declarative delta; route to a maintenance job keyed to measured fragmentation
                     effects {} · notChecked {QA, UAT, Prod, workload: fragmentation (sys.dm_db_index_physical_stats) is not read}
laws:     F3 F4 H4 T2
source:   receipt · SamplePr "rebuild-index: ALTER INDEX REBUILD is operational maintenance with no declarative delta (NothingToApply)"
note:     the tier comes from the delta, never from an operation's name (ARCH §7): an empty delta is an empty, clean proof, and
          the refusal lives in the record's "ships nothing". Empty, violating and remedy do not apply.
```

### OP-rebuild-index-02 · a REBUILD wired into the post-deploy: proven clean, re-run on every publish
```case
kind:     corpus
tags:     edge
env:      pg + IX_Customer_Email (Email) · Customer 6 rows
change:   post-deploy: ALTER INDEX IX_Customer_Email ON [Customer] REBUILD · head otherwise unchanged
          run 2 adds witness.sql: a declaration that the script reads no values (no row)
expect:   answers    HasRows(Customer)=yes
          questions  Authored(post-deploy)
          witnesses  run 2 presence(Customer)
          outcome    run 1 stop authored-script · "the change runs authored SQL over dbo.Customer (IX_Customer_Email);
                     declare a witness for every shape the script handles" · exit 5
                     run 2 verdict clean · exit 0
          record     run 2 tier T3 · ships a blocking rebuild that re-runs on every publish; route it to the maintenance job
                     effects {} · notChecked {QA, UAT, Prod, workload: the repeated blocking rebuild, volume}
laws:     F3 H2 A5 H4
source:   expected · skills/op/rebuild-index ("a post-deploy ALTER INDEX … REBUILD re-runs on every redeploy")
note:     the silence check passes (an empty second plan, unchanged hashes), so nothing in the proof sees the repetition; only
          the authored-script stop puts it in front of a person.
```

### OP-rebuild-index-03 · a fill-factor edit meant to force a rebuild yields no delta
```case
kind:     corpus
tags:     edge
env:      pg + IX_Customer_Email (Email) · Customer 6 rows
change:   head adds WITH (FILLFACTOR = 80) to IX_Customer_Email
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · ships nothing: the delta is empty under IgnoreFillFactor · effects {} · notChecked {QA, UAT, Prod}
laws:     F3 F4 F6
source:   verify · DacFx's IgnoreFillFactor (default True) is expected to hide the edit; with it False, DacFx is expected to re-create the index, which is modify-index; the first run under the captured options decides
```
