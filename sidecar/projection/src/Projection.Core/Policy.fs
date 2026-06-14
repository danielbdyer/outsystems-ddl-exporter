namespace Projection.Core

/// Selection axis of `Policy` (A12 amended). Determines which kinds
/// participate in a projection. The closed three-way discriminant covers
/// "all" (the default), "include only this set", and "exclude this set."
/// Wider selectors (predicate-driven, profile-driven) appear when admire
/// passes surface them.
type SelectionPolicy =
    /// Every kind in the catalog participates. Default.
    | IncludeAll
    /// Only kinds whose SsKey is in this set participate.
    | IncludeOnly of SsKey Set
    /// Every kind participates except those whose SsKey is in this set.
    | ExcludeOnly of SsKey Set


/// Within-`EmitData` composition axis (chapter 4.1.B slice η —
/// `DataEmissionComposer` dispatch). Selects WHICH composition of
/// data emitters fires when `EmissionPolicy.EmitData = true`. Per
/// `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` §3.1 option (b): the new
/// DU lands as a sibling field on the existing `EmissionPolicy`
/// record, not as a rename — preserving the four-axis A12 amendment
/// while landing the meaningful inflection point of the dispatch.
///
/// Variants (per pre-scope §3.2):
///   - `AllRemaining` — Static + MigrationDependencies + Bootstrap
///     all fire; Bootstrap covers everything not covered by the
///     prior two. The promoted-lane default.
///   - `AllExceptStatic` — Static skipped (already populated upstream
///     by the cutover team's static seed pass); Migration + Bootstrap
///     fire.
///   - `AllData` — Bootstrap covers everything (Static included);
///     useful for full data-only refresh against a populated schema.
///
/// Emitters cannot consume `Policy` per A18 amended; the
/// `DataEmissionComposer` (slice η) reads
/// `Policy.Emission.DataComposition` and chooses which emitters
/// fire — emitters do not.
type DataComposition =
    /// Default (promoted-lane). Bootstrap covers what Static +
    /// MigrationDependencies don't.
    | AllRemaining
    /// Static skipped (cutover-time pre-population). Migration +
    /// Bootstrap fire.
    | AllExceptStatic
    /// Bootstrap fires for every kind including static; Static +
    /// MigrationDependencies skipped (data-only full refresh).
    | AllData


/// One equality term of a convergent-delete scope (AC-D7 / AC-G4): the
/// physical column gating eligibility plus its raw value (typed per kind
/// at resolution — the same `SqlLiteral.ofRaw` lift row values ride).
type DeleteScopeTerm = {
    Column : string
    Value  : string
}

/// The Emission-axis delete-scope policy (AC-D7 / AC-G4 — operator intent,
/// `OverlayAxis = Emission`). Declares the scope within which the data
/// emitters' MERGE may carry a `WHEN NOT MATCHED BY SOURCE … THEN DELETE`
/// arm (a tenant / partition gate). Absent (the default), no arm is
/// emitted — byte-identical to the upsert-only MERGE. The terms name
/// PHYSICAL columns; resolution is per kind (see `DeleteScopePolicy.resolveFor`).
type DeleteScopePolicy = {
    Terms : DeleteScopeTerm list
}

[<RequireQualifiedAccess>]
module DeleteScopePolicy =

    /// Resolve the policy against ONE kind: `Some` typed `(column, literal)`
    /// terms when every term column is a writable attribute of the kind
    /// (the scope predicate is expressible there), `None` otherwise.
    /// `None` is the FAITHFUL rendering, not a silent skip: a kind that
    /// does not carry the gating column has no row inside the scope, so
    /// the convergent-delete arm selects nothing and is omitted.
    let resolveFor (kind: Kind) (policy: DeleteScopePolicy) : (string * SqlLiteral) list option =
        let resolveTerm (t: DeleteScopeTerm) : (string * SqlLiteral) option =
            kind.Attributes
            |> List.tryFind (fun a ->
                (ColumnRealization.columnNameText a.Column).Equals(t.Column, System.StringComparison.OrdinalIgnoreCase))
            |> Option.map (fun a -> ColumnRealization.columnNameText a.Column, SqlLiteral.ofRaw a.Type t.Value)
        let resolved = policy.Terms |> List.map resolveTerm
        if not (List.isEmpty resolved) && List.forall Option.isSome resolved
        then Some (List.choose id resolved)
        else None


/// NM-73 (WP6.6, operator choice C2) — the data emitters' drift-guard
/// posture. `Standard` (the default) emits the MERGE alone: byte-identical
/// to pre-NM-73 and the CDC-silence-canonical posture V2 commits to.
/// `ValidateBeforeApply` is the operator's **conservative override** — it
/// prepends a typed symmetric-`EXCEPT` drift guard before the MERGE
/// (mirrors V1's `StaticSeedSqlBuilder.ValidateThenApply`): if the target
/// is non-empty AND its managed rows differ — in *either* direction — from
/// the source about to be written, `THROW 50000` aborts the batch before
/// the MERGE overwrites. First apply over an empty target proceeds; an
/// idempotent re-apply stays silent; a *drifted* re-apply throws. Per C2,
/// CDC-silence stays canonical; this is the opt-in fallback until J5
/// proves the CDC path on real UAT. Modeled as a closed DU (not a bool)
/// so a future third verification posture lands as a named variant.
[<RequireQualifiedAccess>]
type DataVerification =
    /// MERGE alone — byte-identical, CDC-silence-canonical (the default).
    | Standard
    /// Symmetric-`EXCEPT` drift guard prelude + `THROW 50000`, one GO
    /// batch with the MERGE. The operator's conservative override.
    | ValidateBeforeApply

/// Emission axis. Which artifact families a projection emits. The booleans
/// are deliberate; orthogonality of schema / data / diagnostics is the
/// algebra's commitment (decomposition Vector 2). When emission shapes
/// multiply, this record grows fields rather than packing flags into a DU.
///
/// **Slice η (chapter 4.1.B) extension**: `DataComposition` field
/// (closed DU) controls which combination of data emitters fires
/// when `EmitData = true`. Default `AllRemaining` matches V1's
/// promoted-lane behavior (Static + Migration + Bootstrap together).
type EmissionPolicy = {
    EmitSchema      : bool
    EmitData        : bool
    EmitDiagnostics : bool
    DataComposition : DataComposition
    /// Chapter 4.8 slice γ — operator toggle for platform-auto-generated
    /// indexes (V1's `SsdtManifestOptions.IncludePlatformAutoIndexes`).
    /// `true` (V1 default) = include platform-auto indexes in the SSDT
    /// bundle; `false` = filter them at emission time. Consumes the
    /// `Index.IsPlatformAuto` IR field shipped at chapter 4.6 slice β.
    IncludePlatformAutoIndexes : bool
    /// AC-D7 / AC-G4 — the operator's convergent-delete scope for the data
    /// emitters' MERGE. `None` (the default) emits NO delete arm —
    /// byte-identical to the pre-scope output.
    DeleteScope : DeleteScopePolicy option
    /// NM-38 — operator toggle for the SSDT constraint-rendering overlay
    /// (`ConstraintFormatter.Mode`, classified `OperatorIntent Emission`).
    /// `true` (the V1-parity production default) reformats ScriptDom's
    /// compact column-inline constraints into V1's elegant multi-line
    /// shape; `false` is the diagnostic / V1-parity-bisect opt-out that
    /// passes ScriptDom's raw output through. The typed `Mode` lives in
    /// `Projection.Targets.SSDT` (downstream of Core), so the axis rides
    /// here as a bool the SSDT emit seam lifts to `Render.toTextWith`.
    /// Default `true` keeps the default bundle byte-identical.
    RenderConstraintsElegant : bool
    /// NM-70 (WP5) — operator toggle for the identity extended-property
    /// annotations (the `Projection.SsKey` / `Projection.LogicalName`
    /// SsKey-bearing extended properties `SsdtDdlEmitter` writes). `true`
    /// (the production default) emits them unconditionally — byte-identical
    /// to pre-NM-70 emission, and the posture that lets ReadSide recover the
    /// persisted SsKey on roundtrip read. `false` is a NAMED DOWNGRADE: the
    /// `Projection.*` identity properties are suppressed (other extended
    /// properties — Descriptions, authored properties — still emit), and the
    /// composition seam emits the `emission.identityAnnotations.omitted`
    /// diagnostic recording that identity recovery now degrades to
    /// name-derived SsKeys (no persisted SsKey to read back). Sibling to
    /// `RenderConstraintsElegant`; lifted to the SSDT emit seam at the
    /// composition layer (A18 — the emitter never reads `Policy`).
    EmitIdentityAnnotations : bool
    /// NM-73 (WP6.6) — the data emitters' drift-guard posture. `Standard`
    /// (the default) is byte-identical to pre-NM-73; `ValidateBeforeApply`
    /// prepends the symmetric-`EXCEPT` `THROW 50000` guard before each
    /// MERGE. Lifted to the data emit seam as a plain value (A18 — the
    /// emitter never reads `Policy`). See the `DataVerification` DU.
    DataVerification : DataVerification
}


/// Insertion axis. How data artifacts are applied to the target. For
/// schema-only configurations this is `SchemaOnly`. The four variants
/// match the masterwork's `InsertionStrategy` (lines 580–666).
type InsertionPolicy =
    | SchemaOnly
    | InsertNew
    | Merge
    | TruncateAndInsert


/// Tightening axis (A12 amended 2026-05-09). The fourth orthogonal
/// Policy axis. Tightening is genuinely orthogonal to Selection /
/// Emission / Insertion — it controls *what shape of constraint
/// decisions* gets produced, independent of which kinds participate,
/// what artifacts are emitted, or how data is applied.
///
/// Modeled as a **registry of named interventions**, not as a flat
/// configuration record. The empty TighteningPolicy contains no
/// interventions, and a pass running against it produces no decisions —
/// **V2 introduces no alterations to the system unless an intervention
/// is explicitly registered.** Each intervention carries a stable `Id`
/// so its application is trackable through lineage events; "which
/// intervention changed this column?" becomes a structural question
/// that the audit trail answers.
///
/// See DECISIONS 2026-05-09 (the plugin/intervention refinement) for
/// the worked example: the first commit shipped a flat-record default
/// shape, and reflection refined it to this plugin shape on the
/// principle that defaults that intervene are themselves an
/// intervention.

// Mode is intentionally absent. V1's TighteningMode enum (Cautious /
// EvidenceGated / Aggressive) is collapsed to a single tightening
// behavior at the V2 level — V1 only ever uses Cautious in production,
// and "IR grows under evidence" forbids carrying unused variants.
// If a real second mode lands later (admire pass surfaces a need), it
// arrives as a new field or as a new TighteningIntervention variant
// at that point.


/// One row of the override table on a NullabilityTighteningConfig.
/// Keyed by attribute identity (per A4) rather than by (module, entity,
/// attribute) names — the V2 boundary resolves V1's name-keyed overrides
/// to SsKey before they reach the pure core.
type TighteningOverride = {
    AttributeKey : SsKey
    Action       : OverrideAction
}

/// What an override does. V2 starts with the single action V1 actually
/// uses — keep the column nullable, bypassing the entire signal
/// hierarchy. Future actions extend the DU when admire passes surface
/// them (e.g., force-not-null, require-operator-approval).
and OverrideAction =
    /// Force the column to remain nullable; bypass signal evaluation
    /// entirely. Operator-approved escape hatch; rationale recorded as
    /// `NullabilityOverride`.
    | KeepNullable


/// V1's nullability-tightening intervention, collapsed to V2's
/// single-mode form. Wrapped inside a
/// `TighteningIntervention.Nullability` so its application is named
/// and trackable. V2 carries no defaults — every field is explicit;
/// the caller registering the intervention chooses every value.
type NullabilityTighteningConfig = {
    /// Permitted null fraction — `allowed = RowCount * NullBudget`.
    /// Range [0, 1]; enforced at construction by
    /// `NullabilityTighteningConfig.create`.
    NullBudget               : decimal
    /// May a column whose model declares mandatory be relaxed to
    /// nullable when profile evidence shows nulls? V1 keyed this on
    /// the (now collapsed) Cautious mode and named it
    /// `AllowCautiousNullabilityRelaxation`; V2 names it for the
    /// semantic ("permit mandatory→nullable relaxation under
    /// evidence"). The caller chooses explicitly — there is no
    /// default behavior.
    AllowMandatoryRelaxation : bool
    /// Operator-approved overrides. Each override bypasses the
    /// signal hierarchy entirely for its target attribute. Empty
    /// list = no overrides.
    Overrides                : TighteningOverride list
}


/// V1's unique-index tightening intervention. Carries the V1
/// `TighteningOptions.Uniqueness` shape verbatim (two boolean toggles —
/// no NullBudget, no Overrides — V1's UniqueIndex configuration is
/// minimal; the V1↔V2 admire (ADMIRE.md 2026-05-10) confirms this).
type UniqueIndexTighteningConfig = {
    /// Should single-column unique constraints be enforced?
    /// V1's `UniquenessOptions.EnforceSingleColumnUnique`.
    EnforceSingleColumnUnique : bool
    /// Should composite (multi-column) unique constraints be enforced?
    /// V1's `UniquenessOptions.EnforceMultiColumnUnique`.
    EnforceMultiColumnUnique  : bool
}


/// V2's per-attribute distribution-driven uniqueness inference
/// configuration. The first **distribution-aware** strategy
/// configuration (ADMIRE.md 2026-05-13). Hybrid mode:
/// uniqueness-domain inheritance from V1 + per-attribute
/// distribution-driven inference V1 cannot perform.
///
/// V1 has no analog; V1 collects no Categorical distribution
/// evidence per ADMIRE.md 2026-05-12. Configuration shape is
/// V2-defined.
type CategoricalUniquenessConfig = {
    /// Don't suggest uniqueness for vocabularies smaller than this.
    /// A binary attribute (`distinctCount = 2`) is rarely meaningful
    /// as unique; a single-value attribute (`distinctCount = 1`) is
    /// pathological. Caller chooses the floor; the algebra reports
    /// what the caller chose.
    MinDistinctCountForUniqueness : int64
}


/// V1's foreign-key tightening intervention. Carries V1's
/// `ForeignKeyOptions` shape verbatim (five boolean toggles — V1's
/// FK configuration is plain enable/allow gates with no thresholds
/// or override lists; the V1↔V2 admire (ADMIRE.md 2026-05-11)
/// confirms this).
///
/// V1's `_mode == Cautious` gate on the WITH NOCHECK path
/// (ForeignKeyEvaluator.cs:159) is collapsed in V2 because V2 has
/// no TighteningMode (DECISIONS 2026-05-09). The semantic is
/// preserved by `AllowNoCheckCreation` — the caller registering
/// the intervention chooses whether the WITH NOCHECK fallback is
/// allowed.
type ForeignKeyTighteningConfig = {
    /// Should FK constraints be created at all?
    /// V1's `ForeignKeyOptions.EnableCreation`.
    EnableCreation                 : bool
    /// May an FK cross schema boundaries?
    /// V1's `ForeignKeyOptions.AllowCrossSchema`.
    AllowCrossSchema               : bool
    /// May an FK cross catalog (database) boundaries? V2's IR does
    /// not yet model catalog names (`PhysicalRealization` carries
    /// `Schema` and `Table` only); this toggle is reachable through
    /// the DU's keep-reason variant but the rule is unreachable
    /// today (ADMIRE.md 2026-05-11 — IR refinement deferred).
    AllowCrossCatalog              : bool
    /// Treat a missing DeleteRule as if it were "Ignore" (V1's
    /// `TreatMissingDeleteRuleAsIgnore`). V2's `Reference.OnDelete`
    /// is a closed DU and cannot be missing; this toggle is preserved
    /// for V1 parity but currently unreachable from V2's IR (the
    /// V1↔V2 adapter would resolve missing rules to `OnDelete.NoAction`
    /// at the boundary, which is what V1 effectively does).
    TreatMissingDeleteRuleAsIgnore : bool
    /// May the constraint be created WITH NOCHECK when orphans or
    /// Ignore-rules would otherwise block it? V1's
    /// `AllowNoCheckCreation` plus the (now collapsed) Cautious mode.
    AllowNoCheckCreation           : bool
}


/// One tightening intervention. The DU is closed; new intervention
/// kinds (type tightening, view-column tightening, etc.) land as
/// new variants when admire passes surface the need.
///
/// Every intervention carries an `Id` — a stable string identifier the
/// caller chooses (e.g., `"v1-cautious-nullability"`,
/// `"v1-style-uniqueness"`, `"v1-style-fk"`). The Id appears in
/// lineage events emitted by passes that fire this intervention;
/// audit consumers can ask "which intervention changed this column
/// / index / reference" and the trail answers structurally.
type TighteningIntervention =
    /// V1's nullability tightening — the
    /// `NullabilityEvaluator` migration's natural form.
    | Nullability of id: string * config: NullabilityTighteningConfig
    /// V1's unique-index tightening — the
    /// `UniqueIndexDecisionOrchestrator` migration's natural form.
    /// Decides per-index, not per-attribute (the structural divergence
    /// from Nullability; ADMIRE.md 2026-05-10).
    | UniqueIndex of id: string * config: UniqueIndexTighteningConfig
    /// V1's foreign-key tightening — the `ForeignKeyEvaluator`
    /// migration's natural form. Decides per-reference (the third
    /// granularity, after per-attribute and per-index; ADMIRE.md
    /// 2026-05-11).
    | ForeignKey of id: string * config: ForeignKeyTighteningConfig
    /// V2's per-attribute distribution-driven uniqueness inference —
    /// the first **distribution-aware** strategy (ADMIRE.md
    /// 2026-05-13). Hybrid-mode admire: V1's UniqueIndexEvaluator
    /// covers the uniqueness concept per-index based on binary
    /// HasDuplicate evidence; this strategy adds per-attribute
    /// inference based on richer Categorical distribution evidence.
    /// The fourth registered-intervention variant.
    | CategoricalUniqueness of id: string * config: CategoricalUniquenessConfig


/// Tightening axis. A registry of zero or more named interventions.
/// Empty = no interventions = no tightening decisions produced.
/// V2's strict default: the system introduces no alterations unless
/// the caller explicitly registers an intervention.
type TighteningPolicy = {
    Interventions : TighteningIntervention list
}


// ---------------------------------------------------------------------------
// User-FK reflow axis (chapter 4.2 slice α; Policy axis #5).
//
// Per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §3: `UserMatchingStrategy`
// is operator intent — the per-environment decision *how to bridge*
// cross-environment user identity. Lives on `Policy` alongside the
// other four axes (Selection / Emission / Insertion / Tightening) per
// pre-scope §2's "the new Policy shape" framing.
//
// Identity value objects (`UserId` / `SourceUserId` / `TargetUserId`
// / `Email`) live in `UserIdentity.fs` (compiles before Profile.fs)
// so Profile can carry `UserPopulation<SourceUserId>` /
// `UserPopulation<TargetUserId>` typed fields (slice β). The strategy
// DU below references those types from `UserIdentity.fs` directly.
// ---------------------------------------------------------------------------

/// Per-environment user-matching strategy. Closed DU; per pre-scope
/// §3 + V1's empirical experience (`UserMatchingEngine.cs:33-67` +
/// `UserMatchingOptions.cs:7-19`):
///
///   - V1's three primary strategies (`CaseInsensitiveEmail`,
///     `ExactAttribute`, `Regex`) collapse to V2's two (`ByEmail`,
///     `BySsKey`) plus `ManualOverride` (V1's `Regex` is
///     structurally indistinguishable from operator-supplied
///     transformation for V2's algebraic purposes; V1's
///     `ExactAttribute` folds into `BySsKey` only when V1's
///     configured attribute IS SsKey — the V1 differential test
///     codifies this Skip-stub).
///   - V1's orthogonal `Ignore | SingleTarget | RoundRobin`
///     fallback dimension collapses to one strategy variant
///     (`FallbackToSystemUser`) — chosen over an
///     orthogonal-axis representation because V1's empirical
///     pipeline uses fallback as a *post-hoc* layer on top of
///     one primary strategy.
///
/// The recursive `FallbackToSystemUser of fallback × primary`
/// shape encodes "try the primary; on miss, attribute to the
/// system user" structurally. The list-of-rules alternative
/// invites composability the operator workflow does not actually
/// need, and `BySsKey | ByEmail` ordering would be a third
/// variant (`OrTried of strategy × strategy`) the IR-grows-under-
/// evidence discipline says should not exist until a real
/// consumer demands it.
///
/// Smart-constructor invariants (slice γ; not yet shipped at
/// slice α): `Email.create` rejects blank input. `UserMatchingStrategy`
/// itself has no construction validation; `ManualOverride
/// Map.empty` is structurally valid (a degenerate override map
/// is a no-op).
type UserMatchingStrategy =
    /// V1's `CaseInsensitiveEmail`. Match source user by email to
    /// target user with same email (case-insensitive, trimmed).
    /// Failure mode: identical email in two environments belonging
    /// to logically different humans; or environment-divergent
    /// email format. Surfaces as `Warning` `userFkReflow.email
    /// DidNotMatch` per pre-scope §6.
    | ByEmail
    /// Match by V1 SSKey GUID (`OssysOriginal` SsKey). The most
    /// identity-stable strategy when both environments inherit
    /// from a shared OSSYS origin. V1 has no exact-SsKey strategy;
    /// this is the V2-native cleanup since V2 already carries SsKey
    /// as identity (A4).
    | BySsKey
    /// Operator-supplied per-user mapping. Always works for
    /// every source user IN the override map; sources NOT in the
    /// map fall through to `Unmatched`. V1 reference: `UserMapLoader.
    /// Load` + CSV (`SourceUserId,TargetUserId,Rationale`).
    /// `Map.empty` is a degenerate no-op (every source user is
    /// unmatched).
    | ManualOverride of Map<SourceUserId, TargetUserId>
    /// Recursive composition: try `primary`; on miss, attribute
    /// to `fallback`. Structurally guarantees `Set.isEmpty
    /// Unmatched` (the safety net catches every miss). Lineage
    /// distinguishes primary-matched vs. fallback-matched via
    /// `Annotated "matched-by-FallbackToSystemUser.fallback"`
    /// vs. `"matched-by-FallbackToSystemUser.primary"`.
    | FallbackToSystemUser of fallback: TargetUserId * primary: UserMatchingStrategy


/// The five-axis policy aggregate (A12 amended 2026-05-09 four-axis;
/// extended at chapter 4.2 slice α to add `UserMatching` per pre-scope
/// §2). Each axis is its own structured value; the five are composed
/// in a single record. Changing one axis does not constrain the
/// others. `Policy.empty` is the no-policy default — schema-only
/// emission, every kind selected, no insertion semantics, no
/// tightening interventions, default `ByEmail` user matching — and is
/// a first-class input for use cases that need none of the axes.
///
/// **Why `UserMatching` is a Policy axis** (per pre-scope §2): it is
/// per-environment operator decision, supplied at promotion time,
/// describing how cross-environment user identity should be
/// reconciled. Not evidence (Profile carries the empirical user
/// populations); not structure (Catalog carries the FK shape); the
/// operator's choice of *how to bridge* evidence between environments.
/// Adding a record field doesn't trigger DU exhaustiveness — record-
/// construction sites must add `UserMatching = ...` (one site:
/// `Policy.empty`); pattern-match sites destructuring `Policy` (zero
/// today; consumers read fields by name) are unaffected.
type Policy = {
    Selection    : SelectionPolicy
    Emission     : EmissionPolicy
    Insertion    : InsertionPolicy
    Tightening   : TighteningPolicy
    UserMatching : UserMatchingStrategy
}


/// The three substantive inputs to `Project = Π ∘ E` per A6 amended.
/// Bundling them into a single record lets passes name their triple
/// explicitly when they consume more than one.
///
/// Use cases that consume only Catalog (e.g., `canonicalizeIdentity`)
/// continue to take `Catalog` directly; passes that need Policy or
/// Profile evidence accept `ProjectionInput` (or destructure as needed).
type ProjectionInput = {
    Catalog : Catalog
    Policy  : Policy
    Profile : Profile
}


[<RequireQualifiedAccess>]
module SelectionPolicy =

    /// The default — every kind participates.
    let empty : SelectionPolicy = IncludeAll

    /// True iff the kind is selected under this policy.
    let isSelected (key: SsKey) (policy: SelectionPolicy) : bool =
        match policy with
        | IncludeAll        -> true
        | IncludeOnly keys  -> Set.contains key keys
        | ExcludeOnly keys  -> not (Set.contains key keys)

    /// Project a catalog to only its selected kinds. Useful for emitters
    /// that want to operate on the selected subset; structural passes
    /// continue to operate on the full catalog (per A33: sort/order
    /// passes see all kinds, emission filters afterwards).
    let filterCatalog (policy: SelectionPolicy) (c: Catalog) : Catalog =
        c
        |> Lens.over CatalogLenses.modules (
            List.map (Lens.over CatalogLenses.kindsOf (
                List.filter (fun k -> isSelected k.SsKey policy))))


[<RequireQualifiedAccess>]
module EmissionPolicy =

    let private allFalse =
        ValidationError.create
            "emissionPolicy.allFalse"
            "EmissionPolicy must enable at least one of EmitSchema / EmitData / EmitDiagnostics — a no-op policy is a programmer error."

    /// Smart constructor enforcing the per-A39 invariant: at least
    /// one artifact family is enabled. A no-op `EmissionPolicy` is
    /// a programmer error (the catalog-emit pipeline produces zero
    /// output, which is silently wrong rather than loudly missing).
    /// Chapter-3.6 cash-out of audit Top-10 #8: future-proofs
    /// against invariant insertion.
    ///
    /// **Slice η (chapter 4.1.B)**: `dataComposition` field added.
    /// Defaults at the `empty` / `schemaOnly` / `dataOnly` /
    /// `combined` convenience constructors to `AllRemaining` (the
    /// promoted-lane default per pre-scope §3.2).
    let create
        (emitSchema: bool)
        (emitData: bool)
        (emitDiagnostics: bool)
        (dataComposition: DataComposition)
        : Result<EmissionPolicy> =
        use _ = Bench.scope "ir.policy.emission.create"
        if not emitSchema && not emitData && not emitDiagnostics then
            Result.failureOf allFalse
        else
            Result.success
                { EmitSchema      = emitSchema
                  EmitData        = emitData
                  EmitDiagnostics = emitDiagnostics
                  DataComposition = dataComposition
                  // Chapter 4.8 slice γ — V1 parity default.
                  IncludePlatformAutoIndexes = true
                  // AC-D7 — upsert-only default; the scope is operator opt-in.
                  DeleteScope = None
                  // NM-38 — V1-parity default-on; the elegant multi-line
                  // constraint shape is the production default (matches the
                  // prior hardcoded `Render.toText` Enabled mode).
                  RenderConstraintsElegant = true
                  // NM-70 — identity annotations emit by default (the
                  // downgrade-free posture; byte-identical to pre-NM-70 and
                  // the posture ReadSide's persisted-SsKey recovery needs).
                  EmitIdentityAnnotations = true
                  // NM-73 — CDC-silence-canonical default; the EXCEPT drift
                  // guard is the operator's opt-in conservative override.
                  DataVerification = DataVerification.Standard }

    /// Replace `IncludePlatformAutoIndexes` while preserving the rest
    /// of the policy. Chapter 4.8 slice γ. Operators set to `false` to
    /// filter platform-auto indexes from the SSDT bundle.
    let withIncludePlatformAutoIndexes (includeAuto: bool) (policy: EmissionPolicy) : EmissionPolicy =
        { policy with IncludePlatformAutoIndexes = includeAuto }

    /// NM-38 — replace `RenderConstraintsElegant` while preserving the rest
    /// of the policy. Operators set to `false` to fall back to ScriptDom's
    /// compact column-inline constraint emission (the V1-parity / regression-
    /// bisect opt-out). Sibling to `withIncludePlatformAutoIndexes`.
    let withRenderConstraintsElegant (elegant: bool) (policy: EmissionPolicy) : EmissionPolicy =
        { policy with RenderConstraintsElegant = elegant }

    /// NM-70 (WP5) — replace `EmitIdentityAnnotations` while preserving the
    /// rest of the policy. Operators set to `false` to suppress the
    /// `Projection.*` identity extended properties (the named downgrade:
    /// identity recovery degrades to name-derived SsKeys). Sibling to
    /// `withRenderConstraintsElegant`.
    let withEmitIdentityAnnotations (emit: bool) (policy: EmissionPolicy) : EmissionPolicy =
        { policy with EmitIdentityAnnotations = emit }

    /// NM-73 (WP6.6) — replace `DataVerification` while preserving the rest
    /// of the policy. Operators set `ValidateBeforeApply` to prepend the
    /// symmetric-`EXCEPT` drift guard before the data MERGEs (the C2
    /// conservative override). Sibling to `withEmitIdentityAnnotations`.
    let withDataVerification (verification: DataVerification) (policy: EmissionPolicy) : EmissionPolicy =
        { policy with DataVerification = verification }

    /// Project a catalog by the `IncludePlatformAutoIndexes` toggle. When
    /// the policy says `true` (V1 default), returns the catalog
    /// unchanged. When `false`, returns a catalog with each Kind's
    /// `Indexes` list pruned of `IsPlatformAuto = true` entries. Per A18
    /// amended: the emitter consumes the filtered catalog; Policy lives
    /// at the composition layer.
    ///
    /// Chapter 4.8 slice γ. Sibling to `SelectionPolicy.filterCatalog`.
    let filterPlatformAutoIndexes (policy: EmissionPolicy) (c: Catalog) : Catalog =
        if policy.IncludePlatformAutoIndexes then c
        else
            { Modules =
                c.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                { k with
                                    Indexes =
                                        k.Indexes
                                        |> List.filter (fun i -> not i.IsPlatformAuto) }) })
              Sequences = c.Sequences }

    /// Default emission: schema + diagnostics (the default bundle).
    /// `EmitData` is opt-in (off here); `EmitSchema` and `EmitDiagnostics`
    /// are both on because the default `Compose.project` bundle emits the
    /// CREATE/SSDT schema bundle AND the operational diagnostic artifacts
    /// (decision log / remediation / summary / suggest-config).
    ///
    /// NM-02 (2026-06-13): these two booleans now gate real emit steps
    /// (`projectFromChainWithState`). The default must therefore declare
    /// what it actually emits — so `EmitDiagnostics` is `true` here (it was
    /// previously `false`, an inert field the `allFalse` validator defended
    /// while gating nothing). Flipping it to `true` keeps the default bundle
    /// byte-identical (the default already emitted diagnostics) while making
    /// the field honest — the audit's invariant: every disjunct of the
    /// `allFalse` refusal gates a real emit step.
    /// Constructed via the smart constructor; `Result.value` is safe
    /// because the constants satisfy the invariant by construction.
    let empty : EmissionPolicy =
        create true false true AllRemaining |> Result.value

    /// Schema artifacts only — the CREATE/SSDT bundle with NO diagnostic
    /// artifacts. Distinct from `empty` since NM-02 wired `EmitDiagnostics`
    /// to gate the diagnostic-artifact emission: `empty` is the default
    /// bundle (schema + diagnostics); `schemaOnly` is the narrower
    /// schema-without-diagnostics profile.
    let schemaOnly : EmissionPolicy =
        create true false false AllRemaining |> Result.value

    /// Data artifacts only — for full-export pipelines that keep schema
    /// emission elsewhere.
    let dataOnly : EmissionPolicy =
        create false true false AllRemaining |> Result.value

    /// All three artifact families together.
    let combined : EmissionPolicy =
        create true true true AllRemaining |> Result.value

    /// Replace the `DataComposition` field while preserving the three
    /// emit-axis booleans. Useful for callers who want the existing
    /// emission profile (schema-only / data-only / combined) but a
    /// non-default composition (e.g., data-only refresh under
    /// `AllExceptStatic` because static seeds were applied upstream).
    let withDataComposition (composition: DataComposition) (policy: EmissionPolicy) : EmissionPolicy =
        { policy with DataComposition = composition }


[<RequireQualifiedAccess>]
module InsertionPolicy =

    let empty : InsertionPolicy = SchemaOnly


[<RequireQualifiedAccess>]
module NullabilityTighteningConfig =

    let private nullBudgetOutOfRange =
        ValidationError.create
            "nullabilityTighteningConfig.nullBudget.outOfRange"
            "NullBudget must be in [0, 1]."

    /// Construct a `NullabilityTighteningConfig`. Validates
    /// `NullBudget ∈ [0, 1]`. Carries no defaults — every field is
    /// explicit.
    let create
        (nullBudget: decimal)
        (allowMandatoryRelaxation: bool)
        (overrides: TighteningOverride list)
        : Result<NullabilityTighteningConfig> =
        use _ = Bench.scope "ir.policy.nullability.create"
        if nullBudget < 0.0m || nullBudget > 1.0m then
            Result.failureOf nullBudgetOutOfRange
        else
            Result.success
                { NullBudget               = nullBudget
                  AllowMandatoryRelaxation = allowMandatoryRelaxation
                  Overrides                = overrides }

    /// True iff there's a `KeepNullable` override for the given
    /// attribute in this intervention's override list.
    let shouldKeepNullable (attributeKey: SsKey) (config: NullabilityTighteningConfig) : bool =
        config.Overrides
        |> List.exists (fun o -> o.AttributeKey = attributeKey && o.Action = KeepNullable)


[<RequireQualifiedAccess>]
module UniqueIndexTighteningConfig =

    /// The default — both toggles off. V2's strict default: no
    /// interventions fire by default. The caller registering the
    /// intervention chooses the toggles explicitly.
    let empty : UniqueIndexTighteningConfig =
        { EnforceSingleColumnUnique = false
          EnforceMultiColumnUnique  = false }

    /// Construct a `UniqueIndexTighteningConfig`. No validation
    /// required — both fields are booleans with no out-of-range
    /// possibility.
    let create
        (enforceSingleColumnUnique: bool)
        (enforceMultiColumnUnique: bool)
        : UniqueIndexTighteningConfig =
        use _ = Bench.scope "ir.policy.uniqueIndex.create"
        { EnforceSingleColumnUnique = enforceSingleColumnUnique
          EnforceMultiColumnUnique  = enforceMultiColumnUnique }


[<RequireQualifiedAccess>]
module CategoricalUniquenessConfig =

    /// Construct a `CategoricalUniquenessConfig`. Validates
    /// `MinDistinctCountForUniqueness >= 0`. Caller chooses the
    /// floor explicitly per V2's strict-default discipline
    /// (DECISIONS 2026-05-09 — Tightening as a registry of named
    /// interventions).
    let create (minDistinctCountForUniqueness: int64) : Result<CategoricalUniquenessConfig> =
        use _ = Bench.scope "ir.policy.categoricalUniqueness.create"
        let errors =
            Validation.nonNegative
                "categoricalUniquenessConfig.minDistinctCountForUniqueness.negative"
                "MinDistinctCountForUniqueness must be non-negative."
                minDistinctCountForUniqueness
        if List.isEmpty errors then
            Result.success
                { MinDistinctCountForUniqueness = minDistinctCountForUniqueness }
        else
            Result.failure errors


[<RequireQualifiedAccess>]
module ForeignKeyTighteningConfig =

    /// Construct a `ForeignKeyTighteningConfig`. No validation
    /// required — every field is a boolean. Carries no defaults; the
    /// caller registering the intervention chooses every value
    /// explicitly per V2's strict-default discipline (DECISIONS
    /// 2026-05-09 — Tightening as a registry of named interventions).
    let create
        (enableCreation: bool)
        (allowCrossSchema: bool)
        (allowCrossCatalog: bool)
        (treatMissingDeleteRuleAsIgnore: bool)
        (allowNoCheckCreation: bool)
        : ForeignKeyTighteningConfig =
        use _ = Bench.scope "ir.policy.foreignKey.create"
        { EnableCreation                 = enableCreation
          AllowCrossSchema               = allowCrossSchema
          AllowCrossCatalog              = allowCrossCatalog
          TreatMissingDeleteRuleAsIgnore = treatMissingDeleteRuleAsIgnore
          AllowNoCheckCreation           = allowNoCheckCreation }


[<RequireQualifiedAccess>]
module TighteningIntervention =

    /// The intervention's stable identifier. Recorded in lineage events
    /// when the intervention fires. The pattern-match here is
    /// compiler-checked exhaustive; adding a new
    /// `TighteningIntervention` variant will fail this function until
    /// a new branch is added — a small algebraic reward for the closed
    /// DU.
    let id (intervention: TighteningIntervention) : string =
        match intervention with
        | Nullability           (id, _) -> id
        | UniqueIndex           (id, _) -> id
        | ForeignKey            (id, _) -> id
        | CategoricalUniqueness (id, _) -> id


[<RequireQualifiedAccess>]
module TighteningPolicy =

    /// The empty Tightening policy: zero interventions, zero decisions
    /// produced. V2's strict default — no system alterations unless the
    /// caller explicitly registers an intervention.
    let empty : TighteningPolicy = { Interventions = [] }

    /// True iff no interventions are registered.
    let isEmpty (policy: TighteningPolicy) : bool =
        List.isEmpty policy.Interventions

    /// Find a Nullability intervention's config by intervention id.
    /// **Variant-filtering combinator (chapter-Cluster-B compression;
    /// 2026-05-22).** Generic primitive for "extract every intervention
    /// matching a typed variant predicate." Each per-axis accessor
    /// (`nullabilityInterventions`, `uniqueIndexInterventions`, etc.)
    /// supplies the variant-specific extractor; the combinator threads
    /// the registration-order traversal. The same combinator handles
    /// the singular "find by id" pattern via `List.tryPick`.
    ///
    /// **Algebra.** This is the closed-DU filtering primitive — `'extract`
    /// chooses which variant + sub-fields to expose; the combinator
    /// drops non-matching interventions. Collapses 8 sites of identical
    /// `List.choose (fun i -> match i with | Variant (...) -> Some (...)
    /// | _ -> None)` to one-liners.
    let private filterIntervention
        (extract: TighteningIntervention -> 'a option)
        (policy: TighteningPolicy)
        : 'a list =
        policy.Interventions |> List.choose extract

    /// Sibling combinator for the "find first matching" case. Same
    /// extractor shape; uses `List.tryPick` for early termination.
    let private tryFindIntervention
        (extract: TighteningIntervention -> 'a option)
        (policy: TighteningPolicy)
        : 'a option =
        policy.Interventions |> List.tryPick extract

    // Per-variant extractors. Each names the variant + sub-fields the
    // accessor exposes; reuse across `tryFind*` (id-keyed) and
    // `*Interventions` (full list) accessors.
    let private extractNullability =
        function Nullability (id, cfg) -> Some (id, cfg) | _ -> None

    let private extractUniqueIndex =
        function UniqueIndex (id, cfg) -> Some (id, cfg) | _ -> None

    let private extractForeignKey =
        function ForeignKey (id, cfg) -> Some (id, cfg) | _ -> None

    let private extractCategoricalUniqueness =
        function CategoricalUniqueness (id, cfg) -> Some (id, cfg) | _ -> None

    // Slice 8 (2026-06-02): the four `tryFindX` accessors below share an
    // identical "extract → filter by id → project cfg" shape. The
    // function-composition (`>>`) form (`extractX >> Option.filter
    // (fst >> (=) id) >> Option.map snd`) was the most Haskell-leaning
    // code in the codebase; the named-match form below reads as the
    // domain intent ("if this is an X intervention with the matching
    // id, here's the cfg"). N=4 instances of the SAME shape earn the
    // shared helper `matchById`; per the two-consumer threshold codified
    // in `DECISIONS 2026-05-13`, four consumers of one shape is well
    // above the line.
    let private matchById
        (extractor: TighteningIntervention -> (string * 'cfg) option)
        (id: string)
        (candidate: TighteningIntervention)
        : 'cfg option =
        match extractor candidate with
        | Some (xid, cfg) when xid = id -> Some cfg
        | _ -> None

    /// Find a Nullability intervention's config by intervention id.
    /// Returns `None` if no Nullability intervention has that id (or
    /// if no Nullability intervention is registered at all).
    let tryFindNullability (id: string) (policy: TighteningPolicy) : NullabilityTighteningConfig option =
        policy |> tryFindIntervention (matchById extractNullability id)

    /// All registered Nullability interventions, paired with their ids,
    /// in registration order. Useful for passes that may apply more
    /// than one intervention (composing multiple nullability rules).
    let nullabilityInterventions (policy: TighteningPolicy) : (string * NullabilityTighteningConfig) list =
        policy |> filterIntervention extractNullability

    /// Find a UniqueIndex intervention's config by intervention id.
    /// Returns `None` if no UniqueIndex intervention has that id (or
    /// if no UniqueIndex intervention is registered at all).
    let tryFindUniqueIndex (id: string) (policy: TighteningPolicy) : UniqueIndexTighteningConfig option =
        policy |> tryFindIntervention (matchById extractUniqueIndex id)

    /// All registered UniqueIndex interventions, paired with their ids,
    /// in registration order.
    let uniqueIndexInterventions (policy: TighteningPolicy) : (string * UniqueIndexTighteningConfig) list =
        policy |> filterIntervention extractUniqueIndex

    /// Find a ForeignKey intervention's config by intervention id.
    /// Returns `None` if no ForeignKey intervention has that id (or
    /// if no ForeignKey intervention is registered at all).
    let tryFindForeignKey (id: string) (policy: TighteningPolicy) : ForeignKeyTighteningConfig option =
        policy |> tryFindIntervention (matchById extractForeignKey id)

    /// All registered ForeignKey interventions, paired with their ids,
    /// in registration order.
    let foreignKeyInterventions (policy: TighteningPolicy) : (string * ForeignKeyTighteningConfig) list =
        policy |> filterIntervention extractForeignKey

    /// Find a CategoricalUniqueness intervention's config by id.
    /// Returns `None` if no matching intervention is registered.
    let tryFindCategoricalUniqueness (id: string) (policy: TighteningPolicy) : CategoricalUniquenessConfig option =
        policy |> tryFindIntervention (matchById extractCategoricalUniqueness id)

    /// All registered CategoricalUniqueness interventions, paired with
    /// their ids, in registration order.
    let categoricalUniquenessInterventions (policy: TighteningPolicy) : (string * CategoricalUniquenessConfig) list =
        policy |> filterIntervention extractCategoricalUniqueness


[<RequireQualifiedAccess>]
module UserMatchingStrategy =

    /// V2's default strategy — `ByEmail`. Mirrors V1's
    /// `CaseInsensitiveEmail = 0` enum default
    /// (`UserMatchingOptions.cs:9`). The chapter-4.2 discovery
    /// pass against this default produces V1-equivalent matching
    /// for source users with non-blank emails.
    let empty : UserMatchingStrategy = ByEmail


[<RequireQualifiedAccess>]
module Policy =

    /// The empty policy: every axis at its empty default. A valid input
    /// for any pass; passes that consume Policy must produce sensible
    /// behavior on `Policy.empty`.
    let empty : Policy =
        { Selection    = SelectionPolicy.empty
          Emission     = EmissionPolicy.empty
          Insertion    = InsertionPolicy.empty
          Tightening   = TighteningPolicy.empty
          UserMatching = UserMatchingStrategy.empty }

    /// Merge two policies on a "right wins on non-default axes" basis
    /// — H-054's "applyDelta union for independent axes" semantics
    /// from HORIZON Cluster F.
    ///
    /// For each non-Tightening axis: if `b.axis` differs from
    /// `Policy.empty.axis`, use `b.axis`; otherwise preserve `a.axis`.
    /// For Tightening: interventions accumulate (`a @ b`) — both
    /// sides' interventions persist.
    ///
    /// **Algebraic properties** (tested in PolicySimulationTests):
    ///   - **Identity:** `merge empty p = p`; `merge p empty = p`.
    ///   - **Associativity:** `merge (merge a b) c = merge a (merge b c)`
    ///     across all axes (list-append associativity covers
    ///     Tightening).
    ///   - **Commutativity on disjoint axes:** `merge a b = merge b a`
    ///     whenever the set of non-default axes of `a` is disjoint
    ///     from the set of non-default axes of `b`. (The disjoint-axis
    ///     case includes the Tightening axis: both sides' Tightening
    ///     must be empty for commutativity to hold structurally.)
    ///
    /// **Distinction from `PolicyExpr.Seq`.** `Seq` is unconditionally
    /// right-wins on every non-Tightening axis (the right side's
    /// `Policy.empty.axis` clobbers the left's non-default value).
    /// `merge` preserves left-side non-defaults when right is at
    /// default — this is the "applyDelta" semantics HORIZON H-054
    /// asks for and was missing from the codebase before Cluster F's
    /// follow-up.
    let merge (a: Policy) (b: Policy) : Policy =
        let selection =
            if b.Selection = SelectionPolicy.empty then a.Selection else b.Selection
        let emission =
            if b.Emission = EmissionPolicy.empty then a.Emission else b.Emission
        let insertion =
            if b.Insertion = InsertionPolicy.empty then a.Insertion else b.Insertion
        let userMatching =
            if b.UserMatching = UserMatchingStrategy.empty then a.UserMatching else b.UserMatching
        let tightening =
            { Interventions = a.Tightening.Interventions @ b.Tightening.Interventions }
        { Selection    = selection
          Emission     = emission
          Insertion    = insertion
          Tightening   = tightening
          UserMatching = userMatching }


[<RequireQualifiedAccess>]
module ProjectionInput =

    /// Build a `ProjectionInput` whose Policy and Profile are the
    /// neutral defaults. Convenience for passes that consume only the
    /// catalog but need to flow through a triple-shaped pipeline.
    let ofCatalog (c: Catalog) : ProjectionInput =
        { Catalog = c; Policy = Policy.empty; Profile = Profile.empty }

    /// True iff the input is in the "no policy, no profile" minimal form.
    let isMinimal (input: ProjectionInput) : bool =
        input.Policy = Policy.empty && Profile.isEmpty input.Profile

