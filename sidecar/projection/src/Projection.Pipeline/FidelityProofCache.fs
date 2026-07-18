namespace Projection.Pipeline

// LINT-ALLOW-FILE: the incremental fidelity-proof cache (wave B6). Machine-local
//   file paths + structured JSON at a persistence boundary — the same sidecar
//   codec + atomic-write + fail-closed idiom `EstateEvidenceStore` established,
//   and the freshness derivation reuses that store's `KindFingerprint` /
//   `staleKinds` / `shapeHashOf` / `probeLive` directly. The one terminal-text
//   surface is the JSON compose/parse; the resolution + freshness are pure.

open System
open System.IO
open System.Text.Json.Nodes
open Projection.Core

/// One flow's last GREEN proof, cached so an unchanged estate skips the
/// EXPENSIVE container proof (scaffold → transfer → compare → reap). Keyed by
/// the FLOW — one file per flow, `<store>/proofs/<flow>.proof.json` — so the
/// cache is trivially clearable by flow name (delete one file), or wholesale
/// (`rm -rf <store>/proofs`). The inputs that would INVALIDATE the proof ride
/// INSIDE the file (the model's shape digest + the source's per-kind
/// fingerprints), compared on read; a moved fingerprint or a model retype makes
/// the entry stale and forces a re-prove. Only a GREEN proof is ever cached (the
/// writer clears a non-green one), so the mere PRESENCE of a fresh entry means
/// "the last proof was green and nothing has moved since" — a residual can never
/// short-circuit a re-prove.
type CachedProof =
    {
        /// The model's logical-shape digest at prove time — a retype / renullable
        /// moves it and invalidates the proof.
        ModelHash       : string
        /// The source's per-kind fingerprints at prove time — a moved fingerprint
        /// (rows added/removed, a schema shift) invalidates it.
        Fingerprints    : KindFingerprint list
        /// Carried for the operator-facing "N rows proven" line on a cache hit.
        RowsCompared    : int64
        DifferenceTotal : int64
        WrittenAtUtc    : DateTimeOffset
    }

[<RequireQualifiedAccess>]
module FidelityProofCache =

    let private sha256Hex (text: string) : string =
        Convert.ToHexString(
            Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes text))

    /// The proofs directory under a given estate store root — `<root>/proofs`.
    /// The root is the caller's resolved `EstateEvidenceStore.storeDir ()`; the
    /// I/O is root-parameterized (the `EstateEvidenceStore.save`/`load` pattern)
    /// so it tests against a temp dir with no process-global env mutation.
    let proofsDir (root: string) : string = Path.Combine(root, "proofs")

    /// A flow name reduced to a safe file stem — path separators and `..` cannot
    /// escape the proofs directory (the flow name is operator-supplied).
    let private safeStem (flow: string) : string =
        let mapped =
            flow |> String.map (fun c -> if Char.IsLetterOrDigit c || c = '-' || c = '_' then c else '_')
        if mapped = "" || mapped = "." || mapped = ".." then "_" else mapped

    /// One flow's cache file — `<root>/proofs/<flow>.proof.json`.
    let cachePath (root: string) (flow: string) : string =
        Path.Combine(proofsDir root, safeStem flow + ".proof.json")

    /// The model's logical-shape digest — SHA256 over the SORTED per-kind shape
    /// hashes (each `EstateEvidenceStore.shapeHashOf`), so a type/nullability
    /// change moves it. Pure and deterministic by the catalog codec's law.
    let modelHash (model: Catalog) : string =
        model
        |> Catalog.allKinds
        |> List.map EstateEvidenceStore.shapeHashOf
        |> List.sort
        |> String.concat ""
        |> sha256Hex

    // -- the codec (the `EstateEvidenceStore` sidecar shape, per-flow) ---------

    let private toJson (proof: CachedProof) : string =
        let root = JsonObject()
        root.["modelHash"] <- JsonValue.Create proof.ModelHash
        root.["rowsCompared"] <- JsonValue.Create proof.RowsCompared
        root.["differenceTotal"] <- JsonValue.Create proof.DifferenceTotal
        root.["writtenAtUtc"] <- JsonValue.Create(proof.WrittenAtUtc.ToString "O")
        let kinds = JsonArray()
        for fp in proof.Fingerprints |> List.sortBy (fun f -> SsKey.serialize f.Kind) do
            let o = JsonObject()
            o.["kind"] <- JsonValue.Create(SsKey.serialize fp.Kind)
            o.["rowCount"] <- JsonValue.Create fp.RowCount
            (match fp.MaxPk with Some pk -> o.["maxPk"] <- JsonValue.Create pk | None -> ())
            o.["schemaShapeHash"] <- JsonValue.Create fp.SchemaShapeHash
            kinds.Add o
        root.["fingerprints"] <- kinds
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

    let private fingerprintOfNode (node: JsonNode) : KindFingerprint option =
        match node with
        | :? JsonObject as o ->
            match tryStr o "kind" |> Option.bind (fun s -> SsKey.deserialize s |> Result.toOption),
                  tryI64 o "rowCount",
                  tryStr o "schemaShapeHash" with
            | Some kind, Some rows, Some hash ->
                Some { Kind = kind; RowCount = rows; MaxPk = tryStr o "maxPk"; SchemaShapeHash = hash }
            | _ -> None
        | _ -> None

    /// Read one flow's cached proof — `None` when absent, unreadable, or
    /// malformed (fail-closed: a torn cache reads as absent and the proof runs;
    /// a stale cache never masquerades as fresh, and a corrupt one never blocks
    /// a proof).
    let tryRead (root: string) (flow: string) : CachedProof option =
        let path = cachePath root flow
        try
            if not (File.Exists path) then None
            else
                match Option.ofObj (JsonNode.Parse(File.ReadAllText path)) with
                | Some (:? JsonObject as o) ->
                    let fps =
                        tryNode o "fingerprints"
                        |> Option.bind (function :? JsonArray as a -> Some a | _ -> None)
                        |> Option.bind (fun arr ->
                            let parsed = [ for n in arr do match Option.ofObj n with Some node -> yield fingerprintOfNode node | None -> () ]
                            if parsed |> List.forall Option.isSome then Some (List.choose id parsed) else None)
                    let written =
                        tryStr o "writtenAtUtc"
                        |> Option.bind (fun s ->
                            match DateTimeOffset.TryParse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
                            | true, v -> Some v
                            | _ -> None)
                    match tryStr o "modelHash", fps, written with
                    | Some mh, Some fingerprints, Some w ->
                        Some
                            { ModelHash = mh
                              Fingerprints = fingerprints
                              RowsCompared = tryI64 o "rowsCompared" |> Option.defaultValue 0L
                              DifferenceTotal = tryI64 o "differenceTotal" |> Option.defaultValue 0L
                              WrittenAtUtc = w }
                    | _ -> None
                | _ -> None
        with
        | :? IOException -> None
        | :? UnauthorizedAccessException -> None
        | :? System.Text.Json.JsonException -> None

    /// Is a cached proof still valid for the current world? The model shape is
    /// unchanged AND no source fingerprint has moved. A fresh entry exists only
    /// for a green proof (the writer clears a non-green one), so freshness ⇒ the
    /// last proof was green and nothing has moved since.
    let isFresh (cached: CachedProof) (currentModelHash: string) (currentFingerprints: KindFingerprint list) : bool =
        cached.ModelHash = currentModelHash
        && List.isEmpty (EstateEvidenceStore.staleKinds cached.Fingerprints currentFingerprints)

    // -- the I/O surface (atomic writes, advisory failures) -------------------

    /// Persist one flow's green proof (atomic — `*.tmp` + move). ADVISORY — a
    /// write failure is swallowed and the proof still stands (a cache write never
    /// fails a read-only verb, the `EstateEvidenceStore.save` precedent).
    let write (root: string) (flow: string) (proof: CachedProof) : unit =
        try
            Directory.CreateDirectory(proofsDir root) |> ignore
            let path = cachePath root flow
            let tmp = path + ".tmp"
            File.WriteAllText(tmp, toJson proof)
            File.Move(tmp, path, overwrite = true)
        with
        | :? IOException -> ()
        | :? UnauthorizedAccessException -> ()

    /// Clear one flow's cached proof (the `--refresh` clear, and the non-green
    /// clear). ADVISORY and idempotent — a missing file is already clear. Returns
    /// whether a file was removed (so the face can name the clear).
    let clear (root: string) (flow: string) : bool =
        let path = cachePath root flow
        try
            if File.Exists path then File.Delete path; true else false
        with
        | :? IOException -> false
        | :? UnauthorizedAccessException -> false

    /// Clear EVERY flow's cached proof — remove the whole `proofs/` directory.
    /// ADVISORY; returns whether the directory existed.
    let clearAll (root: string) : bool =
        let dir = proofsDir root
        try
            if Directory.Exists dir then Directory.Delete(dir, true); true else false
        with
        | :? IOException -> false
        | :? UnauthorizedAccessException -> false
