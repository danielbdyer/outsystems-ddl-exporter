# The Facsimile: architecture

*The successor to the Twin. Written 2026-10-18. It is specified test-first: the invariants in
`INVARIANTS.md` and the cases under `cases/` come before any implementation, and the implementation
is judged against them. The reasoning that led here is `../../../../TWIN_SUCCESSION.md` at the
repository root (§0.1 and §6b in particular).*

---

## 0. The essence

**A facsimile agrees with an environment on every question the publish engine will ask of its data,
and on nothing else.**

When DacFx publishes a change, it asks the data two kinds of question:

- **"Is there a row?"** This is the `BlockOnPossibleDataLoss` guard,
  `IF EXISTS (select top 1 1 from T) RAISERROR(…,16,127)`.
- **"Is there a row that breaks this rule?"** This is constraint validation, unique-index builds,
  and type conversion.

Both are existence questions, so they have a finite, enumerable set of answers. The facsimile
therefore never tries to *resemble* an environment's data. It reads the environment twice, counts
the answers, and plants **one synthetic row for every "yes"** in a LocalDB copy of the
environment's own schema. Then it lets DacFx publish the change and reads what happened.

The line between what the facsimile proves and what it cannot is mechanical:

- **Statements DacFx generates** ask closed questions, and the facsimile answers them.
- **Statements a person authored** (pre- and post-deploy scripts, seeds, the first release of a
  two-release change) carry meaning. The facsimile stops and asks someone to *look* in the real
  environment, using the value-free shape profile. What they see is declared back as witnesses.

The Twin tried to resemble the data. The facsimile only needs to agree with it.

## 1. What "ultra-powered minimum-viable code" means here

**Minimum viable code** is the least code for which every invariant in `INVARIANTS.md` has a
mechanism and every case in `cases/` passes. "Least" is enforced, not hoped for:

- every line must be necessary to at least one case (the necessity law M1, checked by deletion);
- every file has a line budget (M4).

The whole edition is budgeted at **about 1,600 lines**, PowerShell and T-SQL together. That is
roughly 1% of what the Twin stood on (about 5,000 lines of F# over a 114,000-line kernel).

**Ultra-powered** means the power comes from leverage, not from lines:

1. **Microsoft writes the engine.** DacFx diffs, orders, guards and publishes. SQL Server validates.
   The facsimile only arranges for them to meet the right rows. It reimplements nothing either of
   them does.
2. **The spec is the test.** Every case in `cases/` is written in a fixed grammar that a ~150-line
   runner executes directly, so there is no translation step where spec and test can drift.
3. **Reality is the oracle.** Integration cases are *differential*. The facsimile's verdict on a
   stand-in environment must equal the verdict of publishing the same change onto a full copy of
   that environment. Correctness is adjudicated by the engine itself, not only by a hand-written
   expectation.
4. **The answer space is finite, so it is enumerated, not sampled.** Each question kind has a closed
   set of value classes, and the corpus covers every (question kind × value class × populated or
   empty) cell.
5. **Rules are data.** The vocabulary (statement → question → probe → witness) is a table. Growing
   the facsimile usually means adding a row plus the case that proves it, not writing code.
6. **There is no state to keep true.** Every run is re-derived from the repository and the
   environment. Caches are keyed by content and can be deleted at any time.

## 2. What the Twin does today, and what becomes of each part

This is surveyed from `src/Twin.*`, both Twin test pools, `THE_TWIN.md`, the skill tree, the
scripts and the workflows. Every row is backed by a case in `cases/80-twin-parity.md`: either a
**parity** case (the facsimile does it) or a **retirement** case (no verdict depends on it).

| # | Twin responsibility | Disposition | The facsimile's form |
|---:|---|---|---|
| 1 | Docker container lifecycle (`ensureRunning`, ports, `sa`) | replaced | LocalDB instance; `doctor` |
| 2 | `twin.json` closed config: unknown key refused by path; no inline secrets (D9, `env:` and `file:` references); located refusals | **kept** | `facsimile.json`, same laws (E4), both reference forms |
| 3 | Estate globs (tables, schemas) | replaced | the project file, built by MSBuild or `dotnet build` |
| 4 | Static lanes applied verbatim | **kept** | `referenceData` lanes: the merge-base version on every proof copy, the head version for `up` |
| 5 | DacFx publish of the estate | **kept** | sqlpackage, with the effective pipeline options |
| 6 | Fingerprint convergence ("Nothing to apply") | **kept** for `up` | D4 |
| 7 | Clean slate (null deferred FKs, child-first wipe, reseed) | replaced | every proof restores a fresh base cache (S5); nothing is wiped |
| 8 | ReadSide read-back to a catalog, plus the identity ACL (coordinates, no SsKey) | retired | the planter reads `sys.*` in the copy; coordinates are plain `schema.table.column` |
| 9 | Coordinate totality (law 2): every coordinate resolves or refuses | **kept** | every question, answer and witness coordinate resolves against the environment's schema, or stops (S3) |
| 10 | Views excluded from the mint, but still published | **kept** | the planter skips views (PLT cases) |
| 11 | σ mint: distribution-faithful synthesis | retired | ordinal presence rows. No verdict depends on distribution (F20; witness report) |
| 12 | Schema-derived floor (name routing: email-shaped and so on) | retired | ordinal literals fitted to width; `up` data is plain but valid |
| 13 | Post-mint trust gate (every constraint ends trusted) | **inverted** | the copy reproduces the *environment's* trust state (F9); forcing trust erased real states |
| 14 | Evidence tiers: import, derive, verify (shape and rich) | retired | `answers.<env>.json` (counts) plus the `shape` verb (discovery) |
| 15 | Corrections and Faker realization; `twin classify` (PII) | retired | no real value ever reaches the copy (P1, P5), so there is nothing to mask |
| 16 | Scenarios: weights, `between` windows, `perParent` | retired | none: a distribution never flips a verdict |
| 17 | Scenario pins (exact rows) | **kept** | `witness.sql` (proofs) and `up.sql` (the daily database), declared DML rows |
| 18 | `twin check` (π∘σ≈id, byte-identical re-mint, zero orphans) | replaced | acceptance re-measure (F1), schema identity (F2), determinism (D1), and before/after hashes of every touched table (the Twin's value-preservation digests, kept) |
| 19 | S-stable (a schema edit re-mints only the touched columns) | **kept** | ordinal-addressed literals (D3) |
| 20 | T1 determinism | **kept** | D1 |
| 21 | `twin bake` (image) and `bake.mjs` (.bacpac artifact) | retired | content-keyed base caches; nothing is distributed |
| 22 | `twin up / seed / status / down / reset / init` | **kept by meaning** | `facsimile up / up --fresh / status / down / down --all / init` (T3). `down` keeps `fx_local` and its hand-written rows, as `twin down` kept its container's data; `down --all` removes it, as `twin reset` did |
| 23 | The scale lane (`twin.scale.json`, timing at 181k and 1.18M rows) | retired | the `scale` flag from row counts, and a named residual; real timing needs a full-size server (out of scope) |
| 24 | The nightly proof lane (`SamplePr*Tests`, about 50 facts) | **kept as the corpus** | the differential corpus, nightly and on every PR (T2) |
| 25 | `talk-to-local-sql` detection ladder (Twin → warm container → stop) | replaced | `doctor`: LocalDB, or a configured server for CI and the sandbox |
| 26 | `prove.mjs` (build → delta → Strict → classify) | absorbed, then retired | its classifier and delta signals become the pure core |
| 27 | `inflight-check.mjs` (the lag-window hold) | **kept**, ported | the `in-flight` stop, keyed on the tables of each environment's delta (not on changed files). Its exemption (a PR that also edits the ledger warns rather than stops) and its unreadable-ledger refusal (`stop config`) are kept |
| 28 | `ssdt-agent-proof-lane.yml` | replaced | `facsimile-corpus.yml` |
| 29 | `ssdt-agent-bake.yml` | retired | — |
| 30 | `pipelines/ssdt-agent-pr-validation.yml` (proves against an empty database) | replaced | `facsimile prove` in CI from the committed answers (fixes the inversion) |
| 31 | `twin-test.sh`, `twin-eject-dryrun.sh` | retired | the runner |
| 32 | Evidence collision refusal (law 4) | retired | there are no evidence sources left to collide |
| 33 | Shape-tier literal freedom (law 3) | **kept**, strengthened | P1 (only schema and integers leave), P6 (shape-profile bounds) |
| 34 | `[twin]` state schema excluded from comparison (law 5) | retired | no state object in any database. `up`'s fingerprint lives in the local cache |
| 35 | Convergence (law 1: `up` twice applies nothing) | **kept** | D4 |
| 36 | The eject seam (kernel manifest, boundary tests) | retired | there is no kernel to peel from; the facsimile is self-contained |

## 3. The shape

```
               the environment: read-only, touched exactly twice
     ┌───────────────────────────────────────────────────────────────────────┐
     │ 1  Extract  → schema.<env>.dacpac          needs VIEW DEFINITION        │
     │ 4  Count    → answers.<env>.json           needs SELECT; Rows needs     │
     │                                            only VIEW DEFINITION         │
     └───────────────────────────────────────────────────────────────────────┘
   project ── build ──► head.dacpac ─┬─ 2  Script offline vs schema.<env> ──► delta.<env>.sql
   merge-base ── build ─► base.dacpac┴─ 2′ Script offline vs base.dacpac  ──► delta.pr.sql
   delta.<env> + the env catalog ── 3  Questions (pure, vocabulary) ──► questions
   delta.<env> vs delta.pr ──────────── Lag      (pure) ──► lag/drift operations
   questions + answers ─────────────── Tier     (pure) ──► tier, flags
   questions + answers ─────────────── Plan     (pure) ──► witness plan
   LocalDB ── 5  copy:  publish schema.<env> · base referenceData lanes · cache .bak
                        restore · plant(plan, witness.sql) · hash touched tables
                        accept: re-count == answers · identity: schema plan empty
          ── 6  prove:  publish head with effective options ──► output text · re-hash
   output + delta ──────────── Outcome  (pure) ──► verdict | stop
   verdict + the rest ──────── Record   (pure) ──► verdict.<env>.json ──► author-pr
```

**A pure core with a thin shell.** Every invariant that can be stated over text and tables lives in
eight pure PowerShell functions, which are CLM-safe and do no I/O:

| Function | In → out |
|---|---|
| `Get-FxQuestions` | the delta text, the environment's column catalog (parsed from `schema.<env>.dacpac`'s `model.xml`: type, length, precision, scale, nullability, collation, defaults, constraints, trust), and the vocabulary → questions plus `measures` (the touched tables whose `Rows` are counted, for presence and `scale`), or `stop unknown-statement` |
| `Compare-FxDeltas` | `delta.<env>`, `delta.pr` → the operations the environment receives beyond this PR (`lag`), and the environment-only objects the deploy drops (`drift`) |
| `Get-FxTier` | questions, answers, measures, the in-flight ledger rows, the config (`read: none`, `allowOlderLocalDb`) and the lag/drift result → tier and flags |
| `Get-FxWitnessPlan` | questions, answers, the environment catalog → the plan: per table, which needs (presence, each violation class, links, ancestors, trust state) and the rows they require, plus the *needs* only a person can meet (check, computed, authored). Keys and reused reference rows are resolved at plant time against the copy, and `plant.sql` reports the resolved plan |
| `Test-FxAcceptance` | questions, environment answers, copy answers → ok, or `stop acceptance` |
| `Get-FxOutcome` | the publish output and the delta → verdict, or `stop unclassified` |
| `Get-FxRecord` | verdict, questions, answers, deltas, hashes → record fields for the ten-section PR |
| `Read-FxConfig` | JSON text → config, or `stop config` naming every bad JSON path in one pass |

Pure cases (`kind: pure`) run these in milliseconds with no SQL Server. The shell only calls
Microsoft-signed tools (`sqlpackage`, `SqlLocalDB`, MSBuild via `vswhere`, `sqlcmd`), runs the
T-SQL files, and writes files. The floor is Windows PowerShell 5.1 in ConstrainedLanguage mode.

**Files and budgets** (vendored into the SSDT repository as `facsimile/`):

| File | Role | Budget (lines) |
|---|---|---:|
| `facsimile.ps1` | the shell: verbs, I/O, exit codes, one-line stdout | 400 |
| `core.ps1` | the eight pure functions | 380 |
| `vocabulary.psd1` | the question table (data) | 20 rows |
| `sql/probes.sql` | one templated count per question kind and value class | 220 |
| `sql/plant.sql` | presence rows with FK links and ancestors, plus one procedure per witness kind | 260 |
| `sql/catalog.sql` | the copy's catalog as one result set, for the planter | 60 |
| `sql/shape.sql` | the value-free discovery profile | 70 |
| `sql/hash.sql` | an order-independent content hash (NULL distinct from `''`) | 30 |
| `tests/run-cases.ps1` | the runner and the lint: parses `cases/*.md`, executes by kind | 180 |
| **Total** | | **≈1,600** |

## 4. The vocabulary

The vocabulary is the line between what the facsimile proves and what it hands back, kept as data.
Every statement in a delta falls into exactly one of three classes (F4):

- **data-sensitive:** the statement reads existing rows and can fail on their values. It maps to a
  question.
- **data-insensitive:** it reads or rewrites rows but cannot fail on their values. It asks no
  question, but its tables' `Rows` are measured for the `scale` flag.
- **non-reading:** it touches no existing rows.

Anything else is `Unknown` and stops.

| Kind | Statement DacFx writes (matched per batch) | Question | Count in the environment | Witness on the copy | Value classes |
|---|---|---|---|---|---|
| `HasRows` | `IF EXISTS (select top 1 1 from [s].[T])` followed by `RAISERROR (…, 16, 127)` | does T hold a row? | `sys.partitions` rows (VIEW DEFINITION only) | `presence(T)` with FK links and ancestors | — |
| `NotNull` | `ALTER TABLE [s].[T] ALTER COLUMN [c] … NOT NULL` where the env catalog says c is nullable | any NULL in c? | `WHERE c IS NULL` | `null(T.c)` | — |
| `Fits` | `ALTER COLUMN [c] <type>` where the env catalog's type, length, precision, scale or collation differs and the new one is narrower or of another kind | anything in c that won't fit? | per value class | `misfit(T.c, class)` | `overlen` (LEN), `overbytes` (trailing spaces only), `nonconvertible`, `roundtrip` (lossy), `empty` (`''` to a non-string type), `overflow` (precision or scale) |
| `Unique` | `CREATE UNIQUE … INDEX [i] ON [s].[T] (cols)` (optional `WHERE`); `ADD CONSTRAINT … UNIQUE` / `PRIMARY KEY` | any duplicate, under the collation the index will be built with (head's target collation for each key column), within the filter? | duplicate-group count per class | `dup(T, cols, class)` | `exact`, `case`, `accent`, `trailing`, `nulls` (two NULLs in a non-filtered index) |
| `Orphan` | `WITH CHECK CHECK CONSTRAINT [x]` where `x` is a FOREIGN KEY | any child without a parent? | orphan count (MATCH SIMPLE: a NULL in any FK column passes) | `orphan(T.cols→P)` | — |
| `CheckFalse` | `WITH CHECK CHECK CONSTRAINT [x]` where `x` is a CHECK | any row where the rule is FALSE (not UNKNOWN)? | `WHERE NOT (predicate)`, under the predicate exception below; otherwise `unmeasured` | **needs:** an agent-written row in `witness.sql` | — |
| `KeyLength` | `CREATE … INDEX` whose maximum key exceeds 1700 bytes (900 clustered) | any key value over the limit? | `DATALENGTH` sum over the key | `longkey(T, i)` | — |
| `Rebuild` | `CREATE TABLE [s].[tmp_ms_xx_T]` … `INSERT INTO … SELECT … FROM [s].[T]` | rows in T; does every moved column fit; identity gaps? | `HasRows`, plus `Fits` per moved column, plus `gapped` | presence, misfit, `gapped(T)` | — |
| `Computed` | `ADD [c] AS (…) PERSISTED` on an existing table | any row where the expression fails? | `unmeasured` | **needs:** an agent-written row; recorded `declared-not-measured` | — |
| `Temporal` | `ADD PERIOD FOR SYSTEM_TIME`; `SET (SYSTEM_VERSIONING = ON …)` with a history table that holds rows | any row whose period end is not after its start, or that overlaps? | invalid-period count | `misfit(T.period, invalid)` | — |
| `Scan` | non-unique `CREATE INDEX`; an index DROP+CREATE; `ADD [c] … NOT NULL DEFAULT …` (backfill); a clustered PK drop or create | none (data-insensitive) | `Rows(T)` for the `scale` flag | none | — |
| `Authored` | a pre- or post-deploy statement in head that is not in the merge-base | what shapes does this script handle? | `shape` verb (discovery), then declared counts | **needs:** declared witnesses, or `-- shapes: none (<reason>)` | — |
| `SeedDrift` | a post-deploy MERGE seed that changed (an `Authored` subset) | does the environment's lookup disagree with the seed's own rows? | count of rows whose (key, code) disagrees with the seed's VALUES (repository values are sent; only integers return) | `declared(seed drift row)` | — |
| `Unknown` | any other statement that reads existing rows | — | — | **stop** `unknown-statement` | — |

**Coordinates.** Every question names coordinates (`schema.table.column`). They resolve in this
order:

1. **Created by this delta.** The question is answered *from the delta*, not by a probe:
   - a new table is empty;
   - a new column holds its default, or NULL, in every existing row of its table. So a unique index
     on a new defaulted column of a table with two or more rows is an `exact` duplicate;
   - an FK from an existing column to a new table has an orphan for every non-NULL child value,
     unless a pre- or post-deploy script fills the parent. Constraint validation runs after the
     post-deploy script (observed 2026-10-18 on sqlpackage 170.5.96; a `verify` case pins it per
     version), so a seeded lookup counts. Then the question becomes `Authored`.
2. **Renamed or moved by this delta.** `sp_rename` and `ALTER SCHEMA … TRANSFER` in the same delta
   map the coordinate to its old name, and the probe uses the old name.
3. **Present in the environment's catalog.** It is probed.
4. **Otherwise** the run stops `missing-object` (S3).

**The predicate exception (P2).** A probe is a template. The only non-template text it may carry is
a predicate copied verbatim from the delta's own generated DDL: a CHECK expression, or a filtered
index's `WHERE`. That is model text the deploy itself will evaluate on that data. Such a probe:
- is wrapped so it returns an integer only;
- is refused, and recorded as `unmeasured`, if the predicate contains `CONVERT`, `CAST` or `PARSE`
  (P3).

**The doctrine for authored remedies.** A remedy must be written by predicate
(`WHERE NOT EXISTS (…)`, `WHERE LEN(Code) > 10`), never by a literal value (`WHERE CustomerId = 999`).
A literal-keyed remedy fixes the rows of one environment only. The facsimile's synthetic witnesses
expose that by staying unfixed, so a block after an authored remedy prints: "If the remedy names
specific values, rewrite it by predicate so it is total in every environment."

## 5. Building the copy

The steps run in this order, every time, for each environment:

1. **Restore** the base cache. Its key is the hash of `schema.<env>.dacpac`, the merge-base's
   `referenceData` lanes, the effective options, the tool version, the DacFx version and the LocalDB
   version (D5). On a miss, create it once, under a cache lock:
   - make a fresh `fx_base_<hash12>_<rand4>` database;
   - publish `schema.<env>.dacpac` into it;
   - run the **merge-base** version of the `referenceData` lanes, so reference data is as the
     environment holds it before this change;
   - take a `BACKUP`.

   Every proof runs on a new `fx_<slug>_<env>_<rand4>` database restored from that backup, recorded
   in the tool's registry. Nothing is ever wiped (S5).
2. **Plant presence:** one valid row in every touched table whose environment `Rows > 0` (every
   touched table's `Rows` is measured), plus its NOT NULL FK ancestors.
   - A violation row also counts as presence. A table needs the *maximum* of its needs, not their
     sum: one row by default, two for a `dups` or `nulls` class or a gapped identity.
   - Every probe and the planter run under `SET LANGUAGE us_english; SET DATEFORMAT ymd`. The
     Octopus login's language and date format are part of the captured options, or the
     `platform:dateformat` residual.
   - An ancestor reuses an existing row (reference data) before planting a new one.
   - For every `Orphan` question the plan **links** the child's column to a planted or reference
     parent row, even though the FK does not exist yet in the base. Otherwise the copy would hold
     orphans the environment doesn't.
   - Nullable columns get non-NULL values, so "populated but clean" stays clean. Nullable FK
     columns with no `Orphan` question are the exception and get NULL.
3. **Plant violations:** one row per (question answered "violating" × value class).
4. **Reproduce trust state.** For every constraint the environment holds untrusted or disabled, the
   planter may suspend it to plant, then restores exactly the environment's state (F9). Only the
   planter does this; `witness.sql` stays DML only.
5. **Apply `witness.sql`:** the text is DML only, holding agent-declared witnesses and pins. The
   planter runs it inside the same suspend-and-restore envelope as step 4, so a declared row can be
   inserted under an existing trusted constraint, or in a NOT NULL cycle, and the environment's
   trust state is restored afterwards.
   - **A block** begins `-- plants: <T>` (it owns T's rows) or `-- witness: <question key>` (it
     discharges that need). The blocks run in FK order.
   - **A failing row** stops `witness-failed`.
   - **A literal** that is neither synthetic nor repository-defined stops `witness-not-synthetic`.
6. **Hash** every touched table and every reference table.
7. **Accept:** re-run the same probes against the copy. Every boolean must equal the environment's,
   including every "no" (F1). `unmeasured` questions are excluded and recorded
   `declared-not-measured`.
8. **Check identity:** script `schema.<env>.dacpac` against the copy under the effective options. The
   plan must be empty (F2).
9. **Publish head** under the effective options (Strict), classify the outcome, and re-hash.
10. **After a block:**
    - script head against the copy and record what was left applied, by phase (H6). The phases, in the
      order DacFx runs them: pre-deploy, main (transactional only when `IncludeTransactionalScripts`
      is on), post-deploy, then constraint validation (`WITH CHECK CHECK`, outside the transaction);
    - with `--permissive`, restore, re-plant, and publish with only `BlockOnPossibleDataLoss`
      flipped, recording the hashes before and after.
11. **After a clean publish:** check silence (D6). Publish head again. The plan must be empty, the
    rows affected zero, and every hash unchanged. Otherwise record `not-silent` with the evidence.
12. **For a multi-release program** (`--program R1 R2 …`): the questions come from the final target
    (F8). Prove R1, then R1 again (a non-empty replay is the F17 hazard, recorded), then R2, all on
    one copy.

The base is **never re-published after planting** (S2). The copy is only ever a database the tool
created (S1).

## 6. Outcomes

A **proving** verb (`prove`) ends in exactly one verdict or one stop. Every **other** verb ends in
`done` or a stop (H1).

stdout carries exactly one line (A1):

```
VERDICT <clean|blocked <reason>|refused-at-build> · <object or message> · <file> · exit <n>
STOP <kind> · <next step> · <file> · exit <n>
DONE <verb> · <what happened, e.g. "Nothing to apply"> · <file> · exit 0
```

A multi-environment `prove` prints the line of its most severe outcome (stop > blocked > clean) and
names `verdicts.json`, the index of every per-environment file. Every outcome, `DONE` included,
writes its JSON:
- change-scoped verbs write `proofs/<slug>/<verb>.<env>.json`, for example `read.QA.json` or
  `verdict.QA.json`;
- machine-scoped verbs (`doctor`, `up`, `down`, `status`, `init`) write `.facsimile/out/<verb>.json`.

**Verdicts** (exit 0 or 3):

| Verdict | Signal | Exit |
|---|---|---:|
| `clean` | `Successfully published database`, every question answered or declared, acceptance and identity passed, no unknown statement | 0 |
| `blocked guard` | Msg 50000 state 127, in a failing batch that is a guard batch of the delta. SQL72045 reformats the batch it quotes, so batches are compared token by token (whitespace and bracket-insensitive) | 3 |
| `blocked constraint` | 547 (FK or CHECK, named) | 3 |
| `blocked null` | 515; 4901 (a NOT NULL column added without a default to a table with rows); 8111 (a PRIMARY KEY on a nullable column, data-independent) | 3 |
| `blocked unique` | 1505 (+1750); on insert, 2601 or 2627 | 3 |
| `blocked truncation` | 2628, 8152 | 3 |
| `blocked overflow` | 8115, 220, 248 | 3 |
| `blocked conversion` | 245, 241, 8114, 8169 | 3 |
| `blocked keylength` | 1946 | 3 |
| `blocked cdc` | 4928 raised by a generated (main-phase) statement, only on a server that has CDC. A 4711 from a TRUNCATE in an authored script is `blocked authored`, with the `cdc` flag | 3 |
| `blocked temporal` | 13575, or the system-versioning consistency check's failure (its number is ARCH §11 question 13), raised by a generated `Temporal` statement | 3 |
| `blocked collation` | 468 raised by a generated (main-phase) statement. A 468 inside an authored batch is `blocked authored` when the copy's server collation equals the environment's, otherwise `stop platform` | 3 |
| `blocked authored` | **any** error raised inside a pre- or post-deploy batch: the seed's 207, 544, 8672, 2627; a remedy's own `RAISERROR`/`THROW`; a script's 208, 2714, 3726, 13575 | 3 |
| `refused-at-build` | SQL71516 (a referenced key dropped) | 3 |

Engine warnings (Msg 1945; SQL72015, SQL72016) never decide a verdict; they are recorded under
`warnings`.

**Stops** (one closed list; each names one next step, addressed to the agent; the exit code says who
acts):

| Stop | Exit | Who acts | Next step, as the stop prints it |
|---|---:|---|---|
| `usage` | 1 | the agent | "Unknown verb or argument <x>. Usage: <one line>." |
| `unreachable` | 4 | the developer | "Cannot reach <server> (<timeout\|network>). Check the network, or ask someone with access to run `facsimile read <env>`." |
| `no-access` | 4 | the developer or a lead | "<login> <cannot log in to \| lacks VIEW DEFINITION \| lacks SELECT on> <db>. Ask a lead to run `facsimile read <env>`, or request the grant." |
| `probe-failed` | 4 | the developer | "A count failed on <env> with Msg <n> (text withheld). Re-run `facsimile read <env>`; if it repeats, ask the principal." |
| `missing-object` | 5 | the agent | "<env> has no <object>, and this delta does not create it. Prove against what <env> has, or record <env> as Not checked." |
| `stale-answers` | 5 | the agent | "answers.<env>.json was measured for a different delta or head. Re-run `facsimile read <env>`." |
| `answers-invalid` | 5 | the agent | "answers.<env>.json does not validate (<path>), or it names a server other than <env>'s. Re-run `facsimile read <env>`." |
| `authored-script` | 5 | the agent with the developer | "The change runs authored SQL reading <existing columns>. Run `facsimile shape <env> <column>` for each, then declare a witness in witness.sql for every shape the script handles, or `-- shapes: none (<reason>)`, and make the script check its own preconditions." |
| `check-witness` | 5 | the agent | "Write one row into witness.sql that makes <CK> FALSE, then re-run." |
| `computed-witness` | 5 | the agent | "Write one row into witness.sql whose <column> expression fails, then re-run." |
| `planter-needs-row` | 5 | the agent | "Cannot make a valid row for <T> (<reason>). Write one valid INSERT for <T> into witness.sql (or up.sql) under `-- plants: <T>`." |
| `witness-failed` | 5 | the agent | "witness.sql line <n> failed with Msg <m>. Fix that row." |
| `witness-not-synthetic` | 5 | the agent | "witness.sql line <n> carries a literal that is neither marked synthetic nor defined in the repository's model (a seed value, a default, a CHECK predicate's literal). Use a `w`-prefixed value or a repository-defined one." |
| `acceptance` | 5 | the agent | "The copy answers <question> <yes\|no>, but <env> answered <no\|yes>. Fix witness.sql." |
| `identity` | 5 | the agent | "witness.sql changed the schema (<object>). Witness files are DML only." |
| `in-flight` | 5 | the developer | "<T> is in an open two-release window (<in-flight ledger>). Hold this change, or make it the window's next release." |
| `config` | 6 | the developer | "facsimile.json: <json path>: <problem>." (all problems, one pass) |
| `toolchain` | 6 | the developer | "<tool> not found, too old, or not responding (LocalDB gave no answer in 180 s after one retry). <how to get it or restart it>." |
| `platform` | 6 | the developer | "<LocalDB <v> is older than the project's target <DSP> \| tempdb collation <c1> differs from <env>'s <c2> and the script compares #temp columns>. <remedy>." |
| `options-unknown` | 6 | the principal | "No usable captured effective options (<missing \| BlockOnPossibleDataLoss=False>). Capture the Octopus step's options into the file `options` names." |
| `build-failed` | 7 | the developer | "The project does not build: <first error>." |
| `unknown-statement` | 9 | the principal | "Delta line <n> reads rows, but no vocabulary row matches it. Add a row with its case." |
| `unclassified` | 9 | the principal | "The publish ended with <message> that no rule classifies. Add a rule with its case." |
| `cdc` | 9 | the principal | "<T> is tracked by CDC, and LocalDB has none. This change is principal-led." |
| `base-unpublishable` | 9 | the principal | "<env>'s schema does not publish to LocalDB (<feature>). Record it as Not checked." |
| `identifier-unsafe` | 9 | the principal | "<identifier> cannot be validated against <env> safely. Nothing was sent." |
| `interrupted` | 9 | the developer | "The run was interrupted at <step>. `facsimile down` removes its copies; re-run." |
| `internal` | 9 | the principal | "Unexpected failure at <step> (<type>). Nothing claims a verdict." |

**CDC, precisely.** The facts record `is_tracked_by_cdc` for every touched table:
- a tracked touched table sets the `cdc` flag;
- on LocalDB it also stops the proof with `cdc`;
- `blocked cdc` is only ever a verdict on a full SQL Server that has CDC (corpus jobs).

## 7. Tier and flags

The tier is a pure function of the questions. The record carries two tiers:
- the PR's own tier, from `delta.pr.sql`, which is identical on every machine;
- each environment's tier, from that environment's delta, which may be higher when the environment
  lags (`lag:<env>`).

| Tier | Holds when | What it costs |
|---|---|---|
| `T0 additive` | no data-sensitive statements | a clean publish of head onto the copy |
| `T1 presence` | the only questions are `HasRows` | one count, from `sys.partitions` |
| `T2 validation` | any of `NotNull`, `Fits`, `Unique`, `Orphan`, `CheckFalse`, `KeyLength`, `Rebuild`, `Computed`, `Temporal` | counts, witnesses, and the proof |
| `T3 authored` | any `Authored` or `SeedDrift` | look first (shape), then T2 |

**Flags:**

| Flag | Set when |
|---|---|
| `scale` | a touched table has `Rows ≥ 1,000,000`, and the statement on it is data-sensitive, `Scan`, `Rebuild`, or authored DML |
| `cdc` | a touched table is tracked by CDC |
| `unread:<env>` | an environment has no answers (such as Prod) |
| `assumed:<env>` | its verdict came from `--assume` |
| `lag:<env>` | the environment receives operations beyond this PR (from `Compare-FxDeltas`) |
| `drift:<env>` | the deploy would drop environment-only objects |
| `in-flight` | a touched table is in an open two-release window |
| `older-localdb` | `allowOlderLocalDb` was used |

A tiered risk harness consumes `tier` and `flags` from `verdict.<env>.json` and decides which further
checks to run. The facsimile assigns the tier from the delta, never from the operation's name, so
the tier cannot be argued with.

## 8. Verbs, config and files

| Verb | Touches the environment | What it does |
|---|---|---|
| `facsimile doctor [--reach <env>]` | only with `--reach` | tools, versions, LocalDB vs DSP, language mode, captured options, config |
| `facsimile read <env>` | yes, twice | extract, script offline, questions, counts. Writes `schema.<env>.dacpac`, `delta.<env>.sql`, `delta.pr.sql` and `answers.<env>.json` |
| `facsimile prove [<env>…] [--permissive] [--program <R1> <R2>…]` | no | copy, plant, accept, check identity, publish, classify. Writes `verdict.<env>.json` and `verdicts.json`. CI runs this |
| `facsimile prove --assume <env>=worst` | no | for an environment with no answers (Prod), or with `unmeasured` answers: proves both worlds per question (clean, violating), and records the message the violating world would hit and the probe to run before promoting. The schema comes from the nearest read environment, and the verdict is stamped `assumed` |
| `facsimile shape <env> <s.t.c> [--vocabulary]` | yes (SELECT) | the value-free discovery profile for one column. Writes `shape.<env>.<s.t.c>.json` |
| `facsimile up [--fresh]` | no | the daily local database `fx_local`: the repository schema, reference data, k presence rows per table, then `up.sql` (declared rows and pins, DML only). Converged |
| `facsimile status` | no | the facsimile databases, `up` freshness, cached schemas and their age, answer staleness |
| `facsimile down [--all]` | no | drop this tool's proof copies. `fx_local` and the caches stay; `--all` also drops `fx_local` and clears caches. Never drops a database absent from the tool's registry |
| `facsimile init` | no | scaffold `facsimile.json` |

**`facsimile.json`** is a closed schema (E4):
- an unknown key is refused by its JSON path;
- every problem is reported in one pass;
- a secret is accepted only as an `env:` or `file:` reference (the Twin's D9, kept).

```json
{
  "project": "Estate.sqlproj",
  "options": "facsimile/options.publish.xml",
  "sqlcmd": { "SomeVar": "value" },
  "referenceData": ["ReferenceData/*.sql"],
  "inFlight": "ledgers/in-flight.md",
  "localdb": "MSSQLLocalDB",
  "allowOlderLocalDb": false,
  "environments": {
    "Dev":  { "server": "dev-sql",  "database": "Estate", "auth": "integrated" },
    "QA":   { "server": "qa-sql",   "database": "Estate", "auth": "integrated", "sqlcmd": { "SomeVar": "qa" } },
    "UAT":  { "server": "uat-sql",  "database": "Estate", "auth": "integrated" },
    "Prod": { "server": "prod-sql", "database": "Estate", "read": "none" }
  },
  "up": { "rowsPerTable": 3 }
}
```

| Key | Required | Meaning |
|---|---|---|
| `project` | yes | the `.sqlproj` to build |
| `options` | yes | the captured **effective** Octopus options: the profile with every `/p:` override folded in |
| `sqlcmd` | no | the global SQLCMD values; `environments.<env>.sqlcmd` overrides them per environment |
| `referenceData` | no | globs of seed lanes, run at the merge-base on every copy and at head for `up` (Twin parity #4) |
| `inFlight` | no | the in-flight ledger: `estate/in-flight.md` in the source tree, `ledgers/in-flight.md` when vendored |
| `localdb` | no | the LocalDB instance; default `MSSQLLocalDB` |
| `allowOlderLocalDb` | no | permits a LocalDB older than the DSP by setting `AllowIncompatiblePlatform=True` **on copy publishes only**. This is the one documented exception to F6, stamped `older-localdb` on every record |
| `environments` | yes | names matching `[A-Za-z][A-Za-z0-9_-]*`. Each has a `server` and a `database`; an `auth` of `integrated` (the default), `env:VAR` or `file:path` (secret-bearing); an optional `sqlcmd`; and `read: none` for an environment nobody can read |
| `up.rowsPerTable` | no | k for `up`; default 3 |

**What one change commits** (`proofs/<branch-slug>/`, written by the tool, reviewed in the PR):

| File | Contents |
|---|---|
| `schema.<env>.dacpac` | the extracted schema: structure only, no data (P1). Committed so CI and reviewers reproduce the proof without access to the environment |
| `delta.<env>.sql` and `delta.pr.sql` | the two deltas |
| `answers.<env>.json` | stamped with server, database, login, time, head commit, delta hash, and probe text. Its content hash covers answer values only |
| `witness.sql` | if any |
| `shape.*.json` | if any: signatures and counts only. `--vocabulary` literals are written to `.facsimile/out/` and are never committed (P1, P6) |
| `verdict.<env>.json` and `verdicts.json` | the outcomes |

**What is never committed:** `.facsimile/`, meaning the base backups, head builds, the registry and
`up`'s fingerprint.

## 9. How every invariant is attended to

1. **Traceability is generated, not hoped for.** `tests/run-cases.ps1 --lint` refuses a catalog in
   which any of these is missing:
   - an invariant without two cases;
   - a case without an invariant;
   - a vocabulary row without a `corpus` case;
   - a stop kind without a case;
   - a Twin responsibility without a parity or retirement case;
   - an operation without its standard situations (`empty`, `clean`, `violating:<class>`, `remedy`,
     `edge`), unless a case's `note: n/a <tag> — <reason>` excuses one;
   - a TWN case that does not name its ARCH §2 row as `row <n>` in its title or note.
2. **The differential oracle.** For every `corpus` case the runner builds two things from one fixture:
   the stand-in environment, and a full copy of it. It requires
   `verdict(facsimile(env)) == verdict(publish(head → copy of env))`. A hand-written expectation can
   be wrong; the engine cannot disagree with itself.
3. **Exhaustive value classes.** The value classes are finite (§4), and a generated case matrix
   covers every (kind × class × populated) cell. They are enumerated, not sampled.
4. **Necessity by deletion (M1).** A mutation pass removes each vocabulary row, witness kind and
   pipeline step in turn. At least one case must fail each time, or the item is dead weight and is
   removed.
5. **Budgets (M4).** A line-count check over the §3 table. Exceeding a budget fails the build until
   the budget is re-argued in a reviewed PR.
6. **The pure core / thin shell split.** The invariants live where tests are cheap. The shell is too
   thin to hold an invariant on its own.

## 10. The migration (the team keeps working throughout)

| Gate | What must be true | What the team does |
|---|---|---|
| **G0** | This catalog is complete and audited (`cases/90-traceability.md`) | nothing changes |
| **G1** | The pure core and the runner exist; every `pure` case is green | nothing changes |
| **G2** | The shell and SQL exist; every `sql` and `corpus` case is green on SQL Server 2022 *and* on LocalDB (Windows CI); the differential agrees 100% | early adopters try `facsimile prove` beside the Twin |
| **G3** | Twin parity: every `TWN` case is green, including all SamplePr facts | — |
| **G4** | Side by side for one release cycle: each schema PR records both verdicts, and any disagreement is a defect (T4) | `twin up` users move to `facsimile up`; the skills switch (below) |
| **G5** | Deprecation: Twin banner; proof lane and PR pipeline on the facsimile; `prove.mjs`, `bake.mjs` and the bake workflow retired | the Twin is read-only reference |
| **G6** | Removal from the vendored bundle; archive tag | — |

**Changes to the `ssdt-agent` tree** (made at G4, after the cases are green, never before):

- `skills/talk-to-local-sql/SKILL.md` is rewritten around `facsimile doctor`, `read`, `shape` and
  `up`. The Twin and warm-container ladder is removed.
- `skills/prove-on-dacpac/SKILL.md` keeps the reasoning; the mechanics become `facsimile prove`, and
  the scaffolded sqlpackage sequence stays as the explanation of what the tool does.
- `agents/change-author.md` and `agents/reviewer.md` drop docker, bash and `warm-sql.sh`. The
  reviewer extends the proof with `facsimile read QA|UAT` (leads can read those environments).
- Every `skills/op/*/SKILL.md` gains a `## Proof plan` section (questions, value classes, the
  witnesses an author must declare) generated from that op's cases.
- `skills/deploy-scripts/SKILL.md:87` gets its correction: pre- and post-deploy work is not rolled
  back with a failed deploy.
- `PORTABILITY.md`, `PROVING_PATH_WINDOWS.md` and `proving-ground/README.md` point to the facsimile.
- `THE_TWIN.md` and `TWIN_README.md` get a deprecation banner at G5.

## 11. Questions the first implementation must settle (`verify` cases)

These are genuinely uncertain, and each is a case marked `verify`. Its first run decides it, and the
case is updated with the receipt.

1. Does `sqlpackage /Action:Extract` preserve a disabled FK, and an enabled-but-untrusted
   (`WITH NOCHECK`) constraint? If not, trust state needs metadata witnesses.
2. Is `sys.partitions.rows` exact at zero vs non-zero on a quiet database, under a VIEW
   DEFINITION-only login, on SQL Server 2019 and 2022?
3. Do the generated statement shapes in §4 hold on DacFx 162.x as well as 170.x?
4. Does a classic `.sqlproj` build with only VS 2022's MSBuild on a corporate laptop, and how long
   does it take for the real estate?
5. Does `sqlcmd` from the VS install exist, or is go-sqlcmd needed?
6. How long does LocalDB take to CREATE, RESTORE and DROP for a ~300-table schema? (This sets the
   cache design.)
7. Do the planter's literals satisfy the estate's real CHECK constraints often enough, or is
   `planter-needs-row` frequent?
8. Does every probe and the planter run under ConstrainedLanguage mode as written?
9. Does constraint validation (`WITH CHECK CHECK`) run after the post-deploy script on DacFx 162.x,
   as observed on 170.5.96? It decides whether a post-deploy-seeded lookup satisfies a new FK.
10. Do system-named defaults (`DF__…`) and other system-named constraints keep their names through
    Extract → publish to the copy? If not, does the head delta against the copy differ from the
    delta against the environment (F5)?
11. Does Extract, as distinct from Script, send `xp_instance_regread` under a hardened read-only
    login?
12. Which language and date format does the Octopus deployment login run under? It decides
    whether `platform:dateformat` stays a residual.
13. Does `SET SYSTEM_VERSIONING = ON` run its consistency check on LocalDB as on SQL Server 2022,
    and which message does a failed check raise?
