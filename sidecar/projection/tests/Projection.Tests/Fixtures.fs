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
// All identifiers are constructed via the SsKey/Name modules so the
// fixture itself exercises the validation surface. Runtime panics here
// would indicate a bug in `original` or `Name.create`.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Success v -> v
    | Failure es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp $"Fixture construction failed: {codes}"

let private ssKey (s: string) : SsKey = SsKey.original s |> mustOk
let private name (s: string)  : Name  = Name.create s   |> mustOk

// ---------------------------------------------------------------------------
// Customer
// ---------------------------------------------------------------------------

let customerKey       = ssKey "OS_KIND_Customer"
let customerIdAttrKey = ssKey "OS_ATTR_Customer_Id"
let customerNameKey   = ssKey "OS_ATTR_Customer_Name"
let customerTenantKey = ssKey "OS_ATTR_Customer_TenantId"

let customer : Kind = {
    SsKey    = customerKey
    Name     = name "Customer"
    Origin   = OsNative
    Modality = [ TenantScoped ]
    Physical = { Schema = "dbo"; Table = "OSUSR_S1S_CUSTOMER" }
    Attributes = [
        { SsKey        = customerIdAttrKey
          Name         = name "Id"
          Type         = Integer
          Column       = { ColumnName = "ID"; IsNullable = false }
          IsPrimaryKey = true; IsMandatory = false }
        { SsKey        = customerNameKey
          Name         = name "Name"
          Type         = Text
          Column       = { ColumnName = "NAME"; IsNullable = false }
          IsPrimaryKey = false; IsMandatory = false }
        { SsKey        = customerTenantKey
          Name         = name "TenantId"
          Type         = Integer
          Column       = { ColumnName = "TENANT_ID"; IsNullable = false }
          IsPrimaryKey = false; IsMandatory = false }
    ]
    References = []
}

// ---------------------------------------------------------------------------
// Order — has a directional reference to Customer (the FK in the spec).
// ---------------------------------------------------------------------------

let orderKey            = ssKey "OS_KIND_Order"
let orderIdAttrKey      = ssKey "OS_ATTR_Order_Id"
let orderCustomerFkKey  = ssKey "OS_ATTR_Order_CustomerId"
let orderRefToCustomer  = ssKey "OS_REF_Order_Customer"

let order : Kind = {
    SsKey    = orderKey
    Name     = name "Order"
    Origin   = OsNative
    Modality = []
    Physical = { Schema = "dbo"; Table = "OSUSR_S1S_ORDER" }
    Attributes = [
        { SsKey        = orderIdAttrKey
          Name         = name "Id"
          Type         = Integer
          Column       = { ColumnName = "ID"; IsNullable = false }
          IsPrimaryKey = true; IsMandatory = false }
        { SsKey        = orderCustomerFkKey
          Name         = name "CustomerId"
          Type         = Integer
          Column       = { ColumnName = "CUSTOMER_ID"; IsNullable = false }
          IsPrimaryKey = false; IsMandatory = false }
    ]
    References = [
        { SsKey           = orderRefToCustomer
          Name            = name "Customer"
          SourceAttribute = orderCustomerFkKey
          TargetKind      = customerKey
          OnDelete        = NoAction }
    ]
}

// ---------------------------------------------------------------------------
// Country — Static, with a small populated row set.
// ---------------------------------------------------------------------------

let countryKey       = ssKey "OS_KIND_Country"
let countryIdAttrKey = ssKey "OS_ATTR_Country_Id"
let countryCodeKey   = ssKey "OS_ATTR_Country_Code"
let countryLabelKey  = ssKey "OS_ATTR_Country_Label"

let private rowKey (code: string) : SsKey =
    ssKey $"OS_ROW_Country_{code}"

let countryPopulations : StaticRow list = [
    { Identifier = rowKey "US"
      Values = Map.ofList [
          name "Code",  "US"
          name "Label", "United States" ] }
    { Identifier = rowKey "CA"
      Values = Map.ofList [
          name "Code",  "CA"
          name "Label", "Canada" ] }
    { Identifier = rowKey "MX"
      Values = Map.ofList [
          name "Code",  "MX"
          name "Label", "Mexico" ] }
]

let country : Kind = {
    SsKey    = countryKey
    Name     = name "Country"
    Origin   = OsNative
    Modality = [ Static countryPopulations ]
    Physical = { Schema = "dbo"; Table = "OSUSR_S1S_COUNTRY" }
    Attributes = [
        { SsKey        = countryIdAttrKey
          Name         = name "Id"
          Type         = Integer
          Column       = { ColumnName = "ID"; IsNullable = false }
          IsPrimaryKey = true; IsMandatory = false }
        { SsKey        = countryCodeKey
          Name         = name "Code"
          Type         = Text
          Column       = { ColumnName = "CODE"; IsNullable = false }
          IsPrimaryKey = false; IsMandatory = false }
        { SsKey        = countryLabelKey
          Name         = name "Label"
          Type         = Text
          Column       = { ColumnName = "LABEL"; IsNullable = false }
          IsPrimaryKey = false; IsMandatory = false }
    ]
    References = []
}

// ---------------------------------------------------------------------------
// Catalog: one module ("Sales") containing all three kinds.
// ---------------------------------------------------------------------------

let salesModuleKey = ssKey "OS_MOD_Sales"

let salesModule : Module = {
    SsKey = salesModuleKey
    Name  = name "Sales"
    Kinds = [ customer; order; country ]
}

let sampleCatalog : Catalog = {
    Modules = [ salesModule ]
}
