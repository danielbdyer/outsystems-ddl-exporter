namespace Projection.Tests

// The entity-less-espace regression (2026-07-07, the live partial-transfer
// catch, part two). A real estate routinely carries espaces with no
// entities — UI / theme / service modules. V1 skips them ("Module 'X'
// contains no entities and will be skipped"); V2's rowset path fed them
// to `Module.create`, whose LR1/A39 non-empty-kinds invariant failed the
// WHOLE metamodel read with `module.kinds.empty` — the go board's
// "a metamodel could not be read" red line, hit right after the
// SequentialAccess row-mapping fix let the extraction complete. The
// edge-case seed never fires this shape (every seeded espace has
// entities), which is how it shipped past the canary; this suite pins
// it end-to-end: seed + one entity-less espace → extraction → bundle →
// catalog parse succeeds WITHOUT the module, and the erasure is NAMED
// (one notice from `OssysRowsetReader.entityLessModules`).
//
// Serial via Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

[<Xunit.Collection("Docker-SqlServer")>]
type EntityLessModuleReadDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``an espace with no entities is skipped as a named erasure — the metamodel read succeeds (V1 parity)`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP EntityLessModuleRead: Docker daemon not reachable."
        else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "EmptyEspace" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn (MetadataExtractionSql.readEdgeCaseSeed ())
                // The estate shape the canary seed lacks: an active,
                // non-system espace that owns no entities at all.
                do! Deploy.executeBatch cnn
                        "INSERT INTO [dbo].[ossys_Espace] ([Id], [Name], [Is_System], [Is_Active], [EspaceKind], [SS_Key]) \
                         VALUES (800, N'PortalTheme', 0, 1, N'eSpace', '88888888-8888-8888-8888-888888888888');"
                let! snapshotResult =
                    MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                let snapshot =
                    match snapshotResult with
                    | Ok s -> s
                    | Error errors -> failwithf "extraction failed: %A" errors
                let bundle = MetadataSnapshotRunner.toBundle snapshot
                // The erasure is NAMED — exactly one notice, naming the
                // skipped module.
                let notices = OssysRowsetReader.entityLessModules bundle
                let notice = Assert.Single(notices)
                Assert.Equal("adapter.ossys.module.entityLess", notice.Code)
                Assert.Contains("PortalTheme", notice.Message)
                // ...and the catalog parse succeeds without the module —
                // the read that used to die whole with `module.kinds.empty`.
                let! catalogResult =
                    CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
                match catalogResult with
                | Error errors -> Assert.Fail (sprintf "catalog parse failed: %A" errors)
                | Ok catalog ->
                    let names = catalog.Modules |> List.map (fun m -> Name.value m.Name)
                    Assert.DoesNotContain("PortalTheme", names)
                    Assert.Contains("AppCore", names)
                return ()
            }))
