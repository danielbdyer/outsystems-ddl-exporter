namespace Projection.Tests

// The SequentialAccess row-mapping regression (2026-07-07). The OSSYS
// metadata runner executes with `CommandBehavior.SequentialAccess`; the
// live reader therefore permits each column ordinal to be visited at
// most once, strictly ascending. `mapAttributeRow`'s PhysicalCol
// fallback (NULL `PhysicalColumnName` → upper-cased AttrName — a
// re-read of ordinal 2 after ordinal 17) violated that contract and
// killed BOTH contract reads of a partial transfer, on an estate where
// an attribute's physical name resolves NULL (the physical column is
// absent, so the script's sys.columns backfill cannot supply a name).
// The runner now captures each row at rest — one ascending,
// single-visit sweep — before mapping; this suite pins the estate
// shape that detonated:
//
//   * an ACTIVE attribute whose Physical_Column_Name is NULL and whose
//     Name matches no physical column (an orphan — metadata outliving
//     a dropped column), so NULL survives to the mapper and the
//     fallback path actually executes under SequentialAccess. The
//     edge-case seed alone never fires it (every seeded attribute
//     carries a physical name), which is exactly how the defect
//     shipped past the canary.
//
// Serial via Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

[<Xunit.Collection("Docker-SqlServer")>]
type MetadataSnapshotSequentialAccessTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``a NULL PhysicalColumnName maps under SequentialAccess — the PhysicalCol fallback reads the captured row, not the live reader`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SeqAccessRegression: Docker daemon not reachable."
        else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "SeqAccess" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn (MetadataExtractionSql.readEdgeCaseSeed ())
                // The orphan: active metadata on City (2001), NULL
                // Physical_Column_Name, and a Name ('ArchivedNote')
                // matching no column of OSUSR_DEF_CITY — neither the
                // seed nor the script's catalog backfill can supply a
                // physical name, so the mapper's fallback must fire.
                do! Deploy.executeBatch cnn
                        "INSERT INTO [dbo].[ossys_Entity_Attr] \
                            ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Order_Num]) \
                         VALUES (20014, 2001, N'ArchivedNote', 'dddddddd-0000-0000-0000-000000000004', N'Text', 200, 0, 1, 0, 0, 30);"
                let! snapshotResult =
                    MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                match snapshotResult with
                | Error errors ->
                    Assert.Fail (sprintf "extraction failed: %A" errors)
                | Ok snapshot ->
                    let orphan =
                        snapshot.Attributes |> List.find (fun a -> a.AttrId = 20014)
                    Assert.Equal("ARCHIVEDNOTE", orphan.PhysicalCol)
                    // Neighbors on the same entity keep their real
                    // physical names — the fallback fired for the
                    // orphan only, not as a blanket rewrite.
                    let named =
                        snapshot.Attributes |> List.find (fun a -> a.AttrId = 20012)
                    Assert.Equal("NAME", named.PhysicalCol)
                return ()
            }))
