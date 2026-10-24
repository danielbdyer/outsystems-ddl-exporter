namespace Twin.Tests.Integration

open Xunit

/// THE TWIN — the Docker pool's collection. One collection, strictly
/// serial (the OOM survival rule: SQL Server containers and parallel
/// test execution do not share a 4-core host well). Every test touching
/// a container carries this collection.
[<CollectionDefinition("Twin-Docker", DisableParallelization = true)>]
type TwinDockerCollection = class end
