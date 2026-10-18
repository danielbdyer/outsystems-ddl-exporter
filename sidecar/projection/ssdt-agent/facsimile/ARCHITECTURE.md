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

The whole edition is budgeted at **about 1,500 lines**, PowerShell and T-SQL together. That is
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
| 2 | `twin.json` closed config: unknown key refused by path; no inline secrets (D9); located refusals | **kept** | `facsimile.json`, same laws (E4) |
| 3 | Estate globs (tables, schemas) | replaced | the project file, built by MSBuild or `dotnet build` |
| 4 | Static lanes applied verbatim | **kept** | `referenceData` lanes, run on the copy before planting |
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
| 17 | Scenario pins (exact rows) | **kept** | `witness.sql`, declared rows |
| 18 | `twin check` (π∘σ≈id, byte-identical re-mint, zero orphans) | replaced | acceptance re-measure (F1) plus schema identity (F2) plus determinism (D1) |
| 19 | S-stable (a schema edit re-mints only the touched columns) | **kept** | ordinal-addressed literals (D3) |
| 20 | T1 determinism | **kept** | D1 |
| 21 | `twin bake` (image) and `bake.mjs` (.bacpac artifact) | retired | content-keyed base caches; nothing is distributed |
| 22 | `twin up / seed / status / down / reset / init` | **kept by meaning** | `facsimile up / up --fresh / status / down / down --all / init` (T3) |
| 23 | The scale lane (`twin.scale.json`, timing at 181k and 1.18M rows) | retired | the `scale` flag from row counts, and a named residual; real timing needs a full-size server (out of scope) |
| 24 | The nightly proof lane (`SamplePr*Tests`, about 50 facts) | **kept as the corpus** | the differential corpus, nightly and on every PR (T2) |
| 25 | `talk-to-local-sql` detection ladder (Twin → warm container → stop) | replaced | `doctor`: LocalDB, or a configured server for CI and the sandbox |
| 26 | `prove.mjs` (build → delta → Strict → classify) | absorbed, then retired | its classifier and delta signals become the pure core |
| 27 | `inflight-check.mjs` (the lag-window hold) | **kept**, ported | the `in-flight` stop, read from `estate/in-flight.md` |
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
     │ 4  Count    → answers.<env>.json           needs SELECT; "has rows?"    │
     │                                            needs only VIEW DEFINITION   │
     └───────────────────────────────────────────────────────────────────────┘
   project ── build ──► head.dacpac ─┐
                                     ├─ 2  Script offline ──► delta.<env>.sql
   schema.<env>.dacpac ──────────────┘
   delta ──────────────── 3  Questions   (pure, vocabulary) ──► questions
   questions + answers ─────── Tier      (pure) ──► tier, flags
   questions + answers ─────── Plan      (pure) ──► witness plan
   LocalDB ── 5  copy:  publish schema · referenceData lanes · cache .bak
                        restore · plant(plan, witness.sql)
                        accept: re-count == answers · identity: schema plan empty
          ── 6  prove:  publish head with effective options ──► output text
   output + delta ──────────── Outcome   (pure) ──► verdict | stop
   verdict + the rest ──────── Record    (pure) ──► verdict.<env>.json ──► author-pr
```

**A pure core with a thin shell.** Every invariant that can be stated over text and tables lives in
seven pure PowerShell functions, which are CLM-safe and do no I/O:

| Function | In → out |
|---|---|
| `Get-FxQuestions` | delta text, vocabulary → questions, or `stop unknown-statement` |
| `Get-FxTier` | questions, answers → tier and flags |
| `Get-FxWitnessPlan` | questions, answers → plan items, plus *needs* (check, computed, authored) |
| `Test-FxAcceptance` | questions, environment answers, copy answers → ok, or `stop acceptance` |
| `Get-FxOutcome` | publish output, delta → verdict, or `stop unclassified` |
| `Get-FxRecord` | verdict, questions, answers, delta → record fields for the ten-section PR |
| `Read-FxConfig` | JSON text → config, or `stop config` naming the JSON path |

Pure cases (`kind: pure`) run these in milliseconds with no SQL Server. The shell only calls
Microsoft-signed tools: `sqlpackage`, `SqlLocalDB`, MSBuild via `vswhere`, and `sqlcmd`. It runs
the T-SQL files and writes files.

**Files and budgets** (vendored into the SSDT repository as `facsimile/`):

| File | Role | Budget (lines) |
|---|---|---:|
| `facsimile.ps1` | the shell: verbs, I/O, exit codes, one-line stdout | 400 |
| `core.ps1` | the seven pure functions | 350 |
| `vocabulary.psd1` | the question table (data) | 15 rows |
| `sql/probes.sql` | one templated count per question kind and value class | 200 |
| `sql/plant.sql` | presence rows with FK ancestors, plus one procedure per witness kind | 250 |
| `sql/catalog.sql` | the copy's catalog as one result set, for the planter | 60 |
| `sql/shape.sql` | the value-free discovery profile | 50 |
| `sql/hash.sql` | an order-independent content hash (NULL distinct from `''`) | 30 |
| `tests/run-cases.ps1` | the runner: parses `cases/*.md`, executes by kind | 150 |
| **Total** | | **≈1,500** |

## 4. The vocabulary

The vocabulary is the line between what the facsimile proves and what it hands back, kept as data.
Each row maps a DacFx-generated statement to the question it asks, the count that answers it, and
the witness that reproduces a "yes".

| Kind | Statement DacFx writes (matched per batch) | Question | Count in the environment | Witness on the copy | Value classes |
|---|---|---|---|---|---|
| `HasRows` | `IF EXISTS (select top 1 1 from [s].[T])` followed by `RAISERROR (…, 16, 127)` | does T hold a row? | `sys.partitions` rows (VIEW DEFINITION only) | `presence(T)` with FK ancestors | — |
| `NotNull` | `ALTER TABLE [s].[T] ALTER COLUMN [c] … NOT NULL` | any NULL in c? | `WHERE c IS NULL` | `null(T.c)` | — |
| `Fits` | `ALTER COLUMN [c] <type>` where the type narrows or changes | anything in c that won't fit? | per value class | `misfit(T.c, class)` | `overlen` (LEN), `overbytes` (trailing spaces only), `nonconvertible`, `roundtrip` (lossy), `empty` (`''` to a non-string type), `overflow` (precision or scale) |
| `Unique` | `CREATE UNIQUE … INDEX [i] ON [s].[T] (cols)` (optional `WHERE`); `ADD CONSTRAINT … UNIQUE` / `PRIMARY KEY` | any duplicate, under T's collation, within the filter? | duplicate-group count per class | `dup(T, cols, class)` | `exact`, `case`, `accent`, `trailing`, `nulls` (two NULLs in a non-filtered index) |
| `Orphan` | `WITH CHECK CHECK CONSTRAINT [fk]` where `fk` is a FOREIGN KEY added `WITH NOCHECK` | any child without a parent? | orphan count (MATCH SIMPLE: a NULL in any FK column passes) | `orphan(T.cols→P)` | — |
| `CheckFalse` | `WITH CHECK CHECK CONSTRAINT [ck]` where `ck` is a CHECK | any row where the rule is FALSE (not UNKNOWN)? | `WHERE NOT (predicate)` | **needs:** an agent-written row in `witness.sql` | — |
| `KeyLength` | `CREATE … INDEX` whose maximum key exceeds 1700 bytes (900 clustered) | any key value over the limit? | `DATALENGTH` sum over the key | `longkey(T, i)` | — |
| `Rebuild` | `CREATE TABLE [s].[tmp_ms_xx_T]` … `INSERT INTO … SELECT … FROM [s].[T]` | rows in T; does every moved column fit; identity gaps? | `HasRows` plus `Fits` per moved column, plus `gapped` | presence, misfit, `gapped(T)` | — |
| `Computed` | `ADD [c] AS (…) PERSISTED` on an existing table | any row where the expression fails? | — | **needs:** an agent-written row | — |
| `Authored` | any pre- or post-deploy statement that changed against the base | what shapes does this script handle? | `shape` verb (discovery), then declared counts | **needs:** declared witnesses | — |
| `Unknown` | any other statement that reads existing rows | — | — | **stop** `unknown-statement` | — |

## 5. Building the copy

The steps run in this order, every time, for each environment:

1. **Restore** the base cache for `hash(schema.<env>.dacpac, referenceData, effective options)`. On a
   miss, create it once:
   - make a fresh `fx_<hash12>_base` database;
   - publish `schema.<env>.dacpac` into it;
   - run the `referenceData` lanes;
   - take a `BACKUP`.

   Every proof runs on a new `fx_<slug>_<env>_<rand>` database restored from that backup. Nothing
   is ever wiped (S5).
2. **Plant presence:** one valid row per table where `HasRows=yes`, plus its NOT NULL FK ancestors.
   An ancestor reuses an existing row (reference data) before planting a new one. Nullable columns
   get non-NULL values, so "populated but clean" stays clean. Nullable FK columns are the exception
   and get NULL.
3. **Plant violations:** one row per question answered "violating", per value class.
4. **Apply `witness.sql`:** DML only. Its rows cover agent-written witnesses and pins.
5. **Accept:** re-run the same probes against the copy. Every boolean must equal the environment's,
   including every "no" (F1).
6. **Check identity:** script `schema.<env>.dacpac` against the copy under the effective options. The
   plan must be empty (F2).
7. **Publish head** under the effective options (Strict), then classify the outcome.
8. **After a block:**
   - script head against the copy and record what was left applied, by phase (H6);
   - with `--permissive`, restore, re-plant, and publish with only `BlockOnPossibleDataLoss` flipped,
     taking a content hash of every touched table before and after.
9. **After a clean publish:** check silence. A second plan must be empty and the reference-data
   hashes unchanged.
10. **For a multi-release program:** prove R1, then R1 again (a non-empty second plan is the F17
    hazard), then R2, all on one copy (F8).

The base is **never re-published after planting** (S2). The copy is never anything but a database
the tool created (S1).

## 6. Outcomes

Every run ends in exactly one outcome (H1). stdout carries one line; the full JSON goes to a file.

**Verdicts** (exit 0 or 3):

| Verdict | Signal | Exit |
|---|---|---:|
| `clean` | `Successfully published database`, every question answered, acceptance and identity passed, no unknown statement | 0 |
| `blocked guard` | Msg 50000 state 127, in a batch that is a guard batch of the delta | 3 |
| `blocked constraint` | 547 (FK or CHECK, named) | 3 |
| `blocked null` | 515 | 3 |
| `blocked unique` | 1505 (+1750); on insert, 2601 or 2627 | 3 |
| `blocked truncation` / `overflow` / `conversion` | 2628, 8152 / 8115 / 245, 241, 8114 | 3 |
| `blocked seed` | 207, 544, 8672 (in the post-deploy phase) | 3 |
| `blocked keylength` | 1946 | 3 |
| `blocked cdc` | 4928, 4711 (only on a server that has CDC) | 3 |
| `blocked collation` | 468 | 3 |
| `refused-at-build` | SQL71516 (a referenced key dropped) | 3 |

**Stops** (one closed list; each names one next step, addressed to the agent, and the exit code
says who acts):

| Stop | Exit | Who acts | Next step, as the stop prints it |
|---|---:|---|---|
| `unreachable` | 4 | the developer | "Cannot reach <server>. Check the network, or ask someone with access to run `facsimile read <env>`." |
| `no-access` | 4 | the developer or a lead | "<login> lacks <VIEW DEFINITION \| SELECT> on <db>. Ask a lead to run `facsimile read <env>`, or request the grant." |
| `missing-object` | 5 | the agent | "<env> has no <object>. It has not received the change that creates it. Prove against what <env> has, or record <env> as Not checked." |
| `stale-answers` | 5 | the agent | "answers.<env>.json was measured for a different delta. Re-run `facsimile read <env>`." |
| `authored-script` | 5 | the agent with the developer | "The change runs authored SQL over <columns>. Run `facsimile shape <env> <column>` for each, declare a witness in witness.sql for every shape the script handles, and make the script check its own preconditions." |
| `check-witness` | 5 | the agent | "Write one row into witness.sql that makes <CK> FALSE, then re-run." |
| `computed-witness` | 5 | the agent | "Write one row into witness.sql whose <column> expression fails, then re-run." |
| `planter-needs-row` | 5 | the agent | "Cannot make a valid row for <T> (<reason>). Write one valid INSERT for <T> into witness.sql under `-- plants: <T>`." |
| `acceptance` | 5 | the agent | "The copy answers <question> <yes\|no>, but <env> answered <no\|yes>. Fix witness.sql." |
| `identity` | 5 | the agent | "witness.sql changed the schema (<object>). Witness files are DML only." |
| `in-flight` | 5 | the developer | "<T> is in an open two-release window (estate/in-flight.md). Hold this change, or make it the window's next release." |
| `config` | 6 | the developer | "facsimile.json: <json path>: <problem>." |
| `toolchain` | 6 | the developer | "<tool> not found. <how to get it>." |
| `platform` | 6 | the developer | "LocalDB <v> is older than the project's target <DSP>. Install LocalDB 2022 or later, or set `allowOlderLocalDb` (stamped on every record)." |
| `options-unknown` | 6 | the principal | "No captured effective options. Capture the Octopus step's options into facsimile/options.publish.xml." |
| `build-failed` | 7 | the developer | "The project does not build: <first error>." |
| `unknown-statement` | 9 | the principal | "Delta line <n> reads rows, but no vocabulary row matches it. Add a row with its case." |
| `unclassified` | 9 | the principal | "The publish ended with <message> that no rule classifies. Add a rule with its case." |
| `cdc` | 9 | the principal | "<T> is tracked by CDC, and LocalDB has none. This change is principal-led." |
| `base-unpublishable` | 9 | the principal | "<env>'s schema does not publish to LocalDB (<feature>). Record it as Not checked." |

## 7. Tier and flags

The tier is a pure function of the questions, so it is identical in every environment and on every
machine:

| Tier | Holds when | What it costs |
|---|---|---|
| `T0 additive` | there are no row-reading statements | a clean publish of head onto the copy |
| `T1 presence` | the only questions are `HasRows` | one count, from `sys.partitions` |
| `T2 validation` | any of `NotNull`, `Fits`, `Unique`, `Orphan`, `CheckFalse`, `KeyLength`, `Rebuild`, `Computed` | counts, witnesses, and the proof |
| `T3 authored` | any `Authored` | look first (shape), then T2 |

**Flags:**
- `scale`: a touched table has 1M rows or more and the delta builds an index, rebuilds a table,
  alters a column, or validates a constraint on it;
- `cdc`;
- `unread:<env>`: environments with no answers, such as Prod;
- `in-flight`;
- `older-localdb`.

A tiered risk harness consumes `tier` and `flags` from `verdict.<env>.json` and decides which further
checks to run. The facsimile assigns the tier from the delta, never from the operation's name, so
the tier cannot be argued with.

## 8. Verbs and config

| Verb | Touches the environment | What it does |
|---|---|---|
| `facsimile doctor` | no (optional reachability) | tools, versions, LocalDB vs DSP, language mode, captured options, config |
| `facsimile read <env>` | yes, twice | extract, script offline, questions, counts. Writes `delta.<env>.sql` and `answers.<env>.json` |
| `facsimile prove [<env>…]` | no | copy, plant, accept, check identity, publish, classify. Writes `verdict.<env>.json`. CI runs this |
| `facsimile prove --assume <env>=worst` | no | for an environment with no answers (Prod): proves both worlds, populated-and-clean and populated-and-violating. Records the message the violating world would hit and the probe to run before promoting. The schema comes from the nearest read environment, and the verdict is stamped `assumed` |
| `facsimile shape <env> <s.t.c>` | yes (SELECT) | the value-free discovery profile for one column |
| `facsimile up [--fresh]` | no | the daily local database (repository schema, reference data, k presence rows per table), converged |
| `facsimile status` | no | the facsimile databases, `up` freshness, cached schemas and their age, answer staleness |
| `facsimile down [--all]` | no | drop the `fx_` databases (`--all` also clears caches) |
| `facsimile init` | no | scaffold `facsimile.json` |

`facsimile.json` is a closed schema. An unknown key is refused by its JSON path, and a secret is
accepted only as an `env:` reference:

```jsonc
{
  "project": "Estate.sqlproj",
  "options": "facsimile/options.publish.xml",     // the captured EFFECTIVE Octopus options
  "sqlcmd": { "SomeVar": "value" },               // the captured per-pipeline SQLCMD values (non-secret)
  "referenceData": ["ReferenceData/*.sql"],       // the seed lanes run on every copy (Twin parity #4)
  "localdb": "MSSQLLocalDB",
  "environments": {
    "Dev":  { "server": "dev-sql",  "database": "Estate" },
    "QA":   { "server": "qa-sql",   "database": "Estate" },
    "UAT":  { "server": "uat-sql",  "database": "Estate" },
    "Prod": { "server": "prod-sql", "database": "Estate", "read": "none" }
  },
  "up": { "rowsPerTable": 3 }
}
```

**What one change commits** (`proofs/<branch-slug>/`, written by the tool and reviewed in the PR):

- `delta.<env>.sql`;
- `answers.<env>.json`, stamped with login, time, delta hash and probe text;
- `witness.sql`, if any;
- `verdict.<env>.json`.

**What is never committed:** the caches under `.facsimile/`, meaning schema dacpacs, base backups,
and head builds.

## 9. How every invariant is attended to

1. **Traceability is generated, not hoped for.** `tests/run-cases.ps1 --lint` refuses a catalog in
   which any of these is missing:
   - an invariant without two cases;
   - a case without an invariant;
   - a vocabulary row without a `corpus` case;
   - a stop kind without a case;
   - a Twin responsibility without a parity or retirement case;
   - an operation without its four standard cases (empty, populated-clean, populated-violating per
     class, authored remedy where one applies).
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
