namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE: terminal XML/refactorlog text emission; `String.Join` composes typed
//   operation segments at the absolute terminal text boundary, the use-case-
//   specific primitive for this multi-segment join.

open Projection.Core

/// Π_RefactorLog — chapter 3.5 substantive deliverable. The fourth
/// sibling Π. Consumes a `CatalogDiff` (per `EmitterOverDiff
/// <'element>` declared at `Types.fs:62-63`) and produces typed
/// refactor-log entries — the structural input to SSDT's
/// `.refactorlog` XML document that DacFx's incremental deploy
/// planner reads to convert `DROP COLUMN` + `ADD COLUMN` into
/// `sp_rename`.
///
/// First-slice scope: kind-level renames (i.e., table renames in
/// SSDT vocabulary). The `CatalogDiff` is kind-level (per chapter
/// 3.5 prescope §2 first-slice scope); each `Renamed` entry produces
/// one `RefactorLogEntry` of `ElementType = SqlTable`. Column-level
/// renames defer to a follow-on slice gated on `CatalogDiff` carrying
/// attribute-level keys (the closed-DU empirical-test discipline
/// applies — adding `SqlSimpleColumn` exercises the variant only at
/// match sites within this module).
///
/// **Per A35 / T11 amended again (diff-typed inputs)**: T11
/// (sibling-Π commutativity) extends to diff-typed inputs by typing
/// `ArtifactByKind` over the diff's *target* Catalog. Every kind in
/// the target is in the artifact's keyset; renamed kinds carry
/// non-empty entry lists; everything else carries the empty list.
/// The chapter 3.5 close cashes the amendment.
///
/// **Per A18**: the emitter consumes `CatalogDiff` (which carries
/// `Source: Catalog` and `Target: Catalog` as fields) — never
/// `Policy`. Catalog and Profile are evidence; Policy is intent.

/// SSDT-native operation kind. The `.refactorlog` XML format
/// supports several refactor kinds (`RenameRefactor`,
/// `MoveSchemaRefactor`, `WildcardExpansionRefactor`,
/// `ChangeColumnTypeRefactor`); chapter 3.5 first slice ships
/// `RenameRefactor` only. New variants land under closed-DU
/// expansion empirical-test discipline when a real consumer
/// surfaces them.
type RefactorOperationKind =
    | RenameRefactor

/// SSDT element-type discriminator. SSDT enumerates these as
/// strings (`SqlTable`, `SqlSimpleColumn`, `SqlSchema`, …); V2
/// keeps a closed DU and renders to string at emission time.
/// First slice carries the three variants that table-level rename
/// evidence touches; column-level renames extend the DU with
/// further parent shapes if they materialize.
type RefactorElementType =
    | SqlTable
    | SqlSimpleColumn
    | SqlForeignKey
    | SqlSchema

/// One record's worth of refactor evidence — exactly what becomes
/// one `<Operation>` element in the rendered `.refactorlog` XML.
/// `OperationKey` is the deterministic UUIDv5-derived stable
/// identifier; two emit runs against the same `CatalogDiff` produce
/// the same `OperationKey` (T1).
type RefactorLogEntry =
    {
        /// Deterministic UUIDv5(`namespace`, `"rename:<rootOriginal>:<oldName>:<newName>"`).
        /// Stable across emit runs; SSDT's GUI generates random
        /// GUIDs but DacFx accepts any stable Guid as long as it's
        /// unique per operation.
        OperationKey      : System.Guid
        OperationKind     : RefactorOperationKind
        /// SSDT element reference. Bracket-quoted form per
        /// `[schema].[table]` (table) or `[schema].[table].[col]`
        /// (column).
        ElementName       : string
        ElementType       : RefactorElementType
        /// Owner of the element — the schema for a table, the
        /// table for a column.
        ParentElementName : string
        ParentElementType : RefactorElementType
        /// The new (post-rename) leaf name — the new table name
        /// for a table rename; the new column name for a column
        /// rename.
        NewName           : string
        /// Pass version of the emitter that produced this entry,
        /// per `RefactorLogEmitter.version`.
        PassVersion       : int
    }

[<RequireQualifiedAccess>]
module RefactorLogEmitter =

    /// Emitter version. Bumped when the entry shape changes meaning
    /// (e.g., when column-level renames land). Stamped onto every
    /// `RefactorLogEntry` so cross-version triage can distinguish
    /// entries from older emit-versions.
    [<Literal>]
    let version : int = 1

    /// Namespace Guid for refactor-log `OperationKey` derivation.
    /// This is the **stable V2-side namespace** for refactor-log
    /// identity threading — once chosen, never change without an
    /// explicit DECISIONS amendment. Per the chapter 3.5 prescope
    /// §10 risk R3 mitigation, the namespace is the cross-version
    /// disambiguator; changing it invalidates every `OperationKey`
    /// ever emitted.
    [<Literal>]
    let private namespaceString : string =
        "5d3f9f5c-1a2b-4c8d-9e7f-3b6a8c4d2e1f"

    let namespaceGuid : System.Guid =
        System.Guid.Parse namespaceString

    /// `[schema]` — bracket-quoted schema name. SSDT's `SqlSchema`
    /// element-name convention.
    let private schemaQualified (table: TableId) : string =
        Render.quote (TableId.schemaText table)

    /// Derive a deterministic `OperationKey` for a kind-level rename.
    /// Same diff → same `(rootOriginal, OldName, NewName)` triple →
    /// same UUIDv5 → same `OperationKey` (T1).
    ///
    /// **Per-site analysis (chapter 3.5 deep audit, hard line)**:
    /// the prior implementation joined the four-component list via
    /// `String.concat ":"` to build the UUIDv5 input string. The
    /// data-structure-oriented refactor uses `UuidV5.createFromSegments`
    /// which feeds typed UTF-8 byte segments to BCL
    /// `SHA1.TransformBlock` *incrementally* — no intermediate
    /// concatenated string is allocated; the typed quadruple
    /// `[ "rename"; rootOriginal; oldName; newName ]` flows
    /// directly through the BCL incremental-hash surface. The
    /// separator byte `':'` (0x3A) is the field delimiter; per
    /// RFC 4122 §4.3 the byte-equivalent input produces a
    /// byte-identical UUIDv5 to the legacy string-then-hash form.
    let private renameOperationKey
        (kindKey: SsKey)
        (record: RenameRecord)
        : System.Guid =
        let utf8 (s: string) : byte[] =
            System.Text.Encoding.UTF8.GetBytes s
        UuidV5.createFromSegments
            namespaceGuid
            (byte ':')
            [
                utf8 "rename"
                utf8 (SsKey.rootOriginal kindKey)
                utf8 (Name.value record.OldName)
                utf8 (Name.value record.NewName)
            ]

    /// Deterministic `OperationKey` for a column-level (physical) rename.
    /// Keyed on the attribute's SsKey + the old/new physical column names
    /// so two emit runs against the same physical rename agree (T1).
    let private columnRenameOperationKey
        (attrKey: SsKey)
        (oldColumn: string)
        (newColumn: string)
        : System.Guid =
        let utf8 (s: string) : byte[] = System.Text.Encoding.UTF8.GetBytes s
        UuidV5.createFromSegments
            namespaceGuid
            (byte ':')
            [
                utf8 "rename:column"
                utf8 (SsKey.rootOriginal attrKey)
                utf8 oldColumn
                utf8 newColumn
            ]

    /// Deterministic `OperationKey` for a FOREIGN-KEY rename (gap N1).
    /// Keyed on the reference's SsKey + the old/new logical FK names so two
    /// emit runs against the same FK rename agree (T1). A distinct
    /// `"rename:foreignkey"` discriminator keeps the key space disjoint from
    /// the kind ("rename") and column ("rename:column") rename axes so a
    /// table, column, and FK that happen to share an old→new name never
    /// collide on `OperationKey`.
    let private referenceRenameOperationKey
        (referenceKey: SsKey)
        (oldName: string)
        (newName: string)
        : System.Guid =
        let utf8 (s: string) : byte[] = System.Text.Encoding.UTF8.GetBytes s
        UuidV5.createFromSegments
            namespaceGuid
            (byte ':')
            [
                utf8 "rename:foreignkey"
                utf8 (SsKey.rootOriginal referenceKey)
                utf8 oldName
                utf8 newName
            ]

    /// N1 — the per-kind FOREIGN-KEY-rename refactor entries. SSDT requires a
    /// `SqlForeignKeyConstraint` `.refactorlog` operation for every FK rename,
    /// or DacFx interprets the rename as DROP CONSTRAINT + ADD CONSTRAINT.
    ///
    /// Detection mirrors the kind/column levels: a **logical `Reference.Name`**
    /// change (`ReferenceDiffs[k].Renamed`, the rename axis `CatalogDiff`
    /// records). The FK is a child of its source table, so `ParentElementType`
    /// is `SqlTable`; `ElementName` carries the old logical FK name qualified
    /// by the table, `NewName` the new logical FK name. Disjoint from the
    /// kind/column rename channels (different SsKey space, different element
    /// type), so the three channels never double-emit the same slice.
    let private referenceRefactorEntries
        (diff: CatalogDiff)
        (k: Kind)
        : RefactorLogEntry list =
        match CatalogDiff.referenceDiffOf k.SsKey diff with
        | None -> []
        | Some rd ->
            let tableQualified : string =
                Render.tableQualified { Schema = k.Physical.Schema; Table = k.Physical.Table; Catalog = None }
            rd.Renamed
            |> Map.toList
            |> List.map (fun (referenceKey, record) ->
                let oldName = Name.value record.OldName
                let newName = Name.value record.NewName
                {
                    OperationKey = referenceRenameOperationKey referenceKey oldName newName
                    OperationKind = RenameRefactor
                    ElementName = System.String.Join(".", [| tableQualified; Render.quote oldName |])
                    ElementType = SqlForeignKey
                    ParentElementName = tableQualified
                    ParentElementType = SqlTable
                    NewName = newName
                    PassVersion = version
                })

    /// 6.A.12 — the per-kind COLUMN-rename refactor entries. SSDT requires
    /// a `SqlSimpleColumn` `.refactorlog` operation for every column rename,
    /// or DacFx interprets the rename as DROP COLUMN + ADD COLUMN and the
    /// data is lost (handbook §"The Silent Catastrophe").
    ///
    /// Detection mirrors the kind level: a **logical `Attribute.Name`**
    /// change (`AttributeDiffs[k].Renamed`, the rename axis `CatalogDiff`
    /// records). The refactorlog is computed over deployed-state-vs-emitted-
    /// state catalogs, both of which carry logical names (V2 emits the
    /// logical name as the physical object via `LogicalColumnEmission`), so
    /// the rename is `oldLogical → newLogical` — `ElementName` carries the
    /// old logical column name, `NewName` the new one. The caller (the
    /// `migrate` orchestrator) is responsible for feeding the read-back
    /// deployed catalog as source and the emitted catalog as target; this
    /// emitter just projects the diff. Disjoint from `SchemaMigrationEmitter`
    /// (shape changes → ALTER) by *operation*, not by attribute: rename
    /// (logical `Name`) and reshape (shape facets) are independent axes
    /// (`AttributeDiff.Renamed` vs `.Reshaped`), so one column may be both.
    /// The two channels still never double-emit the *same operation* — this
    /// emits the sp_rename; `SchemaMigrationEmitter` emits the ALTER COLUMN
    /// against the target attribute. (Tested: RefactorLogEmitterTests
    /// "rename → one SqlSimpleColumn AND reshape → one ALTER COLUMN, disjoint".)
    let private columnRefactorEntries
        (diff: CatalogDiff)
        (k: Kind)
        : RefactorLogEntry list =
        match CatalogDiff.attributeDiffOf k.SsKey diff with
        | None -> []
        | Some ad ->
            let tableQualified : string =
                Render.tableQualified { Schema = k.Physical.Schema; Table = k.Physical.Table; Catalog = None }
            ad.Renamed
            |> Map.toList
            |> List.map (fun (attrKey, record) ->
                let oldName = Name.value record.OldName
                let newName = Name.value record.NewName
                {
                    OperationKey = columnRenameOperationKey attrKey oldName newName
                    OperationKind = RenameRefactor
                    ElementName = System.String.Join(".", [| tableQualified; Render.quote oldName |])
                    ElementType = SqlSimpleColumn
                    ParentElementName = tableQualified
                    ParentElementType = SqlTable
                    NewName = newName
                    PassVersion = version
                })

    /// Build the per-kind refactor-entry slice for one kind. Returns
    /// the empty list when the kind is not renamed in the diff
    /// (`Unchanged`, `Added`, or `Removed` partitions); a one-entry
    /// list when the kind is in `Renamed`.
    ///
    /// Per T11 amended again, the artifact's keyset is the *target*
    /// Catalog's kind set — every kind in the target appears, with
    /// possibly empty per-kind evidence. Empty per-key payload still
    /// satisfies T11 (the keyset is what the structural type
    /// guarantees, not non-emptiness of values).
    let private kindRefactorEntries
        (renames: Map<SsKey, RenameRecord>)
        (k: Kind)
        : RefactorLogEntry list =
        match Map.tryFind k.SsKey renames with
        | None -> []
        | Some record ->
            let target : TableId =
                { Schema = k.Physical.Schema; Table = k.Physical.Table; Catalog = None }
            [
                {
                    OperationKey = renameOperationKey k.SsKey record
                    OperationKind = RenameRefactor
                    ElementName = Render.tableQualified target
                    ElementType = SqlTable
                    ParentElementName = schemaQualified target
                    ParentElementType = SqlSchema
                    NewName = Name.value record.NewName
                    PassVersion = version
                }
            ]

    /// Π port realization for the diff-typed sibling. Per
    /// `EmitterOverDiff<RefactorLogEntry list>`, the artifact's
    /// keyset is the diff's *target* Catalog's kind set —
    /// `ArtifactByKind.create` enforces strict equality between the
    /// slice's keys and `Catalog.allKinds target`'s SsKey set.
    /// T11 (sibling-Π commutativity, structural type encoding,
    /// extended to diff-typed inputs) holds by construction: any two
    /// `ArtifactByKind` values built from the same target Catalog
    /// have equal keysets.
    ///
    /// Big-O: O(N log N) where N = |target kinds|. `Catalog.allKinds`
    /// is O(N); per-kind `Map.tryFind` lookup is O(log R) where R =
    /// |renames|; `ArtifactByKind.create`'s set-difference
    /// O(N log N). Renames map is amortized O(R) for a kind-by-kind
    /// scan (each lookup O(log R), total O(N log R)).
    let emit : EmitterOverDiff<RefactorLogEntry list> = fun diff ->
        use _ = Bench.scope "emit.refactorLog.emit"
        let target = CatalogDiff.target diff
        let renames = CatalogDiff.renamed diff
        // Per kind: the table-level rename (logical Kind.Name), the
        // column-level renames (logical Attribute.Name, 6.A.12), AND the
        // foreign-key renames (logical Reference.Name, N1). All are
        // RenameRefactor operations on the same kind's slice; a kind with
        // none carries the empty list (T11 keyset = target's kinds).
        ArtifactByKind.perKind target (fun k ->
            kindRefactorEntries renames k
            @ columnRefactorEntries diff k
            @ referenceRefactorEntries diff k)


    // ------------------------------------------------------------------
    // G3 (DECISIONS 2026-07-16) — the DEPLOYED-vocabulary emission.
    //
    // `emit` above speaks the CATALOG plane: `ElementName` renders the
    // kind's `Physical` (OSSYS-flavored) table and the raw logical
    // names. That vocabulary matches a deployed estate only when the
    // logical-name emission is recompiled OFF — under the production
    // default (`LogicalTableEmission`/`LogicalColumnEmission`, default-
    // on, no config off-switch — register H1) the deployed estate
    // speaks LOGICAL names, and DacFx matches refactor operations
    // against the DEPLOYED model's element names. An operation naming
    // `[dbo].[OSUSR_ABC_CUSTOMER]` on an estate whose table is
    // `[dbo].[Customer]` matches nothing, is skipped, and the rename
    // DROP+CREATEs anyway — the exact catastrophe the refactorlog
    // exists to prevent. `emitDeployed` projects both sides of every
    // rename through the SAME name-decision rules the emission passes
    // apply, so the operations speak the deployed vocabulary:
    //
    //   * table — the post-chain outcome of `LogicalTableEmission.
    //     substituteKind` + the operator rename pass (the LAST
    //     `Kind.Physical` writer): a kind an operator `tableRenames`
    //     override targets (EITHER form) deploys under its authored
    //     `Physical` (physical-form additionally rides the S6.3 pins);
    //     blank / >128-char logical names keep `Physical` (H1's
    //     tripwire edge); otherwise the logical name IS the deployed
    //     table name.
    //   * column — `LogicalColumnEmission.substituteAttribute`: blank /
    //     >128 falls back to the physical column; otherwise logical.
    //   * a rename whose old and new DEPLOYED names coincide (a pinned
    //     kind's logical rename; a double fallback) emits NO operation —
    //     nothing changed on the estate, and a no-match operation is
    //     noise at best.
    //
    // The FOREIGN-KEY channel is deliberately ABSENT here: deployed FK
    // constraint names are SYNTHESIZED (`FK_<Owner>_<Target>_<Col>`,
    // `SsdtDdlEmitter.fkDef` — honoring `Reference.Name` is WP-7's open
    // remainder), so a logical `Reference.Name` rename changes no
    // deployed constraint and its operation could never match. When
    // WP-7 lands, the FK channel re-opens with the then-real deployed
    // names. Kind/column operations keep `emit`'s OperationKey
    // derivations verbatim (keys are over the LOGICAL rename triple),
    // so accumulation dedup and cross-run identity are unchanged.
    //
    // Recompile coupling, named: if `LogicalTableEmission`/
    // `LogicalColumnEmission` are ever recompiled `Disabled`, this
    // projection must follow (deployed names become the physical ones).
    // ------------------------------------------------------------------

    /// The deployed table name a kind carries under the production
    /// emission rule, for an arbitrary logical name (so the OLD and NEW
    /// sides of a rename each project through the kind that carried
    /// them — source kind for old, target kind for new). Validity by
    /// construction: `TableName.create` embeds the exact blank/>128 rule
    /// `LogicalTableEmission.substituteKind` pre-checks — an invalid
    /// logical name keeps the physical (H1's tripwire edge).
    let private deployedTableName (physicalNamedKinds: Set<SsKey>) (k: Kind) (logical: Name) : string =
        if Set.contains k.SsKey physicalNamedKinds then TableId.tableText k.Physical
        else
            match TableName.create (Name.value logical) with
            | Ok _    -> Name.value logical
            | Error _ -> TableId.tableText k.Physical

    /// The deployed column name an attribute carries under the
    /// production emission rule (`LogicalColumnEmission` — no pins at
    /// the column grain; an invalid logical name keeps the physical,
    /// by the same `ColumnName.create` validity rule the pass checks).
    let private deployedColumnName (a: Attribute) (logical: Name) : string =
        match ColumnName.create (Name.value logical) with
        | Ok _    -> Name.value logical
        | Error _ -> ColumnRealization.columnNameText a.Column

    /// `[schema]` for a kind — the SqlSchema parent element name.
    let private deployedSchemaQualified (k: Kind) : string =
        Render.quote (TableId.schemaText k.Physical)

    /// The kind-level (SqlTable) deployed-vocabulary rename entries.
    let private deployedKindRefactorEntries
        (physicalNamedKinds: Set<SsKey>)
        (diff: CatalogDiff)
        (renames: Map<SsKey, RenameRecord>)
        (k: Kind)
        : RefactorLogEntry list =
        match Map.tryFind k.SsKey renames with
        | None -> []
        | Some record ->
            // The OLD side projects through the SOURCE kind (the plane the
            // prior episode deployed); the NEW side through the target kind.
            let sourceKind =
                Catalog.tryFindKind k.SsKey (CatalogDiff.source diff)
                |> Option.defaultValue k
            let oldDeployed = deployedTableName physicalNamedKinds sourceKind record.OldName
            let newDeployed = deployedTableName physicalNamedKinds k record.NewName
            if oldDeployed = newDeployed then []
            else
                [
                    {
                        OperationKey = renameOperationKey k.SsKey record
                        OperationKind = RenameRefactor
                        ElementName =
                            System.String.Join(".", [| deployedSchemaQualified k; Render.quote oldDeployed |])
                        ElementType = SqlTable
                        ParentElementName = deployedSchemaQualified k
                        ParentElementType = SqlSchema
                        NewName = newDeployed
                        PassVersion = version
                    }
                ]

    /// The column-level (SqlSimpleColumn) deployed-vocabulary rename
    /// entries. Operations describe the PRE-publish estate, so the table
    /// qualifier is the kind's OLD deployed name (relevant when the kind
    /// renames in the same displacement — operations are sorted by
    /// OperationKey, not authored order, so they must all speak one
    /// consistent pre-publish vocabulary).
    let private deployedColumnRefactorEntries
        (physicalNamedKinds: Set<SsKey>)
        (diff: CatalogDiff)
        (renames: Map<SsKey, RenameRecord>)
        (k: Kind)
        : RefactorLogEntry list =
        match CatalogDiff.attributeDiffOf k.SsKey diff with
        | None -> []
        | Some ad when Map.isEmpty ad.Renamed -> []
        | Some ad ->
            let sourceKind =
                Catalog.tryFindKind k.SsKey (CatalogDiff.source diff)
                |> Option.defaultValue k
            let kindOldLogical =
                match Map.tryFind k.SsKey renames with
                | Some r -> r.OldName
                | None -> k.Name
            let tableDeployedOld = deployedTableName physicalNamedKinds sourceKind kindOldLogical
            let tableQualifiedOld =
                System.String.Join(".", [| deployedSchemaQualified k; Render.quote tableDeployedOld |])
            ad.Renamed
            |> Map.toList
            |> List.choose (fun (attrKey, record) ->
                let sourceAttr = sourceKind.Attributes |> List.tryFind (fun a -> a.SsKey = attrKey)
                let targetAttr = k.Attributes |> List.tryFind (fun a -> a.SsKey = attrKey)
                match sourceAttr |> Option.orElse targetAttr, targetAttr |> Option.orElse sourceAttr with
                | Some sa, Some ta ->
                    let oldDeployed = deployedColumnName sa record.OldName
                    let newDeployed = deployedColumnName ta record.NewName
                    if oldDeployed = newDeployed then None
                    else
                        Some
                            {
                                OperationKey =
                                    columnRenameOperationKey attrKey (Name.value record.OldName) (Name.value record.NewName)
                                OperationKind = RenameRefactor
                                ElementName =
                                    System.String.Join(".", [| tableQualifiedOld; Render.quote oldDeployed |])
                                ElementType = SqlSimpleColumn
                                ParentElementName = tableQualifiedOld
                                ParentElementType = SqlTable
                                NewName = newDeployed
                                PassVersion = version
                            }
                | _ -> None)

    /// Π port realization for the DEPLOYED-vocabulary document (G3) —
    /// the sibling of `emit` whose operations DacFx can match against a
    /// logically-emitted estate. Same T11 keyset contract (every kind in
    /// the diff's target Catalog appears; non-renamed kinds carry the
    /// empty list); same OperationKey derivations (dedup identity is
    /// unchanged); table + column channels only (FK channel named-absent
    /// above). `physicalNamedKinds` are the kinds whose deployed table
    /// name is their operator-authored `Physical.Table` — every operator
    /// `tableRenames` target, either form (`Compose.operatorRenamedKinds`):
    /// the chain's last `Kind.Physical` writer is the operator rename, so
    /// for these kinds the logical name never deploys.
    let emitDeployed (physicalNamedKinds: Set<SsKey>) : EmitterOverDiff<RefactorLogEntry list> = fun diff ->
        use _ = Bench.scope "emit.refactorLog.emitDeployed"
        let target = CatalogDiff.target diff
        let renames = CatalogDiff.renamed diff
        ArtifactByKind.perKind target (fun k ->
            deployedKindRefactorEntries physicalNamedKinds diff renames k
            @ deployedColumnRefactorEntries physicalNamedKinds diff renames k)

    /// Flatten an `ArtifactByKind<RefactorLogEntry list>` into a single
    /// `OperationKey`-sorted entry list. The per-kind keyset is discarded;
    /// what carries forward across episodes is the flat operation log (the
    /// `.refactorlog` document is a flat `<Operations>` list, not a
    /// per-kind structure). Sorted by `OperationKey` for T1 determinism.
    let flatten (artifact: ArtifactByKind<RefactorLogEntry list>) : RefactorLogEntry list =
        artifact
        |> ArtifactByKind.toMap
        |> Map.toSeq
        |> Seq.collect snd
        |> Seq.sortBy (fun e -> e.OperationKey)
        |> Seq.toList

    /// **Accumulate-against-prior** (gap N6 / AC-P6). The `.refactorlog`
    /// is a *cumulative* document — every rename a timeline has ever
    /// performed stays in the log so DacFx applies `sp_rename` (not
    /// DROP+ADD) for any source older than the latest. So an episode's
    /// emitted log is `prior ⊕ current`, deduped by `OperationKey`: a
    /// rename already present in the prior committed log is **not**
    /// re-emitted (its `OperationKey` already names that operation); a
    /// genuinely new rename is appended. Works uniformly across all three
    /// channels — table (`SqlTable`), column (`SqlSimpleColumn`), and FK
    /// (`SqlForeignKey`) renames — because the identity is the
    /// `OperationKey`, not the element type.
    ///
    /// `OperationKey` is the identity (deterministic UUIDv5 over the
    /// rename triple), so dedup-by-key is dedup-by-operation. The prior
    /// entry **wins** on a key collision — the prior committed log is the
    /// source of truth for an operation already recorded (its shape is
    /// what DacFx already saw); re-emitting would only churn the audit
    /// metadata. Result is sorted by `OperationKey` for T1 determinism
    /// (order-independent of how the two inputs were ordered).
    let accumulate
        (prior: RefactorLogEntry list)
        (current: RefactorLogEntry list)
        : RefactorLogEntry list =
        let priorKeys = prior |> List.map (fun e -> e.OperationKey) |> Set.ofList
        let novel =
            current
            |> List.filter (fun e -> not (Set.contains e.OperationKey priorKeys))
        prior @ novel
        |> List.sortBy (fun e -> e.OperationKey)

    /// `accumulate` taking the current emission as the typed artifact
    /// (the common caller shape — `emit diff` produces an `ArtifactByKind`).
    /// Flattens then accumulates against the prior committed entries.
    let accumulateArtifact
        (prior: RefactorLogEntry list)
        (current: ArtifactByKind<RefactorLogEntry list>)
        : RefactorLogEntry list =
        accumulate prior (flatten current)
