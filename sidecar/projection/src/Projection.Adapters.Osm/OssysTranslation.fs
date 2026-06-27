namespace Projection.Adapters.Osm

open System.Text.Json
open Projection.Core
open OssysRowsetTypes

/// OSSYS translation leaf layer — the shared, path-agnostic helpers both
/// the JSON path and the rowset path build IR fragments from: error
/// aggregation, the SsKey synthesizers, the JSON-element accessors, the
/// attribute-type resolution chain, FK delete-rule / SQL-action parsing,
/// and Origin derivation. Extracted verbatim from `CatalogReader`
/// (2026-06-04 R1 decomposition step 2). `CatalogReader` and the reader
/// modules `open` this. No `[<RequireQualifiedAccess>]`.
module OssysTranslation =
    let adapterError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "adapter.osm.%s" code) message

    /// Aggregate underlying errors from a list of `Result.errors`
    /// projections; fall back to a single `adapterError` if no
    /// underlying errors decompose out of the failure (theoretical
    /// case — the outer `match _ -> ...` arm fires on a combination
    /// the explicit branches didn't enumerate).
    ///
    /// Codified at the two-consumer threshold (CLAUDE.md operating-
    /// disciplines table — emergent primitives earn their place
    /// through multi-consumer demand). Four V1↔V2 build-failure
    /// collection sites share this exact shape: `parseKindRow` /
    /// `parseModuleRow` (rowset path; chapter 3.2 slices 1 + 2);
    /// `parseKind` / `parseModule` (JSON path; chapter 2 sessions
    /// 18–25, now corrected). Before extraction the rowset path
    /// carried the inline form and the JSON path swallowed the
    /// underlying error with a generic `kindBuild` / `moduleBuild`
    /// umbrella — losing the substantive cause (e.g.,
    /// `adapter.osm.unmappedDeleteRule` from `parseDeleteRule`).
    /// The helper makes the diagnostic surface honest across both
    /// translation paths uniformly.
    let propagateOrFallback
        (errorLists: ValidationError list list)
        (fallback: unit -> ValidationError) : Result<'a> =
        let combined = List.concat errorLists
        if List.isEmpty combined then Result.failureOf (fallback ())
        else Error combined

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

    // The synthesis source / basis split (slice 5.5 / `CHAPTER_3_PRESCOPE_
    // ARTIFACTBYKIND_REFACTOR.md` §7) makes A1's JSON-projection-lossiness
    // bound type-visible: `Synthesized (source, basis)` carries the
    // bounded variant tag, which downstream consumers can pattern-match
    // on. Each call site below names the source convention (`OS_MOD`,
    // `OS_KIND`, etc.) explicitly; the basis is the dot-separated
    // identifier coordinate.

    // Per the no-string-concatenation discipline (`DECISIONS
    // 2026-05-09`) + chapter-3.6 slice-δ: synthesis-basis composition
    // flows through `SsKey.synthesizedComposite` over typed component
    // lists; the typed `string list` IS the structure inside the
    // `Synthesized` DU, no `String.concat "_"` at the build path.
    // Strings emerge only at the terminal `SsKey.rootOriginal`
    // display projection.

    let moduleSsKey (moduleName: string) : Result<SsKey> =
        SsKey.synthesized "OS_MOD" moduleName

    let kindSsKey (moduleName: string) (entityName: string) : Result<SsKey> =
        SsKey.synthesizedComposite "OS_KIND" [ moduleName; entityName ]

    let attributeSsKey
        (moduleName: string) (entityName: string) (attrName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_ATTR" [ moduleName; entityName; attrName ]

    /// Reference SsKey synthesis (session 19). The reference identifies
    /// by its source coordinate — `<srcModule>_<srcEntity>_<viaAttr>`
    /// uniquely names an FK because each attribute carries at most one
    /// outgoing reference in V1's metadata.
    let referenceSsKey
        (sourceModuleName: string) (sourceEntityName: string) (viaAttrName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_REF" [ sourceModuleName; sourceEntityName; viaAttrName ]

    /// Index SsKey synthesis (session 22). Indexes identify by their
    /// V1 IndexName, scoped to the entity. V1's `IndexName` is unique
    /// per entity per V1's SQL extraction (`#AllIdx` keyed by
    /// `EntityId, IndexName`).
    let indexSsKey
        (moduleName: string) (entityName: string) (indexName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_IDX" [ moduleName; entityName; indexName ]

    /// Trigger SsKey synthesis (chapter A.0' slice γ). Triggers
    /// identify by their V1 name scoped to the entity they fire on
    /// (a trigger is owned by exactly one table per SQL Server
    /// semantics).
    let triggerSsKey
        (moduleName: string) (entityName: string) (triggerName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_TRG" [ moduleName; entityName; triggerName ]

    /// Sequence SsKey synthesis (chapter A.0' slice δ). Sequences
    /// identify by schema + name (a sequence is a top-level
    /// schema-scoped object per SQL Server semantics; SsKey carries
    /// the full coordinate to disambiguate across modules / schemas).
    let sequenceSsKey
        (schemaName: string) (sequenceName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_SEQ" [ schemaName; sequenceName ]

    /// ColumnCheck SsKey synthesis (chapter A.0' slice ε). CHECK
    /// constraints identify by their declared name scoped to the
    /// entity. V1's `AttributeOnDiskCheckConstraint` carries a
    /// nullable name; when absent, the check is unnamed and the
    /// SsKey is derived from the entity + a synthetic discriminator
    /// at call time.
    let columnCheckSsKey
        (moduleName: string) (entityName: string) (checkName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_CHK" [ moduleName; entityName; checkName ]

    // -----------------------------------------------------------------------
    // JSON helpers — light wrappers over System.Text.Json.JsonElement.
    // These are private; they exist to keep the translation code
    // readable, not as a general-purpose JSON utility surface.
    // -----------------------------------------------------------------------

    let getProperty (element: JsonElement) (name: string) : Result<JsonElement> =
        match element.TryGetProperty(name) with
        | true, value -> Result.success value
        | _ ->
            Result.failureOf (
                adapterError
                    "missingProperty"
                    (sprintf "Required property '%s' not found." name))

    let getString (element: JsonElement) (name: string) : Result<string> =
        match getProperty element name with
        | Error errors -> Error errors
        | Ok value ->
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
    /// Chapter A.0' slice β — the session-21 inactive-records
    /// filter retired. This helper now populates the V2 IR's
    /// `Module.IsActive` / `Kind.IsActive` / `Attribute.IsActive`
    /// carriage fields instead of gating record inclusion. Per the
    /// pillar-9 harvest-dichotomy classification: filtering on a
    /// lifecycle flag is `OperatorIntent` (a Selection-axis choice),
    /// mis-placed at the adapter boundary, which is restricted to
    /// `DataIntent` carriage. Downstream emitters decide whether to
    /// suppress inactive records; no Selection-axis pass ships with
    /// slice β (deferred-with-trigger; IR-grows-under-evidence).
    let isActiveOrDefault (element: JsonElement) : bool =
        match element.TryGetProperty("isActive") with
        | true, value when value.ValueKind = JsonValueKind.False -> false
        | _ -> true

    let getBool (element: JsonElement) (name: string) : Result<bool> =
        match getProperty element name with
        | Error errors -> Error errors
        | Ok value ->
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
    let getIntFlag (element: JsonElement) (name: string) : Result<bool> =
        match getProperty element name with
        | Error errors -> Error errors
        | Ok value ->
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

    /// Optional V1 int-flag with a caller-supplied default. Mirrors
    /// V1's `ISNULL(<col>, <default>)` SQL idiom (used at
    /// `outsystems_model_export.sql:730` for `hasDbConstraint`, etc.).
    /// Convenience over `match getIntFlag … with Ok v -> v | Error _ ->
    /// default` — chapter 4.7 slice α consolidation. Returns the
    /// flag value when present + parseable; returns the default for
    /// absent / unparseable values (the failure case is itself the
    /// V1-COALESCE-equivalent silent default).
    let getOptionalIntFlag
        (element: JsonElement)
        (name: string)
        (defaultValue: bool)
        : bool =
        match getIntFlag element name with
        | Ok v -> v
        | Error _ -> defaultValue

    /// Optional bool property with caller-supplied default. Sibling
    /// of `getOptionalIntFlag` for fields V1 projects as JSON
    /// booleans (e.g., index `isPlatformAuto`).
    let getOptionalBool
        (element: JsonElement)
        (name: string)
        (defaultValue: bool)
        : bool =
        match getBool element name with
        | Ok v -> v
        | Error _ -> defaultValue

    /// Optional string property. Returns `None` for missing or
    /// JSON-null values, `Some s` for non-null strings, `Error`
    /// when the property exists but is not a string.
    let getOptionalString (element: JsonElement) (name: string) : Result<string option> =
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

    /// Normalize an `ossys_EntityAttr.Type` value to its mapping key.
    /// V1's `Osm.Smo/TypeMappingKeyNormalizer` is the donor: strip the
    /// `rt` runtime-type prefix (`rtText` → `Text`), drop separators
    /// (`_`, `-`, space), lowercase. Both the runtime form (`rtText`,
    /// `rtLongInteger`, `rtPhoneNumber`) and the bare domain form
    /// (`Text`, `LongInteger`) collapse to one key.
    let normalizeAttributeType (rawType: string) : string =
        let trimmed = rawType.Trim()
        let withoutRt =
            if trimmed.Length > 2
               && System.String.Equals(
                    trimmed.Substring(0, 2), "rt",
                    System.StringComparison.OrdinalIgnoreCase)
            then trimmed.Substring(2)
            else trimmed
        withoutRt
            .Replace("_", System.String.Empty)
            .Replace("-", System.String.Empty)
            .Replace(" ", System.String.Empty)
            .ToLowerInvariant()

    /// Resolve an OSSYS attribute's semantic category AND concrete SQL Server
    /// storage type. The mapping DECISIONS now live in pure Core
    /// (`OssysTypeMapping.tryParse`, recon #10 — property-testable without an OSSYS
    /// fixture, reusable by a second source adapter); this thin adapter shim keeps
    /// only what's the boundary's to own: turning an unmapped type into the
    /// `adapter.osm.unmappedDataType` refusal.
    let parseSemanticType
        (normalizedType: string)
        (length: int option)
        (precision: int option)
        (scale: int option)
        : Result<PrimitiveType * SqlStorageType> =
        match OssysTypeMapping.tryParse normalizedType length precision scale with
        | Some mapped -> Result.success mapped
        | None ->
            Result.failureOf (
                adapterError
                    "unmappedDataType"
                    (sprintf "DataType '%s' has no V2 PrimitiveType mapping yet." normalizedType))

    /// Full attribute-type resolution: semantic category + concrete
    /// storage, with the optional `external_dbType` override applied.
    /// V1's `TypeMappingPolicy.Resolve` priority is preserved — an
    /// `external_dbType` SQL-type string overrides the OSSYS-derived
    /// storage EXCEPT for `identifier` / `autonumber` / `longinteger`,
    /// which force the runtime mapping (so a `longinteger` stays
    /// `BIGINT` regardless of any external override).
    let resolveAttributeType
        (rawType: string)
        (length: int option)
        (precision: int option)
        (scale: int option)
        (externalDbType: string option)
        : Result<PrimitiveType * SqlStorageType> =
        let normalized = normalizeAttributeType rawType
        parseSemanticType normalized length precision scale
        |> Result.map (fun (pt, ossysStorage) ->
            let storage =
                match normalized with
                | "identifier" | "autonumber" | "longinteger" -> ossysStorage
                | _ ->
                    match externalDbType |> Option.bind (fun raw -> SqlStorageType.ofSqlType raw None None None) with
                    | Some overridden -> overridden
                    | None -> ossysStorage
            pt, storage)

    /// V1 reference_deleteRuleCode → V2 ReferenceAction. Mirrors the
    /// V1 mapping in `Osm.Smo/SmoEntityEmitter.cs`:
    ///   "Delete"  → Cascade
    ///   "Protect" → NoAction
    ///   "Ignore"  → NoAction
    ///   null      → NoAction (V1's TreatMissingDeleteRuleAsIgnore default)
    /// Other / unmapped values fail with adapter.osm.unmappedDeleteRule.
    let parseDeleteRule (code: string option) : Result<ReferenceAction> =
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

    /// Slice A.4.7'-prelude.row17-18-rowset-roundtrip — parse V1's
    /// `#FkReality.update_referential_action_desc` / `delete_referential
    /// _action_desc` (SQL Server vocabulary, distinct from
    /// `parseDeleteRule`'s OutSystems vocabulary). SQL Server's
    /// `sys.foreign_keys` columns emit `NO_ACTION` / `CASCADE` /
    /// `SET_NULL` / `SET_DEFAULT` (underscored uppercase per
    /// `*_referential_action_desc`). Returns `None` for unrecognized
    /// vocabulary (defensive — V2 omits the ON UPDATE clause rather
    /// than emitting an unsupported variant). `SET_DEFAULT` degrades
    /// to `None` because V2's `ReferenceAction` DU doesn't model it
    /// yet (lift trigger: a real-world FK with ON UPDATE SET DEFAULT
    /// surfaces in fixture data).
    let parseSqlForeignKeyAction (code: string option) : ReferenceAction option =
        match code with
        | None                -> None
        | Some "NO_ACTION"    -> Some NoAction
        | Some "CASCADE"      -> Some Cascade
        | Some "SET_NULL"     -> Some SetNull
        | Some "SET_DEFAULT"  -> None
        | Some _              -> None

    // -----------------------------------------------------------------------
    // Translation — V1 attribute → V2 Attribute.
    // -----------------------------------------------------------------------

    /// Optional non-negative-integer property reader. Returns
    /// `None` when the property is missing OR explicitly null;
    /// `Some n` when the property is present and parseable.
    /// Per session-32: V1's `osm_model.json` carries length /
    /// precision / scale as nullable JSON numbers (or omitted
    /// when not applicable to the type), so the adapter must
    /// gracefully absorb absence.
    let getOptionalInt (element: JsonElement) (name: string) : int option =
        match element.TryGetProperty(name) with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt32() with
            | true, n -> Some n
            | _ -> None
        | _ -> None

    let parseOrigin (isExternal: bool) : Origin =
        if isExternal then ExternalIndirect else Native

    /// Three-way `Origin` translation for the rowset path (chapter
    /// 3.2 slice 3). Refines `parseOrigin`'s two-way placeholder
    /// using V1's `EspaceKind` string (rowset 1; previously dropped
    /// at the JSON projection layer). Empirical evidence:
    ///   - `"eSpace"` (normal module; seen in V1 test seed
    ///     `tests/Fixtures/sql/model.edge-case.seed.sql:97-99`)
    ///   - `"Extension"` (conventional IS-extension marker per
    ///     `DECISIONS 2026-05-19 — rule 17`; not yet observed in
    ///     the V1 test corpus but documented as the operative
    ///     marker until a production sample surfaces otherwise)
    ///
    /// The translation:
    ///   - `isExternal: false`                          → Native
    ///   - `isExternal: true` AND EspaceKind = "Extension" → ExternalIndirect
    ///   - `isExternal: true` otherwise                  → ExternalDirect
    ///
    /// Case-insensitive comparison on the marker (V1's column is
    /// nvarchar(50); historical samples have varied in capitalization
    /// across V1 versions). Null EspaceKind on an external entity
    /// resolves to ExternalDirect — the absence of an IS marker
    /// witnesses the absence of an IS step.
    ///
    /// Rule 17 now refines from the session-20 placeholder
    /// (collapsing to ExternalIndirect for all external
    /// entities) to the empirically-rooted three-way real. The JSON
    /// path's `parseOrigin` is the still-bounded sibling — same
    /// signature, same JSON-projection-lossiness disposition.
    let parseOriginFromRowset
        (isExternal: bool) (espaceKind: string option) : Origin =
        if not isExternal then Native
        else
            let isIsExtension =
                match espaceKind with
                | Some kind ->
                    System.String.Equals(kind, "Extension", System.StringComparison.OrdinalIgnoreCase)
                | None -> false
            if isIsExtension then ExternalIndirect
            else ExternalDirect

    /// Extract a Reference from a V1 attribute that carries
    /// `isReference: 1` plus its `refEntity_*` and
    /// `reference_deleteRuleCode` fields. Returns `None` for
    /// non-reference attributes; `Some Reference` for FK-bearing
    /// ones; `Error` when the attribute claims isReference=1 but
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
