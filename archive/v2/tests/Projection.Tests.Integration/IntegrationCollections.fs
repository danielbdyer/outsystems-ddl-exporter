namespace Projection.Tests

open Xunit

/// Docker-SqlServer xUnit collection (moved out of the monolithic
/// TestCollections.fs at the 2026-07-01 assembly split). The collection
/// DEFINITION must live in the SAME assembly as its [<Collection>] members —
/// xUnit resolves collection config per-assembly, so a definition in another
/// assembly would silently drop DisableParallelization and re-open the
/// single-instance CREATE/DROP + CDC livelock. All Docker tests are in THIS
/// assembly, so the definition lives here with them.
[<CollectionDefinition("Docker-SqlServer", DisableParallelization = true)>]
type DockerSqlServerCollection() = class end
