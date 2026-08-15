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
open Projection.Adapters.OssysSql

module SinkStoreTests =

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
                [ { MetadataSnapshotRunner.defaultParameters with ModuleNames = [ "Fulfillment" ] }
                  { MetadataSnapshotRunner.defaultParameters with IncludeInactive = false }
                  { MetadataSnapshotRunner.defaultParameters with OnlyActiveAttributes = true }
                  { MetadataSnapshotRunner.defaultParameters with IncludeSystem = false }
                  { MetadataSnapshotRunner.defaultParameters with EntityFilterJson = Some "{}" } ]
            for parameters in scoped do
                match SinkStore.witnessWith (Some root) nowUtc "server" "db" None parameters (snapshotA ()) with
                | SinkStore.WitnessOutcome.SkippedScoped _ -> ()
                | other -> Assert.Fail (sprintf "scoped read was not gated: %A" other)
            // Nothing was written by any of them.
            Assert.False(Directory.Exists(SinkStore.sinkRoot root) && Directory.EnumerateFileSystemEntries(SinkStore.sinkRoot root) |> Seq.isEmpty |> not))

    [<Fact>]
    let ``store disabled is a named no-op, never a throw`` () =
        match SinkStore.witnessWith None nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ()) with
        | SinkStore.WitnessOutcome.Disabled reason -> Assert.Contains("live-only", reason)
        | other -> Assert.Fail (sprintf "expected Disabled, got %A" other)

    // ------------------------------------------------------------------
    // Witness → journal → replay: the chain.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``witness persists sync 1, journals every appearance, and replay reproduces the canonical snapshot`` () =
        withTempStore (fun root ->
            let outcome = SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ())
            match outcome with
            | SinkStore.WitnessOutcome.Persisted (1, displacements, false) ->
                Assert.Equal(3, displacements) // module + entity + attribute
            | other -> Assert.Fail (sprintf "expected Persisted sync 1, got %A" other)
            let digest = SinkStore.connDigest16 "server" "db"
            let manifest = (SinkStore.loadManifest root digest).Value
            Assert.Equal(1, manifest.LatestSyncId)
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
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            let before = File.ReadAllText(SinkStore.journalPath root digest)
            match SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ()) with
            | SinkStore.WitnessOutcome.Unchanged 1 -> ()
            | other -> Assert.Fail (sprintf "expected Unchanged 1, got %A" other)
            Assert.Equal(before, File.ReadAllText(SinkStore.journalPath root digest)))

    [<Fact>]
    let ``a changed estate witnesses sync 2 with the tombstone displacement journaled and replay tracking it`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            match SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotB ()) with
            | SinkStore.WitnessOutcome.Persisted (2, 1, false) -> ()
            | other -> Assert.Fail (sprintf "expected Persisted sync 2 with 1 displacement, got %A" other)
            let lines = (SinkJournal.load (SinkStore.journalPath root digest)) |> Result.defaultValue []
            let last = List.last lines
            Assert.Equal(2, last.SyncId)
            Assert.Equal(Some 1, last.PrevSyncId)
            let verified = (SinkJournal.admitChain lines) |> Result.defaultWith (fun e -> failwithf "chain refused: %A" e)
            Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                SinkDisplacement.canonical (snapshotB ()),
                SinkJournal.replay verified)
            // The latest snapshot loads SHA-bound; the previous stays loadable.
            Assert.True((SinkStore.loadSnapshotAt root digest 2).IsSome)
            Assert.True((SinkStore.loadSnapshotAt root digest 1).IsSome))

    [<Fact>]
    let ``nameEnvironment stamps the label and later witnesses preserve it`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            Assert.True(SinkStore.nameEnvironment root digest "cloud-uat")
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotB ()) |> ignore
            Assert.Equal(Some "cloud-uat", (SinkStore.loadManifest root digest).Value.EnvLabel))

    // ------------------------------------------------------------------
    // Journal file contract.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``journal: a torn trailing line is tolerated; an interior corrupt line refuses by name`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
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
            { SinkJournal.SyncId = syncId
              SinkJournal.PrevSyncId = None
              SinkJournal.CapturedAtUtc = nowUtc
              SinkJournal.Displacement =
                { Table = SinkDisplacement.SinkTable.Modules
                  KeyText = "espace:800"
                  KeyBasis = SinkDisplacement.KeyBasis.Positional 800
                  Before = None
                  After = Some (SinkDisplacement.SinkRow.Module (OssysSnapshotBuilders.moduleRow 800 "Fulfillment"))
                  Domain = None } }
        match SinkJournal.admitChain [ line 2; line 1 ] with
        | Ok _ -> Assert.Fail "a regressing chain admitted"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.journal.syncRegression")

    [<Fact>]
    let ``journal line codec: a rendered line parses back to the same displacement (domain re-derivable, images exact)`` () =
        let original =
            { SinkJournal.SyncId = 4
              SinkJournal.PrevSyncId = Some 3
              SinkJournal.CapturedAtUtc = nowUtc
              SinkJournal.Displacement =
                { Table = SinkDisplacement.SinkTable.Entities
                  KeyText = "entity:8000"
                  KeyBasis = SinkDisplacement.KeyBasis.Positional 8000
                  Before = Some (SinkDisplacement.SinkRow.Entity (OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER"))
                  After = Some (SinkDisplacement.SinkRow.Entity { OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" with IsActive = false })
                  Domain = Some SinkDisplacement.DomainTransition.EntityDeactivated } }
        match SinkJournal.parseLine (SinkJournal.renderLine original) with
        | Error e -> Assert.Fail (sprintf "line did not parse back: %A" e)
        | Ok parsed ->
            Assert.Equal(original.SyncId, parsed.SyncId)
            Assert.Equal(original.PrevSyncId, parsed.PrevSyncId)
            Assert.Equal(original.CapturedAtUtc, parsed.CapturedAtUtc)
            Assert.Equal(original.Displacement.Table, parsed.Displacement.Table)
            Assert.Equal(original.Displacement.KeyText, parsed.Displacement.KeyText)
            Assert.Equal(original.Displacement.Before, parsed.Displacement.Before)
            Assert.Equal(original.Displacement.After, parsed.Displacement.After)

    // ------------------------------------------------------------------
    // Orphan reconciliation.
    // ------------------------------------------------------------------

    [<Fact>]
    let ``an orphan snapshot (manifest past the journal) reconciles at the next witness, named in its outcome`` () =
        withTempStore (fun root ->
            let digest = SinkStore.connDigest16 "server" "db"
            SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotA ()) |> ignore
            // Simulate the crash window: the journal vanishes; the
            // manifest + snapshot survive.
            File.Delete(SinkStore.journalPath root digest)
            match SinkStore.witnessWith (Some root) nowUtc "server" "db" None MetadataSnapshotRunner.defaultParameters (snapshotB ()) with
            | SinkStore.WitnessOutcome.Persisted (2, 1, true) -> ()
            | other -> Assert.Fail (sprintf "expected reconciling Persisted sync 2, got %A" other)
            // The reconciled journal replays to the latest state whole.
            let lines = (SinkJournal.load (SinkStore.journalPath root digest)) |> Result.defaultValue []
            let verified = (SinkJournal.admitChain lines) |> Result.defaultWith (fun e -> failwithf "chain refused: %A" e)
            Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(
                SinkDisplacement.canonical (snapshotB ()),
                SinkJournal.replay verified))
