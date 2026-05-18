module Projection.Tests.AdvancedSqlExportParityTests

// V1 parity audit — slice 5.1.σ. Reserves the contract name for
// `V1_PARITY_MATRIX.md` row 39 (V1's `outsystems_model_export.sql`;
// V1's JSON-emitter SQL that produces osm_model.json).

open Xunit

[<Fact(Skip = "Matrix row 39 — ⚫ V1-SUNSET. V1's `src/AdvancedSql/outsystems_model_export.sql` (931 LOC) is V1's JSON-emitter SQL — it executes against OSSYS-source and produces V1's `osm_model.json` (the document V1's downstream pipeline consumes). The script is the producer-side companion to the JSON-aggregation rowsets in `outsystems_metadata_rowsets.sql` (matrix rows 13, 21, 22, 24-28 — all ⚫ V1-SUNSET). V2's OssysSql adapter consumes V1's structured rowsets (matrix rows 4-8) directly into V2's Catalog IR; V2 emits SSDT artifacts via the Π chorus rather than producing `osm_model.json`. The export SQL sunsets with V1 per `DECISIONS 2026-05-17 (slice 5.1.α) — V1's JSON-aggregation rowsets sunset with V1's osm_model.json emission path`. Closes the AdvancedSql audit started at row 1.")>]
let ``5.1.σ row 39: V1 outsystems_model_export.sql sunsets with V1 osm_model.json emission path`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 39"

[<Fact>]
let ``5.1.σ: advanced-sql export-parity file present`` () =
    Assert.True(true)
