namespace Projection.Targets.SSDT

open Projection.Core
open Projection.Core.Passes

/// Π_SSDT-DDL — chapter 4.1.A substantive deliverable. The production-
/// deployment SSDT DDL emitter (sibling Π) that complements the DACPAC
/// fast-iteration emitter (chapter 3.x) and feeds the Azure DevOps
/// integration-test promoted lane.
///
/// **V2-driver KPI Phase 2 (per `V2_DRIVER.md`).** This is the first
/// production-write surface V2 owns under V2-driver mode; the artifact
/// is the bytes that promote into the operator's existing Azure DevOps
/// pipeline (per chapter pre-scope §1: `<outDir>/Modules/<Module>/
/// <Schema>.<Table>.sql`). The canary verifies V2's SSDT directory
/// against V1's modulo named tolerances; per-environment-per-artifact-
/// type R6 governance flips authority from V1 to V2 once N=10
/// consecutive green canary runs + operator sign-off support the flip.
///
/// **Per-kind output is a typed `SsdtFile`** carrying the cross-platform-
/// deterministic relative path AND the rendered SQL body. The body
/// flows through ScriptDom's typed AST (`Statement.CreateTable` →
/// `ScriptDomBuild.buildStatement` → `ScriptDomGenerate.generateOne`)
/// — pillar 7 (gold-standard library precedence) holds end-to-end; no
/// `String.Concat` at the SQL emission site (the chapter-3.7 slice β'
/// Render.columnSqlType-through-ScriptDom precedent extends to whole
/// statements).
///
/// **Slice scope (chapter pre-scope §8):** This file ships slice 1 —
/// single-table catalog, schema + table emission. Columns + PK; no
/// indexes, no FKs, no extended properties, no static-population
/// inserts. Subsequent slices (2 multi-attr, 3 indexes, 4 composite-
/// PK, 5 intra-module FKs, 6 cross-module FKs, 7 identity+default,
/// 8 extended properties, 9 manifest, 10 refactor-log composition)
/// extend the same Emitter signature.
///
/// **F# core never touches the file system.** The emitter produces
/// `ArtifactByKind<SsdtFile>` (in-memory typed map). A downstream
/// composition layer (`Render.toSsdtDirectory` per chapter pre-scope
/// §2; chapter-4.1.A slice 10) consumes the map and produces the
/// `Map<RelativePath, string>` directory bundle; a downstream Pipeline
/// host writes the files. Per A35 (Π's canonical output is a typed
/// deterministic stream / map; realization layers are sibling
/// consumers).
[<RequireQualifiedAccess>]
module SsdtDdlEmitter =

    /// Pass version. Bump when the SSDT DDL emission shape changes
    /// in a way that matters for cross-version comparators.
    [<Literal>]
    let version : int = 1

    /// Per-kind production-deployment SSDT DDL artifact. Concept-
    /// shaped name (the file IS the artifact); per pillar 8, no
    /// generic suffix. Two fields: the cross-platform-deterministic
    /// relative path (forward-slash separators per chapter pre-scope
    /// §4 + §6 — Path.Combine considered + rejected because platform-
    /// specific separators violate T1 byte-determinism for V2's
    /// production-deployment artifact contract); and the rendered
    /// SQL body emitted by ScriptDom's `Sql160ScriptGenerator`.
    type SsdtFile =
        {
            /// Relative path from the SSDT output directory root.
            /// V1 convention: `Modules/<ModuleName>/<Schema>.<Table>.sql`.
            /// Forward-slash separators regardless of host OS — V1's
            /// `SsdtEmitter` at `src/Osm.Emission/SsdtEmitter.cs:55-122`
            /// hard-codes forward slashes; V2 mirrors for cross-platform
            /// determinism.
            RelativePath : string
            /// Rendered SQL body (CREATE TABLE statement; subsequent
            /// slices extend with indexes + extended properties + inline
            /// FKs). Emitted by `Sql160ScriptGenerator` via
            /// `ScriptDomGenerate.generateOne`; pinned-options writer
            /// guarantees byte-determinism (T1).
            Body : string
        }

    // -------------------------------------------------------------------
    // Slice-1 helpers — local to this module per the two-consumer
    // threshold discipline. The Kind→Statement helpers (`toTableId`,
    // `columnDef`, `pkDef`) duplicate the same shape from
    // `RawTextEmitter`'s private helpers; per chapter pre-scope §8
    // slice 2, a shared `SqlTypeMap.fs` extraction lifts the type-
    // mapping; per slice 3, an index-name helper extracts; per slice
    // 5, an FK-name helper extracts. Until those extractions earn
    // their second consumer, the code stays inline.
    // -------------------------------------------------------------------

    // Slice 4.3 — preserve the catalog coordinate (L3-S10 / L3-I10).
    // Pre-slice this hard-coded `Catalog = None`, which silently
    // downgraded a cross-database FK target (`k.Physical.Catalog =
    // Some db`) to a two-part name. Carrying `k.Physical.Catalog`
    // through lets `schemaObjectFromTableId` emit the three-part
    // `[db].[schema].[table]` (honored reference) instead of the silent
    // drop. Additive: `Physical.Catalog = None` (today's universal
    // case — most V2 sources project `db_catalog: null`) is unchanged.
    // The truly-external case (target kind absent from the catalog)
    // still drops via `fkDef` → `foreignKeyDropDiagnostics` Warning, so
    // neither cross-DB path is a *silent* drop (L3-Boundary-NoSilentDrop).
    let private toTableId (k: Kind) : TableId =
        { Schema = k.Physical.Schema; Table = k.Physical.Table; Catalog = k.Physical.Catalog }

    /// Derive a `ColumnDef` (the Statement-DU column shape from
    /// chapter 3.5) from a V2 `Attribute`. The `Provenance` field is
    /// empty for SSDT DDL emission — per chapter pre-scope §10
    /// `Tolerance.IgnoreHeaderComments = true` initially, V2 omits
    /// V1's `/* Source: ... */` per-table header block.
    // Wave-2 slice 2.3 — apply the NOT NULL tightening decision at emission.
    // **Additive-only** (`field && not enforce`, never `field = decision`):
    // a column is emitted NOT NULL iff the source already made it NOT NULL
    // OR a registered Nullability intervention decided `EnforceNotNull` for
    // it. A non-enforce decision never loosens source truth. A18-amended
    // holds — the overlay carries the decision (evidence), not Policy.
    let private columnDef (overlay: DecisionOverlay) (a: Attribute) : ColumnDef =
        let enforceNotNull = Set.contains a.SsKey overlay.EnforceNotNull
        {
            Name         = ColumnRealization.columnNameText a.Column
            Type         = a.Type
            // Concrete SQL storage evidence (BIGINT / DATETIME /
            // NVARCHAR(MAX)) when the OSSYS adapter resolved it from
            // `ossys_EntityAttr.Type`; `None` falls back to the
            // `PrimitiveType` mapping at the realization layer.
            SqlStorage   = a.SqlStorage
            Length       = a.Length
            Precision    = a.Precision
            Scale        = a.Scale
            // Additive-only tightening: source NULL ∧ ¬enforce stays NULL;
            // source NOT NULL stays NOT NULL regardless; enforce ⇒ NOT NULL.
            Nullable     = a.Column.IsNullable && not enforceNotNull
            IsIdentity   = a.IsIdentity
            IsPrimaryKey = a.IsPrimaryKey
            // Slice 5.13.column-features-emit: DEFAULT clause carriage
            // from Attribute.DefaultValue (chapter A.0' slice ε IR
            // lift). V2 IR carries `SqlLiteral option`; the realization
            // layer's `columnDefinition` emits an inline
            // `CONSTRAINT <name> DEFAULT <literal>` when populated.
            // The default name surfaces from a future rowset wiring
            // (#ColumnReality.DefaultConstraintName); when absent,
            // SQL Server auto-names the constraint. Today the JSON
            // path populates DefaultValue from V1's "default" JSON
            // field; rowset path leaves it None pending the
            // #Attr.DefaultValue lift (separate slice).
            DefaultValue = a.DefaultValue
            // Slice 5.3.α.column-axis-deferral-closeout (matrix row 53
            // partial cash-out): thread V2 IR's `Attribute.DefaultName`
            // through. V1 source: `AttributeOnDiskDefaultConstraint.Name`
            // (the deployed-target's named DEFAULT constraint identifier).
            // The realization layer emits `CONSTRAINT [name] DEFAULT (value)`
            // when DefaultName is Some; SQL Server auto-names otherwise.
            DefaultName  = a.DefaultName |> Option.map Name.value
            // Slice 5.3.α.column-axis-deferral-closeout (LR4 cash-out):
            // thread V2 IR's `Attribute.Computed`. Computed columns
            // suppress Type / Length / Precision / Scale / Identity /
            // Nullability / DEFAULT material at the realization layer
            // (V1 CreateTableStatementBuilder.cs L362-365 + L296-302
            // shape). The realization layer emits `[col] AS (expression)
            // [PERSISTED]` when Computed is Some.
            Computed     = a.Computed
            Provenance   = ""
        }

    /// 6.A.12 — the canonical `ColumnDef` for an attribute under no
    /// tightening overlay. The `SchemaMigrationEmitter` reuses this so an
    /// ALTER/ADD COLUMN's column shape is byte-identical to the same
    /// column's CREATE TABLE declaration (a structural A→B diff carries no
    /// tightening overlay — the target attribute's shape is authoritative).
    let columnDefOfAttribute (a: Attribute) : ColumnDef =
        columnDef DecisionOverlay.empty a

    /// Project a `Kind.ColumnChecks` entry to the SSDT realization
    /// layer's `ColumnCheckDef`. Slice 5.13.column-features-emit
    /// (chapter A.0' slice ε emit closure). The `Name` field maps
    /// V2's `Name option` directly (V1's CHECK constraint name when
    /// present; SQL Server auto-name when None).
    let private columnCheckDef (chk: ColumnCheck) : ColumnCheckDef =
        {
            Name         = chk.Name |> Option.map Name.value
            Definition   = chk.Definition
            IsNotTrusted = chk.IsNotTrusted
        }

    /// Build the primary-key definition from a Kind's attributes.
    /// V1 convention (per chapter pre-scope §3): `PK_<PhysicalSchema>_
    /// <PhysicalTable>`; single-column PKs get inlined as column-
    /// constraints (handled by `ScriptDomBuild.buildCreateTable`);
    /// composite PKs get emitted as table-constraints (slice 4).
    let private pkDef (k: Kind) : PrimaryKeyDef option =
        let pkColumns =
            k.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map (fun a -> ColumnRealization.columnNameText a.Column)
        if List.isEmpty pkColumns then None
        else
            Some
                {
                    // V1 PK naming: `PK_<Schema>_<Table>`. Per pillar 7
                    // four-question analysis: no use-case-specific BCL
                    // primitive for V1-naming-convention PK names;
                    // String.Concat with explicit underscore separator
                    // is the typed alternative (segments are typed:
                    // k.Physical.Schema and k.Physical.Table are the
                    // canonical SchemaName/TableName strings from the
                    // Coordinates value object).
                    // Slice 3b — generated names ride the identifier
                    // budget (≤128 byte-identical; over ⇒ 115 + hash12).
                    Name = IdentifierBudget.fit (System.String.Concat("PK_", TableId.schemaText k.Physical, "_", TableId.tableText k.Physical))  // LINT-ALLOW: V1 naming-convention PK constraint name; ScriptDom has no helper for V1-specific naming; segments are pre-unwrapped via TableId.schemaText/tableText (otherwise Concat would call ToString on the typed VOs and emit "SchemaName \"dbo\"")
                    Columns = pkColumns
                }

    let private toReferenceActionSql (a: ReferenceAction) : ReferenceActionSql =
        match a with
        | NoAction -> NoActionSql
        | Cascade  -> CascadeSql
        | SetNull  -> SetNullSql
        | Restrict -> NoActionSql

    /// Resolve a `Reference` to a `ForeignKeyDef` for inline FK
    /// emission. Same shape as `RawTextEmitter.fkDef`; per the
    /// chapter-3.6 N=3-of-distinct-shapes refinement, two identical-
    /// shape consumers don't yet pressure extraction (the third
    /// distinct-shape consumer — chapter 3.x DacpacEmitter or chapter
    /// 4.4 RemediationEmitter — would justify a shared
    /// `CatalogResolution` module). Returns `None` when the FK target
    /// kind isn't in the catalog (cross-catalog FKs; chapter 3.2
    /// territory) — the inline FK is dropped. The drop is no longer
    /// silent: `foreignKeyDropDiagnostics` (Wave-2 slice 2.5(b), retiring
    /// the slice-μ deferral) produces a Warning witness per unresolved-
    /// target drop.
    ///
    /// **Naming convention (chapter 4.1.A pre-scope §3 + V1
    /// `ForeignKeyNameFactory.cs:17-60`):**
    /// `FK_<OwnerTable>_<TargetTable>_<SourceColumn>`. Length-cap at
    /// 128 with `_<sha256-12-hex>` suffix when over is V1's
    /// `ConstraintNameNormalizer` discipline — SHIPPED at slice 3b via
    /// `IdentifierBudget.fit` below (matrix row 57 length-cap trigger
    /// cashed out). (Honoring a present `Reference.Name` over this
    /// synthesized form is the open WP7 remainder, not the cap.)
    let private fkDef
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        (r: Reference)
        : ForeignKeyDef option =
        let sourceColumnOpt =
            k.Attributes
            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
            |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
        match sourceColumnOpt,
              Map.tryFind r.TargetKind targetByKey,
              Map.tryFind r.TargetKind pkAttrByKey with
        | Some sourceColumn, Some target, Some pkAttr ->
            // V1 FK naming: `FK_<OwnerTable>_<TargetTable>_<SourceColumn>`.
            // Per pillar 7 four-question analysis: the use-case-specific
            // library is V1's `ForeignKeyNameFactory.CreateEvidenceName`,
            // which is C# trunk code V2 cannot reference (cherry-pick
            // discipline per `DECISIONS 2026-05-06`). The naming convention
            // is documented; mirroring it here is the V1↔V2 ubiquitous-
            // language commitment per pillar 8. String.Concat with
            // explicit underscore separator; segments are typed
            // (k.Physical.Table from Coordinates.TableId; target.Physical
            // .Table likewise; sourceColumn from k.Attributes).
            let fkName =
                // Slice 3b — generated names ride the identifier budget
                // (≤128 byte-identical; over ⇒ 115-char head + hash12;
                // the matrix row 57 length-cap trigger, cashed out).
                IdentifierBudget.fit (
                    System.String.Concat(  // LINT-ALLOW: V1 FK naming-convention mirror; V1 ForeignKeyNameFactory considered + cannot be referenced (cherry-pick discipline); segments pre-unwrapped via TableId.tableText (otherwise Concat would call ToString on TableName VO and emit "TableName \"X\"")
                        "FK_",
                        TableId.tableText k.Physical,
                        "_",
                        TableId.tableText target.Physical,
                        "_",
                        sourceColumn))
            Some
                {
                    Name         = fkName
                    SourceColumn = sourceColumn
                    Target       = toTableId target
                    TargetColumn = ColumnRealization.columnNameText pkAttr.Column
                    OnDelete     = toReferenceActionSql r.OnDelete
                    // Slice 5.13.fk-features-emit (matrix rows 58 + 59).
                    // OnUpdate threads through to ScriptDom's
                    // ForeignKeyConstraintDefinition.UpdateAction;
                    // IsConstraintTrusted feeds the post-CREATE-TABLE
                    // ALTER TABLE NOCHECK statement emitted by
                    // `untrustedFkAlters` (sibling helper below).
                    OnUpdate            = r.OnUpdate |> Option.map toReferenceActionSql
                    IsConstraintTrusted = r.IsConstraintTrusted
                }
        | _ -> None

    /// Build the CREATE TABLE statement for a single Kind. Columns +
    /// PK + intra-module + cross-module FKs (slice 5; per chapter
    /// pre-scope §3 V2 follows V1's pattern of inline FKs for both
    /// same-module and cross-module references). The FK list is
    /// resolved against pre-built lookup tables (see emitSlices); FKs
    /// whose target kind isn't in the catalog drop silently
    /// (cross-catalog territory; chapter 3.2).
    let private createTableStatement
        (overlay: DecisionOverlay)
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        : Statement =
        let columns = k.Attributes |> List.map (columnDef overlay)
        let pk = pkDef k
        // Wave-2 slice 2.4 — suppress the inline FK when the reference was
        // decided `DoNotEnforce` (overlay.DropFk). Additive-only on the drop
        // axis: a reference NOT in DropFk resolves exactly as before
        // (byte-identical with the empty overlay).
        // DECISIONS 2026-06-12 (reconciliation slice 1) — deployable
        // references only: a symmetric-closure inverse resolved through
        // `fkDef` scripts a second FK on the TARGET'S PK column (duplicate
        // FK_* names on the CreatedBy/UpdatedBy shape; PK-to-PK type
        // mismatches). Inverses are navigation edges; they never reach a
        // constraint-emission surface.
        let fks =
            k.References
            |> List.filter Reference.isDeployable
            |> List.filter (fun r -> not (Set.contains r.SsKey overlay.DropFk))
            |> List.choose (fkDef targetByKey pkAttrByKey k)
        // Slice 5.13.column-features-emit: thread Kind.ColumnChecks
        // (chapter A.0' slice ε IR; now populated via cluster A1's
        // rowset path lifting #ColumnCheckReality) through the
        // realization layer's CHECK-constraint emission.
        let checks = k.ColumnChecks |> List.map columnCheckDef
        // H-022: extract TemporalConfig from Kind.Modality if the kind is
        // a system-versioned temporal table. `tryPick` returns None for
        // non-temporal kinds; None is the `TemporalConfig option` default.
        let temporal =
            k.Modality |> List.tryPick (function
                | ModalityMark.Temporal tc -> Some tc
                | _ -> None)
        Statement.CreateTable (toTableId k, columns, pk, fks, checks, temporal)

    /// Yield the NOCHECK-FK alter pair per `IsConstraintTrusted = false` FK
    /// on this kind. Emitted AFTER the kind's CREATE TABLE so the named
    /// constraint exists when the ALTERs reference it. Slice
    /// 5.13.fk-features-emit (matrix row 59); 6.A.6 — the two-step.
    ///
    /// **The two-step (6.A.6).** An inline CREATE TABLE FK is always created
    /// TRUSTED, and `WITH NOCHECK CHECK CONSTRAINT` alone is a no-op for
    /// `is_not_trusted` on a freshly-created constraint (verified against SQL
    /// Server). To reproduce the deployed `WITH NOCHECK` state
    /// (`is_not_trusted = 1`, still enabled) the emitter DISABLES the
    /// constraint (`NOCHECK CONSTRAINT` → untrusted + disabled) then RE-ENABLES
    /// it skipping validation (`WITH NOCHECK CHECK CONSTRAINT` → enabled, still
    /// untrusted). Order matters: disable precedes re-enable.
    ///
    /// Same FK resolution path as `createTableStatement` (via `fkDef`); a
    /// reference that doesn't resolve to a `ForeignKeyDef` (cross-catalog
    /// target; missing PK on target) silently drops here too — the
    /// corresponding CREATE TABLE inline FK is absent by the same predicate,
    /// so the ALTERs would reference a non-existent constraint anyway.
    // Wave-2 slice 2.4 — emit the alter pair for a reference decided
    // `EnforceConstraint (ScriptWithNoCheck _)` (overlay.NoCheckFk), in
    // addition to the source's own `IsConstraintTrusted = false` state.
    // References decided `DoNotEnforce` (overlay.DropFk) are excluded — their
    // inline FK was suppressed in `createTableStatement`, so there is no
    // constraint to NOCHECK. The two FK overlay axes are mutually exclusive
    // by construction (DoNotEnforce vs EnforceConstraint), so the DropFk
    // exclusion only guards the defensive case.
    let private untrustedFkAlters
        (overlay: DecisionOverlay)
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        : Statement list =
        k.References
        // Deployable references only (DECISIONS 2026-06-12) — an inverse's
        // inline FK is never created, so an ALTER pair against its phantom
        // constraint name must never emit either.
        |> List.filter Reference.isDeployable
        |> List.filter (fun r -> not (Set.contains r.SsKey overlay.DropFk))
        |> List.collect (fun r ->
            match fkDef targetByKey pkAttrByKey k r with
            | Some fk when not fk.IsConstraintTrusted || Set.contains r.SsKey overlay.NoCheckFk ->
                [ Statement.AlterTableDisableConstraint (toTableId k, fk.Name)
                  Statement.AlterTableNoCheckConstraint (toTableId k, fk.Name) ]
            | _ -> [])

    /// Resolve a column-SsKey to its physical column name within a
    /// kind. The IR's `Index.Columns` carries SsKey list (per
    /// `Catalog.fs:228`); the SSDT emission needs physical column
    /// names. Per A1 (identity-survives-rename), each column SsKey
    /// resolves to exactly one Attribute; the resolved column name
    /// is the Attribute's physical `ColumnName`.
    let private resolveColumnName (k: Kind) (columnSsKey: SsKey) : string =
        match k.Attributes |> List.tryFind (fun a -> a.SsKey = columnSsKey) with
        | Some a -> ColumnRealization.columnNameText a.Column
        | None ->
            // Unreachable when Catalog.create has run (chapter 3.1
            // aggregate-root smart constructor enforces the
            // referential-integrity invariant: every Index.Column
            // resolves within its owning Kind). Defensive invalidOp
            // so the unreachability is structural.
            invalidOp (sprintf "SsdtDdlEmitter.resolveColumnName: column SsKey %A not found in kind %A (unreachable; Catalog.create invariant)" columnSsKey k.SsKey)

    /// Build the CREATE INDEX statements for a Kind's non-PK indexes.
    /// Per chapter pre-scope §8 slice 3: PK-marked indexes are
    /// skipped (PK is inlined in CREATE TABLE per V1 convention);
    /// remaining indexes are sorted by SsKey for deterministic
    /// emission ordering (A33).
    // Wave-2 slice 2.3 — apply the UNIQUE tightening decision at emission.
    // Additive-only (`field || enforce`): an index is emitted UNIQUE iff the
    // source already declared it unique OR a registered UniqueIndex
    // intervention decided `EnforceUnique`. A non-enforce decision never
    // un-uniques a source-unique index.
    let private indexStatements (overlay: DecisionOverlay) (k: Kind) : Statement list =
        k.Indexes
        |> List.filter (fun idx -> not (IndexUniqueness.isPrimaryKey idx.Uniqueness))
        |> List.sortBy (fun idx -> idx.SsKey)
        |> List.map (fun idx ->
            // Chapter 4.9 slice γ — Index.Columns now carries
            // IndexColumn (SsKey + direction). Resolve to
            // realization-layer IndexDefColumn (name + direction).
            let keyColumns =
                idx.Columns
                |> List.map (fun c ->
                    let direction =
                        match c.Direction with
                        | IndexColumnDirection.Descending -> IndexDefColumnDirection.Descending
                        | IndexColumnDirection.Ascending  -> IndexDefColumnDirection.Ascending
                    { Name = resolveColumnName k c.Attribute; Direction = direction })
            let includedColumnNames =
                idx.IncludedColumns |> List.map (resolveColumnName k)
            // Slice 5.13.index-features-emit (matrix row 56) — map
            // V2's `DataCompressionLevel` IR DU to the realization-
            // layer mirror. Closed-DU dispatch keeps the seam typed.
            let dataCompressionSql =
                idx.DataCompression
                |> Option.map (function
                    | DataCompressionLevel.None -> NoneCompressionSql
                    | DataCompressionLevel.Row  -> RowCompressionSql
                    | DataCompressionLevel.Page -> PageCompressionSql)
            // Slice A.4.7'-prelude.row56-dataspace (LR7 closure): map
            // V2's `DataSpace` IR DU to the realization-layer mirror.
            // Closed-DU dispatch keeps the seam typed.
            let dataSpaceSql =
                idx.DataSpace
                |> Option.map (function
                    | DataSpace.Filegroup name ->
                        FilegroupDataSpaceSql name
                    | DataSpace.PartitionScheme (name, cols) ->
                        PartitionSchemeDataSpaceSql (name, cols))
            let indexDef : IndexDef =
                {
                    Name     = Name.value idx.Name
                    Table    = toTableId k
                    Columns  = keyColumns
                    IsUnique = IndexUniqueness.isUnique idx.Uniqueness || Set.contains idx.SsKey overlay.EnforceUnique
                    Filter   = idx.Filter
                    IncludedColumns = includedColumnNames
                    // Chapter 4.8 slice β — on-disk index options.
                    FillFactor            = idx.FillFactor
                    IsPadded              = idx.IsPadded
                    AllowRowLocks         = idx.AllowRowLocks
                    AllowPageLocks        = idx.AllowPageLocks
                    NoRecomputeStatistics = idx.NoRecomputeStatistics
                    // Slice 5.13.index-features-emit (matrix rows 55 + 56).
                    IgnoreDuplicateKey    = idx.IgnoreDuplicateKey
                    IsDisabled            = idx.IsDisabled
                    DataCompression       = dataCompressionSql
                    // Slice A.4.7'-prelude.row56-dataspace (LR7).
                    DataSpace             = dataSpaceSql
                }
            Statement.CreateIndex indexDef)

    /// Yield one `AlterIndexDisable` statement per non-PK index where
    /// `IsDisabled = true`. Emitted AFTER the kind's CREATE INDEX
    /// statements so the named index exists when the ALTER references
    /// it. PK-marked indexes filter out at `indexStatements` (PK is
    /// always enforced; V1 invariant). Slice 5.13.index-features-emit
    /// (matrix row 55).
    let private disabledIndexAlters (k: Kind) : Statement list =
        k.Indexes
        |> List.filter (fun idx -> not (IndexUniqueness.isPrimaryKey idx.Uniqueness) && idx.IsDisabled)
        |> List.sortBy (fun idx -> idx.SsKey)
        |> List.map (fun idx ->
            Statement.AlterIndexDisable (toTableId k, Name.value idx.Name))

    /// Emit per trigger: a `Comment` line carrying trigger metadata
    /// (name + disabled state, mirroring V1's `-- Trigger: <name>
    /// (disabled: true/false)` shape), then the `CreateTrigger`
    /// statement, then (when `Trigger.IsDisabled = true`) a post-
    /// CREATE `AlterTableDisableTrigger` statement. Sorted
    /// deterministically by `SsKey`. Slice D.2.d extends H-019's
    /// CreateTrigger emission with the disable-state axis +
    /// metadata-comment per V1 fixture parity.
    let private triggerStatements (k: Kind) : Statement list =
        k.Triggers
        |> List.sortBy (fun t -> t.SsKey)
        |> List.collect (fun t ->
            let triggerName = Name.value t.Name
            let metadataComment =
                System.String.Concat(  // LINT-ALLOW: terminal text-emission boundary; segments are typed (Name.value Trigger.Name + bool→string projection); BCL `String.Concat` is the use-case-specific library for the four-segment audit-narration comment
                    "Trigger: ", triggerName,
                    " (disabled: ", (if t.IsDisabled then "true" else "false"), ")")
            let createStmt = Statement.CreateTrigger t.Definition
            let baseStatements = [ Statement.Comment metadataComment; createStmt ]
            if t.IsDisabled then
                baseStatements @ [ Statement.AlterTableDisableTrigger (k.Physical, triggerName) ]
            else
                baseStatements)

    /// Emit one `CreateSequence` statement per `Catalog.Sequences` entry,
    /// sorted deterministically by `SsKey`. Sequences are catalog-level
    /// schema objects; this helper is called before the table loop in
    /// `SsdtDdlEmitter.statements` so they are deployed before any DEFAULT
    /// constraints that reference them. H-020 (Cluster A — Close the loops).
    let private sequenceStatements (catalog: Catalog) : Statement list =
        catalog.Sequences
        |> List.sortBy (fun s -> s.SsKey)
        |> List.map Statement.CreateSequence

    /// Build the cross-platform-deterministic relative path for a
    /// kind's SSDT DDL file. V1 convention: `Modules/<ModuleName>/
    /// <Schema>.<Table>.sql`. Forward-slash separators throughout.
    ///
    /// **Pillar-7 four-question analysis at this site:**
    ///   1. Use-case-specific library: `System.IO.Path.Combine`.
    ///   2. Already in BCL: yes.
    ///   3. Cost: zero LOC.
    ///   4. Structural reason it doesn't apply: **YES.**
    ///      `Path.Combine` produces platform-specific separators
    ///      (`\` on Windows, `/` on Linux/macOS), which violates T1
    ///      byte-determinism for V2's production-deployment artifact
    ///      contract. V2 RelativePath MUST be byte-identical regardless
    ///      of host platform; V1's `SsdtEmitter.cs:55-122` hard-codes
    ///      forward slashes; the SSDT consumer (Azure DevOps + DacFx)
    ///      tolerates only forward slashes in cross-platform manifests.
    ///   Conclusion: `String.Concat` with explicit `/` separator is
    ///   the right primitive; segments are typed (`Name.value m.Name`,
    ///   `k.Physical.Schema`, `k.Physical.Table` are all typed values
    ///   from V2's IR).
    let private relativePath (m: Module) (k: Kind) : string =
        let schemaStr, tableStr = TableId.qualifiedParts k.Physical
        System.String.Concat(  // LINT-ALLOW: cross-platform-deterministic relative path; Path.Combine considered + rejected (platform-specific separators violate T1 byte-determinism); segments are typed (m.Name + TableId.schemaText/tableText from Coordinates.TableId)
            "Modules/",
            Name.value m.Name,
            "/",
            schemaStr,
            ".",
            tableStr,
            ".sql")

    /// Per-kind `SetExtendedProperty` statements (chapter 4.1.A slice 8).
    /// Consumes chapter A.0' slice α's `Kind.Description` +
    /// `Attribute.Description` (V2 IR fields carrying V1's
    /// `MS_Description` metadata) and slice ζ's `ExtendedProperties`
    /// lists on `Kind` / `Attribute` / `Index`. Retires the
    /// `Tolerance.CommentMetadataUnreflected` deferral.
    ///
    /// Emission order per V1 + canonical determinism (A33):
    ///   1. Table-level `MS_Description` (Kind.Description) — once if Some.
    ///   2. Table-level extended properties (Kind.ExtendedProperties)
    ///      in source order.
    ///   3. Per-column `MS_Description` (Attribute.Description) — once
    ///      per attribute that carries one.
    ///   4. Per-column extended properties (Attribute.ExtendedProperties)
    ///      in source order.
    ///   5. Per-index extended properties (Index.ExtendedProperties)
    ///      in source order.
    /// `Module.ExtendedProperties` are deferred-with-trigger:
    /// SQL Server's level0 = SCHEMA semantics maps module → schema
    /// only when modules align 1:1 with schemas, which V2 doesn't yet
    /// formalize. The triple-deliverable will surface when V1
    /// emission for module-level extended properties is confirmed
    /// (chapter 4.x).
    /// NM-70 (WP5) — `emitIdentityAnnotations = false` suppresses the
    /// `Projection.*` identity extended properties (`Projection.SsKey` +
    /// `Projection.LogicalName`, table and column level). All other
    /// extended properties — `MS_Description`, authored
    /// `ExtendedProperties` — still emit. `true` (the production default)
    /// keeps the bundle byte-identical. Omit is a NAMED DOWNGRADE: identity
    /// recovery degrades to name-derived SsKeys (no persisted SsKey to read
    /// back); the composition seam emits the
    /// `emission.identityAnnotations.omitted` diagnostic — this pure emitter
    /// only honors the gate.
    let private extendedPropertyStatements (emitIdentityAnnotations: bool) (k: Kind) : Statement seq =
        seq {
            let table = k.Physical
            match k.Description with
            | Some desc ->
                yield Statement.SetExtendedProperty (
                    TableProperty table, "MS_Description", Some desc)
            | None -> ()

            // Slice D.1.b — V2.LogicalName extended property at the
            // table level. Carries `Name.value k.Name` (the logical
            // entity name; `Kind.Name` is untouched by the slice-D.1.a
            // substitution). ReadSide queries this on roundtrip read
            // to hydrate `Kind.Name` from the deployed schema, so the
            // logical-vs-physical divergence survives deploy → read.
            // NM-70 — gated on the identity-annotation axis.
            if emitIdentityAnnotations then
                yield Statement.SetExtendedProperty (
                    TableProperty table, "Projection.LogicalName", Some (Name.value k.Name))

                // Wave 4.1 — V2.SsKey extended property at the table level.
                // Carries the round-trippable serialization of the kind's
                // identity (A1: identity survives rename). ReadSide reads this
                // on roundtrip and `SsKey.deserialize`s it, recovering the
                // original key instead of synthesizing `READSIDE_KIND` from
                // physical coordinates. Sibling to V2.LogicalName.
                yield Statement.SetExtendedProperty (
                    TableProperty table, "Projection.SsKey", Some (SsKey.serialize k.SsKey))

            for ep in k.ExtendedProperties do
                yield Statement.SetExtendedProperty (
                    TableProperty table, ep.Name, ep.Value)

            for attr in k.Attributes do
                let columnName = ColumnRealization.columnNameText attr.Column
                match attr.Description with
                | Some desc ->
                    yield Statement.SetExtendedProperty (
                        ColumnProperty (table, columnName), "MS_Description", Some desc)
                | None -> ()

                // Slice D.1.b — V2.LogicalName extended property at
                // the column level. Same roundtrip-recovery role as
                // the table-level sibling above. NM-70 — gated.
                if emitIdentityAnnotations then
                    yield Statement.SetExtendedProperty (
                        ColumnProperty (table, columnName), "Projection.LogicalName", Some (Name.value attr.Name))

                for ep in attr.ExtendedProperties do
                    yield Statement.SetExtendedProperty (
                        ColumnProperty (table, columnName), ep.Name, ep.Value)

            for idx in k.Indexes do
                for ep in idx.ExtendedProperties do
                    yield Statement.SetExtendedProperty (
                        IndexProperty (table, Name.value idx.Name), ep.Name, ep.Value)
        }

    /// Render one Kind to a typed `SsdtFile`. The CREATE TABLE
    /// statement (slice 1; with FKs inline per slice 5) plus zero-or-
    /// more CREATE INDEX statements (slice 3) plus zero-or-more
    /// `EXEC sys.sp_addextendedproperty` calls (slice 8 — Descriptions
    /// and ExtendedProperties) flow through ScriptDom's typed AST and
    /// emerge as SQL text only at the absolute terminal
    /// `Sql160ScriptGenerator` boundary (per pillar 1 + pillar 7).
    ///
    /// `ScriptDomGenerate.toText` (chapter 3.5) is the typed-statement-
    /// stream consumer that handles multi-statement bodies: each
    /// statement gets emitted via the pinned-options writer, with
    /// blank-line framing between statements per A33 (deterministic-
    /// ordered schema emission).
    /// Emit `Module.ExtendedProperties` as SCHEMA-level
    /// `sp_addextendedproperty` statements when the given kind is the
    /// alphabetically first kind of its schema within the module. The
    /// "first kind per schema" gate ensures each module's SCHEMA-level
    /// properties emit exactly once per distinct schema the module
    /// occupies, even if the module spans multiple schemas. Chapter 4.9
    /// slice ε.
    let private moduleSchemaPropertyStatements (m: Module) (k: Kind) : Statement seq =
        seq {
            let schema = k.Physical.Schema
            let firstKindOfSchema =
                m.Kinds
                |> List.filter (fun candidate -> candidate.Physical.Schema = schema)
                |> List.sortBy (fun candidate -> candidate.SsKey)
                |> List.tryHead
            match firstKindOfSchema with
            | Some first when first.SsKey = k.SsKey ->
                for ep in m.ExtendedProperties do
                    yield Statement.SetExtendedProperty (
                        SchemaProperty (SchemaName.value schema), ep.Name, ep.Value)
            | _ -> ()
        }

    let private kindToSsdtFile
        (renderMode: ConstraintFormatter.Mode)
        (emitIdentityAnnotations: bool)
        (overlay: DecisionOverlay)
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (m: Module)
        (k: Kind)
        : SsdtFile =
        use _ = Bench.scope "emit.ssdt.kindToSsdtFile"
        let statements =
            seq {
                yield! moduleSchemaPropertyStatements m k
                yield createTableStatement overlay targetByKey pkAttrByKey k
                // Slice 5.13.fk-features-emit (matrix row 59):
                // post-CREATE-TABLE ALTER statements preserve the
                // deployed target's NOCHECK FK trust state. Emitted
                // BEFORE indexes (CREATE INDEX is unaffected by FK
                // trust, but the FK constraint must exist before
                // ALTER references it — CREATE TABLE just created it
                // inline). Today no adapter populates
                // `IsConstraintTrusted = false`; this seam is
                // structurally positioned for the rowset-path JOIN
                // slice that wires `#FkReality.IsNoCheck`.
                yield! untrustedFkAlters overlay targetByKey pkAttrByKey k
                yield! indexStatements overlay k
                // Slice 5.13.index-features-emit (matrix row 55):
                // post-CREATE-INDEX ALTER INDEX DISABLE statements
                // preserve the deployed target's index disable state.
                // Emitted AFTER CREATE INDEX so the named index
                // exists when the ALTER references it.
                yield! disabledIndexAlters k
                yield! extendedPropertyStatements emitIdentityAnnotations k
                // H-019: triggers fire after the table + all indexes are
                // deployed so the ON <table> reference resolves cleanly.
                yield! triggerStatements k
            }
        // Reconciliation slice 3 (operator blessing, DECISIONS
        // 2026-06-13) — the per-table file body renders through the
        // SAME `Render.toText` realization the flat stream uses (one
        // rendering algorithm; A40): the framed `GO` BETWEEN statements
        // (never trailing — V1 StatementBatchFormatter.JoinStatements),
        // the constraint ladder, and the wrapped EXEC shape. Supersedes
        // the prior raw `ScriptDomGenerate.toText` no-GO contract.
        let separated =
            statements
            |> List.ofSeq
            |> List.mapi (fun i s -> if i = 0 then [ s ] else [ BatchSeparator; s ])
            |> List.concat
        // NM-38 — `renderMode` threads the operator's
        // `EmissionPolicy.RenderConstraintsElegant` axis to the constraint
        // post-processor. `Enabled` (the default wrapper) is byte-identical
        // to the prior hardcoded `Render.toText`.
        let body = Render.toTextWith renderMode separated
        { RelativePath = relativePath m k; Body = body }

    /// Lookup table from kind SsKey to owning Module. Same shape as
    /// `RawTextEmitter.moduleByKindKey` (private there; mirrored here
    /// per the two-consumer-threshold discipline — extraction earns
    /// its place at the second consumer; this file IS the second
    /// consumer; lift candidates for a future shared helper).
    let private moduleByKindKey (catalog: Catalog) : Map<SsKey, Module> =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds |> List.map (fun k -> k.SsKey, m))
        |> Map.ofList

    /// Π port realization (chapter 4.1.A slice 1). Per A18 amended,
    /// `Catalog` only — no Profile, no Policy. Per T11 (structural by
    /// construction at chapter 3.5 + 3.7), the smart-constructor's
    /// strict-equality keyset enforcement guarantees the artifact's
    /// keyset equals `Catalog.allKinds.SsKey set`. Per pillar 1 (data-
    /// structure-oriented over string-parsing), the per-kind value is a
    /// typed `SsdtFile` carrying the relative path + SQL body; strings
    /// emerge only at the terminal `Sql160ScriptGenerator` boundary in
    /// `ScriptDomGenerate.generateOne`.
    ///
    /// **V2-driver KPI Phase 2 deliverable.** This emitter completes the
    /// Π port realization across four sibling emitters (RawText
    /// `Statement list`, Json `JsonNode`, Distributions `JsonNode`,
    /// SSDT-DDL `SsdtFile`); T11 sibling-Π commutativity is structural
    /// across all four, asserted by `SiblingEmitterContractTests.fs`.
    /// Lifted FK-resolution lookups. Per session-35 (chapter 3.1) — O(1)
    /// hash lookup per reference instead of O(K) catalog scan, shared
    /// between the per-kind `emitSlices` realization and the catalog-
    /// wide `statements` realization.
    let private buildLookups
        (catalog: Catalog)
        : Kind list * Map<SsKey, Kind> * Map<SsKey, Attribute> =
        let allKinds = Catalog.allKinds catalog
        let targetByKey =
            allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        let pkAttrByKey =
            allKinds
            |> List.choose (fun k ->
                k.Attributes
                |> List.tryFind (fun a -> a.IsPrimaryKey)
                |> Option.map (fun pk -> k.SsKey, pk))
            |> Map.ofList
        allKinds, targetByKey, pkAttrByKey

    /// C1 emitter follow-on — resolve one `Reference` on `k` to a
    /// `ForeignKeyDef` against `catalog` (the public surface over the private
    /// `fkDef` + `buildLookups`). `None` when the target kind / its PK is not
    /// in the catalog (cross-catalog FK) — the migration emitter then refuses
    /// the add fail-loud rather than emitting a dangling constraint.
    let foreignKeyDefOf (catalog: Catalog) (k: Kind) (r: Reference) : ForeignKeyDef option =
        let _, targetByKey, pkAttrByKey = buildLookups catalog
        fkDef targetByKey pkAttrByKey k r

    /// C1 emitter follow-on — the `CreateIndex` statements for the given
    /// indexes of `k`, with NO operator overlay (the migration emitter is
    /// A18-pure: it reproduces each index's own uniqueness, never a tightening
    /// decision). PK-backing indexes are skipped (inlined in CREATE TABLE).
    let createIndexStatements (k: Kind) (indexes: Index list) : Statement list =
        indexStatements DecisionOverlay.empty { k with Indexes = indexes }

    /// Catalog-wide typed statement stream. Per A35 (Π's canonical
    /// output is a typed deterministic statement stream): the same
    /// CREATE TABLE + CREATE INDEX statements that `emitSlices`
    /// per-kind-bundles into `SsdtFile` bodies, here flattened across
    /// kinds in `Catalog.allKinds` order. Realization layers (Render
    /// .toText for one-string output; Deploy.executeStream for
    /// statement-by-statement deploy; canary tests for round-trip
    /// verification) consume the stream and choose their emission form.
    ///
    /// **Per `DECISIONS 2026-05-10 — RawTextEmitter retirement` (chapter
    /// 4.1.A close arc):** this function is the typed-stream
    /// equivalent of the legacy `RawTextEmitter.statements`, MINUS the
    /// raw `InsertRow` static populations (which are chapter 4.1.B's
    /// `StaticSeedsEmitter` territory with the CDC-aware MERGE shape,
    /// not raw INSERTs). Schema-only emission is what V2's production
    /// canary surface needs; data goes through the chapter-4.1.B
    /// triumvirate.
    /// Wave-2 slice 2.2 — overlay-bearing form of `statements`. The
    /// `DecisionOverlay` (the emitter-consumable projection of the tightening
    /// decisions) is a curried prefix argument; A18-amended holds because the
    /// overlay carries decisions (evidence-derived facts), never `Policy`.
    /// `statements` is the principled `empty`-default wrapper (sibling-wrapper
    /// discipline — `empty` is a default the caller couldn't otherwise
    /// access). With `empty`, output is byte-identical to pre-overlay emission.
    let statementsWithIdentityAnnotations
        (emitIdentityAnnotations: bool)
        (overlay: DecisionOverlay)
        (catalog: Catalog)
        : seq<Statement> =
        use _ = Bench.scope "emit.ssdt.statements"
        let _, targetByKey, pkAttrByKey = buildLookups catalog
        // Topological order via `TopologicalOrderPass.runWith
        // SkipSelfEdges` (per A40 / chapter-3.1 SelfLoopPolicy
        // codification): FK targets emit before their referencers,
        // so deploy-time the inline `FOREIGN KEY ... REFERENCES`
        // constraint resolves against an already-created target
        // table. Self-FKs are SQL-Server-legal inline; SkipSelfEdges
        // keeps a self-FK kind in its natural topological position.
        // Same algorithm RawTextEmitter used (mirrors session-36
        // audit Agent 4 #6: harmonization-via-parameterization;
        // single TopologicalOrderPass with two emitters).
        let order =
            (TopologicalOrderPass.runWith SkipSelfEdges catalog).Value.Order
        let kindByKey =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey, k)
            |> Map.ofList
        let orderedKinds =
            order |> List.choose (fun key -> Map.tryFind key kindByKey)
        seq {
            // Slice D.2.c — insert `BatchSeparator` after every
            // top-level statement so deploy paths split into
            // per-statement `ExecuteNonQueryAsync` round-trips
            // (BatchSplitter handles GO recognition) AND so the
            // rendered text matches V1's per-statement-group
            // emission convention (every top-level statement
            // followed by a blank line + GO + blank line).
            let yieldWithSeparator (stmt: Statement) =
                seq { yield stmt; yield BatchSeparator }
            let yieldAllWithSeparator (stmts: seq<Statement>) =
                stmts |> Seq.collect yieldWithSeparator
            // H-020: sequences before tables — they may be referenced by
            // DEFAULT constraints in CREATE TABLE statements.
            yield! yieldAllWithSeparator (sequenceStatements catalog)
            for k in orderedKinds do
                yield! yieldWithSeparator (createTableStatement overlay targetByKey pkAttrByKey k)
                // Slice 5.13.fk-features-emit — mirrors the per-kind
                // emission order in `kindToSsdtFile`: post-CREATE-TABLE
                // ALTER for untrusted FKs, then indexes, then post-
                // CREATE-INDEX ALTER for disabled indexes.
                yield! yieldAllWithSeparator (untrustedFkAlters overlay targetByKey pkAttrByKey k)
                yield! yieldAllWithSeparator (indexStatements overlay k)
                // Slice 5.13.index-features-emit (matrix row 55).
                yield! yieldAllWithSeparator (disabledIndexAlters k)
                // Slice D.1.c — match `kindToSsdtFile`'s per-kind
                // emission order so the flat-stream surface carries
                // the same SetExtendedProperty entries (including the
                // D.1.b V2.LogicalName bindings ReadSide hydrates on
                // roundtrip read). Without this, `Render.toText`-based
                // deploys (Deploy.runWithReadback / runWithLoader) lose
                // logical-name recovery and the M3 closure breaks.
                yield! yieldAllWithSeparator (extendedPropertyStatements emitIdentityAnnotations k)
                // H-019: triggers after table + indexes per kindToSsdtFile
                // emission order (ON <table> must exist before the trigger).
                yield! yieldAllWithSeparator (triggerStatements k)
        }

    /// Overlay-bearing flat stream with the identity annotations ON — the
    /// byte-identical default wrapper over `statementsWithIdentityAnnotations`
    /// (NM-70: `emit` is the default downgrade-free posture).
    let statementsWith (overlay: DecisionOverlay) (catalog: Catalog) : seq<Statement> =
        statementsWithIdentityAnnotations true overlay catalog

    /// Catalog-wide typed statement stream (the `empty`-overlay default).
    /// Byte-identical to pre-Wave-2 emission. See `statementsWith`.
    let statements (catalog: Catalog) : seq<Statement> =
        statementsWith DecisionOverlay.empty catalog

    /// NM-38 + NM-70 — overlay + constraint-rendering-mode +
    /// identity-annotation-gate form of `emitSlices`. `renderMode` threads
    /// the operator's `EmissionPolicy.RenderConstraintsElegant` axis to the
    /// per-file `Render.toTextWith` post-processor; `emitIdentityAnnotations`
    /// threads `EmissionPolicy.EmitIdentityAnnotations` (NM-70) — `true` ⇒
    /// the `Projection.*` extended properties emit (byte-identical to
    /// pre-NM-70 emission); `false` ⇒ they are suppressed (the named
    /// downgrade, diagnostic emitted at the composition seam). Per A18, the
    /// `Emitter<SsdtFile>` port stays `Catalog`-only — both are
    /// realization-layer overlay choices resolved at the composition seam,
    /// never read from `Policy` inside the emitter.
    let emitSlicesWithRendering
        (renderMode: ConstraintFormatter.Mode)
        (emitIdentityAnnotations: bool)
        (overlay: DecisionOverlay)
        : Emitter<SsdtFile> = fun catalog ->
        use _ = Bench.scope "emit.ssdt.emitSlices"
        let modules = moduleByKindKey catalog
        let _allKinds, targetByKey, pkAttrByKey = buildLookups catalog
        // Per-kind iterMap — surfaces P50/P95/P99 of per-kind emission
        // cost (the dominant emit.ssdt work at production scale is
        // proportional to the kind count). Slice A.4.7'-prelude
        // .perf-sweep-6 instrumentation gap-fill.
        ArtifactByKind.perKindBenched "emit.ssdt.emitSlices.kind" catalog (fun k ->
            match Map.tryFind k.SsKey modules with
            | Some m ->
                kindToSsdtFile renderMode emitIdentityAnnotations overlay targetByKey pkAttrByKey m k
            | None ->
                // Unreachable: `Catalog.allKinds` walks
                // `c.Modules |> List.collect (fun m -> m.Kinds)`;
                // every yielded Kind has an owning Module. The
                // defensive `invalidOp` makes the unreachability
                // structural.
                invalidOp (sprintf "SsdtDdlEmitter.emitSlices: kind %A has no owning module (unreachable; Catalog.allKinds invariant)" k.SsKey))

    /// Wave-2 slice 2.2 — overlay-bearing form of `emitSlices`. `emitSlices`
    /// is the principled `empty`-default wrapper. With `empty`, every per-kind
    /// `SsdtFile` body is byte-identical to pre-overlay emission. Renders with
    /// the `Enabled` (V1-parity) constraint mode; see `emitSlicesWithRendering`
    /// for the operator-overridable form (NM-38).
    let emitSlicesWith (overlay: DecisionOverlay) : Emitter<SsdtFile> =
        emitSlicesWithRendering ConstraintFormatter.Enabled true overlay

    /// Π port realization (the `empty`-overlay default). Byte-identical to
    /// pre-Wave-2 emission. See `emitSlicesWith`.
    let emitSlices : Emitter<SsdtFile> = emitSlicesWith DecisionOverlay.empty

    /// Wave-2 slice 2.5(b) — the FK silent-drop WITNESS (retires the slice-μ
    /// deferral). `fkDef` returns `None` (the inline FK is dropped) for two
    /// reachable reasons; both were SILENT. This sibling produces one
    /// `Warning` `DiagnosticEntry` per drop so the loss is observable.
    ///
    ///   - **unresolved target** (`emit.ssdt.foreignKey.unresolvedTargetDropped`):
    ///     the reference's target kind is absent from the catalog. NB:
    ///     `Catalog.create` already *rejects* this at construction
    ///     (`catalog.reference.danglingTarget`) — a stronger guarantee than a
    ///     witness — so this fires only for a catalog that reached the emitter
    ///     bypassing the aggregate-root smart constructor (defense in depth).
    ///   - **target missing PK** (`emit.ssdt.foreignKey.targetMissingPrimaryKeyDropped`):
    ///     the target kind resolves but declares no primary key, so there is
    ///     no column to reference. This IS reachable through `Catalog.create`
    ///     (a PK-less kind is valid) — the genuinely-reachable silent drop.
    ///
    /// **Pure sibling output (A18 holds).** The `statements` / `emitSlices`
    /// Emitter port stays `Catalog`-only and byte-identical — the witness
    /// rides a separate `Diagnostics` channel, never a `Policy` parameter.
    let foreignKeyDropDiagnostics (catalog: Catalog) : DiagnosticEntry list =
        let allKinds, targetByKey, pkAttrByKey = buildLookups catalog
        // Pillar 1 (data-structure-oriented): the structural detail rides the
        // typed `Metadata` map; the `Message` is a constant per reason. No
        // string composition at the diagnostic boundary.
        let witness (k: Kind) (r: Reference) (code: string) (message: string) : DiagnosticEntry =
            { DiagnosticEntry.create
                "emitter:ssdtDdlEmitter" DiagnosticSeverity.Warning code message
              with
                SsKey = Some r.SsKey
                Metadata =
                    Map.ofList
                        [ "sourceSchema", TableId.schemaText k.Physical
                          "sourceTable", TableId.tableText k.Physical
                          "reference", Name.value r.Name ] }
        allKinds
        |> List.collect (fun k ->
            k.References
            // Deployable references only (DECISIONS 2026-06-12) — an
            // inverse never attempts an inline FK, so its structural
            // non-resolution is not a drop to witness.
            |> List.filter Reference.isDeployable
            |> List.choose (fun r ->
                match fkDef targetByKey pkAttrByKey k r with
                | Some _ -> None
                | None ->
                    match Map.tryFind r.TargetKind targetByKey with
                    | None ->
                        Some (witness k r
                                "emit.ssdt.foreignKey.unresolvedTargetDropped"
                                "Foreign key dropped: its target kind is not present in the catalog (cross-catalog or dangling target). No inline FK constraint emitted.")
                    | Some _ ->
                        // Target resolves; the drop is a missing PK (no column
                        // to reference) — unless the source attribute itself is
                        // missing, which `Catalog.create` forbids (unreachable).
                        match Map.tryFind r.TargetKind pkAttrByKey with
                        | None ->
                            Some (witness k r
                                    "emit.ssdt.foreignKey.targetMissingPrimaryKeyDropped"
                                    "Foreign key dropped: its target kind declares no primary key to reference. No inline FK constraint emitted.")
                        | Some _ -> None))

    /// 6.A.9 — the DECISION-driven FK-drop audit trail (red-team Decision
    /// #2b). `foreignKeyStatements` filters out every reference whose key is
    /// in `overlay.DropFk` BEFORE `fkDef` is consulted, so a `Decision`-driven
    /// removal (the operator/pass decided `DoNotEnforce` on an FK the source
    /// enforced) never reaches `foreignKeyDropDiagnostics` — it was applied
    /// SILENTLY at emission. This sibling surfaces one `Warning`
    /// (`decision.fkDropped`) per dropped decision so the manifest/log names
    /// every constraint the engine removed. Pure sibling of the emitter port
    /// (A18 holds; `statements` / `emitSlices` stay byte-identical — the audit
    /// rides the `Diagnostics` channel). Pairs with `DecisionLogEmitter`.
    // DECISIONS 2026-06-12 (reconciliation slice 1) — the audit stops
    // lying: "the source enforced it" is claimed only when the
    // reference's `HasDbConstraint` says so (post-carve-out this is
    // exactly the missing-target/scoped-export case). Logical-only
    // non-introduction is a different fact with its own code —
    // `decision.fkNotIntroduced` (Info): the emitted schema matches
    // source reality. Inverse-derived references are excluded outright
    // (navigation edges; never decision subjects as of FK pass v3 —
    // the filter here is defense in depth).
    let foreignKeyDecisionDropDiagnostics
        (overlay: DecisionOverlay)
        (catalog: Catalog)
        : DiagnosticEntry list =
        Catalog.allKinds catalog
        |> List.collect (fun k ->
            k.References
            |> List.filter Reference.isDeployable
            |> List.choose (fun r ->
                if Set.contains r.SsKey overlay.DropFk then
                    let severity, code, message =
                        if r.HasDbConstraint then
                            DiagnosticSeverity.Warning,
                            "decision.fkDropped",
                            "Foreign key dropped by decision: a tightening Decision (DoNotEnforce) removed this FK constraint at emission. The source enforced it; the emitted schema does not."
                        else
                            DiagnosticSeverity.Info,
                            "decision.fkNotIntroduced",
                            "Foreign key not introduced: a tightening Decision (DoNotEnforce) declined to create this constraint. The source does not enforce it either (logical-only reference); the emitted schema matches source reality."
                    Some
                        { DiagnosticEntry.create
                            "emitter:ssdtDdlEmitter" severity code message
                          with
                            SsKey = Some r.SsKey
                            Metadata =
                                Map.ofList
                                    [ "sourceSchema", TableId.schemaText k.Physical
                                      "sourceTable", TableId.tableText k.Physical
                                      "reference", Name.value r.Name ] }
                else None))

    /// DECISIONS 2026-06-12 (reconciliation slice 1) — the FK-name
    /// collision TRIPWIRE. SQL Server constraint names are
    /// schema-scoped; two emitted FKs sharing `(schema, name)` fail
    /// deployment. V1 deduped via a silent `processedConstraints`
    /// HashSet (SmoForeignKeyBuilder.cs:23) — V2 names the wound
    /// instead: one `Error` per participating reference so every
    /// collision site is visible. With inverses excluded from emission
    /// this is structurally unreachable for forward references (the
    /// name embeds the source column, unique per reference); the
    /// tripwire guards the invariant, it does not implement behavior.
    /// Pure sibling of the emitter port (A18 holds; the audit rides
    /// the `Diagnostics` channel).
    let foreignKeyNameCollisionDiagnostics
        (overlay: DecisionOverlay)
        (catalog: Catalog)
        : DiagnosticEntry list =
        let allKinds, targetByKey, pkAttrByKey = buildLookups catalog
        let emittedFks =
            allKinds
            |> List.collect (fun k ->
                k.References
                |> List.filter Reference.isDeployable
                |> List.filter (fun r -> not (Set.contains r.SsKey overlay.DropFk))
                |> List.choose (fun r ->
                    fkDef targetByKey pkAttrByKey k r
                    |> Option.map (fun fk -> k, r, fk)))
        emittedFks
        |> List.groupBy (fun (k, _, fk) -> TableId.schemaText k.Physical, fk.Name)
        |> List.collect (fun ((schemaText, fkName), members) ->
            if List.length members <= 1 then []
            else
                members
                |> List.map (fun (k, r, _) ->
                    { DiagnosticEntry.create
                        "emitter:ssdtDdlEmitter" DiagnosticSeverity.Error
                        "emit.ssdt.foreignKey.nameCollision"
                        "Foreign-key constraint name collision: two or more emitted FK constraints share a schema-scoped name. The deployment would fail; resolve the naming overlap before publishing."
                      with
                        SsKey = Some r.SsKey
                        Metadata =
                            Map.ofList
                                [ "schema", schemaText
                                  "constraintName", fkName
                                  "sourceTable", TableId.tableText k.Physical
                                  "reference", Name.value r.Name ] }))

    /// Slice 5.13.emit-features-registry (2026-05-18) — the SSDT
    /// emitter's `RegisteredTransform` surface. Metadata-only per the
    /// OSSYS-adapter precedent (chapter A.4.7 slice δ): the emitter's
    /// `Catalog -> Result<ArtifactByKind<SsdtFile>, EmitError>`
    /// signature doesn't fit the typed `RegisteredTransform<'In, 'Out>
    /// .Run : 'In -> Lineage<Diagnostics<'Out>>` shell because Result-
    /// with-EmitError is the realization-layer boundary's error
    /// reporting; Lineage+Diagnostics is the pass-layer evidence-trail
    /// shape. The metadata view is what the registry's totality-coverage
    /// scan + manifest emission need; per-site invocation uses
    /// `SsdtDdlEmitter.emitSlices` directly.
    ///
    /// All emission sites classify as `DataIntent` per pillar 9: an
    /// SSDT emitter projects evidence from the Catalog into the
    /// realization layer's typed Statement stream; no operator opinion
    /// enters. Selection-axis operator intent (e.g., which schemas to
    /// include) runs in passes upstream of the emitter (A18 amended).
    ///
    /// The Sites enumeration is intra-pass-classification fidelity at
    /// the emission-feature level — one Site per V1-CreateTable axis
    /// V2 emits structurally. Adding a new emit feature (e.g., the
    /// row-56 partition-scheme axis) requires extending this list
    /// (and the harvest-classification rationale must name the axis
    /// substantively).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "ssdtDdlEmitter" Schema
            [ TransformSite.dataIntent "createTable"
                "Project Kind → Statement.CreateTable via ScriptDom's typed AST (CreateTableStatement). Columns / nullability / IDENTITY / multi-column PK / inline FK constraints all flow through ScriptDomBuild.buildCreateTable. The projection is shape-preserving: V2 IR evidence maps 1:1 to ScriptDom's grammar."
              TransformSite.dataIntent "createIndex"
                "Project Kind.Indexes → Statement.CreateIndex per non-PK index via ScriptDomBuild.buildCreateIndex. Key columns + sort direction + INCLUDE + filter + on-disk options (FillFactor / PadIndex / AllowRowLocks / AllowPageLocks / StatsNoRecompute) thread through ScriptDom's IndexOption hierarchy. PK-marked indexes filter out — PK is inlined in CREATE TABLE per V1 convention."
              TransformSite.dataIntent "columnDefaultClause"
                "Slice 5.13.column-features-emit (matrix row 53). Project Attribute.DefaultValue : SqlLiteral option → ScriptDom's DefaultConstraintDefinition on the column's Constraints. The literal flows through buildSqlLiteral (same path as MERGE / UPDATE statements). DefaultName (V1 constraint identity) is positioned but unwired pending the rowset-path lift of #ColumnReality.DefaultConstraintName."
              TransformSite.dataIntent "columnCheckConstraint"
                "Slice 5.13.column-features-emit (matrix row 12). Project Kind.ColumnChecks : ColumnCheck list → ScriptDom's CheckConstraintDefinition entries on TableConstraints. The check predicate parses via TSql160Parser.ParseBooleanExpression; parse-failure fallback wraps raw text. Source: V1's #ColumnCheckReality rowset (cluster A1)."
              TransformSite.dataIntent "foreignKeyConstraint"
                "Project Reference → ScriptDom's ForeignKeyConstraintDefinition (inline in CREATE TABLE). DeleteAction maps V2's ReferenceAction DU to ScriptDom's DeleteUpdateAction. Slice 5.13.fk-features-emit (matrix row 58) extended with UpdateAction when Reference.OnUpdate = Some action; None omits the clause (V1 default)."
              TransformSite.dataIntent "alterTableNoCheckConstraint"
                "Slice 5.13.fk-features-emit (matrix row 59). When Reference.IsConstraintTrusted = false, emit a post-CREATE-TABLE Statement.AlterTableNoCheckConstraint via ScriptDom's AlterTableConstraintModificationStatement with ExistingRowsCheckEnforcement = NoCheck + ConstraintEnforcement = Check. Preserves the deployed target's WITH NOCHECK FK trust state across emit → deploy → readback. Source: V1's #FkReality.IsNoCheck via the toBundle JOIN."
              TransformSite.dataIntent "alterIndexDisable"
                "Slice 5.13.index-features-emit (matrix row 55). When Index.IsDisabled = true, emit a post-CREATE-INDEX Statement.AlterIndexDisable via ScriptDom's AlterIndexStatement with AlterIndexType.Disable. Preserves the deployed target's disabled-index state. Source: V1's #AllIdx.IsDisabled via the toBundle path."
              TransformSite.dataIntent "indexIgnoreDuplicateKey"
                "Slice 5.13.index-features-emit (matrix row 55). When Index.IgnoreDuplicateKey = true, emit IGNORE_DUP_KEY = ON in the CREATE INDEX WITH clause via ScriptDom's IndexStateOption + IndexOptionKind.IgnoreDupKey. Source: V1's #AllIdx.IgnoreDupKey."
              TransformSite.dataIntent "indexDataCompression"
                "Slice 5.13.index-features-emit (matrix row 56 partial). When Index.DataCompression = Some level, emit DATA_COMPRESSION = NONE|ROW|PAGE in the CREATE INDEX WITH clause via ScriptDom's DataCompressionOption. Single-value form (uniform across partitions) ships; per-partition compression list is the row 56 residual. Source: V1's #AllIdx.DataCompressionJson parsed via tryParseUniformDataCompression."
              TransformSite.dataIntent "indexDataSpace"
                "Slice A.4.7'-prelude.row56-dataspace (LR7 closure). When Index.DataSpace = Some, emit `ON [filegroup]` (DataSpace.Filegroup) or `ON [partition_scheme]([cols])` (DataSpace.PartitionScheme) via ScriptDom's CreateIndexStatement.OnFileGroupOrPartitionScheme. Both variants share ScriptDom's FileGroupOrPartitionScheme shape (IsFileGroup discriminates); the realization-layer DU mirrors V1's `IndexDataSpace.Type` enum closed-set. Source: V1's #AllIdx.DataSpaceName + DataSpaceType (+ PartitionColumnsJson for partition schemes) projected via tryProjectDataSpace at the OssysSql adapter boundary."
              TransformSite.dataIntent "setExtendedProperty"
                "Project ExtendedProperty values at Schema / Table / Column / Index levels → Statement.SetExtendedProperty (chapter 4.1.A slice 8). ScriptDom builds EXEC sys.sp_addextendedproperty with typed ExecuteParameter binding (multi-level @level0type / @level1type / @level2type). Replaces V1's hand-rolled escaping."
              TransformSite.dataIntent "topologicalOrder"
                "Order kinds via TopologicalOrderPass.runWith SkipSelfEdges (per A40 SelfLoopPolicy) so FK targets emit before referencers — deploy-time inline FK constraints resolve against an already-created target. Same algorithm pillar that RawTextEmitter used (chapter 3.1 harmonization-via-parameterization). DataIntent: ordering is structural-evidence, not operator opinion." ]
