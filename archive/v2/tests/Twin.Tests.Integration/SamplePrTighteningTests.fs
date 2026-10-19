module Twin.Tests.Integration.SamplePrTighteningTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the "tightening blocked by data" archetype, five operations,
// each proven the make-mandatory way: the BEFORE estate is materialized with
// real-shaped synthetic data, the op's schema edit is applied, and the
// PRODUCTION-FAITHFUL publish (SamplePrPublish.strict, BlockOnPossibleDataLoss
// = true) is the objective proof. Every fact is self-contained: it rewrites
// every table it touches back to the canonical baseline, drops the twin
// database, and reconverges before applying its edit — so the class is
// order-independent (a strict publish alters the LIVE schema without updating
// __state, which a plain re-mint cannot revert; the database drop can).
//
// The two block faces this archetype exhibits (both discovered, not assumed):
//   * row-presence (data-blind) — the tightening-class guard
//       `IF EXISTS (SELECT TOP 1 1 FROM <t>) RAISERROR(...,16,127)` above the
//       ALTER, fired by BlockOnPossibleDataLoss on any populated table
//       regardless of whether the data satisfies the new rule (narrow,
//       retype-explicit's lossy leg).
//   * value-violation — SQL Server refusing the claim against the data
//       (a new NOT NULL column with no value for existing rows; a duplicate
//       key under a unique index; a row that fails a CHECK predicate).
// ---------------------------------------------------------------------------

/// Its own container + port (isolated from every other Twin fixture).
type SamplePrTighteningFixture () =
    inherit TwinEstateFixture ("twin-e2e-tighten", 21834)

[<Collection("Twin-Docker")>]
type SamplePrTighteningTests (fixture: SamplePrTighteningFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op schema edits (baseline + one change) -----------------

    let orderLineWithMandatoryAmount =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    [Amount]   DECIMAL(18,2) NOT NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    let customerNarrowEmail (width: int) =
        SamplePrBaseline.customer.Replace("NVARCHAR(250)", sprintf "NVARCHAR(%d)" width)

    // Uniqueness renders as a SEPARATE index object (the v2 emitter's
    // `CREATE UNIQUE INDEX`). One statement per estate file — a second
    // statement appended to a table's CREATE fails the model build with
    // SQL71006 (only one statement per batch). The table file is left
    // untouched; the index rides its own `Tables/*.sql` file.
    let orderUniqueChannelIndex = "CREATE UNIQUE INDEX [UIX_Order_Channel] ON [dbo].[Order] ([Channel]);\n"
    let customerUniqueEmailIndex = "CREATE UNIQUE INDEX [UIX_Customer_Email] ON [dbo].[Customer] ([Email]);\n"
    let orderIndexRel = "Tables/dbo.Order.UniqueChannel.sql"
    let customerIndexRel = "Tables/dbo.Customer.UniqueEmail.sql"

    let orderLineQuantityAs (sqlType: string) =
        SamplePrBaseline.orderLine.Replace("[Quantity] INT ", sprintf "[Quantity] %s " sqlType)

    let orderLineWithQuantityCheck =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]),
    CONSTRAINT [CK_OrderLine_Quantity] CHECK ([Quantity] > 0)
);
"""

    interface IClassFixture<SamplePrTighteningFixture>

    // ---- helpers (mirrors the make-mandatory exemplar) -------------------

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

    /// Converge (relaxed) and demand success. `force` re-mints even when the
    /// fingerprints match.
    member private this.Converge (label: string) (force: bool) : Task<Runs.MaterializeReport> =
        task {
            let! outcome =
                if force then Runs.seed fixture.Root fixture.Config TwinConfig.BaselineScenario
                else this.Up()
            match outcome with
            | Ok (Runs.Materialized r) -> return r
            | Ok (Runs.NothingToApply _) -> return failwithf "%s: expected Materialized, got NothingToApply" label
            | Error es -> return failwithf "%s: up refused: %A" label (es |> List.map (fun e -> e.Code, e.Message))
        }

    /// Delete an estate file if present (used to retract an added index file).
    member private _.DeleteTableFile (rel: string) : unit =
        try System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)))
        with _ -> ()

    /// Restore every table to baseline, drop the twin database, reconverge —
    /// the per-fact isolation primitive that makes the class order-independent.
    /// Any stray `Tables/*.sql` a prior fact added (e.g. a unique-index file)
    /// is removed first so it cannot bleed into this fact's estate.
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
            return! this.Converge label false
        }

    // =====================================================================
    // 1) add-mandatory — a NEW NOT NULL column with no default.
    // =====================================================================
    [<Fact>]
    member this.``add-mandatory: a new NOT NULL column with no default blocks a populated table, applies when empty`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-add-mandatory.txt"), evidence.ToString()) with _ -> ()

            let colExistsSql = "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'OrderLine' AND c.name = N'Amount';"
            let isNullableSql = "SELECT ISNULL((SELECT CAST(c.is_nullable AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'OrderLine' AND c.name = N'Amount'), -1);"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"

            // BEFORE — populated OrderLine, no Amount column.
            let! baseline = this.Fresh "add-mandatory/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! colBefore = this.Scalar colExistsSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(0L, colBefore)   // Amount does not exist yet
            record (sprintf "baseline: OrderLine rows=%d, Amount column exists=%d, twin totalRows=%d" rowsBefore colBefore baseline.TotalRows)

            // PRIMARY — production publish REFUSES the NOT NULL add on the
            // populated table (no value for the existing rows).
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineWithMandatoryAmount
            let! strictBlocked = SamplePrPublish.strict fixture.Root fixture.Config
            let strictDetail =
                match strictBlocked with
                | Ok () -> failwith "PROOF FAILED: production publish accepted a NOT NULL column with no default on a populated table"
                | Error es -> this.Detail es
            let! colAfterBlock = this.Scalar colExistsSql
            let! rowsAfterBlock = this.Scalar rowCountSql
            record "PRIMARY production publish (BlockOnPossibleDataLoss=true): REFUSED the new NOT NULL column on a populated table."
            record (sprintf "  after block: OrderLine rows=%d (intact), Amount column exists=%d (not added)" rowsAfterBlock colAfterBlock)
            record "  strict detail:"
            record strictDetail
            flush ()

            Assert.Contains("twin.publish.failed", (match strictBlocked with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Equal(0L, colAfterBlock)          // column not added
            Assert.Equal(rowsBefore, rowsAfterBlock) // rows intact
            // The warning names the cause (a new NOT NULL column with no
            // default); the block itself is the data-loss row-presence guard.
            Assert.Contains("SQL72015", strictDetail)
            Assert.Contains("no default value and does not allow NULL values", strictDetail)
            Assert.Contains("Msg 50000", strictDetail)
            Assert.Contains("Rows were detected", strictDetail)

            // CONTRAST — the identical edit on an EMPTY table applies clean.
            let! _ = this.Exec "DELETE FROM [dbo].[OrderLine];"
            let! rowsEmpty = this.Scalar rowCountSql
            Assert.Equal(0L, rowsEmpty)
            let! strictEmpty = SamplePrPublish.strict fixture.Root fixture.Config
            match strictEmpty with
            | Ok () -> ()
            | Error es -> failwithf "empty-table production publish unexpectedly refused: %s" (this.Detail es)
            let! colEmpty = this.Scalar colExistsSql
            let! nullableEmpty = this.Scalar isNullableSql
            record (sprintf "CONTRAST empty-table production publish APPLIED: Amount column exists=%d, is_nullable=%d (mandatory), rows=%d" colEmpty nullableEmpty rowsEmpty)
            flush ()
            Assert.Equal(1L, colEmpty)      // column now exists
            Assert.Equal(0L, nullableEmpty) // and is NOT NULL
        }

    // =====================================================================
    // 2) narrow — shrink Customer.Email below the data it holds.
    // =====================================================================
    [<Fact>]
    member this.``narrow: over-length data and the row-presence guard block a populated column, applies when empty`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-narrow.txt"), evidence.ToString()) with _ -> ()

            let maxLenSql = "SELECT ISNULL(MAX(LEN([Email])), 0) FROM [dbo].[Customer];"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let maxLengthSql = "SELECT CAST(c.max_length AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Customer' AND c.name = N'Email';"

            // BEFORE — populated Customer, Email NVARCHAR(250).
            let! _ = this.Fresh "narrow/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! maxLenBefore = this.Scalar maxLenSql
            let! maxLengthBefore = this.Scalar maxLengthSql
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(500L, maxLengthBefore)   // NVARCHAR(250) => 500 bytes
            Assert.True(maxLenBefore >= 4L, "emails must be long enough to narrow below")
            let narrowWidth = int (max 1L (maxLenBefore - 3L))
            let overLenSql = sprintf "SELECT COUNT(*) FROM [dbo].[Customer] WHERE LEN([Email]) > %d;" narrowWidth
            let! overLen = this.Scalar overLenSql
            Assert.True(overLen > 0L, "some emails must exceed the narrow width")
            record (sprintf "baseline: Customer rows=%d, MAX(LEN(Email))=%d, Email max_length=%d bytes (NVARCHAR 250)" rowsBefore maxLenBefore maxLengthBefore)
            record (sprintf "chosen narrow width = NVARCHAR(%d); rows with LEN(Email) > %d = %d" narrowWidth narrowWidth overLen)

            // SECONDARY — the Twin's relaxed publish (BlockOnPossibleDataLoss
            // = false) attempts the ALTER and hits the data itself.
            fixture.Rewrite "Tables/dbo.Customer.sql" (customerNarrowEmail narrowWidth)
            let! relaxed = this.Up()
            let relaxedDetail =
                match relaxed with
                | Ok _ -> "RELAXED PUBLISH APPLIED (no data-loss error surfaced)"
                | Error es -> this.Detail es
            let! maxLengthRelaxed = this.Scalar maxLengthSql
            record (sprintf "SECONDARY relaxed Runs.up (BlockOnPossibleDataLoss=false) narrowing to NVARCHAR(%d): %s" narrowWidth (match relaxed with Ok _ -> "APPLIED" | Error _ -> "REFUSED"))
            record (sprintf "  Email max_length after = %d bytes" maxLengthRelaxed)
            record "  relaxed detail:"
            record relaxedDetail
            flush ()
            // The relaxed publish runs the ALTER and SQL Server itself refuses
            // the truncation (the data-driven face).
            Assert.True((match relaxed with Error _ -> true | Ok _ -> false), "relaxed narrow must be refused by truncation")
            Assert.Contains("Msg 2628", relaxedDetail)
            Assert.Contains("String or binary data would be truncated", relaxedDetail)
            Assert.Equal(500L, maxLengthRelaxed)   // rolled back

            // reset to a clean populated Customer at NVARCHAR(250).
            fixture.Rewrite "Tables/dbo.Customer.sql" SamplePrBaseline.customer
            let! _ = this.Converge "narrow/reset" true
            let! maxLengthReset = this.Scalar maxLengthSql
            Assert.Equal(500L, maxLengthReset)

            // PRIMARY — production publish REFUSES the narrow on the populated
            // table (data-blind row-presence guard).
            fixture.Rewrite "Tables/dbo.Customer.sql" (customerNarrowEmail narrowWidth)
            let! strictBlocked = SamplePrPublish.strict fixture.Root fixture.Config
            let strictDetail =
                match strictBlocked with
                | Ok () -> failwith "PROOF FAILED: production publish accepted a narrowing over a populated table"
                | Error es -> this.Detail es
            let! maxLengthAfterBlock = this.Scalar maxLengthSql
            let! rowsAfterBlock = this.Scalar rowCountSql
            record (sprintf "PRIMARY production publish (BlockOnPossibleDataLoss=true) narrowing to NVARCHAR(%d): REFUSED on the populated table." narrowWidth)
            record (sprintf "  after block: Customer rows=%d (intact), Email max_length=%d bytes (unchanged)" rowsAfterBlock maxLengthAfterBlock)
            record "  strict detail:"
            record strictDetail
            flush ()
            Assert.Contains("twin.publish.failed", (match strictBlocked with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Equal(500L, maxLengthAfterBlock)  // unchanged
            Assert.Equal(rowsBefore, rowsAfterBlock) // rows intact
            // Same data-blind row-presence guard as make-mandatory, not Msg 2628.
            Assert.Contains("Msg 50000", strictDetail)
            Assert.Contains("Rows were detected", strictDetail)

            // CONTRAST — empty the table (child-first) and the narrow applies.
            let! _ = this.Exec "DELETE FROM [dbo].[OrderLine];"
            let! _ = this.Exec "DELETE FROM [dbo].[Order];"
            let! _ = this.Exec "DELETE FROM [dbo].[Customer];"
            let! rowsEmpty = this.Scalar rowCountSql
            Assert.Equal(0L, rowsEmpty)
            let! strictEmpty = SamplePrPublish.strict fixture.Root fixture.Config
            match strictEmpty with
            | Ok () -> ()
            | Error es -> failwithf "empty-table narrow unexpectedly refused: %s" (this.Detail es)
            let! maxLengthEmpty = this.Scalar maxLengthSql
            record (sprintf "CONTRAST empty-table production publish APPLIED: Email max_length=%d bytes (NVARCHAR %d), rows=%d" maxLengthEmpty narrowWidth rowsEmpty)
            flush ()
            Assert.Equal(int64 (narrowWidth * 2), maxLengthEmpty)
        }

    // =====================================================================
    // 3) add-unique — a UNIQUE index over duplicate values.
    // =====================================================================
    [<Fact>]
    member this.``add-unique: duplicate values block the unique index, a unique column applies`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-add-unique.txt"), evidence.ToString()) with _ -> ()

            let channelCountSql = "SELECT COUNT(*) FROM [dbo].[Order];"
            let channelDistinctSql = "SELECT COUNT(DISTINCT [Channel]) FROM [dbo].[Order];"
            let channelDupePairsSql = "SELECT ISNULL(SUM(n),0) FROM (SELECT COUNT(*) AS n FROM [dbo].[Order] GROUP BY [Channel] HAVING COUNT(*) > 1) d;"
            let orderIndexSql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Order') AND name = N'UIX_Order_Channel';"
            let emailCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let emailDistinctSql = "SELECT COUNT(DISTINCT [Email]) FROM [dbo].[Customer];"
            let emailIndexUniqueSql = "SELECT ISNULL((SELECT CAST(is_unique AS BIGINT) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'UIX_Customer_Email'), -1);"

            // BEFORE — seed a deterministic duplicate Channel (CON-02 shape).
            let! _ = this.Fresh "add-unique/fresh"
            let! _ = this.Exec "UPDATE [dbo].[Order] SET [Channel] = N'DUPE' WHERE [Id] IN (SELECT TOP 2 [Id] FROM [dbo].[Order] ORDER BY [Id]);"
            let! chanTotal = this.Scalar channelCountSql
            let! chanDistinct = this.Scalar channelDistinctSql
            let! chanDupeRows = this.Scalar channelDupePairsSql
            Assert.True(chanTotal > chanDistinct, "Channel must hold duplicates for the proof")
            record (sprintf "baseline: Order rows=%d, DISTINCT Channel=%d, rows in duplicate groups=%d" chanTotal chanDistinct chanDupeRows)

            // PRIMARY — the unique index build is REFUSED by the duplicate.
            // The index is its own estate file (one statement per file); the
            // Order table definition is untouched.
            fixture.Rewrite orderIndexRel orderUniqueChannelIndex
            let! strictBlocked = SamplePrPublish.strict fixture.Root fixture.Config
            let strictDetail =
                match strictBlocked with
                | Ok () -> failwith "PROOF FAILED: the unique index built over duplicate Channel values"
                | Error es -> this.Detail es
            let! indexAfterBlock = this.Scalar orderIndexSql
            let! rowsAfterBlock = this.Scalar channelCountSql
            record "PRIMARY production publish (CREATE UNIQUE INDEX over duplicates): REFUSED."
            record (sprintf "  after block: Order rows=%d (intact), UIX_Order_Channel exists=%d (not built)" rowsAfterBlock indexAfterBlock)
            record "  strict detail:"
            record strictDetail
            flush ()
            Assert.Contains("twin.publish.failed", (match strictBlocked with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Equal(0L, indexAfterBlock)   // index not built
            Assert.Equal(chanTotal, rowsAfterBlock)
            // The value-violation face: SQL Server refuses the index build.
            Assert.Contains("Msg 1505", strictDetail)
            Assert.Contains("duplicate key", strictDetail)

            // retract the index file and reset to a clean re-minted estate.
            this.DeleteTableFile orderIndexRel
            let! _ = this.Converge "add-unique/reset" true

            // CONTRAST — a genuinely unique column applies clean. Guarantee
            // Customer.Email uniqueness deterministically first (probe records
            // the natural cardinality before any normalization).
            let! emailTotal = this.Scalar emailCountSql
            let! emailDistinctNatural = this.Scalar emailDistinctSql
            if emailDistinctNatural <> emailTotal then
                let! _ = this.Exec "UPDATE [dbo].[Customer] SET [Email] = CONCAT(N'unique', CAST([Id] AS NVARCHAR(12)), N'@example.test');"
                ()
            let! emailDistinctFinal = this.Scalar emailDistinctSql
            Assert.Equal(emailTotal, emailDistinctFinal)   // Email is now unique
            record (sprintf "CONTRAST setup: Customer rows=%d, DISTINCT Email natural=%d, final=%d (unique)" emailTotal emailDistinctNatural emailDistinctFinal)
            fixture.Rewrite customerIndexRel customerUniqueEmailIndex
            let! strictClean = SamplePrPublish.strict fixture.Root fixture.Config
            match strictClean with
            | Ok () -> ()
            | Error es -> failwithf "unique index over unique Email unexpectedly refused: %s" (this.Detail es)
            let! emailIndexUnique = this.Scalar emailIndexUniqueSql
            record (sprintf "CONTRAST production publish (UNIQUE INDEX over unique Email): APPLIED, is_unique=%d" emailIndexUnique)
            flush ()
            this.DeleteTableFile customerIndexRel   // cleanup so it cannot bleed
            Assert.Equal(1L, emailIndexUnique)   // unique index enforced
        }

    // =====================================================================
    // 4) retype-explicit — a lossy narrowing type change.
    // =====================================================================
    [<Fact>]
    member this.``retype-explicit: a lossy narrowing type change blocks a populated column, a widening retype applies`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-retype-explicit.txt"), evidence.ToString()) with _ -> ()

            let typeSql = "SELECT ty.name FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'OrderLine' AND c.name = N'Quantity';"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let maxQtySql = "SELECT ISNULL(MAX([Quantity]), 0) FROM [dbo].[OrderLine];"
            let overTinySql = "SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [Quantity] > 255;"

            // BEFORE — seed a Quantity that cannot fit TINYINT (0..255).
            let! _ = this.Fresh "retype/fresh"
            let! _ = this.Exec "UPDATE [dbo].[OrderLine] SET [Quantity] = 1000 WHERE [Id] = (SELECT MIN([Id]) FROM [dbo].[OrderLine]);"
            let! rowsBefore = this.Scalar rowCountSql
            let! typeBefore = this.ScalarStr typeSql
            let! maxQty = this.Scalar maxQtySql
            let! overTiny = this.Scalar overTinySql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal("int", typeBefore)
            Assert.True(overTiny > 0L, "a Quantity must exceed TINYINT's range")
            record (sprintf "baseline: OrderLine rows=%d, Quantity type=%s, MAX(Quantity)=%d, rows with Quantity>255=%d" rowsBefore typeBefore maxQty overTiny)

            // SECONDARY — the relaxed publish attempts the INT->TINYINT ALTER
            // and hits the out-of-range value.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" (orderLineQuantityAs "TINYINT")
            let! relaxed = this.Up()
            let relaxedDetail =
                match relaxed with
                | Ok _ -> "RELAXED PUBLISH APPLIED (no overflow surfaced)"
                | Error es -> this.Detail es
            record (sprintf "SECONDARY relaxed Runs.up (BlockOnPossibleDataLoss=false) INT->TINYINT: %s" (match relaxed with Ok _ -> "APPLIED" | Error _ -> "REFUSED"))
            record "  relaxed detail:"
            record relaxedDetail
            flush ()
            // The relaxed publish runs the ALTER and SQL Server refuses the
            // out-of-range value (the data-driven face).
            Assert.True((match relaxed with Error _ -> true | Ok _ -> false), "relaxed retype must be refused by overflow")
            Assert.Contains("Msg 220", relaxedDetail)
            Assert.Contains("Arithmetic overflow error for data type tinyint", relaxedDetail)

            // reset a clean populated INT Quantity.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" SamplePrBaseline.orderLine
            let! _ = this.Converge "retype/reset" true
            let! typeReset = this.ScalarStr typeSql
            Assert.Equal("int", typeReset)

            // PRIMARY — production publish REFUSES the lossy narrowing over the
            // populated table (data-blind row-presence guard).
            fixture.Rewrite "Tables/dbo.OrderLine.sql" (orderLineQuantityAs "TINYINT")
            let! strictBlocked = SamplePrPublish.strict fixture.Root fixture.Config
            let strictDetail =
                match strictBlocked with
                | Ok () -> failwith "PROOF FAILED: production publish accepted a lossy INT->TINYINT over a populated table"
                | Error es -> this.Detail es
            let! typeAfterBlock = this.ScalarStr typeSql
            let! rowsAfterBlock = this.Scalar rowCountSql
            record "PRIMARY production publish (BlockOnPossibleDataLoss=true) INT->TINYINT: REFUSED on the populated table."
            record (sprintf "  after block: OrderLine rows=%d (intact), Quantity type=%s (unchanged)" rowsAfterBlock typeAfterBlock)
            record "  strict detail:"
            record strictDetail
            flush ()
            Assert.Contains("twin.publish.failed", (match strictBlocked with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Equal("int", typeAfterBlock)      // unchanged
            Assert.Equal(rowsBefore, rowsAfterBlock) // rows intact
            // Data-blind row-presence guard, not the Msg 220 overflow.
            Assert.Contains("Msg 50000", strictDetail)
            Assert.Contains("Rows were detected", strictDetail)

            // CONTRAST — a widening retype (the retype-implicit direction)
            // applies clean on the same populated table.
            fixture.Rewrite "Tables/dbo.OrderLine.sql" (orderLineQuantityAs "BIGINT")
            let! strictWiden = SamplePrPublish.strict fixture.Root fixture.Config
            match strictWiden with
            | Ok () -> ()
            | Error es -> failwithf "widening INT->BIGINT unexpectedly refused: %s" (this.Detail es)
            let! typeWiden = this.ScalarStr typeSql
            let! rowsWiden = this.Scalar rowCountSql
            record (sprintf "CONTRAST production publish (widening INT->BIGINT): APPLIED on the populated table, Quantity type=%s, rows=%d (intact)" typeWiden rowsWiden)
            flush ()
            Assert.Equal("bigint", typeWiden)
            Assert.Equal(rowsBefore, rowsWiden)
        }

    // =====================================================================
    // 5) add-check — a CHECK constraint, both legs in one proof.
    // =====================================================================
    [<Fact>]
    member this.``add-check: a violating row blocks the check, conforming data applies`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-add-check.txt"), evidence.ToString()) with _ -> ()

            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let violatorsSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE NOT ([Quantity] > 0);"
            let checkExistsSql = "SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_OrderLine_Quantity';"
            let notTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.check_constraints WHERE name = N'CK_OrderLine_Quantity'), -1);"

            // ---- LEG A (violating) — seed a row that breaks Quantity > 0 ----
            let! _ = this.Fresh "add-check/fresh-a"
            let! _ = this.Exec "UPDATE [dbo].[OrderLine] SET [Quantity] = 0 WHERE [Id] = (SELECT MIN([Id]) FROM [dbo].[OrderLine]);"
            let! rowsA = this.Scalar rowCountSql
            let! violatorsA = this.Scalar violatorsSql
            Assert.True(rowsA > 0L)
            Assert.True(violatorsA > 0L, "a row must violate Quantity > 0")
            record (sprintf "LEG A baseline: OrderLine rows=%d, rows violating (Quantity > 0)=%d" rowsA violatorsA)

            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineWithQuantityCheck
            let! strictBlocked = SamplePrPublish.strict fixture.Root fixture.Config
            let blockDetail =
                match strictBlocked with
                | Ok () -> failwith "PROOF FAILED: the CHECK was validated-and-trusted while a row violated it"
                | Error es -> this.Detail es
            // The TRUE post-block state: SSDT emits the constraint WITH NOCHECK,
            // then a separate `WITH CHECK CHECK` re-validation which fails on the
            // violating row (Msg 547). The publish is REFUSED, but the untrusted
            // constraint can LINGER — so the objective proof is "the check did
            // not land trusted/enforced", i.e. is_not_trusted <> 0.
            let! checkAfterBlock = this.Scalar checkExistsSql
            let! notTrustedAfterBlock = this.Scalar notTrustedSql
            record "LEG A production publish (WITH CHECK over a violating row): REFUSED at the WITH CHECK CHECK re-validation (Msg 547)."
            record (sprintf "  after block: CK_OrderLine_Quantity exists=%d, is_not_trusted=%d (added WITH NOCHECK, never validated -> untrusted)" checkAfterBlock notTrustedAfterBlock)
            record "  strict detail:"
            record blockDetail
            flush ()
            Assert.Contains("twin.publish.failed", (match strictBlocked with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Contains("Msg 547", blockDetail)                 // the validation conflict
            Assert.NotEqual(0L, notTrustedAfterBlock)               // NOT trusted-and-enforced

            // ---- LEG B (satisfying) — data already conforms ----
            let! _ = this.Fresh "add-check/fresh-b"
            let! _ = this.Exec "UPDATE [dbo].[OrderLine] SET [Quantity] = 1 WHERE [Quantity] <= 0;"  // guarantee all conform
            let! violatorsB = this.Scalar violatorsSql
            Assert.Equal(0L, violatorsB)
            record (sprintf "LEG B baseline: rows violating (Quantity > 0)=%d (all conform)" violatorsB)
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineWithQuantityCheck
            let! strictClean = SamplePrPublish.strict fixture.Root fixture.Config
            match strictClean with
            | Ok () -> ()
            | Error es -> failwithf "CHECK over conforming data unexpectedly refused: %s" (this.Detail es)
            let! checkExists = this.Scalar checkExistsSql
            let! notTrusted = this.Scalar notTrustedSql
            record (sprintf "LEG B production publish (WITH CHECK over conforming data): APPLIED, CK_OrderLine_Quantity exists=%d, is_not_trusted=%d" checkExists notTrusted)
            flush ()
            Assert.Equal(1L, checkExists)     // constraint added
            Assert.Equal(0L, notTrusted)      // and trusted
        }
