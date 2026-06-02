module Projection.Tests.LogicalNameEmissionTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Slice D.1.a — `LogicalTableEmission` + `LogicalColumnEmission`.
//
// These passes SUBSTITUTE the logical name (already in the catalog) into
// the physical-realization slot the emitter reads. They are NOT renames —
// no new name is authored; both axes already exist in the catalog. The
// substitution lets SSDT emission show `[dbo].[Customer]([Email])` instead
// of `[dbo].[OSUSR_ABC_CUSTOMER]([EMAIL])` without the emitter layer
// changing what it reads.
//
// Disciplines under test:
//   A1   — SsKey carries through unchanged (identity preserved).
//   A18 amended — emitter consumes `Kind.Physical` + `ColumnRealization.columnNameText Attribute.Column`;
//                 the pass writes there pre-emit. No Policy in the emitter.
//   Pillar 9 — both passes classified `OperatorIntent of Emission`; events
//             carry the classification.
//
// Fixture shape: a catalog with deliberate divergence between logical
// names (Customer / Email) and physical realizations (OSUSR_ABC_CUSTOMER
// / EMAIL) — the OSSYS shape the slice targets.
// ---------------------------------------------------------------------------

let private name (s: string) : Name =
    match Name.create s with
    | Ok n -> n
    | Error es -> failwithf "fixture name '%s' invalid: %A" s es

let private tableId (schema: string) (table: string) : TableId =
    match TableId.create schema table with
    | Ok t -> t
    | Error es -> failwithf "fixture TableId '%s.%s' invalid: %A" schema table es

let private divergentCatalog () : Catalog =
    // Logical name "Customer"; physical "OSUSR_ABC_CUSTOMER".
    // Attribute logical "Email"; physical "EMAIL".
    let customerKey = kindKey ["Sales"; "Customer"]
    let emailAttr =
        let a = Attribute.create (attrKey ["Sales"; "Customer"; "Email"]) (name "Email") PrimitiveType.Text
        { a with Column = ColumnRealization.create ("EMAIL") (false) |> Result.value }
    let idAttr =
        let a = Attribute.create (attrKey ["Sales"; "Customer"; "Id"]) (name "Id") PrimitiveType.Integer
        { a with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
    let customer =
        Kind.create
            customerKey
            (name "Customer")
            (tableId "dbo" "OSUSR_ABC_CUSTOMER")
            [ idAttr; emailAttr ]
    let salesModule =
        { SsKey              = modKey "Sales"
          Name               = name "Sales"
          Kinds              = [ customer ]
          IsActive           = true
          ExtendedProperties = [] }
    { Modules = [ salesModule ]; Sequences = [] }

// ---------------------------------------------------------------------------
// LogicalTableEmission
// ---------------------------------------------------------------------------

let private runTablePass (mode: LogicalTableEmission.Mode) (c: Catalog) : Lineage<Diagnostics<Catalog>> =
    (LogicalTableEmission.registered mode).Run c

[<Fact>]
let ``LogicalTableEmission Enabled: Kind.Physical.Table is substituted with Name.value k.Name`` () =
    let catalog = divergentCatalog ()
    let result = runTablePass LogicalTableEmission.Enabled catalog
    let after = result.Value.Value
    let customer = Catalog.allKinds after |> List.head
    Assert.Equal("Customer", TableId.tableText customer.Physical)
    Assert.Equal("dbo", TableId.schemaText customer.Physical)

[<Fact>]
let ``LogicalTableEmission Enabled: Kind.SsKey is byte-identical post-substitution (A1)`` () =
    let catalog = divergentCatalog ()
    let before = Catalog.allKinds catalog |> List.head
    let after = (runTablePass LogicalTableEmission.Enabled catalog).Value.Value
    let afterCustomer = Catalog.allKinds after |> List.head
    Assert.Equal(before.SsKey, afterCustomer.SsKey)

[<Fact>]
let ``LogicalTableEmission Enabled: Kind.Name is untouched (only physical-realization slot changes)`` () =
    let catalog = divergentCatalog ()
    let after = (runTablePass LogicalTableEmission.Enabled catalog).Value.Value
    let customer = Catalog.allKinds after |> List.head
    Assert.Equal(name "Customer", customer.Name)

[<Fact>]
let ``LogicalTableEmission Enabled: emits one PhysicallyRenamed event per substituted kind`` () =
    let catalog = divergentCatalog ()
    let lineage = runTablePass LogicalTableEmission.Enabled catalog
    let substEvents =
        lineage.Trail
        |> List.filter (fun e ->
            match e.TransformKind with
            | PhysicallyRenamed _ -> true
            | _ -> false)
    Assert.Equal(1, List.length substEvents)
    match (List.head substEvents).TransformKind with
    | PhysicallyRenamed payload ->
        Assert.Equal("OSUSR_ABC_CUSTOMER", TableId.tableText payload.Before)
        Assert.Equal("Customer", TableId.tableText payload.After)
    | other -> failwithf "Expected PhysicallyRenamed, got %A" other

[<Fact>]
let ``LogicalTableEmission Enabled: lineage events classified OperatorIntent Emission (pillar 9)`` () =
    let catalog = divergentCatalog ()
    let lineage = runTablePass LogicalTableEmission.Enabled catalog
    Assert.NotEmpty lineage.Trail
    Assert.All(
        lineage.Trail,
        fun e -> Assert.Equal(OperatorIntent Emission, e.Classification))

[<Fact>]
let ``LogicalTableEmission Enabled: kinds whose logical = physical emit no event (no-op)`` () =
    let aligned =
        let customer =
            Kind.create
                (kindKey ["Sales"; "Customer"])
                (name "Customer")
                (tableId "dbo" "Customer")
                []
        let m =
            { SsKey = modKey "Sales"; Name = name "Sales"; Kinds = [ customer ]
              IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }
    let lineage = runTablePass LogicalTableEmission.Enabled aligned
    Assert.Empty lineage.Trail

[<Fact>]
let ``LogicalTableEmission Disabled: pass-through; no rewrites and no events`` () =
    let catalog = divergentCatalog ()
    let lineage = runTablePass LogicalTableEmission.Disabled catalog
    Assert.Empty lineage.Trail
    let after = lineage.Value.Value
    let customer = Catalog.allKinds after |> List.head
    Assert.Equal("OSUSR_ABC_CUSTOMER", TableId.tableText customer.Physical)

[<Fact>]
let ``LogicalTableEmission registry: classification is OperatorIntent Emission`` () =
    let rt = LogicalTableEmission.registered LogicalTableEmission.Enabled
    let site = List.head rt.Sites
    Assert.Equal(OperatorIntent Emission, site.Classification)

// ---------------------------------------------------------------------------
// LogicalColumnEmission
// ---------------------------------------------------------------------------

let private runColumnPass (mode: LogicalColumnEmission.Mode) (c: Catalog) : Lineage<Diagnostics<Catalog>> =
    (LogicalColumnEmission.registered mode).Run c

[<Fact>]
let ``LogicalColumnEmission Enabled: ColumnRealization.columnNameText Attribute.Column substituted with Name.value a.Name`` () =
    let catalog = divergentCatalog ()
    let after = (runColumnPass LogicalColumnEmission.Enabled catalog).Value.Value
    let customer = Catalog.allKinds after |> List.head
    let email = customer.Attributes |> List.find (fun a -> Name.value a.Name = "Email")
    Assert.Equal("Email", ColumnRealization.columnNameText email.Column)

[<Fact>]
let ``LogicalColumnEmission Enabled: Attribute.SsKey + Attribute.Name unchanged`` () =
    let catalog = divergentCatalog ()
    let beforeEmail =
        Catalog.allKinds catalog
        |> List.head
        |> fun k -> k.Attributes |> List.find (fun a -> Name.value a.Name = "Email")
    let after = (runColumnPass LogicalColumnEmission.Enabled catalog).Value.Value
    let afterEmail =
        Catalog.allKinds after
        |> List.head
        |> fun k -> k.Attributes |> List.find (fun a -> Name.value a.Name = "Email")
    Assert.Equal(beforeEmail.SsKey, afterEmail.SsKey)
    Assert.Equal(beforeEmail.Name, afterEmail.Name)
    Assert.Equal(beforeEmail.Column.IsNullable, afterEmail.Column.IsNullable)

[<Fact>]
let ``LogicalColumnEmission Enabled: emits one ColumnPhysicallyRenamed event per substituted attribute`` () =
    let catalog = divergentCatalog ()
    let lineage = runColumnPass LogicalColumnEmission.Enabled catalog
    let substEvents =
        lineage.Trail
        |> List.filter (fun e ->
            match e.TransformKind with
            | ColumnPhysicallyRenamed _ -> true
            | _ -> false)
    // Two attributes diverge: Id (logical) vs ID (physical), Email vs EMAIL.
    Assert.Equal(2, List.length substEvents)

[<Fact>]
let ``LogicalColumnEmission Enabled: event payload carries kind coordinate + before/after column names`` () =
    let catalog = divergentCatalog ()
    let lineage = runColumnPass LogicalColumnEmission.Enabled catalog
    let emailEvent =
        lineage.Trail
        |> List.pick (fun e ->
            match e.TransformKind with
            | ColumnPhysicallyRenamed payload when payload.Before = "EMAIL" -> Some payload
            | _ -> None)
    Assert.Equal("OSUSR_ABC_CUSTOMER", TableId.tableText emailEvent.Kind)
    Assert.Equal("EMAIL", emailEvent.Before)
    Assert.Equal("Email", emailEvent.After)

[<Fact>]
let ``LogicalColumnEmission Enabled: lineage events classified OperatorIntent Emission (pillar 9)`` () =
    let catalog = divergentCatalog ()
    let lineage = runColumnPass LogicalColumnEmission.Enabled catalog
    Assert.NotEmpty lineage.Trail
    Assert.All(
        lineage.Trail,
        fun e -> Assert.Equal(OperatorIntent Emission, e.Classification))

[<Fact>]
let ``LogicalColumnEmission Disabled: pass-through; no rewrites and no events`` () =
    let catalog = divergentCatalog ()
    let lineage = runColumnPass LogicalColumnEmission.Disabled catalog
    Assert.Empty lineage.Trail
    let after = lineage.Value.Value
    let customer = Catalog.allKinds after |> List.head
    let email = customer.Attributes |> List.find (fun a -> Name.value a.Name = "Email")
    Assert.Equal("EMAIL", ColumnRealization.columnNameText email.Column)

[<Fact>]
let ``LogicalColumnEmission registry: classification is OperatorIntent Emission`` () =
    let rt = LogicalColumnEmission.registered LogicalColumnEmission.Enabled
    let site = List.head rt.Sites
    Assert.Equal(OperatorIntent Emission, site.Classification)

// ---------------------------------------------------------------------------
// Composed: both passes run via the production chain → emitter sees logical
// names at every read site (table identifier, column identifier, PK / FK
// derivation that composes Kind.Physical.Table + Column.ColumnName).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice D.1.a end-to-end: after both passes, every Kind.Physical.Table and Column.ColumnName equals the logical name`` () =
    let catalog = divergentCatalog ()
    let lineage =
        catalog
        |> (LogicalTableEmission.registered LogicalTableEmission.Enabled).Run
        |> LineageDiagnostics.bind (fun c -> (LogicalColumnEmission.registered LogicalColumnEmission.Enabled).Run c)
    let after = lineage.Value.Value
    for k in Catalog.allKinds after do
        Assert.Equal(Name.value k.Name, TableId.tableText k.Physical)
        for a in k.Attributes do
            Assert.Equal(Name.value a.Name, ColumnRealization.columnNameText a.Column)
