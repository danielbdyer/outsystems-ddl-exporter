module Twin.Tests.Integration.TwinCrossoverRehearsalTests

open System.Threading.Tasks
open System.Text.Json
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// The three-environment rehearsal — the program's acceptance gate
// (PROVING_SURFACE_DESIGN §5.2). Three fabricated environments carry
// DIVERGENT realities on one trunk: QA holds the worst null rate and the
// duplicate emails; UAT holds the longest email, the widest numeric
// envelope, and orphans on an edge the trunk does not constrain (recorded
// through a WITH NOCHECK foreign key added on the UAT copy alone — the
// capture-side reference π measures orphan reality against). The loop under
// proof, exactly as the capture point will run it:
//
//   capture ×3 → merge (attribution per winner) → mint from the merged pack
//   → execute the witness pair (failures = 0) → the per-environment fidelity
//   audit (zero blocking failures) → block-equivalence live: the FK-add
//   blocks Msg 547 on UAT's orphans, the unique-add blocks Msg 1505 on QA's
//   duplicate — each blocking fact traceable to the environment that
//   supplied it.
// ---------------------------------------------------------------------------

/// Estate files + the twin container for the minted template; the three
/// environment copies ride the warm-honoring container acquisition.
type TwinCrossoverRehearsalFixture () =
    inherit TwinEstateFixture ("twin-e2e-rehearsal", 21845)

[<Collection("Twin-Docker")>]
type TwinCrossoverRehearsalTests (fixture: TwinCrossoverRehearsalFixture) =

    // ------------------------------------------------------------------
    // The rehearsal trunk: Email loosened to NULL-able (QA's nulls must
    // be representable), Score added (the numeric envelope's seat), and
    // RegionId added WITHOUT a reference (the FK-add case's seat) beside
    // a Region parent the trunk carries but never enforces.
    // ------------------------------------------------------------------

    static let customerBase =
        """CREATE TABLE [dbo].[Customer] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)  NOT NULL,
    [Email]     NVARCHAR(250)  NULL,
    [StatusId]  INT            NOT NULL,
    [CreatedOn] DATETIME2      NOT NULL,
    [Score]     INT            NULL,
    [RegionId]  INT            NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    static let customerWithRegionFk =
        customerBase.Replace(
            "    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])",
            "    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]),\n    CONSTRAINT [FK_Customer_Region] FOREIGN KEY ([RegionId]) REFERENCES [dbo].[Region] ([Id])")

    static let regionTable =
        """CREATE TABLE [dbo].[Region] (
    [Id]   INT           NOT NULL,
    [Name] NVARCHAR(50)  NOT NULL,
    CONSTRAINT [PK_Region] PRIMARY KEY ([Id])
);
"""

    static let uniqueEmailIndex =
        "CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email] ON [dbo].[Customer] ([Email]) WHERE [Email] IS NOT NULL;\n"

    // ------------------------------------------------------------------
    // The three environments' dirt. Every value below is synthetic; the
    // XSECRET-style capture-leak checks live in the pure pools — here
    // the point is DIVERGENCE with a known winner per statistic.
    //   Email nulls:   dev 0/25 · qa 8/20 (the 40% winner) · uat 0/15
    //   Email length:  dev 15 · qa 15 · uat 120 (the winner)
    //   Email dupes:   qa only ('dupe@qa.example' ×3)
    //   Score:         all 20% null; envelope dev [10,86] · qa [5,95]
    //                  · uat [-5,120] (both edges' winner)
    //   RegionId:      dev 18/25 null · qa 14/20 · uat 9/15; values 1..3
    //                  everywhere plus UAT's orphans 9001..9003
    //   Names:         'Common' + one env-exclusive value each
    // ------------------------------------------------------------------

    static let regionSeed =
        "INSERT INTO [dbo].[Region] ([Id], [Name]) VALUES (1, N'North'), (2, N'South'), (3, N'East');"

    static let devRows =
        """DECLARE @i INT = 1;
WHILE @i <= 25
BEGIN
    INSERT INTO [dbo].[Customer] ([Name], [Email], [StatusId], [CreatedOn], [Score], [RegionId])
    VALUES (
        CASE WHEN @i >= 24 THEN N'COMMON' WHEN @i <= 15 THEN N'Common' ELSE N'DevOnly' END,
        CONCAT(N'dev', @i, N'@x.example'),
        1 + (@i % 3),
        DATEADD(DAY, @i, '2026-01-01'),
        CASE WHEN @i <= 5 THEN NULL ELSE 10 + (@i - 6) * 4 END,
        CASE WHEN @i <= 18 THEN NULL ELSE 1 + (@i % 3) END);
    SET @i = @i + 1;
END
"""

    static let qaRows =
        """DECLARE @i INT = 1;
WHILE @i <= 20
BEGIN
    INSERT INTO [dbo].[Customer] ([Name], [Email], [StatusId], [CreatedOn], [Score], [RegionId])
    VALUES (
        CASE WHEN @i >= 19 THEN N'' WHEN @i <= 12 THEN N'Common' ELSE N'QaOnly' END,
        CASE WHEN @i <= 8 THEN NULL
             WHEN @i <= 11 THEN N'dupe@qa.example'
             ELSE CONCAT(N'qa', @i, N'@y.example') END,
        1 + (@i % 3),
        DATEADD(DAY, @i, '2026-02-01'),
        CASE WHEN @i <= 4 THEN NULL ELSE 5 + (@i - 5) * 6 END,
        CASE WHEN @i <= 14 THEN NULL ELSE 1 + (@i % 3) END);
    SET @i = @i + 1;
END
"""

    static let uatRows =
        """DECLARE @i INT = 1;
WHILE @i <= 15
BEGIN
    INSERT INTO [dbo].[Customer] ([Name], [Email], [StatusId], [CreatedOn], [Score], [RegionId])
    VALUES (
        CASE WHEN @i >= 14 THEN N'UatOnly ' WHEN @i <= 9 THEN N'Common' ELSE N'UatOnly' END,
        CASE WHEN @i = 1 THEN CONCAT(REPLICATE(N'x', 108), N'@uat.example')
             ELSE CONCAT(N'uat', @i, N'@z.example') END,
        1 + (@i % 3),
        DATEADD(DAY, @i, '2026-03-01'),
        CASE WHEN @i <= 3 THEN NULL
             WHEN @i = 4 THEN -5
             WHEN @i = 15 THEN 120
             ELSE 10 + @i END,
        CASE WHEN @i <= 9 THEN NULL
             WHEN @i <= 12 THEN @i - 9
             ELSE 8988 + @i END);
    SET @i = @i + 1;
END
"""

    /// The capture-side reference: added on the UAT copy alone, AFTER the
    /// orphan rows land (WITH NOCHECK skips existing rows only — a
    /// pre-existing FK would refuse the orphan INSERTs).
    static let uatNocheckFk =
        "ALTER TABLE [dbo].[Customer] WITH NOCHECK ADD CONSTRAINT [FK_Customer_Region] FOREIGN KEY ([RegionId]) REFERENCES [dbo].[Region] ([Id]);"

    // ------------------------------------------------------------------
    // Support.
    // ------------------------------------------------------------------

    interface IClassFixture<TwinCrossoverRehearsalFixture>

    member private _.ParseConfig (json: string) : TwinConfig =
        match TwinConfig.parse json with
        | Ok c -> c
        | Error es -> failwithf "rehearsal config refused: %A" (es |> List.map (fun e -> e.Code, e.Metadata))

    /// One capture config per environment — law 4 holds: each import sees
    /// exactly one source and writes its own rich pack.
    member private this.CaptureConfig (name: string) (connVar: string) : TwinConfig =
        this.ParseConfig
            (fixture.ConfigJson.Replace(
                "\"seed\": 7,",
                System.String.Concat(
                    "\"seed\": 7,\n  \"evidence\": { \"rich\": \"twin/", name, ".rich.json\",\n",
                    "    \"sources\": [ { \"name\": \"", name, "\", \"rendition\": \"logical\", \"conn\": \"env:", connVar, "\",\n",
                    "      \"tables\": [\"dbo.Customer\", \"dbo.Region\"] } ] },")))

    /// The merge/mint/audit config: the merged pack lands where the mint
    /// already looks; the audit reads the same inputs.
    member private this.MergeConfig () : TwinConfig =
        this.ParseConfig
            (fixture.ConfigJson.Replace(
                "\"seed\": 7,",
                "\"seed\": 7,\n  \"evidence\": { \"rich\": \"twin/merged.rich.json\",\n    \"merge\": { \"inputs\": [ \"twin/dev.rich.json\", \"twin/qa.rich.json\", \"twin/uat.rich.json\" ] } },"))

    /// Run the witness assertion script and return the names of the
    /// checks that did NOT land (the detail result set's ok=0 rows) — a
    /// failure names its witnesses.
    member private _.FailingChecks (connStr: string) (sql: string) : Task<string list> =
        task {
            use cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            use! reader = cmd.ExecuteReaderAsync()
            let failing = System.Collections.Generic.List<string>()
            let mutable moreSets = true
            while moreSets do
                let mutable moreRows = true
                while moreRows do
                    let! has = reader.ReadAsync()
                    if has then
                        if reader.FieldCount = 2 && reader.GetName 0 = "name" then
                            if System.Convert.ToInt32(reader.GetValue 1) = 0 then
                                failing.Add(reader.GetString 0)
                    else moreRows <- false
                let! next = reader.NextResultAsync()
                moreSets <- next
            return List.ofSeq failing
        }

    /// The winning environment for one statistic in the merge report.
    member private _.Winner (report: JsonElement) (table: string) (column: string) (statistic: string) : string =
        let text (e: JsonElement) : string =
            match e.GetString() with
            | null -> ""
            | s -> s
        let found =
            report.GetProperty("statistics").EnumerateArray()
            |> Seq.tryFind (fun s ->
                text (s.GetProperty "table") = table
                && text (s.GetProperty "statistic") = statistic
                && (match s.TryGetProperty "column" with
                    | true, c -> text c = column
                    | false, _ -> false))
        match found with
        | Some s -> text (s.GetProperty "winner")
        | None -> failwithf "the merge report carries no %s statistic for %s.%s" statistic table column

    member private _.Publish (masterConn: string) (db: string) (dacpac: byte[]) : Task<unit> =
        task {
            let! published = EstateModel.publishTo masterConn db dacpac
            match published with
            | Error es -> failwithf "environment publish refused for %s: %A" db (es |> List.map (fun e -> e.Code, e.Message))
            | Ok () -> return ()
        }

    member private _.Drop (masterConn: string) (db: string) : unit =
        try
            use cnn = new SqlConnection(masterConn)
            cnn.Open()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                System.String.Concat(
                    "IF DB_ID(N'", db, "') IS NOT NULL BEGIN ALTER DATABASE [", db,
                    "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [", db, "]; END")
            cmd.ExecuteNonQuery() |> ignore
        with _ -> ()

    // ------------------------------------------------------------------
    // The rehearsal.
    // ------------------------------------------------------------------

    [<Fact>]
    member this.``three environments' realities crossover into one template that blocks like all of them`` () : Task =
        task {
            // The rehearsal trunk on disk — shared by the environment
            // copies AND the twin's own mint.
            fixture.Rewrite "Tables/dbo.Customer.sql" customerBase
            fixture.Rewrite "Tables/dbo.Region.sql" regionTable

            let! handle = Deploy.acquireContainer ()
            let suffix = System.Guid.NewGuid().ToString("N").Substring(0, 8)
            let devDb = System.String.Concat("TwinRehearsalDev_", suffix)
            let qaDb = System.String.Concat("TwinRehearsalQa_", suffix)
            let uatDb = System.String.Concat("TwinRehearsalUat_", suffix)
            try
                // 1 — fabricate the three environments on one trunk.
                let estate =
                    match EstateFiles.resolve fixture.Root fixture.Config.Estate with
                    | Ok e -> e
                    | Error es -> failwithf "estate resolve refused: %A" (es |> List.map (fun e -> e.Code))
                let dacpac =
                    match EstateModel.buildDacpac estate with
                    | Ok d -> d
                    | Error es -> failwithf "dacpac refused: %A" (es |> List.map (fun e -> e.Code))
                do! this.Publish handle.MasterConnectionString devDb dacpac
                do! this.Publish handle.MasterConnectionString qaDb dacpac
                do! this.Publish handle.MasterConnectionString uatDb dacpac

                let connOf (db: string) =
                    let builder = SqlConnectionStringBuilder handle.MasterConnectionString
                    builder.InitialCatalog <- db
                    builder.ConnectionString
                let statics = EstateDefinition.staticData estate |> List.head |> fun f -> f.Content
                let seedEnv (db: string) (rows: string) (extra: string option) : Task<unit> =
                    task {
                        use cnn = new SqlConnection(connOf db)
                        do! cnn.OpenAsync()
                        do! Deploy.executeBatch cnn statics
                        do! Deploy.executeBatch cnn regionSeed
                        do! Deploy.executeBatch cnn rows
                        match extra with
                        | Some sql -> do! Deploy.executeBatch cnn sql
                        | None -> ()
                    }
                do! seedEnv devDb devRows None
                do! seedEnv qaDb qaRows None
                // UAT: orphan rows FIRST, the NOCHECK reference after — the
                // capture-side edge π measures orphan reality against.
                do! seedEnv uatDb uatRows (Some uatNocheckFk)

                // 2 — capture ×3, one config per environment (law 4).
                System.Environment.SetEnvironmentVariable("TWIN_REHEARSAL_DEV_CONN", connOf devDb)
                System.Environment.SetEnvironmentVariable("TWIN_REHEARSAL_QA_CONN", connOf qaDb)
                System.Environment.SetEnvironmentVariable("TWIN_REHEARSAL_UAT_CONN", connOf uatDb)
                let importOne (name: string) (connVar: string) : Task<unit> =
                    task {
                        let! imported = EvidenceImport.importAll fixture.Root (this.CaptureConfig name connVar)
                        match imported with
                        | Error es -> failwithf "%s capture refused: %A" name (es |> List.map (fun e -> e.Code, e.Message))
                        | Ok report -> Assert.Equal(2, (List.exactlyOne report.Sources).Tables)
                    }
                do! importOne "dev" "TWIN_REHEARSAL_DEV_CONN"
                do! importOne "qa" "TWIN_REHEARSAL_QA_CONN"
                do! importOne "uat" "TWIN_REHEARSAL_UAT_CONN"

                // The UAT capture recorded the orphan reality through its
                // own reference — the edge the trunk leaves unconstrained.
                let uatPack =
                    match Evidence.deserialize (System.IO.File.ReadAllText (System.IO.Path.Combine(fixture.Root, "twin", "uat.rich.json"))) with
                    | Ok p -> p
                    | Error es -> failwithf "uat pack unreadable: %A" (es |> List.map (fun e -> e.Code))
                let uatOrphan = List.exactlyOne uatPack.Orphans
                Assert.Equal("dbo.Customer", uatOrphan.ChildTable)
                Assert.Equal("RegionId", uatOrphan.ChildColumn)
                Assert.Equal("dbo.Region", uatOrphan.ParentTable)
                Assert.Equal(3L, uatOrphan.OrphanCount)

                // The string-plane probe discovered each environment's dirt
                // with no configuration: QA's empty Names, UAT's trailing
                // spaces, Dev's case collision.
                let packOf (name: string) =
                    match Evidence.deserialize (System.IO.File.ReadAllText (System.IO.Path.Combine(fixture.Root, "twin", name))) with
                    | Ok p -> p
                    | Error es -> failwithf "pack unreadable: %A" (es |> List.map (fun e -> e.Code))
                let nameShape (pack: EvidencePack) =
                    let customer = pack.Tables |> List.find (fun t -> t.Table = "dbo.Customer")
                    (customer.Columns |> List.find (fun c -> c.Column = "Name")).Text
                let qaName = nameShape (packOf "qa.rich.json")
                Assert.Equal(Some 2L, qaName |> Option.map (fun ts -> ts.EmptyCount))
                let uatName = nameShape (packOf "uat.rich.json")
                Assert.Equal(Some 2L, uatName |> Option.map (fun ts -> ts.TrailingSpaceCount))
                let devName = nameShape (packOf "dev.rich.json")
                Assert.Equal(Some 1L, devName |> Option.map (fun ts -> ts.CaseCollisions))

                // 3 — the crossover: extremes survive, winners named.
                let config = this.MergeConfig ()
                let! run = EvidenceMerge.run fixture.Root config
                let merge =
                    match run with
                    | Ok r -> r
                    | Error es -> failwithf "merge refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                Assert.Equal(2, merge.MergedTables)
                use reportDoc = JsonDocument.Parse (System.IO.File.ReadAllText merge.ReportPath)
                let report = reportDoc.RootElement
                Assert.Equal("qa", this.Winner report "dbo.Customer" "Email" "nullRate")
                Assert.Equal("uat", this.Winner report "dbo.Customer" "Email" "maxLength")
                Assert.Equal("qa", this.Winner report "dbo.Customer" "Email" "hasDuplicates")
                Assert.Equal("uat", this.Winner report "dbo.Customer" "Score" "envelopeMin")
                Assert.Equal("uat", this.Winner report "dbo.Customer" "Score" "envelopeMax")
                Assert.Equal("uat", this.Winner report "dbo.Customer" "RegionId" "envelopeMax")
                Assert.Equal("qa", this.Winner report "dbo.Customer" "Name" "emptyRate")
                Assert.Equal("uat", this.Winner report "dbo.Customer" "Name" "trailingSpaceRate")
                Assert.Equal("dev", this.Winner report "dbo.Customer" "Name" "caseCollisions")
                // The trunk-enforced StatusId edge lawfully declines its
                // witness — the skip the audit will exempt.
                let reportJson = System.IO.File.ReadAllText merge.ReportPath
                Assert.Contains("dbo.Customer.StatusId", reportJson)
                Assert.Contains("enforcedReference", reportJson)
                // No captured literal reaches the committed report.
                Assert.DoesNotContain("dupe@qa.example", reportJson)
                Assert.DoesNotContain("@x.example", reportJson)

                // The merged pack carries QA's rate at Dev's volume, UAT's
                // length, and UAT's orphans.
                let merged =
                    match Evidence.deserialize (System.IO.File.ReadAllText merge.RichPath) with
                    | Ok p -> p
                    | Error es -> failwithf "merged pack unreadable: %A" (es |> List.map (fun e -> e.Code))
                let customer = merged.Tables |> List.find (fun t -> t.Table = "dbo.Customer")
                let email = customer.Columns |> List.find (fun c -> c.Column = "Email")
                Assert.Equal(25L, email.RowCount)
                Assert.Equal(10L, email.NullCount)
                Assert.Equal(Some 120, email.MaxLength)
                Assert.True email.HasDuplicates
                Assert.Equal(3L, (List.exactlyOne merged.Orphans).OrphanCount)
                // The merged string counts: QA's empty rate and UAT's
                // trailing rate each rescaled to the merged 25 rows with
                // the ceiling; Dev's collision carried by max.
                let mergedName = customer.Columns |> List.find (fun c -> c.Column = "Name")
                let mergedText =
                    match mergedName.Text with
                    | Some ts -> ts
                    | None -> failwith "the merge dropped the string counts"
                Assert.Equal(3L, mergedText.EmptyCount)
                Assert.Equal(4L, mergedText.TrailingSpaceCount)
                Assert.Equal(1L, mergedText.CaseCollisions)

                // 4 — mint from the merged pack (zero mint changes), then
                // plant the witnesses and prove they landed.
                let! minted = Runs.seed fixture.Root config TwinConfig.BaselineScenario
                match minted with
                | Error es -> failwithf "mint refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                | Ok _ -> ()
                let twinConn = fixture.TwinConnectionString
                let! _ = SamplePrSql.exec twinConn (System.IO.File.ReadAllText merge.WitnessSqlPath)
                let! failing = this.FailingChecks twinConn (System.IO.File.ReadAllText merge.WitnessAssertPath)
                if not (List.isEmpty failing) then
                    // Preserve the artifacts and the minted landscape — a
                    // failed rehearsal must be diagnosable after teardown.
                    let debugDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twin-rehearsal-debug")
                    System.IO.Directory.CreateDirectory debugDir |> ignore
                    let keep (path: string) =
                        let file = match System.IO.Path.GetFileName path with | null | "" -> "artifact" | f -> f
                        System.IO.File.Copy(path, System.IO.Path.Combine(debugDir, file), true)
                    keep merge.WitnessSqlPath
                    keep merge.WitnessAssertPath
                    keep merge.RichPath
                    keep merge.ReportPath
                    let! emailNulls = SamplePrSql.scalar twinConn "SELECT COUNT_BIG(*) FROM [dbo].[Customer] WHERE [Email] IS NULL;"
                    let! emailMaxLen = SamplePrSql.scalar twinConn "SELECT ISNULL(MAX(LEN([Email])), -1) FROM [dbo].[Customer];"
                    let! regionIdNonNull = SamplePrSql.scalar twinConn "SELECT COUNT_BIG(*) FROM [dbo].[Customer] WHERE [RegionId] IS NOT NULL;"
                    let! regionIdMax = SamplePrSql.scalar twinConn "SELECT ISNULL(MAX([RegionId]), -1) FROM [dbo].[Customer];"
                    let! regionMaxId = SamplePrSql.scalar twinConn "SELECT ISNULL(MAX([Id]), -1) FROM [dbo].[Region];"
                    // Bind the merged pack against the twin's own read-back
                    // catalog — the exact binding the mint performed — and
                    // name what each suspect axis holds.
                    use probeCnn = new SqlConnection(twinConn)
                    do! probeCnn.OpenAsync()
                    let! twinCatalog = Readback.readSchema probeCnn
                    let profileDump =
                        match twinCatalog with
                        | Error es -> sprintf "readback refused: %A" (es |> List.map (fun e -> e.Code))
                        | Ok cat ->
                            let idx = CatalogIndex.ofCatalog cat
                            let keyOf (column: string) : (SsKey * bool) option =
                                match TableCoordinate.parse "dbo.Customer" with
                                | Error _ -> None
                                | Ok coord ->
                                    match ColumnCoordinate.create coord column |> Result.bind (CatalogIndex.bindColumn idx) with
                                    | Ok (_, a) -> Some (a.SsKey, a.Column.IsNullable)
                                    | Error _ -> None
                            match Evidence.toProfile idx merged with
                            | Error es -> sprintf "toProfile refused: %A" (es |> List.map (fun e -> e.Code))
                            | Ok prof ->
                                let describe (column: string) : string =
                                    match keyOf column with
                                    | None -> sprintf "%s=unbound" column
                                    | Some (k, isNullable) ->
                                        let colPair =
                                            match Profile.tryFindColumn k prof with
                                            | Some c -> sprintf "%d/%d" c.NullCount c.RowCount
                                            | None -> "-"
                                        let cat =
                                            match Profile.tryFindCategorical k prof with
                                            | Some c -> sprintf "cat:%d" (List.length c.Frequencies)
                                            | None -> "cat:-"
                                        let num =
                                            match Profile.tryFindNumeric k prof with
                                            | Some _ -> "num:yes"
                                            | None -> "num:-"
                                        sprintf "%s[nullable=%b %s %s %s]" column isNullable colPair cat num
                                String.concat " " [ describe "Email"; describe "RegionId"; describe "Score" ]
                    failwithf
                        "witnesses did not land: %s || minted landscape: emailNulls=%d emailMaxLen=%d regionIdNonNull=%d regionIdMax=%d regionMaxId=%d || bound profile: %s || artifacts: %s"
                        (String.concat " | " failing)
                        emailNulls emailMaxLen regionIdNonNull regionIdMax regionMaxId profileDump debugDir

                // 5 — the per-environment fidelity audit: the template is
                // at least as blocking as EVERY captured environment.
                let! audited = EvidenceAudit.run fixture.Root config
                let audit =
                    match audited with
                    | Ok a -> a
                    | Error es -> failwithf "audit refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                Assert.Equal(0, audit.TotalFailures)
                Assert.Equal<string list>(
                    [ "dev"; "qa"; "uat" ],
                    audit.Sections |> List.map (fun (source, _, _) -> source))
                for section in audit.Sections do
                    let (_, sectionFailures, _) = section
                    Assert.Equal(0, sectionFailures)

                // 6 — the realities, live on the minted template.
                let! emailNulls = SamplePrSql.scalar twinConn "SELECT COUNT_BIG(*) FROM [dbo].[Customer] WHERE [Email] IS NULL;"
                Assert.True(emailNulls >= 10L, sprintf "the null-rate floor did not hold (nulls=%d)" emailNulls)
                let! emailMax = SamplePrSql.scalar twinConn "SELECT MAX(LEN([Email])) FROM [dbo].[Customer];"
                Assert.Equal(120L, emailMax)
                let! scoreMin = SamplePrSql.scalar twinConn "SELECT MIN([Score]) FROM [dbo].[Customer];"
                let! scoreMax = SamplePrSql.scalar twinConn "SELECT MAX([Score]) FROM [dbo].[Customer];"
                Assert.Equal(-5L, scoreMin)
                Assert.Equal(120L, scoreMax)
                let! orphanRows =
                    SamplePrSql.scalar twinConn
                        "SELECT COUNT_BIG(*) FROM [dbo].[Customer] c LEFT JOIN [dbo].[Region] r ON c.[RegionId] = r.[Id] WHERE c.[RegionId] IS NOT NULL AND r.[Id] IS NULL;"
                Assert.True(orphanRows >= 3L, sprintf "UAT's orphan reality did not land (orphans=%d)" orphanRows)
                let! vocab = SamplePrSql.scalar twinConn "SELECT COUNT_BIG(DISTINCT [Name]) FROM [dbo].[Customer] WHERE [Name] IN (N'DevOnly', N'QaOnly', N'UatOnly');"
                Assert.Equal(3L, vocab)
                let! emptyNames = SamplePrSql.scalar twinConn "SELECT COUNT_BIG(*) FROM [dbo].[Customer] WHERE [Name] IS NOT NULL AND DATALENGTH([Name]) = 0;"
                Assert.True(emptyNames >= 3L, sprintf "QA's empty-string reality did not land (empties=%d)" emptyNames)
                let! trailingNames = SamplePrSql.scalar twinConn "SELECT COUNT_BIG(*) FROM [dbo].[Customer] WHERE [Name] IS NOT NULL AND DATALENGTH([Name]) <> DATALENGTH(RTRIM([Name]));"
                Assert.True(trailingNames >= 1L, sprintf "UAT's trailing-space reality did not land (trailing=%d)" trailingNames)
                let! collisionPairs = SamplePrSql.scalar twinConn "SELECT COUNT_BIG(*) FROM (SELECT UPPER([Name]) AS u FROM [dbo].[Customer] WHERE [Name] IS NOT NULL AND DATALENGTH([Name]) > 0 GROUP BY UPPER([Name]) HAVING COUNT(DISTINCT [Name] COLLATE Latin1_General_BIN2) > 1) g;"
                Assert.True(collisionPairs >= 1L, sprintf "Dev's case-collision reality did not land (groups=%d)" collisionPairs)

                // 7 — block-equivalence: the tightening each environment
                // would refuse is refused by the template, with the same
                // message the real deployment engine prints.
                fixture.Rewrite "Tables/dbo.Customer.sql" customerWithRegionFk
                let! fkAdd = SamplePrPublish.strict fixture.Root fixture.Config
                match fkAdd with
                | Ok () -> failwith "the FK-add must be blocked by UAT's orphan reality"
                | Error es ->
                    let detail = SamplePrSql.detail es
                    Assert.Contains("Msg 547", detail)
                    Assert.Contains("FK_Customer_Region", detail)

                fixture.Rewrite "Tables/dbo.Customer.sql" customerBase
                fixture.Rewrite "Tables/dbo.UX_Customer_Email.sql" uniqueEmailIndex
                let! uniqueAdd = SamplePrPublish.strict fixture.Root fixture.Config
                match uniqueAdd with
                | Ok () -> failwith "the unique-add must be blocked by QA's duplicate reality"
                | Error es ->
                    let detail = SamplePrSql.detail es
                    Assert.Contains("Msg 1505", detail)
                    Assert.Contains("duplicate key", detail)
            finally
                this.Drop handle.MasterConnectionString devDb
                this.Drop handle.MasterConnectionString qaDb
                this.Drop handle.MasterConnectionString uatDb
                (handle.DisposeAsync()).GetAwaiter().GetResult()
        }
