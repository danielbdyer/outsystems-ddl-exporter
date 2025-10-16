# Test Suite Guide

The repository splits fast-running unit tests from slower CLI smoke tests so contributors can iterate quickly while still having an end-to-end safety net.

## Default test run (unit tests only)
Most test projects complete quickly and exercise the command handlers, binders, and pipeline services entirely in-process. To run just these tests use the default filter that skips the slower scenarios:

```bash
dotnet test --filter Category!=Integration
```

## CLI smoke tests
The scenarios in `tests/Osm.Cli.Tests/CliIntegrationTests.cs` spawn the CLI via `dotnet run` and perform full filesystem comparisons against the fixtures. They are tagged with `[Trait("Category", "Integration")]` so they can be invoked on demand or from a dedicated CI job:

```bash
dotnet test --filter Category=Integration
```

These tests assume the repository has been restored and built (see `notes/run-checklist.md`), and they rely on the fixture data under `tests/Fixtures`. Expect them to take noticeably longer than the in-process unit tests because they emit SSDT artifacts and DMM diffs to temporary directories.

Running the smoke tests after significant CLI changes is recommended to confirm that the published artifacts still match the expected fixtures before opening a pull request.
