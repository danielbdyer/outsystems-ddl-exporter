namespace Twin.Runtime

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

    /// Publish the dacpac to the twin database (created when absent,
    /// upgraded in place otherwise — DacFx computes the minimal delta).
    let publish (masterConnStr: string) (dacpac: byte[]) : Task<Result<unit>> =
        task {
            try
                use stream = new MemoryStream(dacpac)
                use package = DacPackage.Load stream
                let services = DacServices masterConnStr
                do! Task.Run(fun () ->
                        services.Deploy(package, TwinContainer.TwinDatabaseName, true, deployOptions ()))
                return Result.success ()
            with ex ->
                return Result.failureOf (publishFailure ex.Message)
        }
