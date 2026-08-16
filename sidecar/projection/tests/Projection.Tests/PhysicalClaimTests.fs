module Projection.Tests.PhysicalClaimTests

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.OssysSql

// Physical-claim adjudication (the data-sink chapter, S11): the ladder is
// total and closed; a sole live claim adopts with its tombstones as
// outranked lineage (the cutover's happy path); two or more live claims are
// CONTESTED, ALWAYS — a live writer is never silently outranked — with the
// rivals ladder-ordered so the finding leads with the recommendation; only
// tombstones ⇒ TombstoneOnly; empty ⇒ Unclaimed. Plus the journal-assembly
// facts: claims assemble from the WITNESSED edition (tombstones included —
// the sink is total), the extension flag reads the module's case-insensitive
// 'Extension' kind, and each claim carries its first-witnessed sync.

/// align-III.1: expected-ordinal literal for asserts (patterns can't call functions).
let private ord (n: int) : SyncOrdinal =
    match SyncOrdinal.create n with
    | Ok o -> o
    | Error m -> failwith m

let private claim (id: int) (name: string) (active: bool) (ext: bool) (sync: int) : PhysicalClaimRules.PhysicalClaim =
    { EntityId = id
      EntityKey = None
      EntityName = name
      ModuleName = if ext then "FulfillmentExtension" else "Fulfillment"
      IsActive = active
      IsExternalRegistration = ext
      FirstWitnessedSync = PhysicalClaimRules.FirstWitnessedSync.ofAppearance (Some (ord sync)) }

let private setOf (claims: PhysicalClaimRules.PhysicalClaim list) : PhysicalClaimRules.ClaimSet =
    { Schema = "dbo"; Table = "OSUSR_FUL_T"; Claims = claims }

[<Fact>]
let ``the ladder is total and closed: empty ⇒ Unclaimed; tombstones only ⇒ TombstoneOnly`` () =
    match PhysicalClaimRules.adjudicate (setOf []) with
    | PhysicalClaimRules.PhysicalClaimOutcome.Unclaimed -> ()
    | other -> Assert.Fail (sprintf "expected Unclaimed, got %A" other)
    match PhysicalClaimRules.adjudicate (setOf [ claim 1 "A" false false 1; claim 2 "B" false true 2 ]) with
    | PhysicalClaimRules.PhysicalClaimOutcome.TombstoneOnly ts -> Assert.Equal(2, List.length ts)
    | other -> Assert.Fail (sprintf "expected TombstoneOnly, got %A" other)

[<Fact>]
let ``a sole live claim adopts; its tombstones ride as outranked lineage (the cutover's happy path)`` () =
    match PhysicalClaimRules.adjudicate (setOf [ claim 8002 "Shipment" false false 1; claim 9002 "Shipment" true true 1 ]) with
    | PhysicalClaimRules.PhysicalClaimOutcome.Adopted (winner, outranked) ->
        Assert.Equal(9002, winner.EntityId)
        Assert.Equal<int list>([ 8002 ], outranked |> List.map (fun c -> c.EntityId))
    | other -> Assert.Fail (sprintf "expected the extension re-registration adopted, got %A" other)

[<Fact>]
let ``two live claims are Contested ALWAYS — a live writer is never silently outranked`` () =
    // Cross-tier (eSpace + extension), cross-sync — every ordering signal
    // present, and STILL contested: the signals rank the recommendation,
    // they never adopt over a live rival.
    match PhysicalClaimRules.adjudicate (setOf [ claim 9003 "Carrier" true true 3; claim 8003 "Carrier" true false 1 ]) with
    | PhysicalClaimRules.PhysicalClaimOutcome.Contested rivals ->
        // Ladder order: the eSpace claim leads (tier), regardless of sync.
        Assert.Equal<int list>([ 8003; 9003 ], rivals |> List.map (fun c -> c.EntityId))
    | other -> Assert.Fail (sprintf "expected Contested, got %A" other)

[<Property(MaxTest = 60)>]
let ``property: adoption requires a SOLE live claim — two-plus live is Contested for every shape`` (activeFlags: bool list) =
    let flags = if List.isEmpty activeFlags then [ true ] else activeFlags
    let claims = flags |> List.mapi (fun i active -> claim (100 + i) (sprintf "E%d" i) active (i % 2 = 0) (1 + i % 3))
    let liveCount = flags |> List.filter id |> List.length
    match PhysicalClaimRules.adjudicate (setOf claims) with
    | PhysicalClaimRules.PhysicalClaimOutcome.Adopted _ -> liveCount = 1
    | PhysicalClaimRules.PhysicalClaimOutcome.Contested rivals -> liveCount >= 2 && List.length rivals = liveCount
    | PhysicalClaimRules.PhysicalClaimOutcome.TombstoneOnly ts -> liveCount = 0 && List.length ts = List.length claims
    | PhysicalClaimRules.PhysicalClaimOutcome.Unclaimed -> false

[<Fact>]
let ``contested rivals rank tier-first, then the LATER first-witnessed sync, then row id`` () =
    let rivals =
        [ claim 5 "B" true true 9    // extension, late
          claim 3 "A" true false 1   // espace, early
          claim 4 "A2" true false 7  // espace, late — the recommendation
          claim 6 "B2" true true 9 ] // extension, late (id after 5)
    match PhysicalClaimRules.adjudicate (setOf rivals) with
    | PhysicalClaimRules.PhysicalClaimOutcome.Contested ordered ->
        Assert.Equal<int list>([ 4; 3; 5; 6 ], ordered |> List.map (fun c -> c.EntityId))
    | other -> Assert.Fail (sprintf "expected Contested, got %A" other)

// -- the cross-cutover identity correspondence (S14) --------------------------
// The proposer is structurally incapable of adopting: no catalog reaches
// it and no SsKey leaves it — the proposal carries the two CLAIMS (with
// their native keys as evidence) for a DECIDE finding the operator rules.

[<Fact>]
let ``a sole live claim over tombstones proposes the correspondence — From is the LATEST-witnessed tombstone`` () =
    let s = setOf [ claim 8002 "Shipment" false false 1   // early tombstone
                    claim 8005 "Shipment" false false 4   // the cutover-nearest tombstone
                    claim 9002 "Shipment" true true 5 ]   // the continuing registration
    match PhysicalClaimRules.proposeCorrespondence s (PhysicalClaimRules.adjudicate s) with
    | Some p ->
        Assert.Equal(9002, p.To.EntityId)
        Assert.Equal(8005, p.From.EntityId)
        Assert.True(p.SameName)
        Assert.Equal("OSUSR_FUL_T", p.Table)
    | None -> Assert.Fail "expected a correspondence proposal"

[<Fact>]
let ``a renamed continuation still proposes (the table is the carrier); the name disagreement is stated`` () =
    let s = setOf [ claim 8002 "Shipment" false false 1; claim 9002 "ShipmentV2" true true 2 ]
    match PhysicalClaimRules.proposeCorrespondence s (PhysicalClaimRules.adjudicate s) with
    | Some p ->
        Assert.False(p.SameName)
        let clauses = PhysicalClaimRules.correspondenceClauses p |> Map.ofList
        Assert.Equal("false", clauses.["sameName"])
        Assert.Contains("tombstone", clauses.["from"])
    | None -> Assert.Fail "expected a proposal for the renamed continuation"

[<Property(MaxTest = 60)>]
let ``property: a proposal exists IFF the adjudication is Adopted over at least one tombstone — no other shape proposes`` (activeFlags: bool list) =
    let flags = if List.isEmpty activeFlags then [ true ] else activeFlags
    let claims = flags |> List.mapi (fun i active -> claim (100 + i) (sprintf "E%d" i) active (i % 2 = 0) (1 + i % 3))
    let s = setOf claims
    let outcome = PhysicalClaimRules.adjudicate s
    let proposal = PhysicalClaimRules.proposeCorrespondence s outcome
    let liveCount = flags |> List.filter id |> List.length
    let tombCount = List.length flags - liveCount
    match proposal with
    | Some p -> liveCount = 1 && tombCount >= 1 && p.To.IsActive && not p.From.IsActive
    | None -> liveCount <> 1 || tombCount = 0

// -- the journal assembly (SinkClaims) ---------------------------------------

let private edition () =
    { OssysSnapshotBuilders.snapshotOf
        [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment"
          { OssysSnapshotBuilders.moduleRow 900 "FulfillmentExtension" with EspaceKind = Some "Extension" } ]
        [ OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"
          { OssysSnapshotBuilders.entityRow 8002 800 "Shipment" "OSUSR_FUL_SHIPMENT" with IsActive = false }
          { OssysSnapshotBuilders.entityRow 9002 900 "Shipment" "OSUSR_FUL_SHIPMENT" with IsExternal = true } ]
        [ OssysSnapshotBuilders.identifierRow 80001 8000
          OssysSnapshotBuilders.identifierRow 80021 8002
          OssysSnapshotBuilders.identifierRow 90021 9002 ]
      with Capabilities = [] }

[<Fact>]
let ``claims assemble from the witnessed edition (tombstones included) with the extension flag from the module kind`` () =
    let sets = SinkClaims.assemble (edition ()) []
    Assert.Equal<string list>([ "OSUSR_FUL_ORDER"; "OSUSR_FUL_SHIPMENT" ], sets |> List.map (fun s -> s.Table))
    let shipment = sets |> List.find (fun s -> s.Table = "OSUSR_FUL_SHIPMENT")
    Assert.Equal(2, List.length shipment.Claims)
    let ext = shipment.Claims |> List.find (fun c -> c.EntityId = 9002)
    Assert.True(ext.IsExternalRegistration, "the extension module's external entity carries the flag")
    Assert.True(ext.IsActive)
    let tomb = shipment.Claims |> List.find (fun c -> c.EntityId = 8002)
    Assert.False(tomb.IsActive)
    Assert.False(tomb.IsExternalRegistration)
    match SinkClaims.adjudicateAll (edition ()) [] |> List.find (fun (s, _) -> s.Table = "OSUSR_FUL_SHIPMENT") |> snd with
    | PhysicalClaimRules.PhysicalClaimOutcome.Adopted (winner, _) -> Assert.Equal(9002, winner.EntityId)
    | other -> Assert.Fail (sprintf "expected the re-registration adopted, got %A" other)

// -- the claims-annotation pass (S13) -----------------------------------------

open Projection.Core.Passes
open Projection.Tests.Fixtures

let private adjudicatedAt (table: string) (claims: PhysicalClaimRules.PhysicalClaim list) =
    let s : PhysicalClaimRules.ClaimSet = { Schema = "dbo"; Table = table; Claims = claims }
    s, PhysicalClaimRules.adjudicate s

[<Fact>]
let ``the annotation pass marks kinds whose tables carry non-trivial decisions; sole adoptions and the chain default stay quiet`` () =
    // The fixture catalog's Customer kind lives at dbo.OSUSR_S1S_CUSTOMER.
    let table = "OSUSR_S1S_CUSTOMER"
    let contested = adjudicatedAt table [ claim 1 "Customer" true false 1; claim 2 "Customer" true true 1 ]
    let annotated = (PhysicalClaimPass.registered [ contested ]).Run sampleCatalog
    // Identity-preserving: no kind dropped, no key invented.
    Assert.Equal<Catalog>(sampleCatalog, LineageDiagnostics.payload annotated)
    let details =
        annotated.Trail
        |> List.choose (fun e ->
            match e.TransformKind with
            | Annotated (PhysicalClaimDecision (t, o)) -> Some (t, o)
            | _ -> None)
    match details with
    | [ (t, PhysicalClaimRules.PhysicalClaimOutcome.Contested _) ] -> Assert.Equal("dbo." + table, t)
    | other -> Assert.Fail (sprintf "expected one contested annotation, got %A" other)
    // A sole adoption (no rivals) is trivial — quiet trail.
    let sole = adjudicatedAt table [ claim 1 "Customer" true false 1 ]
    Assert.Empty ((PhysicalClaimPass.registered [ sole ]).Run sampleCatalog).Trail
    // The chain default ([]) is the identity with an empty trail.
    Assert.Empty ((PhysicalClaimPass.registered []).Run sampleCatalog).Trail

// -- align-II.10: FirstWitnessedSync is a value, never a fabricated ordinal ---

[<Fact>]
let ``align-II.10: the first-witnessed reading is a trichotomy — genesis, a named sync, or UNKNOWN (the retired fabrication) — and the rendering says so`` () =
    // The classifier: sync 1 IS the genesis witness; a later sync names
    // itself; no appearance line is UNKNOWN — previously fabricated as 1.
    Assert.Equal(PhysicalClaimRules.FirstWitnessedSync.SinceGenesis, PhysicalClaimRules.FirstWitnessedSync.ofAppearance (Some (ord 1)))
    Assert.Equal(PhysicalClaimRules.FirstWitnessedSync.AtSync (ord 3), PhysicalClaimRules.FirstWitnessedSync.ofAppearance (Some (ord 3)))
    Assert.Equal(PhysicalClaimRules.FirstWitnessedSync.Unknown, PhysicalClaimRules.FirstWitnessedSync.ofAppearance None)
    // The rendering: healthy readings keep their ordinal bytes; Unknown
    // says "?" instead of lying.
    Assert.Equal("1", PhysicalClaimRules.FirstWitnessedSync.text PhysicalClaimRules.FirstWitnessedSync.SinceGenesis)
    Assert.Equal("3", PhysicalClaimRules.FirstWitnessedSync.text (PhysicalClaimRules.FirstWitnessedSync.AtSync (ord 3)))
    Assert.Equal("?", PhysicalClaimRules.FirstWitnessedSync.text PhysicalClaimRules.FirstWitnessedSync.Unknown)
    // The ladder rank: missing provenance never rewards recency (Unknown
    // ranks as the earliest, exactly where the retired fabrication sat, so
    // healthy AND gappy adjudication ladders are order-stable).
    Assert.Equal(1, PhysicalClaimRules.FirstWitnessedSync.rank PhysicalClaimRules.FirstWitnessedSync.Unknown)
    Assert.Equal(1, PhysicalClaimRules.FirstWitnessedSync.rank PhysicalClaimRules.FirstWitnessedSync.SinceGenesis)
    Assert.Equal(3, PhysicalClaimRules.FirstWitnessedSync.rank (PhysicalClaimRules.FirstWitnessedSync.AtSync (ord 3)))

[<Fact>]
let ``align-II.10: assembly reads the journal's appearance line — genesis, a later sync, and the journal-silent claim each classify honestly`` () =
    let appearance (syncId: int) (entityId: int) (table: string) : SinkJournal.JournalLine =
        { SyncId = ord syncId; PrevSyncId = (if syncId = 1 then None else Some (ord (syncId - 1))); CapturedAtUtc = System.DateTimeOffset.UnixEpoch
          Displacement =
            { Table = SinkDisplacement.SinkTable.Entities
              KeyText = sprintf "entity:%d" entityId
              KeyBasis = SinkDisplacement.KeyBasis.Positional entityId
              Before = None
              After = Some (SinkDisplacement.SinkRow.Entity (OssysSnapshotBuilders.entityRow entityId 800 "Order" table))
              Domain = None } }
    // Order appeared at genesis; Shipment's re-registration at sync 3; the
    // tombstoned original has NO appearance line (a reconciled ledger).
    let journal = [ appearance 1 8000 "OSUSR_FUL_ORDER"; appearance 3 9002 "OSUSR_FUL_SHIPMENT" ]
    let sets = SinkClaims.assemble (edition ()) journal
    let order = (sets |> List.find (fun s -> s.Table = "OSUSR_FUL_ORDER")).Claims |> List.find (fun c -> c.EntityId = 8000)
    Assert.Equal(PhysicalClaimRules.FirstWitnessedSync.SinceGenesis, order.FirstWitnessedSync)
    let shipment = sets |> List.find (fun s -> s.Table = "OSUSR_FUL_SHIPMENT")
    let ext = shipment.Claims |> List.find (fun c -> c.EntityId = 9002)
    Assert.Equal(PhysicalClaimRules.FirstWitnessedSync.AtSync (ord 3), ext.FirstWitnessedSync)
    let tomb = shipment.Claims |> List.find (fun c -> c.EntityId = 8002)
    Assert.Equal(PhysicalClaimRules.FirstWitnessedSync.Unknown, tomb.FirstWitnessedSync)
    // The unknown reading renders "?" through the one claim renderer.
    let outcome = PhysicalClaimRules.adjudicate shipment
    match outcome with
    | PhysicalClaimRules.PhysicalClaimOutcome.Adopted (winner, _) ->
        Assert.Equal(9002, winner.EntityId)
        match PhysicalClaimRules.proposeCorrespondence shipment outcome with
        | Some p ->
            let fromText = PhysicalClaimRules.correspondenceClauses p |> List.find (fst >> (=) "from") |> snd
            Assert.Contains("@sync ?", fromText)
        | None -> Assert.Fail "expected a correspondence proposal over the tombstone"
    | other -> Assert.Fail (sprintf "expected adoption, got %A" other)
