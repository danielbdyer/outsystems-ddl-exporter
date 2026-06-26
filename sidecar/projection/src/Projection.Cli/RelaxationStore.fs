module Projection.Cli.RelaxationStore
// LINT-ALLOW-FILE-MUTATION: sealed function-local mutable accumulator in the relaxation-store merge; returned immutably

open System.IO
open System.Text.Json
open System.Text.Json.Nodes

/// The durable home of operator-blessed tightening relaxations: a
/// `tighteningRelaxations` string array in `projection.json` (the `kind.column`
/// keys from `Preflight.violationKey`). Relax-ALWAYS writes here; a later
/// HEADLESS migrate reads here to honor the blessing without prompting (the
/// A44-reachable equivalent of the interactive choice — a future run is total
/// because the override is now a named, persisted exception).
///
/// Written by a SURGICAL JSON merge: every other key in `projection.json` is
/// preserved byte-for-byte. The config doctrine is that the parser ignores
/// unknown keys, so this rides ALONGSIDE the movement vocabulary without
/// touching `renderConfig` or its `parse ∘ render = id` round-trip (A44).
/// (Making `renderConfig` itself preserve it is the broader audit-F7 fix.)

[<Literal>]
let private key = "tighteningRelaxations"

/// The configured `projection.json` path (`PROJECTION_CONFIG`, else the default).
let configPath () : string =
    match System.Environment.GetEnvironmentVariable "PROJECTION_CONFIG" with
    | null | "" -> "projection.json"
    | p -> p

let private rootObjectOf (path: string) : JsonObject option =
    if not (File.Exists path) then None
    else
        try
            match JsonNode.Parse(File.ReadAllText path) with
            | :? JsonObject as o -> Some o
            | _ -> None
        with _ -> None

/// The blessed relaxation keys recorded in `path` — empty when the file or the
/// section is absent / malformed (a missing blessing is simply no blessing).
let read (path: string) : Set<string> =
    match rootObjectOf path with
    | None -> Set.empty
    | Some o ->
        match o.TryGetPropertyValue key with
        | true, arrNode ->
            match arrNode with
            | :? JsonArray as a ->
                a
                |> Seq.choose (fun n ->
                    match n with
                    | :? JsonValue as v ->
                        match v.TryGetValue<string>() with
                        | true, s -> Option.ofObj s
                        | _ -> None
                    | _ -> None)
                |> Set.ofSeq
            | _ -> Set.empty
        | _ -> Set.empty

/// Add `keys` to `path`'s relaxation section (deduped + sorted), preserving every
/// other key byte-for-byte. Creates the file as a minimal object only if absent.
/// Returns true on a successful write.
let persist (path: string) (keys: string seq) : bool =
    try
        let root =
            match rootObjectOf path with
            | Some o -> o
            | None when File.Exists path -> JsonObject()   // present but not an object — start fresh
            | None -> JsonObject()
        let merged = Set.union (read path) (Set.ofSeq keys) |> Set.toList |> List.sort
        let arr = JsonArray()
        for k in merged do arr.Add(JsonValue.Create k)
        root.[key] <- arr
        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        true
    with _ -> false
