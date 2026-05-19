namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE-MUTATION: Function-local capturedDate
//   placeholder before assignment from JSON snapshot header.
//   Tight scope; output is immutable record.

open System
open System.Text.Json
open Projection.Core

/// Boundary adapter — converts V1's `ProfileSnapshot` JSON shape into
/// V2's `Profile` aggregate. The second IR-conversion adapter; follows
/// the canonical pattern set by `Projection.Adapters.Sql.Static`
/// (DECISIONS 2026-05-09 — pattern-setters explicitly named).
///
/// V1 input shape (per
/// `tests/Fixtures/profiling/profile.*.json` and
/// `src/Osm.Json/ProfileSnapshotSerializer.cs`):
///
///   ```
///   { "columns": [
///       { "Schema": "dbo", "Table": "OSUSR_P_PARENT", "Column": "ID",
///         "IsNullablePhysical": false, "IsComputed": false,
///         "IsPrimaryKey": true, "IsUniqueKey": false,
///         "DefaultDefinition": null,
///         "RowCount": 500, "NullCount": 0,
///         "NullCountStatus": {
///             "CapturedAtUtc": "2024-01-01T00:00:00Z",
///             "SampleSize": 500,
///             "Outcome": "Succeeded" } }, ... ],
///     "uniqueCandidates": [...],
///     "compositeUniqueCandidates": [...],
///     "fkReality": [
///       { "Ref": { "FromSchema": ..., "FromTable": ..., "FromColumn": ...,
///                  "ToSchema": ..., "ToTable": ..., "ToColumn": ...,
///                  "HasDbConstraint": true },
///         "HasOrphan": false, "IsNoCheck": false,
///         "ProbeStatus": { ... } }, ... ] }
///   ```
///
/// V2 output: `Profile` keyed by `SsKey` throughout. The adapter
/// resolves V1's physical coordinates to V2 identities by walking the
/// catalog — column profiles match by `(Schema, Table, ColumnName)`
/// against `Kind.Physical` × `Attribute.Column.ColumnName`; FK realities
/// match by `(from, to)` against `Reference.SourceAttribute` and
/// `Reference.TargetKind`. Unresolvable rows are silently skipped — the
/// catalog's selection is the contract, not the JSON's.
///
/// V1 fields V2 does not model are dropped at the boundary:
///   - `IsNullablePhysical`, `IsComputed`, `IsPrimaryKey`,
///     `IsUniqueKey`, `DefaultDefinition` — these are catalog metadata
///     in V2 (`Attribute.Column.IsNullable`, `Attribute.IsPrimaryKey`),
///     not profile evidence. The boundary trusts the catalog and
///     ignores V1's redundant copies.
///   - `NullSample`, `OrphanSample` — operational diagnostics, not
///     IR. V2's `Profile` carries only the empirical-evidence shape.
[<RequireQualifiedAccess>]
module ProfileSnapshot =

    /// Typed source surface for V1's `ProfileSnapshot` JSON. Per the
    /// chapter-3.7 slice ζ cash-out (audit Tier-1 #6), the boundary
    /// adapter's input is structurally a *port* (an external
    /// dependency-injection point); typing it through a closed DU
    /// names the port concept-shape rather than hiding it behind a
    /// raw `string`. Mirrors the `Projection.Adapters.Osm.CatalogReader
    /// .SnapshotSource` shape (chapter 2's precedent) and the sibling
    /// `Static.StaticPopulationsSource`.
    ///
    /// **Single variant today.** `ProfileSnapshotJson` covers every
    /// current consumer (tests; the canary's pipeline assembles the
    /// JSON in memory from probes). Per
    /// `DECISIONS 2026-05-07 — IR grows under evidence`, the future
    /// `ProfileSnapshotFile of path: string` variant lands when a CLI
    /// / pipeline consumer materializes that reads from disk directly
    /// (closed-DU expansion; no signature change for existing JSON
    /// consumers).
    type ProfileSnapshotSource =
        /// In-memory snapshot of V1's `ProfileSnapshot` JSON. The
        /// shape is documented at the top of this module's docstring.
        | ProfileSnapshotJson of json: string

    // -----------------------------------------------------------------------
    // Probe-outcome string ↔ ProbeOutcome mapping. V1 serializes the
    // enum as JSON strings; V2's DU constructors are the parsing target.
    // -----------------------------------------------------------------------

    let private parseOutcome (s: string) : Result<ProbeOutcome> =
        match s with
        | "Succeeded"         -> Result.success Succeeded
        | "FallbackTimeout"   -> Result.success FallbackTimeout
        | "Cancelled"         -> Result.success Cancelled
        | "TrustedConstraint" -> Result.success TrustedConstraint
        | "AmbiguousMapping"  -> Result.success AmbiguousMapping
        // V1 also serializes "Unknown" but the V2 ProbeOutcome DU
        // does not include it — the sidecar's session-2 reading
        // collapsed Unknown into "no probe ran" semantics. If a real
        // fixture surfaces Unknown, extend the DU under "IR grows
        // under evidence."
        | "Unknown"           -> Result.success FallbackTimeout
        | other ->
            Result.failureOf
                (ValidationError.create
                    "profileAdapter.probeOutcome.unknown"
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
                    "profileAdapter.probeStatus.capturedAtUtc.invalid"
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
    // Catalog coordinate → SsKey lookups. Built once per `attach` call;
    // queried per V1 row.
    // -----------------------------------------------------------------------

    type private CatalogIndex = {
        AttributeByCoord : Map<string * string * string, SsKey>
            // (Schema, Table, ColumnName) → Attribute SsKey
        KindByPhysical   : Map<string * string, SsKey>
            // (Schema, Table) → Kind SsKey
        ReferenceByCoord :
            Map<(string * string * string) * (string * string * string), SsKey>
            // ((FromSchema, FromTable, FromColumn),
            //  (ToSchema,   ToTable,   ToColumn))   → Reference SsKey
    }

    let private buildIndex (catalog: Catalog) : CatalogIndex =
        let allKinds = Catalog.allKinds catalog
        let kindByKey =
            allKinds
            |> List.map (fun k -> k.SsKey, k)
            |> Map.ofList

        let attributeByCoord =
            allKinds
            |> List.collect (fun k ->
                k.Attributes
                |> List.map (fun a ->
                    (k.Physical.Schema, k.Physical.Table, a.Column.ColumnName), a.SsKey))
            |> Map.ofList

        let kindByPhysical =
            allKinds
            |> List.map (fun k -> (k.Physical.Schema, k.Physical.Table), k.SsKey)
            |> Map.ofList

        let referenceByCoord =
            allKinds
            |> List.collect (fun k ->
                k.References
                |> List.choose (fun r ->
                    // The source-side coordinate: the source attribute
                    // on the source kind.
                    let sourceAttrColumn =
                        k.Attributes
                        |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                        |> Option.map (fun a -> a.Column.ColumnName)
                    // The target-side coordinate: the target kind's PK
                    // (synthetic-milestone — single-PK assumption; real
                    // composite-FK semantics arrive when fixtures surface
                    // the case).
                    let targetKindOpt = Map.tryFind r.TargetKind kindByKey
                    let targetPkColumn =
                        targetKindOpt
                        |> Option.bind (fun target ->
                            target.Attributes
                            |> List.tryFind (fun a -> a.IsPrimaryKey)
                            |> Option.map (fun a -> a.Column.ColumnName))
                    match sourceAttrColumn, targetKindOpt, targetPkColumn with
                    | Some srcCol, Some target, Some tgtCol ->
                        let coord =
                            (k.Physical.Schema, k.Physical.Table, srcCol),
                            (target.Physical.Schema, target.Physical.Table, tgtCol)
                        Some (coord, r.SsKey)
                    | _ -> None))
            |> Map.ofList

        { AttributeByCoord = attributeByCoord
          KindByPhysical   = kindByPhysical
          ReferenceByCoord = referenceByCoord }

    // -----------------------------------------------------------------------
    // Per-element parsers. Each returns Result<...> on parse failure;
    // the top-level `attach` accumulates and either succeeds with the
    // full Profile or short-circuits on the first parse error.
    //
    // Unresolvable rows (coordinates that don't match any catalog
    // node) are silently skipped — that is the catalog's contract,
    // not an adapter error.
    // -----------------------------------------------------------------------

    let private parseColumnProfile (index: CatalogIndex) (element: JsonElement) : Result<ColumnProfile option> =
        let schema =
            element.GetProperty("Schema").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let table =
            element.GetProperty("Table").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let column =
            element.GetProperty("Column").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        match Map.tryFind (schema, table, column) index.AttributeByCoord with
        | None ->
            // Unresolvable — V1 has profile evidence for an attribute
            // V2's catalog doesn't carry. Skip silently.
            Result.success None
        | Some attributeKey ->
            let rowCount = element.GetProperty("RowCount").GetInt64()
            let nullCount = element.GetProperty("NullCount").GetInt64()
            element.GetProperty("NullCountStatus")
            |> parseProbeStatus
            |> Result.map (fun probeStatus ->
                Some
                    { AttributeKey         = attributeKey
                      RowCount             = rowCount
                      NullCount            = nullCount
                      NullCountProbeStatus = probeStatus })

    let private parseUniqueCandidate (index: CatalogIndex) (element: JsonElement) : Result<UniqueCandidateProfile option> =
        let schema =
            element.GetProperty("Schema").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let table =
            element.GetProperty("Table").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let column =
            element.GetProperty("Column").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        match Map.tryFind (schema, table, column) index.AttributeByCoord with
        | None -> Result.success None
        | Some attributeKey ->
            let hasDuplicate = element.GetProperty("HasDuplicate").GetBoolean()
            element.GetProperty("ProbeStatus")
            |> parseProbeStatus
            |> Result.map (fun probeStatus ->
                Some
                    { AttributeKey = attributeKey
                      HasDuplicate = hasDuplicate
                      ProbeStatus  = probeStatus })

    let private parseCompositeUniqueCandidate (index: CatalogIndex) (element: JsonElement) : Result<CompositeUniqueCandidateProfile option> =
        let schema =
            element.GetProperty("Schema").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        let table =
            element.GetProperty("Table").GetString()
            |> Option.ofObj |> Option.defaultValue ""
        match Map.tryFind (schema, table) index.KindByPhysical with
        | None -> Result.success None
        | Some kindKey ->
            let columns =
                element.GetProperty("Columns").EnumerateArray()
                |> Seq.map (fun c ->
                    c.GetString() |> Option.ofObj |> Option.defaultValue "")
                |> Seq.toList
            let attributeKeys =
                columns
                |> List.choose (fun c ->
                    Map.tryFind (schema, table, c) index.AttributeByCoord)
            // V1's CompositeUniqueCandidateProfile lacks ProbeStatus;
            // V2 adds it (per session-2 design — closing V1's gap).
            // The adapter synthesizes a Succeeded probe at UnixEpoch
            // for V1 inputs that lack the field; if the V1 fixture
            // ever adds the field, parse it here.
            let probe =
                ProbeStatus.create DateTimeOffset.UnixEpoch 0L Succeeded
                |> Result.value
            let hasDuplicate = element.GetProperty("HasDuplicate").GetBoolean()
            Result.success
                (Some
                    { KindKey       = kindKey
                      AttributeKeys = attributeKeys
                      HasDuplicate  = hasDuplicate
                      ProbeStatus   = probe })

    let private parseForeignKeyReality (index: CatalogIndex) (element: JsonElement) : Result<ForeignKeyReality option> =
        // V1 deserializer accepts either "Ref" or "Reference" — match V1's
        // backward compatibility.
        let refElement =
            match element.TryGetProperty("Ref") with
            | true, r -> Some r
            | _ ->
                match element.TryGetProperty("Reference") with
                | true, r -> Some r
                | _       -> None
        match refElement with
        | None -> Result.success None
        | Some r ->
            let fromSchema =
                r.GetProperty("FromSchema").GetString()
                |> Option.ofObj |> Option.defaultValue ""
            let fromTable =
                r.GetProperty("FromTable").GetString()
                |> Option.ofObj |> Option.defaultValue ""
            let fromColumn =
                r.GetProperty("FromColumn").GetString()
                |> Option.ofObj |> Option.defaultValue ""
            let toSchema =
                r.GetProperty("ToSchema").GetString()
                |> Option.ofObj |> Option.defaultValue ""
            let toTable =
                r.GetProperty("ToTable").GetString()
                |> Option.ofObj |> Option.defaultValue ""
            let toColumn =
                r.GetProperty("ToColumn").GetString()
                |> Option.ofObj |> Option.defaultValue ""
            let coord =
                (fromSchema, fromTable, fromColumn),
                (toSchema, toTable, toColumn)
            match Map.tryFind coord index.ReferenceByCoord with
            | None -> Result.success None
            | Some referenceKey ->
                let hasOrphan = element.GetProperty("HasOrphan").GetBoolean()
                let orphanCount =
                    match element.TryGetProperty("OrphanCount") with
                    | true, oc -> oc.GetInt64()
                    | _ -> 0L
                let isNoCheck = element.GetProperty("IsNoCheck").GetBoolean()
                element.GetProperty("ProbeStatus")
                |> parseProbeStatus
                |> Result.map (fun probeStatus ->
                    Some
                        { ReferenceKey = referenceKey
                          HasOrphan    = hasOrphan
                          OrphanCount  = orphanCount
                          IsNoCheck    = isNoCheck
                          ProbeStatus  = probeStatus })

    // -----------------------------------------------------------------------
    // Public surface.
    // -----------------------------------------------------------------------

    let private collectArray
        (parser: JsonElement -> Result<'a option>)
        (root: JsonElement)
        (propertyName: string)
        : Result<'a list> =
        match root.TryGetProperty(propertyName) with
        | true, arr when arr.ValueKind = JsonValueKind.Array ->
            arr.EnumerateArray()
            |> Seq.toList
            |> List.map parser
            |> Result.collect
            |> Result.map (List.choose id)
        | _ ->
            // Property absent or not an array — treat as empty (V1's
            // older fixtures sometimes omit collections).
            Result.success []

    /// Parse a V1 `ProfileSnapshot` JSON document into a V2 `Profile`,
    /// using the catalog to resolve physical coordinates to V2
    /// identities. Unresolvable rows (coordinates absent from the
    /// catalog) are silently skipped — the catalog's selection is
    /// the contract, not the JSON's.
    ///
    /// **Source surface (chapter-3.7 slice ζ).** The input flows
    /// through a typed `ProfileSnapshotSource` DU — concept-shaped
    /// port surface mirroring `CatalogReader.SnapshotSource`. New
    /// variants land via closed-DU expansion in the type definition
    /// above; no signature change at this consumer.
    let attach (catalog: Catalog) (source: ProfileSnapshotSource) : Result<Profile> =
        let profileJson =
            match source with
            | ProfileSnapshotJson json -> json
        try
            use doc = JsonDocument.Parse(profileJson)
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                Result.failureOf
                    (ValidationError.create
                        "profileAdapter.json.shape"
                        "Expected top-level object.")
            else
                let index = buildIndex catalog
                let columnsR = collectArray (parseColumnProfile index) root "columns"
                let uniqueR = collectArray (parseUniqueCandidate index) root "uniqueCandidates"
                let compositeR = collectArray (parseCompositeUniqueCandidate index) root "compositeUniqueCandidates"
                let fkRealityR = collectArray (parseForeignKeyReality index) root "fkReality"

                columnsR
                |> Result.bind (fun columns ->
                    uniqueR
                    |> Result.bind (fun unique ->
                        compositeR
                        |> Result.bind (fun composite ->
                            fkRealityR
                            |> Result.map (fun fkReality ->
                                { Columns                   = columns
                                  UniqueCandidates          = unique
                                  CompositeUniqueCandidates = composite
                                  ForeignKeys               = fkReality
                                  // Distributions populated by the
                                  // ProfileStatistics sibling adapter
                                  // (session 9 commit 3); this V1
                                  // adapter leaves the field empty
                                  // because V1 collects no
                                  // distribution evidence (ADMIRE.md
                                  // 2026-05-12).
                                  Distributions             = []
                                  // AttributeRealities populated by the
                                  // LiveProfiler sibling adapter (slice
                                  // A.4.7'-prelude.live-profiler, 2026-05-19;
                                  // matrix row 49 cash-out). V1's JSON
                                  // ProfileSnapshot has no per-attribute
                                  // reality projection; empty-default keeps
                                  // A34 (Profile independence).
                                  AttributeRealities        = []
                                  // CdcAwareness populated by the
                                  // chapter-3.1 read-side adapter
                                  // extension (slice γ); the V1
                                  // snapshot adapter has no CDC
                                  // discovery surface (per chapter
                                  // 4.1.B slice β).
                                  CdcAwareness              = CdcAwareness.empty
                                  // SourceUsers / TargetUsers populated
                                  // by the chapter 4.2 boundary adapter
                                  // (slice β placeholder fields; per-
                                  // environment user populations are
                                  // empirical evidence the OSSYS adapter
                                  // surfaces from `osm_model.json`).
                                  // V1 ProfileSnapshot has no User-
                                  // population surface; A34 holds —
                                  // empty-default produces identical
                                  // output as no User-FK reflow.
                                  SourceUsers               = UserPopulation.empty
                                  TargetUsers               = UserPopulation.empty }))))
        with
        | :? JsonException as ex ->
            Result.failureOf
                (ValidationError.create
                    "profileAdapter.json.parse"
                    ex.Message)
