namespace Projection.Targets.OperationalDiagnostics

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// Π_DecisionLog — chapter 4.3 slice α emitter for the operator-
/// facing audit-channel artifact. Routes every `DiagnosticEntry` the
/// passes produced into a per-kind JSON document keyed by the
/// entry's `SsKey` field (entries without an `SsKey` route to the
/// catalog-level "unscoped" bucket per pre-scope §1.5 slice α
/// acceptance).
///
/// **A18 amended preserved structurally.** The signature carries
/// `Catalog × DiagnosticEntry list` — Diagnostic entries are
/// empirical evidence the passes produce (sibling to Profile in
/// shape; not Policy). Future slices β + γ (`OpportunitiesEmitter` +
/// `ValidationsEmitter`) share the entries via a shared routing
/// primitive that lands at slice β per the two-consumer threshold.
///
/// **T11 sibling-Π commutativity.** The emitter produces an
/// `ArtifactByKind<JsonNode>` keyed by every catalog kind. Kinds
/// without any entries get a JSON document with an empty `entries`
/// array — the keyset coverage is structural per the
/// `ArtifactByKind.create` smart constructor.
///
/// **Pillar 1 / Pillar 3 cash-out** (chapter-3.7 slice ε precedent).
/// Per-kind value flows through `JsonNode` rather than `string` so
/// the typed structure survives at the Π port; strings emerge only
/// at the absolute terminal `Utf8JsonWriter` step. Internal path:
/// `Utf8JsonWriter` → `MemoryStream` → `byte[]` → `JsonNode.Parse(
/// ReadOnlySpan<byte>)`; same architectural shape as JsonEmitter.
[<RequireQualifiedAccess>]
module DecisionLogEmitter =

    [<Literal>]
    let version : int = 1

    // -----------------------------------------------------------------------
    // Per-entry writers.
    // -----------------------------------------------------------------------

    let private severityString (s: DiagnosticSeverity) : string =
        match s with
        | DiagnosticSeverity.Info    -> "Info"
        | DiagnosticSeverity.Warning -> "Warning"
        | DiagnosticSeverity.Error   -> "Error"

    /// Render one `DiagnosticEntry` as a JSON object. Field order is
    /// fixed by the write sequence for T1 byte-determinism.
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
        w.WriteString("ssKey",     SsKey.rootOriginal kind.SsKey)
        w.WriteString("name",      Name.value kind.Name)
        w.WritePropertyName("entries")
        w.WriteStartArray()
        for e in entries do
            writeEntry w e
        w.WriteEndArray()
        w.WriteEndObject()

    /// Produce the typed `JsonNode` for one kind's document. Same
    /// `Utf8JsonWriter` → `MemoryStream` → `JsonNode.Parse(
    /// ReadOnlySpan<byte>)` pipeline as `JsonEmitter.kindJsonNode`
    /// — pillar 1 cash-out: typed seam at the Π port.
    let private kindJsonNode (kind: Kind) (entries: DiagnosticEntry list) : JsonNode =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, (JsonOptions.compact ()))
            writeKindDocument writer kind entries
            writer.Flush()
        let bytes = stream.ToArray()
        match JsonNode.Parse(System.ReadOnlySpan<byte>(bytes)) with
        | null -> invalidOp "DecisionLogEmitter.kindJsonNode: writer produced empty stream (unreachable; writeKindDocument always emits an object)"
        | node -> node

    /// Group entries by their `SsKey` field. Entries with `SsKey =
    /// None` (catalog-level diagnostics) bucket together via a
    /// sibling map keyed by `None` — pre-scope §1.5 slice α
    /// acceptance routes them to the catalog-level "unscoped"
    /// bucket. Slice α MVP: unscoped entries are dropped at the
    /// per-kind seam (every entry that lands in a kind's document
    /// has a matching SsKey); slice η (CLI wire-up) lifts the
    /// catalog-level shape into the emitter's output.
    let private entriesByKind
        (entries: DiagnosticEntry list)
        : Map<SsKey, DiagnosticEntry list> =
        entries
        |> List.choose (fun e ->
            e.SsKey |> Option.map (fun k -> k, e))
        |> List.groupBy fst
        |> List.map (fun (k, pairs) ->
            k, pairs |> List.map snd)
        |> Map.ofList

    /// Π_DecisionLog emit (standalone). Per A18 amended (Catalog +
    /// sibling-evidence input; never Policy). Per T11 (every kind
    /// in the keyset; empty `entries` array when a kind has no
    /// matching diagnostics).
    let emit
        (catalog: Catalog)
        (entries: DiagnosticEntry list)
        : Result<ArtifactByKind<JsonNode>, EmitError> =
        use _ = Bench.scope "emit.decisionLog.emit"
        let grouped = entriesByKind entries
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k ->
                let kindEntries =
                    Map.tryFind k.SsKey grouped |> Option.defaultValue []
                k.SsKey, kindJsonNode k kindEntries)
            |> Map.ofList
        ArtifactByKind.create catalog slices
