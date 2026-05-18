module Projection.Tests.OssysOfflineFixtureParityTests

// V1 parity audit — slice 5.1.δ. Reserves the contract name for
// `V1_PARITY_MATRIX.md` row 37 (V1's offline-test-fixture surface;
// V2 chose a different fixture shape via `SnapshotRowsets` variant).

open Xunit

[<Fact(Skip = "Matrix row 37 — 🟡 DIVERGENCE. V1 ships two manifest-keyed offline fixtures — `FixtureAdvancedSqlExecutor` (implements `IAdvancedSqlExecutor` from pre-canned JSON rowset files) and `FixtureOutsystemsMetadataReader` (implements `IOutsystemsMetadataReader` similarly) — totaling ~450 LOC. Both load from a JSON manifest mapping `(modules + system/inactive flags)` keys to disk-stored JSON rowset files; V1 unit tests + offline pipelines use them to bypass live SQL execution. V2 chose a different fixture shape: `Projection.Adapters.Osm.CatalogReader.SnapshotRowsets` consumes an in-memory `RowsetBundle` constructed directly in F# (via `IRBuilders.fs` and `Fixtures.fs` literal constructors); offline canary tests use this path and bypass `MetadataSnapshotRunner.runAsync` entirely. No fixture-mode entry point exists for the `OssysSql` adapter specifically (the runner only executes against a live `SqlConnection`). See `DECISIONS 2026-05-17 (slice 5.1.δ) — Offline fixture-mode shape: in-memory RowsetBundle over manifest-keyed JSON files`. Re-open trigger: a test scenario surfaces that needs to exercise `MetadataSnapshotRunner.runAsync` against fixture rowsets specifically (e.g., contract-version testing per row 38; failure-mode testing on the SQL layer).")>]
let ``5.1.δ row 37: V1 offline fixture stack vs V2 SnapshotRowsets variant`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 37 + DECISIONS 2026-05-17 (slice 5.1.δ)"

[<Fact>]
let ``5.1.δ: offline-fixture parity file present`` () =
    Assert.True(true)
