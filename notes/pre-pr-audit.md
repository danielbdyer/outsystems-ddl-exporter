# Pre-PR Audit – 2025-09-27T06:30:26Z

## Checklist Confirmation
- [x] .NET SDK verified (`dotnet --info`, `dotnet --list-sdks`).
- [x] Solution restored (`dotnet restore OutSystemsModelToSql.sln`).
- [x] Release build completed (`dotnet build OutSystemsModelToSql.sln -c Release --no-restore`).
- [x] Full test suite passed (`dotnet test OutSystemsModelToSql.sln -c Release --no-build`).
- [x] CLI smoke run executed (`dotnet run --project src/Osm.Cli -- --help`).

## Outstanding Gaps Before PR
- Multi-column unique enforcement & tests remain TODO (see `notes/test-plan.md` §3.9).
- Physical metadata integration/perf profiling scenarios are still unchecked in the test plan (§2.4–§2.5).
- SMO unique-index suppression scenarios and CLI decision-summary emission are still tracked as open items in `tasks.md`.
- CI/formatting guardrails and operations roadmap documentation slices are pending to satisfy backlog §8–§10.

## Suggested Next Steps
1. Close the remaining tightening policy items by covering composite unique indexes and exporting rationales into manifests.
2. Finish SMO unique-index toggle tests and wire the CLI to surface decision telemetry + manifest entries.
3. Implement the profiling physical metadata/performance fixtures so EvidenceGated rules can reason about computed columns deterministically.
4. Stand up CI scripts (restore/build/test + CLI smoke) and add formatting/analyzer enforcement to lock in coding standards before PR submission.

Maintaining this list alongside `tasks.md` and `architecture-guardrails.md` should keep the PR scope honest while making the final review traceable.
