namespace Twin.Core

open System.Text.Json
open Projection.Core

/// THE TWIN — evidence (Twin.Core, pure).
///
/// The durable, coordinate-keyed distribution evidence the mint rides
/// on. Two tiers of one shape:
///
///   **rich** — everything the profiler captured, including literal
///     values (categorical frequencies, numeric percentiles). Lives
///     OUT of the repository.
///   **shape** — the committed tier: counts, null rates, distinct
///     counts, lengths, truncation flags, fan-out shapes — and **no
///     captured literal of any kind** (law 3, property-tested).
///
/// The pack's wire format carries coordinates and scalars only — no
/// SsKey, no engine type — so it survives ejection unchanged and can be
/// reviewed line by line in the repository.
///
/// Rebinding is the identity ACL applied to evidence:
///   `ofProfile`  — capture-side: engine Profile → coordinate-keyed pack
///     (the capture catalog's names are the map);
///   `toProfile`  — mint-side: pack → engine Profile against the twin
///     catalog (law 2 — an unbound coordinate refuses by name);
///   `layer`      — precedence: a later profile's evidence replaces the
///     earlier per attribute (rich over shape; the scenario overlay
///     rides above both, in the compiler).
type NumericShape = {
    Min : decimal; P25 : decimal; P50 : decimal; P75 : decimal
    P95 : decimal; P99 : decimal; Max : decimal
}

type ColumnEvidence = {
    Column        : string
    RowCount      : int64
    NullCount     : int64
    MaxLength     : int option
    DistinctCount : int64 option
    Truncated     : bool
    /// Rich tier only; `[]` in the shape tier.
    Frequencies   : (string * int64) list
    /// Rich tier only; `None` in the shape tier.
    Numeric       : NumericShape option
}

type TableEvidence = {
    Table    : string
    RowCount : int64
    Columns  : ColumnEvidence list
}

/// Child-per-parent fan-out for one relationship, addressed by the
/// child table + FK column (+ the parent, to disambiguate a column
/// carrying several relationships is impossible in SQL — one FK column,
/// one target — but the parent names the edge for the reader).
type FanOutEvidence = {
    ChildTable  : string
    ChildColumn : string
    ParentTable : string
    Shape       : NumericShape
}

type EvidenceTier =
    | ShapeTier
    | RichTier

type EvidencePack = {
    Tier    : EvidenceTier
    /// Provenance labels — the source names that contributed.
    Sources : string list
    Tables  : TableEvidence list
    FanOuts : FanOutEvidence list
}

[<RequireQualifiedAccess>]
module Evidence =

    let emptyPack (tier: EvidenceTier) : EvidencePack =
        { Tier = tier; Sources = []; Tables = []; FanOuts = [] }

    // ------------------------------------------------------------------
    // Capture-side rebinding: Profile → pack (rich).
    // ------------------------------------------------------------------

    /// The capture-side name map: engine keys → coordinate texts. Built
    /// from the capture catalog, filtered to the closed table set.
    type private CaptureMap = {
        KindCoord   : Map<SsKey, string>
        AttrCoord   : Map<SsKey, string * string>          // key → (table, column)
        RefCoord    : Map<SsKey, string * string * string> // key → (childTable, childColumn, parentTable)
    }

    let private captureMapOf (catalog: Catalog) (keep: Kind -> string option) : CaptureMap =
        let kinds =
            Catalog.allKinds catalog
            |> List.choose (fun k -> keep k |> Option.map (fun coord -> k, coord))
        let kindCoord = kinds |> List.map (fun (k, c) -> k.SsKey, c) |> Map.ofList
        let attrCoord =
            kinds
            |> List.collect (fun (k, coord) ->
                k.Attributes |> List.map (fun a -> a.SsKey, (coord, ColumnRealization.columnNameText a.Column)))
            |> Map.ofList
        let refCoord =
            kinds
            |> List.collect (fun (k, coord) ->
                k.References
                |> List.choose (fun r ->
                    match Map.tryFind r.TargetKind kindCoord with
                    | None -> None
                    | Some parentCoord ->
                        let column =
                            k.Attributes
                            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                            |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
                        column |> Option.map (fun col -> r.SsKey, (coord, col, parentCoord))))
            |> Map.ofList
        { KindCoord = kindCoord; AttrCoord = attrCoord; RefCoord = refCoord }

    let private shapeOf (n: NumericDistribution) : NumericShape =
        { Min = n.Min; P25 = n.P25; P50 = n.P50; P75 = n.P75; P95 = n.P95; P99 = n.P99; Max = n.Max }

    /// Rebind a captured engine Profile to a coordinate-keyed RICH pack.
    /// `keep` maps a capture-side kind to its estate coordinate text —
    /// the rendition seam: a logical source keeps its physical
    /// `schema.table`; a physical (OutSystems cloud) source maps through
    /// its logical entity name. Kinds mapped to `None` are outside the
    /// closed set and contribute nothing.
    let ofProfile
        (sourceName: string)
        (catalog: Catalog)
        (keep: Kind -> string option)
        (profile: Profile)
        : EvidencePack =
        let map = captureMapOf catalog keep
        let categoricalByAttr =
            profile.Distributions
            |> List.choose (function AttributeDistribution.Categorical c -> Some (c.AttributeKey, c) | _ -> None)
            |> Map.ofList
        let numericByAttr =
            profile.Distributions
            |> List.choose (function AttributeDistribution.Numeric n -> Some (n.AttributeKey, n) | _ -> None)
            |> Map.ofList
        let columns =
            profile.Columns
            |> List.choose (fun c ->
                Map.tryFind c.AttributeKey map.AttrCoord
                |> Option.map (fun (table, column) ->
                    table,
                    { Column = column
                      RowCount = c.RowCount
                      NullCount = c.NullCount
                      MaxLength = c.MaxObservedLength
                      DistinctCount =
                          Map.tryFind c.AttributeKey categoricalByAttr |> Option.map (fun cat -> cat.DistinctCount)
                      Truncated =
                          Map.tryFind c.AttributeKey categoricalByAttr |> Option.map (fun cat -> cat.IsTruncated) |> Option.defaultValue false
                      Frequencies =
                          Map.tryFind c.AttributeKey categoricalByAttr |> Option.map (fun cat -> cat.Frequencies) |> Option.defaultValue []
                      Numeric =
                          Map.tryFind c.AttributeKey numericByAttr |> Option.map shapeOf }))
        let tables =
            columns
            |> List.groupBy fst
            |> List.map (fun (table, cols) ->
                let columnEvidence = cols |> List.map snd |> List.sortBy (fun c -> c.Column.ToLowerInvariant())
                { Table = table
                  RowCount = columnEvidence |> List.map (fun c -> c.RowCount) |> function [] -> 0L | xs -> List.max xs
                  Columns = columnEvidence })
            |> List.sortBy (fun t -> t.Table.ToLowerInvariant())
        let fanOuts =
            profile.ForeignKeyCardinalities
            |> List.choose (fun f ->
                Map.tryFind f.ReferenceKey map.RefCoord
                |> Option.map (fun (child, column, parent) ->
                    { ChildTable = child; ChildColumn = column; ParentTable = parent
                      Shape = shapeOf f.ChildCountDistribution }))
            |> List.sortBy (fun f -> f.ChildTable.ToLowerInvariant(), f.ChildColumn.ToLowerInvariant())
        { Tier = RichTier; Sources = [ sourceName ]; Tables = tables; FanOuts = fanOuts }

    // ------------------------------------------------------------------
    // The tier projection (law 3) and the merge (law 4's backstop).
    // ------------------------------------------------------------------

    /// Rich → shape: every captured literal dropped; structure, counts,
    /// and shapes kept. Fan-out shapes carry counts (children per
    /// parent), never values, so they survive.
    let deriveShape (pack: EvidencePack) : EvidencePack =
        { pack with
            Tier = ShapeTier
            Tables =
                pack.Tables
                |> List.map (fun t ->
                    { t with
                        Columns =
                            t.Columns
                            |> List.map (fun c -> { c with Frequencies = []; Numeric = None }) }) }

    let private mergeCollision (table: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.mergeCollision"
            "Two evidence packs carry the same table. Each table belongs to exactly one source."
            (Map.ofList [ "table", Some table ])

    /// Union packs with disjoint table sets (the artifact-level backstop
    /// of the config's collision law).
    let merge (packs: EvidencePack list) : Result<EvidencePack> =
        match packs with
        | [] -> Result.success (emptyPack RichTier)
        | first :: _ ->
            let collisions =
                packs
                |> List.collect (fun p -> p.Tables |> List.map (fun t -> t.Table.ToLowerInvariant()))
                |> List.groupBy id
                |> List.filter (fun (_, g) -> List.length g > 1)
                |> List.map (fst >> mergeCollision)
            if not (List.isEmpty collisions) then Result.failure collisions
            else
                Result.success
                    { Tier = first.Tier
                      Sources = packs |> List.collect (fun p -> p.Sources) |> List.distinct
                      Tables = packs |> List.collect (fun p -> p.Tables) |> List.sortBy (fun t -> t.Table.ToLowerInvariant())
                      FanOuts = packs |> List.collect (fun p -> p.FanOuts) |> List.sortBy (fun f -> f.ChildTable.ToLowerInvariant(), f.ChildColumn.ToLowerInvariant()) }

    // ------------------------------------------------------------------
    // Mint-side rebinding: pack → Profile against the twin catalog.
    // ------------------------------------------------------------------

    let private numericOf (attrKey: SsKey) (rows: int64) (s: NumericShape) : Result<NumericDistribution> =
        NumericDistribution.create
            attrKey s.Min s.P25 s.P50 s.P75 s.P95 s.P99 s.Max
            (max rows NumericDistribution.sampleSizeFloor)
            (ProbeStatus.observed (max rows NumericDistribution.sampleSizeFloor))

    /// Bind a pack to the twin catalog as an engine Profile. Law 2: every
    /// evidenced coordinate must exist — an unbound table or column is a
    /// named refusal, never a silent skip (the estate moved ahead of the
    /// evidence; `twin evidence verify` is the drift answer).
    let toProfile (index: CatalogIndex) (pack: EvidencePack) : Result<Profile> =
        let tableResults =
            pack.Tables
            |> List.map (fun t ->
                match TableCoordinate.parse t.Table with
                | Error es -> Result.failure es
                | Ok coord ->
                    CatalogIndex.bindKind index coord
                    |> Result.bind (fun kind ->
                        t.Columns
                        |> List.map (fun c ->
                            ColumnCoordinate.create coord c.Column
                            |> Result.bind (CatalogIndex.bindColumn index)
                            |> Result.bind (fun (_, attr) ->
                                let columnProfile =
                                    ColumnProfile.create attr.SsKey c.RowCount c.NullCount (ProbeStatus.observed c.RowCount)
                                    |> Result.map (fun cp ->
                                        match c.MaxLength with
                                        | Some len -> ColumnProfile.withMaxObservedLength len cp
                                        | None -> cp)
                                let categorical =
                                    match c.Frequencies with
                                    | [] -> Result.success None
                                    | freqs ->
                                        let distinct = defaultArg c.DistinctCount (int64 (List.length freqs))
                                        CategoricalDistribution.create attr.SsKey freqs distinct c.Truncated (ProbeStatus.observed c.RowCount)
                                        |> Result.map Some
                                let numeric =
                                    match c.Numeric with
                                    | None -> Result.success None
                                    | Some s -> numericOf attr.SsKey c.RowCount s |> Result.map Some
                                match columnProfile, categorical, numeric with
                                | Ok cp, Ok cat, Ok num -> Result.success (cp, cat, num)
                                | cpR, catR, numR ->
                                    Result.failure (Result.errors cpR @ Result.errors catR @ Result.errors numR)))
                        |> Result.aggregate
                        |> Result.map (fun cols -> kind, cols)))
            |> Result.aggregate
        let fanOutResults =
            pack.FanOuts
            |> List.map (fun f ->
                match TableCoordinate.parse f.ChildTable, TableCoordinate.parse f.ParentTable with
                | Ok childCoord, Ok parentCoord ->
                    CatalogIndex.bindKind index childCoord
                    |> Result.bind (fun childKind ->
                        CatalogIndex.bindKind index parentCoord
                        |> Result.bind (fun parentKind ->
                            let reference =
                                childKind.References
                                |> List.tryFind (fun r ->
                                    r.TargetKind = parentKind.SsKey
                                    && (childKind.Attributes
                                        |> List.exists (fun a ->
                                            a.SsKey = r.SourceAttribute
                                            && System.String.Equals(
                                                ColumnRealization.columnNameText a.Column, f.ChildColumn,
                                                System.StringComparison.OrdinalIgnoreCase))))
                            match reference with
                            | None ->
                                Result.failureOf
                                    (ValidationError.createWithMetadata
                                        "twin.evidence.fanOutUnbound"
                                        "A fan-out names a relationship the estate does not carry."
                                        (Map.ofList
                                            [ "child", Some f.ChildTable; "column", Some f.ChildColumn
                                              "parent", Some f.ParentTable ]))
                            | Some r ->
                                numericOf r.SsKey (int64 NumericDistribution.sampleSizeFloor) f.Shape
                                |> Result.map (fun dist ->
                                    ForeignKeyCardinality.create r.SsKey dist)))
                | cR, pR -> Result.failure (Result.errors cR @ Result.errors pR))
            |> Result.aggregate
        match tableResults, fanOutResults with
        | Ok tables, Ok fanOuts ->
            let cols = tables |> List.collect (fun (_, cols) -> cols)
            Result.success
                { Profile.empty with
                    Columns = cols |> List.map (fun (cp, _, _) -> cp)
                    Distributions =
                        (cols |> List.choose (fun (_, cat, _) -> cat |> Option.map AttributeDistribution.Categorical))
                        @ (cols |> List.choose (fun (_, _, num) -> num |> Option.map AttributeDistribution.Numeric))
                    ForeignKeyCardinalities = fanOuts }
        | tR, fR -> Result.failure (Result.errors tR @ Result.errors fR)

    /// Precedence layering: `over`'s evidence replaces `base`'s per
    /// attribute/reference key; everything else unions.
    let layer (baseProfile: Profile) (over: Profile) : Profile =
        let overColumnKeys = over.Columns |> List.map (fun c -> c.AttributeKey) |> Set.ofList
        let distKey (d: AttributeDistribution) =
            match d with
            | AttributeDistribution.Categorical c -> c.AttributeKey
            | AttributeDistribution.Numeric n -> n.AttributeKey
        let overDistKeys = over.Distributions |> List.map distKey |> Set.ofList
        let overFanKeys = over.ForeignKeyCardinalities |> List.map (fun f -> f.ReferenceKey) |> Set.ofList
        { baseProfile with
            Columns =
                (baseProfile.Columns |> List.filter (fun c -> not (Set.contains c.AttributeKey overColumnKeys)))
                @ over.Columns
            Distributions =
                (baseProfile.Distributions |> List.filter (fun d -> not (Set.contains (distKey d) overDistKeys)))
                @ over.Distributions
            ForeignKeyCardinalities =
                (baseProfile.ForeignKeyCardinalities |> List.filter (fun f -> not (Set.contains f.ReferenceKey overFanKeys)))
                @ over.ForeignKeyCardinalities }

    /// The kinds a layered profile carries column evidence for — the
    /// volume seam: an evidenced kind rides observed × scale; an
    /// unevidenced kind gets the default volume.
    let evidencedKinds (index: CatalogIndex) (profile: Profile) : Set<SsKey> =
        let byAttr =
            CatalogIndex.kinds index
            |> List.collect (fun (_, k) -> k.Attributes |> List.map (fun a -> a.SsKey, k.SsKey))
            |> Map.ofList
        profile.Columns
        |> List.choose (fun c -> Map.tryFind c.AttributeKey byAttr)
        |> Set.ofList

    // ------------------------------------------------------------------
    // The codec — deterministic, total, round-tripping.
    // ------------------------------------------------------------------

    let private tierText (t: EvidenceTier) : string =
        match t with ShapeTier -> "shape" | RichTier -> "rich"

    let serialize (pack: EvidencePack) : string =
        let options = JsonWriterOptions(Indented = true)
        use stream = new System.IO.MemoryStream()
        (fun () ->
            use writer = new Utf8JsonWriter(stream, options)
            writer.WriteStartObject()
            writer.WriteString("tier", tierText pack.Tier)
            writer.WriteStartArray "sources"
            for s in pack.Sources |> List.sort do writer.WriteStringValue s
            writer.WriteEndArray()
            writer.WriteStartArray "tables"
            for t in pack.Tables |> List.sortBy (fun t -> t.Table.ToLowerInvariant()) do
                writer.WriteStartObject()
                writer.WriteString("table", t.Table)
                writer.WriteNumber("rowCount", t.RowCount)
                writer.WriteStartArray "columns"
                for c in t.Columns |> List.sortBy (fun c -> c.Column.ToLowerInvariant()) do
                    writer.WriteStartObject()
                    writer.WriteString("column", c.Column)
                    writer.WriteNumber("rowCount", c.RowCount)
                    writer.WriteNumber("nullCount", c.NullCount)
                    match c.MaxLength with
                    | Some l -> writer.WriteNumber("maxLength", l)
                    | None -> ()
                    match c.DistinctCount with
                    | Some d -> writer.WriteNumber("distinctCount", d)
                    | None -> ()
                    if c.Truncated then writer.WriteBoolean("truncated", true)
                    match c.Frequencies with
                    | [] -> ()
                    | freqs ->
                        writer.WriteStartArray "frequencies"
                        for (v, n) in freqs do
                            writer.WriteStartObject()
                            writer.WriteString("value", v)
                            writer.WriteNumber("count", n)
                            writer.WriteEndObject()
                        writer.WriteEndArray()
                    match c.Numeric with
                    | None -> ()
                    | Some s ->
                        writer.WriteStartObject "numeric"
                        writer.WriteNumber("min", s.Min); writer.WriteNumber("p25", s.P25)
                        writer.WriteNumber("p50", s.P50); writer.WriteNumber("p75", s.P75)
                        writer.WriteNumber("p95", s.P95); writer.WriteNumber("p99", s.P99)
                        writer.WriteNumber("max", s.Max)
                        writer.WriteEndObject()
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteStartArray "fanOuts"
            for f in pack.FanOuts |> List.sortBy (fun f -> f.ChildTable.ToLowerInvariant(), f.ChildColumn.ToLowerInvariant()) do
                writer.WriteStartObject()
                writer.WriteString("child", f.ChildTable)
                writer.WriteString("column", f.ChildColumn)
                writer.WriteString("parent", f.ParentTable)
                writer.WriteStartObject "shape"
                writer.WriteNumber("min", f.Shape.Min); writer.WriteNumber("p25", f.Shape.P25)
                writer.WriteNumber("p50", f.Shape.P50); writer.WriteNumber("p75", f.Shape.P75)
                writer.WriteNumber("p95", f.Shape.P95); writer.WriteNumber("p99", f.Shape.P99)
                writer.WriteNumber("max", f.Shape.Max)
                writer.WriteEndObject()
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteEndObject()) ()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let private codecError (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.codec"
            "The evidence pack did not parse."
            (Map.ofList [ "detail", Some detail ])

    let deserialize (json: string) : Result<EvidencePack> =
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement
            let getStr (el: JsonElement) (name: string) : string =
                match el.TryGetProperty name with
                | true, v ->
                    (match v.GetString() with null -> "" | s -> s)
                | _ -> ""
            let tier =
                match getStr root "tier" with
                | "shape" -> ShapeTier
                | _ -> RichTier
            let sources =
                match root.TryGetProperty "sources" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for s in arr.EnumerateArray() -> match s.GetString() with null -> "" | v -> v ]
                | _ -> []
            let shapeOfEl (el: JsonElement) : NumericShape =
                let d (name: string) = el.GetProperty(name).GetDecimal()
                { Min = d "min"; P25 = d "p25"; P50 = d "p50"; P75 = d "p75"; P95 = d "p95"; P99 = d "p99"; Max = d "max" }
            let tables =
                match root.TryGetProperty "tables" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for t in arr.EnumerateArray() ->
                        { Table = getStr t "table"
                          RowCount = t.GetProperty("rowCount").GetInt64()
                          Columns =
                              match t.TryGetProperty "columns" with
                              | true, cols when cols.ValueKind = JsonValueKind.Array ->
                                  [ for c in cols.EnumerateArray() ->
                                      { Column = getStr c "column"
                                        RowCount = c.GetProperty("rowCount").GetInt64()
                                        NullCount = c.GetProperty("nullCount").GetInt64()
                                        MaxLength =
                                            match c.TryGetProperty "maxLength" with
                                            | true, v -> Some (v.GetInt32())
                                            | _ -> None
                                        DistinctCount =
                                            match c.TryGetProperty "distinctCount" with
                                            | true, v -> Some (v.GetInt64())
                                            | _ -> None
                                        Truncated =
                                            match c.TryGetProperty "truncated" with
                                            | true, v -> v.GetBoolean()
                                            | _ -> false
                                        Frequencies =
                                            match c.TryGetProperty "frequencies" with
                                            | true, freqs when freqs.ValueKind = JsonValueKind.Array ->
                                                [ for f in freqs.EnumerateArray() ->
                                                    getStr f "value", f.GetProperty("count").GetInt64() ]
                                            | _ -> []
                                        Numeric =
                                            match c.TryGetProperty "numeric" with
                                            | true, n when n.ValueKind = JsonValueKind.Object -> Some (shapeOfEl n)
                                            | _ -> None } ]
                              | _ -> [] } ]
                | _ -> []
            let fanOuts =
                match root.TryGetProperty "fanOuts" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for f in arr.EnumerateArray() ->
                        { ChildTable = getStr f "child"
                          ChildColumn = getStr f "column"
                          ParentTable = getStr f "parent"
                          Shape = shapeOfEl (f.GetProperty "shape") } ]
                | _ -> []
            Result.success { Tier = tier; Sources = sources; Tables = tables; FanOuts = fanOuts }
        with ex ->
            Result.failureOf (codecError ex.Message)
