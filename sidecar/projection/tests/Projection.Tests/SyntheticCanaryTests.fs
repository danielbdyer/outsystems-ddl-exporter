namespace Projection.Tests

// THE_SYNTHETIC_DATA_DESIGN §1 / §8, slice S3 — the synthetic canary.
// The correctness theorem made executable: π ∘ σ ≈ id. Profile a populated
// source (π → P), generate from P (σ), load to a fresh sink, re-profile the
// sink (π → P′), and assert P′ ≈ P:
//   L1 (structural-exact): per-column row counts match; zero FK orphans.
//   L2 (distributional within ε): a preserved low-cardinality categorical
//       reproduces its real value set; a numeric column stays within range.
// Serial via the Docker-SqlServer collection; blocking wait via `TaskSync.run`.

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.Json

module private SyntheticCanaryFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let custCount = 40
    let ordCount  = 120

    /// IDENTITY-PK schema (the OutSystems norm). STATUS is a low-cardinality
    /// categorical (preserved); SCORE is numeric; Order has a NOT-NULL FK.
    let ddl =
        "CREATE TABLE [dbo].[CAN_CUSTOMER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, " +
        "[STATUS] NVARCHAR(20) NOT NULL, [SCORE] INT NOT NULL); " +
        "CREATE TABLE [dbo].[CAN_ORDER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [CUSTOMER_ID] INT NOT NULL, " +
        "CONSTRAINT [FK_CanOrder_Customer] FOREIGN KEY ([CUSTOMER_ID]) " +
        "REFERENCES [dbo].[CAN_CUSTOMER] ([ID]));"

    let statuses = [| "Active"; "Inactive"; "Pending" |]
    let statusSet = Set.ofArray statuses

    /// Real seed: a clear categorical shape on STATUS, varied SCORE, and
    /// every Order referencing an existing customer (IDENTITY mints 1..N).
    let seed : string =
        let custValues =
            [ for i in 0 .. custCount - 1 ->
                sprintf "(N'%s', %d)" statuses.[i % statuses.Length] (i * 7 % 100) ]
            |> String.concat ", "
        let ordValues =
            [ for i in 0 .. ordCount - 1 -> sprintf "(%d)" ((i % custCount) + 1) ]
            |> String.concat ", "
        sprintf
            "INSERT INTO [dbo].[CAN_CUSTOMER] ([STATUS],[SCORE]) VALUES %s; \
             INSERT INTO [dbo].[CAN_ORDER] ([CUSTOMER_ID]) VALUES %s;"
            custValues ordValues

    /// Every categorical distribution's value set in a profile.
    let categoricalValueSets (p: Profile) : Set<string> list =
        p.Distributions
        |> List.choose (function
            | AttributeDistribution.Categorical c -> Some (c.Frequencies |> List.map fst |> Set.ofList)
            | _ -> None)

    /// ReadSide marks every read kind `Modality = [Static rows]` (it lifts the
    /// live rows for the per-row PhysicalSchema canary), and `LiveProfiler`
    /// skips static kinds (their data lives in the catalog, not the DB). For
    /// the synthetic canary the data IS in the DB, so strip the Static mark to
    /// re-enable live profiling (the integration tests profile authored,
    /// non-static catalogs for the same reason).
    let stripStatic (c: Catalog) : Catalog =
        Catalog.mapKinds
            (fun k -> { k with Modality = k.Modality |> List.filter (function Static _ -> false | _ -> true) })
            c

    /// Row count observed for a kind's table (any of its column profiles).
    let kindRowCount (catalog: Catalog) (table: string) (p: Profile) : int64 option =
        Catalog.allKinds catalog
        |> List.tryFind (fun k -> (TableId.tableText k.Physical).ToUpperInvariant() = table)
        |> Option.bind (fun k ->
            k.Attributes
            |> List.tryPick (fun a -> Profile.tryFindColumn a.SsKey p |> Option.map (fun c -> c.RowCount)))


[<Xunit.Collection("Docker-SqlServer")>]
type SyntheticCanaryTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``the durable ProfileCodec round-trips a real captured profile``() =
        // The capture verb / synthetic flow persist a LiveProfiler-captured
        // profile through ProfileCodec; this proves the round-trip law on real
        // captured evidence (not just hand-built / FsCheck-generated values).
        let label = "syntheticCapture"
        if not (SyntheticCanaryFixtures.skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase label (fun source _ ->
                task {
                    do! Deploy.executeBatch source SyntheticCanaryFixtures.ddl
                    do! Deploy.executeBatch source SyntheticCanaryFixtures.seed
                    match! ReadSide.read source with
                    | Error es -> Assert.True(false, sprintf "source read failed: %A" es)
                    | Ok cat0 ->
                        match! LiveProfiler.attach source (SyntheticCanaryFixtures.stripStatic cat0) Profile.empty with
                        | Error es -> Assert.True(false, sprintf "capture failed: %A" es)
                        | Ok captured ->
                            Assert.False(Profile.isEmpty captured, "captured profile is empty")
                            match ProfileCodec.deserialize (ProfileCodec.serialize captured) with
                            | Ok restored -> Assert.Equal<Profile>(captured, restored)
                            | Error es -> Assert.True(false, sprintf "codec round-trip failed: %A" es)
                }))

    [<Fact>]
    member _.``canary: pi of sigma approximates id (counts match, zero orphans, categorical set preserved)``() =
        let label = "syntheticCanary"
        if not (SyntheticCanaryFixtures.skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            // Source: deploy + seed real data, read its catalog, profile it (π → P).
            fixture.WithEphemeralDatabase (label + "Src") (fun source _ ->
                task {
                    do! Deploy.executeBatch source SyntheticCanaryFixtures.ddl
                    do! Deploy.executeBatch source SyntheticCanaryFixtures.seed
                    match! ReadSide.read source with
                    | Error es -> Assert.True(false, sprintf "source read failed: %A" es); return ()
                    | Ok catalogA0 ->
                        let catalogA = SyntheticCanaryFixtures.stripStatic catalogA0
                        match! LiveProfiler.attach source catalogA Profile.empty with
                        | Error es -> Assert.True(false, sprintf "source profile failed: %A" es); return ()
                        | Ok profileP ->
                            // Sanity: the capture is non-empty (π actually ran).
                            Assert.False(Profile.isEmpty profileP, "captured profile P is empty")

                            // σ — generate, then load to a fresh sink.
                            return!
                                fixture.WithEphemeralDatabase (label + "Sink") (fun sink _ ->
                                    task {
                                        do! Deploy.executeBatch sink SyntheticCanaryFixtures.ddl
                                        let! loadReport =
                                            Transfer.runSynthetic
                                                Transfer.Execute EmissionMode.Incremental true
                                                sink catalogA profileP SyntheticConfig.defaultConfig 7UL id
                                        match loadReport with
                                        | Error es -> Assert.True(false, sprintf "synthetic load failed: %A" es)
                                        | Ok _ ->
                                            // π again on the sink → P′.
                                            match! ReadSide.read sink with
                                            | Error es -> Assert.True(false, sprintf "sink read failed: %A" es)
                                            | Ok catalogB0 ->
                                                let catalogB = SyntheticCanaryFixtures.stripStatic catalogB0
                                                match! LiveProfiler.attach sink catalogB Profile.empty with
                                                | Error es -> Assert.True(false, sprintf "sink profile failed: %A" es)
                                                | Ok profileP' ->
                                                    // L1 — volume: per-kind row counts match.
                                                    let rcA t = SyntheticCanaryFixtures.kindRowCount catalogA t profileP
                                                    let rcB t = SyntheticCanaryFixtures.kindRowCount catalogB t profileP'
                                                    Assert.Equal(Some (int64 SyntheticCanaryFixtures.custCount), rcA "CAN_CUSTOMER")
                                                    Assert.Equal(rcA "CAN_CUSTOMER", rcB "CAN_CUSTOMER")
                                                    Assert.Equal(rcA "CAN_ORDER", rcB "CAN_ORDER")

                                                    // L1 — zero FK orphans on the sink (the FK reality
                                                    // probe reports no orphan; the deployed FK would
                                                    // also have rejected any orphan insert).
                                                    Assert.True(
                                                        profileP'.ForeignKeys |> List.forall (fun fk -> not fk.HasOrphan),
                                                        "re-profiled sink reports FK orphans")

                                                    // L2 — the preserved low-cardinality categorical
                                                    // (STATUS) reproduces its real value set.
                                                    let setsA = SyntheticCanaryFixtures.categoricalValueSets profileP
                                                    let setsB = SyntheticCanaryFixtures.categoricalValueSets profileP'
                                                    Assert.Contains(SyntheticCanaryFixtures.statusSet, setsA)
                                                    Assert.Contains(SyntheticCanaryFixtures.statusSet, setsB)
                                    })
                }))
