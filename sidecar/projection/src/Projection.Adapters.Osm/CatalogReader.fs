namespace Projection.Adapters.Osm

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Projection.Core

/// Boundary adapter — converts V1's `osm_model.json` snapshot shape
/// into V2's `Catalog` IR.
///
/// **V1↔V2 boundary.** V1's metadata extraction chain
/// (`outsystems_metadata_rowsets.sql` → `MetadataSnapshotRunner` →
/// `SnapshotJsonBuilder` → `osm_model.json`) is the source of truth
/// for OutSystems platform metadata. V2's adapter consumes the JSON
/// document V1 produces. The cherry-pick discipline (`HANDOFF.md`)
/// keeps the boundary as data, not typed cross-references — this
/// adapter does not depend on any V1 C# types.
///
/// **Position B for `ICatalogReader`.** Per `DECISIONS 2026-05-15 —
/// OSSYS adapter parse signature`, the entry-point shape is
/// `SnapshotSource -> Task<Result<Catalog>>`. A future
/// `ICatalogReader` interface (when a second catalog source
/// materializes) wraps this signature trivially via object expression;
/// no retrofit needed.
///
/// **Implementation discipline.** Only what the differential tests
/// demand. The OSSYS implementation chapter accumulates translation
/// rules under empirical pressure from
/// `OsmCatalogReaderDifferentialTests`; speculative DTO design that
/// mirrors V1's full ~22 rowset surface is deliberately avoided.
[<RequireQualifiedAccess>]
module CatalogReader =

    /// The input slot on the parse function. Closed DU; a future
    /// `LiveOssysConnection` variant lands as explicit DU expansion
    /// when V2 grows a SQL-running entry point per the re-open
    /// trigger named in `DECISIONS 2026-05-15 — OSSYS adapter parse
    /// signature`. Until that trigger fires, V1's JSON chain remains
    /// the metadata producer; V2 reads its output.
    type SnapshotSource =
        /// Path to a V1-produced `osm_model.json` file on disk.
        | SnapshotFile of path: string
        /// In-memory snapshot string. Useful for tests and for
        /// pipelines that produce the snapshot in memory rather than
        /// via disk.
        | SnapshotJson of json: string

    let private adapterError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "adapter.osm.%s" code) message

    // -----------------------------------------------------------------------
    // SsKey synthesis — V1's `osm_model.json` does NOT carry SSKey
    // values. SnapshotJsonBuilder writes names and physical-names; the
    // rowsets have SSKeys but the assembled JSON discards them. The
    // V2 parser synthesizes SsKey deterministically from name fields.
    // The synthesis convention matches existing V2 fixture builders
    // and is stable across runs of identical input. Re-open trigger
    // (per session 18 commit 4 DECISIONS entry): a real V1 chain
    // change that emits SSKeys, OR an alternative input shape that
    // carries them.
    // -----------------------------------------------------------------------

    let private moduleSsKey (moduleName: string) : Result<SsKey> =
        SsKey.original (sprintf "OS_MOD_%s" moduleName)

    let private kindSsKey (moduleName: string) (entityName: string) : Result<SsKey> =
        SsKey.original (sprintf "OS_KIND_%s_%s" moduleName entityName)

    let private attributeSsKey
        (moduleName: string) (entityName: string) (attrName: string)
        : Result<SsKey> =
        SsKey.original (sprintf "OS_ATTR_%s_%s_%s" moduleName entityName attrName)

    // -----------------------------------------------------------------------
    // JSON helpers — light wrappers over System.Text.Json.JsonElement.
    // These are private; they exist to keep the translation code
    // readable, not as a general-purpose JSON utility surface.
    // -----------------------------------------------------------------------

    let private getProperty (element: JsonElement) (name: string) : Result<JsonElement> =
        match element.TryGetProperty(name) with
        | true, value -> Result.success value
        | _ ->
            Result.failureOf (
                adapterError
                    "missingProperty"
                    (sprintf "Required property '%s' not found." name))

    let private getString (element: JsonElement) (name: string) : Result<string> =
        match getProperty element name with
        | Failure errors -> Failure errors
        | Success value ->
            if value.ValueKind = JsonValueKind.String then
                match value.GetString() with
                | null ->
                    Result.failureOf (
                        adapterError
                            "nullProperty"
                            (sprintf "Property '%s' is null; expected a string." name))
                | raw ->
                    Result.success raw
            else
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a string." name))

    let private getBool (element: JsonElement) (name: string) : Result<bool> =
        match getProperty element name with
        | Failure errors -> Failure errors
        | Success value ->
            match value.ValueKind with
            | JsonValueKind.True  -> Result.success true
            | JsonValueKind.False -> Result.success false
            | _ ->
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a boolean." name))

    // -----------------------------------------------------------------------
    // Translation — DataType string → V2 PrimitiveType.
    // The mapping rule is documented in session 18 commit 4 DECISIONS
    // entry. The fixture-driven implementation discipline applies:
    // only data types the differential tests exercise are mapped.
    // Subsequent fixtures extend the table.
    // -----------------------------------------------------------------------

    let private parsePrimitiveType (dataType: string) : Result<PrimitiveType> =
        match dataType with
        | "Identifier" -> Result.success Integer
        | "Text"       -> Result.success Text
        | other ->
            Result.failureOf (
                adapterError
                    "unmappedDataType"
                    (sprintf "DataType '%s' has no V2 PrimitiveType mapping yet." other))

    // -----------------------------------------------------------------------
    // Translation — V1 attribute → V2 Attribute.
    // -----------------------------------------------------------------------

    let private parseAttribute
        (moduleName: string) (entityName: string) (attrJson: JsonElement)
        : Result<Attribute> =
        let nameResult     = getString  attrJson "name"
        let physicalResult = getString  attrJson "physicalName"
        let dataTypeStr    = getString  attrJson "dataType"
        let isMandatory    = getBool    attrJson "isMandatory"
        let isIdentifier   = getBool    attrJson "isIdentifier"
        match nameResult, physicalResult, dataTypeStr, isMandatory, isIdentifier with
        | Success rawName, Success physicalName, Success rawDataType,
          Success mandatory, Success identifier ->
            let nameDU       = Name.create rawName
            let key          = attributeSsKey moduleName entityName rawName
            let primitive    = parsePrimitiveType rawDataType
            match nameDU, key, primitive with
            | Success n, Success k, Success p ->
                Result.success
                    { SsKey        = k
                      Name         = n
                      Type         = p
                      Column       = { ColumnName = physicalName; IsNullable = not mandatory }
                      IsPrimaryKey = identifier
                      IsMandatory  = mandatory }
            | _ ->
                Result.failureOf (
                    adapterError
                        "attributeBuild"
                        (sprintf "Failed to build attribute '%s' on '%s.%s'." rawName moduleName entityName))
        | _ ->
            Result.failureOf (
                adapterError
                    "attributeFields"
                    (sprintf "Required attribute fields missing on an entity in module '%s'." moduleName))

    // -----------------------------------------------------------------------
    // Translation — V1 entity → V2 Kind.
    // -----------------------------------------------------------------------

    let private parseOrigin (isExternal: bool) : Origin =
        // Minimal-fixture rule: isExternal=false → OsNative.
        // The IS-vs-Direct collapse for isExternal=true is deferred
        // (see session 18 commit 4 DECISIONS entry).
        if isExternal then ExternalDirect else OsNative

    let private parseKind
        (moduleName: string) (entityJson: JsonElement)
        : Result<Kind> =
        let nameResult       = getString entityJson "name"
        let physicalResult   = getString entityJson "physicalName"
        let schemaResult     = getString entityJson "db_schema"
        let isStaticResult   = getBool   entityJson "isStatic"
        let isExternalResult = getBool   entityJson "isExternal"
        match nameResult, physicalResult, schemaResult, isStaticResult, isExternalResult with
        | Success entityName, Success physicalName, Success schema,
          Success isStatic, Success isExternal ->
            let kindKey   = kindSsKey moduleName entityName
            let kindName  = Name.create entityName
            let attrsArr  =
                match entityJson.TryGetProperty("attributes") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> List.map (parseAttribute moduleName entityName)
                | _ ->
                    []
            // Collect attribute results — first failure wins.
            let foldedAttrs =
                attrsArr
                |> List.fold
                    (fun acc next ->
                        match acc, next with
                        | Success xs, Success x  -> Result.success (xs @ [x])
                        | Failure es, _          -> Failure es
                        | _, Failure es          -> Failure es)
                    (Result.success [])
            match kindKey, kindName, foldedAttrs with
            | Success k, Success n, Success attrs ->
                let modality =
                    if isStatic then [ Static [] ] else []
                Result.success
                    { SsKey      = k
                      Name       = n
                      Origin     = parseOrigin isExternal
                      Modality   = modality
                      Physical   = { Schema = schema; Table = physicalName }
                      Attributes = attrs
                      References = []
                      Indexes    = [] }
            | _ ->
                Result.failureOf (
                    adapterError
                        "kindBuild"
                        (sprintf "Failed to build kind '%s' in module '%s'." entityName moduleName))
        | _ ->
            Result.failureOf (
                adapterError
                    "kindFields"
                    (sprintf "Required entity fields missing in module '%s'." moduleName))

    // -----------------------------------------------------------------------
    // Translation — V1 module → V2 Module.
    // -----------------------------------------------------------------------

    let private parseModule (moduleJson: JsonElement) : Result<Module> =
        let nameResult = getString moduleJson "name"
        match nameResult with
        | Success rawName ->
            let modKey  = moduleSsKey rawName
            let modName = Name.create rawName
            let entitiesArr =
                match moduleJson.TryGetProperty("entities") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> List.map (parseKind rawName)
                | _ ->
                    []
            let foldedKinds =
                entitiesArr
                |> List.fold
                    (fun acc next ->
                        match acc, next with
                        | Success xs, Success x  -> Result.success (xs @ [x])
                        | Failure es, _          -> Failure es
                        | _, Failure es          -> Failure es)
                    (Result.success [])
            match modKey, modName, foldedKinds with
            | Success k, Success n, Success kinds ->
                Result.success
                    { SsKey = k; Name = n; Kinds = kinds }
            | _ ->
                Result.failureOf (
                    adapterError
                        "moduleBuild"
                        (sprintf "Failed to build module '%s'." rawName))
        | _ ->
            Result.failureOf (
                adapterError
                    "moduleFields"
                    "Required module fields missing.")

    // -----------------------------------------------------------------------
    // Translation — V1 osm_model.json document → V2 Catalog.
    // -----------------------------------------------------------------------

    let private parseDocument (root: JsonElement) : Result<Catalog> =
        match root.TryGetProperty("modules") with
        | true, arr when arr.ValueKind = JsonValueKind.Array ->
            let modulesList =
                arr.EnumerateArray()
                |> Seq.toList
                |> List.map parseModule
            let folded =
                modulesList
                |> List.fold
                    (fun acc next ->
                        match acc, next with
                        | Success xs, Success x  -> Result.success (xs @ [x])
                        | Failure es, _          -> Failure es
                        | _, Failure es          -> Failure es)
                    (Result.success [])
            match folded with
            | Success modules -> Result.success { Modules = modules }
            | Failure errors  -> Failure errors
        | _ ->
            Result.failureOf (
                adapterError
                    "missingModules"
                    "Document is missing the 'modules' array.")

    let private parseJsonString (json: string) : Result<Catalog> =
        try
            use document = JsonDocument.Parse(json)
            parseDocument document.RootElement
        with
        | :? JsonException as ex ->
            Result.failureOf (
                adapterError
                    "jsonInvalid"
                    (sprintf "Failed to parse JSON: %s" ex.Message))

    /// Parse a V1 `osm_model.json` snapshot into a V2 `Catalog`.
    ///
    /// Async at the boundary even though the JSON-path implementation
    /// is synchronous; future async-by-nature variants
    /// (DACPAC unzip, eventual `LiveOssysConnection`) need the
    /// `Task<...>` shape. See `DECISIONS 2026-05-15 — OSSYS adapter
    /// parse signature` for the rationale.
    let parse (source: SnapshotSource) : Task<Result<Catalog>> =
        match source with
        | SnapshotJson json ->
            Task.FromResult(parseJsonString json)
        | SnapshotFile path ->
            // Read-then-parse is the natural shape; async file I/O
            // would benefit primarily for very large snapshots, which
            // the chapter-open scope hasn't reached yet. Keep the
            // synchronous read for now; the boundary is async.
            try
                let json = File.ReadAllText(path)
                Task.FromResult(parseJsonString json)
            with
            | :? IOException as ex ->
                Task.FromResult(
                    Result.failureOf (
                        adapterError
                            "fileReadFailed"
                            (sprintf "Failed to read snapshot file '%s': %s" path ex.Message)))
            | :? System.UnauthorizedAccessException as ex ->
                Task.FromResult(
                    Result.failureOf (
                        adapterError
                            "fileAccessDenied"
                            (sprintf "Access denied reading snapshot file '%s': %s" path ex.Message)))
