module Twin.Tests.Integration.SamplePrReferenceIntegrityTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the REFERENCE-INTEGRITY archetype: five operations about keys
// and relationships, each proven the make-mandatory / add-check way against a
// live Twin filled with real-shaped synthetic data, under the PRODUCTION-
// FAITHFUL publish (SamplePrPublish.strict — BlockOnPossibleDataLoss = true,
// GenerateSmartDefaults = false, DropObjectsNotInSource = false).
//
// THE GOVERNING FACT ABOUT THE MINT (discovered from Runs/Mint): the Twin's
// synthetic generator reads the FK graph from the LIVE schema and draws every
// child key from a real parent — so data is orphan-free ONLY when it is minted
// WITH the constraint present. A re-mint after the constraint is gone would
// invent child keys with no parent. Therefore each "before WITHOUT the key"
// state is established by minting under the full baseline (relationship present
// -> consistent data) and then dropping the key with a DIRECT `ALTER TABLE`
// (schema-only, no re-mint) — the data stays exactly as it was minted. The
// key is then (re-)added by the strict publish, which does NOT mint.
//
// The five ops and their TRUE outcomes (discovered, not assumed):
//   1. create-fk-clean   — add an FK over clean child data. The strict publish
//      ADDs it and it lands TRUSTED (is_not_trusted = 0); the orphan probe is 0.
//   2. define-pk         — add a PRIMARY KEY to a heap whose key is unique and
//      non-NULL. The strict publish builds the clustered index clean; rows intact.
//   3. junction          — a NEW M:N bridge (dbo.OrderTag) with two FKs and a
//      composite PK, plus a small dbo.Tag parent. Created empty by the strict
//      publish: both FKs TRUSTED, the composite PK present (2 key columns), both
//      orphan legs 0. Seeded valid pairs prove the shape; a duplicate pair is
//      rejected by the composite PK (Msg 2627).
//   4. create-fk-orphan  — add an FK while an ORPHAN exists. SSDT adds it
//      WITH NOCHECK, then WITH CHECK CHECK fails Msg 547 -> the publish is
//      REFUSED and the FK lingers UNTRUSTED. Remedy: reconcile the orphan, then
//      WITH CHECK CHECK re-validates -> is_not_trusted flips 1 -> 0.
//   5. toggle-trust      — an existing UNTRUSTED FK (added WITH NOCHECK over
//      clean data) is made TRUSTED by WITH CHECK CHECK: is_not_trusted 1 -> 0.
//      While a row violates, WITH CHECK CHECK fails Msg 547 (trust needs clean
//      data first) — proven, then reconciled back to trusted.
//
// Every fact is self-contained and order-independent: it restores every table
// to baseline, removes any stray added file, DROPS the twin database, and
// reconverges before applying its edit. Evidence is flushed to scratch BEFORE
// the assertions so a surprising outcome is preserved as a finding.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrReferenceIntegrityFixture () =
    inherit TwinEstateFixture ("twin-e2e-refint", 21840)

[<Collection("Twin-Docker")>]
type SamplePrReferenceIntegrityTests (fixture: SamplePrReferenceIntegrityFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op schema shapes (baseline +/- one thing) ----------------

    // OrderLine with FK_OrderLine_Order removed from the CREATE (the coherent
    // "before" for create-fk-clean / create-fk-orphan — a heap-with-PK, no FK).
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

    // OrderLine with PK_OrderLine removed from the CREATE (the coherent "before"
    // for define-pk — a heap that still carries its outbound FK to Order).
    let orderLineNoPk =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    // junction: a small parent Tag, and the OrderTag bridge whose composite PK
    // spans its two FK columns (OrderId -> Order, TagId -> Tag). Each is a single
    // CREATE TABLE with inline constraints (one statement per file).
    let tagRel = "Tables/dbo.Tag.sql"
    let tagCreate =
        """CREATE TABLE [dbo].[Tag] (
    [Id]   INT           IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(50)  NOT NULL,
    CONSTRAINT [PK_Tag] PRIMARY KEY ([Id])
);
"""
    let orderTagRel = "Tables/dbo.OrderTag.sql"
    let orderTagCreate =
        """CREATE TABLE [dbo].[OrderTag] (
    [OrderId] INT NOT NULL,
    [TagId]   INT NOT NULL,
    CONSTRAINT [PK_OrderTag] PRIMARY KEY ([OrderId], [TagId]),
    CONSTRAINT [FK_OrderTag_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]),
    CONSTRAINT [FK_OrderTag_Tag] FOREIGN KEY ([TagId]) REFERENCES [dbo].[Tag] ([Id])
);
"""

    interface IClassFixture<SamplePrReferenceIntegrityFixture>

    // ---- helpers (mirror the removal / make-mandatory exemplars) ----------

    member private _.Up () : Task<Result<Runs.UpOutcome>> =
        Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false

    member private _.Scalar (sql: string) : Task<int64> =
        SamplePrSql.scalar fixture.TwinConnectionString sql

    member private _.Exec (sql: string) : Task<int> =
        SamplePrSql.exec fixture.TwinConnectionString sql

    member private _.Detail (es: ValidationError list) : string =
        SamplePrSql.detail es

    /// Run a non-query and capture the SQL Server error as text ("" = success).
    /// Used for the operational `WITH CHECK CHECK` / duplicate-key steps whose
    /// refusal (Msg 547 / Msg 2627) is the objective proof, not a thrown test.
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

    /// Delete an estate file if present.
    member private _.DeleteTableFile (rel: string) : unit =
        try System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)))
        with _ -> ()

    /// Restore every table to baseline, remove any stray added file, drop the
    /// twin database, reconverge — the per-fact isolation primitive. The
    /// database drop is what reverts a prior fact's applied strict edit (a plain
    /// re-mint's schema fingerprint still matches the baseline on disk).
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

    // Shared probe fragments.
    // Insert an ORPHAN child (an OrderId that points at no Order) inside a
    // rolled-back tran; returns the SQL error number (547 = FK conflict =
    // BLOCKED / enforced) or 0 if the insert was ALLOWED.
    member private this.OrphanInsertProbe () : Task<int64> =
        this.Scalar
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

    // =====================================================================
    // 1) create-fk-clean — add an FK whose child data has no orphans.
    //    TRUE OUTCOME: strict Ok, the FK lands TRUSTED (is_not_trusted = 0),
    //    the orphan probe is 0, and a bad write is now rejected (Msg 547).
    // =====================================================================
    [<Fact>]
    member this.``create-fk-clean: an FK over clean child data applies in place and lands trusted`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-create-fk-clean.txt"), evidence.ToString()) with _ -> ()

            let fkExistsSql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order';"
            let notTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order'), -1);"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let orphanProbeSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] c LEFT JOIN [dbo].[Order] p ON c.[OrderId] = p.[Id] WHERE p.[Id] IS NULL;"

            // BEFORE — mint under the full baseline (FK present), so OrderLine
            // is minted with every OrderId drawn from a real Order.
            let! _ = this.Fresh "create-fk-clean/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! fkMinted = this.Scalar fkExistsSql
            let! orphansMinted = this.Scalar orphanProbeSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(1L, fkMinted)          // baseline mints WITH the FK
            Assert.Equal(0L, orphansMinted)     // minted under the relationship -> no orphans
            record (sprintf "baseline (minted WITH the relationship): OrderLine rows=%d, FK_OrderLine_Order exists=%d, orphan probe (OrderLine->Order)=%d" rowsBefore fkMinted orphansMinted)

            // Establish the "before WITHOUT the FK" state: drop the FK with a
            // direct ALTER (schema-only, no re-mint -> data stays consistent),
            // and rewrite the estate file to match (the coherent before-state).
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order];"
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineNoFk
            let! fkAbsent = this.Scalar fkExistsSql
            let! orphansNoFk = this.Scalar orphanProbeSql
            let! orphanWriteBefore = this.OrphanInsertProbe ()
            Assert.Equal(0L, fkAbsent)          // the relationship is not declared yet
            Assert.Equal(0L, orphansNoFk)       // the data is still consistent
            record (sprintf "before (no FK declared): FK exists=%d, orphan probe=%d, inserting a child pointing at a missing Order returned SQL error=%d (0 = ALLOWED, nothing guards it yet)" fkAbsent orphansNoFk orphanWriteBefore)

            // APPLY — add the FK back and run the production-faithful publish.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" SamplePrBaseline.orderLine
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! fkAfter = this.Scalar fkExistsSql
            let! notTrustedAfter = this.Scalar notTrustedSql
            let! orphansAfter = this.Scalar orphanProbeSql
            let! rowsAfter = this.Scalar rowCountSql
            let! orphanWriteAfter = this.OrphanInsertProbe ()
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) ADD CONSTRAINT [FK_OrderLine_Order]: %s" strictOutcome)
            record (sprintf "  after apply: FK exists=%d, is_not_trusted=%d (0 = validated and honored by the optimizer), orphan probe=%d, OrderLine rows=%d (was %d, intact)" fkAfter notTrustedAfter orphansAfter rowsAfter rowsBefore)
            record (sprintf "  going forward: inserting a child pointing at a missing Order now returns SQL error=%d (547 = rejected by the foreign key)" orphanWriteAfter)
            flush ()   // preserve the outcome before any assertion

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "create-fk-clean: strict publish unexpectedly REFUSED over clean child data: %s" (this.Detail es)
            Assert.Equal(1L, fkAfter)                 // the FK landed
            Assert.Equal(0L, notTrustedAfter)         // TRUSTED — the whole point
            Assert.Equal(0L, orphansAfter)            // every child points at a real parent
            Assert.Equal(rowsBefore, rowsAfter)       // no row touched
            Assert.Equal(0L, orphanWriteBefore)       // before: a bad write was allowed
            Assert.Equal(547L, orphanWriteAfter)      // after: a bad write is rejected
        }

    // =====================================================================
    // 2) define-pk — add a PRIMARY KEY to a heap whose key is unique/non-NULL.
    //    TRUE OUTCOME: the strict publish builds the clustered index clean;
    //    the PK exists (sys.key_constraints type='PK'); every row intact.
    // =====================================================================
    [<Fact>]
    member this.``define-pk: adding a primary key over unique non-NULL keys builds the clustered index clean`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-define-pk.txt"), evidence.ToString()) with _ -> ()

            let pkExistsSql = "SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.OrderLine') AND type = 'PK';"
            let clusteredPkSql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OrderLine') AND is_primary_key = 1;"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let nullKeySql = "SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [Id] IS NULL;"
            let dupKeySql = "SELECT ISNULL(SUM(n),0) FROM (SELECT COUNT(*) AS n FROM [dbo].[OrderLine] GROUP BY [Id] HAVING COUNT(*) > 1) d;"
            let idNullableSql = "SELECT ISNULL((SELECT CAST(c.is_nullable AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'OrderLine' AND c.name = N'Id'), -1);"
            let digestSql = "SELECT ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM([Id],[OrderId],[Sku],[Quantity])) AS BIGINT), 0) FROM [dbo].[OrderLine];"

            // BEFORE — mint under the full baseline (PK present), unique keys.
            let! _ = this.Fresh "define-pk/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! pkMinted = this.Scalar pkExistsSql
            let! digestBefore = this.Scalar digestSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(1L, pkMinted)          // baseline mints WITH the PK
            record (sprintf "baseline: OrderLine rows=%d, PK_OrderLine exists=%d, row digest=%d" rowsBefore pkMinted digestBefore)

            // Establish the "before WITHOUT the PK" heap: drop the PK with a
            // direct ALTER (schema-only, no re-mint), and rewrite the estate to
            // match. The Id column is IDENTITY -> already NOT NULL and unique.
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [PK_OrderLine];"
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineNoPk
            let! pkAbsent = this.Scalar pkExistsSql
            let! clusteredAbsent = this.Scalar clusteredPkSql
            let! nullKeys = this.Scalar nullKeySql
            let! dupKeys = this.Scalar dupKeySql
            let! idNullable = this.Scalar idNullableSql
            Assert.Equal(0L, pkAbsent)          // it is a heap now
            Assert.Equal(0L, clusteredAbsent)   // no primary-key clustered index
            Assert.Equal(0L, nullKeys)          // no NULL key value
            Assert.Equal(0L, dupKeys)           // no duplicate key value
            Assert.Equal(0L, idNullable)        // Id is already NOT NULL (no widening needed)
            record (sprintf "before (heap): PK exists=%d, clustered PK index=%d, key NULLs=%d, duplicate keys=%d, [Id] is_nullable=%d (0 = already NOT NULL, so no NOT NULL step is needed first)" pkAbsent clusteredAbsent nullKeys dupKeys idNullable)

            // APPLY — add the primary key back and run the production publish.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" SamplePrBaseline.orderLine
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! pkAfter = this.Scalar pkExistsSql
            let! clusteredAfter = this.Scalar clusteredPkSql
            let! rowsAfter = this.Scalar rowCountSql
            let! digestAfter = this.Scalar digestSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) ADD CONSTRAINT [PK_OrderLine] PRIMARY KEY: %s" strictOutcome)
            record (sprintf "  after apply: PK exists=%d, clustered primary-key index=%d, OrderLine rows=%d (was %d, intact), row digest=%d (unchanged=%b)" pkAfter clusteredAfter rowsAfter rowsBefore digestAfter (digestAfter = digestBefore))
            flush ()

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "define-pk: strict publish unexpectedly REFUSED over unique non-NULL keys: %s" (this.Detail es)
            Assert.Equal(1L, pkAfter)                 // the primary key exists
            Assert.Equal(1L, clusteredAfter)          // as the clustered index
            Assert.Equal(rowsBefore, rowsAfter)       // every row intact
            Assert.Equal(digestBefore, digestAfter)   // every value byte-identical
        }

    // =====================================================================
    // 3) junction — a NEW M:N bridge with two FKs and a composite PK.
    //    TRUE OUTCOME: created empty by the strict publish; both FKs TRUSTED,
    //    the composite PK present (2 key columns), both orphan legs 0. Seeded
    //    valid pairs keep the legs at 0; a duplicate pair is rejected (Msg 2627).
    // =====================================================================
    [<Fact>]
    member this.``junction: a new bridge with two FKs and a composite PK applies clean, both legs trusted`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-junction.txt"), evidence.ToString()) with _ -> ()

            let tableExists (t: string) = sprintf "SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.%s');" t
            let fkExists (n: string) = sprintf "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'%s';" n
            let fkTrusted (n: string) = sprintf "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'%s'), -1);" n
            let pkExistsSql = "SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.OrderTag') AND type = 'PK';"
            let pkColCountSql = "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.object_id = OBJECT_ID(N'dbo.OrderTag') AND i.is_primary_key = 1;"
            let otRowsSql = "SELECT COUNT(*) FROM [dbo].[OrderTag];"
            let orphanOrderLegSql = "SELECT COUNT(*) FROM [dbo].[OrderTag] b LEFT JOIN [dbo].[Order] p ON p.[Id] = b.[OrderId] WHERE p.[Id] IS NULL;"
            let orphanTagLegSql = "SELECT COUNT(*) FROM [dbo].[OrderTag] b LEFT JOIN [dbo].[Tag] t ON t.[Id] = b.[TagId] WHERE t.[Id] IS NULL;"

            // BEFORE — the two parents exist and hold rows; the bridge does not.
            let! _ = this.Fresh "junction/fresh"
            let! orderRows = this.Scalar "SELECT COUNT(*) FROM [dbo].[Order];"
            let! tagBefore = this.Scalar (tableExists "Tag")
            let! otBefore = this.Scalar (tableExists "OrderTag")
            Assert.True(orderRows > 0L, "Order must hold rows for the proof")
            Assert.Equal(0L, tagBefore)         // Tag does not exist yet
            Assert.Equal(0L, otBefore)          // the bridge does not exist yet
            record (sprintf "baseline: Order rows=%d, dbo.Tag exists=%d, dbo.OrderTag exists=%d" orderRows tagBefore otBefore)

            // APPLY — add the Tag parent and the OrderTag bridge as new estate
            // files, then run the production-faithful publish.
            fixture.Rewrite tagRel tagCreate
            fixture.Rewrite orderTagRel orderTagCreate
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! tagAfter = this.Scalar (tableExists "Tag")
            let! otAfter = this.Scalar (tableExists "OrderTag")
            let! fkOrder = this.Scalar (fkExists "FK_OrderTag_Order")
            let! fkOrderTrust = this.Scalar (fkTrusted "FK_OrderTag_Order")
            let! fkTag = this.Scalar (fkExists "FK_OrderTag_Tag")
            let! fkTagTrust = this.Scalar (fkTrusted "FK_OrderTag_Tag")
            let! pk = this.Scalar pkExistsSql
            let! pkCols = this.Scalar pkColCountSql
            let! otRows = this.Scalar otRowsSql
            let! legOrder0 = this.Scalar orphanOrderLegSql
            let! legTag0 = this.Scalar orphanTagLegSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) CREATE TABLE dbo.Tag + dbo.OrderTag (composite PK over two FKs): %s" strictOutcome)
            record (sprintf "  after apply: Tag exists=%d, OrderTag exists=%d, FK_OrderTag_Order exists=%d is_not_trusted=%d, FK_OrderTag_Tag exists=%d is_not_trusted=%d" tagAfter otAfter fkOrder fkOrderTrust fkTag fkTagTrust)
            record (sprintf "  composite PK present=%d, PK key columns=%d (2 = the pair), OrderTag rows=%d (created empty; strict publish does not mint), orphan legs: Order=%d Tag=%d" pk pkCols otRows legOrder0 legTag0)
            flush ()

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "junction: strict publish unexpectedly REFUSED for a brand-new empty bridge: %s" (this.Detail es)
            Assert.Equal(1L, tagAfter)
            Assert.Equal(1L, otAfter)
            Assert.Equal(1L, fkOrder)
            Assert.Equal(0L, fkOrderTrust)      // Order leg TRUSTED
            Assert.Equal(1L, fkTag)
            Assert.Equal(0L, fkTagTrust)        // Tag leg TRUSTED
            Assert.Equal(1L, pk)                // the composite PK exists
            Assert.Equal(2L, pkCols)            // spanning both FK columns
            Assert.Equal(0L, otRows)            // created empty
            Assert.Equal(0L, legOrder0)         // no orphan pairs (vacuously, empty)
            Assert.Equal(0L, legTag0)

            // Prove the shape holds over REAL pairs: seed the Tag parent and a
            // handful of valid pairs, re-probe both legs (still 0), and show the
            // composite PK rejects a duplicate pair.
            let! _ = this.Exec "INSERT INTO [dbo].[Tag] ([Name]) VALUES (N'red'), (N'blue'), (N'green');"
            let! _ = this.Exec "INSERT INTO [dbo].[OrderTag] ([OrderId],[TagId]) SELECT TOP 3 o.[Id], 1 FROM [dbo].[Order] o ORDER BY o.[Id];"
            let! _ = this.Exec "INSERT INTO [dbo].[OrderTag] ([OrderId],[TagId]) SELECT TOP 2 o.[Id], 2 FROM [dbo].[Order] o ORDER BY o.[Id];"
            let! pairRows = this.Scalar otRowsSql
            let! legOrder = this.Scalar orphanOrderLegSql
            let! legTag = this.Scalar orphanTagLegSql
            // a duplicate of an existing (smallest Order, Tag 1) pair -> PK violation.
            let! dupMsg = this.ExecMsg "INSERT INTO [dbo].[OrderTag] ([OrderId],[TagId]) SELECT TOP 1 o.[Id], 1 FROM [dbo].[Order] o ORDER BY o.[Id];"
            record (sprintf "seeded valid pairs: OrderTag rows=%d, orphan legs after seeding: Order=%d Tag=%d" pairRows legOrder legTag)
            record (sprintf "  duplicate pair rejected by the composite PK: %s" (if dupMsg = "" then "NOT REJECTED (unexpected)" else dupMsg))
            flush ()

            this.DeleteTableFile tagRel         // cleanup so the extra files cannot bleed
            this.DeleteTableFile orderTagRel

            Assert.True(pairRows > 0L, "valid pairs must be seeded")
            Assert.Equal(0L, legOrder)          // every pair points at a real Order
            Assert.Equal(0L, legTag)            // every pair points at a real Tag
            Assert.Contains("2627", dupMsg)     // composite PK forbids a duplicate pair
        }

    // =====================================================================
    // 4) create-fk-orphan — add an FK while an ORPHAN exists.
    //    TRUE OUTCOME (discovered, add-check shape): SSDT adds the FK
    //    WITH NOCHECK, then WITH CHECK CHECK fails Msg 547 -> the strict publish
    //    is REFUSED and the FK is left UNTRUSTED. Remedy: reconcile the orphan,
    //    then WITH CHECK CHECK re-validates -> is_not_trusted flips 1 -> 0.
    // =====================================================================
    [<Fact>]
    member this.``create-fk-orphan: an orphan blocks the FK at WITH CHECK CHECK and leaves it untrusted; reconcile then re-validate to trusted`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-create-fk-orphan.txt"), evidence.ToString()) with _ -> ()

            let fkExistsSql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order';"
            let notTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order'), -1);"
            let orphanProbeSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] c LEFT JOIN [dbo].[Order] p ON c.[OrderId] = p.[Id] WHERE p.[Id] IS NULL;"

            // BEFORE — mint clean, drop the FK, then SEED an orphan child by
            // repointing one OrderLine at an Order id that does not exist.
            let! _ = this.Fresh "create-fk-orphan/fresh"
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order];"
            let! _ = this.Exec "UPDATE [dbo].[OrderLine] SET [OrderId] = (SELECT ISNULL(MAX([Id]),0) + 100000 FROM [dbo].[Order]) WHERE [Id] = (SELECT MIN([Id]) FROM [dbo].[OrderLine]);"
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineNoFk
            let! orphansBefore = this.Scalar orphanProbeSql
            Assert.True(orphansBefore > 0L, "an orphan child must exist for the proof")
            record (sprintf "before: orphan probe (OrderLine->Order)=%d (a child points at an Order that does not exist)" orphansBefore)

            // APPLY (blocking) — add the FK back; the production publish adds it
            // WITH NOCHECK, then WITH CHECK CHECK fails Msg 547 on the orphan.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" SamplePrBaseline.orderLine
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let blockDetail =
                match strict with
                | Ok () -> "APPLIED (unexpected)"
                | Error es -> this.Detail es
            let! fkAfterBlock = this.Scalar fkExistsSql
            let! notTrustedAfterBlock = this.Scalar notTrustedSql
            record "production publish ADD CONSTRAINT [FK_OrderLine_Order] over an orphan: REFUSED at the WITH CHECK CHECK re-validation (Msg 547)."
            record (sprintf "  after block: FK exists=%d, is_not_trusted=%d (1 = added WITH NOCHECK but never validated -> untrusted; -1 = rolled back / absent)" fkAfterBlock notTrustedAfterBlock)
            record "  strict detail:"
            record blockDetail
            flush ()

            Assert.Contains("twin.publish.failed", (match strict with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Contains("Msg 547", blockDetail)         // the referential conflict
            Assert.NotEqual(0L, notTrustedAfterBlock)       // NOT trusted-and-validated

            // REMEDY — the NOCHECK -> reconcile -> WITH CHECK CHECK ladder.
            // Ensure the constraint is present WITH NOCHECK (re-add it if the
            // failed publish rolled it back), confirming the untrusted state.
            if fkAfterBlock = 0L then
                let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] WITH NOCHECK ADD CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]);"
                ()
            let! notTrustedUntrusted = this.Scalar notTrustedSql
            Assert.Equal(1L, notTrustedUntrusted)           // present, untrusted (guards nothing)
            record (sprintf "remedy step 1 (constraint present WITH NOCHECK): is_not_trusted=%d" notTrustedUntrusted)

            // WITH CHECK CHECK while the orphan is STILL there -> fails Msg 547,
            // stays untrusted. Trust cannot be granted over violating data.
            let! failMsg = this.ExecMsg "ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];"
            let! notTrustedStillDirty = this.Scalar notTrustedSql
            Assert.Contains("547", failMsg)
            Assert.Equal(1L, notTrustedStillDirty)
            record (sprintf "remedy step 2 (WITH CHECK CHECK while the orphan remains): %s -> is_not_trusted=%d (still untrusted)" failMsg notTrustedStillDirty)

            // Reconcile the orphan (repoint it at a real Order), then re-validate.
            let! _ = this.Exec "UPDATE [dbo].[OrderLine] SET [OrderId] = (SELECT MIN([Id]) FROM [dbo].[Order]) WHERE [OrderId] NOT IN (SELECT [Id] FROM [dbo].[Order]);"
            let! orphansAfterReconcile = this.Scalar orphanProbeSql
            Assert.Equal(0L, orphansAfterReconcile)
            let! okMsg = this.ExecMsg "ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];"
            let! notTrustedFinal = this.Scalar notTrustedSql
            record (sprintf "remedy step 3 (reconcile -> orphan probe=%d -> WITH CHECK CHECK): %s -> is_not_trusted=%d (0 = trusted)" orphansAfterReconcile (if okMsg = "" then "OK" else okMsg) notTrustedFinal)
            flush ()

            Assert.Equal("", okMsg)                         // re-validation succeeds over clean data
            Assert.Equal(0L, notTrustedFinal)               // TRUSTED — the honest end state
        }

    // =====================================================================
    // 5) toggle-trust — turn an existing UNTRUSTED FK into a TRUSTED one.
    //    TRUE OUTCOME: over clean data, WITH CHECK CHECK flips is_not_trusted
    //    1 -> 0. While a row violates, WITH CHECK CHECK fails Msg 547 (trust
    //    needs clean data first) — proven, then reconciled back to trusted.
    // =====================================================================
    [<Fact>]
    member this.``toggle-trust: WITH CHECK CHECK makes an untrusted FK trusted over clean data; a violating row keeps it untrusted`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-toggle-trust.txt"), evidence.ToString()) with _ -> ()

            let notTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order'), -1);"
            let fkExistsSql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order';"
            let orphanProbeSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] c LEFT JOIN [dbo].[Order] p ON c.[OrderId] = p.[Id] WHERE p.[Id] IS NULL;"

            // BEFORE — mint clean, then re-create the FK WITH NOCHECK so it is
            // present but UNTRUSTED even though the data is clean (NOCHECK skips
            // validation). This is the state a NOCHECK shortcut leaves behind.
            let! _ = this.Fresh "toggle-trust/fresh"
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order];"
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] WITH NOCHECK ADD CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]);"
            let! fkExists = this.Scalar fkExistsSql
            let! orphansClean = this.Scalar orphanProbeSql
            let! notTrustedBefore = this.Scalar notTrustedSql
            Assert.Equal(1L, fkExists)          // the FK is present
            Assert.Equal(0L, orphansClean)      // the data is clean
            Assert.Equal(1L, notTrustedBefore)  // yet untrusted (added WITH NOCHECK)
            record (sprintf "before: FK_OrderLine_Order exists=%d, orphan probe=%d (clean), is_not_trusted=%d (1 = present but ignored by the optimizer, guards nothing)" fkExists orphansClean notTrustedBefore)

            // PRIMARY — WITH CHECK CHECK over clean data flips it to TRUSTED.
            let! okMsg = this.ExecMsg "ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];"
            let! notTrustedAfter = this.Scalar notTrustedSql
            record (sprintf "toggle-trust (ALTER TABLE ... WITH CHECK CHECK over clean data): %s -> is_not_trusted %d -> %d" (if okMsg = "" then "OK" else okMsg) notTrustedBefore notTrustedAfter)
            flush ()
            Assert.Equal("", okMsg)             // validation succeeds
            Assert.Equal(0L, notTrustedAfter)   // TRUSTED — the end-state proof

            // SECONDARY (the guard the note names) — return the FK to NOCHECK,
            // introduce a violating row, and show WITH CHECK CHECK fails Msg 547
            // and leaves it untrusted: trust requires clean data first.
            let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] NOCHECK CONSTRAINT [FK_OrderLine_Order];"
            let! notTrustedNoCheck = this.Scalar notTrustedSql
            let! _ = this.Exec "UPDATE [dbo].[OrderLine] SET [OrderId] = (SELECT ISNULL(MAX([Id]),0) + 100000 FROM [dbo].[Order]) WHERE [Id] = (SELECT MIN([Id]) FROM [dbo].[OrderLine]);"
            let! orphansDirty = this.Scalar orphanProbeSql
            let! failMsg = this.ExecMsg "ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];"
            let! notTrustedDirty = this.Scalar notTrustedSql
            record (sprintf "guard: NOCHECK again (is_not_trusted=%d), seed a violating row (orphan probe=%d), then WITH CHECK CHECK: %s -> is_not_trusted=%d (still 1)" notTrustedNoCheck orphansDirty failMsg notTrustedDirty)
            flush ()
            Assert.Equal(1L, notTrustedNoCheck) // NOCHECK returns it to untrusted
            Assert.True(orphansDirty > 0L, "a violating row must exist for the guard")
            Assert.Contains("547", failMsg)     // WITH CHECK CHECK refuses over a violating row
            Assert.Equal(1L, notTrustedDirty)   // and it stays untrusted

            // Restore the honest end state: reconcile, re-validate to trusted.
            let! _ = this.Exec "UPDATE [dbo].[OrderLine] SET [OrderId] = (SELECT MIN([Id]) FROM [dbo].[Order]) WHERE [OrderId] NOT IN (SELECT [Id] FROM [dbo].[Order]);"
            let! okMsg2 = this.ExecMsg "ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];"
            let! notTrustedEnd = this.Scalar notTrustedSql
            record (sprintf "recover: reconcile the orphan, WITH CHECK CHECK again: %s -> is_not_trusted=%d (0 = trusted)" (if okMsg2 = "" then "OK" else okMsg2) notTrustedEnd)
            flush ()
            Assert.Equal("", okMsg2)
            Assert.Equal(0L, notTrustedEnd)     // trusted once the data is clean again
        }
