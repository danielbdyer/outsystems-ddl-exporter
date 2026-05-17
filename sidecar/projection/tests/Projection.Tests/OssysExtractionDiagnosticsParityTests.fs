module Projection.Tests.OssysExtractionDiagnosticsParityTests

// V1 parity audit — slice 5.1.ε. Reserves the contract name for
// `V1_PARITY_MATRIX.md` row 30 (V1's operator-debugging telemetry
// surface during SQL extraction). When V2 cashes out the capability,
// the Skip flips to a real assertion exercising the lift.

open Xunit

[<Fact(Skip = "Matrix row 30 — 🟠 NOT-MAPPED. V1's operator-debugging telemetry surface during SQL extraction spans 3 files: `Pipeline/Sql/SqlMetadataLog.cs` (in-memory observation accumulator — snapshot-on-success / errors-on-failure / per-request payloads), `Pipeline/SqlExtraction/MetadataRowSnapshot.cs` (last-row-context-on-failure carrier for the mapping site that threw), `Pipeline/SqlExtraction/SqlMetadataDiagnosticsWriter.cs` (JSON-dump emitter writing the log to an operator-provided path). V2's `MetadataSnapshotRunner.runAsync` returns `Result<MetadataSnapshot>` with success or a single `ValidationError` on failure; no observation accumulator, no row-snapshot-on-failure, no JSON-dump emitter. Trigger: V2 ships a CLI surface for production OSSYS extraction OR a cutover-windowed failure mode demands partial-state context for post-mortem debugging.")>]
let ``5.1.ε row 30: V1 operator-debugging telemetry surface lifts to V2 OssysSql adapter`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 30"

[<Fact>]
let ``5.1.ε: diagnostics parity inventory file present`` () =
    // Anchor test mirroring the OssysRowsetParityInventoryTests
    // pattern — surfaces this Diagnostics-axis inventory file in
    // test discovery so future slices flipping the Skip stub above
    // surface immediately in the run.
    Assert.True(true)
