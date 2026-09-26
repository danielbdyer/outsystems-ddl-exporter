namespace Projection.Adapters.Osm

open System.Text.Json
open Projection.Core
open OssysRowsetTypes
open OssysTranslation

/// OSSYS JSON-path reader — translates V1's `osm_model.json` document
/// (the `FOR JSON PATH` snapshot) into a `Catalog`. Identity is
/// name-synthesized (the JSON path is A1-bounded; SsKey is not carried).
/// Extracted from `CatalogReader` (2026-06-04 R1 decomposition step 3).
/// `parseJsonString` is the entry point consumed by `CatalogReader.parse`.
module OssysJsonReader =
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
            // WP8 / NM-72 — Service-Studio authored order from V1's
            // `order` JSON property (the rowset SQL's `AttributesJson`
            // payload carries `a.[Order_Num]`). `None` when the source
            // omits it (older snapshots, hand-built fixtures); the
            // canonical fallback (PK-first / SsKey) then applies.
            let orderOpt     = getOptionalInt attrJson "order"
            // Resolve the semantic category + concrete SQL Server
            // storage type from the OutSystems type name (rt-prefix
            // aware) plus the declared length / precision / scale, with
            // the optional `external_dbType` override applied. The
            // semantic `PrimitiveType` stays canonical for the IR's
            // `Type` field; the concrete `SqlStorageType` carries the
            // emission evidence (`rtLongInteger` → BIGINT, etc.).
            // The JSON projection does not carry parsed deployed-storage
            // evidence (its `onDisk` sub-object is not lifted), so the
            // deployed-storage channel is absent here.
            let typeEvidence =
                resolveAttributeType
                    rawDataType lengthOpt precisionOpt scaleOpt externalDatabaseType None
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
                        rawOpt
                        |> Option.map (fun r ->
                            // WP-3 (F11): a V1-JSON `"default": ""` on a Text
                            // attribute is the platform's empty-string default
                            // — `TextLit ""` (`DEFAULT N''`), no longer NullLit.
                            // On a non-text type an empty default keeps its
                            // pre-WP-3 `DEFAULT NULL` rendering (ambiguous V1
                            // authoring; refusing would fail whole-model ingest).
                            // DECISIONS 2026-07-18 (#669 M-1): the authored
                            // classifier discriminates niladic calls
                            // (`getutcdate()` — the callable expression) and
                            // SQL-quoted text forms (`''` — the value inside
                            // the quotes) before the value lift.
                            match p, r with
                            | (PrimitiveType.Text | PrimitiveType.Binary), _ ->
                                SqlLiteral.ofAuthoredDefault p r
                                |> Option.defaultValue (SqlLiteral.ofRaw p (Some r))
                            | _, "" -> SqlLiteral.NullLit
                            | _ ->
                                SqlLiteral.ofAuthoredDefault p r
                                |> Option.defaultValue (SqlLiteral.ofRaw p (Some r)))
                    | _ -> None
            let columnNameDU = ColumnName.create physicalName
            match nameDU, key, typeEvidence, columnNameDU with
            | Ok n, Ok k, Ok (p, storage), Ok physicalColumnName ->
                Result.success
                    { SsKey        = k
                      Name         = n
                      Type         = p
                      // F1/F10 (audit 2026-06-17): the JSON source exposes neither
                      // collation nor a non-default identity seed.
                      Column       = { ColumnName = physicalColumnName; IsNullable = not mandatory; Collation = None; Identity = None }
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
                      SqlStorage         = Some storage
                      // WP8 / NM-72 — authored Service-Studio order from
                      // the `order` JSON property (sourced from the
                      // rowset SQL's `a.[Order_Num]`).
                      Order              = orderOpt }
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
                            // M4 — the JSON source carries only hasDbConstraint;
                            // trust defaults to true (a reflected FK is trusted
                            // unless IsNoCheck), normalized via the DU constructor.
                            ConstraintState = ConstraintState.ofLegacyBooleans hasDbConstraint true })
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

    let parseJsonString (json: string) : Result<Catalog> =
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

