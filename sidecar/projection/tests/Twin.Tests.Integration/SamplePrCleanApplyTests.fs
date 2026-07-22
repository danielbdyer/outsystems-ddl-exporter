module Twin.Tests.Integration.SamplePrCleanApplyTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the "clean additive apply" archetype, six operations, each
// proven the make-mandatory way but from the SAFE side: the BEFORE estate is
// materialized with real-shaped synthetic data, the op's additive edit is
// applied, and the PRODUCTION-FAITHFUL publish (SamplePrPublish.strict,
// BlockOnPossibleDataLoss = true — the deployment a real environment runs) is
// the objective proof. The point of this archetype is the OPPOSITE of the
// tightening class: the strict publish returns `Ok ()` on a POPULATED table.
// Each op is safe in production, so it applies clean, and the proof is:
//   1. strict publish returns Ok () under BlockOnPossibleDataLoss = true;
//   2. the change is LIVE (sys.columns / sys.indexes / sys.default_constraints
//      / sys.types show it);
//   3. the existing rows are intact (rowcount stable; for widen and
//      retype-implicit, every stored value is preserved — a digest and a
//      single-row probe are compared before/after).
//
// Every fact is self-contained and order-independent: it rewrites every table
// it touches back to the canonical baseline, removes any stray added file,
// DROPS the twin database, and reconverges before applying its edit. The drop
// is essential here precisely because a strict publish that APPLIES alters the
// LIVE schema without updating the Twin's `__state` fingerprint — a plain
// re-mint (whose schema fingerprint still matches the baseline on disk) would
// leave the prior fact's applied column/index in the live database; only a
// database drop reverts it.
// ---------------------------------------------------------------------------

/// Its own container + port (isolated from every other Twin fixture).
type SamplePrCleanApplyFixture () =
    inherit TwinEstateFixture ("twin-e2e-clean", 21836)

[<Collection("Twin-Docker")>]
type SamplePrCleanApplyTests (fixture: SamplePrCleanApplyFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op additive schema edits (baseline + one change) --------

    // add-optional: a new NULL column on Customer.
    let customerWithMiddleName =
        """CREATE TABLE [dbo].[Customer] (
    [Id]         INT            IDENTITY(1,1) NOT NULL,
    [Name]       NVARCHAR(100)  NOT NULL,
    [Email]      NVARCHAR(250)  NOT NULL,
    [MiddleName] NVARCHAR(100)  NULL,
    [StatusId]   INT            NOT NULL,
    [CreatedOn]  DATETIME2      NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    // add-default: a new NOT NULL column WITH a named default on Customer.
    // The default backfills the existing rows as the column is added, so the
    // populated table applies clean (the safe counterpart to add-mandatory).
    let customerWithIsActiveDefault =
        """CREATE TABLE [dbo].[Customer] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)  NOT NULL,
    [Email]     NVARCHAR(250)  NOT NULL,
    [StatusId]  INT            NOT NULL,
    [CreatedOn] DATETIME2      NOT NULL,
    [IsActive]  BIT            NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT ((1)),
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    // add-index: a NON-unique index rides its OWN one-statement estate file
    // (a CREATE INDEX appended to the table's CREATE fails the model build
    // with SQL71006 — one statement per batch). The Customer table file is
    // left untouched; the index file is retracted at the fact's end.
    let customerEmailIndex = "CREATE NONCLUSTERED INDEX [IX_Customer_Email] ON [dbo].[Customer] ([Email]);\n"
    let customerIndexRel = "Tables/dbo.Customer.IX_Email.sql"

    // widen: Customer.Email NVARCHAR(250) -> NVARCHAR(400).
    let customerWiderEmail = SamplePrBaseline.customer.Replace("NVARCHAR(250)", "NVARCHAR(400)")

    // make-optional: OrderLine.Sku NOT NULL -> NULL.
    let orderLineSkuOptional =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    // retype-implicit: OrderLine.Quantity INT -> BIGINT (a widening cast).
    let orderLineQuantityBigint = SamplePrBaseline.orderLine.Replace("[Quantity] INT ", "[Quantity] BIGINT ")

    interface IClassFixture<SamplePrCleanApplyFixture>

    // ---- helpers (mirror the make-mandatory / tightening exemplars) ------

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

    /// Delete an estate file if present (retract an added index file).
    member private _.DeleteTableFile (rel: string) : unit =
        try System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)))
        with _ -> ()

    /// Restore every table to baseline, remove any stray added file, drop the
    /// twin database, reconverge — the per-fact isolation primitive that makes
    /// the class order-independent. The database drop is what reverts a prior
    /// fact's cleanly-APPLIED strict edit (which a plain re-mint cannot, since
    /// its schema fingerprint still matches the baseline on disk).
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
    // 1) add-optional — a new NULL column applies clean on a populated table.
    // =====================================================================
    [<Fact>]
    member this.``add-optional: a new nullable column applies clean on a populated table`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-add-optional.txt"), evidence.ToString()) with _ -> ()

            let colExistsSql = "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Customer' AND c.name = N'MiddleName';"
            let isNullableSql = "SELECT ISNULL((SELECT CAST(c.is_nullable AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Customer' AND c.name = N'MiddleName'), -1);"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"

            // BEFORE — populated Customer, no MiddleName column.
            let! _ = this.Fresh "add-optional/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! colBefore = this.Scalar colExistsSql
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(0L, colBefore)   // MiddleName does not exist yet
            record (sprintf "baseline: Customer rows=%d, MiddleName column exists=%d" rowsBefore colBefore)

            // APPLY — production-faithful publish of the new NULL column.
            fixture.Rewrite "Tables/dbo.Customer.sql" customerWithMiddleName
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: add-optional refused by production publish: %s" (this.Detail es)
            let! colAfter = this.Scalar colExistsSql
            let! nullableAfter = this.Scalar isNullableSql
            let! rowsAfter = this.Scalar rowCountSql
            record "production publish (BlockOnPossibleDataLoss=true) ADD [MiddleName] NVARCHAR(100) NULL: APPLIED (Ok)."
            record (sprintf "  after apply: MiddleName column exists=%d, is_nullable=%d, Customer rows=%d (intact)" colAfter nullableAfter rowsAfter)
            flush ()

            Assert.Equal(1L, colAfter)        // column now exists
            Assert.Equal(1L, nullableAfter)   // and is nullable
            Assert.Equal(rowsBefore, rowsAfter) // rows intact
        }

    // =====================================================================
    // 2) add-default — a NOT NULL column WITH a default applies clean and
    //    backfills existing rows (the safe counterpart to add-mandatory).
    // =====================================================================
    [<Fact>]
    member this.``add-default: a NOT NULL column with a default applies clean and backfills existing rows`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-add-default.txt"), evidence.ToString()) with _ -> ()

            let colExistsSql = "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Customer' AND c.name = N'IsActive';"
            let isNullableSql = "SELECT ISNULL((SELECT CAST(c.is_nullable AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Customer' AND c.name = N'IsActive'), -1);"
            let defaultExistsSql = "SELECT COUNT(*) FROM sys.default_constraints WHERE name = N'DF_Customer_IsActive';"
            let defaultDefSql = "SELECT ISNULL((SELECT definition FROM sys.default_constraints WHERE name = N'DF_Customer_IsActive'), N'');"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let activeRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer] WHERE [IsActive] = 1;"
            let nonDefaultRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer] WHERE [IsActive] <> 1;"

            // BEFORE — populated Customer, no IsActive column.
            let! _ = this.Fresh "add-default/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! colBefore = this.Scalar colExistsSql
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(0L, colBefore)   // IsActive does not exist yet
            record (sprintf "baseline: Customer rows=%d, IsActive column exists=%d" rowsBefore colBefore)

            // APPLY — production-faithful publish of the NOT NULL + DEFAULT column.
            // add-mandatory (NOT NULL, no default) BLOCKS a populated table; the
            // default backfills existing rows, so this applies clean.
            fixture.Rewrite "Tables/dbo.Customer.sql" customerWithIsActiveDefault
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: add-default refused by production publish: %s" (this.Detail es)
            let! colAfter = this.Scalar colExistsSql
            let! nullableAfter = this.Scalar isNullableSql
            let! defaultExists = this.Scalar defaultExistsSql
            let! defaultDef = this.ScalarStr defaultDefSql
            let! rowsAfter = this.Scalar rowCountSql
            let! activeRows = this.Scalar activeRowsSql
            let! nonDefaultRows = this.Scalar nonDefaultRowsSql
            record "production publish (BlockOnPossibleDataLoss=true) ADD [IsActive] BIT NOT NULL DEFAULT ((1)): APPLIED (Ok) on the populated table."
            record (sprintf "  after apply: IsActive exists=%d, is_nullable=%d (mandatory), DF_Customer_IsActive exists=%d, definition=%s" colAfter nullableAfter defaultExists defaultDef)
            record (sprintf "  backfill: Customer rows=%d (intact), rows with IsActive=1 = %d, rows NOT carrying the default = %d" rowsAfter activeRows nonDefaultRows)
            flush ()

            Assert.Equal(1L, colAfter)             // column now exists
            Assert.Equal(0L, nullableAfter)        // and is NOT NULL (mandatory)
            Assert.Equal(1L, defaultExists)        // named default constraint present
            Assert.Contains("1", defaultDef)       // default is (1)
            Assert.Equal(rowsBefore, rowsAfter)    // rows intact
            Assert.Equal(rowsBefore, activeRows)   // EVERY existing row backfilled to the default
            Assert.Equal(0L, nonDefaultRows)       // no row escaped the default
        }

    // =====================================================================
    // 3) add-index — a non-unique index builds clean over a populated table.
    // =====================================================================
    [<Fact>]
    member this.``add-index: a non-unique index builds clean over a populated table`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-add-index.txt"), evidence.ToString()) with _ -> ()

            let indexExistsSql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email';"
            let isUniqueSql = "SELECT ISNULL((SELECT CAST(is_unique AS BIGINT) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email'), -1);"
            let isDisabledSql = "SELECT ISNULL((SELECT CAST(is_disabled AS BIGINT) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email'), -1);"
            let typeDescSql = "SELECT ISNULL((SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Email'), N'');"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"

            // BEFORE — populated Customer, no IX_Customer_Email index.
            let! _ = this.Fresh "add-index/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! indexBefore = this.Scalar indexExistsSql
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(0L, indexBefore)   // index does not exist yet
            record (sprintf "baseline: Customer rows=%d, IX_Customer_Email exists=%d" rowsBefore indexBefore)

            // APPLY — the index rides its own one-statement estate file; the
            // Customer table definition is left untouched.
            fixture.Rewrite customerIndexRel customerEmailIndex
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: add-index refused by production publish: %s" (this.Detail es)
            let! indexAfter = this.Scalar indexExistsSql
            let! isUnique = this.Scalar isUniqueSql
            let! isDisabled = this.Scalar isDisabledSql
            let! typeDesc = this.ScalarStr typeDescSql
            let! rowsAfter = this.Scalar rowCountSql
            record "production publish (BlockOnPossibleDataLoss=true) CREATE NONCLUSTERED INDEX [IX_Customer_Email]: APPLIED (Ok)."
            record (sprintf "  after apply: IX_Customer_Email exists=%d, type_desc=%s, is_unique=%d (non-unique), is_disabled=%d, Customer rows=%d (intact)" indexAfter typeDesc isUnique isDisabled rowsAfter)
            flush ()

            this.DeleteTableFile customerIndexRel   // cleanup so the extra file cannot bleed

            Assert.Equal(1L, indexAfter)          // index built
            Assert.Equal(0L, isUnique)            // non-unique
            Assert.Equal(0L, isDisabled)          // enabled
            Assert.Equal("NONCLUSTERED", typeDesc)
            Assert.Equal(rowsBefore, rowsAfter)   // rows intact
        }

    // =====================================================================
    // 4) widen — enlarging a column applies clean and preserves every value.
    // =====================================================================
    [<Fact>]
    member this.``widen: enlarging a column applies clean and preserves every value`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-widen.txt"), evidence.ToString()) with _ -> ()

            let maxLengthSql = "SELECT ISNULL((SELECT CAST(c.max_length AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'Customer' AND c.name = N'Email'), -1);"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let digestSql = "SELECT ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM([Email])) AS BIGINT), 0) FROM [dbo].[Customer];"
            let maxLenSql = "SELECT ISNULL(MAX(LEN([Email])), 0) FROM [dbo].[Customer];"
            let probeSql = "SELECT ISNULL((SELECT [Email] FROM [dbo].[Customer] WHERE [Id] = (SELECT MIN([Id]) FROM [dbo].[Customer])), N'');"

            // BEFORE — populated Customer, Email NVARCHAR(250) = 500 bytes.
            let! _ = this.Fresh "widen/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! maxLengthBefore = this.Scalar maxLengthSql
            let! digestBefore = this.Scalar digestSql
            let! maxLenBefore = this.Scalar maxLenSql
            let! probeBefore = this.ScalarStr probeSql
            Assert.True(rowsBefore > 0L, "Customer must hold rows for the proof")
            Assert.Equal(500L, maxLengthBefore)   // NVARCHAR(250) => 500 bytes
            record (sprintf "baseline: Customer rows=%d, Email max_length=%d bytes (NVARCHAR 250), MAX(LEN(Email))=%d, value digest=%d" rowsBefore maxLengthBefore maxLenBefore digestBefore)
            record (sprintf "  probe (Email of MIN(Id) row) before = %s" probeBefore)

            // APPLY — widen Email to NVARCHAR(400) = 800 bytes.
            fixture.Rewrite "Tables/dbo.Customer.sql" customerWiderEmail
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: widen refused by production publish: %s" (this.Detail es)
            let! maxLengthAfter = this.Scalar maxLengthSql
            let! rowsAfter = this.Scalar rowCountSql
            let! digestAfter = this.Scalar digestSql
            let! maxLenAfter = this.Scalar maxLenSql
            let! probeAfter = this.ScalarStr probeSql
            record "production publish (BlockOnPossibleDataLoss=true) ALTER Email NVARCHAR(250)->NVARCHAR(400): APPLIED (Ok)."
            record (sprintf "  after apply: Email max_length=%d bytes (NVARCHAR 400), Customer rows=%d (intact), MAX(LEN(Email))=%d, value digest=%d" maxLengthAfter rowsAfter maxLenAfter digestAfter)
            record (sprintf "  probe (Email of MIN(Id) row) after = %s" probeAfter)
            flush ()

            Assert.Equal(800L, maxLengthAfter)      // NVARCHAR(400) => 800 bytes
            Assert.Equal(rowsBefore, rowsAfter)     // rows intact
            Assert.Equal(digestBefore, digestAfter) // every value preserved (aggregate digest)
            Assert.Equal(maxLenBefore, maxLenAfter) // longest value unchanged
            Assert.Equal(probeBefore, probeAfter)   // the probed value is byte-identical
        }

    // =====================================================================
    // 5) make-optional — relaxing NOT NULL to NULL applies clean.
    // =====================================================================
    [<Fact>]
    member this.``make-optional: relaxing NOT NULL to NULL applies clean on a populated table`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-make-optional.txt"), evidence.ToString()) with _ -> ()

            let isNullableSql = "SELECT ISNULL((SELECT CAST(c.is_nullable AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'OrderLine' AND c.name = N'Sku'), -1);"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let nonNullSkuSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [Sku] IS NOT NULL;"

            // BEFORE — populated OrderLine, Sku NOT NULL.
            let! _ = this.Fresh "make-optional/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! nullableBefore = this.Scalar isNullableSql
            let! nonNullBefore = this.Scalar nonNullSkuSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(0L, nullableBefore)   // Sku is NOT NULL
            record (sprintf "baseline: OrderLine rows=%d, Sku is_nullable=%d (NOT NULL), non-NULL Sku rows=%d" rowsBefore nullableBefore nonNullBefore)

            // APPLY — relax Sku to NULL (a loosening is never refused).
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineSkuOptional
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: make-optional refused by production publish: %s" (this.Detail es)
            let! nullableAfter = this.Scalar isNullableSql
            let! rowsAfter = this.Scalar rowCountSql
            let! nonNullAfter = this.Scalar nonNullSkuSql
            record "production publish (BlockOnPossibleDataLoss=true) ALTER Sku NVARCHAR(64) NOT NULL -> NULL: APPLIED (Ok)."
            record (sprintf "  after apply: Sku is_nullable=%d (nullable), OrderLine rows=%d (intact), non-NULL Sku rows=%d (values kept)" nullableAfter rowsAfter nonNullAfter)
            flush ()

            Assert.Equal(1L, nullableAfter)      // Sku now permits NULL
            Assert.Equal(rowsBefore, rowsAfter)  // rows intact
            Assert.Equal(nonNullBefore, nonNullAfter) // existing values untouched by the loosening
        }

    // =====================================================================
    // 6) retype-implicit — a widening type change applies clean and preserves
    //    every value.
    // =====================================================================
    [<Fact>]
    member this.``retype-implicit: a widening type change applies clean and preserves every value`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-retype-implicit.txt"), evidence.ToString()) with _ -> ()

            let typeSql = "SELECT ISNULL((SELECT ty.name FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'OrderLine' AND c.name = N'Quantity'), N'');"
            let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
            let sumSql = "SELECT ISNULL(SUM(CAST([Quantity] AS BIGINT)), 0) FROM [dbo].[OrderLine];"
            let maxSql = "SELECT ISNULL(MAX([Quantity]), 0) FROM [dbo].[OrderLine];"
            let probeSql = "SELECT ISNULL((SELECT [Quantity] FROM [dbo].[OrderLine] WHERE [Id] = (SELECT MIN([Id]) FROM [dbo].[OrderLine])), 0);"

            // BEFORE — populated OrderLine, Quantity INT.
            let! _ = this.Fresh "retype-implicit/fresh"
            let! rowsBefore = this.Scalar rowCountSql
            let! typeBefore = this.ScalarStr typeSql
            let! sumBefore = this.Scalar sumSql
            let! maxBefore = this.Scalar maxSql
            let! probeBefore = this.Scalar probeSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal("int", typeBefore)
            record (sprintf "baseline: OrderLine rows=%d, Quantity type=%s, SUM(Quantity)=%d, MAX(Quantity)=%d" rowsBefore typeBefore sumBefore maxBefore)
            record (sprintf "  probe (Quantity of MIN(Id) row) before = %d" probeBefore)

            // APPLY — widen Quantity INT -> BIGINT (every value already fits).
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineQuantityBigint
            let! strict = SamplePrPublish.strict fixture.Root fixture.Config
            match strict with
            | Ok () -> ()
            | Error es -> failwithf "PROOF FAILED: retype-implicit refused by production publish: %s" (this.Detail es)
            let! typeAfter = this.ScalarStr typeSql
            let! rowsAfter = this.Scalar rowCountSql
            let! sumAfter = this.Scalar sumSql
            let! maxAfter = this.Scalar maxSql
            let! probeAfter = this.Scalar probeSql
            record "production publish (BlockOnPossibleDataLoss=true) ALTER Quantity INT -> BIGINT: APPLIED (Ok)."
            record (sprintf "  after apply: Quantity type=%s, OrderLine rows=%d (intact), SUM(Quantity)=%d, MAX(Quantity)=%d" typeAfter rowsAfter sumAfter maxAfter)
            record (sprintf "  probe (Quantity of MIN(Id) row) after = %d" probeAfter)
            flush ()

            Assert.Equal("bigint", typeAfter)     // widened in place
            Assert.Equal(rowsBefore, rowsAfter)   // rows intact
            Assert.Equal(sumBefore, sumAfter)     // every value preserved (aggregate)
            Assert.Equal(maxBefore, maxAfter)     // largest value unchanged
            Assert.Equal(probeBefore, probeAfter) // the probed value is identical
        }
