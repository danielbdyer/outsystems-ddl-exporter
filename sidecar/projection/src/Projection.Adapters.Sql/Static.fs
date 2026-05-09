namespace Projection.Adapters.Sql

open System.Text.Json
open Projection.Core

/// Boundary adapter — converts V1's static-data JSON shape into V2's
/// `Static` modality populations on a target catalog.
///
/// V1 input shape (per `tests/Fixtures/static-data/static-entities.*.json`):
///
///   ```
///   { "tables": [
///       { "schema": "dbo",
///         "table":  "OSUSR_DEF_CITY",
///         "rows":   [ { "ID": 1, "NAME": "Lisbon", "ISACTIVE": true }, ... ] } ] }
///   ```
///
/// V2 output: the same `Catalog` with each `Static`-flagged kind's
/// populations filled in. `StaticRow.Identifier` derives from the kind's
/// `SsKey` plus the row's PK column value(s) joined with `:`. Cell
/// values are coerced to canonical invariant-culture strings — that
/// coercion is the boundary's job per the `EntitySeedDeterminizer`
/// admire entry's "split" placement (the type-aware comparison V1 did
/// stays at the boundary; the pure pass operates on canonical strings).
///
/// The adapter takes the JSON content as a string (not a file path or a
/// V1-typed object). This honors the cherry-pick discipline — the
/// sidecar references no trunk source files — and matches the algebra:
/// the V1↔V2 boundary is data, not typed cross-references. JSON is the
/// wire format; SQL Server is the upstream source. Future SQL-direct
/// adapters live alongside as a separate project (likely C#) when real
/// SQL Server I/O joins V2; this file handles V1's serialized form.
[<RequireQualifiedAccess>]
module Static =

    let private adapterError (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    /// Coerce a JSON cell to a canonical invariant-culture string. The
    /// V1 `EntitySeedDeterminizer`'s full type-aware coercion (numeric →
    /// decimal, DateTime, byte[] hex, etc.) is the boundary's
    /// responsibility per ADMIRE. This synthetic-milestone form covers
    /// the JSON primitive cases (string, number, boolean, null) and
    /// falls back to raw JSON text for objects/arrays. When a real V1
    /// fixture surfaces a divergence — e.g., decimals formatted
    /// differently between V1's `Convert.ToString(invariantCulture)` and
    /// `JsonElement.GetRawText()` — tighten the coercion here.
    let private invariantString (cell: JsonElement) : string =
        match cell.ValueKind with
        | JsonValueKind.String -> cell.GetString() |> Option.ofObj |> Option.defaultValue ""
        | JsonValueKind.Number -> cell.GetRawText()
        | JsonValueKind.True   -> "true"
        | JsonValueKind.False  -> "false"
        | JsonValueKind.Null   -> ""
        | _                    -> cell.GetRawText()

    /// Derive a row's `SsKey` from its parent kind's SsKey plus the
    /// row's PK value(s). Deterministic per A5: same PK values yield
    /// the same identifier. Per slice 5.5 / `CHAPTER_3_PRESCOPE_
    /// ARTIFACTBYKIND_REFACTOR.md` §7, the row identifier uses the
    /// `OS_ROW` synthesis source so A1's bound is type-visible at the
    /// variant tag.
    let private deriveRowIdentifier (kindKey: SsKey) (pkValues: string list) : Result<SsKey> =
        let suffix = String.concat ":" pkValues
        let basis = sprintf "%s_%s" (SsKey.rootOriginal kindKey) suffix
        SsKey.synthesized "OS_ROW" basis

    /// Build a `(Name * string)` value pair for every attribute on the
    /// kind whose JSON cell is present in the row. Attributes whose
    /// cell is absent are silently omitted — the catalog template
    /// decides whether absence is an error (e.g., via a downstream
    /// validation pass).
    let private buildValues (kind: Kind) (row: JsonElement) : Map<Name, string> =
        kind.Attributes
        |> List.choose (fun a ->
            match row.TryGetProperty(a.Column.ColumnName) with
            | true, cell -> Some (a.Name, invariantString cell)
            | false, _   -> None)
        |> Map.ofList

    /// Convert a JSON row to a V2 `StaticRow`, returning `Failure` if
    /// the row is malformed (not an object) or any PK column is missing.
    let private convertRow (kind: Kind) (pkAttrs: Attribute list) (row: JsonElement) : Result<StaticRow> =
        if row.ValueKind <> JsonValueKind.Object then
            Result.failureOf
                (adapterError
                    "staticAdapter.row.shape"
                    (sprintf "Row in kind '%s' is not a JSON object." (Name.value kind.Name)))
        else
            // Collect PK values in declaration order. Each missing PK
            // column is a fatal error — the row's identity cannot be
            // derived.
            let rec collectPkValues remaining acc =
                match remaining with
                | [] -> Result.success (List.rev acc)
                | (a: Attribute) :: rest ->
                    match row.TryGetProperty(a.Column.ColumnName) with
                    | true, cell -> collectPkValues rest (invariantString cell :: acc)
                    | false, _ ->
                        Result.failureOf
                            (adapterError
                                "staticAdapter.pk.missing"
                                (sprintf "Row in kind '%s' missing PK column '%s'."
                                    (Name.value kind.Name) a.Column.ColumnName))
            collectPkValues pkAttrs []
            |> Result.bind (fun pkValues ->
                deriveRowIdentifier kind.SsKey pkValues
                |> Result.map (fun identifier ->
                    { Identifier = identifier
                      Values     = buildValues kind row }))

    /// Parse the V1 JSON content into a `(schema, table) → JsonElement
    /// list` lookup. Tables sharing the same physical key are
    /// (intentionally) merged — the last occurrence wins. This is
    /// defensive; real V1 fixtures don't duplicate table entries.
    let private indexTables (root: JsonElement) : Result<Map<string * string, JsonElement list>> =
        if root.ValueKind <> JsonValueKind.Object then
            Result.failureOf
                (adapterError "staticAdapter.json.shape"
                    "Expected top-level object with a 'tables' array.")
        else
            match root.TryGetProperty("tables") with
            | false, _ ->
                Result.failureOf
                    (adapterError "staticAdapter.json.tables.missing"
                        "Top-level 'tables' property missing.")
            | true, tables when tables.ValueKind <> JsonValueKind.Array ->
                Result.failureOf
                    (adapterError "staticAdapter.json.tables.notArray"
                        "Top-level 'tables' must be an array.")
            | true, tables ->
                let mutable index : Map<string * string, JsonElement list> = Map.empty
                for table in tables.EnumerateArray() do
                    let schema =
                        table.GetProperty("schema").GetString()
                        |> Option.ofObj |> Option.defaultValue ""
                    let name =
                        table.GetProperty("table").GetString()
                        |> Option.ofObj |> Option.defaultValue ""
                    let rows =
                        match table.TryGetProperty("rows") with
                        | true, r when r.ValueKind = JsonValueKind.Array ->
                            r.EnumerateArray() |> Seq.toList
                        | _ -> []
                    index <- Map.add (schema, name) rows index
                Result.success index

    /// Replace a kind's `Static` modality populations with the given
    /// list. A kind without a `Static` modality is returned unchanged.
    let private withStaticPopulations (populations: StaticRow list) (kind: Kind) : Kind =
        let newModality =
            kind.Modality
            |> List.map (function
                | Static _      -> Static populations
                | other         -> other)
        { kind with Modality = newModality }

    /// True iff the kind's modality includes `Static`.
    let private hasStaticModality (kind: Kind) : bool =
        kind.Modality |> List.exists (function Static _ -> true | _ -> false)

    /// Attach `Static` populations from V1 JSON to the matching kinds
    /// in the catalog. The catalog template defines which kinds expect
    /// populations (those carrying the `Static` modality); kinds whose
    /// `(Schema, Table)` pair does not appear in the JSON pass through
    /// unchanged. Kinds without a `Static` modality are also unchanged.
    /// Kinds with a `Static` modality but no PK attributes return a
    /// failure (the row identifier cannot be derived).
    ///
    /// The catalog itself is not validated — duplicate (Schema, Table)
    /// pairs across kinds, missing kinds for JSON tables, or other
    /// shape concerns are the catalog reader's responsibility, not
    /// this adapter's.
    let attachStaticPopulations (catalog: Catalog) (staticDataJson: string) : Result<Catalog> =
        try
            use doc = JsonDocument.Parse(staticDataJson)
            indexTables doc.RootElement
            |> Result.bind (fun rowsByPhysical ->
                // Walk the catalog. For each Static-flagged kind whose
                // (Schema, Table) appears in the JSON, convert rows and
                // attach. Result composes via Result.collect so the
                // first failure short-circuits.
                let modulesResult : Result<Module list> =
                    catalog.Modules
                    |> List.map (fun m ->
                        let kindsResult : Result<Kind list> =
                            m.Kinds
                            |> List.map (fun k ->
                                if not (hasStaticModality k) then
                                    Result.success k
                                else
                                    let key = (k.Physical.Schema, k.Physical.Table)
                                    match Map.tryFind key rowsByPhysical with
                                    | None ->
                                        // No matching JSON table; leave the
                                        // catalog template's populations
                                        // (typically empty) intact.
                                        Result.success k
                                    | Some [] ->
                                        Result.success k
                                    | Some rows ->
                                        let pkAttrs = Kind.primaryKey k
                                        if List.isEmpty pkAttrs then
                                            Result.failureOf
                                                (adapterError
                                                    "staticAdapter.kind.noPk"
                                                    (sprintf
                                                        "Kind '%s' has no IsPrimaryKey attribute; cannot derive row identifiers."
                                                        (Name.value k.Name)))
                                        else
                                            rows
                                            |> List.map (convertRow k pkAttrs)
                                            |> Result.collect
                                            |> Result.map (fun populations ->
                                                withStaticPopulations populations k))
                            |> Result.collect
                        Result.map (fun kinds -> { m with Kinds = kinds }) kindsResult)
                    |> Result.collect
                Result.map (fun modules -> { catalog with Modules = modules }) modulesResult)
        with
        | :? JsonException as ex ->
            Result.failureOf (adapterError "staticAdapter.json.parse" ex.Message)
