# dbchange-lite — predict read-only whether a schema change blocks, and prove it on a local copy

dbchange-lite is a command-line tool over DacFx for the SSDT repository that carries an OutSystems
system's external entities. It answers three read-only questions: does the environment hold what the
repository says it holds (`drift`), what does this branch change (`diff`), and will the change block
when the deployment step deploys it (`predict`). Where reading cannot settle the third, `prove` deploys
the change to a disposable copy on a local SQL Server, never to a shared database. `doctor` says
whether this machine can do the work. Every answer is one JSON object with an `open` list: each item
names the command that closes it, or the person who must answer and the question.

The design, every contract and the build plan are `DBCHANGE_LITE.md` at the repository root. v3, the
tool built in `kernel/`, `io/` and `cli/`, is the specification: every term here is v3's, and
`tests/Golden/changes/` is the acceptance test.

## Build and run

```
dotnet build DbChangeLite.sln
dotnet test DbChangeLite.sln --filter Category=fast
ci/publish.sh                                  # ci/publish.ps1 on Windows: dist/dbchange-lite/ with DacFx's build targets
dotnet dist/dbchange-lite/dbchange-lite.dll --help
```

A verb runs from a checkout of the SSDT repository, which holds `dbchange/environments.json`,
`dbchange/ledgers/toolchain.md` and the publish profile the environments file names. `DBCHANGE_LITE_TOOL`
names the published folder when the tool runs from elsewhere.

The fixture tests need `DBCHANGE_SQL`, a connection string for a SQL Server on this machine, and
`DBCHANGE_LITE_TOOL`; the corpus test runs every sample change of `tests/Golden/changes/` on an
empty, a populated and a violating copy and compares each outcome with its `predict.txt`.

## Where things are

- `src/` — the tool: one file per concern, no comments; `DBCHANGE_LITE.md` §9 names each file's job.
- `src/exits.frozen` — v3's exit table, held equal by a test.
- `src/operations.json` — each catalog operation and the deploy report operations it may produce.
- `src/causes.json` — the known causes of a difference `drift` reports.
- `tests/` — fast, fixture and build tests, in v3's categories.
- `knowledge/dbchange-lite.md` — the entry skill a developer's agent follows.
- `ci/` — the publish scripts.
