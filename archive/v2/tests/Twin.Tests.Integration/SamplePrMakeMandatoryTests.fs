module Twin.Tests.Integration.SamplePrMakeMandatoryTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PR — "Make OrderLine.Note mandatory" (NULL -> NOT NULL).
//
// The objective proof, against the live Twin with real-shaped synthetic data,
// that tightening a nullable Attribute to mandatory is what PRODUCTION refuses.
//
// PRIMARY proof (production-faithful, BlockOnPossibleDataLoss = true via
// SamplePrPublish.strict): a POPULATED table is BLOCKED even with ZERO NULLs —
// SSDT's row-presence guard fires. The identical edit on an EMPTY table applies
// clean. This mirrors the proving-ground COL-03 / 03B / 03C triple and is the
// block a real environment raises.
//
// SECONDARY context (the Twin's own relaxed Runs.up posture,
// BlockOnPossibleDataLoss = false): the block there is data-driven — SQL Server
// Msg 515 on an actual NULL — because the row-presence guard is suppressed.
// Kept so the record is honest about both postures.
//
// Discovered publish semantics (drives every tightening op's test): a blocked
// publish surfaces as `Error [ ValidationError ]` with Code
// "twin.publish.failed" and the DacFx report in Metadata["detail"] — from BOTH
// `Runs.up` (relaxed) and `SamplePrPublish.strict` (production-faithful).
// ---------------------------------------------------------------------------

/// Its own container + port (isolated from the schema/mint/evidence fixtures
/// on 21533 / 21633 / 21733).
type TwinSampleEstateFixture () =
    inherit TwinEstateFixture ("twin-e2e-sample", 21833)

[<Collection("Twin-Docker")>]
type SamplePrMakeMandatoryTests (fixture: TwinSampleEstateFixture) =

    let nullableOrderLine =
        """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""

    // The ONE edit the OutSystems developer makes: Is Mandatory = Yes on
    // OrderLine.Note. Identical to the estate's OrderLine but for NULL ->
    // NOT NULL on [Note].
    let notNullOrderLine =
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

    let isNullableSql =
        "SELECT CAST(c.is_nullable AS BIGINT) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = N'OrderLine' AND c.name = N'Note';"

    let rowCountSql = "SELECT COUNT(*) FROM [dbo].[OrderLine];"
    let nullNotesSql = "SELECT COUNT(*) FROM [dbo].[OrderLine] WHERE [Note] IS NULL;"

    interface IClassFixture<TwinSampleEstateFixture>

    member private _.Up () : Task<Result<Runs.UpOutcome>> =
        Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false

    member private _.Scalar (sql: string) : Task<int64> =
        task {
            use cnn = new SqlConnection(fixture.TwinConnectionString)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt64 v
        }

    member private _.Exec (sql: string) : Task<int> =
        task {
            use cnn = new SqlConnection(fixture.TwinConnectionString)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! n = cmd.ExecuteNonQueryAsync()
            return n
        }

    /// The DacFx deploy report for a refused publish — parked in the
    /// ValidationError's "detail" metadata by both publish paths.
    member private _.Detail (es: ValidationError list) : string =
        es
        |> List.map (fun e -> e.Metadata |> Map.tryFind "detail" |> Option.flatten |> Option.defaultValue "")
        |> String.concat "\n"

    /// Converge (relaxed) and demand success. `force` re-mints even when the
    /// fingerprints match (used to reset the data between phases).
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

    [<Fact>]
    member this.``make OrderLine.Note mandatory: production publish blocks a populated table, applies when empty`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                let outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twin-sample-pr-make-mandatory-evidence.txt")
                try System.IO.File.WriteAllText(outPath, evidence.ToString()) with _ -> ()

            // ===== Phase 0 — materialize the BEFORE estate ================
            let! baseline = this.Converge "baseline" false
            Assert.True(baseline.SchemaPublished, "the first up publishes the estate schema")
            let! rowsBefore = this.Scalar rowCountSql
            let! nullBefore = this.Scalar nullNotesSql
            let! nullableBefore = this.Scalar isNullableSql
            Assert.True(rowsBefore > 0L, "OrderLine must hold rows for the proof")
            Assert.Equal(1L, nullableBefore)   // is_nullable = 1 (Note is optional)
            Assert.Equal(0L, nullBefore)       // fresh mint fills every row — 0 NULLs
            record (sprintf "baseline: OrderLine rows=%d, Note IS NULL count=%d, is_nullable=%d, twin totalRows=%d"
                        rowsBefore nullBefore nullableBefore baseline.TotalRows)

            // ===== SECONDARY — the Twin's relaxed Runs.up blocks only on an
            // actual NULL (Msg 515). Recorded first, while __state is in sync.
            let! _ = this.Exec "UPDATE TOP (3) [dbo].[OrderLine] SET [Note] = NULL;"
            let! nullSeeded = this.Scalar nullNotesSql
            Assert.Equal(3L, nullSeeded)
            fixture.Rewrite "Tables/dbo.OrderLine.sql" notNullOrderLine
            let! relaxed = this.Up()
            let relaxedDetail =
                match relaxed with
                | Ok _ -> failwith "relaxed up unexpectedly accepted NOT NULL over a NULL row"
                | Error es ->
                    Assert.Contains("twin.publish.failed", es |> List.map (fun e -> e.Code))
                    this.Detail es
            Assert.Contains("Msg 515", relaxedDetail)
            Assert.Contains("Cannot insert the value NULL into column 'Note'", relaxedDetail)
            let! nullableRelaxed = this.Scalar isNullableSql
            let! rowsRelaxed = this.Scalar rowCountSql
            let! nullRelaxed = this.Scalar nullNotesSql
            Assert.Equal(1L, nullableRelaxed)  // still nullable — rolled back
            record (sprintf "SECONDARY relaxed Runs.up (BlockOnPossibleDataLoss=false): REFUSED with Msg 515 while %d of %d rows held NULL Note; is_nullable stayed %d"
                        nullRelaxed rowsRelaxed nullableRelaxed)
            record "  relaxed detail:"
            record relaxedDetail

            // ===== Reset to a clean POPULATED-with-0-NULLs table ==========
            fixture.Rewrite "Tables/dbo.OrderLine.sql" nullableOrderLine
            let! _ = this.Converge "reset" true    // force re-mint
            let! rowsClean = this.Scalar rowCountSql
            let! nullClean = this.Scalar nullNotesSql
            let! nullableClean = this.Scalar isNullableSql
            Assert.True(rowsClean > 0L)
            Assert.Equal(0L, nullClean)            // zero NULLs — the production-guard condition
            Assert.Equal(1L, nullableClean)
            record (sprintf "reset: OrderLine rows=%d, Note IS NULL count=%d, is_nullable=%d (populated, zero NULLs)"
                        rowsClean nullClean nullableClean)

            // ===== PRIMARY — the PRODUCTION-FAITHFUL publish BLOCKS the
            // populated table even with ZERO NULLs (row-presence guard). ====
            fixture.Rewrite "Tables/dbo.OrderLine.sql" notNullOrderLine
            let! strictBlocked = SamplePrPublish.strict fixture.Root fixture.Config
            let strictDetail =
                match strictBlocked with
                | Ok () -> failwith "PROOF FAILED: the production publish accepted NOT NULL on a populated table"
                | Error es ->
                    Assert.Contains("twin.publish.failed", es |> List.map (fun e -> e.Code))
                    this.Detail es
            let! nullableAfterStrict = this.Scalar isNullableSql
            let! rowsAfterStrict = this.Scalar rowCountSql
            let! nullAfterStrict = this.Scalar nullNotesSql
            record "PRIMARY production-faithful publish (BlockOnPossibleDataLoss=true): REFUSED on a populated, zero-NULL table."
            record (sprintf "  before block: rows=%d, Note IS NULL=%d, is_nullable=1" rowsClean nullClean)
            record (sprintf "  after  block: rows=%d, Note IS NULL=%d, is_nullable=%d (unchanged)" rowsAfterStrict nullAfterStrict nullableAfterStrict)
            record "  strict detail:"
            record strictDetail
            flush () // capture before content assertions, so a wrong marker never loses the text

            // The block is row-presence, NOT NULL content: zero NULLs, still refused.
            Assert.Equal(0L, nullAfterStrict)          // still zero NULLs
            Assert.Equal(rowsClean, rowsAfterStrict)   // rows intact
            Assert.Equal(1L, nullableAfterStrict)      // schema unchanged — still nullable
            // The row-presence guard fired (Msg 50000 RAISERROR above the ALTER),
            // NOT the NULL-content check (Msg 515) — the production behavior.
            Assert.Contains("Msg 50000", strictDetail)
            Assert.Contains("Rows were detected", strictDetail)
            Assert.Contains("data loss might occur", strictDetail)
            Assert.Contains("IF EXISTS", strictDetail)
            Assert.Contains("RAISERROR", strictDetail)
            Assert.DoesNotContain("Msg 515", strictDetail) // it is NOT the NULL-content block

            // ===== PRIMARY contrast — EMPTY table applies clean ===========
            let! _ = this.Exec "DELETE FROM [dbo].[OrderLine];"
            let! rowsEmptied = this.Scalar rowCountSql
            Assert.Equal(0L, rowsEmptied)
            let! strictEmpty = SamplePrPublish.strict fixture.Root fixture.Config
            match strictEmpty with
            | Ok () -> ()
            | Error es -> failwithf "empty-table production publish unexpectedly refused: %s" (this.Detail es)
            let! nullableEmpty = this.Scalar isNullableSql
            let! rowsEmpty = this.Scalar rowCountSql
            Assert.Equal(0L, nullableEmpty)            // is_nullable = 0 — Note is now mandatory
            Assert.Equal(0L, rowsEmpty)                // strict publish alters schema only; no re-mint
            record (sprintf "PRIMARY empty-table production publish APPLIED: is_nullable=%d (mandatory), rows=%d" nullableEmpty rowsEmpty)

            flush ()
        }
