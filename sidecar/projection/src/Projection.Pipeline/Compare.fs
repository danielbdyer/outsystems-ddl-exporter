namespace Projection.Pipeline

// LINT-ALLOW-FILE: the rolled-up text renderer + the compare.json codec compose
//   operator-facing report prose (THE_VOICE twelve-rule register) and structured
//   JSON at a terminal reporting boundary. The aggregation core (the schema-delta
//   roll-up + the data-dealbreaker reuse of `ModelFidelity`) is pure and carries
//   no I/O — the operand resolution that opens connections lives one layer up
//   (the CLI run face), so this module stays a pure function of resolved operands.

open System.Text.Json.Nodes
open Projection.Core

/// `projection compare <A> <B>` — the read-only multi-environment readiness
/// check (WP9 / NM-71). It diffs two environments and flags whether B can
/// receive A's model and data:
///
///   - **Schema delta** — `CatalogDiff.between B A`: the changes B's schema
///     needs so it can hold A's model (added / dropped / renamed / reshaped).
///   - **Data dealbreakers** — the `ModelFidelity` violation engine, run with
///     A's data evidence (its `Profile`) against B's DECLARED model: "if A's
///     data lands in B's schema, what breaks?" — a NOT-NULL column that would
///     carry NULLs, a UNIQUE/PK that would carry duplicates, an FK that would
///     orphan, a value that would overflow B's declared width.
///
/// Advisory only — no writes. The report is the operator's go/no-go read: a
/// schema delta the target can absorb plus zero data dealbreakers means B is
/// ready to receive A.
[<RequireQualifiedAccess>]
module Compare =

    /// A closed-DU describing where a compare operand comes from — the matrix's
    /// `DiffSource` shape. Each resolves (in the run face, via the `Ref`
    /// machinery) to a `(Catalog, Profile option)`: the declared model and, when
    /// a live env or a captured artifact supplies it, the observed data evidence.
    ///
    ///   - `LiveEnv`   — a live connection reference (`env:VAR` / `file:path` /
    ///                   a raw conn string): the deployed catalog read back +
    ///                   its profiled data reality (the only source that yields
    ///                   a `Profile`).
    ///   - `StoredRun` — a stored episode `@runId`: the run's captured
    ///                   `model.json` catalog; no data evidence (a run records
    ///                   the model, not the rows) → `Profile` absent.
    ///   - `ModelFile` — a model / config file on disk: the declared catalog;
    ///                   no data evidence → `Profile` absent.
    type DiffSource =
        | LiveEnv of conn: string
        | StoredRun of runId: string
        | ModelFile of path: string

    /// Human-readable identity of a `DiffSource` (for the report headers).
    let sourceIdentity (s: DiffSource) : string =
        match s with
        | LiveEnv conn -> "live:" + conn
        | StoredRun runId -> "@" + runId
        | ModelFile path -> "file:" + path

    /// A resolved operand — the declared catalog and (when available) the
    /// observed data evidence, carried with the operator-facing label so the
    /// report never re-resolves identity.
    type Operand =
        {
            Label   : string
            Catalog : Catalog
            /// The observed data reality (a live env supplies it; a model file /
            /// stored run does not). Absent ⇒ the data-dealbreaker section is
            /// honestly advisory-silent for this operand (no rows observed ⇒
            /// nothing to contradict).
            Profile : Profile option
        }

    // -- The report record -------------------------------------------------

    /// The assembled compare report. Count-first: the renderer leads with the
    /// schema-delta total and the dealbreaker total, then the per-category
    /// drill-down beneath. `DataEvidenceAvailable` records whether A carried a
    /// `Profile` — `false` means the dealbreaker section is advisory-silent
    /// because no data was observed, not because the data is clean.
    type CompareReport =
        {
            SourceLabel           : string
            TargetLabel           : string
            /// `CatalogDiff.between target source` — the changes B (target)
            /// needs to match A (source). `None` when the two catalogs could
            /// not be compared (a malformed catalog); the renderer states it.
            SchemaDelta           : CatalogDiff option
            /// Whether A carried profiled data evidence (a live env). When
            /// `false` the dealbreaker section is advisory-silent.
            DataEvidenceAvailable : bool
            /// The `ModelFidelity` data violations of A's data against B's
            /// declared model — the dealbreakers. Empty when no evidence, or
            /// when A's data is compatible with B's schema.
            DataDealbreakers      : ModelFidelity.DataViolation list
        }

    // ----------------------------------------------------------------------
    // The computation — pure over resolved operands.
    // ----------------------------------------------------------------------

    /// Compute the compare report from the source operand A and the target
    /// operand B. The schema delta is `CatalogDiff.between B A` (the changes B
    /// needs to match A). The data dealbreakers reuse the `ModelFidelity`
    /// violation engine: A's `Profile` (its data) against B's `Catalog` (its
    /// declared model) — every declared constraint of B that A's data would
    /// contradict. With no A-profile the dealbreaker section is empty and
    /// `DataEvidenceAvailable = false`.
    let compute (source: Operand) (target: Operand) : CompareReport =
        let schemaDelta =
            match CatalogDiff.between target.Catalog source.Catalog with
            | Ok d -> Some d
            | Error _ -> None
        let dealbreakers, evidence =
            match source.Profile with
            | Some profile ->
                // Reuse the fidelity engine: A's data versus B's declared model.
                // The estate label + the (empty) categorical decisions and
                // tolerance residual are inert here — the data-violation section
                // is the only one the dealbreaker read consumes.
                let report =
                    ModelFidelity.compose source.Label target.Catalog profile { Decisions = [] } []
                report.DataViolations, true
            | None -> [], false
        { SourceLabel           = source.Label
          TargetLabel           = target.Label
          SchemaDelta           = schemaDelta
          DataEvidenceAvailable = evidence
          DataDealbreakers      = dealbreakers }

    // ----------------------------------------------------------------------
    // Roll-ups — count-first totals the renderer + codec both read.
    // ----------------------------------------------------------------------

    /// The schema-delta norm ‖δ‖ — total move count B needs to match A. `0`
    /// when the catalogs are byte-identical, or when the delta could not be
    /// computed (the renderer distinguishes the two).
    let schemaNorm (report: CompareReport) : int =
        match report.SchemaDelta with
        | Some d -> CatalogDiff.norm d
        | None -> 0

    /// The data-dealbreaker roll-up — total + distinct entities + the per-
    /// category subtotals, reusing the fidelity rollup vocabulary so the two
    /// reports speak the same count-first language.
    let dealbreakerRollup (report: CompareReport) : ModelFidelity.DataViolationRollup =
        // A bare `ModelFidelityReport` carrying only the dealbreaker violations,
        // so the shared rollup machinery (per-category subtotals, distinct
        // entities) computes the compare report's data section identically.
        let fidelity =
            { ModelFidelity.empty report.SourceLabel with
                DataViolations = report.DataDealbreakers }
        ModelFidelity.dataViolationRollup fidelity

    /// The compatibility verdict — `true` when B can receive A's model and data
    /// with no dealbreaker. The schema delta is NOT a dealbreaker (a schema
    /// change is the expected work B does to match A); only a data violation
    /// (NULLs into NOT-NULL, duplicates into UNIQUE, orphaned FKs, overflow)
    /// blocks the move, because those cannot be reconciled by a schema change
    /// alone. A failed schema comparison is itself a blocker (the operator
    /// cannot read the readiness).
    let isCompatible (report: CompareReport) : bool =
        report.SchemaDelta.IsSome && List.isEmpty report.DataDealbreakers

    // ----------------------------------------------------------------------
    // The rolled-up text renderer (THE_VOICE register: stative, agentless,
    // count-first, the proof one level beneath the finding).
    // ----------------------------------------------------------------------

    let private humane (n: int) : string =
        (int64 n).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    let private humane64 (n: int64) : string =
        n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    let private categoryLabel (c: ModelFidelity.ViolationCategory) : string =
        match c with
        | ModelFidelity.NotNullCategory  -> "NOT NULL declared, NULLs would land"
        | ModelFidelity.UniqueCategory   -> "UNIQUE/PK declared, duplicates would land"
        | ModelFidelity.OrphanCategory   -> "FK orphans would land"
        | ModelFidelity.OverflowCategory -> "Length / type overflow"

    /// The per-category top-offenders trailer — capped, impact-ranked. Names the
    /// few that matter, rolls up the rest. Mirrors `ModelFidelity.topOffenders`
    /// so the two reports read alike.
    let private offenders (cap: int) (violations: ModelFidelity.DataViolation list) : string =
        let weight (v: ModelFidelity.DataViolation) : int64 =
            match v.Kind with
            | ModelFidelity.NotNullButNullsPresent n -> n
            | ModelFidelity.ForeignKeyOrphans n      -> n
            | ModelFidelity.UniqueButDuplicatesPresent -> 1L
            | ModelFidelity.LengthOrTypeOverflow _   -> 1L
        let ranked = violations |> List.sortByDescending weight |> List.truncate cap
        match ranked with
        | [] -> ""
        | _ ->
            let part (v: ModelFidelity.DataViolation) : string =
                let count =
                    match v.Kind with
                    | ModelFidelity.NotNullButNullsPresent n when n > 0L -> System.String.Concat(" ", humane64 n)
                    | ModelFidelity.ForeignKeyOrphans n      when n > 0L -> System.String.Concat(" ", humane64 n)
                    | _ -> ""
                System.String.Concat(ModelFidelity.entityColumnText v.Reference, count)
            let remainder = List.length violations - List.length ranked
            let body = ranked |> List.map part |> String.concat " · "
            let tail = if remainder > 0 then System.String.Concat(" · and ", humane remainder, " more") else ""
            System.String.Concat("[", body, tail, "]")

    /// Render the report as the operator-facing rolled-up text — the readiness
    /// verdict on top, the schema-delta + dealbreaker counts beneath, the per-
    /// category drill-down last. THE_VOICE: stative, agentless, neutral
    /// reference to the two environments, the finding on top, the count-evidence
    /// beside it, the next move named at the close.
    let render (report: CompareReport) : string list =
        let schemaN = schemaNorm report
        let rollup = dealbreakerRollup report
        [ // The masthead — the two operands, named.
          yield
              sprintf "READINESS — %s would receive %s" report.TargetLabel report.SourceLabel

          // The verdict line (THE_VOICE §3): is the move ready?
          match report.SchemaDelta with
          | None ->
              yield "  Stopped. The two models could not be compared; the readiness is unknown."
          | Some _ ->
              if List.isEmpty report.DataDealbreakers then
                  if schemaN = 0 then
                      yield "  Ready. The schemas already match and no data dealbreaker is present."
                  else
                      yield
                          sprintf "  Ready. %s schema change(s) bring the target into agreement; no data dealbreaker is present."
                              (humane schemaN)
              else
                  yield
                      sprintf "  Paused. %s data dealbreaker(s) block the move; the source data contradicts the target's declared model."
                          (humane rollup.Total)

          // Section 1 — the schema delta (what the target must change to match).
          match report.SchemaDelta with
          | None -> ()
          | Some d ->
              if schemaN = 0 then
                  yield "  SCHEMA DELTA — the target schema already matches the source."
              else
                  let c = CatalogDiff.channelCounts d
                  let removed =
                      c.RemovedKinds + c.RemovedAttributes + c.RemovedReferences
                      + c.RemovedIndexes + c.RemovedSequences
                  yield
                      sprintf "  SCHEMA DELTA (changes the target needs to match the source)   %s total · %s removal(s)"
                          (humane schemaN) (humane removed)
                  yield
                      sprintf "      tables        %s added · %s dropped · %s renamed"
                          (humane c.AddedKinds) (humane c.RemovedKinds) (humane c.RenamedKinds)
                  yield
                      sprintf "      columns       %s added · %s dropped · %s renamed · %s reshaped"
                          (humane c.AddedAttributes) (humane c.RemovedAttributes) (humane c.RenamedAttributes) (humane c.ChangedAttributes)
                  yield
                      sprintf "      relationships %s added · %s dropped"
                          (humane c.AddedReferences) (humane c.RemovedReferences)

          // Section 2 — the data dealbreakers (count-first).
          if not report.DataEvidenceAvailable then
              yield "  DATA DEALBREAKERS — the source carries no observed data evidence; the data readiness is advisory-silent."
          elif rollup.Total = 0 then
              yield "  DATA DEALBREAKERS — the source data satisfies every constraint the target declares."
          else
              yield
                  sprintf "  DATA DEALBREAKERS (the source data versus the target's declared model)   %s total · %s entity(ies)"
                      (humane rollup.Total) (humane rollup.Entities)
              for cat in rollup.Categories do
                  if cat.Count > 0 then
                      yield
                          sprintf "      %-44s %s   %s"
                              (categoryLabel cat.Category) (humane cat.Count) (offenders 4 cat.Violations) ]

    // ----------------------------------------------------------------------
    // The compare.json codec (structured, machine-read sibling of the text).
    // ----------------------------------------------------------------------

    let private channelNode (c: CatalogDiff.ChannelCounts) : JsonObject =
        let o = JsonObject()
        let tables = JsonObject()
        tables.["added"] <- JsonValue.Create c.AddedKinds
        tables.["dropped"] <- JsonValue.Create c.RemovedKinds
        tables.["renamed"] <- JsonValue.Create c.RenamedKinds
        o.["tables"] <- tables
        let columns = JsonObject()
        columns.["added"] <- JsonValue.Create c.AddedAttributes
        columns.["dropped"] <- JsonValue.Create c.RemovedAttributes
        columns.["renamed"] <- JsonValue.Create c.RenamedAttributes
        columns.["reshaped"] <- JsonValue.Create c.ChangedAttributes
        o.["columns"] <- columns
        let rels = JsonObject()
        rels.["added"] <- JsonValue.Create c.AddedReferences
        rels.["dropped"] <- JsonValue.Create c.RemovedReferences
        o.["relationships"] <- rels
        o

    let private violationKindNode (k: ModelFidelity.ViolationKind) : JsonObject =
        let o = JsonObject()
        match k with
        | ModelFidelity.NotNullButNullsPresent n ->
            o.["axis"] <- JsonValue.Create "notNullButNullsPresent"
            o.["nullCount"] <- JsonValue.Create n
        | ModelFidelity.UniqueButDuplicatesPresent ->
            o.["axis"] <- JsonValue.Create "uniqueButDuplicatesPresent"
        | ModelFidelity.ForeignKeyOrphans n ->
            o.["axis"] <- JsonValue.Create "foreignKeyOrphans"
            o.["orphanCount"] <- JsonValue.Create n
        | ModelFidelity.LengthOrTypeOverflow (observed, declared) ->
            o.["axis"] <- JsonValue.Create "lengthOrTypeOverflow"
            o.["observed"] <- JsonValue.Create observed
            o.["declared"] <- JsonValue.Create declared
        o

    let private dealbreakerNode (v: ModelFidelity.DataViolation) : JsonObject =
        let o = JsonObject()
        o.["entity"] <- JsonValue.Create v.Reference.Entity
        o.["column"] <- JsonValue.Create v.Reference.Column
        o.["reference"] <- JsonValue.Create (ModelFidelity.entityColumnText v.Reference)
        o.["kind"] <- violationKindNode v.Kind
        o

    /// Serialize the report to its `compare.json` document — the structured,
    /// byte-deterministic sibling of the rolled-up text. The roll-ups are
    /// pre-computed so a downstream reader gets the count-first shape without
    /// re-aggregating.
    let toJson (report: CompareReport) : JsonObject =
        let root = JsonObject()
        root.["source"] <- JsonValue.Create report.SourceLabel
        root.["target"] <- JsonValue.Create report.TargetLabel
        root.["compatible"] <- JsonValue.Create (isCompatible report)

        let schema = JsonObject()
        match report.SchemaDelta with
        | None ->
            schema.["comparable"] <- JsonValue.Create false
            schema.["total"] <- JsonValue.Create 0
        | Some d ->
            schema.["comparable"] <- JsonValue.Create true
            schema.["total"] <- JsonValue.Create (CatalogDiff.norm d)
            schema.["channels"] <- channelNode (CatalogDiff.channelCounts d)
        root.["schemaDelta"] <- schema

        let rollup = dealbreakerRollup report
        let data = JsonObject()
        data.["evidenceAvailable"] <- JsonValue.Create report.DataEvidenceAvailable
        data.["total"] <- JsonValue.Create rollup.Total
        data.["entities"] <- JsonValue.Create rollup.Entities
        let categories = JsonArray()
        for cat in rollup.Categories do
            let c = JsonObject()
            c.["category"] <- JsonValue.Create (categoryLabel cat.Category)
            c.["count"] <- JsonValue.Create cat.Count
            c.["entities"] <- JsonValue.Create cat.Entities
            let items = JsonArray()
            for v in cat.Violations do items.Add(dealbreakerNode v)
            c.["dealbreakers"] <- items
            categories.Add(c)
        data.["categories"] <- categories
        root.["dataDealbreakers"] <- data
        root

    /// Serialize to a pretty-printed JSON string (the artifact body).
    let toJsonString (report: CompareReport) : string =
        let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        (toJson report).ToJsonString(opts)
