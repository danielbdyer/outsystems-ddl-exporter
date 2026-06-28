namespace Projection.Pipeline

// LINT-ALLOW-FILE: terminal text composition at the lifecycle-store persistence boundary;
//   segments are typed and the persisted shape is JSON. `String.concat` is the
//   BCL primitive at this terminal boundary.

open System
open System.Text.Json
open Projection.Core
open Projection.Targets.Json

/// Boundary persistence for the durable provenance substrate (`EXECUTION_PLAN.md`
/// 6.H.2). The `EpisodicLifecycle` algebra lives in Core (pure); this module is
/// its durable JSON home, so the multi-episodic chain — the time-axis the
/// calculus integrates over — is *recorded and reconstructable* across runs
/// instead of existing only as in-memory test values (the morphology's "no
/// durable episode" gap). PRJ001: Pipeline boundary (I/O), not Core; typed-AST
/// serialization (`Utf8JsonWriter` / `JsonDocument`), no string composition.
///
/// **Composes the codec, does not re-encode the `Catalog`.** Each episode's
/// schema plane is serialized by `CatalogCodec` (the total / deterministic /
/// re-validating round-trip); this module embeds that output and frames the
/// chain (timeline + per-episode coordinate + data observation + refactorlog
/// reference). The statistical `Profile` is **not persisted** — per
/// `WAVE_6_ALGEBRA.md` §12.4 the data δ is substrate-fused, so its durable form
/// is the CDC capture series carried on `DataObservation`, not a serialized
/// Profile. A loaded episode therefore equals its `Episode.durableProjection`
/// (Profile reset to empty).
type LifecycleStoreError =
    | ReadFailure of path: string * message: string
    | ParseFailure of path: string * message: string
    | WriteFailure of path: string * message: string

[<RequireQualifiedAccess>]
module LifecycleStore =

    /// The located cause of a store failure, in plain words — never a raw DU
    /// dump on the operator surface (`THE_VOICE.md` §10: what · why · where).
    let describe (error: LifecycleStoreError) : string =
        match error with
        | LifecycleStoreError.ReadFailure (path, message)  -> sprintf "the timeline store at %s could not be read — %s" path message
        | LifecycleStoreError.ParseFailure (path, message) -> sprintf "the timeline store at %s could not be parsed — %s" path message
        | LifecycleStoreError.WriteFailure (path, message) -> sprintf "the timeline store at %s could not be written — %s" path message

    [<Literal>]
    let private isoFormat = "O"   // round-trippable ISO-8601 (DateTimeOffset)

    let private inv = System.Globalization.CultureInfo.InvariantCulture

    let private errorsToMessage (errs: ValidationError list) : string =
        errs |> List.map (fun e -> sprintf "%s: %s" e.Code e.Message) |> String.concat "; "

    // Error-type-generic bind/map for this module's `Result<_, string>` chain
    // (the Core `Result` module's bind/map are specialized to `ValidationError list`).
    let private bindR (f: 'a -> Result<'b, string>) (r: Result<'a, string>) : Result<'b, string> =
        match r with Ok v -> f v | Error e -> Error e
    let private mapR (f: 'a -> 'b) (r: Result<'a, string>) : Result<'b, string> =
        match r with Ok v -> Ok (f v) | Error e -> Error e

    // -- ENCODE --------------------------------------------------------------

    /// Environment serialized as a tagged object so the `Named` escape hatch
    /// round-trips unambiguously (a `Named "DEV"` cannot collide with `Dev`).
    let private writeEnvironment (jw: Utf8JsonWriter) (e: Environment) : unit =
        jw.WriteStartObject()
        (match e with
         | Environment.Dev     -> jw.WriteString("kind", "Dev")
         | Environment.Qa      -> jw.WriteString("kind", "Qa")
         | Environment.Uat     -> jw.WriteString("kind", "Uat")
         | Environment.Prod    -> jw.WriteString("kind", "Prod")
         | Environment.Named n -> jw.WriteString("kind", "Named"); jw.WriteString("name", n))
        jw.WriteEndObject()

    let private writeCoordinate (jw: Utf8JsonWriter) (c: EpisodeCoordinate) : unit =
        jw.WriteStartObject()
        jw.WriteNumber("versionOrdinal", Version.ordinal c.Version)
        jw.WriteString("versionLabel", Version.label c.Version)
        jw.WritePropertyName "environment"
        writeEnvironment jw c.Environment
        jw.WriteString("at", c.At.ToString(isoFormat, inv))
        jw.WriteEndObject()

    let private writeData (jw: Utf8JsonWriter) (d: DataObservation) : unit =
        jw.WriteStartObject()
        jw.WriteNumber("cdcCaptureCount", d.CdcCaptureCount)
        match d.CdcHandle with
        | Some h -> jw.WriteString("cdcHandle", h)
        | None   -> jw.WriteNull("cdcHandle")
        jw.WriteEndObject()

    /// The episode's **tolerance residual** (NM-34) — the accepted-divergence
    /// set as a name-sorted array of `ToleratedDivergence.name` tokens (the same
    /// tokens `Tolerance.parse` reads back). `Tolerance.strict` ⇒ `[]`. Sorted
    /// for byte-determinism (T1), matching `ChangeManifest.between`'s ordering.
    let private writeTolerances (jw: Utf8JsonWriter) (t: Tolerance) : unit =
        jw.WriteStartArray()
        t
        |> Tolerance.divergences
        |> Set.toList
        |> List.map ToleratedDivergence.name
        |> List.sort
        |> List.iter jw.WriteStringValue
        jw.WriteEndArray()

    /// The episode's **applied-transforms** overlay enumeration (NM-34) — each
    /// `(SsKey × OverlayAxis option)` row as `{ ssKey; overlay }`, where `ssKey`
    /// is the lossless `SsKey.serialize` form (NOT the lossy `rootOriginal`
    /// display) and `overlay` is the `OverlayAxis.name` token or `null` for the
    /// skeleton-only (`None`) rows. Order preserved (already `(SsKey, OverlayAxis
    /// option)`-sorted at its source).
    let private writeAppliedTransforms (jw: Utf8JsonWriter) (rows: (SsKey * OverlayAxis option) list) : unit =
        jw.WriteStartArray()
        for (ssKey, axisOpt) in rows do
            jw.WriteStartObject()
            jw.WriteString("ssKey", SsKey.serialize ssKey)
            match axisOpt with
            | Some axis -> jw.WriteString("overlay", OverlayAxis.name axis)
            | None      -> jw.WriteNull("overlay")
            jw.WriteEndObject()
        jw.WriteEndArray()

    let private writeEpisode (jw: Utf8JsonWriter) (e: Episode) : unit =
        jw.WriteStartObject()
        jw.WritePropertyName "coordinate"
        writeCoordinate jw e.Coordinate
        // The schema plane: embed the codec's deterministic output as a nested
        // object (the codec is the single source of catalog wire-format truth).
        jw.WritePropertyName "schema"
        jw.WriteRawValue(CatalogCodec.serialize e.Schema)
        match e.RefactorLogRef with
        | Some r -> jw.WriteString("refactorLogRef", r)
        | None   -> jw.WriteNull("refactorLogRef")
        jw.WritePropertyName "data"
        writeData jw e.Data
        // NM-34 — the provenance planes: the tolerance residual + the §5.5
        // applied-transforms overlay enumeration. `durableProjection` KEEPS
        // both, so persisting them is what makes a stored episode equal its own
        // `durableProjection` (before this, a provenance-bearing episode lost
        // them on the store round-trip).
        jw.WritePropertyName "tolerances"
        writeTolerances jw e.Tolerances
        jw.WritePropertyName "appliedTransforms"
        writeAppliedTransforms jw e.AppliedTransforms
        jw.WriteEndObject()

    let private serialize (lifecycle: EpisodicLifecycle) : byte[] =
        use stream = new System.IO.MemoryStream()
        // Pinned LF newlines (`JsonOptions.indented` sets NewLine = "\n"): the
        // lifecycle store is a durable artifact, so its bytes must be identical
        // across platforms (T1). A bare `JsonWriterOptions(Indented = true)`
        // inherits the host newline (CRLF on Windows) under .NET 9.
        (use jw = new Utf8JsonWriter(stream, JsonOptions.indented ())
         jw.WriteStartObject()
         jw.WriteString("timeline", Timeline.name (EpisodicLifecycle.timeline lifecycle))
         jw.WritePropertyName "episodes"
         jw.WriteStartArray()
         for e in EpisodicLifecycle.episodes lifecycle do
             writeEpisode jw e
         jw.WriteEndArray()
         jw.WriteEndObject())
        stream.ToArray()

    /// Persist the lifecycle to `path`, overwriting. Determinism + typed-AST per
    /// the disciplines; failures surface as `WriteFailure`.
    let save (path: string) (lifecycle: EpisodicLifecycle) : Result<unit, LifecycleStoreError> =
        try
            System.IO.File.WriteAllBytes(path, serialize lifecycle)
            Ok ()
        with ex ->
            Error (WriteFailure (path, ex.Message))

    // -- DECODE --------------------------------------------------------------

    let private prop (el: JsonElement) (name: string) : Result<JsonElement, string> =
        match el.TryGetProperty name with
        | true, v -> Ok v
        | _ -> Error (sprintf "missing field '%s'" name)

    let private asString (el: JsonElement) : Result<string, string> =
        if el.ValueKind = JsonValueKind.String then
            match el.GetString() with
            | null -> Error "string field returned null"
            | s -> Ok s
        else Error (sprintf "expected string, got %A" el.ValueKind)

    let private fieldStr (el: JsonElement) (name: string) : Result<string, string> =
        prop el name |> bindR asString

    let private optStr (el: JsonElement) (name: string) : string option =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String ->
            match v.GetString() with null -> None | s -> Some s
        | _ -> None

    let private fieldInt (el: JsonElement) (name: string) : Result<int, string> =
        match prop el name with
        | Error m -> Error m
        | Ok v ->
            if v.ValueKind = JsonValueKind.Number then
                match v.TryGetInt32() with
                | true, n -> Ok n
                | _ -> Error (sprintf "field '%s' is not an int32" name)
            else Error (sprintf "field '%s': expected number, got %A" name v.ValueKind)

    let private readEnvironment (el: JsonElement) : Result<Environment, string> =
        fieldStr el "kind"
        |> bindR (function
            | "Dev"  -> Ok Environment.Dev
            | "Qa"   -> Ok Environment.Qa
            | "Uat"  -> Ok Environment.Uat
            | "Prod" -> Ok Environment.Prod
            | "Named" -> fieldStr el "name" |> mapR Environment.Named
            | o -> Error (sprintf "unknown environment kind '%s'" o))

    let private readCoordinate (el: JsonElement) : Result<EpisodeCoordinate, string> =
        match fieldInt el "versionOrdinal", fieldStr el "versionLabel" with
        | Ok ordinal, Ok label ->
            match Version.create ordinal label with
            | Error errs -> Error (errorsToMessage errs)
            | Ok version ->
                match prop el "environment" |> bindR readEnvironment with
                | Error m -> Error m
                | Ok environment ->
                    let at =
                        match optStr el "at" with
                        | Some s ->
                            match DateTimeOffset.TryParse(s, inv, System.Globalization.DateTimeStyles.RoundtripKind) with
                            | true, dto -> dto
                            | _ -> DateTimeOffset.MinValue
                        | None -> DateTimeOffset.MinValue
                    Ok (EpisodeCoordinate.create version environment at)
        | Error m, _ -> Error m
        | _, Error m -> Error m

    let private readData (el: JsonElement) : Result<DataObservation, string> =
        fieldInt el "cdcCaptureCount"
        |> mapR (fun count -> DataObservation.create count (optStr el "cdcHandle"))

    /// The episode's tolerance residual (NM-34). A **missing** `tolerances`
    /// field reads as `Tolerance.strict` (forward-compatible with pre-NM-34
    /// stores). A present field is the name-token array `Tolerance.parse` reads;
    /// an **unrecognized** token is a hard error (fail-closed — the same
    /// `UnknownDivergence` safety `Tolerance.parse` guarantees; never silently
    /// widening or narrowing the canary's equivalence).
    let private readTolerances (el: JsonElement) : Result<Tolerance, string> =
        match el.TryGetProperty "tolerances" with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            let tokens : string list =
                v.EnumerateArray()
                |> Seq.choose (fun t ->
                    if t.ValueKind = JsonValueKind.String then
                        match t.GetString() with null -> None | s -> Some s
                    else None)
                |> Seq.toList
            match Tolerance.parse tokens with
            | Ok t -> Ok t
            | Error (UnknownDivergence token) ->
                Error (sprintf "unknown tolerated-divergence token '%s'" token)
        | true, v -> Error (sprintf "field 'tolerances': expected array, got %A" v.ValueKind)
        | _ -> Ok Tolerance.strict

    /// The episode's applied-transforms overlay enumeration (NM-34). A
    /// **missing** `appliedTransforms` field reads as `[]`. Each row is
    /// `{ ssKey; overlay }` — `ssKey` through `SsKey.deserialize` (the lossless
    /// inverse), `overlay` through `OverlayAxis.tryParse` (`null` ⇒ the `None`
    /// skeleton row). A malformed `ssKey` or an unknown `overlay` token is a hard
    /// error (the store is never silently partial — a half-loaded provenance
    /// plane would corrupt the change-accounting).
    let private readAppliedTransforms (el: JsonElement) : Result<(SsKey * OverlayAxis option) list, string> =
        match el.TryGetProperty "appliedTransforms" with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            let rec loop acc (rows: JsonElement list) =
                match rows with
                | [] -> Ok (List.rev acc)
                | row :: rest ->
                    match fieldStr row "ssKey" with
                    | Error m -> Error m
                    | Ok ssKeyText ->
                        match SsKey.deserialize ssKeyText with
                        | Error errs -> Error (errorsToMessage errs)
                        | Ok ssKey ->
                            let axisResult =
                                match optStr row "overlay" with
                                | None -> Ok None
                                | Some token ->
                                    match OverlayAxis.tryParse token with
                                    | Some axis -> Ok (Some axis)
                                    | None -> Error (sprintf "unknown overlay axis token '%s'" token)
                            match axisResult with
                            | Error m -> Error m
                            | Ok axisOpt -> loop ((ssKey, axisOpt) :: acc) rest
            loop [] (v.EnumerateArray() |> Seq.toList)
        | true, v -> Error (sprintf "field 'appliedTransforms': expected array, got %A" v.ValueKind)
        | _ -> Ok []

    let private readEpisode (el: JsonElement) : Result<Episode, string> =
        match prop el "coordinate" |> bindR readCoordinate with
        | Error m -> Error m
        | Ok coordinate ->
            match prop el "schema" with
            | Error m -> Error m
            | Ok schemaEl ->
                match CatalogCodec.deserialize (schemaEl.GetRawText()) with
                | Error errs -> Error (errorsToMessage errs)
                | Ok schema ->
                    match prop el "data" |> bindR readData with
                    | Error m -> Error m
                    | Ok data ->
                        // Profile is not persisted (§12.4) — a loaded episode is
                        // its own `durableProjection` (Profile.empty). The
                        // provenance planes (NM-34) ARE persisted and KEPT by
                        // `durableProjection`, so they are re-threaded here via
                        // `withProvenance` — that is what makes stored = durable.
                        match readTolerances el, readAppliedTransforms el with
                        | Error m, _ -> Error m
                        | _, Error m -> Error m
                        | Ok tolerances, Ok appliedTransforms ->
                            Episode.create coordinate schema Profile.empty (optStr el "refactorLogRef") data
                            |> Episode.withProvenance tolerances appliedTransforms
                            |> Ok

    /// Reconstruct the chain from a non-empty episode list, enforcing the
    /// monotone-history invariant via `EpisodicLifecycle.append` — the
    /// episode grain's ResumeAdmit (R3 / RI-3), run over every loaded edge:
    /// chain structure is verified at load; the B'≡B write witness is not
    /// re-verifiable here and is not pretended to be (see
    /// `MigrationRun.recordVerified`, the grain's WriteAdmit).
    let private buildLifecycle (timeline: Timeline) (episodes: Episode list) : Result<EpisodicLifecycle, string> =
        match episodes with
        | [] -> Error "lifecycle has no episodes (a lifecycle opens at genesis)"
        | genesis :: rest ->
            rest
            |> List.fold
                (fun acc ep ->
                    match acc with
                    | Error m -> Error m
                    | Ok lc ->
                        match EpisodicLifecycle.append ep lc with
                        | Ok lc' -> Ok lc'
                        | Error errs -> Error (errorsToMessage errs))
                (Ok (EpisodicLifecycle.genesis timeline genesis))

    /// Load the lifecycle from `path`. A **missing file is a `ParseFailure`** —
    /// unlike `ApprovalStore` (where "no file" is the empty registry), an
    /// `EpisodicLifecycle` has no empty value (it always opens at genesis), so a
    /// caller asking to load a non-existent timeline is a real error, not a
    /// safe default. A present-but-malformed file is also a `ParseFailure`
    /// (never silently partial — a half-loaded chain would corrupt the FTC).
    let load (path: string) : Result<EpisodicLifecycle, LifecycleStoreError> =
        if not (System.IO.File.Exists path) then
            Error (ParseFailure (path, "no lifecycle file at path"))
        else
            let textResult =
                try Ok (System.IO.File.ReadAllText path)
                with ex -> Error (ReadFailure (path, ex.Message))
            match textResult with
            | Error e -> Error e
            | Ok text ->
                try
                    use doc = JsonDocument.Parse text
                    let root = doc.RootElement
                    if root.ValueKind <> JsonValueKind.Object then
                        Error (ParseFailure (path, "expected a JSON object at the root"))
                    else
                        let parsed =
                            match fieldStr root "timeline" with
                            | Error m -> Error m
                            | Ok timelineName ->
                                match Timeline.create timelineName with
                                | Error errs -> Error (errorsToMessage errs)
                                | Ok timeline ->
                                    match prop root "episodes" with
                                    | Error m -> Error m
                                    | Ok episodesEl when episodesEl.ValueKind = JsonValueKind.Array ->
                                        let folded =
                                            episodesEl.EnumerateArray()
                                            |> Seq.toList
                                            |> List.fold
                                                (fun acc el ->
                                                    match acc with
                                                    | Error m -> Error m
                                                    | Ok eps ->
                                                        match readEpisode el with
                                                        | Ok ep -> Ok (ep :: eps)
                                                        | Error m -> Error m)
                                                (Ok [])
                                        match folded with
                                        | Error m -> Error m
                                        | Ok revEpisodes -> buildLifecycle timeline (List.rev revEpisodes)
                                    | Ok _ -> Error "field 'episodes': expected array"
                        match parsed with
                        | Ok lc -> Ok lc
                        | Error m -> Error (ParseFailure (path, m))
                with ex ->
                    Error (ParseFailure (path, ex.Message))

    /// Load the durable timeline at `path`, then run a pure `fromChain` over it,
    /// mapping BOTH failure modes to operator-facing strings: a malformed/absent
    /// store via `describe`, a non-composable chain via `onChainError`. The shared
    /// load→describe→run shape behind `EjectRun.fromStore` / `ReportRun.fromStore`
    /// (recon #25) — generic over the chain-result's error so each caller keeps its
    /// own typed `fromChain` and its own reconstruction-failure message.
    let withLoaded (fromChain: EpisodicLifecycle -> Result<'a, 'e>) (onChainError: string) (path: string) : Result<'a, string> =
        match load path with
        | Error e -> Error (describe e)
        | Ok chain ->
            match fromChain chain with
            | Ok value -> Ok value
            | Error _  -> Error onChainError
