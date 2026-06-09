namespace Projection.Targets.OperationalDiagnostics

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// Internal: per-kind JSON document construction shared across the
/// three operator-facing diagnostic emitters (chapter 4.3 slices α /
/// β / γ). Per pre-scope §1.5: each emitter projects a different
/// filtered subset of `DiagnosticEntry` records into the same per-
/// kind shape; this module is the shared writer earned at the second
/// consumer per the two-consumer threshold.
module internal DiagnosticDocument =

    let private severityString (s: DiagnosticSeverity) : string =
        match s with
        | DiagnosticSeverity.Info    -> "Info"
        | DiagnosticSeverity.Warning -> "Warning"
        | DiagnosticSeverity.Error   -> "Error"

    /// Render the `suggestedConfig` JSON object per logging-format
    /// contract §12. Field order matches the contract's `{path,
    /// value, note}` quoted shape. `note` is omitted when `None`
    /// (the JSON shape carries it only when the operator-rationale
    /// is meaningful).
    let private writeSuggestedConfig (w: Utf8JsonWriter) (cfg: SuggestedConfig) : unit =
        w.WriteStartObject()
        w.WriteString("path",  cfg.Path)
        w.WriteString("value", cfg.Value)
        match cfg.Note with
        | Some n -> w.WriteString("note", n)
        | None   -> ()
        w.WriteEndObject()

    /// Render one `DiagnosticEntry` as a JSON object. Field order is
    /// fixed by the write sequence for T1 byte-determinism.
    /// **Chapter B.4 slice 6:** `suggestedConfig` field surfaces
    /// after `metadata` when the entry carries `SuggestedConfig =
    /// Some _`; omitted otherwise so default-shape entries stay
    /// shape-equivalent to pre-slice-6 output (existing consumer
    /// JSON-parse code that doesn't know about `suggestedConfig`
    /// continues working).
    let private writeEntry (w: Utf8JsonWriter) (e: DiagnosticEntry) : unit =
        w.WriteStartObject()
        w.WriteString("source",   e.Source)
        w.WriteString("severity", severityString e.Severity)
        w.WriteString("code",     e.Code)
        w.WriteString("message",  e.Message)
        match e.SsKey with
        | Some k -> w.WriteString("ssKey", SsKey.rootOriginal k)
        | None   -> w.WriteNull("ssKey")
        // Metadata: write in sorted-key order so byte-determinism
        // holds across hash-table iteration variance.
        w.WritePropertyName("metadata")
        w.WriteStartObject()
        for (k, v) in e.Metadata |> Map.toSeq |> Seq.sortBy fst do
            w.WriteString(k, (v: string))
        w.WriteEndObject()
        // SuggestedConfig: written only when present so default
        // shape stays back-compatible with pre-slice-6 consumers.
        match e.SuggestedConfig with
        | Some cfg ->
            w.WritePropertyName("suggestedConfig")
            writeSuggestedConfig w cfg
        | None -> ()
        w.WriteEndObject()

    /// Render the per-kind document for one kind: ssKey, name, plus
    /// the `entries` array filtered to entries whose `SsKey = Some
    /// k.SsKey`. Empty kinds (no matching entries) emit `entries:
    /// []` — T11 keyset is structural.
    let private writeKindDocument
        (w: Utf8JsonWriter)
        (kind: Kind)
        (entries: DiagnosticEntry list)
        : unit =
        w.WriteStartObject()
        w.WriteString("ssKey", SsKey.rootOriginal kind.SsKey)
        w.WriteString("name",  Name.value kind.Name)
        w.WritePropertyName("entries")
        w.WriteStartArray()
        for e in entries do
            writeEntry w e
        w.WriteEndArray()
        w.WriteEndObject()

    /// Produce the typed `JsonNode` for one kind's document.
    /// `Utf8JsonWriter` → `MemoryStream` → `JsonNode.Parse(
    /// ReadOnlySpan<byte>)` — pillar 1 cash-out: typed seam at
    /// the Π port; strings emerge only at the terminal writer
    /// step.
    let kindJsonNode (kind: Kind) (entries: DiagnosticEntry list) : JsonNode =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, (JsonOptions.compact ()))
            writeKindDocument writer kind entries
            writer.Flush()
        let bytes = stream.ToArray()
        match JsonNode.Parse(System.ReadOnlySpan<byte>(bytes)) with
        | null -> invalidOp "DiagnosticDocument.kindJsonNode: writer produced empty stream (unreachable; writeKindDocument always emits an object)"
        | node -> node

    /// Group entries by their `SsKey` field. Entries with `SsKey =
    /// None` (catalog-level diagnostics) drop at the per-kind seam;
    /// slice δ (CLI wire-up) lifts the catalog-level shape if a
    /// consumer demands it.
    let entriesByKind
        (entries: DiagnosticEntry list)
        : Map<SsKey, DiagnosticEntry list> =
        entries
        |> List.choose (fun e ->
            e.SsKey |> Option.map (fun k -> k, e))
        |> List.groupBy fst
        |> List.map (fun (k, pairs) ->
            k, pairs |> List.map snd)
        |> Map.ofList

    /// Build the per-kind artifact for a given catalog + filtered
    /// entry list. The bench-scope label is the emitter's
    /// responsibility (each sibling sets its own).
    let buildArtifact
        (catalog: Catalog)
        (entries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        let grouped = entriesByKind entries
        ArtifactByKind.perKind catalog (fun k ->
            let kindEntries =
                Map.tryFind k.SsKey grouped |> Option.defaultValue []
            kindJsonNode k kindEntries)


/// Π_DecisionLog — chapter 4.3 slice α emitter for the operator-
/// facing audit-channel artifact. Per the chapter-4.3 routing
/// table (`Routing.fs`): consumes every `DiagnosticEntry` that
/// does NOT match the opportunity / validation Code prefixes — the
/// residual audit-channel content.
///
/// **A18 amended preserved structurally.** The signature carries
/// `Catalog × DiagnosticEntry list` — Diagnostic entries are
/// empirical evidence the passes produce (sibling to Profile in
/// shape; not Policy).
///
/// **T11 sibling-Π commutativity.** Produces `ArtifactByKind<JsonNode>`
/// keyed by every catalog kind; kinds without matching entries get
/// `entries: []`. Structural per `ArtifactByKind.create`.
///
/// **Pillar 1 / Pillar 3 cash-out** (chapter-3.7 slice ε precedent):
/// per-kind value flows through `JsonNode`; strings emerge only at
/// the terminal `Utf8JsonWriter` step.
[<RequireQualifiedAccess>]
module DecisionLogEmitter =

    [<Literal>]
    let version : int = 1

    /// Π_DecisionLog emit. Slice α MVP: every entry routes here
    /// (the routing primitive at `Routing.fs` ships at slice β; the
    /// composer applies the filter when slices β + γ ship sibling
    /// emitters). For callers that have NOT pre-filtered, this
    /// emit produces the full audit log.
    /// **Chapter B.4 slice 6:** entries pass through
    /// `ActionableDiagnostics.organize` first — severity-sorted +
    /// clustered by axis for navigation. No occlusion: every input
    /// entry surfaces in the output (the earlier "cap at N per
    /// axis with overflow marker" design dropped source defects;
    /// the right response to noise is per-finding-type emission
    /// gates at the strategy layer, not after-the-fact suppression
    /// at the emit boundary).
    let emit
        (catalog: Catalog)
        (entries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        use _ = Bench.scope "emit.decisionLog.emit"
        let organized = ActionableDiagnostics.organize entries
        DiagnosticDocument.buildArtifact catalog organized

    /// Π_DecisionLog emit with routing pre-applied. Slice β + γ
    /// callers (the three-emitter composition) route entries
    /// through `Routing.partition` first, then pass the
    /// `DecisionLog`-channel entries here. The signature is the
    /// same as `emit`; the name documents that the caller has
    /// pre-filtered.
    let emitRouted
        (catalog: Catalog)
        (decisionLogEntries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        use _ = Bench.scope "emit.decisionLog.emitRouted"
        DiagnosticDocument.buildArtifact catalog decisionLogEntries


/// Π_Opportunities — chapter 4.3 slice β emitter for the operator-
/// channel artifact (actionable suggestions). Consumes only entries
/// whose `Code` matches the `*.opportunity.*` prefix per the
/// routing table.
///
/// Same architectural shape as `DecisionLogEmitter`; the only
/// difference is the filter applied (via `Routing.route`).
[<RequireQualifiedAccess>]
module OpportunitiesEmitter =

    [<Literal>]
    let version : int = 1

    /// Π_Opportunities emit. Routes entries through `Routing.route`;
    /// only those classified as `Opportunities` survive. Then
    /// `ActionableDiagnostics.organize` sorts by severity + clusters
    /// by axis for navigation. No occlusion.
    let emit
        (catalog: Catalog)
        (entries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        use _ = Bench.scope "emit.opportunities.emit"
        let filtered =
            entries
            |> List.filter (fun e -> Routing.route e = Opportunities)
        let organized = ActionableDiagnostics.organize filtered
        DiagnosticDocument.buildArtifact catalog organized

    /// Π_Opportunities emit with routing pre-applied. Slice β + γ
    /// callers route entries through `Routing.partition` first.
    let emitRouted
        (catalog: Catalog)
        (opportunityEntries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        use _ = Bench.scope "emit.opportunities.emitRouted"
        DiagnosticDocument.buildArtifact catalog opportunityEntries


/// Π_Validations — chapter 4.3 slice γ emitter for the developer-
/// channel artifact (pass-witnessed invariant confirmations).
/// Consumes only entries whose `Code` matches the `*.validation.*`
/// prefix per the routing table.
[<RequireQualifiedAccess>]
module ValidationsEmitter =

    [<Literal>]
    let version : int = 1

    /// Π_Validations emit. Routes entries through `Routing.route`;
    /// only those classified as `Validations` survive. Then
    /// `ActionableDiagnostics.organize` sorts by severity + clusters
    /// by axis for navigation. **No occlusion** — every invariant-
    /// case violation surfaces in the output; operators must see each
    /// one. (Per-finding-type emission gates that reduce noise at
    /// the source live in the strategy/policy layer, not here.)
    let emit
        (catalog: Catalog)
        (entries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        use _ = Bench.scope "emit.validations.emit"
        let filtered =
            entries
            |> List.filter (fun e -> Routing.route e = Validations)
        let organized = ActionableDiagnostics.organize filtered
        DiagnosticDocument.buildArtifact catalog organized

    /// Π_Validations emit with routing pre-applied.
    let emitRouted
        (catalog: Catalog)
        (validationEntries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        use _ = Bench.scope "emit.validations.emitRouted"
        DiagnosticDocument.buildArtifact catalog validationEntries
