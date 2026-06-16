module Projection.Tests.DataIntegrityCheckerTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Slice 4.4 — pure-pool coverage of DataIntegrityChecker.diff / isClean.
// The end-to-end gate (capture two deployments + diff) is exercised under
// Docker in VerifyDataIntegrationTests; these tests pin the pure diff
// algebra deterministically without a SQL Server.
// ---------------------------------------------------------------------------

let private cachedKind (key: SsKey) (rowCount: int64) (nulls: (SsKey * int64) list) : CachedKind =
    {
        KindKey      = key
        RowCount     = rowCount
        NullCounts   = Map.ofList nulls
        Columns      = []
        ColumnsByKey = Map.empty
    }

let private cache (kinds: CachedKind list) : EvidenceCache =
    { Kinds = kinds |> List.map (fun k -> k.KindKey, k) |> Map.ofList }

let private items = kindKey ["Items"]
let private orders = kindKey ["Orders"]
let private noteAttr = attrKey ["Orders"; "Note"]

[<Fact>]
let ``diff: identical caches are clean`` () =
    let c = cache [ cachedKind items 3L []; cachedKind orders 2L [ noteAttr, 0L ] ]
    let report = DataIntegrityChecker.diff c c
    Assert.True(DataIntegrityChecker.isClean report)
    Assert.Empty(report.RowCountDeltas)
    Assert.Empty(report.NullCountDeltas)
    Assert.Empty(report.Warnings)

[<Fact>]
let ``diff: flags exactly the diverging kind's row-count delta`` () =
    let before = cache [ cachedKind items 3L []; cachedKind orders 2L [] ]
    let after  = cache [ cachedKind items 6L []; cachedKind orders 2L [] ]
    let report = DataIntegrityChecker.diff before after
    Assert.Equal(1, report.RowCountDeltas.Length)
    let d = List.head report.RowCountDeltas
    Assert.Equal(items, d.Kind)
    Assert.Equal(3L<row>, d.Before)
    Assert.Equal(6L<row>, d.After)
    Assert.False(DataIntegrityChecker.isClean report)

[<Fact>]
let ``diff: flags a per-attribute null-count delta (row count unchanged)`` () =
    let before = cache [ cachedKind orders 2L [ noteAttr, 0L ] ]
    let after  = cache [ cachedKind orders 2L [ noteAttr, 1L ] ]
    let report = DataIntegrityChecker.diff before after
    Assert.Empty(report.RowCountDeltas)
    Assert.Equal(1, report.NullCountDeltas.Length)
    let d = List.head report.NullCountDeltas
    Assert.Equal(orders, d.Kind)
    Assert.Equal(noteAttr, d.Attribute)
    Assert.Equal(0L<row>, d.Before)
    Assert.Equal(1L<row>, d.After)

[<Fact>]
let ``diff: kind present in only one cache surfaces a warning`` () =
    let before = cache [ cachedKind items 3L []; cachedKind orders 2L [] ]
    let after  = cache [ cachedKind items 3L [] ]
    let report = DataIntegrityChecker.diff before after
    Assert.Empty(report.RowCountDeltas)
    Assert.Equal(1, report.Warnings.Length)
    let w = List.head report.Warnings
    Assert.Equal("verifyData.kind.missingInAfter", w.Code)
    Assert.Equal<SsKey option>(Some orders, w.SsKey)
    Assert.False(DataIntegrityChecker.isClean report)

[<Fact>]
let ``diff: deltas are sorted by SsKey (T1 determinism)`` () =
    // Build with both kinds diverging; assert the report's RowCountDeltas
    // come back in SsKey order regardless of map enumeration.
    let before = cache [ cachedKind items 1L []; cachedKind orders 1L [] ]
    let after  = cache [ cachedKind items 2L []; cachedKind orders 2L [] ]
    let report = DataIntegrityChecker.diff before after
    let keys = report.RowCountDeltas |> List.map (fun d -> d.Kind)
    Assert.Equal<SsKey list>(List.sort keys, keys)
