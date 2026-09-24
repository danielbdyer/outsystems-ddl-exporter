namespace Twin.Runtime
// LINT-ALLOW-FILE-MUTATION: the Twin's estate-model file reader — the mutable
//   locals are a single-pass accumulator over the on-disk model files (an
//   imperative fold at the file-read boundary, isolated and pure-pool-testable,
//   no escape). I/O-boundary read; no pure-functional equivalent for the
//   sequential file walk.

open System.IO
open System.Threading.Tasks
open Microsoft.SqlServer.Dac
open Microsoft.SqlServer.Dac.Model
open Projection.Core
open Twin.Core

/// THE TWIN — the estate model (Twin.Runtime).
///
/// Builds the repository's table definitions into a DacFx model +
/// `.dacpac`, and publishes it to the twin database. The build mirrors
/// the kernel's `DacpacEmitter` exactly (`TSqlModel.AddObjects` per
/// script, `DacPackageExtensions.BuildPackage` to bytes) — but ingests
/// the estate's own authored files rather than an emitted statement
/// stream, so a repo that fails to model is refused with the offending
/// file named. Publish is `DacServices.Deploy` with the twin posture:
/// the twin mirrors the repo (`DropObjectsNotInSource`), data loss is
/// acceptable by definition (every row is re-mintable), and security
/// objects are left alone (the ssdt-playbook's standard exclusions).
[<RequireQualifiedAccess>]
module EstateModel =

    let private modelFailure (path: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.estate.model"
            "An estate file could not be added to the database model. The repository does not build; correct the script and rerun."
            (Map.ofList [ "path", Some path; "detail", Some detail ])

    let private packageFailure (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.estate.package"
            "The database model did not validate as a package."
            (Map.ofList [ "detail", Some detail ])

    let private publishFailure (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.publish.failed"
            "The schema publish did not succeed."
            (Map.ofList [ "detail", Some detail ])

    /// Build the estate's `.dacpac` bytes. Schema scripts ingest before
    /// table scripts (a `CREATE SCHEMA` must model before its tables);
    /// static-data lanes never enter the model (they are executed, not
    /// modeled — the DacFx package is schema-only by construction, the
    /// kernel's documented DacFx limitation).
    let buildDacpac (estate: EstateDefinition) : Result<byte[]> =
        try
            use model = new TSqlModel(SqlServerVersion.Sql160, TSqlModelOptions())
            let ingest (f: EstateFile) : ValidationError list =
                try
                    if System.String.IsNullOrWhiteSpace f.Content then []
                    else
                        model.AddObjects f.Content
                        []
                with ex -> [ modelFailure f.RelativePath ex.Message ]
            let errors =
                (EstateDefinition.schemas estate @ EstateDefinition.tables estate)
                |> List.collect ingest
            match errors with
            | _ :: _ -> Result.failure errors
            | [] ->
                use stream = new MemoryStream()
                let metadata = PackageMetadata(Name = "Twin", Description = "The twin's estate model", Version = "1.0.0.0")
                DacPackageExtensions.BuildPackage(stream, model, metadata)
                Result.success (stream.ToArray())
        with ex ->
            Result.failureOf (packageFailure ex.Message)

    /// The twin publish posture. Named once; THE_TWIN.md documents each
    /// choice.
    let private deployOptions () : DacDeployOptions =
        let options = DacDeployOptions()
        options.BlockOnPossibleDataLoss <- false
        options.DropObjectsNotInSource <- true
        // The ssdt-playbook's standard exclusions: never manage security
        // principals from a schema publish.
        options.DoNotDropObjectTypes <-
            [| ObjectType.Users; ObjectType.Logins; ObjectType.RoleMembership; ObjectType.Permissions |]
        options

    /// The PRODUCTION-FAITHFUL publish posture — the deployment a real
    /// environment runs, mirroring
    /// `ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml`.
    /// Unlike the twin posture above, `BlockOnPossibleDataLoss` is ON, so
    /// SSDT emits its row-presence guard (`IF EXISTS(...) RAISERROR(... data
    /// loss ...)`) above a NULL→NOT NULL `ALTER COLUMN` and REFUSES the change
    /// while the table holds rows — even after every NULL is backfilled — and
    /// likewise refuses a truncating narrow or a populated drop. Smart-
    /// defaults stay OFF so the refusal is preserved, not silently repaired.
    /// The one deliberate deviation from the strict profile:
    /// `DropObjectsNotInSource` is OFF, because the target is the twin
    /// database, which carries bookkeeping the estate does not declare
    /// (`__state`) — a schema publish must leave it, and every other object,
    /// alone and surface only the estate delta under test.
    let private strictDeployOptions () : DacDeployOptions =
        let options = DacDeployOptions()
        options.BlockOnPossibleDataLoss <- true
        options.GenerateSmartDefaults <- false
        options.IgnoreColumnOrder <- true
        options.DropObjectsNotInSource <- false
        options.IncludeTransactionalScripts <- true
        options.AllowIncompatiblePlatform <- false
        options.IgnorePermissions <- true
        options

    /// The shared DacFx deploy body: load the package, deploy with the given
    /// options, translate any failure into the named publish refusal.
    let private deployPackage
        (options: DacDeployOptions)
        (masterConnStr: string)
        (databaseName: string)
        (dacpac: byte[])
        : Task<Result<unit>> =
        task {
            try
                use stream = new MemoryStream(dacpac)
                use package = DacPackage.Load stream
                let services = DacServices masterConnStr
                do! Task.Run(fun () ->
                        services.Deploy(package, databaseName, true, options))
                return Result.success ()
            with ex ->
                return Result.failureOf (publishFailure ex.Message)
        }

    /// Publish the dacpac to a named database (created when absent,
    /// upgraded in place otherwise — DacFx computes the minimal delta).
    let publishTo (masterConnStr: string) (databaseName: string) (dacpac: byte[]) : Task<Result<unit>> =
        deployPackage (deployOptions ()) masterConnStr databaseName dacpac

    /// Publish to the twin database.
    let publish (masterConnStr: string) (dacpac: byte[]) : Task<Result<unit>> =
        publishTo masterConnStr TwinContainer.TwinDatabaseName dacpac

    /// Publish to the twin database with the PRODUCTION-FAITHFUL (strict)
    /// posture — the deployment a real environment would run. A change whose
    /// `.sql` text is harmless but whose data is not (NOT NULL over rows, a
    /// truncating narrow, a populated drop) is REFUSED here exactly as
    /// production refuses it. The Twin's own `Runs.up` deliberately relaxes
    /// this (it is disposable and re-mintable); the sample-PR proofs use this
    /// to demonstrate what ships to a real environment. Reuses the same DacFx
    /// Deploy path as `publish`.
    let publishStrict (masterConnStr: string) (dacpac: byte[]) : Task<Result<unit>> =
        deployPackage (strictDeployOptions ()) masterConnStr TwinContainer.TwinDatabaseName dacpac
