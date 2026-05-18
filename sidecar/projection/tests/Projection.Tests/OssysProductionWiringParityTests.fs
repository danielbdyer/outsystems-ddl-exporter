module Projection.Tests.OssysProductionWiringParityTests

// V1 parity audit — slice 5.1.γ. Reserves contract names for
// `V1_PARITY_MATRIX.md` rows 32–36 (V1's production-wiring
// behaviors on `SqlClientOutsystemsMetadataReader` + adjacent
// connection/command/processor abstractions). Each row tracks
// one structurally-orthogonal concern.

open Xunit

[<Fact(Skip = "Matrix row 32 — 🟠 NOT-MAPPED. Exception classification. V1's `MetadataSnapshotRunner` catches three distinct exception classes — `MetadataRowMappingException` (row parsing failure; rebuilds friendly context per processor + row coordinates), `MetadataResultSetMissingException` (contract breach; expected vs. actual rowset count), `DbException` (catch-all). V2's `MetadataSnapshotRunner.runAsync` catches all exceptions under a single `with ex ->` clause that wraps `ex.Message` in `ValidationError.create`; no class-discriminating logic, no friendly-context reconstruction. Trigger: V2 ships a production CLI surface that needs operator-distinguishable failure modes during OSSYS extraction (row-mapping failure vs. contract breach vs. transient SQL error need different operator responses).")>]
let ``5.1.γ row 32: V1 exception classification lifts to V2 OssysSql adapter`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 32"

[<Fact(Skip = "Matrix row 33 — 🟡 DIVERGENCE. Command timeout. V1 reads timeout from `SqlExecutionOptions.CommandTimeoutSeconds` (caller-tunable; falls back to ADO.NET default of 30s when unset); aligns with Polly / EF Core patterns. V2 sets `command.CommandTimeout <- 0` unconditionally (unlimited; tolerates V1's `SET TEXTSIZE -1` + complex queries in canary scope). See `DECISIONS 2026-05-17 (slice 5.1.γ) — Command-timeout discipline: canary unlimited, production tunable`. Trigger to re-promote to PARITY: V2 ships production CLI surface for cloud OSSYS (Azure SQL); add `commandTimeoutSeconds : int option` parameter to `runAsync`.")>]
let ``5.1.γ row 33: V1 tunable command timeout vs V2 unconditional zero`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 33 + DECISIONS 2026-05-17 (slice 5.1.γ)"

[<Fact(Skip = "Matrix row 34 — 🟠 NOT-MAPPED. Transient-error retry. V1's reader has no explicit Polly retry policy; transient handling appears to be delegated to caller orchestration. V2's adapter has zero transient-detection and zero retry — every `SqlException` propagates immediately as a `ValidationError`. Cutover-critical: per V2_DRIVER + R6 split-brain governance, V2's canary must tolerate transient SqlExceptions on cloud OSSYS without producing false-positive divergence reports. Trigger: V2 reads from a cloud OSSYS source (Azure SQL / managed instance) where transient errors are routine. Cash-out shape: Polly retry policy with 3× attempts, exponential backoff, retry on SqlException.Number ∈ {-2 (timeout), -1 (network drop), 40197 / 40501 / 40613 (Azure transients)} at both connection-open and command-execute layers.")>]
let ``5.1.γ row 34: V2 lacks transient-error retry policy on OSSYS extraction`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 34"

[<Fact(Skip = "Matrix row 35 — 🟠 NOT-MAPPED. Result-set count contract enforcement. V1's `MetadataSnapshotRunner.EnsureNextResultSetAsync` fails fast with `MetadataResultSetMissingException` (carrying processor name + row count + expected next set) when an expected result set is absent. V2 reads via a `while hasMore do let! advanced = reader.NextResultAsync()` loop that exits silently when the result-set stream ends; if V1's SQL changes from 22 to 20 result sets, V2 silently accepts the partial data. Trigger: V2's canary fails a parity assertion AND the failure traces back to a SQL-contract-shape change; OR V2 ships a production CLI where silent partial-data acceptance is operator-hostile.")>]
let ``5.1.γ row 35: V2 silently accepts result-set count mismatch on OSSYS rowsets`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 35"

[<Fact(Skip = "Matrix row 36 — 🟠 NOT-MAPPED. Progress tracking. V1 integrates `ITaskProgressAccessor` for per-processor progress ticks during extraction (operator sees `Extracting Metadata: ModuleRow` → `Extracting Metadata: EntityRow` etc.). V2 has no callback or progress-reporting interface; `MetadataSnapshotRunner.runAsync` is opaque from start to finish. Acceptable for the offline canary scope (≤8s warm); operator-hostile at production scale where extraction against a 300-table catalog may run minutes. Trigger: V2 ships a production CLI for OSSYS extraction at full catalog scale OR an operator workflow demands extraction-progress observability. Cash-out shape: optional `onProcessorComplete : (rowsetName : string * rowCount : int) -> unit` parameter.")>]
let ``5.1.γ row 36: V2 lacks per-rowset progress tracking on OSSYS extraction`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 36"

[<Fact>]
let ``5.1.γ: production-wiring parity file present`` () =
    Assert.True(true)
