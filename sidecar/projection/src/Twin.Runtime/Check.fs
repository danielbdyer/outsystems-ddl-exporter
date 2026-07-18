namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Json
open Twin.Core

/// THE TWIN — the proof and the M5 companions (Twin.Runtime).
///
/// `Check.run` — the twin's self-check on a THROWAWAY database (never
/// the persistent twin): the estate models, publishes, lanes apply, the
/// mint lands with zero relationship orphans, a re-mint is
/// byte-identical (per-table digests), and every preserved categorical
/// the mint was shaped by re-profiles inside its declared vocabulary —
/// π ∘ σ ≈ id at the twin's grain. The throwaway database rides the
/// warm-honoring container acquisition (a fresh Testcontainers instance
/// when no warm connection is present) and is dropped afterward.
///
/// `Classify.run` — the kernel's PII proposer over the read-back estate,
/// written as the reviewable corrections artifact.
///
/// `Bake.run` — the `DockerImageEmitter` cash-out: a docker build
/// context (Dockerfile + dacpac + entrypoint + readme) for a
/// distributable image of the estate's schema.
[<RequireQualifiedAccess>]
module Check =

    type DistributionFinding = {
        Coordinate : string
        Detail     : string
    }

    type CheckReport = {
        TablesDefined       : int
        TablesLive          : int
        LanesApplied        : int
        TotalRows           : int64
        OrphanRows          : int64
        DeterministicRemint : bool
        Findings            : DistributionFinding list
    }

    let private openCnn (connStr: string) : Task<SqlConnection> =
        task {
            let cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            return cnn
        }

    let private scalar (cnn: SqlConnection) (sql: string) : Task<int64> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return if isNull v || v = box System.DBNull.Value then 0L else System.Convert.ToInt64 v
        }

    /// Orphan probe over the read-back catalog's own relationships: for
    /// every reference, children whose FK value finds no parent key.
    let private orphanCount (cnn: SqlConnection) (catalog: Catalog) : Task<int64> =
        task {
            let mutable total = 0L
            for kind in Catalog.allKinds catalog do
                for reference in kind.References do
                    let sourceColumn =
                        kind.Attributes
                        |> List.tryFind (fun a -> a.SsKey = reference.SourceAttribute)
                    let parent = Catalog.tryFindKind reference.TargetKind catalog
                    match sourceColumn, parent with
                    | Some fk, Some parentKind ->
                        match parentKind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey) with
                        | None -> ()
                        | Some pk ->
                            let sql =
                                System.String.Concat(
                                    "SELECT COUNT_BIG(*) FROM ",
                                    Projection.Targets.SSDT.Render.tableQualified kind.Physical,
                                    " c LEFT JOIN ",
                                    Projection.Targets.SSDT.Render.tableQualified parentKind.Physical,
                                    " p ON p.", Projection.Targets.SSDT.Render.quote (ColumnRealization.columnNameText pk.Column),
                                    " = c.", Projection.Targets.SSDT.Render.quote (ColumnRealization.columnNameText fk.Column),
                                    " WHERE c.", Projection.Targets.SSDT.Render.quote (ColumnRealization.columnNameText fk.Column),
                                    " IS NOT NULL AND p.", Projection.Targets.SSDT.Render.quote (ColumnRealization.columnNameText pk.Column),
                                    " IS NULL;")  // LINT-ALLOW: terminal SQL-text probe; identifiers pass through the SSDT renderer's quoting
                            let! n = scalar cnn sql
                            total <- total + n
                    | _ -> ()
            return total
        }

    /// Per-table content digests (order-independent): the determinism
    /// ruler for the re-mint comparison.
    let private digests (cnn: SqlConnection) (catalog: Catalog) : Task<Map<string, int64>> =
        task {
            let mutable acc = Map.empty
            for kind in Catalog.allKinds catalog do
                let table = Projection.Targets.SSDT.Render.tableQualified kind.Physical
                let! d = scalar cnn (System.String.Concat("SELECT COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(*)), 0) FROM ", table, ";"))  // LINT-ALLOW: terminal SQL-text probe; the table identifier passes through the SSDT renderer's quoting
                acc <- Map.add table d acc
            return acc
        }

    /// Preserved-vocabulary findings: for every categorical the mint was
    /// shaped by whose mode is Preserve, the minted values must be a
    /// subset of the declared vocabulary.
    let private vocabularyFindings
        (cnn: SqlConnection)
        (catalog: Catalog)
        (plan: Mint.MintPlan)
        : Task<DistributionFinding list> =
        task {
            let index = CatalogIndex.ofCatalog catalog
            let byAttr =
                CatalogIndex.kinds index
                |> List.collect (fun (_, k) -> k.Attributes |> List.map (fun a -> a.SsKey, (k, a)))
                |> Map.ofList
            let findings = System.Collections.Generic.List<DistributionFinding>()
            for dist in plan.Profile.Distributions do
                match dist with
                | AttributeDistribution.Categorical cat when
                    Set.contains
                        (byAttr |> Map.tryFind cat.AttributeKey |> Option.map (fun (_, a) -> Name.value a.Name) |> Option.defaultValue "")
                        plan.Config.PreserveColumns ->
                    match Map.tryFind cat.AttributeKey byAttr with
                    | None -> ()
                    | Some (kind, attr) ->
                        let table = Projection.Targets.SSDT.Render.tableQualified kind.Physical
                        let column = Projection.Targets.SSDT.Render.quote (ColumnRealization.columnNameText attr.Column)
                        let vocabulary = cat.Frequencies |> List.map fst |> Set.ofList
                        use cmd = cnn.CreateCommand()
                        cmd.CommandText <- System.String.Concat("SELECT DISTINCT ", column, " FROM ", table, " WHERE ", column, " IS NOT NULL;")  // LINT-ALLOW: terminal SQL-text probe; identifiers pass through the SSDT renderer's quoting
                        use! reader = cmd.ExecuteReaderAsync()
                        let observed = System.Collections.Generic.List<string>()
                        let mutable more = true
                        while more do
                            let! has = reader.ReadAsync()
                            if has then observed.Add(reader.GetValue(0) |> string) else more <- false
                        let outside = observed |> Seq.filter (fun v -> not (Set.contains v vocabulary)) |> List.ofSeq
                        if not (List.isEmpty outside) then
                            findings.Add
                                { Coordinate = System.String.Concat(TableCoordinate.text (TwinIdentity.coordinateOfKind kind), ".", ColumnRealization.columnNameText attr.Column)  // LINT-ALLOW: terminal finding coordinate text
                                  Detail = System.String.Concat(string (List.length outside), " minted values fall outside the declared vocabulary") }  // LINT-ALLOW: terminal finding detail text
                | _ -> ()
            return List.ofSeq findings
        }

    /// The proof, end to end, on a throwaway database.
    let run (root: string) (config: TwinConfig) (scenarioName: string) : Task<Result<CheckReport>> =
        task {
            match EstateFiles.resolve root config.Estate with
            | Error es -> return Result.failure es
            | Ok estate ->
                match TwinConfig.resolveScenario config scenarioName with
                | Error es -> return Result.failure es
                | Ok _ ->
                    match EstateModel.buildDacpac estate with
                    | Error es -> return Result.failure es
                    | Ok dacpac ->
                        let! handle = Deploy.acquireContainer ()
                        try
                            let dbName = System.String.Concat("TwinCheck_", System.Guid.NewGuid().ToString("N").Substring(0, 12))  // LINT-ALLOW: terminal throwaway database name
                            let builder = SqlConnectionStringBuilder handle.MasterConnectionString
                            let! published = EstateModel.publishTo builder.ConnectionString dbName dacpac
                            match published with
                            | Error es -> return Result.failure es
                            | Ok () ->
                                builder.InitialCatalog <- dbName
                                use! cnn = openCnn builder.ConnectionString
                                try
                                    let! lanes = TwinDatabase.applyStaticLanes cnn estate
                                    match lanes with
                                    | Error es -> return Result.failure es
                                    | Ok lanesApplied ->
                                        let! readBack = Readback.read cnn
                                        match readBack with
                                        | Error es -> return Result.failure es
                                        | Ok catalog ->
                                            let pools = Readback.providedPools catalog
                                            let mintCatalog = Catalog.stripStaticPopulations catalog
                                            match Mint.prepare root config scenarioName mintCatalog pools with
                                            | Error es -> return Result.failure es
                                            | Ok plan ->
                                                let! minted = Mint.run cnn mintCatalog plan
                                                match minted with
                                                | Error es -> return Result.failure es
                                                | Ok _ ->
                                                    let! first = digests cnn mintCatalog
                                                    let! reminted = Mint.run cnn mintCatalog plan
                                                    match reminted with
                                                    | Error es -> return Result.failure es
                                                    | Ok _ ->
                                                        let! second = digests cnn mintCatalog
                                                        let! orphans = orphanCount cnn catalog
                                                        let! rows = TwinDatabase.totalRows cnn
                                                        let! findings = vocabularyFindings cnn mintCatalog plan
                                                        let counts = EstateDefinition.counts estate
                                                        return
                                                            Result.success
                                                                { TablesDefined = counts.Tables
                                                                  TablesLive = List.length (Catalog.allKinds catalog)
                                                                  LanesApplied = lanesApplied
                                                                  TotalRows = rows
                                                                  OrphanRows = orphans
                                                                  DeterministicRemint = (first = second)
                                                                  Findings = findings }
                                finally
                                    // Drop the throwaway database, always.
                                    try
                                        use master = new SqlConnection(handle.MasterConnectionString)
                                        master.Open()
                                        use cmd = master.CreateCommand()
                                        cmd.CommandText <-
                                            System.String.Concat(
                                                "IF DB_ID(N'", dbName, "') IS NOT NULL BEGIN ALTER DATABASE ",
                                                Projection.Targets.SSDT.Render.quote dbName,
                                                " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ",
                                                Projection.Targets.SSDT.Render.quote dbName, "; END")  // LINT-ALLOW: terminal throwaway-cleanup SQL; the generated name passes through the SSDT renderer's quoting
                                        cmd.ExecuteNonQuery() |> ignore
                                    with _ -> ()
                        finally
                            (handle.DisposeAsync()).GetAwaiter().GetResult()
        }

[<RequireQualifiedAccess>]
module Classify =

    [<Literal>]
    let DefaultCorrectionsPath = "twin/corrections.json"

    type ClassifyReport = {
        Path       : string
        Classified : int
        ConfigSet  : bool
    }

    /// Propose PII classifications from the read-back estate and write
    /// the reviewable corrections artifact.
    let run (root: string) (config: TwinConfig) (twinCatalog: Catalog) : Result<ClassifyReport> =
        let proposed = CorrectionProposer.propose (Catalog.stripStaticPopulations twinCatalog)
        let rel = defaultArg config.CorrectionsPath DefaultCorrectionsPath
        let full = System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar))
        match System.IO.Path.GetDirectoryName full with
        | null | "" -> ()
        | dir -> System.IO.Directory.CreateDirectory dir |> ignore
        System.IO.File.WriteAllText(full, CorrectionCodec.serialize proposed)
        Result.success
            { Path = full
              Classified = List.length (Correction.entries proposed)
              ConfigSet = config.CorrectionsPath.IsSome }

[<RequireQualifiedAccess>]
module Bake =

    type BakeReport = {
        Directory : string
        Files     : string list
    }

    /// The `data-twin` deferral's distributable form: a docker build
    /// context for the estate's schema image, written to `twin/bake/`.
    /// (Schema-only by DacFx construction — run the lanes + `twin seed`
    /// against a started container, or publish evidence with the image.)
    let run (root: string) (twinCatalog: Catalog) : Result<BakeReport> =
        match Projection.Targets.SSDT.DockerImageEmitter.emit (Catalog.stripStaticPopulations twinCatalog) with
        | Error es -> Result.failure es
        | Ok context ->
            let dir = System.IO.Path.Combine(root, "twin", "bake")
            System.IO.Directory.CreateDirectory dir |> ignore
            let write (nameText: string) (content: string) =
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, nameText), content)
            write "Dockerfile" context.Dockerfile
            write "entrypoint.sh" context.EntrypointScript
            write "README.md" context.Readme
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "catalog.dacpac"), context.DacpacBytes)
            Result.success
                { Directory = dir
                  Files = [ "Dockerfile"; "entrypoint.sh"; "README.md"; "catalog.dacpac" ] }
