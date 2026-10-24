namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// **X7 — schema drift detection (the protein P-8 drift check).** The criterion
/// is "diff the deployed substrate vs **the model**" — not vs a second deployed
/// substrate (which is what `verify-data` does, and cannot tell whether the one
/// deployed schema has drifted from the authored intent). `detect` reads the
/// live schema through `ReadSide` and computes `PhysicalSchema.diff(model,
/// deployed)`: an empty diff means the deployment still matches the model; a
/// non-empty diff is the drift, rendered for the operator. The model is the
/// authored `Catalog` B (in-memory) — the comparison basis is intent, not
/// another database.
[<RequireQualifiedAccess>]
module DriftRun =

    /// Read the deployed schema and diff it against the authored `model`.
    /// `PhysicalSchema.isEqual` of the returned diff ⇒ no drift.
    let detect (model: Catalog) (cnn: SqlConnection) : Task<Result<PhysicalSchemaDiff>> =
        task {
            use _ = Bench.scope "driftRun.detect"
            let! readBack = ReadSide.read cnn
            match readBack with
            | Error es -> return Result.failure es
            | Ok deployed ->
                let diff =
                    PhysicalSchema.diff
                        (PhysicalSchema.ofCatalog model)
                        (PhysicalSchema.ofCatalog deployed)
                return Result.success diff
        }
