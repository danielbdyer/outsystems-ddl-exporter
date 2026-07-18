namespace Twin.Runtime
// LINT-ALLOW-FILE: the Twin's run-store driver — persists and lists run records
//   on disk. `String.Concat` composes terminal run-directory paths / run-id tags
//   at the filesystem boundary (the concatenated segment IS the artifact key);
//   considered `Path.Combine`, rejected because it emits platform-specific
//   separators that break T1 cross-platform byte-determinism. I/O-boundary.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Twin.Core

/// THE TWIN — the runs (Twin.Runtime).
///
/// The verb orchestrations: `up` (converge everything), `seed` (force a
/// fresh mint), `status` (read-only), `down`, `reset`. Each returns a
/// typed report the CLI renders in the VOICE register; nothing here
/// prints.
///
/// Convergence (law 1) rides two fingerprints stored in the twin:
///   schema plane — the estate files + the schema-relevant config;
///   data plane   — the schema fingerprint's inputs plus the evidence
///     and correction artifacts, the mint config, the scenario chain,
///     and the effective seed.
/// `up` skips each plane whose fingerprint matches; `seed` always
/// re-mints. A mint starts from a clean slate — every estate table is
/// wiped (nullable deferred-FK columns nulled first, then child-first
/// deletes through the kernel's own wipe), the estate's static lanes are
/// re-applied, and the lane-seeded kinds become the K1 provided pools by
/// observation: after the wipe, a row-carrying kind is lane-seeded by
/// construction. No configuration names the seeded set; the estate
/// itself does.
[<RequireQualifiedAccess>]
module Runs =

    [<Literal>]
    let ToolVersion = "0.1.0"

    type StatusReport = {
        Container        : TwinContainer.ContainerState
        DatabasePresent  : bool
        DefinedTables    : int
        DefinedLanes     : int
        SchemaCurrent    : bool option
        DataCurrent      : bool option
        StoredScenario   : string option
        StoredSeed       : uint64 option
        StoredMintedRows : int64 option
        LiveTables       : int option
        LiveRows         : int64 option
    }

    type MaterializeReport = {
        SchemaPublished : bool
        LanesApplied    : int
        ProvidedKinds   : int
        MintedKinds     : int
        Scenario        : string
        Seed            : uint64
        TotalRows       : int64
        DefinedTables   : int
        UnsatisfiableFks : int
    }

    type UpOutcome =
        | NothingToApply of tables: int * rows: int64 * scenario: string
        | Materialized of MaterializeReport

    // ------------------------------------------------------------------
    // Fingerprint assembly.
    // ------------------------------------------------------------------

    let private tryArtifactContribution (root: string) (name: string) (rel: string option) : Fingerprint.Contribution list =
        match rel with
        | None -> []
        | Some r ->
            let cleaned = if r.StartsWith "file:" then r.Substring 5 else r
            let full = System.IO.Path.Combine(root, cleaned.Replace('/', System.IO.Path.DirectorySeparatorChar))
            if System.IO.File.Exists full then
                try [ { Fingerprint.Contribution.Name = name; Fingerprint.Contribution.Content = System.IO.File.ReadAllText full } ]
                with _ -> []
            else []

    let private schemaFingerprint (config: TwinConfig) (estate: EstateDefinition) : Fingerprint =
        let contributions =
            { Fingerprint.Contribution.Name = "config/estate"; Fingerprint.Contribution.Content = TwinConfig.canonicalEstate config }
            :: EstateFiles.contributions estate
        Fingerprint.compute ToolVersion "schema" 0UL contributions

    let private dataFingerprint
        (root: string)
        (config: TwinConfig)
        (estate: EstateDefinition)
        (scenarioName: string)
        (effectiveSeed: uint64)
        : Fingerprint =
        let contributions =
            [ { Fingerprint.Contribution.Name = "config/estate"; Fingerprint.Contribution.Content = TwinConfig.canonicalEstate config }
              { Fingerprint.Contribution.Name = "config/mint"; Fingerprint.Contribution.Content = TwinConfig.canonicalMint config scenarioName } ]
            @ EstateFiles.contributions estate
            @ tryArtifactContribution root "artifact/corrections" config.CorrectionsPath
            @ tryArtifactContribution root "artifact/evidence.shape" config.Evidence.ShapePath
            @ tryArtifactContribution root "artifact/evidence.rich" config.Evidence.RichRef
        Fingerprint.compute ToolVersion scenarioName effectiveSeed contributions

    // ------------------------------------------------------------------
    // The clean-slate wipe (mint precondition).
    // ------------------------------------------------------------------

    let private wipeAll (twinCnn: SqlConnection) (catalog: Catalog) : Task<Result<unit>> =
        task {
            try
                let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle catalog).Value
                let plan = DataLoadPlan.build catalog topo Map.empty SurrogateRemapContext.empty
                // Nullable deferred-FK columns (the broken cycle edges) are
                // nulled first so the child-first deletes cannot be blocked
                // by an intra-cycle reference.
                for load in plan.Loads do
                    if not (Set.isEmpty load.DeferredFkColumns) then
                        match Catalog.tryFindKind load.Kind catalog with
                        | None -> ()
                        | Some kind ->
                            let nullable =
                                kind.Attributes
                                |> List.filter (fun a ->
                                    Set.contains a.Name load.DeferredFkColumns && a.Column.IsNullable)
                            for a in nullable do
                                do! Deploy.executeBatch twinCnn
                                        (System.String.Concat(
                                            "UPDATE ",
                                            Projection.Targets.SSDT.Render.tableQualified kind.Physical,
                                            " SET ",
                                            Projection.Targets.SSDT.Render.quote (ColumnRealization.columnNameText a.Column),
                                            " = NULL;"))  // LINT-ALLOW: terminal SQL-text boundary; identifiers pass through the SSDT renderer's quoting
                do! TransferResume.wipeFkOrdered twinCnn catalog plan topo None
                // Identity counters reset to the declared seed, so a re-mint
                // lands byte-identical keys (T1 at the twin's grain). SQL
                // Server's RESEED semantics differ between a never-inserted
                // table (the next row uses the reseed value as-is) and a
                // deleted-out table (reseed value + increment); the
                // `last_value IS NOT NULL` guard reseeds only the second
                // case, to seed − increment, so the next identity equals the
                // declared seed on both paths.
                for kind in Catalog.allKinds catalog do
                    match kind.Attributes |> List.tryFind (fun a -> a.IsIdentity) with
                    | None -> ()
                    | Some identityAttr ->
                        let seedValue, increment =
                            defaultArg identityAttr.Column.Identity (1L, 1L)
                        let table = Projection.Targets.SSDT.Render.tableQualified kind.Physical
                        do! Deploy.executeBatch twinCnn
                                (System.String.Concat(
                                    "IF EXISTS (SELECT 1 FROM sys.identity_columns WHERE [object_id] = OBJECT_ID(N'",
                                    table.Replace("'", "''"),
                                    "') AND [last_value] IS NOT NULL) DBCC CHECKIDENT (N'",
                                    table.Replace("'", "''"),
                                    "', RESEED, ",
                                    string (seedValue - increment),
                                    ");"))  // LINT-ALLOW: terminal SQL-text boundary; the table identifier passes through the SSDT renderer's quoting and is string-literal-escaped for the OBJECT_ID/DBCC name arguments
                return Result.success ()
            with ex ->
                return
                    Result.failureOf
                        (ValidationError.createWithMetadata
                            "twin.wipe.failed"
                            "The pre-mint wipe did not complete."
                            (Map.ofList [ "detail", Some ex.Message ]))
        }

    // ------------------------------------------------------------------
    // The materialization (shared by up and seed).
    // ------------------------------------------------------------------

    let private openCnn (connStr: string) : Task<SqlConnection> =
        task {
            let cnn = new SqlConnection(connStr)
            do! cnn.OpenAsync()
            return cnn
        }

    let private materialize
        (root: string)
        (config: TwinConfig)
        (scenarioName: string)
        (publishSchema: bool)
        (schemaFp: Fingerprint)
        (dataFp: Fingerprint)
        (estate: EstateDefinition)
        (password: string)
        : Task<Result<MaterializeReport>> =
        task {
            let masterConnStr = TwinContainer.masterConnectionString config.Container password
            let twinConnStr = TwinContainer.twinConnectionString config.Container password
            // Schema plane.
            let! published =
                task {
                    if not publishSchema then return Result.success false
                    else
                        match EstateModel.buildDacpac estate with
                        | Error es -> return Result.failure es
                        | Ok dacpac ->
                            let! deployed = EstateModel.publish masterConnStr dacpac
                            match deployed with
                            | Error es -> return Result.failure es
                            | Ok () ->
                                use! twinCnn = openCnn twinConnStr
                                let! ensured = TwinDatabase.ensureState twinCnn
                                match ensured with
                                | Error es -> return Result.failure es
                                | Ok () ->
                                    let! wrote = TwinDatabase.writeSchemaState twinCnn schemaFp
                                    return wrote |> Result.map (fun () -> true)
                }
            match published with
            | Error es -> return Result.failure es
            | Ok schemaPublished ->
                // Data plane — clean slate, lanes, pools by observation, mint.
                use! twinCnn = openCnn twinConnStr
                let! ensured = TwinDatabase.ensureState twinCnn
                match ensured with
                | Error es -> return Result.failure es
                | Ok () ->
                    let! schemaCatalog = Readback.readSchema twinCnn
                    match schemaCatalog with
                    | Error es -> return Result.failure es
                    | Ok bareCatalog ->
                        let! wiped = wipeAll twinCnn bareCatalog
                        match wiped with
                        | Error es -> return Result.failure es
                        | Ok () ->
                            let! lanes = TwinDatabase.applyStaticLanes twinCnn estate
                            match lanes with
                            | Error es -> return Result.failure es
                            | Ok lanesApplied ->
                                let! readBack = Readback.read twinCnn
                                match readBack with
                                | Error es -> return Result.failure es
                                | Ok catalog ->
                                    let pools = Readback.providedPools catalog
                                    // The mint operates on the schema-plane catalog
                                    // (populations stripped — σ never reads them; the
                                    // pools carry what matters).
                                    let mintCatalog = Catalog.stripStaticPopulations catalog
                                    match Mint.prepare root config scenarioName mintCatalog pools with
                                    | Error es -> return Result.failure es
                                    | Ok plan ->
                                        let! minted = Mint.run twinCnn mintCatalog plan
                                        match minted with
                                        | Error es -> return Result.failure es
                                        | Ok report ->
                                            let! totalRows = TwinDatabase.totalRows twinCnn
                                            let! wrote =
                                                TwinDatabase.writeDataState twinCnn dataFp scenarioName plan.Seed totalRows
                                            match wrote with
                                            | Error es -> return Result.failure es
                                            | Ok () ->
                                                let counts = EstateDefinition.counts estate
                                                return
                                                    Result.success
                                                        { SchemaPublished = schemaPublished
                                                          LanesApplied = lanesApplied
                                                          ProvidedKinds = Map.count pools
                                                          MintedKinds =
                                                              Catalog.allKinds mintCatalog
                                                              |> List.filter (fun k -> not (Map.containsKey k.SsKey pools))
                                                              |> List.length
                                                          Scenario = scenarioName
                                                          Seed = plan.Seed
                                                          TotalRows = totalRows
                                                          DefinedTables = counts.Tables
                                                          UnsatisfiableFks = List.length report.SyntheticUnsatisfiableFks }
        }

    /// The one-click converge. Fingerprint match on both planes is the
    /// fast no-op; otherwise each stale plane is brought current.
    let up (root: string) (config: TwinConfig) (scenarioName: string) (force: bool) : Task<Result<UpOutcome>> =
        task {
            match EstateFiles.resolve root config.Estate with
            | Error es -> return Result.failure es
            | Ok estate ->
                match TwinConfig.resolveScenario config scenarioName with
                | Error es -> return Result.failure es
                | Ok scenario ->
                    match TwinContainer.resolvePassword config.Container.PasswordRef with
                    | Error es -> return Result.failure es
                    | Ok password ->
                        let schemaFp = schemaFingerprint config estate
                        let _, seed = Mint.effectiveScaleSeed config (TwinConfig.scenarioChain config scenarioName)
                        let dataFp = dataFingerprint root config estate scenarioName seed
                        let! running = TwinContainer.ensureRunning config.Container password
                        match running with
                        | Error es -> return Result.failure es
                        | Ok () ->
                            use! masterCnn = openCnn (TwinContainer.masterConnectionString config.Container password)
                            let! dbExists = TwinDatabase.databaseExists masterCnn
                            let! stored =
                                task {
                                    if not dbExists then return TwinDatabase.emptyState
                                    else
                                        use! twinCnn = openCnn (TwinContainer.twinConnectionString config.Container password)
                                        return! TwinDatabase.readState twinCnn
                                }
                            let schemaCurrent = stored.SchemaFingerprint = Some (Fingerprint.value schemaFp)
                            let dataCurrent = stored.DataFingerprint = Some (Fingerprint.value dataFp)
                            if schemaCurrent && dataCurrent && not force then
                                return
                                    Result.success
                                        (NothingToApply (
                                            (EstateDefinition.counts estate).Tables,
                                            defaultArg stored.MintedRows 0L,
                                            defaultArg stored.Scenario scenarioName))
                            else
                                let! report =
                                    materialize root config scenarioName (not schemaCurrent) schemaFp dataFp estate password
                                return report |> Result.map Materialized
        }

    /// Force a fresh mint (schema converged first when stale).
    let seed (root: string) (config: TwinConfig) (scenarioName: string) : Task<Result<UpOutcome>> =
        up root config scenarioName true

    /// Read-only: what the twin holds against what the repo defines.
    let status (root: string) (config: TwinConfig) (scenarioName: string) : Task<Result<StatusReport>> =
        task {
            match EstateFiles.resolve root config.Estate with
            | Error es -> return Result.failure es
            | Ok estate ->
                match TwinConfig.resolveScenario config scenarioName with
                | Error es -> return Result.failure es
                | Ok scenario ->
                    match TwinContainer.resolvePassword config.Container.PasswordRef with
                    | Error es -> return Result.failure es
                    | Ok password ->
                        let counts = EstateDefinition.counts estate
                        let schemaFp = schemaFingerprint config estate
                        let _, seed = Mint.effectiveScaleSeed config (TwinConfig.scenarioChain config scenarioName)
                        let dataFp = dataFingerprint root config estate scenarioName seed
                        let! containerState = TwinContainer.state config.Container
                        match containerState with
                        | Error es -> return Result.failure es
                        | Ok state ->
                            match state with
                            | TwinContainer.Running ->
                                use! masterCnn = openCnn (TwinContainer.masterConnectionString config.Container password)
                                let! dbExists = TwinDatabase.databaseExists masterCnn
                                if not dbExists then
                                    return
                                        Result.success
                                            { Container = state; DatabasePresent = false
                                              DefinedTables = counts.Tables; DefinedLanes = counts.StaticData
                                              SchemaCurrent = Some false; DataCurrent = Some false
                                              StoredScenario = None; StoredSeed = None; StoredMintedRows = None
                                              LiveTables = None; LiveRows = None }
                                else
                                    use! twinCnn = openCnn (TwinContainer.twinConnectionString config.Container password)
                                    let! stored = TwinDatabase.readState twinCnn
                                    let! rows = TwinDatabase.totalRows twinCnn
                                    let! live = Readback.readSchema twinCnn
                                    let liveTables =
                                        match live with
                                        | Ok c -> Some (List.length (Catalog.allKinds c))
                                        | Error _ -> None
                                    return
                                        Result.success
                                            { Container = state; DatabasePresent = true
                                              DefinedTables = counts.Tables; DefinedLanes = counts.StaticData
                                              SchemaCurrent = Some (stored.SchemaFingerprint = Some (Fingerprint.value schemaFp))
                                              DataCurrent = Some (stored.DataFingerprint = Some (Fingerprint.value dataFp))
                                              StoredScenario = stored.Scenario; StoredSeed = stored.Seed
                                              StoredMintedRows = stored.MintedRows
                                              LiveTables = liveTables; LiveRows = Some rows }
                            | _ ->
                                return
                                    Result.success
                                        { Container = state; DatabasePresent = false
                                          DefinedTables = counts.Tables; DefinedLanes = counts.StaticData
                                          SchemaCurrent = None; DataCurrent = None
                                          StoredScenario = None; StoredSeed = None; StoredMintedRows = None
                                          LiveTables = None; LiveRows = None }
        }

    /// Stop the container; state preserved.
    let down (config: TwinConfig) : Task<Result<unit>> =
        TwinContainer.stop config.Container

    /// Remove the container and everything in it.
    let reset (config: TwinConfig) : Task<Result<unit>> =
        TwinContainer.remove config.Container
