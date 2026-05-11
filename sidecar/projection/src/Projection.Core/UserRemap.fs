namespace Projection.Core

// LINT-ALLOW-FILE: validation-error message construction in
// `UserRemapContext.create` smart constructor uses `sprintf "...%d..."`
// to format the SourceUserId's underlying integer in the diagnostic
// message. Same allowed-exception class as `Catalog.create` /
// `Module.create` (per their LINT-ALLOW-FILE block); the validation
// payload is operator-facing audit-trail prose where typed value
// formatting is the right primitive. Per `DECISIONS 2026-05-09 —
// Built-in obligation`, no BCL alternative emits typed-integer
// validation message construction in a way that would be cleaner.

/// User-FK remap evidence — the value `UserFkReflowPass.discover`
/// (chapter 4.2 slice δ) produces and the data-emission triumvirate's
/// MigrationDependenciesEmitter + BootstrapEmitter (chapter 4.2 slice
/// η) consume to rewrite User-FK column values at emission time.
///
/// **Slice γ scope (chapter 4.2; this file is new at slice γ).**
/// Refines the chapter-4.1.B slice ζ placeholder
/// (`UserRemapContext = Map<SsKey, Map<int64, int64>>`) to the
/// structural shape per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §4: a
/// record carrying the matched mapping + the unmatched source-user
/// set + the per-source diagnostic trail. The outer-`SsKey`-keyed
/// `Map` from the masterwork sketch (`AXIOMS.md:486-489` +
/// `VISION.md:169`) is degenerate when there is exactly one user
/// kind in the deployment (the realistic case at chapter 4.2);
/// the flat shape is what consumers actually use. Per IR-grows-
/// under-evidence: if a future deployment has multiple user kinds,
/// the outer Map re-emerges; until then, the flat form pays its
/// weight.
///
/// **Smart-constructor invariant** (per pre-scope §7 slice 3):
/// `Mapping.Keys ∩ Unmatched = ∅` (disjoint). A source user is
/// either matched (in Mapping) or unmatched (in Unmatched), never
/// both — `UserRemapContext.create` validates and rejects overlap.
/// Construction-time validation per the structural-commitment-via-
/// construction-validation operational principle (`AXIOMS.md`); the
/// invariant rides on every value.

/// Per-source-user diagnostic from the discovery pass. Each
/// unmatched source user emits exactly one variant; the variant
/// names WHY the match failed so the operator can route the
/// remediation (re-issue an override, fix the source data, accept
/// the fallback). Per pre-scope §6: every unmatched user produces
/// exactly one `Warning` `DiagnosticEntry` carrying the diagnostic
/// variant via the dual-writer's `Lineage<Diagnostics<'a>>`
/// composition (chapter 2 substrate).
type RemapDiagnostic =
    /// Source user has no email registered; the `ByEmail` strategy
    /// (or any strategy with `ByEmail` as its primary recursion arm)
    /// cannot match. Surfaces as `Warning` `userFkReflow.noEmail`
    /// at slice δ.
    | NoEmail of source: SourceUserId
    /// Source user has an email but no target user with the same
    /// (case-insensitive, trimmed) email exists. The `ByEmail`
    /// strategy's primary failure mode in cross-environment User
    /// reflow.
    | EmailDidNotMatch of source: SourceUserId * email: Email
    /// Source user's `OssysOriginal` SsKey has no target match.
    /// The `BySsKey` strategy's failure mode — typically when the
    /// source user is environment-specific (per pre-scope §3:
    /// "users are environment-resident").
    | SsKeyDidNotMatch of source: SourceUserId * key: SsKey
    /// Source user is not in the `ManualOverride` map. Operator
    /// supplied an override map but missed this source user;
    /// remediation = extend the override.
    | OverrideMissing of source: SourceUserId
    /// Strategy chain ran to exhaustion without a match and no
    /// `FallbackToSystemUser` was configured. The most actionable
    /// diagnostic — operator can either supply a fallback or
    /// accept that the row is not reflowed.
    | NoFallbackConfigured of source: SourceUserId


/// Discovered remap evidence. Produced by `UserFkReflowPass.discover`
/// (slice δ); consumed by emitters that rewrite User-FK column
/// values at emission time. Per A32: a sibling discovery-pass
/// produced value visible to consumer Π's via the EnrichedCatalog
/// shape (chapter 4.2 close cashes out A32's reserved "UAT-Users"
/// example).
type UserRemapContext =
    {
        /// Source-user → target-user mapping for users matched by
        /// the strategy. Keys are disjoint from `Unmatched` per
        /// the smart-constructor invariant.
        Mapping     : Map<SourceUserId, TargetUserId>
        /// Source users the strategy could not match. Disjoint
        /// from `Mapping.Keys` per the smart-constructor invariant.
        /// The emitter consults this set to decide whether to skip
        /// rows or surface a Diagnostic at emission time
        /// (V1 reference: `UserMatchingResult.cs` + `EmitArtifactsStep.
        /// cs` — V1's behavior is "diagnostic + skip"; V2 inherits).
        Unmatched   : Set<SourceUserId>
        /// Per-source-user diagnostics. Order is the discovery
        /// pass's iteration order (sorted by source SsKey for T1
        /// byte-determinism).
        Diagnostics : RemapDiagnostic list
    }


[<RequireQualifiedAccess>]
module UserRemapContext =

    let private overlap (sourceUserValue: int) =
        ValidationError.create
            "userRemapContext.overlap"
            (sprintf
                "UserRemapContext invariant violation: source user %d appears in both Mapping and Unmatched."
                sourceUserValue)

    /// The empty remap context. The neutral input for callers
    /// without a populated remap (the dominant case at slice γ
    /// since the discovery pass arrives at slice δ); MigrationDeps
    /// + Bootstrap against `empty` produce no User-FK rewrites.
    let empty : UserRemapContext =
        { Mapping = Map.empty; Unmatched = Set.empty; Diagnostics = [] }

    /// True iff every source user the strategy considered is in
    /// the mapping (no unmatched). Per pre-scope §7 slice 3:
    /// `FallbackToSystemUser` strategies structurally guarantee
    /// `isFullyMapped = true` — the safety-net catches every miss.
    let isFullyMapped (c: UserRemapContext) : bool =
        Set.isEmpty c.Unmatched

    /// Count of source users the strategy could not match. The
    /// operator's primary triage signal — a non-zero count means
    /// rows attributed to those source users are at risk of
    /// orphan FKs in the target environment.
    let unmatchedCount (c: UserRemapContext) : int =
        Set.count c.Unmatched

    /// Look up a source user's target. `None` if the user is
    /// unmatched OR not in the source population that produced
    /// the context.
    let tryFindTarget (source: SourceUserId) (c: UserRemapContext) : TargetUserId option =
        Map.tryFind source c.Mapping

    /// Smart constructor enforcing the disjointness invariant
    /// (`Mapping.Keys ∩ Unmatched = ∅`). Per the structural-
    /// commitment-via-construction-validation operational
    /// principle (`AXIOMS.md`): the invariant rides on every
    /// value; consumers don't re-validate.
    ///
    /// Returns `Error` with one `userRemapContext.overlap`
    /// diagnostic per overlapping source user (validation-style
    /// accumulation; FsToolkit `validation {}` precedent). The
    /// discovery pass at slice δ constructs through `create` so
    /// any pass-internal logic bug producing overlap surfaces at
    /// the type level immediately.
    let create
        (mapping: Map<SourceUserId, TargetUserId>)
        (unmatched: Set<SourceUserId>)
        (diagnostics: RemapDiagnostic list)
        : Result<UserRemapContext> =
        let mappingKeys =
            mapping |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let overlaps = Set.intersect mappingKeys unmatched
        if Set.isEmpty overlaps then
            Result.success
                { Mapping     = mapping
                  Unmatched   = unmatched
                  Diagnostics = diagnostics }
        else
            overlaps
            |> Set.toList
            |> List.map (fun s -> overlap (SourceUserId.value s))
            |> Result.failure
