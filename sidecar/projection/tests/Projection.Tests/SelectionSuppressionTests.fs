module Projection.Tests.SelectionSuppressionTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// The lifecycle-selection pass (the data-sink chapter, S9): ONE drop
// semantic (`ModuleFilter.lifecycleFilter`), two channels — `apply` drops
// silently on the Result channel exactly as it always has; the registered
// pass emits one `Removed` lineage event per suppressed kind with the axis
// that fired. These pin the equality of the two channels, the attribution
// order (system wins over inactive — the historical arm order), the
// module-drop semantics, and the identity default.

let private nm (s: string) : Name =
    match Name.create s with Ok n -> n | Error es -> failwithf "bad name %s: %A" s es

let private kindIn (moduleTag: string) (kindName: string) (active: bool) (system: bool) : Kind =
    { customer with
        SsKey = kindKey [ moduleTag; kindName ]
        Name = nm kindName
        Physical = mkTableId "dbo" (sprintf "OSUSR_%s_%s" moduleTag (kindName.ToUpperInvariant()))
        // Attribute identities re-keyed per kind — the fixture kind's
        // attribute SsKeys must not collide across the synthetic kinds.
        Attributes =
            customer.Attributes
            |> List.mapi (fun i a -> { a with SsKey = attrKey [ moduleTag; kindName; string i ] })
        References = []
        Indexes = []
        IsActive = active
        Modality = if system then [ ModalityMark.SystemOwned ] else [] }

let private moduleOf (moduleName: string) (active: bool) (kinds: Kind list) : Module =
    { mkModule (modKey moduleName) (nm moduleName) kinds with IsActive = active }

let private runPass (includeSystem: bool) (includeInactive: bool) (c: Catalog) =
    (SelectionSuppression.registered
        { IncludeSystemModules = includeSystem
          IncludeInactiveModules = includeInactive }).Run c

let private mixedCatalog () =
    mkCatalog
        [ moduleOf "AppCore" true
            [ kindIn "APP" "Order" true false
              kindIn "APP" "Archive" false false ]
          moduleOf "Retired" false
            [ kindIn "RET" "Legacy" true false ]
          moduleOf "SystemUsers" true
            [ kindIn "SYS" "User" true true ] ]

let private removedReasons (trail: LineageEvent list) : RemovalReason list =
    trail |> List.choose (fun e -> match e.TransformKind with Removed r -> Some r | _ -> None)

[<Fact>]
let ``one drop semantic, two channels: the pass equals apply's lifecycle arm and names every suppression`` () =
    let c = mixedCatalog ()
    // Channel 1 — the silent Result channel: apply with ONLY the
    // lifecycle axes restricted (no names, no entity filters).
    let opts = { ModuleFilter.empty with IncludeSystemModules = false; IncludeInactiveModules = false }
    let viaApply =
        match ModuleFilter.apply opts c with
        | Ok filtered -> filtered
        | Error es -> failwithf "apply refused: %A" es
    // Channel 2 — the lineage channel: the registered pass. NOTE: apply
    // also cross-scope-prunes at its exit; this catalog carries no
    // cross-module references, so the two channels' values are equal.
    let viaPass = runPass false false c
    Assert.Equal<Catalog>(viaApply, LineageDiagnostics.payload viaPass)
    // Every suppression is NAMED: the inactive kind (Archive), the
    // inactive module's kind (Legacy), and the system module's kind
    // (User) each carry their axis.
    let reasons = removedReasons viaPass.Trail
    Assert.Equal(3, List.length reasons)
    Assert.Equal(2, reasons |> List.filter (fun r -> r = LifecycleInactive) |> List.length)
    Assert.Equal(1, reasons |> List.filter (fun r -> r = SystemOwnedModule) |> List.length)

[<Fact>]
let ``attribution: a system-owned inactive module names SystemOwnedModule (system wins — the historical arm order)`` () =
    let c =
        mkCatalog
            [ moduleOf "SysRetired" false [ kindIn "SR" "Ghost" false true ]
              moduleOf "AppCore" true [ kindIn "APP" "Order" true false ] ]
    let viaPass = runPass false false c
    Assert.Equal<RemovalReason list>([ SystemOwnedModule ], removedReasons viaPass.Trail)

[<Fact>]
let ``a module emptied by kind-grain suppression is dropped whole (the historical Step-2 semantics)`` () =
    let c =
        mkCatalog
            [ moduleOf "AllInactive" true [ kindIn "AI" "A" false false; kindIn "AI" "B" false false ]
              moduleOf "AppCore" true [ kindIn "APP" "Order" true false ] ]
    let viaPass = runPass true false c
    let survivor = LineageDiagnostics.payload viaPass
    Assert.Equal<string list>([ "AppCore" ], survivor.Modules |> List.map (fun m -> Name.value m.Name))
    Assert.Equal(2, removedReasons viaPass.Trail |> List.filter (fun r -> r = LifecycleInactive) |> List.length)

[<Fact>]
let ``the identity axes suppress nothing and emit nothing (the registered chain default is byte-identical)`` () =
    let c = mixedCatalog ()
    let viaPass = (SelectionSuppression.registered SelectionSuppression.identity).Run c
    Assert.Equal<Catalog>(c, LineageDiagnostics.payload viaPass)
    Assert.Empty viaPass.Trail
