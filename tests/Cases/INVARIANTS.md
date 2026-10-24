# The Facsimile: invariants

*What must be true of the new edition, written before any implementation. Each law has:*

- *an **ID**;*
- *a **statement**;*
- *the **mechanism** that holds it;*
- *the **property** its cases test.*

*The cases live beside this file. `90-traceability.md` maps every law to its cases, and
the test project's lint (`../../archive/v2/ssdt-agent/facsimile/TARGET.md` §7) refuses a law with fewer than two.*

*Vocabulary from `ARCHITECTURE.md` (§4 questions, §6 outcomes) is used without restating it.*

---

## F — Fidelity: the copy agrees with the environment

**F1 · Answer agreement.** For every question q derived from the delta, the copy's boolean answer
equals the environment's: `populated(copy,q) = populated(env,q)` and
`violating(copy,q, class) = violating(env,q, class)` for every value class. This includes every
"no".
*Mechanism:* the acceptance step re-runs the same probes against the copy (`Test-FxAcceptance`).
*Property:* for every generated (kind × class × populated) cell, plant, then re-count, then compare.
The case passes only if every cell agrees, and fails with `stop acceptance` when a witness is
deliberately missing or extra.

**F2 · Shape identity.** After planting, the copy's schema equals the environment's: scripting
`schema.<env>.dacpac` against the copy under the effective options yields an empty plan.
*Mechanism:* the identity step. `witness.sql` is DML only.
*Property:* the plan is empty for every corpus case. A witness file containing any DDL yields
`stop identity`.

**F3 · Verdict agreement (the differential law).** For every corpus fixture,
`verdict(facsimile(env)) = verdict(publish(head → full copy of env))`. The verdict class, the
message number and the named object must all match.
*Mechanism:* F1 + F2 + F6, and the fact that the guards are existence-sensitive.
*Property:* the runner executes both arms for every `corpus` case that reaches a verdict.
Disagreement fails the case whatever the hand-written expectation says.
*Scope:*
- **Designed stops.** A case whose facsimile outcome is a designed stop (`cdc`, `platform`,
  `base-unpublishable`, `check-witness`, `computed-witness`, `authored-script`) asserts the stop. The
  oracle's verdict is recorded for information only.
- **Literal-keyed remedies.** An authored remedy keyed to a literal value is not total. The
  facsimile's synthetic witness stays unfixed and blocks while the oracle publishes clean. That
  disagreement is the *intended* finding, and the case asserts the doctrine line (ARCH §4).

**F4 · Question totality.** Every statement in the delta falls into exactly one class (ARCH §4):
- **data-sensitive,** which maps to exactly one question kind;
- **data-insensitive,** a `Scan`: no question, but its tables' `Rows` are measured;
- **non-reading.**

A statement in no class stops `unknown-statement`. Nothing that reads existing rows is silently
ignored.
*Mechanism:* `Get-FxQuestions` classifies every batch. It reads the delta together with the
environment's column catalog, so a narrowing is told from a widening and a NULL→NOT NULL from a
restated NOT NULL.
*Property:* every statement shape DacFx emits in the corpus (scanned from the generated deltas) is
either matched or produces the stop. A synthetic unknown shape always stops.

**F5 · Offline delta equivalence.** `Script(head, extracted schema)` and `Script(head, live env)`
are identical, ignoring the header, `:setvar` lines and the database name.
*Mechanism:* Extract, then a package-to-package Script.
*Property:* for every corpus fixture, both scripts are generated and compared after normalization.
(Receipt: identical on SQL Server 2022 with sqlpackage 170.5.96, 2026-10-15, TWIN_SUCCESSION §0.1.)

**F6 · Effective options.** Every proof publish uses exactly the captured effective pipeline
options (profile, plus overrides, plus SQLCMD values). The Permissive publish differs from Strict
in `BlockOnPossibleDataLoss` alone.
*Mechanism:* one options file, loaded once. Permissive is derived from it by a single override.
*Property:* the effective option set the publish received is recorded in the verdict and equals the
captured set. A profile that disables `BlockOnPossibleDataLoss` is refused with
`stop options-unknown`.

**F7 · Per-environment proof.** Each environment is proven on its own extracted schema and its own
answers. Two environments may share one proof only when their delta hash and answers hash are both
equal. Answers are never unioned across environments.
*Mechanism:* proofs are keyed by `(delta hash, answers hash)`.
*Property:* environments with differing answers produce separate verdicts. Environments with
identical inputs produce one shared proof, cited by both.

**F8 · Final-target questions.** In a multi-release program, the questions derive from the final
target (the last release's head), and every release is proven in sequence on one copy: R1, R1
replayed, then R2.
*Mechanism:* `prove --program <R1> <R2>…`.
*Property:* an R1 whose own model diff is empty still plants the witnesses the R2 target needs. A
replay of R1 with a non-empty plan is reported as the F17 hazard.

**F9 · Metadata fidelity.** For every touched object, the copy matches the environment in five
things, or the run stops:
- constraint trust;
- enabled or disabled state;
- default-constraint names;
- column collation;
- compatibility level.

*Mechanism:* the extracted schema carries them, where it does (verify ARCH §11.1). Where it doesn't,
metadata witnesses reproduce them.
*Property:* environments holding an untrusted FK, a disabled FK, a system-named default, or a
non-default column collation each yield the same delta and verdict on the copy as on the
environment.

## H — Honesty: what the tool says is true

**H1 · Two outcomes.** A proving invocation (`prove`) ends in exactly one verdict or one stop. Every
other verb ends in exactly one `done` or one stop. Either way there is exactly one stdout line and
one JSON file (ARCH §6). There is no third outcome: no warning-only exit, no partial success.
*Property:* every case's outcome parses as exactly one of the two. Killing the process mid-run
leaves no file claiming a verdict.

**H2 · No silent clean.** `clean` is reported only when all five of these hold:
- the publish succeeded;
- every question has an answer;
- acceptance passed;
- identity passed;
- no statement was unknown.

Anything less is a stop.
*Property:* every one of those conditions, falsified alone, turns `clean` into a named stop.
`unmeasured` questions count as answered only when a declared witness covers them, and then the
record carries `declared-not-measured`.

**H3 · Actionable stop.** Every stop names one next step, addressed to the agent, citing the file,
table, column or command concerned (ARCH §6). It is never a bare error.
*Property:* for every stop kind, the message matches its template with every placeholder filled.

**H4 · Explicit residuals.** Every verdict carries a non-empty `notChecked` list, drawn from this
closed set of tokens:

| Token | What was not checked |
|---|---|
| `unread:<env>` | an environment with no answers |
| `assumed:<env>` | an environment whose verdict came from `--assume` |
| `volume` | timing, transaction-log size, CommandTimeout, and locks at volume |
| `cdc` | change data capture |
| `workload` | index usage and plans |
| `platform:<what>` | a difference between the copy's platform and the environment's |
| `drift-since-measure` | data changed between measurement and promotion |
| `engine-version` | the Octopus DacFx version |
| `integration-studio` | the external entity refresh |
| `external-consumers` | SSIS and other readers |
| `app-behaviour` | application insert paths |
| `declared-not-measured` | a question answered only by a declared witness |
| `tooling` | a break-glass merge |

A residual that does not apply is omitted, never left blank. A verdict with nothing to say still
says what it did not check.
*Property:* every verdict's `notChecked` is non-empty and drawn from the closed set.

**H5 · Classification by evidence.** A block is attributed to the data-loss guard only when the
failing batch quoted in SQL72045 matches a guard batch of this delta (`RAISERROR … 16, 127`),
compared token by token because SQL72045 reformats what it quotes. Any error raised inside a pre- or
post-deploy batch, whatever its number (a counterfeit 50000, a THROW, 207, 208, 2714, 3726, 13575),
is `blocked authored`. An unknown message number or shape in the generated main phase is
`stop unclassified`, never `clean`.
*Property:* a counterfeit `RAISERROR('…',16,127)` in a pre-deploy is classified as the authored
script, not the guard. An unknown number stops.

**H6 · Residue after a block.** After any block, the verdict records what the publish left applied,
by phase, in the order DacFx runs them:
1. pre-deploy;
2. main (transactional only with `IncludeTransactionalScripts`);
3. post-deploy;
4. constraint validation (`WITH CHECK CHECK`, outside the transaction, observed after post-deploy).

It is read by scripting head against the copy.
*Property:* a 1505 unique build leaves the index dropped and the column altered (receipt, red team
#4), and the residue lists both. A 547 at `WITH CHECK CHECK` lists every other applied change and
the untrusted constraint.

**H7 · Stamped.** Every verdict carries these stamps:
- the tool version;
- the sqlpackage/DacFx version;
- the LocalDB (or server) version and edition;
- the head commit and the merge-base commit;
- the server and database the answers were read from;
- hashes of the effective options, the extracted schema, the answers' *content*, `witness.sql` and
  both deltas;
- the login and time the answers were measured.

*Property:* every verdict contains every stamp. Changing any **input** changes the verdict's
identity hash. The login and the time are stamps but not inputs, so a re-read of unchanged data
keeps the identity, and F7 can share proofs.

## P — Privacy: nothing but structure and integers leaves an environment

**P1 · Only schema and integers leave.** The only bytes the tool carries out of an environment are:
- the extracted schema;
- integer answers;
- the shape profile's bounded structure: signatures and counts (P6).

The one exception is P6's `--vocabulary` literals, which stay on the machine (`.facsimile/out/`) and
are never committed.
*Property:* every `answers.<env>.json` validates against a schema whose leaves are integers,
booleans, identifiers, hashes and timestamps. Captured row values never appear. A planted canary
value in a stand-in environment never appears in any artifact.

**P2 · Templated probes only.** Every statement sent to an environment comes from
`sql/probes.sql` (or `sql/shape.sql`). Identifiers are validated against the environment with
`OBJECT_ID` or `COL_LENGTH`, then bracket-quoted. An identifier that cannot be validated safely
stops `identifier-unsafe`, and nothing is sent. No agent-written text is ever executed in an
environment.

The one exception is ARCH §4's **predicate exception.** A CHECK expression or a filtered-index
`WHERE`, copied verbatim from the delta's generated DDL, may be carried inside an integer-returning
count. If it contains `CONVERT`, `CAST` or `PARSE` it is refused, and the question is recorded
`unmeasured`.
*Property:* an injection attempt through a table, column or predicate name either validates as a
real identifier or stops. The probe text sent is recorded and is a template instance.

**P3 · Nothing that quotes a value.** Probes use `TRY_CONVERT` and `TRY_CAST`, never
`CONVERT`/`CAST`, whose errors quote the value. An environment's error *text* is never recorded,
only its number.
*Property:* a probe over a column holding an unconvertible value returns a count, and the recorded
error for a forced failure contains a number only.

**P4 · Read-only by construction.** Environment connections carry `ApplicationIntent=ReadOnly`, and
the tool sends no DML or DDL to an environment. Its only environment traffic is DacFx's Extract
(metadata reads) and the probe SELECTs. The delta is scripted offline, so Script sends nothing.
*Property:* run against a stand-in environment under a login with no write permission, every verb
succeeds. A trace of statements sent contains only SELECT and Extract's metadata reads. Whether
Extract also sends `xp_instance_regread`, as Script does, is a `verify` case.

**P5 · Synthetic witnesses.** Every planted value is either generated by the planter or defined by
the repository. No environment value ever reaches the copy.
- **Generated values** come from (table, column, ordinal, class). They carry the `w` marker wherever
  the type, the width and the class allow it.
- **Where the marker cannot fit,** the value comes from the planter's fixed, documented per-class
  table: an empty string, an ambiguous day-first date such as `03/04/2001` (convertible, but to the
  wrong date under a month-first format), a numeric overflow, a `char(1)`.
- **Repository-defined values** are any literal the repository's model defines: a seed's code, a
  default, a key from reference data, a literal inside a CHECK predicate. They are used where a
  question requires equality with one, or where a constraint forces one.
*Property:* a canary value present in the stand-in environment is absent from the copy.

**P6 · Bounded discovery.** The `shape` verb returns structure only: signatures, lengths, counts,
and distinct-as-collated vs distinct-binary.
- **Every character becomes a class symbol.** ASCII letters become `a`/`A`, digits `9`, spaces `_`,
  and every other non-punctuation character `x`, so no non-Latin text passes through verbatim.
- **Rare signatures are summarized, not hidden.** A signature seen fewer than k times (default 5)
  is folded into "other: n rows in m signatures", so the reader knows a rare shape exists without
  seeing it.
- **Literal values are bounded.** They are returned only with `--vocabulary`, only for a column with
  50 or fewer distinct values, and only for values each seen at least k times.
- **Confirmation counts are not suppressed.** They are integers.

*Property:*
- `shape` on a high-cardinality email column returns no literal;
- a non-Latin name column returns class symbols only;
- a singleton signature appears only inside "other";
- `--vocabulary` on a 4-value status column with every value seen 5 or more times returns the four
  values;
- a 3-row table's name column returns none.

## S — Safety of the copy itself

**S1 · Disposable only.** Every publish targets a database the tool created, named `fx_*` and
registered in this run, or `fx_local` for `up`. Any other target is refused.
*Property:* pointing a publish at a pre-existing non-`fx_` database exits `stop config` without
connecting for a write.

**S2 · Never re-publish the base after planting.** The base is published once per cache. Planting
happens only after restore.
*Property:* the pipeline's step trace never shows a base publish after a plant. (Receipt, red team
#9: a re-publish erases witnesses.)

**S3 · A missing object is a stop, never zero.** Coordinates resolve in ARCH §4's order:
1. created by this delta, so answered from the delta;
2. renamed or moved by it, so probed under the old name;
3. present in the environment, so probed.

A coordinate that is none of these stops `missing-object`. It is never read as "empty".
*Property:*
- an answers file reused against an environment lacking a column it names produces the stop, not
  `populated=false`;
- a unique index on a column the same delta adds is answered from the delta (its default) and
  never stops;
- a constraint on a column renamed earlier in the delta is probed under the old name.

**S4 · Platform.** The LocalDB major version must be at least the project's DSP. Otherwise the run
stops `platform`, unless `allowOlderLocalDb` is set, in which case every verdict is stamped
`older-localdb` and the platform residual is added.
*Property:* a Sql160 project on LocalDB 15 stops. With the override, the verdict carries the flag.

**S5 · Clean slate.** Every proof starts from a fresh restore of the base cache. No state leaks
between proofs, environments or cases.
*Property:* proving the same fixture after a dirty previous run (a block leaves residue) gives an
identical verdict.

**S6 · Reapable.** The tool keeps a registry of the databases it created. `down` removes every
registered proof copy and keeps `fx_local`. `down --all` also removes `fx_local` and the caches. A
crash leaves only registered databases and cache files, all reapable. A database not in the
registry is never dropped, whatever its name.
*Property:*
- after a killed run, `down` leaves zero registered proof copies;
- a person's own database named `fx_mine` survives `down --all`;
- nothing else on the instance is touched.

## D — Determinism and statelessness

**D1 · Deterministic.** The same inputs (repository commit, extracted schema, answers,
`witness.sql`, tool version) produce a byte-identical witness plan, planted rows, delta and
verdict, apart from the timestamp field.
*Property:* every corpus case runs twice and the artifacts are compared.

**D2 · Stateless.** There is no persistent state beyond content-keyed caches. Deleting every cache
changes run time and nothing else.
*Property:* a case run with a warm cache and with no cache gives identical verdicts.

**D3 · S-stable planting.** A generated literal is a pure function of (table, column, ordinal,
type, width, value class). Adding, removing or retyping one column changes only that column's
planted values.

The values that depend on the copy are outside D3 but inside D1:
- keys resolved from the copy (reused parents, orphan keys beyond the parent's maximum);
- system-generated period columns;
- identity values.

*Property:* plan two schemas that differ in one column. Every other column's generated literals are
identical.

**D4 · Convergent `up`.** After `up`, a second `up` with unchanged inputs does nothing ("Nothing to
apply."). Any input change makes `up` re-converge.
*Property:* `up`, `up` is a no-op. A schema edit followed by `up` re-converges. A reference-data
edit followed by `up` re-converges.

**D5 · Cache soundness.** A cache key is the hash of every input that shaped the cache:
- the schema;
- the merge-base reference data;
- the effective options;
- the tool version;
- the DacFx version;
- the LocalDB version.

A changed input always misses. Two runs missing the same key at once do not collide (a lock, and
unique names).
*Property:* each input is changed in isolation, and each change misses. Two concurrent misses
produce one cache and two valid proofs.

**D6 · Silence.** A change that published clean, published again onto the same copy, is silent:
- the plan is empty;
- the rows affected are zero (a guarded MERGE writes nothing);
- the hash of every touched and reference table is unchanged.

Otherwise the record carries `not-silent` with the evidence. This is the Twin's and v2's
CDC-silence guarantee, kept at the copy's grain.
*Property:* every clean corpus case is replayed. A deliberately unguarded MERGE, an R1 replay, and a
same-value rewrite (caught by rows-affected, not by the hash) each produce `not-silent`.

## M — Minimality: lean by law

**M1 · Necessity.** Removing any vocabulary row, witness kind, stop kind, or step of §5 makes at
least one case fail.
*Mechanism:* the mutation pass (ARCH §9.4).
*Property:* for each item, delete it, run the suite, and require at least one failure.

**M2 · Witness minimality.** Outside `up`, the *generated* planted set is the minimal set satisfying
F1. For each populated touched table, it plants the maximum of that table's needs, not their sum.
A violation row also serves as presence. A `dups` or `nulls` class, or a gapped identity, needs two
rows. Each table also gets its required FK ancestors and links.

Rows declared in `witness.sql` are added on top. They are counted and reported separately, never
mistaken for generated minimality.
*Property:* the generated row count equals the count in the resolved plan `plant.sql` reports.
Removing any generated row breaks F1. The declared rows appear in the record by line.

**M3 · Two touches.** `read` makes exactly two round-trips per environment: one Extract and one
batched count. `prove` makes none.
*Property:* a connection trace for `read` shows two sessions. One for `prove` shows zero
environment sessions.

**M4 · Budgets.** Each file stays within its line budget in `../../archive/v2/ssdt-agent/facsimile/TARGET.md` §6.
*Property:* a line-count check in CI.

## E — Evolution: the tool grows only through reviewed rows and cases

**E1 · The ratchet.** No vocabulary row, classifier rule or stop kind exists without a case that
fails when it is removed (M1). A new DacFx statement shape enters the tool only as a new vocabulary
row added with its corpus case.
*Property:* the lint checks the row ↔ case links.

**E2 · Pin discipline.** Changing the pinned sqlpackage/DacFx version requires the full corpus to be
green on the new version first. Every verdict is stamped with the version (H7).
*Property:* the corpus workflow runs on the pin bump PR, and a verdict without a version stamp is
invalid.

**E3 · Proof memory.** Every schema-changing PR commits `proofs/<slug>/` (ARCH §8) containing:
- the extracted schema;
- both deltas;
- the answers;
- the witnesses;
- the verdicts.

CI refuses a schema PR whose answers are missing, or keyed to a different delta hash or head
commit, or which name a server other than the configured environment's.
*Property:* a PR with a schema edit and no proofs fails. A PR whose proofs predate its last schema
edit fails as stale.

**E4 · Closed config.** `facsimile.json` refuses an unknown key by its JSON path, and accepts a
secret only as an `env:` or `file:` reference. All problems are reported in one pass. (Twin laws D9
and the closed schema, kept, with both of the Twin's reference forms.)
*Property:* unknown keys at every nesting level, inline secrets, and multiple simultaneous errors
are each refused, all named.

## A — Agent ergonomics: a weak agent on the weakest surface succeeds

**A1 · One line, one file.** stdout is exactly one line in ARCH §6's grammar:
- `VERDICT …`;
- `STOP <kind> …`;
- `DONE <verb> …`.

Each line ends `· <file> · exit <n>`. The JSON goes to the named file, and progress goes to stderr.
*Property:* every case's stdout matches the grammar. A multi-environment `prove` prints its most
severe outcome and names `verdicts.json`.

**A2 · Runs under the laptop's application-control policy.** Every verb runs on a team laptop as
the laptop check measures it, without elevation.
- **The C# realization** is a framework-dependent assembly loaded by the Microsoft-signed `dotnet`
  host. It starts no unsigned `.exe`, and carries no native code beyond SqlClient's own.
- **The PowerShell fallback** runs under ConstrainedLanguage mode: no `Add-Type`, no arbitrary .NET
  methods, no COM.
*Property:*
- the Windows CI job runs the tool as a standard user, through `dotnet` only;
- under the fallback, CI runs the suite with `__PSLockdownPolicy` forcing ConstrainedLanguage;
- the laptop check confirms both on a real machine (manual).

**A3 · Stable exits.** Exit codes mean who acts: 0 clean · 3 a finding · 4 can't reach ·
5 needs input · 6 machine not ready · 7 doesn't build · 9 needs the principal.
*Property:* each stop kind maps to exactly one exit code as listed in ARCH §6.

**A4 · Reading budget.** An agent proving one change needs to read at most 600 lines (2026-10-18) of the
knowledge layer: the router, one op skill with its Proof plan, the record form, and the proving guide (written at G4).
*Property:* a check over the files the router names for each op.

**A5 · Stops make progress.** Following a stop's next step literally changes the next run's outcome:
- it clears the stop;
- or it advances to a different stop;
- or it reaches a verdict.

*Property:* for every stop kind with an agent action, the case applies the instructed fix and
asserts the outcome moved.

## T — Twin parity: the team keeps working through the change

**T1 · Responsibility coverage.** Each of the 36 Twin responsibilities (ARCH §2) has either a parity
case (the facsimile does it) or a retirement case (no verdict depends on it).
*Property:* the lint checks the §2 rows against `80-twin-parity.md`.

**T2 · Golden parity.** Every fact in the Twin's nightly lane (`tests/Twin.Tests.Integration/SamplePr*Tests.fs`
and the four loop tests) reproduces under the facsimile with the same verdict.
*Property:* one `corpus` case per fact, with its source test cited.

**T3 · Workflow continuity.** `up`, `up --fresh`, `status`, `down`, `down --all` and `init` keep the
meaning `twin up / seed / status / down / reset / init` had for the team. Converge, reproduce,
report and remove all work without Docker:
- `down` keeps `fx_local` and its hand-written rows, as `twin down` kept the container's data;
- `down --all` removes them, as `twin reset` did;
- `up.sql` carries the pins that `twin.json` scenarios carried.
*Property:* each Twin verb's acceptance (THE_TWIN.md §7) is restated as a facsimile case and passes.

**T4 · Side by side.** During the G4 window, every schema PR records both the Twin's verdict and the
facsimile's. Any disagreement is a defect in one of them, investigated and resolved, never averaged.
*Property:* a process case. The PR template carries both lines during G4, and the lint refuses a G4
PR that has only one.
