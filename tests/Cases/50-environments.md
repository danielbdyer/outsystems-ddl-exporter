# 50 · Environments (`ENV-nn`)

*Each environment is read on its own, under the login its reader actually holds, and proven on its own schema and answers.
These cases cover access, reachability, missing objects, lag and drift, per-environment proof, two-release state, collation,
CDC, staleness and Prod. `read` writes into `proofs/<slug>/`: `schema.<env>.dacpac` (committed, so CI reproduces the proof),
`delta.<env>.sql`, `delta.pr.sql` and `answers.<env>.json`, stamped with server, database, login, time, head commit, delta hash
and probe text. The answers' content hash covers answer values only, so F7 shares a proof by content. `read` ends
`done read` or a stop whose JSON is `proofs/<slug>/read.<env>.json`.
Logins: `fx_full` (db_datareader + VIEW DEFINITION), `fx_vd` (VIEW DEFINITION only), `fx_dr` (db_datareader only). `trace.xel` is an Extended Events capture on the stand-in, filtered to the tool's login.*

## Access

### ENV-01 · full reader: read and prove a populated tightening
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NULL×2 · login fx_full
change:   head Customer.Email NULL→NOT NULL · scripts unchanged
expect:   leg1       facsimile read Dev
                     files    proofs/<slug>/: schema.Dev.dacpac · delta.Dev.sql · delta.pr.sql · answers.Dev.json stamped
                              server dev-sql, database Estate, login fx_full, time, head commit, delta hash and probe text
                     outcome  done read · answers.Dev.json written · exit 0
          leg2       facsimile prove Dev
                     answers  HasRows(Customer)=yes · NotNull(Customer.Email)=violating
                     outcome  verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
                     record   tier T2 · flags {unread:QA, unread:UAT, unread:Prod}
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
expect:   answers    HasRows(Customer)=yes (sys.partitions) · Unique(Customer,(Email))=unmeasured (reason: SELECT)
          files      schema.Dev.dacpac · delta.Dev.sql · delta.pr.sql · answers.Dev.json holding the unmeasured entry; no
                     unmeasured answer reads as clean
          outcome    stop no-access · "fx_vd lacks SELECT on Estate (Dev). Ask a lead to run facsimile read Dev, or
                     request the grant." · exit 4
laws:     H2 H3 A3 P4
source:   expected · TWIN_SUCCESSION §6b zone 1 (VIEW DEFINITION answers populated-or-not; SELECT answers violations);
          ARCH §8 (`prove --assume` covers an environment with unmeasured answers)
note:     decision: read keeps what it could measure, marks the rest unmeasured and stops, so a lead's re-read or
          `prove --assume` (ENV-61) continues from it. The stop is the outcome; nothing claims a verdict (H1).
```

### ENV-04 · VIEW DEFINITION only, empty table: violation questions are implied
```case
kind:     sql
env:      Dev = pg · Customer 0 rows · login fx_vd
action:   facsimile read Dev; facsimile prove Dev   (head adds UX_Customer_Email)
expect:   leg1       facsimile read Dev
                     answers  HasRows(Customer)=no · Unique(Customer,(Email))={} implied by HasRows=no; no SELECT issued
                     outcome  done read · answers.Dev.json written · exit 0
          leg2       facsimile prove Dev
                     outcome  verdict clean · exit 0
                     record   tier T2
laws:     F1 H2 P4
source:   verify · ARCH §11.2: is sys.partitions.rows exactly 0 for an empty table under a VIEW DEFINITION-only login
          on 2019 and 2022? If not, the implication is unsound and read stops no-access instead.
```

### ENV-05 · VIEW DEFINITION only, guarded tightening: the guard decides without SELECT
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NULL×2 · login fx_vd
change:   head Customer.Email NULL→NOT NULL · scripts unchanged
expect:   leg1       facsimile read Dev
                     answers  HasRows(Customer)=yes · NotNull(Customer.Email)=unmeasured (reason: SELECT)
                     outcome  stop no-access · "fx_vd lacks SELECT on Estate (Dev). …" · exit 4
          leg2       facsimile prove Dev
                     outcome  verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
                     record   tier T2 · notChecked {unread:QA, unread:UAT, unread:Prod, volume} · its answers list
                              NotNull(Customer.Email) as unmeasured
          leg3       facsimile prove Dev --permissive
                     outcome  stop no-access · "fx_vd lacks SELECT on Estate (Dev). …" · exit 4 (the Permissive world
                              needs the NotNull count)
laws:     H2 H4 F3 P4
source:   expected · FINDINGS F7 (a populated make-mandatory blocks on the guard) and F1 (the guard is table-has-rows,
          not does-the-data-fit)
note:     DacFx emits every guard before the main script's first ALTER, so one populated guarded table decides Strict.
          H2 constrains clean only: an unmeasured question with no declared witness never yields clean.
```

### ENV-06 · db_datareader only: Extract refuses, read stops before any count
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · login fx_dr
action:   facsimile read Dev   (head Customer.Email NULL→NOT NULL)
expect:   outcome    stop no-access · "fx_dr lacks VIEW DEFINITION on Estate (Dev). Ask a lead to run facsimile read Dev,
                     or request the grant." · exit 4
          files      no schema.Dev.dacpac, delta or answers file; the stop's read.Dev.json only; trace.xel: no count batch
                     (Extract failed first)
          stdout     STOP no-access · … · proofs/<slug>/read.Dev.json · exit 4; sqlpackage's own message is not echoed
laws:     H1 H3 A3 M3 P3
source:   receipt · TWIN_SUCCESSION §6b (2026-10-15: sqlpackage refused a data-reader without VIEW DEFINITION:
          "you do not have View Definition permission")
```

### ENV-07 · db_datareader only: the shape verb still works
```case
kind:     sql
env:      Dev = pg · Customer 12 rows · Email NULL×1 · ''×1 · login fx_dr
action:   facsimile shape Dev dbo.Customer.Email
expect:   outcome    done shape · shape.Dev.dbo.Customer.Email.json written · exit 0
          files      the profile: rows_total 12 · nulls 1 · empty 1 · both distinct counts (confirmation counts, never
                     suppressed) · signatures seen 5 times or more · rarer ones folded into "other: n rows in m signatures"
                     · trace.xel: SELECT only; COL_LENGTH validates without VIEW DEFINITION (SELECT carries visibility)
laws:     P2 P4 P6
source:   expected · TWIN_SUCCESSION §6b (the shape profile ran as a data-reader and printed no value); INVARIANTS P6
```

### ENV-08 · no login on the server: a developer tries to read QA
```case
kind:     sql
env:      QA = pg · Customer 6 rows · the developer's Windows identity has no server login on QA (Msg 18456)
action:   facsimile read QA   (as the developer)
expect:   outcome    stop no-access · "<developer> cannot log in to Estate (QA). Ask a lead to run facsimile read QA, or
                     request the grant." · exit 4
          files      no schema.QA.dacpac, delta or answers file; the stop's read.QA.json only; the branch's Dev proofs
                     byte-identical
          record     a later prove keeps flags {unread:QA}
laws:     H3 A3 A5 P3
source:   expected · ARCH §6 no-access (the "cannot log in to" form); TWIN_SUCCESSION §3 and §11.3 (developers read Dev;
          dev leads read QA and UAT)
note:     A server login with no database user (Msg 4060) ends the same way. The refusal is recorded by number only (P3).
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
note:     Estates often deny read access to PII columns. The run must stop, never count around the column.
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
env:      on the author's branch, as the lead · Dev = pg · Customer 6 rows · Email distinct (read earlier by the author,
          login dev1) · QA = pg · Customer 9 rows · Email 'a@x.invalid' and 'A@x.invalid' · login lead1 (fx_full shape)
change:   head adds UX_Customer_Email
expect:   leg1       facsimile read QA
                     answers  QA: HasRows(Customer)=yes · Unique(Customer,(Email))={case}
                     files    schema.QA.dacpac, delta.QA.sql and answers.QA.json stamped server qa-sql, database Estate,
                              login lead1; Dev's schema, delta, answers and verdict byte-identical
                     outcome  done read · answers.QA.json written · exit 0
          leg2       facsimile prove QA
                     witnesses dup(Customer,(Email),case): two rows, which also answer HasRows(Customer)
                     outcome  verdict blocked unique · Msg 1505 · dbo.Customer.UX_Customer_Email · exit 3
                     record   a QA line beside Dev's clean line; flags drop unread:QA
laws:     F7 F3 H7
source:   expected · TWIN_SUCCESSION §8 (the lead's review extends the proof to QA and UAT)
```

### ENV-12 · the developers' real Dev login
```case
kind:     manual
setup:    a developer's laptop; facsimile.json Dev = the real Dev server and database
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
expect:   outcome    stop unreachable · "Cannot reach dev-sql (network). Check the network, or ask someone with access to
                     run facsimile read Dev." · exit 4
          files      no schema.Dev.dacpac, delta or answers file; the stop's read.Dev.json only; no cache entry claims a
                     Dev schema
          stdout     STOP unreachable · … · proofs/<slug>/read.Dev.json · exit 4; no driver text beyond the server name
laws:     H1 H3 A1 A3
source:   expected · ARCH §6
note:     A firewall that drops packets ends the same way, bounded by the connect timeout times the retry count, and the
          stop names the timeout.
```

### ENV-14 · the server name does not resolve
```case
kind:     process
setup:    facsimile.json Dev.server = dev-sql.corp.invalid (no DNS record)
action:   facsimile read Dev
expect:   outcome    stop unreachable · "Cannot reach dev-sql.corp.invalid (network) …" (the name as configured, so a
                     typo shows) · exit 4
laws:     H3 A3 A5
source:   expected · ARCH §6
```

### ENV-15 · the first connection fails once, then succeeds
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · the first login attempt is refused (server starting), the second succeeds
action:   facsimile read Dev
expect:   files      schema.Dev.dacpac, delta.Dev.sql, delta.pr.sql, answers.Dev.json; trace.xel: one failed login, then
                     exactly two sessions
          outcome    done read · answers.Dev.json written · exit 0
laws:     M3 H1
source:   expected · TWIN_SUCCESSION Appendix B ("LocalDB's first connection can time out, so retry"), applied to
          the environment connection
note:     M3's two sessions count sessions that run statements; a failed login is not a touch.
```

### ENV-16 · the connection drops between the two touches
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · the network drops after Extract and before the count batch
action:   facsimile read Dev   (head Customer.Email NULL→NOT NULL); an older proofs/<slug>/ for Dev is already committed
expect:   outcome    stop unreachable · "Cannot reach dev-sql (network) …" · exit 4
          files      none of schema.Dev.dacpac, delta.Dev.sql, delta.pr.sql or answers.Dev.json is rewritten; the older
                     files are byte-identical
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
          (TWIN_SUCCESSION §6, volume). ARCH §6's unreachable carries the timeout form.
```

### ENV-71 · a count fails in the environment: probe-failed, the number kept and the text withheld
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · Email NULL×2 · login fx_full · another session holds a schema-modification lock on
          dbo.Customer (an offline index rebuild in progress) · the count batch runs with a bounded lock wait
action:   facsimile read Dev   (head Customer.Email NULL→NOT NULL)
expect:   outcome    stop probe-failed · "A count failed on Dev with Msg 1222 (text withheld). Re-run facsimile read Dev; if
                     it repeats, ask the principal." · exit 4
          files      read.Dev.json records {"msg": 1222} and no text; neither delta.Dev.sql nor answers.Dev.json is
                     rewritten
laws:     H1 H3 P3 A3
source:   expected · ARCH §6 probe-failed; INVARIANTS P3 (an environment's error text is never recorded, only its number)
note:     decision: the count batch sets a bounded LOCK_TIMEOUT, so a Sch-M lock ends in Msg 1222 rather than a hang. Unlike
          a refused login (no-access) or a lost connection (unreachable), the environment answered, with an error.
```

## Missing objects

### ENV-18 · QA has not received the release that creates the column the change alters: lag, never silently clean
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · no Customer.EmailNormalized (the merged release that adds it as nvarchar(256) NULL
          reached Dev only)
change:   the branch narrows Customer.EmailNormalized nvarchar(256)→nvarchar(100)
expect:   questions  QA: none · QA's own delta creates the column, so it is answered from the delta: NULL in every existing
                     row (ARCH §4, coordinates rule 1)
          outcome    QA: verdict clean · exit 0
          flags      lag:QA
          record     lag:QA {adds Customer.EmailNormalized nvarchar(100)}: delta.QA.sql adds what delta.pr.sql only narrows
laws:     S3 H2 F7
source:   expected · ARCH §3 (Compare-FxDeltas), §4 coordinates rule 1, §7 (lag); red-team #8 (a missing object is never
          read as empty)
note:     The narrowing never reaches QA as a narrowing, so there is nothing to probe and no missing-object stop. The lag
          flag says QA would receive an operation beyond this PR, so the clean verdict is not silent (H2).
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

### ENV-20 · pre-cutover Dev holds only the OutSystems physical tables: everything is lag
```case
kind:     corpus
env:      Dev = OSUSR_ABC_CUSTOMER 6 rows; no dbo.Customer
change:   head Customer.Email NULL→NOT NULL (the branch alters dbo.Customer)
expect:   questions  Dev: none · every repository table is created by Dev's delta, so each is empty (ARCH §4, coordinates
                     rule 1)
          outcome    Dev: verdict clean · exit 0
          flags      lag:Dev
          record     lag:Dev {creates dbo.Customer and every other repository table} · no drift under the production
                     setting DropObjectsNotInSource=False (OSUSR_ABC_CUSTOMER is untouched)
laws:     S3 H2 F7
source:   expected · INVARIANTS S3; ARCH §3 (Compare-FxDeltas), §7 (lag, drift)
note:     The verdict is honest about Dev (creating empty tables publishes clean), and the lag flag says it is not a proof
          of this PR. With DropObjectsNotInSource=True the leftover would be drift, behind a HasRows guard (ENV-23).
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
          flags      drift:QA
          record     tier T0 · effects {adds Customer.Nickname; removes IX_Customer_Region, CK_Customer_Name} · drift:QA
                     {drops IX_Customer_Region, CK_Customer_Name}
laws:     F3 F6 F7 H4
source:   receipt · red-team #14 (environment-only objects dropped silently ✓); TWIN_SUCCESSION §2 (environment-only
          constraints and indexes silently dropped)
note:     DropObjectsNotInSource=False does not protect child objects. The captured effective options decide (F6), and
          Compare-FxDeltas names the drops, because delta.pr.sql has none.
```

### ENV-23 · an environment-only table: a phantom under the production setting, guarded when dropped
```case
kind:     corpus
env:      UAT = pg · plus dbo.OSUSR_LEGACY_ORDER 4 rows (a cutover leftover, not in the repository)
change:   head adds Customer.Nickname NULL · run twice: effective DropObjectsNotInSource=False, then =True
expect:   leg1       DropObjectsNotInSource=False
                     questions none
                     outcome  verdict clean · exit 0
                     record   effects {adds Customer.Nickname}; the leftover is untouched; no drift flag
          leg2       DropObjectsNotInSource=True
                     questions HasRows(OSUSR_LEGACY_ORDER)
                     outcome  verdict blocked guard · Msg 50000/127 · dbo.OSUSR_LEGACY_ORDER · exit 3
                     flags    drift:UAT
                     record   effects {adds Customer.Nickname; removes dbo.OSUSR_LEGACY_ORDER} · drift:UAT {drops
                              dbo.OSUSR_LEGACY_ORDER}
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
          flags      QA: drift:QA
          record     QA effects {adds Customer.Nickname; removes Customer.HotfixFlag} · drift:QA {drops Customer.HotfixFlag}
laws:     F7 F3 H2
source:   expected · skills/op/delete-attribute (a column drop on a populated table is guarded); red-team #1 (each
          environment receives its own delta)
```

### ENV-25 · QA lacks an object the base already has: lag, not the change
```case
kind:     corpus
env:      QA = pg · Order 4 rows · without IX_Order_CustomerId (merge-base, head and Dev all have it)
change:   head adds Order.Note nvarchar(200) NULL
expect:   questions  QA: none · measures Rows(Order) (the index build is a Scan)
          outcome    QA: verdict clean · exit 0
          flags      lag:QA
          record     QA effects {adds Order.Note, IX_Order_CustomerId} · lag:QA {adds IX_Order_CustomerId}
laws:     F7 H4
source:   expected · ARCH §3 (Compare-FxDeltas: delta.QA.sql against delta.pr.sql); TWIN_SUCCESSION §3.2 (the delta each
          environment actually receives)
note:     delta.pr.sql (head against the merge-base) holds only the Note add, so the index is what QA receives beyond this
          PR.
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
          flags      QA: lag:QA
          record     QA effects {adds Customer.Nickname; modifies Customer.Email nvarchar(320)→nvarchar(256)} · lag:QA
                     {modifies Customer.Email nvarchar(320)→nvarchar(256)}
laws:     F7 F1 F3
source:   expected · red-team #1 (proving against main misses the delta each environment receives); FINDINGS F1 (a
          populated narrowing blocks on the guard)
```

### ENV-72 · Compare-FxDeltas: lag is what an environment receives beyond this PR; drift is what the deploy drops
```case
kind:     pure
action:   Compare-FxDeltas
input:    delta.QA.sql: ALTER TABLE [dbo].[Customer] ADD [Nickname] NVARCHAR (40) NULL · CREATE NONCLUSTERED INDEX
          [IX_Order_CustomerId] ON [dbo].[Order] ([CustomerId]) · DROP INDEX [IX_Customer_Region] ON [dbo].[Customer]
          · delta.pr.sql: ALTER TABLE [dbo].[Customer] ADD [Nickname] NVARCHAR (40) NULL
expect:   record     lag:QA {adds IX_Order_CustomerId} · drift:QA {drops IX_Customer_Region} · the Nickname add is the PR's
                     own and in neither list
          flags      lag:QA · drift:QA
laws:     F7 H2
source:   expected · ARCH §3 (Compare-FxDeltas), §7 (the lag and drift flags)
note:     A DROP in delta.QA.sql that delta.pr.sql lacks removes an object head does not have: drift. Any other operation
          only in delta.QA.sql is lag. An environment equal to the merge-base gets neither flag.
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
          witnesses  Dev: presence(Customer) · QA: dup(Customer,(Email),case), two rows that also answer HasRows(Customer)
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
env:      Dev and QA = pg · Customer 6 rows · Email distinct (identical schema and answer values)
change:   head adds UX_Customer_Email
action:   facsimile prove Dev QA
expect:   outcome    verdict clean · exit 0
          files      verdict.Dev.json and verdict.QA.json cite one proof identity (equal delta hash and answers content
                     hash); one copy built, one publish · verdicts.json indexes both
laws:     F7 D1 M2
source:   expected · INVARIANTS F7; ARCH §8 (the answers' content hash covers answer values only)
note:     The server, database, login and time stamps are not in the content hash, so environments read by different people
          at different times still share. A QA delta that differs by one drift statement makes two proofs.
```

### ENV-29 · the same delta and booleans, different counts
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NULL×2
          QA = pg · Customer 40 rows · Email NULL×7
change:   head Customer.Email NULL→NOT NULL
action:   facsimile prove Dev QA
expect:   outcome    Dev and QA: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          files      two proofs (the answers content hashes differ) with byte-identical witness plans
laws:     F7 D1
source:   expected · INVARIANTS F7 (proofs keyed by the answers hash); ARCH §8 (the content hash covers answer values)
note:     The answer values are counts (P1), so differing counts never share a proof even when every boolean agrees.
```

### ENV-30 · answers are never unioned
```case
kind:     corpus
env:      Dev = pg · Customer 4 rows · Order 3 rows, one CustomerId with no Customer · Product 5 rows · Code distinct
          QA = pg · Customer 4 rows · Order 3 rows, every CustomerId resolves · Product 5 rows · Code 'AB'×2
change:   head adds FK_Order_Customer (Order.CustomerId→Customer.Id) and UX_Product_Code
action:   facsimile prove Dev QA
expect:   witnesses  Dev: orphan(Order.(CustomerId)→Customer), which also answers presence of Order · presence(Product)
                     QA: presence(Order) (+link Customer) · dup(Product,(Code),exact), two rows that also answer presence
          copy       no copy holds both an orphan and a duplicate
          outcome    Dev: verdict blocked constraint · Msg 547 · FK_Order_Customer · exit 3
                     QA: verdict blocked unique · Msg 1505 · dbo.Product.UX_Product_Code · exit 3
laws:     F7 M2 F1 F3
source:   expected · red-team #5 (a union of environments builds a state no environment has; one proof per distinct
          (delta, facts)); FINDINGS F9 (an orphan blocks the FK, Msg 547) and F10 (a duplicate blocks, Msg 1505)
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
          nullable column the same model change blocks)
note:     Each tier is Get-FxTier over that environment's own questions. ARCH §7 calls the tier identical in every
          environment, which holds only when the deltas agree (reported).
```

### ENV-32 · an open lag window holds a table this environment's delta touches
```case
kind:     process
setup:    estate/in-flight.md (facsimile.json `inFlight`) row CHG-1 · tables dbo.Customer · phase 1 of 2; the branch does
          not edit the ledger
action:   facsimile prove Dev   (head adds Customer.Nickname NULL, so delta.Dev.sql touches dbo.Customer)
expect:   outcome    stop in-flight · "dbo.Customer is in an open two-release window (estate/in-flight.md, CHG-1). Hold
                     this change, or make it the window's next release." · exit 5
          files      verdict.Dev.json records the stop; no verdict is claimed
laws:     H3 A3 A5
source:   expected · ARCH §2 row 27 (keyed on the tables of each environment's delta); FINDINGS F17 (the hazard: a publish
          during the lag window reverted the in-flight change on a green publish); scripts/inflight-check.mjs (the hold,
          ported)
```

### ENV-33 · the change is the window's next release: a warning, not a stop
```case
kind:     corpus
env:      Dev = pg · Customer 6 rows · Email NOT NULL physically (R1 landed) · in-flight row CHG-1 holds dbo.Customer
change:   head = R2: Customer.Email NULL→NOT NULL · the branch advances CHG-1 to phase 2 of 2 in estate/in-flight.md
expect:   outcome    verdict clean · exit 0 (no in-flight stop)
          record     tier T0 · flags {in-flight, unread:QA, unread:UAT, unread:Prod} · warnings {in-flight: the PR edits the
                     ledger, so it is taken as the window's next release}
laws:     H3 A5 F8
source:   expected · ARCH §2 row 27 (a PR that also edits the ledger warns rather than stops); scripts/inflight-check.mjs
          (`ledgerEdited`)
```

### ENV-34 · drift alone puts a held table into QA's delta
```case
kind:     sql
env:      Dev = pg · QA = pg plus Customer.HotfixFlag (environment-only) · in-flight row CHG-1 holds dbo.Customer
action:   facsimile read Dev; facsimile read QA; facsimile prove Dev; facsimile prove QA
          (head adds Product.Nickname NULL; the branch touches no Customer file)
expect:   outcome    Dev: verdict clean · exit 0
                     QA: stop in-flight · "dbo.Customer is in an open two-release window …" · exit 5
laws:     F7 H3
source:   expected · ARCH §2 row 27 (the hold is keyed on each environment's delta tables, not on changed files); FINDINGS
          F17 (any publish that touches a held table reverts it)
note:     QA's delta drops HotfixFlag from dbo.Customer, so QA touches the held table although the branch does not. The
          Twin-era check read tables from the changed files and would have passed it.
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
source:   receipt · FINDINGS F7 (the one-release tightening blocks); red-team #1 (R1 and R2 both merged before QA deploys)
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
          flags      QA: lag:QA
          record     lag:QA {renames Customer.Region → SalesRegion}
laws:     F7 S3 F3
source:   receipt · FINDINGS F13 (a release carrying a rename and a populated tightening is refused atomically)
note:     ARCH §4 coordinates rule 2: a column renamed in the same delta is probed under its old name.
```

## Collation and platform

### ENV-38 · the copy takes the environment's database collation, not LocalDB's
```case
kind:     corpus
env:      Dev = pg, database collation Latin1_General_CI_AI · Product 5 rows · Code 'cafe' and 'café'
change:   head adds UX_Product_Code
expect:   answers    Collation(Product.Code)=Latin1_General_CI_AI · Unique(Product,(Code))={accent}
          witnesses  dup(Product,(Code),accent): two rows, which also answer HasRows(Product)
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
          flags      QA: lag:QA
          record     lag:QA {modifies Product.Code collation Latin1_General_CI_AS→Latin1_General_CI_AI; rebuilds UX_Product_Code}
laws:     F7 F9 F3
source:   receipt · red-team #3 (data-reading DDL with no guard: a collation change on a unique column ✓)
note:     ARCH §4 Unique: duplicates are counted under the collation the index will be built with, not the one the column has.
```

### ENV-40 · a #temp table in an authored script, the environment's server collation unlike LocalDB's
```case
kind:     corpus
env:      UAT = pg on a server collated Latin1_General_CI_AS; database collation SQL_Latin1_General_CP1_CI_AS ·
          Category 3 rows
change:   pre-deploy: CREATE TABLE #map (Code nvarchar(30)) … JOIN dbo.Category c ON c.Code = m.Code (no COLLATE
          DATABASE_DEFAULT) · witness.sql declares the shapes the script handles
expect:   leg1       on a copy server collated like UAT's (the corpus job's SQL Server 2022)
                     outcome  verdict blocked collation · Msg 468 · Script.PreDeployment.sql · exit 3
          leg2       on LocalDB (server collation SQL_Latin1_General_CP1_CI_AS)
                     outcome  stop platform · "tempdb collation SQL_Latin1_General_CP1_CI_AS differs from UAT's
                              Latin1_General_CI_AS and the script compares #temp columns. <remedy>" · exit 6
                     record   notChecked ∋ platform:tempdb-collation
laws:     F9 H4 H2
source:   expected · ARCH §6 (blocked collation only when the copy's server collation equals the environment's;
          otherwise stop platform); red-team #13 (#temp joins hit Msg 468 against the server collation ✓)
note:     On LocalDB the copy cannot raise UAT's 468, so a clean there would be false. The stop is a designed one (F3
          scope); the oracle's verdict is recorded for information only.
```

### ENV-41 · the converse: LocalDB would raise a 468 the environment does not
```case
kind:     corpus
env:      Dev = pg on a server collated Latin1_General_CI_AS; database collation Latin1_General_CI_AS · Category 3 rows
change:   the ENV-40 pre-deploy · witness.sql declares the shapes
expect:   leg1       on LocalDB (server collation SQL_Latin1_General_CP1_CI_AS)
                     outcome  stop platform · "tempdb collation SQL_Latin1_General_CP1_CI_AS differs from Dev's
                              Latin1_General_CI_AS and the script compares #temp columns. <remedy>" · exit 6
          leg2       on a copy server collated like Dev's
                     outcome  verdict clean · exit 0, equal to the oracle's
laws:     F9 H5 H4
source:   expected · ARCH §6 (a 468 is a verdict only when the copy's server collation equals the environment's;
          otherwise stop platform)
note:     LocalDB's 468 here would be a false block. The rule reads static facts (the two server collations differ and the
          script compares #temp columns), so the false block never reaches a verdict.
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
          record     set: flags {older-localdb} · notChecked ∋ platform:LocalDB 15 < Sql160 · stamp LocalDB 15.0.x
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
note:     A trust witness is applied by the planter inside its suspend-and-restore envelope (ARCH §5 step 4), never by
          witness.sql, which stays DML only (F2).
```

### ENV-46 · a disabled FK in QA
```case
kind:     corpus
env:      QA = pg + FK_Order_Customer disabled (NOCHECK CONSTRAINT) · Order 3 rows · one orphan
change:   head declares FK_Order_Customer enabled and adds Order.Note NULL
expect:   answers    QA: Trust(Order,FK_Order_Customer)=disabled
          files      delta.QA.sql differs from delta.pr.sql: the disabled key is touched in QA only
          flags      lag:QA
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
          flags      drift:QA
          outcome    the copy yields the same delta and verdict as QA (F3)
laws:     F9 F5 F2
source:   verify · does Extract keep a system-generated constraint name, and can an offline Script name it in a DROP?
          If not, F5 fails here and the copy publishes a different system name.
```

## CDC

### ENV-48 · a tightening on a CDC-tracked table, on LocalDB
```case
kind:     sql
env:      QA = pg · Customer 6 rows · CDC enabled on dbo.Customer · login fx_full
action:   facsimile read QA; facsimile prove QA on LocalDB   (head Customer.Email NULL→NOT NULL)
expect:   leg1       facsimile read QA
                     answers  Cdc(Customer)=yes (sys.tables.is_tracked_by_cdc; VIEW DEFINITION suffices)
                     outcome  done read · answers.QA.json written · exit 0
          leg2       facsimile prove QA
                     outcome  stop cdc · "dbo.Customer is tracked by CDC, and LocalDB has none. This change is
                              principal-led." · exit 9
                     record   flags {cdc}
laws:     H2 H3 A3 H4
source:   expected · ARCH §6 (CDC, precisely: a tracked touched table sets the cdc flag, and on LocalDB stops cdc);
          red-team #6 (CDC hazards: 4928 on sp_rename, 4711 on TRUNCATE, a stale capture instance)
```

### ENV-49 · an additive column on a CDC-tracked table still stops on LocalDB
```case
kind:     sql
env:      QA = pg · Customer 6 rows · CDC enabled on dbo.Customer
action:   facsimile read QA; facsimile prove QA on LocalDB   (head adds Customer.Nickname NULL)
expect:   questions  none (T0)
          outcome    stop cdc · "dbo.Customer is tracked by CDC …" · exit 9
          record     flags {cdc}
laws:     H2 H4
source:   expected · ARCH §6 (CDC, precisely); red-team #6 (a silently stale capture instance ✓: the new column is not
          captured and the publish is green)
note:     The publish itself would be green; the hazard is the stale capture instance, which no copy without CDC can show.
```

### ENV-50 · a rename on a CDC-tracked table: blocked cdc on a server with CDC, stop cdc on LocalDB
```case
kind:     corpus
env:      QA = pg · Customer 6 rows · CDC enabled on dbo.Customer (a stand-in server with CDC)
change:   head renames Customer.Region→SalesRegion with a refactorlog entry (sp_rename)
expect:   leg1       the corpus job on SQL Server 2022 with CDC, the copy's capture enabled on dbo.Customer as QA's is
                     outcome  verdict blocked cdc · Msg 4928 · dbo.Customer · exit 3, equal to the oracle's
                     flags    cdc
          leg2       on LocalDB
                     outcome  stop cdc · "dbo.Customer is tracked by CDC, and LocalDB has none. This change is
                              principal-led." · exit 9, before any publish to a copy
laws:     H2 H5 A3 F3
source:   verify · whether the copy reproduces QA's capture instance (CDC is not in the dacpac model, so it would be a
          metadata witness); red-team #6 (4928 on sp_rename ✓) is the oracle's verdict
note:     The LocalDB leg is a designed stop (F3 scope): the oracle's 4928 is recorded for information only.
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
expect:   outcome    stop stale-answers · "answers.Dev.json was measured for a different delta or head. Re-run facsimile
                     read Dev." · exit 5
          files      verdict.Dev.json records the stop; no verdict for head H2
laws:     E3 H2 A5
source:   expected · INVARIANTS E3; ARCH §6
```

### ENV-53 · data changed between measurement and promotion
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · Email distinct at read time
action:   facsimile read Dev; insert a duplicate Email in Dev; facsimile prove Dev   (head adds UX_Customer_Email)
expect:   outcome    verdict clean · exit 0 (prove never re-touches Dev)
          record     notChecked ∋ drift-since-measure · the stamps say when and by whom the answers were measured, so the
                     promotion pre-flight knows what to re-probe
laws:     H4 M3 H7
source:   expected · TWIN_SUCCESSION §6 (change over time) and §8 (the Octopus pre-flight)
note:     Not a corpus case: an oracle copy taken after the insert would disagree by design.
```

### ENV-54 · a re-read with nothing changed
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · Email NULL×2
action:   facsimile read Dev twice, a minute apart, and facsimile prove Dev after each   (head Email NULL→NOT NULL)
expect:   files      delta.Dev.sql byte-identical; answers.Dev.json identical but for the time stamp; the answers content
                     hash and the verdict's identity hash unchanged, so the cached proof is reused
laws:     D1 D2 H7 F7
source:   expected · INVARIANTS D1 ("apart from the timestamp field") and H7 (the login and the time are stamps, not inputs)
```

### ENV-55 · answers copied from another environment
```case
kind:     process
setup:    QA and UAT have identical schemas and values; answers.QA.json copied over answers.UAT.json
action:   facsimile prove UAT
expect:   outcome    stop answers-invalid · "answers.UAT.json names a server other than UAT's (qa-sql/Estate). Re-run
                     facsimile read UAT." · exit 5
laws:     F7 E3 H7
source:   expected · ARCH §6 answers-invalid; ARCH §8 (answers stamped with server and database)
note:     The values are identical, so the content hash cannot catch the copy; the server and database stamps do.
```

### ENV-56 · answers measured with an older probe template
```case
kind:     process
setup:    answers.Dev.json records probe text from probes.sql v1; the tool now ships v2 with a changed NotNull template
action:   facsimile prove Dev
expect:   outcome    stop answers-invalid · "answers.Dev.json does not validate (a recorded probe text is not an instance of
                     the shipped NotNull template). Re-run facsimile read Dev." · exit 5
laws:     E3 P2 H7
source:   expected · ARCH §8 (answers stamped with the probe text); INVARIANTS P2 (the probe text sent is a template
          instance)
note:     decision: a template change is neither a different delta nor a different head, so it is not stale-answers; the
          recorded text no longer validates against the template it claims (reported).
```

## Prod

### ENV-57 · Prod has no reader
```case
kind:     pure
input:    questions HasRows(Customer) · NotNull(Customer.Email); answers for Dev, QA and UAT; config Prod.read = none
action:   Get-FxTier; Get-FxRecord
expect:   record     tier T2 · flags {unread:Prod} · notChecked ∋ unread:Prod, volume, drift-since-measure
laws:     H4 F7
source:   expected · TWIN_SUCCESSION §6 (Prod has no reader); ARCH §7; INVARIANTS H4 (the closed tokens)
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
expect:   witnesses  clean world: presence(Customer) · violating world: dup(Customer,(Email),c) for each class c, two rows
                     that also answer HasRows(Customer)
          outcome    verdict blocked unique · Msg 1505 · dbo.Customer.UX_Customer_Email · exit 3, stamped assumed
          flags      assumed:Prod
          record     Prod (assumed): clean if no duplicate; blocked unique if any, under Prod's collation; the probe to
                     run before promoting is the Unique duplicate-group count; notChecked ∋ assumed:Prod (schema from UAT)
laws:     H4 H7 F7
source:   expected · TWIN_SUCCESSION §3.7 and §11.4 (prove both worlds; record the Msg and the probe); ARCH §8 (--assume)
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
expect:   leg1       facsimile read Prod
                     answers  HasRows(Customer)=yes · Unique(Customer,(Email))=unmeasured (reason: SELECT)
                     outcome  stop no-access · "fx_vd lacks SELECT on Estate (Prod) …" · exit 4
          leg2       facsimile prove --assume Prod=worst
                     outcome  both worlds for Unique(Customer,(Email)) only; presence is measured, not assumed
                     record   stamped assumed for Unique(Customer,(Email)) alone; with HasRows=no, a single empty world and no
                              assumption
laws:     H4 H2 F7
source:   expected · TWIN_SUCCESSION §6b and §11.4; ARCH §8 (--assume covers an environment with unmeasured answers)
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
source:   verify · INVARIANTS P4: the offline Script sends nothing, and a live Script sends xp_instance_regread; does
          Extract send it too, or create temp tables?
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

### ENV-70 · prove on a CI agent from the committed proofs alone
```case
kind:     process
setup:    a CI agent: committed proofs/<slug>/ (schema.Dev.dacpac, delta.Dev.sql, delta.pr.sql, answers.Dev.json,
          witness.sql, verdict.Dev.json); no .facsimile cache; no route to Dev; no credentials
action:   facsimile prove Dev · then the same with schema.Dev.dacpac deleted from the checkout
expect:   leg1       outcome  the author's verdict reproduced, byte-identical but for the timestamp
                     files    network trace: no connection attempt to dev-sql; the copy is published from the committed
                              schema.Dev.dacpac
          leg2       outcome  stop answers-invalid naming the missing schema.Dev.dacpac the answers are keyed to · exit 5;
                              never a proof against an empty or merge-base database
laws:     M3 E3 D2
source:   expected · ARCH §8 (schema.<env>.dacpac is committed so CI and reviewers reproduce the proof); TWIN_SUCCESSION §8
note:     The Twin-era PR pipeline proved into an empty database (TWIN_SUCCESSION §8); the committed schema closes that
          gap. decision: leg2's stop is answers-invalid, the nearest kind for an incomplete proofs folder (reported).
```
