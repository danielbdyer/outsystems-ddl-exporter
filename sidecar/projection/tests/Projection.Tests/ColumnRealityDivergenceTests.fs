module Projection.Tests.ColumnRealityDivergenceTests

open Xunit
open Projection.Core
open Projection.Adapters.OssysSql

// F9 (audit 2026-06-17) — the OSSYS adapter carries the LOGICAL Service-Studio
// nullability/identity; the SAME snapshot fetched the DEPLOYED `#ColumnReality`.
// `MetadataSnapshotRunner.columnRealityDivergences` NAMES each disagreement as
// an operator `Warning` (the carried value is unchanged — the operator decides
// which source is authoritative). These pin the pure detector: it fires on a
// real divergence, stays silent on agreement, and is total over the no-reality
// case.

let private attr (attrId: int) (col: string) (isMandatory: bool) (isAutoNumber: bool) : MetadataSnapshotRunner.OssysAttributeRow =
    { AttrId = attrId; EntityId = 1; AttrName = col; AttrSsKey = None
      DataType = Some "Text"; Length = None; Precision = None; Scale = None
      IsMandatory = isMandatory; IsActive = true; IsAutoNumber = isAutoNumber
      IsIdentifier = false; RefEntityId = None; OriginalName = None
      ExternalDbType = None; DeleteRule = None; PhysicalCol = col
      Description = None; Order = None }

let private reality (attrId: int) (col: string) (isNullable: bool) (isIdentity: bool) : MetadataSnapshotRunner.OssysColumnRealityRow =
    { AttrId = attrId; IsNullable = isNullable; SqlType = Some "nvarchar"
      MaxLength = None; Precision = None; Scale = None; CollationName = None
      IsIdentity = isIdentity; IsComputed = false; ComputedDefinition = None
      DefaultConstraintName = None; DefaultDefinition = None; PhysicalColumn = Some col }

let private snapshotOf
    (attrs: MetadataSnapshotRunner.OssysAttributeRow list)
    (realities: MetadataSnapshotRunner.OssysColumnRealityRow list)
    : MetadataSnapshotRunner.MetadataSnapshot =
    { Modules = []; Entities = []; Attributes = attrs; References = []
      PhysicalTables = []; ColumnReality = realities; ColumnChecks = []
      PhysColsPresent = []; Indexes = []; IndexColumns = []
      ForeignKeysReality = []; ForeignKeyColumns = []; Triggers = [] }

let private divergences (s: MetadataSnapshotRunner.MetadataSnapshot) =
    MetadataSnapshotRunner.columnRealityDivergences s

[<Fact>]
let ``F9: a logical NOT NULL column deployed nullable surfaces a nullability divergence`` () =
    // logical mandatory (NOT NULL) vs deployed nullable.
    let s = snapshotOf [ attr 100 "NOTE" true false ] [ reality 100 "NOTE" true false ]
    match divergences s with
    | [ d ] ->
        Assert.Equal<string>("adapter.ossys.columnReality.nullabilityDivergence", d.Code)
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
        // F#'s `string (b: bool)` renders capitalized ("False"/"True").
        Assert.Equal<string option>(Some "False", Map.tryFind "logicalNullable" d.Metadata)
        Assert.Equal<string option>(Some "True", Map.tryFind "deployedNullable" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one nullability divergence, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``F9: a logical autonumber deployed non-identity surfaces an identity divergence`` () =
    let s = snapshotOf [ attr 100 "ID" true true ] [ reality 100 "ID" false false ]
    let codes = divergences s |> List.map (fun d -> d.Code)
    Assert.Contains("adapter.ossys.columnReality.identityDivergence", codes)

[<Fact>]
let ``F9: agreeing logical and deployed facets surface no divergence`` () =
    let s = snapshotOf [ attr 100 "NOTE" true false ] [ reality 100 "NOTE" false false ]
    Assert.Empty(divergences s)

[<Fact>]
let ``F9: an attribute with no deployed reality row surfaces no divergence (total)`` () =
    let s = snapshotOf [ attr 100 "NOTE" true false ] []
    Assert.Empty(divergences s)

[<Fact>]
let ``F9: both facets diverging on one column surface two distinct diagnostics`` () =
    // logical NOT NULL + autonumber; deployed nullable + non-identity.
    let s = snapshotOf [ attr 100 "ID" true true ] [ reality 100 "ID" true false ]
    let codes = divergences s |> List.map (fun d -> d.Code) |> List.sort
    Assert.Equal<string list>(
        [ "adapter.ossys.columnReality.identityDivergence"
          "adapter.ossys.columnReality.nullabilityDivergence" ],
        codes)
