namespace Projection.Pipeline

open System.IO
open System.Text.Json
open Projection.Core
open Projection.Targets.Data
open FsToolkit.ErrorHandling

/// The migration-dependency file adapter (the boundary the
/// `MigrationDependenciesEmitter` docstring deferred until a real
/// ingestion path surfaced — that path is now the config-driven
/// full-export run). Reads the operator-curated row inventory at
/// `overrides.migrationDependencies.path` into the typed
/// `MigrationDependencyContext` the composer threads to the emitter.
///
/// **File format (JSON, logical-keyed; operator decision 2026-06-15).**
/// The operator authors rows under the logical `Module.Entity`
/// coordinate — no raw `SsKey` GUIDs — and each row carries a stable
/// `id` plus logical-column → raw-value cells (the same raw-string
/// surface as `StaticRow.Values`; `RawValueCodec` applies the
/// per-column type at MERGE construction; `""` is NULL):
///
/// ```json
/// {
///   "kinds": [
///     {
///       "module": "ServiceCenter",
///       "entity": "Role",
///       "rows": [
///         { "id": "Admin",   "values": { "Id": "1", "Label": "Administrator" } },
///         { "id": "Auditor", "values": { "Id": "2", "Label": "Auditor" } }
///       ]
///     }
///   ]
/// }
/// ```
///
/// The logical `(module, entity)` resolves to the kind's `SsKey` via
/// the shared `CatalogResolution.tryKindByLogical` (rename-invariant,
/// A1 — resolution against the pre-rename catalog is sound because the
/// `SsKey` is what flows downstream). The row `id` synthesizes a stable
/// `Identifier` (`SsKey.synthesizedComposite "migration" [module;
/// entity; id]`) so cross-version diffs and re-publication track per
/// the `MigrationDependencyRow.Identifier` contract.
///
/// **Fail loud, never silent (standing law §4).** No path ⇒ the empty
/// context (no-op; byte-identical to the prior `MigrationDependency
/// Context.empty` threading). A path that is set but unreadable /
/// malformed / names an unresolved kind is a NAMED failure
/// (`pipeline.migrationDependencies.*`) — the operator declared the
/// file, so we honor it strictly.
[<RequireQualifiedAccess>]
module MigrationDependenciesBinding =

    let private bindError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.migrationDependencies.%s" code) message

    /// Read a required non-blank string property. `None` when absent,
    /// the wrong kind, JSON `null`, or whitespace — the caller supplies
    /// the structural error (mirrors `Config.getString`'s null handling
    /// under `<Nullable>enable</Nullable>`).
    let private tryNonBlankString (element: JsonElement) (key: string) : string option =
        match element.TryGetProperty(key) with
        | true, v when v.ValueKind = JsonValueKind.String ->
            match v.GetString() with
            | null -> None
            | s when System.String.IsNullOrWhiteSpace s -> None
            | s -> Some s
        | _ -> None

    /// The raw, pre-resolution shape of one kind entry — logical
    /// coordinate + its rows. Resolution against the catalog happens in
    /// a second pass so a parse error and a resolution error stay
    /// distinct in the operator's diagnostics.
    type private RawRow =
        { Id     : string
          Values : Map<Name, string> }

    type private RawKind =
        { Module : string
          Entity : string
          Rows   : RawRow list }

    /// Project one JSON value cell to its raw invariant-culture string.
    /// Strings pass through; numbers / booleans render via `GetRawText`
    /// (invariant by `System.Text.Json` contract); `null` is the NULL
    /// convention (`""`, per `StaticRow.Values`). Objects / arrays are a
    /// named error — a migration cell is a scalar.
    let private cellValue (column: string) (v: JsonElement) : Result<string> =
        match v.ValueKind with
        | JsonValueKind.String                       ->
            match v.GetString() with
            | null -> Result.success ""
            | s    -> Result.success s
        | JsonValueKind.Number                       -> Result.success (v.GetRawText())
        | JsonValueKind.True | JsonValueKind.False   -> Result.success (v.GetRawText())
        | JsonValueKind.Null                         -> Result.success ""
        | _ ->
            Result.failureOf (
                bindError "cellNotScalar"
                    (sprintf "migration values cell '%s' must be a string, number, boolean, or null." column))

    let private parseValues (rowLabel: string) (element: JsonElement) : Result<Map<Name, string>> =
        match element.TryGetProperty("values") with
        | false, _ -> Result.success Map.empty
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success Map.empty
            | JsonValueKind.Object ->
                v.EnumerateObject()
                |> Seq.map (fun prop ->
                    validation {
                        let! name  = Name.create prop.Name
                        and! value = cellValue prop.Name prop.Value
                        return (name, value)
                    })
                |> Result.aggregate
                |> Result.map Map.ofList
            | _ ->
                Result.failureOf (
                    bindError "valuesNotObject"
                        (sprintf "migration row %s 'values' must be a JSON object of column → value." rowLabel))

    let private parseRow (kindLabel: string) (element: JsonElement) : Result<RawRow> =
        match tryNonBlankString element "id" with
        | None ->
            Result.failureOf (
                bindError "rowMissingId"
                    (sprintf "every migration row under %s needs a non-blank string 'id'." kindLabel))
        | Some id ->
            parseValues (sprintf "%s/%s" kindLabel id) element
            |> Result.map (fun values -> { Id = id; Values = values })

    let private parseKind (element: JsonElement) : Result<RawKind> =
        let getReqStr (key: string) : Result<string> =
            match tryNonBlankString element key with
            | Some s -> Result.success s
            | None ->
                Result.failureOf (
                    bindError "kindMissingCoordinate"
                        (sprintf "every migration kind entry needs a non-blank string '%s'." key))
        validation {
            let! moduleName = getReqStr "module"
            and! entityName = getReqStr "entity"
            let kindLabel = sprintf "%s.%s" moduleName entityName
            let! rows =
                match element.TryGetProperty("rows") with
                | false, _ -> Result.success []
                | true, v ->
                    match v.ValueKind with
                    | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
                    | JsonValueKind.Array ->
                        v.EnumerateArray()
                        |> Seq.map (parseRow kindLabel)
                        |> Result.aggregate
                    | _ ->
                        Result.failureOf (
                            bindError "rowsNotArray"
                                (sprintf "migration kind %s 'rows' must be an array." kindLabel))
            return { Module = moduleName; Entity = entityName; Rows = rows }
        }

    /// Parse the document text into the raw (pre-resolution) kind list.
    /// Root must be an object with an optional `kinds` array.
    let private parseDocument (text: string) : Result<RawKind list> =
        try
            use doc = JsonDocument.Parse(text)
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                Result.failureOf (
                    bindError "rootNotObject"
                        "the migration-dependencies file must be a JSON object with a 'kinds' array.")
            else
                match root.TryGetProperty("kinds") with
                | false, _ -> Result.success []
                | true, v ->
                    match v.ValueKind with
                    | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
                    | JsonValueKind.Array ->
                        v.EnumerateArray()
                        |> Seq.map parseKind
                        |> Result.aggregate
                    | _ ->
                        Result.failureOf (
                            bindError "kindsNotArray" "'kinds' must be an array.")
        with :? JsonException as ex ->
            Result.failureOf (bindError "malformedJson" (sprintf "could not parse the migration-dependencies file as JSON: %s" ex.Message))

    /// Resolve one raw kind entry against the catalog — logical
    /// `(module, entity)` → the kind's `SsKey`; each row's `id` → a
    /// synthesized `Identifier`. Unresolved coordinate ⇒ a named error.
    let private resolveKind (catalog: Catalog) (raw: RawKind) : Result<MigrationDependencyRow list> =
        match CatalogResolution.tryKindByLogical catalog raw.Module raw.Entity with
        | None ->
            Result.failureOf (
                bindError "unresolvedKind"
                    (sprintf "migration kind %s.%s did not match any catalog kind." raw.Module raw.Entity))
        | Some kindKey ->
            raw.Rows
            |> List.map (fun row ->
                SsKey.synthesizedComposite "migration" [ raw.Module; raw.Entity; row.Id ]
                |> Result.map (fun identifier ->
                    { KindKey    = kindKey
                      Identifier = identifier
                      Values     = row.Values }))
            |> Result.aggregate

    /// Read + parse + resolve the file at `path` into a context.
    let private readFile (catalog: Catalog) (path: string) : Result<MigrationDependencyContext> =
        let textR =
            try Result.success (File.ReadAllText path)
            with ex ->
                Result.failureOf (
                    bindError "readFailed"
                        (sprintf "could not read the migration-dependencies file at '%s': %s" path ex.Message))
        textR
        |> Result.bind parseDocument
        |> Result.bind (fun rawKinds ->
            rawKinds
            |> List.map (resolveKind catalog)
            |> Result.aggregate
            |> Result.map (fun perKind -> { Rows = List.concat perKind }))

    /// Build the typed `MigrationDependencyContext` from a parsed
    /// `Config` + the resolved `Catalog`. No `overrides.migration
    /// Dependencies.path` ⇒ the empty context (no-op; byte-identical to
    /// the prior `MigrationDependencyContext.empty` threading). A path
    /// that is present but unreadable / malformed / names an unresolved
    /// kind fails loud (`pipeline.migrationDependencies.*`).
    let fromConfig
        (catalog: Catalog)
        (cfg: Config.Config)
        : Result<MigrationDependencyContext> =
        match cfg.Overrides.MigrationDependencies with
        | None      -> Result.success MigrationDependencyContext.empty
        | Some over -> readFile catalog over.Path

    /// The set of kind `SsKey`s the migration context populates — the
    /// Bootstrap-complement exclusion set (so `hydrateBootstrapRows`
    /// keeps the three lanes disjoint; the partition law). Empty for the
    /// empty context.
    let kindKeysOf (ctx: MigrationDependencyContext) : Set<SsKey> =
        ctx.Rows |> List.map (fun r -> r.KindKey) |> Set.ofList
