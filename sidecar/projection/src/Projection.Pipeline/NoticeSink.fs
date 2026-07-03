namespace Projection.Pipeline

open System.IO
open System.Text.Json

/// File-system sink for operator **notice detail** — the constant-size answer
/// to the notice-flood failure mode (2026-07-02; the F9 surface-never-discard
/// law kept, the per-item stderr flood retired). The screen carries ONE Warn
/// rollup envelope (count + families + first samples); the full per-item
/// detail lands here as the run's notice artifact, addressed like the bench
/// snapshot (R1c — keyed by the run that produced it):
///
///     notices/<tag>/<runId>.json
///
/// The rollup envelope and the board's done-frame name this path, so the
/// operator can always reach ALL of it — never a silent cap.
[<RequireQualifiedAccess>]
module NoticeSink =

    /// One surfaced notice — the operator-relevant projection of a
    /// `DiagnosticEntry` (source elided: the tag names the producing surface).
    type Notice =
        {
            Code     : string
            Severity : string
            Message  : string
            Metadata : Map<string, string>
        }

    /// Subdirectory under `rootDir` collecting notice artifacts.
    [<Literal>]
    let private NoticeSubdirectory : string = "notices"

    [<Literal>]
    let private ArtifactExtension : string = ".json"

    /// Compose the per-run `notices/<tag>/<runId>.json` path under `rootDir`.
    let runPath (rootDir: string) (tag: string) (runId: string) : string =
        Path.Combine(rootDir, NoticeSubdirectory, tag, runId + ArtifactExtension)  // LINT-ALLOW: terminal filesystem path; runId is the LogSink-minted ULID

    let private jsonOptions : JsonSerializerOptions =
        JsonSerializerOptions(WriteIndented = true)

    /// The lock serializing read-modify-write appends. Appenders today are
    /// sequential (the 2-3 model-read legs of one publish run); the lock is
    /// insurance for the concurrent realization stream the live board's
    /// off-thread move anticipates.
    let private appendLock = obj ()

    /// Append `notices` to the artifact at `path`, creating it (and its
    /// directory) on first write. Appends MERGE-DEDUPE on full notice
    /// equality: a publish run reads the same model 2-3 times (extract +
    /// store leg + load leg), and repeating identical facts per leg would
    /// misstate the estate's notice count.
    let append (path: string) (notices: Notice list) : Notice list =
        lock appendLock (fun () ->
            let existing : Notice list =
                if File.Exists path then
                    try
                        match JsonSerializer.Deserialize<Notice list>(File.ReadAllText path, jsonOptions) with
                        | null -> []
                        | notices -> notices
                    with _ -> []
                else []
            let merged =
                (existing @ notices)
                |> List.distinct
            match Path.GetDirectoryName path with
            | null -> ()
            | dir when System.String.IsNullOrEmpty dir -> ()
            | dir -> Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, JsonSerializer.Serialize(merged, jsonOptions))
            merged)
