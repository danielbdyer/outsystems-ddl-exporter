module Projection.Tests.OssysDomainIndexParityTests

// V1 parity audit — slice 5.2.α.index. Reserves matrix rows 54–56
// (V1's 8-file index aggregate vs V2's typed Index record).

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------
// Shared fixture for row 55 below — a single-kind catalog with a
// unique index, mirroring the `SsdtDdlEmitterTests.fs`
// "Slice 5.13.index-features-emit" fixture shape (which exercises the
// same two axes exhaustively via property + example tests).
// Parametrized on `IgnoreDuplicateKey` / `IsDisabled` so the row builds
// only the axis combination it needs.
// ---------------------------------------------------------------------

let private idxParityKindKey = kindKey ["IdxWidget"]
let private idxParityIdAttr  = attrKey ["IdxWidget"; "Id"]
let private idxParityNameAttr = attrKey ["IdxWidget"; "Name"]
let private idxParityIdxKey = idxKey ["IdxWidget"; "IX_Name"]

let private idxParityKind (ignoreDup: bool) (disabled: bool) : Kind =
    let idAttr =
        { Attribute.create idxParityIdAttr (mkName "Id") Integer with
            Column       = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true
            IsMandatory  = true }
    let nameAttr =
        { Attribute.create idxParityNameAttr (mkName "Name") Text with
            Column      = ColumnRealization.create "NAME" false |> Result.value
            Length      = Some 100
            IsMandatory = true }
    let idx =
        { Index.ofKeyColumns idxParityIdxKey (mkName "IX_Widget_Name") [ idxParityNameAttr ] with
            Uniqueness         = Unique
            IgnoreDuplicateKey = ignoreDup
            IsDisabled         = disabled }
    { Kind.create
        idxParityKindKey
        (mkName "IdxWidget")
        (mkTableId "dbo" "OSUSR_IW_WIDGET")
        [ idAttr; nameAttr ]
      with Indexes = [ idx ] }

let private idxParityCatalog (ignoreDup: bool) (disabled: bool) : Catalog =
    mkCatalog [ mkModule (modKey "IdxWidgetModule") (mkName "IdxWidgetModule") [ idxParityKind ignoreDup disabled ] ]

let private idxParityBody (catalog: Catalog) : string =
    SsdtDdlEmitter.statements catalog |> Render.toText

[<Fact(Skip = "Matrix row 54 — 🟡 DIVERGENCE. V1's `IndexKind` enum has 6 variants (`PrimaryKey | UniqueConstraint | UniqueIndex | NonUniqueIndex | ClusteredIndex | NonClusteredIndex`). V2 decomposes the axis into **boolean flags** `IsPrimaryKey : bool` + `IsUnique : bool` + emitter-side clustered/non-clustered choice. V2's decomposition is structurally equivalent (closed coverage of the 6-variant cross-product) but trades the enum's name-as-constant for boolean composition. V2's choice flows from `IR grows under evidence, not speculation` — when V2 added `IsPrimaryKey` (chapter 3.2) the demand was per-PK behavior, not per-enum-variant. Re-open trigger: an emission consumer needs per-IndexKind dispatch (e.g., a per-variant validation rule); cash-out: lift to `IndexKind = PrimaryKey | UniqueConstraint | UniqueIndex | NonUniqueIndex | ClusteredIndex | NonClusteredIndex` closed DU and rebuild `IsPrimaryKey` / `IsUnique` as derived projections.")>]
let ``5.2.α row 54: V1 IndexKind enum vs V2 IsPrimaryKey+IsUnique boolean decomposition`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 54"

[<Fact>]
let ``5.2.α row 55: V1 IsDisabled + IgnoreDuplicateKey index axes lift to V2 Index IR and render (matrix row 55 cashed out)`` () : unit =
    // Both axes fired at `Index.IsDisabled` + `Index.IgnoreDuplicateKey`
    // (Catalog.fs:1103-1121). Assert (a) the IR carries `true` for
    // both (distinct from the `false` V1-parity default) and (b) the
    // SSDT emitter renders `IGNORE_DUP_KEY = ON` in the CREATE INDEX
    // WITH clause plus a post-CREATE-INDEX `ALTER INDEX ... DISABLE`.
    let onCatalog = idxParityCatalog true true
    let idx =
        (Catalog.allKinds onCatalog |> List.find (fun k -> k.SsKey = idxParityKindKey)).Indexes
        |> List.exactlyOne
    Assert.True(idx.IgnoreDuplicateKey)
    Assert.True(idx.IsDisabled)
    let onBody = idxParityBody onCatalog
    Assert.Contains("IGNORE_DUP_KEY = ON", onBody)
    Assert.Contains("ALTER INDEX [UIX_IdxWidget_Name]", onBody)
    Assert.Contains("DISABLE", onBody)

    // And the V1-parity default (`false` for both) omits both clauses —
    // proving the axes actually distinguish state rather than always
    // firing.
    let offCatalog = idxParityCatalog false false
    let offBody = idxParityBody offCatalog
    Assert.DoesNotContain("IGNORE_DUP_KEY", offBody)
    Assert.DoesNotContain("ALTER INDEX", offBody)

[<Fact(Skip = "Matrix row 56 — 🟠 NOT-MAPPED. V1's on-disk introspection axis spans 3 files: `IndexDataSpace` (filegroup or partition-scheme name + type), `IndexPartitionColumn` (per-partition column membership + ordinal), `IndexPartitionCompression` (per-partition data compression level). V2 has no partition / data-space / data-compression carriage. Trigger: V2's SSDT emission must support partitioned tables (i.e., a deployed target uses partitioning + V2 must round-trip the partition scheme). Cash-out shape: extend `Index` with `DataSpace : DataSpace option` (closed DU `DataSpace = Filegroup of name | PartitionScheme of name * columns : SsKey list`); add `Index.PartitionCompression : PartitionCompression list` (per-partition typed config). Adapter pickup at OssysSql rowset 10 lift (paired with row 55). **Priority:** low — V2's canary target (synthetic OSSYS) doesn't use partitioning; trigger fires when production OSSYS uses partitioned indexes that V2 must round-trip without losing the partition scheme.")>]
let ``5.2.α row 56: V1 IndexDataSpace + PartitionColumn + PartitionCompression on-disk axes lift to V2 Index IR`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 56"

[<Fact>]
let ``5.2.α.index: domain-index parity file present`` () =
    Assert.True(true)
