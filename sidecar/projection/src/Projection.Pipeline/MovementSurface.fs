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
//
// THE_CLI.md (2026-06-08) re-grounds the surface onto two config layers —
// `environments` (places, with `access`/`grant`) and `flows` (named Move
// recipes). Slice F1 adds those types + their parse, additively, alongside
// the existing `targets` block (deprecated in F5).

/// Permission facet — how a target environment is *reached* (THE_CLI.md §6).
/// `Bundle` writes only files (an SSDT bundle for Octopus) and needs no live
/// gate; `Direct` is a live connection; `Docker` is the ephemeral one-touch.
[<RequireQualifiedAccess>]
type Access =
    | Bundle of out: string
    | Direct of ConnectionRef
    | Docker

/// Permission facet — what may *change* at a target (THE_CLI.md §6). A refusal
/// gate, not a setting: a schema-changing flow against `DataOnly` is a type
/// mismatch (resolved at flow time). An environment used only as a source
/// (read) carries no grant.
[<RequireQualifiedAccess>]
type Grant =
    | SchemaAndData
    | DataOnly

/// A named place (THE_CLI.md §4.1): its reach (`Access`) and, for a target,
/// its permission (`Grant`). D9 holds — a `Direct`/`Bundle` address is a
/// reference or a folder, never an inline secret.
type Environment =
    {
        Name   : string
        Access : Access
        Grant  : Grant option
    }

/// Where a flow's content originates (THE_CLI.md §4.2): another environment
/// (the cross-substrate Move), the authored model's own data, profiled
/// synthetic data, or no data (schema only).
[<RequireQualifiedAccess>]
type FlowSource =
    | Env of env: string
    | Model
    | Synthetic of profile: string option
    | NoData

/// A named movement (THE_CLI.md §4.2): a `Move` from a source to a target
/// environment, with optional specialization (Reidentify re-key; a declared
/// table subset).
type Flow =
    {
        Name   : string
        From   : FlowSource
        To     : string
        Rekey  : string option
        Tables : string list
    }

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
        Targets      : Map<string, Target>
        /// THE_CLI.md §4.1 — named places with access/grant (the re-grounded surface).
        Environments : Map<string, Environment>
        /// THE_CLI.md §4.2 — named source→target Move recipes.
        Flows        : Map<string, Flow>
        Model        : string option
        Defaults     : Map<string, string>
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

    let empty : TargetConfig =
        { Targets = Map.empty; Environments = Map.empty; Flows = Map.empty; Model = None; Defaults = Map.empty }

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

    // --- environments / flows (THE_CLI.md 2026-06-08; slice F1) -----------

    /// The reach facet: `bundle` needs an `out` folder; `direct` needs a
    /// D9-safe `conn` reference; `docker` is bare.
    let private parseAccess (envName: string) (el: JsonElement) : Result<Access> =
        match getString el "access" with
        | None ->
            Result.failureOf (err "cli.config.envAccessMissing" (sprintf "environment '%s' sets no 'access' (bundle | direct | docker)." envName))
        | Some a ->
            match a.ToLowerInvariant() with
            | "bundle" ->
                match getString el "out" with
                | Some out -> Result.success (Access.Bundle out)
                | None -> Result.failureOf (err "cli.config.envBundleNoOut" (sprintf "environment '%s' is access:bundle but sets no 'out' folder." envName))
            | "direct" ->
                match getString el "conn" with
                | None -> Result.failureOf (err "cli.config.envDirectNoConn" (sprintf "environment '%s' is access:direct but sets no 'conn'." envName))
                | Some conn ->
                    if looksLikeSecret conn then
                        Result.failureOf (err "cli.config.envSecretInline" (sprintf "environment '%s' conn looks like an inline secret; use env:<VAR> or file:<path> (D9)." envName))
                    else
                        match TransferSpec.parseConnectionSpec conn with
                        | Ok r    -> Result.success (Access.Direct r)
                        | Error e -> Result.failure e
            | "docker" -> Result.success Access.Docker
            | other -> Result.failureOf (err "cli.config.envAccessUnknown" (sprintf "environment '%s' access '%s' is not bundle | direct | docker." envName other))

    /// The permission facet (a refusal gate): schema+data | data.
    let private parseGrant (envName: string) (raw: string) : Result<Grant> =
        match raw.ToLowerInvariant() with
        | "schema+data" | "schemaanddata" | "schema-and-data" -> Result.success Grant.SchemaAndData
        | "data" | "dataonly" | "data-only"                   -> Result.success Grant.DataOnly
        | other -> Result.failureOf (err "cli.config.envGrantUnknown" (sprintf "environment '%s' grant '%s' is not schema+data | data." envName other))

    let private parseEnvironment (name: string) (el: JsonElement) : Result<Environment> =
        if el.ValueKind <> JsonValueKind.Object then
            Result.failureOf (err "cli.config.envShape" (sprintf "environment '%s' must be a JSON object." name))
        else
            match parseAccess name el with
            | Error e -> Result.failure e
            | Ok access ->
                match getString el "grant" with
                | None -> Result.success { Name = name; Access = access; Grant = None }
                | Some g ->
                    match parseGrant name g with
                    | Ok grant -> Result.success { Name = name; Access = access; Grant = Some grant }
                    | Error e  -> Result.failure e

    /// The content origin: `from` names an environment (the Move), or one of
    /// the keywords `model` / `synthetic` (with optional `profile`) / `none`.
    let private parseFlowSource (el: JsonElement) : FlowSource =
        match getString el "from" with
        | None -> FlowSource.Model
        | Some f ->
            match f.ToLowerInvariant() with
            | "model"     -> FlowSource.Model
            | "synthetic" -> FlowSource.Synthetic (getString el "profile")
            | "none"      -> FlowSource.NoData
            | _           -> FlowSource.Env f

    let private parseFlow (name: string) (el: JsonElement) : Result<Flow> =
        if el.ValueKind <> JsonValueKind.Object then
            Result.failureOf (err "cli.config.flowShape" (sprintf "flow '%s' must be a JSON object." name))
        else
            match getString el "to" with
            | None -> Result.failureOf (err "cli.config.flowNoTo" (sprintf "flow '%s' sets no 'to' target environment." name))
            | Some toEnv ->
                let tables =
                    match el.TryGetProperty "tables" with
                    | true, t when t.ValueKind = JsonValueKind.Array ->
                        [ for v in t.EnumerateArray() do
                            if v.ValueKind = JsonValueKind.String then
                                match Option.ofObj (v.GetString()) with
                                | Some s when not (String.IsNullOrWhiteSpace s) -> yield s.Trim()
                                | _ -> () ]
                    | _ -> []
                Result.success
                    { Name = name; From = parseFlowSource el; To = toEnv; Rekey = getString el "rekey"; Tables = tables }

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
                let envResults =
                    match root.TryGetProperty "environments" with
                    | true, e when e.ValueKind = JsonValueKind.Object ->
                        [ for p in e.EnumerateObject() -> parseEnvironment p.Name p.Value ]
                    | _ -> []
                let flowResults =
                    match root.TryGetProperty "flows" with
                    | true, f when f.ValueKind = JsonValueKind.Object ->
                        [ for p in f.EnumerateObject() -> parseFlow p.Name p.Value ]
                    | _ -> []
                let errors =
                    (targetResults |> List.collect (function Error e -> e | Ok _ -> []))
                    @ (envResults |> List.collect (function Error e -> e | Ok _ -> []))
                    @ (flowResults |> List.collect (function Error e -> e | Ok _ -> []))
                if not (List.isEmpty errors) then Result.failure errors
                else
                    let targets =
                        targetResults
                        |> List.choose (function Ok t -> Some (t.Name, t) | _ -> None)
                        |> Map.ofList
                    let environments =
                        envResults |> List.choose (function Ok e -> Some (e.Name, e) | _ -> None) |> Map.ofList
                    let flows =
                        flowResults |> List.choose (function Ok f -> Some (f.Name, f) | _ -> None) |> Map.ofList
                    let defaults =
                        match root.TryGetProperty "defaults" with
                        | true, d when d.ValueKind = JsonValueKind.Object ->
                            [ for p in d.EnumerateObject() do
                                match getString d p.Name with
                                | Some v -> yield (p.Name, v)
                                | None -> () ]
                            |> Map.ofList
                        | _ -> Map.empty
                    Result.success
                        { Targets = targets; Environments = environments; Flows = flows
                          Model = getString root "model"; Defaults = defaults }
        with ex ->
            Result.failureOf (err "cli.config.parseFailed" (sprintf "projection.json did not parse: %s" ex.Message))

    /// Read and parse `projection.json` from disk; an absent file is the
    /// empty config (aliasing is opt-in, not required).
    let fromFile (path: string) : Result<TargetConfig> =
        if not (File.Exists path) then Result.success empty
        else
            try parse (File.ReadAllText path)
            with ex -> Result.failureOf (err "cli.config.readFailed" (sprintf "could not read '%s': %s" path ex.Message))

/// The per-run intent that finishes a resolved flow (THE_CLI.md §3) — the
/// only words that vary at the moment of action and never live in config.
type FlowRunOpts =
    {
        Go         : bool
        Fresh      : bool
        AllowDrops : bool
    }

[<RequireQualifiedAccess>]
module Command =

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
          match spec.Baseline with
          | Baseline.Auto -> ()
          | _ -> "--from accepted; the engine reads the prior state automatically (auto applied)."
          match spec.Data with
          | DataOrigin.Synthetic -> "--data synthetic accepted; synthetic generation is pending (model data applied)."
          | DataOrigin.NoData    -> "--data none accepted; the engine emits model data (model data applied)."
          | _ -> () ]

    /// `--how` → the data-plane `EmissionMode`: merge is the incremental MERGE
    /// (the norm-minimal default); replace / fresh are the wipe-and-load.
    let private emissionOf (strategy: Strategy) : EmissionMode =
        match strategy with
        | Strategy.Merge -> EmissionMode.Incremental
        | Strategy.Replace | Strategy.Fresh -> EmissionMode.WipeAndLoad

    let private optsOf (spec: MovementSpec) : LoadOpts =
        { Declaration = (if spec.AllowDrops then DeclareAll else DeclareNone)
          Emission    = emissionOf spec.Strategy
          Reconcile   = spec.Reconcile
          Rekey       = spec.Rekey
          AllowCdc    = spec.AllowCdc
          Store       = spec.Store
          Env         = spec.Env }

    /// `--allow-drops` (accept all) / repeated `--declare-drop <token>` (accept
    /// each) / else refuse all destructive removals — the loss-declaration gate,
    /// parsed purely from the verb tail.
    let parseLossDeclaration (args: string list) : LossDeclaration =
        if List.contains "--allow-drops" args then DeclareAll
        else
            let arr = List.toArray args
            let tokens =
                arr |> Array.indexed
                |> Array.choose (fun (i, a) -> if a = "--declare-drop" && i + 1 < arr.Length then Some arr.[i + 1] else None)
                |> Array.toList
            match tokens with [] -> DeclareNone | _ -> DeclareThese (Set.ofList tokens)

    let private flagValue (args: string list) (flag: string) : string option =
        let arr = List.toArray args
        arr |> Array.tryFindIndex ((=) flag) |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)

    /// Route a `project` `MovementSpec` to its engine face — purely.
    let planProject (cfg: TargetConfig) (spec: MovementSpec) : ExecutionPlan =
        let opts = optsOf spec
        let modelMissing prefix = PlanAction.Refused (1, err "cli.project.modelMissing" (prefix + noModel))
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
                | ModelSource.ConfigFile c -> PlanAction.PublishBundle (c, dir, spec.Store, spec.Env)
                | ModelSource.ModelFile m ->
                    match spec.Shape with
                    | Shape.Skeleton            -> PlanAction.EmitSkeleton (m, dir)
                    | Shape.Bundle | Shape.Ssdt -> PlanAction.EmitBundle (m, dir)
                | ModelSource.Unspecified -> modelMissing "projection project: "
            | Destination.Docker ->
                match spec.Model with
                | ModelSource.Unspecified -> modelMissing "projection project --to docker: "
                | m -> PlanAction.DeployDocker m
            | Destination.Live connRef ->
                let conn = connSpecOf connRef
                let schemaOnly = (spec.Scope = Scope.Schema)
                let dataOnly = (spec.Scope = Scope.Data)
                if not spec.Commit then
                    match spec.Data with
                    | DataOrigin.FromTarget alias when not schemaOnly ->
                        match dataConn alias with
                        | Ok src -> PlanAction.Transfer (src, conn, opts, false)
                        | Error es -> PlanAction.Refused (6, List.head es)
                    | _ ->
                        match spec.Model with
                        | ModelSource.Unspecified -> modelMissing "projection project: "
                        | m -> PlanAction.PreviewSchema (m, conn, opts.Declaration)
                else
                    match spec.Data with
                    | DataOrigin.FromTarget alias when not schemaOnly ->
                        match dataConn alias with
                        | Error es -> PlanAction.Refused (6, List.head es)
                        | Ok src ->
                            if dataOnly then PlanAction.Transfer (src, conn, opts, true)
                            else
                                match spec.Model with
                                | ModelSource.Unspecified -> modelMissing "projection project: "
                                | m -> PlanAction.MigrateWithData (m, conn, src, opts)
                    | _ when dataOnly ->
                        PlanAction.Refused (2, err "cli.project.scopeDataNoSource" "projection project --scope data: a DML-only load needs --data <target>.")
                    | _ ->
                        match spec.Model with
                        | ModelSource.ConfigFile c -> PlanAction.PublishAndLoad (c, conn, spec.Store, spec.Env)
                        | ModelSource.ModelFile _  -> PlanAction.Migrate (spec.Model, conn, opts)
                        | ModelSource.Unspecified  -> modelMissing "projection project: "
        // --how is honored only on the pure-transfer data load (→ EmissionMode);
        // any other action ignores the strategy, so note it (no silent drop).
        let howNote =
            match spec.Strategy, action with
            | Strategy.Merge, _ -> []
            | _, PlanAction.Transfer _ -> []
            | _ -> [ "--how accepted; this action has no selectable data-load strategy (default applied)." ]
        { Notes = unhonoredNotes spec @ howNote; Action = action }

    /// Route a `check` verb tail to its proof-plane action — purely.
    let planCheck (cfg: TargetConfig) (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let connOf (raw: string) : Result<string> =
            match resolveTarget cfg raw with
            | Ok rt -> (match rt.Destination with Destination.Live r -> Result.success (connSpecOf r) | _ -> Result.failureOf (err "cli.check.notLive" (sprintf "'%s' is not a live target." raw)))
            | Error es -> Result.failure es
        let action =
            match args with
            | "drift" :: _ ->
                match valueOf "--model", valueOf "--to" with
                | Some m, Some toRaw -> (match connOf toRaw with Ok c -> PlanAction.CheckDrift (m, c) | Error es -> PlanAction.Refused (6, List.head es))
                | _ -> PlanAction.Refused (2, err "cli.check.driftArgs" "projection check drift: requires --model <model.json> --to <target>.")
            | "data" :: _ ->
                match valueOf "--before", valueOf "--after" with
                | Some b, Some a -> (match connOf b, connOf a with | Ok bc, Ok ac -> PlanAction.CheckData (bc, ac) | (Error es, _) | (_, Error es) -> PlanAction.Refused (6, List.head es))
                | _ -> PlanAction.Refused (2, err "cli.check.dataArgs" "projection check data: requires --before <target> --after <target>.")
            | "ready" :: _ -> PlanAction.CheckReady
            | _ ->
                match args |> List.tryFind (fun a -> not (a.StartsWith "--") && a <> "fidelity") with
                | Some path -> PlanAction.CheckCanary (path, List.contains "--cdc-silence" args)
                | None -> PlanAction.Refused (1, err "cli.check.noDdl" "projection check: the fidelity canary needs a source DDL path (check <source.sql>).")
        { Notes = []; Action = action }

    /// Route an `explain` verb tail to its understanding action — purely.
    let planExplain (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let depthOpt =
            match valueOf "--depth" with
            | Some "all" -> Some System.Int32.MaxValue
            | Some d -> (match System.Int32.TryParse d with | true, n -> Some (max 0 n) | _ -> None)
            | None -> None
        let action =
            match args with
            | "diff" :: a :: b :: _ -> PlanAction.ExplainDiff (a, b, (valueOf "--format" = Some "json"), depthOpt)
            | "policy" :: a :: b :: _ -> PlanAction.ExplainPolicy (a, b)
            | "node" :: c :: k :: _   -> PlanAction.ExplainNode (c, k)
            | "suggest" :: c :: _     -> PlanAction.ExplainSuggest (c, valueOf "--apply")
            | "registry" :: _         -> PlanAction.ExplainRegistry
            | "migrate" :: _ ->
                let decl = parseLossDeclaration args
                match valueOf "--to", valueOf "--from", valueOf "--store" with
                | Some toP, Some fromP, _    -> PlanAction.ExplainMigratePreview (fromP, toP, decl)
                | Some toP, None, Some store -> PlanAction.ExplainMigrateFromStore (store, toP, decl)
                | _ -> PlanAction.Refused (2, err "cli.explain.migrateArgs" "projection explain migrate: needs --to <modelB> with --from <modelA> or --store <lifecycle>.")
            | _ -> PlanAction.Refused (2, err "cli.explain.unknown" "projection explain: expected diff | policy | node | suggest | registry | migrate.")
        { Notes = []; Action = action }

    /// Route a `seal` verb tail to its provenance action — purely.
    let planSeal (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let action =
            match args with
            | "approve" :: version :: _ ->
                match valueOf "--approver" with
                | Some approver -> PlanAction.SealApprove (version, approver, valueOf "--rationale", valueOf "--store")
                | None -> PlanAction.Refused (2, err "cli.seal.approveArgs" "projection seal approve: requires --approver <name>.")
            | _ ->
                match valueOf "--store" with
                | Some store -> PlanAction.SealEject store
                | None -> PlanAction.Refused (2, err "cli.seal.ejectArgs" "projection seal: requires --store <path> (the durable timeline).")
        { Notes = []; Action = action }

    // --- flow resolution (THE_CLI.md 2026-06-08; slice F2) -----------------

    /// A `to` environment's reach → the `Destination` the engine lands at.
    let private destinationOfAccess (access: Access) : Destination =
        match access with
        | Access.Bundle out -> Destination.Folder out
        | Access.Direct r   -> Destination.Live r
        | Access.Docker     -> Destination.Docker

    /// A flow's `from` → the `DataOrigin`. A source environment must be
    /// `direct` (a live place to read rows from); the scheme-prefixed ref
    /// flows on as the transfer source `planProject` resolves.
    let private dataOriginOfSource (cfg: TargetConfig) (source: FlowSource) : Result<DataOrigin> =
        match source with
        | FlowSource.Model       -> Result.success DataOrigin.Model
        | FlowSource.NoData      -> Result.success DataOrigin.NoData
        | FlowSource.Synthetic _ -> Result.success DataOrigin.Synthetic
        | FlowSource.Env e ->
            match Map.tryFind e cfg.Environments with
            | None ->
                Result.failureOf (err "cli.flow.fromUnknown" (sprintf "flow source environment '%s' is not defined." e))
            | Some env ->
                match env.Access with
                | Access.Direct r -> Result.success (DataOrigin.FromTarget (connSpecOf r))
                | _ -> Result.failureOf (err "cli.flow.fromNotDirect" (sprintf "flow source '%s' must be access:direct (a live place to read rows from)." e))

    /// Resolve a named flow to a full `MovementSpec`, reading its `to`/`from`
    /// environments; the per-run intent finishes it. Pure; env-resolution
    /// failures are `Error` (the grant gate is a `planFlow` refusal).
    let resolveFlowSpec (cfg: TargetConfig) (flow: Flow) (opts: FlowRunOpts) : Result<MovementSpec> =
        match Map.tryFind flow.To cfg.Environments with
        | None ->
            Result.failureOf (err "cli.flow.toUnknown" (sprintf "flow '%s' target environment '%s' is not defined." flow.Name flow.To))
        | Some toEnv ->
            match dataOriginOfSource cfg flow.From with
            | Error es -> Result.failure es
            | Ok data ->
                let baseSpec = MovementSpec.forDestination (destinationOfAccess toEnv.Access)
                Result.success
                    { baseSpec with
                        Model    = (match cfg.Model with Some m -> ModelSource.ModelFile m | None -> ModelSource.Unspecified)
                        Scope    = (match toEnv.Grant with Some Grant.DataOnly -> Scope.Data | _ -> Scope.All)
                        Strategy = (if opts.Fresh then Strategy.Fresh else Strategy.Merge)
                        Data     = data
                        Baseline = (if opts.Fresh then Baseline.Empty else Baseline.Auto)
                        Rekey    = flow.Rekey
                        AllowDrops = opts.AllowDrops
                        Commit   = opts.Go }

    /// Route a named flow to its `ExecutionPlan`. The grant gate refuses a
    /// schema-bearing flow (content from the authored model) against a
    /// data-only target — a type mismatch, refused loud (exit 9), never a
    /// silent scope-narrowing. Otherwise the resolved spec rides the
    /// totality-tested `planProject` routing.
    let planFlow (cfg: TargetConfig) (flow: Flow) (opts: FlowRunOpts) : ExecutionPlan =
        match Map.tryFind flow.To cfg.Environments with
        | None ->
            { Notes = []
              Action = PlanAction.Refused (1, err "cli.flow.toUnknown" (sprintf "flow '%s' target environment '%s' is not defined." flow.Name flow.To)) }
        | Some toEnv ->
            match toEnv.Grant, flow.From with
            | Some Grant.DataOnly, FlowSource.Model ->
                { Notes = []
                  Action = PlanAction.Refused (9, err "cli.flow.grantSchemaRefused" (sprintf "flow '%s' publishes schema from the model, but target '%s' grants data only; the schema must already agree." flow.Name flow.To)) }
            | _ ->
                match resolveFlowSpec cfg flow opts with
                | Error es -> { Notes = []; Action = PlanAction.Refused (6, List.head es) }
                | Ok spec ->
                    let plan = planProject cfg spec
                    let tableNote =
                        if List.isEmpty flow.Tables then []
                        else [ sprintf "flow tables (%s) accepted; subset selection is pending (all tables applied)." (String.concat ", " flow.Tables) ]
                    { plan with Notes = plan.Notes @ tableNote }

    /// The one pure routing for the whole surface — every `Intent` to its
    /// `ExecutionPlan`. The runner executes it; the totality test sweeps it.
    let plan (cfg: TargetConfig) (intent: Intent) : ExecutionPlan =
        match intent with
        | Intent.Project spec -> planProject cfg spec
        | Intent.Check args   -> planCheck cfg args
        | Intent.Explain args -> planExplain args
        | Intent.Seal args    -> planSeal args
