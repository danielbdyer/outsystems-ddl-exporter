namespace Projection.Pipeline

open Projection.Core

/// Chapter C slice C.4 — operator-supplied feature-toggle groupings
/// of registered transformations. `TransformGroup` is a closed DU
/// (preset list; see `Classification.fs`); `TransformGroups` carries
/// the runtime `Map<TransformGroup, bool>` (`true` = include the
/// group; `false` = exclude). Missing entries default to `true`
/// (V1-parity behavior: all transforms run unless the operator
/// explicitly disables).
///
/// **Pillar 9 classification** — `TransformGroups` is an
/// `OperatorIntent` overlay (operator publishes "these groups are
/// on; those are off"). Lives parallel to other Policy axes
/// (TighteningPolicy, EmissionPolicy); the chain-filter fires at the
/// Pipeline-layer realization boundary, outside Π per A18 amended.
///
/// **Tag-map locality** — the (pass name → tag set) mapping
/// `passTags` lives in this module (not as a field on
/// `RegisteredTransformMetadata` in the Core). Per pillar 9,
/// operator-overlay-axis classification belongs at the Pipeline-
/// realization layer, not on the Core's DataIntent-pure registry
/// record. The trade-off: a refactor renaming a registered pass
/// could silently break the map; the `passTagsCoverageInvariant`
/// property (tested via `TransformGroupsCoverageTests`) catches
/// drift by asserting every name in `passTags` exists in
/// `RegisteredAllTransforms.all`.

/// Typed runtime form of `Policy.TransformGroups`. Default policy is
/// "every group included"; `Map.empty` is equivalent to "every group
/// at default = `true`". Operator entries override default per group.
type TransformGroups = {
    ByGroup : Map<TransformGroup, bool>
}

[<RequireQualifiedAccess>]
module TransformGroups =

    let empty : TransformGroups = {
        ByGroup = Map.empty
    }

    let isEmpty (groups: TransformGroups) : bool =
        Map.isEmpty groups.ByGroup

    /// True iff the named group is currently enabled.
    /// Missing entries default to `true` (V1-parity: all groups on).
    let isEnabled (group: TransformGroup) (groups: TransformGroups) : bool =
        match Map.tryFind group groups.ByGroup with
        | Some b -> b
        | None   -> true

    /// The set of groups the operator has explicitly disabled. Used
    /// by the chain filter to decide which passes to skip.
    let disabledGroups (groups: TransformGroups) : Set<TransformGroup> =
        groups.ByGroup
        |> Map.toSeq
        |> Seq.filter (fun (_, enabled) -> not enabled)
        |> Seq.map fst
        |> Set.ofSeq


/// Pass-name → `Set<TransformGroup>` static map. The (name, tags)
/// pairs IS the operator-toggle-vocabulary surface; one row per pass
/// that participates in any group. Passes absent from this map carry
/// no tags (always included regardless of TransformGroups setting).
///
/// Per the closed-DU expansion empirical-test discipline: a new
/// `TransformGroup` variant + a new pass joining a group triggers
/// (a) a new variant in `Classification.fs`'s `TransformGroup` DU,
/// (b) a new row here naming the pass + its group(s),
/// (c) a DECISIONS entry naming the operator-pull trigger.
[<RequireQualifiedAccess>]
module RegisteredTransformTags =

    /// One row per registered pass that belongs to at least one
    /// `TransformGroup`. Pass names mirror `RegisteredTransform.Name`
    /// at each pass module's `.registered` declaration. The closed-DU
    /// expansion discipline means dead rows produce compile errors
    /// at the consumer (`passTagsCoverageInvariant` test enforces no
    /// row references a name absent from the registry).
    let passTags : Map<string, Set<TransformGroup>> =
        Map.ofList [
            "nullability",           Set.singleton TransformGroup.Tightening
            "uniqueIndex",           Set.singleton TransformGroup.Tightening
            "foreignKey",            Set.singleton TransformGroup.Tightening
            "categoricalUniqueness", Set.singleton TransformGroup.Tightening
            "userFkReflow",          Set.singleton TransformGroup.UserReflow
        ]

    /// Look up a pass's tags by name. `Set.empty` for passes not in
    /// the map (untagged passes always run).
    let tagsFor (name: string) : Set<TransformGroup> =
        match Map.tryFind name passTags with
        | Some s -> s
        | None   -> Set.empty


[<RequireQualifiedAccess>]
module TransformGroupsBinding =

    let private bindError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.transformGroups.%s" code) message

    /// Parse a textual group name (operator config string) into the
    /// typed closed-DU value. The recognized vocabulary is the
    /// closed-DU's case names verbatim; structural totality means
    /// unknown names surface as `pipeline.transformGroups.unknownGroup`
    /// before the filter fires.
    let private parseGroupName (name: string) : Result<TransformGroup> =
        match name with
        | "Tightening" -> Result.success TransformGroup.Tightening
        | "UserReflow" -> Result.success TransformGroup.UserReflow
        | other ->
            Result.failureOf (
                bindError
                    "unknownGroup"
                    (sprintf
                        "policy.transformGroups entry '%s' is not a recognized TransformGroup. Known: Tightening | UserReflow."
                        other))

    /// Build the typed `TransformGroups` runtime value from a parsed
    /// `Config`. Aggregates all unknown-group errors so the operator
    /// sees every misspelled entry in one pass. Duplicate group names
    /// (operator lists the same group twice with different values)
    /// take the LAST entry — `Map.ofList` semantics; mirrors C.3's
    /// duplicate-ref behavior.
    let fromConfig
        (cfg: Config.Config)
        : Result<TransformGroups> =
        cfg.Policy.TransformGroups
        |> List.map (fun entry ->
            match parseGroupName entry.Name with
            | Error es -> Error es
            | Ok group -> Result.success (group, entry.Enabled))
        |> Result.aggregate
        |> Result.map (fun pairs ->
            { ByGroup = Map.ofList pairs })
