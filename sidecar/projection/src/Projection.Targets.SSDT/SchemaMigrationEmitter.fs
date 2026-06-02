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
        let statements, entries =
            CatalogDiff.attributeDiffs diff
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.fold
                (fun (accStmts, accEntries) (kindKey, ad) ->
                    let stmts, entries = kindMigration sourceCatalog targetCatalog kindKey ad
                    accStmts @ stmts, accEntries @ entries)
                ([], [])
        Diagnostics.tellMany entries (Diagnostics.ofValue statements)
