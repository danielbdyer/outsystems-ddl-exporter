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

// ---------------------------------------------------------------------------
// Primary-key divergence — OSSYS carries PK identity twice (per-attribute
// `Is_Identifier`; per-entity `PrimaryKey_SS_Key`). The reader uses the
// entity key as RECOVERY only; a contradiction (both present, naming
// different attributes) is surfaced by `primaryKeyDivergences`, never
// resolved by the engine.
// ---------------------------------------------------------------------------

let private entity (entityId: int) (name: string) (pkSsKey: System.Guid option) : MetadataSnapshotRunner.OssysEntityRow =
    { EntityId = entityId; EntityName = name; PhysicalTableName = name.ToUpperInvariant()
      EspaceId = 1; IsActive = true; IsSystemEntity = false; IsExternal = false
      DataKind = None; PrimaryKeySsKey = pkSsKey; EntitySsKey = None; Description = None }

let private keyedAttr (attrId: int) (name: string) (ssKey: System.Guid option) (isIdentifier: bool) : MetadataSnapshotRunner.OssysAttributeRow =
    { attr attrId name true false with AttrSsKey = ssKey; IsIdentifier = isIdentifier; AttrName = name }

let private pkSnapshotOf
    (entities: MetadataSnapshotRunner.OssysEntityRow list)
    (attrs: MetadataSnapshotRunner.OssysAttributeRow list)
    : MetadataSnapshotRunner.MetadataSnapshot =
    { snapshotOf attrs [] with Entities = entities }

let private idKey    = System.Guid.Parse("33333333-3333-4333-8333-333333333333")
let private emailKey = System.Guid.Parse("44444444-4444-4444-8444-444444444444")

[<Fact>]
let ``PK divergence: flag and entity key naming different attributes surfaces a warning`` () =
    let s =
        pkSnapshotOf
            [ entity 1 "User" (Some emailKey) ]
            [ keyedAttr 100 "Id" (Some idKey) true
              keyedAttr 101 "Email" (Some emailKey) false ]
    match MetadataSnapshotRunner.primaryKeyDivergences s with
    | [ d ] ->
        Assert.Equal<string>("adapter.ossys.primaryKey.divergence", d.Code)
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
        Assert.Equal<string option>(Some "Id", Map.tryFind "flaggedAttributes" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one PK divergence, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``PK divergence: agreeing flag and entity key surface nothing`` () =
    let s =
        pkSnapshotOf
            [ entity 1 "User" (Some idKey) ]
            [ keyedAttr 100 "Id" (Some idKey) true
              keyedAttr 101 "Email" (Some emailKey) false ]
    Assert.Empty(MetadataSnapshotRunner.primaryKeyDivergences s)

[<Fact>]
let ``PK divergence: an entity key with no flagged attribute is the recovery shape, not a divergence`` () =
    let s =
        pkSnapshotOf
            [ entity 1 "User" (Some idKey) ]
            [ keyedAttr 100 "Id" (Some idKey) false
              keyedAttr 101 "Email" (Some emailKey) false ]
    Assert.Empty(MetadataSnapshotRunner.primaryKeyDivergences s)

[<Fact>]
let ``PK divergence: a flagged attribute with no entity key surfaces nothing (total)`` () =
    let s =
        pkSnapshotOf
            [ entity 1 "User" None ]
            [ keyedAttr 100 "Id" (Some idKey) true ]
    Assert.Empty(MetadataSnapshotRunner.primaryKeyDivergences s)
