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
      DefaultValue = None
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

// ---------------------------------------------------------------------------
// Default-dataspace suppression — `[PRIMARY]` filegroup placement is SQL
// Server's default, not an intentional choice: the snapshot boundary lifts
// only NON-primary filegroups (and partition schemes) into `DataSpace`, so
// emitted DDL never restates `ON [PRIMARY]` from reflection alone.
// ---------------------------------------------------------------------------

let private idxRow (name: string) (dsName: string option) (dsType: string option) : MetadataSnapshotRunner.OssysAllIdxRow =
    { EntityId = 1; ObjectId = 1; IndexId = 2; IndexName = name; IsUnique = false; IsPrimary = false
      Kind = "INDEX"; FilterDefinition = None; IsDisabled = false; IsPadded = false; FillFactor = 0
      IgnoreDupKey = false; AllowRowLocks = true; AllowPageLocks = true; NoRecompute = false
      DataSpaceName = dsName; DataSpaceType = dsType; PartitionColumnsJson = None; DataCompressionJson = None }

[<Fact>]
let ``dataspace: reflected PRIMARY filegroup placement is suppressed at the snapshot boundary`` () =
    let s = { snapshotOf [] [] with Indexes = [ idxRow "IX_T" (Some "PRIMARY") (Some "ROWS_FILEGROUP") ] }
    let bundle = MetadataSnapshotRunner.toBundle s
    let row = bundle.Indexes |> List.find (fun i -> i.IndexName = "IX_T")
    Assert.Equal(None, row.DataSpace)

[<Fact>]
let ``dataspace: a non-primary filegroup is intentional configuration and carries`` () =
    let s = { snapshotOf [] [] with Indexes = [ idxRow "IX_T" (Some "INDEX_FG") (Some "ROWS_FILEGROUP") ] }
    let bundle = MetadataSnapshotRunner.toBundle s
    let row = bundle.Indexes |> List.find (fun i -> i.IndexName = "IX_T")
    Assert.Equal(Some (DataSpace.Filegroup "INDEX_FG"), row.DataSpace)

// ---------------------------------------------------------------------------
// WP-4b (DECISIONS 2026-07-16) — columnStorageDivergences names every
// logical-vs-deployed scalar storage-type disagreement, and reports which
// value the engine emits (deployed for same-category refinements; logical for
// the forced-BIGINT family and cross-category conflicts). bt* refs excluded.
// ---------------------------------------------------------------------------

let private attrT (attrId: int) (col: string) (dataType: string) : MetadataSnapshotRunner.OssysAttributeRow =
    { AttrId = attrId; EntityId = 1; AttrName = col; AttrSsKey = None
      DataType = Some dataType; Length = None; Precision = None; Scale = None
      DefaultValue = None
      IsMandatory = false; IsActive = true; IsAutoNumber = false
      IsIdentifier = false; RefEntityId = None; OriginalName = None
      ExternalDbType = None; DeleteRule = None; PhysicalCol = col
      Description = None; Order = None }

let private realityT (attrId: int) (col: string) (sqlType: string) (maxLen: int option) : MetadataSnapshotRunner.OssysColumnRealityRow =
    { AttrId = attrId; IsNullable = true; SqlType = Some sqlType
      MaxLength = maxLen; Precision = None; Scale = None; CollationName = None
      IsIdentity = false; IsComputed = false; ComputedDefinition = None
      DefaultConstraintName = None; DefaultDefinition = None; PhysicalColumn = Some col }

let private storageDivergences (s: MetadataSnapshotRunner.MetadataSnapshot) =
    MetadataSnapshotRunner.columnStorageDivergences s

[<Fact>]
let ``WP-4b: a same-category storage divergence is named and reports the DEPLOYED value wins`` () =
    // logical text -> NVARCHAR(MAX); deployed VARCHAR(250). Same category (Text).
    let s = snapshotOf [ attrT 100 "NOTES" "text" ] [ realityT 100 "NOTES" "varchar" (Some 250) ]
    match storageDivergences s with
    | [ d ] ->
        Assert.Equal<string>("adapter.ossys.columnReality.storageDivergence", d.Code)
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
        Assert.Equal<string option>(Some "NOTES", Map.tryFind "physicalColumn" d.Metadata)
        Assert.Equal<string option>(Some "deployed", Map.tryFind "emits" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one storage divergence, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``WP-4b: the forced-BIGINT family divergence is named but reports the LOGICAL value wins (C2)`` () =
    // logical identifier -> BIGINT; deployed int. Named, but the engine keeps BIGINT.
    let s = snapshotOf [ attrT 100 "LEGACYID" "Identifier" ] [ realityT 100 "LEGACYID" "int" None ]
    match storageDivergences s with
    | [ d ] ->
        Assert.Equal<string>("adapter.ossys.columnReality.storageDivergence", d.Code)
        Assert.Equal<string option>(Some "logical", Map.tryFind "emits" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one storage divergence, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``WP-4b: a cross-category divergence is named and reports the LOGICAL value wins (no reclassification)`` () =
    // logical rtDate -> DATETIME (the platform mapping, DECISIONS 2026-07-18);
    // a deployed true DATE column (DateTime vs Date category) is a genuine
    // cross-category conflict — the logical value stands, the divergence named.
    let s = snapshotOf [ attrT 100 "BIRTHDATE" "rtDate" ] [ realityT 100 "BIRTHDATE" "date" None ]
    match storageDivergences s with
    | [ d ] -> Assert.Equal<string option>(Some "logical", Map.tryFind "emits" d.Metadata)
    | other -> Assert.Fail(sprintf "expected one storage divergence, got %A" (other |> List.map (fun d -> d.Code)))

[<Fact>]
let ``a date attribute deployed as datetime agrees with the platform mapping and is silent`` () =
    // The real estate's shape: rtDate columns are physically datetime. With the
    // platform mapping (rtDate -> DATETIME) the pair agrees; no divergence fires.
    let s = snapshotOf [ attrT 100 "BIRTHDATE" "rtDate" ] [ realityT 100 "BIRTHDATE" "datetime" None ]
    Assert.Empty(storageDivergences s)

[<Fact>]
let ``WP-4b: an agreeing logical-vs-deployed storage is silent`` () =
    // logical text -> NVARCHAR(MAX); deployed nvarchar with no length -> NVARCHAR(MAX). Equal.
    let s = snapshotOf [ attrT 100 "NOTES" "text" ] [ realityT 100 "NOTES" "nvarchar" None ]
    Assert.Empty(storageDivergences s)

[<Fact>]
let ``WP-4b: a bt* reference attribute is excluded from storage divergence`` () =
    // bt* reference resolves BIGINT logically; deployed int would differ, but
    // references have their own deployed-storage channel and are not named here.
    let s = snapshotOf [ attrT 100 "OFFICEID" "btAbc*Def" ] [ realityT 100 "OFFICEID" "int" None ]
    Assert.Empty(storageDivergences s)
