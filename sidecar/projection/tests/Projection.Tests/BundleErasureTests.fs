module Projection.Tests.BundleErasureTests

open Xunit
open Projection.Core
open Projection.Adapters.OssysSql

// align-II.8 (E1; audit a1) — A54: the bundle projection's erasures are
// NAMED VALUES. `toBundle` returns `RowsetBundle * BundleErasure list`;
// the constant modulus (the by-design folds + capability-invariance) is
// present on every projection, and the data-dependent assumptions/drops
// fire exactly when their shape occurs. Nothing the projection loses is
// silent — the adjunction's modulus at this seam is enumerable.

let private moduleRow : MetadataSnapshotRunner.OssysModuleRow =
    { EspaceId = 1; EspaceName = "Sales"; IsSystemModule = false; IsActive = true
      EspaceKind = None; EspaceSsKey = None }

let private entity (entityId: int) (name: string) : MetadataSnapshotRunner.OssysEntityRow =
    { EntityId = entityId; EntityName = name; PhysicalTableName = sprintf "OSUSR_X_%s" (name.ToUpperInvariant())
      EspaceId = 1; IsActive = true; IsSystemEntity = false; IsExternal = false
      DataKind = None; PrimaryKeySsKey = None; EntitySsKey = None; Description = None }

let private physicalTable (entityId: int) : MetadataSnapshotRunner.OssysPhysicalTableRow =
    { EntityId = entityId; SchemaName = "sales"; TableName = "OSUSR_X_T"; ObjectId = 900 + entityId }

let private attrWithType (attrId: int) (dataType: string option) : MetadataSnapshotRunner.OssysAttributeRow =
    { AttrId = attrId; EntityId = 1; AttrName = sprintf "Attr%d" attrId; AttrSsKey = None
      DataType = dataType; Length = None; Precision = None; Scale = None
      DefaultValue = None
      IsMandatory = false; IsActive = true; IsAutoNumber = false
      IsIdentifier = false; RefEntityId = None; OriginalName = None
      ExternalDbType = None; DeleteRule = None; PhysicalCol = sprintf "ATTR%d" attrId
      Description = None; Order = None }

let private referenceRow (attrId: int) (refName: string option) : MetadataSnapshotRunner.OssysReferenceRow =
    { AttrId = attrId; RefEntityId = Some 2; RefEntityName = refName; RefPhysicalName = None }

let private snapshotWith
    (entities: MetadataSnapshotRunner.OssysEntityRow list)
    (physicalTables: MetadataSnapshotRunner.OssysPhysicalTableRow list)
    (attrs: MetadataSnapshotRunner.OssysAttributeRow list)
    (references: MetadataSnapshotRunner.OssysReferenceRow list)
    : MetadataSnapshotRunner.MetadataSnapshot =
    { Modules = [ moduleRow ]; Entities = entities; Attributes = attrs; References = references
      PhysicalTables = physicalTables; ColumnReality = []; ColumnChecks = []; Sequences = []; Temporal = []
      PhysColsPresent = []; Indexes = []; IndexColumns = []
      ForeignKeysReality = []; ForeignKeyColumns = []; Triggers = []
      Capabilities = []
      Scope = MetadataSnapshotRunner.AcquisitionScope.Total }

/// A snapshot every projection joins cleanly — the constant modulus alone.
let private fullyJoined () : MetadataSnapshotRunner.MetadataSnapshot =
    snapshotWith [ entity 1 "Customer" ] [ physicalTable 1 ] [ attrWithType 10 (Some "Text") ] []

let private erasuresOf (s: MetadataSnapshotRunner.MetadataSnapshot) : MetadataSnapshotRunner.BundleErasure list =
    snd (MetadataSnapshotRunner.toBundle s)

let private isConstant (e: MetadataSnapshotRunner.BundleErasure) : bool =
    match e with
    | MetadataSnapshotRunner.BundleErasure.FoldedRowset _
    | MetadataSnapshotRunner.BundleErasure.CapabilityInvariant -> true
    | _ -> false

[<Fact>]
let ``A54 modulus: a fully-joined snapshot erases exactly the constant modulus — the by-design folds and capability-invariance, nothing else`` () =
    let erasures = erasuresOf (fullyJoined ())
    Assert.True(erasures |> List.forall isConstant,
        sprintf "a fully-joined snapshot minted a data-dependent erasure: %A" erasures)
    // Capability-invariance is stated as a value; the folds name their rowsets.
    Assert.Contains(MetadataSnapshotRunner.BundleErasure.CapabilityInvariant, erasures)
    let foldedRowsets =
        erasures
        |> List.choose (function
            | MetadataSnapshotRunner.BundleErasure.FoldedRowset (rowset, _) -> Some rowset
            | _ -> None)
    Assert.Equal<string list>([ "physColsPresent"; "fkReality"; "columnReality"; "entities" ], foldedRowsets)

[<Fact>]
let ``A54 modulus: every folded rowset cites a name the rowset contract carries — the erasure record and the wire contract share one vocabulary`` () =
    let contractNames =
        MetadataSnapshotRunner.RowsetContract.all |> List.map (fun e -> e.Name) |> Set.ofList
    for e in erasuresOf (fullyJoined ()) do
        match e with
        | MetadataSnapshotRunner.BundleErasure.FoldedRowset (rowset, detail) ->
            Assert.True(Set.contains rowset contractNames, sprintf "'%s' is not a contract rowset" rowset)
            Assert.False(System.String.IsNullOrWhiteSpace detail, sprintf "'%s' folds with no detail" rowset)
        | _ -> ()

[<Fact>]
let ``A54: an entity with no physical-table row erases as AssumedSchema and the bundle assumes dbo — exactly when the join misses`` () =
    let missing = snapshotWith [ entity 1 "Customer"; entity 2 "Order" ] [ physicalTable 1 ] [] []
    let bundle, erasures = MetadataSnapshotRunner.toBundle missing
    Assert.Contains(MetadataSnapshotRunner.BundleErasure.AssumedSchema (2, "Order"), erasures)
    Assert.DoesNotContain(MetadataSnapshotRunner.BundleErasure.AssumedSchema (1, "Customer"), erasures)
    let orderKind = bundle.Kinds |> List.find (fun k -> k.EntityId = 2)
    Assert.Equal("dbo", orderKind.DbSchema)
    let customerKind = bundle.Kinds |> List.find (fun k -> k.EntityId = 1)
    Assert.Equal("sales", customerKind.DbSchema)

[<Fact>]
let ``A54: an attribute with no declared data type erases as AssumedDataType and the bundle assumes Text — exactly when the type is absent`` () =
    let s = snapshotWith [ entity 1 "Customer" ] [ physicalTable 1 ] [ attrWithType 10 None; attrWithType 11 (Some "Integer") ] []
    let bundle, erasures = MetadataSnapshotRunner.toBundle s
    Assert.Contains(MetadataSnapshotRunner.BundleErasure.AssumedDataType (10, "Attr10"), erasures)
    Assert.DoesNotContain(MetadataSnapshotRunner.BundleErasure.AssumedDataType (11, "Attr11"), erasures)
    Assert.Equal("Text", (bundle.Attributes |> List.find (fun a -> a.AttrId = 10)).DataType)

[<Fact>]
let ``A54: a reference that does not join erases as UnjoinedReference — the drop count reconciles the projection`` () =
    // 20 joins (its attribute exists); 21's owning attribute is unknown;
    // 22 carries no target entity name. Both drops erase, named.
    let s =
        snapshotWith [ entity 1 "Customer" ] [ physicalTable 1 ] [ attrWithType 20 (Some "Integer") ]
            [ referenceRow 20 (Some "Order"); referenceRow 21 (Some "Order"); referenceRow 22 None ]
    let bundle, erasures = MetadataSnapshotRunner.toBundle s
    Assert.Contains(MetadataSnapshotRunner.BundleErasure.UnjoinedReference 21, erasures)
    Assert.Contains(MetadataSnapshotRunner.BundleErasure.UnjoinedReference 22, erasures)
    let unjoinedCount =
        erasures |> List.filter (function MetadataSnapshotRunner.BundleErasure.UnjoinedReference _ -> true | _ -> false) |> List.length
    Assert.Equal(List.length s.References, List.length bundle.References + unjoinedCount)

[<Fact>]
let ``A54: every erasure case carries a distinct routing code and a complete located sentence; the by-design cases ride Info, the data-dependent ones warn`` () =
    let specimens : MetadataSnapshotRunner.BundleErasure list =
        [ MetadataSnapshotRunner.BundleErasure.FoldedRowset ("fkReality", "folds to four per-reference scalars")
          MetadataSnapshotRunner.BundleErasure.UnjoinedReference 21
          MetadataSnapshotRunner.BundleErasure.AssumedSchema (2, "Order")
          MetadataSnapshotRunner.BundleErasure.AssumedDataType (10, "Notes")
          MetadataSnapshotRunner.BundleErasure.CapabilityInvariant ]
    let codes = specimens |> List.map MetadataSnapshotRunner.BundleErasure.code
    Assert.Equal(List.length specimens, codes |> List.distinct |> List.length)
    for s in specimens do
        let sentence = MetadataSnapshotRunner.BundleErasure.describe s
        Assert.False(System.String.IsNullOrWhiteSpace sentence)
        Assert.True(sentence.EndsWith ".", sprintf "erasure sentence is a fragment: %s" sentence)
        let d = MetadataSnapshotRunner.BundleErasure.toDiagnostic s
        Assert.Equal(MetadataSnapshotRunner.BundleErasure.code s, d.Code)
        let expectedSeverity =
            if isConstant s then DiagnosticSeverity.Info else DiagnosticSeverity.Warning
        Assert.Equal(expectedSeverity, d.Severity)
