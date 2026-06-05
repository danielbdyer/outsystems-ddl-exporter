namespace Projection.Pipeline

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Security.Cryptography

/// Masterful base #2 (`REPORTING_HORIZON`) — the addressable run aggregate.
/// A `Run` is the complete, content-addressed, round-trippable record of one
/// projection execution: its identity, the digest of its INPUTS (so the same
/// config+catalog yields the same digest regardless of wall-clock — the basis
/// for dedup and "is this the same run"), its verdict, and its event stream
/// captured as the already-serialized NDJSON envelopes (opaque + faithful — no
/// rich-DU round-trip; offline explain / suggest-config / diff re-parse the
/// JSON they already are).
///
/// Discriminating predicate: `load (save run) = run`, and `inputDigest`
/// depends only on inputs, not wall-clock. This SUPPORTS persist / diff /
/// query / migrate-inputs without completing any of them — it is the noun
/// those verbs would operate on. `RunLedger.LedgerRecord` is its index
/// projection (`toLedgerEntry`), so the run *subsumes* the ledger row.
module Run =

    type Run = {
        RunId       : string
        Ts          : string
        Command     : string
        /// Content hash of the run's inputs (config + source catalog) —
        /// stable across wall-clock; same inputs → same digest.
        InputDigest : string
        Outcome     : string
        Canary      : string option
        Registered  : int
        Applied     : int
        Declined    : int
        /// The run's NDJSON envelopes, verbatim — the faithful, opaque trail.
        Events      : string list
        /// The run's tree — its output artifacts keyed by name (catalog JSON,
        /// SSDT bundle, manifest, …) as opaque content blobs. This is what
        /// makes a `runId` resolve to *artifacts* (so diff / migrate / explain
        /// can operate offline from a stored run), not just events.
        Artifacts   : Map<string, string>
    }

    /// Content-address the run by its inputs. Two runs with the same config +
    /// source catalog share a digest even if their runId / timestamp differ.
    let inputDigest (configText: string) (catalogJson: string) : string =
        use sha = SHA256.Create()
        Encoding.UTF8.GetBytes(configText + "\n" + catalogJson)
        |> sha.ComputeHash
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""

    // --- serialization (round-trippable) -----------------------------------

    let private toJson (r: Run) : string =
        let o = JsonObject()
        o.["runId"]       <- JsonValue.Create r.RunId
        o.["ts"]          <- JsonValue.Create r.Ts
        o.["command"]     <- JsonValue.Create r.Command
        o.["inputDigest"] <- JsonValue.Create r.InputDigest
        o.["outcome"]     <- JsonValue.Create r.Outcome
        (match r.Canary with Some c -> o.["canary"] <- JsonValue.Create c | None -> ())
        o.["registered"]  <- JsonValue.Create r.Registered
        o.["applied"]     <- JsonValue.Create r.Applied
        o.["declined"]    <- JsonValue.Create r.Declined
        let a = JsonArray()
        for e in r.Events do a.Add(JsonValue.Create e)
        o.["events"] <- a
        let art = JsonObject()
        for KeyValue(k, v) in r.Artifacts do art.[k] <- JsonValue.Create v
        o.["artifacts"] <- art
        o.ToJsonString(JsonSerializerOptions(WriteIndented = true))

    let private parse (json: string) : Run option =
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement
            let nz (s: string | null) = match s with null -> "" | v -> v
            let str (name: string) =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty(name, &v) && v.ValueKind = JsonValueKind.String then nz (v.GetString()) else ""
            let i (name: string) =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty(name, &v) && v.ValueKind = JsonValueKind.Number then v.GetInt32() else 0
            let canary =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty("canary", &v) && v.ValueKind = JsonValueKind.String then Some (nz (v.GetString())) else None
            let events =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty("events", &v) && v.ValueKind = JsonValueKind.Array
                then [ for e in v.EnumerateArray() -> nz (e.GetString()) ] else []
            let artifacts =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty("artifacts", &v) && v.ValueKind = JsonValueKind.Object
                then [ for p in v.EnumerateObject() -> (p.Name, nz (p.Value.GetString())) ] |> Map.ofList
                else Map.empty
            Some {
                RunId = str "runId"; Ts = str "ts"; Command = str "command"
                InputDigest = str "inputDigest"; Outcome = str "outcome"; Canary = canary
                Registered = i "registered"; Applied = i "applied"; Declined = i "declined"
                Events = events; Artifacts = artifacts
            }
        with _ -> None

    // --- the store (opt-in via PROJECTION_RUNS_DIR) ------------------------

    let configuredDir () : string option =
        match Environment.GetEnvironmentVariable "PROJECTION_RUNS_DIR" with
        | null | "" -> None
        | d         -> Some d

    let runPath (dir: string) (runId: string) : string = Path.Combine(dir, runId, "run.json")

    let save (dir: string) (r: Run) : unit =
        let p = runPath dir r.RunId
        Directory.CreateDirectory(nonNull (Path.GetDirectoryName p)) |> ignore
        File.WriteAllText(p, toJson r)

    let load (dir: string) (runId: string) : Run option =
        let p = runPath dir runId
        if File.Exists p then parse (File.ReadAllText p) else None

    /// Enumerate every persisted run under `dir`.
    let list (dir: string) : Run list =
        if Directory.Exists dir then
            Directory.GetDirectories dir
            |> Array.choose (fun d ->
                let p = Path.Combine(d, "run.json")
                if File.Exists p then parse (File.ReadAllText p) else None)
            |> Array.toList
        else []

    /// Project the run onto its ledger index row — the run subsumes the
    /// `RunLedger.LedgerRecord` (one source, the ledger is a derived view).
    let toLedgerEntry (r: Run) : RunLedger.LedgerRecord =
        { RunId = r.RunId; Ts = r.Ts; Command = r.Command; Outcome = r.Outcome
          Canary = r.Canary; Registered = r.Registered; Applied = r.Applied; Declined = r.Declined }

    /// Capture a Run from a live execution — the bridge from the running
    /// pipeline to the aggregate. Reads the verdict + the serialized event
    /// stream from the `LogSink` accumulator; the caller supplies the input
    /// digest (config + source catalog) and the output artifacts (the tree).
    /// This is the producer a `persist` verb would call; building it completes
    /// the Run into a genuine hub — a `runId` now resolves to a full record
    /// (verdict + events + artifacts), persistable + diffable offline. It
    /// SUPPORTS persist without completing it (nothing here is wired into a
    /// verb).
    let capture
        (command: string)
        (code: int)
        (inputDigest: string)
        (artifacts: Map<string, string>)
        : Run =
        let registered, applied, declined = LogSink.transformCounts ()
        { RunId       = LogSink.runId ()
          Ts          = DateTime.UtcNow.ToString("o")
          Command     = command
          InputDigest = inputDigest
          Outcome     = (if code = 0 then "succeeded" else "failed")
          Canary      = LogSink.canaryVerdict ()
          Registered  = registered
          Applied     = applied
          Declined    = declined
          Events      = LogSink.serializedEnvelopes ()
          Artifacts   = artifacts }
