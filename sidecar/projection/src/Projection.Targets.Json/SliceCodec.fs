namespace Projection.Targets.Json

open System.Text.Json
open Projection.Core

/// `SliceSpec ↔ JSON` codec — the durable, versioned **slice-definition
/// artifact** (the "use case" config the spec calls for). The operator hand-
/// authors or `slice-propose`-drafts it; `slice-extract --slice <path>` reads
/// it. **Total** over every `Predicate` / `TraversalDirection` arm (a missed
/// arm would be a silent drop, forbidden), **deterministic**, and
/// **re-validating** (A39) — decode rebuilds through `SliceSpec.create`, so a
/// hand-edited artifact with duplicate directives / a negative depth / no roots
/// is REFUSED on load. Round-trip law: `∀ s. deserialize (serialize s) = Ok s`.
[<RequireQualifiedAccess>]
module SliceCodec =

    [<Literal>]
    let version = 1

    // -- ENCODE ------------------------------------------------------------

    let private wCoordinate (jw: Utf8JsonWriter) (name: string) (c: EntityCoordinate) : unit =
        jw.WriteStartObject name
        jw.WriteString("module", c.Module)
        jw.WriteString("entity", c.Entity)
        jw.WriteEndObject()

    let rec private wPredicate (jw: Utf8JsonWriter) (p: Predicate) : unit =
        jw.WriteStartObject()
        (match p with
         | Predicate.All -> jw.WriteString("op", "all")
         | Predicate.Equals (c, v) ->
             jw.WriteString("op", "eq"); jw.WriteString("column", Name.value c); jw.WriteString("value", v)
         | Predicate.In (c, vs) ->
             jw.WriteString("op", "in"); jw.WriteString("column", Name.value c)
             jw.WriteStartArray("values")
             for v in vs do jw.WriteStringValue v
             jw.WriteEndArray()
         | Predicate.And ps ->
             jw.WriteString("op", "and")
             jw.WriteStartArray("terms")
             for sub in ps do wPredicate jw sub
             jw.WriteEndArray()
         | Predicate.Raw sql ->
             jw.WriteString("op", "raw"); jw.WriteString("sql", sql))
        jw.WriteEndObject()

    let private wDirection (jw: Utf8JsonWriter) (d: TraversalDirection) : unit =
        jw.WriteStartObject "direction"
        (match d with
         | TraversalDirection.Up         -> jw.WriteString("kind", "up")
         | TraversalDirection.Stop       -> jw.WriteString("kind", "stop")
         | TraversalDirection.Down depth -> jw.WriteString("kind", "down"); jw.WriteNumber("depth", depth))
        jw.WriteEndObject()

    let serialize (spec: SliceSpec) : string =
        use ms = new System.IO.MemoryStream()
        use jw = new Utf8JsonWriter(ms, JsonWriterOptions(Indented = true))
        jw.WriteStartObject()
        jw.WriteNumber("version", spec.Version)
        jw.WriteStartArray("roots")
        for r in spec.Roots do
            jw.WriteStartObject()
            wCoordinate jw "entity" r.Entity
            jw.WritePropertyName "predicate"
            wPredicate jw r.Predicate
            jw.WriteEndObject()
        jw.WriteEndArray()
        jw.WriteStartArray("directives")
        for d in spec.Directives do
            jw.WriteStartObject()
            wCoordinate jw "from" d.From
            jw.WriteString("relationship", d.Relationship)
            wDirection jw d.Direction
            jw.WriteEndObject()
        jw.WriteEndArray()
        jw.WriteEndObject()
        jw.Flush()
        System.Text.Encoding.UTF8.GetString(ms.ToArray())

    // -- DECODE (re-validating through SliceSpec.create) -------------------

    // Decode kernel — thin delegations to the shared `JsonCodecKernel` (prefix
    // `"slice"`), so the emitted error codes stay byte-identical.
    let private fail (code: string) (msg: string) : Result<'a> = JsonCodecKernel.fail code msg
    let private asString (el: JsonElement) : Result<string> = JsonCodecKernel.asString "slice" el
    let private prop (el: JsonElement) (name: string) : Result<JsonElement> = JsonCodecKernel.prop "slice" el name
    let private field (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a> = JsonCodecKernel.field "slice" el name read

    let private readName (el: JsonElement) : Result<Name> =
        asString el |> Result.bind Name.create

    let private readCoordinate (el: JsonElement) : Result<EntityCoordinate> =
        field el "module" asString
        |> Result.bind (fun m -> field el "entity" asString |> Result.map (fun e -> { Module = m; Entity = e }))

    let rec private readPredicate (el: JsonElement) : Result<Predicate> =
        field el "op" asString
        |> Result.bind (function
            | "all" -> Ok Predicate.All
            | "eq"  ->
                field el "column" readName
                |> Result.bind (fun c -> field el "value" asString |> Result.map (fun v -> Predicate.Equals (c, v)))
            | "in"  ->
                field el "column" readName
                |> Result.bind (fun c ->
                    match el.TryGetProperty "values" with
                    | true, vs when vs.ValueKind = JsonValueKind.Array ->
                        vs.EnumerateArray() |> Seq.map asString |> Result.collect |> Result.map (fun vals -> Predicate.In (c, vals))
                    | _ -> fail "slice.predicate.in.values" "'in' predicate missing a 'values' array")
            | "and" ->
                (match el.TryGetProperty "terms" with
                 | true, ts when ts.ValueKind = JsonValueKind.Array ->
                     ts.EnumerateArray() |> Seq.map readPredicate |> Result.collect |> Result.map Predicate.And
                 | _ -> fail "slice.predicate.and.terms" "'and' predicate missing a 'terms' array")
            | "raw" -> field el "sql" asString |> Result.map Predicate.Raw
            | o     -> fail "slice.predicate.unknown" (sprintf "unknown predicate op '%s'" o))

    let private readDirection (el: JsonElement) : Result<TraversalDirection> =
        field el "kind" asString
        |> Result.bind (function
            | "up"   -> Ok TraversalDirection.Up
            | "stop" -> Ok TraversalDirection.Stop
            | "down" ->
                match prop el "depth" with
                | Ok d when d.ValueKind = JsonValueKind.Number -> Ok (TraversalDirection.Down (d.GetInt32()))
                | Ok _    -> fail "slice.direction.depth" "'down' direction has a non-numeric depth"
                | Error e -> Error e
            | o      -> fail "slice.direction.unknown" (sprintf "unknown traversal direction '%s'" o))

    let private readRoot (el: JsonElement) : Result<RootSpec> =
        field el "entity" readCoordinate
        |> Result.bind (fun coord ->
            field el "predicate" readPredicate |> Result.map (fun p -> { Entity = coord; Predicate = p }))

    let private readDirective (el: JsonElement) : Result<RelationshipDirective> =
        field el "from" readCoordinate
        |> Result.bind (fun from ->
            field el "relationship" asString
            |> Result.bind (fun rel ->
                field el "direction" readDirection
                |> Result.map (fun dir -> { From = from; Relationship = rel; Direction = dir })))

    let deserialize (json: string) : Result<SliceSpec> =
        match (try Ok (JsonDocument.Parse json) with ex -> fail "slice.parse" ex.Message) with
        | Error e -> Error e
        | Ok parsed ->
            use doc = parsed
            let root = doc.RootElement
            let ver =
                // Optional for hand-authoring (a `slices` block in projection.json
                // need not repeat it); render always emits it, so the round-trip
                // holds. Absent ⇒ the current schema version.
                match root.TryGetProperty "version" with
                | true, v when v.ValueKind = JsonValueKind.Number -> Ok (v.GetInt32())
                | true, _ -> fail "slice.version" "field 'version' is not a number"
                | _       -> Ok SliceSpec.CurrentVersion
            let roots =
                match root.TryGetProperty "roots" with
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    v.EnumerateArray() |> Seq.map readRoot |> Result.collect
                | _ -> Ok []
            let directives =
                match root.TryGetProperty "directives" with
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    v.EnumerateArray() |> Seq.map readDirective |> Result.collect
                | _ -> Ok []
            match ver, roots, directives with
            | Ok v, Ok rs, Ok ds -> SliceSpec.create v rs ds   // A39 — re-validate on decode
            | Error e, _, _ -> Error e
            | _, Error e, _ -> Error e
            | _, _, Error e -> Error e
