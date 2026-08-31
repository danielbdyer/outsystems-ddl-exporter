namespace Projection.Tests

// The sink codec's laws (the data-sink chapter, S3; CHAPTER_SINK_OPEN.md).
// The persisted acquisition witness stands on exactly the CatalogCodec
// law-triple — round-trip totality, byte-determinism, fail-closed decode —
// plus the head-sniffable version key. The FsCheck property runs over
// `OssysSnapshotBuilders.fullyPopulated` (every option Some, every scalar
// non-default, two bits seed-varied), so a codec field-drop — the
// survival-rule-7 reconstruction trap at the 16-record grain — cannot
// round-trip.

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Adapters.OssysSql

module MetadataSnapshotCodecTests =

    [<Property(MaxTest = 40)>]
    let ``sink codec law: deserialize (serialize s) = Ok s over fully-populated snapshots`` (seed: int) =
        let snapshot = OssysSnapshotBuilders.fullyPopulated seed
        match MetadataSnapshotCodec.deserialize (MetadataSnapshotCodec.serialize snapshot) with
        | Ok round -> round = snapshot
        | Error _ -> false

    [<Fact>]
    let ``sink codec law: the empty snapshot round-trips`` () =
        let snapshot = OssysSnapshotBuilders.emptySnapshot
        match MetadataSnapshotCodec.deserialize (MetadataSnapshotCodec.serialize snapshot) with
        | Ok round -> Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(snapshot, round)
        | Error errors -> Assert.Fail (sprintf "empty snapshot failed to round-trip: %A" errors)

    [<Fact>]
    let ``sink codec law: options round-trip None (null is a written value, never an omission)`` () =
        let bareModule =
            { OssysSnapshotBuilders.moduleRow 800 "Fulfillment" with
                EspaceKind = None
                EspaceSsKey = None }
        let bareEntity =
            { OssysSnapshotBuilders.entityRow 8000 800 "Order" "OSUSR_FUL_ORDER" with
                DataKind = None
                PrimaryKeySsKey = None
                EntitySsKey = None
                Description = None }
        let snapshot =
            OssysSnapshotBuilders.snapshotOf [ bareModule ] [ bareEntity ] []
        match MetadataSnapshotCodec.deserialize (MetadataSnapshotCodec.serialize snapshot) with
        | Ok round -> Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(snapshot, round)
        | Error errors -> Assert.Fail (sprintf "None-bearing snapshot failed to round-trip: %A" errors)

    [<Fact>]
    let ``sink codec determinism: serialize is byte-identical across calls (T1)`` () =
        let snapshot = OssysSnapshotBuilders.fullyPopulated 11
        Assert.Equal(
            MetadataSnapshotCodec.serialize snapshot,
            MetadataSnapshotCodec.serialize snapshot)

    [<Fact>]
    let ``sink codec version: codecVersion is the first key (head-sniffable)`` () =
        let text = MetadataSnapshotCodec.serialize OssysSnapshotBuilders.emptySnapshot
        let head = text.Substring(0, min 40 text.Length)
        Assert.Contains("\"codecVersion\": 1", head)

    [<Fact>]
    let ``sink codec fail-closed: garbage is a named error, never an exception`` () =
        match MetadataSnapshotCodec.deserialize "not json at all {{{" with
        | Ok _ -> Assert.Fail "garbage parsed as a snapshot"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.codec.malformedJson")

    [<Fact>]
    let ``sink codec fail-closed: an unknown codecVersion refuses by name`` () =
        let text =
            MetadataSnapshotCodec.serialize OssysSnapshotBuilders.emptySnapshot
        let bumped = text.Replace("\"codecVersion\": 1", "\"codecVersion\": 999")
        match MetadataSnapshotCodec.deserialize bumped with
        | Ok _ -> Assert.Fail "unknown version parsed as a snapshot"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.codec.versionUnsupported")

    [<Fact>]
    let ``sink codec fail-closed: a missing rowset array refuses by name (the witness is total or it is not the witness)`` () =
        let text = MetadataSnapshotCodec.serialize OssysSnapshotBuilders.emptySnapshot
        let withoutTemporal = text.Replace("\"temporal\": []", "\"notTemporal\": []")
        match MetadataSnapshotCodec.deserialize withoutTemporal with
        | Ok _ -> Assert.Fail "a snapshot missing a rowset array parsed"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.codec.missingField")

    [<Fact>]
    let ``sink codec fail-closed: a mistyped field refuses by name`` () =
        let snapshot = OssysSnapshotBuilders.snapshotOf [ OssysSnapshotBuilders.moduleRow 800 "Fulfillment" ] [] []
        let text = MetadataSnapshotCodec.serialize snapshot
        let mistyped = text.Replace("\"espaceId\": 800", "\"espaceId\": \"eight hundred\"")
        match MetadataSnapshotCodec.deserialize mistyped with
        | Ok _ -> Assert.Fail "a mistyped field parsed"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.codec.expectedInt")

    // -- align-II.9 (E2) — the OPTIONAL scope field (R12) --------------------
    // A pre-II.9 stored snapshot (no `scope` key) deserializes with
    // Scope = Total — honest by the totality gate's own invariant (only
    // total acquisitions were ever witnessed); a present scope round-trips
    // (the fully-populated property above varies it by seed); a malformed
    // token fail-closes with the token named. No codecVersion bump.

    [<Fact>]
    let ``sink codec scope (R12): a snapshot document with no scope key deserializes as Total — old stores read back truthfully`` () =
        let text = MetadataSnapshotCodec.serialize OssysSnapshotBuilders.emptySnapshot
        Assert.Contains("\"scope\": \"total\"", text)
        let sixteenField = text.Replace(",\n  \"scope\": \"total\"", "")
        Assert.DoesNotContain("scope", sixteenField)
        match MetadataSnapshotCodec.deserialize sixteenField with
        | Ok round -> Assert.Equal(MetadataSnapshotRunner.AcquisitionScope.Total, round.Scope)
        | Error errors -> Assert.Fail (sprintf "the 16-field document failed to read: %A" errors)

    [<Fact>]
    let ``sink codec scope (R12): a scoped token round-trips and a malformed one refuses by name`` () =
        let scoped =
            { OssysSnapshotBuilders.emptySnapshot with
                Scope =
                    MetadataSnapshotRunner.AcquisitionScope.Scoped
                        [ MetadataSnapshotRunner.ScopeAxis.Modules
                          MetadataSnapshotRunner.ScopeAxis.EntityFilter ] }
        match MetadataSnapshotCodec.deserialize (MetadataSnapshotCodec.serialize scoped) with
        | Ok round -> Assert.Equal<MetadataSnapshotRunner.MetadataSnapshot>(scoped, round)
        | Error errors -> Assert.Fail (sprintf "scoped snapshot failed to round-trip: %A" errors)
        let text = MetadataSnapshotCodec.serialize OssysSnapshotBuilders.emptySnapshot
        let malformed = text.Replace("\"scope\": \"total\"", "\"scope\": \"scoped:nonsense\"")
        match MetadataSnapshotCodec.deserialize malformed with
        | Ok _ -> Assert.Fail "a malformed scope token parsed"
        | Error errors ->
            Assert.Contains(errors, fun (e: ValidationError) -> e.Code = "sink.codec.scopeMalformed")
