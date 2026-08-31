namespace Projection.Pipeline
// LINT-ALLOW-FILE-MUTATION: sealed function-local mutable accumulators in the append-only ledger writer; mutation never escapes the write

open System
open System.IO
open System.Text.Json

/// Tier-4 reporting (`REPORTING_HORIZON.md` §3) — the thin durable JSONL
/// INDEX of completed runs (renamed from `RunLedger` at align-III.21/X5,
/// executing the III.3 micro-ruling: `RunHistory` reads the richer per-run
/// `Run` store and is the SOURCE; this module's `read`/`append` is the
/// residue index; the readiness GAUGE here is the one shared definition
/// both reach. The old name overloaded the flagship "ledger" noun the
/// `LedgerSpec` algebra owns — an index is not a ledger). Each completed
/// run appends one compact record; the readiness gauge answers the R6
/// cutover question ("how many consecutive green canaries; is the gate
/// eligible?"). Persistence is **opt-in** via the `PROJECTION_LEDGER_DIR`
/// env var (the VAR NAME and the on-disk `runs.jsonl` are operator surface
/// contracts and keep their names) so default runs — tests, the SessionEnd
/// canary hook — don't accumulate a committed index; the operator sets it
/// to start a run history.
module RunIndex =

    /// R6 governance gate (`DECISIONS 2026-05-22 — R6`): N=10 consecutive
    /// green canary runs (plus operator sign-off, which the gauge does not
    /// model — the gauge measures the evidence, the operator makes the call).
    [<Literal>]
    let R6Threshold = 10

    /// One ledger row — the run's verdict in the form the readiness gauge +
    /// a future `diff` consume. Compact by design (the full evidence lives in
    /// the run's `summary.runComplete`; this is the index).
    type IndexRecord = {
        RunId      : string
        /// Typed at align-III.1 (a5's typed-instants charge). The wire
        /// keeps the UTC `o` form; a malformed stored `ts` drops the line
        /// through this reader's existing lenient posture (align-III.3
        /// owns naming the skips).
        Ts         : DateTimeOffset
        Command    : string
        Outcome    : string
        /// The round-trip canary's verdict — typed at align-III.3
        /// (`CanaryVerdict`): `Green` / `Red` / `NotRun`. The wire keeps the
        /// `"green"` / `"red"` token (absent for `NotRun`), so pre-III.3
        /// ledgers round-trip byte-identical.
        Canary     : Projection.Core.CanaryVerdict
        Registered : int
        Applied    : int
        Declined   : int
    }

    let private str (e: JsonElement) : string =
        match e.GetString() with
        | null -> ""
        | s    -> s

    let private toJsonLine (r: IndexRecord) : string =
        use ms = new MemoryStream()
        (use jw = new Utf8JsonWriter(ms)
         jw.WriteStartObject()
         jw.WriteString("runId", r.RunId)
         jw.WriteString("ts", r.Ts.UtcDateTime.ToString("o", Globalization.CultureInfo.InvariantCulture))
         jw.WriteString("command", r.Command)
         jw.WriteString("outcome", r.Outcome)
         (match Projection.Core.CanaryVerdict.tokenOpt r.Canary with
          | Some c -> jw.WriteString("canary", c)
          | None   -> jw.WriteNull("canary"))
         jw.WriteNumber("registered", r.Registered)
         jw.WriteNumber("applied", r.Applied)
         jw.WriteNumber("declined", r.Declined)
         jw.WriteEndObject())
        Text.Encoding.UTF8.GetString(ms.ToArray())

    let private parseLine (line: string) : IndexRecord option =
        try
            use doc = JsonDocument.Parse line
            let root = doc.RootElement
            let getStr (name: string) =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty(name, &v) && v.ValueKind = JsonValueKind.String then str v else ""
            let getInt (name: string) =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty(name, &v) && v.ValueKind = JsonValueKind.Number then v.GetInt32() else 0
            let canaryToken =
                let mutable v = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty("canary", &v) && v.ValueKind = JsonValueKind.String
                then Some (str v) else None
            // align-III.1 — the instant parses FAIL-CLOSED within this
            // reader's existing lenient posture: a malformed `ts` drops
            // the line (like any malformed line), never fabricates an
            // empty instant.
            match DateTimeOffset.TryParse(getStr "ts", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AssumeUniversal) with
            | false, _ -> None
            | true, ts ->
                Some {
                    RunId = getStr "runId"; Ts = ts; Command = getStr "command"
                    // align-III.3 — the token reads back to the typed verdict
                    // (absent/unknown ⇒ NotRun, the forward-compatible safe
                    // direction).
                    Outcome = getStr "outcome"; Canary = Projection.Core.CanaryVerdict.ofTokenOpt canaryToken
                    Registered = getInt "registered"; Applied = getInt "applied"; Declined = getInt "declined"
                }
        with :? System.Text.Json.JsonException -> None   // malformed ledger JSON → None; a fatal propagates

    /// The configured ledger directory (opt-in via `PROJECTION_LEDGER_DIR`).
    let configuredDir () : string option =
        match Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR" with
        | null | "" -> None
        | d         -> Some d

    let indexPath (dir: string) : string = Path.Combine(dir, "runs.jsonl")

    /// Append one record (creates the dir on first write). JSONL — one
    /// self-describing line per run, append-only, `grep`/`jq`-able.
    let append (dir: string) (record: IndexRecord) : unit =
        Directory.CreateDirectory dir |> ignore
        File.AppendAllText(indexPath dir, toJsonLine record + "\n")

    /// A fail-closed read of the JSONL ledger (align-III.3). A malformed
    /// TRAILING line is tolerated silently (a crash mid-append is normal —
    /// the append is not atomic at the line grain); an INTERIOR malformed
    /// line is COUNTED (`SkippedLines`) and surfaced on the readiness board,
    /// never silently dropped as before. Not a hard refuse — the ledger is
    /// opt-in and forward-compatible, so one corrupt interior line names
    /// itself rather than breaking the whole gauge.
    type IndexReading = { Records: IndexRecord list; SkippedLines: int }

    let private readLines (dir: string) : IndexReading =
        let p = indexPath dir
        if not (File.Exists p) then { Records = []; SkippedLines = 0 }
        else
            let raw = File.ReadAllLines p
            let lastIndex = raw.Length - 1
            let records, skipped =
                raw
                |> Array.toList
                |> List.mapi (fun i line -> (i, parseLine line))
                |> List.fold
                    (fun (recs, sk) (i, parsed) ->
                        match parsed with
                        | Some r -> (r :: recs, sk)
                        // trailing-torn tolerated; interior corruption counted.
                        | None -> (recs, (if i = lastIndex then sk else sk + 1)))
                    ([], 0)
            { Records = List.rev records; SkippedLines = skipped }

    /// Read the full run history (chronological — append order). Malformed
    /// interior lines are counted (see `readReading`); the trailing torn line
    /// is tolerated.
    let read (dir: string) : IndexRecord list = (readLines dir).Records

    /// Read the ledger WITH its skipped-interior-line count (the fail-closed
    /// reading the readiness board surfaces).
    let readReading (dir: string) : IndexReading = readLines dir

    /// The R6 cutover-readiness gauge.
    type Readiness = {
        TotalRuns        : int
        CanaryRuns       : int
        ConsecutiveGreen : int
        LastCanary       : Projection.Core.CanaryVerdict option
        Threshold        : int
        /// Interior ledger lines that could not be parsed and were skipped
        /// (align-III.3) — 0 for a clean or trailing-torn-only ledger, and
        /// for the pure `readiness` gauge over already-parsed records.
        SkippedLines     : int
        Eligible         : bool
    }

    /// R6 readiness from the ledger history: count green canaries backward
    /// from the most recent canary-bearing run until the first non-green.
    /// Eligible when that streak reaches the R6 threshold AND the last canary
    /// was green (a red after a long green streak resets eligibility — the
    /// gate measures the *current* streak, not the historical best). Pure
    /// over already-parsed records, so `SkippedLines = 0`; `readinessOf`
    /// carries the reading's skip count through.
    let readiness (records: IndexRecord list) : Readiness =
        let canaryRuns = records |> List.map (fun r -> r.Canary) |> List.filter Projection.Core.CanaryVerdict.ran
        let consecutiveGreen =
            canaryRuns
            |> List.rev
            |> List.takeWhile Projection.Core.CanaryVerdict.isGreen
            |> List.length
        let last = canaryRuns |> List.tryLast
        {
            TotalRuns        = List.length records
            CanaryRuns       = List.length canaryRuns
            ConsecutiveGreen = consecutiveGreen
            LastCanary       = last
            Threshold        = R6Threshold
            SkippedLines     = 0
            Eligible         = consecutiveGreen >= R6Threshold && last = Some Projection.Core.CanaryVerdict.Green
        }

    /// The gauge over a fail-closed reading — surfaces the reading's
    /// interior-skip count on `Readiness.SkippedLines`.
    let readinessOf (reading: IndexReading) : Readiness =
        { readiness reading.Records with SkippedLines = reading.SkippedLines }
