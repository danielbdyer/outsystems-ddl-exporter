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
    /// (one MERGE / one bulk batch) is the atomic commit point.
    let append (journal: CaptureJournal) (record: ChunkRecord) : unit =
        File.AppendAllText(journal.FilePath, JsonSerializer.Serialize record + "\n")
