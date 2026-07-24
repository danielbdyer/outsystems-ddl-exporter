namespace Projection.Core

/// The **generic pure kernel** of bridge retargeting — the readiness engine that
/// decides whether a foreign key can safely be retargeted from resolving through
/// its original parent to resolving through a *bridge* attribute (a named unique
/// column on a second table), WITHOUT rewriting a single child FK cell value.
///
/// **What "bridge retargeting" is.** A child reference today targets a parent's
/// primary key. An operator wants the same child FK *value* to instead resolve
/// through a bridge table's named-unique key — the FK constraint's target moves,
/// the stored value does not. The success invariant is exact: every retargeted
/// value `v` is either NULL (nothing to resolve), or it resolves to EXACTLY one
/// source row AND EXACTLY one bridge row. Anything else — a value with no bridge
/// row, an ambiguous bridge match, a non-unique bridge key — is a named refusal,
/// never a silent retarget.
///
/// **Why this file is pure and evidence-shaped (the load-bearing boundary).**
/// The engine decides over a SUPPLIED `BridgeRetargetProfile` — the aggregated
/// evidence (counts, resolvability, type-match, orphan/conflict tallies) that a
/// binder gathers from the catalog and from live profiling. The kernel never
/// touches a live table: it is a TOTAL function `profile → decision`, so it is
/// unit-testable against synthetic profiles and cannot depend on any specific
/// estate. The proprietary specifics (which tables, which bridge, where the
/// identity evidence comes from) enter later, at the config + injected-evidence
/// layer — never here. This is the "generic base; specialize at config/DI"
/// split: this module is the base.
///
/// **Three independent verdicts.** Readiness to *retarget the FK*, readiness of
/// the *bridge rows* themselves, and readiness to *sync payload* are separate
/// questions — an operator can be clear to retarget while bridge-row supply or
/// payload reconciliation is not yet ready. Each verdict aggregates only the
/// checks that inform it (`BridgeCheck.verdictsOf`), so a payload-conflict
/// warning never blocks a retarget and a bridge-key block never masquerades as a
/// payload problem.
///
/// The config-shaped declaration (`from`/`to`/`scope`/`qualityControls`/
/// `missingRows`) and the bridge-row supplement live in the slices that consume
/// them (the config binder and the supplement lane); this file carries only the
/// evidence, the check taxonomy, and the decision — the vocabulary the whole
/// feature is proven against.

/// The consequence weight of a quality-control check. `Block` checks gate a
/// readiness verdict to `Blocked` when they fail; `Warn` checks only downgrade a
/// verdict to `ReadyWithWarnings`. The severity is INTRINSIC to the check (a
/// property of what it protects), not operator-configurable — the operator's
/// only lever is scope + which retargets to declare, never whether a broken
/// invariant is "just a warning."
[<RequireQualifiedAccess>]
type BridgeCheckSeverity =
    | Block
    | Warn

/// The closed taxonomy of bridge-retarget quality-control checks — the exact
/// vocabulary the operator reads in a readiness report, so each case name is a
/// stable diagnostic code (`BridgeCheck.name`). Blocks protect the success
/// invariant and the constraint; warnings surface hazards that do not, on their
/// own, make the retarget unsafe. The set is closed: a new check is added here
/// (and the three total matches below fail to compile until it is classified),
/// never invented ad hoc at a call site.
[<RequireQualifiedAccess>]
type BridgeCheck =
    // ---- Blocks: the retarget is unsafe unless every one of these holds. ----
    /// The bridge key is present as usable evidence (the column exists and the
    /// bridge table carries rows to resolve through).
    | BridgeKeyExists
    /// The bridge key is unique — a value resolves to at most one bridge row.
    /// Non-uniqueness breaks the "exactly one bridge row" half of the invariant.
    | BridgeKeyUnique
    /// The bridge key is non-null — a NULL bridge key cannot be a resolution
    /// target for any non-null child value.
    | BridgeKeyNonNull
    /// The source key and the bridge key share a compatible SQL type — a
    /// type-mismatched retarget would compare across an implicit conversion.
    | SourceAndBridgeKeyTypesMatch
    /// The retarget targets a NAMED UNIQUE bridge attribute, not the bridge's
    /// primary key. Fires (fails) when the retarget would land on the bridge PK
    /// — the whole point is to resolve through a business key, not the surrogate.
    | RetargetWouldUseBridgePrimaryKey
    /// Every in-scope non-null source value resolves through the bridge (the
    /// success invariant's coverage half). Fails when any value has no bridge
    /// row or an ambiguous one.
    | AllSourceValuesResolveThroughBridge
    /// After retargeting, source values still resolve to the ORIGINAL parent —
    /// the retarget augments, it does not sever the existing relationship.
    | SourceValuesStillResolveToOriginalParent
    /// No existing retarget already claims this reference (a conflicting prior
    /// bridge retarget on the same FK).
    | ExistingRetargetConflict
    /// A TRUSTED constraint is achievable for the retargeted FK — the retarget
    /// can land as a checked (trusted) constraint, not a permanent `WITH NOCHECK`.
    | TrustedConstraintPossible
    // ---- Warnings: surfaced, never blocking. ----
    /// Some bridge rows have no corresponding source row (orphaned bridge rows).
    | BridgeRowsOrphanedFromSource
    /// Bridge and source disagree on a shared payload column (a sync hazard).
    | BridgePayloadConflicts
    /// The identity evidence backing an auto-supplemented bridge row is missing.
    | IdentityEvidenceMissing
    /// The identity evidence backing an auto-supplemented bridge row is
    /// ambiguous (more than one candidate).
    | IdentityEvidenceAmbiguous
    /// References other than audit-style (`CreatedBy`/`UpdatedBy`-class) are
    /// included in the retarget — a broader blast radius than an audit retarget.
    | NonAuditReferencesIncluded
    /// An existing constraint on the reference is untrusted (`WITH NOCHECK`).
    | ExistingConstraintUntrusted

/// Which of the three readiness verdicts a check informs. A check may inform
/// more than one (bridge-key presence bears on both the retarget and the bridge
/// rows).
[<RequireQualifiedAccess>]
type BridgeVerdictKind =
    | Retargeting
    | BridgeRows
    | PayloadSync

[<RequireQualifiedAccess>]
module BridgeCheck =

    /// Every check, in report order (blocks first, then warnings). The single
    /// enumeration `evaluate` folds over — kept exhaustive by the compiler via
    /// the total matches below.
    let all : BridgeCheck list =
        [ BridgeCheck.BridgeKeyExists
          BridgeCheck.BridgeKeyUnique
          BridgeCheck.BridgeKeyNonNull
          BridgeCheck.SourceAndBridgeKeyTypesMatch
          BridgeCheck.RetargetWouldUseBridgePrimaryKey
          BridgeCheck.AllSourceValuesResolveThroughBridge
          BridgeCheck.SourceValuesStillResolveToOriginalParent
          BridgeCheck.ExistingRetargetConflict
          BridgeCheck.TrustedConstraintPossible
          BridgeCheck.BridgeRowsOrphanedFromSource
          BridgeCheck.BridgePayloadConflicts
          BridgeCheck.IdentityEvidenceMissing
          BridgeCheck.IdentityEvidenceAmbiguous
          BridgeCheck.NonAuditReferencesIncluded
          BridgeCheck.ExistingConstraintUntrusted ]

    /// The stable diagnostic code for a check — the camelCase name the operator
    /// reads in a readiness report and names in `qualityControls`.
    let name (check: BridgeCheck) : string =
        match check with
        | BridgeCheck.BridgeKeyExists                           -> "bridgeKeyExists"
        | BridgeCheck.BridgeKeyUnique                           -> "bridgeKeyUnique"
        | BridgeCheck.BridgeKeyNonNull                          -> "bridgeKeyNonNull"
        | BridgeCheck.SourceAndBridgeKeyTypesMatch              -> "sourceAndBridgeKeyTypesMatch"
        | BridgeCheck.RetargetWouldUseBridgePrimaryKey          -> "retargetWouldUseBridgePrimaryKey"
        | BridgeCheck.AllSourceValuesResolveThroughBridge       -> "allSourceValuesResolveThroughBridge"
        | BridgeCheck.SourceValuesStillResolveToOriginalParent  -> "sourceValuesStillResolveToOriginalParent"
        | BridgeCheck.ExistingRetargetConflict                  -> "existingRetargetConflict"
        | BridgeCheck.TrustedConstraintPossible                 -> "trustedConstraintPossible"
        | BridgeCheck.BridgeRowsOrphanedFromSource              -> "bridgeRowsOrphanedFromSource"
        | BridgeCheck.BridgePayloadConflicts                    -> "bridgePayloadConflicts"
        | BridgeCheck.IdentityEvidenceMissing                   -> "identityEvidenceMissing"
        | BridgeCheck.IdentityEvidenceAmbiguous                 -> "identityEvidenceAmbiguous"
        | BridgeCheck.NonAuditReferencesIncluded                -> "nonAuditReferencesIncluded"
        | BridgeCheck.ExistingConstraintUntrusted               -> "existingConstraintUntrusted"

    /// The check's intrinsic severity.
    let severity (check: BridgeCheck) : BridgeCheckSeverity =
        match check with
        | BridgeCheck.BridgeKeyExists
        | BridgeCheck.BridgeKeyUnique
        | BridgeCheck.BridgeKeyNonNull
        | BridgeCheck.SourceAndBridgeKeyTypesMatch
        | BridgeCheck.RetargetWouldUseBridgePrimaryKey
        | BridgeCheck.AllSourceValuesResolveThroughBridge
        | BridgeCheck.SourceValuesStillResolveToOriginalParent
        | BridgeCheck.ExistingRetargetConflict
        | BridgeCheck.TrustedConstraintPossible                 -> BridgeCheckSeverity.Block
        | BridgeCheck.BridgeRowsOrphanedFromSource
        | BridgeCheck.BridgePayloadConflicts
        | BridgeCheck.IdentityEvidenceMissing
        | BridgeCheck.IdentityEvidenceAmbiguous
        | BridgeCheck.NonAuditReferencesIncluded
        | BridgeCheck.ExistingConstraintUntrusted               -> BridgeCheckSeverity.Warn

    /// The verdicts a check informs. `Retargeting` is the readiness to flip the
    /// FK target; `BridgeRows` is the readiness of the bridge-row supply;
    /// `PayloadSync` is the readiness to reconcile shared payload columns.
    let verdictsOf (check: BridgeCheck) : Set<BridgeVerdictKind> =
        let ofList xs = Set.ofList xs
        match check with
        | BridgeCheck.BridgeKeyExists ->
            ofList [ BridgeVerdictKind.Retargeting; BridgeVerdictKind.BridgeRows ]
        | BridgeCheck.BridgeKeyUnique
        | BridgeCheck.BridgeKeyNonNull
        | BridgeCheck.SourceAndBridgeKeyTypesMatch
        | BridgeCheck.RetargetWouldUseBridgePrimaryKey
        | BridgeCheck.AllSourceValuesResolveThroughBridge
        | BridgeCheck.SourceValuesStillResolveToOriginalParent
        | BridgeCheck.ExistingRetargetConflict
        | BridgeCheck.TrustedConstraintPossible
        | BridgeCheck.NonAuditReferencesIncluded
        | BridgeCheck.ExistingConstraintUntrusted ->
            ofList [ BridgeVerdictKind.Retargeting ]
        | BridgeCheck.BridgeRowsOrphanedFromSource
        | BridgeCheck.IdentityEvidenceMissing
        | BridgeCheck.IdentityEvidenceAmbiguous ->
            ofList [ BridgeVerdictKind.BridgeRows ]
        | BridgeCheck.BridgePayloadConflicts ->
            ofList [ BridgeVerdictKind.PayloadSync ]

/// The provenance of a bridge row's identity evidence — the anchor the
/// auto-supplement stands on (an object id / a single UPN / a single mail).
/// Anything but `Present` is a warning, never a block: an operator can retarget
/// against bridge rows whose identity evidence is thin, but the report says so.
[<RequireQualifiedAccess>]
type BridgeIdentityEvidence =
    | Present
    | Missing
    | Ambiguous

/// The aggregated evidence a bridge-retarget decision is taken over — gathered
/// by a binder from the catalog (resolvability, declared uniqueness/nullability,
/// type match, constraint state) and from live profiling (resolution coverage,
/// orphan/conflict tallies, identity evidence). The kernel treats every field
/// as an already-observed fact; it never asks WHERE a fact came from. A `clean`
/// profile (nothing wrong) yields every verdict `Ready`.
type BridgeRetargetProfile =
    { /// The declared retarget this profile is evidence for (echoed onto the
      /// decision so a caller can correlate without threading context).
      RetargetId : string
      /// `BridgeKeyExists`: the bridge key is usable evidence.
      BridgeKeyPresent : bool
      /// `BridgeKeyUnique`: count of bridge keys occurring on more than one row.
      /// Zero ⇒ unique.
      BridgeKeyDuplicateCount : int64
      /// `BridgeKeyNonNull`: count of bridge rows whose key cell is NULL. Zero ⇒
      /// non-null.
      BridgeKeyNullCount : int64
      /// `SourceAndBridgeKeyTypesMatch`: the source and bridge key SQL types are
      /// compatible.
      KeyTypesMatch : bool
      /// `RetargetWouldUseBridgePrimaryKey`: TRUE when the retarget would land on
      /// the bridge's primary key rather than a named unique attribute (the
      /// hazard the check fires on).
      TargetsBridgePrimaryKey : bool
      /// `AllSourceValuesResolveThroughBridge`: count of in-scope non-null source
      /// values that do NOT resolve to exactly one bridge row. Zero ⇒ full
      /// coverage.
      UnresolvedThroughBridgeCount : int64
      /// `SourceValuesStillResolveToOriginalParent`: count of source values that
      /// would no longer resolve to the original parent after retargeting. Zero ⇒
      /// the original relationship is preserved.
      BrokenOriginalParentCount : int64
      /// `ExistingRetargetConflict`: TRUE when a prior retarget already claims
      /// this reference.
      ExistingRetargetConflict : bool
      /// `TrustedConstraintPossible`: a trusted (checked) constraint is
      /// achievable for the retargeted FK.
      TrustedConstraintPossible : bool
      /// `BridgeRowsOrphanedFromSource`: count of bridge rows with no source row.
      OrphanedBridgeRowCount : int64
      /// `BridgePayloadConflicts`: count of shared payload columns where bridge
      /// and source disagree.
      PayloadConflictCount : int64
      /// `IdentityEvidenceMissing` / `IdentityEvidenceAmbiguous`: the provenance
      /// of the bridge-row identity evidence.
      IdentityEvidence : BridgeIdentityEvidence
      /// `NonAuditReferencesIncluded`: TRUE when the retarget scope is limited to
      /// audit-style references (`CreatedBy`/`UpdatedBy`-class). FALSE fires the
      /// warning.
      OnlyAuditReferences : bool
      /// `ExistingConstraintUntrusted`: `None` = no existing constraint on the
      /// reference; `Some true` = a trusted existing constraint; `Some false` = an
      /// existing `WITH NOCHECK` (untrusted) constraint (the warning fires). Maps
      /// off the reference's `ConstraintState`.
      ExistingConstraintTrusted : bool option }

[<RequireQualifiedAccess>]
module BridgeRetargetProfile =

    /// The all-clear profile for a retarget — every block holds, no warning
    /// fires. The observable identity of the engine: `evaluate (clean id)` has
    /// all three verdicts `Ready`. Tests perturb one field at a time from here.
    let clean (retargetId: string) : BridgeRetargetProfile =
        { RetargetId                   = retargetId
          BridgeKeyPresent             = true
          BridgeKeyDuplicateCount      = 0L
          BridgeKeyNullCount           = 0L
          KeyTypesMatch                = true
          TargetsBridgePrimaryKey      = false
          UnresolvedThroughBridgeCount = 0L
          BrokenOriginalParentCount    = 0L
          ExistingRetargetConflict     = false
          TrustedConstraintPossible    = true
          OrphanedBridgeRowCount       = 0L
          PayloadConflictCount         = 0L
          IdentityEvidence             = BridgeIdentityEvidence.Present
          OnlyAuditReferences          = true
          ExistingConstraintTrusted    = None }

    /// The FAIL-CLOSED default a binder starts from before any profiling evidence
    /// is gathered: every DATA-derived check is unproven, so the retarget is
    /// BLOCKED — nothing is asserted to hold. A binder overrides the
    /// catalog-derivable facts it CAN compute (key presence, declared
    /// uniqueness/nullability, type match, targets-bridge-PK, constraint trust);
    /// the data facts (coverage, broken-parent, orphans, conflicts, identity) stay
    /// fail-closed until live evidence supplies them. So a configured retarget with
    /// no evidence NEVER clears — `RetargetFk` stays empty and emission is
    /// byte-identical. Evidence (a later slice) is what lets a retarget clear.
    let unproven (retargetId: string) : BridgeRetargetProfile =
        { RetargetId                   = retargetId
          BridgeKeyPresent             = false
          BridgeKeyDuplicateCount      = 1L
          BridgeKeyNullCount           = 1L
          KeyTypesMatch                = false
          TargetsBridgePrimaryKey      = false
          UnresolvedThroughBridgeCount = 1L
          BrokenOriginalParentCount    = 1L
          ExistingRetargetConflict     = false
          TrustedConstraintPossible    = false
          OrphanedBridgeRowCount       = 0L
          PayloadConflictCount         = 0L
          IdentityEvidence             = BridgeIdentityEvidence.Missing
          OnlyAuditReferences          = true
          ExistingConstraintTrusted    = None }

/// The evaluated outcome of one check: whether it held over the profile, its
/// intrinsic severity (echoed so a report needs no second lookup), and a factual
/// one-line detail (the observed evidence). Prose polish is the Voice layer's
/// job; `Detail` here is a bare factual anchor.
type BridgeCheckResult =
    { Check    : BridgeCheck
      Severity : BridgeCheckSeverity
      Passed   : bool
      Detail   : string }

/// A single readiness verdict — `Ready`, `ReadyWithWarnings` (only `Warn` checks
/// failed, listed), or `Blocked` (at least one `Block` check failed, listed).
/// The lists carry the offending checks so a caller renders the reason without
/// re-deriving it.
[<RequireQualifiedAccess>]
type BridgeReadiness =
    | Ready
    | ReadyWithWarnings of warnings: BridgeCheck list
    | Blocked of blocks: BridgeCheck list

/// The engine's verdict on one retarget: the full check ledger plus the three
/// independent readiness verdicts, each aggregated only from the checks that
/// inform it.
type BridgeRetargetDecision =
    { RetargetId  : string
      Checks      : BridgeCheckResult list
      /// Readiness to flip the FK constraint's target.
      Retargeting : BridgeReadiness
      /// Readiness of the bridge-row supply.
      BridgeRows  : BridgeReadiness
      /// Readiness to reconcile shared payload columns.
      PayloadSync : BridgeReadiness }

/// A RESOLVED bridge retarget — the binder's output, ready for the decision pass.
/// `ReferenceKey` is the FK to retarget (resolved from the operator's
/// module/entity/relationship); `BridgeAttributeKey` is the target attribute on
/// the bridge kind (resolved from the operator's module/entity/attribute); the
/// `Profile` bundles the assembled evidence the pass evaluates. When the
/// retarget's `Retargeting` verdict clears, the pass emits
/// `ReferenceKey → BridgeAttributeKey` into `ComposeState.BridgeRetargets`.
type BridgeRetargetPlan =
    { ReferenceKey       : SsKey
      BridgeAttributeKey : SsKey
      Profile            : BridgeRetargetProfile }

/// The bridge-retarget policy axis — the resolved plans the operator declared,
/// carried on `Policy` and read by the decision pass. `empty` (no plans) is the
/// observable identity: the pass writes an empty retarget map, byte-identical.
type BridgeRetargetPolicy =
    { Plans : BridgeRetargetPlan list }

[<RequireQualifiedAccess>]
module BridgeRetargetPolicy =

    let empty : BridgeRetargetPolicy = { Plans = [] }

[<RequireQualifiedAccess>]
module BridgeRetarget =

    let private plural (n: int64) (one: string) (many: string) : string =
        if n = 1L then System.String.Concat("1 ", one)  // LINT-ALLOW: terminal factual count rendering for a diagnostic detail; not SQL/JSON
        else System.String.Concat(string n, " ", many)  // LINT-ALLOW: terminal factual count rendering for a diagnostic detail; not SQL/JSON

    /// Evaluate one check against the profile — returns whether it held and a
    /// factual detail. A block PASSES when its protected condition holds; a
    /// warning PASSES when its hazard is absent.
    let private checkResult (profile: BridgeRetargetProfile) (check: BridgeCheck) : BridgeCheckResult =
        let passed, detail =
            match check with
            | BridgeCheck.BridgeKeyExists ->
                profile.BridgeKeyPresent,
                (if profile.BridgeKeyPresent then "bridge key present" else "bridge key absent — no evidence to resolve through")
            | BridgeCheck.BridgeKeyUnique ->
                profile.BridgeKeyDuplicateCount = 0L,
                (if profile.BridgeKeyDuplicateCount = 0L then "bridge key unique"
                 else System.String.Concat(plural profile.BridgeKeyDuplicateCount "duplicated bridge key" "duplicated bridge keys", " — resolution would be ambiguous"))
            | BridgeCheck.BridgeKeyNonNull ->
                profile.BridgeKeyNullCount = 0L,
                (if profile.BridgeKeyNullCount = 0L then "bridge key non-null"
                 else System.String.Concat(plural profile.BridgeKeyNullCount "bridge row" "bridge rows", " carry a NULL key"))
            | BridgeCheck.SourceAndBridgeKeyTypesMatch ->
                profile.KeyTypesMatch,
                (if profile.KeyTypesMatch then "source and bridge key types match" else "source and bridge key types differ")
            | BridgeCheck.RetargetWouldUseBridgePrimaryKey ->
                not profile.TargetsBridgePrimaryKey,
                (if profile.TargetsBridgePrimaryKey then "retarget would land on the bridge primary key, not a named unique key" else "retarget targets a named unique bridge key")
            | BridgeCheck.AllSourceValuesResolveThroughBridge ->
                profile.UnresolvedThroughBridgeCount = 0L,
                (if profile.UnresolvedThroughBridgeCount = 0L then "every in-scope value resolves through the bridge"
                 else System.String.Concat(plural profile.UnresolvedThroughBridgeCount "in-scope value" "in-scope values", " do not resolve to exactly one bridge row"))
            | BridgeCheck.SourceValuesStillResolveToOriginalParent ->
                profile.BrokenOriginalParentCount = 0L,
                (if profile.BrokenOriginalParentCount = 0L then "original parent resolution preserved"
                 else System.String.Concat(plural profile.BrokenOriginalParentCount "value" "values", " would stop resolving to the original parent"))
            | BridgeCheck.ExistingRetargetConflict ->
                not profile.ExistingRetargetConflict,
                (if profile.ExistingRetargetConflict then "another retarget already claims this reference" else "no conflicting retarget")
            | BridgeCheck.TrustedConstraintPossible ->
                profile.TrustedConstraintPossible,
                (if profile.TrustedConstraintPossible then "a trusted constraint is achievable" else "the retargeted FK could not land as a trusted constraint")
            | BridgeCheck.BridgeRowsOrphanedFromSource ->
                profile.OrphanedBridgeRowCount = 0L,
                (if profile.OrphanedBridgeRowCount = 0L then "no orphaned bridge rows"
                 else System.String.Concat(plural profile.OrphanedBridgeRowCount "bridge row" "bridge rows", " have no source row"))
            | BridgeCheck.BridgePayloadConflicts ->
                profile.PayloadConflictCount = 0L,
                (if profile.PayloadConflictCount = 0L then "no payload conflicts"
                 else System.String.Concat(plural profile.PayloadConflictCount "payload column" "payload columns", " disagree between bridge and source"))
            | BridgeCheck.IdentityEvidenceMissing ->
                profile.IdentityEvidence <> BridgeIdentityEvidence.Missing,
                (if profile.IdentityEvidence = BridgeIdentityEvidence.Missing then "identity evidence missing" else "identity evidence present")
            | BridgeCheck.IdentityEvidenceAmbiguous ->
                profile.IdentityEvidence <> BridgeIdentityEvidence.Ambiguous,
                (if profile.IdentityEvidence = BridgeIdentityEvidence.Ambiguous then "identity evidence ambiguous" else "identity evidence unambiguous")
            | BridgeCheck.NonAuditReferencesIncluded ->
                profile.OnlyAuditReferences,
                (if profile.OnlyAuditReferences then "scope limited to audit references" else "non-audit references included in scope")
            | BridgeCheck.ExistingConstraintUntrusted ->
                (profile.ExistingConstraintTrusted <> Some false),
                (match profile.ExistingConstraintTrusted with
                 | Some false -> "an existing constraint on the reference is untrusted (WITH NOCHECK)"
                 | Some true  -> "existing constraint is trusted"
                 | None       -> "no existing constraint")
        { Check = check; Severity = BridgeCheck.severity check; Passed = passed; Detail = detail }

    let private readinessFor (verdict: BridgeVerdictKind) (results: BridgeCheckResult list) : BridgeReadiness =
        let inVerdict = results |> List.filter (fun r -> Set.contains verdict (BridgeCheck.verdictsOf r.Check))
        let failed sev = inVerdict |> List.filter (fun r -> r.Severity = sev && not r.Passed) |> List.map (fun r -> r.Check)
        let blocks = failed BridgeCheckSeverity.Block
        let warns  = failed BridgeCheckSeverity.Warn
        if not (List.isEmpty blocks) then BridgeReadiness.Blocked blocks
        elif not (List.isEmpty warns) then BridgeReadiness.ReadyWithWarnings warns
        else BridgeReadiness.Ready

    /// Evaluate every check over the profile and roll the results up into the
    /// three independent readiness verdicts. TOTAL: every check named in
    /// `BridgeCheck.all` is evaluated; `evaluate (BridgeRetargetProfile.clean id)`
    /// has all three verdicts `Ready`.
    let evaluate (profile: BridgeRetargetProfile) : BridgeRetargetDecision =
        let results = BridgeCheck.all |> List.map (checkResult profile)
        { RetargetId  = profile.RetargetId
          Checks      = results
          Retargeting = readinessFor BridgeVerdictKind.Retargeting results
          BridgeRows  = readinessFor BridgeVerdictKind.BridgeRows results
          PayloadSync = readinessFor BridgeVerdictKind.PayloadSync results }

    /// Whether a decision clears the retarget to land — the `Retargeting` verdict
    /// is not `Blocked`. (`ReadyWithWarnings` still lands; warnings are surfaced,
    /// not gating.)
    let retargetCleared (decision: BridgeRetargetDecision) : bool =
        match decision.Retargeting with
        | BridgeReadiness.Blocked _ -> false
        | BridgeReadiness.Ready
        | BridgeReadiness.ReadyWithWarnings _ -> true

    /// The decision-pass core: evaluate every plan's readiness and collect the
    /// CLEARED retargets into the reference→bridge-attribute map the emitter
    /// consumes (via `ComposeState.BridgeRetargets` → `DecisionOverlay.RetargetFk`),
    /// alongside the full decision ledger for the diagnostics plane. A plan whose
    /// `Retargeting` verdict is `Blocked` contributes NO map entry — its FK stays
    /// on the original parent, so an unproven or unsafe retarget is inert, never a
    /// silent mis-retarget. Empty policy ⇒ empty map (byte-identical emission).
    let decide (policy: BridgeRetargetPolicy) : Map<SsKey, SsKey> * BridgeRetargetDecision list =
        let decisions = policy.Plans |> List.map (fun p -> p, evaluate p.Profile)
        let cleared =
            decisions
            |> List.choose (fun (p, d) -> if retargetCleared d then Some (p.ReferenceKey, p.BridgeAttributeKey) else None)
            |> Map.ofList
        cleared, (decisions |> List.map snd)
