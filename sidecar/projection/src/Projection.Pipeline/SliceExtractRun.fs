namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.Json

/// `projection slice-extract` — the use-case-scoped data-portability EXTRACT
/// verb (Slice 3). Reads a curated "use case" (a root entity + an optional
/// WHERE predicate) from a live SOURCE, walks the FK closure to a self-
/// contained, referentially-closed row-set, writes the portable golden dataset,
/// and returns the closure census + the dangling-mandatory-FK count (the
/// operator's proof the slice is referentially self-contained). Read-only
/// against the source (mirrors `ProfileCaptureRun`'s connect→read→write shape).
[<RequireQualifiedAccess>]
module SliceExtractRun =

    /// Open a read connection to the source from any of the accepted spec
    /// forms: `env:<VAR>` / `file:<path>` (resolved through the canonical
    /// `ConnectionResolver`), or `live:<connStr>` / a bare connection string
    /// (opened directly). The cross-environment design is indifferent to how
    /// the source is addressed.
    let private openSource (spec: string) : Task<Result<SqlConnection>> =
        task {
            if spec.StartsWith "env:" || spec.StartsWith "file:" then
                match TransferSpec.parseConnectionSpec spec with
                | Error es -> return Result.failure es
                | Ok connRef ->
                    let sub : Substrate =
                        { Environment   = Environment.Named "slice-source"
                          Role          = SubstrateRole.Source
                          ConnectionRef = connRef }
                    return! ConnectionResolver.openSubstrate sub
            else
                let connStr = if spec.StartsWith "live:" then spec.Substring 5 else spec
                try
                    let cnn = new SqlConnection(connStr)
                    do! cnn.OpenAsync()
                    return Result.success cnn
                with ex ->
                    return Result.failureOf (ValidationError.create "connection.openFailed" ex.Message)
        }

    /// Extract the closure for a single-root slice and write the golden dataset
    /// to `outPath`. Returns `(censusByEntity, danglingMandatoryCount)`.
    let extract
        (connSpec: string)
        (rootEntity: string)
        (whereRaw: string option)
        (outPath: string)
        : Task<Result<(string * int) list * int>> =
        task {
            match! openSource connSpec with
            | Error es -> return Result.failure es
            | Ok cnn ->
                    use cnn = cnn
                    match! ReadSide.read cnn with
                    | Error es -> return Result.failure es
                    | Ok catalog ->
                        match ClosureOracle.resolveEntity catalog (EntityCoordinate.ofEntity rootEntity) with
                        | None ->
                            return
                                Result.failureOf
                                    (ValidationError.create
                                        "slice.root.unknown"
                                        (sprintf "root entity '%s' was not found in the source catalog." rootEntity))
                        | Some rootKind ->
                            let predicate =
                                match whereRaw with
                                | Some w -> Predicate.Raw w
                                | None   -> Predicate.All
                            let! roots = ClosureOracle.fetchRootsByPredicate cnn rootKind predicate
                            let! state = ClosureOracle.walk cnn catalog [] [ roots ]
                            let golden = GoldenDataset.ofClosure catalog state
                            let report = Closure.report catalog state
                            let census = golden.Entities |> List.map (fun e -> e.Entity, List.length e.Rows)
                            try
                                System.IO.File.WriteAllText(outPath, GoldenCodec.serialize golden)
                                return Result.success (census, List.length report.DanglingMandatory)
                            with ex ->
                                return Result.failureOf (ValidationError.create "slice.writeFailed" ex.Message)
        }
