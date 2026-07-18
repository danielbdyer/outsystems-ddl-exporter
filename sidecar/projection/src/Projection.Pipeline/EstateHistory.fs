namespace Projection.Pipeline

// LINT-ALLOW-FILE: the estate's recorded readings (wave A7 — the burndown's
//   memory; DECISIONS 2026-07-15 entry 4 names the layout:
//   `estate/<runId>.estate.json` + `estate/latest.json` under the evidence
//   store). The record codec and directory layout compose machine-local file
//   paths and structured JSON at a persistence boundary; the movement and
//   streak derivations are pure — the I/O surface (atomic save / fail-closed
//   load) is the thin boundary the `check environments` face calls.

open System
open System.IO
open System.Text.Json.Nodes
open Projection.Core

/// One finding's line in a recorded reading — the key (the cross-artifact
/// identity the burndown diffs on), enough of the finding to rank movement,
/// and the first-seen instant CARRIED ACROSS READINGS so age is the
/// finding's, not the file's.
type HistoryFinding =
    {
        Key            : string
        Kind           : string
        Lane           : string
        Weight         : int64
        FirstSeenAtUtc : DateTimeOffset
    }

/// One recorded reading of the estate — the burndown's baseline form.
/// `Streak` counts consecutive UNIFIED readings ending at this one (0 while
/// the estate diverges), so the gate streak is O(1) to read back.
type HistoryRecord =
    {
        RunId    : string
        AtUtc    : DateTimeOffset
        Verdict  : string
        Streak   : int
        Findings : HistoryFinding list
    }

[<RequireQualifiedAccess>]
module EstateHistory =

    // ------------------------------------------------------------------
    // Layout — the DECISIONS entry-4 shape, under the same store root the
    // evidence rides: `estate/<runId>.estate.json` + `estate/latest.json`.
    // ------------------------------------------------------------------

    let private historyDir (root: string) : string =
        Path.Combine(root, "estate")

    let recordPath (root: string) (runId: string) : string =
        Path.Combine(historyDir root, runId + ".estate.json")

    let latestPath (root: string) : string =
        Path.Combine(historyDir root, "latest.json")

    // ------------------------------------------------------------------
    // The pure derivations — the record of this run, the movement against
    // a baseline, the streak.
    // ------------------------------------------------------------------

    let private verdictText (v: Estate.Verdict) : string =
        match v with
        | Estate.Verdict.Unified -> "unified"
        | Estate.Verdict.Converging -> "converging"
        | Estate.Verdict.Forked -> "forked"

    /// This run's recorded reading. First-seen instants carry forward from
    /// the previous reading by key — a finding's age belongs to the finding,
    /// never to the file that happened to record it. The streak increments
    /// through unified readings and resets to zero the moment the estate
    /// diverges.
    let recordOf
        (nowUtc: DateTimeOffset)
        (runId: string)
        (previous: HistoryRecord option)
        (report: Estate.EstateReport)
        : HistoryRecord =
        let firstSeen : Map<string, DateTimeOffset> =
            match previous with
            | Some prev -> prev.Findings |> List.map (fun f -> f.Key, f.FirstSeenAtUtc) |> Map.ofList
            | None -> Map.empty
        let findings =
            report.Findings
            |> List.map (fun f ->
                let key = FindingKey.text f.Key
                { Key = key
                  Kind = EstateFindingKind.token f.Kind
                  Lane =
                    (match f.Lane with
                     | EstateLane.Decide -> "decide"
                     | EstateLane.Repair -> "repair"
                     | EstateLane.Relax -> "relax"
                     | EstateLane.Watch -> "watch")
                  Weight = Estate.weightOf f
                  FirstSeenAtUtc = Map.tryFind key firstSeen |> Option.defaultValue nowUtc })
        let streak =
            if report.Verdict = Estate.Verdict.Unified then
                1 + (previous |> Option.map (fun p -> p.Streak) |> Option.defaultValue 0)
            else 0
        { RunId = runId
          AtUtc = nowUtc
          Verdict = verdictText report.Verdict
          Streak = streak
          Findings = findings }

    /// The movement between a baseline reading and this run's report —
    /// closed / opened / remaining by `FindingKey`, and the oldest OPEN
    /// finding's age (first-seen carried from the baseline; a finding the
    /// baseline never saw is as old as this run).
    let burndownOf
        (nowUtc: DateTimeOffset)
        (baseline: HistoryRecord)
        (report: Estate.EstateReport)
        : Estate.Burndown =
        let baselineKeys = baseline.Findings |> List.map (fun f -> f.Key) |> Set.ofList
        let currentKeys =
            report.Findings |> List.map (fun f -> FindingKey.text f.Key) |> Set.ofList
        let closed = Set.difference baselineKeys currentKeys
        let opened = Set.difference currentKeys baselineKeys
        let remaining = Set.intersect baselineKeys currentKeys
        let firstSeen : Map<string, DateTimeOffset> =
            baseline.Findings |> List.map (fun f -> f.Key, f.FirstSeenAtUtc) |> Map.ofList
        let ageDaysOf (instant: DateTimeOffset) : int =
            max 0 (int ((nowUtc - instant).TotalDays))
        let oldestDays =
            if Set.isEmpty currentKeys then None
            else
                currentKeys
                |> Set.toList
                |> List.map (fun key ->
                    Map.tryFind key firstSeen |> Option.defaultValue nowUtc |> ageDaysOf)
                |> List.max
                |> Some
        { SinceRunId = baseline.RunId
          SinceAgeDays = max 0 (int ((nowUtc - baseline.AtUtc).TotalDays))
          Closed = Set.count closed
          Opened = Set.count opened
          Remaining = Set.count remaining
          OldestDays = oldestDays }

    // ------------------------------------------------------------------
    // The record codec — deterministic out, fail-closed back (the
    // EstateEvidenceStore sidecar idioms).
    // ------------------------------------------------------------------

    let private recordJson (record: HistoryRecord) : string =
        let root = JsonObject()
        root.["runId"] <- JsonValue.Create record.RunId
        root.["atUtc"] <- JsonValue.Create(record.AtUtc.ToString "O")
        root.["verdict"] <- JsonValue.Create record.Verdict
        root.["streak"] <- JsonValue.Create record.Streak
        let findings = JsonArray()
        for f in record.Findings |> List.sortBy (fun f -> f.Key) do
            let o = JsonObject()
            o.["key"] <- JsonValue.Create f.Key
            o.["kind"] <- JsonValue.Create f.Kind
            o.["lane"] <- JsonValue.Create f.Lane
            o.["weight"] <- JsonValue.Create f.Weight
            o.["firstSeenAtUtc"] <- JsonValue.Create(f.FirstSeenAtUtc.ToString "O")
            findings.Add o
        root.["findings"] <- findings
        root.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true))

    let private tryNode (o: JsonObject) (key: string) : JsonNode option =
        match o.TryGetPropertyValue key with
        | true, node -> Option.ofObj node
        | _ -> None

    let private tryStr (o: JsonObject) (key: string) : string option =
        tryNode o key
        |> Option.bind (fun node ->
            try Some (node.GetValue<string>())
            with :? InvalidOperationException | :? FormatException -> None)

    let private tryInt (o: JsonObject) (key: string) : int option =
        tryNode o key
        |> Option.bind (fun node ->
            try Some (node.GetValue<int>())
            with :? InvalidOperationException | :? FormatException -> None)

    let private tryInt64 (o: JsonObject) (key: string) : int64 option =
        tryNode o key
        |> Option.bind (fun node ->
            try Some (node.GetValue<int64>())
            with :? InvalidOperationException | :? FormatException -> None)

    let private tryInstant (o: JsonObject) (key: string) : DateTimeOffset option =
        tryStr o key
        |> Option.bind (fun s ->
            match DateTimeOffset.TryParse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
            | true, v -> Some v
            | _ -> None)

    let private asObject (node: JsonNode) : JsonObject option =
        match node with :? JsonObject as o -> Some o | _ -> None

    let private asArray (node: JsonNode) : JsonArray option =
        match node with :? JsonArray as a -> Some a | _ -> None

    let private elements (arr: JsonArray) : JsonNode list =
        [ for n in arr do match Option.ofObj n with Some node -> yield node | None -> () ]

    let private findingOfNode (node: JsonNode) : HistoryFinding option =
        match asObject node with
        | None -> None
        | Some o ->
            match tryStr o "key", tryStr o "kind", tryStr o "lane", tryInt64 o "weight", tryInstant o "firstSeenAtUtc" with
            | Some key, Some kind, Some lane, Some weight, Some firstSeen ->
                Some { Key = key; Kind = kind; Lane = lane; Weight = weight; FirstSeenAtUtc = firstSeen }
            | _ -> None

    /// Parse a recorded reading back — `None` on any malformed shape,
    /// including a single unparseable finding (fail-closed: a torn record
    /// reads as no baseline; the board then says "first recorded reading"
    /// rather than diffing against a half-truth).
    let private tryParseRecord (text: string) : HistoryRecord option =
        try
            match Option.ofObj (JsonNode.Parse text) |> Option.bind asObject with
            | None -> None
            | Some root ->
                let findings =
                    tryNode root "findings"
                    |> Option.bind asArray
                    |> Option.bind (fun arr ->
                        let parsed = elements arr |> List.map findingOfNode
                        if parsed |> List.forall Option.isSome
                        then Some (parsed |> List.choose id)
                        else None)
                match tryStr root "runId", tryInstant root "atUtc", tryStr root "verdict", tryInt root "streak", findings with
                | Some runId, Some at, Some verdict, Some streak, Some fs ->
                    Some { RunId = runId; AtUtc = at; Verdict = verdict; Streak = streak; Findings = fs }
                | _ -> None
        with :? System.Text.Json.JsonException -> None

    // ------------------------------------------------------------------
    // The I/O surface — atomic writes, advisory failures, fail-closed reads.
    // ------------------------------------------------------------------

    let private writeAtomic (path: string) (content: string) : unit =
        let tmp = path + ".tmp"
        File.WriteAllText(tmp, content)
        File.Move(tmp, path, overwrite = true)

    /// Persist this run's reading — the per-run record AND `latest.json`
    /// (the same bytes; the pointer IS a copy, so a torn pointer never
    /// orphans the record). A failure is ADVISORY — a history write never
    /// fails a read-only verb.
    let save (root: string) (record: HistoryRecord) : Result<unit> =
        try
            Directory.CreateDirectory(historyDir root) |> ignore
            let text = recordJson record
            writeAtomic (recordPath root record.RunId) text
            writeAtomic (latestPath root) text
            Result.success ()
        with
        | :? IOException as ex ->
            Result.failureOf (ValidationError.create "estate.history.writeFailed" ex.Message)
        | :? UnauthorizedAccessException as ex ->
            Result.failureOf (ValidationError.create "estate.history.writeFailed" ex.Message)

    /// The latest recorded reading — `None` when absent or unreadable
    /// (fail-closed; the board says "first recorded reading").
    let loadLatest (root: string) : HistoryRecord option =
        try
            let path = latestPath root
            if not (File.Exists path) then None
            else tryParseRecord (File.ReadAllText path)
        with
        | :? IOException -> None
        | :? UnauthorizedAccessException -> None

    /// A NAMED baseline reading (`--since @runId`) — `None` when the run
    /// was never recorded here or its record is unreadable; the face
    /// refuses by name rather than silently falling back to latest.
    let loadRun (root: string) (runId: string) : HistoryRecord option =
        try
            let path = recordPath root runId
            if not (File.Exists path) then None
            else tryParseRecord (File.ReadAllText path)
        with
        | :? IOException -> None
        | :? UnauthorizedAccessException -> None
