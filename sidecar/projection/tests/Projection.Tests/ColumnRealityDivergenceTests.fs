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
let ``F9: a logical NOT NULL column deployed nullable surfaces ONE informational nullability summary`` () =
    // logical mandatory (NOT NULL) vs deployed nullable. Nullability
    // divergences aggregate (schema-reality observation at estate
    // scale); identity divergences stay per-column.
    let s = snapshotOf [ attr 100 "NOTE" true false ] [ reality 100 "NOTE" true false ]
    match divergences s with
    | [ d ] ->
        Assert.Equal<string>("adapter.ossys.columnReality.nullabilityDivergence", d.Code)
        Assert.Equal(DiagnosticSeverity.Info, d.Severity)
        Assert.Equal<string option>(Some "1", Map.tryFind "count" d.Metadata)
        Assert.Equal<string option>(Some "1", Map.tryFind "logicalMandatoryDeployedNullable" d.Metadata)
        Assert.Equal<string option>(Some "0", Map.tryFind "logicalNullableDeployedNotNull" d.Metadata)
        Assert.Equal<string option>(Some "NOTE", Map.tryFind "sample.0" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one nullability summary, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``F9: many nullability divergences still surface exactly one summary, with counts per direction and a capped sample`` () =
    let attrs =
        [ for i in 1 .. 8 -> attr (100 + i) (sprintf "COL%d" i) true false ]   // mandatory
        @ [ attr 200 "LOOSE" false false ]                                     // nullable
    let realities =
        [ for i in 1 .. 8 -> reality (100 + i) (sprintf "COL%d" i) true false ] // deployed nullable
        @ [ reality 200 "LOOSE" false false ]                                   // deployed NOT NULL
    let s = snapshotOf attrs realities
    match divergences s with
    | [ d ] ->
        Assert.Equal<string>("adapter.ossys.columnReality.nullabilityDivergence", d.Code)
        Assert.Equal(DiagnosticSeverity.Info, d.Severity)
        Assert.Equal<string option>(Some "9", Map.tryFind "count" d.Metadata)
        Assert.Equal<string option>(Some "8", Map.tryFind "logicalMandatoryDeployedNullable" d.Metadata)
        Assert.Equal<string option>(Some "1", Map.tryFind "logicalNullableDeployedNotNull" d.Metadata)
        // The sample is capped at 5 (attribute-id order).
        Assert.Equal<string option>(Some "COL5", Map.tryFind "sample.4" d.Metadata)
        Assert.Equal<string option>(None, Map.tryFind "sample.5" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one nullability summary, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``F9: a logical autonumber deployed non-identity surfaces a PER-COLUMN identity warning`` () =
    let s = snapshotOf [ attr 100 "ID" true true ] [ reality 100 "ID" false false ]
    let identity =
        divergences s
        |> List.filter (fun d -> d.Code = "adapter.ossys.columnReality.identityDivergence")
    match identity with
    | [ d ] ->
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
        Assert.Equal<string option>(Some "ID", Map.tryFind "physicalColumn" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one per-column identity warning, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``F9: agreeing logical and deployed facets surface no divergence`` () =
    let s = snapshotOf [ attr 100 "NOTE" true false ] [ reality 100 "NOTE" false false ]
    Assert.Empty(divergences s)

[<Fact>]
let ``F9: an attribute with no deployed reality row surfaces no divergence (total)`` () =
    let s = snapshotOf [ attr 100 "NOTE" true false ] []
    Assert.Empty(divergences s)

[<Fact>]
let ``F9: both facets diverging on one column surface a per-column identity warning plus the nullability summary`` () =
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

// ---------------------------------------------------------------------------
// Deployed-storage lift — `#ColumnReality.SqlType` + facets parse into
// `AttributeRow.DeployedStorage` at the snapshot boundary (the typed channel
// the resolver consults for reference-shaped `bt*` attributes). MaxLength is
// already character-normalized by the rowsets SQL.
// ---------------------------------------------------------------------------

[<Fact>]
let ``toBundle: ColumnReality SqlType and facets lift into AttributeRow.DeployedStorage`` () =
    let s =
        snapshotOf
            [ attr 100 "OFFICEID" true false ]
            [ { reality 100 "OFFICEID" false false with SqlType = Some "nvarchar"; MaxLength = Some 50 } ]
    let bundle = MetadataSnapshotRunner.toBundle s
    let row = bundle.Attributes |> List.find (fun a -> a.AttrId = 100)
    Assert.Equal(Some (SqlStorageType.NVarChar (Bounded 50)), row.DeployedStorage)

[<Fact>]
let ``toBundle: no ColumnReality row means no DeployedStorage evidence`` () =
    let s = snapshotOf [ attr 100 "OFFICEID" true false ] []
    let bundle = MetadataSnapshotRunner.toBundle s
    let row = bundle.Attributes |> List.find (fun a -> a.AttrId = 100)
    Assert.Equal(None, row.DeployedStorage)
