namespace Projection.Tests

// The sink store + journal laws (the data-sink chapter, S4b;
// CHAPTER_SINK_OPEN.md): digest credential-rotation invariance, the
// totality gate, atomic persistence with fail-closed reads, the journal's
// fsync/torn-line/regression contract, and the replay chain through
// Ledger.replay — all pure-pool over temp stores.

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open FsCheck.Xunit
open Projection.Adapters.OssysSql

module SinkStoreTests =

    /// align-III.1: expected-ordinal literal for asserts (patterns can't call functions).
    let private ord (n: int) : SyncOrdinal =
        match SyncOrdinal.create n with
        | Ok o -> o
        | Error m -> failwith m

    let private nowUtc = DateTimeOffset.Parse("2026-08-15T12:00:00Z", Globalization.CultureInfo.InvariantCulture)

    let private withTempStore (test: string -> unit) =
        let root = Path.Combine(Path.GetTempPath(), "sink-store-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        try test root
        finally
            try Directory.Delete(root, true) with _ -> ()

    let private snapshotA () =
        OssysSnapshotBuilders.snapshotOf
            [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ]
            [ OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" ]
            [ OssysSnapshotBuilders.identifierRow 80001 8000 ]

    let private snapshotB () =
        OssysSnapshotBuilders.snapshotOf
            [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ]
            [ { OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" with IsActive = false } ]
            [ OssysSnapshotBuilders.identifierRow 80001 8000 ]

    // ------------------------------------------------------------------
    // The digest.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``digest: credential-rotation invariant — only DataSource and InitialCatalog participate`` () =
        // Same server+db under different casing/whitespace = one store.
        Assert.Equal(
            SinkStore.connDigest16 "tcp:Estate.example,1433" "OSDB",
            SinkStore.connDigest16 "  TCP:ESTATE.EXAMPLE,1433 " "osdb")
        // A different database is a different store.
        Assert.NotEqual<string>(
            SinkStore.connDigest16 "tcp:estate.example,1433" "osdb",
            SinkStore.connDigest16 "tcp:estate.example,1433" "osdb2")
        Assert.Equal(16, (SinkStore.connDigest16 "s" "d").Length)

    // ------------------------------------------------------------------
    // The totality gate.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``totality gate: only a defaultParameters-shaped read witnesses; each scoped axis skips by name`` () =
        withTempStore (fun root ->
            let scoped =
                [ { MetadataSnapshotRunner.defaultParameters with ModuleNames = [ "Fulfillment" ] }, MetadataSnapshotRunner.ScopeAxis.Modules
                  { MetadataSnapshotRunner.defaultParameters with IncludeInactive = false }, MetadataSnapshotRunner.ScopeAxis.Lifecycle
                  { MetadataSnapshotRunner.defaultParameters with OnlyActiveAttributes = true }, MetadataSnapshotRunner.ScopeAxis.AttributeActivity
                  { MetadataSnapshotRunner.defaultParameters with IncludeSystem = false }, MetadataSnapshotRunner.ScopeAxis.System
                  { MetadataSnapshotRunner.defaultParameters with EntityFilterJson = Some "{}" }, MetadataSnapshotRunner.ScopeAxis.EntityFilter ]
            for parameters, expectedAxis in scoped do
                match SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] parameters (snapshotA ()) with
                // align-II.9 — the skip names the axis that fired the gate.
                | SinkStore.WitnessOutcome.SkippedScoped axes ->
                    Assert.Equal<MetadataSnapshotRunner.ScopeAxis list>([ expectedAxis ], axes)
                | other -> Assert.Fail (sprintf "scoped read was not gated: %A" other)
            // Nothing was written by any of them.
            Assert.False(Directory.Exists(SinkStore.sinkRoot root) && Directory.EnumerateFileSystemEntries(SinkStore.sinkRoot root) |> Seq.isEmpty |> not))

    [<Fact>]
    let ``store disabled is a named no-op, never a throw`` () =
        match SinkStore.witnessWith None nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ()) with
        | SinkStore.WitnessOutcome.Disabled reason -> Assert.Contains("live-only", reason)
        | other -> Assert.Fail (sprintf "expected Disabled, got %A" other)

    // ------------------------------------------------------------------
    // Witness → journal → replay: the chain.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``witness persists sync 1, journals every appearance, and replay reproduces the canonical snapshot`` () =
        withTempStore (fun root ->
            let outcome = SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ())
            match outcome with
            | SinkStore.WitnessOutcome.Persisted (e, displacements, false) when e.Ordinal = ord 1 ->
                Assert.Equal(3, displacements) // module + entity + attribute
            | other -> Assert.Fail (sprintf "expected Persisted sync 1, got %A" other)
            let digest = SinkStore.connDigest16 "server" "db"
            let manifest = (SinkStore.loadManifest root digest).Value
            Assert.Equal(ord 1, manifest.LatestSyncId)
            Assert.Equal(None, manifest.EnvLabel)
            let lines =
                match SinkJournal.load (SinkStore.journalPath root digest) with
                | Ok l -> l
                | Error e -> failwithf "journal load failed: %A" e
            Assert.Equal(3, List.length lines)
            let verified =
                match SinkJournal.admitChain lines with
                | Ok v -> v
                | Error e -> failwithf "chain refused: %A" e
            Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                SinkDisplacement.canonical (snapshotA ()),
                SinkJournal.replay verified))

    [<Fact>]
    let ``a second unchanged witness is Unchanged and appends nothing (CDC-silence at the store)`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            let before = File.ReadAllText(SinkStore.journalPath root digest)
            match SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ()) with
            // The unchanged outcome names the full EDITION it still
            // equals (align-III.1: the SinkEdition carrier).
            | SinkStore.WitnessOutcome.Unchanged e when e = { ConnDigest = digest; Ordinal = ord 1 } -> ()
            | other -> Assert.Fail (sprintf "expected Unchanged at edition 1, got %A" other)
            Assert.Equal(before, File.ReadAllText(SinkStore.journalPath root digest)))

    [<Fact>]
    let ``a changed estate witnesses sync 2 with the tombstone displacement journaled and replay tracking it`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            match SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotB ()) with
            | SinkStore.WitnessOutcome.Persisted (e, 1, false) when e.Ordinal = ord 2 -> ()
            | other -> Assert.Fail (sprintf "expected Persisted sync 2 with 1 displacement, got %A" other)
            let lines = (SinkJournal.load (SinkStore.journalPath root digest)) |> Result.defaultValue []
            let last = List.last lines
            Assert.Equal(ord 2, last.SyncId)
            Assert.Equal(Some (ord 1), last.PrevSyncId)
            let verified = (SinkJournal.admitChain lines) |> Result.defaultWith (fun e -> failwithf "chain refused: %A" e)
            Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                SinkDisplacement.canonical (snapshotB ()),
                SinkJournal.replay verified)
            // The latest snapshot loads SHA-bound; the previous stays loadable.
            Assert.True((SinkStore.loadSnapshotAt root digest (ord 2)).IsSome)
            Assert.True((SinkStore.loadSnapshotAt root digest (ord 1)).IsSome))

    [<Fact>]
    let ``nameEnvironment stamps the label and later witnesses preserve it`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            Assert.True(SinkStore.nameEnvironment root digest "cloud-uat")
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotB ()) |> ignore
            Assert.Equal(Some "cloud-uat", (SinkStore.loadManifest root digest).Value.EnvLabel))

    // ------------------------------------------------------------------
    // Journal file contract.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``journal: a torn trailing line is tolerated; an interior corrupt line refuses by name`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            let path = SinkStore.journalPath root digest
            let intact = File.ReadAllText path
            // Torn trailing line: append a half-written line with no newline.
            File.WriteAllText(path, intact + "{\"syncId\": 2, \"prevSy")
            match SinkJournal.load path with
            | Ok lines -> Assert.Equal(3, List.length lines)
            | Error e -> Assert.Fail (sprintf "torn trailing line was not tolerated: %A" e)
            // Interior corruption: the same garbage followed by a newline
            // and more content refuses by name.
            File.WriteAllText(path, "garbage line\n" + intact)
            match SinkJournal.load path with
            | Ok _ -> Assert.Fail "interior corruption parsed"
            | Error errors ->
                Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.journal.corruptLine"))

    [<Fact>]
    let ``journal: a regressing syncId refuses on the drift channel (sink.journal.syncRegression)`` () =
        let line syncId =
            { SinkJournal.SyncId = ord syncId
              SinkJournal.PrevSyncId = None
              SinkJournal.CapturedAtUtc = nowUtc
              SinkJournal.Displacement =
                { Table = SinkDisplacement.SinkTable.Modules
                  KeyText = "espace:800"
                  KeyBasis = SinkDisplacement.KeyBasis.Positional 800
                  Before = None
                  After = Some (SinkDisplacement.WitnessedRow.Module (OssysSnapshotBuilders.moduleRow 800 "Fulfillment"))
                  Domain = None } }
        match SinkJournal.admitChain [ line 2; line 1 ] with
        | Ok _ -> Assert.Fail "a regressing chain admitted"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.journal.syncRegression")

    [<Fact>]
    let ``align-III.2: a broken predecessor link refuses on the chain channel (sink.journal.brokenChain)`` () =
        // The tautology's retirement made visible: a MONOTONE chain whose
        // sync 2 names the WRONG predecessor (sync 9, never witnessed) now
        // refuses `brokenChain` — the old FingerprintOf=SyncId self-compare
        // admitted it silently. `line sync prev` builds one sync's line.
        let line syncId prevSyncId =
            { SinkJournal.SyncId = ord syncId
              SinkJournal.PrevSyncId = prevSyncId
              SinkJournal.CapturedAtUtc = nowUtc
              SinkJournal.Displacement =
                { Table = SinkDisplacement.SinkTable.Modules
                  KeyText = "espace:800"
                  KeyBasis = SinkDisplacement.KeyBasis.Positional 800
                  Before = None
                  After = Some (SinkDisplacement.WitnessedRow.Module (OssysSnapshotBuilders.moduleRow 800 "Fulfillment"))
                  Domain = None } }
        // A well-formed two-sync chain admits (genesis → sync 2 linking to 1).
        match SinkJournal.admitChain [ line 1 None; line 2 (Some (ord 1)) ] with
        | Ok verified -> Assert.Equal(2, List.length verified)
        | Error errors -> Assert.Fail(sprintf "a linked chain must admit: %A" (errors |> List.map (fun e -> e.Code)))
        // The same chain with sync 2 naming predecessor 9 breaks by name.
        match SinkJournal.admitChain [ line 1 None; line 2 (Some (ord 9)) ] with
        | Ok _ -> Assert.Fail "a broken predecessor link admitted"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.journal.brokenChain")

    [<Fact>]
    let ``align-III.1: a stored line naming sync 0 is a corrupt line by name — the ordinal re-mints fail-closed`` () =
        // Render a healthy line, then tamper the wire's syncId to 0 (the
        // ordinal VO makes the value unmintable in memory, so only a torn
        // or foreign store can present it).
        let rendered =
            SinkJournal.renderLine
                { SinkJournal.SyncId = ord 1
                  SinkJournal.PrevSyncId = None
                  SinkJournal.CapturedAtUtc = nowUtc
                  SinkJournal.Displacement =
                    { Table = SinkDisplacement.SinkTable.Modules
                      KeyText = "espace:800"
                      KeyBasis = SinkDisplacement.KeyBasis.Positional 800
                      Before = None
                      After = Some (SinkDisplacement.WitnessedRow.Module (OssysSnapshotBuilders.moduleRow 800 "Fulfillment"))
                      Domain = None } }
        let tampered = rendered.Replace("\"syncId\":1", "\"syncId\":0")
        match SinkJournal.parseLine tampered with
        | Ok l -> Assert.Fail (sprintf "a sync-0 line parsed: %A" l.SyncId)
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.journal.corruptLine")

    [<Fact>]
    let ``journal line codec: a rendered line parses back to the same displacement (domain re-derivable, images exact)`` () =
        let original =
            { SinkJournal.SyncId = ord 4
              SinkJournal.PrevSyncId = Some (ord 3)
              SinkJournal.CapturedAtUtc = nowUtc
              SinkJournal.Displacement =
                { Table = SinkDisplacement.SinkTable.Entities
                  KeyText = "entity:8000"
                  KeyBasis = SinkDisplacement.KeyBasis.Positional 8000
                  Before = Some (SinkDisplacement.WitnessedRow.Entity (OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"))
                  After = Some (SinkDisplacement.WitnessedRow.Entity { OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" with IsActive = false })
                  Domain = Some SinkDisplacement.DomainTransition.EntityDeactivated } }
        match SinkJournal.parseLine (SinkJournal.renderLine original) with
        | Error e -> Assert.Fail (sprintf "line did not parse back: %A" e)
        | Ok parsed ->
            // align-II.10: the FULL inverse — the domain classification
            // decodes too, so the whole line round-trips identically.
            Assert.Equal(original, parsed)

    [<Fact>]
    let ``align-II.10 (beside T19): parseLine ∘ renderLine = id over every domain classification — the read side loses nothing the witness recorded`` () =
        let domains : SinkDisplacement.DomainTransition option list =
            [ None
              Some SinkDisplacement.DomainTransition.EntityDeactivated
              Some SinkDisplacement.DomainTransition.EntityReactivated
              Some (SinkDisplacement.DomainTransition.EntityRehomed (800, 900))
              Some SinkDisplacement.DomainTransition.EntityRegisteredExternal
              Some (SinkDisplacement.DomainTransition.PhysicalTableClaimChanged ("OSUSR_A_T", "OSUSR_B_T"))
              Some (SinkDisplacement.DomainTransition.PhysicalTableSuperseded "OSUSR_A_T")
              Some SinkDisplacement.DomainTransition.AttributeRetired
              Some SinkDisplacement.DomainTransition.AttributeReactivated
              Some (SinkDisplacement.DomainTransition.AttributeRetyped [ AttributeFacet.DataType; AttributeFacet.Length ])
              Some SinkDisplacement.DomainTransition.ModuleRetired
              Some SinkDisplacement.DomainTransition.ModuleReactivated
              Some SinkDisplacement.DomainTransition.ShapeChanged ]
        for domain in domains do
            let line =
                { SinkJournal.SyncId = ord 4
                  SinkJournal.PrevSyncId = Some (ord 3)
                  SinkJournal.CapturedAtUtc = nowUtc
                  SinkJournal.Displacement =
                    { Table = SinkDisplacement.SinkTable.Entities
                      KeyText = "entity:8000"
                      KeyBasis = SinkDisplacement.KeyBasis.Positional 8000
                      Before = Some (SinkDisplacement.WitnessedRow.Entity (OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"))
                      After = Some (SinkDisplacement.WitnessedRow.Entity { OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" with IsActive = false })
                      Domain = domain } }
            match SinkJournal.parseLine (SinkJournal.renderLine line) with
            | Ok parsed -> Assert.Equal(line, parsed)
            | Error e -> Assert.Fail (sprintf "domain %A did not round-trip: %A" domain e)
        // An unknown domain token fail-closes — never silently unclassified.
        let rendered =
            SinkJournal.renderLine
                { SinkJournal.SyncId = ord 1; SinkJournal.PrevSyncId = None; SinkJournal.CapturedAtUtc = nowUtc
                  SinkJournal.Displacement =
                    { Table = SinkDisplacement.SinkTable.Entities; KeyText = "entity:1"
                      KeyBasis = SinkDisplacement.KeyBasis.Positional 1
                      Before = None
                      After = Some (SinkDisplacement.WitnessedRow.Entity (OssysSnapshotBuilders.entityRow 1 800 "A" "OSUSR_A"))
                      Domain = Some SinkDisplacement.DomainTransition.ShapeChanged } }
        match SinkJournal.parseLine (rendered.Replace("\"shapeChanged\"", "\"mysteryToken\"")) with
        | Ok _ -> Assert.Fail "an unknown domain token parsed"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.journal.corruptLine")

    [<Fact>]
    let ``align-II.10: JournalReading names an unreadable ledger and carries the lines of a readable one`` () =
        match SinkJournal.JournalReading.ofLoad (Ok []) with
        | SinkJournal.JournalReading.Read [] -> ()
        | other -> Assert.Fail (sprintf "expected Read [], got %A" other)
        let unreadable =
            SinkJournal.JournalReading.ofLoad
                (Result.failureOf (ValidationError.create "sink.journal.unreadable" "journal read failed: the disk is on fire"))
        match unreadable with
        | SinkJournal.JournalReading.Unreadable cause ->
            Assert.Contains("the disk is on fire", cause)
            Assert.Empty(SinkJournal.JournalReading.lines unreadable)
        | other -> Assert.Fail (sprintf "expected Unreadable, got %A" other)

    // ------------------------------------------------------------------
    // Orphan reconciliation.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``an orphan snapshot (manifest past the journal) reconciles at the next witness, named in its outcome`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            // Simulate the crash window: the journal vanishes; the
            // manifest + snapshot survive.
            File.Delete(SinkStore.journalPath root digest)
            match SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (snapshotB ()) with
            | SinkStore.WitnessOutcome.Persisted (e, 1, true) when e.Ordinal = ord 2 -> ()
            | other -> Assert.Fail (sprintf "expected reconciling Persisted sync 2, got %A" other)
            // The reconciled journal replays to the latest state whole.
            let lines = (SinkJournal.load (SinkStore.journalPath root digest)) |> Result.defaultValue []
            let verified = (SinkJournal.admitChain lines) |> Result.defaultWith (fun e -> failwithf "chain refused: %A" e)
            Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                SinkDisplacement.canonical (snapshotB ()),
                SinkJournal.replay verified))

    // ------------------------------------------------------------------
    // A49's structural pin (the sync verb; S6).
    // ------------------------------------------------------------------

    [<Fact>]
    let ``A49 pin: the sync verb acquires with defaultParameters exactly (total acquisition, structurally)`` () =
        // `projection sync` MUST ride the show-me-everything shape — the
        // totality gate at the store enforces it dynamically; this pins it
        // structurally so a drifted binding fails in the pure pool, cite-ably
        // (AXIOMS A49 promotes over this at S9).
        Assert.Equal(MetadataSnapshotRunner.defaultParameters, SinkSyncRun.acquisitionParameters)
        Assert.True(SinkStore.isTotalAcquisition SinkSyncRun.acquisitionParameters)

    // ------------------------------------------------------------------
    // T19's chain law (S10): the journal replays — fold from genesis =
    // latest, at canonical grain, for ANY witnessed chain.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``T19 chain law: witnessing any snapshot chain, the journal replays to the latest canonical state`` () =
        // An enumerated chain family over the fully-populated builder
        // (16 rowsets, seed-varied): duplicates exercise CDC-silence
        // mid-chain (an Unchanged witness appends nothing and must not
        // disturb the fold); reorderings exercise non-monotone estates.
        let chains =
            [ [ 1 ]
              [ 1; 1 ]
              [ 1; 2 ]
              [ 1; 2; 1 ]
              [ 3; 3; 4; 5; 4 ]
              [ 9; 8; 7; 6; 5; 4 ] ]
        for chain in chains do
            withTempStore (fun root ->
                let digest = SinkStore.connDigest16 "server" "db"
                for seed in chain do
                    SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (OssysSnapshotBuilders.fullyPopulated seed) |> ignore
                let verified =
                    SinkJournal.load (SinkStore.journalPath root digest)
                    |> Result.defaultValue []
                    |> SinkJournal.admitChain
                    |> Result.defaultWith (fun e -> failwithf "chain %A refused: %A" chain e)
                let latest =
                    match SinkStore.loadLatest root digest with
                    | Some (_, snapshot) -> snapshot
                    | None -> failwithf "chain %A left no witnessed state" chain
                Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                    SinkDisplacement.canonical latest, SinkJournal.replay verified))

    [<Property(MaxTest = 30)>]
    let ``T19 chain law (FsCheck): random two-to-four-edition chains replay to the latest canonical state`` (a: int) (b: int) (c: int) =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            for seed in [ a; b; c ] do
                SinkStore.witnessWith (Some root) nowUtc "server" "db" None [] MetadataSnapshotRunner.defaultParameters (OssysSnapshotBuilders.fullyPopulated seed) |> ignore
            let verified =
                SinkJournal.load (SinkStore.journalPath root digest)
                |> Result.defaultValue []
                |> SinkJournal.admitChain
                |> Result.defaultWith (fun e -> failwithf "chain refused: %A" e)
            match SinkStore.loadLatest root digest with
            | Some (_, latest) ->
                Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                    SinkDisplacement.canonical latest, SinkJournal.replay verified)
            | None -> Assert.Fail "no witnessed state")
