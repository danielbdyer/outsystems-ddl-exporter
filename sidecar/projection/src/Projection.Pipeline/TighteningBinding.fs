namespace Projection.Pipeline

open Projection.Core

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

    let private bindError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.tightening.%s" code) message

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
            // Try logical: Module.Kind.Attribute
            let logicalHit =
                catalog.Modules
                |> List.tryPick (fun m ->
                    if Name.value m.Name = target1 then
                        m.Kinds
                        |> List.tryPick (fun k ->
                            if Name.value k.Name = target2 then
                                k.Attributes
                                |> List.tryFind (fun a -> Name.value a.Name = target3)
                            else None)
                    else None)
            match logicalHit with
            | Some attr -> Result.success attr.SsKey
            | None ->
                // Try physical: Schema.Table.Column
                let physicalHit =
                    catalog.Modules
                    |> List.tryPick (fun m ->
                        m.Kinds
                        |> List.tryPick (fun k ->
                            if k.Physical.Schema = target1 && k.Physical.Table = target2 then
                                k.Attributes
                                |> List.tryFind (fun a -> a.Column.ColumnName = target3)
                            else None))
                match physicalHit with
                | Some attr -> Result.success attr.SsKey
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
        match resolveAttributeRef catalog entry.AttributeRef with
        | Error es -> Error es
        | Ok ssKey ->
            match parseOverrideAction entry.Action with
            | Error es -> Error es
            | Ok action ->
                Result.success {
                    AttributeKey = ssKey
                    Action       = action
                }

    /// Build a `TighteningIntervention.Nullability` from a config
    /// entry. Defaults: `NullBudget = 0.0` (strict), `Allow
    /// MandatoryRelaxation = false` (V1 cautious default). Overrides
    /// resolve against the catalog.
    let private bindNullability
        (catalog: Catalog)
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        let nullBudget = defaultArg entry.NullBudget 0m
        let allowMand = defaultArg entry.AllowMandatoryRelaxation false
        let overridesR =
            entry.NullabilityOverrides
            |> List.map (bindOverride catalog)
            |> Result.aggregate
        match overridesR with
        | Error es -> Error es
        | Ok overrides ->
            match NullabilityTighteningConfig.create nullBudget allowMand overrides with
            | Error es -> Error es
            | Ok config ->
                Result.success (TighteningIntervention.Nullability (entry.Id, config))

    let private bindUniqueIndex
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        let config : UniqueIndexTighteningConfig = {
            EnforceSingleColumnUnique = defaultArg entry.EnforceSingleColumnUnique true
            EnforceMultiColumnUnique  = defaultArg entry.EnforceMultiColumnUnique true
        }
        Result.success (TighteningIntervention.UniqueIndex (entry.Id, config))

    let private bindForeignKey
        (entry: Config.TighteningInterventionEntry)
        : Result<TighteningIntervention> =
        let config : ForeignKeyTighteningConfig = {
            EnableCreation                 = defaultArg entry.EnableCreation true
            AllowCrossSchema               = defaultArg entry.AllowCrossSchema true
            AllowCrossCatalog              = defaultArg entry.AllowCrossCatalog false
            TreatMissingDeleteRuleAsIgnore = defaultArg entry.TreatMissingDeleteRuleAsIgnore false
            AllowNoCheckCreation           = defaultArg entry.AllowNoCheckCreation false
        }
        Result.success (TighteningIntervention.ForeignKey (entry.Id, config))

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
        | "foreignKey"            -> bindForeignKey entry
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
            s.Interventions
            |> List.map (bindEntry catalog)
            |> Result.aggregate
            |> Result.map (fun interventions -> { Interventions = interventions })
