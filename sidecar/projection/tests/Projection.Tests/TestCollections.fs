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

/// **`Global-MutableState` collection** (chapter B.4 slice 6.5 + 7
/// follow-on; chapter C slice C.1).
///
/// Tests that mutate module-private mutable singletons need to be
/// serialized so they don't race with each other:
///   - `Projection.Core.Bench` — `record` mutates a process-scoped
///     `Dictionary<string, ResizeArray<int64>>` under one lock;
///     `Bench.reset` zeroes it.
///   - `Projection.Pipeline.LogSink` — `emit` mutates a process-
///     scoped `RunState`; `reset` zeroes it; `setWriter` flips the
///     destination.
///
/// Members:
///   - `BenchTests` (direct Bench API exercise)
///   - `LogSinkTests` (direct LogSink API exercise; calls
///     `Bench.snapshot` during `runComplete`)
///   - `FullExportCliTests` (in-process CLI mirror that calls
///     `LogSink.reset` + `Bench.reset` at entry; emits envelopes
///     that the LogSink-tests' captured output would interleave with
///     under parallel execution).
[<CollectionDefinition("Global-MutableState", DisableParallelization = true)>]
type GlobalMutableStateCollection() = class end
