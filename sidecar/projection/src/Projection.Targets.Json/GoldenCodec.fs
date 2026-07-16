namespace Projection.Targets.Json

open System.Text.Json
open Projection.Core

/// One entity's rows in the portable golden dataset — LOGICAL (entity `Name` +
/// attribute-`Name`-keyed values), decoupled from any environment's physical
/// names or surrogate identities, so the dataset re-materializes against ANY
/// target whose schema is congruent by coordinate (the eSpace-divergent-
/// physical-name requirement).
type GoldenEntity =
    { Entity : string
      /// WP-3 (F11): a cell is `string option` — `None` is SQL NULL
      /// (serialized as JSON `null`), `Some ""` a genuine empty string.
      Rows   : Map<Name, string option> list }

/// The portable, committable **golden dataset** (Slice 6 — the deterministic
/// emission vehicle): the closure's row-set serialized in logical space. The
/// re-materializable golden baseline an environment can be reset to.
type GoldenDataset =
    { Version  : int
      Entities : GoldenEntity list }

[<RequireQualifiedAccess>]
module GoldenDataset =

    /// Version 2 (WP-3, F11): NULL cells serialize as JSON `null`; a v1
    /// dataset's `""` still reads as NULL (the retired sentinel era).
    [<Literal>]
    let CurrentVersion = 2

    /// Project a closed `Closure.ClosureState` into the portable dataset, in
    /// LOGICAL space: each kind's entity `Name`, rows sorted by PK value,
    /// columns by `Name`. Deterministic (T1 — byte-identical from byte-
    /// identical input). Kinds absent from the catalog are skipped.
    let ofClosure (catalog: Catalog) (state: Closure.ClosureState) : GoldenDataset =
        let entities =
            state.Rows
            |> Map.toList
            |> List.choose (fun (kindKey, rowsByPk) ->
                Catalog.tryFindKind kindKey catalog
                |> Option.map (fun k ->
                    let pk = Kind.primaryKey k |> List.tryHead |> Option.map (fun a -> a.Name)
                    let rows =
                        rowsByPk
                        |> Map.toList
                        |> List.map snd
                        |> List.sortBy (fun r ->
                            match pk with
                            | Some n -> StaticRow.valueOrEmpty n r
                            | None   -> "")
                        |> List.map (fun r -> r.Values)
                    { Entity = Name.value k.Name; Rows = rows }))
            |> List.sortBy (fun e -> e.Entity)
        { Version = CurrentVersion; Entities = entities }

/// `GoldenDataset ↔ JSON` codec — the durable golden artifact. **Total**,
/// **deterministic (T1)** (fixed write order; entities by name, rows by PK,
/// columns by name), **round-tripping** (`∀ ds. deserialize (serialize ds) = Ok ds`).
[<RequireQualifiedAccess>]
module GoldenCodec =

    [<Literal>]
    let version = 1

    // -- ENCODE ------------------------------------------------------------

    let serialize (ds: GoldenDataset) : string =
        use ms = new System.IO.MemoryStream()
        use jw = new Utf8JsonWriter(ms, JsonWriterOptions(Indented = true))
        jw.WriteStartObject()
        jw.WriteNumber("version", ds.Version)
        jw.WriteStartArray("entities")
        for e in ds.Entities do
            jw.WriteStartObject()
            jw.WriteString("entity", e.Entity)
            jw.WriteStartArray("rows")
            for row in e.Rows do
                jw.WriteStartObject()
                for (name, v) in (row |> Map.toList |> List.sortBy (fun (n, _) -> Name.value n)) do
                    match v with
                    | Some s -> jw.WriteString(Name.value name, s)
                    | None   -> jw.WriteNull(Name.value name)
                jw.WriteEndObject()
            jw.WriteEndArray()
            jw.WriteEndObject()
        jw.WriteEndArray()
        jw.WriteEndObject()
        jw.Flush()
        System.Text.Encoding.UTF8.GetString(ms.ToArray())

    // -- DECODE ------------------------------------------------------------

    // Decode kernel — thin delegations to the shared `JsonCodecKernel` (prefix
    // `"golden"`), so the emitted error codes stay byte-identical.
    let private fail (code: string) (msg: string) : Result<'a> = JsonCodecKernel.fail code msg
    let private asString (el: JsonElement) : Result<string> = JsonCodecKernel.asString "golden" el
    let private prop (el: JsonElement) (name: string) : Result<JsonElement> = JsonCodecKernel.prop "golden" el name

    /// Version-gated cell read (WP-3): JSON `null` → NULL; a string is the
    /// value — except in v1 datasets, where `""` was the universal NULL
    /// sentinel and still reads as NULL.
    let private readRow (ver: int) (el: JsonElement) : Result<Map<Name, string option>> =
        if el.ValueKind <> JsonValueKind.Object then fail "golden.expectedRowObject" "row is not a JSON object"
        else
            el.EnumerateObject()
            |> Seq.map (fun p ->
                let value =
                    match p.Value.ValueKind with
                    | JsonValueKind.Null -> Ok None
                    | JsonValueKind.String ->
                        match p.Value.GetString() with
                        | null -> Ok None // unreachable for a String element; read as NULL
                        | s -> if ver < 2 && s = "" then Ok None else Ok (Some s)
                    | _ -> fail "golden.expectedString" (sprintf "field '%s': expected string or null" p.Name)
                value
                |> Result.bind (fun v -> Name.create p.Name |> Result.map (fun n -> n, v)))
            |> Result.collect
            |> Result.map Map.ofList

    let private readEntity (ver: int) (el: JsonElement) : Result<GoldenEntity> =
        prop el "entity"
        |> Result.bind asString
        |> Result.bind (fun entity ->
            match el.TryGetProperty "rows" with
            | true, rowsEl when rowsEl.ValueKind = JsonValueKind.Array ->
                rowsEl.EnumerateArray() |> Seq.map (readRow ver) |> Result.collect
                |> Result.map (fun rows -> { Entity = entity; Rows = rows })
            | true, _ -> fail "golden.expectedArray" "field 'rows': expected array"
            | _       -> Ok { Entity = entity; Rows = [] })

    let deserialize (json: string) : Result<GoldenDataset> =
        match (try Ok (JsonDocument.Parse json) with ex -> fail "golden.parse" ex.Message) with
        | Error e -> Error e
        | Ok doc ->
            use doc = doc
            let root = doc.RootElement
            let decodedVersion =
                match prop root "version" with
                | Ok v when v.ValueKind = JsonValueKind.Number -> Ok (v.GetInt32())
                | Ok _  -> fail "golden.version" "field 'version' is not a number"
                | Error e -> Error e
            match decodedVersion with
            | Error e -> Error e
            | Ok ver ->
                let entities =
                    match root.TryGetProperty "entities" with
                    | true, v when v.ValueKind = JsonValueKind.Array ->
                        v.EnumerateArray() |> Seq.map (readEntity ver) |> Result.collect
                    | true, _ -> fail "golden.expectedArray" "field 'entities': expected array"
                    | _       -> Ok []
                entities |> Result.map (fun ents -> { Version = ver; Entities = ents })
