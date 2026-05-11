namespace Projection.Tests

open Xunit

/// xUnit test-collection definitions for V2's test surface.
///
/// **`Docker-SqlServer` collection** (chapter 4.1.B slice δ
/// observability cash-out — audit-during-validation discipline).
/// Every test class that touches the warm `projection-mssql-warm`
/// container (or spins its own ephemeral one) joins this collection
/// so xUnit serializes them within the assembly. Without this
/// serialization the assembly defaults to parallel-test-class
/// execution; multiple classes concurrently CREATE / DROP per-test
/// ephemeral databases on the same SQL Server instance, and when
/// `CdcSilenceTests` runs alongside (its CDC infrastructure has
/// instance-wide effects on `master.sys.databases.is_cdc_enabled`),
/// the combination livelocks on instance-level locks.
///
/// **Why a collection rather than `xunit.runner.json`
/// `parallelizeTestCollections: false`:** the broad-stroke fix
/// would also serialize the ~840 pure-F# tests that have no
/// container dependency and parallelize cleanly today (full
/// non-canary suite ~5s). The collection scopes the serialization
/// to the failure mode without blunting the rest of the suite.
///
/// **Members of this collection** (see `[<Collection
/// ("Docker-SqlServer")>]` markers in the per-test-class modules):
///   - `CanaryDeployTests`
///   - `CanaryRoundTripTests`
///   - `CdcSilenceTests`
///   - `GeneratorScaleTests`
///   - `StaticPopulationEmitterTests`
[<CollectionDefinition("Docker-SqlServer", DisableParallelization = true)>]
type DockerSqlServerCollection() = class end
