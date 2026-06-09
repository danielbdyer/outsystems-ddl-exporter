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

/// Metadata facet — which *rendition* of the one authored model a place bears
/// (THE_CLI.md §4.1; THE_DATA_PRODUCERS §0/§4.6; DECISIONS 2026-06-09 item 5).
/// The estate hosts ONE `SsKey` model in two physical shapes: `Physical` is the
/// frozen OSUSR cloud rendition (A — the up-leg sink); `Logical` is the hosted
/// on-prem rendition (B — the migration team's load target, the legacy reverse
/// leg's source). The flag distinguishes a *peer* source (physical, the `golden`
/// cloud→cloud move) from a *legacy* source (logical, the `preview` B→A reverse
/// leg). It is env METADATA, not a refusal gate — it does not narrow `access` /
/// `grant`; it marks the rendition so the reverse-leg wiring (M3.b / LE-1) can
/// pick source=logical / sink=physical. Closed so a renderer is total over it.
[<RequireQualifiedAccess>]
type Rendition =
    | Physical
    | Logical

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
        /// Which rendition of the one authored model this place bears
        /// (`physical` = OSUSR cloud, A; `logical` = hosted on-prem, B). `None`
        /// = unspecified (the minimal non-breaking default — same-rendition
        /// moves, the established surface, never set it). Env metadata, not a
        /// gate. THE_CLI.md §4.1; THE_DATA_PRODUCERS §4.6.
        Rendition : Rendition option
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
        /// The authored `osm_model.json` file — the model **fallback** (kept
        /// for cutover safety, not retired).
        Model        : string option
        /// A live OSSYS connection (env/file ref) — the **primary** model
        /// source (V1-free: read OutSystems metadata directly → native SsKey).
        /// When set it wins over `Model`. See `ModelResolution`.
        ModelOssys   : string option
        Defaults     : Map<string, string>
        /// The model-shaping view of the SAME `projection.json`
        /// (`overrides`/`emission`/`policy`/`profiler`/`cache`/`typeMapping`/
        /// `output` and the canonical `model` object), parsed leniently so a
        /// movement-only file defaults every section. THE_CONFIG_CONTROL_PLANE
        /// §5 — one isomorphic surface behind two views. Nothing CONSUMES this
        /// yet at S1; S2 reads `Shaping.Model`; S3 threads it into emission.
        Shaping      : Config.Config
    }

[<RequireQualifiedAccess>]
module ProjectionConfig =

    let empty : ProjectionConfig =
        { Environments = Map.empty; Flows = Map.empty; Model = None; ModelOssys = None; Defaults = Map.empty
          Shaping = Config.defaultConfig }

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

    /// The rendition metadata facet: physical (OSUSR cloud, A) | logical
    /// (hosted on-prem, B). Absent = `None` (unspecified — the same-rendition
    /// default). Closed; an unknown value is a named refusal, never silently
    /// dropped.
    let private parseRendition (envName: string) (raw: string) : Result<Rendition> =
        match raw.ToLowerInvariant() with
        | "physical" -> Result.success Rendition.Physical
        | "logical"  -> Result.success Rendition.Logical
        | other -> Result.failureOf (err "cli.config.envRenditionUnknown" (sprintf "environment '%s' rendition '%s' is not physical | logical." envName other))

    let private parseEnvironment (name: string) (el: JsonElement) : Result<Environment> =
        if el.ValueKind <> JsonValueKind.Object then
            Result.failureOf (err "cli.config.envShape" (sprintf "environment '%s' must be a JSON object." name))
        else
            match parseAccess name el with
            | Error e -> Result.failure e
            | Ok access ->
                let store = getString el "store"
                let renditionR =
                    match getString el "rendition" with
                    | None   -> Result.success None
                    | Some r -> parseRendition name r |> Result.map Some
                match renditionR with
                | Error e -> Result.failure e
                | Ok rendition ->
                    match getString el "grant" with
                    | None -> Result.success { Name = name; Access = access; Grant = None; Store = store; Rendition = rendition }
                    | Some g ->
                        match parseGrant name g with
                        | Ok grant -> Result.success { Name = name; Access = access; Grant = Some grant; Store = store; Rendition = rendition }
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
                    // The shaping view of the SAME document, parsed leniently
                    // (a movement-only file defaults every shaping section).
                    // Any shaping error (D9 credential, type mismatch) surfaces
                    // here so the unified document is validated as one. Nothing
                    // consumes `Shaping` yet at S1.
                    match Config.parseLenient json with
                    | Error es -> Result.failure es
                    | Ok shaping ->
                    Result.success
                        { Environments = environments; Flows = flows
                          Model = getString root "model"
                          ModelOssys = getString root "modelOssys"
                          Defaults = defaults
                          Shaping = shaping }
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
          match spec.Data, spec.Destination with
          // Synthetic generation is honored only on a live data load; a
          // file/docker bundle carries model data.
          | DataOrigin.Synthetic _, (Destination.Folder _ | Destination.Docker) ->
              "synthetic data accepted; the file/docker bundle carries model data (model data applied)."
          | DataOrigin.NoData, _ -> "data:none accepted; the engine emits model data (model data applied)."
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
          Resumable   = spec.Resumable
          Store       = spec.Store
          Env         = spec.Env
          Tables      = spec.Tables }

    // --- the pure movement routing (the surface→engine map) ----------------

    /// Route a resolved `MovementSpec` to its engine face — purely. A flow
    /// resolves to one of these specs; the totality test sweeps the space.
    let planMovement (cfg: ProjectionConfig) (spec: MovementSpec) : ExecutionPlan =
        let opts = optsOf spec
        let modelMissing prefix = PlanAction.Refused (1, err "cli.move.modelMissing" (prefix + noModel))
        let dataConn (alias: string) : Result<string> = resolveLiveConn cfg alias
        // Live OSSYS (primary) when configured; the model file (fallback) is
        // optional then. `hasModel` is true when either source is available.
        let modelOssys = cfg.ModelOssys
        let hasModel = spec.Model <> ModelSource.Unspecified || Option.isSome modelOssys
        let action =
            match spec.Destination with
            | Destination.Folder dir ->
                match spec.Model with
                | ModelSource.ConfigFile c -> PlanAction.PublishBundle (c, dir, spec.Store, spec.Env)
                | _ when hasModel ->
                    match spec.Shape with
                    | Shape.Skeleton            -> PlanAction.EmitSkeleton (spec.Model, modelOssys, dir)
                    | Shape.Bundle | Shape.Ssdt -> PlanAction.EmitBundle (spec.Model, modelOssys, dir)
                | _ -> modelMissing "projection: "
            | Destination.Docker ->
                if hasModel then PlanAction.DeployDocker (spec.Model, modelOssys)
                else modelMissing "projection (docker): "
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
                    | DataOrigin.Synthetic profile when not schemaOnly ->
                        PlanAction.SynthesizeAndLoad (spec.Model, cfg.ModelOssys, profile, conn, opts, false)
                    | _ ->
                        if hasModel then PlanAction.PreviewSchema (spec.Model, modelOssys, conn, opts.Declaration)
                        else modelMissing "projection: "
                else
                    match spec.Data with
                    | DataOrigin.FromTarget alias when not schemaOnly ->
                        match dataConn alias with
                        | Error es -> PlanAction.Refused (6, List.head es)
                        | Ok src ->
                            if dataOnly then PlanAction.Transfer (src, conn, opts, true)
                            elif hasModel then PlanAction.MigrateWithData (spec.Model, modelOssys, conn, src, opts)
                            else modelMissing "projection: "
                    | DataOrigin.Synthetic profile when not schemaOnly ->
                        if dataOnly then PlanAction.SynthesizeAndLoad (spec.Model, cfg.ModelOssys, profile, conn, opts, true)
                        else PlanAction.Refused (2, err "cli.move.syntheticScope" "a synthetic load moves data only; point the flow at a data-granting target (grant: data).")
                    | _ when dataOnly ->
                        PlanAction.Refused (2, err "cli.move.scopeDataNoSource" "a DML-only load needs a data source (a flow whose `from` is a live environment).")
                    | _ ->
                        match spec.Model with
                        | ModelSource.ConfigFile c -> PlanAction.PublishAndLoad (c, conn, spec.Store, spec.Env)
                        | _ when hasModel -> PlanAction.Migrate (spec.Model, modelOssys, conn, opts)
                        | _ -> modelMissing "projection: "
        // The wipe-and-load strategy is honored only on the pure-transfer data
        // load (→ EmissionMode); any other action keeps the incremental MERGE,
        // so note it (no silent drop).
        let freshNote =
            match spec.Strategy, action with
            | Strategy.Merge, _ -> []
            | _, PlanAction.Transfer _ -> []
            | _, PlanAction.SynthesizeAndLoad _ -> []
            | _ -> [ "--fresh accepted; this action has no selectable data-load strategy (incremental applied)." ]
        // The resumable/idempotent envelope (G10) is honored on the pure-transfer
        // data leg only; any other action carries no resumable write seam, so the
        // flag is noted, never silently dropped (THE_VOICE no-silent-drop).
        let resumableNote =
            match spec.Resumable, action with
            | false, _ -> []
            | true, PlanAction.Transfer _ -> []
            | true, _ -> [ "--resumable accepted; this action has no resumable data-load seam (standard write applied)." ]
        { Notes = unhonoredNotes spec @ freshNote @ resumableNote; Action = action }

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
                // `--from empty` is the genesis-force keyword: A = ∅ against the
                // named `--store` (or, with no store, an empty-string store the
                // forced genesis never reads). It is NOT a model path, so it does
                // not route to the two-model `ExplainMigratePreview`.
                | Some toP, Some "empty", store ->
                    PlanAction.ExplainMigrateFromStore (defaultArg store "", toP, decl, true)
                | Some toP, Some fromP, _    -> PlanAction.ExplainMigratePreview (fromP, toP, decl)
                | Some toP, None, Some store -> PlanAction.ExplainMigrateFromStore (store, toP, decl, false)
                | _ -> PlanAction.Refused (2, err "cli.explain.migrateArgs" "projection explain migrate: needs --to <modelB> with --from <modelA> or --store <lifecycle>.")
            | sub :: _ when Map.containsKey sub cfg.Flows ->
                // explain <flow>: B = the flow's model, A_prior = the target store.
                let flow = Map.find sub cfg.Flows
                let decl = parseLossDeclaration args
                // `--from empty` forces genesis (A = ∅) for the flow preview too.
                let forceGenesis = (valueOf "--from" = Some "empty")
                match cfg.Model, (Map.tryFind flow.To cfg.Environments |> Option.bind (fun e -> e.Store)) with
                | Some model, Some store -> PlanAction.ExplainMigrateFromStore (store, model, decl, forceGenesis)
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

    /// M3.b — the recognized B→A reverse leg of a flow (`THE_DATA_PRODUCERS`
    /// LE-1): a `legacy` move whose live source bears the `logical` rendition (B,
    /// the migration-team-populated on-prem model) and whose live sink bears the
    /// `physical` rendition (A, the frozen OSUSR cloud model) of the ONE authored
    /// `SsKey` model. Carries the resolved source/sink connection specs; the
    /// engine face is `Transfer.runWithRenames` (the LE-2-proven capability) over
    /// the two RENDITIONS of the one model — once the rendering mechanism supplies
    /// those two contracts (the residual; see `reverseLegOf`).
    type ReverseLeg =
        {
            Flow       : Flow
            SourceConn : string
            SinkConn   : string
        }

    /// A `to` environment's reach → the `Destination` the engine lands at.
    let private destinationOfAccess (access: Access) : Destination =
        match access with
        | Access.Bundle out -> Destination.Folder out
        | Access.Direct r   -> Destination.Live r
        | Access.Docker     -> Destination.Docker

    /// M3.b (pure) — recognize a flow as a B→A reverse leg from the M1 `rendition`
    /// flag: `Some` exactly when the flow reads from a live `logical` source (B)
    /// and writes to a live `physical` sink (A) — the `legacy`/`preview` shape.
    /// This is the operator-facing FACE of the LE-2-proven engine capability: the
    /// rendition flag drives the classification (no flow re-tagging needed; a flow
    /// IS a reverse leg iff its endpoints' renditions say so). `None` for every
    /// other shape (same-rendition moves, model/synthetic sources, non-`Direct`
    /// endpoints) — those ride the established `resolveFlowSpec`/`planMovement`
    /// routing unchanged.
    ///
    /// NOTE (the residual, J3 / LE-1): recognizing the reverse leg and resolving
    /// its connections is clean and total here; what is NOT yet available is the
    /// pair of SsKey-aligned CONTRACTS `Transfer.runWithRenames` consumes (source
    /// = logical rendition, sink = physical rendition of the SAME model). ReadSide
    /// SsKeys are name-derived, so reading the two live DBs independently would
    /// NOT align them; the contracts must be RENDERED from one authored model in
    /// both renditions. That renderer does not exist yet — so this classifier is
    /// the landed partial, and the runner wiring waits on the rendering design
    /// (documented in `THE_DATA_PRODUCERS.md` §6 LE-1 + `CONFIRMED_BACKLOG` J3).
    let reverseLegOf (cfg: ProjectionConfig) (flow: Flow) : ReverseLeg option =
        let liveConnOf (envName: string) : (Environment * string) option =
            match Map.tryFind envName cfg.Environments with
            | Some env ->
                match env.Access with
                | Access.Direct r -> Some (env, connSpecOf r)
                | _ -> None
            | None -> None
        match flow.From with
        | FlowSource.Env sourceName ->
            match liveConnOf sourceName, liveConnOf flow.To with
            | Some (sourceEnv, sourceConn), Some (sinkEnv, sinkConn)
                  when sourceEnv.Rendition = Some Rendition.Logical
                       && sinkEnv.Rendition = Some Rendition.Physical ->
                Some { Flow = flow; SourceConn = sourceConn; SinkConn = sinkConn }
            | _ -> None
        | FlowSource.Model | FlowSource.Synthetic _ | FlowSource.NoData -> None

    /// A flow's `from` → the `DataOrigin`. A source environment must be
    /// `direct` (a live place to read rows from); the scheme-prefixed ref
    /// flows on as the transfer source `planMovement` resolves.
    let private dataOriginOfSource (cfg: ProjectionConfig) (source: FlowSource) : Result<DataOrigin> =
        match source with
        | FlowSource.Model       -> Result.success DataOrigin.Model
        | FlowSource.NoData      -> Result.success DataOrigin.NoData
        | FlowSource.Synthetic (Some profile) -> Result.success (DataOrigin.Synthetic profile)
        | FlowSource.Synthetic None ->
            Result.failureOf (err "cli.flow.syntheticNoProfile" "flow source `synthetic` needs a `profile` (e.g. \"profile\": \"file:legacy.profile.json\") — the evidence the generator replays.")
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
                        Tables   = flow.Tables
                        AllowDrops = opts.AllowDrops
                        AllowCdc = opts.AllowCdc
                        Resumable = opts.Resumable
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
                    // The declared table subset is honored on the data-transfer
                    // leg (golden data); on any other action it does not apply,
                    // so note it (no silent drop).
                    let tableNote =
                        match List.isEmpty flow.Tables, plan.Action with
                        | true, _ -> []
                        | false, PlanAction.Transfer _ -> []
                        | false, _ -> [ sprintf "flow tables (%s) apply to the data-transfer leg only; this action moves the full model." (String.concat ", " flow.Tables) ]
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

    /// Route a `profile` verb tail (THE_SYNTHETIC_DATA_DESIGN §2.2):
    /// `profile <env> --out <path>` captures the durable Profile artifact from
    /// a live environment. The env resolves to its live connection; the
    /// `--out` path is the durable file the synthetic flow later replays.
    let planProfile (cfg: ProjectionConfig) (args: string list) : ExecutionPlan =
        let valueOf = flagValue args
        // The env is positional-first (`profile <env> --out <path>`); a leading
        // flag means no env was named (avoids mistaking `--out`'s value for it).
        let envArg = match args with | first :: _ when not (first.StartsWith "--") -> Some first | _ -> None
        let action =
            match envArg with
            | None ->
                PlanAction.Refused (2, err "cli.profile.noEnv" "projection profile: name a source environment (profile <env> --out <path>).")
            | Some envRaw ->
                match valueOf "--out" with
                | None ->
                    PlanAction.Refused (2, err "cli.profile.noOut" "projection profile: requires --out <path> (the durable profile file to write).")
                | Some out ->
                    match resolveLiveConn cfg envRaw with
                    | Ok conn  -> PlanAction.CaptureProfile (conn, out)
                    | Error es -> PlanAction.Refused (6, List.head es)
        { Notes = []; Action = action }

    /// The closed secondary-verb set (THE_CLI.md §3). A first token outside
    /// this set is read as a flow name; an unknown one is refused, naming both.
    let private secondaryVerbs = set [ "check"; "explain"; "seal"; "report"; "profile"; "init"; "diff" ]

    /// Map an argv to an `Intent` (THE_CLI.md §3): the daily surface
    /// `projection <flow> [--go] [--fresh] [--allow-drops]` (the verb is
    /// implied), or one of the closed secondary verbs. Pure; the engine
    /// execution + the global-flag strip are the wiring slice.
    let parse (cfg: ProjectionConfig) (argv: string list) : Result<Intent> =
        match argv with
        | "check" :: rest   -> Result.success (Intent.Check rest)
        | "explain" :: rest -> Result.success (Intent.Explain rest)
        // `diff <a> <b>` — the top-level alias for `explain diff <a> <b>`: the
        // run-vs-run change surface promoted to a first-class verb. The tail
        // rides the SAME `planExplain` "diff" routing (→ `runDiff`), so the
        // alias is behavior-identical to the `explain diff` form.
        | "diff" :: rest    -> Result.success (Intent.Explain ("diff" :: rest))
        | "seal" :: rest    -> Result.success (Intent.Seal rest)
        | "report" :: rest  -> Result.success (Intent.Report rest)
        | "profile" :: rest -> Result.success (Intent.Profile rest)
        | first :: rest when Map.containsKey first cfg.Flows ->
            let opts =
                { Go         = List.contains "--go" rest
                  Fresh      = List.contains "--fresh" rest
                  AllowDrops = List.contains "--allow-drops" rest
                  AllowCdc   = List.contains "--allow-cdc" rest
                  Resumable  = List.contains "--resumable" rest }
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
            Result.failureOf (err "cli.verb.missing" "no flow or verb given; expected <flow> | check | explain | seal | report | profile.")

    /// The one pure routing for the whole surface — every `Intent` to its
    /// `ExecutionPlan`. The runner executes it; the totality test sweeps it.
    let plan (cfg: ProjectionConfig) (intent: Intent) : ExecutionPlan =
        match intent with
        | Intent.Flow (flow, opts) -> planFlow cfg flow opts
        | Intent.Check args        -> planCheck cfg args
        | Intent.Explain args      -> planExplain cfg args
        | Intent.Seal args         -> planSeal args
        | Intent.Report args       -> planReport cfg args
        | Intent.Profile args      -> planProfile cfg args
