module Projection.Tests.FkRealityRowsetRoundTripTests

// Slice A.4.7'-prelude.row17-18-rowset-roundtrip — end-to-end
// verification that V1 `#FkReality.UpdateAction` (SQL Server vocabulary:
// CASCADE / NO_ACTION / SET_NULL / SET_DEFAULT) flows through to
// V2 `Reference.OnUpdate : ReferenceAction option`, and that
// `#FkReality.IsNoCheck` flows through to
// `Reference.IsConstraintTrusted : bool`.
//
// **Bug found during slice B audit (2026-05-19):** prior slice
// 5.13.fk-reality-join (2026-05-18) wired the 3-step JOIN
// (OssysReferenceRow → OssysFkColumnRow → OssysFkRealityRow) at
// `MetadataSnapshotRunner.toBundle` and threaded `OnUpdate : string
// option` + `IsConstraintTrusted : bool` through `CatalogReader
// .ReferenceRow`. However, `parseReferenceRowFor` parsed the
// `OnUpdate` string via `parseDeleteRule` which only recognizes
// OutSystems-domain vocabulary ("Delete" / "Protect" / "Ignore" /
// "SetNull"). V1 SQL emits SQL Server's `update_referential_action_desc`
// vocabulary which falls into `parseDeleteRule`'s error branch,
// then `Option.bind` silently degrades to `None`. Result: V2 never
// populated `Reference.OnUpdate` from the rowset path despite the
// JOIN being correctly wired.
//
// This slice ships the parsing fix (separate `parseSqlForeignKeyAction`
// for SQL Server's vocabulary) + end-to-end tests asserting the
// round-trip works for each variant.
//
// Per pillar 9: pure DataIntent. The fix amplifies the existing
// `typeTranslation` Site in `CatalogReader.registeredMetadata`;
// no new TransformRegistry Sites needed.

open Xunit
open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Shared fixtures — minimal two-kind catalog: AppCore.Customer (PK) +
// AppCore.Order (FK → Customer). FK reality varies per test via the
// ReferenceRow's OnUpdate + IsConstraintTrusted fields.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %A" es)

let private moduleRow : CatalogReader.ModuleRow =
    { EspaceId       = 1
      EspaceName     = "AppCore"
      IsSystemModule = false
      IsActive       = true
      EspaceKind     = None
      EspaceSsKey    = None }

let private customerKindRow : CatalogReader.KindRow =
    { EntityId          = 10
      EspaceId          = 1
      EntityName        = "Customer"
      PhysicalTableName = "OSUSR_C_CUSTOMER"
      DbSchema          = "dbo"
      IsStatic          = false
      IsExternal        = false
      IsSystemEntity    = false
      IsActive          = true
      EntitySsKey       = None
      PrimaryKeySsKey   = None
      Description       = None }

let private orderKindRow : CatalogReader.KindRow =
    { EntityId          = 11
      EspaceId          = 1
      EntityName        = "Order"
      PhysicalTableName = "OSUSR_O_ORDER"
      DbSchema          = "dbo"
      IsStatic          = false
      IsExternal        = false
      IsSystemEntity    = false
      IsActive          = true
      EntitySsKey       = None
      PrimaryKeySsKey   = None
      Description       = None }

let private customerIdAttrRow : CatalogReader.AttributeRow =
    { AttrId               = 100
      EntityId             = 10
      AttrName             = "Id"
      PhysicalCol          = "ID"
      DataType             = "Identifier"
      IsMandatory          = true
      IsIdentifier         = true
      IsAutoNumber         = true
      Length               = None
      Precision            = None
      Scale                = None
      AttrSsKey            = None
      IsActive             = true
      Description          = None
      OriginalName         = None
      ExternalDatabaseType = None
      IsComputed           = false
      ComputedDefinition   = None
      DefaultConstraintName = None }

let private orderIdAttrRow : CatalogReader.AttributeRow =
    { customerIdAttrRow with
        AttrId   = 200
        EntityId = 11 }

let private orderCustomerFkAttrRow : CatalogReader.AttributeRow =
    { AttrId               = 201
      EntityId             = 11
      AttrName             = "CustomerId"
      PhysicalCol          = "CUSTOMER_ID"
      DataType             = "Identifier"
      IsMandatory          = true
      IsIdentifier         = false
      IsAutoNumber         = false
      Length               = None
      Precision            = None
      Scale                = None
      AttrSsKey            = None
      IsActive             = true
      Description          = None
      OriginalName         = None
      ExternalDatabaseType = None
      IsComputed           = false
      ComputedDefinition   = None
      DefaultConstraintName = None }

let private fkReferenceRow
    (onUpdate: string option)
    (isConstraintTrusted: bool)
    : CatalogReader.ReferenceRow =
    { AttrId              = 201
      RefEntityName       = "Customer"
      RefEntityId         = Some 10
      DeleteRuleCode      = Some "Protect"
      HasDbConstraint     = true
      OnUpdate            = onUpdate
      IsConstraintTrusted = isConstraintTrusted }

let private buildBundle (refRow: CatalogReader.ReferenceRow) : CatalogReader.RowsetBundle =
    { CatalogReader.RowsetBundle.empty with
        Modules    = [ moduleRow ]
        Kinds      = [ customerKindRow; orderKindRow ]
        Attributes = [ customerIdAttrRow; orderIdAttrRow; orderCustomerFkAttrRow ]
        References = [ refRow ] }

let private parseToCatalog (bundle: CatalogReader.RowsetBundle) : Catalog =
    let task = CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
    let result = task.GetAwaiter().GetResult()
    mustOk result

let private findOrderReference (cat: Catalog) : Reference =
    cat.Modules
    |> List.collect (fun m -> m.Kinds)
    |> List.find (fun k -> Name.value k.Name = "Order")
    |> fun k -> k.References |> List.head

// ---------------------------------------------------------------------------
// Row 17 (OnUpdate axis): V1 #FkReality.update_referential_action_desc
// (SQL Server vocabulary) → V2 Reference.OnUpdate (ReferenceAction option).
//
// The SQL Server vocabulary is:
//   NO_ACTION   — server-default; equivalent to no ON UPDATE clause
//   CASCADE
//   SET_NULL
//   SET_DEFAULT — not currently in V2's ReferenceAction (deferred trigger)
//
// V2 ReferenceAction variants: NoAction | Cascade | SetNull | Restrict
// (Restrict is V1-style; SQL Server's NO_ACTION maps to NoAction).
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row17-18: V1 #FkReality.UpdateAction = "CASCADE" populates Reference.OnUpdate = Some Cascade`` () =
    let cat = parseToCatalog (buildBundle (fkReferenceRow (Some "CASCADE") true))
    let reference = findOrderReference cat
    Assert.Equal(Some Cascade, reference.OnUpdate)

[<Fact>]
let ``A.4.7'-prelude.row17-18: V1 #FkReality.UpdateAction = "SET_NULL" populates Reference.OnUpdate = Some SetNull`` () =
    let cat = parseToCatalog (buildBundle (fkReferenceRow (Some "SET_NULL") true))
    let reference = findOrderReference cat
    Assert.Equal(Some SetNull, reference.OnUpdate)

[<Fact>]
let ``A.4.7'-prelude.row17-18: V1 #FkReality.UpdateAction = "NO_ACTION" populates Reference.OnUpdate = Some NoAction`` () =
    let cat = parseToCatalog (buildBundle (fkReferenceRow (Some "NO_ACTION") true))
    let reference = findOrderReference cat
    Assert.Equal(Some NoAction, reference.OnUpdate)

[<Fact>]
let ``A.4.7'-prelude.row17-18: V1 #FkReality.UpdateAction = None leaves Reference.OnUpdate = None (server-default)`` () =
    let cat = parseToCatalog (buildBundle (fkReferenceRow None true))
    let reference = findOrderReference cat
    Assert.Equal(None, reference.OnUpdate)

[<Fact>]
let ``A.4.7'-prelude.row17-18: V1 #FkReality.UpdateAction = "SET_DEFAULT" degrades to None (deferred V2 ReferenceAction variant)`` () =
    // SET_DEFAULT is a valid SQL Server referential action that V2's
    // current ReferenceAction DU doesn't model. The parser silently
    // degrades to None rather than failing the whole Reference (same
    // posture as parseDeleteRule on unfamiliar OutSystems vocabulary).
    // Lift trigger: a real-world FK with ON UPDATE SET DEFAULT surfaces.
    let cat = parseToCatalog (buildBundle (fkReferenceRow (Some "SET_DEFAULT") true))
    let reference = findOrderReference cat
    Assert.Equal(None, reference.OnUpdate)

// ---------------------------------------------------------------------------
// Row 18 (IsConstraintTrusted axis): V1 #FkReality.is_not_trusted (bool;
// inverted) → V2 Reference.IsConstraintTrusted (bool).
//
// V1's #FkReality.IsNoCheck reflects sys.foreign_keys.is_not_trusted (a
// post-CREATE ALTER TABLE WITH NOCHECK CHECK CONSTRAINT state). V2's
// Reference.IsConstraintTrusted is the inverse — `true` means the FK
// is currently trusted (no NOCHECK in effect); `false` means the FK
// was deployed with WITH NOCHECK CHECK CONSTRAINT and the deployed
// state hasn't re-validated it.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row17-18: ReferenceRow.IsConstraintTrusted = true populates Reference.IsConstraintTrusted = true`` () =
    let cat = parseToCatalog (buildBundle (fkReferenceRow None true))
    let reference = findOrderReference cat
    Assert.True(reference.IsConstraintTrusted)

[<Fact>]
let ``A.4.7'-prelude.row17-18: ReferenceRow.IsConstraintTrusted = false populates Reference.IsConstraintTrusted = false (NOCHECK state preserved)`` () =
    let cat = parseToCatalog (buildBundle (fkReferenceRow None false))
    let reference = findOrderReference cat
    Assert.False(reference.IsConstraintTrusted)

// ---------------------------------------------------------------------------
// Combined: both axes together (the production cutover scenario where
// V2 round-trips a non-default FK reality through the rowset path).
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row17-18: both OnUpdate and IsConstraintTrusted round-trip together`` () =
    let cat =
        parseToCatalog (buildBundle (fkReferenceRow (Some "CASCADE") false))
    let reference = findOrderReference cat
    Assert.Equal(Some Cascade, reference.OnUpdate)
    Assert.False(reference.IsConstraintTrusted)
