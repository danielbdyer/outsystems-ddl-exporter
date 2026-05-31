module Projection.Tests.VerifyDataIntegrationTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql

// ---------------------------------------------------------------------------
// Slice 4.4 — `osm verify-data` post-deploy integrity gate (Docker-gated).
//
// DataIntegrityChecker.compare captures both deployments' exact aggregate
// evidence (LiveProfiler RowCount + per-attribute NullCounts) and diffs them
// in pure F#. The acceptance: against two deployments of the same schema +
// seed, a mutation of ONE table on the after side surfaces EXACTLY that
// table's row-count delta — no false positives on the untouched table.
//
// The schema contract is read back from the before deployment via
// ReadSide.read (the same derivation the CLI verb uses), so the report's
// SsKeys are the reconstructed identities both sides share.
//
// No Docker → skip (mirrors LiveProfilerIntegrationTests). The
// Docker-SqlServer collection keeps these serial + isolated from the pure
// pool (OOM discipline).
// ---------------------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type VerifyDataIntegrationTests(fixture: EphemeralContainerFixture) =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    let mustOk (r: Result<'a>) : 'a =
        match r with
        | Ok v -> v
        | Error es -> Assert.Fail(sprintf "expected Ok; got %A" es); Unchecked.defaultof<'a>

    // Two non-static tables; ORDERS carries a nullable NOTE column so the
    // null-count axis is exercisable. No FK — verify-data diffs row/null
    // aggregates, which are FK-independent.
    let ddl : string =
        "CREATE TABLE [dbo].[OSUSR_VD_ITEMS] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL);" +
        "CREATE TABLE [dbo].[OSUSR_VD_ORDERS] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NOTE] NVARCHAR(50) NULL);"

    let seed : string =
        "INSERT INTO [dbo].[OSUSR_VD_ITEMS] ([ID], [NAME]) VALUES " +
        "(1, N'alpha'), (2, N'beta'), (3, N'gamma');" +
        "INSERT INTO [dbo].[OSUSR_VD_ORDERS] ([ID], [NOTE]) VALUES " +
        "(1, N'first'), (2, N'second');"

    /// Deploy ddl+seed to two fresh databases, apply `mutateAfter` to the
    /// after database only, then diff. Returns the report + the contract
    /// (so callers can map physical tables to the report's SsKeys).
    let runScenario (mutateAfter: string option) : Task<IntegrityReport * Catalog> =
        fixture.WithEphemeralDatabase "VerifyBefore" (fun before _ -> task {
            do! Deploy.executeBatch before ddl
            do! Deploy.executeBatch before seed
            return!
                fixture.WithEphemeralDatabase "VerifyAfter" (fun after _ -> task {
                    do! Deploy.executeBatch after ddl
                    do! Deploy.executeBatch after seed
                    match mutateAfter with
                    | Some sql -> do! Deploy.executeBatch after sql
                    | None     -> ()
                    let! contractR = ReadSide.read before
                    let contract = mustOk contractR
                    let! reportR = DataIntegrityChecker.compare before after contract
                    return (mustOk reportR, contract)
                })
        })

    let ssKeyOfTable (contract: Catalog) (physicalTable: string) : SsKey =
        Catalog.allKinds contract
        |> List.find (fun k -> k.Physical.Table = physicalTable)
        |> fun k -> k.SsKey

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``verify-data flags exactly the mutated row-count delta`` () =
        if not (skipIfNoDocker "verify-data-rowcount-delta") then () else
        // Insert 3 extra ITEMS rows on the after side; ORDERS untouched.
        let mutate =
            "INSERT INTO [dbo].[OSUSR_VD_ITEMS] ([ID], [NAME]) VALUES " +
            "(4, N'delta'), (5, N'epsilon'), (6, N'zeta');"
        let report, contract = (runScenario (Some mutate)).GetAwaiter().GetResult()
        // EXACTLY one row-count delta, and it is ITEMS with +3.
        Assert.Equal(1, report.RowCountDeltas.Length)
        let delta = List.head report.RowCountDeltas
        Assert.Equal(ssKeyOfTable contract "OSUSR_VD_ITEMS", delta.Kind)
        Assert.Equal(3L, delta.Before)
        Assert.Equal(6L, delta.After)
        // No false positives: the untouched ORDERS table is absent, and the
        // inserted rows carry non-null NAME so no null-count divergence fires.
        Assert.Empty(report.NullCountDeltas)
        Assert.Empty(report.Warnings)
        Assert.False(DataIntegrityChecker.isClean report)

    [<Fact>]
    member _.``verify-data reports clean for identical deployments`` () =
        if not (skipIfNoDocker "verify-data-clean") then () else
        let report, _ = (runScenario None).GetAwaiter().GetResult()
        Assert.True(
            DataIntegrityChecker.isClean report,
            sprintf "expected clean; got %A" report)

    [<Fact>]
    member _.``verify-data flags a per-column null-count divergence`` () =
        if not (skipIfNoDocker "verify-data-nullcount-delta") then () else
        // One extra ORDERS row with NULL NOTE on the after side: both the
        // row count (+1) and NOTE's null count (+1) diverge.
        let mutate =
            "INSERT INTO [dbo].[OSUSR_VD_ORDERS] ([ID], [NOTE]) VALUES (3, NULL);"
        let report, contract = (runScenario (Some mutate)).GetAwaiter().GetResult()
        let ordersKey = ssKeyOfTable contract "OSUSR_VD_ORDERS"
        let noteKey =
            Catalog.allKinds contract
            |> List.find (fun k -> k.SsKey = ordersKey)
            |> fun k -> k.Attributes |> List.find (fun a -> a.Column.ColumnName = "NOTE")
            |> fun a -> a.SsKey
        // The NOTE null count went 0 -> 1 on ORDERS.
        let nullDelta =
            report.NullCountDeltas
            |> List.find (fun d -> d.Kind = ordersKey && d.Attribute = noteKey)
        Assert.Equal(0L, nullDelta.Before)
        Assert.Equal(1L, nullDelta.After)
        // The row count moved too (2 -> 3 on ORDERS).
        let rowDelta = report.RowCountDeltas |> List.find (fun d -> d.Kind = ordersKey)
        Assert.Equal(2L, rowDelta.Before)
        Assert.Equal(3L, rowDelta.After)
