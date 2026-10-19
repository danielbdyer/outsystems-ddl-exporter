namespace Projection.Tests

// THE CSV EXPORT, end-to-end against a real mock OutSystems environment
// (2026-07-10, the csv-destination program): one live cell, a read-only
// export.
//
//   1. WITH the referenced pull: the declared table's CSV lands under its
//      physical name with physical headers; the referenced NON-STATIC parent
//      (City) is pulled KEYED — only the rows the subset points at, not the
//      whole table; the STATIC parent (Country — an injected FK, the seed
//      carries none) is SKIPPED entirely; the manifest records the mapping,
//      the counts, and each table's provenance.
//   2. WITHOUT the pull: only the declared file is written, and the report
//      names every escaping reference (never a refusal — the run narrates
//      the `withReferenced` remedy).
//
// The Customer→Country FK is INJECTED into the cell's metamodel + physical
// table by a row batch (the GoBoardDocker unrelated-cycle precedent): the
// edge-case estate declares Country `staticEntity` but ships no FK into it,
// and the static-skip needs a live edge to hold back.
//
// Serial via Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

[<Xunit.Collection("Docker-SqlServer")>]
type CsvExportDockerTests (fixture: EphemeralContainerFixture) =

    /// Customer gains a nullable COUNTRYID onto the STATIC Country entity:
    /// the physical column plus its `ossys_Entity_Attr` row (the `bt<espace
    /// SS>*<entity SS>` reference encoding the estate uses; Country's espace
    /// RefData carries SS 7777…). Mirrors the CityId attr row's shape.
    let injectCountryFk =
        "ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER] ADD [COUNTRYID] INT NULL; \
         INSERT INTO [dbo].[ossys_Entity_Attr] \
             ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Precision], [Scale], [Default_Value], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Original_Name], [External_Column_Type], [Delete_Rule], [Physical_Column_Name], [Database_Name], [Type], [Legacy_Type], [Decimals], [Original_Type], [Description], [Order_Num]) \
         VALUES \
             (10099, 1000, N'CountryId', 'cccccccc-0000-0000-0000-000000000099', N'Identifier', NULL, NULL, NULL, NULL, 0, 1, 0, 0, NULL, NULL, NULL, N'Protect', N'COUNTRYID', NULL, N'bt77777777-7777-7777-7777-777777777777*bbbbbbbb-0000-0000-0000-000000000040', NULL, NULL, NULL, N'FK to static Country', 60);"

    /// Three cities but only two referenced — the keyed pull must carry
    /// exactly the referenced two. Two countries, both referenced — and
    /// still no Country file (static). Two customers, the declared set.
    let dataRows =
        "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; \
         INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1), (2, N'Porto', 1), (3, N'Faro', 1); \
         SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF; \
         SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] ON; \
         INSERT INTO [dbo].[OSUSR_REF_COUNTRY] ([ID],[CODE],[NAME]) VALUES (100, N'PT', N'Portugal'), (101, N'ES', N'Spain'); \
         SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] OFF; \
         SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
         INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID],[COUNTRYID]) VALUES \
             (10, N'alice@x', N'Alice', N'Almeida, \"the first\"', 1, 100), (11, N'bob@x', N'Bob', N'Barbosa', 2, 101); \
         SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;"

    let csvLines (path: string) : string list =
        (System.IO.File.ReadAllText path).Split("\r\n")
        |> Array.filter (fun s -> s <> "")
        |> List.ofArray

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``csv export: the declared table + the keyed non-static pull land as files; the static parent is skipped; without the pull the escapes are narrated`` () =
        if not (GoBoardFixtures.skipIfNoDocker "CsvExport") then () else
        TaskSync.run (fun () ->
            MockOutSystemsEnv.withMockEnv fixture "CsvExport" "" [ injectCountryFk; dataRows ] MockOutSystemsEnv.ManagedDml
                (fun env ->
                    task {
                        let outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "csvx-" + System.Guid.NewGuid().ToString("N").Substring(0, 8))
                        try
                            // 1. WITH the pull.
                            let! r = CsvExportRun.export MetadataSnapshotRunner.defaultParameters env.EngineConnStr outDir [ "Customer" ] true
                            let report = Result.value r
                            Assert.Empty(report.EscapeLines)   // the pull carries them; nothing to warn about
                            // the declared table: physical file, physical header, both rows.
                            let customerCsv = System.IO.Path.Combine(outDir, "OSUSR_ABC_CUSTOMER.csv")
                            Assert.True(System.IO.File.Exists customerCsv, "the declared table's CSV must exist under its physical name")
                            let customerLines = csvLines customerCsv
                            Assert.StartsWith("ID,", List.head customerLines)
                            Assert.Contains("EMAIL", List.head customerLines)
                            // 2 data rows; the comma-and-quote surname survives the file round-trip.
                            Assert.Equal(3, customerLines.Length)
                            Assert.Contains("\"Almeida, \"\"the first\"\"\"", System.IO.File.ReadAllText customerCsv)
                            // the referenced NON-static parent: pulled KEYED — the
                            // two referenced cities only, never unreferenced Faro.
                            let cityCsv = System.IO.Path.Combine(outDir, "OSUSR_DEF_CITY.csv")
                            Assert.True(System.IO.File.Exists cityCsv, "the referenced non-static parent must be pulled")
                            let cityLines = csvLines cityCsv
                            Assert.Equal(3, cityLines.Length)   // header + Lisbon + Porto
                            Assert.DoesNotContain("Faro", System.IO.File.ReadAllText cityCsv)
                            // the STATIC parent: referenced by both customers, and SKIPPED.
                            Assert.False(System.IO.File.Exists (System.IO.Path.Combine(outDir, "OSUSR_REF_COUNTRY.csv")),
                                         "a static reference table must not be exported")
                            // the manifest: mapping, counts, provenance; no Country entry.
                            let manifest = System.IO.File.ReadAllText report.ManifestPath
                            let doc = System.Text.Json.JsonDocument.Parse manifest
                            let tables =
                                (doc.RootElement.GetProperty "tables").EnumerateArray()
                                |> Seq.map (fun t ->
                                    let str (p: string) = (t.GetProperty p).GetString() |> Option.ofObj |> Option.defaultValue ""
                                    str "entity", str "provenance", (t.GetProperty "rowCount").GetInt32())
                                |> List.ofSeq
                            Assert.Contains(("Customer", "declared", 2), tables)
                            Assert.Contains(("City", "referenced", 2), tables)
                            Assert.DoesNotContain(tables, fun (e, _, _) -> e = "Country")

                            // 2. WITHOUT the pull, into a fresh directory.
                            let outDir2 = outDir + "-bare"
                            let! r2 = CsvExportRun.export MetadataSnapshotRunner.defaultParameters env.EngineConnStr outDir2 [ "Customer" ] false
                            let report2 = Result.value r2
                            Assert.True(System.IO.File.Exists (System.IO.Path.Combine(outDir2, "OSUSR_ABC_CUSTOMER.csv")))
                            Assert.False(System.IO.File.Exists (System.IO.Path.Combine(outDir2, "OSUSR_DEF_CITY.csv")),
                                         "without the pull, only the declared table is written")
                            Assert.NotEmpty(report2.EscapeLines)   // City and Country both named
                            Assert.Contains(report2.EscapeLines, fun (l: string) -> l.Contains "City")
                            Assert.Contains(report2.EscapeLines, fun (l: string) -> l.Contains "Country")
                            return ()
                        finally
                            for d in [ outDir; outDir + "-bare" ] do
                                if System.IO.Directory.Exists d then System.IO.Directory.Delete(d, true)
                    }))
