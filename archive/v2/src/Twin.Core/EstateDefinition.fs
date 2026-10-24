namespace Twin.Core

open Projection.Core

/// THE TWIN — the estate definition (THE_TWIN.md §language).
///
/// The pure model of what the SSDT repository defines: the table
/// definition scripts, the schema scripts, and the repo's own static
/// reference-data lanes. File **contents** arrive here already read
/// (Twin.Runtime's `EstateFiles` owns the file system); this module owns
/// the invariants — non-empty, duplicate-free, deterministically ordered —
/// so everything downstream (the DacFx model build, the fingerprint) is
/// order-stable by construction.
type EstateFile = {
    /// Repo-relative path with forward slashes — the deterministic sort
    /// key and the operator-facing name of the file.
    RelativePath : string
    /// Full file text (UTF-8 decoded).
    Content      : string
}

/// The estate definition: what the repository says the database is.
type EstateDefinition = private {
    tables     : EstateFile list
    schemas    : EstateFile list
    staticData : EstateFile list
}

[<RequireQualifiedAccess>]
module EstateDefinition =

    let private noTables =
        ValidationError.create
            "twin.estate.empty"
            "The estate definition carries no table scripts. Check the estate.tables pattern against the repository."

    let private byPath (f: EstateFile) : string = f.RelativePath.ToLowerInvariant()

    let private duplicates (category: string) (files: EstateFile list) : ValidationError list =
        Validation.duplicateKeyErrors
            "twin.estate.duplicatePath"
            (fun key -> System.String.Concat("The estate lists the same ", category, " file twice: ", key, "."))  // LINT-ALLOW: terminal refusal message naming the duplicate path; static phrase + the offending key
            byPath
            files

    /// Build the estate definition. Table scripts are required; schema
    /// scripts and static-data lanes may be absent. Every category is
    /// sorted by lower-cased relative path so downstream consumers are
    /// deterministic regardless of file-system enumeration order.
    let create
        (tables: EstateFile list)
        (schemas: EstateFile list)
        (staticData: EstateFile list)
        : Result<EstateDefinition> =
        let errors =
            Validation.nonEmpty "twin.estate.empty" noTables.Message tables
            @ duplicates "table" tables
            @ duplicates "schema" schemas
            @ duplicates "static-data" staticData
        if not (List.isEmpty errors) then Result.failure errors
        else
            Result.success
                { tables     = tables     |> List.sortBy byPath
                  schemas    = schemas    |> List.sortBy byPath
                  staticData = staticData |> List.sortBy byPath }

    /// Table definition scripts, path-sorted.
    let tables (e: EstateDefinition) : EstateFile list = e.tables

    /// Schema scripts (`CREATE SCHEMA` for non-dbo estates), path-sorted.
    let schemas (e: EstateDefinition) : EstateFile list = e.schemas

    /// The repo's static reference-data lanes, path-sorted. Applied to
    /// the twin as-is after schema publish — this is the estate's own
    /// data, never synthetic.
    let staticData (e: EstateDefinition) : EstateFile list = e.staticData

    /// Every file in the definition, category order (schemas → tables →
    /// static data), path-sorted within each — the fingerprint's file
    /// enumeration.
    let allFiles (e: EstateDefinition) : EstateFile list =
        e.schemas @ e.tables @ e.staticData

    /// The count surfaces the VOICE report leads with.
    let counts (e: EstateDefinition) : {| Tables: int; Schemas: int; StaticData: int |} =
        {| Tables = List.length e.tables
           Schemas = List.length e.schemas
           StaticData = List.length e.staticData |}
