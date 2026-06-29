module Projection.Tests.TransformGroupsBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter C slice C.4 — `TransformGroupsBinding.fromConfig` coverage.
/// Mirrors the C.3 binder test structure: typed runtime overlay built
/// from a textual config section; structured errors carry the
/// `pipeline.transformGroups.*` code namespace.

let private mkConfig (entries: Config.TransformGroupEntry list) : Config.Config =
    {
        Model       = {
            Path                   = Some "ignored.json"
            Ossys                  = None
            Modules                = []
            IncludeSystemModules   = false
            IncludeInactiveModules = false
            OnlyActiveAttributes   = true
        }
        Profile     = { Path = None }
        Profiler    = { Provider = Config.ProfilerProvider.Fixture }
        Overrides   = {
            TableRenames           = []
            MigrationDependencies  = None
            StaticData             = None
            CircularDependencies   = None
            AllowMissingPrimaryKey = []
            EmissionFolders        = []
        }
        Emission    = {
            Ssdt = true; Dacpac = true; Sqlproj = false; Json = true; Distributions = true
            StaticSeeds = true; MigrationDependencies = true; Bootstrap = true; BootstrapAllData = false
            DecisionLog = true; Opportunities = true; Validations = true; IncludePlatformAutoIndexes = true; DeleteScope = None; RenderConstraintsElegant = true; EmitIdentityAnnotations = true; DataVerification = Projection.Core.DataVerification.Standard; Tolerance = None; DataStaging = Projection.Core.DataStagingPolicy.auto
        }
        Policy      = {
            Insertion       = "SchemaOnly"
            Tightening      = None
            TransformGroups = entries
        }
        Output      = { Dir = "out/" }
    }

let private hasErrorCode (code: string) (errs: ValidationError list) : bool =
    errs |> List.exists (fun e -> e.Code = code)

// ----------------------------------------------------------------------
// Empty path
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: empty config opts UserReflow OFF (opt-in default); Tightening stays opt-out`` () =
    // Wave-3 uat-users collapse: UserReflow is OPT-IN (off by default), so an
    // empty config injects it as disabled; Tightening keeps its opt-out default
    // (enabled, V1-parity, absent from the map).
    let cfg = mkConfig []
    match TransformGroupsBinding.fromConfig cfg with
    | Ok groups ->
        Assert.Equal(Some false, Map.tryFind TransformGroup.UserReflow groups.ByGroup)
        Assert.True(TransformGroups.isEnabled TransformGroup.Tightening groups)
        Assert.False(TransformGroups.isEnabled TransformGroup.UserReflow groups)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// Recognized group names
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: known Tightening group resolves to typed DU`` () =
    let cfg = mkConfig [ { Name = "Tightening"; Enabled = false } ]
    match TransformGroupsBinding.fromConfig cfg with
    | Ok groups ->
        Assert.Equal(Some false, Map.tryFind TransformGroup.Tightening groups.ByGroup)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``C.4: known UserReflow group resolves to typed DU`` () =
    let cfg = mkConfig [ { Name = "UserReflow"; Enabled = true } ]
    match TransformGroupsBinding.fromConfig cfg with
    | Ok groups ->
        Assert.Equal(Some true, Map.tryFind TransformGroup.UserReflow groups.ByGroup)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// Default semantics
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: Tightening defaults enabled (opt-out); UserReflow defaults disabled (opt-in)`` () =
    let cfg = mkConfig []
    match TransformGroupsBinding.fromConfig cfg with
    | Ok groups ->
        Assert.True(TransformGroups.isEnabled TransformGroup.Tightening groups)
        Assert.False(TransformGroups.isEnabled TransformGroup.UserReflow groups)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``C.4: isEnabled returns explicit value for groups present in config`` () =
    let cfg = mkConfig [ { Name = "Tightening"; Enabled = false } ]
    match TransformGroupsBinding.fromConfig cfg with
    | Ok groups ->
        Assert.False(TransformGroups.isEnabled TransformGroup.Tightening groups)
        // UserReflow absent → opt-in default is OFF (was `true` under the
        // pre-collapse opt-out semantics).
        Assert.False(TransformGroups.isEnabled TransformGroup.UserReflow groups)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``C.4 (uat-users collapse): UserReflow runs only when explicitly opted in`` () =
    // The opt-in switch: policy.transformGroups names UserReflow enabled.
    let cfgOptIn = mkConfig [ { Name = "UserReflow"; Enabled = true } ]
    match TransformGroupsBinding.fromConfig cfgOptIn with
    | Ok groups ->
        Assert.True(TransformGroups.isEnabled TransformGroup.UserReflow groups)
        Assert.False(Set.contains TransformGroup.UserReflow (TransformGroups.disabledGroups groups))
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)
    // Absent → disabled → userFkReflowPass filtered out of the chain.
    match TransformGroupsBinding.fromConfig (mkConfig []) with
    | Ok groups ->
        Assert.True(Set.contains TransformGroup.UserReflow (TransformGroups.disabledGroups groups))
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

[<Fact>]
let ``C.4: disabledGroups returns only groups explicitly disabled`` () =
    let cfg = mkConfig
                  [ { Name = "Tightening"; Enabled = false }
                    { Name = "UserReflow"; Enabled = true } ]
    match TransformGroupsBinding.fromConfig cfg with
    | Ok groups ->
        let disabled = TransformGroups.disabledGroups groups
        Assert.Equal(1, Set.count disabled)
        Assert.Contains(TransformGroup.Tightening, disabled)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// Unknown group names — structured error
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: unknown group name surfaces structured error`` () =
    let cfg = mkConfig [ { Name = "InvalidGroup"; Enabled = false } ]
    match TransformGroupsBinding.fromConfig cfg with
    | Ok _ -> Assert.Fail("expected Error for unknown group")
    | Error errs ->
        Assert.True(
            hasErrorCode "pipeline.transformGroups.unknownGroup" errs,
            sprintf "expected pipeline.transformGroups.unknownGroup; got %A" errs)

[<Fact>]
let ``C.4: multiple unknown groups aggregate errors`` () =
    let cfg = mkConfig
                  [ { Name = "BadOne";   Enabled = false }
                    { Name = "BadTwo";   Enabled = false }
                    { Name = "BadThree"; Enabled = false } ]
    match TransformGroupsBinding.fromConfig cfg with
    | Ok _ -> Assert.Fail("expected Error")
    | Error errs ->
        let unknowns =
            errs |> List.filter (fun e -> e.Code = "pipeline.transformGroups.unknownGroup")
        Assert.Equal(3, unknowns.Length)

// ----------------------------------------------------------------------
// Duplicate entry semantics
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: duplicate group entry takes last (Map.ofList semantics)`` () =
    let cfg = mkConfig
                  [ { Name = "Tightening"; Enabled = true }
                    { Name = "Tightening"; Enabled = false } ]
    match TransformGroupsBinding.fromConfig cfg with
    | Ok groups ->
        Assert.False(TransformGroups.isEnabled TransformGroup.Tightening groups)
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)

// ----------------------------------------------------------------------
// passTags coverage invariant — every name in passTags must exist
// in the registry
// ----------------------------------------------------------------------

[<Fact>]
let ``C.4: passTags coverage invariant — every tagged name appears in RegisteredAllTransforms.all`` () =
    let registryNames =
        RegisteredAllTransforms.all
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    let taggedNames =
        RegisteredTransformTags.passTags
        |> Map.toSeq
        |> Seq.map fst
        |> Set.ofSeq
    let missing = Set.difference taggedNames registryNames
    Assert.True(
        Set.isEmpty missing,
        sprintf "passTags references unknown registry names: %A" missing)

// ----------------------------------------------------------------------
// NM-44 — reverse-coverage guard (the deferred half).
//
// The forward invariant above proves every TAGGED name is a real pass.
// The reverse direction was missing: a group-toggleable pass added
// WITHOUT a `passTags` row would silently fall through `tagsFor`'s
// `Set.empty` default and ALWAYS RUN, ignoring the operator's group
// toggle — the exact `model.path` "accidental non-gating" dual.
//
// This guard derives the expected tag STRUCTURALLY from each pass's
// `Sites` classification: every CHAIN-STEP pass whose `Sites` carry
// `OperatorIntent` on an axis that `RegisteredTransformTags.groupForAxis`
// maps to a `TransformGroup` MUST appear in `passTags` carrying that
// group. Scoped to the axis-derivable groups (today: Tightening) so it
// does NOT over-fire on the group-less axes (Selection / Emission /
// Insertion / Ordering) — `VisibilityMask` (Selection), `TableRename`
// (Emission), `TopologicalOrderPass` (Ordering) always run and are
// correctly NOT flagged.
//
// **Scope: CHAIN STEPS only.** The group filter (`filterChainByGroups`,
// Pipeline.fs) operates on the `PassChainAdapter` chain — the passes
// projected from `RegisteredTransforms.chainSteps`. The STRATEGY
// registrations (`nullabilityRules`, `uniqueIndexRules`, …) also carry
// `OperatorIntent Tightening` sites, but they are NOT independent chain
// entries — a strategy rides INSIDE its pass, so disabling the pass
// already disables the strategy. They have no `passTags` row by
// construction and must not be flagged here. The `UserReflow` group is
// not axis-derivable (UserFkReflowPass classifies on the Selection axis)
// and is guarded by the forward test.
// ----------------------------------------------------------------------

[<Fact>]
let ``NM-44: reverse-coverage — every axis-derivable group-class chain-step pass is tagged with that group`` () =
    // For each pass, the set of axis-derived groups its OperatorIntent
    // sites imply.
    let expectedGroupsFor (rt: RegisteredTransformMetadata) : Set<TransformGroup> =
        rt.Sites
        |> List.choose (fun site ->
            match site.Classification with
            | OperatorIntent axis -> RegisteredTransformTags.groupForAxis axis
            | DataIntent -> None)
        |> Set.ofList

    // The filterable chain-step passes (what `filterChainByGroups`
    // actually toggles) — projected from the live `chainSteps`, NOT the
    // strategy registrations that ride inside their passes.
    let chainStepMetadata =
        RegisteredTransforms.chainSteps
        |> List.map ChainStep.metadata

    let violations =
        chainStepMetadata
        |> List.choose (fun rt ->
            let expected = expectedGroupsFor rt
            if Set.isEmpty expected then None
            else
                let actual = RegisteredTransformTags.tagsFor rt.Name
                let uncovered = Set.difference expected actual
                if Set.isEmpty uncovered then None
                else Some (rt.Name, uncovered))

    Assert.True(
        List.isEmpty violations,
        sprintf
            "Group-class passes missing their `passTags` row (would silently ALWAYS RUN, ignoring the operator's group toggle): %A"
            violations)
