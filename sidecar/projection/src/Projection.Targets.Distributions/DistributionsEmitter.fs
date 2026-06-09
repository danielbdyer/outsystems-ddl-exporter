namespace Projection.Targets.Distributions

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// The third sibling Π. Consumes the **enriched IR** (Catalog × Profile)
/// and emits a distribution report — for each attribute carrying
/// distribution evidence, the observed vocabulary plus probe metadata.
/// Per the rich-profiling agenda (ADMIRE.md 2026-05-12), this is the
/// first consumer of `AttributeDistribution`; it validates that
/// distribution evidence flows through V2's emission discipline as a
/// sibling functor and not as a one-off formatter.
///
/// **A18 amended (Π takes Catalog or the enriched IR).** The existing
/// SSDT and JSON emitters take `Catalog -> string` because they emit
/// catalog metadata only. The Distributions emitter takes
/// `Catalog -> Profile -> string` because distribution evidence lives
/// on Profile. Both signatures honor the policy-doesn't-flow-through-Π
/// rule (A18); the difference is which of the two evidence inputs the
/// emitter consumes. Future emitters that consume both Catalog and
/// Profile (e.g., the Faker synthetic generator) take the enriched
/// signature; emitters that consume only Catalog continue to take the
/// narrower one.
///
/// **T4 / T11 sibling commutativity.** The output names every catalog
/// kind and attribute by SsKey, regardless of whether profile evidence
/// is present. Attributes without distribution evidence carry
/// `"distribution": null`; attributes with evidence carry the
/// structured payload. Same enrichment ⇒ same output content; the
/// sibling-Π commutativity claim holds across SSDT, JSON, and now
/// Distributions.
[<RequireQualifiedAccess>]
module DistributionsEmitter =

    /// Pass version. Bump when:
    /// - the JSON output shape changes
    /// - new distribution variants land (Numeric, Temporal, ...)
    /// - the determinism contract changes
    ///
    /// v1 (session 9): Categorical rendering only.
    /// v2 (session 10): Numeric rendering added; closed DU dispatch
    ///                  in `writeDistribution` becomes exhaustive on
    ///                  the two-variant DU.
    [<Literal>]
    let version : int = 2

    [<Literal>]
    let private emitterName : string = "Projection.Targets.Distributions"

    // SsKey rendering moved to `Projection.Core.SsKey.display` (sibling
    // to `rootOriginal` / `isDerived`); call sites reference the
    // canonical projection directly.

    let private outcomeString (o: ProbeOutcome) : string =
        match o with
        | Succeeded         -> "Succeeded"
        | FallbackTimeout   -> "FallbackTimeout"
        | Cancelled         -> "Cancelled"
        | TrustedConstraint -> "TrustedConstraint"
        | AmbiguousMapping  -> "AmbiguousMapping"

    let private writeProbeStatus (w: Utf8JsonWriter) (probe: ProbeStatus) : unit =
        w.WriteStartObject()
        w.WriteString("outcome", outcomeString probe.Outcome)
        w.WriteNumber("sampleSize", probe.SampleSize)
        w.WriteString("capturedAtUtc", probe.CapturedAtUtc.ToString("o"))
        w.WriteEndObject()

    let private writeCategorical (w: Utf8JsonWriter) (cat: CategoricalDistribution) : unit =
        w.WriteStartObject()
        w.WriteString("kind", "Categorical")
        w.WriteNumber("distinctCount", cat.DistinctCount)
        w.WriteBoolean("isTruncated", cat.IsTruncated)
        w.WritePropertyName("frequencies")
        w.WriteStartArray()
        for (value, count) in cat.Frequencies do
            w.WriteStartObject()
            w.WriteString("value", value)
            w.WriteNumber("count", count)
            w.WriteEndObject()
        w.WriteEndArray()
        w.WritePropertyName("probe")
        writeProbeStatus w cat.ProbeStatus
        w.WriteEndObject()

    let private writeNumeric (w: Utf8JsonWriter) (num: NumericDistribution) : unit =
        w.WriteStartObject()
        w.WriteString("kind", "Numeric")
        w.WriteNumber("min", num.Min)
        w.WriteNumber("p25", num.P25)
        w.WriteNumber("p50", num.P50)
        w.WriteNumber("p75", num.P75)
        w.WriteNumber("p95", num.P95)
        w.WriteNumber("p99", num.P99)
        w.WriteNumber("max", num.Max)
        w.WriteNumber("sampleSize", num.SampleSize)
        w.WritePropertyName("probe")
        writeProbeStatus w num.ProbeStatus
        w.WriteEndObject()

    let private writeDistribution (w: Utf8JsonWriter) (d: AttributeDistribution) : unit =
        match d with
        | AttributeDistribution.Categorical cat -> writeCategorical w cat
        | AttributeDistribution.Numeric     num -> writeNumeric w num

    let private writeAttribute
        (w: Utf8JsonWriter)
        (profile: Profile)
        (a: Attribute)
        : unit =
        w.WriteStartObject()
        w.WriteString("ssKey", SsKey.display a.SsKey)
        w.WriteString("name", Name.value a.Name)
        w.WriteString("column", ColumnRealization.columnNameText a.Column)
        // Variant-agnostic lookup; the emitter renders whatever
        // distribution variant the IR carries. The closed DU's
        // exhaustiveness on `writeDistribution` ensures every variant
        // has a writer; future variants land as new branches there.
        match Profile.tryFindDistribution a.SsKey profile with
        | Some dist ->
            w.WritePropertyName("distribution")
            writeDistribution w dist
        | None ->
            w.WriteNull("distribution")
        w.WriteEndObject()

    let private writeKind
        (w: Utf8JsonWriter)
        (profile: Profile)
        (k: Kind)
        : unit =
        use _ = Bench.scope "emit.distributions.kind"
        w.WriteStartObject()
        w.WriteString("ssKey", SsKey.display k.SsKey)
        w.WriteString("name", Name.value k.Name)
        w.WriteString("schema", TableId.schemaText k.Physical)
        w.WriteString("table", TableId.tableText k.Physical)
        w.WritePropertyName("attributes")
        w.WriteStartArray()
        // Sort attributes by SsKey for deterministic output regardless
        // of catalog input order — the same discipline that
        // TopologicalOrderPass and the strategy passes apply.
        k.Attributes
        |> List.sortBy (fun a -> a.SsKey)
        |> Bench.iterDo "emit.distributions.attribute" (writeAttribute w profile)
        w.WriteEndArray()
        w.WriteEndObject()

    let private writeModule
        (w: Utf8JsonWriter)
        (profile: Profile)
        (m: Module)
        : unit =
        use _ = Bench.scope "emit.distributions.module"
        w.WriteStartObject()
        w.WriteString("ssKey", SsKey.display m.SsKey)
        w.WriteString("name", Name.value m.Name)
        w.WritePropertyName("kinds")
        w.WriteStartArray()
        m.Kinds
        |> List.sortBy (fun k -> k.SsKey)
        |> Bench.iterDo "emit.distributions.moduleKind" (writeKind w profile)
        w.WriteEndArray()
        w.WriteEndObject()

    // Pinned-deterministic JSON writer options come from
    // `Projection.Core.JsonOptions` — the single sanctioned home
    // for the BCL's mutable `JsonWriterOptions` struct (per the FP
    // strict-mode discipline). Same shape as `JsonEmitter`.

    /// Render one kind's distribution-payload JSON object as a typed
    /// `JsonNode`. Used by `emitSlices` to produce the per-kind value
    /// indexed in `ArtifactByKind`. Property order is fixed by
    /// `writeKind`'s call sequence and matches what the indented
    /// document writer would emit at depth-3, modulo indentation.
    ///
    /// **Pillar-1 cash-out (chapter-3.7 slice ε; audit Tier-1 #7).**
    /// The per-kind value flows through `JsonNode` rather than
    /// `string` so the typed structure survives at the Π port
    /// boundary — strings emerge ONLY at the absolute terminal
    /// `Utf8JsonWriter` step in `emit`. The internal serialization
    /// path (`Utf8JsonWriter` → `MemoryStream` → `byte[]` →
    /// `JsonNode.Parse(ReadOnlySpan<byte>)`) is BCL-typed
    /// end-to-end; no managed `string` is materialized and no
    /// stream-position mutation is needed (the byte-buffer is
    /// passed directly via the span overload).
    let private kindJsonNode (profile: Profile) (k: Kind) : JsonNode =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, (JsonOptions.compact ()))
            writeKind writer profile k
            writer.Flush()
        let bytes = stream.ToArray()
        match JsonNode.Parse(System.ReadOnlySpan<byte>(bytes)) with
        | null  -> invalidOp "DistributionsEmitter.kindJsonNode: writer produced empty stream (unreachable; writeKind always emits an object)"
        | node  -> node

    /// Π port realization (chapter 3.5 slice γ; chapter-3.7 slice ε
    /// pillar-1 cash-out). Profile-consuming Π; per
    /// `EmitterWithProfile<'element>`. T11 is structural by
    /// construction — `ArtifactByKind.create`'s strict-equality
    /// contract guarantees the artifact's keyset equals
    /// `Catalog.allKinds`'s SsKey set. Per pillar 1 (data-structure-
    /// oriented over string-parsing), the per-kind value is a typed
    /// `JsonNode` carrying the kind's distribution-payload structure;
    /// strings emerge only at the terminal `Utf8JsonWriter` boundary
    /// in `emit`. T11 is structural at BOTH the keyset axis AND the
    /// per-kind value-type axis — sibling-Π consumers can mutate the
    /// typed tree (drift detection, post-write enrichment) without
    /// re-parsing.
    let emitSlices : EmitterWithProfile<JsonNode> = fun catalog profile ->
        use _ = Bench.scope "emit.distributions.emitSlices"
        ArtifactByKind.perKind catalog (kindJsonNode profile)

    /// Emit the distribution report as JSON text. Takes the enriched
    /// IR (`Catalog × Profile`) and produces a deterministic string —
    /// byte-identical across repeat invocations on the same input
    /// (T1). Composes through the typed `emitSlices` port so the seam
    /// is exercised by the canonical text realization.
    ///
    /// Output shape (pinned at version 2):
    ///
    ///   ```
    ///   { "emitter": "Projection.Targets.Distributions",
    ///     "version": 2,
    ///     "modules": [ { "ssKey": "...", "name": "...",
    ///       "kinds": [ { ...,
    ///         "attributes": [
    ///           { "ssKey": "...", "name": "...", "column": "...",
    ///             "distribution": null }              // no evidence
    ///           { "ssKey": "...", "name": "...", "column": "...",
    ///             "distribution": {                    // categorical
    ///               "kind": "Categorical",
    ///               "distinctCount": 3, "isTruncated": false,
    ///               "frequencies": [ { "value": "...", "count": 1 }, ... ],
    ///               "probe": { ... } } }
    ///           { "ssKey": "...", "name": "...", "column": "...",
    ///             "distribution": {                    // numeric
    ///               "kind": "Numeric",
    ///               "min": 0, "p25": 10, "p50": 25, "p75": 50,
    ///               "p95": 90, "p99": 99, "max": 100,
    ///               "sampleSize": 100,
    ///               "probe": { ... } } } ] } ] } ] }
    ///   ```
    ///
    /// Determinism: indented; cross-platform `\n` newline; sibling
    /// elements sorted by SsKey at every level (modules + kinds in
    /// the composer; attributes inside per-kind rendering). Profile
    /// lookups go through `Profile.tryFindDistribution` (variant-
    /// agnostic); attributes without distribution evidence emit
    /// `"distribution": null`. Per-variant rendering is dispatched
    /// from `writeDistribution` over the closed DU — adding a new
    /// variant requires extending that dispatch and the smart
    /// constructor for the variant's structural commitments.
    let emit (catalog: Catalog) (profile: Profile) : string =
        use _ = Bench.scope "emit.distributions.emit"
        match emitSlices catalog profile with
        | Error err ->
            invalidOp
                (sprintf
                    "DistributionsEmitter.emit: ArtifactByKind invariant breach: %A"
                    err)
        | Ok artifact ->
            let slices = ArtifactByKind.toMap artifact
            use stream = new MemoryStream()
            do
                use w = new Utf8JsonWriter(stream, (JsonOptions.indented ()))
                w.WriteStartObject()
                w.WriteString("emitter", emitterName)
                w.WriteNumber("version", version)
                w.WritePropertyName("modules")
                w.WriteStartArray()
                let sortedModules =
                    catalog.Modules |> List.sortBy (fun m -> m.SsKey)
                for m in sortedModules do
                    use _ = Bench.scope "emit.distributions.catalogModule"
                    w.WriteStartObject()
                    w.WriteString("ssKey", SsKey.display m.SsKey)
                    w.WriteString("name", Name.value m.Name)
                    w.WritePropertyName("kinds")
                    w.WriteStartArray()
                    let sortedKinds =
                        m.Kinds |> List.sortBy (fun k -> k.SsKey)
                    for k in sortedKinds do
                        use _ = Bench.scope "emit.distributions.moduleKind"
                        match Map.tryFind k.SsKey slices with
                        | Some node ->
                            // Typed JsonNode → writer directly; no
                            // intermediate string. Chapter-3.7 slice ε
                            // retired the prior re-parse path; the
                            // typed JsonNode is the canonical seam.
                            node.WriteTo(w)
                        | None -> ()  // unreachable: T11 guarantees coverage
                    w.WriteEndArray()
                    w.WriteEndObject()
                w.WriteEndArray()
                w.WriteEndObject()
                w.Flush()
            System.Text.Encoding.UTF8.GetString(stream.ToArray())

    /// The "wide" alias mentioned in the codification refinement: an
    /// emitter that consumes the enriched IR. Provided for symmetry
    /// with the existing narrow signature `Catalog -> string`. Some
    /// callers prefer to pre-bundle (Catalog × Profile) and then
    /// uncurry; this is the convenience.
    let emitFromInput (input: ProjectionInput) : string =
        emit input.Catalog input.Profile

    // -----------------------------------------------------------------------
    // Slice 5.13.sibling-emitter-registry-distributions —
    // `registeredMetadata` entry for the DistributionsEmitter sibling Π.
    // Mirrors JsonEmitter / SsdtDdlEmitter on the Profile-evidence axis.
    //
    // **Classification.** All Sites carry `DataIntent`. Profile is
    // *evidence* per A18 amended (Profile-driven observations are
    // DataIntent per pillar 9). The emitter signature
    // `Catalog → Profile → string` honors A18: Catalog + Profile flow
    // through; Policy does not. Distribution evidence projections are
    // observation-shaped, never operator-supplied policy.
    // -----------------------------------------------------------------------

    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "distributionsEmitter" Diagnostics
            [ TransformSite.dataIntent "catalogDocument"
                "Top-level emit assembles `{ emitter, version, modules : [...] }` via `Utf8JsonWriter` with pinned `JsonOptions.indented` (T1 byte-determinism). Catalog × Profile → string projection; per-module envelope wraps `attributeJson` outputs in SsKey-sorted order (T11 sibling commutativity)."
              TransformSite.dataIntent "kindJson"
                "Project Kind → JsonNode via `writeKind` — ssKey / name / attributes[]. Path flows through `kindJsonNode` (`Utf8JsonWriter` → `MemoryStream` → `byte[]` → `JsonNode.Parse`) so the typed JsonNode is the canonical sibling-Π port value (pillar 1)."
              TransformSite.dataIntent "attributeDistributionJson"
                "Per-attribute projection looks up distribution evidence in `Profile.Distributions`; emits `distribution: null` when absent (T4 / T11 — output enumerates every catalog attribute regardless of profile coverage). Profile is evidence (A18 amended); the lookup is a pure projection of observation, not an operator policy decision."
              TransformSite.dataIntent "writeCategorical"
                "Project `CategoricalDistribution` → JsonNode — kind / distinctCount / isTruncated / frequencies / probe. Frequencies array preserves capture order (the IR carries them as `(string * int64) list`); probe status flattens to `outcome / sampleSize / capturedAtUtc` (ISO-8601 round-trip). Categorical evidence is observation-shaped per pillar 9."
              TransformSite.dataIntent "writeNumeric"
                "Project `NumericDistribution` → JsonNode — kind / min / p25 / p50 / p75 / p95 / p99 / max / sampleSize / probe. All percentile fields are `decimal` (T1 byte-determinism per `DECISIONS 2026-05-13 — Decimal as default for continuous statistical evidence`). Closed-DU `AttributeDistribution` exhaustively dispatches Categorical / Numeric."
              TransformSite.dataIntent "writeProbeStatus"
                "Project `ProbeStatus` → JsonNode — outcome / sampleSize / capturedAtUtc. Closed-DU `ProbeOutcome` flattens via `outcomeString` (5 variants: Succeeded / FallbackTimeout / Cancelled / TrustedConstraint / AmbiguousMapping). The probe status IS the per-distribution capture provenance; observation-shaped."
              TransformSite.dataIntent "emitSlices"
                "Π port realization — `Catalog → Profile → Result<ArtifactByKind<JsonNode>, EmitError>` (A35 stream-realization pattern on the Profile-evidence axis). The `EmitterWithProfile<JsonNode>` wide signature honors A18 amended (Catalog × Profile both flow); per-kind `JsonNode` carries the typed structure at the port boundary." ]
