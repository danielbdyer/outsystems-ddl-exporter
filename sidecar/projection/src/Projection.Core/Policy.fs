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


/// Emission axis. Which artifact families a projection emits. The booleans
/// are deliberate; orthogonality of schema / data / diagnostics is the
/// algebra's commitment (decomposition Vector 2). When emission shapes
/// multiply, this record grows fields rather than packing flags into a DU.
type EmissionPolicy = {
    EmitSchema      : bool
    EmitData        : bool
    EmitDiagnostics : bool
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


/// The four-axis policy aggregate (A12 amended 2026-05-09). Each axis is
/// its own structured value; the four are composed in a single record.
/// Changing one axis does not constrain the others. `Policy.empty` is
/// the no-policy default — schema-only emission, every kind selected,
/// no insertion semantics, Cautious tightening with zero null budget —
/// and is a first-class input for use cases that need none of the axes.
type Policy = {
    Selection  : SelectionPolicy
    Emission   : EmissionPolicy
    Insertion  : InsertionPolicy
    Tightening : TighteningPolicy
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
        { Modules =
            c.Modules
            |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.filter (fun k -> isSelected k.SsKey policy) }) }


[<RequireQualifiedAccess>]
module EmissionPolicy =

    /// Default emission: schema only. The most common configuration and
    /// the one where the algebra's structural claims are sharpest.
    let empty : EmissionPolicy =
        { EmitSchema = true; EmitData = false; EmitDiagnostics = false }

    /// Schema artifacts only.
    let schemaOnly : EmissionPolicy = empty

    /// Data artifacts only — for full-export pipelines that keep schema
    /// emission elsewhere.
    let dataOnly : EmissionPolicy =
        { EmitSchema = false; EmitData = true; EmitDiagnostics = false }

    /// All three artifact families together.
    let combined : EmissionPolicy =
        { EmitSchema = true; EmitData = true; EmitDiagnostics = true }


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
        { EnforceSingleColumnUnique = enforceSingleColumnUnique
          EnforceMultiColumnUnique  = enforceMultiColumnUnique }


[<RequireQualifiedAccess>]
module CategoricalUniquenessConfig =

    let private negativeFloor =
        ValidationError.create
            "categoricalUniquenessConfig.minDistinctCountForUniqueness.negative"
            "MinDistinctCountForUniqueness must be non-negative."

    /// Construct a `CategoricalUniquenessConfig`. Validates
    /// `MinDistinctCountForUniqueness >= 0`. Caller chooses the
    /// floor explicitly per V2's strict-default discipline
    /// (DECISIONS 2026-05-09 — Tightening as a registry of named
    /// interventions).
    let create (minDistinctCountForUniqueness: int64) : Result<CategoricalUniquenessConfig> =
        if minDistinctCountForUniqueness < 0L then
            Result.failureOf negativeFloor
        else
            Result.success
                { MinDistinctCountForUniqueness = minDistinctCountForUniqueness }


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
    /// Returns `None` if no Nullability intervention has that id (or
    /// if no Nullability intervention is registered at all).
    let tryFindNullability (id: string) (policy: TighteningPolicy) : NullabilityTighteningConfig option =
        policy.Interventions
        |> List.tryPick (fun intervention ->
            match intervention with
            | Nullability (i, cfg) when i = id -> Some cfg
            | _                                -> None)

    /// All registered Nullability interventions, paired with their ids,
    /// in registration order. Useful for passes that may apply more
    /// than one intervention (composing multiple nullability rules).
    let nullabilityInterventions (policy: TighteningPolicy) : (string * NullabilityTighteningConfig) list =
        policy.Interventions
        |> List.choose (fun intervention ->
            match intervention with
            | Nullability (id, cfg) -> Some (id, cfg)
            | _                     -> None)

    /// Find a UniqueIndex intervention's config by intervention id.
    /// Returns `None` if no UniqueIndex intervention has that id (or
    /// if no UniqueIndex intervention is registered at all).
    let tryFindUniqueIndex (id: string) (policy: TighteningPolicy) : UniqueIndexTighteningConfig option =
        policy.Interventions
        |> List.tryPick (fun intervention ->
            match intervention with
            | UniqueIndex (i, cfg) when i = id -> Some cfg
            | _                                -> None)

    /// All registered UniqueIndex interventions, paired with their ids,
    /// in registration order.
    let uniqueIndexInterventions (policy: TighteningPolicy) : (string * UniqueIndexTighteningConfig) list =
        policy.Interventions
        |> List.choose (fun intervention ->
            match intervention with
            | UniqueIndex (id, cfg) -> Some (id, cfg)
            | _                     -> None)

    /// Find a ForeignKey intervention's config by intervention id.
    /// Returns `None` if no ForeignKey intervention has that id (or
    /// if no ForeignKey intervention is registered at all).
    let tryFindForeignKey (id: string) (policy: TighteningPolicy) : ForeignKeyTighteningConfig option =
        policy.Interventions
        |> List.tryPick (fun intervention ->
            match intervention with
            | ForeignKey (i, cfg) when i = id -> Some cfg
            | _                               -> None)

    /// All registered ForeignKey interventions, paired with their ids,
    /// in registration order.
    let foreignKeyInterventions (policy: TighteningPolicy) : (string * ForeignKeyTighteningConfig) list =
        policy.Interventions
        |> List.choose (fun intervention ->
            match intervention with
            | ForeignKey (id, cfg) -> Some (id, cfg)
            | _                    -> None)

    /// Find a CategoricalUniqueness intervention's config by id.
    /// Returns `None` if no matching intervention is registered.
    let tryFindCategoricalUniqueness (id: string) (policy: TighteningPolicy) : CategoricalUniquenessConfig option =
        policy.Interventions
        |> List.tryPick (fun intervention ->
            match intervention with
            | CategoricalUniqueness (i, cfg) when i = id -> Some cfg
            | _                                          -> None)

    /// All registered CategoricalUniqueness interventions, paired with
    /// their ids, in registration order.
    let categoricalUniquenessInterventions (policy: TighteningPolicy) : (string * CategoricalUniquenessConfig) list =
        policy.Interventions
        |> List.choose (fun intervention ->
            match intervention with
            | CategoricalUniqueness (id, cfg) -> Some (id, cfg)
            | _                               -> None)


[<RequireQualifiedAccess>]
module Policy =

    /// The empty policy: every axis at its empty default. A valid input
    /// for any pass; passes that consume Policy must produce sensible
    /// behavior on `Policy.empty`.
    let empty : Policy =
        { Selection  = SelectionPolicy.empty
          Emission   = EmissionPolicy.empty
          Insertion  = InsertionPolicy.empty
          Tightening = TighteningPolicy.empty }


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

