module Projection.Tests.LifecycleStoreTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err -> Assert.Fail(sprintf "%A" err); Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private mustStoreOk (r: FsResult<'a, LifecycleStoreError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> Assert.Fail(sprintf "%A" e); Unchecked.defaultof<'a>

let private nameOf (s: string) : Name = Name.create s |> mustResultOk
let private ver (o: int) (l: string) : Version = Version.create o l |> mustResultOk
let private tl (name: string) : Timeline = Timeline.create name |> mustResultOk
let private at (iso: string) : DateTimeOffset = DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture)

let private renamedKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedModule : Module = { salesModule with Kinds = [ renamedKind; order; country ] }
let private targetCatalog : Catalog = IRBuilders.mkCatalog [ renamedModule ]

let private coord o lbl env day = EpisodeCoordinate.create (ver o lbl) env (at (sprintf "2026-06-%02dT09:00:00+00:00" day))

// A durable-faithful chain (every episode carries Profile.empty), promoted
// across environments (dev → qa → named "staging"), exercising the tagged
// Environment round-trip including the `Named` escape hatch.
let private e0 = Episode.ofSchema (coord 0 "1.0.0" Environment.Dev 1) sampleCatalog
let private e1 =
    Episode.create (coord 1 "1.1.0" Environment.Qa 8) targetCatalog Profile.empty (Some "reflog#1") (DataObservation.create 120 (Some "lsn:0x10"))
let private e2 =
    Episode.create (coord 2 "1.2.0" (Environment.Named "staging") 15) targetCatalog Profile.empty None (DataObservation.create 0 None)

let private chain : EpisodicLifecycle =
    EpisodicLifecycle.genesis (tl "dev") e0
    |> (fun lc -> EpisodicLifecycle.append e1 lc |> mustResultOk)
    |> (fun lc -> EpisodicLifecycle.append e2 lc |> mustResultOk)

let private withTempFile (f: string -> unit) : unit =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "lifecycle-%s.json" (Guid.NewGuid().ToString "N"))
    try f path
    finally if System.IO.File.Exists path then System.IO.File.Delete path

// ===========================================================================
// 6.H.2 — the LifecycleStore persists the chain; load reconstructs it
// ===========================================================================

[<Fact>]
let ``6.H.2: save then load round-trips a durable-faithful chain exactly`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        Assert.Equal<EpisodicLifecycle>(chain, loaded))

[<Fact>]
let ``save creates missing parent directories (self-preparing output location, parent grain only)`` () =
    // A store path under a not-yet-existing nested directory (the fresh
    // checkout shape, e.g. `<out>/lifecycle/full-export.json`) must not
    // require manual directory setup; the write itself still fails loudly
    // once the directory exists.
    let root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "lifecycle-nested-%s" (Guid.NewGuid().ToString "N"))
    let path = System.IO.Path.Combine(root, "store", "lifecycle", "full-export.json")
    try
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        Assert.Equal<EpisodicLifecycle>(chain, loaded)
    finally
        if System.IO.Directory.Exists root then System.IO.Directory.Delete(root, true)

[<Fact>]
let ``6.H.2: reconstructLatestSchema over the persisted chain reproduces the stored latest schema (FTC, durable)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let reconstructed = EpisodicLifecycle.reconstructLatestSchema loaded |> mustOk
        // The FTC over the loaded chain reproduces the stored latest schema
        // (modulo the diff's captured surface).
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between targetCatalog reconstructed)))

[<Fact>]
let ``6.H.2: every plane but the Profile survives the round-trip (coordinate, schema, data, refactorlog)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedE1 = EpisodicLifecycle.episodes loaded |> List.item 1
        Assert.Equal("QA", Environment.name loadedE1.Coordinate.Environment)
        Assert.Equal(1, Version.ordinal loadedE1.Coordinate.Version)
        Assert.Equal(at "2026-06-08T09:00:00+00:00", loadedE1.Coordinate.At)
        Assert.Equal(Some "reflog#1", loadedE1.RefactorLogRef)
        Assert.Equal(120, loadedE1.Data.CdcCaptureCount)
        Assert.Equal(Some "lsn:0x10", loadedE1.Data.CdcHandle)
        Assert.Equal<Catalog>(targetCatalog, loadedE1.Schema))

[<Fact>]
let ``6.H.2: the Named environment escape hatch round-trips unambiguously`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedE2 = EpisodicLifecycle.episodes loaded |> List.item 2
        Assert.Equal("staging", Environment.name loadedE2.Coordinate.Environment))

[<Fact>]
let ``6.H.2: the Profile is not persisted — a loaded episode equals its durableProjection`` () =
    withTempFile (fun path ->
        // Save a chain whose genesis carries a non-empty in-memory Profile.
        let profiled =
            { e0 with Profile = { Profile.empty with CdcAwareness = CdcAwareness.create (Set.singleton order.SsKey) Map.empty } }
        let profiledChain =
            EpisodicLifecycle.genesis (tl "dev") profiled
            |> (fun lc -> EpisodicLifecycle.append e1 lc |> mustResultOk)
        LifecycleStore.save path profiledChain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedGenesis = EpisodicLifecycle.head loaded
        // §12.4: the statistical Profile is not durable provenance.
        Assert.Equal<Profile>(Profile.empty, loadedGenesis.Profile)
        // The loaded chain equals the original projected through durableProjection.
        let expected =
            EpisodicLifecycle.genesis (tl "dev") (Episode.durableProjection profiled)
            |> (fun lc -> EpisodicLifecycle.append (Episode.durableProjection e1) lc |> mustResultOk)
        Assert.Equal<EpisodicLifecycle>(expected, loaded))

[<Fact>]
let ``6.H.2: save is byte-deterministic (T1)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let first = System.IO.File.ReadAllBytes path
        LifecycleStore.save path chain |> mustStoreOk
        let second = System.IO.File.ReadAllBytes path
        Assert.Equal<byte[]>(first, second))

// ===========================================================================
// Failure paths — fail-closed, structured errors, never silent
// ===========================================================================

[<Fact>]
let ``6.H.2: loading a non-existent file is a ParseFailure (a lifecycle has no empty value)`` () =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "missing-%s.json" (Guid.NewGuid().ToString "N"))
    match LifecycleStore.load path with
    | FsResult.Error (LifecycleStoreError.ParseFailure _) -> ()
    | other -> Assert.Fail(sprintf "expected ParseFailure, got %A" other)

[<Fact>]
let ``6.H.2: a malformed file is a ParseFailure, never a silent partial load`` () =
    withTempFile (fun path ->
        System.IO.File.WriteAllText(path, "{ not a lifecycle")
        match LifecycleStore.load path with
        | FsResult.Error (LifecycleStoreError.ParseFailure _) -> ()
        | other -> Assert.Fail(sprintf "expected ParseFailure, got %A" other))

[<Fact>]
let ``6.H.2: a corrupt embedded schema fails the load (codec re-validation surfaces)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        // Corrupt the embedded catalog interior: rename each kind's "physical"
        // property (written only by the codec, not the store envelope), so the
        // codec's `field "physical"` fails inside CatalogCodec.deserialize. The
        // store surfaces that as a ParseFailure — codec re-validation reaches
        // through the persistence boundary.
        let text = System.IO.File.ReadAllText path
        let corrupted = text.Replace("\"physical\"", "\"physicalX\"")
        System.IO.File.WriteAllText(path, corrupted)
        match LifecycleStore.load path with
        | FsResult.Error (LifecycleStoreError.ParseFailure _) -> ()
        | other -> Assert.Fail(sprintf "expected ParseFailure from codec re-validation, got %A" other))

// ===========================================================================
// NM-34 — the provenance planes (Tolerances + AppliedTransforms) survive the
// store round-trip. Before this, the writer/reader serialized coordinate / cdc
// / refactorlog but NOT the provenance, so a provenance-bearing episode lost it
// on store round-trip — stored ≠ durableProjection (which KEEPS them).
// ===========================================================================

/// A provenance-bearing chain: the genesis is the bare durable-faithful shape,
/// but the second episode carries BOTH a non-empty tolerance residual and a
/// non-empty applied-transforms overlay enumeration (mixing `Some axis` rows
/// across distinct axes and a skeleton-only `None` row).
let private provenanceTolerances : Tolerance =
    Tolerance.strict
    |> Tolerance.withDivergence ToleratedDivergence.HeaderCommentsOmitted
    |> Tolerance.withDivergence ToleratedDivergence.IndexOptionsUnreflected

let private provenanceApplied : (SsKey * OverlayAxis option) list =
    [ customer.SsKey, Some OverlayAxis.Emission
      customer.SsKey, Some OverlayAxis.Tightening
      order.SsKey,    None ]
    |> List.sort

let private provenanceEpisode : Episode =
    Episode.create (coord 1 "1.1.0" Environment.Qa 8) targetCatalog Profile.empty (Some "reflog#1") (DataObservation.create 120 (Some "lsn:0x10"))
    |> Episode.withProvenance provenanceTolerances provenanceApplied

let private provenanceChain : EpisodicLifecycle =
    EpisodicLifecycle.genesis (tl "dev") e0
    |> (fun lc -> EpisodicLifecycle.append provenanceEpisode lc |> mustResultOk)

[<Fact>]
let ``NM-34: the tolerance residual + applied-transforms survive the store round-trip`` () =
    withTempFile (fun path ->
        LifecycleStore.save path provenanceChain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedE1 = EpisodicLifecycle.episodes loaded |> List.item 1
        Assert.Equal<Tolerance>(provenanceTolerances, loadedE1.Tolerances)
        Assert.Equal<(SsKey * OverlayAxis option) list>(provenanceApplied, loadedE1.AppliedTransforms))

[<Fact>]
let ``NM-34: a provenance-bearing loaded episode equals its own durableProjection (stored = durable)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path provenanceChain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        // `durableProjection` keeps the provenance planes (drops only Profile),
        // so the loaded chain equals the original projected through it. Before
        // NM-34 the load dropped the planes and this equality failed.
        let expected =
            EpisodicLifecycle.genesis (tl "dev") (Episode.durableProjection e0)
            |> (fun lc -> EpisodicLifecycle.append (Episode.durableProjection provenanceEpisode) lc |> mustResultOk)
        Assert.Equal<EpisodicLifecycle>(expected, loaded))

[<Fact>]
let ``NM-34: a pre-provenance store (no tolerances/appliedTransforms fields) loads as strict/empty`` () =
    withTempFile (fun path ->
        // Save the strict/empty `chain` (every episode's provenance defaults to
        // `strict` / `[]`, so the new fields serialize as empty arrays), then
        // remove those exact two trailing fields from each episode object to
        // simulate a store written before NM-34. They are the LAST two fields of
        // each episode, so removing them (and the comma after `data`'s object)
        // leaves valid JSON. The reader must default forward-compatibly, never
        // fail on the absent fields.
        LifecycleStore.save path chain |> mustStoreOk
        let stripped =
            (System.IO.File.ReadAllText path)
                .Replace(",\n      \"tolerances\": [],\n      \"appliedTransforms\": []", "")
        // Sanity: the strip actually removed the fields (guards against a
        // formatting drift silently turning this into a no-op assertion).
        Assert.DoesNotContain("tolerances", stripped)
        Assert.DoesNotContain("appliedTransforms", stripped)
        System.IO.File.WriteAllText(path, stripped)
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedGenesis = EpisodicLifecycle.head loaded
        Assert.True(Tolerance.isStrict loadedGenesis.Tolerances)
        Assert.Empty(loadedGenesis.AppliedTransforms))

[<Fact>]
let ``NM-34: an unknown tolerated-divergence token fails the load (fail-closed, never silently dropped)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path provenanceChain |> mustStoreOk
        let text = System.IO.File.ReadAllText path
        let corrupted = text.Replace("\"HeaderCommentsOmitted\"", "\"NotARealDivergence\"")
        System.IO.File.WriteAllText(path, corrupted)
        match LifecycleStore.load path with
        | FsResult.Error (LifecycleStoreError.ParseFailure _) -> ()
        | other -> Assert.Fail(sprintf "expected ParseFailure on an unknown divergence token, got %A" other))

// -- approved data-correction receipts: the intervention-ledger plane ---------

let private sampleReceipts : DataCorrectionReceipt list =
    [ { CorrectionId = "backfill-customerid"; SourceRemediationId = Some "D1-legacy"
        Subject = AttributeCoordinate.create "Sales" "Account" "CustomerId"
        Derivation = DataCorrectionDerivation.SameRowAttribute
        GuardResults =
            [ DataCorrectionGuardResult.passed DataCorrectionGuard.TargetIsNull (Some 4120L)
              DataCorrectionGuardResult.passed DataCorrectionGuard.SourceIsNotNull None ]
        RowsMatched = 4120L; RowsChanged = 4118L; RowsExcluded = 0L
        BeforeDigest = Some "abc123"; AfterDigest = Some "def456"
        EvidenceColumns = [ AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId" ]; EvidenceDigest = Some "ev123"
        ApprovedBy = Some "operator"; ApprovedAt = Some "2026-07-23" }
      { CorrectionId = "drop-malformed"; SourceRemediationId = None
        Subject = AttributeCoordinate.create "Ops" "Rule" "Id"
        Derivation = DataCorrectionDerivation.ExcludeRows
        GuardResults = [ DataCorrectionGuardResult.passed DataCorrectionGuard.NoFormalInboundReferences None ]
        RowsMatched = 12L; RowsChanged = 0L; RowsExcluded = 12L
        BeforeDigest = Some "ghi789"; AfterDigest = None
        EvidenceColumns = []; EvidenceDigest = None
        ApprovedBy = None; ApprovedAt = None } ]

let private receiptEpisode : Episode =
    Episode.create (coord 1 "1.1.0" Environment.Qa 8) targetCatalog Profile.empty (Some "reflog#1") (DataObservation.create 120 (Some "lsn:0x10"))
    |> Episode.withDataCorrectionReceipts sampleReceipts

let private receiptChain : EpisodicLifecycle =
    EpisodicLifecycle.genesis (tl "dev") e0
    |> (fun lc -> EpisodicLifecycle.append receiptEpisode lc |> mustResultOk)

[<Fact>]
let ``data-correction receipts survive the store round-trip (counts, guards, digests)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path receiptChain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let loadedE1 = EpisodicLifecycle.episodes loaded |> List.item 1
        Assert.Equal<DataCorrectionReceipt list>(DataCorrectionReceipt.sorted sampleReceipts, loadedE1.DataCorrectionReceipts))

[<Fact>]
let ``a receipt-bearing loaded episode equals its own durableProjection (stored = durable)`` () =
    withTempFile (fun path ->
        LifecycleStore.save path receiptChain |> mustStoreOk
        let loaded = LifecycleStore.load path |> mustStoreOk
        let expected =
            EpisodicLifecycle.genesis (tl "dev") (Episode.durableProjection e0)
            |> (fun lc -> EpisodicLifecycle.append (Episode.durableProjection receiptEpisode) lc |> mustResultOk)
        Assert.Equal<EpisodicLifecycle>(expected, loaded))

[<Fact>]
let ``a pre-feature store (no dataCorrectionReceipts field) loads as empty`` () =
    withTempFile (fun path ->
        LifecycleStore.save path chain |> mustStoreOk
        let stripped =
            (System.IO.File.ReadAllText path)
                .Replace(",\n      \"dataCorrectionReceipts\": []", "")
        Assert.DoesNotContain("dataCorrectionReceipts", stripped)
        System.IO.File.WriteAllText(path, stripped)
        let loaded = LifecycleStore.load path |> mustStoreOk
        for e in EpisodicLifecycle.episodes loaded do
            Assert.Empty e.DataCorrectionReceipts)

// -- item 3: --correction-receipts loads recorded receipts for reconcile ------

[<Fact>]
let ``loadCorrectionReceipts reads a receipts file; reconcile matches equal counts and reds on drift`` () =
    withTempFile (fun path ->
        System.IO.File.WriteAllText(path, """{ "dataCorrectionReceipts": [ { "correctionId": "c1", "rowsChanged": 105, "rowsExcluded": 0 } ] }""")
        match FidelityCompareRun.loadCorrectionReceipts path with
        | Error es -> Assert.True(false, sprintf "load failed: %A" es)
        | Ok recorded ->
            Assert.Equal("c1", (List.exactlyOne recorded).CorrectionId)
            Assert.Equal(105L, (List.exactlyOne recorded).RowsChanged)
            let replayedMatch = [ { List.head recorded with RowsChanged = 105L } ]
            Assert.True((match ApprovedDataCorrections.reconcile recorded replayedMatch with Ok () -> true | _ -> false))
            let replayedDrift = [ { List.head recorded with RowsChanged = 104L } ]
            Assert.True((match ApprovedDataCorrections.reconcile recorded replayedDrift with Error _ -> true | _ -> false)))

[<Fact>]
let ``loadCorrectionReceipts refuses a malformed receipts file by name`` () =
    withTempFile (fun path ->
        System.IO.File.WriteAllText(path, """{ "not": "receipts" }""")
        match FidelityCompareRun.loadCorrectionReceipts path with
        | Ok _ -> Assert.True(false, "expected a shape refusal")
        | Error es -> Assert.Equal("fidelity.correctionReceipts.shape", (List.head es).Code))
