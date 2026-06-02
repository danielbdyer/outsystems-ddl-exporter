namespace Projection.Targets.SSDT

open Projection.Core

/// Π_SchemaMigration — 6.A.12. The **implied emission differential**: turns a
/// `CatalogDiff` (the *virtual differential* — a structural comparison of two
/// periodic catalog values) into the minimum-viable-touch schema DDL. Where
/// `SsdtDdlEmitter` emits a full CREATE TABLE per kind, this emits only the
/// `ALTER TABLE … ADD` / `ALTER TABLE … ALTER COLUMN` the delta requires — so
/// a column type change touches one column, not the whole table.
///
/// **Clean-room separation (the operator's requirement).** `CatalogDiff.between`
/// is the comparison; this emitter is the emission. They are distinct functions
/// over distinct types — `between : Catalog → Catalog → CatalogDiff` observes;
/// `emit : CatalogDiff → Diagnostics<Statement list>` projects. The comparison
/// never knows about emission; the emission never re-derives the comparison.
///
/// **Disjoint from the rename channel.** Renames (kind + column) are the
/// `RefactorLogEmitter`'s job — SSDT requires `sp_rename` via `.refactorlog`,
/// not DROP+ADD, or data is lost. A renamed attribute carries no shape facet
/// (`CatalogDiff` tracks rename and shape on separate axes), so this emitter
/// and the RefactorLog emitter never touch the same attribute. A column that is
/// both renamed AND reshaped gets a refactorlog rename (old→new) here-not, and
/// an ALTER COLUMN on its new (target) name there-yes — DacFx applies the
/// refactorlog rename first, then the ALTER, which references the new name.
///
/// **Safe-additive first slice (T-I faithfulness — no silent destructive
/// emission).** Emitted: column ADD (additive) and column-SHAPE ALTER
/// (type / length / precision / scale / nullability). Narrowing / NULL-
/// tightening is emitted with a `Warning` (tolerance-gated, surfaced).
/// Refused fail-loud (`Error` diagnostic, no statement): a dropped column
/// (`migration.destructiveColumnDrop`) and a facet ALTER COLUMN cannot express
/// — DEFAULT / computed / identity / primary-key (`migration.unsupportedFacetChange`),
/// which need separate constraint DDL (a follow-on). Added/removed *kinds*
/// (whole tables) are out of scope — a new table is a full CREATE via
/// `SsdtDdlEmitter`; the `migrate` orchestrator routes those.
[<RequireQualifiedAccess>]
module SchemaMigrationEmitter =

    /// Emitter version. Bump when the emission shape changes meaning
    /// (e.g., when DEFAULT-constraint ALTER DDL lands).
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private source : string = "emitter:schemaMigration"

    /// The column-shape facets `ALTER TABLE … ALTER COLUMN <col> <type>
    /// NULL|NOT NULL` can express in one statement. Other facets
    /// (`DefaultValue` / `Computed` / `Identity` / `PrimaryKey`) need
    /// separate constraint DDL and are refused fail-loud in this slice.
    let private shapeFacets : Set<AttributeFacet> =
        Set.ofList
            [ AttributeFacet.DataType
              AttributeFacet.Length
              AttributeFacet.Precision
              AttributeFacet.Scale
              AttributeFacet.Nullability ]

    let private diag
        (severity: DiagnosticSeverity)
        (code: string)
        (message: string)
        (attrKey: SsKey)
        (kind: Kind)
        : DiagnosticEntry =
        { DiagnosticEntry.create source severity code message with
            SsKey = Some attrKey
            Metadata =
                Map.ofList
                    [ "schema", TableId.schemaText kind.Physical
                      "table", TableId.tableText kind.Physical ] }

    let private attrByKey (kind: Kind) (key: SsKey) : Attribute option =
        kind.Attributes |> List.tryFind (fun a -> a.SsKey = key)

    /// Is the shape change potentially destructive at deploy? NULL→NOT NULL
    /// (existing NULL rows violate) or a declared-length / precision shrink
    /// (truncation). Surfaced as a `Warning` — emitted, but never silently.
    let private narrowingWarning (src: Attribute) (tgt: Attribute) : string option =
        let nullTightened = src.Column.IsNullable && not tgt.Column.IsNullable
        let shrank (s: int option) (t: int option) =
            match s, t with Some a, Some b -> b < a | _ -> false
        if nullTightened then Some "nullability tightened (NULL → NOT NULL); existing NULL rows would violate the new constraint."
        elif shrank src.Length tgt.Length then Some "declared length narrowed; existing values may be truncated."
        elif shrank src.Precision tgt.Precision then Some "declared precision narrowed; existing values may lose precision."
        else None

    /// Per-kind emission. Accumulates ALTER statements + diagnostics for one
    /// kind's `AttributeDiff`. Source order (the kind's attribute list, via
    /// `AttributeDiff` construction) keeps emission deterministic (T1).
    let private kindMigration
        (sourceCatalog: Catalog)
        (targetCatalog: Catalog)
        (kindKey: SsKey)
        (ad: AttributeDiff)
        : Statement list * DiagnosticEntry list =
        match Catalog.tryFindKind kindKey targetCatalog with
        | None -> [], []   // unreachable: AttributeDiffs key only present kinds
        | Some targetKind ->
            let table = targetKind.Physical
            let sourceKind = Catalog.tryFindKind kindKey sourceCatalog
            // ADD COLUMN — additive, safe. Target order for determinism.
            let adds =
                targetKind.Attributes
                |> List.choose (fun a ->
                    if Set.contains a.SsKey ad.Added then
                        Some (Statement.AlterTableAddColumn (table, SsdtDdlEmitter.columnDefOfAttribute a))
                    else None)
            // ALTER COLUMN (shape) or refusal. Source-ordered `Changed`.
            let alterResults =
                ad.Changed
                |> List.map (fun change ->
                    let nonShape = Set.difference change.Facets shapeFacets
                    if not (Set.isEmpty nonShape) then
                        // ALTER COLUMN cannot express DEFAULT/computed/identity/PK
                        // in one statement — refuse fail-loud, emit nothing for it.
                        let facetNames =
                            nonShape |> Set.toList |> List.map (sprintf "%A") |> String.concat ", "
                        Error (diag DiagnosticSeverity.Error "migration.unsupportedFacetChange"
                                    (sprintf "Column change requires separate constraint DDL (facets: %s); refused — no minimal ALTER emitted." facetNames)
                                    change.AttributeKey targetKind)
                    else
                        match attrByKey targetKind change.AttributeKey with
                        | None -> Error (diag DiagnosticSeverity.Error "migration.unsupportedFacetChange"
                                            "Changed attribute absent from target kind (unreachable)." change.AttributeKey targetKind)
                        | Some tgtAttr ->
                            let stmt = Statement.AlterTableAlterColumn (table, SsdtDdlEmitter.columnDefOfAttribute tgtAttr)
                            // Narrowing → emit + Warning (tolerance-gated).
                            let warn =
                                sourceKind
                                |> Option.bind (fun sk -> attrByKey sk change.AttributeKey)
                                |> Option.bind (fun srcAttr -> narrowingWarning srcAttr tgtAttr)
                                |> Option.map (fun msg ->
                                    diag DiagnosticSeverity.Warning "migration.narrowingColumn" msg change.AttributeKey targetKind)
                            Ok (stmt, warn))
            let alters = alterResults |> List.choose (function Ok (s, _) -> Some s | Error _ -> None)
            let alterWarnings = alterResults |> List.choose (function Ok (_, Some w) -> Some w | _ -> None)
            let alterRefusals = alterResults |> List.choose (function Error e -> Some e | Ok _ -> None)
            // Dropped columns — destructive, refused fail-loud.
            let dropRefusals =
                ad.Removed
                |> Set.toList
                |> List.map (fun key ->
                    diag DiagnosticSeverity.Error "migration.destructiveColumnDrop"
                        "Column drop is destructive; refused — no DROP COLUMN emitted (declare the drop explicitly to proceed)."
                        key targetKind)
            adds @ alters, alterWarnings @ alterRefusals @ dropRefusals

    // -- C1 follow-on: reference / index / sequence channel emission ---------
    //
    // Same "safe-additive first, destructive refused fail-loud" discipline as
    // the attribute channel. Added FKs / indexes / sequences emit their
    // minimum-viable DDL; a Trust-only FK change emits the WITH NOCHECK
    // two-step (6.A.6); every other change/removal refuses with a named Error
    // (no silent drop, no DROP+CREATE the operator didn't declare). The clean
    // ALTER for those (DROP CONSTRAINT / DROP INDEX / ALTER SEQUENCE under an
    // explicit allow-drops) is the next follow-on.

    /// Per-kind reference (FK) channel emission.
    let private kindReferenceMigration
        (targetCatalog: Catalog)
        (kindKey: SsKey)
        (rd: ReferenceDiff)
        : Statement list * DiagnosticEntry list =
        match Catalog.tryFindKind kindKey targetCatalog with
        | None -> [], []
        | Some targetKind ->
            let table = targetKind.Physical
            let refByKey = targetKind.References |> List.map (fun r -> r.SsKey, r) |> Map.ofList
            let nocheckSteps (fk: ForeignKeyDef) =
                [ Statement.AlterTableDisableConstraint (table, fk.Name)
                  Statement.AlterTableNoCheckConstraint (table, fk.Name) ]
            // Added FKs — additive. Resolve via the public emitter surface;
            // refuse fail-loud when the FK target/PK is not in the catalog.
            let addStmts, addRefusals =
                targetKind.References
                |> List.filter (fun r -> Set.contains r.SsKey rd.Added)
                |> List.fold
                    (fun (stmts, refusals) r ->
                        match SsdtDdlEmitter.foreignKeyDefOf targetCatalog targetKind r with
                        | Some fk ->
                            let trust = if not r.IsConstraintTrusted then nocheckSteps fk else []
                            stmts @ (Statement.AlterTableAddForeignKey (table, fk) :: trust), refusals
                        | None ->
                            stmts,
                            refusals @ [ diag DiagnosticSeverity.Error "migration.unresolvedReferenceAdd"
                                            "Added FK's target kind / PK is not in the catalog (cross-catalog FK); refused — no ADD CONSTRAINT emitted."
                                            r.SsKey targetKind ])
                    ([], [])
            // Changed: Trust-only → reproduce WITH NOCHECK; everything else refuses.
            let changeStmts, changeRefusals =
                rd.Changed
                |> List.fold
                    (fun (stmts, refusals) (change: ReferenceChange) ->
                        match Map.tryFind change.ReferenceKey refByKey with
                        | Some r when change.Facets = Set.singleton ReferenceFacet.Trust && not r.IsConstraintTrusted ->
                            match SsdtDdlEmitter.foreignKeyDefOf targetCatalog targetKind r with
                            | Some fk -> stmts @ nocheckSteps fk, refusals
                            | None -> stmts, refusals
                        | _ ->
                            stmts,
                            refusals @ [ diag DiagnosticSeverity.Error "migration.unsupportedReferenceChange"
                                            "FK shape change needs DROP+ADD CONSTRAINT; refused — declare it explicitly to proceed."
                                            change.ReferenceKey targetKind ])
                    ([], [])
            let removeRefusals =
                rd.Removed
                |> Set.toList
                |> List.map (fun key ->
                    diag DiagnosticSeverity.Error "migration.destructiveReferenceDrop"
                        "FK drop changes referential integrity; refused — declare the drop explicitly to proceed."
                        key targetKind)
            addStmts @ changeStmts, addRefusals @ changeRefusals @ removeRefusals

    /// Per-kind index channel emission.
    let private kindIndexMigration
        (targetCatalog: Catalog)
        (kindKey: SsKey)
        (idd: IndexDiff)
        : Statement list * DiagnosticEntry list =
        match Catalog.tryFindKind kindKey targetCatalog with
        | None -> [], []
        | Some targetKind ->
            let addedIndexes = targetKind.Indexes |> List.filter (fun i -> Set.contains i.SsKey idd.Added)
            // PK-backing indexes are inlined in CREATE TABLE; an added PK index
            // on an existing table needs table-level DDL — refuse.
            let pkRefusals =
                addedIndexes
                |> List.filter (fun i -> IndexUniqueness.isPrimaryKey i.Uniqueness)
                |> List.map (fun i ->
                    diag DiagnosticSeverity.Error "migration.unsupportedIndexChange"
                        "PK-backing index add on an existing table needs table-level DDL; refused." i.SsKey targetKind)
            let addStmts = SsdtDdlEmitter.createIndexStatements targetKind addedIndexes
            let changeRefusals =
                idd.Changed
                |> List.map (fun (c: IndexChange) ->
                    diag DiagnosticSeverity.Error "migration.unsupportedIndexChange"
                        "Index change needs DROP+CREATE INDEX; refused — declare it explicitly to proceed." c.IndexKey targetKind)
            let removeRefusals =
                idd.Removed
                |> Set.toList
                |> List.map (fun key ->
                    diag DiagnosticSeverity.Error "migration.destructiveIndexDrop"
                        "Index drop is destructive; refused — declare the drop explicitly to proceed." key targetKind)
            addStmts, pkRefusals @ changeRefusals @ removeRefusals

    /// Catalog-level sequence channel emission (sequences are not kind-scoped).
    let private sequenceMigration
        (targetCatalog: Catalog)
        (sd: SequenceDiff)
        : Statement list * DiagnosticEntry list =
        let seqDiag (code: string) (message: string) (key: SsKey) : DiagnosticEntry =
            { DiagnosticEntry.create source DiagnosticSeverity.Error code message with SsKey = Some key }
        let addStmts =
            targetCatalog.Sequences
            |> List.filter (fun s -> Set.contains s.SsKey sd.Added)
            |> List.sortBy (fun s -> SsKey.rootOriginal s.SsKey)
            |> List.map Statement.CreateSequence
        let changeRefusals =
            sd.Changed
            |> List.map (fun (c: SequenceChange) ->
                seqDiag "migration.unsupportedSequenceChange"
                    "Sequence change needs ALTER SEQUENCE; refused (follow-on)." c.SequenceKey)
        let removeRefusals =
            sd.Removed
            |> Set.toList
            |> List.map (seqDiag "migration.destructiveSequenceDrop" "Sequence drop is destructive; refused.")
        addStmts, changeRefusals @ removeRefusals

    /// Emit the schema migration's ALTER differential from a `CatalogDiff`.
    /// Kinds sorted by SsKey root for deterministic statement order. The
    /// `Diagnostics` channel carries `Warning` (narrowing — emitted) and
    /// `Error` (refusals — not emitted) so a consumer fails loud on any
    /// `Error` before deploying. A18 holds — the emitter consumes the diff
    /// (evidence), never `Policy`.
    let emit (diff: CatalogDiff) : Diagnostics<Statement list> =
        use _ = Bench.scope "emit.schemaMigration.emit"
        let sourceCatalog = CatalogDiff.source diff
        let targetCatalog = CatalogDiff.target diff
        let foldByKind (diffs: Map<SsKey, 'd>) (f: SsKey -> 'd -> Statement list * DiagnosticEntry list) =
            diffs
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.fold
                (fun (accStmts, accEntries) (kindKey, d) ->
                    let stmts, entries = f kindKey d
                    accStmts @ stmts, accEntries @ entries)
                ([], [])
        // Deploy order: sequences (schema objects referenced by DEFAULTs) →
        // column adds/alters → indexes → FKs (after their columns + target
        // tables exist). Refusals from every channel aggregate so a consumer
        // fails loud on any Error before deploying.
        let seqStmts, seqEntries = sequenceMigration targetCatalog (CatalogDiff.sequenceDiff diff)
        let attrStmts, attrEntries = foldByKind (CatalogDiff.attributeDiffs diff) (kindMigration sourceCatalog targetCatalog)
        let idxStmts, idxEntries = foldByKind (CatalogDiff.indexDiffs diff) (kindIndexMigration targetCatalog)
        let refStmts, refEntries = foldByKind (CatalogDiff.referenceDiffs diff) (kindReferenceMigration targetCatalog)
        let statements = seqStmts @ attrStmts @ idxStmts @ refStmts
        let entries = seqEntries @ attrEntries @ idxEntries @ refEntries
        Diagnostics.tellMany entries (Diagnostics.ofValue statements)
