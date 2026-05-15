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
    /// V1 rowset 1 — `#E` modules; chapter 3.2 slice 1. Hand-written
    /// F# transcription of V1's `OutsystemsModuleRow` DTO at
    /// `IOutsystemsMetadataReader.cs:71-87`, mapped to V2 algebraic
    /// vocabulary (`Module` / `Espace` aliasing per V1 OSSYS
    /// convention; the `Espace*` field names mirror V1's SQL surface).
    /// `EspaceSsKey` is the load-bearing addition over the JSON path
    /// (which drops it via `SnapshotJsonBuilder`'s field selection).
    ///
    /// **Slice 3 extension:** `EspaceKind` lifts V1's `ossys_Espace.EspaceKind`
    /// string (rowset 1; `OutsystemsModuleRow.EspaceKind`). Refines
    /// rule 17's `Origin` translation from the JSON-path two-way
    /// placeholder (`isExternal → ExternalViaIntegrationStudio`) to
    /// the three-way real (OsNative / ExternalViaIntegrationStudio /
    /// ExternalDirect). Empirical V1 values observed in fixtures:
    /// `"eSpace"` for normal modules (V1 test seed at
    /// tests/Fixtures/sql/model.edge-case.seed.sql:97-99). The IS-
    /// extension marker is conventionally `"Extension"` per
    /// `DECISIONS 2026-05-19 — rule 17` ("Extension" — or whatever
    /// the IS-marker turns out to be); this slice adopts `"Extension"`
    /// as the operative marker until a real V1 production sample
    /// surfaces a different string. Nullable (V1 column is nullable).
    type ModuleRow =
        {
            EspaceId       : int
            EspaceName     : string
            IsSystemModule : bool
            IsActive       : bool
            EspaceKind     : string option
            EspaceSsKey    : System.Guid option
        }

    /// V1 rowset 2 — `#Ent` entities; chapter 3.2 slice 1. Mapped to
    /// V2's `Kind` algebraic vocabulary (V2 uses `Kind` where V1 uses
    /// `Entity`). FK to `ModuleRow.EspaceId` (linkage flat across
    /// `RowsetBundle.Modules`/`RowsetBundle.Kinds`/`RowsetBundle.Attributes`,
    /// matching V1 SQL's normalized rowset shape; `parseRowsetBundle`
    /// joins on load). `EntitySsKey` + `PrimaryKeySsKey` are the
    /// load-bearing additions; `EspaceKind` (slice 3) on `ModuleRow`
    /// distinguishes Origin three-way.
    ///
    /// **Slice 4 extension:** `IsSystemEntity` lifts V1's
    /// `ossys_Entity.Is_System` column (rowset 2; previously dropped
    /// at the JSON projection layer). Lifts into a new V2 IR refinement
    /// — `ModalityMark.SystemOwned` — payload-free mark sibling to
    /// `TenantScoped` / `SoftDeletable`. Rationale for the IR
    /// refinement choice (boundary-discipline question per chapter
    /// 3.2 open):
    ///   - Flat `Kind.IsSystem: bool` rejected — V2 convention avoids
    ///     `Is*` booleans in the IR.
    ///   - `Origin` expansion (`OsNativeSystem`) rejected — system-
    ///     entity is orthogonal to native-vs-external; conflating
    ///     axes loses information.
    ///   - New `Kind.Stewardship: Stewardship` DU rejected — heavier
    ///     surface than evidence demands; defer until a second
    ///     stewardship axis surfaces (e.g., third-party-managed).
    ///   - `ModalityMark.SystemOwned` selected — matches existing
    ///     orthogonal-axes-list pattern; payload-free; consumers
    ///     walk `kind.Modality |> List.contains SystemOwned`.
    type KindRow =
        {
            EntityId          : int
            EspaceId          : int
            EntityName        : string
            PhysicalTableName : string
            DbSchema          : string
            IsStatic          : bool
            IsExternal        : bool
            IsSystemEntity    : bool
            IsActive          : bool
            EntitySsKey       : System.Guid option
            PrimaryKeySsKey   : System.Guid option
            /// Chapter A.0' slice α — Description lift. Carries V1's
            /// `ossys_Entity.Description` column. `None` when V1's
            /// source row is NULL.
            Description       : string option
        }

    /// V1 rowset 3 — `#Attr` attributes; chapter 3.2 slice 1. FK to
    /// `KindRow.EntityId`. `AttrSsKey` is the load-bearing addition
    /// over the JSON path. Per session-21 inactive-records filter:
    /// `IsActive=false` rows are dropped at the boundary (parity with
    /// the JSON path at `parseKind`'s attribute filter).
    type AttributeRow =
        {
            AttrId       : int
            EntityId     : int
            AttrName     : string
            PhysicalCol  : string
            DataType     : string
            IsMandatory  : bool
            IsIdentifier : bool
            IsAutoNumber : bool
            Length       : int option
            Precision    : int option
            Scale        : int option
            AttrSsKey    : System.Guid option
            IsActive     : bool
            /// Chapter A.0' slice α — Description lift. Carries V1's
            /// `ossys_EntityAttr.Description` column. `None` when V1's
            /// source row is NULL.
            Description  : string option
        }

    /// V1 rowset 4 — `#RefResolved` resolved-reference rows; chapter
    /// 3.2 slice 2. One row per attribute that bears a foreign-key
    /// reference. FK to `AttributeRow.AttrId`. `RefEntityName`
    /// resolves the target kind by name (V1's `#RefResolved`
    /// aggregates the cross-module name resolution).
    /// `DeleteRuleCode` + `HasDbConstraint` come from V1's `#FkReality`
    /// (rowset 12); denormalized here so the V2 adapter sees
    /// per-reference completeness without joining a third rowset.
    /// The future C# loader pre-joins V1's `#RefResolved` ⊕ `#FkReality`
    /// → flat `ReferenceRow` records; in-memory test fixtures
    /// construct these literals directly. Same-module assumption
    /// (rule 16): `RefEntityName` is resolved within the source
    /// attribute's module. Cross-module FK references are a
    /// documented deferral (DECISIONS Active deferrals index —
    /// "Cross-module FK IR refinement").
    type ReferenceRow =
        {
            AttrId          : int
            RefEntityName   : string
            DeleteRuleCode  : string option
            HasDbConstraint : bool
        }

    /// V1 rowset bundle — the in-memory carrier the future C# SqlClient
    /// loader produces; in-memory test fixtures construct directly. Per
    /// `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` §2-§3: hand-written F#
    /// records mirroring V1's first three rowsets (modules / entities /
    /// attributes); extends under empirical pressure as future
    /// lossiness members surface (`EspaceKind` at slice 3;
    /// `IsSystemEntity` at slice 4; per-table column structure / check
    /// constraints from rowsets 6+ at deferred slices). Flat-list shape
    /// matches V1's normalized rowset SQL output; `parseRowsetBundle`
    /// joins by FK ID columns at load time.
    ///
    /// **Slice 2 extension:** `References` lifts V1's `#RefResolved`
    /// (rowset 4) ⊕ `#FkReality` (rowset 12) into a flat-list join
    /// surface. Adding the field is a closed-DU-style extension on
    /// the record (existing literal sites must add `References = []`
    /// explicitly; the empirical-test discipline applies — the
    /// changed-callers walk catches surprises at compile time).
    type RowsetBundle =
        {
            Modules    : ModuleRow list
            Kinds      : KindRow list
            Attributes : AttributeRow list
            References : ReferenceRow list
        }

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

    let private adapterError (code: string) (message: string) : ValidationError =
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
    let private propagateOrFallback
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

    let private moduleSsKey (moduleName: string) : Result<SsKey> =
        SsKey.synthesized "OS_MOD" moduleName

    let private kindSsKey (moduleName: string) (entityName: string) : Result<SsKey> =
        SsKey.synthesizedComposite "OS_KIND" [ moduleName; entityName ]

    let private attributeSsKey
        (moduleName: string) (entityName: string) (attrName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_ATTR" [ moduleName; entityName; attrName ]

    /// Reference SsKey synthesis (session 19). The reference identifies
    /// by its source coordinate — `<srcModule>_<srcEntity>_<viaAttr>`
    /// uniquely names an FK because each attribute carries at most one
    /// outgoing reference in V1's metadata.
    let private referenceSsKey
        (sourceModuleName: string) (sourceEntityName: string) (viaAttrName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_REF" [ sourceModuleName; sourceEntityName; viaAttrName ]

    /// Index SsKey synthesis (session 22). Indexes identify by their
    /// V1 IndexName, scoped to the entity. V1's `IndexName` is unique
    /// per entity per V1's SQL extraction (`#AllIdx` keyed by
    /// `EntityId, IndexName`).
    let private indexSsKey
        (moduleName: string) (entityName: string) (indexName: string)
        : Result<SsKey> =
        SsKey.synthesizedComposite "OS_IDX" [ moduleName; entityName; indexName ]

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
    /// Chapter A.0' slice β — used by `parseAttribute` / `parseKind` /
    /// `parseModule` to populate `Attribute.IsActive` / `Kind.IsActive`
    /// / `Module.IsActive` in the V2 IR. Supersedes session-21's
    /// silent-drop filter usage (per `DECISIONS 2026-05-15 — A.0' slice
    /// β` amendment); the helper now carries the value rather than
    /// guarding a drop.
    let private isActiveOrDefault (element: JsonElement) : bool =
        match element.TryGetProperty("isActive") with
        | true, value when value.ValueKind = JsonValueKind.False -> false
        | _ -> true

    let private getBool (element: JsonElement) (name: string) : Result<bool> =
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
    let private getIntFlag (element: JsonElement) (name: string) : Result<bool> =
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

    /// Optional string property. Returns `None` for missing or
    /// JSON-null values, `Some s` for non-null strings, `Error`
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

    /// Optional non-negative-integer property reader. Returns
    /// `None` when the property is missing OR explicitly null;
    /// `Some n` when the property is present and parseable.
    /// Per session-32: V1's `osm_model.json` carries length /
    /// precision / scale as nullable JSON numbers (or omitted
    /// when not applicable to the type), so the adapter must
    /// gracefully absorb absence.
    let private getOptionalInt (element: JsonElement) (name: string) : int option =
        match element.TryGetProperty(name) with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt32() with
            | true, n -> Some n
            | _ -> None
        | _ -> None

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
        // Chapter A.0' slice β — IsActive carry-through. Default-true
        // per V1's SQL `ISNULL(Is_Active, 1)` semantics; supersedes
        // session-21's silent-drop disposition.
        let isActive = isActiveOrDefault attrJson
        match nameResult, physicalResult, dataTypeStr, isMandatory, isIdentifier with
        | Ok rawName, Ok physicalName, Ok rawDataType,
          Ok mandatory, Ok identifier ->
            let nameDU       = Name.create rawName
            let key          = attributeSsKey moduleName entityName rawName
            let primitive    = parsePrimitiveType rawDataType
            let description  =
                match descriptionResult with
                | Ok d -> d
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
            // Identity = isAutoNumber per V1 convention (only
            // primary-key columns marked isAutoNumber=true map to
            // SQL Server IDENTITY).
            let isIdentity =
                match isAutoNumber with
                | Ok true -> true
                | _ -> false
            match nameDU, key, primitive with
            | Ok n, Ok k, Ok p ->
                Result.success
                    { SsKey        = k
                      Name         = n
                      Type         = p
                      Column       = { ColumnName = physicalName; IsNullable = not mandatory }
                      IsPrimaryKey = identifier
                      IsMandatory  = mandatory
                      Length       = lengthOpt
                      Precision    = precisionOpt
                      Scale        = scaleOpt
                      IsIdentity   = isIdentity
                      Description  = description
                      IsActive     = isActive }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                // Substantive causes (e.g., `adapter.osm.unmappedDataType`
                // from `parsePrimitiveType`) survive the attribute-level
                // wrap.
                propagateOrFallback
                    [ Result.errors nameDU
                      Result.errors key
                      Result.errors primitive ]
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
    ///   - `isExternal: false`                          → OsNative
    ///   - `isExternal: true` AND EspaceKind = "Extension" → ExternalViaIntegrationStudio
    ///   - `isExternal: true` otherwise                  → ExternalDirect
    ///
    /// Case-insensitive comparison on the marker (V1's column is
    /// nvarchar(50); historical samples have varied in capitalization
    /// across V1 versions). Null EspaceKind on an external entity
    /// resolves to ExternalDirect — the absence of an IS marker
    /// witnesses the absence of an IS step.
    ///
    /// Rule 17 now refines from the session-20 placeholder
    /// (collapsing to ExternalViaIntegrationStudio for all external
    /// entities) to the empirically-rooted three-way real. The JSON
    /// path's `parseOrigin` is the still-bounded sibling — same
    /// signature, same JSON-projection-lossiness disposition.
    let private parseOriginFromRowset
        (isExternal: bool) (espaceKind: string option) : Origin =
        if not isExternal then OsNative
        else
            let isIsExtension =
                match espaceKind with
                | Some kind ->
                    System.String.Equals(kind, "Extension", System.StringComparison.OrdinalIgnoreCase)
                | None -> false
            if isIsExtension then ExternalViaIntegrationStudio
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
                    Result.success (Some
                        { SsKey           = rKey
                          Name            = rName
                          SourceAttribute = srcKey
                          TargetKind      = tgtKey
                          OnDelete        = rule
                          // Slice ζ: User-FK detection deferred to a
                          // sibling pass at chapter 4.2's adapter-
                          // integration boundary. Defaults to false
                          // until the OSSYS-platform user-kind
                          // identification surface materializes (per
                          // V1 reference ModelUserSchemaGraphFactory.
                          // GetSyntheticUserForeignKeys); the chapter
                          // 4.2 close ritual codifies the trigger.
                          IsUserFk        = false })
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
            // Walk columns[]; filter isIncluded=true; sort by ordinal;
            // resolve each key column's attribute name to its V2 SsKey.
            let keyColResults =
                match indexJson.TryGetProperty("columns") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> List.choose (fun col ->
                        // Drop isIncluded=true (V1 included columns).
                        match col.TryGetProperty("isIncluded") with
                        | true, v when v.ValueKind = JsonValueKind.True -> None
                        | _ -> Some col)
                    |> List.map (fun col ->
                        // Best-effort ordinal extraction; missing
                        // ordinal sorts as 0 (preserves first-in-array
                        // for malformed-but-readable inputs).
                        let ordinal =
                            match col.TryGetProperty("ordinal") with
                            | true, o when o.ValueKind = JsonValueKind.Number ->
                                match o.TryGetInt32() with
                                | true, n -> n
                                | _       -> 0
                            | _ -> 0
                        let attrNameResult = getString col "attribute"
                        ordinal, attrNameResult)
                    |> List.sortBy fst
                    |> List.map snd
                    |> List.map (fun attrNameRes ->
                        match attrNameRes with
                        | Error es -> Error es
                        | Ok an -> attributeSsKey moduleName entityName an)
                | _ -> []
            // Per `Result.aggregate` (chapter-3.1 close audit): the
            // canonical accumulator for `Result<'a> seq` collapses to
            // `Result<'a list>` with errors aggregated (not short-
            // circuited). Retires the O(N²) `xs @ [x]` fold pattern
            // per `DECISIONS 2026-05-09` Big-O discipline.
            let foldedKeyCols = Result.aggregate keyColResults
            match indexKey, indexNm, foldedKeyCols with
            | Ok k, Ok n, Ok cols ->
                Result.success
                    { SsKey        = k
                      Name         = n
                      Columns      = cols
                      IsUnique     = isUnique
                      IsPrimaryKey = isPrimary }
            | _ ->
                // Propagate underlying errors via `propagateOrFallback`.
                propagateOrFallback
                    [ Result.errors indexKey
                      Result.errors indexNm
                      Result.errors foldedKeyCols ]
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

    let private parseKind
        (moduleName: string) (entityJson: JsonElement)
        : Result<Kind> =
        let nameResult       = getString entityJson "name"
        let physicalResult   = getString entityJson "physicalName"
        let schemaResult     = getString entityJson "db_schema"
        let isStaticResult   = getBool   entityJson "isStatic"
        let isExternalResult = getBool   entityJson "isExternal"
        // Chapter A.0' slice α — Description lift. Same defensive
        // read shape as parseAttribute.
        let descriptionResult = getOptionalString entityJson "description"
        // Chapter A.0' slice β — IsActive carry-through. Default-true
        // per V1's `ISNULL(Is_Active, 1)`; supersedes session-21's
        // silent-drop disposition. Attributes carry through with
        // their own IsActive value rather than being filtered.
        let isActive = isActiveOrDefault entityJson
        match nameResult, physicalResult, schemaResult, isStaticResult, isExternalResult with
        | Ok entityName, Ok physicalName, Ok schema,
          Ok isStatic, Ok isExternal ->
            let description =
                match descriptionResult with
                | Ok d -> d
                | Error _ -> None
            let kindKey   = kindSsKey moduleName entityName
            let kindName  = Name.create entityName
            // Chapter A.0' slice β — retires session-21's attribute
            // inactive-records filter. All attributes flow to
            // parseAttribute; each carries its own `IsActive` flag.
            // Downstream emitters decide filtering policy.
            let attrJsonList =
                match entityJson.TryGetProperty("attributes") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray() |> Seq.toList
                | _ -> []
            let attrsResults =
                attrJsonList
                |> List.map (parseAttribute moduleName entityName)
            let refResults =
                attrJsonList
                |> List.map (parseReference moduleName entityName)
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
                    |> List.map (parseIndex moduleName entityName)
                | _ -> []
            let foldedIdx = Result.aggregate indexResults
            match kindKey, kindName, foldedAttrs, foldedRefs, foldedIdx with
            | Ok k, Ok n, Ok attrs, Ok refs, Ok idxs ->
                let modality =
                    if isStatic then [ Static [] ] else []
                Result.success
                    { SsKey       = k
                      Name        = n
                      Origin      = parseOrigin isExternal
                      Modality    = modality
                      Physical    = { Schema = schema; Table = physicalName }
                      Attributes  = attrs
                      References  = refs
                      Indexes     = idxs
                      Description = description
                      IsActive    = isActive }
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
                      Result.errors foldedIdx ]
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
            // Chapter A.0' slice β — module-level IsActive carry-
            // through. Default-true per V1's `ISNULL(Is_Active, 1)`;
            // first exercised at slice β (was named in session-21 as
            // "not yet handled by the parser"). Supersedes the
            // attribute / entity filter at this site too.
            let isActive = isActiveOrDefault moduleJson
            // Chapter A.0' slice β — retires session-21's entity
            // inactive-records filter. All entities flow to parseKind;
            // each carries its own `IsActive` flag. Downstream
            // emitters decide filtering policy.
            let entitiesArr =
                match moduleJson.TryGetProperty("entities") with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.toList
                    |> List.map (parseKind rawName)
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
                Module.create k n kinds isActive
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
                |> List.map parseModule
            let folded = Result.aggregate modulesList
            match folded with
            | Ok modules ->
                // Per DECISIONS pillar 6: boundary adapter flows
                // through `Catalog.create` (aggregate-root invariant
                // check) rather than record-literal construction.
                Catalog.create modules
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
        let primitive = parsePrimitiveType row.DataType
        match nameDU, key, primitive with
        | Ok n, Ok k, Ok p ->
            Result.success
                { SsKey        = k
                  Name         = n
                  Type         = p
                  Column       = { ColumnName = row.PhysicalCol
                                   IsNullable = not row.IsMandatory }
                  IsPrimaryKey = row.IsIdentifier
                  IsMandatory  = row.IsMandatory
                  Length       = row.Length
                  Precision    = row.Precision
                  Scale        = row.Scale
                  IsIdentity   = row.IsAutoNumber
                  Description  = row.Description
                  // Chapter A.0' slice β — IsActive carry-through;
                  // supersedes session-21's silent-drop disposition.
                  IsActive     = row.IsActive }
        | _ ->
            // Propagate underlying errors via `propagateOrFallback`.
            propagateOrFallback
                [ Result.errors nameDU
                  Result.errors key
                  Result.errors primitive ]
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
        (moduleName: string)
        (entityName: string)
        (attrRow: AttributeRow)
        (refRow: ReferenceRow)
        : Result<Reference> =
        let refKey     = referenceSsKey moduleName entityName attrRow.AttrName
        let refName    = Name.create attrRow.AttrName
        let srcAttrKey = attributeSsKeyFromRow moduleName entityName attrRow
        let tgtKindKey = kindSsKey moduleName refRow.RefEntityName
        let onDelete   = parseDeleteRule refRow.DeleteRuleCode
        match refKey, refName, srcAttrKey, tgtKindKey, onDelete with
        | Ok rKey, Ok rName, Ok srcKey, Ok tgtKey, Ok rule ->
            Result.success
                { SsKey           = rKey
                  Name            = rName
                  SourceAttribute = srcKey
                  TargetKind      = tgtKey
                  OnDelete        = rule
                  // Slice ζ: User-FK detection deferred at the
                  // rowset adapter (same as the JSON adapter; both
                  // depend on the OSSYS-platform user-kind
                  // identification surface that lands at the
                  // chapter 4.2 adapter-integration boundary).
                  IsUserFk        = false }
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

    let private parseKindRow
        (moduleName: string)
        (moduleEspaceKind: string option)
        (attributesByEntity: Map<int, AttributeRow list>)
        (referencesByAttr: Map<int, ReferenceRow list>)
        (kindRow: KindRow)
        : Result<Kind> =
        let kindKey  = kindSsKeyFromRow moduleName kindRow
        let kindName = Name.create kindRow.EntityName
        // Chapter A.0' slice β — retires session-21's attribute
        // inactive-records filter (rowset-path parity with JSON
        // path). All attributes flow to parseAttributeRow; each
        // carries its own `IsActive` flag. References on inactive
        // attributes also carry through (the chained reference-drop
        // behavior retires too; see DECISIONS 2026-05-15 slice β
        // amendment for the disposition rationale).
        let attrRows =
            Map.tryFind kindRow.EntityId attributesByEntity
            |> Option.defaultValue []
        let attrResults =
            attrRows
            |> List.map (parseAttributeRow moduleName kindRow.EntityName)
        let foldedAttrs = Result.aggregate attrResults
        // Slice 2: per-attribute reference build. For each surviving
        // attribute, look up its reference rows by AttrId; multi-row
        // collations (composite FKs) are not carried at slice 2 — V2's
        // `Reference` is single-attribute today; cross-FK composite
        // case is a documented deferral. Reference order is
        // declared-attribute order (matches the JSON path's
        // attribute-walk shape).
        let refResults =
            attrRows
            |> List.collect (fun a ->
                Map.tryFind a.AttrId referencesByAttr
                |> Option.defaultValue []
                |> List.map (parseReferenceRowFor moduleName kindRow.EntityName a))
        let foldedRefs = Result.aggregate refResults
        match kindKey, kindName, foldedAttrs, foldedRefs with
        | Ok k, Ok n, Ok attrs, Ok refs ->
            // Modality marks list. `Static []` (parity with parseKind:
            // populations NOT carried by rowsets 1-3; defer to a later
            // slice surfacing V1 rowset 19+); `SystemOwned` (slice 4:
            // lifts V1's IsSystemEntity into the V2 IR). Order is
            // declaration order — Static first if present, SystemOwned
            // second if present. Future ModalityMark variants append.
            let modality =
                [
                    if kindRow.IsStatic       then yield Static []
                    if kindRow.IsSystemEntity then yield SystemOwned
                ]
            Result.success
                { SsKey       = k
                  Name        = n
                  // Origin via parseOriginFromRowset (slice 3): three-way
                  // real driven by ModuleRow.EspaceKind. Refines the
                  // JSON-path's parseOrigin two-way placeholder.
                  Origin      = parseOriginFromRowset kindRow.IsExternal moduleEspaceKind
                  Modality    = modality
                  Physical    = { Schema = kindRow.DbSchema
                                  Table  = kindRow.PhysicalTableName }
                  Attributes  = attrs
                  References  = refs
                  // Indexes deferred to a future slice (rowsets 10-11
                  // #AllIdx / #IdxColsMapped). Empty at slice 2.
                  Indexes     = []
                  Description = kindRow.Description
                  // Chapter A.0' slice β — IsActive carry-through.
                  IsActive    = kindRow.IsActive }
        | _ ->
            // Propagate underlying errors via `propagateOrFallback`
            // (codified at two-consumer threshold; same surface as
            // parseKind on the JSON path). Substantive causes — e.g.,
            // `adapter.osm.unmappedDeleteRule` from `parseDeleteRule`
            // — survive the kind-level wrap.
            propagateOrFallback
                [ Result.errors kindKey
                  Result.errors kindName
                  Result.errors foldedAttrs
                  Result.errors foldedRefs ]
                (fun () ->
                    adapterError
                        "kindRowBuild"
                        (sprintf
                            "Failed to build kind '%s' in module '%s' from rowset bundle."
                            kindRow.EntityName moduleName))

    let private parseModuleRow
        (kindsByEspace: Map<int, KindRow list>)
        (attributesByEntity: Map<int, AttributeRow list>)
        (referencesByAttr: Map<int, ReferenceRow list>)
        (moduleRow: ModuleRow)
        : Result<Module> =
        let modKey  = moduleSsKeyFromRow moduleRow
        let modName = Name.create moduleRow.EspaceName
        // Chapter A.0' slice β — retires session-21's entity inactive-
        // records filter (rowset-path parity with JSON path). All kinds
        // flow to parseKindRow; each carries its own `IsActive` flag.
        let kindRows =
            Map.tryFind moduleRow.EspaceId kindsByEspace
            |> Option.defaultValue []
        let kindResults =
            kindRows
            |> List.map (
                parseKindRow
                    moduleRow.EspaceName
                    moduleRow.EspaceKind
                    attributesByEntity
                    referencesByAttr)
        let foldedKinds = Result.aggregate kindResults
        match modKey, modName, foldedKinds with
        | Ok k, Ok n, Ok kinds ->
            Module.create k n kinds moduleRow.IsActive
        | _ ->
            // Propagate underlying errors via `propagateOrFallback`.
            // Substantive causes survive the module-level wrap.
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
        // Chapter A.0' slice β — retires session-21's module-level
        // inactive-records filter (rowset-path parity with JSON path).
        // All modules flow to parseModuleRow; each carries its own
        // `IsActive` flag to V2's IR.
        let moduleResults =
            bundle.Modules
            |> List.map (parseModuleRow kindsByEspace attributesByEntity referencesByAttr)
        match Result.aggregate moduleResults with
        | Ok modules -> Catalog.create modules
        | Error errors -> Error errors

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
