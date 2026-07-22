module Twin.Tests.Integration.SamplePrSeedTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the IDEMPOTENT-SEED / static-data archetype: four operations on
// reference data (Static Entities). The signature proof of every one is
// IDEMPOTENCE — the SILENCE proof: apply the seed lane once, capture the
// table's content-hash + row count, then run the identical guarded MERGE a
// SECOND time and prove it touches ZERO rows and leaves a byte-identical
// content-hash. A seed is correct precisely because re-running it changes
// nothing. Proven against a live Twin (its own container + port 21842) filled
// with real-shaped synthetic data, with the estate's own guarded MERGE idiom
// (WHEN MATCHED AND changed THEN UPDATE / WHEN NOT MATCHED BY TARGET THEN
// INSERT / WHEN NOT MATCHED BY SOURCE THEN DELETE).
//
// Two independent silence signals are used:
//   1. MERGE-level: re-run the exact guarded MERGE via ExecuteNonQuery and
//      assert 0 rows affected + identical SHA2_256(FOR XML RAW) hash. This is
//      the guarded MERGE proving itself a no-op against the already-seeded
//      table.
//   2. Fingerprint-level: a second `Runs.up` returns `NothingToApply` — the
//      Twin's convergence sees both planes current and skips the lane entirely
//      (the silence proof where the estate lane applies).
//
// The four ops and their proven outcomes:
//   1. create-static-seed — a new dbo.Priority lookup (explicit IDs, its own
//      guarded MERGE lane) is created + seeded on the first converge; the
//      second run affects 0 rows with an identical hash. IDs are explicit, not
//      IDENTITY.
//   2. edit-seed — changing a value (Status 'Pending' -> 'On Hold') lands via
//      the guarded WHEN MATCHED UPDATE, which touches EXACTLY ONE row (not the
//      table); the re-run is silent (0 rows, identical hash).
//   3. extract-to-lookup — the CORE (phase 1) of promoting free-text
//      Order.Channel into a dbo.Channel lookup + Order.ChannelId FK: the
//      lookup is seeded (idempotent), the distinct source values map 1:1 (0
//      unmapped, 0 orphan), the FK is trusted. The old text column is retained
//      here — its retirement is a later phase (a multi-PR shape).
//   4. delete-seed-value — the WHEN NOT MATCHED BY SOURCE THEN DELETE branch.
//      Discovered guard: deleting an UNUSED value deletes cleanly + idempotent;
//      deleting a REFERENCED value is BLOCKED by the FK (Msg 547) and rolled
//      back. Remove the reference and the same delete succeeds + is idempotent.
//
// Every fact is self-contained and order-independent: it restores every table
// and the seed lane to baseline, deletes any added file, DROPS the twin
// database, and reconverges before its own work. Evidence is flushed to scratch
// BEFORE the assertions so a surprising outcome is preserved as a finding.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrSeedFixture () =
    inherit TwinEstateFixture ("twin-e2e-seed", 21842)

[<Collection("Twin-Docker")>]
type SamplePrSeedTests (fixture: SamplePrSeedFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    /// The estate's baseline Status seed lane (copied verbatim from
    /// TwinEstateFixture) — the guarded MERGE for Open/Closed/Pending.
    let baselineSeed =
        """MERGE INTO [dbo].[Status] AS t
USING (VALUES (1, N'Open'), (2, N'Closed'), (3, N'Pending')) AS s ([Id], [Name])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Name] <> s.[Name] THEN UPDATE SET [Name] = s.[Name]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name])
WHEN NOT MATCHED BY SOURCE THEN DELETE;
"""

    /// An order-sensitive content-hash of a table's rows: SHA2_256 over the
    /// ORDERED FOR XML RAW projection. A byte-identical hex string proves the
    /// content is byte-identical; any inserted/updated/deleted row shifts it.
    let hashSql (table: string) (cols: string) (orderBy: string) : string =
        sprintf
            "SELECT ISNULL(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CAST((SELECT %s FROM %s ORDER BY %s FOR XML RAW) AS NVARCHAR(MAX))), 2), 'EMPTY');"
            cols table orderBy

    interface IClassFixture<SamplePrSeedFixture>

    // ---- helpers (mirror the make-mandatory / rename / clean-apply exemplars) ----

    member private _.Up () : Task<Result<Runs.UpOutcome>> =
        Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false

    member private _.Scalar (sql: string) : Task<int64> =
        SamplePrSql.scalar fixture.TwinConnectionString sql

    member private _.ScalarStr (sql: string) : Task<string> =
        SamplePrSql.scalarString fixture.TwinConnectionString sql

    member private _.Exec (sql: string) : Task<int> =
        SamplePrSql.exec fixture.TwinConnectionString sql

    /// Run a statement and capture the SQL Server error as text ("" = success).
    /// Used for the FK-guard probe whose refusal (Msg 547) is the objective
    /// proof that a referenced lookup value cannot be deleted.
    member private _.ExecMsg (sql: string) : Task<string> =
        task {
            try
                use cnn = new SqlConnection(fixture.TwinConnectionString)
                do! cnn.OpenAsync()
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- sql
                let! _ = cmd.ExecuteNonQueryAsync()
                return ""
            with
            | :? SqlException as ex -> return sprintf "Msg %d, Line %d: %s" ex.Number ex.LineNumber ex.Message
        }

    /// Converge (relaxed Runs.up) and demand a materialization.
    member private this.Converge (label: string) : Task<Runs.MaterializeReport> =
        task {
            let! outcome = this.Up()
            match outcome with
            | Ok (Runs.Materialized r) -> return r
            | Ok (Runs.NothingToApply _) -> return failwithf "%s: expected Materialized, got NothingToApply" label
            | Error es -> return failwithf "%s: up refused: %A" label (es |> List.map (fun e -> e.Code, e.Message))
        }

    /// Classify the outcome of a converge as a short tag (for the silence proof).
    member private this.UpKind () : Task<string> =
        task {
            let! outcome = this.Up()
            match outcome with
            | Ok (Runs.NothingToApply _) -> return "NothingToApply"
            | Ok (Runs.Materialized _) -> return "Materialized"
            | Error es -> return sprintf "Error[%s]" (es |> List.map (fun e -> e.Code) |> String.concat ",")
        }

    /// Restore every table + the seed lane to baseline, remove any added file,
    /// drop the twin database, reconverge — the per-fact isolation primitive
    /// that makes the class order-independent. The database drop reverts any
    /// direct DDL a prior fact ran against the live twin (a plain re-mint's
    /// schema fingerprint still matches the baseline on disk and cannot).
    member private this.Fresh (label: string) : Task<Runs.MaterializeReport> =
        task {
            let keep = set [ "dbo.Status.sql"; "dbo.Customer.sql"; "dbo.Order.sql"; "dbo.OrderLine.sql" ]
            let tablesDir = System.IO.Path.Combine(fixture.Root, "Tables")
            if System.IO.Directory.Exists tablesDir then
                for file in System.IO.Directory.GetFiles(tablesDir, "*.sql") do
                    let name = System.IO.Path.GetFileName file |> Option.ofObj |> Option.defaultValue ""
                    if not (keep.Contains name) then
                        try System.IO.File.Delete file with _ -> ()
            for f in SamplePrBaseline.files do fixture.Rewrite (fst f) (snd f)
            fixture.Rewrite "Data/StaticSeeds.sql" baselineSeed
            do! SamplePrBaseline.dropTwinDatabase fixture.Config
            return! this.Converge label
        }

    // =====================================================================
    // 1) create-static-seed — a NEW lookup (dbo.Priority) with its own guarded
    //    MERGE lane seeds on the first converge; the second run affects 0 rows
    //    with an identical content-hash (idempotent). IDs are explicit.
    // =====================================================================
    [<Fact>]
    member this.``create-static-seed: a new lookup + guarded MERGE lane seeds on first converge and is idempotent (0 rows + identical hash) on the second`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-create-static-seed.txt"), evidence.ToString()) with _ -> ()

            let priorityCreate =
                """CREATE TABLE [dbo].[Priority] (
    [Id]   INT           NOT NULL,
    [Name] NVARCHAR(50)  NOT NULL,
    CONSTRAINT [PK_Priority] PRIMARY KEY ([Id])
);
"""
            // The new lookup's OWN guarded MERGE — the estate's seed idiom.
            let priorityMerge =
                """MERGE INTO [dbo].[Priority] AS t
USING (VALUES (1, N'High'), (2, N'Medium'), (3, N'Low')) AS s ([Id], [Name])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Name] <> s.[Name] THEN UPDATE SET [Name] = s.[Name]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name])
WHEN NOT MATCHED BY SOURCE THEN DELETE;
"""
            let seedWithPriority = baselineSeed + "\n" + priorityMerge
            let priorityRel = "Tables/dbo.Priority.sql"

            let existsSql = "SELECT CASE WHEN OBJECT_ID(N'[dbo].[Priority]', N'U') IS NULL THEN 0 ELSE 1 END;"
            let rowsSql = "SELECT ISNULL((SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p WHERE p.object_id = OBJECT_ID(N'dbo.Priority') AND p.index_id IN (0,1)), -1);"
            let isIdentitySql = "SELECT ISNULL((SELECT CAST(is_identity AS BIGINT) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Priority') AND name = N'Id'), -1);"
            let namesSql = "SELECT ISNULL((SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), CONCAT([Id], N'=', [Name])), N',') WITHIN GROUP (ORDER BY [Id]) FROM [dbo].[Priority]), N'');"
            let phash = hashSql "[dbo].[Priority]" "[Id],[Name]" "[Id]"

            // BEFORE — baseline estate; dbo.Priority does not exist.
            let! _ = this.Fresh "create-static-seed/fresh"
            let! existsBefore = this.Scalar existsSql
            Assert.Equal(0L, existsBefore)
            record (sprintf "baseline: dbo.Priority exists=%d (absent), estate has Status seeded only" existsBefore)

            // APPLY — add the lookup table + its guarded MERGE lane, converge.
            fixture.Rewrite priorityRel priorityCreate
            fixture.Rewrite "Data/StaticSeeds.sql" seedWithPriority
            let! report = this.Converge "create-static-seed/apply"
            let! existsAfter = this.Scalar existsSql
            let! rowsAfter = this.Scalar rowsSql
            let! isIdentity = this.Scalar isIdentitySql
            let! names = this.ScalarStr namesSql
            let! hashAfterSeed = this.ScalarStr phash
            record (sprintf "first converge (schema published=%b, lanes applied=%d): dbo.Priority exists=%d, rows=%d, Id is_identity=%d (0 = explicit IDs, not IDENTITY)" report.SchemaPublished report.LanesApplied existsAfter rowsAfter isIdentity)
            record (sprintf "  seeded rows: %s" names)
            record (sprintf "  content-hash after first seed = %s" hashAfterSeed)

            // IDEMPOTENCE 1 (MERGE-level) — re-run the exact guarded MERGE.
            let! rerunAffected = this.Exec priorityMerge
            let! rowsRerun = this.Scalar rowsSql
            let! hashRerun = this.ScalarStr phash
            record (sprintf "SECOND run of the identical guarded MERGE: rows affected = %d (0 = idempotent no-op), rows = %d, content-hash = %s (identical=%b)" rerunAffected rowsRerun hashRerun (hashRerun = hashAfterSeed))

            // IDEMPOTENCE 2 (fingerprint-level) — a second converge is silent.
            let! secondConverge = this.UpKind ()
            let! hashAfterConverge = this.ScalarStr phash
            record (sprintf "SECOND converge (Runs.up): %s (the Twin sees both planes current and skips the lane)" secondConverge)
            record (sprintf "  content-hash after second converge = %s (identical=%b)" hashAfterConverge (hashAfterConverge = hashAfterSeed))
            flush ()

            // cleanup so nothing bleeds into the next fact
            try System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, "Tables", "dbo.Priority.sql")) with _ -> ()
            fixture.Rewrite "Data/StaticSeeds.sql" baselineSeed

            Assert.Equal(1L, existsAfter)                 // the lookup table was created
            Assert.Equal(3L, rowsAfter)                   // three seed rows inserted
            Assert.Equal(0L, isIdentity)                  // explicit IDs, NOT IDENTITY
            Assert.Equal<string>("1=High,2=Medium,3=Low", names)
            Assert.Equal(0, rerunAffected)                // the second MERGE touched ZERO rows
            Assert.Equal(3L, rowsRerun)                   // row count unchanged
            Assert.Equal<string>(hashAfterSeed, hashRerun) // byte-identical content-hash
            Assert.Equal<string>("NothingToApply", secondConverge) // the converge was silent
            Assert.Equal<string>(hashAfterSeed, hashAfterConverge)
        }

    // =====================================================================
    // 2) edit-seed — changing a seed value lands via the guarded WHEN MATCHED
    //    UPDATE, touching EXACTLY ONE row; the re-run is silent.
    // =====================================================================
    [<Fact>]
    member this.``edit-seed: the guarded WHEN MATCHED UPDATE changes exactly one row and the re-run is silent (0 rows + identical hash)`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-edit-seed.txt"), evidence.ToString()) with _ -> ()

            // The one edit: Status 'Pending' -> 'On Hold' in the MERGE VALUES.
            let editedSeed = baselineSeed.Replace("N'Pending'", "N'On Hold'")

            let nameByIdSql (id: int) = sprintf "SELECT ISNULL((SELECT [Name] FROM [dbo].[Status] WHERE [Id] = %d), N'')" id
            let rowsSql = "SELECT COUNT(*) FROM [dbo].[Status];"
            let shash = hashSql "[dbo].[Status]" "[Id],[Name]" "[Id]"

            // BEFORE — baseline Status Open/Closed/Pending.
            let! _ = this.Fresh "edit-seed/fresh"
            let! rowsBefore = this.Scalar rowsSql
            let! name3Before = this.ScalarStr (nameByIdSql 3)
            let! hashBefore = this.ScalarStr shash
            Assert.Equal(3L, rowsBefore)
            Assert.Equal<string>("Pending", name3Before)
            record (sprintf "baseline: Status rows=%d, Id=3 Name='%s', content-hash=%s" rowsBefore name3Before hashBefore)

            // APPLY the edit DIRECTLY against the seeded table so the guarded
            // WHEN MATCHED UPDATE branch actually fires (a converge would wipe +
            // re-INSERT; this exercises the UPDATE path and counts the rows).
            let! editAffected = this.Exec editedSeed
            let! name1After = this.ScalarStr (nameByIdSql 1)
            let! name2After = this.ScalarStr (nameByIdSql 2)
            let! name3After = this.ScalarStr (nameByIdSql 3)
            let! rowsAfter = this.Scalar rowsSql
            let! hashAfterEdit = this.ScalarStr shash
            record (sprintf "edited guarded MERGE (Pending -> On Hold): rows affected = %d (1 = only the changed row, NOT the table of %d)" editAffected rowsAfter)
            record (sprintf "  Id=1 Name='%s' (unchanged), Id=2 Name='%s' (unchanged), Id=3 Name='%s' (changed), rows=%d" name1After name2After name3After rowsAfter)
            record (sprintf "  content-hash after edit = %s (differs from baseline=%b)" hashAfterEdit (hashAfterEdit <> hashBefore))

            // IDEMPOTENCE 1 (MERGE-level) — re-run the identical edited MERGE.
            let! rerunAffected = this.Exec editedSeed
            let! hashRerun = this.ScalarStr shash
            record (sprintf "SECOND run of the identical edited MERGE: rows affected = %d (0 = idempotent no-op), content-hash = %s (identical=%b)" rerunAffected hashRerun (hashRerun = hashAfterEdit))

            // IDEMPOTENCE 2 (estate lane) — put the edit on disk, converge
            // (materializes the new value), then a second converge is silent.
            fixture.Rewrite "Data/StaticSeeds.sql" editedSeed
            let! convergeKind = this.UpKind ()
            let! name3AfterConverge = this.ScalarStr (nameByIdSql 3)
            let! hashAfterConverge = this.ScalarStr shash
            let! secondConverge = this.UpKind ()
            let! hashAfterSecond = this.ScalarStr shash
            record (sprintf "estate lane: first converge with the edit on disk = %s, Id=3 Name='%s', hash=%s" convergeKind name3AfterConverge hashAfterConverge)
            record (sprintf "  SECOND converge = %s (silent), hash=%s (identical=%b)" secondConverge hashAfterSecond (hashAfterSecond = hashAfterConverge))
            flush ()

            fixture.Rewrite "Data/StaticSeeds.sql" baselineSeed   // restore so it cannot bleed

            Assert.Equal(1, editAffected)                  // EXACTLY one row updated
            Assert.Equal<string>("Open", name1After)       // guarded: unchanged rows untouched
            Assert.Equal<string>("Closed", name2After)
            Assert.Equal<string>("On Hold", name3After)    // the changed value landed
            Assert.Equal(3L, rowsAfter)                    // no insert/delete
            Assert.NotEqual<string>(hashBefore, hashAfterEdit) // content genuinely changed
            Assert.Equal(0, rerunAffected)                 // re-run touched ZERO rows
            Assert.Equal<string>(hashAfterEdit, hashRerun) // identical content-hash
            Assert.Equal<string>("Materialized", convergeKind)
            Assert.Equal<string>("On Hold", name3AfterConverge) // the lane lands the same value
            Assert.Equal<string>(hashAfterEdit, hashAfterConverge) // lane content == direct-edit content
            Assert.Equal<string>("NothingToApply", secondConverge) // the lane redeploy is silent
            Assert.Equal<string>(hashAfterConverge, hashAfterSecond)
        }

    // =====================================================================
    // 3) extract-to-lookup (CORE / phase 1) — promote free-text Order.Channel
    //    into a dbo.Channel lookup + Order.ChannelId FK. Prove: lookup seeded
    //    (idempotent), distinct source values map 1:1 (0 unmapped, 0 orphan),
    //    FK trusted. The old text column is RETAINED — its drop is a later
    //    phase (a multi-PR shape, like split-table).
    // =====================================================================
    [<Fact>]
    member this.``extract-to-lookup (phase 1): the lookup is seeded idempotently, every distinct source value maps 1:1, and the FK is trusted`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-extract-to-lookup.txt"), evidence.ToString()) with _ -> ()

            // The lookup's guarded MERGE — explicit IDs, the seed idiom.
            let channelMerge =
                """MERGE INTO [dbo].[Channel] AS t
USING (VALUES (1, N'Web'), (2, N'Store')) AS s ([Id], [Code])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Code] <> s.[Code] THEN UPDATE SET [Code] = s.[Code]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Code]) VALUES (s.[Id], s.[Code])
WHEN NOT MATCHED BY SOURCE THEN DELETE;
"""
            let ordersSql = "SELECT COUNT(*) FROM [dbo].[Order];"
            let distinctChannelSql = "SELECT COUNT(*) FROM (SELECT DISTINCT [Channel] FROM [dbo].[Order]) d;"
            let channelRowsSql = "SELECT ISNULL((SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p WHERE p.object_id = OBJECT_ID(N'dbo.Channel') AND p.index_id IN (0,1)), -1);"
            // totality: every distinct source text value has a lookup Code.
            let unmappedSql = "SELECT COUNT(*) FROM (SELECT DISTINCT [Channel] FROM [dbo].[Order]) d WHERE d.[Channel] NOT IN (SELECT [Code] FROM [dbo].[Channel]);"
            let nullFkSql = "SELECT COUNT(*) FROM [dbo].[Order] WHERE [ChannelId] IS NULL;"
            let orphanFkSql = "SELECT COUNT(*) FROM [dbo].[Order] o LEFT JOIN [dbo].[Channel] c ON c.[Id] = o.[ChannelId] WHERE c.[Id] IS NULL;"
            let notTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_Order_Channel'), -1);"
            let oldColSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Order') AND name = N'Channel';"
            let webSql = "SELECT COUNT(*) FROM [dbo].[Order] WHERE [Channel] = N'Web';"
            let storeSql = "SELECT COUNT(*) FROM [dbo].[Order] WHERE [Channel] = N'Store';"
            let chash = hashSql "[dbo].[Channel]" "[Id],[Code]" "[Id]"

            // BEFORE — baseline; establish the free-text column's known values
            // (deterministic {Web, Store}) so the distinct set is meaningful.
            let! _ = this.Fresh "extract-to-lookup/fresh"
            let! totalOrders = this.Scalar ordersSql
            let! _ = this.Exec "UPDATE [dbo].[Order] SET [Channel] = CASE WHEN [Id] % 2 = 1 THEN N'Web' ELSE N'Store' END;"
            let! distinctBefore = this.Scalar distinctChannelSql
            let! webBefore = this.Scalar webSql
            let! storeBefore = this.Scalar storeSql
            Assert.True(totalOrders > 0L, "Order must hold rows for the proof")
            Assert.Equal(2L, distinctBefore)
            record (sprintf "baseline: Order rows=%d, free-text Channel distinct=%d (Web=%d, Store=%d)" totalOrders distinctBefore webBefore storeBefore)

            // STEP 1 — create the lookup table (explicit-ID PK, Code).
            let! _ = this.Exec "CREATE TABLE [dbo].[Channel] ([Id] INT NOT NULL, [Code] NVARCHAR(20) NOT NULL, CONSTRAINT [PK_Channel] PRIMARY KEY ([Id]));"
            // STEP 2 — seed it with the distinct existing values (guarded MERGE).
            let! seedAffected = this.Exec channelMerge
            let! channelRows = this.Scalar channelRowsSql
            let! hashAfterSeed = this.ScalarStr chash
            record (sprintf "step 1-2: created dbo.Channel, seeded via guarded MERGE -> rows affected=%d, lookup rows=%d, content-hash=%s" seedAffected channelRows hashAfterSeed)

            // STEP 3 — totality BEFORE any FK/backfill: every source value maps.
            let! unmappedBefore = this.Scalar unmappedSql
            record (sprintf "step 3 (totality, pre-backfill): distinct source values with NO lookup row = %d (0 = the mapping is total; nothing becomes NULL)" unmappedBefore)

            // STEP 4 — add the FK column + FK (validates over all-NULL -> trusted).
            let! _ = this.Exec "ALTER TABLE [dbo].[Order] ADD [ChannelId] INT NULL;"
            let! _ = this.Exec "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Channel] FOREIGN KEY ([ChannelId]) REFERENCES [dbo].[Channel] ([Id]);"
            // STEP 5 — backfill by joining text -> lookup key.
            let! backfillAffected = this.Exec "UPDATE o SET o.[ChannelId] = c.[Id] FROM [dbo].[Order] o JOIN [dbo].[Channel] c ON c.[Code] = o.[Channel];"
            let! nullFkAfter = this.Scalar nullFkSql
            let! orphanFk = this.Scalar orphanFkSql
            let! notTrusted = this.Scalar notTrustedSql
            let! oldCol = this.Scalar oldColSql
            record (sprintf "step 4-5: added Order.ChannelId + FK_Order_Channel, backfilled %d rows -> ChannelId IS NULL=%d, orphan ChannelId=%d, FK is_not_trusted=%d (0 = trusted)" backfillAffected nullFkAfter orphanFk notTrusted)
            record (sprintf "  old free-text column dbo.Order.Channel still present=%d (RETAINED - the drop is a later phase)" oldCol)

            // IDEMPOTENCE — the seed leg re-run is silent.
            let! seedRerun = this.Exec channelMerge
            let! hashSeedRerun = this.ScalarStr chash
            record (sprintf "SECOND run of the identical lookup MERGE: rows affected=%d (0 = idempotent), content-hash=%s (identical=%b)" seedRerun hashSeedRerun (hashSeedRerun = hashAfterSeed))
            flush ()

            Assert.Equal(2, seedAffected)                  // two lookup rows inserted
            Assert.Equal(2L, channelRows)
            Assert.Equal(totalOrders, webBefore + storeBefore) // the free-text set is exactly Web+Store
            Assert.Equal(0L, unmappedBefore)               // totality: 0 unmapped source values
            Assert.Equal(totalOrders, int64 backfillAffected) // every order row backfilled
            Assert.Equal(0L, nullFkAfter)                  // no order left without a lookup key
            Assert.Equal(0L, orphanFk)                     // every ChannelId resolves 1:1
            Assert.Equal(0L, notTrusted)                   // the FK is trusted
            Assert.Equal(1L, oldCol)                       // old column retained (phase boundary)
            Assert.Equal(0, seedRerun)                     // the seed re-run touched ZERO rows
            Assert.Equal<string>(hashAfterSeed, hashSeedRerun) // identical content-hash
        }

    // =====================================================================
    // 4) delete-seed-value — the WHEN NOT MATCHED BY SOURCE THEN DELETE branch.
    //    Deleting an UNUSED value deletes cleanly + idempotent; deleting a
    //    REFERENCED value is BLOCKED by the FK (Msg 547) and rolled back. Remove
    //    the reference and the same delete succeeds + is idempotent.
    // =====================================================================
    [<Fact>]
    member this.``delete-seed-value: an unused value deletes cleanly and idempotently; a referenced value is blocked by the FK (Msg 547) until the reference is removed`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-delete-seed-value.txt"), evidence.ToString()) with _ -> ()

            // The Status seed with a value removed from the source VALUES -> the
            // WHEN NOT MATCHED BY SOURCE THEN DELETE branch fires for that Id.
            let seedNoPending =
                """MERGE INTO [dbo].[Status] AS t
USING (VALUES (1, N'Open'), (2, N'Closed')) AS s ([Id], [Name])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Name] <> s.[Name] THEN UPDATE SET [Name] = s.[Name]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name])
WHEN NOT MATCHED BY SOURCE THEN DELETE;
"""
            let seedNoOpen =
                """MERGE INTO [dbo].[Status] AS t
USING (VALUES (2, N'Closed'), (3, N'Pending')) AS s ([Id], [Name])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Name] <> s.[Name] THEN UPDATE SET [Name] = s.[Name]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name])
WHEN NOT MATCHED BY SOURCE THEN DELETE;
"""
            let statusHasSql (id: int) = sprintf "SELECT COUNT(*) FROM [dbo].[Status] WHERE [Id] = %d;" id
            let rowsSql = "SELECT COUNT(*) FROM [dbo].[Status];"
            let refCountSql (id: int) = sprintf "SELECT (SELECT COUNT(*) FROM [dbo].[Customer] WHERE [StatusId] = %d) + (SELECT COUNT(*) FROM [dbo].[Order] WHERE [StatusId] = %d);" id id
            let shash = hashSql "[dbo].[Status]" "[Id],[Name]" "[Id]"

            // BEFORE — baseline Status {1,2,3}; force every fact row to reference
            // Open(1), leaving Closed(2) and Pending(3) UNREFERENCED.
            let! _ = this.Fresh "delete-seed-value/fresh"
            let! _ = this.Exec "UPDATE [dbo].[Customer] SET [StatusId] = 1;"
            let! _ = this.Exec "UPDATE [dbo].[Order] SET [StatusId] = 1;"
            let! rowsBefore = this.Scalar rowsSql
            let! refToOpen = this.Scalar (refCountSql 1)
            let! refToPending = this.Scalar (refCountSql 3)
            Assert.Equal(3L, rowsBefore)
            Assert.True(refToOpen > 0L, "Open(1) must be referenced for the block proof")
            Assert.Equal(0L, refToPending)
            record (sprintf "baseline: Status rows=%d; references to Open(1)=%d, to Pending(3)=%d (unreferenced)" rowsBefore refToOpen refToPending)

            // ---- CASE A: delete an UNUSED value (Pending) cleanly ----------
            let! delPending = this.Exec seedNoPending
            let! hasPendingAfter = this.Scalar (statusHasSql 3)
            let! rowsAfterA = this.Scalar rowsSql
            let! hashAfterA = this.ScalarStr shash
            record (sprintf "CASE A (delete UNUSED Pending): seed re-run with Pending removed -> WHEN NOT MATCHED BY SOURCE THEN DELETE, rows affected=%d (1), Id=3 present=%d (0 = deleted), Status rows=%d, hash=%s" delPending hasPendingAfter rowsAfterA hashAfterA)
            // idempotent
            let! delPendingRerun = this.Exec seedNoPending
            let! hashARerun = this.ScalarStr shash
            record (sprintf "  SECOND run (Pending already gone): rows affected=%d (0 = idempotent), hash=%s (identical=%b)" delPendingRerun hashARerun (hashARerun = hashAfterA))

            // ---- CASE B: delete a REFERENCED value (Open) is BLOCKED --------
            // restore Status to {1,2,3}; Open(1) is still referenced by all rows.
            let! _ = this.Exec baselineSeed
            let! rowsRestored = this.Scalar rowsSql
            let! refToOpenB = this.Scalar (refCountSql 1)
            record (sprintf "CASE B setup: Status restored to rows=%d; references to Open(1)=%d (still referenced)" rowsRestored refToOpenB)

            let! blockMsg = this.ExecMsg seedNoOpen
            let! hasOpenAfterBlock = this.Scalar (statusHasSql 1)
            let! rowsAfterBlock = this.Scalar rowsSql
            record (sprintf "CASE B (delete REFERENCED Open): seed re-run with Open removed -> the DELETE is REFUSED by the FK: %s" (if blockMsg = "" then "(no error - UNEXPECTED)" else blockMsg))
            record (sprintf "  after the blocked delete: Open(1) present=%d (1 = survived, rolled back), Status rows=%d (all 3 intact)" hasOpenAfterBlock rowsAfterBlock)

            // remedy — remove the reference, then the same delete SUCCEEDS.
            let! _ = this.Exec "UPDATE [dbo].[Customer] SET [StatusId] = 2 WHERE [StatusId] = 1;"
            let! _ = this.Exec "UPDATE [dbo].[Order] SET [StatusId] = 2 WHERE [StatusId] = 1;"
            let! refToOpenAfterRepoint = this.Scalar (refCountSql 1)
            let! delOpen = this.Exec seedNoOpen
            let! hasOpenFinal = this.Scalar (statusHasSql 1)
            let! rowsFinal = this.Scalar rowsSql
            let! hashFinal = this.ScalarStr shash
            record (sprintf "  remedy: repoint references off Open(1) -> references now=%d; the SAME delete now succeeds, rows affected=%d (1), Open(1) present=%d (0 = deleted), Status rows=%d" refToOpenAfterRepoint delOpen hasOpenFinal rowsFinal)
            // idempotent
            let! delOpenRerun = this.Exec seedNoOpen
            let! hashFinalRerun = this.ScalarStr shash
            record (sprintf "  SECOND run (Open already gone): rows affected=%d (0 = idempotent), hash=%s (identical=%b)" delOpenRerun hashFinalRerun (hashFinalRerun = hashFinal))
            flush ()

            fixture.Rewrite "Data/StaticSeeds.sql" baselineSeed   // restore so it cannot bleed

            // CASE A — the unused value deletes cleanly and idempotently.
            Assert.Equal(1, delPending)                    // exactly one row deleted
            Assert.Equal(0L, hasPendingAfter)              // Pending gone
            Assert.Equal(2L, rowsAfterA)                   // 3 -> 2
            Assert.Equal(0, delPendingRerun)               // re-run touched ZERO rows
            Assert.Equal<string>(hashAfterA, hashARerun)   // identical content-hash
            // CASE B — the referenced value is blocked, then deletes after remedy.
            Assert.Contains("547", blockMsg)               // FK REFERENCE constraint block
            Assert.Equal(1L, hasOpenAfterBlock)            // Open survived the rolled-back delete
            Assert.Equal(3L, rowsAfterBlock)               // nothing deleted
            Assert.Equal(0L, refToOpenAfterRepoint)        // reference removed
            Assert.Equal(1, delOpen)                       // now the delete lands
            Assert.Equal(0L, hasOpenFinal)                 // Open gone
            Assert.Equal(2L, rowsFinal)                    // 3 -> 2
            Assert.Equal(0, delOpenRerun)                  // re-run touched ZERO rows
            Assert.Equal<string>(hashFinal, hashFinalRerun) // identical content-hash
        }
