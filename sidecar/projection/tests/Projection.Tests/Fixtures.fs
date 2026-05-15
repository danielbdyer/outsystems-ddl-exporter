module Projection.Tests.Fixtures

open Projection.Core

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

/// Build a `Catalog` from a list of modules with sensible defaults.
/// Defaults: Triggers = [].
let catalog (modules: Module list) : Catalog =
    { Modules  = modules
      Triggers = [] }

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
