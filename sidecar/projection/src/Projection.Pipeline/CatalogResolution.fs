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
