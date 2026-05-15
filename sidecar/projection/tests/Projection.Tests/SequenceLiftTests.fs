module Projection.Tests.SequenceLiftTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter A.0' slice δ — Sequence lift tests.
//
// Mirror of TriggerLiftTests.fs under builder-mediated mode (per
// DECISIONS 2026-05-15 — Closed-DU empirical-test discipline
// refinement). Test sites use `Fixtures.attribute / kind / module' /
// catalog` builders from the start; the new `Catalog.Sequences`
// default flows through the catalog builder so future record
// extensions reach these call sites only through the builder seam.
//
// Two-path coverage of the new `Catalog.Sequences : Sequence list`:
//   - JSON path: V1's top-level `sequences` JSON array flows through
//     `CatalogReader.parseSequence` (called from `parseDocument`).
//   - Rowset path: `RowsetBundle.Sequences : SequenceRow list` flows
//     through `parseSequenceRow` and aggregates the same way. V1's
//     current SQL extraction emits empty per
//     `ModelDeserializerFacade.cs:72`; V2's rowset bundle adds the
//     surface for symmetry with the JSON path so a future loader
//     populates it through this shape.
//
// L3-S5 axiom: every sequence in `Catalog.Sequences` carries
// schema-qualified identity (`Schema` namespace), `Name`, `DataType`
// (free-form SQL Server type), optional bounds (`StartValue` /
// `Increment` / `MinValue` / `MaxValue`), `IsCycleEnabled`,
// `CacheMode` (closed DU), and `CacheSize`. Pillar-9 classification:
// DataIntent (V1 evidence carriage; no operator intent at parse
// time).
// ---------------------------------------------------------------------------

let private mkName s = Name.create s |> Result.value

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    (CatalogReader.parse source).GetAwaiter().GetResult()

let private firstSequence (c: Catalog) : Sequence = c.Sequences |> List.head

// ---------------------------------------------------------------------------
// JSON path — V1 top-level `sequences[]` array. The JSON shape
// matches V1's `Osm.Json/Deserialization/ModelJsonDeserializer
// .SequenceDocument` declaration field-for-field.
// ---------------------------------------------------------------------------

let private jsonWithOneSequence : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": []
    }
  ],
  "sequences": [
    {
      "schema": "dbo",
      "name": "SEQ_INVOICE",
      "dataType": "bigint",
      "startValue": 1,
      "increment": 1,
      "minValue": 1,
      "maxValue": null,
      "cycle": false,
      "cacheMode": "cache",
      "cacheSize": 50
    }
  ]
}"""

let private jsonWithMultipleSequences : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": []
    }
  ],
  "sequences": [
    { "schema": "dbo",   "name": "SEQ_INVOICE", "dataType": "bigint", "startValue": 1,    "increment": 1, "minValue": 1, "maxValue": null,    "cycle": false, "cacheMode": "cache",   "cacheSize": 50 },
    { "schema": "audit", "name": "SEQ_AUDIT",   "dataType": "int",    "startValue": 1000, "increment": 1, "minValue": 1, "maxValue": 999999, "cycle": true,  "cacheMode": "nocache", "cacheSize": null }
  ]
}"""

let private jsonNoSequences : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": []
    }
  ],
  "sequences": []
}"""

let private jsonSequencesPropertyOmitted : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": []
    }
  ]
}"""

let private jsonSequenceCacheModeUnknown : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": []
    }
  ],
  "sequences": [
    { "schema": "dbo", "name": "SEQ_WEIRD", "dataType": "bigint", "cycle": false, "cacheMode": "rotate-eventually", "cacheSize": 16 }
  ]
}"""

let private jsonSequenceCacheModeMissing : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": []
    }
  ],
  "sequences": [
    { "schema": "dbo", "name": "SEQ_DEFAULT", "dataType": "bigint", "cycle": false }
  ]
}"""

let private jsonSequenceCacheWithoutSize : string =
    """{
  "exportedAtUtc": "2026-05-15T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": []
    }
  ],
  "sequences": [
    { "schema": "dbo", "name": "SEQ_BROKEN", "dataType": "bigint", "cycle": false, "cacheMode": "cache", "cacheSize": null }
  ]
}"""

[<Fact>]
let ``L3-S5 slice δ: JSON path carries a single sequence to Catalog.Sequences`` () =
    match parseSync (CatalogReader.SnapshotJson jsonWithOneSequence) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal (1, List.length catalog.Sequences)
        let seq = firstSequence catalog
        Assert.Equal<Name>(mkName "SEQ_INVOICE", seq.Name)
        Assert.Equal ("dbo", seq.Schema)
        Assert.Equal ("bigint", seq.DataType)
        Assert.Equal (Some 1m, seq.StartValue)
        Assert.Equal (Some 1m, seq.Increment)
        Assert.Equal (Some 1m, seq.MinValue)
        Assert.Equal (None, seq.MaxValue)
        Assert.False seq.IsCycleEnabled
        Assert.Equal (Cache, seq.CacheMode)
        Assert.Equal (Some 50, seq.CacheSize)

[<Fact>]
let ``L3-S5 slice δ: JSON path carries multiple sequences preserving per-row attributes`` () =
    match parseSync (CatalogReader.SnapshotJson jsonWithMultipleSequences) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal (2, List.length catalog.Sequences)
        let invoice = catalog.Sequences |> List.find (fun s -> s.Name = mkName "SEQ_INVOICE")
        let audit   = catalog.Sequences |> List.find (fun s -> s.Name = mkName "SEQ_AUDIT")
        Assert.Equal ("dbo", invoice.Schema)
        Assert.Equal ("audit", audit.Schema)
        Assert.False invoice.IsCycleEnabled
        Assert.True audit.IsCycleEnabled
        Assert.Equal (Cache, invoice.CacheMode)
        Assert.Equal (NoCache, audit.CacheMode)
        Assert.Equal (Some 1000m, audit.StartValue)
        Assert.Equal (Some 999999m, audit.MaxValue)

[<Fact>]
let ``L3-S5 slice δ: JSON path with empty sequences array yields empty Catalog.Sequences`` () =
    match parseSync (CatalogReader.SnapshotJson jsonNoSequences) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Empty catalog.Sequences

[<Fact>]
let ``L3-S5 slice δ: JSON path with omitted sequences property yields empty Catalog.Sequences`` () =
    match parseSync (CatalogReader.SnapshotJson jsonSequencesPropertyOmitted) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Empty catalog.Sequences

[<Fact>]
let ``L3-S5 slice δ: unknown cacheMode strings surface as UnsupportedYet (mirrors V1's mapper)`` () =
    // Pillar 9: V2 mirrors V1's `SequenceDocumentMapper
    // .ParseSequenceCacheMode` interpretation verbatim — unknown
    // strings collapse to `UnsupportedYet` rather than dropping the
    // row or coalescing silently to `Unspecified`. The signal stays
    // visible at the IR.
    match parseSync (CatalogReader.SnapshotJson jsonSequenceCacheModeUnknown) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let seq = firstSequence catalog
        Assert.Equal (UnsupportedYet, seq.CacheMode)

[<Fact>]
let ``L3-S5 slice δ: missing cacheMode defaults to Unspecified`` () =
    match parseSync (CatalogReader.SnapshotJson jsonSequenceCacheModeMissing) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let seq = firstSequence catalog
        Assert.Equal (Unspecified, seq.CacheMode)

[<Fact>]
let ``L3-S5 slice δ: Cache with null cacheSize normalizes to UnsupportedYet (mirrors V1 domain layer)`` () =
    // V1's `Osm.Domain.Model.SequenceModel.Create`
    // (`SequenceModel.cs:47-50`) collapses `(Cache, null)` to
    // `UnsupportedYet`. V2 mirrors the normalization so the IR's
    // `(CacheMode, CacheSize)` pair stays internally consistent at
    // the adapter boundary; the harvest classification holds
    // verbatim (DataIntent).
    match parseSync (CatalogReader.SnapshotJson jsonSequenceCacheWithoutSize) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let seq = firstSequence catalog
        Assert.Equal (UnsupportedYet, seq.CacheMode)
        Assert.Equal (None, seq.CacheSize)

// ---------------------------------------------------------------------------
// Rowset path — `RowsetBundle.Sequences : SequenceRow list`. V1's
// SQL extraction does not surface sequences today (per
// `ModelDeserializerFacade.cs:72` — `ImmutableArray<SequenceModel>
// .Empty`); the rowset DTO exists for symmetry with the JSON path
// so a future loader / DACPAC reader populates through it.
// ---------------------------------------------------------------------------

let private moduleRow : CatalogReader.ModuleRow =
    { EspaceId       = 1
      EspaceName     = "AppCore"
      IsSystemModule = false
      IsActive       = true
      EspaceKind     = Some "eSpace"
      EspaceSsKey    = None }

let private sequenceRow
    (schema: string) (name: string) (dataType: string)
    (start: decimal option) (incr: decimal option)
    (minV: decimal option) (maxV: decimal option)
    (cycle: bool) (cacheMode: string option) (cacheSize: int option)
    : CatalogReader.SequenceRow =
    { Schema         = schema
      SequenceName   = name
      DataType       = dataType
      StartValue     = start
      Increment      = incr
      MinValue       = minV
      MaxValue       = maxV
      IsCycleEnabled = cycle
      CacheMode      = cacheMode
      CacheSize      = cacheSize }

[<Fact>]
let ``L3-S5 slice δ: rowset path carries a single sequence to Catalog.Sequences`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = []
          Attributes = []
          References = []
          Triggers   = []
          Sequences  =
            [ sequenceRow
                "dbo" "SEQ_INVOICE" "bigint"
                (Some 1m) (Some 1m) (Some 1m) None
                false (Some "cache") (Some 50) ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Equal (1, List.length catalog.Sequences)
        let seq = firstSequence catalog
        Assert.Equal<Name>(mkName "SEQ_INVOICE", seq.Name)
        Assert.Equal ("dbo", seq.Schema)
        Assert.Equal ("bigint", seq.DataType)
        Assert.Equal (Cache, seq.CacheMode)
        Assert.Equal (Some 50, seq.CacheSize)

[<Fact>]
let ``L3-S5 slice δ: rowset path preserves bounds and cycle flag verbatim`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = []
          Attributes = []
          References = []
          Triggers   = []
          Sequences  =
            [ sequenceRow
                "audit" "SEQ_AUDIT" "int"
                (Some 1000m) (Some 5m) (Some 1m) (Some 999999m)
                true (Some "nocache") None ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let seq = firstSequence catalog
        Assert.Equal (Some 1000m, seq.StartValue)
        Assert.Equal (Some 5m, seq.Increment)
        Assert.Equal (Some 1m, seq.MinValue)
        Assert.Equal (Some 999999m, seq.MaxValue)
        Assert.True seq.IsCycleEnabled
        Assert.Equal (NoCache, seq.CacheMode)

[<Fact>]
let ``L3-S5 slice δ: rowset path with empty SequenceRow list yields empty Catalog.Sequences`` () =
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = []
          Attributes = []
          References = []
          Triggers   = []
          Sequences  = [] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        Assert.Empty catalog.Sequences

[<Fact>]
let ``L3-S5 slice δ: rowset path Cache with null cacheSize normalizes to UnsupportedYet`` () =
    // Same domain-layer invariant as the JSON path (`SequenceModel
    // .Create` in V1's domain). The rowset path applies the
    // normalization uniformly via `normalizeCacheMode` so the IR's
    // `(CacheMode, CacheSize)` invariant holds regardless of source.
    let bundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = []
          Attributes = []
          References = []
          Triggers   = []
          Sequences  =
            [ sequenceRow
                "dbo" "SEQ_BROKEN" "bigint"
                None None None None false (Some "cache") None ] }
    match parseSync (CatalogReader.SnapshotRowsets bundle) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let seq = firstSequence catalog
        Assert.Equal (UnsupportedYet, seq.CacheMode)

// ---------------------------------------------------------------------------
// Cross-source parity — JSON path and rowset path produce identical
// `Sequence` values for matching input. Same shape as
// TriggerLiftTests.fs's parity test.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S5 slice δ: JSON and rowset paths agree on the same sequence shape`` () =
    let jsonResult = parseSync (CatalogReader.SnapshotJson jsonWithOneSequence)
    let rowsetBundle : CatalogReader.RowsetBundle =
        { Modules    = [ moduleRow ]
          Kinds      = []
          Attributes = []
          References = []
          Triggers   = []
          Sequences  =
            [ sequenceRow
                "dbo" "SEQ_INVOICE" "bigint"
                (Some 1m) (Some 1m) (Some 1m) None
                false (Some "cache") (Some 50) ] }
    let rowsetResult = parseSync (CatalogReader.SnapshotRowsets rowsetBundle)
    match jsonResult, rowsetResult with
    | Ok jsonCatalog, Ok rowsetCatalog ->
        Assert.Equal (1, List.length jsonCatalog.Sequences)
        Assert.Equal (1, List.length rowsetCatalog.Sequences)
        let jsonSeq = firstSequence jsonCatalog
        let rowsetSeq = firstSequence rowsetCatalog
        // Both paths synthesize the same SsKey (`OS_SEQ_<schema>
        // _<name>`); neither V1's JSON projection nor the SQL bundle
        // carries a per-sequence Guid today.
        Assert.Equal<SsKey>(jsonSeq.SsKey, rowsetSeq.SsKey)
        Assert.Equal<Name>(jsonSeq.Name, rowsetSeq.Name)
        Assert.Equal (jsonSeq.Schema, rowsetSeq.Schema)
        Assert.Equal (jsonSeq.DataType, rowsetSeq.DataType)
        Assert.Equal (jsonSeq.StartValue, rowsetSeq.StartValue)
        Assert.Equal (jsonSeq.Increment, rowsetSeq.Increment)
        Assert.Equal (jsonSeq.MinValue, rowsetSeq.MinValue)
        Assert.Equal (jsonSeq.MaxValue, rowsetSeq.MaxValue)
        Assert.Equal (jsonSeq.IsCycleEnabled, rowsetSeq.IsCycleEnabled)
        Assert.Equal (jsonSeq.CacheMode, rowsetSeq.CacheMode)
        Assert.Equal (jsonSeq.CacheSize, rowsetSeq.CacheSize)
    | _ ->
        Assert.Fail(sprintf "Expected Ok from both paths; got JSON=%A, Rowset=%A" jsonResult rowsetResult)

// ---------------------------------------------------------------------------
// Pillar-9 worked-example axis — verify the harvest-dichotomy
// classification holds: sequence carriage is DataIntent (skeleton-
// reachable; every V1 sequence surfaces in the IR with no operator
// filter at parse time).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Pillar 9 slice δ: sequence lift is skeleton-reachable (every V1 sequence surfaces in IR)`` () =
    // Per DECISIONS 2026-05-15 — A.0' slice δ amendment: sequence
    // carriage classifies as DataIntent. All sequences from V1's
    // top-level `sequences` array carry through to the IR; emitter
    // overlays own the choice of which sequences to project as DDL
    // (e.g., suppress unused / rewrite for environment promotion /
    // etc.) — those are downstream OperatorIntent, not adapter-time
    // filters.
    match parseSync (CatalogReader.SnapshotJson jsonWithMultipleSequences) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        // 2 sequences in V1 input → 2 sequences in V2 IR; the
        // cycle-true sequence reaches the skeleton alongside the
        // cycle-false one (no filter at parse time).
        Assert.Equal (2, List.length catalog.Sequences)
        Assert.Contains (catalog.Sequences, (fun s -> s.IsCycleEnabled))
        Assert.Contains (catalog.Sequences, (fun s -> not s.IsCycleEnabled))
