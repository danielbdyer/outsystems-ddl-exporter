module Projection.Tests.Fixtures

open Projection.Core
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Synthetic catalog used as the test bed for the entire pipeline.
//
// Three kinds:
//   1. Customer  — OsNative, TenantScoped.
//   2. Order     — OsNative, has a foreign key to Customer.
//   3. Country   — OsNative, Static, with a populated row set.
//
// One module ("Sales") wraps all three kinds. No SQL involved.
//
// All identifiers are constructed via the SsKey/Name typed builders so
// the fixture itself exercises the validation surface. Runtime panics
// here indicate a bug in `synthesized` / `synthesizedComposite` /
// `Name.create`.
//
// Chapter 3.6 slice-δ + DECISIONS pillar 6 (no V2-internal back-compat
// paths): `SsKey.original` parser-shim was retired; fixtures construct
// typed-segment SsKeys directly. The helpers below mirror the OSSYS /
// Sql adapter conventions one-for-one.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp $"Fixture construction failed: {codes}"

let private name (s: string)  : Name  = Name.create s   |> mustOk

/// Typed SsKey builders mirroring the adapter conventions
/// (`Projection.Adapters.Osm.CatalogReader`,
/// `Projection.Adapters.Sql.Static`). Tests build expected catalogs
/// via the same typed shape that the adapters produce, so structural
/// equality holds end-to-end. Public so other test files can reuse
/// (eliminates ~25 per-file `SsKey.original` parser-shim consumers).
let kindKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_KIND" parts |> mustOk

let attrKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_ATTR" parts |> mustOk

let modKey (m: string) : SsKey =
    SsKey.synthesized "OS_MOD" m |> mustOk

let refKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_REF" parts |> mustOk

let idxKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_IDX" parts |> mustOk

let rowKey (basis: string) : SsKey =
    SsKey.synthesized "OS_ROW" basis |> mustOk

/// Test-only label key. Used by tests whose nodes are pure labels
/// ("A", "B", "C" graph-shape fixtures) rather than OSSYS-shaped
/// identities. Carries the explicit `"TEST"` source convention so
/// the typed payload is honest about its provenance.
let testKey (label: string) : SsKey =
    SsKey.synthesized "TEST" label |> mustOk

// ---------------------------------------------------------------------------
// Record builders (chapter A.0' slice γ).
//
// Per DECISIONS 2026-05-15 — Closed-DU empirical-test discipline
// refinement: additive-default-shaped IR fields use builder-mediated
// mode; the builders centralize sensible defaults so future record
// extensions touch only the builder + literal sites that already
// override the relevant field.
//
// Usage:
//   { Fixtures.attribute key (name "Id") with Type = Integer; IsPrimaryKey = true }
//   { Fixtures.kind key (name "User") with Attributes = [ ... ] }
//   { Fixtures.module' key (name "AppCore") with Kinds = [ user ] }
//   { Fixtures.catalog [ m ] with Triggers = [ trigger ] }
//
// Builders are test-only. Production code constructs via smart
// constructors (`Module.create`, `Catalog.create`) or direct literals
// inside passes; the builder surface is never imported into
// `Projection.Core` / `Projection.Adapters.*` / `Projection.Targets.*`.
// ---------------------------------------------------------------------------

/// Build an `Attribute` with sensible defaults. Defaults:
///   Type = Integer; Column = { ColumnName = Name.value n; IsNullable = false };
///   IsPrimaryKey = false; IsMandatory = false; Length / Precision / Scale = None;
///   IsIdentity = false; Description = None; IsActive = true.
let attribute (ssKey: SsKey) (n: Name) : Attribute =
    { SsKey        = ssKey
      Name         = n
      Type         = Integer
      Column       = { ColumnName = Name.value n; IsNullable = false }
      IsPrimaryKey = false
      IsMandatory  = false
      Length       = None
      Precision    = None
      Scale        = None
      IsIdentity   = false
      Description  = None
      IsActive     = true }

/// Build a `Kind` with sensible defaults. Defaults:
///   Origin = OsNative; Modality = []; Physical = { Schema = "dbo"; Table = Name.value n };
///   Attributes = []; References = []; Indexes = [];
///   Description = None; IsActive = true.
let kind (ssKey: SsKey) (n: Name) : Kind =
    { SsKey       = ssKey
      Name        = n
      Origin      = OsNative
      Modality    = []
      Physical    = { Schema = "dbo"; Table = Name.value n }
      Attributes  = []
      References  = []
      Indexes     = []
      Description = None
      IsActive    = true }

/// Build a `Module` with sensible defaults. Defaults:
///   Kinds = []; IsActive = true.
///
/// The trailing apostrophe avoids the F# `module` keyword collision.
let module' (ssKey: SsKey) (n: Name) : Module =
    { SsKey    = ssKey
      Name     = n
      Kinds    = []
      IsActive = true }

/// Build a `Reference` with sensible defaults. Defaults:
///   OnDelete = NoAction; IsUserFk = false.
let reference (ssKey: SsKey) (n: Name) (source: SsKey) (target: SsKey) : Reference =
    { SsKey           = ssKey
      Name            = n
      SourceAttribute = source
      TargetKind      = target
      OnDelete        = NoAction
      IsUserFk        = false }

/// Build an `Index` with sensible defaults. Defaults:
///   IsUnique = false; IsPrimaryKey = false.
let index (ssKey: SsKey) (n: Name) (columns: SsKey list) : Index =
    { SsKey        = ssKey
      Name         = n
      Columns      = columns
      IsUnique     = false
      IsPrimaryKey = false }

/// Build a `Catalog` from a list of modules with sensible defaults.
/// Defaults: Triggers = []; Sequences = [].
let catalog (modules: Module list) : Catalog =
    { Modules   = modules
      Triggers  = []
      Sequences = [] }

// ---------------------------------------------------------------------------
// Rowset-DTO builders (chapter A.0' slice ε prelude).
//
// V1's rowset bundle (`CatalogReader.RowsetBundle` + the per-row DTOs
// `ModuleRow` / `KindRow` / `AttributeRow` / `ReferenceRow` /
// `TriggerRow` / `SequenceRow`) accumulates fields slice-by-slice
// (`Description` at slice α; `IsActive` at slice β; the trigger DTO
// at slice γ; the sequence DTO at slice δ). Without builders, every
// rowset literal site pays a field-addition touch per slice — and
// these literals are concentrated in a few canonical files
// (`OsmRowsetReaderTests`, the lift-tests, `DescriptionLiftTests`)
// where the churn is mechanical but mass.
//
// Builders absorb the default-shaped churn the same way `Fixtures
// .attribute` / `kind` / `module'` / `catalog` do for the Core IR.
// The per-row DTOs are test-only carriers (production rowset rows
// come from a C# SqlClient loader that isn't wired yet); the
// `RowsetBundle` builder + per-row builders are the surfaces tests
// actually need.
// ---------------------------------------------------------------------------

/// Build a `ModuleRow` with sensible defaults. Defaults:
///   IsSystemModule = false; IsActive = true; EspaceKind = Some "eSpace";
///   EspaceSsKey = None.
let moduleRow (espaceId: int) (espaceName: string) : CatalogReader.ModuleRow =
    { EspaceId       = espaceId
      EspaceName     = espaceName
      IsSystemModule = false
      IsActive       = true
      EspaceKind     = Some "eSpace"
      EspaceSsKey    = None }

/// Build a `KindRow` with sensible defaults. Defaults:
///   PhysicalTableName = uppercase entityName prefixed with "OSUSR_T_";
///   DbSchema = "dbo"; IsStatic / IsExternal / IsSystemEntity = false;
///   IsActive = true; EntitySsKey / PrimaryKeySsKey / Description = None.
let kindRow (entityId: int) (espaceId: int) (entityName: string) : CatalogReader.KindRow =
    { EntityId          = entityId
      EspaceId          = espaceId
      EntityName        = entityName
      PhysicalTableName = sprintf "OSUSR_T_%s" (entityName.ToUpperInvariant())
      DbSchema          = "dbo"
      IsStatic          = false
      IsExternal        = false
      IsSystemEntity    = false
      IsActive          = true
      EntitySsKey       = None
      PrimaryKeySsKey   = None
      Description       = None }

/// Build an `AttributeRow` with sensible defaults. Defaults:
///   PhysicalCol = attrName.ToUpperInvariant(); DataType = "Identifier";
///   IsMandatory / IsIdentifier / IsAutoNumber = false;
///   Length / Precision / Scale / AttrSsKey / Description = None;
///   IsActive = true.
let attributeRow
    (attrId: int) (entityId: int) (attrName: string)
    : CatalogReader.AttributeRow =
    { AttrId       = attrId
      EntityId     = entityId
      AttrName     = attrName
      PhysicalCol  = attrName.ToUpperInvariant()
      DataType     = "Identifier"
      IsMandatory  = false
      IsIdentifier = false
      IsAutoNumber = false
      Length       = None
      Precision    = None
      Scale        = None
      AttrSsKey    = None
      IsActive     = true
      Description  = None }

/// Build a `ReferenceRow` with sensible defaults. Defaults:
///   DeleteRuleCode = None (parses to NoAction);
///   HasDbConstraint = true.
let referenceRow (attrId: int) (refEntityName: string) : CatalogReader.ReferenceRow =
    { AttrId          = attrId
      RefEntityName   = refEntityName
      DeleteRuleCode  = None
      HasDbConstraint = true }

/// Build a `TriggerRow` with sensible defaults. Defaults:
///   IsDisabled = false.
let triggerRow
    (entityId: int) (triggerName: string) (definition: string)
    : CatalogReader.TriggerRow =
    { EntityId          = entityId
      TriggerName       = triggerName
      IsDisabled        = false
      TriggerDefinition = definition }

/// Build a `SequenceRow` with sensible defaults. Defaults:
///   DataType = "bigint"; StartValue / Increment / MinValue / MaxValue = None;
///   IsCycleEnabled = false; CacheMode / CacheSize = None.
let sequenceRow (schema: string) (sequenceName: string) : CatalogReader.SequenceRow =
    { Schema         = schema
      SequenceName   = sequenceName
      DataType       = "bigint"
      StartValue     = None
      Increment      = None
      MinValue       = None
      MaxValue       = None
      IsCycleEnabled = false
      CacheMode      = None
      CacheSize      = None }

/// Build a `RowsetBundle` from a list of module rows with sensible
/// defaults. Defaults: Kinds / Attributes / References / Triggers /
/// Sequences = []. Mirrors `Fixtures.catalog`'s shape for the rowset
/// side; future bundle-level field additions (next: probably
/// `ExtendedProperties` at slice ζ if it lifts via rowsets) touch the
/// builder, not the literal sites.
let rowsetBundle (modules: CatalogReader.ModuleRow list) : CatalogReader.RowsetBundle =
    { Modules    = modules
      Kinds      = []
      Attributes = []
      References = []
      Triggers   = []
      Sequences  = [] }

// ---------------------------------------------------------------------------
// Customer
// ---------------------------------------------------------------------------

let customerKey       = kindKey ["Customer"]
let customerIdAttrKey = attrKey ["Customer"; "Id"]
let customerNameKey   = attrKey ["Customer"; "Name"]
let customerTenantKey = attrKey ["Customer"; "TenantId"]

let customer : Kind =
    { kind customerKey (name "Customer") with
        Modality = [ TenantScoped ]
        Physical = { Schema = "dbo"; Table = "OSUSR_S1S_CUSTOMER" }
        Attributes =
            [ { attribute customerIdAttrKey (name "Id") with
                  Column = { ColumnName = "ID"; IsNullable = false }
                  IsPrimaryKey = true }
              { attribute customerNameKey (name "Name") with
                  Type = Text
                  Column = { ColumnName = "NAME"; IsNullable = false } }
              { attribute customerTenantKey (name "TenantId") with
                  Column = { ColumnName = "TENANT_ID"; IsNullable = false } } ] }

// ---------------------------------------------------------------------------
// Order — has a directional reference to Customer (the FK in the spec).
// ---------------------------------------------------------------------------

let orderKey            = kindKey ["Order"]
let orderIdAttrKey      = attrKey ["Order"; "Id"]
let orderCustomerFkKey  = attrKey ["Order"; "CustomerId"]
let orderRefToCustomer  = refKey  ["Order"; "Customer"]

let order : Kind =
    { kind orderKey (name "Order") with
        Physical = { Schema = "dbo"; Table = "OSUSR_S1S_ORDER" }
        Attributes =
            [ { attribute orderIdAttrKey (name "Id") with
                  Column = { ColumnName = "ID"; IsNullable = false }
                  IsPrimaryKey = true }
              { attribute orderCustomerFkKey (name "CustomerId") with
                  Column = { ColumnName = "CUSTOMER_ID"; IsNullable = false } } ]
        References =
            [ { SsKey           = orderRefToCustomer
                Name            = name "Customer"
                SourceAttribute = orderCustomerFkKey
                TargetKind      = customerKey
                OnDelete        = NoAction
                IsUserFk        = false } ] }

// ---------------------------------------------------------------------------
// Country — Static, with a small populated row set.
// ---------------------------------------------------------------------------

let countryKey       = kindKey ["Country"]
let countryIdAttrKey = attrKey ["Country"; "Id"]
let countryCodeKey   = attrKey ["Country"; "Code"]
let countryLabelKey  = attrKey ["Country"; "Label"]

let private countryRowKey (code: string) : SsKey =
    rowKey ($"Country_{code}")

let countryPopulations : StaticRow list = [
    { Identifier = countryRowKey "US"
      Values = Map.ofList [
          name "Code",  "US"
          name "Label", "United States" ] }
    { Identifier = countryRowKey "CA"
      Values = Map.ofList [
          name "Code",  "CA"
          name "Label", "Canada" ] }
    { Identifier = countryRowKey "MX"
      Values = Map.ofList [
          name "Code",  "MX"
          name "Label", "Mexico" ] }
]

let country : Kind =
    { kind countryKey (name "Country") with
        Modality = [ Static countryPopulations ]
        Physical = { Schema = "dbo"; Table = "OSUSR_S1S_COUNTRY" }
        Attributes =
            [ { attribute countryIdAttrKey (name "Id") with
                  Column = { ColumnName = "ID"; IsNullable = false }
                  IsPrimaryKey = true }
              { attribute countryCodeKey (name "Code") with
                  Type = Text
                  Column = { ColumnName = "CODE"; IsNullable = false } }
              { attribute countryLabelKey (name "Label") with
                  Type = Text
                  Column = { ColumnName = "LABEL"; IsNullable = false } } ] }

// ---------------------------------------------------------------------------
// Catalog: one module ("Sales") containing all three kinds.
// ---------------------------------------------------------------------------

let salesModuleKey = modKey "Sales"

let salesModule : Module =
    { module' salesModuleKey (name "Sales") with
        Kinds = [ customer; order; country ] }

let sampleCatalog : Catalog = catalog [ salesModule ]
