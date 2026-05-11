namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver diagnostic message construction uses
// `sprintf "%d"` to interpolate typed user-id integers into operator-
// facing Diagnostic.Message text. Same allowed-exception class as
// `Catalog.create` / `Module.create` smart-constructor validation
// errors (per `Catalog.fs` LINT-ALLOW-FILE block). Strategy-label
// composition uses `String.concat ""` at the terminal diagnostic
// projection boundary — the typed `UserMatchingStrategy` DU IS the
// structure being projected; the label string surfaces only in the
// `AnnotationDetail.Label` payload (free-form audit narration).

open Projection.Core

/// Π's-sibling discovery pass — chapter 4.2 slice δ
/// (`CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §4). Walks the source user
/// population, applies `Policy.UserMatching` against the target user
/// population, produces `UserRemapContext` (the typed remap evidence
/// emitter siblings consume at slice η to rewrite User-FK column
/// values).
///
/// **Pass shape** (per `CHAPTER_3_1_CLOSE.md` writer-fidelity
/// codification + pre-scope §6): returns
/// `Lineage<Diagnostics<UserRemapContext>>` — the canonical shape
/// for passes producing decisions + observer-relevant findings. One
/// `Annotated` lineage event per matched user; one `Warning`
/// diagnostic per unmatched user.
///
/// **Slice δ scope** (ByEmail only): the discovery pass dispatches
/// on `Policy.UserMatching`'s DU. `ByEmail` is implemented; the
/// other three variants (`BySsKey`, `ManualOverride`,
/// `FallbackToSystemUser`) emit a deferred-strategy `Error`
/// diagnostic at slice δ and treat every source user as unmatched.
/// Slice ε retires the deferred-strategy emission with real per-
/// variant implementations.
[<RequireQualifiedAccess>]
module UserFkReflowPass =

    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "userFkReflow"

    // -----------------------------------------------------------------------
    // Lineage + Diagnostics primitives.
    // -----------------------------------------------------------------------

    /// One `Annotated` event per matched source user. Per pre-scope
    /// §6: `TransformKind = Annotated "matched-by-<strategy>"`. The
    /// strategy label is a stable diagnostic narration (`"ByEmail"` /
    /// `"BySsKey"` / `"ManualOverride"` / `"FallbackToSystemUser.primary"`
    /// / `"FallbackToSystemUser.fallback"`) — `AnnotationDetail.Label`
    /// is the typed-payload variant designated for production passes
    /// whose typed shape hasn't yet been earned (per the discipline
    /// in `Lineage.fs:130`). Slice η consumers reading the trail can
    /// answer "which strategy resolved this user's identity?" via
    /// the label.
    let private matchedEvent (sourceKey: SsKey) (strategyLabel: string) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = sourceKey
          TransformKind = Annotated (Label (System.String.Concat ("userFkReflow.matched-by-", strategyLabel))) }  // LINT-ALLOW: terminal diagnostic-label composition at the AnnotationDetail.Label boundary; BCL `String.Concat` is the right primitive for the two-segment audit-narration label

    /// One `Warning` diagnostic per unmatched source user. Per
    /// pre-scope §6: `Source = "userFkReflow"`, `Code = "userFkReflow.
    /// <reason>"`. Each unmatched user maps to exactly one entry via
    /// `unmatchedEntry`; the variant on `RemapDiagnostic` names the
    /// reason structurally for downstream consumers.
    let private unmatchedEntry (diagnostic: RemapDiagnostic) : DiagnosticEntry =
        let (sourceUserValue, code, messageDetail) =
            match diagnostic with
            | NoEmail source ->
                SourceUserId.value source,
                "userFkReflow.noEmail",
                "source user has no email registered (ByEmail strategy)"
            | EmailDidNotMatch (source, email) ->
                SourceUserId.value source,
                "userFkReflow.emailDidNotMatch",
                sprintf "source user email '%s' did not match any target user (ByEmail strategy)" (Email.value email)
            | SsKeyDidNotMatch (source, _) ->
                SourceUserId.value source,
                "userFkReflow.ssKeyDidNotMatch",
                "source user SsKey did not match any target user (BySsKey strategy)"
            | OverrideMissing source ->
                SourceUserId.value source,
                "userFkReflow.overrideMissing",
                "source user not present in ManualOverride map"
            | NoFallbackConfigured source ->
                SourceUserId.value source,
                "userFkReflow.noFallbackConfigured",
                "no FallbackToSystemUser configured; primary strategy exhausted"
        { Source   = passName
          Severity = DiagnosticSeverity.Warning
          Code     = code
          Message  = sprintf "source user %d: %s" sourceUserValue messageDetail
          SsKey    = None
          Metadata =
              Map.ofList
                  [ "sourceUserId", sprintf "%d" sourceUserValue ] }

    /// Deferred-strategy diagnostic — emitted at slice δ for the
    /// three strategy variants whose implementation lands at slice
    /// ε. Per total-decisions discipline + the strategy-layer
    /// codification: "no decision" is a named entry rather than
    /// silence. The diagnostic surfaces explicitly so callers can
    /// route the deferred case to operator review.
    let private deferredStrategyEntry (label: string) : DiagnosticEntry =
        { Source   = passName
          Severity = DiagnosticSeverity.Error
          Code     = "userFkReflow.strategyNotYetImplemented"
          Message  =
              System.String.Concat (  // LINT-ALLOW: terminal Diagnostic.Message composition at the operator-narration boundary; `String.Concat` joins the strategy-label segment to the static narration; same architectural shape as the matchedEvent label composition above
                  "UserMatchingStrategy variant '", label,
                  "' is not implemented at slice δ; routing every source user as unmatched. ",
                  "Slice ε (CHAPTER_4_PRESCOPE_USERFK_REFLOW §7) lands the implementation.")
          SsKey    = None
          Metadata = Map.ofList [ "strategyLabel", label ] }

    // -----------------------------------------------------------------------
    // ByEmail strategy.
    // -----------------------------------------------------------------------

    /// Case-insensitive email matching key. Per pre-scope §3 +
    /// V1 parity (`UserMatchingEngine.cs:84-86, 97`): match by
    /// `OrdinalIgnoreCase`. `Email.create` normalizes via `Trim()`
    /// at construction; we lowercase the trimmed value here to
    /// produce the matching key. The Email value itself preserves
    /// case for downstream display + audit-trail consistency.
    let private emailKey (email: Email) : string =
        (Email.value email).ToLowerInvariant()

    /// Build an email-keyed index from the target population once
    /// per `discover` call. Per pre-scope §11 (performance risk):
    /// "the pass must build a dictionary-keyed index once per
    /// discover-call, not per source user." Two users with the
    /// same email in the target population is the V1-acknowledged
    /// collision case (`UserMatchingEngine.cs BuildLookup` uses
    /// `List<UserIdentifier>` per email key, accepting collisions);
    /// V2 takes first-match-by-iteration-order (Map.add over
    /// reverse iteration so the first user wins). A future variant
    /// surfacing collision-warning diagnostics earns its place
    /// when V1-canary-parity surfaces the gap.
    let private buildEmailIndex
        (targets: UserPopulation<TargetUserId>)
        : Map<string, TargetUserId> =
        targets.Users
        |> List.choose (fun u ->
            u.Email |> Option.map (fun e -> emailKey e, u.Id))
        // Reverse-fold so the first-occurrence wins on duplicate-
        // email collision (per pre-scope §11 risk discussion).
        |> List.rev
        |> Map.ofList

    /// Apply `ByEmail` matching to one source user. Per pre-scope
    /// §4 algorithm step 1: look up in the target population's
    /// email index; produce `Ok target` or fail with
    /// `EmailDidNotMatch` (or `NoEmail` if source has no email).
    let private matchByEmail
        (emailIndex: Map<string, TargetUserId>)
        (source: UserAttributes<SourceUserId>)
        : Result<TargetUserId, RemapDiagnostic> =
        match source.Email with
        | None -> Error (NoEmail source.Id)
        | Some e ->
            let key = emailKey e
            match Map.tryFind key emailIndex with
            | Some target -> Ok target
            | None        -> Error (EmailDidNotMatch (source.Id, e))

    // -----------------------------------------------------------------------
    // Pass entry points.
    // -----------------------------------------------------------------------

    /// Source-user ordering for T1 byte-determinism. Sort by
    /// `SsKey` (per the determinism-is-constructed discipline);
    /// the iteration order of `sourceUsers.Users` is the
    /// operator-supplied list order, which can vary across runs.
    let private orderedSources
        (sourceUsers: UserPopulation<SourceUserId>)
        : UserAttributes<SourceUserId> list =
        sourceUsers.Users |> List.sortBy (fun u -> u.SsKey)

    /// Accumulator state. Builds up the mapping, unmatched set,
    /// per-source diagnostic list, lineage events list, and
    /// diagnostics-channel entries during the source-walk.
    type private State =
        {
            Mapping        : Map<SourceUserId, TargetUserId>
            Unmatched      : Set<SourceUserId>
            RemapDiagnostics : RemapDiagnostic list
            Events         : LineageEvent list
            Entries        : DiagnosticEntry list
        }

    let private emptyState : State =
        { Mapping = Map.empty
          Unmatched = Set.empty
          RemapDiagnostics = []
          Events = []
          Entries = [] }

    /// Project the accumulator state into the pass's typed return
    /// shape. `UserRemapContext.create` validates the disjointness
    /// invariant per slice γ; the algorithm-internal invariant
    /// (each source user is added to AT MOST ONE of `Mapping` /
    /// `Unmatched`) ensures the smart constructor succeeds.
    let private projectState (state: State) : Lineage<Diagnostics<UserRemapContext>> =
        match UserRemapContext.create state.Mapping state.Unmatched (List.rev state.RemapDiagnostics) with
        | Ok ctx ->
            { Value = { Value = ctx; Entries = List.rev state.Entries }
              Trail = List.rev state.Events }
        | Error _ ->
            // Defensive: the algorithm enforces disjointness by
            // construction (each source user is added to AT MOST
            // ONE of Mapping/Unmatched per the single-pass walk).
            // If we ever reach here, it's a pass-internal bug;
            // surface as an Error diagnostic and return an empty
            // UserRemapContext.
            let bugEntry =
                { Source   = passName
                  Severity = DiagnosticSeverity.Error
                  Code     = "userFkReflow.disjointnessViolated"
                  Message  = "pass-internal bug: UserRemapContext disjointness invariant violated"
                  SsKey    = None
                  Metadata = Map.empty }
            { Value =
                { Value = UserRemapContext.empty
                  Entries = List.rev (bugEntry :: state.Entries) }
              Trail = List.rev state.Events }

    /// Walk source users sequentially under a per-source matching
    /// function. The matcher returns `Ok target` for a match (with
    /// the strategy label for the lineage annotation) or `Error
    /// diagnostic` for a miss. Used by slice δ's ByEmail and (slice
    /// ε) by the other strategy implementations.
    let private walkSources
        (sources: UserAttributes<SourceUserId> list)
        (strategyLabel: string)
        (matchOne: UserAttributes<SourceUserId> -> Result<TargetUserId, RemapDiagnostic>)
        (initialState: State)
        : State =
        sources
        |> List.fold (fun (state: State) source ->
            match matchOne source with
            | Ok target ->
                { state with
                    Mapping = Map.add source.Id target state.Mapping
                    Events  = matchedEvent source.SsKey strategyLabel :: state.Events }
            | Error diagnostic ->
                { state with
                    Unmatched        = Set.add source.Id state.Unmatched
                    RemapDiagnostics = diagnostic :: state.RemapDiagnostics
                    Entries          = unmatchedEntry diagnostic :: state.Entries })
            initialState

    /// Treat every source user as unmatched + emit one shared
    /// deferred-strategy diagnostic. Used by slice δ for the three
    /// strategy variants whose implementation lands at slice ε
    /// (`BySsKey` / `ManualOverride` / `FallbackToSystemUser`).
    let private allUnmatched
        (sources: UserAttributes<SourceUserId> list)
        (strategyLabel: string)
        : State =
        let deferredEntry = deferredStrategyEntry strategyLabel
        sources
        |> List.fold (fun (state: State) source ->
            { state with
                Unmatched        = Set.add source.Id state.Unmatched
                RemapDiagnostics = NoFallbackConfigured source.Id :: state.RemapDiagnostics
                Entries          = unmatchedEntry (NoFallbackConfigured source.Id) :: state.Entries })
            { emptyState with Entries = [ deferredEntry ] }

    /// Discover user-FK remap evidence from per-environment user
    /// populations under the supplied matching strategy. Per pre-
    /// scope §4: pure function (no I/O — population shaping
    /// happens in the boundary adapter). Source users iterated in
    /// `SsKey`-sorted order for T1 byte-determinism.
    let discover
        (sourceUsers: UserPopulation<SourceUserId>)
        (targetUsers: UserPopulation<TargetUserId>)
        (strategy: UserMatchingStrategy)
        : Lineage<Diagnostics<UserRemapContext>> =
        use _ = Bench.scope "passes.userFkReflow.discover"
        let sources = orderedSources sourceUsers
        let state =
            match strategy with
            | ByEmail ->
                use _ = Bench.scope "userFkReflow.byEmail"
                let emailIndex = buildEmailIndex targetUsers
                walkSources sources "ByEmail" (matchByEmail emailIndex) emptyState
            | BySsKey                  -> allUnmatched sources "BySsKey"
            | ManualOverride _         -> allUnmatched sources "ManualOverride"
            | FallbackToSystemUser _   -> allUnmatched sources "FallbackToSystemUser"
        projectState state

    /// Pass entry point per the canonical signature (`Catalog ×
    /// Policy × Profile → Lineage<Diagnostics<'output>>`). Reads
    /// `Profile.SourceUsers` + `Profile.TargetUsers` (slice β) and
    /// `Policy.UserMatching` (slice α); produces
    /// `UserRemapContext` (slice γ).
    let run
        (_catalog: Catalog)
        (policy: Policy)
        (profile: Profile)
        : Lineage<Diagnostics<UserRemapContext>> =
        use _ = Bench.scope "passes.userFkReflow"
        discover profile.SourceUsers profile.TargetUsers policy.UserMatching
