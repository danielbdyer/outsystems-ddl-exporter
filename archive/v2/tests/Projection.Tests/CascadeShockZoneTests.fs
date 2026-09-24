module Projection.Tests.CascadeShockZoneTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// H-039 — Cascade shock zone detection
// ---------------------------------------------------------------------------
//
// The pass detects cascade shock zones by DFS along FK edges in the
// FK direction (source-with-FK → target-being-referenced). Each step
// must be classified as `EdgeStrength.Cascade`. A zone with |Reachable|
// ≥ 3 transitive cascade dependencies is flagged.
//
// To trigger the detection, we build a 4-deep linear cascade chain:
//   D → C → B → A   (each child has an FK with OnDelete=Cascade)
// DFS from D reaches {C, B, A} — size 3 ≥ 3 ⇒ shock zone for D.
// ---------------------------------------------------------------------------

let private synthKey (ns: string) (key: string) : SsKey =
    SsKey.synthesized ns key |> Result.value

let private kA = synthKey "Mod" "A"
let private kB = synthKey "Mod" "B"
let private kC = synthKey "Mod" "C"
let private kD = synthKey "Mod" "D"

let private physical (table: string) : PhysicalRealization =
    TableId.create "dbo" table |> Result.value

let private mkAttr (ownerRoot: string) (name: string) (isNullable: bool) : Attribute =
    let key = synthKey ownerRoot name
    { Attribute.create key (Name.create name |> Result.value) PrimitiveType.Integer with
        Column = ColumnRealization.create (name) (isNullable) |> Result.value }

let private mkRefCascade (ownerKey: SsKey) (targetKey: SsKey) : Reference =
    let ownerRoot = SsKey.rootOriginal ownerKey
    let attrKey = synthKey ownerRoot "fk"
    let refKey  = synthKey ownerRoot "ref"
    { Reference.create refKey (Name.create "fk" |> Result.value) attrKey targetKey with
        OnDelete        = Cascade
        ConstraintState = ConstraintState.TrustedConstraint }

// Linear cascade chain: D → C → B → A. From D's perspective, deleting
// any ancestor in {C, B, A} cascade-kills D. |Reachable from D| = 3.
let private buildCascadeChainCatalog () : Catalog =
    let a =
        Kind.create
            kA
            (Name.create "A" |> Result.value)
            (physical "A")
            [ mkAttr "A" "Id" false |> fun a -> { a with IsPrimaryKey = true } ]
    let b =
        { Kind.create
              kB
              (Name.create "B" |> Result.value)
              (physical "B")
              [ mkAttr "B" "Id" false |> fun a -> { a with IsPrimaryKey = true }
                mkAttr "B" "fk" false ]
            with References = [ mkRefCascade kB kA ] }
    let c =
        { Kind.create
              kC
              (Name.create "C" |> Result.value)
              (physical "C")
              [ mkAttr "C" "Id" false |> fun a -> { a with IsPrimaryKey = true }
                mkAttr "C" "fk" false ]
            with References = [ mkRefCascade kC kB ] }
    let d =
        { Kind.create
              kD
              (Name.create "D" |> Result.value)
              (physical "D")
              [ mkAttr "D" "Id" false |> fun a -> { a with IsPrimaryKey = true }
                mkAttr "D" "fk" false ]
            with References = [ mkRefCascade kD kC ] }
    mkCatalog
        [ mkModule (synthKey "Mod" "Mod") (Name.create "Mod" |> Result.value)
            [ a; b; c; d ] ]

[<Fact>]
let ``empty catalog reports no shock zones`` () =
    let catalog = mkCatalog []
    let t = TopologicalOrder.empty
    let result =
        TopologicalOrderPass.runCascadeShockZones catalog t
        |> LineageDiagnostics.payload
    Assert.Empty(result)

[<Fact>]
let ``cascade chain with reachable ≥ 3 produces a shock zone`` () =
    let catalog = buildCascadeChainCatalog ()
    let t = TopologicalOrderPass.registered.Run catalog |> LineageDiagnostics.payload
    let result =
        TopologicalOrderPass.runCascadeShockZones catalog t
        |> LineageDiagnostics.payload
    Assert.NotEmpty(result)

[<Fact>]
let ``shock zone diagnostic has Warning severity and correct code`` () =
    let catalog = buildCascadeChainCatalog ()
    let t = TopologicalOrderPass.registered.Run catalog |> LineageDiagnostics.payload
    let diagnostics_ =
        TopologicalOrderPass.runCascadeShockZones catalog t
        |> LineageDiagnostics.entries
    Assert.NotEmpty(diagnostics_)
    for d in diagnostics_ do
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
        Assert.Equal("topology.cascadeShock", d.Code)
