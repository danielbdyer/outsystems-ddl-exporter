namespace Projection.Targets.Distributions

open System.IO
open System.Text.Json
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

    let private renderSsKey (key: SsKey) : string =
        let root = SsKey.rootOriginal key
        if SsKey.isDerived key then sprintf "%s [derived]" root else root

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
        w.WriteString("ssKey", renderSsKey a.SsKey)
        w.WriteString("name", Name.value a.Name)
        w.WriteString("column", a.Column.ColumnName)
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
        w.WriteString("ssKey", renderSsKey k.SsKey)
        w.WriteString("name", Name.value k.Name)
        w.WriteString("schema", k.Physical.Schema)
        w.WriteString("table", k.Physical.Table)
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
        w.WriteString("ssKey", renderSsKey m.SsKey)
        w.WriteString("name", Name.value m.Name)
        w.WritePropertyName("kinds")
        w.WriteStartArray()
        m.Kinds
        |> List.sortBy (fun k -> k.SsKey)
        |> Bench.iterDo "emit.distributions.moduleKind" (writeKind w profile)
        w.WriteEndArray()
        w.WriteEndObject()

    /// Emit the distribution report as JSON text. Takes the enriched
    /// IR (`Catalog × Profile`) and produces a deterministic string —
    /// byte-identical across repeat invocations on the same input
    /// (T1).
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
    /// elements sorted by SsKey at every level. Profile lookups go
    /// through `Profile.tryFindDistribution` (variant-agnostic);
    /// attributes without distribution evidence emit
    /// `"distribution": null`. Per-variant rendering is dispatched
    /// from `writeDistribution` over the closed DU — adding a new
    /// variant requires extending that dispatch and the smart
    /// constructor for the variant's structural commitments.
    let emit (catalog: Catalog) (profile: Profile) : string =
        use stream = new MemoryStream()
        let options =
            JsonWriterOptions(
                Indented = true,
                NewLine = "\n",
                SkipValidation = false)
        do
            use w = new Utf8JsonWriter(stream, options)
            w.WriteStartObject()
            w.WriteString("emitter", emitterName)
            w.WriteNumber("version", version)
            w.WritePropertyName("modules")
            w.WriteStartArray()
            catalog.Modules
            |> List.sortBy (fun m -> m.SsKey)
            |> Bench.iterDo "emit.distributions.catalogModule" (writeModule w profile)
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
