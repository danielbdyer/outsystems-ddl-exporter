module Twin.Tests.Integration.TwinCrossoverMergeTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// `twin evidence merge`, end to end on the estate fixture: two fabricated
// per-environment packs → clamp against the TRUNK (drift named per
// environment) → the crossover → the merged pack lands where the mint
// already looks — and Mint.prepare then carries the extremes with zero
// mint changes (PROVING_SURFACE_DESIGN §5.2, the drop-in claim).
// ---------------------------------------------------------------------------

/// Estate files only — the twin container itself never starts here: the
/// merge's trunk binding rides the warm-honoring acquisition.
type TwinCrossoverMergeFixture () =
    inherit TwinEstateFixture ("twin-e2e-crossover", 21835)

[<Collection("Twin-Docker")>]
type TwinCrossoverMergeTests (fixture: TwinCrossoverMergeFixture) =

    interface IClassFixture<TwinCrossoverMergeFixture>

    [<Fact>]
    member _.``merge clamps to the trunk, attributes winners, and the mint reads the extremes unchanged`` () : Task =
        task {
            let col name rows nulls =
                { Column = name; RowCount = rows; NullCount = nulls; MaxLength = None
                  DistinctCount = None; Truncated = false; HasDuplicates = false
                  Frequencies = []; Numeric = None; Text = None }
            let devPack =
                { Evidence.emptyPack RichTier with
                    Sources = [ "dev" ]
                    Tables =
                        [ { Table = "dbo.Customer"; RowCount = 100L
                            Columns = [ col "Email" 100L 5L ] } ] }
            let qaPack =
                { Evidence.emptyPack RichTier with
                    Sources = [ "qa" ]
                    Tables =
                        [ { Table = "dbo.Customer"; RowCount = 40L
                            // LegacyCode exists only in QA — cutover drift the
                            // trunk does not carry; the clamp must report it.
                            Columns = [ col "Email" 40L 20L; col "LegacyCode" 40L 0L ] } ]
                    Orphans =
                        [ { ChildTable = "dbo.Order"; ChildColumn = "CustomerId"
                            ParentTable = "dbo.Customer"; OrphanCount = 4L } ] }
            fixture.Rewrite "twin/dev.rich.json" (Evidence.serialize devPack)
            fixture.Rewrite "twin/qa.rich.json" (Evidence.serialize qaPack)

            let configJson =
                fixture.ConfigJson.Replace(
                    "\"seed\": 7,",
                    "\"seed\": 7,\n  \"evidence\": { \"rich\": \"twin/merged.rich.json\",\n    \"merge\": { \"inputs\": [ \"twin/dev.rich.json\", \"twin/qa.rich.json\" ] } },")
            let config =
                match TwinConfig.parse configJson with
                | Ok c -> c
                | Error es -> failwithf "merge config refused: %A" (es |> List.map (fun e -> e.Code, e.Metadata))

            let! run = EvidenceMerge.run fixture.Root config
            match run with
            | Error es -> failwithf "merge refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
            | Ok report ->
                Assert.Equal(1, report.MergedTables)
                Assert.True(report.DriftCount >= 1)
                Assert.True(System.IO.File.Exists report.RichPath)
                Assert.True(System.IO.File.Exists report.ReportPath)
                // The witness pair lands beside the merged pack, and both
                // undeliverable realities are NAMED skips, never planted
                // witnesses: the orphan edge is enforced by the trunk
                // (Order.CustomerId carries a reference), and QA's null rate
                // sits on a column the trunk declares NOT NULL — the
                // make-mandatory drift the promotion story owns.
                Assert.True(System.IO.File.Exists report.WitnessSqlPath)
                Assert.True(System.IO.File.Exists report.WitnessAssertPath)
                Assert.Equal(2, report.WitnessSkips)
                let witnessReport = System.IO.File.ReadAllText report.ReportPath
                Assert.Contains("enforcedReference", witnessReport)
                Assert.Contains("notNullable", witnessReport)
                let reportJson = System.IO.File.ReadAllText report.ReportPath
                // The QA-only column is drift, named to its environment; the
                // null-rate winner is QA.
                Assert.Contains("columnNotInTrunk", reportJson)
                Assert.Contains("LegacyCode", reportJson)
                Assert.Contains("\"winner\": \"qa\"", reportJson)

                // The merged pack: QA's 50% rate at Dev's 100-row volume.
                let merged =
                    match Evidence.deserialize (System.IO.File.ReadAllText report.RichPath) with
                    | Ok p -> p
                    | Error es -> failwithf "merged pack unreadable: %A" (es |> List.map (fun e -> e.Code))
                let email = (List.exactlyOne merged.Tables).Columns |> List.exactlyOne
                Assert.Equal(100L, email.RowCount)
                Assert.Equal(50L, email.NullCount)
                Assert.Equal(4L, (List.exactlyOne merged.Orphans).OrphanCount)

                // Zero mint changes: the merged pack sits at evidence.rich, so
                // Mint.prepare binds it against the trunk and the plan's
                // Profile carries the extremes.
                let! trunk = TrunkModel.readback fixture.Root config
                match trunk with
                | Error es -> failwithf "trunk readback refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                | Ok catalog ->
                    let pools = Readback.providedPools catalog
                    match Mint.prepare fixture.Root config "default" catalog pools with
                    | Error es -> failwithf "mint prepare refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                    | Ok plan ->
                        let index = CatalogIndex.ofCatalog catalog
                        let emailKey =
                            match TableCoordinate.parse "dbo.Customer" with
                            | Error es -> failwithf "coordinate refused: %A" es
                            | Ok coord ->
                                match ColumnCoordinate.create coord "Email" |> Result.bind (CatalogIndex.bindColumn index) with
                                | Ok (_, attr) -> attr.SsKey
                                | Error es -> failwithf "Email did not bind: %A" (es |> List.map (fun e -> e.Code))
                        match Profile.tryFindColumn emailKey plan.Profile with
                        | Some c ->
                            Assert.Equal(100L, c.RowCount)
                            Assert.Equal(50L, c.NullCount)
                        | None -> failwith "the merged Email evidence did not reach the mint's profile"
                        let fk = List.exactlyOne plan.Profile.ForeignKeys
                        Assert.True fk.HasOrphan
                        Assert.Equal(4L, fk.OrphanCount)
        }
