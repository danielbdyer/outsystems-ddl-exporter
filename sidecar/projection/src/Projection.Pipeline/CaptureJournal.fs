namespace Projection.Pipeline

// LINT-ALLOW-FILE: journal persistence boundary — NDJSON via
//   System.Text.Json (the typed-AST library for the format) over an
//   append-only file; the digest-derived file name is terminal text.

open System.Collections.Generic
open System.IO
open System.Text.Json
open Projection.Core

/// One completed chunk of one kind's streaming load, as the journal
/// records it: the chunk's SOURCE fingerprint (first/last source PK +
/// raw row count — resume validity requires the source unchanged, and
/// the fingerprint is how drift refuses by name), the written count,
/// and the chunk's captured `(source → assigned)` pairs (empty for the
/// minted-bulk and preserved lanes — neither produces a remap consumer).
type ChunkRecord =
    {
        Kind         : string
        ChunkIx      : int
        FirstPk      : string
        LastPk       : string
        RawCount     : int
        WrittenCount : int
        Pairs        : string[][]
    }

/// The chunk-resume journal — the CLIENT-SIDE durable record of a
/// streaming transfer's progress. Why client-side: the sink is
/// `grant: data` (DML-only), so a sink-resident progress table would
/// need CREATE TABLE the grant forbids; the journal is the engine's own
/// ledger (D9 posture — operational state lives out of band). One
/// append-only NDJSON file per (directory, plan marker); each line is a
/// `ChunkRecord`. On resume, a journaled chunk whose fingerprint matches
/// is SKIPPED (its pairs rebuild the remap); a fingerprint mismatch is
/// the named `transfer.resume.sourceDrift` refusal, never a silent
/// re-run over changed data.
type CaptureJournal =
    private
        {
            FilePath : string
        }

/// The MEMORY-LEAN resume index over a `CaptureJournal`. The resume run holds
/// this for its whole duration alongside the live remap, so it deliberately does
/// NOT retain the journal's bulk (the per-chunk captured pairs — `O(minted keys)`
/// for an estate-scale `AssignedBySink` load). Instead it maps each
/// `(kind root, chunk index)` to the BYTE OFFSET of its NDJSON line; the one
/// chunk being admitted is re-read + parsed on demand (`tryFindRecord`). Peak is
/// `O(journal keys)` offsets, not `O(journal pairs)` arrays — the prior
/// `CaptureJournal.load` Dictionary held every record's pairs at once.
type ResumeIndex =
    private
        {
            IndexFilePath : string
            Offsets       : System.Collections.Generic.Dictionary<string * int, int64>
        }

[<RequireQualifiedAccess>]
module CaptureJournal =

    let private digestOf (marker: string) : string =
        let bytes = System.Text.Encoding.UTF8.GetBytes marker
        System.Security.Cryptography.SHA256.HashData bytes
        |> System.Convert.ToHexString
        |> fun h -> h.Substring(0, 16).ToLowerInvariant()

    /// Open (or create) the journal for one plan marker under the given
    /// directory. The marker keys the file, so two different transfers
    /// never share a journal and a re-run of the SAME transfer finds its
    /// own progress.
    let create (directory: string) (marker: string) : CaptureJournal =
        Directory.CreateDirectory directory |> ignore
        { FilePath = Path.Combine(directory, sprintf "transfer-%s.ndjson" (digestOf marker)) }

    let filePath (journal: CaptureJournal) : string = journal.FilePath

    /// Open an EXISTING journal by its file path — the second consumer's
    /// door (2026-07-15, fidelity wave B4b): the row-fidelity comparator
    /// replays a journal the operator names directly (the transfer's
    /// `--journal` directory holds `transfer-<digest>.ndjson`), where the
    /// (directory, marker) pair `create` derives the path from is not in
    /// hand. Read-side only — writers keep arriving through `create`, so
    /// the marker-keyed addressing (and its drift guard) stays the one
    /// write-path rule.
    let ofFile (path: string) : CaptureJournal =
        { FilePath = path }

    /// Phase 3 — the address-drift guard. The journal is addressed by the
    /// plan-marker digest (`transfer-<digest>.ndjson`), so if the marker
    /// changes between a crashed run and its resume — a `planMarker`/schema
    /// byte-change — THIS run's file is absent yet the directory still holds
    /// the prior run's `transfer-*.ndjson`. Left unguarded that silently
    /// orphans the prior journal and starts fresh (re-doing, or under
    /// AssignedBySink DOUBLING, committed work). This returns the sibling
    /// journals that signal the drift: NON-EMPTY only when THIS journal's own
    /// file is ABSENT (a would-be fresh run) AND other `transfer-*.ndjson`
    /// exist beside it. Empty when the own file is present (a clean resume) or
    /// the directory is genuinely empty (a true fresh run). The streaming
    /// execute refuses by name on a non-empty result rather than orphaning
    /// the prior journal — the silence the risk register names is killed.
    let siblingJournalsUnderDrift (journal: CaptureJournal) : string list =
        if File.Exists journal.FilePath then []
        else
            match Path.GetDirectoryName journal.FilePath with
            | null | "" -> []
            | dir when not (Directory.Exists dir) -> []
            | dir ->
                Directory.GetFiles(dir, "transfer-*.ndjson")
                |> Array.filter (fun f -> not (System.String.Equals(f, journal.FilePath, System.StringComparison.OrdinalIgnoreCase)))
                |> Array.sort
                |> Array.toList

    /// Every journaled chunk, indexed by (kind root, chunk index). A
    /// missing or empty journal is an empty index (a fresh run).
    ///
    /// Torn-trailing-line tolerance (2026-07-09): a crash mid-append can leave a
    /// half-written FINAL line with NO closing newline (the append fsyncs, but a
    /// kill between the write and the fsync — or a torn multi-block record — is
    /// still possible at EOF). Such a partial trailing line is SKIPPED, not thrown
    /// on, so a hard crash never wedges resume/revert behind a hand-truncation.
    /// A newline-TERMINATED line is complete: a corrupt one throws even when last
    /// (real corruption, not an expected mid-crash partial) — the tolerance is
    /// keyed on the missing trailing newline, matching `openResumeIndex`'s
    /// byte-scan, never on mere position.
    let load (journal: CaptureJournal) : Dictionary<string * int, ChunkRecord> =
        let index = Dictionary<string * int, ChunkRecord>()
        if File.Exists journal.FilePath then
            let text = File.ReadAllText journal.FilePath
            if text.Length > 0 then
                // A trailing newline means the final record is complete; its
                // absence means the last non-empty line is a torn partial.
                let endsClean = text.EndsWith "\n"
                let lines = text.Split '\n'
                let lastIx = lines.Length - 1
                lines
                |> Array.iteri (fun i line ->
                    if not (System.String.IsNullOrWhiteSpace line) then
                        let isTornTrailing = i = lastIx && not endsClean
                        let recordOpt =
                            try
                                match JsonSerializer.Deserialize<ChunkRecord> line with
                                | null   -> None
                                | record -> Some record
                            with _ when isTornTrailing -> None   // torn partial — skip, do not wedge
                        match recordOpt with
                        | Some record -> index[(record.Kind, record.ChunkIx)] <- record
                        | None        -> ())
        index

    /// The NDJSON line separator (the `append` writes `Serialize record + "\n"`).
    [<Literal>]
    let private NewlineByte : byte = 0x0Auy

    /// Read the single NDJSON line that STARTS at `offset` (up to the next
    /// newline or EOF), decoded as UTF-8. Used by `tryFindRecord` to parse one
    /// record's bytes on demand — the only place a record's (potentially large)
    /// pairs are materialized on the resume path.
    let private readLineAt (path: string) (offset: int64) : string =
        use fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        fs.Seek(offset, SeekOrigin.Begin) |> ignore
        let acc = System.Collections.Generic.List<byte>()
        let buf = Array.zeroCreate<byte> 65536
        let mutable finished = false
        while not finished do
            let n = fs.Read(buf, 0, buf.Length)
            if n = 0 then finished <- true
            else
                match System.Array.IndexOf(buf, NewlineByte, 0, n) with
                | -1 -> acc.AddRange(System.ArraySegment(buf, 0, n))
                | nl ->
                    acc.AddRange(System.ArraySegment(buf, 0, nl))
                    finished <- true
        System.Text.Encoding.UTF8.GetString(acc.ToArray())

    /// Build the memory-lean `ResumeIndex`: a single byte-level scan that maps
    /// each `(kind root, chunk index)` to its line's START offset WITHOUT
    /// retaining the parsed record (only enough is deserialized to read the key).
    /// A re-appended key takes the LATEST offset (last-write-wins — mirrors
    /// `load`). Blank / literal-`null` lines are skipped. An INTERIOR corrupt line
    /// (newline-terminated, so complete) throws — the not-silently-lossy contract.
    /// A torn TRAILING line (a mid-crash partial at EOF, with no final newline) is
    /// SKIPPED, not thrown on, so a hard crash never wedges resume behind a
    /// hand-truncation (2026-07-09). A missing journal is an empty index (fresh run).
    let openResumeIndex (journal: CaptureJournal) : ResumeIndex =
        let offsets = System.Collections.Generic.Dictionary<string * int, int64>()
        if File.Exists journal.FilePath then
            use fs = new FileStream(journal.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read)
            let buf = Array.zeroCreate<byte> 65536
            let lineBytes = System.Collections.Generic.List<byte>()
            let mutable lineStart = 0L
            let mutable bufStart = 0L
            // `tolerant` distinguishes an interior line (newline-terminated →
            // complete → strict) from the trailing line (no final newline → a
            // possible mid-crash partial → skip on parse failure).
            let recordLine (tolerant: bool) =
                if lineBytes.Count > 0 then
                    let line = System.Text.Encoding.UTF8.GetString(lineBytes.ToArray())
                    if not (System.String.IsNullOrWhiteSpace line) then
                        let recordOpt =
                            try
                                match JsonSerializer.Deserialize<ChunkRecord> line with
                                | null   -> None
                                | record -> Some record
                            with _ when tolerant -> None
                        match recordOpt with
                        | Some record -> offsets[(record.Kind, record.ChunkIx)] <- lineStart
                        | None        -> ()
                lineBytes.Clear()
            let mutable read = fs.Read(buf, 0, buf.Length)
            while read > 0 do
                for i in 0 .. read - 1 do
                    if buf.[i] = NewlineByte then
                        recordLine false
                        lineStart <- bufStart + int64 i + 1L
                    else
                        lineBytes.Add buf.[i]
                bufStart <- bufStart + int64 read
                read <- fs.Read(buf, 0, buf.Length)
            // The trailing line has no final newline (the append always writes one,
            // so this is normally empty) — tolerate a torn/hand-truncated partial.
            recordLine true
        { IndexFilePath = journal.FilePath; Offsets = offsets }

    /// Resolve one journaled chunk by `(kind root, chunk index)`, re-reading and
    /// parsing its line at the indexed offset. `None` when the key is absent (a
    /// fresh chunk past the crash point). The pairs are materialized HERE, for
    /// the one chunk being admitted — never the whole journal at once.
    let tryFindRecord (index: ResumeIndex) (kindRoot: string) (chunkIx: int) : ChunkRecord option =
        match index.Offsets.TryGetValue((kindRoot, chunkIx)) with
        | false, _     -> None
        | true, offset ->
            match JsonSerializer.Deserialize<ChunkRecord> (readLineAt index.IndexFilePath offset) with
            | null   -> None
            | record -> Some record

    /// Append one completed chunk, FSYNC'd before returning (`Flush(true)`), so
    /// the record is durable on disk — closing the "the OS write-back buffer
    /// never flushed → silent last-record loss" window `File.AppendAllText` left
    /// open (2026-07-09).
    ///
    /// The ordering is AT-LEAST-ONCE, and this names the window rather than
    /// asserting atomicity: the chunk's sink statement (one MERGE / one bulk
    /// batch) commits FIRST (`TransferRun`), THEN this append records it. A crash
    /// in that window — sink committed, append not yet durable — leaves the chunk
    /// journaled-as-absent, so resume RE-writes it; for an `AssignedBySink` kind
    /// that re-mints, i.e. duplicates. The write-ahead intent protocol that closes
    /// the window (journal the chunk fingerprint BEFORE the sink write; on resume
    /// treat a journaled-unconfirmed chunk as maybe-committed and probe / MERGE
    /// idempotently on a deterministic capture key) is the named follow-on. This
    /// card makes the durability honest and the window documented, not silent.
    /// The append's position remains the grain's WriteAdmit (R3 / RI-3) for a
    /// clean run — the completed sink statement is the external witness.
    let append (journal: CaptureJournal) (record: ChunkRecord) : unit =
        use fs = new FileStream(journal.FilePath, FileMode.Append, FileAccess.Write, FileShare.Read)
        let bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize record + "\n")
        fs.Write(bytes, 0, bytes.Length)
        fs.Flush(true)   // fsync — the record is on disk before control returns

    // -- T0.4 write-ahead intent (2026-07-09) — close the at-least-once window ---
    // A chunk is journaled TWICE: an INTENT record (WrittenCount = `IntentPending`)
    // fsync'd BEFORE the sink write, then the COMPLETE record (WrittenCount ≥ 0,
    // pairs) fsync'd after. `load`/`openResumeIndex` are last-write-wins per
    // (kind, chunkIx), so a clean chunk resolves to its COMPLETE record; a crash
    // between the two leaves only the INTENT — an IN-DOUBT chunk the resume path
    // probes rather than silently re-mints.

    /// The `WrittenCount` sentinel marking an INTENT (attempted-but-unconfirmed)
    /// record. Negative, so it never collides with a real written count (≥ 0).
    let IntentPending : int = -1

    /// A record is COMPLETE (its sink write is confirmed) iff its written count is
    /// non-negative; an INTENT record carries `IntentPending`.
    let isComplete (record: ChunkRecord) : bool = record.WrittenCount >= 0

    /// The write-ahead INTENT record for a chunk about to be written — the
    /// fingerprint fields only, no pairs (the assigned identities are not known
    /// until the sink write returns).
    let intentRecord (kind: string) (chunkIx: int) (firstPk: string) (lastPk: string) (rawCount: int) : ChunkRecord =
        { Kind = kind; ChunkIx = chunkIx; FirstPk = firstPk; LastPk = lastPk
          RawCount = rawCount; WrittenCount = IntentPending; Pairs = [||] }

    /// The total rows a kind's COMPLETED chunks wrote, per the resume index — the
    /// sink row count expected for the kind if an in-doubt chunk did NOT commit
    /// (the streaming load starts empty; T1.8 forbids incremental into a populated
    /// sink). Reads one record per journaled chunk of the kind; INTENT records
    /// (unconfirmed) contribute nothing.
    let completedWrittenCountForKind (index: ResumeIndex) (kindRoot: string) : int =
        index.Offsets
        |> Seq.filter (fun kv -> fst kv.Key = kindRoot)
        |> Seq.sumBy (fun kv ->
            match JsonSerializer.Deserialize<ChunkRecord> (readLineAt index.IndexFilePath kv.Value) with
            | null   -> 0
            | record -> if isComplete record then record.WrittenCount else 0)

    // -- L2: the journal grain on the ledger contract (R3 / RI-3) ------------

    /// The chunk's SOURCE fingerprint — what `Ledger.resumeAdmit` compares
    /// against the live stream's recomputation (first/last source PK + raw
    /// count; deterministic because `ReadSide` orders by PK). The persisted
    /// bytes are unchanged by this card: the `ChunkRecord` NDJSON line IS
    /// the stored fingerprint, so existing journals resume across the
    /// contract cutover (the RI-7 byte-stability discipline).
    let fingerprintOf (record: ChunkRecord) : string * string * int =
        record.FirstPk, record.LastPk, record.RawCount

    /// A journaled record in chain form: position = the chunk index within
    /// its kind. The one NDJSON file is a per-kind FAMILY of chains —
    /// `load`'s `(kind, chunkIx)` last-write-wins index — so the contract
    /// instance is per kind, and `Ledger.resumePoint` over one kind's
    /// entries is where its crashed run resumes.
    let toEntry (record: ChunkRecord) : LedgerEntry<ChunkRecord, string * string * int> =
        { Position = record.ChunkIx; Fingerprint = fingerprintOf record; Entry = record }

    /// The journal grain's `LedgerSpec` instance for one kind — the
    /// EFFECTFUL REMAP FOLD, ADAPTED AT THE INSTANCE (RI-3): the contract
    /// record stays pure data; this instance's `Apply` feeds the journaled
    /// `(source → assigned)` pairs into the accumulator it is handed and
    /// returns the same value. `Genesis` is the run's SHARED in-flight
    /// remap (FKs cross kinds, so the run owns ONE accumulator; replay
    /// folds into it, never beside it).
    let spec (kind: SsKey) (remap: PackedSurrogateRemap) : LedgerSpec<PackedSurrogateRemap, ChunkRecord, string * string * int> =
        { Genesis = remap
          Apply =
            fun acc record ->
                record.Pairs
                |> Array.iter (fun pair ->
                    if pair.Length = 2 then PackedSurrogateRemap.capture kind pair[0] pair[1] acc)
                acc
          FingerprintOf = fingerprintOf }
