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
        jw.WriteEndObject()

    let private serialize (lifecycle: EpisodicLifecycle) : byte[] =
        use stream = new System.IO.MemoryStream()
        (use jw = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
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
                        // its own `durableProjection` (Profile.empty).
                        Ok (Episode.create coordinate schema Profile.empty (optStr el "refactorLogRef") data)

    /// Reconstruct the chain from a non-empty episode list, enforcing the
    /// monotone-history invariant via `EpisodicLifecycle.append`.
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
