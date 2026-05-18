module Projection.Tests.UserFkReflowIntegrationTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.Data

// ---------------------------------------------------------------------------
// Chapter 4.2 slice η — UserRemapContext wired into MigrationDependencies
// Emitter at row-emission time. User-FK column values are rewritten from
// source-environment ids to target-environment ids; rows whose source
// users are unmatched are skipped (V1 "diagnostic + skip" parity per
// `UserMatchingResult.cs` + `EmitArtifactsStep.cs`).
//
// Plus the chapter-signature multi-environment commutativity property:
// same `(sourceUsers, ByEmail)` against four distinct target populations
// yields four UserRemapContext values whose Mapping source-keysets agree
// (modulo per-environment unmatched). T11 specialization for sibling Π's
// commuting on shared UserRemapContext.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mustOkEmit (r: Result<'a, EmitError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e); Unchecked.defaultof<_>

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkName (s: string) : Name = Name.create s |> mustOk

let private mkEmail (raw: string) : Email = Email.create raw |> mustOk

let private mkSourceUser (id: int) (sskeyParts: string list) (email: string option) : UserAttributes<SourceUserId> =
    UserAttributes.create
        (SourceUserId.ofInt id)
        (mkKey sskeyParts)
        (email |> Option.map mkEmail)

let private mkTargetUser (id: int) (sskeyParts: string list) (email: string option) : UserAttributes<TargetUserId> =
    UserAttributes.create
        (TargetUserId.ofInt id)
        (mkKey sskeyParts)
        (email |> Option.map mkEmail)

// ---------------------------------------------------------------------------
// Fixture: an Order kind with an explicit User-FK reference (CreatedBy).
// ---------------------------------------------------------------------------

let private mkOrderKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Order"]
    let idKey = mkKey ["TestModule"; "Order"; "Id"]
    let createdByKey = mkKey ["TestModule"; "Order"; "CreatedBy"]
    let createdByRefKey = mkKey ["TestModule"; "Order"; "RefCreatedBy"]
    {
        SsKey    = kindKey
        Name     = mkName "Order"
        Origin   = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "OSUSR_TEST_ORDER"; Catalog = None }
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = { ColumnName = "ID";        IsNullable = false }; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create createdByKey (mkName "CreatedBy") Integer with Column = { ColumnName = "CREATEDBY"; IsNullable = false }; IsMandatory = true }
            ]
        References =
            [ { Reference.create
                  createdByRefKey
                  (mkName "CreatedByFk")
                  createdByKey
                  (mkKey ["IDM"; "User"])  // platform User kind (synthetic)
                with IsUserFk = true } ]  // slice ζ User-FK marker
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        IRBuilders.mkModule (mkKey ["TestModule"]) (mkName "TestModule") kinds
    IRBuilders.mkCatalog [ m ]

let private orderRowKey (id: int) : SsKey =
    mkKey ["TestModule"; "Order"; "Row"; sprintf "%d" id]

let private migrationRow (orderId: int) (createdBySourceUserId: int) : MigrationDependencyRow =
    let kindKey = mkKey ["TestModule"; "Order"]
    {
        KindKey    = kindKey
        Identifier = orderRowKey orderId
        Values =
            Map.ofList
                [ mkName "Id",        sprintf "%d" orderId
                  mkName "CreatedBy", sprintf "%d" createdBySourceUserId ]
    }

let private normWs (s: string) : string =
    System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim()

// ---------------------------------------------------------------------------
// Slice η: User-FK rewrite on matched source users.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice η: matched source User-FK value is rewritten to target-environment id`` () =
    let order = mkOrderKind ()
    let catalog = mkCatalog [ order ]
    let migration =
        { Rows = [ migrationRow 1 7 ] }  // Order #1 created by source user 7
    let userRemap =
        UserRemapContext.create
            (Map.ofList [ SourceUserId.ofInt 7, TargetUserId.ofInt 700 ])
            Set.empty
            []
        |> mustOk
    let artifact =
        (let topo' = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value in
             MigrationDependenciesEmitter.emitWithTopo topo' catalog Profile.empty migration userRemap)
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find order.SsKey
    Assert.Equal (1, List.length script.Phase1Merges)
    // The rendered MERGE carries the target id 700, not source 7.
    let r = normWs script.Rendered
    Assert.Contains ("(1, 700)", r)
    Assert.DoesNotContain ("(1, 7)", r)

[<Fact>]
let ``Slice η: unmatched source User-FK value drops the row (V1 diagnostic+skip parity)`` () =
    let order = mkOrderKind ()
    let catalog = mkCatalog [ order ]
    let migration =
        { Rows =
            [ migrationRow 1 7        // matched
              migrationRow 2 99 ] }   // unmatched (no remap entry for 99)
    let userRemap =
        UserRemapContext.create
            (Map.ofList [ SourceUserId.ofInt 7, TargetUserId.ofInt 700 ])
            (Set.ofList [ SourceUserId.ofInt 99 ])
            [ NoFallbackConfigured (SourceUserId.ofInt 99) ]
        |> mustOk
    let artifact =
        (let topo' = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value in
             MigrationDependenciesEmitter.emitWithTopo topo' catalog Profile.empty migration userRemap)
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find order.SsKey
    // Only the matched row survives; the unmatched row is silently
    // dropped (its diagnostic was emitted by UserFkReflowPass).
    Assert.Equal (1, List.length script.Phase1Merges)
    let r = normWs script.Rendered
    Assert.Contains ("(1, 700)", r)
    Assert.DoesNotContain ("(2,", r)

[<Fact>]
let ``Slice η: kind with no User-FK references passes through unrewritten`` () =
    // No IsUserFk references → User-FK rewrite is a no-op; rows
    // pass through with their source values.
    let kindKey = mkKey ["TestModule"; "Country"]
    let idKey = mkKey ["TestModule"; "Country"; "Id"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    let country : Kind =
        { SsKey    = kindKey
          Name     = mkName "Country"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "OSUSR_TEST_COUNTRY"; Catalog = None }
          Attributes =
              [ { Attribute.create idKey (mkName "Id") Integer with Column = { ColumnName = "ID";    IsNullable = false }; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create labelKey (mkName "Label") Text with Column = { ColumnName = "LABEL"; IsNullable = false }; IsMandatory = true } ]
          References = []  // no User-FK
          Indexes    = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let catalog = mkCatalog [ country ]
    let migration =
        { Rows =
            [ { KindKey    = kindKey
                Identifier = mkKey ["TestModule"; "Country"; "Row"; "1"]
                Values =
                    Map.ofList
                        [ mkName "Id",    "1"
                          mkName "Label", "United States" ] } ] }
    // Even with a populated UserRemap, no rewrite happens (no User-FKs).
    let userRemap =
        UserRemapContext.create
            (Map.ofList [ SourceUserId.ofInt 7, TargetUserId.ofInt 700 ])
            Set.empty
            []
        |> mustOk
    let artifact =
        (let topo' = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value in
             MigrationDependenciesEmitter.emitWithTopo topo' catalog Profile.empty migration userRemap)
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Equal (1, List.length script.Phase1Merges)
    let r = normWs script.Rendered
    Assert.Contains ("N'United States'", r)

// ---------------------------------------------------------------------------
// Slice η: composer-level integration.
// ---------------------------------------------------------------------------

let private policyWith (composition: DataComposition) : Policy =
    { Policy.empty with
        Emission =
            { Policy.empty.Emission with
                EmitData = true
                DataComposition = composition } }

[<Fact>]
let ``Slice η: DataEmissionComposer.composeFull threads UserRemapContext to MigrationDependenciesEmitter`` () =
    let order = mkOrderKind ()
    let catalog = mkCatalog [ order ]
    let migration = { Rows = [ migrationRow 1 7 ] }
    let userRemap =
        UserRemapContext.create
            (Map.ofList [ SourceUserId.ofInt 7, TargetUserId.ofInt 700 ])
            Set.empty
            []
        |> mustOk
    let artifact =
        DataEmissionComposer.composeFull
            (policyWith AllRemaining) catalog Profile.empty migration userRemap
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find order.SsKey
    let r = normWs script.Rendered
    Assert.Contains ("(1, 700)", r)

// ---------------------------------------------------------------------------
// Multi-environment commutativity property (the chapter signature
// deliverable per CHAPTER_4_2_OPEN.md axis 8 + pre-scope §7 slice 7).
//
// Same source population + ByEmail strategy against four distinct target
// populations yields four UserRemapContext values; the source-keyset of
// Mapping ∪ Unmatched agrees across all four. T11 specialization for
// sibling Π's commuting on shared UserRemapContext.
// ---------------------------------------------------------------------------

[<Fact>]
let ``η chapter signature: multi-environment commutativity holds under ByEmail across four target populations`` () =
    let sources =
        UserPopulation.create
            [ mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
              mkSourceUser 2 ["U"; "S2"] (Some "bob@example.com")
              mkSourceUser 3 ["U"; "S3"] (Some "carol@example.com")
              mkSourceUser 4 ["U"; "S4"] None  // no email; always unmatched under ByEmail
            ]
    // Four distinct target populations — Dev, QA, UAT, Prod-shaped.
    let dev =
        UserPopulation.create
            [ mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
              mkTargetUser 200 ["U"; "T200"] (Some "bob@example.com") ]
    let qa =
        UserPopulation.create
            [ mkTargetUser 110 ["U"; "T110"] (Some "alice@example.com")
              mkTargetUser 210 ["U"; "T210"] (Some "bob@example.com")
              mkTargetUser 310 ["U"; "T310"] (Some "carol@example.com") ]
    let uat =
        UserPopulation.create
            [ mkTargetUser 120 ["U"; "T120"] (Some "alice@example.com") ]
    let prod =
        UserPopulation.create
            [ mkTargetUser 130 ["U"; "T130"] (Some "alice@example.com")
              mkTargetUser 230 ["U"; "T230"] (Some "bob@example.com")
              mkTargetUser 330 ["U"; "T330"] (Some "carol@example.com")
              mkTargetUser 430 ["U"; "T430"] (Some "dave@example.com") ]
    let targets = [ dev; qa; uat; prod ]
    let contexts =
        targets
        |> List.map (fun t -> (UserFkReflowPass.discover sources t ByEmail).Value.Value)
    // Property 1: every UserRemapContext sees the same source-keyset
    // (Mapping ∪ Unmatched) — equal to the source population's id-set.
    let sourceKeyset =
        sources.Users
        |> List.map (fun u -> u.Id)
        |> Set.ofList
    for ctx in contexts do
        let mappingKeys =
            ctx.Mapping |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let coveredKeys = Set.union mappingKeys ctx.Unmatched
        Assert.Equal<Set<SourceUserId>> (sourceKeyset, coveredKeys)
    // Property 2: each context's Mapping is non-overlapping with its
    // Unmatched (UserRemapContext smart-constructor invariant; verifies
    // that the discovery pass produces valid UserRemapContext values
    // regardless of target-population shape).
    for ctx in contexts do
        let mappingKeys =
            ctx.Mapping |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        Assert.True (Set.isEmpty (Set.intersect mappingKeys ctx.Unmatched))
    // Property 3: the source user with no email is unmatched in every
    // environment under ByEmail (orientation-invariant; the source
    // condition determines unmatched, not the target).
    let s4 = SourceUserId.ofInt 4
    for ctx in contexts do
        Assert.True (Set.contains s4 ctx.Unmatched)
    // Property 4: per-environment differences live entirely in the
    // TargetUserId values (the source-side of each Mapping entry is
    // the same; only the target id differs). Verify by checking that
    // every Mapping key (matched source) maps to a target whose
    // integer value belongs to that environment's target population.
    let assertTargetsBelong (ctx: UserRemapContext) (population: UserPopulation<TargetUserId>) =
        let validTargets =
            population.Users
            |> List.map (fun u -> u.Id)
            |> Set.ofList
        for (_, target) in Map.toList ctx.Mapping do
            Assert.Contains (target, validTargets)
    List.zip contexts targets
    |> List.iter (fun (ctx, pop) -> assertTargetsBelong ctx pop)
