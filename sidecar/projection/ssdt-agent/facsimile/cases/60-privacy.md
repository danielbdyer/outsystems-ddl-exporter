# 60 · Privacy (`PRV-nn`)

*Every P law, attacked. The stand-in environment carries canaries in every table and value class: `CANARY-7f3a9c` in strings, 913577913 in integers, 1913-05-17 in dates.
A leak scan reads everything the tool writes or prints: the committed proofs (`schema.<env>.dacpac`, both deltas, answers, `witness.sql`, `shape.*.json`, verdicts), witness plans, the copy, the base backup, logs, stdout and stderr. One row value found fails the case.
Logins are as in `50-environments.md` (`fx_full`, `fx_vd`, `fx_dr`); `trace.xel` is an Extended Events capture on the stand-in, filtered to the tool's login.*

## P1 · Only schema and integers leave

### PRV-01 · the canary sweep
```case
kind:     corpus
env:      Dev = pg · every table populated · canaries in every string, integer and date column, including the
          violating rows: Email NULL×1 · Email 'CANARY-7f3a9c@corp.example'×2 · one Order orphan · Product.Code of 12
          characters
change:   head adds UX_Customer_Email · Product.Code nvarchar(50)→nvarchar(10) · FK_Order_Customer
expect:   leg1       facsimile read Dev
                     outcome  done read · answers.Dev.json written · exit 0
          leg2       facsimile prove Dev
                     outcome  verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          leg3       facsimile prove Dev --permissive · facsimile status
                     outcome  the leg2 verdict, with the Permissive publish's message and hashes recorded · done status ·
                              exit 0
          files      no canary in schema.Dev.dacpac, delta.Dev.sql, delta.pr.sql, answers.Dev.json, verdict.Dev.json, the
                     witness plan, any log under .facsimile/, or the base backup
          copy       no canary in any row of any table
          stdout     no canary on stdout or stderr
laws:     P1 P5 A1
source:   expected · INVARIANTS P1 and P5 (the canary property)
note:     The guard on the populated Product narrowing decides Strict before the unique build and the FK validation run;
          Permissive then reaches the value-bearing errors, whose text must still not appear (P3).
```

### PRV-02 · integer and date canaries: no answer carries a value
```case
kind:     sql
env:      Dev = pg + Customer.BirthDate date · OrderLine.LineNumber 913577913 · Order.Total 913577.91 · BirthDate
          1913-05-17
action:   facsimile read Dev   (head narrows Order.Total to decimal(7,2), LineNumber to smallint, BirthDate to
          smalldatetime, and adds a unique index on OrderLine (OrderId, LineNumber))
expect:   answers    every leaf is a count, a boolean or a length; no MIN or MAX of a value; 913577913, 913577.91 and
                     1913-05-17 appear nowhere
laws:     P1 P3
source:   expected · INVARIANTS P1; v3 donor io/SqlServer.cs Allowlist (MIN and MAX only over LEN or DATALENGTH)
```

### PRV-03 · Extract carries no table data
```case
kind:     sql
env:      Dev = pg · Status and Category hold canary Codes
action:   facsimile read Dev; unpack the committed proofs/<slug>/schema.Dev.dacpac
expect:   files      the Extract command line sets neither ExtractAllTableData nor TableData; the package has no data
                     part; no canary inside it
laws:     P1
source:   expected · Microsoft docs (sqlpackage Extract: ExtractAllTableData defaults to False; TableData names tables
          whose data is extracted); ARCH §8 (the extracted schema is committed, structure only)
```

### PRV-04 · a literal inside the environment's own code is schema, and leaves only as schema
```case
kind:     sql
env:      QA = pg + an environment-only view dbo.vVip (WHERE Email = N'CANARY-7f3a9c@corp.example') + an
          environment-only default on Customer.Region whose literal is the canary
action:   facsimile read QA; facsimile prove QA   (head adds Customer.Nickname NULL)
expect:   files      the canary is in schema.QA.dacpac, which is committed (ARCH §8): code written into the environment is
                     schema, which P1 lets leave · no answer, delta, verdict or log holds a row value · delta.QA.sql drops
                     the default by name only, and the view is not in the delta (DropObjectsNotInSource=False)
          copy       the view's definition holds the canary; no row does
          record     drift:QA {drops the environment-only default on Customer.Region}
laws:     P1 P5
source:   expected · INVARIANTS P1 (the extracted schema may leave); ARCH §8 (schema.<env>.dacpac is committed)
note:     P5's property ("a canary value present in the stand-in environment is absent from the copy") must read "no
          environment row value": a literal in the environment's own code reaches the copy as schema (reported).
```

### PRV-05 · committed proofs hold no row values
```case
kind:     process
setup:    proofs/<slug>/ as written by PRV-01
action:   the runner's leak scan over every committed file, plus a structural check of both deltas
expect:   files      no canary in any committed file, schema.Dev.dacpac included (structure only, P1) · delta.Dev.sql and
                     delta.pr.sql hold no INSERT … VALUES beyond head's own seed text, and no literal absent from head, the
                     referenceData lanes and the captured options
laws:     P1 E3
source:   expected · INVARIANTS P1; ARCH §8 (what one change commits)
```

### PRV-06 · a secret never leaves
```case
kind:     process
setup:    facsimile.json Dev auth "env:FX_DEV_PWD" (the variable holds 'CANARY-pw-55e1'), then auth "file:secrets/dev.pwd"
          holding the same value
action:   facsimile read Dev; facsimile prove Dev; facsimile read Dev with a wrong password, under each form
expect:   files      answers stamped with server, database and login name only; the secret in no artifact, log or echoed
                     command line, under either reference form
          outcome    the wrong-password run: stop no-access · "fx_full cannot log in to Estate (Dev). …" · exit 4
          stdout     no secret on stdout or stderr in any run
laws:     P1 E4 H7
source:   expected · ARCH §8 (a secret only as an env: or file: reference, the Twin's D9)
```

## P2 · Templated probes only

### PRV-07 · a closing bracket in a name
```case
kind:     sql
env:      Dev = pg + dbo.[Cust]]omer] (the table Cust]omer) 6 rows · Email NULL×1, in the environment and in head
action:   facsimile read Dev   (head makes Cust]omer.Email NOT NULL)
expect:   answers    HasRows(Cust]omer)=yes · NotNull(Cust]omer.Email)=violating
          files      the recorded probe text quotes [Cust]]omer]; trace.xel holds exactly that text
laws:     P2
source:   expected · Microsoft docs (QUOTENAME doubles a closing bracket)
```

### PRV-08 · a single quote in a name reaches OBJECT_ID as a literal
```case
kind:     sql
env:      Dev = pg + dbo.[O'Brien] 3 rows, in the environment and in head
action:   facsimile read Dev   (head narrows a column of O'Brien)
expect:   files      trace.xel: the validation is OBJECT_ID(N'[dbo].[O''Brien]') with the quote doubled; no syntax error
          answers    HasRows(O'Brien)=yes
laws:     P2 S3
source:   expected · INVARIANTS P2 (identifiers validated with OBJECT_ID or COL_LENGTH, then bracket-quoted)
```

### PRV-09 · statement terminators and comment markers in names
```case
kind:     sql
env:      Dev = pg + column Customer.[Email; DROP TABLE dbo.[Order]]--] and table dbo.[a/*b] 2 rows, in the
          environment and in head
action:   facsimile read Dev   (head makes both columns NOT NULL)
expect:   answers    both counted on exactly those objects
          files      trace.xel: one count batch of SELECTs; no second statement; dbo.[Order] still exists
laws:     P2 P4
source:   expected · INVARIANTS P2
```

### PRV-10 · names outside ASCII
```case
kind:     sql
env:      Dev = pg + dbo.[Clientè] and dbo.[顧客] with rows, and a column whose name holds U+202E and U+200B
action:   facsimile read Dev   (head narrows a column on each)
expect:   answers    counted on exactly those objects; no lookalike name resolves instead
          files      the recorded probe text round-trips byte-identically as UTF-8
laws:     P2 S3
source:   verify · ARCH §11.5 and §11.8: does the sqlcmd the laptop has (VS-bundled or go-sqlcmd), driven under
          ConstrainedLanguage, pass N'…' identifiers without code-page loss (-f 65001)?
```

### PRV-11 · a line break and a batch separator inside a name: identifier-unsafe, nothing sent
```case
kind:     sql
env:      Dev = pg + a Customer column whose name is Note<CR><LF>GO<CR><LF>x, with rows, in the environment and in head
action:   facsimile read Dev   (head narrows it)
expect:   outcome    stop identifier-unsafe · "[Note…x] cannot be validated against Dev safely. Nothing was sent." · exit 9
          files      trace.xel: Extract's session only; no count batch, and no batch split at the embedded GO
laws:     P2 H3 A3
source:   expected · ARCH §6 identifier-unsafe; INVARIANTS P2; Microsoft docs (sqlcmd splits batches on any line reading
          GO, without parsing brackets)
note:     The name is legal in SQL Server, but it cannot pass through sqlcmd whole, so even validating it is unsafe.
```

### PRV-41 · sqlcmd variable syntax inside a name: sent verbatim, or identifier-unsafe
```case
kind:     sql
env:      Dev = pg + column Customer.[Price$(Tax)] with rows, in the environment and in head
action:   facsimile read Dev   (head narrows it)
expect:   files      trace.xel: [Price$(Tax)] arrives verbatim (variable substitution off, sqlcmd -x)
          outcome    the column is counted whole; otherwise, before any probe is sent: stop identifier-unsafe ·
                     "[Price$(Tax)] cannot be validated against Dev safely. Nothing was sent." · exit 9
laws:     P2 H3
source:   verify · sqlcmd substitutes $(var) unless -x is given (Microsoft docs); whether the sqlcmd on the laptop
          (VS-bundled or go-sqlcmd) honours -x under ConstrainedLanguage is the open part (ARCH §11.5, §11.8)
```

### PRV-12 · a delta naming an object the environment lacks, injection text in the name
```case
kind:     sql
env:      Dev = pg · Customer 6 rows
action:   facsimile read Dev with Get-FxQuestions fed the delta line
          ALTER TABLE [dbo].[Gh'ost; DELETE FROM dbo.Customer;--] ALTER COLUMN [c] NVARCHAR (10) NOT NULL
expect:   outcome    stop missing-object · "Dev has no dbo.[Gh'ost; DELETE FROM dbo.Customer;--] …" · exit 5
          files      trace.xel: the name appears only inside the OBJECT_ID literal, quote doubled; dbo.Customer still
                     holds 6 rows
laws:     P2 S3
source:   expected · INVARIANTS P2 ("validates as a real identifier or stops")
```

### PRV-13 · agent-supplied text given to shape
```case
kind:     sql
env:      Dev = pg · Customer 6 rows
action:   facsimile shape Dev "dbo.Customer.Email; DROP TABLE dbo.[Order]"
expect:   outcome    stop missing-object · "Dev has no dbo.Customer.Email; DROP TABLE dbo.[Order] …" · exit 5
          files      trace.xel: the argument appears only inside the COL_LENGTH literal, quote-doubled; dbo.[Order] exists
laws:     P2 S3 P6
source:   expected · INVARIANTS P2 ("no agent-written or user-written text is ever executed in an environment")
note:     shape takes <env> <s.t.c> and --vocabulary only; no option accepts a predicate.
```

### PRV-14 · witness.sql and the authored scripts never reach an environment
```case
kind:     sql
env:      Dev = pg · Customer 6 rows
action:   facsimile read Dev; facsimile prove Dev; facsimile shape Dev dbo.Customer.Email, with witness.sql, the
          pre-deploy, the post-deploy and a referenceData lane each carrying the comment /* WITNESS-MARK */
expect:   files      trace.xel on Dev holds no WITNESS-MARK and no statement from those files; they run only on fx_
                     databases
laws:     P2 P4 S1
source:   expected · INVARIANTS P2
```

### PRV-15 · recorded probe text is a template instance
```case
kind:     process
setup:    answers.Dev.json from a read: (a) unchanged; (b) one probe text hand-edited to append OR 1=1
action:   facsimile prove Dev
expect:   outcome    (a) each probe text equals its template id instantiated with its recorded bindings, byte for
                     byte, and the proof runs
                     (b) stop answers-invalid · "answers.Dev.json does not validate (a probe text is not an instance of its
                     template). Re-run facsimile read Dev." · exit 5
laws:     P2 E3 H7
source:   expected · INVARIANTS P2 ("the probe text sent is recorded and is a template instance"); ARCH §6 answers-invalid
```

### PRV-16 · the probe and shape templates pass the allowlist lint
```case
kind:     process
setup:    sql/probes.sql and sql/shape.sql, then each with one seeded violation
action:   tests/run-cases.ps1 --lint
expect:   exit       probes.sql: every template is one SELECT whose outer list holds only COUNT_BIG(*), COUNT(DISTINCT
                     col), SUM(CASE WHEN … THEN 1 ELSE 0 END), MAX(LEN or DATALENGTH of col) or an integer literal; its one
                     non-template slot is the predicate exception's (a CHECK or filtered-index WHERE copied from the delta,
                     inside an integer-only wrapper) · shape.sql: values only as class-symbol signatures, or as vocabulary
                     behind --vocabulary; groups seen fewer than @k times fold into one "other" row (n rows, m signatures)
                     · both: no name of three or four parts, EXEC, sp_executesql, INTO, #temp, table hint, CONVERT, CAST or
                     PARSE in template text; every literal is template text or a schema-derived parameter (width,
                     precision, scale, type, collation). A seeded violation fails the lint, naming the template and the form
laws:     P2 P3 P6 E1
source:   expected · v3 donor io/SqlServer.cs Allowlist (origin/claude/v3-build, lines ~765-997; TWIN_SUCCESSION
          Appendix B); ARCH §4 (the predicate exception)
note:     Schema-derived literals close the binary-search oracle: nobody can ask "how many Emails sort before 'm'?".
```

### PRV-17 · the predicate exception: a CHECK copied verbatim from the delta, inside an integer-only count
```case
kind:     sql
env:      Dev = pg · Order 4 rows · Total -5×1
action:   facsimile read Dev, twice: head adds (a) CK_Order_Total CHECK (Total > 0) · (b) CK_Order_Text CHECK (StatusText
          <> N'x; DROP TABLE dbo.[Order]--')
expect:   answers    (a) CheckFalse(Order,CK_Order_Total)=violating · (b) CheckFalse(Order,CK_Order_Text)=clean
          files      trace.xel: one integer-returning count per question, whose predicate text equals the delta's generated
                     DDL byte for byte · (b)'s literal stays a literal: one statement, and dbo.[Order] still exists
laws:     P2 P3 H2
source:   expected · ARCH §4 (the predicate exception); INVARIANTS P2
note:     The predicate is model text the deploy itself will evaluate on that data, so carrying it into a count adds no
          exposure. Only CONVERT, CAST and PARSE are refused (PRV-18), because their errors quote a value.
```

## P3 · Nothing that quotes a value

### PRV-18 · CONVERT, CAST or PARSE in a CHECK predicate: not probed, recorded unmeasured
```case
kind:     sql
env:      Dev = pg + Customer.BirthText nvarchar(20) · Customer 6 rows · one BirthText 'CANARY-31/02/1990'
action:   facsimile read Dev; facsimile prove Dev   (head adds CK_Customer_Birth CHECK (CONVERT(int,
          LEFT(BirthText, 4)) > 1900))
expect:   leg1       facsimile read Dev
                     answers  CheckFalse(Customer,CK_Customer_Birth)=unmeasured (reason: CONVERT)
                     files    trace.xel: no CONVERT, CAST or PARSE sent; no error text recorded
                     outcome  done read · answers.Dev.json written · exit 0
          leg2       facsimile prove Dev
                     outcome  stop check-witness · "Write one row into witness.sql that makes CK_Customer_Birth FALSE, then
                              re-run." · exit 5
                     record   the stop's JSON lists CheckFalse(Customer,CK_Customer_Birth) unmeasured; once a witness is
                              declared, notChecked ∋ declared-not-measured
laws:     P3 P2 H2
source:   expected · ARCH §4 (the predicate exception refuses CONVERT, CAST and PARSE); Microsoft docs (Msg 245 quotes the
          value that failed to convert)
```

### PRV-19 · TRY_CONVERT counts the unconvertible and the empty
```case
kind:     sql
env:      Dev = pg · Customer 7 rows · ContactPhone '5550100'×5 · ''×1 · 'CANARY-7f3a9c'×1
action:   facsimile read Dev   (head retypes Customer.ContactPhone nvarchar(40)→bigint)
expect:   answers    Fits(Customer.ContactPhone→bigint)={nonconvertible, empty} as counts 1 and 1; no error raised
          files      trace.xel: TRY_CONVERT only; no canary in any artifact
laws:     P3 P1
source:   receipt · red-team #10 (TRY_CONVERT('') varies by type ✓: 0 for bigint, so '' is its own class)
```

### PRV-20 · a deliberately failing probe records a number only
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · ContactPhone 'CANARY-7f3a9c'×1 · a second run as fx_vd
action:   a fault-injection build of sql/probes.sql whose Fits template uses CONVERT(bigint, ContactPhone):
          facsimile read Dev; then the real build as fx_vd (SELECT denied, Msg 229)
expect:   outcome    first run: stop probe-failed · "A count failed on Dev with Msg 245 (text withheld). Re-run facsimile
                     read Dev; if it repeats, ask the principal." · exit 4 · second run: stop no-access · "fx_vd lacks
                     SELECT on Estate (Dev). …" · exit 4
          files      the first run stores {"msg": 245}; the second stores 229; neither stores text (245's text quotes the
                     canary, 229's names objects and the database)
          stdout     STOP lines in ARCH §6's templates; no canary and no SQL Server text on stdout or stderr
laws:     P3 P1 H1 H3
source:   expected · Microsoft docs (Msg 245 quotes the value); INVARIANTS P3 property; ARCH §6 probe-failed
note:     PRV-16's lint makes the fault-injected template unshippable; this case proves the stop behind it.
```

### PRV-21 · sqlpackage's own output is not kept
```case
kind:     sql
env:      Dev = pg · login fx_dr (Extract refused)
action:   facsimile read Dev
expect:   outcome    stop no-access · "fx_dr lacks VIEW DEFINITION on Estate (Dev) …" · exit 4
          files      sqlpackage's raw output is in no artifact or log; the stop carries the tool's classification only
laws:     P3 A1 H3
source:   expected · INVARIANTS P3 ("an environment's error text is never recorded, only its number")
```

## P4 · Read-only by construction

### PRV-22 · ApplicationIntent=ReadOnly on every environment connection
```case
kind:     process
setup:    recording shims in place of sqlpackage and sqlcmd on PATH
action:   facsimile doctor; facsimile read Dev; facsimile shape Dev dbo.Customer.Email
expect:   files      every environment command line carries ApplicationIntent=ReadOnly (the Extract source connection
                     string; sqlcmd -K ReadOnly); no environment connection string reaches /Action:Publish, Import,
                     DeployReport or a live Script
laws:     P4 S1
source:   expected · INVARIANTS P4
note:     ReadOnly intent routes only on an availability group and is ignored elsewhere; PRV-24's trace is the proof.
```

### PRV-23 · a login that cannot write runs every verb
```case
kind:     sql
env:      Dev = pg · login fx_ro: db_datareader + VIEW DEFINITION, DENY INSERT, UPDATE, DELETE, ALTER, CREATE TABLE,
          and EXECUTE on everything but what ENV-66 finds Extract needs
action:   facsimile doctor; facsimile read Dev; facsimile shape Dev dbo.Customer.Email
expect:   exit       0 for each; no permission error
laws:     P4
source:   expected · INVARIANTS P4 property
```

### PRV-24 · the statement trace
```case
kind:     sql
env:      Dev = pg · XE session (sql_batch_completed, rpc_completed, sp_statement_completed) filtered to the tool's
          login · run once as fx_full and once as a db_owner lead login
action:   facsimile read Dev; facsimile shape Dev dbo.Customer.Email
expect:   files      trace.xel holds only SELECTs (probe and shape template instances), Extract's catalog reads, SET
                     options, and whatever ENV-66 settles for xp_instance_regread; no INSERT, UPDATE, DELETE, MERGE,
                     CREATE, ALTER, DROP, TRUNCATE or other EXEC; the same kinds under both logins
laws:     P4 P2 M3
source:   verify · whether Extract's own catalog queries create #temp tables or EXEC anything (ENV-66); the tool's own
          statements are expected
note:     Read-only is by construction: the owner login sends the same statement kinds.
```

## P5 · Synthetic witnesses

### PRV-25 · every planted value is generated or repository-defined
```case
kind:     sql
env:      the copy after planting PRV-01's plan
action:   scan every planted row
expect:   copy       every planted string carries the w marker where its width and class allow it; every other planted value
                     is the ordinal, an entry of the planter's fixed per-class table (`''`, a day-first date, a numeric
                     overflow, a char(1)) or a repository-defined value (a seed's code, a default, a reference-data key) ·
                     no planted value equals an environment value · no value depends on a column's name
laws:     P5 D3
source:   expected · INVARIANTS P5
```

### PRV-26 · reference data on the copy comes from the repository
```case
kind:     corpus
env:      Dev = pg + FK_Order_Status (StatusId NOT NULL → Status.Id) · Status rows with canary Codes (Dev-only values,
          absent from the referenceData lanes) · Order 3 rows
change:   head Order.StatusText nvarchar(20)→nvarchar(10)
expect:   copy       Status holds only the merge-base lanes' rows; no canary · presence(Order)'s ancestor reuses a lane row
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Order · exit 3
laws:     P5 P1
source:   expected · ARCH §5 step 1 (the merge-base referenceData lanes run on the copy before planting); INVARIANTS P5
```

### PRV-27 · a real-looking value in witness.sql stops witness-not-synthetic
```case
kind:     process
setup:    witness.sql: `-- plants: dbo.Customer` then INSERT dbo.Customer (Name, Email) VALUES (N'wJoe',
          N'joe.bloggs@corp.example')
action:   facsimile prove Dev
expect:   outcome    stop witness-not-synthetic · "witness.sql line 2 carries a literal not marked synthetic. Use a w-prefixed
                     value or a repository-defined value." · exit 5
          files      the stop names the line, never the literal
laws:     P5 P1
source:   expected · ARCH §5 step 5, §6 witness-not-synthetic; INVARIANTS P5
note:     witness.sql is committed, so a copied environment value would leave through it. N'wJoe' passes; the address does
          not. A shape that needs a real format (a day-first date) keeps the format, not the digits.
```

## P6 · Bounded discovery

### PRV-28 · rare signatures are summarized at the default k
```case
kind:     sql
env:      Dev = pg · Customer 20 rows · Email shapes: first.last@x.y ×11 · name@x.y ×5 · 'first last@x.y' ×4
action:   facsimile shape Dev dbo.Customer.Email
expect:   outcome    done shape · shape.Dev.dbo.Customer.Email.json written · exit 0
          files      signature lines a.a@a.a 11 and a@a.a 5 · "other: 4 rows in 1 signature" (a_a@a.a never printed) ·
                     summary rows_total 20
laws:     P6
source:   expected · INVARIANTS P6; TWIN_SUCCESSION Appendix D (@k = 5, and the facsimile's fold into "other")
```

### PRV-29 · a singleton placeholder is summarized, not hidden
```case
kind:     sql
env:      Dev = pg · Customer 12 rows · Email: 10 addresses of one shape · 'N/A'×1 · ''×1
action:   facsimile shape Dev dbo.Customer.Email
expect:   outcome    done shape · shape.Dev.dbo.Customer.Email.json written · exit 0
          files      signature a.a@a.a 10 · "other: 1 row in 1 signature" (A/A never printed) · the summary counts empty 1
                     and includes N/A in both distinct counts (confirmation counts are never suppressed)
laws:     P6
source:   expected · INVARIANTS P6; TWIN_SUCCESSION Appendix D (§6b's A/A came from a demonstration run at k = 1 on a
          six-row table)
note:     A remedy must handle exactly such rare shapes. "other" tells the author one exists; a confirmation count (how many
          rows are 'N/A' or blank-like) then sizes it.
```

### PRV-30 · a high-cardinality email column returns no literal
```case
kind:     sql
env:      Dev = pg · Customer 400 rows · 400 distinct canary Emails
action:   facsimile shape Dev dbo.Customer.Email --vocabulary
expect:   outcome    done shape · … · exit 0
          files      signatures and counts only; the vocabulary refused (400 distinct > 50), naming the count
          stdout     no canary
laws:     P6 P1
source:   expected · INVARIANTS P6
```

### PRV-31 · a four-value status column, and the 50 / 51 boundary
```case
kind:     sql
env:      Dev = pg · Order 41 rows · StatusText Pending×20 · Shipped×10 · Cancelled×6 · Returned×5 · Product 300
          rows · LegacyCode first 50 distinct values, then 51, each seen ≥ 5 times
action:   facsimile shape Dev dbo.[Order].StatusText, then with --vocabulary; facsimile shape Dev dbo.Product.LegacyCode
          --vocabulary at 50 and at 51 distinct
expect:   outcome    each run: done shape · … · exit 0
          files      StatusText: no literal without the flag; with it, the four values · LegacyCode: 50 → the literals;
                     51 → none
laws:     P6
source:   expected · INVARIANTS P6; TWIN_SUCCESSION Appendix D (the Twin's 50-distinct masking pivot)
```

### PRV-32 · a small table's PII column passes the 50-distinct test and still shows nothing
```case
kind:     sql
env:      Dev = pg · Customer 30 rows · 30 distinct canary Emails
action:   facsimile shape Dev dbo.Customer.Email --vocabulary
expect:   files      no literal value: a vocabulary value is shown only when it is seen at least k times, so 30 singletons
                     show nothing
          stdout     no canary
laws:     P6 P1
source:   expected · INVARIANTS P6 (literals only for 50 or fewer distinct values, each seen at least k times)
note:     Without the per-value k, "50 or fewer distinct" alone would return all 30 addresses from a small Dev table.
```

### PRV-33 · signatures reveal structure only
```case
kind:     sql
env:      Dev = pg + Customer.BirthText · Customer 30 rows · Email 'first.last@corp.example'-shaped · ContactPhone
          '+1 (555) 010-9999'-shaped · BirthText '31/12/1990'-shaped ×10 and '1990-12-31'-shaped ×10
action:   facsimile shape Dev on each column
expect:   outcome    signatures a.a@a.a · +9_(999)_999-9999 · 99/99/9999 and 9999-99-99; no letter or digit of a value
laws:     P6
source:   receipt · TWIN_SUCCESSION §6b (the profile surfaced 99/99/9999 and 9999-99-99 formats without printing a
          value)
```

### PRV-34 · accented Latin letters become `x`
```case
kind:     sql
env:      Dev = pg · Customer 10 rows · Name 'José'×5 · 'Zoë'×5
action:   facsimile shape Dev dbo.Customer.Name
expect:   files      signature Aax 10 (runs of ASCII letters collapse; é and ë each become x) · no accented letter and no
                     name in the output
laws:     P6 P1
source:   expected · INVARIANTS P6 (every non-ASCII, non-punctuation character becomes x); TWIN_SUCCESSION Appendix D (the
          facsimile's shape.sql replaces the demonstration query's pass-through)
```

### PRV-35 · letters outside the Latin alphabet do not pass through as values
```case
kind:     sql
env:      Dev = pg · Customer 17 rows · Name '王小明'×6 · 'Иван'×6 · 'Σοφία'×5
action:   facsimile shape Dev dbo.Customer.Name
expect:   files      signatures built only of x, one per character (xxx 6 · xxxx 6 · xxxxx 5) · none of the three names
          stdout     none of the three names
laws:     P6 P1
source:   expected · INVARIANTS P6 (every non-ASCII character becomes the class symbol x)
note:     The demonstration query's TRANSLATE maps only a-z and A-Z (Appendix D's known limit); shape.sql maps every other
          non-punctuation character to x. Whether runs of x collapse, as letter runs do, P6 does not say (reported).
```

### PRV-36 · shape requires SELECT
```case
kind:     sql
env:      Dev = pg · Customer 6 rows · login fx_vd
action:   facsimile shape Dev dbo.Customer.Email
expect:   outcome    stop no-access · "fx_vd lacks SELECT on Estate (Dev). Ask a lead to run facsimile shape …, or
                     request the grant" · exit 4
          files      no partial profile written
laws:     P6 H3 A3
source:   expected · TWIN_SUCCESSION §6b zone 2 (discovery needs SELECT)
```

### PRV-37 · shape never runs under prove
```case
kind:     process
setup:    a change whose pre-deploy reads Customer.Email; Dev reachable; no witnesses declared
action:   facsimile prove Dev
expect:   outcome    stop authored-script · "… Run facsimile shape Dev dbo.Customer.Email for each …" · exit 5
          files      no environment session during prove; the stop names the shape command, the tool does not run it
laws:     P6 M3 P4
source:   expected · INVARIANTS M3; ARCH §6 (authored-script)
```

### PRV-38 · shape output is committed beside the answers, never merged into them
```case
kind:     process
setup:    PRV-31's --vocabulary run
action:   inspect the files and stdout it produced
expect:   files      shape.Dev.dbo.[Order].StatusText.json is committed under proofs/<slug>/ (ARCH §8) and never merged into
                     answers.Dev.json, which still validates as integers, booleans, identifiers, hashes and timestamps
          stdout     DONE shape · … · <file> · exit 0; no literal and no signature on it
laws:     P1 P6 A1
source:   expected · ARCH §8 (what one change commits: shape.*.json)
note:     P1 says only schema and integers leave; shape's signatures and its bounded vocabulary are neither, yet ARCH §8
          commits them. P1 needs a carve-out for shape, bounded by P6 (reported).
```

## The answers file and stdout

### PRV-39 · answers.json validates: integers, booleans, identifiers, hashes, timestamps
```case
kind:     process
setup:    (a) answers.Dev.json from PRV-01 · (b) the same plus a leaf "sample": "CANARY-7f3a9c@corp.example" · (c) an
          identifier leaf "Customer.CANARY-7f3a9c" naming nothing in schema.Dev.dacpac or the questions
action:   facsimile prove Dev
expect:   outcome    (a) validates and proves · (b) and (c) stop answers-invalid · "answers.Dev.json does not validate
                     (<json path>). Re-run facsimile read Dev." · exit 5, naming the path, never the value
laws:     P1 E3 H3
source:   expected · INVARIANTS P1 property; ARCH §6 answers-invalid
```

### PRV-40 · stdout and stderr never carry a value
```case
kind:     process
setup:    PRV-01's stand-in, and one fixture per outcome the corpus reaches (clean, each blocked reason, each stop kind),
          each over canary-bearing rows
action:   run each fixture's verbs, capturing stdout and stderr
expect:   stdout     one line per run, in ARCH §6's grammar: VERDICT … · <file> · exit <n>, STOP <kind> · <next step> · <file>
                     · exit <n>, or DONE <verb> · … · <file> · exit 0 · no canary on stdout or stderr · a blocked unique
                     shows Msg 1505 and the index, never "The duplicate key value is (…)"
laws:     A1 P1 P3
source:   expected · INVARIANTS A1
note:     A sweep over every outcome, not one differential proof, so it is a process case.
```
