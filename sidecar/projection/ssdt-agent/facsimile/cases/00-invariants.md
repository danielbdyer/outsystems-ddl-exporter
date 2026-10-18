# 00 · Invariants (`INV-<law>-nn`)

*Property cases for the laws that other families touch only in passing: D1–D6, M1–M4, S1, S2, S5, S6, H1, H6, H7, E1–E4, A1–A5, T3, T4, F5, F6 and F8.
Each case states a property over a generated input set: `setup`, `input` or `env` names the generator, and `action` begins "for all".
Each `note` names the counter-example the property must catch, so that a vacuous mechanism, or a vacuous test of it, fails.*

## D · Determinism and statelessness

### INV-D1-01 · the same inputs give byte-identical artifacts
```case
kind:     process
setup:    generator: every `corpus` fixture × each environment it defines, with its answers and witness.sql
          committed; .facsimile/ emptied before each run
action:   for all fixtures X: facsimile prove in a fresh process, twice; compare every artifact
expect:   files      the witness plan, the planted rows (sql/hash.sql per table), delta.<env>.sql, delta.pr.sql,
                     verdict.<env>.json and verdicts.json byte-identical across the two runs, apart from timestamp fields
laws:     D1
source:   expected · INVARIANTS D1
note:     counter-example: plan items held in a `@{}` keyed by table name; PowerShell 7 seeds string hashing per
          process, so the plan's order, and the verdict's bytes, change from run to run.
```

### INV-D1-02 · input order and line endings never reorder the output
```case
kind:     pure
input:    generator: for every `pure` case's input, each permutation of key order in the answers JSON, of question
          order, and of environment order in the config, plus CRLF and LF variants of the delta text
action:   for all variants: Get-FxQuestions, Get-FxTier, Get-FxWitnessPlan and Get-FxRecord; ConvertTo-Json each
          result
expect:   plan       byte-identical across the variants of one input
          record     byte-identical across the variants of one input
laws:     D1
source:   expected · INVARIANTS D1
note:     counter-example: notChecked assembled in answers-file order (`{unread:QA, unread:Prod}` against `{unread:Prod, unread:QA}`), or a pattern
          ending in `\n` that misses every CRLF line of a Windows checkout.
```

### INV-D1-03 · nothing about the machine reaches the verdict
```case
kind:     process
setup:    generator: one corpus fixture × culture {en-US, de-DE, tr-TR} × checkout path {C:\src\Estate,
          C:\Users\First Last\source\Estate} × edition {Windows PowerShell 5.1, PowerShell 7}
action:   for all combinations: prove from the same committed answers; compare the verdicts
expect:   files      verdict.<env>.json identical apart from timestamps
laws:     D1
source:   verify · whether the two editions' ConvertTo-Json can be made to emit identical bytes (they differ in
          escaping and indentation), or the tool must pin one edition
note:     counter-example: an absolute checkout path written into the verdict, or `[string]` of a decimal under de-DE
          giving `0,5`.
```

### INV-D2-01 · a warm cache and no cache give the same verdict
```case
kind:     process
setup:    generator: every corpus fixture × cache state {base backups and head builds absent, warm from this fixture,
          warm from another fixture, warm with the base .bak deleted, warm with the head build deleted}
action:   for all pairs: prove; compare the verdicts
expect:   files      verdicts identical apart from timestamps; only the run time differs
laws:     D2
source:   expected · INVARIANTS D2
note:     counter-example: a `.facsimile/known-questions.json` left by an earlier run that lets a later run skip the
          unknown-statement stop; deleting it changes the outcome.
```

### INV-D2-02 · no state object in any database the tool creates
```case
kind:     sql
env:      generator: every database a full corpus run creates (each fx_base_<hash12>_<rand4>, each proof copy), and
          fx_local after `up`
action:   for all databases D: list D's objects and database-level extended properties absent from
          schema.<env>.dacpac (from head's model, for fx_local)
expect:   copy       none · `up`'s fingerprint lives only under .facsimile/
laws:     D2 F2
source:   expected · ARCH §2 rows 6 and 34 (no state object in any database)
note:     counter-example: the Twin's `[twin].[__state]` carried into fx_local, or a "made by fx" extended property on
          the database (an F2 identity difference as well).
```

### INV-D2-03 · the committed schema is the one input prove cannot rebuild
```case
kind:     process
setup:    generator: every corpus fixture after `read`, with every environment server stopped, then one of:
          proofs/<slug>/schema.<env>.dacpac deleted · every .facsimile/ cache deleted
action:   for all fixtures and both deletions: facsimile prove <env>
expect:   outcome    schema deleted: stop answers-invalid · "answers.<env>.json does not validate
                     (proofs/<slug>/schema.<env>.dacpac: missing) … Re-run `facsimile read <env>`." · exit 5 · never a
                     verdict
                     caches deleted: the verdict of a warm run, the base rebuilt from the committed schema
          files      zero environment sessions
laws:     D2 M3 H2
source:   expected · INVARIANTS D2, M3; ARCH §8 (schema.<env>.dacpac is committed so CI reproduces the proof without
          access to the environment)
note:     counter-example: prove re-extracting quietly (breaks M3), or proving against the merge-base build instead (a
          schema the environment does not have; breaks F7). ARCH §6 has no template that names a missing schema;
          answers-invalid, whose next step is the right one, is the nearest.
```

### INV-D3-01 · one column changed, every other column's planted values unchanged
```case
kind:     sql
env:      generator: for every `pg` table T and column c, schema pairs (S, S′) where S′ differs from S only by: c
          removed · a new column inserted before c · c retyped (nvarchar→varchar, int→bigint) · c narrowed · c
          widened
action:   for all pairs: plant presence with k = 3 for T under S and under S′; hash each column's values
expect:   copy       every column but c (and the inserted one) holds byte-identical values under S and S′
laws:     D3
source:   expected · INVARIANTS D3; ARCH §2 row 19 (S-stable, kept)
note:     counter-example: literals built from the column's ordinal position (`w<table>_<position>_<row>`), so
          inserting one column shifts the values of every column after it.
```

### INV-D3-02 · planted values ignore planting order
```case
kind:     sql
env:      generator: `pg` plus FK_Order_Customer, FK_Order_Status, FK_OrderLine_Order and
          FK_CustomerAddress_Customer; every permutation of table planting order and of ancestor discovery order
action:   for all permutations: plant the same plan into a fresh restore; hash every table
expect:   copy       identical table hashes under every permutation
laws:     D3 D1
source:   expected · INVARIANTS D3 (a planted value is a function of table, column, ordinal, type and width)
note:     counter-example: one counter shared across tables (`w0001`, `w0002`, …), so planting Customer before Order
          changes the values of both.
```

### INV-D4-01 · after convergence nothing to apply; after any input change, re-convergence
```case
kind:     process
setup:    generator: edit sequences of length ≤ 4 over {none, add a nullable column, edit a referenceData row, an
          up.sql row, up.rowsPerTable 3→5, one option value, the referenceData glob list}
action:   for all sequences: after each edit, run `facsimile up` twice
expect:   stdout     the first up after a real edit: DONE up · converged · .facsimile/out/up.json · exit 0
                     every second up: DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
laws:     D4 T3
source:   expected · INVARIANTS D4
note:     counter-example: a fingerprint over the project's table files only, so a Status row edit (or an up.sql pin)
          reports "Nothing to apply" over stale data.
```

### INV-D4-02 · the converged state does not depend on the path taken
```case
kind:     process
setup:    generator: pairs of edit sequences that end at the same inputs (add a column then remove it, against no
          edit; add a Status row then change its Code, against adding the changed row)
action:   for all pairs: `up` along each path, then `up --fresh` at the final inputs on a clean machine
expect:   copy       the plan of head against fx_local empty on every path · each table's content hash equal across
                     both paths and --fresh
laws:     D4 D1
source:   expected · INVARIANTS D4
note:     counter-example: an in-place re-convergence that leaves a removed column's default constraint, or a deleted
          lane row, behind.
```

### INV-D5-01 · each base-cache input, changed alone, misses
```case
kind:     process
setup:    generator: from a warm base for fixture X, exactly one change of: an index name in schema.<env>.dacpac ·
          one merge-base referenceData file's content (same path) · the order of the referenceData globs · one value
          in options.publish.xml · one `sqlcmd` value · the tool version · the sqlpackage version · the LocalDB version
action:   for all changes: prove
expect:   copy       a new base backup under a new hash12, built through a fresh fx_base_<hash12>_<rand4>, for every
                     change; the old backup untouched
laws:     D5
source:   expected · INVARIANTS D5; ARCH §5 step 1 (key = schema, merge-base referenceData, effective options, tool,
          DacFx and LocalDB versions)
note:     counter-example: a key over the options file's path rather than its content, so an option edit restores the
          old base.
```

### INV-D5-02 · the head build misses on any project input
```case
kind:     process
setup:    generator: exactly one change of: a table's .sql · the pre-deploy · the post-deploy · a file the post-deploy
          includes with `:r` · the refactorlog · a SqlCmdVariable in the .sqlproj
action:   for all changes: prove
expect:   files      head rebuilt every time; the delta and its questions reflect the change (the post-deploy edit
                     raises authored-script)
laws:     D5
source:   expected · INVARIANTS D5
note:     counter-example: a head key over `Tables/**/*.sql` only, so a post-deploy edit proves the stale head and the
          Authored question never appears.
```

### INV-D5-03 · the engine version is a cache input
```case
kind:     process
setup:    generator: every corpus fixture with a base built by sqlpackage 162.5.57, then the pin moved to 170.5.96,
          and back
action:   for all fixtures: prove
expect:   copy       the base rebuilt under the new engine
          files      the verdict stamps the new version
laws:     D5 E2 H7
source:   expected · INVARIANTS D5, E2; FINDINGS_AND_CHANGES Part 5 and F19 (trust state differs between 162.x and
          170.x)
note:     counter-example: a 162-built base restored after the bump, its trust states carried into a 170.x proof.
```

### INV-D6-01 · every clean corpus case, published again, is silent
```case
kind:     corpus
env:      generator: every corpus fixture whose facsimile outcome is verdict clean
change:   the fixture's own change, published a second time onto the same copy (ARCH §5 step 11)
expect:   outcome    verdict clean · exit 0
          record     silence ok: the second plan empty, rows affected 0, the hash of every touched and reference table
                     unchanged
laws:     D6 D1
source:   expected · INVARIANTS D6; FINDINGS F12 (a guarded seed MERGE over unchanged rows touches 0 rows and keeps
          its content hash)
note:     counter-example: a silence check that compares only the plan, so a post-deploy MERGE that rewrites every
          row on each publish passes.
```

### INV-D6-02 · an unguarded MERGE, an R1 replay and a same-value rewrite are each not-silent
```case
kind:     corpus
env:      pg · Status 3 rows (the lane) · Customer 4 rows · Product 3 rows
change:   one per row: (a) the post-deploy Status MERGE written without its `AND (target.Code <> source.Code …)` guard ·
          (b) a two-release R1 (its pre-deploy drops Product.LegacyCode while the model lags), proved with
          `--program R1 R2`, so R1 is replayed · (c) a post-deploy `UPDATE dbo.Customer SET Name = Name`
expect:   outcome    verdict clean · exit 0 in every row
          record     (a) silence not-silent(rows affected 3 on the replay; hashes unchanged)
                     (b) silence not-silent(the R1 replay's plan re-creates Product.LegacyCode from its default): the F17
                     hazard
                     (c) silence not-silent(rows affected 4 on the replay; hashes unchanged)
laws:     D6 F8 H2
source:   expected · INVARIANTS D6 property; F12 (written unguarded, the seed MERGE touches 3 rows: the churn
          anti-pattern); F17 (a replay of contract-R1 published "successfully" and re-created the column from its
          default)
note:     (a) and (c) change no value, so the hash alone cannot see them; rows affected does. Silence never changes
          the verdict; it is a record field.
```

### INV-D6-03 · silence is judged only after a clean publish, from all three signals
```case
kind:     pure
input:    generator: every verdict class and every stop × silence evidence {replay plan empty or not, rows affected 0
          or n, every touched and reference hash equal or one differing}
action:   for all pairs: Get-FxRecord
expect:   record     clean: silence ok exactly when all three signals hold, otherwise silence not-silent(<each signal that
                     failed, with its evidence>) · any block or stop: no silence field
          outcome    the verdict or stop given, unchanged by the silence evidence
laws:     D6 H1
source:   expected · INVARIANTS D6; ARCH §5 step 11 ("after a clean publish: check silence")
note:     counter-example: not-silent raised as a stop, which would refuse the R1 of every two-release program; or a
          record that names only the first failed signal.
```

## M · Minimality

### INV-M1-01 · every removable item kills a case
```case
kind:     process
setup:    generator: the mutants = each vocabulary row · each witness kind in sql/plant.sql · each stop kind · each
          classifier rule in Get-FxOutcome · each §5 step (restore, presence, violations, trust state, witness.sql,
          hash, accept, identity, publish, residue, permissive, silence, program)
action:   for all mutants: delete the item, run the suite, restore the item
expect:   outcome    every mutant fails at least one case; the report names the killing cases per mutant
laws:     M1 E1
source:   expected · INVARIANTS M1; ARCH §9.4
note:     counter-example: a vocabulary row shadowed by an earlier, broader one (both match `ALTER COLUMN … NOT NULL`);
          deleting it fails nothing, so the pass must call it dead.
```

### INV-M1-02 · the mutation pass is not vacuous
```case
kind:     process
setup:    generator: the tree plus seeded dead items: a vocabulary row matching `SELECT 1 FROM [x].[never]`, a stop
          kind no code path raises, and a planter procedure no plan calls
action:   for all seeded items: the mutation pass
expect:   outcome    all three reported as surviving mutants; the job fails, naming them
laws:     M1
source:   expected · ARCH §9.4
note:     counter-example: a pass that counts "the suite ran" rather than "a case's outcome changed", or one that runs
          only `pure` cases, so a dead planter procedure survives.
```

### INV-M2-01 · planted rows equal the resolved plan, the maximum of each table's needs
```case
kind:     sql
env:      generator: every (kind × class × populated) cell of ARCH §9.3 over `pg` plus FK_Order_Customer (NOT NULL)
          and FK_Customer_Account (nullable), one environment per cell holding exactly that answer, plus pairs of
          cells on one table (two violating columns; a violation beside a dups class)
action:   for all cells: Get-FxWitnessPlan, then plant; count generated rows per table in the copy
expect:   copy       generated rows per table = the count plant.sql's resolved plan reports · for each populated touched
                     table, the maximum of its needs, not their sum: 1 row for presence alone, or for presence with
                     violations one row can carry; 2 for a dups or nulls class or a gapped identity · plus NOT NULL FK
                     ancestors (reusing reference rows) and, for every Orphan question, the linked parent · none in an
                     untouched table
laws:     M2
source:   expected · INVARIANTS M2; ARCH §5 steps 2–3
note:     counter-example: a presence row planted beside the violation row (the sum), so a Customer with one NULL Email
          gets two rows; or a new Status ancestor planted though the merge-base lane already holds Status rows.
```

### INV-M2-02 · every generated row is load-bearing
```case
kind:     sql
env:      generator: every INV-M2-01 cell with at least two generated rows
action:   for all generated rows r: delete r from the copy, then re-run Test-FxAcceptance
expect:   outcome    every deletion gives stop acceptance, or is refused by a foreign key; no generated row can go with
                     F1 intact · rows declared in witness.sql are counted apart and reported by line
laws:     M2 F1
source:   expected · INVARIANTS M2 ("removing any generated row breaks F1")
note:     counter-example: an Account row planted for the nullable Customer.AccountId, against ARCH §5 step 2; deleting
          it leaves every answer unchanged.
```

### INV-M3-01 · read makes exactly two environment sessions
```case
kind:     process
setup:    generator: every corpus fixture's stand-in environment, with an Extended Events session recording the
          read-only login's sessions
action:   for all fixtures: facsimile read <env>
expect:   files      two sessions: one from sqlpackage (Extract), one from sqlcmd carrying every probe in one batch
laws:     M3 P2
source:   verify · whether sqlpackage's Extract opens one session or several; if several, M3 is restated as two tool
          invocations
note:     counter-example: one sqlcmd call per question (N sessions), or a separate session for the sys.partitions
          count.
```

### INV-M3-02 · no other verb opens an environment session
```case
kind:     process
setup:    generator: every corpus fixture after `read`, with every environment server stopped and its name
          unresolvable
action:   for all fixtures: prove · status · up · down · doctor (without its reachability check)
expect:   outcome    each verb's outcome as when the environment is up
          files      zero connection attempts to any environment server (trace, DNS log)
laws:     M3 P4
source:   expected · INVARIANTS M3; ARCH §8 (which verbs touch the environment)
note:     counter-example: `status` judging answer staleness by scripting head against the live environment.
```

### INV-M4-01 · every file within its budget
```case
kind:     process
setup:    generator: the ARCH §3 table; for each file, a mutant padded to its budget + 1
action:   for all mutants, and for the tree: the budgets check
expect:   outcome    the tree passes; each mutant fails, naming the file, its lines and its budget
laws:     M4
source:   expected · INVARIANTS M4; ARCH §3
note:     counter-example: a check on the total alone (≈1,600), which lets facsimile.ps1 reach 600 lines while sql/
          shrinks.
```

### INV-M4-02 · no code outside the budget table
```case
kind:     process
setup:    generator: mutants adding facsimile/helpers.ps1 (dot-sourced by facsimile.ps1) · sql/extra.sql · a 21st
          vocabulary row · one vocabulary row holding two patterns
action:   for all mutants: the budgets check
expect:   outcome    each refused: an unlisted code file outside tests/ and cases/; the vocabulary counted in rows (budget
                     20) and patterns, not lines
laws:     M4 M1
source:   expected · ARCH §3, §9.5
note:     counter-example: logic moved into an unlisted file, or vocabulary.psd1 measured in lines, so one long row
          carries two statement shapes.
```

## S · Safety of the copy

### INV-S1-01 · the write guard refuses every target the tool did not make
```case
kind:     process
setup:    generator: target names {Estate, master, tempdb, model, msdb, Estate_Local, FX_Reports (pre-existing),
          fx_local outside `up`, fx_x_Dev_1 (well formed, not registered this run), `fx_x];DROP DATABASE [Estate`, the
          empty name}, each fed to every write path (publish, restore, plant, drop) through a test hook
action:   for all (name, path) pairs: attempt the write
expect:   outcome    stop config before any write connection · exit 6
          files      the trace shows no CREATE, RESTORE, DROP, INSERT or publish against the name
laws:     S1
source:   expected · INVARIANTS S1
note:     counter-example: a guard of `-like 'fx_*'`, case-blind and blind to the run's registry, which publishes over
          (and `down` drops) a person's FX_Reports.
```

### INV-S1-02 · the captured profile's target is never used
```case
kind:     process
setup:    generator: options.publish.xml variants carrying a TargetConnectionString to dev-sql/Estate, a
          TargetDatabaseName of Estate, and a Password= in the connection string × verbs {prove, prove --permissive,
          up}
action:   for all pairs: run, tracing every connection
expect:   files      every publish and script targets the run's fx_ copy or fx_local · zero connections to dev-sql ·
                     the recorded options carry neither target nor password
laws:     S1 F6 P4
source:   expected · TWIN_SUCCESSION Appendix B (strip the target and password from the pipeline profile)
note:     counter-example: `sqlpackage /Action:Publish /Profile:options.publish.xml` with no /TargetServerName, which
          publishes to the profile's own target.
```

### INV-S2-01 · the step trace never publishes the base after a plant
```case
kind:     process
setup:    generator: every corpus fixture × {base cache miss, hit} × {Strict, --permissive, --program}
action:   for all runs: prove with the step trace on
expect:   files      every copy's steps read restore · plant · trust state · witness · hash · accept · identity · publish
                     head · …; a publish of schema.<env>.dacpac appears only into an fx_base_<hash12>_<rand4>, before its
                     BACKUP
laws:     S2 S5
source:   expected · INVARIANTS S2; ARCH §5 ("the base is never re-published after planting")
note:     counter-example: the Permissive arm re-publishing schema.<env>.dacpac over the planted copy instead of
          restoring it.
```

### INV-S2-02 · why: re-publishing the base after planting loses witnesses
```case
kind:     sql
env:      generator: every corpus fixture whose copy holds a planted violation, with its referenceData lanes
action:   for all fixtures: re-publish schema.<env>.dacpac onto the planted copy under the effective options (a test
          hook), then re-run Test-FxAcceptance
expect:   outcome    stop acceptance on every fixture where the re-publish changed a planted row; INV-S2-01's trace
                     refuses the pipeline that did it
laws:     S2 F1
source:   expected · red-team #9 recorded the erasure on its own fixture (TWIN_SUCCESSION Appendix A); which of these
          fixtures flip is not yet witnessed
note:     counter-example: a pipeline that "refreshes" the base after planting and still reports clean.
```

### INV-S5-01 · residue from one proof never reaches the next
```case
kind:     process
setup:    generator: ordered pairs (A, B) of corpus fixtures over one environment schema, where A ends blocked with
          residue (a 1505 unique build leaves the index dropped and the column altered)
action:   for all pairs: prove A, then B, with no `down` between; separately prove B alone on a fresh instance
expect:   files      verdict(B after A) = verdict(B alone), bar timestamps
laws:     S5 D2
source:   expected · INVARIANTS S5; the residue A leaves is H6's (red-team #4)
note:     counter-example: copies named `fx_<slug>_<env>` with no random part and reused when present, so A's
          half-applied schema is where B starts.
```

### INV-S5-02 · each environment and each arm gets its own restore
```case
kind:     process
setup:    generator: fixtures with two environments whose answers differ × {Strict, --permissive}
action:   for all fixtures: prove Dev QA (with --permissive in half the runs) with the step trace on
expect:   copy       one RESTORE per (environment, arm) · no copy planted twice · none of Dev's witnesses in QA's copy
                     (sql/hash.sql per touched table)
laws:     S5 F7
source:   expected · INVARIANTS S5, F7
note:     counter-example: QA planted on top of Dev's copy to save a restore, the answers union of red-team #5.
```

### INV-S6-01 · a kill at any step leaves only reapable things
```case
kind:     process
setup:    generator: the step trace of a prove (base create, base publish, referenceData, BACKUP, RESTORE, plant, trust
          state, witness, hash, accept, identity, publish head, residue script, permissive restore, silence) and of an
          up; Stop-Process -Force after each step
action:   for all kill points: list the instance's databases and the files under LocalDB's data folder and the
          repository; run facsimile down
expect:   files      before down: only databases in the tool's registry and .facsimile/ files are new · after down: zero
                     registered proof copies, their .mdf and .ldf gone · fx_local kept · every database absent from the
                     registry untouched
laws:     S6 S1
source:   expected · INVARIANTS S6
note:     counter-example: a BACKUP written to LocalDB's default backup folder, outside .facsimile/ and so never cleared
          by `down --all`; or a scratch database created before it is registered, so a kill in that gap orphans it.
```

### INV-S6-02 · down drops proof copies that are busy or half-made
```case
kind:     process
setup:    generator: registered proof copies in each state {idle, held by an open sqlcmd session, RESTORING after a
          killed restore, SINGLE_USER held by another session, OFFLINE}
action:   for all states: facsimile down, twice
expect:   files      zero registered proof copies after the first run · fx_local untouched
          stdout     both runs: DONE down · <n> proof copies dropped · .facsimile/out/down.json · exit 0
laws:     S6
source:   expected · INVARIANTS S6; TWIN_SUCCESSION Appendix B (SINGLE_USER WITH ROLLBACK IMMEDIATE before DROP)
note:     counter-example: a DROP without SINGLE_USER WITH ROLLBACK IMMEDIATE, which waits forever on the open session;
          a RESTORING database cannot be set SINGLE_USER and must be dropped directly.
```

### INV-S6-03 · down and down --all drop only what the registry lists
```case
kind:     process
setup:    generator: instance states holding, in any combination: registered proof copies · fx_local with a
          hand-written row · a person's fx_mine and FX_Reports · a well-formed fx_x_Dev_ab12 made by another clone of
          the repository (absent from this registry)
action:   for all states: facsimile down; then facsimile down --all
expect:   files      after down: every registered proof copy gone · fx_local, its hand-written row and the caches kept ·
                     after down --all: fx_local gone and .facsimile/ caches cleared · fx_mine, FX_Reports and
                     fx_x_Dev_ab12 untouched by both
          stdout     DONE down · … · .facsimile/out/down.json · exit 0, for both
laws:     S6 S1 T3
source:   expected · INVARIANTS S6 ("a database not in the registry is never dropped, whatever its name"), T3; ARCH §8
          (down, down --all)
note:     counter-example: a `down` that drops `fx_%` by name, which takes a colleague's copy and a person's fx_mine.
```

## H · Honesty

### INV-H1-01 · every invocation: one outcome, one line, one file
```case
kind:     process
setup:    generator: every `process` and `corpus` case in the catalog, with its own setup and action
action:   for all cases: run; capture stdout, the exit code and the files written
expect:   stdout     exactly one line, parsing as `VERDICT …` for `prove`, or `STOP <kind> …`, or `DONE <verb> …` for a
                     verb that does not prove
          files      the named file exists and its `outcome` equals the line
          exit       equal to the line's `exit n`
laws:     H1 A1 A3
source:   expected · INVARIANTS H1; ARCH §6
note:     counter-example: a `WARNING:` line before the outcome (two lines), or exit 0 with a stop in the file after a
          caught exception.
```

### INV-H1-02 · an interrupted run leaves no verdict claim
```case
kind:     process
setup:    generator: the kill points of INV-S6-01 × {Ctrl+C, Stop-Process -Force, LocalDB stopped mid-publish} × {an
          earlier verdict.Dev.json present, absent}
action:   for all combinations: interrupt `facsimile prove Dev` at that point
expect:   outcome    Ctrl+C: stop interrupted · "The run was interrupted at <step>. `facsimile down` removes its copies;
                     re-run." · exit 9 · LocalDB stopped: a stop (toolchain, or unclassified when the publish output
                     is all it has) · Stop-Process -Force: no outcome at all
          files      verdict.Dev.json absent, byte-identical to the file before the run, or holding the stop; never a
                     verdict from the interrupted run · no temporary or truncated JSON under proofs/
          stdout     never a VERDICT line
laws:     H1 S6
source:   expected · INVARIANTS H1 ("killing the process mid-run leaves no file claiming a verdict"); ARCH §6
          (interrupted)
note:     counter-example: `outcome: clean` written as soon as the publish succeeds, before §5 step 11's silence
          check has finished.
```

### INV-H1-03 · an unexpected failure is still a stop
```case
kind:     process
setup:    generator: one injected fault at each shell boundary: a native tool exits non-zero with output no rule
          knows, during the publish and outside it · a tool prints nothing · an unreadable answers file · a full disk
          when the verdict is written
action:   for all faults: run the verb that crosses that boundary
expect:   outcome    the stop a rule names where one does (a publish with output no rule knows, or none: stop
                     unclassified · exit 9; an unreadable answers file: stop answers-invalid · exit 5); every other fault:
                     stop internal · "Unexpected failure at <step> (<type>). Nothing claims a verdict." · exit 9
          stdout     one STOP line; never a PowerShell error record or stack trace; never exit 1, which is usage's alone
laws:     H1 H2 A3
source:   expected · INVARIANTS H1, H2; ARCH §6 (internal)
note:     counter-example: an uncaught `throw` ending in exit 1 and a red ErrorRecord.
```

### INV-H7-01 · every verdict carries every stamp
```case
kind:     process
setup:    generator: every verdict a full corpus run produces, including `assumed`, --permissive, --program and
          witness-free verdicts
action:   for all verdicts: validate against the stamp schema
expect:   files      non-empty: tool version · sqlpackage/DacFx version · LocalDB (or server) version and edition · head
                     commit and merge-base commit · the server and database the answers were read from · hashes of the
                     effective options, schema.<env>.dacpac, the answers' content, witness.sql, delta.<env>.sql and
                     delta.pr.sql · the answers' login and measuredAt
laws:     H7
source:   expected · INVARIANTS H7
note:     counter-example: the witness hash left out when there is no witness.sql, so "no witnesses" and "stamp missing"
          look alike; it must be the hash of a declared empty value.
```

### INV-H7-02 · each stamped input moves the identity hash; the login and the time do not
```case
kind:     process
setup:    generator: from one verdict, exactly one change of: an option value · an index name in the schema · one
          count in the answers · a byte of witness.sql · whitespace in delta.<env>.sql · a line of delta.pr.sql · the
          head commit · the merge-base commit · a byte of core.ps1 · the sqlpackage version · the LocalDB version; then,
          separately, only the answers' measuredAt, or only their login
action:   for all changes: prove again
expect:   files      the identity hash differs for every input change · equal when only measuredAt or only the login
                     changed, and when nothing changed
laws:     H7 D1 F7
source:   expected · INVARIANTS H7 ("the login and the time are stamps but not inputs")
note:     counter-example: an answers hash over the whole file, so a re-read of unchanged data (a new measuredAt) breaks
          F7's proof sharing; or a tool version read from a hand-bumped constant, so an edited core.ps1 keeps the old
          identity.
```

### INV-H6-01 · constraint validation runs after the post-deploy, on the pinned engine
```case
kind:     sql
env:      pg · Category 2 rows (the lane) · Product 2 rows, one with CategoryId 9
action:   publish head (adds FK_Product_Category; its post-deploy seeds Category (9, N'wTools', 1) and PRINTs a marker)
          onto a restored copy under the effective options; read the publish output and sys.foreign_keys
expect:   trace      the marker PRINT precedes "Checking existing data against newly created constraints" · the
                     WITH CHECK CHECK batch runs outside the transacted portion
          copy       FK_Product_Category present and trusted (is_not_trusted = 0); the publish succeeded
laws:     H6 E2
source:   verify · the order was observed 2026-10-18 on sqlpackage 170.5.96 (ARCH §4, §5 step 10); this case pins it per
          engine version and is re-run on every pin bump
note:     If validation ran before the post-deploy, the publish would block Msg 547 on FK_Product_Category, and ARCH
          §4's seeded-parent rule (a seeded lookup counts) and H6's phase order would both be revised with that receipt.
```

## E · Evolution

### INV-E1-01 · no rule without its case
```case
kind:     process
setup:    generator: every vocabulary row, every Get-FxOutcome message rule and every stop kind; plus mutants, each
          adding one rule with no case (a classifier for Msg 1934, a vocabulary row `Phantom`)
action:   for all mutants, and for the tree: run-cases.ps1 --lint
expect:   outcome    the tree passes; each mutant refused, naming the rule that has no case
laws:     E1 M1
source:   expected · INVARIANTS E1; ARCH §9.1
note:     counter-example: a lint that checks vocabulary rows only, so a classifier rule ships without a case.
```

### INV-E1-02 · a new statement shape enters only as a row with its case
```case
kind:     process
setup:    generator: every DacFx statement shape scanned from the corpus deltas, each vocabulary row's matches by
          shape, plus one synthetic row-reading shape that no row lists
action:   for all shapes: the corpus scan
expect:   outcome    every shape a row matches appears in at least one of that row's corpus cases · the synthetic shape
                     gives stop unknown-statement · exit 9
laws:     E1 F4
source:   expected · INVARIANTS E1, F4
note:     counter-example: a row's pattern widened to `ALTER TABLE .*`, which takes in a shape no case proves.
```

### INV-E2-01 · a pin bump runs the corpus on the new version
```case
kind:     process
setup:    generator: PRs that move the sqlpackage pin up or down, on a runner whose tool cache holds the old binary
action:   for all PRs: the corpus workflow
expect:   outcome    each job asserts `sqlpackage /Version` equals the new pin before any case · the merge waits for
                     green · every verdict produced stamps the new version
laws:     E2 H7
source:   expected · INVARIANTS E2
note:     counter-example: a tool cache keyed without the version, so the corpus runs green on the old binary.
```

### INV-E2-02 · a verdict without its version, or off the pin
```case
kind:     process
setup:    generator: committed verdicts whose sqlpackageVersion is missing · empty · equal to the pin · different from
          the pin
action:   for all verdicts: the PR gate's proof check
expect:   outcome    missing or empty: invalid, refused · different: notChecked must include engine-version · equal:
                     valid
laws:     E2 H4 H7
source:   expected · INVARIANTS E2 ("a verdict without a version stamp is invalid"), H4
note:     counter-example: a comparison of major versions only (170 = 170), where the pin is a full build number.
```

### INV-E3-01 · a schema PR needs fresh, complete proofs for every readable environment
```case
kind:     process
setup:    generator: PR diffs touching {a table, a view, the pre-deploy, the post-deploy, a `:r` include, the
          refactorlog, the .sqlproj, docs only} × proofs {absent, fresh, Dev only, predating the last schema edit,
          lacking schema.<env>.dacpac, lacking delta.pr.sql}
action:   for all pairs: the PR gate
expect:   outcome    accepted iff no schema file is touched, or proofs are present, complete and fresh for every readable
                     environment; each refusal names the environment and the reason (missing, incomplete or stale)
laws:     E3
source:   expected · INVARIANTS E3; ARCH §8 (what one change commits)
note:     counter-example: "schema PR" detected by `Tables/**` alone, so a post-deploy change merges without proofs.
```

### INV-E3-02 · staleness and server identity are computed, not declared
```case
kind:     process
setup:    generator: proofs whose answers' deltaHash was hand-edited to the new delta's hash · proofs whose head commit
          predates the last schema edit · answers naming a server other than the configured environment's · proofs
          that are honestly fresh
action:   for all cases: the PR gate
expect:   outcome    stale (the CI-derived delta hash or head commit differs from the answers' stamps, or the CI-derived
                     delta asks a question no probe text in the answers answers): refused as stop stale-answers · a
                     server mismatch: refused as stop answers-invalid · the honest proofs pass
laws:     E3 H7
source:   expected · INVARIANTS E3; ARCH §6 (stale-answers, answers-invalid), §8 (answers stamped with server,
          database, login, time, head commit, delta hash and probe text)
note:     counter-example: staleness judged by commit order alone, or by trusting the deltaHash field; both pass a
          hand-patched file.
```

### INV-E4-01 · an unknown key is refused at every object node
```case
kind:     pure
input:    generator: the ARCH §8 config without comments; for every object node p ($, each $.environments.<env>,
          $.up), insert `zz`, then separately a case variant of a key already at p (`Project`, `Server`,
          `RowsPerTable`)
action:   for all variants: Read-FxConfig
expect:   outcome    stop config naming exactly `<p>.zz` (or the case variant) · exit 6
laws:     E4
source:   expected · INVARIANTS E4
note:     counter-example: validation at the root only, or PowerShell's case-blind property lookup accepting `Project`
          as `project`.
```

### INV-E4-02 · every fault in one pass, and only those
```case
kind:     pure
input:    generator: every non-empty subset of five independent faults: `$.up.rows` unknown · `$.sqlcmd.DbPassword`
          inline · `$.up.rowsPerTable` "3" · `$.project` missing · `$.environments.Prod.read` "no"
action:   for all subsets: Read-FxConfig
expect:   outcome    one stop config · exit 6, whose file lists exactly the subset's paths
laws:     E4 H3
source:   expected · INVARIANTS E4 ("all problems are reported in one pass")
note:     counter-example: a fail-fast parser that reports only the first fault, so five faults take five runs.
```

### INV-E4-03 · a secret only as an env: or file: reference, and never echoed
```case
kind:     pure
input:    generator: every string leaf × values {`env:FX_X`, `env:`, `env:1bad`, `file:secrets/dev.txt`, `file:`,
          `Password=hunter2`, `Server=x;User Id=sa;Pwd=hunter2`}
action:   for all pairs: Read-FxConfig
expect:   outcome    accepted only for `env:FX_X` and `file:secrets/dev.txt` (and for plain values that are not
                     secrets); every refusal is stop config naming its path · exit 6; no message, and no file, contains
                     hunter2
laws:     E4 H3
source:   expected · ARCH §2 row 2 and §8 (the Twin's D9: `env:` and `file:` references); INVARIANTS E4's text names
          `env:` alone
note:     counter-example: a refusal that quotes the value ("'hunter2' is an inline secret"), which writes the secret
          into the stop file.
```

## A · Agent ergonomics

### INV-A1-01 · stdout's grammar, for every case
```case
kind:     process
setup:    generator: every `process` and `corpus` case × {`powershell.exe -File`, `pwsh -File`}, stdout redirected to a
          file
action:   for all runs: read stdout's bytes
expect:   stdout     one line matching `^(VERDICT (clean|blocked [a-z]+|refused-at-build)|STOP [a-z-]+|DONE [a-z]+)
                     · .+ · \S+ · exit (0|1|3|4|5|6|7|9)$`, ending in one newline; the path is relative to the
                     repository and exists; a multi-environment prove names verdicts.json
laws:     A1
source:   expected · INVARIANTS A1; ARCH §6
note:     counter-example: sqlpackage's or MSBuild's own output inherited by stdout from a native call that was not
          captured, adding hundreds of lines.
```

### INV-A1-02 · the named file is the whole truth
```case
kind:     process
setup:    generator: every verdict and every stop the catalog produces
action:   for all outcomes: discard stderr; rebuild the outcome from stdout and the named file alone
expect:   files      everything ARCH §6 says a stop prints (its next step, every placeholder filled) and every record
                     field are in the file; stderr held nothing the file lacks
laws:     A1 H3
source:   expected · INVARIANTS A1, H3
note:     counter-example: the next step printed only to stderr among the progress, leaving a bare kind in the file.
```

### INV-A2-01 · ConstrainedLanguage changes no outcome
```case
kind:     process
setup:    generator: the whole suite × language mode {FullLanguage, ConstrainedLanguage via `__PSLockdownPolicy`}
action:   for all cases: run in both modes
expect:   outcome    identical outcomes, files and stdout per case; the ConstrainedLanguage run first asserts its mode
laws:     A2
source:   expected · INVARIANTS A2
note:     counter-example: `[System.IO.File]::WriteAllText(…)` to write BOM-less UTF-8, legal in FullLanguage and refused
          under ConstrainedLanguage ("method invocation is supported only on core types").
```

### INV-A2-02 · no ConstrainedLanguage-forbidden construct in the source
```case
kind:     process
setup:    generator: every .ps1 under facsimile/, plus mutants each inserting one of: `Add-Type` ·
          `New-Object -ComObject` · `[System.Data.SqlClient.SqlConnection]::new()` ·
          `[Reflection.Assembly]::LoadFrom(…)` · a `class` definition
action:   for all mutants, and for the tree: the ConstrainedLanguage lint
expect:   outcome    the tree passes; each mutant refused, naming file and line
laws:     A2
source:   expected · TWIN_SUCCESSION §4 (native exes, cmdlets, [xml], [regex], ConvertTo-Json, no SqlClient objects);
          Microsoft about_Language_Modes
note:     counter-example: a scan that misses a PowerShell `class`, which ConstrainedLanguage blocks, or a helper
          dot-sourced from outside facsimile/.
```

### INV-A3-01 · each stop kind exits exactly as ARCH §6 says
```case
kind:     process
setup:    generator: one fixture per stop kind and per verdict class (the CLI and A5 cases in 70-process.md supply
          them)
action:   for all fixtures: run; read the process exit code
expect:   exit       usage → 1 · unreachable, no-access, probe-failed → 4 · missing-object, stale-answers,
                     answers-invalid, authored-script, check-witness, computed-witness, planter-needs-row,
                     witness-failed, witness-not-synthetic, acceptance, identity, in-flight → 5 · config, toolchain,
                     platform, options-unknown → 6 · build-failed → 7 · unknown-statement, unclassified, cdc,
                     base-unpublishable, identifier-unsafe, interrupted, internal → 9 · clean → 0 · every blocked, and
                     refused-at-build → 3
laws:     A3
source:   expected · INVARIANTS A3; ARCH §6
note:     counter-example: acceptance mapped to 9 ("a principal should look"), so the agent never tries the fix it can
          make.
```

### INV-A3-02 · the exit code survives every documented invocation
```case
kind:     process
setup:    generator: invocation forms {`powershell.exe -File`, `pwsh -File`, `& .\facsimile.ps1` then `$LASTEXITCODE`,
          `powershell.exe -ExecutionPolicy Bypass -File`, `powershell.exe -Command "& .\facsimile.ps1 …"`} × outcomes
          with exits 0, 1, 3, 5, 6 and 9
action:   for all pairs: run; read the exit code the caller sees
expect:   exit       equal to the line's `exit n` for every form PROVING.md documents
laws:     A3 A1
source:   verify · how each form passes a script's `exit n` to its caller (about_PowerShell_exe describes -File and
          -Command differently)
note:     counter-example: PROVING.md suggesting `-Command`, where a script's `exit 3` can reach the caller as 1, which
          is usage's exit.
```

### INV-A4-01 · every op is provable within 600 lines
```case
kind:     process
setup:    generator: every op the router names × the files an agent must read: the router, the op's SKILL.md with its
          Proof plan, each file that skill says to read first, the record form, PROVING.md
action:   for all ops: the reading-budget check
expect:   outcome    every op's total ≤ 600 lines; the report lists them
laws:     A4
source:   expected · INVARIANTS A4
note:     counter-example: a check that counts SKILL.md alone while the skill says "read _index/tightening-class first",
          another 150 lines.
```

### INV-A4-02 · every routed op has exactly one Proof plan
```case
kind:     process
setup:    generator: every op in skills/op/ and in the router; mutants removing a `## Proof plan`, moving it to a
          sibling file, or routing one op to two skills
action:   for all mutants: the reading-budget check
expect:   outcome    each mutant refused, naming the op
laws:     A4
source:   expected · INVARIANTS A4; ARCH §10 (every op skill gains a `## Proof plan`)
note:     counter-example: a Proof plan in a sibling file the check never opens, so the budget passes on a skill the
          agent cannot use alone.
```

### INV-A5-01 · following the instruction moves the outcome
```case
kind:     process
setup:    generator: every stop kind whose next step is the agent's (exit 1 or 5) × every catalog case that raises it;
          the fix is a scripted application of the printed next step and nothing else
action:   for all pairs: run; apply the fix; run again
expect:   outcome    the second outcome differs from the first: the stop clears, moves to another stop, or reaches a
                     verdict
laws:     A5 H3
source:   expected · INVARIANTS A5
note:     counter-example: acceptance raised for a copy-side extra the planter made; "Fix witness.sql" cannot move it,
          so the stop must name what can.
```

### INV-A5-02 · chains of stops end, without revisiting
```case
kind:     process
setup:    generator: from every agent-actionable starting stop, the chain of outcomes under literal fixes, capped at 10
          runs
action:   for all starts: follow the chain
expect:   outcome    every chain reaches a verdict, or a stop that is not the agent's (exit 4, 6, 7 or 9), within 10
                     runs; no (kind, object) pair repeats
laws:     A5
source:   expected · INVARIANTS A5
note:     counter-example: a planter-needs-row message that omits its reason, so the agent's row fails the same CHECK
          and the same stop comes back.
```

## T · Twin parity

### INV-T3-01 · each kept Twin verb's acceptance, restated
```case
kind:     process
setup:    generator: THE_TWIN §7's verbs mapped to the facsimile's (up → up, seed → up --fresh, status → status, down →
          down, reset → down --all, init → init), run as TwinSchemaLoopTests' sequence (up, up, status, edit, status,
          up, up) on `pg` with a view, then up --fresh twice, then a hand-written row, down, up, and down --all
action:   for all steps of the sequence: run it with `facsimile`
expect:   outcome    done up · converged · done up · Nothing to apply · done status: current · done status: stale (schema)
                     · done up · converged, the new column present · done up · Nothing to apply · the view published
                     and data-free · the two --fresh runs give equal content hashes · after down and up, fx_local and
                     the hand-written row still there · after down --all, no fx_local
laws:     T3 D4
source:   expected · parity with TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an
          estate edit" and TwinViewLoopTests "A view in the estate: twin up succeeds, base tables mint, the view stays
          published and data-free"
note:     counter-example: up --fresh planting random suffixes, which loses `twin seed`'s "reproducibly"; or a `down`
          that drops fx_local, which `twin down` never did to the container's data.
```

### INV-T3-02 · no verb needs Docker
```case
kind:     process
setup:    generator: every T3 verb × machines {no Docker binary, Docker present with its daemon stopped, DOCKER_HOST
          pointing nowhere}
action:   for all pairs: run the verb
expect:   outcome    the outcomes of a machine that never had Docker
          files      no docker process spawned
laws:     T3
source:   expected · INVARIANTS T3 ("all work without Docker"); ARCH §2 row 1
note:     counter-example: a leftover `docker inspect` probe in doctor or status (the Twin's `ensureRunning`).
```

### INV-T4-01 · every G4 schema PR carries both verdicts
```case
kind:     process
setup:    generator: PR bodies in the G4 window × {both lines, facsimile only, Twin only, neither} × {schema PR, not}
action:   for all bodies: the G4 PR lint
expect:   outcome    a schema PR with fewer than both lines refused · a PR that is not a schema PR exempt · after G5,
                     the Twin's line no longer required
laws:     T4
source:   expected · INVARIANTS T4
note:     counter-example: a lint that looks for `verdict` anywhere in the body, so one line passes.
```

### INV-T4-02 · a disagreement is a defect, never an average
```case
kind:     process
setup:    generator: every pair (Twin verdict class, facsimile verdict class), equal and unequal
action:   for all pairs: the G4 PR lint and the merge check
expect:   outcome    an unequal pair needs a linked defect and stays unmergeable until the defect is resolved; the
                     record states both classes verbatim · an equal pair needs nothing
laws:     T4
source:   expected · INVARIANTS T4 ("investigated and resolved, never averaged")
note:     counter-example: a merged line ("blocked (Twin: clean)"), or a disagreement merged with no defect.
```

## F · Fidelity

### INV-F5-01 · the offline delta equals the live one, for every fixture
```case
kind:     sql
env:      generator: every corpus fixture's stand-in environment, including F9's states (an untrusted FK, a disabled
          FK, a system-named default, a non-default column collation)
action:   for all fixtures: Script(head, Extract(env)) and Script(head, env) under the effective options; normalize the
          header, `:setvar` lines and the database name; compare
expect:   outcome    identical text for every fixture
laws:     F5
source:   expected · the receipt covers one change only (TWIN_SUCCESSION §0.1: identical on SQL Server 2022 with
          sqlpackage 170.5.96, 2026-10-15); ARCH §11.1 (trust state through Extract) is still open
note:     counter-example: an Extract that loses `WITH NOCHECK` trust, so the live delta has `WITH CHECK CHECK
          CONSTRAINT` and the offline one does not.
```

### INV-F5-02 · the normalization hides only what it names
```case
kind:     pure
input:    generator: pairs of delta texts that differ in exactly one line, by class: the header comment · a `:setvar`
          line · the database name · a guard batch · a DROP · an ALTER COLUMN · a GO separator · an object whose name
          equals the database's
action:   for all pairs: the runner's F5 normalizer, then compare
expect:   outcome    equal after normalization for the first three classes only; every other difference survives
laws:     F5
source:   expected · INVARIANTS F5 ("ignoring the header, :setvar lines and the database name")
note:     counter-example: replacing the database name everywhere, which also rewrites a table named `[Estate]` and
          hides a real difference.
```

### INV-F6-01 · the recorded option set is the captured set
```case
kind:     process
setup:    generator: options files varying non-guard options (DropObjectsNotInSource, IgnoreColumnOrder,
          GenerateSmartDefaults, ScriptDatabaseOptions, IncludeTransactionalScripts) × `sqlcmd` value sets × {Strict,
          --permissive} × allowOlderLocalDb {false, true}
action:   for all combinations: prove; read the option set in the verdict and the sqlpackage command lines in the step
          trace
expect:   record     Strict's set = the captured set, normalized · Permissive's = the captured set with
                     BlockOnPossibleDataLoss=False and nothing else · no `/p:` on a command line that the capture lacks,
                     except AllowIncompatiblePlatform=True on copy publishes when allowOlderLocalDb is set, recorded with
                     the options and flagged older-localdb
laws:     F6 S4
source:   expected · INVARIANTS F6; ARCH §8 (allowOlderLocalDb, the one documented exception to F6)
note:     counter-example: the shell adding `/p:AllowIncompatiblePlatform=True` or `/p:CreateNewDatabase=True` without
          recording it.
```

### INV-F6-02 · a disabled guard is refused in every spelling, by every verb
```case
kind:     process
setup:    generator: options files with BlockOnPossibleDataLoss in {False, false, FALSE, " False "} against {True, true,
          absent} × verbs {doctor, read, prove}
action:   for all pairs: run the verb
expect:   outcome    every False spelling: stop options-unknown · exit 6 · True or absent: no options stop
laws:     F6 A3
source:   expected · INVARIANTS F6 ("a profile that disables BlockOnPossibleDataLoss is refused")
note:     counter-example: a check through `[bool]$value`, under which the string 'False' is $true.
```

### INV-F8-01 · the questions come from the final target
```case
kind:     corpus
env:      generator: every two-release program in the corpus (make-mandatory, narrow, delete-attribute) over an
          environment that holds R2's violating shape
change:   R1 = head with an empty model diff, plus its pre-deploy · R2 = the final target · `prove --program R1 R2`
expect:   questions  derived from R2's head, plus R1's authored ones
          witnesses  planted before R1 is published
          outcome    R2's verdict equals the oracle's (R1, R1 again, then R2, published onto a full copy of env) · never
                     clean from an empty copy
          record     one result per release, on one copy
laws:     F8 F3
source:   expected · INVARIANTS F8; red-team #2 (a two-release R1 has an empty model diff, so nothing is planted)
note:     counter-example: questions taken from R1's own delta, which is empty, so nothing is planted and both releases
          come out clean on an empty copy.
```

### INV-F8-02 · an R1 replay with a non-empty plan is the F17 hazard
```case
kind:     corpus
env:      generator: every program whose R1 is a contract release (the column dropped while the model lags)
change:   prove R1, replay R1, then R2, on one copy (`prove --program R1 R2`)
expect:   outcome    the verdict the oracle reaches for the same sequence on a full copy of env; the replay itself
                     publishes clean
          record     silence not-silent(the replay's plan re-creates the column, backfilled from its default): the F17
                     hazard, never a silent clean
laws:     F8 H2 D6
source:   expected · the engine behaviour is F17's receipt (a replay of contract-R1 reported `Successfully published
          database` and re-created the column from its default; DB PG_mol_x1); the tool's classification is not yet
          witnessed
note:     counter-example: a replay judged only by its publish message, which reads success.
```
