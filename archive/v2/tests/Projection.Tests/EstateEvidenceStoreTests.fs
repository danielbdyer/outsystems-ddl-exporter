module Projection.Tests.EstateEvidenceStoreTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The pay-once evidence store (`EstateEvidenceStore` — DECISIONS 2026-07-15,
// the estate chapter opens, entry 4; the plan's §6 caching design). The laws
// under test:
//   - ROUND-TRIP: `load` after `save` returns the pair — the profile rides
//     ProfileCodec's law-tested round-trip, the fingerprints and the capture
//     time ride the sidecar codec.
//   - REPLACE, NEVER MERGE: a second save fully replaces the first (a merged
//     cache is a worst-case union — stale maxima would live forever).
//   - FAIL-CLOSED: a tampered profile breaks the sidecar's SHA binding; an
//     unreadable sidecar and a never-captured environment read as absent —
//     never a silently-wrong evidence basis.
//   - STALENESS: `staleKinds` names exactly the moved kinds, including a
//     schema-shape movement at identical row count and a kind newly present
//     or newly absent on either side.
//   - RESOLUTION: one directory rule — the estate dir wins, the ledger
//     fallback appends `estate`, neither reads as disabled (`None`).
// ---------------------------------------------------------------------------

let private capturedAt = DateTimeOffset(2026, 7, 15, 8, 30, 0, TimeSpan.Zero)

let private colEvidence (attrKey: SsKey) (rowCount: int64) (nullCount: int64) : ColumnProfile =
    { AttributeKey = attrKey
      RowCount = rowCount
      NullCount = nullCount
      MaxObservedLength = Some 512
      NullCountProbeStatus = ProbeStatus.observed rowCount }

let private fp (kind: SsKey) (rows: int64) (maxPk: string option) (hash: string) : KindFingerprint =
    { Kind = kind; RowCount = rows; MaxPk = maxPk; ContentHash = None; SchemaShapeHash = hash }

let private withTempStore (f: string -> unit) : unit =
    let root = Path.Combine(Path.GetTempPath(), "estate-store-" + Guid.NewGuid().ToString "N")
    try f root
    finally if Directory.Exists root then Directory.Delete(root, true)

let private mustSave (root: string) (env: string) (profile: Profile) (fps: KindFingerprint list) : unit =
    match EstateEvidenceStore.save capturedAt root env profile fps with
    | Ok () -> ()
    | Error es -> failwithf "save failed: %A" es

let private byKind (fps: KindFingerprint list) : KindFingerprint list =
    fps |> List.sortBy (fun f -> SsKey.serialize f.Kind)

// -- round-trip ---------------------------------------------------------------

[<Fact>]
let ``evidence store: load after save returns the pair — the profile rides ProfileCodec's round-trip law`` () =
    withTempStore (fun root ->
        let profile =
            { Profile.empty with
                Columns = [ colEvidence customerNameKey 4200L 17L ] }
        let fingerprints =
            [ fp customerKey 4200L (Some "9931") "shape-customer-v1"
              fp orderKey 0L None "shape-order-v1" ]
        mustSave root "cloud-uat" profile fingerprints
        match EstateEvidenceStore.load root "cloud-uat" with
        | None -> failwith "the just-saved evidence loaded as absent"
        | Some evidence ->
            Assert.Equal<Profile>(profile, evidence.Profile)
            Assert.Equal(capturedAt, evidence.CapturedAtUtc)
            Assert.Equal<KindFingerprint list>(byKind fingerprints, byKind evidence.Fingerprints))

[<Fact>]
let ``evidence store: a second save replaces the first — refresh replaces, never merges`` () =
    withTempStore (fun root ->
        let first  = { Profile.empty with Columns = [ colEvidence customerNameKey 4200L 17L ] }
        let second = { Profile.empty with Columns = [ colEvidence customerNameKey 9000L 0L ] }
        mustSave root "cloud-uat" first  [ fp customerKey 4200L (Some "1") "h1" ]
        mustSave root "cloud-uat" second [ fp customerKey 9000L (Some "2") "h2" ]
        match EstateEvidenceStore.load root "cloud-uat" with
        | None -> failwith "the replaced evidence loaded as absent"
        | Some evidence ->
            Assert.Equal<Profile>(second, evidence.Profile)
            Assert.Equal<KindFingerprint list>([ fp customerKey 9000L (Some "2") "h2" ], evidence.Fingerprints))

[<Fact>]
let ``staleKinds + codec: a content-hash change flags the kind even when rows and max PK hold (survival rule 14 closed) and round-trips`` () =
    // The in-place-UPDATE case: same COUNT_BIG, same MAX(pk), same schema
    // shape — only the aggregate row checksum moved. The content hash makes it
    // movement, so cached evidence is not reused over the update.
    let cached = [ { fp customerKey 4200L (Some "9931") "shape-v1" with ContentHash = Some "AAAA" } ]
    let live   = [ { fp customerKey 4200L (Some "9931") "shape-v1" with ContentHash = Some "BBBB" } ]
    Assert.Equal<SsKey list>([ customerKey ], EstateEvidenceStore.staleKinds cached live)
    // Identical content hashes read as clean — no spurious re-profile.
    Assert.Empty(EstateEvidenceStore.staleKinds cached cached)
    // The content hash survives the sidecar round-trip.
    withTempStore (fun root ->
        mustSave root "cloud-uat" Profile.empty cached
        match EstateEvidenceStore.load root "cloud-uat" with
        | Some ev -> Assert.Equal<KindFingerprint list>(cached, ev.Fingerprints)
        | None -> failwith "the content-hash sidecar loaded as absent")

// -- fail-closed ---------------------------------------------------------------

[<Fact>]
let ``evidence store: a tampered profile fails the sidecar's SHA binding and loads as absent (fail-closed)`` () =
    withTempStore (fun root ->
        mustSave root "cloud-uat" Profile.empty [ fp customerKey 100L None "h1" ]
        File.AppendAllText(EstateEvidenceStore.profilePath root "cloud-uat", " ")
        Assert.True(EstateEvidenceStore.load root "cloud-uat" |> Option.isNone))

[<Fact>]
let ``evidence store: an unreadable sidecar reads as an absent cache (fail-closed)`` () =
    withTempStore (fun root ->
        mustSave root "cloud-uat" Profile.empty [ fp customerKey 100L None "h1" ]
        File.WriteAllText(EstateEvidenceStore.fingerprintsPath root "cloud-uat", "not json")
        Assert.True(EstateEvidenceStore.load root "cloud-uat" |> Option.isNone))

[<Fact>]
let ``evidence store: an environment never captured loads as absent`` () =
    withTempStore (fun root ->
        Assert.True(EstateEvidenceStore.load root "cloud-qa" |> Option.isNone))

// -- staleness -----------------------------------------------------------------

[<Fact>]
let ``staleKinds: a moved fingerprint names its kind; an unchanged fingerprint stays quiet`` () =
    let cached = [ fp customerKey 4200L (Some "9931") "h-customer"; fp orderKey 500L (Some "500") "h-order" ]
    let live   = [ fp customerKey 4200L (Some "9931") "h-customer"; fp orderKey 501L (Some "501") "h-order" ]
    Assert.Equal<SsKey list>([ orderKey ], EstateEvidenceStore.staleKinds cached live)
    Assert.Empty(EstateEvidenceStore.staleKinds cached cached)

[<Fact>]
let ``staleKinds: a schema-shape movement at identical row count still invalidates the kind (the fingerprint's counterweight to the in-place-UPDATE caveat)`` () =
    let cached = [ fp customerKey 4200L (Some "9931") "shape-v1" ]
    let live   = [ fp customerKey 4200L (Some "9931") "shape-v2" ]
    Assert.Equal<SsKey list>([ customerKey ], EstateEvidenceStore.staleKinds cached live)

[<Fact>]
let ``staleKinds: a kind newly present or newly absent is stale on either side (movement, never a silent skip)`` () =
    let customer = fp customerKey 100L None "h"
    let order    = fp orderKey 200L None "h"
    Assert.Equal<SsKey list>([ orderKey ], EstateEvidenceStore.staleKinds [ customer ] [ customer; order ])
    Assert.Equal<SsKey list>([ orderKey ], EstateEvidenceStore.staleKinds [ customer; order ] [ customer ])

// -- directory resolution --------------------------------------------------------

[<Fact>]
let ``store resolution: the estate dir wins, the ledger fallback appends estate, neither reads as disabled`` () =
    Assert.Equal<string option>(Some "/var/estate", EstateEvidenceStore.storeDirFrom (Some "/var/estate") (Some "/var/ledger"))
    Assert.Equal<string option>(Some (Path.Combine("/var/ledger", "estate")), EstateEvidenceStore.storeDirFrom None (Some "/var/ledger"))
    Assert.Equal<string option>(None, EstateEvidenceStore.storeDirFrom None None)
    // A blank estate dir is not a choice — the fallback rule still applies.
    Assert.Equal<string option>(Some (Path.Combine("/var/ledger", "estate")), EstateEvidenceStore.storeDirFrom (Some "   ") (Some "/var/ledger"))
