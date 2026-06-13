namespace Projection.Pipeline

open Projection.Adapters.OssysSql

/// Boundary translation from the `Config.ModelSection` module-selection axis
/// to the OSSYS adapter's `SnapshotParameters` — the QUERY-TIME scope
/// pushdown (reconciliation slice 4, DECISIONS 2026-06-13; operator
/// adjudication C4 in `V1_FULL_EXPORT_RECONCILIATION_PLAN.md` §6).
///
/// **The division of labor.** `ModuleFilter.apply` (via
/// `ModuleFilterBinding`) REMAINS the semantic owner of module/entity
/// selection — it runs after every model read, exactly as before. This
/// binding is the extraction-COST reduction: when the operator declares a
/// scope, the carbon-copied rowsets SQL (whose `@ModuleNamesCsv` /
/// `@EntityFilterJson` / `@IncludeSystem` / `@IncludeInactive` pushdown
/// capability V1 always used and V2's live caller never bound) narrows
/// `#E`/`#Ent` server-side so only the requested estate crosses the wire.
/// Double enforcement is V1's own precedent: SQL at query time AND
/// `ModuleFilter` at every load.
///
/// **The opt-in gate (byte-identical default).** Mirrors
/// `ModuleFilterBinding` exactly: an empty `model.modules` yields
/// `MetadataSnapshotRunner.defaultParameters` (the show-me-everything
/// stance) — the pushdown, like the filter, is opt-in through a non-empty
/// module selection, and the include flags act only alongside it (the A7
/// polarity decision, 2026-06-10).
///
/// **`OnlyActiveAttributes` is deliberately NOT pushed down.** The
/// in-memory filter does not filter attributes, so binding it would break
/// the pushdown ≡ filter equivalence (`scopedRead(scope) ≡
/// ModuleFilter.apply(scope) ∘ fullRead`) and would silently starve
/// `InactiveAttributeDiagnostics` of its evidence. The dormant
/// `model.onlyActiveAttributes` key keeps its standing deferral.
[<RequireQualifiedAccess>]
module SnapshotScopeBinding =

    /// Serialize the per-module entity allow-lists into the JSON shape the
    /// rowsets script documents (`{"ServiceCenter": ["User"], …}`,
    /// `outsystems_metadata_rowsets.sql:28`). Modules and entities are
    /// sorted case-insensitively for T1 determinism (the SQL consumes the
    /// JSON as an unordered filter; the bytes are ours to pin).
    let private entityFilterJson (selectors: Config.ModuleSelector list) : string option =
        let filters =
            selectors
            |> List.choose (function
                | Config.ModuleSelector.WithEntities (name, entities) when not (List.isEmpty entities) ->
                    Some (name, entities |> List.sortWith (fun a b -> System.String.Compare(a, b, System.StringComparison.OrdinalIgnoreCase)))
                | _ -> None)
            |> List.sortWith (fun (a, _) (b, _) -> System.String.Compare(a, b, System.StringComparison.OrdinalIgnoreCase))
        if List.isEmpty filters then None
        else
            let payload = System.Collections.Generic.Dictionary<string, string list>()
            for name, entities in filters do
                payload.[name] <- entities
            Some (System.Text.Json.JsonSerializer.Serialize payload)

    /// Construct the adapter `SnapshotParameters` from the `model` section.
    /// Empty `model.modules` ⇒ `defaultParameters` (pushdown is opt-in;
    /// byte-identical default). Non-empty ⇒ module names (sorted
    /// case-insensitively, deduplicated — the V1
    /// `ModelExtractionCommand.Create` discipline), the per-module entity
    /// allow-list JSON, and the operator's include flags.
    let fromModel (model: Config.ModelSection) : MetadataSnapshotRunner.SnapshotParameters =
        match model.Modules with
        | [] -> MetadataSnapshotRunner.defaultParameters
        | selectors ->
            let names =
                selectors
                |> List.map (function
                    | Config.ModuleSelector.Whole name             -> name
                    | Config.ModuleSelector.WithEntities (name, _) -> name)
                |> List.distinctBy (fun n -> n.ToUpperInvariant())
                |> List.sortWith (fun a b -> System.String.Compare(a, b, System.StringComparison.OrdinalIgnoreCase))
            {
                ModuleNames          = names
                IncludeSystem        = model.IncludeSystemModules
                IncludeInactive      = model.IncludeInactiveModules
                OnlyActiveAttributes = false
                EntityFilterJson     = entityFilterJson selectors
            }
