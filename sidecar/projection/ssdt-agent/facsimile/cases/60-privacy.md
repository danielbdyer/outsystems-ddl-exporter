# 60 · Privacy (`PRV-nn`)

*Every P law, attacked. The stand-in environment carries canaries in every table and value class: `CANARY-7f3a9c` in strings, 913577913 in integers, 1913-05-17 in dates.
A leak scan reads everything the tool writes or prints: answers, deltas, verdicts, witness plans, the copy, the base backup, logs, stdout and stderr. One hit fails the case.
Logins are as in `50-environments.md` (`fx_full`, `fx_vd`, `fx_dr`); `trace.xel` is an Extended Events capture on the stand-in, filtered to the tool's login.*

## P1 · Only schema and integers leave

### PRV-01 · the canary sweep
```case
kind:     corpus
env:      Dev = pg · every table populated · canaries in every string, integer and date column, including the
          violating rows: Email NULL×1 · Email 'CANARY-7f3a9c@corp.example'×2 · one Order orphan · Product.Code of 12
          characters
change:   head adds UX_Customer_Email · Product.Code nvarchar(50)→nvarchar(10) · FK_Order_Customer
action:   facsimile read Dev; facsimile prove Dev; facsimile prove Dev --permissive; facsimile status
expect:   files      no canary in answers.Dev.json, delta.Dev.sql, verdict.Dev.json, the witness plan, any log under
                     .facsimile/, or the base backup
          copy       no canary in any row of any table
          stdout     no canary on stdout or stderr
laws:     P1 P5 A1
source:   expected · INVARIANTS P1 (the canary property)
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
action:   facsimile read Dev; unpack the cached schema.Dev.dacpac
expect:   files      the Extract command line sets neither ExtractAllTableData nor TableData; the package has no data
                     part; no canary inside it
laws:     P1
source:   expected · Microsoft docs (sqlpackage Extract: ExtractAllTableData defaults to False; TableData names tables
          whose data is extracted)
```

### PRV-04 · a literal inside the environment's own code is schema, and stays out of committed files
```case
kind:     sql
env:      QA = pg + an environment-only view dbo.vVip (WHERE Email = N'CANARY-7f3a9c@corp.example') + an
          environment-only default on Customer.Region whose literal is the canary
action:   facsimile read QA; facsimile prove QA   (head adds Customer.Nickname NULL)
expect:   files      the canary is in schema.QA.dacpac (a cache, never committed); nothing under proofs/<slug>/ holds
                     it: delta.QA.sql drops the default by name only, and the view is not in the delta
                     (DropObjectsNotInSource=False)
          copy       the view's definition holds the canary; no row does
laws:     P1 P5
source:   expected · INVARIANTS P1 (the extracted schema may leave); ARCH §8 (caches are never committed)
note:     P5's canary property must read "no environment row value": code written into the environment is schema,
          which P1 lets out.
```

### PRV-05 · committed proofs hold no values
```case
kind:     process
setup:    proofs/<slug>/ as written by PRV-01
action:   the runner's leak scan over every committed file, plus a structural check of delta.Dev.sql
expect:   files      no canary; delta.Dev.sql holds no INSERT … VALUES beyond head's own seed text, and no literal
                     absent from head, the referenceData lanes and the captured options
laws:     P1 E3
source:   expected · INVARIANTS P1; ARCH §8 (what one change commits)
```

### PRV-06 · a password never leaves
```case
kind:     process
setup:    facsimile.json Dev uses SQL authentication, password env:FX_DEV_PWD = 'CANARY-pw-55e1'
action:   facsimile read Dev; facsimile prove Dev; facsimile read Dev with a wrong password
expect:   files      answers stamped with the login name only; the password in no artifact, log or echoed command line
          outcome    the wrong-password run: stop no-access · "fx_full cannot log in to dev-sql …" · exit 4
          stdout     no password on stdout or stderr in any run
laws:     P1 E4 H7
source:   expected · INVARIANTS E4 (a secret is accepted only as an env: reference)
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

### PRV-11 · sqlcmd's own syntax inside a name
```case
kind:     sql
env:      Dev = pg + column Customer.[Price$(Tax)] and a column whose name is Note<CR><LF>GO<CR><LF>x, with rows
action:   facsimile read Dev   (head narrows both)
expect:   files      trace.xel: Price$(Tax) arrives verbatim (variable substitution off, sqlcmd -x); no batch is split
                     at the embedded GO
          outcome    either both are counted whole, or before any probe is sent: stop unknown-statement · "Delta line
                     <n> names an identifier holding a line break …" · exit 9
laws:     P2 H3
source:   verify · sqlcmd splits batches on any line reading GO without parsing brackets, and substitutes $(var) unless
          -x is given (Microsoft docs); whether the tool can send such a name whole is the open part
note:     ARCH §6 has no stop kind for an identifier the shell cannot send safely; unknown-statement is the nearest.
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
                     (b) stop stale-answers · "answers.Dev.json was measured … Re-run facsimile read Dev" · exit 5
laws:     P2 E3 H7
source:   expected · INVARIANTS P2 ("the probe text sent is recorded and is a template instance")
```

### PRV-16 · the probe and shape templates pass the allowlist lint
```case
kind:     process
setup:    sql/probes.sql and sql/shape.sql, then each with one seeded violation
action:   tests/run-cases.ps1 --lint
expect:   exit       probes.sql: every template is one SELECT whose outer list holds only COUNT_BIG(*), COUNT(DISTINCT
                     col), SUM(CASE WHEN … THEN 1 ELSE 0 END), MAX(LEN or DATALENGTH of col) or an integer literal ·
                     shape.sql: values only as signatures, or as vocabulary behind --vocabulary; every grouped query
                     has HAVING COUNT_BIG(*) >= @k · both: no name of three or four parts, EXEC, sp_executesql,
                     INTO, #temp, table hint, CONVERT, CAST or PARSE; every literal is template text or a
                     schema-derived parameter (width, precision, scale, type, collation). A seeded violation fails
                     the lint, naming the template and the form
laws:     P2 P3 P6 E1
source:   expected · v3 donor io/SqlServer.cs Allowlist (origin/claude/v3-build, lines ~765-997; TWIN_SUCCESSION
          Appendix B)
note:     Schema-derived literals close the binary-search oracle: nobody can ask "how many Emails sort before 'm'?".
```

### PRV-17 · a CHECK predicate the change authored is user text
```case
kind:     sql
env:      Dev = pg · Order 4 rows · Total -5×1
action:   facsimile read Dev, twice: head adds CK_Order_Total CHECK (Total > 0); then CHECK (Total > 0 AND
          dbo.fnAudit() = 1)
expect:   answers    first: CheckFalse(Order,CK_Order_Total)=violating, counted with the predicate re-rendered from
                     parsed parts (columns of Order, literals, comparison and logical operators, IS NULL, IN lists,
                     LEN, TRY_CONVERT) · second: CheckFalse(Order,CK_Order_Total)=unmeasured (a function call), no
                     part of it sent
          files      trace.xel: the first predicate arrives only as the re-rendered template instance
laws:     P2 P3 H2
source:   verify · ARCH §4 counts CheckFalse with WHERE NOT (predicate), and the predicate is text from head, which P2
          forbids sending; can a ConstrainedLanguage parser re-render predicates soundly without ScriptDom?
```

## P3 · Nothing that quotes a value

### PRV-18 · CONVERT, CAST or PARSE in a CHECK predicate: not probed
```case
kind:     sql
env:      Dev = pg + Customer.BirthText nvarchar(20) · Customer 6 rows · one BirthText 'CANARY-31/02/1990'
action:   facsimile read Dev; facsimile prove Dev   (head adds CK_Customer_Birth CHECK (CONVERT(int,
          LEFT(BirthText, 4)) > 1900))
expect:   answers    CheckFalse(Customer,CK_Customer_Birth)=unmeasured (measured:false, reason CONVERT)
          files      trace.xel: no CONVERT, CAST or PARSE sent; no error text recorded
          outcome    prove: stop check-witness · "Write one row into witness.sql that makes CK_Customer_Birth FALSE,
                     then re-run" · exit 5
          record     notChecked names the unmeasured constraint
laws:     P3 P2 H2
source:   expected · Microsoft docs (Msg 245 quotes the value that failed to convert)
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
expect:   files      the first run stores its error as {"msg": 245}; the second stores 229; neither stores text (245's
                     text quotes the canary, 229's names objects and the database)
          stdout     the stop lines are ARCH §6 templates; no canary and no SQL Server text on stdout or stderr
laws:     P3 P1 H1
source:   expected · Microsoft docs (Msg 245 quotes the value); INVARIANTS P3 property
note:     ARCH §6 has no stop kind for a probe that fails in the environment; PRV-16's lint should make it unreachable.
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

### PRV-25 · every planted value is marked synthetic
```case
kind:     sql
env:      the copy after planting PRV-01's plan
action:   scan every planted row
expect:   copy       every planted string starts with w; email-like columns end @example.invalid; integers are ordinals
                     or class witnesses; no planted value equals an environment value
laws:     P5 D3
source:   expected · INVARIANTS P5
```

### PRV-26 · reference data on the copy comes from the repository
```case
kind:     corpus
env:      Dev = pg · Status rows with canary Codes (Dev-only values, absent from the referenceData lanes) · Order 3 rows
change:   head Order.StatusText nvarchar(20)→nvarchar(10)
expect:   copy       Status holds only the lanes' rows; no canary; presence(Order)'s ancestors reuse lane rows
laws:     P5 P1
source:   expected · ARCH §5 (the referenceData lanes run on the copy before planting)
```

### PRV-27 · a real-looking value in witness.sql
```case
kind:     process
setup:    witness.sql: INSERT dbo.Customer (Name, Email) VALUES (N'wJoe', N'joe.bloggs@corp.example')
action:   facsimile prove Dev
expect:   outcome    stop identity · "witness.sql line 1 holds a value without the synthetic marker (w prefix,
                     @example.invalid). Witness files hold synthetic values only" · exit 5
laws:     P5 P1
source:   expected · INVARIANTS P5
note:     witness.sql is committed, so a copied environment value would leave through it. No ARCH §6 kind names this;
          identity is nearest. A shape needing a real format (a day-first date) keeps the format, not the digits.
```

## P6 · Bounded discovery

### PRV-28 · k-suppression at its default
```case
kind:     sql
env:      Dev = pg · Customer 20 rows · Email shapes: first.last@x.y ×11 · name@x.y ×5 · 'first last@x.y' ×4
action:   facsimile shape Dev dbo.Customer.Email
expect:   outcome    signature lines a.a@a.a 11 and a@a.a 5; a_a@a.a (4 rows) suppressed; summary rows_total 20
laws:     P6
source:   expected · TWIN_SUCCESSION Appendix D (@k = 5)
```

### PRV-29 · a singleton placeholder is suppressed
```case
kind:     sql
env:      Dev = pg · Customer 12 rows · Email: 10 addresses · 'N/A'×1 · ''×1
action:   facsimile shape Dev dbo.Customer.Email
expect:   outcome    no A/A line (1 < k); the summary still counts empty 1 and includes N/A in both distinct counts
laws:     P6
source:   verify · TWIN_SUCCESSION §6b reports one A/A found by the Appendix D profile, whose @k is 5; either the
          experiment ran with a lower k or the line overstates. Which k does the remedy discovery need?
note:     A remedy must handle exactly such rare shapes; the profile may need to say how many rows its suppressed
          signatures cover.
```

### PRV-30 · a high-cardinality email column returns no literal
```case
kind:     sql
env:      Dev = pg · Customer 400 rows · 400 distinct canary Emails
action:   facsimile shape Dev dbo.Customer.Email --vocabulary
expect:   outcome    signatures and counts only; the vocabulary is refused (400 distinct > 50), naming the count ·
                     exit 0
          stdout     no canary
laws:     P6 P1
source:   expected · INVARIANTS P6
```

### PRV-31 · a four-value status column, and the 50 / 51 boundary
```case
kind:     sql
env:      Dev = pg · Order 41 rows · StatusText Pending×20 · Shipped×10 · Cancelled×6 · Returned×5 · Product 300
          rows · LegacyCode first 50 distinct values, then 51, each ≥ 5 times
action:   facsimile shape Dev dbo.[Order].StatusText, then with --vocabulary; facsimile shape Dev dbo.Product.LegacyCode
          --vocabulary at 50 and at 51 distinct
expect:   outcome    StatusText: no literal without the flag; with it, the four values · LegacyCode: 50 → the
                     literals; 51 → none
laws:     P6
source:   expected · INVARIANTS P6; TWIN_SUCCESSION Appendix D (the Twin's 50-distinct masking pivot)
```

### PRV-32 · a small table's PII column passes the 50-distinct test
```case
kind:     sql
env:      Dev = pg · Customer 30 rows · 30 distinct canary Emails
action:   facsimile shape Dev dbo.Customer.Email --vocabulary
expect:   outcome    no literal value: a vocabulary value is shown only when it occurs at least k times, so 30
                     singletons show nothing
          stdout     no canary
laws:     P6 P1
source:   expected · INVARIANTS P6 (literals only for vocabularies)
note:     Read literally, "50 or fewer distinct" returns all 30 addresses from a small Dev table. Applying k to each
          vocabulary value closes it; the spec does not yet say so.
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

### PRV-34 · accented Latin letters pass as characters
```case
kind:     sql
env:      Dev = pg · Customer 10 rows · Name 'José'×5 · 'Zoë'×5
action:   facsimile shape Dev dbo.Customer.Name
expect:   outcome    signatures Aaé 5 and Aaë 5: the accented letter, never the name
laws:     P6
source:   expected · TWIN_SUCCESSION Appendix D ("accented letters pass through as themselves")
```

### PRV-35 · letters outside the Latin alphabet do not pass through as values
```case
kind:     sql
env:      Dev = pg · Customer 17 rows · Name '王小明'×6 · 'Иван'×6 · 'Σοφία'×5
action:   facsimile shape Dev dbo.Customer.Name
expect:   outcome    no signature reproduces a name; each non-Latin letter collapses to a letter class
          stdout     none of the three names
laws:     P6 P1
source:   expected · INVARIANTS P6 ("structure only")
note:     Appendix D's TRANSLATE maps only a-z and A-Z, so these names come back verbatim, and k = 5 does not stop a
          common name. The SQL needs a class for every letter outside ASCII.
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

### PRV-38 · shape output stays out of answers and proofs
```case
kind:     process
setup:    PRV-31's --vocabulary run
action:   inspect the files and stdout it produced
expect:   files      the shape JSON is written outside proofs/<slug>/ and never merged into answers.<env>.json
          stdout     one line naming the file; no literal and no signature on it
laws:     P1 P6 A1
source:   expected · ARCH §8 (what one change commits)
note:     P1 says only schema and integers leave; shape's signatures and vocabulary are neither. P1 needs a scope (read
          and prove) or a carve-out for shape.
```

## The answers file and stdout

### PRV-39 · answers.json validates: integers, booleans, identifiers, hashes, timestamps
```case
kind:     process
setup:    (a) answers.Dev.json from PRV-01 · (b) the same plus a leaf "sample": "CANARY-7f3a9c@corp.example" · (c) an
          identifier leaf "Customer.CANARY-7f3a9c" naming nothing in schema.Dev.dacpac or the questions
action:   facsimile prove Dev
expect:   outcome    (a) validates and proves · (b) and (c) stop stale-answers · "answers.Dev.json does not validate at
                     <json path>. Re-run facsimile read Dev" · exit 5, naming the path, never the value
laws:     P1 E3 H3
source:   expected · INVARIANTS P1 property
note:     No ARCH §6 stop names an invalid answers file; stale-answers is the nearest.
```

### PRV-40 · stdout and stderr never carry a value
```case
kind:     corpus
env:      PRV-01's stand-in, driven to every outcome the corpus reaches: clean, each blocked reason, each stop kind
change:   PRV-01's changes, plus one change per blocked reason and per stop kind, each over canary-bearing rows
action:   capture stdout and stderr of every run
expect:   stdout     one line per run matching <OUTCOME> <reason> · <file> · exit <n>; no canary on stdout or stderr;
                     a blocked unique shows Msg 1505 and the index, never "The duplicate key value is (…)"
laws:     A1 P1 P3
source:   expected · INVARIANTS A1
```
