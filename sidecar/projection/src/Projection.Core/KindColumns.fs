namespace Projection.Core

/// **The per-kind column vocabulary shared by the data-emission lanes.**
///
/// The static-seed, migration-dependency, and staged-`#temp` MERGE
/// emitters each answer the same handful of questions about a `Kind`:
/// which columns it writes, in what order, which are its primary key,
/// which are updatable, the column→type lookup the renderer needs to
/// format raw IR values, and how a raw `StaticRow` lifts to typed
/// `SqlLiteral`s. These projections are pure functions of the `Kind`
/// (and the catalog's canonical attribute order); they carried as
/// byte-identical `private` copies in `StaticSeedsEmitter`,
/// `MigrationDependenciesEmitter`, AND `StagedMerge` — the docstrings
/// there literally read "mirrors `StaticSeedsEmitter.X`."
///
/// Per the two-consumer-threshold discipline (`DECISIONS 2026-05-13`),
/// the second consumer earns the extraction; here there are three. This
/// module is the single source of truth so a change to "what columns a
/// kind writes" (e.g. the computed-column exclusion that keeps the
/// `AllColumns` projection and the per-row VALUES list aligned) lands in
/// one place and every lane inherits it.
///
/// **A33 (deterministic-ordered emission).** Column order follows the
/// kind's declared attribute order, which is itself canonical after
/// `CanonicalizeIdentity`. Computed columns (`Computed = Some _`) are
/// SQL-Server-computed at write time and can never appear in an INSERT
/// column list, an UPDATE SET, or a USING source — `writableAttributes`
/// is the single filter that drops them.
[<RequireQualifiedAccess>]
module KindColumns =

    /// Column→type lookup for a kind's attributes — `(Name, PrimitiveType)`
    /// per attribute — so the renderer can format raw IR values as SQL
    /// literals.
    let columnTypeLookup (k: Kind) : Map<Name, PrimitiveType> =
        k.Attributes
        |> List.map (fun a -> a.Name, a.Type)
        |> Map.ofList

    /// Writable attributes for a kind — computed/persisted columns
    /// (`Computed = Some _`) are SQL-Server-computed and never written
    /// (no INSERT column, no UPDATE SET, no USING source; including one
    /// is a hard SQL error). The single filter that keeps `AllColumns`
    /// and the per-row VALUES projection aligned.
    let writableAttributes (k: Kind) : Attribute list =
        k.Attributes |> List.filter (fun a -> a.Computed = None)

    /// Writable column names in the kind's declared (canonical) order —
    /// the MERGE's `AllColumns` projection. Computed columns excluded.
    let orderedColumnNames (k: Kind) : string list =
        writableAttributes k |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    /// Primary-key column names in declared order. The MERGE's ON-clause
    /// joins on these; the WHEN-NOT-MATCHED INSERT includes them; the
    /// WHEN-MATCHED UPDATE excludes them (PK is stable per row identity);
    /// Phase-2 UPDATEs use the same set for the WHERE-clause row scope.
    let pkColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> a.IsPrimaryKey)
        |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    /// Non-PK column names (the MERGE's UPDATE-target columns).
    let updatableColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> not a.IsPrimaryKey)
        |> List.map (fun a -> ColumnRealization.columnNameText a.Column)

    /// Project a raw `StaticRow` (the `DataLoadPlan`'s converged row
    /// carrier — both the static and migration row shapes fold into it)
    /// into the typed `Map<Name, SqlLiteral>` shape `DataInsertRow.Values`
    /// expects (slice κ pillar-1 lift). Absent values default to the
    /// empty raw (`""` → NULL per the `RawValueCodec` contract); unknown
    /// columns default to `PrimitiveType.Text`.
    let rowToTypedValues
        (typeLookup: Map<Name, PrimitiveType>)
        (attributes: Attribute list)
        (row: StaticRow)
        : Map<Name, SqlLiteral> =
        attributes
        |> List.map (fun a ->
            let raw =
                Map.tryFind a.Name row.Values
                |> Option.defaultValue ""
            let typ =
                Map.tryFind a.Name typeLookup
                |> Option.defaultValue PrimitiveType.Text
            a.Name, SqlLiteral.ofRaw typ raw)
        |> Map.ofList

    /// Project a typed-Values row into the ordered `SqlLiteral list` the MERGE's
    /// `Rows` (the VALUES projection) expects, iterating the kind's attributes in
    /// declared order. Slice δ: columns named in `deferred` emit
    /// `SqlLiteral.NullLit` regardless of the row's value (the Phase-1 cycle
    /// break — the deferred FK is NULLed on insert and re-pointed in Phase-2).
    let typedValuesToSqlLiterals
        (deferred: Set<Name>)
        (attributes: Attribute list)
        (values: Map<Name, SqlLiteral>)
        : SqlLiteral list =
        attributes
        |> List.map (fun a ->
            if Set.contains a.Name deferred then
                SqlLiteral.NullLit
            else
                Map.tryFind a.Name values
                |> Option.defaultValue SqlLiteral.NullLit)
