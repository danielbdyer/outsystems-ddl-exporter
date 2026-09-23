module Twin.Tests.Integration.SamplePrCompoundTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the COMPOUND archetype: the unit of proof is the RELEASE DELTA.
// The engine compiles one script per release, so a pull request carrying
// several operations is proven by publishing its combined delta — never by the
// sum of its atoms' individual proofs. First proven live on the proving ground
// (sample-prs/compound/, sqlpackage 170.5.76, 2026-08-28); these facts are the
// same laws' executable witnesses on the Twin substrate, so the nightly proof
// lane holds them:
//   1. the additive batch — two new tables, two foreign keys, and a defaulted
//      NOT NULL column on a populated table, ONE publish: nothing blocks (the
//      release inherits no guard from all-additive atoms), DacFx orders the
//      objects itself, the default stamps every existing row, the keys land.
//   2. the atomic veto — one INNOCENT additive atom (a new nullable column on
//      Order) and one BLOCKING atom (a tightening on populated OrderLine) in
//      ONE publish: the guard refuses the release and the WHOLE delta rolls
//      back — the innocent atom included. A release is vetoed by its
//      strictest atom; this is the proven reason reshape-coupled atoms
//      serialize (skills/decompose, sample-prs/compound/rename-then-tighten).
//
// Facts are self-contained and order-independent (the removal archetype's
// Fresh primitive); evidence flushes to the temp dir before assertions.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrCompoundFixture () =
    inherit TwinEstateFixture ("twin-e2e-compound", 21846)

[<Collection("Twin-Docker")>]
type SamplePrCompoundTests (fixture: SamplePrCompoundFixture) =

    // ---- the batch molecule's files ---------------------------------------

    let returnReason =
        """CREATE TABLE [dbo].[ReturnReason] (
    [Id]       INT           NOT NULL,
    [Code]     NVARCHAR(30)  NOT NULL,
    [IsActive] BIT           NOT NULL CONSTRAINT [DF_ReturnReason_IsActive] DEFAULT ((1)),
    CONSTRAINT [PK_ReturnReason] PRIMARY KEY ([Id])
);
"""

    let returnTable =
        """CREATE TABLE [dbo].[Return] (
    [Id]             INT NOT NULL IDENTITY(1,1),
    [OrderLineId]    INT NOT NULL,
    [ReturnReasonId] INT NOT NULL,
    [Quantity]       INT NOT NULL CONSTRAINT [DF_Return_Quantity] DEFAULT ((1)),
    CONSTRAINT [PK_Return] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Return_OrderLine] FOREIGN KEY ([OrderLineId]) REFERENCES [dbo].[OrderLine] ([Id]),
    CONSTRAINT [FK_Return_ReturnReason] FOREIGN KEY ([ReturnReasonId]) REFERENCES [dbo].[ReturnReason] ([Id])
);
"""

    let orderWithReturnsAllowed =
        """CREATE TABLE [dbo].[Order] (
    [Id]             INT           IDENTITY(1,1) NOT NULL,
    [CustomerId]     INT           NOT NULL,
    [StatusId]       INT           NOT NULL,
    [Channel]        NVARCHAR(20)  NOT NULL,
    [Total]          DECIMAL(18,2) NOT NULL,
    [PlacedOn]       DATETIME2     NOT NULL,
    [ReturnsAllowed] BIT           NOT NULL CONSTRAINT [DF_Order_ReturnsAllowed] DEFAULT ((1)),
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    // ---- the veto molecule's edits ----------------------------------------

    let orderWithMemo =
        """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    [Memo]       NVARCHAR(100) NULL,
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    let orderLineNoteNotNull =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NOT NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    interface IClassFixture<SamplePrCompoundFixture>

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
    // 1) the additive batch — one publish, nothing blocks, defaults stamp
    //    every existing row, the engine orders the objects itself.
    // =====================================================================
    [<Fact>]
    member this.``compound additive batch: two tables, two keys, and a defaulted NOT NULL column land in one publish`` () : Task =
        task {
            let ev = System.Text.StringBuilder()
            let! _ = this.Fresh "compound-batch"
            let! ordersBefore = this.Scalar "SELECT COUNT(*) FROM [dbo].[Order]"

            fixture.Rewrite "Tables/dbo.ReturnReason.sql" returnReason
            fixture.Rewrite "Tables/dbo.Return.sql" returnTable
            fixture.Rewrite "Tables/dbo.Order.sql" orderWithReturnsAllowed
            let! outcome = SamplePrPublish.strict fixture.Root fixture.Config
            match outcome with
            | Error es ->
                this.Flush "twin-sample-pr-compound-batch-evidence.txt" ev
                failwithf "the all-additive batch refused: %s" (this.Detail es)
            | Ok () -> ()

            let! stamped = this.Scalar "SELECT COUNT(*) FROM [dbo].[Order] WHERE [ReturnsAllowed] = 1"
            let! newTables = this.Scalar "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Return','ReturnReason')"
            let! newKeys = this.Scalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN ('FK_Return_OrderLine','FK_Return_ReturnReason')"
            ev.AppendLine(sprintf "one publish: orders %d, stamped %d, tables %d, keys %d" ordersBefore stamped newTables newKeys) |> ignore
            this.Flush "twin-sample-pr-compound-batch-evidence.txt" ev

            Assert.True(ordersBefore > 0L)
            Assert.Equal(ordersBefore, stamped)
            Assert.Equal(2L, newTables)
            Assert.Equal(2L, newKeys)
        }

    // =====================================================================
    // 2) the atomic veto — the blocking atom refuses the release and the
    //    innocent atom rolls back with it.
    // =====================================================================
    [<Fact>]
    member this.``compound atomic veto: the blocking atom refuses the release and the innocent atom rolls back with it`` () : Task =
        task {
            let ev = System.Text.StringBuilder()
            let! _ = this.Fresh "compound-veto"
            let! linesBefore = this.Scalar "SELECT COUNT(*) FROM [dbo].[OrderLine]"

            fixture.Rewrite "Tables/dbo.Order.sql" orderWithMemo
            fixture.Rewrite "Tables/dbo.OrderLine.sql" orderLineNoteNotNull
            let! outcome = SamplePrPublish.strict fixture.Root fixture.Config
            let detail =
                match outcome with
                | Ok () -> ""
                | Error es -> this.Detail es
            let! memoExists = this.Scalar "SELECT CASE WHEN COL_LENGTH('dbo.Order','Memo') IS NULL THEN 0 ELSE 1 END"
            let! noteNullable = this.Scalar "SELECT CAST(is_nullable AS INT) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OrderLine') AND name = 'Note'"
            ev.AppendLine(sprintf "lines=%d memoExists=%d noteNullable=%d" linesBefore memoExists noteNullable) |> ignore
            ev.AppendLine("refusal detail: " + detail) |> ignore
            this.Flush "twin-sample-pr-compound-veto-evidence.txt" ev

            match outcome with
            | Ok () -> failwith "the combined release published — expected the tightening's guard to veto it"
            | Error _ -> ()
            Assert.True(linesBefore > 0L)
            Assert.Contains("Rows were detected", detail)
            Assert.Equal(0L, memoExists)
            Assert.Equal(1L, noteNullable)
        }
