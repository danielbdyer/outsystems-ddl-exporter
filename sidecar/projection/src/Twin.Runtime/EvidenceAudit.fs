namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Twin.Core

/// THE TWIN — `twin evidence audit` (Twin.Runtime).
///
/// The operator-reality validation: profile the minted template itself
/// through the same capture path the environments went through, then run
/// the pure pack-versus-pack audit against each merge input. The one
/// thing profiling cannot see — orphans planted on edges the trunk does
/// not constrain (the profiler measures orphan reality per catalog
/// reference only) — is probed directly, per recorded edge, and added to
/// the minted pack before the comparison. Witness legality skips become
/// the audit's exemptions, recomputed deterministically from the merged
/// pack rather than parsed from a report.
[<RequireQualifiedAccess>]
module EvidenceAudit =

    type AuditRunReport = {
        /// (source label, blocking failures, advisories).
        Sections      : (string * int * int) list
        TotalFailures : int
        ReportPath    : string
    }

    let defaultReportPath = "twin/evidence-audit.report.json"

    let private mergeUnset : ValidationError =
        ValidationError.create
            "twin.evidence.merge.unset"
            "The audit compares the template against the merge inputs, and none are configured. Add evidence.merge.inputs to twin.json, then rerun."

    let private richUnset : ValidationError =
        ValidationError.create
            "twin.evidence.richUnset"
            "The audit reads the merged pack from the rich path, and no rich path is configured. Set evidence.rich in twin.json, then rerun."

    let private mergedMissing (path: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.audit.mergedMissing"
            "The merged pack is not present. Run: twin evidence merge"
            (Map.ofList [ "path", Some path ])

    let private notUp : ValidationError =
        ValidationError.create "twin.notUp" "The twin is not running. Run: twin up, then retry."

    let private loadPack (path: string) (absent: ValidationError) : Result<EvidencePack> =
        if not (System.IO.File.Exists path) then Result.failureOf absent
        else
            try
                Evidence.deserialize (System.IO.File.ReadAllText path)
            with ex ->
                Result.failureOf
                    (ValidationError.createWithMetadata
                        "twin.evidence.unreadable"
                        "An evidence pack could not be read."
                        (Map.ofList [ "path", Some path; "detail", Some ex.Message ]))

    let private quote (s: string) : string = Projection.Targets.SSDT.Render.quote s

    let private write (path: string) (content: string) : unit =
        match System.IO.Path.GetDirectoryName path with
        | null | "" -> ()
        | dir -> System.IO.Directory.CreateDirectory dir |> ignore
        System.IO.File.WriteAllText(path, content)

    /// Count the orphans on one edge, directly — the profiler cannot see
    /// an edge the catalog carries no reference for.
    let private orphanCount
        (cnn: SqlConnection)
        (index: CatalogIndex)
        (edge: string * string * string)
        : Task<int64 option> =
        task {
            let (childTable, childColumn, parentTable) = edge
            let bound =
                match TableCoordinate.parse childTable, TableCoordinate.parse parentTable with
                | Ok childCoord, Ok parentCoord ->
                    match CatalogIndex.bindKind index childCoord, CatalogIndex.bindKind index parentCoord with
                    | Ok _, Ok parentKind ->
                        parentKind.Attributes
                        |> List.tryFind (fun a -> a.IsPrimaryKey)
                        |> Option.map (fun pk ->
                            childCoord, parentCoord, ColumnRealization.columnNameText pk.Column)
                    | _ -> None
                | _ -> None
            match bound with
            | None -> return None
            | Some (childCoord, parentCoord, parentKey) ->
                let qualifiedOf (c: TableCoordinate) =
                    System.String.Concat(quote (SchemaName.value c.Schema), ".", quote (TableName.value c.Table))  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                use cmd = cnn.CreateCommand()
                cmd.CommandText <-  // LINT-ALLOW: terminal audit probe SQL at the command boundary
                    System.String.Concat(  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                        "SELECT COUNT_BIG(*) FROM ", qualifiedOf childCoord, " c LEFT JOIN ",
                        qualifiedOf parentCoord, " p ON c.", quote childColumn, " = p.", quote parentKey,
                        " WHERE c.", quote childColumn, " IS NOT NULL AND p.", quote parentKey, " IS NULL")  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                let! count = cmd.ExecuteScalarAsync()
                return Some (System.Convert.ToInt64 count)
        }

    let run (root: string) (config: TwinConfig) : Task<Result<AuditRunReport>> =
        task {
            match config.Evidence.Merge, config.Evidence.RichRef with
            | None, _ -> return Result.failureOf mergeUnset
            | Some _, None -> return Result.failureOf richUnset
            | Some merge, Some richRef ->
                let richPath = TwinConfig.resolvePath root richRef
                match loadPack richPath (mergedMissing richPath) with
                | Error es -> return Result.failure es
                | Ok mergedPack ->
                    let inputs =
                        merge.Inputs
                        |> List.map (fun input ->
                            let path = TwinConfig.resolvePath root input
                            loadPack path (mergedMissing path))
                        |> Result.aggregate
                    match inputs with
                    | Error es -> return Result.failure es
                    | Ok sourcePacks ->
                        match TwinSubstrate.resolve config with
                        | Error es -> return Result.failure es
                        | Ok resolved ->
                            let! state = TwinSubstrate.state config
                            match state with
                            | Error es -> return Result.failure es
                            | Ok TwinContainer.Running ->
                                use cnn = new SqlConnection(resolved.TwinConnectionString)
                                do! cnn.OpenAsync()
                                let! readBack = Readback.readSchema cnn
                                match readBack with
                                | Error es -> return Result.failure es
                                | Ok catalog ->
                                    let profileCatalog = Catalog.stripStaticPopulations catalog
                                    let! cache =
                                        LiveProfiler.captureEvidenceCacheWith
                                            SqlProfilerOptions.defaults cnn profileCatalog
                                    match cache with
                                    | Error es -> return Result.failure es
                                    | Ok cache ->
                                        let profile =
                                            ProfileDerivation.attachFromCache cache profileCatalog Profile.empty
                                        let keep (k: Kind) =
                                            Some (TableCoordinate.text (TwinIdentity.coordinateOfKind k))
                                        let minted =
                                            Evidence.ofProfile "minted" profileCatalog keep profile
                                        // Probe the recorded orphan edges directly.
                                        let index = CatalogIndex.ofCatalog catalog
                                        let edges =
                                            sourcePacks
                                            |> List.collect (fun p -> p.Orphans)
                                            |> List.map (fun o -> o.ChildTable, o.ChildColumn, o.ParentTable)
                                            |> List.distinctBy (fun (c, col, p) ->
                                                c.ToLowerInvariant(), col.ToLowerInvariant(), p.ToLowerInvariant())
                                        let probed = System.Collections.Generic.List<OrphanEvidence>()
                                        for edge in edges do
                                            let! count = orphanCount cnn index edge
                                            match count with
                                            | Some n when n > 0L ->
                                                let (c, col, p) = edge
                                                probed.Add
                                                    { ChildTable = c; ChildColumn = col
                                                      ParentTable = p; OrphanCount = n }
                                            | _ -> ()
                                        let minted = { minted with Orphans = minted.Orphans @ List.ofSeq probed }
                                        // Witness legality skips are the exemptions,
                                        // recomputed deterministically.
                                        let _, skips = Witness.plan index mergedPack
                                        let exempt = skips |> List.map (fun s -> s.Coordinate) |> Set.ofList
                                        let report = FidelityAudit.auditAll exempt sourcePacks minted
                                        let reportPath = TwinConfig.resolvePath root defaultReportPath
                                        write reportPath (FidelityAudit.serializeReport report)
                                        return
                                            Result.success
                                                { Sections =
                                                      report.Sections
                                                      |> List.map (fun s -> s.Source, s.Failures, s.Advisories)
                                                  TotalFailures = FidelityAudit.failures report
                                                  ReportPath = reportPath }
                            | Ok _ -> return Result.failureOf notUp
        }
