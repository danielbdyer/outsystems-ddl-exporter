namespace Projection.Core.Passes

open Projection.Core

/// The lifecycle-selection pass (the data-sink chapter, S9 — A49's "selection
/// is pure" made structural). `ModuleFilter.apply` historically dropped
/// lifecycle-excluded metadata (inactive modules/kinds; all-system modules)
/// SILENTLY inside its Result channel — rows the acquisition had already
/// paid for erased with no lineage, the exact untracked-operator-intent
/// pattern the pillar-9 sweep hunts. This pass is the SAME drop semantic
/// (`ModuleFilter.lifecycleFilter` — one definition, two channels) on the
/// lineage channel: one `Removed` event per suppressed kind, carrying the
/// typed `RemovalReason` that fired (`SystemOwnedModule` /
/// `LifecycleInactive` — the `VisibilityMask` filtering-pass convention).
///
/// Name-resolution refusals (`moduleFilter.modules.missing` /
/// `entities.missing`) STAY in `ModuleFilter`'s Result channel — a refusal
/// is not a suppression. The Pipeline's `applyModuleFilter` seam interleaves
/// this pass between `ModuleFilter.applySelection` and
/// `ModuleFilter.applyEntityFilters`, preserving the monolith's exact
/// step order (names → lifecycle → entities).
///
/// Like `VisibilityMask`, references from a surviving kind to a removed
/// kind are NOT rewritten here (the catalog's structural truth is
/// preserved); `ModuleFilter.applyEntityFilters`' cross-scope prune (or any
/// downstream consumer that cares) handles them.
[<RequireQualifiedAccess>]
module SelectionSuppression =

    /// Pass version. Bump when the suppression-evaluation rules change.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "selectionSuppression"

    /// The two lifecycle axes, as the operator's `model` section binds
    /// them. `identity` (both true) suppresses nothing — the registered
    /// chain default, byte-identical (the `VisibilityMask.empty` posture).
    type Axes = {
        IncludeSystemModules   : bool
        IncludeInactiveModules : bool
    }

    /// The no-suppression identity.
    let identity : Axes = { IncludeSystemModules = true; IncludeInactiveModules = true }

    /// The pass suppresses on operator-chosen scope — the Selection axis
    /// (the `VisibilityMask` classification, slice α's ruling: operator
    /// chose which kinds appear in the catalog).
    let private classification : Classification = OperatorIntent Selection

    let private removedEvent (reason: RemovalReason) (key: SsKey) : LineageEvent =
        LineageEvent.forPass passName version classification key (Removed reason)

    /// Run the suppression over a catalog. Module-grain traversal (a
    /// lifecycle drop can remove a module WHOLE — `CatalogTraversal
    /// .mapKinds` keeps emptied modules, so this pass walks modules
    /// directly); every suppressed kind emits one `Removed` event with
    /// the axis that fired. Identity is preserved: no SsKey is invented
    /// or rekeyed (A3, A4).
    let private run (axes: Axes) (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.selectionSuppression"
        let events = LineageBuffer.create ()
        let kept =
            c.Modules
            |> List.choose (fun m ->
                let survivor, removal =
                    ModuleFilter.lifecycleFilter axes.IncludeSystemModules axes.IncludeInactiveModules m
                for key in removal.SystemOwned do
                    LineageBuffer.add (removedEvent SystemOwnedModule key) events
                for key in removal.Inactive do
                    LineageBuffer.add (removedEvent LifecycleInactive key) events
                survivor)
        Lineage.ofValueAndEvents (LineageBuffer.toList events) { c with Modules = kept }

    /// S9 factory — the axes are operator-supplied (the `model` section's
    /// include flags). Registered in `chainSteps` with `identity` (the
    /// config-invariant metadata projection; the chain default is the
    /// no-suppression identity, byte-identical); the Pipeline's
    /// `applyModuleFilter` seam executes it with the real axes.
    let registered (axes: Axes) : RegisteredTransform<Catalog, Catalog> =
        { Name = passName
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "suppress"
                Classification = classification
                Rationale = "Drop lifecycle-excluded metadata (inactive modules/kinds when the scope excludes inactive; all-SystemOwned modules when the scope excludes system) with one Removed lineage event per suppressed kind — the axes ModuleFilter.apply used to erase silently in its Result channel. One drop semantic (ModuleFilter.lifecycleFilter), two channels; name-resolution refusals stay in ModuleFilter." } ]
          Run = fun c -> run axes c |> Lineage.map Diagnostics.ofValue
          Status = Active }
