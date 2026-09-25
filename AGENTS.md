# AGENTS.md — working in this repository

This repository builds `estate`, which tells a developer whether a schema change to an OutSystems
estate on SSDT blocks or applies on each environment before Octopus deploys it: one CLI (its verbs
listed by `estate --help --json`, and from M8 in `cli/VERBS.md`, generated from it) and, from M7,
`knowledge/`, the files a developer's Copilot session reads in the estate repository. Read this
file, then `NEXT.md`, then the README of the package being changed. Nothing else is required.

## Before anything

Run `estate doctor` and quote its line before claiming a tool, a daemon or a database is missing.
The line reads `READY` or `DEGRADED` and names the .NET SDK and runtime, the tool folder and its
DacFx against `estate/ledgers/toolchain.md`, the build route, the scratch server (Docker or LocalDB)
and Git LFS; every missing item carries a remedy.
Without the session hook, `dotnet run --project cli -- doctor` runs it.

## Where truth lives

- What is built, and in what order: `V3_MILESTONES.md`; where its §4 changes a design document,
  the plan wins. The design: `V3_ARCHITECTURE.md` (the code) and `V3_INSTRUCTION_ARCHITECTURE.md`
  (the documents), until `ARCHITECTURE.md` replaces both at M8.
- What it upholds: `VALUES.md`. The laws, each with the test that states it: `LAWS.md`, generated
  from the tests from M1; the CI run says which are green. What was decided: `DECISIONS.md`. What is
  next: `NEXT.md`.
- The domain (SQL Server, DacFx, the estate, the platform): `knowledge/` from M7; until then, the
  design documents.
- The past: `archive/` holds v1 and v2, indexed in `archive/INDEX.md`. It is provenance: cite it as
  archive, and re-verify against the current files before relying on it. It stays readable until
  M8 because v2 is the specification a port reads: a work package that ports names its v2 files,
  ports their tests first, and is reviewed against them. No agent edits `archive/`; the deny rule
  arrives with the operator's `.claude/settings.json` at M0. The root `.ignore` hides it from
  search: search it with `rg --no-ignore <pattern> archive/`, or read a file by its path.

## Building and testing

- `dotnet build Estate.sln` — warnings are errors; `kernel/BannedSymbols.txt` keeps I/O, the
  clock, randomness and `Task` out of the kernel.
- `dotnet test Estate.sln --filter Category=fast` before every push: no SQL Server, under five
  minutes.
- `dotnet test Estate.sln --filter Category=fixture` when `io/` changed: needs a SQL Server, the
  container from the pinned image where Docker runs, LocalDB where it does not.
- Every test carries `[Trait("Category", "fast")]`, or `[Trait("Category", "fixture")]` when it
  needs SQL Server. No test skips itself, and none retries.
- The scale lane (from M3) and the proof lane (from M4) run in CI. Run them locally only when the
  task is about them, and never both in one `dotnet test`.
- A stale build after switching branches: `dotnet clean`; an old RID-specific output directory
  shadows a fresh build without an error.

## How to change things

- Tests first: a work package's "done when" becomes a test that fails, then passes.
- A kernel change starts with the test (a law or a property), then the type, then the function.
  Types are records and closed hierarchies; `V3_ARCHITECTURE.md` §6.6 gives the encoding.
- A verb change updates its `--help` text in the same commit; the bundle (from M7) and
  `cli/VERBS.md` (from M8) are generated from it.
- From M7, a knowledge change edits the canonical file under `knowledge/`, and
  `estate knowledge package` regenerates the pointers, the index and the bundle. A generated file
  is never edited.
- A red budget or manifest test is a design question: shrink the thing, or raise its ceiling in
  `ci/budgets.json` with a decision line in the same pull request.
- A new package is a decision line, a `PackageVersion` in `Directory.Packages.props` and a line in
  `ci/packages.allow`, in the same pull request.
- A new law is a test with an English name and a `[Trait("Law", "<the law>")]`; `ci/laws.sh`
  (`ci/laws.ps1` on Windows) regenerates `LAWS.md`, and `Budgets.Tests: Laws` fails until the
  regenerated file is committed. A law without a green test is not a law.

## What a session writes

Code and tests, under the laws and the budgets. A pull request body in the register below. At most
one line in `DECISIONS.md` per decision, each in the format
`2026-09-23 · <the decision> · #<pull request>`. `NEXT.md`, rewritten, never appended to, under its
budget. A knowledge file, when what is true of the domain changed. `V3_MILESTONES.md` only to keep
it true, one paragraph at a time, each correction with a decision line.

Nothing else: no new top-level document, no status section, no plan, no assessment. A new markdown
file fails the build until its row in `ci/docs.manifest.json` says who reads it and when. An edit
that makes a file shorter never needs a reason. No commit, pull request, code or document names
the model that wrote it.

## The register

Write plain, technical English. The rules hold for documents, error and finding messages, CLI output,
test names, review pages and pull-request bodies.

- Name the exact object: the file and line, the type, the SQL Server feature, the command, the number.
  Use the word a DBA, an OutSystems developer or the DacFx documentation uses for it.
- Give the circumstances first, then the background a reader needs, then the point. Define a term where
  it first matters.
- Order the text top-down: open with the situation, state the conclusion, and group the support beneath
  it (the Minto pyramid, applied with judgement).
- Use a literal phrase wherever one exists. A metaphor or flourish ("a dial worth turning" for "a
  parameter worth varying") makes the reader recover a meaning the literal phrase states directly, and
  it brings connotations nobody chose.
- State content instead of announcing it: no "Two things:", "Here is why:" or "as follows".
- Make every reference add information: say what the cited thing says as well as its identifier. A test
  asserts behaviour the code could get wrong; it never restates the implementation or itself.
- Write agentless, in the active voice, without hedging; say what was not checked. Use no private
  nickname, no retired word (`ci/register.json` lists each with what to write instead, and
  `Register.Prose` fails on one) and no count that a generated file carries.

## When something fails

- An error names its code and its remedy. Do the remedy; a workaround hides the error.
- A tool call refused by the permission check: say so, and continue with the rest.
- A red budget test names the ceiling and the file.
- A red law names the law. The law is right until a decision line says otherwise.
- The scratch server not answering (exit 4): `ci/sql.sh up`, or `ci/sql.ps1 up` on Windows, then
  `estate doctor`.
- A proof whose verdict disagrees with a recorded finding: the verdict is a new finding with its
  provenance; append it and strike the old one, which stays.
- A question only the operator can answer: one line in `NEXT.md` naming the question and who
  answers it, and work on that question stops there.

## The pull request

Summary; what changed; the law or test that pins it; budgets; the decision lines, or none; and
*Not checked*, never empty. CI runs the budget and law tests, which name the ceiling or the law that
fails; the author says what moved and why. A person approves; no lane ever does.
