module Twin.Tests.Integration.SamplePrSchemaChangeTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the "subtle alter / maintenance" archetype: four operations that
// each touch an EXISTING object's definition (or ask for maintenance outside the
// declarative model), proven the make-mandatory way but with the emphasis on
// DISCOVERING what the publish engine actually does — a metadata DROP+ADD, a
// DROP+CREATE rebuild, a phantom "move", or NO delta at all. The BEFORE estate
// is materialized with real-shaped synthetic data (including the pre-existing
// object each op modifies), the op's edit is applied, and the PRODUCTION-FAITHFUL
// publish (SamplePrPublish.strict, BlockOnPossibleDataLoss = true,
// DropObjectsNotInSource = false — the deployment a real environment runs) is the
// objective proof. Unlike the clean-apply waves, "green" here means the ASSERTED
// TRUE OUTCOME holds, whatever it turned out to be:
//   1. change-delete-rule — an FK's ON DELETE action NO ACTION -> CASCADE. SSDT
//      DROP+ADDs the FK (a new object_id proves it), the publish is clean (Ok),
//      no row is touched; the cascade's runtime scope is proven on a rolled-back
//      parent delete. In OutSystems: the reference's Delete Rule (Protect->Delete).
//   2. modify-index — an EXISTING single-column index becomes composite + INCLUDE.
//      SSDT DROP+CREATEs it (a full rebuild), the publish is clean (Ok), rows
//      intact; the new shape is read from sys.index_columns.
//   3. move-schema — dbo.OrderLine "moved" to a sales schema by editing the
//      CREATE TABLE header, with NO refactorlog entry. THE DISCOVERED OUTCOME: the
//      production-faithful publish returns Ok but performs a PHANTOM MOVE — it
//      creates sales.OrderLine EMPTY and (because DropObjectsNotInSource = false)
//      leaves the populated dbo.OrderLine in place; the rows do NOT follow. The
//      honest, correct move is then proven directly: ALTER SCHEMA TRANSFER
//      preserves the object_id and all 25 rows.
//   4. rebuild-index — an ALTER INDEX ... REBUILD. THE DISCOVERED OUTCOME: it is
//      OPERATIONAL maintenance with NO declarative destination. With the index at
//      its converged fixpoint, `Runs.up` returns NothingToApply; performing the
//      physical rebuild out-of-band changes neither the definition nor the data;
//      and a subsequent `Runs.up` STILL returns NothingToApply — the dacpac never
//      sees it.
//
// Every fact is self-contained and order-independent: it rewrites every table it
// touches back to the canonical baseline, removes any stray added file, DROPS the
// twin database, and reconverges before applying its edit. Any file a fact adds
// is retracted at the fact's end so it never bleeds. Evidence is flushed to
// scratch BEFORE the assertions so a surprising outcome is preserved as a finding.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrSchemaChangeFixture () =
    inherit TwinEstateFixture ("twin-e2e-schemachg", 21838)

[<Collection("Twin-Docker")>]
type SamplePrSchemaChangeTests (fixture: SamplePrSchemaChangeFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op schema edits (baseline + one change) -----------------

    // change-delete-rule: FK_OrderLine_Order gains ON DELETE CASCADE. The
    // baseline declares the FK with no ON DELETE clause (NO ACTION); this adds
    // the cascade. SSDT DROP+ADDs the FK to set the action.
    let orderLineCascade =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]) ON DELETE CASCADE
);
"""

    // modify-index / rebuild-index: a nonclustered index rides its own
    // one-statement estate file (a CREATE INDEX in the table's CREATE fails the
    // model build with SQL71006). The Customer table file is left untouched; the
    // index file is retracted at the fact's end.
    let indexRel = "Tables/dbo.Customer.IX_Email.sql"
    let indexSingleCol = "CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email]);\n"
    // modify-index target: composite key (Email, StatusId) + an included column (Name).
    let indexComposite = "CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email], [StatusId]) INCLUDE ([Name]);\n"

    // move-schema: a sales schema (its own one-statement file, named to sort
    // first among Tables/*.sql so it models before its tables), plus OrderLine
    // re-headed under [sales]. The dbo.OrderLine.sql file is rewritten to define
    // [sales].[OrderLine], so the source model no longer contains dbo.OrderLine.
    let salesSchemaRel = "Tables/dbo.__sales-schema.sql"
    let salesSchema = "CREATE SCHEMA [sales];\n"
    let orderLineInSales =
        """CREATE TABLE [sales].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    interface IClassFixture<SamplePrSchemaChangeFixture>

    // ---- helpers (mirror the clean-apply / make-mandatory exemplars) -----

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

    /// Delete an estate file if present (retract an added index / schema file).
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
    // 1) change-delete-rule — an FK's ON DELETE action NO ACTION -> CASCADE.
    //    SSDT DROP+ADDs the FK (metadata only, no row loss) -> strict Ok. The
    //    delete_referential_action flips 0 -> 1, a NEW object_id proves the
    //    DROP+ADD, and the cascade's runtime scope is proven on a rolled-back
    //    parent delete (Protect -> Delete).
    // =====================================================================
    [<Fact>]
    member this.``change-delete-rule: an FK ON DELETE NO ACTION -> CASCADE is a clean metadata DROP+ADD`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-change-delete-rule.txt"), evidence.ToString()) with _ -> ()

            let fkExistsSql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order';"
            let actionSql = "SELECT ISNULL((SELECT CAST(delete_referential_action AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order'), -1);"
            let actionDescSql = "SELECT ISNULL((SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order'), N'');"
            let fkObjectIdSql = "SELECT ISNULL((SELECT CAST(object_id AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order'), -1);"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            // Lines belonging to the busiest parent Order (the cascade's blast radius).
            let busiestChildCountSql =
                "SELECT CAST(COUNT(*) AS BIGINT) FROM [dbo].[OrderLine] WHERE [OrderId] = (SELECT TOP 1 [OrderId] FROM [dbo].[OrderLine] GROUP BY [OrderId] ORDER BY COUNT(*) DESC, [OrderId]);"
            // Attempt to delete that parent inside a rolled-back tran; return the SQL error
            // number (547 = FK conflict = the delete was BLOCKED / Protect), or 0 if it succeeded.
            let parentDeleteProbeSql =
                """SET NOCOUNT ON;
DECLARE @oid INT = (SELECT TOP 1 [OrderId] FROM [dbo].[OrderLine] GROUP BY [OrderId] ORDER BY COUNT(*) DESC, [OrderId]);
BEGIN TRY
  BEGIN TRAN;
  DELETE FROM [dbo].[Order] WHERE [Id] = @oid;
  IF @@TRANCOUNT > 0 ROLLBACK;
  SELECT CAST(0 AS BIGINT);
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  SELECT CAST(ERROR_NUMBER() AS BIGINT);
END CATCH;"""
            // After CASCADE: delete the busiest parent inside a rolled-back tran; return how many
            // child lines the cascade removed (@before - @after). The ROLLBACK restores everything.
            let cascadeRemovedSql =
                """SET NOCOUNT ON;
DECLARE @oid INT = (SELECT TOP 1 [OrderId] FROM [dbo].[OrderLine] GROUP BY [OrderId] ORDER BY COUNT(*) DESC, [OrderId]);
DECLARE @before INT = (SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [OrderId] = @oid);
BEGIN TRAN;
DELETE FROM [dbo].[Order] WHERE [Id] = @oid;
DECLARE @after INT = (SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [OrderId] = @oid);
IF @@TRANCOUNT > 0 ROLLBACK;
SELECT CAST(@before - @after AS BIGINT);"""

            // BEFORE — baseline OrderLine, FK present with NO ACTION.
            let! _ = this.Fresh "change-delete-rule/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! fkBefore = this.Scalar fkExistsSql
            let! actionBefore = this.Scalar actionSql
            let! actionDescBefore = this.ScalarStr actionDescSql
            let! fkObjectIdBefore = this.Scalar fkObjectIdSql
            let! busiestChildCount = this.Scalar busiestChildCountSql
            let! parentDeleteBefore = this.Scalar parentDeleteProbeSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(1L, fkBefore)             // the FK exists in the baseline
            Assert.Equal(0L, actionBefore)         // its delete rule is NO ACTION (Protect)
            record (sprintf "baseline: OrderLine rows=%d, FK_OrderLine_Order exists=%d, delete_referential_action=%d (%s), object_id=%d" rowsBefore fkBefore actionBefore actionDescBefore fkObjectIdBefore)
            record (sprintf "  busiest parent Order has %d child lines; deleting that parent under NO ACTION returned SQL error number=%d (547 = FK conflict = BLOCKED / Protect)" busiestChildCount parentDeleteBefore)

            // APPLY — production-faithful publish of the ON DELETE CASCADE edit.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineCascade
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! fkAfter = this.Scalar fkExistsSql
            let! actionAfter = this.Scalar actionSql
            let! actionDescAfter = this.ScalarStr actionDescSql
            let! fkObjectIdAfter = this.Scalar fkObjectIdSql
            let! rowsAfter = this.Scalar rowCountSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) ALTER FK_OrderLine_Order ON DELETE NO ACTION -> CASCADE [SSDT DROP+ADD]: %s" strictOutcome)
            record (sprintf "  after apply: FK exists=%d, delete_referential_action=%d (%s), object_id=%d (was %d -> changed=%b, proves DROP+ADD), OrderLine rows=%d (intact)" fkAfter actionAfter actionDescAfter fkObjectIdAfter fkObjectIdBefore (fkObjectIdAfter <> fkObjectIdBefore) rowsAfter)

            // Guard the cascade probe behind the apply — it only makes sense once CASCADE landed.
            match strict with
            | Ok () -> ()
            | Error es -> flush (); failwithf "PROOF FAILED: change-delete-rule refused by production publish: %s" (this.Detail es)

            let! cascadeRemoved = this.Scalar cascadeRemovedSql
            let! rowsRestored = this.Scalar rowCountSql
            record (sprintf "  cascade scope (rolled back): deleting the busiest parent Order removed %d child OrderLine rows (Delete); OrderLine rows after rollback=%d (fully restored)" cascadeRemoved rowsRestored)
            flush ()

            Assert.Equal("APPLIED (Ok)", strictOutcome)   // schema-only DROP+ADD is never blocked
            Assert.Equal(1L, fkAfter)                      // FK still present
            Assert.Equal(1L, actionAfter)                  // delete_referential_action flipped 0 -> 1 (CASCADE)
            Assert.Equal<string>("CASCADE", actionDescAfter)
            Assert.NotEqual(fkObjectIdBefore, fkObjectIdAfter)  // new object_id proves DROP+ADD, not in-place alter
            Assert.Equal(rowsBefore, rowsAfter)            // no row touched by the schema change
            Assert.Equal(547L, parentDeleteBefore)         // before: parent delete BLOCKED (Protect)
            Assert.True(busiestChildCount > 0L, "the busiest parent must have child lines")
            Assert.Equal(busiestChildCount, cascadeRemoved) // after: the cascade removed exactly its child lines (Delete)
            Assert.Equal(rowsBefore, rowsRestored)         // the rolled-back demo left the data intact
        }

    // =====================================================================
    // 2) modify-index — an existing single-column index becomes composite +
    //    INCLUDE. SSDT DROP+CREATEs it (rebuild, no row loss) -> strict Ok. The
    //    new shape is read from sys.index_columns; rows intact.
    // =====================================================================
    [<Fact>]
    member this.``modify-index: an existing index gains a key column and an INCLUDE via a clean DROP+CREATE`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-modify-index.txt"), evidence.ToString()) with _ -> ()

            let indexExistsSql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email';"
            let keyColCountSql = "SELECT ISNULL((SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = N'IX_Customer_Email' AND ic.is_included_column = 0), -1);"
            let inclColCountSql = "SELECT ISNULL((SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = N'IX_Customer_Email' AND ic.is_included_column = 1), -1);"
            let secondKeyColSql = "SELECT ISNULL((SELECT c.name FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.name = N'IX_Customer_Email' AND ic.is_included_column = 0 AND ic.key_ordinal = 2), N'');"
            let inclColNameSql = "SELECT ISNULL((SELECT TOP 1 c.name FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.name = N'IX_Customer_Email' AND ic.is_included_column = 1), N'');"
            let indexIdSql = "SELECT ISNULL((SELECT CAST(index_id AS BIGINT) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email'), -1);"
            let typeDescSql = "SELECT ISNULL((SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email'), N'');"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"

            // BEFORE — populated Customer with an EXISTING single-column index.
            let! _ = this.Fresh "modify-index/fresh"
            fixture.Rewrite indexRel indexSingleCol
            let! _ = this.Converge "modify-index/single-col"
            let! rowsBefore = this.Scalar rowCountSql
            let! indexBefore = this.Scalar indexExistsSql
            let! keyColsBefore = this.Scalar keyColCountSql
            let! inclColsBefore = this.Scalar inclColCountSql
            let! indexIdBefore = this.Scalar indexIdSql
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(1L, indexBefore)      // the index exists in the BEFORE state
            Assert.Equal(1L, keyColsBefore)    // one key column (Email)
            Assert.Equal(0L, inclColsBefore)   // no included columns
            record (sprintf "baseline: Customer rows=%d, IX_Customer_Email exists=%d, key columns=%d (Email), included columns=%d, index_id=%d" rowsBefore indexBefore keyColsBefore inclColsBefore indexIdBefore)

            // APPLY — modify the index to composite key (Email, StatusId) + INCLUDE (Name).
            fixture.Rewrite indexRel indexComposite
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! indexAfter = this.Scalar indexExistsSql
            let! keyColsAfter = this.Scalar keyColCountSql
            let! inclColsAfter = this.Scalar inclColCountSql
            let! secondKeyCol = this.ScalarStr secondKeyColSql
            let! inclColName = this.ScalarStr inclColNameSql
            let! indexIdAfter = this.Scalar indexIdSql
            let! typeDesc = this.ScalarStr typeDescSql
            let! rowsAfter = this.Scalar rowCountSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) modify IX_Customer_Email (Email) -> (Email, StatusId) INCLUDE (Name) [SSDT DROP+CREATE]: %s" strictOutcome)
            record (sprintf "  after apply: index exists=%d, key columns=%d, 2nd key column=%s, included columns=%d, included column=%s, type_desc=%s, index_id=%d (was %d), Customer rows=%d (intact)" indexAfter keyColsAfter secondKeyCol inclColsAfter inclColName typeDesc indexIdAfter indexIdBefore rowsAfter)
            flush ()

            this.DeleteTableFile indexRel   // retract the added index file so it cannot bleed

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: modify-index refused by production publish: %s" (this.Detail es)
            Assert.Equal(1L, indexAfter)           // the index still exists
            Assert.Equal(2L, keyColsAfter)         // now two key columns
            Assert.Equal<string>("StatusId", secondKeyCol)  // the added key column
            Assert.Equal(1L, inclColsAfter)        // now one included column
            Assert.Equal<string>("Name", inclColName)       // the added included column
            Assert.Equal<string>("NONCLUSTERED", typeDesc)
            Assert.Equal(rowsBefore, rowsAfter)    // rows intact through the rebuild
        }

    // =====================================================================
    // 3) move-schema — dbo.OrderLine "moved" to a sales schema by editing the
    //    CREATE TABLE header, NO refactorlog entry. DISCOVERED OUTCOME: the
    //    production-faithful publish returns Ok but performs a PHANTOM MOVE —
    //    sales.OrderLine is created EMPTY and dbo.OrderLine is left in place with
    //    all its rows (DropObjectsNotInSource = false). The rows do NOT follow.
    //    The correct move (ALTER SCHEMA TRANSFER, object_id + rows preserved) is
    //    then proven directly as the honest remedy.
    // =====================================================================
    [<Fact>]
    member this.``move-schema: a header-edit move without a refactorlog is a phantom move; ALTER SCHEMA TRANSFER is the real one`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-move-schema.txt"), evidence.ToString()) with _ -> ()

            let dboExistsSql = "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = N'OrderLine' AND s.name = N'dbo';"
            let salesExistsSql = "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = N'OrderLine' AND s.name = N'sales';"
            let salesSchemaExistsSql = "SELECT COUNT(*) FROM sys.schemas WHERE name = N'sales';"
            let dboObjectIdSql = "SELECT ISNULL((SELECT CAST(t.object_id AS BIGINT) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = N'OrderLine' AND s.name = N'dbo'), -1);"
            let salesObjectIdSql = "SELECT ISNULL((SELECT CAST(t.object_id AS BIGINT) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = N'OrderLine' AND s.name = N'sales'), -1);"
            // Metadata-safe row counts by schema (never bind a table that may not exist).
            let dboRowsSql = "SELECT ISNULL((SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p JOIN sys.tables t ON t.object_id = p.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = N'OrderLine' AND s.name = N'dbo' AND p.index_id IN (0,1)), -1);"
            let salesRowsSql = "SELECT ISNULL((SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p JOIN sys.tables t ON t.object_id = p.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = N'OrderLine' AND s.name = N'sales' AND p.index_id IN (0,1)), -1);"

            // BEFORE — baseline; dbo.OrderLine populated, no sales schema.
            let! _ = this.Fresh "move-schema/fresh"
            let! dboBefore = this.Scalar dboExistsSql
            let! salesSchemaBefore = this.Scalar salesSchemaExistsSql
            let! salesBefore = this.Scalar salesExistsSql
            let! dboRowsBefore = this.Scalar dboRowsSql
            let! dboObjIdBefore = this.Scalar dboObjectIdSql
            Assert.Equal(1L, dboBefore)          // dbo.OrderLine exists
            Assert.Equal(0L, salesSchemaBefore)  // sales schema does not exist yet
            Assert.Equal(0L, salesBefore)        // sales.OrderLine does not exist yet
            Assert.True(dboRowsBefore > 0L, "dbo.OrderLine must hold rows so 'did the rows follow' is meaningful")
            record (sprintf "baseline: dbo.OrderLine exists=%d, rows=%d, object_id=%d; sales schema exists=%d, sales.OrderLine exists=%d" dboBefore dboRowsBefore dboObjIdBefore salesSchemaBefore salesBefore)

            // APPLY — the declarative "move": add the sales schema + re-head OrderLine under [sales],
            // WITHOUT a refactorlog entry. dbo.OrderLine.sql is rewritten to define sales.OrderLine,
            // so the source model no longer contains dbo.OrderLine.
            fixture.Rewrite salesSchemaRel salesSchema
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineInSales
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! salesSchemaAfter = this.Scalar salesSchemaExistsSql
            let! salesAfter = this.Scalar salesExistsSql
            let! salesRowsAfter = this.Scalar salesRowsSql
            let! dboAfter = this.Scalar dboExistsSql
            let! dboRowsAfter = this.Scalar dboRowsSql
            let! dboObjIdAfter = this.Scalar dboObjectIdSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true, DropObjectsNotInSource=false) header-edit move dbo.OrderLine -> sales.OrderLine, NO refactorlog: %s" strictOutcome)
            record (sprintf "  DISCOVERED phantom move: sales schema created=%d, sales.OrderLine exists=%d with rows=%d (EMPTY - rows did NOT follow)" salesSchemaAfter salesAfter salesRowsAfter)
            record (sprintf "  the source table was NOT dropped: dbo.OrderLine still exists=%d with rows=%d, object_id=%d (unchanged=%b) - the populated original is stranded" dboAfter dboRowsAfter dboObjIdAfter (dboObjIdAfter = dboObjIdBefore))
            flush ()   // preserve the discovery before any assertion

            match strict with
            | Ok () -> ()
            | Error es -> this.DeleteTableFile salesSchemaRel; failwithf "move-schema: strict publish unexpectedly REFUSED: %s" (this.Detail es)

            // The correct move, proven directly on the disposable copy: drop the empty phantom, then
            // ALTER SCHEMA TRANSFER the populated original — object_id and rows come through unchanged.
            let! _ = this.Exec "DROP TABLE [sales].[OrderLine];"
            let! dboRowsPreTransfer = this.Scalar dboRowsSql
            let! _ = this.Exec "ALTER SCHEMA [sales] TRANSFER [dbo].[OrderLine];"
            let! dboAfterTransfer = this.Scalar dboExistsSql
            let! salesAfterTransfer = this.Scalar salesExistsSql
            let! salesRowsAfterTransfer = this.Scalar salesRowsSql
            let! salesObjIdAfterTransfer = this.Scalar salesObjectIdSql
            record (sprintf "the REAL move (ALTER SCHEMA [sales] TRANSFER [dbo].[OrderLine]): dbo.OrderLine exists=%d (gone), sales.OrderLine exists=%d rows=%d object_id=%d (dbo's original was %d -> preserved=%b)" dboAfterTransfer salesAfterTransfer salesRowsAfterTransfer salesObjIdAfterTransfer dboObjIdBefore (salesObjIdAfterTransfer = dboObjIdBefore))
            flush ()

            this.DeleteTableFile salesSchemaRel   // retract the added schema file so it cannot bleed

            // The DISCOVERED true outcome: strict Ok, but a PHANTOM move.
            Assert.Equal("APPLIED (Ok)", strictOutcome)
            Assert.Equal(1L, salesSchemaAfter)              // the sales schema was created
            Assert.Equal(1L, salesAfter)                    // sales.OrderLine was created
            Assert.Equal(0L, salesRowsAfter)                // but EMPTY - the rows did not follow
            Assert.Equal(1L, dboAfter)                      // dbo.OrderLine was NOT dropped
            Assert.Equal(dboRowsBefore, dboRowsAfter)       // it still holds all its rows
            Assert.Equal(dboObjIdBefore, dboObjIdAfter)     // and the original object_id is untouched
            // The scripted transfer is the real move.
            Assert.Equal(dboRowsBefore, dboRowsPreTransfer) // still all there just before the transfer
            Assert.Equal(0L, dboAfterTransfer)              // dbo.OrderLine is gone after the transfer
            Assert.Equal(1L, salesAfterTransfer)            // sales.OrderLine now holds the table
            Assert.Equal(dboRowsBefore, salesRowsAfterTransfer)      // with all the original rows
            Assert.Equal(dboObjIdBefore, salesObjIdAfterTransfer)    // and the SAME object_id (a move, not a rebuild)
        }

    // =====================================================================
    // 4) rebuild-index — an ALTER INDEX ... REBUILD. DISCOVERED OUTCOME: no
    //    declarative delta. With the index at its converged fixpoint, `Runs.up`
    //    returns NothingToApply; the physical rebuild done out-of-band changes
    //    neither definition nor data; a subsequent `Runs.up` STILL returns
    //    NothingToApply. It is operational maintenance, not a schema migration.
    // =====================================================================
    [<Fact>]
    member this.``rebuild-index: ALTER INDEX REBUILD is operational maintenance with no declarative delta (NothingToApply)`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-rebuild-index.txt"), evidence.ToString()) with _ -> ()

            let indexExistsSql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email';"
            let keyColCountSql = "SELECT ISNULL((SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = N'IX_Customer_Email' AND ic.is_included_column = 0), -1);"
            let typeDescSql = "SELECT ISNULL((SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email'), N'');"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let digestSql = "SELECT ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM([Email])) AS BIGINT), 0) FROM [dbo].[Customer];"
            let fragSql = "SELECT ISNULL((SELECT CAST(avg_fragmentation_in_percent AS NVARCHAR(50)) FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID(N'dbo.Customer'), NULL, NULL, 'LIMITED') ps JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id WHERE i.name = N'IX_Customer_Email'), N'n/a');"

            // BEFORE — populated Customer with an EXISTING index, converged to its fixpoint.
            let! _ = this.Fresh "rebuild-index/fresh"
            fixture.Rewrite indexRel indexSingleCol
            let! _ = this.Converge "rebuild-index/index"   // first up: creates the index (Materialized)

            // A second `up` with nothing changed must report NothingToApply — the estate is at its
            // declarative fixpoint, so a "rebuild me" request has no publishable delta to add.
            let! secondUp = this.Up()
            let secondOutcome =
                match secondUp with
                | Ok (Runs.NothingToApply (t, r, sc)) -> sprintf "NothingToApply (tables=%d, rows=%d, scenario=%s)" t r sc
                | Ok (Runs.Materialized _) -> "Materialized (UNEXPECTED)"
                | Error es -> sprintf "Error: %s" (this.Detail es)

            let! indexBefore = this.Scalar indexExistsSql
            let! keyColsBefore = this.Scalar keyColCountSql
            let! typeDescBefore = this.ScalarStr typeDescSql
            let! rowsBefore = this.Scalar rowCountSql
            let! digestBefore = this.Scalar digestSql
            let! fragBefore = this.ScalarStr fragSql
            Assert.Equal(1L, indexBefore)
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            record (sprintf "baseline: index converged; second `up` (no change) -> %s" secondOutcome)
            record (sprintf "  before rebuild: IX_Customer_Email exists=%d, key columns=%d, type_desc=%s, Customer rows=%d, Email digest=%d, avg_fragmentation_in_percent=%s" indexBefore keyColsBefore typeDescBefore rowsBefore digestBefore fragBefore)

            // The OPERATIONAL maintenance action itself, run out-of-band (NOT a publish).
            let! _ = this.Exec "ALTER INDEX [IX_Customer_Email] ON [dbo].[Customer] REBUILD;"

            let! indexAfter = this.Scalar indexExistsSql
            let! keyColsAfter = this.Scalar keyColCountSql
            let! typeDescAfter = this.ScalarStr typeDescSql
            let! rowsAfter = this.Scalar rowCountSql
            let! digestAfter = this.Scalar digestSql
            let! fragAfter = this.ScalarStr fragSql

            // A third `up` AFTER the physical rebuild must STILL be NothingToApply — the rebuild
            // produced no schema/data-fingerprint delta; the dacpac never saw it.
            let! thirdUp = this.Up()
            let thirdOutcome =
                match thirdUp with
                | Ok (Runs.NothingToApply (t, r, sc)) -> sprintf "NothingToApply (tables=%d, rows=%d, scenario=%s)" t r sc
                | Ok (Runs.Materialized _) -> "Materialized (UNEXPECTED)"
                | Error es -> sprintf "Error: %s" (this.Detail es)
            record "operational: ALTER INDEX [IX_Customer_Email] ON [dbo].[Customer] REBUILD executed out-of-band (a direct DB maintenance command, not a publish)."
            record (sprintf "  after rebuild: index exists=%d, key columns=%d, type_desc=%s (definition unchanged), Customer rows=%d, Email digest=%d (data unchanged), avg_fragmentation_in_percent=%s" indexAfter keyColsAfter typeDescAfter rowsAfter digestAfter fragAfter)
            record (sprintf "  third `up` AFTER the physical rebuild -> %s" thirdOutcome)
            flush ()

            this.DeleteTableFile indexRel   // retract the added index file so it cannot bleed

            // The DISCOVERED true outcome: no declarative delta before OR after the rebuild.
            match secondUp with
            | Ok (Runs.NothingToApply _) -> ()
            | other -> failwithf "PROOF FAILED: expected NothingToApply before the rebuild, got %A" other
            match thirdUp with
            | Ok (Runs.NothingToApply _) -> ()
            | other -> failwithf "PROOF FAILED: expected NothingToApply after the rebuild, got %A" other
            Assert.Equal(indexBefore, indexAfter)          // the index still exists
            Assert.Equal(keyColsBefore, keyColsAfter)      // its definition (key columns) is unchanged
            Assert.Equal<string>(typeDescBefore, typeDescAfter)  // still NONCLUSTERED
            Assert.Equal(rowsBefore, rowsAfter)            // the data is untouched
            Assert.Equal(digestBefore, digestAfter)        // every value is byte-identical (only physical storage moved)
        }
