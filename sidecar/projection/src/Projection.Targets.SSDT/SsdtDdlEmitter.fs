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

    let private toTableId (k: Kind) : TableId =
        { Schema = k.Physical.Schema; Table = k.Physical.Table; Catalog = None }

    /// Derive a `ColumnDef` (the Statement-DU column shape from
    /// chapter 3.5) from a V2 `Attribute`. The `Provenance` field is
    /// empty for SSDT DDL emission — per chapter pre-scope §10
    /// `Tolerance.IgnoreHeaderComments = true` initially, V2 omits
    /// V1's `/* Source: ... */` per-table header block.
    let private columnDef (a: Attribute) : ColumnDef =
        {
            Name         = a.Column.ColumnName
            Type         = a.Type
            Length       = a.Length
            Precision    = a.Precision
            Scale        = a.Scale
            Nullable     = a.Column.IsNullable
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
            DefaultName  = None
            Provenance   = ""
        }

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
            |> List.map (fun a -> a.Column.ColumnName)
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
                    Name = System.String.Concat("PK_", k.Physical.Schema, "_", k.Physical.Table)  // LINT-ALLOW: V1 naming-convention PK constraint name; ScriptDom has no helper for V1-specific naming; segments are typed (Coordinates.TableId fields)
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
    /// territory) — the FK is silently dropped, with a future
    /// Diagnostics scaffolding (slice μ deferral) naming the drop.
    ///
    /// **Naming convention (chapter 4.1.A pre-scope §3 + V1
    /// `ForeignKeyNameFactory.cs:17-60`):**
    /// `FK_<OwnerTable>_<TargetTable>_<SourceColumn>`. Length-cap at
    /// 128 with `_<sha256-12-hex>` suffix when over is V1's
    /// `ConstraintNameNormalizer` discipline; deferred-with-trigger
    /// to slice 6 when cross-module FKs make the length cap
    /// observable on real OSSYS-shaped fixtures.
    let private fkDef
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        (r: Reference)
        : ForeignKeyDef option =
        let sourceColumnOpt =
            k.Attributes
            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
            |> Option.map (fun a -> a.Column.ColumnName)
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
                System.String.Concat(  // LINT-ALLOW: V1 FK naming-convention mirror; V1 ForeignKeyNameFactory considered + cannot be referenced (cherry-pick discipline); segments are typed (Coordinates.TableId fields + Attribute.Column.ColumnName)
                    "FK_",
                    k.Physical.Table,
                    "_",
                    target.Physical.Table,
                    "_",
                    sourceColumn)
            Some
                {
                    Name         = fkName
                    SourceColumn = sourceColumn
                    Target       = toTableId target
                    TargetColumn = pkAttr.Column.ColumnName
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
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        : Statement =
        let columns = k.Attributes |> List.map columnDef
        let pk = pkDef k
        let fks = k.References |> List.choose (fkDef targetByKey pkAttrByKey k)
        // Slice 5.13.column-features-emit: thread Kind.ColumnChecks
        // (chapter A.0' slice ε IR; now populated via cluster A1's
        // rowset path lifting #ColumnCheckReality) through the
        // realization layer's CHECK-constraint emission.
        let checks = k.ColumnChecks |> List.map columnCheckDef
        Statement.CreateTable (toTableId k, columns, pk, fks, checks)

    /// Yield one `AlterTableNoCheckConstraint` statement per
    /// `IsConstraintTrusted = false` FK on this kind. Emitted AFTER
    /// the kind's CREATE TABLE so the named constraint exists when the
    /// ALTER references it. Slice 5.13.fk-features-emit (matrix row 59).
    ///
    /// Same FK resolution path as `createTableStatement` (via
    /// `fkDef`); a reference that doesn't resolve to a `ForeignKeyDef`
    /// (cross-catalog target; missing PK on target) silently drops
    /// here too — the corresponding CREATE TABLE inline FK is absent
    /// by the same predicate, so the ALTER would be referencing a
    /// non-existent constraint anyway.
    let private untrustedFkAlters
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        : Statement list =
        k.References
        |> List.choose (fun r ->
            match fkDef targetByKey pkAttrByKey k r with
            | Some fk when not fk.IsConstraintTrusted ->
                Some (Statement.AlterTableNoCheckConstraint (toTableId k, fk.Name))
            | _ -> None)

    /// Resolve a column-SsKey to its physical column name within a
    /// kind. The IR's `Index.Columns` carries SsKey list (per
    /// `Catalog.fs:228`); the SSDT emission needs physical column
    /// names. Per A1 (identity-survives-rename), each column SsKey
    /// resolves to exactly one Attribute; the resolved column name
    /// is the Attribute's physical `ColumnName`.
    let private resolveColumnName (k: Kind) (columnSsKey: SsKey) : string =
        match k.Attributes |> List.tryFind (fun a -> a.SsKey = columnSsKey) with
        | Some a -> a.Column.ColumnName
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
    let private indexStatements (k: Kind) : Statement list =
        k.Indexes
        |> List.filter (fun idx -> not idx.IsPrimaryKey)
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
            let indexDef : IndexDef =
                {
                    Name     = Name.value idx.Name
                    Table    = toTableId k
                    Columns  = keyColumns
                    IsUnique = idx.IsUnique
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
        |> List.filter (fun idx -> not idx.IsPrimaryKey && idx.IsDisabled)
        |> List.sortBy (fun idx -> idx.SsKey)
        |> List.map (fun idx ->
            Statement.AlterIndexDisable (toTableId k, Name.value idx.Name))

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
        System.String.Concat(  // LINT-ALLOW: cross-platform-deterministic relative path; Path.Combine considered + rejected (platform-specific separators violate T1 byte-determinism); segments are typed (m.Name + k.Physical.Schema + k.Physical.Table from Coordinates.TableId)
            "Modules/",
            Name.value m.Name,
            "/",
            k.Physical.Schema,
            ".",
            k.Physical.Table,
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
    let private extendedPropertyStatements (k: Kind) : Statement seq =
        seq {
            let table = k.Physical
            match k.Description with
            | Some desc ->
                yield Statement.SetExtendedProperty (
                    TableProperty table, "MS_Description", Some desc)
            | None -> ()

            for ep in k.ExtendedProperties do
                yield Statement.SetExtendedProperty (
                    TableProperty table, ep.Name, ep.Value)

            for attr in k.Attributes do
                let columnName = attr.Column.ColumnName
                match attr.Description with
                | Some desc ->
                    yield Statement.SetExtendedProperty (
                        ColumnProperty (table, columnName), "MS_Description", Some desc)
                | None -> ()
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
                        SchemaProperty schema, ep.Name, ep.Value)
            | _ -> ()
        }

    let private kindToSsdtFile
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (m: Module)
        (k: Kind)
        : SsdtFile =
        use _ = Bench.scope "emit.ssdt.kindToSsdtFile"
        let statements =
            seq {
                yield! moduleSchemaPropertyStatements m k
                yield createTableStatement targetByKey pkAttrByKey k
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
                yield! untrustedFkAlters targetByKey pkAttrByKey k
                yield! indexStatements k
                // Slice 5.13.index-features-emit (matrix row 55):
                // post-CREATE-INDEX ALTER INDEX DISABLE statements
                // preserve the deployed target's index disable state.
                // Emitted AFTER CREATE INDEX so the named index
                // exists when the ALTER references it.
                yield! disabledIndexAlters k
                yield! extendedPropertyStatements k
            }
        let body = ScriptDomGenerate.toText statements
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
    let statements (catalog: Catalog) : seq<Statement> =
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
            for k in orderedKinds do
                yield createTableStatement targetByKey pkAttrByKey k
                // Slice 5.13.fk-features-emit — mirrors the per-kind
                // emission order in `kindToSsdtFile`: post-CREATE-TABLE
                // ALTER for untrusted FKs, then indexes, then post-
                // CREATE-INDEX ALTER for disabled indexes.
                yield! untrustedFkAlters targetByKey pkAttrByKey k
                yield! indexStatements k
                // Slice 5.13.index-features-emit (matrix row 55).
                yield! disabledIndexAlters k
        }

    let emitSlices : Emitter<SsdtFile> = fun catalog ->
        use _ = Bench.scope "emit.ssdt.emitSlices"
        let modules = moduleByKindKey catalog
        let allKinds, targetByKey, pkAttrByKey = buildLookups catalog
        let slices =
            allKinds
            |> List.map (fun k ->
                match Map.tryFind k.SsKey modules with
                | Some m ->
                    k.SsKey, kindToSsdtFile targetByKey pkAttrByKey m k
                | None ->
                    // Unreachable: `Catalog.allKinds` walks
                    // `c.Modules |> List.collect (fun m -> m.Kinds)`;
                    // every yielded Kind has an owning Module. The
                    // defensive `invalidOp` makes the unreachability
                    // structural.
                    invalidOp (sprintf "SsdtDdlEmitter.emitSlices: kind %A has no owning module (unreachable; Catalog.allKinds invariant)" k.SsKey))
            |> Map.ofList
        ArtifactByKind.create catalog slices

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
              TransformSite.dataIntent "setExtendedProperty"
                "Project ExtendedProperty values at Schema / Table / Column / Index levels → Statement.SetExtendedProperty (chapter 4.1.A slice 8). ScriptDom builds EXEC sys.sp_addextendedproperty with typed ExecuteParameter binding (multi-level @level0type / @level1type / @level2type). Replaces V1's hand-rolled escaping."
              TransformSite.dataIntent "topologicalOrder"
                "Order kinds via TopologicalOrderPass.runWith SkipSelfEdges (per A40 SelfLoopPolicy) so FK targets emit before referencers — deploy-time inline FK constraints resolve against an already-created target. Same algorithm pillar that RawTextEmitter used (chapter 3.1 harmonization-via-parameterization). DataIntent: ordering is structural-evidence, not operator opinion." ]
