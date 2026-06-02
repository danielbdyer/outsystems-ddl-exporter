namespace Projection.Core

// LINT-ALLOW-FILE: validation-error message construction in
// `ModuleFilter.Options.create` and `ModuleFilter.apply` uses
// `sprintf "...%s..."` to format operator-supplied module / entity
// names in the diagnostic message. Same allowed-exception class as
// `Catalog.create` / `Module.create` / `UserRemap.fs` (per their
// LINT-ALLOW-FILE block); the validation payload is operator-facing
// audit-trail prose where typed value formatting is the right
// primitive. Per `DECISIONS 2026-05-09 — Built-in obligation`, no BCL
// alternative emits typed-string validation message construction in
// a way that would be cleaner.

// File-header carbon-copy citation (per `DECISIONS 2026-05-16 (later) —
// V2 self-containment + carbon-copy editorial inheritance`). V1 source
// files inherited from at chapter B.4 slice 4 (2026-05-19):
//   src/Osm.Pipeline/ModelIngestion/ModuleFilter.cs          (~140 LOC)
//   src/Osm.Domain/Configuration/ModuleFilterOptions.cs      (~220 LOC)
//   src/Osm.Domain/Configuration/ModuleEntityFilterOptions.cs (~130 LOC)
// The V2 port is a single F# file consolidating the three V1 files
// into one module-shaped surface. V1 separated by C# class-per-file
// convention; V2's F# convention is to consolidate related types +
// the consuming operation into one file under the bounded-context
// module name. Refactor status: partially refactored — V2 vocabulary
// (Catalog / Module / Kind / Name / SsKey) replaces V1 vocabulary
// (OsmModel / ModuleModel / EntityModel / ModuleName); structural
// shape preserved. V1's `ValidationOverrides` axis (per-module
// validation suppression) is NOT ported here — that surface routes
// through slice 5's `MetadataContractOverrides` port, where it is
// the natural home. ADMIRE entry: `ADMIRE.md` § "2026-05-19 —
// ModuleFilter (chapter B.4 slice 4 carbon-copy)".

/// Per-module entity filter — operator-supplied entity-name include
/// list restricting which `Kind`s of a module pass the filter. Empty
/// = no restriction; non-empty = only kinds whose `Name` matches one
/// of the filter entries pass. Matching is case-insensitive.
///
/// V2 simplification of V1's `ModuleEntityFilterOptions` (carbon-copy
/// editorial discipline): V1 carried a `NameSet` cached
/// `ImmutableHashSet<string, OrdinalIgnoreCase>` alongside the array;
/// V2 represents the same shape with a single `Set<string>` of
/// lowercase-normalized names. The case-insensitive invariant rides
/// on construction (names lowercased at `create` time); consumers
/// lookup via the lowercased candidate. No separate physical-name
/// matching axis (V1 matched both `entity.LogicalName.Value` and
/// `entity.PhysicalName.Value`) — V2's `Kind.Name` IS the operator-
/// visible name; the physical/logical split lives one level down at
/// the attribute / column-name boundary, not at the kind level.
type ModuleEntityFilter = {
    /// Lowercase-normalized entity names. Operator-supplied names
    /// are lowercased at `create` time; matching uses the
    /// lowercased candidate.
    NormalizedNames : Set<string>
    /// Original-case names preserved for diagnostic messages (the
    /// operator wrote `"E1"`; the error message should say `"E1"`
    /// not `"e1"`). Same length as `NormalizedNames`.
    OriginalNames : string list
}


/// Operator-supplied filter narrowing which modules + which kinds-
/// within-modules survive into the downstream pipeline. The dichotomy
/// per pillar 9: every field expresses `OperatorIntent of Selection`
/// — the operator declares which slice of the catalog to operate
/// against. None of these fields represent data intention; an empty-
/// options instance (`Options.empty`) is the identity (DataIntent-
/// preserving pass-through).
///
/// V2 simplification of V1's `ModuleFilterOptions` (carbon-copy):
/// V1 carried a `ValidationOverrides` axis that this port does NOT
/// carry — `ValidationOverrides` (per-module validation suppression)
/// is structurally a `MetadataContractOverrides` concern and lands
/// at slice 5's port, not here.
type ModuleFilterOptions = {
    /// Module names the operator selected. Empty = include every
    /// module that survives the system / inactive filters. Non-
    /// empty = include ONLY the named modules (every name must
    /// exist in the catalog or `apply` fails with
    /// `moduleFilter.modules.missing`). Lowercase-normalized at
    /// `create` time; matching is case-insensitive.
    Modules                : Set<string>
    /// Diagnostic-side original-case names paired with `Modules`.
    /// Preserved for error messages so operator sees their own
    /// typing. Set semantics not used; list order matches operator
    /// input order.
    ModulesOriginal        : string list
    /// When false: drop modules whose every kind carries
    /// `ModalityMark.SystemOwned` (V2 translation of V1's
    /// `IsSystemModule` flag — V1's per-module bit becomes V2's
    /// per-kind modality marked at adapter time per
    /// `CatalogReader.fs:2109` `if kindRow.IsSystemEntity then
    /// yield SystemOwned`). Default true (V1 parity); the slice-7
    /// `full-export` config maps the operator's
    /// `model.includeSystemModules` JSON property here.
    IncludeSystemModules   : bool
    /// When false: drop modules whose `IsActive = false` AND drop
    /// inactive kinds within active modules. V2 surfaces `IsActive`
    /// at module + kind + attribute levels (chapter A.0' slice β
    /// IR fidelity lift); this filter exercises the module +
    /// kind levels. Attribute-level activeness is a separate axis
    /// (`OnlyActiveAttributes` in `Pipeline.Config.ModelSection`)
    /// and is NOT applied by this filter — it lives at the
    /// adapter / extraction boundary or a per-attribute pass.
    /// Default true (V1 parity).
    IncludeInactiveModules : bool
    /// Per-module entity restrictions. Map key = lowercase-
    /// normalized module name (matches `Modules` normalization).
    /// Value = the entity filter for that module. Modules absent
    /// from the map pass through with all kinds; modules present
    /// in the map keep only kinds whose `Name` matches the
    /// filter. Empty map = no per-module entity restrictions.
    EntityFilters          : Map<string, ModuleEntityFilter>
}


[<RequireQualifiedAccess>]
module ModuleEntityFilter =

    /// Smart constructor: lowercase-normalize the operator-supplied
    /// entity names; reject null / whitespace / duplicate names.
    /// Returns `Result<ModuleEntityFilter>` carrying structured
    /// validation errors on failure. Per the structural-commitment-
    /// via-construction-validation operational principle.
    let create (entityNames: string seq) : Result<ModuleEntityFilter> =
        if isNull (box entityNames) then
            Result.failureOf (
                ValidationError.create
                    "moduleFilter.entities.null"
                    "Entity filter must not be null.")
        else
        let candidates = entityNames |> Seq.toList
        let errors = ResizeArray<ValidationError>()
        let normalized = ResizeArray<string>()
        let originals = ResizeArray<string>()
        let seen = System.Collections.Generic.HashSet<string>()
        candidates
        |> List.iteri (fun idx candidate ->
            if isNull (box candidate) then
                errors.Add(
                    ValidationError.create
                        "moduleFilter.entities.nullEntry"
                        (sprintf "Entity name at position %d must not be null." idx))
            elif System.String.IsNullOrWhiteSpace(candidate) then
                errors.Add(
                    ValidationError.create
                        "moduleFilter.entities.empty"
                        (sprintf "Entity name at position %d must not be empty or whitespace." idx))
            else
                let trimmed = candidate.Trim()
                let lowered = trimmed.ToLowerInvariant()
                if seen.Add(lowered) then
                    normalized.Add(lowered)
                    originals.Add(trimmed))
        if errors.Count > 0 then
            Result.failure (List.ofSeq errors)
        elif normalized.Count = 0 then
            Result.failureOf (
                ValidationError.create
                    "moduleFilter.entities.empty"
                    "Entity filter must include at least one entity name.")
        else
            Result.success {
                NormalizedNames = Set.ofSeq normalized
                OriginalNames   = List.ofSeq originals
            }

    /// Predicate: does the given `Kind`'s name match this filter?
    /// Case-insensitive — the normalized form was computed at
    /// `create` time.
    let matches (kind: Kind) (filter: ModuleEntityFilter) : bool =
        Set.contains (Name.value kind.Name |> fun s -> s.ToLowerInvariant()) filter.NormalizedNames

    /// Per-filter accounting: the original-case names that did NOT
    /// match any kind. Empty list = every name in the filter
    /// matched a kind. Used by `ModuleFilter.apply` to surface
    /// `moduleFilter.entities.missing` errors with operator-visible
    /// names.
    let missingNames (kinds: Kind list) (filter: ModuleEntityFilter) : string list =
        let kindNames =
            kinds
            |> List.map (fun k -> (Name.value k.Name).ToLowerInvariant())
            |> Set.ofList
        filter.OriginalNames
        |> List.filter (fun original ->
            let lowered = original.ToLowerInvariant()
            not (Set.contains lowered kindNames))


[<RequireQualifiedAccess>]
module ModuleFilter =

    /// The identity filter — passes every module + every kind
    /// through unchanged. `Options.empty` is the all-permissive
    /// shape: no module-name restriction; system + inactive modules
    /// included; no per-module entity restrictions. Operationally
    /// `apply Options.empty c == Ok c` for every catalog `c`.
    let empty : ModuleFilterOptions = {
        Modules                = Set.empty
        ModulesOriginal        = []
        IncludeSystemModules   = true
        IncludeInactiveModules = true
        EntityFilters          = Map.empty
    }

    /// Predicate: does this options instance carry any restriction
    /// at all? True iff at least one axis differs from `empty`.
    /// `apply` short-circuits to `Ok input` when this returns false.
    let hasFilter (opts: ModuleFilterOptions) : bool =
        not (Set.isEmpty opts.Modules)
        || not opts.IncludeSystemModules
        || not opts.IncludeInactiveModules
        || not (Map.isEmpty opts.EntityFilters)

    /// Smart constructor: normalize operator-supplied module names
    /// (trim + lowercase + dedupe); attach entity filters keyed by
    /// the normalized module name. Returns `Result<Options>`
    /// accumulating validation errors per module + per entity
    /// filter. Per the structural-commitment-via-construction-
    /// validation operational principle.
    let createOptions
        (modules: string seq)
        (includeSystemModules: bool)
        (includeInactiveModules: bool)
        (entityFilters: (string * string seq) seq)
        : Result<ModuleFilterOptions> =
        let modulesList =
            if isNull (box modules) then []
            else Seq.toList modules
        let errors = ResizeArray<ValidationError>()
        let normalized = ResizeArray<string>()
        let originals = ResizeArray<string>()
        let seen = System.Collections.Generic.HashSet<string>()
        modulesList
        |> List.iteri (fun idx candidate ->
            if isNull (box candidate) then
                errors.Add(
                    ValidationError.create
                        "moduleFilter.modules.null"
                        (sprintf "Module name at position %d must not be null." idx))
            elif System.String.IsNullOrWhiteSpace(candidate) then
                errors.Add(
                    ValidationError.create
                        "moduleFilter.modules.empty"
                        (sprintf "Module name at position %d must not be empty or whitespace." idx))
            else
                let trimmed = candidate.Trim()
                let lowered = trimmed.ToLowerInvariant()
                if seen.Add(lowered) then
                    normalized.Add(lowered)
                    originals.Add(trimmed))
        let entityFiltersList =
            if isNull (box entityFilters) then []
            else Seq.toList entityFilters
        let filterMap = System.Collections.Generic.Dictionary<string, ModuleEntityFilter>()
        entityFiltersList
        |> List.iter (fun (moduleKey, names) ->
            if isNull (box moduleKey) || System.String.IsNullOrWhiteSpace(moduleKey) then
                errors.Add(
                    ValidationError.create
                        "moduleFilter.entities.module.empty"
                        "Module name for entity filter must not be null or whitespace.")
            else
                let moduleLowered = moduleKey.Trim().ToLowerInvariant()
                match ModuleEntityFilter.create names with
                | Ok filter -> filterMap.[moduleLowered] <- filter
                | Error filterErrors ->
                    filterErrors
                    |> List.iter (fun e ->
                        errors.Add(
                            ValidationError.create
                                e.Code
                                (sprintf "Module '%s' entity filter invalid: %s" (moduleKey.Trim()) e.Message))))
        if errors.Count > 0 then
            Result.failure (List.ofSeq errors)
        else
            Result.success {
                Modules                = Set.ofSeq normalized
                ModulesOriginal        = List.ofSeq originals
                IncludeSystemModules   = includeSystemModules
                IncludeInactiveModules = includeInactiveModules
                EntityFilters          =
                    filterMap
                    |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                    |> Map.ofSeq
            }

    /// Predicate: is this module fully composed of `SystemOwned`
    /// kinds? V2 translation of V1's per-module `IsSystemModule`
    /// flag — V1 carried a single bit; V2's adapter
    /// (`CatalogReader.fs:2109`) marks per-kind `ModalityMark.
    /// SystemOwned`. The aggregate test "every kind in this module
    /// is SystemOwned" is the cleanest translation: a module the
    /// operator considers "the system" is one whose every entity
    /// is system-owned. An empty-kind module returns false (cannot
    /// exist post-`Module.create` per LR1 non-empty invariant).
    let private isAllSystemModule (m: Module) : bool =
        not (List.isEmpty m.Kinds)
        && m.Kinds
           |> List.forall (fun k -> List.contains ModalityMark.SystemOwned k.Modality)

    /// Apply the filter to a catalog. Returns `Result<Catalog>`
    /// carrying the filtered catalog (or accumulated validation
    /// errors on operator-supplied-name mismatches / filter-empties-
    /// everything cases).
    ///
    /// **Pillar 9 — `OperatorIntent of Selection`**: this function is
    /// the canonical operator-intent selection seam at the catalog
    /// granularity. The slice-7 `full-export` orchestrator wraps the
    /// call in `LineageEvent` emission (per the logging-format
    /// contract §18 slice-4 cue: `config.toggleResolved` per resolved
    /// rule + `transform.applied` with `intent=OperatorIntent,
    /// overlayAxis=Selection`); this function itself is pure (Core
    /// stays I/O-free per the load-bearing commitment).
    ///
    /// **Failure-mode taxonomy** (mirrors V1):
    ///   - `moduleFilter.modules.missing`: operator named a module
    ///     not present in the catalog.
    ///   - `moduleFilter.modules.empty`: every filter axis combined
    ///     would leave zero modules.
    ///   - `moduleFilter.entities.missing`: operator named an
    ///     entity not present in its module.
    ///   - `moduleFilter.entities.empty`: per-module entity filter
    ///     would leave zero kinds in a selected module.
    let apply (opts: ModuleFilterOptions) (catalog: Catalog) : Result<Catalog> =
        use _ = Bench.scope "moduleFilter.apply"
        if not (hasFilter opts) then
            Result.success catalog
        else
        // Step 1: resolve operator-supplied module names against the
        // catalog. Lookup is by lowercased Name.value; the original
        // operator-typed name surfaces in any missing-name error.
        let catalogIndex =
            catalog.Modules
            |> List.map (fun m -> (Name.value m.Name).ToLowerInvariant(), m)
            |> Map.ofList
        let selected, missingErrors =
            if Set.isEmpty opts.Modules then
                catalog.Modules, []
            else
                let resolved = ResizeArray<Module>()
                let missing = ResizeArray<string>()
                opts.ModulesOriginal
                |> List.iter (fun original ->
                    let lowered = original.ToLowerInvariant()
                    match Map.tryFind lowered catalogIndex with
                    | Some m -> resolved.Add(m)
                    | None -> missing.Add(original))
                if missing.Count > 0 then
                    let codes =
                        missing
                        |> String.concat ", "
                    [],
                    [ ValidationError.create
                        "moduleFilter.modules.missing"
                        (sprintf "Requested module(s) not found in catalog: %s." codes) ]
                else
                    List.ofSeq resolved, []
        if not (List.isEmpty missingErrors) then
            Result.failure missingErrors
        else
        // Step 2: apply system + inactive filters at module level.
        // Inactive modules also reduce their kind list to active-
        // only kinds (V1 parity: `module with { Entities =
        // activeEntities }`).
        let filteredModules =
            selected
            |> List.choose (fun m ->
                if not opts.IncludeSystemModules && isAllSystemModule m then
                    None
                elif not opts.IncludeInactiveModules then
                    if not m.IsActive then None
                    else
                        let activeKinds = m.Kinds |> List.filter (fun k -> k.IsActive)
                        if List.isEmpty activeKinds then None
                        else Some (Lens.set CatalogLenses.kindsOf activeKinds m)
                else
                    Some m)
        if List.isEmpty filteredModules then
            Result.failureOf (
                ValidationError.create
                    "moduleFilter.modules.empty"
                    "Module filter removed all modules from the catalog.")
        else
        // Step 3: apply per-module entity filters. Each filter's
        // unmatched-name set surfaces as one
        // `moduleFilter.entities.missing` error; an entity filter
        // that would zero out its module surfaces as
        // `moduleFilter.entities.empty`. Errors accumulate (a
        // multi-module filter producing two missing-name events
        // returns both).
        if Map.isEmpty opts.EntityFilters then
            Result.success { catalog with Modules = filteredModules }
        else
        let entityErrors = ResizeArray<ValidationError>()
        let adjusted =
            filteredModules
            |> List.map (fun m ->
                let moduleLowered = (Name.value m.Name).ToLowerInvariant()
                match Map.tryFind moduleLowered opts.EntityFilters with
                | None -> m
                | Some filter ->
                    let kept = m.Kinds |> List.filter (fun k -> ModuleEntityFilter.matches k filter)
                    let missing = ModuleEntityFilter.missingNames m.Kinds filter
                    if not (List.isEmpty missing) then
                        entityErrors.Add(
                            ValidationError.create
                                "moduleFilter.entities.missing"
                                (sprintf
                                    "Module '%s' does not contain entity(ies): %s."
                                    (Name.value m.Name)
                                    (String.concat ", " missing)))
                    if List.isEmpty kept && List.isEmpty missing then
                        entityErrors.Add(
                            ValidationError.create
                                "moduleFilter.entities.empty"
                                (sprintf
                                    "Entity filter removed all entities from module '%s'."
                                    (Name.value m.Name)))
                    Lens.set CatalogLenses.kindsOf kept m)
        if entityErrors.Count > 0 then
            Result.failure (List.ofSeq entityErrors)
        else
        // Step 4: post-entity-filter empty-module check — a filter
        // that matched zero kinds in a module should already have
        // surfaced as `entities.empty` above; this catch is for the
        // degenerate "all modules ended up with zero kinds AND no
        // entity filter was active" case (cannot occur structurally
        // post-Step-2 since Step-2's `isAllSystemModule` /
        // `IncludeInactiveModules` checks already drop empty
        // modules — defense-in-depth).
        if adjusted |> List.exists (fun m -> List.isEmpty m.Kinds) then
            Result.failureOf (
                ValidationError.create
                    "moduleFilter.modules.empty"
                    "Module filter removed all entities from at least one module.")
        else
            Result.success { catalog with Modules = adjusted }
