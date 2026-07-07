module Projection.Tests.TransferScopeTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Pipeline
open Projection.Tests.Fixtures

let private mkKey (s: string) : SsKey = testKey s

// ---------------------------------------------------------------------------
// The EFFECTIVE TRANSFER GRAPH (2026-07-07, the go-board scoping program;
// readiness log Entry 25). `TransferScope` reifies "which kinds does this
// transfer actually touch"; its `topology` runs ordering/cycle analysis on
// the induced subgraph (FK edges bind only between written kinds), so:
//   * an unrelated estate cycle no longer degrades a subset transfer's
//     load order or mints out-of-subset UnbreakableCycleFks;
//   * a cycle through a RECONCILED kind (never written; FKs re-keyed to
//     pre-existing sink rows) correctly breaks;
//   * a full, non-reconciling transfer is the identity scope —
//     byte-identical to the unscoped pass.
// ---------------------------------------------------------------------------

/// A kind with one non-nullable NoAction FK — EdgeStrength `Other`, which
/// the asymmetric-2-cycle resolver refuses to break: two of these pointing
/// at each other are an UNRESOLVABLE cycle (Mode = Alphabetical).
let private strongRefKind (kindKey: string) (targetKey: SsKey) : Kind =
    let attrId = mkKey (kindKey + "_Id")
    let attrFk = mkKey (kindKey + "_Fk")
    { SsKey = mkKey kindKey
      Name = mkName kindKey
      Origin = Native
      Modality = []
      Physical = mkTableId "dbo" kindKey
      Attributes = [
          { Attribute.create attrId (mkName "Id") Integer with Column = ColumnRealization.create "ID" false |> Result.value; IsPrimaryKey = true }
          { Attribute.create attrFk (mkName "Fk") Integer with Column = ColumnRealization.create "FK" false |> Result.value } ]
      References = [
          Reference.create (mkKey (kindKey + "_Ref")) (mkName "ToOther") attrFk targetKey ]
      Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

let private plainKind (kindKey: string) : Kind =
    let attrId = mkKey (kindKey + "_Id")
    { SsKey = mkKey kindKey
      Name = mkName kindKey
      Origin = Native
      Modality = []
      Physical = mkTableId "dbo" kindKey
      Attributes = [
          { Attribute.create attrId (mkName "Id") Integer with Column = ColumnRealization.create "ID" false |> Result.value; IsPrimaryKey = true } ]
      References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

let private catalogOf (kinds: Kind list) : Catalog =
    { Modules = [ { SsKey = mkKey "M"; Name = mkName "M"; Kinds = kinds; IsActive = true; ExtendedProperties = [] } ]
      Sequences = [] }

/// City ← Customer (acyclic subset) alongside an UNRELATED unresolvable
/// two-cycle (CycleA ↔ CycleB, both edges non-nullable).
let private estateWithUnrelatedCycle : Catalog =
    catalogOf
        [ plainKind "City"
          strongRefKind "Customer" (mkKey "City")
          strongRefKind "CycleA" (mkKey "CycleB")
          strongRefKind "CycleB" (mkKey "CycleA") ]

let private subsetScope : TransferScope =
    TransferScope.create estateWithUnrelatedCycle (Some (Set.ofList [ mkKey "City"; mkKey "Customer" ])) Set.empty

[<Fact>]
let ``an unrelated unresolvable estate cycle degrades the WHOLE-estate order (the pre-scope behavior, kept for full transfers)`` () =
    let whole = (TopologicalOrderPass.runWith TreatAsCycle estateWithUnrelatedCycle).Value
    Assert.Equal(Alphabetical, whole.Mode)
    Assert.True(Option.isSome (Transfer.orderedLoadGate whole))

[<Fact>]
let ``the scoped topology ignores an unrelated estate cycle — the subset's load order proves topological`` () =
    let topo = TransferScope.topology TreatAsCycle subsetScope estateWithUnrelatedCycle
    Assert.Equal(Topological, topo.Mode)
    Assert.True(Option.isNone (Transfer.orderedLoadGate topo))
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "City"; mkKey "Customer" ], Set.ofList topo.Order)
    Assert.True(TopologicalOrder.precedes (mkKey "City") (mkKey "Customer") topo)

[<Fact>]
let ``the scoped plan mints no out-of-subset UnbreakableCycleFks — cycle lines mention only the transfer closure`` () =
    let topo = TransferScope.topology TreatAsCycle subsetScope estateWithUnrelatedCycle
    let plan =
        DataLoadPlan.build estateWithUnrelatedCycle topo Map.empty SurrogateRemapContext.empty
    Assert.Empty(plan.UnbreakableCycleFks)
    // The unscoped topology DOES mint them — the contrast that pins the fix.
    let whole = (TopologicalOrderPass.runWith TreatAsCycle estateWithUnrelatedCycle).Value
    let wholePlan =
        DataLoadPlan.build estateWithUnrelatedCycle whole Map.empty SurrogateRemapContext.empty
    Assert.NotEmpty(wholePlan.UnbreakableCycleFks)

[<Fact>]
let ``a cycle through a RECONCILED kind breaks — the reconciled kind rides as an isolated node`` () =
    // Customer ↔ Account, both edges strong (unresolvable whole-estate);
    // Account reconciled → its edges drop; Customer orders freely and
    // Account stays in the Order (its ReconciledByRule report line).
    let estate =
        catalogOf
            [ strongRefKind "Customer" (mkKey "Account")
              strongRefKind "Account" (mkKey "Customer") ]
    let scope = TransferScope.create estate None (Set.ofList [ mkKey "Account" ])
    let topo = TransferScope.topology TreatAsCycle scope estate
    Assert.Equal(Topological, topo.Mode)
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "Customer"; mkKey "Account" ], Set.ofList topo.Order)
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "Customer" ], scope.WriteKinds)

[<Fact>]
let ``a full non-reconciling transfer is the identity scope — scoped and unscoped topologies agree`` () =
    let scope = TransferScope.create estateWithUnrelatedCycle None Set.empty
    let scoped = TransferScope.topology TreatAsCycle scope estateWithUnrelatedCycle
    let whole = (TopologicalOrderPass.runWith TreatAsCycle estateWithUnrelatedCycle).Value
    Assert.Equal(whole, scoped)

[<Fact>]
let ``TransferScope.create drops unknown keys and excludes reconciled kinds from the write set`` () =
    let estate = catalogOf [ plainKind "City"; plainKind "Country" ]
    let scope =
        TransferScope.create
            estate
            (Some (Set.ofList [ mkKey "City"; mkKey "Ghost" ]))
            (Set.ofList [ mkKey "Country"; mkKey "AlsoGhost" ])
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "City" ], scope.WriteKinds)
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "City"; mkKey "Country" ], scope.Nodes)
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "Country" ], scope.Reconciled)

[<Fact>]
let ``plannedTransferWrites: INSERT+UPDATE per written kind, DELETE only under a wipe, nothing for reconciled kinds`` () =
    let estate = catalogOf [ plainKind "City"; plainKind "Country" ]
    let scope = TransferScope.create estate None (Set.ofList [ mkKey "Country" ])
    let incremental = Transfer.plannedTransferWrites scope EmissionMode.Incremental estate
    Assert.Equal<Set<string * Preflight.WriteAction>>(
        set [ ("City", Preflight.Insert); ("City", Preflight.Update) ],
        incremental |> List.map (fun w -> w.Table, w.Action) |> Set.ofList)
    let wipe = Transfer.plannedTransferWrites scope EmissionMode.WipeAndLoad estate
    Assert.Contains(wipe, fun w -> w.Table = "City" && w.Action = Preflight.Delete)
    Assert.DoesNotContain(wipe, fun w -> w.Table = "Country")
