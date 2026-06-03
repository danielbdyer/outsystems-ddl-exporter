namespace Projection.Targets.SSDT

open Projection.Core

/// Π_SchemaMigration — 6.A.12. The **implied emission differential**: turns a
/// `CatalogDiff` (the *virtual differential* — a structural comparison of two
/// periodic catalog values) into the minimum-viable-touch schema DDL. Where
/// `SsdtDdlEmitter` emits a full CREATE TABLE per kind, this emits only the
/// `ALTER TABLE … ADD` / `ALTER TABLE … ALTER COLUMN` the delta requires — so
/// a column type change touches one column, not the whole table.
///
/// **Positioning — this is the IMPERATIVE executor's emitter, NOT the
/// declarative SSDT deploy artifact** (`WAVE_6_ONTOLOGY.md` §4). The canonical
/// SSDT deploy is *declarative*: emit the target `CREATE TABLE` model + the
/// `.refactorlog`, and **DacFx computes the ALTER/DROP at publish** (applying
/// `BlockOnPossibleDataLoss` / `DropObjectsNotInSource`). This emitter's
/// statements are consumed only by the in-place `migrate --execute` executor
/// (`MigrationRun.execute` → `Deploy.executeBatch`, the live square / T16) and
/// as a **preview + verification lens** (does DacFx's publish plan match what
/// the engine predicted?). It never feeds the `.dacpac` (see
/// `DacpacEmitter.isSchemaStatement`). The destructive emission below
/// (`--allow-drops`) is therefore the *in-place evolver's* drop, distinct from
/// the declarative path's drop-by-absence-then-DacFx.
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
/// emission).** Emitted: column ADD (additive) and *widening* column-SHAPE
/// ALTER (type / length / precision / scale / nullability). A **narrowing**
/// (NULL→NOT NULL tightening, or length / precision / scale shrink) is a
/// declared-loss: refused fail-loud (`migration.narrowingColumn`, no ALTER)
/// unless the operator declares it via the same `allowDrops` gate as a DROP.
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

    /// Is the shape change a **narrowing** — destructive at deploy? NULL→NOT NULL
    /// (existing NULL rows violate), a declared-length / precision / scale shrink
    /// (truncation / precision loss). A narrowing is a declared-loss, gated by the
    /// SAME `allowDrops` declaration as a destructive DROP: refused fail-loud
    /// unless the operator has declared it, never emitted silently. (Mirrors the
    /// `migration.destructiveColumnDrop` gate — `WAVE_6_ONTOLOGY.md` §5.)
    let private narrowing (src: Attribute) (tgt: Attribute) : string option =
        let nullTightened = src.Column.IsNullable && not tgt.Column.IsNullable
        let shrank (s: int option) (t: int option) =
            match s, t with Some a, Some b -> b < a | _ -> false
        if nullTightened then Some "nullability tightened (NULL → NOT NULL); existing NULL rows would violate the new constraint."
        elif shrank src.Length tgt.Length then Some "declared length narrowed; existing values may be truncated."
        elif shrank src.Precision tgt.Precision then Some "declared precision narrowed; existing values may lose precision."
        elif shrank src.Scale tgt.Scale then Some "declared scale narrowed; existing values may lose precision."
        else None

    /// Per-kind emission. Accumulates ALTER statements + diagnostics for one
    /// kind's `AttributeDiff`. Source order (the kind's attribute list, via
    /// `AttributeDiff` construction) keeps emission deterministic (T1).
    let private kindMigration
        (allowDrops: bool)
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
                            // Narrowing (NULL→NOT NULL / length / precision / scale
                            // shrink) is a declared-loss — refuse fail-loud unless
                            // the operator has declared it (the same `allowDrops`
                            // gate as a destructive DROP). Without the declaration:
                            // Error + NO ALTER. With it: emit the ALTER.
                            let narrowed =
                                sourceKind
                                |> Option.bind (fun sk -> attrByKey sk change.AttributeKey)
                                |> Option.bind (fun srcAttr -> narrowing srcAttr tgtAttr)
                            match narrowed with
                            | Some msg when not allowDrops ->
                                Error (diag DiagnosticSeverity.Error "migration.narrowingColumn"
                                            (sprintf "Column narrowing is destructive (%s); refused — pass --allow-drops to emit the ALTER COLUMN." msg)
                                            change.AttributeKey targetKind)
                            | _ -> Ok stmt)
            let alters = alterResults |> List.choose (function Ok s -> Some s | Error _ -> None)
            let alterRefusals = alterResults |> List.choose (function Error e -> Some e | Ok _ -> None)
            // Dropped columns — destructive: emit DROP COLUMN under --allow-drops,
            // else refuse fail-loud. The column name comes from the SOURCE kind
            // (the attribute is removed, so it is absent from the target).
            let dropStmts, dropRefusals =
                if allowDrops then
                    let stmts =
                        ad.Removed
                        |> Set.toList
                        |> List.sortBy SsKey.rootOriginal
                        |> List.choose (fun key ->
                            sourceKind
                            |> Option.bind (fun sk -> attrByKey sk key)
                            |> Option.map (fun a ->
                                Statement.AlterTableDropColumn (table, ColumnRealization.columnNameText a.Column)))
                    stmts, []
                else
                    [],
                    (ad.Removed
                     |> Set.toList
                     |> List.map (fun key ->
                        diag DiagnosticSeverity.Error "migration.destructiveColumnDrop"
                            "Column drop is destructive; refused — pass --allow-drops to emit DROP COLUMN."
                            key targetKind))
            adds @ alters @ dropStmts, alterRefusals @ dropRefusals

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
        (allowDrops: bool)
        (sourceCatalog: Catalog)
        (targetCatalog: Catalog)
        (kindKey: SsKey)
        (rd: ReferenceDiff)
        : Statement list * DiagnosticEntry list =
        match Catalog.tryFindKind kindKey targetCatalog with
        | None -> [], []
        | Some targetKind ->
            let table = targetKind.Physical
            let refByKey = targetKind.References |> List.map (fun r -> r.SsKey, r) |> Map.ofList
            let sourceKind = Catalog.tryFindKind kindKey sourceCatalog
            let sourceRefByKey =
                sourceKind
                |> Option.map (fun sk -> sk.References |> List.map (fun r -> r.SsKey, r) |> Map.ofList)
                |> Option.defaultValue Map.empty
            // The deployed constraint name of a SOURCE-side FK (for DROP).
            let sourceFkName (key: SsKey) : string option =
                match sourceKind, Map.tryFind key sourceRefByKey with
                | Some sk, Some r -> SsdtDdlEmitter.foreignKeyDefOf sourceCatalog sk r |> Option.map (fun fk -> fk.Name)
                | _ -> None
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
            // Changed: Trust-only → reproduce WITH NOCHECK; other shape changes →
            // DROP + ADD CONSTRAINT under --allow-drops, else refuse.
            let changeStmts, changeRefusals =
                rd.Changed
                |> List.fold
                    (fun (stmts, refusals) (change: ReferenceChange) ->
                        match Map.tryFind change.ReferenceKey refByKey with
                        | Some r when change.Facets = Set.singleton ReferenceFacet.Trust && not r.IsConstraintTrusted ->
                            match SsdtDdlEmitter.foreignKeyDefOf targetCatalog targetKind r with
                            | Some fk -> stmts @ nocheckSteps fk, refusals
                            | None -> stmts, refusals
                        | Some r when allowDrops ->
                            // DROP the old constraint (source name), ADD the new.
                            match sourceFkName change.ReferenceKey, SsdtDdlEmitter.foreignKeyDefOf targetCatalog targetKind r with
                            | Some oldName, Some newFk ->
                                let trust = if not r.IsConstraintTrusted then nocheckSteps newFk else []
                                stmts @ [ Statement.AlterTableDropConstraint (table, oldName); Statement.AlterTableAddForeignKey (table, newFk) ] @ trust, refusals
                            | _ -> stmts, refusals
                        | _ ->
                            stmts,
                            refusals @ [ diag DiagnosticSeverity.Error "migration.unsupportedReferenceChange"
                                            "FK shape change needs DROP+ADD CONSTRAINT; refused — pass --allow-drops to emit it."
                                            change.ReferenceKey targetKind ])
                    ([], [])
            // Removed FKs — DROP CONSTRAINT under --allow-drops, else refuse.
            let removeStmts, removeRefusals =
                if allowDrops then
                    let stmts =
                        rd.Removed
                        |> Set.toList
                        |> List.sortBy SsKey.rootOriginal
                        |> List.choose (fun key -> sourceFkName key |> Option.map (fun name -> Statement.AlterTableDropConstraint (table, name)))
                    stmts, []
                else
                    [],
                    (rd.Removed
                     |> Set.toList
                     |> List.map (fun key ->
                        diag DiagnosticSeverity.Error "migration.destructiveReferenceDrop"
                            "FK drop changes referential integrity; refused — pass --allow-drops to emit DROP CONSTRAINT."
                            key targetKind))
            addStmts @ changeStmts @ removeStmts, addRefusals @ changeRefusals @ removeRefusals

    /// Per-kind index channel emission.
    let private kindIndexMigration
        (allowDrops: bool)
        (sourceCatalog: Catalog)
        (targetCatalog: Catalog)
        (kindKey: SsKey)
        (idd: IndexDiff)
        : Statement list * DiagnosticEntry list =
        match Catalog.tryFindKind kindKey targetCatalog with
        | None -> [], []
        | Some targetKind ->
            let table = targetKind.Physical
            let sourceIndexName (key: SsKey) : string option =
                Catalog.tryFindKind kindKey sourceCatalog
                |> Option.bind (fun sk -> sk.Indexes |> List.tryFind (fun i -> i.SsKey = key))
                |> Option.map (fun i -> Name.value i.Name)
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
            // Changed index — DROP (old name) + CREATE (new) under --allow-drops, else refuse.
            let changeStmts, changeRefusals =
                if allowDrops then
                    let stmts =
                        idd.Changed
                        |> List.collect (fun (c: IndexChange) ->
                            match sourceIndexName c.IndexKey, targetKind.Indexes |> List.tryFind (fun i -> i.SsKey = c.IndexKey) with
                            | Some oldName, Some newIdx ->
                                Statement.DropIndex (table, oldName) :: SsdtDdlEmitter.createIndexStatements targetKind [ newIdx ]
                            | _ -> [])
                    stmts, []
                else
                    [],
                    (idd.Changed
                     |> List.map (fun (c: IndexChange) ->
                        diag DiagnosticSeverity.Error "migration.unsupportedIndexChange"
                            "Index change needs DROP+CREATE INDEX; refused — pass --allow-drops to emit it." c.IndexKey targetKind))
            // Removed index — DROP INDEX under --allow-drops, else refuse.
            let removeStmts, removeRefusals =
                if allowDrops then
                    let stmts =
                        idd.Removed
                        |> Set.toList
                        |> List.sortBy SsKey.rootOriginal
                        |> List.choose (fun key -> sourceIndexName key |> Option.map (fun name -> Statement.DropIndex (table, name)))
                    stmts, []
                else
                    [],
                    (idd.Removed
                     |> Set.toList
                     |> List.map (fun key ->
                        diag DiagnosticSeverity.Error "migration.destructiveIndexDrop"
                            "Index drop is destructive; refused — pass --allow-drops to emit DROP INDEX." key targetKind))
            addStmts @ changeStmts @ removeStmts, pkRefusals @ changeRefusals @ removeRefusals

    /// Catalog-level sequence channel emission (sequences are not kind-scoped).
    let private sequenceMigration
        (allowDrops: bool)
        (sourceCatalog: Catalog)
        (targetCatalog: Catalog)
        (sd: SequenceDiff)
        : Statement list * DiagnosticEntry list =
        let seqDiag (code: string) (message: string) (key: SsKey) : DiagnosticEntry =
            { DiagnosticEntry.create source DiagnosticSeverity.Error code message with SsKey = Some key }
        let sourceSeq (key: SsKey) = sourceCatalog.Sequences |> List.tryFind (fun s -> s.SsKey = key)
        let dropOf (s: Sequence) = Statement.DropSequence (s.Schema, Name.value s.Name)
        let addStmts =
            targetCatalog.Sequences
            |> List.filter (fun s -> Set.contains s.SsKey sd.Added)
            |> List.sortBy (fun s -> SsKey.rootOriginal s.SsKey)
            |> List.map Statement.CreateSequence
        // Changed sequence — DROP (old) + CREATE (new) under --allow-drops, else
        // refuse. Value-preserving ALTER SEQUENCE is a noted refinement.
        let changeStmts, changeRefusals =
            if allowDrops then
                let stmts =
                    sd.Changed
                    |> List.collect (fun (c: SequenceChange) ->
                        match sourceSeq c.SequenceKey, targetCatalog.Sequences |> List.tryFind (fun s -> s.SsKey = c.SequenceKey) with
                        | Some oldSeq, Some newSeq -> [ dropOf oldSeq; Statement.CreateSequence newSeq ]
                        | _ -> [])
                stmts, []
            else
                [],
                (sd.Changed
                 |> List.map (fun (c: SequenceChange) ->
                    seqDiag "migration.unsupportedSequenceChange"
                        "Sequence change needs DROP+CREATE (or ALTER SEQUENCE); refused — pass --allow-drops to emit it." c.SequenceKey))
        let removeStmts, removeRefusals =
            if allowDrops then
                let stmts =
                    sd.Removed
                    |> Set.toList
                    |> List.sortBy SsKey.rootOriginal
                    |> List.choose (fun key -> sourceSeq key |> Option.map dropOf)
                stmts, []
            else
                [],
                (sd.Removed
                 |> Set.toList
                 |> List.map (seqDiag "migration.destructiveSequenceDrop" "Sequence drop is destructive; refused — pass --allow-drops to emit DROP SEQUENCE."))
        addStmts @ changeStmts @ removeStmts, changeRefusals @ removeRefusals

    /// Emit the schema migration's ALTER differential from a `CatalogDiff`.
    /// Kinds sorted by SsKey root for deterministic statement order. The
    /// `Diagnostics` channel carries `Error` refusals (not emitted) so a
    /// consumer fails loud on any `Error` before deploying. A18 holds — the
    /// emitter consumes the diff (evidence), never `Policy`.
    /// `allowDrops` gates the destructive emission — DROP COLUMN / DROP
    /// CONSTRAINT / DROP INDEX / DROP SEQUENCE + the DROP-then-recreate for FK /
    /// index / sequence reshapes, AND a column **narrowing** (NULL→NOT NULL /
    /// length / precision / scale shrink — destructive at deploy). With it
    /// `false` (the default), every destructive touch refuses fail-loud with a
    /// named Error — `migrate`'s `RefusedBySchemaErrors`. With it `true` the
    /// operator has accepted the data/integrity loss (the same gate as
    /// `Migration.plan`'s violations).
    let emitWith (allowDrops: bool) (diff: CatalogDiff) : Diagnostics<Statement list> =
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
        let seqStmts, seqEntries = sequenceMigration allowDrops sourceCatalog targetCatalog (CatalogDiff.sequenceDiff diff)
        let attrStmts, attrEntries = foldByKind (CatalogDiff.attributeDiffs diff) (kindMigration allowDrops sourceCatalog targetCatalog)
        let idxStmts, idxEntries = foldByKind (CatalogDiff.indexDiffs diff) (kindIndexMigration allowDrops sourceCatalog targetCatalog)
        let refStmts, refEntries = foldByKind (CatalogDiff.referenceDiffs diff) (kindReferenceMigration allowDrops sourceCatalog targetCatalog)
        let statements = seqStmts @ attrStmts @ idxStmts @ refStmts
        let entries = seqEntries @ attrEntries @ idxEntries @ refEntries
        Diagnostics.tellMany entries (Diagnostics.ofValue statements)

    /// Non-destructive emission (the safe default — every drop refuses). The
    /// `emitWith false` specialization preserved as the prior call surface.
    let emit (diff: CatalogDiff) : Diagnostics<Statement list> = emitWith false diff
