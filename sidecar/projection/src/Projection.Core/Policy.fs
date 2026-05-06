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


/// Tightening axis (A12 amended 2026-05-09). The fourth orthogonal Policy
/// axis. Tightening is genuinely orthogonal to Selection / Emission /
/// Insertion — it controls *what shape of constraint decisions* gets
/// produced, independent of which kinds participate, what artifacts are
/// emitted, or how data is applied. Surfaced under the
/// "IR grows under evidence" discipline by the `NullabilityEvaluator`
/// admire (ADMIRE.md, 2026-05-09); see DECISIONS for the worked example.
///
/// Three modes capture V1's `TighteningMode` enum verbatim. Mode names
/// are part of V2's vocabulary; future modes can be added when admire
/// passes surface them.
type TighteningMode =
    /// Conservative — only structural signals (PK, physical NOT NULL,
    /// logical Mandatory) drive tightening. FK and Unique signals are
    /// telemetry-only.
    | Cautious
    /// FK and Unique signals drive tightening only when their probe
    /// succeeded (RequiresEvidence: true).
    | EvidenceGated
    /// All signals drive tightening; missing probe outcomes flag
    /// remediation rather than withhold the signal.
    | Aggressive


/// One row of the override table. Keyed by attribute identity (per A4)
/// rather than by (module, entity, attribute) names — the V2 boundary
/// resolves V1's name-keyed overrides to SsKey before they reach the
/// pure core.
type TighteningOverride = {
    AttributeKey : SsKey
    Action       : OverrideAction
}

/// What an override does. V2 starts with the single action V1 actually
/// uses — keep the column nullable, bypassing the entire signal
/// hierarchy. Future actions extend the DU when admire passes surface
/// them (e.g., force-not-null, require-operator-approval); the new
/// variants land under "IR grows under evidence."
and OverrideAction =
    /// Force the column to remain nullable; bypass signal evaluation
    /// entirely. Operator-approved escape hatch; rationale recorded as
    /// `NullabilityOverride`.
    | KeepNullable


/// Tightening axis. Carries the policy inputs `NullabilityEvaluator`
/// (and any future tightening-flavored pass) needs.
type TighteningPolicy = {
    /// Which signal-set composition rule applies.
    Mode                       : TighteningMode
    /// Permitted null fraction — `allowed = RowCount * NullBudget`.
    /// Range [0, 1]; enforced at construction by `TighteningPolicy.create`.
    NullBudget                 : decimal
    /// In Cautious mode, may a column whose model declares mandatory
    /// be relaxed to nullable when profile evidence shows nulls? Default
    /// false — Cautious blocks relaxation by default, flagging
    /// remediation; setting true permits the relaxation.
    AllowCautiousRelaxation    : bool
    /// Operator-approved overrides. Each override bypasses the signal
    /// hierarchy entirely for its target attribute.
    Overrides                  : TighteningOverride list
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
module TighteningPolicy =

    let private nullBudgetOutOfRange =
        ValidationError.create
            "tighteningPolicy.nullBudget.outOfRange"
            "NullBudget must be in [0, 1]."

    /// The empty Tightening policy: Cautious mode, zero null budget,
    /// relaxation forbidden, no overrides. The default for use cases
    /// that consume no profile evidence — `NullabilityPass` running on
    /// `Policy.empty` is structurally valid; it produces conservative
    /// decisions (only PK / PhysicalNotNull / Mandatory signals fire,
    /// and Mandatory requires zero observed nulls).
    let empty : TighteningPolicy =
        { Mode                    = Cautious
          NullBudget              = 0.0m
          AllowCautiousRelaxation = false
          Overrides               = [] }

    /// Construct a `TighteningPolicy`. Validates `NullBudget` ∈ [0, 1].
    let create
        (mode: TighteningMode)
        (nullBudget: decimal)
        (allowCautiousRelaxation: bool)
        (overrides: TighteningOverride list)
        : Result<TighteningPolicy> =
        if nullBudget < 0.0m || nullBudget > 1.0m then
            Result.failureOf nullBudgetOutOfRange
        else
            Result.success
                { Mode                    = mode
                  NullBudget              = nullBudget
                  AllowCautiousRelaxation = allowCautiousRelaxation
                  Overrides               = overrides }

    /// True iff there's a `KeepNullable` override for the given attribute.
    let shouldKeepNullable (attributeKey: SsKey) (policy: TighteningPolicy) : bool =
        policy.Overrides
        |> List.exists (fun o -> o.AttributeKey = attributeKey && o.Action = KeepNullable)


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

