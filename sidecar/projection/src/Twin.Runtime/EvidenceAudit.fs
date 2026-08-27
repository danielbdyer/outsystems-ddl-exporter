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
/// the pure pack-versus-pack audit against each merge input. What
/// profiling cannot see is probed directly, per recorded edge, and added
/// to the minted pack before the comparison: orphans planted on edges the
/// trunk does not constrain (the profiler measures orphan reality per
/// catalog reference only), and fan-outs on logical-but-unenforced edges
/// (the read-back catalog carries enforced references only, so the
/// profiler's cardinality capture never reaches them). Witness legality
/// skips become the audit's exemptions, recomputed deterministically from
/// the merged pack rather than parsed from a report.
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

    // Hoisted from `run` (FS3511: a `for` over tuple-typed elements with
    // an await in the body does not statically compile in Release; the
    // while-walk with single-value binds does — the survival rule's
    // hoist remedy).
    let private probeOrphans
        (cnn: SqlConnection)
        (index: CatalogIndex)
        (edges: (string * string * string) list)
        : Task<OrphanEvidence list> =
        task {
            let probed = System.Collections.Generic.List<OrphanEvidence>()
            let mutable remaining = edges  // LINT-ALLOW: the while-walk's cursor — FS3511 forces this shape (a `for` over tuple elements with an await does not compile in Release); confined to this loop
            while not (List.isEmpty remaining) do
                let edge = List.head remaining
                remaining <- List.tail remaining  // LINT-ALLOW: the while-walk's cursor advance; same FS3511 confinement
                let! count = orphanCount cnn index edge
                match count with
                | Some n when n > 0L ->
                    let (childTable, childColumn, parentTable) = edge
                    probed.Add
                        { ChildTable = childTable; ChildColumn = childColumn
                          ParentTable = parentTable; OrphanCount = n }
                | _ -> ()
            return List.ofSeq probed
        }

    /// Measure one edge's realized fan-out on the minted copy — child
    /// side only, because children-per-parent needs no parent join. The
    /// four measured figures fabricate a conservative shape (interior
    /// quartiles borrow the measured medians); the audit reads only P95
    /// (margin) and Max (blocking), both of which are exact here.
    let private fanOutShape
        (cnn: SqlConnection)
        (index: CatalogIndex)
        (childTable: string)
        (childColumn: string)
        : Task<NumericShape option> =
        task {
            let bound =
                match TableCoordinate.parse childTable with
                | Ok childCoord ->
                    match CatalogIndex.bindKind index childCoord with
                    | Ok _ -> Some childCoord
                    | _ -> None
                | _ -> None
            match bound with
            | None -> return None
            | Some childCoord ->
                let qualified =
                    System.String.Concat(quote (SchemaName.value childCoord.Schema), ".", quote (TableName.value childCoord.Table))  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                use cmd = cnn.CreateCommand()
                cmd.CommandText <-  // LINT-ALLOW: terminal audit probe SQL at the command boundary
                    System.String.Concat(  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                        "SELECT DISTINCT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY cnt) OVER () AS p50, ",
                        "PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY cnt) OVER () AS p95, ",
                        "MIN(cnt) OVER () AS mn, MAX(cnt) OVER () AS mx FROM (SELECT COUNT_BIG(*) AS cnt FROM ",
                        qualified, " WHERE ", quote childColumn, " IS NOT NULL GROUP BY ", quote childColumn, ") g")  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                use! reader = cmd.ExecuteReaderAsync()
                let! has = reader.ReadAsync()
                if not has || reader.IsDBNull 3 then return None
                else
                    let p50 = if reader.IsDBNull 0 then 0m else decimal (reader.GetDouble 0)
                    let p95 = if reader.IsDBNull 1 then 0m else decimal (reader.GetDouble 1)
                    let mn = if reader.IsDBNull 2 then 0m else decimal (reader.GetInt64 2)
                    let mx = decimal (reader.GetInt64 3)
                    return Some { Min = mn; P25 = p50; P50 = p50; P75 = p95; P95 = p95; P99 = mx; Max = mx }
        }

    // Hoisted for the same FS3511 reason as `probeOrphans`: the fan-out
    // leg walks recorded edges with an await per step.
    let private probeFanOuts
        (cnn: SqlConnection)
        (index: CatalogIndex)
        (edges: (string * string * string) list)
        : Task<FanOutEvidence list> =
        task {
            let probed = System.Collections.Generic.List<FanOutEvidence>()
            let mutable remaining = edges  // LINT-ALLOW: the while-walk's cursor — FS3511 forces this shape (a `for` over tuple elements with an await does not compile in Release); confined to this loop
            while not (List.isEmpty remaining) do
                let edge = List.head remaining
                remaining <- List.tail remaining  // LINT-ALLOW: the while-walk's cursor advance; same FS3511 confinement
                let (childTable, childColumn, parentTable) = edge
                let! shape = fanOutShape cnn index childTable childColumn
                match shape with
                | Some s ->
                    probed.Add
                        { ChildTable = childTable; ChildColumn = childColumn
                          ParentTable = parentTable; Shape = s }
                | None -> ()
            return List.ofSeq probed
        }

    // The audit's tail, hoisted so its awaits head their own state
    // machine (the FS3511 survival rule's shape): probe the recorded
    // orphan edges on the minted copy, recompute the witness exemptions,
    // run the pack-vs-pack comparison per environment, write the report.
    let private auditMinted
        (cnn: SqlConnection)
        (root: string)
        (sourcePacks: EvidencePack list)
        (mergedPack: EvidencePack)
        (catalog: Catalog)
        (minted: EvidencePack)
        : Task<Result<AuditRunReport>> =
        task {
            let index = CatalogIndex.ofCatalog catalog
            let edges =
                sourcePacks
                |> List.collect (fun p -> p.Orphans)
                |> List.map (fun o -> o.ChildTable, o.ChildColumn, o.ParentTable)
                |> List.distinctBy (fun (c, col, p) ->
                    c.ToLowerInvariant(), col.ToLowerInvariant(), p.ToLowerInvariant())
            let! probed = probeOrphans cnn index edges
            let minted = { minted with Orphans = minted.Orphans @ probed }
            // Fan-outs the profiler could not reach: recorded edges (max
            // two or more — the audit ignores the rest) that the minted
            // pack does not already carry, i.e. edges with no enforced
            // reference in the read-back catalog.
            let mintedEdgeKeys =
                minted.FanOuts
                |> List.map (fun f ->
                    f.ChildTable.ToLowerInvariant(), f.ChildColumn.ToLowerInvariant(), f.ParentTable.ToLowerInvariant())
                |> Set.ofList
            let fanOutEdges =
                sourcePacks
                |> List.collect (fun p -> p.FanOuts)
                |> List.filter (fun f -> System.Decimal.Ceiling f.Shape.Max >= 2m)
                |> List.map (fun f -> f.ChildTable, f.ChildColumn, f.ParentTable)
                |> List.distinctBy (fun (c, col, p) ->
                    c.ToLowerInvariant(), col.ToLowerInvariant(), p.ToLowerInvariant())
                |> List.filter (fun (c, col, p) ->
                    not (Set.contains (c.ToLowerInvariant(), col.ToLowerInvariant(), p.ToLowerInvariant()) mintedEdgeKeys))
            let! probedFanOuts = probeFanOuts cnn index fanOutEdges
            let minted = { minted with FanOuts = minted.FanOuts @ probedFanOuts }
            let skips = snd (Witness.plan index mergedPack)
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
                                        let bare =
                                            Evidence.ofProfile "minted" profileCatalog keep profile
                                        // The minted side carries the same
                                        // string-plane counts the sources do —
                                        // the audit compares like with like.
                                        let boundKinds =
                                            Catalog.allKinds profileCatalog
                                            |> List.choose (fun k -> keep k |> Option.map (fun coordText -> coordText, k))
                                        let! enriched = RealityProbe.enrich cnn boundKinds bare
                                        match enriched with
                                        | Error es -> return Result.failure es
                                        | Ok minted ->
                                            return! auditMinted cnn root sourcePacks mergedPack catalog minted
                            | Ok _ -> return Result.failureOf notUp
        }
