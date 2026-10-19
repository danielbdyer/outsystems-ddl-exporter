# V2 sidecar test suite — assembly layout

The test suite is split across three assemblies (2026-07-01) so an edit
recompiles only its own assembly instead of the whole ~86k-LOC suite, and so
the pure and Docker pools are an assembly boundary rather than a name filter.
All three keep the `Projection.Tests` namespace, so a test can `open
Projection.Tests.Fixtures` etc. regardless of which assembly it lives in.

| Project | Role | Depends on |
|---|---|---|
| `Projection.Tests.Support` | Shared fixtures/helpers (library, no tests): `Fixtures`, `IRBuilders`, `StaticCatalogFixtures`, `ProfileFixtures`, `GoldenCatalog`, `TotalityFunctor`, `SourceFixtures/*`, `TaskSync`. | src only |
| `Projection.Tests.Integration` | **Every `[<Collection("Docker-SqlServer")>]` test** + the container fixtures (`EphemeralContainerFixture`, `ContainerFixtureSupport`) + the `Docker-SqlServer` collection definition + `PerfHarnessScenarios`. | Support + src |
| `Projection.Tests` | The pure pool (everything else) + the two cross-cutting aggregators (`AxiomTests`, `AdjunctionLawTests`). | Support + Integration + src |

## Adding a test — where does it go?

- **Needs a real SQL Server / a container** (marked `[<Collection("Docker-SqlServer")>]`,
  uses `EphemeralContainerFixture` / `IsolatedContainerFixture`, or is an `…E2ETests` /
  `…DockerTests` / canary): add it to **`Projection.Tests.Integration`**.
  The `Docker-SqlServer` collection definition lives in that assembly — xUnit resolves
  collection config **per-assembly**, so a `[<Collection>]` member in a different assembly
  from its `CollectionDefinition` silently loses `DisableParallelization` and re-opens the
  single-instance CREATE/DROP + CDC livelock the serial pool exists to prevent.
- **A pure unit/property test**: add it to **`Projection.Tests`**.
- **A fixture/helper reused across many test files**: add it to **`Projection.Tests.Support`**
  (it must not reference any `*Tests` file — Support is the bottom layer).

## Running

`scripts/test.sh` drives the pools by assembly:

- `fast` — the pure pool (`Projection.Tests`), the inner loop.
- `docker` — the Docker pool (`Projection.Tests.Integration`), scale tier excluded.
- `scale` — the throughput/perf measurements tiered out of `docker`.
- `all` — `fast` + `docker` + `scale`, sequential (never concurrent — that OOM-kills the host).

`scripts/test-docker-parallel.sh` is an **opt-in** parallel Docker runner for a many-core
CI host; on a 4-core box it is a measured net-loss (the pool is CPU-bound), so the default
`docker` pool stays serial.

## Gotcha: source-as-fixture tests

A few tests read their own source file as a fixture (`AxiomTests` M16 citation gate;
`PerfHarnessCatalogTests` H7). When a cited/scanned file moves between assemblies, update the
path there — M16 hardcodes `citationOf "tests/<Project>/<File>.fs"` and H7 walks up to find
`PerfHarnessScenarios.fs` (now under `Projection.Tests.Integration/`).
