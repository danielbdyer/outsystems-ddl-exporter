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
    /// Pillar 9 (chapter A.4.7 slice α): User FK reflow consumes
    /// operator-supplied User-table replacement specs and reroutes
    /// references to point at the canonical User table. Operator
    /// intent on the Selection axis — the operator selects which
    /// User-table references reroute vs preserve as-is. Lands as
    /// registered overlay. (Refinement candidate at slice γ harvest
    /// analysis: Insertion may fit as well as Selection; pillar-8
    /// four-question analysis at registration time.)
    let private classification : Classification = OperatorIntent Selection

    let private matchedEvent (sourceKey: SsKey) (strategyLabel: string) : LineageEvent =
        { PassName       = passName
          PassVersion    = version
          SsKey          = sourceKey
          TransformKind  = Annotated (Label (System.String.Concat ("userFkReflow.matched-by-", strategyLabel)))  // LINT-ALLOW: terminal diagnostic-label composition at the AnnotationDetail.Label boundary; BCL `String.Concat` is the right primitive for the two-segment audit-narration label
          Classification = classification }

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
                  [ "sourceUserId", sprintf "%d" sourceUserValue ]
          SuggestedConfig = None }

    // -----------------------------------------------------------------------
    // Per-strategy matching (slices δ + ε).
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

    /// Build an SsKey-keyed index from the target population (slice
    /// ε). Mirrors `buildEmailIndex` — duplicate-SsKey collisions
    /// (defensive case; SsKey identity is structurally unique per
    /// A4 but adapter-side bugs could produce duplicates) resolve
    /// first-occurrence-wins via reverse-fold.
    let private buildSsKeyIndex
        (targets: UserPopulation<TargetUserId>)
        : Map<SsKey, TargetUserId> =
        targets.Users
        |> List.map (fun u -> u.SsKey, u.Id)
        |> List.rev
        |> Map.ofList

    /// Outcome of applying a strategy to one source user. Used by
    /// the recursive `applyStrategy` walker so `FallbackToSystemUser`
    /// can compose primary-then-fallback decisions structurally.
    type private MatchOutcome =
        | Matched of target: TargetUserId * strategyLabel: string
        | UnmatchedWith of diagnostic: RemapDiagnostic

    /// Apply one matching strategy to one source user. Per pre-
    /// scope §4 algorithm:
    ///   1. `ByEmail` — look up in target email index;
    ///       produce `Matched (target, "ByEmail")` or
    ///       `UnmatchedWith` (`NoEmail` or `EmailDidNotMatch`).
    ///   2. `BySsKey` — look up in target SsKey index;
    ///       produce `Matched (target, "BySsKey")` or
    ///       `UnmatchedWith (SsKeyDidNotMatch ...)`.
    ///   3. `ManualOverride map` — `Map.tryFind sourceId map`;
    ///       produce `Matched (target, "ManualOverride")` or
    ///       `UnmatchedWith (OverrideMissing source.Id)`.
    ///   4. `FallbackToSystemUser (fallback, primary)` — recurse
    ///       into `primary`; on `Matched` return with
    ///       `"FallbackToSystemUser.primary"` label; on
    ///       `UnmatchedWith _` return `Matched (fallback,
    ///       "FallbackToSystemUser.fallback")` (the safety-net
    ///       catches every miss, structurally guaranteeing
    ///       `Set.isEmpty Unmatched` per pre-scope §3).
    ///
    /// Recursive on `FallbackToSystemUser`'s `primary` arm, so
    /// nested fallback chains compose. Inner-strategy-specific
    /// labels are discarded at the FallbackToSystemUser layer per
    /// the pre-scope's narration shape (`matched-by-
    /// FallbackToSystemUser.primary` / `.fallback`); deeper trail
    /// query lives in slice η emitter integration if a consumer
    /// demands the nested-strategy detail.
    let rec private applyStrategy
        (emailIndex: Lazy<Map<string, TargetUserId>>)
        (ssKeyIndex: Lazy<Map<SsKey, TargetUserId>>)
        (strategy: UserMatchingStrategy)
        (source: UserAttributes<SourceUserId>)
        : MatchOutcome =
        match strategy with
        | ByEmail ->
            match source.Email with
            | None -> UnmatchedWith (NoEmail source.Id)
            | Some e ->
                let key = emailKey e
                match Map.tryFind key emailIndex.Value with
                | Some target -> Matched (target, "ByEmail")
                | None        -> UnmatchedWith (EmailDidNotMatch (source.Id, e))
        | BySsKey ->
            match Map.tryFind source.SsKey ssKeyIndex.Value with
            | Some target -> Matched (target, "BySsKey")
            | None        -> UnmatchedWith (SsKeyDidNotMatch (source.Id, source.SsKey))
        | ManualOverride overrideMap ->
            match Map.tryFind source.Id overrideMap with
            | Some target -> Matched (target, "ManualOverride")
            | None        -> UnmatchedWith (OverrideMissing source.Id)
        | FallbackToSystemUser (fallback, primary) ->
            match applyStrategy emailIndex ssKeyIndex primary source with
            | Matched (target, _)  -> Matched (target, "FallbackToSystemUser.primary")
            | UnmatchedWith _      -> Matched (fallback, "FallbackToSystemUser.fallback")

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
                  Metadata = Map.empty
                  SuggestedConfig = None }
            { Value =
                { Value = UserRemapContext.empty
                  Entries = List.rev (bugEntry :: state.Entries) }
              Trail = List.rev state.Events }

    /// Walk source users sequentially under the strategy walker.
    /// One pass over sources; index builds are `lazy` so a strategy
    /// that doesn't need an index (e.g., pure `ManualOverride`)
    /// pays zero index-construction cost. Each source user produces
    /// either a `Matched` event (lineage Annotated + Mapping entry)
    /// or an `UnmatchedWith` outcome (Unmatched set entry + Warning
    /// diagnostic + RemapDiagnostic).
    let private walkSources
        (emailIndex: Lazy<Map<string, TargetUserId>>)
        (ssKeyIndex: Lazy<Map<SsKey, TargetUserId>>)
        (strategy: UserMatchingStrategy)
        (sources: UserAttributes<SourceUserId> list)
        : State =
        // Per-candidate deferred-FK scan distribution surfaces under
        // `pass.userFkReflow.candidate` — one Bench sample per
        // source user evaluated against the matching strategy
        // (email/ssKey index hits, fallback recursion). The fold
        // accumulator threading is preserved; the scope decorates
        // each iteration body.
        sources
        |> List.fold (fun (state: State) source ->
            use _ = Bench.scope "pass.userFkReflow.candidate"
            match applyStrategy emailIndex ssKeyIndex strategy source with
            | Matched (target, label) ->
                { state with
                    Mapping = Map.add source.Id target state.Mapping
                    Events  = matchedEvent source.SsKey label :: state.Events }
            | UnmatchedWith diagnostic ->
                { state with
                    Unmatched        = Set.add source.Id state.Unmatched
                    RemapDiagnostics = diagnostic :: state.RemapDiagnostics
                    Entries          = unmatchedEntry diagnostic :: state.Entries })
            emptyState

    /// Discover user-FK remap evidence from per-environment user
    /// populations under the supplied matching strategy. Per pre-
    /// scope §4: pure function (no I/O — population shaping
    /// happens in the boundary adapter). Source users iterated in
    /// `SsKey`-sorted order for T1 byte-determinism. Full strategy
    /// DU coverage (slice ε): `ByEmail` / `BySsKey` /
    /// `ManualOverride` / `FallbackToSystemUser` all implemented.
    let discover
        (sourceUsers: UserPopulation<SourceUserId>)
        (targetUsers: UserPopulation<TargetUserId>)
        (strategy: UserMatchingStrategy)
        : Lineage<Diagnostics<UserRemapContext>> =
        use _ = Bench.scope "passes.userFkReflow.discover"
        let sources = orderedSources sourceUsers
        // Indexes are lazy: a pure-`ManualOverride` strategy pays
        // zero cost to build the email/SsKey indexes; a
        // `FallbackToSystemUser (ByEmail, ManualOverride)`
        // strategy builds only the indexes its branches reach.
        let emailIndex = lazy (buildEmailIndex targetUsers)
        let ssKeyIndex = lazy (buildSsKeyIndex targetUsers)
        let state = walkSources emailIndex ssKeyIndex strategy sources
        projectState state

    /// Pass entry point per the canonical signature (`Catalog ×
    /// Policy × Profile → Lineage<Diagnostics<'output>>`). Reads
    /// `Profile.SourceUsers` + `Profile.TargetUsers` (slice β) and
    /// `Policy.UserMatching` (slice α); produces
    /// `UserRemapContext` (slice γ).
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `UserFkReflowPass.registered.Run`
    let private run
        (_catalog: Catalog)
        (policy: Policy)
        (profile: Profile)
        : Lineage<Diagnostics<UserRemapContext>> =
        use _ = Bench.scope "passes.userFkReflow"
        discover profile.SourceUsers profile.TargetUsers policy.UserMatching

    /// Chapter A.4.7 slice γ — factory. Captures operator-supplied
    /// `Policy` (`UserMatching` axis) + `Profile`
    /// (`SourceUsers` / `TargetUsers` evidence) in closure. Single
    /// `OperatorIntent Selection` site — operator selects which
    /// User-table references reroute via the matching strategies +
    /// source/target user populations. Output is `UserRemapContext`
    /// (not Catalog) — this is a decision-producing pass; downstream
    /// consumers apply the remap.
    let registered (policy: Policy) (profile: Profile) : RegisteredTransform<Catalog, UserRemapContext> =
        { Name = passName
          Domain = Identity
          StageBinding = Pass
          Sites =
            [ { SiteName = "reflow"
                Classification = classification
                Rationale = "Reroute User-table references via operator-supplied matching strategies (Policy.UserMatching) + source/target user populations (Profile). Lands as Selection-axis overlay; Insertion was considered alternative classification, but re-direction reads more naturally as Selection (which references reroute)." } ]
          Run = fun c -> run c policy profile
          Status = Active }
