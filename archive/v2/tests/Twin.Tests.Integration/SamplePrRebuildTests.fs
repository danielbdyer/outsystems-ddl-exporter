module Twin.Tests.Integration.SamplePrRebuildTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the "rebuild / convert" archetype: two HEAVY operations whose
// one-line `.sql` edit expands into a much larger deploy, and whose true
// outcome under the PRODUCTION-FAITHFUL publish is DISCOVERED by running, not
// assumed. Both are proven the make-mandatory way — materialize the BEFORE
// estate with real-shaped synthetic data, apply the op's edit, publish with
// SamplePrPublish.strict (BlockOnPossibleDataLoss = true), and CONSUME the
// data to assert the objective outcome — but each may APPLY, BLOCK, or STAGE,
// so the fact captures the generated delta, records what actually happened,
// and proves phase 1 either way (a data-preserving apply, or a block whose
// scripted remedy achieves the same end state).
//
//   1. identity-swap — REMOVE IDENTITY from [dbo].[Order].[Id]
//      ([Id] INT IDENTITY(1,1) -> [Id] INT NOT NULL). A column's IDENTITY
//      property is fixed at creation: it cannot be ALTERed, so SSDT rebuilds
//      the whole table (a shadow table, a key-preserving copy, and the
//      incoming FK from OrderLine dropped and recreated around the rebuild).
//      DISCOVER: does the production gate PERFORM the data-preserving rebuild,
//      or BLOCK the data motion? Proven either way — after phase 1 Order.Id is
//      no longer IDENTITY, every Id value is preserved (digest match), and
//      every OrderLine still resolves to a real Order.
//
//   2. temporal-convert — turn the EXISTING populated [dbo].[Customer] into a
//      system-versioned (temporal) table: add the ValidFrom/ValidTo
//      GENERATED ALWAYS period columns, PERIOD FOR SYSTEM_TIME, and enable
//      SYSTEM_VERSIONING with a paired Customer_History table. Unlike
//      temporal-new (a fresh empty table), the period columns must backfill
//      every existing row. DISCOVER: does the strict publish apply the
//      conversion in place (and with what ROW START backfill?), or does it need
//      the staged add-columns-with-historical-defaults / enable-versioning
//      program? Proven either way — after phase 1 Customer is temporal_type=2
//      with two period columns and a linked history table, and the existing
//      rows' business data is byte-for-byte unchanged (digest match).
//
// Every fact is self-contained and order-independent: it restores every table
// to the canonical baseline, DROPS the twin database, and reconverges before
// applying its own edit — the database drop is what reverts a prior fact's
// applied strict edit (a plain re-mint's schema fingerprint still matches the
// baseline on disk). Evidence is flushed to scratch BEFORE the assertions so a
// surprising outcome is preserved as a finding.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrRebuildFixture () =
    inherit TwinEstateFixture ("twin-e2e-rebuild", 21844)

[<Collection("Twin-Docker")>]
type SamplePrRebuildTests (fixture: SamplePrRebuildFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op schema shapes (baseline +/- one thing) ----------------

    // identity-swap: Order with IDENTITY(1,1) removed from [Id] (the ONLY
    // change — every other column and constraint is the baseline verbatim).
    // OutSystems: "stop auto-numbering this entity's Id; I'll set Ids myself."
    let orderNoIdentity =
        """CREATE TABLE [dbo].[Order] (
    [Id]         INT           NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    // temporal-convert: the existing Customer turned system-versioned — the
    // two GENERATED ALWAYS period columns, PERIOD FOR SYSTEM_TIME, and
    // SYSTEM_VERSIONING ON with a paired Customer_History table. Every original
    // column and the FK to Status are kept verbatim. OutSystems: "start keeping
    // full history on Customer, which already has data."
    let customerTemporal =
        """CREATE TABLE [dbo].[Customer] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)  NOT NULL,
    [Email]     NVARCHAR(250)  NOT NULL,
    [StatusId]  INT            NOT NULL,
    [CreatedOn] DATETIME2      NOT NULL,
    [ValidFrom] DATETIME2      GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]   DATETIME2      GENERATED ALWAYS AS ROW END   NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]),
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Customer_History]));
"""

    /// An order-sensitive content-hash of a projection: SHA2_256 over the
    /// ORDERED FOR XML RAW rows. A byte-identical hex string proves the content
    /// is byte-identical; any changed/lost/added row shifts it. (Copied
    /// verbatim from the structural exemplar's hash idiom.)
    let hashSql (table: string) (cols: string) (orderBy: string) : string =
        sprintf
            "SELECT ISNULL(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CAST((SELECT %s FROM %s ORDER BY %s FOR XML RAW) AS NVARCHAR(MAX))), 2), 'EMPTY');"
            cols table orderBy

    interface IClassFixture<SamplePrRebuildFixture>

    // ---- helpers (mirror the structural / clean-apply exemplars) -----------

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

    /// Restore every table to baseline, drop the twin database, reconverge —
    /// the per-fact isolation primitive that makes the class order-independent.
    /// The database drop is what reverts a prior fact's applied strict edit (a
    /// plain re-mint's schema fingerprint still matches the baseline on disk).
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
    // 1) identity-swap — REMOVE IDENTITY from [dbo].[Order].[Id]. The column's
    //    IDENTITY property cannot be ALTERed, so SSDT rebuilds the whole table:
    //    a shadow table, a key-preserving copy, and the incoming FK from
    //    OrderLine dropped and recreated around it. DISCOVER whether the
    //    production gate performs the data-preserving rebuild or blocks the
    //    data motion — and prove phase 1 (Id no longer IDENTITY, every key
    //    preserved, every OrderLine still resolves) either way.
    // =====================================================================
    [<Fact>]
    member this.``identity-swap: removing IDENTITY from Order.Id is a table rebuild — the true outcome under the production gate, keys and incoming FK preserved`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-identity-swap.txt"), evidence.ToString()) with _ -> ()

            // COLUMNPROPERTY is the task-mandated identity probe: 1 = IDENTITY,
            // 0 = plain column, -1 = column absent.
            let isIdentitySql = "SELECT ISNULL(CONVERT(BIGINT, COLUMNPROPERTY(OBJECT_ID(N'dbo.Order'), N'Id', 'IsIdentity')), -1);"
            let orderRowsSql = "SELECT COUNT(*) FROM [dbo].[Order];"
            let orderLineRowsSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let maxIdSql = "SELECT ISNULL(MAX([Id]), -1) FROM [dbo].[Order];"
            // OrderLine rows whose OrderId does NOT resolve to a real Order (an
            // orphan the rebuild would introduce if it re-minted the keys).
            let orphansSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] ol LEFT JOIN [dbo].[Order] o ON o.[Id] = ol.[OrderId] WHERE o.[Id] IS NULL;"
            let fkExistsSql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order';"
            let fkTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_OrderLine_Order'), -1);"
            let identColsSql = "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'dbo.Order');"
            // the key set, and the whole row set, hashed to prove preservation.
            let idDigestSql = hashSql "[dbo].[Order]" "[Id]" "[Id]"
            let rowDigestSql = hashSql "[dbo].[Order]" "[Id],[CustomerId],[StatusId],[Channel],[Total],[PlacedOn]" "[Id]"

            // BEFORE — baseline; Order.Id is IDENTITY with real minted keys.
            let! _ = this.Fresh "identity-swap/fresh"
            let! isIdentityBefore = this.Scalar isIdentitySql
            let! orderRowsBefore = this.Scalar orderRowsSql
            let! orderLineRowsBefore = this.Scalar orderLineRowsSql
            let! maxIdBefore = this.Scalar maxIdSql
            let! orphansBefore = this.Scalar orphansSql
            let! fkExistsBefore = this.Scalar fkExistsSql
            let! identColsBefore = this.Scalar identColsSql
            let! idDigestBefore = this.ScalarStr idDigestSql
            let! rowDigestBefore = this.ScalarStr rowDigestSql
            Assert.Equal(1L, isIdentityBefore)         // Order.Id STARTS as IDENTITY
            Assert.True(orderRowsBefore > 0L, "Order must hold rows for the proof")
            Assert.Equal(0L, orphansBefore)            // baseline references all resolve
            record (sprintf "baseline: Order.Id IsIdentity=%d (1 = IDENTITY), sys.identity_columns=%d, Order rows=%d, MAX(Id)=%d, OrderLine rows=%d, OrderLine orphans=%d, FK_OrderLine_Order exists=%d" isIdentityBefore identColsBefore orderRowsBefore maxIdBefore orderLineRowsBefore orphansBefore fkExistsBefore)
            record (sprintf "  Id digest=%s; whole-row digest=%s" idDigestBefore rowDigestBefore)

            // READ THE DELTA — script (without executing) what the production
            // gate would deploy. The IDENTITY edit is one line; the generated
            // delta reveals the shadow-table rebuild + FK drop/recreate.
            fixture.Rewrite "Tables/dbo.Order.sql" orderNoIdentity
            let! scriptResult = SamplePrPublish.strictScript fixture.Root fixture.Config
            let scriptText =
                match scriptResult with
                | Ok s -> s
                | Error es -> sprintf "(deploy-script generation refused: %s)" (this.Detail es)
            record "generated strict deploy delta (IDENTITY -> non-IDENTITY on [dbo].[Order].[Id]):"
            record scriptText
            let scriptLower = scriptText.ToLowerInvariant()
            let mentionsShadow = scriptLower.Contains "tmp_ms_" || scriptLower.Contains "identity_insert"
            let mentionsFkRecreate = scriptText.Contains "FK_OrderLine_Order"
            record (sprintf "delta reveals a table rebuild: shadow-table/identity_insert markers=%b, FK_OrderLine_Order drop/recreate mentioned=%b" mentionsShadow mentionsFkRecreate)
            flush ()

            // APPLY — the production-faithful publish. DISCOVER the outcome.
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let applied = match strict with | Ok () -> true | Error _ -> false
            let strictDetail = match strict with | Ok () -> "" | Error es -> this.Detail es
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) remove IDENTITY from [dbo].[Order].[Id]: %s" (if applied then "APPLIED (Ok) — the gate performed the data-preserving rebuild" else "REFUSED (blocked) — the data motion tripped the gate; ships as a scripted rebuild"))
            if not applied then
                record "  strict refusal detail:"
                record strictDetail
            flush ()

            // If the production gate BLOCKED the declarative rebuild, this is
            // how it ships: the scripted table rebuild a real migration runs.
            // Each DDL step is its own batch so the renamed table resolves.
            // (Skipped entirely when the gate applied the rebuild itself.)
            let mutable rebuiltHow = if applied then "declarative (production publish performed the rebuild)" else ""
            if not applied then
                let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order];"
                let! _ = this.Exec "CREATE TABLE [dbo].[Order_rebuild] ([Id] INT NOT NULL, [CustomerId] INT NOT NULL, [StatusId] INT NOT NULL, [Channel] NVARCHAR(20) NOT NULL, [Total] DECIMAL(18,2) NOT NULL, [PlacedOn] DATETIME2 NOT NULL);"
                let! copied = this.Exec "INSERT INTO [dbo].[Order_rebuild] ([Id],[CustomerId],[StatusId],[Channel],[Total],[PlacedOn]) SELECT [Id],[CustomerId],[StatusId],[Channel],[Total],[PlacedOn] FROM [dbo].[Order];"
                let! _ = this.Exec "DROP TABLE [dbo].[Order];"
                let! _ = this.Exec "EXEC sp_rename N'[dbo].[Order_rebuild]', N'Order';"
                let! _ = this.Exec "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [PK_Order] PRIMARY KEY ([Id]);"
                let! _ = this.Exec "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]);"
                let! _ = this.Exec "ALTER TABLE [dbo].[Order] ADD CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]);"
                let! _ = this.Exec "ALTER TABLE [dbo].[OrderLine] ADD CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]);"
                rebuiltHow <- sprintf "scripted rebuild (shadow table + key-preserving copy of %d rows + FK drop/recreate)" copied
                record (sprintf "scripted rebuild ran (the gate blocked the declarative form): copied %d rows into the shadow table, FK_OrderLine_Order dropped and recreated" copied)
                flush ()

            // AFTER — consume the post-state (works for BOTH paths).
            let! isIdentityAfter = this.Scalar isIdentitySql
            let! orderRowsAfter = this.Scalar orderRowsSql
            let! orderLineRowsAfter = this.Scalar orderLineRowsSql
            let! maxIdAfter = this.Scalar maxIdSql
            let! orphansAfter = this.Scalar orphansSql
            let! fkExistsAfter = this.Scalar fkExistsSql
            let! fkTrustedAfter = this.Scalar fkTrustedSql
            let! identColsAfter = this.Scalar identColsSql
            let! idDigestAfter = this.ScalarStr idDigestSql
            let! rowDigestAfter = this.ScalarStr rowDigestSql
            record (sprintf "phase 1 result (%s):" rebuiltHow)
            record (sprintf "  Order.Id IsIdentity=%d (0 = plain column, no longer auto-numbered), sys.identity_columns=%d" isIdentityAfter identColsAfter)
            record (sprintf "  Order rows=%d (was %d), MAX(Id)=%d (was %d), OrderLine rows=%d (was %d)" orderRowsAfter orderRowsBefore maxIdAfter maxIdBefore orderLineRowsAfter orderLineRowsBefore)
            record (sprintf "  keys preserved: Id digest=%s (match=%b); whole-row digest=%s (match=%b)" idDigestAfter (idDigestBefore = idDigestAfter) rowDigestAfter (rowDigestBefore = rowDigestAfter))
            record (sprintf "  incoming reference intact: OrderLine orphans=%d (0 = every child still points at a real parent), FK_OrderLine_Order exists=%d (is_not_trusted=%d)" orphansAfter fkExistsAfter fkTrustedAfter)
            flush ()

            // Restore baseline on disk so the next fact converges clean.
            fixture.Rewrite "Tables/dbo.Order.sql" SamplePrBaseline.order

            // The generated delta must have shown a rebuild, not an in-place
            // alter — either the shadow-table/identity_insert markers or the
            // incoming-FK recreate (present whenever DacFx could script it).
            match scriptResult with
            | Ok _ -> Assert.True(mentionsShadow || mentionsFkRecreate, "the generated delta must reveal a table rebuild (shadow table / IDENTITY_INSERT / FK recreate), not an in-place ALTER")
            | Error _ -> ()   // the gate refused to even script it — recorded above

            // Phase 1 is proven the SAME way whichever path shipped it:
            Assert.Equal(0L, isIdentityAfter)                       // Id is no longer IDENTITY
            Assert.Equal(0L, identColsAfter)                        // and off sys.identity_columns
            Assert.Equal(orderRowsBefore, orderRowsAfter)           // every Order row survived
            Assert.Equal(orderLineRowsBefore, orderLineRowsAfter)   // every OrderLine row survived
            Assert.Equal(maxIdBefore, maxIdAfter)                   // the key range is unchanged
            Assert.Equal<string>(idDigestBefore, idDigestAfter)     // every Id VALUE preserved
            Assert.Equal<string>(rowDigestBefore, rowDigestAfter)   // every row byte-for-byte preserved
            Assert.NotEqual<string>("EMPTY", idDigestAfter)         // and not vacuously empty
            Assert.Equal(0L, orphansAfter)                          // no orphan introduced by the rebuild
            Assert.Equal(1L, fkExistsAfter)                         // the incoming FK is present again
            Assert.Equal(0L, fkTrustedAfter)                        // and trusted (recreated WITH CHECK)
        }

    // =====================================================================
    // 2) temporal-convert — turn the EXISTING populated [dbo].[Customer] into a
    //    system-versioned table. Unlike temporal-new (a fresh empty table), the
    //    ValidFrom/ValidTo period columns must backfill every existing row.
    //    DISCOVER whether the strict publish applies the conversion in place
    //    (and with what ROW START backfill), or whether it needs the staged
    //    add-period-columns-with-historical-defaults / enable-versioning
    //    program — and prove phase 1 (temporal_type=2, two period columns, a
    //    linked history table, existing rows' business data untouched) either
    //    way.
    // =====================================================================
    [<Fact>]
    member this.``temporal-convert: turning the existing populated Customer system-versioned — the true outcome under the production gate, existing rows preserved`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-temporal-convert.txt"), evidence.ToString()) with _ -> ()

            let temporalTypeSql = "SELECT ISNULL((SELECT CAST(temporal_type AS BIGINT) FROM sys.tables WHERE name = N'Customer'), -1);"
            let customerRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let periodColsSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customer') AND generated_always_type <> 0;"
            let historyExistsSql = "SELECT COUNT(*) FROM sys.tables WHERE name = N'Customer_History';"
            let historyTypeSql = "SELECT ISNULL((SELECT CAST(temporal_type AS BIGINT) FROM sys.tables WHERE name = N'Customer_History'), -1);"
            let historyLinkSql = "SELECT ISNULL((SELECT h.name FROM sys.tables t JOIN sys.tables h ON h.object_id = t.history_table_id WHERE t.name = N'Customer'), N'');"
            // business columns only (period columns are new, so excluded) —
            // proves the CONVERSION does not touch the existing rows' data.
            let bizDigestSql = hashSql "[dbo].[Customer]" "[Id],[Name],[Email],[StatusId],[CreatedOn]" "[Id]"
            // the ROW START the existing rows carry after conversion (the trap:
            // do they falsely claim to begin at conversion time?).
            let validFromMinSql = "SELECT ISNULL(CONVERT(VARCHAR(30), MIN([ValidFrom]), 121), N'') FROM [dbo].[Customer];"
            let validFromMaxSql = "SELECT ISNULL(CONVERT(VARCHAR(30), MAX([ValidFrom]), 121), N'') FROM [dbo].[Customer];"

            // BEFORE — baseline; Customer is a plain (non-temporal) populated table.
            let! _ = this.Fresh "temporal-convert/fresh"
            let! temporalBefore = this.Scalar temporalTypeSql
            let! rowsBefore = this.Scalar customerRowsSql
            let! periodColsBefore = this.Scalar periodColsSql
            let! historyBefore = this.Scalar historyExistsSql
            let! bizDigestBefore = this.ScalarStr bizDigestSql
            Assert.Equal(0L, temporalBefore)      // 0 = NON_TEMPORAL_TABLE
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(0L, periodColsBefore)    // no period columns yet
            Assert.Equal(0L, historyBefore)       // no history table yet
            record (sprintf "baseline: Customer temporal_type=%d (0 = NON_TEMPORAL), rows=%d, GENERATED-ALWAYS period columns=%d, Customer_History exists=%d" temporalBefore rowsBefore periodColsBefore historyBefore)
            record (sprintf "  business-data digest=%s" bizDigestBefore)

            // READ THE DELTA — script (without executing) the conversion the
            // production gate would deploy: ADD period columns + PERIOD, then
            // SET SYSTEM_VERSIONING ON over the live rows.
            fixture.Rewrite "Tables/dbo.Customer.sql" customerTemporal
            let! scriptResult = SamplePrPublish.strictScript fixture.Root fixture.Config
            let scriptText =
                match scriptResult with
                | Ok s -> s
                | Error es -> sprintf "(deploy-script generation refused: %s)" (this.Detail es)
            record "generated strict deploy delta (convert [dbo].[Customer] to SYSTEM_VERSIONED):"
            record scriptText
            flush ()

            // APPLY — the production-faithful publish. DISCOVER the outcome.
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            let applied = match strict with | Ok () -> true | Error _ -> false
            let strictDetail = match strict with | Ok () -> "" | Error es -> this.Detail es
            record (sprintf "production publish (BlockOnPossibleDataLoss=true) convert Customer to system-versioned: %s" (if applied then "APPLIED (Ok) — the gate performed the in-place conversion + backfill" else "REFUSED (blocked) — ships as the staged add-columns-with-defaults / enable-versioning program"))
            if not applied then
                record "  strict refusal detail:"
                record strictDetail
            flush ()

            // If the production gate BLOCKED the declarative conversion, this is
            // the staged remedy a real migration runs: add the period columns
            // with SENSIBLE HISTORICAL DEFAULTS (so existing rows do NOT falsely
            // claim to begin at conversion time), then enable versioning. ValidTo
            // must be the datetime2 max for every current row or the versioning
            // switch's consistency check fails. (Skipped when the gate applied it.)
            let mutable convertedHow = if applied then "declarative (production publish performed the conversion + backfill)" else ""
            if not applied then
                let! _ = this.Exec "ALTER TABLE [dbo].[Customer] ADD [ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL CONSTRAINT [DF_Customer_ValidFrom] DEFAULT (CONVERT(DATETIME2, '2020-01-01T00:00:00.0000000')), [ValidTo] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL CONSTRAINT [DF_Customer_ValidTo] DEFAULT (CONVERT(DATETIME2, '9999-12-31T23:59:59.9999999')), PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]);"
                let! _ = this.Exec "ALTER TABLE [dbo].[Customer] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Customer_History]));"
                convertedHow <- "staged scripted conversion (ADD period columns with historical defaults, then SET SYSTEM_VERSIONING = ON)"
                record "staged remedy ran (the gate blocked the declarative form): period columns added with ValidFrom default 2020-01-01 (a sane historical floor) and ValidTo the datetime2 max; SYSTEM_VERSIONING enabled with HISTORY_TABLE = Customer_History"
                flush ()

            // AFTER — consume the post-state (works for BOTH paths).
            let! temporalAfter = this.Scalar temporalTypeSql
            let! rowsAfter = this.Scalar customerRowsSql
            let! periodColsAfter = this.Scalar periodColsSql
            let! historyAfter = this.Scalar historyExistsSql
            let! historyType = this.Scalar historyTypeSql
            let! historyLink = this.ScalarStr historyLinkSql
            let! bizDigestAfter = this.ScalarStr bizDigestSql
            let! validFromMin = this.ScalarStr validFromMinSql
            let! validFromMax = this.ScalarStr validFromMaxSql
            record (sprintf "phase 1 result (%s):" convertedHow)
            record (sprintf "  Customer temporal_type=%d (2 = SYSTEM_VERSIONED_TEMPORAL_TABLE), rows=%d (was %d), GENERATED-ALWAYS period columns=%d" temporalAfter rowsAfter rowsBefore periodColsAfter)
            record (sprintf "  history: Customer_History exists=%d, temporal_type=%d (1 = HISTORY_TABLE), linked history table name=%s" historyAfter historyType historyLink)
            record (sprintf "  existing rows preserved: business-data digest=%s (match=%b)" bizDigestAfter (bizDigestBefore = bizDigestAfter))
            record (sprintf "  ROW START backfill on existing rows: MIN(ValidFrom)=%s, MAX(ValidFrom)=%s" validFromMin validFromMax)
            flush ()

            // Restore baseline on disk so the next fact converges clean.
            fixture.Rewrite "Tables/dbo.Customer.sql" SamplePrBaseline.customer

            // Phase 1 is proven the SAME way whichever path shipped it:
            Assert.Equal(2L, temporalAfter)                       // Customer is now system-versioned
            Assert.Equal(2L, periodColsAfter)                     // ValidFrom + ValidTo GENERATED ALWAYS
            Assert.Equal(1L, historyAfter)                        // its history table exists
            Assert.Equal(1L, historyType)                         // marked as a history table
            Assert.Equal<string>("Customer_History", historyLink) // linked as Customer's history
            Assert.Equal(rowsBefore, rowsAfter)                   // every existing row survived
            Assert.Equal<string>(bizDigestBefore, bizDigestAfter) // and its business data is untouched
            Assert.NotEqual<string>("EMPTY", bizDigestAfter)      // and not vacuously empty
            Assert.NotEqual<string>("", validFromMin)             // the ROW START backfill landed a value
        }
