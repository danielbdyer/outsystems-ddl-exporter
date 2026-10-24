namespace Twin.Core
// LINT-ALLOW-FILE: the Twin's config parse/render surface — `String.concat` and
//   interpolation compose terminal operator-facing config text (paths, section
//   headers, defaults narration) at the read/write boundary; no typed AST
//   applies to free-text config prose, and the values are validated before they
//   reach the boundary. (The one wide tuple is a local parse intermediate; a
//   named record is a worthwhile follow-up, flagged, but touches the parse
//   call sites this PR does not otherwise change.)

open System.Text.Json
open Projection.Core

/// THE TWIN — the configuration surface (THE_TWIN.md §config).
///
/// One `twin.json` at the SSDT repository root. The surface obeys four
/// laws, each executable in the pure pool:
///
///   closed schema — an unrecognized key is refused, named by its JSON
///     path (`twin.config.unknownKey`), never ignored;
///   located refusals — every parse error names the path it occurred at
///     and states the expected shape (THE_VOICE §14 register);
///   secret-free (D9) — a connection or password value must be a
///     reference (`env:<VAR>` / `file:<path>`); an inline-looking secret
///     is refused at parse time;
///   collision-free sources (law 4) — no table claimed by two evidence
///     sources; the refusal names the table and both sources.
///
/// Scenario bodies are parsed to typed IR here (so the closed-schema law
/// covers them from day one); *compilation* to engine inputs is the
/// scenario compiler's job (M4), which may only rewrite evidence,
/// volumes, corrections, and pins — never generate.
type EvidenceRendition =
    /// A logical-model database (on-prem): names match the estate
    /// definition directly.
    | Logical
    /// A physical OutSystems cloud database: names are physical and map
    /// to logical coordinates through the capture-side catalog.
    | Physical

type EvidenceSource = {
    Name       : string
    Rendition  : EvidenceRendition
    /// Connection *reference* (`env:` / `file:`), never a raw secret.
    ConnRef    : string
    /// The closed table set this source is authoritative for.
    Tables     : TableCoordinate list
    /// Optional per-source sampled-row cap (feeds `SamplingPolicy`;
    /// counts stay exact under any cap).
    SampleRows : int option
}

type EstateSection = {
    /// Glob (repo-relative, forward slashes) selecting the table scripts.
    TablesPattern  : string
    /// Optional glob selecting `CREATE SCHEMA` scripts.
    SchemasPattern : string option
    /// The repo's static reference-data lanes, applied in list order.
    StaticData     : string list
}

type ContainerSection = {
    Name        : string
    Port        : int
    Image       : string
    /// Optional password reference (`env:` / `file:`). When absent the
    /// documented local development default applies.
    PasswordRef : string option
}

type EvidenceSection = {
    /// The committed shape-tier pack path (repo-relative).
    ShapePath : string option
    /// The out-of-repo rich-tier pack reference (`file:` ref or plain path).
    RichRef   : string option
    Sources   : EvidenceSource list
}

/// How a date window skews inside its range (scenario column override).
type DateSkew =
    | SkewUniform
    | SkewEarly
    | SkewLate

/// One scenario column override — a categorical weighting or a date
/// window, never both (refused at parse).
type ColumnOverride =
    /// Ratio weights over pinned category values (ints — T1 discipline).
    | Weights of (string * int) list
    /// A date window with an optional skew.
    | Between of lowIso: string * highIso: string * skew: DateSkew

/// One pinned operator-authored row: column name → value. `None` pins
/// SQL NULL. Values are raw JSON scalars; the scenario compiler renders
/// them per attribute type.
type PinValue =
    | PinText of string
    | PinNumber of string
    | PinBool of bool
    | PinNull

type Pin = {
    Table : TableCoordinate
    Rows  : (string * PinValue) list list
}

type TableOverride = {
    Rows      : int option
    Columns   : (string * ColumnOverride) list
    /// Declared on the CHILD: mean child rows per parent, keyed by the
    /// parent table coordinate (a child may carry several FKs).
    PerParent : (TableCoordinate * decimal) list
}

type ScenarioIr = {
    Name    : string
    Extends : string option
    Scale   : decimal option
    Seed    : uint64 option
    Tables  : (TableCoordinate * TableOverride) list
    Pins    : Pin list
}

/// How evidence-free default volumes distribute across kinds.
type VolumeMode =
    /// Flat `defaultRows` per kind.
    | FlatVolumes
    /// PageRank-centrality-weighted (the kernel's `SyntheticVolume`
    /// derivation) — "the average set implied by the schema."
    | CentralityVolumes

type TwinConfig = {
    Estate          : EstateSection
    Container       : ContainerSection
    Evidence        : EvidenceSection
    CorrectionsPath : string option
    Seed            : uint64
    Scale           : decimal
    DefaultRows     : int
    Volumes         : VolumeMode
    Scenarios       : (string * ScenarioIr) list
}

[<RequireQualifiedAccess>]
module TwinConfig =

    // ------------------------------------------------------------------
    // Defaults — every one documented in THE_TWIN.md's config reference.
    // ------------------------------------------------------------------

    [<Literal>]
    let DefaultContainerName = "twin-mssql"

    [<Literal>]
    let DefaultPort = 21433

    [<Literal>]
    let DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest"

    [<Literal>]
    let DefaultRowsPerKind = 100

    let private defaultSeed : uint64 = 1UL
    let private defaultScale : decimal = 1.0m

    /// The neutral scenario name `up`/`seed` use when none is named.
    [<Literal>]
    let BaselineScenario = "default"

    // ------------------------------------------------------------------
    // Located-refusal helpers. Codes are stable; the JSON path rides in
    // metadata (the static-phrase + structured-metadata discipline).
    // ------------------------------------------------------------------

    let private err (code: string) (message: string) (path: string) : ValidationError =
        ValidationError.createWithMetadata code message (Map.ofList [ "path", Some path ])

    let private unknownKey (path: string) : ValidationError =
        err "twin.config.unknownKey"
            "The configuration carries a key the surface does not define. Remove it, or check the spelling against the config reference."
            path

    let private wrongType (expected: string) (path: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.config.type"
            "The value at this path does not have the expected shape."
            (Map.ofList [ "path", Some path; "expected", Some expected ])

    let private missing (expected: string) (path: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.config.required"
            "A required value is absent."
            (Map.ofList [ "path", Some path; "expected", Some expected ])

    let private outOfRange (expected: string) (path: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.config.range"
            "The value at this path is outside the accepted range."
            (Map.ofList [ "path", Some path; "expected", Some expected ])

    /// D9 — a connection/password value must be an out-of-band reference.
    let private inlineSecret (path: string) : ValidationError =
        err "twin.config.secretInline"
            "A connection or password is carried inline. Secrets never live in twin.json — pass a reference: env:<VARIABLE> or file:<path>."
            path

    let private located (path: string) (es: ValidationError list) : ValidationError list =
        es |> List.map (ValidationError.withMetadata "path" (Some path))

    let private child (parent: string) (key: string) : string =
        System.String.Concat(parent, ".", key)  // LINT-ALLOW: terminal JSON-path breadcrumb for located refusals; the path IS a string, no AST

    let private item (parent: string) (index: int) : string =
        System.String.Concat(parent, "[", string index, "]")  // LINT-ALLOW: terminal JSON-path breadcrumb for located refusals; the path IS a string, no AST

    // ------------------------------------------------------------------
    // Element readers — each accumulates located errors.
    // ------------------------------------------------------------------

    let private checkClosed (path: string) (allowed: string list) (el: JsonElement) : ValidationError list =
        let allowedSet = Set.ofList allowed
        [ for property in el.EnumerateObject() do
            if not (Set.contains property.Name allowedSet) then
                yield unknownKey (child path property.Name) ]

    let private tryProp (name: string) (el: JsonElement) : JsonElement option =
        match el.TryGetProperty name with
        | true, v when v.ValueKind <> JsonValueKind.Null -> Some v
        | _ -> None

    let private readString (path: string) (el: JsonElement) : Result<string> =
        if el.ValueKind = JsonValueKind.String then
            match el.GetString() with
            | null -> Result.failureOf (wrongType "a non-blank string" path)
            | v when System.String.IsNullOrWhiteSpace v -> Result.failureOf (wrongType "a non-blank string" path)
            | v -> Result.success v
        else Result.failureOf (wrongType "a string" path)

    let private readInt (path: string) (lo: int) (hi: int) (el: JsonElement) : Result<int> =
        if el.ValueKind = JsonValueKind.Number then
            match el.TryGetInt32() with
            | true, v when v >= lo && v <= hi -> Result.success v
            | _ -> Result.failureOf (outOfRange (System.String.Concat("an integer between ", string lo, " and ", string hi)) path)  // LINT-ALLOW: terminal expected-shape phrase in refusal metadata
        else Result.failureOf (wrongType "an integer" path)

    let private readSeed (path: string) (el: JsonElement) : Result<uint64> =
        if el.ValueKind = JsonValueKind.Number then
            match el.TryGetUInt64() with
            | true, v -> Result.success v
            | _ -> Result.failureOf (outOfRange "a non-negative integer seed" path)
        else Result.failureOf (wrongType "an integer seed" path)

    let private readDecimal (path: string) (el: JsonElement) : Result<decimal> =
        if el.ValueKind = JsonValueKind.Number then
            match el.TryGetDecimal() with
            | true, v when v > 0m -> Result.success v
            | _ -> Result.failureOf (outOfRange "a positive number" path)
        else Result.failureOf (wrongType "a number" path)

    /// A secret-bearing value: must read as a reference. `env:`/`file:`
    /// pass through; anything else is the D9 refusal.
    let private readSecretRef (path: string) (el: JsonElement) : Result<string> =
        readString path el
        |> Result.bind (fun v ->
            if v.StartsWith "env:" || v.StartsWith "file:"
            then Result.success v
            else Result.failureOf (inlineSecret path))

    let private readTableCoordinate (path: string) (el: JsonElement) : Result<TableCoordinate> =
        readString path el
        |> Result.bind (fun text ->
            match TableCoordinate.parse text with
            | Ok c -> Result.success c
            | Error es -> Result.failure (located path es))

    let private readStringList (path: string) (el: JsonElement) : Result<string list> =
        if el.ValueKind <> JsonValueKind.Array then
            Result.failureOf (wrongType "an array of strings" path)
        else
            el.EnumerateArray()
            |> Seq.mapi (fun i v -> readString (item path i) v)
            |> Result.aggregate

    // ------------------------------------------------------------------
    // Section parsers.
    // ------------------------------------------------------------------

    let private parseEstate (path: string) (el: JsonElement) : Result<EstateSection> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "tables"; "schemas"; "staticData" ] el
        let tables =
            match tryProp "tables" el with
            | Some v -> readString (child path "tables") v
            | None -> Result.failureOf (missing "a glob such as \"Modules/**/*.sql\"" (child path "tables"))
        let schemas =
            match tryProp "schemas" el with
            | Some v -> readString (child path "schemas") v |> Result.map Some
            | None -> Result.success None
        let staticData =
            match tryProp "staticData" el with
            | Some v -> readStringList (child path "staticData") v
            | None -> Result.success []
        match tables, schemas, staticData, closed with
        | Ok t, Ok s, Ok d, [] -> Result.success { TablesPattern = t; SchemasPattern = s; StaticData = d }
        | tR, sR, dR, closedErrors ->
            Result.failure (Result.errors tR @ Result.errors sR @ Result.errors dR @ closedErrors)

    let private defaultContainer : ContainerSection =
        { Name = DefaultContainerName
          Port = DefaultPort
          Image = DefaultImage
          PasswordRef = None }

    let private parseContainer (path: string) (el: JsonElement) : Result<ContainerSection> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "name"; "port"; "image"; "password" ] el
        let name =
            match tryProp "name" el with
            | Some v -> readString (child path "name") v
            | None -> Result.success DefaultContainerName
        let port =
            match tryProp "port" el with
            | Some v -> readInt (child path "port") 1 65535 v
            | None -> Result.success DefaultPort
        let image =
            match tryProp "image" el with
            | Some v -> readString (child path "image") v
            | None -> Result.success DefaultImage
        let password =
            match tryProp "password" el with
            | Some v -> readSecretRef (child path "password") v |> Result.map Some
            | None -> Result.success None
        match name, port, image, password, closed with
        | Ok n, Ok p, Ok i, Ok pw, [] -> Result.success { Name = n; Port = p; Image = i; PasswordRef = pw }
        | nR, pR, iR, pwR, closedErrors ->
            Result.failure (Result.errors nR @ Result.errors pR @ Result.errors iR @ Result.errors pwR @ closedErrors)

    let private parseRendition (path: string) (el: JsonElement) : Result<EvidenceRendition> =
        readString path el
        |> Result.bind (fun v ->
            match v with
            | "logical" -> Result.success Logical
            | "physical" -> Result.success Physical
            | _ -> Result.failureOf (wrongType "\"logical\" or \"physical\"" path))

    let private parseSource (path: string) (el: JsonElement) : Result<EvidenceSource> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "name"; "rendition"; "conn"; "tables"; "sampleRows" ] el
        let name =
            match tryProp "name" el with
            | Some v -> readString (child path "name") v
            | None -> Result.failureOf (missing "a source name" (child path "name"))
        let rendition =
            match tryProp "rendition" el with
            | Some v -> parseRendition (child path "rendition") v
            | None -> Result.failureOf (missing "\"logical\" or \"physical\"" (child path "rendition"))
        let conn =
            match tryProp "conn" el with
            | Some v -> readSecretRef (child path "conn") v
            | None -> Result.failureOf (missing "a connection reference (env:<VAR> or file:<path>)" (child path "conn"))
        let tables =
            match tryProp "tables" el with
            | Some v when v.ValueKind = JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.mapi (fun i t -> readTableCoordinate (item (child path "tables") i) t)
                |> Result.aggregate
                |> Result.bind (fun coords ->
                    if List.isEmpty coords
                    then Result.failureOf (missing "at least one table coordinate" (child path "tables"))
                    else Result.success coords)
            | Some _ -> Result.failureOf (wrongType "an array of table coordinates" (child path "tables"))
            | None -> Result.failureOf (missing "the closed table set this source is authoritative for" (child path "tables"))
        let sampleRows =
            match tryProp "sampleRows" el with
            | Some v -> readInt (child path "sampleRows") 1 System.Int32.MaxValue v |> Result.map Some
            | None -> Result.success None
        match name, rendition, conn, tables, sampleRows, closed with
        | Ok n, Ok r, Ok c, Ok t, Ok s, [] ->
            Result.success { Name = n; Rendition = r; ConnRef = c; Tables = t; SampleRows = s }
        | nR, rR, cR, tR, sR, closedErrors ->
            Result.failure (Result.errors nR @ Result.errors rR @ Result.errors cR @ Result.errors tR @ Result.errors sR @ closedErrors)

    /// Law 4 — collision refusal: no table claimed by two sources.
    let private sourceCollisions (path: string) (sources: EvidenceSource list) : ValidationError list =
        sources
        |> List.collect (fun s -> s.Tables |> List.map (fun t -> TableCoordinate.key t, (TableCoordinate.text t, s.Name)))
        |> List.groupBy fst
        |> List.choose (fun (_, claims) ->
            match claims with
            | [] | [_] -> None
            | (_, (coordText, _)) :: _ ->
                let claimants = claims |> List.map (fun (_, (_, source)) -> source) |> List.distinct
                Some (ValidationError.createWithMetadata
                        "twin.config.evidence.collision"
                        "A table is claimed by more than one evidence source. Each table belongs to exactly one source; remove it from all but one."
                        (Map.ofList
                            [ "path", Some path
                              "table", Some coordText
                              "sources", Some (String.concat ", " claimants) ])))

    let private parseEvidence (path: string) (el: JsonElement) : Result<EvidenceSection> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "shape"; "rich"; "sources" ] el
        let shape =
            match tryProp "shape" el with
            | Some v -> readString (child path "shape") v |> Result.map Some
            | None -> Result.success None
        let rich =
            match tryProp "rich" el with
            | Some v -> readString (child path "rich") v |> Result.map Some
            | None -> Result.success None
        let sources =
            match tryProp "sources" el with
            | Some v when v.ValueKind = JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.mapi (fun i s -> parseSource (item (child path "sources") i) s)
                |> Result.aggregate
            | Some _ -> Result.failureOf (wrongType "an array of evidence sources" (child path "sources"))
            | None -> Result.success []
        match shape, rich, sources, closed with
        | Ok sh, Ok ri, Ok so, [] ->
            let nameDups =
                Validation.duplicateKeyErrors
                    "twin.config.evidence.duplicateSource"
                    (fun (n: string) -> System.String.Concat("Two evidence sources share the name '", n, "'. Source names identify imports; make them unique."))  // LINT-ALLOW: terminal refusal message naming the duplicate; static phrase + the offending key
                    (fun (s: EvidenceSource) -> s.Name)
                    so
            let collisions = sourceCollisions (child path "sources") so
            match nameDups @ collisions with
            | [] -> Result.success { ShapePath = sh; RichRef = ri; Sources = so }
            | errors -> Result.failure errors
        | shR, riR, soR, closedErrors ->
            Result.failure (Result.errors shR @ Result.errors riR @ Result.errors soR @ closedErrors)

    // ------------------------------------------------------------------
    // Scenario parsing (IR only; compilation is M4's scenario compiler).
    // ------------------------------------------------------------------

    let private parseSkew (path: string) (el: JsonElement) : Result<DateSkew> =
        readString path el
        |> Result.bind (fun v ->
            match v with
            | "uniform" -> Result.success SkewUniform
            | "early" -> Result.success SkewEarly
            | "late" -> Result.success SkewLate
            | _ -> Result.failureOf (wrongType "\"uniform\", \"early\", or \"late\"" path))

    let private parseWeights (path: string) (el: JsonElement) : Result<(string * int) list> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object of value → integer ratio" path) else
        el.EnumerateObject()
        |> Seq.map (fun p ->
            readInt (child path p.Name) 1 System.Int32.MaxValue p.Value
            |> Result.map (fun ratio -> p.Name, ratio))
        |> Result.aggregate
        |> Result.bind (fun weights ->
            if List.isEmpty weights
            then Result.failureOf (missing "at least one value → ratio pair" path)
            else Result.success weights)

    let private parseColumnOverride (path: string) (el: JsonElement) : Result<ColumnOverride> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "weights"; "between"; "skew" ] el
        let weights = tryProp "weights" el
        let between = tryProp "between" el
        let skew = tryProp "skew" el
        match weights, between, closed with
        | Some _, Some _, _ ->
            Result.failure
                (err "twin.config.scenario.overrideConflict"
                    "A column override carries both weights and a date window. Pick one: weights reshape a categorical column; between reshapes a date column."
                    path
                 :: closed)
        | Some w, None, [] ->
            match skew with
            | Some _ ->
                Result.failureOf
                    (err "twin.config.scenario.skewWithoutBetween"
                        "skew only applies to a date window. Add between, or remove skew."
                        (child path "skew"))
            | None -> parseWeights (child path "weights") w |> Result.map Weights
        | None, Some b, [] ->
            let range =
                if b.ValueKind = JsonValueKind.Array && b.GetArrayLength() = 2 then
                    let arr = b.EnumerateArray() |> Seq.toArray
                    match readString (item (child path "between") 0) arr.[0], readString (item (child path "between") 1) arr.[1] with
                    | Ok lo, Ok hi -> Result.success (lo, hi)
                    | loR, hiR -> Result.failure (Result.errors loR @ Result.errors hiR)
                else Result.failureOf (wrongType "an array of exactly two ISO dates [\"from\", \"to\"]" (child path "between"))
            let skewParsed =
                match skew with
                | Some s -> parseSkew (child path "skew") s
                | None -> Result.success SkewUniform
            match range, skewParsed with
            | Ok (lo, hi), Ok sk -> Result.success (Between (lo, hi, sk))
            | rR, sR -> Result.failure (Result.errors rR @ Result.errors sR)
        | None, None, [] ->
            Result.failureOf
                (missing "either weights or between" path)
        | _, _, closedErrors -> Result.failure closedErrors

    let private parsePerParent (path: string) (el: JsonElement) : Result<(TableCoordinate * decimal) list> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object of parent coordinate → { mean }" path) else
        el.EnumerateObject()
        |> Seq.map (fun p ->
            let entryPath = child path p.Name
            let coord =
                match TableCoordinate.parse p.Name with
                | Ok c -> Result.success c
                | Error es -> Result.failure (located entryPath es)
            let mean =
                if p.Value.ValueKind = JsonValueKind.Object then
                    let closed = checkClosed entryPath [ "mean" ] p.Value
                    match tryProp "mean" p.Value, closed with
                    | Some m, [] -> readDecimal (child entryPath "mean") m
                    | None, [] -> Result.failureOf (missing "a positive mean" (child entryPath "mean"))
                    | _, closedErrors -> Result.failure closedErrors
                else Result.failureOf (wrongType "an object { \"mean\": <number> }" entryPath)
            match coord, mean with
            | Ok c, Ok m -> Result.success (c, m)
            | cR, mR -> Result.failure (Result.errors cR @ Result.errors mR))
        |> Result.aggregate

    let private parseTableOverride (path: string) (el: JsonElement) : Result<TableOverride> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "rows"; "columns"; "perParent" ] el
        let rows =
            match tryProp "rows" el with
            | Some v -> readInt (child path "rows") 0 System.Int32.MaxValue v |> Result.map Some
            | None -> Result.success None
        let columns =
            match tryProp "columns" el with
            | Some v when v.ValueKind = JsonValueKind.Object ->
                v.EnumerateObject()
                |> Seq.map (fun p ->
                    parseColumnOverride (child (child path "columns") p.Name) p.Value
                    |> Result.map (fun o -> p.Name, o))
                |> Result.aggregate
            | Some _ -> Result.failureOf (wrongType "an object of column → override" (child path "columns"))
            | None -> Result.success []
        let perParent =
            match tryProp "perParent" el with
            | Some v -> parsePerParent (child path "perParent") v
            | None -> Result.success []
        match rows, columns, perParent, closed with
        | Ok r, Ok c, Ok p, [] -> Result.success { Rows = r; Columns = c; PerParent = p }
        | rR, cR, pR, closedErrors ->
            Result.failure (Result.errors rR @ Result.errors cR @ Result.errors pR @ closedErrors)

    let private parsePinValue (path: string) (el: JsonElement) : Result<PinValue> =
        match el.ValueKind with
        | JsonValueKind.String ->
            match el.GetString() with
            | null -> Result.success PinNull
            | v -> Result.success (PinText v)
        | JsonValueKind.Number -> Result.success (PinNumber (el.GetRawText()))
        | JsonValueKind.True -> Result.success (PinBool true)
        | JsonValueKind.False -> Result.success (PinBool false)
        | JsonValueKind.Null -> Result.success PinNull
        | _ -> Result.failureOf (wrongType "a scalar (string, number, boolean, or null)" path)

    let private parsePin (path: string) (el: JsonElement) : Result<Pin> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "table"; "rows" ] el
        let table =
            match tryProp "table" el with
            | Some v -> readTableCoordinate (child path "table") v
            | None -> Result.failureOf (missing "a table coordinate" (child path "table"))
        let rows =
            match tryProp "rows" el with
            | Some v when v.ValueKind = JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.mapi (fun i rowEl ->
                    let rowPath = item (child path "rows") i
                    if rowEl.ValueKind <> JsonValueKind.Object then
                        Result.failureOf (wrongType "an object of column → value" rowPath)
                    else
                        rowEl.EnumerateObject()
                        |> Seq.map (fun p ->
                            parsePinValue (child rowPath p.Name) p.Value
                            |> Result.map (fun v -> p.Name, v))
                        |> Result.aggregate)
                |> Result.aggregate
                |> Result.bind (fun rows ->
                    if List.isEmpty rows
                    then Result.failureOf (missing "at least one pinned row" (child path "rows"))
                    else Result.success rows)
            | Some _ -> Result.failureOf (wrongType "an array of row objects" (child path "rows"))
            | None -> Result.failureOf (missing "the pinned rows" (child path "rows"))
        match table, rows, closed with
        | Ok t, Ok r, [] -> Result.success { Table = t; Rows = r }
        | tR, rR, closedErrors -> Result.failure (Result.errors tR @ Result.errors rR @ closedErrors)

    let private parseScenario (name: string) (path: string) (el: JsonElement) : Result<ScenarioIr> =
        if el.ValueKind <> JsonValueKind.Object then Result.failureOf (wrongType "an object" path) else
        let closed = checkClosed path [ "extends"; "scale"; "seed"; "tables"; "pins" ] el
        let extends =
            match tryProp "extends" el with
            | Some v -> readString (child path "extends") v |> Result.map Some
            | None -> Result.success None
        let scale =
            match tryProp "scale" el with
            | Some v -> readDecimal (child path "scale") v |> Result.map Some
            | None -> Result.success None
        let seed =
            match tryProp "seed" el with
            | Some v -> readSeed (child path "seed") v |> Result.map Some
            | None -> Result.success None
        let tables =
            match tryProp "tables" el with
            | Some v when v.ValueKind = JsonValueKind.Object ->
                v.EnumerateObject()
                |> Seq.map (fun p ->
                    let entryPath = child (child path "tables") p.Name
                    let coord =
                        match TableCoordinate.parse p.Name with
                        | Ok c -> Result.success c
                        | Error es -> Result.failure (located entryPath es)
                    match coord with
                    | Ok c -> parseTableOverride entryPath p.Value |> Result.map (fun o -> c, o)
                    | Error es -> Result.failure es)
                |> Result.aggregate
            | Some _ -> Result.failureOf (wrongType "an object of table coordinate → override" (child path "tables"))
            | None -> Result.success []
        let pins =
            match tryProp "pins" el with
            | Some v when v.ValueKind = JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.mapi (fun i p -> parsePin (item (child path "pins") i) p)
                |> Result.aggregate
            | Some _ -> Result.failureOf (wrongType "an array of pins" (child path "pins"))
            | None -> Result.success []
        match extends, scale, seed, tables, pins, closed with
        | Ok e, Ok sc, Ok se, Ok t, Ok p, [] ->
            Result.success { Name = name; Extends = e; Scale = sc; Seed = se; Tables = t; Pins = p }
        | eR, scR, seR, tR, pR, closedErrors ->
            Result.failure (Result.errors eR @ Result.errors scR @ Result.errors seR @ Result.errors tR @ Result.errors pR @ closedErrors)

    /// Extends resolution — every `extends` names a defined scenario and
    /// the inheritance chain is acyclic. Single inheritance; cycles are
    /// refused naming the chain's entry point.
    let private checkExtends (path: string) (scenarios: (string * ScenarioIr) list) : ValidationError list =
        let byName = scenarios |> List.map (fun (n, s) -> n, s) |> Map.ofList
        let unknowns =
            scenarios
            |> List.choose (fun (n, s) ->
                match s.Extends with
                | Some parent when not (Map.containsKey parent byName) ->
                    Some (ValidationError.createWithMetadata
                            "twin.config.scenario.extendsUnknown"
                            "A scenario extends a scenario the configuration does not define."
                            (Map.ofList [ "path", Some (child (child path n) "extends"); "extends", Some parent ]))
                | _ -> None)
        let cycles =
            scenarios
            |> List.choose (fun (n, _) ->
                let rec walk (seen: Set<string>) (current: string) : bool =
                    if Set.contains current seen then true
                    else
                        match Map.tryFind current byName |> Option.bind (fun s -> s.Extends) with
                        | Some parent -> walk (Set.add current seen) parent
                        | None -> false
                if walk Set.empty n then
                    Some (ValidationError.createWithMetadata
                            "twin.config.scenario.extendsCycle"
                            "Scenario inheritance forms a cycle. Break the extends chain."
                            (Map.ofList [ "path", Some (child (child path n) "extends"); "scenario", Some n ]))
                else None)
        unknowns @ cycles

    // ------------------------------------------------------------------
    // The parse entry point.
    // ------------------------------------------------------------------

    let private topLevelKeys =
        [ "estate"; "container"; "evidence"; "corrections"
          "seed"; "scale"; "defaultRows"; "volumes"; "scenarios" ]

    /// Parse `twin.json` text to the typed config. Errors aggregate —
    /// one pass reports every problem, each located by its JSON path.
    let parse (json: string) : Result<TwinConfig> =
        let doc =
            try Ok (JsonDocument.Parse json)
            with ex ->
                Error [ ValidationError.createWithMetadata
                          "twin.config.json"
                          "twin.json is not well-formed JSON. Correct the syntax and rerun."
                          (Map.ofList [ "detail", Some ex.Message ]) ]
        match doc with
        | Error es -> Result.failure es
        | Ok doc ->
            use doc = doc
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                Result.failureOf (wrongType "a JSON object" "$")
            else
                let closed = checkClosed "$" topLevelKeys root
                let estate =
                    match tryProp "estate" root with
                    | Some v -> parseEstate "$.estate" v
                    | None -> Result.failureOf (missing "the estate section (where the table scripts live)" "$.estate")
                let container =
                    match tryProp "container" root with
                    | Some v -> parseContainer "$.container" v
                    | None -> Result.success defaultContainer
                let evidence =
                    match tryProp "evidence" root with
                    | Some v -> parseEvidence "$.evidence" v
                    | None -> Result.success { ShapePath = None; RichRef = None; Sources = [] }
                let corrections =
                    match tryProp "corrections" root with
                    | Some v -> readString "$.corrections" v |> Result.map Some
                    | None -> Result.success None
                let seed =
                    match tryProp "seed" root with
                    | Some v -> readSeed "$.seed" v
                    | None -> Result.success defaultSeed
                let scale =
                    match tryProp "scale" root with
                    | Some v -> readDecimal "$.scale" v
                    | None -> Result.success defaultScale
                let defaultRows =
                    match tryProp "defaultRows" root with
                    | Some v -> readInt "$.defaultRows" 1 System.Int32.MaxValue v
                    | None -> Result.success DefaultRowsPerKind
                let volumes =
                    match tryProp "volumes" root with
                    | Some v ->
                        readString "$.volumes" v
                        |> Result.bind (fun t ->
                            match t with
                            | "flat" -> Result.success FlatVolumes
                            | "centrality" -> Result.success CentralityVolumes
                            | _ -> Result.failureOf (wrongType "\"flat\" or \"centrality\"" "$.volumes"))
                    | None -> Result.success FlatVolumes
                let scenarios =
                    match tryProp "scenarios" root with
                    | Some v when v.ValueKind = JsonValueKind.Object ->
                        v.EnumerateObject()
                        |> Seq.map (fun p ->
                            parseScenario p.Name (child "$.scenarios" p.Name) p.Value
                            |> Result.map (fun s -> p.Name, s))
                        |> Result.aggregate
                        |> Result.bind (fun ss ->
                            match checkExtends "$.scenarios" ss with
                            | [] -> Result.success ss
                            | errors -> Result.failure errors)
                    | Some _ -> Result.failureOf (wrongType "an object of scenario name → scenario" "$.scenarios")
                    | None -> Result.success []
                match estate, container, evidence, corrections, seed, scale, defaultRows, volumes, scenarios, closed with
                | Ok e, Ok c, Ok ev, Ok co, Ok se, Ok sc, Ok dr, Ok vo, Ok scn, [] ->
                    Result.success
                        { Estate = e
                          Container = c
                          Evidence = ev
                          CorrectionsPath = co
                          Seed = se
                          Scale = sc
                          DefaultRows = dr
                          Volumes = vo
                          Scenarios = scn }
                | eR, cR, evR, coR, seR, scR, drR, voR, scnR, closedErrors ->
                    Result.failure
                        (Result.errors eR @ Result.errors cR @ Result.errors evR @ Result.errors coR
                         @ Result.errors seR @ Result.errors scR @ Result.errors drR @ Result.errors voR
                         @ Result.errors scnR @ closedErrors)

    // ------------------------------------------------------------------
    // Canonical renderings — the fingerprint's config contributions.
    // Length-prefixed fields (the SsKey-codec discipline), fixed field
    // order, invariant formatting: the same config renders the same
    // bytes on any host.
    // ------------------------------------------------------------------

    let private field (s: string) : string =
        System.String.Concat(string s.Length, ":", s)  // LINT-ALLOW: length-prefixed canonical-form field; the canonical form IS a string, no AST

    let private fields (parts: string list) : string =
        parts |> List.map field |> String.concat ""

    let private inv (d: decimal) : string =
        d.ToString(System.Globalization.CultureInfo.InvariantCulture)

    let private renderColumnOverride (o: ColumnOverride) : string =
        match o with
        | Weights ws ->
            fields ("weights" :: (ws |> List.collect (fun (v, r) -> [ v; string r ])))
        | Between (lo, hi, skew) ->
            let skewText =
                match skew with
                | SkewUniform -> "uniform"
                | SkewEarly -> "early"
                | SkewLate -> "late"
            fields [ "between"; lo; hi; skewText ]

    let private renderPinValue (v: PinValue) : string =
        match v with
        | PinText t -> fields [ "text"; t ]
        | PinNumber n -> fields [ "number"; n ]
        | PinBool b -> fields [ "bool"; (if b then "1" else "0") ]
        | PinNull -> fields [ "null" ]

    let private renderTableOverride (coord: TableCoordinate, o: TableOverride) : string =
        let columns =
            o.Columns
            |> List.map (fun (c, ov) -> fields [ c.ToLowerInvariant(); renderColumnOverride ov ])
            |> String.concat ""
        let perParent =
            o.PerParent
            |> List.map (fun (p, m) -> fields [ TableCoordinate.key p; inv m ])
            |> String.concat ""
        let rows = match o.Rows with Some r -> string r | None -> ""
        fields [ TableCoordinate.key coord; rows; columns; perParent ]

    let private renderPin (p: Pin) : string =
        let rows =
            p.Rows
            |> List.map (fun row ->
                row
                |> List.map (fun (c, v) -> fields [ c.ToLowerInvariant(); renderPinValue v ])
                |> String.concat "")
            |> String.concat ""
        fields [ TableCoordinate.key p.Table; rows ]

    let private renderScenario (s: ScenarioIr) : string =
        let tables = s.Tables |> List.map renderTableOverride |> String.concat ""
        let pins = s.Pins |> List.map renderPin |> String.concat ""
        let scale = match s.Scale with Some sc -> inv sc | None -> ""
        let seed = match s.Seed with Some se -> string se | None -> ""
        fields [ s.Name; defaultArg s.Extends ""; scale; seed; tables; pins ]

    /// The scenario inheritance chain, base-first: the named scenario's
    /// ancestors then itself. Assumes `checkExtends` held at parse (no
    /// cycles, no unknowns) — a broken link just ends the chain.
    let scenarioChain (config: TwinConfig) (name: string) : ScenarioIr list =
        let byName = Map.ofList config.Scenarios
        let rec walk (acc: ScenarioIr list) (current: string) : ScenarioIr list =
            match Map.tryFind current byName with
            | None -> acc
            | Some s ->
                match s.Extends with
                | Some parent when not (List.exists (fun (a: ScenarioIr) -> a.Name = parent) acc) ->
                    walk (s :: acc) parent
                | _ -> s :: acc
        walk [] name

    /// The schema-plane canonical config text: what, beyond the estate
    /// files themselves, changes what the published schema is.
    let canonicalEstate (c: TwinConfig) : string =
        fields
            [ c.Estate.TablesPattern
              defaultArg c.Estate.SchemasPattern ""
              String.concat ";" c.Estate.StaticData
              c.Container.Image ]

    /// The mint-plane canonical config text: what, beyond the schema and
    /// the evidence/correction artifact contents, changes what a mint
    /// produces. Includes the effective scenario chain (base-first) so an
    /// edit to an inherited scenario invalidates its descendants.
    let canonicalMint (c: TwinConfig) (scenarioName: string) : string =
        fields
            [ string c.Seed
              inv c.Scale
              string c.DefaultRows
              (match c.Volumes with FlatVolumes -> "flat" | CentralityVolumes -> "centrality")
              defaultArg c.CorrectionsPath ""
              defaultArg c.Evidence.ShapePath ""
              defaultArg c.Evidence.RichRef ""
              scenarioName
              scenarioChain c scenarioName |> List.map renderScenario |> String.concat "" ]

    /// The scenario a run uses: the named one, or the baseline when the
    /// configuration defines no scenario of that name and the name is the
    /// baseline default (an explicitly named missing scenario is a
    /// refusal — a typo'd `--scenario quater-end` must not silently mint
    /// baseline data).
    let resolveScenario (config: TwinConfig) (name: string) : Result<ScenarioIr option> =
        match config.Scenarios |> List.tryFind (fun (n, _) -> n = name) with
        | Some (_, s) -> Result.success (Some s)
        | None when name = BaselineScenario -> Result.success None
        | None ->
            Result.failureOf
                (ValidationError.createWithMetadata
                    "twin.scenario.unknown"
                    "The named scenario is not defined in twin.json. Check the spelling against the scenarios section."
                    (Map.ofList [ "scenario", Some name ]))
