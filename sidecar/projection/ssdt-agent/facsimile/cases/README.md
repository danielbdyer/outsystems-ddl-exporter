# The case catalog: grammar, notation and families

*The facsimile is specified by these cases before it is built. Every case is written in one fixed
grammar, so that `tests/run-cases.ps1` can execute the catalog directly: the spec **is** the test
suite. Read `../ARCHITECTURE.md` (§4 questions, §5 the copy, §6 outcomes, §7 tier) and
`../INVARIANTS.md` first. This file defines only how a case is written.*

---

## 1. The grammar

A case is a level-3 heading followed by one fenced block whose info string is `case`:

````
### OP-make-mandatory-03 · populated, NULLs present, no remedy
```case
kind:     corpus
tags:     violating:null
env:      pg · Customer 6 rows · Email NULL×2
change:   head Customer.Email NULL→NOT NULL · scripts unchanged
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          questions  HasRows(Customer) · NotNull(Customer.Email)
          witnesses  presence(Customer) · null(Customer.Email)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · ships two releases (F4, F7) · notChecked {QA, UAT, Prod, volume}
laws:     F1 F3 H2 H5
source:   receipt · F7 · SamplePr "make OrderLine.Note mandatory: production publish blocks a populated table, applies when empty"
```
````

**The heading** is `### <ID> · <subject or situation>`. IDs are unique across the whole catalog. A
number may have two or three digits (`OP-narrow-07`, `TWN-214`).

**Fields.** A field is `name:` at column 0, padded to column 10. A continuation line is indented to
column 10. These are the fields, with the kinds that require them:

| Field | Required for | Meaning |
|---|---|---|
| `kind` | all | `pure` · `sql` · `corpus` · `process` · `manual` (§3) |
| `tags` | op cases | the situation, from the standard set (§5) |
| `input` | `pure` | the literal input to a pure function: delta text, publish output, answers, config JSON |
| `env` | `sql`, `corpus` | the stand-in environment: schema relative to `pg`, plus the data shapes that matter |
| `change` | `corpus` | the edit to head, and any authored scripts and `witness.sql` rows |
| `setup` | `process`, `manual` | the machine state |
| `action` | `sql`, `process`, `manual` | the command run, or the function called |
| `expect` | all | the sub-fields that apply: `answers`, `questions`, `measures`, `witnesses`, `plan`, `copy`, `outcome`, `record`, `flags`, `files`, `stdout`, `stderr`, `trace`, `exit`; for a multi-step case, `leg1`, `leg2`, … each holding its own sub-fields |
| `laws` | all | the invariant IDs this case witnesses (at least one) |
| `source` | all | `receipt` / `expected` / `verify`, plus citations (§4) |
| `note` | optional | one or two lines of why, when the expectation is not obvious. `note: n/a <tag> — <reason>` excuses a standard op situation that cannot occur (§5). A TWN case names its ARCH §2 row here, or in its title, as `row <n>` |

Write only what the case needs. Leave out sub-fields that carry nothing.

## 2. Notation

**Schemas.** `pg` is the proving-ground baseline (`../../proving-ground/Modules/*.sql`). Its tables are
named without `dbo.`:

| Table | Columns | Notes |
|---|---|---|
| `Account` | Id IDENTITY PK · Name nvarchar(100) NOT NULL · Region nvarchar(50) NULL | |
| `Category` | Id int PK (explicit, no IDENTITY) · Code nvarchar(30) NOT NULL · IsActive bit NOT NULL DF 1 | a lookup |
| `Customer` | Id IDENTITY PK · Name nvarchar(100) NOT NULL · Email nvarchar(256) NULL · ContactPhone nvarchar(40) NULL · Region nvarchar(50) NULL · AccountId int NULL | |
| `CustomerAddress` | Id IDENTITY PK · CustomerId int NOT NULL · Line1 nvarchar(120) NOT NULL · City nvarchar(80) NULL · PostalCode nvarchar(20) NULL | |
| `Order` | Id IDENTITY PK · CustomerId int NOT NULL · StatusId int NOT NULL · Total decimal(18,2) NOT NULL DF 0 · StatusText nvarchar(20) NOT NULL DF 'Pending' | `[Order]` |
| `OrderLine` | Id IDENTITY PK · OrderId int NOT NULL · LineNumber int NOT NULL · Amount decimal(18,2) NOT NULL DF 0 | |
| `Product` | Id IDENTITY PK · Code nvarchar(50) NOT NULL · Name nvarchar(200) NULL · LegacyCode nvarchar(40) NOT NULL DF 'LEGACY' · CategoryId int NULL | |
| `Status` | Id int PK (explicit) · Code nvarchar(20) NOT NULL · IsActive bit NOT NULL DF 1 | a lookup; `UIX_Status_Code` UNIQUE on Code |

`pg` carries no foreign keys. Cases add them in `env` (the environment already has them) or in
`change` (the change adds them). Reference data (`Status`, `Category`) arrives through the
`referenceData` lane, at the merge-base version, unless the case says otherwise.

`tw` is `pg` as the Twin's nightly lane stages it (`tests/Twin.Tests.Integration/SamplePrSupport.fs`):
the same schema, minted and seeded by the Twin. It is used only by TWN cases that restate a Twin
fact.

**Environment data.** `Customer 6 rows · Email NULL×2 · ''×1`: state only the shapes that matter. A
table not mentioned is empty. `Customer 0 rows` states emptiness explicitly when it matters.

**Answers** (what `read` measures):

| Form | Meaning |
|---|---|
| `HasRows(T)=yes\|no` | the table holds rows, or not |
| `NotNull(T.c)=clean\|violating` | |
| `Fits(T.c→type)={overlen, overbytes, nonconvertible, roundtrip, empty, overflow}` | list only the classes present; `{}` means clean |
| `Unique(T,(c1,c2)[,filter])={exact, case, accent, trailing, nulls}` | likewise |
| `Orphan(T.(c)→P)=clean\|violating` | |
| `CheckFalse(T,CK)=clean\|violating\|unmeasured` | `unmeasured` when the predicate exception refuses the probe (P2/P3) |
| `KeyLength(T,IX)=clean\|violating` | |
| `Gapped(T)=yes\|no` | |
| `Trust(T,X)=trusted\|untrusted\|disabled` | X is any FK or CHECK constraint |
| `Rows(T)=n` | when the scale flag matters |
| `Cdc(T)=yes` | |
| `Collation(T.c)=<name>` | when it matters |

**Witnesses:**

- `presence(T)` (+ancestors `P1, P2`);
- `null(T.c)`;
- `misfit(T.c, class)`;
- `dup(T,(c), class)`;
- `orphan(T.(c)→P)`;
- `longkey(T, IX)`;
- `gapped(T)`;
- `trust(T, FK, untrusted|disabled)`;
- `declared(<what>)` for a `witness.sql` row;
- `none`.

**Outcomes:**

- `verdict clean · exit 0`
- `verdict blocked <reason> · Msg <n>[/<state>] · <object> · exit 3`, where the reason is one of: guard,
  constraint, null, unique, truncation, overflow, conversion, seed, keylength, cdc, collation
- `verdict refused-at-build · SQL71516 · <object> · exit 3`
- `verdict blocked authored · Msg <n> · <pre-deploy\|post-deploy> · exit 3`, for any error raised inside an
  authored batch
- `stop <kind> · "<gist of the next step>" · exit <n>`, where the kind is one from ARCH §6
- `done <verb> · <what happened> · exit 0`, for a non-proving verb that succeeded (`read`, `shape`,
  `up`, `status`, `down`, `init`, `doctor`)

**Record:**

- `tier T0|T1|T2|T3`
- `flags {scale, cdc, unread:<env>, in-flight, older-localdb}`
- `ships <how it ships>`
- `effects {adds, modifies, removes, renames}`
- `residue {…}` after a block
- `notChecked {…}`, using INVARIANTS H4's closed tokens
- `silence ok|not-silent(<evidence>)`
- `warnings {…}`
- `lag:<env> {…}` and `drift:<env> {…}`

## 3. Kinds, and how the runner executes each

| Kind | Needs | The runner does |
|---|---|---|
| `pure` | nothing | calls the pure function named in `action` (or inferred from `expect`) on `input`, and compares |
| `sql` | SQL Server | builds the `env` database, runs `action` (a probe, the planter, `shape`, `hash`), and compares |
| `corpus` | SQL Server | builds the stand-in **env** database from the fixture and runs `read` against it under a read-only login, then `prove`. In parallel, as **the oracle**, it restores a full copy of env and publishes head onto it directly. It requires facsimile verdict = oracle verdict (F3) **and** = the `expect` line |
| `process` | the tool and a file system | runs verbs and checks files, stdout, exit codes and the lint |
| `manual` | the corporate machine (Windows, LocalDB, Visual Studio, Octopus, a real environment) | a checklist item for the laptop check and the G4 window. The runner lists these and does not execute them |

A `corpus` case may end in a **stop**. When the stop is a designed one (INVARIANTS F3 *Scope*), the
case asserts the stop, and the oracle's verdict is recorded for information only.

When the oracle and the `expect` line disagree but the oracle and the facsimile agree, the
*expectation* was wrong. The case is corrected and its source becomes `receipt`. When the facsimile
and the oracle disagree, it is a defect, whatever the expectation says.

**Fixtures.** A `corpus` case describes its fixture in `env` and `change`. At G2 each case
materializes as `cases/fixtures/<ID>/`, in one of two styles:

- **extract style,** for declarative changes: `env.sql` (schema plus data) and `head.sql`
  (schema). Both dacpacs are extracted from scratch databases.
- **project style,** for changes with authored scripts: a copy of `../../proving-ground/` with the
  case's edits.

The catalog stays the source of truth, and a fixture is regenerated from it.

## 4. Sources

| Marker | Meaning | Cite |
|---|---|---|
| `receipt` | the expectation is backed by a recorded live proof | one of: `FINDINGS_AND_CHANGES.md`, any part (F1–F20, Part 6, Part 7); a Twin test by its exact name (`SamplePr "…"`, `TwinMintLoopTests "…"`); a sample PR's recorded live proof (`sample-prs/<slug>.md`, "What proving showed"); the 2026-10-15 boundary experiment (`TWIN_SUCCESSION §6b/§0.1`); the red team (`red-team #n`, TWIN_SUCCESSION Appendix A); a measured fact in the v3 plan (`V3_MILESTONES §1 fact n`) |
| `expected` | derived from documented engine behavior or the skill tree, but not yet witnessed live here | the skill (`skills/op/<slug>`, `_index/<x>`) or the Microsoft doc |
| `verify` | genuinely uncertain; the first run decides, and the case is then updated with its receipt | what is uncertain and why |

Never mark a case `receipt` without a citation that actually records it. When in doubt, it is
`expected`.

## 5. The standard op set (`tags`)

Each of the 45 operations (`../../skills/op/`) has at least these situations, each as its own case
whenever the operation can be in that situation:

| Tag | Situation |
|---|---|
| `empty` | the touched table holds no rows in the environment |
| `clean` | populated, with no violating rows |
| `violating:<class>` | populated, with one case per value class the operation's questions have (§4 of ARCH) |
| `remedy` | the authored fix (pre-deploy reconcile, or the two-release R1 → R2 program): first the `authored-script` stop, then with declared witnesses, the verdict |
| `edge` | anything the op skill, the findings or the red team names as a trap: collation twins, trust state, drift, CDC, the scale flag, a missing refactorlog, and so on |
| `refuse` | an operation the facsimile refuses or routes (e.g. rebuild-index has no declarative delta) |

Where a situation cannot occur (an empty table cannot be violating; create-entity has no existing
table), say so in a `note` on the nearest case instead of inventing one.

## 6. The families

| File | Prefix | Covers |
|---|---|---|
| `00-invariants.md` | `INV-<law>-nn` | property cases for laws not naturally covered elsewhere (D, M, S, H1, H7, E, A) |
| `10-questions.md` | `QST-nn` | `Get-FxQuestions`: every vocabulary row, every statement shape, unknown statements, the authored detection |
| `20-planter.md` | `PLT-nn` | the planter: every SQL Server type, every structural feature, ancestors, value-class witnesses, acceptance, S-stable |
| `30-outcomes.md` | `CLS-nn`, `TIER-nn`, `REC-nn` | `Get-FxOutcome`, `Get-FxTier`, `Get-FxRecord` |
| `40-ops-columns.md` | `OP-<slug>-nn` | columns, tightening, defaults, renames, identity |
| `41-ops-keys.md` | `OP-<slug>-nn` | constraints, keys, indexes |
| `42-ops-tables.md` | `OP-<slug>-nn` | tables, seeds, structural and multi-phase ops |
| `43-compound.md` | `CMP-nn` | release-grain molecules: additive batch, atomic veto, rename then tighten, programs |
| `50-environments.md` | `ENV-nn` | access, reachability, drift, per-environment proof, two-release state, collation, Prod |
| `60-privacy.md` | `PRV-nn` | the P laws, attacked |
| `70-process.md` | `CLI-nn`, `UP-nn`, `CFG-nn`, `CI-nn`, `TLC-nn` | verbs, `up`, config, CI, toolchain, the agent surface |
| `80-twin-parity.md` | `TWN-nn` | every Twin responsibility (ARCH §2) and every Twin integration fact: parity or retirement |
| `90-traceability.md` | — | law → cases, op → cases, vocabulary row → cases, stop → cases, Twin row → cases, plus the audit findings |
