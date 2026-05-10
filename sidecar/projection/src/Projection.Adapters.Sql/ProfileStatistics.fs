namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE-MUTATION: Function-local capturedDate
//   placeholder (same pattern as ProfileSnapshot.fs).

open System
open System.Text.Json
open Projection.Core

/// Boundary adapter — populates V2's distribution evidence from a
/// V2-defined JSON shape. The third IR-conversion adapter
/// (`Static.fs`, `ProfileSnapshot.fs`, now this); the **first** that
/// has no V1 equivalent to mirror because V1 collects no distribution
/// evidence (ADMIRE.md 2026-05-12).
///
/// The sibling-adapter discipline (DECISIONS 2026-05-11 — the
/// rich-profiling agenda) keeps V1-derived evidence
/// (`ProfileSnapshot.attach`) separate from V2-only evidence
/// (this module). The two adapters compose at the call site:
/// `Profile.empty |> ProfileSnapshot.attach catalog snapshotJson |>
///  Result.bind (ProfileStatistics.attach catalog distributionsJson)`,
/// or any equivalent ordering. A future top-level orchestrator may
/// fan them out, but at N=2 adapters the explicit composition is
/// cheap.
///
/// V2 input shape (proposed in ADMIRE.md 2026-05-12):
///
///   ```
///   { "distributions": [
///       { "Schema": "dbo", "Table": "OSUSR_S1S_COUNTRY", "Column": "CODE",
///         "Kind": "Categorical",
///         "DistinctCount": 3,
///         "IsTruncated": false,
///         "Frequencies": [
///           { "Value": "CA", "Count": 1 },
///           { "Value": "MX", "Count": 1 },
///           { "Value": "US", "Count": 1 } ],
///         "ProbeStatus": { "CapturedAtUtc": "...", "SampleSize": 3,
///                          "Outcome": "Succeeded" } } ] }
///   ```
///
/// The adapter resolves V1-style physical coordinates
/// (`Schema`, `Table`, `Column`) to V2 `AttributeKey`s by walking
/// the catalog — the same coordinate-resolution shape the sibling
/// `ProfileSnapshot.attach` uses, kept consistent for future
/// orchestrator factoring. Unresolvable rows (coordinates absent
/// from the catalog) are silently skipped — same contract as
/// ProfileSnapshot.attach.
///
/// `Kind` is the discriminator string for which `AttributeDistribution`
/// variant to construct. Legal values: `"Categorical"` (session 9),
/// `"Numeric"` (session 10). Future variants land here as new
/// branches when temporal density / joint distributions / etc.
/// arrive.
[<RequireQualifiedAccess>]
module ProfileStatistics =

    /// Typed source surface for the V2 distributions JSON. Per the
    /// chapter-3.7 slice ζ cash-out (audit Tier-1 #6), the boundary
    /// adapter's input is structurally a *port* (an external
    /// dependency-injection point); typing it through a closed DU
    /// names the port concept-shape rather than hiding it behind a
    /// raw `string`. Mirrors the `Projection.Adapters.Osm.CatalogReader
    /// .SnapshotSource` shape (chapter 2's precedent) and the sibling
    /// `Static.StaticPopulationsSource` /
    /// `ProfileSnapshot.ProfileSnapshotSource`.
    ///
    /// **Single variant today.** `DistributionsJson` covers every
    /// current consumer (tests; the canary's pipeline assembles the
    /// JSON in memory from probes). Per
    /// `DECISIONS 2026-05-07 — IR grows under evidence`, the future
    /// `DistributionsFile of path: string` variant lands when a CLI /
    /// pipeline consumer materializes that reads from disk directly
    /// (closed-DU expansion; no signature change for existing JSON
    /// consumers).
    type DistributionsSource =
        /// In-memory snapshot of the V2 distributions JSON. The shape
        /// is the V2-internal distributions surface (sibling to V1's
        /// ProfileSnapshot but carrying empirical-distribution
        /// payloads).
        | DistributionsJson of json: string

    // -----------------------------------------------------------------------
    // Probe-outcome string ↔ ProbeOutcome mapping. Matches the V1 shape
    // exactly (the JSON convention is shared even though no V1 source
    // populates it — consistency with ProfileSnapshot.attach is the
    // contract).
    // -----------------------------------------------------------------------

    let private parseOutcome (s: string) : Result<ProbeOutcome> =
        match s with
        | "Succeeded"         -> Result.success Succeeded
        | "FallbackTimeout"   -> Result.success FallbackTimeout
        | "Cancelled"         -> Result.success Cancelled
        | "TrustedConstraint" -> Result.success TrustedConstraint
        | "AmbiguousMapping"  -> Result.success AmbiguousMapping
        | other ->
            Result.failureOf
                (ValidationError.create
                    "profileStatisticsAdapter.probeOutcome.unknown"
                    (sprintf "Unknown ProbeOutcome value '%s'." other))

    let private parseProbeStatus (status: JsonElement) : Result<ProbeStatus> =
        let capturedRaw =
            status.GetProperty("CapturedAtUtc").GetString()
            |> Option.ofObj
            |> Option.defaultValue ""
        let mutable capturedDate = DateTimeOffset.UnixEpoch
        let parsed = DateTimeOffset.TryParse(capturedRaw, &capturedDate)
        if not parsed then
            Result.failureOf
                (ValidationError.create
                    "profileStatisticsAdapter.probeStatus.capturedAtUtc.invalid"
                    (sprintf "Could not parse CapturedAtUtc '%s'." capturedRaw))
        else
            let sampleSize = status.GetProperty("SampleSize").GetInt64()
            let outcomeRaw =
                status.GetProperty("Outcome").GetString()
                |> Option.ofObj
                |> Option.defaultValue ""
            outcomeRaw
            |> parseOutcome
            |> Result.bind (fun outcome ->
                ProbeStatus.create capturedDate sampleSize outcome)

    // -----------------------------------------------------------------------
    // Catalog coordinate index — built once per `attach` call.
    // -----------------------------------------------------------------------

    let private buildAttributeIndex (catalog: Catalog) : Map<string * string * string, SsKey> =
        Catalog.allKinds catalog
        |> List.collect (fun k ->
            k.Attributes
            |> List.map (fun a ->
                (k.Physical.Schema, k.Physical.Table, a.Column.ColumnName), a.SsKey))
        |> Map.ofList

    // -----------------------------------------------------------------------
    // Categorical-variant parser. Returns Result<AttributeDistribution
    // option> — Some on resolved + valid; None on unresolved coordinate.
    // -----------------------------------------------------------------------

    let private parseFrequencyEntry (element: JsonElement) : Result<string * int64> =
        let valueOpt =
            match element.TryGetProperty("Value") with
            | true, v ->
                match v.ValueKind with
                | JsonValueKind.String -> Some (v.GetString() |> Option.ofObj |> Option.defaultValue "")
                | JsonValueKind.Null   -> Some ""
                // Non-string categorical values are rendered as their
                // raw JSON text (numbers, booleans). Same coercion
                // discipline as Static.fs's invariantString.
                | _ -> Some (v.GetRawText())
            | _ -> None
        match valueOpt with
        | None ->
            Result.failureOf
                (ValidationError.create
                    "profileStatisticsAdapter.frequency.value.missing"
                    "Frequency entry missing 'Value' field.")
        | Some value ->
            match element.TryGetProperty("Count") with
            | true, c -> Result.success (value, c.GetInt64())
            | _ ->
                Result.failureOf
                    (ValidationError.create
                        "profileStatisticsAdapter.frequency.count.missing"
                        "Frequency entry missing 'Count' field.")

    let private parseCategorical
        (attrIndex: Map<string * string * string, SsKey>)
        (element: JsonElement)
        : Result<AttributeDistribution option> =
        let schema =
            element.GetProperty("Schema").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let table =
            element.GetProperty("Table").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let column =
            element.GetProperty("Column").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        match Map.tryFind (schema, table, column) attrIndex with
        | None ->
            // Unresolvable — V2's distribution evidence references an
            // attribute V2's catalog doesn't carry. Skip silently.
            Result.success None
        | Some attributeKey ->
            let distinctCount = element.GetProperty("DistinctCount").GetInt64()
            let isTruncated = element.GetProperty("IsTruncated").GetBoolean()
            let frequenciesR =
                element.GetProperty("Frequencies").EnumerateArray()
                |> Seq.toList
                |> List.map parseFrequencyEntry
                |> Result.collect
            let probeR =
                element.GetProperty("ProbeStatus")
                |> parseProbeStatus
            frequenciesR
            |> Result.bind (fun frequencies ->
                probeR
                |> Result.bind (fun probe ->
                    CategoricalDistribution.create
                        attributeKey frequencies distinctCount isTruncated probe
                    |> Result.map (fun cat ->
                        Some (AttributeDistribution.Categorical cat))))

    // -----------------------------------------------------------------------
    // Numeric-variant parser. Same shape as parseCategorical:
    // resolve coordinate; parse fixed fields; smart-construct;
    // wrap in DU. Returns Result<AttributeDistribution option> —
    // Some on resolved + valid; None on unresolved coordinate.
    //
    // V2 input shape:
    //
    //   { "Schema", "Table", "Column",
    //     "Kind": "Numeric",
    //     "Min", "P25", "P50", "P75", "P95", "P99", "Max",
    //     "SampleSize",
    //     "ProbeStatus": { ... } }
    //
    // The smart constructor (NumericDistribution.create) enforces
    // monotonicity (Min ≤ P25 ≤ … ≤ P99 ≤ Max) and the sample-size
    // floor (≥ 5). Bad fixtures fail loudly through the Result chain.
    // -----------------------------------------------------------------------

    let private parseNumeric
        (attrIndex: Map<string * string * string, SsKey>)
        (element: JsonElement)
        : Result<AttributeDistribution option> =
        let schema =
            element.GetProperty("Schema").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let table =
            element.GetProperty("Table").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let column =
            element.GetProperty("Column").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        match Map.tryFind (schema, table, column) attrIndex with
        | None -> Result.success None
        | Some attributeKey ->
            let min        = element.GetProperty("Min").GetDecimal()
            let p25        = element.GetProperty("P25").GetDecimal()
            let p50        = element.GetProperty("P50").GetDecimal()
            let p75        = element.GetProperty("P75").GetDecimal()
            let p95        = element.GetProperty("P95").GetDecimal()
            let p99        = element.GetProperty("P99").GetDecimal()
            let max        = element.GetProperty("Max").GetDecimal()
            let sampleSize = element.GetProperty("SampleSize").GetInt64()
            element.GetProperty("ProbeStatus")
            |> parseProbeStatus
            |> Result.bind (fun probe ->
                NumericDistribution.create
                    attributeKey min p25 p50 p75 p95 p99 max sampleSize probe
                |> Result.map (fun num ->
                    Some (AttributeDistribution.Numeric num)))

    // -----------------------------------------------------------------------
    // Distribution-kind dispatcher. Dispatches on the JSON's `Kind`
    // field. **Single function with internal dispatch** (per the
    // session-10 framing — splitting into attachCategoricals /
    // attachNumerics would duplicate coordinate resolution). Future
    // variants (Temporal, Joint) land as additional branches here.
    // -----------------------------------------------------------------------

    let private parseDistribution
        (attrIndex: Map<string * string * string, SsKey>)
        (element: JsonElement)
        : Result<AttributeDistribution option> =
        let kindRaw =
            match element.TryGetProperty("Kind") with
            | true, k -> k.GetString() |> Option.ofObj |> Option.defaultValue ""
            | _       -> ""
        match kindRaw with
        | "Categorical" ->
            parseCategorical attrIndex element
        | "Numeric" ->
            parseNumeric attrIndex element
        | other ->
            Result.failureOf
                (ValidationError.create
                    "profileStatisticsAdapter.distribution.kind.unknown"
                    (sprintf "Unknown distribution Kind '%s'. Currently supported: Categorical, Numeric." other))

    // -----------------------------------------------------------------------
    // Public surface.
    // -----------------------------------------------------------------------

    let private collectArray
        (parser: JsonElement -> Result<AttributeDistribution option>)
        (root: JsonElement)
        (propertyName: string)
        : Result<AttributeDistribution list> =
        match root.TryGetProperty(propertyName) with
        | true, arr when arr.ValueKind = JsonValueKind.Array ->
            arr.EnumerateArray()
            |> Seq.toList
            |> List.map parser
            |> Result.collect
            |> Result.map (List.choose id)
        | _ ->
            // Property absent — empty list. Same convention as
            // ProfileSnapshot.attach.
            Result.success []

    /// Parse a V2 distribution-evidence JSON document into the
    /// `AttributeDistribution list` field of the supplied profile.
    /// Designed to compose with `ProfileSnapshot.attach`: call it after
    /// (or before — order doesn't matter) the V1 adapter to combine
    /// V1-derived and V2-only evidence into a single Profile.
    ///
    /// Unresolvable rows (coordinates absent from the catalog) are
    /// silently skipped — same contract as `ProfileSnapshot.attach`.
    /// The catalog's selection is the contract, not the JSON's.
    ///
    /// **Source surface (chapter-3.7 slice ζ).** The input flows
    /// through a typed `DistributionsSource` DU — concept-shaped
    /// port surface mirroring `CatalogReader.SnapshotSource`. New
    /// variants land via closed-DU expansion in the type definition
    /// above; no signature change at this consumer.
    let attach (catalog: Catalog) (source: DistributionsSource) (profile: Profile) : Result<Profile> =
        let distributionsJson =
            match source with
            | DistributionsJson json -> json
        try
            use doc = JsonDocument.Parse(distributionsJson)
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                Result.failureOf
                    (ValidationError.create
                        "profileStatisticsAdapter.json.shape"
                        "Expected top-level object.")
            else
                let attrIndex = buildAttributeIndex catalog
                collectArray (parseDistribution attrIndex) root "distributions"
                |> Result.map (fun newDistributions ->
                    // Append rather than replace — composition with
                    // any existing distributions in the profile (e.g.,
                    // from a prior call). Callers that want
                    // replacement reset Distributions = [] before
                    // calling.
                    { profile with
                        Distributions = profile.Distributions @ newDistributions })
        with
        | :? JsonException as ex ->
            Result.failureOf
                (ValidationError.create
                    "profileStatisticsAdapter.json.parse"
                    ex.Message)
