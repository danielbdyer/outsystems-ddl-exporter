# Test Baseline Summary - Before M1.2/M1.3 Work

**Date**: 2025-11-19
**Branch**: feature/m1.2-m1.3-circular-dependency-diagnostics
**Commit**: (baseline - before any M1.2/M1.3 changes)

## Overall Test Results

### Test Run Summary

| Project | Total | Passed | Failed | Skipped |
|---------|-------|--------|--------|---------|
| Osm.Domain.Tests | 75 | 75 | 0 | 0 |
| Osm.Json.Tests | 89 | 89 | 0 | 0 |
| Osm.Validation.Tests | 127 | 127 | 0 | 0 |
| Osm.Dmm.Tests | 4 | 4 | 0 | 0 |
| Osm.Emission.Tests | 108 | 100 | 8 | 0 |
| Osm.Smo.Tests | 54 | 54 | 0 | 0 |
| Osm.Documentation.Tests | 23 | 23 | 0 | 0 |
| Osm.LoadHarness.Tests | 2 | 2 | 0 | 0 |
| Osm.LoadHarness.Integration.Tests | 10 | 1 | 4 | 5 |
| Osm.Etl.Integration.Tests | 6 | 2 | 0 | 4 |
| Osm.Pipeline.Tests | 384 | 382 | 2 | 0 |
| Osm.Cli.Tests | 88 | 77 | 11 | 0 |

**Total**: 970 tests
- **Passed**: 936
- **Failed**: 25
- **Skipped**: 9

## Known Failures (Baseline)

### Osm.Emission.Tests (8 failures)
- All 8 failures are integration-related (likely environment or test data issues)

### Osm.LoadHarness.Integration.Tests (4 failures)
- Integration test failures (likely requires specific test environment)

### Osm.Pipeline.Tests (2 failures)
- Integration-related failures

### Osm.Cli.Tests (11 failures)
- Most failures appear to be CLI integration test issues
- Specific failures:
  - `ConfigurableConnectionTests.ExtractModel_ShouldWriteJsonUsingFixtureExecutor`
  - `CliIntegrationTests.BuildSsdt_and_dmm_compare_complete_successfully` (expected 6, got 5)
  - `CliIntegrationTests.Profile_command_captures_fixture_snapshot` (KeyNotFoundException)
  - Plus 8 additional integration test failures

## M1.2/M1.3 Work Expectations

### What Should Remain Stable
- All unit tests (Domain, Json, Validation, Dmm, Smo, Documentation)
- Existing TopologicalOrderingValidator tests (should all continue to pass)

### New Tests to Add
- CircularDependencyDetector tests (new test class)
- Cycle detection integration tests
- Configuration/allowlist tests
- Diagnostic output tests

### Success Criteria for M1.2/M1.3
After M1.2/M1.3 implementation:
1. **No regression**: All currently passing tests (936) must still pass
2. **New tests**: CircularDependencyDetector tests should be green
3. **Known failures**: The 25 baseline failures should remain unchanged (not increase)

## Notes
- The 25 failures are **pre-existing** and unrelated to M1.2/M1.3 work
- These appear to be integration test failures requiring specific environments
- Focus for M1.2/M1.3: Keep the 936 passing tests green while adding new functionality
