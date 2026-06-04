namespace Projection.Tests

// ---------------------------------------------------------------------------
// Track W1-A seam T1 — the production CDC capture-count reader's
// discriminating witness.
//
// `ReadSide.cdcCaptureCount` is the "Measure" leg the change-over-time
// proteins (X1/X4/X5/X8) need: the change-measure `‖·‖` of
// `WAVE_6_ALGEBRA.md`, physically the CDC capture count. Until W1-A it
// existed only in the test harness (`CdcSilenceCrossEmitterTests`'
// `countTotalCaptures`); this witness pins the PRODUCTION reader.
//
// The witness discriminates the criterion with EXACT counts (not `>`):
//
//   - No-op leg: an idempotent redeploy / no data change ⇒ delta == 0.
//     An over-capturing or double-counting reader turns this RED.
//   - Change/INSERT leg: one INSERT ⇒ delta == +1 (exactly one capture
//     row). An over-counting reader (e.g. summing a CT table twice, or
//     including a stale CT relation) turns this RED.
//   - Change/UPDATE leg: one UPDATE under @supports_net_changes=0 logs a
//     delete-image + insert-image PAIR ⇒ delta == +2. An under-counting
//     reader (e.g. counting DISTINCT keys, or net-changes) turns this RED.
//
// Docker test: `Docker-SqlServer` collection + `IsolatedContainerFixture`
// (one ephemeral CDC-suitable container per class) + `TaskSync.run` for
// the sync-over-async boundary (per the tiered-test-runner discipline).
// CDC capture is forced synchronously via `sys.sp_cdc_scan` (Agent-free,
// works in the warm container).
// ---------------------------------------------------------------------------

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Adapters.Sql
open Projection.Pipeline

module private CdcMeasureFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    // A single CDC-tracked table, schema `dbo`, name `CDCM_COUNTRY`, so the
    // capture-table name SQL Server derives is `cdc.dbo_CDCM_COUNTRY_CT` —
    // exactly what `ReadSide.cdcCaptureCount` reconstructs from the tracked
    // `dbo.CDCM_COUNTRY` discovery string.
    let schemaSql : string =
        "CREATE TABLE dbo.CDCM_COUNTRY ( \
           Id INT NOT NULL PRIMARY KEY, \
           Code NVARCHAR(50) NOT NULL, \
           Label NVARCHAR(200) NOT NULL );"

    let enableCdcSql : string =
        "EXEC sys.sp_cdc_enable_table \
           @source_schema=N'dbo', \
           @source_name=N'CDCM_COUNTRY', \
           @role_name=NULL, \
           @supports_net_changes=0;"

    /// Force a synchronous CDC capture pass, then sum the production
    /// capture-count reader over the discovered tracked tables.
    let scanAndMeasure (cnn: SqlConnection) : Task<int> =
        task {
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"
            let! tracked = ReadSide.cdcTrackedTables cnn
            return! ReadSide.cdcCaptureCount cnn tracked
        }

open CdcMeasureFixtures

[<Xunit.Collection("Docker-SqlServer")>]
type CdcMeasureTests(fixture: IsolatedContainerFixture) =

    interface IClassFixture<IsolatedContainerFixture>

    [<Fact>]
    member _.``W1-A seam T1: cdcCaptureCount no-op leg — idempotent redeploy with no data change yields delta 0`` () =
        if not (skipIfNoDocker "cdc-measure-noop") then () else

        let baseline, post =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "CdcMeasureNoop" (fun cnn _ -> task {
                    do! Deploy.executeBatch cnn schemaSql
                    do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
                    do! Deploy.executeBatch cnn enableCdcSql

                    // Seed two rows → establishes a non-zero baseline (proves
                    // the reader is actually counting CDC traffic).
                    do! Deploy.executeBatch cnn
                            "INSERT INTO dbo.CDCM_COUNTRY (Id, Code, Label) VALUES \
                               (1, N'US', N'United States'), (2, N'CA', N'Canada');"
                    let! baseline = scanAndMeasure cnn

                    // No-op leg: an idempotent redeploy that changes no data.
                    // `CREATE TABLE IF NOT EXISTS`-shaped guard + a self-
                    // assigning UPDATE that touches NO column value.
                    do! Deploy.executeBatch cnn
                            "IF OBJECT_ID('dbo.CDCM_COUNTRY') IS NULL \
                               CREATE TABLE dbo.CDCM_COUNTRY (Id INT NOT NULL PRIMARY KEY, Code NVARCHAR(50) NOT NULL, Label NVARCHAR(200) NOT NULL);"
                    let! post = scanAndMeasure cnn
                    return baseline, post
                }))

        // The seed fired CDC traffic, so the reader sees a real, non-zero
        // baseline (otherwise the witness isn't exercising CDC at all).
        Assert.Equal (2, baseline)
        // THE NO-OP DISCRIMINATOR: zero data change ⇒ exactly zero new
        // capture rows. Exact equality (not `<=`) — an over-capturing or
        // double-counting reader turns this RED.
        Assert.Equal (baseline, post)

    [<Fact>]
    member _.``W1-A seam T1: cdcCaptureCount change leg — one INSERT yields delta +1, one UPDATE yields delta +2`` () =
        if not (skipIfNoDocker "cdc-measure-change") then () else

        let baseline, afterInsert, afterUpdate =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "CdcMeasureChange" (fun cnn _ -> task {
                    do! Deploy.executeBatch cnn schemaSql
                    do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
                    do! Deploy.executeBatch cnn enableCdcSql

                    do! Deploy.executeBatch cnn
                            "INSERT INTO dbo.CDCM_COUNTRY (Id, Code, Label) VALUES \
                               (1, N'US', N'United States');"
                    let! baseline = scanAndMeasure cnn

                    // INSERT leg: exactly one new row ⇒ exactly +1 capture.
                    do! Deploy.executeBatch cnn
                            "INSERT INTO dbo.CDCM_COUNTRY (Id, Code, Label) VALUES \
                               (2, N'CA', N'Canada');"
                    let! afterInsert = scanAndMeasure cnn

                    // UPDATE leg: change one column on one row. Under
                    // @supports_net_changes=0 CDC logs a delete-image +
                    // insert-image PAIR ⇒ exactly +2 captures.
                    do! Deploy.executeBatch cnn
                            "UPDATE dbo.CDCM_COUNTRY SET Label = N'USA' WHERE Id = 1;"
                    let! afterUpdate = scanAndMeasure cnn
                    return baseline, afterInsert, afterUpdate
                }))

        // Baseline: one seed INSERT ⇒ exactly one capture row.
        Assert.Equal (1, baseline)
        // THE INSERT DISCRIMINATOR: one INSERT ⇒ exactly +1 capture row.
        Assert.Equal (baseline + 1, afterInsert)
        // THE UPDATE DISCRIMINATOR: one UPDATE ⇒ exactly +2 capture rows
        // (delete-image + insert-image under @supports_net_changes=0).
        // An under-counting reader (DISTINCT-key / net-changes) turns this
        // RED; an over-capturing one overshoots +2.
        Assert.Equal (afterInsert + 2, afterUpdate)
