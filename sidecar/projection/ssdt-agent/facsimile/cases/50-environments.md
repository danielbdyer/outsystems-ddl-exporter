# 50 · Environments (`ENV-nn`)

*Each environment is read on its own, under the login its reader actually holds, and proven on its own schema and answers.
These cases cover access, reachability, missing objects, drift, per-environment proof, two-release state, collation, CDC, staleness and Prod.
Logins: `fx_full` (db_datareader + VIEW DEFINITION), `fx_vd` (VIEW DEFINITION only), `fx_dr` (db_datareader only). `trace.xel` is an Extended Events capture on the stand-in, filtered to the tool's login.*

## Access

### ENV-01 · full reader: read and prove a populated tightening
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NULL×2 · login fx_full
change:   head Customer.Email NULL→NOT NULL · scripts unchanged
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          files      delta.Dev.sql · answers.Dev.json stamped login fx_full, time, delta hash, probe text
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · flags {unread:QA, unread:UAT, unread:Prod}
laws:     F1 F3 H7 P4
source:   receipt · FINDINGS F7 (a populated make-mandatory blocks on the guard); TWIN_SUCCESSION §6b zone 1
          (VIEW DEFINITION for the delta, SELECT for violation counts)
```

### ENV-02 · VIEW DEFINITION only, presence-only delta: complete without data access
```case
kind:     corpus
env:      Dev = pg · Product 5 rows · login fx_vd
change:   head drops Product.LegacyCode (one release) · scripts unchanged
expect:   questions  HasRows(Product)
          answers    HasRows(Product)=yes, from sys.partitions
          files      trace.xel: no statement reads dbo.Product's rows; one sys.partitions read in the count batch
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     tier T1
laws:     F1 F3 P1 P4
source:   receipt · TWIN_SUCCESSION §6b (2026-10-15: a VIEW DEFINITION-only login extracted the full schema, generated
          the real delta with its guard, and read row counts from sys.partitions)
```

### ENV-03 · VIEW DEFINITION only, an unguarded validation question: read stops for SELECT
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · Email distinct · login fx_vd
action:   facsimile read Dev   (head adds unique index UX_Customer_Email on Customer.Email; no guard)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))=unmeasured (measured:false, reason SELECT)
          files      delta.Dev.sql · answers.Dev.json holding the unmeasured entry; no unmeasured answer reads as clean
          outcome    stop no-access · "fx_vd lacks SELECT on Estate (Dev). Ask a lead to run facsimile read Dev,
                     or request the grant" · exit 4
laws:     H2 H3 A3 P4
source:   expected · TWIN_SUCCESSION §6b zone 1 (VIEW DEFINITION answers populated-or-not; SELECT answers violations)
note:     ARCH and README §2 have no unmeasured answer state; this case and ENV-05/ENV-63 assume one.
```

### ENV-04 · VIEW DEFINITION only, empty table: violation questions are implied
```case
kind:     sql
env:      Dev = pg · Customer 0 rows · login fx_vd
action:   facsimile read Dev; facsimile prove Dev   (head adds UX_Customer_Email)
expect:   answers    HasRows(Customer)=no · Unique(Customer,(Email))={} implied by HasRows=no; no SELECT issued
          outcome    verdict clean · exit 0
          record     tier T2
laws:     F1 H2 P4
source:   verify · ARCH §11.2: is sys.partitions.rows exactly 0 for an empty table under a VIEW DEFINITION-only login
          on 2019 and 2022? If not, the implication is unsound and this case becomes stop no-access.
```

### ENV-05 · VIEW DEFINITION only, guarded tightening: the guard decides without SELECT
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NULL×2 · login fx_vd
change:   head Customer.Email NULL→NOT NULL · scripts unchanged
action:   facsimile read Dev; facsimile prove Dev; facsimile prove Dev --permissive
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=unmeasured
          outcome    read: stop no-access · "fx_vd lacks SELECT on Estate (Dev) …" · exit 4
                     prove: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
                     prove --permissive: stop no-access (the consequence world needs the NotNull count) · exit 4
          record     tier T2 · notChecked {Dev counts: NotNull(Customer.Email) unmeasured; the R1 remedy needs it}
laws:     H2 H4 F3 P4
source:   receipt · FINDINGS F7 and proving-ground/Modules/Customer.sql (a populated make-mandatory blocks on the guard
          whether or not NULLs are present)
note:     DacFx emits every guard before the main script's first ALTER, so one populated guarded table decides Strict.
          H2 constrains clean only; F1's acceptance has no rule for an unmeasured question.
```

### ENV-06 · db_datareader only: Extract refuses, read stops before any count
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · login fx_dr
action:   facsimile read Dev   (head Customer.Email NULL→NOT NULL)
expect:   outcome    stop no-access · "fx_dr lacks VIEW DEFINITION on Estate (Dev). Ask a lead to run facsimile read Dev,
                     or request the grant" · exit 4
          files      no delta.Dev.sql, no answers.Dev.json; trace.xel: no count batch (Extract failed first)
          stdout     the stop line only; sqlpackage's own message is not echoed
laws:     H1 H3 A3 M3 P3
source:   receipt · TWIN_SUCCESSION §6b (2026-10-15: sqlpackage refused a data-reader without VIEW DEFINITION:
          "you do not have View Definition permission")
```

### ENV-07 · db_datareader only: the shape verb still works
```case
kind:     sql
env:      Dev = pg · Customer 12 rows · Email NULL×1 · ''×1 · login fx_dr
action:   facsimile shape Dev dbo.Customer.Email
expect:   outcome    summary rows_total 12 · nulls 1 · empty 1 · both distinct counts; signatures with n ≥ 5 only
          files      trace.xel: SELECT only; COL_LENGTH validates without VIEW DEFINITION (SELECT carries visibility)
          exit       0
laws:     P2 P4 P6
source:   receipt · TWIN_SUCCESSION §6b (the shape profile ran as a data-reader and printed no value)
```

### ENV-08 · no login on the server: a developer tries to read QA
```case
kind:     sql
env:      QA = pg · Customer 6 rows · the developer's Windows identity has no server login on QA (Msg 18456)
action:   facsimile read QA   (as the developer)
expect:   outcome    stop no-access · "<developer> cannot log in to qa-sql. Ask a lead to run facsimile read QA,
                     or request the grant" · exit 4
          files      nothing written; the branch's Dev proofs byte-identical
          record     a later prove keeps flags {unread:QA}
laws:     H3 A3 A5 P3
source:   expected · TWIN_SUCCESSION §3 and §11.3 (developers read Dev; dev leads read QA and UAT)
note:     A server login with no database user (Msg 4060) ends the same way. ARCH §6's no-access template names only
          VIEW DEFINITION | SELECT; it needs a "cannot log in" form.
```

### ENV-09 · a column-level DENY on a PII column
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · Email NULL×1 · login fx_full with DENY SELECT ON dbo.Customer (Email)
action:   facsimile read Dev   (head Customer.Email NULL→NOT NULL)
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=unmeasured
          outcome    stop no-access · "fx_full lacks SELECT on dbo.Customer.Email (Dev). Ask a lead to run
                     facsimile read Dev, or request the grant" · exit 4
          files      the error is recorded as Msg 230 by number; its text is not recorded
laws:     H2 H3 P3
source:   expected · Microsoft docs (a column-level DENY overrides table-level SELECT; Msg 230)
note:     Corporate estates often deny PII columns. The run must stop, never count around the column.
```

### ENV-10 · VIEW DEFINITION granted on one schema only
```case
kind:     sql
env:      Dev = pg plus schema sales with sales.Invoice 3 rows · login fx_vdschema: VIEW DEFINITION ON SCHEMA::dbo only
action:   facsimile read Dev   (head narrows sales.Invoice.Ref)
expect:   outcome    stop no-access · "fx_vdschema lacks VIEW DEFINITION on Estate (Dev) …" · exit 4
          files      never a delta.Dev.sql that CREATEs sales.Invoice because Extract could not see it
laws:     S3 H2 F5
source:   verify · does Extract under schema-scoped VIEW DEFINITION refuse, or silently omit the invisible schema? If it
          omits, read must check database-level VIEW DEFINITION (HAS_PERMS_BY_NAME) before trusting Extract.
```

### ENV-11 · a lead extends the author's proof to QA
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email distinct (read earlier by the author, login dev1)
          QA = pg · Customer 9 rows · Email 'a@x.invalid' and 'A@x.invalid' · login lead1 (fx_full shape)
change:   head adds UX_Customer_Email
action:   on the author's branch, as the lead: facsimile read QA; facsimile prove QA
expect:   answers    QA: HasRows(Customer)=yes · Unique(Customer,(Email))={case}
          files      delta.QA.sql, answers.QA.json stamped login lead1; Dev's delta, answers and verdict byte-identical
          outcome    QA: verdict blocked unique · Msg 1505 · dbo.Customer.UX_Customer_Email · exit 3
          record     a QA line beside Dev's clean line; flags drop unread:QA
laws:     F7 F3 H7
source:   expected · TWIN_SUCCESSION §8 (the lead's review extends the proof to QA and UAT)
```

### ENV-12 · the developers' real Dev login
```case
kind:     manual
setup:    a developer's corporate laptop; facsimile.json Dev = the real Dev server and database
action:   as that developer: SELECT HAS_PERMS_BY_NAME(NULL,'DATABASE','VIEW DEFINITION'),
          HAS_PERMS_BY_NAME(NULL,'DATABASE','SELECT'); then facsimile read Dev
expect:   outcome    both 1 and read completes; otherwise the access ticket names the missing grant
laws:     P4 H3
source:   verify · TWIN_SUCCESSION §6b ("check what the developers' Dev login actually holds")
```

## Reachability

### ENV-13 · the server refuses the connection
```case
kind:     process
setup:    facsimile.json Dev.server = a host with no SQL Server listening
action:   facsimile read Dev
expect:   outcome    stop unreachable · "Cannot reach dev-sql. Check the network, or ask someone with access to run
                     facsimile read Dev" · exit 4
          files      nothing under proofs/; no cache entry claims a Dev schema
          stdout     one line; no driver text beyond the server name
laws:     H1 H3 A1 A3
source:   expected · ARCH §6
note:     A firewall that drops packets ends the same way, bounded by the connect timeout times the retry count.
```

### ENV-14 · the server name does not resolve
```case
kind:     process
setup:    facsimile.json Dev.server = dev-sql.corp.invalid (no DNS record)
action:   facsimile read Dev
expect:   outcome    stop unreachable · "Cannot reach dev-sql.corp.invalid …" (the name as configured, so a typo
                     shows) · exit 4
laws:     H3 A3 A5
source:   expected · ARCH §6
```

### ENV-15 · the first connection fails once, then succeeds
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · the first login attempt is refused (server starting), the second succeeds
action:   facsimile read Dev
expect:   files      delta.Dev.sql, answers.Dev.json; trace.xel: one failed login, then exactly two sessions
          exit       0
laws:     M3 H1
source:   expected · TWIN_SUCCESSION Appendix B ("LocalDB's first connection can time out, so retry"), applied to
          the environment connection
note:     M3's two sessions count sessions that run statements; a failed login is not a touch.
```

### ENV-16 · the connection drops between the two touches
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · the network drops after Extract and before the count batch
action:   facsimile read Dev   (head Customer.Email NULL→NOT NULL); an older answers.Dev.json is already committed
expect:   outcome    stop unreachable · "Cannot reach dev-sql …" · exit 4
          files      neither delta.Dev.sql nor answers.Dev.json is rewritten; the older answers.Dev.json is
                     byte-identical
laws:     H1 M3
source:   expected · INVARIANTS H1 (no partial success)
```

### ENV-17 · the count batch on the real estate's largest table
```case
kind:     manual
setup:    the real Dev estate (later QA and UAT); a change touching its largest table (Rows ≥ 1M) with NotNull
          and Unique questions on unindexed columns
action:   facsimile read Dev, timed
expect:   outcome    answers within the command timeout; otherwise stop unreachable naming the timeout, never a
                     partial answers.Dev.json
laws:     M3 H1
source:   verify · how long one batched count takes on the real estate, and what command timeout read needs
          (TWIN_SUCCESSION §6, volume). ARCH §6 has no stop kind for a timeout; unreachable is the nearest.
```

## Missing objects

### ENV-18 · QA has not received the release that creates the column the change alters
```case
kind:     sql
env:      QA = pg · Customer 6 rows · no Customer.EmailNormalized (the merged release that adds it reached Dev only)
action:   facsimile read QA   (the branch narrows Customer.EmailNormalized nvarchar(256)→nvarchar(100))
expect:   outcome    stop missing-object · "QA has no dbo.Customer.EmailNormalized. It has not received the change
                     that creates it. Prove against what QA has, or record QA as Not checked" · exit 5
          record     never a QA verdict clean from a delta that only ADDs the column at its new width
laws:     S3 H3 A5
source:   receipt · red-team #8 (a missing object read as empty; disposition: stop, never zero)
note:     QA's own delta ADDs the column (no question), so S3 needs the objects the branch alters (head vs merge-base).
          ARCH §3 builds head only.
```

### ENV-19 · an object dropped between the two touches
```case
kind:     sql
env:      Dev = pg · Product 5 rows · a hook drops Product.LegacyCode after Extract, before the count batch
action:   facsimile read Dev   (head narrows Product.LegacyCode to nvarchar(10))
expect:   answers    no Fits(Product.LegacyCode→nvarchar(10)) answer; COL_LENGTH returned NULL and nothing counted as 0
          outcome    stop missing-object · "Dev has no dbo.Product.LegacyCode …" · exit 5
laws:     S3 P2 H1
source:   expected · INVARIANTS S3 and P2 (identifiers validated with OBJECT_ID / COL_LENGTH before counting)
```

### ENV-20 · pre-cutover Dev holds only the OutSystems physical tables
```case
kind:     sql
env:      Dev = OSUSR_ABC_CUSTOMER 6 rows; no dbo.Customer
action:   facsimile read Dev   (head Customer.Email NULL→NOT NULL; the branch alters dbo.Customer)
expect:   outcome    stop missing-object · "Dev has no dbo.Customer. It has not received the change that creates it
                     …" · exit 5
          record     never tier T0 · verdict clean from a delta that would CREATE dbo.Customer
laws:     S3 H2 H3
source:   expected · INVARIANTS S3
note:     Same mechanism gap as ENV-18: telling "the change alters it" from "the delta creates it" needs the base build.
```

### ENV-21 · shape on a column the environment lacks
```case
kind:     sql
env:      Dev = pg · Customer 6 rows (no BirthText column)
action:   facsimile shape Dev dbo.Customer.BirthText
expect:   outcome    stop missing-object · "Dev has no dbo.Customer.BirthText …" · exit 5
          stdout     never a profile reading rows_total 0
laws:     S3 P2 P6
source:   expected · INVARIANTS S3 and P2
```

## Drift

### ENV-22 · an environment-only index and CHECK are dropped by the delta
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · plus IX_Customer_Region and CK_Customer_Name (a DBA hotfix, not in the repository)
change:   head adds Customer.Nickname nvarchar(40) NULL · effective options DropObjectsNotInSource=False,
          DropIndexesNotInSource and DropConstraintsNotInSource at their defaults (True)
expect:   questions  none (DROP INDEX and DROP CONSTRAINT read no rows)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds Customer.Nickname; removes IX_Customer_Region, CK_Customer_Name}
laws:     F3 F6 F7 H4
source:   receipt · red-team #14 (environment-only objects dropped silently ✓); TWIN_SUCCESSION §2 (environment-only
          constraints and indexes silently dropped)
note:     DropObjectsNotInSource=False does not protect child objects. The captured effective options decide (F6).
```

### ENV-23 · an environment-only table: a phantom under the production setting, guarded when dropped
```case
kind:     corpus
env:      UAT = pg · plus dbo.OSUSR_LEGACY_ORDER 4 rows (a cutover leftover, not in the repository)
change:   head adds Customer.Nickname NULL · run twice: effective DropObjectsNotInSource=False, then =True
expect:   questions  True only: HasRows(OSUSR_LEGACY_ORDER)
          outcome    False: verdict clean · exit 0 · effects {adds Customer.Nickname}; the leftover is untouched
                     True: verdict blocked guard · Msg 50000/127 · dbo.OSUSR_LEGACY_ORDER · exit 3
          record     True: effects {adds Customer.Nickname; removes dbo.OSUSR_LEGACY_ORDER}
laws:     F6 F3 H5
source:   expected · proving-ground/profiles/ProvingGround.Pipeline.publish.xml (DropObjectsNotInSource=False is the
          production drop setting, pending capture); skills/op/delete-entity
```

### ENV-24 · an environment-only column added by hand in QA
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows
          QA = pg · Customer 6 rows · plus Customer.HotfixFlag bit NULL
change:   head adds Customer.Nickname NULL
expect:   questions  Dev: none · QA: HasRows(Customer)
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     QA effects {adds Customer.Nickname; removes Customer.HotfixFlag}
laws:     F7 F3 H6
source:   expected · skills/op/delete-attribute (a column drop on a populated table is guarded); red-team #1 (each
          environment receives its own delta)
```

### ENV-25 · QA lacks an object the base already has
```case
kind:     corpus
env:      QA = pg · Order 4 rows · without IX_Order_CustomerId (base, head and Dev all have it)
change:   head adds Order.Note nvarchar(200) NULL
expect:   outcome    QA: verdict clean · exit 0
          record     QA effects {adds Order.Note, IX_Order_CustomerId}; the index add is marked drift, not the change
laws:     F7 H4
source:   expected · TWIN_SUCCESSION §3.2 (the delta each environment actually receives)
note:     Telling drift from the change needs the base build (head vs merge-base); ARCH §3 builds head only.
```

### ENV-26 · QA's column type drifted, so an unrelated change carries a narrowing there
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows
          QA = pg · Customer 6 rows · Email widened by hand to nvarchar(320) · one Email of 300 characters
change:   head adds Customer.Nickname NULL (Email stays nvarchar(256))
expect:   questions  QA: HasRows(Customer) · Fits(Customer.Email→nvarchar(256))
          answers    QA: HasRows(Customer)=yes · Fits(Customer.Email→nvarchar(256))={overlen}
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     QA effects {adds Customer.Nickname; modifies Customer.Email nvarchar(320)→nvarchar(256)}
laws:     F7 F1 F3
source:   receipt · red-team #1 (proving against main misses each environment's delta, drift included ✓); FINDINGS F1
          (a populated narrowing blocks on the guard)
```

## Per-environment proof

### ENV-27 · different answers give separate verdicts
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email distinct
          QA = pg · Customer 6 rows · Email 'a@x.invalid' and 'A@x.invalid'
change:   head adds UX_Customer_Email
action:   facsimile prove Dev; facsimile prove QA
expect:   answers    Dev: Unique(Customer,(Email))={} · QA: Unique(Customer,(Email))={case}
          witnesses  Dev: presence(Customer) · QA: presence(Customer) · dup(Customer,(Email),case)
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked unique · Msg 1505 · dbo.Customer.UX_Customer_Email · exit 3
          record     two verdict files, one line per environment; nothing averages them into one disposition
laws:     F7 F3 F1
source:   receipt · FINDINGS F10 (a duplicate blocks a unique-index build, Msg 1505); TWIN_SUCCESSION §6b (a case twin
          shows as distinct_as_collated < distinct_binary and would block a unique index)
```

### ENV-28 · identical inputs share one proof
```case
kind:     corpus
env:      Dev and QA = pg · Customer 6 rows · Email distinct (identical schema and answers)
change:   head adds UX_Customer_Email
action:   facsimile prove Dev QA
expect:   outcome    verdict clean · exit 0
          files      verdict.Dev.json and verdict.QA.json cite one proof identity (equal delta hash and answers hash);
                     one copy built, one publish
laws:     F7 D1 M2
source:   expected · INVARIANTS F7
note:     The answers hash must exclude the login and time stamps, or environments read at different times never
          share (ENV-56). A QA delta that differs by one drift statement makes two proofs.
```

### ENV-29 · the same delta and booleans, different counts
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NULL×2
          QA = pg · Customer 40 rows · Email NULL×7
change:   head Customer.Email NULL→NOT NULL
action:   facsimile prove Dev QA
expect:   outcome    Dev and QA: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          files      two proofs (the answers hashes differ) with byte-identical witness plans
laws:     F7 D1
source:   expected · INVARIANTS F7 as written (proofs keyed by the answers hash)
note:     Hashing the booleans F1 compares, instead of the counts, would let these share one proof. Undecided.
```

### ENV-30 · answers are never unioned
```case
kind:     corpus
env:      Dev = pg · Customer 4 rows · Order 3 rows, one CustomerId with no Customer · Product 5 rows · Code distinct
          QA = pg · Customer 4 rows · Order 3 rows, every CustomerId resolves · Product 5 rows · Code 'AB'×2
change:   head adds FK_Order_Customer (Order.CustomerId→Customer.Id) and UX_Product_Code
action:   facsimile prove Dev QA
expect:   witnesses  Dev: presence(Order) +ancestors Customer · orphan(Order.(CustomerId)→Customer) · presence(Product)
                     QA: presence(Order) +ancestors Customer · presence(Product) · dup(Product,(Code),exact)
          copy       no copy holds both an orphan and a duplicate
          outcome    Dev: verdict blocked constraint · Msg 547 · FK_Order_Customer · exit 3
                     QA: verdict blocked unique · Msg 1505 · dbo.Product.UX_Product_Code · exit 3
laws:     F7 M2 F1 F3
source:   receipt · red-team #5 (a union of environments builds a state no environment has; one proof per distinct
          (delta, facts))
```

## Two-release state

### ENV-31 · R1 has landed in QA but not in UAT
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · Email already NOT NULL (R1's pre-deploy ran in QA)
          UAT = pg · Customer 6 rows · Email nullable, NULL×0 (R1 not deployed)
change:   head = R2: Customer.Email NULL→NOT NULL in the model, no pre-deploy
action:   facsimile read QA; facsimile read UAT; facsimile prove QA; facsimile prove UAT
expect:   questions  QA: none (empty delta) · UAT: HasRows(Customer) · NotNull(Customer.Email)
          outcome    QA: verdict clean · exit 0
                     UAT: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     QA tier T0 · UAT tier T2 · two proofs (delta.QA.sql ≠ delta.UAT.sql)
laws:     F7 F3 F8
source:   receipt · FINDINGS F4 and F7 (R2 over a database R1 already tightened generates nothing; over a populated
          nullable column the same model change blocks); red-team #1
```

### ENV-32 · an open lag window holds the table
```case
kind:     process
setup:    estate/in-flight.md row CHG-1 · tables dbo.Customer · phase 1 of 2; the branch does not edit the ledger
action:   facsimile prove Dev   (head adds Customer.Nickname NULL)
expect:   outcome    stop in-flight · "Customer is in an open two-release window (estate/in-flight.md, CHG-1). Hold
                     this change, or make it the window's next release" · exit 5
          files      no verdict.Dev.json written
laws:     H3 A3 A5
source:   receipt · FINDINGS F17 (a publish during the lag window reverted the in-flight change on a green publish);
          scripts/inflight-check.mjs (the hold, ported per ARCH §2 row 27)
```

### ENV-33 · the change is the window's next release
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NOT NULL physically (R1 landed) · in-flight row CHG-1 holds dbo.Customer
change:   head = R2: Customer.Email NULL→NOT NULL · the branch advances CHG-1 to phase 2 of 2 in estate/in-flight.md
expect:   outcome    verdict clean · exit 0 (no in-flight stop)
          record     tier T0 · flags {in-flight, unread:QA, unread:UAT, unread:Prod}
laws:     H3 A5 F8
source:   expected · scripts/inflight-check.mjs (a PR that also edits the ledger is the phase-advance PR: reported,
          not refused)
```

### ENV-34 · drift alone puts a held table into QA's delta
```case
kind:     sql
env:      Dev = pg · QA = pg plus Customer.HotfixFlag (environment-only) · in-flight row CHG-1 holds dbo.Customer
action:   facsimile read Dev; facsimile read QA; facsimile prove Dev; facsimile prove QA
          (head adds Product.Nickname NULL; the branch touches no Customer file)
expect:   outcome    Dev: verdict clean · exit 0
                     QA: stop in-flight · "Customer is in an open two-release window …" · exit 5
laws:     F7 H3
source:   expected · FINDINGS F17 (any publish that touches a held table reverts it)
note:     inflight-check.mjs reads tables from the changed files; the facsimile must read them from each delta, or
          QA's drift-only DROP COLUMN on the held table passes.
```

### ENV-35 · R1 and R2 both merged before QA deploys
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · Email NULL×2 (neither R1 nor R2 deployed)
change:   main carries R1 (pre-deploy backfill + ALTER NOT NULL, model unchanged) and then R2 (model Email NOT NULL;
          R1's pre-deploy retired); QA receives head = R2
expect:   questions  QA: HasRows(Customer) · NotNull(Customer.Email)
          outcome    QA: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     ships: QA never ran R1's backfill; deploy R1's release to QA first
laws:     F7 F8 H4
source:   receipt · red-team #1 (R1 and R2 both merged before QA deploys); FINDINGS F7 (the one-release tightening
          blocks)
```

### ENV-36 · proving a program where R1 has already landed
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NOT NULL physically (R1 landed; the model still says NULL)
change:   program R1 (model Email NULL; pre-deploy backfill + ALTER NOT NULL) → R2 (model Email NOT NULL)
action:   facsimile prove --program R1 R2   (Dev)
expect:   outcome    verdict clean · exit 0 (R2 over Dev is empty)
          record     ships: R2 alone in Dev; R1's plan over Dev is non-empty (ALTER COLUMN Email NULL), the F17
                     hazard: re-publishing R1 reverts Email
laws:     F8 H4
source:   receipt · FINDINGS F3 and F17 (a model that lags the database reverts it on the next green publish)
```

### ENV-37 · a rename and a tightening both pending in QA
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · Region NULL×1 (the merged rename Region→SalesRegion reached Dev only)
change:   main carries R1 (rename Customer.Region→SalesRegion, refactorlog); the branch makes SalesRegion NOT NULL
expect:   questions  QA: HasRows(Customer) · NotNull(Customer.SalesRegion), counted on QA's Customer.Region through the
                     delta's sp_rename
          answers    QA: NotNull(Customer.SalesRegion)=violating; never stop missing-object, never 0
          outcome    QA: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     F7 S3 F3
source:   receipt · FINDINGS F13 (a release carrying a rename and a populated tightening is refused atomically)
note:     ARCH §4 does not say how a question's coordinates map back through an sp_rename in the same delta.
```

## Collation and platform

### ENV-38 · the copy takes the environment's database collation, not LocalDB's
```case
kind:     corpus
env:      Dev = pg, database collation Latin1_General_CI_AI · Product 5 rows · Code 'cafe' and 'café'
change:   head adds UX_Product_Code
expect:   answers    Collation(Product.Code)=Latin1_General_CI_AI · Unique(Product,(Code))={accent}
          witnesses  presence(Product) · dup(Product,(Code),accent)
          copy       database collation Latin1_General_CI_AI on a LocalDB instance collated SQL_Latin1_General_CP1_CI_AS
          outcome    verdict blocked unique · Msg 1505 · dbo.Product.UX_Product_Code · exit 3
laws:     F9 F1 F3
source:   receipt · red-team #13 (the copy takes the dacpac's collation ✓)
note:     Holds when the publish creates the base database. A database the tool pre-creates takes the instance
          collation unless ScriptDatabaseCollation is set; acceptance then stops.
```

### ENV-39 · a column collation drifted in QA meets a unique index
```case
kind:     corpus
env:      Dev = pg + UX_Product_Code · Product.Code COLLATE Latin1_General_CI_AI · Code distinct
          QA = pg + UX_Product_Code · Product.Code COLLATE Latin1_General_CI_AS (drift) · Code 'cafe' and 'café'
change:   head = Dev's shape plus Product.Name nvarchar(200)→nvarchar(250)
expect:   questions  QA: Unique(Product,(Code)) counted under the target collation Latin1_General_CI_AI
          answers    QA: Unique(Product,(Code))={accent}
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked unique · Msg 1505 · dbo.Product.UX_Product_Code · exit 3
laws:     F7 F9 F3
source:   receipt · red-team #3 (data-reading DDL with no guard: a collation change on a unique column ✓)
note:     The duplicate count uses the collation the column moves to, not the one it has.
```

### ENV-40 · a #temp table in an authored script, server collation unlike LocalDB's
```case
kind:     corpus
env:      UAT = pg on a server collated Latin1_General_CI_AS; database collation SQL_Latin1_General_CP1_CI_AS ·
          Category 3 rows
change:   pre-deploy: CREATE TABLE #map (Code nvarchar(30)) … JOIN dbo.Category c ON c.Code = m.Code (no COLLATE
          DATABASE_DEFAULT) · witness.sql declares the shapes the script handles
expect:   outcome    on an instance collated like UAT's server: verdict blocked collation · Msg 468 · Script.PreDeployment.sql · exit 3
                     on LocalDB (SQL_Latin1_General_CP1_CI_AS): verdict clean · exit 0, only with the record below
          record     tier T3 · notChecked {platform: UAT server collation ≠ LocalDB's; #map at
                     Script.PreDeployment.sql:<n> lacks COLLATE DATABASE_DEFAULT}
laws:     F9 H4 H2
source:   receipt · red-team #13 (#temp joins hit Msg 468 against the server collation ✓)
note:     LocalDB's instance collation is fixed, so the copy cannot raise this 468; the lint line is the only signal.
```

### ENV-41 · the converse: LocalDB raises a 468 the environment would not
```case
kind:     corpus
env:      Dev = pg on a server collated Latin1_General_CI_AS; database collation Latin1_General_CI_AS · Category 3 rows
change:   the ENV-40 pre-deploy · witness.sql declares the shapes
expect:   outcome    on LocalDB: verdict blocked collation · Msg 468 · Script.PreDeployment.sql · exit 3, as ARCH §6 now classifies it
          record     notChecked {platform: LocalDB's tempdb collation ≠ Dev's server collation}
laws:     F9 H5 H4
source:   verify · may a 468 on a copy whose instance collation differs from the environment's server collation be
          attributed to the environment? ARCH has no rule; as written this is a false block (the safe direction).
```

### ENV-42 · compatibility level
```case
kind:     sql
env:      QA = pg at compatibility level 130
action:   facsimile prove QA   (head adds Customer.Nickname NULL); read the copy's compatibility_level
expect:   copy       compatibility_level 130, equal to QA's, under the captured effective options
          outcome    otherwise a stop before publishing head; never a verdict from a copy at LocalDB's default level
laws:     F9 F2 F6
source:   verify · does publishing schema.QA.dacpac under the effective options set the copy's level (it depends on
          ScriptDatabaseOptions), and does the identity step see a difference?
```

### ENV-43 · LocalDB older than the project's DSP, with and without the override
```case
kind:     process
setup:    project DSP Sql160; LocalDB 15.0 (2019) only · run twice: allowOlderLocalDb unset, then set
action:   facsimile prove Dev
expect:   outcome    unset: stop platform · "LocalDB 15 is older than the project's target Sql160. Install LocalDB 2022
                     or later, or set allowOlderLocalDb (stamped on every record)" · exit 6
                     set: the case's verdict
          record     set: flags {older-localdb} · notChecked {platform} · stamp LocalDB 15.0.x
laws:     S4 A3 H4 H7
source:   expected · TWIN_SUCCESSION Appendix C (2019 is 15.x; a Sql160 project needs 16.x) and §10 (the override on
          local copies only, stamped)
```

### ENV-44 · the environment's schema uses a feature LocalDB cannot host
```case
kind:     sql
env:      QA = pg plus dbo.Attachment with a FILESTREAM column
action:   facsimile prove QA   (on LocalDB)
expect:   outcome    stop base-unpublishable · "QA's schema does not publish to LocalDB (FILESTREAM). Record it as Not
                     checked" · exit 9
laws:     H3 A3 H4
source:   expected · Microsoft docs (SQL Server Express LocalDB does not support FILESTREAM)
```

## Trust and metadata

### ENV-45 · an untrusted FK and an untrusted CHECK in the environment
```case
kind:     corpus
env:      QA = pg + FK_Order_Customer and CK_Order_Total (Total >= 0), both added WITH NOCHECK · Order 3 rows · one
          orphan CustomerId · one Total -5
change:   head declares both constraints (trusted) and adds Order.Note NULL
expect:   answers    QA: Trust(Order,FK_Order_Customer)=untrusted · Trust(Order,CK_Order_Total)=untrusted (catalog
                     reads; VIEW DEFINITION suffices)
          copy       both constraints untrusted, as in QA
          outcome    the copy's delta and verdict equal the oracle's (F3); if the delta re-validates them: verdict
                     blocked constraint · Msg 547 · FK_Order_Customer · exit 3
laws:     F9 F2 F3 F5
source:   verify · ARCH §11.1: does Extract carry WITH NOCHECK, and does DacFx then emit WITH CHECK CHECK for an
          untrusted constraint the model declares trusted? If Extract drops it, the planter must apply trust(…).
note:     A trust witness is DDL, applied by the planter, not by witness.sql (which F2 keeps DML-only).
```

### ENV-46 · a disabled FK in QA
```case
kind:     corpus
env:      QA = pg + FK_Order_Customer disabled (NOCHECK CONSTRAINT) · Order 3 rows · one orphan
change:   head declares FK_Order_Customer enabled and adds Order.Note NULL
expect:   answers    QA: Trust(Order,FK_Order_Customer)=disabled
          files      delta.QA.sql differs from Script(head, main build): the disabled key is touched in QA only
          outcome    the copy reproduces QA's delta and verdict (F3)
laws:     F9 F7 F3
source:   verify · ARCH §11.1, whether Extract carries the disabled state onto the copy; that a disabled FK changes the
          environment's delta is red-team #1 ✓
```

### ENV-47 · a system-named default in the environment
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · Customer.Region carries an unnamed default (DF__Customer__Regio__<hex>)
change:   head adds Customer.Nickname NULL (the repository declares no default on Region)
expect:   files      delta.QA.sql (offline) equals Script(head, live QA) after normalization, including the DROP of
                     the system-named default (F5)
          outcome    the copy yields the same delta and verdict as QA (F3)
laws:     F9 F5 F2
source:   verify · does Extract keep a system-generated constraint name, and can an offline Script name it in a DROP?
          If not, F5 fails here and the copy publishes a different system name.
```

## CDC

### ENV-48 · a tightening on a CDC-tracked table
```case
kind:     sql
env:      QA = pg · Customer 6 rows · CDC enabled on dbo.Customer
action:   facsimile read QA; facsimile prove QA   (head Customer.Email NULL→NOT NULL)
expect:   answers    Cdc(Customer)=yes (sys.tables.is_tracked_by_cdc; VIEW DEFINITION suffices)
          outcome    stop cdc · "Customer is tracked by CDC, and LocalDB has none. This change is principal-led" · exit 9
          record     flags {cdc}
laws:     H2 H3 A3 H4
source:   expected · ARCH §6; red-team #6 (CDC hazards: 4928 on sp_rename, 4711 on TRUNCATE, a stale capture instance)
```

### ENV-49 · an additive column on a CDC-tracked table still stops
```case
kind:     sql
env:      QA = pg · Customer 6 rows · CDC enabled on dbo.Customer
action:   facsimile read QA; facsimile prove QA   (head adds Customer.Nickname NULL)
expect:   questions  none (T0)
          outcome    stop cdc · "Customer is tracked by CDC …" · exit 9
laws:     H2 H4
source:   receipt · red-team #6 (a silently stale capture instance ✓: the new column is not captured and the publish
          is green)
```

### ENV-50 · a rename on a CDC-tracked table
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · CDC enabled on dbo.Customer (a stand-in server with CDC)
change:   head renames Customer.Region→SalesRegion with a refactorlog entry (sp_rename)
expect:   outcome    stop cdc · "Customer is tracked by CDC …" · exit 9, before any publish to a copy
laws:     H2 H5 A3
source:   receipt · red-team #6 (4928 on sp_rename ✓)
note:     The oracle on the full copy blocks with Msg 4928. F3 compares verdicts, so a designed stop needs a carve-out.
```

### ENV-51 · an untouched CDC-tracked table does not stop
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · CDC enabled on dbo.[Order] only
change:   head adds Customer.Nickname NULL
expect:   answers    Cdc(Customer)=no (only tables the delta touches are asked)
          outcome    verdict clean · exit 0
          record     tier T0 · no cdc flag
laws:     H2 F3
source:   expected · ARCH §6 (the cdc stop names a touched table)
```

## Staleness

### ENV-52 · answers keyed to a different delta
```case
kind:     process
setup:    answers.Dev.json measured for delta hash H1; head then narrowed Customer.Email as well (delta hash H2)
action:   facsimile prove Dev
expect:   outcome    stop stale-answers · "answers.Dev.json was measured for a different delta. Re-run facsimile read
                     Dev" · exit 5
          files      no verdict.Dev.json for head H2
laws:     E3 H2 A5
source:   expected · INVARIANTS E3; ARCH §6
```

### ENV-53 · data changed between measurement and promotion
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · Email distinct at read time
action:   facsimile read Dev; insert a duplicate Email in Dev; facsimile prove Dev   (head adds UX_Customer_Email)
expect:   outcome    verdict clean · exit 0 (prove never re-touches Dev)
          record     notChecked {change-over-time: answers measured <time> by <login>; re-probe at promotion}
laws:     H4 M3 H7
source:   expected · TWIN_SUCCESSION §6 (change over time) and §8 (the Octopus pre-flight)
note:     Not a corpus case: an oracle copy taken after the insert would disagree by design.
```

### ENV-54 · a re-read with nothing changed
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · Email NULL×2
action:   facsimile read Dev twice, a minute apart, and facsimile prove Dev after each   (head Email NULL→NOT NULL)
expect:   files      delta.Dev.sql byte-identical; answers.Dev.json identical but for the time stamp; the answers hash
                     unchanged, so the cached proof is reused
laws:     D1 D2 H7 F7
source:   expected · INVARIANTS D1 ("apart from the timestamp field")
note:     H7 has every stamp (login and time included) change the verdict's identity hash; F7 keys proofs by the
          answers hash. They must be two different hashes.
```

### ENV-55 · answers copied from another environment
```case
kind:     process
setup:    QA and UAT have identical schemas; answers.QA.json copied over answers.UAT.json
action:   facsimile prove UAT
expect:   outcome    stop stale-answers · "answers.UAT.json was measured on qa-sql/Estate. Re-run facsimile read UAT" ·
                     exit 5
laws:     F7 E3 H7
source:   expected · INVARIANTS F7 (answers are never shared across environments)
note:     H7 stamps the login and time but not the server and database read; without them this copy passes.
```

### ENV-56 · answers measured with an older probe template
```case
kind:     process
setup:    answers.Dev.json records probe text from probes.sql v1; the tool now ships v2 with a changed NotNull template
action:   facsimile prove Dev
expect:   outcome    stop stale-answers · "… Re-run facsimile read Dev" · exit 5
laws:     E3 P2 H7
source:   expected · ARCH §8 (answers stamped with the probe text)
```

## Prod

### ENV-57 · Prod has no reader
```case
kind:     pure
input:    questions HasRows(Customer) · NotNull(Customer.Email); answers for Dev, QA and UAT; config Prod.read = none
action:   Get-FxTier; Get-FxRecord
expect:   record     tier T2 · flags {unread:Prod} · notChecked {Prod, volume, change-over-time}
laws:     H4 F7
source:   expected · TWIN_SUCCESSION §6 (Prod has no reader); ARCH §7
```

### ENV-58 · read Prod is refused without connecting
```case
kind:     process
setup:    facsimile.json Prod { server prod-sql, database Estate, read none }
action:   facsimile read Prod
expect:   outcome    stop no-access · "Prod has no reader (facsimile.json environments.Prod.read = none). Prove it
                     with facsimile prove --assume Prod=worst" · exit 4
          files      network trace: no connection attempt to prod-sql
laws:     P4 H3 A3 M3
source:   expected · ARCH §8 ("read": "none")
note:     ARCH §6 does not say which stop a read: none environment gives; no-access fits who acts.
```

### ENV-59 · both worlds for Prod
```case
kind:     corpus
env:      UAT = pg · Customer 6 rows · Email distinct (the nearest read environment; Prod unread)
change:   head adds UX_Customer_Email
action:   facsimile prove --assume Prod=worst
expect:   witnesses  clean world: presence(Customer) · violating world: presence(Customer) · dup(Customer,(Email),c)
                     for each class c
          outcome    verdict blocked unique · Msg 1505 · dbo.Customer.UX_Customer_Email · exit 3, stamped assumed
          record     Prod (assumed): clean if no duplicate; blocked unique if any, under Prod's collation; the probe to
                     run before promoting is the Unique duplicate-group count; notChecked {Prod, Prod drift: schema
                     from UAT}
laws:     H4 H7 F7
source:   expected · TWIN_SUCCESSION §3.7 and §11.4 (prove both worlds; record the Msg and the probe)
note:     ARCH §8 does not say which world's verdict is the outcome; the name "worst" is read literally here.
```

### ENV-60 · a future VIEW DEFINITION-only Prod login: a presence-only change gets a real verdict
```case
kind:     corpus
env:      Prod = pg · Product 5 rows · login fx_vd (the recommended Prod ask)
change:   head drops Product.LegacyCode
expect:   answers    HasRows(Product)=yes, from sys.partitions
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3, not stamped assumed
          record     no unread:Prod flag
laws:     F1 F3 P1 P4
source:   receipt · TWIN_SUCCESSION §6b (a VIEW DEFINITION-only login predicts every presence-based guard without the
          right to read data)
```

### ENV-61 · a future VIEW DEFINITION-only Prod login: measured presence, assumed violations
```case
kind:     sql
env:      Prod = pg · Customer 6 rows · login fx_vd
action:   facsimile read Prod; facsimile prove --assume Prod=worst   (head adds UX_Customer_Email)
expect:   answers    HasRows(Customer)=yes · Unique(Customer,(Email))=unmeasured
          outcome    read: stop no-access · "fx_vd lacks SELECT on Estate (Prod) …" · exit 4
                     prove: both worlds for Unique(Customer,(Email)) only; presence is measured, not assumed
          record     stamped assumed for Unique(Customer,(Email)) alone; with HasRows=no, a single empty world, no
                     assumption
laws:     H4 H2 F7
source:   expected · TWIN_SUCCESSION §6b and §11.4
note:     ARCH defines --assume only for an environment with no answers; merging measured presence with assumed
          violations is unspecified.
```

## sys.partitions accuracy

### ENV-62 · zero vs non-zero on a quiet database
```case
kind:     sql
env:      a quiet stand-in: T0 empty heap · T1 one-row clustered table · T2 one row inserted then deleted · login fx_vd ·
          run on SQL Server 2019 and 2022
action:   the HasRows probe (sys.partitions, index_id 0 or 1, summed over partitions), beside CASE WHEN EXISTS under
          fx_full
expect:   answers    HasRows(T0)=no · HasRows(T1)=yes · HasRows(T2)=no; equal to EXISTS on every table and version
laws:     F1 P4
source:   verify · ARCH §11.2 (sys.partitions.rows is documented as approximate; is it exact at zero vs non-zero?)
```

### ENV-63 · a database that is not quiet
```case
kind:     sql
env:      T3 after DELETE of every row, ghost cleanup pending · T4 after TRUNCATE · T5 with an uncommitted INSERT open in
          another session · login fx_vd
action:   the HasRows probe
expect:   answers    HasRows(T3)=no · HasRows(T4)=no · HasRows(T5)=yes (an uncommitted row reads as present, the
                     conservative answer)
laws:     F1 H4
source:   verify · whether sys.partitions counts ghost and uncommitted rows; at deploy time the guard would wait on the
          uncommitted row's lock instead
```

### ENV-64 · heaps, partitions and secondary indexes are counted once
```case
kind:     sql
env:      a heap with two nonclustered indexes, 1 row · a table partitioned by date, its only row in partition 3 · a
          clustered table with a filtered index, 1 row · login fx_vd
action:   the HasRows probe
expect:   answers    HasRows=yes for each; Rows(T)=1 for each (index_id 0 or 1 only, all partitions)
laws:     F1
source:   expected · Microsoft docs (sys.partitions: one row per partition of every index; index_id 0 heap, 1 clustered)
```

## Extract, offline Script and xp_instance_regread

### ENV-65 · the offline Script opens no environment session
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · login fx_full
action:   facsimile read Dev with trace.xel on; then sqlpackage /Action:Script of head against live Dev, for comparison
expect:   files      trace.xel: the read's Script step opens no session (package to package); delta.Dev.sql equals the
                     live script after normalization (header, :setvar lines, database name)
laws:     F5 M3 P4
source:   receipt · TWIN_SUCCESSION §0.1 (2026-10-15: the offline delta, guard included, is identical to the live one)
```

### ENV-66 · what Extract sends
```case
kind:     sql
env:      Dev = pg · login fx_vd · XE session capturing sql_batch_completed, rpc_completed and sp_statement_completed
action:   sqlpackage /Action:Extract as fx_vd
expect:   files      trace.xel: catalog SELECTs only; records whether any EXEC xp_instance_regread appears, and whether
                     any #temp table is created
laws:     P4 M3
source:   verify · P4 names one xp_instance_regread sent by Script, but read Scripts offline; does Extract send it, or
          create temp tables?
```

### ENV-67 · a hardened login without xp_instance_regread
```case
kind:     sql
env:      Dev = pg · login fx_vd with EXECUTE on master.dbo.xp_instance_regread denied
action:   facsimile read Dev; separately, sqlpackage /Action:Script against live Dev as the same login
expect:   outcome    read completes (Extract, then the offline Script) whatever the regread permission; the live
                     Script's result is recorded
laws:     P4 F5 M3
source:   verify · TWIN_SUCCESSION §10 asks for xp_instance_regread "for Script"; if the offline path never sends it,
          the grant leaves the access ticket and P4's trace clause
```

## Two touches

### ENV-68 · read is exactly two sessions, even for an additive delta
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · login fx_full
action:   facsimile read Dev twice: head T2 (Email NOT NULL), then head T0 (adds Customer.Nickname)
expect:   files      trace.xel per run: two sessions, one Extract and one count batch; the T0 batch reads metadata only
                     (CDC, collations, compatibility level, versions, sys.partitions)
laws:     M3 P4
source:   expected · INVARIANTS M3; TWIN_SUCCESSION §3.4 (metadata measured with the counts)
```

### ENV-69 · prove touches no environment
```case
kind:     process
setup:    committed proofs/<slug>/ for Dev; warm .facsimile cache; Dev unreachable; no credentials configured
action:   facsimile prove Dev
expect:   outcome    the verdict recorded when Dev was reachable, byte-identical but for the timestamp
          files      network trace: no connection attempt to dev-sql
laws:     M3 D1 D2
source:   expected · INVARIANTS M3
```

### ENV-70 · prove on a CI agent with no extracted schema
```case
kind:     process
setup:    a CI agent: committed proofs/<slug>/ (delta, answers, witness.sql, verdict); no .facsimile cache; no route
          to Dev
action:   facsimile prove Dev
expect:   outcome    the author's verdict reproduced, or a named stop; never a proof against an empty database
laws:     M3 E3 D2
source:   verify · ARCH §8 never commits schema.<env>.dacpac, yet prove builds the copy from it and CI cannot reach the
          environment. Commit the schema (P1 lets it leave), or prove CI against the merge-base. Undecided.
note:     TWIN_SUCCESSION §8 names the inverted template that proved into an empty database; this is its successor.
```
