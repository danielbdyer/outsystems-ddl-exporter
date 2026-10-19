module Projection.Tests.BootstrapEmitterTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.Data
open Projection.Tests

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice ζ — BootstrapEmitter v0 (UserRemapContext = empty
// pass-through stub).
//
// Per pre-scope §2.3: Bootstrap emits "inserts for system users, default
// policies, and any remaining-by-policy kinds whose data is not in
// StaticSeeds or MigrationDependencies." Until chapter 4.2 ships
// `UserFkReflowPass`, this emitter is a structural stub — empty no-op
// artifact for every kind, T11 keyset preserved.
//
// The slice ζ MVP tests cover the structural hook (signature, T11,
// composer integration) so the chapter-4.2 / 4.3 row-source consumers
// have a fixed insertion point.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkName (s: string) : Name =
    Name.create s |> mustOk

let private mustOkEmit (r: Result<'a, EmitError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e); Unchecked.defaultof<_>

let private mkKind (name: string) : Kind =
    let kindKey = mkKey ["TestModule"; name]
    let idKey = mkKey ["TestModule"; name; "Id"]
    Kind.create kindKey (mkName name) (Fixtures.mkTableId "dbo" (sprintf "OSUSR_TEST_%s" (name.ToUpperInvariant()))) [ { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true } ]

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        IRBuilders.mkModule (mkKey ["TestModule"]) (mkName "TestModule") kinds
    IRBuilders.mkCatalog [ m ]

// ---------------------------------------------------------------------------
// UserRemapContext — chapter 4.2 slice γ shape.
//
// The slice ζ placeholder (`Map<SsKey, Map<int64, int64>>`) was refined
// at chapter 4.2 slice γ to a typed record (`{ Mapping; Unmatched;
// Diagnostics }`) living in `Projection.Core/UserRemap.fs`. The Bootstrap
// emitter still consumes the type at the same composer integration
// point; the slice ζ MVP behavior (every kind a no-op artifact under
// `UserRemapContext.empty`) is preserved.
//
// Slice-γ smart-constructor invariants are tested in
// `UserRemapContextTests.fs`; this file covers the BootstrapEmitter's
// consumption of the new shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserRemapContext.empty (slice γ shape) has empty Mapping + Unmatched + Diagnostics`` () =
    let ctx = UserRemapContext.empty
    Assert.True (Map.isEmpty ctx.Mapping)
    Assert.True (Set.isEmpty ctx.Unmatched)
    Assert.Empty ctx.Diagnostics

[<Fact>]
let ``UserRemapContext.empty is fully-mapped (no unmatched users) and unmatchedCount = 0`` () =
    Assert.True (UserRemapContext.isFullyMapped UserRemapContext.empty)
    Assert.Equal (0, UserRemapContext.unmatchedCount UserRemapContext.empty)

// ---------------------------------------------------------------------------
// BootstrapEmitter — T11 keyset + slice ζ MVP shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``BootstrapEmitter.emit produces one DataInsertScript per kind (T11 keyset)`` () =
    let catalog = mkCatalog [ mkKind "Customer"; mkKind "Order" ]
    let artifact = BootstrapEmitter.emit DataEmitOptions.defaults catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.Equal (2, Map.count map)

[<Fact>]
let ``WP6 step 2: BootstrapEmitter.emit with an empty row source renders empty per kind (renderer is real; rows arrive at hydration)`` () =
    // Post-delegation (DECISIONS 2026-06-13) the emitter is no longer a
    // stub that discards its plan — it renders whatever plan it is handed.
    // The composer-facing `emit`/`emitWithTopo` build the plan from an
    // empty row source (the per-kind hydration graft is WP6 step 4), so the
    // output is empty per kind; the REASON is "no rows yet", not "stub".
    let customer = mkKind "Customer"
    let catalog = mkCatalog [ customer ]
    let artifact = BootstrapEmitter.emit DataEmitOptions.defaults catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    Assert.Empty script.Phase1Merges
    Assert.Empty script.Phase2Updates
    Assert.Equal<string> ("", script.Rendered)

[<Fact>]
let ``WP6 step 2: BootstrapEmitter.emitFromPlan delegates to the static-seeds renderer (byte-identical for the same plan)`` () =
    // The delegation witness: for the SAME DataLoadPlan, Bootstrap's output
    // equals StaticSeedsEmitter's output (both realize the same algebra via
    // StaticSeedsEmitter.emitFromPlanWith — A40), and a populated plan
    // renders a real MERGE (no longer the empty stub).
    let idKey = mkKey ["TestModule"; "Customer"; "Id"]
    let nameKey = mkKey ["TestModule"; "Customer"; "Name"]
    let customer : Kind =
        Kind.create (mkKey ["TestModule"; "Customer"]) (mkName "Customer")
            (Fixtures.mkTableId "dbo" "OSUSR_TEST_CUSTOMER")
            [ { Attribute.create idKey (mkName "Id") Integer with
                  Column = ColumnRealization.create ("ID") (false) |> Result.value
                  IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create nameKey (mkName "Name") Text with
                  Column = ColumnRealization.create ("NAME") (false) |> Result.value
                  IsMandatory = true } ]
    let catalog = mkCatalog [ customer ]
    let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
    let rawRows : Map<SsKey, StaticRow list> =
        Map.ofList
            [ customer.SsKey,
              [ { Identifier = mkKey ["TestModule"; "Customer"; "Row"; "1"]
                  Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "Name", "Acme" ] } ] ]
    let plan = DataLoadPlan.build catalog topo rawRows SurrogateRemapContext.empty
    let boot = BootstrapEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan |> mustOkEmit |> ArtifactByKind.toMap
    let stat = StaticSeedsEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan |> mustOkEmit |> ArtifactByKind.toMap
    Assert.Equal<Map<SsKey, DataInsertScript>> (stat, boot)
    let script = Map.find customer.SsKey boot
    Assert.NotEmpty script.Phase1Merges
    Assert.Contains("MERGE", script.Rendered)

[<Fact>]
let ``T1: BootstrapEmitter.emit is byte-deterministic across repeat invocations`` () =
    let catalog = mkCatalog [ mkKind "Customer" ]
    let r1 = BootstrapEmitter.emit DataEmitOptions.defaults catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let r2 = BootstrapEmitter.emit DataEmitOptions.defaults catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1
    let s2 = ArtifactByKind.toMap r2
    Assert.Equal<Map<SsKey, DataInsertScript>> (s1, s2)
