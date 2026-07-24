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
            "bridgeRetarget",        Set.singleton TransformGroup.BridgeRetarget
        ]

    /// NM-44 — the partial `OverlayAxis → TransformGroup` map: which
    /// operator-intent AXIS a pass's `OperatorIntent` site carries
    /// implies which feature-toggle GROUP it belongs to. Most axes map
    /// to no group (`None`): `Selection` / `Emission` / `Insertion` /
    /// `Ordering` are operator-intent axes that carry no group toggle
    /// (a `VisibilityMask` Selection pass, a `TableRename` Emission
    /// pass, a `TopologicalOrderPass` Ordering pass all always run —
    /// they are not group-toggleable). Only `Tightening` maps to a
    /// group (`TransformGroup.Tightening`).
    ///
    /// This is INTENTIONALLY partial, NOT a bijection. `TransformGroup`
    /// and `OverlayAxis` are distinct concepts (Classification.fs: a
    /// group names *which preset*; an axis names *whose intent*). The
    /// `UserReflow` group is the worked counter-example: `UserFkReflowPass`
    /// classifies its `OperatorIntent` site on the *Selection* axis
    /// ("re-direction reads more naturally as Selection"), yet the
    /// operator-toggle preset it belongs to is `UserReflow`. So the
    /// `UserReflow` group is NOT axis-derivable; it stays a hand `passTags`
    /// row keyed by the pass name. The reverse-coverage guard
    /// (`TransformGroupsBindingTests`) therefore scopes to the
    /// axis-derivable groups — it catches the dominant silent-always-run
    /// risk (a new `OperatorIntent Tightening` pass added without a
    /// `passTags` row) without over-firing on the group-less axes.
    let groupForAxis (axis: OverlayAxis) : TransformGroup option =
        match axis with
        | Tightening -> Some TransformGroup.Tightening
        | Selection
        | Emission
        | Insertion
        | Ordering -> None

    /// Look up a pass's tags by name. `Set.empty` for passes not in
    /// the map (untagged passes always run).
    ///
    /// NM-44 — `passTags` is the hand-maintained pass-name → group map.
    /// Coverage was historically ONE-DIRECTIONAL (every tagged name is a
    /// real pass), so a new group-toggleable pass added without a `passTags`
    /// row would silently ALWAYS RUN, ignoring the operator's group toggle.
    /// The reverse-coverage guard now closes the axis-derivable half via
    /// `groupForAxis` (above): any pass whose `Sites` carry `OperatorIntent`
    /// on an axis that maps to a `TransformGroup` MUST be tagged with that
    /// group. The `UserReflow` group is not axis-derivable (see `groupForAxis`)
    /// and stays a hand row guarded by the forward coverage test.
    let tagsFor (name: string) : Set<TransformGroup> =
        match Map.tryFind name passTags with
        | Some s -> s
        | None   -> Set.empty


[<RequireQualifiedAccess>]
module TransformGroupsBinding =

    /// Parse a textual group name (operator config string) into the
    /// typed closed-DU value. The recognized vocabulary is the
    /// closed-DU's case names verbatim; structural totality means
    /// unknown names surface as `pipeline.transformGroups.unknownGroup`
    /// before the filter fires.
    let private parseGroupName (name: string) : Result<TransformGroup> =
        Binding.ofClosedName ConfigAxis.TransformGroups "unknownGroup" "policy.transformGroups entry" "TransformGroup"
            [ "Tightening", TransformGroup.Tightening
              "UserReflow", TransformGroup.UserReflow
              "BridgeRetarget", TransformGroup.BridgeRetarget ]
            name

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
            parseGroupName entry.Name
            |> Result.map (fun group -> (group, entry.Enabled)))
        |> Result.aggregate
        |> Result.map (fun pairs ->
            // Wave-3 (uat-users collapse, 2026-05-30) — `UserReflow` is
            // OPT-IN (off by default), unlike `Tightening` (opt-out, V1-parity).
            // There is no standalone `uat-users` verb; the Dev→UAT user-FK
            // reflow collapses into `full-export` (this config switch) and
            // `transfer` (`--reconcile <User>:<emailColumn>`, live source+sink).
            // Unless the operator explicitly names UserReflow in
            // `policy.transformGroups`, it is disabled — `userFkReflowPass` is
            // excluded from the chain entirely. Operator entries (true OR
            // false) are honored verbatim; only the *absent* case flips.
            let explicit = pairs |> List.map fst |> Set.ofList
            let withUserReflowOptIn =
                if Set.contains TransformGroup.UserReflow explicit then pairs
                else (TransformGroup.UserReflow, false) :: pairs
            // BridgeRetarget is likewise OPT-IN (off by default): the pass is
            // excluded from the chain unless the operator explicitly names it in
            // `policy.transformGroups` (alongside `overrides.bridgeRetargets` +
            // the emission signoff). Absent ⇒ disabled; explicit entries honored.
            let withBridgeRetargetOptIn =
                if Set.contains TransformGroup.BridgeRetarget explicit then withUserReflowOptIn
                else (TransformGroup.BridgeRetarget, false) :: withUserReflowOptIn
            { ByGroup = Map.ofList withBridgeRetargetOptIn })
