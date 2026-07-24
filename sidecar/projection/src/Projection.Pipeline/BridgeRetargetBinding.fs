namespace Projection.Pipeline

open Projection.Core

/// Binds `overrides.bridgeRetargets` (textual config) into the typed
/// `BridgeRetargetPolicy` the decision pass reads off `Policy`. Fail-closed: an
/// entity / relationship / bridge attribute the operator names that is not in the
/// model is a NAMED refusal (`pipeline.config.bridgeRetargets.*`), never a silent
/// skip.
///
/// **The evidence boundary.** The binder assembles only the STRUCTURAL,
/// catalog-derivable facts of each retarget's profile — the bridge attribute is
/// present, whether it is the bridge's primary key (a hazard), whether the source
/// and bridge key TYPES match, and the reference's existing constraint trust.
/// Every DATA-derived fact (resolution coverage, actual uniqueness / nullness,
/// orphans, payload conflicts, identity evidence) stays at its FAIL-CLOSED default
/// (`BridgeRetargetProfile.unproven`), so a configured retarget is BLOCKED — and
/// `RetargetFk` stays empty, emission byte-identical — until live profiling
/// evidence (a later slice) proves the data half. This is the safe posture: a
/// retarget never lands on the strength of the catalog declaration alone.
[<RequireQualifiedAccess>]
module BridgeRetargetBinding =

    let private err (code: string) (message: string) : Result<'a> =
        Result.failureOf (ValidationError.create code message)

    let private ciEq (a: string) (b: string) : bool =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    /// Resolve one config entry to a `BridgeRetargetPlan` (fail-closed).
    let private bindOne (catalog: Catalog) (entry: Config.BridgeRetargetEntry) : Result<BridgeRetargetPlan> =
        // Resolve the FK to retarget: the owning kind (entity coordinate) + the
        // reference (relationship name) on it.
        let kindMatches =
            Catalog.allModulesKinds catalog
            |> List.filter (fun (m, k) ->
                (entry.Entity.Module = "" || ciEq (Name.value m.Name) entry.Entity.Module)
                && ciEq (Name.value k.Name) entry.Entity.Entity)
            |> List.map snd
            |> List.distinctBy (fun k -> k.SsKey)
        match kindMatches with
        | [] ->
            err "pipeline.config.bridgeRetargets.entity.notFound"
                (String.concat "" [ "bridge retarget '"; entry.Id; "': entity "; entry.Entity.Module; "/"; entry.Entity.Entity; " is not in the model" ])
        | _ :: _ :: _ ->
            err "pipeline.config.bridgeRetargets.entity.ambiguous"
                (String.concat "" [ "bridge retarget '"; entry.Id; "': entity "; entry.Entity.Module; "/"; entry.Entity.Entity; " is ambiguous across the resolved scope" ])
        | [ kind ] ->
            match kind.References |> List.tryFind (fun r -> ciEq (Name.value r.Name) entry.Relationship) with
            | None ->
                err "pipeline.config.bridgeRetargets.relationship.notFound"
                    (String.concat "" [ "bridge retarget '"; entry.Id; "': relationship '"; entry.Relationship; "' is not a reference on "; entry.Entity.Entity ])
            | Some reference ->
                let sourceAttr = kind.Attributes |> List.tryFind (fun a -> a.SsKey = reference.SourceAttribute)
                match AttributeCoordinate.resolveFull catalog entry.Bridge with
                | Error _ ->
                    err "pipeline.config.bridgeRetargets.bridge.notFound"
                        (String.concat "" [ "bridge retarget '"; entry.Id; "': bridge attribute "; entry.Bridge.Module; "/"; entry.Bridge.Entity; "/"; entry.Bridge.Attribute; " is not in the model" ])
                | Ok (bridgeKindKey, _, bridgeAttrKey) ->
                    let bridgeAttr =
                        Catalog.tryFindKind bridgeKindKey catalog
                        |> Option.bind (fun bk -> bk.Attributes |> List.tryFind (fun a -> a.SsKey = bridgeAttrKey))
                    match sourceAttr, bridgeAttr with
                    | Some sa, Some ba ->
                        // Structural catalog facts only; the data facts stay
                        // fail-closed (unproven) until profiling supplies them.
                        let existingConstraintTrusted =
                            if Reference.hasDbConstraint reference then Some (Reference.isConstraintTrusted reference)
                            else None
                        let profile =
                            { BridgeRetargetProfile.unproven entry.Id with
                                BridgeKeyPresent          = true
                                TargetsBridgePrimaryKey   = ba.IsPrimaryKey
                                KeyTypesMatch             = (sa.Type = ba.Type)
                                ExistingConstraintTrusted = existingConstraintTrusted }
                        Result.success
                            { ReferenceKey       = reference.SsKey
                              BridgeAttributeKey = bridgeAttrKey
                              Profile            = profile }
                    | None, _ ->
                        err "pipeline.config.bridgeRetargets.sourceAttribute.missing"
                            (String.concat "" [ "bridge retarget '"; entry.Id; "': the reference's source attribute is missing from its kind" ])
                    | _, None ->
                        err "pipeline.config.bridgeRetargets.bridge.attributeMissing"
                            (String.concat "" [ "bridge retarget '"; entry.Id; "': the resolved bridge attribute is missing from its kind" ])

    /// Bind every declared retarget, accumulating named refusals. `[]` ⇒
    /// `BridgeRetargetPolicy.empty` (byte-identical — the pass writes an empty map).
    let fromConfig (catalog: Catalog) (entries: Config.BridgeRetargetEntry list) : Result<BridgeRetargetPolicy> =
        entries
        |> List.map (bindOne catalog)
        |> Result.aggregate
        |> Result.map (fun plans -> { Plans = plans })
