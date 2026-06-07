namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: function-local accumulators while parsing the JSON
//   target-config DOM into the immutable typed record; the mutation is sealed
//   at each parse function's exit (mirrors Config.fs).

open System
open System.IO
open System.Text.Json
open Projection.Core

// THE_CLI.md §4 — the `projection.json` target-aliasing surface and the
// `--to` resolver, plus the argv → `Intent` dispatch the four verbs share.
// D9 holds in config: a target carries a connection *reference*
// (`env:` / `file:`), never a literal connection string.

/// How a named target is addressed: a live database (out-of-band ref) or
/// a folder on disk. The two `Destination` shapes a target may resolve to.
[<RequireQualifiedAccess>]
type TargetAddress =
    | LiveRef of ConnectionRef
    | FolderPath of string

/// One named target from `projection.json`. Carries addressing plus the
/// benign defaults config may set (never intent or danger — `--go`,
/// `--rekey`, `--allow-drops` stay on the command line).
type Target =
    {
        Name     : string
        Address  : TargetAddress
        Store    : string option
        Scope    : Scope option
        Strategy : Strategy option
    }

/// The parsed `projection.json`: named targets, the default authored model
/// (so `project --to dev` needs no model path), plus a global defaults block.
type TargetConfig =
    {
        Targets  : Map<string, Target>
        Model    : string option
        Defaults : Map<string, string>
    }

/// A resolved `--to` value: the destination plus the benign defaults the
/// matched target contributes (empty for a scheme-prefixed or path form).
type ResolvedTarget =
    {
        Destination : Destination
        Store       : string option
        Scope       : Scope option
        Strategy    : Strategy option
    }

[<RequireQualifiedAccess>]
module TargetConfig =

    let empty : TargetConfig = { Targets = Map.empty; Model = None; Defaults = Map.empty }

    let private err (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    /// D9 belt: reject a value that looks like an inline secret rather than
    /// a reference (a connection string pasted into the config).
    let private looksLikeSecret (value: string) : bool =
        let v = value.ToLowerInvariant()
        v.Contains "password" || v.Contains "pwd=" || v.Contains ";"

    let private getString (el: JsonElement) (name: string) : string option =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String ->
            match v.GetString() with
            | null -> None
            | s when String.IsNullOrWhiteSpace s -> None
            | s -> Some (s.Trim())
        | _ -> None

    let private parseScope (raw: string) : Result<Scope> =
        match raw.ToLowerInvariant() with
        | "all"    -> Result.success Scope.All
        | "schema" -> Result.success Scope.Schema
        | "data"   -> Result.success Scope.Data
        | other    -> Result.failureOf (err "cli.config.scopeUnknown" (sprintf "scope '%s' is not all | schema | data." other))

    let private parseStrategy (raw: string) : Result<Strategy> =
        match raw.ToLowerInvariant() with
        | "merge"   -> Result.success Strategy.Merge
        | "replace" -> Result.success Strategy.Replace
        | "fresh"   -> Result.success Strategy.Fresh
        | other     -> Result.failureOf (err "cli.config.strategyUnknown" (sprintf "strategy '%s' is not merge | replace | fresh." other))

    let private parseTarget (name: string) (el: JsonElement) : Result<Target> =
        if el.ValueKind <> JsonValueKind.Object then
            Result.failureOf (err "cli.config.targetShape" (sprintf "target '%s' must be a JSON object." name))
        else
            let connRaw = getString el "conn"
            let dirRaw  = getString el "dir"
            let address : Result<TargetAddress> =
                match connRaw, dirRaw with
                | Some _, Some _ ->
                    Result.failureOf (err "cli.config.targetAmbiguous" (sprintf "target '%s' sets both 'conn' and 'dir'; choose one." name))
                | None, None ->
                    Result.failureOf (err "cli.config.targetAddressMissing" (sprintf "target '%s' sets neither 'conn' nor 'dir'." name))
                | Some conn, None ->
                    if looksLikeSecret conn then
                        Result.failureOf (err "cli.config.targetSecretInline" (sprintf "target '%s' conn looks like an inline secret; use env:<VAR> or file:<path> (D9)." name))
                    else
                        match TransferSpec.parseConnectionSpec conn with
                        | Ok r    -> Result.success (TargetAddress.LiveRef r)
                        | Error e -> Result.failure e
                | None, Some dir ->
                    Result.success (TargetAddress.FolderPath dir)
            match address with
            | Error e -> Result.failure e
            | Ok addr ->
                let scopeR =
                    match getString el "scope" with
                    | None -> Result.success None
                    | Some s -> match parseScope s with Ok v -> Result.success (Some v) | Error e -> Result.failure e
                let strategyR =
                    match getString el "strategy" with
                    | None -> Result.success None
                    | Some s -> match parseStrategy s with Ok v -> Result.success (Some v) | Error e -> Result.failure e
                match scopeR, strategyR with
                | Ok scope, Ok strategy ->
                    Result.success
                        { Name = name; Address = addr; Store = getString el "store"; Scope = scope; Strategy = strategy }
                | _ ->
                    let es =
                        (match scopeR with Error e -> e | _ -> [])
                        @ (match strategyR with Error e -> e | _ -> [])
                    Result.failure es

    /// Parse the `projection.json` document text into a `TargetConfig`.
    /// Aggregates every per-target error so the operator sees them all.
    let parse (json: string) : Result<TargetConfig> =
        if String.IsNullOrWhiteSpace json then Result.success empty
        else
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                Result.failureOf (err "cli.config.shape" "projection.json root must be a JSON object.")
            else
                let targetResults =
                    match root.TryGetProperty "targets" with
                    | true, t when t.ValueKind = JsonValueKind.Object ->
                        [ for p in t.EnumerateObject() -> parseTarget p.Name p.Value ]
                    | _ -> []
                let errors = targetResults |> List.collect (function Error e -> e | Ok _ -> [])
                if not (List.isEmpty errors) then Result.failure errors
                else
                    let targets =
                        targetResults
                        |> List.choose (function Ok t -> Some (t.Name, t) | _ -> None)
                        |> Map.ofList
                    let defaults =
                        match root.TryGetProperty "defaults" with
                        | true, d when d.ValueKind = JsonValueKind.Object ->
                            [ for p in d.EnumerateObject() do
                                match getString d p.Name with
                                | Some v -> yield (p.Name, v)
                                | None -> () ]
                            |> Map.ofList
                        | _ -> Map.empty
                    Result.success { Targets = targets; Model = getString root "model"; Defaults = defaults }
        with ex ->
            Result.failureOf (err "cli.config.parseFailed" (sprintf "projection.json did not parse: %s" ex.Message))

    /// Read and parse `projection.json` from disk; an absent file is the
    /// empty config (aliasing is opt-in, not required).
    let fromFile (path: string) : Result<TargetConfig> =
        if not (File.Exists path) then Result.success empty
        else
            try parse (File.ReadAllText path)
            with ex -> Result.failureOf (err "cli.config.readFailed" (sprintf "could not read '%s': %s" path ex.Message))

[<RequireQualifiedAccess>]
module Surface =

    let private err (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    let private looksLikePath (raw: string) : bool =
        raw.Contains "/" || raw.Contains "\\" || raw.StartsWith "." || raw.EndsWith "/"

    /// Resolve a `--to` value to a `ResolvedTarget` (THE_CLI.md §4.1):
    /// reserved `docker`; a named config target; a scheme-prefixed ref
    /// (`dir:` / `env:` / `file:`); a path-shaped folder; else refused
    /// with the known targets named.
    let resolveTarget (cfg: TargetConfig) (raw: string) : Result<ResolvedTarget> =
        let plain dest = Result.success { Destination = dest; Store = None; Scope = None; Strategy = None }
        if String.IsNullOrWhiteSpace raw then
            Result.failureOf (err "cli.to.empty" "--to requires a destination.")
        elif raw = "docker" then
            plain Destination.Docker
        elif raw.StartsWith "dir:" then
            plain (Destination.Folder (raw.Substring(4)))
        elif raw.StartsWith "env:" || raw.StartsWith "file:" then
            match TransferSpec.parseConnectionSpec raw with
            | Ok r    -> plain (Destination.Live r)
            | Error e -> Result.failure e
        else
            match Map.tryFind raw cfg.Targets with
            | Some t ->
                let dest =
                    match t.Address with
                    | TargetAddress.LiveRef r    -> Destination.Live r
                    | TargetAddress.FolderPath p -> Destination.Folder p
                Result.success { Destination = dest; Store = t.Store; Scope = t.Scope; Strategy = t.Strategy }
            | None ->
                if looksLikePath raw then plain (Destination.Folder raw)
                else
                    let known = cfg.Targets |> Map.toList |> List.map fst |> String.concat ", "
                    let suffix = if known = "" then "no targets configured." else sprintf "known targets: %s." known
                    Result.failureOf (err "cli.to.unknownTarget" (sprintf "unknown destination '%s'; %s" raw suffix))

    /// Apply a resolved target's benign defaults beneath an already-built
    /// spec. Precedence (THE_CLI.md §4): the spec's values (from the CLI)
    /// win; the target only fills what the CLI left at its built-in default.
    let withTargetDefaults (resolved: ResolvedTarget) (cliSetScope: bool) (cliSetStrategy: bool) (spec: MovementSpec) : MovementSpec =
        let scope = if cliSetScope then spec.Scope else (defaultArg resolved.Scope spec.Scope)
        let strategy = if cliSetStrategy then spec.Strategy else (defaultArg resolved.Strategy spec.Strategy)
        { spec with Scope = scope; Strategy = strategy }

    // --- the project flag reader (THE_CLI.md §3.1) -------------------------

    let private readFlag (name: string) (argv: string list) : string option =
        let rec loop = function
            | a :: v :: _ when a = name -> Some v
            | _ :: rest -> loop rest
            | [] -> None
        loop argv

    let private hasFlag (name: string) (argv: string list) : bool =
        argv |> List.contains name

    /// Every `<value>` immediately following a repeated `name` flag.
    let private readMany (name: string) (argv: string list) : string list =
        let rec loop acc = function
            | a :: v :: rest when a = name -> loop (v :: acc) rest
            | _ :: rest -> loop acc rest
            | [] -> List.rev acc
        loop [] argv

    let private parseData (raw: string) : DataOrigin =
        match raw.ToLowerInvariant() with
        | "model"     -> DataOrigin.Model
        | "synthetic" -> DataOrigin.Synthetic
        | "none"      -> DataOrigin.NoData
        | _           -> DataOrigin.FromTarget raw   // any other token is a target alias (the transfer)

    let private parseShape (raw: string) : Result<Shape> =
        match raw.ToLowerInvariant() with
        | "bundle"   -> Result.success Shape.Bundle
        | "ssdt"     -> Result.success Shape.Ssdt
        | "skeleton" -> Result.success Shape.Skeleton
        | other      -> Result.failureOf (err "cli.shape.unknown" (sprintf "shape '%s' is not bundle | ssdt | skeleton." other))

    let private parseScopeFlag (raw: string) : Result<Scope> =
        match raw.ToLowerInvariant() with
        | "all" -> Result.success Scope.All
        | "schema" -> Result.success Scope.Schema
        | "data" -> Result.success Scope.Data
        | other -> Result.failureOf (err "cli.scope.unknown" (sprintf "scope '%s' is not all | schema | data." other))

    let private parseStrategyFlag (raw: string) : Result<Strategy> =
        match raw.ToLowerInvariant() with
        | "merge" -> Result.success Strategy.Merge
        | "replace" -> Result.success Strategy.Replace
        | "fresh" -> Result.success Strategy.Fresh
        | other -> Result.failureOf (err "cli.strategy.unknown" (sprintf "strategy '%s' is not merge | replace | fresh." other))

    let private parseBaseline (raw: string) : Baseline =
        match raw.ToLowerInvariant() with
        | "auto"  -> Baseline.Auto
        | "empty" -> Baseline.Empty
        | _ -> if raw.StartsWith "@" then Baseline.FromTarget (raw.Substring 1) else Baseline.FromModel raw

    /// Resolve the source of B (THE_CLI.md §3): `--config` (the unified config
    /// carrying model + overlays) wins, then `--model` (a bare authored model),
    /// then `projection.json`'s `model` field; absent all three, Unspecified
    /// (the executor refuses with a named message naming the three ways to set it).
    let private resolveModel (cfg: TargetConfig) (argv: string list) : ModelSource =
        match readFlag "--config" argv with
        | Some c -> ModelSource.ConfigFile c
        | None ->
            match readFlag "--model" argv with
            | Some m -> ModelSource.ModelFile m
            | None ->
                match cfg.Model with
                | Some m -> ModelSource.ModelFile m
                | None -> ModelSource.Unspecified

    /// Build a `project` `MovementSpec` from the `--to` value and modifier
    /// flags, applying target defaults beneath CLI-set values.
    let buildProject (cfg: TargetConfig) (argv: string list) : Result<MovementSpec> =
        match readFlag "--to" argv with
        | None -> Result.failureOf (err "cli.project.toMissing" "project requires --to <destination>.")
        | Some toRaw ->
            match resolveTarget cfg toRaw with
            | Error e -> Result.failure e
            | Ok resolved ->
                let baseSpec = MovementSpec.forDestination resolved.Destination
                let scopeFlag = readFlag "--scope" argv
                let strategyFlag = readFlag "--how" argv
                let shapeR =
                    match readFlag "--shape" argv with
                    | None -> Result.success baseSpec.Shape
                    | Some s -> parseShape s
                let scopeR =
                    match scopeFlag with
                    | None -> Result.success baseSpec.Scope
                    | Some s -> parseScopeFlag s
                let strategyR =
                    match strategyFlag with
                    | None -> Result.success baseSpec.Strategy
                    | Some s -> parseStrategyFlag s
                let errors =
                    (match shapeR with Error e -> e | _ -> [])
                    @ (match scopeR with Error e -> e | _ -> [])
                    @ (match strategyR with Error e -> e | _ -> [])
                if not (List.isEmpty errors) then Result.failure errors
                else
                    let data =
                        match readFlag "--data" argv with
                        | None -> baseSpec.Data
                        | Some d -> parseData d
                    let baseline =
                        match readFlag "--from" argv with
                        | None -> baseSpec.Baseline
                        | Some f -> parseBaseline f
                    let spec =
                        { baseSpec with
                            Model = resolveModel cfg argv
                            Scope = (match scopeR with Ok v -> v | _ -> baseSpec.Scope)
                            Strategy = (match strategyR with Ok v -> v | _ -> baseSpec.Strategy)
                            Shape = (match shapeR with Ok v -> v | _ -> baseSpec.Shape)
                            Data = data
                            Baseline = baseline
                            Rekey = readFlag "--rekey" argv
                            Reconcile = readMany "--reconcile" argv
                            AllowDrops = hasFlag "--allow-drops" argv
                            AllowCdc = hasFlag "--allow-cdc" argv
                            Store = (match readFlag "--store" argv with Some s -> Some s | None -> resolved.Store)
                            Env = readFlag "--env" argv
                            Commit = hasFlag "--go" argv }
                    Result.success (withTargetDefaults resolved (Option.isSome scopeFlag) (Option.isSome strategyFlag) spec)

    /// Map an argv to an `Intent` (THE_CLI.md §2). `project` resolves to a
    /// full `MovementSpec`; `check` / `explain` / `seal` capture their tail
    /// for their build slices. Pure; the engine execution + the global-flag
    /// strip are the wiring slice.
    let parse (cfg: TargetConfig) (argv: string list) : Result<Intent> =
        match argv with
        | "project" :: rest ->
            match buildProject cfg rest with
            | Ok spec -> Result.success (Intent.Project spec)
            | Error e -> Result.failure e
        | "check" :: rest   -> Result.success (Intent.Check rest)
        | "explain" :: rest -> Result.success (Intent.Explain rest)
        | "seal" :: rest    -> Result.success (Intent.Seal rest)
        | verb :: _ ->
            Result.failureOf (err "cli.verb.unknown" (sprintf "unknown verb '%s'; expected project | check | explain | seal." verb))
        | [] ->
            Result.failureOf (err "cli.verb.missing" "no verb given; expected project | check | explain | seal.")

    // --- the pure project routing (THE_CLI fidelity #1) --------------------

    /// Reconstruct the out-of-band connection spec from a resolved ref.
    let connSpecOf (r: ConnectionRef) : string =
        match r with
        | ConnectionRef.EnvVar n -> "env:" + n
        | ConnectionRef.File p   -> "file:" + p

    let private noModel = "no model — pass --model <model.json>, --config <config.json>, or set \"model\" in projection.json."

    /// Notes for axes the current engine does not yet honor — surfaced, never
    /// silently dropped (fidelity #2; THE_VOICE no-silent-drop).
    let unhonoredNotes (spec: MovementSpec) : string list =
        [ // --scope is honored for live destinations (data→transfer, schema→migrate);
          // for folder/docker the bundle carries all legs.
          match spec.Scope, spec.Destination with
          | (Scope.Schema | Scope.Data), (Destination.Folder _ | Destination.Docker) ->
              "--scope accepted; the file/docker bundle carries all legs (all applied)."
          | _ -> ()
          if spec.Strategy <> Strategy.Merge then
              "--how accepted; the engine uses its default realization (merge applied)."
          match spec.Baseline with
          | Baseline.Auto -> ()
          | _ -> "--from accepted; the engine reads the prior state automatically (auto applied)."
          match spec.Data with
          | DataOrigin.Synthetic -> "--data synthetic accepted; synthetic generation is pending (model data applied)."
          | DataOrigin.NoData    -> "--data none accepted; the engine emits model data (model data applied)."
          | _ -> () ]

    /// Route a `project` `MovementSpec` to its engine face — purely. The runner
    /// (`runProjectPlan`) matches on the result, so the surface→engine mapping
    /// is totality-tested rather than trusted.
    let planProject (cfg: TargetConfig) (spec: MovementSpec) : ExecutionPlan =
        let dataConn (alias: string) : Result<string> =
            match resolveTarget cfg alias with
            | Ok rt ->
                match rt.Destination with
                | Destination.Live r -> Result.success (connSpecOf r)
                | _ -> Result.failureOf (err "cli.project.dataNotLive" (sprintf "--data %s: a data source must be a live target." alias))
            | Error es -> Result.failure es
        let action =
            match spec.Destination with
            | Destination.Folder dir ->
                match spec.Model with
                | ModelSource.ConfigFile c -> PlanAction.PublishBundle (c, dir)
                | ModelSource.ModelFile m ->
                    match spec.Shape with
                    | Shape.Skeleton            -> PlanAction.EmitSkeleton (m, dir)
                    | Shape.Bundle | Shape.Ssdt -> PlanAction.EmitBundle (m, dir)
                | ModelSource.Unspecified -> PlanAction.Refused (1, "projection project: " + noModel)
            | Destination.Docker ->
                match spec.Model with
                | ModelSource.Unspecified -> PlanAction.Refused (1, "projection project --to docker: " + noModel)
                | m -> PlanAction.DeployDocker m
            | Destination.Live connRef ->
                let conn = connSpecOf connRef
                let schemaOnly = (spec.Scope = Scope.Schema)
                let dataOnly = (spec.Scope = Scope.Data)
                if not spec.Commit then
                    match spec.Data with
                    | DataOrigin.FromTarget alias when not schemaOnly ->
                        match dataConn alias with
                        | Ok src -> PlanAction.PreviewData (src, conn)
                        | Error es -> PlanAction.Refused (6, "projection project: " + (es |> List.map (fun e -> e.Message) |> String.concat "; "))
                    | _ ->
                        match spec.Model with
                        | ModelSource.Unspecified -> PlanAction.Refused (1, "projection project: " + noModel)
                        | m -> PlanAction.PreviewSchema (m, conn)
                else
                    match spec.Data with
                    | DataOrigin.FromTarget alias when not schemaOnly ->
                        match dataConn alias with
                        | Error es -> PlanAction.Refused (6, "projection project: " + (es |> List.map (fun e -> e.Message) |> String.concat "; "))
                        | Ok src ->
                            if dataOnly then PlanAction.TransferData (src, conn)
                            else
                                match spec.Model with
                                | ModelSource.Unspecified -> PlanAction.Refused (1, "projection project: " + noModel)
                                | m -> PlanAction.MigrateWithData (m, conn, src)
                    | _ when dataOnly ->
                        PlanAction.Refused (2, "projection project --scope data: a DML-only load needs --data <target>.")
                    | _ ->
                        match spec.Model with
                        | ModelSource.ConfigFile c -> PlanAction.PublishAndLoad (c, conn)
                        | ModelSource.ModelFile _  -> PlanAction.Migrate (spec.Model, conn)
                        | ModelSource.Unspecified  -> PlanAction.Refused (1, "projection project: " + noModel)
        { Notes = unhonoredNotes spec; Action = action }
