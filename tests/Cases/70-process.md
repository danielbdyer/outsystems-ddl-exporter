# 70 · Process (`CLI-nn`, `UP-nn`, `CFG-nn`, `CI-nn`, `TLC-nn`)

*The shell's surface (ARCH §6, §8), `up`'s continuity with the Twin (T3), `facsimile.json` (E4), the gates in CI (ARCH §9–§10, TWIN_SUCCESSION §8), and the machine the tool runs on (TWIN_SUCCESSION §4, Appendices B and C).
`pure` cases call `Read-FxConfig`; `process` cases run verbs and read files, stdout and exit codes; `manual` cases belong to the laptop check and the G4 window.
Logins are as in `50-environments.md` (`fx_full`, `fx_vd`, `fx_dr`); every stdout line is checked against A1 and every exit against A3.*

## CLI · the verbs

*Setup **M0**, unless a case says otherwise: the `pg` project as `Estate.sqlproj` (DSP Sql160) on branch `make-email-required`, changing
Customer.Email NULL→NOT NULL; the ARCH §8 config without its comments; options captured; LocalDB 16.x and every tool at its pin; Dev
`Customer 6 rows · Email NULL×2`, already read with `fx_full`. `P/` is `proofs/make-email-required/`. stdout is ARCH §6's one line:
`VERDICT …` (prove), `STOP <kind> …`, or `DONE <verb> …`, each ending `· <file> · exit <n>`. Change-scoped verbs write under `P/`:
`read` its `schema.<env>.dacpac`, `delta.<env>.sql`, `delta.pr.sql` and `answers.<env>.json`; `prove` `verdict.<env>.json` (a
verdict, or that environment's stop) and, for several environments, `verdicts.json`; `shape` `shape.<env>.<s.t.c>.json`. Machine-scoped
verbs (`doctor`, `up`, `down`, `status`) write `.facsimile/out/<verb>.json`; `init` writes `facsimile.json`. Provisional, because ARCH
names only the directories: a change-scoped stop with no such file writes `P/<verb>[.<env>].json`, and `usage`
`.facsimile/out/usage.json`. Every file is written by atomic replace when the run ends.*

### CLI-01 · doctor on a ready machine
```case
kind:     process
setup:    M0 · the Dev, QA and UAT servers unreachable
action:   facsimile doctor
expect:   files      .facsimile/out/doctor.json: path and version of sqlpackage, SqlLocalDB, MSBuild (found by
                     vswhere) and sqlcmd · LocalDB 16.x against DSP Sql160 · language mode · options hash · config
                     valid · zero environment sessions
          stdout     DONE doctor · ready · .facsimile/out/doctor.json · exit 0
          exit       0
laws:     A1 A3 H1
source:   expected · ARCH §8 (doctor: tools, versions, LocalDB vs DSP, language mode, captured options, config)
note:     Reachability is checked only with `--reach <env>` (ARCH §8), so unreachable environments do not stop doctor.
```

### CLI-02 · doctor: each stop it can raise
```case
kind:     process
setup:    M0 with exactly one fault per row: (a) facsimile.json has `"projet"` · (b) no sqlpackage on PATH or in
          any Visual Studio DAC folder · (c) LocalDB 15.0 only · (d) facsimile/options.publish.xml absent ·
          (e) dev-sql does not resolve · (f) LocalDB installed but not answering · (g) dev-sql answers but refuses
          fx_full's login
action:   facsimile doctor, once per row; rows (e) and (g) as `facsimile doctor --reach Dev`
expect:   outcome    (a) stop config · "facsimile.json: $.projet: unknown key." · exit 6
                     (b) stop toolchain · "sqlpackage not found. Request the pinned sqlpackage zip through an
                     Azure DevOps Artifacts feed." · exit 6
                     (c) stop platform · "LocalDB 15.0 is older than the project's target Sql160. …" · exit 6
                     (d) stop options-unknown · "No usable captured effective options (missing). …" · exit 6
                     (e) stop unreachable · "Cannot reach dev-sql (network). …" · exit 4
                     (f) stop toolchain · "SqlLocalDB not responding (LocalDB gave no answer in 180 s after one
                     retry). …" · exit 6
                     (g) stop no-access · "fx_full cannot log in to Estate. …" · exit 4
          stdout     STOP <kind> · <next step> · .facsimile/out/doctor.json · exit <n>, one line per run
laws:     A3 H3 A1
source:   expected · ARCH §6 (the stop table: toolchain covers a LocalDB not responding, no-access a login refused), §8
          (`doctor [--reach <env>]`)
```

### CLI-03 · doctor with three faults names one next step at a time
```case
kind:     process
setup:    M0 with no sqlpackage, LocalDB 15.0 only, and no options file
action:   facsimile doctor; apply exactly the fix the stop names; repeat until ready
expect:   outcome    run 1 stop toolchain (sqlpackage) · run 2 stop platform · run 3 stop options-unknown ·
                     run 4 done doctor · ready · exit 0
          files      run 1's doctor.json already lists all three findings
laws:     A5 H3 H1
source:   expected · ARCH §6 ("each names one next step")
note:     ARCH does not order simultaneous stops; config → toolchain → platform → options-unknown is assumed.
```

### CLI-04 · read Dev
```case
kind:     process
setup:    M0 before `read` · login fx_full (VIEW DEFINITION and SELECT)
action:   facsimile read Dev
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          files      P/schema.Dev.dacpac · P/delta.Dev.sql · P/delta.pr.sql · P/answers.Dev.json, stamped with server,
                     database, login, time, head commit, delta hash and probe text, its content hash over the answer
                     values only
          stdout     DONE read · Dev measured · P/answers.Dev.json · exit 0
          exit       0
laws:     M3 A1 P1 E3
source:   expected · ARCH §3 steps 1–4, §8 (`read` writes the schema, both deltas and the answers; what one change
          commits)
```

### CLI-05 · read with a VIEW DEFINITION-only login, presence-only change
```case
kind:     process
setup:    M0 before `read`, but the change drops Customer.ContactPhone (a guard on Customer, nothing else) · login
          fx_vd (VIEW DEFINITION, no SELECT)
action:   facsimile read Dev
expect:   questions  HasRows(Customer)
          answers    HasRows(Customer)=yes, from sys.partitions
          stdout     DONE read · Dev measured · P/answers.Dev.json · exit 0
laws:     M3 P4
source:   expected · the engine side is recorded in TWIN_SUCCESSION §6b (2026-10-15: VIEW DEFINITION alone
          extracts, scripts the guard and reads sys.partitions)
```

### CLI-06 · read: each stop it can raise
```case
kind:     process
setup:    M0 before `read`, with exactly one fault per row: (a) dev-sql does not resolve · (b) login fx_vd ·
          (c) login fx_dr (a data reader without VIEW DEFINITION) · (d) Customer.Email dropped in Dev between the
          Extract and the count · (e) a T-SQL error in Tables/Customer.sql · (f) the options file absent · (g) the
          delta holds a row-reading statement that no vocabulary row matches
action:   facsimile read Dev, once per row
expect:   outcome    (a) stop unreachable · "Cannot reach dev-sql (network). …" · exit 4 · (b) stop no-access "fx_vd
                     lacks SELECT on Estate. …" · exit 4 · (c) stop no-access "fx_dr lacks VIEW DEFINITION on Estate. …"
                     · exit 4 · (d) stop missing-object "Dev has no dbo.Customer.Email, and this delta does not create
                     it. …" · exit 5 · (e) stop build-failed · exit 7 · (f) stop options-unknown · exit 6 ·
                     (g) stop unknown-statement "Delta line <n> reads rows, …" · exit 9
          stdout     STOP <kind> · <next step> · P/read.Dev.json · exit <n>
          files      P/answers.Dev.json neither written nor altered by any row
laws:     A3 H3 S3 H1
source:   expected · ARCH §6; the engine refusal in row (c) is recorded in TWIN_SUCCESSION §6b
note:     read needs no LocalDB, so it never raises platform (CLI-07).
```

### CLI-07 · a lead without LocalDB can still read
```case
kind:     process
setup:    M0 without SqlLocalDB · a lead's login on QA with VIEW DEFINITION and SELECT
action:   facsimile read QA
expect:   stdout     DONE read · QA measured · P/answers.QA.json · exit 0
laws:     A3 M3
source:   expected · ARCH §3 (read needs the project build and sqlpackage, not LocalDB); TWIN_SUCCESSION §8 (the
          lead's review extends the proof to QA and UAT)
note:     read still needs MSBuild, since the offline Script compares head, so the lead's machine needs VS.
```

### CLI-08 · read Prod is refused without connecting
```case
kind:     process
setup:    M0 · Prod configured with `"read": "none"`
action:   facsimile read Prod
expect:   outcome    stop config · "facsimile.json: $.environments.Prod.read: none. Prove Prod with `facsimile
                     prove --assume Prod=worst`." · exit 6
          stdout     STOP config · … · P/read.Prod.json · exit 6
          files      zero connection attempts to prod-sql (trace)
laws:     P4 H3 A3
source:   expected · ARCH §8 (`"read": "none"`)
note:     ARCH §6 has no stop for an environment configured as unreadable; `config` is assumed.
```

### CLI-09 · prove Dev: clean
```case
kind:     process
setup:    M0, but Dev `Customer 0 rows` · every environment server unreachable
action:   facsimile prove Dev
expect:   answers    HasRows(Customer)=no · NotNull(Customer.Email)=clean
          outcome    verdict clean · exit 0
          record     tier T2 · flags {unread:QA, unread:UAT, unread:Prod} · notChecked non-empty · silence ok
          files      P/verdict.Dev.json, carrying every H7 stamp · zero environment sessions
          stdout     VERDICT clean · Successfully published database · P/verdict.Dev.json · exit 0
laws:     A1 A3 M3 H1 D6
source:   expected · ARCH §5, §6, §7 (the tier follows the questions, not the answers)
```

### CLI-10 · prove Dev: blocked by the guard
```case
kind:     process
setup:    M0
action:   facsimile prove Dev
expect:   witnesses  null(Customer.Email), which is also Customer's presence row
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          stdout     VERDICT blocked guard · dbo.Customer · P/verdict.Dev.json · exit 3
          exit       3
laws:     A1 A3 H1 M2
source:   expected · ARCH §6; the engine outcome is F7 (a populated NULL→NOT NULL is refused, Msg 50000)
```

### CLI-11 · prove with no environment named
```case
kind:     process
setup:    M0 · answers for Dev only · QA and UAT readable but unread · Prod `"read": "none"`
action:   facsimile prove
expect:   files      P/verdict.Dev.json only
          record     flags {unread:QA, unread:UAT, unread:Prod} · notChecked lists unread:QA, unread:UAT and
                     unread:Prod
          stdout     VERDICT blocked guard · dbo.Customer · P/verdict.Dev.json · exit 3
laws:     H4 F7 A1
source:   expected · ARCH §7 (`unread:<env>`), §8 (`prove [<env>…]`)
note:     assumed: with no argument, prove proves every environment that has answers and flags the rest.
```

### CLI-12 · prove Dev QA: two verdicts, one line naming verdicts.json
```case
kind:     process
setup:    M0 · answers for Dev (Email NULL×2) and for QA (`Customer 0 rows`)
action:   facsimile prove Dev QA
expect:   files      P/verdict.Dev.json (blocked guard) · P/verdict.QA.json (clean) · P/verdicts.json indexing both
          stdout     exactly one line: VERDICT blocked guard · dbo.Customer · P/verdicts.json · exit 3
          exit       3
laws:     A1 F7 H1
source:   expected · ARCH §6 ("a multi-environment prove prints the line of its most severe outcome (stop > blocked >
          clean) and names verdicts.json"), INVARIANTS F7
```

### CLI-13 · prove --assume Prod=worst
```case
kind:     process
setup:    M0, but the change adds UX_Customer_Email, a unique index on Customer.Email · Prod `"read": "none"`, no
          answers
action:   facsimile prove --assume Prod=worst
expect:   record     stamped `assumed` · schema from Dev, the nearest read environment · world populated-and-clean:
                     verdict clean · world populated-and-violating: blocked unique · Msg 1505 · UX_Customer_Email ·
                     the probe to run before promoting: Unique(Customer,(Email)) with its probe text ·
                     flags {unread:Prod, assumed:Prod} · notChecked includes assumed:Prod
          files      P/verdict.Prod.json · zero environment sessions
          stdout     VERDICT blocked unique · UX_Customer_Email · P/verdict.Prod.json · exit 3
laws:     H4 A1 M3
source:   expected · ARCH §8 (`prove --assume`); TWIN_SUCCESSION §3 step 7 (both worlds, and the probe to run)
note:     "nearest read environment" and the exit of a two-world result are unspecified; assumed: the first read
          environment in config order, and the violating world's outcome.
```

### CLI-14 · prove --permissive after a block
```case
kind:     process
setup:    M0
action:   facsimile prove Dev --permissive
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     permissive arm: Msg 515 on dbo.Customer.Email · content hashes of dbo.Customer before and
                     after · its option set differs from Strict's in BlockOnPossibleDataLoss alone
          stdout     VERDICT blocked guard · dbo.Customer · P/verdict.Dev.json · exit 3
laws:     F6 A1 S5
source:   expected · ARCH §5 step 10, §8 (`--permissive`); an ALTER COLUMN … NOT NULL over NULLs fails with Msg 515 once
          the guard is lifted (documented engine behaviour, not yet witnessed here)
```

### CLI-15 · prove: the stops raised before any environment
```case
kind:     process
setup:    M0 with exactly one fault per row: (a) facsimile.json invalid · (b) no MSBuild reachable through vswhere
          · (c) LocalDB 15.0 · (d) options with BlockOnPossibleDataLoss=False · (e) a T-SQL error in the project
action:   facsimile prove Dev, once per row
expect:   outcome    (a) stop config · exit 6 · (b) stop toolchain · exit 6 · (c) stop platform · exit 6 ·
                     (d) stop options-unknown · "No usable captured effective options (BlockOnPossibleDataLoss=False).
                     …" · exit 6 · (e) stop build-failed · "The project does not build: <first error>." · exit 7
          stdout     STOP <kind> · <next step> · P/prove.json · exit <n>
          files      P/verdict.Dev.json neither written nor altered · no database created
laws:     A3 H3 A1 F6
source:   expected · ARCH §6
```

### CLI-16 · prove: the stops for the principal and the developer
```case
kind:     process
setup:    M0 with exactly one condition per row: (a) a delta line reads rows and matches no vocabulary row ·
          (b) the publish ends with a message number no classifier rule names · (c) the answers carry
          Cdc(Customer)=yes · (d) Dev's schema holds a FILESTREAM column · (e) the in-flight ledger (`inFlight`:
          ledgers/in-flight.md) lists dbo.Customer in an open two-release window · (f) the ledger is unreadable ·
          (g) as (e), and the PR also edits the ledger
action:   facsimile prove Dev, once per row
expect:   outcome    (a) stop unknown-statement · exit 9 · (b) stop unclassified · exit 9 · (c) stop cdc · "dbo.Customer
                     is tracked by CDC, and LocalDB has none. This change is principal-led." · exit 9 ·
                     (d) stop base-unpublishable · "Dev's schema does not publish to LocalDB (FILESTREAM). Record it
                     as Not checked." · exit 9 · (e) stop in-flight · "dbo.Customer is in an open two-release window
                     (ledgers/in-flight.md). Hold this change, or make it the window's next release." · exit 5 ·
                     (f) stop config · "facsimile.json: $.inFlight: ledgers/in-flight.md cannot be read." · exit 6 ·
                     (g) verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3, with a warning that the PR
                     edits the ledger
          stdout     STOP <kind> · <next step> · P/verdict.Dev.json · exit <n> (row (g): its VERDICT line)
          files      P/verdict.Dev.json holds outcome `stop` with its next step filled in; never `clean`
laws:     A3 H2 H3
source:   expected · ARCH §2 row 27 (the in-flight hold, its exemption and its unreadable-ledger refusal), §6 (CDC,
          precisely: the flag, the stop on LocalDB); LocalDB does not support FILESTREAM (Microsoft, SQL Server
          Express LocalDB)
note:     The in-flight key is the tables of Dev's own delta, not the PR's changed files (TIER-27).
```

### CLI-17 · A5 · stale-answers, then read again
```case
kind:     process
setup:    M0 · after `read Dev`, Tables/Customer.sql also narrows Name to nvarchar(50)
action:   facsimile prove Dev; then, as told, facsimile read Dev; then facsimile prove Dev
expect:   outcome    run 1 stop stale-answers · "answers.Dev.json was measured for a different delta or head. Re-run
                     `facsimile read Dev`." · exit 5 · run 3 verdict blocked guard · Msg 50000/127 · dbo.Customer ·
                     exit 3
          stdout     run 1: STOP stale-answers · … · P/verdict.Dev.json · exit 5
laws:     A5 H3 E3
source:   expected · ARCH §6 (stale-answers)
```

### CLI-18 · A5 · missing-object, then prove against what the environment has
```case
kind:     process
setup:    M0 · a lead runs `read QA` while a deploy drops Customer.Email from QA between the Extract and the count
action:   facsimile read QA; then, as told, facsimile read QA again; then facsimile prove QA
expect:   outcome    run 1 stop missing-object · "QA has no dbo.Customer.Email, and this delta does not create it. Prove
                     against what QA has, or record QA as Not checked." · exit 5 · run 3 a verdict on QA's own delta
          files      run 1 writes no P/answers.QA.json
laws:     A5 S3 H3
source:   expected · ARCH §6, INVARIANTS S3; red-team #8 (a missing object read as empty)
note:     with per-environment deltas this stop needs a schema that moves between read's two touches, or questions
          from a final target (F8); it is rare, which is why it must stay a stop.
```

### CLI-19 · A5 · authored-script, then shape and declare
```case
kind:     process
setup:    M0 · the change also adds a pre-deploy `UPDATE dbo.Customer SET Email = N'none@example.invalid' WHERE
          Email IS NULL`
action:   facsimile prove Dev; then, as told, facsimile shape Dev dbo.Customer.Email and declare in witness.sql, under
          `-- witness: Authored(pre:Script.PreDeployment.sql:1)`, one row per shape the script handles; then
          facsimile prove Dev
expect:   outcome    run 1 stop authored-script · "The change runs authored SQL reading dbo.Customer.Email. Run
                     `facsimile shape Dev dbo.Customer.Email` for each, then declare a witness in witness.sql for every
                     shape the script handles, or `-- shapes: none (<reason>)`, and make the script check its own
                     preconditions." · exit 5 · run 3 verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     run 3 tier T3 · residue {pre-deploy: Customer.Email backfilled · main: none}
laws:     A5 H3 H6
source:   expected · ARCH §4 (Authored), §5 step 5 (`-- witness: <question key>` blocks), §6; F2 and F6 (a pre-deploy
          commits before the guard fires)
```

### CLI-20 · A5 · check-witness, then write the row
```case
kind:     process
setup:    M0, but the change adds CK_Order_Total CHECK (Total >= 0) · Dev `Order 4 rows · Total<0 ×1`, read
action:   facsimile prove Dev; then, as told, add to witness.sql, under `-- witness: CheckFalse(Order,CK_Order_Total)`,
          one Order row with Total -1; facsimile prove Dev
expect:   answers    CheckFalse(Order,CK_Order_Total)=violating
          outcome    run 1 stop check-witness · "Write one row into witness.sql that makes CK_Order_Total FALSE, then
                     re-run." · exit 5 · run 2 verdict blocked constraint · Msg 547 · CK_Order_Total · exit 3
laws:     A5 H3 F1
source:   expected · ARCH §4 (CheckFalse), §6; F10 (add-check blocks as create-fk does, Msg 547 at WITH CHECK CHECK)
```

### CLI-21 · A5 · computed-witness, then write the row
```case
kind:     process
setup:    M0, but the change adds Product.CodeNum AS (CONVERT(int, Code)) PERSISTED · Dev `Product 3 rows`, one Code
          not numeric
action:   facsimile prove Dev; then, as told, add to witness.sql, under `-- witness: Computed(Product.CodeNum)`, one
          Product row with Code N'wABC'; facsimile prove Dev
expect:   outcome    run 1 stop computed-witness · "Write one row into witness.sql whose dbo.Product.CodeNum expression
                     fails, then re-run." · exit 5 · run 2 verdict blocked conversion · Msg 245 · dbo.Product.CodeNum ·
                     exit 3
          record     run 2 notChecked includes declared-not-measured
laws:     A5 H3 H2
source:   expected · ARCH §4 (Computed: unmeasured, agent-declared), §6; red-team #3 (a PERSISTED computed column reads
          rows with no guard ✓)
note:     Computed has no environment count, so acceptance excludes it (ARCH §5 step 7) and the record says the
          question was declared, not measured.
```

### CLI-22 · A5 · planter-needs-row, then write the INSERT
```case
kind:     process
setup:    M0, but the change narrows Product.Name to nvarchar(100) · Dev has CK_Product_Code CHECK (Code LIKE
          'P-[0-9][0-9][0-9][0-9]') and `Product 3 rows`, every Name fitting
action:   facsimile prove Dev; then, as told, add under `-- plants: dbo.Product` one INSERT with Code N'P-0001';
          facsimile prove Dev
expect:   outcome    run 1 stop planter-needs-row · "Cannot make a valid row for dbo.Product (CK_Product_Code). Write one
                     valid INSERT for dbo.Product into witness.sql (or up.sql) under `-- plants: dbo.Product`." · exit 5 ·
                     run 2 verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
laws:     A5 H3
source:   verify · whether witness-not-synthetic (ARCH §5 step 5) accepts a literal whose shape the repository's own
          CHECK forces: no `w`-marked value fits 'P-[0-9][0-9][0-9][0-9]'; ARCH §11.7 (how often the planter's
          literals miss real CHECKs) is open too
note:     If the check refuses N'P-0001', run 2 is stop witness-not-synthetic, and the two stops' next steps contradict
          each other (an A5 chain that cannot end); the first implementation run decides.
```

### CLI-23 · A5 · acceptance, then fix witness.sql
```case
kind:     process
setup:    M0, but Dev's Email holds no NULL (answered NotNull(Customer.Email)=clean) · witness.sql, left from an
          earlier attempt, inserts a Customer with Email NULL
action:   facsimile prove Dev; then, as told, delete that row from witness.sql; facsimile prove Dev
expect:   outcome    run 1 stop acceptance · "The copy answers NotNull(Customer.Email) yes, but Dev answered no. Fix
                     witness.sql." · exit 5 · run 2 verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     A5 F1 H3
source:   expected · ARCH §5 step 7, §6
```

### CLI-24 · A5 · identity, then keep witness.sql to DML
```case
kind:     process
setup:    M0 · witness.sql holds `ALTER TABLE dbo.Customer ADD Note nvarchar(10) NULL;` beside its INSERTs
action:   facsimile prove Dev; then, as told, delete the ALTER; facsimile prove Dev
expect:   outcome    run 1 stop identity · "witness.sql changed the schema (dbo.Customer.Note). Witness files are DML
                     only." · exit 5 · run 2 verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     A5 F2 H3
source:   expected · ARCH §5 step 8, §6; red-team #11 (witness.sql can change the schema)
```

### CLI-25 · shape Dev dbo.Customer.Email
```case
kind:     process
setup:    M0, with Dev's Email holding NULL×2 · ''×1 · 'N/A'×1 and one case twin · login fx_full
action:   facsimile shape Dev dbo.Customer.Email
expect:   files      P/shape.Dev.dbo.Customer.Email.json: rows_total 6 · nulls 2 · empty 1 · whitespace_only ·
                     edge_spaces · max_len · max_bytes · distinct_as_collated below distinct_binary · signatures in
                     class symbols (a, A, 9, _, x) · signatures seen fewer than 5 times folded into "other: n rows in m
                     signatures" · no literal value
          stdout     DONE shape · dbo.Customer.Email profiled · P/shape.Dev.dbo.Customer.Email.json · exit 0
laws:     P6 A1
source:   expected · ARCH §8 (`shape` writes shape.<env>.<s.t.c>.json, committed with the proofs); INVARIANTS P6;
          TWIN_SUCCESSION Appendix D (the profile and its k = 5)
```

### CLI-26 · shape: arguments and stops
```case
kind:     process
setup:    M0, one row each: (a) `shape Dev Customer.Email` (a two-part name) · (b) `shape Dev dbo.Customer.Nope` ·
          (c) `shape Prod dbo.Customer.Email` with Prod `"read": "none"` · (d) `shape Dev dbo.Customer.Email`
          under login fx_vd
action:   facsimile shape …, once per row
expect:   outcome    (a) stop usage · "Unknown verb or argument Customer.Email. Usage: facsimile shape <env>
                     <schema>.<table>.<column> [--vocabulary]." · exit 1 · (b) stop missing-object · exit 5 · (c) stop
                     config, with no connection to prod-sql · exit 6 · (d) stop no-access "fx_vd lacks SELECT on
                     Estate. …" · exit 4
          stdout     STOP <kind> · <next step> · <its file> · exit <n>
laws:     A3 H3 P2
source:   expected · ARCH §6, §8; INVARIANTS P2 (identifiers are validated before any probe)
```

### CLI-27 · status
```case
kind:     process
setup:    M0 · `up` converged · a proof copy left behind by a killed run · then Tables/Customer.sql widens Name to
          nvarchar(200)
action:   facsimile status
expect:   files      .facsimile/out/status.json: every database in the tool's registry (fx_local, the orphaned copy) ·
                     fx_local stale (schema) · each base cache and its age · answers.Dev stale (the head commit and delta
                     hash differ from its stamps) · QA and UAT unread · Prod not readable · no database created or
                     dropped · zero environment sessions
          stdout     DONE status · … · .facsimile/out/status.json · exit 0
laws:     T3 M3 A1
source:   expected · ARCH §8 (status); parity with TwinSchemaLoopTests "law 1: up materializes, converges, and
          re-converges after an estate edit" (status tells the truth at every step)
```

### CLI-28 · down drops every registered proof copy, and nothing else
```case
kind:     process
setup:    M0 · fx_local, two registered proof copies (one held open by a sqlcmd session) · a person's Estate_Local,
          Sandbox and fx_reports on the same instance
action:   facsimile down; facsimile down
expect:   files      after run 1: zero registered proof copies · fx_local and its rows kept · Estate_Local, Sandbox and
                     fx_reports untouched (absent from the registry) · .facsimile/ caches kept
          stdout     run 1: DONE down · 2 proof copies dropped · .facsimile/out/down.json · exit 0
                     run 2: DONE down · 0 proof copies dropped · .facsimile/out/down.json · exit 0
laws:     S6 S1 T3
source:   expected · ARCH §8 (down: "fx_local and the caches stay … never drops a database absent from the tool's
          registry"); INVARIANTS S6
```

### CLI-29 · down --all clears fx_local and the caches, never the proofs
```case
kind:     process
setup:    CLI-28's machine, with .facsimile/ holding base backups and head builds
action:   facsimile down --all; then facsimile prove Dev
expect:   files      zero databases in the registry, fx_local included · .facsimile/ holds only out/down.json · P/
                     (schema.Dev.dacpac among it), facsimile.json, facsimile/options.publish.xml and witness.sql
                     byte-identical · fx_reports untouched
          stdout     DONE down · … · .facsimile/out/down.json · exit 0
          outcome    the prove that follows rebuilds its base from P/schema.Dev.dacpac and reaches verdict blocked
                     guard · Msg 50000/127 · dbo.Customer · exit 3, the verdict of a warm run (D2)
laws:     S6 D2 T3
source:   expected · ARCH §8 (`--all` also drops fx_local and clears caches); INVARIANTS D2
```

### CLI-30 · init in a fresh repository
```case
kind:     process
setup:    a folder holding Estate.sqlproj and nothing of the facsimile's
action:   facsimile init; then facsimile doctor
expect:   files      facsimile.json: `project` Estate.sqlproj · `options` facsimile/options.publish.xml · no secret ·
                     no comment · accepted by Read-FxConfig under both PowerShell editions
          stdout     DONE init · facsimile.json written · facsimile.json · exit 0
          outcome    doctor then: stop options-unknown · exit 6 (its next step: capture the options)
laws:     T3 E4
source:   expected · ARCH §8 (init); parity with THE_TWIN §7 (`twin init`: write a starter twin.json)
```

### CLI-31 · init leaves an existing file alone, and does not guess a project
```case
kind:     process
setup:    one row each: (a) facsimile.json exists · (b) two .sqlproj files · (c) no .sqlproj
action:   facsimile init, once per row
expect:   outcome    (a) done init · facsimile.json is already present; nothing was written · exit 0 · (b) stop config ·
                     "facsimile.json: $.project: two project files (A.sqlproj, B.sqlproj); …" · exit 6 · (c) stop
                     config · "facsimile.json: $.project: no project file." · exit 6
          files      (a) facsimile.json byte-identical · (b), (c) nothing written
laws:     E4 H3 A3
source:   expected · parity with the Twin's init (TWN-307: an existing file is left as it is, and said so); ARCH §6
          (config: "facsimile.json: <json path>: <problem>.")
```

### CLI-32 · unknown verbs and arguments stop usage
```case
kind:     process
setup:    M0, one row each: `facsimile prov` · `facsimile` · `facsimile prove --permisive` · `facsimile prove
          --assume Prod` · `facsimile prove --assume Prod=best` · `facsimile read` · `facsimile read Staging`
action:   run each
expect:   outcome    each a stop usage · "Unknown verb or argument <x>. Usage: <one line>." naming the offending word
                     (`read Staging`: Staging, no such environment in facsimile.json) · exit 1
          stdout     one line each: STOP usage · … · .facsimile/out/usage.json · exit 1
          files      nothing else written · no PowerShell parameter-binding error on stdout
laws:     H1 A1 A3
source:   expected · ARCH §6 (usage, exit 1, the agent acts); INVARIANTS H1, A1
note:     Exit 1 is usage's alone: a script that dies on an uncaught error must not be mistaken for it (INV-H1-03).
```

### CLI-33 · stdout carries one line; progress goes to stderr
```case
kind:     process
setup:    M0
action:   facsimile prove Dev 1> out.txt 2> err.txt, under `powershell.exe -File` (5.1) and `pwsh -File` (7)
expect:   files      out.txt: the A1 line alone, with no sqlpackage, MSBuild or sqlcmd output, no ANSI escape and no
                     Write-Host text · err.txt: at least one progress line per §5 step · with err.txt discarded,
                     stdout and P/verdict.Dev.json still carry the whole outcome
          stdout     VERDICT blocked guard · dbo.Customer · P/verdict.Dev.json · exit 3
laws:     A1
source:   verify · whether Write-Host or information-stream progress reaches redirected stdout under `powershell.exe
          -File`, and which stderr writer is ConstrainedLanguage-safe
```

### CLI-34 · two proofs at once use distinct copies
```case
kind:     process
setup:    M0 · answers for Dev and QA
action:   in three shells within one second: facsimile prove Dev · facsimile prove QA · facsimile prove Dev
expect:   copy       three distinct registered databases fx_make-email-required_Dev_<r1>, …_QA_<r2> and …_Dev_<r3>; no
                     run restores into, plants into or drops another's
          files      each verdict byte-identical, bar timestamps, to its solo run · P/verdict.Dev.json one complete file
laws:     S5 S1 D1
source:   expected · ARCH §5 step 1 (`fx_<slug>_<env>_<rand4>`, recorded in the tool's registry)
```

### CLI-35 · two first-time proofs race for one base cache
```case
kind:     process
setup:    M0 · no base backup cached · Dev and QA share schema, referenceData and options (one base key)
action:   facsimile prove Dev and facsimile prove QA, started together
expect:   outcome    both verdicts as when run alone; neither run stops
          files      exactly one base backup kept under its key; a half-written .bak is never restored; at most one
                     fx_base_<hash12>_<rand4> built
laws:     D5 S5 H1
source:   expected · ARCH §5 step 1 (the base is built once, under a cache lock, in a uniquely named database);
          INVARIANTS D5 ("two runs missing the same key at once do not collide")
```

### CLI-36 · down while a proof is publishing
```case
kind:     process
setup:    M0 · facsimile prove Dev in its publish step · a second row: in its plant step
action:   facsimile down, in another shell
expect:   outcome    publish step: prove ends stop unclassified (the publish lost its database) · exit 9 · plant step:
                     stop internal · "Unexpected failure at plant (<type>). Nothing claims a verdict." · exit 9 · down:
                     done down · exit 0
          files      zero registered proof copies afterwards · P/verdict.Dev.json holds the stop, never a verdict
laws:     H2 H1 S6
source:   expected · INVARIANTS H2 (anything less than clean is a stop); ARCH §6 (internal)
```

### CLI-37 · Ctrl+C mid-run is interrupted
```case
kind:     process
setup:    M0 · a P/verdict.Dev.json from earlier inputs
action:   facsimile prove Dev; Ctrl+C during the plant step; then, as told, facsimile down; then facsimile prove Dev
expect:   outcome    the interrupted run: stop interrupted · "The run was interrupted at plant. `facsimile down` removes
                     its copies; re-run." · exit 9 · the last prove: verdict blocked guard · Msg 50000/127 · dbo.Customer
                     · exit 3
          stdout     the interrupted run: STOP interrupted · … · P/verdict.Dev.json · exit 9, and no VERDICT line
          files      P/verdict.Dev.json holds the stop, written by atomic replace; no partial JSON under P/ · the run's
                     copy dropped by its own cleanup, or left registered for down to remove
laws:     H1 S6 S5 A5
source:   expected · INVARIANTS H1 (a kill leaves no file claiming a verdict), S6; ARCH §6 (interrupted)
note:     An uncatchable kill (Stop-Process -Force) prints nothing and leaves the earlier file byte-identical
          (INV-H1-02).
```

### CLI-38 · read: a count that fails on the environment is probe-failed, its text withheld
```case
kind:     process
setup:    M0 before `read` · a test hook makes the batched count fail on Dev, one row each: (a) Msg 1205 (chosen as
          deadlock victim) · (b) Msg 1222 (lock request time-out)
action:   facsimile read Dev, once per row; then, as told, facsimile read Dev again with the hook off
expect:   outcome    run 1: stop probe-failed · "A count failed on Dev with Msg 1205 (text withheld). Re-run `facsimile
                     read Dev`; if it repeats, ask the principal." · exit 4 (row (b) naming Msg 1222) · run 2: done
                     read · Dev measured · exit 0
          stdout     run 1: STOP probe-failed · … · P/read.Dev.json · exit 4
          files      P/read.Dev.json carries the number and no engine text · P/answers.Dev.json not written by run 1
laws:     H3 A3 P3 A5
source:   expected · ARCH §6 (probe-failed); INVARIANTS P3 ("an environment's error text is never recorded, only its
          number")
note:     A failed count is never read as zero: no answers file is written from a partial batch.
```

### CLI-39 · prove: answers that do not validate, name another server, or contradict themselves
```case
kind:     process
setup:    M0, one fault per row in P/answers.Dev.json: (a) a count edited to the string "two" · (b) `server` set to
          qa-sql, while Dev's configured server is dev-sql · (c) HasRows(Customer)=no beside
          NotNull(Customer.Email)=violating · (d) Rows(Customer)=0 beside Unique(Customer,(Email))={exact}
action:   facsimile prove Dev, once per row; then, as told, facsimile read Dev; facsimile prove Dev
expect:   outcome    (a) stop answers-invalid · "answers.Dev.json does not validate (<the count's JSON path>: expected an
                     integer), or it names a server other than Dev's. Re-run `facsimile read Dev`." · exit 5
                     (b) stop answers-invalid naming `$.server: qa-sql, not dev-sql` · exit 5
                     (c), (d) stop answers-invalid naming the contradicting pair · exit 5
                     after the re-read: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          stdout     STOP answers-invalid · … · P/verdict.Dev.json · exit 5
          files      no database created for any row
laws:     H3 A3 A5 E3 P1
source:   expected · ARCH §6 (answers-invalid: "does not validate (<path>), or it names a server other than <env>'s"),
          §8 (answers stamped with server and database); INVARIANTS P1 (the answers schema's leaves), E3
note:     A contradiction is a validation failure: a table with no rows cannot hold a violating one. Proving it anyway
          would plant a violation into an "empty" table and break F1 silently.
```

### CLI-40 · A5 · witness-failed, then fix the row
```case
kind:     process
setup:    M0 · witness.sql line 3, under `-- plants: dbo.Customer`, inserts a Customer whose Name is NULL (Name is NOT
          NULL)
action:   facsimile prove Dev; then, as told, give that row Name N'wName1'; facsimile prove Dev
expect:   outcome    run 1 stop witness-failed · "witness.sql line 3 failed with Msg 515. Fix that row." · exit 5 · run 2
                     verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          stdout     run 1: STOP witness-failed · … · P/verdict.Dev.json · exit 5
laws:     A5 H3 A3
source:   expected · ARCH §5 step 5 ("a failing row stops witness-failed"), §6
note:     The row runs inside the planter's suspend-and-restore envelope, so it fails on the column's NOT NULL, not on a
          constraint the planter suspended; the message names the line, never the value (P3).
```

### CLI-41 · A5 · witness-not-synthetic, then use a synthetic or repository value
```case
kind:     process
setup:    M0 · witness.sql line 2, under `-- plants: dbo.Customer`, declares a Customer with Email
          N'ada.lovelace@contoso.com' · variants: Email N'wada@example.invalid' · a Status row whose Code is N'Shipped',
          the seed's own
action:   facsimile prove Dev, once per row; for the first, then as told, replace the literal with N'wEmail1' and prove
          again
expect:   outcome    first row: stop witness-not-synthetic · "witness.sql line 2 carries a literal not marked synthetic.
                     Use a `w`-prefixed value or a repository-defined value." · exit 5 · after the fix, and for both
                     variants: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          stdout     first run: STOP witness-not-synthetic · … · P/verdict.Dev.json · exit 5
laws:     A5 P5 H3
source:   expected · ARCH §5 step 5 ("a literal that is neither synthetic nor repository-defined stops
          witness-not-synthetic"); INVARIANTS P5
note:     The check guards P5 where a person types values: a pasted production email must never reach a copy or a
          committed file.
```

### CLI-42 · read: an identifier that cannot be validated safely sends nothing
```case
kind:     process
setup:    M0 before `read` · Dev's Customer also holds a column named `Note$(Region)`, and head makes it NOT NULL ·
          a second row: a column whose name holds a control character (U+0007)
action:   facsimile read Dev, once per row
expect:   outcome    stop identifier-unsafe · "dbo.Customer.Note$(Region) cannot be validated against Dev safely. Nothing
                     was sent." · exit 9 (the second row likewise, the character shown as U+0007)
          trace      Dev received Extract's metadata reads only; no probe batch
          files      P/answers.Dev.json not written
laws:     P2 H3 A3
source:   expected · INVARIANTS P2 ("an identifier that cannot be validated safely stops identifier-unsafe, and
          nothing is sent"); ARCH §6
note:     `$(` is sqlcmd's variable syntax, so the name cannot ride a templated probe unaltered. A name that validates
          with OBJECT_ID or COL_LENGTH is bracket-quoted and probed however odd (QST-98); an injection attempt
          validates as no real object and stops missing-object (60-privacy).
```

### CLI-43 · an unexpected failure mid-plant is internal
```case
kind:     process
setup:    M0 · a test hook makes sqlcmd exit 1 with no output while sql/plant.sql runs
action:   facsimile prove Dev; then facsimile down; then facsimile prove Dev with the hook off
expect:   outcome    stop internal · "Unexpected failure at plant (<type>). Nothing claims a verdict." · exit 9 · the
                     re-run: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          stdout     STOP internal · … · P/verdict.Dev.json · exit 9 (one line, no stack trace)
          files      no verdict claimed by the failed run; its copy registered, and removed by down
laws:     H1 H2 A3 S6
source:   expected · ARCH §6 (internal); INVARIANTS H1, H2
```

### CLI-44 · prove Dev QA: a stop outranks a block
```case
kind:     process
setup:    M0 · answers for Dev (Email NULL×2) · QA's answers measured before the last schema edit
action:   facsimile prove Dev QA
expect:   files      P/verdict.Dev.json (blocked guard) · P/verdict.QA.json (stop stale-answers) · P/verdicts.json
                     indexing both
          outcome    stop stale-answers · "answers.QA.json was measured for a different delta or head. Re-run
                     `facsimile read QA`." · exit 5
          stdout     STOP stale-answers · … · P/verdicts.json · exit 5
laws:     A1 F7 H1
source:   expected · ARCH §6 ("the line of its most severe outcome (stop > blocked > clean)")
```

## UP · the daily database (T3)

*Setup **U0**, unless a case says otherwise: the `pg` project on a machine with no Docker; `referenceData` seeds Status (3 rows) and
Category (2 rows); `up.rowsPerTable` 3; no fx_local yet. Each case restates a Twin acceptance (THE_TWIN §7) where one exists.*

### UP-01 · converge from nothing
```case
kind:     process
setup:    U0 · every environment server unreachable
action:   facsimile up
expect:   copy       fx_local: the plan of head against it is empty · Status 3 and Category 2 rows, from the lanes at
                     head · every other table 3 rows · up.sql's rows applied
          stdout     DONE up · converged · .facsimile/out/up.json · exit 0
          files      no docker process started · zero environment sessions
laws:     T3 D4
source:   expected · parity with TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an
          estate edit" (the first up); ARCH §8 (up: the repository schema, reference data, k presence rows, then up.sql)
```

### UP-02 · a second up does nothing
```case
kind:     process
setup:    U0 after UP-01
action:   facsimile up
expect:   stdout     DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          copy       every table's content hash unchanged
          files      no MSBuild build, no sqlpackage publish, no lane run (step trace)
laws:     D4 T3
source:   expected · parity with TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an estate edit" (the second up is NothingToApply)
note:     the Twin's no-op took about a second (THE_TWIN §7); the fast path must not build the project.
```

### UP-03 · rows a developer wrote survive a no-op up
```case
kind:     process
setup:    U0 after UP-01 · the developer inserts a Customer ('wAda', 'wada@example.invalid') into fx_local
action:   facsimile up
expect:   stdout     DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          copy       Customer 4 rows, wAda among them
laws:     D4 T3
source:   expected · INVARIANTS D4 ("a second up with unchanged inputs does nothing")
```

### UP-04 · a schema edit re-converges
```case
kind:     process
setup:    UP-03's machine · then Tables/Order.sql gains `Note nvarchar(50) NULL`
action:   facsimile up; facsimile up
expect:   stdout     run 1: DONE up · converged · .facsimile/out/up.json · exit 0
                     run 2: DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          copy       Order.Note present · every table outside the lanes back to 3 rows plus up.sql's (wAda replaced)
laws:     D4 T3
source:   expected · parity with TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an estate edit" (the estate edit, then a no-op)
note:     ARCH does not say whether up re-converges in place or re-plants; this pins the Twin's behaviour, where any
          input change re-mints the data. Rows meant to survive belong in up.sql (T3).
```

### UP-05 · a reference-data edit re-converges
```case
kind:     process
setup:    U0 after UP-01 · ReferenceData/Status.sql adds (4, 'Refunded')
action:   facsimile up; facsimile up
expect:   stdout     run 1: DONE up · converged · .facsimile/out/up.json · exit 0
                     run 2: DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          copy       Status 4 rows, Refunded among them
laws:     D4 T3
source:   expected · INVARIANTS D4 ("a reference-data edit followed by up re-converges")
```

### UP-06 · up --fresh re-plants, reproducibly
```case
kind:     process
setup:    UP-03's machine
action:   facsimile up --fresh; record every table's content hash; facsimile up --fresh
expect:   stdout     both runs: DONE up · re-planted · .facsimile/out/up.json · exit 0
          copy       wAda gone · the hashes after each --fresh equal each other and the first up's
laws:     T3 D1 D3
source:   expected · parity with THE_TWIN §7 (`twin seed`: force a fresh mint, reproducibly) and TwinMintLoopTests
          "T1: the same seed mints the same rows; the mint is reproducible"
```

### UP-07 · k valid rows per table
```case
kind:     process
setup:    U0 · the project adds FK_Order_Customer, FK_Order_Status, FK_OrderLine_Order, FK_Customer_Account (AccountId
          nullable) and a UNIQUE index on Product.Code
action:   facsimile up
expect:   copy       every table but Status and Category 3 rows · each Order.StatusId a lane Status row (no Status
                     planted) · each Order.CustomerId a planted Customer · Customer.AccountId NULL · Product.Code 3
                     distinct values that fit nvarchar(50) · string values carry the `w` marker where the width
                     allows · every constraint enabled and trusted as declared
laws:     T3 D3 P5
source:   expected · ARCH §2 rows 4 and 12, §5 step 2 (ancestors reuse reference rows; nullable FKs get NULL)
```

### UP-08 · views are published, not planted
```case
kind:     process
setup:    U0 · the project adds vCustomerOrders over Customer and Order
action:   facsimile up; facsimile up --fresh
expect:   copy       vCustomerOrders in sys.views and readable · no INSERT or DELETE aimed at it (step trace) · base
                     tables 3 rows each
          stdout     run 1: DONE up · converged · .facsimile/out/up.json · exit 0
                     run 2: DONE up · re-planted · .facsimile/out/up.json · exit 0
laws:     T3
source:   expected · parity with TwinViewLoopTests "A view in the estate: twin up succeeds, base tables mint, the view
          stays published and data-free"
```

### UP-09 · status reports freshness at every step
```case
kind:     process
setup:    U0
action:   status · up · status · edit Tables/Order.sql · status · up · status · edit ReferenceData/Status.sql · status ·
          run `ALTER TABLE dbo.Customer ADD Hack int NULL` by hand in fx_local · status
expect:   files      fx_local in each status.json, in order: absent · current · stale (schema) · current · stale
                     (referenceData) · stale (drift: fx_local differs from head)
          stdout     every status: DONE status · … · .facsimile/out/status.json · exit 0
laws:     T3 D4
source:   expected · parity with TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an estate edit" (status current, then stale after the estate edit)
```

### UP-10 · drift is repaired by up
```case
kind:     process
setup:    UP-09's last machine (fx_local holds Customer.Hack)
action:   facsimile up; facsimile status
expect:   stdout     DONE up · converged · .facsimile/out/up.json · exit 0
          copy       Customer.Hack gone; the plan of head against fx_local empty
          files      status.json: fx_local current
laws:     D4 T3
source:   expected · INVARIANTS D4; the Twin published with drop-not-in-source (THE_TWIN §6)
```

### UP-11 · deleting the caches costs time, not the developer's rows
```case
kind:     process
setup:    UP-03's machine · then .facsimile/ deleted
action:   facsimile up
expect:   stdout     DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          copy       wAda still present
laws:     D2 D4
source:   expected · INVARIANTS D2 ("deleting every cache changes run time and nothing else")
note:     currentness is re-derived (head's plan against fx_local empty, lane and up.sql hashes equal); were it kept only
          in the cached fingerprint, this up would re-plant and lose wAda.
```

### UP-12 · up, status and down need neither Docker nor an environment
```case
kind:     process
setup:    U0 on two machines: no Docker at all · Docker installed with its daemon stopped · every environment server
          unreachable on both
action:   facsimile up; facsimile status; facsimile down
expect:   outcome    the same on both machines: done up · converged · done status · done down
          files      no docker process · zero environment sessions · fx_local kept by down
laws:     T3 M3
source:   expected · INVARIANTS T3 ("all work without Docker"); ARCH §2 row 1
```

### UP-13 · the stops up can raise
```case
kind:     process
setup:    U0 after UP-01, with exactly one fault per row: (a) facsimile.json invalid · (b) no MSBuild · (c) LocalDB
          15.0 · (d) a T-SQL error in the project · (e) the project adds CHECK (Code LIKE 'P-[0-9][0-9][0-9][0-9]') on
          Product
action:   facsimile up, once per row
expect:   outcome    (a) stop config · exit 6 · (b) stop toolchain · exit 6 · (c) stop platform · exit 6 · (d) stop
                     build-failed · exit 7 · (e) stop planter-needs-row · "Cannot make a valid row for dbo.Product
                     (CK_Product_Code). Write one valid INSERT for dbo.Product into witness.sql (or up.sql) under
                     `-- plants: dbo.Product`." · exit 5
          stdout     STOP <kind> · <next step> · .facsimile/out/up.json · exit <n>
          copy       fx_local as UP-01 left it
laws:     A3 H3 H1
source:   expected · ARCH §6 (planter-needs-row names up.sql for `up`), §8 (up.sql)
```

### UP-14 · down keeps fx_local and its hand-written rows; down --all removes them
```case
kind:     process
setup:    UP-03's machine (fx_local holds wAda) · one registered proof copy
action:   facsimile down; facsimile up; facsimile down --all; facsimile up
expect:   outcome    done down · done up · Nothing to apply (wAda still there) · done down · done up · converged (from
                     nothing; wAda gone)
          files      after down: the proof copy gone, fx_local kept · after down --all: no fx_local, .facsimile/ caches
                     cleared
laws:     T3 S6 D4
source:   expected · INVARIANTS T3 ("down keeps fx_local and its hand-written rows, as twin down kept the container's
          data; down --all removes them, as twin reset did"); ARCH §2 row 22
```

### UP-15 · up takes the lanes at head and applies up.sql's pins
```case
kind:     process
setup:    U0 · the branch adds (4, 'Refunded') to ReferenceData/Status.sql, absent at the merge-base · up.sql pins one
          Order row for Customer wAda (`-- plants: dbo.Order`) and one Customer wAda (`-- plants: dbo.Customer`)
action:   facsimile up; then facsimile prove Dev
expect:   copy       fx_local: Status 4 rows (head's lane), Refunded among them · wAda and her Order present, in FK order
                     · the plan of head against fx_local empty · the proof copy (prove): Status as the merge-base lane
                     holds it, 3 rows
          stdout     DONE up · converged · .facsimile/out/up.json · exit 0
laws:     T3 F7
source:   expected · ARCH §2 rows 4 and 17 (lanes at the merge-base on every proof copy, at head for up; up.sql carries
          the pins), §8 (up)
note:     A proof copy must hold reference data as the environment holds it before this change, so the merge-base
          lane; `up` is the developer's head, so the head lane.
```

## CFG · facsimile.json (E4)

*`pure` cases call `Read-FxConfig` on the input; `process` cases need the files it names. **C0** is the ARCH §8 example with its `//`
comments removed.*

### CFG-01 · the example parses
```case
kind:     pure
input:    C0
action:   Read-FxConfig
expect:   outcome    a config: project Estate.sqlproj · options facsimile/options.publish.xml · sqlcmd {SomeVar} ·
                     referenceData [ReferenceData/*.sql] · localdb MSSQLLocalDB · Dev, QA and UAT readable · Prod
                     `read: none` · up.rowsPerTable 3
laws:     E4
source:   expected · ARCH §8; parity with TwinConfigTests "a minimal config parses with documented defaults"
```

### CFG-02 · the example as printed, comments included
```case
kind:     pure
input:    the ARCH §8 block verbatim, with its `//` comments
action:   Read-FxConfig under Windows PowerShell 5.1 and under PowerShell 7
expect:   outcome    the same result on both editions: CFG-01's config, or stop config naming the first comment's
                     line · exit 6
laws:     E4 D1
source:   verify · Windows PowerShell 5.1's ConvertFrom-Json rejects `//` comments where PowerShell 7 accepts them, so
          ARCH's own example may not parse on the laptop's default shell
note:     init (CLI-30) must write a file both editions parse.
```

### CFG-03 · an unknown key at each nesting level
```case
kind:     pure
input:    C0 plus one of: (a) `"projet": "x"` at the root · (b) `"serverr": "x"` in `environments.Dev` · (c) `"rows":
          5` in `up`
action:   Read-FxConfig, once per row
expect:   outcome    (a) stop config · "facsimile.json: $.projet: unknown key." · (b) stop config · "facsimile.json:
                     $.environments.Dev.serverr: unknown key." · (c) stop config · "facsimile.json: $.up.rows:
                     unknown key."; each exit 6
laws:     E4 H3
source:   expected · ARCH §8; parity with TwinConfigTests "closed schema: an unknown key refuses, named by its path" and
          "closed schema holds inside nested sections"
note:     `sqlcmd` and `environments` are maps keyed by names, open by design; their values are closed.
```

### CFG-04 · a key that differs only in case is unknown
```case
kind:     pure
input:    C0 with `"project"` spelled `"Project"`, and `environments.Dev.server` spelled `"Server"`
action:   Read-FxConfig
expect:   outcome    stop config listing `$.Project: unknown key` · `$.project: required` ·
                     `$.environments.Dev.Server: unknown key` · `$.environments.Dev.server: required` · exit 6
laws:     E4
source:   expected · ARCH §8 (closed schema)
note:     PowerShell's property lookup ignores case (`$cfg.project` reads `Project`), so the check must compare key
          names exactly.
```

### CFG-05 · wrong types, located
```case
kind:     pure
input:    C0 with `"referenceData": ["ReferenceData/*.sql", 7]`, `"up": {"rowsPerTable": "3"}` and
          `environments.Dev.server` set to `["dev-sql"]`
action:   Read-FxConfig
expect:   outcome    stop config listing `$.referenceData[1]: expected a string` · `$.up.rowsPerTable: expected an
                     integer` · `$.environments.Dev.server: expected a string` · exit 6
laws:     E4 H3
source:   expected · ARCH §8; parity with TwinConfigTests "an unknown rendition refuses with the expected tokens"
note:     PowerShell would coerce "3" to 3 without a word; the check must see the JSON type.
```

### CFG-06 · inline secrets are refused; env: and file: references are accepted
```case
kind:     pure
input:    C0 with one of: (a) `environments.Dev.server` "dev-sql;User Id=fx;Password=hunter2" · (b) `"sqlcmd":
          {"DbPassword": "hunter2"}` · (c) `"sqlcmd": {"DbPassword": "env:FX_DB_PASSWORD"}` · (d) `"sqlcmd":
          {"DbPassword": "env:"}` · (e) `environments.Dev.auth` "env:FX_DEV_AUTH" · (f) `environments.Dev.auth`
          "file:secrets/dev.txt" · (g) `environments.Dev.auth` "Password=hunter2"
action:   Read-FxConfig, once per row
expect:   outcome    (a) stop config · "facsimile.json: $.environments.Dev.server: inline secret; use an env: or file:
                     reference." · (b) the same for `$.sqlcmd.DbPassword` · (c), (e), (f) a config · (d) stop config ·
                     "facsimile.json: $.sqlcmd.DbPassword: env: names no variable." · (g) stop config naming
                     `$.environments.Dev.auth`; each stop exit 6; no message contains hunter2
laws:     E4 H3
source:   expected · ARCH §8 (`auth`: integrated, `env:VAR` or `file:path`; a secret only as an env: or file:
          reference); parity with TwinConfigTests "D9: an inline connection secret refuses at parse" and "D9: an
          inline container password refuses at parse"
note:     `auth` is the key that takes a secret; how a `sqlcmd` value (b) is recognized as one remains unspecified (gap
          reported). INVARIANTS E4's text names env: alone; ARCH §2 row 2 and §8 keep both forms.
```

### CFG-07 · an env: reference that is unset when the run needs it
```case
kind:     process
setup:    C0 with `"sqlcmd": {"DbPassword": "env:FX_DB_PASSWORD"}` · FX_DB_PASSWORD unset, then set to s3cret
action:   facsimile prove Dev; set the variable; facsimile prove Dev
expect:   outcome    run 1 stop config · "facsimile.json: $.sqlcmd.DbPassword: env:FX_DB_PASSWORD is not set." · exit 6
                     · run 2 a verdict
          files      s3cret in no file under P/ or .facsimile/; the verdict records env:FX_DB_PASSWORD
laws:     E4 A5
source:   expected · ARCH §8
```

### CFG-08 · every problem in one pass
```case
kind:     pure
input:    C0 with `"projet"` at the root, `"serverr"` in `environments.QA`, `$.sqlcmd.DbPassword` "hunter2",
          `$.up.rowsPerTable` "3", and `options` removed
action:   Read-FxConfig
expect:   outcome    one stop config · exit 6, listing all five paths; its next step names the first in document order
                     and says four more follow
laws:     E4 H3
source:   expected · INVARIANTS E4; parity with TwinConfigTests "errors aggregate: one pass reports every problem"
```

### CFG-09 · required keys
```case
kind:     pure
input:    (a) `{}` · (b) `{ "project": "Estate.sqlproj" }`
action:   Read-FxConfig, once per row
expect:   outcome    (a) stop config listing `$.project: required`, `$.options: required` and `$.environments:
                     required` · (b) stop config listing `$.options: required` and `$.environments: required` · each
                     exit 6
laws:     E4
source:   expected · ARCH §8 (project, options and environments are required); parity with TwinConfigTests "a missing
          estate section refuses with the expected shape"
```

### CFG-10 · environments: `read: none`, and names that become file names
```case
kind:     pure
input:    C0 with one of: (a) nothing changed · (b) `environments.Prod.read` "no" · (c) an environment named
          `Dev/../x` · (d) `"environments": {}`
action:   Read-FxConfig, once per row
expect:   outcome    (a) a config, Prod unreadable · (b) stop config · "facsimile.json: $.environments.Prod.read:
                     expected \"none\"." · exit 6 · (c) stop config · "facsimile.json: $.environments['Dev/../x']: an
                     environment name matches [A-Za-z][A-Za-z0-9_-]*." · exit 6 · (d) a config with no environment
laws:     E4 H3
source:   expected · ARCH §8 (environment names match `[A-Za-z][A-Za-z0-9_-]*`)
note:     environment names become `answers.<env>.json` and part of `fx_<slug>_<env>_<rand4>`, so a path or a space
          in one must never reach a file name.
```

### CFG-11 · file references that resolve to nothing
```case
kind:     process
setup:    C0, one fault per row: (a) `"project": "Estat.sqlproj"`, which does not exist · (b) `"referenceData":
          ["RefData/*.sql"]`, which matches no file
action:   facsimile doctor, once per row
expect:   outcome    (a) stop config · "facsimile.json: $.project: Estat.sqlproj not found." · exit 6 · (b) stop
                     config · "facsimile.json: $.referenceData[0]: matches no file." · exit 6
laws:     E4 H3
source:   expected · ARCH §8
note:     a silently empty lane would build copies with no reference data, and the stops that follow would look like
          the agent's fault.
```

### CFG-12 · the options file is missing
```case
kind:     process
setup:    C0 · facsimile/options.publish.xml absent
action:   facsimile doctor; facsimile read Dev; facsimile prove Dev
expect:   outcome    each stop options-unknown · "No usable captured effective options (missing). Capture the Octopus
                     step's options into the file `options` names." · exit 6
          stdout     STOP options-unknown · … · <the verb's file> · exit 6 (doctor: .facsimile/out/doctor.json; read:
                     P/read.Dev.json; prove: P/verdict.Dev.json)
laws:     F6 A3 H3
source:   expected · ARCH §6 (options-unknown)
```

### CFG-13 · a profile that turns the guard off is refused
```case
kind:     process
setup:    C0, options.publish.xml with BlockOnPossibleDataLoss, one row each: (a) False · (b) false · (c) absent ·
          (d) True
action:   facsimile doctor, once per row
expect:   outcome    (a), (b) stop options-unknown · "No usable captured effective options
                     (BlockOnPossibleDataLoss=False). …" · exit 6 · (c), (d) done doctor · ready · exit 0, since DacFx
                     defaults it to True
laws:     F6 A3
source:   expected · INVARIANTS F6; ARCH §6 (options-unknown names `BlockOnPossibleDataLoss=False`)
note:     In PowerShell `[bool]'False'` is $true, so a cast-based check passes row (a).
```

### CFG-14 · the profile's target and password are stripped
```case
kind:     process
setup:    C0 · options.publish.xml carries TargetConnectionString "Data Source=dev-sql;Initial Catalog=Estate;User
          ID=deploy;Password=p@ss" and TargetDatabaseName Estate
action:   facsimile prove Dev; facsimile up
expect:   files      every publish targets the run's fx_ copy or fx_local · zero connections to dev-sql · the recorded
                     options omit the target and the password · p@ss in no file
laws:     S1 F6
source:   expected · TWIN_SUCCESSION Appendix B (PublishProfiles.cs: load the pipeline profile, strip the target and
          password, flip one flag)
```

### CFG-15 · the captured SQLCMD values reach every publish, per environment
```case
kind:     process
setup:    Estate.sqlproj declares SqlCmdVariables SomeVar and Env · one row each: (a) `"sqlcmd": {"SomeVar": "value",
          "Env": "Dev"}` · (b) Env left out · (c) an extra `"Other": "x"` that the project does not declare · (d) (a)
          plus `environments.QA.sqlcmd` `{"Env": "QA"}`
action:   facsimile prove Dev, once per row; row (d) as facsimile prove Dev QA
expect:   outcome    (a) a verdict; every sqlpackage call carries exactly /v:SomeVar=value /v:Env=Dev, and the verdict
                     records both · (b) stop options-unknown naming Env · exit 6 · (c) stop config · "facsimile.json:
                     $.sqlcmd.Other: Estate.sqlproj declares no such variable." · exit 6 · (d) Dev's calls carry Env=Dev,
                     QA's Env=QA, each recorded in its own verdict
laws:     F6 E4 H7 F7
source:   expected · ARCH §8 (`sqlcmd`, overridden per environment by `environments.<env>.sqlcmd`); TWIN_SUCCESSION §10
          (the effective options include per-environment SQLCMD values)
```

### CFG-16 · malformed JSON
```case
kind:     pure
input:    (a) `{ not json` · (b) an empty file
action:   Read-FxConfig, once per row
expect:   outcome    stop config · "facsimile.json: $: not valid JSON (line 1)." · exit 6; no PowerShell parser text
                     reaches stdout
laws:     E4 H1
source:   expected · parity with TwinConfigTests "malformed JSON refuses with the syntax code"
```

### CFG-17 · duplicate keys
```case
kind:     pure
input:    `{ "project": "A.sqlproj", "project": "B.sqlproj", "options": "facsimile/options.publish.xml" }`
action:   Read-FxConfig under Windows PowerShell 5.1 and PowerShell 7
expect:   outcome    stop config · "facsimile.json: $.project: duplicate key." · exit 6, on both editions
laws:     E4
source:   verify · whether ConvertFrom-Json on 5.1 and on 7 reports a same-case duplicate or keeps the last value
          silently; if silently, Read-FxConfig must find it in the text
```

## CI · the corpus workflow and the PR gate

*Two surfaces: `facsimile-corpus.yml` in this repository (nightly, and on PRs that touch the facsimile), and the estate's PR gate, which
runs `facsimile prove` with no route to any environment (TWIN_SUCCESSION §8).*

### CI-01 · the corpus workflow's shape
```case
kind:     process
setup:    .github/workflows/facsimile-corpus.yml as merged
action:   run-cases.ps1 --lint (the workflow check)
expect:   files      triggers: a nightly schedule, and pull_request on the facsimile's paths · jobs: LocalDB on
                     windows-latest · SQL Server 2022 · ConstrainedLanguage · budgets · traceability lint · mutation
                     pass (nightly) · every job installs sqlpackage at the pin
laws:     E2 A2 M4 M1
source:   expected · ARCH §2 row 28, §9, §10 G2
```

### CI-02 · the LocalDB job
```case
kind:     process
setup:    a windows-latest hosted runner
action:   the LocalDB job: facsimile doctor, then run-cases.ps1 for every kind but manual
expect:   outcome    done doctor · ready with LocalDB 16 or later, installed by the job if the image lacks it · every pure,
                     sql, corpus and process case green · facsimile and oracle agree on every corpus case
laws:     F3 A3 S4
source:   verify · which LocalDB major the windows-latest image carries; a 15.x would stop `platform` on Sql160
```

### CI-03 · the SQL Server 2022 job
```case
kind:     process
setup:    a SQL Server 2022 instance configured as the copy server, not LocalDB
action:   run-cases.ps1 for every kind but manual
expect:   outcome    the same cases green · facsimile and oracle agree on every corpus case · LocalDB-only cases
                     (platform) reported as skipped by name, never silently
          files      every verdict stamps server 16.x and its edition
laws:     F3 H7
source:   expected · ARCH §2 row 25 (a configured server for CI), §10 G2
note:     facsimile.json has only `localdb`; the key that names a configured server is unspecified (gap reported).
```

### CI-04 · a differential disagreement fails, whatever the expectation says
```case
kind:     process
setup:    a corpus fixture whose `expect` line matches the facsimile, while a test hook withholds one witness so the
          oracle (head published onto a full copy of env) reaches a different verdict class
action:   run-cases.ps1 on that case
expect:   outcome    the case fails as a defect ("facsimile ≠ oracle"), not as a wrong expectation; the job is red
laws:     F3
source:   expected · ARCH §9.2; cases/README §3
```

### CI-05 · the PR gate proves from the committed proofs
```case
kind:     process
setup:    an estate PR with P/ committed (schema.Dev.dacpac, delta.Dev.sql, delta.pr.sql, answers.Dev.json,
          witness.sql, verdict.Dev.json and verdicts.json) · the build agent has no route to any environment server
action:   the PR gate: facsimile prove
expect:   outcome    the re-proved verdict equals the committed one, bar timestamps · a different verdict class is
                     reported on the PR, not failed · a blocked or refused-at-build verdict fails the gate
          files      zero environment sessions · the copy built from P/schema.Dev.dacpac
laws:     M3 D1 E3
source:   expected · TWIN_SUCCESSION §8 (PR pipeline steps 1–5); ARCH §2 row 30, §8 (schema.<env>.dacpac committed so
          CI reproduces the proof without access to the environment)
```

### CI-06 · which PRs need proofs
```case
kind:     process
setup:    PRs with no P/, each touching only: (a) Tables/Customer.sql · (b) the post-deploy script · (c) a file the
          post-deploy includes with `:r` · (d) Estate.refactorlog · (e) Estate.sqlproj · (f) README.md
action:   the PR gate
expect:   outcome    (a)–(e) refused, naming the missing proofs/<slug>/ · (f) passes
laws:     E3
source:   expected · INVARIANTS E3
```

### CI-07 · stale proofs are refused
```case
kind:     process
setup:    P/ committed for Dev and QA; a later commit widens Customer.Name to nvarchar(200) · a variant in which only
          QA's answers predate the edit
action:   the PR gate
expect:   outcome    refused, naming Dev and QA as stale (stop stale-answers: the CI-derived delta hash and the head
                     commit differ from the answers' stamps) · in the variant, naming QA alone
laws:     E3 H7
source:   expected · INVARIANTS E3 ("keyed to a different delta hash or head commit"); ARCH §6 (stale-answers)
```

### CI-08 · a hand-patched delta hash does not pass
```case
kind:     process
setup:    after a schema edit makes Customer.Region NOT NULL, the author copies the new delta's hash into
          answers.Dev.json without reading Dev again
action:   the PR gate
expect:   outcome    refused as stop stale-answers: the answers' head commit predates the edit, and the CI-derived delta
                     asks NotNull(Customer.Region), which no probe text in answers.Dev.json answers
laws:     E3 F4
source:   expected · INVARIANTS E3; ARCH §8 (answers are stamped with their head commit, delta hash and probe text)
```

### CI-09 · the gate finds proofs by the PR's branch
```case
kind:     process
setup:    an Azure DevOps PR from `feature/make-email-required`, checked out as its detached merge commit
action:   the PR gate
expect:   files      the gate reads proofs/<slug of feature/make-email-required>/, never a folder named after HEAD or
                     the merge ref
laws:     E3
source:   expected · ARCH §8 (`proofs/<branch-slug>/`)
note:     the slug rule (case, `/`, the `feature/` prefix) is unspecified (gap reported).
```

### CI-10 · break glass is offered only for tooling exits
```case
kind:     process
setup:    the gate's facsimile run ends, one row each, with exit 0 · 3 · 4 · 5 · 6 · 7 · 9
action:   the PR gate
expect:   outcome    0 green · 3 and 5 red, with no break glass offered (a finding; input needed) · 4, 6, 7 and 9 red
                     and marked "tooling", with the instruction that a lead may merge with "Not checked: tooling"
laws:     A3 E3
source:   expected · TWIN_SUCCESSION §8 (break glass for exits 4, 6, 7 and 9)
```

### CI-11 · a break-glass merge is recorded and logged
```case
kind:     manual
setup:    an Azure DevOps PR whose gate is red with exit 6 (toolchain on the build agent)
action:   the on-team principal or a dev lead overrides the branch policy and merges
expect:   record     notChecked {tooling}, with the exit and the stop kind
          files      the override logged with PR, exit, approver and date · an override by anyone else is refused by
                     branch policy
laws:     E3 H4
source:   expected · TWIN_SUCCESSION §8, §11.5 (break-glass approval sits with the on-team primary and a dev lead);
          INVARIANTS H4 (`tooling`: a break-glass merge)
note:     ARCH names no place for the log (gap reported).
```

### CI-12 · G4: both verdicts on every schema PR
```case
kind:     process
setup:    PRs in the G4 window: (a) `Twin: blocked guard` and `Facsimile: blocked guard` · (b) the facsimile line only
          · (c) two lines that disagree, with no defect link · (d) the same, with a defect link · (e) after G5, the
          facsimile line only
action:   the G4 PR lint
expect:   outcome    (a) passes · (b) refused: the Twin's verdict is missing · (c) refused: a disagreement needs a
                     defect · (d) passes the lint, unmergeable until the defect is resolved · (e) passes
laws:     T4
source:   expected · INVARIANTS T4; ARCH §10 G4
```

### CI-13 · a pin bump runs the corpus on the new engine
```case
kind:     process
setup:    a PR moving the sqlpackage pin from 170.5.96 to a later 170.x · a variant moving it down to 162.5.57
action:   the corpus workflow
expect:   outcome    every job installs the new pin and asserts `sqlpackage /Version` equals it before any case · the
                     merge waits for green · the verdicts it produces stamp the new version · a committed verdict
                     with no version stamp is invalid
laws:     E2 H7
source:   expected · INVARIANTS E2; estate/toolchain.md (a finding on one version does not carry to another)
```

### CI-14 · the ConstrainedLanguage job
```case
kind:     process
setup:    windows-latest · the job sets `__PSLockdownPolicy=4` at machine scope, then starts a new process
action:   print `$ExecutionContext.SessionState.LanguageMode`; then run-cases.ps1
expect:   outcome    the first step reads ConstrainedLanguage, or the job fails (it never passes vacuously) · every case
                     green · a mutant adding `Add-Type` to core.ps1 turns the job red
laws:     A2
source:   verify · whether `__PSLockdownPolicy` forces ConstrainedLanguage for Windows PowerShell 5.1 and for
          PowerShell 7 on the hosted image (it is a test hook, not a supported policy)
```

### CI-15 · the budgets check
```case
kind:     process
setup:    the tree as merged · mutants: facsimile.ps1 at 401 lines · core.ps1 at 381 · a 21st vocabulary row · an
          unlisted facsimile/helpers.ps1
action:   the budgets check
expect:   outcome    the tree passes; each mutant fails, naming the file, its lines (rows, for the vocabulary) and its
                     budget
laws:     M4
source:   expected · ARCH §3 (budgets: facsimile.ps1 400, core.ps1 380, vocabulary 20 rows), §9.5
```

### CI-16 · the traceability lint refuses each missing link
```case
kind:     process
setup:    the catalog as merged · one mutant per check: (a) a law left with one case · (b) a case with an empty
          `laws:` · (c) the KeyLength vocabulary row with no `corpus` case · (d) the base-unpublishable stop with no
          case · (e) Twin row 27 with no TWN case naming `row 27` · (f) make-mandatory without its `empty` case and
          without a `note: n/a empty — <reason>` excusing it
action:   run-cases.ps1 --lint
expect:   outcome    the catalog passes; each mutant is refused, naming the law, case, row, stop, Twin row or (op, tag)
                     that lacks its link
laws:     E1 T1 M1
source:   expected · ARCH §9.1; cases/README §1 (`note: n/a <tag> — <reason>`; a TWN case names `row <n>`)
```

### CI-17 · the lint refuses a malformed case
```case
kind:     process
setup:    mutants: one ID in two files · a field name at column 2 · `kind: smoke` · `source: receipt` with no
          citation · a `CLI-` case filed in 20-planter.md
action:   run-cases.ps1 --lint
expect:   outcome    each refused, naming the file and line
laws:     E1
source:   expected · cases/README §1 (the grammar), §4 (no receipt without a citation), §6 (the families)
```

### CI-18 · the mutation pass
```case
kind:     process
setup:    nightly · the INVARIANTS M1 mutant set: each vocabulary row, witness kind, stop kind, classifier rule and §5
          step
action:   the mutation pass
expect:   outcome    every mutant killed by at least one case; the report lists the killers per mutant; a survivor
                     fails the job, named
laws:     M1 E1
source:   expected · ARCH §9.4
```

## TLC · the toolchain and the machine

*Setup **M0** unless stated. The machine facts come from the laptop check (TWIN_SUCCESSION Appendix C) and from v3's lessons
(Appendix B).*

### TLC-01 · each required tool absent
```case
kind:     process
setup:    M0, one tool missing per row: (a) sqlpackage, on PATH and in every VS DAC folder · (b) SqlLocalDB ·
          (c) vswhere and MSBuild, with a classic .sqlproj · (d) both sqlcmd and go-sqlcmd
action:   facsimile doctor, once per row
expect:   outcome    stop toolchain · exit 6, naming the tool and how to get it: (a) "Request the pinned sqlpackage
                     zip through an Azure DevOps Artifacts feed." · (b) "Request the SqlLocalDB 2022 MSI (needs
                     admin)." · (c) the Visual Studio workload that carries MSBuild and SSDT · (d) the sqlcmd install
laws:     H3 A3
source:   expected · ARCH §6 (toolchain); TWIN_SUCCESSION Appendix C (how to read the output)
```

### TLC-02 · sqlpackage inside Visual Studio, and two engines on one machine
```case
kind:     process
setup:    rows: (a) no sqlpackage on PATH; VS 2022's `Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe`
          present · (b) sqlpackage 170.5.96 from the zip on PATH, and VS 2022's bundled copy as well
action:   facsimile doctor; facsimile prove Dev
expect:   outcome    (a) done doctor · ready, naming the bundled path found through vswhere · (b) doctor lists both and
                     names the one it uses
          files      every sqlpackage call in the run uses that one (step trace); the verdict stamps its version
laws:     H7 D1
source:   expected · TWIN_SUCCESSION Appendix C (the bundled sqlpackage under the VS DAC folder)
note:     the selection rule (PATH first, or the pin first) and where the facsimile reads its pin are unspecified (gap
          reported); a VS-bundled engine may be the 162.x family (estate/toolchain.md).
```

### TLC-03 · the engine version is stamped; a mismatch is a residual
```case
kind:     process
setup:    rows: (a) sqlpackage at the pin · (b) sqlpackage 162.5.57 against a 170.5.96 pin
action:   facsimile prove Dev
expect:   record     (a), (b) sqlpackageVersion equals `sqlpackage /Version` byte for byte · (b) notChecked includes
                     engine-version
laws:     H7 E2 H4
source:   expected · INVARIANTS H7, H4; FINDINGS_AND_CHANGES Part 5 and F19 (trust behaviour differs between 162.x and
          170.x)
```

### TLC-04 · LocalDB's major version against the project's target
```case
kind:     process
setup:    LocalDB 15 (2019), 16 (2022) or 17 (2025) × DSP Sql150, Sql160 or Sql170
action:   facsimile doctor, once per pair
expect:   outcome    done doctor · ready where LocalDB's major is at least the target's (Sql150 → 15, Sql160 → 16,
                     Sql170 → 17) · otherwise stop platform · "LocalDB <v> is older than the project's target <DSP>. …" ·
                     exit 6
laws:     S4 A3
source:   expected · INVARIANTS S4; TWIN_SUCCESSION Appendix C (2019 is 15.x, 2022 is 16.x, 2025 is 17.x)
```

### TLC-05 · allowOlderLocalDb
```case
kind:     process
setup:    LocalDB 15.0 · DSP Sql160 · `"allowOlderLocalDb": true` in facsimile.json
action:   facsimile prove Dev
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3, not a stop
          record     flags {older-localdb, …} · notChecked includes platform:older-localdb · the override
                     AllowIncompatiblePlatform=True recorded with the options, on copy publishes only
laws:     S4 F6 H4
source:   expected · INVARIANTS S4; ARCH §8 (allowOlderLocalDb: the one documented exception to F6); TWIN_SUCCESSION
          Appendix C (AllowIncompatiblePlatform=True on local copies only, stamped in every record)
```

### TLC-06 · missing SSDT build targets are toolchain, not the developer's build
```case
kind:     process
setup:    VS 2022 with MSBuild but without the SSDT component · a classic .sqlproj
action:   facsimile prove Dev
expect:   outcome    stop toolchain (MSB4019: Microsoft.Data.Tools.Schema.SqlTasks.targets not found) · exit 6, never
                     stop build-failed · exit 7
laws:     A3 H3
source:   expected · ARCH §6 (toolchain is the developer's machine; build-failed is the project)
```

### TLC-07 · a classic .sqlproj builds with Visual Studio's MSBuild
```case
kind:     process
setup:    the real estate's classic project · VS 2022 with SSDT; separately VS 2026
action:   facsimile prove Dev with no head cache
expect:   files      head.dacpac built by the MSBuild that vswhere finds; the build's duration on stderr and in
                     doctor.json
laws:     A3 D5
source:   verify · ARCH §11.4: whether the classic project builds with only VS's MSBuild on a team laptop, and
          how long it takes
```

### TLC-08 · an SDK-style .sqlproj
```case
kind:     process
setup:    proving-ground's SampleCatalog.sqlproj (Microsoft.Build.Sql 2.2.0) · rows: (a) a .NET SDK present · (b) no
          .NET SDK, VS's MSBuild only, NuGet through the network's proxy
action:   facsimile prove Dev
expect:   outcome    head.dacpac built in both rows, or row (b) stops toolchain naming the .NET SDK · exit 6; never
                     build-failed for a missing SDK
laws:     A3
source:   verify · whether VS's MSBuild resolves the Microsoft.Build.Sql SDK through the proxy without a .NET SDK
```

### TLC-09 · sqlcmd flavours
```case
kind:     process
setup:    rows: (a) the ODBC sqlcmd from VS or the SQL tools · (b) go-sqlcmd only · (c) both
action:   facsimile prove Dev
expect:   outcome    every probe, plant and hash script runs against `(localdb)\MSSQLLocalDB`; the verdict equals the
                     reference run's in every row
          files      doctor.json names the flavour in use
laws:     D1 H1
source:   verify · ARCH §11.5: whether VS ships sqlcmd, and whether go-sqlcmd reaches LocalDB with the same flags (-b,
          -V, -h -1, -W)
```

### TLC-10 · LocalDB's first connection times out, and is retried
```case
kind:     process
setup:    `sqllocaldb stop MSSQLLocalDB` before the run, so the first connection must start the instance · a variant
          in which the instance cannot start
action:   facsimile prove Dev
expect:   outcome    the verdict equals a warm run's; stderr notes the retry · in the variant, stop toolchain ·
                     "SqlLocalDB not responding (LocalDB gave no answer in 180 s after one retry). …" · exit 6, never a
                     hang
laws:     H1 D2
source:   expected · TWIN_SUCCESSION Appendix B ("LocalDB's first connection can time out, so retry"); ARCH §6
          (toolchain)
```

### TLC-11 · CREATE, RESTORE and DROP get 180 seconds
```case
kind:     process
setup:    a ~300-table base on LocalDB, the real estate's size · no base cache
action:   facsimile prove Dev; facsimile down
expect:   files      each CREATE DATABASE, RESTORE and DROP DATABASE carries an explicit 180 s timeout (step trace) ·
                     one that runs past it, after one retry, ends in stop toolchain (LocalDB not responding), not a hang
laws:     H1 S6
source:   verify · ARCH §11.6: how long LocalDB takes to CREATE, RESTORE and DROP a ~300-table schema; the 180 s is
          v3's (TWIN_SUCCESSION Appendix B) and ARCH §6's toolchain template
```

### TLC-12 · DROP with sessions open
```case
kind:     process
setup:    a registered proof copy held open by a developer's sqlcmd session and by a lingering sqlpackage connection
action:   facsimile down
expect:   files      `ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE` precedes every DROP · the drop
                     succeeds · no connection the tool opened outlives its step
          stdout     DONE down · 1 proof copy dropped · .facsimile/out/down.json · exit 0
laws:     S6
source:   expected · TWIN_SUCCESSION Appendix B (SINGLE_USER WITH ROLLBACK IMMEDIATE and ClearPool before DROP)
note:     ClearPool is an in-process SqlClient call with no ConstrainedLanguage equivalent; here every connection
          belongs to a child process, so no pool survives a step.
```

### TLC-13 · telemetry is off before any tool runs
```case
kind:     process
setup:    M0 · a process monitor recording each child process's environment
action:   facsimile doctor; facsimile prove Dev
expect:   files      DACFX_TELEMETRY_OPTOUT=1 in the environment of every sqlpackage call, doctor's first `/Version`
                     included · DOTNET_CLI_TELEMETRY_OPTOUT=1 for every dotnet call
laws:     P1
source:   expected · TWIN_SUCCESSION Appendix B ("set DACFX_TELEMETRY_OPTOUT before DacFx loads")
note:     telemetry is a path off the machine that may carry schema names; no law names it, so it rides on P1 (gap
          reported).
```

### TLC-14 · the culture does not reach the artifacts
```case
kind:     process
setup:    the same committed answers, proved under en-US, de-DE (decimal comma) and tr-TR (dotted and dotless i)
action:   facsimile prove Dev, once per culture
expect:   questions  identical in all three; under tr-TR the vocabulary still matches `WITH CHECK CHECK CONSTRAINT`
          files      P/verdict.Dev.json identical bar timestamps; numbers with `.`; times in ISO 8601 UTC
laws:     D1
source:   expected · TWIN_SUCCESSION Appendix B ("run under InvariantCulture")
note:     a case-insensitive match that is not culture-invariant folds `I` to `ı` under tr-TR, so `with check` can
          miss `WITH CHECK`.
```

### TLC-15 · encodings, in files and on stdout
```case
kind:     process
setup:    an estate with a column named Região and a CHECK whose text holds é · rows: Windows PowerShell 5.1 ·
          PowerShell 7 · console code page 437 · 65001
action:   facsimile prove Dev > out.txt
expect:   files      every file under P/ is UTF-8, with one BOM policy on both editions · Região round-trips through
                     the delta, the answers and the verdict
          stdout     the `·` separators arrive as U+00B7, or A1's grammar is made ASCII
laws:     D1 A1
source:   verify · 5.1's Set-Content (ANSI) and Out-File (UTF-16LE) defaults against 7's BOM-less UTF-8; whether 5.1
          under ConstrainedLanguage can write UTF-8 without a BOM; what code page 437 does to `·`
```

### TLC-16 · paths with spaces, and LocalDB's backslash
```case
kind:     process
setup:    the repository at `C:\Users\First Last\source\repos\Estate (copy)` · the instance `(localdb)\MSSQLLocalDB`
action:   facsimile doctor; facsimile prove Dev; facsimile up
expect:   outcome    M0's outcomes, unchanged
          files      every native call receives its arguments intact: sqlpackage /SourceFile and
                     /TargetServerName:(localdb)\MSSQLLocalDB, sqlcmd -S, the MSBuild project path
laws:     D1 H1
source:   expected · TWIN_SUCCESSION §4 (the `(localdb)\` escaping an agent would otherwise re-assemble every time)
note:     Windows PowerShell 5.1 passes embedded quotes to native programs inconsistently; this case catches it.
```

### TLC-17 · Windows PowerShell 5.1 and PowerShell 7
```case
kind:     process
setup:    M0 under `powershell.exe` 5.1 (the default in VS's Developer PowerShell) and under `pwsh` 7
action:   every process case, once per edition
expect:   outcome    identical outcomes; doctor.json records the edition
laws:     A2 D1
source:   verify · ARCH names no PowerShell edition or version floor; 5.1 lacks `ConvertFrom-Json -AsHashtable` and
          comment support, and the editions' ConvertTo-Json differ in escaping and indentation
```

### TLC-18 · execution policy
```case
kind:     manual
setup:    laptops matching Appendix C's rows: (a) Restricted as a local default, MachinePolicy and UserPolicy
          Undefined · (b) AllSigned pinned by Group Policy · (c) RemoteSigned, the repository cloned with git
action:   the laptop check (TWIN_SUCCESSION Appendix C), then `facsimile doctor` as PROVING.md says to run it
expect:   outcome    (a) runs as `powershell -ExecutionPolicy Bypass -File facsimile.ps1 doctor` · (b) blocked until
                     the script is signed with the org certificate (a ticket), and PROVING.md says so · (c) runs
laws:     A2
source:   expected · TWIN_SUCCESSION Appendix C (how to read the output)
```

### TLC-19 · AppLocker and WDAC
```case
kind:     manual
setup:    a laptop with AppLocker script rules enforced, or WDAC user mode at 2
action:   the laptop check, then every verb against the real estate's Dev
expect:   outcome    `Language (script): ConstrainedLanguage` · every verb's outcome as on a FullLanguage machine ·
                     nothing asks for an unsigned exe (`AppLocker exes: True` changes nothing)
laws:     A2
source:   expected · TWIN_SUCCESSION §4, Appendix C
```

### TLC-20 · in Visual Studio's Terminal, driven by Copilot
```case
kind:     manual
setup:    VS 2022's Terminal (Developer PowerShell), where Copilot runs its commands
action:   the laptop check; then Copilot runs `facsimile doctor` and `facsimile prove Dev`
expect:   outcome    the language mode and the outcomes match the console's · Copilot sees the one stdout line intact,
                     `·` included, and reads the exit code correctly
laws:     A1 A2 A3
source:   expected · TWIN_SUCCESSION §10 (run the check once more in Visual Studio's Terminal window), Appendix C
```

### TLC-21 · the reading budget
```case
kind:     process
setup:    the vendored knowledge layer: the router (.github/copilot-instructions.md), each op SKILL.md with its Proof
          plan, ssdt/RECORD.md, ssdt/PROVING.md
action:   the reading-budget check
expect:   outcome    for every op the router names: router + op skill (+ each file it says to read first) + record form
                     + PROVING.md ≤ 600 lines; the report lists each op's total; an op over budget fails, named
laws:     A4
source:   expected · INVARIANTS A4; TWIN_SUCCESSION §7 (router ≤ 80, PROVING ≤ 150, RECORD ≤ 120, op skill ≤ 60)
note:     80 + 60 + 120 + 150 = 410, which leaves 190 lines for read-first pointers.
```
