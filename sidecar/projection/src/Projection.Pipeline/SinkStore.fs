// LINT-ALLOW-FILE: the sink store (the data-sink chapter S4b; DECISIONS
//   "The data sink chapter opens") — the acquisition witness's durable home.
//   The digest rule, the totality gate, and the manifest codec are pure; the
//   I/O surface (atomic snapshot/manifest saves, fail-closed loads, the
//   journal delegation) is the thin persistence boundary the LiveModelRead
//   witness hook and the sync verb call. Composes machine-local file paths
//   and structured JSON at that boundary (the EstateEvidenceStore precedent).
namespace Projection.Pipeline

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Projection.Core
open Projection.Adapters.OssysSql

/// The sink store (S4b): `<store>/sink/<connDigest16>/` — per-source
/// witnessed `MetadataSnapshot`s, an append-only displacement journal, and
/// a manifest naming the latest sync. Keyed by a credential-rotation-
/// invariant connection digest (the witness hook knows the connection, not
/// the environment); the manifest carries the operator's `EnvLabel` once
/// `projection sync <env>` names it — the sync verb is the act that makes
/// an environment addressable by name.
///
/// **The totality gate.** Only `defaultParameters`-shaped acquisitions
/// become sink states and journal entries; a scoped read skips witnessing
/// with a named outcome (a scoped read diffed against a total predecessor
/// would fabricate removals). **The policy lever is elsewhere**: freshness
/// (`sink.policy`) governs probe/reuse only — store presence is the one
/// witness gate (DECISIONS ruling R2).
///
/// **Failure posture.** Saves are atomic (tmp + move) and ADVISORY — a
/// store write failure warns and never fails a read; loads are fail-closed
/// (a torn or foreign file reads as absent/refused, never as data).
[<RequireQualifiedAccess>]
module SinkStore =

    // ------------------------------------------------------------------
    // Identity: the connection digest.
    // ------------------------------------------------------------------

    /// SHA-256 (first 16 hex, lowercase) over the normalized
    /// (DataSource, InitialCatalog) pair — trim + invariant-lowercase, a
    /// NUL separator so ("ab","c") never collides with ("a","bc").
    /// Credentials and every other connection option stay OUT: rotation
    /// must not fork the store.
    let connDigest16 (dataSource: string) (database: string) : string =
        let normalize (s: string) = s.Trim().ToLowerInvariant()
        let basis = String.Concat(normalize dataSource, "\u0000", normalize database) // LINT-ALLOW: terminal digest-basis composition at the persistence boundary — two normalized segments and a NUL separator feeding SHA-256; the bytes are the carrier, no rendered text exists to build with a typed writer
        use sha = SHA256.Create()
        let hash = sha.ComputeHash(Encoding.UTF8.GetBytes basis)
        let hex = Convert.ToHexString(hash).ToLowerInvariant()
        hex.Substring(0, 16)

    // ------------------------------------------------------------------
    // Layout.
    // ------------------------------------------------------------------

    let sinkRoot (storeRoot: string) : string = Path.Combine(storeRoot, "sink")
    let sourceDir (storeRoot: string) (digest: string) : string = Path.Combine(sinkRoot storeRoot, digest)
    let manifestPath (storeRoot: string) (digest: string) : string = Path.Combine(sourceDir storeRoot digest, "manifest.json")
    let journalPath (storeRoot: string) (digest: string) : string = Path.Combine(sourceDir storeRoot digest, SinkJournal.fileName)
    let snapshotPath (storeRoot: string) (digest: string) (syncId: SyncOrdinal) : string =
        Path.Combine(sourceDir storeRoot digest, "snapshots", SyncOrdinal.text syncId, "snapshot.json")

    // ------------------------------------------------------------------
    // The manifest.
    // ------------------------------------------------------------------

    /// align-II.11 (E6; audit a7) — one freshness bellwether's reading,
    /// TYPED: the three explicit measurements the probe takes. The
    /// manifest persists them as fields (codec idiom: absent terms as
    /// null), so comparison is field-wise and a miss can name WHICH AXES
    /// moved — the packed `count|maxPk|content` string this replaces
    /// interned the three readings into one opaque token.
    type FingerprintReading =
        {
            RowCount : int64
            MaxPk    : string option
            Content  : string option
        }

    /// The axes a fingerprint comparison can move on.
    [<RequireQualifiedAccess>]
    type FingerprintAxis =
        | Rows
        | MaxPk
        | Content

    [<RequireQualifiedAccess>]
    module FingerprintReading =

        /// The S8 packed display/legacy-wire form (`count|maxPk|content`,
        /// absent terms as `-`).
        let packed (r: FingerprintReading) : string =
            sprintf "%d|%s|%s" r.RowCount (defaultArg r.MaxPk "-") (defaultArg r.Content "-")

        /// The packed form's inverse — pre-II.11 manifests persisted the
        /// packed string; parsing it keeps every old store readable with
        /// no re-witnessing. `None` for a token the packed grammar never
        /// produced (the reader then skips the entry — the safe direction:
        /// `auto` reads live).
        let tryParsePacked (raw: string) : FingerprintReading option =
            match raw.Split '|' with
            | [| count; maxPk; content |] ->
                match System.Int64.TryParse count with
                | true, n ->
                    Some
                        { RowCount = n
                          MaxPk = (if maxPk = "-" then None else Some maxPk)
                          Content = (if content = "-" then None else Some content) }
                | _ -> None
            | _ -> None

        /// Field-wise comparison, the moved axes named. Trivial equality
        /// per axis — the reading is three explicit fields, so "what
        /// moved" is a projection, never a string diff.
        let movedAxes (recorded: FingerprintReading) (current: FingerprintReading) : FingerprintAxis list =
            [ if recorded.RowCount <> current.RowCount then FingerprintAxis.Rows
              if recorded.MaxPk <> current.MaxPk then FingerprintAxis.MaxPk
              if recorded.Content <> current.Content then FingerprintAxis.Content ]

    type Manifest =
        {
            ConnDigest     : string
            /// Typed at align-III.1: manifests always name a real edition
            /// (the first witness mints the genesis ordinal), so this is
            /// never optional — and never 0. The wire stays a raw int and
            /// re-mints fail-closed on read.
            LatestSyncId   : SyncOrdinal
            EnvLabel       : string option
            SourceDataSource : string
            SourceDatabase : string
            CapturedAtUtc  : DateTimeOffset
            /// SHA-256 over the latest snapshot's serialized text — the
            /// torn-write detector (the estate sidecar's binding rule).
            SnapshotSha256 : string
            /// The freshness bellwethers recorded AT witness time (S8;
            /// TYPED at align-II.11): per-target readings over the three
            /// ossys tables, matched field-wise by `SinkFreshness.decide`
            /// under `auto`. `[]` when the capture-time probe failed or
            /// the state predates S8 — `auto` then always reads live (the
            /// safe direction; a pin never consults these).
            SourceFingerprints : (string * FingerprintReading) list
        }

    let private sha256Text (text: string) : string =
        use sha = SHA256.Create()
        Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes text)).ToLowerInvariant()

    let private manifestJson (m: Manifest) : string =
        JsonWriting.writeToString (fun jw ->
            jw.WriteStartObject()
            jw.WriteString("connDigest", m.ConnDigest)
            jw.WriteNumber("latestSyncId", SyncOrdinal.value m.LatestSyncId)
            (match m.EnvLabel with
             | Some l -> jw.WriteString("envLabel", l)
             | None -> jw.WriteNull("envLabel"))
            jw.WriteString("sourceDataSource", m.SourceDataSource)
            jw.WriteString("sourceDatabase", m.SourceDatabase)
            jw.WriteString("capturedAtUtc", m.CapturedAtUtc.ToString("O", Globalization.CultureInfo.InvariantCulture))
            jw.WriteString("snapshotSha256", m.SnapshotSha256)
            jw.WriteStartArray("sourceFingerprints")
            for (target, reading) in m.SourceFingerprints do
                jw.WriteStartObject()
                jw.WriteString("target", target)
                // align-II.11 — three explicit fields (absent as null);
                // pre-II.11 readers are not a supported direction, and
                // pre-II.11 WRITERS' packed `fingerprint` strings still
                // read (the parse below).
                jw.WriteNumber("rowCount", reading.RowCount)
                (match reading.MaxPk with
                 | Some v -> jw.WriteString("maxPk", v)
                 | None -> jw.WriteNull("maxPk"))
                (match reading.Content with
                 | Some v -> jw.WriteString("content", v)
                 | None -> jw.WriteNull("content"))
                jw.WriteEndObject()
            jw.WriteEndArray()
            jw.WriteEndObject())

    let private tryParseManifest (text: string) : Manifest option =
        try
            use doc = JsonDocument.Parse text
            let root = doc.RootElement
            let strOf (name: string) =
                match root.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.String ->
                    match v.GetString() with
                    | null -> None
                    | s -> Some s
                | _ -> None
            let intOf (name: string) =
                match root.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.Number -> Some (v.GetInt32())
                | _ -> None
            // align-III.1 — the ordinal re-mints fail-closed: a manifest
            // naming sync 0 (or below) is torn/foreign and reads as absent,
            // exactly like every other undecodable field here.
            let ordinalOf (name: string) =
                intOf name
                |> Option.bind (fun n ->
                    match SyncOrdinal.create n with
                    | Ok o -> Some o
                    | Error _ -> None)
            let optStrOf (name: string) =
                match root.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.String ->
                    match v.GetString() with
                    | null -> None
                    | s -> Some (Some s)
                | true, v when v.ValueKind = JsonValueKind.Null -> Some None
                | _ -> None
            // Total over older manifests: a state witnessed before S8
            // carries no `sourceFingerprints` array and reads as [] (auto
            // then always reads live — the safe direction).
            let fingerprints =
                match root.TryGetProperty "sourceFingerprints" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for e in arr.EnumerateArray() do
                        if e.ValueKind = JsonValueKind.Object then
                            let field (name: string) =
                                match e.TryGetProperty name with
                                | true, v when v.ValueKind = JsonValueKind.String ->
                                    match v.GetString() with null -> None | s -> Some s
                                | _ -> None
                            let optField (name: string) : string option =
                                match e.TryGetProperty name with
                                | true, v when v.ValueKind = JsonValueKind.String ->
                                    match v.GetString() with null -> None | s -> Some s
                                | _ -> None
                            let rowCount =
                                match e.TryGetProperty "rowCount" with
                                | true, v when v.ValueKind = JsonValueKind.Number -> Some (v.GetInt64())
                                | _ -> None
                            // align-II.11: typed fields when present; the
                            // pre-II.11 packed `fingerprint` string parses
                            // through the one inverse; an entry neither
                            // form explains is skipped (the safe
                            // direction — `auto` reads live).
                            match field "target", rowCount with
                            | Some t, Some n ->
                                yield t, ({ RowCount = n; MaxPk = optField "maxPk"; Content = optField "content" } : FingerprintReading)
                            | Some t, None ->
                                match field "fingerprint" |> Option.bind FingerprintReading.tryParsePacked with
                                | Some reading -> yield t, reading
                                | None -> ()
                            | _ -> () ]
                | _ -> []
            match strOf "connDigest", ordinalOf "latestSyncId", optStrOf "envLabel", strOf "sourceDataSource", strOf "sourceDatabase", strOf "capturedAtUtc", strOf "snapshotSha256" with
            | Some digest, Some latest, Some envLabel, Some ds, Some db, Some at, Some sha ->
                match DateTimeOffset.TryParse(at, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
                | true, dto ->
                    Some
                        { ConnDigest = digest
                          LatestSyncId = latest
                          EnvLabel = envLabel
                          SourceDataSource = ds
                          SourceDatabase = db
                          CapturedAtUtc = dto
                          SnapshotSha256 = sha
                          SourceFingerprints = fingerprints }
                | _ -> None
            | _ -> None
        with _ -> None

    /// The manifest codec, exposed pure (align-II.11: the wire laws —
    /// typed-field round-trip + packed-string legacy read — pin it).
    let manifestJsonText (m: Manifest) : string = manifestJson m
    let tryParseManifestText (text: string) : Manifest option = tryParseManifest text

    // ------------------------------------------------------------------
    // Atomic write / fail-closed read primitives (estate idioms).
    // ------------------------------------------------------------------

    let private writeAtomic (path: string) (text: string) : unit =
        (match Path.GetDirectoryName path with
         | null -> ()
         | "" -> ()
         | dir -> Directory.CreateDirectory dir |> ignore)
        let tmp = path + ".tmp" // LINT-ALLOW: terminal temp-file path suffix at the atomic-write boundary — the path and a fixed extension are the irreducible primitive; Path.ChangeExtension would clobber the real extension the move restores
        File.WriteAllText(tmp, text)
        File.Move(tmp, path, true)

    let private tryReadAllText (path: string) : string option =
        try
            if File.Exists path then Some (File.ReadAllText path) else None
        with
        | :? IOException | :? UnauthorizedAccessException -> None

    /// Fail-closed manifest read: absent, unreadable, or unparseable reads
    /// as None.
    let loadManifest (storeRoot: string) (digest: string) : Manifest option =
        tryReadAllText (manifestPath storeRoot digest)
        |> Option.bind tryParseManifest

    /// Fail-closed snapshot read, SHA-bound when the manifest names this
    /// sync: a torn write reads as absent, never as data.
    let loadSnapshotAt (storeRoot: string) (digest: string) (syncId: SyncOrdinal) : MetadataSnapshotRunner.MetadataSnapshot option =
        tryReadAllText (snapshotPath storeRoot digest syncId)
        |> Option.bind (fun text ->
            let shaBound =
                match loadManifest storeRoot digest with
                | Some m when m.LatestSyncId = syncId -> m.SnapshotSha256 = sha256Text text
                | _ -> true
            if not shaBound then None
            else
                match MetadataSnapshotCodec.deserialize text with
                | Ok snapshot -> Some snapshot
                | Error _ -> None)

    let loadLatest (storeRoot: string) (digest: string) : (Manifest * MetadataSnapshotRunner.MetadataSnapshot) option =
        loadManifest storeRoot digest
        |> Option.bind (fun m ->
            loadSnapshotAt storeRoot digest m.LatestSyncId
            |> Option.map (fun s -> m, s))

    /// Every manifest under the sink root — the env-label scan surface
    /// (S7's `sink:<env>` resolution; offline-true, config-free).
    let manifests (storeRoot: string) : Manifest list =
        let root = sinkRoot storeRoot
        if not (Directory.Exists root) then []
        else
            Directory.EnumerateDirectories root
            |> Seq.choose (fun dir ->
                match Path.GetFileName dir with
                | null | "" -> None
                | digest -> loadManifest storeRoot digest)
            |> Seq.sortBy (fun m -> m.ConnDigest)
            |> Seq.toList

    // ------------------------------------------------------------------
    // The witness.
    // ------------------------------------------------------------------

    /// Why a read did not become a sink state — every skip is named.
    [<RequireQualifiedAccess>]
    type WitnessOutcome =
        /// A new sync landed: snapshot + journal lines + manifest. The
        /// outcome names WHICH edition landed (align-III.1: the digest ×
        /// ordinal pair travels as the `SinkEdition` carrier).
        | Persisted of edition: SinkEdition * displacements: int * reconciledOrphan: bool
        /// The estate is byte-identical to the latest witnessed state —
        /// nothing written (the metadata plane's CDC-silence); the edition
        /// it still equals, named.
        | Unchanged of edition: SinkEdition
        /// A scoped acquisition — witnessing refused by the totality gate;
        /// the axes it narrowed on, named (align-II.9: the scope is a
        /// value, so the skip carries WHICH narrowing fired the gate).
        | SkippedScoped of axes: MetadataSnapshotRunner.ScopeAxis list
        /// No store root resolved — live-only, named.
        | Disabled of reason: string
        /// The store was reachable but a write failed — ADVISORY; the
        /// read that produced the snapshot proceeds untouched.
        | Failed of code: string * message: string

    /// The totality gate, pure: only the show-me-everything shape becomes
    /// a sink state. align-II.9 — the gate READS THE TYPE: the one scope
    /// classifier decides (the `parameters = defaultParameters` structural
    /// equality it replaces is the same predicate, law-pinned).
    let isTotalAcquisition (parameters: MetadataSnapshotRunner.SnapshotParameters) : bool =
        MetadataSnapshotRunner.AcquisitionScope.ofParameters parameters = MetadataSnapshotRunner.AcquisitionScope.Total

    /// Witness one acquisition. Resolves the store internally
    /// (`EstateStoreLocation.storeDir()`); gates on totality; canonicalizes;
    /// diffs against the latest witnessed state; persists snapshot →
    /// journal → manifest (the manifest is the commit point — a crash
    /// before it leaves an orphan snapshot the NEXT witness reconciles by
    /// re-differencing, named in its outcome).
    let witnessWith
        (storeRoot: string option)
        (nowUtc: DateTimeOffset)
        (dataSource: string)
        (database: string)
        (envLabel: string option)
        (fingerprints: (string * FingerprintReading) list)
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        (snapshot: MetadataSnapshotRunner.MetadataSnapshot)
        : WitnessOutcome =
        match storeRoot with
        | None ->
            WitnessOutcome.Disabled "no store root (PROJECTION_ESTATE_DIR / PROJECTION_LEDGER_DIR unset) — live-only"
        | Some root ->
            match MetadataSnapshotRunner.AcquisitionScope.ofParameters parameters with
            | MetadataSnapshotRunner.AcquisitionScope.Scoped axes ->
                // The totality gate: only a total read becomes a sink
                // state — the skip names the axes that narrowed this one.
                WitnessOutcome.SkippedScoped axes
            | MetadataSnapshotRunner.AcquisitionScope.Total ->
                try
                    let digest = connDigest16 dataSource database
                    // The snapshot persists EXACTLY as acquired — the
                    // source-shaped witness (S7's K2 parity ruling: a sink
                    // read replays THE acquisition, byte-for-byte at the
                    // rowset grain, so `parse` sees the same rows in the
                    // same wire order the live read saw). Canonical
                    // (key-sorted) order is the DIFF algebra's internal
                    // normal form — `diff` is Map-keyed and order-blind,
                    // and `applyAll`/replay produce canonical states — so
                    // nothing here needs to canonicalize at rest.
                    let previousManifest = loadManifest root digest
                    let previous =
                        previousManifest
                        |> Option.bind (fun m -> loadSnapshotAt root digest m.LatestSyncId |> Option.map (fun s -> m, s))
                    let previousSnapshot =
                        previous |> Option.map snd |> Option.defaultValue SinkDisplacement.emptySnapshot
                    // align-III.1 — "nothing witnessed yet" is the option,
                    // never a magic 0: both fabrication sites below carry
                    // it directly as the predecessor link.
                    let previousOrdinal =
                        previous |> Option.map (fun (m, _) -> m.LatestSyncId)
                    // Orphan reconciliation: a manifest pointing past the
                    // journal's last sync means a crash landed between the
                    // manifest and the journal in some earlier ordering, or
                    // a snapshot exists whose lines never fsync'd. Re-diff
                    // the stored chain and append the missing lines before
                    // witnessing the new state.
                    let journalFile = journalPath root digest
                    let journalLines =
                        match SinkJournal.load journalFile with
                        | Ok lines -> lines
                        | Error _ -> []
                    let journalLast = SinkJournal.lastSyncId journalLines
                    let journalBehind =
                        match previousOrdinal, journalLast with
                        | Some _, None -> true
                        | Some prev, Some jl -> jl < prev
                        | None, _ -> false
                    let reconciled =
                        match previousOrdinal with
                        | Some prev when journalBehind ->
                            let fromSnapshot =
                                match journalLast with
                                | None -> SinkDisplacement.emptySnapshot
                                | Some jl -> loadSnapshotAt root digest jl |> Option.defaultValue SinkDisplacement.emptySnapshot
                            let missing = SinkDisplacement.diff fromSnapshot previousSnapshot
                            let lines =
                                missing
                                |> List.map (fun d ->
                                    { SinkJournal.SyncId = prev
                                      SinkJournal.PrevSyncId = journalLast
                                      SinkJournal.CapturedAtUtc = nowUtc
                                      SinkJournal.Displacement = d })
                            match SinkJournal.append journalFile lines with
                            | Ok () -> true
                            | Error _ -> false
                        | _ -> false
                    let displacements = SinkDisplacement.diff previousSnapshot snapshot
                    match displacements, previous with
                    | [], Some (previousM, _) ->
                        // CDC-silence — nothing journaled. The freshness
                        // recording still RE-ANCHORS when the probe moved
                        // while the projected state did not (a mutation in a
                        // column the rowsets never read): the state is
                        // confirmed identical, so refreshing the bellwethers
                        // keeps `auto` from degenerating into always-live
                        // for such estates. A failed probe ([]) never
                        // clobbers good recordings.
                        (if not (List.isEmpty fingerprints) && fingerprints <> previousM.SourceFingerprints then
                            writeAtomic (manifestPath root digest) (manifestJson { previousM with SourceFingerprints = fingerprints }))
                        WitnessOutcome.Unchanged { ConnDigest = digest; Ordinal = previousM.LatestSyncId }
                    | _ ->
                        let syncId = SyncOrdinal.next previousOrdinal
                        let text = MetadataSnapshotCodec.serialize snapshot
                        writeAtomic (snapshotPath root digest syncId) text
                        let lines =
                            displacements
                            |> List.map (fun d ->
                                { SinkJournal.SyncId = syncId
                                  SinkJournal.PrevSyncId = previousOrdinal
                                  SinkJournal.CapturedAtUtc = nowUtc
                                  SinkJournal.Displacement = d })
                        match SinkJournal.append journalFile lines with
                        | Error errors ->
                            let message =
                                errors
                                |> List.map (fun e -> e.Message)
                                |> String.concat "; " // LINT-ALLOW: terminal advisory-diagnostic aggregation at the persistence boundary — joining already-rendered error messages for one advisory outcome string; no typed carrier exists downstream of this dead end
                            WitnessOutcome.Failed ("sink.writeFailed", message)
                        | Ok () ->
                            let manifest =
                                { ConnDigest = digest
                                  LatestSyncId = syncId
                                  EnvLabel =
                                    match envLabel with
                                    | Some _ -> envLabel
                                    | None -> previousManifest |> Option.bind (fun m -> m.EnvLabel)
                                  SourceDataSource = dataSource
                                  SourceDatabase = database
                                  CapturedAtUtc = nowUtc
                                  SnapshotSha256 = sha256Text text
                                  SourceFingerprints = fingerprints }
                            writeAtomic (manifestPath root digest) (manifestJson manifest)
                            WitnessOutcome.Persisted ({ ConnDigest = digest; Ordinal = syncId }, List.length displacements, reconciled)
                with ex ->
                    WitnessOutcome.Failed ("sink.writeFailed", ex.Message)

    /// The boundary-resolving entry the witness hook calls. `fingerprints`
    /// is the hook's capture-time freshness probe (S8) — `[]` when the
    /// probe was skipped or failed (auto then reads live; safe direction).
    let witness
        (nowUtc: DateTimeOffset)
        (dataSource: string)
        (database: string)
        (fingerprints: (string * FingerprintReading) list)
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        (snapshot: MetadataSnapshotRunner.MetadataSnapshot)
        : WitnessOutcome =
        witnessWith (EstateStoreLocation.storeDir ()) nowUtc dataSource database None fingerprints parameters snapshot

    /// Stamp the operator's environment label onto a source's manifest —
    /// the sync verb's naming act.
    let nameEnvironment (storeRoot: string) (digest: string) (label: string) : bool =
        match loadManifest storeRoot digest with
        | None -> false
        | Some m ->
            writeAtomic (manifestPath storeRoot digest) (manifestJson { m with EnvLabel = Some label })
            true
