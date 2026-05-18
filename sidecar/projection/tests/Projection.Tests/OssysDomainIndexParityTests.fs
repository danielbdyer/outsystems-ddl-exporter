module Projection.Tests.OssysDomainIndexParityTests

// V1 parity audit — slice 5.2.α.index. Reserves matrix rows 54–56
// (V1's 8-file index aggregate vs V2's typed Index record).

open Xunit

[<Fact(Skip = "Matrix row 54 — 🟡 DIVERGENCE. V1's `IndexKind` enum has 6 variants (`PrimaryKey | UniqueConstraint | UniqueIndex | NonUniqueIndex | ClusteredIndex | NonClusteredIndex`). V2 decomposes the axis into **boolean flags** `IsPrimaryKey : bool` + `IsUnique : bool` + emitter-side clustered/non-clustered choice. V2's decomposition is structurally equivalent (closed coverage of the 6-variant cross-product) but trades the enum's name-as-constant for boolean composition. V2's choice flows from `IR grows under evidence, not speculation` — when V2 added `IsPrimaryKey` (chapter 3.2) the demand was per-PK behavior, not per-enum-variant. Re-open trigger: an emission consumer needs per-IndexKind dispatch (e.g., a per-variant validation rule); cash-out: lift to `IndexKind = PrimaryKey | UniqueConstraint | UniqueIndex | NonUniqueIndex | ClusteredIndex | NonClusteredIndex` closed DU and rebuild `IsPrimaryKey` / `IsUnique` as derived projections.")>]
let ``5.2.α row 54: V1 IndexKind enum vs V2 IsPrimaryKey+IsUnique boolean decomposition`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 54"

[<Fact(Skip = "Matrix row 55 — 🟠 NOT-MAPPED. V1's `IndexOnDiskMetadata` carries `IsDisabled : bool` (V1 reflects sys.indexes disabled state) and `IgnoreDuplicateKey : bool` (V1 reflects the `IGNORE_DUP_KEY` SQL Server option). V2's `Index` record carries neither. Trigger: emission-axis demands V2 round-trip these fields (e.g., a deployed-target reflection that needs to preserve disabled-index state or `IGNORE_DUP_KEY`-bearing indexes). Cash-out shape: add `Index.IsDisabled : bool` (defaults `false`) + `Index.IgnoreDuplicateKey : bool` (defaults `false`); adapter pickup at OssysSql rowset 10 lift (paired with matrix rows 15+16); emitter consumption at `ScriptDomBuild.buildCreateIndex` (set `IndexStatement.IgnoreDupKey = true`).")>]
let ``5.2.α row 55: V1 IsDisabled + IgnoreDuplicateKey index axes lift to V2 Index IR`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 55"

[<Fact(Skip = "Matrix row 56 — 🟠 NOT-MAPPED. V1's on-disk introspection axis spans 3 files: `IndexDataSpace` (filegroup or partition-scheme name + type), `IndexPartitionColumn` (per-partition column membership + ordinal), `IndexPartitionCompression` (per-partition data compression level). V2 has no partition / data-space / data-compression carriage. Trigger: V2's SSDT emission must support partitioned tables (i.e., a deployed target uses partitioning + V2 must round-trip the partition scheme). Cash-out shape: extend `Index` with `DataSpace : DataSpace option` (closed DU `DataSpace = Filegroup of name | PartitionScheme of name * columns : SsKey list`); add `Index.PartitionCompression : PartitionCompression list` (per-partition typed config). Adapter pickup at OssysSql rowset 10 lift (paired with row 55). **Priority:** low — V2's canary target (synthetic OSSYS) doesn't use partitioning; trigger fires when production OSSYS uses partitioned indexes that V2 must round-trip without losing the partition scheme.")>]
let ``5.2.α row 56: V1 IndexDataSpace + PartitionColumn + PartitionCompression on-disk axes lift to V2 Index IR`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 56"

[<Fact>]
let ``5.2.α.index: domain-index parity file present`` () =
    Assert.True(true)
