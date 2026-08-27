namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Twin.Core

/// THE TWIN — `twin evidence audit` (Twin.Runtime).
///
/// The operator-reality validation: profile the minted template itself
/// through the same capture path the environments went through, then run
/// the pure pack-versus-pack audit against each merge input. What
/// profiling cannot see is probed directly, per recorded coordinate, and
/// added to the minted pack before the comparison: orphans planted on
/// edges the trunk does not constrain (the profiler measures orphan
/// reality per catalog reference only), fan-outs on
/// logical-but-unenforced edges (the read-back catalog carries enforced
/// references only, so the profiler's cardinality capture never reaches
/// them), and exact envelopes for columns the numeric sample floor
/// silences (a heavily-floored small population still holds its planted
/// edges — MIN/MAX are exact at any count). Witness legality skips
/// become the audit's exemptions, recomputed deterministically from the
/// merged pack rather than parsed from a report.
[<RequireQualifiedAccess>]
module EvidenceAudit =

    type AuditRunReport = {
        /// (source label, blocking failures, advisories).
        Sections      : (string * int * int) list
        /// The deep per-environment legs (F3): a throwaway template
        /// minted from each input alone, witness-planted, and audited
        /// against that same input — "would this block at QA
        /// specifically", proven rather than asserted. Empty only when
        /// the merge names no inputs (which the audit already refuses).
        Deep          : (string * int * int) list
        TotalFailures : int
        ReportPath    : string
        DeepReportPath : string option
    }

    let defaultReportPath = "twin/evidence-audit.report.json"
    let deepReportPath = "twin/evidence-audit.deep.report.json"

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

    /// Probe the exact envelope for one column — MIN and MAX are exact
    /// at any row count, while the profiler's numeric distribution obeys
    /// the statistical sample-size floor and goes silent on a
    /// heavily-floored small population. Dates probe as ticks, matching
    /// the capture convention (K2).
    let private envelopeOf
        (cnn: SqlConnection)
        (index: CatalogIndex)
        (tableName: string)
        (columnName: string)
        : Task<NumericShape option> =
        task {
            let bound =
                match TableCoordinate.parse tableName with
                | Ok coord ->
                    match CatalogIndex.bindKind index coord with
                    | Ok kind ->
                        kind.Attributes
                        |> List.tryFind (fun a ->
                            System.String.Equals(
                                ColumnRealization.columnNameText a.Column, columnName,
                                System.StringComparison.OrdinalIgnoreCase))
                        |> Option.bind (fun a ->
                            match a.Type with
                            | PrimitiveType.Integer | PrimitiveType.Decimal
                            | PrimitiveType.DateTime | PrimitiveType.Date -> Some coord
                            | _ -> None)
                    | _ -> None
                | _ -> None
            match bound with
            | None -> return None
            | Some coord ->
                let qualified =
                    System.String.Concat(quote (SchemaName.value coord.Schema), ".", quote (TableName.value coord.Table))  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                use cmd = cnn.CreateCommand()
                cmd.CommandText <-  // LINT-ALLOW: terminal audit probe SQL at the command boundary
                    System.String.Concat(  // LINT-ALLOW: terminal audit probe SQL; identifiers pass through the SSDT renderer's quoting
                        "SELECT MIN(", quote columnName, "), MAX(", quote columnName, ") FROM ",
                        qualified, " WHERE ", quote columnName, " IS NOT NULL")
                use! reader = cmd.ExecuteReaderAsync()
                let! has = reader.ReadAsync()
                if not has || reader.IsDBNull 0 || reader.IsDBNull 1 then return None
                else
                    let toDecimal (o: obj) : decimal =
                        match o with
                        | :? System.DateTime as dt -> decimal dt.Ticks
                        | :? System.DateTimeOffset as dto -> decimal dto.Ticks
                        | v -> System.Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture)
                    let mn = toDecimal (reader.GetValue 0)
                    let mx = toDecimal (reader.GetValue 1)
                    let mid = (mn + mx) / 2m
                    return Some { Min = mn; P25 = mn; P50 = mid; P75 = mx; P95 = mx; P99 = mx; Max = mx }
        }

    // Hoisted for the same FS3511 reason as `probeOrphans`: the envelope
    // walk awaits once per unmeasured coordinate.
    let private probeEnvelopes
        (cnn: SqlConnection)
        (index: CatalogIndex)
        (sourcePacks: EvidencePack list)
        (minted: EvidencePack)
        : Task<EvidencePack> =
        task {
            let mintedMeasured =
                minted.Tables
                |> List.collect (fun t ->
                    t.Columns
                    |> List.map (fun c ->
                        (t.Table.ToLowerInvariant(), c.Column.ToLowerInvariant()), c.Numeric.IsSome))
                |> Map.ofList
            let targets =
                sourcePacks
                |> List.collect (fun p ->
                    p.Tables
                    |> List.collect (fun t ->
                        t.Columns
                        |> List.filter (fun c -> c.Numeric.IsSome)
                        |> List.map (fun c -> t.Table, c.Column)))
                |> List.distinctBy (fun (t, c) -> t.ToLowerInvariant(), c.ToLowerInvariant())
                |> List.filter (fun (t, c) ->
                    match Map.tryFind (t.ToLowerInvariant(), c.ToLowerInvariant()) mintedMeasured with
                    | Some false -> true
                    | _ -> false)
            let probed = System.Collections.Generic.Dictionary<string * string, NumericShape>()  // LINT-ALLOW: the while-walk's accumulator — sealed function-local, assembled once then read immutably; FS3511 forces the walk shape
            let mutable remaining = targets  // LINT-ALLOW: the while-walk's cursor — FS3511 forces this shape (a `for` over tuple elements with an await does not compile in Release); confined to this loop
            while not (List.isEmpty remaining) do
                let target = List.head remaining
                remaining <- List.tail remaining  // LINT-ALLOW: the while-walk's cursor advance; same FS3511 confinement
                let (tableName, columnName) = target
                let! shape = envelopeOf cnn index tableName columnName
                match shape with
                | Some s -> probed.[(tableName.ToLowerInvariant(), columnName.ToLowerInvariant())] <- s  // LINT-ALLOW: the while-walk's accumulator write; same sealed-local confinement
                | None -> ()
            return
                { minted with
                    Tables =
                        minted.Tables
                        |> List.map (fun t ->
                            { t with
                                Columns =
                                    t.Columns
                                    |> List.map (fun c ->
                                        match probed.TryGetValue((t.Table.ToLowerInvariant(), c.Column.ToLowerInvariant())) with
                                        | true, s when c.Numeric.IsNone -> { c with Numeric = Some s }
                                        | _ -> c) }) }
        }

    /// Probe what profiling cannot see for the given sources — orphans
    /// on unconstrained edges, fan-outs on unenforced ones, exact
    /// envelopes under the numeric sample floor — and fold the results
    /// into the minted pack before the comparison. Shared by the
    /// template leg and the deep per-environment legs.
    let private probeRecordedEdges
        (cnn: SqlConnection)
        (index: CatalogIndex)
        (sourcePacks: EvidencePack list)
        (minted: EvidencePack)
        : Task<EvidencePack> =
        task {
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
            return! probeEnvelopes cnn index sourcePacks minted
        }

    /// Profile a minted database through the same capture path the
    /// environments went through, string-plane counts included. The
    /// catalog is the profile-plane one (static populations stripped).
    let private profileMinted
        (cnn: SqlConnection)
        (profileCatalog: Catalog)
        : Task<Result<EvidencePack>> =
        task {
            let! cache =
                LiveProfiler.captureEvidenceCacheWith SqlProfilerOptions.defaults cnn profileCatalog
            match cache with
            | Error es -> return Result.failure es
            | Ok cache ->
                let profile = ProfileDerivation.attachFromCache cache profileCatalog Profile.empty
                let keep (k: Kind) =
                    Some (TableCoordinate.text (TwinIdentity.coordinateOfKind k))
                let bare = Evidence.ofProfile "minted" profileCatalog keep profile
                let boundKinds =
                    Catalog.allKinds profileCatalog
                    |> List.choose (fun k -> keep k |> Option.map (fun coordText -> coordText, k))
                return! RealityProbe.enrich cnn boundKinds bare
        }

    // The audit's tail, hoisted so its awaits head their own state
    // machine (the FS3511 survival rule's shape): probe the recorded
    // edges on the minted copy, recompute the witness exemptions, run
    // the pack-vs-pack comparison per environment.
    let private auditMinted
        (cnn: SqlConnection)
        (sourcePacks: EvidencePack list)
        (mergedPack: EvidencePack)
        (catalog: Catalog)
        (minted: EvidencePack)
        : Task<Result<AuditReport>> =
        task {
            let index = CatalogIndex.ofCatalog catalog
            let! minted = probeRecordedEdges cnn index sourcePacks minted
            let skips = snd (Witness.plan index mergedPack)
            let exempt = skips |> List.map (fun s -> s.Coordinate) |> Set.ofList
            return Result.success (FidelityAudit.auditAll exempt sourcePacks minted)
        }

    // ------------------------------------------------------------------
    // The deep per-environment leg (F3): mint a throwaway template from
    // ONE environment's pack alone, plant that pack's own witnesses, and
    // audit the result against the same pack — decision (j)'s
    // per-environment round-trip, automatic whenever the merge names
    // inputs. "Would this block at QA specifically" is then proven per
    // bake, never asserted.
    // ------------------------------------------------------------------

    let private connTo (serverCnnStr: string) (db: string) : string =
        let builder = SqlConnectionStringBuilder serverCnnStr
        builder.InitialCatalog <- db  // LINT-ALLOW: terminal ADO.NET builder boundary; the vendor connection-string API is imperative by contract
        builder.ConnectionString

    let private deepDbName (label: string) : string =
        let cleaned = label |> String.map (fun ch -> if System.Char.IsLetterOrDigit ch then ch else '_')
        System.String.Concat("TwinDeepAudit_", cleaned)  // LINT-ALLOW: terminal throwaway database name; the label is reduced to identifier characters on the line above

    let private dropDatabase (serverCnnStr: string) (db: string) : Task<unit> =
        task {
            use cnn = new SqlConnection(serverCnnStr)
            do! cnn.OpenAsync()
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-  // LINT-ALLOW: terminal throwaway-drop SQL at the command boundary
                System.String.Concat(  // LINT-ALLOW: terminal throwaway-drop SQL; the database name is generated and identifier-safe by construction
                    "IF DB_ID(N'", db, "') IS NOT NULL BEGIN ALTER DATABASE [", db,
                    "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [", db, "]; END")
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

    let private deepWitnessFailed (label: string) (failures: int64) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.audit.deepWitnessFailed"
            "A deep per-environment leg's witnesses did not land on its throwaway template."
            (Map.ofList [ "environment", Some label; "failures", Some (string failures) ])

    /// Read the witness assertion script's final `failures` figure.
    let private witnessFailures (cnn: SqlConnection) (sql: string) : Task<int64> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql  // LINT-ALLOW: terminal witness-assert SQL at the command boundary; the script is the emitter's own artifact
            use! reader = cmd.ExecuteReaderAsync()
            let mutable failures = 0L  // LINT-ALLOW: the result-set walk's accumulator — ADO.NET readers are imperatively paged; confined to this loop
            let mutable moreSets = true  // LINT-ALLOW: the result-set walk's cursor; same confinement
            while moreSets do
                let failureSet = reader.FieldCount = 1 && reader.GetName 0 = "failures"
                let mutable moreRows = true  // LINT-ALLOW: the result-set walk's row cursor; same confinement
                while moreRows do
                    let! has = reader.ReadAsync()
                    if not has then moreRows <- false  // LINT-ALLOW: the result-set walk's cursor advance; same confinement
                    elif failureSet then failures <- System.Convert.ToInt64 (reader.GetValue 0)  // LINT-ALLOW: the result-set walk's accumulator assignment; same confinement
                let! next = reader.NextResultAsync()
                moreSets <- next  // LINT-ALLOW: the result-set walk's cursor advance; same confinement
            return failures
        }

    // Publish the estate head into the throwaway, apply the static
    // lanes, and mint from the ONE environment's pack — the same steps
    // the twin's own seed takes, against a different database.
    let private deepMint
        (root: string)
        (config: TwinConfig)
        (serverCnn: string)
        (db: string)
        (estate: EstateDefinition)
        (dacpac: byte[])
        (inputRel: string)
        : Task<Result<Catalog>> =
        task {
            let! published = EstateModel.publishTo serverCnn db dacpac
            match published with
            | Error es -> return Result.failure es
            | Ok () ->
                use cnn = new SqlConnection(connTo serverCnn db)
                do! cnn.OpenAsync()
                let! lanes = TwinDatabase.applyStaticLanes cnn estate
                match lanes with
                | Error es -> return Result.failure es
                | Ok _ ->
                    let! readBack = Readback.read cnn
                    match readBack with
                    | Error es -> return Result.failure es
                    | Ok catalog ->
                        let pools = Readback.providedPools catalog
                        let mintCatalog = Catalog.stripStaticPopulations catalog
                        let envConfig =
                            { config with
                                Evidence = { config.Evidence with ShapePath = None; RichRef = Some inputRel } }
                        match Mint.prepare root envConfig TwinConfig.BaselineScenario mintCatalog pools with
                        | Error es -> return Result.failure es
                        | Ok plan ->
                            let! minted = Mint.run cnn mintCatalog plan
                            match minted with
                            | Error es -> return Result.failure es
                            | Ok _ -> return Result.success mintCatalog
        }

    let private deepAuditBody
        (root: string)
        (config: TwinConfig)
        (serverCnn: string)
        (db: string)
        (estate: EstateDefinition)
        (dacpac: byte[])
        (label: string)
        (inputRel: string)
        (envPack: EvidencePack)
        : Task<Result<AuditSection>> =
        task {
            let! mintedCatalog = deepMint root config serverCnn db estate dacpac inputRel
            match mintedCatalog with
            | Error es -> return Result.failure es
            | Ok mintCatalog ->
                use cnn = new SqlConnection(connTo serverCnn db)
                do! cnn.OpenAsync()
                let index = CatalogIndex.ofCatalog mintCatalog
                let witnessPlan, skips = Witness.plan index envPack
                do! Deploy.executeBatch cnn (Witness.emitSql config.Seed witnessPlan)
                let! failures = witnessFailures cnn (Witness.emitAssertSql witnessPlan)
                if failures > 0L then return Result.failureOf (deepWitnessFailed label failures)
                else
                    let! minted = profileMinted cnn mintCatalog
                    match minted with
                    | Error es -> return Result.failure es
                    | Ok minted ->
                        let! minted = probeRecordedEdges cnn index [ envPack ] minted
                        let exempt = skips |> List.map (fun s -> s.Coordinate) |> Set.ofList
                        return Result.success (FidelityAudit.audit exempt envPack minted)
        }

    let private deepAuditOne
        (root: string)
        (config: TwinConfig)
        (serverCnn: string)
        (estate: EstateDefinition)
        (dacpac: byte[])
        (entry: string * string * EvidencePack)
        : Task<Result<AuditSection>> =
        task {
            let (label, inputRel, envPack) = entry
            let db = deepDbName label
            // Pre-clean a crashed prior run, then guarantee the drop.
            do! dropDatabase serverCnn db
            try
                return! deepAuditBody root config serverCnn db estate dacpac label inputRel envPack
            finally
                (dropDatabase serverCnn db).GetAwaiter().GetResult()
        }

    // Hoisted for the same FS3511 reason as `probeOrphans`: the deep
    // walk awaits once per environment.
    let private deepAuditAll
        (root: string)
        (config: TwinConfig)
        (serverCnn: string)
        (entries: (string * string * EvidencePack) list)
        : Task<Result<AuditSection list>> =
        task {
            match EstateFiles.resolve root config.Estate with
            | Error es -> return Result.failure es
            | Ok estate ->
                match EstateModel.buildDacpac estate with
                | Error es -> return Result.failure es
                | Ok dacpac ->
                    let sections = System.Collections.Generic.List<AuditSection>()
                    let mutable failed : ValidationError list = []  // LINT-ALLOW: the while-walk's failure latch — FS3511 forces this shape (a `for` over tuple elements with an await does not compile in Release); confined to this loop
                    let mutable remaining = entries  // LINT-ALLOW: the while-walk's cursor; same FS3511 confinement
                    while not (List.isEmpty remaining) && List.isEmpty failed do
                        let entry = List.head remaining
                        remaining <- List.tail remaining  // LINT-ALLOW: the while-walk's cursor advance; same FS3511 confinement
                        let! result = deepAuditOne root config serverCnn estate dacpac entry
                        match result with
                        | Error es -> failed <- es  // LINT-ALLOW: the while-walk's failure latch assignment; same FS3511 confinement
                        | Ok section -> sections.Add section
                    if not (List.isEmpty failed) then return Result.failure failed
                    else return Result.success (List.ofSeq sections)
        }

    let private labelInputs (inputs: string list) (packs: EvidencePack list) : (string * string * EvidencePack) list =
        List.map2
            (fun rel (pack: EvidencePack) ->
                let label =
                    match pack.Sources with
                    | [] -> "(unlabeled)"
                    | sources -> sources |> List.sort |> String.concat "+"  // LINT-ALLOW: the attribution label is the sorted source names joined; a label IS a string primitive
                label, rel, pack)
            inputs packs

    /// Write both reports and fold the two legs into the run summary.
    let private assembleRunReport
        (root: string)
        (main: AuditReport)
        (deepSections: AuditSection list)
        : AuditRunReport =
        let deep : AuditReport = { Sections = deepSections |> List.sortBy (fun s -> s.Source) }
        let mainPath = TwinConfig.resolvePath root defaultReportPath
        write mainPath (FidelityAudit.serializeReport main)
        let deepPath = TwinConfig.resolvePath root deepReportPath
        write deepPath (FidelityAudit.serializeReport deep)
        { Sections = main.Sections |> List.map (fun s -> s.Source, s.Failures, s.Advisories)
          Deep = deep.Sections |> List.map (fun s -> s.Source, s.Failures, s.Advisories)
          TotalFailures = FidelityAudit.failures main + FidelityAudit.failures deep
          ReportPath = mainPath
          DeepReportPath = Some deepPath }

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
                                    // The minted side carries the same
                                    // string-plane counts the sources do —
                                    // the audit compares like with like.
                                    let profileCatalog = Catalog.stripStaticPopulations catalog
                                    let! minted = profileMinted cnn profileCatalog
                                    match minted with
                                    | Error es -> return Result.failure es
                                    | Ok minted ->
                                        let! mainReport = auditMinted cnn sourcePacks mergedPack catalog minted
                                        match mainReport with
                                        | Error es -> return Result.failure es
                                        | Ok mainReport ->
                                            let! deepSections =
                                                deepAuditAll root config resolved.ServerConnectionString
                                                    (labelInputs merge.Inputs sourcePacks)
                                            match deepSections with
                                            | Error es -> return Result.failure es
                                            | Ok deepSections ->
                                                return Result.success (assembleRunReport root mainReport deepSections)
                            | Ok _ -> return Result.failureOf notUp
        }
