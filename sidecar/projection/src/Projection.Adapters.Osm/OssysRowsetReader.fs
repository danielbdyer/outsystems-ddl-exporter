namespace Projection.Adapters.Osm

open Projection.Core
open OssysRowsetTypes
open OssysTranslation

/// OSSYS rowset-path reader — translates V1's trailing metadata rowsets
/// (`RowsetBundle`) into a `Catalog`. Identity flips to native GUID
/// (`OssysOriginal`) where the rowset carries it; the path preserves
/// per-table column structure the JSON `FOR JSON PATH` aggregation
/// collapses. Extracted from `CatalogReader` (2026-06-04 R1 decomposition
/// step 4). `parseRowsetBundle` is the entry consumed by `CatalogReader.parse`.
module OssysRowsetReader =
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
        (entityPrimaryKeySsKey: System.Guid option)
        (row: AttributeRow)
        : Result<Attribute> =
        let nameDU    = Name.create row.AttrName
        let key       = attributeSsKeyFromRow moduleName entityName row
        // Primary-key identity from either OSSYS source: the explicit
        // attribute-level `Is_Identifier` flag when the estate exposes it,
        // or the entity-level `ossys_Entity.PrimaryKey_SS_Key` fallback —
        // some estates project attribute rows without `Is_Identifier`, and
        // without the fallback their entities reach the data lanes with
        // rows but no primary-key columns. The caller suppresses the
        // fallback (passes None) whenever ANY attribute of the entity
        // carries the explicit flag, so a disagreement between the two
        // sources can never mint a composite key here; the live path
        // names that disagreement as a divergence diagnostic instead.
        let matchesEntityPrimaryKey =
            match row.AttrSsKey, entityPrimaryKeySsKey with
            | Some attrKey, Some pkKey -> attrKey = pkKey
            | _ -> false
        // Resolve semantic category + concrete SQL Server storage from
        // the rowset's `Type` value (rt-prefix aware), the declared
        // length / precision / scale, any `ExternalColumnType` override,
        // and the deployed `#ColumnReality` storage evidence (consulted
        // for reference-shaped `bt*` attributes only, behind an explicit
        // external type). Same resolution as the JSON path.
        let typeEvidence =
            resolveAttributeType
                row.DataType row.Length row.Precision row.Scale row.ExternalDatabaseType row.DeployedStorage
        let columnNameDU = ColumnName.create row.PhysicalCol
        match nameDU, key, typeEvidence, columnNameDU with
        | Ok n, Ok k, Ok (p, storage), Ok physicalColumnName ->
            Result.success
                { SsKey        = k
                  Name         = n
                  Type         = p
                  // F1 (audit 2026-06-17): carry the source-declared collation
                  // (sys.columns.collation_name) so the emit re-states COLLATE.
                  // F10: the rowset path does not yet read a non-default identity
                  // seed (OS-native autonumbers are (1,1)) — None = default.
                  Column       = { ColumnName = physicalColumnName
                                   // Decision 2 (DECISIONS 2026-07-18; #669
                                   // M-3 / EF-18): a deployed NOT NULL is
                                   // preserved over a model-optional
                                   // declaration — deployed-schema over
                                   // model. Otherwise the model decides.
                                   IsNullable =
                                       match row.DeployedIsNullable with
                                       | Some false -> false
                                       | _          -> not row.IsMandatory
                                   Collation  = row.Collation
                                   Identity   = None }
                  IsPrimaryKey = row.IsIdentifier || matchesEntityPrimaryKey
                  IsMandatory  = row.IsMandatory
                  Length       = row.Length
                  Precision    = row.Precision
                  Scale        = row.Scale
                  IsIdentity   = row.IsAutoNumber
                  Description  = row.Description
                  IsActive     = row.IsActive
                  // Authored-default lift (DECISIONS 2026-07-18; the #669
                  // M-1 finding): `SqlLiteral.ofAuthoredDefault` classifies
                  // the LOGICAL `Default_Value` surface — a niladic call
                  // (`getutcdate()`) becomes the callable expression, a
                  // SQL-quoted text form (`''`, `'Draft'`) becomes the
                  // value inside the quotes, and the bare value forms
                  // project as before (an authored `False` on BIT emits
                  // `DEFAULT 0`). No-op defaults are suppressed inside the
                  // classifier: an absent or whitespace-only authored value
                  // carries nothing (a nullable column's implicit NULL is
                  // normal SQL behavior, not a configured default). This is
                  // the authored channel only — `#ColumnReality
                  // .DefaultDefinition` (the reflected constraint
                  // expression) stays un-lifted per matrix row 53's named
                  // trigger.
                  DefaultValue =
                      row.DefaultValue
                      |> Option.bind (SqlLiteral.ofAuthoredDefault p)
                  // Slice A.4.7'-prelude.row53-source-side: V1
                  // `#ColumnReality.DefaultConstraintName` (sys
                  // .default_constraints.name) → V2 DefaultName for
                  // round-trip parity with an AUTHORED
                  // `DF_<table>_<column>` constraint identifier. `None`
                  // when no named DEFAULT constraint exists at the
                  // deployed target — and for SQL Server's `DF__…`
                  // physical AUTO-names (double underscore + hex
                  // suffix): a reflected auto-name is an incidental
                  // property of the source instance, never the emitted
                  // naming contract.
                  DefaultName  =
                      row.DefaultConstraintName
                      |> Option.filter (fun raw ->
                          not (raw.StartsWith("DF__", System.StringComparison.OrdinalIgnoreCase)))
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
                              // #669 EF-21 (DECISIONS 2026-07-18): the
                              // deployed PERSISTED marking carries; the
                              // emission renders the keyword, closing the
                              // round-trip the audit proved dropped.
                              ComputedColumnConfig.create expr row.IsPersisted
                              |> Result.toOption)
                      else
                          None
                  ExtendedProperties = []
                  OriginalName = row.OriginalName
                  ExternalDatabaseType = row.ExternalDatabaseType
                  SqlStorage   = Some storage
                  // WP8 / NM-72 — Service-Studio authored order from the
                  // real `ossys_Entity_Attr.Order_Num` column (rowset
                  // path). `CanonicalizeIdentity` consumes it for emission
                  // column ordering (PK first, then Order ascending).
                  Order        = row.Order }
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
        // WP-1b (DECISIONS 2026-07-16) — the emitted ON DELETE action.
        // For a physically-backed FK the reflected `#FkReality.DeleteAction`
        // is database reality and outranks the model's delete-rule code
        // (E1: mirror `sys.foreign_keys`); a logical-only reference (no
        // reflected FK) keeps the model rule. `chooseOnDeleteAction`
        // encapsulates the preference; `deleteRuleDivergences` (the
        // OssysSql runner) surfaces the named diagnostic when the model and
        // the reflected action disagree, so reality wins the value while the
        // disagreement is announced, never silently swallowed.
        let onDelete   = chooseOnDeleteAction refRow.DeleteRuleCode refRow.ReflectedOnDelete
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
        (definition: string)
        (row: ColumnCheckRow)
        : Result<ColumnCheck> =
        let chkKey  = columnCheckSsKey moduleName entityName row.ConstraintName
        let chkName = Name.create row.ConstraintName
        match chkKey, chkName with
        | Ok k, Ok n ->
            ColumnCheck.create k (Some n) definition row.IsNotTrusted
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
        // Entity-level primary-key fallback: `KindRow.PrimaryKeySsKey`
        // recovers PK identity on estates whose attribute rows lack
        // `Is_Identifier`. This is a recovery rule for missing metadata,
        // not a second PK-selection policy — whenever ANY attribute
        // carries the explicit flag, the fallback is suppressed so the
        // two sources can never compose into an invented composite key
        // (a disagreement is surfaced by the live path's divergence
        // diagnostics, not resolved here).
        let entityPrimaryKeySsKey =
            if attrRows |> List.exists (fun r -> r.IsIdentifier) then None
            else kindRow.PrimaryKeySsKey
        let attrResults =
            attrRows
            |> Bench.iterMap "adapter.osm.parse.rowsetAttribute" (parseAttributeRow moduleName kindRow.EntityName entityPrimaryKeySsKey)
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
            // A `Definition = None` row is the VIEW-DEFINITION-less read
            // (the managed-cloud grant): the constraint exists but its
            // body is unreadable, and `ColumnCheck` cannot represent a
            // definition-less check — the row is SKIPPED (a named
            // erasure: ColumnChecks are physical-realization artifacts
            // the shape verdict strips (`Readiness.toLogicalShape`) and
            // the data plane never consumes; a privileged read still
            // carries them). 2026-07-06, the phase-2 mock-env program.
            |> List.choose (fun row -> row.Definition |> Option.map (fun d -> row, d))
            |> Bench.iterMap "adapter.osm.parse.rowsetColumnCheck" (fun (row, d) -> parseColumnCheckRowFor moduleName kindRow.EntityName d row)
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

    /// Stable notice codes for the bundle-normalization erasures (the
    /// `<domain>.<subject>.<problem>` convention; sites emit codes, the
    /// Voice owns copy).
    [<Literal>]
    let CodeModuleEntityLess = "adapter.ossys.module.entityLess"

    [<Literal>]
    let CodeKindInactiveShadow = "adapter.ossys.kind.inactiveShadow"

    /// The bundle NORMALIZATION pass — the named erasures a raw OSSYS
    /// rowset bundle needs before the aggregate-root smart constructors
    /// see it (2026-07-07, the live partial-transfer program; readiness
    /// log Entries 23 + 24). Two steps, applied in order:
    ///
    ///   1. **Inactive-shadow resolution** (`CodeKindInactiveShadow`).
    ///      The same entity SS_Key on multiple kind rows is the routine
    ///      trace of an entity MOVED between modules — the old espace
    ///      keeps an INACTIVE `ossys_Entity` row with the same SS_Key
    ///      (visible whenever the read includes inactive entities).
    ///      When exactly one duplicate is active, the active row is
    ///      carried, each inactive shadow is dropped, and references
    ///      aimed at a shadow's EntityId re-aim at the survivor (same
    ///      identity — the GUID is the conserved charge). Any other
    ///      duplicate shape (two active; all inactive) is carried
    ///      UNCHANGED — the A4 refusal at `Catalog.create` is the
    ///      correct terminal for a contradiction only the operator can
    ///      adjudicate.
    ///
    ///   2. **Entity-less module skip** (`CodeModuleEntityLess`; V1
    ///      parity — "Module 'X' contains no entities and will be
    ///      skipped", V1's `ModuleDocumentMapper` /
    ///      `ModelDeserializerFacade` / `FullExportApplicationService`).
    ///      An espace with no entities carries nothing this engine
    ///      publishes or transfers, and `Module.create` (LR1 / A39)
    ///      rightly refuses an empty-kinds module. Computed AFTER step 1,
    ///      so a module whose only entity was a dropped shadow skips too.
    ///
    /// Pure, deterministic (notices ordered by name then id within each
    /// step), and idempotent — a normalized bundle re-normalizes to
    /// itself with zero notices. `parseRowsetBundle` applies it
    /// internally (every consumer gets the semantics); the live read
    /// (`LiveModelRead`) calls it directly to surface the notices, so
    /// no erasure is ever silent.
    let normalizeBundle (bundle: RowsetBundle) : RowsetBundle * DiagnosticEntry list =
        // -- step 1: inactive-shadow resolution ---------------------------
        let moduleNameByEspaceId =
            bundle.Modules |> List.map (fun m -> m.EspaceId, m.EspaceName) |> Map.ofList
        let moduleNameOf (espaceId: int) : string =
            Map.tryFind espaceId moduleNameByEspaceId
            |> Option.defaultValue (sprintf "espace-%d" espaceId)
        let resolvedDuplicates =
            bundle.Kinds
            |> List.choose (fun k -> k.EntitySsKey |> Option.map (fun g -> g, k))
            |> List.groupBy fst
            |> List.choose (fun (ssKey, pairs) ->
                let kinds = pairs |> List.map snd
                if List.length kinds < 2 then None
                else
                    match kinds |> List.partition (fun k -> k.IsActive) with
                    | [ survivor ], shadows -> Some (ssKey, survivor, shadows)
                    | _ -> None)
        let shadowEntityIds =
            resolvedDuplicates
            |> List.collect (fun (_, _, shadows) -> shadows |> List.map (fun s -> s.EntityId))
            |> Set.ofList
        let survivorEntityIdByShadow =
            resolvedDuplicates
            |> List.collect (fun (_, survivor, shadows) ->
                shadows |> List.map (fun s -> s.EntityId, survivor.EntityId))
            |> Map.ofList
        let shadowNotices =
            resolvedDuplicates
            |> List.sortBy (fun (_, survivor, _) -> survivor.EntityName, survivor.EntityId)
            |> List.collect (fun (ssKey, survivor, shadows) ->
                shadows
                |> List.sortBy (fun s -> s.EntityId)
                |> List.map (fun s ->
                    { DiagnosticEntry.create
                        "adapter:OSSYS" DiagnosticSeverity.Info
                        CodeKindInactiveShadow
                        (sprintf
                            "Entity %s (id %d, inactive) in module %s carries the same SS_Key %O as the active entity %s (id %d) in module %s — the trace of an entity moved between modules. The inactive shadow is dropped; the active entity is carried."
                            s.EntityName s.EntityId (moduleNameOf s.EspaceId)
                            ssKey survivor.EntityName survivor.EntityId (moduleNameOf survivor.EspaceId))
                      with Metadata =
                            Map.ofList
                                [ "ssKey",            string ssKey
                                  "shadowEntityId",   string s.EntityId
                                  "shadowModule",     moduleNameOf s.EspaceId
                                  "survivorEntityId", string survivor.EntityId
                                  "survivorModule",   moduleNameOf survivor.EspaceId ] }))
        let survivingKinds =
            bundle.Kinds
            |> List.filter (fun k -> not (Set.contains k.EntityId shadowEntityIds))
        // Re-aim references that target a dropped shadow's EntityId at the
        // survivor — same SS_Key, so the resolved reference identity is
        // unchanged; only the join id moves.
        let reaimedReferences =
            if Map.isEmpty survivorEntityIdByShadow then bundle.References
            else
                bundle.References
                |> List.map (fun r ->
                    match r.RefEntityId with
                    | Some id when Map.containsKey id survivorEntityIdByShadow ->
                        { r with RefEntityId = Some (Map.find id survivorEntityIdByShadow) }
                    | _ -> r)
        // -- step 2: entity-less module skip (post-shadow-drop) -----------
        let populatedEspaces =
            survivingKinds |> List.map (fun k -> k.EspaceId) |> Set.ofList
        let populatedModules, entityLess =
            bundle.Modules
            |> List.partition (fun m -> Set.contains m.EspaceId populatedEspaces)
        let entityLessNotices =
            entityLess
            |> List.sortBy (fun m -> m.EspaceName, m.EspaceId)
            |> List.map (fun m ->
                { DiagnosticEntry.create
                    "adapter:OSSYS" DiagnosticSeverity.Info
                    CodeModuleEntityLess
                    (sprintf
                        "Module %s (espace %d) contains no entities and is skipped from the model read — nothing to publish or transfer."
                        m.EspaceName m.EspaceId)
                  with Metadata =
                        Map.ofList
                            [ "espaceId", string m.EspaceId
                              "moduleName", m.EspaceName ] })
        { bundle with
            Modules    = populatedModules
            Kinds      = survivingKinds
            References = reaimedReferences },
        shadowNotices @ entityLessNotices

    /// V1 rowset bundle → V2 Catalog. Sibling to `parseDocument` (JSON
    /// path). The flat-list bundle joins by FK ID columns at load time
    /// (`AttributeRow.EntityId` ↔ `KindRow.EntityId`; `KindRow.EspaceId`
    /// ↔ `ModuleRow.EspaceId`; `ReferenceRow.AttrId` ↔
    /// `AttributeRow.AttrId`); the resulting structure feeds the
    /// existing `Module.create` / `Catalog.create` aggregate-root
    /// smart constructors, so referential-integrity invariants are
    /// checked at the boundary identically to the JSON path.
    /// The bundle is normalized first (`normalizeBundle` above —
    /// inactive-shadow resolution + entity-less module skip); the
    /// notices are surfaced by the live read, which calls
    /// `normalizeBundle` directly, so the parse-side application here
    /// is an idempotent re-run that guarantees EVERY consumer gets the
    /// normalized semantics.
    ///
    /// Big-O / pillar 7 perf clause: O(N + E + A + R) for the input
    /// bundle plus O(E + A) for the three Map.ofList constructions
    /// (one per ID-keyed projection). Per-module dispatch is O(E_m × A_e)
    /// with O(1) Map lookups; per-kind reference assembly is O(R_e)
    /// with O(1) Map lookups. Linear in the bundle's total size;
    /// matches `parseDocument`'s complexity class.
    let parseRowsetBundle (rawBundle: RowsetBundle) : Result<Catalog> =
        let bundle, _erasureNotices = normalizeBundle rawBundle
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
