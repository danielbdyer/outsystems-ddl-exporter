module Projection.Tests.Fixtures

open Projection.Core
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Synthetic catalog used as the test bed for the entire pipeline.
//
// Three kinds:
//   1. Customer  — Native, TenantScoped.
//   2. Order     — Native, has a foreign key to Customer.
//   3. Country   — Native, Static, with a populated row set.
//
// One module ("Sales") wraps all three kinds. No SQL involved.
//
// **Chapter A.0' post-XXXXL refactor.** This file now uses
// `IRBuilders.mk*` to construct records with minimum-evidence
// defaults; per-fixture overrides via `{ Attribute.create ... with ... }`.
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

/// Canonical fixture `Name` builder. Tests across the suite re-spell
/// `Name.create s |> Result.value` (~44 local `mkName` copies, B6 audit);
/// this is the one shared, accessible definition. Force-unwraps because
/// fixture names are constants known to satisfy `Name.create`. The
/// `private name` alias above is the in-file spelling; `mkName` is the
/// exported one consuming test files open and reuse.
let mkName (s: string) : Name = name s

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

/// Shared TableId builder for test fixtures. Names in tests are
/// constants known to satisfy `SchemaName.create` / `TableName.create`
/// (non-blank, <= 128 chars); force-unwrapping is the fixture pattern.
/// Used wherever fixtures construct a `TableId` with no catalog
/// (the common shape — explicit catalogs are tested via dedicated
/// helpers).
let mkTableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> mustOk

/// Per-fixture helper: build an attribute with the column-name
/// uppercased from the logical name. Override IsPrimaryKey via
/// record-update at the call site.
let private mkFixtureAttribute (key: SsKey) (logical: string) (ptype: PrimitiveType) (isPk: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value
        IsPrimaryKey = isPk }

// ---------------------------------------------------------------------------
// Customer
// ---------------------------------------------------------------------------

let customerKey       = kindKey ["Customer"]
let customerIdAttrKey = attrKey ["Customer"; "Id"]
let customerNameKey   = attrKey ["Customer"; "Name"]
let customerTenantKey = attrKey ["Customer"; "TenantId"]

let customer : Kind =
    { Kind.create
        customerKey
        (name "Customer")
        (mkTableId "dbo" "OSUSR_S1S_CUSTOMER")
        [ { mkFixtureAttribute customerIdAttrKey "Id" Integer true with
              Column = ColumnRealization.create "ID" false |> Result.value }
          { mkFixtureAttribute customerNameKey "Name" Text false with
              Column = ColumnRealization.create "NAME" false |> Result.value }
          { mkFixtureAttribute customerTenantKey "TenantId" Integer false with
              Column = ColumnRealization.create "TENANT_ID" false |> Result.value } ]
        with Modality = [ TenantScoped ] }

// ---------------------------------------------------------------------------
// Order — has a directional reference to Customer (the FK in the spec).
// ---------------------------------------------------------------------------

let orderKey            = kindKey ["Order"]
let orderIdAttrKey      = attrKey ["Order"; "Id"]
let orderCustomerFkKey  = attrKey ["Order"; "CustomerId"]
let orderRefToCustomer  = refKey  ["Order"; "Customer"]

let order : Kind =
    { Kind.create
        orderKey
        (name "Order")
        (mkTableId "dbo" "OSUSR_S1S_ORDER")
        [ { mkFixtureAttribute orderIdAttrKey "Id" Integer true with
              Column = ColumnRealization.create "ID" false |> Result.value }
          { mkFixtureAttribute orderCustomerFkKey "CustomerId" Integer false with
              Column = ColumnRealization.create "CUSTOMER_ID" false |> Result.value } ]
        with
        References =
            [ Reference.create orderRefToCustomer (name "Customer") orderCustomerFkKey customerKey ] }

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
      Values = StaticRow.presentValues [
          name "Code",  "US"
          name "Label", "United States" ] }
    { Identifier = countryRowKey "CA"
      Values = StaticRow.presentValues [
          name "Code",  "CA"
          name "Label", "Canada" ] }
    { Identifier = countryRowKey "MX"
      Values = StaticRow.presentValues [
          name "Code",  "MX"
          name "Label", "Mexico" ] }
]

let country : Kind =
    { Kind.create
        countryKey
        (name "Country")
        (mkTableId "dbo" "OSUSR_S1S_COUNTRY")
        [ { mkFixtureAttribute countryIdAttrKey "Id" Integer true with
              Column = ColumnRealization.create "ID" false |> Result.value }
          { mkFixtureAttribute countryCodeKey "Code" Text false with
              Column = ColumnRealization.create "CODE" false |> Result.value }
          { mkFixtureAttribute countryLabelKey "Label" Text false with
              Column = ColumnRealization.create "LABEL" false |> Result.value } ]
        with Modality = [ Static countryPopulations ] }

// ---------------------------------------------------------------------------
// Catalog: one module ("Sales") containing all three kinds.
// ---------------------------------------------------------------------------

let salesModuleKey = modKey "Sales"

let salesModule : Module =
    mkModule salesModuleKey (name "Sales") [ customer; order; country ]

let sampleCatalog : Catalog =
    mkCatalog [ salesModule ]

// ---------------------------------------------------------------------------
// Wave-1 round-trip fixtures (slice 1.2 / 1.3). Reusable feature-bearing
// catalogs for the un-hollowed canary axes — DEFAULT constraints and the
// table/catalog-scoped annotations (triggers / CHECK constraints /
// sequences / extended properties). Lifted out of CanaryRoundTripTests
// (per the "don't maintain synthetic catalogs inside business-logic-
// adjacent test files" review) so every feature-vertical reuses ONE
// definition rather than re-spelling 15-field record literals (which drift
// when the IR grows — the exact pain that motivated IRBuilders).
//
// Built through the production smart constructors (Attribute.create /
// Trigger.create / ColumnCheck.create / Sequence.create / ExtendedProperty
// .create) so the fixtures carry the same invariants as the forward path.
// ---------------------------------------------------------------------------

/// A Catalog whose `Account` kind carries integer DEFAULT constraints
/// (slice 1.2's round-trip bed). `Balance DEFAULT 0`, `Status DEFAULT 1`.
let defaultBearingCatalog : Catalog =
    let mkAttr (column: string) (isPk: bool) (def: SqlLiteral option) : Attribute =
        { mkFixtureAttribute (attrKey ["DefAccount"; column]) column Integer isPk with
            Column = ColumnRealization.create (column.ToUpperInvariant()) false |> Result.value
            DefaultValue = def }
    let kind : Kind =
        { customer with
            SsKey = kindKey ["DefAccount"]
            Name = name "Account"
            Physical = mkTableId "dbo" "OSUSR_DEF_ACCOUNT"
            Attributes =
                [ mkAttr "Id" true None
                  mkAttr "Balance" false (Some (SqlLiteral.IntegerLit "0"))
                  mkAttr "Status" false (Some (SqlLiteral.IntegerLit "1")) ]
            References = []
            Indexes = []
            Modality = [] }
    mkCatalog [ mkModule (modKey "DefMod") (name "DefMod") [ kind ] ]

/// A Catalog whose `Widget` kind carries one of each table/catalog-scoped
/// annotation (slice 1.3's round-trip bed): a trigger, a CHECK constraint,
/// a column extended property, plus a catalog-level sequence. `Description`
/// is deliberately left `None` — `MS_Description` ↔ `Description` recovery
/// is a separate in-feature follow-on (ReadSide recovers MS_Description as a
/// generic annotation, not yet as `Description`).
let annotationBearingCatalog : Catalog =
    let mustOkLocal r = match r with | Ok v -> v | Error _ -> failwith "annotationBearingCatalog fixture"
    let mkAttr (column: string) (isPk: bool) (eps: ExtendedProperty list) : Attribute =
        { mkFixtureAttribute (attrKey ["AnnWidget"; column]) column Integer isPk with
            Column = ColumnRealization.create (column.ToUpperInvariant()) false |> Result.value
            ExtendedProperties = eps }
    let custEp = ExtendedProperty.create "Widget.Classification" (Some "operational") |> mustOkLocal
    let chk =
        ColumnCheck.create (testKey "OS_CHK_AnnWidget_Qty") (Some (name "CK_Widget_Qty")) "([QTY]>=(0))" false
        |> mustOkLocal
    let trg =
        Trigger.create (testKey "OS_TRG_AnnWidget") (name "TR_Widget_Audit") false
            "CREATE TRIGGER [dbo].[TR_Widget_Audit] ON [dbo].[OSUSR_ANN_WIDGET] AFTER INSERT AS BEGIN SET NOCOUNT ON; END"
        |> mustOkLocal
    let kind : Kind =
        { customer with
            SsKey = kindKey ["AnnWidget"]
            Name = name "Widget"
            Physical = mkTableId "dbo" "OSUSR_ANN_WIDGET"
            Attributes = [ mkAttr "Id" true []; mkAttr "Qty" false [ custEp ] ]
            References = []
            Indexes = []
            Modality = []
            Triggers = [ trg ]
            ColumnChecks = [ chk ] }
    let seq_ =
        Sequence.create (testKey "OS_SEQ_AnnTicket") (name "SEQ_Ticket") "dbo" "bigint"
            (Some 1000M) (Some 1M) (Some 1000M) (Some 9999999M) false NoCache None
        |> mustOkLocal
    { mkCatalog [ mkModule (modKey "AnnMod") (name "AnnMod") [ kind ] ] with Sequences = [ seq_ ] }

/// A Catalog whose `Gadget` kind carries a PERSISTED computed column
/// (slice 1.3 / L3-S7's round-trip bed). `TotalCents AS ([QTY]*(100))
/// PERSISTED`. The source expression is given WITHOUT SQL Server's outer
/// paren-wrap so both halves normalize equal under
/// `PhysicalSchema.encodeComputed` (`((expr))` ↔ `expr`). `Qty` is the
/// non-computed base column the expression references.
let computedBearingCatalog : Catalog =
    let mustOkC r = match r with | Ok v -> v | Error _ -> failwith "computedBearingCatalog fixture"
    let mkAttr (column: string) (isPk: bool) (computed: ComputedColumnConfig option) : Attribute =
        { mkFixtureAttribute (attrKey ["Gadget"; column]) column Integer isPk with
            Column = ColumnRealization.create (column.ToUpperInvariant()) (not isPk) |> Result.value
            Computed = computed }
    let totalCents = ComputedColumnConfig.create "[QTY]*(100)" true |> mustOkC
    let kind : Kind =
        { customer with
            SsKey = kindKey ["Gadget"]
            Name = name "Gadget"
            Physical = mkTableId "dbo" "OSUSR_CMP_GADGET"
            Attributes =
                [ mkAttr "Id" true None
                  mkAttr "Qty" false None
                  mkAttr "TotalCents" false (Some totalCents) ]
            References = []
            Indexes = []
            Modality = [] }
    mkCatalog [ mkModule (modKey "CmpMod") (name "CmpMod") [ kind ] ]
