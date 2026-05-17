module Projection.Tests.Fixtures

open Projection.Core
open Projection.Tests.IRBuilders

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
// **Chapter A.0' post-XXXXL refactor.** This file now uses
// `IRBuilders.mk*` to construct records with minimum-evidence
// defaults; per-fixture overrides via `{ mkAttribute ... with ... }`.
// New IR fields added by subsequent slices land in `IRBuilders.fs`
// alone, not here. Pre-refactor: ~150 record-literal sites carried
// every field explicitly; ExpressLane refactor consolidates the
// boilerplate.
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

/// Per-fixture helper: build an attribute with the column-name
/// uppercased from the logical name. Override IsPrimaryKey via
/// record-update at the call site.
let private mkFixtureAttribute (key: SsKey) (logical: string) (ptype: PrimitiveType) (isPk: bool) : Attribute =
    { mkAttribute key (name logical) ptype with
        Column       = { ColumnName = logical.ToUpperInvariant(); IsNullable = false }
        IsPrimaryKey = isPk }

// ---------------------------------------------------------------------------
// Customer
// ---------------------------------------------------------------------------

let customerKey       = kindKey ["Customer"]
let customerIdAttrKey = attrKey ["Customer"; "Id"]
let customerNameKey   = attrKey ["Customer"; "Name"]
let customerTenantKey = attrKey ["Customer"; "TenantId"]

let customer : Kind =
    { mkKind
        customerKey
        (name "Customer")
        { Schema = "dbo"; Table = "OSUSR_S1S_CUSTOMER"; Catalog = None }
        [ { mkFixtureAttribute customerIdAttrKey "Id" Integer true with
              Column = { ColumnName = "ID"; IsNullable = false } }
          { mkFixtureAttribute customerNameKey "Name" Text false with
              Column = { ColumnName = "NAME"; IsNullable = false } }
          { mkFixtureAttribute customerTenantKey "TenantId" Integer false with
              Column = { ColumnName = "TENANT_ID"; IsNullable = false } } ]
        with Modality = [ TenantScoped ] }

// ---------------------------------------------------------------------------
// Order — has a directional reference to Customer (the FK in the spec).
// ---------------------------------------------------------------------------

let orderKey            = kindKey ["Order"]
let orderIdAttrKey      = attrKey ["Order"; "Id"]
let orderCustomerFkKey  = attrKey ["Order"; "CustomerId"]
let orderRefToCustomer  = refKey  ["Order"; "Customer"]

let order : Kind =
    { mkKind
        orderKey
        (name "Order")
        { Schema = "dbo"; Table = "OSUSR_S1S_ORDER"; Catalog = None }
        [ { mkFixtureAttribute orderIdAttrKey "Id" Integer true with
              Column = { ColumnName = "ID"; IsNullable = false } }
          { mkFixtureAttribute orderCustomerFkKey "CustomerId" Integer false with
              Column = { ColumnName = "CUSTOMER_ID"; IsNullable = false } } ]
        with
        References =
            [ { SsKey           = orderRefToCustomer
                Name            = name "Customer"
                SourceAttribute = orderCustomerFkKey
                TargetKind      = customerKey
                OnDelete        = NoAction
                IsUserFk        = false; HasDbConstraint = false } ] }

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
    { mkKind
        countryKey
        (name "Country")
        { Schema = "dbo"; Table = "OSUSR_S1S_COUNTRY"; Catalog = None }
        [ { mkFixtureAttribute countryIdAttrKey "Id" Integer true with
              Column = { ColumnName = "ID"; IsNullable = false } }
          { mkFixtureAttribute countryCodeKey "Code" Text false with
              Column = { ColumnName = "CODE"; IsNullable = false } }
          { mkFixtureAttribute countryLabelKey "Label" Text false with
              Column = { ColumnName = "LABEL"; IsNullable = false } } ]
        with Modality = [ Static countryPopulations ] }

// ---------------------------------------------------------------------------
// Catalog: one module ("Sales") containing all three kinds.
// ---------------------------------------------------------------------------

let salesModuleKey = modKey "Sales"

let salesModule : Module =
    mkModule salesModuleKey (name "Sales") [ customer; order; country ]

let sampleCatalog : Catalog =
    mkCatalog [ salesModule ]
