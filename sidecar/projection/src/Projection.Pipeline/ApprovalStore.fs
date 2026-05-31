namespace Projection.Pipeline

open System
open System.Text.Json
open Projection.Core

/// Boundary persistence for the R6 operator-approval registry (Wave-3 slice
/// 3.2). The `ApprovalRegistry` algebra lives in Core (pure); this module is
/// its durable JSON home so operator sign-off — R6's flip gate — is *recorded
/// and consultable* across runs, instead of being constructed and discarded
/// by the `approve` verb. PRJ001: Pipeline boundary (I/O), not Core; typed-AST
/// serialization (`Utf8JsonWriter` / `JsonDocument`), no string composition.
type ApprovalError =
    | ReadFailure of path: string * message: string
    | ParseFailure of path: string * message: string
    | WriteFailure of path: string * message: string

[<RequireQualifiedAccess>]
module ApprovalStore =

    [<Literal>]
    let private isoFormat = "O"   // round-trippable ISO-8601 (DateTimeOffset)

    let private decisionToken (d: ApprovalDecision) : string =
        match d with
        | Approved -> "Approved"
        | Rejected -> "Rejected"
        | Pending  -> "Pending"

    let private parseDecision (s: string) : ApprovalDecision option =
        match s with
        | "Approved" -> Some Approved
        | "Rejected" -> Some Rejected
        | "Pending"  -> Some Pending
        | _ -> None

    let private writeRecord (jw: Utf8JsonWriter) (r: ApprovalRecord) : unit =
        jw.WriteStartObject()
        jw.WriteString("policyVersion", r.PolicyVersion)
        jw.WriteString("decision", decisionToken r.Decision)
        match r.ApprovedBy with
        | Some a -> jw.WriteString("approvedBy", a)
        | None -> jw.WriteNull("approvedBy")
        jw.WriteString("at", r.At.ToString(isoFormat, System.Globalization.CultureInfo.InvariantCulture))
        match r.Rationale with
        | Some t -> jw.WriteString("rationale", t)
        | None -> jw.WriteNull("rationale")
        jw.WriteEndObject()

    /// Serialize the registry to a deterministic JSON array (records ordered
    /// by digest key, so re-saving an unchanged registry is byte-stable — T1).
    let private serialize (registry: ApprovalRegistry) : byte[] =
        use stream = new System.IO.MemoryStream()
        (use jw = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
         jw.WriteStartArray()
         for (_, r) in registry.ByDigest |> Map.toList do
             writeRecord jw r
         jw.WriteEndArray())
        stream.ToArray()

    /// Persist the registry to `path`, overwriting. Determinism + typed-AST
    /// per the disciplines; failures surface as `WriteFailure`.
    let save (path: string) (registry: ApprovalRegistry) : Result<unit, ApprovalError> =
        try
            System.IO.File.WriteAllBytes(path, serialize registry)
            Ok ()
        with ex ->
            Error (WriteFailure (path, ex.Message))

    let private readRecord (el: JsonElement) : Result<ApprovalRecord, string> =
        let tryStr (name: string) : string option =
            match el.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.String ->
                match v.GetString() with
                | null -> None
                | s -> Some s
            | _ -> None
        match tryStr "policyVersion", tryStr "decision" with
        | Some pv, Some decStr ->
            match parseDecision decStr with
            | None -> Error (sprintf "unknown decision token '%s'" decStr)
            | Some decision ->
                let at =
                    match tryStr "at" with
                    | Some s ->
                        match DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) with
                        | true, dto -> dto
                        | _ -> DateTimeOffset.MinValue
                    | None -> DateTimeOffset.MinValue
                Ok
                    { PolicyVersion = pv
                      Decision      = decision
                      ApprovedBy    = tryStr "approvedBy"
                      At            = at
                      Rationale     = tryStr "rationale" }
        | _ -> Error "record missing required 'policyVersion' / 'decision' string fields"

    /// Load the registry from `path`. A **missing file is `Ok empty`** — a
    /// not-yet-created store is an empty registry (the safe first-run default),
    /// not an error. A present-but-malformed file is a `ParseFailure` (never
    /// silently treated as empty — that would lose recorded approvals).
    let load (path: string) : Result<ApprovalRegistry, ApprovalError> =
        if not (System.IO.File.Exists path) then Ok ApprovalRegistry.empty
        else
            let textResult =
                try Ok (System.IO.File.ReadAllText path)
                with ex -> Error (ReadFailure (path, ex.Message))
            match textResult with
            | Error e -> Error e
            | Ok text ->
                try
                    use doc = JsonDocument.Parse text
                    if doc.RootElement.ValueKind <> JsonValueKind.Array then
                        Error (ParseFailure (path, "expected a JSON array of approval records"))
                    else
                        // Pure fold with a short-circuiting Result accumulator:
                        // the first malformed record stops the fold (fail-closed —
                        // a partial load would silently lose later approvals).
                        let folded =
                            doc.RootElement.EnumerateArray()
                            |> Seq.toList
                            |> List.fold
                                (fun acc el ->
                                    match acc with
                                    | Error _ -> acc
                                    | Ok reg ->
                                        match readRecord el with
                                        | Ok r -> Ok (ApprovalRegistry.record r reg)
                                        | Error m -> Error m)
                                (Ok ApprovalRegistry.empty)
                        match folded with
                        | Ok reg -> Ok reg
                        | Error m -> Error (ParseFailure (path, m))
                with ex ->
                    Error (ParseFailure (path, ex.Message))
