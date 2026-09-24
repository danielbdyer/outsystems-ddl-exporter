namespace Projection.Pipeline

// LINT-ALLOW-FILE: the portable proof manifest (P2 — offline verification).
//   Machine-local file paths + structured JSON at a persistence boundary — the
//   SAME sidecar codec + atomic-write + fail-closed idiom `FidelityProofCache` /
//   `EstateEvidenceStore` established (this is `FidelityProofCache` with the
//   per-kind `KindFingerprint` replaced by the STRONG `RowDigestFold.TableDigest`
//   and the freshness predicate replaced by a portable capture + offline
//   reconcile). The one terminal-text surface is the JSON compose/parse; the
//   build (`ofReport`) is pure.

open System
open System.IO
open System.Text.Json.Nodes
open Projection.Core

/// One kind's captured SOURCE-content digest — the `RowDigestFold` aggregate
/// (order-independent; digests a physical stream and a logical stream IDENTICALLY
/// across the rename gap) plus its row count, keyed by the model's `SsKey`. The
/// manifest commits to this ONE digest plane; the server-digest plane is never
/// mixed in (their values differ by construction — `ServerDigest.fs`).
type ManifestKind =
    {
        Kind     : SsKey
        KindName : string
        Digest   : RowDigestFold.TableDigest
    }

/// A PORTABLE capture of a source estate's per-kind content digests plus the
/// alignment provenance, so a target the tool did NOT stage (a database the
/// operator applied themselves) can be verified against it WITHOUT the live
/// source (P2). `ModelHash` is the alignment basis's logical-shape digest — a
/// target read under a DIFFERENT model is a named refusal, never a false verdict.
/// `TolerancesInForce` records the comparator's tolerance posture at capture, so
/// the reconcile states — and holds — the same basis. The digest plane is
/// `RowDigestFold` (pure, server-free, cross-rename-gap-stable), stamped in the
/// file so a future plane cannot be silently mis-read.
type ProofManifest =
    {
        /// The source estate the digests were captured from (operator-facing).
        SourceLabel       : string
        CapturedAtUtc     : DateTimeOffset
        /// The alignment model's logical-shape digest (`FidelityProofCache.modelHash`).
        ModelHash         : string
        TolerancesInForce : string list
        Kinds             : ManifestKind list
    }

[<RequireQualifiedAccess>]
module ProofManifest =

    /// The manifest's on-disk format version — bumped only on a breaking codec
    /// change (the `GoldenCodec` version precedent). Read back and checked so an
    /// unknown version fails closed rather than mis-parsing.
    [<Literal>]
    let Version : int = 1

    /// The one digest plane this manifest commits to (stamped in the file so a
    /// reader can refuse a plane mismatch rather than compare incomparable hex).
    [<Literal>]
    let Plane : string = "rowDigestFold"

    // -- the builder (pure — the SOURCE side of a completed fidelity run) -------

    /// Build a manifest from a completed fidelity run's report: the SOURCE side's
    /// per-kind digests (the estate's rows in logical-aligned form — exactly what
    /// a reconcile re-derives from a target that carries the same logical shape).
    /// The clock and the model hash arrive from the boundary (Core is clockless).
    let ofReport (capturedAtUtc: DateTimeOffset) (modelHash: string) (report: RowFidelityReport) : ProofManifest =
        { SourceLabel       = report.BeforeLabel
          CapturedAtUtc     = capturedAtUtc
          ModelHash         = modelHash
          TolerancesInForce = report.TolerancesInForce
          Kinds =
            report.Kinds
            |> List.map (fun v -> { Kind = v.Kind; KindName = v.KindName; Digest = v.Source }) }

    // -- the codec (the `FidelityProofCache` / `EstateEvidenceStore` sidecar shape) --

    /// Compose the manifest to canonical indented JSON. Kinds are emitted in
    /// `SsKey`-sorted order so the bytes are deterministic (T1 — same manifest,
    /// same bytes).
    let toJson (m: ProofManifest) : string =
        let root = JsonObject()
        root.["version"] <- JsonValue.Create Version
        root.["plane"] <- JsonValue.Create Plane
        root.["sourceLabel"] <- JsonValue.Create m.SourceLabel
        root.["capturedAtUtc"] <- JsonValue.Create(m.CapturedAtUtc.ToString "O")
        root.["modelHash"] <- JsonValue.Create m.ModelHash
        let tols = JsonArray()
        for t in m.TolerancesInForce do tols.Add(JsonValue.Create t)
        root.["tolerancesInForce"] <- tols
        let kinds = JsonArray()
        for k in m.Kinds |> List.sortBy (fun k -> SsKey.serialize k.Kind) do
            let o = JsonObject()
            o.["kind"] <- JsonValue.Create(SsKey.serialize k.Kind)
            o.["kindName"] <- JsonValue.Create k.KindName
            o.["aggregate"] <- JsonValue.Create k.Digest.Aggregate
            o.["count"] <- JsonValue.Create k.Digest.Count
            kinds.Add o
        root.["kinds"] <- kinds
        root.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true))

    let private tryNode (o: JsonObject) (key: string) : JsonNode option =
        match o.TryGetPropertyValue key with
        | true, node -> Option.ofObj node
        | _ -> None

    let private tryStr (o: JsonObject) (key: string) : string option =
        tryNode o key
        |> Option.bind (fun n -> try Some (n.GetValue<string>()) with :? InvalidOperationException | :? FormatException -> None)

    let private tryI64 (o: JsonObject) (key: string) : int64 option =
        tryNode o key
        |> Option.bind (fun n -> try Some (n.GetValue<int64>()) with :? InvalidOperationException | :? FormatException -> None)

    let private tryI32 (o: JsonObject) (key: string) : int option =
        tryNode o key
        |> Option.bind (fun n -> try Some (n.GetValue<int>()) with :? InvalidOperationException | :? FormatException -> None)

    let private manifestKindOfNode (node: JsonNode) : ManifestKind option =
        match node with
        | :? JsonObject as o ->
            match tryStr o "kind" |> Option.bind (fun s -> SsKey.deserialize s |> Result.toOption),
                  tryStr o "kindName",
                  tryStr o "aggregate",
                  tryI64 o "count" with
            | Some kind, Some name, Some agg, Some count ->
                Some { Kind = kind; KindName = name; Digest = { Aggregate = agg; Count = count } }
            | _ -> None
        | _ -> None

    /// Parse a manifest from its JSON text — `None` when malformed, version-
    /// mismatched, plane-mismatched, or missing any load-bearing field
    /// (fail-closed: a torn manifest reconciles to NOTHING, never to a false
    /// green). Pure; the file read is `tryRead`.
    let tryParse (text: string) : ProofManifest option =
        try
            match Option.ofObj (JsonNode.Parse text) with
            | Some (:? JsonObject as o) ->
                // Version + plane gate: an unknown version or a foreign digest
                // plane is refused, not mis-compared.
                match tryI32 o "version", tryStr o "plane" with
                | Some v, Some plane when v = Version && plane = Plane ->
                    let kinds =
                        tryNode o "kinds"
                        |> Option.bind (function :? JsonArray as a -> Some a | _ -> None)
                        |> Option.bind (fun arr ->
                            let parsed = [ for n in arr do match Option.ofObj n with Some node -> yield manifestKindOfNode node | None -> () ]
                            if parsed |> List.forall Option.isSome then Some (List.choose id parsed) else None)
                    let captured =
                        tryStr o "capturedAtUtc"
                        |> Option.bind (fun s ->
                            match DateTimeOffset.TryParse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
                            | true, dt -> Some dt
                            | _ -> None)
                    let tols =
                        tryNode o "tolerancesInForce"
                        |> Option.bind (function :? JsonArray as a -> Some a | _ -> None)
                        |> Option.map (fun arr -> [ for n in arr do match Option.ofObj n |> Option.bind (fun n -> try Some (n.GetValue<string>()) with _ -> None) with Some s -> yield s | None -> () ])
                        |> Option.defaultValue []
                    match tryStr o "sourceLabel", tryStr o "modelHash", captured, kinds with
                    | Some label, Some mh, Some at, Some ks ->
                        Some { SourceLabel = label; CapturedAtUtc = at; ModelHash = mh; TolerancesInForce = tols; Kinds = ks }
                    | _ -> None
                | _ -> None
            | _ -> None
        with :? System.Text.Json.JsonException -> None

    // -- the I/O surface (fail-closed read; atomic, RESULT-bearing write) -------

    /// Read a manifest from a path — `None` when absent, unreadable, or malformed
    /// (fail-closed, the `FidelityProofCache.tryRead` posture).
    let tryRead (path: string) : ProofManifest option =
        try
            if not (File.Exists path) then None else tryParse (File.ReadAllText path)
        with
        | :? IOException -> None
        | :? UnauthorizedAccessException -> None

    /// Persist a manifest to a path (atomic — `*.tmp` + move). UNLIKE the advisory
    /// cache write, this is RESULT-bearing: `--capture` is an explicitly-requested
    /// artifact, so a write failure is a named error the caller surfaces, never a
    /// silent drop.
    let write (path: string) (m: ProofManifest) : Result<unit> =
        try
            // Pattern-match narrows the nullable `GetDirectoryName` result to a
            // non-null dir in the last arm (a bare-filename path has no dir).
            (match Path.GetDirectoryName path with
             | null | "" -> ()
             | dir -> Directory.CreateDirectory dir |> ignore)
            let tmp = path + ".tmp"
            File.WriteAllText(tmp, toJson m)
            File.Move(tmp, path, overwrite = true)
            Result.success ()
        with ex ->
            Result.failureOf
                (ValidationError.createWithMetadata
                    "manifest.write.failed"
                    "The proof manifest could not be written."
                    (Map.ofList [ "path", Some path; "detail", Some ex.Message ]))
