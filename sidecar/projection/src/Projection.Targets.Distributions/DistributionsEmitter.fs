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
    /// - new distribution variants land (Numeric, Temporal)
    /// - the determinism contract changes
    [<Literal>]
    let version : int = 1

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

    let private writeDistribution (w: Utf8JsonWriter) (d: AttributeDistribution) : unit =
        match d with
        | AttributeDistribution.Categorical cat -> writeCategorical w cat
        // Numeric rendering lands in session 10 commit 4. Until then
        // the variant is wireable through the IR but not surfaced by
        // the emitter — a documented intermediate state. Tests in
        // commit 2 verify the variant exists; commit 4 wires the
        // rendering and replaces this placeholder.
        | AttributeDistribution.Numeric _ -> ()

    let private writeAttribute
        (w: Utf8JsonWriter)
        (profile: Profile)
        (a: Attribute)
        : unit =
        w.WriteStartObject()
        w.WriteString("ssKey", renderSsKey a.SsKey)
        w.WriteString("name", Name.value a.Name)
        w.WriteString("column", a.Column.ColumnName)
        match Profile.tryFindCategorical a.SsKey profile with
        | Some cat ->
            w.WritePropertyName("distribution")
            writeCategorical w cat
        | None ->
            w.WriteNull("distribution")
        w.WriteEndObject()

    let private writeKind
        (w: Utf8JsonWriter)
        (profile: Profile)
        (k: Kind)
        : unit =
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
        for a in k.Attributes |> List.sortBy (fun a -> a.SsKey) do
            writeAttribute w profile a
        w.WriteEndArray()
        w.WriteEndObject()

    let private writeModule
        (w: Utf8JsonWriter)
        (profile: Profile)
        (m: Module)
        : unit =
        w.WriteStartObject()
        w.WriteString("ssKey", renderSsKey m.SsKey)
        w.WriteString("name", Name.value m.Name)
        w.WritePropertyName("kinds")
        w.WriteStartArray()
        for k in m.Kinds |> List.sortBy (fun k -> k.SsKey) do
            writeKind w profile k
        w.WriteEndArray()
        w.WriteEndObject()

    /// Emit the distribution report as JSON text. Takes the enriched
    /// IR (`Catalog × Profile`) and produces a deterministic string —
    /// byte-identical across repeat invocations on the same input
    /// (T1).
    ///
    /// Output shape (pinned at version 1):
    ///
    ///   ```
    ///   { "emitter": "Projection.Targets.Distributions",
    ///     "version": 1,
    ///     "modules": [
    ///       { "ssKey": "OS_MOD_Sales", "name": "Sales",
    ///         "kinds": [
    ///           { "ssKey": "OS_KIND_Country", ...,
    ///             "attributes": [
    ///               { "ssKey": "OS_ATTR_Country_Code", "name": "Code",
    ///                 "column": "CODE",
    ///                 "distribution": {
    ///                   "kind": "Categorical",
    ///                   "distinctCount": 3, "isTruncated": false,
    ///                   "frequencies": [
    ///                     { "value": "CA", "count": 1 }, ... ],
    ///                   "probe": { ... } } } ] } ] } ] }
    ///   ```
    ///
    /// Determinism: indented; cross-platform `\n` newline; sibling
    /// elements sorted by SsKey at every level. Profile lookups go
    /// through `Profile.tryFindCategorical`; attributes without
    /// distribution evidence emit `"distribution": null`.
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
            for m in catalog.Modules |> List.sortBy (fun m -> m.SsKey) do
                writeModule w profile m
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
