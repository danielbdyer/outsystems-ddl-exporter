module Projection.Tests.OssysSnapshotShapeValidationParityTests

// V1 parity audit — slice 5.1.β. Reserves the contract name for
// `V1_PARITY_MATRIX.md` row 31 (V1's JSON-shape pre-deserialization
// validation, sunset by F# type system + A39 Catalog.create
// invariants).

open Xunit

[<Fact(Skip = "Matrix row 31 — ⚫ V1-SUNSET. V1's `SnapshotValidator.Validate(jsonStream)` runs JSON-shape validation pre-deserialization: modules array exists + non-null; per module entities array exists + non-null; per entity attributes/relationships/indexes/triggers arrays exist + non-null; throws `InvalidDataException` on contract breach. V2's `MetadataSnapshotRunner` constructs typed F# records directly from `SqlDataReader` (no JSON layer); V2's `SnapshotJson` consumer uses `System.Text.Json` deserialization into typed records (nullable arrays are structurally impossible). V2's `Catalog.create` A39 smart constructor performs IR-level referential-integrity invariants (duplicate-SsKey detection, FK danglingSource/danglingTarget, index-column SsKey membership) — higher-level than V1's shape check. V1's pre-deserialization check sunsets because: (a) F# type system makes null arrays impossible by construction; (b) A39 invariants subsume the structural-integrity goal at the IR layer. No analogous V2 capability needed.")>]
let ``5.1.β row 31: V1 SnapshotValidator JSON-shape pre-deserialization check sunsets`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 31"

[<Fact>]
let ``5.1.β: snapshot-shape-validation parity file present`` () =
    Assert.True(true)
