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

    // ----------------------------------------------------------------------
    // Aggregation — from a declared Catalog × the profiled evidence.
    // ----------------------------------------------------------------------

    /// Every attribute that backs a UNIQUE index or the PK of its kind — the
    /// set whose `HasDuplicates` evidence is a real violation (a duplicate in a
    /// non-unique column is expected, not a contradiction).
    let private uniqueBackedAttributeKeys (kind: Kind) : Set<SsKey> =
        let fromIndexes =
            kind.Indexes
            |> List.filter (fun ix ->
                IndexUniqueness.isUnique ix.Uniqueness || IndexUniqueness.isPrimaryKey ix.Uniqueness)
            |> List.collect (fun ix -> ix.Columns |> List.map (fun ic -> ic.Attribute))
            |> Set.ofList
        // PK attributes are always unique-backed even absent an explicit index row.
        let fromPk =
            kind.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map (fun a -> a.SsKey)
            |> Set.ofList
        Set.union fromIndexes fromPk

    let private entityColumnOf (kind: Kind) (attr: Attribute) : EntityColumn =
        { Entity = Name.value kind.Name
          Column = Name.value attr.Name }

    let private realityFor (attrKey: SsKey) (profile: Profile) : AttributeReality option =
        profile.AttributeRealities |> List.tryFind (fun r -> r.AttributeKey = attrKey)

    /// NOT-NULL declared but NULLs present — generalizes
    /// `Preflight.dataViolatesTightening` beyond the `EnforceNotNull` overlay:
    /// EVERY attribute the model declares non-nullable (a PK, or a column with
    /// `IsNullable = false`) whose profiled evidence carries at least one NULL.
    /// Exact `NullCount` carried from `Profile.Columns` when present; otherwise
    /// the boolean `HasNulls` reality witnesses the violation with an unknown
    /// count (carried as 0, which the renderer reads as "present").
    let private notNullViolations (catalog: Catalog) (profile: Profile) : DataViolation list =
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
                    match exactCount with
                    | Some n when n > 0L ->
                        Some { Reference = entityColumnOf kind attr; Kind = NotNullButNullsPresent n }
                    | Some _ -> None
                    | None when realityHasNulls ->
                        Some { Reference = entityColumnOf kind attr; Kind = NotNullButNullsPresent 0L }
                    | None -> None))

    /// UNIQUE / PK declared but duplicates present — every unique-backed
    /// attribute whose `AttributeReality.HasDuplicates = true`.
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
                        Some { Reference = entityColumnOf kind attr; Kind = UniqueButDuplicatesPresent }
                    else None))

    /// FK orphans — reuse the profiler's per-Reference orphan evidence
    /// (`ForeignKeyReality.HasOrphan` + `OrphanCount`). The orphan is reported
    /// against the FK's SOURCE attribute (the column whose values fail to
    /// resolve), so the operator sees `Entity.ForeignKeyColumn`.
    let private orphanViolations (catalog: Catalog) (profile: Profile) : DataViolation list =
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
                    Some
                        { Reference = { Entity = Name.value kind.Name; Column = column }
                          Kind = ForeignKeyOrphans fk.OrphanCount }
                | _ -> None))

    /// Length / type overflow — declared `Length` / type vs the profiled
    /// maximum. FLAGGED: scoped to the empty list today. The string-length leg
    /// is unreachable from the Profile — no axis surfaces max-observed-string-
    /// length (the `EvidenceCache` holds the raw values, but no derivation
    /// projects the max length). The category exists in the rollup so the
    /// section is present and honest about its current reach; it fills when a
    /// `max-observed-length` Profile axis lands (an IR-grows-under-evidence
    /// slice).
    let private overflowViolations (_catalog: Catalog) (_profile: Profile) : DataViolation list =
        []

    /// Aggregate the data-violation section from a declared catalog × profiled
    /// evidence. Deterministic — sorted by category then operator-facing
    /// reference (T1), so the rollup is byte-stable across runs.
    let private aggregateDataViolations (catalog: Catalog) (profile: Profile) : DataViolation list =
        [ notNullViolations catalog profile
          uniqueViolations catalog profile
          orphanViolations catalog profile
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
        // Entity.Column via the catalog (the decision carries only the SsKey).
        let nameOf (attrKey: SsKey) : EntityColumn option =
            catalog
            |> Catalog.allKinds
            |> List.tryPick (fun kind ->
                Kind.tryFindAttribute attrKey kind
                |> Option.map (fun attr -> entityColumnOf kind attr))
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
    /// residual. The estate masthead counts modules + entities from the catalog.
    let compose
        (estate: string)
        (catalog: Catalog)
        (profile: Profile)
        (categoricalDecisions: CategoricalUniquenessDecisionSet)
        (acceptedDivergences: ToleratedDivergence list)
        : ModelFidelityReport =
        { Estate               = estate
          ModuleCount          = List.length catalog.Modules
          EntityCount          = catalog |> Catalog.allKinds |> List.length
          DataViolations       = aggregateDataViolations catalog profile
          AcceptedDivergences  = acceptedDivergences |> List.map (fun d -> { Divergence = d })
          UniquenessCandidates = aggregateUniquenessCandidates catalog categoricalDecisions }

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

    let private violationNode (v: DataViolation) : JsonObject =
        let o = JsonObject()
        o.["entity"] <- JsonValue.Create v.Reference.Entity
        o.["column"] <- JsonValue.Create v.Reference.Column
        o.["reference"] <- JsonValue.Create (entityColumnText v.Reference)
        o.["kind"] <- violationKindNode v.Kind
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

    let private tryStr (o: JsonObject) (key: string) : string option =
        tryNode o key
        |> Option.bind (fun node -> try Some (node.GetValue<string>()) with _ -> None)

    let private tryInt (o: JsonObject) (key: string) : int option =
        tryNode o key |> Option.bind (fun node -> try Some (node.GetValue<int>()) with _ -> None)

    let private tryInt64 (o: JsonObject) (key: string) : int64 option =
        tryNode o key |> Option.bind (fun node -> try Some (node.GetValue<int64>()) with _ -> None)

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
                                                          yield { Reference = { Entity = entity; Column = column }; Kind = kind }
                                                      | None -> ()
                                                  | _ -> () ]
                let accepted =
                    [ match tryNode root "acceptedDivergences" |> Option.bind asArray with
                      | None -> ()
                      | Some arr ->
                          for n in elements arr do
                              match (try Some (n.GetValue<string>()) with _ -> None) with
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
        with _ -> None
