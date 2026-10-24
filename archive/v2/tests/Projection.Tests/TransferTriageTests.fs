module Projection.Tests.TransferTriageTests

// THE TRIAGE LAYER (2026-07-10, the manifest program, slice 1 —
// THE_TRANSFER_MANIFEST.md §3 / §9.1): the pure witnesses. The single safety
// invariant under test is FAIL-TOWARD-FOREGROUNDING — the only dangerous
// mistake is an `Open` unit mis-classed `Settled` — plus determinism of the
// ranking (pretty and JSON lenses must agree under any input permutation) and
// the total-preserving fold (the fold hides scroll, never tally).

open Xunit
open Projection.Core
open Projection.Pipeline

// -- fixture: deterministic segments over a small combinatorial grid ---------

let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "TRIAGE_KIND" [ s ] |> Result.value

let private ctx (name: string) (before: int) (added: int) (deleted: int) (changed: int) (unchanged: int) : TransferImpact.TableContext =
    { Kind = kKey name; SinkBefore = before; Added = added; Deleted = deleted; Changed = changed; Unchanged = unchanged }

let private seg (names: string list) (contexts: TransferImpact.TableContext list) : TransferImpact.Segment =
    { Members = names |> List.map kKey
      Roots = names |> List.truncate 1 |> List.map kKey
      Documents = []
      Context = contexts }

/// The deterministic battery: every combination of (member count 1..3) ×
/// (added 0/2) × (deleted 0/1) × (changed 0/3) × (unchanged 0/5) — 48
/// structurally distinct segments, no RNG (determinism is constructed).
let private battery : TransferImpact.Segment list =
    [ for m in 1..3 do
        for a in [ 0; 2 ] do
          for d in [ 0; 1 ] do
            for c in [ 0; 3 ] do
              for u in [ 0; 5 ] do
                let names = [ for i in 1..m -> sprintf "K%d_m%d_%d%d%d%d" i m a d c u ]
                yield seg names (names |> List.map (fun n -> ctx n 10 a d c u)) ]

let private keysOf (s: TransferImpact.Segment) : Set<SsKey> = Set.ofList s.Members

let private noSignals = Set.empty<SsKey>

// -- 1. totality --------------------------------------------------------------

[<Fact>]
let ``triage: classify is TOTAL over the whole battery under every signal shape (never throws)`` () =
    for s in battery do
        let members = keysOf s
        // no signals, every signal, and each signal alone
        for (e, r, dv, ds, st) in
            [ noSignals, noSignals, noSignals, noSignals, noSignals
              members, members, members, members, members
              members, noSignals, noSignals, noSignals, noSignals
              noSignals, members, noSignals, noSignals, noSignals
              noSignals, noSignals, members, noSignals, noSignals
              noSignals, noSignals, noSignals, members, noSignals
              noSignals, noSignals, noSignals, noSignals, members ] do
            TransferTriage.classify e r dv ds st s |> ignore

// -- 2. fail toward foregrounding ---------------------------------------------

[<Fact>]
let ``triage: any force-OPEN signal intersecting the members yields an Open class, never Settled`` () =
    for s in battery do
        let members = keysOf s
        let one = members |> Set.toList |> List.head |> Set.singleton
        // an escape, a red verdict, or a divergence forces OpenEscaping
        for signal in [ members; one ] do
            Assert.Equal(TransferTriage.TriageClass.OpenEscaping, TransferTriage.classify signal noSignals noSignals noSignals noSignals s)
            Assert.Equal(TransferTriage.TriageClass.OpenEscaping, TransferTriage.classify noSignals signal noSignals noSignals noSignals s)
            Assert.Equal(TransferTriage.TriageClass.OpenEscaping, TransferTriage.classify noSignals noSignals signal noSignals noSignals s)
            // a destructive act forces OpenDestructive (absent the above)
            Assert.Equal(TransferTriage.TriageClass.OpenDestructive, TransferTriage.classify noSignals noSignals noSignals signal noSignals s)

[<Fact>]
let ``triage: precedence — an escape outranks a destructive signal on the same unit`` () =
    let s = battery |> List.head
    let members = keysOf s
    Assert.Equal(TransferTriage.TriageClass.OpenEscaping, TransferTriage.classify members noSignals noSignals members noSignals s)

// -- 3. a wipe is never SettledClosed ------------------------------------------

[<Fact>]
let ``triage: a wiped unit is never SettledClosed — every destructive act forces OPEN (§10-K)`` () =
    // a unit whose rows would otherwise read settled (changed-only), with its
    // kind in the destructive (wipe) set: must be OpenDestructive.
    let s = seg [ "W" ] [ ctx "W" 100 0 0 3 97 ]
    let wiped = Set.singleton (kKey "W")
    Assert.Equal(TransferTriage.TriageClass.OpenDestructive, TransferTriage.classify noSignals noSignals noSignals wiped noSignals s)
    Assert.NotEqual(TransferTriage.TriageClass.SettledClosed, TransferTriage.classify noSignals noSignals noSignals wiped noSignals s)

// -- 4. rank determinism --------------------------------------------------------

[<Fact>]
let ``triage: rank is permutation-invariant — pretty and JSON lenses agree under any input order`` () =
    let units =
        TransferTriage.unitsOf noSignals noSignals noSignals noSignals noSignals battery
    let shuffled =
        // a deterministic permutation (reverse + interleave), no RNG
        let rev = List.rev units
        let odd = rev |> List.indexed |> List.filter (fun (i, _) -> i % 2 = 1) |> List.map snd
        let even = rev |> List.indexed |> List.filter (fun (i, _) -> i % 2 = 0) |> List.map snd
        odd @ even
    Assert.Equal<TransferTriage.TransferUnit list>(TransferTriage.rank units, TransferTriage.rank shuffled)

[<Fact>]
let ``triage: open units rank before settled; the heaviest coupled unit ranks first in its band`` () =
    let heavy = seg [ "Heavy" ] [ ctx "Heavy" 0 500 0 0 0 ]
    let light = seg [ "Light" ] [ ctx "Light" 0 2 0 0 0 ]
    let escaping = seg [ "Esc" ] [ ctx "Esc" 0 3 0 0 0 ]
    let units = TransferTriage.unitsOf (Set.singleton (kKey "Esc")) noSignals noSignals (Set.ofList [ kKey "Heavy"; kKey "Light"; kKey "Esc" ]) noSignals [ light; heavy; escaping ]
    // the 3-row escaping unit outranks the 500-row clean reload (the penalty
    // and the band), and within the destructive band the heavy unit outranks
    // the light one.
    let labelOf (u: TransferTriage.TransferUnit) =
        let r = SsKey.rootOriginal (List.head u.Segment.Members)
        if r.Contains "Esc" then "Esc" elif r.Contains "Heavy" then "Heavy" else "Light"
    Assert.Equal<string list>([ "Esc"; "Heavy"; "Light" ], units |> List.map labelOf)

// -- 5. the fold preserves the tally --------------------------------------------

[<Fact>]
let ``triage: unitsOf preserves every segment and its tally — the fold hides scroll, never rows`` () =
    let units = TransferTriage.unitsOf noSignals noSignals noSignals noSignals noSignals battery
    Assert.Equal(List.length battery, List.length units)
    let tally (ss: TransferImpact.Segment list) =
        ss |> List.sumBy (fun s -> s.Context |> List.sumBy (fun c -> c.Added + c.Deleted + c.Changed + c.Unchanged))
    Assert.Equal(tally battery, tally (units |> List.map (fun u -> u.Segment)))

// -- 6. SettledStatic requires empty divergences ---------------------------------

[<Fact>]
let ``triage: a static-lookup unit with a divergence is OpenEscaping, never SettledStatic`` () =
    let s = seg [ "S" ] [ ctx "S" 10 0 0 0 10 ]
    let staticKinds = Set.singleton (kKey "S")
    // clean static: SettledStatic
    Assert.Equal(TransferTriage.TriageClass.SettledStatic, TransferTriage.classify noSignals noSignals noSignals noSignals staticKinds s)
    // the same unit with a divergence: OpenEscaping
    Assert.Equal(TransferTriage.TriageClass.OpenEscaping, TransferTriage.classify noSignals noSignals (Set.singleton (kKey "S")) noSignals staticKinds s)

// -- 7. the JSON twin carries every unit, uncapped --------------------------------

[<Fact>]
let ``triage: the JSON twin carries EVERY unit with its triage and couplingWeight — the machine lens never loses what the pretty lens folds`` () =
    let emptyCatalog = Catalog.create [] [] |> Result.value
    // 20 settled units — far past the pretty lens's settled tail cap.
    let many = [ for i in 1..20 -> seg [ sprintf "T%02d" i ] [ ctx (sprintf "T%02d" i) 5 0 0 0 5 ] ]
    let units = TransferTriage.unitsOf noSignals noSignals noSignals noSignals noSignals many
    let impact : TransferImpact.Impact =
        { Flow = "golden"; Strategy = "merge (upsert)"
          Summary = []
          Segments = many
          Totals = { Added = 0; Deleted = 0; Changed = 0; Unchanged = 100 } }
    let json = Projection.Cli.TransferImpactView.toJsonTriaged emptyCatalog units impact
    use doc = System.Text.Json.JsonDocument.Parse json
    let segs = doc.RootElement.GetProperty "segments"
    Assert.Equal(20, segs.GetArrayLength())
    for i in 0 .. segs.GetArrayLength() - 1 do
        let s = segs.[i]
        Assert.Equal("settled-noop", s.GetProperty("triage").GetString())
        Assert.True(s.TryGetProperty "couplingWeight" |> fst)
