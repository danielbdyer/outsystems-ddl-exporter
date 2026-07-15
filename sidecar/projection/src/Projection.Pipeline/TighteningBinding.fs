namespace Projection.Pipeline

open Projection.Core
open FsToolkit.ErrorHandling

/// Chapter C slice C.1 — operator config → `TighteningPolicy` binder.
/// Converts `Config.TighteningSection` (textual operator surface)
/// into the typed runtime `TighteningPolicy` consumed by V2's
/// tightening passes (`NullabilityPass` / `UniqueIndexPass` /
/// `ForeignKeyPass` / `CategoricalUniquenessPass`).
///
/// Per the chapter C strategic exploration entry: the
/// `Policy.TighteningPolicy` + `TighteningOverride` substrate already
/// exists structurally in `Projection.Core/Policy.fs`. This slice
/// adds the config-binding layer mapping operator JSON entries to
/// registered interventions. Pillar 9 classification: every binding
/// outcome is `OperatorIntent of Tightening` per the registered-
/// transform framing.
///
/// **Attribute-reference resolution.** Operators name attributes by
/// either the **logical** path (`"Module.Entity.Attribute"`) or the
/// **physical** path (`"Schema.Table.Column"`). The binder resolves
/// each ref against the loaded `Catalog` at bind time — the textual
/// reference becomes a typed `SsKey` before passes consume it. When
/// the ref matches neither, the binder returns a structured
/// `ValidationError` naming the unresolvable input.
[<RequireQualifiedAccess>]
module TighteningBinding =

    let private bindError = Binding.error ConfigAxis.Tightening

    /// Resolve an operator-supplied attribute reference against the
    /// loaded catalog. Tries logical-name match first (`Module.Kind
    /// .Attribute`), then physical-name match (`Schema.Table.Column`).
    /// Returns the matched attribute's `SsKey` on success.
    let private resolveAttributeRef
        (catalog: Catalog)
        (ref: string)
        : Result<SsKey> =
        let parts = ref.Split([| '.' |])
        if parts.Length <> 3 then
            Result.failureOf (
                bindError
                    "overrideRef.shape"
                    (sprintf
                        "Override attributeRef '%s' must be a 3-part dotted name (Module.Entity.Attribute or Schema.Table.Column)."
                        ref))
        else
            let target1 = parts.[0]
            let target2 = parts.[1]
            let target3 = parts.[2]
            // Logical (Module.Entity.Attribute) first, then physical
            // (Schema.Table.Column) — both via the shared CatalogResolution
            // lookups (B8 centralization; the binder keeps its own error).
            match CatalogResolution.tryAttributeByLogical catalog target1 target2 target3 with
            | Some key -> Result.success key
            | None ->
                match CatalogResolution.tryAttributeByPhysical catalog target1 target2 target3 with
                | Some key -> Result.success key
                | None ->
                    Result.failureOf (
                        bindError
                            "overrideRef.unresolved"
                            (sprintf
                                "Override attributeRef '%s' did not match any catalog attribute (tried logical Module.Entity.Attribute and physical Schema.Table.Column forms)."
                                ref))

    let private parseOverrideAction
        (raw: string)
        : Result<OverrideAction> =
        match raw with
        | "keepNullable" -> Result.success KeepNullable
        | other ->
            Result.failureOf (
                bindError
                    "overrideAction.unknown"
                    (sprintf "Override action '%s' is unknown. Valid: 'keepNullable'." other))

    let private bindOverride
        (catalog: Catalog)
        (entry: Config.TighteningAttributeOverride)
        : Result<TighteningOverride> =
        result {
            let! ssKey  = resolveAttributeRef catalog entry.AttributeRef
            let! action = parseOverrideAction entry.Action
            return {
                AttributeKey = ssKey
                Action       = action
            }
        }

    /// Build the RELAXATION-ONLY `TighteningIntervention.Nullability`
    /// from a config entry (DECISIONS 2026-07-15, the estate A6
    /// amendment — amends 2026-06-22). Only budget-less entries reach
    /// this binder (`fromConfig` drops the coercion direction), so the
    /// bound intervention's ONLY reachable acts are its resolved
    /// `keepNullable` overrides — they relax emission below the declared
    /// shape (`DecisionOverlay.KeepNullable`) until the reopen probe
    /// retires them. `allowMandatoryRelaxation` binds verbatim for the
    /// policy fingerprint; under RelaxationOnly no budget hierarchy runs
    /// to consult it.
    let private bindNullability
        (catalog: Catalog)
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        let allowMand = defaultArg entry.AllowMandatoryRelaxation false
        result {
            let! overrides =
                entry.NullabilityOverrides
                |> List.map (bindOverride catalog)
                |> Result.aggregate
            let config = NullabilityTighteningConfig.relaxationOnly allowMand overrides
            return TighteningIntervention.Nullability (entry.Id, config)
        }

    let private bindUniqueIndex
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        let config : UniqueIndexTighteningConfig = {
            EnforceSingleColumnUnique = defaultArg entry.EnforceSingleColumnUnique true
            EnforceMultiColumnUnique  = defaultArg entry.EnforceMultiColumnUnique true
        }
        Result.success (TighteningIntervention.UniqueIndex (entry.Id, config))

    let private parseReferenceOverrideAction
        (raw: string)
        : Result<ForeignKeyOverrideAction> =
        match raw with
        | "keepUntracked" -> Result.success KeepUntracked
        | other ->
            Result.failureOf (
                bindError
                    "referenceOverrideAction.unknown"
                    (sprintf "Reference override action '%s' is unknown. Valid: 'keepUntracked'." other))

    /// Resolve a `referenceRef` (the relationship named by its ANCHORING
    /// attribute, logical or physical form) to the reference's `SsKey`:
    /// the attribute resolves first, then the kind carrying it yields
    /// the relationship it anchors. An attribute that anchors no
    /// relationship is a structured refusal — never a silent drop.
    let private bindReferenceOverride
        (catalog: Catalog)
        (entry: Config.TighteningReferenceOverride)
        : Result<ForeignKeyOverride> =
        result {
            let! attrKey = resolveAttributeRef catalog entry.ReferenceRef
            let! action = parseReferenceOverrideAction entry.Action
            let referenceKey =
                Catalog.allKinds catalog
                |> List.tryPick (fun k ->
                    k.References
                    |> List.tryFind (fun r -> r.SourceAttribute = attrKey)
                    |> Option.map (fun r -> r.SsKey))
            match referenceKey with
            | Some key -> return { ReferenceKey = key; Action = action }
            | None ->
                return!
                    Result.failureOf (
                        bindError
                            "referenceRef.noReference"
                            (sprintf
                                "Reference override '%s' resolves to an attribute that anchors no relationship."
                                entry.ReferenceRef))
        }

    /// Build a `TighteningIntervention.ForeignKey` from a config entry.
    /// The DIRECTION is the entry's shape (DECISIONS 2026-07-15, the
    /// estate A6 amendment): an entry carrying `referenceOverrides` and
    /// NONE of the five V1 toggles is the SURGICAL relaxation-only form
    /// (only the named references move; everything else carries the
    /// declared shape) — the form the estate overlay emits. Any toggle
    /// present keeps the evidence-driven hierarchy, with the overrides
    /// still consulted first (absolute in both directions).
    let private bindForeignKey
        (catalog: Catalog)
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        result {
            let! overrides =
                entry.ForeignKeyOverrides
                |> List.map (bindReferenceOverride catalog)
                |> Result.aggregate
            let togglesAbsent =
                entry.EnableCreation.IsNone
                && entry.AllowCrossSchema.IsNone
                && entry.AllowCrossCatalog.IsNone
                && entry.TreatMissingDeleteRuleAsIgnore.IsNone
                && entry.AllowNoCheckCreation.IsNone
            let config : ForeignKeyTighteningConfig =
                if togglesAbsent && not (List.isEmpty overrides) then
                    ForeignKeyTighteningConfig.relaxationOnly overrides
                else
                    { EnableCreation                 = defaultArg entry.EnableCreation true
                      AllowCrossSchema               = defaultArg entry.AllowCrossSchema true
                      AllowCrossCatalog              = defaultArg entry.AllowCrossCatalog false
                      TreatMissingDeleteRuleAsIgnore = defaultArg entry.TreatMissingDeleteRuleAsIgnore false
                      AllowNoCheckCreation           = defaultArg entry.AllowNoCheckCreation false
                      Overrides                      = overrides
                      Direction                      = TighteningDirection.EvidenceDriven }
            return TighteningIntervention.ForeignKey (entry.Id, config)
        }

    let private bindCategoricalUniqueness
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        let minDistinct = defaultArg entry.MinDistinctCountForUniqueness 100L
        let config : CategoricalUniquenessConfig = {
            MinDistinctCountForUniqueness = minDistinct
        }
        Result.success (TighteningIntervention.CategoricalUniqueness (entry.Id, config))

    /// Dispatch on `entry.Kind` to the per-variant binder. Unknown
    /// kinds surface as `ValidationError`.
    let private bindEntry
        (catalog: Catalog)
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        match entry.Kind with
        | "nullability"           -> bindNullability catalog entry
        | "uniqueIndex"           -> bindUniqueIndex entry
        | "foreignKey"            -> bindForeignKey catalog entry
        | "categoricalUniqueness" -> bindCategoricalUniqueness entry
        | other ->
            Result.failureOf (
                bindError
                    "intervention.kindUnknown"
                    (sprintf
                        "Tightening intervention kind '%s' is unknown. Valid: nullability / uniqueIndex / foreignKey / categoricalUniqueness."
                        other))

    /// Convert an operator config's `TighteningSection` into a typed
    /// `TighteningPolicy`. Resolves per-attribute override refs against
    /// the loaded catalog. `None` (no tightening section in config) →
    /// `TighteningPolicy.empty` (V2's strict default: no interventions).
    let fromConfig
        (catalog: Catalog)
        (section: Config.TighteningSection option)
        : Result<TighteningPolicy> =
        match section with
        | None -> Result.success TighteningPolicy.empty
        | Some s ->
            // DECISIONS 2026-06-22 (config-driven nullable→NOT NULL coercion
            // disabled — the team's declared nullability is authoritative, not
            // the tool's), AS AMENDED 2026-07-15 (the estate chapter's A6
            // relaxation-direction re-opening): a `kind:"nullability"` entry
            // that names a `nullBudget` is the COERCION direction and stays
            // dropped (not refused, so an existing config does not hard-fail;
            // null-density stays a profiling statistic only). An entry WITHOUT
            // a budget binds as a RELAXATION-ONLY intervention — its
            // `keepNullable` overrides (and nothing else) act, relaxing
            // emission below the declared shape. That is the estate overlay's
            // nullability arm, and it closes the A44 gap the 2026-06-22 drop
            // opened: `overrides` was expressible-but-inert; now every
            // expressible key binds and reaches emission. The sibling
            // blessing surface, `tighteningRelaxations` (F7), is DIFFERENT
            // machinery — the migrate face's data-compat gate honoring, scoped
            // to tightening-work violations — and stays untouched.
            s.Interventions
            |> List.filter (fun e -> not (e.Kind = "nullability" && e.NullBudget.IsSome))
            |> List.map (bindEntry catalog)
            |> Result.aggregate
            |> Result.map (fun interventions -> { Interventions = interventions })
