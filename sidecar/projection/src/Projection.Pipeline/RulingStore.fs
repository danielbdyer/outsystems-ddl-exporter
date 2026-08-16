namespace Projection.Pipeline

open System
open System.Text.Json
open Projection.Core

/// align-II.1 (A53) — the durable keyed home of operator rulings. One
/// JSON document per ruled subject under `<store>/rulings/<digest16>.json`
/// (digest filenames per the SinkStore precedent; the full finding key
/// lives INSIDE the document). Keyed replace-by-key — a re-ruling
/// REPLACES the prior ruling for its key; append-only ruling HISTORY is
/// a named deferral past align-III.2 (`BasisAnchor.SinkEdition` is its
/// widen trigger). NOT a `LedgerSpec` (the align-II.0 standing ruling).
/// PRJ001: Pipeline boundary (I/O); typed-AST JSON both directions.
type RulingError =
    | ReadFailure of path: string * message: string
    | ParseFailure of path: string * message: string
    | WriteFailure of path: string * message: string

[<RequireQualifiedAccess>]
module RulingStore =

    [<Literal>]
    let private isoFormat = "O"

    /// `rulings/` under the store root — the keyed directory.
    let private rulingsDir (storeRoot: string) : string =
        IO.Path.Combine(storeRoot, "rulings")

    /// Deterministic per-key filename: lowercase hex SHA256 of the key
    /// text, first 16 chars (the SinkStore connDigest16 shape). The key
    /// text itself carries `:` (illegal in some filesystems), so the
    /// digest is the filename and the readable key lives in the document.
    let private fileNameOf (key: FindingKey) : string =
        let bytes = Text.Encoding.UTF8.GetBytes(FindingKey.text key)
        let hash = Security.Cryptography.SHA256.HashData(bytes)
        String.Concat(Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 16), ".json")  // LINT-ALLOW: terminal filename composition at the store boundary; two typed segments (hex digest prefix + fixed extension) — the SinkStore digest-filename idiom

    let private pathOf (storeRoot: string) (key: FindingKey) : string =
        IO.Path.Combine(rulingsDir storeRoot, fileNameOf key)

    let private writeBasis (jw: Utf8JsonWriter) (basis: BasisAnchor option) : unit =
        match basis with
        | None -> jw.WriteNull("basis")
        | Some anchor ->
            jw.WriteStartObject("basis")
            (match anchor with
             | BasisAnchor.Digest v ->
                 jw.WriteString("kind", "digest")
                 jw.WriteString("value", v)
             | BasisAnchor.Fingerprint v ->
                 jw.WriteString("kind", "fingerprint")
                 jw.WriteString("value", v)
             | BasisAnchor.FindingKey k ->
                 jw.WriteString("kind", "findingKey")
                 jw.WriteString("value", FindingKey.text k)
             | BasisAnchor.EvidenceDigest v ->
                 jw.WriteString("kind", "evidenceDigest")
                 jw.WriteString("value", v))
            jw.WriteEndObject()

    let private writeReopen (jw: Utf8JsonWriter) (reopen: ReopenCondition option) : unit =
        match reopen with
        | None -> jw.WriteNull("reopen")
        | Some cond ->
            jw.WriteStartObject("reopen")
            (match cond with
             | ReopenCondition.OnEvidenceChange ->
                 jw.WriteString("kind", "onEvidenceChange")
             | ReopenCondition.After at ->
                 jw.WriteString("kind", "after")
                 jw.WriteString("at", at.ToString(isoFormat, Globalization.CultureInfo.InvariantCulture)))
            jw.WriteEndObject()

    let private serialize (ruling: OperatorRuling<FindingKey>) : byte[] =
        use stream = new IO.MemoryStream()
        (use jw = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
         jw.WriteStartObject()
         jw.WriteString("findingKey", FindingKey.text ruling.Subject)
         jw.WriteString("verdict", RulingVerdict.token ruling.Verdict)
         writeBasis jw ruling.Basis
         jw.WriteString("by", ruling.By)
         jw.WriteString("at", ruling.At.ToString(isoFormat, Globalization.CultureInfo.InvariantCulture))
         (match ruling.Rationale with
          | Some r -> jw.WriteString("rationale", r)
          | None -> jw.WriteNull("rationale"))
         writeReopen jw ruling.Reopen
         jw.WriteEndObject())
        stream.ToArray()

    /// Persist one ruling, REPLACING any prior ruling for its key.
    /// Atomic (tmp + move-overwrite — the EstateHistory idiom); bytes
    /// are deterministic for an unchanged ruling (T1).
    let save (storeRoot: string) (ruling: OperatorRuling<FindingKey>) : Result<unit, RulingError> =
        let path = pathOf storeRoot ruling.Subject
        try
            IO.Directory.CreateDirectory(rulingsDir storeRoot) |> ignore
            let tmp = String.Concat(path, ".tmp")
            IO.File.WriteAllBytes(tmp, serialize ruling)
            IO.File.Move(tmp, path, overwrite = true)
            Ok ()
        with ex ->
            Error (WriteFailure (path, ex.Message))

    // ---- read side -------------------------------------------------------

    let private tryStr (el: JsonElement) (name: string) : string option =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String ->
            match v.GetString() with
            | null -> None
            | s -> Some s
        | _ -> None

    let private parseKey (text: string) : Result<FindingKey, string> =
        // align-II.2 hoist: the reconstruction lives on FindingKey itself
        // (second consumer — TighteningBinding parses persisted keys too).
        match FindingKey.tryParse text with
        | Some k -> Ok k
        | None -> Error (sprintf "malformed or unknown finding key '%s'" text)

    let private parseBasis (el: JsonElement) : Result<BasisAnchor option, string> =
        match el.TryGetProperty "basis" with
        | false, _ -> Error "record missing 'basis' field (null allowed; absence is malformed)"
        | true, v when v.ValueKind = JsonValueKind.Null -> Ok None
        | true, v when v.ValueKind = JsonValueKind.Object ->
            match tryStr v "kind", tryStr v "value" with
            | Some "digest", Some value         -> Ok (Some (BasisAnchor.Digest value))
            | Some "fingerprint", Some value    -> Ok (Some (BasisAnchor.Fingerprint value))
            | Some "evidenceDigest", Some value -> Ok (Some (BasisAnchor.EvidenceDigest value))
            | Some "findingKey", Some value ->
                match parseKey value with
                | Ok k -> Ok (Some (BasisAnchor.FindingKey k))
                | Error e -> Error e
            | Some other, _ -> Error (sprintf "unknown basis kind '%s'" other)
            | None, _ -> Error "basis object missing 'kind'"
        | _ -> Error "malformed 'basis' (expected object or null)"

    let private parseReopen (el: JsonElement) : Result<ReopenCondition option, string> =
        match el.TryGetProperty "reopen" with
        | false, _ -> Error "record missing 'reopen' field (null allowed; absence is malformed)"
        | true, v when v.ValueKind = JsonValueKind.Null -> Ok None
        | true, v when v.ValueKind = JsonValueKind.Object ->
            match tryStr v "kind" with
            | Some "onEvidenceChange" -> Ok (Some ReopenCondition.OnEvidenceChange)
            | Some "after" ->
                match tryStr v "at" with
                | Some s ->
                    match DateTimeOffset.TryParse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
                    | true, dto -> Ok (Some (ReopenCondition.After dto))
                    | _ -> Error (sprintf "malformed reopen 'at' instant '%s'" s)
                | None -> Error "reopen kind 'after' missing 'at'"
            | Some other -> Error (sprintf "unknown reopen kind '%s'" other)
            | None -> Error "reopen object missing 'kind'"
        | _ -> Error "malformed 'reopen' (expected object or null)"

    let private readRuling (el: JsonElement) : Result<OperatorRuling<FindingKey>, string> =
        match tryStr el "findingKey", tryStr el "verdict", tryStr el "by", tryStr el "at" with
        | Some keyText, Some verdictText, Some by, Some atText ->
            match parseKey keyText with
            | Error e -> Error e
            | Ok key ->
                match RulingVerdict.tryParse verdictText with
                | None -> Error (sprintf "unknown verdict token '%s'" verdictText)
                | Some verdict ->
                    match DateTimeOffset.TryParse(atText, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
                    | false, _ -> Error (sprintf "malformed 'at' instant '%s'" atText)
                    | true, at ->
                        match parseBasis el with
                        | Error e -> Error e
                        | Ok basis ->
                            match parseReopen el with
                            | Error e -> Error e
                            | Ok reopen ->
                                Ok
                                    { Subject = key
                                      Verdict = verdict
                                      Basis = basis
                                      By = by
                                      At = at
                                      Rationale = tryStr el "rationale"
                                      Reopen = reopen }
        | _ -> Error "record missing required 'findingKey' / 'verdict' / 'by' / 'at' string fields"

    let private parseDocument (path: string) (text: string) : Result<OperatorRuling<FindingKey>, RulingError> =
        try
            use doc = JsonDocument.Parse text
            if doc.RootElement.ValueKind <> JsonValueKind.Object then
                Error (ParseFailure (path, "expected a JSON object (one ruling per file)"))
            else
                match readRuling doc.RootElement with
                | Ok r -> Ok r
                | Error m -> Error (ParseFailure (path, m))
        with ex ->
            Error (ParseFailure (path, ex.Message))

    /// Load the ruling for one key. **A missing file is `Ok None`** — an
    /// unruled subject is pending by ABSENCE (the safe first-run default).
    /// A present-but-malformed file is a `ParseFailure` — never silently
    /// treated as unruled (that would lose recorded operator judgment).
    let load (storeRoot: string) (key: FindingKey) : Result<OperatorRuling<FindingKey> option, RulingError> =
        let path = pathOf storeRoot key
        if not (IO.File.Exists path) then Ok None
        else
            let textResult =
                try Ok (IO.File.ReadAllText path)
                with ex -> Error (ReadFailure (path, ex.Message))
            match textResult with
            | Error e -> Error e
            | Ok text ->
                match parseDocument path text with
                | Ok r -> Ok (Some r)
                | Error e -> Error e

    /// Load EVERY ruling in the store (the II.5 reception read). Missing
    /// directory is `Ok []`; the FIRST malformed file fail-closes the
    /// whole read, naming the file — a partial ruling read would silently
    /// lose operator judgment.
    let loadAll (storeRoot: string) : Result<OperatorRuling<FindingKey> list, RulingError> =
        let dir = rulingsDir storeRoot
        if not (IO.Directory.Exists dir) then Ok []
        else
            let files =
                IO.Directory.GetFiles(dir, "*.json")
                |> Array.sort
            files
            |> Array.fold
                (fun acc path ->
                    match acc with
                    | Error _ -> acc
                    | Ok rulings ->
                        let textResult =
                            try Ok (IO.File.ReadAllText path)
                            with ex -> Error (ReadFailure (path, ex.Message))
                        match textResult with
                        | Error e -> Error e
                        | Ok text ->
                            match parseDocument path text with
                            | Ok r -> Ok (r :: rulings)
                            | Error e -> Error e)
                (Ok [])
            |> function
               | Ok rulings -> Ok (List.rev rulings)
               | Error e -> Error e

    /// The located cause, in plain words (THE_VOICE §10).
    let describe (error: RulingError) : string =
        match error with
        | ReadFailure (path, message)  -> sprintf "the ruling store at %s could not be read — %s" path message
        | ParseFailure (path, message) -> sprintf "the ruling store at %s could not be parsed — %s" path message
        | WriteFailure (path, message) -> sprintf "the ruling store at %s could not be written — %s" path message
