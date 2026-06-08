namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: function-local accumulators while parsing the JSON
//   projection-config DOM into the immutable typed record; the mutation is
//   sealed at each parse function's exit (mirrors Config.fs).

open System
open System.IO
open System.Text.Json
open Projection.Core

// THE_CLI.md (2026-06-08) — the `projection.json` two-layer config
// (`environments` + `flows`) and the `projection <flow>` dispatch. D9 holds:
// an environment's connection is a *reference* (`env:` / `file:`), never a
// literal connection string. The prior `targets` block + `project --to`
// surface were removed at slice F5 — flows are the only entry.

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
        /// The durable timeline (the episode store) this place accumulates;
        /// `seal` records into it and `report` diffs against it (THE_CLI.md §8).
        Store  : string option
    }

// `FlowSource`, `Flow`, and `FlowRunOpts` live in MovementSpec.fs (the types
// file) — `Intent.Flow` carries them, so they precede it in compile order.

/// The parsed `projection.json`: named environments (places) and flows
/// (source→target Move recipes), the default authored model (so a flow needs
/// no model path), plus a global defaults block.
type ProjectionConfig =
    {
        /// THE_CLI.md §4.1 — named places with access/grant.
        Environments : Map<string, Environment>
        /// THE_CLI.md §4.2 — named source→target Move recipes.
        Flows        : Map<string, Flow>
        Model        : string option
        Defaults     : Map<string, string>
    }

[<RequireQualifiedAccess>]
module ProjectionConfig =

    let empty : ProjectionConfig =
        { Environments = Map.empty; Flows = Map.empty; Model = None; Defaults = Map.empty }

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
                let store = getString el "store"
                match getString el "grant" with
                | None -> Result.success { Name = name; Access = access; Grant = None; Store = store }
                | Some g ->
                    match parseGrant name g with
                    | Ok grant -> Result.success { Name = name; Access = access; Grant = Some grant; Store = store }
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

    /// Parse the `projection.json` document text into a `ProjectionConfig`.
    /// Aggregates every per-environment / per-flow error so the operator
    /// sees them all at once.
    let parse (json: string) : Result<ProjectionConfig> =
        if String.IsNullOrWhiteSpace json then Result.success empty
        else
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                Result.failureOf (err "cli.config.shape" "projection.json root must be a JSON object.")
            else
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
                    (envResults |> List.collect (function Error e -> e | Ok _ -> []))
                    @ (flowResults |> List.collect (function Error e -> e | Ok _ -> []))
                if not (List.isEmpty errors) then Result.failure errors
                else
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
                        { Environments = environments; Flows = flows
                          Model = getString root "model"; Defaults = defaults }
        with ex ->
            Result.failureOf (err "cli.config.parseFailed" (sprintf "projection.json did not parse: %s" ex.Message))

    /// Read and parse `projection.json` from disk; an absent file is the
    /// empty config (configuration is opt-in, not required).
    let fromFile (path: string) : Result<ProjectionConfig> =
        if not (File.Exists path) then Result.success empty
        else
            try parse (File.ReadAllText path)
            with ex -> Result.failureOf (err "cli.config.readFailed" (sprintf "could not read '%s': %s" path ex.Message))

[<RequireQualifiedAccess>]
module Command =

    let private err (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    /// Reconstruct the out-of-band connection spec from a resolved ref.
    let connSpecOf (r: ConnectionRef) : string =
        match r with
        | ConnectionRef.EnvVar n -> "env:" + n
        | ConnectionRef.File p   -> "file:" + p

    /// Resolve a live-connection reference: a scheme-prefixed ref (env:/file:)
    /// or a named `direct` environment → its out-of-band connection spec.
    let private resolveLiveConn (cfg: ProjectionConfig) (raw: string) : Result<string> =
        if raw.StartsWith "env:" || raw.StartsWith "file:" then
            match TransferSpec.parseConnectionSpec raw with
            | Ok r    -> Result.success (connSpecOf r)
            | Error e -> Result.failure e
        else
            match Map.tryFind raw cfg.Environments with
            | Some env ->
                match env.Access with
                | Access.Direct r -> Result.success (connSpecOf r)
                | _ -> Result.failureOf (err "cli.env.notLive" (sprintf "environment '%s' is not access:direct (no live connection)." raw))
            | None ->
                let known = cfg.Environments |> Map.toList |> List.map fst |> String.concat ", "
                let suffix = if known = "" then "no environments configured." else sprintf "known environments: %s." known
                Result.failureOf (err "cli.env.unknown" (sprintf "unknown environment '%s'; %s" raw suffix))

    let private flagValue (args: string list) (flag: string) : string option =
        let arr = List.toArray args
        arr |> Array.tryFindIndex ((=) flag) |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)

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

    let private noModel = "no model — set \"model\" in projection.json."

    /// Notes for spec axes the current engine does not yet honor — surfaced,
    /// never silently dropped (THE_VOICE no-silent-drop; THE_CLI.md §12).
    let unhonoredNotes (spec: MovementSpec) : string list =
        [ // Scope is honored for live destinations (data→transfer, schema→migrate);
          // a file/docker bundle carries all legs regardless.
          match spec.Scope, spec.Destination with
          | (Scope.Schema | Scope.Data), (Destination.Folder _ | Destination.Docker) ->
              "scope accepted; the file/docker bundle carries all legs (all applied)."
          | _ -> ()
          match spec.Baseline with
          | Baseline.Auto -> ()
          | _ -> "baseline accepted; the engine reads the prior state automatically (auto applied)."
          match spec.Data with
          | DataOrigin.Synthetic -> "synthetic data accepted; synthetic generation is pending (model data applied)."
          | DataOrigin.NoData    -> "data:none accepted; the engine emits model data (model data applied)."
          | _ -> () ]

    /// `--fresh` → the data-plane `EmissionMode`: merge is the incremental MERGE
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

    // --- the pure movement routing (the surface→engine map) ----------------

    /// Route a resolved `MovementSpec` to its engine face — purely. A flow
    /// resolves to one of these specs; the totality test sweeps the space.
    let planMovement (cfg: ProjectionConfig) (spec: MovementSpec) : ExecutionPlan =
        let opts = optsOf spec
        let modelMissing prefix = PlanAction.Refused (1, err "cli.move.modelMissing" (prefix + noModel))
        let dataConn (alias: string) : Result<string> = resolveLiveConn cfg alias
        let action =
            match spec.Destination with
            | Destination.Folder dir ->
                match spec.Model with
                | ModelSource.ConfigFile c -> PlanAction.PublishBundle (c, dir, spec.Store, spec.Env)
                | ModelSource.ModelFile m ->
                    match spec.Shape with
                    | Shape.Skeleton            -> PlanAction.EmitSkeleton (m, dir)
                    | Shape.Bundle | Shape.Ssdt -> PlanAction.EmitBundle (m, dir)
                | ModelSource.Unspecified -> modelMissing "projection: "
            | Destination.Docker ->
                match spec.Model with
                | ModelSource.Unspecified -> modelMissing "projection (docker): "
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
                        | ModelSource.Unspecified -> modelMissing "projection: "
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
                                | ModelSource.Unspecified -> modelMissing "projection: "
                                | m -> PlanAction.MigrateWithData (m, conn, src, opts)
                    | _ when dataOnly ->
                        PlanAction.Refused (2, err "cli.move.scopeDataNoSource" "a DML-only load needs a data source (a flow whose `from` is a live environment).")
                    | _ ->
                        match spec.Model with
                        | ModelSource.ConfigFile c -> PlanAction.PublishAndLoad (c, conn, spec.Store, spec.Env)
                        | ModelSource.ModelFile _  -> PlanAction.Migrate (spec.Model, conn, opts)
                        | ModelSource.Unspecified  -> modelMissing "projection: "
        // The wipe-and-load strategy is honored only on the pure-transfer data
        // load (→ EmissionMode); any other action keeps the incremental MERGE,
        // so note it (no silent drop).
        let freshNote =
            match spec.Strategy, action with
            | Strategy.Merge, _ -> []
            | _, PlanAction.Transfer _ -> []
            | _ -> [ "--fresh accepted; this action has no selectable data-load strategy (incremental applied)." ]
        { Notes = unhonoredNotes spec @ freshNote; Action = action }

    /// Route a `check` verb tail to its proof-plane action — purely.
    let planCheck (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        let connOf (raw: string) : Result<string> = resolveLiveConn cfg raw
        let action =
            match args with
            | "drift" :: _ ->
                match valueOf "--model", valueOf "--to" with
                | Some m, Some toRaw -> (match connOf toRaw with Ok c -> PlanAction.CheckDrift (m, c) | Error es -> PlanAction.Refused (6, List.head es))
                | _ -> PlanAction.Refused (2, err "cli.check.driftArgs" "projection check drift: requires --model <model.json> --to <environment>.")
            | "data" :: _ ->
                match valueOf "--before", valueOf "--after" with
                | Some b, Some a -> (match connOf b, connOf a with | Ok bc, Ok ac -> PlanAction.CheckData (bc, ac) | (Error es, _) | (_, Error es) -> PlanAction.Refused (6, List.head es))
                | _ -> PlanAction.Refused (2, err "cli.check.dataArgs" "projection check data: requires --before <environment> --after <environment>.")
            | "ready" :: _ -> PlanAction.CheckReady
            | _ ->
                match args |> List.tryFind (fun a -> not (a.StartsWith "--") && a <> "fidelity") with
                | Some path -> PlanAction.CheckCanary (path, List.contains "--cdc-silence" args)
                | None -> PlanAction.Refused (1, err "cli.check.noDdl" "projection check: the fidelity canary needs a source DDL path (check <source.sql>).")
        { Notes = []; Action = action }

    /// Route an `explain` verb tail to its understanding action — purely.
    /// `explain <flow>` is the live preview: what publishing the flow would
    /// change against its target's last sealed episode (B = the flow's model,
    /// A_prior = the target store) — the preview sibling to `report`'s history.
    let planExplain (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
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
            | sub :: _ when Map.containsKey sub cfg.Flows ->
                // explain <flow>: B = the flow's model, A_prior = the target store.
                let flow = Map.find sub cfg.Flows
                let decl = parseLossDeclaration args
                match cfg.Model, (Map.tryFind flow.To cfg.Environments |> Option.bind (fun e -> e.Store)) with
                | Some model, Some store -> PlanAction.ExplainMigrateFromStore (store, model, decl)
                | None, _ -> PlanAction.Refused (1, err "cli.explain.flowNoModel" (sprintf "explain '%s': no model — set \"model\" in projection.json." sub))
                | _, None -> PlanAction.Refused (6, err "cli.explain.flowNoStore" (sprintf "explain '%s': target environment '%s' has no `store` to diff against (publish + seal once first)." sub flow.To))
            | _ -> PlanAction.Refused (2, err "cli.explain.unknown" "projection explain: expected <flow> | diff | policy | node | suggest | registry | migrate.")
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
    /// flows on as the transfer source `planMovement` resolves.
    let private dataOriginOfSource (cfg: ProjectionConfig) (source: FlowSource) : Result<DataOrigin> =
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
    let resolveFlowSpec (cfg: ProjectionConfig) (flow: Flow) (opts: FlowRunOpts) : Result<MovementSpec> =
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
                        // The target's durable timeline: a live --go records an
                        // episode into it (which `report` later diffs). F4.
                        Store    = toEnv.Store
                        Commit   = opts.Go }

    /// Route a named flow to its `ExecutionPlan`. The grant gate refuses a
    /// schema-bearing flow (content from the authored model) against a
    /// data-only target — a type mismatch, refused loud (exit 9), never a
    /// silent scope-narrowing. Otherwise the resolved spec rides the
    /// totality-tested `planMovement` routing.
    let planFlow (cfg: ProjectionConfig) (flow: Flow) (opts: FlowRunOpts) : ExecutionPlan =
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
                    let plan = planMovement cfg spec
                    let tableNote =
                        if List.isEmpty flow.Tables then []
                        else [ sprintf "flow tables (%s) accepted; subset selection is pending (all tables applied)." (String.concat ", " flow.Tables) ]
                    { plan with Notes = plan.Notes @ tableNote }

    /// Route a `report` verb tail (THE_CLI.md §8): `report <flow>` reads the
    /// flow's target durable timeline (its `store`) and renders the recorded
    /// ChangeManifest series — what changed since the last sealed episode. An
    /// explicit `--store <path>` overrides; a target with no store is refused
    /// (named, never silent). The bundle itself is built by the runner.
    let planReport (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        let storeOf () : Result<string> =
            match flagValue args "--store" with
            | Some s -> Result.success s
            | None ->
                match args |> List.tryFind (fun a -> not (a.StartsWith "--")) with
                | None -> Result.failureOf (err "cli.report.noFlow" "projection report: name a flow (report <flow>) or pass --store <path>.")
                | Some flowName ->
                    match Map.tryFind flowName cfg.Flows with
                    | None -> Result.failureOf (err "cli.report.unknownFlow" (sprintf "report: unknown flow '%s'." flowName))
                    | Some flow ->
                        match Map.tryFind flow.To cfg.Environments |> Option.bind (fun e -> e.Store) with
                        | Some store -> Result.success store
                        | None -> Result.failureOf (err "cli.report.noStore" (sprintf "report '%s': target environment '%s' has no `store` (the durable timeline); add one or pass --store <path>." flowName flow.To))
        match storeOf () with
        | Ok store -> { Notes = []; Action = PlanAction.ReportBundle store }
        | Error es -> { Notes = []; Action = PlanAction.Refused (6, List.head es) }

    /// The closed secondary-verb set (THE_CLI.md §3). A first token outside
    /// this set is read as a flow name; an unknown one is refused, naming both.
    let private secondaryVerbs = set [ "check"; "explain"; "seal"; "report"; "init" ]

    /// Map an argv to an `Intent` (THE_CLI.md §3): the daily surface
    /// `projection <flow> [--go] [--fresh] [--allow-drops]` (the verb is
    /// implied), or one of the closed secondary verbs. Pure; the engine
    /// execution + the global-flag strip are the wiring slice.
    let parse (cfg: ProjectionConfig) (argv: string list) : Result<Intent> =
        match argv with
        | "check" :: rest   -> Result.success (Intent.Check rest)
        | "explain" :: rest -> Result.success (Intent.Explain rest)
        | "seal" :: rest    -> Result.success (Intent.Seal rest)
        | "report" :: rest  -> Result.success (Intent.Report rest)
        | first :: rest when Map.containsKey first cfg.Flows ->
            let opts =
                { Go         = List.contains "--go" rest
                  Fresh      = List.contains "--fresh" rest
                  AllowDrops = List.contains "--allow-drops" rest }
            Result.success (Intent.Flow (Map.find first cfg.Flows, opts))
        | first :: _ when secondaryVerbs.Contains first ->
            // a known verb with a malformed tail falls through its own branch;
            // this arm is unreachable for those, kept total for the type.
            Result.failureOf (err "cli.verb.unknown" (sprintf "verb '%s' is not yet routed." first))
        | first :: _ ->
            let flows = cfg.Flows |> Map.toList |> List.map fst |> String.concat ", "
            let suffix = if flows = "" then "no flows configured." else sprintf "known flows: %s." flows
            Result.failureOf (err "cli.verb.unknown" (sprintf "unknown flow or verb '%s'; %s" first suffix))
        | [] ->
            Result.failureOf (err "cli.verb.missing" "no flow or verb given; expected <flow> | check | explain | seal | report.")

    /// The one pure routing for the whole surface — every `Intent` to its
    /// `ExecutionPlan`. The runner executes it; the totality test sweeps it.
    let plan (cfg: ProjectionConfig) (intent: Intent) : ExecutionPlan =
        match intent with
        | Intent.Flow (flow, opts) -> planFlow cfg flow opts
        | Intent.Check args        -> planCheck cfg args
        | Intent.Explain args      -> planExplain cfg args
        | Intent.Seal args         -> planSeal args
        | Intent.Report args       -> planReport cfg args
