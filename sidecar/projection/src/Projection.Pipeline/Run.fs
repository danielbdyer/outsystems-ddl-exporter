namespace Projection.Pipeline
// LINT-ALLOW-FILE-MUTATION: the run record + RunState envelope/event-stream are assembled by sealed function-local imperative loops, then returned immutably; mutation never escapes the builder (FP-promised-land pillar 4 — mutation reified at file level)

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

    /// R1a — a reference to a durable ledger the run touched. The run
    /// aggregate carries WHERE its ledgers live, never their content: the
    /// capture journal by its digest (the digest IS the filename,
    /// `transfer-<digest16>.ndjson` — RI-7), the episode by its timeline
    /// coordinate. `LedgerRef` is the run-side name for the R3 contract's
    /// instances (L1–L4): a stored run can answer "which chains did this
    /// run extend?" offline.
    type LedgerRef =
        | JournalRef of digest: string
        | EpisodeRef of timeline: string * ordinal: int

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
        /// R1a — the ledgers this run extended (journal digests + episode
        /// coordinates). Empty for runs that touched no chain.
        Ledgers     : LedgerRef list
        /// R1a — the run's measurement snapshot, when one was captured
        /// (R1c keys the BenchSink file by RunId; this carries the value
        /// on the aggregate so `diff <runA> <runB>` can compare offline).
        Bench       : Projection.Core.Bench.Run option
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
        // R1a — codec totality over the completed aggregate: every field
        // on the record round-trips (the load(save run) = run predicate).
        let ledgers = JsonArray()
        for l in r.Ledgers do
            let lo = JsonObject()
            (match l with
             | JournalRef digest ->
                 lo.["kind"]   <- JsonValue.Create "journal"
                 lo.["digest"] <- JsonValue.Create digest
             | EpisodeRef (timeline, ordinal) ->
                 lo.["kind"]     <- JsonValue.Create "episode"
                 lo.["timeline"] <- JsonValue.Create timeline
                 lo.["ordinal"]  <- JsonValue.Create ordinal)
            ledgers.Add lo
        o.["ledgers"] <- ledgers
        (match r.Bench with
         | None -> ()
         | Some b ->
             let bo = JsonObject()
             bo.["capturedAtUtc"] <- JsonValue.Create (b.CapturedAtUtc.ToString("o"))
             bo.["tag"] <- JsonValue.Create b.Tag
             let sa = JsonArray()
             for s in b.Stats do
                 let so = JsonObject()
                 so.["label"]   <- JsonValue.Create s.Label
                 so.["count"]   <- JsonValue.Create s.Count
                 so.["totalMs"] <- JsonValue.Create s.TotalMs
                 so.["minMs"]   <- JsonValue.Create s.MinMs
                 so.["maxMs"]   <- JsonValue.Create s.MaxMs
                 so.["meanMs"]  <- JsonValue.Create s.MeanMs
                 so.["p50Ms"]   <- JsonValue.Create s.P50Ms
                 so.["p95Ms"]   <- JsonValue.Create s.P95Ms
                 so.["p99Ms"]   <- JsonValue.Create s.P99Ms
                 sa.Add so
             bo.["stats"] <- sa
             o.["bench"] <- bo)
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
            // R1a — total over the new fields; a pre-R1a run.json (no
            // 'ledgers' / 'bench') loads with the empty/None defaults.
            let estr (el: JsonElement) (name: string) =
                let mutable v = Unchecked.defaultof<JsonElement>
                if el.TryGetProperty(name, &v) && v.ValueKind = JsonValueKind.String then nz (v.GetString()) else ""
            let ledgers =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty("ledgers", &v) && v.ValueKind = JsonValueKind.Array then
                    [ for el in v.EnumerateArray() do
                        match estr el "kind" with
                        | "journal" -> yield JournalRef (estr el "digest")
                        | "episode" ->
                            let mutable ov = Unchecked.defaultof<JsonElement>
                            let ordinal = if el.TryGetProperty("ordinal", &ov) && ov.ValueKind = JsonValueKind.Number then ov.GetInt32() else 0
                            yield EpisodeRef (estr el "timeline", ordinal)
                        | _ -> () ]
                else []
            let bench =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty("bench", &v) && v.ValueKind = JsonValueKind.Object then
                    let stats =
                        let mutable sv = Unchecked.defaultof<JsonElement>
                        if v.TryGetProperty("stats", &sv) && sv.ValueKind = JsonValueKind.Array then
                            [ for el in sv.EnumerateArray() do
                                let i64 (name: string) =
                                    let mutable nv = Unchecked.defaultof<JsonElement>
                                    if el.TryGetProperty(name, &nv) && nv.ValueKind = JsonValueKind.Number then nv.GetInt64() else 0L
                                let f (name: string) =
                                    let mutable nv = Unchecked.defaultof<JsonElement>
                                    if el.TryGetProperty(name, &nv) && nv.ValueKind = JsonValueKind.Number then nv.GetDouble() else 0.0
                                let c =
                                    let mutable nv = Unchecked.defaultof<JsonElement>
                                    if el.TryGetProperty("count", &nv) && nv.ValueKind = JsonValueKind.Number then nv.GetInt32() else 0
                                ({ Label = estr el "label"; Count = c
                                   TotalMs = i64 "totalMs"; MinMs = i64 "minMs"; MaxMs = i64 "maxMs"
                                   MeanMs = f "meanMs"
                                   P50Ms = i64 "p50Ms"; P95Ms = i64 "p95Ms"; P99Ms = i64 "p99Ms" } : Projection.Core.Bench.Stats) ]
                        else []
                    let capturedAt =
                        match DateTime.TryParse(estr v "capturedAtUtc", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) with
                        | true, dt -> dt
                        | _ -> DateTime.MinValue
                    Some ({ CapturedAtUtc = capturedAt; Tag = estr v "tag"; Stats = stats } : Projection.Core.Bench.Run)
                else None
            Some {
                RunId = str "runId"; Ts = str "ts"; Command = str "command"
                InputDigest = str "inputDigest"; Outcome = str "outcome"; Canary = canary
                Registered = i "registered"; Applied = i "applied"; Declined = i "declined"
                Events = events; Artifacts = artifacts
                Ledgers = ledgers; Bench = bench
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

    /// R1d — where runs are stored, one resolution rule for every reader:
    /// `PROJECTION_RUNS_DIR` when set (the explicit override), else
    /// `PROJECTION_LEDGER_DIR` (where the R1b bracket capture persists).
    let storeDir () : string option =
        match configuredDir () with
        | Some d -> Some d
        | None -> RunLedger.configuredDir ()

    // R1d — the run-vs-run delta surface. The §7 units-of-measure
    // promotion FIRES here, scoped to this surface exactly as gated
    // (CONSTELLATION §9.7): the trigger was mixed quantities — counts
    // beside milliseconds — in one expression; the measure keeps a
    // bench millisecond from ever adding to a transform count.
    [<Measure>]
    type ms

    /// One label's wall-time movement between two runs. `None` = the
    /// label is absent from that side (a stage that ran only once).
    type BenchDelta =
        {
            Label    : string
            BeforeMs : int64<ms> option
            AfterMs  : int64<ms> option
            /// after − before, an absent side counting 0.
            DeltaMs  : int64<ms>
        }

    /// The run-vs-run projection: verdict movement + count deltas (b − a)
    /// + the per-label bench deltas.
    type RunDiff =
        {
            RunIds      : string * string
            Commands    : string * string
            Outcomes    : string * string
            Canaries    : string option * string option
            Registered  : int
            Applied     : int
            Declined    : int
            Events      : int
            BenchDeltas : BenchDelta list
        }

    let private benchTotals (r: Run) : Map<string, int64<ms>> =
        match r.Bench with
        | None -> Map.empty
        | Some b -> b.Stats |> List.map (fun s -> s.Label, s.TotalMs * 1L<ms>) |> Map.ofList

    /// R1d — diff two stored runs. `keyLabels` is the restriction the
    /// harness's before/after protocol is an instance of: `Some labels`
    /// compares exactly those; `None` compares every label present on
    /// either side. Labels sort by |delta| descending — the biggest
    /// movement leads.
    let diff (keyLabels: Set<string> option) (a: Run) (b: Run) : RunDiff =
        let ta, tb = benchTotals a, benchTotals b
        let labels =
            Set.union (ta |> Map.toSeq |> Seq.map fst |> Set.ofSeq) (tb |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
            |> fun all -> match keyLabels with Some ks -> Set.intersect all ks | None -> all
        let deltas =
            labels
            |> Set.toList
            |> List.map (fun label ->
                let before = Map.tryFind label ta
                let after = Map.tryFind label tb
                { Label = label
                  BeforeMs = before
                  AfterMs = after
                  DeltaMs = (defaultArg after 0L<ms>) - (defaultArg before 0L<ms>) })
            |> List.sortByDescending (fun d -> abs (int64 d.DeltaMs))
        { RunIds = a.RunId, b.RunId
          Commands = a.Command, b.Command
          Outcomes = a.Outcome, b.Outcome
          Canaries = a.Canary, b.Canary
          Registered = b.Registered - a.Registered
          Applied = b.Applied - a.Applied
          Declined = b.Declined - a.Declined
          Events = List.length b.Events - List.length a.Events
          BenchDeltas = deltas }

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
          Ts          = DateTime.UtcNow.ToString("o")  // LINT-ALLOW: wall-clock timestamp at the run-record IO boundary (ISO-8601 round-trip o format)
          Command     = command
          InputDigest = inputDigest
          Outcome     = (if code = 0 then "succeeded" else "failed")
          Canary      = LogSink.canaryVerdict ()
          Registered  = registered
          Applied     = applied
          Declined    = declined
          Events      = LogSink.serializedEnvelopes ()
          Artifacts   = artifacts
          // R1a — additive defaults at capture; R1b/R1c thread the real
          // ledger refs + bench snapshot when the envelope wires them.
          Ledgers     = []
          Bench       = None }
