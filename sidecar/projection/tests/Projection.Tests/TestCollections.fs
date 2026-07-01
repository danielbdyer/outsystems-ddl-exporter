namespace Projection.Tests

open Xunit

/// xUnit test-collection definitions for V2's test surface.
///
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
