namespace Projection.Pipeline

// LINT-ALLOW-FILE: the pay-once evidence store (DECISIONS 2026-07-15 — the
//   estate chapter opens, entry 4). The sidecar codec + directory layout
//   compose machine-local file paths and structured JSON at a persistence
//   boundary; the staleness derivation (`staleKinds`), the codec core, and the
//   directory-resolution rule are pure — the I/O surface (atomic save /
//   fail-closed load) is the thin boundary the `check estate` face calls.

open System
open System.IO
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.Json

/// One kind's staleness fingerprint — the cheap re-derivable shape that gates
/// evidence reuse (the `CaptureJournal.fingerprintOf` precedent, per-kind).
/// The `(RowCount, MaxPk)` pair catches inserts and deletes; `ContentHash`
/// (an aggregate row checksum) closes the in-place-UPDATE blindness that was
/// survival rule 14 — an UPDATE that changes a value but neither the row
/// count nor the max PK still moves the content hash, so cached evidence is
/// not reused over stale reality. `SchemaShapeHash` catches what neither can:
/// a type/nullability change at identical rows and content. The residual
/// caveats (a kind with only an XML column carries no content hash and falls
/// back to row/PK; a checksum collision is vanishingly rare) keep `--refresh`
/// the override, and evidence age still rides every decision line.
type KindFingerprint =
    {
        Kind            : SsKey
        /// `COUNT_BIG(*)` at capture.
        RowCount        : int64
        /// `MAX(pk)`'s canonical string at capture; `None` for a PK-less kind.
        MaxPk           : string option
        /// `CHECKSUM_AGG(BINARY_CHECKSUM(...))` at capture — the content-movement
        /// signal (survival rule 14's mitigation). `None` for a kind with no
        /// checksummable column. An older sidecar (pre-content-hash) parses this
        /// as `None`; a live probe returns `Some`, so the record inequality
        /// reads as movement and re-profiles — backward-compatible in the safe
        /// direction.
        ContentHash     : string option
        /// SHA256 over the kind's logical shape at capture (an opaque token —
        /// the probe computes it; the store only compares).
        SchemaShapeHash : string
    }

/// One environment's durable evidence pair: the whole `Profile` (the same
/// artifact `profile <env> --out` writes — one durable form, two producers)
/// plus its staleness sidecar.
type EnvEvidence =
    {
        Profile       : Profile
        Fingerprints  : KindFingerprint list
        CapturedAtUtc : DateTimeOffset
    }

[<RequireQualifiedAccess>]
module EstateEvidenceStore =

    // ------------------------------------------------------------------
    // Directory resolution — one rule for every reader (the `Run.storeDir`
    // R1d precedent): `PROJECTION_ESTATE_DIR` when set; else the ledger
    // dir's `estate/` child; else the store is DISABLED — the run is
    // live-only, and the report says so (never a silent degradation).
    // ------------------------------------------------------------------

    /// The pure resolution over the two variables' values (testable without
    /// process-global environment mutation).
    let storeDirFrom (estateDir: string option) (ledgerDir: string option) : string option =
        match estateDir with
        | Some d when not (String.IsNullOrWhiteSpace d) -> Some d
        | _ ->
            match ledgerDir with
            | Some l when not (String.IsNullOrWhiteSpace l) -> Some (Path.Combine(l, "estate"))
            | _ -> None

    /// The boundary read: resolve from the process environment.
    let storeDir () : string option =
        storeDirFrom
            (Option.ofObj (Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"))
            (Option.ofObj (Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR"))

    let private evidenceDir (root: string) (env: string) : string =
        Path.Combine(root, "evidence", env)

    /// The durable profile — `ProfileCodec.serialize`'s artifact, byte-shared
    /// with `profile <env> --out`.
    let profilePath (root: string) (env: string) : string =
        Path.Combine(evidenceDir root env, "profile.json")

    /// The staleness sidecar: capture time · the profile's SHA256 binding ·
    /// the per-kind fingerprints.
    let fingerprintsPath (root: string) (env: string) : string =
        Path.Combine(evidenceDir root env, "fingerprints.json")

    // ------------------------------------------------------------------
    // The staleness derivation — pure.
    // ------------------------------------------------------------------

    /// The kinds whose fingerprint moved since capture — including a kind
    /// newly present or newly absent on either side (both are movement; a
    /// silent skip here would launder a dropped or added table into "clean").
    let staleKinds (cached: KindFingerprint list) (live: KindFingerprint list) : SsKey list =
        let index (fps: KindFingerprint list) : Map<SsKey, KindFingerprint> =
            fps |> List.map (fun f -> f.Kind, f) |> Map.ofList
        let cachedIx = index cached
        let liveIx = index live
        Set.union (cachedIx |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
                  (liveIx |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        |> Set.toList
        |> List.filter (fun kind ->
            match Map.tryFind kind cachedIx, Map.tryFind kind liveIx with
            | Some a, Some b -> a <> b
            | _ -> true)

    // ------------------------------------------------------------------
    // The sidecar codec — deterministic out, fail-closed back.
    // ------------------------------------------------------------------

    let private sha256Hex (text: string) : string =
        Convert.ToHexString(
            Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes text))

    let private sidecarJson
        (capturedAtUtc: DateTimeOffset)
        (profileSha256: string)
        (env: string)
        (fingerprints: KindFingerprint list)
        : string =
        let root = JsonObject()
        root.["env"] <- JsonValue.Create env
        root.["capturedAtUtc"] <- JsonValue.Create(capturedAtUtc.ToString "O")
        root.["profileSha256"] <- JsonValue.Create profileSha256
        let kinds = JsonArray()
        for fp in fingerprints |> List.sortBy (fun f -> SsKey.serialize f.Kind) do
            let o = JsonObject()
            o.["kind"] <- JsonValue.Create(SsKey.serialize fp.Kind)
            o.["rowCount"] <- JsonValue.Create fp.RowCount
            (match fp.MaxPk with
             | Some pk -> o.["maxPk"] <- JsonValue.Create pk
             | None -> ())
            (match fp.ContentHash with
             | Some h -> o.["contentHash"] <- JsonValue.Create h
             | None -> ())
            o.["schemaShapeHash"] <- JsonValue.Create fp.SchemaShapeHash
            kinds.Add o
        root.["kinds"] <- kinds
        root.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true))

    // The read-back helpers ride `ModelFidelity.fromJson`'s idioms: nullable
    // nodes narrow through `Option.ofObj`; the value coercions catch only the
    // type-mismatch pair (`InvalidOperationException` / `FormatException`) so
    // fatals propagate rather than masquerading as an absent cache.

    let private tryNode (o: JsonObject) (key: string) : JsonNode option =
        match o.TryGetPropertyValue key with
        | true, node -> Option.ofObj node
        | _ -> None

    let private tryStr (o: JsonObject) (key: string) : string option =
        tryNode o key
        |> Option.bind (fun node ->
            try Some (node.GetValue<string>())
            with :? InvalidOperationException | :? FormatException -> None)

    let private tryInt64 (o: JsonObject) (key: string) : int64 option =
        tryNode o key
        |> Option.bind (fun node ->
            try Some (node.GetValue<int64>())
            with :? InvalidOperationException | :? FormatException -> None)

    let private asObject (node: JsonNode) : JsonObject option =
        match node with :? JsonObject as o -> Some o | _ -> None

    let private asArray (node: JsonNode) : JsonArray option =
        match node with :? JsonArray as a -> Some a | _ -> None

    /// The non-null elements of a JSON array, narrowed for nullness.
    let private elements (arr: JsonArray) : JsonNode list =
        [ for n in arr do match Option.ofObj n with Some node -> yield node | None -> () ]

    let private fingerprintOfNode (node: JsonNode) : KindFingerprint option =
        match asObject node with
        | None -> None
        | Some o ->
            match tryStr o "kind" |> Option.bind (fun s -> SsKey.deserialize s |> Result.toOption),
                  tryInt64 o "rowCount",
                  tryStr o "schemaShapeHash" with
            | Some kind, Some rowCount, Some hash ->
                Some
                    { Kind = kind
                      RowCount = rowCount
                      MaxPk = tryStr o "maxPk"
                      ContentHash = tryStr o "contentHash"
                      SchemaShapeHash = hash }
            | _ -> None

    /// Parse the sidecar back — `None` on any malformed shape, including a
    /// single unparseable fingerprint (fail-closed: a partially-readable
    /// sidecar reads as an absent cache, named at the face — never a
    /// silently-thinner evidence basis).
    let private tryParseSidecar (text: string) : (DateTimeOffset * string * KindFingerprint list) option =
        try
            match Option.ofObj (JsonNode.Parse text) |> Option.bind asObject with
            | None -> None
            | Some root ->
                let captured =
                    tryStr root "capturedAtUtc"
                    |> Option.bind (fun s ->
                        match DateTimeOffset.TryParse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
                        | true, v -> Some v
                        | _ -> None)
                let sha = tryStr root "profileSha256"
                let fingerprints =
                    tryNode root "kinds"
                    |> Option.bind asArray
                    |> Option.bind (fun arr ->
                        let parsed = elements arr |> List.map fingerprintOfNode
                        if parsed |> List.forall Option.isSome
                        then Some (parsed |> List.choose id)
                        else None)
                match captured, sha, fingerprints with
                | Some c, Some s, Some fps -> Some (c, s, fps)
                | _ -> None
        // Malformed top-level JSON (the fail-closed contract); a fatal still
        // propagates rather than masquerading as an absent cache.
        with :? System.Text.Json.JsonException -> None

    // ------------------------------------------------------------------
    // The I/O surface — atomic writes, advisory failures, fail-closed reads.
    // ------------------------------------------------------------------

    let private writeAtomic (path: string) (content: string) : unit =
        let tmp = path + ".tmp"
        File.WriteAllText(tmp, content)
        File.Move(tmp, path, overwrite = true)

    /// Persist one environment's evidence pair. A failure is ADVISORY — the
    /// caller warns and the check proceeds live-only; a cache write never
    /// fails a read-only verb (the `Shell.appendLedger` catch precedent).
    let save
        (capturedAtUtc: DateTimeOffset)
        (root: string)
        (env: string)
        (profile: Profile)
        (fingerprints: KindFingerprint list)
        : Result<unit> =
        try
            Directory.CreateDirectory(evidenceDir root env) |> ignore
            let profileText = ProfileCodec.serialize profile
            writeAtomic (profilePath root env) profileText
            writeAtomic (fingerprintsPath root env) (sidecarJson capturedAtUtc (sha256Hex profileText) env fingerprints)
            Result.success ()
        with
        | :? IOException as ex ->
            Result.failureOf (ValidationError.create "estate.evidence.writeFailed" ex.Message)
        | :? UnauthorizedAccessException as ex ->
            Result.failureOf (ValidationError.create "estate.evidence.writeFailed" ex.Message)

    /// Load one environment's evidence pair. `None` when absent, unreadable,
    /// or when the sidecar's SHA does not bind the profile text (a mismatched
    /// pair is a torn write — it reads as absent, named at the face, never a
    /// silently-wrong basis).
    let load (root: string) (env: string) : EnvEvidence option =
        try
            let pPath = profilePath root env
            let fPath = fingerprintsPath root env
            if not (File.Exists pPath && File.Exists fPath) then None
            else
                let profileText = File.ReadAllText pPath
                match tryParseSidecar (File.ReadAllText fPath) with
                | Some (captured, sha, fingerprints) when sha = sha256Hex profileText ->
                    match ProfileCodec.deserialize profileText with
                    | Ok profile ->
                        Some { Profile = profile; Fingerprints = fingerprints; CapturedAtUtc = captured }
                    | Error _ -> None
                | _ -> None
        with
        | :? IOException -> None
        | :? UnauthorizedAccessException -> None

    // ------------------------------------------------------------------
    // The live fingerprint — the schema-shape half is pure (the catalog's
    // canonical kind bytes, hashed); the row half is the one-batch SQL
    // probe (`EvidenceFingerprint.probe`, Adapters.Sql).
    // ------------------------------------------------------------------

    /// SHA256 over one kind's canonical JSON (`CatalogCodec.serializeKind`)
    /// — the fingerprint's counterweight to the `(RowCount, MaxPk)` caveat:
    /// a type or nullability change invalidates a kind's data evidence at
    /// an identical row count. Pure and deterministic by the codec's law.
    let shapeHashOf (kind: Kind) : string =
        sha256Hex (CatalogCodec.serializeKind kind)

    /// Join the probe's row readings with the catalog's shape hashes into
    /// the store's fingerprint form. Pure; a reading whose kind the catalog
    /// no longer carries hashes empty — record inequality then reads as
    /// movement, the safe direction.
    let fingerprintsOf (catalog: Catalog) (readings: FingerprintReading list) : KindFingerprint list =
        let hashByKind =
            catalog
            |> Catalog.allKinds
            |> List.map (fun k -> k.SsKey, shapeHashOf k)
            |> Map.ofList
        readings
        |> List.map (fun r ->
            { Kind = r.Kind
              RowCount = r.RowCount
              MaxPk = r.MaxPk
              ContentHash = r.Content
              SchemaShapeHash = Map.tryFind r.Kind hashByKind |> Option.defaultValue "" })

    /// The live staleness probe for one environment: resolve the conn-ref
    /// under the one resolution rule (`Source.resolveConn`), read every
    /// kind's `(COUNT_BIG, MAX(pk))` in one round-trip, and join the pure
    /// shape hashes. A failure is the caller's NAMED degradation — the
    /// estate falls back to live profiling (fresh evidence; only the
    /// pay-once saving is lost).
    let probeLive (connRef: string) (catalog: Catalog) : Task<Result<KindFingerprint list>> =
        task {
            try
                use cnn = new SqlConnection(Source.resolveConn connRef)
                do! cnn.OpenAsync()
                let! readings = EvidenceFingerprint.probe cnn (Catalog.allKinds catalog)
                return readings |> Result.map (fingerprintsOf catalog)
            with ex ->
                return
                    Result.failureOf
                        (ValidationError.create "estate.evidence.probeFailed" ex.Message)
        }
