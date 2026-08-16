namespace Projection.Tests

// The data-sink chapter's fixture witness (S1; CHAPTER_SINK_OPEN.md). The
// lifecycle seed carries the post-cutover estate shapes the edge-case seed
// deliberately lacks — the tombstoned entity whose table survives, the
// tombstone ↔ extension re-registration pair on one physical table, the
// two-active-claims contested pair, and the orphan physical table with no
// metadata row. This suite pins that a TOTAL read (defaultParameters —
// the sync path's shape) carries every one of those rows into the bundle
// and the catalog: nothing the source said is erased, no shadow collapse
// fires (the pairs carry DISTINCT SS_Keys), and the orphan is invisible
// to the OSSYS read by construction (the residue sweep's motivation, K6).
//
// Serial via Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

[<Xunit.Collection("Docker-SqlServer")>]
type SinkLifecycleSeedTests(fixture: EphemeralContainerFixture) =

    // The catalog-shape assertions, hoisted out of the task CE: a heavy
    // match tree followed by further awaits is an FS3511 state machine
    // Release builds refuse (survival rule 5's family; the chapter-close
    // codification) — the task below keeps only awaits and `use`s.
    let assertCatalogShapes (catalogResult: Result<Catalog>) : unit =
        match catalogResult with
        | Error errors ->
            Assert.Fail (sprintf "catalog parse failed: %A" errors)
        | Ok catalog ->
            let moduleNames = catalog.Modules |> List.map (fun m -> Name.value m.Name)
            Assert.Contains("Fulfillment", moduleNames)
            Assert.Contains("FulfillmentExtension", moduleNames)

            let kinds = Catalog.allKinds catalog
            let named n = kinds |> List.filter (fun k -> Name.value k.Name = n)
            let physicalOf (k: Kind) = TableId.tableText k.Physical
            let isExternalIndirect (k: Kind) =
                match k.Origin with
                | Origin.ExternalIndirect -> true
                | _ -> false

            // The tombstone-only entity: carried, inactive, its
            // attributes riding along — the recoverability substrate.
            let invoice = Assert.Single(named "Invoice")
            Assert.False(invoice.IsActive)
            Assert.Equal("OSUSR_FUL_INVOICE", physicalOf invoice)
            Assert.True(invoice.Attributes |> List.length >= 3)

            // The re-registration pair: one tombstoned Native, one
            // active ExternalIndirect, ONE physical table.
            let shipments = named "Shipment"
            Assert.Equal(2, List.length shipments)
            Assert.All(shipments, fun k -> Assert.Equal("OSUSR_FUL_SHIPMENT", physicalOf k))
            let shipmentTombstone = Assert.Single(shipments |> List.filter (fun k -> not k.IsActive))
            Assert.False(isExternalIndirect shipmentTombstone)
            let shipmentExtension = Assert.Single(shipments |> List.filter (fun k -> k.IsActive))
            Assert.True(isExternalIndirect shipmentExtension)

            // The contested pair: BOTH active, one physical table,
            // distinct origins — exactly the claim set adjudication
            // receives (K5's Contested case).
            let carriers = named "Carrier"
            Assert.Equal(2, List.length carriers)
            Assert.All(carriers, fun k ->
                Assert.True(k.IsActive)
                Assert.Equal("OSUSR_FUL_CARRIER", physicalOf k))
            Assert.Equal(1, carriers |> List.filter isExternalIndirect |> List.length)

            // The orphan: physically present, invisible to OSSYS —
            // no kind claims it (K6's present-but-unclaimed case).
            Assert.DoesNotContain(kinds, fun k -> physicalOf k = "OSUSR_FUL_ARCHIVE")

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``lifecycle seed: a total read carries the tombstone, the extension pair, and the contested pair; the orphan table is invisible to OSSYS`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP SinkLifecycleSeed: Docker daemon not reachable."
        else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "SinkLifecycle" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn (MetadataExtractionSql.readLifecycleSeed ())
                let! snapshotResult =
                    MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                let snapshot =
                    match snapshotResult with
                    | Ok s -> s
                    | Error errors -> failwithf "extraction failed: %A" errors

                // The tombstone's physical join still works under the total
                // read: #PhysTbls carries the deleted entity's surviving table.
                Assert.Contains(snapshot.PhysicalTables, fun (p: MetadataSnapshotRunner.OssysPhysicalTableRow) ->
                    p.EntityId = 8001 && p.TableName = "OSUSR_FUL_INVOICE")

                let bundle, notices =
                    OssysRowsetReader.normalizeBundle (MetadataSnapshotRunner.toBundle snapshot)

                // Distinct SS_Keys ⇒ the inactive-shadow normalization does
                // NOT collapse the pairs; every module has entities. Nothing
                // is erased, so nothing is named.
                Assert.Empty(notices)

                let! catalogResult =
                    CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
                assertCatalogShapes catalogResult

                // ...and the table really is there (sys.tables), so the
                // invisibility is the metadata plane's, never the estate's.
                use probe = cnn.CreateCommand()
                probe.CommandText <- "SELECT COUNT(*) FROM sys.tables WHERE name = N'OSUSR_FUL_ARCHIVE';"
                let! present = probe.ExecuteScalarAsync()
                Assert.Equal(1, unbox<int> present)
                return ()
            }))
