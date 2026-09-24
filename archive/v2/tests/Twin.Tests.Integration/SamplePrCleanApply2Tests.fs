module Twin.Tests.Integration.SamplePrCleanApply2Tests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the "clean additive apply, second wave" archetype: four
// operations that each carry MORE STRUCTURE than the first clean-apply wave,
// proven the make-mandatory way but from the SAFE side. The BEFORE estate is
// materialized with real-shaped synthetic data, the op's additive edit is
// applied, and the PRODUCTION-FAITHFUL publish (SamplePrPublish.strict,
// BlockOnPossibleDataLoss = true — the deployment a real environment runs) is
// the objective proof. Each op is safe in production, so it applies clean, and
// the proof is:
//   1. strict publish returns Ok () under BlockOnPossibleDataLoss = true;
//   2. the change is LIVE (sys.tables / sys.columns / sys.foreign_keys /
//      sys.default_constraints / sys.key_constraints show it);
//   3. the existing rows are intact (rowcounts stable; for the function-default
//      backfill every existing row is stamped; for modify-default a channel
//      digest is compared before/after and a row written under the old default
//      keeps its stored value).
//
// The four ops, each carrying structure the first wave did not:
//   1. create-entity  — a brand-new table (Address) with an IDENTITY PK and an
//                       FK to Customer. Purest additive CREATE TABLE.
//   2. audit-columns  — add CreatedOn/CreatedBy/UpdatedOn to Order as NOT NULL
//                       columns whose FUNCTION defaults (SYSUTCDATETIME(),
//                       SUSER_SNAME()) backfill every existing row.
//   3. temporal-new   — a brand-new SYSTEM-VERSIONED entity (Rate) with period
//                       columns and a paired history table.
//   4. modify-default — change an existing named default's VALUE (Web -> Store).
//                       Teaches the nuance: only FUTURE inserts see the new
//                       default; existing rows keep their stored values.
//
// Every fact is self-contained and order-independent: it rewrites every table
// it touches back to the canonical baseline, removes any stray added file
// (Address / Rate), DROPS the twin database, and reconverges before applying
// its edit — the drop is what reverts a prior fact's applied strict edit, since
// a plain re-mint's schema fingerprint still matches the baseline on disk.
// Any file a fact adds is also retracted at the fact's end so it never bleeds.
// ---------------------------------------------------------------------------

/// Its own container + port (isolated from every other Twin fixture; the first
/// clean-apply wave owns 21836, this second wave owns 21837).
type SamplePrCleanApply2Fixture () =
    inherit TwinEstateFixture ("twin-e2e-clean2", 21837)

[<Collection("Twin-Docker")>]
type SamplePrCleanApply2Tests (fixture: SamplePrCleanApply2Fixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op additive schema edits (baseline + one change) --------

    // create-entity: a brand-new Address table — IDENTITY PK, FK to Customer,
    // following the estate's conventions. It rides its own estate file; the
    // baseline tables are untouched. Retracted at the fact's end.
    let addressRel = "Tables/dbo.Address.sql"
    let addressTable =
        """CREATE TABLE [dbo].[Address] (
    [Id]         INT            IDENTITY(1,1) NOT NULL,
    [CustomerId] INT            NOT NULL,
    [Line1]      NVARCHAR(200)  NOT NULL,
    [City]       NVARCHAR(100)  NOT NULL,
    [PostalCode] NVARCHAR(20)   NULL,
    CONSTRAINT [PK_Address] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Address_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
);
"""

    // audit-columns: Order gains three NOT NULL audit stamps whose FUNCTION
    // defaults backfill the existing rows as the columns land (the safe way to
    // add a required column to a populated table — see add-default).
    let orderWithAudit =
        """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    [CreatedOn]  DATETIME2     NOT NULL CONSTRAINT [DF_Order_CreatedOn] DEFAULT (SYSUTCDATETIME()),
    [CreatedBy]  NVARCHAR(100) NOT NULL CONSTRAINT [DF_Order_CreatedBy] DEFAULT (SUSER_SNAME()),
    [UpdatedOn]  DATETIME2     NOT NULL CONSTRAINT [DF_Order_UpdatedOn] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    // temporal-new: a brand-new system-versioned Rate table — period columns +
    // SYSTEM_VERSIONING ON with a paired history table. One CREATE statement
    // (the history table is auto-created by SQL Server). Retracted at the end.
    let rateRel = "Tables/dbo.Rate.sql"
    let rateTable =
        """CREATE TABLE [dbo].[Rate] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Code]      NVARCHAR(20)   NOT NULL,
    [Amount]    DECIMAL(18,4)  NOT NULL,
    [ValidFrom] DATETIME2      GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]   DATETIME2      GENERATED ALWAYS AS ROW END   NOT NULL,
    CONSTRAINT [PK_Rate] PRIMARY KEY ([Id]),
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Rate_History]));
"""

    // modify-default: Order.Channel gains a named default, then that default's
    // VALUE changes Web -> Store. The BEFORE state already HAS a default.
    let orderChannelWebDefault =
        """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL CONSTRAINT [DF_Order_Channel] DEFAULT (N'Web'),
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""
    let orderChannelStoreDefault = orderChannelWebDefault.Replace("DEFAULT (N'Web')", "DEFAULT (N'Store')")

    interface IClassFixture<SamplePrCleanApply2Fixture>

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

    /// Delete an estate file if present (retract an added table file).
    member private _.DeleteTableFile (rel: string) : unit =
        try System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)))
        with _ -> ()

    /// Restore every table to baseline, remove any stray added file, drop the
    /// twin database, reconverge — the per-fact isolation primitive that makes
    /// the class order-independent (identical to the first clean-apply wave).
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
    // 1) create-entity — a brand-new table (IDENTITY PK + FK to Customer)
    //    publishes clean; its PK and FK land; existing tables are untouched.
    // =====================================================================
    [<Fact>]
    member this.``create-entity: a new table with an identity PK and an FK to Customer applies clean`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-create-entity.txt"), evidence.ToString()) with _ -> ()

            let tableExistsSql = "SELECT COUNT(*) FROM sys.tables WHERE name = N'Address';"
            let pkExistsSql = "SELECT COUNT(*) FROM sys.key_constraints WHERE name = N'PK_Address' AND type = 'PK';"
            let fkExistsSql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_Address_Customer';"
            let fkTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_Address_Customer'), -1);"
            let identityColsSql = "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'dbo.Address');"
            // metadata-safe row count: reads partition stats by object_id, so it never binds
            // [dbo].[Address] directly and cannot throw if a refusal left the table absent.
            let addressRowsSql = "SELECT ISNULL((SELECT SUM(row_count) FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID(N'dbo.Address') AND index_id IN (0,1)), -1);"
            let customerRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let orderRowsSql = "SELECT COUNT(*) FROM [dbo].[Order];"
            let orderLineRowsSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"

            // BEFORE — the baseline estate; Address does not exist.
            let! _ = this.Fresh "create-entity/fresh"
            let! tableBefore = this.Scalar tableExistsSql
            let! customerBefore = this.Scalar customerRowsSql
            let! orderBefore = this.Scalar orderRowsSql
            let! orderLineBefore = this.Scalar orderLineRowsSql
            Assert.Equal(0L, tableBefore)   // Address does not exist yet
            Assert.True(customerBefore > 0L, "Customer must hold rows so 'existing rows intact' is meaningful")
            record (sprintf "baseline: Address exists=%d; existing rows Customer=%d, Order=%d, OrderLine=%d" tableBefore customerBefore orderBefore orderLineBefore)

            // APPLY — the new table rides its own estate file; nothing else is edited.
            fixture.Rewrite addressRel addressTable
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! tableAfter = this.Scalar tableExistsSql
            let! pkAfter = this.Scalar pkExistsSql
            let! fkAfter = this.Scalar fkExistsSql
            let! fkTrusted = this.Scalar fkTrustedSql
            let! identityCols = this.Scalar identityColsSql
            let! addressRows = this.Scalar addressRowsSql
            let! customerAfter = this.Scalar customerRowsSql
            let! orderAfter = this.Scalar orderRowsSql
            let! orderLineAfter = this.Scalar orderLineRowsSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) CREATE TABLE [dbo].[Address] (+ PK, + FK to Customer): %s" strictOutcome)
            record (sprintf "  after apply: Address exists=%d, PK_Address exists=%d, FK_Address_Customer exists=%d (is_not_trusted=%d), identity columns=%d, Address rows=%d (created empty)" tableAfter pkAfter fkAfter fkTrusted identityCols addressRows)
            record (sprintf "  existing rows intact: Customer=%d, Order=%d, OrderLine=%d" customerAfter orderAfter orderLineAfter)
            flush ()

            this.DeleteTableFile addressRel   // retract the added file so it cannot bleed

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: create-entity refused by production publish: %s" (this.Detail es)
            Assert.Equal(1L, tableAfter)        // the new table exists
            Assert.Equal(1L, pkAfter)           // its primary key exists
            Assert.Equal(1L, fkAfter)           // its foreign key exists
            Assert.Equal(0L, fkTrusted)         // and is trusted (created WITH CHECK over an empty child)
            Assert.Equal(1L, identityCols)      // the IDENTITY PK column landed
            Assert.Equal(0L, addressRows)       // strict publish creates it empty (no mint)
            Assert.Equal(customerBefore, customerAfter)   // existing tables untouched
            Assert.Equal(orderBefore, orderAfter)
            Assert.Equal(orderLineBefore, orderLineAfter)
        }

    // =====================================================================
    // 2) audit-columns — NOT NULL audit stamps whose FUNCTION defaults
    //    backfill every existing row (add-default, with function defaults).
    // =====================================================================
    [<Fact>]
    member this.``audit-columns: NOT NULL audit stamps with function defaults apply clean and backfill existing rows`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-audit-columns.txt"), evidence.ToString()) with _ -> ()

            let colExistsSql (col: string) = sprintf "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Order' AND c.name = N'%s';" col
            let colNullableSql (col: string) = sprintf "SELECT ISNULL((SELECT CAST(c.is_nullable AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Order' AND c.name = N'%s'), -1);" col
            let defExistsSql (name: string) = sprintf "SELECT COUNT(*) FROM sys.default_constraints WHERE name = N'%s';" name
            let defDefSql (name: string) = sprintf "SELECT ISNULL((SELECT definition FROM sys.default_constraints WHERE name = N'%s'), N'');" name
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Order];"
            let nullCreatedOnSql = "SELECT COUNT(*) FROM [dbo].[Order] WHERE [CreatedOn] IS NULL;"
            let nullCreatedBySql = "SELECT COUNT(*) FROM [dbo].[Order] WHERE [CreatedBy] IS NULL;"
            let nullUpdatedOnSql = "SELECT COUNT(*) FROM [dbo].[Order] WHERE [UpdatedOn] IS NULL;"
            let stampedBySql = "SELECT ISNULL((SELECT TOP 1 [CreatedBy] FROM [dbo].[Order]), N'');"

            // BEFORE — populated Order, no audit columns.
            let! _ = this.Fresh "audit-columns/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! createdOnBefore = this.Scalar (colExistsSql "CreatedOn")
            Assert.True(rowsBefore > 0L, "Order must hold rows for the proof")
            Assert.Equal(0L, createdOnBefore)   // audit columns do not exist yet
            record (sprintf "baseline: Order rows=%d, CreatedOn column exists=%d" rowsBefore createdOnBefore)

            // APPLY — production-faithful publish of the NOT NULL audit columns.
            // The function defaults backfill existing rows as the columns land.
            fixture.Rewrite "Tables/dbo.Order.sql" orderWithAudit
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! createdOnAfter = this.Scalar (colExistsSql "CreatedOn")
            let! createdByAfter = this.Scalar (colExistsSql "CreatedBy")
            let! updatedOnAfter = this.Scalar (colExistsSql "UpdatedOn")
            let! createdOnNullable = this.Scalar (colNullableSql "CreatedOn")
            let! createdByNullable = this.Scalar (colNullableSql "CreatedBy")
            let! dfCreatedOn = this.Scalar (defExistsSql "DF_Order_CreatedOn")
            let! dfCreatedBy = this.Scalar (defExistsSql "DF_Order_CreatedBy")
            let! dfUpdatedOn = this.Scalar (defExistsSql "DF_Order_UpdatedOn")
            let! dfCreatedOnDef = this.ScalarStr (defDefSql "DF_Order_CreatedOn")
            let! dfCreatedByDef = this.ScalarStr (defDefSql "DF_Order_CreatedBy")
            let! rowsAfter = this.Scalar rowCountSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) ADD [CreatedOn]/[CreatedBy]/[UpdatedOn] NOT NULL with function defaults: %s" strictOutcome)
            record (sprintf "  after apply: CreatedOn exists=%d (is_nullable=%d), CreatedBy exists=%d (is_nullable=%d), UpdatedOn exists=%d" createdOnAfter createdOnNullable createdByAfter createdByNullable updatedOnAfter)
            record (sprintf "  named defaults: DF_Order_CreatedOn=%d def=%s, DF_Order_CreatedBy=%d def=%s, DF_Order_UpdatedOn=%d" dfCreatedOn dfCreatedOnDef dfCreatedBy dfCreatedByDef dfUpdatedOn)

            // Guard BEFORE the column-dependent backfill queries: on a refusal the audit columns
            // would not exist, so capture the metadata evidence + refusal detail and stop cleanly.
            match strict with
            | Ok () -> ()
            | Error es -> flush (); failwithf "PROOF FAILED: audit-columns refused by production publish: %s" (this.Detail es)

            let! nullCreatedOn = this.Scalar nullCreatedOnSql
            let! nullCreatedBy = this.Scalar nullCreatedBySql
            let! nullUpdatedOn = this.Scalar nullUpdatedOnSql
            let! stampedBy = this.ScalarStr stampedBySql
            record (sprintf "  backfill: Order rows=%d (intact), CreatedOn NULLs=%d, CreatedBy NULLs=%d, UpdatedOn NULLs=%d, sample CreatedBy stamp=%s" rowsAfter nullCreatedOn nullCreatedBy nullUpdatedOn stampedBy)
            flush ()

            Assert.Equal(1L, createdOnAfter)     // all three columns exist
            Assert.Equal(1L, createdByAfter)
            Assert.Equal(1L, updatedOnAfter)
            Assert.Equal(0L, createdOnNullable)  // and are NOT NULL (mandatory)
            Assert.Equal(0L, createdByNullable)
            Assert.Equal(1L, dfCreatedOn)        // named defaults present
            Assert.Equal(1L, dfCreatedBy)
            Assert.Equal(1L, dfUpdatedOn)
            Assert.Equal(rowsBefore, rowsAfter)  // rows intact
            Assert.Equal(0L, nullCreatedOn)      // every existing row backfilled
            Assert.Equal(0L, nullCreatedBy)
            Assert.Equal(0L, nullUpdatedOn)
            Assert.NotEqual<string>("", stampedBy)  // the SUSER_SNAME() stamp is a real login name
        }

    // =====================================================================
    // 3) temporal-new — a brand-new SYSTEM-VERSIONED entity publishes clean:
    //    the table is system-versioned, its history table exists, and the
    //    period columns are GENERATED ALWAYS. Existing tables are untouched.
    // =====================================================================
    [<Fact>]
    member this.``temporal-new: a new system-versioned entity applies clean with its history table`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-temporal-new.txt"), evidence.ToString()) with _ -> ()

            let rateExistsSql = "SELECT COUNT(*) FROM sys.tables WHERE name = N'Rate';"
            let rateTemporalTypeSql = "SELECT ISNULL((SELECT CAST(temporal_type AS BIGINT) FROM sys.tables WHERE name = N'Rate'), -1);"
            let historyExistsSql = "SELECT COUNT(*) FROM sys.tables WHERE name = N'Rate_History';"
            let historyTemporalTypeSql = "SELECT ISNULL((SELECT CAST(temporal_type AS BIGINT) FROM sys.tables WHERE name = N'Rate_History'), -1);"
            let historyLinkSql = "SELECT ISNULL((SELECT h.name FROM sys.tables t JOIN sys.tables h ON h.object_id = t.history_table_id WHERE t.name = N'Rate'), N'');"
            let periodColsSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Rate') AND generated_always_type <> 0;"
            let pkRateSql = "SELECT COUNT(*) FROM sys.key_constraints WHERE name = N'PK_Rate' AND type = 'PK';"
            let customerRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let orderRowsSql = "SELECT COUNT(*) FROM [dbo].[Order];"
            let orderLineRowsSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"

            // BEFORE — the baseline estate; Rate does not exist.
            let! _ = this.Fresh "temporal-new/fresh"
            let! rateBefore = this.Scalar rateExistsSql
            let! customerBefore = this.Scalar customerRowsSql
            let! orderBefore = this.Scalar orderRowsSql
            let! orderLineBefore = this.Scalar orderLineRowsSql
            Assert.Equal(0L, rateBefore)   // Rate does not exist yet
            Assert.True(customerBefore > 0L, "Customer must hold rows so 'existing rows intact' is meaningful")
            record (sprintf "baseline: Rate exists=%d; existing rows Customer=%d, Order=%d, OrderLine=%d" rateBefore customerBefore orderBefore orderLineBefore)

            // APPLY — the system-versioned CREATE rides its own estate file.
            fixture.Rewrite rateRel rateTable
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! rateAfter = this.Scalar rateExistsSql
            let! rateTemporalType = this.Scalar rateTemporalTypeSql
            let! historyExists = this.Scalar historyExistsSql
            let! historyTemporalType = this.Scalar historyTemporalTypeSql
            let! historyLink = this.ScalarStr historyLinkSql
            let! periodCols = this.Scalar periodColsSql
            let! pkRate = this.Scalar pkRateSql
            let! customerAfter = this.Scalar customerRowsSql
            let! orderAfter = this.Scalar orderRowsSql
            let! orderLineAfter = this.Scalar orderLineRowsSql
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) CREATE system-versioned TABLE [dbo].[Rate] (SYSTEM_VERSIONING = ON, HISTORY_TABLE = [dbo].[Rate_History]): %s" strictOutcome)
            record (sprintf "  after apply: Rate exists=%d, temporal_type=%d (2 = SYSTEM_VERSIONED_TEMPORAL_TABLE), PK_Rate exists=%d, GENERATED-ALWAYS period columns=%d" rateAfter rateTemporalType pkRate periodCols)
            record (sprintf "  history: Rate_History exists=%d, temporal_type=%d (1 = HISTORY_TABLE), linked history table name=%s" historyExists historyTemporalType historyLink)
            record (sprintf "  existing rows intact: Customer=%d, Order=%d, OrderLine=%d" customerAfter orderAfter orderLineAfter)
            flush ()   // capture BEFORE any assertion so a refusal's detail is preserved as a finding

            this.DeleteTableFile rateRel   // retract the added file so it cannot bleed

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: temporal-new refused by production publish: %s" (this.Detail es)
            Assert.Equal(1L, rateAfter)              // the new table exists
            Assert.Equal(2L, rateTemporalType)       // system-versioned
            Assert.Equal(1L, historyExists)          // its history table exists
            Assert.Equal(1L, historyTemporalType)    // marked as a history table
            Assert.Equal<string>("Rate_History", historyLink)  // linked as Rate's history table
            Assert.Equal(2L, periodCols)             // ValidFrom + ValidTo GENERATED ALWAYS
            Assert.Equal(1L, pkRate)                 // PK landed
            Assert.Equal(customerBefore, customerAfter)   // existing tables untouched
            Assert.Equal(orderBefore, orderAfter)
            Assert.Equal(orderLineBefore, orderLineAfter)
        }

    // =====================================================================
    // 4) modify-default — changing a named default's VALUE applies clean and
    //    changes only FUTURE inserts; existing rows keep their stored values.
    // =====================================================================
    [<Fact>]
    member this.``modify-default: changing a default's value applies clean and leaves existing rows unchanged`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-modify-default.txt"), evidence.ToString()) with _ -> ()

            let defExistsSql = "SELECT COUNT(*) FROM sys.default_constraints WHERE name = N'DF_Order_Channel';"
            let defDefSql = "SELECT ISNULL((SELECT definition FROM sys.default_constraints WHERE name = N'DF_Order_Channel'), N'');"
            let maxIdSql = "SELECT ISNULL(MAX([Id]), 0) FROM [dbo].[Order];"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Order];"
            let channelOfSql (id: int64) = sprintf "SELECT ISNULL((SELECT [Channel] FROM [dbo].[Order] WHERE [Id] = %d), N'');" id
            let mintedDigestSql (maxMinted: int64) = sprintf "SELECT ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM([Channel])) AS BIGINT), 0) FROM [dbo].[Order] WHERE [Id] <= %d;" maxMinted
            // Insert a row that OMITS Channel, so whichever default is live fills it. FKs are satisfied
            // from the smallest existing Customer / Status keys; Total/PlacedOn carry supplied values.
            let insertOmittingChannelSql =
                "INSERT INTO [dbo].[Order] ([CustomerId],[StatusId],[Total],[PlacedOn]) VALUES ((SELECT MIN([Id]) FROM [dbo].[Customer]), (SELECT MIN([Id]) FROM [dbo].[Status]), 0, SYSUTCDATETIME());"

            // BEFORE — Order rewritten to carry a named default DEFAULT (N'Web') on Channel, converged
            // (this re-mints, filling Channel with real-shaped values, NOT the default).
            let! _ = this.Fresh "modify-default/fresh"
            fixture.Rewrite "Tables/dbo.Order.sql" orderChannelWebDefault
            let! _ = this.Converge "modify-default/web-default"
            let! defBeforeExists = this.Scalar defExistsSql
            let! defBefore = this.ScalarStr defDefSql
            let! maxMintedId = this.Scalar maxIdSql
            let! mintedRowsBefore = this.Scalar rowCountSql
            let! mintedDigestBefore = this.Scalar (mintedDigestSql maxMintedId)
            Assert.Equal(1L, defBeforeExists)   // the BEFORE state already HAS a default
            Assert.True(mintedRowsBefore > 0L, "Order must hold rows for the proof")
            record (sprintf "baseline: DF_Order_Channel exists=%d, definition=%s; Order minted rows=%d (Channel filled by the mint, not the default), max minted Id=%d, minted-Channel digest=%d" defBeforeExists defBefore mintedRowsBefore maxMintedId mintedDigestBefore)

            // A row written UNDER THE OLD default (Web), before the change.
            let! _ = this.Exec insertOmittingChannelSql
            let! idWeb = this.Scalar maxIdSql
            let! channelWebAtInsert = this.ScalarStr (channelOfSql idWeb)
            record (sprintf "row Id=%d inserted under the OLD default: Channel=%s" idWeb channelWebAtInsert)

            // APPLY — change the default's VALUE to Store (SSDT drops the old default, adds the new).
            fixture.Rewrite "Tables/dbo.Order.sql" orderChannelStoreDefault
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let strictOutcome =
                match strict with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! defAfterExists = this.Scalar defExistsSql
            let! defAfter = this.ScalarStr defDefSql
            let! mintedDigestAfter = this.Scalar (mintedDigestSql maxMintedId)
            let! channelWebAfterChange = this.ScalarStr (channelOfSql idWeb)

            // A row written UNDER THE NEW default (Store), after the change.
            let! _ = this.Exec insertOmittingChannelSql
            let! idStore = this.Scalar maxIdSql
            let! channelStoreAtInsert = this.ScalarStr (channelOfSql idStore)

            record (sprintf "production publish (BlockOnPossibleDataLoss=true) modify DF_Order_Channel DEFAULT (N'Web') -> (N'Store') [SSDT drop-then-add]: %s" strictOutcome)
            record (sprintf "  after apply: DF_Order_Channel exists=%d, definition=%s (was %s)" defAfterExists defAfter defBefore)
            record (sprintf "  FUTURE inserts: row Id=%d written after the change carries Channel=%s (the NEW default)" idStore channelStoreAtInsert)
            record (sprintf "  EXISTING rows unchanged: row Id=%d (written under the old default) still Channel=%s; minted-Channel digest %d -> %d" idWeb channelWebAfterChange mintedDigestBefore mintedDigestAfter)
            flush ()

            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: modify-default refused by production publish: %s" (this.Detail es)
            Assert.Equal(1L, defAfterExists)                 // the named default still exists
            Assert.Contains("Web", defBefore)                // it WAS Web
            Assert.DoesNotContain("Store", defBefore)
            Assert.Contains("Store", defAfter)               // it is NOW Store
            Assert.DoesNotContain("Web", defAfter)
            Assert.NotEqual<string>(defBefore, defAfter)     // the definition changed
            Assert.Equal<string>("Web", channelWebAtInsert)  // old-default row got Web
            Assert.Equal<string>("Store", channelStoreAtInsert)  // new-default row got Store
            Assert.Equal<string>("Web", channelWebAfterChange)   // the old-default row KEPT its stored value
            Assert.Equal(mintedDigestBefore, mintedDigestAfter)  // every minted row's Channel is unchanged
        }
