module Twin.Tests.Integration.SamplePrInverseTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the "inverse" archetype: the four drop operations that remove a
// CONSTRAINT-level guarantee (a default, a check, a unique index, a primary
// key), each first proven live on the proving ground via the packaged loop
// (sample-prs/drop-*.md, sqlpackage 170.5.76, 2026-08-28) and here given its
// executable witness on the Twin substrate so the nightly proof lane holds it.
//
// The family's shared law, proven per-op below: the DROP itself always lands
// (nothing to validate, no row touched) — and the COST of the drop is deferred
// to the gap, proven by writing the newly-permitted violation and watching the
// guarantee's RE-ADD refuse over it:
//   1. drop-default — add DF, drop DF: both in-place, rows never move. The gap
//      cost (inserts that omitted the column) is an application-runtime fact a
//      publish cannot witness; the fact proves the schema legs.
//   2. drop-check  — add CK over clean data (lands trusted), drop it cleanly,
//      write the violating row the check would have refused, then prove the
//      re-add REFUSES over that row (the constraint-is-a-claim law, on the
//      gap's own data).
//   3. drop-unique — the unique index drops declaratively (the granular
//      DropIndexesNotInSource default, as the removal archetype discovered);
//      a duplicate then writes freely, and the re-add REFUSES over it
//      (Msg 1505 — the door swings shut behind the drop).
//   4. drop-pk    — two faces. REFERENCED by any FK: the MODEL refuses to
//      build (SQL71516) — the refusal comes before the engine is reached.
//      UNREFERENCED: the drop publishes clean and the table is a HEAP after,
//      every row intact.
//
// Every fact is self-contained and order-independent (the removal archetype's
// Fresh primitive). Evidence is flushed to the temp dir BEFORE assertions so a
// surprising outcome is preserved as a finding.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrInverseFixture () =
    inherit TwinEstateFixture ("twin-e2e-inverse", 21845)

[<Collection("Twin-Docker")>]
type SamplePrInverseTests (fixture: SamplePrInverseFixture) =

    // ---- the per-op schema edits ------------------------------------------

    // drop-default legs: Order with a named default on [Channel], and baseline.
    let orderWithDefault =
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

    // drop-check legs: Order with a CHECK on [Total], and baseline.
    let orderWithCheck =
        """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]),
    CONSTRAINT [CK_Order_Total_NonNegative] CHECK ([Total] >= 0)
);
"""

    // drop-unique legs: the unique index rides its own one-statement file.
    let uniqueRel = "Tables/dbo.Customer.UX_Email.sql"
    let uniqueCreate = "CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email] ON [dbo].[Customer] ([Email]);\n"

    // drop-pk legs: Order without its PK (referenced face — FK_OrderLine_Order
    // still references it), and OrderLine without its PK (unreferenced face —
    // nothing references OrderLine's key).
    let orderNoPk =
        """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

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

    interface IClassFixture<SamplePrInverseFixture>

    // ---- helpers (mirror the removal archetype) ---------------------------

    member private _.Up () : Task<Result<Runs.UpOutcome>> =
        Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false

    member private _.Scalar (sql: string) : Task<int64> =
        SamplePrSql.scalar fixture.TwinConnectionString sql

    member private _.Exec (sql: string) : Task<int> =
        SamplePrSql.exec fixture.TwinConnectionString sql

    member private _.Detail (es: ValidationError list) : string =
        SamplePrSql.detail es

    member private this.Converge (label: string) : Task<Runs.MaterializeReport> =
        task {
            let! outcome = this.Up()
            match outcome with
            | Ok (Runs.Materialized r) -> return r
            | Ok (Runs.NothingToApply _) -> return failwithf "%s: expected Materialized, got NothingToApply" label
            | Error es -> return failwithf "%s: up refused: %A" label (es |> List.map (fun e -> e.Code, e.Message))
        }

    /// Restore the baseline, remove stray files, drop the twin DB, reconverge.
    /// The non-awaiting reset prep, hoisted OUT of the task (FS3511:
    /// `for` + `try/with` inside a resumable state machine fail to reduce
    /// in Release builds — CLAUDE.md survival rule 5, the align-I.1 class).
    member private _.ResetFiles () : unit =
        let keep = set [ "dbo.Status.sql"; "dbo.Customer.sql"; "dbo.Order.sql"; "dbo.OrderLine.sql" ]
        let tablesDir = System.IO.Path.Combine(fixture.Root, "Tables")
        if System.IO.Directory.Exists tablesDir then
            for file in System.IO.Directory.GetFiles(tablesDir, "*.sql") do
                let name = System.IO.Path.GetFileName file |> Option.ofObj |> Option.defaultValue ""
                if not (keep.Contains name) then
                    try System.IO.File.Delete file with _ -> ()
        for f in SamplePrBaseline.files do fixture.Rewrite (fst f) (snd f)

    member private this.Fresh (label: string) : Task<Runs.MaterializeReport> =
        task {
            this.ResetFiles ()
            do! SamplePrBaseline.dropTwinDatabase fixture.Config
            return! this.Converge label
        }

    member private _.Flush (name: string) (evidence: System.Text.StringBuilder) : unit =
        let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name)
        System.IO.File.WriteAllText(path, evidence.ToString())

    // =====================================================================
    // 1) drop-default — both legs in place, rows never move.
    // =====================================================================
    [<Fact>]
    member this.``drop-default: the add and the drop are both in-place; existing rows never move`` () : Task =
        task {
            let ev = System.Text.StringBuilder()
            let! _ = this.Fresh "drop-default"
            let! rowsBefore = this.Scalar "SELECT COUNT(*) FROM [dbo].[Order]"

            fixture.Rewrite "Tables/dbo.Order.sql" orderWithDefault
            let! addOutcome = SamplePrPublish.strict fixture.Root fixture.Config
            match addOutcome with
            | Error es -> failwithf "add-default refused unexpectedly: %s" (this.Detail es)
            | Ok () -> ()
            let! dfCount = this.Scalar "SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_Order_Channel'"
            ev.AppendLine(sprintf "after add: DF exists=%d, rows=%d" dfCount rowsBefore) |> ignore

            fixture.Rewrite "Tables/dbo.Order.sql" SamplePrBaseline.order
            let! dropOutcome = SamplePrPublish.strict fixture.Root fixture.Config
            match dropOutcome with
            | Error es -> failwithf "drop-default refused unexpectedly: %s" (this.Detail es)
            | Ok () -> ()
            let! dfAfter = this.Scalar "SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_Order_Channel'"
            let! rowsAfter = this.Scalar "SELECT COUNT(*) FROM [dbo].[Order]"
            ev.AppendLine(sprintf "after drop: DF exists=%d, rows=%d" dfAfter rowsAfter) |> ignore
            this.Flush "twin-sample-pr-drop-default-evidence.txt" ev

            Assert.Equal(1L, dfCount)
            Assert.Equal(0L, dfAfter)
            Assert.Equal(rowsBefore, rowsAfter)
        }

    // =====================================================================
    // 2) drop-check — the drop is clean; the gap's violating row blocks the
    //    guarantee's return (constraint-is-a-claim, on the gap's own data).
    // =====================================================================
    [<Fact>]
    member this.``drop-check: the drop is clean; a violating row written in the gap refuses the re-add`` () : Task =
        task {
            let ev = System.Text.StringBuilder()
            let! _ = this.Fresh "drop-check"

            // Clean the minted Totals so the add lands over conforming data.
            let! _ = this.Exec "UPDATE [dbo].[Order] SET [Total] = ABS([Total])"
            fixture.Rewrite "Tables/dbo.Order.sql" orderWithCheck
            let! addOutcome = SamplePrPublish.strict fixture.Root fixture.Config
            match addOutcome with
            | Error es -> failwithf "add-check over clean data refused: %s" (this.Detail es)
            | Ok () -> ()
            let! trust = this.Scalar "SELECT CAST(is_not_trusted AS INT) FROM sys.check_constraints WHERE name = 'CK_Order_Total_NonNegative'"
            ev.AppendLine(sprintf "add-check landed, is_not_trusted=%d" trust) |> ignore

            fixture.Rewrite "Tables/dbo.Order.sql" SamplePrBaseline.order
            let! dropOutcome = SamplePrPublish.strict fixture.Root fixture.Config
            match dropOutcome with
            | Error es -> failwithf "drop-check refused unexpectedly: %s" (this.Detail es)
            | Ok () -> ()
            let! ckAfter = this.Scalar "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_Order_Total_NonNegative'"

            // The gap: the violation the check would have refused now writes freely.
            let! inserted =
                this.Exec
                    "INSERT INTO [dbo].[Order] ([CustomerId],[StatusId],[Channel],[Total],[PlacedOn]) SELECT TOP 1 [Id], 1, N'Web', -5.00, SYSUTCDATETIME() FROM [dbo].[Customer]"
            ev.AppendLine(sprintf "gap: violating row inserted (%d row), check gone (count=%d)" inserted ckAfter) |> ignore

            // The re-add refuses over the gap's data.
            fixture.Rewrite "Tables/dbo.Order.sql" orderWithCheck
            let! readd = SamplePrPublish.strict fixture.Root fixture.Config
            match readd with
            | Ok () ->
                this.Flush "twin-sample-pr-drop-check-evidence.txt" ev
                failwith "re-adding the check over a violating row applied — expected the claim to refuse"
            | Error es ->
                ev.AppendLine("re-add refused: " + this.Detail es) |> ignore
                this.Flush "twin-sample-pr-drop-check-evidence.txt" ev
                Assert.Equal(0L, trust)
                Assert.Equal(0L, ckAfter)
                Assert.Equal(1, inserted)
                Assert.Contains("CK_Order_Total_NonNegative", this.Detail es)
        }

    // =====================================================================
    // 3) drop-unique — the index drops declaratively; a duplicate written in
    //    the gap refuses the re-add (the door swings shut behind the drop).
    // =====================================================================
    [<Fact>]
    member this.``drop-unique: the drop is clean; a duplicate written in the gap refuses the re-add`` () : Task =
        task {
            let ev = System.Text.StringBuilder()
            let! _ = this.Fresh "drop-unique"

            // Make emails unique so the unique index lands.
            let! _ = this.Exec "UPDATE [dbo].[Customer] SET [Email] = CONCAT(N'u', [Id], N'@example.test')"
            fixture.Rewrite uniqueRel uniqueCreate
            let! addOutcome = SamplePrPublish.strict fixture.Root fixture.Config
            match addOutcome with
            | Error es -> failwithf "add-unique over deduplicated data refused: %s" (this.Detail es)
            | Ok () -> ()
            let! uxCount = this.Scalar "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_Customer_Email'"

            // Remove the index's file: the granular DropIndexesNotInSource drops it.
            System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, uniqueRel.Replace('/', System.IO.Path.DirectorySeparatorChar)))
            let! dropOutcome = SamplePrPublish.strict fixture.Root fixture.Config
            match dropOutcome with
            | Error es -> failwithf "drop-unique refused unexpectedly: %s" (this.Detail es)
            | Ok () -> ()
            let! uxAfter = this.Scalar "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_Customer_Email'"

            // The gap: a duplicate now writes freely.
            let! inserted =
                this.Exec
                    "INSERT INTO [dbo].[Customer] ([Name],[Email],[StatusId],[CreatedOn]) SELECT TOP 1 N'Duplicate', [Email], [StatusId], SYSUTCDATETIME() FROM [dbo].[Customer]"
            ev.AppendLine(sprintf "index add=%d drop=%d, duplicate inserted=%d" uxCount uxAfter inserted) |> ignore

            // The re-add refuses over the duplicate.
            fixture.Rewrite uniqueRel uniqueCreate
            let! readd = SamplePrPublish.strict fixture.Root fixture.Config
            match readd with
            | Ok () ->
                this.Flush "twin-sample-pr-drop-unique-evidence.txt" ev
                failwith "re-adding uniqueness over a duplicate applied — expected the build to refuse"
            | Error es ->
                ev.AppendLine("re-add refused: " + this.Detail es) |> ignore
                this.Flush "twin-sample-pr-drop-unique-evidence.txt" ev
                Assert.Equal(1L, uxCount)
                Assert.Equal(0L, uxAfter)
                Assert.Equal(1, inserted)
        }

    // =====================================================================
    // 4) drop-pk — referenced: the MODEL refuses to build (the engine is never
    //    reached); unreferenced: clean drop, the table is a heap, rows intact.
    // =====================================================================
    [<Fact>]
    member this.``drop-pk: referenced refuses at the model build; unreferenced drops clean and leaves a heap`` () : Task =
        task {
            let ev = System.Text.StringBuilder()
            let! _ = this.Fresh "drop-pk"

            // Referenced face: FK_OrderLine_Order still targets Order's key.
            fixture.Rewrite "Tables/dbo.Order.sql" orderNoPk
            let! referenced = SamplePrPublish.strict fixture.Root fixture.Config
            let referencedText =
                match referenced with
                | Ok () -> ""
                | Error es -> this.Detail es + " " + (es |> List.map (fun e -> e.Message) |> String.concat " ")
            ev.AppendLine("referenced face: " + referencedText) |> ignore

            // Unreferenced face: nothing references OrderLine's key.
            fixture.Rewrite "Tables/dbo.Order.sql" SamplePrBaseline.order
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineNoPk
            let! rowsBefore = this.Scalar "SELECT COUNT(*) FROM [dbo].[OrderLine]"
            let! unreferenced = SamplePrPublish.strict fixture.Root fixture.Config
            match unreferenced with
            | Error es ->
                this.Flush "twin-sample-pr-drop-pk-evidence.txt" ev
                failwithf "unreferenced pk drop refused unexpectedly: %s" (this.Detail es)
            | Ok () -> ()
            let! pkCount = this.Scalar "SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.OrderLine') AND type = 'PK'"
            let! heap = this.Scalar "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.OrderLine') AND index_id = 0"
            let! rowsAfter = this.Scalar "SELECT COUNT(*) FROM [dbo].[OrderLine]"
            ev.AppendLine(sprintf "unreferenced face: pk=%d heap=%d rows %d -> %d" pkCount heap rowsBefore rowsAfter) |> ignore
            this.Flush "twin-sample-pr-drop-pk-evidence.txt" ev

            match referenced with
            | Ok () -> failwith "removing a REFERENCED primary key published — expected the model build to refuse"
            | Error _ -> Assert.Contains("71516", referencedText)
            Assert.Equal(0L, pkCount)
            Assert.Equal(1L, heap)
            Assert.Equal(rowsBefore, rowsAfter)
        }
