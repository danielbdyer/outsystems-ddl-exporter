namespace Projection.Pipeline

open Projection.Core

/// Boundary translation from the `Config.ModelSection` module-selection axis
/// (JSON-shape: a `model.modules` selector list + the system / inactive
/// include flags) to the Core-shape `ModuleFilterOptions` the
/// `ModuleFilter.apply` Selection seam consumes.
///
/// Per V2_PRODUCTION_CUTOVER.md §3.4 / §5.6: Config types live at the Pipeline
/// boundary and carry JSON-shape primitives; Core types carry the
/// ubiquitous-language value objects. This module is the binding step between
/// the two (the sibling of `RenameBinding` / `TighteningBinding`), so it lives
/// in Pipeline (it depends on `Config.ModelSection`, a Pipeline type, and
/// produces `ModuleFilterOptions`, a Core type).
///
/// **Pillar 9 — `OperatorIntent of Selection`.** The mapping is a faithful
/// translation: each `ModuleSelector` becomes one module name (and, for the
/// `WithEntities` form, one per-module entity filter); `IncludeSystemModules`
/// / `IncludeInactiveModules` carry the operator's include flags through.
///
/// **The opt-in gate (byte-identical default).** Module selection is opt-in
/// through a non-empty `model.modules`. When `modules` is empty, the binding
/// returns `ModuleFilter.empty` — the all-permissive identity — so the
/// full-export path that ships today (no module selection; system + inactive
/// modules carried, never filtered at the adapter boundary per
/// `CatalogReader.fs:166`) is preserved EXACTLY. The system / inactive include
/// flags take effect only alongside a declared module selection; their config
/// defaults (`includeSystemModules: false` / `includeInactiveModules: false`)
/// thus do NOT silently activate a system/inactive drop on a config that names
/// no modules. (See the binding's `DECISIONS` cash-out: the config-default
/// polarity is the opposite of `ModuleFilter.empty`'s identity polarity, so
/// straight-through wiring would change the default; the opt-in gate resolves
/// the conflict in favor of byte-identical-by-default.)
[<RequireQualifiedAccess>]
module ModuleFilterBinding =

    /// Split a `ModuleSelector` list into the module-name include list and the
    /// per-module entity-filter pairs (`WithEntities` only). The module name is
    /// carried for every selector; the entity filter is attached for the
    /// `WithEntities` form.
    let private split (selectors: Config.ModuleSelector list) : string list * (string * string seq) list =
        let names =
            selectors
            |> List.map (function
                | Config.ModuleSelector.Whole name             -> name
                | Config.ModuleSelector.WithEntities (name, _) -> name)
        let entityFilters =
            selectors
            |> List.choose (function
                | Config.ModuleSelector.WithEntities (name, entities) -> Some (name, Seq.ofList entities)
                | Config.ModuleSelector.Whole _                       -> None)
        names, entityFilters

    /// Construct the Core-shape `ModuleFilterOptions` from the `model` section.
    /// An empty `model.modules` is the all-permissive identity (`ModuleFilter.
    /// empty`) — module selection is opt-in, so the default config is
    /// byte-identical. A non-empty selection routes the include flags + the
    /// per-module entity filters through `ModuleFilter.createOptions` (which
    /// validates + normalizes the operator-supplied names).
    let fromConfig (model: Config.ModelSection) : Result<ModuleFilterOptions> =
        match model.Modules with
        | [] -> Result.success ModuleFilter.empty
        | selectors ->
            let names, entityFilters = split selectors
            ModuleFilter.createOptions
                names
                model.IncludeSystemModules
                model.IncludeInactiveModules
                entityFilters

    /// A7 (no-silent-drop) — the include flags are inert without a declared
    /// `model.modules` selection (the opt-in gate above), so a config that
    /// sets them alone deserves a NAMED note, never silence. `Some` exactly
    /// when a flag is set and `modules` is empty; the consuming surface
    /// chooses the channel (the dispatch Note line; the full-export
    /// diagnostic stream). The global-polarity question (should the flags
    /// act estate-wide with no `modules` named?) stays parked as a deliberate
    /// default-behavior change — see `CONFIRMED_BACKLOG` "A7 polarity".
    let inertFlagNote (model: Config.ModelSection) : string option =
        if List.isEmpty model.Modules
           && (model.IncludeSystemModules || model.IncludeInactiveModules) then
            Some "model.includeSystemModules/includeInactiveModules accepted; model.modules names no modules, so the filter is inert (all modules carried). Name modules under model.modules to activate the selection."
        else None
