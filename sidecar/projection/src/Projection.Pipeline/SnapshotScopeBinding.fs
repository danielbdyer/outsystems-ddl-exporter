namespace Projection.Pipeline
// LINT-ALLOW-FILE-MUTATION: sealed function-local mutable accumulator building the snapshot-scope parameter set, returned immutably

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
/// **`OnlyActiveAttributes` is pushed down.** Inactive duplicate OSSYS
/// attributes materialize as duplicate logical columns, duplicated FK
/// constraints, duplicated index columns, and DacFx `SQL71508` /
/// unresolved-reference failures downstream. The IR's attribute
/// population must be scoped before naming, ordering, FK, index, and
/// DDL logic runs: scope is query-time selection, not post-hoc
/// de-duplication. The rowsets script filters `#Attr` at build time
/// (`outsystems_metadata_rowsets.sql`, `@OnlyActiveAttributes`), and
/// every dependent rowset (column reality, FK column pivots,
/// index-column mappings, checks, JSON-compatibility) derives from
/// `#Attr`, so none resurrect inactive attributes. Unlike the module
/// pushdown, this axis is NOT gated on a declared module selection —
/// attribute activity is orthogonal to module scope, and the config
/// default (`onlyActiveAttributes = true`) requests active-only even
/// for an unscoped export. There is no in-memory sibling seam for this
/// axis (the pushdown ≡ filter equivalence law governs the
/// module/entity axes only, with attribute activity held equal across
/// both legs). When the operator asks to include inactive attributes
/// (`onlyActiveAttributes = false`), they are preserved and later
/// diagnostics explain any deploy conflicts.
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
        | [] ->
            { MetadataSnapshotRunner.defaultParameters with
                OnlyActiveAttributes = model.OnlyActiveAttributes }
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
                OnlyActiveAttributes = model.OnlyActiveAttributes
                EntityFilterJson     = entityFilterJson selectors
            }

    /// Rewrite a snapshot scope's MODULE names through a source→sink module map
    /// (`moduleMap`, case-insensitive), for the cloned-module (`by-name`) SINK
    /// read. The `model` section names the SOURCE modules; the sink's clone
    /// modules carry the mapped names, so applying the source scope to the sink
    /// read would narrow to the wrong (or a broader, referenced-entity-
    /// duplicating) module set — the `catalog.kinds.duplicateKey` failure. The
    /// entity allow-lists ride through unchanged (cloned entities share names);
    /// a module name absent from the map passes through; an empty map is
    /// identity (so a `by-sskey` flow, or an unscoped model, is untouched).
    let remapModules
        (moduleMap: Map<string, string>)
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        : MetadataSnapshotRunner.SnapshotParameters =
        if Map.isEmpty moduleMap then parameters
        else
            let lower =
                moduleMap |> Map.toList
                |> List.map (fun (k, v) -> k.ToLowerInvariant(), v) |> Map.ofList
            let rename (n: string) = Map.tryFind (n.ToLowerInvariant()) lower |> Option.defaultValue n
            let remappedFilter =
                parameters.EntityFilterJson
                |> Option.map (fun json ->
                    let payload =
                        System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string[]>> json
                    let renamed = System.Collections.Generic.Dictionary<string, string[]>()
                    for kv in payload do renamed.[rename kv.Key] <- kv.Value
                    System.Text.Json.JsonSerializer.Serialize renamed)
            { parameters with
                ModuleNames      = parameters.ModuleNames |> List.map rename
                EntityFilterJson = remappedFilter }
