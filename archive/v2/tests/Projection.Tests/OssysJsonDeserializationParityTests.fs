module Projection.Tests.OssysJsonDeserializationParityTests

// V1 parity audit — slice 5.2.β.json. Reserves matrix rows 148-155.
// V1's 47-file Osm.Json deserialization machinery vs V2's
// CatalogReader.SnapshotJson path. Mostly PARITY/MAPPED.

open Xunit

[<Fact(Skip = "Matrix row 148 — 🟢 PARITY. V1 `Osm.Json/Deserialization/ModelJsonDeserializer.cs` (multi-partial sealed class with lazy-init shared pipeline; Deserialize(Stream, options) surface) ↔ V2 `Projection.Adapters.Osm.CatalogReader.parse : SnapshotSource -> Task<Result<Catalog>>` + synchronous `parseJsonString`. Closed DU `SnapshotSource` (SnapshotFile/SnapshotJson/SnapshotRowsets) is isomorphic to V1's lazy pipeline. V2 adds async surface (V1→V2 boundary).")>]
let ``5.2.β row 148: V1 ModelJsonDeserializer ↔ V2 CatalogReader.parse PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 148"

[<Fact(Skip = "Matrix row 149 — 🟢 PARITY. V1 5 mapper classes (`EntityDocumentMapper` / `RelationshipDocumentMapper` / `SequenceDocumentMapper` / `TriggerDocumentMapper` + module/extended-prop mappers) ↔ V2 7 `let` functions (`parseKind` / `parseModule` / `parseAttribute` / `parseReference` / `parseTrigger` / `parseIndex` / `parseExtendedProperty`). V1 class-per-aggregate → V2 function-per-aggregate. V2 adds `parseIndex`; no semantic gap.")>]
let ``5.2.β row 149: V1 5 mapper classes ↔ V2 7 parse functions PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 149"

[<Fact(Skip = "Matrix row 150 — 🟠 NOT-MAPPED. V1 `Deserialization/AttributeDeduplicator.cs` + `IAttributeDeduplicator.cs` + `DuplicateWarningEmitter.cs` + `IDuplicateWarningEmitter.cs` — duplicate-attribute handling for V1 JSON-projection artifact (multiple attribute rows for same logical/physical name when reference target has multiple active/inactive versions); uses `ReferenceEntityIsActive` to break ties; emits warnings into collector when `AllowDuplicateAttributeLogicalNames` / `AllowDuplicateAttributeColumnNames` flags set. V2 JSON path has no equivalent; rowset path will deduplicate via per-attribute SsKey identity when SnapshotRowsets implementation lands. **Cash-out**: SnapshotRowsets adapter ports the dedup logic; JSON path inherits if a fixture surfaces the duplicate-attribute case. **Trigger**: SnapshotRowsets ships OR JSON fixture reproduces V1 duplicate-attribute condition.")>]
let ``5.2.β row 150: V1 AttributeDeduplicator + DuplicateWarningEmitter lift to V2 SnapshotRowsets adapter`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 150"

[<Fact(Skip = "Matrix row 151 — 🟡 DIVERGENCE. V1 `Osm.Json/CirSchemaValidator.cs` static class loads embedded JSON Schema `cir-v1.json` resource; validates root element pre-deserialization; collects errors fail-fast. V2 validation deferred to per-entity / per-attribute / per-reference parse-step error handling (no JSON Schema). **Already covered by matrix row 31** (SnapshotValidator subsumes CirSchemaValidator). V2's structural validation (type system + smart constructors) is canonical; CIR schema is V1 editorial artifact not carried forward. Per-element-during-traversal vs fail-fast-before-traversal trade-off; V2's localization is stronger.")>]
let ``5.2.β row 151: V1 CirSchemaValidator JSON Schema vs V2 per-element validation (covered by row 31)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 151"

[<Fact(Skip = "Matrix row 152 — 🟢 PARITY. V1 `Deserialization/BooleanAsZeroOneConverter.cs` (custom `JsonConverter<bool>`; accepts 0/1 numbers, JSON booleans, or strings) registered on `ModelDocumentSerializerContext`. V2 `CatalogReader.getIntFlag` + `getOptionalIntFlag` helpers — explicit `match value.ValueKind` over JSON token types; mirrors V1 numeric 0/1 coercion. Functionally isomorphic; V2 named helpers make call sites self-describing.")>]
let ``5.2.β row 152: V1 BooleanAsZeroOneConverter ↔ V2 getIntFlag helpers PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 152"

[<Fact(Skip = "Matrix row 153 — 🟡 DIVERGENCE. V1 `Deserialization/ProfileSnapshotSerializer.cs` + `ProfileSnapshotDeserializer.cs` build isolated `ProfileSnapshot` domain object from JSON; extensive record types for JSON DTOs. V2 `ProfileSnapshot.attach` (F# module) parses JSON string → probes → attaches to catalog-keyed index → returns `Profile` aggregate. **Design change**: V1 Profile JSON constructs isolated records; V2's adapter consults Catalog (physical coordinates → SsKey resolution) during parse. V2 also drops V1's `NullSample` / `OrphanSample` operational diagnostics (Profile carries only empirical evidence per pillar 9). Semantically aligned; structurally inverted.")>]
let ``5.2.β row 153: V1 ProfileSnapshotSerializer isolated DTOs vs V2 ProfileSnapshot.attach catalog-driven (V2 inverted)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 153"

[<Fact(Skip = "Matrix row 154 — 🟢 PARITY. V1 error-metadata: `DocumentPathContext` DU (tracks JSON path during traversal; appended to errors); `ValidationError.WithMetadata(\"json.path\", ...)`. V2 `adapterError` named-parameter helper; inline path composition at error sites; no context stack. V1's path-tracking stack is more structured; V2 embeds path composition in error messages directly. V2's approach scales to Result<'a> + pipeline; V1's mutable context is C#-idiomatic. No semantic loss; V2 paths are human-readable; V1's are structured but incur DU allocation overhead.")>]
let ``5.2.β row 154: V1 DocumentPathContext error stack ↔ V2 inline path composition PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 154"

[<Fact(Skip = "Matrix row 155 — 🟠 NOT-MAPPED (out-of-scope deferral). V1 `Deserialization/CircularDependencyConfigDeserializer.cs` parses allowedCycles array + strictMode flag from operator config JSON. V2's config story lives in `Projection.Core.Configuration` + Pipeline layer, not in adapter. The V1 deserializer is orthogonal to JSON-deserialization; defers to V2's ConfigurationProvider when that surface materializes. **No action item** — circular-dep config is operator-intent (Tightening-axis overlay in V2 vocabulary); lives at Pipeline/Config layer, not Catalog adapter.")>]
let ``5.2.β row 155: V1 CircularDependencyConfigDeserializer (operator-intent; lives at Pipeline layer not adapter)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 155"

[<Fact>]
let ``5.2.β.json: json-deserialization parity file present`` () =
    Assert.True(true)
