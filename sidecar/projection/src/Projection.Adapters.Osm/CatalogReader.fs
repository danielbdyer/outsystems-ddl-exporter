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

    /// The input slot on the parse function. Closed DU.
    ///
    /// **Two variants today; one variant planned (canonical
    /// resolution); one variant reserved.**
    ///
    ///   - `SnapshotFile` and `SnapshotJson` are the current
    ///     consumers. Both feed V1's canonical `osm_model.json`
    ///     shape; SsKey is name-synthesized; the bound on A1 is
    ///     documented (`DECISIONS 2026-05-15 — OSSYS adapter
    ///     translation rules`).
    ///
    ///   - **Planned: `SnapshotRowsets`.** Per the operator
    ///     decision recorded in `DECISIONS 2026-05-15 — OSSYS
    ///     adapter translation rules`, session-20 amendment, the
    ///     canonical resolution to the lossy-SSKey question is to
    ///     consume V1's trailing rowsets directly. Rowsets carry
    ///     SSKey natively and preserve per-table column structure
    ///     the `FOR JSON PATH` aggregations collapse. The variant
    ///     itself lands when chapter 2's organic flow brings it —
    ///     likely after the current OSSYS adapter chapter
    ///     completes its translation work through `SnapshotJson`.
    ///     The operator decision is locked; not subject to
    ///     relitigation.
    ///
    ///   - **Reserved: `LiveOssysConnection`.** A future variant
    ///     for the case where V2 needs to operate without V1's
    ///     chain in the loop entirely. Per `DECISIONS 2026-05-15 —
    ///     OSSYS adapter parse signature`, deferred until its
    ///     specific demand surfaces.
    ///
    /// Adding `SnapshotRowsets` speculatively today would violate
    /// the closed-DU expansion discipline (one consumer needed;
    /// zero exist). The variant is named here so future readers of
    /// the code see the architectural commitment without having to
    /// read DECISIONS to discover it.
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

    /// Reference SsKey synthesis (session 19). The reference identifies
    /// by its source coordinate — `<srcModule>_<srcEntity>_<viaAttr>`
    /// uniquely names an FK because each attribute carries at most one
    /// outgoing reference in V1's metadata.
    let private referenceSsKey
        (sourceModuleName: string) (sourceEntityName: string) (viaAttrName: string)
        : Result<SsKey> =
        SsKey.original (sprintf "OS_REF_%s_%s_%s" sourceModuleName sourceEntityName viaAttrName)

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

    /// Read an `isActive` flag with V1's default-true semantics. V1's
    /// SQL coerces missing/null `Is_Active` columns to active=true
    /// (per `outsystems_metadata_rowsets.sql:94, 116, 239`). V2's
    /// adapter mirrors that semantically: a missing `isActive`
    /// property is treated as active=true; explicit `false` is
    /// inactive; explicit `true` is active.
    ///
    /// Used by the inactive-records filter at the boundary
    /// (`DECISIONS 2026-05-15 — OSSYS adapter translation rules`,
    /// session-21 amendment): `entity.isActive: false` drops the
    /// entity from the V2 Catalog; `attribute.isActive: false`
    /// drops the attribute from its Kind's Attributes list.
    let private isActiveOrDefault (element: JsonElement) : bool =
        match element.TryGetProperty("isActive") with
        | true, value when value.ValueKind = JsonValueKind.False -> false
        | _ -> true

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

    /// V1 carries some boolean-shaped flags as numbers (0/1) rather
    /// than JSON booleans — `isReference`, `reference_hasDbConstraint`,
    /// `physical_isPresentButInactive`. This helper accepts either
    /// shape; non-zero is true.
    let private getIntFlag (element: JsonElement) (name: string) : Result<bool> =
        match getProperty element name with
        | Failure errors -> Failure errors
        | Success value ->
            match value.ValueKind with
            | JsonValueKind.Number ->
                match value.TryGetInt32() with
                | true, n -> Result.success (n <> 0)
                | _ ->
                    Result.failureOf (
                        adapterError
                            "typeMismatch"
                            (sprintf "Property '%s' is not an integer flag." name))
            | JsonValueKind.True  -> Result.success true
            | JsonValueKind.False -> Result.success false
            | _ ->
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a numeric flag or boolean." name))

    /// Optional string property. Returns `None` for missing or
    /// JSON-null values, `Some s` for non-null strings, `Failure`
    /// when the property exists but is not a string.
    let private getOptionalString (element: JsonElement) (name: string) : Result<string option> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success None
        | true, value ->
            match value.ValueKind with
            | JsonValueKind.Null      -> Result.success None
            | JsonValueKind.Undefined -> Result.success None
            | JsonValueKind.String ->
                match value.GetString() with
                | null    -> Result.success None
                | raw     -> Result.success (Some raw)
            | _ ->
                Result.failureOf (
                    adapterError
                        "typeMismatch"
                        (sprintf "Property '%s' is not a string when present." name))

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

    /// V1 reference_deleteRuleCode → V2 ReferenceAction. Mirrors the
    /// V1 mapping in `Osm.Smo/SmoEntityEmitter.cs`:
    ///   "Delete"  → Cascade
    ///   "Protect" → NoAction
    ///   "Ignore"  → NoAction
    ///   null      → NoAction (V1's TreatMissingDeleteRuleAsIgnore default)
    /// Other / unmapped values fail with adapter.osm.unmappedDeleteRule.
    let private parseDeleteRule (code: string option) : Result<ReferenceAction> =
        match code with
        | None              -> Result.success NoAction
        | Some "Delete"     -> Result.success Cascade
        | Some "Protect"    -> Result.success NoAction
        | Some "Ignore"     -> Result.success NoAction
        | Some "SetNull"    -> Result.success SetNull
        | Some other ->
            Result.failureOf (
                adapterError
                    "unmappedDeleteRule"
                    (sprintf "reference_deleteRuleCode '%s' has no V2 ReferenceAction mapping yet." other))

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

    /// V1 isExternal → V2 Origin three-way collapse rule (session 20).
    ///
    /// Through the JSON-snapshot path V2 consumes today, V2 sees only
    /// the boolean `isExternal` flag at entity level. V1's
    /// IS-vs-Direct distinction is encoded in `EspaceKind` (string
    /// column at the espace/rowset level) which `SnapshotJsonBuilder`
    /// does NOT write to `osm_model.json`. The full distinction is
    /// **bound by the input path**:
    ///
    ///   - `isExternal: false` → OsNative (clear)
    ///   - `isExternal: true`  → ExternalViaIntegrationStudio (placeholder)
    ///
    /// The `ExternalViaIntegrationStudio` placeholder reflects that
    /// IS extensions are the standard V1 mechanism for external
    /// entities; most isExternal=true cases are IS-imported. Direct
    /// external entities (no IS step) exist but are rarer. The
    /// bound resolves when the `SnapshotRowsets` variant lands —
    /// rowsets carry `EspaceKind` natively, enabling the full
    /// three-way distinction. See `DECISIONS 2026-05-15 — OSSYS
    /// adapter translation rules`, session-20 amendment for the
    /// bounded-A1-equivalent disposition.
    ///
    /// The session-18 placeholder for this branch was
    /// `ExternalDirect`; that choice was made before the empirical
    /// pressure of an external-entity fixture. Session 20's fixture
    /// surfaced the question; the placeholder updates under the
    /// pressure. Documented in the session-20 DECISIONS amendment.
    let private parseOrigin (isExternal: bool) : Origin =
        if isExternal then ExternalViaIntegrationStudio else OsNative

    /// Extract a Reference from a V1 attribute that carries
    /// `isReference: 1` plus its `refEntity_*` and
    /// `reference_deleteRuleCode` fields. Returns `None` for
    /// non-reference attributes; `Some Reference` for FK-bearing
    /// ones; `Failure` when the attribute claims isReference=1 but
    /// the required fields are missing or malformed.
    ///
    /// V1's relationships[] array carries the same information in an
    /// aggregated form (viaAttributeName + toEntity_name +
    /// hasDbConstraint). The V2 adapter walks attributes[] directly
    /// because the attribute carries every field the Reference shape
    /// needs; the relationships[] array becomes a cross-check rather
    /// than the primary source.
    ///
    /// Same-module assumption: TargetKind is synthesized as
    /// OS_KIND_<sourceModule>_<refEntity_name>. Cross-module FK
    /// references would surface a richer rule when a fixture forces
    /// the question; the minimal session-19 fixture is same-module.
    let private parseReference
        (sourceModuleName: string) (sourceEntityName: string) (attrJson: JsonElement)
        : Result<Reference option> =
        match getIntFlag attrJson "isReference" with
        | Failure errors -> Failure errors
        | Success false  -> Result.success None
        | Success true ->
            let attrNameResult     = getString          attrJson "name"
            let refEntityNameResult = getOptionalString attrJson "refEntity_name"
            let deleteRuleResult   = getOptionalString attrJson "reference_deleteRuleCode"
            match attrNameResult, refEntityNameResult, deleteRuleResult with
            | Success attrName, Success (Some refEntityName), Success deleteRuleCode ->
                let refKey      = referenceSsKey sourceModuleName sourceEntityName attrName
                let refName     = Name.create attrName
                let srcAttrKey  = attributeSsKey sourceModuleName sourceEntityName attrName
                let tgtKindKey  = kindSsKey sourceModuleName refEntityName
                let onDelete    = parseDeleteRule deleteRuleCode
                match refKey, refName, srcAttrKey, tgtKindKey, onDelete with
                | Success rKey, Success rName, Success srcKey, Success tgtKey, Success rule ->
                    Result.success (Some
                        { SsKey           = rKey
                          Name            = rName
                          SourceAttribute = srcKey
                          TargetKind      = tgtKey
                          OnDelete        = rule })
                | _, _, _, _, Failure es -> Failure es
                | _ ->
                    Result.failureOf (
                        adapterError
                            "referenceBuild"
                            (sprintf
                                "Failed to build reference for attribute '%s' on '%s.%s'."
                                attrName sourceModuleName sourceEntityName))
            | Success attrName, Success None, _ ->
                Result.failureOf (
                    adapterError
                        "referenceFields"
                        (sprintf
                            "Attribute '%s' on '%s.%s' has isReference=1 but no refEntity_name."
                            attrName sourceModuleName sourceEntityName))
            | _ ->
                Result.failureOf (
                    adapterError
                        "referenceFields"
                        (sprintf
                            "Required reference fields missing on an attribute in '%s.%s'."
                            sourceModuleName sourceEntityName))

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
            // Inactive-records filter (session 21): attributes with
            // `isActive: false` are dropped at the boundary. The
            // session-21 DECISIONS amendment captures the rule, the
            // bound, and the silent-drop disposition pending the
            // future adapter-return-shape extension to support
            // Diagnostics-attached audit.
            let attrJsonList =
                match entityJson.TryGetProperty("attributes") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.filter isActiveOrDefault
                    |> Seq.toList
                | _ -> []
            let attrsResults =
                attrJsonList
                |> List.map (parseAttribute moduleName entityName)
            let refResults =
                attrJsonList
                |> List.map (parseReference moduleName entityName)
            // Collect attribute results — first failure wins.
            let foldedAttrs =
                attrsResults
                |> List.fold
                    (fun acc next ->
                        match acc, next with
                        | Success xs, Success x  -> Result.success (xs @ [x])
                        | Failure es, _          -> Failure es
                        | _, Failure es          -> Failure es)
                    (Result.success [])
            // Collect reference results — first failure wins. None
            // entries are dropped; Some entries flatten into a list.
            let foldedRefs =
                refResults
                |> List.fold
                    (fun acc next ->
                        match acc, next with
                        | Success xs, Success None     -> Result.success xs
                        | Success xs, Success (Some r) -> Result.success (xs @ [r])
                        | Failure es, _                -> Failure es
                        | _, Failure es                -> Failure es)
                    (Result.success [])
            match kindKey, kindName, foldedAttrs, foldedRefs with
            | Success k, Success n, Success attrs, Success refs ->
                let modality =
                    if isStatic then [ Static [] ] else []
                Result.success
                    { SsKey      = k
                      Name       = n
                      Origin     = parseOrigin isExternal
                      Modality   = modality
                      Physical   = { Schema = schema; Table = physicalName }
                      Attributes = attrs
                      References = refs
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
            // Inactive-records filter (session 21): entities with
            // `isActive: false` are dropped at the boundary. Same
            // disposition as the attribute-level filter in
            // parseKind. The DECISIONS amendment captures the
            // rule.
            let entitiesArr =
                match moduleJson.TryGetProperty("entities") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.filter isActiveOrDefault
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
