namespace Projection.Adapters.Osm

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Projection.Core
open OssysRowsetTypes
open OssysTranslation

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
        /// V1 pre-aggregation rowset bundle. Chapter 3.2 — closes the
        /// JSON-projection-lossiness class (`DECISIONS 2026-05-19 —
        /// naming the two classes`). The rowsets carry SsKey natively
        /// (via `OssysOriginal guid` per `Identity.fs:70`); A1's
        /// "identity survives rename" bound resolves through this
        /// path. Coexists permanently with `SnapshotJson` per
        /// `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` §6 — no
        /// deprecation trigger named.
        | SnapshotRowsets of bundle: RowsetBundle

    let private parseAttribute
        (moduleName: string) (entityName: string) (attrJson: JsonElement)
        : Result<Attribute> =
        let nameResult     = getString  attrJson "name"
        let physicalResult = getString  attrJson "physicalName"
        let dataTypeStr    = getString  attrJson "dataType"
        let isMandatory    = getBool    attrJson "isMandatory"
        let isIdentifier   = getBool    attrJson "isIdentifier"
        let isAutoNumber   = getBool    attrJson "isAutoNumber"
        // Chapter A.0' slice α — Description lift. Defensive read
        // via `getOptionalString` returns Ok None when the source
        // omits the field; the JSON path consumes V1's `description`
        // JSON property which `SnapshotJsonBuilder.cs` writes when
        // V1's `ossys_EntityAttr.Description` is non-null.
        let descriptionResult = getOptionalString attrJson "description"
        // Chapter 4.9 slice β — OriginalName + ExternalDatabaseType lift.
        // V1's JSON projects via `originalName` (NULL when no rename
        // history) and `external_dbType` (NULL for OS-native entities
        // and for external entities lacking an override). Both fields
        // are defensive optional reads; the adapter carries `None` when
        // the source omits or null-projects either.
        let originalNameResult       = getOptionalString attrJson "originalName"
        let externalDbTypeResult     = getOptionalString attrJson "external_dbType"
        match nameResult, physicalResult, dataTypeStr, isMandatory, isIdentifier with
        | Ok rawName, Ok physicalName, Ok rawDataType,
          Ok mandatory, Ok identifier ->
            let nameDU       = Name.create rawName
            let key          = attributeSsKey moduleName entityName rawName
            let description  =
                match descriptionResult with
                | Ok d -> d
                | Error _ -> None
            let originalName : string option =
                match originalNameResult with
                | Ok n -> n
                | Error _ -> None
            let externalDatabaseType : string option =
                match externalDbTypeResult with
                | Ok t -> t
                | Error _ -> None
            // Per session-32 — V1 surfaces length / precision /
            // scale on attribute records when applicable. The
            // adapter pulls them through to the V2 IR so the
            // canary's round-trip sees byte-faithful column
            // declarations (NVARCHAR(N) instead of NVARCHAR(MAX),
            // DECIMAL(P, S) instead of DECIMAL(18, 4) default).
            let lengthOpt    = getOptionalInt attrJson "length"
            let precisionOpt = getOptionalInt attrJson "precision"
            let scaleOpt     = getOptionalInt attrJson "scale"
            // Resolve the semantic category + concrete SQL Server
            // storage type from the OutSystems type name (rt-prefix
            // aware) plus the declared length / precision / scale, with
            // the optional `external_dbType` override applied. The
            // semantic `PrimitiveType` stays canonical for the IR's
            // `Type` field; the concrete `SqlStorageType` carries the
            // emission evidence (`rtLongInteger` → BIGINT, etc.).
            let typeEvidence =
                resolveAttributeType
                    rawDataType lengthOpt precisionOpt scaleOpt externalDatabaseType
            // Identity = isAutoNumber per V1 convention (only
            // primary-key columns marked isAutoNumber=true map to
            // SQL Server IDENTITY).
            let isIdentity =
                match isAutoNumber with
                | Ok true -> true
                | _ -> false
            // Chapter A.0' slice ε — DefaultValue lift. V1's JSON
            // `default` field is typically `null` in current
            // projections; when present, the adapter projects via
            // `SqlLiteral.ofRaw` against the typed `PrimitiveType`.
            // Falls back to `None` if the type-resolution failed
            // upstream (the parent record error path handles the
            // primitive's Error case).
            let defaultValue : SqlLiteral option =
                match typeEvidence with
                | Error _ -> None
                | Ok (p, _) ->
                    match attrJson.TryGetProperty("default") with
                    | true, value when value.ValueKind <> JsonValueKind.Null ->
                        let rawOpt =
                            match value.ValueKind with
                            | JsonValueKind.String ->
                                match value.GetString() with
                                | null -> None
                                | s    -> Some s
                            | JsonValueKind.Number -> Some (value.GetRawText())
                            | JsonValueKind.True  -> Some "true"
                            | JsonValueKind.False -> Some "false"
                            | _ -> None
                        rawOpt |> Option.map (SqlLiteral.ofRaw p)
                    | _ -> None
            let columnNameDU = ColumnName.create physicalName
            match nameDU, key, typeEvidence, columnNameDU with
            | Ok n, Ok k, Ok (p, storage), Ok physicalColumnName ->
                Result.success
                    { SsKey        = k
                      Name         = n
                      Type         = p
                      Column       = { ColumnName = physicalColumnName; IsNullable = not mandatory }
                      IsPrimaryKey = identifier
                      IsMandatory  = mandatory
                      Length       = lengthOpt
                      Precision    = precisionOpt
                      Scale        = scaleOpt
                      IsIdentity   = isIdentity
                      Description  = description
                      IsActive     = isActiveOrDefault attrJson
                      DefaultValue = defaultValue
                      DefaultName  = None
                      // Chapter A.0' slice ε — Computed lift; V1's
                      // JSON projection does not surface computed-
                      // column metadata. Positioned for future use.
                      Computed     = None
                      // Chapter A.0' slice ζ — ExtendedProperties
                      // attribute-level lift; V1's JSON projection
                      // does not surface attribute-level extended
                      // properties at the boundary today.
                      ExtendedProperties = []
                      OriginalName       = originalName
                      ExternalDatabaseType = externalDatabaseType
                      SqlStorage         = Some storage }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                // Substantive causes (e.g., `adapter.osm.unmappedDataType`
                // from `resolveAttributeType`) survive the attribute-level
                // wrap.
                propagateOrFallback
                    [ Result.errors nameDU
                      Result.errors key
                      Result.errors typeEvidence
                      Result.errors columnNameDU ]
                    (fun () ->
                        adapterError
                            "attributeBuild"
                            (sprintf
                                "Failed to build attribute '%s' on '%s.%s'."
                                rawName moduleName entityName))
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
    ///   - `isExternal: false` → Native (clear)
    ///   - `isExternal: true`  → ExternalIndirect (placeholder)
    ///
    /// The `ExternalIndirect` placeholder reflects that
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
    let private parseReference
        (sourceModuleName: string) (sourceEntityName: string) (attrJson: JsonElement)
        : Result<Reference option> =
        match getIntFlag attrJson "isReference" with
        | Error errors -> Error errors
        | Ok false  -> Result.success None
        | Ok true ->
            let attrNameResult     = getString          attrJson "name"
            let refEntityNameResult = getOptionalString attrJson "refEntity_name"
            let deleteRuleResult   = getOptionalString attrJson "reference_deleteRuleCode"
            match attrNameResult, refEntityNameResult, deleteRuleResult with
            | Ok attrName, Ok (Some refEntityName), Ok deleteRuleCode ->
                let refKey      = referenceSsKey sourceModuleName sourceEntityName attrName
                let refName     = Name.create attrName
                let srcAttrKey  = attributeSsKey sourceModuleName sourceEntityName attrName
                let tgtKindKey  = kindSsKey sourceModuleName refEntityName
                let onDelete    = parseDeleteRule deleteRuleCode
                match refKey, refName, srcAttrKey, tgtKindKey, onDelete with
                | Ok rKey, Ok rName, Ok srcKey, Ok tgtKey, Ok rule ->
                    // Chapter 4.6 slice α — capture V1's
                    // `reference_hasDbConstraint` int-flag
                    // (COALESCE'd from outsystems_model_export.sql:730
                    // HasFK column; V1's JSON projection renames to
                    // `reference_hasDbConstraint` per SnapshotJsonBuilder).
                    // Defaults to false when V1 source omits the field
                    // (mirrors V1's ISNULL coalesce semantics).
                    // Chapter 4.7 slice α: getOptionalIntFlag retires the
                    // local `match … | Ok v -> v | Error _ -> default`
                    // pattern.
                    let hasDbConstraint =
                        getOptionalIntFlag attrJson "reference_hasDbConstraint" false
                    // Slice 5.13.fk-features-emit — smart-constructor
                    // migration. `Reference.create` carries the
                    // minimum-evidence defaults (OnDelete = NoAction;
                    // IsUserFk = false; HasDbConstraint = false;
                    // OnUpdate = None; IsConstraintTrusted = true);
                    // the JSON path overrides what the source carries.
                    // User-FK detection (chapter 4.2 deferral) stays at
                    // default `false`.
                    Result.success (Some
                        { Reference.create rKey rName srcKey tgtKey with
                            OnDelete        = rule
                            HasDbConstraint = hasDbConstraint })
                | _ ->
                    // Propagate underlying errors via
                    // `propagateOrFallback` — uniform with the four
                    // build sites in parseKind / parseModule /
                    // parseAttribute / parseIndex. Retires the
                    // partial-decompose `Error es` branch (which
                    // caught only the onDelete-error shape and dropped
                    // the other four error sources on the floor).
                    propagateOrFallback
                        [ Result.errors refKey
                          Result.errors refName
                          Result.errors srcAttrKey
                          Result.errors tgtKindKey
                          Result.errors onDelete ]
                        (fun () ->
                            adapterError
                                "referenceBuild"
                                (sprintf
                                    "Failed to build reference for attribute '%s' on '%s.%s'."
                                    attrName sourceModuleName sourceEntityName))
            | Ok attrName, Ok None, _ ->
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

    /// Extract one V2 Index from a V1 index JSON entry.
    ///
    /// V1's index JSON shape (per `outsystems_metadata_rowsets.sql`,
    /// `#IdxJson` aggregation at line 864+) carries: `name`,
    /// `isPrimary`, `kind`, `isUnique`, `isPlatformAuto`, plus
    /// storage/perf attributes (`isDisabled` / `isPadded` / etc.),
    /// structural fields (`filterDefinition`, `dataSpace`,
    /// `partitionColumns`, `dataCompression`), and `columns` array.
    /// V2's `Index` shape consumes only `name`, `isPrimary`,
    /// `isUnique`, plus the key columns from `columns[]`.
    ///
    /// **Included-columns drop.** Per the OSSYS ADMIRE entry's
    /// "what V2 will explicitly NOT carry forward" section, V1
    /// `columns[]` entries with `isIncluded: true` are dropped at
    /// the boundary; V2's `Columns` carries only key columns.
    /// Documented divergence; not a bug.
    ///
    /// **Column ordering.** V1 carries `columns[].ordinal` to
    /// preserve key-column order. V2's adapter sorts by ordinal
    /// before flattening to `Columns: SsKey list`.
    let private parseIndex
        (moduleName: string) (entityName: string) (indexJson: JsonElement)
        : Result<Index> =
        let nameResult      = getString indexJson "name"
        let isPrimaryResult = getBool   indexJson "isPrimary"
        let isUniqueResult  = getBool   indexJson "isUnique"
        match nameResult, isPrimaryResult, isUniqueResult with
        | Ok indexName, Ok isPrimary, Ok isUnique ->
            let indexKey  = indexSsKey moduleName entityName indexName
            let indexNm   = Name.create indexName
            // Walk columns[]; partition into key columns + included columns
            // (chapter 4.5 slice β — V2 now carries both axes; pre-slice-β
            // the adapter dropped isIncluded=true entries). Sort each
            // partition by ordinal; resolve attribute name to V2 SsKey.
            let columnsList =
                match indexJson.TryGetProperty("columns") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray() |> Seq.toList
                | _ -> []
            let extractCol (col: JsonElement) =
                // Best-effort ordinal extraction; missing ordinal sorts
                // as 0 (preserves first-in-array for malformed input).
                let ordinal =
                    match col.TryGetProperty("ordinal") with
                    | true, o when o.ValueKind = JsonValueKind.Number ->
                        match o.TryGetInt32() with
                        | true, n -> n
                        | _       -> 0
                    | _ -> 0
                let attrNameResult = getString col "attribute"
                ordinal, col, attrNameResult
            let isIncluded (col: JsonElement) : bool =
                match col.TryGetProperty("isIncluded") with
                | true, v when v.ValueKind = JsonValueKind.True -> true
                | _ -> false
            // Chapter 4.9 slice γ — parse per-column direction. V1's
            // JSON property is `direction` ("ASC" / "DESC" /
            // case-insensitive; absent / null / unknown → Ascending).
            // V1's `IndexColumnDirection.Unspecified` collapses to
            // Ascending under SQL Server semantics (the keyword is
            // omitted in CREATE INDEX, matching ScriptDom's
            // `SortOrder.NotSpecified`).
            let parseDirection (col: JsonElement) : IndexColumnDirection =
                match col.TryGetProperty("direction") with
                | true, v when v.ValueKind = JsonValueKind.String ->
                    match Option.ofObj (v.GetString()) with
                    | Some raw when System.String.Equals(raw.Trim(), "DESC", System.StringComparison.OrdinalIgnoreCase) ->
                        Descending
                    | _ -> Ascending
                | _ -> Ascending
            let keyColResults =
                columnsList
                |> List.filter (fun c -> not (isIncluded c))
                |> List.map extractCol
                |> List.sortBy (fun (o, _, _) -> o)
                |> List.map (fun (_, col, attrNameRes) ->
                    match attrNameRes with
                    | Error es -> Error es
                    | Ok an ->
                        match attributeSsKey moduleName entityName an with
                        | Error es -> Error es
                        | Ok key -> Result.success { Attribute = key; Direction = parseDirection col })
            let includedColResults =
                columnsList
                |> List.filter isIncluded
                |> List.map extractCol
                |> List.sortBy (fun (o, _, _) -> o)
                |> List.map (fun (_, _, attrNameRes) ->
                    match attrNameRes with
                    | Error es -> Error es
                    | Ok an -> attributeSsKey moduleName entityName an)
            // Per `Result.aggregate` (chapter-3.1 close audit): the
            // canonical accumulator for `Result<'a> seq` collapses to
            // `Result<'a list>` with errors aggregated (not short-
            // circuited). Retires the O(N²) `xs @ [x]` fold pattern
            // per `DECISIONS 2026-05-09` Big-O discipline.
            let foldedKeyCols = Result.aggregate keyColResults
            let foldedIncludedCols = Result.aggregate includedColResults
            match indexKey, indexNm, foldedKeyCols, foldedIncludedCols with
            | Ok k, Ok n, Ok cols, Ok includedCols ->
                // Chapter 4.5 slice α — capture V1's
                // `filterDefinition` if present (per V1 JSON shape;
                // raw string preserved through to emit time, parsed
                // by TSql160Parser at ScriptDomBuild.buildCreateIndex).
                let filter =
                    match indexJson.TryGetProperty("filterDefinition") with
                    | true, v when v.ValueKind = JsonValueKind.String ->
                        match Option.ofObj (v.GetString()) with
                        | Some raw when not (System.String.IsNullOrWhiteSpace raw) ->
                            Some raw
                        | _ -> None
                    | _ -> None
                // Chapter 4.6 slice β — capture V1's `isPlatformAuto`
                // flag (JSON projection of IndexModel.IsPlatformAuto).
                // Defaults to false when V1 source omits the field.
                // Chapter 4.7 slice α: getOptionalBool retires the
                // local `match … | Ok v -> v | Error _ -> default`
                // pattern.
                let isPlatformAuto = getOptionalBool indexJson "isPlatformAuto" false
                // Slice 5.13.smart-constructor-lift migration —
                // `Index.create` carries minimum-evidence defaults;
                // the JSON path overrides what the source surfaces.
                // V1's JSON projection does not yet carry on-disk
                // metadata or the slice 5.13.index-features-emit
                // axes (IgnoreDuplicateKey / IsDisabled /
                // DataCompression); those stay at smart-constructor
                // defaults pending a rowset wiring slice.
                Result.success
                    { Index.create k n cols with
                        Uniqueness     = IndexUniqueness.ofLegacyBooleans isUnique isPrimary
                        Filter         = filter
                        IncludedColumns = includedCols
                        IsPlatformAuto = isPlatformAuto }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                propagateOrFallback
                    [ Result.errors indexKey
                      Result.errors indexNm
                      Result.errors foldedKeyCols
                      Result.errors foldedIncludedCols ]
                    (fun () ->
                        adapterError
                            "indexBuild"
                            (sprintf
                                "Failed to build index '%s' on '%s.%s'."
                                indexName moduleName entityName))
        | _ ->
            Result.failureOf (
                adapterError
                    "indexFields"
                    (sprintf
                        "Required index fields missing on an entity in '%s.%s'."
                        moduleName entityName))

    /// Parse one V1 entity-level `triggers[]` element into a V2
    /// `Trigger`. Chapter A.0' slice γ — IR fidelity lift (L3-S4).
    /// V1 JSON shape: `{ name, isDisabled, definition }`. V1's
    /// `TriggerModel.Create` requires non-null definition; V2's
    /// `Trigger.create` mirrors that invariant via the smart
    /// constructor.
    let private parseTrigger
        (moduleName: string) (entityName: string) (triggerJson: JsonElement)
        : Result<Trigger> =
        let nameResult       = getString triggerJson "name"
        let definitionResult = getString triggerJson "definition"
        let isDisabled =
            match triggerJson.TryGetProperty("isDisabled") with
            | true, value when value.ValueKind = JsonValueKind.True -> true
            | _ -> false
        match nameResult, definitionResult with
        | Ok triggerName, Ok definition ->
            let key  = triggerSsKey moduleName entityName triggerName
            let name = Name.create triggerName
            match key, name with
            | Ok k, Ok n -> Trigger.create k n isDisabled definition
            | _ ->
                propagateOrFallback
                    [ Result.errors key
                      Result.errors name ]
                    (fun () ->
                        adapterError
                            "triggerBuild"
                            (sprintf
                                "Failed to build trigger '%s' on '%s.%s'."
                                triggerName moduleName entityName))
        | _ ->
            propagateOrFallback
                [ Result.errors nameResult
                  Result.errors definitionResult ]
                (fun () ->
                    adapterError
                        "triggerFields"
                        (sprintf
                            "Required trigger fields missing on '%s.%s'."
                            moduleName entityName))

    /// Parse one V1 entity-level `extendedProperties[]` element into a
    /// V2 `ExtendedProperty`. Chapter A.0' slice ζ — IR fidelity lift
    /// (L3-S9 extended-properties sub-axiom). V1 JSON shape:
    /// `{ name, value }` (value may be null).
    let private parseExtendedProperty
        (moduleName: string) (entityName: string) (epJson: JsonElement)
        : Result<ExtendedProperty> =
        let nameResult = getString epJson "name"
        let value =
            match epJson.TryGetProperty("value") with
            | true, v when v.ValueKind = JsonValueKind.String ->
                match v.GetString() with
                | null -> None
                | s    -> Some s
            | _ -> None
        match nameResult with
        | Ok epName -> ExtendedProperty.create epName value
        | _ ->
            propagateOrFallback
                [ Result.errors nameResult ]
                (fun () ->
                    adapterError
                        "extendedPropertyFields"
                        (sprintf
                            "Required extended-property fields missing on '%s.%s'."
                            moduleName entityName))

    let private parseKind
        (moduleName: string) (entityJson: JsonElement)
        : Result<Kind> =
        let nameResult       = getString entityJson "name"
        let physicalResult   = getString entityJson "physicalName"
        let schemaResult     = getString entityJson "db_schema"
        // Chapter A.0' slice θ — Catalog (database) coordinate lift
        // (L3-S10 / L3-I10). V1's JSON projects `db_catalog` (typically
        // `null`; explicit cross-database references land as a
        // non-blank string). Defensive read via `getOptionalString`.
        let catalogResult    = getOptionalString entityJson "db_catalog"
        let isStaticResult   = getBool   entityJson "isStatic"
        let isExternalResult = getBool   entityJson "isExternal"
        // Chapter A.0' slice α — Description lift. Same defensive
        // read shape as parseAttribute.
        let descriptionResult = getOptionalString entityJson "description"
        match nameResult, physicalResult, schemaResult, isStaticResult, isExternalResult with
        | Ok entityName, Ok physicalName, Ok schema,
          Ok isStatic, Ok isExternal ->
            let description =
                match descriptionResult with
                | Ok d -> d
                | Error _ -> None
            let kindKey   = kindSsKey moduleName entityName
            let kindName  = Name.create entityName
            // Chapter A.0' slice β — the session-21 inactive-records
            // filter retires. Inactive attributes are carried into
            // `Kind.Attributes` with `Attribute.IsActive=false`; the
            // adapter no longer drops them. Pillar-9 harvest analysis:
            // a Selection-axis filter is `OperatorIntent`, not
            // `DataIntent`; the adapter carries only `DataIntent`.
            let attrJsonList =
                match entityJson.TryGetProperty("attributes") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                | _ -> []
            let attrsResults =
                attrJsonList
                |> Bench.iterMap "adapter.osm.parse.attribute" (parseAttribute moduleName entityName)
            let refResults =
                attrJsonList
                |> Bench.iterMap "adapter.osm.parse.reference" (parseReference moduleName entityName)
            // Collect attribute results — `Result.aggregate` collapses
            // `Result<'a> seq` to `Result<'a list>` with errors
            // aggregated. Retires the O(N²) `xs @ [x]` fold pattern.
            let foldedAttrs = Result.aggregate attrsResults
            // Collect reference results — `Result.aggregate` then drop
            // `None` entries via `List.choose id`. Same Big-O profile
            // as the legacy fold (O(N) overall) without the per-step
            // append.
            let foldedRefs =
                refResults
                |> Result.aggregate
                |> Result.map (List.choose id)
            // Collect index results — session 22; iterate the
            // entity's `indexes[]` array. The inactive-records
            // filter (session 21) does NOT extend to indexes today;
            // V1 index records have no `isActive` field on the
            // index itself (storage-level objects in V1's metadata).
            let indexResults =
                match entityJson.TryGetProperty("indexes") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.index" (parseIndex moduleName entityName)
                | _ -> []
            let foldedIdx = Result.aggregate indexResults
            // Chapter A.0' slice γ — Triggers lift. V1's JSON projects
            // entity-level `triggers[]` (carrying name + isDisabled +
            // definition). Empty array when none.
            let triggerResults =
                match entityJson.TryGetProperty("triggers") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.trigger" (parseTrigger moduleName entityName)
                | _ -> []
            let foldedTriggers = Result.aggregate triggerResults
            // Chapter A.0' slice ζ — ExtendedProperties lift (kind
            // level). V1's JSON projects entity-level
            // `extendedProperties[]`.
            let epResults =
                match entityJson.TryGetProperty("extendedProperties") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.extendedProperty" (parseExtendedProperty moduleName entityName)
                | _ -> []
            let foldedEps = Result.aggregate epResults
            // Slice 5 — TableId is typed (SchemaName / TableName).
            // Validate the raw schema/table strings via the smart
            // constructors; aggregate failures into the kindBuild fan-in
            // below.
            let physicalSchemaResult = SchemaName.create schema
            let physicalTableResult = TableName.create physicalName
            match kindKey, kindName, foldedAttrs, foldedRefs, foldedIdx, foldedTriggers, foldedEps, physicalSchemaResult, physicalTableResult with
            | Ok k, Ok n, Ok attrs, Ok refs, Ok idxs, Ok triggers, Ok eps, Ok schemaName, Ok tableName ->
                let modality =
                    if isStatic then [ Static [] ] else []
                Result.success
                    { SsKey       = k
                      Name        = n
                      Origin      = parseOrigin isExternal
                      Modality    = modality
                      Physical    =
                        { Schema = schemaName
                          Table = tableName
                          // Chapter A.0' slice θ — `db_catalog` carried
                          // through; `None` when V1 projects `null`
                          // (implicit-current-database scope).
                          Catalog =
                            match catalogResult with
                            | Ok value -> value
                            | Error _  -> None }
                      Attributes  = attrs
                      References  = refs
                      Indexes     = idxs
                      Description = description
                      IsActive    = isActiveOrDefault entityJson
                      Triggers    = triggers
                      // Chapter A.0' slice ε — ColumnChecks lift; V1's
                      // JSON projection does not surface table-level
                      // CHECK constraints today (V1 carries them on
                      // the on-disk metadata channel, not the JSON
                      // boundary). Empty default.
                      ColumnChecks = []
                      ExtendedProperties = eps }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`
                // (codified at two-consumer threshold; same surface as
                // parseKindRow on the rowset path). Substantive causes
                // — e.g., `adapter.osm.unmappedDeleteRule` from
                // `parseDeleteRule`, `adapter.osm.unmappedDataType`
                // from `parsePrimitiveType` — survive the kind-level
                // wrap. Prior shape swallowed them under a generic
                // `kindBuild` umbrella.
                propagateOrFallback
                    [ Result.errors kindKey
                      Result.errors kindName
                      Result.errors foldedAttrs
                      Result.errors foldedRefs
                      Result.errors foldedIdx
                      Result.errors foldedTriggers
                      Result.errors foldedEps
                      Result.errors physicalSchemaResult
                      Result.errors physicalTableResult ]
                    (fun () ->
                        adapterError
                            "kindBuild"
                            (sprintf
                                "Failed to build kind '%s' in module '%s'."
                                entityName moduleName))
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
        | Ok rawName ->
            let modKey  = moduleSsKey rawName
            let modName = Name.create rawName
            // Chapter A.0' slice β — the session-21 entity-level
            // filter retires. Inactive entities carry into
            // `Module.Kinds` with `Kind.IsActive=false`; downstream
            // emitters decide. Per Subagent #3's O2 finding the JSON
            // path's `parseModule` did not previously filter on
            // `module.isActive` (the filter only operated at entity
            // and attribute levels); slice β adds module-level
            // carriage via `isActiveOrDefault` so the IR's
            // `Module.IsActive` field has authoritative provenance
            // from both paths.
            let entitiesArr =
                match moduleJson.TryGetProperty("entities") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> Bench.iterMap "adapter.osm.parse.kind" (parseKind rawName)
                | _ ->
                    []
            let foldedKinds = Result.aggregate entitiesArr
            match modKey, modName, foldedKinds with
            | Ok k, Ok n, Ok kinds ->
                // Per DECISIONS pillar 6 (chapter-3.6 sidebar):
                // boundary adapters flow through the aggregate-root
                // smart constructor, not record-literal — invariants
                // (kind-SsKey-disjoint within module) are checked
                // structurally at the boundary, not deferred.
                //
                // Chapter A.0' slice ζ — Module.ExtendedProperties
                // populated empty; V1's JSON projection does not
                // surface module-level extended properties.
                Module.create k n kinds (isActiveOrDefault moduleJson) []
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                // Substantive causes survive the module-level wrap.
                propagateOrFallback
                    [ Result.errors modKey
                      Result.errors modName
                      Result.errors foldedKinds ]
                    (fun () ->
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
                |> Bench.iterMap "adapter.osm.parse.module" parseModule
            let folded = Result.aggregate modulesList
            match folded with
            | Ok modules ->
                // Per DECISIONS pillar 6: boundary adapter flows
                // through `Catalog.create` (aggregate-root invariant
                // check) rather than record-literal construction.
                //
                // Chapter A.0' slice δ — Catalog.Sequences populated
                // empty; V1's `osm_model.json` projection does not
                // surface sequences at the catalog boundary today.
                Catalog.create modules []
            | Error errors  -> Error errors
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

    // -----------------------------------------------------------------------
    // Translation — V1 RowsetBundle → V2 Catalog.
    //
    // Chapter 3.2 slice 1. Sibling translation path to parseDocument.
    // SsKey carriage flips from `Synthesized ("OS_KIND", [...])` (JSON
    // path) to `OssysOriginal guid` (rowset path) when the rowset DTO
    // carries the Guid; falls back to synthesized form when absent
    // (test convenience for partial fixtures). Per A1's chapter-3.2
    // bound resolution: this is the path where SsKey is no longer
    // JSON-projection-bounded.
    //
    // Per `DECISIONS 2026-05-22 — Stage 0 foundation phase` aggregate-
    // root smart constructor commitment + chapter-3.6 pillar 6
    // amendment: boundary translation flows through
    // `Catalog.create` / `Module.create` (referential-integrity
    // invariants) rather than record-literal construction.
    // -----------------------------------------------------------------------

    let private moduleSsKeyFromRow (row: ModuleRow) : Result<SsKey> =
        match row.EspaceSsKey with
        | Some g -> Result.success (SsKey.ossysOriginal g)
        | None   -> moduleSsKey row.EspaceName

    let private kindSsKeyFromRow
        (moduleName: string)
        (row: KindRow)
        : Result<SsKey> =
        match row.EntitySsKey with
        | Some g -> Result.success (SsKey.ossysOriginal g)
        | None   -> kindSsKey moduleName row.EntityName

    let private attributeSsKeyFromRow
        (moduleName: string)
        (entityName: string)
        (row: AttributeRow)
        : Result<SsKey> =
        match row.AttrSsKey with
        | Some g -> Result.success (SsKey.ossysOriginal g)
        | None   -> attributeSsKey moduleName entityName row.AttrName

    let private parseAttributeRow
        (moduleName: string)
        (entityName: string)
        (row: AttributeRow)
        : Result<Attribute> =
        let nameDU    = Name.create row.AttrName
        let key       = attributeSsKeyFromRow moduleName entityName row
        // Resolve semantic category + concrete SQL Server storage from
        // the rowset's `Type` value (rt-prefix aware), the declared
        // length / precision / scale, and any `ExternalColumnType`
        // override. Same resolution as the JSON path.
        let typeEvidence =
            resolveAttributeType
                row.DataType row.Length row.Precision row.Scale row.ExternalDatabaseType
        let columnNameDU = ColumnName.create row.PhysicalCol
        match nameDU, key, typeEvidence, columnNameDU with
        | Ok n, Ok k, Ok (p, storage), Ok physicalColumnName ->
            Result.success
                { SsKey        = k
                  Name         = n
                  Type         = p
                  Column       = { ColumnName = physicalColumnName
                                   IsNullable = not row.IsMandatory }
                  IsPrimaryKey = row.IsIdentifier
                  IsMandatory  = row.IsMandatory
                  Length       = row.Length
                  Precision    = row.Precision
                  Scale        = row.Scale
                  IsIdentity   = row.IsAutoNumber
                  Description  = row.Description
                  IsActive     = row.IsActive
                  // Slice A.4.7'-prelude.row53-source-side: V1's
                  // `#ColumnReality.DefaultDefinition` carries the
                  // expression text (e.g., `((0))`, `(getdate())`).
                  // Parens-stripping + literal-vs-expression
                  // disambiguation deferred per matrix row 53's named
                  // trigger ("expression-shaped defaults flow via
                  // raw-string pass-through at the realization
                  // boundary"). For now: rowset path leaves
                  // DefaultValue = None; the JSON path's
                  // `parseAttribute` populates from V1's `default`
                  // field which V1 emits as the literal value.
                  DefaultValue = None
                  // Slice A.4.7'-prelude.row53-source-side: V1
                  // `#ColumnReality.DefaultConstraintName` (sys
                  // .default_constraints.name) → V2 DefaultName for
                  // round-trip parity with V1's `DF_<table>_<column>`
                  // constraint identifier. `None` when no named
                  // DEFAULT constraint exists at the deployed target.
                  DefaultName  =
                      row.DefaultConstraintName
                      |> Option.bind (fun raw ->
                          Name.create raw |> Result.toOption)
                  // Slice A.4.7'-prelude.row53-source-side (LR4 cash-
                  // out completion): V1 `#ColumnReality.IsComputed` +
                  // `ComputedDefinition` (sys.computed_columns
                  // .definition) → V2 ComputedColumnConfig. The
                  // `IsPersisted` axis defaults to false because V1's
                  // SQL doesn't surface `sys.computed_columns
                  // .is_persisted`; persisted-detection is a follow-up
                  // rowset extension when V2 emission demands the
                  // PERSISTED keyword for round-trip.
                  Computed     =
                      if row.IsComputed then
                          row.ComputedDefinition
                          |> Option.bind (fun expr ->
                              ComputedColumnConfig.create expr false
                              |> Result.toOption)
                      else
                          None
                  ExtendedProperties = []
                  OriginalName = row.OriginalName
                  ExternalDatabaseType = row.ExternalDatabaseType
                  SqlStorage   = Some storage }
        | _ ->
            // Propagate underlying errors via `propagateOrFallback`.
            propagateOrFallback
                [ Result.errors nameDU
                  Result.errors key
                  Result.errors typeEvidence
                  Result.errors columnNameDU ]
                (fun () ->
                    adapterError
                        "attributeRowBuild"
                        (sprintf
                            "Failed to build attribute '%s' on '%s.%s' from rowset bundle."
                            row.AttrName moduleName entityName))

    /// Build one V2 `Reference` from a paired `(AttributeRow, ReferenceRow)`.
    /// Same structural shape as `parseReference` (JSON path,
    /// CatalogReader.fs:496) — both delegate to the shared
    /// `referenceSsKey` / `attributeSsKey` / `kindSsKey` synthesis
    /// helpers; both apply rule 16's same-module assumption (target
    /// kind name resolves within the source attribute's module).
    /// Cross-module FK lifts the same deferral.
    let private parseReferenceRowFor
        (kindKeysByEntityId: Map<int, SsKey>)
        (kindKeysByEntityName: Map<string, SsKey>)
        (moduleName: string)
        (entityName: string)
        (attrRow: AttributeRow)
        (refRow: ReferenceRow)
        : Result<Reference> =
        let refKey     = referenceSsKey moduleName entityName attrRow.AttrName
        let refName    = Name.create attrRow.AttrName
        let srcAttrKey = attributeSsKeyFromRow moduleName entityName attrRow
        // Target-kind resolution for the reference (FK). The primary key
        // is the CTE-resolved `RefEntityId` against the GLOBAL
        // `kindKeysByEntityId` map — cross-module-correct, including for
        // `bt<espace>*<entity>`-encoded references the rowset CTE
        // resolves (the espace GUID names the target's module, the
        // entity GUID its entity). Chapter 5.0 slice γ: this also handles
        // GUID-based EntitySsKey targets, where the synthesized
        // `(module, name)` key would have a different shape and break the
        // danglingTarget invariant.
        //
        // Fallback when `RefEntityId` is absent: resolve by entity name
        // across EVERY module (`kindKeysByEntityName`) rather than
        // assuming the source module — a cross-module reference whose ID
        // didn't resolve still finds its target by name. Only when the
        // name is unknown bundle-wide does it degrade to same-module
        // synthesis.
        let resolveByName () : Result<SsKey> =
            match Map.tryFind refRow.RefEntityName kindKeysByEntityName with
            | Some key -> Result.success key
            | None     -> kindSsKey moduleName refRow.RefEntityName
        let tgtKindKey =
            match refRow.RefEntityId with
            | Some id ->
                match Map.tryFind id kindKeysByEntityId with
                | Some key -> Result.success key
                | None     -> resolveByName ()
            | None -> resolveByName ()
        let onDelete   = parseDeleteRule refRow.DeleteRuleCode
        // Slice A.4.7'-prelude.row17-18-rowset-roundtrip — `OnUpdate`
        // carries SQL Server's `sys.foreign_keys.update_referential_action
        // _desc` vocabulary (NO_ACTION / CASCADE / SET_NULL / SET_DEFAULT),
        // not OutSystems' DeleteRuleCode vocabulary
        // (Delete / Protect / Ignore / SetNull). The prior
        // 5.13.fk-reality-join slice routed through `parseDeleteRule`
        // which silently dropped every valid SQL Server value into the
        // error branch (bug found 2026-05-19 via FkRealityRowsetRoundTripTests).
        // `parseSqlForeignKeyAction` is the SQL-Server-vocabulary parser;
        // unfamiliar values degrade to None per the rowset adapter's
        // defensive-parsing posture.
        let onUpdateRule = parseSqlForeignKeyAction refRow.OnUpdate
        match refKey, refName, srcAttrKey, tgtKindKey, onDelete with
        | Ok rKey, Ok rName, Ok srcKey, Ok tgtKey, Ok rule ->
            // Slice 5.13.fk-features-emit — smart-constructor migration.
            // Slice 5.13.fk-reality-join (2026-05-18) — `OnUpdate` +
            // `IsConstraintTrusted` thread through from the rowset
            // path's `#FkReality` JOIN at `toBundle`. Cross-catalog +
            // JSON-path references default to `(None, true)` per the
            // smart-constructor defaults.
            // G14 — normalize the constraint-state pair through the guard so a
            // V1 rowset carrying the illegal `(hasFK=0 ∧ isNoCheck=1)` quadrant
            // (untrusted without a constraint) canonicalizes to vacuous-trust.
            Result.success
                ({ Reference.create rKey rName srcKey tgtKey with
                     OnDelete = rule
                     OnUpdate = onUpdateRule }
                 |> Reference.withConstraintState refRow.HasDbConstraint refRow.IsConstraintTrusted)
        | _ ->
            // Propagate underlying errors via `propagateOrFallback` —
            // uniform with parseReference on the JSON path.
            propagateOrFallback
                [ Result.errors refKey
                  Result.errors refName
                  Result.errors srcAttrKey
                  Result.errors tgtKindKey
                  Result.errors onDelete ]
                (fun () ->
                    adapterError
                        "referenceRowBuild"
                        (sprintf
                            "Failed to build reference for attribute '%s' on '%s.%s' from rowset bundle."
                            attrRow.AttrName moduleName entityName))

    /// Per-id-keyed groupings the rowset-bundle parser threads through
    /// `parseModuleRow` → `parseKindRow`. Slice 5.13.ossys-rowsets-cluster
    /// consolidates four existing Maps + four new index/trigger/check
    /// Maps into one record so future rowset lifts (matrix rows 58 +
    /// 59 cash-out, etc.) extend the context shape rather than the
    /// function signature.
    ///
    /// Sibling-wrapper-discipline-friendly: extending the record (an
    /// IR-grows-under-evidence move) is structurally cheap; expanding
    /// the parseKindRow signature with N more Maps is the anti-pattern
    /// the discipline names.
    type private RowsetParseContext =
        {
            /// EntityId → kind's resolved V2 SsKey (composite of GUID or
            /// synthesized identity per `kindSsKeyFromRow`). Used by
            /// `parseReferenceRowFor` for cross-module FK resolution.
            KindKeysByEntityId : Map<int, SsKey>
            /// Entity NAME → kind's resolved V2 SsKey, spanning every
            /// module in the bundle. The cross-module fallback for
            /// `parseReferenceRowFor` when the resolved `RefEntityId`
            /// is absent: a `bt<espace>*<entity>` reference whose target
            /// lives in a different module resolves by name across the
            /// whole bundle rather than being mis-synthesized into the
            /// source module. Last-write-wins on the rare cross-module
            /// name collision (deterministic in bundle order); the
            /// primary `RefEntityId` path is unambiguous and preferred.
            KindKeysByEntityName : Map<string, SsKey>
            /// EspaceId → kinds belonging to that module. Owned by
            /// `parseModuleRow`'s walk.
            KindsByEspace : Map<int, KindRow list>
            /// EntityId → attributes belonging to that kind. Used by
            /// `parseKindRow` for attribute construction.
            AttributesByEntity : Map<int, AttributeRow list>
            /// AttrId → references on that attribute. Used by
            /// `parseKindRow` for reference assembly.
            ReferencesByAttr : Map<int, ReferenceRow list>
            /// EntityId → indexes belonging to that kind. Slice
            /// 5.13.ossys-rowsets-cluster; matrix row 15.
            IndexesByEntity : Map<int, IndexRow list>
            /// (EntityId, IndexName) → index columns belonging to that
            /// index. Slice 5.13.ossys-rowsets-cluster; matrix row 16.
            IndexColumnsByIndex : Map<int * string, IndexColumnRow list>
            /// EntityId → triggers belonging to that kind. Slice
            /// 5.13.ossys-rowsets-cluster; matrix row 23.
            TriggersByEntity : Map<int, TriggerRow list>
            /// EntityId → CHECK constraints rolling up from this
            /// kind's attributes. Pre-grouped from `ColumnCheckRow`
            /// (per-AttrId in V1) into per-Kind list via AttrId→EntityId
            /// resolution at context construction. Slice
            /// 5.13.ossys-rowsets-cluster; matrix row 12.
            ColumnChecksByEntity : Map<int, ColumnCheckRow list>
        }

    /// Slice 5.13.ossys-rowsets-cluster — IndexColumn direction parser.
    /// V1's `#IdxColsMapped.Direction` carries `"ASC"` / `"DESC"`
    /// (case-insensitive); absent / null collapses to Ascending under
    /// SQL Server semantics (the keyword is omitted in CREATE INDEX,
    /// matching ScriptDom's `SortOrder.NotSpecified`). Sibling to the
    /// JSON-path's inline `parseDirection`.
    let private parseRowsetIndexDirection (raw: string option) : IndexColumnDirection =
        match raw with
        | Some d when
            System.String.Equals(d.Trim(), "DESC", System.StringComparison.OrdinalIgnoreCase) ->
            Descending
        | _ -> Ascending

    /// Slice 5.13.ossys-rowsets-cluster — IndexColumn attribute SsKey
    /// resolution. V1's `#IdxColsMapped.HumanAttr` is the COALESCE of
    /// `(PhysicalColumnName, DatabaseColumnName, AttrName)` — it
    /// carries the PHYSICAL name first when present (the typical
    /// case for OS-managed attributes), falling back to the logical
    /// name only when the physical name is empty. To bridge to V2's
    /// `attributeSsKey` (which keys on the **logical** AttrName), the
    /// resolver looks up the candidate string in the kind's
    /// `AttributeRow` list and uses the resolved `AttrName`.
    ///
    /// **Resolution order** (case-insensitive against the kind's
    /// attribute set):
    ///   1. `HumanAttr` matches an attribute's `AttrName` → use that
    ///      attribute's `AttrName`. Covers V1's COALESCE fallback to
    ///      `AttrName` when both physical-name columns are NULL.
    ///   2. `HumanAttr` matches an attribute's `PhysicalCol` → use
    ///      that attribute's `AttrName`. Covers V1's COALESCE
    ///      primary case where the PhysicalColumnName populates
    ///      HumanAttr.
    ///   3. `PhysicalColumn` matches an attribute's `PhysicalCol` →
    ///      use that attribute's `AttrName`. Fallback for rows where
    ///      HumanAttr is NULL.
    ///   4. None of the above → fail with `indexColumnUnresolved`.
    ///      The index references a column V2's attribute set doesn't
    ///      model — typically a system column (`OSPK`); the
    ///      diagnostic surfaces it so the operator can choose to
    ///      drop the index or extend V2's attribute model.
    let private resolveIndexColumnAttribute
        (moduleName: string)
        (entityName: string)
        (entityAttrs: AttributeRow list)
        (row: IndexColumnRow)
        : Result<SsKey> =
        let trimNonEmpty (s: string option) =
            s |> Option.map (fun v -> v.Trim())
              |> Option.filter (fun v -> not (System.String.IsNullOrEmpty v))
        let humanAttr   = trimNonEmpty row.HumanAttr
        let physColumn  = trimNonEmpty row.PhysicalColumn
        let findByAttrName (target: string) =
            entityAttrs
            |> List.tryFind (fun a ->
                System.String.Equals(a.AttrName, target, System.StringComparison.OrdinalIgnoreCase))
        let findByPhysicalCol (target: string) =
            entityAttrs
            |> List.tryFind (fun a ->
                System.String.Equals(a.PhysicalCol, target, System.StringComparison.OrdinalIgnoreCase))
        let firstHit (candidates: (unit -> AttributeRow option) list) : AttributeRow option =
            candidates
            |> List.tryPick (fun thunk -> thunk ())
        let resolved =
            firstHit
                [ (fun () -> humanAttr  |> Option.bind findByAttrName)
                  (fun () -> humanAttr  |> Option.bind findByPhysicalCol)
                  (fun () -> physColumn |> Option.bind findByPhysicalCol) ]
        match resolved with
        | Some attr ->
            // Per parseAttributeRow's shape: attribute SsKey is
            // `OssysOriginal GUID` when AttrSsKey is populated, else
            // synthesized from AttrName. Use the same helper to
            // produce a key that matches the attribute's actual SsKey.
            attributeSsKeyFromRow moduleName entityName attr
        | None ->
            Result.failureOf (
                adapterError
                    "indexColumnUnresolved"
                    (sprintf
                        "Index '%s' on kind '%s' references column '%s' (humanAttr='%s'); no matching attribute in V2's IR for this kind."
                        row.IndexName
                        entityName
                        (defaultArg physColumn "")
                        (defaultArg humanAttr "")))

    /// Slice 5.13.ossys-rowsets-cluster — per-Index assembly. Joins
    /// `IndexRow` with its `IndexColumnRow` list (lookup by EntityId
    /// + IndexName via `ctx.IndexColumnsByIndex`), partitions into
    /// key columns + included columns, sorts each partition by
    /// `Ordinal` for T1 byte-determinism, resolves attribute SsKeys
    /// per column, and lifts to V2's `Index` IR.
    let private parseIndexRowFor
        (ctx: RowsetParseContext)
        (moduleName: string)
        (entityName: string)
        (entityAttrs: AttributeRow list)
        (row: IndexRow)
        : Result<Index> =
        let indexKey  = indexSsKey moduleName entityName row.IndexName
        let indexName = Name.create row.IndexName
        let cols =
            Map.tryFind (row.EntityId, row.IndexName) ctx.IndexColumnsByIndex
            |> Option.defaultValue []
        let keyCols =
            cols
            |> List.filter (fun c -> not c.IsIncluded)
            |> List.sortBy (fun c -> c.Ordinal)
        let includedCols =
            cols
            |> List.filter (fun c -> c.IsIncluded)
            |> List.sortBy (fun c -> c.Ordinal)
        let keyColResults =
            keyCols
            |> List.map (fun c ->
                resolveIndexColumnAttribute moduleName entityName entityAttrs c
                |> Result.map (fun attrKey ->
                    { Attribute = attrKey
                      Direction = parseRowsetIndexDirection c.Direction } : IndexColumn))
        let includedColResults =
            includedCols
            |> List.map (resolveIndexColumnAttribute moduleName entityName entityAttrs)
        let foldedKeyCols      = Result.aggregate keyColResults
        let foldedIncludedCols = Result.aggregate includedColResults
        // FillFactor: SQL Server stores 0 as "server default" (unset);
        // V2 represents the default as None. Non-zero values pass
        // through; clamping to [1, 100] is V1's responsibility.
        let fillFactor =
            if row.FillFactor = 0 then None else Some row.FillFactor
        let filter =
            match row.FilterDefinition with
            | Some s when not (System.String.IsNullOrWhiteSpace s) -> Some s
            | _ -> None
        match indexKey, indexName, foldedKeyCols, foldedIncludedCols with
        | Ok k, Ok n, Ok keys, Ok included ->
            // Slice 5.13.smart-constructor-lift migration + slice
            // 5.13.fk-reality-join (2026-05-18) — rowset path
            // surfaces every #AllIdx axis V1 reflects: IsUnique /
            // IsPrimary, on-disk metadata, filter, included columns,
            // plus the slice-5.13.index-features-emit triple
            // (IsDisabled / IgnoreDuplicateKey / DataCompression).
            // IsPlatformAuto stays at default (rowset path doesn't
            // surface it; it lives on V1's logical IndexModel
            // projection, not on sys.indexes reality).
            let dataCompressionLevel =
                row.DataCompression
                |> Option.bind (fun s ->
                    match s.ToUpperInvariant() with
                    | "NONE" -> Some DataCompressionLevel.None
                    | "ROW"  -> Some DataCompressionLevel.Row
                    | "PAGE" -> Some DataCompressionLevel.Page
                    | _      -> None)
            Result.success
                { Index.create k n keys with
                    Uniqueness            = IndexUniqueness.ofLegacyBooleans row.IsUnique row.IsPrimary
                    Filter                = filter
                    IncludedColumns       = included
                    FillFactor            = fillFactor
                    IsPadded              = row.IsPadded
                    AllowRowLocks         = row.AllowRowLocks
                    AllowPageLocks        = row.AllowPageLocks
                    NoRecomputeStatistics = row.NoRecompute
                    IsDisabled            = row.IsDisabled
                    IgnoreDuplicateKey    = row.IgnoreDupKey
                    DataCompression       = dataCompressionLevel
                    // Slice A.4.7'-prelude.row56-dataspace (LR7
                    // closure): V1 #AllIdx.DataSpaceName/Type/
                    // PartitionColumnsJson → V2 Index.DataSpace.
                    // Carriage is direct; MetadataSnapshotRunner
                    // .toBundle does the JSON parse + DU shaping
                    // (the Adapter.Osm boundary trusts the typed
                    // DataSpace coming from the OssysSql adapter).
                    DataSpace             = row.DataSpace }
        | _ ->
            propagateOrFallback
                [ Result.errors indexKey
                  Result.errors indexName
                  Result.errors foldedKeyCols
                  Result.errors foldedIncludedCols ]
                (fun () ->
                    adapterError
                        "indexRowBuild"
                        (sprintf
                            "Failed to build index '%s' on kind '%s' from rowset bundle."
                            row.IndexName
                            entityName))

    /// Slice 5.13.ossys-rowsets-cluster — per-Trigger lift. Sibling
    /// to the JSON-path's `parseTrigger`. The caller pre-filters rows
    /// with blank Definition (Trigger.create rejects them); `def` is
    /// the unwrapped non-blank definition string.
    let private parseTriggerRowFor
        (moduleName: string)
        (entityName: string)
        (row: TriggerRow)
        (def: string)
        : Result<Trigger> =
        let trigKey  = triggerSsKey moduleName entityName row.TriggerName
        let trigName = Name.create row.TriggerName
        match trigKey, trigName with
        | Ok k, Ok n ->
            Trigger.create k n row.IsDisabled def
        | _ ->
            propagateOrFallback
                [ Result.errors trigKey
                  Result.errors trigName ]
                (fun () ->
                    adapterError
                        "triggerRowBuild"
                        (sprintf
                            "Failed to build trigger '%s' on kind '%s' from rowset bundle."
                            row.TriggerName
                            entityName))

    /// Slice 5.13.ossys-rowsets-cluster — per-ColumnCheck lift. V1's
    /// `#ColumnCheckReality` is per-column; V2's `Kind.ColumnChecks`
    /// is table-scoped (multi-column CHECKs collapse to one entry).
    /// The caller dedupes by ConstraintName before passing rows here.
    let private parseColumnCheckRowFor
        (moduleName: string)
        (entityName: string)
        (row: ColumnCheckRow)
        : Result<ColumnCheck> =
        let chkKey  = columnCheckSsKey moduleName entityName row.ConstraintName
        let chkName = Name.create row.ConstraintName
        match chkKey, chkName with
        | Ok k, Ok n ->
            ColumnCheck.create k (Some n) row.Definition row.IsNotTrusted
        | _ ->
            propagateOrFallback
                [ Result.errors chkKey
                  Result.errors chkName ]
                (fun () ->
                    adapterError
                        "columnCheckRowBuild"
                        (sprintf
                            "Failed to build CHECK constraint '%s' on kind '%s' from rowset bundle."
                            row.ConstraintName
                            entityName))

    let private parseKindRow
        (ctx: RowsetParseContext)
        (moduleName: string)
        (moduleEspaceKind: string option)
        (kindRow: KindRow)
        : Result<Kind> =
        let kindKey  = kindSsKeyFromRow moduleName kindRow
        let kindName = Name.create kindRow.EntityName
        // Chapter A.0' slice β — the session-21 attribute-level
        // filter retires on the rowset path (parity with the JSON
        // path retirement). Inactive attributes are carried with
        // `Attribute.IsActive=false`. References on inactive
        // attributes are carried through the join below (an
        // inactive attribute still has its reference rows; the
        // adapter's adapter-boundary discipline restricts to
        // `DataIntent` carriage).
        let attrRows =
            Map.tryFind kindRow.EntityId ctx.AttributesByEntity
            |> Option.defaultValue []
        let attrResults =
            attrRows
            |> Bench.iterMap "adapter.osm.parse.rowsetAttribute" (parseAttributeRow moduleName kindRow.EntityName)
        let foldedAttrs = Result.aggregate attrResults
        let refResults =
            attrRows
            |> List.collect (fun a ->
                Map.tryFind a.AttrId ctx.ReferencesByAttr
                |> Option.defaultValue []
                |> List.map (parseReferenceRowFor ctx.KindKeysByEntityId ctx.KindKeysByEntityName moduleName kindRow.EntityName a))
        let foldedRefs = Result.aggregate refResults
        // Slice 5.13.ossys-rowsets-cluster — per-Kind index assembly
        // from `IndexesByEntity` × `IndexColumnsByIndex`. The JOIN
        // resolves each IndexColumnRow's HumanAttr (preferred) or
        // PhysicalColumn (fallback) to V2's attribute SsKey via the
        // same `attributeSsKey` synthesizer the JSON path uses. Sort
        // by Ordinal within (key columns + included columns)
        // partitions for byte-determinism.
        let indexResults =
            Map.tryFind kindRow.EntityId ctx.IndexesByEntity
            |> Option.defaultValue []
            |> Bench.iterMap "adapter.osm.parse.rowsetIndex" (parseIndexRowFor ctx moduleName kindRow.EntityName attrRows)
        let foldedIndexes = Result.aggregate indexResults
        let triggerResults =
            Map.tryFind kindRow.EntityId ctx.TriggersByEntity
            |> Option.defaultValue []
            |> List.choose (fun row ->
                // `Trigger.create` rejects blank Definition; V1 rows
                // with NULL TriggerDefinition (rare; defensive)
                // filter out at the adapter boundary.
                match row.Definition with
                | None -> None
                | Some def when System.String.IsNullOrWhiteSpace def -> None
                | Some def -> Some (parseTriggerRowFor moduleName kindRow.EntityName row def))
        let foldedTriggers = Result.aggregate triggerResults
        let columnCheckResults =
            Map.tryFind kindRow.EntityId ctx.ColumnChecksByEntity
            |> Option.defaultValue []
            // Dedupe by ConstraintName — a multi-column CHECK
            // surfaces once per column in `#ColumnCheckReality`; V2's
            // `Kind.ColumnChecks` is table-scoped (one entry per
            // unique constraint).
            |> List.distinctBy (fun row -> row.ConstraintName)
            |> Bench.iterMap "adapter.osm.parse.rowsetColumnCheck" (parseColumnCheckRowFor moduleName kindRow.EntityName)
        let foldedColumnChecks = Result.aggregate columnCheckResults
        // Slice 5 — TableId is typed (SchemaName / TableName).
        let physicalSchemaResult = SchemaName.create kindRow.DbSchema
        let physicalTableResult = TableName.create kindRow.PhysicalTableName
        match kindKey, kindName, foldedAttrs, foldedRefs,
              foldedIndexes, foldedTriggers, foldedColumnChecks,
              physicalSchemaResult, physicalTableResult with
        | Ok k, Ok n, Ok attrs, Ok refs,
          Ok idx, Ok trigs, Ok checks,
          Ok schemaName, Ok tableName ->
            let modality =
                [
                    if kindRow.IsStatic       then yield Static []
                    if kindRow.IsSystemEntity then yield SystemOwned
                ]
            Result.success
                { SsKey       = k
                  Name        = n
                  Origin      = parseOriginFromRowset kindRow.IsExternal moduleEspaceKind
                  Modality    = modality
                  Physical    = { Schema = schemaName
                                  Table  = tableName; Catalog = None }
                  Attributes  = attrs
                  References  = refs
                  Indexes     = idx
                  Description = kindRow.Description
                  IsActive    = kindRow.IsActive
                  Triggers    = trigs
                  ColumnChecks = checks
                  // Module-level ExtendedProperties not surfaced by
                  // V1's rowsets (chapter A.0' slice ζ deferral).
                  ExtendedProperties = [] }
        | _ ->
            propagateOrFallback
                [ Result.errors kindKey
                  Result.errors kindName
                  Result.errors foldedAttrs
                  Result.errors foldedRefs
                  Result.errors foldedIndexes
                  Result.errors foldedTriggers
                  Result.errors foldedColumnChecks
                  Result.errors physicalSchemaResult
                  Result.errors physicalTableResult ]
                (fun () ->
                    adapterError
                        "kindRowBuild"
                        (sprintf
                            "Failed to build kind '%s' in module '%s' from rowset bundle."
                            kindRow.EntityName moduleName))

    let private parseModuleRow
        (ctx: RowsetParseContext)
        (moduleRow: ModuleRow)
        : Result<Module> =
        let modKey  = moduleSsKeyFromRow moduleRow
        let modName = Name.create moduleRow.EspaceName
        let kindRows =
            Map.tryFind moduleRow.EspaceId ctx.KindsByEspace
            |> Option.defaultValue []
        let kindResults =
            kindRows
            |> Bench.iterMap "adapter.osm.parse.rowsetKind" (parseKindRow ctx moduleRow.EspaceName moduleRow.EspaceKind)
        let foldedKinds = Result.aggregate kindResults
        match modKey, modName, foldedKinds with
        | Ok k, Ok n, Ok kinds ->
            // Chapter A.0' slice ζ — Module.ExtendedProperties empty
            // on the rowset path; V1's rowsets do not surface
            // module-level extended properties.
            Module.create k n kinds moduleRow.IsActive []
        | _ ->
            propagateOrFallback
                [ Result.errors modKey
                  Result.errors modName
                  Result.errors foldedKinds ]
                (fun () ->
                    adapterError
                        "moduleRowBuild"
                        (sprintf
                            "Failed to build module '%s' from rowset bundle."
                            moduleRow.EspaceName))

    /// V1 rowset bundle → V2 Catalog. Sibling to `parseDocument` (JSON
    /// path). The flat-list bundle joins by FK ID columns at load time
    /// (`AttributeRow.EntityId` ↔ `KindRow.EntityId`; `KindRow.EspaceId`
    /// ↔ `ModuleRow.EspaceId`; `ReferenceRow.AttrId` ↔
    /// `AttributeRow.AttrId`); the resulting structure feeds the
    /// existing `Module.create` / `Catalog.create` aggregate-root
    /// smart constructors, so referential-integrity invariants are
    /// checked at the boundary identically to the JSON path.
    ///
    /// Big-O / pillar 7 perf clause: O(N + E + A + R) for the input
    /// bundle plus O(E + A) for the three Map.ofList constructions
    /// (one per ID-keyed projection). Per-module dispatch is O(E_m × A_e)
    /// with O(1) Map lookups; per-kind reference assembly is O(R_e)
    /// with O(1) Map lookups. Linear in the bundle's total size;
    /// matches `parseDocument`'s complexity class.
    let private parseRowsetBundle (bundle: RowsetBundle) : Result<Catalog> =
        let attributesByEntity =
            bundle.Attributes |> List.groupBy (fun a -> a.EntityId) |> Map.ofList
        let kindsByEspace =
            bundle.Kinds |> List.groupBy (fun k -> k.EspaceId) |> Map.ofList
        let referencesByAttr =
            bundle.References |> List.groupBy (fun r -> r.AttrId) |> Map.ofList
        // Slice 5.13.ossys-rowsets-cluster — per-id-keyed groupings
        // for the new index/trigger/check axes.
        let indexesByEntity =
            bundle.Indexes |> List.groupBy (fun i -> i.EntityId) |> Map.ofList
        let indexColumnsByIndex =
            bundle.IndexColumns
            |> List.groupBy (fun c -> c.EntityId, c.IndexName)
            |> Map.ofList
        let triggersByEntity =
            bundle.Triggers |> List.groupBy (fun t -> t.EntityId) |> Map.ofList
        // ColumnChecks are per-AttrId in V1's rowset; group up to
        // per-EntityId via the AttrId→EntityId resolution from the
        // attributes bundle. This pre-roll is O(C) rather than the
        // per-Kind alternative which would be O(C × E) (re-walk per
        // Kind), so the pre-built map keeps parseKindRow cheap.
        let entityByAttrId =
            bundle.Attributes
            |> List.map (fun a -> a.AttrId, a.EntityId)
            |> Map.ofList
        let columnChecksByEntity =
            bundle.ColumnChecks
            |> List.choose (fun row ->
                Map.tryFind row.AttrId entityByAttrId
                |> Option.map (fun eid -> eid, row))
            |> List.groupBy fst
            |> List.map (fun (eid, pairs) -> eid, pairs |> List.map snd)
            |> Map.ofList
        let moduleNameByEspaceId =
            bundle.Modules
            |> List.map (fun m -> m.EspaceId, m.EspaceName)
            |> Map.ofList
        let kindKeysByEntityId =
            bundle.Kinds
            |> List.choose (fun k ->
                match Map.tryFind k.EspaceId moduleNameByEspaceId with
                | None -> None
                | Some modName ->
                    let resolved =
                        match k.EntitySsKey with
                        | Some g -> Ok (SsKey.ossysOriginal g)
                        | None -> kindSsKey modName k.EntityName
                    match resolved with
                    | Ok key -> Some (k.EntityId, key)
                    | Error _ -> None)
            |> Map.ofList
        // Global entity-name → kind-key map (spans every module). The
        // cross-module fallback for `parseReferenceRowFor` when a
        // bt-resolved reference carries no `RefEntityId`.
        let kindKeysByEntityName =
            bundle.Kinds
            |> List.choose (fun k ->
                Map.tryFind k.EntityId kindKeysByEntityId
                |> Option.map (fun key -> k.EntityName, key))
            |> Map.ofList
        let ctx : RowsetParseContext =
            { KindKeysByEntityId   = kindKeysByEntityId
              KindKeysByEntityName = kindKeysByEntityName
              KindsByEspace        = kindsByEspace
              AttributesByEntity   = attributesByEntity
              ReferencesByAttr     = referencesByAttr
              IndexesByEntity      = indexesByEntity
              IndexColumnsByIndex  = indexColumnsByIndex
              TriggersByEntity     = triggersByEntity
              ColumnChecksByEntity = columnChecksByEntity }
        let moduleResults =
            bundle.Modules |> Bench.iterMap "adapter.osm.parse.rowsetModule" (parseModuleRow ctx)
        match Result.aggregate moduleResults with
        | Ok modules ->
            Catalog.create modules []
        | Error errors -> Error errors

    /// Parse a V1 `osm_model.json` snapshot into a V2 `Catalog`.
    ///
    /// Async at the boundary even though the JSON-path implementation
    /// is synchronous; future async-by-nature variants
    /// (DACPAC unzip, eventual `LiveOssysConnection`) need the
    /// `Task<...>` shape. See `DECISIONS 2026-05-15 — OSSYS adapter
    /// parse signature` for the rationale.
    let parse (source: SnapshotSource) : Task<Result<Catalog>> =
        use _ = Bench.scope "adapter.osm.parse"
        match source with
        | SnapshotJson json ->
            Task.FromResult(parseJsonString json)
        | SnapshotRowsets bundle ->
            // Chapter 3.2 slice 1. Pure translation; no I/O. The
            // rowset-shaped V1 metadata flows through
            // `parseRowsetBundle` for FK-by-ID join + Module/Kind/
            // Attribute construction. Closed-DU expansion empirical-
            // test discipline (`DECISIONS 2026-05-13`): exhaustiveness
            // errors should light up only at this match site.
            Task.FromResult(parseRowsetBundle bundle)
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

    /// Chapter A.4.7 slice δ. The OSSYS adapter's `RegisteredTransform`
    /// surface — metadata-only, per the adapter's boundary-stage
    /// nature. The adapter's `parse : SnapshotSource -> Task<Result<
    /// Catalog>>` is not a pure `Catalog -> Lineage<Diagnostics<...>>`
    /// transformation (it does I/O for `SnapshotFile`, returns
    /// `Task<Result<...>>` for boundary-error reporting); the
    /// `RegisteredTransform<'In, 'Out>` typed shell doesn't fit cleanly.
    /// Slice δ ships `registeredMetadata : RegisteredTransformMetadata`
    /// — the metadata view of the adapter's harvest-discipline
    /// classification, suitable for the registry's totality-coverage
    /// scan (slice θ) and manifest emission (slice η).
    ///
    /// Per the chapter A.4.7 open: "every transformative rule (filters,
    /// remaps, derivations — not pass-through field-to-field mappings)
    /// gets a RegisteredTransform entry." Slice δ packages the rules
    /// as Sites within one registry entry (intra-adapter classification
    /// fidelity per pillar 9 + Q11); per-rule separate registration
    /// would require extracting each helper into a standalone
    /// transformation, which is a larger refactor deferred-with-trigger
    /// (real consumer pressure for per-rule audit granularity).
    ///
    /// All adapter rules classify as `DataIntent`. The adapter is a
    /// translation layer carrying V1 source-schema evidence forward
    /// into V2 typed evidence; no operator opinion enters at the
    /// adapter boundary (the operator-intent passes — IsActive
    /// filter retired at slice β, etc. — run downstream of the
    /// adapter). The skeleton-purity property test (slice θ) will
    /// witness that `Project(catalog, Policy.empty, profile)` traverses
    /// the adapter without emitting any `OperatorIntent` lineage event.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "ossysCatalogReader" Schema
            [ TransformSite.dataIntent "identitySynthesis"
                "Synthesize V2 SsKeys from V1 names: moduleSsKey / kindSsKey / attributeSsKey / referenceSsKey / indexSsKey / triggerSsKey / sequenceSsKey / columnCheckSsKey. Derivation is deterministic from source identifiers; no operator opinion enters."
              TransformSite.dataIntent "typeTranslation"
                "Map V1 type/code values to V2 typed DUs: parsePrimitiveType (V1 dataType string → V2 PrimitiveType per A13's typed surface); parseDeleteRule (V1 OutSystems-domain onDelete code 'Delete'/'Protect'/'Ignore'/'SetNull' → V2 ReferenceAction); parseSqlForeignKeyAction (V1 #FkReality SQL-Server-domain update_referential_action_desc 'NO_ACTION'/'CASCADE'/'SET_NULL'/'SET_DEFAULT' → V2 ReferenceAction option — distinct vocabulary from parseDeleteRule per slice A.4.7'-prelude.row17-18-rowset-roundtrip); parseOrigin / parseOriginFromRowset (isExternal flag → Origin DU). All translations are structural — V1's vocabulary maps deterministically into V2's typed system."
              TransformSite.dataIntent "jsonAggregateParsing"
                "Assemble JSON-path IR records: parseAttribute / parseReference / parseIndex / parseTrigger / parseExtendedProperty / parseKind / parseModule / parseDocument / parseJsonString. Each parser threads V1 evidence into V2's typed records; the parsing is field-by-field translation with no operator overlay."
              TransformSite.dataIntent "rowsetAggregateParsing"
                "Assemble rowset-path IR records: parseAttributeRow / parseReferenceRowFor / parseKindRow / parseModuleRow / parseRowsetBundle. Mirrors the JSON-path semantics for the rowset-source variant (chapter 3.2 slice 1 onward); same DataIntent translation discipline. Slice A.4.7'-prelude.row53-source-side extended the AttributeRow projection to surface V1 `#ColumnReality` reflection (IsComputed + ComputedDefinition + DefaultConstraintName) — V1 deployed-target evidence flows into `Attribute.Computed : ComputedColumnConfig option` + `Attribute.DefaultName : Name option` via `MetadataSnapshotRunner.toBundle`'s join."
              TransformSite.dataIntent "isActiveCarryThrough"
                "Chapter A.0' slice β retroactive site. IsActive is carried through at Module / Kind / Attribute levels (not filtered at the adapter boundary; the session-21 filter was retired as a mis-placed OperatorIntent of Selection per DECISIONS 2026-05-16 (slice β) — the first worked example of pillar 9). The carriage itself is DataIntent evidence; a downstream Selection-axis pass that re-applies an inactive-records drop is deferred-with-trigger per IR-grows-under-evidence."
              TransformSite.dataIntent "tableIdCatalogRead"
                "Chapter A.0' slice θ retroactive site. V1's db_catalog field is read into TableId.Catalog (string option); cross-database FK qualification carries through without silent degradation to implicit-current-database scope. DataIntent — source-schema evidence carried forward." ]
