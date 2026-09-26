module Projection.Tests.ReferenceHasDbConstraintLivePathTests

open Xunit
open Projection.Adapters.OssysSql
open Projection.Adapters.Osm  // OssysRowsetTypes (the RowsetBundle the runner assembles)

// ---------------------------------------------------------------------------
// WP-1a (DECISIONS 2026-07-16) — the live-extraction `HasDbConstraint` fix.
//
// `MetadataSnapshotRunner.toBundle` projects the raw `MetadataSnapshot`
// (rows straight off the source SQL Server) into an `OssysRowsetTypes
// .RowsetBundle`. For each `OssysReferenceRow` it JOINs `#FkReality`
// (via `#FkColumns.ParentAttrId`) to decide whether the attribute's FK
// is physically reflected. `HasDbConstraint` was HARDCODED `true`, so
// on a live extraction EVERY reference presented as source-backed — the
// logical-vs-backed distinction the FK evidence gate depends on was
// erased. (The JSON adapter path already defaults absent → false via
// `ISNULL(HasFK, 0)`; these pin the live path to the same semantics.)
//
// The fix: `HasDbConstraint = Option.isSome fkOpt` — true exactly when
// the reference's attribute has a reflected `#FkReality` row.
//
// These tests operate at the `toBundle` seam (raw snapshot → bundle),
// which is UPSTREAM of `FkRealityRowsetRoundTripTests` (those build a
// `RowsetBundle` by hand and exercise the bundle → catalog step).
// ---------------------------------------------------------------------------

let private attrRow (attrId: int) (entityId: int) (name: string) (col: string) : MetadataSnapshotRunner.OssysAttributeRow =
    { AttrId = attrId; EntityId = entityId; AttrName = name; AttrSsKey = None
      DataType = Some "Identifier"; Length = None; Precision = None; Scale = None
      DefaultValue = None
      IsMandatory = true; IsActive = true; IsAutoNumber = false
      IsIdentifier = false; RefEntityId = Some 10; OriginalName = None
      ExternalDbType = None; DeleteRule = Some "Protect"; PhysicalCol = col
      Description = None; Order = None }

let private referenceRow (attrId: int) : MetadataSnapshotRunner.OssysReferenceRow =
    { AttrId = attrId; RefEntityId = Some 10; RefEntityName = Some "Customer"
      RefPhysicalName = Some "OSUSR_C_CUSTOMER" }

let private fkColumnRow (parentAttrId: int) (fkObjectId: int) : MetadataSnapshotRunner.OssysFkColumnRow =
    { EntityId = 11; FkObjectId = fkObjectId; Ordinal = 1
      ParentColumn = "CUSTOMER_ID"; ReferencedColumn = "ID"
      ParentAttrId = Some parentAttrId; ParentAttrName = Some "CustomerId"
      ReferencedAttrId = Some 100; ReferencedAttrName = Some "Id" }

let private fkRealityRow (fkObjectId: int) : MetadataSnapshotRunner.OssysFkRealityRow =
    { EntityId = 11; FkObjectId = fkObjectId; FkName = "FK_OSUSR_O_ORDER_CUSTOMER_ID"
      DeleteAction = Some "NO_ACTION"; UpdateAction = None
      ReferencedObjectId = 999; ReferencedEntityId = Some 10
      ReferencedSchema = Some "dbo"; ReferencedTable = Some "OSUSR_C_CUSTOMER"
      IsNoCheck = false }

/// A snapshot carrying two references off entity 11 (Order):
///   - AttrId 201: BACKED — a matching `#FkColumns` + `#FkReality` pair.
///   - AttrId 301: LOGICAL-ONLY — no reflected FK.
let private twoReferenceSnapshot () : MetadataSnapshotRunner.MetadataSnapshot =
    { Modules = []; Entities = []
      Attributes =
        [ attrRow 201 11 "CustomerId" "CUSTOMER_ID"
          attrRow 301 11 "AltCustomerId" "ALT_CUSTOMER_ID" ]
      References =
        [ referenceRow 201
          referenceRow 301 ]
      PhysicalTables = []; ColumnReality = []; ColumnChecks = []; Sequences = []; Temporal = []
      PhysColsPresent = []; Indexes = []; IndexColumns = []
      ForeignKeysReality = [ fkRealityRow 5000 ]
      ForeignKeyColumns  = [ fkColumnRow 201 5000 ]
      Triggers = [] }

let private referenceByAttrId (bundle: OssysRowsetTypes.RowsetBundle) (attrId: int) : OssysRowsetTypes.ReferenceRow =
    bundle.References |> List.find (fun r -> r.AttrId = attrId)

[<Fact>]
let ``WP-1a: a reference with a reflected #FkReality row projects HasDbConstraint = true`` () =
    let bundle = MetadataSnapshotRunner.toBundle (twoReferenceSnapshot ())
    let backed = referenceByAttrId bundle 201
    Assert.True(backed.HasDbConstraint)

[<Fact>]
let ``WP-1a: a logical-only reference (no reflected FK) projects HasDbConstraint = false — was hardcoded true`` () =
    let bundle = MetadataSnapshotRunner.toBundle (twoReferenceSnapshot ())
    let logicalOnly = referenceByAttrId bundle 301
    Assert.False(logicalOnly.HasDbConstraint)

[<Fact>]
let ``WP-1a: the fix leaves the sibling #FkReality axes intact (backed ref stays trusted; logical-only defaults trusted, no ON UPDATE)`` () =
    let bundle = MetadataSnapshotRunner.toBundle (twoReferenceSnapshot ())
    let backed = referenceByAttrId bundle 201
    let logicalOnly = referenceByAttrId bundle 301
    // Backed FK reflected as trusted (IsNoCheck = false ⇒ trusted).
    Assert.True(backed.IsConstraintTrusted)
    Assert.Equal(None, backed.OnUpdate)
    // Logical-only reference has no FK reality to read: trusted default,
    // no ON UPDATE — HasDbConstraint is the ONLY axis that changed.
    Assert.True(logicalOnly.IsConstraintTrusted)
    Assert.Equal(None, logicalOnly.OnUpdate)
