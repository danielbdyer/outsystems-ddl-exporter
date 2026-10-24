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
// (one notice from `OssysRowsetReader.normalizeBundle`).
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
                let bundle, notices =
                    OssysRowsetReader.normalizeBundle (MetadataSnapshotRunner.toBundle snapshot)
                // The erasure is NAMED — exactly one notice, naming the
                // skipped module.
                let notice = Assert.Single(notices)
                Assert.Equal(OssysRowsetReader.CodeModuleEntityLess, notice.Code)
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

    /// The other real-estate duplicate shape (readiness log Entry 24): an
    /// entity MOVED between modules leaves an INACTIVE `ossys_Entity` row in
    /// the old espace with the SAME SS_Key — under the show-me-everything
    /// read both rows load, and carrying both used to fail the whole read
    /// with `catalog.kinds.duplicateKey` (A4). The normalization drops the
    /// inactive shadow as a named erasure and carries the active entity.
    [<Fact>]
    member _.``an inactive duplicate SS_Key entity (the moved-entity shadow) is dropped as a named erasure — the metamodel read succeeds`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP InactiveShadow: Docker daemon not reachable."
        else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "ShadowKind" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn (MetadataExtractionSql.readEdgeCaseSeed ())
                // The shadow: an inactive entity in a second espace carrying
                // City's SS_Key (bbbbbbbb-…-0002) — the trace of City having
                // been moved out of 'AppCoreOld'. One inactive attribute rides
                // along (the realistic leftover row shape).
                do! Deploy.executeBatch cnn
                        "INSERT INTO [dbo].[ossys_Espace] ([Id], [Name], [Is_System], [Is_Active], [EspaceKind], [SS_Key]) \
                         VALUES (900, N'AppCoreOld', 0, 1, N'eSpace', '99999999-9999-4999-8999-999999999999'); \
                         INSERT INTO [dbo].[ossys_Entity] ([Id], [Name], [Physical_Table_Name], [Espace_Id], [Is_Active], [Is_System], [Is_External], [Data_Kind], [PrimaryKey_SS_Key], [SS_Key], [Description]) \
                         VALUES (9001, N'City', N'OSUSR_OLD_CITY', 900, 0, 0, 0, N'entity', NULL, 'bbbbbbbb-0000-0000-0000-000000000002', NULL); \
                         INSERT INTO [dbo].[ossys_Entity_Attr] ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Physical_Column_Name], [Order_Num]) \
                         VALUES (90011, 9001, N'Id', 'dddddddd-9999-4999-8999-000000000001', N'Identifier', 1, 0, 1, 1, N'ID', 1);"
                let! snapshotResult =
                    MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                let snapshot =
                    match snapshotResult with
                    | Ok s -> s
                    | Error errors -> failwithf "extraction failed: %A" errors
                let bundle, notices =
                    OssysRowsetReader.normalizeBundle (MetadataSnapshotRunner.toBundle snapshot)
                // The shadow drop is NAMED, and dropping AppCoreOld's only
                // entity makes the module itself entity-less — both erasures
                // surface.
                Assert.Contains(notices, fun n -> n.Code = OssysRowsetReader.CodeKindInactiveShadow)
                Assert.Contains(notices, fun n -> n.Code = OssysRowsetReader.CodeModuleEntityLess)
                let! catalogResult =
                    CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
                match catalogResult with
                | Error errors -> Assert.Fail (sprintf "catalog parse failed: %A" errors)
                | Ok catalog ->
                    let names = catalog.Modules |> List.map (fun m -> Name.value m.Name)
                    Assert.DoesNotContain("AppCoreOld", names)
                    // City survives exactly once, in its active home.
                    let cityKinds =
                        Catalog.allKinds catalog
                        |> List.filter (fun k -> Name.value k.Name = "City")
                    Assert.Equal(1, List.length cityKinds)
                return ()
            }))
