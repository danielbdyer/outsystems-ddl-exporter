# Test Matrix Cheatsheet

> One-stop view of the test projects, what they validate, and how to run them individually. Use this when scoping regression coverage or triaging failures.

## Unit / Component Projects

| Project | Command | What it covers | Notes |
| --- | --- | --- | --- |
| `tests/Osm.Domain.Tests` | `dotnet test tests/Osm.Domain.Tests/Osm.Domain.Tests.csproj -c Release --no-build` | Entity/index invariants, logical model validation. | Fastest suite; run before touching domain models or configuration defaults. |
| `tests/Osm.Json.Tests` | `dotnet test tests/Osm.Json.Tests/Osm.Json.Tests.csproj -c Release --no-build` | JSON ingestion, DTO compatibility with fixtures. | Uses `tests/Fixtures/model.*.json` payloads. |
| `tests/Osm.Validation.Tests` | `dotnet test tests/Osm.Validation.Tests/Osm.Validation.Tests.csproj -c Release --no-build` | Policy/toggle resolution, tightening decisions. | Update when editing `TighteningToggleSnapshot` or `TighteningMode` logic. |
| `tests/Osm.Smo.Tests` | `dotnet test tests/Osm.Smo.Tests/Osm.Smo.Tests.csproj -c Release --no-build` | SMO graph creation, constraint naming, emission decisions. | Warnings flag string comparison anti-patterns; fix them when touching tests. |
| `tests/Osm.Dmm.Tests` | `dotnet test tests/Osm.Dmm.Tests/Osm.Dmm.Tests.csproj -c Release --no-build` | ScriptDom parsing + DMM serialization. | Great for checking guardrail §5 compliance. |
| `tests/Osm.Emission.Tests` | `dotnet test tests/Osm.Emission.Tests/Osm.Emission.Tests.csproj -c Release --no-build` | SSDT emission pipeline, `SsdtEmitter` behaviors. | Warnings about `ConfigureAwait(false)` appear today—capture when tracking observability tasks. |
| `tests/Osm.Cli.Tests` | `dotnet test tests/Osm.Cli.Tests/Osm.Cli.Tests.csproj -c Release --no-build` | CLI option parsing, manifest wiring, report launching. | High-value when altering commands or toggles. |
| `tests/Osm.Pipeline.Tests` | `dotnet test tests/Osm.Pipeline.Tests/Osm.Pipeline.Tests.csproj -c Release --no-build` | Orchestration, manifest assembly, telemetry loggers. | Current failures often hint at missing DTO references (e.g., `ModelExtractionResult`). |

## Integration / Harness Projects

| Project | Command | Purpose | Environment notes |
| --- | --- | --- | --- |
| `tests/Osm.LoadHarness.Integration.Tests` | `dotnet test tests/Osm.LoadHarness.Integration.Tests/Osm.LoadHarness.Integration.Tests.csproj -c Release --no-build` | Exercises load harness logic against fixture-driven runs. | Requires Docker for SQL Server containers. |
| `tests/Osm.Pipeline.Integration.Tests` | `dotnet test tests/Osm.Pipeline.Integration.Tests/Osm.Pipeline.Integration.Tests.csproj -c Release --no-build` | Full pipeline exercises (extraction → emission). | Also container-backed; skip (and document) when Docker is unavailable. |
| CLI smoke | `dotnet run --project src/Osm.Cli -- --in tests/Fixtures/model.edge-case.json` | Verifies CLI ingest + summary output with fixtures. | Optional but useful before PRs; ensure `--profile-mock-folder` toggles match fixture path. |

## Usage Tips

1. Run `dotnet restore` once, then call the specific `dotnet test` command above with `--no-build` to save time.
2. For repeated iterations, append `-v minimal` or `-v n` to reduce log noise.
3. Capture any systemic failures (e.g., missing types or failing tests) in status updates referencing the `chunk_id` from the shell log for easy quoting.

This matrix keeps regression coverage front-of-mind so we can cite the exact suite(s) exercised in PR descriptions without restating commands from memory.
