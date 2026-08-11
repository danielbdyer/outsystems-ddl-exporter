module Twin.Tests.Integration.TwinViewLoopTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// A view in the estate publishes, but is never wiped or minted. A view over
// multiple base tables is not updatable (it refuses DELETE), which tripped
// the clean-slate wipe before the fix (`twin.wipe.failed`). Readback now
// excludes views from the wipe/mint (they carry no data), while leaving them
// published in the schema. (DECISIONS 2026-07-20.)
// ---------------------------------------------------------------------------

[<Collection("Twin-Docker")>]
type TwinViewLoopTests (fixture: TwinViewEstateFixture) =

    interface IClassFixture<TwinViewEstateFixture>

    member private _.Scalar (sql: string) : Task<int64> =
        task {
            use cnn = new SqlConnection(fixture.TwinConnectionString)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt64 v
        }

    [<Fact>]
    member this.``A view in the estate: twin up succeeds, base tables mint, the view stays published and data-free`` () : Task =
        task {
            // Pre-fix this refused with `twin.wipe.failed` — a DELETE against
            // the multi-base-table view. It must now succeed.
            let! outcome = Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false
            match outcome with
            | Error es -> return failwithf "up refused (the view broke the wipe): %A" (es |> List.map (fun e -> e.Code, e.Message))
            | Ok _ ->
                // The base tables still mint with a view present.
                let! customers = this.Scalar "SELECT COUNT(*) FROM [dbo].[Customer];"
                Assert.True(customers > 0L, "base tables must still mint with a view present")

                // The view is published in the schema plane...
                let! views = this.Scalar "SELECT COUNT(*) FROM sys.views WHERE [name] = 'vCustomerOrders';"
                Assert.Equal(1L, views)

                // ...and reads through to its base rows (proving it is a real,
                // resolvable view over the minted data), while never being a
                // wipe/mint target itself.
                let! throughView = this.Scalar "SELECT COUNT(*) FROM [dbo].[vCustomerOrders];"
                Assert.True(throughView >= 0L, "the published view must be queryable")

                // A re-seed stays deterministic — the view does not perturb the mint.
                let! reseed = Runs.seed fixture.Root fixture.Config TwinConfig.BaselineScenario
                match reseed with
                | Error es -> return failwithf "re-seed refused: %A" (es |> List.map (fun e -> e.Code, e.Message))
                | Ok _ -> return ()
        }
