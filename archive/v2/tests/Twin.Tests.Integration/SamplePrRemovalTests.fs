module Twin.Tests.Integration.SamplePrRemovalTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the "removal / drop" archetype: five operations that each take
// something OUT of the model (an index, a foreign key, a column, a whole table)
// or retire an entity, proven the make-mandatory way but with the emphasis on
// DISCOVERING what the production-faithful publish actually does when an object
// simply stops being mentioned.
//
// THE GOVERNING POSTURE (SamplePrPublish.strict / EstateModel.publishStrict):
// BlockOnPossibleDataLoss = true, GenerateSmartDefaults = false,
// DropObjectsNotInSource = FALSE. That last option is the production default
// `sqlpackage` itself ships — a deploy never drops objects a developer merely
// stopped mentioning. The consequence, discovered per-op below, is NOT uniform:
//   1. drop-index      — an index removed from source. The master DropObjects
//      switch is off, but DacFx's GRANULAR DropIndexesNotInSource defaults TRUE
//      and is independent — so the index IS dropped declaratively, cleanly,
//      losing no rows. The scripted `DROP INDEX` is proven the equivalent
//      lossless form.
//   2. drop-fk         — a foreign key removed from source. DropConstraintsNotIn
//      Source defaults TRUE and is independent — so the FK IS dropped
//      declaratively, cleanly, losing no rows. The real risk is proven directly:
//      an orphan child that WAS blocked (Msg 547) can now be written.
//   3. delete-attribute— a populated column removed from source. A column is not
//      an "object not in source"; the DROP COLUMN proceeds and
//      BlockOnPossibleDataLoss BLOCKS it (the data-loss guard, transactional
//      rollback). The scripted `ALTER TABLE DROP COLUMN` is the irreversible
//      destructive step.
//   4. delete-entity   — a whole populated table removed from source. This IS an
//      "object not in source", so DropObjectsNotInSource = false PROTECTS it: a
//      PHANTOM removal — publish returns Ok, the table and all 25 rows survive
//      (exactly the move-schema shape). The scripted `DROP TABLE` is the
//      destructive, reviewed step that actually removes it (and loses the rows).
//   5. archive-entity  — the SAFE retirement: not a drop at all but a DATA MOVE
//      to an archive table. SSDT does not express data motion; the proof is a
//      conservation check — live + archived == original, byte-identical, batched.
//
// Every fact is self-contained and order-independent: it rewrites every table it
// touches back to the canonical baseline, removes any stray file, DROPS the twin
// database, and reconverges before applying its edit. Evidence is flushed to
// scratch BEFORE the assertions so a surprising outcome is preserved as a finding.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrRemovalFixture () =
    inherit TwinEstateFixture ("twin-e2e-removal", 21839)

[<Collection("Twin-Docker")>]
type SamplePrRemovalTests (fixture: SamplePrRemovalFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op schema edits (baseline minus one thing) --------------

    // drop-index: a nonclustered index rides its own one-statement estate file
    // (a CREATE INDEX inside the table's CREATE fails the model build with
    // SQL71006). It is created, then its file is DELETED to remove it.
    let indexRel = "Tables/dbo.Customer.IX_Email.sql"
    let indexCreate = "CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email]);\n"

    // drop-fk: OrderLine with FK_OrderLine_Order removed from the CREATE.
    let orderLineNoFk =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id])
);
"""

    // delete-attribute: OrderLine with the populated [Sku] column removed.
    let orderLineNoSku =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    interface IClassFixture<SamplePrRemovalFixture>

    // ---- helpers (mirror the schema-change / make-mandatory exemplars) ----

    member private _.Up () : Task<Result<Runs.UpOutcome>> =
        Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false

    member private _.Scalar (sql: string) : Task<int64> =
        SamplePrSql.scalar fixture.TwinConnectionString sql

    member private _.ScalarStr (sql: string) : Task<string> =
        SamplePrSql.scalarString fixture.TwinConnectionString sql

    member private _.Exec (sql: string) : Task<int> =
        SamplePrSql.exec fixture.TwinConnectionString sql

    member private _.Detail (es: ValidationError list) : string =
        SamplePrSql.detail es

    /// Converge (relaxed Runs.up) and demand a materialization.
    member private this.Converge (label: string) : Task<Runs.MaterializeReport> =
        task {
            let! outcome = this.Up()
            match outcome with
            | Ok (Runs.Materialized r) -> return r
            | Ok (Runs.NothingToApply _) -> return failwithf "%s: expected Materialized, got NothingToApply" label
            | Error es -> return failwithf "%s: up refused: %A" label (es |> List.map (fun e -> e.Code, e.Message))
        }

    /// Delete an estate file if present.
    member private _.DeleteTableFile (rel: string) : unit =
        try System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)))
        with _ -> ()

    /// Restore every table to baseline, remove any stray added file, drop the
    /// twin database, reconverge — the per-fact isolation primitive that makes
    /// the class order-independent. The database drop is what reverts a prior
    /// fact's applied strict edit (a plain re-mint's schema fingerprint still
    /// matches the baseline on disk).
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
            do! SamplePrBaseline.dropTwinDatabase fixture.Config
            return! this.Converge label
        }

    // =====================================================================
    // 1) drop-index — an index removed from source. DISCOVERED OUTCOME: the
    //    granular DropIndexesNotInSource (default TRUE, independent of the
    //    DropObjectsNotInSource master switch) drops the index declaratively
    //    and cleanly (strict Ok, no row lost) — dropping an index loses no data.
    //    The scripted `DROP INDEX` is proven the equivalent lossless form.
    // =====================================================================
    [<Fact>]
    member this.``drop-index: removing an index publishes clean and loses no rows; the scripted DROP INDEX is lossless`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-drop-index.txt"), evidence.ToString()) with _ -> ()

            let indexExistsSql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email';"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let digestSql = "SELECT ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM([Id],[Name],[Email],[StatusId],[CreatedOn])) AS BIGINT), 0) FROM [dbo].[Customer];"

            // BEFORE — populated Customer with the index present (created via its own file).
            let! _ = this.Fresh "drop-index/fresh"
            fixture.Rewrite indexRel indexCreate
            let! _ = this.Converge "drop-index/create-index"
            let! rowsBefore = this.Scalar rowCountSql
            let! indexBefore = this.Scalar indexExistsSql
            let! digestBefore = this.Scalar digestSql
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(1L, indexBefore)         // the index exists in the BEFORE state
            record (sprintf "baseline: Customer rows=%d, IX_Customer_Email exists=%d, Email/row digest=%d" rowsBefore indexBefore digestBefore)

            // APPLY — remove the index by DELETING its source file, then production publish.
            this.DeleteTableFile indexRel
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! indexAfter = this.Scalar indexExistsSql
            let! rowsAfter = this.Scalar rowCountSql
            let! digestAfter = this.Scalar digestSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove IX_Customer_Email from source: %s" strictOutcome)
            record (sprintf "  DISCOVERED: index exists after=%d (0 = DROPPED declaratively by the granular DropIndexesNotInSource default; 1 = phantom survival), Customer rows=%d (was %d, intact), digest=%d (unchanged=%b)" indexAfter rowsAfter rowsBefore digestAfter (digestAfter = digestBefore))
            flush ()   // preserve the discovery before any assertion

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "drop-index: strict publish unexpectedly REFUSED (an index drop loses no rows): %s" (this.Detail es)

            // Prove the scripted `DROP INDEX` is the equivalent lossless form. Ensure the index is
            // present (re-create it if the declarative publish already dropped it), then drop it.
            let! existsNow = this.Scalar indexExistsSql
            if existsNow = 0L then
                let! _ = this.Exec indexCreate
                ()
            let! existsPreScript = this.Scalar indexExistsSql
            let! _ = this.Exec "DROP INDEX [IX_Customer_Email] ON [dbo].[Customer];"
            let! existsPostScript = this.Scalar indexExistsSql
            let! rowsPostScript = this.Scalar rowCountSql
            let! digestPostScript = this.Scalar digestSql
            record (sprintf "scripted DROP INDEX [IX_Customer_Email] ON [dbo].[Customer]: index exists before=%d -> after=%d (gone), Customer rows=%d (intact), digest=%d (unchanged=%b)" existsPreScript existsPostScript rowsPostScript digestPostScript (digestPostScript = digestBefore))
            flush ()

            this.DeleteTableFile indexRel   // retract the index file so it cannot bleed

            // The DISCOVERED true outcome: strict Ok, index removed declaratively, no row lost.
            Assert.Equal("APPLIED (Ok)", strictOutcome)   // an index drop is never blocked — it holds no rows
            Assert.Equal(0L, indexAfter)                  // the index was DROPPED by the declarative removal
            Assert.Equal(rowsBefore, rowsAfter)           // every Customer row intact
            Assert.Equal(digestBefore, digestAfter)       // every value byte-identical
            // The scripted form is lossless too.
            Assert.Equal(1L, existsPreScript)             // (re-)present before the scripted drop
            Assert.Equal(0L, existsPostScript)            // gone after DROP INDEX
            Assert.Equal(rowsBefore, rowsPostScript)      // rows intact
            Assert.Equal(digestBefore, digestPostScript)  // values intact
        }

    // =====================================================================
    // 2) drop-fk — a foreign key removed from source. DISCOVERED OUTCOME: the
    //    granular DropConstraintsNotInSource (default TRUE, independent of the
    //    DropObjectsNotInSource master switch) drops the FK declaratively and
    //    cleanly (strict Ok, no row lost). The real risk is proven directly: an
    //    orphan child that WAS blocked (Msg 547) can now be written.
    // =====================================================================
    [<Fact>]
    member this.``drop-fk: removing a foreign key publishes clean and loses no rows, but an orphan can now be written`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-drop-fk.txt"), evidence.ToString()) with _ -> ()

            let fkExistsSql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order';"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            // Attempt to insert an ORPHAN child (an OrderId that points at no Order) inside a
            // rolled-back tran; return the SQL error number (547 = FK conflict = BLOCKED), or 0
            // if the insert was ALLOWED (the referential guarantee is gone).
            let orphanProbeSql =
                """SET NOCOUNT ON;
DECLARE @badOrder INT = (SELECT ISNULL(MAX([Id]), 0) + 100000 FROM [dbo].[Order]);
BEGIN TRY
  BEGIN TRAN;
  INSERT INTO [dbo].[OrderLine] ([OrderId],[Sku],[Quantity],[Note]) VALUES (@badOrder, N'ORPHAN', 1, NULL);
  IF @@TRANCOUNT > 0 ROLLBACK;
  SELECT CAST(0 AS BIGINT);
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  SELECT CAST(ERROR_NUMBER() AS BIGINT);
END CATCH;"""

            // BEFORE — baseline OrderLine, FK present, an orphan child is BLOCKED.
            let! _ = this.Fresh "drop-fk/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! fkBefore = this.Scalar fkExistsSql
            let! orphanBefore = this.Scalar orphanProbeSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(1L, fkBefore)            // the FK exists in the baseline
            Assert.Equal(547L, orphanBefore)      // an orphan child is BLOCKED (Msg 547) while the FK stands
            record (sprintf "baseline: OrderLine rows=%d, FK_OrderLine_Order exists=%d; inserting an orphan child returned SQL error number=%d (547 = FK conflict = BLOCKED)" rowsBefore fkBefore orphanBefore)

            // APPLY — remove the FK from the CREATE, then production publish.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineNoFk
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! fkAfter = this.Scalar fkExistsSql
            let! rowsAfter = this.Scalar rowCountSql
            let! orphanAfter = this.Scalar orphanProbeSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove FK_OrderLine_Order from source: %s" strictOutcome)
            record (sprintf "  DISCOVERED: FK exists after=%d (0 = DROPPED declaratively by the granular DropConstraintsNotInSource default; 1 = phantom survival), OrderLine rows=%d (was %d, intact)" fkAfter rowsAfter rowsBefore)
            record (sprintf "  the real consequence: inserting an orphan child now returns SQL error number=%d (0 = ALLOWED — nothing prevents an OrderLine pointing at a missing Order; 547 = still blocked)" orphanAfter)
            flush ()   // preserve the discovery before any assertion

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "drop-fk: strict publish unexpectedly REFUSED (a constraint drop loses no rows): %s" (this.Detail es)

            // Prove the scripted `ALTER TABLE ... DROP CONSTRAINT` is the equivalent lossless form.
            // Ensure the FK is present (re-add it if the declarative publish already dropped it), then drop it.
            let! fkNow = this.Scalar fkExistsSql
            if fkNow = 0L then
                let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] ADD CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]);"
                ()
            let! fkPreScript = this.Scalar fkExistsSql
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order];"
            let! fkPostScript = this.Scalar fkExistsSql
            let! rowsPostScript = this.Scalar rowCountSql
            record (sprintf "scripted ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order]: FK exists before=%d -> after=%d (gone via sys.foreign_keys), OrderLine rows=%d (intact)" fkPreScript fkPostScript rowsPostScript)
            flush ()

            // The DISCOVERED true outcome: strict Ok, FK removed declaratively, rows intact, orphan now writable.
            Assert.Equal("APPLIED (Ok)", strictOutcome)   // a constraint drop is never blocked — it touches no rows
            Assert.Equal(0L, fkAfter)                     // the FK was DROPPED by the declarative removal
            Assert.Equal(rowsBefore, rowsAfter)           // every OrderLine row intact
            Assert.Equal(0L, orphanAfter)                 // the referential guarantee is gone — an orphan is now accepted
            // The scripted form is lossless too.
            Assert.Equal(1L, fkPreScript)                 // (re-)present before the scripted drop
            Assert.Equal(0L, fkPostScript)                // gone after DROP CONSTRAINT
            Assert.Equal(rowsBefore, rowsPostScript)      // rows intact
        }

    // =====================================================================
    // 3) delete-attribute — a populated column removed from source. DISCOVERED
    //    OUTCOME: a column is not an "object not in source"; the DROP COLUMN
    //    proceeds and BlockOnPossibleDataLoss BLOCKS it (data-loss guard,
    //    transactional rollback — schema and data unchanged). The scripted
    //    `ALTER TABLE ... DROP COLUMN` is the irreversible destructive step.
    // =====================================================================
    [<Fact>]
    member this.``delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the scripted DROP COLUMN is the irreversible step`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-delete-attribute.txt"), evidence.ToString()) with _ -> ()

            let colExistsSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.OrderLine') AND name = N'Sku';"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let skuPopulatedSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [Sku] IS NOT NULL;"

            // BEFORE — baseline OrderLine, [Sku] present and populated on every row.
            let! _ = this.Fresh "delete-attribute/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! colBefore = this.Scalar colExistsSql
            let! skuPopulatedBefore = this.Scalar skuPopulatedSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(1L, colBefore)                    // the column exists in the baseline
            Assert.Equal(rowsBefore, skuPopulatedBefore)   // every row carries a Sku value (nothing NULL)
            record (sprintf "baseline: OrderLine rows=%d, [Sku] column exists=%d, rows with Sku NOT NULL=%d (fully populated)" rowsBefore colBefore skuPopulatedBefore)

            // APPLY — remove the [Sku] column from the CREATE, then production publish.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineNoSku
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictRefused = match strict with | Ok () -> false | Error _ -> true
            let strictDetail = match strict with | Ok () -> "" | Error es -> this.Detail es
            let strictOutcome = if strictRefused then sprintf "REFUSED: %s" strictDetail else "APPLIED (Ok)"
            let! colAfter = this.Scalar colExistsSql
            let! rowsAfter = this.Scalar rowCountSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove [Sku] from source: %s" strictOutcome)
            record (sprintf "  DISCOVERED: [Sku] column exists after=%d (1 = survived the block; 0 = dropped), OrderLine rows=%d (was %d)" colAfter rowsAfter rowsBefore)
            flush ()   // preserve the block message before any assertion

            // Prove the scripted `ALTER TABLE ... DROP COLUMN` is the destructive step SSDT refused —
            // it drops the column (irreversibly discarding its values) while the rows themselves remain.
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] DROP COLUMN [Sku];"
            let! colPostScript = this.Scalar colExistsSql
            let! rowsPostScript = this.Scalar rowCountSql
            record (sprintf "scripted ALTER TABLE [dbo].[OrderLine] DROP COLUMN [Sku]: [Sku] exists after=%d (gone), OrderLine rows=%d (rows remain; the Sku values are irrecoverably lost)" colPostScript rowsPostScript)
            flush ()

            // The DISCOVERED true outcome: the declarative drop of a populated column is BLOCKED.
            Assert.True(strictRefused, "removing a populated column must be blocked by the data-loss guard")
            Assert.Contains("data loss", strictDetail.ToLowerInvariant())   // the DacFx data-loss guard fired
            Assert.Equal(1L, colAfter)                     // [Sku] survived the blocked publish
            Assert.Equal(rowsBefore, rowsAfter)            // rows unchanged by the (rolled-back) block
            // The scripted drop is the irreversible destructive step.
            Assert.Equal(0L, colPostScript)                // [Sku] is gone after the explicit DROP COLUMN
            Assert.Equal(rowsBefore, rowsPostScript)       // the rows remain; only the column's values are lost
        }

    // =====================================================================
    // 4) delete-entity — a whole populated table removed from source.
    //    DISCOVERED OUTCOME: the table IS an "object not in source", so
    //    DropObjectsNotInSource = false PROTECTS it — a PHANTOM removal: the
    //    publish returns Ok, the table and all its rows survive untouched
    //    (the move-schema shape). The scripted `DROP TABLE` is the destructive,
    //    reviewed step that actually removes it (and loses the rows).
    // =====================================================================
    [<Fact>]
    member this.``delete-entity: removing a whole table from source is a phantom removal under DropObjectsNotInSource=false; the scripted DROP TABLE is the real, destructive one`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-delete-entity.txt"), evidence.ToString()) with _ -> ()

            let tableExistsSql = "SELECT CASE WHEN OBJECT_ID(N'[dbo].[OrderLine]', N'U') IS NULL THEN 0 ELSE 1 END;"
            let objectIdSql = "SELECT ISNULL(CAST(OBJECT_ID(N'[dbo].[OrderLine]', N'U') AS BIGINT), -1);"
            // Metadata-safe row count (never binds a table that may not exist).
            let rowsSql = "SELECT ISNULL((SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p WHERE p.object_id = OBJECT_ID(N'dbo.OrderLine') AND p.index_id IN (0,1)), -1);"

            // BEFORE — baseline; dbo.OrderLine present and populated. OrderLine is a leaf
            // (nothing references it), so no inbound FK complicates the removal.
            let! _ = this.Fresh "delete-entity/fresh"
            let! tableBefore = this.Scalar tableExistsSql
            let! rowsBefore = this.Scalar rowsSql
            let! objIdBefore = this.Scalar objectIdSql
            Assert.Equal(1L, tableBefore)         // dbo.OrderLine exists
            Assert.True(rowsBefore > 0L, "dbo.OrderLine must hold rows so 'were they dropped' is meaningful")
            record (sprintf "baseline: dbo.OrderLine exists=%d, rows=%d, object_id=%d" tableBefore rowsBefore objIdBefore)

            // APPLY — remove the whole table by DELETING its source file, then production publish.
            this.DeleteTableFile "Tables/dbo.OrderLine.sql"
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! tableAfter = this.Scalar tableExistsSql
            let! rowsAfter = this.Scalar rowsSql
            let! objIdAfter = this.Scalar objectIdSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) remove whole dbo.OrderLine from source: %s" strictOutcome)
            record (sprintf "  DISCOVERED phantom removal: dbo.OrderLine exists after=%d (1 = SURVIVED), rows=%d (was %d), object_id=%d (unchanged=%b) - the table was NOT dropped" tableAfter rowsAfter rowsBefore objIdAfter (objIdAfter = objIdBefore))
            flush ()   // preserve the discovery before any assertion

            match strict with
            | Ok () -> ()
            | Error es -> fixture.Rewrite "Tables/dbo.OrderLine.sql" SamplePrBaseline.orderLine; failwithf "delete-entity: strict publish unexpectedly REFUSED: %s" (this.Detail es)

            // Prove the scripted DROP TABLE is the destructive step that actually removes it.
            // OrderLine is a leaf (no inbound FK), so it drops directly; the 25 rows are lost.
            let! rowsPreScript = this.Scalar rowsSql
            let! _ = this.Exec "DROP TABLE [dbo].[OrderLine];"
            let! tablePostScript = this.Scalar tableExistsSql
            let! objIdPostScript = this.Scalar objectIdSql
            record (sprintf "scripted DROP TABLE [dbo].[OrderLine] (rows just before=%d): table exists after=%d (gone), object_id=%d (-1 = no such object) - the rows are gone with it (irreversible without a backup)" rowsPreScript tablePostScript objIdPostScript)
            flush ()

            fixture.Rewrite "Tables/dbo.OrderLine.sql" SamplePrBaseline.orderLine   // restore the baseline file so it cannot bleed

            // The DISCOVERED true outcome: strict Ok, but a PHANTOM removal — the table survives.
            Assert.Equal("APPLIED (Ok)", strictOutcome)
            Assert.Equal(1L, tableAfter)                   // dbo.OrderLine was NOT dropped
            Assert.Equal(rowsBefore, rowsAfter)            // it still holds all its rows
            Assert.Equal(objIdBefore, objIdAfter)          // and the original object_id is untouched
            // The scripted drop is the real, destructive removal.
            Assert.Equal(rowsBefore, rowsPreScript)        // still all there just before the scripted drop
            Assert.Equal(0L, tablePostScript)              // the table is gone after DROP TABLE
            Assert.Equal(-1L, objIdPostScript)             // no such object remains
        }

    // =====================================================================
    // 5) archive-entity — the SAFE retirement of an entity: NOT a drop but a
    //    DATA MOVE to an archive table (keep the data, stop using it live).
    //    SSDT expresses shapes, not data motion, so the archive table is
    //    created and the rows are moved by a scripted, BATCHED
    //    `DELETE ... OUTPUT DELETED.* INTO archive.X`. The proof is a
    //    CONSERVATION check: live + archived == original, each moved row
    //    byte-identical, none dropped or duplicated, and the move batched.
    // =====================================================================
    [<Fact>]
    member this.``archive-entity: retiring rows to an archive table conserves every row (live + archived == original), byte-identical and batched`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-archive-entity.txt"), evidence.ToString()) with _ -> ()

            let liveTotalSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let archivedTotalSql = "SELECT COUNT(*) FROM [archive].[OrderLine];"
            // The cutoff Id that splits off the "older" half (the rows to retire).
            let cutoffSql = "SELECT CAST(MAX([Id]) AS BIGINT) FROM (SELECT TOP 50 PERCENT [Id] FROM [dbo].[OrderLine] ORDER BY [Id]) x;"

            // BEFORE — baseline OrderLine populated; capture the total and the digest of the
            // rows that will move (so we can prove the archive copy is byte-identical).
            let! _ = this.Fresh "archive-entity/fresh"
            let! originalTotal = this.Scalar liveTotalSql
            let! cutoff = this.Scalar cutoffSql
            Assert.True(originalTotal > 0L, "OrderLine must hold rows for the proof")
            let toMoveCountSql = sprintf "SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [Id] <= %d;" cutoff
            let movedDigestBeforeSql = sprintf "SELECT ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM([Id],[OrderId],[Sku],[Quantity],[Note])) AS BIGINT), 0) FROM [dbo].[OrderLine] WHERE [Id] <= %d;" cutoff
            let archiveDigestSql = "SELECT ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM([Id],[OrderId],[Sku],[Quantity],[Note])) AS BIGINT), 0) FROM [archive].[OrderLine];"
            let overlapSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] d JOIN [archive].[OrderLine] a ON a.[Id] = d.[Id];"
            let! toMove = this.Scalar toMoveCountSql
            let! movedDigestBefore = this.Scalar movedDigestBeforeSql
            Assert.True(toMove > 0L && toMove < originalTotal, "the split must retire some rows and keep some live")
            record (sprintf "baseline: dbo.OrderLine total rows=%d; retire the older half (Id <= %d) = %d rows; keep %d live; moved-rows digest=%d" originalTotal cutoff toMove (originalTotal - toMove) movedDigestBefore)

            // Create the archive destination (a passive store — no IDENTITY, so the original Ids
            // are preserved). In production this ships as an additive declarative table + a scripted
            // post-deployment move; here both are scripted on the disposable copy.
            let! _ = this.Exec "IF SCHEMA_ID(N'archive') IS NULL EXEC(N'CREATE SCHEMA [archive];');"
            let! _ = this.Exec "IF OBJECT_ID(N'[archive].[OrderLine]', N'U') IS NOT NULL DROP TABLE [archive].[OrderLine];"
            let! _ = this.Exec "CREATE TABLE [archive].[OrderLine] ([Id] INT NOT NULL, [OrderId] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Quantity] INT NOT NULL, [Note] NVARCHAR(200) NULL, CONSTRAINT [PK_archive_OrderLine] PRIMARY KEY ([Id]));"

            // The BATCHED move: DELETE ... OUTPUT DELETED.* INTO the archive, in bounded batches so
            // the transaction log stays bounded. Returns the number of batches that committed.
            let batchedMoveSql =
                sprintf """SET NOCOUNT ON;
DECLARE @batches INT = 0, @n INT = 1;
WHILE @n > 0
BEGIN
  DELETE TOP (7) t
    OUTPUT DELETED.[Id], DELETED.[OrderId], DELETED.[Sku], DELETED.[Quantity], DELETED.[Note]
      INTO [archive].[OrderLine] ([Id],[OrderId],[Sku],[Quantity],[Note])
  FROM [dbo].[OrderLine] t
  WHERE t.[Id] <= %d;
  SET @n = @@ROWCOUNT;
  IF @n > 0 SET @batches += 1;
END
SELECT CAST(@batches AS BIGINT);""" cutoff
            let! batches = this.Scalar batchedMoveSql

            // AFTER — the conservation facts.
            let! liveAfter = this.Scalar liveTotalSql
            let! archivedAfter = this.Scalar archivedTotalSql
            let! archiveDigest = this.Scalar archiveDigestSql
            let! overlap = this.Scalar overlapSql
            record (sprintf "batched move (DELETE TOP (7) ... OUTPUT DELETED.* INTO [archive].[OrderLine] WHERE Id <= %d): committed in %d batches" cutoff batches)
            record (sprintf "  after move: live rows=%d, archived rows=%d, live + archived=%d (original total=%d -> conserved=%b)" liveAfter archivedAfter (liveAfter + archivedAfter) originalTotal (liveAfter + archivedAfter = originalTotal))
            record (sprintf "  archived-rows digest=%d (moved-rows digest before=%d -> byte-identical=%b); overlap (Id in both tables)=%d (0 = none duplicated)" archiveDigest movedDigestBefore (archiveDigest = movedDigestBefore) overlap)
            flush ()

            // Tidy the disposable copy (the next fact's Fresh drops the whole DB regardless).
            let! _ = this.Exec "IF OBJECT_ID(N'[archive].[OrderLine]', N'U') IS NOT NULL DROP TABLE [archive].[OrderLine];"
            let! _ = this.Exec "IF SCHEMA_ID(N'archive') IS NOT NULL EXEC(N'DROP SCHEMA [archive];');"

            // The proven outcome: every row conserved, byte-identical, batched, none duplicated.
            Assert.Equal(originalTotal, liveAfter + archivedAfter)   // no row lost, none doubled
            Assert.Equal(toMove, archivedAfter)                     // exactly the intended rows moved
            Assert.Equal(originalTotal - toMove, liveAfter)         // the rest stayed live
            Assert.Equal(0L, overlap)                               // no row is in both tables
            Assert.Equal(movedDigestBefore, archiveDigest)          // the archived rows are byte-identical to the originals
            Assert.True(batches >= 2L, "the move must commit in more than one batch (log-bounded)")
        }
