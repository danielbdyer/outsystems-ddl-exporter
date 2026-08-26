namespace Twin.Runtime

open System.Threading.Tasks
open Projection.Core
open Twin.Core

/// THE TWIN — `twin evidence merge` (Twin.Runtime).
///
/// Reads the per-environment rich packs the config names, clamps each to
/// the TRUNK's coordinates (drift is reported per environment, never
/// merged into the model), runs the crossover, and writes two artifacts:
/// the merged rich pack at `evidence.rich` — where the mint already
/// looks, so minting from the crossover needs no mint change — and the
/// merge report, literal-free and committable, carrying every winning
/// extreme's environment.
///
/// The trunk binding needs no running twin and no Docker: it rides
/// `TrunkModel.readback`, which honors `PROJECTION_MSSQL_CONN_STR`.
[<RequireQualifiedAccess>]
module EvidenceMerge =

    type MergeRunReport = {
        /// (source label, tables the clamped pack carries).
        Inputs       : (string * int) list
        MergedTables : int
        DriftCount   : int
        RichPath     : string
        ReportPath   : string
    }

    let defaultReportPath = "twin/evidence-merge.report.json"

    let private mergeUnset : ValidationError =
        ValidationError.create
            "twin.evidence.merge.unset"
            "No crossover inputs are configured. Add evidence.merge.inputs to twin.json — one rich pack per environment — then rerun."

    let private richUnset : ValidationError =
        ValidationError.create
            "twin.evidence.richUnset"
            "The merge writes the merged pack to the rich path, and no rich path is configured. Set evidence.rich in twin.json (an out-of-repo location), then rerun."

    let private inputMissing (path: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.merge.inputMissing"
            "A crossover input pack is not present. Merging without an environment would let an average replace an extreme; capture it first (twin evidence import), or remove it from evidence.merge.inputs."
            (Map.ofList [ "path", Some path ])

    let private loadInput (path: string) : Result<EvidencePack> =
        if not (System.IO.File.Exists path) then Result.failureOf (inputMissing path)
        else
            try
                Evidence.deserialize (System.IO.File.ReadAllText path)
            with ex ->
                Result.failureOf
                    (ValidationError.createWithMetadata
                        "twin.evidence.unreadable"
                        "An evidence pack could not be read."
                        (Map.ofList [ "path", Some path; "detail", Some ex.Message ]))

    let private write (path: string) (content: string) : unit =
        match System.IO.Path.GetDirectoryName path with
        | null | "" -> ()
        | dir -> System.IO.Directory.CreateDirectory dir |> ignore
        System.IO.File.WriteAllText(path, content)

    let run (root: string) (config: TwinConfig) : Task<Result<MergeRunReport>> =
        task {
            match config.Evidence.Merge, config.Evidence.RichRef with
            | None, _ -> return Result.failureOf mergeUnset
            | Some _, None -> return Result.failureOf richUnset
            | Some merge, Some richRef ->
                let loaded =
                    merge.Inputs
                    |> List.map (fun input -> loadInput (TwinConfig.resolvePath root input))
                    |> Result.aggregate
                match loaded with
                | Error es -> return Result.failure es
                | Ok inputs ->
                    let! trunk = TrunkModel.readback root config
                    match trunk with
                    | Error es -> return Result.failure es
                    | Ok catalog ->
                        let sets = Crossover.trunkSets (CatalogIndex.ofCatalog catalog)
                        let clamped = inputs |> List.map (Crossover.clamp sets)
                        let drift = clamped |> List.collect snd
                        match Crossover.merge (clamped |> List.map fst) with
                        | Error es -> return Result.failure es
                        | Ok (merged, report) ->
                            let report = { report with Drift = drift }
                            let richPath = TwinConfig.resolvePath root richRef
                            let reportPath =
                                TwinConfig.resolvePath root (defaultArg merge.ReportPath defaultReportPath)
                            write richPath (Evidence.serialize merged)
                            write reportPath (Crossover.serializeReport report)
                            return
                                Result.success
                                    { Inputs =
                                          (clamped |> List.map fst, inputs)
                                          ||> List.map2 (fun c original ->
                                              let label =
                                                  match original.Sources with
                                                  | [] -> "(unlabeled)"
                                                  | sources -> sources |> List.sort |> String.concat "+"
                                              label, List.length c.Tables)
                                      MergedTables = List.length merged.Tables
                                      DriftCount = List.length drift
                                      RichPath = richPath
                                      ReportPath = reportPath }
        }
