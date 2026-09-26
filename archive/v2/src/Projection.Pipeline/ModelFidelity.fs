namespace Projection.Pipeline

// LINT-ALLOW-FILE: the rolled-up text renderer + JSON codec compose operator-facing
//   report prose (THE_VOICE twelve-rule register) and structured JSON at a terminal
//   reporting boundary; the per-section accumulators are function-local. The
//   aggregation core (the violation/candidate/divergence computations) is pure and
//   carries no I/O.

open System.Text.Json.Nodes
open Projection.Core

/// The Model Fidelity Report — a per-run, rolled-up (count-first, drill-down
/// beneath) account of the distance between the DECLARED model and the SOURCE
/// reality the live profiler observed. It aggregates the adapter's profiling
/// evidence (`Profile.AttributeRealities` / `Columns` / `ForeignKeys`) against
/// the catalog's declarations into three sections:
///
///   - **Data violations** (the headline) — where the source data contradicts
///     a declared constraint: a NOT-NULL / PK attribute carrying NULLs, a
///     UNIQUE / PK attribute carrying duplicates, an FK whose source rows
///     orphan, a value overflowing its declared length / type.
///   - **Accepted divergences** — the `ToleratedDivergence` set the run's
///     round-trip canary actually accepted (the per-run tolerance residual).
///   - **Uniqueness candidates** (advisory) — the distribution-driven
///     `SuggestUnique` outcomes the `CategoricalUniquenessPass` produced
///     (closing NM-35 by giving those suggestions their consumer).
///
/// Pipeline-layer because it aggregates adapter `LiveProfiler` evidence
/// (Profile) against Core declarations (Catalog) — neither side owns the
/// crossing. The aggregation is pure; the renderer + codec are terminal-text.
[<RequireQualifiedAccess>]
module ModelFidelity =

    // -- Identity → operator copy ------------------------------------------
    //
    // THE_VOICE §2.1: the engine's `SsKey` resolves to the table / column NAME
    // the operator reads. The report never shows an `OS_KIND_*` / `SsKey` root.

    /// The operator-facing reference for one offending attribute — `Entity.Col`,
    /// the table-then-column form the operator reads (THE_VOICE §2.1; never the
    /// `SsKey` root, never `OS_ATTR_*`).
    type EntityColumn =
        {
            Entity : string
            Column : string
        }

    /// Render an `EntityColumn` as `Entity.Column` (the operator-facing token).
    let entityColumnText (ec: EntityColumn) : string =
        System.String.Concat(ec.Entity, ".", ec.Column)

    // -- The recommendation layer (evidence → interpretation → move) --------
    //
    // 2026-07-18 (the fidelity-recommendation program): a violation is a
    // FACT; the operator needs the DECISION it opens. Each violation carries
    // a typed recommendation — the interpretation its evidence supports, the
    // next move as a bare imperative, and the lever the move ends on (the
    // prepared remediation block, a config edit in the overlay vocabulary, a
    // model correction, or a constraint review). The copy holds THE_VOICE's
    // twelve rules: stative interpretation, imperative move, no pronouns,
    // hedged only where genuinely interpretive (rule 4). The platform facts
    // the interpretations rest on are the OutSystems 11 references: a
    // mandatory attribute is validated at run time only (database
    // constraints exist for primary keys and references); an Ignore delete
    // rule creates no foreign-key constraint; Decimal storage takes its
    // precision and scale from the Length and Decimals properties.

    /// The lever a recommendation's move ends on. `EditConfig` reuses the
    /// Core `SuggestedConfig` triple (path + serialized value + note) — the
    /// same §12 shape the overlay and `suggest-config.json` speak, so the
    /// config-edit vocabulary stays one vocabulary across artifacts.
    type RecommendationLever =
        /// Review the prepared per-finding block in `manifest.remediation.sql`.
        | ReviewRemediation
        /// Merge a config edit (the overlay vocabulary: the intervention
        /// entry the tightening binder accepts, at the interventions path).
        | EditConfig of SuggestedConfig
        /// Correct the declaration in the model (the metadata side).
        | ReviewModel
        /// Review the declared key itself before touching any data.
        | ReviewConstraint

    /// One violation's recommendation: what the evidence most plausibly
    /// means, the next move, and the lever it ends on.
    type Recommendation =
        {
            /// The interpretation the evidence supports — stative, grounded.
            Interpretation : string
            /// The next move — a bare imperative (THE_VOICE rule 2).
            Action         : string
            /// The lever the move ends on.
            Lever          : RecommendationLever
        }

    /// The fix-vs-relax threshold for a data violation: at or below the band
    /// the prepared repair leads (backfill / cleanup, then the declaration
    /// deploys); above it the interim relaxation leads (`keepNullable` /
    /// `keepUntracked`) until the data clears. ONE source of truth for both
    /// consumers — the per-violation recommendation here and the estate
    /// board's lane split (`Estate.dataFindingOf`, which aliases this);
    /// `readiness.estate.repairBand` overrides at the estate surface.
    let repairBandDefault : int64 = 100_000L

    // -- Data violations (the headline section) ----------------------------

    /// The four declared-constraint axes the source data can contradict. A
    /// closed DU so the rollup stays total over the violation vocabulary — a
    /// new axis fires the exhaustiveness check at every match site.
    type ViolationKind =
        /// A NOT-NULL (or PK) attribute whose profiled `NullCount > 0`.
        | NotNullButNullsPresent of nullCount: int64
        /// A UNIQUE-index / PK attribute whose profiled values carry duplicates.
        | UniqueButDuplicatesPresent
        /// A foreign key whose source rows reference absent target rows.
        | ForeignKeyOrphans of orphanCount: int64
        /// A value exceeding its declared length or overflowing its declared type.
        | LengthOrTypeOverflow of observed: string * declared: string

    /// One declared-constraint contradiction the source data carries.
    /// Identity-keyed (per A4) but carries the operator-facing names so the
    /// renderer never re-resolves.
    type DataViolation =
        {
            Reference  : EntityColumn
            Kind       : ViolationKind
            /// The decision this violation opens (the recommendation layer,
            /// 2026-07-18). Minted at `compose` from the violation's own
            /// evidence + catalog context; `None` only on a legacy
            /// `fidelity.json` parsed without recommendation nodes.
            Recommendation : Recommendation option
        }

    /// The four-way rollup category a violation rolls up into (the renderer's
    /// top-level lines).
    type ViolationCategory =
        | NotNullCategory
        | UniqueCategory
        | OrphanCategory
        | OverflowCategory

    let categoryOf (v: DataViolation) : ViolationCategory =
        match v.Kind with
        | NotNullButNullsPresent _    -> NotNullCategory
        | UniqueButDuplicatesPresent  -> UniqueCategory
        | ForeignKeyOrphans _         -> OrphanCategory
        | LengthOrTypeOverflow _      -> OverflowCategory

    let private categoryOrdinal (c: ViolationCategory) : int =
        match c with
        | NotNullCategory  -> 0
        | UniqueCategory   -> 1
        | OrphanCategory   -> 2
        | OverflowCategory -> 3

    // -- Uniqueness candidates (advisory section) --------------------------

    /// One advisory uniqueness candidate — the `CategoricalUniquenessPass`
    /// suggested this attribute as a natural key because every observed value
    /// was distinct.
    type UniquenessCandidate =
        {
            Reference         : EntityColumn
            DistinctCount     : int64
            TotalObservations : int64
        }

    /// The distinct fraction (0..1) as observed — `None` when no observations
    /// were recorded (degenerate; the candidate would not have fired).
    let candidateDistinctFraction (c: UniquenessCandidate) : decimal option =
        if c.TotalObservations = 0L then None
        else Some (decimal c.DistinctCount / decimal c.TotalObservations)

    // -- Accepted divergences (the per-run tolerance residual) -------------

    /// One tolerated divergence the run's canary actually accepted this run.
    type AcceptedDivergence =
        {
            Divergence : ToleratedDivergence
        }

    // -- The report record -------------------------------------------------

    /// The assembled per-run fidelity report. Count-first: the renderer leads
    /// with totals, then the per-entity drill-down beneath. The estate framing
    /// (`Estate` / `ModuleCount` / `EntityCount`) is the masthead THE_VOICE §12
    /// keeps a constant size while only the numbers grow.
    type ModelFidelityReport =
        {
            Estate               : string
            ModuleCount          : int
            EntityCount          : int
            DataViolations       : DataViolation list
            AcceptedDivergences  : AcceptedDivergence list
            UniquenessCandidates : UniquenessCandidate list
        }

    /// The empty report for an estate with no profiled evidence (the honest
    /// `Profile.empty` base case — a pure emit with no live source observes no
    /// reality, so it asserts no violations).
    let empty (estate: string) : ModelFidelityReport =
        { Estate               = estate
          ModuleCount          = 0
          EntityCount          = 0
          DataViolations       = []
          AcceptedDivergences  = []
          UniquenessCandidates = [] }

    /// Stamp the run's resolved tolerance residual onto the report's accepted-
    /// divergences section. The report is computed at emit time (before any
    /// round-trip); the canary's matched-tolerance set is resolved later, so a
    /// caller that has run a round-trip threads the residual through here.
    ///
    /// FLAGGED (the tolerance-residual canary coupling): the full-export / migrate
    /// emit path has no round-trip canary of its own — the canary is the separate
    /// `check` verb, and the store leg records `Tolerance.strict` because no
    /// production caller resolves a non-strict residual yet (see
    /// `Pipeline.runStoreLeg`'s standing FLAG + `Episode.withProvenance`). This
    /// updater is the clean hook: a future canary-coupled run resolves its
    /// matched-tolerance set and stamps it here, and the section surfaces it —
    /// without re-touching the aggregation. Until then the section is honestly
    /// empty (a pure emit compares nothing).
    let withAcceptedDivergences
        (divergences: ToleratedDivergence list)
        (report: ModelFidelityReport)
        : ModelFidelityReport =
        { report with
            AcceptedDivergences = divergences |> List.map (fun d -> { Divergence = d }) }

    // ----------------------------------------------------------------------
    // Aggregation — from a declared Catalog × the profiled evidence.
    // ----------------------------------------------------------------------

    /// Every attribute whose per-column duplicate evidence WITNESSES a
    /// uniqueness violation: the sole column of a single-column UNIQUE index /
    /// PK, or the sole PK attribute of its kind. **Single-column only, by
    /// altitude** (2026-07-18): a composite unique declaration constrains the
    /// TUPLE — each member column can carry duplicates while the tuple stays
    /// distinct — so per-column `HasDuplicates` can never witness a composite
    /// violation, and reading it as one flooded real estates with findings
    /// rooted in the detector, not the data (the column-by-column reading of
    /// a composite business key). Tuple-grain evidence for DECLARED-unique
    /// composite indexes is a named follow-on (`deriveCompositeUniqueCandidates`
    /// probes non-unique candidates only today).
    let private uniqueBackedAttributeKeys (kind: Kind) : Set<SsKey> =
        let fromIndexes =
            kind.Indexes
            |> List.filter (fun ix ->
                (IndexUniqueness.isUnique ix.Uniqueness || IndexUniqueness.isPrimaryKey ix.Uniqueness)
                && List.length ix.Columns = 1)
            |> List.collect (fun ix -> ix.Columns |> List.map (fun ic -> ic.Attribute))
            |> Set.ofList
        // A SOLE PK attribute is unique-backed even absent an explicit index
        // row; the attributes of a composite PK are not singly backed.
        let fromPk =
            match kind.Attributes |> List.filter (fun a -> a.IsPrimaryKey) with
            | [ sole ] -> Set.singleton sole.SsKey
            | _        -> Set.empty
        Set.union fromIndexes fromPk

    let private entityColumnOf (kind: Kind) (attr: Attribute) : EntityColumn =
        { Entity = Name.value kind.Name
          Column = Name.value attr.Name }

    let private realityFor (attrKey: SsKey) (profile: Profile) : AttributeReality option =
        profile.AttributeRealities |> List.tryFind (fun r -> r.AttributeKey = attrKey)

    // -- Recommendation minting (per-category, evidence-driven) -------------

    let private humaneBand (band: int64) : string =
        band.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    /// The 3-part `Module.Entity.Attribute` reference the tightening binder
    /// resolves (`TighteningBinding.resolveAttributeRef`) — the overlay
    /// vocabulary's addressing form.
    let private attributeRef3 (moduleName: string) (ec: EntityColumn) : string =
        System.String.Concat(moduleName, ".", ec.Entity, ".", ec.Column) // LINT-ALLOW: the tightening binder's 3-part dotted addressing form, minted once at the recommendation boundary from validated name segments

    /// The `keepNullable` intervention entry as the overlay vocabulary's
    /// suggested edit — the EXACT shape `TighteningBinding` binds (A44:
    /// every emitted key binds and reaches emission), at the interventions
    /// path. Mirrors `EstateOverlayEmitter.entryOf`'s nullability arm.
    let private keepNullableEdit (moduleName: string) (ec: EntityColumn) (nullCount: int64) : SuggestedConfig =
        let value = JsonObject()
        value.["id"] <- JsonValue.Create (System.String.Concat("fidelity.notNull:", entityColumnText ec)) // LINT-ALLOW: stable intervention id token — the fidelity category discriminator + the operator-facing reference, joined once at the config-edit boundary
        value.["kind"] <- JsonValue.Create "nullability"
        let overrides = JsonArray()
        let o = JsonObject()
        o.["attributeRef"] <- JsonValue.Create (attributeRef3 moduleName ec)
        o.["action"] <- JsonValue.Create "keepNullable"
        overrides.Add o
        value.["overrides"] <- overrides
        { Path  = "$.policy.tightening.interventions[+]"
          Value = value.ToJsonString()
          Note  =
            Some (
                sprintf "Interim relaxation; the NULL count that forced it: %s. Retire the entry once the backfill lands."
                    (humaneBand nullCount)) }

    /// The `keepUntracked` intervention entry — the overlay vocabulary's
    /// foreign-key arm, same contract as `keepNullableEdit`.
    let private keepUntrackedEdit (moduleName: string) (ec: EntityColumn) (orphanCount: int64) : SuggestedConfig =
        let value = JsonObject()
        value.["id"] <- JsonValue.Create (System.String.Concat("fidelity.orphans:", entityColumnText ec)) // LINT-ALLOW: stable intervention id token — the fidelity category discriminator + the operator-facing reference, joined once at the config-edit boundary
        value.["kind"] <- JsonValue.Create "foreignKey"
        let overrides = JsonArray()
        let o = JsonObject()
        o.["referenceRef"] <- JsonValue.Create (attributeRef3 moduleName ec)
        o.["action"] <- JsonValue.Create "keepUntracked"
        overrides.Add o
        value.["referenceOverrides"] <- overrides
        { Path  = "$.policy.tightening.interventions[+]"
          Value = value.ToJsonString()
          Note  =
            Some (
                sprintf "Interim relaxation; the orphan count that forced it: %s. Retire the entry once the references clear."
                    (humaneBand orphanCount)) }

    /// The NOT-NULL recommendation: the platform validates a mandatory
    /// attribute at run time only (no database constraint), so NULL rows
    /// under a mandatory declaration are a tightening decision, not
    /// corruption. At or below the band the backfill leads; above it the
    /// interim `keepNullable` relaxation leads.
    let private recommendNotNull
        (band: int64)
        (moduleName: string)
        (ec: EntityColumn)
        (nullCount: int64)
        : Recommendation =
        let interpretation =
            "A mandatory attribute is validated by the platform at run time only; the deployed column allows NULL, and the NOT NULL declaration cannot deploy while the rows remain."
        if nullCount > band then
            { Interpretation = interpretation
              Action =
                sprintf "Keep the column nullable until the backfill — %s NULL row(s) exceed the repair band (%s). Merge the keepNullable entry for %s."
                    (humaneBand nullCount) (humaneBand band) (attributeRef3 moduleName ec)
              Lever = EditConfig (keepNullableEdit moduleName ec nullCount) }
        else
            { Interpretation = interpretation
              Action =
                "Review the backfill block in manifest.remediation.sql, or keep the column nullable (keepNullable) until the backfill."
              Lever = ReviewRemediation }

    /// The uniqueness recommendation: a constraint review, never an
    /// automatic cleanup — duplicates under a single-column unique
    /// declaration frequently mean the declared key is narrower than the
    /// real business key, or the declaration is stale (rule 4: a genuine
    /// interpretation, hedged).
    let private recommendUnique : Recommendation =
        { Interpretation =
            "Duplicates under a single-column unique declaration frequently mean the declared key is narrower than the real business key, or the declaration is stale — a data cleanup cannot settle which."
          Action =
            "Review the declared key before any cleanup: confirm the business key, then correct the declaration or schedule the deduplication."
          Lever = ReviewConstraint }

    /// The FK-orphan recommendation, split by the reference's constraint
    /// state: an Ignore delete rule creates no database constraint (orphans
    /// accumulate by the platform's own design); WITH NOCHECK never
    /// validated the existing rows; a trusted constraint with observed
    /// orphans is a disagreement worth naming. At or below the band the
    /// prepared cleanup leads; above it `keepUntracked` leads.
    let private recommendOrphans
        (band: int64)
        (moduleName: string)
        (ec: EntityColumn)
        (state: ConstraintState)
        (orphanCount: int64)
        : Recommendation =
        let interpretation =
            match state with
            | ConstraintState.NoDbConstraint ->
                "The relationship carries no database constraint (an Ignore delete rule creates none), so rows referencing absent targets accumulate by the platform's own design."
            | ConstraintState.UntrustedConstraint ->
                "The relationship's constraint is enforced WITH NOCHECK (untrusted), so the existing rows were never validated against it."
            | ConstraintState.TrustedConstraint ->
                "The relationship's constraint is trusted, yet the profile observed orphan rows — the two readings disagree, and the evidence basis merits review."
        if orphanCount > band then
            { Interpretation = interpretation
              Action =
                sprintf "Keep the relationship untracked until the references clear — %s orphan row(s) exceed the repair band (%s). Merge the keepUntracked entry for %s."
                    (humaneBand orphanCount) (humaneBand band) (attributeRef3 moduleName ec)
              Lever = EditConfig (keepUntrackedEdit moduleName ec orphanCount) }
        else
            { Interpretation = interpretation
              Action =
                "Review the block in manifest.remediation.sql: point the row(s) at existing targets, clear the reference, or delete them."
              Lever = ReviewRemediation }

    /// The overflow recommendation: a width RULING (the estate board's D4
    /// discipline) — widen the declaration or truncate the rows; the ruling
    /// precedes any repair, because a prepared truncation would read as a
    /// default path and there is none.
    let private recommendOverflow : Recommendation =
        { Interpretation =
            "Values past the declared width are already present, so the declared width cannot deploy without loss and the load cannot carry the rows."
          Action =
            "Rule the width: declare the wider envelope in the model, or truncate the rows to the declaration — the ruling precedes any repair."
          Lever = ReviewModel }

    /// NOT-NULL declared but NULLs present — generalizes
    /// `Preflight.dataViolatesTightening` beyond the `EnforceNotNull` overlay:
    /// EVERY attribute the model declares non-nullable (a PK, or a column with
    /// `IsNullable = false`) whose profiled evidence carries at least one NULL.
    /// Exact `NullCount` carried from `Profile.Columns` when present; otherwise
    /// the boolean `HasNulls` reality witnesses the violation with an unknown
    /// count (carried as 0, which the renderer reads as "present").
    let private notNullViolations
        (moduleOf: Kind -> string)
        (band: int64)
        (catalog: Catalog)
        (profile: Profile)
        : DataViolation list =
        catalog
        |> Catalog.allKinds
        |> List.collect (fun kind ->
            kind.Attributes
            |> List.choose (fun attr ->
                let declaredNotNull = attr.IsPrimaryKey || not attr.Column.IsNullable
                if not declaredNotNull then None
                else
                    let exactCount =
                        Profile.tryFindColumn attr.SsKey profile
                        |> Option.map (fun c -> c.NullCount)
                    let realityHasNulls =
                        realityFor attr.SsKey profile
                        |> Option.map (fun r -> r.HasNulls)
                        |> Option.defaultValue false
                    let violation (n: int64) : DataViolation =
                        let reference = entityColumnOf kind attr
                        { Reference = reference
                          Kind = NotNullButNullsPresent n
                          Recommendation = Some (recommendNotNull band (moduleOf kind) reference n) }
                    match exactCount with
                    | Some n when n > 0L -> Some (violation n)
                    | Some _ -> None
                    | None when realityHasNulls -> Some (violation 0L)
                    | None -> None))

    /// UNIQUE / PK declared but duplicates present — every SINGLY
    /// unique-backed attribute whose `AttributeReality.HasDuplicates = true`
    /// (the single-column altitude; see `uniqueBackedAttributeKeys`).
    let private uniqueViolations (catalog: Catalog) (profile: Profile) : DataViolation list =
        catalog
        |> Catalog.allKinds
        |> List.collect (fun kind ->
            let backed = uniqueBackedAttributeKeys kind
            kind.Attributes
            |> List.choose (fun attr ->
                if not (Set.contains attr.SsKey backed) then None
                else
                    let hasDuplicates =
                        realityFor attr.SsKey profile
                        |> Option.map (fun r -> r.HasDuplicates)
                        |> Option.defaultValue false
                    if hasDuplicates then
                        Some
                            { Reference = entityColumnOf kind attr
                              Kind = UniqueButDuplicatesPresent
                              Recommendation = Some recommendUnique }
                    else None))

    /// FK orphans — reuse the profiler's per-Reference orphan evidence
    /// (`ForeignKeyReality.HasOrphan` + `OrphanCount`). The orphan is reported
    /// against the FK's SOURCE attribute (the column whose values fail to
    /// resolve), so the operator sees `Entity.ForeignKeyColumn`.
    let private orphanViolations
        (moduleOf: Kind -> string)
        (band: int64)
        (catalog: Catalog)
        (profile: Profile)
        : DataViolation list =
        catalog
        |> Catalog.allKinds
        |> List.collect (fun kind ->
            kind.References
            |> List.choose (fun reference ->
                match Profile.tryFindForeignKey reference.SsKey profile with
                | Some fk when fk.HasOrphan ->
                    let column =
                        Kind.tryFindAttribute reference.SourceAttribute kind
                        |> Option.map (fun a -> Name.value a.Name)
                        |> Option.defaultValue (Name.value reference.Name)
                    let ec = { Entity = Name.value kind.Name; Column = column }
                    Some
                        { Reference = ec
                          Kind = ForeignKeyOrphans fk.OrphanCount
                          Recommendation =
                            Some (recommendOrphans band (moduleOf kind) ec reference.ConstraintState fk.OrphanCount) }
                | _ -> None))

    /// Length / type overflow — declared `Attribute.Length` vs the profiled
    /// `ColumnProfile.MaxObservedLength`. A violation fires when the source
    /// carries a value LONGER than the declared cap: the declared model
    /// asserts a width the data exceeds, so the declaration cannot hold the
    /// reality. Scoped to attributes that declare a POSITIVE `Length` — an
    /// absent length is MAX / open-ended, and a `0` (or negative) carried
    /// from OSSYS metadata declares NO width, not a width of zero: the
    /// storage lane already reads it so (`OssysTypeMapping.textLength` /
    /// `boundedOr` treat only a positive length as `Bounded`), and the
    /// platform's own mapping has no zero-width bounded type. Before this
    /// gate aligned (2026-07-18), a metadata `Length = 0` under any observed
    /// value fired "observed 5, declared 0" findings rooted in the reader,
    /// not the data. Also scoped to attributes carrying a probed
    /// `MaxObservedLength` (a non-text/binary column, or an unprobed one,
    /// surfaces no length axis → no violation). The `observed` / `declared`
    /// tokens render the proof beside the finding.
    let private overflowViolations (catalog: Catalog) (profile: Profile) : DataViolation list =
        catalog
        |> Catalog.allKinds
        |> List.collect (fun kind ->
            kind.Attributes
            |> List.choose (fun attr ->
                match attr.Length with
                | Some declared when declared > 0 ->
                    Profile.tryFindColumn attr.SsKey profile
                    |> Option.bind (fun c -> c.MaxObservedLength)
                    |> Option.bind (fun observed ->
                        if observed > declared then
                            Some
                                { Reference = entityColumnOf kind attr
                                  Kind =
                                    LengthOrTypeOverflow (
                                        observed = string observed,
                                        declared = string declared)
                                  Recommendation = Some recommendOverflow }
                        else None)
                | _ -> None))  // Absent or non-positive — no declared cap to overflow.

    /// Aggregate the data-violation section from a declared catalog × profiled
    /// evidence. Deterministic — sorted by category then operator-facing
    /// reference (T1), so the rollup is byte-stable across runs. The
    /// kind→module index resolves each violation's 3-part config reference
    /// (`Module.Entity.Attribute`) once, for the recommendation layer.
    let private aggregateDataViolations (band: int64) (catalog: Catalog) (profile: Profile) : DataViolation list =
        let moduleByKind : Map<SsKey, string> =
            catalog.Modules
            |> List.collect (fun m ->
                m.Kinds |> List.map (fun k -> k.SsKey, Name.value m.Name))
            |> Map.ofList
        let moduleOf (kind: Kind) : string =
            // Total by construction — every catalog kind belongs to a module;
            // the defensive arm keeps the resolver total over a hand-built
            // catalog fragment.
            Map.tryFind kind.SsKey moduleByKind |> Option.defaultValue "Model"
        [ notNullViolations moduleOf band catalog profile
          uniqueViolations catalog profile
          orphanViolations moduleOf band catalog profile
          overflowViolations catalog profile ]
        |> List.concat
        |> List.sortBy (fun v -> categoryOrdinal (categoryOf v), entityColumnText v.Reference)

    /// Surface the `CategoricalUniquenessPass` `SuggestUnique` outcomes as the
    /// advisory uniqueness-candidate section (closes NM-35 — these suggestions
    /// were always meant to feed a report). Reads the decision set directly;
    /// `DoNotSuggest` outcomes are not candidates. Deterministic — sorted by
    /// the operator-facing reference.
    let private aggregateUniquenessCandidates
        (catalog: Catalog)
        (decisions: CategoricalUniquenessDecisionSet)
        : UniquenessCandidate list =
        // Resolve each decision's attribute identity to its operator-facing
        // Entity.Column via the catalog (the decision carries only the
        // SsKey). The index is built ONCE — the per-decision full-catalog
        // `tryPick` was an O(decisions × attributes) scan at estate scale —
        // and only when there are decisions to resolve.
        if List.isEmpty decisions.Decisions then []
        else
        let attrIndex : Map<SsKey, EntityColumn> =
            catalog
            |> Catalog.allKinds
            |> List.collect (fun kind ->
                kind.Attributes |> List.map (fun attr -> attr.SsKey, entityColumnOf kind attr))
            |> Map.ofList
        let nameOf (attrKey: SsKey) : EntityColumn option =
            Map.tryFind attrKey attrIndex
        decisions.Decisions
        |> List.choose (fun decision ->
            match decision.Outcome with
            | CategoricalUniquenessOutcome.SuggestUnique (EveryValueDistinct (distinct, total)) ->
                nameOf decision.AttributeKey
                |> Option.map (fun reference ->
                    { Reference         = reference
                      DistinctCount     = distinct
                      TotalObservations = total })
            | CategoricalUniquenessOutcome.DoNotSuggest _ -> None)
        |> List.sortBy (fun c -> entityColumnText c.Reference)

    /// Compose the full report from a declared catalog, the profiled evidence,
    /// the categorical-uniqueness decision set, and the run's accepted-tolerance
    /// residual, under an explicit fix-vs-relax band (the recommendation
    /// layer's threshold). The estate masthead counts modules + entities from
    /// the catalog.
    let composeWithBand
        (band: int64)
        (estate: string)
        (catalog: Catalog)
        (profile: Profile)
        (categoricalDecisions: CategoricalUniquenessDecisionSet)
        (acceptedDivergences: ToleratedDivergence list)
        : ModelFidelityReport =
        { Estate               = estate
          ModuleCount          = List.length catalog.Modules
          EntityCount          = catalog |> Catalog.allKinds |> List.length
          DataViolations       = aggregateDataViolations band catalog profile
          AcceptedDivergences  = acceptedDivergences |> List.map (fun d -> { Divergence = d })
          UniquenessCandidates = aggregateUniquenessCandidates catalog categoricalDecisions }

    /// `composeWithBand` under the default repair band — the standing
    /// signature every existing caller keeps.
    let compose
        (estate: string)
        (catalog: Catalog)
        (profile: Profile)
        (categoricalDecisions: CategoricalUniquenessDecisionSet)
        (acceptedDivergences: ToleratedDivergence list)
        : ModelFidelityReport =
        composeWithBand repairBandDefault estate catalog profile categoricalDecisions acceptedDivergences

    // ----------------------------------------------------------------------
    // Rollups — count-first totals the renderer + codec both read.
    // ----------------------------------------------------------------------

    /// Distinct entities (tables) touched by a violation list — the renderer's
    /// "<K> entities" headline figure.
    let private distinctEntities (violations: DataViolation list) : int =
        violations
        |> List.map (fun v -> v.Reference.Entity)
        |> List.distinct
        |> List.length

    /// The per-category subtotal: count of violations + the distinct entities
    /// they touch, plus the top offenders (operator-facing references with
    /// their counts).
    type CategoryRollup =
        {
            Category   : ViolationCategory
            Count      : int
            Entities   : int
            Violations : DataViolation list
        }

    let private rollupCategory (category: ViolationCategory) (violations: DataViolation list) : CategoryRollup =
        let inCat = violations |> List.filter (fun v -> categoryOf v = category)
        { Category   = category
          Count      = List.length inCat
          Entities   = distinctEntities inCat
          Violations = inCat }

    /// The full data-violation rollup: total, distinct entities, per-category
    /// subtotals (in render order).
    type DataViolationRollup =
        {
            Total      : int
            Entities   : int
            Categories : CategoryRollup list
        }

    let dataViolationRollup (report: ModelFidelityReport) : DataViolationRollup =
        { Total      = List.length report.DataViolations
          Entities   = distinctEntities report.DataViolations
          Categories =
            [ NotNullCategory; UniqueCategory; OrphanCategory; OverflowCategory ]
            |> List.map (fun c -> rollupCategory c report.DataViolations) }

    /// The structured-channel code for the data-violation rollup — ONE Warn
    /// envelope per run when the source data contradicts the declared model
    /// (2026-07-06; the §12 at-scale law: the fidelity artifact holds the
    /// detail, the wire carries the constant-size rollup). The live board's
    /// notice strip and the verdict panel both read it, so the finding and
    /// its remediation artifact are never a silent JSON-only count.
    let dataViolationsCode : string = "fidelity.dataViolations"

    /// The rollup envelope's payload — per-category counts + the artifact
    /// pointers. `None` when the report carries no violations (the envelope
    /// is only emitted when there is a finding to surface).
    let dataViolationsPayload
        (remediationPath: string)
        (fidelityPath: string)
        (report: ModelFidelityReport)
        : Map<string, objnull> option =
        let dv = dataViolationRollup report
        if dv.Total = 0 then None
        else
            let countOf (category: ViolationCategory) : int =
                dv.Categories
                |> List.tryFind (fun c -> c.Category = category)
                |> Option.map (fun c -> c.Count)
                |> Option.defaultValue 0
            Some
                (Map.ofList
                    [ "total",           box dv.Total
                      "entities",        box dv.Entities
                      "notNull",         box (countOf NotNullCategory)
                      "unique",          box (countOf UniqueCategory)
                      "orphans",         box (countOf OrphanCategory)
                      "overflow",        box (countOf OverflowCategory)
                      "remediationPath", box remediationPath
                      "fidelityPath",    box fidelityPath ])

    // ----------------------------------------------------------------------
    // The rolled-up text renderer (THE_VOICE register: stative, count-first,
    // estate framed neutrally, the proof one level beneath the finding).
    // ----------------------------------------------------------------------

    /// Humane integer — thousands-separated invariant form (`2,140`, not
    /// `2140`; THE_VOICE §12 "numbers are humane"). The estate runs to
    /// thousands of changes; the rollup stays readable at that size.
    let private humane (n: int) : string =
        (int64 n).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    let private humane64 (n: int64) : string =
        n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    /// Render the distinct entity-name set a category touches as a bracketed,
    /// impact-capped list — THE_VOICE §12 "cap the breadth, name the
    /// remainder." The top few entities are named; the rest roll up to a count.
    let private entityList (cap: int) (violations: DataViolation list) : string =
        let names = violations |> List.map (fun v -> v.Reference.Entity) |> List.distinct
        match names with
        | [] -> ""
        | _ ->
            let shown = names |> List.truncate cap
            let remainder = List.length names - List.length shown
            let head = String.concat ", " shown
            if remainder > 0 then System.String.Concat(head, ", and ", humane remainder, " more")
            else head

    /// The per-category top-offenders trailer — `[Entity.Col <count> · …]`,
    /// capped, impact-ranked (highest count first). Names the few that matter.
    let private topOffenders (cap: int) (violations: DataViolation list) : string =
        let weight (v: DataViolation) : int64 =
            match v.Kind with
            | NotNullButNullsPresent n -> n
            | ForeignKeyOrphans n      -> n
            | UniqueButDuplicatesPresent -> 1L
            | LengthOrTypeOverflow _   -> 1L
        let ranked =
            violations
            |> List.sortByDescending weight
            |> List.truncate cap
        match ranked with
        | [] -> ""
        | _ ->
            let part (v: DataViolation) : string =
                let count =
                    match v.Kind with
                    | NotNullButNullsPresent n when n > 0L -> System.String.Concat(" ", humane64 n)
                    | ForeignKeyOrphans n      when n > 0L -> System.String.Concat(" ", humane64 n)
                    | _ -> ""
                System.String.Concat(entityColumnText v.Reference, count)
            let remainder = List.length violations - List.length ranked
            let body = ranked |> List.map part |> String.concat " · "
            let tail = if remainder > 0 then System.String.Concat(" · and ", humane remainder, " more") else ""
            System.String.Concat("[", body, tail, "]")

    let private categoryLabel (c: ViolationCategory) : string =
        match c with
        | NotNullCategory  -> "NOT NULL declared, NULLs present"
        | UniqueCategory   -> "UNIQUE/PK declared, duplicates"
        | OrphanCategory   -> "FK orphans"
        | OverflowCategory -> "Length / type overflow"

    /// The category-level interpretation the render states beneath each
    /// non-clean category line — total over the category vocabulary, so a
    /// new axis cannot land without its reading. The per-violation
    /// interpretations in `fidelity.json` refine these (the orphan split by
    /// constraint state); the category line carries the shared mechanism.
    let private categoryInterpretation (c: ViolationCategory) : string =
        match c with
        | NotNullCategory ->
            "A mandatory attribute is validated by the platform at run time only; the deployed columns allow NULL, and the NOT NULL declarations cannot deploy while the rows remain."
        | UniqueCategory ->
            "Duplicates under a single-column unique declaration frequently mean the declared key is narrower than the real business key, or the declaration is stale — a data cleanup cannot settle which."
        | OrphanCategory ->
            "A relationship without a database constraint (an Ignore delete rule creates none), or one enforced WITH NOCHECK, accumulates rows that reference absent targets."
        | OverflowCategory ->
            "Values past a declared width are already present, so the declared width cannot deploy without loss and the load cannot carry the rows."

    /// Join the per-arm fragments into one imperative sentence — sentence
    /// case on the lead, "; " between arms, terminal period. The fallback
    /// carries the category's generic both-arm imperative when no violation
    /// carries a lever (a legacy `fidelity.json` parsed without
    /// recommendation nodes).
    let private joinArms (fallback: string) (parts: string list) : string =
        match parts with
        | [] -> fallback
        | _ ->
            let sentence = String.concat "; " parts
            let lead =
                System.String.Concat(
                    string (System.Char.ToUpperInvariant sentence.[0]),
                    sentence.Substring 1)
            System.String.Concat(lead, ".")

    /// The category-level next-move line, aggregated from the per-violation
    /// arms: the count on the prepared-repair arm (the remediation block)
    /// beside the count on the relax arm (past-band `keepNullable` /
    /// `keepUntracked` config entries).
    let private categoryActionLine (c: ViolationCategory) (violations: DataViolation list) : string =
        let levers = violations |> List.choose (fun v -> v.Recommendation |> Option.map (fun r -> r.Lever))
        let repairs = levers |> List.filter (function ReviewRemediation -> true | _ -> false) |> List.length
        let relaxes = levers |> List.filter (function EditConfig _ -> true | _ -> false) |> List.length
        match c with
        | NotNullCategory ->
            [ if repairs > 0 then
                yield sprintf "review the backfill block(s) in manifest.remediation.sql (%s column(s))" (humane repairs)
              if relaxes > 0 then
                yield sprintf "keep %s column(s) nullable via keepNullable entries (past the repair band)" (humane relaxes) ]
            |> joinArms "Review the backfill blocks in manifest.remediation.sql, or keep past-band columns nullable via keepNullable entries."
        | UniqueCategory ->
            "Review each declared key before any cleanup: confirm the business key, then correct the declaration or schedule the deduplication."
        | OrphanCategory ->
            [ if repairs > 0 then
                yield sprintf "review the orphan block(s) in manifest.remediation.sql (%s relationship(s))" (humane repairs)
              if relaxes > 0 then
                yield sprintf "keep %s relationship(s) untracked via keepUntracked entries (past the repair band)" (humane relaxes) ]
            |> joinArms "Review the orphan blocks in manifest.remediation.sql, or keep past-band relationships untracked via keepUntracked entries."
        | OverflowCategory ->
            "Rule each width: declare the wider envelope in the model, or truncate the rows to the declaration — the ruling precedes any repair."

    /// Render the full report as the operator-facing rolled-up text — totals at
    /// the top, per-entity breakdown beneath (the operator's "eminently useful
    /// at first glance" requirement). THE_VOICE: stative, agentless, neutral
    /// estate reference, the finding on top and the count-evidence beside it.
    let render (report: ModelFidelityReport) : string list =
        let dv = dataViolationRollup report
        [ // The masthead — estate, scale.
          yield
              sprintf "MODEL FIDELITY — %s (%s module(s), %s entity(ies))"
                  report.Estate (humane report.ModuleCount) (humane report.EntityCount)

          // Section 1 — data violations (the headline; count-first).
          if dv.Total = 0 then
              yield "  DATA VIOLATIONS — the source data is consistent with every declared constraint."
          else
              yield
                  sprintf "  DATA VIOLATIONS (source data versus declared model)   %s total · %s entity(ies)"
                      (humane dv.Total) (humane dv.Entities)
              for cat in dv.Categories do
                  if cat.Count > 0 then
                      let entities = entityList 5 cat.Violations
                      let offenders = topOffenders 4 cat.Violations
                      yield
                          sprintf "      %-36s %s   %s   %s"
                              (categoryLabel cat.Category) (humane cat.Count) entities offenders
                      // The recommendation layer (2026-07-18): the finding on
                      // top, the interpretation beneath it, the next move
                      // last — every category ends on its move (THE_VOICE:
                      // end on the move; the statement on top, the proof and
                      // the reading one level beneath).
                      yield sprintf "          %s" (categoryInterpretation cat.Category)
                      yield sprintf "          Next: %s" (categoryActionLine cat.Category cat.Violations)

          // Section 2 — accepted divergences (the per-run tolerance residual).
          match report.AcceptedDivergences with
          | [] ->
              yield "  ACCEPTED DIVERGENCES — no tolerance fired this run; the comparison is strict."
          | divergences ->
              yield
                  sprintf "  ACCEPTED DIVERGENCES (tolerances fired this run)   %s"
                      (humane (List.length divergences))
              for d in divergences do
                  yield sprintf "      %s" (ToleratedDivergence.name d.Divergence)

          // Section 3 — uniqueness candidates (advisory).
          match report.UniquenessCandidates with
          | [] ->
              yield "  UNIQUENESS CANDIDATES — none advised; no column observed every value distinct."
          | candidates ->
              yield
                  sprintf "  UNIQUENESS CANDIDATES (advisory)   %s"
                      (humane (List.length candidates))
              for c in candidates do
                  let pct =
                      match candidateDistinctFraction c with
                      | Some f -> sprintf "%.1f%% distinct" (float f * 100.0)
                      | None   -> "distinct"
                  yield
                      sprintf "      %-28s %s → natural key" (entityColumnText c.Reference) pct ]

    // ----------------------------------------------------------------------
    // The fidelity.json codec (structured, machine-read sibling of the text).
    // ----------------------------------------------------------------------

    let private violationKindNode (k: ViolationKind) : JsonObject =
        let o = JsonObject()
        match k with
        | NotNullButNullsPresent n ->
            o.["axis"] <- JsonValue.Create "notNullButNullsPresent"
            o.["nullCount"] <- JsonValue.Create n
        | UniqueButDuplicatesPresent ->
            o.["axis"] <- JsonValue.Create "uniqueButDuplicatesPresent"
        | ForeignKeyOrphans n ->
            o.["axis"] <- JsonValue.Create "foreignKeyOrphans"
            o.["orphanCount"] <- JsonValue.Create n
        | LengthOrTypeOverflow (observed, declared) ->
            o.["axis"] <- JsonValue.Create "lengthOrTypeOverflow"
            o.["observed"] <- JsonValue.Create observed
            o.["declared"] <- JsonValue.Create declared
        o

    /// The lever's stable machine token (the `fidelity.json` discriminator).
    let private leverToken (lever: RecommendationLever) : string =
        match lever with
        | ReviewRemediation -> "reviewRemediation"
        | EditConfig _      -> "editConfig"
        | ReviewModel       -> "reviewModel"
        | ReviewConstraint  -> "reviewConstraint"

    let private recommendationNode (r: Recommendation) : JsonObject =
        let o = JsonObject()
        o.["interpretation"] <- JsonValue.Create r.Interpretation
        o.["action"] <- JsonValue.Create r.Action
        o.["lever"] <- JsonValue.Create (leverToken r.Lever)
        (match r.Lever with
         | EditConfig sc ->
             o.["configPath"] <- JsonValue.Create sc.Path
             o.["configValue"] <- JsonValue.Create sc.Value
             match sc.Note with
             | Some note -> o.["configNote"] <- JsonValue.Create note
             | None -> ()
         | _ -> ())
        o

    let private violationNode (v: DataViolation) : JsonObject =
        let o = JsonObject()
        o.["entity"] <- JsonValue.Create v.Reference.Entity
        o.["column"] <- JsonValue.Create v.Reference.Column
        o.["reference"] <- JsonValue.Create (entityColumnText v.Reference)
        o.["kind"] <- violationKindNode v.Kind
        match v.Recommendation with
        | Some r -> o.["recommendation"] <- recommendationNode r
        | None -> ()
        o

    /// Serialize the report to its `fidelity.json` document — the structured,
    /// byte-deterministic sibling of the rolled-up text. The rollups are
    /// pre-computed so a downstream reader gets the count-first shape without
    /// re-aggregating.
    let toJson (report: ModelFidelityReport) : JsonObject =
        let dv = dataViolationRollup report
        let root = JsonObject()
        root.["estate"] <- JsonValue.Create report.Estate
        root.["moduleCount"] <- JsonValue.Create report.ModuleCount
        root.["entityCount"] <- JsonValue.Create report.EntityCount

        let dataViolations = JsonObject()
        dataViolations.["total"] <- JsonValue.Create dv.Total
        dataViolations.["entities"] <- JsonValue.Create dv.Entities
        let categories = JsonArray()
        for cat in dv.Categories do
            let c = JsonObject()
            c.["category"] <- JsonValue.Create (categoryLabel cat.Category)
            c.["count"] <- JsonValue.Create cat.Count
            c.["entities"] <- JsonValue.Create cat.Entities
            // The category-grain recommendation (2026-07-18) — pre-computed
            // like the rollups, so a downstream reader gets the
            // interpretation + next move without re-aggregating; the
            // per-violation nodes beneath carry the refined per-finding arms.
            if cat.Count > 0 then
                let recNode = JsonObject()
                recNode.["interpretation"] <- JsonValue.Create (categoryInterpretation cat.Category)
                recNode.["action"] <- JsonValue.Create (categoryActionLine cat.Category cat.Violations)
                c.["recommendation"] <- recNode
            let items = JsonArray()
            for v in cat.Violations do items.Add(violationNode v)
            c.["violations"] <- items
            categories.Add(c)
        dataViolations.["categories"] <- categories
        root.["dataViolations"] <- dataViolations

        let accepted = JsonArray()
        for d in report.AcceptedDivergences do
            accepted.Add(JsonValue.Create (ToleratedDivergence.name d.Divergence))
        root.["acceptedDivergences"] <- accepted

        let candidates = JsonArray()
        for c in report.UniquenessCandidates do
            let node = JsonObject()
            node.["reference"] <- JsonValue.Create (entityColumnText c.Reference)
            node.["entity"] <- JsonValue.Create c.Reference.Entity
            node.["column"] <- JsonValue.Create c.Reference.Column
            node.["distinctCount"] <- JsonValue.Create c.DistinctCount
            node.["totalObservations"] <- JsonValue.Create c.TotalObservations
            candidates.Add(node)
        root.["uniquenessCandidates"] <- candidates
        root

    /// Serialize to a pretty-printed JSON string (the artifact body).
    let toJsonString (report: ModelFidelityReport) : string =
        let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        (toJson report).ToJsonString(opts)

    // ----------------------------------------------------------------------
    // The codec inverse — read a recorded `fidelity.json` back into a report
    // so the `report` verb can surface a prior run's roll-up. Fail-closed: a
    // malformed document yields `None` (the caller states "no fidelity report
    // recorded" rather than crashing).
    // ----------------------------------------------------------------------

    let private tryNode (o: JsonObject) (key: string) : JsonNode option =
        match o.TryGetPropertyValue key with
        | true, node -> Option.ofObj node
        | _ -> None

    // The narrow catch on the value coercions: `JsonNode.GetValue<T>` throws
    // `InvalidOperationException` (wrong node kind) / `FormatException` (bad
    // number) on a type mismatch — those are the "absent/ill-typed field → None"
    // cases. A bare `with _` also swallowed OutOfMemory / cancellation etc.; the
    // narrowed catch lets those fatals propagate.
    let private tryStr (o: JsonObject) (key: string) : string option =
        tryNode o key
        |> Option.bind (fun node -> try Some (node.GetValue<string>()) with :? System.InvalidOperationException | :? System.FormatException -> None)

    let private tryInt (o: JsonObject) (key: string) : int option =
        tryNode o key |> Option.bind (fun node -> try Some (node.GetValue<int>()) with :? System.InvalidOperationException | :? System.FormatException -> None)

    let private tryInt64 (o: JsonObject) (key: string) : int64 option =
        tryNode o key |> Option.bind (fun node -> try Some (node.GetValue<int64>()) with :? System.InvalidOperationException | :? System.FormatException -> None)

    let private asObject (node: JsonNode) : JsonObject option =
        match node with :? JsonObject as o -> Some o | _ -> None

    let private asArray (node: JsonNode) : JsonArray option =
        match node with :? JsonArray as a -> Some a | _ -> None

    /// The non-null elements of a JSON array, narrowed for nullness.
    let private elements (arr: JsonArray) : JsonNode list =
        [ for n in arr do match Option.ofObj n with Some node -> yield node | None -> () ]

    let private kindFromNode (o: JsonObject) : ViolationKind option =
        match tryStr o "axis" with
        | Some "notNullButNullsPresent" ->
            Some (NotNullButNullsPresent (tryInt64 o "nullCount" |> Option.defaultValue 0L))
        | Some "uniqueButDuplicatesPresent" -> Some UniqueButDuplicatesPresent
        | Some "foreignKeyOrphans" ->
            Some (ForeignKeyOrphans (tryInt64 o "orphanCount" |> Option.defaultValue 0L))
        | Some "lengthOrTypeOverflow" ->
            Some (LengthOrTypeOverflow (tryStr o "observed" |> Option.defaultValue "", tryStr o "declared" |> Option.defaultValue ""))
        | _ -> None

    /// Parse a violation's recommendation node back — verbatim carry (the
    /// recommendation was minted at compose time; a reader re-derives
    /// nothing). `None` on an absent node (a legacy document) or an unknown
    /// lever token (fail-closed to no recommendation, never a crash).
    let private recommendationFromNode (o: JsonObject) : Recommendation option =
        match tryStr o "interpretation", tryStr o "action", tryStr o "lever" with
        | Some interpretation, Some action, Some lever ->
            let leverValue =
                match lever with
                | "reviewRemediation" -> Some ReviewRemediation
                | "reviewModel"       -> Some ReviewModel
                | "reviewConstraint"  -> Some ReviewConstraint
                | "editConfig" ->
                    match tryStr o "configPath", tryStr o "configValue" with
                    | Some path, Some value ->
                        Some (EditConfig { Path = path; Value = value; Note = tryStr o "configNote" })
                    | _ -> None
                | _ -> None
            leverValue
            |> Option.map (fun l ->
                { Interpretation = interpretation; Action = action; Lever = l })
        | _ -> None

    /// Parse a `fidelity.json` document back into a `ModelFidelityReport`.
    /// `None` on a malformed document (fail-closed). Reconstructs the
    /// data-violation list from the per-category arrays + the candidate /
    /// divergence sections.
    let fromJson (json: string) : ModelFidelityReport option =
        try
            match Option.ofObj (JsonNode.Parse json) |> Option.bind asObject with
            | None -> None
            | Some root ->
                let estate = tryStr root "estate" |> Option.defaultValue "the model"
                let moduleCount = tryInt root "moduleCount" |> Option.defaultValue 0
                let entityCount = tryInt root "entityCount" |> Option.defaultValue 0
                let violations =
                    [ match tryNode root "dataViolations" |> Option.bind asObject with
                      | None -> ()
                      | Some dv ->
                          match tryNode dv "categories" |> Option.bind asArray with
                          | None -> ()
                          | Some cats ->
                              for cat in elements cats do
                                  match asObject cat with
                                  | None -> ()
                                  | Some c ->
                                      match tryNode c "violations" |> Option.bind asArray with
                                      | None -> ()
                                      | Some items ->
                                          for item in elements items do
                                              match asObject item with
                                              | None -> ()
                                              | Some v ->
                                                  match tryStr v "entity", tryStr v "column", tryNode v "kind" |> Option.bind asObject with
                                                  | Some entity, Some column, Some k ->
                                                      match kindFromNode k with
                                                      | Some kind ->
                                                          let recommendation =
                                                              tryNode v "recommendation"
                                                              |> Option.bind asObject
                                                              |> Option.bind recommendationFromNode
                                                          yield
                                                              { Reference = { Entity = entity; Column = column }
                                                                Kind = kind
                                                                Recommendation = recommendation }
                                                      | None -> ()
                                                  | _ -> () ]
                let accepted =
                    [ match tryNode root "acceptedDivergences" |> Option.bind asArray with
                      | None -> ()
                      | Some arr ->
                          for n in elements arr do
                              match (try Some (n.GetValue<string>()) with :? System.InvalidOperationException | :? System.FormatException -> None) with
                              | Some token ->
                                  match ToleratedDivergence.tryParse token with
                                  | Some d -> yield { Divergence = d }
                                  | None -> ()
                              | None -> () ]
                let candidates =
                    [ match tryNode root "uniquenessCandidates" |> Option.bind asArray with
                      | None -> ()
                      | Some arr ->
                          for n in elements arr do
                              match asObject n with
                              | None -> ()
                              | Some c ->
                                  match tryStr c "entity", tryStr c "column" with
                                  | Some entity, Some column ->
                                      yield
                                          { Reference         = { Entity = entity; Column = column }
                                            DistinctCount     = tryInt64 c "distinctCount" |> Option.defaultValue 0L
                                            TotalObservations = tryInt64 c "totalObservations" |> Option.defaultValue 0L }
                                  | _ -> () ]
                Some
                    { Estate               = estate
                      ModuleCount          = moduleCount
                      EntityCount          = entityCount
                      DataViolations       = violations
                      AcceptedDivergences  = accepted
                      UniquenessCandidates = candidates }
        // Malformed top-level JSON (the fail-closed contract); a fatal still
        // propagates rather than masquerading as "no fidelity report recorded".
        with :? System.Text.Json.JsonException -> None
