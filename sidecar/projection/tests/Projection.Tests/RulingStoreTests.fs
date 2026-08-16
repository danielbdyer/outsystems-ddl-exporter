module Projection.Tests.RulingStoreTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline

// align-II.1 (A53 candidate) — the keyed ruling store's laws. Keyed
// replace-by-key under <store>/rulings/ (one JSON document per ruled
// finding, digest filename, full key inside the document); fail-closed
// load (missing → Ok None — pending-by-absence; malformed → ParseFailure,
// never silently unruled); atomic deterministic writes (T1). NOT a
// LedgerSpec — the align-II.0 standing ruling; history is deferred past
// align-III.2 with BasisAnchor.SinkEdition as its widen trigger.

let private at = DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)

let private withTempStore (f: string -> 'a) : 'a =
    let root = IO.Path.Combine(IO.Path.GetTempPath(), sprintf "ruling-store-%s" (Guid.NewGuid().ToString("N")))
    IO.Directory.CreateDirectory(root) |> ignore
    try f root
    finally (try IO.Directory.Delete(root, true) with _ -> ())

let private key (subject: string) : FindingKey =
    FindingKey.create EstateFindingKind.DataOrphans subject

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "fixture: %A" es

let private ruling
    (subject: FindingKey)
    (verdict: RulingVerdict)
    (basis: BasisAnchor option)
    (rationale: string option)
    (reopen: ReopenCondition option)
    : OperatorRuling<FindingKey> =
    OperatorRuling.create subject verdict basis "dan" at rationale reopen |> mustOk

[<Fact>]
let ``A53 store law: a ruling round-trips through save + load for every anchor and optional-field shape`` () =
    withTempStore (fun root ->
        let shapes =
            [ ruling (key "Order.CustomerId") RulingVerdict.Confirmed None None None
              ruling (key "Order.CustomerId2") RulingVerdict.Rejected (Some (BasisAnchor.Digest "d1")) (Some "not ours") None
              ruling (key "Ent.A") RulingVerdict.Confirmed (Some (BasisAnchor.Fingerprint "fp:1")) None (Some ReopenCondition.OnEvidenceChange)
              ruling (key "Ent.B") RulingVerdict.Confirmed (Some (BasisAnchor.FindingKey (key "Ent.B"))) (Some "matches the witnessed shape") (Some (ReopenCondition.After at))
              ruling (key "Ent.C") RulingVerdict.Rejected (Some (BasisAnchor.EvidenceDigest "ev256")) None None ]
        for r in shapes do
            match RulingStore.save root r with
            | Error e -> Assert.Fail(RulingStore.describe e)
            | Ok () -> ()
            match RulingStore.load root r.Subject with
            | Ok (Some loaded) -> Assert.Equal(r, loaded)
            | Ok None -> Assert.Fail(sprintf "ruling for %s vanished" (FindingKey.text r.Subject))
            | Error e -> Assert.Fail(RulingStore.describe e))

[<Fact>]
let ``A53 store law: a missing ruling is Ok None (pending-by-absence), not an error`` () =
    withTempStore (fun root ->
        match RulingStore.load root (key "Never.Ruled") with
        | Ok None -> ()
        | other -> Assert.Fail(sprintf "expected Ok None, got %A" other))

[<Fact>]
let ``A53 store law: replace-by-key — the second ruling for a key WINS and the first is gone`` () =
    withTempStore (fun root ->
        let subject = key "Order.CustomerId"
        RulingStore.save root (ruling subject RulingVerdict.Confirmed None (Some "first look") None) |> ignore
        RulingStore.save root (ruling subject RulingVerdict.Rejected (Some (BasisAnchor.Digest "d2")) (Some "re-examined") None) |> ignore
        match RulingStore.load root subject with
        | Ok (Some r) ->
            Assert.Equal(RulingVerdict.Rejected, r.Verdict)
            Assert.Equal(Some "re-examined", r.Rationale)
        | other -> Assert.Fail(sprintf "expected the replacing ruling, got %A" other)
        match RulingStore.loadAll root with
        | Ok all -> Assert.Equal(1, List.length all)
        | Error e -> Assert.Fail(RulingStore.describe e))

[<Fact>]
let ``A53 store law: a malformed document is a ParseFailure — never silently unruled`` () =
    withTempStore (fun root ->
        let subject = key "Order.CustomerId"
        RulingStore.save root (ruling subject RulingVerdict.Confirmed None None None) |> ignore
        // Corrupt the stored document in place.
        let dir = IO.Path.Combine(root, "rulings")
        let file = IO.Directory.GetFiles(dir, "*.json") |> Array.exactlyOne
        IO.File.WriteAllText(file, "{ \"findingKey\": 42 }")
        match RulingStore.load root subject with
        | Error (RulingError.ParseFailure _) -> ()
        | other -> Assert.Fail(sprintf "expected ParseFailure, got %A" other)
        // loadAll fail-closes on the same file, naming it.
        match RulingStore.loadAll root with
        | Error (RulingError.ParseFailure (path, _)) -> Assert.Equal(file, path)
        | other -> Assert.Fail(sprintf "expected loadAll ParseFailure, got %A" other))

[<Fact>]
let ``A53 store law: an unknown verdict or basis kind fail-closes with the token named`` () =
    withTempStore (fun root ->
        let subject = key "Order.CustomerId"
        RulingStore.save root (ruling subject RulingVerdict.Confirmed None None None) |> ignore
        let dir = IO.Path.Combine(root, "rulings")
        let file = IO.Directory.GetFiles(dir, "*.json") |> Array.exactlyOne
        let text = IO.File.ReadAllText(file)
        IO.File.WriteAllText(file, text.Replace("\"confirmed\"", "\"maybe\""))
        match RulingStore.load root subject with
        | Error (RulingError.ParseFailure (_, message)) -> Assert.Contains("maybe", message)
        | other -> Assert.Fail(sprintf "expected ParseFailure naming the token, got %A" other))

[<Fact>]
let ``A53 store law: re-saving an unchanged ruling is byte-stable (T1)`` () =
    withTempStore (fun root ->
        let r = ruling (key "Ent.B") RulingVerdict.Confirmed (Some (BasisAnchor.FindingKey (key "Ent.B"))) (Some "stable") (Some ReopenCondition.OnEvidenceChange)
        RulingStore.save root r |> ignore
        let dir = IO.Path.Combine(root, "rulings")
        let file = IO.Directory.GetFiles(dir, "*.json") |> Array.exactlyOne
        let first = IO.File.ReadAllBytes(file)
        RulingStore.save root r |> ignore
        let second = IO.File.ReadAllBytes(file)
        Assert.Equal<byte[]>(first, second))

[<Fact>]
let ``A53 store law: loadAll returns every ruled subject (missing directory is Ok empty)`` () =
    withTempStore (fun root ->
        match RulingStore.loadAll root with
        | Ok [] -> ()
        | other -> Assert.Fail(sprintf "expected Ok [] on a fresh store, got %A" other)
        RulingStore.save root (ruling (key "A.X") RulingVerdict.Confirmed None None None) |> ignore
        RulingStore.save root (ruling (key "B.Y") RulingVerdict.Rejected None (Some "no") None) |> ignore
        match RulingStore.loadAll root with
        | Ok all ->
            let subjects = all |> List.map (fun r -> FindingKey.text r.Subject) |> Set.ofList
            Assert.Equal<Set<string>>(Set.ofList [ "data.orphans:A.X"; "data.orphans:B.Y" ], subjects)
        | Error e -> Assert.Fail(RulingStore.describe e))
