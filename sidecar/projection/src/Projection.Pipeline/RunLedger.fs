namespace Projection.Pipeline
// LINT-ALLOW-FILE-MUTATION: sealed function-local mutable accumulators in the append-only ledger writer; mutation never escapes the write

open System
open System.IO
open System.Text.Json

/// Tier-4 reporting (`REPORTING_HORIZON.md` §3) — the cross-run ledger.
/// Each completed run appends one compact record; the readiness gauge reads
/// the history and answers the R6 cutover question ("how many consecutive
/// green canaries; is the gate eligible?"). Persistence is **opt-in** via
/// the `PROJECTION_LEDGER_DIR` env var (mirrors bench's `PROJECTION_BENCH_DIR`)
/// so default runs — tests, the SessionEnd canary hook — don't accumulate a
/// committed ledger; the operator sets it to start a run history.
module RunLedger =

    /// R6 governance gate (`DECISIONS 2026-05-22 — R6`): N=10 consecutive
    /// green canary runs (plus operator sign-off, which the gauge does not
    /// model — the gauge measures the evidence, the operator makes the call).
    [<Literal>]
    let R6Threshold = 10

    /// One ledger row — the run's verdict in the form the readiness gauge +
    /// a future `diff` consume. Compact by design (the full evidence lives in
    /// the run's `summary.runComplete`; this is the index).
    type LedgerRecord = {
        RunId      : string
        Ts         : string
        Command    : string
        Outcome    : string
        /// "green" (canary.diffEmpty) / "red" (canary.divergence) / None (no
        /// canary leg in this run).
        Canary     : string option
        Registered : int
        Applied    : int
        Declined   : int
    }

    let private str (e: JsonElement) : string =
        match e.GetString() with
        | null -> ""
        | s    -> s

    let private toJsonLine (r: LedgerRecord) : string =
        use ms = new MemoryStream()
        (use jw = new Utf8JsonWriter(ms)
         jw.WriteStartObject()
         jw.WriteString("runId", r.RunId)
         jw.WriteString("ts", r.Ts)
         jw.WriteString("command", r.Command)
         jw.WriteString("outcome", r.Outcome)
         (match r.Canary with
          | Some c -> jw.WriteString("canary", c)
          | None   -> jw.WriteNull("canary"))
         jw.WriteNumber("registered", r.Registered)
         jw.WriteNumber("applied", r.Applied)
         jw.WriteNumber("declined", r.Declined)
         jw.WriteEndObject())
        Text.Encoding.UTF8.GetString(ms.ToArray())

    let private parseLine (line: string) : LedgerRecord option =
        try
            use doc = JsonDocument.Parse line
            let root = doc.RootElement
            let getStr (name: string) =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty(name, &v) && v.ValueKind = JsonValueKind.String then str v else ""
            let getInt (name: string) =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty(name, &v) && v.ValueKind = JsonValueKind.Number then v.GetInt32() else 0
            let canary =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty("canary", &v) && v.ValueKind = JsonValueKind.String
                then Some (str v) else None
            Some {
                RunId = getStr "runId"; Ts = getStr "ts"; Command = getStr "command"
                Outcome = getStr "outcome"; Canary = canary
                Registered = getInt "registered"; Applied = getInt "applied"; Declined = getInt "declined"
            }
        with :? System.Text.Json.JsonException -> None   // malformed ledger JSON → None; a fatal propagates

    /// The configured ledger directory (opt-in via `PROJECTION_LEDGER_DIR`).
    let configuredDir () : string option =
        match Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR" with
        | null | "" -> None
        | d         -> Some d

    let ledgerPath (dir: string) : string = Path.Combine(dir, "runs.jsonl")

    /// Append one record (creates the dir on first write). JSONL — one
    /// self-describing line per run, append-only, `grep`/`jq`-able.
    let append (dir: string) (record: LedgerRecord) : unit =
        Directory.CreateDirectory dir |> ignore
        File.AppendAllText(ledgerPath dir, toJsonLine record + "\n")

    /// Read the full run history (chronological — append order). Malformed
    /// lines are skipped (forward-compatibility: a future schema addition
    /// doesn't break an older reader).
    let read (dir: string) : LedgerRecord list =
        let p = ledgerPath dir
        if File.Exists p then File.ReadAllLines p |> Array.toList |> List.choose parseLine
        else []

    /// The R6 cutover-readiness gauge.
    type Readiness = {
        TotalRuns        : int
        CanaryRuns       : int
        ConsecutiveGreen : int
        LastCanary       : string option
        Threshold        : int
        Eligible         : bool
    }

    /// R6 readiness from the ledger history: count green canaries backward
    /// from the most recent canary-bearing run until the first non-green.
    /// Eligible when that streak reaches the R6 threshold AND the last canary
    /// was green (a red after a long green streak resets eligibility — the
    /// gate measures the *current* streak, not the historical best).
    let readiness (records: LedgerRecord list) : Readiness =
        let canaryVerdicts = records |> List.choose (fun r -> r.Canary)
        let consecutiveGreen =
            canaryVerdicts
            |> List.rev
            |> List.takeWhile (fun c -> c = "green")
            |> List.length
        let last = canaryVerdicts |> List.tryLast
        {
            TotalRuns        = List.length records
            CanaryRuns       = List.length canaryVerdicts
            ConsecutiveGreen = consecutiveGreen
            LastCanary       = last
            Threshold        = R6Threshold
            Eligible         = consecutiveGreen >= R6Threshold && last = Some "green"
        }
