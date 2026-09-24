# estate — the change engine for an OutSystems estate on SSDT

`estate` reads what an environment holds and what the repository says, predicts read-only whether
a schema change blocks or applies on each environment and why, proves the change on a disposable
copy by letting DacFx publish it under the pipeline's own profile, and writes the record a dev lead
approves by reading. It works on the SSDT project that carries the estate's external entities;
Octopus still deploys.

## The verbs

`cli/VERBS.md` lists every verb with its flags, its JSON output and its exit codes. It is generated
from `estate --help --json` and never edited by hand.

## Try it in five minutes

1. `estate doctor` names the .NET SDK, the DacFx pin, the substrate and the Twin, each with a
   remedy when it is missing.
2. `estate twin up` builds the repository's schema, publishes it to a local SQL Server (Docker with
   the pinned SQL Server image, or LocalDB where Docker is absent), mints rows from the committed
   evidence, and caches the result by fingerprint under `.estate/`. The second run starts from
   that local cache.
3. `estate prove --project tests/Golden/Golden.sqlproj --target twin` proves a change on a fresh
   copy of the Twin and prints the verdict with its receipt.
4. Read the verdict: how the change ships, what proving showed, and what was not checked.

Each step answers once its milestone lands (`doctor` at M1, `twin up` at M3, `prove` at M4);
`NEXT.md` says where the build is. From a clone, `dotnet run --project cli -- doctor` runs the verb
without the session hook.

At run time `estate` opens no network connection except to the SQL Server it is given. The one
fetch is Docker pulling the pinned SQL Server image when it is absent. The CLI turns off DacFx's
and the .NET SDK's telemetry in its own process.

## Where things are

- `kernel/` — the domain as types and pure functions: no I/O, no clock, no randomness.
- `io/` — everything that touches SQL Server, DacFx, git, Docker or a file.
- `cli/` — the `estate` executable: the verb table, the dispatcher, the renderers.
- `tests/` — `Kernel.Tests`, `Io.Tests` (needs SQL Server) and `Budgets.Tests` (the repository's
  own rules); the corpus under `tests/Golden/`.
- `ci/` — the data the tests read: the budgets, the document manifest, the package allowlist.
- `knowledge/` — from M7: the operations, the record, the findings and the ledgers, vendored to
  the estate.
- `archive/` — v1 and v2, frozen; `archive/INDEX.md` says what is where.

## What it upholds

`VALUES.md` names each value with the mechanism that holds it and where that mechanism lives.
`LAWS.md`, generated from the test names from M1, lists the laws that are green.

## For developers on the estate

This repository is not needed on the estate. The estate carries the published tool folder under
`tools/estate/`, the `estate.cmd` and `estate.ps1` shims at its root, and the generated knowledge
bundle, all committed by one vendoring pull request per version. Start at the knowledge bundle's
README there.

## For maintainers

Read `AGENTS.md`, then `NEXT.md`, then the README of the package being changed. Until M8 the design
is `V3_ARCHITECTURE.md` (the code) and `V3_INSTRUCTION_ARCHITECTURE.md` (the documents), and the
build plan is `V3_MILESTONES.md`, which wins where its §4 changes a design document.

## Provenance

v1 (C#) and v2 (F#) are frozen under `archive/`, indexed in `archive/INDEX.md`; v2 is the
specification v3's ports are read from. The tag `v2-final` marks the commit v3 started from.
`DECISIONS.md` is the log, one line per decision.
