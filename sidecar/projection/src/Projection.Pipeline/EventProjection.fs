namespace Projection.Pipeline

open Projection.Core

/// §16 egress projection — turns the pass chain's accumulated writers
/// (the `Lineage` trail + the `Diagnostics` stream, surfaced on
/// `Compose.RunReport`) into `LogSink.Envelope` values per the §7.4
/// `transform.*` taxonomy in `docs/logging-format.md`. This is the
/// boundary realization of §16: Core never calls `LogSink` (no I/O in
/// Core, and Core compiles before `LogSink`); the run boundary projects
/// the accumulated writers at egress.
///
/// **Pure.** Every function returns `LogSink.Envelope list`; the caller
/// (`FullExportRun`) does the `LogSink.emit` I/O. This keeps the
/// projection unit-testable without a writer and matches the codebase's
/// pure-function / boundary-I/O posture.
///
/// **One envelope per `LineageEvent`.** A decision-bearing `Annotated`
/// event projects to `transform.applied` (the intervention enforced) or
/// `transform.declined` (it kept/skipped, with a named rationale) at
/// `info`; every other event projects to `transform.lineage` at `debug`
/// (the raw audit trail, default-hidden per §4). The `ssKey` rides the
/// envelope's structured `SsKey` field so the §11 rollup groups by it.
[<RequireQualifiedAccess>]
module EventProjection =

    let private overlayAxisTag (axis: OverlayAxis) : string =
        match axis with
        | Selection  -> "selection"
        | Emission   -> "emission"
        | Insertion  -> "insertion"
        | Tightening -> "tightening"
        | Ordering   -> "ordering"

    let private classificationTag (c: Classification) : string =
        match c with
        | DataIntent          -> "dataIntent"
        | OperatorIntent axis -> "operatorIntent:" + overlayAxisTag axis

    /// `transform.lineage` tag + optional typed-payload rendering for one
    /// `TransformKind`. The detail string reuses Core's existing
    /// `toDiagnosticString` projections — strings emerge only at this
    /// rendering boundary (the typed DU IS the structure). Public so the
    /// `explain` drill-down (P3) renders the same decision text the event
    /// stream does — one rendering, two surfaces.
    let transformKindRender (kind: TransformKind) : string * string option =
        match kind with
        | Touched                       -> "touched", None
        | Renamed                       -> "renamed", None
        | Created                       -> "created", None
        | Removed reason                -> "removed", Some (RemovalReason.toDiagnosticString reason)
        | Annotated detail              -> "annotated", Some (AnnotationDetail.toDiagnosticString detail)
        | PhysicallyRenamed rename      -> "physicallyRenamed", Some (PhysicalRename.toDiagnosticString rename)
        | ColumnPhysicallyRenamed rename -> "columnPhysicallyRenamed", Some (ColumnRename.toDiagnosticString rename)

    let private transformEnvelope
        (level: LogSink.Level)
        (code: string)
        (ssKey: SsKey)
        (payload: Map<string, objnull>)
        : LogSink.Envelope =
        { LogSink.envelope level LogSink.Transform code payload with
            SsKey = Some ssKey }

    /// Classify a decision `AnnotationDetail` as enforced (→ `applied`)
    /// or kept/not-enforced (→ `declined`), with the rationale rendered
    /// from the typed outcome. Returns `None` for non-decision
    /// annotations (`ClosureSkipped` / `Label`), which fall through to
    /// `transform.lineage`.
    let private decisionOf (detail: AnnotationDetail) : (string * bool * string) option =
        match detail with
        | NullabilityDecision (id, outcome) ->
            let applied = match outcome with NullabilityOutcome.EnforceNotNull _ -> true | _ -> false
            Some (id, applied, NullabilityOutcome.toDiagnosticString outcome)
        | UniqueIndexDecision (id, outcome) ->
            let applied = match outcome with UniqueIndexOutcome.EnforceUnique _ -> true | _ -> false
            Some (id, applied, UniqueIndexOutcome.toDiagnosticString outcome)
        | ForeignKeyDecision (id, outcome) ->
            let applied = match outcome with ForeignKeyOutcome.EnforceConstraint _ -> true | _ -> false
            Some (id, applied, ForeignKeyOutcome.toDiagnosticString outcome)
        | CategoricalUniquenessDecision (id, outcome) ->
            let applied = match outcome with CategoricalUniquenessOutcome.SuggestUnique _ -> true | _ -> false
            Some (id, applied, CategoricalUniquenessOutcome.toDiagnosticString outcome)
        | ClosureSkipped _ | Label _ -> None

    /// Project one `LineageEvent`. Decision annotations promote to the
    /// `info`-level `transform.applied` / `transform.declined` narration;
    /// everything else is the `debug`-level `transform.lineage` trail.
    let ofLineageEvent (ev: LineageEvent) : LogSink.Envelope =
        let decision =
            match ev.TransformKind with
            | Annotated detail -> decisionOf detail
            | _                -> None
        match decision with
        | Some (interventionId, applied, rationale) ->
            let code = if applied then "transform.applied" else "transform.declined"
            let payload : Map<string, objnull> =
                Map.ofList [
                    "transformId", box ev.PassName
                    "interventionId", box interventionId
                    (if applied then "decision" else "rationale"), box rationale
                ]
            transformEnvelope LogSink.Info code ev.SsKey payload
        | None ->
            let tag, detail = transformKindRender ev.TransformKind
            let basePayload : (string * objnull) list = [
                "passName", box ev.PassName
                "transformKind", box tag
                "classification", box (classificationTag ev.Classification)
            ]
            let payload =
                match detail with
                | Some d -> basePayload @ [ "detail", box d ]
                | None   -> basePayload
            transformEnvelope LogSink.Debug "transform.lineage" ev.SsKey (Map.ofList payload)

    /// Project the full Lineage trail. Trail order is preserved (A24
    /// chronological), so the envelopes emit earliest-first.
    let ofLineageTrail (trail: LineageEvent list) : LogSink.Envelope list =
        trail |> List.map ofLineageEvent

    let private levelOfSeverity (s: DiagnosticSeverity) : LogSink.Level =
        match s with
        | DiagnosticSeverity.Info    -> LogSink.Info
        | DiagnosticSeverity.Warning -> LogSink.Warn
        | DiagnosticSeverity.Error   -> LogSink.Error

    /// Project one `DiagnosticEntry` as a `transform.diagnostic` envelope
    /// (§7.4 — level mirrors `Severity`). When the entry carries a
    /// `SuggestedConfig`, it surfaces under the `suggestedConfig` payload
    /// key so the §11 rollup's `suggestedConfigEdits` counter increments
    /// (the operator's prioritized to-do list, L3-X12).
    let ofDiagnosticEntry (entry: DiagnosticEntry) : LogSink.Envelope =
        let basePayload : (string * objnull) list = [
            "source",  box entry.Source
            "code",    box entry.Code
            "message", box entry.Message
            "metadata", box entry.Metadata
        ]
        let withSuggested =
            match entry.SuggestedConfig with
            | None     -> basePayload
            | Some cfg ->
                let cfgMap : Map<string, objnull> =
                    let baseCfg : (string * objnull) list =
                        [ "path", box cfg.Path; "value", box cfg.Value ]
                    let full =
                        match cfg.Note with
                        | Some n -> baseCfg @ [ "note", box n ]
                        | None   -> baseCfg
                    Map.ofList full
                basePayload @ [ "suggestedConfig", box cfgMap ]
        let env =
            LogSink.envelope (levelOfSeverity entry.Severity) LogSink.Transform
                "transform.diagnostic" (Map.ofList withSuggested)
        match entry.SsKey with
        | Some k -> { env with SsKey = Some k }
        | None   -> env

    /// Project a `DiagnosticEntry` stream. Used for the chain's full
    /// `PassDiagnostics` (disjoint from the curated operational set the
    /// run already emits).
    let ofDiagnostics (entries: DiagnosticEntry list) : LogSink.Envelope list =
        entries |> List.map ofDiagnosticEntry

    // ------------------------------------------------------------------
    // §7.4 transform.registered — the pillar-9 totality surface
    // ------------------------------------------------------------------

    let private stageTag (s: StageBinding) : string =
        match s with
        | StageBinding.Adapter        -> "adapter"
        | StageBinding.Pass           -> "pass"
        | StageBinding.OrderingPolicy -> "orderingPolicy"
        | StageBinding.Emitter        -> "emitter"
        | StageBinding.Pipeline       -> "pipeline"

    let private domainTag (d: Domain) : string =
        match d with
        | Domain.Schema        -> "schema"
        | Domain.Data          -> "data"
        | Domain.Identity      -> "identity"
        | Domain.Diagnostics   -> "diagnostics"
        | Domain.CutoverSafety -> "cutoverSafety"
        | Domain.CrossCutting  -> "crossCutting"

    let private statusTag (s: TransformStatus) : string =
        match s with
        | Active                -> "active"
        | NotImplementedInV2 _  -> "notImplementedInV2"

    /// Project one registered transform's metadata to a
    /// `transform.registered` envelope (§7.4 — debug, start phase). The
    /// payload carries the **per-Site** classification (DataIntent |
    /// OperatorIntent<axis>) rather than one lossy intent per transform —
    /// a transform may touch several axes. At run start this is the
    /// pillar-9 totality surface: every registered transform, its stage,
    /// and the intent of each site — the data-intent / operator-intent
    /// separation made visible, from the same registry that drives the run.
    let ofRegisteredTransform (meta: RegisteredTransformMetadata) : LogSink.Envelope =
        let sites : objnull =
            meta.Sites
            |> List.map (fun site ->
                Map.ofList [
                    "site",           box site.SiteName
                    "classification", box (classificationTag site.Classification)
                ] : Map<string, objnull>)
            |> box
        let payload : Map<string, objnull> =
            Map.ofList [
                "transformId", box meta.Name
                "domain",      box (domainTag meta.Domain)
                "stage",       box (stageTag meta.StageBinding)
                "status",      box (statusTag meta.Status)
                "sites",       sites
            ]
        { LogSink.envelope LogSink.Debug LogSink.Transform "transform.registered" payload with
            Phase = LogSink.Start }

    /// Project the full registry — one `transform.registered` per entry.
    /// Emitted at run start so the §11 rollup counts the run's complete
    /// classified transform inventory.
    let ofRegistry (registry: RegisteredTransformMetadata list) : LogSink.Envelope list =
        registry |> List.map ofRegisteredTransform

    /// Tier-1 reporting (§7.7) — project a wide-canary `PhysicalSchemaDiff`
    /// into structured `canary.*` events: `canary.diffEmpty` (green; info /
    /// end) or `canary.divergence` (red; error — **fails the run**) carrying
    /// the per-axis divergence breakdown + the rendered human diff. The CLI
    /// canary verb emits these alongside the operator-facing prose, so the
    /// fidelity verdict is machine-readable + ledger-able, not just printed.
    let canaryEnvelopes (tableCount: int) (diff: PhysicalSchemaDiff) : LogSink.Envelope list =
        if PhysicalSchema.isEqual diff then
            [ { LogSink.envelope LogSink.Info LogSink.Canary "canary.diffEmpty"
                  (Map.ofList [ "tableCount", box tableCount ]) with
                  Phase = LogSink.End } ]
        else
            let axisCounts : Map<string, objnull> =
                Map.ofList [
                    "missingColumns",             box (List.length diff.MissingColumns)
                    "extraColumns",               box (List.length diff.ExtraColumns)
                    "missingForeignKeys",         box (List.length diff.MissingForeignKeys)
                    "extraForeignKeys",           box (List.length diff.ExtraForeignKeys)
                    "missingIndexes",             box (List.length diff.MissingIndexes)
                    "extraIndexes",               box (List.length diff.ExtraIndexes)
                    "missingRows",                box (List.length diff.MissingRows)
                    "extraRows",                  box (List.length diff.ExtraRows)
                    "missingRowDigests",          box (List.length diff.MissingRowDigests)
                    "extraRowDigests",            box (List.length diff.ExtraRowDigests)
                    "missingAnnotations",         box (List.length diff.MissingAnnotations)
                    "extraAnnotations",           box (List.length diff.ExtraAnnotations)
                    "missingLogicalNameBindings", box (List.length diff.MissingLogicalNameBindings)
                    "extraLogicalNameBindings",   box (List.length diff.ExtraLogicalNameBindings)
                ]
            [ { LogSink.envelope LogSink.Error LogSink.Canary "canary.divergence"
                  (Map.ofList [ "axisCounts",   box axisCounts
                                "renderedDiff", box (PhysicalSchema.renderDiff diff) ]) with
                  Phase = LogSink.ErrorPhase } ]
