namespace Projection.Targets.SSDT

open Projection.Core

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
        { Schema = k.Physical.Schema; Table = k.Physical.Table }

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
            Provenance   = ""
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
        Statement.CreateTable (toTableId k, columns, pk, fks)

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
            let columnNames = idx.Columns |> List.map (resolveColumnName k)
            let indexDef : IndexDef =
                {
                    Name     = Name.value idx.Name
                    Table    = toTableId k
                    Columns  = columnNames
                    IsUnique = idx.IsUnique
                }
            Statement.CreateIndex indexDef)

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

    /// Render one Kind to a typed `SsdtFile`. The CREATE TABLE
    /// statement (slice 1; with FKs inline per slice 5) plus zero-or-
    /// more CREATE INDEX statements (slice 3) flow through ScriptDom's
    /// typed AST and emerge as SQL text only at the absolute terminal
    /// `Sql160ScriptGenerator` boundary (per pillar 1 + pillar 7).
    ///
    /// `ScriptDomGenerate.toText` (chapter 3.5) is the typed-statement-
    /// stream consumer that handles multi-statement bodies: each
    /// statement gets emitted via the pinned-options writer, with
    /// blank-line framing between statements per A33 (deterministic-
    /// ordered schema emission).
    let private kindToSsdtFile
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (m: Module)
        (k: Kind)
        : SsdtFile =
        use _ = Bench.scope "emit.ssdt.kindToSsdtFile"
        let statements =
            seq {
                yield createTableStatement targetByKey pkAttrByKey k
                yield! indexStatements k
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
    let emitSlices : Emitter<SsdtFile> = fun catalog ->
        use _ = Bench.scope "emit.ssdt.emitSlices"
        let modules = moduleByKindKey catalog
        let allKinds = Catalog.allKinds catalog
        // Per session-35 (chapter 3.1) — lift `(targetByKey, pkAttrByKey)`
        // once so FK resolution is O(1) hash lookup per reference instead
        // of O(K) catalog scan. Same pattern as RawTextEmitter.emitSlices.
        let targetByKey =
            allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        let pkAttrByKey =
            allKinds
            |> List.choose (fun k ->
                k.Attributes
                |> List.tryFind (fun a -> a.IsPrimaryKey)
                |> Option.map (fun pk -> k.SsKey, pk))
            |> Map.ofList
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
