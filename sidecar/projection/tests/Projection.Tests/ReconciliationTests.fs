module Projection.Tests.ReconciliationTests

open Xunit
open Projection.Core

// Pure tests for Slice C′ core: the per-kind reconciliation engine (the
// generalization of UserFkReflowPass.discover) + the connection apparatus.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_RECON" parts |> mustOk
let private mkName (s: string) : Name = Name.create s |> mustOk

/// A row with an "ID" surrogate column plus extra columns.
let private row (pk: string) (extra: (string * string) list) : StaticRow =
    { Identifier = mkKey [ "row"; pk ]
      Values     = ("ID", pk) :: extra |> List.map (fun (k, v) -> mkName k, v) |> Map.ofList }

let private userKey = mkKey [ "User" ]
let private idCol = mkName "ID"
let private byEmail = ReconciliationStrategy.MatchByColumn (mkName "Email")

// -- reconciliation engine -------------------------------------------------

[<Fact>]
let ``reconcileKind matches Source to pre-existing Sink by column (the Dev->UAT User re-key)`` () =
    // Dev user 280 (alice) is UAT user 18; Dev 281 (bob) is UAT 19.
    let source = [ row "280" [ "Email", "alice@x" ]; row "281" [ "Email", "bob@x" ] ]
    let sink   = [ row "18" [ "Email", "alice@x" ]; row "19" [ "Email", "bob@x" ]; row "20" [ "Email", "carol@x" ] ]
    let result = Reconciliation.reconcileKind userKey idCol byEmail source sink
    Assert.Empty result.Unmatched
    Assert.Equal(Some (AssignedKey.ofString "18"), SurrogateRemapContext.tryFindAssigned userKey (SourceKey.ofString "280") result.Remap)
    Assert.Equal(Some (AssignedKey.ofString "19"), SurrogateRemapContext.tryFindAssigned userKey (SourceKey.ofString "281") result.Remap)

[<Fact>]
let ``reconcileKind skips-and-diagnoses a Source identity absent from the Sink`` () =
    let source = [ row "280" [ "Email", "alice@x" ]; row "999" [ "Email", "ghost@x" ] ]
    let sink   = [ row "18" [ "Email", "alice@x" ] ]
    let result = Reconciliation.reconcileKind userKey idCol byEmail source sink
    Assert.Equal<(SsKey * SourceKey) list>([ userKey, SourceKey.ofString "999" ], result.Unmatched)
    Assert.Equal(Some (AssignedKey.ofString "18"), SurrogateRemapContext.tryFindAssigned userKey (SourceKey.ofString "280") result.Remap)

[<Fact>]
let ``reconcileKind ManualOverride maps explicit Source->Sink; sources outside the map fall through`` () =
    let source = [ row "280" []; row "281" [] ]
    let overrides = Map.ofList [ SourceKey.ofString "280", AssignedKey.ofString "18" ]
    let result = Reconciliation.reconcileKind userKey idCol (ReconciliationStrategy.ManualOverride overrides) source []
    Assert.Equal(Some (AssignedKey.ofString "18"), SurrogateRemapContext.tryFindAssigned userKey (SourceKey.ofString "280") result.Remap)
    Assert.Equal<(SsKey * SourceKey) list>([ userKey, SourceKey.ofString "281" ], result.Unmatched)

// -- FK re-pointing through the remap --------------------------------------

/// An Order kind with a nullable USER_ID FK to the User kind, so a Transfer
/// can re-point USER_ID from the Source surrogate to the reconciled Sink one.
let private orderKey = mkKey [ "Order" ]
let private userIdKey = mkKey [ "Order"; "USER_ID" ]
let private orderUserRef =
    { Reference.create (mkKey [ "Order"; "UserRef" ]) (mkName "UserRef") userIdKey userKey with
        HasDbConstraint = true }
let private orderKind : Kind =
    { Kind.create orderKey (mkName "Order") { Schema = "dbo"; Table = "OSUSR_ORDER"; Catalog = None }
        [ { Attribute.create (mkKey [ "Order"; "ID" ]) (mkName "ID") Integer with
              Column = { ColumnName = "ID"; IsNullable = false }; IsPrimaryKey = true; IsMandatory = true }
          { Attribute.create userIdKey (mkName "USER_ID") Integer with
              Column = { ColumnName = "USER_ID"; IsNullable = true } } ]
      with References = [ orderUserRef ]; Indexes = []; ColumnChecks = [] }

[<Fact>]
let ``fkColumnsTargeting selects only FK columns whose target is in the remap set`` () =
    Assert.Equal<Map<Name, SsKey>>(
        Map.ofList [ mkName "USER_ID", userKey ],
        SurrogateRemap.fkColumnsTargeting (Set.singleton userKey) orderKind)
    Assert.True(Map.isEmpty (SurrogateRemap.fkColumnsTargeting Set.empty orderKind))

[<Fact>]
let ``remapRowFks re-points a targeted FK value through the matched assigned surrogate`` () =
    let fkTargets = Map.ofList [ mkName "USER_ID", userKey ]
    let remap = SurrogateRemapContext.capture userKey (SourceKey.ofString "280") (AssignedKey.ofString "18") SurrogateRemapContext.empty |> mustOk
    let result = SurrogateRemap.remapRowFks fkTargets remap [ row "10" [ "USER_ID", "280" ] ]
    Assert.Empty result.Skipped
    Assert.Equal(Some "18", Map.tryFind (mkName "USER_ID") result.Rows.Head.Values)

[<Fact>]
let ``remapRowFks drops a row whose targeted FK has no assigned counterpart (skip-and-diagnose)`` () =
    let fkTargets = Map.ofList [ mkName "USER_ID", userKey ]
    let result = SurrogateRemap.remapRowFks fkTargets SurrogateRemapContext.empty [ row "10" [ "USER_ID", "999" ] ]
    Assert.Empty result.Rows
    let s = List.exactlyOne result.Skipped
    Assert.Equal(mkName "USER_ID", s.Column)
    Assert.Equal(userKey, s.Target)
    Assert.Equal(SourceKey.ofString "999", s.UnresolvedSource)

[<Fact>]
let ``remapRowFks leaves a NULL FK untouched and keeps the row`` () =
    let fkTargets = Map.ofList [ mkName "USER_ID", userKey ]
    let result = SurrogateRemap.remapRowFks fkTargets SurrogateRemapContext.empty [ row "10" [ "USER_ID", "" ] ]
    Assert.Empty result.Skipped
    Assert.Equal(1, result.Rows.Length)

// -- connection apparatus --------------------------------------------------

let private srcSub = { Environment = Environment.Dev; Role = SubstrateRole.Source; ConnectionRef = ConnectionRef.EnvVar "DEV_CONN" }
let private sinkSub = { Environment = Environment.Uat; Role = SubstrateRole.Sink; ConnectionRef = ConnectionRef.EnvVar "UAT_CONN" }

[<Fact>]
let ``TransferConnections profiles the Sink only when reconciling (Sink is not write-only)`` () =
    let preserve = TransferConnections.create srcSub sinkSub false |> mustOk
    Assert.Equal(1, preserve.ProfiledForIdentity.Length)
    let reconcile = TransferConnections.create srcSub sinkSub true |> mustOk
    Assert.Equal(2, reconcile.ProfiledForIdentity.Length)

[<Fact>]
let ``TransferConnections.create rejects a substrate carrying the wrong role`` () =
    let badSource = { srcSub with Role = SubstrateRole.Sink }
    match TransferConnections.create badSource sinkSub false with
    | Ok _     -> Assert.Fail "expected role-mismatch rejection"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connections.roleMismatch")

[<Fact>]
let ``Environment.name renders the four standard environments`` () =
    Assert.Equal("DEV", Environment.name Environment.Dev)
    Assert.Equal("UAT", Environment.name Environment.Uat)
    Assert.Equal("Corp", Environment.name (Environment.Named "Corp"))
