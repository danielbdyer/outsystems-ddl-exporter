module Projection.Tests.TableRenameTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `TableRename.run` is private; the
// canonical surface is `.registered.Run`. The original `run` returned
// `Result<Lineage<Catalog>>` (validation can fail). This per-file shim
// reconstructs that shape from the registry's
// `Lineage<Diagnostics<Catalog>>` by promoting Error-severity entries
// back to `Result.Error`.
let private trRun (specs: TableRename.RenameSpec list) (c: Catalog) : Result<Lineage<Catalog>> =
    let lineage = (TableRename.registered specs).Run c
    let diag = lineage.Value
    let errors =
        diag.Entries
        |> List.filter (fun e -> e.Severity = DiagnosticSeverity.Error)
    if List.isEmpty errors then
        Result.success (lineage |> Lineage.map (fun d -> d.Value))
    else
        errors
        |> List.map (fun e ->
            { Code = e.Code
              Message = e.Message
              Metadata = e.Metadata |> Map.map (fun _ v -> Some v) })
        |> Error

// -----------------------------------------------------------------------
// Tests for `Projection.Core.Passes.TableRename` — the pre-emit
// pass that rewrites `Kind.Physical` according to operator-supplied
// rename specs while preserving `Kind.SsKey` identity (A1).
//
// Plus boundary-mapper tests for `Projection.Pipeline.RenameBinding`.
//
// Cite the axioms / decisions the tests enforce:
//   A1   — identity (SsKey) survives rename
//   A18  — emitters never read Policy; intent flows via pre-emit passes
//   A39  — Catalog.create invariants preserved through rewrite
//   D12  — canonical ordering of rename specs (R11 dissolved; see
//          V2_PRODUCTION_CUTOVER §3.6 — topo is SsKey-only)
// -----------------------------------------------------------------------

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        failwithf "Expected Ok, got Error(s): %s" codes

let private mustFail (r: Result<'a>) : ValidationError list =
    match r with
    | Ok _ -> failwith "Expected Error, got Ok."
    | Error es -> es

let private hasCode (code: string) (errors: ValidationError list) : bool =
    errors |> List.exists (fun e -> e.Code = code)

let private mkTableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> mustOk

let private findKindByKey (key: SsKey) (c: Catalog) : Kind =
    Catalog.tryFindKind key c
    |> Option.defaultWith (fun () -> failwithf "Kind not found: %A" key)

// -----------------------------------------------------------------------
// Empty spec list — pass-through.
// -----------------------------------------------------------------------

[<Fact>]
let ``empty rename spec list: catalog unchanged, no lineage events`` () =
    let result = trRun [] sampleCatalog |> mustOk
    Assert.Same(sampleCatalog, result.Value)
    Assert.Empty(result.Trail)

// -----------------------------------------------------------------------
// A1: identity-preserving rename.
// -----------------------------------------------------------------------

[<Fact>]
let ``A1: rename preserves Kind.SsKey while rewriting Kind.Physical`` () =
    let originalCustomer = findKindByKey customerKey sampleCatalog
    let target = mkTableId "core" "CUSTOMER_NEW"
    let specs : TableRename.RenameSpec list = [
        { Key = TableRename.Logical (mkName "Sales", mkName "Customer"); Target = target }
    ]
    let result = trRun specs sampleCatalog |> mustOk
    let renamedCustomer = findKindByKey customerKey result.Value
    Assert.Equal(originalCustomer.SsKey, renamedCustomer.SsKey)
    Assert.Equal(target, renamedCustomer.Physical)
    Assert.NotEqual(originalCustomer.Physical, renamedCustomer.Physical)

[<Fact>]
let ``rename emits one PhysicallyRenamed event carrying typed before/after TableIds`` () =
    let originalCustomer = findKindByKey customerKey sampleCatalog
    let target = mkTableId "core" "CUSTOMER_NEW"
    let specs : TableRename.RenameSpec list = [
        { Key = TableRename.Logical (mkName "Sales", mkName "Customer"); Target = target }
    ]
    let result = trRun specs sampleCatalog |> mustOk
    Assert.Equal(1, result.Trail.Length)
    let evt = List.head result.Trail
    Assert.Equal("tableRename", evt.PassName)
    Assert.Equal(TableRename.version, evt.PassVersion)
    Assert.Equal(customerKey, evt.SsKey)
    match evt.TransformKind with
    | PhysicallyRenamed payload ->
        Assert.Equal(originalCustomer.Physical, payload.Before)
        Assert.Equal(target,                     payload.After)
    | other -> failwithf "Expected PhysicallyRenamed, got %A" other

[<Fact>]
let ``PhysicalRename.toDiagnosticString renders schema.table -> schema.table`` () =
    let payload : PhysicalRename = {
        Before = mkTableId "dbo"  "OSUSR_X"
        After  = mkTableId "core" "NEW_X"
    }
    let rendered = PhysicalRename.toDiagnosticString payload
    Assert.Equal("dbo.OSUSR_X -> core.NEW_X", rendered)

[<Fact>]
let ``no-op rename (target equals current physical) emits no lineage event`` () =
    let currentPhysical = (findKindByKey customerKey sampleCatalog).Physical
    let specs : TableRename.RenameSpec list = [
        { Key = TableRename.Logical (mkName "Sales", mkName "Customer"); Target = currentPhysical }
    ]
    let result = trRun specs sampleCatalog |> mustOk
    Assert.Empty(result.Trail)
    Assert.Equal(currentPhysical, (findKindByKey customerKey result.Value).Physical)

// -----------------------------------------------------------------------
// References stay rename-safe (they carry SsKey, not TableId).
// -----------------------------------------------------------------------

[<Fact>]
let ``references to renamed Kind continue to resolve via SsKey`` () =
    let specs : TableRename.RenameSpec list = [
        { Key = TableRename.Logical (mkName "Sales", mkName "Customer")
          Target = mkTableId "core" "CUSTOMER_NEW" }
    ]
    let result = trRun specs sampleCatalog |> mustOk
    let renamedOrder = findKindByKey orderKey result.Value
    let refToCustomer = renamedOrder.References |> List.head
    Assert.Equal(customerKey, refToCustomer.TargetKind)

// -----------------------------------------------------------------------
// Both source forms resolve correctly.
// -----------------------------------------------------------------------

[<Fact>]
let ``physical source form: schema.table resolves correctly`` () =
    let target = mkTableId "core" "ORDER_NEW"
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Physical (mkTableId "dbo" "OSUSR_S1S_ORDER")
          Target = target }
    ]
    let result = trRun specs sampleCatalog |> mustOk
    let renamedOrder = findKindByKey orderKey result.Value
    Assert.Equal(target, renamedOrder.Physical)

[<Fact>]
let ``mixed logical and physical source forms each apply independently`` () =
    let custTarget  = mkTableId "core" "CUSTOMER_NEW"
    let orderTarget = mkTableId "core" "ORDER_NEW"
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Logical (mkName "Sales", mkName "Customer"); Target = custTarget }
        { Key    = TableRename.Physical (mkTableId "dbo" "OSUSR_S1S_ORDER"); Target = orderTarget }
    ]
    let result = trRun specs sampleCatalog |> mustOk
    Assert.Equal(custTarget,  (findKindByKey customerKey result.Value).Physical)
    Assert.Equal(orderTarget, (findKindByKey orderKey    result.Value).Physical)

// -----------------------------------------------------------------------
// Validation failures.
// -----------------------------------------------------------------------

[<Fact>]
let ``source not found fails with structured error`` () =
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Logical (mkName "Sales", mkName "Phantom")
          Target = mkTableId "core" "ANY" }
    ]
    let errors = trRun specs sampleCatalog |> mustFail
    Assert.True(hasCode "rename.sourceNotFound" errors)

[<Fact>]
let ``two specs targeting the same kind fail with sourceDuplicate`` () =
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Logical  (mkName "Sales", mkName "Customer")
          Target = mkTableId "core" "CUSTOMER_A" }
        { Key    = TableRename.Physical (mkTableId "dbo" "OSUSR_S1S_CUSTOMER")
          Target = mkTableId "core" "CUSTOMER_B" }
    ]
    let errors = trRun specs sampleCatalog |> mustFail
    Assert.True(hasCode "rename.sourceDuplicate" errors)

[<Fact>]
let ``two specs mapping to the same target fail with targetCollision`` () =
    let shared = mkTableId "core" "SHARED"
    let specs : TableRename.RenameSpec list = [
        { Key = TableRename.Logical (mkName "Sales", mkName "Customer"); Target = shared }
        { Key = TableRename.Logical (mkName "Sales", mkName "Order");    Target = shared }
    ]
    let errors = trRun specs sampleCatalog |> mustFail
    Assert.True(hasCode "rename.targetCollision" errors)

// -----------------------------------------------------------------------
// Determinism (D12 / R11 dissolved): rename order in the spec list
// does not change the output catalog. Topo is SsKey-only so rename
// ordering cannot perturb downstream emission.
// -----------------------------------------------------------------------

[<Fact>]
let ``D12: rename spec order does not affect the rewritten catalog`` () =
    let custTarget  = mkTableId "core" "CUSTOMER_NEW"
    let orderTarget = mkTableId "core" "ORDER_NEW"
    let specsA : TableRename.RenameSpec list = [
        { Key = TableRename.Logical (mkName "Sales", mkName "Customer"); Target = custTarget }
        { Key = TableRename.Logical (mkName "Sales", mkName "Order");    Target = orderTarget }
    ]
    let specsB : TableRename.RenameSpec list = [
        { Key = TableRename.Logical (mkName "Sales", mkName "Order");    Target = orderTarget }
        { Key = TableRename.Logical (mkName "Sales", mkName "Customer"); Target = custTarget }
    ]
    let resultA = trRun specsA sampleCatalog |> mustOk
    let resultB = trRun specsB sampleCatalog |> mustOk
    Assert.Equal(resultA.Value, resultB.Value)

// -----------------------------------------------------------------------
// Boundary mapper: Pipeline.RenameBinding.fromConfig
// -----------------------------------------------------------------------

module RenameBindingTests =

    open Projection.Pipeline

    [<Fact>]
    let ``empty config list maps to empty Core list`` () =
        let specs = RenameBinding.fromConfig [] |> mustOk
        Assert.Empty(specs)

    [<Fact>]
    let ``logical rename round-trips through boundary`` () =
        let configRename : Config.TableRename = {
            From = Config.LogicalSource { Module = "Sales"; Entity = "Customer" }
            To   = { Schema = "core"; Table = "CUSTOMER_NEW" }
        }
        let specs = RenameBinding.fromConfig [ configRename ] |> mustOk
        Assert.Equal(1, specs.Length)
        match specs.[0].Key with
        | TableRename.Logical (m, e) ->
            Assert.Equal("Sales",    Name.value m)
            Assert.Equal("Customer", Name.value e)
        | _ -> failwith "Expected Logical key"
        Assert.Equal("core",         TableId.schemaText specs.[0].Target)
        Assert.Equal("CUSTOMER_NEW", TableId.tableText specs.[0].Target)

    [<Fact>]
    let ``physical rename round-trips through boundary`` () =
        let configRename : Config.TableRename = {
            From = Config.PhysicalSource { Schema = "dbo"; Table = "OSUSR_OLD" }
            To   = { Schema = "core"; Table = "OSUSR_NEW" }
        }
        let specs = RenameBinding.fromConfig [ configRename ] |> mustOk
        match specs.[0].Key with
        | TableRename.Physical t ->
            Assert.Equal("dbo",       TableId.schemaText t)
            Assert.Equal("OSUSR_OLD", TableId.tableText t)
        | _ -> failwith "Expected Physical key"

    [<Fact>]
    let ``invalid logical source (empty module name) fails`` () =
        let configRename : Config.TableRename = {
            From = Config.LogicalSource { Module = ""; Entity = "Customer" }
            To   = { Schema = "core"; Table = "OK" }
        }
        let errors = RenameBinding.fromConfig [ configRename ] |> mustFail
        Assert.True(errors |> List.exists (fun e -> e.Code = "name.empty"))

    [<Fact>]
    let ``invalid physical target (empty schema) fails`` () =
        let configRename : Config.TableRename = {
            From = Config.LogicalSource { Module = "Sales"; Entity = "Customer" }
            To   = { Schema = ""; Table = "OK" }
        }
        let errors = RenameBinding.fromConfig [ configRename ] |> mustFail
        Assert.True(errors |> List.exists (fun e -> e.Code = "tableId.schema.empty"))

    [<Fact>]
    let ``multiple invalid entries aggregate all errors`` () =
        let a : Config.TableRename = {
            From = Config.LogicalSource { Module = ""; Entity = "X" }
            To   = { Schema = ""; Table = "Y" }
        }
        let b : Config.TableRename = {
            From = Config.LogicalSource { Module = "M"; Entity = "" }
            To   = { Schema = "s"; Table = "" }
        }
        let errors = RenameBinding.fromConfig [ a; b ] |> mustFail
        Assert.True(errors.Length >= 4)
