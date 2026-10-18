# The Facsimile: invariants

*What must be true of the new edition, written before any implementation. Each law has:*

- *an **ID**;*
- *a **statement**;*
- *the **mechanism** that holds it;*
- *the **property** its cases test.*

*The cases live in `cases/`. `cases/90-traceability.md` maps every law to its cases, and
`tests/run-cases.ps1 --lint` refuses a law with fewer than two.*

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
*Property:* the runner executes both arms for every `corpus` case. Disagreement fails the case
whatever the hand-written expectation says.

**F4 · Question totality.** Every statement in the delta that reads existing rows maps to exactly
one vocabulary row, or the run stops `unknown-statement`. No row-reading statement is silently
ignored.
*Mechanism:* `Get-FxQuestions` classifies every batch as row-reading or not. A row-reading batch
with no match is a stop.
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

**H1 · Two outcomes.** Every invocation ends in exactly one of a verdict or a stop, with exactly one
stdout line and one JSON file. There is no third outcome: no warning-only exit, no partial success.
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

**H3 · Actionable stop.** Every stop names one next step, addressed to the agent, citing the file,
table, column or command concerned (ARCH §6). It is never a bare error.
*Property:* for every stop kind, the message matches its template with every placeholder filled.

**H4 · Explicit residuals.** Every verdict carries a non-empty `notChecked` list, drawn from a
closed set of residuals: unread environments, volume and timing, CDC, workload, platform
differences, change over time, the engine version, and the Integration Studio refresh. A residual
that does not apply is omitted, never left blank. A verdict with nothing to say still says what it
did not check.
*Property:* every verdict's `notChecked` is non-empty and drawn from the closed set.

**H5 · Classification by evidence.** A block is attributed to the data-loss guard only when the
failing batch quoted in SQL72045 is a guard batch of this delta (`RAISERROR … 16, 127`). Any other
Msg 50000 is reported as the authored script that raised it. An unknown message number or shape is
`stop unclassified`, never `clean`.
*Property:* a counterfeit `RAISERROR('…',16,127)` in a pre-deploy is classified as the authored
script, not the guard. An unknown number stops.

**H6 · Residue after a block.** After any block, the verdict records what the publish left applied,
by phase (pre-deploy, main, validation, post-deploy). It is read by scripting head against the
copy.
*Property:* a 1505 unique build leaves the index dropped and the column altered (receipt, red team
#4), and the residue lists both. A 547 at `WITH CHECK CHECK` lists every other applied change and
the untrusted constraint.

**H7 · Stamped.** Every verdict carries these stamps:
- the tool version;
- the sqlpackage/DacFx version;
- the LocalDB (or server) version and edition;
- hashes of the effective options, the extracted schema, the answers, `witness.sql` and the delta;
- the login and time the answers were measured.

*Property:* every verdict contains every stamp. Changing any stamped input changes the verdict's
identity hash.

## P — Privacy: nothing but structure and integers leaves an environment

**P1 · Only schema and integers leave.** The only bytes the tool carries out of an environment are
the extracted schema and integer answers.
*Property:* every `answers.<env>.json` validates against a schema whose leaves are integers,
booleans, identifiers, hashes and timestamps. Captured row values never appear. A planted canary
value in a stand-in environment never appears in any artifact.

**P2 · Templated probes only.** Every statement sent to an environment comes from
`sql/probes.sql` (or `sql/shape.sql`). Identifiers are validated against the environment with
`OBJECT_ID` or `COL_LENGTH`, then bracket-quoted. No agent-written or user-written text is ever
executed in an environment.
*Property:* an injection attempt through a table, column or predicate name either validates as a
real identifier or stops. The probe text sent is recorded and is a template instance.

**P3 · Nothing that quotes a value.** Probes use `TRY_CONVERT` and `TRY_CAST`, never
`CONVERT`/`CAST`, whose errors quote the value. An environment's error *text* is never recorded,
only its number.
*Property:* a probe over a column holding an unconvertible value returns a count, and the recorded
error for a forced failure contains a number only.

**P4 · Read-only by construction.** Environment connections carry `ApplicationIntent=ReadOnly`, and
the tool sends no DML or DDL to an environment. Its only environment statements are Extract,
Script, and the probe SELECTs.
*Property:* run against a stand-in environment under a login with no write permission, every verb
succeeds. A trace of statements sent contains only SELECT and Extract/Script metadata reads, plus
the single `xp_instance_regread` that DacFx's Script sends.

**P5 · Synthetic witnesses.** Every planted value is synthetic and recognizably so: string witnesses
carry the `w` prefix, and email-like columns use `@example.invalid`. No environment value reaches
the copy.
*Property:* a canary value present in the stand-in environment is absent from the copy.

**P6 · Bounded discovery.** The `shape` verb returns structure only: signatures, lengths, counts,
and distinct-as-collated vs distinct-binary. A signature seen fewer than k times is suppressed
(default k = 5). Literal values are returned only for columns with 50 or fewer distinct values, and
only with `--vocabulary`.
*Property:* `shape` on a high-cardinality email column returns no literal value. A singleton
signature is suppressed. `--vocabulary` on a 4-value status column returns the four values.

## S — Safety of the copy itself

**S1 · Disposable only.** Every publish targets a database the tool created, named `fx_*` and
registered in this run, or `fx_local` for `up`. Any other target is refused.
*Property:* pointing a publish at a pre-existing non-`fx_` database exits `stop config` without
connecting for a write.

**S2 · Never re-publish the base after planting.** The base is published once per cache. Planting
happens only after restore.
*Property:* the pipeline's step trace never shows a base publish after a plant. (Receipt, red team
#9: a re-publish erases witnesses.)

**S3 · A missing object is a stop, never zero.** If a question names an object the environment does
not have, the run stops `missing-object`. It never reads that answer as "empty".
*Property:* an environment lacking a column named by the delta produces the stop, not
`populated=false`.

**S4 · Platform.** The LocalDB major version must be at least the project's DSP. Otherwise the run
stops `platform`, unless `allowOlderLocalDb` is set, in which case every verdict is stamped
`older-localdb` and the platform residual is added.
*Property:* a Sql160 project on LocalDB 15 stops. With the override, the verdict carries the flag.

**S5 · Clean slate.** Every proof starts from a fresh restore of the base cache. No state leaks
between proofs, environments or cases.
*Property:* proving the same fixture after a dirty previous run (a block leaves residue) gives an
identical verdict.

**S6 · Reapable.** `down` removes every `fx_` database. A crash leaves only `fx_` databases and
cache files, all reapable.
*Property:* after a killed run, `down` leaves zero `fx_` databases. Nothing else on the instance is
touched.

## D — Determinism and statelessness

**D1 · Deterministic.** The same inputs (repository commit, extracted schema, answers,
`witness.sql`, tool version) produce a byte-identical witness plan, planted rows, delta and
verdict, apart from the timestamp field.
*Property:* every corpus case runs twice and the artifacts are compared.

**D2 · Stateless.** There is no persistent state beyond content-keyed caches. Deleting every cache
changes run time and nothing else.
*Property:* a case run with a warm cache and with no cache gives identical verdicts.

**D3 · S-stable planting.** A planted value is a pure function of (table, column, ordinal, type,
width). Adding, removing or retyping one column changes only that column's planted values.
*Property:* plan two schemas that differ in one column. Every other column's values are identical.

**D4 · Convergent `up`.** After `up`, a second `up` with unchanged inputs does nothing ("Nothing to
apply."). Any input change makes `up` re-converge.
*Property:* `up`, `up` is a no-op. A schema edit followed by `up` re-converges. A reference-data
edit followed by `up` re-converges.

**D5 · Cache soundness.** A cache key is the hash of every input that shaped the cache, so a changed
input always misses.
*Property:* each input of the base cache (schema, reference data, options) is changed in isolation,
and each change misses.

## M — Minimality: lean by law

**M1 · Necessity.** Removing any vocabulary row, witness kind, stop kind, or step of §5 makes at
least one case fail.
*Mechanism:* the mutation pass (ARCH §9.4).
*Property:* for each item, delete it, run the suite, and require at least one failure.

**M2 · Witness minimality.** Outside `up`, the planted set is the minimal set satisfying F1: one
presence row per populated touched table, plus its required FK ancestors, plus one row per
(violating question × class).
*Property:* the planted row count equals the plan's predicted count, and removing any planted row
breaks F1.

**M3 · Two touches.** `read` makes exactly two round-trips per environment: one Extract and one
batched count. `prove` makes none.
*Property:* a connection trace for `read` shows two sessions. One for `prove` shows zero
environment sessions.

**M4 · Budgets.** Each file stays within its line budget in ARCH §3.
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

**E3 · Proof memory.** Every schema-changing PR commits `proofs/<slug>/` containing its delta,
answers, witnesses and verdict. CI refuses a schema PR whose answers are missing or keyed to a
different delta hash.
*Property:* a PR with a schema edit and no proofs fails. A PR whose proofs predate its last schema
edit fails as stale.

**E4 · Closed config.** `facsimile.json` refuses an unknown key by its JSON path, and accepts a
secret only as an `env:` reference. All problems are reported in one pass. (Twin laws D9 and the
closed schema, kept.)
*Property:* unknown keys at every nesting level, inline secrets, and multiple simultaneous errors
are each refused, all named.

## A — Agent ergonomics: a weak agent on the weakest surface succeeds

**A1 · One line, one file.** stdout is exactly one line, `<OUTCOME> <reason> · <file> · exit <n>`.
The JSON goes to the named file, and progress goes to stderr.
*Property:* every case's stdout matches the grammar.

**A2 · ConstrainedLanguage-safe.** Every verb runs under PowerShell ConstrainedLanguage mode. It
uses no `Add-Type`, no arbitrary .NET methods, and no COM.
*Property:* CI runs the suite with `__PSLockdownPolicy` set to force ConstrainedLanguage.

**A3 · Stable exits.** Exit codes mean who acts: 0 clean · 3 a finding · 4 can't reach ·
5 needs input · 6 machine not ready · 7 doesn't build · 9 needs the principal.
*Property:* each stop kind maps to exactly one exit code as listed in ARCH §6.

**A4 · Reading budget.** An agent proving one change needs to read at most 600 lines of the
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
*Property:* the lint checks the §2 rows against `cases/80-twin-parity.md`.

**T2 · Golden parity.** Every fact in the Twin's nightly lane (`tests/Twin.Tests.Integration/SamplePr*Tests.fs`
and the four loop tests) reproduces under the facsimile with the same verdict.
*Property:* one `corpus` case per fact, with its source test cited.

**T3 · Workflow continuity.** `up`, `up --fresh`, `status`, `down`, `down --all` and `init` keep the
meaning `twin up / seed / status / down / reset / init` had for the team. Converge, reproduce,
report and remove all work without Docker.
*Property:* each Twin verb's acceptance (THE_TWIN.md §7) is restated as a facsimile case and passes.

**T4 · Side by side.** During the G4 window, every schema PR records both the Twin's verdict and the
facsimile's. Any disagreement is a defect in one of them, investigated and resolved, never averaged.
*Property:* a process case. The PR template carries both lines during G4, and the lint refuses a G4
PR that has only one.
