namespace Projection.Pipeline

open Projection.Core

/// Pure catalog-coordinate resolution shared by the config binders.
/// Each `*Binding` module resolves operator-supplied textual refs
/// (logical `Module.Entity` pairs; physical table names) against the
/// loaded `Catalog` into typed `SsKey`s. The *lookup* is identical
/// across binders; only the per-binder error wrapping differs — so the
/// lookup lives here (returning `SsKey option`) and each binder keeps
/// its own `ValidationError`. Extracted at the second consumer per the
/// two-consumer threshold (the prior copies in
/// `SpecialCircumstancesBinding` + `EmissionFoldersBinding` were
/// verbatim; the latter's docstring already noted the mirror).
[<RequireQualifiedAccess>]
module CatalogResolution =

    /// Find the kind whose owning module's `Name` equals `moduleName`
    /// and whose own `Name` equals `entityName` — the logical
    /// `Module.Entity` coordinate. `None` when no kind matches; the
    /// caller supplies the structural error.
    let tryKindByLogical
        (catalog: Catalog)
        (moduleName: string)
        (entityName: string)
        : SsKey option =
        catalog.Modules
        |> List.tryPick (fun m ->
            if Name.value m.Name = moduleName then
                m.Kinds
                |> List.tryFind (fun k -> Name.value k.Name = entityName)
                |> Option.map (fun k -> k.SsKey)
            else None)

    /// Find the kind whose physical table name equals `tableName` —
    /// schema-IGNORING by design (V1's circular-dependency cycle entries
    /// don't disambiguate schemas; promote to schema-qualified matching
    /// when a real multi-schema cycle surfaces — IR grows under evidence).
    /// Case-insensitive per SQL default-collation semantics
    /// (`TableId.tableTextEquals` — N3: a raw `=` here was the latent bug,
    /// diverging from `TransferSpec`'s case-insensitive table lookup).
    let tryKindByPhysicalTable
        (catalog: Catalog)
        (tableName: string)
        : SsKey option =
        catalog.Modules
        |> List.tryPick (fun m ->
            m.Kinds
            |> List.tryFind (fun k -> TableId.tableTextEquals tableName k.Physical)
            |> Option.map (fun k -> k.SsKey))

    /// Find the attribute at the logical `Module.Entity.Attribute`
    /// coordinate. `None` when no attribute matches.
    let tryAttributeByLogical
        (catalog: Catalog)
        (moduleName: string)
        (entityName: string)
        (attributeName: string)
        : SsKey option =
        catalog.Modules
        |> List.tryPick (fun m ->
            if Name.value m.Name = moduleName then
                m.Kinds
                |> List.tryPick (fun k ->
                    if Name.value k.Name = entityName then
                        k.Attributes
                        |> List.tryFind (fun a -> Name.value a.Name = attributeName)
                        |> Option.map (fun a -> a.SsKey)
                    else None)
            else None)

    /// Find the attribute at the physical `Schema.Table.Column`
    /// coordinate. `None` when no attribute matches. Case-insensitive on
    /// all three identifiers per SQL default-collation semantics (N3 — the
    /// raw `=` form was the latent bug).
    let tryAttributeByPhysical
        (catalog: Catalog)
        (schemaName: string)
        (tableName: string)
        (columnName: string)
        : SsKey option =
        catalog.Modules
        |> List.tryPick (fun m ->
            m.Kinds
            |> List.tryPick (fun k ->
                if TableId.schemaTextEquals schemaName k.Physical && TableId.tableTextEquals tableName k.Physical then
                    k.Attributes
                    |> List.tryFind (fun a -> ColumnRealization.columnNameEquals columnName a.Column)
                    |> Option.map (fun a -> a.SsKey)
                else None))
