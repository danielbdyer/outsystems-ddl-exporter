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
    let load (journal: CaptureJournal) : Dictionary<string * int, ChunkRecord> =
        let index = Dictionary<string * int, ChunkRecord>()
        if File.Exists journal.FilePath then
            for line in File.ReadLines journal.FilePath do
                if not (System.String.IsNullOrWhiteSpace line) then
                    match JsonSerializer.Deserialize<ChunkRecord> line with
                    | null -> ()
                    | record -> index[(record.Kind, record.ChunkIx)] <- record
        index

    /// Append one completed chunk. The write is flushed on close, so a
    /// crash AFTER the append never re-executes the chunk and a crash
    /// DURING the chunk never journals it — the chunk's sink statement
    /// (one MERGE / one bulk batch) is the atomic commit point. This
    /// positioning IS the grain's WriteAdmit (R3 / RI-3): the completed
    /// sink statement is the external witness, proven by control flow —
    /// a ceremonial `Ledger.writeAdmit` here would assert nothing the
    /// append's position does not already.
    let append (journal: CaptureJournal) (record: ChunkRecord) : unit =
        File.AppendAllText(journal.FilePath, JsonSerializer.Serialize record + "\n")

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
