module Projection.Tests.SpecialCircumstancesDiagnosticsTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

/// Chapter C slice C.2 — `SpecialCircumstancesDiagnostics.emit`
/// coverage. Two structural findings under scan: missing-PK target
/// kinds (referenced by some other kind) + unresolved cycles from
/// the post-chain `TopologicalOrder`. Acceptance annotation via
/// `Metadata.acceptedVia` cross-references the operator allowlist.


let private mkKindNoPk (kindName: string) (table: string) : Kind =
    let kKey = kindKey [ kindName ]
    let aKey = attrKey [ kindName; "Label" ]
    { Kind.create
        kKey
        (mkName kindName)
        (mkTableId "dbo" table)
        [ { Attribute.create aKey (mkName "Label") PrimitiveType.Text with
              Column       = ColumnRealization.create ("LABEL") (false) |> Result.value
              IsPrimaryKey = false } ]
        with References = [] }

let private mkKindWithRefTo (kindName: string) (table: string) (target: Kind) : Kind =
    let kKey  = kindKey [ kindName ]
    let idKey = attrKey [ kindName; "Id" ]
    let fkKey = attrKey [ kindName; sprintf "%sId" (Name.value target.Name) ]
    let rKey  = refKey  [ kindName; Name.value target.Name ]
    { Kind.create
        kKey
        (mkName kindName)
        (mkTableId "dbo" table)
        [ { Attribute.create idKey (mkName "Id") PrimitiveType.Integer with
              Column       = ColumnRealization.create ("ID") (false) |> Result.value
              IsPrimaryKey = true }
          { Attribute.create fkKey (mkName (sprintf "%sId" (Name.value target.Name))) PrimitiveType.Integer with
              Column       = ColumnRealization.create (sprintf "%s_ID" ((Name.value target.Name).ToUpperInvariant())) false |> Result.value
              IsPrimaryKey = false } ]
        with
        References =
            [ Reference.create rKey target.Name fkKey target.SsKey ] }

let private stateOf (catalog: Catalog) : ComposeState =
    ComposeState.initial catalog

// ----------------------------------------------------------------------
// Missing primary key
// ----------------------------------------------------------------------

[<Fact>]
let ``C.2: emit yields no missing-PK entries when every referenced kind has a PK`` () =
    // sampleCatalog: Order → Customer (Customer has PK), plus Country (static).
    let state = stateOf sampleCatalog
    let entries =
        SpecialCircumstancesDiagnostics.emit SpecialCircumstances.empty state
    let pkEntries =
        entries |> List.filter (fun e -> e.Code = "structural.targetMissingPrimaryKey")
    Assert.Empty(pkEntries)

[<Fact>]
let ``C.2: emit surfaces one missing-PK entry per missing-PK referenced kind`` () =
    let auditNoPk = mkKindNoPk "Audit" "OSUSR_TEST_AUDIT"
    let logger    = mkKindWithRefTo "Logger" "OSUSR_TEST_LOGGER" auditNoPk
    let modKey'   = modKey "Test"
    let catalog   =
        mkCatalog [ mkModule modKey' (mkName "Test") [ auditNoPk; logger ] ]
    let entries =
        SpecialCircumstancesDiagnostics.emit SpecialCircumstances.empty (stateOf catalog)
    let pkEntries =
        entries |> List.filter (fun e -> e.Code = "structural.targetMissingPrimaryKey")
    Assert.Equal(1, pkEntries.Length)
    let entry = pkEntries |> List.head
    Assert.Equal(Some auditNoPk.SsKey, entry.SsKey)
    Assert.Equal(DiagnosticSeverity.Warning, entry.Severity)
    Assert.False(Map.containsKey "acceptedVia" entry.Metadata)

[<Fact>]
let ``C.2: emit deduplicates missing-PK to one entry per target kind even with N referencing kinds`` () =
    let auditNoPk = mkKindNoPk "Audit" "OSUSR_TEST_AUDIT"
    let l1        = mkKindWithRefTo "LoggerA" "OSUSR_TEST_LOGA" auditNoPk
    let l2        = mkKindWithRefTo "LoggerB" "OSUSR_TEST_LOGB" auditNoPk
    let modKey'   = modKey "Test"
    let catalog   =
        mkCatalog [ mkModule modKey' (mkName "Test") [ auditNoPk; l1; l2 ] ]
    let entries =
        SpecialCircumstancesDiagnostics.emit SpecialCircumstances.empty (stateOf catalog)
    let pkEntries =
        entries |> List.filter (fun e -> e.Code = "structural.targetMissingPrimaryKey")
    Assert.Equal(1, pkEntries.Length)

[<Fact>]
let ``C.2: missing-PK entry whose target is in allowlist carries acceptedVia metadata`` () =
    let auditNoPk = mkKindNoPk "Audit" "OSUSR_TEST_AUDIT"
    let logger    = mkKindWithRefTo "Logger" "OSUSR_TEST_LOGGER" auditNoPk
    let modKey'   = modKey "Test"
    let catalog   =
        mkCatalog [ mkModule modKey' (mkName "Test") [ auditNoPk; logger ] ]
    let overrides = { SpecialCircumstances.empty with
                        AllowedMissingPrimaryKeys = Set.singleton auditNoPk.SsKey }
    let entries =
        SpecialCircumstancesDiagnostics.emit overrides (stateOf catalog)
    let entry =
        entries
        |> List.find (fun e -> e.Code = "structural.targetMissingPrimaryKey")
    Assert.Equal(
        Some "config:overrides.allowMissingPrimaryKey",
        Map.tryFind "acceptedVia" entry.Metadata)

[<Fact>]
let ``C.2: ignored unreferenced missing-PK kinds — scan only fires on referenced targets`` () =
    let lonelyNoPk = mkKindNoPk "Lonely" "OSUSR_TEST_LONELY"
    let modKey'    = modKey "Test"
    let catalog    =
        mkCatalog [ mkModule modKey' (mkName "Test") [ lonelyNoPk ] ]
    let entries =
        SpecialCircumstancesDiagnostics.emit SpecialCircumstances.empty (stateOf catalog)
    let pkEntries =
        entries |> List.filter (fun e -> e.Code = "structural.targetMissingPrimaryKey")
    Assert.Empty(pkEntries)

// ----------------------------------------------------------------------
// Unresolved cycles
// ----------------------------------------------------------------------

let private mkCycleDiag (members: SsKey list) (reason: string) : CycleDiagnostic = {
    Members        = members
    BreakableEdges = []
    Reason         = reason
}

let private mkTopo (cycles: CycleDiagnostic list) : TopologicalOrder = {
    Mode         = Alphabetical
    Order        = []
    Edges        = []
    MissingEdges = []
    Cycles       = cycles
    Diagnostics  = []
}

let private stateWithTopo (catalog: Catalog) (topo: TopologicalOrder) : ComposeState =
    { ComposeState.initial catalog with TopologicalOrder = Some topo }

[<Fact>]
let ``C.2: emit yields no cycle entries when TopologicalOrder absent`` () =
    let entries =
        SpecialCircumstancesDiagnostics.emit SpecialCircumstances.empty (stateOf sampleCatalog)
    let cycleEntries =
        entries |> List.filter (fun e -> e.Code = "structural.cycleUnresolved")
    Assert.Empty(cycleEntries)

[<Fact>]
let ``C.2: emit yields one cycle entry per unresolved cycle in TopologicalOrder`` () =
    let topo = mkTopo [ mkCycleDiag [ customerKey; orderKey ] "unresolved 2-cycle" ]
    let state = stateWithTopo sampleCatalog topo
    let entries = SpecialCircumstancesDiagnostics.emit SpecialCircumstances.empty state
    let cycleEntries =
        entries |> List.filter (fun e -> e.Code = "structural.cycleUnresolved")
    Assert.Equal(1, cycleEntries.Length)
    let entry = cycleEntries |> List.head
    Assert.Equal(DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal(None, entry.SsKey)
    Assert.False(Map.containsKey "acceptedVia" entry.Metadata)

[<Fact>]
let ``C.2: cycle entry whose member-set matches allowlist carries acceptedVia metadata`` () =
    let cycleMembers = Set.ofList [ customerKey; orderKey ]
    let topo = mkTopo [ mkCycleDiag [ customerKey; orderKey ] "unresolved 2-cycle" ]
    let state = stateWithTopo sampleCatalog topo
    let overrides = { SpecialCircumstances.empty with
                        AllowedCycles = Set.singleton cycleMembers }
    let entries = SpecialCircumstancesDiagnostics.emit overrides state
    let entry =
        entries |> List.find (fun e -> e.Code = "structural.cycleUnresolved")
    Assert.Equal(
        Some "config:overrides.circularDependencies",
        Map.tryFind "acceptedVia" entry.Metadata)

[<Fact>]
let ``C.2: cycle allowlist matches on set-equality (order-independent)`` () =
    // Allowlist registered with members in reverse order — set semantics
    // mean the cycle is still recognized.
    let cycleMembersReversed = Set.ofList [ orderKey; customerKey ]
    let topo = mkTopo [ mkCycleDiag [ customerKey; orderKey ] "unresolved 2-cycle" ]
    let state = stateWithTopo sampleCatalog topo
    let overrides = { SpecialCircumstances.empty with
                        AllowedCycles = Set.singleton cycleMembersReversed }
    let entries = SpecialCircumstancesDiagnostics.emit overrides state
    let entry =
        entries |> List.find (fun e -> e.Code = "structural.cycleUnresolved")
    Assert.True(Map.containsKey "acceptedVia" entry.Metadata)

[<Fact>]
let ``C.2: cycle entry carries member list in Metadata`` () =
    let topo = mkTopo [ mkCycleDiag [ customerKey; orderKey ] "test cycle" ]
    let state = stateWithTopo sampleCatalog topo
    let entries = SpecialCircumstancesDiagnostics.emit SpecialCircumstances.empty state
    let entry =
        entries |> List.find (fun e -> e.Code = "structural.cycleUnresolved")
    let members = Map.find "members" entry.Metadata
    Assert.Contains(SsKey.rootOriginal customerKey, members)
    Assert.Contains(SsKey.rootOriginal orderKey, members)
    Assert.Equal("test cycle", Map.find "reason" entry.Metadata)
