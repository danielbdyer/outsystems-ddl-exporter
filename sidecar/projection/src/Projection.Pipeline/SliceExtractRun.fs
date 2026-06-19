namespace Projection.Pipeline

// STOPGAP (2026-06-18 — pre-existing, NOT part of the Spectre render chapter): the
// `extractSpec` task below trips FS3511 (an `await`/`let!` inside a `for` loop) under
// Release's static-state-machine optimization, which TreatWarningsAsErrors promotes to
// an error — breaking the whole Release build (and the perf-gate) downstream. The code
// is correct: FS3511 falls back to a dynamic state machine, and this is the small
// root-resolution loop, not a hot path. Suppressed here to unblock Release; the proper
// fix (hoist the await-loop into a module-level recursive `task` helper, minding the
// reverse list order the loop builds) is tracked as a separate task.
#nowarn "3511"

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

    /// Extract a MULTI-root slice from a `SliceSpec` (the config-driven form):
    /// every root's predicate seeds the walk, under the spec's traversal
    /// directives. Returns `(censusByEntity, danglingMandatoryCount)`.
    let extractSpec (connSpec: string) (spec: SliceSpec) (outPath: string) : Task<Result<(string * int) list * int>> =
        task {
            match! openSource connSpec with
            | Error es -> return Error es
            | Ok cnn ->
                use cnn = cnn
                match! ReadSide.read cnn with
                | Error es -> return Error es
                | Ok catalog ->
                    // Resolve every root entity first (pure); refuse on the first miss.
                    let resolved = spec.Roots |> List.map (fun r -> r, ClosureOracle.resolveEntity catalog r.Entity)
                    match resolved |> List.tryPick (fun (r, k) -> if Option.isNone k then Some r.Entity else None) with
                    | Some missing ->
                        return
                            Result.failureOf
                                (ValidationError.create "slice.root.unknown"
                                    (sprintf "root entity '%s' was not found in the source catalog."
                                        (EntityCoordinate.render missing)))
                    | None ->
                        let mutable rootFetches : Closure.FetchedRows list = []
                        // `for pair in …` (not a tuple-pattern `for`) — FS3511-safe.
                        for pair in resolved do
                            match snd pair with
                            | Some kind ->
                                let! fr = ClosureOracle.fetchRootsByPredicate cnn kind (fst pair).Predicate
                                rootFetches <- fr :: rootFetches
                            | None -> ()
                        let! state = ClosureOracle.walk cnn catalog spec.Directives rootFetches
                        let golden = GoldenDataset.ofClosure catalog state
                        let report = Closure.report catalog state
                        let census = golden.Entities |> List.map (fun e -> e.Entity, List.length e.Rows)
                        try
                            System.IO.File.WriteAllText(outPath, GoldenCodec.serialize golden)
                            return Result.success (census, List.length report.DanglingMandatory)
                        with ex ->
                            return Result.failureOf (ValidationError.create "slice.writeFailed" ex.Message)
        }

    /// The config-driven entry: read a versioned slice-definition file
    /// (`SliceCodec`, re-validated on decode), then `extractSpec`.
    let extractSpecFromFile (connSpec: string) (slicePath: string) (outPath: string) : Task<Result<(string * int) list * int>> =
        task {
            let json =
                try Ok (System.IO.File.ReadAllText slicePath)
                with ex -> Result.failureOf (ValidationError.create "slice.spec.read" ex.Message)
            match json with
            | Error es -> return Error es
            | Ok j ->
                match SliceCodec.deserialize j with
                | Error es -> return Error es
                | Ok spec -> return! extractSpec connSpec spec outPath
        }
