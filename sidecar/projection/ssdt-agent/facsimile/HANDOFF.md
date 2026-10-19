# Handoff: bringing the facsimile into v3

*2026-10-19. To the agent who brings this directory into v3: the `dbchange` tree on
`origin/claude/v3-build`. From the session that specified it (PR #706, branch
`claude/twin-rearchitecture-strategy-hq5lry`). In this letter, v3's files are written from v3's root,
as `<v3>/NEXT.md`. Paths without the prefix are in this repository.*

---

## What you are carrying

You are carrying a specification, written test-first, of how to prove a schema change against an
environment's existing data without copying any of the environment's rows. There is no code. The
finding behind it fits in four sentences:

> DacFx asks the data only two things when it publishes: *is there a row?* and *is there a row
> that breaks this rule?* The guard is `IF EXISTS (select top 1 1 …) RAISERROR(…,16,127)`, and
> constraint validation fails on the first violating row. So a copy holding one synthetic witness
> row for every "yes" gets the same verdict as the environment. Read the environment twice (its
> schema, then one batched count per question), plant the witnesses in a local copy of that schema,
> and let DacFx publish the change onto it.

The receipts are in `../../../../TWIN_SUCCESSION.md`. §0.1 shows that the offline delta equals the
live delta, guard included, on SQL Server 2022 with DacFx 170.5.96. §6b shows what a VIEW
DEFINITION-only login can and cannot read.

**v3 already has this idea and names it.** `ExistingData` is the facsimile's answers for one
environment. M3's *existing-data tier* is the facsimile. WP 3.3 already states its law: "given an
`ExistingData` it realizes that existing data and nothing more." What this directory adds is:

- the exact list of questions a delta asks;
- how one witness row is made for every type and structure;
- how a publish is classified;
- a catalog of cases that holds all three in place.

## What changed after the spec was written

1. **v3 is the home.** `../../../../TWIN_SUCCESSION.md` §9 says to tag v3 as a donor and close it.
   That was written when PowerShell was the realization, and it is superseded. The operator settled
   the realization gate on 2026-10-18: the team already builds C# and already runs MCP servers in
   Copilot.
2. **Inside v3, v3's decisions win** wherever v3 has already decided. `TARGET.md` was written for a
   standalone `facsimile` tool, so some of its choices do not carry over:

   | `TARGET.md` chose | v3 already decided |
   |---|---|
   | `net8.0` | `net10.0` |
   | its own two-project layout | `kernel` / `io` / `cli` |
   | its own exit codes | the frozen exit table |
   | its own database names | v3's copy registry |
   | its own vocabulary | v3's names |

   Keep `TARGET.md` for what v3 has not decided: the NFR table (§6), the MCP surface (§8), the
   carried constraints (§9), and the realization-bound cases (Appendix B).
3. **The case catalog and the laws are realization-neutral.** They move unchanged. Only their
   addresses change.

## Settled: do not reopen

| Decision | Settled by | Date |
|---|---|---|
| C#, not PowerShell. Appendix A of `TARGET.md` is a record, not a fallback in waiting | the operator | 2026-10-18 |
| MCP is a first-class surface, with the CLI as the floor. This answers `<v3>/V3_INSTRUCTION_ARCHITECTURE.md` §9.3's open question: the organization's MCP policy is enabled, so `dbchange serve --mcp` can be built | the operator | 2026-10-18 |
| Team laptops run LocalDB 16.x (SQL Server 2022). That meets a Sql160 project (law S4). The estate's network has no Docker, so LocalDB is the local server there, and a configured instance serves CI and the sandbox. v3's latest `io/LocalServer` already turns to LocalDB only when nothing else is chosen | the operator | 2026-10-19 |
| Access: developers read Dev; dev leads read QA and UAT, so a lead's review extends the proof; nobody reads Prod. Prod is proved with `--assume Prod=worst` until a VIEW DEFINITION-only login exists. A data-reader-only login is refused by Extract and Script | the operator | 2026-10-18 |
| Two principals keep the tool, one on the team and one on the supporting team. They are deliberately not named in any repository. Break-glass is the on-team principal plus a dev lead | the operator | 2026-10-18 |
| The Twin stays in service until parity (G3) and one release cycle side by side (G4). `ARCHITECTURE.md` §10 has the gates | this spec | 2026-10-18 |

Assume the person who commissioned this may not be reachable for long. Everything above is written
so that the principals can carry it without them.

## The one decision to put to the operator first

M3 plans three tiers (existing-data, shape and scale) and ports v2's generator into
`<v3>/kernel/Synth.cs`. At the head of `origin/claude/v3-build` (`81beef29`, 2026-09-26),
`<v3>/ci/budgets.json` plans about 4,840 lines for M3 and 1,800 for `Synth.cs` alone. The facsimile's evidence is that **a proof needs only the existing-data
tier, and that tier is made by a planter of witness rows, not by a generator**. The facsimile's
whole production budget on 2026-10-18 was ≤ 1,900 lines, and v3 already holds part of it:
`io/LocalServer`, `io/DacFx`, `io/SqlServer.Measure`, `io/TSql` and `kernel/DacFxMessage`.

The shape tier was buying two things, and both have homes without it:

- **Statements a person wrote** (pre- and post-deploy scripts, seeds) are where looking is needed.
  The facsimile looks with a value-free shape profile: `TRANSLATE` signatures,
  `../../../../TWIN_SUCCESSION.md` Appendix D. The look ends in witnesses the developer declares.
- **Scale and timing** are a residual flag on the record, never a verdict.

**My recommendation:**

1. Build the existing-data tier as the facsimile's planter, behind WP 3.3's existing interface.
2. Take the shape and scale tiers, and the port of v2's `SyntheticData`, off the critical path.
3. Bring them back on a named trigger: `planter-needs-row` turning out to be frequent on the real
   estate (`ARCHITECTURE.md` §11, question 7), or a question about scale.

Do not make this decision silently. Write it as one line in `<v3>/DECISIONS.md` in the pull request
that brings the facsimile in, and let the operator confirm or overturn it.

## Where each part lands in v3

| Facsimile part | v3 home | Note |
|---|---|---|
| The pure core: `Questions.Derive`, the delta comparison, `Tiering`, `Planning`, `Acceptance`, `Outcomes.Classify`, `Records`, `Config.Read` (`TARGET.md` §3) | `<v3>/kernel/`: `Preconditions`, `ExistingData`, `Outcome`, `Claims` | **The kernel references the BCL and nothing else.** `TARGET.md` §3 has `Core/Questions` calling ScriptDom, and that is wrong for v3. In v3, `io/TSql` (already `TSql160Parser`) parses the delta, and io hands the kernel typed statements |
| The schema read | `<v3>/io/SqlServer.Model` | the same VIEW DEFINITION read |
| The count read (one batched query per environment) | `<v3>/io/SqlServer.Measure` | one `SELECT` whose columns are `CASE WHEN EXISTS (…)` or `SUM(CASE …)` already passes Measure's allowlist. Reading `HasRows` from `sys.partitions` under a VIEW DEFINITION-only login may need one more allowlist entry, with its negative case |
| The planter: `witness.sql`, the trust suspend-and-restore envelope, the 12-step copy build (`ARCHITECTURE.md` §5) | `<v3>/io/` under the `Copy` type, inside WP 3.5's `synthetic-copy up --tier existing-data` | `Publish` exists only on `Copy`, so law S1 (disposable only) is already a v3 capability law |
| `fx_` databases, `.facsimile/` | v3's `dbchange_<host>_<pid>_<rand>`, `.dbchange/copies.json`, `.dbchange/cache/` | the T laws (teardown) attach to v3's registry and its sweep |
| `proofs/<slug>/answers.<env>.json` | `ExistingData` in the pull request's machine block, and `--existing-data <file>` | same content, v3's form |
| Verbs | `read` → the reading half of `predict`; `prove` → `prove`; `up`, `status`, `down` → `synthetic-copy up`, `status`, `down`; `shape` → `profile`, value-free; `doctor` → `doctor`; `init` → `dbchange/environments.json` | v3 keeps `predict` (pure, from counts) beside `prove` (published). v3's law P (on a copy, prediction equals proof) is the check between them, and the facsimile's corpus feeds it |
| The stdout grammar (`VERDICT`, `STOP`, `DONE`) and the stop list | `<v3>/cli/Render`, `<v3>/cli/Contract`, and the versioned schemas under `<v3>/cli/schemas/` | every stop still names one next step (A5) |
| MCP mode (`TARGET.md` §8) | `dbchange serve --mcp`, generated from v3's verb table | three-sentence server instructions; no resources and no prompts; knowledge lives in the skill files |
| The planned skill-tree changes at G4 (`ARCHITECTURE.md` §10) | `<v3>/knowledge/`, in the parallel lane that rebuilds it | v3 rebuilds the skill files itself; apply the changes there, not here |

## Translations

**Vocabulary: v3's names win.** The operator's ruling N5 (2026-09-25) kept `ExistingData`,
`PreconditionState`, `Precondition` and `AppliesTo`. The same concept takes the same name, so the
spec's terms are translated on the way in. Case IDs and law IDs stay as they are.

| Facsimile | v3 | |
|---|---|---|
| question | `Precondition` | |
| answer | `PreconditionState` | |
| one environment's answers | `ExistingData` | |
| `HasRows` | `TableEmpty` | the same site, opposite polarity |
| `NotNull`, `Fits`, `Unique` | `NotNull`, `Fits`, `Unique` | the facsimile adds the value classes of `Fits` (overlen, overbytes, nonconvertible, roundtrip, empty, overflow) and the collation classes of `Unique` (exact, case, accent, trailing, nulls) |
| `Orphan` | `ForeignKey` | |
| `CheckFalse` | `Check` | |
| `KeyLength`, `Rebuild`, `Computed`, `Temporal`, `Scan`, `Authored`, `SeedDrift`, `Unknown` | none yet | each arrives with its cases from `cases/10-questions.md`. The coordinate rules (`ARCHITECTURE.md` §4) and the predicate exception (P2) arrive with them |
| witness row, planter | none yet | new concepts: introduce each with a `<v3>/DECISIONS.md` line, or name them inside `SyntheticData` at the existing-data tier |

**Exit codes: v3's frozen table wins.** `<v3>/cli/exits.frozen` adds a code only in the change that
adds it to `Contract.cs`, and never renumbers one.

| Facsimile | v3 |
|---|---|
| 0 clean | 0 done |
| 1 usage | 1 bad-arguments |
| 3 finding | 3 blocked |
| 4 can't reach | 4 unreachable |
| 7 build | 7 build-failed |
| 6 machine not ready (`config`, `toolchain`, `platform`, `options-unknown`) | 6 configuration-refused |
| 9 principal | 9 refused-by-name |
| `interrupted` (9) | 130 interrupted |
| `answers-invalid` (5) | 2 unparsed-input |
| 5, the agent's next step (`missing-object`, `authored-script`, the witness stops, `acceptance`, `identity`) | **no home.** v3's 5 means *differs*. I recommend adding `8 needs-input` rather than overloading 5 |

**Laws.** v3 generates `<v3>/LAWS.md` from `[Trait("Law", …)]`. Attach each facsimile case's tests to
the v3 law that already states the same thing, and add a trait only where no v3 law does. A first
pass at the overlap, to confirm law by law:

| Facsimile | v3 | Relationship |
|---|---|---|
| F3, the differential | law P | they sit beside each other: P compares prediction with proof; F3 compares the facsimile with a full copy |
| H7 (the version stamp) and E2 (pinned DacFx) | law T | |
| P1–P6 | Measure's allowlist, and the rule that a failed count on a real environment keeps only its Msg number | |
| S | "a named environment cannot be written" | |
| D | values D2 and D3 | culture, bytes |
| M | `<v3>/ci/budgets.json` and the Budgets tests | |
| T | M3's parity checks against v2 | |

## What v3's own tests will say when the files arrive

| Test | What it will flag | The fix |
|---|---|---|
| **Manifest** | Every markdown file outside `archive/` needs a row in `<v3>/ci/docs.manifest.json`, with its reader, its moment and its line budget. The case files run from about 200 to 2,400 lines each (2026-10-19) | add the rows |
| **NoRestatedCounts** | This spec restates counts of laws and operations in prose | point to `cases/90-traceability.md`, which is generated; or drop the number; or date the line |
| **Citations** | The spec's relative paths assume `sidecar/projection/ssdt-agent/facsimile/`, and every `../../../../` breaks on the move | fix each one |
| The Python lint `tests/catalog_lint.py` | becomes a C# test in `<v3>/tests/Budgets.Tests/`, as milestone M-a of `TARGET.md` §11 already plans. Its `--matrix` mode is the generator of `90-traceability.md`, so the generated file's banner names the new generator | port it |

**My recommendation for where the files go:**

| File | Destination |
|---|---|
| `../../../../TWIN_SUCCESSION.md` | `<v3>/archive/`, with an `<v3>/archive/INDEX.md` row. It is the reasoning, and in v3 reasoning is provenance |
| `ARCHITECTURE.md`, `INVARIANTS.md`, `cases/` | beside the corpus under `<v3>/tests/Golden/`, because the catalog *is* the suite |
| the parts of `TARGET.md` that survive | folded into `<v3>/V3_MILESTONES.md` as amendments |
| the rest of `TARGET.md` | archived |

The placement is yours to decide with the operator.

You do not need to merge PR #706 into `main` for any of this. The files can be taken from its
branch as they stand:
`git checkout origin/claude/twin-rearchitecture-strategy-hq5lry -- sidecar/projection/ssdt-agent/facsimile TWIN_SUCCESSION.md`.

## The order of work, against v3's work packages

`TARGET.md` §11's milestones M-a to M-f assumed nothing existed. In v3 a good part of the shell
already exists, so the work lands inside v3's own packages. The precondition parts can begin now,
but M2 itself is held until the operator allows it (`<v3>/NEXT.md`).

| v3 package | What the facsimile brings | The cases |
|---|---|---|
| **WP 2.1** (`Preconditions`, `Outcome.Predict`) | extend the precondition table with the facsimile's kinds and the coordinate rules. Coordinates: an object the delta creates is answered from the delta; one it renames or moves is counted under its old name; anything else is counted where it is, or stops `missing-object` | `cases/10-questions.md` |
| **WP 2.6** (the corpus begins) | seed `<v3>/tests/Golden/changes/` from the four operations files | `cases/40-ops-columns.md`, `cases/41-ops-keys.md`, `cases/42-ops-tables.md`, `cases/43-compound.md` |
| **WP 3.3** (`Generate`, existing-data tier) | the planter, if the operator agrees to the recommendation above | `cases/20-planter.md` |
| **WP 3.4** (`LocalServer`) | LocalDB 16.x is confirmed. Keep the 180 s DDL wait, `SINGLE_USER WITH ROLLBACK IMMEDIATE`, `ClearPool`, and the single retry on the first connect | `TARGET.md` §9 item 5 |
| **WP 3.5** (`synthetic-copy up`) | the 12-step copy build | `ARCHITECTURE.md` §5 |
| **WP 3.6** (`check`, `status`, `down`) | "the deploy plan against it is empty" is F2 (shape identity); silence on replay is D6 | `cases/00-invariants.md` |
| **M4** (`prove`) | `Outcomes.Classify` and the verdict and stop tables | `cases/30-outcomes.md` |
| **M4** (`prove`) | per-environment proof, drift, Prod | `cases/50-environments.md` |
| **M4** (`prove`) | the privacy laws, attacked | `cases/60-privacy.md` |
| **G3** | Twin parity: every Twin responsibility kept or safely retired | `cases/80-twin-parity.md` |

## Still owed by people (merge these with v3's asks, so nobody is asked twice)

| What | v3's ask | Why it matters here |
|---|---|---|
| The Octopus step's *effective* options (the profile XML, plus `/p:` overrides, plus the per-environment SQLCMD values), its DacFx version, and its login's language and DATEFORMAT | **S7** | the same ask, so ask once. No verdict on the real estate is trusted until the options are captured; never keep a hand-written Strict profile |
| Whether laptops and hosted agents have the .NET 10 SDK | **S6** | the operator confirmed that C# works, not which SDK. One `dotnet --list-sdks` on a laptop answers it. If only .NET 8 is there, that is v3's question as a whole, not the facsimile's |
| Each environment's version, compatibility level, RCSI and collation | **S8** | the `platform` stop compares the copy's tempdb collation with each environment's, and a proof is stamped with the server it ran on |
| A VIEW DEFINITION-only login for Prod, and for QA and UAT when the tool reads them | **new** | ask for it by that exact grant. Until it exists, Prod is `--assume Prod=worst` |
| Whether an Azure Artifacts feed exists | **new** | distribution only. v3 already builds `cli` into `.dbchange/bin/`, which works without one |

## Questions only a first run can answer

`ARCHITECTURE.md` §11 lists thirteen of them, and `cases/90-traceability.md` lists every `verify`
case. Each first run decides its case, and the receipt replaces the expectation. Four of them could
change the design rather than a case:

| § 11 | The question | If the answer is no |
|---|---|---|
| 1 | Does Extract preserve a disabled FK and an untrusted (`WITH NOCHECK`) constraint? | trust state needs metadata witnesses |
| 3 | Do the generated statement shapes hold on DacFx 162.x as well as 170.x? | the shapes need a 162.x variant. v3's spike S2 asks the same question; run it once and let it answer both |
| 9 | Does constraint validation run after the post-deploy script on 162.x, as it does on 170.5.96? | a lookup seeded post-deploy may no longer satisfy a new FK |
| 10 | Do system-named constraints (`DF__…`) keep their names through Extract and a publish to the copy? | the head delta against the copy may differ from the delta against the environment (F5) |

## What not to do

- **Do not port v2's generator in order to prove.** Witness rows are enough. Port it only if the
  operator keeps the shape or scale tier for another reason.
- **Never re-publish the base after planting.** A re-publish erases the witnesses (S2; red team #9).
- **Write remedies by predicate, never by literal value.** A remedy that names a value leaks the
  environment's data.
- **Never keep a hand-written Strict profile.** Load the captured options, and flip only
  `BlockOnPossibleDataLoss`. v3 already holds this.
- **Do not treat a failed deploy as atomic.** Under the default options it is not.
- **Do not run on LocalDB a proof that touches a CDC-tracked table.** LocalDB has no SQL Agent, so
  such a proof is principal-led and runs on a full SQL Server.
- **Do not instantiate `pipelines/ssdt-agent-pr-validation.yml` as written** (`../pipelines/`). It
  proves into a new, empty database, so every populated tightening would pass
  (`../../../../TWIN_SUCCESSION.md` §8).
- **Do not import the standalone choices.** That covers the V2 pillars (`TARGET.md` §10) and also
  this spec's own `net8.0`, its exit numbers and its names, wherever v3 has already decided. v3's
  `<v3>/VALUES.md` governs v3.

## Where the evidence lives

| File | What it holds |
|---|---|
| `../../../../TWIN_SUCCESSION.md` | the reasoning and the receipts. §0.1: the offline delta equals the live delta. §6b: the boundary, and what VIEW DEFINITION alone reads. Appendix A: the red team's dispositions. Appendix B: the v3 donor parts, most of which v3 has since built. Appendix C: the laptop check. Appendix D: the shape-profile SQL |
| `cases/90-traceability.md` | which cases witness which law, operation, question kind, stop and Twin responsibility, plus the open `verify` cases and the audit history |
| `README.md` | the reading order for the spec itself |

The work that was hardest to see is already done: what to leave out. The Twin tried to resemble the
data; this only has to agree with it. That is a smaller thing to build, and a smaller thing to keep.
