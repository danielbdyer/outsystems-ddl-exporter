namespace Twin.Core

open Projection.Core

/// THE TWIN — the identity anti-corruption layer (THE_TWIN.md §4.4).
///
/// The engine keys everything on `SsKey`; the ejected estate carries
/// none. This module is the entire bridge, in both directions:
///
///   coordinate → kind/attribute   (`bindKind` / `bindColumn`) — exact
///     case-insensitive name lookup against a catalog (typically the
///     twin database read back through `ReadSide`, whose synthesized
///     name-based keys are a pure function of the same names, so the
///     binding is stable run to run by construction);
///
///   kind/attribute → coordinate   (`coordinateOfKind` /
///     `coordinateOfColumn`) — the projection every Twin artifact is
///     written in.
///
/// Nothing else in the Twin context touches `SsKey` directly; an SsKey
/// lives exactly as long as one process run and never reaches a config
/// file, an artifact, or an operator surface.
type CatalogIndex = private {
    kindsByKey   : Map<string, Kind>
    columnsByKey : Map<string, Kind * Attribute>
}

[<RequireQualifiedAccess>]
module CatalogIndex =

    /// Index a catalog for coordinate binding. One walk; keys are the
    /// case-insensitive normalized coordinate keys, so lookups agree
    /// with SQL Server default-collation matching. Physical realization
    /// (`kind.Physical` / `attribute.Column.ColumnName`) is the name
    /// authority — for a read-back catalog those ARE the estate's
    /// logical names, which is the point of the eject.
    let ofCatalog (catalog: Catalog) : CatalogIndex =
        let kinds =
            Catalog.allKinds catalog
            |> List.map (fun k -> TableId.normalizedKey k.Physical, k)
            |> Map.ofList
        let columns =
            Catalog.allKinds catalog
            |> List.collect (fun k ->
                k.Attributes
                |> List.map (fun a ->
                    let key =
                        System.String.Concat(
                            TableId.normalizedKey k.Physical,
                            ".",
                            (ColumnRealization.columnNameText a.Column).ToLowerInvariant())  // LINT-ALLOW: terminal normalized comparison key (lowercased dotted path); the key IS a string, no AST
                    key, (k, a)))
            |> Map.ofList
        { kindsByKey = kinds; columnsByKey = columns }

    let private tableNotFound (c: TableCoordinate) : ValidationError =
        ValidationError.createWithMetadata
            "twin.coordinate.table.unknown"
            "The coordinate names a table the estate definition does not carry. Check the spelling against the repository's table scripts."
            (Map.ofList [ "coordinate", Some (TableCoordinate.text c) ])

    let private columnNotFound (c: ColumnCoordinate) : ValidationError =
        ValidationError.createWithMetadata
            "twin.coordinate.column.unknown"
            "The coordinate names a column the estate definition does not carry. Check the spelling against the repository's table scripts."
            (Map.ofList [ "coordinate", Some (ColumnCoordinate.text c) ])

    /// Bind a table coordinate to its kind. Law 2 (coordinate totality):
    /// an unbound coordinate is a named refusal, never a silent skip.
    let bindKind (index: CatalogIndex) (c: TableCoordinate) : Result<Kind> =
        match Map.tryFind (TableCoordinate.key c) index.kindsByKey with
        | Some kind -> Result.success kind
        | None -> Result.failureOf (tableNotFound c)

    /// Bind a column coordinate to its kind + attribute. Law 2.
    let bindColumn (index: CatalogIndex) (c: ColumnCoordinate) : Result<Kind * Attribute> =
        match Map.tryFind (ColumnCoordinate.key c) index.columnsByKey with
        | Some bound -> Result.success bound
        | None -> Result.failureOf (columnNotFound c)

    /// Does the index carry this table? (The evidence importer's
    /// closed-set membership probe — refusals are built by the caller
    /// so the error can name the source, not just the coordinate.)
    let containsTable (index: CatalogIndex) (c: TableCoordinate) : bool =
        Map.containsKey (TableCoordinate.key c) index.kindsByKey

    /// Every kind in the index, coordinate-keyed. For coverage boards.
    let kinds (index: CatalogIndex) : (TableCoordinate * Kind) list =
        index.kindsByKey
        |> Map.toList
        |> List.map (fun (_, k) -> TableCoordinate.ofTableId k.Physical, k)

[<RequireQualifiedAccess>]
module TwinIdentity =

    /// The coordinate of a kind — its physical realization, which for
    /// the ejected estate IS the logical name pair.
    let coordinateOfKind (k: Kind) : TableCoordinate =
        TableCoordinate.ofTableId k.Physical

    /// The coordinate of an attribute within its kind.
    let coordinateOfColumn (k: Kind) (a: Attribute) : ColumnCoordinate =
        { Table = coordinateOfKind k; Column = a.Column.ColumnName }
